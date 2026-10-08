using CluckWars.Abilities;
using CluckWars.Gameplay;
using CluckWars.Logging;
using CluckWars.Settings;
using CluckWars.UI;
using CluckWars.Visuals;
using UnityEngine;
using UnityEngine.UIElements;
using Zenject;

namespace CluckWars.Input
{
    /// <summary>
    /// UI Toolkit driver for the Game scene's on-screen touch controls
    /// (Stage 2b UI rebuild): a left virtual joystick + a right-hand MOBA arc of
    /// three hex ability buttons. Replaces the procedural-UGUI
    /// <c>TouchControlsHud</c>. Layout lives in <c>Assets/UI/TouchControls.uxml</c>;
    /// styling in <c>Assets/UI/Styles/TouchControls.uss</c>.
    /// </summary>
    /// <remarks>
    /// Preserves the exact input contract <see cref="TouchInputProvider"/> reads:
    /// <see cref="Movement"/> (Vector2, ~[-1,1] per axis, up = +y) and the three
    /// edge-triggered <c>AbilityNPressed</c> flags — each true for exactly one
    /// frame on press (cleared in <see cref="LateUpdate"/>), mirroring the old
    /// <c>HoldButton.WasPressedThisFrame</c> so <c>FusionNetworkService</c>'s
    /// per-frame latch still catches every tap on a non-tick frame.
    ///
    /// v0.6 hold-to-aim (FEEDBACK.md §2, Stage 2b): each hex now also captures its
    /// pointer on down (mirroring the joystick's own <c>CapturePointer</c>/
    /// <c>ReleasePointer</c> pattern below) and tracks a level-triggered
    /// <see cref="IsAbilityHeld"/> per slot, cleared cleanly on release/capture-loss.
    /// A hold whose finger is released inside the edge band (within 20 dp of a physical screen
    /// edge; <see cref="HoldCancelRules"/>), or whose touch is lost (PointerCancel / capture-out),
    /// sets a one-shot cancel flag
    /// (<see cref="ConsumeAbilityCancelled"/>) instead of clearing the hold silently,
    /// so <c>TouchInputProvider</c> can tell "released to fire" apart from "cancelled"
    /// — both look identical as a plain hold-bit falling edge
    /// otherwise, and the two must fire opposite outcomes on the network.
    ///
    /// Per-slot visuals mirror the old <c>TouchControlsHud.Update()</c>: accent
    /// tint from the equipped <see cref="AbilityBaseSO.AccentColor"/>, a bottom-up
    /// cooldown clip (ART §6.6 layer 6), slot-3 hidden unless the Combo (Assassin)
    /// slot is equipped, icon sprite + short label + slot badge + cooldown-seconds
    /// number. The local chicken is resolved the same way the old HUD did
    /// (input-authority scan). Self-inject falls back to the SceneContext first per
    /// CONVENTIONS IP-fix1.
    ///
    /// <b>v0.6 Stage 5 — Aftermath / refusal HUD (FEEDBACK.md §5.2 + §6, cases
    /// 17–19 and 24–29).</b> Everything Stage 5 needs lives in this one document, on
    /// purpose: §5.2's whole point is that a status has to be shown <i>where it
    /// bites</i>, and both things it bites (the ability hexes and the move stick)
    /// already live here. No second UIDocument, no second controller, no new scene
    /// wiring. Three additions, none of them networked and none of them a new ART
    /// §6.6 layer:
    /// <list type="number">
    ///   <item><b>Refusal treatment on the hexes.</b> One live
    ///   <see cref="AbilityRefusal"/> per slot, read from
    ///   <see cref="AbilityController.EvaluateRefusal"/> — the same precedence table
    ///   <c>TryActivate</c> itself refuses through, so the HUD can never disagree
    ///   with gameplay about <i>why</i> a button is dead. Because the reasons are
    ///   mutually exclusive, ART §6.6's layer 9 generalises from "cooldown seconds"
    ///   to "state indicator" and carries whichever reason won, and layer 6's clip
    ///   mechanism is reused a second time (anchored top, accent-tinted) for the
    ///   ability that is actually running.</item>
    ///   <item><b>Denied-press bump.</b> <see cref="AbilityController.TryConsumeDeniedPress"/>
    ///   is polled every frame; a refused press shakes its own hex. A press is
    ///   never absorbed silently (§6).</item>
    ///   <item><b>Control-consequence marking + status strip.</b> The move stick is
    ///   greyed / shackled / tinted from the local chicken's
    ///   <see cref="ChickenController.CurrentControlState"/>, and a strip above it
    ///   lists every simultaneously-active status with a draining bar. The stick
    ///   stays draggable in every state — <c>ControlRules.CanMove</c> is enforced in
    ///   <c>ChickenController</c>, and a HUD that also swallowed the input would
    ///   double-gate it and hide input bugs.</item>
    /// </list>
    /// All of it is read off already-<c>[Networked]</c> state on the local chicken.
    /// No RPCs, no new networked properties, nothing written back to gameplay.
    /// </remarks>
    [RequireComponent(typeof(UIDocument))]
    public sealed class TouchControlsController : MonoBehaviour
    {
        private const string Source = "TouchHud";

        public static TouchControlsController Instance { get; private set; }

        // Ability -> exported-icon USS class comes from the shared
        // CluckWars.UI.AbilityIconStyle (single source of truth, also used by the
        // character-select / lobby menus). Refusal / control-state USS classes and
        // the status glyphs come from CluckWars.UI.HudFeedbackStyle for the same
        // reason — the world-space badges read the same glyph consts.

        // Joystick geometry (reference px; mirrors the old UGUI tunables).
        private const float JoyMaxRadius = 108f; // joystickRadius 140 - knobRadius 64 * 0.5

        /// <summary>Sentinel forcing the first refusal/state write through the
        /// "only write what changed" guards below.</summary>
        private const AbilityRefusal UnsetRefusal = (AbilityRefusal)byte.MaxValue;
        private const ControlState   UnsetControl = (ControlState)byte.MaxValue;

        // ---- Injected services -------------------------------------------------
        private ILogService   _log;
        private readonly System.Collections.Generic.HashSet<string> _reportedMissingIcons = new();

        // ---- UI element refs (queried once, on bind) ---------------------------
        private VisualElement _root;
        private SafeAreaPadding _safeArea;   // keeps the stick and the hexes out of a notch / cutout / gesture bar
        private bool          _bound;

        private VisualElement _joyBase;
        private VisualElement _joyKnob;
        private Label         _joyGlyph;      // ⛓ while rooted
        private Label         _joySlowLabel;  // ×0.45 while slowed
        private int           _joyPointerId = -1;

        /// <summary>Per-slot driven element refs for one hex ability button.</summary>
        private struct HexSlot
        {
            public VisualElement Hex;        // the hit target; its sprite is the INK RIM (cream while held), scaled/dimmed while held
            public VisualElement Fill;       // inset hex sprite tinted per refusal state (ART §6.6 layers 2–4); the rim shows around it
            public VisualElement Cooldown;   // layer 6: bottom-up black clip (height % = remaining/total)
            public VisualElement Drain;      // layer 6 mirrored: top-down accent clip while this slot is running
            public VisualElement DrainFill;  // accent-tinted silhouette inside the drain clip
            public VisualElement Icon;       // layer 7: sprite swapped via .cw-hex-icon--* class
            public Label         Label;      // layer 7: short label under the icon
            public Label         CdNum;      // layer 9: seconds remaining
            public VisualElement StateGlyph; // layer 9: drawn refusal mark (⃠ / ✕)
            public VisualElement StateBarA;  // one arm of the ✕
            public VisualElement StateBarB;  // the other arm of the ✕
            public VisualElement Mark;       // category shape (CategoryMark): the category without colour
            public VisualElement Pip;        // in-range pip: a rival is in this move's shape (bottom-centre)
            public HexDurationRing Ring;     // duration ring while this slot's ability is running
            public VisualElement CancelX;    // built in code: the cream X shown while the edge-band cancel is armed
            public VisualElement FizzleSlash;// built in code: the 0.3 s slash flash after a fizzle
            public VisualElement Puff;       // built in code: the Fx_Whiff puff after a fizzle
        }
        /// <summary>Mirrors <see cref="Gameplay.AbilityController.SlotCount"/> so the hex
        /// cluster and the gameplay slot count cannot drift apart.</summary>
        private const int SlotCount = Gameplay.AbilityController.SlotCount;

        private readonly HexSlot[]        _slots           = new HexSlot[SlotCount];
        private readonly AbilityBaseSO[]  _appliedSlots    = new AbilityBaseSO[SlotCount];
        private readonly string[]         _appliedIconCls  = new string[SlotCount];

        // Refusal state, cached so class/colour writes only happen on a real change.
        private readonly AbilityRefusal[] _appliedRefusal    = new AbilityRefusal[SlotCount];
        private readonly string[]         _appliedRefusalCls = new string[SlotCount];
        private readonly int[]            _appliedCdSeconds  = new int[SlotCount];

        // Denied-press bump timers (§6). -1 = idle.
        private readonly float[] _bumpTimer = new float[SlotCount];

        // Phase 6 chunk 2: fizzle flash timers (seconds into the 0.3 s flash; -1 = idle) and the touch edge-band cancel.
        private readonly float[] _fizzleTimer  = new float[SlotCount] { -1f, -1f, -1f, -1f };
        private readonly bool[]  _cancelArmed  = new bool[SlotCount];
        private readonly ScreenEdge[] _armedEdge = new ScreenEdge[SlotCount];
        private readonly bool[]  _appliedCancelX = new bool[SlotCount];
        private ScreenEdge _appliedGlowEdge;
        private VisualElement _glowLeft, _glowRight, _glowTop, _glowBottom;

        // Phase 6 (A1) hex states, cached so style writes only happen on a real change.
        private readonly bool[]  _appliedHeld = new bool[SlotCount];
        private readonly bool[]  _appliedDim  = new bool[SlotCount];
        private readonly bool[]  _pipShown    = new bool[SlotCount];
        private readonly float[] _pipPopTimer = new float[SlotCount]; // seconds into the 0.2 s pop; -1 = idle
        private readonly bool[]  _ringShown   = new bool[SlotCount];
        private AbilitySlotOverlay _localOverlay;   // the ONE source of the "hot" (rival in shape) fact
        private ChickenController  _overlayFor;
        private bool _reportedMissingOverlay;

        // ---- Status strip refs -------------------------------------------------
        private struct StatusRowRefs
        {
            public VisualElement Root;
            public Label         Glyph;
            public Label         Name;
            public Label         Value;
            public VisualElement Bar;
            public VisualElement BarFill;
        }
        private VisualElement   _statusStrip;
        private StatusRowRefs[] _statusRows;
        private HudStatusRow[]  _statusScratch;
        private HudStatusKind[] _appliedRowKind;
        private int[]           _appliedRowQuantity;

        /// <summary>
        /// Peak <c>StunRemaining</c>/<c>RootRemaining</c> observed since the status
        /// appeared, indexed by <see cref="HudStatusKind"/>. The bar needs the status's
        /// <i>original</i> duration to draw remaining/total, and neither
        /// <c>ChickenController</c> nor the <c>TickTimer</c>s behind those properties
        /// expose it — the timer only knows when it ends. Caching the peak locally is
        /// exact from the first frame the status is observed (the countdown is monotonically
        /// decreasing, so the first sample <i>is</i> the total) and is a purely local
        /// visual concern: adding a networked "original duration" field to carry a bar
        /// width would put presentation data on the wire for no gameplay reason.
        /// Re-applied stuns/roots refresh the peak upward on the frame the timer jumps.
        /// </summary>
        private readonly float[] _statusPeak = new float[4];

        // Control-state marking on the stick.
        private ControlState _appliedJoyState = UnsetControl;
        private string       _appliedJoyCls;
        private int          _appliedSlowQuantity = int.MinValue;

        // Shared 5 Hz gate for every countdown/magnitude string in this document —
        // FeedbackTuning.BadgeCountdownTextUpdateHz exists because UI Toolkit text
        // writes trigger layout, so they explicitly do NOT run per-frame. Bar widths
        // are not gated: a width write is a cheap style value with no relayout.
        private float _nextTextUpdate;
        private bool  _textGateOpen;

        // Edge-triggered press flags (one-frame, cleared in LateUpdate).
        private readonly bool[] _pressed = new bool[SlotCount];

        // v0.6 hold-to-aim: level-triggered per-slot hold state + which pointer owns it.
        private readonly bool[] _held          = new bool[SlotCount];
        private readonly int[]  _heldPointerId = new int[SlotCount] { -1, -1, -1, -1 };
        // One-shot "this hold was dragged off, don't let it fire" flag — consumed by
        // TouchInputProvider.GetAbilityCancelPressed via ConsumeAbilityCancelled.
        private readonly bool[] _cancelled     = new bool[SlotCount];

        // ---- Runtime state -----------------------------------------------------
        private Vector2           _movement;
        private ChickenController _localChicken;
        private float             _localChickenLastSearch;

        // ---- Input contract (read by TouchInputProvider) -----------------------
        public Vector2 Movement       => _movement;
        public bool    Ability1Pressed => _pressed[0];
        public bool    Ability2Pressed => _pressed[1];
        public bool    Ability3Pressed => _pressed[2];
        public bool    Ability4Pressed => _pressed[3];

        /// <summary>Level-triggered: true while slot's hex is held down and the hold
        /// hasn't been drag-cancelled. Safe to read every frame.</summary>
        public bool IsAbilityHeld(int slot) => slot >= 0 && slot < SlotCount && _held[slot];

        /// <summary>Level-triggered: a held hex's finger is inside the edge band, so releasing now cancels.
        /// Read by <see cref="TouchInputProvider.IsAbilityCancelArmed"/> for the local preview.</summary>
        public bool IsCancelArmed
        {
            get
            {
                for (int i = 0; i < SlotCount; i++)
                    if (_held[i] && _cancelArmed[i]) return true;
                return false;
            }
        }

        /// <summary>Returns (and clears) whether <paramref name="slot"/>'s hold was
        /// just cancelled — the "released to fire" vs "dragged off to cancel"
        /// distinction <see cref="TouchInputProvider"/> needs (see class remarks).</summary>
        public bool ConsumeAbilityCancelled(int slot)
        {
            if (slot < 0 || slot >= SlotCount || !_cancelled[slot]) return false;
            _cancelled[slot] = false;
            return true;
        }

        [Inject]
        public void Construct(ILogService log)
        {
            _log = log;
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;

            int rows = Mathf.Max(1, FeedbackTuning.StatusBadgeMaxRows);
            _statusRows         = new StatusRowRefs[rows];
            _statusScratch      = new HudStatusRow[rows];
            _appliedRowKind     = new HudStatusKind[rows];
            _appliedRowQuantity = new int[rows];

            for (int i = 0; i < SlotCount; i++)
            {
                _appliedRefusal[i]   = UnsetRefusal;
                _appliedCdSeconds[i] = int.MinValue;
                _bumpTimer[i]        = -1f;
                _pipPopTimer[i]      = -1f;
            }

            if (_log == null) ProjectContext.Instance.Container.Inject(this);
        }

        private void OnEnable() => TryBind();

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        // ---- Bind --------------------------------------------------------------

        private void TryBind()
        {
            var doc = GetComponent<UIDocument>();
            _root = doc != null ? doc.rootVisualElement : null;
            if (_root == null) return;

            // Each cluster lives in a full-screen zone that the safe area insets (round-2 findings 5 + 15).
            var joyZone = _root.Q<VisualElement>("JoystickRoot");
            var abilityZone = _root.Q<VisualElement>("AbilityRoot");
            if (joyZone == null || abilityZone == null)
                _log?.Error(Source, "TouchControls.uxml is missing #JoystickRoot or #AbilityRoot: the touch controls ignore the safe area.");
            _safeArea = new SafeAreaPadding(SafeAreaPadding.Edge.Offsets, joyZone, abilityZone);

            _joyBase      = _root.Q<VisualElement>("JoystickBase");
            _joyKnob      = _root.Q<VisualElement>("JoystickKnob");
            _joyGlyph     = _root.Q<Label>("JoyStateGlyph");
            _joySlowLabel = _root.Q<Label>("JoySlowLabel");
            if (_joyBase != null)
            {
                _joyBase.RegisterCallback<PointerDownEvent>(OnJoyDown);
                _joyBase.RegisterCallback<PointerMoveEvent>(OnJoyMove);
                _joyBase.RegisterCallback<PointerUpEvent>(OnJoyUp);
                _joyBase.RegisterCallback<PointerCaptureOutEvent>(OnJoyCaptureOut);
            }

            _statusStrip = _root.Q<VisualElement>("StatusStrip");
            for (int i = 0; i < _statusRows.Length; i++)
            {
                _statusRows[i] = new StatusRowRefs
                {
                    Root    = _root.Q<VisualElement>($"StatusRow{i}"),
                    Glyph   = _root.Q<Label>($"StatusGlyph{i}"),
                    Name    = _root.Q<Label>($"StatusName{i}"),
                    Value   = _root.Q<Label>($"StatusValue{i}"),
                    Bar     = _root.Q<VisualElement>($"StatusBar{i}"),
                    BarFill = _root.Q<VisualElement>($"StatusBarFill{i}"),
                };
                _appliedRowKind[i]     = HudStatusKind.None;
                _appliedRowQuantity[i] = int.MinValue;
            }

            for (int i = 0; i < SlotCount; i++)
            {
                int n = i + 1;
                var hex = _root.Q<VisualElement>($"Ability{n}");
                _slots[i] = new HexSlot
                {
                    Hex        = hex,
                    Cooldown   = _root.Q<VisualElement>($"Cooldown{n}"),
                    Drain      = _root.Q<VisualElement>($"ActiveDrain{n}"),
                    DrainFill  = _root.Q<VisualElement>($"ActiveDrainFill{n}"),
                    Icon       = _root.Q<VisualElement>($"Icon{n}"),
                    Label      = _root.Q<Label>($"Label{n}"),
                    CdNum      = _root.Q<Label>($"CooldownNum{n}"),
                    StateGlyph = _root.Q<VisualElement>($"StateGlyph{n}"),
                    StateBarA  = _root.Q<VisualElement>($"StateBarA{n}"),
                    StateBarB  = _root.Q<VisualElement>($"StateBarB{n}"),
                    Fill       = _root.Q<VisualElement>($"Fill{n}"),
                    Pip        = _root.Q<VisualElement>($"Pip{n}"),
                    Mark       = hex != null ? CategoryMark.Create() : null,
                    Ring       = hex != null ? new HexDurationRing() : null,
                };
                if (hex != null)
                {
                    hex.Add(_slots[i].Mark);
                    hex.Add(_slots[i].Ring);
                    _slots[i].Ring.style.display = DisplayStyle.None;
                    BuildFlashElements(ref _slots[i], hex);
                    if (_slots[i].Fill == null || _slots[i].Pip == null)
                        _log?.Error(Source, $"TouchControls.uxml is missing #Fill{n} or #Pip{n}: that hex draws without its rim / in-range pip.");

                    // Held hex scales up and the others dim with a 60 ms ease-out (A1). Scale/opacity are
                    // written inline from FeedbackTuning; only the transition is declared here, once.
                    hex.style.transitionProperty = new System.Collections.Generic.List<StylePropertyName>
                        { new StylePropertyName("scale"), new StylePropertyName("opacity") };
                    hex.style.transitionDuration = new System.Collections.Generic.List<TimeValue>
                        { new TimeValue(FeedbackTuning.HexHeldTransitionSeconds, TimeUnit.Second) };
                    hex.style.transitionTimingFunction = new System.Collections.Generic.List<EasingFunction>
                        { new EasingFunction(EasingMode.EaseOut) };
                }
                if (hex != null)
                {
                    int slot = i; // capture
                    hex.RegisterCallback<PointerDownEvent>(evt => OnHexDown(slot, evt));
                    // PointerMoveEvent drives the edge-band cancel arming (HoldCancelRules).
                    hex.RegisterCallback<PointerMoveEvent>(evt => OnHexMove(slot, evt));
                    hex.RegisterCallback<PointerUpEvent>(evt => OnHexUp(slot, evt));
                    hex.RegisterCallback<PointerLeaveEvent>(evt => OnHexLeave(slot, evt));
                    hex.RegisterCallback<PointerCaptureOutEvent>(evt => OnHexCaptureOut(slot, evt));
                    // The OS taking the touch (system edge gesture, palm rejection) must cancel, never fire.
                    hex.RegisterCallback<PointerCancelEvent>(evt => OnHexPointerCancel(slot, evt));
                }
            }

            _glowLeft   = _root.Q<VisualElement>("EdgeGlowLeft");
            _glowRight  = _root.Q<VisualElement>("EdgeGlowRight");
            _glowTop    = _root.Q<VisualElement>("EdgeGlowTop");
            _glowBottom = _root.Q<VisualElement>("EdgeGlowBottom");
            if (_glowLeft == null || _glowRight == null || _glowTop == null || _glowBottom == null)
                _log?.Error(Source, "TouchControls.uxml is missing an #EdgeGlow* strip: the cancel band will not show which edge cancels.");

            _bound = true;
            _log?.Info(Source, $"Bound UITK touch controls: joystick + {SlotCount} hex ability buttons + status strip.");
        }

        // ---- Joystick pointer handling ----------------------------------------

        private void OnJoyDown(PointerDownEvent evt)
        {
            _joyPointerId = evt.pointerId;
            _joyBase.CapturePointer(evt.pointerId);
            UpdateJoyFrom(evt.localPosition);
            evt.StopPropagation();
        }

        private void OnJoyMove(PointerMoveEvent evt)
        {
            if (_joyPointerId != evt.pointerId || !_joyBase.HasPointerCapture(evt.pointerId)) return;
            UpdateJoyFrom(evt.localPosition);
        }

        private void OnJoyUp(PointerUpEvent evt)
        {
            if (_joyBase.HasPointerCapture(evt.pointerId)) _joyBase.ReleasePointer(evt.pointerId);
            ResetJoy();
        }

        private void OnJoyCaptureOut(PointerCaptureOutEvent evt) => ResetJoy();

        private void UpdateJoyFrom(Vector3 localPos)
        {
            var center  = _joyBase.contentRect.center;
            var delta   = new Vector2(localPos.x - center.x, localPos.y - center.y);
            var clamped = Vector2.ClampMagnitude(delta, JoyMaxRadius);

            // Knob follows the finger in UITK space (y down = positive).
            if (_joyKnob != null) _joyKnob.style.translate = new Translate(clamped.x, clamped.y, 0f);

            // Movement: normalize + flip y so up = +y (game convention).
            _movement = new Vector2(clamped.x, -clamped.y) / JoyMaxRadius;
        }

        private void ResetJoy()
        {
            _joyPointerId = -1;
            _movement = Vector2.zero;
            if (_joyKnob != null) _joyKnob.style.translate = new Translate(0f, 0f, 0f);
        }

        // ---- Hex press / hold / drag-cancel ------------------------------------

        private void OnHexDown(int slot, PointerDownEvent evt)
        {
            _pressed[slot] = true;
            _held[slot] = true;
            _cancelled[slot] = false;
            _cancelArmed[slot] = false;
            _armedEdge[slot] = ScreenEdge.None;
            _heldPointerId[slot] = evt.pointerId;
            _slots[slot].Hex?.CapturePointer(evt.pointerId);
            evt.StopPropagation();
            RefreshHeldVisuals(); // the press frame, locally: not after the networked charge
        }

        /// <summary>
        /// Edge-band cancel (Phase 6, A4): while held, the pointer's RAW screen position (the panel position
        /// converted back to screen pixels, then to dp) arms the cancel within 20 dp of any physical screen edge
        /// and disarms beyond 28 dp (<see cref="HoldCancelRules"/>). Nothing is decided here beyond that: the
        /// release is what cancels (<see cref="EndHexPointer"/>). There is deliberately no radial drag-off any
        /// more; a thumb that merely drifts never cancels.
        /// </summary>
        private void OnHexMove(int slot, PointerMoveEvent evt)
        {
            if (!_held[slot] || _heldPointerId[slot] != evt.pointerId) return;
            if (!TryPanelToScreen(evt.position, out var screen)) return;

            bool armed = HoldCancelRules.Evaluate(_cancelArmed[slot], screen, Screen.width, Screen.height,
                                                  Screen.dpi, out var edge);
            SetCancelArmed(slot, armed, edge);
        }

        /// <summary>Panel-space position (origin top-left) to physical screen pixels (origin top-left). The panel
        /// scales with the screen (reference 1920x1080), so the ratio of the two sizes is the whole conversion.</summary>
        private bool TryPanelToScreen(Vector3 panelPos, out Vector2 screen)
        {
            screen = default;
            var size = _root != null ? _root.worldBound.size : Vector2.zero;
            if (size.x <= 1f || size.y <= 1f) return false;
            screen = new Vector2(panelPos.x / size.x * Screen.width, panelPos.y / size.y * Screen.height);
            return true;
        }

        private void SetCancelArmed(int slot, bool armed, ScreenEdge edge)
        {
            bool wasArmed = _cancelArmed[slot];
            _cancelArmed[slot] = armed;
            _armedEdge[slot] = armed ? edge : ScreenEdge.None;
            if (armed && !wasArmed) OnCancelArmed(slot);
        }

        /// <summary>
        /// The moment the edge-band cancel arms. Phase 6 chunk 6 plays the 10-20 ms haptic tick from here
        /// (gated by the "Buzz When Hit" toggle); there is intentionally no vibration code yet.
        /// </summary>
        private void OnCancelArmed(int slot) { }

        private void OnHexUp(int slot, PointerUpEvent evt)
        {
            if (_heldPointerId[slot] != evt.pointerId) return;
            EndHexPointer(slot, HexPointerEnd.Up, evt.pointerId);
        }

        /// <summary>
        /// Registered per the Stage 2 spec, but deliberately NOT a cancel trigger: the finger legitimately
        /// leaves the hex's visual bounds while aiming, and the captured pointer keeps delivering move / up
        /// events regardless. The edge band is the only cancel gesture.
        /// </summary>
        private void OnHexLeave(int slot, PointerLeaveEvent evt) { }

        /// <summary>Pointer capture lost abnormally (OS gesture takeover, multi-touch
        /// conflict). Treated as a cancel, not a release-to-fire: an involuntary loss
        /// of tracking is not a deliberate "let go to cast" gesture.</summary>
        private void OnHexCaptureOut(int slot, PointerCaptureOutEvent evt)
        {
            if (!_held[slot]) return;
            EndHexPointer(slot, HexPointerEnd.CaptureOut, -1);
        }

        /// <summary>The touch was cancelled by the system (edge swipe, palm rejection). Never fires.</summary>
        private void OnHexPointerCancel(int slot, PointerCancelEvent evt)
        {
            if (!_held[slot] || _heldPointerId[slot] != evt.pointerId) return;
            EndHexPointer(slot, HexPointerEnd.Cancel, evt.pointerId);
        }

        /// <summary>
        /// The single place a hex interaction ends. State is settled BEFORE the pointer is released, so the
        /// capture-out event that release raises finds the slot already idle. A lift fires unless the edge band is
        /// armed; PointerCancel and a lost capture always cancel (<see cref="HoldCancelRules.ResolveRelease"/>).
        /// </summary>
        private void EndHexPointer(int slot, HexPointerEnd end, int pointerId)
        {
            var release = HoldCancelRules.ResolveRelease(end, _cancelArmed[slot]);
            _held[slot] = false;
            _heldPointerId[slot] = -1;
            if (release == HexRelease.Cancel) _cancelled[slot] = true;
            SetCancelArmed(slot, false, ScreenEdge.None);

            var hex = _slots[slot].Hex;
            if (pointerId >= 0 && hex != null && hex.HasPointerCapture(pointerId)) hex.ReleasePointer(pointerId);
            RefreshHeldVisuals();
        }

        private void LateUpdate()
        {
            // Clear the press edges after the frame so callers (and the network
            // latch) read each tap exactly once — matches HoldButton timing.
            // Loop, never an unrolled list: this line was the ONE hardcoded 3 left after the
            // four-slot generalisation, and it meant _pressed[3] was set on the first tap of
            // the fourth hex and never cleared — GetAbility4Pressed() then reported a press
            // on every tick for the rest of the session.
            for (int i = 0; i < SlotCount; i++) _pressed[i] = false;
        }

        // ---- Per-frame visual refresh -----------------------------------------

        private void Update()
        {
            if (!_bound)
            {
                TryBind();
                if (!_bound) return;
            }
            _safeArea?.Apply();   // two struct compares a frame unless the safe area moved

            if (_localChicken == null || _localChicken.Object == null || !_localChicken.Object.IsValid)
            {
                if (Time.unscaledTime - _localChickenLastSearch > 0.5f)
                {
                    _localChickenLastSearch = Time.unscaledTime;
                    _localChicken = FindLocalChicken();
                }
            }

            ResolveLocalOverlay();

            // One 5 Hz gate shared by every string this document writes.
            _textGateOpen = Time.unscaledTime >= _nextTextUpdate;
            if (_textGateOpen)
                _nextTextUpdate = Time.unscaledTime + 1f / Mathf.Max(1f, FeedbackTuning.BadgeCountdownTextUpdateHz);

            var abilities = _localChicken != null ? _localChicken.Abilities : null;

            PollDeniedPress(abilities);
            RefreshHeldVisuals();

            for (int slot = 0; slot < SlotCount; slot++)
                RefreshSlot(slot, abilities);

            RefreshControlConsequences();
        }

        /// <summary>Caches the local chicken's <see cref="AbilitySlotOverlay"/> (the source of the "hot" verdict
        /// the pips read). Warns once if the local chicken has none — otherwise the pips are silently dead.</summary>
        private void ResolveLocalOverlay()
        {
            if (_localChicken == _overlayFor) return;
            _overlayFor = _localChicken;
            _localOverlay = _localChicken != null ? _localChicken.GetComponent<AbilitySlotOverlay>() : null;
            if (_localChicken != null && _localOverlay == null && !_reportedMissingOverlay)
            {
                _reportedMissingOverlay = true;
                _log?.Warn(Source, "Local chicken has no AbilitySlotOverlay: the in-range pips will never light.");
            }
        }

        /// <summary>
        /// Held / aiming state of the cluster (Phase 6, A1), driven by the LOCAL pointer state so it answers on
        /// the press frame: the held hex scales to <see cref="FeedbackTuning.HexHeldScale"/> with a bright
        /// (cream) rim, every other hex dims to <see cref="FeedbackTuning.HexOthersWhileHeldAlpha"/>. The
        /// transition is the 60 ms ease-out declared at bind.
        /// </summary>
        private void RefreshHeldVisuals()
        {
            bool any = false;
            for (int i = 0; i < SlotCount; i++) any |= _held[i];

            for (int slot = 0; slot < SlotCount; slot++)
            {
                var hex = _slots[slot].Hex;
                if (hex == null) continue;

                bool held = _held[slot];
                if (held != _appliedHeld[slot])
                {
                    _appliedHeld[slot] = held;
                    hex.EnableInClassList(HeldClass, held);
                    float k = held ? FeedbackTuning.HexHeldScale : 1f;
                    hex.style.scale = new Scale(new Vector3(k, k, 1f));
                }

                bool dim = any && !held;
                if (dim != _appliedDim[slot])
                {
                    _appliedDim[slot] = dim;
                    hex.style.opacity = dim ? FeedbackTuning.HexOthersWhileHeldAlpha : 1f;
                }

                bool showX = held && _cancelArmed[slot];
                if (showX != _appliedCancelX[slot])
                {
                    _appliedCancelX[slot] = showX;
                    if (_slots[slot].CancelX != null)
                        _slots[slot].CancelX.style.display = showX ? DisplayStyle.Flex : DisplayStyle.None;
                }
            }

            RefreshEdgeGlow();
        }

        /// <summary>Shows the glow strip along the screen edge a held, armed finger is approaching (and only that one).</summary>
        private void RefreshEdgeGlow()
        {
            var edge = ScreenEdge.None;
            for (int i = 0; i < SlotCount; i++)
                if (_held[i] && _cancelArmed[i]) { edge = _armedEdge[i]; break; }

            if (edge == _appliedGlowEdge) return;
            _appliedGlowEdge = edge;
            SetGlow(_glowLeft,   edge == ScreenEdge.Left);
            SetGlow(_glowRight,  edge == ScreenEdge.Right);
            SetGlow(_glowTop,    edge == ScreenEdge.Top);
            SetGlow(_glowBottom, edge == ScreenEdge.Bottom);
        }

        private static void SetGlow(VisualElement strip, bool on)
        {
            if (strip != null) strip.style.display = on ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private const string HeldClass = "cw-hex--held";

        private void RefreshSlot(int slot, AbilityController abilities)
        {
            var refs = _slots[slot];
            if (refs.Hex == null) return;

            AbilityBaseSO equipped = abilities != null ? SlotAbility(abilities, slot) : null;

            // Swap icon sprite + short label only when the equipped SO changes.
            if (equipped != _appliedSlots[slot])
            {
                _appliedSlots[slot] = equipped;
                ApplyEquipped(slot, equipped);
            }

            // Slot 3 (Combo) is only meaningful for Assassin — hide when empty. This is
            // also the whole of the SlotUnavailable refusal treatment (§6 case 28): ART
            // §6.6's slot-3 visibility rule already removes the hex from the layout, so
            // there is nothing left to mark.
            if (slot == 2)
            {
                var display = equipped != null ? DisplayStyle.Flex : DisplayStyle.None;
                if (refs.Hex.style.display != display) refs.Hex.style.display = display;
            }

            float remaining = 0f, total = 0f;
            if (equipped != null)
            {
                total = equipped.Cooldown;
                if (total > 0f) remaining = abilities.CooldownRemaining(slot);
            }

            float pct = total > 0f ? Mathf.Clamp01(remaining / total) : 0f;

            // The one live reason this slot would refuse to fire. Comes from the same
            // precedence table TryActivate refuses through (AbilityRefusalRules.Evaluate),
            // so the button and the gameplay gate can never tell different stories.
            AbilityRefusal refusal = (abilities != null && equipped != null)
                ? abilities.EvaluateRefusal(slot)
                : AbilityRefusal.None;

            ApplyRefusalState(slot, refusal);

            // ART §6.6 layer 6 — black, bottom-up: "this slot is cooling".
            if (refs.Cooldown != null)
                refs.Cooldown.style.height = new Length(pct * 100f, LengthUnit.Percent);

            // ART §6.6 layer 6 again, mirrored — accent, top-down: "this slot is
            // RUNNING". Opposite direction and opposite colour from the cooldown clip on
            // purpose (§6 case 27): the two states routinely coexist on the same hex,
            // because an ability burns its cooldown at activation.
            float active01 = (abilities != null && equipped != null)
                ? abilities.ActiveRemaining01For(slot)
                : 0f;
            if (refs.Drain != null)
                refs.Drain.style.height = new Length(active01 * 100f, LengthUnit.Percent);

            // Phase 6 (A1): while the ability runs, a duration ring around the hex rim drains with it.
            bool ringOn = active01 > 0f;
            if (refs.Ring != null)
            {
                if (ringOn != _ringShown[slot])
                {
                    _ringShown[slot] = ringOn;
                    refs.Ring.style.display = ringOn ? DisplayStyle.Flex : DisplayStyle.None;
                }
                if (ringOn) refs.Ring.SetFraction(active01);
            }

            RefreshPip(slot, equipped, refusal);

            // The menu's category colour (round-2 finding 5): a move reads the same job in the deck and
            // on the button. AccentColor stays the VFX colour.
            Color accent = equipped != null ? AbilityPalette.HexColor(equipped) : AbilityPalette.EmptyHex;

            if (refs.DrainFill != null && active01 > 0f)
            {
                // Full-strength category colour: the running slot reads bright; the cluster is not dimmed
                // while abilities run (Phase 6 chunk 3).
                var drainTint = accent;
                drainTint.a = FeedbackTuning.HexAlphaReady;
                refs.DrainFill.style.unityBackgroundImageTintColor = drainTint;
            }

            // ART §6.6 layers 2–4. Exactly one refusal applies at a time, so the whole
            // table is a flat switch; every alpha and colour is named in FeedbackTuning.
            Color tint;
            switch (refusal)
            {
                case AbilityRefusal.Cooldown:
                    tint = accent;
                    tint.a = FeedbackTuning.HexAlphaCooldown;
                    break;
                case AbilityRefusal.Stunned:
                    // The illegal-cast wash, the same one the telegraph uses when a hold
                    // goes illegal mid-aim — being stunned and having your aim invalidated
                    // are the same story told at two moments.
                    tint = FeedbackTuning.IllegalCastTintColor;
                    break;
                default:
                    // Ready, and "no target in range" too (Phase 6, A1): nobody around is not a refusal, so
                    // the hex rests at its full category hue, opaque. The in-range pip says when someone is.
                    tint = accent;
                    tint.a = FeedbackTuning.HexAlphaReady;
                    break;
            }
            (refs.Fill ?? refs.Hex).style.unityBackgroundImageTintColor = tint;

            // Layer 9. The seconds number is only one of the marks this layer can carry
            // now; the others are drawn shapes toggled by the refusal class in USS.
            RefreshCenterMark(slot, refusal, remaining);
        }

        /// <summary>
        /// The in-range pip (Phase 6, A1): lit while a rival is inside this slot's shape and the slot is not
        /// cooling down (<see cref="AbilityPreviewRules.InRangePipVisible"/>). "Hot" comes from the local
        /// chicken's <see cref="AbilitySlotOverlay"/> — the same verdict that brightens the ground guide, so the
        /// two never disagree. One 0.2 s scale pop when it lights; static under Reduced Motion.
        /// </summary>
        private void RefreshPip(int slot, AbilityBaseSO equipped, AbilityRefusal refusal)
        {
            var pip = _slots[slot].Pip;
            if (pip == null) return;

            bool hot = equipped != null && _localOverlay != null && _localOverlay.IsHot(slot);
            bool show = AbilityPreviewRules.InRangePipVisible(hot, refusal == AbilityRefusal.Cooldown);

            if (show != _pipShown[slot])
            {
                _pipShown[slot] = show;
                pip.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
                _pipPopTimer[slot] = show && !PlayerPreferences.ReducedMotionEnabled ? 0f : -1f;
                if (!show || PlayerPreferences.ReducedMotionEnabled)
                    pip.style.scale = new Scale(Vector3.one);
            }

            if (_pipPopTimer[slot] < 0f) return;

            // unscaledDeltaTime: the pop is HUD motion and must not stretch with the hit-stop timeScale dip.
            _pipPopTimer[slot] += Time.unscaledDeltaTime;
            float t = _pipPopTimer[slot] / FeedbackTuning.HexPipPopSeconds;
            if (t >= 1f)
            {
                _pipPopTimer[slot] = -1f;
                pip.style.scale = new Scale(Vector3.one);
                return;
            }
            float k = 1f + (FeedbackTuning.HexPipPopScale - 1f) * Mathf.Sin(t * Mathf.PI);
            pip.style.scale = new Scale(new Vector3(k, k, 1f));
        }

        /// <summary>
        /// ART §6.6 layer 9, generalised from "cooldown seconds" to "state indicator".
        /// Only the seconds branch writes text, and only when the displayed integer
        /// actually moves — a seconds countdown changes at most once a second, well
        /// inside the <see cref="FeedbackTuning.BadgeCountdownTextUpdateHz"/> budget, so
        /// gating on the value itself is strictly tighter than gating on the clock.
        /// </summary>
        private void RefreshCenterMark(int slot, AbilityRefusal refusal, float remaining)
        {
            var refs = _slots[slot];
            if (refs.CdNum == null) return;

            var mark = HudFeedbackStyle.CenterMark(refusal);

            // With drawn marks, the label is the seconds readout and nothing else; with
            // the glyph fallback it also carries ⃠ / ✕ (text written in ApplyRefusalState).
            bool showLabel = HudFeedbackStyle.UseDrawnRefusalMarks
                ? mark == HexCenterMark.CooldownSeconds
                : mark != HexCenterMark.None;

            var display = showLabel ? DisplayStyle.Flex : DisplayStyle.None;
            if (refs.CdNum.style.display != display) refs.CdNum.style.display = display;

            if (mark != HexCenterMark.CooldownSeconds) return;

            int seconds = Mathf.CeilToInt(remaining);
            if (seconds == _appliedCdSeconds[slot]) return;
            _appliedCdSeconds[slot] = seconds;
            refs.CdNum.text = seconds.ToString();
        }

        /// <summary>
        /// Applies the refusal's USS class and the drawn mark's colours. Runs only on a
        /// real state change — the class list and the mark colours are static for the
        /// whole time a refusal holds, and rewriting them per-frame would dirty the
        /// element's style every frame for nothing.
        /// </summary>
        private void ApplyRefusalState(int slot, AbilityRefusal refusal)
        {
            if (_appliedRefusal[slot] == refusal) return;
            _appliedRefusal[slot] = refusal;

            var refs = _slots[slot];

            if (!string.IsNullOrEmpty(_appliedRefusalCls[slot]))
                refs.Hex.RemoveFromClassList(_appliedRefusalCls[slot]);

            string cls = HudFeedbackStyle.HexClass(refusal);
            if (!string.IsNullOrEmpty(cls)) refs.Hex.AddToClassList(cls);
            _appliedRefusalCls[slot] = cls;

            // Force the next seconds write through, so returning to a cooldown after any
            // other state repaints the number instead of trusting a stale cache.
            _appliedCdSeconds[slot] = int.MinValue;

            var mark = HudFeedbackStyle.CenterMark(refusal);

            if (HudFeedbackStyle.UseDrawnRefusalMarks)
            {
                // USS decides which strokes are visible; only the semantic colour is
                // written here, and only from FeedbackTuning.
                if (mark == HexCenterMark.StunnedCross)
                {
                    if (refs.StateBarA != null)
                        refs.StateBarA.style.backgroundColor = FeedbackTuning.RefusalStunnedCrossColor;
                    if (refs.StateBarB != null)
                        refs.StateBarB.style.backgroundColor = FeedbackTuning.RefusalStunnedCrossColor;
                }
                return;
            }

            // Glyph fallback (HudFeedbackStyle.UseDrawnRefusalMarks == false): the same
            // layer-9 label carries the character instead of the drawn shape. Clearing
            // the inline colour hands the label back to the stylesheet's warm white
            // rather than baking a second copy of that colour here.
            if (refs.CdNum == null) return;
            switch (mark)
            {
                case HexCenterMark.StunnedCross:
                    refs.CdNum.text        = HudFeedbackStyle.StunnedCrossGlyph;
                    refs.CdNum.style.color = FeedbackTuning.RefusalStunnedCrossColor;
                    break;
                default:
                    refs.CdNum.style.color = new StyleColor(StyleKeyword.Null);
                    break;
            }
        }

        // ---- Denied-press bump (FEEDBACK.md §6, case 29) -----------------------

        /// <summary>
        /// A press that was refused must never be absorbed silently. <c>AbilityController</c>
        /// latches one flag per refused press; this consumes it and shakes that hex.
        /// </summary>
        private void PollDeniedPress(AbilityController abilities)
        {
            if (abilities != null && abilities.TryConsumeDeniedPress(out int denied)
                && denied >= 0 && denied < SlotCount)
            {
                _bumpTimer[denied] = 0f;
            }

            // A fizzle (target-gated move released with nobody in range) is its own one-shot: the plain bump plus
            // the slash flash and whiff puff (the whiff SOUND is played by AbilityController, local only).
            if (abilities != null && abilities.TryConsumeFizzle(out int fizzled)
                && fizzled >= 0 && fizzled < SlotCount)
            {
                _bumpTimer[fizzled] = 0f;
                _fizzleTimer[fizzled] = 0f;
                SetFizzleVisible(fizzled, true);
            }

            for (int slot = 0; slot < SlotCount; slot++)
            {
                UpdateBump(slot);
                UpdateFizzle(slot);
            }
        }

        /// <summary>
        /// Builds the code-made hex overlays: the cancel X (two bars), the fizzle slash and the whiff puff. All
        /// picking-mode Ignore, hidden until driven. Painted above everything else in the hex.
        /// </summary>
        private static void BuildFlashElements(ref HexSlot slot, VisualElement hex)
        {
            var x = new VisualElement { pickingMode = PickingMode.Ignore };
            x.AddToClassList("cw-hex-x");
            var a = new VisualElement { pickingMode = PickingMode.Ignore };
            a.AddToClassList("cw-hex-x-bar"); a.AddToClassList("cw-hex-x-bar--a");
            var b = new VisualElement { pickingMode = PickingMode.Ignore };
            b.AddToClassList("cw-hex-x-bar"); b.AddToClassList("cw-hex-x-bar--b");
            x.Add(a); x.Add(b);
            x.style.display = DisplayStyle.None;

            var puff = new VisualElement { pickingMode = PickingMode.Ignore };
            puff.AddToClassList("cw-hex-puff");
            puff.style.display = DisplayStyle.None;

            var slash = new VisualElement { pickingMode = PickingMode.Ignore };
            slash.AddToClassList("cw-hex-flash-slash");
            slash.style.display = DisplayStyle.None;

            hex.Add(puff);
            hex.Add(slash);
            hex.Add(x);
            slot.CancelX = x;
            slot.Puff = puff;
            slot.FizzleSlash = slash;
        }

        private void SetFizzleVisible(int slot, bool on)
        {
            var d = on ? DisplayStyle.Flex : DisplayStyle.None;
            var refs = _slots[slot];
            if (refs.FizzleSlash != null) refs.FizzleSlash.style.display = d;
            if (refs.Puff != null) refs.Puff.style.display = d;
        }

        /// <summary>
        /// The 0.3 s fizzle flash: the slash is a transient flash (full strength, then gone with the puff), and the
        /// whiff puff grows and fades. Under Reduced Motion the puff does not scale; it only fades. Driven by
        /// <c>unscaledDeltaTime</c>, like the bump, so a hit-stop dip cannot stretch it.
        /// </summary>
        private void UpdateFizzle(int slot)
        {
            if (_fizzleTimer[slot] < 0f) return;

            var refs = _slots[slot];
            _fizzleTimer[slot] += Time.unscaledDeltaTime;
            float t = _fizzleTimer[slot] / FeedbackTuning.FizzleFlashSeconds;
            if (t >= 1f)
            {
                _fizzleTimer[slot] = -1f;
                SetFizzleVisible(slot, false);
                if (refs.Puff != null) refs.Puff.style.scale = new Scale(Vector3.one);
                return;
            }

            if (refs.FizzleSlash != null) refs.FizzleSlash.style.opacity = t < 0.6f ? 1f : 1f - (t - 0.6f) / 0.4f;
            if (refs.Puff != null)
            {
                refs.Puff.style.opacity = 1f - t;
                float k = PlayerPreferences.ReducedMotionEnabled
                    ? 1f
                    : Mathf.Lerp(FeedbackTuning.FizzlePuffStartScale, FeedbackTuning.FizzlePuffEndScale, t);
                refs.Puff.style.scale = new Scale(new Vector3(k, k, 1f));
            }
        }

        /// <summary>
        /// Decaying horizontal oscillation over
        /// <see cref="FeedbackTuning.DeniedPressBumpDurationSeconds"/>. Driven by
        /// <c>unscaledDeltaTime</c> on purpose: the §3.4 execute hit-stop dips
        /// <c>Time.timeScale</c>, and a refusal cue that stretched out with it would
        /// outlive the press that caused it.
        /// </summary>
        private void UpdateBump(int slot)
        {
            if (_bumpTimer[slot] < 0f) return;

            var hex = _slots[slot].Hex;
            _bumpTimer[slot] += Time.unscaledDeltaTime;
            float t = _bumpTimer[slot] / FeedbackTuning.DeniedPressBumpDurationSeconds;

            if (t >= 1f)
            {
                _bumpTimer[slot] = -1f;
                if (hex != null) hex.style.translate = new Translate(0f, 0f, 0f);
                return;
            }

            float x = FeedbackTuning.DeniedPressBumpAmplitudePx * (1f - t)
                    * Mathf.Sin(t * 2f * Mathf.PI * FeedbackTuning.DeniedPressBumpOscillations);
            if (hex != null) hex.style.translate = new Translate(x, 0f, 0f);
        }

        // ---- Control consequences: the stick + the status strip (§5.2) ---------

        private void RefreshControlConsequences()
        {
            var chicken = _localChicken;
            bool valid  = chicken != null && chicken.Object != null && chicken.Object.IsValid;

            float slow = valid ? chicken.SlowMultiplier : 1f;
            ApplyJoystickState(valid ? chicken.CurrentControlState : ControlState.Free, slow);

            int count = valid
                ? HudFeedbackStyle.CollectStatusRows(
                      chicken.IsStunned, chicken.StunRemaining,
                      chicken.Rooted,    chicken.RootRemaining,
                      slow, _statusScratch)
                : 0;

            // Forget the cached peak of any status that is no longer running, so the next
            // stun/root measures its own duration instead of draining against a stale
            // one. Keyed by kind, never by row index — a row's kind changes when a more
            // severe status above it expires and the rest shuffle up.
            bool stunLive = false, rootLive = false;
            for (int i = 0; i < count; i++)
            {
                if (_statusScratch[i].Kind == HudStatusKind.Stun) stunLive = true;
                else if (_statusScratch[i].Kind == HudStatusKind.Root) rootLive = true;
            }
            if (!stunLive) _statusPeak[(int)HudStatusKind.Stun] = 0f;
            if (!rootLive) _statusPeak[(int)HudStatusKind.Root] = 0f;

            ApplyStatusStrip(count);
        }

        /// <summary>
        /// §5.2's control-consequence marking on the move stick. Communication only: the
        /// stick stays draggable and <see cref="Movement"/> keeps reporting, because
        /// <c>ControlRules.CanMove</c> is already enforced in <c>ChickenController</c> and
        /// a second gate here would hide input bugs rather than fix them.
        /// </summary>
        private void ApplyJoystickState(ControlState state, float slowMultiplier)
        {
            if (_joyBase == null) return;

            if (state != _appliedJoyState)
            {
                _appliedJoyState = state;

                if (!string.IsNullOrEmpty(_appliedJoyCls)) _joyBase.RemoveFromClassList(_appliedJoyCls);
                string cls = HudFeedbackStyle.JoystickClass(state);
                if (!string.IsNullOrEmpty(cls)) _joyBase.AddToClassList(cls);
                _appliedJoyCls = cls;

                // Greyed = "this control is refused", at the same neutral colour and the
                // same alpha a refused ability hex uses, so a dead stick and a dead button
                // read as one idea. Slowed is TINTED, not greyed: movement still works, it
                // is just worth less, and greying it would say the opposite of the truth.
                Color tint;
                switch (state)
                {
                    case ControlState.Stunned:
                    case ControlState.Rooted:
                        tint = FeedbackTuning.NeutralNoEffectColor;
                        tint.a = FeedbackTuning.HexAlphaNoTarget;
                        break;
                    case ControlState.Slowed:
                        tint = FeedbackTuning.CanonicalSlowColor;
                        break;
                    default:
                        tint = Color.white; // untinted: back to the authored sprite
                        break;
                }
                _joyBase.style.unityBackgroundImageTintColor = tint;
                if (_joyKnob != null) _joyKnob.style.unityBackgroundImageTintColor = tint;

                if (_joyGlyph != null)
                {
                    // Rooted alone gets a glyph. A stunned player already has a red cross
                    // on every ability hex, which is the stronger read for "you can do
                    // nothing"; a second mark on the stick would be noise.
                    string glyph = state == ControlState.Rooted ? HudFeedbackStyle.RootGlyph : string.Empty;
                    if (_joyGlyph.text != glyph) _joyGlyph.text = glyph;
                    if (state == ControlState.Rooted)
                        _joyGlyph.style.color = FeedbackTuning.CanonicalRootColor;
                }

                _appliedSlowQuantity = int.MinValue;
            }

            if (state != ControlState.Slowed || _joySlowLabel == null) return;

            // ×0.45, not a countdown: SlowMultiplier is re-derived every tick from whatever
            // is currently touching the chicken and has no deadline to count down to (see
            // ChickenController.SlowMultiplier's remarks). The magnitude IS the number for
            // this state, by design.
            int quantity = Mathf.RoundToInt(slowMultiplier * 100f);
            if (quantity == _appliedSlowQuantity || !_textGateOpen) return;
            _appliedSlowQuantity = quantity;
            _joySlowLabel.text = "×" + (quantity * 0.01f).ToString("0.00");
            _joySlowLabel.style.color = FeedbackTuning.CanonicalSlowColor;
        }

        /// <summary>
        /// §5.2's status strip: one row per <i>active</i> status, not per dominant state —
        /// stun / root / slow are independent and routinely co-occur, and a strip that
        /// showed only the dominant one would hide the fact that a root is still ticking
        /// under a stun.
        /// </summary>
        private void ApplyStatusStrip(int count)
        {
            if (_statusStrip == null) return;

            var stripDisplay = count > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            if (_statusStrip.style.display != stripDisplay) _statusStrip.style.display = stripDisplay;

            for (int i = 0; i < _statusRows.Length; i++)
            {
                var row = _statusRows[i];
                if (row.Root == null) continue;

                if (i >= count)
                {
                    if (row.Root.style.display != DisplayStyle.None)
                        row.Root.style.display = DisplayStyle.None;
                    _appliedRowKind[i] = HudStatusKind.None;
                    continue;
                }

                var data = _statusScratch[i];
                if (row.Root.style.display != DisplayStyle.Flex)
                    row.Root.style.display = DisplayStyle.Flex;

                bool hasBar = HudFeedbackStyle.HasDrainBar(data.Kind);

                if (_appliedRowKind[i] != data.Kind)
                {
                    _appliedRowKind[i]     = data.Kind;
                    _appliedRowQuantity[i] = int.MinValue;

                    Color c = StatusColor(data.Kind);
                    if (row.Glyph != null)
                    {
                        row.Glyph.text = HudFeedbackStyle.GlyphFor(data.Kind);
                        row.Glyph.style.color = c;
                    }
                    if (row.Name != null)
                    {
                        row.Name.text = HudFeedbackStyle.NameFor(data.Kind);
                        row.Name.style.color = c;
                    }
                    if (row.Value != null) row.Value.style.color = c;

                    if (row.Bar != null)
                    {
                        var barDisplay = hasBar ? DisplayStyle.Flex : DisplayStyle.None;
                        if (row.Bar.style.display != barDisplay) row.Bar.style.display = barDisplay;
                        if (hasBar)
                        {
                            // Same track/arc contrast ratio the world-space drain rings use,
                            // so "how much is left" out-reads "how much there was" identically
                            // in both places.
                            var track = c;
                            track.a *= FeedbackTuning.DrainRingTrackAlphaMultiplier;
                            row.Bar.style.backgroundColor = track;
                            if (row.BarFill != null) row.BarFill.style.backgroundColor = c;
                        }
                    }
                }

                if (hasBar && row.BarFill != null)
                {
                    int k = (int)data.Kind;
                    if (data.Value > _statusPeak[k]) _statusPeak[k] = data.Value;
                    float frac = _statusPeak[k] > 0f ? Mathf.Clamp01(data.Value / _statusPeak[k]) : 0f;
                    // Per-frame is fine here: a width is a cheap style value and, unlike a
                    // text write, does not trigger a text relayout.
                    row.BarFill.style.width = new Length(frac * 100f, LengthUnit.Percent);
                }

                if (row.Value == null) continue;

                int quantity = data.Kind == HudStatusKind.Slow
                    ? Mathf.RoundToInt(data.Value * 100f)     // hundredths of a multiplier
                    : Mathf.CeilToInt(data.Value * 10f);      // tenths of a second

                if (quantity == _appliedRowQuantity[i] || !_textGateOpen) continue;
                _appliedRowQuantity[i] = quantity;
                row.Value.text = data.Kind == HudStatusKind.Slow
                    ? "×" + (quantity * 0.01f).ToString("0.00")
                    : (quantity > 0 ? (quantity * 0.1f).ToString("0.0") + "s" : string.Empty);
            }
        }

        private static Color StatusColor(HudStatusKind kind)
        {
            switch (kind)
            {
                case HudStatusKind.Stun: return FeedbackTuning.CanonicalStunColor;
                case HudStatusKind.Root: return FeedbackTuning.CanonicalRootColor;
                case HudStatusKind.Slow: return FeedbackTuning.CanonicalSlowColor;
                default:                 return Color.white;
            }
        }

        // ---- Equipped-ability swap ---------------------------------------------

        private void ApplyEquipped(int slot, AbilityBaseSO equipped)
        {
            var refs = _slots[slot];
            CategoryMark.Apply(refs.Mark, equipped != null ? equipped.Category : (AbilityCategory?)null);

            // Icon sprite via USS class (swap the previously applied one). Every mapped
            // ability has a sprite (AbilityIconArtTests), so the icon alone identifies it and
            // the short label is only the safety net for a type with no AbilityIconStyle entry.
            string cls = CluckWars.UI.AbilityIconStyle.ClassFor(equipped);
            bool hasSprite = !string.IsNullOrEmpty(cls);
            if (refs.Icon != null)
            {
                if (!string.IsNullOrEmpty(_appliedIconCls[slot]))
                    refs.Icon.RemoveFromClassList(_appliedIconCls[slot]);
                if (hasSprite) refs.Icon.AddToClassList(cls);
                refs.Icon.style.display = hasSprite ? DisplayStyle.Flex : DisplayStyle.None;
                _appliedIconCls[slot] = cls;
            }
            if (equipped != null && !hasSprite && _reportedMissingIcons.Add(equipped.GetType().Name))
                _log?.Warn(Source, $"Ability '{equipped.name}' ({equipped.GetType().Name}) has no AbilityIconStyle entry; " +
                                   "its hex shows the short label instead of an icon.");

            if (refs.Label != null)
            {
                refs.Label.text = equipped != null && !hasSprite
                    ? (string.IsNullOrEmpty(equipped.ShortLabel) ? equipped.DisplayName : equipped.ShortLabel)
                    : string.Empty;
                refs.Label.style.display = hasSprite ? DisplayStyle.None : DisplayStyle.Flex;
            }
        }

        /// <summary>
        /// The ability in <paramref name="slot"/>, or null. A <c>switch</c> with an explicit
        /// default rather than a chained ternary: the ternary's trailing else silently mapped
        /// slot 3 onto Slot2, so the fourth hex drew the third ability's icon, label and
        /// cooldown constant while its timer and refusal state came from the real slot 3.
        /// </summary>
        private static AbilityBaseSO SlotAbility(AbilityController abilities, int slot) => slot switch
        {
            0 => abilities.Slot0,
            1 => abilities.Slot1,
            2 => abilities.Slot2,
            3 => abilities.Slot3,
            _ => null,
        };

        private static ChickenController FindLocalChicken()
        {
            var all = FindObjectsByType<ChickenController>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            for (int i = 0; i < all.Length; i++)
            {
                var c = all[i];
                if (c.Object != null && c.Object.IsValid && c.HasInputAuthority) return c;
            }
            return null;
        }
    }
}

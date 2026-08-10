using CluckWars.Abilities;
using CluckWars.Gameplay;
using CluckWars.Logging;
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
    /// A hold that drags more than <c>FeedbackTuning.DragCancelDistancePx</c> away
    /// from the hex's centre sets a one-shot cancel flag
    /// (<see cref="ConsumeAbilityCancelled"/>) instead of clearing the hold silently,
    /// so <c>TouchInputProvider</c> can tell "released to fire" apart from "dragged
    /// off to cancel" — both look identical as a plain hold-bit falling edge
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
        private ColorSchemeSO _colors;

        // ---- UI element refs (queried once, on bind) ---------------------------
        private VisualElement _root;
        private bool          _bound;

        private VisualElement _joyBase;
        private VisualElement _joyKnob;
        private Label         _joyGlyph;      // ⛓ while rooted
        private Label         _joySlowLabel;  // ×0.45 while slowed
        private int           _joyPointerId = -1;

        /// <summary>Per-slot driven element refs for one hex ability button.</summary>
        private struct HexSlot
        {
            public VisualElement Hex;        // tinted per refusal state (ART §6.6 layers 2–4)
            public VisualElement Cooldown;   // layer 6: bottom-up black clip (height % = remaining/total)
            public VisualElement Drain;      // layer 6 mirrored: top-down accent clip while this slot is running
            public VisualElement DrainFill;  // accent-tinted silhouette inside the drain clip
            public VisualElement Icon;       // layer 7: sprite swapped via .cw-hex-icon--* class
            public Label         Label;      // layer 7: short label under the icon
            public Label         CdNum;      // layer 9: seconds remaining
            public VisualElement StateGlyph; // layer 9: drawn refusal mark (⃠ / ✕)
            public VisualElement StateRing;  // the ⃠ circle
            public VisualElement StateBarA;  // ⃠ slash, and one arm of the ✕
            public VisualElement StateBarB;  // the other arm of the ✕
        }
        private readonly HexSlot[]        _slots           = new HexSlot[3];
        private readonly AbilityBaseSO[]  _appliedSlots    = new AbilityBaseSO[3];
        private readonly string[]         _appliedIconCls  = new string[3];

        // Refusal state, cached so class/colour writes only happen on a real change.
        private readonly AbilityRefusal[] _appliedRefusal    = new AbilityRefusal[3];
        private readonly string[]         _appliedRefusalCls = new string[3];
        private readonly int[]            _appliedCdSeconds  = new int[3];

        // Denied-press bump timers (§6). -1 = idle.
        private readonly float[] _bumpTimer = new float[3];

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
        private readonly bool[] _pressed = new bool[3];

        // v0.6 hold-to-aim: level-triggered per-slot hold state + which pointer owns it.
        private readonly bool[] _held          = new bool[3];
        private readonly int[]  _heldPointerId = new int[3] { -1, -1, -1 };
        // One-shot "this hold was dragged off, don't let it fire" flag — consumed by
        // TouchInputProvider.GetAbilityCancelPressed via ConsumeAbilityCancelled.
        private readonly bool[] _cancelled     = new bool[3];

        // ---- Runtime state -----------------------------------------------------
        private Vector2           _movement;
        private ChickenController _localChicken;
        private float             _localChickenLastSearch;

        // ---- Input contract (read by TouchInputProvider) -----------------------
        public Vector2 Movement       => _movement;
        public bool    Ability1Pressed => _pressed[0];
        public bool    Ability2Pressed => _pressed[1];
        public bool    Ability3Pressed => _pressed[2];

        /// <summary>Level-triggered: true while slot's hex is held down and the hold
        /// hasn't been drag-cancelled. Safe to read every frame.</summary>
        public bool IsAbilityHeld(int slot) => slot >= 0 && slot < 3 && _held[slot];

        /// <summary>Returns (and clears) whether <paramref name="slot"/>'s hold was
        /// just drag-cancelled — the "released to fire" vs "dragged off to cancel"
        /// distinction <see cref="TouchInputProvider"/> needs (see class remarks).</summary>
        public bool ConsumeAbilityCancelled(int slot)
        {
            if (slot < 0 || slot >= 3 || !_cancelled[slot]) return false;
            _cancelled[slot] = false;
            return true;
        }

        [Inject]
        public void Construct(ILogService log, ColorSchemeSO colors)
        {
            _log = log;
            _colors = colors;
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

            for (int i = 0; i < 3; i++)
            {
                _appliedRefusal[i]   = UnsetRefusal;
                _appliedCdSeconds[i] = int.MinValue;
                _bumpTimer[i]        = -1f;
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

            for (int i = 0; i < 3; i++)
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
                    StateRing  = _root.Q<VisualElement>($"StateRing{n}"),
                    StateBarA  = _root.Q<VisualElement>($"StateBarA{n}"),
                    StateBarB  = _root.Q<VisualElement>($"StateBarB{n}"),
                };
                if (hex != null)
                {
                    int slot = i; // capture
                    hex.RegisterCallback<PointerDownEvent>(evt => OnHexDown(slot, evt));
                    // PointerMoveEvent isn't in the spec's literal event list but is the
                    // only way to measure drag distance for FeedbackTuning.DragCancelDistancePx
                    // (see class remarks) — without it there is no signal to cancel on.
                    hex.RegisterCallback<PointerMoveEvent>(evt => OnHexMove(slot, evt));
                    hex.RegisterCallback<PointerUpEvent>(evt => OnHexUp(slot, evt));
                    hex.RegisterCallback<PointerLeaveEvent>(evt => OnHexLeave(slot, evt));
                    hex.RegisterCallback<PointerCaptureOutEvent>(evt => OnHexCaptureOut(slot, evt));
                }
            }

            _bound = true;
            _log?.Info(Source, "Bound UITK touch controls: joystick + 3 hex ability buttons + status strip.");
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
            _heldPointerId[slot] = evt.pointerId;
            _slots[slot].Hex?.CapturePointer(evt.pointerId);
            evt.StopPropagation();
        }

        /// <summary>
        /// Drag-off cancel (FEEDBACK.md §2.6): while held, once the pointer strays past
        /// <see cref="FeedbackTuning.DragCancelDistancePx"/> from the hex's own centre,
        /// stop treating this as a live hold and flag it cancelled instead — so the
        /// eventual release fires <see cref="ConsumeAbilityCancelled"/> rather than a
        /// plain "hold fell, fire" edge. Once cancelled for this press, stays cancelled
        /// until the next <see cref="OnHexDown"/> (no un-cancelling by drifting back).
        /// </summary>
        private void OnHexMove(int slot, PointerMoveEvent evt)
        {
            if (!_held[slot] || _heldPointerId[slot] != evt.pointerId) return;

            var hex = _slots[slot].Hex;
            if (hex == null) return;

            var center = hex.contentRect.center;
            var delta  = new Vector2(evt.localPosition.x - center.x, evt.localPosition.y - center.y);
            if (delta.magnitude > FeedbackTuning.DragCancelDistancePx)
            {
                _held[slot] = false;
                _cancelled[slot] = true;
            }
        }

        private void OnHexUp(int slot, PointerUpEvent evt)
        {
            if (_heldPointerId[slot] != evt.pointerId) return;

            var hex = _slots[slot].Hex;
            if (hex != null && hex.HasPointerCapture(evt.pointerId)) hex.ReleasePointer(evt.pointerId);
            _held[slot] = false;
            _heldPointerId[slot] = -1;
        }

        /// <summary>
        /// Registered per the Stage 2 spec, but deliberately NOT a cancel trigger: the
        /// hex's own visual bounds (~75px half-width) are smaller than
        /// <see cref="FeedbackTuning.DragCancelDistancePx"/> (130px), so treating "left
        /// the element" as cancel would fire well before the tuned drag threshold and
        /// make that constant meaningless. Left as a no-op — <see cref="OnHexMove"/>'s
        /// distance check is the single source of truth for drag-off cancel, and the
        /// captured pointer keeps delivering move/up events regardless of visual bounds.
        /// </summary>
        private void OnHexLeave(int slot, PointerLeaveEvent evt) { }

        /// <summary>Pointer capture lost abnormally (OS gesture takeover, multi-touch
        /// conflict). Treated as a cancel, not a release-to-fire — an involuntary loss
        /// of tracking is not a deliberate "let go to cast" gesture.</summary>
        private void OnHexCaptureOut(int slot, PointerCaptureOutEvent evt)
        {
            if (!_held[slot]) return;
            _held[slot] = false;
            _cancelled[slot] = true;
            _heldPointerId[slot] = -1;
        }

        private void LateUpdate()
        {
            // Clear the press edges after the frame so callers (and the network
            // latch) read each tap exactly once — matches HoldButton timing.
            _pressed[0] = _pressed[1] = _pressed[2] = false;
        }

        // ---- Per-frame visual refresh -----------------------------------------

        private void Update()
        {
            if (!_bound)
            {
                TryBind();
                if (!_bound) return;
            }

            if (_localChicken == null || _localChicken.Object == null || !_localChicken.Object.IsValid)
            {
                if (Time.unscaledTime - _localChickenLastSearch > 0.5f)
                {
                    _localChickenLastSearch = Time.unscaledTime;
                    _localChicken = FindLocalChicken();
                }
            }

            // One 5 Hz gate shared by every string this document writes.
            _textGateOpen = Time.unscaledTime >= _nextTextUpdate;
            if (_textGateOpen)
                _nextTextUpdate = Time.unscaledTime + 1f / Mathf.Max(1f, FeedbackTuning.BadgeCountdownTextUpdateHz);

            var abilities = _localChicken != null ? _localChicken.Abilities : null;

            PollDeniedPress(abilities);

            for (int slot = 0; slot < 3; slot++)
                RefreshSlot(slot, abilities);

            RefreshControlConsequences();
        }

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
            float active01 = (abilities != null && equipped != null && abilities.ActiveSlot == slot)
                ? abilities.ActiveRemaining01
                : 0f;
            if (refs.Drain != null)
                refs.Drain.style.height = new Length(active01 * 100f, LengthUnit.Percent);

            Color accent = equipped != null ? equipped.AccentColor : _colors.AbilityNormal;

            if (refs.DrainFill != null && active01 > 0f)
            {
                // Full-strength accent: the running slot must be the one bright thing
                // while the rest of the cluster sits at HexAlphaOtherActive.
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
                case AbilityRefusal.NoTarget:
                    tint = FeedbackTuning.NeutralNoEffectColor;
                    tint.a = FeedbackTuning.HexAlphaNoTarget;
                    break;
                case AbilityRefusal.Stunned:
                    // The illegal-cast wash, the same one the telegraph uses when a hold
                    // goes illegal mid-aim — being stunned and having your aim invalidated
                    // are the same story told at two moments.
                    tint = FeedbackTuning.IllegalCastTintColor;
                    break;
                case AbilityRefusal.OtherAbilityActive:
                    tint = accent;
                    tint.a = FeedbackTuning.HexAlphaOtherActive;
                    break;
                default:
                    tint = accent;
                    tint.a = FeedbackTuning.HexAlphaReady;
                    break;
            }
            refs.Hex.style.unityBackgroundImageTintColor = tint;

            // Layer 9. The seconds number is only one of the marks this layer can carry
            // now; the others are drawn shapes toggled by the refusal class in USS.
            RefreshCenterMark(slot, refusal, remaining);
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
                if (mark == HexCenterMark.NoTarget)
                {
                    SetBorderColor(refs.StateRing, FeedbackTuning.NeutralNoEffectColor);
                    if (refs.StateBarA != null)
                        refs.StateBarA.style.backgroundColor = FeedbackTuning.NeutralNoEffectColor;
                }
                else if (mark == HexCenterMark.StunnedCross)
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
                case HexCenterMark.NoTarget:
                    refs.CdNum.text        = HudFeedbackStyle.NoTargetGlyph;
                    refs.CdNum.style.color = FeedbackTuning.NeutralNoEffectColor;
                    break;
                case HexCenterMark.StunnedCross:
                    refs.CdNum.text        = HudFeedbackStyle.StunnedCrossGlyph;
                    refs.CdNum.style.color = FeedbackTuning.RefusalStunnedCrossColor;
                    break;
                default:
                    refs.CdNum.style.color = new StyleColor(StyleKeyword.Null);
                    break;
            }
        }

        private static void SetBorderColor(VisualElement e, Color c)
        {
            if (e == null) return;
            e.style.borderTopColor    = c;
            e.style.borderRightColor  = c;
            e.style.borderBottomColor = c;
            e.style.borderLeftColor   = c;
        }

        // ---- Denied-press bump (FEEDBACK.md §6, case 29) -----------------------

        /// <summary>
        /// A press that was refused must never be absorbed silently. <c>AbilityController</c>
        /// latches one flag per refused press; this consumes it and shakes that hex.
        /// </summary>
        private void PollDeniedPress(AbilityController abilities)
        {
            if (abilities != null && abilities.TryConsumeDeniedPress(out int denied)
                && denied >= 0 && denied < 3)
            {
                _bumpTimer[denied] = 0f;
            }

            for (int slot = 0; slot < 3; slot++) UpdateBump(slot);
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

            // Icon sprite via USS class (swap the previously applied one).
            if (refs.Icon != null)
            {
                if (!string.IsNullOrEmpty(_appliedIconCls[slot]))
                    refs.Icon.RemoveFromClassList(_appliedIconCls[slot]);

                string cls = CluckWars.UI.AbilityIconStyle.ClassFor(equipped);

                if (!string.IsNullOrEmpty(cls))
                {
                    refs.Icon.AddToClassList(cls);
                    refs.Icon.style.display = DisplayStyle.Flex;
                }
                else
                {
                    // No sprite mapping — hide the icon so the short label carries it.
                    refs.Icon.style.display = DisplayStyle.None;
                }
                _appliedIconCls[slot] = cls;
            }

            // Short label (falls back to DisplayName; the sole content if no icon).
            if (refs.Label != null)
                refs.Label.text = equipped != null
                    ? (string.IsNullOrEmpty(equipped.ShortLabel) ? equipped.DisplayName : equipped.ShortLabel)
                    : string.Empty;
        }

        private static AbilityBaseSO SlotAbility(AbilityController abilities, int slot) =>
            slot == 0 ? abilities.Slot0 : (slot == 1 ? abilities.Slot1 : abilities.Slot2);

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

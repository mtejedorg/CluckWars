using CluckWars.Abilities;
using CluckWars.Audio;
using CluckWars.Gameplay;
using CluckWars.Injection;
using CluckWars.Localization;
using CluckWars.Logging;
using CluckWars.Services;
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
    /// tint from the equipped <see cref="AbilityAccent.AccentColor"/>, a bottom-up
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
    ///
    /// <b>Phase 6 chunk 6 (A10): the layout follows the last-used device.</b> <see cref="Mode"/> is
    /// <see cref="HudLayoutRules.FromDevice"/> of <c>IInputProvider.Device</c>: keyboard / mouse and gamepad hide the
    /// joystick and turn the hexes into a smaller, click-through information strip badged with the keys; any touch
    /// brings the thumb layout back. Device reads stay in the input providers; this class only asks which one is in
    /// hand. The class also owns the first-five-holds hint and the cancel-arm haptic tick.
    /// </remarks>
    [RequireComponent(typeof(UIDocument))]
    public sealed partial class TouchControlsController : MonoBehaviour
    {
        private const string Source = "TouchHud";

        public static TouchControlsController Instance { get; private set; }

        // Ability -> exported-icon USS class comes from the shared
        // CluckWars.UI.AbilityIconStyle (single source of truth, also used by the
        // character-select / lobby menus). Refusal / control-state USS classes and
        // the status glyphs come from CluckWars.UI.HudFeedbackStyle for the same
        // reason — the world-space badges read the same glyph consts.

        // Joystick geometry. The throw is JoystickRules.ThrowRadiusPanelPx (60 dp, floored at the old 108 px
        // reference value), recomputed at every touch-down because the panel scale follows the screen.
        private float _joyThrow = JoystickRules.MinThrowPanelPx;

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
            public Label         BadgeText;  // slot badge text: the slot number, or the key / pad button on desktop
            public VisualElement Pip;        // in-range pip: a rival is in this move's shape (bottom-centre)
            public HexDurationRing Ring;     // duration ring while this slot's ability is running
            public HexChainRing Chain;       // spinning dashed ring on the Peck hex while the auto-chain runs
            public VisualElement CancelX;    // built in code: the cream X shown while the edge-band cancel is armed
            public VisualElement FizzleSlash;// built in code: the 0.3 s slash flash after a fizzle
            public VisualElement Puff;       // built in code: the Fx_Whiff puff after a fizzle
            public Label         KillText;   // Death Mark: "KILL" over the hex while a press would kill
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
        private bool _appliedZoneShown;
        private readonly float[] _heldSeconds = new float[SlotCount]; // touch hold time per hex; the cancel zone shows past the tap threshold
        private VisualElement _glowLeft, _glowRight, _glowTop, _glowBottom;

        // Phase 6 (A1) hex states, cached so style writes only happen on a real change.
        private readonly bool[]  _appliedHeld = new bool[SlotCount];
        private readonly bool[]  _appliedDim  = new bool[SlotCount];
        private readonly float[] _appliedScale = new float[SlotCount];   // the inline scale last written (rest scale x held scale)
        private readonly string[] _appliedCdText = new string[SlotCount]; // desktop cooldown readout cache (tenths under 1 s)

        // A4 caster-side stun countdown (CasterStunCountdownRules): the last cast of the local chicken (its
        // ChickenLocalEvents.Cast, subscribed per local chicken), and the per-frame verdict the Stun hex reads.
        private ChickenController _stunCastFor;
        private ChickenLocalEvents _stunCastEvents;   // _stunCastFor's events while subscribed
        private bool  _stunCastPending;
        private CastEvent _stunCast;
        private bool  _stunCastIsWingSlam;
        private int   _stunCastHits;
        private float _stunCastAt;
        private float _stunCastSeconds;
        private float[] _rivalStun = new float[4];   // grows only when more rivals exist than it holds
        private bool  _casterStunShow;
        private int   _casterStunTenths;
        private readonly bool[] _stunDigitShown     = new bool[SlotCount];
        private readonly int[]  _appliedStunTenths  = new int[SlotCount];

        // J3 egg-phase hex (EggPhaseHexRules): per slot, the egg's remaining tenths this frame (0 = not in the egg phase), whether
        // the cracked-egg icon class and the digit are currently applied, and the last tenth written.
        private readonly int[]  _eggTenths         = new int[SlotCount];
        private readonly bool[] _eggIconShown      = new bool[SlotCount];
        private readonly bool[] _eggDigitShown     = new bool[SlotCount];
        private readonly int[]  _appliedEggTenths  = new int[SlotCount];

        // Death Mark hexes (MarkFeedback.Current): KILL on the Death Mark hex exactly while a press would kill; Ambush lit while
        // the target stands in its shape. Found by the equipped ability's type, never by slot number.
        private readonly bool[]  _markKill = new bool[SlotCount];
        private readonly bool[]  _markLit  = new bool[SlotCount];
        private readonly bool[]  _appliedMarkKill = new bool[SlotCount];
        private readonly bool[]  _appliedMarkLit  = new bool[SlotCount];
        private readonly float[] _markKillSince = new float[SlotCount];
        private const string MarkKillClass = "cw-hex--kill";
        private const string MarkLitClass  = "cw-hex--lit";

        // Phase 6 chunk 6: layout mode (A10), haptics (A9) and the first-time hold hint (A1).
        private IInputProvider  _input;
        private IHapticsService _haptics;
        private IAudioService   _audio;     // E1: the denied-press click (local HUD sound)
        private AudioRegistrySO _sfx;
        private bool            _missingAudioReported;
        private HudDeviceMode   _mode = HudDeviceMode.Touch;
        private bool            _modeApplied;
        private VisualElement   _hudRoot;
        private VisualElement   _hintHost;
        private Label           _hint;
        private bool            _holdActive;
        private float           _holdBeganAt;
        private bool            _holdQualified;
        private bool            _hintOn;
        private readonly bool[]  _pipShown    = new bool[SlotCount];
        private readonly float[] _pipPopTimer = new float[SlotCount]; // seconds into the 0.2 s pop; -1 = idle
        private readonly bool[]  _ringShown   = new bool[SlotCount];
        private readonly bool[]  _chainShown  = new bool[SlotCount];
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

        /// <summary>The control scheme the HUD is laid out for right now (the last-used device's).</summary>
        public HudDeviceMode Mode => _mode;

        /// <summary>How many status-strip rows are showing (0 = strip hidden). The strip is an info zone only while it has
        /// rows (<c>HudControlZones.BuildInfoZones</c>, WP7).</summary>
        public int StatusRowCount { get; private set; }

        /// <summary>Test / capture hook: pins the layout regardless of the last-used device. Null = follow the device.</summary>
        public static HudDeviceMode? DebugForceMode;

        [Inject]
        public void Construct(ILogService log, [InjectOptional] IHapticsService haptics, [InjectOptional] IInputProvider input,
                              [InjectOptional] IAudioService audio, [InjectOptional] AudioRegistrySO sfx)
        {
            _log = log;
            _haptics = haptics;
            _input = input;
            _audio = audio;
            _sfx = sfx;
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DebugForceMode = null;

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
                _appliedScale[i]     = 1f;
            }

            SelfInject.Ensure(this, _log != null);
        }

        private void OnEnable() => TryBind();

        // A controller that stops ticking must not leave a stale strip info zone behind (HudControlZones, WP7 W6).
        private void OnDisable()
        {
            StatusRowCount = 0;
            BindStunCastEvents(null);   // the paired unsubscribe; the next Update re-binds
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        // ---- Bind --------------------------------------------------------------

        private void TryBind()
        {
            // A duplicate controller (Awake returned before allocating the row arrays and scheduled its own
            // Destroy) still gets OnEnable and a frame of Update: binding it threw every frame (BL-134).
            if (Instance != null && Instance != this) return;
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
                    BadgeText  = _root.Q<Label>($"BadgeText{n}"),
                    KillText   = _root.Q<Label>($"KillText{n}"),
                    Mark       = hex != null ? CategoryMark.Create() : null,
                    Ring       = hex != null ? new HexDurationRing() : null,
                    Chain      = hex != null ? new HexChainRing() : null,
                };
                if (hex != null)
                {
                    hex.Add(_slots[i].Mark);
                    hex.Add(_slots[i].Ring);
                    _slots[i].Ring.style.display = DisplayStyle.None;
                    hex.Add(_slots[i].Chain);
                    _slots[i].Chain.style.display = DisplayStyle.None;
                    BuildFlashElements(ref _slots[i], hex);
                    if (_slots[i].Fill == null || _slots[i].Pip == null)
                        _log?.Error(Source, $"TouchControls.uxml is missing #Fill{n} or #Pip{n}: that hex draws without its rim / in-range pip.");
                    if (_slots[i].KillText == null)
                        _log?.Error(Source, $"TouchControls.uxml is missing #KillText{n}: a Death Mark on that hex shows no KILL word.");
                    else
                        _slots[i].KillText.text = UiText.Get(UiKeys.MarkHexKill);

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

            _hudRoot  = _root.Q<VisualElement>("TouchRoot") ?? _root;
            // Screen-shape classes (portrait / landscape / tall / short / narrow) for the portrait overrides (WP7). Separate
            // from UpdateLayoutMode, which switches on the device in hand (touch / desktop / pad), not the screen.
            LayoutClasses.Bind(_root, _hudRoot);
            _hintHost = _root.Q<VisualElement>("HoldHintHost");
            _hint     = _root.Q<Label>("HoldHint");
            if (_hint == null)
                _log?.Error(Source, "TouchControls.uxml is missing #HoldHint: the first-time hold hint will not show.");

            _modeApplied = false;   // a re-bind (domain reload, UIDocument re-enable) re-applies the layout classes
            _bound = true;
            _log?.Info(Source, $"Bound UITK touch controls: joystick + {SlotCount} hex ability buttons + status strip.");
        }

        // ---- Layout mode (Phase 6 chunk 6, A10) ---------------------------------

        /// <summary>
        /// Follows the last-used device (<see cref="HudLayoutRules.FromDevice"/>): re-applies the layout only when the
        /// mode actually changes, so a steady frame costs one property read.
        /// </summary>
        private void UpdateLayoutMode()
        {
            var wanted = DebugForceMode ?? HudLayoutRules.FromDevice(
                _input != null ? _input.Device : InputDeviceKind.None, Application.isMobilePlatform);
            if (_modeApplied && wanted == _mode) return;
            ApplyMode(wanted);
        }

        private void ApplyMode(HudDeviceMode mode)
        {
            bool leavingTouch = _modeApplied && _mode == HudDeviceMode.Touch && mode != HudDeviceMode.Touch;
            if (leavingTouch) ReleaseAllPointers();

            _mode = mode;
            _modeApplied = true;
            bool interactive = HudLayoutRules.HexesAreInteractive(mode);

            if (_hudRoot != null)
            {
                _hudRoot.EnableInClassList("cw-hud--desktop", mode == HudDeviceMode.KeyboardMouse);
                _hudRoot.EnableInClassList("cw-hud--pad", mode == HudDeviceMode.Gamepad);
            }
            if (_joyBase != null)
                _joyBase.style.display = interactive ? DisplayStyle.Flex : DisplayStyle.None;

            for (int slot = 0; slot < SlotCount; slot++)
            {
                var refs = _slots[slot];
                if (refs.Hex != null) refs.Hex.pickingMode = interactive ? PickingMode.Position : PickingMode.Ignore;
                if (refs.BadgeText != null) refs.BadgeText.text = HudLayoutRules.BadgeLabel(mode, slot);
                _appliedCdText[slot] = null;
                _appliedCdSeconds[slot] = int.MinValue;
                _appliedScale[slot] = -1f;   // rest scale changed: force the next held refresh to rewrite it
            }

            if (_hint != null && _hintOn) HideHint();
            _log?.Info(Source, $"HUD layout: {mode}.");
        }

        /// <summary>The layout is leaving the thumb arc while pointers may still be down: nothing fires from them.</summary>
        private void ReleaseAllPointers()
        {
            for (int slot = 0; slot < SlotCount; slot++)
            {
                if (_held[slot])
                {
                    int id = _heldPointerId[slot];
                    _held[slot] = false;
                    _heldPointerId[slot] = -1;
                    _cancelled[slot] = true;
                    var hex = _slots[slot].Hex;
                    if (id >= 0 && hex != null && hex.HasPointerCapture(id)) hex.ReleasePointer(id);
                }
                _cancelArmed[slot] = false;
                _armedEdge[slot] = ScreenEdge.None;
            }
            if (_joyBase != null && _joyPointerId >= 0 && _joyBase.HasPointerCapture(_joyPointerId))
                _joyBase.ReleasePointer(_joyPointerId);
            ResetJoy();
        }

        // ---- First-time hold hint (Phase 6 chunk 6, A1) ---------------------------

        /// <summary>
        /// A real hold (it outlived the tap / hold threshold) shows the one-line hint, and counts toward the five that
        /// retire it. Taps never count. Quick Moves has no hold to explain, so it neither shows nor counts.
        /// </summary>
        private void UpdateHoldHint()
        {
            bool any = false;
            for (int i = 0; i < SlotCount && !any; i++) any = IsHeldVisual(i);

            if (!any)
            {
                _holdActive = false;
                if (_hintOn) HideHint();
                return;
            }

            if (!_holdActive)
            {
                _holdActive = true;
                _holdQualified = false;
                _holdBeganAt = Time.unscaledTime;
            }

            if (_holdQualified || PlayerPreferences.QuickMovesEnabled) return;
            if (Time.unscaledTime - _holdBeganAt < FeedbackTuning.TapHoldThresholdSeconds) return;

            _holdQualified = true;
            int count = PlayerPreferences.HoldHintCount;
            if (!HoldHintRules.OnHoldQualified(ref count)) return;
            PlayerPreferences.HoldHintCount = count;
            ShowHint();
        }

        private void ShowHint()
        {
            if (_hint == null) return;
            _hint.text = HoldHintRules.Format(_mode, UiText.Get(HoldHintRules.KeyFor(_mode)));
            // Reduced Motion: appear and vanish at once instead of fading.
            _hint.style.transitionDuration = PlayerPreferences.ReducedMotionEnabled
                ? new StyleList<TimeValue>(new System.Collections.Generic.List<TimeValue> { new TimeValue(0f, TimeUnit.Second) })
                : new StyleList<TimeValue>(StyleKeyword.Null);
            _hint.AddToClassList(HintOnClass);
            _hintOn = true;
        }

        private void HideHint()
        {
            _hint?.RemoveFromClassList(HintOnClass);
            _hintOn = false;
        }

        private const string HintOnClass = "cw-hold-hint--on";

        // ---- Joystick pointer handling ----------------------------------------

        private void OnJoyDown(PointerDownEvent evt)
        {
            _joyPointerId = evt.pointerId;
            _joyThrow = JoystickRules.ThrowRadiusPanelPx(Screen.dpi, _root.worldBound.width, Screen.width);
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
            var clamped = Vector2.ClampMagnitude(delta, _joyThrow);

            // Knob follows the finger in UITK space (y down = positive).
            if (_joyKnob != null) _joyKnob.style.translate = new Translate(clamped.x, clamped.y, 0f);

            // Movement: normalize + flip y so up = +y (game convention), then the dead zone (0.12) with its
            // rescaled ramp so the output leaves 0 smoothly at the dead-zone edge. The knob still follows the
            // finger exactly; only the reported movement is dead-zoned.
            _movement = JoystickRules.ApplyDeadZone(new Vector2(clamped.x, -clamped.y) / _joyThrow);
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
            _heldSeconds[slot] = 0f;
            _heldPointerId[slot] = evt.pointerId;
            _slots[slot].Hex?.CapturePointer(evt.pointerId);
            evt.StopPropagation();
            // A press that lands inside the edge band (a hex near the screen edge, a wide thumb) is armed from the first
            // frame, not only after the first move event (Phase 7 B2).
            EvaluateCancelBand(slot, evt.position);
            RefreshHeldVisuals(); // the press frame, locally: not after the networked charge
        }

        /// <summary>
        /// Edge-band cancel (Phase 6, A4): while held, the pointer's RAW screen position (the panel position
        /// converted back to screen pixels, then to dp) arms the cancel within 36 dp of any physical screen edge
        /// and disarms beyond 44 dp (<see cref="HoldCancelRules"/>). Nothing is decided here beyond that: the
        /// release is what cancels (<see cref="EndHexPointer"/>). There is deliberately no radial drag-off any
        /// more; a thumb that merely drifts never cancels.
        /// </summary>
        private void OnHexMove(int slot, PointerMoveEvent evt)
        {
            if (!_held[slot] || _heldPointerId[slot] != evt.pointerId) return;
            EvaluateCancelBand(slot, evt.position);
        }

        /// <summary>Runs <see cref="HoldCancelRules"/> for a pointer position (down frame and every move).</summary>
        private void EvaluateCancelBand(int slot, Vector3 panelPos)
        {
            if (!TryPanelToScreen(panelPos, out var screen)) return;

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
        /// The moment the edge-band cancel arms: a 15 ms haptic tick (gated by "Buzz When Hit" and the shared rate
        /// limit inside <see cref="IHapticsService"/>).
        /// </summary>
        private void OnCancelArmed(int slot) => _haptics?.Buzz(HapticKind.CancelArm);

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
            if (Instance != null && Instance != this) return;
            if (!_bound)
            {
                TryBind();
                if (!_bound) { StatusRowCount = 0; return; }   // no strip drawn: no strip zone
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

            UpdateLayoutMode();
            UpdateMarkHexes(abilities);
            UpdateCasterStunCountdown(abilities);
            PollDeniedPress(abilities);
            for (int i = 0; i < SlotCount; i++)
                _heldSeconds[i] = _held[i] ? _heldSeconds[i] + Time.unscaledDeltaTime : 0f;
            RefreshHeldVisuals();
            UpdateHoldHint();

            for (int slot = 0; slot < SlotCount; slot++)
                RefreshSlot(slot, abilities);

            RefreshControlConsequences();
        }
    }
}

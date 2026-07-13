using CluckWars.Abilities;
using CluckWars.Gameplay;
using CluckWars.Logging;
using CluckWars.UI;
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
    /// Per-slot visuals mirror the old <c>TouchControlsHud.Update()</c>: accent
    /// tint from the equipped <see cref="AbilityBaseSO.AccentColor"/>, a bottom-up
    /// cooldown clip (ART §6.6 layer 6), desaturation when a range-gated ability
    /// has no target, slot-3 hidden unless the Combo (Assassin) slot is equipped,
    /// icon sprite + short label + slot badge + cooldown-seconds number. The local
    /// chicken is resolved the same way the old HUD did (input-authority scan).
    /// Self-inject falls back to the SceneContext first per CONVENTIONS IP-fix1.
    /// </remarks>
    [RequireComponent(typeof(UIDocument))]
    public sealed class TouchControlsController : MonoBehaviour
    {
        private const string Source = "TouchHud";

        public static TouchControlsController Instance { get; private set; }

        // Ability -> exported-icon USS class comes from the shared
        // CluckWars.UI.AbilityIconStyle (single source of truth, also used by the
        // character-select / lobby menus).

        // Desaturated tint for "ready but no target in range" (range-gated abilities).
        private static readonly Color OutOfRangeTint = new Color(0.42f, 0.44f, 0.47f, 1f);

        // Joystick geometry (reference px; mirrors the old UGUI tunables).
        private const float JoyMaxRadius = 108f; // joystickRadius 140 - knobRadius 64 * 0.5

        // ---- Injected services -------------------------------------------------
        private ILogService   _log;
        private ColorSchemeSO _colors;

        // ---- UI element refs (queried once, on bind) ---------------------------
        private VisualElement _root;
        private bool          _bound;

        private VisualElement _joyBase;
        private VisualElement _joyKnob;
        private int           _joyPointerId = -1;

        /// <summary>Per-slot driven element refs for one hex ability button.</summary>
        private struct HexSlot
        {
            public VisualElement Hex;       // tinted to AccentColor; dimmed on cooldown
            public VisualElement Cooldown;  // bottom-up clip wrapper (height % = remaining/total)
            public VisualElement Icon;      // sprite swapped via .cw-hex-icon--* class
            public Label         Label;     // short label under the icon
            public Label         CdNum;     // seconds remaining, centered, only while cooling down
        }
        private readonly HexSlot[]        _slots           = new HexSlot[3];
        private readonly AbilityBaseSO[]  _appliedSlots    = new AbilityBaseSO[3];
        private readonly string[]         _appliedIconCls  = new string[3];

        // Edge-triggered press flags (one-frame, cleared in LateUpdate).
        private readonly bool[] _pressed = new bool[3];

        // ---- Runtime state -----------------------------------------------------
        private Vector2           _movement;
        private ChickenController _localChicken;
        private float             _localChickenLastSearch;

        // ---- Input contract (read by TouchInputProvider) -----------------------
        public Vector2 Movement       => _movement;
        public bool    Ability1Pressed => _pressed[0];
        public bool    Ability2Pressed => _pressed[1];
        public bool    Ability3Pressed => _pressed[2];

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

            _joyBase = _root.Q<VisualElement>("JoystickBase");
            _joyKnob = _root.Q<VisualElement>("JoystickKnob");
            if (_joyBase != null)
            {
                _joyBase.RegisterCallback<PointerDownEvent>(OnJoyDown);
                _joyBase.RegisterCallback<PointerMoveEvent>(OnJoyMove);
                _joyBase.RegisterCallback<PointerUpEvent>(OnJoyUp);
                _joyBase.RegisterCallback<PointerCaptureOutEvent>(OnJoyCaptureOut);
            }

            for (int i = 0; i < 3; i++)
            {
                int n = i + 1;
                var hex = _root.Q<VisualElement>($"Ability{n}");
                _slots[i] = new HexSlot
                {
                    Hex      = hex,
                    Cooldown = _root.Q<VisualElement>($"Cooldown{n}"),
                    Icon     = _root.Q<VisualElement>($"Icon{n}"),
                    Label    = _root.Q<Label>($"Label{n}"),
                    CdNum    = _root.Q<Label>($"CooldownNum{n}"),
                };
                if (hex != null)
                {
                    int slot = i; // capture
                    hex.RegisterCallback<PointerDownEvent>(evt => OnHexDown(slot, evt));
                }
            }

            _bound = true;
            _log?.Info(Source, "Bound UITK touch controls: joystick + 3 hex ability buttons.");
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

        // ---- Hex press (edge-triggered) ---------------------------------------

        private void OnHexDown(int slot, PointerDownEvent evt)
        {
            _pressed[slot] = true;
            evt.StopPropagation();
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

            var abilities = _localChicken != null ? _localChicken.Abilities : null;
            for (int slot = 0; slot < 3; slot++)
                RefreshSlot(slot, abilities);
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

            // Slot 3 (Combo) is only meaningful for Assassin — hide when empty.
            if (slot == 2)
            {
                var display = equipped != null ? DisplayStyle.Flex : DisplayStyle.None;
                if (refs.Hex.style.display != display) refs.Hex.style.display = display;
            }

            // Cooldown + usability state.
            float remaining = 0f, total = 0f;
            bool  usable    = true;
            if (equipped != null)
            {
                total = equipped.Cooldown;
                if (total > 0f) remaining = abilities.CooldownRemaining(slot);
                usable = equipped.IsUsable(_localChicken);
            }

            float pct  = total > 0f ? Mathf.Clamp01(remaining / total) : 0f;
            bool  onCd = pct > 0f;

            if (refs.Cooldown != null)
                refs.Cooldown.style.height = new Length(pct * 100f, LengthUnit.Percent);

            if (refs.CdNum != null)
            {
                var display = onCd ? DisplayStyle.Flex : DisplayStyle.None;
                if (refs.CdNum.style.display != display) refs.CdNum.style.display = display;
                if (onCd) refs.CdNum.text = Mathf.CeilToInt(remaining).ToString();
            }

            // Three tint states (priority): cooldown -> dimmed accent; ready-no-target
            // -> desaturated grey; usable -> full accent. Mirrors the old HUD.
            Color accent = equipped != null ? equipped.AccentColor : _colors.AbilityNormal;
            Color tint   = (onCd || usable) ? accent : OutOfRangeTint;
            tint.a       = onCd ? 0.45f : (usable ? 1f : 0.7f);
            refs.Hex.style.unityBackgroundImageTintColor = tint;

            if (refs.Icon != null)
                refs.Icon.style.opacity = onCd ? 0.35f : (usable ? 1f : 0.5f);
        }

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

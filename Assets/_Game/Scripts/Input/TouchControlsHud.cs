using CluckWars.Abilities;
using CluckWars.Gameplay;
using CluckWars.Logging;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;
using Zenject;

namespace CluckWars.Input
{
    /// <summary>
    /// On-screen touch controls for the Game scene: virtual joystick (left thumb),
    /// attack button + 2 ability buttons (right thumb). Builds the entire UGUI
    /// hierarchy procedurally on Awake so the scene file stays minimal — drop a
    /// single GameObject with this component into <c>Game.unity</c> and it builds
    /// the canvas + buttons at runtime.
    /// </summary>
    /// <remarks>
    /// Always visible (PC + Android). On Windows the buttons are clickable with
    /// the mouse, which doubles as a way to test the touch flow without leaving
    /// the editor. Visuals are placeholder squares with white tint — Phase 7
    /// polish replaces them with hand-authored sprites.
    /// </remarks>
    public sealed class TouchControlsHud : MonoBehaviour
    {
        public static TouchControlsHud Instance { get; private set; }

        // --- Layout tunables (canvas units; reference resolution 1920×1080) ---
        [Header("Layout")]
        [SerializeField] private Vector2 _referenceResolution = new Vector2(1920f, 1080f);
        [SerializeField] private float _joystickRadius = 140f;
        [SerializeField] private float _knobRadius = 64f;
        [SerializeField] private Vector2 _joystickAnchoredPosition = new Vector2(220f, 220f);
        [SerializeField] private float _attackButtonSize = 220f;
        [SerializeField] private Vector2 _attackAnchoredPosition = new Vector2(-220f, 200f);
        [SerializeField] private float _abilityButtonSize = 140f;
        [SerializeField] private Vector2 _ability1AnchoredPosition = new Vector2(-440f, 240f);
        [SerializeField] private Vector2 _ability2AnchoredPosition = new Vector2(-260f, 420f);

        // Visuals come from ColorSchemeSO — no inline literals here anymore.
        // Defaults are the SO's field initializers, so even an empty / default
        // scheme produces the same visuals as before this refactor.

        private VirtualJoystick _joystick;
        private HoldButton _attack;
        private HoldButton _ability1;
        private HoldButton _ability2;
        private Image _ability1CooldownOverlay;
        private Image _ability2CooldownOverlay;
        private ChickenController _localChicken;
        private float _localChickenLastSearch;
        private ILogService _log;
        private ColorSchemeSO _colors;

        // Cached so we re-apply the ability tint only when the equipped SO changes.
        private AbilityBaseSO _appliedSlot0;
        private AbilityBaseSO _appliedSlot1;

        public Vector2 Movement => _joystick != null ? _joystick.Value : Vector2.zero;
        public bool AttackHeld => _attack != null && _attack.IsHeld;
        public bool Ability1Pressed => _ability1 != null && _ability1.WasPressedThisFrame;
        public bool Ability2Pressed => _ability2 != null && _ability2.WasPressedThisFrame;

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

            // Game scene typically has a SceneContext, but be defensive: if Construct
            // hasn't fired yet, self-inject from ProjectContext.
            if (_log == null) ProjectContext.Instance.Container.Inject(this);

            EnsureEventSystem();
            BuildCanvas();

            _log?.Info("TouchHud", "Built: joystick + attack + ability1 + ability2.");
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        // ---- UI construction ---------------------------------------------------

        private static void EnsureEventSystem()
        {
            if (FindFirstObjectByType<EventSystem>() != null) return;

            // Use the new Input System's UI module — the project has the legacy module
            // disabled, so StandaloneInputModule wouldn't dispatch events.
            new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
        }

        private void BuildCanvas()
        {
            var canvasGO = new GameObject("TouchControlsCanvas",
                typeof(Canvas),
                typeof(CanvasScaler),
                typeof(GraphicRaycaster));
            canvasGO.transform.SetParent(transform, worldPositionStays: false);

            var canvas = canvasGO.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 50; // above CargoHud's IMGUI overlay (IMGUI draws on top regardless, but explicit doesn't hurt)

            var scaler = canvasGO.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = _referenceResolution;
            scaler.matchWidthOrHeight = 0.5f;

            BuildJoystick(canvasGO.transform);
            _ability1 = BuildAbilityButton(canvasGO.transform, "Ability1",
                _ability1AnchoredPosition, _abilityButtonSize, "Q / 1", isRightAligned: true,
                out _ability1CooldownOverlay);
            _ability2 = BuildAbilityButton(canvasGO.transform, "Ability2",
                _ability2AnchoredPosition, _abilityButtonSize, "E / 2", isRightAligned: true,
                out _ability2CooldownOverlay);
            _attack = BuildAttackButton(canvasGO.transform);
        }

        private void Update()
        {
            // Refresh local chicken reference if it's been despawned (death stun
            // can't despawn the chicken, but a host migration / scene reload could).
            if (_localChicken == null || _localChicken.Object == null || !_localChicken.Object.IsValid)
            {
                if (Time.unscaledTime - _localChickenLastSearch > 0.5f)
                {
                    _localChickenLastSearch = Time.unscaledTime;
                    _localChicken = FindLocalChicken();
                }
            }

            UpdateCooldownOverlay(_ability1CooldownOverlay, slot: 0);
            UpdateCooldownOverlay(_ability2CooldownOverlay, slot: 1);
            UpdateAbilityAccents();
        }

        private void UpdateAbilityAccents()
        {
            // Re-tint the ability buttons to the equipped ability's AccentColor so
            // Speed Burst vs Egg Shell vs Roll & Trample read visually distinct.
            // Only applies when the equipped SO actually changes (per spawn).
            var abilities = _localChicken != null ? _localChicken.Abilities : null;
            if (abilities == null) return;

            if (abilities.Slot0 != _appliedSlot0)
            {
                _appliedSlot0 = abilities.Slot0;
                ApplyAccent(_ability1, abilities.Slot0);
            }
            if (abilities.Slot1 != _appliedSlot1)
            {
                _appliedSlot1 = abilities.Slot1;
                ApplyAccent(_ability2, abilities.Slot1);
            }
        }

        private void ApplyAccent(HoldButton btn, AbilityBaseSO equipped)
        {
            if (btn == null) return;

            Color normal, pressed;
            if (equipped == null)
            {
                // No ability equipped → keep the neutral palette.
                normal = _colors.AbilityNormal;
                pressed = _colors.AbilityPressed;
            }
            else
            {
                // Tint with the SO's AccentColor, but preserve the scheme's
                // normal/pressed alphas so visibility stays consistent.
                normal = equipped.AccentColor;
                normal.a = _colors.AbilityNormal.a;
                pressed = equipped.AccentColor;
                pressed.a = _colors.AbilityPressed.a;
            }

            var img = btn.GetComponent<Image>();
            if (img != null) img.color = normal;
            btn.SetVisuals(img, normal, pressed);
        }

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

        private void UpdateCooldownOverlay(Image overlay, int slot)
        {
            if (overlay == null) return;

            // Default: ready (no overlay). Covers the no-chicken / no-ability-equipped case.
            float fill = 0f;

            var abilities = _localChicken != null ? _localChicken.Abilities : null;
            if (abilities != null)
            {
                var equipped = slot == 0 ? abilities.Slot0 : abilities.Slot1;
                if (equipped != null && equipped.Cooldown > 0f)
                {
                    var remaining = abilities.CooldownRemaining(slot);
                    fill = Mathf.Clamp01(remaining / equipped.Cooldown);
                }
            }

            overlay.fillAmount = fill;
        }

        private void BuildJoystick(Transform canvas)
        {
            // Base
            var baseGO = CreateUI("JoystickBase", canvas, out var baseRT);
            baseRT.anchorMin = new Vector2(0f, 0f);
            baseRT.anchorMax = new Vector2(0f, 0f);
            baseRT.pivot     = new Vector2(0.5f, 0.5f);
            baseRT.sizeDelta = new Vector2(_joystickRadius * 2f, _joystickRadius * 2f);
            baseRT.anchoredPosition = _joystickAnchoredPosition;

            var baseImg = baseGO.AddComponent<Image>();
            baseImg.color = _colors.JoystickBase;
            baseImg.raycastTarget = true;

            // Knob (child of base)
            var knobGO = CreateUI("Knob", baseGO.transform, out var knobRT);
            knobRT.anchorMin = new Vector2(0.5f, 0.5f);
            knobRT.anchorMax = new Vector2(0.5f, 0.5f);
            knobRT.pivot     = new Vector2(0.5f, 0.5f);
            knobRT.sizeDelta = new Vector2(_knobRadius * 2f, _knobRadius * 2f);
            knobRT.anchoredPosition = Vector2.zero;

            var knobImg = knobGO.AddComponent<Image>();
            knobImg.color = _colors.JoystickKnob;
            knobImg.raycastTarget = false; // base captures all input

            _joystick = baseGO.AddComponent<VirtualJoystick>();
            _joystick.SetKnob(knobRT);
            _joystick.SetMaxRadius(_joystickRadius - _knobRadius * 0.5f);
        }

        private HoldButton BuildAttackButton(Transform canvas)
        {
            var go = CreateUI("AttackButton", canvas, out var rt);
            rt.anchorMin = new Vector2(1f, 0f);
            rt.anchorMax = new Vector2(1f, 0f);
            rt.pivot     = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(_attackButtonSize, _attackButtonSize);
            rt.anchoredPosition = _attackAnchoredPosition;

            var img = go.AddComponent<Image>();
            img.color = _colors.AttackNormal;
            img.raycastTarget = true;

            CreateLabel(go.transform, "ATTACK", fontSize: 36);

            var btn = go.AddComponent<HoldButton>();
            btn.SetVisuals(img, _colors.AttackNormal, _colors.AttackPressed);
            return btn;
        }

        private HoldButton BuildAbilityButton(Transform canvas, string name, Vector2 anchoredPosition,
            float size, string label, bool isRightAligned, out Image cooldownOverlay)
        {
            var go = CreateUI(name, canvas, out var rt);
            rt.anchorMin = new Vector2(isRightAligned ? 1f : 0f, 0f);
            rt.anchorMax = new Vector2(isRightAligned ? 1f : 0f, 0f);
            rt.pivot     = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(size, size);
            rt.anchoredPosition = anchoredPosition;

            var img = go.AddComponent<Image>();
            img.color = _colors.AbilityNormal;
            img.raycastTarget = true;

            // Cooldown overlay (between background and label) — radial fill from top,
            // counter-clockwise. fillAmount = remainingCooldown / totalCooldown:
            // 1 = just activated (fully covered), 0 = ready (uncovered).
            var overlayGO = CreateUI("CooldownOverlay", go.transform, out var overlayRT);
            overlayRT.anchorMin = Vector2.zero;
            overlayRT.anchorMax = Vector2.one;
            overlayRT.offsetMin = Vector2.zero;
            overlayRT.offsetMax = Vector2.zero;
            cooldownOverlay = overlayGO.AddComponent<Image>();
            cooldownOverlay.color = _colors.CooldownDim;
            cooldownOverlay.type = Image.Type.Filled;
            cooldownOverlay.fillMethod = Image.FillMethod.Radial360;
            cooldownOverlay.fillOrigin = (int)Image.Origin360.Top;
            cooldownOverlay.fillClockwise = false;
            cooldownOverlay.fillAmount = 0f;
            cooldownOverlay.raycastTarget = false;

            CreateLabel(go.transform, label, fontSize: 28);

            var btn = go.AddComponent<HoldButton>();
            btn.SetVisuals(img, _colors.AbilityNormal, _colors.AbilityPressed);
            return btn;
        }

        private static GameObject CreateUI(string name, Transform parent, out RectTransform rt)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, worldPositionStays: false);
            rt = (RectTransform)go.transform;
            return go;
        }

        private static void CreateLabel(Transform parent, string text, int fontSize)
        {
            var go = new GameObject("Label", typeof(RectTransform));
            go.transform.SetParent(parent, worldPositionStays: false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            var label = go.AddComponent<Text>();
            label.text = text;
            label.fontSize = fontSize;
            label.alignment = TextAnchor.MiddleCenter;
            label.color = new Color(0.95f, 0.95f, 0.95f, 0.95f);
            label.fontStyle = FontStyle.Bold;
            label.horizontalOverflow = HorizontalWrapMode.Overflow;
            label.verticalOverflow = VerticalWrapMode.Overflow;
            label.raycastTarget = false;

            // Built-in fallback font — Unity 6 ships LegacyRuntime.ttf; older versions had Arial.
            var f = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (f == null) f = Resources.GetBuiltinResource<Font>("Arial.ttf");
            label.font = f;
        }
    }
}

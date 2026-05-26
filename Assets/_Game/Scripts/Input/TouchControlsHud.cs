using CluckWars.Abilities;
using CluckWars.Gameplay;
using CluckWars.Logging;
using CluckWars.UI;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;
using Zenject;

namespace CluckWars.Input
{
    /// <summary>
    /// On-screen touch controls for the Game scene: virtual joystick (left thumb)
    /// and 3 ability buttons (right thumb). The third button is only meaningful
    /// for Assassin (Combo passive) but is always visible — Maestro can hide it
    /// per-class if desired.
    /// </summary>
    /// <remarks>
    /// Always visible (PC + Android). On Windows the buttons are clickable with
    /// the mouse, which doubles as a way to test the touch flow without leaving
    /// the editor. Visuals are placeholder squares with white tint — Phase 7
    /// polish replaces them with hand-authored sprites.
    ///
    /// v0.3: attack button replaced by Ability3. AttackHeld removed; Ability3Pressed added.
    /// </remarks>
    public sealed class TouchControlsHud : MonoBehaviour
    {
        public static TouchControlsHud Instance { get; private set; }

        // --- Layout tunables (canvas units; reference resolution 1920×1080) ---
        // v0.3 design (cluckwars-hud-v3): MOBA arc — primary slot anchors at the
        // bottom-right thumb base, the rest arc up-and-left within thumb sweep.
        // 2 abilities → vertical-ish stack; 3 abilities (Assassin) → triangle.
        [Header("Layout")]
        [SerializeField] private Vector2 _referenceResolution = new Vector2(1920f, 1080f);
        [SerializeField] private float _joystickRadius = 140f;
        [SerializeField] private float _knobRadius = 64f;
        [SerializeField] private Vector2 _joystickAnchoredPosition = new Vector2(220f, 220f);
        [SerializeField] private float _abilityButtonSize = 150f;
        [Tooltip("Slot 1 (primary thumb anchor, bottom-right).")]
        [SerializeField] private Vector2 _ability1AnchoredPosition = new Vector2(-170f, 170f);
        [Tooltip("Slot 2 — arc above-left of the primary.")]
        [SerializeField] private Vector2 _ability2AnchoredPosition = new Vector2(-210f, 360f);
        [Tooltip("Slot 3 (Assassin only). Far-left of the triangle.")]
        [SerializeField] private Vector2 _ability3AnchoredPosition = new Vector2(-410f, 270f);

        // ★3 gold (Assassin's Combo slot privilege — design v3).
        private static readonly Color SlotBadgeGold = new Color(0.96f, 0.78f, 0.26f, 1f);

        // Visuals come from ColorSchemeSO — no inline literals here anymore.
        private VirtualJoystick _joystick;

        /// <summary>All per-slot UI references for one hex ability button.</summary>
        private struct AbilityBtn
        {
            public HoldButton  Button;
            public Image       BaseImg;          // tinted to AccentColor; dimmed on cooldown
            public Image       CooldownOverlay;  // radial fill
            public TMP_Text    Icon;             // emoji glyph (TMP — AbilityBaseSO.ResolveIcon)
            public Text        ShortLabel;       // ShortLabel under the icon
            public Text        CooldownNumber;   // seconds remaining, centered
        }
        private readonly AbilityBtn[] _abilityBtns = new AbilityBtn[3];

        private ChickenController _localChicken;
        private float _localChickenLastSearch;
        private ILogService _log;
        private ColorSchemeSO _colors;

        // Cached so we re-apply the ability tint/icon only when the equipped SO changes.
        private readonly AbilityBaseSO[] _appliedSlots = new AbilityBaseSO[3];

        public Vector2 Movement => _joystick != null ? _joystick.Value : Vector2.zero;
        public bool Ability1Pressed => _abilityBtns[0].Button != null && _abilityBtns[0].Button.WasPressedThisFrame;
        public bool Ability2Pressed => _abilityBtns[1].Button != null && _abilityBtns[1].Button.WasPressedThisFrame;
        public bool Ability3Pressed => _abilityBtns[2].Button != null && _abilityBtns[2].Button.WasPressedThisFrame;

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

            EnsureEventSystem();
            BuildCanvas();

            _log?.Info("TouchHud", "Built: joystick + ability1 + ability2 + ability3.");
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        // ---- UI construction ---------------------------------------------------

        private static void EnsureEventSystem()
        {
            if (FindFirstObjectByType<EventSystem>() != null) return;
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
            canvas.sortingOrder = 50;

            var scaler = canvasGO.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = _referenceResolution;
            scaler.matchWidthOrHeight = 0.5f;

            BuildJoystick(canvasGO.transform);
            _abilityBtns[0] = BuildAbilityButton(canvasGO.transform, "Ability1",
                _ability1AnchoredPosition, _abilityButtonSize, slotNum: 1);
            _abilityBtns[1] = BuildAbilityButton(canvasGO.transform, "Ability2",
                _ability2AnchoredPosition, _abilityButtonSize, slotNum: 2);
            _abilityBtns[2] = BuildAbilityButton(canvasGO.transform, "Ability3",
                _ability3AnchoredPosition, _abilityButtonSize, slotNum: 3);
        }

        private void Update()
        {
            if (_localChicken == null || _localChicken.Object == null || !_localChicken.Object.IsValid)
            {
                if (Time.unscaledTime - _localChickenLastSearch > 0.5f)
                {
                    _localChickenLastSearch = Time.unscaledTime;
                    _localChicken = FindLocalChicken();
                }
            }

            for (int slot = 0; slot < 3; slot++)
                UpdateCooldownOverlay(slot);
            UpdateAbilityAccents();
        }

        private void UpdateAbilityAccents()
        {
            var abilities = _localChicken != null ? _localChicken.Abilities : null;
            if (abilities == null) return;

            for (int slot = 0; slot < 3; slot++)
            {
                AbilityBaseSO equipped = SlotAbility(abilities, slot);
                if (equipped != _appliedSlots[slot])
                {
                    _appliedSlots[slot] = equipped;
                    ApplyAccent(slot, equipped);
                }

                // Slot 3 (the Combo slot) is only meaningful for Assassin, who is the
                // only class that equips Slot2 — hide the button when nothing's there.
                if (slot == 2 && _abilityBtns[2].Button != null)
                {
                    bool show = equipped != null;
                    var go = _abilityBtns[2].Button.gameObject;
                    if (go.activeSelf != show) go.SetActive(show);
                }
            }
        }

        private static AbilityBaseSO SlotAbility(AbilityController abilities, int slot) =>
            slot == 0 ? abilities.Slot0 : (slot == 1 ? abilities.Slot1 : abilities.Slot2);

        private void ApplyAccent(int slot, AbilityBaseSO equipped)
        {
            var refs = _abilityBtns[slot];
            if (refs.Button == null) return;

            Color normal, pressed;
            if (equipped == null)
            {
                normal  = _colors.AbilityNormal;
                pressed = _colors.AbilityPressed;
            }
            else
            {
                normal    = equipped.AccentColor;
                normal.a  = _colors.AbilityNormal.a;
                pressed   = equipped.AccentColor;
                pressed.a = _colors.AbilityPressed.a;
            }

            if (refs.BaseImg != null) refs.BaseImg.color = normal;
            refs.Button.SetVisuals(refs.BaseImg, normal, pressed);

            // Icon glyph (design v3) + short label, swapped to match the equipped ability.
            if (refs.Icon != null)
                refs.Icon.text = equipped != null ? equipped.ResolveIcon() : string.Empty;
            if (refs.ShortLabel != null)
                refs.ShortLabel.text = equipped != null
                    ? (string.IsNullOrEmpty(equipped.ShortLabel) ? equipped.DisplayName : equipped.ShortLabel)
                    : string.Empty;
            // Note: base-image alpha is overridden each frame by UpdateCooldownOverlay.
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

        private void UpdateCooldownOverlay(int slot)
        {
            var refs = _abilityBtns[slot];
            if (refs.CooldownOverlay == null) return;

            float fill      = 0f;
            float remaining = 0f;
            var abilities = _localChicken != null ? _localChicken.Abilities : null;
            if (abilities != null)
            {
                AbilityBaseSO equipped = SlotAbility(abilities, slot);
                if (equipped != null && equipped.Cooldown > 0f)
                {
                    remaining = abilities.CooldownRemaining(slot);
                    fill      = Mathf.Clamp01(remaining / equipped.Cooldown);
                }
            }

            refs.CooldownOverlay.fillAmount = fill;

            // Seconds-remaining number (design v3) — visible only while cooling down.
            if (refs.CooldownNumber != null)
            {
                bool onCd = fill > 0f;
                if (refs.CooldownNumber.gameObject.activeSelf != onCd)
                    refs.CooldownNumber.gameObject.SetActive(onCd);
                if (onCd) refs.CooldownNumber.text = Mathf.CeilToInt(remaining).ToString();
            }

            // B4: grey-out the base button (and dim the glyph) when on cooldown.
            // Alpha 0.45 on cooldown, 1.0 when ready — clear visual affordance.
            if (refs.BaseImg != null)
            {
                var c = refs.BaseImg.color;
                c.a = fill > 0f ? 0.45f : 1.0f;
                refs.BaseImg.color = c;
            }
            if (refs.Icon != null)
            {
                var c = refs.Icon.color;
                c.a = fill > 0f ? 0.35f : 1.0f;
                refs.Icon.color = c;
            }
        }

        private void BuildJoystick(Transform canvas)
        {
            var baseGO = CreateUI("JoystickBase", canvas, out var baseRT);
            baseRT.anchorMin = new Vector2(0f, 0f);
            baseRT.anchorMax = new Vector2(0f, 0f);
            baseRT.pivot     = new Vector2(0.5f, 0.5f);
            baseRT.sizeDelta = new Vector2(_joystickRadius * 2f, _joystickRadius * 2f);
            baseRT.anchoredPosition = _joystickAnchoredPosition;

            var baseImg = baseGO.AddComponent<Image>();
            baseImg.sprite = UiGfx.Circle();
            baseImg.color = _colors.JoystickBase;
            baseImg.raycastTarget = true;

            var knobGO = CreateUI("Knob", baseGO.transform, out var knobRT);
            knobRT.anchorMin = new Vector2(0.5f, 0.5f);
            knobRT.anchorMax = new Vector2(0.5f, 0.5f);
            knobRT.pivot     = new Vector2(0.5f, 0.5f);
            knobRT.sizeDelta = new Vector2(_knobRadius * 2f, _knobRadius * 2f);
            knobRT.anchoredPosition = Vector2.zero;

            var knobImg = knobGO.AddComponent<Image>();
            knobImg.sprite = UiGfx.Circle();
            knobImg.color = _colors.JoystickKnob;
            knobImg.raycastTarget = false;

            _joystick = baseGO.AddComponent<VirtualJoystick>();
            _joystick.SetKnob(knobRT);
            _joystick.SetMaxRadius(_joystickRadius - _knobRadius * 0.5f);
        }

        /// <summary>
        /// Builds one glossy ability button (design v3 §6.6 hex anatomy, adapted to
        /// UGUI): accent-tinted base + radial cooldown overlay + centered emoji icon +
        /// short label + a slot-index badge (1 / 2 / ★3) + a seconds-remaining number.
        /// </summary>
        private AbilityBtn BuildAbilityButton(Transform canvas, string name, Vector2 anchoredPosition,
            float size, int slotNum)
        {
            var refs = new AbilityBtn();

            var go = CreateUI(name, canvas, out var rt);
            rt.anchorMin = new Vector2(1f, 0f);
            rt.anchorMax = new Vector2(1f, 0f);
            rt.pivot     = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(size, size);
            rt.anchoredPosition = anchoredPosition;

            int hexPx = Mathf.RoundToInt(size);
            var img = go.AddComponent<Image>();
            img.sprite = UiGfx.Hex(hexPx);          // pointy-top glossy hex (ART.md §6.6)
            img.type   = Image.Type.Simple;
            img.color = _colors.AbilityNormal;
            img.raycastTarget = true;
            refs.BaseImg = img; // expose for the cooldown grey-out system

            // Cooldown radial overlay (hex-masked).
            var overlayGO = CreateUI("CooldownOverlay", go.transform, out var overlayRT);
            overlayRT.anchorMin = Vector2.zero;
            overlayRT.anchorMax = Vector2.one;
            overlayRT.offsetMin = Vector2.zero;
            overlayRT.offsetMax = Vector2.zero;
            var overlay = overlayGO.AddComponent<Image>();
            overlay.sprite = UiGfx.Hex(hexPx);
            overlay.color = _colors.CooldownDim;
            overlay.type = Image.Type.Filled;
            overlay.fillMethod = Image.FillMethod.Radial360;
            overlay.fillOrigin = (int)Image.Origin360.Top;
            overlay.fillClockwise = false;
            overlay.fillAmount = 0f;
            overlay.raycastTarget = false;
            refs.CooldownOverlay = overlay;

            // Emoji icon glyph (TMP — legacy Text can't render supplementary-plane emoji).
            refs.Icon = UiGfx.AddIcon(go.transform, "Icon", string.Empty, size * 0.42f, Color.white);
            var iconRT = refs.Icon.rectTransform;
            iconRT.offsetMin = new Vector2(0f, size * 0.10f);
            iconRT.offsetMax = new Vector2(0f, size * 0.10f);

            // Short label under the icon.
            refs.ShortLabel = CreateGlyph(go.transform, "ShortLabel", string.Empty,
                Mathf.RoundToInt(size * 0.16f), new Vector2(0f, -size * 0.30f));
            refs.ShortLabel.color = new Color(1f, 0.96f, 0.88f, 0.95f);

            // Seconds-remaining number — centered, shown only while on cooldown.
            refs.CooldownNumber = CreateGlyph(go.transform, "CooldownNumber", string.Empty,
                Mathf.RoundToInt(size * 0.36f), Vector2.zero);
            refs.CooldownNumber.color = new Color(1f, 0.96f, 0.88f, 1f);
            refs.CooldownNumber.gameObject.SetActive(false);

            // Slot-index badge (top-left): 1 / 2 / ★3. Gold for the Assassin combo slot.
            BuildSlotBadge(go.transform, slotNum, size);

            var btn = go.AddComponent<HoldButton>();
            btn.SetVisuals(img, _colors.AbilityNormal, _colors.AbilityPressed);
            refs.Button = btn;

            // Slot 3 (Assassin Combo) starts hidden; UpdateAbilityAccents reveals it
            // once an ability is actually equipped in slot 2.
            if (slotNum == 3) go.SetActive(false);
            return refs;
        }

        /// <summary>Small circular slot-index badge anchored to the button's top-left.</summary>
        private void BuildSlotBadge(Transform button, int slotNum, float size)
        {
            bool isCombo = slotNum == 3;
            float badge = size * 0.26f;

            var badgeGO = CreateUI("SlotBadge", button, out var badgeRT);
            badgeRT.anchorMin = new Vector2(0f, 1f);
            badgeRT.anchorMax = new Vector2(0f, 1f);
            badgeRT.pivot     = new Vector2(0.5f, 0.5f);
            badgeRT.sizeDelta = new Vector2(badge, badge);
            badgeRT.anchoredPosition = new Vector2(badge * 0.25f, -badge * 0.25f);
            var badgeImg = badgeGO.AddComponent<Image>();
            badgeImg.sprite = UiGfx.Circle();
            badgeImg.color = isCombo ? SlotBadgeGold : new Color(0.16f, 0.10f, 0.05f, 0.95f);
            badgeImg.raycastTarget = false;

            var lbl = CreateGlyph(badgeGO.transform, "Num", isCombo ? "★" : slotNum.ToString(),
                Mathf.RoundToInt(badge * 0.62f), Vector2.zero);
            if (isCombo) lbl.font = UiGfx.EmojiFont(); // ★ glyph not in Lilita One
            lbl.color = isCombo ? new Color(0.10f, 0.06f, 0.02f, 1f) : new Color(1f, 0.96f, 0.88f, 1f);
        }

        /// <summary>Creates a centered overlay Text (icon / label / number) on a button.</summary>
        private static Text CreateGlyph(Transform parent, string name, string content,
            int fontSize, Vector2 offset)
        {
            var go = CreateUI(name, parent, out var rt);
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(0f, offset.y);
            rt.offsetMax = new Vector2(0f, offset.y);

            var text = go.AddComponent<Text>();
            text.text = content;
            text.fontSize = fontSize;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = new Color(1f, 1f, 1f, 1f);
            text.fontStyle = FontStyle.Bold;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.raycastTarget = false;
            text.font = ResolveDefaultFont();
            UiGfx.AddShadow(text);
            return text;
        }

        private static GameObject CreateUI(string name, Transform parent, out RectTransform rt)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, worldPositionStays: false);
            rt = (RectTransform)go.transform;
            return go;
        }

        private static Font ResolveDefaultFont() => UiGfx.ChunkyFont();
    }
}

using System.Collections.Generic;
using CluckWars.Bootstrap;
using CluckWars.Gameplay;
using CluckWars.Logging;
using CluckWars.Services;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;
using Zenject;

namespace CluckWars.UI
{
    /// <summary>
    /// Bootstrap-scene menu. Builds an interactive UGUI canvas at runtime so the
    /// scene file stays minimal: clickable buttons for class (Warrior / Speedy / Fatty
    /// / Assassin), mode (Solo / Host / Join), and a Start button. Keyboard shortcuts
    /// (1-4 / numpad, S/H/J, Space/Enter) still work for quick dev iteration.
    /// </summary>
    /// <remarks>
    /// Self-injects from <c>ProjectContext</c> so the script works without a
    /// <c>SceneContext</c> in the Bootstrap scene. Locks the app to landscape on
    /// Awake (mobile builds also flip the manifest via <c>ProjectSettings.asset</c>).
    /// Phase 7 will replace this with a hand-authored UGUI scene + UGS lobby UI.
    /// </remarks>
    [RequireComponent(typeof(SceneLoader))]
    public sealed class CharacterSelectController : MonoBehaviour
    {
        private const string Source = "CharacterSelect";

        // --- Visual tunables ---
        private static readonly Color PanelColor    = new Color(0.08f, 0.08f, 0.10f, 0.92f);
        private static readonly Color ButtonNormal  = new Color(0.18f, 0.18f, 0.22f, 1f);
        private static readonly Color ButtonHover   = new Color(0.28f, 0.28f, 0.34f, 1f);
        private static readonly Color ButtonActive  = new Color(0.95f, 0.65f, 0.20f, 1f);  // food-orange
        private static readonly Color TextColor     = new Color(0.95f, 0.95f, 0.95f, 1f);
        private static readonly Color TextOnActive  = new Color(0.10f, 0.08f, 0.05f, 1f);
        private static readonly Color StartColor    = new Color(0.30f, 0.65f, 0.30f, 1f);

        private ISessionSelectionService _selection;
        private ILogService _log;
        private SceneLoader _sceneLoader;

        private readonly Dictionary<ChickenClass, Button> _classButtons = new();
        private readonly Dictionary<SessionMode, Button> _modeButtons = new();
        private Text _statusLabel;

        [Inject]
        public void Construct(ISessionSelectionService selection, ILogService log)
        {
            _selection = selection;
            _log = log;
        }

        private void Awake()
        {
            _sceneLoader = GetComponent<SceneLoader>();
            _sceneLoader.SetAutoLoad(false);

            // Bootstrap scene has no SceneContext, so [Inject] won't fire automatically.
            // Self-inject from ProjectContext — accessing .Instance triggers its lazy
            // instantiation, so this works on cold start before any other scene component
            // has touched the container.
            if (_log == null)
            {
                ProjectContext.Instance.Container.Inject(this);
            }

            if (_log == null)
            {
                Debug.LogError(
                    "[CharacterSelect] ILogService injection failed — ProjectContext likely not loaded. " +
                    "Check that Assets/_Game/Resources/ProjectContext.prefab has ProjectInstaller in its Mono Installers list.");
                return;
            }

            // Landscape lock — both orientations allowed so a phone autorotates between them.
            // ProjectSettings.asset also restricts the build manifest; this is the runtime guarantee.
            Screen.autorotateToPortrait = false;
            Screen.autorotateToPortraitUpsideDown = false;
            Screen.autorotateToLandscapeLeft = true;
            Screen.autorotateToLandscapeRight = true;
            if (Screen.orientation == ScreenOrientation.Portrait || Screen.orientation == ScreenOrientation.PortraitUpsideDown)
                Screen.orientation = ScreenOrientation.LandscapeLeft;

            EnsureEventSystem();
            BuildCanvas();
            RefreshSelectionVisuals();

            _log.Info(Source,
                _selection != null
                    ? $"Awake. Initial selection class={_selection.SelectedClass}, mode={_selection.Mode}, session='{_selection.SessionName}'."
                    : "Awake but ISessionSelectionService injection FAILED. Buttons / keys will be ignored.");
        }

        private void Update()
        {
            var kb = Keyboard.current;
            if (kb == null || _selection == null) return;

            // Accept both top-row digit keys and numpad — Spanish keyboards in particular
            // report shifted top-row digits oddly, and full-size keyboards default to numpad.
            if (kb.digit1Key.wasPressedThisFrame || kb.numpad1Key.wasPressedThisFrame) SelectClass(ChickenClass.Warrior, "1");
            else if (kb.digit2Key.wasPressedThisFrame || kb.numpad2Key.wasPressedThisFrame) SelectClass(ChickenClass.Speedy, "2");
            else if (kb.digit3Key.wasPressedThisFrame || kb.numpad3Key.wasPressedThisFrame) SelectClass(ChickenClass.Fatty, "3");
            else if (kb.digit4Key.wasPressedThisFrame || kb.numpad4Key.wasPressedThisFrame) SelectClass(ChickenClass.Assassin, "4");

            if (kb.sKey.wasPressedThisFrame) SelectMode(SessionMode.Solo);
            else if (kb.hKey.wasPressedThisFrame) SelectMode(SessionMode.Host);
            else if (kb.jKey.wasPressedThisFrame) SelectMode(SessionMode.Join);

            if (kb.spaceKey.wasPressedThisFrame || kb.enterKey.wasPressedThisFrame)
            {
                Confirm("keyboard");
            }
        }

        // ---- Selection plumbing ------------------------------------------------

        private void SelectClass(ChickenClass cls, string source)
        {
            if (_selection == null) return;
            _selection.SelectedClass = cls;
            _log?.Debug(Source, $"{source} → SelectedClass = {cls}.");
            RefreshSelectionVisuals();
        }

        private void SelectMode(SessionMode mode)
        {
            if (_selection == null) return;
            _selection.Mode = mode;
            _log?.Debug(Source, $"Mode = {mode} (session='{_selection.SessionName}').");
            RefreshSelectionVisuals();
        }

        private void Confirm(string source)
        {
            if (_selection == null) return;
            _log?.Info(Source,
                $"Confirm ({source}): class={_selection.SelectedClass}, mode={_selection.Mode}, session='{_selection.SessionName}'.");
            _sceneLoader.LoadNext();
        }

        private void RefreshSelectionVisuals()
        {
            if (_selection == null) return;

            foreach (var kv in _classButtons)
                ApplyButtonState(kv.Value, isActive: kv.Key == _selection.SelectedClass);

            foreach (var kv in _modeButtons)
                ApplyButtonState(kv.Value, isActive: kv.Key == _selection.Mode);

            if (_statusLabel != null)
                _statusLabel.text = $"Class: {_selection.SelectedClass}    Mode: {_selection.Mode}    Session: \"{_selection.SessionName}\"";
        }

        private static void ApplyButtonState(Button button, bool isActive)
        {
            if (button == null) return;
            var img = button.GetComponent<Image>();
            if (img != null) img.color = isActive ? ButtonActive : ButtonNormal;

            var text = button.GetComponentInChildren<Text>();
            if (text != null) text.color = isActive ? TextOnActive : TextColor;

            // Tweak the ColorBlock so hover doesn't visually overwrite the active state.
            var colors = button.colors;
            colors.normalColor      = isActive ? ButtonActive : ButtonNormal;
            colors.highlightedColor = isActive ? ButtonActive : ButtonHover;
            colors.pressedColor     = ButtonHover;
            colors.selectedColor    = isActive ? ButtonActive : ButtonHover;
            button.colors = colors;
        }

        // ---- UI construction ---------------------------------------------------

        private static void EnsureEventSystem()
        {
            if (FindFirstObjectByType<EventSystem>() != null) return;

            var go = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
            // EventSystem auto-survives scene loads if we want, but Bootstrap → Game replaces
            // the scene single-mode, so we don't need DontDestroyOnLoad here.
        }

        private void BuildCanvas()
        {
            // Canvas
            var canvasGO = new GameObject("BootstrapMenuCanvas",
                typeof(Canvas),
                typeof(CanvasScaler),
                typeof(GraphicRaycaster));
            canvasGO.transform.SetParent(transform, worldPositionStays: false);

            var canvas = canvasGO.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            var scaler = canvasGO.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            // Center panel — anchored mid-screen, content layout grows vertically.
            // Sum of children + spacing + padding lands around 644 px; a ContentSizeFitter
            // keeps the panel honest if anyone changes the row count / sizes later.
            var panel = CreateUIObject("Panel", canvasGO.transform, out var panelRT);
            var panelImg = panel.AddComponent<Image>();
            panelImg.color = PanelColor;
            panelRT.anchorMin = new Vector2(0.5f, 0.5f);
            panelRT.anchorMax = new Vector2(0.5f, 0.5f);
            panelRT.pivot     = new Vector2(0.5f, 0.5f);
            panelRT.sizeDelta = new Vector2(960f, 680f);  // baseline; ContentSizeFitter overrides height
            panelRT.anchoredPosition = Vector2.zero;

            var panelLayout = panel.AddComponent<VerticalLayoutGroup>();
            panelLayout.padding = new RectOffset(40, 40, 32, 32);
            panelLayout.spacing = 18f;
            panelLayout.childAlignment = TextAnchor.UpperCenter;
            panelLayout.childForceExpandWidth = true;
            panelLayout.childForceExpandHeight = false;
            panelLayout.childControlWidth = true;
            panelLayout.childControlHeight = true;

            // Auto-grow vertically so we don't have to keep the magic-number height
            // in sync with the row inventory by hand. Width stays at 960.
            var panelFitter = panel.AddComponent<ContentSizeFitter>();
            panelFitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            panelFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            // Title
            var title = CreateText("Title", panel.transform, "Cluck Wars", fontSize: 56, alignment: TextAnchor.MiddleCenter);
            title.fontStyle = FontStyle.Bold;
            var titleLE = title.gameObject.AddComponent<LayoutElement>();
            titleLE.minHeight = 78f;

            var subtitle = CreateText("Subtitle", panel.transform, "Pick a chicken and a session mode", fontSize: 24, alignment: TextAnchor.MiddleCenter);
            var subLE = subtitle.gameObject.AddComponent<LayoutElement>();
            subLE.minHeight = 40f;

            // Class section
            CreateSectionLabel(panel.transform, "CLASS");
            var classRow = CreateHorizontalRow(panel.transform, "ClassRow");
            _classButtons.Clear();
            _classButtons[ChickenClass.Warrior]  = CreateChoiceButton(classRow.transform, "[1] Warrior",  () => SelectClass(ChickenClass.Warrior,  "click"));
            _classButtons[ChickenClass.Speedy]   = CreateChoiceButton(classRow.transform, "[2] Speedy",   () => SelectClass(ChickenClass.Speedy,   "click"));
            _classButtons[ChickenClass.Fatty]    = CreateChoiceButton(classRow.transform, "[3] Fatty",    () => SelectClass(ChickenClass.Fatty,    "click"));
            _classButtons[ChickenClass.Assassin] = CreateChoiceButton(classRow.transform, "[4] Assassin", () => SelectClass(ChickenClass.Assassin, "click"));

            // Mode section
            CreateSectionLabel(panel.transform, "SESSION");
            var modeRow = CreateHorizontalRow(panel.transform, "ModeRow");
            _modeButtons.Clear();
            _modeButtons[SessionMode.Solo] = CreateChoiceButton(modeRow.transform, "[S] Solo", () => SelectMode(SessionMode.Solo));
            _modeButtons[SessionMode.Host] = CreateChoiceButton(modeRow.transform, "[H] Host", () => SelectMode(SessionMode.Host));
            _modeButtons[SessionMode.Join] = CreateChoiceButton(modeRow.transform, "[J] Join", () => SelectMode(SessionMode.Join));

            // Status
            _statusLabel = CreateText("Status", panel.transform, "", fontSize: 22, alignment: TextAnchor.MiddleCenter);
            var statusLE = _statusLabel.gameObject.AddComponent<LayoutElement>();
            statusLE.minHeight = 36f;

            // Start button
            var startBtn = CreateButton(panel.transform, "Start Match  (SPACE)", () => Confirm("click"));
            var startImg = startBtn.GetComponent<Image>();
            if (startImg != null) startImg.color = StartColor;
            var startLE = startBtn.gameObject.AddComponent<LayoutElement>();
            startLE.minHeight = 72f;
            startLE.preferredHeight = 72f;
            var startColors = startBtn.colors;
            startColors.normalColor      = StartColor;
            startColors.highlightedColor = new Color(0.40f, 0.78f, 0.40f, 1f);
            startColors.pressedColor     = new Color(0.22f, 0.50f, 0.22f, 1f);
            startColors.selectedColor    = StartColor;
            startBtn.colors = startColors;
        }

        // ---- UGUI builders -----------------------------------------------------

        private static GameObject CreateUIObject(string name, Transform parent, out RectTransform rt)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, worldPositionStays: false);
            rt = (RectTransform)go.transform;
            return go;
        }

        private static Text CreateText(string name, Transform parent, string content, int fontSize, TextAnchor alignment)
        {
            var go = CreateUIObject(name, parent, out _);
            var text = go.AddComponent<Text>();
            text.text = content;
            text.fontSize = fontSize;
            text.color = TextColor;
            text.alignment = alignment;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.font = ResolveDefaultFont();
            return text;
        }

        private static void CreateSectionLabel(Transform parent, string label)
        {
            var t = CreateText(label + "Label", parent, label, fontSize: 22, alignment: TextAnchor.MiddleLeft);
            t.color = new Color(0.7f, 0.7f, 0.75f, 1f);
            var le = t.gameObject.AddComponent<LayoutElement>();
            le.minHeight = 30f;
        }

        private static GameObject CreateHorizontalRow(Transform parent, string name)
        {
            var go = CreateUIObject(name, parent, out _);
            var layout = go.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 12f;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = true;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            var le = go.AddComponent<LayoutElement>();
            le.minHeight = 84f;
            return go;
        }

        private static Button CreateChoiceButton(Transform parent, string label, System.Action onClick)
        {
            var btn = CreateButton(parent, label, onClick);
            var le = btn.gameObject.AddComponent<LayoutElement>();
            le.minHeight = 84f;
            return btn;
        }

        private static Button CreateButton(Transform parent, string label, System.Action onClick)
        {
            var go = CreateUIObject("Btn_" + label, parent, out _);
            var img = go.AddComponent<Image>();
            img.color = ButtonNormal;
            img.raycastTarget = true;

            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
            var colors = btn.colors;
            colors.normalColor      = ButtonNormal;
            colors.highlightedColor = ButtonHover;
            colors.pressedColor     = ButtonHover;
            colors.selectedColor    = ButtonHover;
            colors.disabledColor    = ButtonNormal;
            btn.colors = colors;

            btn.onClick.AddListener(() => onClick?.Invoke());

            // Label as child
            var label_ = CreateText("Label", go.transform, label, fontSize: 28, alignment: TextAnchor.MiddleCenter);
            var labelRT = label_.rectTransform;
            labelRT.anchorMin = Vector2.zero;
            labelRT.anchorMax = Vector2.one;
            labelRT.offsetMin = Vector2.zero;
            labelRT.offsetMax = Vector2.zero;

            return btn;
        }

        // Built-in fonts: Unity 2023+ ships LegacyRuntime.ttf as the Arial replacement.
        // Older versions used Arial.ttf. We try both, and fall back to null (Unity will
        // pick a default) so the menu doesn't crash on unfamiliar editor builds.
        private static Font ResolveDefaultFont()
        {
            var f = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (f == null) f = Resources.GetBuiltinResource<Font>("Arial.ttf");
            return f;
        }
    }
}

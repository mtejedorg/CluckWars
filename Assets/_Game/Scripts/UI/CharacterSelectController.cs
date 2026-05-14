using System;
using System.Collections.Generic;
using System.Threading.Tasks;
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
    /// Bootstrap-scene menu. Builds an interactive UGUI canvas at runtime.
    /// Phase 10: integrates UGS Lobby — Host creates a lobby and receives a
    /// shareable join code; Joiners enter the code or pick from the browser.
    /// The join code is used as the Photon Fusion session name.
    /// </summary>
    [RequireComponent(typeof(SceneLoader))]
    public sealed class CharacterSelectController : MonoBehaviour
    {
        private const string Source = "CharacterSelect";

        // ---- Injected --------------------------------------------------
        private ISessionSelectionService _selection;
        private ILogService              _log;
        private ColorSchemeSO            _colors;
        private IUGSService              _ugs;
        private SceneLoader              _sceneLoader;

        // ---- Class / mode button maps ----------------------------------
        private readonly Dictionary<ChickenClass, Button> _classButtons = new();
        private readonly Dictionary<SessionMode, Button>  _modeButtons  = new();

        // ---- Dynamic labels --------------------------------------------
        private Text   _statusLabel;
        private Button _actionButton;
        private Text   _actionButtonText;

        // ---- Host-specific UI ------------------------------------------
        private GameObject _hostSection;
        private InputField _lobbyNameInput;

        // ---- Join-specific UI ------------------------------------------
        private GameObject _joinSection;
        private InputField _joinCodeInput;

        // ---- Code-display (shown after lobby creation) -----------------
        private GameObject _codeSection;
        private Text       _codeText;

        // ---- Lobby browser overlay -------------------------------------
        private GameObject _lobbyBrowserPanel;
        private Transform  _lobbyListContent;
        private Text       _lobbyBrowserStatus;

        // ---- State -----------------------------------------------------
        private bool _isBusy;
        // True after lobby creation; makes the next Confirm() call load the game instead of recreating the lobby.
        private bool _lobbyReady;

        // ---- Injection -------------------------------------------------

        [Inject]
        public void Construct(
            ISessionSelectionService selection,
            ILogService log,
            ColorSchemeSO colors,
            IUGSService ugs)
        {
            _selection = selection;
            _log       = log;
            _colors    = colors;
            _ugs       = ugs;
        }

        // ---- Unity lifecycle -------------------------------------------

        private void Awake()
        {
            _sceneLoader = GetComponent<SceneLoader>();
            _sceneLoader.SetAutoLoad(false);

            if (_log == null)
                ProjectContext.Instance.Container.Inject(this);

            if (_log == null)
            {
                Debug.LogError(
                    "[CharacterSelect] ILogService injection failed — " +
                    "check that ProjectContext.prefab has ProjectInstaller in its Mono Installers list.");
                return;
            }

            Screen.autorotateToPortrait          = false;
            Screen.autorotateToPortraitUpsideDown = false;
            Screen.autorotateToLandscapeLeft      = true;
            Screen.autorotateToLandscapeRight     = true;
            if (Screen.orientation == ScreenOrientation.Portrait ||
                Screen.orientation == ScreenOrientation.PortraitUpsideDown)
                Screen.orientation = ScreenOrientation.LandscapeLeft;

            // Clean up any previous lobby session from a completed match.
            if (_ugs?.CurrentLobby != null)
                _ = _ugs.LeaveLobbyAsync();

            EnsureEventSystem();
            BuildCanvas();
            RefreshModeSection();

            _log.Info(Source,
                _selection != null
                    ? $"Awake. class={_selection.SelectedClass} mode={_selection.Mode} session='{_selection.SessionName}'."
                    : "Awake but ISessionSelectionService injection FAILED.");
        }

        private void Update()
        {
            var kb = Keyboard.current;
            if (kb == null || _selection == null || _isBusy) return;

            if (kb.digit1Key.wasPressedThisFrame || kb.numpad1Key.wasPressedThisFrame) SelectClass(ChickenClass.Warrior,  "1");
            else if (kb.digit2Key.wasPressedThisFrame || kb.numpad2Key.wasPressedThisFrame) SelectClass(ChickenClass.Speedy,   "2");
            else if (kb.digit3Key.wasPressedThisFrame || kb.numpad3Key.wasPressedThisFrame) SelectClass(ChickenClass.Fatty,    "3");
            else if (kb.digit4Key.wasPressedThisFrame || kb.numpad4Key.wasPressedThisFrame) SelectClass(ChickenClass.Assassin, "4");

            if (kb.sKey.wasPressedThisFrame)      SelectMode(SessionMode.Solo);
            else if (kb.hKey.wasPressedThisFrame) SelectMode(SessionMode.Host);
            else if (kb.jKey.wasPressedThisFrame) SelectMode(SessionMode.Join);

            // SPACE / Enter trigger the action only when no InputField is focused.
            if (kb.spaceKey.wasPressedThisFrame || kb.enterKey.wasPressedThisFrame)
            {
                var focusedInput = EventSystem.current?.currentSelectedGameObject?.GetComponent<InputField>();
                if (focusedInput == null)
                    Confirm("keyboard");
            }
        }

        // ---- Selection -------------------------------------------------

        private void SelectClass(ChickenClass cls, string source)
        {
            if (_selection == null) return;
            _selection.SelectedClass = cls;
            _log?.Debug(Source, $"{source} → SelectedClass={cls}.");
            RefreshClassVisuals();
        }

        private void SelectMode(SessionMode mode)
        {
            if (_selection == null || _isBusy) return;
            _selection.Mode = mode;
            _lobbyReady = false;   // reset so switching modes doesn't carry over a stale lobby
            _codeSection?.SetActive(false);
            SetBusy(false, null);
            _log?.Debug(Source, $"Mode={mode}.");
            RefreshModeSection();
        }

        private void Confirm(string source)
        {
            if (_selection == null || _isBusy) return;

            // Lobby already created — just load the game scene regardless of mode.
            if (_lobbyReady)
            {
                _log?.Info(Source, $"Confirm ({source}): lobby ready, loading game. session='{_selection.SessionName}'.");
                _sceneLoader.LoadNext();
                return;
            }

            _log?.Info(Source,
                $"Confirm ({source}): class={_selection.SelectedClass} mode={_selection.Mode} session='{_selection.SessionName}'.");

            switch (_selection.Mode)
            {
                case SessionMode.Solo: _sceneLoader.LoadNext(); break;
                case SessionMode.Host: _ = CreateLobbyAndProceed(); break;
                case SessionMode.Join: _ = JoinByCodeAndProceed(); break;
            }
        }

        // ---- Async lobby flows -----------------------------------------

        private async Task CreateLobbyAndProceed()
        {
            SetBusy(true, "Creating lobby...");
            try
            {
                var name = _lobbyNameInput?.text?.Trim();
                var info = await _ugs.CreateLobbyAsync(name, 4);
                _selection.SessionName = info.JoinCode;
                ShowCodeSection(info.JoinCode);
                _log?.Info(Source, $"Lobby created. Code={info.JoinCode}. Showing code to host.");
            }
            catch (Exception e)
            {
                _log?.Error(Source, $"CreateLobby failed: {e.Message}");
                SetBusy(false, $"Error: {e.Message}");
            }
        }

        private async Task JoinByCodeAndProceed()
        {
            var code = _joinCodeInput?.text?.Trim();
            if (string.IsNullOrEmpty(code))
            {
                SetBusy(false, "Enter a join code first.");
                return;
            }
            SetBusy(true, "Joining...");
            try
            {
                var info = await _ugs.JoinLobbyByCodeAsync(code);
                _selection.SessionName = info.JoinCode;
                _log?.Info(Source, $"Joined lobby. Code={info.JoinCode}. Loading game.");
                _sceneLoader.LoadNext();
            }
            catch (Exception e)
            {
                _log?.Error(Source, $"JoinByCode failed: {e.Message}");
                SetBusy(false, $"Error: {e.Message}");
            }
        }

        private async Task JoinFromBrowserAsync(LobbyInfo info)
        {
            _lobbyBrowserPanel?.SetActive(false);
            SetBusy(true, $"Joining '{info.LobbyName}'...");
            try
            {
                var joined = await _ugs.JoinLobbyAsync(info);
                _selection.SessionName = joined.JoinCode;
                _log?.Info(Source, $"Joined lobby from browser. Code={joined.JoinCode}. Loading game.");
                _sceneLoader.LoadNext();
            }
            catch (Exception e)
            {
                _log?.Error(Source, $"JoinFromBrowser failed: {e.Message}");
                SetBusy(false, $"Error: {e.Message}");
            }
        }

        // ---- Lobby browser ---------------------------------------------

        private void OpenLobbyBrowser()
        {
            if (_lobbyBrowserPanel == null) return;
            _lobbyBrowserPanel.SetActive(true);
            _ = RefreshLobbyListAsync();
        }

        private async Task RefreshLobbyListAsync()
        {
            ClearLobbyList();
            SetBrowserStatus("Loading...");
            try
            {
                var lobbies = await _ugs.QueryLobbiesAsync();
                ClearLobbyList();
                if (lobbies.Count == 0)
                {
                    SetBrowserStatus("No open lobbies found.");
                }
                else
                {
                    SetBrowserStatus(string.Empty);
                    foreach (var lobby in lobbies)
                        AddLobbyEntry(lobby);
                }
            }
            catch (Exception e)
            {
                SetBrowserStatus($"Error: {e.Message}");
            }
        }

        private void ClearLobbyList()
        {
            if (_lobbyListContent == null) return;
            foreach (Transform child in _lobbyListContent)
                Destroy(child.gameObject);
        }

        private void SetBrowserStatus(string msg)
        {
            if (_lobbyBrowserStatus != null) _lobbyBrowserStatus.text = msg;
        }

        private void AddLobbyEntry(LobbyInfo info)
        {
            var row = new GameObject("LobbyEntry_" + info.JoinCode, typeof(RectTransform));
            row.transform.SetParent(_lobbyListContent, worldPositionStays: false);
            var rowImg = row.AddComponent<Image>();
            rowImg.color = new Color(0.18f, 0.18f, 0.22f, 1f);
            var rowLayout = row.AddComponent<HorizontalLayoutGroup>();
            rowLayout.padding = new RectOffset(12, 8, 4, 4);
            rowLayout.spacing = 8f;
            rowLayout.childAlignment  = TextAnchor.MiddleLeft;
            rowLayout.childForceExpandWidth  = false;
            rowLayout.childForceExpandHeight = true;
            rowLayout.childControlWidth  = true;
            rowLayout.childControlHeight = true;
            var rowLE = row.AddComponent<LayoutElement>();
            rowLE.minHeight = 52f;

            // Name (flex)
            var nameText = CreateText("Name", row.transform, info.LobbyName, 24, TextAnchor.MiddleLeft);
            var nameLE = nameText.gameObject.AddComponent<LayoutElement>();
            nameLE.flexibleWidth = 1f;

            // Player count
            var countText = CreateText("Count", row.transform,
                $"{info.PlayerCount}/{info.MaxPlayers}", 22, TextAnchor.MiddleCenter);
            var countLE = countText.gameObject.AddComponent<LayoutElement>();
            countLE.minWidth       = 60f;
            countLE.preferredWidth = 60f;

            // Join button
            var capturedInfo = info;
            var joinBtn = CreateButton(row.transform, "Join", () => _ = JoinFromBrowserAsync(capturedInfo));
            var joinLE = joinBtn.gameObject.AddComponent<LayoutElement>();
            joinLE.minWidth       = 80f;
            joinLE.preferredWidth = 80f;
        }

        // ---- State helpers ---------------------------------------------

        private void SetBusy(bool busy, string statusText)
        {
            _isBusy = busy;
            if (_actionButton != null) _actionButton.interactable = !busy;
            if (statusText != null && _statusLabel != null)
                _statusLabel.text = statusText;
        }

        private void ShowCodeSection(string code)
        {
            _lobbyReady = true;   // next Confirm() → LoadNext(), not CreateLobby again
            _isBusy     = false;
            if (_actionButton != null)     _actionButton.interactable = true;
            if (_codeSection != null)      _codeSection.SetActive(true);
            if (_codeText != null)         _codeText.text = code;
            if (_actionButtonText != null) _actionButtonText.text = "Continue  (SPACE)";
            if (_statusLabel != null)
                _statusLabel.text = "Share this code, then click Continue when ready.";
        }

        // ---- Visual refresh --------------------------------------------

        private void RefreshClassVisuals()
        {
            foreach (var kv in _classButtons)
                ApplyButtonState(kv.Value, kv.Key == _selection.SelectedClass);
        }

        private void RefreshModeSection()
        {
            if (_selection == null) return;

            foreach (var kv in _modeButtons)
                ApplyButtonState(kv.Value, kv.Key == _selection.Mode);

            // Show/hide mode-specific sections.
            _hostSection?.SetActive(_selection.Mode == SessionMode.Host);
            _joinSection?.SetActive(_selection.Mode == SessionMode.Join);
            _codeSection?.SetActive(false);

            // Action button label.
            if (_actionButtonText != null)
            {
                _actionButtonText.text = _selection.Mode switch
                {
                    SessionMode.Host => "Create Lobby  (SPACE)",
                    SessionMode.Join => "Join by Code  (SPACE)",
                    _                => "Start Match   (SPACE)",
                };
            }

            if (_statusLabel != null)
                _statusLabel.text =
                    $"Class: {_selection.SelectedClass}    Mode: {_selection.Mode}";
        }

        private void ApplyButtonState(Button button, bool isActive)
        {
            if (button == null) return;
            var img = button.GetComponent<Image>();
            if (img != null) img.color = isActive ? _colors.ButtonActive : _colors.ButtonNormal;

            var text = button.GetComponentInChildren<Text>();
            if (text != null) text.color = isActive ? _colors.TextOnActive : _colors.TextOnButton;

            var cb = button.colors;
            cb.normalColor      = isActive ? _colors.ButtonActive : _colors.ButtonNormal;
            cb.highlightedColor = isActive ? _colors.ButtonActive : _colors.ButtonHover;
            cb.pressedColor     = _colors.ButtonHover;
            cb.selectedColor    = isActive ? _colors.ButtonActive : _colors.ButtonHover;
            button.colors = cb;
        }

        // ---- UI construction -------------------------------------------

        private static void EnsureEventSystem()
        {
            if (FindFirstObjectByType<EventSystem>() != null) return;
            new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
        }

        private void BuildCanvas()
        {
            // Main canvas
            var canvasGO = new GameObject("BootstrapMenuCanvas",
                typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasGO.transform.SetParent(transform, worldPositionStays: false);
            var canvas = canvasGO.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasGO.GetComponent<CanvasScaler>();
            scaler.uiScaleMode        = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight  = 0.5f;

            // Center panel
            var panel = CreateUIObject("Panel", canvasGO.transform, out var panelRT);
            panel.AddComponent<Image>().color = _colors.PanelBackground;
            panelRT.anchorMin        = new Vector2(0.5f, 0.5f);
            panelRT.anchorMax        = new Vector2(0.5f, 0.5f);
            panelRT.pivot            = new Vector2(0.5f, 0.5f);
            panelRT.sizeDelta        = new Vector2(960f, 700f);
            panelRT.anchoredPosition = Vector2.zero;
            var panelLayout = panel.AddComponent<VerticalLayoutGroup>();
            panelLayout.padding              = new RectOffset(40, 40, 32, 32);
            panelLayout.spacing              = 16f;
            panelLayout.childAlignment       = TextAnchor.UpperCenter;
            panelLayout.childForceExpandWidth = true;
            panelLayout.childForceExpandHeight = false;
            panelLayout.childControlWidth    = true;
            panelLayout.childControlHeight   = true;
            var panelFitter = panel.AddComponent<ContentSizeFitter>();
            panelFitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            panelFitter.verticalFit   = ContentSizeFitter.FitMode.PreferredSize;

            // Title
            var title = CreateText("Title", panel.transform, "Cluck Wars", 56, TextAnchor.MiddleCenter);
            title.fontStyle = FontStyle.Bold;
            title.gameObject.AddComponent<LayoutElement>().minHeight = 72f;

            // CLASS
            CreateSectionLabel(panel.transform, "CLASS");
            var classRow = CreateRow(panel.transform, "ClassRow", 84f, 12f);
            _classButtons.Clear();
            _classButtons[ChickenClass.Warrior]  = CreateChoiceButton(classRow.transform, "[1] Warrior",  () => SelectClass(ChickenClass.Warrior,  "click"));
            _classButtons[ChickenClass.Speedy]   = CreateChoiceButton(classRow.transform, "[2] Speedy",   () => SelectClass(ChickenClass.Speedy,   "click"));
            _classButtons[ChickenClass.Fatty]    = CreateChoiceButton(classRow.transform, "[3] Fatty",    () => SelectClass(ChickenClass.Fatty,    "click"));
            _classButtons[ChickenClass.Assassin] = CreateChoiceButton(classRow.transform, "[4] Assassin", () => SelectClass(ChickenClass.Assassin, "click"));

            // SESSION
            CreateSectionLabel(panel.transform, "SESSION");
            var modeRow = CreateRow(panel.transform, "ModeRow", 84f, 12f);
            _modeButtons.Clear();
            _modeButtons[SessionMode.Solo] = CreateChoiceButton(modeRow.transform, "[S] Solo", () => SelectMode(SessionMode.Solo));
            _modeButtons[SessionMode.Host] = CreateChoiceButton(modeRow.transform, "[H] Host", () => SelectMode(SessionMode.Host));
            _modeButtons[SessionMode.Join] = CreateChoiceButton(modeRow.transform, "[J] Join", () => SelectMode(SessionMode.Join));

            // Host section (lobby name input)
            _hostSection = CreateRow(panel.transform, "HostSection", 52f, 12f);
            CreateSectionLabel(_hostSection.transform, "LOBBY NAME");
            _hostSection.GetComponent<LayoutElement>().minHeight = 52f;
            _lobbyNameInput = CreateInputField(_hostSection.transform, "Cluck Wars (optional)", 52f);
            _hostSection.SetActive(false);

            // Join section (code input + Browse button)
            _joinSection = CreateRow(panel.transform, "JoinSection", 52f, 12f);
            CreateSectionLabel(_joinSection.transform, "CODE");
            _joinCodeInput = CreateInputField(_joinSection.transform, "e.g. ABC123", 52f);
            _joinCodeInput.characterLimit = 12;
            _joinCodeInput.onEndEdit.AddListener(s => { if (!string.IsNullOrEmpty(s)) Confirm("enter-key"); });
            var browseBtn = CreateButton(_joinSection.transform, "Browse", OpenLobbyBrowser);
            var browseLE = browseBtn.gameObject.AddComponent<LayoutElement>();
            browseLE.minWidth       = 130f;
            browseLE.preferredWidth = 130f;
            _joinSection.SetActive(false);

            // Code display section (shown after lobby created as host)
            _codeSection = CreateRow(panel.transform, "CodeSection", 64f, 16f);
            var codeSectionLayout = _codeSection.GetComponent<HorizontalLayoutGroup>();
            codeSectionLayout.childAlignment = TextAnchor.MiddleCenter;
            CreateText("CodeLabel", _codeSection.transform, "Your code:", 26, TextAnchor.MiddleLeft)
                .gameObject.AddComponent<LayoutElement>().minWidth = 140f;
            _codeText = CreateText("CodeValue", _codeSection.transform, "", 40, TextAnchor.MiddleLeft);
            _codeText.fontStyle = FontStyle.Bold;
            _codeText.color = new Color(0.4f, 1f, 0.5f, 1f);
            _codeText.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
            _codeSection.SetActive(false);

            // Status label
            _statusLabel = CreateText("Status", panel.transform, "", 22, TextAnchor.MiddleCenter);
            _statusLabel.gameObject.AddComponent<LayoutElement>().minHeight = 36f;

            // Action button
            _actionButton = CreateButton(panel.transform, "Start Match  (SPACE)", () => Confirm("click"));
            _actionButtonText = _actionButton.GetComponentInChildren<Text>();
            var actionImg = _actionButton.GetComponent<Image>();
            if (actionImg != null) actionImg.color = _colors.StartButton;
            var actionLE = _actionButton.gameObject.AddComponent<LayoutElement>();
            actionLE.minHeight       = 72f;
            actionLE.preferredHeight = 72f;
            var startBase   = _colors.StartButton;
            var actionColors = _actionButton.colors;
            actionColors.normalColor      = startBase;
            actionColors.highlightedColor = new Color(startBase.r * 1.3f, startBase.g * 1.2f, startBase.b * 1.3f, startBase.a);
            actionColors.pressedColor     = new Color(startBase.r * 0.75f, startBase.g * 0.75f, startBase.b * 0.75f, startBase.a);
            actionColors.selectedColor    = startBase;
            _actionButton.colors = actionColors;

            // Lobby browser (separate high-sort-order canvas so it overlays everything)
            BuildLobbyBrowserPanel(canvasGO.transform);

            RefreshClassVisuals();
        }

        private void BuildLobbyBrowserPanel(Transform canvasParent)
        {
            // Overlay canvas with higher sort order.
            var overlayGO = new GameObject("LobbyBrowserOverlay",
                typeof(Canvas), typeof(GraphicRaycaster));
            overlayGO.transform.SetParent(canvasParent.parent, worldPositionStays: false);
            var overlayCanvas = overlayGO.GetComponent<Canvas>();
            overlayCanvas.renderMode  = RenderMode.ScreenSpaceOverlay;
            overlayCanvas.sortingOrder = 10;

            // Semi-transparent backdrop (blocks clicks on the main menu).
            var backdrop = CreateUIObject("Backdrop", overlayGO.transform, out var backdropRT);
            backdrop.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.75f);
            backdropRT.anchorMin = Vector2.zero;
            backdropRT.anchorMax = Vector2.one;
            backdropRT.offsetMin = Vector2.zero;
            backdropRT.offsetMax = Vector2.zero;

            // Centered browser panel.
            var panel = CreateUIObject("BrowserPanel", overlayGO.transform, out var panelRT);
            panel.AddComponent<Image>().color = _colors.PanelBackground;
            panelRT.anchorMin        = new Vector2(0.5f, 0.5f);
            panelRT.anchorMax        = new Vector2(0.5f, 0.5f);
            panelRT.pivot            = new Vector2(0.5f, 0.5f);
            panelRT.sizeDelta        = new Vector2(860f, 540f);
            panelRT.anchoredPosition = Vector2.zero;
            var panelLayout = panel.AddComponent<VerticalLayoutGroup>();
            panelLayout.padding              = new RectOffset(24, 24, 20, 20);
            panelLayout.spacing              = 12f;
            panelLayout.childAlignment       = TextAnchor.UpperCenter;
            panelLayout.childForceExpandWidth = true;
            panelLayout.childForceExpandHeight = false;
            panelLayout.childControlWidth    = true;
            panelLayout.childControlHeight   = true;

            // Header row (title + close button).
            var headerRow = CreateRow(panel.transform, "HeaderRow", 48f, 8f);
            var headerLayout = headerRow.GetComponent<HorizontalLayoutGroup>();
            headerLayout.childForceExpandWidth = false;
            var headerTitle = CreateText("Title", headerRow.transform, "AVAILABLE LOBBIES", 30, TextAnchor.MiddleLeft);
            headerTitle.fontStyle = FontStyle.Bold;
            headerTitle.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
            var closeBtn = CreateButton(headerRow.transform, "✕",
                () => _lobbyBrowserPanel.SetActive(false));
            var closeBtnLE = closeBtn.gameObject.AddComponent<LayoutElement>();
            closeBtnLE.minWidth       = 48f;
            closeBtnLE.preferredWidth = 48f;

            // Browser status line.
            _lobbyBrowserStatus = CreateText("BrowserStatus", panel.transform, "", 21, TextAnchor.MiddleCenter);
            _lobbyBrowserStatus.gameObject.AddComponent<LayoutElement>().minHeight = 26f;

            // Scroll view.
            var scrollGO = CreateUIObject("ScrollRect", panel.transform, out _);
            var scrollLE = scrollGO.AddComponent<LayoutElement>();
            scrollLE.flexibleHeight = 1f;
            scrollLE.minHeight      = 300f;
            var scrollRect = scrollGO.AddComponent<ScrollRect>();
            scrollRect.horizontal = false;
            scrollRect.vertical   = true;

            var viewportGO = CreateUIObject("Viewport", scrollGO.transform, out var viewportRT);
            viewportRT.anchorMin = Vector2.zero;
            viewportRT.anchorMax = Vector2.one;
            viewportRT.offsetMin = Vector2.zero;
            viewportRT.offsetMax = Vector2.zero;
            var viewportImg = viewportGO.AddComponent<Image>();
            viewportImg.color = new Color(0f, 0f, 0f, 0.15f);
            var mask = viewportGO.AddComponent<Mask>();
            mask.showMaskGraphic = false;
            scrollRect.viewport = viewportRT;

            var contentGO = CreateUIObject("Content", viewportGO.transform, out var contentRT);
            contentRT.anchorMin = new Vector2(0f, 1f);
            contentRT.anchorMax = new Vector2(1f, 1f);
            contentRT.pivot     = new Vector2(0f, 1f);
            contentRT.offsetMin = Vector2.zero;
            contentRT.offsetMax = Vector2.zero;
            var contentLayout = contentGO.AddComponent<VerticalLayoutGroup>();
            contentLayout.padding              = new RectOffset(4, 4, 4, 4);
            contentLayout.spacing              = 5f;
            contentLayout.childForceExpandWidth = true;
            contentLayout.childForceExpandHeight = false;
            contentLayout.childControlWidth    = true;
            contentLayout.childControlHeight   = true;
            var contentFitter = contentGO.AddComponent<ContentSizeFitter>();
            contentFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            scrollRect.content = contentRT;
            _lobbyListContent = contentGO.transform;

            // Bottom row: Refresh + Back.
            var bottomRow = CreateRow(panel.transform, "BottomRow", 56f, 12f);
            CreateChoiceButton(bottomRow.transform, "Refresh", () => _ = RefreshLobbyListAsync());
            CreateChoiceButton(bottomRow.transform, "Back",    () => _lobbyBrowserPanel.SetActive(false));

            _lobbyBrowserPanel = overlayGO;
            _lobbyBrowserPanel.SetActive(false);
        }

        // ---- UGUI helpers ----------------------------------------------

        private static GameObject CreateUIObject(string name, Transform parent, out RectTransform rt)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, worldPositionStays: false);
            rt = (RectTransform)go.transform;
            return go;
        }

        private static GameObject CreateRow(Transform parent, string name, float minHeight, float spacing)
        {
            var go = CreateUIObject(name, parent, out _);
            var layout = go.AddComponent<HorizontalLayoutGroup>();
            layout.spacing              = spacing;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = true;
            layout.childControlWidth    = true;
            layout.childControlHeight   = true;
            go.AddComponent<LayoutElement>().minHeight = minHeight;
            return go;
        }

        private Text CreateText(string name, Transform parent, string content, int fontSize, TextAnchor alignment)
        {
            var go = CreateUIObject(name, parent, out _);
            var text = go.AddComponent<Text>();
            text.text              = content;
            text.fontSize          = fontSize;
            text.color             = _colors.TextOnButton;
            text.alignment         = alignment;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow   = VerticalWrapMode.Overflow;
            text.font              = ResolveDefaultFont();
            return text;
        }

        private void CreateSectionLabel(Transform parent, string label)
        {
            var t = CreateText(label + "Label", parent, label, 21, TextAnchor.MiddleLeft);
            var c = _colors.TextOnButton;
            t.color = new Color(c.r * 0.75f, c.g * 0.75f, c.b * 0.78f, c.a);
            t.gameObject.AddComponent<LayoutElement>().minHeight = 28f;
        }

        private InputField CreateInputField(Transform parent, string placeholder, float minHeight)
        {
            var go = CreateUIObject("Input", parent, out _);
            var img = go.AddComponent<Image>();
            img.color = new Color(0.12f, 0.12f, 0.15f, 1f);
            var field = go.AddComponent<InputField>();
            field.targetGraphic = img;
            go.AddComponent<LayoutElement>().minHeight = minHeight;

            // Text child
            var textGO = CreateUIObject("Text", go.transform, out var textRT);
            var text   = textGO.AddComponent<Text>();
            text.font    = ResolveDefaultFont();
            text.color   = _colors.TextOnButton;
            text.fontSize = 26;
            textRT.anchorMin = Vector2.zero;
            textRT.anchorMax = Vector2.one;
            textRT.offsetMin = new Vector2(10f, 4f);
            textRT.offsetMax = new Vector2(-10f, -4f);
            field.textComponent = text;

            // Placeholder child
            var phGO = CreateUIObject("Placeholder", go.transform, out var phRT);
            var ph   = phGO.AddComponent<Text>();
            ph.font       = ResolveDefaultFont();
            ph.color      = new Color(0.55f, 0.55f, 0.58f, 1f);
            ph.fontStyle  = FontStyle.Italic;
            ph.fontSize   = 24;
            ph.text       = placeholder;
            phRT.anchorMin = Vector2.zero;
            phRT.anchorMax = Vector2.one;
            phRT.offsetMin = new Vector2(10f, 4f);
            phRT.offsetMax = new Vector2(-10f, -4f);
            field.placeholder = ph;

            return field;
        }

        private Button CreateChoiceButton(Transform parent, string label, Action onClick)
        {
            var btn = CreateButton(parent, label, onClick);
            btn.gameObject.AddComponent<LayoutElement>().minHeight = 84f;
            return btn;
        }

        private Button CreateButton(Transform parent, string label, Action onClick)
        {
            var go  = CreateUIObject("Btn_" + label, parent, out _);
            var img = go.AddComponent<Image>();
            img.color         = _colors.ButtonNormal;
            img.raycastTarget = true;
            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
            var cb = btn.colors;
            cb.normalColor      = _colors.ButtonNormal;
            cb.highlightedColor = _colors.ButtonHover;
            cb.pressedColor     = _colors.ButtonHover;
            cb.selectedColor    = _colors.ButtonHover;
            cb.disabledColor    = new Color(_colors.ButtonNormal.r * 0.5f,
                                            _colors.ButtonNormal.g * 0.5f,
                                            _colors.ButtonNormal.b * 0.5f, 0.8f);
            btn.colors = cb;
            btn.onClick.AddListener(() => onClick?.Invoke());

            var labelText = CreateText("Label", go.transform, label, 28, TextAnchor.MiddleCenter);
            var labelRT   = labelText.rectTransform;
            labelRT.anchorMin = Vector2.zero;
            labelRT.anchorMax = Vector2.one;
            labelRT.offsetMin = Vector2.zero;
            labelRT.offsetMax = Vector2.zero;

            return btn;
        }

        private static Font ResolveDefaultFont()
        {
            var f = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (f == null) f = Resources.GetBuiltinResource<Font>("Arial.ttf");
            return f;
        }
    }
}

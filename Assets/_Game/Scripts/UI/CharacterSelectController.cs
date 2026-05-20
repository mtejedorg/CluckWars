using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CluckWars.Abilities;
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
    /// Visual theme: warm Clash Royale / Supercell style (cluckwars-tokens-v2).
    /// </summary>
    [RequireComponent(typeof(SceneLoader))]
    public sealed class CharacterSelectController : MonoBehaviour
    {
        private const string Source = "CharacterSelect";

        // ---- Design tokens (Phase 10 redesign — ART.md §6, cluckwars-tokens-v2) ---
        // Inline so the warm palette is self-contained regardless of the serialised
        // ColorSchemeSO asset version in the scene.
        private static readonly Color DtScreenBg    = new Color(0.06f, 0.03f, 0.02f, 1.00f); // #0e0804
        private static readonly Color DtPanelBg     = new Color(0.23f, 0.13f, 0.06f, 0.96f); // #3a2210
        private static readonly Color DtGold        = new Color(0.96f, 0.78f, 0.26f, 1.00f); // #f5c842
        private static readonly Color DtGoldMid     = new Color(0.83f, 0.63f, 0.13f, 0.90f); // #d4a020
        private static readonly Color DtTextPrimary = new Color(1.00f, 0.96f, 0.88f, 1.00f); // #fef5e0

        // ---- Injected --------------------------------------------------
        private ISessionSelectionService _selection;
        private ILogService              _log;
        private ColorSchemeSO            _colors;
        private IUGSService              _ugs;
        private SceneLoader              _sceneLoader;

        // ---- Class card maps (replaces flat class buttons) -------------
        private readonly Dictionary<ChickenClass, Image> _cardBgImages  = new();
        private readonly Dictionary<ChickenClass, Text>  _cardNameTexts = new();
        private readonly Dictionary<SessionMode, Button> _modeButtons   = new();

        // ---- Ability picker --------------------------------------------
        private ChickenClassRegistrySO _classRegistry;
        private int  _abilityIndex0;
        private int  _abilityIndex1;
        private Text _abilitySlot0Label;
        private Text _abilitySlot1Label;

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
            IUGSService ugs,
            [InjectOptional] ChickenClassRegistrySO classRegistry)
        {
            _selection     = selection;
            _log           = log;
            _colors        = colors;
            _ugs           = ugs;
            _classRegistry = classRegistry;
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
            // Reset ability indices when class changes so the picker starts at the
            // first valid option for the new class pool.
            _abilityIndex0 = 0;
            _abilityIndex1 = 1;
            RefreshClassVisuals();
            RefreshAbilityPicker();
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
            rowImg.color = new Color(0.31f, 0.18f, 0.08f, 1f); // medium brown row tint
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
            if (_selection == null) return;
            foreach (var kv in _cardBgImages)
            {
                bool sel = kv.Key == _selection.SelectedClass;
                if (kv.Value != null)
                    kv.Value.color = sel
                        ? new Color(0.40f, 0.25f, 0.06f, 1f)  // #663f10 warm amber selected
                        : new Color(0.18f, 0.10f, 0.04f, 1f); // #2e1a0a dark unselected
            }
            foreach (var kv in _cardNameTexts)
            {
                bool sel = kv.Key == _selection.SelectedClass;
                if (kv.Value != null)
                    kv.Value.color = sel ? DtGold : DtTextPrimary;
            }
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

            var btnNormal = new Color(0.31f, 0.18f, 0.08f, 1f); // medium brown
            var btnHover  = new Color(0.42f, 0.25f, 0.12f, 1f); // lighter hover
            var btnActive = new Color(0.83f, 0.63f, 0.13f, 1f); // gold #d4a020

            var img = button.GetComponent<Image>();
            if (img != null) img.color = isActive ? btnActive : btnNormal;

            var text = button.GetComponentInChildren<Text>();
            if (text != null)
                text.color = isActive
                    ? new Color(0.10f, 0.08f, 0.05f, 1f) // dark on gold
                    : DtTextPrimary;

            var cb = button.colors;
            cb.normalColor      = isActive ? btnActive : btnNormal;
            cb.highlightedColor = isActive ? btnActive : btnHover;
            cb.pressedColor     = btnHover;
            cb.selectedColor    = isActive ? btnActive : btnHover;
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

            // Full-screen dark background — #0e0804 near-black warm brown.
            var bgGO = CreateUIObject("ScreenBackground", canvasGO.transform, out var bgRT);
            bgGO.AddComponent<Image>().color = DtScreenBg;
            bgRT.anchorMin = Vector2.zero;
            bgRT.anchorMax = Vector2.one;
            bgRT.offsetMin = Vector2.zero;
            bgRT.offsetMax = Vector2.zero;

            // Center panel
            var panel = CreateUIObject("Panel", canvasGO.transform, out var panelRT);
            panel.AddComponent<Image>().color = DtPanelBg;
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
            var title = CreateText("Title", panel.transform, "CLUCK WARS", 64, TextAnchor.MiddleCenter);
            title.fontStyle = FontStyle.Bold;
            title.color     = DtGold;
            title.gameObject.AddComponent<LayoutElement>().minHeight = 80f;

            // CLASS — stat cards (replaces flat buttons)
            CreateSectionLabel(panel.transform, "CLASS");
            BuildClassCards(panel.transform);

            // ABILITIES — per-slot picker
            CreateSectionLabel(panel.transform, "ABILITIES");
            BuildAbilityPicker(panel.transform);

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
            var startBase = new Color(0.20f, 0.64f, 0.20f, 1f); // DtGreenMid #33a332
            var actionImg = _actionButton.GetComponent<Image>();
            if (actionImg != null) actionImg.color = startBase;
            var actionLE = _actionButton.gameObject.AddComponent<LayoutElement>();
            actionLE.minHeight       = 72f;
            actionLE.preferredHeight = 72f;
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
            panel.AddComponent<Image>().color = DtPanelBg;
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
            text.color             = DtTextPrimary; // warm white #fef5e0
            text.alignment         = alignment;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow   = VerticalWrapMode.Overflow;
            text.font              = ResolveDefaultFont();
            return text;
        }

        private void CreateSectionLabel(Transform parent, string label)
        {
            var t = CreateText(label + "Label", parent, label, 20, TextAnchor.MiddleLeft);
            t.color = DtGoldMid; // warm gold accent for section headers
            t.gameObject.AddComponent<LayoutElement>().minHeight = 28f;
        }

        private InputField CreateInputField(Transform parent, string placeholder, float minHeight)
        {
            var go = CreateUIObject("Input", parent, out _);
            var img = go.AddComponent<Image>();
            img.color = new Color(0.14f, 0.07f, 0.03f, 1f); // very dark warm brown input bg
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
            // Use design-token colors directly so the result is independent of the
            // serialised ColorSchemeSO asset version.
            var btnNormal = new Color(0.31f, 0.18f, 0.08f, 1f); // medium brown
            var btnHover  = new Color(0.42f, 0.25f, 0.12f, 1f); // lighter brown hover

            var go  = CreateUIObject("Btn_" + label, parent, out _);
            var img = go.AddComponent<Image>();
            img.color         = btnNormal;
            img.raycastTarget = true;
            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
            var cb = btn.colors;
            cb.normalColor      = btnNormal;
            cb.highlightedColor = btnHover;
            cb.pressedColor     = btnHover;
            cb.selectedColor    = btnHover;
            cb.disabledColor    = new Color(btnNormal.r * 0.5f, btnNormal.g * 0.5f, btnNormal.b * 0.5f, 0.8f);
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

        // ---- Class stat cards ------------------------------------------

        private void BuildClassCards(Transform parent)
        {
            // Compute stat maxima for normalized bars.
            float maxSpd = 1f, maxHp = 1f, maxCgo = 1f;
            var allClasses = new[]
            {
                ChickenClass.Warrior, ChickenClass.Speedy,
                ChickenClass.Fatty,   ChickenClass.Assassin,
            };
            foreach (var c in allClasses)
            {
                if (_classRegistry == null || !_classRegistry.TryGet(c, out var e) || e.Stats == null) continue;
                maxSpd = Mathf.Max(maxSpd, e.Stats.MoveSpeed);
                maxHp  = Mathf.Max(maxHp,  e.Stats.MaxHP);
                maxCgo = Mathf.Max(maxCgo, (float)e.Stats.CargoCapacity);
            }

            var row = CreateUIObject("ClassCardsRow", parent, out _);
            var layout = row.AddComponent<HorizontalLayoutGroup>();
            layout.spacing                = 10f;
            layout.childForceExpandWidth  = true;
            layout.childForceExpandHeight = true;
            layout.childControlWidth      = true;
            layout.childControlHeight     = true;
            row.AddComponent<LayoutElement>().minHeight = 180f;

            _cardBgImages.Clear();
            _cardNameTexts.Clear();
            foreach (var cls in allClasses)
                BuildClassCard(row.transform, cls, maxSpd, maxHp, maxCgo);
        }

        private void BuildClassCard(Transform parent, ChickenClass cls,
            float maxSpd, float maxHp, float maxCgo)
        {
            var tint         = GetClassTint(cls);
            var stats        = GetClassStats(cls);
            int abilityCount = GetAvailableAbilities(cls).Length;
            string shortKey  = cls switch
            {
                ChickenClass.Warrior  => "[1]",
                ChickenClass.Speedy   => "[2]",
                ChickenClass.Fatty    => "[3]",
                ChickenClass.Assassin => "[4]",
                _                     => "[ ]",
            };

            // Card root — dark bg, vertical layout, entire card is a button.
            var cardGO  = CreateUIObject("Card_" + cls, parent, out _);
            var cardImg = cardGO.AddComponent<Image>();
            cardImg.color = new Color(0.18f, 0.10f, 0.04f, 1f);
            _cardBgImages[cls] = cardImg;

            var cardLayout = cardGO.AddComponent<VerticalLayoutGroup>();
            cardLayout.padding                = new RectOffset(0, 0, 0, 0);
            cardLayout.spacing                = 0f;
            cardLayout.childForceExpandWidth  = true;
            cardLayout.childForceExpandHeight = false;
            cardLayout.childControlWidth      = true;
            cardLayout.childControlHeight     = true;

            var capturedCls = cls;
            var btn = cardGO.AddComponent<Button>();
            btn.targetGraphic = cardImg;
            btn.onClick.AddListener(() => SelectClass(capturedCls, "card-click"));
            var bc = btn.colors;
            bc.normalColor      = cardImg.color;
            bc.highlightedColor = new Color(0.28f, 0.16f, 0.06f, 1f);
            bc.pressedColor     = DtGoldMid;
            bc.selectedColor    = new Color(0.40f, 0.25f, 0.06f, 1f);
            btn.colors = bc;

            // Top tint strip (10 px).
            var stripGO = CreateUIObject("Strip", cardGO.transform, out _);
            stripGO.AddComponent<Image>().color = tint;
            stripGO.AddComponent<LayoutElement>().minHeight = 10f;

            // Body.
            var body       = CreateUIObject("Body", cardGO.transform, out _);
            var bodyLayout = body.AddComponent<VerticalLayoutGroup>();
            bodyLayout.padding                = new RectOffset(8, 8, 6, 6);
            bodyLayout.spacing                = 4f;
            bodyLayout.childForceExpandWidth  = true;
            bodyLayout.childForceExpandHeight = false;
            bodyLayout.childControlWidth      = true;
            bodyLayout.childControlHeight     = true;
            body.AddComponent<LayoutElement>().flexibleHeight = 1f;

            // Class name.
            var nameLabel = CreateText("Name", body.transform, $"{shortKey} {cls}", 22, TextAnchor.MiddleCenter);
            nameLabel.fontStyle = FontStyle.Bold;
            nameLabel.gameObject.AddComponent<LayoutElement>().minHeight = 30f;
            _cardNameTexts[cls] = nameLabel;

            // Stat bars.
            if (stats != null)
            {
                BuildStatBar(body.transform, "SPD", maxSpd > 0f ? stats.MoveSpeed           / maxSpd : 0f, tint);
                BuildStatBar(body.transform, "HP",  maxHp  > 0f ? stats.MaxHP                / maxHp  : 0f, tint);
                BuildStatBar(body.transform, "CGO", maxCgo > 0f ? (float)stats.CargoCapacity / maxCgo : 0f, tint);
            }
            else
            {
                // Placeholder bars when registry is not bound.
                float[] d = cls switch
                {
                    ChickenClass.Warrior  => new[] { 0.55f, 0.75f, 0.55f },
                    ChickenClass.Speedy   => new[] { 1.00f, 0.45f, 0.40f },
                    ChickenClass.Fatty    => new[] { 0.30f, 1.00f, 1.00f },
                    ChickenClass.Assassin => new[] { 0.80f, 0.50f, 0.40f },
                    _                     => new[] { 0.50f, 0.50f, 0.50f },
                };
                BuildStatBar(body.transform, "SPD", d[0], tint);
                BuildStatBar(body.transform, "HP",  d[1], tint);
                BuildStatBar(body.transform, "CGO", d[2], tint);
            }

            // Ability-pool hint.
            string hintText = abilityCount > 0 ? $"{abilityCount} in pool" : "all abilities";
            var hint = CreateText("Hint", body.transform, hintText, 16, TextAnchor.MiddleCenter);
            hint.color = DtGoldMid;
            hint.gameObject.AddComponent<LayoutElement>().minHeight = 20f;
        }

        private void BuildStatBar(Transform parent, string label, float fill, Color barColor)
        {
            var row = CreateUIObject("StatRow_" + label, parent, out _);
            var rowLayout = row.AddComponent<HorizontalLayoutGroup>();
            rowLayout.spacing                = 4f;
            rowLayout.childForceExpandWidth  = false;
            rowLayout.childForceExpandHeight = true;
            rowLayout.childControlWidth      = true;
            rowLayout.childControlHeight     = true;
            row.AddComponent<LayoutElement>().minHeight = 13f;

            // Label (fixed width).
            var lbl  = CreateText("Lbl", row.transform, label, 11, TextAnchor.MiddleLeft);
            lbl.color = DtGoldMid;
            var lblLE = lbl.gameObject.AddComponent<LayoutElement>();
            lblLE.minWidth       = 30f;
            lblLE.preferredWidth = 30f;
            lblLE.flexibleWidth  = 0f;

            // Bar background (flex width).
            var bgGO = CreateUIObject("BarBg", row.transform, out _);
            bgGO.AddComponent<Image>().color = new Color(0.08f, 0.05f, 0.02f, 1f);
            bgGO.AddComponent<LayoutElement>().flexibleWidth = 1f;

            // Fill (anchor-based, percentage of background width).
            var fillGO = CreateUIObject("Fill", bgGO.transform, out var fillRT);
            fillGO.AddComponent<Image>().color = barColor;
            fillRT.anchorMin = new Vector2(0f, 0.15f);
            fillRT.anchorMax = new Vector2(Mathf.Clamp01(fill), 0.85f);
            fillRT.offsetMin = Vector2.zero;
            fillRT.offsetMax = Vector2.zero;
        }

        // ---- Ability slot picker ---------------------------------------

        private void BuildAbilityPicker(Transform parent)
        {
            var row = CreateUIObject("AbilityPickerRow", parent, out _);
            var rowLayout = row.AddComponent<HorizontalLayoutGroup>();
            rowLayout.spacing                = 16f;
            rowLayout.childForceExpandWidth  = true;
            rowLayout.childForceExpandHeight = true;
            rowLayout.childControlWidth      = true;
            rowLayout.childControlHeight     = true;
            row.AddComponent<LayoutElement>().minHeight = 56f;

            BuildAbilitySlot(row.transform, 0, out _abilitySlot0Label);

            // Thin vertical divider.
            var divGO = CreateUIObject("Divider", row.transform, out _);
            divGO.AddComponent<Image>().color = new Color(0.40f, 0.30f, 0.15f, 0.60f);
            var divLE = divGO.AddComponent<LayoutElement>();
            divLE.minWidth       = 2f;
            divLE.preferredWidth = 2f;
            divLE.flexibleWidth  = 0f;

            BuildAbilitySlot(row.transform, 1, out _abilitySlot1Label);

            RefreshAbilityPicker();
        }

        private void BuildAbilitySlot(Transform parent, int slot, out Text nameLabel)
        {
            var container = CreateUIObject($"AbilitySlot{slot}", parent, out _);
            var layout = container.AddComponent<HorizontalLayoutGroup>();
            layout.spacing                = 4f;
            layout.childForceExpandWidth  = false;
            layout.childForceExpandHeight = true;
            layout.childControlWidth      = true;
            layout.childControlHeight     = true;
            layout.childAlignment         = TextAnchor.MiddleCenter;

            var leftBtn = CreateButton(container.transform, "◀", () => CycleAbility(slot, -1));
            leftBtn.gameObject.AddComponent<LayoutElement>().minWidth = 38f;
            leftBtn.gameObject.GetComponent<LayoutElement>().preferredWidth = 38f;

            var slotLabel = CreateText($"AbilityName{slot}", container.transform, "—", 20, TextAnchor.MiddleCenter);
            slotLabel.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
            nameLabel = slotLabel;

            var rightBtn = CreateButton(container.transform, "▶", () => CycleAbility(slot, +1));
            rightBtn.gameObject.AddComponent<LayoutElement>().minWidth = 38f;
            rightBtn.gameObject.GetComponent<LayoutElement>().preferredWidth = 38f;
        }

        private void RefreshAbilityPicker()
        {
            if (_selection == null) return;
            var abilities = GetAvailableAbilities(_selection.SelectedClass);

            // Slot 0.
            if (_abilitySlot0Label != null)
            {
                if (abilities.Length > 0)
                {
                    _abilityIndex0 = Mathf.Clamp(_abilityIndex0, 0, abilities.Length - 1);
                    var ab = abilities[_abilityIndex0];
                    _abilitySlot0Label.text  = ab != null ? ab.DisplayName : "—";
                    _abilitySlot0Label.color = ab != null ? ab.AccentColor : DtTextPrimary;
                    _selection.Ability0 = ab;
                }
                else
                {
                    _abilitySlot0Label.text  = "Default";
                    _abilitySlot0Label.color = DtGoldMid;
                    _selection.Ability0 = null;
                }
            }

            // Slot 1 — avoid duplicating slot 0 when pool has at least 2 entries.
            if (_abilitySlot1Label != null)
            {
                if (abilities.Length > 1)
                {
                    _abilityIndex1 = Mathf.Clamp(_abilityIndex1, 0, abilities.Length - 1);
                    if (_abilityIndex1 == _abilityIndex0)
                        _abilityIndex1 = (_abilityIndex0 + 1) % abilities.Length;
                    var ab = abilities[_abilityIndex1];
                    _abilitySlot1Label.text  = ab != null ? ab.DisplayName : "—";
                    _abilitySlot1Label.color = ab != null ? ab.AccentColor : DtTextPrimary;
                    _selection.Ability1 = ab;
                }
                else
                {
                    // Pool has 0 or 1 entry → no second slot selection.
                    _abilitySlot1Label.text  = abilities.Length == 1 ? "—" : "Default";
                    _abilitySlot1Label.color = DtGoldMid;
                    _selection.Ability1 = null;
                }
            }
        }

        private void CycleAbility(int slot, int direction)
        {
            if (_selection == null) return;
            var abilities = GetAvailableAbilities(_selection.SelectedClass);
            if (abilities.Length == 0) return;

            if (slot == 0)
                _abilityIndex0 = (_abilityIndex0 + direction + abilities.Length) % abilities.Length;
            else
                _abilityIndex1 = (_abilityIndex1 + direction + abilities.Length) % abilities.Length;

            RefreshAbilityPicker();
        }

        // ---- Class registry helpers ------------------------------------

        private AbilityBaseSO[] GetAvailableAbilities(ChickenClass cls)
        {
            if (_classRegistry == null) return Array.Empty<AbilityBaseSO>();
            if (!_classRegistry.TryGet(cls, out var entry)) return Array.Empty<AbilityBaseSO>();
            var pool = entry.Stats?.AvailableAbilities;
            return pool != null && pool.Length > 0 ? pool : Array.Empty<AbilityBaseSO>();
        }

        private Color GetClassTint(ChickenClass cls)
        {
            if (_classRegistry != null && _classRegistry.TryGet(cls, out var entry))
                return entry.TintColor;
            // Fallback: Okabe-Ito inspired per-class colors.
            return cls switch
            {
                ChickenClass.Warrior  => new Color(0.91f, 0.46f, 0.10f, 1f), // orange
                ChickenClass.Speedy   => new Color(0.10f, 0.50f, 0.77f, 1f), // blue
                ChickenClass.Fatty    => new Color(0.77f, 0.16f, 0.43f, 1f), // pink/magenta
                ChickenClass.Assassin => new Color(0.05f, 0.62f, 0.48f, 1f), // teal
                _                     => Color.white,
            };
        }

        private ChickenStatsSO GetClassStats(ChickenClass cls)
        {
            if (_classRegistry == null) return null;
            return _classRegistry.TryGet(cls, out var entry) ? entry.Stats : null;
        }
    }
}

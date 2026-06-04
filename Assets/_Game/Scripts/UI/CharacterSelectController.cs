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
    [RequireComponent(typeof(SceneLoader))]
    public sealed class CharacterSelectController : MonoBehaviour
    {
        private const string Source = "CharacterSelect";

        // Layout Colors
        public static readonly Color DtScreenBg    = UiGfx.Hex32("f4ece1");
        public static readonly Color DtPanelBg     = UiGfx.Hex32("1e0f05");
        public static readonly Color DtGold        = new Color(0.96f, 0.78f, 0.26f, 1.00f);
        public static readonly Color DtGoldMid     = new Color(0.83f, 0.63f, 0.13f, 0.90f);
        public static readonly Color DtTextPrimary = new Color(1.00f, 0.96f, 0.88f, 1.00f);
        public static readonly Color DtRed         = UiGfx.Hex32("e04536");
        public static readonly Color DtCardOff     = UiGfx.Hex32("3d2112");
        public static readonly Color DtCardOn      = UiGfx.Hex32("4a1d13");

        private ISessionSelectionService _selection;
        private ILogService              _log;
        private ColorSchemeSO            _colors;
        private IUGSService              _ugs;
        private SceneLoader              _sceneLoader;
        private ChickenClassRegistrySO _classRegistry;
        private AbilityRegistrySO      _abilityRegistry;

        // V3 State
        private enum MenuPhase { CharacterSelect, Lobby }
        private MenuPhase _phase = MenuPhase.CharacterSelect;
        private int _selectedPickerSlot = 0;
        private bool _isBusy;

        // Panels
        [Header("Main Panels")]
        [SerializeField] private GameObject _charSelectPanel;
        [SerializeField] private GameObject _lobbyPanel;

        // UI Prefabs
        [Header("UI Prefabs")]
        [SerializeField] private UiClassCardView _classCardPrefab;
        [SerializeField] private UiEquipSlotView _equipSlotPrefab;
        [SerializeField] private UiAbilitySectionView _abilitySectionPrefab;
        [SerializeField] private UiAbilityCardView _abilityCardPrefab;

        // UI Refs - Char Select
        [Header("Containers")]
        [SerializeField] private Transform _classListContainer;
        [SerializeField] private UiEquipAreaView _equipArea;
        [SerializeField] private Transform _abilityGridContent;

        [Header("Preview Area")]
        [SerializeField] private Image _previewChickenImg;
        [SerializeField] private Image _previewGlowImg;
        [SerializeField] private Text _previewNameText;
        [SerializeField] private Text _previewBadgeText;
        [SerializeField] private Text _previewDescText;

        // Data Tracking
        private readonly Dictionary<ChickenClass, UiClassCardView> _classCards = new();
        private readonly UiEquipSlotView[] _slotSelectorBg = new UiEquipSlotView[3];

        // UI Refs - Lobby
        private GameObject _hostSection;
        private GameObject _joinSection;
        private InputField _joinCodeInput;
        private readonly Dictionary<SessionMode, Button> _modeButtons = new();
        private Transform _playerListContent;
        private Text _roomCodeDisplay;

        [Inject]
        public void Construct(
            ISessionSelectionService selection,
            ILogService log,
            ColorSchemeSO colors,
            IUGSService ugs,
            [InjectOptional] ChickenClassRegistrySO classRegistry,
            [InjectOptional] AbilityRegistrySO abilityRegistry)
        {
            _selection       = selection;
            _log             = log;
            _colors          = colors;
            _ugs             = ugs;
            _classRegistry   = classRegistry;
            _abilityRegistry = abilityRegistry;
        }

        private void Awake()
        {
            _sceneLoader = GetComponent<SceneLoader>();
            _sceneLoader.SetAutoLoad(false);

            if (_log == null) ProjectContext.Instance.Container.Inject(this);

            EnsureEventSystem();
            InitializeUI();
            SwitchPhase(MenuPhase.CharacterSelect);
        }

        private void Update()
        {
            var kb = Keyboard.current;
            if (kb == null || _selection == null || _isBusy) return;

            if (_phase == MenuPhase.CharacterSelect)
            {
                if (kb.digit1Key.wasPressedThisFrame) SelectClass(ChickenClass.Warrior);
                else if (kb.digit2Key.wasPressedThisFrame) SelectClass(ChickenClass.Speedy);
                else if (kb.digit3Key.wasPressedThisFrame) SelectClass(ChickenClass.Fatty);
                else if (kb.digit4Key.wasPressedThisFrame) SelectClass(ChickenClass.Assassin);
            }
            
            if (kb.sKey.wasPressedThisFrame)      SelectMode(SessionMode.Solo);
            else if (kb.hKey.wasPressedThisFrame) SelectMode(SessionMode.Host);
            else if (kb.jKey.wasPressedThisFrame) SelectMode(SessionMode.Join);
        }

        private void SwitchPhase(MenuPhase phase)
        {
            _phase = phase;
            if (_charSelectPanel != null) _charSelectPanel.SetActive(phase == MenuPhase.CharacterSelect);
            if (_lobbyPanel != null) _lobbyPanel.SetActive(phase == MenuPhase.Lobby);

            if (phase == MenuPhase.CharacterSelect)
            {
                RefreshClassVisuals();
                RefreshAbilityPicker();
            }
            else
            {
                RefreshLobbyView();
            }
        }

        private void SelectClass(ChickenClass cls)
        {
            if (_selection == null) return;
            _selection.SelectedClass = cls;
            _selectedPickerSlot = 0;
            _selection.Ability0 = null;
            _selection.Ability1 = null;
            _selection.Ability2 = null;
            RefreshClassVisuals();
            RefreshAbilityPicker();
        }

        private void SelectMode(SessionMode mode)
        {
            if (_selection == null || _isBusy) return;
            _selection.Mode = mode;
            RefreshLobbyView();
        }

        private async void ConfirmReady()
        {
            if (_isBusy) return;
            SwitchPhase(MenuPhase.Lobby);
        }

        private async void StartMatch()
        {
            if (_selection == null || _isBusy) return;
            switch (_selection.Mode)
            {
                case SessionMode.Solo: _sceneLoader.LoadNext(); break;
                case SessionMode.Host: await CreateLobbyAndProceed(); break;
                case SessionMode.Join: await JoinByCodeAndProceed(); break;
            }
        }

        private async Task CreateLobbyAndProceed()
        {
            _isBusy = true;
            try
            {
                var info = await _ugs.CreateLobbyAsync("CluckWars Match", 4);
                _selection.SessionName = info.JoinCode;
                _sceneLoader.LoadNext();
            }
            catch (Exception e) { _log?.Error(Source, $"CreateLobby failed: {e.Message}"); }
            _isBusy = false;
        }

        private async Task JoinByCodeAndProceed()
        {
            var code = _joinCodeInput?.text?.Trim();
            if (string.IsNullOrEmpty(code)) return;
            _isBusy = true;
            try
            {
                var info = await _ugs.JoinLobbyByCodeAsync(code);
                _selection.SessionName = info.JoinCode;
                _sceneLoader.LoadNext();
            }
            catch (Exception e) { _log?.Error(Source, $"JoinByCode failed: {e.Message}"); }
            _isBusy = false;
        }

        // ---- UI CONSTRUCTION ----
        private static void EnsureEventSystem()
        {
            if (FindFirstObjectByType<EventSystem>() != null) return;
            new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
        }

        private void InitializeUI()
        {
            if (_charSelectPanel == null || _lobbyPanel == null)
            {
                BuildFallbackCanvas(); // Build the old procedural UI if the scene isn't wired up yet
                return;
            }

            // Build atomic prefabs if wired up
            if (_classListContainer != null && _classCardPrefab != null)
            {
                foreach (var cls in new[] { ChickenClass.Warrior, ChickenClass.Speedy, ChickenClass.Fatty, ChickenClass.Assassin })
                {
                    var card = Instantiate(_classCardPrefab, _classListContainer);
                    var pInfo = GetPassiveInfo(cls);
                    card.Bind(cls, pInfo.subRole, () => SelectClass(cls));
                    _classCards[cls] = card;
                }
            }

            if (_equipArea != null && _equipSlotPrefab != null)
            {
                for (int s = 0; s < 3; s++)
                {
                    int captured = s;
                    var slot = Instantiate(_equipSlotPrefab, _equipArea.SlotsContainer);
                    slot.Bind(s, null, false, () => SelectPickerSlot(captured));
                    _slotSelectorBg[s] = slot;
                }
            }
        }

        private void RefreshClassVisuals()
        {
            if (_selection == null) return;
            var cls = _selection.SelectedClass;
            
            foreach(var kv in _classCards)
            {
                kv.Value.SetSelected(kv.Key == cls);
            }

            if (_previewChickenImg != null)
            {
                var spr = UiGfx.Chicken(cls.ToString().ToLowerInvariant());
                _previewChickenImg.sprite = spr;
                _previewChickenImg.color = spr != null ? Color.white : Color.clear;
            }
            
            if (_previewGlowImg != null)
            {
                _previewGlowImg.gameObject.SetActive(_previewChickenImg.sprite != null);
            }
            
            if (_previewNameText != null) _previewNameText.text = $"{cls.ToString().ToUpper()} CHICKEN";
            
            if (_previewDescText != null)
            {
                var pInfo = GetPassiveInfo(cls);
                if (_previewBadgeText != null) _previewBadgeText.text = $"PASSIVE - {pInfo.name}";
                _previewDescText.text = pInfo.desc;
            }
        }

        private void SelectPickerSlot(int slot)
        {
            if (_selection == null) return;
            if (slot == 2 && _selection.SelectedClass != ChickenClass.Assassin) return;
            _selectedPickerSlot = slot;
            RefreshAbilityPicker();
        }

        private void EquipAbilityInSelectedSlot(AbilityBaseSO ability)
        {
            if (_selection == null) return;
            int s = _selectedPickerSlot;
            if (s == 0) _selection.Ability0 = ability;
            else if (s == 1) _selection.Ability1 = ability;
            else if (s == 2) _selection.Ability2 = ability;
            RefreshAbilityPicker();
        }

        private void RefreshAbilityPicker()
        {
            if (_selection == null) return;
            bool isAss = _selection.SelectedClass == ChickenClass.Assassin;
            if (_selectedPickerSlot == 2 && !isAss) _selectedPickerSlot = 0;

            int equippedCount = 0;
            if (_selection.Ability0 != null) equippedCount++;
            if (_selection.Ability1 != null) equippedCount++;
            if (_selection.Ability2 != null && isAss) equippedCount++;
            
            if (_equipArea != null)
                _equipArea.SetEquippedCount(equippedCount, isAss ? 3 : 2);

            for(int s=0; s<3; s++)
            {
                if (_slotSelectorBg[s] == null) continue;
                bool vis = s < 2 || isAss;
                _slotSelectorBg[s].gameObject.SetActive(vis);
                
                var eq = s == 0 ? _selection.Ability0 : (s == 1 ? _selection.Ability1 : _selection.Ability2);
                _slotSelectorBg[s].Bind(s, eq, s == 2 && !isAss, () => SelectPickerSlot(s));
                _slotSelectorBg[s].SetSelected(s == _selectedPickerSlot);
            }

            var pool = _abilityRegistry?.All ?? Array.Empty<AbilityBaseSO>();
            
            if (_abilityGridContent != null && _abilitySectionPrefab != null && _abilityCardPrefab != null)
            {
                foreach (Transform t in _abilityGridContent) Destroy(t.gameObject);
                
                var dict = new Dictionary<string, List<AbilityBaseSO>>();
                dict["DAMAGE"] = new List<AbilityBaseSO>();
                dict["CONTROL"] = new List<AbilityBaseSO>();
                dict["DEFENSE"] = new List<AbilityBaseSO>();
                dict["UTILITY"] = new List<AbilityBaseSO>();

                foreach (var ab in pool) {
                    string key = ab.Category.ToString().ToUpper();
                    if (!dict.ContainsKey(key)) dict[key] = new List<AbilityBaseSO>();
                    dict[key].Add(ab);
                }

                foreach(var kv in dict)
                {
                    if (kv.Value.Count == 0) continue;
                    
                    var section = Instantiate(_abilitySectionPrefab, _abilityGridContent);
                    section.Bind(kv.Key);
                    
                    int count = kv.Value.Count;
                    int rows = Mathf.CeilToInt(count / 3.0f);
                    float gridHeight = rows * 100 + Mathf.Max(0, rows - 1) * 15;
                    
                    var le = section.GridContainer.gameObject.AddComponent<LayoutElement>();
                    le.preferredHeight = gridHeight;

                    foreach (var ab in kv.Value)
                    {
                        var card = Instantiate(_abilityCardPrefab, section.GridContainer);
                        card.Bind(ab, () => EquipAbilityInSelectedSlot(ab));
                    }
                }
            }
        }

        private void RefreshLobbyView()
        {
            if (_selection == null || _playerListContent == null) return;
            foreach (var kv in _modeButtons) kv.Value.GetComponent<Image>().color = kv.Key == _selection.Mode ? DtGold : UiGfx.CardBorder;
            if (_hostSection != null) _hostSection.SetActive(_selection.Mode == SessionMode.Host);
            if (_joinSection != null) _joinSection.SetActive(_selection.Mode == SessionMode.Join);
            foreach (Transform child in _playerListContent) Destroy(child.gameObject);

            var me = CreateUIObject("MeRow", _playerListContent, out var meRT);
            meRT.sizeDelta = new Vector2(0, 60);
            UiGfx.StylePanel(me, UiGfx.CardBottom, DtGoldMid, 10, 2);
            var meBtn = me.AddComponent<Button>();
            meBtn.onClick.AddListener(() => SwitchPhase(MenuPhase.CharacterSelect));
            var meL = me.AddComponent<HorizontalLayoutGroup>();
            meL.padding = new RectOffset(10,10,5,5); meL.spacing = 10; meL.childAlignment = TextAnchor.MiddleLeft;
            
            var meChick = CreateUIObject("Chick", me.transform, out var mcRT);
            mcRT.sizeDelta = new Vector2(50, 50);
            meChick.AddComponent<Image>().sprite = UiGfx.Chicken(_selection.SelectedClass.ToString().ToLower());
            
            var meText = CreateText("Name", me.transform, $"P1 (You)  -  {_selection.SelectedClass}", 20, TextAnchor.MiddleLeft);
            meText.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;

            var meRdy = CreateText("Rdy", me.transform, "✓ Ready", 20, TextAnchor.MiddleRight);
            meRdy.color = new Color(0.4f, 1f, 0.4f);
        }

        private void BuildFallbackCanvas()
        {
            var canvasGO = new GameObject("MenuCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasGO.transform.SetParent(transform, worldPositionStays: false);
            var canvas = canvasGO.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasGO.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            var bgGO = CreateUIObject("ScreenBackground", canvasGO.transform, out var bgRT);
            bgGO.AddComponent<Image>().color = DtScreenBg;
            SetStretch(bgRT);

            _charSelectPanel = CreateUIObject("CharSelectPanel", canvasGO.transform, out var csRT);
            SetStretch(csRT);

            _lobbyPanel = CreateUIObject("LobbyPanel", canvasGO.transform, out var lbRT);
            SetStretch(lbRT);
            BuildLobby(_lobbyPanel.transform);
            
            _log?.Info(Source, "Using fallback UI! The scene is not wired up to the prefabs.");
        }

        // --- Helpers ---
        private static GameObject CreateUIObject(string name, Transform parent, out RectTransform rt)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            rt = (RectTransform)go.transform;
            return go;
        }

        private static void SetStretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
        }

        private Text CreateText(string name, Transform parent, string content, int fontSize, TextAnchor align)
        {
            var go = CreateUIObject(name, parent, out _);
            var t = go.AddComponent<Text>();
            t.text = content; t.fontSize = fontSize; t.alignment = align;
            t.font = UiGfx.ChunkyFont(); t.color = DtTextPrimary;
            UiGfx.AddShadow(t);
            return t;
        }

        private InputField CreateInputField(Transform parent, string ph, float h)
        {
            var go = CreateUIObject("Input", parent, out var rt);
            rt.sizeDelta = new Vector2(200, h);
            var img = go.AddComponent<Image>(); img.sprite = UiGfx.Rounded(8); img.type = Image.Type.Sliced; img.color = new Color(0.1f,0.05f,0.02f);
            var fld = go.AddComponent<InputField>(); fld.targetGraphic = img;
            
            var txtGo = CreateUIObject("Txt", go.transform, out var tRT); SetStretch(tRT); tRT.offsetMin=new Vector2(10,0);
            var txt = txtGo.AddComponent<Text>(); txt.font = UiGfx.ChunkyFont(); txt.fontSize = 24; txt.color = Color.white; txt.alignment = TextAnchor.MiddleLeft;
            fld.textComponent = txt;
            return fld;
        }

        private Button CreateButton(Transform parent, string label, Action cb, int fontSize = 24)
        {
            var go = CreateUIObject("Btn", parent, out var rt);
            var img = UiGfx.StylePanel(go, DtCardOff, UiGfx.CardBorder, 10, 2);
            var btn = go.AddComponent<Button>(); btn.targetGraphic = img; btn.onClick.AddListener(() => cb?.Invoke());
            var txt = CreateText("Lbl", go.transform, label, fontSize, TextAnchor.MiddleCenter);
            SetStretch(txt.rectTransform);
            return btn;
        }
        
        private void BuildLobby(Transform parent)
        {
            var center = CreateUIObject("CenterPanel", parent, out var rt);
            rt.anchorMin = new Vector2(0.5f, 0.5f); rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f); rt.sizeDelta = new Vector2(700, 600);
            rt.anchoredPosition = Vector2.zero;
            UiGfx.StylePanel(center, DtPanelBg, UiGfx.CardBorder, 18, 3);

            var layout = center.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(30,30,30,30); layout.spacing = 20;
            layout.childControlHeight = false;

            var hdr = CreateUIObject("Hdr", center.transform, out var hdrRT);
            hdrRT.sizeDelta = new Vector2(0, 50);
            var hdrL = hdr.AddComponent<HorizontalLayoutGroup>();
            hdrL.childControlWidth = false; hdrL.childForceExpandWidth = true;
            CreateText("Title", hdr.transform, "MATCH LOBBY", 36, TextAnchor.MiddleLeft).color = DtGold;
            
            var modeGrp = CreateUIObject("ModeGrp", hdr.transform, out var mgRT);
            mgRT.sizeDelta = new Vector2(300, 50);
            var mgL = modeGrp.AddComponent<HorizontalLayoutGroup>(); mgL.spacing = 5;
            _modeButtons[SessionMode.Solo] = CreateButton(modeGrp.transform, "Solo", () => SelectMode(SessionMode.Solo));
            _modeButtons[SessionMode.Host] = CreateButton(modeGrp.transform, "Host", () => SelectMode(SessionMode.Host));
            _modeButtons[SessionMode.Join] = CreateButton(modeGrp.transform, "Join", () => SelectMode(SessionMode.Join));

            _hostSection = CreateUIObject("HostCode", center.transform, out var hsRT);
            hsRT.sizeDelta = new Vector2(0, 40);
            var hsL = _hostSection.AddComponent<HorizontalLayoutGroup>();
            CreateText("Lbl", _hostSection.transform, "Lobby Code: ", 24, TextAnchor.MiddleLeft);
            _roomCodeDisplay = CreateText("Val", _hostSection.transform, "------", 28, TextAnchor.MiddleLeft);
            _roomCodeDisplay.color = new Color(0.4f, 1f, 0.5f);

            _joinSection = CreateUIObject("JoinCode", center.transform, out var jsRT);
            jsRT.sizeDelta = new Vector2(0, 50);
            var jsL = _joinSection.AddComponent<HorizontalLayoutGroup>(); jsL.spacing=10;
            CreateText("Lbl", _joinSection.transform, "Enter Code:", 24, TextAnchor.MiddleLeft);
            _joinCodeInput = CreateInputField(_joinSection.transform, "Code", 50);

            var plBg = CreateUIObject("PlayerList", center.transform, out var plRT);
            plRT.sizeDelta = new Vector2(0, 250);
            UiGfx.StylePanel(plBg, new Color(0,0,0,0.3f), Color.clear, 10, 0);
            var plLayout = plBg.AddComponent<VerticalLayoutGroup>();
            plLayout.padding = new RectOffset(10,10,10,10); plLayout.spacing = 10;
            _playerListContent = plBg.transform;

            var startBtn = CreateButton(center.transform, "START MATCH", StartMatch, 36);
            startBtn.GetComponent<RectTransform>().sizeDelta = new Vector2(0, 80);
        }

        public static (string name, string desc, string subRole) GetPassiveInfo(ChickenClass cls) => cls switch
        {
            ChickenClass.Warrior  => ("TOUGH",     "Deals increased ability damage.", "All-Rounder"),
            ChickenClass.Speedy   => ("SLIPPERY",  "Reduced control-effect duration.", "Hit & Run"),
            ChickenClass.Fatty    => ("IMMOVABLE", "Greatly reduced knockback.", "Bulk Carrier"),
            ChickenClass.Assassin => ("COMBO",     "Equips 3 abilities instead of 2.", "Disruptor"),
            _                     => ("—",         "", ""),
        };
    }
}

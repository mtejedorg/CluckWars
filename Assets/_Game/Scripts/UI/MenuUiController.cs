using System;
using System.Collections.Generic;
using System.Linq;
using CluckWars.Abilities;
using CluckWars.Bootstrap;
using CluckWars.Gameplay;
using CluckWars.Localization;
using CluckWars.Logging;
using CluckWars.Services;
using CluckWars.Settings;
using UnityEngine;
using UnityEngine.UIElements;
using Zenject;

namespace CluckWars.UI
{
    /// <summary>
    /// UI Toolkit menu front-end: Main Menu → Class Select → Loadout → Lobby → Game.
    /// Replaces the procedural-UGUI CharacterSelectController. Visuals live in
    /// Assets/UI/*.uxml + Assets/UI/Styles/CluckWarsTheme.uss; this controller
    /// only binds data and drives navigation. All selections are written back to
    /// <see cref="ISessionSelectionService"/> exactly as the old controller did,
    /// so the spawner (Game scene) keeps working unchanged.
    /// </summary>
    /// <remarks>
    /// Character select used to be one screen (class pick + passive fork + stat
    /// preview + two ability pick rows + detail strip + toggles + ready button).
    /// Split 2026-09 into two steps: Class Select (pick class + specialization)
    /// and Loadout (pick abilities into four explicit slots). The old
    /// "tap a card, it auto-fills the first open slot, and drops the oldest pick
    /// when full" behaviour made the in-match button an ability landed on
    /// unpredictable; it is replaced by an explicit slot-arming state machine
    /// (see <see cref="_armedSlot"/>) that never compacts.
    /// </remarks>
    [RequireComponent(typeof(UIDocument))]
    public sealed class MenuUiController : MonoBehaviour
    {
        private const string Source = "MenuUI";

        [Header("Page templates (assigned in scene)")]
        [SerializeField] private VisualTreeAsset _mainMenuUxml;
        [SerializeField] private VisualTreeAsset _classSelectUxml;
        // Field name kept as "_characterSelectUxml" (was the whole old character-select
        // screen) to avoid re-pointing the scene reference; it now instantiates Step 2
        // "Loadout" (Assets/UI/CharacterSelect.uxml, reworked in place).
        [SerializeField] private VisualTreeAsset _characterSelectUxml;
        [SerializeField] private VisualTreeAsset _lobbyUxml;

        // ---- Injected services ------------------------------------------------
        private ISessionSelectionService _selection;
        private ILogService              _log;
        private IUGSService              _ugs;
        private SceneLoader              _sceneLoader;
        private ChickenClassRegistrySO   _classRegistry;
        private AbilityRegistrySO        _abilityRegistry;
        private MatchConfigSO            _matchConfig;

        // ---- Runtime state ----------------------------------------------------
        private VisualElement _root;
        // Backdrop (image + tint) sits behind every page and bleeds to the screen edges;
        // the page host holds the pages and carries the safe-area padding.
        private VisualElement _backdrop, _backdropTint, _pageHost;
        private VisualElement _mainMenu, _classSelect, _loadout, _lobby;
        private VisualElement _settingsSheet;
        private bool _isBusy;
        private Rect _appliedSafeArea;
        private Vector2Int _appliedScreenSize;
        // Abilities already reported as having no sprite (logged once each, not per refresh).
        private readonly HashSet<AbilityBaseSO> _reportedMissingIcons = new();

        private readonly Dictionary<ChickenClass, VisualElement> _classChips = new();
        // (pill element, the passive it selects) for every SpecOptA/SpecOptB across
        // all four chips — populated once in BuildClassSelect, used by
        // RefreshClassSelect to toggle the selected-highlight class.
        private readonly List<(VisualElement Pill, PassiveAbilitySO Passive)> _specPills = new();
        // #PreFilledTag per class chip — populated once in BuildClassSelect, repainted by
        // RefreshPreFilledTags. A dictionary rather than a per-chip Q<Label> lookup on every
        // refresh, matching the _classChips caching pattern.
        private readonly Dictionary<ChickenClass, Label> _preFilledTags = new();
        private VisualElement _previewChicken, _previewGlow, _previewDisc;
        private Label _previewName, _previewQuote;
        private Label _roleCalloutStrong, _roleCalloutWeak;
        // .cw-chicken--<class> currently on the big preview figure (for swap).
        private string _previewChickenClass;

        // The two flat pick rows that replaced the slot-hex row + scrolling grid.
        private VisualElement _commonCards, _classCards;
        private Label _commonTitle, _classTitle, _commonHint, _commonCount, _classHint, _classCount;
        // Shared "what does this do" strip — the description's only home. Cards
        // carry icon + name + category/CD; the last-tapped ability's text lands
        // here at full reading size. Null until a card is tapped.
        private VisualElement _abilityDetail;
        private Label _abilityDetailName, _abilityDetailText;
        private AbilityBaseSO _focusedAbility;
        private Button _readyBtn;

        // The four physical slot boxes on the Loadout screen (SlotBox0..3). The
        // ARMED slot is the one the next ability-card tap will act on — see the
        // state machine on OnAbilityCardTapped/AutoArmFirstEmpty. Always a valid
        // index into 0..3 once BuildLoadout has run.
        private VisualElement[] _slotBoxes = new VisualElement[4];
        private int _armedSlot;

        // Dim neutral tint for an empty ability hex (no equipped accent).
        private static readonly Color HexEmptyTint = new Color(0.45f, 0.38f, 0.28f, 0.7f);

        // Direction A surface colours (spec "Visual system"; tokens in CluckWarsTokens.uss).
        // Only the ones C# has to paint inline, because it also sets a state colour on the
        // same element — everything static lives in CluckWarsTheme.uss.
        private static readonly Color Ink        = UiGfx.Hex32("2a1a0c");
        private static readonly Color Cream      = UiGfx.Hex32("fff8e8");
        private static readonly Color CreamInset = UiGfx.Hex32("f3e6c8");
        private static readonly Color InkSoft    = UiGfx.Hex32("6b4a2a");

        private static readonly ChickenClass[] Order =
            { ChickenClass.Warrior, ChickenClass.Speedy, ChickenClass.Fatty, ChickenClass.Assassin };

        private sealed class ClassMeta
        {
            // PassiveName/PassiveDesc were removed on 2026-08-23. They still carried the
            // damage-era passives deleted in 62219fc ("MIGHTY: +25% outgoing ability damage",
            // "COMBO: equips 3 abilities instead of 2") and were rendering on the lobby card.
            // Beyond being stale, a per-CLASS passive name is now structurally wrong: the
            // passive comes from the chosen specialization, so any single hardcoded name is
            // right for at most one of a class's two builds. Role is stable across both.
            public Color Tint;
            public int[] Stats; // cargo, rate, speed (1..5) — HP/Resist removed in v0.4 (no health)
        }

        private static readonly Dictionary<ChickenClass, ClassMeta> Meta = new()
        {
            [ChickenClass.Warrior]  = new ClassMeta { Tint = UiGfx.Hex32("C04030"), Stats = new[]{3,3,3} },
            [ChickenClass.Speedy]   = new ClassMeta { Tint = UiGfx.Hex32("E85A2A"), Stats = new[]{2,3,5} },
            [ChickenClass.Fatty]    = new ClassMeta { Tint = UiGfx.Hex32("F5D75A"), Stats = new[]{5,5,2} },
            [ChickenClass.Assassin] = new ClassMeta { Tint = UiGfx.Hex32("7B68EE"), Stats = new[]{2,2,4} },
        };

        // Class names, roles and callouts are player-facing copy and live in the wording
        // dictionary; this table only picks the keys.
        private readonly struct ClassKeySet
        {
            public readonly string Full, Short, Role, Strong, Weak;
            public ClassKeySet(string full, string shortName, string role, string strong, string weak)
            { Full = full; Short = shortName; Role = role; Strong = strong; Weak = weak; }
        }

        private static readonly Dictionary<ChickenClass, ClassKeySet> ClassKeys = new()
        {
            [ChickenClass.Warrior]  = new ClassKeySet(UiKeys.ClassWarriorName,  UiKeys.ClassWarriorShort,  UiKeys.RoleWarrior,  UiKeys.CalloutWarriorStrong,  UiKeys.CalloutWarriorWeak),
            [ChickenClass.Speedy]   = new ClassKeySet(UiKeys.ClassSpeedyName,   UiKeys.ClassSpeedyShort,   UiKeys.RoleSpeedy,   UiKeys.CalloutSpeedyStrong,   UiKeys.CalloutSpeedyWeak),
            [ChickenClass.Fatty]    = new ClassKeySet(UiKeys.ClassFattyName,    UiKeys.ClassFattyShort,    UiKeys.RoleFatty,    UiKeys.CalloutFattyStrong,    UiKeys.CalloutFattyWeak),
            [ChickenClass.Assassin] = new ClassKeySet(UiKeys.ClassAssassinName, UiKeys.ClassAssassinShort, UiKeys.RoleAssassin, UiKeys.CalloutAssassinStrong, UiKeys.CalloutAssassinWeak),
        };

        private static string ClassFullName(ChickenClass cls) => UiText.Get(ClassKeys[cls].Full);
        private static string ClassShortName(ChickenClass cls) => UiText.Get(ClassKeys[cls].Short);
        private static string RoleName(ChickenClass cls) => UiText.Get(ClassKeys[cls].Role);

        private static readonly string[] ChipNames  = { "ClassWarrior", "ClassSpeedy", "ClassFatty", "ClassAssassin" };

        // Ability categories. These used to be section headers in a scrolling
        // grid; with 3 cards per row that produced headers holding one card each,
        // so the category now rides on the card itself as a colored tag.
        private sealed class CatMeta { public string LabelKey; public Color Color; }
        private static readonly Dictionary<AbilityCategory, CatMeta> Cats = new()
        {
            [AbilityCategory.Steal]   = new CatMeta { LabelKey = UiKeys.CategorySteal, Color = UiGfx.Hex32("FF5722") },
            [AbilityCategory.Control] = new CatMeta { LabelKey = UiKeys.CategoryControl, Color = UiGfx.Hex32("9C27B0") },
            [AbilityCategory.Defense] = new CatMeta { LabelKey = UiKeys.CategoryDefense, Color = UiGfx.Hex32("4CAF50") },
            [AbilityCategory.Utility] = new CatMeta { LabelKey = UiKeys.CategoryUtility, Color = UiGfx.Hex32("00BCD4") },
        };

        // ======================================================================
        [Inject]
        public void Construct(
            ISessionSelectionService selection,
            ILogService log,
            IUGSService ugs,
            [InjectOptional] SceneLoader sceneLoader,
            [InjectOptional] ChickenClassRegistrySO classRegistry,
            [InjectOptional] AbilityRegistrySO abilityRegistry,
            [InjectOptional] MatchConfigSO matchConfig)
        {
            _selection       = selection;
            _log             = log;
            _ugs             = ugs;
            _sceneLoader     = sceneLoader;
            _classRegistry   = classRegistry;
            _abilityRegistry = abilityRegistry;
            _matchConfig     = matchConfig;
        }

        private void Awake()
        {
            if (_selection == null)
                ProjectContext.Instance.Container.Inject(this);

            UiText.SetLogger(_log);

            if (_sceneLoader == null) _sceneLoader = GetComponent<SceneLoader>();
            if (_sceneLoader == null) _sceneLoader = FindFirstObjectByType<SceneLoader>();
            _sceneLoader?.SetAutoLoad(false);

            EnsureEventSystem();
        }

        /// <summary>
        /// UI Toolkit runtime panels need an EventSystem (+ Input System UI module)
        /// to receive pointer clicks. The old UGUI flow created one; replicate that
        /// here so the menu is interactive even if the scene has none.
        /// </summary>
        private static void EnsureEventSystem()
        {
            if (FindFirstObjectByType<UnityEngine.EventSystems.EventSystem>() != null) return;
            new GameObject("EventSystem",
                typeof(UnityEngine.EventSystems.EventSystem),
                typeof(UnityEngine.InputSystem.UI.InputSystemUIInputModule));
        }

        private void OnEnable()
        {
            var doc = GetComponent<UIDocument>();
            _root = doc.rootVisualElement;
            if (_root == null) return;
            // Build next frame so it doesn't matter whether UIDocument.OnEnable ran first.
            _root.schedule.Execute(BuildAll).ExecuteLater(0);
        }

        private void BuildAll()
        {
            if (_root == null) return;
            _root.Clear();
            _root.style.flexGrow = 1;

            // Guaranteed brand font even if a USS url() font ref fails to resolve.
            var f = UiGfx.ChunkyFont();
            if (f != null) _root.style.unityFontDefinition = new StyleFontDefinition(FontDefinition.FromFont(f));

            // The page templates link CluckWarsTheme.uss, but the backdrop, page host and the
            // root-level state classes (.cw-reduced-motion) live ABOVE the templates, where a
            // template's sheet does not reach. Attach the same sheet(s) to the root once.
            if (_mainMenuUxml != null)
                foreach (var sheet in _mainMenuUxml.stylesheets)
                    if (sheet != null && !_root.styleSheets.Contains(sheet)) _root.styleSheets.Add(sheet);

            // Backdrop first (drawn underneath), then the safe-area page host on top.
            _backdrop = new VisualElement { name = "Backdrop", pickingMode = PickingMode.Ignore };
            _backdrop.AddToClassList("cw-backdrop");
            _backdropTint = new VisualElement { name = "BackdropTint", pickingMode = PickingMode.Ignore };
            _backdropTint.AddToClassList("cw-backdrop-tint");
            _pageHost = new VisualElement { name = "PageHost" };
            _pageHost.AddToClassList("cw-page-host");
            _pageHost.style.flexGrow = 1;
            StretchToParent(_backdrop);
            StretchToParent(_backdropTint);
            _root.Add(_backdrop);
            _root.Add(_backdropTint);
            _root.Add(_pageHost);

            _mainMenu    = ClonePage(_mainMenuUxml);
            _classSelect = ClonePage(_classSelectUxml);
            _loadout     = ClonePage(_characterSelectUxml);
            _lobby       = ClonePage(_lobbyUxml);

            BuildMainMenu();
            BuildClassSelect();
            BuildLoadout();
            BuildLobby();
            ApplyReducedMotion();

            _root.RegisterCallback<GeometryChangedEvent>(OnRootGeometry);
            UpdateLayout(_root.resolvedStyle.width, _root.resolvedStyle.height);

            // Next on the class screen is never gated (a class is always selected), so a
            // specialization must be too — otherwise a player who skips the pills reaches the
            // loadout with every slot full, no Passive, and a permanently disabled READY.
            if (_selection != null && _selection.Passive == null)
            {
                var cls = _selection.SelectedClass;
                var def = _abilityRegistry?.GetDefaultPassiveForClass(cls);
                if (def != null) SelectClassAndPassive(cls, def);
            }

            ShowMainMenu();
            ConsumePostMatchIntent();
        }

        private VisualElement ClonePage(VisualTreeAsset vta)
        {
            if (vta == null) return new VisualElement();
            var ve = vta.Instantiate();
            UiText.ResolveTree(ve);
            ve.style.flexGrow = 1;
            ve.style.display = DisplayStyle.None;
            _pageHost.Add(ve);
            return ve;
        }

        private static void StretchToParent(VisualElement ve)
        {
            ve.style.position = Position.Absolute;
            ve.style.left = 0; ve.style.top = 0; ve.style.right = 0; ve.style.bottom = 0;
        }

        // ---- Layout -----------------------------------------------------------
        private void OnRootGeometry(GeometryChangedEvent evt)
        {
            UpdateLayout(evt.newRect.width, evt.newRect.height);
            ApplySafeArea(force: true);
        }

        private void UpdateLayout(float w, float h)
        {
            bool landscape = w >= h;
            _root.EnableInClassList("layout--landscape", landscape);
            _root.EnableInClassList("layout--portrait", !landscape);
        }

        // A notch or gesture bar can change Screen.safeArea without the panel's geometry
        // changing (e.g. the system bars toggling), so it is also polled — two struct
        // compares a frame, no allocation.
        private void Update() => ApplySafeArea(force: false);

        /// <summary>
        /// Pads <see cref="_pageHost"/> so no page content sits under a notch, cutout or the
        /// gesture bar, while the backdrop keeps bleeding to the screen edges.
        /// </summary>
        /// <remarks>
        /// <c>Screen.safeArea</c> is in screen pixels with a bottom-left origin; the panel works
        /// in its own scaled units with a top-left origin, so each corner goes through
        /// <see cref="RuntimePanelUtils.ScreenToPanel"/> after flipping Y. When the safe area is
        /// the whole screen (desktop, Editor, the off-screen capture tool rendering into a
        /// RenderTexture of a different size than the Game view) the padding is zero rather
        /// than a conversion of a rect that does not describe this panel.
        /// </remarks>
        private void ApplySafeArea(bool force)
        {
            if (_pageHost == null || _pageHost.panel == null) return;

            var sa = Screen.safeArea;
            var screen = new Vector2Int(Screen.width, Screen.height);
            if (!force && sa == _appliedSafeArea && screen == _appliedScreenSize) return;
            _appliedSafeArea = sa;
            _appliedScreenSize = screen;

            float left = 0, top = 0, right = 0, bottom = 0;
            bool fullScreen = sa.xMin <= 0f && sa.yMin <= 0f && sa.xMax >= screen.x && sa.yMax >= screen.y;
            if (!fullScreen && screen.x > 0 && screen.y > 0)
            {
                var panel = _pageHost.panel;
                Vector2 min = RuntimePanelUtils.ScreenToPanel(panel, new Vector2(sa.xMin, screen.y - sa.yMax));
                Vector2 max = RuntimePanelUtils.ScreenToPanel(panel, new Vector2(sa.xMax, screen.y - sa.yMin));
                Vector2 full = RuntimePanelUtils.ScreenToPanel(panel, new Vector2(screen.x, screen.y));
                left   = Mathf.Max(0f, min.x);
                top    = Mathf.Max(0f, min.y);
                right  = Mathf.Max(0f, full.x - max.x);
                bottom = Mathf.Max(0f, full.y - max.y);
            }

            _pageHost.style.paddingLeft = left;
            _pageHost.style.paddingTop = top;
            _pageHost.style.paddingRight = right;
            _pageHost.style.paddingBottom = bottom;
        }

        // ---- Backdrop -----------------------------------------------------------
        /// <summary>How a page wants the shared backdrop: which image, and the lift tint over it.</summary>
        private readonly struct BackdropSpec
        {
            public readonly string Resource; // Resources path of a Texture2D, or null = the default plate
            public readonly Color Tint;
            public BackdropSpec(string resource, Color tint) { Resource = resource; Tint = tint; }
        }

        // Warm golden-hour lift over the dusk plate. Per page so Phase 2 can drop in a
        // Backgrounds/Bg_* image for a screen by changing one row here (Resource), without
        // touching any UXML. Null Resource keeps the default plate from .cw-backdrop.
        private static readonly Color WarmLift = new Color(1f, 0.77f, 0.47f, 0.24f);
        private static readonly Color WarmLiftBusy = new Color(1f, 0.80f, 0.55f, 0.30f);

        private BackdropSpec BackdropFor(VisualElement page)
        {
            if (page == _classSelect)
            {
                // Choose Your Chicken: tinted by the selected class (lightened so the plate
                // still lifts toward golden hour rather than darkening into the class hue).
                var t = Lighten(TintOf(Cls), 0.25f);
                return new BackdropSpec(null, new Color(t.r, t.g, t.b, 0.34f));
            }
            if (page == _mainMenu) return new BackdropSpec(null, WarmLift);
            return new BackdropSpec(null, WarmLiftBusy);
        }

        private VisualElement _currentPage;

        /// <summary>Applies <see cref="BackdropFor"/> for the current page to the shared backdrop.</summary>
        private void ApplyBackdrop()
        {
            if (_backdrop == null || _backdropTint == null) return;
            var spec = BackdropFor(_currentPage);

            Texture2D tex = null;
            if (!string.IsNullOrEmpty(spec.Resource))
            {
                tex = Resources.Load<Texture2D>(spec.Resource);
                if (tex == null) _log?.Warn(Source, $"Backdrop '{spec.Resource}' not found in Resources; keeping the default plate.");
            }
            // StyleKeyword.Null hands the image back to .cw-backdrop (the default plate).
            _backdrop.style.backgroundImage = tex != null ? new StyleBackground(tex) : new StyleBackground(StyleKeyword.Null);
            _backdropTint.style.backgroundColor = spec.Tint;
        }

        // ---- Navigation -------------------------------------------------------
        private void ShowMainMenu()    { SetPage(_mainMenu); RefreshDevRow(); RefreshPlayAgain(); }
        private void ShowClassSelect() { SetPage(_classSelect); RefreshClassSelect(); }
        private void ShowLoadout()     { SetPage(_loadout); RefreshLoadout(); }
        private void ShowLobby()       { SetPage(_lobby); RefreshLobby(); }

        private void SetPage(VisualElement page)
        {
            if (page != _mainMenu) CloseSettings();
            _currentPage = page;
            if (_mainMenu    != null) _mainMenu.style.display    = page == _mainMenu    ? DisplayStyle.Flex : DisplayStyle.None;
            if (_classSelect != null) _classSelect.style.display = page == _classSelect ? DisplayStyle.Flex : DisplayStyle.None;
            if (_loadout     != null) _loadout.style.display     = page == _loadout     ? DisplayStyle.Flex : DisplayStyle.None;
            if (_lobby       != null) _lobby.style.display       = page == _lobby       ? DisplayStyle.Flex : DisplayStyle.None;
            ApplyBackdrop();
        }

        // ======================================================================
        //  MAIN MENU
        // ======================================================================
        private void BuildMainMenu()
        {
            Bind<Button>(_mainMenu, "SoloBtn", b => b.clicked += () => ChooseMode(SessionMode.Solo));
            Bind<Button>(_mainMenu, "HostBtn", b => b.clicked += () => ChooseMode(SessionMode.Host));
            Bind<Button>(_mainMenu, "JoinBtn", b => b.clicked += () => ChooseMode(SessionMode.Join));
            Bind<Button>(_mainMenu, "AbilityLabBtn", b => b.clicked += OpenAbilityLab);
            Bind<Button>(_mainMenu, "PlayAgainBtn", b => b.clicked += OnPlayAgainFromMenu);

            // Build stamp — a tester reporting a bug from a device otherwise has no
            // way to say which build produced it.
            Bind<Label>(_mainMenu, "BuildStamp", l => l.text = UiText.Format(UiKeys.MainBuild, ("version", Application.version)));

            BuildSettingsSheet();
        }

        // ======================================================================
        //  SETTINGS SHEET (main menu gear -> modal)
        // ======================================================================
        /// <summary>
        /// Wires the gear, the modal sheet and its four preference rows. The sheet is static
        /// markup in MainMenu.uxml (#SettingsSheet); this only binds it.
        /// </summary>
        private void BuildSettingsSheet()
        {
            _settingsSheet = _mainMenu.Q<VisualElement>("SettingsSheet");
            if (_settingsSheet == null)
            {
                _log?.Error(Source, "MainMenu.uxml has no #SettingsSheet; the settings gear will do nothing.");
                return;
            }

            Bind<Button>(_mainMenu, "SettingsBtn", b => b.clicked += OpenSettings);
            Bind<Button>(_settingsSheet, "SettingsCloseBtn", b => b.clicked += CloseSettings);
            // A tap on the dimmed scrim (outside the panel) closes too; taps inside the panel
            // bubble up here with a different target and are ignored.
            _settingsSheet.RegisterCallback<ClickEvent>(evt =>
            {
                if (evt.target == _settingsSheet) CloseSettings();
            });

            BindSettingRow("RangeGuidesRow", "RangeGuidesToggle",
                () => PlayerPreferences.AbilityRangeGuidesEnabled,
                v => PlayerPreferences.AbilityRangeGuidesEnabled = v, "Ability range guides");
            BindSettingRow("ReducedMotionRow", "ReducedMotionToggle",
                () => PlayerPreferences.ReducedMotionEnabled,
                v => { PlayerPreferences.ReducedMotionEnabled = v; ApplyReducedMotion(); }, "Reduced motion");
            BindSettingRow("PerformanceModeRow", "PerformanceModeToggle",
                () => PlayerPreferences.PerformanceModeEnabled,
                v => PlayerPreferences.PerformanceModeEnabled = v, "Performance mode");
            // Dev Mode refreshes #DevRow the moment it changes — the Ability Lab button sits
            // right behind the sheet, so waiting for the next ShowMainMenu would look broken.
            BindSettingRow("DeveloperModeRow", "DeveloperModeToggle",
                () => PlayerPreferences.DeveloperModeEnabled,
                v => { PlayerPreferences.DeveloperModeEnabled = v; RefreshDevRow(); }, "Developer mode");
        }

        /// <summary>
        /// Binds one settings row: seeds its Toggle from the preference and writes changes
        /// back, and makes the WHOLE row the touch target (a tap on the name or description
        /// flips the toggle too).
        /// </summary>
        /// <remarks>
        /// Seeded with <c>SetValueWithoutNotify</c>: the seed is not a player choice, and a
        /// ChangeEvent would echo the stored value straight back to <c>PlayerPrefs</c> on every
        /// build, turning "never chosen, using the default" into "explicitly chosen" (for Dev
        /// Mode it would write the key on every player's machine; for Performance Mode it would
        /// freeze the platform default). Re-seeded on every <see cref="OpenSettings"/> so the
        /// sheet always shows the stored value, whatever changed it.
        /// </remarks>
        private void BindSettingRow(string rowName, string toggleName, Func<bool> get, Action<bool> set, string logName)
        {
            var row = _settingsSheet.Q<VisualElement>(rowName);
            var toggle = _settingsSheet.Q<Toggle>(toggleName);
            if (row == null || toggle == null)
            {
                _log?.Error(Source, $"Settings sheet is missing #{rowName} or #{toggleName}; '{logName}' cannot be changed from the menu.");
                return;
            }

            toggle.SetValueWithoutNotify(get());
            toggle.RegisterValueChangedCallback(evt =>
            {
                set(evt.newValue);
                _log?.Info(Source, $"{logName} {(evt.newValue ? "enabled" : "disabled")}.");
            });
            // The Toggle handles taps on itself; anywhere else in the row flips it here.
            row.RegisterCallback<ClickEvent>(evt =>
            {
                if (evt.target is VisualElement t && (t == toggle || toggle.Contains(t))) return;
                toggle.value = !toggle.value;
            });
            _settingToggles.Add((toggle, get));
        }

        private readonly List<(Toggle Toggle, Func<bool> Get)> _settingToggles = new();

        private void OpenSettings()
        {
            if (_settingsSheet == null) return;
            foreach (var (toggle, get) in _settingToggles) toggle.SetValueWithoutNotify(get());
            _settingsSheet.style.display = DisplayStyle.Flex;
        }

        private void CloseSettings()
        {
            if (_settingsSheet == null || _settingsSheet.style.display == DisplayStyle.None) return;
            _settingsSheet.style.display = DisplayStyle.None;
            RefreshDevRow();
        }

        /// <summary>
        /// Mirrors <see cref="PlayerPreferences.ReducedMotionEnabled"/> onto the root as
        /// <c>.cw-reduced-motion</c>, which zeroes every menu transition and hover/press pop
        /// (CluckWarsTheme.uss). Any animation added to these menus must be gated the same way.
        /// </summary>
        private void ApplyReducedMotion() =>
            _root?.EnableInClassList("cw-reduced-motion", PlayerPreferences.ReducedMotionEnabled);

        /// <summary>
        /// Shows or hides <c>#DevRow</c> to match
        /// <see cref="PlayerPreferences.DeveloperModeEnabled"/>.
        /// </summary>
        /// <remarks>
        /// Called from <see cref="ShowMainMenu"/>, when the Dev Mode toggle changes and when the
        /// settings sheet closes — not once from <see cref="BuildMainMenu"/>. Binding once would
        /// mean the button only appeared on the next launch, which on a phone is a
        /// reinstall-and-relaunch cycle to discover a feature that is already there.
        /// </remarks>
        private void RefreshDevRow()
        {
            Bind<VisualElement>(_mainMenu, "DevRow", r =>
                r.style.display = PlayerPreferences.DeveloperModeEnabled
                    ? DisplayStyle.Flex
                    : DisplayStyle.None);
        }

        /// <summary>
        /// Build-Settings name of the lab scene. Enabled in Build Settings since 2026-09-04 so
        /// it exists on a device; reaching it is gated on Developer Mode, not on the build.
        /// </summary>
        private const string AbilityLabSceneName = "AbilityLab";

        /// <summary>
        /// Loads <c>AbilityLab.unity</c>, the developer ability-feel scene.
        /// </summary>
        /// <remarks>
        /// <c>SceneManager</c> directly rather than <c>SceneLoader</c>: that component exists
        /// to run the Bootstrap → Game handoff and carries a serialized "next scene", so
        /// pointing it at the lab would mean mutating the menu's own flow to take a detour.
        /// The lab is a leaf — it loads, and its MENU button loads Bootstrap back.
        ///
        /// No <c>ISessionSelectionService</c> write here. <c>AbilityLabBootstrapper.Start</c>
        /// pins the mode to Solo itself, deliberately, rather than inheriting whatever the
        /// menu last left behind — its dummy patrol and held match timer are only sound on a
        /// single peer.
        /// </remarks>
        private void OpenAbilityLab()
        {
            if (!PlayerPreferences.DeveloperModeEnabled)
            {
                // Not reachable through the UI — the row is hidden. Refuse anyway rather than
                // trust that, so the gate holds even if a later change shows the row wrongly.
                _log?.Warn(Source, "Ability Lab requested with Developer Mode off. Ignoring.");
                return;
            }

            _log?.Info(Source, "Opening the Ability Lab.");
            UnityEngine.SceneManagement.SceneManager.LoadScene(AbilityLabSceneName);
        }

        // ======================================================================
        //  REPLAY FLOW: last setup, main-menu PLAY AGAIN, post-match BACK TO LOBBY
        // ======================================================================
        // Menu overhaul decision 5: never automatic. The last committed setup (class, perk,
        // loadout, mode) is saved when READY is pressed and validated on every read.

        private string _reportedLastSetupProblem;

        /// <summary>The stored last setup resolved against the live registry, or false (with the
        /// reason logged once as a Warning) if there is none or it no longer validates.</summary>
        private bool TryGetLastSetup(out ResolvedLastSetup setup)
        {
            setup = default;
            var record = PlayerPreferences.LastSetup;
            if (record == null) return false;
            if (LastSetupResolver.TryResolve(record, _abilityRegistry?.All, out setup, out var problem))
                return true;

            if (problem != _reportedLastSetupProblem)
            {
                _reportedLastSetupProblem = problem;
                _log?.Warn(Source, $"Stored last setup ignored: {problem}.");
            }
            return false;
        }

        /// <summary>Stores the setup the player just committed (called when READY is pressed).</summary>
        private void SaveLastSetup()
        {
            if (_selection?.Passive == null) return;
            var slots = new[] { _selection.Ability0, _selection.Ability1, _selection.Ability2, _selection.Ability3 };
            PlayerPreferences.LastSetup = LastSetupResolver.Record(Cls, _selection.Passive, slots, _selection.Mode);
        }

        /// <summary>Shows or hides the main-menu PLAY AGAIN row and fills its "{cls} · {perk}" sub-line.</summary>
        private void RefreshPlayAgain()
        {
            bool valid = TryGetLastSetup(out var setup);
            Bind<VisualElement>(_mainMenu, "PlayAgainRow", r =>
                r.style.display = valid ? DisplayStyle.Flex : DisplayStyle.None);
            if (!valid) return;
            Bind<Label>(_mainMenu, "PlayAgainSub", l => l.text = UiText.Format(UiKeys.BtnPlayAgainSub,
                ("cls", ClassShortName(setup.Class)), ("perk", setup.Passive.DisplayName.ToUpperInvariant())));
        }

        private void OnPlayAgainFromMenu()
        {
            if (_isBusy || !TryGetLastSetup(out var setup)) return;
            OpenLobbyWithLastSetup(MatchFlowRules.MainMenuPlayAgainMode(setup.Mode));
        }

        /// <summary>
        /// Entry point: restore the last class / perk / loadout into the menu state and open THE
        /// COOP in <paramref name="mode"/>. Never starts a match. Returns false (staying on the
        /// current page) when there is no valid last setup. Used by the main-menu PLAY AGAIN and by
        /// the post-match BACK TO LOBBY handoff (<see cref="ConsumePostMatchIntent"/>).
        /// </summary>
        public bool OpenLobbyWithLastSetup(SessionMode mode)
        {
            if (_selection == null || !TryGetLastSetup(out var setup)) return false;

            _focusedAbility = null;
            _selection.Mode = mode;
            _selection.SelectedClass = setup.Class;
            _selection.Passive = setup.Passive;
            _selection.Ability0 = setup.Slots[0];
            _selection.Ability1 = setup.Slots[1];
            _selection.Ability2 = setup.Slots[2];
            _selection.Ability3 = setup.Slots[3];
            _log?.Info(Source, $"Opening THE COOP with the last setup: {setup.Class} / {setup.Passive.name}, mode {mode}.");
            ShowLobby();
            return true;
        }

        /// <summary>
        /// Post-match BACK TO LOBBY lands here: <see cref="ISessionSelectionService.OpenLobbyOnMenuLoad"/>
        /// is set by the match overlay before it loads Bootstrap. Read once, cleared at once.
        /// Solo and Host reopen THE COOP; a joiner (or an invalid setup) stays on the main menu.
        /// </summary>
        private void ConsumePostMatchIntent()
        {
            if (_selection == null || !_selection.OpenLobbyOnMenuLoad) return;
            _selection.OpenLobbyOnMenuLoad = false;

            bool valid = TryGetLastSetup(out var setup);
            if (MatchFlowRules.LandingAfterMatch(setup.Mode, valid) == MenuLanding.Lobby)
                OpenLobbyWithLastSetup(setup.Mode);
        }

        private void ChooseMode(SessionMode mode)
        {
            if (_selection != null) _selection.Mode = mode;
            ShowClassSelect();
        }

        // ======================================================================
        //  STEP 1 — CLASS SELECT ("Choose Your Chicken")
        // ======================================================================
        private void BuildClassSelect()
        {
            _classChips.Clear();
            _specPills.Clear();
            _preFilledTags.Clear();

            for (int i = 0; i < Order.Length; i++)
            {
                var cls = Order[i];
                var chip = _classSelect.Q<VisualElement>(ChipNames[i]);
                if (chip == null) continue;
                _classChips[cls] = chip;

                var preFilled = chip.Q<Label>("PreFilledTag");
                if (preFilled != null) _preFilledTags[cls] = preFilled;

                // Class chip art — exported design chicken sprite (Stage-3). Chips
                // are fixed per class, so the modifier is applied once here.
                var art = chip.Q<VisualElement>(ChipNames[i] + "Art");
                if (art != null) art.AddToClassList("cw-chicken--" + KeyOf(cls));

                // Signature first, alternative second — registry order would put Bracer
                // ahead of Mighty and Second Wind ahead of Slippery, reading as if the
                // alternative were the class's identity.
                var passives = _abilityRegistry?.GetPassivesForClass(cls)
                                                .OrderByDescending(p => p.IsSignature)
                                                .ToList();
                if (passives != null && passives.Count >= 2)
                {
                    BindSpecPill(chip.Q<VisualElement>("SpecOptA"), cls, passives[0]);
                    BindSpecPill(chip.Q<VisualElement>("SpecOptB"), cls, passives[1]);
                }

                // Deliberately no click handler on the chip root itself: Maestro's
                // one-tap requirement means class + specialization are chosen together
                // by tapping a pill (below), so the chip body has no separate
                // "class only" action any more.
            }

            _previewChicken    = _classSelect.Q<VisualElement>("PreviewChicken");
            _previewGlow       = _classSelect.Q<VisualElement>("PreviewGlow");
            _previewDisc       = _classSelect.Q<VisualElement>("PreviewDisc");
            _previewName       = _classSelect.Q<Label>("PreviewName");
            _previewQuote      = _classSelect.Q<Label>("PreviewQuote");
            _roleCalloutStrong = _classSelect.Q<Label>("RoleCalloutStrong");
            _roleCalloutWeak   = _classSelect.Q<Label>("RoleCalloutWeak");

            Bind<Button>(_classSelect, "HomeBtn", b => b.clicked += ShowMainMenu);
            // Always enabled — BuildAll already seeds a default SelectedClass, so
            // there is never a state where Step 1 has nothing chosen yet.
            Bind<Button>(_classSelect, "NextBtn", b => b.clicked += ShowLoadout);
        }

        /// <summary>
        /// Wires one SpecOptA/SpecOptB pill: fills its name/description/signature-line
        /// children by CSS class (the pill names repeat across all four chips, so these
        /// are queried per-chip, not globally) and registers the one-tap
        /// class+specialization select.
        /// </summary>
        private void BindSpecPill(VisualElement pill, ChickenClass cls, PassiveAbilitySO passive)
        {
            if (pill == null || passive == null) return;

            var nameLbl = pill.Q<Label>(className: "cw-spec-pill__name");
            var descLbl = pill.Q<Label>(className: "cw-spec-pill__desc");
            var sigLbl  = pill.Q<Label>(className: "cw-spec-pill__signature");

            if (nameLbl != null) nameLbl.text = passive.DisplayName.ToUpperInvariant();
            // Never ShortLabel — that is the ≤4-char HUD abbreviation. The line is a template
            // filled from the passive's own fields, so it can never quote a stale number.
            if (descLbl != null) descLbl.text = passive.PerkLine(_matchConfig);

            if (sigLbl != null)
            {
                // Accurate language: this occupies one of the four ability slots, it is
                // never a free bonus.
                string label = ForcedAbilityLabel(cls, passive);
                string sig = label != null ? UiText.Format(UiKeys.PillStartsWith, ("abilities", label)) : null;
                sigLbl.text = sig ?? string.Empty;
                sigLbl.style.display = sig != null ? DisplayStyle.Flex : DisplayStyle.None;
            }

            pill.RegisterCallback<ClickEvent>(evt =>
            {
                // Without this the click would bubble to the (handler-less) chip root
                // too; StopPropagation just makes that explicit rather than relying on
                // there being nothing to run.
                evt.StopPropagation();
                SelectClassAndPassive(cls, passive);
            });

            _specPills.Add((pill, passive));
        }

        /// <summary>
        /// Selects a class and its specialization (passive) in one tap — Maestro's hard
        /// requirement, replacing the old two-step SelectClass/SelectPassive. Old picks
        /// only get cleared when the CLASS actually changes; re-selecting the same
        /// class's other pill must not wipe an in-progress loadout.
        /// </summary>
        private void SelectClassAndPassive(ChickenClass cls, PassiveAbilitySO passive)
        {
            if (_selection == null) return;

            if (cls != _selection.SelectedClass)
            {
                // Old picks may be illegal for the new class.
                _focusedAbility = null;
                _selection.Ability0 = null;
                _selection.Ability1 = null;
                _selection.Ability2 = null;
                _selection.Ability3 = null;
            }

            _selection.SelectedClass = cls;
            _selection.Passive = passive;

            // Seed the mandatory Peck and/or this specialization's signature ability so the
            // picker shows the same loadout the spawner would build. Without this the player
            // composes four abilities, hits READY, and MatchBootstrapper silently swaps one
            // out — a UI that lied.
            SeedForcedAbilities();
            AutoArmFirstEmpty();

            RefreshClassSelect();
        }

        private ChickenClass Cls => _selection?.SelectedClass ?? ChickenClass.Warrior;
        /// <summary>
        /// Total active-ability slots the loadout has to fill — four for every class as
        /// of v0.7, which retired COMBO's old job of granting a third. Purely a slot
        /// COUNT: any class-legal ability, Common or Character, may land in any slot.
        /// Peck and/or the chosen specialization's signature are the only forced
        /// occupants — see <see cref="SeedForcedAbilities"/>.
        /// </summary>
        private int ActiveSlotsForClass => AbilityController.SlotCount;

        private Color TintOf(ChickenClass cls)
        {
            if (_classRegistry != null && _classRegistry.TryGet(cls, out var e) && e.TintColor.a > 0f)
                return e.TintColor;
            return Meta[cls].Tint;
        }

        private void RefreshClassSelect()
        {
            var cls = Cls;

            foreach (var kv in _classChips)
            {
                bool sel = kv.Key == cls;
                kv.Value.EnableInClassList("cw-card--selected", sel);
                // Selected: class-tint border (widened in USS) + the gold CardGlowFrame child
                // + a faint class wash over the cream, so selection never rests on colour
                // alone. Unselected: the ink outline every cream card carries.
                SetBorder(kv.Value, sel ? TintOf(kv.Key) : Ink);
                kv.Value.style.backgroundColor = sel ? Color.Lerp(Cream, TintOf(kv.Key), 0.16f) : Cream;
            }

            foreach (var (pill, passive) in _specPills)
                pill.EnableInClassList("cw-spec-pill--selected", _selection?.Passive == passive);

            if (_previewChicken != null)
            {
                if (!string.IsNullOrEmpty(_previewChickenClass))
                    _previewChicken.RemoveFromClassList(_previewChickenClass);
                _previewChickenClass = "cw-chicken--" + KeyOf(cls);
                _previewChicken.AddToClassList(_previewChickenClass);
            }
            if (_previewGlow != null)
                _previewGlow.style.unityBackgroundImageTintColor = Fade(TintOf(cls), 0.35f);
            if (_previewDisc != null)
            {
                var tint = TintOf(cls);
                SetBorder(_previewDisc, Fade(tint, 0.55f));
                _previewDisc.style.backgroundColor = Fade(tint, 0.10f); // subtle class-tinted platform
            }
            // Raw class tint as text on the near-black screen bg is too dark to read
            // — Warrior's #C04030 lands at 2.5:1, under the 3:1 large-text minimum.
            // Lighten for type only; the disc border and glow keep the pure tint.
            if (_previewName != null) { _previewName.text = ClassFullName(cls); _previewName.style.color = Lighten(TintOf(cls), 0.35f); }
            if (_previewQuote != null)
            {
                if (_classRegistry != null && _classRegistry.TryGet(cls, out var entry) && !string.IsNullOrEmpty(entry.LoreQuote))
                    _previewQuote.text = UiText.Format(UiKeys.ClassQuote, ("quote", entry.LoreQuote));
                else
                    _previewQuote.text = "";
            }

            RefreshRoleCallouts(cls);
            RefreshPreFilledTags();
            // The backdrop on this page is tinted by the selected class.
            if (_currentPage == _classSelect) ApplyBackdrop();
        }

        /// <summary>
        /// Paints every class chip's #PreFilledTag from whichever ability
        /// <see cref="ForcedAbilityLabel"/> reports for it — the selected class's actual chosen
        /// passive, or the other three classes' default (signature) passive as a preview of
        /// what tapping in would force. Hidden for a class with nothing forced, so the tag
        /// never renders as an empty pill.
        /// </summary>
        private void RefreshPreFilledTags()
        {
            var cls = Cls;
            foreach (var (c, tag) in _preFilledTags)
            {
                var passive = c == cls ? _selection?.Passive : DefaultPassiveFor(c);
                string label = ForcedAbilityLabel(c, passive);

                if (label != null)
                {
                    // Real language: it occupies active slots, never a free bonus on top of
                    // them — see AbilityBaseSO.PeckSlotPreEquippedBy / SignaturePreEquippedBy. Two names = two slots.
                    bool two = label.Contains(" + ");
                    tag.text = UiText.Format(two ? UiKeys.TagPreFilledTwo : UiKeys.TagPreFilledOne, ("abilities", label));
                    tag.style.display = DisplayStyle.Flex;
                }
                else
                {
                    tag.text = string.Empty;
                    tag.style.display = DisplayStyle.None;
                }
            }
        }

        /// <summary>
        /// Paints RoleCalloutStrong/RoleCalloutWeak from the dictionary's
        /// <c>callout.&lt;class&gt;.strong/.weak</c> copy. Every class has both lines (Warrior's
        /// weak line is "Great at nothing.") so the second row is never empty.
        /// </summary>
        private void RefreshRoleCallouts(ChickenClass cls)
        {
            var keys = ClassKeys[cls];
            SetCallout(_roleCalloutStrong, UiText.Get(keys.Strong));
            SetCallout(_roleCalloutWeak, UiText.Get(keys.Weak));
        }

        /// <summary>
        /// Fills one callout, hiding its whole row (the +/− glyph included) when the copy is
        /// blank, so an emptied dictionary row can never leave a dangling glyph behind.
        /// </summary>
        private static void SetCallout(Label callout, string text)
        {
            if (callout == null) return;
            bool show = !string.IsNullOrWhiteSpace(text);
            callout.text = show ? text : string.Empty;
            var row = callout.parent ?? callout;
            row.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
        }

        /// <summary>
        /// Paints the shared detail strip from <see cref="_focusedAbility"/>. Until
        /// a card is tapped it shows a prompt rather than an ability, so the strip
        /// never renders as an empty box the player has to interpret.
        /// </summary>
        private void RefreshAbilityDetail()
        {
            if (_abilityDetailText == null) return;

            var ab = _focusedAbility;
            if (ab == null)
            {
                if (_abilityDetailName != null)
                {
                    _abilityDetailName.text = string.Empty;
                    _abilityDetailName.style.display = DisplayStyle.None;
                }
                _abilityDetailText.text = UiText.Get(UiKeys.LoadoutDetailEmpty);
                _abilityDetailText.style.color = InkSoft;
                if (_abilityDetail != null) SetBorder(_abilityDetail, Ink);
                return;
            }

            if (_abilityDetailName != null)
            {
                string label = !string.IsNullOrEmpty(ab.DisplayName) ? ab.DisplayName : ab.name;
                _abilityDetailName.text = label.ToUpperInvariant();
                // Ink, not the accent: accents span the whole luminance range and several
                // (Egg Shell, Featherfoot) vanish on cream. The accent goes on the border.
                _abilityDetailName.style.color = Ink;
                _abilityDetailName.style.display = DisplayStyle.Flex;
            }
            _abilityDetailText.text = ab.Description;
            _abilityDetailText.style.color = Ink;
            if (_abilityDetail != null) SetBorder(_abilityDetail, ab.AccentColor);
        }

        // ======================================================================
        //  STEP 2 — LOADOUT ("Build Your Loadout")
        // ======================================================================
        private void BuildLoadout()
        {
            _commonCards    = _loadout.Q<VisualElement>("CommonCards");
            _classCards     = _loadout.Q<VisualElement>("ClassCards");
            _commonTitle    = _loadout.Q<Label>("CommonTitle");
            _classTitle     = _loadout.Q<Label>("ClassTitle");
            _commonHint     = _loadout.Q<Label>("CommonHint");
            _commonCount    = _loadout.Q<Label>("CommonCount");
            _classHint      = _loadout.Q<Label>("ClassHint");
            _classCount     = _loadout.Q<Label>("ClassCount");
            _abilityDetail     = _loadout.Q<VisualElement>("AbilityDetail");
            _abilityDetailName = _loadout.Q<Label>("AbilityDetailName");
            _abilityDetailText = _loadout.Q<Label>("AbilityDetailText");
            _readyBtn       = _loadout.Q<Button>("ReadyBtn");
            if (_readyBtn != null) _readyBtn.clicked += OnReady;

            _slotBoxes = new VisualElement[4];
            for (int i = 0; i < _slotBoxes.Length; i++)
            {
                var box = _loadout.Q<VisualElement>("SlotBox" + i);
                _slotBoxes[i] = box;
                if (box == null) continue;
                int captured = i; // closures capture by reference — copy the loop var
                box.RegisterCallback<ClickEvent>(_ => OnSlotBoxTapped(captured));
            }

            Bind<Button>(_loadout, "BackBtn", b => b.clicked += ShowClassSelect);
        }

        /// <summary>
        /// Entering Step 2 (or returning to it) always re-arms the first empty slot before
        /// anything else refreshes, so there is never a moment where the screen is visible
        /// but no slot is a visible tap target.
        /// </summary>
        private void RefreshLoadout()
        {
            AutoArmFirstEmpty();
            RebuildPickRows(Cls);
            RefreshAbilityDetail();
            RefreshEquippedState();
            RefreshSlotBoxes();
        }

        // ----------------------------------------------------------------------
        //  Slot-arming state machine — the actual fix for "feels like a lottery".
        // ----------------------------------------------------------------------
        //  _armedSlot is the slot the next ability-card tap acts on. Tapping a
        //  slot box just re-arms; tapping an ability card resolves against
        //  whichever slot is currently armed. Nothing ever compacts — a slot
        //  that goes empty stays empty until the player deliberately fills it.
        // ----------------------------------------------------------------------

        /// <summary>
        /// Arms the lowest-indexed empty slot, or slot 0 if every slot is full. Called on
        /// every entry to Step 2 and after any pick that fills a slot, so there is always a
        /// visible target for the next tap without the player having to manually re-arm.
        /// </summary>
        private void AutoArmFirstEmpty()
        {
            int n = ActiveSlotsForClass;
            for (int i = 0; i < n; i++)
            {
                if (GetEquipped(i) == null) { _armedSlot = i; return; }
            }

            if (n <= 0)
            {
                // Unreachable today — ActiveSlotsForClass is AbilityController.SlotCount, a
                // compile-time constant > 0 — but if that ever stops being true there is no
                // valid slot to arm. Log rather than leave _armedSlot pointing at nothing.
                _log?.Warn(Source, "AutoArmFirstEmpty: ActiveSlotsForClass <= 0, defaulting to slot 0.");
            }
            // Everything is full: arm the first slot the player may actually change.
            _armedSlot = 0;
            for (int i = 0; i < n; i++) if (!IsSlotLocked(i)) { _armedSlot = i; break; }
        }

        /// <summary>Tapping a slot box (filled or empty) only re-arms it — no equip/unequip. Locked pre-equip slots ignore taps.</summary>
        private void OnSlotBoxTapped(int index)
        {
            if (IsSlotLocked(index)) return; // pre-equipped slots cannot be armed, cleared or swapped
            _armedSlot = index;
            RefreshSlotBoxes();
        }

        /// <summary>
        /// One tap on an ability card does one of three things depending on where
        /// <paramref name="ab"/> currently sits relative to <see cref="_armedSlot"/>:
        /// <list type="bullet">
        /// <item>already IN the armed slot → clear it (armed slot stays armed, now empty)</item>
        /// <item>equipped in some OTHER slot → swap it into the armed slot</item>
        /// <item>not equipped anywhere → place it in the armed slot, then auto-advance
        /// arming to the next open slot as a convenience</item>
        /// </list>
        /// This subsumes the old Peck-only "tap to move" special case — every ability now
        /// moves the same way — and there is no "all full, drop the oldest" branch any more:
        /// every case resolves into exactly one of the four slots without needing to make
        /// room by shifting.
        /// </summary>
        private void OnAbilityCardTapped(AbilityBaseSO ab)
        {
            if (_selection == null || ab == null) return;

            _focusedAbility = ab;
            RefreshAbilityDetail();

            // A pre-equipped ability is locked into its slot: tapping its card changes nothing
            // (the detail strip above is read-only info).
            if (IsPreEquipped(ab)) return;

            int existing = SlotOf(ab);

            if (existing == _armedSlot)
            {
                // Case A — tap the ability sitting in the armed slot to clear it.
                SetEquipped(_armedSlot, null);
            }
            else if (existing >= 0)
            {
                // Case B — ab is equipped elsewhere; swap it into the armed slot.
                var displaced = GetEquipped(_armedSlot);
                SetEquipped(existing, displaced);
                SetEquipped(_armedSlot, ab);
            }
            else
            {
                // Case C — ab is unequipped; place it in the armed slot (whatever was
                // there returns to being pickable), then auto-advance for the next tap.
                SetEquipped(_armedSlot, ab);
                AutoArmFirstEmpty();
            }

            RebuildPickRows(Cls);
            RefreshSlotBoxes();
            RefreshEquippedState();
        }

        /// <summary>Redraws all four slot boxes: equipped ability (icon/name/numbered badge)
        /// or an empty state, plus the armed-slot highlight.</summary>
        private void RefreshSlotBoxes()
        {
            for (int i = 0; i < _slotBoxes.Length; i++)
            {
                var box = _slotBoxes[i];
                if (box == null) continue;

                bool armed = i == _armedSlot && !IsSlotLocked(i);
                box.EnableInClassList("cw-slot-box--armed", armed);
                box.EnableInClassList("cw-slot-box--locked", IsSlotLocked(i));

                var ab = GetEquipped(i);
                box.Clear();
                box.EnableInClassList("cw-slot-box--empty", ab == null);

                if (ab == null)
                {
                    // Armed = gold (selection) on top of the USS width/scale bump.
                    SetBorder(box, armed ? UiGfx.Gold : Fade(Ink, 0.45f));
                    box.style.backgroundColor = CreamInset;
                    var empty = new Label(UiText.Get(UiKeys.SlotEmpty));
                    empty.AddToClassList("cw-slot-box__empty-label");
                    box.Add(empty);
                    continue;
                }

                SetBorder(box, armed ? UiGfx.Gold : ab.AccentColor);
                box.style.backgroundColor = Color.Lerp(Cream, ab.AccentColor, 0.18f);

                box.Add(MakeAbilityIcon(ab, "cw-ability-card__sprite"));

                string label = !string.IsNullOrEmpty(ab.DisplayName) ? ab.DisplayName : ab.name;
                var name = new Label(label.ToUpperInvariant());
                name.AddToClassList("cw-ability-card__name");
                box.Add(name);

                // Numbered badge = the in-match button that fires it. Always accurate now
                // — nothing compacts, so a slot's index never silently changes under it.
                var badge = new Label((i + 1).ToString());
                badge.AddToClassList("cw-ability-badge");
                badge.style.backgroundColor = ab.AccentColor;
                badge.style.color = InkOn(ab.AccentColor);
                box.Add(badge);

                if (IsSlotLocked(i))
                {
                    // Padlock + STARTER on an ink pill (.cw-starter-tag, >= 26 px). The padlock
                    // is a Noto Emoji glyph (.cw-glyph), so no new art.
                    var lockTag = new VisualElement { pickingMode = PickingMode.Ignore };
                    lockTag.AddToClassList("cw-starter-tag");
                    var padlock = new Label(UiText.Get(UiKeys.GlyphLock)) { pickingMode = PickingMode.Ignore };
                    padlock.AddToClassList("cw-glyph");
                    padlock.AddToClassList("cw-starter-tag__lock");
                    var starter = new Label(UiText.Get(UiKeys.LabelStarter)) { pickingMode = PickingMode.Ignore };
                    starter.AddToClassList("cw-starter-tag__text");
                    lockTag.Add(padlock);
                    lockTag.Add(starter);
                    box.Add(lockTag);
                }
            }
        }

        // ======================================================================
        //  LOADOUT PICK ROWS
        // ----------------------------------------------------------------------
        //  Two flat rows, both fully visible, selection direct:
        //
        //      COMMON  — any chicken can take these (pool of 3)
        //      CLASS   — legal only for the selected class (pool of 3)
        //
        //  Design override of GDD §7.1-7.2 (explained once, reaffirmed by the
        //  user — not re-litigated here): Common is OPTIONAL, not a mandatory
        //  1st slot. Ability0/Ability1/Ability2/Ability3 are purely positional
        //  bookkeeping for the four active-ability slots. Any legal ability
        //  from EITHER row can land in ANY slot, freely mixed — 0 Common + 4
        //  Character, 3 Common + 1 Character (bounded by the pool sizes), or
        //  any mix in between are all equally valid. There is no per-category
        //  minimum; only the total count (== 4) gates READY.
        //
        //  The two rows stay as a discoverability grouping — legality/category
        //  is still visually separated — but which physical slot a tap lands in
        //  is governed entirely by the slot-arming state machine above, not by
        //  which row the card came from.
        // ======================================================================

        private void RebuildPickRows(ChickenClass cls)
        {
            var all = _abilityRegistry?.All;
            if (all == null)
            {
                FillMissingRegistryNote(_commonCards);
                FillMissingRegistryNote(_classCards);
                return;
            }

            var pool = all.Where(a => a != null && !(a is PassiveAbilitySO)).ToList();
            var flag = AbilityRegistrySO.FlagOf(cls);

            // AllowedClasses alone decides eligibility (the Common/Character slot kind is retired):
            // the SHARED row is everything legal for All, the CLASS row is every other ability this
            // class may pick. AllowedClasses == None (pre-equip-only, e.g. Mark/Kill) matches no
            // class flag, so it never appears here — it is only obtainable as a locked pre-equip.
            var common    = pool.Where(a => AbilityRegistrySO.IsShared(a)
                                            && (a.AllowedClasses & flag) != 0).ToList();
            var character = pool.Where(a => !AbilityRegistrySO.IsShared(a)
                                            && (a.AllowedClasses & flag) != 0).ToList();

            FillRow(_commonCards, common);
            FillRow(_classCards,  character);

            // Row titles carry the audience ("ANY BIRD" / "WARRIOR ONLY"); the hints just say
            // the rows are optional. The old "COMBO lets you take two" hint was dropped: COMBO was
            // retired in v0.7 and the hint was claiming a mechanic that no longer exists.
            if (_commonTitle != null) _commonTitle.text = UiText.Get(UiKeys.LoadoutRowShared);
            if (_classTitle != null)
                _classTitle.text = UiText.Format(UiKeys.LoadoutRowClass, ("cls", ClassShortName(cls)));
            if (_commonHint != null) _commonHint.text = UiText.Get(UiKeys.LoadoutHintShared);
            if (_classHint != null) _classHint.text = UiText.Get(UiKeys.LoadoutHintClass);
        }

        private void FillRow(VisualElement host, List<AbilityBaseSO> abilities)
        {
            if (host == null) return;
            host.Clear();
            if (abilities.Count == 0)
            {
                FillMissingRegistryNote(host);
                return;
            }
            foreach (var ab in abilities) host.Add(MakeAbilityCard(ab));
        }

        private static void FillMissingRegistryNote(VisualElement host)
        {
            if (host == null) return;
            host.Clear();
            var note = new Label(UiText.Get(UiKeys.LoadoutRegistryMissing));
            note.AddToClassList("cw-body");
            host.Add(note);
        }

        private VisualElement MakeAbilityCard(AbilityBaseSO ab)
        {
            var card = new VisualElement();
            card.AddToClassList("cw-ability-card");

            int slot = SlotOf(ab);
            if (slot >= 0)
            {
                // Gold CardGlowFrame = "selected"; first child so it draws under the content.
                var glow = new VisualElement { pickingMode = PickingMode.Ignore };
                glow.AddToClassList("cw-glow");
                card.Add(glow);
            }

            card.Add(MakeAbilityIcon(ab, "cw-ability-card__sprite"));

            string label = !string.IsNullOrEmpty(ab.DisplayName) ? ab.DisplayName : ab.name;
            var name = new Label(label.ToUpperInvariant());
            name.AddToClassList("cw-ability-card__name");
            card.Add(name);

            // The description does NOT live on the card — see #AbilityDetail below.
            // Six cards each carrying their own wrapped description needed ~137px of
            // a 263px card and left nothing for the icon, which flexbox then
            // collapsed to zero height. One shared strip shows the tapped ability's
            // text once, at the full 34px reading size, instead of six times at 24.

            // Category + cooldown share ONE footer row. They used to be two stacked
            // full-width rows; at ~43px each that cost 86px of a 263px card, which
            // is what squeezed the icon to zero once descriptions arrived. Side by
            // side they cost 43px and both stay as text, so the category is not
            // reduced to a colour the way a bare accent stripe would.
            var footer = new VisualElement();
            footer.AddToClassList("cw-card-footer");

            if (Cats.TryGetValue(ab.Category, out var cat))
            {
                var tag = new Label(UiText.Get(cat.LabelKey));
                tag.AddToClassList("cw-cat-tag");
                tag.style.backgroundColor = Fade(cat.Color, 0.85f);
                tag.style.color = InkOn(cat.Color);
                footer.Add(tag);
            }

            bool shortCd = ab.Cooldown <= 6f;
            var badge = new Label(UiText.Get(shortCd ? UiKeys.CooldownShort : UiKeys.CooldownMed));
            badge.AddToClassList("cw-cd-badge");
            badge.AddToClassList(shortCd ? "cw-cd-badge--short" : "cw-cd-badge--med");
            footer.Add(badge);
            card.Add(footer);

            if (slot >= 0)
            {
                SetBorder(card, ab.AccentColor);
                card.style.backgroundColor = Color.Lerp(Cream, ab.AccentColor, 0.18f);
                card.AddToClassList("cw-ability-card--picked");

                // Numbered badge = the in-match button that fires it, matching the
                // touch HUD hex badges. Also the non-color half of the selected
                // state, so the pick reads without relying on the accent border.
                var slotBadge = new Label((slot + 1).ToString());
                slotBadge.AddToClassList("cw-ability-badge");
                slotBadge.style.backgroundColor = ab.AccentColor;
                slotBadge.style.color = InkOn(ab.AccentColor);
                card.Add(slotBadge);
            }

            // One tap does both jobs: equips/unequips/swaps into the armed slot AND
            // reveals what the ability does in the detail strip. Deliberately NOT a
            // long-press — that has no affordance on a touch screen, and a player who
            // never discovers the gesture is back to "equip it and find out in a match".
            card.RegisterCallback<ClickEvent>(_ => OnAbilityCardTapped(ab));
            return card;
        }

        /// <summary>
        /// The icon for <paramref name="ab"/>: its exported sprite when one is authored
        /// (<see cref="AbilityIconStyle.SpriteClassFor"/>), otherwise its
        /// <see cref="AbilityIconStyle.Monogram"/> on an accent disc. Never blank.
        /// </summary>
        /// <remarks>
        /// This closed the blank-hex gap: the old path trusted <see cref="AbilityIconStyle.ClassFor"/>,
        /// which also returns reserved class names for abilities whose art does not exist yet
        /// (Peck, Mark/Kill, the 2026-08-23 roster) — an element with such a class paints
        /// nothing — and the emoji fallback behind it only ran when there was no class at all.
        /// The first time an ability is drawn without a sprite it is logged once.
        /// </remarks>
        private VisualElement MakeAbilityIcon(AbilityBaseSO ab, string spriteClass)
        {
            string iconCls = AbilityIconStyle.SpriteClassFor(ab);
            if (!string.IsNullOrEmpty(iconCls))
            {
                var sprite = new VisualElement { pickingMode = PickingMode.Ignore };
                sprite.AddToClassList(spriteClass);
                sprite.AddToClassList(iconCls);
                return sprite;
            }

            if (ab != null && _reportedMissingIcons.Add(ab))
                _log?.Warn(Source, $"Ability '{ab.name}' has no authored icon sprite; showing its monogram '{AbilityIconStyle.Monogram(ab)}'.");

            var mono = new Label(AbilityIconStyle.Monogram(ab)) { pickingMode = PickingMode.Ignore };
            mono.AddToClassList("cw-ability-mono");
            var accent = ab != null ? ab.AccentColor : HexEmptyTint;
            accent.a = 1f;
            mono.style.backgroundColor = accent;
            mono.style.color = InkOn(accent);
            return mono;
        }

        /// <summary>The Peck (foraging) ability asset, or null if the registry has none.</summary>
        private AbilityBaseSO PeckAbility =>
            _abilityRegistry?.ActiveAbilities.FirstOrDefault(a => a is PeckAbilitySO);

        /// <summary>True when ANY specialization of <paramref name="cls"/> forages (class-level UI,
        /// where no specialization is chosen yet). Derived from the Peck-slot data via
        /// <see cref="PreEquippedLoadout.ClassForages"/>, so the picker and MatchBootstrapper's
        /// spawner can never disagree about who gets a Peck. Peck's own AllowedClasses is None
        /// and is not consulted.</summary>
        private bool CanForage(ChickenClass cls) =>
            PreEquippedLoadout.ClassForages(cls, _abilityRegistry?.Passives, _abilityRegistry?.All, PeckAbility);

        /// <summary>The class's default (signature) specialization, or null if the registry has
        /// none for it. Used to preview a not-yet-selected class chip's PreFilledTag.</summary>
        private PassiveAbilitySO DefaultPassiveFor(ChickenClass cls) => _abilityRegistry?.GetDefaultPassiveForClass(cls);

        /// <summary>
        /// The display name(s) of the abilities <paramref name="passive"/> pre-equips for
        /// <paramref name="cls"/> — its Peck-slot ability then its Signature-slot ability, joined
        /// with " + " when both exist — or null when nothing is forced. Shared by the spec-pill
        /// "Starts with X equipped" line and PreFilledTag so the two can never disagree with
        /// each other or with <see cref="SeedForcedAbilities"/>; all three go through
        /// <see cref="PreEquippedLoadout.Resolve"/>.
        /// </summary>
        private string ForcedAbilityLabel(ChickenClass cls, PassiveAbilitySO passive)
        {
            if (passive == null) return null;

            PreEquippedLoadout.Resolve(cls, passive, _abilityRegistry?.All, PeckAbility, out var peckSlot, out var signature);
            if (peckSlot != null && signature != null) return $"{peckSlot.DisplayName} + {signature.DisplayName}";
            return (peckSlot ?? signature)?.DisplayName;
        }

        private AbilityBaseSO GetEquipped(int slot) => slot switch
        {
            0 => _selection?.Ability0,
            1 => _selection?.Ability1,
            2 => _selection?.Ability2,
            3 => _selection?.Ability3,
            _ => null,
        };

        private void SetEquipped(int slot, AbilityBaseSO ab)
        {
            if (_selection == null) return;
            if (slot == 0) _selection.Ability0 = ab;
            else if (slot == 1) _selection.Ability1 = ab;
            else if (slot == 2) _selection.Ability2 = ab;
            else if (slot == 3) _selection.Ability3 = ab;
        }

        private int SlotOf(AbilityBaseSO ab)
        {
            if (ab == null) return -1;
            for (int i = 0; i < ActiveSlotsForClass; i++) if (GetEquipped(i) == ab) return i;
            return -1;
        }

        /// <summary>The resolved Peck-slot and Signature-slot pre-equips for the current selection.</summary>
        private void ResolvePreEquips(out AbilityBaseSO peckSlot, out AbilityBaseSO signature) =>
            PreEquippedLoadout.Resolve(Cls, _selection?.Passive, _abilityRegistry?.All, PeckAbility, out peckSlot, out signature);

        /// <summary>True when <paramref name="slot"/> holds a locked pre-equip: Peck slot 0, signature
        /// 1 (or 0 when there is no Peck slot).</summary>
        private bool IsSlotLocked(int slot)
        {
            ResolvePreEquips(out var peckSlot, out var signature);
            if (peckSlot != null && slot == 0) return true;
            return signature != null && slot == (peckSlot != null ? 1 : 0);
        }

        /// <summary>True when <paramref name="ab"/> is the current selection's Peck-slot or Signature-slot pre-equip.</summary>
        private bool IsPreEquipped(AbilityBaseSO ab)
        {
            if (ab == null) return false;
            ResolvePreEquips(out var peckSlot, out var signature);
            return ab == peckSlot || ab == signature;
        }

        /// <summary>
        /// Ensures both of the specialization's pre-equipped abilities are equipped exactly where
        /// <c>MatchBootstrapper.ResolveLegalLoadout</c> would force them: the Peck-slot ability
        /// (an ability in the PeckSlotPreEquippedBy column for this subclass, falling back to the plain
        /// Peck for a forager) and the SignaturePreEquippedBy ability, both via
        /// <see cref="PreEquippedLoadout.Resolve"/>. So what the player composes is what actually
        /// spawns — without this the player could compose four abilities, hit READY, and have
        /// the spawner silently swap one out, a UI that lied.
        /// </summary>
        /// <remarks>
        /// <b>Deterministic slots, not "the next open slot".</b> The Peck-slot ability (when
        /// there is one) always lands in slot 0, and the signature (when both apply) in slot 1 —
        /// mirroring ResolveLegalLoadout's own forced-insert order, where the Peck-slot ability
        /// is inserted after the signature and pushes it along. Determinism is what lets the
        /// spec-pill's "Starts with X equipped" line and PreFilledTag name a specific slot truthfully.
        /// <para>
        /// A Peck-slot ability that is a Peck variant replaces the plain Peck rather than joining
        /// it, so a forager doesn't burn two of four slots on forced picks for one mechanic. Peck is
        /// None-class (never hand-picked), so a stray Peck in the old selection is released by the
        /// <c>IsAllowedFor</c> filter below, which also covers a class that cannot forage at all.
        /// </para>
        /// <para>
        /// <b>Pre-equipped slots are locked</b> (supersedes the old "Peck's button position is the
        /// player's to choose" rule): the player cannot clear, swap or move them — see
        /// <see cref="IsSlotLocked"/>. Seeding therefore REBUILDS the layout rather than only
        /// inserting what is missing: pre-equips first, then the player's other picks in their
        /// existing order, minus anything the class cannot pick.
        /// </para>
        /// </remarks>
        private void SeedForcedAbilities()
        {
            if (_selection == null) return;

            PreEquippedLoadout.Resolve(Cls, _selection.Passive, _abilityRegistry?.All, PeckAbility, out var peckSlot, out var signature);

            // Everything the player may keep: currently equipped, hand-pickable by this class, and
            // not a pre-equip. Anything unpickable (a stray Peck, the previous specialization's
            // pre-equip-only ability) is released — the spawner would strip it.
            var others = new List<AbilityBaseSO>();
            for (int i = 0; i < ActiveSlotsForClass; i++)
            {
                var eq = GetEquipped(i);
                if (eq == null || eq == peckSlot || eq == signature || others.Contains(eq)) continue;
                if (!AbilityRegistrySO.IsAllowedFor(eq, Cls)) continue;
                others.Add(eq);
            }

            // Locked layout: Peck slot 0, signature next. Deterministic, so the spec-pill and
            // PreFilledTag text can name real slots. The player's remaining picks keep their order.
            var layout = new List<AbilityBaseSO>(ActiveSlotsForClass);
            if (peckSlot != null) layout.Add(peckSlot);
            if (signature != null) layout.Add(signature);
            layout.AddRange(others);

            for (int i = 0; i < ActiveSlotsForClass; i++)
                SetEquipped(i, i < layout.Count ? layout[i] : null);
        }

        /// <summary>
        /// READY gates on total distinct equipped abilities == 4 + a Passive —
        /// there is no per-category (Common vs Class) minimum any more. The two
        /// row counters are read-outs of "how many of this row's pool are
        /// currently equipped", not a "/1" or "/N" requirement — Common is
        /// entirely optional, per the design override of GDD §7.1-7.2.
        /// </summary>
        private void RefreshEquippedState()
        {
            int n = ActiveSlotsForClass;
            int totalPicked = 0, commonPicked = 0, classPicked = 0;
            for (int i = 0; i < n; i++)
            {
                var eq = GetEquipped(i);
                if (eq == null) continue;
                totalPicked++;
                if (AbilityRegistrySO.IsShared(eq)) commonPicked++;
                else classPicked++;
            }

            // No implied floor: "0 equipped" reads as "optional, none taken yet",
            // not as a missing requirement.
            if (_commonCount != null) _commonCount.text = UiText.Format(UiKeys.LoadoutCount, ("n", commonPicked));
            if (_classCount  != null) _classCount.text  = UiText.Format(UiKeys.LoadoutCount, ("n", classPicked));

            int missing = Mathf.Max(0, n - totalPicked);
            bool ready  = missing == 0 && _selection?.Passive != null;

            if (_readyBtn != null)
            {
                _readyBtn.text = ready
                    ? UiText.Get(UiKeys.BtnReady)
                    : (missing == 1
                        ? UiText.Get(UiKeys.BtnPickMoreOne)
                        : UiText.Format(UiKeys.BtnPickMoreMany, ("n", missing)));
                _readyBtn.SetEnabled(ready);
                _readyBtn.EnableInClassList("cw-btn--green", ready);
                _readyBtn.EnableInClassList("cw-btn--neutral", !ready);
            }
        }

        private void OnReady()
        {
            if (_isBusy) return;
            SaveLastSetup();
            ShowLobby();
        }

        // ======================================================================
        //  LOBBY
        // ======================================================================
        // Okabe-Ito player colors (design cluckwars-tokens-v3 CW_PLAYERS_V3).
        private static readonly Color[] PlayerColors =
        {
            UiGfx.Hex32("E8751A"), UiGfx.Hex32("1A7FC4"),
            UiGfx.Hex32("C4286F"), UiGfx.Hex32("0D9E7A"),
        };
        // Sample CPU bots shown in the Solo lobby (you always spawn vs 3 bots).
        private static readonly ChickenClass[] BotClasses = { ChickenClass.Speedy, ChickenClass.Fatty, ChickenClass.Assassin };
        private static readonly string[]       BotNameKeys = { UiKeys.LobbyBot1, UiKeys.LobbyBot2, UiKeys.LobbyBot3 };

        private string _joinCode = string.Empty;

        private void BuildLobby()
        {
            Bind<Button>(_lobby, "BackBtn",  b => b.clicked += ShowLoadout);
            Bind<Button>(_lobby, "StartBtn", b => b.clicked += OnStartMatch);
            Bind<Button>(_lobby, "CopyBtn",  b => b.clicked += CopyJoinCode);
            Bind<Button>(_lobby, "ShareBtn", b => b.clicked += CopyJoinCode);
        }

        private async void RefreshLobby()
        {
            if (_selection == null) return;
            var mode = _selection.Mode;
            bool isHost = mode == SessionMode.Host;
            bool isJoin = mode == SessionMode.Join;
            bool isSolo = !isHost && !isJoin;

            var inviteCard = _lobby.Q<VisualElement>("InviteCard");
            var joinCard   = _lobby.Q<VisualElement>("JoinCard");
            var status     = _lobby.Q<Label>("LobbyStatus");
            if (inviteCard != null) inviteCard.style.display = isHost ? DisplayStyle.Flex : DisplayStyle.None;
            if (joinCard   != null) joinCard.style.display   = isJoin ? DisplayStyle.Flex : DisplayStyle.None;
            if (status != null) status.text = string.Empty;

            BuildPlayerGrid(isSolo, isHost);
            UpdateLobbyStatus(isSolo, isHost, isJoin);
            RefreshMatchSettings();

            // Host pre-creates the UGS lobby so its join code populates the tiles before START.
            if (isHost)
            {
                _joinCode = string.Empty;
                SetCodeTiles(UiText.Get(UiKeys.LobbyCodePlaceholder));
                try
                {
                    var info = await _ugs.CreateLobbyAsync("CluckWars Match", 4);
                    _joinCode = info.JoinCode;
                    _selection.SessionName = info.JoinCode;
                    SetCodeTiles(info.JoinCode);
                }
                catch (Exception e)
                {
                    _log?.Error(Source, $"CreateLobby failed: {e.Message}");
                    if (status != null) { status.style.color = UiGfx.Hex32("ff786e"); status.text = UiText.Get(UiKeys.LobbyErrCreate); }
                }
            }
        }

        /// <summary>
        /// Paints the TIME / GOAL rows of the MATCH SETTINGS card from the live
        /// <see cref="MatchConfigSO"/>, so the lobby advertises the rules the match will
        /// actually enforce. (ARENA and MODE stay authored: there is no map registry and
        /// FFA is the only mode, so there is nothing to bind them to.)
        /// </summary>
        private void RefreshMatchSettings()
        {
            if (_matchConfig == null)
            {
                // Only reachable when nothing installed the project container (EditMode /
                // headless). The TIME / GOAL values carry no authored text (an EditMode test pins
                // that), so they stay blank rather than advertising rules nobody configured.
                _log?.Warn(Source, "MatchConfig not injected; lobby TIME and GOAL are blank.");
                return;
            }

            var time = _lobby.Q<Label>("SetTime");
            var goal = _lobby.Q<Label>("SetGoal");
            if (time != null) time.text = MatchSettingsText.Time(_matchConfig.MatchDurationSeconds);
            if (goal != null) goal.text = MatchSettingsText.Goal(_matchConfig.FoodTargetToWin);
        }

        private void UpdateLobbyStatus(bool isSolo, bool isHost, bool isJoin)
        {
            var dot   = _lobby.Q<VisualElement>("StatusDot");
            var count = _lobby.Q<Label>("StatusCount");
            var text  = _lobby.Q<Label>("StatusText");
            var start = _lobby.Q<Button>("StartBtn");

            string c, t; Color dotColor; bool readyish;
            int seats = 4;
            if (isSolo)      { c = UiText.Format(UiKeys.LobbyCount, ("n", seats), ("max", seats)); t = UiText.Get(UiKeys.LobbyStatusSolo);      dotColor = UiGfx.Hex32("4ae66a"); readyish = true;  }
            else if (isHost) { c = UiText.Format(UiKeys.LobbyCount, ("n", 1),     ("max", seats)); t = UiText.Get(UiKeys.LobbyStatusWaiting);   dotColor = UiGfx.Gold;             readyish = false; }
            else             { c = UiText.Get(UiKeys.LobbyCountUnknown);                           t = UiText.Get(UiKeys.LobbyStatusEnterCode); dotColor = UiGfx.Gold;             readyish = false; }

            if (count != null) count.text = c;
            // The status line sits on a cream inset: dark green / dark amber clear 4.5:1 there
            // (the old light green / gold were tuned for a dark pill). The dot keeps the bright
            // hue - it is a lamp, not text, and carries an ink outline in USS.
            if (text  != null) { text.text = t; text.style.color = readyish ? UiGfx.Hex32("1e6a1e") : UiGfx.Hex32("6e4800"); }
            if (dot   != null) dot.style.backgroundColor = dotColor;
            if (start != null) start.text = UiText.Get(isJoin ? UiKeys.BtnJoinMatch : UiKeys.BtnStart);
        }

        private void SetCodeTiles(string code)
        {
            var tiles = _lobby.Q<VisualElement>("CodeTiles");
            if (tiles == null) return;
            tiles.Clear();
            if (string.IsNullOrEmpty(code)) return;
            foreach (var ch in code.ToUpperInvariant())
            {
                var t = new Label(ch.ToString());
                t.AddToClassList("cw-code-tile");
                tiles.Add(t);
            }
        }

        private void CopyJoinCode()
        {
            if (string.IsNullOrEmpty(_joinCode)) return;
            GUIUtility.systemCopyBuffer = _joinCode;
            var status = _lobby.Q<Label>("LobbyStatus");
            if (status != null) { status.style.color = UiGfx.Gold; status.text = UiText.Format(UiKeys.LobbyCopied, ("code", _joinCode)); }
        }

        // ---- Player grid (2×2) ------------------------------------------------
        private void BuildPlayerGrid(bool isSolo, bool isHost)
        {
            var grid = _lobby.Q<VisualElement>("PlayerGrid");
            if (grid == null) return;
            grid.Clear();

            // Slot 0 — you.
            var mine = new List<AbilityBaseSO>();
            for (int i = 0; i < ActiveSlotsForClass; i++) { var a = GetEquipped(i); if (a != null) mine.Add(a); }
            grid.Add(MakeLobbyCard(0, Cls, UiText.Get(UiKeys.LabelYou), isHost, false, ready: true, abilities: mine, empty: false, slots: ActiveSlotsForClass));

            // Slots 1-3 — solo fills CPU bots; host/join show open seats.
            for (int i = 1; i < 4; i++)
            {
                if (isSolo)
                {
                    var bc = BotClasses[i - 1];
                    grid.Add(MakeLobbyCard(i, bc, UiText.Get(BotNameKeys[i - 1]), false, true, ready: true, abilities: SampleBotAbilities(bc, i), empty: false, slots: ActiveSlotsForClass));
                }
                else
                {
                    grid.Add(MakeLobbyCard(i, ChickenClass.Warrior, null, false, false, ready: false, abilities: null, empty: true, slots: 2));
                }
            }
        }

        /// <summary>
        /// Cosmetic loadout preview for a lobby-only CPU card. NOT what the bot actually spawns
        /// with — that is a randomly-rolled preset resolved by
        /// <c>MatchBootstrapper.ResolveLegalLoadout</c> once <c>Game.unity</c> loads, and this
        /// screen (<c>Bootstrap.unity</c>) has no access to that component or its scene-baked
        /// presets to preview exactly. What it must still get right is the shape of a real
        /// loadout: <see cref="ActiveSlotsForClass"/> abilities, every one legal for
        /// <paramref name="cls"/>, Peck present iff the class can forage.
        /// </summary>
        /// <remarks>
        /// Previously indexed into <c>_abilityRegistry.All</c> unfiltered by class and capped
        /// at a hardcoded 2 — so a bot preview could show another class's ability, and every
        /// bot card read as carrying half the abilities the human player's card showed for the
        /// same four slots. Verified live 2026-08-23: a solo lobby with Warrior (4 icons) next
        /// to Speedy/Fatty/Assassin bots (2 icons each).
        /// </remarks>
        private List<AbilityBaseSO> SampleBotAbilities(ChickenClass cls, int seed)
        {
            var list = new List<AbilityBaseSO>(ActiveSlotsForClass);
            if (_abilityRegistry == null) return list;

            var peck = PeckAbility;
            if (peck != null && CanForage(cls))
                list.Add(peck);

            // Class abilities first, shared as backfill — mirrors ResolveLegalLoadout's own
            // priority order (a real loadout is never short on Character slots while Common
            // sits unused). Rotated by seed so DashFox/BrunoB/PeckNoir don't all show the
            // same three abilities from the front of each list.
            var pool = _abilityRegistry.GetClassAbilitiesForClass(cls)
                .Concat(_abilityRegistry.SharedAbilities.Where(a => a != peck))
                .ToList();

            for (int i = 0; i < pool.Count && list.Count < ActiveSlotsForClass; i++)
            {
                var a = pool[(seed + i) % pool.Count];
                if (!list.Contains(a)) list.Add(a);
            }
            return list;
        }

        private VisualElement MakeLobbyCard(int idx, ChickenClass cls, string name, bool isHost, bool cpu, bool ready, List<AbilityBaseSO> abilities, bool empty, int slots)
        {
            var color = PlayerColors[Mathf.Clamp(idx, 0, 3)];

            if (empty)
            {
                var ec = new VisualElement();
                ec.AddToClassList("cw-player-card");
                ec.AddToClassList("cw-player-card--empty");
                var lbl = new Label(UiText.Format(UiKeys.LobbyWaitingFor, ("n", idx + 1)));
                lbl.AddToClassList("cw-player-empty-label");
                ec.Add(lbl);
                return ec;
            }

            var card = new VisualElement();
            card.AddToClassList("cw-player-card");
            // Player colour on the border at full strength; a seat still picking is quieter.
            SetBorder(card, ready ? color : Color.Lerp(color, CreamInset, 0.45f));

            // "You" is the only gold-glowing card (gold = you / selection).
            if (idx == 0)
            {
                var glow = new VisualElement { pickingMode = PickingMode.Ignore };
                glow.AddToClassList("cw-glow");
                card.Add(glow);
            }

            var accent = new VisualElement();
            accent.AddToClassList("cw-player-accent");
            accent.style.backgroundColor = color;
            card.Add(accent);

            // Row 1: chicken + (name / tags, then the class line at full width).
            var top = new VisualElement();
            top.AddToClassList("cw-player-top");

            var art = new VisualElement();
            art.AddToClassList("cw-player-art");
            art.AddToClassList("cw-chicken--" + KeyOf(cls));
            top.Add(art);

            var mid = new VisualElement();
            mid.AddToClassList("cw-player-mid");

            // Name gives way (ellipsis) before the P-tag / CPU tag do — they are flex-shrink 0.
            var nameRow = new VisualElement();
            nameRow.AddToClassList("cw-player-namerow");
            var nameLbl = new Label(name);
            nameLbl.AddToClassList("cw-player-name");
            var pn = new Label(UiText.Format(UiKeys.LobbyPlayerTag, ("n", idx + 1)));
            pn.AddToClassList("cw-player-pn");
            // Player colour as the tag's left stripe: the text stays cream-on-ink (~15:1),
            // which no Okabe-Ito fill could give it.
            pn.style.borderLeftColor = color;
            nameRow.Add(nameLbl); nameRow.Add(pn);
            if (isHost || cpu)
            {
                var tag = new Label(UiText.Get(isHost ? UiKeys.TagHost : UiKeys.TagCpu));
                tag.AddToClassList("cw-player-host");
                if (isHost) tag.AddToClassList("cw-player-host--host");
                nameRow.Add(tag);
            }
            mid.Add(nameRow);

            var clsLine = new Label(UiText.Format(UiKeys.LobbyClassLine, ("cls", ClassShortName(cls)), ("role", RoleName(cls))));
            clsLine.AddToClassList("cw-player-class");
            mid.Add(clsLine);
            top.Add(mid);
            card.Add(top);

            // Row 2: ability hexes, then the READY chip in its own spot (wraps below the
            // hexes on a narrow 4:3 card instead of covering the class line).
            var bottom = new VisualElement();
            bottom.AddToClassList("cw-player-bottom");

            var chips = new VisualElement();
            chips.AddToClassList("cw-player-chips");
            for (int i = 0; i < slots; i++)
            {
                AbilityBaseSO ab = (abilities != null && i < abilities.Count) ? abilities[i] : null;
                chips.Add(MakeMiniHex(ab));
            }
            bottom.Add(chips);

            var state = new VisualElement();
            state.AddToClassList("cw-player-state");
            state.AddToClassList(ready ? "cw-player-state--ready" : "cw-player-state--picking");
            var sl = new Label(UiText.Get(ready ? UiKeys.StateReady : UiKeys.StatePicking));
            sl.AddToClassList("cw-player-state__label");
            state.Add(sl);
            bottom.Add(state);
            card.Add(bottom);

            return card;
        }

        private VisualElement MakeMiniHex(AbilityBaseSO ab)
        {
            var hex = new VisualElement();
            hex.AddToClassList("cw-mini-hex");
            if (ab == null) { hex.style.unityBackgroundImageTintColor = HexEmptyTint; return hex; }
            hex.style.unityBackgroundImageTintColor = ab.AccentColor;

            var icon = MakeAbilityIcon(ab, "cw-mini-hex__sprite");
            // A monogram's accent disc would double up with the hex, which already carries
            // the accent: keep only its (accent-contrasting) text.
            if (icon is Label) icon.style.backgroundColor = new Color(0f, 0f, 0f, 0f);
            hex.Add(icon);
            return hex;
        }

        private async void OnStartMatch()
        {
            if (_isBusy || _selection == null) return;
            var status = _lobby.Q<Label>("LobbyStatus");

            switch (_selection.Mode)
            {
                case SessionMode.Solo:
                case SessionMode.Host:
                    _sceneLoader?.LoadNext();
                    break;

                case SessionMode.Join:
                    var field = _lobby.Q<TextField>("LobbyJoinField");
                    var code = field?.value?.Trim();
                    if (string.IsNullOrEmpty(code)) { if (status != null) status.text = UiText.Get(UiKeys.LobbyErrNoCode); return; }
                    _isBusy = true;
                    if (status != null) status.text = UiText.Get(UiKeys.LobbyJoining);
                    try
                    {
                        var info = await _ugs.JoinLobbyByCodeAsync(code);
                        _selection.SessionName = info.JoinCode;
                        _sceneLoader?.LoadNext();
                    }
                    catch (Exception e)
                    {
                        _log?.Error(Source, $"JoinByCode failed: {e.Message}");
                        if (status != null) status.text = UiText.Get(UiKeys.LobbyErrJoin);
                    }
                    _isBusy = false;
                    break;
            }
        }

        // ======================================================================
        //  Helpers
        // ======================================================================
        private static string KeyOf(ChickenClass cls) => cls.ToString().ToLowerInvariant();

        private static Color Fade(Color c, float a) => new Color(c.r, c.g, c.b, a);

        /// <summary>Blend a colour <paramref name="t"/> of the way toward white, for use as text on the dark screen bg.</summary>
        private static Color Lighten(Color c, float t) =>
            new Color(Mathf.Lerp(c.r, 1f, t), Mathf.Lerp(c.g, 1f, t), Mathf.Lerp(c.b, 1f, t), 1f);

        /// <summary>WCAG 2.1 relative luminance of an sRGB colour (alpha ignored).</summary>
        private static float Luminance(Color c)
        {
            static float Lin(float v) => v <= 0.03928f ? v / 12.92f : Mathf.Pow((v + 0.055f) / 1.055f, 2.4f);
            return 0.2126f * Lin(c.r) + 0.7152f * Lin(c.g) + 0.0722f * Lin(c.b);
        }

        /// <summary>
        /// Ink colour to draw on top of <paramref name="bg"/> — whichever of the cream and
        /// dark-brown text colours actually contrasts with it.
        /// <para>
        /// Required because ability accent colours span the whole luminance range: Egg Shell
        /// is <c>(0.92, 0.94, 1.00)</c>, i.e. near-white, so the fixed cream label made its
        /// numbered slot badge unreadable, while Root Egg's dark brown needs the cream.
        /// Anything that fills an element with an authored accent and then writes text on it
        /// must go through this rather than assuming a fixed ink.
        /// </para>
        /// </summary>
        private static Color InkOn(Color bg)
        {
            float bgL = Luminance(bg);
            float Ratio(Color fg)
            {
                float a = Luminance(fg), b = bgL;
                if (a < b) (a, b) = (b, a);
                return (a + 0.05f) / (b + 0.05f);
            }
            return Ratio(UiGfx.TextPrimary) >= Ratio(UiGfx.TextDark) ? UiGfx.TextPrimary : UiGfx.TextDark;
        }

        private static void SetBorder(VisualElement ve, Color c)
        {
            ve.style.borderTopColor = c; ve.style.borderBottomColor = c;
            ve.style.borderLeftColor = c; ve.style.borderRightColor = c;
        }

        private static void Bind<T>(VisualElement root, string name, Action<T> act) where T : VisualElement
        {
            var e = root?.Q<T>(name);
            if (e != null) act(e);
        }
    }
}

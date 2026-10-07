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
    /// (<see cref="LoadoutSlotModel{T}"/>) that never compacts. Phase 1 (2026-10-06): every
    /// card, slot, tile, perk badge and lobby seat is built once and refreshed in place.
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
        private MenuAudio                _audio = MenuAudio.Silent();

        // ---- Runtime state ----------------------------------------------------
        private VisualElement _root;
        // Backdrop (image + tint) sits behind every page and bleeds to the screen edges;
        // the page host holds the pages and carries the safe-area padding.
        private VisualElement _backdrop, _backdropTint, _pageHost;
        private VisualElement _mainMenu, _classSelect, _loadout, _lobby;
        private VisualElement _settingsSheet;
        private bool _isBusy;

        // ---- Menu juice (Phase 3B): MenuJuice engine, MenuJuicePolicy timings ----
        // Every effect goes through MenuJuice, which is a no-op under Reduced Motion; the callers
        // below take the consequence (a sound, a slot appearing) from that no-op instead.
        private VisualElement _fxLayer;
        private MenuJuice _juice;
        private bool _stampPending;          // READY was pressed: stamp + Ready cue when THE COOP is on screen
        private bool _lobbyAllReady;
        private int _stampHideId;
        private readonly MenuJuice.Effect[] _flightFor = new MenuJuice.Effect[AbilityController.SlotCount];
        private readonly AbilityBaseSO[] _flightAbility = new AbilityBaseSO[AbilityController.SlotCount];
        private readonly List<VisualElement> _staggerScratch = new();
        private Rect _appliedSafeArea;
        private Vector2Int _appliedScreenSize;
        // Abilities already reported as having no sprite (logged once each, not per refresh).
        private readonly HashSet<AbilityBaseSO> _reportedMissingIcons = new();

        // ---- PICK YOUR BIRD (built once in BuildClassSelect) --------------------
        private readonly Dictionary<ChickenClass, VisualElement> _classTiles = new();
        private VisualElement _heroStage, _previewChicken, _previewGlow;
        private Label _previewName, _previewQuote, _perkDetail;
        private Label _roleCalloutStrong, _roleCalloutWeak;
        // .cw-chicken--<class> currently on the hero figure (for swap).
        private string _previewChickenClass;
        private readonly PerkBadgeView[] _perkBadges = new PerkBadgeView[2];
        private VisualElement _starterRow;
        private readonly StarterChipView[] _starterChips = new StarterChipView[2];

        // ---- GEAR UP (slots built once; cards once per class) -------------------
        private VisualElement _commonCards, _classCards;
        private Label _commonTitle, _classTitle;
        private VisualElement _abilityDetail;
        private Label _abilityDetailName, _abilityDetailText, _abilityDetailCat, _abilityDetailCd;
        private AbilityBaseSO _focusedAbility;
        private Button _readyBtn;
        private readonly SlotView[] _slots = new SlotView[AbilityController.SlotCount];
        private readonly List<DeckCardView> _deckCards = new();
        private ChickenClass? _deckBuiltFor;
        // The slot-arming state machine (pure, unit tested): which slot the next card tap fills.
        private readonly LoadoutSlotModel<AbilityBaseSO> _slotModel = new(AbilityController.SlotCount);

        // ---- THE COOP (seats built once in BuildLobby) ---------------------------
        private readonly SeatView[] _seats = new SeatView[4];
        private VisualElement _readyBanner;

        // ---- Live 3D chicken stage (Phase 3; only while Performance Mode is OFF) ----------
        // Slot 0 = the PICK YOUR BIRD hero, 1..4 = the lineup seats. Null = static renders.
        private const int HeroStageSlot = 0;
        private MenuChickenStage _stage;
        private bool _stageFailed;
        // What each lineup seat shows (null = open seat), kept so the stage can be (re)bound
        // when Performance Mode turns OFF without the lobby being refreshed.
        private readonly ChickenClass?[] _seatClasses = new ChickenClass?[4];

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
            public readonly string Short, Role, Strong, Weak;
            public ClassKeySet(string shortName, string role, string strong, string weak)
            { Short = shortName; Role = role; Strong = strong; Weak = weak; }
        }

        private static readonly Dictionary<ChickenClass, ClassKeySet> ClassKeys = new()
        {
            [ChickenClass.Warrior]  = new ClassKeySet(UiKeys.ClassWarriorShort,  UiKeys.RoleWarrior,  UiKeys.CalloutWarriorStrong,  UiKeys.CalloutWarriorWeak),
            [ChickenClass.Speedy]   = new ClassKeySet(UiKeys.ClassSpeedyShort,   UiKeys.RoleSpeedy,   UiKeys.CalloutSpeedyStrong,   UiKeys.CalloutSpeedyWeak),
            [ChickenClass.Fatty]    = new ClassKeySet(UiKeys.ClassFattyShort,    UiKeys.RoleFatty,    UiKeys.CalloutFattyStrong,    UiKeys.CalloutFattyWeak),
            [ChickenClass.Assassin] = new ClassKeySet(UiKeys.ClassAssassinShort, UiKeys.RoleAssassin, UiKeys.CalloutAssassinStrong, UiKeys.CalloutAssassinWeak),
        };

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
            [InjectOptional] MatchConfigSO matchConfig,
            [InjectOptional] MenuAudio audio)
        {
            _selection       = selection;
            _log             = log;
            _ugs             = ugs;
            _sceneLoader     = sceneLoader;
            _classRegistry   = classRegistry;
            _abilityRegistry = abilityRegistry;
            _matchConfig     = matchConfig;
            _audio           = audio ?? MenuAudio.Silent();
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
            DisposeJuice();
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

            // Above every page: fly-to-slot ghosts, particles and the 3-2-1 (never takes a tap).
            _fxLayer = new VisualElement { name = "FxLayer", pickingMode = PickingMode.Ignore };
            StretchToParent(_fxLayer);
            _root.Add(_fxLayer);
            _juice = new MenuJuice(_fxLayer);

            _mainMenu    = ClonePage(_mainMenuUxml);
            _classSelect = ClonePage(_classSelectUxml);
            _loadout     = ClonePage(_characterSelectUxml);
            _lobby       = ClonePage(_lobbyUxml);

            BuildMainMenu();
            BuildClassSelect();
            BuildLoadout();
            BuildLobby();
            ApplyReducedMotion();

            // One trickle-down hook plays the generic button sound; buttons with their own cue are
            // registered through BackCue / OwnCue while the pages are built above.
            _root.UnregisterCallback<ClickEvent>(OnRootClick, TrickleDown.TrickleDown);
            _root.RegisterCallback<ClickEvent>(OnRootClick, TrickleDown.TrickleDown);

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
            RefreshStage();
            _audio.StartMenuMusic();
        }

        // ---- Menu audio ---------------------------------------------------------
        // Buttons get the generic tap from OnRootClick, except those registered below: BACK-type
        // buttons (softer cue) and buttons whose handler plays a more specific cue itself (so the
        // tap would double up).
        private readonly HashSet<VisualElement> _backCueButtons = new();
        private readonly HashSet<VisualElement> _ownCueButtons = new();

        private void BackCue(Button b) => _backCueButtons.Add(b);
        private void OwnCue(Button b) => _ownCueButtons.Add(b);

        private void OnRootClick(ClickEvent evt)
        {
            for (var ve = evt.target as VisualElement; ve != null; ve = ve.parent)
            {
                if (ve is not Button) continue;
                if (!ve.enabledInHierarchy || _ownCueButtons.Contains(ve)) return;
                if (_backCueButtons.Contains(ve)) _audio.Back(); else _audio.Tap();
                return;
            }
        }

        private void LateUpdate() { if (_stage != null) RunOnStage(_stage.Tick); }

        private void OnDisable() { DisposeStage(); DisposeJuice(); }

        private void OnDestroy() { DisposeStage(); DisposeJuice(); }

        /// <summary>Ends every running effect (landing / stamp cues still fire) and the countdown; the next BuildAll makes new ones.</summary>
        private void DisposeJuice()
        {
            _juice?.Dispose();
            _juice = null;
            _stampPending = false;
        }

        private VisualElement ClonePage(VisualTreeAsset vta)
        {
            if (vta == null) return new VisualElement();
            var ve = vta.Instantiate();
            UiText.ResolveTree(ve);
            ve.style.flexGrow = 1;
            ve.style.display = DisplayStyle.None;
            ve.AddToClassList(PageWrapClass);
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

        // At or below this panel height the pages compress their headers/title (20:9 phones: the
        // Pixel 9 canvas is ~2157x961; 16:9 at 1080 too, where the main menu with PLAY AGAIN
        // otherwise touches both edges). Below this aspect the wide two-column pages stack
        // (4:3 tablets: 2048x1536 -> ~1663x1247).
        private const float ShortLayoutHeight = 1100f;
        private const float NarrowLayoutAspect = 1.55f;

        private void UpdateLayout(float w, float h)
        {
            if (float.IsNaN(w) || float.IsNaN(h) || w <= 0f || h <= 0f) return;
            bool landscape = w >= h;
            _root.EnableInClassList("layout--landscape", landscape);
            _root.EnableInClassList("layout--portrait", !landscape);
            _root.EnableInClassList("layout--short", h < ShortLayoutHeight);
            _root.EnableInClassList("layout--narrow", w / h < NarrowLayoutAspect);
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
        /// <summary>How a page wants the shared backdrop: which painted scene (a USS modifier class
        /// on #Backdrop) and the colour of the tint layer over it.</summary>
        private readonly struct BackdropSpec
        {
            public readonly string SceneClass;
            public readonly Color Tint;
            public BackdropSpec(string sceneClass, Color tint) { SceneClass = sceneClass; Tint = tint; }
        }

        // Phase 2: one painted golden-hour scene per screen (Art/UI/Backgrounds/Bg_*.png), chosen by
        // a USS modifier class rather than Resources.Load, so the textures stay in Art/UI (imported
        // by UiSpriteImportSettings) and the stylesheet is the one place that names them. The tint
        // layer also carries the vignette (.cw-backdrop-tint); its colour is a light ink scrim that
        // knocks the bright scenes back behind the cards, or the class wash on PICK YOUR BIRD.
        private const string SceneMain = "cw-backdrop--main", SceneClass = "cw-backdrop--class",
                             SceneLoadout = "cw-backdrop--loadout", SceneLobby = "cw-backdrop--lobby";
        private static readonly Color ScrimMenu = new Color(0.10f, 0.05f, 0.02f, 0.10f);
        private static readonly Color ScrimBusy = new Color(0.10f, 0.05f, 0.02f, 0.22f);
        private string _appliedScene;

        private BackdropSpec BackdropFor(VisualElement page)
        {
            if (page == _classSelect)
            {
                // PICK YOUR BIRD: washed with the selected class (lightened so it stays sunny).
                var t = Lighten(TintOf(Cls), 0.25f);
                return new BackdropSpec(SceneClass, new Color(t.r, t.g, t.b, 0.30f));
            }
            if (page == _loadout) return new BackdropSpec(SceneLoadout, ScrimBusy);
            if (page == _lobby)   return new BackdropSpec(SceneLobby, ScrimBusy);
            return new BackdropSpec(SceneMain, ScrimMenu);
        }

        private VisualElement _currentPage;

        /// <summary>Applies <see cref="BackdropFor"/> for the current page to the shared backdrop.</summary>
        private void ApplyBackdrop()
        {
            if (_backdrop == null || _backdropTint == null) return;
            var spec = BackdropFor(_currentPage);
            if (_appliedScene != spec.SceneClass)
            {
                if (_appliedScene != null) _backdrop.RemoveFromClassList(_appliedScene);
                _backdrop.AddToClassList(spec.SceneClass);
                _appliedScene = spec.SceneClass;
            }
            _backdropTint.style.backgroundColor = spec.Tint;
        }

        // ---- Navigation -------------------------------------------------------
        private void ShowMainMenu()    { SetPage(_mainMenu); RefreshDevRow(); RefreshPlayAgain(); }
        private void ShowClassSelect() { SetPage(_classSelect); RefreshClassSelect(); }
        private void ShowLoadout()     { SetPage(_loadout); RefreshLoadout(); }
        private void ShowLobby()       { SetPage(_lobby); RefreshLobby(); }

        // Page transitions (spec Phase 1): the outgoing page fades/slides out (120 ms ease-in),
        // then the incoming one slides in from the travel direction (200 ms ease-out). Forward
        // = deeper in the flow (Main -> Pick -> Gear Up -> Coop). Durations live in USS
        // (.cw-pagewrap*); these constants only time the class swaps around them.
        private const string PageWrapClass = "cw-pagewrap";
        private const long PageOutMs = 120;
        private int _pageTransitionId;

        private int PageOrder(VisualElement page) =>
            page == _mainMenu ? 0 : page == _classSelect ? 1 : page == _loadout ? 2 : page == _lobby ? 3 : -1;

        private IEnumerable<VisualElement> Pages() => new[] { _mainMenu, _classSelect, _loadout, _lobby }.Where(p => p != null);

        private void SetPage(VisualElement page)
        {
            if (page != _mainMenu) CloseSettings();
            var previous = _currentPage;
            _currentPage = page;
            ApplyBackdrop();
            LeavePageJuice(page);

            int id = ++_pageTransitionId;
            bool animate = previous != null && previous != page && !PlayerPreferences.ReducedMotionEnabled
                           && previous.style.display.value == DisplayStyle.Flex;
            // Anything mid-transition is settled first, so a fast double tap can never leave two
            // pages visible or one stuck half-faded.
            foreach (var p in Pages())
                if (p != previous || !animate) ClearPageAnim(p);

            if (!animate)
            {
                foreach (var p in Pages()) p.style.display = p == page ? DisplayStyle.Flex : DisplayStyle.None;
                // One frame later, so the Show* caller's Refresh has painted the page first.
                page.schedule.Execute(() => OnPageShown(page, id)).ExecuteLater(0);
                return;
            }

            bool forward = PageOrder(page) >= PageOrder(previous);
            foreach (var p in Pages()) if (p != previous) p.style.display = DisplayStyle.None;
            previous.AddToClassList("cw-pagewrap--out");
            previous.AddToClassList(forward ? "cw-pagewrap--out-fwd" : "cw-pagewrap--out-back");

            previous.schedule.Execute(() =>
            {
                if (id != _pageTransitionId) return; // superseded; the newer SetPage settled it
                ClearPageAnim(previous);
                previous.style.display = DisplayStyle.None;
                page.AddToClassList(forward ? "cw-pagewrap--in-fwd" : "cw-pagewrap--in-back");
                page.style.display = DisplayStyle.Flex;
                OnPageShown(page, id);
                // One frame at the offset with no transition, then release: the page animates to rest.
                page.schedule.Execute(() =>
                {
                    page.RemoveFromClassList("cw-pagewrap--in-fwd");
                    page.RemoveFromClassList("cw-pagewrap--in-back");
                }).ExecuteLater(16);
            }).ExecuteLater(PageOutMs);
        }

        /// <summary>
        /// Leaving whatever page was up: icons still in flight land now (their sound and slot update
        /// happen), the 3-2-1 is cancelled, a pending READY stamp that never got to play fires its cue.
        /// </summary>
        private void LeavePageJuice(VisualElement next)
        {
            _stampHideId++;
            _juice?.CancelAll();
            if (_stampPending && next != _lobby) { _stampPending = false; _audio.Ready(); }
        }

        /// <summary>The page is on screen: its main items enter staggered, and THE COOP stamps READY if the player just pressed it.</summary>
        private void OnPageShown(VisualElement page, int transitionId)
        {
            bool stamp = _stampPending && page == _lobby;
            _stampPending = false;
            if (transitionId != _pageTransitionId || _juice == null)
            {
                if (stamp) _audio.Ready();   // superseded before it could play: the cue still fires once
                return;
            }

            var items = _staggerScratch;
            items.Clear();
            if (page == _classSelect)
            {
                foreach (var cls in Order) if (_classTiles.TryGetValue(cls, out var tile)) items.Add(tile);
            }
            else if (page == _loadout)
            {
                foreach (var v in _slots) if (v != null) items.Add(v.Root);
                foreach (var c in _deckCards) items.Add(c.Root);
            }
            else if (page == _lobby)
            {
                foreach (var seat in _seats) if (seat != null) items.Add(seat.Root);
            }
            if (items.Count > 0) _juice.Stagger(items);

            if (stamp) PlayReadyStamp();
        }

        private static void ClearPageAnim(VisualElement p)
        {
            p.RemoveFromClassList("cw-pagewrap--out");
            p.RemoveFromClassList("cw-pagewrap--out-fwd");
            p.RemoveFromClassList("cw-pagewrap--out-back");
            p.RemoveFromClassList("cw-pagewrap--in-fwd");
            p.RemoveFromClassList("cw-pagewrap--in-back");
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
            Bind<Button>(_settingsSheet, "SettingsCloseBtn", b => { b.clicked += CloseSettings; BackCue(b); });
            // A tap on the dimmed scrim (outside the panel) closes too; taps inside the panel
            // bubble up here with a different target and are ignored.
            _settingsSheet.RegisterCallback<ClickEvent>(evt =>
            {
                if (evt.target != _settingsSheet) return;
                _audio.Back();
                CloseSettings();
            });

            BindSettingRow("RangeGuidesRow", "RangeGuidesToggle",
                () => PlayerPreferences.AbilityRangeGuidesEnabled,
                v => PlayerPreferences.AbilityRangeGuidesEnabled = v, "Ability range guides");
            BindSettingRow("ReducedMotionRow", "ReducedMotionToggle",
                () => PlayerPreferences.ReducedMotionEnabled,
                v => { PlayerPreferences.ReducedMotionEnabled = v; ApplyReducedMotion(); }, "Reduced motion");
            BindSettingRow("PerformanceModeRow", "PerformanceModeToggle",
                () => PlayerPreferences.PerformanceModeEnabled,
                v => { PlayerPreferences.PerformanceModeEnabled = v; RefreshStage(); }, "Performance mode");
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
                _audio.Tap();
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

            bool valid = TryGetLastSetup(out var setup);
            var intent = MatchFlowRules.DecidePostMatchIntent(true, valid, valid ? setup.Mode : SessionMode.Solo);
            if (intent.ClearFlag) _selection.OpenLobbyOnMenuLoad = false;
            if (intent.Landing == MenuLanding.Lobby) OpenLobbyWithLastSetup(intent.Mode);
        }

        private void ChooseMode(SessionMode mode)
        {
            if (_selection != null) _selection.Mode = mode;
            ShowClassSelect();
        }

        // ======================================================================
        //  ICON VIEW (shared by deck cards, slots, starter chips, lobby hexes)
        // ======================================================================
        /// <summary>
        /// One ability icon slot, built once: an accent hex (disc), the icon sprite on it, and a
        /// monogram label (safety net), which <see cref="PaintIcon"/> repaints in place.
        /// </summary>
        private sealed class AbilityIconView
        {
            public readonly VisualElement Disc;
            public readonly VisualElement Sprite;
            public readonly Label Mono;
            public string SpriteClass;

            public AbilityIconView(VisualElement host)
            {
                Disc = new VisualElement { pickingMode = PickingMode.Ignore };
                Disc.AddToClassList("cw-icon-disc");
                Sprite = new VisualElement { pickingMode = PickingMode.Ignore };
                Sprite.AddToClassList("cw-icon-sprite");
                Mono = new Label { pickingMode = PickingMode.Ignore };
                Mono.AddToClassList("cw-ability-mono");
                host.Add(Disc);
                host.Add(Sprite);
                host.Add(Mono);
            }
        }

        /// <summary>
        /// Paints <paramref name="ab"/> into <paramref name="view"/>: its icon sprite
        /// (<see cref="AbilityIconStyle.ClassFor"/>; every mapped type is authored). A type with no
        /// mapping falls back to its <see cref="AbilityIconStyle.Monogram"/> on an accent disc and is
        /// logged once — a safety net, since AbilityIconArtTests fails on any unmapped type.
        /// </summary>
        private void PaintIcon(AbilityIconView view, AbilityBaseSO ab, bool disc = true)
        {
            if (view == null) return;
            if (!string.IsNullOrEmpty(view.SpriteClass)) view.Sprite.RemoveFromClassList(view.SpriteClass);
            view.SpriteClass = null;

            if (ab == null)
            {
                view.Disc.style.display = DisplayStyle.None;
                view.Sprite.style.display = DisplayStyle.None;
                view.Mono.style.display = DisplayStyle.None;
                return;
            }

            var accent = ab.AccentColor;
            accent.a = 1f;
            string iconCls = AbilityIconStyle.ClassFor(ab);
            if (!string.IsNullOrEmpty(iconCls))
            {
                // Cream silhouette on the ability's accent hex (the lobby mini-hex is its own hex).
                view.Disc.style.display = disc ? DisplayStyle.Flex : DisplayStyle.None;
                view.Disc.style.unityBackgroundImageTintColor = accent;
                view.SpriteClass = iconCls;
                view.Sprite.AddToClassList(iconCls);
                view.Sprite.EnableInClassList("cw-icon-sprite--on-disc", disc);
                view.Sprite.style.display = DisplayStyle.Flex;
                view.Mono.style.display = DisplayStyle.None;
                return;
            }

            if (_reportedMissingIcons.Add(ab))
                _log?.Warn(Source, $"Ability '{ab.name}' ({ab.GetType().Name}) has no AbilityIconStyle entry; showing its monogram '{AbilityIconStyle.Monogram(ab)}'.");

            view.Disc.style.display = DisplayStyle.None;
            view.Sprite.style.display = DisplayStyle.None;
            view.Mono.style.display = DisplayStyle.Flex;
            view.Mono.text = AbilityIconStyle.Monogram(ab);
            // On a lobby hex the hex is already the accent disc: text only (contrast vs the accent).
            view.Mono.style.backgroundColor = disc ? accent : new Color(0f, 0f, 0f, 0f);
            view.Mono.style.color = InkOn(accent);
        }

        private static string AbilityLabel(AbilityBaseSO ab) =>
            (!string.IsNullOrEmpty(ab.DisplayName) ? ab.DisplayName : ab.name).ToUpperInvariant();

        // ======================================================================
        //  STEP 1 — PICK YOUR BIRD
        // ======================================================================
        private sealed class PerkBadgeView
        {
            public Button Root;
            public AbilityIconView Icon;
            public Label Name, Line;
            public PassiveAbilitySO Passive;
        }

        private sealed class StarterChipView
        {
            public VisualElement Root;
            public AbilityIconView Icon;
            public Label Name;
        }

        /// <summary>
        /// Wires the page once: four class tiles (tap = pick that class, keeping its current perk
        /// or its signature perk), the hero, two perk badges and two starter chips. Refreshes only
        /// toggle classes and rewrite labels (<see cref="RefreshClassSelect"/>).
        /// </summary>
        private void BuildClassSelect()
        {
            _classTiles.Clear();
            for (int i = 0; i < Order.Length; i++)
            {
                var cls = Order[i];
                var tile = _classSelect.Q<VisualElement>(ChipNames[i]);
                if (tile == null)
                {
                    _log?.Error(Source, $"CharacterSelectClass.uxml has no #{ChipNames[i]}; {cls} cannot be picked.");
                    continue;
                }
                _classTiles[cls] = tile;
                tile.Q<VisualElement>(ChipNames[i] + "Art")?.AddToClassList("cw-chicken--" + KeyOf(cls));
                // Class colour as the full tile fill, lightened so ink text keeps >= 7:1.
                tile.style.backgroundColor = Lighten(TintOf(cls), 0.5f);
                tile.RegisterCallback<ClickEvent>(_ => OnClassTileTapped(cls));
            }

            _heroStage         = _classSelect.Q<VisualElement>("HeroStage");
            _previewChicken    = _classSelect.Q<VisualElement>("PreviewChicken");
            _previewGlow       = _classSelect.Q<VisualElement>("PreviewGlow");
            _previewName       = _classSelect.Q<Label>("PreviewName");
            _previewQuote      = _classSelect.Q<Label>("PreviewQuote");
            _roleCalloutStrong = _classSelect.Q<Label>("RoleCalloutStrong");
            _roleCalloutWeak   = _classSelect.Q<Label>("RoleCalloutWeak");
            _perkDetail        = _classSelect.Q<Label>("PerkDetail");
            _starterRow        = _classSelect.Q<VisualElement>("StarterRow");

            string[] badgeNames = { "PerkBadgeA", "PerkBadgeB" };
            for (int i = 0; i < _perkBadges.Length; i++)
            {
                var root = _classSelect.Q<Button>(badgeNames[i]);
                if (root == null)
                {
                    _log?.Error(Source, $"CharacterSelectClass.uxml has no #{badgeNames[i]}; that perk cannot be picked.");
                    continue;
                }
                var perkIconHost = root.Q<VisualElement>(className: "cw-perk-badge__icon");
                var view = new PerkBadgeView
                {
                    Root = root,
                    Icon = perkIconHost != null ? new AbilityIconView(perkIconHost) : null,
                    Name = root.Q<Label>(className: "cw-perk-badge__name"),
                    Line = root.Q<Label>(className: "cw-perk-badge__line"),
                };
                _perkBadges[i] = view;
                OwnCue(root);
                root.clicked += () =>
                {
                    if (view.Passive == null) return;
                    bool changed = view.Passive != _selection?.Passive;
                    if (changed) _audio.SelectPerk(); else _audio.Tap();
                    SelectClassAndPassive(Cls, view.Passive);
                    if (changed) _juice?.Pop(view.Root, MenuJuicePolicy.PerkRestScale);
                };
            }

            for (int i = 0; i < _starterChips.Length; i++)
            {
                var root = _classSelect.Q<VisualElement>("StarterChip" + i);
                if (root == null) continue;
                var iconHost = root.Q<VisualElement>(className: "cw-starter-chip__icon");
                _starterChips[i] = new StarterChipView
                {
                    Root = root,
                    Icon = iconHost != null ? new AbilityIconView(iconHost) : null,
                    Name = root.Q<Label>(className: "cw-starter-chip__name"),
                };
            }

            Bind<Button>(_classSelect, "HomeBtn", b => { b.clicked += ShowMainMenu; BackCue(b); });
            // Always enabled — BuildAll seeds a default class + perk, so Step 1 always has a pick.
            Bind<Button>(_classSelect, "NextBtn", b => b.clicked += ShowLoadout);
        }

        /// <summary>A tile picks the class. Re-tapping the current class keeps its perk; a new
        /// class starts on its signature perk (the perk badges then switch it).</summary>
        private void OnClassTileTapped(ChickenClass cls)
        {
            if (_selection == null) return;
            if (cls == _selection.SelectedClass && _selection.Passive != null) { _audio.Tap(); RefreshClassSelect(); return; }
            var passive = DefaultPassiveFor(cls);
            if (passive == null)
            {
                _log?.Error(Source, $"No perk is registered for {cls}; it cannot be picked.");
                return;
            }
            SelectClassAndPassive(cls, passive);
            _audio.SelectClass(cls);
            if (_classTiles.TryGetValue(cls, out var tile))
            {
                _juice?.Pop(tile, MenuJuicePolicy.TileRestScale);
                _juice?.BurstAt(tile, ParticleKind.Sparkle, 4);
            }
        }

        /// <summary>The class's two perks, signature first (registry order would put the alternative
        /// first for some classes, reading as if it were the class's identity).</summary>
        private List<PassiveAbilitySO> PerksFor(ChickenClass cls) =>
            _abilityRegistry?.GetPassivesForClass(cls).OrderByDescending(p => p.IsSignature).ToList()
            ?? new List<PassiveAbilitySO>();

        /// <summary>
        /// Selects a class and its perk together. Old picks are only cleared when the CLASS
        /// changes; switching perk within a class keeps the in-progress loadout.
        /// </summary>
        private void SelectClassAndPassive(ChickenClass cls, PassiveAbilitySO passive)
        {
            if (_selection == null) return;

            bool classChanged = cls != _selection.SelectedClass;
            if (classChanged)
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

            // Seed the perk's starters so the picker shows the loadout the spawner will build.
            SeedForcedAbilities();
            SyncSlotModel();
            _slotModel.AutoArmFirstEmpty();

            RefreshClassSelect();
            if (classChanged) ShowHeroOnStage(hop: true);
        }

        private ChickenClass Cls => _selection?.SelectedClass ?? ChickenClass.Warrior;

        /// <summary>Active-ability slots to fill — four for every class.</summary>
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
            var tint = TintOf(cls);

            foreach (var kv in _classTiles)
                kv.Value.EnableInClassList("cw-class-tile--selected", kv.Key == cls);

            if (_previewChicken != null)
            {
                if (!string.IsNullOrEmpty(_previewChickenClass))
                    _previewChicken.RemoveFromClassList(_previewChickenClass);
                _previewChickenClass = "cw-chicken--" + KeyOf(cls);
                _previewChicken.AddToClassList(_previewChickenClass);
            }
            ShowHeroOnStage(hop: false);
            if (_heroStage != null) _heroStage.style.backgroundColor = Fade(Lighten(tint, 0.55f), 1f);
            if (_previewGlow != null) _previewGlow.style.unityBackgroundImageTintColor = Fade(Lighten(tint, 0.3f), 0.9f);
            // Class name without "CHICKEN", ink on cream.
            if (_previewName != null) _previewName.text = ClassShortName(cls);
            if (_previewQuote != null)
            {
                if (_classRegistry != null && _classRegistry.TryGet(cls, out var entry) && !string.IsNullOrEmpty(entry.LoreQuote))
                    _previewQuote.text = UiText.Format(UiKeys.ClassQuote, ("quote", entry.LoreQuote));
                else
                    _previewQuote.text = "";
                _previewQuote.style.display = string.IsNullOrEmpty(_previewQuote.text) ? DisplayStyle.None : DisplayStyle.Flex;
            }

            RefreshRoleCallouts(cls);
            RefreshPerkBadges(cls);
            RefreshStarterChips(cls, _selection?.Passive);
            // The backdrop on this page is tinted by the selected class.
            if (_currentPage == _classSelect) ApplyBackdrop();
        }

        /// <summary>The two perk badges show the selected class's perks; the selected one is gold,
        /// and #PerkDetail explains it (templated from the passive's fields).</summary>
        private void RefreshPerkBadges(ChickenClass cls)
        {
            var perks = PerksFor(cls);
            if (perks.Count < 2)
                _log?.Warn(Source, $"{cls} has {perks.Count} perk(s) registered; PICK YOUR BIRD expects two.");

            for (int i = 0; i < _perkBadges.Length; i++)
            {
                var b = _perkBadges[i];
                if (b == null) continue;
                var p = i < perks.Count ? perks[i] : null;
                b.Passive = p;
                b.Root.style.display = p != null ? DisplayStyle.Flex : DisplayStyle.None;
                if (p == null) continue;
                if (b.Name != null) b.Name.text = p.DisplayName.ToUpperInvariant();
                PaintIcon(b.Icon, p);
                // The line is a template filled from the passive's own fields: never a stale number.
                if (b.Line != null) b.Line.text = p.PerkLine(_matchConfig);
                b.Root.EnableInClassList("cw-perk-badge--selected", _selection?.Passive == p);
            }

            if (_perkDetail != null)
            {
                var sel = _selection?.Passive;
                // A perk whose detail template is absent falls back to its line, which the badge
                // already shows: hide the repeat rather than print it twice.
                string detail = sel != null ? sel.PerkDetail(_matchConfig) : string.Empty;
                _perkDetail.text = sel != null && detail == sel.PerkLine(_matchConfig) ? string.Empty : detail;
                _perkDetail.style.display = string.IsNullOrEmpty(_perkDetail.text) ? DisplayStyle.None : DisplayStyle.Flex;
            }
        }

        /// <summary>The selected perk's starters (locked pre-equips), shown once as icon chips.</summary>
        private void RefreshStarterChips(ChickenClass cls, PassiveAbilitySO passive)
        {
            var starters = StartersFor(cls, passive);
            for (int i = 0; i < _starterChips.Length; i++)
            {
                var chip = _starterChips[i];
                if (chip == null) continue;
                var ab = i < starters.Count ? starters[i] : null;
                chip.Root.style.display = ab != null ? DisplayStyle.Flex : DisplayStyle.None;
                if (ab == null) continue;
                PaintIcon(chip.Icon, ab);
                if (chip.Name != null) chip.Name.text = AbilityLabel(ab);
            }
            if (_starterRow != null)
                _starterRow.style.display = starters.Count > 0 ? DisplayStyle.Flex : DisplayStyle.None;
        }

        /// <summary>
        /// Paints the strong/weak callouts from the dictionary's <c>callout.&lt;class&gt;</c> copy,
        /// hiding a row (glyph included) whose copy is blank.
        /// </summary>
        private void RefreshRoleCallouts(ChickenClass cls)
        {
            var keys = ClassKeys[cls];
            SetCallout(_roleCalloutStrong, UiText.Get(keys.Strong));
            SetCallout(_roleCalloutWeak, UiText.Get(keys.Weak));
        }

        private static void SetCallout(Label callout, string text)
        {
            if (callout == null) return;
            bool show = !string.IsNullOrWhiteSpace(text);
            callout.text = show ? text : string.Empty;
            var row = callout.parent ?? callout;
            row.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
        }

        // ======================================================================
        //  STEP 2 — GEAR UP
        // ======================================================================
        private sealed class SlotView
        {
            public Button Root;
            public AbilityIconView Icon;
            public VisualElement IconHost;
            public Label Name, Empty;
            public VisualElement StarterTag;
        }

        private sealed class DeckCardView
        {
            public AbilityBaseSO Ability;
            public VisualElement Root, Glow, StarterTag, IconHost;
            public Label Badge;
        }

        private void BuildLoadout()
        {
            _commonCards       = _loadout.Q<VisualElement>("CommonCards");
            _classCards        = _loadout.Q<VisualElement>("ClassCards");
            _commonTitle       = _loadout.Q<Label>("CommonTitle");
            _classTitle        = _loadout.Q<Label>("ClassTitle");
            _abilityDetail     = _loadout.Q<VisualElement>("AbilityDetail");
            _abilityDetailName = _loadout.Q<Label>("AbilityDetailName");
            _abilityDetailText = _loadout.Q<Label>("AbilityDetailText");
            _abilityDetailCat  = _loadout.Q<Label>("AbilityDetailCat");
            _abilityDetailCd   = _loadout.Q<Label>("AbilityDetailCd");
            _readyBtn          = _loadout.Q<Button>("ReadyBtn");
            if (_readyBtn != null) { _readyBtn.clicked += OnReady; OwnCue(_readyBtn); }

            for (int i = 0; i < _slots.Length; i++)
            {
                var box = _loadout.Q<Button>("SlotBox" + i);
                if (box == null)
                {
                    _log?.Error(Source, $"CharacterSelect.uxml has no #SlotBox{i}; slot {i + 1} cannot be shown or armed.");
                    continue;
                }
                _slots[i] = BuildSlot(box, i);
                int captured = i;
                OwnCue(box);
                box.clicked += () => OnSlotBoxTapped(captured);
            }

            Bind<Button>(_loadout, "BackBtn", b => { b.clicked += ShowClassSelect; BackCue(b); });
        }

        /// <summary>Builds one slot's children once: number, icon, name / EMPTY, STARTER tag, NEXT caret.</summary>
        private SlotView BuildSlot(Button box, int index)
        {
            box.text = string.Empty;
            var v = new SlotView { Root = box };

            var num = new Label((index + 1).ToString()) { pickingMode = PickingMode.Ignore };
            num.AddToClassList("cw-slot-box__num");

            v.IconHost = new VisualElement { pickingMode = PickingMode.Ignore };
            v.IconHost.AddToClassList("cw-slot-box__icon");
            v.Icon = new AbilityIconView(v.IconHost);

            var texts = new VisualElement { pickingMode = PickingMode.Ignore };
            texts.AddToClassList("cw-slot-box__texts");
            v.Name = new Label { pickingMode = PickingMode.Ignore };
            v.Name.AddToClassList("cw-slot-box__name");
            v.Empty = new Label(UiText.Get(UiKeys.SlotEmpty)) { pickingMode = PickingMode.Ignore };
            v.Empty.AddToClassList("cw-slot-box__empty-label");
            v.StarterTag = MakeStarterTag();
            texts.Add(v.Name); texts.Add(v.Empty); texts.Add(v.StarterTag);

            // NEXT caret: word + pointer, shown above the armed slot (not colour-only).
            var caret = new VisualElement { pickingMode = PickingMode.Ignore };
            caret.AddToClassList("cw-next-caret");
            var caretLabel = new Label(UiText.Get(UiKeys.LoadoutNext)) { pickingMode = PickingMode.Ignore };
            caretLabel.AddToClassList("cw-next-caret__label");
            var tip = new VisualElement { pickingMode = PickingMode.Ignore };
            tip.AddToClassList("cw-next-caret__tip");
            caret.Add(caretLabel); caret.Add(tip);

            box.Add(v.IconHost); box.Add(texts); box.Add(num); box.Add(caret);
            return v;
        }

        /// <summary>Padlock + STARTER on an ink pill (built once per owner, toggled by display).</summary>
        private static VisualElement MakeStarterTag()
        {
            var tag = new VisualElement { pickingMode = PickingMode.Ignore };
            tag.AddToClassList("cw-starter-tag");
            var padlock = new VisualElement { pickingMode = PickingMode.Ignore };   // Glyph_Lock sprite (USS)
            padlock.AddToClassList("cw-starter-tag__lock");
            var starter = new Label(UiText.Get(UiKeys.LabelStarter)) { pickingMode = PickingMode.Ignore };
            starter.AddToClassList("cw-starter-tag__text");
            tag.Add(padlock); tag.Add(starter);
            return tag;
        }

        /// <summary>Entering Step 2: make sure the deck matches the class, re-arm, repaint.</summary>
        private void RefreshLoadout()
        {
            EnsureDeck(Cls);
            SyncSlotModel();
            _slotModel.AutoArmFirstEmpty();
            RefreshLoadoutState();
        }

        /// <summary>Repaints every GEAR UP surface from the model (no element is created here).</summary>
        private void RefreshLoadoutState()
        {
            RefreshSlotBoxes();
            RefreshDeck();
            RefreshAbilityDetail();
            RefreshEquippedState();
        }

        // ---- Slot-arming state machine (LoadoutSlotModel) ------------------------
        // The selection service owns the four slots; the model is loaded from it before each
        // action and written back after, so it never drifts from what the spawner will read.

        private void SyncSlotModel()
        {
            int n = ActiveSlotsForClass;
            var slots = new AbilityBaseSO[n];
            var locks = new bool[n];
            for (int i = 0; i < n; i++) { slots[i] = GetEquipped(i); locks[i] = IsSlotLocked(i); }
            _slotModel.Load(slots, locks);
        }

        private void WriteSlotModel()
        {
            for (int i = 0; i < ActiveSlotsForClass; i++) SetEquipped(i, _slotModel.Get(i));
        }

        /// <summary>Tapping a slot arms it (locked starters can't be armed) and shows what it holds.</summary>
        private void OnSlotBoxTapped(int index)
        {
            SyncSlotModel();
            var ab = _slotModel.Get(index);
            if (ab != null) _focusedAbility = ab;
            bool wasArmed = _slotModel.Armed == index;
            if (_slotModel.ArmSlot(index) && !wasArmed) _audio.ArmSlot();
            RefreshLoadoutState();
        }

        /// <summary>
        /// One tap on a deck card shows its details AND resolves it against the armed slot:
        /// clear (it was in the armed slot), swap (it was in another slot) or place (then arming
        /// advances). Locked starters only show their details.
        /// </summary>
        private void OnAbilityCardTapped(AbilityBaseSO ab)
        {
            if (_selection == null || ab == null) return;
            _focusedAbility = ab;
            SyncSlotModel();
            var result = _slotModel.TapCard(ab, IsPreEquipped(ab));
            if (result != CardTapResult.Ignored) WriteSlotModel();
            bool equipped = result == CardTapResult.Placed || result == CardTapResult.Swapped;
            switch (result)
            {
                case CardTapResult.Placed:
                case CardTapResult.Swapped: break;   // Equip plays when the icon lands (LandSlot), or at once without motion
                case CardTapResult.Cleared: _audio.Clear(); break;
                default: _audio.Tap(); break;        // locked starter: details only
            }

            // Model writes above are immediate; only the VISUAL of an equip is delayed. A flight whose
            // slot no longer holds its ability lands now, so nothing is left hovering or out of step.
            SettleFlights();
            int target = equipped ? _slotModel.SlotOf(ab) : -1;
            bool fly = target >= 0 && MenuJuice.Allowed && _juice != null;
            if (fly)
            {
                LandFlight(target);
                HoldSlotContent(target, true);
            }

            RefreshLoadoutState();

            var card = _deckCards.Find(c => c.Ability == ab);
            if (card != null && result != CardTapResult.Ignored)
                _juice?.Pop(card.Root, card.Root.ClassListContains("cw-ability-card--picked") ? MenuJuicePolicy.CardPickedRestScale : 1f);

            if (!equipped) return;
            if (!fly || !StartFlight(ab, target, card)) { HoldSlotContent(target, false); _audio.Equip(); }
        }

        // ---- Fly-to-slot ---------------------------------------------------------------
        /// <summary>Hides (or shows) the new content of a slot while its icon is still in the air.</summary>
        private void HoldSlotContent(int slot, bool hold)
        {
            if (slot < 0 || slot >= _slots.Length || _slots[slot] == null) return;
            _slots[slot].IconHost.EnableInClassList("cw-fx-hold", hold);
            _slots[slot].Name.EnableInClassList("cw-fx-hold", hold);
        }

        private bool StartFlight(AbilityBaseSO ab, int slot, DeckCardView card)
        {
            var view = _slots[slot];
            if (card?.IconHost == null || view == null) return false;
            var handle = _juice.Fly(card.IconHost, view.IconHost, ghost => PaintGhost(ghost, ab), () => LandSlot(slot));
            if (handle == null) return false;
            _flightFor[slot] = handle;
            _flightAbility[slot] = ab;
            return true;
        }

        private void PaintGhost(VisualElement ghost, AbilityBaseSO ab)
        {
            if (ghost.userData is not AbilityIconView view)
            {
                view = new AbilityIconView(ghost);
                ghost.userData = view;
            }
            PaintIcon(view, ab);
        }

        /// <summary>The icon arrived: slot content shows with a small pop and sparkles, and the equip thunk plays.</summary>
        private void LandSlot(int slot)
        {
            _flightFor[slot] = null;
            _flightAbility[slot] = null;
            HoldSlotContent(slot, false);
            var view = _slots[slot];
            if (view != null)
            {
                _juice?.Pop(view.IconHost);
                _juice?.BurstAt(view.IconHost, ParticleKind.Sparkle, 4, 60f);
            }
            _audio.Equip();
        }

        private void LandFlight(int slot) => _juice?.LandNow(_flightFor[slot]);

        private void SettleFlights()
        {
            for (int i = 0; i < _flightFor.Length; i++)
                if (_flightFor[i] != null && _slotModel.Get(i) != _flightAbility[i]) LandFlight(i);
        }

        private void RefreshSlotBoxes()
        {
            int caret = _slotModel.NextCaretSlot;
            for (int i = 0; i < _slots.Length; i++)
            {
                var v = _slots[i];
                if (v == null) continue;
                var ab = _slotModel.Get(i);
                bool locked = _slotModel.IsLocked(i);
                v.Root.EnableInClassList("cw-slot-box--armed", i == caret);
                v.Root.EnableInClassList("cw-slot-box--caret", i == caret);
                v.Root.EnableInClassList("cw-slot-box--locked", locked);
                v.Root.EnableInClassList("cw-slot-box--filled", ab != null);

                PaintIcon(v.Icon, ab);
                v.IconHost.style.display = ab != null ? DisplayStyle.Flex : DisplayStyle.None;
                v.Name.text = ab != null ? AbilityLabel(ab) : string.Empty;
                v.Name.style.display = ab != null ? DisplayStyle.Flex : DisplayStyle.None;
                v.Empty.style.display = ab == null ? DisplayStyle.Flex : DisplayStyle.None;
                v.StarterTag.style.display = locked && ab != null ? DisplayStyle.Flex : DisplayStyle.None;
            }
        }

        // ---- Deck ------------------------------------------------------------------
        //  Two groups, both always visible: ANY BIRD (AllowedClasses == All) and {CLS} ONLY
        //  (anything else this class may pick). The groups are a discoverability split only —
        //  which slot a tap lands in is decided by the arming state machine. AllowedClasses ==
        //  None (pre-equip-only, e.g. Mark/Kill) never appears; it is only ever a starter.

        /// <summary>Builds the deck cards for <paramref name="cls"/> — once per class change.</summary>
        private void EnsureDeck(ChickenClass cls)
        {
            if (_deckBuiltFor == cls && _deckCards.Count > 0) return;
            _deckBuiltFor = cls;
            _deckCards.Clear();
            _commonCards?.Clear();
            _classCards?.Clear();

            if (_commonTitle != null) _commonTitle.text = UiText.Get(UiKeys.LoadoutRowShared);
            if (_classTitle != null) _classTitle.text = UiText.Format(UiKeys.LoadoutRowClass, ("cls", ClassShortName(cls)));

            var all = _abilityRegistry?.All;
            if (all == null)
            {
                _log?.Error(Source, "AbilityRegistrySO not injected; GEAR UP has no moves to offer.");
                AddRegistryNote(_commonCards);
                return;
            }

            var flag = AbilityRegistrySO.FlagOf(cls);
            var pool = all.Where(a => a != null && !(a is PassiveAbilitySO) && (a.AllowedClasses & flag) != 0).ToList();
            foreach (var ab in pool.Where(AbilityRegistrySO.IsShared)) AddDeckCard(_commonCards, ab);
            foreach (var ab in pool.Where(a => !AbilityRegistrySO.IsShared(a))) AddDeckCard(_classCards, ab);
            if (pool.Count == 0)
            {
                _log?.Error(Source, $"No pickable moves for {cls} in the registry.");
                AddRegistryNote(_classCards);
            }
        }

        private static void AddRegistryNote(VisualElement host)
        {
            if (host == null) return;
            var note = new Label(UiText.Get(UiKeys.LoadoutRegistryMissing));
            note.AddToClassList("cw-body");
            host.Add(note);
        }

        private void AddDeckCard(VisualElement host, AbilityBaseSO ab)
        {
            if (host == null) return;
            var v = new DeckCardView { Ability = ab, Root = new VisualElement() };
            var card = v.Root;
            card.AddToClassList("cw-ability-card");

            // Gold CardGlowFrame = picked; first child so it draws under the content.
            v.Glow = new VisualElement { pickingMode = PickingMode.Ignore };
            v.Glow.AddToClassList("cw-glow");
            card.Add(v.Glow);

            // Category frame: the card border and its top band wear the category colour; the
            // band names it too, so the category never rests on colour alone.
            var catColor = Cats.TryGetValue(ab.Category, out var cat) ? cat.Color : Ink;
            SetBorder(card, catColor);
            var band = new Label(cat != null ? UiText.Get(cat.LabelKey) : string.Empty) { pickingMode = PickingMode.Ignore };
            band.AddToClassList("cw-ability-card__band");
            band.style.backgroundColor = catColor;
            band.style.color = InkOn(catColor);
            card.Add(band);

            var iconHost = new VisualElement { pickingMode = PickingMode.Ignore };
            iconHost.AddToClassList("cw-ability-card__icon");
            PaintIcon(new AbilityIconView(iconHost), ab);
            card.Add(iconHost);
            v.IconHost = iconHost;

            var name = new Label(AbilityLabel(ab)) { pickingMode = PickingMode.Ignore };
            name.AddToClassList("cw-ability-card__name");
            card.Add(name);

            v.StarterTag = MakeStarterTag();
            card.Add(v.StarterTag);

            // Slot number of a picked card = the in-match button that fires it.
            v.Badge = new Label { pickingMode = PickingMode.Ignore };
            v.Badge.AddToClassList("cw-ability-badge");
            var accent = ab.AccentColor; accent.a = 1f;
            v.Badge.style.backgroundColor = accent;
            v.Badge.style.color = InkOn(accent);
            card.Add(v.Badge);

            // One tap does both jobs: equip into the armed slot AND show the details.
            card.RegisterCallback<ClickEvent>(_ => OnAbilityCardTapped(ab));
            host.Add(card);
            _deckCards.Add(v);
        }

        private void RefreshDeck()
        {
            foreach (var v in _deckCards)
            {
                int slot = _slotModel.SlotOf(v.Ability);
                bool starter = IsPreEquipped(v.Ability);
                v.Root.EnableInClassList("cw-ability-card--picked", slot >= 0);
                v.Root.EnableInClassList("cw-ability-card--locked", starter);
                v.Root.EnableInClassList("cw-ability-card--focused", v.Ability == _focusedAbility);
                v.Badge.text = slot >= 0 ? (slot + 1).ToString() : string.Empty;
                v.Badge.style.display = slot >= 0 ? DisplayStyle.Flex : DisplayStyle.None;
                v.StarterTag.style.display = starter ? DisplayStyle.Flex : DisplayStyle.None;
            }
        }

        /// <summary>The bottom bar's details: the tapped move's name, category, cooldown and text,
        /// or a prompt until a move is tapped.</summary>
        private void RefreshAbilityDetail()
        {
            if (_abilityDetailText == null) return;
            var ab = _focusedAbility;
            bool has = ab != null;

            if (_abilityDetailName != null)
            {
                _abilityDetailName.text = has ? AbilityLabel(ab) : string.Empty;
                _abilityDetailName.style.display = has ? DisplayStyle.Flex : DisplayStyle.None;
            }
            if (_abilityDetailCat != null)
            {
                bool showCat = has && Cats.ContainsKey(ab.Category);
                _abilityDetailCat.style.display = showCat ? DisplayStyle.Flex : DisplayStyle.None;
                if (showCat)
                {
                    var cat = Cats[ab.Category];
                    _abilityDetailCat.text = UiText.Get(cat.LabelKey);
                    _abilityDetailCat.style.backgroundColor = cat.Color;
                    _abilityDetailCat.style.color = InkOn(cat.Color);
                }
            }
            if (_abilityDetailCd != null)
            {
                _abilityDetailCd.style.display = has ? DisplayStyle.Flex : DisplayStyle.None;
                if (has)
                {
                    bool shortCd = ab.Cooldown <= 6f;
                    _abilityDetailCd.text = UiText.Get(shortCd ? UiKeys.CooldownShort : UiKeys.CooldownMed);
                    _abilityDetailCd.EnableInClassList("cw-cd-badge--short", shortCd);
                    _abilityDetailCd.EnableInClassList("cw-cd-badge--med", !shortCd);
                }
            }
            _abilityDetailText.text = has ? ab.Description : UiText.Get(UiKeys.LoadoutDetailEmpty);
            _abilityDetailText.style.color = has ? Ink : InkSoft;
            if (_abilityDetail != null) SetBorder(_abilityDetail, has && Cats.TryGetValue(ab.Category, out var c) ? c.Color : Ink);
        }

        /// <summary>The Peck (foraging) ability asset, or null if the registry has none.</summary>
        private AbilityBaseSO PeckAbility =>
            _abilityRegistry?.ActiveAbilities.FirstOrDefault(a => a is PeckAbilitySO);

        /// <summary>True when ANY perk of <paramref name="cls"/> forages (derived from the Peck-slot data
        /// via <see cref="PreEquippedLoadout.ClassForages"/>, so picker and spawner agree).</summary>
        private bool CanForage(ChickenClass cls) =>
            PreEquippedLoadout.ClassForages(cls, _abilityRegistry?.Passives, _abilityRegistry?.All, PeckAbility);

        /// <summary>The class's default (signature) perk, or null if the registry has none.</summary>
        private PassiveAbilitySO DefaultPassiveFor(ChickenClass cls) => _abilityRegistry?.GetDefaultPassiveForClass(cls);

        /// <summary>
        /// The starters <paramref name="passive"/> locks in for <paramref name="cls"/> — its Peck-slot
        /// ability then its Signature-slot ability — via <see cref="PreEquippedLoadout.Resolve"/>, the
        /// same resolution <see cref="SeedForcedAbilities"/> and the spawner use.
        /// </summary>
        private List<AbilityBaseSO> StartersFor(ChickenClass cls, PassiveAbilitySO passive)
        {
            var list = new List<AbilityBaseSO>(2);
            if (passive == null) return list;
            PreEquippedLoadout.Resolve(cls, passive, _abilityRegistry?.All, PeckAbility, out var peckSlot, out var signature);
            if (peckSlot != null) list.Add(peckSlot);
            if (signature != null) list.Add(signature);
            return list;
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

        /// <summary>The resolved Peck-slot and Signature-slot starters for the current selection.</summary>
        private void ResolvePreEquips(out AbilityBaseSO peckSlot, out AbilityBaseSO signature) =>
            PreEquippedLoadout.Resolve(Cls, _selection?.Passive, _abilityRegistry?.All, PeckAbility, out peckSlot, out signature);

        /// <summary>True when <paramref name="slot"/> holds a locked starter: Peck slot 0, signature
        /// 1 (or 0 when there is no Peck slot).</summary>
        private bool IsSlotLocked(int slot)
        {
            ResolvePreEquips(out var peckSlot, out var signature);
            if (peckSlot != null && slot == 0) return true;
            return signature != null && slot == (peckSlot != null ? 1 : 0);
        }

        /// <summary>True when <paramref name="ab"/> is one of the current selection's starters.</summary>
        private bool IsPreEquipped(AbilityBaseSO ab)
        {
            if (ab == null) return false;
            ResolvePreEquips(out var peckSlot, out var signature);
            return ab == peckSlot || ab == signature;
        }

        /// <summary>
        /// Puts the perk's starters exactly where <c>MatchBootstrapper.ResolveLegalLoadout</c> forces
        /// them (Peck slot 0, signature next) and keeps the player's other legal picks in order after
        /// them, so what the player composes is what spawns. Starters are locked
        /// (<see cref="IsSlotLocked"/>); anything the class cannot pick is released.
        /// </summary>
        private void SeedForcedAbilities()
        {
            if (_selection == null) return;

            PreEquippedLoadout.Resolve(Cls, _selection.Passive, _abilityRegistry?.All, PeckAbility, out var peckSlot, out var signature);

            var others = new List<AbilityBaseSO>();
            for (int i = 0; i < ActiveSlotsForClass; i++)
            {
                var eq = GetEquipped(i);
                if (eq == null || eq == peckSlot || eq == signature || others.Contains(eq)) continue;
                if (!AbilityRegistrySO.IsAllowedFor(eq, Cls)) continue;
                others.Add(eq);
            }

            var layout = new List<AbilityBaseSO>(ActiveSlotsForClass);
            if (peckSlot != null) layout.Add(peckSlot);
            if (signature != null) layout.Add(signature);
            layout.AddRange(others);

            for (int i = 0; i < ActiveSlotsForClass; i++)
                SetEquipped(i, i < layout.Count ? layout[i] : null);
        }

        /// <summary>READY gates on all four slots filled + a perk; otherwise it counts down the picks.</summary>
        private void RefreshEquippedState()
        {
            int missing = _slotModel.Missing;
            bool ready = missing == 0 && _selection?.Passive != null;

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
            // The stamp slams in when THE COOP is on screen (OnPageShown) and the Ready cue lands on
            // its hit frame (or at once under Reduced Motion). RefreshLobby reads this flag first.
            _stampPending = true;
            ShowLobby();
        }

        // ======================================================================
        //  THE COOP
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

        private sealed class SeatView
        {
            public VisualElement Root, Pedestal, Chicken, Plate, NameRow, Hexes, State;
            public Label Name, Pn, Tag, ClassLine, Waiting, StateLabel;
            public readonly List<(VisualElement Hex, AbilityIconView Icon)> HexViews = new();
            public string ChickenCss;
        }

        private void BuildLobby()
        {
            Bind<Button>(_lobby, "BackBtn",  b => { b.clicked += ShowLoadout; BackCue(b); });
            Bind<Button>(_lobby, "StartBtn", b => { b.clicked += OnStartMatch; OwnCue(b); });
            Bind<Button>(_lobby, "CopyBtn",  b => b.clicked += CopyJoinCode);
            Bind<Button>(_lobby, "ShareBtn", b => b.clicked += CopyJoinCode);
            _readyBanner = _lobby.Q<VisualElement>("ReadyBanner");

            var grid = _lobby.Q<VisualElement>("PlayerGrid");
            if (grid == null)
            {
                _log?.Error(Source, "Lobby.uxml has no #PlayerGrid; THE COOP cannot show the lineup.");
                return;
            }
            grid.Clear();
            for (int i = 0; i < _seats.Length; i++)
            {
                _seats[i] = BuildSeat(i);
                grid.Add(_seats[i].Root);
            }
        }

        /// <summary>Builds one lineup seat once: pedestal + chicken, nameplate, four move hexes, ready badge.</summary>
        private SeatView BuildSeat(int idx)
        {
            var color = PlayerColors[idx];
            var v = new SeatView { Root = new VisualElement() };
            v.Root.AddToClassList("cw-seat");

            var stage = new VisualElement { pickingMode = PickingMode.Ignore };
            stage.AddToClassList("cw-seat__stage");
            v.Pedestal = new VisualElement { pickingMode = PickingMode.Ignore };
            v.Pedestal.AddToClassList("cw-seat__pedestal");
            v.Pedestal.style.unityBackgroundImageTintColor = color;   // Pedestal_Ring is white art
            v.Chicken = new VisualElement { pickingMode = PickingMode.Ignore };
            v.Chicken.AddToClassList("cw-chicken");
            v.Chicken.AddToClassList("cw-seat__chicken");
            stage.Add(v.Pedestal); stage.Add(v.Chicken);
            v.Root.Add(stage);

            v.Plate = new VisualElement();
            v.Plate.AddToClassList("cw-seat__plate");
            v.Plate.style.borderTopColor = color;
            var glow = new VisualElement { pickingMode = PickingMode.Ignore };
            glow.AddToClassList("cw-glow");
            v.Plate.Add(glow);

            v.NameRow = new VisualElement();
            v.NameRow.AddToClassList("cw-seat__namerow");
            v.Name = new Label();
            v.Name.AddToClassList("cw-seat__name");
            v.Pn = new Label(UiText.Format(UiKeys.LobbyPlayerTag, ("n", idx + 1)));
            v.Pn.AddToClassList("cw-seat__pn");
            // Player colour as the tag's left stripe: the text stays cream-on-ink (~15:1).
            v.Pn.style.borderLeftColor = color;
            v.Tag = new Label();
            v.Tag.AddToClassList("cw-seat__tag");
            v.NameRow.Add(v.Name); v.NameRow.Add(v.Pn); v.NameRow.Add(v.Tag);
            v.Plate.Add(v.NameRow);

            v.ClassLine = new Label();
            v.ClassLine.AddToClassList("cw-seat__class");
            v.Plate.Add(v.ClassLine);

            v.Waiting = new Label(UiText.Format(UiKeys.LobbyWaitingFor, ("n", idx + 1)));
            v.Waiting.AddToClassList("cw-seat__waiting");
            v.Plate.Add(v.Waiting);

            v.Hexes = new VisualElement();
            v.Hexes.AddToClassList("cw-seat__hexes");
            for (int i = 0; i < ActiveSlotsForClass; i++)
            {
                var hex = new VisualElement();
                hex.AddToClassList("cw-mini-hex");
                var iconHost = new VisualElement { pickingMode = PickingMode.Ignore };
                iconHost.AddToClassList("cw-mini-hex__icon");
                hex.Add(iconHost);
                v.Hexes.Add(hex);
                v.HexViews.Add((hex, new AbilityIconView(iconHost)));
            }
            v.Plate.Add(v.Hexes);

            v.State = new VisualElement();
            v.State.AddToClassList("cw-seat__state");
            var rosette = new VisualElement { pickingMode = PickingMode.Ignore };
            rosette.AddToClassList("cw-seat__rosette");
            v.State.Add(rosette);
            v.StateLabel = new Label();
            v.StateLabel.AddToClassList("cw-seat__state-label");
            v.State.Add(v.StateLabel);
            v.Plate.Add(v.State);

            v.Root.Add(v.Plate);
            return v;
        }

        /// <summary>Fills one seat. <paramref name="name"/> null = an open seat (waiting for a player).</summary>
        private void SetSeat(int idx, ChickenClass cls, string name, bool isHost, bool cpu, bool ready, IReadOnlyList<AbilityBaseSO> abilities)
        {
            var v = _seats[idx];
            if (v == null) return;
            bool empty = name == null;
            v.Root.EnableInClassList("cw-seat--empty", empty);
            v.Root.EnableInClassList("cw-seat--you", idx == 0 && !empty);

            if (!string.IsNullOrEmpty(v.ChickenCss)) v.Chicken.RemoveFromClassList(v.ChickenCss);
            v.ChickenCss = empty ? null : "cw-chicken--" + KeyOf(cls);
            if (v.ChickenCss != null) v.Chicken.AddToClassList(v.ChickenCss);
            _seatClasses[idx] = empty ? null : cls;
            ShowSeatOnStage(idx);

            v.NameRow.style.display = empty ? DisplayStyle.None : DisplayStyle.Flex;
            v.ClassLine.style.display = empty ? DisplayStyle.None : DisplayStyle.Flex;
            v.Hexes.style.display = empty ? DisplayStyle.None : DisplayStyle.Flex;
            v.Waiting.style.display = empty ? DisplayStyle.Flex : DisplayStyle.None;
            v.State.style.display = empty ? DisplayStyle.None : DisplayStyle.Flex;
            if (empty) return;

            v.Name.text = name;
            v.Tag.text = isHost ? UiText.Get(UiKeys.TagHost) : cpu ? UiText.Get(UiKeys.TagCpu) : string.Empty;
            v.Tag.style.display = isHost || cpu ? DisplayStyle.Flex : DisplayStyle.None;
            v.ClassLine.text = UiText.Format(UiKeys.LobbyClassLine, ("cls", ClassShortName(cls)), ("role", RoleName(cls)));

            for (int i = 0; i < v.HexViews.Count; i++)
            {
                var ab = abilities != null && i < abilities.Count ? abilities[i] : null;
                var (hex, icon) = v.HexViews[i];
                hex.style.unityBackgroundImageTintColor = ab != null ? ab.AccentColor : HexEmptyTint;
                PaintIcon(icon, ab, disc: false);
            }

            v.State.EnableInClassList("cw-seat__state--picking", !ready);
            v.StateLabel.text = UiText.Get(ready ? UiKeys.StateReady : UiKeys.StatePicking);
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
            if (joinCard   != null) { joinCard.RemoveFromClassList("cw-hidden"); joinCard.style.display = isJoin ? DisplayStyle.Flex : DisplayStyle.None; }
            if (status != null) status.text = string.Empty;

            bool allReady = RefreshLineup(isSolo, isHost);
            RevealReadyBanner(allReady);
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

        /// <summary>Fills the four seats; returns true when every seat is taken and ready.</summary>
        private bool RefreshLineup(bool isSolo, bool isHost)
        {
            var mine = new List<AbilityBaseSO>();
            for (int i = 0; i < ActiveSlotsForClass; i++) { var a = GetEquipped(i); if (a != null) mine.Add(a); }
            SetSeat(0, Cls, UiText.Get(UiKeys.LabelYou), isHost, false, ready: true, mine);

            bool allReady = true;
            for (int i = 1; i < _seats.Length; i++)
            {
                if (isSolo)
                {
                    var bc = BotClasses[i - 1];
                    SetSeat(i, bc, UiText.Get(BotNameKeys[i - 1]), false, true, ready: true, SampleBotAbilities(bc, i));
                }
                else
                {
                    SetSeat(i, ChickenClass.Warrior, null, false, false, ready: false, null);
                    allReady = false;
                }
            }
            return allReady;
        }

        /// <summary>
        /// Shows the READY! banner when every seat is ready: a one-shot pop-in (scale + fade, USS
        /// transition) each time it appears; instant under reduced motion. Hidden otherwise. When
        /// the player just pressed READY the stamp (<see cref="PlayReadyStamp"/>) reveals it instead.
        /// </summary>
        private void RevealReadyBanner(bool show)
        {
            _lobbyAllReady = show;
            if (_readyBanner == null) return;
            _readyBanner.style.opacity = StyleKeyword.Null;   // a hold-then-fade stamp may have left it at 0
            if (!show)
            {
                _readyBanner.style.display = DisplayStyle.None;
                return;
            }
            _readyBanner.style.display = DisplayStyle.Flex;
            if (PlayerPreferences.ReducedMotionEnabled)
            {
                _readyBanner.RemoveFromClassList("cw-ready-banner--hidden");
                return;
            }
            _readyBanner.AddToClassList("cw-ready-banner--hidden");
            if (_stampPending) return;   // the stamp reveals it when the page is on screen
            // After the page has slid in, release the hidden state so the transition plays.
            _readyBanner.schedule.Execute(() => _readyBanner.RemoveFromClassList("cw-ready-banner--hidden"))
                        .ExecuteLater(PageOutMs + 260);
        }

        /// <summary>
        /// READY pressed -> THE COOP: the READY! ribbon slams in (scale 1.6 -> 1, a slight rotation)
        /// and stamps. It is the existing banner, not a second element: the ribbon is the biggest
        /// "READY" on the page, already carries the copy, and a seal-shaped rosette would only
        /// duplicate the small one on the player's own seat. Solo (every seat ready) it stays, as
        /// before; with open seats it holds for a moment and fades. The Ready cue, dust and sparkles
        /// fire on the hit frame. Reduced Motion: no slam, the cue plays at once.
        /// </summary>
        private void PlayReadyStamp()
        {
            if (_readyBanner == null || _juice == null) { _audio.Ready(); return; }
            var previousDisplay = _readyBanner.style.display;
            _readyBanner.style.display = DisplayStyle.Flex;
            _readyBanner.RemoveFromClassList("cw-ready-banner--hidden");
            if (!_juice.Stamp(_readyBanner, MenuJuicePolicy.StampStartDelaySeconds, OnStampHit))
            {
                _readyBanner.style.display = previousDisplay;
                _audio.Ready();
                return;
            }
            if (_lobbyAllReady) return;

            int id = ++_stampHideId;
            long holdMs = (long)((MenuJuicePolicy.StampStartDelaySeconds + MenuJuicePolicy.StampSeconds + MenuJuicePolicy.StampHoldSeconds) * 1000f);
            _readyBanner.schedule.Execute(() =>
            {
                if (id != _stampHideId) return;
                _readyBanner.style.opacity = 0f;   // fades through the banner's own opacity transition
                _readyBanner.schedule.Execute(() =>
                {
                    if (id != _stampHideId) return;
                    _readyBanner.style.display = DisplayStyle.None;
                    _readyBanner.style.opacity = StyleKeyword.Null;
                }).ExecuteLater(400);
            }).ExecuteLater(holdMs);
        }

        private void OnStampHit()
        {
            _audio.Ready();
            if (_readyBanner == null) return;
            _juice?.BurstAt(_readyBanner, ParticleKind.Dust, 5, 110f);
            _juice?.BurstAt(_readyBanner, ParticleKind.Sparkle, 5, 150f);
        }

        /// <summary>
        /// Paints the TIME / GOAL chips from the live <see cref="MatchConfigSO"/>, so the lobby
        /// advertises the rules the match will actually enforce. (ARENA and MODE stay authored.)
        /// </summary>
        private void RefreshMatchSettings()
        {
            if (_matchConfig == null)
            {
                // Only reachable when nothing installed the project container (EditMode / headless).
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
            // Dark green / dark amber clear 4.5:1 on the cream inset; the dot is a lamp, not text.
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

        /// <summary>
        /// Cosmetic loadout preview for a lobby-only CPU seat. NOT what the bot actually spawns with
        /// (a randomly-rolled preset resolved by <c>MatchBootstrapper.ResolveLegalLoadout</c> in
        /// Game.unity); it only has to have a real loadout's shape: four abilities, all legal for
        /// <paramref name="cls"/>, Peck present iff the class can forage.
        /// </summary>
        private List<AbilityBaseSO> SampleBotAbilities(ChickenClass cls, int seed)
        {
            var list = new List<AbilityBaseSO>(ActiveSlotsForClass);
            if (_abilityRegistry == null) return list;

            var peck = PeckAbility;
            if (peck != null && CanForage(cls))
                list.Add(peck);

            // Class abilities first, shared as backfill, rotated by seed so the bots differ.
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

        /// <summary>The menu-to-match handoff: fade the menu loop, sting, load the Game scene.</summary>
        private void LoadMatchScene()
        {
            if (_sceneLoader == null)
            {
                _log?.Error(Source, "No SceneLoader in the menu scene; the match cannot start.");
                return;
            }
            _audio.StopMenuMusicForMatch();
            _audio.MatchSting();
            _sceneLoader.LoadNext();
        }

        private async void OnStartMatch()
        {
            if (_isBusy || _selection == null) return;
            var status = _lobby.Q<Label>("LobbyStatus");

            switch (_selection.Mode)
            {
                case SessionMode.Solo:
                case SessionMode.Host:
                    LoadMatchScene();
                    break;

                case SessionMode.Join:
                    var field = _lobby.Q<TextField>("LobbyJoinField");
                    var code = field?.value?.Trim();
                    if (string.IsNullOrEmpty(code)) { _audio.Tap(); if (status != null) status.text = UiText.Get(UiKeys.LobbyErrNoCode); return; }
                    _audio.Tap();
                    _isBusy = true;
                    if (status != null) status.text = UiText.Get(UiKeys.LobbyJoining);
                    try
                    {
                        var info = await _ugs.JoinLobbyByCodeAsync(code);
                        _selection.SessionName = info.JoinCode;
                        LoadMatchScene();
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
        //  LIVE 3D CHICKEN STAGE (Phase 3, Decision 7)
        // ======================================================================
        // Performance Mode OFF: the hero and the lineup seats show the real models, rendered by
        // MenuChickenStage into RenderTextures painted over the elements' USS background. ON (the
        // mobile default), or if the stage cannot come up: the static Phase 2 renders, untouched.
        // GEAR UP has no stage: its page is slots + deck + details with no hero spot.

        /// <summary>
        /// Brings the stage in line with <see cref="PlayerPreferences.PerformanceModeEnabled"/>:
        /// creates and binds it (hero + seats) when live, disposes it (static renders) when not.
        /// Called at build time and whenever the Performance Mode toggle changes.
        /// </summary>
        private void RefreshStage()
        {
            if (!MenuStagePolicy.WantsLive(PlayerPreferences.PerformanceModeEnabled, _stageFailed))
            {
                DisposeStage();
                return;
            }
            if (_stage == null && !TryCreateStage()) return;

            ShowHeroOnStage(hop: false);
            for (int i = 0; i < _seats.Length; i++) ShowSeatOnStage(i);
        }

        private bool TryCreateStage()
        {
            if (_classRegistry == null)
            {
                FailStage("ChickenClassRegistrySO not injected", null);
                return false;
            }
            try
            {
                _stage = new MenuChickenStage(_classRegistry, _log, 1 + _seats.Length, PanelPixelsPerPoint);
                _log?.Info(Source, "Live chicken stage on (Performance Mode off).");
                return true;
            }
            catch (Exception e)
            {
                FailStage("the stage could not be created", e);
                return false;
            }
        }

        /// <summary>Stage failure: log once, fall back to the static renders for this session.</summary>
        private void FailStage(string reason, Exception e)
        {
            _stageFailed = true;
            _log?.Warn(Source, $"Live chicken stage unavailable ({reason}{(e != null ? ": " + e.Message : "")}); showing the static renders.");
            DisposeStage();
        }

        private void DisposeStage()
        {
            if (_stage == null) return;
            _stage.Dispose();
            _stage = null;
            _log?.Info(Source, "Live chicken stage off.");
        }

        private void ShowHeroOnStage(bool hop)
        {
            if (_stage == null || _previewChicken == null) return;
            // Edges fade into the hero stage's class wash (same colour RefreshClassSelect paints).
            var clear = Lighten(TintOf(Cls), 0.55f);
            RunOnStage(() => _stage.Show(HeroStageSlot, _previewChicken, Cls, hop, sway: false, clear));
        }

        private void ShowSeatOnStage(int idx)
        {
            if (_stage == null || _seats[idx] == null) return;
            int slot = 1 + idx;
            var cls = _seatClasses[idx];
            if (cls == null) { _stage.Clear(slot); return; }
            // Edges fade into the warm barn tone behind the lineup.
            RunOnStage(() => _stage.Show(slot, _seats[idx].Chicken, cls.Value, false, sway: true, UiGfx.Hex32("8a5a2e")));
        }

        private void RunOnStage(Action act)
        {
            try { act(); }
            catch (Exception e) { FailStage("a chicken could not be staged", e); }
        }

        /// <summary>Screen pixels per panel point: the panel's target width (its RenderTexture when
        /// one is set, else the screen) over the root's laid-out width.</summary>
        private float PanelPixelsPerPoint()
        {
            float rootW = _root?.layout.width ?? 0f;
            if (float.IsNaN(rootW) || rootW <= 0f) return 1f;
            var ps = GetComponent<UIDocument>().panelSettings;
            float targetW = ps != null && ps.targetTexture != null ? ps.targetTexture.width : Screen.width;
            return targetW / rootW;
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

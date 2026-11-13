using CluckWars.Injection;
using System;
using System.Collections.Generic;
using System.Linq;
using CluckWars.Abilities;
using CluckWars.Audio;
using CluckWars.Bootstrap;
using CluckWars.Gameplay;
using CluckWars.Input;
using CluckWars.Localization;
using CluckWars.Logging;
using CluckWars.Services;
using CluckWars.Settings;
using UnityEngine;
using UnityEngine.UIElements;
using Zenject;

namespace CluckWars.UI
{
    /// <summary>Where Esc / Android back takes the menu (<see cref="MenuUiController.MenuBackTarget"/>).</summary>
    public enum MenuBackStep { None, MainMenu, ClassSelect, Loadout }

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
    public sealed partial class MenuUiController : MonoBehaviour
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
        private IInputProvider           _input;
        private IAudioService            _audioService;   // the volume sliders; null = the sliders only persist

        // ---- Runtime state ----------------------------------------------------
        private VisualElement _root;
        // Backdrop (image + tint) sits behind every page and bleeds to the screen edges;
        // the page host holds the pages and carries the safe-area padding.
        private VisualElement _backdrop, _backdropTint, _pageHost;
        private VisualElement _mainMenu, _classSelect, _loadout, _lobby;
        private VisualElement _settingsSheet;
        // CW-218 "You were disconnected" sheet (MainMenu.uxml #DisconnectSheet) and the code REJOIN uses.
        private VisualElement _disconnectSheet;
        private Label _disconnectBody, _disconnectStatus;
        private Button _disconnectRejoinBtn, _disconnectBackBtn;
        private string _rejoinCode;
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
        private SafeAreaPadding _safeArea;   // pads _pageHost (shared rule with the in-match overlays)
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
        private VisualElement _gearBody;
        private Label _kitTitle;

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
        // START MATCH hand-off card (Lobby.uxml #GetReadyCard) and how long its fade gets before the
        // background load starts competing for the main thread.
        private VisualElement _getReadyCard;
        private const float GetReadyFadeSeconds = 0.2f;
        private const string GetReadyHiddenClass = "cw-getready--hidden";

        // ---- Live 3D chicken stage (Phase 3; only while Performance Mode is OFF) ----------
        // Slot 0 = the PICK YOUR BIRD hero, 1..4 = the lineup seats. Null = static renders.
        private const int HeroStageSlot = 0;
        // After the four seats: the GEAR UP doorway bird (Phase 4).
        private const int GearStageSlot = 5;
        private VisualElement _gearChicken;
        private string _gearChickenClass;
        private MenuChickenStage _stage;
        private bool _stageFailed;
        // What each lineup seat shows (null = open seat), kept so the stage can be (re)bound
        // when Performance Mode turns OFF without the lobby being refreshed.
        private readonly ChickenClass?[] _seatClasses = new ChickenClass?[4];

        // Direction A surface colours (spec "Visual system"; tokens in CluckWarsTokens.uss).
        // Only the ones C# has to paint inline, because it also sets a state colour on the
        // same element — everything static lives in CluckWarsTheme.uss.
        private static readonly Color Ink        = UiGfx.Hex32("2a1a0c");
        private static readonly Color Cream      = UiGfx.Hex32("fff8e8");
        private static readonly Color CreamInset = UiGfx.Hex32("f3e6c8");
        private static readonly Color InkSoft    = UiGfx.Hex32("6b4a2a");

        private static readonly ChickenClass[] Order = ChickenClasses.All;

        /// <summary>
        /// UI class colours: tile fills, the hero wash and the PICK YOUR BIRD backdrop. NOT the match tints, on
        /// purpose (Phase 4 re-audit item 10; audit 2026-11-12 A05 closed as not a defect): with the registry's
        /// Warrior C04030 and Speedy E85A2A the two lightened tiles are near-identical (dE00 6.9). These give
        /// 23.9, every pair >= 14.6 and ink on every fill >= 6.9:1 (MenuClassTintTests). Fatty and Assassin
        /// equal the registry.
        /// </summary>
        private static readonly Dictionary<ChickenClass, Color> ClassTints = new()
        {
            [ChickenClass.Warrior]  = UiGfx.Hex32("B02A3A"),
            [ChickenClass.Speedy]   = UiGfx.Hex32("F08A2C"),
            [ChickenClass.Fatty]    = UiGfx.Hex32("F5D75A"),
            [ChickenClass.Assassin] = UiGfx.Hex32("7B68EE"),
        };

        // Class names, roles and callouts are player-facing copy and live in the wording
        // dictionary; the keys are the class table's (ChickenClasses, A17).
        private static string ClassShortName(ChickenClass cls) => UiText.Get(ChickenClasses.Of(cls).ShortNameKey);
        private static string RoleName(ChickenClass cls) => UiText.Get(ChickenClasses.Of(cls).RoleKey);

        private static readonly string[] ChipNames  = { "ClassWarrior", "ClassSpeedy", "ClassFatty", "ClassAssassin" };

        // Ability categories. These used to be section headers in a scrolling
        // grid; with 3 cards per row that produced headers holding one card each,
        // so the category now rides on the card itself as a colored tag. The colour of
        // every category (and so of every ability hex) is AbilityPalette's (Phase 4).
        private static readonly Dictionary<AbilityCategory, string> CategoryLabelKeys = new()
        {
            [AbilityCategory.Steal]   = UiKeys.CategorySteal,
            [AbilityCategory.Control] = UiKeys.CategoryControl,
            [AbilityCategory.Defense] = UiKeys.CategoryDefense,
            [AbilityCategory.Utility] = UiKeys.CategoryUtility,
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
            [InjectOptional] MenuAudio audio,
            [InjectOptional] IInputProvider input,
            [InjectOptional] IAudioService audioService)
        {
            _selection       = selection;
            _log             = log;
            _ugs             = ugs;
            _sceneLoader     = sceneLoader;
            _classRegistry   = classRegistry;
            _abilityRegistry = abilityRegistry;
            _matchConfig     = matchConfig;
            _audio           = audio ?? MenuAudio.Silent();
            _input           = input;
            _audioService    = audioService;
        }

        private void Awake()
        {
            SelfInject.Ensure(this, _log != null);

            UiText.SetLogger(_log);
            if (_input == null)
                _log?.Warn(Source, "IInputProvider not injected; Esc / Android back will not navigate the menu.");

            if (_sceneLoader == null) _sceneLoader = GetComponent<SceneLoader>();
            if (_sceneLoader == null) _sceneLoader = FindFirstObjectByType<SceneLoader>();
            _sceneLoader?.SetAutoLoad(false);

            UiElements.EnsureEventSystem();
        }

        private void OnEnable()
        {
            var doc = GetComponent<UIDocument>();
            _root = doc.rootVisualElement;
            if (_root == null) return;
            NavFocusRing.Attach(_root);
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
            _safeArea = new SafeAreaPadding(_pageHost);

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

            // Shape classes (layout--portrait/landscape/tall/short/narrow) come from the shared helper; this
            // controller's own geometry hook only re-applies the safe area.
            _root.RegisterCallback<GeometryChangedEvent>(OnRootGeometry);
            LayoutClasses.Bind(_root);
            SafeAreaSource.LogOnce(_log);

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
            ConsumeDisconnectNotice();
            RefreshStage();
            _audio.StartMenuMusic();
        }

        // ---- Layout -----------------------------------------------------------
        private void OnRootGeometry(GeometryChangedEvent evt) => _safeArea?.Apply(force: true);

        // A notch or gesture bar can change Screen.safeArea without the panel's geometry
        // changing (e.g. the system bars toggling), so it is also polled — two struct
        // compares a frame, no allocation.
        private void Update()
        {
            _safeArea?.Apply();
            // Menu-only poll: the match never runs this, so the in-match ability cancel (also Esc)
            // is untouched. Both read the key's wasPressedThisFrame, which nothing consumes.
            if (_input != null && _input.GetBackPressed()) OnBackPressed();
        }

        /// <summary>
        /// Esc / Android back (re-audit item 20): closes the settings sheet if it is open, else does
        /// what the page's BACK / HOME does, with the back sound. On the main menu it does nothing
        /// (no quit). Ignored while a join is in flight.
        /// </summary>
        private void OnBackPressed()
        {
            if (_isBusy || _currentPage == null) return;
            if (IsDisconnectSheetOpen)
            {
                _audio.Back();
                CloseDisconnectSheet();
                return;
            }
            if (_settingsSheet != null && _settingsSheet.style.display == DisplayStyle.Flex)
            {
                _audio.Back();
                CloseSettings();
                return;
            }
            var step = MenuBackTarget(PageOrder(_currentPage), _lobbyViaGearUp);
            if (step == MenuBackStep.None) return;
            _audio.Back();
            if (step == MenuBackStep.MainMenu) ShowMainMenu();
            else if (step == MenuBackStep.ClassSelect) ShowClassSelect();
            else ShowLoadout();
        }

        /// <summary>Where Esc / back goes from the page at <paramref name="pageOrder"/>
        /// (0 main, 1 pick, 2 gear up, 3 coop) - the same target as that page's BACK / HOME button.
        /// THE COOP goes back through GEAR UP only when it was reached through it; opened directly (PLAY AGAIN,
        /// BACK TO LOBBY) it goes home (round-3 finding 18).</summary>
        public static MenuBackStep MenuBackTarget(int pageOrder, bool lobbyViaGearUp = true) => pageOrder switch
        {
            1 => MenuBackStep.MainMenu,
            2 => MenuBackStep.ClassSelect,
            3 => lobbyViaGearUp ? MenuBackStep.Loadout : MenuBackStep.MainMenu,
            _ => MenuBackStep.None,
        };

        // ---- Navigation -------------------------------------------------------
        private void ShowMainMenu()    { SetPage(_mainMenu); RefreshDevRow(); RefreshPlayAgain(); }
        private void ShowClassSelect() { SetPage(_classSelect); RefreshClassSelect(); }
        private void ShowLoadout()     { SetPage(_loadout); RefreshLoadout(); }
        private void ShowLobby()       { SetPage(_lobby); RefreshLobby(); RefreshLobbyBackButton(); }

        // True when THE COOP was reached through PICK YOUR BIRD and GEAR UP (READY): its BACK returns to GEAR UP.
        // False when it was opened directly (main-menu PLAY AGAIN, post-match BACK TO LOBBY): its BACK is HOME.
        private bool _lobbyViaGearUp = true;
        private Button _lobbyBackBtn;

        private void OnLobbyBack()
        {
            if (_lobbyViaGearUp) ShowLoadout(); else ShowMainMenu();
        }

        private void RefreshLobbyBackButton()
        {
            if (_lobbyBackBtn != null) _lobbyBackBtn.text = UiText.Get(_lobbyViaGearUp ? UiKeys.NavBack : UiKeys.NavHome);
        }

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
            if (page != _mainMenu) { CloseSettings(); CloseDisconnectSheet(); }
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
                foreach (var p in Pages()) UiElements.SetShown(p, p == page);
                // One frame later, so the Show* caller's Refresh has painted the page first.
                page.schedule.Execute(() => OnPageShown(page, id)).ExecuteLater(0);
                return;
            }

            bool forward = PageOrder(page) >= PageOrder(previous);
            foreach (var p in Pages()) if (p != previous) UiElements.SetShown(p, false);
            previous.AddToClassList("cw-pagewrap--out");
            previous.AddToClassList(forward ? "cw-pagewrap--out-fwd" : "cw-pagewrap--out-back");

            previous.schedule.Execute(() =>
            {
                if (id != _pageTransitionId) return; // superseded; the newer SetPage settled it
                ClearPageAnim(previous);
                UiElements.SetShown(previous, false);
                page.AddToClassList(forward ? "cw-pagewrap--in-fwd" : "cw-pagewrap--in-back");
                UiElements.SetShown(page, true);
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
            BuildDisconnectSheet();
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
            if (LastSetupResolver.TryResolve(record, _abilityRegistry?.All, LoadoutModeNow, out setup, out var problem))
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
            Bind<VisualElement>(_mainMenu, "PlayAgainRow", r => UiElements.SetShown(r, valid));
            // One green CTA per screen: PLAY AGAIN while it is offered, else PLAY SOLO.
            Bind<Button>(_mainMenu, "PlayAgainBtn", b =>
            {
                b.EnableInClassList("cw-btn--green", valid);
                b.EnableInClassList("cw-btn--wood", !valid);
            });
            Bind<Button>(_mainMenu, "SoloBtn", b =>
            {
                b.EnableInClassList("cw-btn--green", !valid);
                b.EnableInClassList("cw-btn--wood", valid);
                b.EnableInClassList("cw-menu-primary--demoted", valid);
            });
            if (!valid) return;
            Bind<Label>(_mainMenu, "PlayAgainSub", l => l.text = UiText.Format(UiKeys.BtnPlayAgainSub,
                ("cls", ClassShortName(setup.Class)), ("role", RoleName(setup.Class))));
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
            _lobbyViaGearUp = false;
            ShowLobby();
            return true;
        }

        /// <summary>
        /// Post-match BACK TO LOBBY lands here: <see cref="ISessionSelectionService.OpenLobbyOnMenuLoad"/>
        /// is set by the match overlay before it loads Bootstrap. Read once, cleared at once.
        /// Solo and Host reopen THE COOP; a joiner (or an invalid setup) stays on the main menu. The waiting
        /// room's CHANGE BIRD sets <see cref="ISessionSelectionService.OpenClassSelectOnMenuLoad"/> instead,
        /// which opens PICK YOUR BIRD.
        /// </summary>
        private void ConsumePostMatchIntent()
        {
            if (_selection == null || !(_selection.OpenLobbyOnMenuLoad || _selection.OpenClassSelectOnMenuLoad)) return;

            bool valid = TryGetLastSetup(out var setup);
            var intent = MatchFlowRules.DecidePostMatchIntent(_selection.OpenLobbyOnMenuLoad, _selection.OpenClassSelectOnMenuLoad,
                valid, valid ? setup.Mode : SessionMode.Solo);
            if (intent.ClearFlag) { _selection.OpenLobbyOnMenuLoad = false; _selection.OpenClassSelectOnMenuLoad = false; }
            // CHANGE BIRD (in-match waiting room): PICK YOUR BIRD with the selection the match ran on, which
            // this ProjectContext service still holds (mode, class, perk, loadout).
            if (intent.Landing == MenuLanding.ClassSelect) { _log?.Info(Source, "Opening PICK YOUR BIRD (CHANGE BIRD from the match)."); ShowClassSelect(); }
            else if (intent.Landing == MenuLanding.Lobby) OpenLobbyWithLastSetup(intent.Mode);
        }

        private void ChooseMode(SessionMode mode)
        {
            if (_selection != null) _selection.Mode = mode;
            ShowClassSelect();
        }

        // ======================================================================
        //  ICON PAINT (the view + painter are shared: AbilityIconView.cs)
        // ======================================================================
        /// <summary>Paints <paramref name="ab"/> into <paramref name="view"/>; an unmapped type is logged once.</summary>
        private void PaintIcon(AbilityIconView view, AbilityBaseSO ab, bool disc = true) =>
            AbilityIconPainter.Paint(view, ab, disc, a =>
            {
                if (_reportedMissingIcons.Add(a))
                    _log?.Warn(Source, $"Ability '{a.name}' ({a.IconId}) has no AbilityIconStyle entry; showing its monogram '{AbilityIconStyle.Monogram(a)}'.");
            });

        private static string CategoryLabel(AbilityCategory cat) =>
            CategoryLabelKeys.TryGetValue(cat, out var key) ? UiText.Get(key) : string.Empty;

        /// <summary>Deck-card name size (USS .cw-ability-card__name); a name that cannot fit at it
        /// takes .cw-ability-card__name--long (26 px).</summary>
        private const float CardNameFontPx = 30f;

        /// <summary>
        /// Steps a deck-card name down to 26 px only when it does not fit the card's content width at
        /// 30 px (the sizing contract's caption floor). Single words cannot wrap: at 1920x1080 a card
        /// is ~182 pt inside, and DOPPELGANGER / INVISIBILITY measure ~208 pt at 30 px. Two-line wrapping
        /// would overflow the fixed card height on cards carrying a STARTER tag. On wider cards (4:3,
        /// phone) names that fit stay at 30 px. Re-evaluated whenever the card is laid out.
        /// </summary>
        private static void FitCardName(Label name, VisualElement card)
        {
            float content = card.contentRect.width;
            float size = name.resolvedStyle.fontSize;
            if (float.IsNaN(content) || content <= 0f || size <= 0f) return;
            float atFull = name.MeasureTextSize(name.text, 0, VisualElement.MeasureMode.Undefined, 0, VisualElement.MeasureMode.Undefined).x
                           * CardNameFontPx / size;
            name.EnableInClassList("cw-ability-card__name--long", atFull > content);
        }

        private static string AbilityLabel(AbilityBaseSO ab) =>
            (!string.IsNullOrEmpty(ab.DisplayName) ? ab.DisplayName : ab.name).ToUpperInvariant();

        // ======================================================================
        //  DEV AUTO-TEST HOOKS (CluckWars.Diagnostics.AutoTestDriver; dev-tools builds only)
        // ======================================================================
        // Each hook calls the handler the matching button calls, so an auto-test walks exactly the path a player's taps
        // walk (selection service, slot model, LastSetup, UGS lobby, scene load). Refused outside a dev-tools build.

        private int _autoTestJoinFailures;

        /// <summary>Pages built and nothing loading: an auto-test may start pressing. The menu does not know about the
        /// <c>-cw_menushots</c> walk: the auto-test driver adds that hold itself (<c>AutoTestDriver.MenuIsIdle</c>).</summary>
        public bool AutoTestPagesReady => DevToolsAvailable && _lobby != null && _selection != null && !_isBusy;

        /// <summary>
        /// WP8 <c>-cw_menushots</c>: shows one page for a layout probe + screenshot, without pressing anything. Ids:
        /// <c>main</c>, <c>settings</c> (the sheet over the main menu), <c>disconnect</c> (the CW-218 sheet, REJOIN shown but
        /// disabled: there is nothing to rejoin), <c>class</c>, <c>gear</c>, <c>coop</c> (THE COOP in Solo mode) and
        /// <c>coopjoin</c> (Join mode, the code field). THE COOP is never opened in Host mode here: that creates a UGS lobby.
        /// </summary>
        public bool AutoTestShowPage(string id)
        {
            if (!AutoTestAllowed("page")) return false;
            switch (id)
            {
                case "main":
                    CloseSettings();
                    CloseDisconnectSheet();
                    ShowMainMenu();
                    return true;
                case "settings":
                    CloseDisconnectSheet();
                    ShowMainMenu();
                    OpenSettings();
                    return _settingsSheet != null;
                case "disconnect":
                    CloseSettings();
                    ShowMainMenu();
                    if (_disconnectSheet == null) return false;   // BuildDisconnectSheet already reported the wiring gap
                    SetDisconnectStatus(null);
                    UiElements.SetShown(_disconnectBody, true);
                    UiElements.SetShown(_disconnectRejoinBtn, true);
                    _disconnectRejoinBtn.SetEnabled(false);   // ConsumeDisconnectNotice re-enables it for a real notice
                    _disconnectBackBtn.SetEnabled(true);
                    UiElements.SetShown(_disconnectSheet, true);
                    return true;
                case "class": ShowClassSelect(); return true;
                case "gear":  ShowLoadout(); return true;
                case "coop":
                case "coopjoin":
                    _selection.Mode = id == "coop" ? SessionMode.Solo : SessionMode.Join;
                    _lobbyViaGearUp = true;
                    ShowLobby();
                    return true;
                default:
                    _log?.Warn(Source, $"Auto-test page '{id}' is not a menu page id.");
                    return false;
            }
        }

        /// <summary>The host's join code once UGS created the lobby (THE COOP's code tiles); empty before.</summary>
        public string AutoTestJoinCode => _joinCode;

        /// <summary>Failed JOIN MATCH attempts (the auto-test's retry signal).</summary>
        public int AutoTestJoinFailures => _autoTestJoinFailures;

        private bool AutoTestAllowed(string what)
        {
            if (DevToolsAvailable && _selection != null) return true;
            _log?.Warn(Source, $"Auto-test '{what}' refused: dev tools are not available or the menu is not built.");
            return false;
        }

        /// <summary>Main menu HOST / JOIN.</summary>
        public bool AutoTestChooseMode(SessionMode mode)
        {
            if (!AutoTestAllowed("mode")) return false;
            ChooseMode(mode);
            return true;
        }

        /// <summary>PICK YOUR BIRD: the class tile (which also seeds the perk's starters, the pre-filled loadout).</summary>
        public bool AutoTestPickClass(ChickenClass cls)
        {
            if (!AutoTestAllowed("class")) return false;
            OnClassTileTapped(cls);
            return _selection.SelectedClass == cls && _selection.Passive != null;
        }

        /// <summary>PICK YOUR BIRD: the perk badge of <paramref name="subclass"/> (<c>cw_perk</c>); its class must be selected.</summary>
        public bool AutoTestPickPerk(ChickenSubclass subclass)
        {
            if (!AutoTestAllowed("perk")) return false;
            var perk = PerksFor(Cls).FirstOrDefault(p => p.Subclass == subclass);
            if (perk == null) return false;
            SelectClassAndPassive(Cls, perk);
            return _selection.Passive == perk;
        }

        /// <summary>Pins the solo bots' specializations by index (<c>cw_botperks</c>); read by THE COOP and the spawner.</summary>
        public void AutoTestForceBots(IReadOnlyList<ChickenSubclass> subclasses)
        {
            if (AutoTestAllowed("botperks")) _selection.DevBotSubclasses = subclasses;
        }

        /// <summary>PICK YOUR BIRD: NEXT.</summary>
        public void AutoTestOpenLoadout()
        {
            if (AutoTestAllowed("next")) ShowLoadout();
        }

        /// <summary>
        /// GEAR UP: taps deck cards in deck order (shared row, then the class row; starters skipped) until every slot is
        /// filled, as a player tapping the first cards would. Returns the slots still empty and the resulting loadout.
        /// </summary>
        public int AutoTestFillLoadout(out string loadout)
        {
            loadout = string.Empty;
            if (!AutoTestAllowed("loadout")) return AbilityController.SlotCount;
            SyncSlotModel();
            _slotModel.AutoArmFirstEmpty();
            foreach (var card in _deckCards.ToArray())
            {
                if (_slotModel.Missing == 0) break;
                if (card.Ability == null || _slotModel.SlotOf(card.Ability) >= 0 || IsPreEquipped(card.Ability)) continue;
                OnAbilityCardTapped(card.Ability);
                SyncSlotModel();
            }
            SettleFlights();
            var names = new string[ActiveSlotsForClass];
            for (int i = 0; i < names.Length; i++) names[i] = GetEquipped(i) != null ? GetEquipped(i).name : "-";
            loadout = string.Join(",", names);
            return _slotModel.Missing;
        }

        /// <summary>GEAR UP: READY (only when the button itself would be enabled).</summary>
        public bool AutoTestPressReady()
        {
            if (!AutoTestAllowed("ready")) return false;
            SyncSlotModel();
            if (_slotModel.Missing != 0 || _selection.Passive == null) return false;
            OnReady();
            return true;
        }

        private int _autoTestRejoinFailures;

        /// <summary>Failed REJOIN presses (the auto-test's outcome signal).</summary>
        public int AutoTestRejoinFailures => _autoTestRejoinFailures;

        /// <summary>The CW-218 "You were disconnected" sheet is on screen.</summary>
        public bool AutoTestDisconnectSheetOpen => DevToolsAvailable && IsDisconnectSheetOpen;

        /// <summary>The disconnect sheet's REJOIN (only while the button itself is shown and nothing is loading).</summary>
        public bool AutoTestPressRejoin()
        {
            if (!AutoTestAllowed("rejoin")) return false;
            if (!IsDisconnectSheetOpen || _isBusy || _disconnectRejoinBtn.style.display == DisplayStyle.None) return false;
            OnRejoin();
            return true;
        }

        /// <summary>THE COOP: START (host) or, with a code typed into the join field, JOIN MATCH (guest).</summary>
        public bool AutoTestPressStartOrJoin(string joinCode)
        {
            if (!AutoTestAllowed("start")) return false;
            if (_selection.Mode == SessionMode.Join)
            {
                var field = _lobby.Q<TextField>("LobbyJoinField");
                if (field == null)
                {
                    _log?.Error(Source, "Auto-test JOIN: Lobby.uxml has no #LobbyJoinField.");
                    return false;
                }
                if (!JoinCodeEntry.IsComplete(joinCode))
                {
                    // OnStartMatch would only show "enter a code" and return: refuse here so the auto-test does not wait on it.
                    _log?.Warn(Source, $"Auto-test JOIN refused: '{joinCode}' is not a whole join code.");
                    return false;
                }
                field.value = joinCode;   // the same callback a keyboard runs (upper-case, status pill)
            }
            OnStartMatch();
            return true;
        }

        // ======================================================================
        //  Helpers
        // ======================================================================
        private static string KeyOf(ChickenClass cls) => cls.ToString().ToLowerInvariant();

        private static Color Fade(Color c, float a) => new Color(c.r, c.g, c.b, a);

        /// <summary>Blend a colour <paramref name="t"/> of the way toward white, for use as text on the dark screen bg.</summary>
        private static Color Lighten(Color c, float t) =>
            new Color(Mathf.Lerp(c.r, 1f, t), Mathf.Lerp(c.g, 1f, t), Mathf.Lerp(c.b, 1f, t), 1f);

        private static void SetBorder(VisualElement ve, Color c)
        {
            ve.style.borderTopColor = c; ve.style.borderBottomColor = c;
            ve.style.borderLeftColor = c; ve.style.borderRightColor = c;
        }

        /// <summary>
        /// Runs <paramref name="act"/> on the named element. It used to skip a missing one in silence, so a
        /// UXML rename left a control dead with nothing in the log; <see cref="UiElements.Bind{T}"/> reports
        /// the miss once per name (A22).
        /// </summary>
        private void Bind<T>(VisualElement root, string name, Action<T> act) where T : VisualElement =>
            UiElements.Bind(root, name, act, _log, Source);
    }
}

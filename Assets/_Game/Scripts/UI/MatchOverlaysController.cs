using CluckWars.Injection;
using System;
using System.Collections.Generic;
using CluckWars.Abilities;
using CluckWars.Diagnostics;
using CluckWars.Gameplay;
using CluckWars.Input;
using CluckWars.Localization;
using CluckWars.Logging;
using CluckWars.Networking;
using CluckWars.Services;
using CluckWars.Settings;
using Fusion;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using Zenject;
using LogLevel = CluckWars.Logging.LogLevel;

namespace CluckWars.UI
{
    /// <summary>
    /// UI Toolkit driver for the four in-match overlays: Match End (Phase 4: a podium on the barn),
    /// the multiplayer waiting room (Phase 5: THE COOP's lineup), Session End, Intro countdown. Layout lives in
    /// <c>Assets/UI/MatchOverlays.uxml</c>; styling in
    /// <c>Assets/UI/Styles/MatchOverlays.uss</c>. This controller only toggles each
    /// overlay root's visibility per <see cref="GameManager"/> state / network
    /// shutdown and fills the data containers (podium, rows, code tiles, player grid).
    /// Who each chicken is called, the final order and the KO-column rule live in
    /// <see cref="MatchStandings"/> (pure, tested).
    /// </summary>
    /// <remarks>
    /// Reads only through injected interfaces (<see cref="INetworkService"/>,
    /// <see cref="ILogService"/>, …) and the gameplay static registries
    /// (<see cref="PlayerBase.ActiveBases"/>, <see cref="ChickenController.ActiveControllers"/>,
    /// <see cref="ChickenMatchStats.ActiveStats"/>) — no direct Fusion/UGS calls
    /// beyond what MatchHud already did. Per CONVENTIONS IP-fix1 the self-inject
    /// falls back to the SceneContext first (INetworkService is scene-scoped).
    /// </remarks>
    [RequireComponent(typeof(UIDocument))]
    public sealed partial class MatchOverlaysController : MonoBehaviour
    {
        private const string Source = "MatchOverlays";

        [SerializeField] private float _refreshInterval = 0.25f;

        [Tooltip("Seconds the 'Session ended' overlay stays up before auto-returning.")]
        [Min(0.5f)]
        [SerializeField] private float _disconnectReturnDelay = 5f;

        [Tooltip("Bootstrap scene to load after a disconnect.")]
        [SerializeField] private string _bootstrapSceneName = "Bootstrap";

        // ---- Injected services -------------------------------------------------
        private INetworkService          _network;
        private ILogService              _log;
        private ColorSchemeSO            _colors;      // injected for DI parity with MatchHud
        private MatchConfigSO            _matchConfig;
        private ISessionSelectionService _selection;
        private IInputProvider           _input;        // Esc / Android back / pad Start: LEAVE (waiting room), "Leave match?" (round), BACK TO LOBBY (podium)
        private MenuAudio                _audio = MenuAudio.Silent();
        private ChickenClassRegistrySO   _classRegistry;

        // ---- UI element refs (queried once, on bind) ---------------------------
        private VisualElement _root;
        private bool          _bound;

        private VisualElement _matchEndOverlay, _lobbyOverlay, _sessionEndOverlay, _introOverlay;

        // "Leave match?" (round-3 finding 4): Back during GET READY / the countdown / a round asks first.
        private VisualElement _leaveSheet;
        private Label         _leaveBody;
        private Button        _leaveKeepBtn, _leaveConfirmBtn;
        private bool          _leaveSheetOpen;
        // True while this controller holds the solo runner paused under the sheet (always released on close / leave / destroy).
        private bool          _soloPaused;
        // The 45% countdown scrim is on for GET READY and the 3-2-1 only (finding 17); the top bar dims with it.
        private bool          _introScrim;
        // Post-match music (finding 7): when the podium appeared and whether the menu bed has started under it.
        private float         _podiumShownAt;
        private bool          _podiumBedStarted;

        private Label         _meRibbon, _meWinSub, _meTargetNote, _meHostNote;
        private Button        _mePlayAgainBtn, _meBackBtn;
        private VisualElement _meSafe, _meCrown, _meWinGlow, _meRows, _meHero, _mePodium;
        // "You · 2nd" under the banner when someone else won (round-2 finding 6).
        private Label         _meYouChip;
        // The crowned step (PodiumLayout: 1.25x, crown on the head) or -1, and the winner's class for the
        // static head anchor; set once per match end.
        private readonly bool[]          _crownSlots = new bool[3];             // podium slots wearing a crown (ties crown several)
        private readonly ChickenClass[]  _crownClasses = new ChickenClass[3];
        private readonly VisualElement[] _crowns = new VisualElement[3];        // [1] is #MeCrown; the side steps' are built in code
        // Feathers + sparkles under the podium column (Phase 3B); null if the UXML has no #MeCelebration.
        private MatchCelebration _celebration;
        // Local presentation of the networked intro timer (Phase 3B): tick / GO cues + a slam on the numeral.
        private MenuJuice _introJuice;
        private readonly IntroCueTracker _introCues = new();

        /// <summary>One podium step (#MePod0..2, left to right = 2nd, 1st, 3rd; #MePod3 = draw only).</summary>
        private sealed class PodView
        {
            public VisualElement Root, Ring, Chicken, Plate, Medal;
            public Label Name, Score, Place;
            public string ChickenCss;   // .cw-chicken--<class> currently on Chicken (for swap)
        }
        // Three podium steps plus a 4th pod that only the draw layout uses (four birds on level ground).
        private readonly PodView[] _pods = new PodView[4];

        // Live 3D podium (Performance Mode OFF): the menus' MenuChickenStage, one slot per step.
        // Null = static hero renders (Performance Mode ON, no registry, or the stage failed once).
        private MenuChickenStage      _stage;
        private bool                  _stageFailed;

        private VisualElement _lobbySafe, _codeTiles, _lobbyGrid, _lobbyStatusDot, _lobbyInviteCard;
        private Label         _lobbyStatusCount, _lobbyStatusText, _lobbyHint, _lobbySettingTime, _lobbySettingGoal;
        private Button        _lobbyStartBtn, _lobbyShareBtn, _lobbyCopyBtn, _lobbyLeaveBtn, _lobbyChangeBirdBtn;

        /// <summary>One waiting-room seat (THE COOP's .cw-seat), one per spawn corner, built once on bind.</summary>
        private sealed class SeatView
        {
            public VisualElement Root, Pedestal, Chicken, Plate, NameRow, Hexes, State;
            public Label Name, Pn, Tag, ClassLine, Waiting, StateLabel, PedestalTag;
            public readonly List<(VisualElement Hex, AbilityIconView Icon)> HexViews = new();
            public string ChickenCss;
        }
        private readonly SeatView[] _seats = new SeatView[4];
        private readonly bool[]     _seatFilled = new bool[4];

        private Label         _sessionEndTitle, _sessionEndReason, _sessionEndCountdown;
        private Button        _sessionEndBackBtn;
        // The comeback / event banner (a wood plaque; MatchHud feeds it through EventBannerFeed).
        private VisualElement _eventBanner;
        private Label         _eventBannerHeader, _eventBannerText;
        private bool          _sessionEndLeaveNow;   // BACK TO THE BARN pressed: skip what is left of the countdown
        private Label         _introNumber;
        // GET READY inside the intro overlay (finding 11) + its "Waiting for {name}" line (finding 13).
        private VisualElement _introGetReady;
        private Label         _introWaiting;
        private VisualElement _hostLeftNotice;
        // The opaque arena cover inside the intro overlay (ArenaCoverRule) + this peer's local arena readiness.
        private VisualElement   _arenaCover;
        private IArenaReadiness _arenaReadiness;

        // ---- Runtime state -----------------------------------------------------
        private float           _nextRefresh;
        private bool            _matchEndPopulated;
        private ShutdownReason? _shutdownReason;
        private SessionExitKind _exitKind;      // DisconnectRules.Classify of _shutdownReason (CW-218)
        private float           _shutdownAtUnscaledTime;
        private bool            _returnTriggered;
        // The round this peer watched had ended (its result was on screen) when the session closed: the
        // session-end headline says MATCH OVER instead of COOP CLOSED (SessionEndCopy.HeadlineKey).
        private bool            _roundEndedAtShutdown;
        // True once BACK TO LOBBY / LEAVE / CHANGE BIRD has started leaving on purpose; suppresses the
        // "Session ended" overlay that the runner shutdown would otherwise raise through HandleShutdown.
        private bool            _leavingToLobby;
        private const int       ShutdownTimeoutMs = 5000;

        // Intro GO flourish — lingers briefly after the countdown hits zero.
        private float       _goExpiresAtUnscaledTime;

        // Pre-intro GET READY (IntroOverlayRule): whether a GameManager has ever been seen, since when
        // this overlay has been waiting for the first one, and whether the no-manager timeout was reported.
        private bool        _sawGameManager;
        private float       _waitingForManagerSince = -1f;
        private bool        _reportedNoManager;
        private bool        _noManagerTimedOut;

        // Arena cover (ArenaCoverRule): latched once per scene load when the arena is revealed, the seconds the cover has actually
        // been wanted (the waiting room does not count), when its fade-out ends, and the one-shot wiring error.
        private bool        _arenaRevealed;
        private float       _arenaCoveredSeconds;
        private float       _arenaCoverFadeEndsAt = -1f;
        private bool        _reportedNoReadiness;

        // Host-left notice (finding 13): the manager this overlay watched last frame, its state authority and
        // state, and until when the notice stays up.
        private GameManager _watchedManager;
        private PlayerRef   _watchedAuthority = PlayerRef.None;
        private MatchState  _watchedState = MatchState.WaitingForPlayers;
        private float       _hostLeftUntilUnscaledTime = -1f;
        public const float  HostLeftNoticeSeconds = 3f;

        // ---- Injection ---------------------------------------------------------
        [Inject]
        public void Construct(
            INetworkService          network,
            ILogService              log,
            ColorSchemeSO            colors,
            MatchConfigSO            matchConfig,
            ISessionSelectionService selection,
            [InjectOptional] MenuAudio              audio,
            [InjectOptional] ChickenClassRegistrySO classRegistry,
            [InjectOptional] IInputProvider         input,
            [InjectOptional] IArenaReadiness        arenaReadiness)
        {
            _arenaReadiness = arenaReadiness;
            _network       = network;
            _log           = log;
            _colors        = colors;
            _matchConfig   = matchConfig;
            _selection     = selection;
            _audio         = audio ?? MenuAudio.Silent();
            _classRegistry = classRegistry;
            _input         = input;
        }

        // ---- Unity lifecycle ---------------------------------------------------
        private void Awake()
        {
            SelfInject.Ensure(this, _network != null);

            if (_network != null) _network.OnShutdown += HandleShutdown;
            if (_input == null)
                _log?.Warn(Source, "IInputProvider not injected; Esc / Android back / pad Start will not leave the match (the on-screen buttons still work).");
            UiElements.EnsureEventSystem();
        }

        private void OnDisable()
        {
            // The sheet's pause and input gate are process-wide: never leave them behind with this controller.
            CloseLeaveSheet();
        }

        private void OnDestroy()
        {
            // The cover goes with this scene: nothing may read a stale "cover up" after it.
            RevealState.ResetForScene();
            if (_network != null) _network.OnShutdown -= HandleShutdown;
            // The stage owns cameras, lights, models and RenderTextures in this scene plus render-
            // pipeline callbacks that would outlive it: always hand them back when the scene goes.
            DisposeStage();
        }

        private void HandleShutdown(ShutdownReason reason)
        {
            if (_leavingToLobby) return; // our own BACK TO LOBBY shutdown, not a dropped session
            _shutdownReason         = reason;
            _shutdownAtUnscaledTime = Time.unscaledTime;
            var gm = GameManager.Instance;
            _roundEndedAtShutdown   = (gm != null ? gm.State : _watchedState) == MatchState.Ended;

            // CW-218: a lost connection (e.g. the app backgrounded past the timeout) goes straight to the menu, which
            // says "You were disconnected" and offers REJOIN; host-left / closed sessions keep this screen's countdown.
            var transport = _network?.LastDisconnectReason;
            _exitKind = DisconnectRules.Classify(reason, transport, leftOnPurpose: false);
            var mode = _selection != null ? _selection.Mode : SessionMode.Solo;
            bool menuNotice = DisconnectRules.ShowsMenuNotice(_exitKind, mode);
            if (menuNotice)   // only for a multiplayer mode, so _selection is there
            {
                _selection.DisconnectNoticeOnMenuLoad = true;
                _sessionEndLeaveNow = true;   // TickSessionEnd loads the menu at once
            }
            _log?.Warn(Source, $"Network shutdown: {reason} (transport={(transport.HasValue ? transport.Value.ToString() : "none")}, " +
                $"exit={_exitKind}, mode={mode}, round ended: {_roundEndedAtShutdown}). " +
                (menuNotice ? $"Returning to '{_bootstrapSceneName}' now with the disconnect notice."
                            : $"Returning to '{_bootstrapSceneName}' in {_disconnectReturnDelay}s."));
        }

        private void Update()
        {
            EnsureBound();
            if (!_bound) return;
            _safeArea.Apply();

            TickEventBanner();

            if (_shutdownReason.HasValue)
            {
                CloseLeaveSheet();   // the session-end screen owns a session that closed under the sheet
                TickSessionEnd();
                return;
            }

            bool poll = Time.unscaledTime >= _nextRefresh;
            if (poll) _nextRefresh = Time.unscaledTime + _refreshInterval;

            var gm = GameManager.Instance;
            RefreshBack(gm);
            RefreshMatchEnd(gm);
            RefreshLobby(gm, poll);
            RefreshIntro(gm);
            RefreshHostLeft(gm);

            // The top bar recedes behind ANY full-screen modal, not just the intro.
            // Previously only RefreshIntro drove this, so during MATCH END the live
            // leaderboard sat at full brightness on top of the dimmed arena, directly
            // competing with the FINAL STANDINGS panel showing the same four scores.
            SetTopBarDimmed(_introScrim && IsShown(_introOverlay));
            SetTopBarHidden(IsShown(_matchEndOverlay) || IsShown(_lobbyOverlay));
        }

        private void LateUpdate()
        {
            if (_stage != null)
            {
                try { _stage.Tick(); }
                catch (Exception e) { FailStage("the podium stage could not render", e); }
            }
            PlaceCrown();
        }

        private static bool IsShown(VisualElement ve) =>
            ve != null && ve.style.display.value == DisplayStyle.Flex;

        // ---- Binding -----------------------------------------------------------
        private void EnsureBound()
        {
            if (_bound) return;
            var doc = GetComponent<UIDocument>();
            _root = doc != null ? doc.rootVisualElement : null;
            if (_root == null) return; // UIDocument not ready yet — retry next frame.
            NavFocusRing.Attach(_root);

            _matchEndOverlay   = _root.Q<VisualElement>("MatchEndOverlay");
            _lobbyOverlay      = _root.Q<VisualElement>("LobbyOverlay");
            _sessionEndOverlay = _root.Q<VisualElement>("SessionEndOverlay");
            _introOverlay      = _root.Q<VisualElement>("IntroOverlay");
            _leaveSheet        = _root.Q<VisualElement>("LeaveSheet");
            _leaveBody         = _root.Q<Label>("LeaveBody");
            _leaveKeepBtn      = _root.Q<Button>("LeaveKeepBtn");
            _leaveConfirmBtn   = _root.Q<Button>("LeaveConfirmBtn");
            if (_leaveSheet == null || _leaveBody == null || _leaveKeepBtn == null || _leaveConfirmBtn == null)
                _log?.Error(Source, "MatchOverlays.uxml is missing #LeaveSheet / #LeaveBody / #LeaveKeepBtn / #LeaveConfirmBtn; Back in a running match will do nothing.");

            _meSafe         = _root.Q<VisualElement>("MeSafe");
            _meRibbon       = _root.Q<Label>("MeWinRibbonLabel");
            _meCrown        = _root.Q<VisualElement>("MeCrown");
            _meWinGlow      = _root.Q<VisualElement>("MeWinGlow");
            _meWinSub       = _root.Q<Label>("MeWinSub");
            _meTargetNote   = _root.Q<Label>("MeTargetNote");
            _meHostNote     = _root.Q<Label>("MeHostNote");
            _mePlayAgainBtn = _root.Q<Button>("MePlayAgainBtn");
            _meBackBtn      = _root.Q<Button>("MeBackBtn");
            _meRows         = _root.Q<VisualElement>("MeRows");
            _meHero         = _root.Q<VisualElement>("MeHero");
            _mePodium       = _root.Q<VisualElement>("MePodium");
            _meYouChip      = _root.Q<Label>("MeYouChip");
            if (_meYouChip == null) _log?.Error(Source, "MatchOverlays.uxml has no #MeYouChip; a CPU / rival win will not show the local player's place.");
            for (int i = 0; i < _pods.Length; i++)
            {
                _pods[i] = new PodView
                {
                    Root    = _root.Q<VisualElement>($"MePod{i}"),
                    Ring    = _root.Q<VisualElement>($"MePod{i}Ring"),
                    Chicken = _root.Q<VisualElement>($"MePod{i}Chicken"),
                    Plate   = _root.Q<VisualElement>($"MePod{i}Plate"),
                    Medal   = _root.Q<VisualElement>($"MePod{i}Medal"),
                    Name    = _root.Q<Label>($"MePod{i}Name"),
                    Score   = _root.Q<Label>($"MePod{i}Score"),
                    Place   = _root.Q<Label>($"MePod{i}Place"),
                };
                if (_pods[i].Root == null || _pods[i].Chicken == null || _pods[i].Name == null)
                    _log?.Error(Source, $"MatchOverlays.uxml is missing podium step #MePod{i} (or its chicken / name); the podium will be incomplete.");
            }
            _crowns[1] = _meCrown;
            for (int slot = 0; slot < _crowns.Length; slot++)
            {
                if (_crowns[slot] != null || _pods[slot]?.Chicken == null) continue;
                // A tie for 1st crowns every tied bird, so the side steps need a crown too (same class as #MeCrown).
                var crown = new VisualElement { name = $"MeCrown{slot}", pickingMode = PickingMode.Ignore };
                crown.AddToClassList("cw-me-crown");
                crown.style.display = DisplayStyle.None;
                _pods[slot].Chicken.Add(crown);
                _crowns[slot] = crown;
            }
            foreach (var pod in _pods) pod.Chicken?.RegisterCallback<GeometryChangedEvent>(_ => ApplyPodiumScales());
            var celebrationLayer = _root.Q<VisualElement>("MeCelebration");
            if (celebrationLayer != null) _celebration = new MatchCelebration(celebrationLayer);
            else _log?.Error(Source, "MatchOverlays.uxml has no #MeCelebration; the winner celebration will not show.");

            _lobbySafe        = _root.Q<VisualElement>("LobbySafe");
            _codeTiles        = _root.Q<VisualElement>("CodeTiles");
            _lobbyGrid        = _root.Q<VisualElement>("LobbyGrid");
            _lobbyInviteCard  = _root.Q<VisualElement>("LobbyInviteCard");
            _lobbyStatusDot   = _root.Q<VisualElement>("LobbyStatusDot");
            _lobbyStatusCount = _root.Q<Label>("LobbyStatusCount");
            _lobbyStatusText  = _root.Q<Label>("LobbyStatusText");
            _lobbyHint        = _root.Q<Label>("LobbyHint");
            _lobbySettingTime = _root.Q<Label>("LobbySettingTime");
            _lobbySettingGoal = _root.Q<Label>("LobbySettingGoal");
            _lobbyStartBtn    = _root.Q<Button>("LobbyStartBtn");
            _lobbyShareBtn    = _root.Q<Button>("LobbyShareBtn");
            _lobbyCopyBtn     = _root.Q<Button>("LobbyCopyBtn");
            _lobbyLeaveBtn      = _root.Q<Button>("LobbyLeaveBtn");
            _lobbyChangeBirdBtn = _root.Q<Button>("LobbyChangeBirdBtn");
            if (_lobbyGrid == null) _log?.Error(Source, "MatchOverlays.uxml has no #LobbyGrid; the waiting room shows no seats.");
            else BuildSeats();
            if (_lobbyLeaveBtn == null || _lobbyChangeBirdBtn == null)
                _log?.Error(Source, "MatchOverlays.uxml is missing #LobbyLeaveBtn or #LobbyChangeBirdBtn; the waiting room has no tap-out (Esc / back still leaves).");

            _sessionEndTitle     = _root.Q<Label>("SessionEndTitle");
            _sessionEndReason    = _root.Q<Label>("SessionEndReason");
            _sessionEndCountdown = _root.Q<Label>("SessionEndCountdown");
            _sessionEndBackBtn   = _root.Q<Button>("SessionEndBackBtn");
            _eventBanner         = _root.Q<VisualElement>("EventBanner");
            _eventBannerHeader   = _root.Q<Label>("EventBannerHeader");
            _eventBannerText     = _root.Q<Label>("EventBannerText");
            EventBannerFeed.Hide();   // a banner left up by the previous scene never carries over
            if (_sessionEndBackBtn != null) _sessionEndBackBtn.clicked += () => _sessionEndLeaveNow = true;
            _introNumber         = _root.Q<Label>("IntroNumber");
            _introGetReady       = _root.Q<VisualElement>("IntroGetReady");
            _introWaiting        = _root.Q<Label>("IntroWaiting");
            _arenaCover          = _root.Q<VisualElement>("ArenaCover");
            _hostLeftNotice      = _root.Q<VisualElement>("HostLeftNotice");
            if (_introGetReady == null) _log?.Error(Source, "MatchOverlays.uxml has no #IntroGetReady; GET READY will not show before the countdown.");
            if (_arenaCover == null) _log?.Error(Source, "MatchOverlays.uxml has no #ArenaCover; the arena is revealed as soon as the intro arms, " +
                "so cold-start shader placeholders and pop-in may show.");
            if (_introWaiting == null) _log?.Error(Source, "MatchOverlays.uxml has no #IntroWaiting; GET READY cannot say who the room waits for.");
            if (_hostLeftNotice == null) _log?.Error(Source, "MatchOverlays.uxml has no #HostLeftNotice; a host leaving mid-round will go unannounced.");
            if (_introOverlay != null) _introJuice = new MenuJuice(_introOverlay);

            if (_mePlayAgainBtn != null) _mePlayAgainBtn.clicked += OnPlayAgain;
            if (_meBackBtn      != null) _meBackBtn.clicked      += OnBackToLobby;
            if (_lobbyStartBtn != null) _lobbyStartBtn.clicked += OnLobbyStart;
            if (_lobbyCopyBtn  != null) _lobbyCopyBtn.clicked  += CopyJoinCode;
            if (_lobbyShareBtn != null) _lobbyShareBtn.clicked += CopyJoinCode;
            if (_lobbyLeaveBtn != null) _lobbyLeaveBtn.clicked += OnLobbyLeave;
            if (_lobbyChangeBirdBtn != null) _lobbyChangeBirdBtn.clicked += OnLobbyChangeBird;
            if (_leaveKeepBtn    != null) _leaveKeepBtn.clicked    += OnLeaveKeep;
            if (_leaveConfirmBtn != null) _leaveConfirmBtn.clicked += OnLeaveConfirm;

            // One-time: every @key text in the overlays comes from the wording dictionary.
            UiText.SetLogger(_log);
            UiText.ResolveTree(_root);

            // Same safe-area rule as the menu: content clear of notches / the gesture bar while the
            // backgrounds stay full-bleed (#MeSafe / #LobbySafe sit inside their barn backdrops; the
            // session-end overlay pads itself, its dim fill still reaching the edges).
            if (_meSafe == null) _log?.Error(Source, "MatchOverlays.uxml has no #MeSafe; the post-match screen ignores the safe area.");
            if (_lobbySafe == null) _log?.Error(Source, "MatchOverlays.uxml has no #LobbySafe; the waiting room ignores the safe area.");
            _safeArea = new SafeAreaPadding(_meSafe, _lobbySafe, _sessionEndOverlay, _leaveSheet);
            _root.RegisterCallback<GeometryChangedEvent>(_ => _safeArea.Apply(force: true));
            // The menu's shape classes (LayoutClasses), measured on the always-visible overlay panel and set on each full-screen
            // overlay that has orientation rules (WP9): the waiting room (#LobbyOverlay.layout--short / --narrow slim THE COOP's
            // bar; --portrait stacks it), the results (column in portrait), the LEAVE and session-end sheets and the intro GET
            // READY card (a smaller plate in portrait). They stay off the overlay root itself; the menu's short-screen plate
            // rule is scoped to the menu card (#GetReadyCard), so the intro card keeps its full size in landscape.
            LayoutClasses.BindAll(_root, _lobbyOverlay, _matchEndOverlay, _leaveSheet, _sessionEndOverlay, _introGetReady);
            // The event banner's portrait position (WP7, .cw-eventbanner.layout--portrait). Bind keeps one target per
            // measured element, so this one measures the full-panel #OverlayRoot instead of _root.
            var overlayHost = _root.Q<VisualElement>("OverlayRoot");
            if (_eventBanner != null && overlayHost != null) LayoutClasses.Bind(overlayHost, _eventBanner);
            else _log?.Error(Source, "MatchOverlays.uxml has no #EventBanner or #OverlayRoot: the event banner keeps the landscape spot in portrait, over the ability hexes.");

            // Start hidden; state polls flip them on.
            UiElements.SetShown(_matchEndOverlay, false);
            UiElements.SetShown(_lobbyOverlay, false);
            UiElements.SetShown(_sessionEndOverlay, false);
            // #IntroOverlay / #ArenaCover / #IntroGetReady stay visible (UXML default): the first RefreshIntro below decides, in
            // this same Update, before anything renders. Hiding them here would flash the bare world for a frame.
            UiElements.SetShown(_introWaiting, false);
            UiElements.SetShown(_hostLeftNotice, false);
            UiElements.SetShown(_leaveSheet, false);

            _bound = true;
            _log?.Debug(Source, "Overlays bound.");
        }


        // ========================================================================
        //  BACK  (Esc / Android back / pad Start) + the "Leave match?" sheet
        // ========================================================================
        /// <summary>
        /// One Back press = one action, decided per match phase by <see cref="MatchBackRules"/>: LEAVE in the
        /// waiting room, the "Leave match?" sheet in GET READY / the countdown / a round (Back again closes it),
        /// BACK TO LOBBY on the podium. Also keeps the sheet honest: it closes when the phase no longer allows it
        /// and a solo runner is paused exactly while it is up.
        /// </summary>
        private void RefreshBack(GameManager gm)
        {
            MatchState? state = gm != null ? gm.State : (MatchState?)null;
            if (_leaveSheetOpen && !MatchBackRules.SheetAllowed(state)) CloseLeaveSheet();

            if (!_leavingToLobby && _input != null && _input.GetBackPressed())
            {
                // Esc is also the ability cancel: with a move held it only cancels it. A pad's B is slot 3 in a match: it never opens the sheet or leaves the waiting room (Start does).
                switch (MatchBackRules.Resolve(state, _leaveSheetOpen, _leavingToLobby, AnyAbilityHeld(), _input.GetPausePressed()))
                {
                    case MatchBackAction.LeaveWaitingRoom: OnLobbyLeave(); return;
                    case MatchBackAction.OpenLeaveSheet:   OpenLeaveSheet(); break;
                    case MatchBackAction.CloseLeaveSheet:  _audio.Back(); CloseLeaveSheet(); break;
                    case MatchBackAction.BackToLobby:      OnBackToLobby(); return;
                }
            }

            ApplySoloPause(state);
        }

        private bool AnyAbilityHeld()
        {
            for (int i = 0; i < 4; i++)
                if (_input.GetAbilityHeld(i)) return true;
            return false;
        }

        private bool IsSolo()
        {
            var runner = _network?.Runner;
            return runner != null && runner.GameMode == GameMode.Single;
        }

        private void OpenLeaveSheet()
        {
            if (_leaveSheetOpen || _leaveSheet == null) return;
            _leaveSheetOpen = true;
            LeaveSheetState.IsOpen = true;
            bool solo = IsSolo();
            if (_leaveBody != null) _leaveBody.text = UiText.Get(solo ? UiKeys.LeaveBodySolo : UiKeys.LeaveBodyMp);
            UiElements.SetShown(_leaveSheet, true);
            _audio.Tap();
            _leaveKeepBtn?.Focus();   // the default focus: a pad / keyboard confirm keeps playing
            _log?.Info(Source, $"Leave sheet opened ({(solo ? "solo: the round pauses" : "multiplayer: the match keeps going")}).");
        }

        /// <summary>Hides the sheet, lifts the input gate and resumes a paused solo runner. Safe to call when it is not open.</summary>
        private void CloseLeaveSheet()
        {
            bool wasOpen = _leaveSheetOpen;
            _leaveSheetOpen = false;
            LeaveSheetState.IsOpen = false;
            UiElements.SetShown(_leaveSheet, false);
            ApplySoloPause(GameManager.Instance != null ? GameManager.Instance.State : (MatchState?)null);
            if (wasOpen) _log?.Info(Source, "Leave sheet closed.");
        }

        /// <summary>Solo only: <c>NetworkRunner.SinglePlayerPause</c> freezes ticks, so the match clock, bots,
        /// cooldowns and physics all stop together. Multiplayer is never paused.</summary>
        private void ApplySoloPause(MatchState? state)
        {
            bool want = MatchBackRules.PausesSimulation(IsSolo(), state, _leaveSheetOpen);
            if (want == _soloPaused) return;
            var runner = _network?.Runner;
            if (runner != null)
            {
                try { runner.SinglePlayerPause(want); }
                catch (Exception e)
                {
                    _log?.Error(Source, $"SinglePlayerPause({want}) failed: {e}. The round {(want ? "keeps running under the sheet" : "may stay frozen - leave and restart")}.");
                }
            }
            _soloPaused = want;
        }

        private void OnLeaveKeep()
        {
            _audio.Back();
            CloseLeaveSheet();
        }

        private void OnLeaveConfirm()
        {
            if (_leavingToLobby) return;
            _audio.Back();
            LeaveSession(changeBird: false, "LEAVE");
        }

        // ========================================================================
        //  MATCH END
        // ========================================================================


        /// <summary>
        /// Every deliberate exit from the match scene: disables the exits, sets the menu's one-shot landing
        /// flag (THE COOP / main menu by mode, or PICK YOUR BIRD for CHANGE BIRD), shuts the runner down and
        /// loads Bootstrap.
        /// </summary>
        private void LeaveSession(bool changeBird, string why)
        {
            _leavingToLobby = true;
            // A paused solo runner must be running again before it shuts down.
            CloseLeaveSheet();
            foreach (var b in new[] { _mePlayAgainBtn, _meBackBtn, _lobbyStartBtn, _lobbyLeaveBtn, _lobbyChangeBirdBtn })
                b?.SetEnabled(false);

            if (_selection == null)
                _log?.Error(Source, $"No ISessionSelectionService injected: {why} will open the main menu instead of " +
                    (changeBird ? "PICK YOUR BIRD." : "THE COOP."));
            else if (changeBird) _selection.OpenClassSelectOnMenuLoad = true;
            else _selection.OpenLobbyOnMenuLoad = true;
            _log?.Info(Source, $"{why}: leaving the session.");

            // async void would swallow anything thrown after the first await; LeaveToLobbyAsync
            // catches and logs everything itself, and the continuation surfaces a fault of its own.
            LeaveToLobbyAsync().ContinueWith(
                t => _log?.Error(Source, $"{why} flow faulted: {t.Exception}"),
                System.Threading.Tasks.TaskContinuationOptions.OnlyOnFaulted
                | System.Threading.Tasks.TaskContinuationOptions.ExecuteSynchronously);
        }

        private async System.Threading.Tasks.Task LeaveToLobbyAsync()
        {
            if (_network == null)
            {
                _log?.Error(Source, "No INetworkService injected: leaving cannot shut the session down, loading the menu anyway.");
            }
            else
            {
                try
                {
                    var shutdown = _network.ShutdownAsync();
                    var done = await System.Threading.Tasks.Task.WhenAny(
                        shutdown, System.Threading.Tasks.Task.Delay(ShutdownTimeoutMs));
                    if (done != shutdown)
                        _log?.Error(Source, $"Runner shutdown for leaving the match did not finish in {ShutdownTimeoutMs} ms. " +
                            "Loading the menu anyway - the next session may refuse to start; relaunch the app if so.");
                    else
                        await shutdown; // observe a fault from the shutdown itself
                }
                catch (System.Exception e)
                {
                    _log?.Error(Source, $"Shutting the runner down for leaving the match failed: {e}. " +
                        "Loading the menu anyway - but the next session may refuse to start; relaunch the app if so.");
                }
            }

            _log?.Info(Source, $"Leaving the match → loading '{_bootstrapSceneName}'.");
            SceneManager.LoadScene(_bootstrapSceneName);
        }


        // ========================================================================
        //  WAITING ROOM  (multiplayer; round 2, finding 2: THE COOP's lineup)
        // ========================================================================

        // The menu Coop's status colours (MenuUiController.UpdateLobbyStatus).


        // ========================================================================
        //  SESSION END
        // ========================================================================
        private void TickEventBanner()
        {
            if (_eventBanner == null) return;
            bool show = EventBannerFeed.Visible && !_shutdownReason.HasValue;
            if (show)
            {
                if (_eventBannerHeader != null && _eventBannerHeader.text != EventBannerFeed.Header) _eventBannerHeader.text = EventBannerFeed.Header;
                if (_eventBannerText != null && _eventBannerText.text != EventBannerFeed.Text) _eventBannerText.text = EventBannerFeed.Text;
            }
            UiElements.SetShown(_eventBanner, show);
        }

        private void TickSessionEnd()
        {
            UiElements.SetShown(_sessionEndOverlay, true);
            UiElements.SetShown(_matchEndOverlay, false);
            UiElements.SetShown(_lobbyOverlay, false);
            UiElements.SetShown(_introOverlay, false);
            UiElements.SetShown(_hostLeftNotice, false);   // the session-end screen owns a host that left with the session
            DisposeStage();

            float elapsed   = Time.unscaledTime - _shutdownAtUnscaledTime;
            float remaining = Mathf.Max(0f, _disconnectReturnDelay - elapsed);
            // In-voice lines only (finding 12): never the raw ShutdownReason name.
            if (_sessionEndTitle != null)
                _sessionEndTitle.text = UiText.Get(SessionEndCopy.HeadlineKey(_roundEndedAtShutdown));
            if (_sessionEndReason != null && _shutdownReason.HasValue)
                _sessionEndReason.text = SessionEndCopy.For(_exitKind, _shutdownReason.Value);
            if (_sessionEndCountdown != null)
                _sessionEndCountdown.text = UiText.Format(UiKeys.SessionReturning, ("n", Mathf.CeilToInt(remaining)));

            if (!_returnTriggered && (remaining <= 0f || _sessionEndLeaveNow))
            {
                _returnTriggered = true;
                _log?.Info(Source, $"Loading '{_bootstrapSceneName}' after disconnect.");
                SceneManager.LoadScene(_bootstrapSceneName);
            }
        }

        // ========================================================================
        //  INTRO COUNTDOWN
        // ========================================================================


        // ---- Dev auto-test hooks (CluckWars.Diagnostics.AutoTestDriver; dev-tools builds only) ------------------
        // The same handlers the waiting room's START and the podium's PLAY AGAIN buttons call.

        /// <summary>Waiting room START (host).</summary>
        public void AutoTestPressStart()
        {
            if (!DevTools.Available) { _log?.Warn(Source, "Auto-test START refused: dev tools are not available."); return; }
            OnLobbyStart();
        }

        /// <summary>Podium PLAY AGAIN (host), only while the button itself would be enabled.</summary>
        public void AutoTestPressPlayAgain()
        {
            if (!DevTools.Available) { _log?.Warn(Source, "Auto-test PLAY AGAIN refused: dev tools are not available."); return; }
            var gm = GameManager.Instance;
            if (gm == null || !gm.CanRequestPlayAgain || _leavingToLobby) return;
            OnPlayAgain();
        }


        /// <summary>
        /// "Waiting for {name}…" under GET READY (finding 13): the state authority names the first player it
        /// still waits for after <see cref="IntroArmGate.WaitingNoticeSeconds"/> (<see cref="GameManager.WaitingFor"/>);
        /// collapsed otherwise. Named the way every overlay names a chicken (<see cref="MatchStandings"/>);
        /// a player whose chicken is not up yet, or this peer itself, is "a slow bird".
        /// </summary>
        private void RefreshWaitingLine(GameManager gm)
        {
            if (_introWaiting == null) return;
            var waitingFor = gm != null ? gm.WaitingFor : PlayerRef.None;
            var chicken = waitingFor == PlayerRef.None ? null : ChickenForPlayer(waitingFor);
            var runner = _network != null ? _network.Runner : null;
            bool isThisPeer = (chicken != null && chicken.HasInputAuthority) || (runner != null && waitingFor == runner.LocalPlayer);
            bool show = WaitingLineRule.Shows(waitingFor != PlayerRef.None, isThisPeer);
            UiElements.SetShown(_introWaiting, show);
            if (!show) return;

            string text = chicken != null
                ? UiText.Format(UiKeys.CountdownWaitingFor,
                    ("name", MatchStandings.DisplayName(false, false, chicken.Class, chicken.HomeCornerIndex)))
                : UiText.Get(UiKeys.CountdownWaitingSomeone);
            if (_introWaiting.text != text) _introWaiting.text = text;
        }

        /// <summary>
        /// The host-left notice (finding 13): shown for <see cref="HostLeftNoticeSeconds"/> when the match manager
        /// this peer watched through GET READY or a running round goes away or changes state authority while the
        /// session itself carries on (<see cref="MatchFlowRules.HostLeftMidRound"/>). A session that ends is the
        /// session-end screen's (<see cref="TickSessionEnd"/>), so this never stacks on it.
        /// </summary>
        private void RefreshHostLeft(GameManager gm)
        {
            bool hadManager = !ReferenceEquals(_watchedManager, null);
            bool managerGone = hadManager && !ReferenceEquals(gm, _watchedManager);
            var authority = gm != null && gm.Object != null && gm.Object.IsValid ? gm.Object.StateAuthority : PlayerRef.None;
            bool authorityChanged = hadManager && !managerGone && authority != PlayerRef.None
                && _watchedAuthority != PlayerRef.None && authority != _watchedAuthority;
            bool sessionEnding = _shutdownReason.HasValue || _leavingToLobby || _network == null || !_network.IsRunning;

            if (MatchFlowRules.HostLeftMidRound(_watchedState, managerGone, authorityChanged, sessionEnding))
            {
                _hostLeftUntilUnscaledTime = Time.unscaledTime + HostLeftNoticeSeconds;
                _log?.Info(Source, $"Host left mid-round (manager {(managerGone ? "gone" : "changed authority")}, was {_watchedState}): showing the notice.");
            }

            // Keep the last known authority through a frame where the object is briefly not valid.
            if (authority != PlayerRef.None || !ReferenceEquals(gm, _watchedManager)) _watchedAuthority = authority;
            _watchedManager = gm;
            _watchedState = gm != null ? gm.State : MatchState.WaitingForPlayers;
            UiElements.SetShown(_hostLeftNotice, !sessionEnding && Time.unscaledTime < _hostLeftUntilUnscaledTime);
        }

        /// <summary>
        /// ART.md §6.5: the top bar (a separate UIDocument/controller,
        /// <see cref="MatchHudController"/>) stays visible at 40% opacity behind
        /// a full-screen overlay rather than being hidden outright. Driven from
        /// <see cref="Update"/> for the intro, match-end and lobby modals alike.
        /// </summary>
        // Explicit Unity null checks: ?. on a MonoBehaviour singleton skips the destroyed-object check.
        private static void SetTopBarDimmed(bool dimmed)
        {
            var hud = MatchHudController.Instance;
            if (hud != null) hud.SetIntroDimmed(dimmed);
        }

        /// <summary>
        /// The live top bar is HIDDEN (not dimmed) under the opaque match-end / lobby modals: dimmed,
        /// its "ENDED" + standings read through the scrim above the panel on 4:3 (Phase 1 review).
        /// </summary>
        private static void SetTopBarHidden(bool hidden)
        {
            var hud = MatchHudController.Instance;
            if (hud != null) hud.SetHiddenByModal(hidden);
        }

        // ========================================================================
        //  Data helpers
        // ========================================================================
        private static ChickenController ChickenForCorner(int corner)
        {
            var controllers = ChickenController.ActiveControllers;
            for (int i = 0; i < controllers.Count; i++)
            {
                var c = controllers[i];
                if (c == null || c.Object == null || !c.Object.IsValid || c.IsDecoy) continue;
                if (c.HomeCornerIndex == corner) return c;
            }
            return null;
        }

        /// <summary>The real player's own chicken (no bot, no decoy), or null while it has not spawned.</summary>
        private static ChickenController ChickenForPlayer(PlayerRef player)
        {
            var controllers = ChickenController.ActiveControllers;
            for (int i = 0; i < controllers.Count; i++)
            {
                var c = controllers[i];
                if (c == null || c.Object == null || !c.Object.IsValid || c.IsDecoy || c.IsBot) continue;
                if (c.Object.InputAuthority == player) return c;
            }
            return null;
        }

        private static int GetKillsForCorner(int corner)
        {
            var stats = ChickenMatchStats.ActiveStats;
            for (int i = 0; i < stats.Count; i++)
            {
                var s = stats[i];
                if (s == null || s.Object == null || !s.Object.IsValid) continue;
                var cc = s.GetComponent<ChickenController>();
                if (cc != null && cc.HomeCornerIndex == corner && !cc.IsDecoy) return s.Kills;
            }
            return 0;
        }

        private int LocalCorner()
        {
            var local = ChickenController.Local;
            return local != null ? local.HomeCornerIndex : -1;
        }

        private static string KeyOf(ChickenClass cls) => cls.ToString().ToLowerInvariant();

        private static string ClassName(ChickenClass cls) => MatchStandings.ClassName(cls);
    }
}

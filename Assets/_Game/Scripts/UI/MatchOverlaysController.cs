using System;
using System.Collections.Generic;
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
    public sealed class MatchOverlaysController : MonoBehaviour
    {
        private const string Source = "MatchOverlays";

        [SerializeField] private float _refreshInterval = 0.25f;

        [Tooltip("Seconds the 'Session ended' overlay stays up before auto-returning.")]
        [Min(0.5f)]
        [SerializeField] private float _disconnectReturnDelay = 5f;

        [Tooltip("Bootstrap scene to load after a disconnect.")]
        [SerializeField] private string _bootstrapSceneName = "Bootstrap";

        // Colorblind-safe per-player identity colors (Okabe-Ito; ART.md §6),
        // matching MatchHud so overlays and HUD read the same palette.
        private static readonly Color[] PlayerColors =
        {
            new Color(0.91f, 0.46f, 0.10f, 1f), // P1 Orange  #E8751A
            new Color(0.10f, 0.50f, 0.77f, 1f), // P2 Blue    #1A7FC4
            new Color(0.77f, 0.16f, 0.44f, 1f), // P3 Pink    #C4286F
            new Color(0.05f, 0.62f, 0.48f, 1f), // P4 Teal    #0D9E7A
        };

        // ---- Injected services -------------------------------------------------
        private INetworkService          _network;
        private ILogService              _log;
        private ColorSchemeSO            _colors;      // injected for DI parity with MatchHud
        private MatchConfigSO            _matchConfig;
        private ISessionSelectionService _selection;
        private IInputProvider           _input;        // Esc / Android back = LEAVE while the waiting room shows
        private MenuAudio                _audio = MenuAudio.Silent();
        private ChickenClassRegistrySO   _classRegistry;

        // ---- UI element refs (queried once, on bind) ---------------------------
        private VisualElement _root;
        private bool          _bound;

        private VisualElement _matchEndOverlay, _lobbyOverlay, _sessionEndOverlay, _introOverlay;

        private Label         _meRibbon, _meWinSub, _meTargetNote, _meHostNote;
        private Button        _mePlayAgainBtn, _meBackBtn;
        private VisualElement _meSafe, _meCrown, _meWinGlow, _meRows;
        // Feathers + sparkles under the podium column (Phase 3B); null if the UXML has no #MeCelebration.
        private MatchCelebration _celebration;
        // Local presentation of the networked intro timer (Phase 3B): tick / GO cues + a slam on the numeral.
        private MenuJuice _introJuice;
        private readonly IntroCueTracker _introCues = new();

        /// <summary>One podium step (#MePod0..2, left to right = 2nd, 1st, 3rd).</summary>
        private sealed class PodView
        {
            public VisualElement Root, Ring, Chicken, Plate;
            public Label Name, Score, Place;
            public string ChickenCss;   // .cw-chicken--<class> currently on Chicken (for swap)
        }
        private readonly PodView[] _pods = new PodView[3];

        // Live 3D podium (Performance Mode OFF): the menus' MenuChickenStage, one slot per step.
        // Null = static hero renders (Performance Mode ON, no registry, or the stage failed once).
        private MenuChickenStage      _stage;
        private bool                  _stageFailed;
        private static readonly Color StageClear = new Color(0.84f, 0.67f, 0.43f, 1f); // warm barn tone behind the podium

        private SafeAreaPadding _safeArea;

        private VisualElement _lobbySafe, _codeTiles, _lobbyGrid, _lobbyStatusDot, _lobbyInviteCard;
        private Label         _lobbyStatusCount, _lobbyStatusText, _lobbyHint, _lobbySettingTime, _lobbySettingGoal;
        private Button        _lobbyStartBtn, _lobbyShareBtn, _lobbyCopyBtn, _lobbyLeaveBtn, _lobbyChangeBirdBtn;

        /// <summary>One waiting-room seat (THE COOP's .cw-seat), one per spawn corner, built once on bind.</summary>
        private sealed class SeatView
        {
            public VisualElement Root, Pedestal, Chicken, Plate, NameRow, State;
            public Label Name, Pn, Tag, ClassLine, Waiting, StateLabel, PedestalTag;
            public string ChickenCss;
        }
        private readonly SeatView[] _seats = new SeatView[4];
        private readonly bool[]     _seatFilled = new bool[4];

        private Label         _sessionEndTitle, _sessionEndReason, _sessionEndCountdown;
        private Label         _introNumber;
        // GET READY inside the intro overlay (finding 11) + its "Waiting for {name}" line (finding 13).
        private VisualElement _introGetReady;
        private Label         _introWaiting;
        private VisualElement _hostLeftNotice;

        // ---- Runtime state -----------------------------------------------------
        private float           _nextRefresh;
        private bool            _matchEndPopulated;
        private ShutdownReason? _shutdownReason;
        private float           _shutdownAtUnscaledTime;
        private bool            _returnTriggered;
        // The round this peer watched had ended (its result was on screen) when the session closed: the
        // session-end headline says MATCH OVER instead of COOP CLOSED (SessionEndCopy.HeadlineKey).
        private bool            _roundEndedAtShutdown;
        // True once BACK TO LOBBY / LEAVE / CHANGE BIRD has started leaving on purpose; suppresses the
        // "Session ended" overlay that the runner shutdown would otherwise raise through HandleShutdown.
        private bool            _leavingToLobby;
        // MenuUiController.UpdateLayout's .layout--short threshold (panel height), reused for the waiting room.
        private const float     ShortLayoutHeight = 1100f;
        private const int       ShutdownTimeoutMs = 5000;

        // Intro GO flourish — lingers briefly after the countdown hits zero.
        private float       _goExpiresAtUnscaledTime;

        // Pre-intro GET READY (IntroOverlayRule): whether a GameManager has ever been seen, since when
        // this overlay has been waiting for the first one, and whether the no-manager timeout was reported.
        private bool        _sawGameManager;
        private float       _waitingForManagerSince = -1f;
        private bool        _reportedNoManager;

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
            [InjectOptional] IInputProvider         input)
        {
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
            if (_network == null)
            {
                // INetworkService is bound in GameInstaller (scene scope), so inject
                // from the SceneContext first and fall back to ProjectContext
                // (CONVENTIONS IP-fix1).
                var sceneCtx = FindFirstObjectByType<SceneContext>();
                if (sceneCtx != null) sceneCtx.Container.Inject(this);
                else                  ProjectContext.Instance.Container.Inject(this);
            }

            if (_network != null) _network.OnShutdown += HandleShutdown;
            if (_input == null)
                _log?.Warn(Source, "IInputProvider not injected; Esc / Android back will not leave the waiting room (LEAVE still works).");
            EnsureEventSystem();
        }

        private void OnDestroy()
        {
            if (_network != null) _network.OnShutdown -= HandleShutdown;
            // The stage owns cameras, lights, models and RenderTextures in this scene plus render-
            // pipeline callbacks that would outlive it: always hand them back when the scene goes.
            DisposeStage();
        }

        /// <summary>
        /// UI Toolkit runtime panels need an EventSystem (+ Input System UI module)
        /// for the Lobby's Start/Copy buttons. MatchHud ensures one too; both guard
        /// on "none exists" so only a single EventSystem is ever created.
        /// </summary>
        private static void EnsureEventSystem()
        {
            if (FindFirstObjectByType<UnityEngine.EventSystems.EventSystem>() != null) return;
            new GameObject("EventSystem",
                typeof(UnityEngine.EventSystems.EventSystem),
                typeof(UnityEngine.InputSystem.UI.InputSystemUIInputModule));
        }

        private void HandleShutdown(ShutdownReason reason)
        {
            if (_leavingToLobby) return; // our own BACK TO LOBBY shutdown, not a dropped session
            _shutdownReason         = reason;
            _shutdownAtUnscaledTime = Time.unscaledTime;
            var gm = GameManager.Instance;
            _roundEndedAtShutdown   = (gm != null ? gm.State : _watchedState) == MatchState.Ended;
            _log?.Warn(Source, $"Network shutdown: {reason} (round ended: {_roundEndedAtShutdown}). " +
                $"Returning to '{_bootstrapSceneName}' in {_disconnectReturnDelay}s.");
        }

        private void Update()
        {
            EnsureBound();
            if (!_bound) return;
            _safeArea.Apply();

            if (_shutdownReason.HasValue)
            {
                TickSessionEnd();
                return;
            }

            bool poll = Time.unscaledTime >= _nextRefresh;
            if (poll) _nextRefresh = Time.unscaledTime + _refreshInterval;

            var gm = GameManager.Instance;
            RefreshMatchEnd(gm);
            RefreshLobby(gm, poll);
            RefreshIntro(gm);
            RefreshHostLeft(gm);

            // The top bar recedes behind ANY full-screen modal, not just the intro.
            // Previously only RefreshIntro drove this, so during MATCH END the live
            // leaderboard sat at full brightness on top of the dimmed arena, directly
            // competing with the FINAL STANDINGS panel showing the same four scores.
            SetTopBarDimmed(IsShown(_introOverlay));
            SetTopBarHidden(IsShown(_matchEndOverlay) || IsShown(_lobbyOverlay));
        }

        private void LateUpdate()
        {
            if (_stage == null) return;
            try { _stage.Tick(); }
            catch (Exception e) { FailStage("the podium stage could not render", e); }
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

            _matchEndOverlay   = _root.Q<VisualElement>("MatchEndOverlay");
            _lobbyOverlay      = _root.Q<VisualElement>("LobbyOverlay");
            _sessionEndOverlay = _root.Q<VisualElement>("SessionEndOverlay");
            _introOverlay      = _root.Q<VisualElement>("IntroOverlay");

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
            for (int i = 0; i < _pods.Length; i++)
            {
                _pods[i] = new PodView
                {
                    Root    = _root.Q<VisualElement>($"MePod{i}"),
                    Ring    = _root.Q<VisualElement>($"MePod{i}Ring"),
                    Chicken = _root.Q<VisualElement>($"MePod{i}Chicken"),
                    Plate   = _root.Q<VisualElement>($"MePod{i}Plate"),
                    Name    = _root.Q<Label>($"MePod{i}Name"),
                    Score   = _root.Q<Label>($"MePod{i}Score"),
                    Place   = _root.Q<Label>($"MePod{i}Place"),
                };
                if (_pods[i].Root == null || _pods[i].Chicken == null || _pods[i].Name == null)
                    _log?.Error(Source, $"MatchOverlays.uxml is missing podium step #MePod{i} (or its chicken / name); the podium will be incomplete.");
            }
            _pods[1].Chicken?.RegisterCallback<GeometryChangedEvent>(PlaceCrown);
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
            _introNumber         = _root.Q<Label>("IntroNumber");
            _introGetReady       = _root.Q<VisualElement>("IntroGetReady");
            _introWaiting        = _root.Q<Label>("IntroWaiting");
            _hostLeftNotice      = _root.Q<VisualElement>("HostLeftNotice");
            if (_introGetReady == null) _log?.Error(Source, "MatchOverlays.uxml has no #IntroGetReady; GET READY will not show before the countdown.");
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

            // One-time: every @key text in the overlays comes from the wording dictionary.
            UiText.SetLogger(_log);
            UiText.ResolveTree(_root);

            // Same safe-area rule as the menu: content clear of notches / the gesture bar while the
            // backgrounds stay full-bleed (#MeSafe / #LobbySafe sit inside their barn backdrops; the
            // session-end overlay pads itself, its dim fill still reaching the edges).
            if (_meSafe == null) _log?.Error(Source, "MatchOverlays.uxml has no #MeSafe; the post-match screen ignores the safe area.");
            if (_lobbySafe == null) _log?.Error(Source, "MatchOverlays.uxml has no #LobbySafe; the waiting room ignores the safe area.");
            _safeArea = new SafeAreaPadding(_meSafe, _lobbySafe, _sessionEndOverlay);
            _root.RegisterCallback<GeometryChangedEvent>(evt =>
            {
                _safeArea.Apply(force: true);
                // THE COOP's short-panel tightening (MenuUiController.UpdateLayout), for the waiting room.
                _lobbyOverlay?.EnableInClassList("layout--short", evt.newRect.height < ShortLayoutHeight);
            });

            // Start hidden; state polls flip them on.
            SetShown(_matchEndOverlay, false);
            SetShown(_lobbyOverlay, false);
            SetShown(_sessionEndOverlay, false);
            SetShown(_introOverlay, false);
            SetShown(_introGetReady, false);
            SetShown(_introWaiting, false);
            SetShown(_hostLeftNotice, false);

            _bound = true;
            _log?.Debug(Source, "Overlays bound.");
        }

        private static void SetShown(VisualElement ve, bool shown)
        {
            if (ve == null) return;
            var target = shown ? DisplayStyle.Flex : DisplayStyle.None;
            if (ve.style.display != target) ve.style.display = target;
        }

        // ========================================================================
        //  MATCH END
        // ========================================================================
        private void RefreshMatchEnd(GameManager gm)
        {
            bool show = gm != null && gm.State == MatchState.Ended;
            SetShown(_matchEndOverlay, show);
            if (!show)
            {
                if (_matchEndPopulated) DisposeStage();
                _matchEndPopulated = false;
                _celebration?.Stop();
                return;
            }

            // Scores freeze once the match ends, so build the podium + rows once on entry; only
            // the action buttons track live state (authority can migrate with the host).
            if (!_matchEndPopulated)
            {
                PopulateMatchEnd(gm);
                _matchEndPopulated = true;
            }

            if (_leavingToLobby) return; // buttons stay disabled while the session shuts down
            bool canPlayAgain = gm.CanRequestPlayAgain;
            if (_mePlayAgainBtn != null) _mePlayAgainBtn.SetEnabled(canPlayAgain);
            SetShown(_meHostNote, !canPlayAgain);
        }

        /// <summary>
        /// PLAY AGAIN: host / solo only. Solo goes straight to GET READY, multiplayer back to the waiting room
        /// (<see cref="MatchFlowRules.StateAfterPlayAgain"/>); never straight into a running match.
        /// </summary>
        private void OnPlayAgain()
        {
            var gm = GameManager.Instance;
            if (gm == null) return;
            _audio.Tap();
            _log?.Info(Source, "Match end: PLAY AGAIN → GameManager.RequestPlayAgain().");
            gm.RequestPlayAgain();
        }

        /// <summary>
        /// BACK TO LOBBY (any player): leave the session through <see cref="INetworkService"/>, then load
        /// Bootstrap, which opens on THE COOP via <see cref="ISessionSelectionService.OpenLobbyOnMenuLoad"/>
        /// (a joiner lands on the main menu - decided on the menu side from the stored mode).
        /// </summary>
        private void OnBackToLobby()
        {
            if (_leavingToLobby) return; // a double tap would otherwise start two scene loads
            _audio.Tap();
            LeaveSession(changeBird: false, "BACK TO LOBBY");
        }

        /// <summary>Waiting room LEAVE (and Esc / Android back): the same exit as BACK TO LOBBY, with the back cue.</summary>
        private void OnLobbyLeave()
        {
            if (_leavingToLobby) return;
            _audio.Back();
            LeaveSession(changeBird: false, "LEAVE");
        }

        /// <summary>Waiting room CHANGE BIRD: leave the session and open the menu on PICK YOUR BIRD.</summary>
        private void OnLobbyChangeBird()
        {
            if (_leavingToLobby) return;
            _audio.Tap();
            LeaveSession(changeBird: true, "CHANGE BIRD");
        }

        /// <summary>
        /// Every deliberate exit from the match scene: disables the exits, sets the menu's one-shot landing
        /// flag (THE COOP / main menu by mode, or PICK YOUR BIRD for CHANGE BIRD), shuts the runner down and
        /// loads Bootstrap.
        /// </summary>
        private void LeaveSession(bool changeBird, string why)
        {
            _leavingToLobby = true;
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

        private void PopulateMatchEnd(GameManager gm)
        {
            var ranked = MatchStandings.Ranked(CollectStandings(), gm.WinnerCorner);
            int winnerIdx = ranked.FindIndex(e => e.Corner == gm.WinnerCorner);
            bool hasWinner = gm.WinnerCorner >= 0 && winnerIdx >= 0;
            if (gm.WinnerCorner >= 0 && winnerIdx < 0)
                _log?.Error(Source, $"Winner corner {gm.WinnerCorner} has no base in the standings; showing MATCH ENDED without a winner.");
            var winner = hasWinner ? ranked[winnerIdx] : default;

            // Banner: YOU WIN! / {NAME} WINS! / MATCH ENDED, then the winner's class under it.
            if (_meRibbon != null) _meRibbon.text = MatchStandings.WinBanner(hasWinner, winner);
            if (_meWinSub != null)
                _meWinSub.text = hasWinner
                    ? UiText.Format(UiKeys.PostmatchWinSub, ("cls", ClassName(winner.Class)))
                    : UiText.Get(UiKeys.PostmatchNoWinner);
            if (_meCrown != null) _meCrown.style.display = hasWinner ? DisplayStyle.Flex : DisplayStyle.None;
            if (_meWinGlow != null) _meWinGlow.style.display = hasWinner ? DisplayStyle.Flex : DisplayStyle.None;
            if (hasWinner) _celebration?.Start();   // no-op under Reduced Motion

            // GOAL 40 · REACHED / TIME'S UP. The goal comes from MatchConfig only; without it the note
            // is hidden rather than showing a made-up number.
            if (_meTargetNote != null)
            {
                if (_matchConfig == null)
                {
                    _log?.Error(Source, "No MatchConfigSO injected: the post-match goal note is hidden.");
                    _meTargetNote.style.display = DisplayStyle.None;
                }
                else
                {
                    int target = Mathf.Max(1, _matchConfig.FoodTargetToWin);
                    bool reached = hasWinner && MatchStandings.GoalReached(winner.Total, target);
                    _meTargetNote.style.display = DisplayStyle.Flex;
                    _meTargetNote.text = UiText.Format(
                        reached ? UiKeys.PostmatchTargetReached : UiKeys.PostmatchTargetTimeout, ("n", target));
                }
            }

            PopulatePodium(ranked, hasWinner);

            // Standings rows.
            if (_meRows != null)
            {
                _meRows.Clear();
                _meRows.EnableInClassList("cw-me-rows--no-ko", !MatchStandings.ShowKoColumn(ranked));
                // Bars are the share of the goal, like the HUD (a share of the leader drew a near-full bar
                // for 0.6 food when nobody scored); without a MatchConfig they fall back to the leader.
                float maxTotal = _matchConfig != null
                    ? Mathf.Max(1f, _matchConfig.FoodTargetToWin)
                    : (ranked.Count > 0 ? Mathf.Max(1f, ranked[0].Total) : 1f);
                for (int rank = 0; rank < ranked.Count; rank++)
                    _meRows.Add(BuildMeRow(rank, ranked[rank], maxTotal, isWinner: hasWinner && ranked[rank].Corner == winner.Corner));
            }

            _log?.Info(Source, $"Match end: {(hasWinner ? $"winner corner {winner.Corner} ({MatchStandings.DisplayName(winner)})" : "no winner")}, " +
                $"{ranked.Count} in the standings, podium {(_stage != null ? "live" : "static")}.");
        }

        /// <summary>Fills the three podium steps (2nd, 1st, 3rd) and binds the live stage if it is on.</summary>
        private void PopulatePodium(List<MatchStandings.Entry> ranked, bool hasWinner)
        {
            if (MenuStagePolicy.WantsLive(PlayerPreferences.PerformanceModeEnabled, _stageFailed)) TryCreateStage();

            for (int slot = 0; slot < _pods.Length; slot++)
            {
                var v = _pods[slot];
                if (v?.Root == null) continue;
                int rank = MatchStandings.PodiumRankBySlot[slot];
                bool filled = rank < ranked.Count;
                // visibility (not display) keeps the empty step's width, so 1st stays in the centre.
                v.Root.EnableInClassList("cw-me-pod--hidden", !filled);
                if (!filled) { ShowPodChicken(slot, v, null, false); continue; }

                var e = ranked[rank];
                Color color = ColorForCorner(e.Corner);
                if (v.Ring != null) v.Ring.style.unityBackgroundImageTintColor = color;
                if (v.Plate != null) v.Plate.style.borderTopColor = color;
                if (v.Name != null) v.Name.text = MatchStandings.DisplayName(e);
                if (v.Score != null) v.Score.text = Mathf.FloorToInt(e.Total).ToString();
                if (v.Place != null) v.Place.text = (rank + 1).ToString();
                ShowPodChicken(slot, v, e.HasChicken ? e.Class : (ChickenClass?)null, cheer: hasWinner && rank == 0);
            }
        }

        /// <summary>Static render class on the element, plus the live model over it when the stage is on.</summary>
        private void ShowPodChicken(int slot, PodView v, ChickenClass? cls, bool cheer)
        {
            if (v.Chicken == null) return;
            if (!string.IsNullOrEmpty(v.ChickenCss)) v.Chicken.RemoveFromClassList(v.ChickenCss);
            v.ChickenCss = cls.HasValue ? "cw-chicken--" + KeyOf(cls.Value) : null;
            if (v.ChickenCss != null) v.Chicken.AddToClassList(v.ChickenCss);

            if (_stage == null) return;
            try
            {
                if (cls.HasValue) _stage.Show(slot, v.Chicken, cls.Value, hop: cheer, sway: true, clear: StageClear);
                else _stage.Clear(slot);
            }
            catch (Exception e) { FailStage("a podium chicken could not be staged", e); }
        }

        private void TryCreateStage()
        {
            if (_stage != null) return;
            if (_classRegistry == null)
            {
                FailStage("ChickenClassRegistrySO not injected", null);
                return;
            }
            try
            {
                _stage = new MenuChickenStage(_classRegistry, _log, _pods.Length, PanelPixelsPerPoint);
            }
            catch (Exception e)
            {
                FailStage("the stage could not be created", e);
            }
        }

        /// <summary>Stage failure: log once, keep the static renders for the rest of this scene.</summary>
        private void FailStage(string reason, Exception e)
        {
            _stageFailed = true;
            _log?.Warn(Source, $"Live podium unavailable ({reason}{(e != null ? ": " + e.Message : "")}); showing the static renders.");
            DisposeStage();
        }

        private void DisposeStage()
        {
            if (_stage == null) return;
            _stage.Dispose();
            _stage = null;
        }

        /// <summary>Screen pixels per panel point (sizes the stage textures), as in the menu.</summary>
        private float PanelPixelsPerPoint()
        {
            float rootW = _root?.layout.width ?? 0f;
            if (float.IsNaN(rootW) || rootW <= 0f) return 1f;
            var ps = GetComponent<UIDocument>().panelSettings;
            float targetW = ps != null && ps.targetTexture != null ? ps.targetTexture.width : Screen.width;
            return targetW / rootW;
        }

        /// <summary>
        /// Puts the crown on the winner's head: the render is drawn as a bottom-aligned square
        /// (contain), so the head sits near the top of that square, not of the element.
        /// </summary>
        private void PlaceCrown(GeometryChangedEvent evt)
        {
            if (_meCrown == null) return;
            float w = evt.newRect.width, h = evt.newRect.height;
            if (float.IsNaN(w) || float.IsNaN(h) || w <= 0f || h <= 0f) return;
            float side = Mathf.Min(w, h);
            float crownH = _meCrown.resolvedStyle.height;
            if (float.IsNaN(crownH)) crownH = 112f;
            _meCrown.style.top = (h - side) + side * CrownHeadInset - crownH * 0.62f;
        }
        // Top of the head in the 1024 px cheer renders / live framing, as a fraction of the square.
        private const float CrownHeadInset = 0.06f;

        private VisualElement BuildMeRow(int rank, in MatchStandings.Entry e, float maxTotal, bool isWinner)
        {
            Color color = ColorForCorner(e.Corner);

            var row = new VisualElement();
            row.AddToClassList("cw-me-row");
            row.EnableInClassList("cw-me-row--winner", isWinner);

            // Placement medal (gold / silver / bronze, plain cream after the podium) with its number.
            var medal = new VisualElement();
            medal.AddToClassList("cw-me-medal");
            if (rank < 3) medal.AddToClassList($"cw-me-medal--{rank + 1}");
            var place = new Label((rank + 1).ToString());
            place.AddToClassList("cw-me-medal__num");
            medal.Add(place);
            row.Add(medal);

            // Player identity dot: the only player colour in the text area.
            var dot = new VisualElement();
            dot.AddToClassList("cw-me-dot");
            dot.style.unityBackgroundImageTintColor = color;
            row.Add(dot);

            // Name (+ CPU tag) over the class, ink and brown on cream.
            var mid = new VisualElement();
            mid.AddToClassList("cw-me-rowmid");
            var name = new Label(MatchStandings.DisplayName(e));
            name.AddToClassList("cw-me-rowname");
            mid.Add(name);
            if (e.HasChicken)
            {
                var classRow = new VisualElement();
                classRow.AddToClassList("cw-me-rowclassrow");
                var cls = new Label(ClassName(e.Class));
                cls.AddToClassList("cw-me-rowpn");
                classRow.Add(cls);
                if (e.IsBot)
                {
                    var tag = new Label(UiText.Get(UiKeys.TagCpu));
                    tag.AddToClassList("cw-me-rowtag");
                    classRow.Add(tag);
                }
                mid.Add(classRow);
            }
            row.Add(mid);

            // Progress bar (share of the leader's total).
            var trough = new VisualElement();
            trough.AddToClassList("cw-me-bartrough");
            var fill = new VisualElement();
            fill.AddToClassList("cw-me-barfill");
            fill.style.width = Length.Percent(Mathf.Clamp01(e.Total / maxTotal) * 100f);
            fill.style.unityBackgroundImageTintColor = color;
            trough.Add(fill);
            row.Add(trough);

            // Food score.
            var food = new VisualElement();
            food.AddToClassList("cw-me-rowfood");
            var foodIcon = new VisualElement();
            foodIcon.AddToClassList("cw-me-rowfoodicon");
            var score = new Label(Mathf.FloorToInt(e.Total).ToString());
            score.AddToClassList("cw-me-rowscore");
            food.Add(foodIcon); food.Add(score);
            row.Add(food);

            // Knockouts ("{n} KO"); the whole column hides via .cw-me-rows--no-ko while nobody has one.
            var stats = new Label(UiText.Format(UiKeys.PostmatchKos, ("n", e.Kills)));
            stats.AddToClassList("cw-me-rowstats");
            row.Add(stats);

            return row;
        }

        /// <summary>
        /// One entry per base that a chicken stands on or that banked food (a player who left keeps
        /// their score; an unclaimed corner in a 2-3 player match does not get a row of zeros).
        /// </summary>
        private static List<MatchStandings.Entry> CollectStandings()
        {
            var bases = PlayerBase.ActiveBases;
            var list  = new List<MatchStandings.Entry>(bases.Count);
            for (int i = 0; i < bases.Count; i++)
            {
                var b = bases[i];
                if (b == null) continue;
                var c = ChickenForCorner(b.CornerIndex);
                if (c == null && b.FoodTotal <= 0f) continue;
                list.Add(c != null
                    ? new MatchStandings.Entry(b.CornerIndex, b.FoodTotal, GetKillsForCorner(b.CornerIndex),
                        c.HasInputAuthority, c.IsBot, c.Class)
                    : new MatchStandings.Entry(b.CornerIndex, b.FoodTotal, 0, false, false, ChickenClass.Warrior, hasChicken: false));
            }
            return list;
        }

        // ========================================================================
        //  WAITING ROOM  (multiplayer; round 2, finding 2: THE COOP's lineup)
        // ========================================================================
        private void RefreshLobby(GameManager gm, bool poll)
        {
            bool show = gm != null && gm.State == MatchState.WaitingForPlayers;
            // Fill on the frame it opens, not on the next poll: its TIME / GOAL / status labels are
            // authored blank (bound from MatchConfig here), so a late fill would flash empty rows.
            bool opening = show && !IsShown(_lobbyOverlay);
            SetShown(_lobbyOverlay, show);
            if (!show) return;

            // Esc / Android back = LEAVE, polled only while the room is up (the match's own Esc use, the
            // ability cancel, is idle here: nothing moves in the waiting room).
            if (!_leavingToLobby && _input != null && _input.GetBackPressed()) { OnLobbyLeave(); return; }
            if (!(poll || opening)) return;

            var runner = _network?.Runner;
            bool solo = runner != null && runner.GameMode == GameMode.Single;
            bool isHost = runner != null && (solo || runner.IsSharedModeMasterClient);
            int maxPlayers = _matchConfig != null ? _matchConfig.MaxPlayers : 4;

            // Invite code: only a hosted session has anyone to invite, so never in solo.
            bool showInvite = isHost && !solo;
            SetShown(_lobbyInviteCard, showInvite);
            if (showInvite) SetCodeTiles(_selection?.SessionName);

            // House rules.
            if (_lobbySettingTime != null && _matchConfig != null)
                _lobbySettingTime.text = MatchSettingsText.Time(_matchConfig.MatchDurationSeconds);
            if (_lobbySettingGoal != null && _matchConfig != null)
                _lobbySettingGoal.text = MatchSettingsText.Goal(_matchConfig.FoodTargetToWin);

            var master = gm.Object != null && gm.Object.IsValid ? gm.Object.StateAuthority : PlayerRef.None;
            RefreshSeats(Mathf.Clamp(maxPlayers, 1, _seats.Length), solo, master);

            // Status pill: READY seats over the room size (the menu Coop's count and colours).
            int ready = WaitingRoomRules.ReadyCount(_seatFilled);
            bool full = ready >= Mathf.Clamp(maxPlayers, 1, _seats.Length);
            if (_lobbyStatusCount != null)
                _lobbyStatusCount.text = UiText.Format(UiKeys.LobbyCount, ("n", ready), ("max", maxPlayers));
            if (_lobbyStatusText != null)
            {
                _lobbyStatusText.text = UiText.Get(solo ? UiKeys.LobbyStatusSolo : full ? UiKeys.LobbyAllReady : UiKeys.LobbyStatusWaiting);
                // Dark green / dark amber clear 4.5:1 on the cream inset; the dot is a lamp, not text.
                _lobbyStatusText.style.color = full ? StatusInkReady : StatusInkWaiting;
            }
            if (_lobbyStatusDot != null) _lobbyStatusDot.style.backgroundColor = full ? StatusLampReady : StatusLampWaiting;

            // START (host only) + the host / guest line.
            SetShown(_lobbyStartBtn, isHost);
            if (_lobbyHint != null)
                _lobbyHint.text = UiText.Get(solo ? UiKeys.LobbyHintSolo : isHost ? UiKeys.LobbyHintHost : UiKeys.LobbyHintGuest);
        }

        // The menu Coop's status colours (MenuUiController.UpdateLobbyStatus).
        private static readonly Color StatusInkReady    = new Color32(0x1e, 0x6a, 0x1e, 0xff);
        private static readonly Color StatusInkWaiting  = new Color32(0x6e, 0x48, 0x00, 0xff);
        private static readonly Color StatusLampReady   = new Color32(0x4a, 0xe6, 0x6a, 0xff);
        private static readonly Color StatusLampWaiting = new Color32(0xf5, 0xc8, 0x42, 0xff);

        /// <summary>Builds the four seats once (THE COOP's .cw-seat: pedestal + chicken, nameplate, READY badge).</summary>
        private void BuildSeats()
        {
            _lobbyGrid.Clear();
            for (int corner = 0; corner < _seats.Length; corner++)
            {
                var color = ColorForCorner(corner);
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
                v.PedestalTag = new Label(UiText.Format(UiKeys.LobbyPlayerTag, ("n", corner + 1))) { pickingMode = PickingMode.Ignore };
                v.PedestalTag.AddToClassList("cw-seat__pedestal-tag");
                v.PedestalTag.style.backgroundColor = color;
                v.PedestalTag.style.color = AbilityPalette.InkOn(color);
                stage.Add(v.Pedestal); stage.Add(v.Chicken); stage.Add(v.PedestalTag);
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
                v.Pn = new Label(UiText.Format(UiKeys.LobbyPlayerTag, ("n", corner + 1)));
                v.Pn.AddToClassList("cw-seat__pn");
                v.Pn.style.borderLeftColor = color;   // player colour as the tag's stripe; text stays cream-on-ink
                v.Tag = new Label();
                v.Tag.AddToClassList("cw-seat__tag");
                v.NameRow.Add(v.Name); v.NameRow.Add(v.Pn); v.NameRow.Add(v.Tag);
                v.Plate.Add(v.NameRow);

                v.ClassLine = new Label();
                v.ClassLine.AddToClassList("cw-seat__class");
                v.Plate.Add(v.ClassLine);

                v.Waiting = new Label(UiText.Format(UiKeys.LobbyWaitingFor, ("n", corner + 1)));
                v.Waiting.AddToClassList("cw-seat__waiting");
                v.Plate.Add(v.Waiting);

                // READY: gold rosette badge, ink label (~10.6:1; was cream on green, 3.0:1).
                v.State = new VisualElement();
                v.State.AddToClassList("cw-seat__state");
                var rosette = new VisualElement { pickingMode = PickingMode.Ignore };
                rosette.AddToClassList("cw-seat__rosette");
                v.State.Add(rosette);
                v.StateLabel = new Label(UiText.Get(UiKeys.StateReady));
                v.StateLabel.AddToClassList("cw-seat__state-label");
                v.State.Add(v.StateLabel);
                v.Plate.Add(v.State);

                v.Root.Add(v.Plate);
                _seats[corner] = v;
                _lobbyGrid.Add(v.Root);
            }
        }

        /// <summary>
        /// Fills each corner's seat from the chicken standing on it: "You" / CPU name / "P{n}", HOST on the
        /// master's seat (never in solo), CPU on a bot, "{CLASS} · {role}" like the menu Coop, READY. A corner
        /// with no chicken is an open seat ("WAITING FOR P{n}"); seats past the room size are hidden.
        /// </summary>
        private void RefreshSeats(int seatCount, bool solo, PlayerRef master)
        {
            int localCorner = LocalCorner();
            for (int corner = 0; corner < _seats.Length; corner++)
            {
                var v = _seats[corner];
                if (v == null) continue;
                bool inRoom = corner < seatCount;
                v.Root.style.display = inRoom ? DisplayStyle.Flex : DisplayStyle.None;
                var chicken = inRoom ? ChickenForCorner(corner) : null;
                bool filled = chicken != null;
                _seatFilled[corner] = filled;

                v.Root.EnableInClassList("cw-seat--empty", !filled);
                v.Root.EnableInClassList("cw-seat--you", filled && corner == localCorner);
                string css = filled ? "cw-chicken--" + KeyOf(chicken.Class) : null;
                if (v.ChickenCss != css)
                {
                    if (v.ChickenCss != null) v.Chicken.RemoveFromClassList(v.ChickenCss);
                    if (css != null) v.Chicken.AddToClassList(css);
                    v.ChickenCss = css;
                }

                SetShown(v.NameRow, filled);
                SetShown(v.ClassLine, filled);
                SetShown(v.State, filled);
                SetShown(v.Waiting, !filled);
                if (!filled) continue;

                var cls = chicken.Class;
                v.Name.text = MatchStandings.DisplayName(corner == localCorner, chicken.IsBot, cls, corner);
                bool host = WaitingRoomRules.ShowHostTag(solo, master != PlayerRef.None && chicken.Object.InputAuthority == master, chicken.IsBot);
                v.Tag.text = host ? UiText.Get(UiKeys.TagHost) : chicken.IsBot ? UiText.Get(UiKeys.TagCpu) : string.Empty;
                SetShown(v.Tag, host || chicken.IsBot);
                v.ClassLine.text = MatchStandings.ClassRoleLine(cls);
            }
        }

        private void SetCodeTiles(string code)
        {
            if (_codeTiles == null) return;
            _codeTiles.Clear();
            if (string.IsNullOrEmpty(code)) return;
            foreach (var ch in code.ToUpperInvariant())
            {
                var t = new Label(ch.ToString());
                t.AddToClassList("cw-code-tile");
                _codeTiles.Add(t);
            }
        }

        private void OnLobbyStart()
        {
            var gm = GameManager.Instance;
            if (gm == null) return;
            _log?.Info(Source, "Lobby Start → GameManager.StartMatchNow().");
            gm.StartMatchNow();
        }

        private void CopyJoinCode()
        {
            var code = _selection?.SessionName;
            if (string.IsNullOrEmpty(code)) return;
            GUIUtility.systemCopyBuffer = code;
            _log?.Debug(Source, $"Copied join code '{code}' to clipboard.");
        }

        // ========================================================================
        //  SESSION END
        // ========================================================================
        private void TickSessionEnd()
        {
            SetShown(_sessionEndOverlay, true);
            SetShown(_matchEndOverlay, false);
            SetShown(_lobbyOverlay, false);
            SetShown(_introOverlay, false);
            SetShown(_hostLeftNotice, false);   // the session-end screen owns a host that left with the session
            DisposeStage();

            float elapsed   = Time.unscaledTime - _shutdownAtUnscaledTime;
            float remaining = Mathf.Max(0f, _disconnectReturnDelay - elapsed);
            // In-voice lines only (finding 12): never the raw ShutdownReason name.
            if (_sessionEndTitle != null)
                _sessionEndTitle.text = UiText.Get(SessionEndCopy.HeadlineKey(_roundEndedAtShutdown));
            if (_sessionEndReason != null && _shutdownReason.HasValue)
                _sessionEndReason.text = SessionEndCopy.For(_shutdownReason.Value);
            if (_sessionEndCountdown != null)
                _sessionEndCountdown.text = UiText.Format(UiKeys.SessionReturning, ("n", Mathf.CeilToInt(remaining)));

            if (!_returnTriggered && remaining <= 0f)
            {
                _returnTriggered = true;
                _log?.Info(Source, $"Loading '{_bootstrapSceneName}' after disconnect.");
                SceneManager.LoadScene(_bootstrapSceneName);
            }
        }

        // ========================================================================
        //  INTRO COUNTDOWN
        // ========================================================================
        private void RefreshIntro(GameManager gm)
        {
            // Visibility only — the top-bar dim is decided once in Update() across
            // all three modals, so this method must not touch it or it would clear
            // a dim the match-end / lobby overlay still needs.
            // Before the round's intro is armed — no GameManager yet (the session is still starting)
            // or a round in Starting (the host's world is settling) — the overlay holds GET READY with
            // no digit: the continuation of the menu's GET READY card, never a blank arena.
            if (gm != null) _sawGameManager = true;
            else if (_waitingForManagerSince < 0f) _waitingForManagerSince = Time.unscaledTime;
            float waited = _waitingForManagerSince < 0f ? 0f : Time.unscaledTime - _waitingForManagerSince;
            var hold = IntroOverlayRule.Decide(gm != null, gm != null ? gm.State : MatchState.WaitingForPlayers,
                _sawGameManager, _leavingToLobby, waited);

            if (hold == IntroOverlayRule.PreIntro.TimedOut && !_reportedNoManager)
            {
                _reportedNoManager = true;
                _log?.Error(Source, $"No GameManager after {IntroOverlayRule.NoManagerTimeoutSeconds}s in the match scene " +
                    "(session start failed or the master never spawned it): hiding GET READY. The match cannot start; " +
                    "see the MatchBootstrapper / FusionNetworkService errors above.");
            }
            bool getReady = hold == IntroOverlayRule.PreIntro.GetReady && !_shutdownReason.HasValue;
            // GET READY is only the pre-intro card (finding 11): gone on the first digit, never under GO!.
            SetShown(_introGetReady, getReady);
            RefreshWaitingLine(getReady ? gm : null);
            if (hold != IntroOverlayRule.PreIntro.None)
            {
                _introCues.Reset();
                if (_introNumber != null && _introNumber.text.Length > 0) _introNumber.text = string.Empty;
                SetShown(_introOverlay, getReady);
                return;
            }
            if (gm == null) { _introCues.Reset(); SetShown(_introOverlay, false); return; }

            // The timer is networked and untouched; the cues below are this client's own presentation of it.
            var cue = _introCues.Observe(gm.IsIntroActive, gm.IsIntroActive ? gm.IntroRemaining : 0f, out int number);

            if (gm.IsIntroActive)
            {
                if (_introNumber != null && cue == IntroCueTracker.Cue.Tick) _introNumber.text = number.ToString();
                SetShown(_introOverlay, true);
                _goExpiresAtUnscaledTime = Time.unscaledTime + IntroCueTracker.GoHoldSeconds;
                if (cue == IntroCueTracker.Cue.Tick) { _audio.CountdownTick(number); _introJuice?.Slam(_introNumber); }
                return;
            }

            if (cue == IntroCueTracker.Cue.Go)
            {
                if (_introNumber != null) _introNumber.text = UiText.Get(UiKeys.CountdownGo);
                _audio.CountdownGo();
                _introJuice?.Slam(_introNumber);
            }

            if (Time.unscaledTime < _goExpiresAtUnscaledTime)
            {
                if (_introNumber != null && cue != IntroCueTracker.Cue.Go && _introNumber.text != UiText.Get(UiKeys.CountdownGo))
                    _introNumber.text = UiText.Get(UiKeys.CountdownGo);
                SetShown(_introOverlay, true);
            }
            else
            {
                SetShown(_introOverlay, false);
            }
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
            SetShown(_introWaiting, waitingFor != PlayerRef.None);
            if (waitingFor == PlayerRef.None) return;

            var chicken = ChickenForPlayer(waitingFor);
            string text = chicken != null && !chicken.HasInputAuthority
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
            SetShown(_hostLeftNotice, !sessionEnding && Time.unscaledTime < _hostLeftUntilUnscaledTime);
        }

        /// <summary>
        /// ART.md §6.5: the top bar (a separate UIDocument/controller,
        /// <see cref="MatchHudController"/>) stays visible at 40% opacity behind
        /// a full-screen overlay rather than being hidden outright. Driven from
        /// <see cref="Update"/> for the intro, match-end and lobby modals alike.
        /// </summary>
        private static void SetTopBarDimmed(bool dimmed) => MatchHudController.Instance?.SetIntroDimmed(dimmed);

        /// <summary>
        /// The live top bar is HIDDEN (not dimmed) under the opaque match-end / lobby modals: dimmed,
        /// its "ENDED" + standings read through the scrim above the panel on 4:3 (Phase 1 review).
        /// </summary>
        private static void SetTopBarHidden(bool hidden) => MatchHudController.Instance?.SetHiddenByModal(hidden);

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
            var controllers = ChickenController.ActiveControllers;
            for (int i = 0; i < controllers.Count; i++)
            {
                var c = controllers[i];
                if (c == null || c.Object == null || !c.Object.IsValid) continue;
                if (c.HasInputAuthority) return c.HomeCornerIndex;
            }
            return -1;
        }

        private static Color ColorForCorner(int corner) =>
            corner >= 0 ? PlayerColors[corner % PlayerColors.Length] : Color.grey;

        private static string KeyOf(ChickenClass cls) => cls.ToString().ToLowerInvariant();

        private static string ClassName(ChickenClass cls) => MatchStandings.ClassName(cls);
    }
}

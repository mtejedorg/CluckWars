using System.Collections.Generic;
using CluckWars.Gameplay;
using CluckWars.Localization;
using CluckWars.Logging;
using CluckWars.Networking;
using CluckWars.Services;
using Fusion;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using Zenject;
using LogLevel = CluckWars.Logging.LogLevel;

namespace CluckWars.UI
{
    /// <summary>
    /// UI Toolkit driver for the four in-match overlays (Stage 1 UI rebuild):
    /// Match End, Lobby, Session End, Intro countdown. Layout lives in
    /// <c>Assets/UI/MatchOverlays.uxml</c>; styling in
    /// <c>Assets/UI/Styles/MatchOverlays.uss</c>. This controller only toggles each
    /// overlay root's visibility per <see cref="GameManager"/> state / network
    /// shutdown and fills the data containers (rows, code tiles, player grid),
    /// mirroring the refresh logic that used to live in <see cref="MatchHud"/>.
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
        private MenuAudio                _audio = MenuAudio.Silent();

        // ---- UI element refs (queried once, on bind) ---------------------------
        private VisualElement _root;
        private bool          _bound;

        private VisualElement _matchEndOverlay, _lobbyOverlay, _sessionEndOverlay, _introOverlay;

        private Label         _meRibbon, _meWinSub, _meWinName, _meWinScore, _meTargetNote, _meHostNote;
        private Button        _mePlayAgainBtn, _meBackBtn;
        private VisualElement _meWinChicken, _meWinGlow, _meWinRing, _meWinRosette, _meRows;
        // Feathers + sparkles over the match-end panel (Phase 3B); null if the UXML has no #MeCelebration.
        private MatchCelebration _celebration;
        // Local presentation of the networked intro timer (Phase 3B): tick / GO cues + a slam on the numeral.
        private MenuJuice _introJuice;
        private readonly IntroCueTracker _introCues = new();
        // .cw-chicken--<class> currently on the win-screen hero art (for swap).
        private string        _meWinChickenClass;

        private VisualElement _codeTiles, _lobbyGrid, _lobbyStatusDot, _lobbyInviteCard;
        private Label         _lobbyStatusCount, _lobbyStatusText, _lobbyHint, _lobbySettingTime, _lobbySettingGoal;
        private Button        _lobbyStartBtn, _lobbyShareBtn, _lobbyCopyBtn;

        private Label         _sessionEndReason, _sessionEndCountdown;
        private Label         _introNumber;

        // ---- Runtime state -----------------------------------------------------
        private float           _nextRefresh;
        private bool            _matchEndPopulated;
        private ShutdownReason? _shutdownReason;
        private float           _shutdownAtUnscaledTime;
        private bool            _returnTriggered;
        // True once BACK TO LOBBY has started leaving on purpose; suppresses the "Session ended" overlay
        // that the runner shutdown would otherwise raise through HandleShutdown.
        private bool            _leavingToLobby;
        private const int       ShutdownTimeoutMs = 5000;

        // Intro GO flourish — lingers briefly after the countdown hits zero.
        private float       _goExpiresAtUnscaledTime;
        private const float GoFlourishDuration = 0.6f;

        // ---- Injection ---------------------------------------------------------
        [Inject]
        public void Construct(
            INetworkService          network,
            ILogService              log,
            ColorSchemeSO            colors,
            MatchConfigSO            matchConfig,
            ISessionSelectionService selection,
            [InjectOptional] MenuAudio   audio)
        {
            _network     = network;
            _log         = log;
            _colors      = colors;
            _matchConfig = matchConfig;
            _selection   = selection;
            _audio       = audio ?? MenuAudio.Silent();
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
            EnsureEventSystem();
        }

        private void OnDestroy()
        {
            if (_network != null) _network.OnShutdown -= HandleShutdown;
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
            _log?.Warn(Source, $"Network shutdown: {reason}. Returning to '{_bootstrapSceneName}' in {_disconnectReturnDelay}s.");
        }

        private void Update()
        {
            EnsureBound();
            if (!_bound) return;

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

            // The top bar recedes behind ANY full-screen modal, not just the intro.
            // Previously only RefreshIntro drove this, so during MATCH END the live
            // leaderboard sat at full brightness on top of the dimmed arena, directly
            // competing with the FINAL STANDINGS panel showing the same four scores.
            SetTopBarDimmed(IsShown(_introOverlay));
            SetTopBarHidden(IsShown(_matchEndOverlay) || IsShown(_lobbyOverlay));
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

            _meRibbon     = _root.Q<Label>("MeWinRibbonLabel");
            _meWinChicken = _root.Q<VisualElement>("MeWinChicken");
            _meWinGlow    = _root.Q<VisualElement>("MeWinGlow");
            _meWinRing    = _root.Q<VisualElement>("MeWinRing");
            _meWinRosette = _root.Q<VisualElement>("MeWinRosette");
            _meWinSub     = _root.Q<Label>("MeWinSub");
            _meWinName    = _root.Q<Label>("MeWinName");
            _meWinScore   = _root.Q<Label>("MeWinScore");
            _meTargetNote = _root.Q<Label>("MeTargetNote");
            _meHostNote     = _root.Q<Label>("MeHostNote");
            _mePlayAgainBtn = _root.Q<Button>("MePlayAgainBtn");
            _meBackBtn      = _root.Q<Button>("MeBackBtn");
            _meRows       = _root.Q<VisualElement>("MeRows");
            var celebrationLayer = _root.Q<VisualElement>("MeCelebration");
            if (celebrationLayer != null) _celebration = new MatchCelebration(celebrationLayer);
            else _log?.Error(Source, "MatchOverlays.uxml has no #MeCelebration; the winner celebration will not show.");

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

            _sessionEndReason    = _root.Q<Label>("SessionEndReason");
            _sessionEndCountdown = _root.Q<Label>("SessionEndCountdown");
            _introNumber         = _root.Q<Label>("IntroNumber");
            if (_introOverlay != null) _introJuice = new MenuJuice(_introOverlay);

            if (_mePlayAgainBtn != null) _mePlayAgainBtn.clicked += OnPlayAgain;
            if (_meBackBtn      != null) _meBackBtn.clicked      += OnBackToLobby;
            if (_lobbyStartBtn != null) _lobbyStartBtn.clicked += OnLobbyStart;
            if (_lobbyCopyBtn  != null) _lobbyCopyBtn.clicked  += CopyJoinCode;
            if (_lobbyShareBtn != null) _lobbyShareBtn.clicked += CopyJoinCode;

            // One-time: every @key text in the overlays comes from the wording dictionary.
            UiText.SetLogger(_log);
            UiText.ResolveTree(_root);

            // Start hidden; state polls flip them on.
            SetShown(_matchEndOverlay, false);
            SetShown(_lobbyOverlay, false);
            SetShown(_sessionEndOverlay, false);
            SetShown(_introOverlay, false);

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
            if (!show) { _matchEndPopulated = false; _celebration?.Stop(); return; }

            // Scores freeze once the match ends, so build the hero + rows once on entry; only
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

        /// <summary>PLAY AGAIN: host / solo only. Re-arms the in-session waiting room; never starts a match.</summary>
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
            _leavingToLobby = true;
            _audio.Tap();
            if (_mePlayAgainBtn != null) _mePlayAgainBtn.SetEnabled(false);
            if (_meBackBtn      != null) _meBackBtn.SetEnabled(false);

            if (_selection != null) _selection.OpenLobbyOnMenuLoad = true;
            else _log?.Error(Source, "No ISessionSelectionService injected: BACK TO LOBBY will open the main menu instead of THE COOP.");

            // async void would swallow anything thrown after the first await; LeaveToLobbyAsync
            // catches and logs everything itself, and the continuation surfaces a fault of its own.
            LeaveToLobbyAsync().ContinueWith(
                t => _log?.Error(Source, $"BACK TO LOBBY flow faulted: {t.Exception}"),
                System.Threading.Tasks.TaskContinuationOptions.OnlyOnFaulted
                | System.Threading.Tasks.TaskContinuationOptions.ExecuteSynchronously);
        }

        private async System.Threading.Tasks.Task LeaveToLobbyAsync()
        {
            if (_network == null)
            {
                _log?.Error(Source, "No INetworkService injected: BACK TO LOBBY cannot shut the session down, loading the menu anyway.");
            }
            else
            {
                try
                {
                    var shutdown = _network.ShutdownAsync();
                    var done = await System.Threading.Tasks.Task.WhenAny(
                        shutdown, System.Threading.Tasks.Task.Delay(ShutdownTimeoutMs));
                    if (done != shutdown)
                        _log?.Error(Source, $"Runner shutdown for BACK TO LOBBY did not finish in {ShutdownTimeoutMs} ms. " +
                            "Loading the menu anyway - the next session may refuse to start; relaunch the app if so.");
                    else
                        await shutdown; // observe a fault from the shutdown itself
                }
                catch (System.Exception e)
                {
                    _log?.Error(Source, $"Shutting the runner down for BACK TO LOBBY failed: {e}. " +
                        "Loading the menu anyway - but the next session may refuse to start; relaunch the app if so.");
                }
            }

            _log?.Info(Source, $"Match end: BACK TO LOBBY → loading '{_bootstrapSceneName}'.");
            SceneManager.LoadScene(_bootstrapSceneName);
        }

        private void PopulateMatchEnd(GameManager gm)
        {
            var sorted = BuildSortedLeaderboard();
            int   winnerCorner = gm.WinnerCorner;
            bool  winnerReal    = gm.WinnerPlayer.IsRealPlayer;
            Color winnerColor   = ColorForCorner(winnerCorner);

            // Ribbon
            if (_meRibbon != null)
            {
                _meRibbon.text = winnerCorner >= 0
                    ? UiText.Format(winnerReal ? UiKeys.PostmatchWins : UiKeys.PostmatchWinsCpu, ("n", winnerCorner + 1))
                    : UiText.Get(UiKeys.PostmatchEnded);
            }

            // Winner hero art + tints
            var winnerClass = ClassForCorner(winnerCorner);
            if (_meWinChicken != null)
            {
                if (!string.IsNullOrEmpty(_meWinChickenClass))
                    _meWinChicken.RemoveFromClassList(_meWinChickenClass);
                _meWinChickenClass = "cw-chicken--" + KeyOf(winnerClass);
                _meWinChicken.AddToClassList(_meWinChickenClass);
            }
            if (_meWinGlow != null)
                _meWinGlow.style.unityBackgroundImageTintColor = Fade(winnerColor, 0.45f);
            if (_meWinRing != null) _meWinRing.style.unityBackgroundImageTintColor = winnerColor;   // Pedestal_Ring art
            if (winnerCorner >= 0) _celebration?.Start(winnerColor);   // no-op under Reduced Motion
            if (_meWinRosette != null)
            {
                // Winner rosette (Badge_Rosette, white art) in the winner's player colour.
                _meWinRosette.style.display = winnerCorner >= 0 ? DisplayStyle.Flex : DisplayStyle.None;
                _meWinRosette.style.unityBackgroundImageTintColor = winnerColor;
            }
            if (_meWinName != null)
                _meWinName.text = winnerCorner >= 0
                    ? UiText.Format(UiKeys.LobbyPlayerTag, ("n", winnerCorner + 1))
                    : UiText.Get(UiKeys.PostmatchNoWinner);
            if (_meWinSub != null)
            {
                _meWinSub.text = winnerCorner >= 0
                    ? UiText.Format(UiKeys.PostmatchWinSub, ("cls", ClassName(winnerClass)), ("n", winnerCorner + 1))
                    : string.Empty;
                _meWinSub.style.color = winnerColor;
            }

            float winnerTotal = FoodForCorner(winnerCorner);
            if (_meWinScore != null) _meWinScore.text = Mathf.FloorToInt(winnerTotal).ToString();

            // Target note — reached vs timer-out
            if (_meTargetNote != null)
            {
                int target = _matchConfig != null ? Mathf.Max(1, _matchConfig.FoodTargetToWin) : 150;
                bool reached = winnerTotal >= target;
                _meTargetNote.text = UiText.Format(
                    reached ? UiKeys.PostmatchTargetReached : UiKeys.PostmatchTargetTimeout, ("n", target));
            }

            // Standings rows
            if (_meRows != null)
            {
                _meRows.Clear();
                float maxTotal = sorted.Count > 0 ? Mathf.Max(1f, sorted[0].total) : 1f;
                for (int rank = 0; rank < sorted.Count; rank++)
                {
                    var (corner, total) = sorted[rank];
                    _meRows.Add(BuildMeRow(rank, corner, total, maxTotal,
                        GetKillsForCorner(corner), isWinner: corner == winnerCorner));
                }
            }
        }

        private VisualElement BuildMeRow(int rank, int corner, float total, float maxTotal, int kills, bool isWinner)
        {
            Color color = ColorForCorner(corner);

            var row = new VisualElement();
            row.AddToClassList("cw-me-row");
            if (isWinner)
            {
                SetBorderColor(row, color);
                row.style.backgroundColor = Fade(color, 0.2f);
            }

            // Placement marker: Medal_1..3 for the podium, a plain ink number badge after it
            // (so every row has one, and it never reads as a second player dot).
            var medal = new VisualElement();
            medal.AddToClassList("cw-me-medal");
            if (rank < 3)
            {
                medal.AddToClassList($"cw-me-medal--{rank + 1}");
            }
            else
            {
                medal.AddToClassList("cw-me-place");
                var place = new Label((rank + 1).ToString());
                place.AddToClassList("cw-me-place__num");
                medal.Add(place);
            }
            row.Add(medal);

            // Player identity dot.
            var dot = new VisualElement();
            dot.AddToClassList("cw-me-dot");
            dot.style.unityBackgroundImageTintColor = color;
            row.Add(dot);

            // Name + class.
            var mid = new VisualElement();
            mid.AddToClassList("cw-me-rowmid");
            var name = new Label(UiText.Format(UiKeys.LobbyPlayerTag, ("n", corner + 1)));
            name.AddToClassList("cw-me-rowname");
            var sub = new Label(ClassName(ClassForCorner(corner)));
            sub.AddToClassList("cw-me-rowpn");
            sub.style.color = color;
            mid.Add(name); mid.Add(sub);
            row.Add(mid);

            // Progress bar (share of the leader's total).
            var trough = new VisualElement();
            trough.AddToClassList("cw-me-bartrough");
            var fill = new VisualElement();
            fill.AddToClassList("cw-me-barfill");
            fill.style.width = Length.Percent(Mathf.Clamp01(total / maxTotal) * 100f);
            fill.style.unityBackgroundImageTintColor = color;
            trough.Add(fill);
            row.Add(trough);

            // Food score.
            var food = new VisualElement();
            food.AddToClassList("cw-me-rowfood");
            var foodIcon = new VisualElement();
            foodIcon.AddToClassList("cw-me-rowfoodicon");
            var score = new Label(Mathf.FloorToInt(total).ToString());
            score.AddToClassList("cw-me-rowscore");
            food.Add(foodIcon); food.Add(score);
            row.Add(food);

            // Stats — knockouts only (ChickenMatchStats exposes just Kills). "{n} KO": the old
            // "K0" read as the word "KO" with no number.
            var stats = new Label(UiText.Format(UiKeys.PostmatchKos, ("n", kills)));
            stats.AddToClassList("cw-me-rowstats");
            row.Add(stats);

            return row;
        }

        // ========================================================================
        //  LOBBY
        // ========================================================================
        private void RefreshLobby(GameManager gm, bool poll)
        {
            bool show = gm != null && gm.State == MatchState.WaitingForPlayers;
            SetShown(_lobbyOverlay, show);
            if (!show || !poll) return;

            var runner = _network?.Runner;
            bool isHost = runner != null &&
                (runner.GameMode == GameMode.Single || runner.IsSharedModeMasterClient);

            int playerCount = 0;
            if (runner != null) foreach (var _ in runner.ActivePlayers) playerCount++;
            int maxPlayers = _matchConfig != null ? _matchConfig.MaxPlayers : 4;

            bool solo = runner != null && runner.GameMode == GameMode.Single;

            // Invite code: only a hosted session has anyone to invite, so never in solo.
            bool showInvite = isHost && !solo;
            SetShown(_lobbyInviteCard, showInvite);
            if (showInvite) SetCodeTiles(_selection?.SessionName);

            // Settings.
            if (_lobbySettingTime != null && _matchConfig != null)
                _lobbySettingTime.text = MatchSettingsText.Time(_matchConfig.MatchDurationSeconds);
            if (_lobbySettingGoal != null && _matchConfig != null)
                _lobbySettingGoal.text = MatchSettingsText.Goal(_matchConfig.FoodTargetToWin);

            // Status pill.
            if (_lobbyStatusCount != null) _lobbyStatusCount.text = $"{playerCount}/{maxPlayers}";
            if (_lobbyStatusText  != null)
                _lobbyStatusText.text = solo ? UiText.Get(UiKeys.LobbyStatusSolo)
                    : isHost ? "Waiting…" : "Waiting for host…";

            // Start button + hint (host only).
            SetShown(_lobbyStartBtn, isHost);
            if (_lobbyHint != null)
            {
                _lobbyHint.text = solo ? UiText.Get(UiKeys.LobbyHintSolo)
                    : isHost ? "Start whenever ready — no minimum players required."
                    : "Waiting for host to start the match…";
            }

            BuildPlayerGrid(runner, maxPlayers);
        }

        private void BuildPlayerGrid(NetworkRunner runner, int maxPlayers)
        {
            if (_lobbyGrid == null) return;
            _lobbyGrid.Clear();

            int localCorner = LocalCorner();
            int seats = Mathf.Clamp(maxPlayers, 1, 4);
            for (int corner = 0; corner < seats; corner++)
            {
                var chicken = ChickenForCorner(corner);
                if (chicken == null)
                {
                    _lobbyGrid.Add(MakeEmptyCard(corner));
                    continue;
                }
                bool isLocalHost = corner == localCorner && runner != null &&
                    (runner.GameMode == GameMode.Single || runner.IsSharedModeMasterClient);
                _lobbyGrid.Add(MakePlayerCard(corner, chicken.Class, chicken.IsBot, isLocalHost));
            }
        }

        private VisualElement MakeEmptyCard(int corner)
        {
            var card = new VisualElement();
            card.AddToClassList("cw-player-card");
            card.AddToClassList("cw-player-card--empty");
            var lbl = new Label($"WAITING FOR P{corner + 1}");
            lbl.AddToClassList("cw-player-empty-label");
            card.Add(lbl);
            return card;
        }

        private VisualElement MakePlayerCard(int corner, ChickenClass cls, bool isBot, bool isHost)
        {
            Color color = ColorForCorner(corner);

            var card = new VisualElement();
            card.AddToClassList("cw-player-card");
            SetBorderColor(card, Fade(color, 0.85f));
            card.style.unityBackgroundImageTintColor = Fade(color, 0.18f);

            var accent = new VisualElement();
            accent.AddToClassList("cw-player-accent");
            accent.style.backgroundColor = color;
            card.Add(accent);

            var art = new VisualElement();
            art.AddToClassList("cw-player-art");
            art.AddToClassList("cw-chicken--" + KeyOf(cls));
            card.Add(art);

            var mid = new VisualElement();
            mid.AddToClassList("cw-player-mid");

            var nameRow = new VisualElement();
            nameRow.AddToClassList("cw-player-namerow");
            var name = new Label($"P{corner + 1}");
            name.AddToClassList("cw-player-name");
            var pn = new Label(cls.ToString().ToUpperInvariant());
            pn.AddToClassList("cw-player-pn");
            pn.style.color = color;
            nameRow.Add(name); nameRow.Add(pn);
            if (isHost || isBot)
            {
                var tag = new Label(isHost ? "HOST" : "CPU");
                tag.AddToClassList("cw-player-host");
                if (isBot && !isHost) { tag.style.backgroundColor = color; tag.style.color = new Color(1f, 0.96f, 0.88f, 1f); }
                nameRow.Add(tag);
            }
            mid.Add(nameRow);

            var clsLine = new Label($"{cls.ToString().ToUpperInvariant()} · {Passive(cls)}");
            clsLine.AddToClassList("cw-player-class");
            mid.Add(clsLine);
            card.Add(mid);

            var state = new VisualElement();
            state.AddToClassList("cw-player-state");
            state.AddToClassList("cw-player-state--ready");
            var sl = new Label("✓ READY");
            sl.AddToClassList("cw-player-state__label");
            state.Add(sl);
            card.Add(state);

            return card;
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

            float elapsed   = Time.unscaledTime - _shutdownAtUnscaledTime;
            float remaining = Mathf.Max(0f, _disconnectReturnDelay - elapsed);
            if (_sessionEndReason    != null) _sessionEndReason.text    = $"Reason: {_shutdownReason}";
            if (_sessionEndCountdown != null) _sessionEndCountdown.text = $"Returning to menu in {Mathf.CeilToInt(remaining)}s…";

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
            if (gm == null) { _introCues.Reset(); SetShown(_introOverlay, false); return; }

            // The timer is networked and untouched; the cues below are this client's own presentation of it.
            var cue = _introCues.Observe(gm.IsIntroActive, gm.IsIntroActive ? gm.IntroRemaining : 0f, out int number);

            if (gm.IsIntroActive)
            {
                if (_introNumber != null && cue == IntroCueTracker.Cue.Tick) _introNumber.text = number.ToString();
                SetShown(_introOverlay, true);
                _goExpiresAtUnscaledTime = Time.unscaledTime + GoFlourishDuration;
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
        private static List<(int corner, float total)> BuildSortedLeaderboard()
        {
            var bases = PlayerBase.ActiveBases;
            var list  = new List<(int, float)>(bases.Count);
            for (int i = 0; i < bases.Count; i++)
            {
                var b = bases[i];
                if (b == null) continue;
                list.Add((b.CornerIndex, b.FoodTotal));
            }
            list.Sort((a, b) => b.Item2.CompareTo(a.Item2));
            return list;
        }

        private static float FoodForCorner(int corner)
        {
            var bases = PlayerBase.ActiveBases;
            for (int i = 0; i < bases.Count; i++)
            {
                var b = bases[i];
                if (b != null && b.CornerIndex == corner) return b.FoodTotal;
            }
            return 0f;
        }

        private static ChickenClass ClassForCorner(int corner)
        {
            var c = ChickenForCorner(corner);
            return c != null ? c.Class : ChickenClass.Warrior;
        }

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

        /// <summary>Player-facing class name from the wording dictionary (class.*.short).</summary>
        private static string ClassName(ChickenClass cls) => UiText.Get(cls switch
        {
            ChickenClass.Speedy   => UiKeys.ClassSpeedyShort,
            ChickenClass.Fatty    => UiKeys.ClassFattyShort,
            ChickenClass.Assassin => UiKeys.ClassAssassinShort,
            _                     => UiKeys.ClassWarriorShort,
        });

        private static string Passive(ChickenClass cls) => cls switch
        {
            ChickenClass.Warrior  => "Tough",
            ChickenClass.Speedy   => "Slippery",
            ChickenClass.Fatty    => "Immovable",
            ChickenClass.Assassin => "Combo",
            _                     => "—",
        };

        private static Color Fade(Color c, float a) => new Color(c.r, c.g, c.b, a);

        private static void SetBorderColor(VisualElement ve, Color c)
        {
            ve.style.borderTopColor = c; ve.style.borderBottomColor = c;
            ve.style.borderLeftColor = c; ve.style.borderRightColor = c;
        }
    }
}

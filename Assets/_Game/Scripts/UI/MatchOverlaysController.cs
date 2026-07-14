using System.Collections.Generic;
using CluckWars.Gameplay;
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
    /// falls back to the SceneContext first (MatchConfigSO is scene-scoped).
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

        // ---- UI element refs (queried once, on bind) ---------------------------
        private VisualElement _root;
        private bool          _bound;

        private VisualElement _matchEndOverlay, _lobbyOverlay, _sessionEndOverlay, _introOverlay;

        private Label         _meRibbon, _meWinSub, _meWinName, _meWinScore, _meTargetNote, _meRestart;
        private VisualElement _meWinChicken, _meWinGlow, _meWinRing, _meRows;
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

        // Intro "GO!" flourish — lingers briefly after the countdown hits zero.
        private float       _goExpiresAtUnscaledTime;
        private const float GoFlourishDuration = 0.6f;

        // ---- Injection ---------------------------------------------------------
        [Inject]
        public void Construct(
            INetworkService          network,
            ILogService              log,
            ColorSchemeSO            colors,
            MatchConfigSO            matchConfig,
            ISessionSelectionService selection)
        {
            _network     = network;
            _log         = log;
            _colors      = colors;
            _matchConfig = matchConfig;
            _selection   = selection;
        }

        // ---- Unity lifecycle ---------------------------------------------------
        private void Awake()
        {
            if (_network == null)
            {
                // MatchConfigSO is bound in GameInstaller (scene scope), so inject
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
        }

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
            _meWinSub     = _root.Q<Label>("MeWinSub");
            _meWinName    = _root.Q<Label>("MeWinName");
            _meWinScore   = _root.Q<Label>("MeWinScore");
            _meTargetNote = _root.Q<Label>("MeTargetNote");
            _meRestart    = _root.Q<Label>("MeRestart");
            _meRows       = _root.Q<VisualElement>("MeRows");

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

            if (_lobbyStartBtn != null) _lobbyStartBtn.clicked += OnLobbyStart;
            if (_lobbyCopyBtn  != null) _lobbyCopyBtn.clicked  += CopyJoinCode;
            if (_lobbyShareBtn != null) _lobbyShareBtn.clicked += CopyJoinCode;

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
            if (!show) { _matchEndPopulated = false; return; }

            // Scores freeze once the match ends, so build the hero + rows once on
            // entry; only the restart countdown updates each frame.
            if (!_matchEndPopulated)
            {
                PopulateMatchEnd(gm);
                _matchEndPopulated = true;
            }

            if (_meRestart != null)
            {
                float restartIn = gm.RestartRemaining;
                _meRestart.text = restartIn > 0f
                    ? $"Next match in {Mathf.CeilToInt(restartIn)}s…"
                    : "Starting next match…";
            }
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
                    ? (winnerReal ? $"P{winnerCorner + 1} WINS!" : $"P{winnerCorner + 1} (CPU) WINS!")
                    : "MATCH ENDED";
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
            if (_meWinRing != null) SetBorderColor(_meWinRing, winnerColor);
            if (_meWinName != null)
                _meWinName.text = winnerCorner >= 0 ? $"P{winnerCorner + 1}" : "—";
            if (_meWinSub != null)
            {
                _meWinSub.text = winnerCorner >= 0
                    ? $"{winnerClass.ToString().ToUpperInvariant()} · P{winnerCorner + 1}"
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
                _meTargetNote.text = $"target {target} · {(reached ? "reached" : "timer out")}";
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

            // Rank medal (1-3) or player-tinted dot (4th+).
            var medal = new VisualElement();
            if (rank < 3)
            {
                medal.AddToClassList("cw-me-medal");
                medal.AddToClassList($"cw-me-medal--{rank + 1}");
            }
            else
            {
                medal.AddToClassList("cw-me-dot");
                medal.style.unityBackgroundImageTintColor = color;
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
            var name = new Label($"P{corner + 1}");
            name.AddToClassList("cw-me-rowname");
            var sub = new Label(ClassForCorner(corner).ToString().ToUpperInvariant());
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

            // Stats — kills only (ChickenMatchStats exposes just Kills).
            var stats = new Label($"K{kills}");
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

            // Invite code (host only).
            SetShown(_lobbyInviteCard, isHost);
            if (isHost) SetCodeTiles(_selection?.SessionName);

            // Settings.
            if (_lobbySettingTime != null && _matchConfig != null)
                _lobbySettingTime.text = FormatTime(_matchConfig.MatchDurationSeconds);
            if (_lobbySettingGoal != null && _matchConfig != null)
                _lobbySettingGoal.text = $"{_matchConfig.FoodTargetToWin} food";

            // Status pill.
            if (_lobbyStatusCount != null) _lobbyStatusCount.text = $"{playerCount}/{maxPlayers}";
            if (_lobbyStatusText  != null) _lobbyStatusText.text  = isHost ? "Waiting…" : "Waiting for host…";

            // Start button + hint (host only).
            SetShown(_lobbyStartBtn, isHost);
            if (_lobbyHint != null)
            {
                _lobbyHint.text = isHost
                    ? "Start whenever ready — no minimum players required."
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
            if (gm == null) { SetShown(_introOverlay, false); SetTopBarDimmed(false); return; }

            if (gm.IsIntroActive)
            {
                if (_introNumber != null) _introNumber.text = Mathf.CeilToInt(gm.IntroRemaining).ToString();
                SetShown(_introOverlay, true);
                SetTopBarDimmed(true);
                _goExpiresAtUnscaledTime = Time.unscaledTime + GoFlourishDuration;
                return;
            }

            if (Time.unscaledTime < _goExpiresAtUnscaledTime)
            {
                if (_introNumber != null) _introNumber.text = "GO!";
                SetShown(_introOverlay, true);
                SetTopBarDimmed(true);
            }
            else
            {
                SetShown(_introOverlay, false);
                SetTopBarDimmed(false);
            }
        }

        /// <summary>
        /// ART.md §6.5: the top bar (a separate UIDocument/controller,
        /// <see cref="MatchHudController"/>) stays visible at 40% opacity behind
        /// the intro countdown overlay rather than being hidden outright.
        /// </summary>
        private static void SetTopBarDimmed(bool dimmed) => MatchHudController.Instance?.SetIntroDimmed(dimmed);

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

        private static string Passive(ChickenClass cls) => cls switch
        {
            ChickenClass.Warrior  => "Tough",
            ChickenClass.Speedy   => "Slippery",
            ChickenClass.Fatty    => "Immovable",
            ChickenClass.Assassin => "Combo",
            _                     => "—",
        };

        private static string FormatTime(float seconds)
        {
            int mm = Mathf.Max(0, Mathf.FloorToInt(seconds / 60f));
            int ss = Mathf.Max(0, Mathf.FloorToInt(seconds - mm * 60f));
            return $"{mm}:{ss:00}";
        }

        private static Color Fade(Color c, float a) => new Color(c.r, c.g, c.b, a);

        private static void SetBorderColor(VisualElement ve, Color c)
        {
            ve.style.borderTopColor = c; ve.style.borderBottomColor = c;
            ve.style.borderLeftColor = c; ve.style.borderRightColor = c;
        }
    }
}

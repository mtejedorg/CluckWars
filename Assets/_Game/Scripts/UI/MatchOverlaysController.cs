using System;
using System.Collections.Generic;
using CluckWars.Gameplay;
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
    /// in-match Lobby, Session End, Intro countdown. Layout lives in
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

        // Pre-intro GET READY (IntroOverlayRule): whether a GameManager has ever been seen, since when
        // this overlay has been waiting for the first one, and whether the no-manager timeout was reported.
        private bool        _sawGameManager;
        private float       _waitingForManagerSince = -1f;
        private bool        _reportedNoManager;

        // ---- Injection ---------------------------------------------------------
        [Inject]
        public void Construct(
            INetworkService          network,
            ILogService              log,
            ColorSchemeSO            colors,
            MatchConfigSO            matchConfig,
            ISessionSelectionService selection,
            [InjectOptional] MenuAudio              audio,
            [InjectOptional] ChickenClassRegistrySO classRegistry)
        {
            _network       = network;
            _log           = log;
            _colors        = colors;
            _matchConfig   = matchConfig;
            _selection     = selection;
            _audio         = audio ?? MenuAudio.Silent();
            _classRegistry = classRegistry;
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
            _log?.Warn(Source, $"Network shutdown: {reason}. Returning to '{_bootstrapSceneName}' in {_disconnectReturnDelay}s.");
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

            // Same safe-area rule as the menu: content clear of notches / the gesture bar while the
            // backgrounds stay full-bleed (#MeSafe sits inside the match-end backdrop; the lobby and
            // session-end overlays pad themselves, their dim fill still reaching the edges).
            if (_meSafe == null) _log?.Error(Source, "MatchOverlays.uxml has no #MeSafe; the post-match screen ignores the safe area.");
            _safeArea = new SafeAreaPadding(_meSafe, _lobbyOverlay, _sessionEndOverlay);
            _root.RegisterCallback<GeometryChangedEvent>(_ => _safeArea.Apply(force: true));

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
        //  LOBBY
        // ========================================================================
        private void RefreshLobby(GameManager gm, bool poll)
        {
            bool show = gm != null && gm.State == MatchState.WaitingForPlayers;
            // Fill on the frame it opens, not on the next poll: its TIME / GOAL / status labels are
            // authored blank (bound from MatchConfig here), so a late fill would flash empty rows.
            bool opening = show && !IsShown(_lobbyOverlay);
            SetShown(_lobbyOverlay, show);
            if (!show || !(poll || opening)) return;

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

            // House rules.
            if (_lobbySettingTime != null && _matchConfig != null)
                _lobbySettingTime.text = MatchSettingsText.Time(_matchConfig.MatchDurationSeconds);
            if (_lobbySettingGoal != null && _matchConfig != null)
                _lobbySettingGoal.text = MatchSettingsText.Goal(_matchConfig.FoodTargetToWin);

            // Status pill.
            if (_lobbyStatusCount != null)
                _lobbyStatusCount.text = UiText.Format(UiKeys.LobbyCount, ("n", playerCount), ("max", maxPlayers));
            if (_lobbyStatusText != null)
                _lobbyStatusText.text = UiText.Get(solo ? UiKeys.LobbyStatusSolo : UiKeys.LobbyStatusWaiting);

            // Start button + hint (host only).
            SetShown(_lobbyStartBtn, isHost);
            if (_lobbyHint != null)
                _lobbyHint.text = UiText.Get(solo ? UiKeys.LobbyHintSolo : isHost ? UiKeys.LobbyHintHost : UiKeys.LobbyHintGuest);

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
                bool isLocal = corner == localCorner;
                bool isLocalHost = isLocal && runner != null &&
                    (runner.GameMode == GameMode.Single || runner.IsSharedModeMasterClient);
                _lobbyGrid.Add(MakePlayerCard(corner, chicken, isLocal, isLocalHost));
            }
        }

        private static VisualElement MakeEmptyCard(int corner)
        {
            var card = new VisualElement();
            card.AddToClassList("cw-player-card");
            card.AddToClassList("cw-player-card--empty");
            var lbl = new Label(UiText.Format(UiKeys.LobbyWaitingFor, ("n", corner + 1)));
            lbl.AddToClassList("cw-player-empty-label");
            card.Add(lbl);
            return card;
        }

        private VisualElement MakePlayerCard(int corner, ChickenController chicken, bool isLocal, bool isHost)
        {
            Color color = ColorForCorner(corner);
            var cls = chicken.Class;

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
            var name = new Label(MatchStandings.DisplayName(isLocal, chicken.IsBot, cls, corner));
            name.AddToClassList("cw-player-name");
            nameRow.Add(name);
            if (isHost || chicken.IsBot)
            {
                var tag = new Label(UiText.Get(isHost ? UiKeys.TagHost : UiKeys.TagCpu));
                tag.AddToClassList("cw-player-host");
                nameRow.Add(tag);
            }
            mid.Add(nameRow);

            // "{CLASS} · {perk}" from the chicken's own equipped passive (AbilityController.Passive: the
            // chosen one on this peer, the class default for a remote player). No perk, just the class.
            var passive = chicken.GetComponent<AbilityController>()?.Passive;
            string perk = passive != null ? passive.DisplayName : null;
            var clsLine = new Label(string.IsNullOrEmpty(perk)
                ? ClassName(cls)
                : UiText.Format(UiKeys.LobbyClassPerk, ("cls", ClassName(cls)), ("perk", perk)));
            clsLine.AddToClassList("cw-player-class");
            mid.Add(clsLine);

            // READY sits under the class line, inside the text column: as a third column it
            // covered the tag and the perk on narrow (4:3) cards.
            var state = new VisualElement();
            state.AddToClassList("cw-player-state");
            state.AddToClassList("cw-player-state--ready");
            var sl = new Label(UiText.Get(UiKeys.StateReady));
            sl.AddToClassList("cw-player-state__label");
            state.Add(sl);
            mid.Add(state);
            card.Add(mid);

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
            DisposeStage();

            float elapsed   = Time.unscaledTime - _shutdownAtUnscaledTime;
            float remaining = Mathf.Max(0f, _disconnectReturnDelay - elapsed);
            if (_sessionEndReason != null)
                _sessionEndReason.text = UiText.Format(UiKeys.SessionReason, ("reason", _shutdownReason.ToString()));
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
            if (hold != IntroOverlayRule.PreIntro.None)
            {
                _introCues.Reset();
                if (_introNumber != null && _introNumber.text.Length > 0) _introNumber.text = string.Empty;
                SetShown(_introOverlay, hold == IntroOverlayRule.PreIntro.GetReady && !_shutdownReason.HasValue);
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

        private static Color Fade(Color c, float a) => new Color(c.r, c.g, c.b, a);

        private static void SetBorderColor(VisualElement ve, Color c)
        {
            ve.style.borderTopColor = c; ve.style.borderBottomColor = c;
            ve.style.borderLeftColor = c; ve.style.borderRightColor = c;
        }
    }
}

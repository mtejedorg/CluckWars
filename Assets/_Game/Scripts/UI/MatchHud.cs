using System.Collections.Generic;
using CluckWars.Gameplay;
using CluckWars.Logging;
using CluckWars.Networking;
using CluckWars.Services;
using Fusion;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Zenject;

namespace CluckWars.UI
{
    /// <summary>
    /// UGUI match HUD. Builds the entire canvas procedurally on Awake so
    /// Game.unity stays a single drop-in component.
    /// Phase 10 layout (ART.md §6, cluckwars-tokens-v2):
    /// <list type="bullet">
    ///   <item>Top-left panel: ranked leaderboard sorted live by food score.</item>
    ///   <item>Top-right badge: match timer in gold.</item>
    ///   <item>Bottom-left panel: local HP + cargo bars.</item>
    ///   <item>Centered overlays: lobby, match-end, session-end, intro countdown.</item>
    /// </list>
    /// </summary>
    /// <remarks>
    /// Auto-disables any sibling <c>CargoHud</c> on Awake.
    /// </remarks>
    public sealed class MatchHud : MonoBehaviour
    {
        private const string Source = "MatchHud";

        [SerializeField] private float   _refreshInterval     = 0.25f;
        [SerializeField] private Vector2 _referenceResolution = new Vector2(1920f, 1080f);

        [Tooltip("Seconds the 'Session ended' overlay stays up before auto-returning.")]
        [Min(0.5f)]
        [SerializeField] private float _disconnectReturnDelay = 5f;

        [Tooltip("Bootstrap scene to load after a disconnect.")]
        [SerializeField] private string _bootstrapSceneName = "Bootstrap";

        // ----------------------------------------------------------------
        // Design-token palette — Phase 10 redesign (ART.md §6, cluckwars-tokens-v2).
        // Self-contained so the warm Clash Royale/Supercell look applies regardless
        // of which ColorSchemeSO asset version is serialised in the scene.
        // ----------------------------------------------------------------
        private static readonly Color DtPanelBg     = new Color(0.23f, 0.13f, 0.06f, 1f); // #3a2210
        private static readonly Color DtGold        = new Color(0.96f, 0.78f, 0.26f, 1f); // #f5c842
        private static readonly Color DtGoldMid     = new Color(0.83f, 0.63f, 0.13f, 1f); // #d4a020
        private static readonly Color DtGreenMid    = new Color(0.20f, 0.64f, 0.20f, 1f); // #33a332
        private static readonly Color DtTextPrimary = new Color(1.00f, 0.96f, 0.88f, 1f); // #fef5e0

        // Colorblind-safe per-player identity colors (Okabe-Ito derived; ART.md §6).
        private static readonly Color[] PlayerColors =
        {
            new Color(0.91f, 0.46f, 0.10f, 1f), // P1 Orange  #E8751A
            new Color(0.10f, 0.50f, 0.77f, 1f), // P2 Blue    #1A7FC4
            new Color(0.77f, 0.16f, 0.44f, 1f), // P3 Pink    #C4286F
            new Color(0.05f, 0.62f, 0.48f, 1f), // P4 Teal    #0D9E7A
        };

        // ---- UI refs built procedurally ------------------------------------

        private Text _timerLabel;

        // Ranked leaderboard rows (top-left panel).
        // Replaces the old per-player totals that lived in the top bar (pre-Phase 10).
        private struct LeaderboardRow
        {
            public Image ColorDot;
            public Text  PlayerLabel;
            public Text  ScoreLabel;
            public Image ProgressFill;
        }
        private LeaderboardRow[] _lbRows = new LeaderboardRow[4];

        private Image      _hpFill;
        private Text       _hpLabel;
        private Image      _cargoFill;
        private Text       _cargoLabel;
        private GameObject _matchEndPanel;
        private Text       _matchEndTitle;
        private Text[]     _leaderboardRows;
        private Text       _restartCountdownLabel;
        private GameObject _sessionEndPanel;
        private Text       _sessionEndReason;
        private Text       _sessionEndCountdown;
        private Text       _introLabel;
        private GameObject _lobbyPanel;
        private Text       _lobbyHint;
        private Button     _lobbyStartButton;
        private Text       _lobbyPlayerCount;
        private Text       _lobbyJoinCode;

        // ---- Cached scene refs -------------------------------------------------

        private ChickenController _localController;
        private ChickenCombat     _localCombat;
        private ChickenCargo      _localCargo;
        private PlayerBase[]      _bases     = System.Array.Empty<PlayerBase>();
        private ChickenMatchStats[] _matchStats = System.Array.Empty<ChickenMatchStats>();
        private GameManager       _gameManager;
        private float             _nextRefresh;

        // ---- Injected services -------------------------------------------------

        private INetworkService          _network;
        private ILogService              _log;
        private ColorSchemeSO            _colors;
        private MatchConfigSO            _matchConfig;
        private ISessionSelectionService _selection;
        private ShutdownReason?          _shutdownReason;
        private float                    _shutdownAtUnscaledTime;
        private bool                     _returnTriggered;

        // Intro "GO!" flourish — lingers briefly after the countdown hits zero.
        private float        _goExpiresAtUnscaledTime;
        private const float  GoFlourishDuration = 0.6f;

        // Hit-flash — red overlay on local-chicken HP decrease.
        private Image _hitFlash;
        private float _lastLocalHp = float.NaN;
        private float _hitFlashAlpha;
        [SerializeField] private float _hitFlashPeakAlpha   = 0.35f;
        [SerializeField] private float _hitFlashFadeSeconds = 0.35f;

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
            if (_network == null) ProjectContext.Instance.Container.Inject(this);
            if (_network != null) _network.OnShutdown += HandleShutdown;

            DisableLegacyCargoHud();
            EnsureEventSystem();
            BuildCanvas();
        }

        private void OnDestroy()
        {
            if (_network != null) _network.OnShutdown -= HandleShutdown;
        }

        private void DisableLegacyCargoHud()
        {
            var legacy = FindFirstObjectByType<CargoHud>();
            if (legacy != null)
            {
                legacy.enabled = false;
                _log?.Debug(Source, "Disabled sibling CargoHud (IMGUI) — MatchHud (UGUI) takes over.");
            }
        }

        private static void EnsureEventSystem()
        {
            if (FindFirstObjectByType<EventSystem>() != null) return;
            new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
        }

        private void HandleShutdown(ShutdownReason reason)
        {
            _shutdownReason         = reason;
            _shutdownAtUnscaledTime = Time.unscaledTime;
            _log?.Warn(Source, $"Network shutdown: {reason}. Returning to '{_bootstrapSceneName}' in {_disconnectReturnDelay}s.");
        }

        // ---- Lifecycle tick ----------------------------------------------------

        private void Update()
        {
            if (_shutdownReason.HasValue)
            {
                TickSessionEnd();
                return;
            }

            if (Time.unscaledTime < _nextRefresh
                && _localController != null
                && _bases.Length > 0
                && _gameManager != null)
            {
                RefreshDynamic();
                return;
            }
            _nextRefresh = Time.unscaledTime + _refreshInterval;

            RefreshSceneRefs();
            RefreshDynamic();
        }

        private void TickSessionEnd()
        {
            if (_sessionEndPanel != null) _sessionEndPanel.SetActive(true);
            if (_matchEndPanel   != null) _matchEndPanel.SetActive(false);

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

        private void RefreshSceneRefs()
        {
            if (_localController == null || _localController.Object == null || !_localController.Object.IsValid)
            {
                _localController = null;
                _localCombat     = null;
                _localCargo      = null;
                var all = FindObjectsByType<ChickenController>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
                for (int i = 0; i < all.Length; i++)
                {
                    var c = all[i];
                    if (c.Object == null || !c.Object.IsValid) continue;
                    if (!c.HasInputAuthority) continue;
                    _localController = c;
                    _localCombat     = c.Combat;
                    _localCargo      = c.Cargo;
                    break;
                }
            }

            if (_bases.Length == 0 || HasStaleBase())
                _bases = FindObjectsByType<PlayerBase>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);

            // Refresh match stats refs every scene-ref pass (every _refreshInterval).
            _matchStats = FindObjectsByType<ChickenMatchStats>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);

            if (_gameManager == null || _gameManager.Object == null || !_gameManager.Object.IsValid)
                _gameManager = FindFirstObjectByType<GameManager>();
        }

        private bool HasStaleBase()
        {
            for (int i = 0; i < _bases.Length; i++)
            {
                var b = _bases[i];
                if (b == null || b.Object == null || !b.Object.IsValid) return true;
            }
            return false;
        }

        // ---- Per-frame data → UI -----------------------------------------------

        private void RefreshDynamic()
        {
            RefreshTimer();
            RefreshLeaderboard();
            RefreshLocalStats();
            RefreshMatchEndOverlay();
            RefreshIntroOverlay();
            RefreshLobbyOverlay();
            TickHitFlash();
        }

        // ---- Leaderboard refresh -----------------------------------------------

        private void RefreshLeaderboard()
        {
            if (_lbRows == null) return;
            var   sorted = BuildSortedLeaderboard();
            float goal   = _matchConfig != null ? Mathf.Max(1f, _matchConfig.FoodTargetToWin) : 150f;

            for (int i = 0; i < _lbRows.Length; i++)
            {
                var row = _lbRows[i];
                if (row.PlayerLabel == null) continue;

                if (i >= sorted.Count)
                {
                    if (row.ColorDot    != null) row.ColorDot.color = Color.grey;
                    row.PlayerLabel.text = "—";
                    if (row.ScoreLabel   != null) row.ScoreLabel.text    = "";
                    if (row.ProgressFill != null) row.ProgressFill.fillAmount = 0f;
                    continue;
                }

                var (cornerIdx, total) = sorted[i];
                var playerColor = PlayerColors[cornerIdx % PlayerColors.Length];

                if (row.ColorDot != null) row.ColorDot.color = playerColor;

                row.PlayerLabel.text  = $"#{i + 1}  P{cornerIdx + 1}";
                row.PlayerLabel.color = playerColor;

                if (row.ScoreLabel != null)
                {
                    row.ScoreLabel.text  = Mathf.FloorToInt(total).ToString();
                    row.ScoreLabel.color = DtGold;
                }

                if (row.ProgressFill != null)
                    row.ProgressFill.fillAmount = Mathf.Clamp01(total / goal);
            }
        }

        // ---- Lobby overlay refresh ---------------------------------------------

        private void RefreshLobbyOverlay()
        {
            if (_lobbyPanel == null) return;

            bool show = _gameManager != null && _gameManager.State == MatchState.WaitingForPlayers;
            if (_lobbyPanel.activeSelf != show) _lobbyPanel.SetActive(show);
            if (!show) return;

            int playerCount = 0;
            var runner = _network?.Runner;
            if (runner != null)
                foreach (var _ in runner.ActivePlayers) playerCount++;

            if (_lobbyPlayerCount != null)
            {
                int maxPlayers = _matchConfig != null ? _matchConfig.MaxPlayers : 4;
                _lobbyPlayerCount.text = $"Players: {playerCount} / {maxPlayers}";
            }

            bool isHost = runner != null &&
                (runner.GameMode == GameMode.Single || runner.IsSharedModeMasterClient);

            if (_lobbyJoinCode != null)
            {
                var code = _selection?.SessionName;
                _lobbyJoinCode.text = string.IsNullOrEmpty(code) ? string.Empty : $"Code: {code}";
            }

            if (_lobbyStartButton != null)
                _lobbyStartButton.gameObject.SetActive(isHost);
            if (_lobbyHint != null)
            {
                _lobbyHint.gameObject.SetActive(true);
                _lobbyHint.text = isHost
                    ? "Start whenever ready — no minimum players required."
                    : "Waiting for host to start the match…";
            }
        }

        // ---- Hit-flash ---------------------------------------------------------

        private void TickHitFlash()
        {
            if (_hitFlash == null) return;

            if (_localCombat != null)
            {
                float hp = _localCombat.HP;
                if (!float.IsNaN(_lastLocalHp) && hp < _lastLocalHp - 0.01f)
                    _hitFlashAlpha = _hitFlashPeakAlpha;
                _lastLocalHp = hp;
            }
            else
            {
                _lastLocalHp = float.NaN;
            }

            if (_hitFlashAlpha > 0f)
            {
                float fadePerSec = _hitFlashFadeSeconds > 0f
                    ? _hitFlashPeakAlpha / _hitFlashFadeSeconds
                    : _hitFlashPeakAlpha;
                _hitFlashAlpha = Mathf.Max(0f, _hitFlashAlpha - fadePerSec * Time.unscaledDeltaTime);
            }

            var c = _hitFlash.color;
            c.a = _hitFlashAlpha;
            _hitFlash.color = c;
        }

        // ---- Intro countdown ---------------------------------------------------

        private void RefreshIntroOverlay()
        {
            if (_introLabel == null) return;
            if (_gameManager == null) { _introLabel.gameObject.SetActive(false); return; }

            if (_gameManager.IsIntroActive)
            {
                float remaining = _gameManager.IntroRemaining;
                int   displayed = Mathf.CeilToInt(remaining);
                _introLabel.text = displayed.ToString();
                _introLabel.gameObject.SetActive(true);
                _goExpiresAtUnscaledTime = Time.unscaledTime + GoFlourishDuration;
                return;
            }

            if (Time.unscaledTime < _goExpiresAtUnscaledTime)
            {
                _introLabel.text = "GO!";
                _introLabel.gameObject.SetActive(true);
            }
            else
            {
                _introLabel.gameObject.SetActive(false);
            }
        }

        // ---- Timer -------------------------------------------------------------

        private void RefreshTimer()
        {
            if (_timerLabel == null) return;
            if (_gameManager == null) { _timerLabel.text = "--:--"; return; }

            switch (_gameManager.State)
            {
                case MatchState.Active:
                    float r  = _gameManager.TimeRemaining;
                    int   mm = Mathf.Max(0, Mathf.FloorToInt(r / 60f));
                    int   ss = Mathf.Max(0, Mathf.FloorToInt(r - mm * 60f));
                    _timerLabel.text = $"{mm:00}:{ss:00}";
                    break;
                case MatchState.Ended:
                    _timerLabel.text = "ENDED";
                    break;
                default:
                    _timerLabel.text = "WAIT";
                    break;
            }
        }

        // ---- Local stats -------------------------------------------------------

        private void RefreshLocalStats()
        {
            if (_hpFill != null && _hpLabel != null)
            {
                if (_localCombat != null && _localController?.Stats != null)
                {
                    float max = Mathf.Max(1f, _localController.Stats.MaxHP);
                    float hp  = Mathf.Max(0f, _localCombat.HP);
                    _hpFill.fillAmount = Mathf.Clamp01(hp / max);
                    _hpLabel.text = $"HP {Mathf.CeilToInt(hp)} / {Mathf.CeilToInt(max)}";
                }
                else
                {
                    _hpFill.fillAmount = 0f;
                    _hpLabel.text = "HP —";
                }
            }

            if (_cargoFill != null && _cargoLabel != null)
            {
                if (_localCargo != null)
                {
                    float cap      = Mathf.Max(1f, _localCargo.Capacity);
                    float cur      = Mathf.Max(0f, _localCargo.Cargo);
                    float fraction = Mathf.Clamp01(cur / cap);

                    _cargoFill.fillAmount = fraction;

                    // Grade from gold → orange → red as cargo fills; solid red + urgent
                    // label when at capacity so the player knows to return to base.
                    _cargoFill.color = fraction >= 1f
                        ? new Color(1.00f, 0.20f, 0.10f, 1f)   // red  — FULL
                        : fraction >= 0.75f
                            ? new Color(1.00f, 0.55f, 0.05f, 1f) // orange — nearly full
                            : DtGoldMid;                          // gold — normal

                    _cargoLabel.text = fraction >= 1f
                        ? "FULL  →  RETURN TO BASE!"
                        : $"Cargo  {Mathf.FloorToInt(cur)} / {Mathf.FloorToInt(cap)}";
                    _cargoLabel.color = fraction >= 1f
                        ? new Color(1f, 0.9f, 0.85f, 1f)
                        : DtTextPrimary;
                }
                else
                {
                    _cargoFill.fillAmount = 0f;
                    _cargoFill.color      = DtGoldMid;
                    _cargoLabel.text      = "Cargo —";
                    _cargoLabel.color     = DtTextPrimary;
                }
            }
        }

        // ---- Match-end overlay -------------------------------------------------

        private void RefreshMatchEndOverlay()
        {
            bool show = _gameManager != null && _gameManager.State == MatchState.Ended;
            if (_matchEndPanel != null) _matchEndPanel.SetActive(show);
            if (!show) return;

            if (_matchEndTitle != null)
            {
                var winner = _gameManager.WinnerPlayer;
                _matchEndTitle.text = winner.IsRealPlayer
                    ? $"PLAYER {winner.PlayerId + 1} WINS!"
                    : "MATCH ENDED";
            }

            var rows = BuildSortedLeaderboard();
            for (int i = 0; i < _leaderboardRows.Length; i++)
            {
                var row = _leaderboardRows[i];
                if (row == null) continue;
                if (i >= rows.Count) { row.text = ""; continue; }
                var (cornerIdx, total) = rows[i];
                int kills = GetKillsForCorner(cornerIdx);
                row.text  = $"#{i + 1}  P{cornerIdx + 1}:  {Mathf.FloorToInt(total)} food  |  {kills} kills";
                row.color = PlayerColors[cornerIdx % PlayerColors.Length];
            }

            if (_restartCountdownLabel != null)
            {
                float restartIn = _gameManager.RestartRemaining;
                _restartCountdownLabel.text = restartIn > 0f
                    ? $"Next match in {Mathf.CeilToInt(restartIn)}s…"
                    : "Starting next match…";
            }
        }

        /// <summary>
        /// Returns the kill count for the player assigned to <paramref name="cornerIdx"/>.
        /// Matches by <c>InputAuthority.PlayerId</c> since PlayerId == cornerIdx for real
        /// players (set up that way by <see cref="GameManager.AssignBasesToPlayers"/>).
        /// Returns 0 if no stats object is found (bot corners, unspawned, etc.).
        /// </summary>
        private int GetKillsForCorner(int cornerIdx)
        {
            for (int i = 0; i < _matchStats.Length; i++)
            {
                var s = _matchStats[i];
                if (s == null || s.Object == null || !s.Object.IsValid) continue;
                if (s.Object.InputAuthority.PlayerId == cornerIdx) return s.Kills;
            }
            return 0;
        }

        private List<(int cornerIdx, float total)> BuildSortedLeaderboard()
        {
            var list = new List<(int, float)>(_bases.Length);
            for (int i = 0; i < _bases.Length; i++)
            {
                var b = _bases[i];
                if (b == null) continue;
                list.Add((b.CornerIndex, b.FoodTotal));
            }
            list.Sort((a, b) => b.Item2.CompareTo(a.Item2));
            return list;
        }

        // ========================================================================
        // UGUI BUILD
        // ========================================================================

        private void BuildCanvas()
        {
            var canvasGO = new GameObject("MatchHudCanvas",
                typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasGO.transform.SetParent(transform, worldPositionStays: false);

            var canvas = canvasGO.GetComponent<Canvas>();
            canvas.renderMode  = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 30; // below TouchControlsHud (50) — buttons on top

            var scaler = canvasGO.GetComponent<CanvasScaler>();
            scaler.uiScaleMode        = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = _referenceResolution;
            scaler.matchWidthOrHeight  = 0.5f;

            BuildHitFlash(canvasGO.transform);       // must be first (z-order: under everything)
            BuildLeaderboardPanel(canvasGO.transform);
            BuildTimerBadge(canvasGO.transform);
            BuildLocalStatsPanel(canvasGO.transform);
            BuildMatchEndOverlay(canvasGO.transform);
            BuildSessionEndOverlay(canvasGO.transform);
            BuildLobbyOverlay(canvasGO.transform);
            BuildIntroOverlay(canvasGO.transform);   // topmost — never intercepts input
        }

        // ---- Leaderboard (top-left) -------------------------------------------

        private void BuildLeaderboardPanel(Transform parent)
        {
            var panel = CreateUI("Leaderboard", parent, out var rt);
            rt.anchorMin        = new Vector2(0f, 1f);
            rt.anchorMax        = new Vector2(0f, 1f);
            rt.pivot            = new Vector2(0f, 1f);
            rt.sizeDelta        = new Vector2(272f, 220f);
            rt.anchoredPosition = new Vector2(12f, -12f);
            AddBackground(panel, new Color(DtPanelBg.r, DtPanelBg.g, DtPanelBg.b, 0.88f));

            // "LEADERBOARD" header
            var header = AddText(panel.transform, "Header", "LEADERBOARD", 18, TextAnchor.MiddleLeft, FontStyle.Bold);
            header.color = DtGold;
            var hRT = header.rectTransform;
            hRT.anchorMin        = new Vector2(0f, 1f);
            hRT.anchorMax        = new Vector2(1f, 1f);
            hRT.pivot            = new Vector2(0f, 1f);
            hRT.sizeDelta        = new Vector2(0f, 26f);
            hRT.anchoredPosition = new Vector2(10f, -6f);

            // Thin gold underline
            var lineGO = CreateUI("HeaderLine", panel.transform, out var lineRT);
            lineRT.anchorMin        = new Vector2(0f, 1f);
            lineRT.anchorMax        = new Vector2(1f, 1f);
            lineRT.pivot            = new Vector2(0f, 1f);
            lineRT.sizeDelta        = new Vector2(0f, 2f);
            lineRT.anchoredPosition = new Vector2(0f, -34f);
            var lineImg = lineGO.AddComponent<Image>();
            lineImg.color = new Color(DtGold.r, DtGold.g, DtGold.b, 0.5f);
            lineImg.raycastTarget = false;

            _lbRows = new LeaderboardRow[4];
            for (int i = 0; i < 4; i++)
                BuildLeaderboardRow(panel.transform, i);
        }

        private void BuildLeaderboardRow(Transform parent, int rowIdx)
        {
            const float rowH = 44f;
            float       yOff = -(40f + rowIdx * rowH);

            var row = CreateUI($"LBRow{rowIdx}", parent, out var rowRT);
            rowRT.anchorMin        = new Vector2(0f, 1f);
            rowRT.anchorMax        = new Vector2(1f, 1f);
            rowRT.pivot            = new Vector2(0f, 1f);
            rowRT.sizeDelta        = new Vector2(0f, rowH);
            rowRT.anchoredPosition = new Vector2(0f, yOff);

            // Alternating row tint for readability
            var rowBg = row.AddComponent<Image>();
            rowBg.color = new Color(0f, 0f, 0f, rowIdx % 2 == 0 ? 0.18f : 0.06f);
            rowBg.raycastTarget = false;

            // Color dot — tinted to player color in RefreshLeaderboard
            var dotGO = CreateUI("Dot", row.transform, out var dotRT);
            dotRT.anchorMin        = new Vector2(0f, 0.5f);
            dotRT.anchorMax        = new Vector2(0f, 0.5f);
            dotRT.pivot            = new Vector2(0.5f, 0.5f);
            dotRT.sizeDelta        = new Vector2(14f, 14f);
            dotRT.anchoredPosition = new Vector2(14f, 4f);
            var dotImg = dotGO.AddComponent<Image>();
            dotImg.color         = Color.grey;
            dotImg.raycastTarget = false;
            _lbRows[rowIdx].ColorDot = dotImg;

            // Rank + player label  (#1  P2)
            var nameLbl = AddText(row.transform, "Name", "—", 21, TextAnchor.MiddleLeft, FontStyle.Bold);
            nameLbl.color = DtTextPrimary;
            var nRT = nameLbl.rectTransform;
            nRT.anchorMin        = new Vector2(0f, 0f);
            nRT.anchorMax        = new Vector2(0f, 1f);
            nRT.pivot            = new Vector2(0f, 0.5f);
            nRT.sizeDelta        = new Vector2(106f, 0f);
            nRT.anchoredPosition = new Vector2(26f, 4f);
            _lbRows[rowIdx].PlayerLabel = nameLbl;

            // Food score (right-anchored, gold)
            var scoreLbl = AddText(row.transform, "Score", "0", 21, TextAnchor.MiddleRight, FontStyle.Bold);
            scoreLbl.color = DtGold;
            var sRT = scoreLbl.rectTransform;
            sRT.anchorMin        = new Vector2(1f, 0f);
            sRT.anchorMax        = new Vector2(1f, 1f);
            sRT.pivot            = new Vector2(1f, 0.5f);
            sRT.sizeDelta        = new Vector2(58f, 0f);
            sRT.anchoredPosition = new Vector2(-6f, 4f);
            _lbRows[rowIdx].ScoreLabel = scoreLbl;

            // Thin progress bar strip at row bottom
            var troughGO = CreateUI("BarTrough", row.transform, out var troughRT);
            troughRT.anchorMin        = new Vector2(0f, 0f);
            troughRT.anchorMax        = new Vector2(1f, 0f);
            troughRT.pivot            = new Vector2(0f, 0f);
            troughRT.sizeDelta        = new Vector2(-8f, 5f);
            troughRT.anchoredPosition = new Vector2(4f, 0f);
            var troughImg = troughGO.AddComponent<Image>();
            troughImg.color         = new Color(0f, 0f, 0f, 0.45f);
            troughImg.raycastTarget = false;

            var fillGO = CreateUI("Fill", troughGO.transform, out var fillRT);
            fillRT.anchorMin = Vector2.zero;
            fillRT.anchorMax = Vector2.one;
            fillRT.offsetMin = Vector2.zero;
            fillRT.offsetMax = Vector2.zero;
            var fillImg = fillGO.AddComponent<Image>();
            fillImg.color         = DtGreenMid;
            fillImg.type          = Image.Type.Filled;
            fillImg.fillMethod    = Image.FillMethod.Horizontal;
            fillImg.fillOrigin    = (int)Image.OriginHorizontal.Left;
            fillImg.fillAmount    = 0f;
            fillImg.raycastTarget = false;
            _lbRows[rowIdx].ProgressFill = fillImg;
        }

        // ---- Timer badge (top-right) ------------------------------------------

        private void BuildTimerBadge(Transform parent)
        {
            var badge = CreateUI("TimerBadge", parent, out var rt);
            rt.anchorMin        = new Vector2(1f, 1f);
            rt.anchorMax        = new Vector2(1f, 1f);
            rt.pivot            = new Vector2(1f, 1f);
            rt.sizeDelta        = new Vector2(192f, 64f);
            rt.anchoredPosition = new Vector2(-12f, -12f);
            AddBackground(badge, new Color(DtPanelBg.r, DtPanelBg.g, DtPanelBg.b, 0.88f));

            // Gold underline accent
            var lineGO = CreateUI("BottomLine", badge.transform, out var lineRT);
            lineRT.anchorMin        = new Vector2(0f, 0f);
            lineRT.anchorMax        = new Vector2(1f, 0f);
            lineRT.pivot            = new Vector2(0.5f, 0f);
            lineRT.sizeDelta        = new Vector2(0f, 3f);
            lineRT.anchoredPosition = Vector2.zero;
            lineGO.AddComponent<Image>().color = DtGold;

            _timerLabel = AddText(badge.transform, "Timer", "--:--", 42, TextAnchor.MiddleCenter, FontStyle.Bold);
            _timerLabel.color = DtGold;
            var tRT = _timerLabel.rectTransform;
            tRT.anchorMin = Vector2.zero;
            tRT.anchorMax = Vector2.one;
            tRT.offsetMin = Vector2.zero;
            tRT.offsetMax = Vector2.zero;
        }

        // ---- Local stats panel (bottom-left, above joystick) ------------------

        private void BuildLocalStatsPanel(Transform parent)
        {
            var panel = CreateUI("LocalStats", parent, out var rt);
            rt.anchorMin        = new Vector2(0f, 0f);
            rt.anchorMax        = new Vector2(0f, 0f);
            rt.pivot            = new Vector2(0f, 0f);
            rt.sizeDelta        = new Vector2(360f, 140f);
            rt.anchoredPosition = new Vector2(24f, 220f); // above joystick footprint
            AddBackground(panel, new Color(DtPanelBg.r, DtPanelBg.g, DtPanelBg.b, 0.72f));

            BuildBar(panel.transform, "HP",
                new Vector2(16f, -16f),
                new Color(0.95f, 0.30f, 0.25f, 1f),
                out _hpFill, out _hpLabel);

            BuildBar(panel.transform, "Cargo",
                new Vector2(16f, -70f),
                DtGoldMid,
                out _cargoFill, out _cargoLabel);
        }

        private void BuildBar(Transform parent, string name, Vector2 pos, Color fillColor,
            out Image fillOut, out Text labelOut)
        {
            var row = CreateUI(name + "Row", parent, out var rowRT);
            rowRT.anchorMin        = new Vector2(0f, 1f);
            rowRT.anchorMax        = new Vector2(0f, 1f);
            rowRT.pivot            = new Vector2(0f, 1f);
            rowRT.sizeDelta        = new Vector2(328f, 42f);
            rowRT.anchoredPosition = pos;

            var trough = CreateUI("Trough", row.transform, out var troughRT);
            troughRT.anchorMin = Vector2.zero;
            troughRT.anchorMax = Vector2.one;
            troughRT.offsetMin = Vector2.zero;
            troughRT.offsetMax = Vector2.zero;
            trough.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.35f);

            var fillGO = CreateUI("Fill", row.transform, out var fillRT);
            fillRT.anchorMin = Vector2.zero;
            fillRT.anchorMax = Vector2.one;
            fillRT.offsetMin = Vector2.zero;
            fillRT.offsetMax = Vector2.zero;
            fillOut               = fillGO.AddComponent<Image>();
            fillOut.color         = fillColor;
            fillOut.type          = Image.Type.Filled;
            fillOut.fillMethod    = Image.FillMethod.Horizontal;
            fillOut.fillOrigin    = (int)Image.OriginHorizontal.Left;
            fillOut.fillAmount    = 1f;
            fillOut.raycastTarget = false;

            labelOut = AddText(row.transform, name + "Label", name, 20, TextAnchor.MiddleCenter, FontStyle.Bold);
            var lblRT = labelOut.rectTransform;
            lblRT.anchorMin = Vector2.zero;
            lblRT.anchorMax = Vector2.one;
            lblRT.offsetMin = Vector2.zero;
            lblRT.offsetMax = Vector2.zero;
        }

        // ---- Match-end overlay ------------------------------------------------

        private void BuildMatchEndOverlay(Transform parent)
        {
            var panel = CreateUI("MatchEndPanel", parent, out var rt);
            rt.anchorMin        = new Vector2(0.5f, 0.5f);
            rt.anchorMax        = new Vector2(0.5f, 0.5f);
            rt.pivot            = new Vector2(0.5f, 0.5f);
            rt.sizeDelta        = new Vector2(640f, 460f);
            rt.anchoredPosition = Vector2.zero;
            AddBackground(panel, PanelColor(0.96f));
            _matchEndPanel = panel;

            // Gold top border
            var borderGO = CreateUI("TopBorder", panel.transform, out var borderRT);
            borderRT.anchorMin        = new Vector2(0f, 1f);
            borderRT.anchorMax        = new Vector2(1f, 1f);
            borderRT.pivot            = new Vector2(0.5f, 1f);
            borderRT.sizeDelta        = new Vector2(0f, 4f);
            borderRT.anchoredPosition = Vector2.zero;
            borderGO.AddComponent<Image>().color = DtGold;

            _matchEndTitle = AddText(panel.transform, "Title", "MATCH ENDED", 44, TextAnchor.MiddleCenter, FontStyle.Bold);
            _matchEndTitle.color = DtGold;
            var titleRT = _matchEndTitle.rectTransform;
            titleRT.anchorMin        = new Vector2(0f, 1f);
            titleRT.anchorMax        = new Vector2(1f, 1f);
            titleRT.pivot            = new Vector2(0.5f, 1f);
            titleRT.sizeDelta        = new Vector2(0f, 64f);
            titleRT.anchoredPosition = new Vector2(0f, -24f);

            _leaderboardRows = new Text[4];
            for (int i = 0; i < 4; i++)
            {
                var row = AddText(panel.transform, $"LB{i}", "", 28, TextAnchor.MiddleCenter, FontStyle.Bold);
                var rrt = row.rectTransform;
                rrt.anchorMin        = new Vector2(0f, 1f);
                rrt.anchorMax        = new Vector2(1f, 1f);
                rrt.pivot            = new Vector2(0.5f, 1f);
                rrt.sizeDelta        = new Vector2(0f, 48f);
                rrt.anchoredPosition = new Vector2(0f, -(108f + i * 54f));
                _leaderboardRows[i]  = row;
            }

            _restartCountdownLabel = AddText(panel.transform, "RestartCountdown", "",
                20, TextAnchor.MiddleCenter, FontStyle.Normal);
            _restartCountdownLabel.color = new Color(DtTextPrimary.r, DtTextPrimary.g, DtTextPrimary.b, 0.70f);
            var cdRT = _restartCountdownLabel.rectTransform;
            cdRT.anchorMin        = new Vector2(0f, 0f);
            cdRT.anchorMax        = new Vector2(1f, 0f);
            cdRT.pivot            = new Vector2(0.5f, 0f);
            cdRT.sizeDelta        = new Vector2(0f, 36f);
            cdRT.anchoredPosition = new Vector2(0f, 18f);

            panel.SetActive(false);
        }

        // ---- Session-end overlay ----------------------------------------------

        private void BuildSessionEndOverlay(Transform parent)
        {
            var panel = CreateUI("SessionEndPanel", parent, out var rt);
            rt.anchorMin        = new Vector2(0.5f, 0.5f);
            rt.anchorMax        = new Vector2(0.5f, 0.5f);
            rt.pivot            = new Vector2(0.5f, 0.5f);
            rt.sizeDelta        = new Vector2(620f, 220f);
            rt.anchoredPosition = Vector2.zero;
            AddBackground(panel, PanelColor(0.96f));
            _sessionEndPanel = panel;

            // Gold top border
            var borderGO = CreateUI("TopBorder", panel.transform, out var borderRT);
            borderRT.anchorMin        = new Vector2(0f, 1f);
            borderRT.anchorMax        = new Vector2(1f, 1f);
            borderRT.pivot            = new Vector2(0.5f, 1f);
            borderRT.sizeDelta        = new Vector2(0f, 4f);
            borderRT.anchoredPosition = Vector2.zero;
            borderGO.AddComponent<Image>().color = DtGold;

            var title = AddText(panel.transform, "Title", "SESSION ENDED", 40, TextAnchor.MiddleCenter, FontStyle.Bold);
            title.color = DtGold;
            var titleRT = title.rectTransform;
            titleRT.anchorMin        = new Vector2(0f, 1f);
            titleRT.anchorMax        = new Vector2(1f, 1f);
            titleRT.pivot            = new Vector2(0.5f, 1f);
            titleRT.sizeDelta        = new Vector2(0f, 56f);
            titleRT.anchoredPosition = new Vector2(0f, -20f);

            _sessionEndReason = AddText(panel.transform, "Reason", "", 22, TextAnchor.MiddleCenter, FontStyle.Normal);
            var reasonRT = _sessionEndReason.rectTransform;
            reasonRT.anchorMin        = new Vector2(0f, 1f);
            reasonRT.anchorMax        = new Vector2(1f, 1f);
            reasonRT.pivot            = new Vector2(0.5f, 1f);
            reasonRT.sizeDelta        = new Vector2(0f, 36f);
            reasonRT.anchoredPosition = new Vector2(0f, -90f);

            _sessionEndCountdown = AddText(panel.transform, "Countdown", "", 20, TextAnchor.MiddleCenter, FontStyle.Normal);
            var cdRT = _sessionEndCountdown.rectTransform;
            cdRT.anchorMin        = new Vector2(0f, 0f);
            cdRT.anchorMax        = new Vector2(1f, 0f);
            cdRT.pivot            = new Vector2(0.5f, 0f);
            cdRT.sizeDelta        = new Vector2(0f, 36f);
            cdRT.anchoredPosition = new Vector2(0f, 28f);

            panel.SetActive(false);
        }

        // ---- Lobby overlay ----------------------------------------------------

        private void BuildLobbyOverlay(Transform parent)
        {
            var panel = CreateUI("LobbyPanel", parent, out var rt);
            rt.anchorMin        = new Vector2(0.5f, 0.5f);
            rt.anchorMax        = new Vector2(0.5f, 0.5f);
            rt.pivot            = new Vector2(0.5f, 0.5f);
            rt.sizeDelta        = new Vector2(640f, 440f);
            rt.anchoredPosition = Vector2.zero;
            AddBackground(panel, PanelColor(0.96f));
            _lobbyPanel = panel;

            // Gold top border
            var borderGO = CreateUI("TopBorder", panel.transform, out var borderRT);
            borderRT.anchorMin        = new Vector2(0f, 1f);
            borderRT.anchorMax        = new Vector2(1f, 1f);
            borderRT.pivot            = new Vector2(0.5f, 1f);
            borderRT.sizeDelta        = new Vector2(0f, 4f);
            borderRT.anchoredPosition = Vector2.zero;
            borderGO.AddComponent<Image>().color = DtGold;

            var title = AddText(panel.transform, "Title", "MATCH LOBBY", 44, TextAnchor.MiddleCenter, FontStyle.Bold);
            title.color = DtGold;
            var titleRT = title.rectTransform;
            titleRT.anchorMin        = new Vector2(0f, 1f);
            titleRT.anchorMax        = new Vector2(1f, 1f);
            titleRT.pivot            = new Vector2(0.5f, 1f);
            titleRT.sizeDelta        = new Vector2(0f, 64f);
            titleRT.anchoredPosition = new Vector2(0f, -24f);

            _lobbyJoinCode = AddText(panel.transform, "JoinCode", "", 32, TextAnchor.MiddleCenter, FontStyle.Bold);
            _lobbyJoinCode.color = new Color(0.4f, 1f, 0.5f, 1f); // green for the shareable code
            var codeRT = _lobbyJoinCode.rectTransform;
            codeRT.anchorMin        = new Vector2(0f, 1f);
            codeRT.anchorMax        = new Vector2(1f, 1f);
            codeRT.pivot            = new Vector2(0.5f, 1f);
            codeRT.sizeDelta        = new Vector2(0f, 44f);
            codeRT.anchoredPosition = new Vector2(0f, -104f);

            _lobbyPlayerCount = AddText(panel.transform, "PlayerCount", "Players: 1",
                22, TextAnchor.MiddleCenter, FontStyle.Normal);
            var pcRT = _lobbyPlayerCount.rectTransform;
            pcRT.anchorMin        = new Vector2(0f, 1f);
            pcRT.anchorMax        = new Vector2(1f, 1f);
            pcRT.pivot            = new Vector2(0.5f, 1f);
            pcRT.sizeDelta        = new Vector2(0f, 30f);
            pcRT.anchoredPosition = new Vector2(0f, -162f);

            _lobbyHint = AddText(panel.transform, "Hint", "Waiting for host to start the match…",
                22, TextAnchor.MiddleCenter, FontStyle.Italic);
            _lobbyHint.color = new Color(DtTextPrimary.r, DtTextPrimary.g, DtTextPrimary.b, 0.75f);
            var hintRT = _lobbyHint.rectTransform;
            hintRT.anchorMin        = new Vector2(0f, 1f);
            hintRT.anchorMax        = new Vector2(1f, 1f);
            hintRT.pivot            = new Vector2(0.5f, 1f);
            hintRT.sizeDelta        = new Vector2(0f, 36f);
            hintRT.anchoredPosition = new Vector2(0f, -208f);

            // "Start Match" button — host only
            _lobbyStartButton = BuildButton(panel.transform, "StartMatch", "START MATCH",
                DtGreenMid,
                onClick: () =>
                {
                    var gm = GameManager.Instance;
                    if (gm != null)
                    {
                        _log?.Info(Source, "Lobby Start → GameManager.StartMatchNow().");
                        gm.StartMatchNow();
                    }
                });
            var btnRT = (RectTransform)_lobbyStartButton.transform;
            btnRT.anchorMin        = new Vector2(0.5f, 0f);
            btnRT.anchorMax        = new Vector2(0.5f, 0f);
            btnRT.pivot            = new Vector2(0.5f, 0f);
            btnRT.sizeDelta        = new Vector2(320f, 72f);
            btnRT.anchoredPosition = new Vector2(0f, 32f);

            panel.SetActive(false);
        }

        private Button BuildButton(Transform parent, string name, string label,
            Color baseColor, System.Action onClick)
        {
            var go  = CreateUI(name, parent, out _);
            var img = go.AddComponent<Image>();
            img.color         = baseColor;
            img.raycastTarget = true;

            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
            var cb = btn.colors;
            cb.normalColor      = baseColor;
            cb.highlightedColor = new Color(baseColor.r * 1.3f, baseColor.g * 1.2f, baseColor.b * 1.3f, baseColor.a);
            cb.pressedColor     = new Color(baseColor.r * 0.75f, baseColor.g * 0.75f, baseColor.b * 0.75f, baseColor.a);
            cb.selectedColor    = baseColor;
            cb.disabledColor    = new Color(baseColor.r * 0.5f, baseColor.g * 0.5f, baseColor.b * 0.5f, baseColor.a * 0.6f);
            btn.colors = cb;
            btn.onClick.AddListener(() => onClick?.Invoke());

            var labelText = AddText(go.transform, "Label", label, 26, TextAnchor.MiddleCenter, FontStyle.Bold);
            var labelRT   = labelText.rectTransform;
            labelRT.anchorMin = Vector2.zero;
            labelRT.anchorMax = Vector2.one;
            labelRT.offsetMin = Vector2.zero;
            labelRT.offsetMax = Vector2.zero;
            return btn;
        }

        // ---- Hit-flash (full-screen) ------------------------------------------

        private void BuildHitFlash(Transform parent)
        {
            var go = CreateUI("HitFlash", parent, out var rt);
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            _hitFlash               = go.AddComponent<Image>();
            _hitFlash.color         = new Color(0.95f, 0.20f, 0.20f, 0f);
            _hitFlash.raycastTarget = false;
        }

        // ---- Intro countdown (centered, topmost) ------------------------------

        private void BuildIntroOverlay(Transform parent)
        {
            _introLabel = AddText(parent, "IntroCountdown", "", 220, TextAnchor.MiddleCenter, FontStyle.Bold);
            _introLabel.color = DtGold;
            var rt = _introLabel.rectTransform;
            rt.anchorMin        = new Vector2(0.5f, 0.5f);
            rt.anchorMax        = new Vector2(0.5f, 0.5f);
            rt.pivot            = new Vector2(0.5f, 0.5f);
            rt.sizeDelta        = new Vector2(600f, 280f);
            rt.anchoredPosition = Vector2.zero;
            _introLabel.gameObject.SetActive(false);
        }

        // ---- Static UI helpers ------------------------------------------------

        private static GameObject CreateUI(string name, Transform parent, out RectTransform rt)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, worldPositionStays: false);
            rt = (RectTransform)go.transform;
            return go;
        }

        private static void AddBackground(GameObject go, Color color)
        {
            var img = go.AddComponent<Image>();
            img.color         = color;
            img.raycastTarget = false; // HUD doesn't eat clicks; TouchControlsHud owns input
        }

        private static Text AddText(Transform parent, string name, string content,
            int fontSize, TextAnchor alignment, FontStyle style)
        {
            var go   = CreateUI(name, parent, out _);
            var text = go.AddComponent<Text>();
            text.text              = content;
            text.fontSize          = fontSize;
            text.color             = DtTextPrimary;
            text.alignment         = alignment;
            text.fontStyle         = style;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow   = VerticalWrapMode.Overflow;
            text.raycastTarget      = false;

            var f = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (f == null) f = Resources.GetBuiltinResource<Font>("Arial.ttf");
            text.font = f;
            return text;
        }

        /// <summary>Warm dark wood panel color at the given alpha.</summary>
        private static Color PanelColor(float alpha) =>
            new Color(DtPanelBg.r, DtPanelBg.g, DtPanelBg.b, alpha);
    }
}

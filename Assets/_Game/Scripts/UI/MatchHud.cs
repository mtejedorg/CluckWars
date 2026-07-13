using System.Collections.Generic;
using CluckWars.Gameplay;
using CluckWars.Logging;
using CluckWars.Networking;
using CluckWars.Services;
using CluckWars.Audio;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
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

        private Text       _eventLabel;
        private GameObject _eventBannerPanel;
        private Text       _eventBannerText;
        private float      _bannerExpireTime;
        private MatchEventKind _lastSeenEvent = MatchEventKind.None;

        // ---- Cached scene refs -------------------------------------------------

        private ChickenController _localController;
        private ChickenCombat     _localCombat;
        private ChickenCargo      _localCargo;
        private PlayerBase[]      _bases     = System.Array.Empty<PlayerBase>();
        private GameManager       _gameManager;
        private float             _nextRefresh;

        // ---- Injected services -------------------------------------------------

        private INetworkService          _network;
        private ILogService              _log;
        private ColorSchemeSO            _colors;
        private MatchConfigSO            _matchConfig;
        private IAudioService            _audio;
        private AudioRegistrySO          _audioReg;

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
            IAudioService            audio,
            AudioRegistrySO          audioReg)
        {
            _network     = network;
            _log         = log;
            _colors      = colors;
            _matchConfig = matchConfig;
            _audio       = audio;
            _audioReg    = audioReg;
        }

        // ---- Unity lifecycle ---------------------------------------------------

        private void Awake()
        {
            if (_network == null) ProjectContext.Instance.Container.Inject(this);

            DisableLegacyCargoHud();
            EnsureEventSystem();
            BuildCanvas();
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

        // ---- Lifecycle tick ----------------------------------------------------

        private void Update()
        {
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
            TickHitFlash();
            PollComebackEvent();
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

                row.PlayerLabel.text  = $"{Ordinal(i)}  P{cornerIdx + 1}";
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

                    _cargoLabel.text = _localCargo.IsDepositing
                        ? "DEPOSITING..."
                        : fraction >= 1f
                            ? "FULL  →  RETURN TO BASE!"
                            : $"Cargo  {Mathf.FloorToInt(cur)} / {Mathf.FloorToInt(cap)}";
                    _cargoLabel.color = (fraction >= 1f || _localCargo.IsDepositing)
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

        /// <summary>0-based rank → "1st"/"2nd"/"3rd"/"4th" (design v3 leaderboard).</summary>
        private static string Ordinal(int zeroBasedRank) => zeroBasedRank switch
        {
            0 => "1st",
            1 => "2nd",
            2 => "3rd",
            _ => $"{zeroBasedRank + 1}th",
        };

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
            BuildEventBanner(canvasGO.transform);
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

            BuildTargetBadge(panel.transform);
        }

        /// <summary>
        /// "★ FIRST TO N" win-target badge sitting just under the leaderboard panel
        /// (design v3 HUD). N pulls from <see cref="MatchConfigSO.FoodTargetToWin"/>.
        /// </summary>
        private void BuildTargetBadge(Transform leaderboardPanel)
        {
            int goal = _matchConfig != null ? Mathf.Max(1, Mathf.RoundToInt(_matchConfig.FoodTargetToWin)) : 150;

            var badge = CreateUI("TargetBadge", leaderboardPanel, out var rt);
            rt.anchorMin        = new Vector2(0f, 0f);
            rt.anchorMax        = new Vector2(1f, 0f);
            rt.pivot            = new Vector2(0.5f, 1f);
            rt.sizeDelta        = new Vector2(-12f, 30f);
            rt.anchoredPosition = new Vector2(0f, -6f);
            var bg = badge.AddComponent<Image>();
            bg.sprite = UiGfx.Rounded(8);
            bg.type   = Image.Type.Sliced;
            bg.pixelsPerUnitMultiplier = 1f;
            bg.color = new Color(DtGold.r, DtGold.g, DtGold.b, 0.14f);
            bg.raycastTarget = false;

            var label = AddText(badge.transform, "Label", $"FIRST TO {goal}", 18,
                TextAnchor.MiddleCenter, FontStyle.Bold);
            label.color = DtGold;
            var lRT = label.rectTransform;
            lRT.anchorMin = Vector2.zero;
            lRT.anchorMax = Vector2.one;
            lRT.offsetMin = Vector2.zero;
            lRT.offsetMax = Vector2.zero;
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

            _eventLabel = AddText(badge.transform, "EventLabel", "", 12, TextAnchor.MiddleCenter, FontStyle.Bold);
            _eventLabel.color = DtGold;
            var eRT = _eventLabel.rectTransform;
            eRT.anchorMin = new Vector2(0f, 0f);
            eRT.anchorMax = new Vector2(1f, 0f);
            eRT.pivot = new Vector2(0.5f, 1f);
            eRT.sizeDelta = new Vector2(192f, 20f);
            eRT.anchoredPosition = new Vector2(0f, -6f);
            _eventLabel.gameObject.SetActive(false);
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

        // ---- Static UI helpers ------------------------------------------------

        private static GameObject CreateUI(string name, Transform parent, out RectTransform rt)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, worldPositionStays: false);
            rt = (RectTransform)go.transform;
            return go;
        }

        private static void AddBackground(GameObject go, Color color, float radius = 12f, Color? borderColor = null, float borderWidth = 0f)
        {
            var img = go.AddComponent<Image>();
            img.color         = color;
            img.raycastTarget = false; // HUD doesn't eat clicks; TouchControlsHud owns input
            
            go.AddComponent<SDFImageEffect>();
            var mat = new Material(Shader.Find("CluckWars/UI/SDF"));
            mat.SetColor("_Color", Color.white);
            mat.SetFloat("_Radius", radius);
            if (borderColor.HasValue) {
                mat.SetColor("_BorderColor", borderColor.Value);
                mat.SetFloat("_BorderWidth", borderWidth);
            }
            img.material = mat;
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
            text.font               = UiGfx.ChunkyFont();
            UiGfx.AddShadow(text);
            return text;
        }

        private void BuildEventBanner(Transform parent)
        {
            _eventBannerPanel = CreateUI("EventBanner", parent, out var rt);
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(600f, 80f);
            rt.anchoredPosition = new Vector2(0f, 150f);

            AddBackground(_eventBannerPanel, new Color(DtPanelBg.r, DtPanelBg.g, DtPanelBg.b, 0.95f));

            var lineGO = CreateUI("BottomLine", _eventBannerPanel.transform, out var lineRT);
            lineRT.anchorMin = new Vector2(0f, 0f);
            lineRT.anchorMax = new Vector2(1f, 0f);
            lineRT.pivot = new Vector2(0.5f, 0f);
            lineRT.sizeDelta = new Vector2(0f, 3f);
            lineRT.anchoredPosition = Vector2.zero;
            lineGO.AddComponent<Image>().color = DtGold;

            _eventBannerText = AddText(_eventBannerPanel.transform, "Label", "", 24, TextAnchor.MiddleCenter, FontStyle.Bold);
            _eventBannerText.color = DtGold;
            var tRT = _eventBannerText.rectTransform;
            tRT.anchorMin = Vector2.zero;
            tRT.anchorMax = Vector2.one;
            tRT.offsetMin = Vector2.zero;
            tRT.offsetMax = Vector2.zero;

            _eventBannerPanel.SetActive(false);
        }

        private void PollComebackEvent()
        {
            if (_gameManager == null) return;

            MatchEventKind evt = _gameManager.ActiveEvent;
            if (evt != _lastSeenEvent)
            {
                _lastSeenEvent = evt;
                if (evt != MatchEventKind.None)
                {
                    string bannerName = evt switch
                    {
                        MatchEventKind.GoldenPile => "GOLDEN PILE",
                        MatchEventKind.UnderdogSurge => "UNDERDOG SURGE",
                        MatchEventKind.LeaderBounty => "BOUNTY ON THE LEADER",
                        MatchEventKind.Restock => "RESTOCK",
                        _ => evt.ToString().ToUpper()
                    };

                    if (_eventBannerText != null)
                    {
                        _eventBannerText.text = $"FINAL MINUTE: {bannerName}!";
                    }
                    if (_eventBannerPanel != null)
                    {
                        _eventBannerPanel.SetActive(true);
                    }
                    _bannerExpireTime = Time.unscaledTime + 3f;

                    _audio?.PlaySFX(_audioReg != null ? _audioReg.MatchStart : null);
                }
                else
                {
                    if (_eventBannerPanel != null) _eventBannerPanel.SetActive(false);
                }
            }

            if (_eventBannerPanel != null && _eventBannerPanel.activeSelf && Time.unscaledTime >= _bannerExpireTime)
            {
                _eventBannerPanel.SetActive(false);
            }

            if (_eventLabel != null)
            {
                if (evt != MatchEventKind.None)
                {
                    string labelName = evt switch
                    {
                        MatchEventKind.GoldenPile => "GOLDEN PILE",
                        MatchEventKind.UnderdogSurge => "UNDERDOG SURGE",
                        MatchEventKind.LeaderBounty => "BOUNTY ON THE LEADER",
                        MatchEventKind.Restock => "RESTOCK",
                        _ => evt.ToString().ToUpper()
                    };
                    _eventLabel.text = labelName;
                    _eventLabel.gameObject.SetActive(true);
                }
                else
                {
                    _eventLabel.gameObject.SetActive(false);
                }
            }
        }
    }
}

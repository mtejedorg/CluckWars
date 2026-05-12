using System.Collections.Generic;
using CluckWars.Gameplay;
using CluckWars.Logging;
using CluckWars.Networking;
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
    /// UGUI replacement for the IMGUI <c>CargoHud</c>. Builds the entire match
    /// HUD procedurally on Awake so Game.unity stays a single drop-in component.
    /// Implements the ART.md §6.1 layout:
    /// <list type="bullet">
    ///   <item>Top bar: match timer (center) + per-player food totals (right).</item>
    ///   <item>Bottom-left panel: local HP bar + cargo gauge.</item>
    ///   <item>Centered match-end overlay with sorted leaderboard + restart countdown.</item>
    ///   <item>Centered session-end overlay on disconnect, with auto-return to Bootstrap.</item>
    /// </list>
    /// </summary>
    /// <remarks>
    /// Auto-disables any sibling <c>CargoHud</c> on Awake so the two HUDs don't
    /// overlap during migration. Maestro can delete the CargoHud GameObject
    /// once happy with the UGUI replacement.
    /// </remarks>
    public sealed class MatchHud : MonoBehaviour
    {
        private const string Source = "MatchHud";

        [SerializeField] private float _refreshInterval = 0.25f;
        [SerializeField] private Vector2 _referenceResolution = new Vector2(1920f, 1080f);

        [Tooltip("Seconds the 'Session ended' overlay stays up before auto-returning to the Bootstrap scene.")]
        [Min(0.5f)]
        [SerializeField] private float _disconnectReturnDelay = 5f;

        [Tooltip("Bootstrap scene name to load after a disconnect.")]
        [SerializeField] private string _bootstrapSceneName = "Bootstrap";

        // 4 high-contrast per-player identity colors (ART.md §6).
        private static readonly Color[] PlayerColors = new[]
        {
            new Color(0.95f, 0.30f, 0.25f, 1f), // Red
            new Color(0.30f, 0.55f, 0.95f, 1f), // Blue
            new Color(0.30f, 0.75f, 0.30f, 1f), // Green
            new Color(0.95f, 0.85f, 0.25f, 1f), // Yellow
        };

        // UI references built procedurally.
        private Text _timerLabel;
        private Text[] _playerTotalLabels;
        private Image _hpFill;
        private Text _hpLabel;
        private Image _cargoFill;
        private Text _cargoLabel;
        private GameObject _matchEndPanel;
        private Text _matchEndTitle;
        private Text[] _leaderboardRows;
        private Text _restartCountdownLabel;
        private GameObject _sessionEndPanel;
        private Text _sessionEndReason;
        private Text _sessionEndCountdown;
        private Text _introLabel;

        // Cached scene refs.
        private ChickenController _localController;
        private ChickenCombat _localCombat;
        private ChickenCargo _localCargo;
        private PlayerBase[] _bases = System.Array.Empty<PlayerBase>();
        private GameManager _gameManager;
        private float _nextRefresh;

        // Disconnect state.
        private INetworkService _network;
        private ILogService _log;
        private ColorSchemeSO _colors;
        private ShutdownReason? _shutdownReason;
        private float _shutdownAtUnscaledTime;
        private bool _returnTriggered;

        // Intro "GO!" flourish — lingers briefly after the countdown hits zero.
        private float _goExpiresAtUnscaledTime;
        private const float GoFlourishDuration = 0.6f;

        [Inject]
        public void Construct(INetworkService network, ILogService log, ColorSchemeSO colors)
        {
            _network = network;
            _log = log;
            _colors = colors;
        }

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
            _shutdownReason = reason;
            _shutdownAtUnscaledTime = Time.unscaledTime;
            _log?.Warn(Source, $"Network shutdown observed: {reason}. Returning to '{_bootstrapSceneName}' in {_disconnectReturnDelay}s.");
        }

        // ---- Lifecycle ---------------------------------------------------------

        private void Update()
        {
            // Disconnect path overrides everything else.
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
                // Still tick the dynamic labels even between refreshes — cheap.
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
            if (_matchEndPanel != null) _matchEndPanel.SetActive(false);

            float elapsed = Time.unscaledTime - _shutdownAtUnscaledTime;
            float remaining = Mathf.Max(0f, _disconnectReturnDelay - elapsed);
            if (_sessionEndReason != null) _sessionEndReason.text = $"Reason: {_shutdownReason}";
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
                _localCombat = null;
                _localCargo = null;
                var all = FindObjectsByType<ChickenController>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
                for (int i = 0; i < all.Length; i++)
                {
                    var c = all[i];
                    if (c.Object == null || !c.Object.IsValid) continue;
                    if (!c.HasInputAuthority) continue;
                    _localController = c;
                    _localCombat = c.Combat;
                    _localCargo = c.Cargo;
                    break;
                }
            }

            if (_bases.Length == 0 || HasStaleBase())
            {
                _bases = FindObjectsByType<PlayerBase>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            }

            if (_gameManager == null || _gameManager.Object == null || !_gameManager.Object.IsValid)
            {
                _gameManager = FindFirstObjectByType<GameManager>();
            }
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

        // ---- Per-frame data → UI ----------------------------------------------

        private void RefreshDynamic()
        {
            RefreshTimer();
            RefreshPlayerTotals();
            RefreshLocalStats();
            RefreshMatchEndOverlay();
            RefreshIntroOverlay();
        }

        private void RefreshIntroOverlay()
        {
            if (_introLabel == null) return;
            if (_gameManager == null)
            {
                _introLabel.gameObject.SetActive(false);
                return;
            }

            if (_gameManager.IsIntroActive)
            {
                float remaining = _gameManager.IntroRemaining;
                // Show the next-whole-second so 2.9 → "3", 1.9 → "2", etc.
                int displayed = Mathf.CeilToInt(remaining);
                _introLabel.text = displayed.ToString();
                _introLabel.gameObject.SetActive(true);
                _goExpiresAtUnscaledTime = Time.unscaledTime + GoFlourishDuration;
                return;
            }

            // Intro just ended — flash "GO!" briefly before hiding.
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

        private void RefreshTimer()
        {
            if (_timerLabel == null) return;
            if (_gameManager == null)
            {
                _timerLabel.text = "--:--";
                return;
            }

            switch (_gameManager.State)
            {
                case MatchState.Active:
                    float r = _gameManager.TimeRemaining;
                    int mm = Mathf.Max(0, Mathf.FloorToInt(r / 60f));
                    int ss = Mathf.Max(0, Mathf.FloorToInt(r - mm * 60f));
                    _timerLabel.text = $"{mm:00}:{ss:00}";
                    break;
                case MatchState.Ended:
                    _timerLabel.text = "ENDED";
                    break;
                default:
                    _timerLabel.text = "WAITING";
                    break;
            }
        }

        private void RefreshPlayerTotals()
        {
            for (int i = 0; i < _playerTotalLabels.Length; i++)
            {
                var label = _playerTotalLabels[i];
                if (label == null) continue;

                // Match base by corner index to the slot.
                PlayerBase b = null;
                for (int j = 0; j < _bases.Length; j++)
                {
                    if (_bases[j] != null && _bases[j].CornerIndex == i) { b = _bases[j]; break; }
                }
                if (b == null)
                {
                    label.text = $"P{i + 1}: —";
                    label.color = new Color(0.6f, 0.6f, 0.6f, 0.6f);
                    continue;
                }
                label.text = $"P{i + 1}: {Mathf.FloorToInt(b.FoodTotal)}";
                label.color = PlayerColors[i % PlayerColors.Length];
            }
        }

        private void RefreshLocalStats()
        {
            // HP
            if (_hpFill != null && _hpLabel != null)
            {
                if (_localCombat != null && _localController != null && _localController.Stats != null)
                {
                    float max = Mathf.Max(1f, _localController.Stats.MaxHP);
                    float hp = Mathf.Max(0f, _localCombat.HP);
                    _hpFill.fillAmount = Mathf.Clamp01(hp / max);
                    _hpLabel.text = $"HP {Mathf.CeilToInt(hp)} / {Mathf.CeilToInt(max)}";
                }
                else
                {
                    _hpFill.fillAmount = 0f;
                    _hpLabel.text = "HP —";
                }
            }

            // Cargo
            if (_cargoFill != null && _cargoLabel != null)
            {
                if (_localCargo != null)
                {
                    float cap = Mathf.Max(1f, _localCargo.Capacity);
                    float cur = Mathf.Max(0f, _localCargo.Cargo);
                    _cargoFill.fillAmount = Mathf.Clamp01(cur / cap);
                    _cargoLabel.text = $"Cargo {Mathf.FloorToInt(cur)} / {Mathf.FloorToInt(cap)}";
                }
                else
                {
                    _cargoFill.fillAmount = 0f;
                    _cargoLabel.text = "Cargo —";
                }
            }
        }

        private void RefreshMatchEndOverlay()
        {
            bool show = _gameManager != null && _gameManager.State == MatchState.Ended;
            if (_matchEndPanel != null) _matchEndPanel.SetActive(show);
            if (!show) return;

            // Title
            if (_matchEndTitle != null)
            {
                var winner = _gameManager.WinnerPlayer;
                _matchEndTitle.text = winner.IsRealPlayer
                    ? $"PLAYER {winner.PlayerId + 1} WINS"
                    : "MATCH ENDED";
            }

            // Sorted leaderboard
            var rows = BuildSortedLeaderboard();
            for (int i = 0; i < _leaderboardRows.Length; i++)
            {
                var row = _leaderboardRows[i];
                if (row == null) continue;
                if (i >= rows.Count)
                {
                    row.text = "";
                    continue;
                }
                var (cornerIdx, total) = rows[i];
                row.text = $"P{cornerIdx + 1}:  {Mathf.FloorToInt(total)} food";
                row.color = PlayerColors[cornerIdx % PlayerColors.Length];
            }

            // Restart countdown
            if (_restartCountdownLabel != null)
            {
                float restartIn = _gameManager.RestartRemaining;
                _restartCountdownLabel.text = restartIn > 0f
                    ? $"Next match in {Mathf.CeilToInt(restartIn)}s…"
                    : "Starting next match…";
            }
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

        // ---- UGUI build --------------------------------------------------------

        private void BuildCanvas()
        {
            var canvasGO = new GameObject("MatchHudCanvas",
                typeof(Canvas),
                typeof(CanvasScaler),
                typeof(GraphicRaycaster));
            canvasGO.transform.SetParent(transform, worldPositionStays: false);

            var canvas = canvasGO.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 30; // below TouchControlsHud (50) — buttons on top

            var scaler = canvasGO.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = _referenceResolution;
            scaler.matchWidthOrHeight = 0.5f;

            BuildTopBar(canvasGO.transform);
            BuildLocalStatsPanel(canvasGO.transform);
            BuildMatchEndOverlay(canvasGO.transform);
            BuildSessionEndOverlay(canvasGO.transform);
            BuildIntroOverlay(canvasGO.transform);
        }

        private void BuildIntroOverlay(Transform parent)
        {
            // Big centered number / "GO!" that shows for the first few seconds
            // of the match. Layered on top of the rest of the HUD; doesn't
            // intercept input so the touch buttons keep working underneath.
            _introLabel = AddText(parent, "IntroCountdown", "", 220, TextAnchor.MiddleCenter, FontStyle.Bold);
            var rt = _introLabel.rectTransform;
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot     = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(600f, 280f);
            rt.anchoredPosition = Vector2.zero;
            _introLabel.color = new Color(1f, 0.95f, 0.8f, 1f);
            _introLabel.gameObject.SetActive(false);
        }

        private void BuildTopBar(Transform parent)
        {
            var bar = CreateUI("TopBar", parent, out var rt);
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot     = new Vector2(0.5f, 1f);
            rt.sizeDelta = new Vector2(0f, 80f);
            rt.anchoredPosition = Vector2.zero;
            AddBackground(bar, PanelColor(0.55f));

            // Timer (center)
            _timerLabel = AddText(bar.transform, "Timer", "--:--", 44, TextAnchor.MiddleCenter, FontStyle.Bold);
            var tRT = _timerLabel.rectTransform;
            tRT.anchorMin = new Vector2(0.5f, 0f);
            tRT.anchorMax = new Vector2(0.5f, 1f);
            tRT.pivot     = new Vector2(0.5f, 0.5f);
            tRT.sizeDelta = new Vector2(320f, 0f);
            tRT.anchoredPosition = Vector2.zero;

            // Per-player totals (right side)
            _playerTotalLabels = new Text[4];
            for (int i = 0; i < 4; i++)
            {
                var pl = AddText(bar.transform, $"P{i + 1}Total", $"P{i + 1}: 0", 24, TextAnchor.MiddleRight, FontStyle.Bold);
                var rrt = pl.rectTransform;
                rrt.anchorMin = new Vector2(1f, 0f);
                rrt.anchorMax = new Vector2(1f, 1f);
                rrt.pivot     = new Vector2(1f, 0.5f);
                rrt.sizeDelta = new Vector2(160f, 0f);
                // Stack right-to-left: P1 farthest right, P4 closest to center.
                // Reversed so P1 is leftmost of the group? Either reads — pick one.
                rrt.anchoredPosition = new Vector2(-(20f + i * 160f), 0f);
                pl.color = PlayerColors[i];
                _playerTotalLabels[i] = pl;
            }
        }

        private void BuildLocalStatsPanel(Transform parent)
        {
            var panel = CreateUI("LocalStats", parent, out var rt);
            rt.anchorMin = new Vector2(0f, 0f);
            rt.anchorMax = new Vector2(0f, 0f);
            rt.pivot     = new Vector2(0f, 0f);
            rt.sizeDelta = new Vector2(360f, 140f);
            rt.anchoredPosition = new Vector2(24f, 220f); // above the joystick footprint
            AddBackground(panel, PanelColor(0.55f));

            // HP bar
            BuildBar(panel.transform, "HP",
                new Vector2(16f, -16f),
                new Color(0.95f, 0.30f, 0.25f, 1f),
                out _hpFill, out _hpLabel);

            // Cargo bar (below HP)
            BuildBar(panel.transform, "Cargo",
                new Vector2(16f, -70f),
                new Color(0.95f, 0.65f, 0.20f, 1f),
                out _cargoFill, out _cargoLabel);
        }

        private void BuildBar(Transform parent, string name, Vector2 anchoredPosition, Color fillColor, out Image fillOut, out Text labelOut)
        {
            // Container row (background trough + foreground fill + label overlay)
            var row = CreateUI(name + "Row", parent, out var rowRT);
            rowRT.anchorMin = new Vector2(0f, 1f);
            rowRT.anchorMax = new Vector2(0f, 1f);
            rowRT.pivot     = new Vector2(0f, 1f);
            rowRT.sizeDelta = new Vector2(328f, 42f);
            rowRT.anchoredPosition = anchoredPosition;

            var trough = CreateUI("Trough", row.transform, out var troughRT);
            troughRT.anchorMin = Vector2.zero;
            troughRT.anchorMax = Vector2.one;
            troughRT.offsetMin = Vector2.zero;
            troughRT.offsetMax = Vector2.zero;
            var troughImg = trough.AddComponent<Image>();
            troughImg.color = new Color(0f, 0f, 0f, 0.35f);

            var fillGO = CreateUI("Fill", row.transform, out var fillRT);
            fillRT.anchorMin = Vector2.zero;
            fillRT.anchorMax = Vector2.one;
            fillRT.offsetMin = Vector2.zero;
            fillRT.offsetMax = Vector2.zero;
            fillOut = fillGO.AddComponent<Image>();
            fillOut.color = fillColor;
            fillOut.type = Image.Type.Filled;
            fillOut.fillMethod = Image.FillMethod.Horizontal;
            fillOut.fillOrigin = (int)Image.OriginHorizontal.Left;
            fillOut.fillAmount = 1f;
            fillOut.raycastTarget = false;

            labelOut = AddText(row.transform, name + "Label", name, 20, TextAnchor.MiddleCenter, FontStyle.Bold);
            var lblRT = labelOut.rectTransform;
            lblRT.anchorMin = Vector2.zero;
            lblRT.anchorMax = Vector2.one;
            lblRT.offsetMin = Vector2.zero;
            lblRT.offsetMax = Vector2.zero;
        }

        private void BuildMatchEndOverlay(Transform parent)
        {
            var panel = CreateUI("MatchEndPanel", parent, out var rt);
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot     = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(620f, 420f);
            rt.anchoredPosition = Vector2.zero;
            AddBackground(panel, PanelColor(0.92f));
            _matchEndPanel = panel;

            _matchEndTitle = AddText(panel.transform, "Title", "MATCH ENDED", 44, TextAnchor.MiddleCenter, FontStyle.Bold);
            var titleRT = _matchEndTitle.rectTransform;
            titleRT.anchorMin = new Vector2(0f, 1f);
            titleRT.anchorMax = new Vector2(1f, 1f);
            titleRT.pivot     = new Vector2(0.5f, 1f);
            titleRT.sizeDelta = new Vector2(0f, 64f);
            titleRT.anchoredPosition = new Vector2(0f, -24f);

            // Leaderboard rows
            _leaderboardRows = new Text[4];
            for (int i = 0; i < 4; i++)
            {
                var row = AddText(panel.transform, $"LB{i}", "", 28, TextAnchor.MiddleCenter, FontStyle.Bold);
                var rrt = row.rectTransform;
                rrt.anchorMin = new Vector2(0f, 1f);
                rrt.anchorMax = new Vector2(1f, 1f);
                rrt.pivot     = new Vector2(0.5f, 1f);
                rrt.sizeDelta = new Vector2(0f, 44f);
                rrt.anchoredPosition = new Vector2(0f, -(110f + i * 50f));
                _leaderboardRows[i] = row;
            }

            _restartCountdownLabel = AddText(panel.transform, "RestartCountdown", "", 20, TextAnchor.MiddleCenter, FontStyle.Normal);
            var cdRT = _restartCountdownLabel.rectTransform;
            cdRT.anchorMin = new Vector2(0f, 0f);
            cdRT.anchorMax = new Vector2(1f, 0f);
            cdRT.pivot     = new Vector2(0.5f, 0f);
            cdRT.sizeDelta = new Vector2(0f, 36f);
            cdRT.anchoredPosition = new Vector2(0f, 18f);

            panel.SetActive(false);
        }

        private void BuildSessionEndOverlay(Transform parent)
        {
            var panel = CreateUI("SessionEndPanel", parent, out var rt);
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot     = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(620f, 220f);
            rt.anchoredPosition = Vector2.zero;
            AddBackground(panel, PanelColor(0.92f));
            _sessionEndPanel = panel;

            var title = AddText(panel.transform, "Title", "SESSION ENDED", 40, TextAnchor.MiddleCenter, FontStyle.Bold);
            var titleRT = title.rectTransform;
            titleRT.anchorMin = new Vector2(0f, 1f);
            titleRT.anchorMax = new Vector2(1f, 1f);
            titleRT.pivot     = new Vector2(0.5f, 1f);
            titleRT.sizeDelta = new Vector2(0f, 56f);
            titleRT.anchoredPosition = new Vector2(0f, -20f);

            _sessionEndReason = AddText(panel.transform, "Reason", "", 22, TextAnchor.MiddleCenter, FontStyle.Normal);
            var reasonRT = _sessionEndReason.rectTransform;
            reasonRT.anchorMin = new Vector2(0f, 1f);
            reasonRT.anchorMax = new Vector2(1f, 1f);
            reasonRT.pivot     = new Vector2(0.5f, 1f);
            reasonRT.sizeDelta = new Vector2(0f, 36f);
            reasonRT.anchoredPosition = new Vector2(0f, -90f);

            _sessionEndCountdown = AddText(panel.transform, "Countdown", "", 20, TextAnchor.MiddleCenter, FontStyle.Normal);
            var cdRT = _sessionEndCountdown.rectTransform;
            cdRT.anchorMin = new Vector2(0f, 0f);
            cdRT.anchorMax = new Vector2(1f, 0f);
            cdRT.pivot     = new Vector2(0.5f, 0f);
            cdRT.sizeDelta = new Vector2(0f, 36f);
            cdRT.anchoredPosition = new Vector2(0f, 28f);

            panel.SetActive(false);
        }

        // ---- UI helpers --------------------------------------------------------

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
            img.color = color;
            img.raycastTarget = false; // HUD doesn't intercept clicks; the touch HUD owns input
        }

        private static Text AddText(Transform parent, string name, string content, int fontSize, TextAnchor alignment, FontStyle style)
        {
            var go = CreateUI(name, parent, out _);
            var text = go.AddComponent<Text>();
            text.text = content;
            text.fontSize = fontSize;
            text.color = new Color(0.95f, 0.95f, 0.95f, 1f);
            text.alignment = alignment;
            text.fontStyle = style;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.raycastTarget = false;

            var f = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (f == null) f = Resources.GetBuiltinResource<Font>("Arial.ttf");
            text.font = f;
            return text;
        }

        private Color PanelColor(float alpha)
        {
            var c = _colors != null ? _colors.PanelBackground : new Color(0.08f, 0.08f, 0.10f, 1f);
            c.a = alpha;
            return c;
        }
    }
}

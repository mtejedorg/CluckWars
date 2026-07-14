using System.Collections.Generic;
using CluckWars.Gameplay;
using CluckWars.Logging;
using UnityEngine;
using UnityEngine.UIElements;
using Zenject;

namespace CluckWars.UI
{
    /// <summary>
    /// UI Toolkit driver for the match top-bar HUD (Stage 2a + Stage A UI rebuild):
    /// ranked leaderboard panel + win target + the embossed match timer. Layout lives in
    /// <c>Assets/UI/MatchTopBar.uxml</c>; styling in
    /// <c>Assets/UI/Styles/MatchTopBar.uss</c>.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class MatchHudController : MonoBehaviour
    {
        private const string Source = "MatchHud";

        /// <summary>
        /// Lets <see cref="MatchOverlaysController"/> (a separate UIDocument/controller)
        /// dim the top bar during the intro countdown (ART.md §6.5: "Player badges +
        /// timer visible at 40% opacity behind the overlay"). Same lookup pattern as
        /// <see cref="CluckWars.Input.TouchControlsController"/>.
        /// </summary>
        public static MatchHudController Instance { get; private set; }

        [SerializeField] private float _refreshInterval = 0.25f;

        // Okabe-Ito per-player identity colors (ART.md §6).
        private static readonly Color[] PlayerColors =
        {
            new Color(0.91f, 0.46f, 0.10f, 1f), // P1 Orange #E8751A
            new Color(0.10f, 0.50f, 0.77f, 1f), // P2 Blue   #1A7FC4
            new Color(0.77f, 0.16f, 0.44f, 1f), // P3 Pink   #C4286F
            new Color(0.05f, 0.62f, 0.48f, 1f), // P4 Teal   #0D9E7A
        };

        private ILogService _log;
        private MatchConfigSO _matchConfig;

        private VisualElement _root;
        private VisualElement _topBarRoot;
        private bool          _bound;
        private bool          _introDimmed;

        private struct LbRow
        {
            public VisualElement Root;
            public Label         Ord;
            public VisualElement Dot;
            public Label         Name;
            public VisualElement BarFill;
            public Label         Score;
        }

        private readonly LbRow[] _lbRows = new LbRow[4];
        private Label            _timer;
        private Label            _winTargetBadge;

        private PlayerBase[] _bases = System.Array.Empty<PlayerBase>();
        private GameManager  _gameManager;
        private float        _nextRefresh;

        [Inject]
        public void Construct(ILogService log, MatchConfigSO matchConfig)
        {
            _log = log;
            _matchConfig = matchConfig;
        }

        private void Awake()
        {
            Instance = this;

            if (_log == null)
            {
                var sceneCtx = FindFirstObjectByType<SceneContext>();
                if (sceneCtx != null) sceneCtx.Container.Inject(this);
                else                  ProjectContext.Instance.Container.Inject(this);
            }
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        private void OnEnable() => TryBind();

        private void TryBind()
        {
            var doc = GetComponent<UIDocument>();
            _root = doc != null ? doc.rootVisualElement : null;
            if (_root == null) return;

            _topBarRoot = _root.Q<VisualElement>("TopBarRoot");

            for (int i = 0; i < 4; i++)
            {
                _lbRows[i] = new LbRow
                {
                    Root    = _root.Q<VisualElement>($"LbRow{i}"),
                    Ord     = _root.Q<Label>($"LbOrd{i}"),
                    Dot     = _root.Q<VisualElement>($"LbDot{i}"),
                    Name    = _root.Q<Label>($"LbName{i}"),
                    BarFill = _root.Q<VisualElement>($"LbBarFill{i}"),
                    Score   = _root.Q<Label>($"LbScore{i}"),
                };
            }
            _timer = _root.Q<Label>("MatchTimer");
            _winTargetBadge = _root.Q<Label>("WinTargetBadge");

            if (_winTargetBadge != null)
            {
                int target = _matchConfig != null ? Mathf.Max(1, _matchConfig.FoodTargetToWin) : 150;
                _winTargetBadge.text = $"★ FIRST TO {target}";
            }

            _bound = true;
            ApplyIntroDimmed();
        }

        /// <summary>
        /// Toggles the top bar's 40%-opacity "behind the intro overlay" state
        /// (ART.md §6.5). Called by <see cref="MatchOverlaysController"/>'s
        /// intro-countdown refresh, which owns the separate overlay UIDocument.
        /// </summary>
        public void SetIntroDimmed(bool dimmed)
        {
            _introDimmed = dimmed;
            if (_bound) ApplyIntroDimmed();
        }

        private void ApplyIntroDimmed()
        {
            if (_topBarRoot == null) return;
            if (_introDimmed) _topBarRoot.AddToClassList("cw-topbar--dimmed");
            else              _topBarRoot.RemoveFromClassList("cw-topbar--dimmed");
        }

        private void Update()
        {
            if (!_bound)
            {
                TryBind();
                if (!_bound) return;
            }

            if (Time.unscaledTime < _nextRefresh && _bases.Length > 0 && _gameManager != null)
                return;
            _nextRefresh = Time.unscaledTime + _refreshInterval;

            RefreshSceneRefs();
            RefreshTimer();
            RefreshScores();
        }

        private void RefreshSceneRefs()
        {
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

        private void RefreshTimer()
        {
            if (_timer == null) return;
            if (_gameManager == null) { _timer.text = "--:--"; return; }

            switch (_gameManager.State)
            {
                case MatchState.Active:
                    float r  = _gameManager.TimeRemaining;
                    int   mm = Mathf.Max(0, Mathf.FloorToInt(r / 60f));
                    int   ss = Mathf.Max(0, Mathf.FloorToInt(r - mm * 60f));
                    _timer.text = $"{mm:00}:{ss:00}";
                    break;
                case MatchState.Ended:
                    _timer.text = "ENDED";
                    break;
                default:
                    _timer.text = "WAIT";
                    break;
            }
        }

        private void RefreshScores()
        {
            var sorted = new List<(int corner, float total)>(_bases.Length);
            for (int i = 0; i < _bases.Length; i++)
            {
                var b = _bases[i];
                if (b != null && b.Object != null && b.Object.IsValid)
                    sorted.Add((b.CornerIndex, b.FoodTotal));
            }
            sorted.Sort((a, b) => b.total.CompareTo(a.total));

            int localCorner = LocalCorner();
            float winTarget = _matchConfig != null ? Mathf.Max(1f, _matchConfig.FoodTargetToWin) : 150f;

            for (int rank = 0; rank < 4; rank++)
            {
                var rowRefs = _lbRows[rank];
                if (rowRefs.Root == null) continue;

                if (rank < sorted.Count)
                {
                    rowRefs.Root.style.display = DisplayStyle.Flex;
                    var (corner, total) = sorted[rank];
                    Color color = PlayerColors[corner % PlayerColors.Length];

                    if (rowRefs.Ord != null) rowRefs.Ord.text = Ordinal(rank + 1);
                    if (rowRefs.Dot != null) rowRefs.Dot.style.unityBackgroundImageTintColor = color;
                    if (rowRefs.Name != null)
                    {
                        rowRefs.Name.text = $"P{corner + 1}";
                        rowRefs.Name.style.color = color;
                    }

                    if (rowRefs.BarFill != null)
                    {
                        float pct = Mathf.Clamp01(total / winTarget);
                        rowRefs.BarFill.style.width = Length.Percent(pct * 100f);
                        rowRefs.BarFill.style.backgroundColor = color;
                    }

                    if (rowRefs.Score != null) rowRefs.Score.text = Mathf.FloorToInt(total).ToString();

                    // The local player's row gets a player-color left border + tint.
                    if (corner == localCorner)
                    {
                        rowRefs.Root.style.borderLeftColor = color;
                        rowRefs.Root.style.backgroundColor = Fade(color, 0.2f);
                    }
                    else
                    {
                        rowRefs.Root.style.borderLeftColor = Color.clear;
                        // The leader's row gets a faint gold wash.
                        if (rank == 0)
                        {
                            rowRefs.Root.AddToClassList("cw-lb-row--leader");
                            rowRefs.Root.style.backgroundColor = new StyleColor(StyleKeyword.Null); // Clear inline to use USS
                        }
                        else
                        {
                            rowRefs.Root.RemoveFromClassList("cw-lb-row--leader");
                            rowRefs.Root.style.backgroundColor = Color.clear;
                        }
                    }
                }
                else
                {
                    rowRefs.Root.style.display = DisplayStyle.None;
                }
            }
        }

        private static string Ordinal(int number) => number switch
        {
            1 => "1st",
            2 => "2nd",
            3 => "3rd",
            _ => $"{number}th"
        };

        private static Color Fade(Color c, float a) => new Color(c.r, c.g, c.b, a);

        private int LocalCorner()
        {
            var controllers = FindObjectsByType<ChickenController>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            for (int i = 0; i < controllers.Length; i++)
            {
                var c = controllers[i];
                if (c != null && c.Object != null && c.Object.IsValid && c.HasInputAuthority)
                    return c.HomeCornerIndex;
            }
            return -1;
        }
    }
}

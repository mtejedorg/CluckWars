using CluckWars.Gameplay;
using CluckWars.Logging;
using UnityEngine;
using UnityEngine.UIElements;
using Zenject;

namespace CluckWars.UI
{
    /// <summary>
    /// UI Toolkit driver for the match top-bar HUD (Stage 2a UI rebuild):
    /// four per-player score badges + the centered match timer. Layout lives in
    /// <c>Assets/UI/MatchTopBar.uxml</c>; styling in
    /// <c>Assets/UI/Styles/MatchTopBar.uss</c>. Replaces the procedural-UGUI
    /// leaderboard + timer badge that used to live in <see cref="MatchHud"/>.
    /// </summary>
    /// <remarks>
    /// Each badge maps to a fixed corner (P1→corner 0 … P4→corner 3) and shows that
    /// base's live <see cref="PlayerBase.FoodTotal"/> — a per-player score, not a
    /// ranked list (the design HUD is the minimal top bar; the ranked leaderboard
    /// only appears on the match-end overlay). A badge hides when no base owns its
    /// corner yet. Scene refs are found the same way MatchHud did — no networked
    /// per-corner score exists to inject. Self-inject falls back to SceneContext
    /// first per CONVENTIONS IP-fix1.
    /// </remarks>
    [RequireComponent(typeof(UIDocument))]
    public sealed class MatchHudController : MonoBehaviour
    {
        private const string Source = "MatchHud";

        [SerializeField] private float _refreshInterval = 0.25f;

        // Okabe-Ito per-player identity colors (ART.md §6), matching MatchHud /
        // MatchOverlays so every surface reads the same palette.
        private static readonly Color[] PlayerColors =
        {
            new Color(0.91f, 0.46f, 0.10f, 1f), // P1 Orange #E8751A
            new Color(0.10f, 0.50f, 0.77f, 1f), // P2 Blue   #1A7FC4
            new Color(0.77f, 0.16f, 0.44f, 1f), // P3 Pink   #C4286F
            new Color(0.05f, 0.62f, 0.48f, 1f), // P4 Teal   #0D9E7A
        };

        private ILogService _log;

        private VisualElement _root;
        private bool          _bound;

        private readonly VisualElement[] _scoreBadges = new VisualElement[4];
        private readonly Label[]         _scoreLabels = new Label[4];
        private Label                    _timer;

        private PlayerBase[] _bases = System.Array.Empty<PlayerBase>();
        private GameManager  _gameManager;
        private float        _nextRefresh;

        [Inject]
        public void Construct(ILogService log) => _log = log;

        private void Awake()
        {
            if (_log == null) ProjectContext.Instance.Container.Inject(this);
        }

        private void OnEnable() => TryBind();

        private void TryBind()
        {
            var doc = GetComponent<UIDocument>();
            _root = doc != null ? doc.rootVisualElement : null;
            if (_root == null) return;

            for (int i = 0; i < 4; i++)
            {
                _scoreBadges[i] = _root.Q<VisualElement>($"Score_P{i + 1}");
                _scoreLabels[i] = _root.Q<Label>($"Label_P{i + 1}");
            }
            _timer = _root.Q<Label>("MatchTimer");
            _bound = true;
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
            for (int corner = 0; corner < 4; corner++)
            {
                var badge = _scoreBadges[corner];
                var label = _scoreLabels[corner];
                if (badge == null || label == null) continue;

                PlayerBase b = BaseForCorner(corner);
                bool show = b != null;
                badge.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
                if (show)
                    label.text = $"P{corner + 1}: {Mathf.FloorToInt(b.FoodTotal)}";
            }
        }

        private PlayerBase BaseForCorner(int corner)
        {
            for (int i = 0; i < _bases.Length; i++)
            {
                var b = _bases[i];
                if (b != null && b.CornerIndex == corner) return b;
            }
            return null;
        }
    }
}

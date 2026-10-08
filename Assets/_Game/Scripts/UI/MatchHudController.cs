using System.Collections.Generic;
using CluckWars.Gameplay;
using CluckWars.Localization;
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

        private ILogService _log;
        private MatchConfigSO _matchConfig;

        private VisualElement _root;
        private VisualElement _topBarRoot;
        private SafeAreaPadding _safeArea;   // keeps the leaderboard and timer out of a notch / cutout
        private bool          _bound;
        private bool          _introDimmed;
        private bool          _hiddenByModal;

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
            if (_topBarRoot == null)
                _log?.Error(Source, "MatchTopBar.uxml has no #TopBarRoot: no safe-area padding, intro dim or modal hide for the HUD.");
            else
                _safeArea = new SafeAreaPadding(_topBarRoot);

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
            // The @key placeholders (ordinals, the timer's --:--) come from the wording dictionary.
            UiText.ResolveTree(_root);

            if (_winTargetBadge != null)
            {
                // The goal is MatchConfigSO's; without it the badge says so rather than inventing one.
                if (_matchConfig != null)
                    _winTargetBadge.text = UiText.Format(UiKeys.HudGoal, ("n", Mathf.Max(1, _matchConfig.FoodTargetToWin)));
                else
                {
                    _log?.Error(Source, "No MatchConfigSO injected: the HUD goal badge has no target.");
                    _winTargetBadge.text = UiText.Get(UiKeys.HudGoalUnknown);
                }
            }

            _bound = true;
            ApplyIntroDimmed();
            ApplyHiddenByModal();
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

        /// <summary>
        /// Hides the top bar entirely while an opaque full-screen modal (match end, in-session
        /// lobby) is up, so its live text never reads through the modal's scrim.
        /// </summary>
        public void SetHiddenByModal(bool hidden)
        {
            if (_hiddenByModal == hidden) return;
            _hiddenByModal = hidden;
            if (_bound) ApplyHiddenByModal();
        }

        private void ApplyHiddenByModal()
        {
            if (_topBarRoot == null) return;
            _topBarRoot.EnableInClassList("cw-topbar--hidden", _hiddenByModal);
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
            _safeArea?.Apply();   // two struct compares a frame unless the safe area moved

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
            if (_gameManager == null) { _timer.text = UiText.Get(UiKeys.HudTimerNone); return; }

            switch (_gameManager.State)
            {
                case MatchState.Active:
                    _timer.text = Clock(_gameManager.TimeRemaining);
                    break;
                case MatchState.Starting:
                    // GET READY: the full round length, frozen, as during the intro.
                    _timer.text = Clock(_gameManager.MatchDurationSeconds);
                    break;
                case MatchState.Ended:
                    _timer.text = UiText.Get(UiKeys.HudTimerEnded);
                    break;
                default:
                    _timer.text = UiText.Get(UiKeys.HudTimerWait);
                    break;
            }
        }

        /// <summary>MM:SS, digits only (no words to translate).</summary>
        private static string Clock(float seconds)
        {
            int mm = Mathf.Max(0, Mathf.FloorToInt(seconds / 60f));
            int ss = Mathf.Max(0, Mathf.FloorToInt(seconds - mm * 60f));
            return $"{mm:00}:{ss:00}";
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
            // Bars fill toward the MatchConfig goal; without one (already logged at bind) they are
            // relative to the leader instead of a made-up target.
            float winTarget = _matchConfig != null
                ? Mathf.Max(1f, _matchConfig.FoodTargetToWin)
                : Mathf.Max(1f, sorted.Count > 0 ? sorted[0].total : 1f);

            for (int rank = 0; rank < 4; rank++)
            {
                var rowRefs = _lbRows[rank];
                if (rowRefs.Root == null) continue;

                if (rank < sorted.Count)
                {
                    rowRefs.Root.style.display = DisplayStyle.Flex;
                    var (corner, total) = sorted[rank];
                    Color color = PlayerPalette.ForCorner(corner);
                    bool isLocal = corner == localCorner;

                    if (rowRefs.Ord != null) rowRefs.Ord.text = MatchStandings.Ordinal(rank + 1);
                    // The player colour lives on the dot (and the bar); the name stays cream (USS) for
                    // contrast, and the local player's name is the YOU mark (round-2 findings 1 + 5).
                    if (rowRefs.Dot != null) rowRefs.Dot.style.unityBackgroundImageTintColor = color;
                    if (rowRefs.Name != null)
                    {
                        rowRefs.Name.text = NameForCorner(corner, localCorner);
                        rowRefs.Name.EnableInClassList(PlayerPalette.YouMarkClass, isLocal);
                    }

                    if (rowRefs.BarFill != null)
                    {
                        float pct = Mathf.Clamp01(total / winTarget);
                        rowRefs.BarFill.style.width = Length.Percent(pct * 100f);
                        rowRefs.BarFill.style.backgroundColor = color;
                    }

                    if (rowRefs.Score != null) rowRefs.Score.text = Mathf.FloorToInt(total).ToString();

                    // The local player's row gets a player-colour left border (no colour wash behind the
                    // cream text); the leader's row, local or not, a faint gold wash (USS).
                    rowRefs.Root.style.borderLeftColor = isLocal ? color : Color.clear;
                    rowRefs.Root.EnableInClassList("cw-lb-row--leader", rank == 0);
                }
                else
                {
                    rowRefs.Root.style.display = DisplayStyle.None;
                }
            }
        }

        // Per-corner name cache: the same identity as the post-match screen and the nameplates
        // (MatchStandings.DisplayName), rebuilt only when what it depends on changes, so the 4 Hz
        // refresh does not format a string per row.
        private readonly string[] _names = new string[4];
        private readonly int[]    _nameKeys = { -1, -1, -1, -1 };

        private string NameForCorner(int corner, int localCorner)
        {
            if (corner < 0 || corner >= _names.Length) return MatchStandings.DisplayName(false, false, ChickenClass.Warrior, corner);
            var c = ChickenForCorner(corner);
            bool isLocal = corner == localCorner;
            bool isBot = c != null && c.IsBot;
            var cls = c != null ? c.Class : ChickenClass.Warrior;
            int key = (isLocal ? 1 : 0) | (isBot ? 2 : 0) | ((int)cls << 2);
            if (_nameKeys[corner] != key || _names[corner] == null)
            {
                _nameKeys[corner] = key;
                _names[corner] = MatchStandings.DisplayName(isLocal, isBot, cls, corner);
            }
            return _names[corner];
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

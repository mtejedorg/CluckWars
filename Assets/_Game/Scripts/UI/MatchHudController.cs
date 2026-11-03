using System.Collections.Generic;
using CluckWars.Gameplay;
using CluckWars.Input;
using CluckWars.Localization;
using CluckWars.Logging;
using CluckWars.Visuals;
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
            public VisualElement Skull;   // Death Mark: inside the dot
            public Label         Name;
            public VisualElement BarFill;
            public Label         Score;
        }

        private readonly LbRow[] _lbRows = new LbRow[4];
        private Label            _timer;
        private Label            _winTargetBadge;
        private VisualElement    _lbPanel;
        private float            _tapOpenUntil;      // unscaled time until which a tap keeps the full board open (phone)
        private readonly bool[]  _rowVisible = new bool[4];

        // Death Mark (from MarkFeedback.Current, applied every frame): which corner each rank row shows, the draw highlight,
        // the victim's toast and clock pill.
        private readonly int[]   _rowCorner = { -1, -1, -1, -1 };
        private int              _localCorner = -1;
        private VisualElement    _drawHighlight, _markToast, _markClock;
        private Label            _markClockText, _markToastText;
        private bool             _toastShowsBackIn;
        private int              _shownClockSecs = -1;
        private bool             _boardWantsAll;
        private int              _boardWantsCorner = -1;
        private readonly int[]   _drawRows = new int[4];

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
            // The round orientation lock (C4) lives exactly as long as the match HUD; added here so no scene edit is needed.
            if (GetComponent<OrientationPolicy>() == null) gameObject.AddComponent<OrientationPolicy>();

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
            // Shape classes for the top bar's portrait overrides (WP7), measured on the full panel: the bar itself is a band.
            LayoutClasses.Bind(_root, _topBarRoot ?? _root);
            SafeAreaSource.LogOnce(_log);

            for (int i = 0; i < 4; i++)
            {
                _lbRows[i] = new LbRow
                {
                    Root    = _root.Q<VisualElement>($"LbRow{i}"),
                    Ord     = _root.Q<Label>($"LbOrd{i}"),
                    Dot     = _root.Q<VisualElement>($"LbDot{i}"),
                    Skull   = _root.Q<VisualElement>($"LbSkull{i}"),
                    Name    = _root.Q<Label>($"LbName{i}"),
                    BarFill = _root.Q<VisualElement>($"LbBarFill{i}"),
                    Score   = _root.Q<Label>($"LbScore{i}"),
                };
            }
            _lbPanel = _root.Q<VisualElement>("LeaderboardPanel");
            // A tap on the board opens all four rows on the phone layout (CompactBoardRules); the panel only takes
            // taps while compact (SetBoardTappable), so on desktop it stays click-through like the rest of the bar.
            _lbPanel?.RegisterCallback<PointerDownEvent>(_ =>
            {
                _tapOpenUntil = Time.unscaledTime + CompactBoardRules.TapShowAllSeconds;
                _nextRefresh = 0f;
            });
            _timer = _root.Q<Label>("MatchTimer");
            _drawHighlight = _root.Q<VisualElement>("LbDrawHighlight");
            _markToast = _root.Q<VisualElement>("MarkToast");
            _markClock = _root.Q<VisualElement>("MarkClock");
            _markClockText = _root.Q<Label>("MarkClockText");
            _markToastText = _root.Q<Label>("MarkToastText");
            if (_drawHighlight == null || _markToast == null || _markClock == null || _markClockText == null || _lbRows[0].Skull == null)
                _log?.Error(Source, "MatchTopBar.uxml is missing a Death Mark element (LbDrawHighlight, MarkToast, MarkClock, " +
                    "MarkClockText or LbSkull0-3): the mark's draw, standings skull, toast or clock pill will not show.");
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

            var mark = MarkFeedback.Current;
            // The compact phone board must show the marked row, and every row during the draw: refresh at once when that changes.
            bool wantsAll = mark.DrawShowsAllRows;
            int wantsCorner = mark.SkullAlpha > 0f ? mark.SkullCorner : -1;
            if (wantsAll != _boardWantsAll || wantsCorner != _boardWantsCorner)
            {
                _boardWantsAll = wantsAll;
                _boardWantsCorner = wantsCorner;
                _nextRefresh = 0f;
            }

            if (Time.unscaledTime >= _nextRefresh || _bases.Length == 0 || _gameManager == null)
            {
                _nextRefresh = Time.unscaledTime + _refreshInterval;
                RefreshSceneRefs();
                RefreshTimer();
                RefreshScores();
            }
            ApplyMark(mark);
        }

        // ---- Death Mark (per frame; every decision is MarkPresentationModel's) ----------------------------------------

        private void ApplyMark(in MarkOutputs o)
        {
            for (int rank = 0; rank < 4; rank++)
            {
                var skull = _lbRows[rank].Skull;
                if (skull == null) continue;
                bool on = o.SkullAlpha > 0f && _rowCorner[rank] >= 0 && _rowCorner[rank] == o.SkullCorner && _rowVisible[rank];
                SetShown(skull, on);
                if (on) skull.style.opacity = o.SkullAlpha;
            }

            ApplyDrawHighlight(o);

            if (_markToast != null)
            {
                // The MARKED! toast, or the local player's BACK IN! after an execute (same plate, ink, no skull).
                float alpha = o.ToastAlpha, scale = o.ToastScale;
                bool backIn = alpha <= 0f && BackInToast(LocalRespawnCue.SecondsSince, out alpha, out scale);
                bool toast = alpha > 0f;
                SetShown(_markToast, toast);
                if (toast)
                {
                    if (backIn != _toastShowsBackIn && _markToastText != null)
                    {
                        _toastShowsBackIn = backIn;
                        _markToastText.text = UiText.Get(backIn ? UiKeys.HudBackIn : UiKeys.MarkToast);
                        _markToast.EnableInClassList("cw-mark-toast--backin", backIn);
                    }
                    _markToast.style.opacity = alpha;
                    _markToast.style.scale = new Scale(new Vector3(scale, scale, 1f));
                }
            }

            if (_markClock != null)
            {
                bool clock = o.ClockAlpha > 0f;
                SetShown(_markClock, clock);
                if (clock)
                {
                    _markClock.style.opacity = o.ClockAlpha;
                    if (o.ClockSeconds != _shownClockSecs && _markClockText != null)
                    {
                        _shownClockSecs = o.ClockSeconds;
                        _markClockText.text = UiText.Format(UiKeys.MarkClock, ("secs", _shownClockSecs));
                    }
                }
            }
        }

        /// <summary>The Assassin's draw: step <c>DrawStep</c> of <c>DrawSteps</c> over the visible rows top to bottom, skipping the
        /// local row and looping, timed so the last step lands on the target's row.</summary>
        private void ApplyDrawHighlight(in MarkOutputs o)
        {
            if (_drawHighlight == null) return;
            int n = 0, targetAt = -1;
            if (o.DrawAlpha > 0f)
            {
                for (int rank = 0; rank < 4; rank++)
                {
                    if (!_rowVisible[rank] || _rowCorner[rank] < 0 || _rowCorner[rank] == _localCorner) continue;
                    if (_rowCorner[rank] == o.TargetCorner) targetAt = n;
                    _drawRows[n++] = rank;
                }
            }
            if (n == 0 || targetAt < 0)
            {
                SetShown(_drawHighlight, false);
                return;
            }

            int back = Mathf.Max(0, o.DrawSteps - 1 - o.DrawStep);
            int at = ((targetAt - back) % n + n) % n;
            var row = _lbRows[_drawRows[at]].Root;
            if (row == null || float.IsNaN(row.layout.y))
            {
                SetShown(_drawHighlight, false);
                return;
            }
            SetShown(_drawHighlight, true);
            _drawHighlight.style.translate = new Translate(0f, row.layout.y);
            _drawHighlight.style.opacity = o.DrawAlpha;
            _drawHighlight.style.scale = new Scale(new Vector3(o.DrawScale, o.DrawScale, 1f));
            _drawHighlight.EnableInClassList("cw-lb-draw--locked", o.DrawLocked);
        }

        /// <summary>The BACK IN! toast's alpha / scale <paramref name="since"/> seconds after the respawn: the MARKED! timings.</summary>
        public static bool BackInToast(float since, out float alpha, out float scale)
        {
            alpha = 0f;
            scale = 1f;
            float tin = FeedbackTuning.MarkToastInSeconds, hold = FeedbackTuning.MarkToastHoldSeconds, tout = FeedbackTuning.MarkToastOutSeconds;
            if (!(since >= 0f) || since >= tin + hold + tout) return false;
            if (since < tin) { alpha = since / tin; scale = Mathf.Lerp(FeedbackTuning.MarkToastScaleFrom, 1f, alpha); }
            else if (since < tin + hold) alpha = 1f;
            else alpha = 1f - (since - tin - hold) / tout;
            return alpha > 0f;
        }

        private static void SetShown(VisualElement e, bool shown)
        {
            var d = shown ? DisplayStyle.Flex : DisplayStyle.None;
            if (e.style.display != d) e.style.display = d;
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
                // No row for a corner nobody plays (round-2 leftover; the same rule as post-match).
                if (b != null && b.Object != null && b.Object.IsValid
                    && MatchStandings.Listed(ChickenForCorner(b.CornerIndex) != null, b.FoodTotal))
                    sorted.Add((b.CornerIndex, b.FoodTotal));
            }
            sorted.Sort((a, b) => b.total.CompareTo(a.total));

            int localCorner = LocalCorner();
            _localCorner = localCorner;

            // Phone layout: leader + your row, all four on tap and in the last 10 s (CompactBoardRules).
            var touchHud = TouchControlsController.Instance;
            bool compact = touchHud != null && touchHud.Mode == HudDeviceMode.Touch;
            SetBoardTappable(compact);
            bool matchActive = _gameManager != null && _gameManager.State == MatchState.Active;
            bool showAll = CompactBoardRules.ShowAll(compact, Time.unscaledTime < _tapOpenUntil, matchActive,
                matchActive ? _gameManager.TimeRemaining : float.MaxValue) || _boardWantsAll;   // the Death Mark draw needs every row
            int youRank = -1;
            for (int i = 0; i < sorted.Count; i++) if (sorted[i].corner == localCorner) { youRank = i; break; }
            CompactBoardRules.SelectRows(sorted.Count, youRank, showAll, _rowVisible);
            // A running Death Mark keeps its marked row on the compact board (three rows at most).
            for (int i = 0; i < sorted.Count && i < _rowVisible.Length; i++)
                if (sorted[i].corner == _boardWantsCorner) _rowVisible[i] = true;

            // Bars fill toward the MatchConfig goal; without one (already logged at bind) they are
            // relative to the leader instead of a made-up target.
            float winTarget = _matchConfig != null
                ? Mathf.Max(1f, _matchConfig.FoodTargetToWin)
                : Mathf.Max(1f, sorted.Count > 0 ? sorted[0].total : 1f);

            for (int rank = 0; rank < 4; rank++)
            {
                var rowRefs = _lbRows[rank];
                if (rowRefs.Root == null) continue;

                _rowCorner[rank] = rank < sorted.Count && _rowVisible[rank] ? sorted[rank].corner : -1;
                if (rank < sorted.Count && _rowVisible[rank])
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

        private bool _boardTappable;

        private void SetBoardTappable(bool tappable)
        {
            if (_lbPanel == null || tappable == _boardTappable) return;
            _boardTappable = tappable;
            _lbPanel.pickingMode = tappable ? PickingMode.Position : PickingMode.Ignore;
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

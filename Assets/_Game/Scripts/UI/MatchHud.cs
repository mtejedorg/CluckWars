using CluckWars.Gameplay;
using CluckWars.Localization;
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
    /// UGUI match HUD. Builds its canvas procedurally on Awake so Game.unity stays
    /// a single drop-in component. After the Stage-2a/4 UI rebuild this owns only
    /// the two full-screen effects that have no on-character or UI Toolkit home:
    /// <list type="bullet">
    ///   <item>Full-screen hit-flash on local HP loss.</item>
    ///   <item>The comeback event banner ("FINAL 10!" + the event), lower third between the joystick and the ability buttons.</item>
    /// </list>
    /// The top-bar leaderboard + timer are UI Toolkit (<see cref="MatchHudController"/>
    /// + <c>Assets/UI/MatchTopBar.uxml</c>), and the local HP/cargo bars now live
    /// on the chicken itself in world space (<c>ChickenWorldBars</c>, ART.md §6.3 —
    /// the HUD is deliberately minimal because "the game area is sacred").
    /// </summary>
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
        private static readonly Color DtGold        = new Color(0.96f, 0.78f, 0.26f, 1f); // #f5c842
        private static readonly Color DtTextPrimary = new Color(1.00f, 0.96f, 0.88f, 1f); // #fef5e0
        // The menus' ink (#2a1a0c): text outline + the banner's plate. Gold on it is ~9:1, cream ~14:1.
        private static readonly Color DtInk         = new Color(0.165f, 0.102f, 0.047f, 1f);

        // ---- UI refs built procedurally ------------------------------------

        private GameObject _eventBannerPanel;
        private Text       _eventBannerHeader;   // "FINAL 10!"
        private Text       _eventBannerText;     // "BOUNTY ON THE LEADER!"
        private float      _bannerExpireTime;
        private MatchEventKind _lastSeenEvent = MatchEventKind.None;
        // A new event's banner waits here until the GO! flourish has gone (EventBannerTiming).
        private bool       _bannerPending;
        // Last unscaled time this client saw the intro countdown running; -inf = never (joined after GO).
        private float      _introLastSeenAt = float.NegativeInfinity;

        // ---- Cached scene refs -------------------------------------------------

        private ChickenController _localController;
        private ChickenCombat     _localCombat;   // hit-flash watches its HP
        private GameManager       _gameManager;
        private float             _nextRefresh;

        // ---- Injected services -------------------------------------------------

        private INetworkService          _network;
        private ILogService              _log;
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
            IAudioService            audio,
            AudioRegistrySO          audioReg)
        {
            _network     = network;
            _log         = log;
            _audio       = audio;
            _audioReg    = audioReg;
        }

        // ---- Unity lifecycle ---------------------------------------------------

        private void Awake()
        {
            if (_network == null) ProjectContext.Instance.Container.Inject(this);

            EnsureEventSystem();
            BuildCanvas();
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
                var all = FindObjectsByType<ChickenController>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
                for (int i = 0; i < all.Length; i++)
                {
                    var c = all[i];
                    if (c.Object == null || !c.Object.IsValid) continue;
                    if (!c.HasInputAuthority) continue;
                    _localController = c;
                    _localCombat     = c.Combat;
                    break;
                }
            }

            if (_gameManager == null || _gameManager.Object == null || !_gameManager.Object.IsValid)
                _gameManager = FindFirstObjectByType<GameManager>();
        }

        // ---- Per-frame data → UI -----------------------------------------------

        private void RefreshDynamic()
        {
            TickHitFlash();
            PollComebackEvent();
        }

        // ---- Hit-flash ---------------------------------------------------------

        private void TickHitFlash()
        {
            if (_hitFlash == null) return;

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
            BuildEventBanner(canvasGO.transform);
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

        /// <summary>
        /// The comeback banner (round-2 finding 3): an opaque ink plate with gold rules and ink-outlined
        /// type, so it reads on any patch of arena (the old gold-on-grass text measured 1.2:1; its rounded
        /// panel never drew: the SDF material read the rect size from UV1, a channel this canvas never
        /// passed, so the shape had zero size). Anchored bottom-centre in the lower third, between the
        /// joystick and the ability buttons: the top is taken by the leaderboard column (left, down to its
        /// FIRST TO chip) and the timer (right), and the local chicken + nameplate sit just above screen
        /// centre under the follow camera. GO! never shares the screen with it (EventBannerTiming).
        /// </summary>
        private void BuildEventBanner(Transform parent)
        {
            _eventBannerPanel = CreateUI("EventBanner", parent, out var rt);
            rt.anchorMin = new Vector2(0.5f, 0f);
            rt.anchorMax = new Vector2(0.5f, 0f);
            rt.pivot = new Vector2(0.5f, 0f);
            // 1920x1080 reference (ScaleWithScreenSize @0.5): 760 wide = 40% of a 16:9 screen, 46% of a 4:3 one,
            // so it stays clear of the joystick (left) and the ability hexes (right); bottom edge above the
            // gesture bar.
            rt.sizeDelta = new Vector2(760f, 150f);
            rt.anchoredPosition = new Vector2(0f, 150f);

            var plate = _eventBannerPanel.AddComponent<Image>();
            plate.color = new Color(DtInk.r, DtInk.g, DtInk.b, 0.92f);
            plate.raycastTarget = false;
            AddRule(_eventBannerPanel.transform, "TopRule", top: true);
            AddRule(_eventBannerPanel.transform, "BottomRule", top: false);

            _eventBannerHeader = AddBannerLine("Header", 38, DtGold, yMin: 0.58f, yMax: 0.96f);
            _eventBannerText   = AddBannerLine("Label", 54, DtTextPrimary, yMin: 0.06f, yMax: 0.62f);

            _eventBannerPanel.SetActive(false);
        }

        private static void AddRule(Transform parent, string name, bool top)
        {
            var go = CreateUI(name, parent, out var lineRT);
            lineRT.anchorMin = new Vector2(0f, top ? 1f : 0f);
            lineRT.anchorMax = new Vector2(1f, top ? 1f : 0f);
            lineRT.pivot = new Vector2(0.5f, top ? 1f : 0f);
            lineRT.sizeDelta = new Vector2(0f, 4f);
            lineRT.anchoredPosition = Vector2.zero;
            var img = go.AddComponent<Image>();
            img.color = DtGold;
            img.raycastTarget = false;
        }

        private Text AddBannerLine(string name, int size, Color color, float yMin, float yMax)
        {
            var text = AddText(_eventBannerPanel.transform, name, "", size, TextAnchor.MiddleCenter, FontStyle.Bold);
            text.color = color;
            var outline = text.gameObject.AddComponent<Outline>();
            outline.effectColor = DtInk;
            outline.effectDistance = new Vector2(3f, -3f);
            var tRT = text.rectTransform;
            tRT.anchorMin = new Vector2(0f, yMin);
            tRT.anchorMax = new Vector2(1f, yMax);
            tRT.offsetMin = Vector2.zero;
            tRT.offsetMax = Vector2.zero;
            return text;
        }

        private void PollComebackEvent()
        {
            if (_gameManager == null) return;

            if (_gameManager.IsIntroActive) _introLastSeenAt = Time.unscaledTime;

            MatchEventKind evt = _gameManager.ActiveEvent;
            if (evt != _lastSeenEvent)
            {
                _lastSeenEvent = evt;
                if (evt != MatchEventKind.None)
                {
                    if (_eventBannerHeader != null)
                        _eventBannerHeader.text = UiText.Format(UiKeys.HudEventHeader,
                            ("n", Mathf.RoundToInt(_gameManager.ComebackEventSecondsLeft)));
                    if (_eventBannerText != null)
                        _eventBannerText.text = EventName(evt);
                    _bannerPending = true;
                }
                else
                {
                    _bannerPending = false;
                    if (_eventBannerPanel != null) _eventBannerPanel.SetActive(false);
                }
            }

            // The event fired on schedule (gameplay); only its banner waits for GO! to clear.
            float sinceIntro = Time.unscaledTime - _introLastSeenAt;
            if (_bannerPending && EventBannerTiming.CanShow(_gameManager.IsIntroActive, sinceIntro))
            {
                _bannerPending = false;
                if (_eventBannerPanel != null) _eventBannerPanel.SetActive(true);
                _bannerExpireTime = Time.unscaledTime + 3f;
                if (EventBannerTiming.PlaysSting(sinceIntro))
                    _audio?.PlaySFX(_audioReg != null ? _audioReg.MatchStart : null);
            }

            if (_eventBannerPanel != null && _eventBannerPanel.activeSelf && Time.unscaledTime >= _bannerExpireTime)
            {
                _eventBannerPanel.SetActive(false);
            }
        }

        private static string EventName(MatchEventKind evt) => evt switch
        {
            MatchEventKind.GoldenPile    => UiText.Get(UiKeys.HudEventGoldenPile),
            MatchEventKind.UnderdogSurge => UiText.Get(UiKeys.HudEventUnderdogSurge),
            MatchEventKind.LeaderBounty  => UiText.Get(UiKeys.HudEventLeaderBounty),
            MatchEventKind.Restock       => UiText.Get(UiKeys.HudEventRestock),
            // A kind added without a UiText row: its enum name, never a blank banner.
            _ => evt.ToString().ToUpperInvariant(),
        };
    }
}

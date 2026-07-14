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
    /// UGUI match HUD. Builds its canvas procedurally on Awake so Game.unity stays
    /// a single drop-in component. After the Stage-2a/4 UI rebuild this owns only
    /// the two full-screen effects that have no on-character or UI Toolkit home:
    /// <list type="bullet">
    ///   <item>Full-screen hit-flash on local HP loss.</item>
    ///   <item>Centered final-minute event banner.</item>
    /// </list>
    /// The top-bar leaderboard + timer are UI Toolkit (<see cref="MatchHudController"/>
    /// + <c>Assets/UI/MatchTopBar.uxml</c>), and the local HP/cargo bars now live
    /// on the chicken itself in world space (<c>ChickenWorldBars</c>, ART.md §6.3 —
    /// the HUD is deliberately minimal because "the game area is sacred").
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
        private static readonly Color DtTextPrimary = new Color(1.00f, 0.96f, 0.88f, 1f); // #fef5e0

        // ---- UI refs built procedurally ------------------------------------

        private GameObject _eventBannerPanel;
        private Text       _eventBannerText;
        private float      _bannerExpireTime;
        private MatchEventKind _lastSeenEvent = MatchEventKind.None;

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
        }
    }
}

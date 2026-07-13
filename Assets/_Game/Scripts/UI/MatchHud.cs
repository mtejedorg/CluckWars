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
    /// a single drop-in component. Post Stage-2a UI rebuild this owns only the
    /// remaining UGUI pieces:
    /// <list type="bullet">
    ///   <item>Bottom-left panel: local HP + cargo bars (moves on-character in Stage 4).</item>
    ///   <item>Full-screen hit-flash on local HP loss.</item>
    ///   <item>Centered final-minute event banner.</item>
    /// </list>
    /// The top-bar leaderboard + timer are now UI Toolkit — see
    /// <see cref="MatchHudController"/> + <c>Assets/UI/MatchTopBar.uxml</c>.
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

        private Image      _hpFill;
        private Text       _hpLabel;
        private Image      _cargoFill;
        private Text       _cargoLabel;

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
            RefreshLocalStats();
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
            BuildLocalStatsPanel(canvasGO.transform);
            BuildEventBanner(canvasGO.transform);
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
        }
    }
}

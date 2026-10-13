using CluckWars.Gameplay;
using CluckWars.Localization;
using CluckWars.Settings;
using CluckWars.UI;
using UnityEngine;

namespace CluckWars.Visuals
{
    /// <summary>
    /// Floating name above each chicken's head, the same identity the menus and the
    /// post-match screen use (<see cref="MatchStandings.DisplayName(bool, bool, ChickenClass, int)"/>):
    /// "You" for this peer's own chicken, the Coop's DashFox / BrunoB / PeckNoir for solo
    /// bots, "P{corner+1}" for a remote human. Colour = the corner's player colour
    /// (<see cref="PlayerPalette"/>, same as the HUD, menus and podium). This peer's own chicken
    /// wears the YOU mark instead: ink text on a gold pill, the same mark as the HUD row, the
    /// lobby seat and the podium (round-2 finding 1). The label billboards toward the camera.
    /// </summary>
    /// <remarks>
    /// Pure local visual — no networking. Reads the replicated
    /// <c>HomeCornerIndex</c> / <c>IsBot</c> / <c>Class</c> and this peer's input authority; the
    /// text is rebuilt only when one of those (or the bounty / state badge) changes. Drop this on the Chicken prefab so every chicken (real or
    /// Doppelganger decoy) carries one automatically.
    /// </remarks>
    [RequireComponent(typeof(ChickenController))]
    public sealed class ChickenNameplate : MonoBehaviour
    {
        [Tooltip("Offset above the chicken's pivot. Adjust for taller / shorter visuals.")]
        [SerializeField] private Vector3 _offset = new Vector3(0f, 1.7f, 0f);

        [Tooltip("TextMesh fontSize. Bigger = sharper at the cost of texture memory.")]
        [Range(8, 200)]
        [SerializeField] private int _fontSize = 96;

        [Tooltip("Scale of each character in world units.")]
        [Range(0.005f, 0.5f)]
        [SerializeField] private float _characterSize = 0.05f;

        // YOU-mark pill padding around the text, world units.
        private static readonly Vector2 YouPillPadding = new Vector2(0.22f, 0.08f);
        // Rival pill: dot diameter as a share of the text height, and the gap between dot and text, world units.
        private const float RivalDotScale = 0.78f;
        private const float RivalDotGap = 0.09f;
        private static Sprite _pillSprite;
        private static Sprite _rivalPillSprite;
        private static Sprite _dotSprite;

        private ChickenController _controller;
        private ChickenCombat     _combat;
        private ChickenCargo      _cargo;
        private TextMesh  _text;
        private MeshRenderer _textRenderer;
        private Transform _textTransform;
        private SpriteRenderer _youPill;
        private SpriteRenderer _rivalPill;
        private SpriteRenderer _rivalDotRing;
        private SpriteRenderer _rivalDot;

        private int  _appliedPlayerId  = -2;
        private ChickenClass _appliedClass;
        private bool _appliedClassValid;
        private bool _appliedLocal;
        private bool _appliedBot;
        private bool _appliedBountyActive;
        private bool _wasStunned;
        private string _appliedStateIcon = string.Empty;
        private float _youEmphasis;   // 0..1, GET READY "this is you" marker (YouMarkerRules)

        private void Awake()
        {
            _controller = GetComponent<ChickenController>();
            _combat     = GetComponent<ChickenCombat>();
            _cargo      = GetComponent<ChickenCargo>();
            BuildLabel();
        }

        private void BuildLabel()
        {
            var go = new GameObject("Nameplate");
            go.transform.SetParent(transform, worldPositionStays: false);
            go.transform.localPosition = _offset;
            _textTransform = go.transform;

            _text = go.AddComponent<TextMesh>();
            _text.anchor = TextAnchor.MiddleCenter;
            _text.alignment = TextAlignment.Center;
            _text.fontSize = _fontSize;
            _text.characterSize = _characterSize;
            _text.fontStyle = FontStyle.Bold;
            _text.text = string.Empty;   // filled on the first LateUpdate with a valid network object
            _text.color = Color.white;

            _textRenderer = go.GetComponent<MeshRenderer>();
            if (_textRenderer != null)
            {
                _textRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                _textRenderer.receiveShadows = false;
                _textRenderer.sortingOrder = 2;   // over the YOU pill
            }

            // The YOU mark's pill, behind the text (the label faces away from the camera, so +z is behind).
            var pillGo = new GameObject("YouMark");
            pillGo.transform.SetParent(_textTransform, worldPositionStays: false);
            pillGo.transform.localPosition = new Vector3(0f, 0f, 0.01f);
            _youPill = pillGo.AddComponent<SpriteRenderer>();
            _youPill.sprite = PillSprite(rival: false);
            _youPill.drawMode = SpriteDrawMode.Sliced;
            _youPill.color = Color.white;   // the sprite carries the gold fill and ink rim
            _youPill.sortingOrder = 1;
            _youPill.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _youPill.receiveShadows = false;
            _youPill.enabled = false;

            // A rival's mark (round-4 finding 4): the same capsule in ink with a cream rim, cream text on it (so the label
            // never depends on the ground behind it) and the player colour as a dot on the left, ringed in cream.
            _rivalPill = AddSprite(_textTransform, "RivalMark", PillSprite(rival: true), 1, SpriteDrawMode.Sliced);
            _rivalDotRing = AddSprite(_textTransform, "RivalDotRing", DotSprite(), 2, SpriteDrawMode.Simple);
            _rivalDotRing.color = PlayerPalette.RivalPillRim;
            _rivalDot = AddSprite(_textTransform, "RivalDot", DotSprite(), 3, SpriteDrawMode.Simple);
        }

        private static SpriteRenderer AddSprite(Transform parent, string name, Sprite sprite, int order, SpriteDrawMode mode)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, worldPositionStays: false);
            go.transform.localPosition = new Vector3(0f, 0f, 0.01f);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.drawMode = mode;
            sr.sortingOrder = order;
            sr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            sr.receiveShadows = false;
            sr.enabled = false;
            return sr;
        }

        private void LateUpdate()
        {
            if (_text == null || _controller == null) return;

            // ── Death skull ──────────────────────────────────────────────────
            bool isNowStunned = _combat != null && _combat.IsRemoved;
            if (isNowStunned != _wasStunned)
            {
                _wasStunned = isNowStunned;
                if (!isNowStunned)
                {
                    _appliedPlayerId  = -2;
                    _appliedClassValid = false;
                }
            }

            ApplyYouEmphasis(!isNowStunned);

            if (isNowStunned)
            {
                ShowMarks(false, false, Color.clear);
                _text.text  = "☠";
                _text.color = new Color(0.75f, 0.20f, 0.20f, 0.85f);
                BillboardToCamera();
                return;
            }

            // ── Normal label ────────────────────────────────────────────────
            var obj = _controller.Object;
            if (obj != null && obj.IsValid)
            {
                int corner = _controller.HomeCornerIndex;
                var klass = _controller.Class;
                bool isLocal = _controller.HasInputAuthority;
                bool isBot = _controller.IsBot;
                bool bountyActive = _controller.LeaderBountyActive;
                string stateIcon = StateIcon();
                if (corner != _appliedPlayerId || !_appliedClassValid || klass != _appliedClass
                    || isLocal != _appliedLocal || isBot != _appliedBot
                    || bountyActive != _appliedBountyActive || stateIcon != _appliedStateIcon)
                {
                    _appliedPlayerId = corner;
                    _appliedClass = klass;
                    _appliedClassValid = true;
                    _appliedLocal = isLocal;
                    _appliedBot = isBot;
                    _appliedBountyActive = bountyActive;
                    _appliedStateIcon = stateIcon;

                    // No corner yet (spawn not stamped): the class is all there is to say.
                    string label = corner < 0
                        ? MatchStandings.ClassName(klass)
                        : MatchStandings.DisplayName(isLocal, isBot, klass, corner);
                    // A bot's own name hides which colour it is; the P# says it in text too, so ring and nameplate
                    // colour are never the only way to tell two rivals apart (round-3 finding 11).
                    if (isBot && !isLocal && corner >= 0 && MatchStandings.BotIndex(klass) >= 0)
                        label = UiText.Format(UiKeys.LobbyPlayerTag, ("n", corner + 1)) + " " + label;
                    if (bountyActive) label = UiText.Format(UiKeys.NameplateBounty, ("name", label));
                    _text.text = stateIcon.Length > 0 ? stateIcon + " " + label : label;
                    _text.color = isLocal ? PlayerPalette.YouMarkInk : PlayerPalette.RivalPillText;
                    FitMarks(isLocal, PlayerPalette.ForCorner(corner));
                }
            }

            BillboardToCamera();
        }

        /// <summary>
        /// GET READY "this is you" marker (Phase 6 chunk 7f): the local bird's YOU label is drawn
        /// <see cref="YouMarkerRules.NameplateScale"/>x larger (lifted so it does not sink into the bird) during
        /// GET READY and the countdown, easing back after GO. Reduced Motion snaps instead of easing.
        /// </summary>
        private void ApplyYouEmphasis(bool alive)
        {
            if (_textTransform == null) return;
            var obj = _controller.Object;
            bool show = alive && obj != null && obj.IsValid && _controller.HasInputAuthority && !_controller.IsDecoy
                        && YouMarkerRules.IsGetReadyNow();
            float before = _youEmphasis;
            _youEmphasis = YouMarkerRules.StepEmphasis(_youEmphasis, show, Time.unscaledDeltaTime,
                PlayerPreferences.ReducedMotionEnabled);
            if (_youEmphasis == before && (before <= 0f || before >= 1f)) return;   // settled: nothing to write

            float scale = YouMarkerRules.NameplateScaleAt(_youEmphasis);
            _textTransform.localScale = Vector3.one * scale;
            // Grow upward from the label's bottom edge rather than about its centre.
            _textTransform.localPosition = _offset + Vector3.up * (scale - 1f) * 0.25f;
        }

        private void ShowMarks(bool you, bool rival, Color dot)
        {
            if (_youPill != null) _youPill.enabled = you;
            if (_rivalPill != null) _rivalPill.enabled = rival;
            if (_rivalDotRing != null) _rivalDotRing.enabled = rival;
            if (_rivalDot != null)
            {
                _rivalDot.enabled = rival;
                _rivalDot.color = dot;
            }
        }

        /// <summary>
        /// Puts the right pill behind the label, sized to the text: the gold YOU capsule on the local bird, the ink
        /// capsule with the player-colour dot on every other one.
        /// </summary>
        private void FitMarks(bool isLocal, Color playerColour)
        {
            ShowMarks(isLocal, !isLocal, playerColour);
            if (_textRenderer == null) return;
            // localBounds is in the label's own space, so billboarding does not change it.
            var size = (Vector2)_textRenderer.localBounds.size;
            if (size.x <= 0f || size.y <= 0f) size = new Vector2(0.6f, 0.3f);   // mesh not built yet: a "You"-sized pill
            var centre = new Vector3(_textRenderer.localBounds.center.x, _textRenderer.localBounds.center.y, 0.01f);

            if (isLocal)
            {
                if (_youPill == null) return;
                _youPill.size = size + YouPillPadding * 2f;
                _youPill.transform.localPosition = centre;
                return;
            }

            if (_rivalPill == null) return;
            float dot = size.y * RivalDotScale;
            float extra = dot + RivalDotGap;               // room on the left for the dot
            _rivalPill.size = new Vector2(size.x + extra, size.y) + YouPillPadding * 2f;
            _rivalPill.transform.localPosition = centre + Vector3.left * (extra * 0.5f);
            var dotPos = new Vector3(centre.x - size.x * 0.5f - RivalDotGap - dot * 0.5f, centre.y, 0.005f);
            // The dot sprite is 1 world unit across, so the scale is the diameter.
            _rivalDotRing.transform.localPosition = dotPos;
            _rivalDotRing.transform.localScale = Vector3.one * dot;
            _rivalDot.transform.localPosition = dotPos + Vector3.back * 0.001f;
            _rivalDot.transform.localScale = Vector3.one * (dot * 0.7f);
        }

        /// <summary>
        /// The pill as a 9-sliced sprite: the YOU capsule (gold, dark-ink rim, <see cref="PlayerPalette.YouMarkFill"/> /
        /// <see cref="PlayerPalette.YouMarkInk"/>, the pill UI Toolkit draws with <c>.cw-you-mark</c>) or the rival
        /// capsule (ink, cream rim).
        /// </summary>
        private static Sprite PillSprite(bool rival)
        {
            var cached = rival ? _rivalPillSprite : _pillSprite;
            if (cached != null) return cached;

            const int H = 64, W = 128, R = H / 2;
            var tex = BuildPillTexture(rival ? PlayerPalette.RivalPillFill : PlayerPalette.YouMarkFill,
                                       rival ? PlayerPalette.RivalPillRim : PlayerPalette.YouMarkInk);
            // Border = the rounded ends only (left / right): the whole height stretches, so a label taller than the 0.4 u sprite never opens a gap in a middle band.
            var sprite = Sprite.Create(tex, new Rect(0, 0, W, H), new Vector2(0.5f, 0.5f), H * 2.5f, 0,
                SpriteMeshType.FullRect, new Vector4(R, 0, R, 0));
            if (rival) _rivalPillSprite = sprite; else _pillSprite = sprite;
            return sprite;
        }

        /// <summary>
        /// The pill's 128 x 64 texture: an opaque capsule of <paramref name="fill"/> inside a 5 px rim of
        /// <paramref name="rim"/> (1 px anti-aliased outer edge). Public so a test can read the fill pixel: a rival's
        /// label contrast is only guaranteed while that pixel stays fully opaque.
        /// </summary>
        public static Texture2D BuildPillTexture(Color fill, Color rim)
        {
            const int H = 64, W = 128, R = H / 2;
            const float rimWidth = 5f;
            var px = new Color[W * H];
            for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                // Distance from the capsule's spine (a horizontal segment between the two end centres).
                float cx = Mathf.Clamp(x + 0.5f, R, W - R);
                float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(cx, R));
                float alpha = Mathf.Clamp01(R - d);            // 1 px anti-aliased outer edge
                float rimT = Mathf.Clamp01(d - (R - rimWidth));     // 0 inside, 1 on the rim
                var c = Color.Lerp(fill, rim, rimT);
                c.a = alpha;
                px[y * W + x] = c;
            }
            var tex = new Texture2D(W, H, TextureFormat.RGBA32, false)
            { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            tex.SetPixels(px);
            tex.Apply();
            return tex;
        }

        /// <summary>A white anti-aliased disc, 1 world unit across; tinted per renderer.</summary>
        private static Sprite DotSprite()
        {
            if (_dotSprite != null) return _dotSprite;
            const int N = 64;
            var px = new Color[N * N];
            for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(N * 0.5f, N * 0.5f));
                px[y * N + x] = new Color(1f, 1f, 1f, Mathf.Clamp01(N * 0.5f - d));
            }
            var tex = new Texture2D(N, N, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            tex.SetPixels(px);
            tex.Apply();
            _dotSprite = Sprite.Create(tex, new Rect(0, 0, N, N), new Vector2(0.5f, 0.5f), N, 0, SpriteMeshType.FullRect);
            return _dotSprite;
        }

        /// <summary>
        /// Control-state badge for the nameplate (ART.md §6.10).
        /// </summary>
        private string StateIcon()
        {
            if (_controller == null) return string.Empty;
            if (_controller.Rooted) return "🌱";

            bool slowed = _controller.SlowMultiplier < 0.999f
                          || _controller.AuraSlowActive
                          || (_cargo != null && _cargo.IsPileSlow);
            return slowed ? "🐌" : string.Empty;
        }

        private void BillboardToCamera()
        {
            var cam = Camera.main;
            if (cam != null && _textTransform != null)
            {
                var lookDir = _textTransform.position - cam.transform.position;
                if (lookDir.sqrMagnitude > 1e-6f)
                    _textTransform.rotation = Quaternion.LookRotation(lookDir, Vector3.up);
            }
        }
    }
}

using CluckWars.Gameplay;
using CluckWars.Localization;
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
        private static Sprite _pillSprite;

        private ChickenController _controller;
        private ChickenCombat     _combat;
        private ChickenCargo      _cargo;
        private TextMesh  _text;
        private MeshRenderer _textRenderer;
        private Transform _textTransform;
        private SpriteRenderer _youPill;

        private int  _appliedPlayerId  = -2;
        private ChickenClass _appliedClass;
        private bool _appliedClassValid;
        private bool _appliedLocal;
        private bool _appliedBot;
        private bool _appliedBountyActive;
        private bool _wasStunned;
        private string _appliedStateIcon = string.Empty;

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
            _youPill.sprite = PillSprite();
            _youPill.drawMode = SpriteDrawMode.Sliced;
            _youPill.color = Color.white;   // the sprite carries the gold fill and ink rim
            _youPill.sortingOrder = 1;
            _youPill.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _youPill.receiveShadows = false;
            _youPill.enabled = false;
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

            if (isNowStunned)
            {
                if (_youPill != null) _youPill.enabled = false;
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
                    if (bountyActive) label = UiText.Format(UiKeys.NameplateBounty, ("name", label));
                    _text.text = stateIcon.Length > 0 ? stateIcon + " " + label : label;
                    _text.color = isLocal ? PlayerPalette.YouMarkInk : PlayerPalette.ForCorner(corner);
                    FitYouPill(isLocal);
                }
            }

            BillboardToCamera();
        }

        /// <summary>Shows the YOU pill behind the local chicken's label, sized to the text.</summary>
        private void FitYouPill(bool show)
        {
            if (_youPill == null) return;
            _youPill.enabled = show;
            if (!show || _textRenderer == null) return;
            // localBounds is in the label's own space, so billboarding does not change it.
            var size = (Vector2)_textRenderer.localBounds.size;
            if (size.x <= 0f || size.y <= 0f) size = new Vector2(0.6f, 0.3f);   // mesh not built yet: a "You"-sized pill
            _youPill.size = size + YouPillPadding * 2f;
            _youPill.transform.localPosition = new Vector3(_textRenderer.localBounds.center.x, _textRenderer.localBounds.center.y, 0.01f);
        }

        /// <summary>
        /// The YOU mark as a 9-sliced sprite: a gold capsule with a dark-ink rim
        /// (<see cref="PlayerPalette.YouMarkFill"/> / <see cref="PlayerPalette.YouMarkInk"/>), the same
        /// pill UI Toolkit draws with <c>.cw-you-mark</c>.
        /// </summary>
        private static Sprite PillSprite()
        {
            if (_pillSprite != null) return _pillSprite;

            const int H = 64, W = 128, R = H / 2;
            const float rim = 5f;
            var fill = PlayerPalette.YouMarkFill;
            var ink = PlayerPalette.YouMarkInk;
            var px = new Color[W * H];
            for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                // Distance from the capsule's spine (a horizontal segment between the two end centres).
                float cx = Mathf.Clamp(x + 0.5f, R, W - R);
                float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(cx, R));
                float alpha = Mathf.Clamp01(R - d);            // 1 px anti-aliased outer edge
                float rimT = Mathf.Clamp01(d - (R - rim));     // 0 inside, 1 on the rim
                var c = Color.Lerp(fill, ink, rimT);
                c.a = alpha;
                px[y * W + x] = c;
            }

            var tex = new Texture2D(W, H, TextureFormat.RGBA32, false)
            { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            tex.SetPixels(px);
            tex.Apply();
            // Border = the rounded ends, so slicing stretches only the straight middle.
            _pillSprite = Sprite.Create(tex, new Rect(0, 0, W, H), new Vector2(0.5f, 0.5f), H * 2.5f, 0,
                SpriteMeshType.FullRect, new Vector4(R, R, R, R));
            return _pillSprite;
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

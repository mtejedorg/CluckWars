using CluckWars.Gameplay;
using UnityEngine;

namespace CluckWars.Visuals
{
    /// <summary>
    /// Control-state overlays that attach to the affected chicken in world space
    /// (ART.md §6.10) — stun, slow and root each get a distinct, always-additive
    /// marker so "who is what" reads instantly mid-fight. Stage 4b of the UI rebuild.
    /// </summary>
    /// <remarks>
    /// Pure local visual: every state is *observed* from replicated properties
    /// (<c>ChickenCombat.IsStunned</c>, <c>ChickenController.Rooted</c>, the slow
    /// multiplier / aura / pile flags) and nothing here is written or RPC'd, per
    /// the "animation + VFX are local" rule. The player identity ring
    /// (<see cref="ChickenWorldBars"/>) stays visible underneath every overlay, as
    /// §6.10 requires.
    ///
    /// These overlays are strictly *additive* — they add their own sprites and never
    /// touch the chicken's material or animator, so they cannot fight the hit-flash
    /// or the animator. The design's illustrated treatments that *do* mutate the
    /// body (stun desaturation + tilt, slow speed-trail, knockback motion lines /
    /// ghost trail) are deliberately left to an art pass; knockback also has no
    /// persistent replicated flag to observe, and §6.10 calls it instant anyway.
    /// </remarks>
    [RequireComponent(typeof(ChickenController))]
    public sealed class ChickenStateOverlays : MonoBehaviour
    {
        [Tooltip("Height above the pivot at which the stun stars orbit.")]
        [SerializeField] private float _starHeight = 1.62f;

        [Tooltip("Radius of the stun-star orbit.")]
        [Range(0.05f, 1f)]
        [SerializeField] private float _starOrbitRadius = 0.26f;

        [Tooltip("Stun-star orbit speed, degrees/second.")]
        [SerializeField] private float _starSpinSpeed = 180f;

        private const int StarCount = 3;

        private static readonly Color SlowColor = new Color(0.35f, 0.62f, 1f, 0.42f);
        private static readonly Color RootColor = new Color(0.32f, 0.72f, 0.28f, 0.60f);

        private static Sprite _starSprite;
        private static Sprite _discSprite;

        private ChickenController _controller;
        private ChickenCombat     _combat;
        private ChickenCargo      _cargo;

        private Transform        _starRoot;
        private SpriteRenderer   _slowBlob;
        private SpriteRenderer   _rootBlob;

        private void Awake()
        {
            _controller = GetComponent<ChickenController>();
            _combat     = GetComponent<ChickenCombat>();
            _cargo      = GetComponent<ChickenCargo>();

            BuildStars();
            _slowBlob = BuildBlob("SlowBlob", 1.05f, SlowColor, sortingOrder: 2);
            _rootBlob = BuildBlob("RootBlob", 0.74f, RootColor, sortingOrder: 3);
        }

        // ── Construction ───────────────────────────────────────────────────────

        private void BuildStars()
        {
            var root = new GameObject("StunStars");
            root.transform.SetParent(transform, worldPositionStays: false);
            root.transform.localPosition = new Vector3(0f, _starHeight, 0f);
            _starRoot = root.transform;

            for (int i = 0; i < StarCount; i++)
            {
                float ang = i * (360f / StarCount) * Mathf.Deg2Rad;
                var go = new GameObject("Star" + i);
                go.transform.SetParent(_starRoot, worldPositionStays: false);
                go.transform.localPosition = new Vector3(
                    Mathf.Cos(ang) * _starOrbitRadius, 0f, Mathf.Sin(ang) * _starOrbitRadius);
                go.transform.localScale = Vector3.one * 0.26f;

                var sr = go.AddComponent<SpriteRenderer>();
                sr.sprite       = StarSprite();
                sr.color        = new Color(1f, 0.86f, 0.25f, 1f); // cartoon gold
                sr.sortingOrder = 120;
            }

            _starRoot.gameObject.SetActive(false);
        }

        /// <summary>Soft translucent disc lying flat at the feet, under the chicken.</summary>
        private SpriteRenderer BuildBlob(string name, float diameter, Color color, int sortingOrder)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, worldPositionStays: false);
            // Just above the identity ring so both remain readable (§6.10: the ring
            // stays visible underneath every overlay).
            go.transform.localPosition = new Vector3(0f, 0.03f, 0f);
            go.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            go.transform.localScale    = Vector3.one * diameter;

            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite       = DiscSprite();
            sr.color        = color;
            sr.sortingOrder = sortingOrder;
            go.SetActive(false);
            return sr;
        }

        // ── Per-frame state read ───────────────────────────────────────────────

        private void LateUpdate()
        {
            if (_controller == null) return;

            var obj = _controller.Object;
            bool live = obj != null && obj.IsValid;

            bool stunned = live && _combat != null && _combat.IsStunned;
            // A stunned chicken is already fully told by the stars + the nameplate's
            // skull; don't stack the movement-state blobs on top of it.
            bool rooted  = live && !stunned && _controller.Rooted;
            bool slowed  = live && !stunned && !rooted && IsSlowed();

            SetActive(_starRoot != null ? _starRoot.gameObject : null, stunned);
            SetActive(_rootBlob != null ? _rootBlob.gameObject : null, rooted);
            SetActive(_slowBlob != null ? _slowBlob.gameObject : null, slowed);

            if (stunned && _starRoot != null)
                _starRoot.Rotate(Vector3.up, _starSpinSpeed * Time.deltaTime, Space.Self);

            if (slowed && _slowBlob != null)
            {
                // Gentle pulse so "slowed" reads as an active, temporary drag.
                float p = 0.85f + 0.15f * Mathf.Sin(Time.time * 4f);
                _slowBlob.transform.localScale = Vector3.one * (1.05f * p);
            }
        }

        /// <summary>
        /// Slowed = any of the three sources §6.10 lists: an ability's slow
        /// multiplier, a Feather-Aura field, or the food-pile drag.
        /// </summary>
        private bool IsSlowed()
        {
            if (_controller.SlowMultiplier < 0.999f) return true;
            if (_controller.AuraSlowActive) return true;
            return _cargo != null && _cargo.IsPileSlow;
        }

        private static void SetActive(GameObject go, bool on)
        {
            if (go != null && go.activeSelf != on) go.SetActive(on);
        }

        // ── Procedural sprites ─────────────────────────────────────────────────

        /// <summary>Soft radial disc — the slow / root ground blobs.</summary>
        private static Sprite DiscSprite()
        {
            if (_discSprite != null) return _discSprite;

            const int S = 96;
            var px = new Color[S * S];
            for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                float dx = (x + 0.5f) / S - 0.5f;
                float dy = (y + 0.5f) / S - 0.5f;
                float d  = Mathf.Sqrt(dx * dx + dy * dy) / 0.5f; // 0 centre → 1 edge
                // Solid core that feathers out over the last third of the radius.
                float a = 1f - Mathf.SmoothStep(0.62f, 1f, d);
                px[y * S + x] = new Color(1f, 1f, 1f, Mathf.Clamp01(a));
            }
            _discSprite = MakeSprite(px, S);
            return _discSprite;
        }

        /// <summary>Chunky 5-point cartoon star — the overhead stun markers.</summary>
        private static Sprite StarSprite()
        {
            if (_starSprite != null) return _starSprite;

            const int S = 64;
            const int Points = 5;
            const float outer = 0.46f, inner = 0.19f;
            var px = new Color[S * S];

            for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                float dx = (x + 0.5f) / S - 0.5f;
                float dy = (y + 0.5f) / S - 0.5f;
                float d  = Mathf.Sqrt(dx * dx + dy * dy);

                // Star radius at this angle: sweep between the outer tip and the
                // inner notch as the angle passes through each point's wedge.
                float ang = Mathf.Atan2(dy, dx) + Mathf.PI * 0.5f;      // tip up
                float seg = Mathf.PI * 2f / Points;
                float t   = Mathf.Repeat(ang, seg) / seg;               // 0..1 in wedge
                float tri = 1f - Mathf.Abs(t * 2f - 1f);                // 0 at notch, 1 at tip
                float r   = Mathf.Lerp(inner, outer, tri);

                float a = d <= r ? 1f : 0f;
                px[y * S + x] = new Color(1f, 1f, 1f, a);
            }
            _starSprite = MakeSprite(px, S);
            return _starSprite;
        }

        private static Sprite MakeSprite(Color[] px, int size)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            tex.SetPixels(px);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
        }
    }
}

using CluckWars.Gameplay;
using UnityEngine;

namespace CluckWars.Visuals
{
    /// <summary>
    /// Control-state overlays that attach to the affected chicken in world space
    /// (ART.md §6.10) — stun, slow and root each get a distinct, always-additive
    /// marker so "who is what" reads instantly mid-fight. Stage 4b of the UI rebuild,
    /// extended in Stage 5a with the per-state <b>shape</b> channel FEEDBACK.md §1.3
    /// requires.
    /// </summary>
    /// <remarks>
    /// <b>§1.3 redundant coding — this file owns the shape channel.</b> Colour alone is
    /// not allowed to carry a state: the arena already has four player-identity colours
    /// competing for attention. Each state therefore pairs its hue with a shape/motion
    /// signature that survives greyscale:
    /// <list type="bullet">
    ///   <item><b>Stun</b> — yellow + three <i>orbiting</i> stars overhead. Motion:
    ///   continuous rotation.</item>
    ///   <item><b>Root</b> — green blob + a <i>static</i> ground shackle glyph. The
    ///   deliberate absence of motion is the coding channel: side by side with a stunned
    ///   chicken, "spinning above the head" versus "locked to the floor" is legible with
    ///   no colour at all.</item>
    ///   <item><b>Slow</b> — cyan blob + trailing streaks dragging behind the chicken.
    ///   Motion, but a <i>drag</i> rather than an orbit, and pointed along the direction
    ///   of travel so it reads as resistance rather than as decoration.</item>
    /// </list>
    ///
    /// <b>Colours are canonical.</b> This file used to define its own slow/cyan and
    /// root/green literals that disagreed slightly with <see cref="ControlStateVFX"/>'s,
    /// so a chicken's status ring and its ground blob were not quite the same colour.
    /// That documented divergence (see the colour section of
    /// <see cref="FeedbackTuning"/>) is resolved here in favour of the canonical hues;
    /// only the per-overlay <i>alphas</i> are local, because a soft ground blob needs to
    /// sit much lighter than a ring stroke.
    ///
    /// <b>Pure local visual</b>: every state is *observed* from replicated properties and
    /// nothing here is written or RPC'd, per the "animation + VFX are local" rule. The
    /// player identity ring (<see cref="ChickenWorldBars"/>) stays visible underneath
    /// every overlay, as §6.10 requires.
    ///
    /// These overlays are strictly *additive* — they add their own sprites and never
    /// touch the chicken's material or animator, so they cannot fight the hit-flash
    /// (<see cref="HitFeedback"/>) or the animator.
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

        private const int StarCount   = 3;
        private const int StreakCount = 4;

        /// <summary>How fast a streak travels from the chicken's heel out to its tail, in
        /// cycles per second. 1.6 Hz reads as a steady drag — fast enough to be obviously
        /// moving, slow enough that it never competes with the stun stars' 180 deg/s
        /// orbit for "which of these is the urgent one".</summary>
        private const float StreakScrollHz = 1.6f;

        /// <summary>Planar speed at which the streaks reach full opacity. Roughly a
        /// chicken's unslowed run, so a slowed sprinter smears hard and a slowed chicken
        /// standing still shows only the faint resting streaks below.</summary>
        private const float StreakFullSpeed = 3.0f;

        /// <summary>Streak opacity floor for a stationary slowed chicken. Not zero: §1.3's
        /// shape channel has to survive standing still, or a rooted-and-slowed chicken
        /// would fall back to colour alone.</summary>
        private const float StreakRestingAlpha = 0.35f;

        // Canonical hues (FeedbackTuning), local alphas preserved from the original
        // literals so the blobs stay as soft as they have always been.
        private static readonly Color SlowColor    = WithAlpha(FeedbackTuning.CanonicalSlowColor, 0.42f);
        private static readonly Color RootColor    = WithAlpha(FeedbackTuning.CanonicalRootColor, 0.60f);
        private static readonly Color ShackleColor = WithAlpha(FeedbackTuning.CanonicalRootColor, 0.88f);
        private static readonly Color StreakColor  = WithAlpha(FeedbackTuning.CanonicalSlowColor, 0.55f);

        private static Sprite _starSprite;
        private static Sprite _discSprite;
        private static Sprite _shackleSprite;
        private static Sprite _streakSprite;

        private ChickenController _controller;
        private ChickenCombat     _combat;
        private ChickenCargo      _cargo;

        private Transform      _starRoot;
        private SpriteRenderer _slowBlob;
        private SpriteRenderer _rootBlob;
        private SpriteRenderer _rootShackle;

        private Transform        _streakRoot;
        private SpriteRenderer[] _streaks;
        private Vector3          _lastPos;
        private Vector3          _dragDir = Vector3.forward;
        private float            _smoothedSpeed;

        private void Awake()
        {
            _controller = GetComponent<ChickenController>();
            _combat     = GetComponent<ChickenCombat>();
            _cargo      = GetComponent<ChickenCargo>();

            BuildStars();
            _slowBlob    = BuildFlatSprite("SlowBlob",    DiscSprite(),    1.05f, SlowColor,    sortingOrder: 2, y: 0.03f);
            _rootBlob    = BuildFlatSprite("RootBlob",    DiscSprite(),    0.74f, RootColor,    sortingOrder: 3, y: 0.03f);
            _rootShackle = BuildFlatSprite("RootShackle", ShackleSprite(), 0.92f, ShackleColor, sortingOrder: 4, y: 0.035f);
            BuildStreaks();

            _lastPos = transform.position;
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

        /// <summary>
        /// Builds the trailing-streak set: a root that turns to face the chicken's
        /// direction of travel, with <see cref="StreakCount"/> tapered smears parented
        /// under it at evenly-spread phases. They are children of the chicken (not
        /// world-anchored breadcrumbs) on purpose — a deposited trail would read as a
        /// path someone walked, whereas an attached drag reads as the chicken being held
        /// back right now, which is what "slowed" means. It also keeps the whole effect
        /// allocation-free and teleport-proof.
        /// </summary>
        private void BuildStreaks()
        {
            var root = new GameObject("SlowStreaks");
            root.transform.SetParent(transform, worldPositionStays: false);
            root.transform.localPosition = new Vector3(0f, 0.04f, 0f);
            _streakRoot = root.transform;

            _streaks = new SpriteRenderer[StreakCount];
            for (int i = 0; i < StreakCount; i++)
            {
                var go = new GameObject("Streak" + i);
                go.transform.SetParent(_streakRoot, worldPositionStays: false);
                // Flat on the ground; the sprite's +Y (its wide end) maps to the root's
                // +Z, i.e. back toward the chicken it is trailing from.
                go.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);

                var sr = go.AddComponent<SpriteRenderer>();
                sr.sprite       = StreakSprite();
                sr.color        = StreakColor;
                sr.sortingOrder = 5;
                _streaks[i] = sr;
            }

            _streakRoot.gameObject.SetActive(false);
        }

        /// <summary>Flat ground sprite lying under the chicken. Just above the identity
        /// ring so both remain readable (§6.10: the ring stays visible underneath every
        /// overlay).</summary>
        private SpriteRenderer BuildFlatSprite(string goName, Sprite sprite, float diameter,
                                               Color color, int sortingOrder, float y)
        {
            var go = new GameObject(goName);
            go.transform.SetParent(transform, worldPositionStays: false);
            go.transform.localPosition = new Vector3(0f, y, 0f);
            go.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            go.transform.localScale    = Vector3.one * diameter;

            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite       = sprite;
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

            var flags = _controller.ControlFlags;
            bool stunned = live && ((_combat != null && _combat.IsRemoved) ||
                                    (flags & ControlVfx.Stunned) != 0);
            // A stunned chicken is already fully told by the stars + the nameplate's
            // skull; don't stack the movement-state blobs on top of it.
            bool rooted  = live && !stunned && (flags & ControlVfx.Rooted) != 0;
            bool slowed  = live && !stunned && !rooted && IsSlowed(flags);

            SetActive(_starRoot != null ? _starRoot.gameObject : null, stunned);
            SetActive(_rootBlob != null ? _rootBlob.gameObject : null, rooted);
            SetActive(_rootShackle != null ? _rootShackle.gameObject : null, rooted);
            SetActive(_slowBlob != null ? _slowBlob.gameObject : null, slowed);
            SetActive(_streakRoot != null ? _streakRoot.gameObject : null, slowed);

            if (stunned && _starRoot != null)
                _starRoot.Rotate(Vector3.up, _starSpinSpeed * Time.deltaTime, Space.Self);

            // The root shackle is deliberately left alone here — no spin, no pulse. Its
            // stillness is the §1.3 shape channel against stun's orbit; animating it would
            // delete the very thing that distinguishes the two states in greyscale.

            if (slowed && _slowBlob != null)
            {
                // Gentle pulse so "slowed" reads as an active, temporary drag.
                float p = 0.85f + 0.15f * Mathf.Sin(Time.time * 4f);
                _slowBlob.transform.localScale = Vector3.one * (1.05f * p);
            }

            TrackMovement();
            if (slowed) UpdateStreaks();
        }

        /// <summary>
        /// Tracks planar movement from the transform itself rather than from any velocity
        /// property, so it works identically on a remote peer (where only the networked
        /// transform is interpolated) and on the authority. Runs every frame, slowed or
        /// not, so the drag direction is already correct on the first frame of a slow
        /// instead of snapping into place.
        /// </summary>
        private void TrackMovement()
        {
            Vector3 pos   = transform.position;
            Vector3 delta = pos - _lastPos;
            _lastPos = pos;
            delta.y  = 0f;

            float dt = Time.deltaTime;
            float speed = dt > 0f ? delta.magnitude / dt : 0f;
            _smoothedSpeed = Mathf.Lerp(_smoothedSpeed, speed, 1f - Mathf.Exp(-8f * dt));

            if (delta.sqrMagnitude > 1e-6f) _dragDir = delta.normalized;
        }

        private void UpdateStreaks()
        {
            if (_streakRoot == null || _streaks == null) return;

            _streakRoot.rotation = Quaternion.LookRotation(_dragDir, Vector3.up);

            float intensity = Mathf.Lerp(StreakRestingAlpha, 1f,
                                         Mathf.Clamp01(_smoothedSpeed / StreakFullSpeed));

            for (int i = 0; i < _streaks.Length; i++)
            {
                var sr = _streaks[i];
                if (sr == null) continue;

                float phase = Mathf.Repeat(Time.time * StreakScrollHz + i / (float)_streaks.Length, 1f);

                // Travel backwards away from the heel, shrinking and fading as it goes.
                float dist  = Mathf.Lerp(0.18f, 1.00f, phase);
                float len   = Mathf.Lerp(0.46f, 0.20f, phase);
                float fade  = (1f - phase) * Mathf.Clamp01(phase * 6f); // fade in fast, out slow

                var t = sr.transform;
                t.localPosition = new Vector3(0f, 0f, -dist);
                t.localScale    = new Vector3(0.30f, len, 1f);

                Color c = StreakColor;
                c.a *= fade * intensity;
                sr.color = c;
            }
        }

        /// <summary>
        /// Slowed = any of the three sources §6.10 lists: an ability's slow
        /// multiplier, a Feather-Aura field, or the food-pile drag.
        /// </summary>
        /// <remarks>
        /// <c>ControlFlags</c> is checked <i>first</i> and is the only source that works on
        /// a remote peer: <c>SlowMultiplier</c> is recomputed StateAuthority-side each tick
        /// and reads as 1 everywhere else, so without the flag a rival's collision- or
        /// zone-slow would be invisible to everyone but its owner — exactly the §7 case-21
        /// "a rival's control state, from range" gap. The remaining checks are kept because
        /// they are true one frame earlier on the owning peer than the replicated flag is.
        /// </remarks>
        private bool IsSlowed(ControlVfx flags)
        {
            if ((flags & ControlVfx.Slowed) != 0) return true;
            if (_controller.SlowMultiplier < 0.999f) return true;
            if (_controller.AuraSlowActive) return true;
            return _cargo != null && _cargo.IsPileSlow;
        }

        private static void SetActive(GameObject go, bool on)
        {
            if (go != null && go.activeSelf != on) go.SetActive(on);
        }

        private static Color WithAlpha(Color c, float a) => new Color(c.r, c.g, c.b, a);

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

        /// <summary>
        /// Static ground shackle — an annulus broken into six chunky chain links with
        /// clear gaps between them (§1.3's "static ground shackle glyph"). Hard-edged and
        /// high-contrast on purpose: against the soft feathered <see cref="DiscSprite"/>
        /// underneath it, the link pattern is what still separates "rooted" from "slowed"
        /// once colour is taken away.
        /// </summary>
        private static Sprite ShackleSprite()
        {
            if (_shackleSprite != null) return _shackleSprite;

            const int   S          = 96;
            const int   Links      = 6;
            const float BandCenter = 0.72f; // as a fraction of the half-texture
            const float BandHalf   = 0.17f;
            const float Gap        = 0.11f; // fraction of each link's wedge left empty

            var px = new Color[S * S];
            for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                float dx = (x + 0.5f) / S - 0.5f;
                float dy = (y + 0.5f) / S - 0.5f;
                float rn = Mathf.Sqrt(dx * dx + dy * dy) / 0.5f; // 0 centre → 1 edge

                // Radial: 1 inside the band, feathering over its outer 40%.
                float radial = 1f - Mathf.SmoothStep(BandHalf * 0.60f, BandHalf, Mathf.Abs(rn - BandCenter));

                // Angular: carve the band into Links wedges, each with a gap at both ends.
                float ang  = Mathf.Atan2(dy, dx) + Mathf.PI;         // 0..2pi
                float seg  = Mathf.PI * 2f / Links;
                float t    = Mathf.Repeat(ang, seg) / seg;           // 0..1 within the wedge
                float edge = Gap * 0.5f;
                float angular = Mathf.SmoothStep(Gap - edge, Gap + edge, t) *
                                (1f - Mathf.SmoothStep(1f - Gap - edge, 1f - Gap + edge, t));

                px[y * S + x] = new Color(1f, 1f, 1f, Mathf.Clamp01(radial * angular));
            }
            _shackleSprite = MakeSprite(px, S);
            return _shackleSprite;
        }

        /// <summary>
        /// A single trailing streak: wide and soft at the top of the texture (the end that
        /// sits nearest the chicken), tapering to a point at the bottom. Drawn tapered
        /// rather than as a plain capsule so the streak encodes a <i>direction</i> — which
        /// is what makes a drag read as a drag rather than as a smudge.
        /// </summary>
        private static Sprite StreakSprite()
        {
            if (_streakSprite != null) return _streakSprite;

            const int S = 64;
            var px = new Color[S * S];
            for (int y = 0; y < S; y++)
            {
                float ny    = (y + 0.5f) / S;                        // 0 tail tip → 1 near chicken
                float halfW = Mathf.Lerp(0.015f, 0.20f, ny);
                // Fade in from the tail tip, and soften the near end so it melts into the body.
                float along = Mathf.SmoothStep(0f, 0.30f, ny) * (1f - Mathf.SmoothStep(0.80f, 1f, ny));

                for (int x = 0; x < S; x++)
                {
                    float dxn = Mathf.Abs((x + 0.5f) / S - 0.5f);
                    float across = 1f - Mathf.SmoothStep(halfW * 0.45f, halfW, dxn);
                    px[y * S + x] = new Color(1f, 1f, 1f, Mathf.Clamp01(across * along));
                }
            }
            _streakSprite = MakeSprite(px, S);
            return _streakSprite;
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

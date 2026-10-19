using CluckWars.Gameplay;
using CluckWars.Logging;
using CluckWars.UI;
using UnityEngine;
using Zenject;

namespace CluckWars.Visuals
{
    /// <summary>
    /// Makes rivals findable (playtest note 2 of 5, 2026-08-14). Maestro: *"it is quite hard
    /// to find the opponents in the map, at least with bots."*
    ///
    /// <para>
    /// Two presentations of one idea, switched on whether the rival is in frame:
    /// a <b>ground ring</b> under an on-screen rival, and a <b>chevron</b> pinned to the view
    /// edge pointing at an off-screen one. Never both — an on-screen rival is its own
    /// indicator and does not also need an arrow. "On screen" means any part of the body in
    /// frame (hysteresis on the way out), and the chevron is kept out of the HUD control
    /// zones (<see cref="HudControlZones"/>) by sliding along the edge band.
    /// </para>
    /// <para>
    /// <b>Both are tinted with the rival's PLAYER colour</b> (<see cref="PlayerPalette.ForCorner"/>
    /// of <see cref="ChickenController.HomeCornerIndex"/>) — the same source as the nameplate
    /// dot and the identity ring (Phase 8 decision E1 a). Class identity stays in the chicken
    /// itself: class colours were ~9 degrees apart for Warrior / Speedy, two rivals of one class
    /// got identical arrows, and P1 aqua never reached them.
    /// </para>
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Purely local presentation.</b> No <c>[Networked]</c> state, no RPCs, nothing added
    /// to the input struct. Everything it reads — <c>Class</c>, <c>VisualOpacity</c>,
    /// position, death — is already replicated for other reasons, so every peer derives its
    /// own indicators independently. Per CONVENTIONS, visuals are never networked.
    /// </para>
    /// <para>
    /// <b>One component per chicken</b>, on <c>Chicken.prefab</c>, matching
    /// <see cref="ChickenStateOverlays"/> and <see cref="HitFeedback"/>. Each instance asks
    /// only "am I a rival of the viewer on this peer, and where am I", so there is no
    /// central registry scan and no <c>Physics.Overlap*</c> — which
    /// <c>AbilityAimTests.NoAbilityScript_CallsPhysicsOverlapDirectly</c> exists to prevent.
    /// </para>
    /// <para>
    /// <b>Provisional by request</b> — Maestro: *"will remove later if it doesn't work."*
    /// <see cref="FeedbackTuning.RivalIndicatorsEnabled"/> is the single kill switch and
    /// <see cref="Awake"/> honours it before building any geometry.
    /// </para>
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class RivalIndicator : MonoBehaviour
    {
        private const string Source = "RivalIndicator";

        /// <summary>
        /// A rival faded below this gets no indicator of any kind. <b>Any</b> fade counts as
        /// "this chicken is trying not to be seen", so this sits just under 1, not near 0.
        /// </summary>
        /// <remarks>
        /// <b>This is what stops the feature from cancelling the Invisibility ability</b>, and
        /// the exact value is load-bearing. The first version of this used 0.05 — reasoning
        /// that alpha is multiplied by opacity anyway, so the threshold only had to kill a
        /// one-pixel ghost at the bottom of the ramp. That was wrong:
        /// <see cref="InvisibilityAbilitySO.Opacity"/> is authored at <b>0.2</b>
        /// (<c>Invisibility.asset</c>), comfortably ABOVE 0.05, so a cloaked rival still drew
        /// a full-shaped chevron at alpha 0.85 x 0.2 ≈ 0.17 — sorted above all arena geometry
        /// and sized to catch peripherally. The gate was airtight against a literal zero and
        /// against nothing else that actually ships.
        ///
        /// Off-screen is the damaging case: with no model in frame to compare against, the
        /// chevron becomes the ONLY signal of a cloaked rival's direction — precisely what the
        /// ability exists to deny. The ring is suppressed on the same threshold rather than a
        /// looser one, because "invisible" should mean unmarked, and one rule is easier to
        /// keep correct than two.
        ///
        /// Safe at 0.9 because the only writers of <see cref="ChickenController.VisualOpacity"/>
        /// are Invisibility (0.2) and the resets to 1 — verified by grep, not assumed. If a
        /// future effect introduces a partial fade that should still be trackable, this needs
        /// to become a dedicated "hidden from rival indicators" flag on the ability rather than
        /// a magnitude test on opacity.
        /// </remarks>
        private const float MinVisibleOpacity = 0.9f;

        /// <summary>Test seam for <see cref="MinVisibleOpacity"/> — see
        /// <c>ScreenEdgeProjectionTests.RivalIndicatorOpacityGate_SuppressesAnInvisibleRival</c>,
        /// which asserts it against <c>InvisibilityAbilitySO.Opacity</c> rather than a literal.</summary>
        public static float MinVisibleOpacityForTests => MinVisibleOpacity;

        /// <summary>Ring sits under the §5.1 control-state overlays (orders 2–5), which are
        /// transient and must win; the rival ring is permanent furniture.</summary>
        private const int RingSortingOrder = 1;

        /// <summary>The chevron is screen furniture and must beat every piece of arena
        /// geometry and every world overlay it is drawn over.</summary>
        private const int ChevronSortingOrder = 250;

        private static Sprite _chevronSprite;
        private static Sprite _chevronOutlineSprite;

        /// <summary>The chevron's dark ink outline (Phase 8 decision E1, in the spirit of D9's "dark ink edge on light
        /// ground"): the brand ink of the rival nameplate pill, so the arrow and the pill read as one family. No seat colour
        /// clears 3:1 against the sunlit grass on luminance alone (measured 1.2-2.0), while the ink edge at
        /// <see cref="FeedbackTuning.RivalChevronOutlineAlpha"/> does, and every seat still parts from the ink (the darkest,
        /// P4 teal, ~3.6:1), so no seat needs a cream edge instead.</summary>
        public static Color ChevronOutlineColor => PlayerPalette.RivalPillFill;

        /// <summary>Sorting order of the outline: directly behind the chevron fill, above all arena geometry.</summary>
        public const int ChevronOutlineSortingOrder = ChevronSortingOrder - 1;

        [Tooltip("Phase 8 D8: Art/Materials/RingGhost.mat. Draws the part of the rival ring hidden under a food-pile mound as a faint ghost (GroundRingStyle.ApplyGhost).")]
        [SerializeField] private Material _ringGhostMaterial;

        private ChickenController _controller;
        private CharacterController _body;      // body extent for the on-screen test (capsule bounds)
        private float _bodyHalfWidth;           // class model's lateral half-width (SetBodyFootprint), widens the capsule
        private bool _wasOnScreen;              // last frame's on-screen verdict: the hysteresis memory
        private ILogService _log;

        private SpriteRenderer _ring;
        private SpriteRenderer _ringEdge;      // D9 two-tone edge: ink band + cream rim (GroundRingStyle)
        private SpriteRenderer _ringGhost;     // D8: the arc hidden by a food-pile mound, drawn faint (RingGhost.mat)
        private float _ringRadius = FeedbackTuning.RivalRingRadius;
        private SpriteRenderer _chevron;
        private SpriteRenderer _chevronOutline; // ink edge child, drawn behind the fill (E1)

        [Inject]
        public void Construct(ILogService log) => _log = log;

        /// <summary>The tint of a rival seated at <paramref name="corner"/>: the player colour, or
        /// <see cref="PlayerPalette.Neutral"/> while the corner is not stamped yet — exactly what the
        /// nameplate dot and the identity ring do (<see cref="ChickenWorldBars"/>). A legitimate
        /// transient state, so it is deliberately not logged and has no class-colour fallback.</summary>
        public static Color TintFor(int corner) => PlayerPalette.ForCorner(corner);

        private void Awake()
        {
            // Kill switch first, before any allocation: "off" must mean no geometry, no
            // per-frame cost and nothing left in the hierarchy — not a hidden object.
            if (!FeedbackTuning.RivalIndicatorsEnabled)
            {
                enabled = false;
                return;
            }

            // Self-injection per CONVENTIONS — not gated on HasInstance.
            if (_log == null && ProjectContext.HasInstance)
                ProjectContext.Instance.Container.Inject(this);

            _controller = GetComponent<ChickenController>();
            _body = GetComponent<CharacterController>();
            if (_controller == null)
            {
                _log?.Error(Source,
                    "No ChickenController on this GameObject — cannot tell rivals from the " +
                    "local player, so the indicator would mark everyone. Disabling.");
                enabled = false;
                return;
            }

            BuildGeometry();
        }

        /// <summary>
        /// The chevron lives unparented at the scene root and is only ever updated from
        /// <see cref="LateUpdate"/>, so disabling this component without destroying it would
        /// freeze both indicators on screen forever. Nothing in the game does that today, but
        /// <c>enabled = false</c> is an ordinary Unity operation (a settings toggle, a test
        /// harness) and the ghost would be baffling.
        /// </summary>
        private void OnDisable() => Hide();

        private void OnDestroy()
        {
            // The chevron lives at the scene root (see BuildGeometry), so it does not die
            // with the chicken and has to be taken down by hand.
            if (_chevron != null) Destroy(_chevron.gameObject);
        }

        private void BuildGeometry()
        {
            var ringGo = new GameObject("RivalRing");
            ringGo.transform.SetParent(ChickenGround.Anchor(this), worldPositionStays: false);
            ringGo.transform.localPosition = new Vector3(0f, 0.02f, 0f);
            ringGo.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            _ring = ringGo.AddComponent<SpriteRenderer>();
            _ring.sortingOrder = RingSortingOrder;
            ApplyRingGeometry();
            if (_ringGhost == null)
                _log?.Error(Source, "_ringGhostMaterial is not assigned: the rival ring's arc under a food pile is not drawn (Phase 8 D8).");
            ringGo.SetActive(false);

            // NOT parented to the chicken. The chevron is screen furniture: it is positioned
            // from the camera every frame and must not inherit the chicken's position,
            // rotation or the scale its class model carries.
            var chevGo = new GameObject($"RivalChevron ({name})");
            _chevron = chevGo.AddComponent<SpriteRenderer>();
            _chevron.sprite = ChevronSprite();
            _chevron.sortingOrder = ChevronSortingOrder;

            // Ink outline as a child renderer rather than baked into the fill sprite: the fill is tinted by
            // SpriteRenderer.color (the player colour), which would tint a baked edge too. As a child it inherits the
            // chevron's position / rotation / scale, so shape, size and safe-area placement stay exactly the fill's.
            var edgeGo = new GameObject("RivalChevronOutline");
            edgeGo.transform.SetParent(chevGo.transform, worldPositionStays: false);
            _chevronOutline = edgeGo.AddComponent<SpriteRenderer>();
            _chevronOutline.sprite = ChevronOutlineSprite();
            _chevronOutline.sortingOrder = ChevronOutlineSortingOrder;
            _chevronOutline.color = WithAlpha(ChevronOutlineColor, FeedbackTuning.RivalChevronOutlineAlpha);
            chevGo.SetActive(false);
        }

        /// <summary>Sizes the rival ring to sit just outside the (body-sized) identity ring (Phase 8 chunk C R3).
        /// <paramref name="bodyHalfWidth"/> is the class model's lateral half-width, root-local metres.</summary>
        public void SetBodyFootprint(float bodyHalfWidth)
        {
            _bodyHalfWidth = bodyHalfWidth;
            _ringRadius = GroundRingStyle.RivalRadius(bodyHalfWidth);
            if (_ring != null) ApplyRingGeometry();
        }

        private void ApplyRingGeometry()
        {
            GroundRingStyle.Apply(_ring, ref _ringEdge, _ringRadius, FeedbackTuning.RivalRingWidth,
                GroundRingStyle.RivalEdge, GroundRingStyle.RivalRim, GroundRingStyle.RivalEdgeAlpha);
            GroundRingStyle.ApplyGhost(_ring, ref _ringGhost, _ringGhostMaterial);
        }

        private void LateUpdate()
        {
            if (_controller == null) return;

            var cam = MatchCamera.Instance != null ? MatchCamera.Instance.Camera : null;
            if (cam == null) cam = Camera.main;

            if (!ShouldShow(cam, out float opacity))
            {
                _wasOnScreen = false;
                Hide();
                return;
            }

            var tint = TintFor(_controller.HomeCornerIndex);

            // On screen = ANY part of the body in frame (with hysteresis on the way out), not just the feet: a bird whose feet
            // are below the bottom edge but whose body is in view is its own indicator and must not also get an arrow on top.
            _wasOnScreen = IsOnScreen(cam);

            if (!_wasOnScreen && ScreenEdgeProjection.TryProject(cam, transform.position,
                    FeedbackTuning.RivalChevronSafeArea,
                    HudControlZones.Current(cam), ChevronClearance(cam),
                    out Vector2 clamped, out Vector2 bearing))
            {
                ShowChevron(cam, clamped, bearing, tint, opacity);
            }
            else
            {
                ShowRing(tint, opacity);
            }
        }

        /// <summary>Whether any part of this chicken's body is inside the viewport (see
        /// <see cref="ScreenEdgeProjection.IsPartlyOnScreen"/>). The body is the CharacterController capsule, widened to the
        /// model's lateral half-width; a chicken without one falls back to its feet point.</summary>
        private bool IsOnScreen(Camera cam)
        {
            Bounds b;
            if (_body != null && _body.enabled)
            {
                b = _body.bounds;
                float r = Mathf.Max(b.extents.x, b.extents.z, _bodyHalfWidth);
                b.extents = new Vector3(r, b.extents.y, r);
            }
            else
            {
                b = new Bounds(transform.position, new Vector3(_bodyHalfWidth * 2f, 0f, _bodyHalfWidth * 2f));
            }

            return ScreenEdgeProjection.TryProjectBounds(cam, b, out Rect vp)
                   && ScreenEdgeProjection.IsPartlyOnScreen(vp, _wasOnScreen, FeedbackTuning.RivalOnScreenHysteresis);
        }

        /// <summary>Half the chevron's footprint in viewport units (x scaled by 1/aspect because the chevron is sized in
        /// fractions of the viewport HEIGHT): how far from a HUD control rect the arrow's centre must stay.</summary>
        private static Vector2 ChevronClearance(Camera cam)
        {
            float r = FeedbackTuning.RivalChevronSizeFraction * FeedbackTuning.RivalChevronClearanceRadius;
            return new Vector2(r / Mathf.Max(0.1f, cam.aspect), r);
        }

        /// <summary>
        /// Whether this chicken is a rival the viewer should be helped to find.
        /// </summary>
        /// <remarks>
        /// <b>Rival means <c>!HasInputAuthority</c>, and that is deliberate on decoys too.</b>
        /// A Doppelganger decoy shares input authority with its caster (see the remarks on
        /// <see cref="HitFeedback"/>), so the caster's own decoy is correctly excluded, while
        /// an *enemy* decoy gets a full indicator. That is the point of a decoy: it is a
        /// valid ability target by Maestro's 2026-08-02 call, so an indicator that quietly
        /// skipped decoys would hand every player a free tell for spotting the fake.
        /// </remarks>
        private bool ShouldShow(Camera cam, out float opacity)
        {
            opacity = 0f;
            if (cam == null) return false;

            var obj = _controller.Object;
            if (obj == null || !obj.IsValid) return false;
            if (_controller.HasInputAuthority) return false;

            var combat = _controller.Combat;
            if (combat != null && combat.IsDead) return false;

            opacity = Mathf.Clamp01(_controller.VisualOpacity);
            return opacity >= MinVisibleOpacity;
        }

        private void ShowRing(Color tint, float opacity)
        {
            if (_chevron != null && _chevron.gameObject.activeSelf) _chevron.gameObject.SetActive(false);
            if (_ring == null) return;

            _ring.color = WithAlpha(tint, FeedbackTuning.RivalRingAlpha * opacity);
            GroundRingStyle.SetEdgeAlpha(_ringEdge, GroundRingStyle.RivalEdgeAlpha * opacity);
            GroundRingStyle.SyncGhost(_ring, _ringGhost, FeedbackTuning.RivalRingAlpha);
            if (!_ring.gameObject.activeSelf) _ring.gameObject.SetActive(true);
        }

        private void ShowChevron(Camera cam, Vector2 clamped, Vector2 bearing, Color tint, float opacity)
        {
            if (_ring != null && _ring.gameObject.activeSelf) _ring.gameObject.SetActive(false);
            if (_chevron == null) return;

            float depth = cam.nearClipPlane + FeedbackTuning.RivalChevronDepthOffset;
            var t = _chevron.transform;
            t.position = cam.ViewportToWorldPoint(new Vector3(clamped.x, clamped.y, depth));

            // Bearing is in viewport axes; rebuild it in the camera's basis so the arrow
            // points the right way whatever the match camera's yaw happens to be.
            Vector3 up = cam.transform.right * bearing.x + cam.transform.up * bearing.y;
            if (up.sqrMagnitude < 1e-8f) up = cam.transform.up;
            t.rotation = Quaternion.LookRotation(cam.transform.forward, up);

            float size = ScreenEdgeProjection.ViewportWorldHeight(cam, FeedbackTuning.RivalChevronDepthOffset)
                         * FeedbackTuning.RivalChevronSizeFraction;
            t.localScale = Vector3.one * Mathf.Max(0.0001f, size);

            _chevron.color = WithAlpha(tint, FeedbackTuning.RivalChevronAlpha * opacity);
            if (_chevronOutline != null)
                _chevronOutline.color = WithAlpha(ChevronOutlineColor, FeedbackTuning.RivalChevronOutlineAlpha * opacity);
            if (!_chevron.gameObject.activeSelf) _chevron.gameObject.SetActive(true);
        }

        /// <summary>Test seams: light the ring / chevron for a rival at <paramref name="corner"/> without a
        /// networked controller (Phase8ChunkE1Tests).</summary>
        public void ShowRingForTests(int corner, float opacity) => ShowRing(TintFor(corner), opacity);

        public void ShowChevronForTests(Camera cam, int corner, float opacity) =>
            ShowChevron(cam, new Vector2(0.5f, 0.9f), Vector2.up, TintFor(corner), opacity);

        private void Hide()
        {
            if (_ring != null && _ring.gameObject.activeSelf) _ring.gameObject.SetActive(false);
            if (_chevron != null && _chevron.gameObject.activeSelf) _chevron.gameObject.SetActive(false);
        }

        private static Color WithAlpha(Color c, float a) => new Color(c.r, c.g, c.b, Mathf.Clamp01(a));

        // ── Procedural sprites ─────────────────────────────────────────────────
        // Same approach as ChickenStateOverlays': generated once, shared statically, so the
        // feature adds no art dependency and can be deleted without orphaning assets.

        /// <summary>
        /// Solid triangular chevron pointing along +Y in sprite space, with the stroke
        /// thickness of <see cref="FeedbackTuning.RivalChevronWidthRatio"/>. Drawn as a
        /// filled arrowhead rather than an outline: at the size this appears on a phone an
        /// outline collapses into a smudge, and a solid silhouette is what reads
        /// peripherally, which is the entire job. The thin ink edge round it
        /// (<see cref="ChevronOutlineSprite"/>) is a rim on that solid shape, not a
        /// replacement for it.
        /// </summary>
        private static Sprite ChevronSprite()
        {
            if (_chevronSprite != null) return _chevronSprite;

            var px = new Color[ChevronTexels * ChevronTexels];
            for (int y = 0; y < ChevronTexels; y++)
            for (int x = 0; x < ChevronTexels; x++)
                px[y * ChevronTexels + x] = new Color(1f, 1f, 1f, ChevronFill(x, y) ? 1f : 0f);

            _chevronSprite = MakeSprite(px, ChevronTexels, ChevronTexels);
            return _chevronSprite;
        }

        /// <summary>Fill texture size; the sprite is one world unit across (pixels per unit = this).</summary>
        private const int ChevronTexels = 96;

        /// <summary>Transparent margin the outline sprite adds on every side, so the edge round the tip (2 texels from the
        /// fill sprite's top) is not clipped. Must exceed the outline radius.</summary>
        private const int ChevronOutlinePad = 5;

        /// <summary>Whether fill texel (<paramref name="x"/>, <paramref name="y"/>) is inside the arrowhead. Shared by the fill
        /// and the outline so the outline hugs exactly the shape that is drawn.</summary>
        private static bool ChevronFill(int x, int y)
        {
            if (x < 0 || y < 0 || x >= ChevronTexels || y >= ChevronTexels) return false;
            const int S = ChevronTexels;
            float halfBase = 0.5f * FeedbackTuning.RivalChevronWidthRatio * 2.2f;
            float u = (x + 0.5f) / S - 0.5f;   // -0.5 .. 0.5
            float v = (y + 0.5f) / S - 0.5f;   // -0.5 bottom .. 0.5 tip

            // Triangle: full width at the base, converging to a point at the tip.
            float t = Mathf.InverseLerp(-0.45f, 0.48f, v);
            float halfWidth = Mathf.Lerp(0.42f, 0.0f, t);
            bool inside = v >= -0.45f && v <= 0.48f && Mathf.Abs(u) <= halfWidth;

            // Notch the base into a chevron rather than a plain triangle.
            bool notch = v < -0.45f + halfBase && Mathf.Abs(u) < halfWidth * 0.55f;
            return inside && !notch;
        }

        /// <summary>
        /// The ink edge: every texel within <see cref="FeedbackTuning.RivalChevronOutlineRatio"/> of the fill (soft by half a
        /// texel), plus the fill's own boundary texels so bilinear filtering leaves no see-through seam between fill and edge.
        /// Same pixels-per-unit as the fill and <see cref="ChevronOutlinePad"/> texels larger on every side, centred, so it
        /// sits exactly round the fill at the fill's transform.
        /// </summary>
        private static Sprite ChevronOutlineSprite()
        {
            if (_chevronOutlineSprite != null) return _chevronOutlineSprite;

            int n = ChevronTexels + 2 * ChevronOutlinePad;
            float r = FeedbackTuning.RivalChevronOutlineRatio * ChevronTexels;
            int win = Mathf.CeilToInt(r) + 1;
            var px = new Color[n * n];
            for (int oy = 0; oy < n; oy++)
            for (int ox = 0; ox < n; ox++)
            {
                int fx = ox - ChevronOutlinePad, fy = oy - ChevronOutlinePad;
                float a;
                if (ChevronFill(fx, fy))
                {
                    bool boundary = !ChevronFill(fx + 1, fy) || !ChevronFill(fx - 1, fy) ||
                                    !ChevronFill(fx, fy + 1) || !ChevronFill(fx, fy - 1);
                    a = boundary ? 1f : 0f;
                }
                else
                {
                    float best = float.MaxValue;
                    for (int dy = -win; dy <= win; dy++)
                    for (int dx = -win; dx <= win; dx++)
                        if (ChevronFill(fx + dx, fy + dy)) best = Mathf.Min(best, dx * dx + dy * dy);
                    a = best == float.MaxValue ? 0f : Mathf.Clamp01(r + 0.5f - Mathf.Sqrt(best));
                }
                px[oy * n + ox] = new Color(1f, 1f, 1f, a);
            }

            _chevronOutlineSprite = MakeSprite(px, n, ChevronTexels);
            return _chevronOutlineSprite;
        }

        private static Sprite MakeSprite(Color[] px, int size, int pixelsPerUnit)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };
            tex.SetPixels(px);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), pixelsPerUnit);
        }
    }
}

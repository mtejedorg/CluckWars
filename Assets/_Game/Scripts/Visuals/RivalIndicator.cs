using CluckWars.Gameplay;
using CluckWars.Logging;
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
    /// a class-tinted <b>ground ring</b> under an on-screen rival, and a class-tinted
    /// <b>chevron</b> pinned to the view edge pointing at an off-screen one. Exactly one is
    /// ever lit — an on-screen rival is its own indicator and does not also need an arrow.
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

        private static Sprite _ringSprite;
        private static Sprite _chevronSprite;

        private ChickenController _controller;
        private ChickenClassRegistrySO _registry;
        private ILogService _log;

        private SpriteRenderer _ring;
        private SpriteRenderer _chevron;
        private Color _classColor = Color.white;
        private bool _colorResolved;

        [Inject]
        public void Construct(ChickenClassRegistrySO registry, ILogService log)
        {
            _registry = registry;
            _log = log;
        }

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
            if (_registry == null && ProjectContext.HasInstance)
                ProjectContext.Instance.Container.Inject(this);

            _controller = GetComponent<ChickenController>();
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
            ringGo.transform.SetParent(transform, worldPositionStays: false);
            ringGo.transform.localPosition = new Vector3(0f, 0.02f, 0f);
            ringGo.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            ringGo.transform.localScale = Vector3.one * (FeedbackTuning.RivalRingRadius * 2f);
            _ring = ringGo.AddComponent<SpriteRenderer>();
            _ring.sprite = RingSprite();
            _ring.sortingOrder = RingSortingOrder;
            ringGo.SetActive(false);

            // NOT parented to the chicken. The chevron is screen furniture: it is positioned
            // from the camera every frame and must not inherit the chicken's position,
            // rotation or the scale its class model carries.
            var chevGo = new GameObject($"RivalChevron ({name})");
            _chevron = chevGo.AddComponent<SpriteRenderer>();
            _chevron.sprite = ChevronSprite();
            _chevron.sortingOrder = ChevronSortingOrder;
            chevGo.SetActive(false);
        }

        private void LateUpdate()
        {
            if (_controller == null) return;

            var cam = MatchCamera.Instance != null ? MatchCamera.Instance.Camera : null;
            if (cam == null) cam = Camera.main;

            if (!ShouldShow(cam, out float opacity))
            {
                Hide();
                return;
            }

            if (!_colorResolved) ResolveClassColor();

            if (ScreenEdgeProjection.TryProject(cam, transform.position,
                    FeedbackTuning.RivalChevronSafeArea,
                    out Vector2 clamped, out Vector2 bearing))
            {
                ShowChevron(cam, clamped, bearing, opacity);
            }
            else
            {
                ShowRing(opacity);
            }
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

        private void ResolveClassColor()
        {
            if (_registry == null)
            {
                // White still reads as an indicator; only the per-class identity is lost.
                _log?.Warn(Source,
                    "No ChickenClassRegistrySO injected — rival indicators fall back to white " +
                    "and lose their class identity.");
                _colorResolved = true;
                return;
            }

            _classColor = _registry.GetOrDefault(_controller.Class).TintColor;
            _colorResolved = true;
        }

        private void ShowRing(float opacity)
        {
            if (_chevron != null && _chevron.gameObject.activeSelf) _chevron.gameObject.SetActive(false);
            if (_ring == null) return;

            _ring.color = WithAlpha(_classColor, FeedbackTuning.RivalRingAlpha * opacity);
            if (!_ring.gameObject.activeSelf) _ring.gameObject.SetActive(true);
        }

        private void ShowChevron(Camera cam, Vector2 clamped, Vector2 bearing, float opacity)
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

            _chevron.color = WithAlpha(_classColor, FeedbackTuning.RivalChevronAlpha * opacity);
            if (!_chevron.gameObject.activeSelf) _chevron.gameObject.SetActive(true);
        }

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
        /// Hard-edged annulus. Its stroke is <see cref="FeedbackTuning.RivalRingWidth"/>
        /// expressed as a fraction of the ring's own diameter, so the drawn ring is the
        /// authored width at the authored radius rather than whatever the texture happened
        /// to bake in.
        /// </summary>
        private static Sprite RingSprite()
        {
            if (_ringSprite != null) return _ringSprite;

            const int S = 128;
            float outer = 0.5f;
            float stroke = FeedbackTuning.RivalRingWidth / (FeedbackTuning.RivalRingRadius * 2f);
            float inner = Mathf.Max(0f, outer - stroke);

            // Feather over roughly one texel so the ring is not aliased at low resolutions.
            float feather = 1.5f / S;

            var px = new Color[S * S];
            for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                float dx = (x + 0.5f) / S - 0.5f;
                float dy = (y + 0.5f) / S - 0.5f;
                float d = Mathf.Sqrt(dx * dx + dy * dy);

                float a = Mathf.SmoothStep(0f, 1f, (d - (inner - feather)) / feather) *
                          (1f - Mathf.SmoothStep(0f, 1f, (d - (outer - feather)) / feather));
                px[y * S + x] = new Color(1f, 1f, 1f, Mathf.Clamp01(a));
            }

            _ringSprite = MakeSprite(px, S);
            return _ringSprite;
        }

        /// <summary>
        /// Solid triangular chevron pointing along +Y in sprite space, with the stroke
        /// thickness of <see cref="FeedbackTuning.RivalChevronWidthRatio"/>. Drawn as a
        /// filled arrowhead rather than an outline: at the size this appears on a phone an
        /// outline collapses into a smudge, and a solid silhouette is what reads
        /// peripherally, which is the entire job.
        /// </summary>
        private static Sprite ChevronSprite()
        {
            if (_chevronSprite != null) return _chevronSprite;

            const int S = 96;
            var px = new Color[S * S];
            float halfBase = 0.5f * FeedbackTuning.RivalChevronWidthRatio * 2.2f;

            for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                float u = (x + 0.5f) / S - 0.5f;   // -0.5 .. 0.5
                float v = (y + 0.5f) / S - 0.5f;   // -0.5 bottom .. 0.5 tip

                // Triangle: full width at the base, converging to a point at the tip.
                float t = Mathf.InverseLerp(-0.45f, 0.48f, v);
                float halfWidth = Mathf.Lerp(0.42f, 0.0f, t);
                bool inside = v >= -0.45f && v <= 0.48f && Mathf.Abs(u) <= halfWidth;

                // Notch the base into a chevron rather than a plain triangle.
                bool notch = v < -0.45f + halfBase && Mathf.Abs(u) < halfWidth * 0.55f;

                px[y * S + x] = new Color(1f, 1f, 1f, inside && !notch ? 1f : 0f);
            }

            _chevronSprite = MakeSprite(px, S);
            return _chevronSprite;
        }

        private static Sprite MakeSprite(Color[] px, int size)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };
            tex.SetPixels(px);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
        }
    }
}

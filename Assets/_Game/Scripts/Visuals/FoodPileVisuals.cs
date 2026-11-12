using CluckWars.Injection;
using System.Collections.Generic;
using CluckWars.Abilities;
using CluckWars.Gameplay;
using CluckWars.Settings;
using UnityEngine;
using Zenject;

namespace CluckWars.Visuals
{
    /// <summary>
    /// Local-only feedback for a <see cref="FoodPile"/>: flattens the mound as the pile is
    /// drained, tints it from the full to the empty colour (never darker than <see cref="TintFloor"/>),
    /// and swaps to the scattered-grain "empty" mesh at zero.
    /// Reads the networked <c>Amount</c> on every peer — no [Networked] state of its own.
    /// </summary>
    /// <remarks>
    /// <b>The mound's ground footprint IS the pile's collider (Phase 8 D9).</b> The X,Z size
    /// comes only from the pile root's scale (the current footprint step, the same scale that
    /// sizes the ellipse collider and the NavMesh carve), and the mesh is seated so its widest
    /// ring sits on the ground. Until D9 the mound also shrank in X,Z with the fill ratio and
    /// sat half-buried, so its visible oval was ~0.8x the collider when full and ~0.3x near
    /// empty: chickens stopped well short of the grain. Fill now reads through height.
    /// </remarks>
    [RequireComponent(typeof(FoodPile))]
    public sealed class FoodPileVisuals : MonoBehaviour
    {
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");

        [Tooltip("Mound transform (child of the pile root, unit-footprint mesh). Its Y scale follows the fill ratio; X,Z stay 1 so the footprint is the root's. Defaults to first child.")]
        [SerializeField] private Transform _meshRoot;

        [Tooltip("Renderer(s) tinted from full → empty. Auto-populated from _meshRoot children if empty.")]
        [SerializeField] private Renderer[] _renderers;

        [Tooltip("MeshFilter of the grain mound (child of _meshRoot carrying the FBX's 270 deg X node rotation). Swapped between _fullMesh and _emptyMesh.")]
        [SerializeField] private MeshFilter _moundFilter;

        [Tooltip("Mound mesh while the pile has food (Generated/Env/food_mound.fbx, unit footprint, ground ring at y 0).")]
        [SerializeField] private Mesh _fullMesh;

        [Tooltip("Scattered-grain mesh shown at Amount 0 (Generated/Env/food_mound_empty.fbx). Drawn at scale 1 in footprint space, lifted by _emptyLift.")]
        [SerializeField] private Mesh _emptyMesh;

        [Tooltip("Lift (pile-root local Y) of the empty-grain mesh so it clears the pile disc slab.")]
        [SerializeField] private float _emptyLift = 0.015f;

        [Tooltip("Mound height (local Y scale) at 100% full.")]
        [Min(0.01f)]
        [SerializeField] private float _fullHeight = 1f;

        [Tooltip("Mound height (local Y scale) at 0% (still visible so the pile doesn't disappear unexpectedly during draining).")]
        [Min(0.01f)]
        [SerializeField] private float _emptyHeight = 0.3f;

        [Tooltip("Height of the mound mesh's WIDEST ring below the mesh pivot, in mesh units. 0 for food_mound.fbx (its ground ring is its widest, at y 0). The mesh is lifted by this x its Y scale so that ring sits on the ground, which makes the visible oval equal to the collider ellipse (Phase 8 D9).")]
        [SerializeField] private float _widestRingDepth = 0f;

        [Tooltip("Legacy per-pile override. If null, ColorScheme.FoodPileFull is used.")]
        [SerializeField] private Color _fullColorOverride = new Color(0f, 0f, 0f, 0f);
        [Tooltip("Legacy per-pile override. If null/alpha=0, ColorScheme.FoodPileEmpty is used.")]
        [SerializeField] private Color _emptyColorOverride = new Color(0f, 0f, 0f, 0f);

        [Header("Centre-pile glow (Phase 8 C S1)")]
        [Tooltip("Flat alpha-blended halo under the pile (Art/Meshes/PileGlowRing, Art/Materials/PileGlow.mat). Disabled in the prefab; enabled only on the pile MapGenerator stamps FoodPile.IsCentre, and only while it has food.")]
        [SerializeField] private Renderer _centreGlow;

        [Tooltip("Glow colour (food gold ramp, never a player colour). RGB <= 1 and alpha-blended, so it cannot exceed the PC bloom threshold.")]
        [SerializeField] private Color _glowColor = new Color(0.961f, 0.784f, 0.259f, 1f);

        [Tooltip("Halo opacity at its brightest ring when the pile is full. Scaled down with fill (see GlowAlpha) and 0 when empty.")]
        [Range(0f, 1f)]
        [SerializeField] private float _glowPeakAlpha = 0.6f;

        [Header("Slow-band apron (Phase 8 D14)")]
        [Tooltip("Flat alpha-blended apron on the ground around the footprint, as wide as FoodPile.CollectReach: the band a chicken is pile-slowed in. Child of the pile root, uses Art/Materials/PileGlow.mat with a per-pile colour; its mesh is built at runtime in world metres and counter-scaled against the root's footprint scale. Shown on every pile that has food.")]
        [SerializeField] private Renderer _slowBand;

        [Tooltip("Apron colour: pale bleached straw, deliberately off the gold of the centre glow, the dark of mud and the tan of the paths. Tuned against the match camera on both tiers (about +7 % luma on the grass at the inner edge, a third of the foot ring's colour distance).")]
        [SerializeField] private Color _bandColor = new Color(0.86f, 0.78f, 0.52f, 1f);

        [Tooltip("Apron opacity at its most opaque (inner edge) when the pile is full. Scaled down with fill and 0 when empty. Quieter than the ring and the disc edge.")]
        [Range(0f, 1f)]
        [SerializeField] private float _bandPeakAlpha = 0.45f;

        [Tooltip("Apron height above the ground (world metres): over the dirt paths (0.012) but UNDER the pile disc slab (0.02), so the disc and its rim stay the crisp mark and the apron starts where the disc ends. The renderer also sorts first (sortingOrder -5) so the foot ring and the centre glow draw over it.")]
        [SerializeField] private float _bandLift = 0.015f;

        /// <summary>Fraction of the full apron opacity left on a nearly drained pile.</summary>
        public const float BandFloor = 0.4f;

        /// <summary>Apron opacity for a fill ratio: 0 when empty (no food, no slow), else <c>peak * Lerp(BandFloor, 1, ratio)</c>, so it rises with fill.</summary>
        public static float BandAlpha(float ratio, float peak) =>
            ratio <= 0f ? 0f : peak * Mathf.Lerp(BandFloor, 1f, Mathf.Clamp01(ratio));

        /// <summary>Fraction of the full glow left on a nearly drained centre pile (the halo dims with fill, then turns off at 0).</summary>
        public const float GlowFloor = 0.35f;

        /// <summary>Halo opacity for a fill ratio: 0 when empty ("no food here"), else <c>peak * Lerp(GlowFloor, 1, ratio)</c>.</summary>
        public static float GlowAlpha(float ratio, float peak) =>
            ratio <= 0f ? 0f : peak * Mathf.Lerp(GlowFloor, 1f, Mathf.Clamp01(ratio));

        /// <summary>Smallest fill-ratio change that re-writes the property blocks (0.5 % of a pile).</summary>
        public const float TintWriteThreshold = 0.005f;

        /// <summary>
        /// Tint floor (Phase 8 C S1, approved by Maestro): the pile colour never goes darker than
        /// <c>Lerp(Empty, Full, TintFloor)</c>. At the raw empty colour the low / empty states vanish into the pile disc.
        /// </summary>
        public const float TintFloor = 0.35f;

        /// <summary>Pile colour for a fill ratio: the empty-to-full lerp, clamped at <see cref="TintFloor"/>.</summary>
        public static Color TintFor(Color empty, Color full, float ratio) =>
            Color.Lerp(empty, full, Mathf.Max(Mathf.Clamp01(ratio), TintFloor));

        private FoodPile _pile;
        private float _lastTintRatio = float.NaN;
        private bool _showingEmpty;
        private bool _glowAppliedForCentre;
        private Mesh _bandMesh;
        private Vector2 _bandFootprint;
        private Vector3 _bandRootScale;
        private float _bandReach;
        private MaterialPropertyBlock _propertyBlock;
        private ColorSchemeSO _colors;

        // ---- Peck chip (D11): local, observed from the replicated Amount; nothing here is networked ----
        private float _lastAmount = float.NaN;
        private float _squashStart = -1f;            // Time.unscaledTime of the bite; < 0 = none
        private MeshRenderer _moundRenderer;
        private ParticleSystem _chipPS;
        private ParticleSystem.EmitParams _chipEmit;  // cached: every emit rewrites it, nothing allocates
        private static readonly List<float> s_candDistance = new List<float>(8);
        private static readonly List<bool> s_candOpen = new List<bool>(8);
        private static readonly List<ChickenController> s_candidates = new List<ChickenController>(8);

        /// <summary>Name of the pile-owned kernel system.</summary>
        public const string KernelSystemName = "VFX_PileKernel";

        /// <summary>The pile kernel system (null until the first bite is seen).</summary>
        public ParticleSystem KernelSystem => _chipPS;

        [Inject]
        public void Construct(ColorSchemeSO colors) => _colors = colors;

        private void Awake()
        {
            _pile = GetComponent<FoodPile>();
            if (_meshRoot == null && transform.childCount > 0) _meshRoot = transform.GetChild(0);
            if ((_renderers == null || _renderers.Length == 0) && _meshRoot != null)
                _renderers = _meshRoot.GetComponentsInChildren<Renderer>(includeInactive: true);
            _propertyBlock = new MaterialPropertyBlock();

            // FoodPileVisuals lives on a procedurally-spawned NetworkObject (MapGenerator
            // spawns the pile at runtime), so Zenject's scene-time injection doesn't fire.
            // Self-inject the color scheme — same pattern as the NetworkBehaviours.
            SelfInject.Ensure(this, _colors != null);
        }

        private Color FullColor => _fullColorOverride.a > 0f
            ? _fullColorOverride
            : (_colors != null ? _colors.FoodPileFull : new Color(1f, 0.85f, 0.3f));

        private Color EmptyColor => _emptyColorOverride.a > 0f
            ? _emptyColorOverride
            : (_colors != null ? _colors.FoodPileEmpty : new Color(0.71f, 0.54f, 0.25f));

        /// <summary>Mound Y scale for a fill ratio: linear from <see cref="_emptyHeight"/> to <see cref="_fullHeight"/>.</summary>
        public float MoundHeight(float ratio) => Mathf.Lerp(_emptyHeight, _fullHeight, Mathf.Clamp01(ratio));

        /// <summary>
        /// Pose of the mound root (pile-root local). Food present: Y scale by fill height, widest ring seated on the ground.
        /// Empty: the scattered-grain mesh is authored in footprint space, so scale 1, lifted just above the disc.
        /// </summary>
        /// <param name="squash">Peck-bite squash factor (D11, <see cref="PeckBeatRules.SquashFactor"/>): multiplies the fill height, so the
        /// squash and the fill scale go out as ONE write and the widest ring stays seated. 1 = none (and ignored for the empty mesh).</param>
        public void MoundPose(bool showEmptyMesh, float ratio, out Vector3 scale, out Vector3 localPosition, float squash = 1f)
        {
            if (showEmptyMesh)
            {
                scale = Vector3.one;
                localPosition = new Vector3(0f, _emptyLift, 0f);
                return;
            }
            float height = MoundHeight(ratio) * squash;
            scale = new Vector3(1f, height, 1f);
            localPosition = new Vector3(0f, _widestRingDepth * height, 0f);
        }

        /// <summary>
        /// True when the tint must be re-written: first frame (NaN), a change beyond
        /// <see cref="TintWriteThreshold"/>, or the pile landing exactly on empty / full so the
        /// end colours are never left up to 0.5 % off.
        /// </summary>
        public static bool ShouldWriteTint(float lastWritten, float ratio)
        {
            if (float.IsNaN(lastWritten)) return true;
            float delta = Mathf.Abs(ratio - lastWritten);
            if (delta > TintWriteThreshold) return true;
            return delta > 0f && (ratio <= 0f || ratio >= 1f);
        }

        private void LateUpdate()
        {
            if (_pile == null) return;

            // Object.IsValid means the NetworkObject is spawned and has replicated state.
            if (_pile.Object == null || !_pile.Object.IsValid) return;

            float ratio = _pile.MaxAmount > 0f ? Mathf.Clamp01(_pile.Amount / _pile.MaxAmount) : 0f;

            bool empty = _pile.Amount <= 0f;
            ObservePeck(_pile.Amount);
            if (_moundFilter != null && _fullMesh != null && _emptyMesh != null && (empty != _showingEmpty || _moundFilter.sharedMesh == null))
            {
                _showingEmpty = empty;
                _moundFilter.sharedMesh = empty ? _emptyMesh : _fullMesh;
            }

            if (_meshRoot != null)
            {
                float squash = _squashStart >= 0f ? PeckBeatRules.SquashFactor(Time.unscaledTime - _squashStart, PlayerPreferences.ReducedMotionEnabled) : 1f;
                if (squash >= 1f) _squashStart = -1f;
                MoundPose(empty && _emptyMesh != null, ratio, out var scale, out var pos, squash);
                _meshRoot.localScale = scale;
                _meshRoot.localPosition = pos;
            }

            // Same throttle as the tint (C4: write-on-change only). The glow is re-applied when the tint is due or
            // when the pile's centre flag differs from what was last applied (Fusion pools NetworkObjects).
            bool tintDue = ShouldWriteTint(_lastTintRatio, ratio);
            bool centre = _pile.IsCentre;
            if (_centreGlow != null && (tintDue || centre != _glowAppliedForCentre))
            {
                _glowAppliedForCentre = centre;
                ApplyGlow(centre ? GlowAlpha(ratio, _glowPeakAlpha) : 0f);
            }

            if (_slowBand != null) UpdateSlowBand(ratio, tintDue);

            if (tintDue) _lastTintRatio = ratio;
            if (_renderers != null && _renderers.Length > 0 && tintDue)
            {
                var color = TintFor(EmptyColor, FullColor, ratio);
                for (int i = 0; i < _renderers.Length; i++)
                {
                    var r = _renderers[i];
                    if (r == null) continue;
                    r.GetPropertyBlock(_propertyBlock);
                    _propertyBlock.SetColor(BaseColorId, color);
                    _propertyBlock.SetColor(ColorId, color);
                    r.SetPropertyBlock(_propertyBlock);
                }
            }
        }

        /// <summary>
        /// Keeps the slow-band apron on the pile's CURRENT footprint (rebuilt only when the footprint step or the root scale
        /// changes) and writes its opacity with the tint throttle. Local and visual only: reads the replicated Amount.
        /// </summary>
        private void UpdateSlowBand(float ratio, bool alphaDue)
        {
            var footprint = _pile.CurrentFootprint;
            var rootScale = transform.lossyScale;
            float reach = _pile.CollectReach;
            if (_bandMesh == null)
            {
                _bandMesh = new Mesh { name = "PileSlowBand" };
                _bandMesh.MarkDynamic();
                var filter = _slowBand.GetComponent<MeshFilter>();
                if (filter != null) filter.sharedMesh = _bandMesh;
                _bandFootprint = Vector2.zero;
            }
            if (footprint != _bandFootprint || rootScale != _bandRootScale || !Mathf.Approximately(reach, _bandReach))
            {
                _bandFootprint = footprint;
                _bandRootScale = rootScale;
                _bandReach = reach;
                PileBandMesh.Fill(_bandMesh, footprint, reach);
                PoseBand(_slowBand.transform, rootScale, _bandLift);
            }
            if (!alphaDue && _slowBand.enabled == (_pile.Amount > 0f)) return;
            float alpha = BandAlpha(ratio, _bandPeakAlpha);
            bool on = alpha > 0f;
            if (_slowBand.enabled != on) _slowBand.enabled = on;
            if (!on) return;
            _slowBand.GetPropertyBlock(_propertyBlock);
            _propertyBlock.SetColor(BaseColorId, new Color(_bandColor.r, _bandColor.g, _bandColor.b, alpha));
            _slowBand.SetPropertyBlock(_propertyBlock);
        }

        /// <summary>
        /// Seats the apron under the pile root: the mesh is in world metres, so undo the root's (footprint-sized) scale to land it 1:1,
        /// and lift it <paramref name="lift"/> world metres above the ground. Rotation stays identity: the mesh already uses the
        /// pile's local axes, so the root's yaw carries it.
        /// </summary>
        public static void PoseBand(Transform band, Vector3 rootScale, float lift)
        {
            band.localScale = new Vector3(1f / Mathf.Max(rootScale.x, 1e-4f), 1f / Mathf.Max(rootScale.y, 1e-4f), 1f / Mathf.Max(rootScale.z, 1e-4f));
            band.localPosition = new Vector3(0f, lift / Mathf.Max(rootScale.y, 1e-4f), 0f);
            band.localRotation = Quaternion.identity;
        }

        private void OnDisable()
        {
            _lastAmount = float.NaN;
            _squashStart = -1f;
        }

        private void OnDestroy()
        {
            if (_bandMesh != null) Destroy(_bandMesh);
        }

        // ---- Peck chip (D11, vfx.md 2.7) ----------------------------------------------------------

        /// <summary>
        /// Edge poller on the replicated <c>Amount</c> (first observation seeds the baseline, so a late join never replays a bite). A drop
        /// is a Peck only if some bird in reach has its Peck slot active: the beat belongs to that bird (nearest when several), and with
        /// no pecker nothing plays (a steal drain, or an empty-pile press that shows only the refusal).
        /// </summary>
        private void ObservePeck(float amount)
        {
            bool dropped = PeckBeatRules.AmountDropped(_lastAmount, amount);
            _lastAmount = amount;
            if (!dropped) return;
            var pecker = FindPecker();
            if (pecker != null) PlayPeckBeat(pecker);
        }

        /// <summary>The bird whose Peck window is open at this pile (nearest when several), or null.</summary>
        private ChickenController FindPecker()
        {
            s_candidates.Clear();
            s_candDistance.Clear();
            s_candOpen.Clear();
            var list = ChickenController.ActiveControllers;
            for (int i = 0; i < list.Count; i++)
            {
                var c = list[i];
                if (c == null || c.IsDecoy || c.Object == null || !c.Object.IsValid || c.Abilities == null) continue;
                Vector3 pos = c.transform.position;
                if (!_pile.IsWithinCollectRange(pos)) continue;
                s_candidates.Add(c);
                s_candDistance.Add(_pile.DistanceToSurface(pos));
                s_candOpen.Add(PeckWindowOpen(c.Abilities));
            }
            int pick = PeckBeatRules.PickPecker(s_candDistance, s_candOpen);
            return pick >= 0 ? s_candidates[pick] : null;
        }

        /// <summary>True while the bird Peck slot (the slot holding a <see cref="PeckAbilitySO"/>) is running: the 0.4 s commitment window.</summary>
        public static bool PeckWindowOpen(AbilityController abilities)
        {
            for (int i = 0; i < AbilityController.SlotCount; i++)
                if (abilities.IsSlotActive(i) && abilities.GetSlot(i) is PeckAbilitySO) return true;
            return false;
        }

        /// <summary>
        /// The bite beat: squash the mound, puff straw-cream dust at the contact point and fly kernels from the mound top to the pecker
        /// beak. Nothing for a hidden or faded pecker (the gate is the one every bird-attached emitter asks).
        /// </summary>
        private void PlayPeckBeat(ChickenController pecker)
        {
            if (FoodChunkFlight.IsQuiet(pecker)) return;
            bool reduced = PlayerPreferences.ReducedMotionEnabled;
            if (!reduced && !_showingEmpty) _squashStart = Time.unscaledTime;

            var vfx = pecker.GetComponent<ChickenVFX>();
            Vector3 contact = _pile.SurfaceApproachPoint(pecker.transform.position, 0f);
            contact.y = transform.position.y + FeedbackTuning.PeckPuffHeight;
            if (vfx != null) vfx.EmitPeckPuff(contact);

            if (!EnsureKernelSystem(vfx)) return;
            Vector3 beak = PeckBeatRules.BeakPoint(pecker.transform.position, pecker.transform.forward, ChickenGround.GroundWorldY(pecker));
            Vector3 top = MoundTop();
            Vector3 start = PeckBeatRules.KernelStart(top, beak, reduced);
            Vector3 velocity = PeckBeatRules.KernelVelocity(top, beak, reduced);
            _chipEmit.applyShapeToPosition = false;
            _chipEmit.startLifetime = FeedbackTuning.PeckKernelSeconds;
            _chipEmit.startSize = FeedbackTuning.PeckKernelSize;
            _chipEmit.startColor = FeedbackPalette.Current.PeckAccent;
            int n = PeckBeatRules.KernelCount(reduced);
            for (int i = 0; i < n; i++)
            {
                Vector2 scatter = Random.insideUnitCircle * FeedbackTuning.PeckKernelScatter;
                _chipEmit.position = start + new Vector3(scatter.x, 0f, scatter.y);
                _chipEmit.velocity = velocity;
                _chipEmit.rotation = Random.Range(0f, 360f);
                _chipPS.Emit(_chipEmit, 1);
            }
        }

        /// <summary>World point on top of the mound (renderer bounds: follows the fill height and any squash), else the pile root.</summary>
        private Vector3 MoundTop()
        {
            if (_moundRenderer == null && _moundFilter != null) _moundRenderer = _moundFilter.GetComponent<MeshRenderer>();
            if (_moundRenderer != null)
            {
                var b = _moundRenderer.bounds;
                return new Vector3(b.center.x, b.max.y, b.center.z);
            }
            return transform.position + Vector3.up * MoundHeight(_pile.MaxAmount > 0f ? _pile.Amount / _pile.MaxAmount : 0f);
        }

        /// <summary>
        /// Builds the pile kernel system on first use: a child, world simulation space, the shared Fx material (taken from the pecker
        /// <see cref="ChickenVFX"/>, never an instance), sprite mode over Fx_Corn, maxParticles <see cref="FeedbackTuning.PeckKernelMaxPerPile"/>,
        /// no shape, no prewarm. False when the sprite or material is not available yet (nothing plays, nothing throws).
        /// </summary>
        private bool EnsureKernelSystem(ChickenVFX vfx)
        {
            if (_chipPS != null) return true;
            var corn = FoodChunkFlight.CornSprite;
            var mat = vfx != null ? vfx.ParticleMaterial : null;
            if (corn == null || mat == null) return false;

            var go = new GameObject(KernelSystemName);
            go.transform.SetParent(transform, worldPositionStays: false);
            _chipPS = go.AddComponent<ParticleSystem>();
            _chipPS.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = _chipPS.main;
            main.loop = false;
            main.playOnAwake = false;
            main.prewarm = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.scalingMode = ParticleSystemScalingMode.Local;   // the pile root footprint scale never reaches the kernels
            main.gravityModifier = FeedbackTuning.PeckKernelGravity;
            main.maxParticles = FeedbackTuning.PeckKernelMaxPerPile;
            main.startSpeed = 0f;
            var em = _chipPS.emission;
            em.enabled = true;
            em.rateOverTime = 0f;
            var sh = _chipPS.shape;
            sh.enabled = false;
            var rend = go.GetComponent<ParticleSystemRenderer>();
            rend.renderMode = ParticleSystemRenderMode.Billboard;
            rend.sortingOrder = FeedbackTuning.PeckKernelSortingOrder;
            rend.sharedMaterial = mat;
            ChickenVFX.ApplySprites(_chipPS, corn);
            ChickenVFX.AddFadeOut(_chipPS);
            _chipPS.Play();   // stays playing: Emit() adds particles, there is no timeline
            return true;
        }

        private void ApplyGlow(float alpha)
        {
            bool on = alpha > 0f;
            if (_centreGlow.enabled != on) _centreGlow.enabled = on;
            if (!on) return;
            _centreGlow.GetPropertyBlock(_propertyBlock);
            _propertyBlock.SetColor(BaseColorId, new Color(_glowColor.r, _glowColor.g, _glowColor.b, alpha));
            _centreGlow.SetPropertyBlock(_propertyBlock);
        }
    }
}

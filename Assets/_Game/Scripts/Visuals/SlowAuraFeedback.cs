using CluckWars.Abilities;
using CluckWars.Gameplay;
using CluckWars.Injection;
using CluckWars.Logging;
using CluckWars.Settings;
using UnityEngine;
using Zenject;

namespace CluckWars.Visuals
{
    /// <summary>
    /// The Slow aura as a live area (D8, vfx.md 2.4), drawn from the CASTER's replicated state: the caster's Slow slot
    /// (<see cref="FeatherAuraAbilitySO"/>) is active. Four layers, all local on every peer, no RPC, nothing networked:
    /// <list type="number">
    /// <item>the <b>outline</b>: the dashed ribbon (<c>Fx_DashedRing</c>) in the Slow accent, turning at 0.1 lap/s so a live aura
    /// differs from a static range guide by motion (static under Reduced Motion). It replaces the zone's cyan edge ring for an
    /// aura (<see cref="AbilityZoneVisuals"/> skips caster-anchored zones), so there is exactly one ring at the aura edge;</item>
    /// <item>the <b>edge band</b>: one <c>Fx_AuraEdge</c> quad, outer radius = aura radius, no fill;</item>
    /// <item>the <b>feather-fall</b>: the dedicated <c>VFX_AuraFeather</c> system (world space, spawned over the disc, drifting
    /// down), the one continuous mid-count emitter, kept out of the five <see cref="ChickenVFX"/> families;</item>
    /// <item>the <b>foot puffs</b>: 4 feathers at the caster's feet every 0.4 s (the Feather family).</item>
    /// </list>
    /// The radius is read from the asset (<see cref="SlowAuraRules.Radius"/>), never a literal. Victims are never marked here
    /// (they wear the existing cyan snare ring); nothing is emitted from them. A hidden or faded caster draws nothing at all:
    /// an aura around an invisible bird would reveal it.
    ///
    /// The outline turns by rotating its transform (the ring is rotationally symmetric, so turning it is the same picture as
    /// scrolling the texture) and so needs no per-instance material and no texture-offset write: the one dashed material is
    /// shared by every aura on the peer.
    /// </summary>
    [DefaultExecutionOrder(100)]
    public sealed class SlowAuraFeedback : MonoBehaviour
    {
        /// <summary>Name of the dedicated feather-fall ParticleSystem child.</summary>
        public const string SystemName = "VFX_AuraFeather";

        [Tooltip("Art/UI/Fx/Fx_AuraEdge.png: the white ring gradient of the edge band.")]
        [SerializeField] private Sprite _auraEdgeSprite;
        [Tooltip("Art/UI/Fx/Fx_DashedRing.png: the tiled dashed strip of the outline.")]
        [SerializeField] private Texture2D _dashedRingTexture;

        private static Material s_dashedMaterial;

        /// <summary>The one dashed-ribbon material (Fx_DashedRing) shared by every ring on the peer; built when the first chicken wakes.
        /// <see cref="HideLocalCue"/> borrows it (colour is per-vertex, so one material serves every tint).</summary>
        internal static Material SharedDashedMaterial => s_dashedMaterial;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() { s_dashedMaterial = null; }

        private ChickenController _controller;
        private ChickenCombat _combat;
        private AbilityController _abilities;
        private ChickenVFX _vfx;
        private ILogService _log;

        private ParticleSystem _ps;
        private ParticleSystemRenderer _psRenderer;
        private Transform _groundRoot, _outlineT;
        private LineRenderer _outline;
        private SpriteRenderer _band;
        private Vector3[] _ringBuf;

        private bool _shown, _wiringChecked;
        private float _builtRadius = -1f;
        private bool _builtReduced;
        private Vector3 _appliedParentScale = Vector3.zero;
        private float _puffTimer;
        private ChickenVFX.FxEmit _puff;
        private bool _puffBuilt;

        [Inject]
        public void Construct(ILogService log) { _log = log; }

        /// <summary>The dedicated feather-fall system (tests and the probe read it).</summary>
        public ParticleSystem FeatherSystem => _ps;

        /// <summary>True while the aura layers are drawn on this peer.</summary>
        public bool IsShown => _shown;

        private void Awake()
        {
            _controller = GetComponent<ChickenController>();
            _combat = GetComponent<ChickenCombat>();
            _abilities = GetComponent<AbilityController>();
            _vfx = GetComponent<ChickenVFX>();
            BuildFeatherSystem();
            BuildGround();
            SetLayersEnabled(false);
        }

        private void OnDisable()
        {
            _shown = false;
            SetLayersEnabled(false);
        }

        // ---- Build ------------------------------------------------------------------------------

        private void BuildFeatherSystem()
        {
            var go = new GameObject(SystemName);
            go.transform.SetParent(transform, worldPositionStays: false);
            _ps = go.AddComponent<ParticleSystem>();
            _ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = _ps.main;
            main.loop = true;
            main.playOnAwake = false;
            main.prewarm = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            // The caster root carries the class scale; the disc is metres, so only this transform's own (unit) scale counts.
            main.scalingMode = ParticleSystemScalingMode.Local;
            main.maxParticles = FeedbackTuning.SlowAuraFeatherMaxParticles;
            main.startLifetime = FeedbackTuning.SlowAuraFeatherLifeSeconds;
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(FeedbackTuning.SlowAuraFeatherSizeMin, FeedbackTuning.SlowAuraFeatherSizeMax);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startColor = FeedbackTuning.SlowAuraFeatherColor;
            main.gravityModifier = FeedbackTuning.SlowAuraFeatherGravity;

            var em = _ps.emission;
            em.enabled = false;
            em.rateOverTime = FeedbackTuning.SlowAuraFeatherRate;

            // Circle laid flat (the shape's circle is in XY; 90 degrees about X makes it horizontal), filled (emit from the
            // volume), lifted to the spawn height. The radius follows the asset at each activation.
            var sh = _ps.shape;
            sh.enabled = true;
            sh.shapeType = ParticleSystemShapeType.Circle;
            sh.radius = 1f;
            sh.radiusThickness = 1f;
            sh.arc = 360f;
            sh.position = new Vector3(0f, FeedbackTuning.SlowAuraFeatherSpawnHeight, 0f);
            sh.rotation = new Vector3(90f, 0f, 0f);
            sh.alignToDirection = false;
            sh.randomDirectionAmount = 0f;

            var noise = _ps.noise;
            noise.enabled = true;
            noise.strength = FeedbackTuning.SlowAuraFeatherNoiseStrength;
            noise.frequency = FeedbackTuning.SlowAuraFeatherNoiseFrequency;
            noise.quality = ParticleSystemNoiseQuality.Low;
            noise.scrollSpeed = 0f;
            noise.damping = true;

            _psRenderer = go.GetComponent<ParticleSystemRenderer>();
            _psRenderer.renderMode = ParticleSystemRenderMode.Billboard;
            _psRenderer.sortingOrder = ChickenVFX.BodyFxSortingOrder;
            if (_vfx != null)
            {
                if (_vfx.ParticleMaterial != null) _psRenderer.sharedMaterial = _vfx.ParticleMaterial;   // the shared asset, never an instance
                ChickenVFX.ApplySprites(_ps, _vfx.FeatherSprites);
            }
            ChickenVFX.AddFadeOut(_ps);   // alpha holds, then fades over the last quarter of life
            _ps.Play();                   // stays playing: the emission module is the switch
        }

        private void BuildGround()
        {
            if (s_dashedMaterial == null && _dashedRingTexture != null)
            {
                var m = TelegraphShapes.BuildLineMaterial();
                if (m != null)
                {
                    m.name = "SlowAuraDashed (shared)";
                    m.hideFlags = HideFlags.HideAndDontSave;
                    m.mainTexture = _dashedRingTexture;
                    // LineTextureMode.Tile: scale.x repeats per world unit is width * N (see AbilityTelegraph).
                    m.mainTextureScale = new Vector2(FeedbackTuning.NoTargetPreviewLineWidth * FeedbackTuning.NoTargetPreviewRepeatsPerUnit, 1f);
                    s_dashedMaterial = m;
                }
            }

            var root = new GameObject("SlowAuraGround");
            root.transform.SetParent(transform, worldPositionStays: false);
            _groundRoot = root.transform;

            var ring = new GameObject("SlowAuraOutline");
            ring.transform.SetParent(_groundRoot, worldPositionStays: false);
            ring.transform.localPosition = new Vector3(0f, FeedbackTuning.SlowAuraOutlineY, 0f);
            _outlineT = ring.transform;
            _outline = ring.AddComponent<LineRenderer>();
            _outline.useWorldSpace = false;   // the ring turns with its transform; the points are built once per radius
            _outline.loop = true;
            _outline.positionCount = FeedbackTuning.SlowAuraOutlineSegments;
            _outline.widthMultiplier = FeedbackTuning.NoTargetPreviewLineWidth;
            _outline.numCapVertices = 0;
            _outline.numCornerVertices = 0;
            _outline.alignment = LineAlignment.View;
            _outline.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _outline.receiveShadows = false;
            _outline.textureMode = LineTextureMode.Tile;
            _outline.sortingOrder = FeedbackTuning.SlowAuraOutlineSortingOrder;
            if (s_dashedMaterial != null) _outline.sharedMaterial = s_dashedMaterial;
            _ringBuf = new Vector3[FeedbackTuning.SlowAuraOutlineSegments];

            var bandGo = new GameObject("SlowAuraBand");
            bandGo.transform.SetParent(_groundRoot, worldPositionStays: false);
            bandGo.transform.localPosition = new Vector3(0f, FeedbackTuning.SlowAuraBandY, 0f);
            bandGo.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);   // a flat quad on the ground
            _band = bandGo.AddComponent<SpriteRenderer>();
            _band.sprite = _auraEdgeSprite;
            _band.sortingOrder = FeedbackTuning.SlowAuraBandSortingOrder;
            _band.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _band.receiveShadows = false;
        }

        private void SetLayersEnabled(bool on)
        {
            if (_outline != null) _outline.enabled = on;
            if (_band != null) _band.enabled = on;
        }

        // ---- Per-frame observation -------------------------------------------------------------

        private void LateUpdate()
        {
            if (_abilities == null || _controller == null || _ps == null) return;
            var abObj = _abilities.Object;
            if (abObj == null || !abObj.IsValid) return;

            if (!_wiringChecked)
            {
                _wiringChecked = true;
                SelfInject.Ensure(this, _log != null);
                if (_auraEdgeSprite == null || _dashedRingTexture == null || s_dashedMaterial == null || (_vfx != null && _vfx.ParticleMaterial == null))
                    _log?.Error("SlowAuraFeedback", "Slow aura art is not wired on Chicken.prefab (Fx_AuraEdge sprite, Fx_DashedRing texture, shared particle material): the aura will draw incomplete.");
            }

            FeatherAuraAbilitySO aura = null;
            for (int i = 0; i < AbilityController.SlotCount; i++)
            {
                if (_abilities.GetSlot(i) is FeatherAuraAbilitySO s && _abilities.IsSlotActive(i)) { aura = s; break; }
            }

            bool removed = _combat != null && _combat.IsRemoved;
            bool hidden = HiddenVisibilityRules.IsHiddenFor(_controller);
            bool faded = ControlStateVFX.IsFadedOpacity(_controller.VisualOpacity);
            bool show = aura != null && SlowAuraRules.ShouldShow(true, removed, hidden, faded);

            bool reduced = PlayerPreferences.ReducedMotionEnabled;

            if (!show)
            {
                if (_shown)
                {
                    _shown = false;
                    SetLayersEnabled(false);
                    var em = _ps.emission;
                    em.enabled = false;   // the feathers in the air finish their fall and fade out
                    // An aura around a bird that vanished must not leave its feathers hanging where the bird is.
                    if (hidden || faded || removed) _ps.Clear(true);
                }
                _puffTimer = 0f;
                return;
            }

            float radius = SlowAuraRules.Radius(aura);
            if (!_shown || !Mathf.Approximately(radius, _builtRadius) || reduced != _builtReduced)
                Configure(radius, reduced);
            _shown = true;

            Vector3 p = transform.position;
            _groundRoot.position = new Vector3(p.x, 0f, p.z);
            _groundRoot.rotation = Quaternion.identity;
            NeutralizeParentScale();

            _outlineT.localRotation = Quaternion.Euler(0f, SlowAuraRules.OutlineAngle(Time.unscaledTime, reduced), 0f);

            // Foot puffs: 4 feathers every 0.4 s (2 under Reduced Motion, fading in place).
            _puffTimer -= Time.unscaledDeltaTime;
            if (_puffTimer <= 0f)
            {
                _puffTimer += FeedbackTuning.SlowAuraPuffIntervalSeconds;
                EmitPuff(reduced);
            }
        }

        /// <summary>Applies the aura's size and the Reduced Motion variant: ring geometry, band scale and colour, emitter radius, rate, noise.</summary>
        private void Configure(float radius, bool reduced)
        {
            _builtRadius = radius;
            _builtReduced = reduced;
            Color accent = FeedbackPalette.Current.SlowAccent;

            for (int i = 0; i < _ringBuf.Length; i++)
            {
                float a = i / (float)_ringBuf.Length * Mathf.PI * 2f;
                _ringBuf[i] = new Vector3(Mathf.Cos(a) * radius, 0f, Mathf.Sin(a) * radius);
            }
            _outline.SetPositions(_ringBuf);
            Color line = accent;
            line.a = FeedbackTuning.SlowAuraOutlineAlpha;
            _outline.startColor = _outline.endColor = line;

            if (_band != null && _band.sprite != null)
            {
                float diameter = Mathf.Max(0.01f, _band.sprite.bounds.size.x);
                float s = 2f * radius / diameter;   // the sprite's own outer edge is its full width: outer radius = aura radius
                _band.transform.localScale = new Vector3(s, s, 1f);
                Color c = accent;
                c.a = SlowAuraRules.BandAlpha(reduced);
                _band.color = c;
            }

            var sh = _ps.shape;
            sh.radius = SlowAuraRules.FeatherDiscRadius(radius);
            var noise = _ps.noise;
            noise.enabled = !reduced;
            noise.strength = SlowAuraRules.FeatherNoiseStrength(reduced);
            var em = _ps.emission;
            em.rateOverTime = SlowAuraRules.FeatherRate(reduced);
            em.enabled = true;

            SetLayersEnabled(true);
        }

        private void NeutralizeParentScale()
        {
            Vector3 ps = transform.lossyScale;
            if (ps == _appliedParentScale) return;
            _appliedParentScale = ps;
            _groundRoot.localScale = new Vector3(
                Mathf.Approximately(ps.x, 0f) ? 1f : 1f / ps.x,
                Mathf.Approximately(ps.y, 0f) ? 1f : 1f / ps.y,
                Mathf.Approximately(ps.z, 0f) ? 1f : 1f / ps.z);
        }

        private void EmitPuff(bool reduced)
        {
            if (_vfx == null) return;
            if (!_puffBuilt)
            {
                _puffBuilt = true;
                _puff = new ChickenVFX.FxEmit
                {
                    SizeMin = FeedbackTuning.SlowAuraPuffSizeMin, SizeMax = FeedbackTuning.SlowAuraPuffSizeMax,
                    SpeedMin = 0.5f, SpeedMax = 1.0f,
                    LifeMin = FeedbackTuning.SlowAuraPuffLifeSeconds, LifeMax = FeedbackTuning.SlowAuraPuffLifeSeconds,
                    Shape = ChickenVFX.FxShape.Cone, Radius = 0.3f, ConeAngle = 80f, ConeMinAngle = 45f, Axis = Vector3.up,
                    Gravity = 0f, RandomRotation = true,
                };
            }
            var e = _puff;
            e.Count = SlowAuraRules.PuffCount(reduced);
            e.Color = FeedbackTuning.SlowAuraFeatherColor;
            if (reduced) e.SpeedMin = e.SpeedMax = 0f;
            _vfx.EmitFeathers(in e, FeedbackTuning.SlowAuraPuffHeight);
        }
    }
}

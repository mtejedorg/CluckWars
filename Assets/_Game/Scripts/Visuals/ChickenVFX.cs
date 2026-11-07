using CluckWars.Abilities;
using CluckWars.Gameplay;
using CluckWars.Settings;
using UnityEngine;

namespace CluckWars.Visuals
{
    /// <summary>
    /// Local-only VFX for a chicken: the hit sparks, death burst, ability accent burst, cargo-full ring and food-deposit
    /// celebration. All effects are triggered by polling <c>[Networked]</c> state (HP, removal, Cargo) in <c>LateUpdate</c>:
    /// no RPCs, fires on every peer automatically.
    /// </summary>
    /// <remarks>
    /// <b>One ParticleSystem per sprite family</b> (vfx.md 0.2), all on the shared <c>Fx_Particles.mat</c> in Texture Sheet
    /// Animation sprite mode over the <c>Fx_Atlas</c> sprites: <b>Dust</b> (the cream puff, ground band, sorting 2),
    /// <b>Feather</b> (feather sprites, body band, sorting 5) and <b>Sparkle</b> (the four-point star, body band, sorting 5).
    /// Streak, Kernel and Shard systems are added by the later batch-D items; the budget leaves room for them
    /// (<see cref="FeedbackTuning.ParticleSystemsPerChickenCap"/>, <see cref="FeedbackTuning.ParticlesPerChickenCap"/>).
    /// Every effect is an <see cref="FxEmit"/> description handed to <see cref="EmitDust"/>, <see cref="EmitFeathers"/> or
    /// <see cref="EmitSparkles"/>: later items and <see cref="HitFeedback"/> call these instead of building systems.
    /// Emission goes through one cached <see cref="ParticleSystem.EmitParams"/>, so nothing allocates per emit.
    /// The material and the sprites are serialized here (Chicken.prefab): no <c>Resources.Load</c>, no per-chicken material.
    /// Callers gate on <see cref="HiddenVisibilityRules"/> before emitting from a chicken (a hidden bird emits nothing).
    /// </remarks>
    [RequireComponent(typeof(ChickenController))]
    public sealed class ChickenVFX : MonoBehaviour
    {
        // ---- Effect description ------------------------------------------------

        /// <summary>Spawn/emission shape of an <see cref="FxEmit"/>.</summary>
        public enum FxShape
        {
            /// <summary>Random point in a sphere, flying radially outward.</summary>
            Sphere,
            /// <summary>Small disc, flying inside a cone of <see cref="FxEmit.ConeAngle"/> around <see cref="FxEmit.Axis"/>.</summary>
            Cone,
            /// <summary>Random point in a horizontal disc, drifting radially outward in the plane.</summary>
            Disc,
        }

        /// <summary>
        /// One burst, described as data. Gravity is baked into the launch velocity (the families run with zero gravity so
        /// effects with different pulls can share one system): a constant vertical speed that lands the particle where
        /// the real parabola would at the end of its life.
        /// </summary>
        public struct FxEmit
        {
            public int Count;
            public Color Color;
            public float SizeMin, SizeMax;
            public float SpeedMin, SpeedMax;
            public float LifeMin, LifeMax;
            public FxShape Shape;
            public float Radius;
            public float ConeAngle;
            /// <summary>Cone only: the minimum tilt from <see cref="Axis"/> (0 = anywhere inside the cone), for a ring-shaped fan.</summary>
            public float ConeMinAngle;
            public Vector3 Axis;
            public float Gravity;
            public bool RandomRotation;
        }

        private const float EarthG = 9.81f;

        // ---- Tunables (Inspector-tweakable) -----------------------------------

        [Header("Hit sparks")]
        [Tooltip("Particles ejected when this chicken takes a hit.")]
        [Min(1)] [SerializeField] private int   _hitCount  = 8;
        [SerializeField] private Color _hitColor = new Color(1.00f, 0.78f, 0.20f, 1f);

        [Header("Death burst")]
        [Tooltip("Class-tint feathers (Feather family) ejected when this chicken is removed.")]
        [Min(1)] [SerializeField] private int   _deathFeatherCount = 10;
        [Tooltip("Small neutral cream puff (Dust family) under the feathers.")]
        [Min(0)] [SerializeField] private int   _deathDustCount = 6;
        [SerializeField] private Color _deathDustColor = new Color(0.98f, 0.94f, 0.84f, 1f);

        [Header("Deposit burst")]
        [Tooltip("Upward burst of gold when cargo is deposited at the base.")]
        [Min(1)] [SerializeField] private int   _depositCount = 12;
        [SerializeField] private Color _depositColor = new Color(0.96f, 0.78f, 0.26f, 1f);

        [Header("Shared particle assets (Art/Materials/Fx_Particles.mat, Art/UI/Fx/Fx_Atlas.spriteatlas)")]
        [Tooltip("The one shared particle material: URP Particles/Unlit, alpha blend, no soft particles. Never instanced.")]
        [SerializeField] private Material _particleMaterial;
        [Tooltip("Fx_Dust: the cream puff.")]
        [SerializeField] private Sprite _dustSprite;
        [Tooltip("Fx_Sparkle: the four-point star.")]
        [SerializeField] private Sprite _sparkleSprite;
        [Tooltip("Fx_Feather_1..3: picked at random per particle.")]
        [SerializeField] private Sprite[] _featherSprites = new Sprite[0];
        [Tooltip("Fx_EggShard_1..3: picked at random per particle (the Egg Shell hatch).")]
        [SerializeField] private Sprite[] _shardSprites = new Sprite[0];
        [Tooltip("Fx_SpeedLine: the Speed wind streak (Stretched Billboard).")]
        [SerializeField] private Sprite _speedLineSprite;

        // ---- Budget (maxParticles per family; sum = ParticlesPerChickenCap minus headroom) ----

        /// <summary>
        /// Dust family cap: the 6-puff death puff and the Speed stride dust (spec max 8) share it. Was 12; D4 took 5 for the
        /// Streak family (a dead bird does not sprint and the stride dust is spec-capped at 8, so 7 only clips the densest
        /// sprint by one puff).
        /// </summary>
        public const int DustMaxParticles = 7;
        /// <summary>
        /// Feather family cap: 10 death feathers + the 14-feather execute burst of HitFeedback. Was 24; D4 took 8: the
        /// execute burst still fits whole (16), only the death-plus-execute coincidence clips, and that kill is a one-frame
        /// moment, not a sprint.
        /// </summary>
        public const int FeatherMaxParticles = 16;
        /// <summary>
        /// Sparkle family cap: ability burst 16 + hit 8 + cargo ring (~7 live) + hit-confirm 10 rarely coincide. Was 28; D2
        /// took 3 for the Shard family and D4 took 5 for Streak so the five families still sum to the 64 per-chicken cap (the
        /// old cap was already below the four-way coincidence, so the clip is the same rare, invisible case). 20 keeps the
        /// cast burst 16 plus 4 of the Speed edge's 6 feet sparkles, the only coincidence that happens on every cast.
        /// </summary>
        public const int SparkleMaxParticles = 20;
        /// <summary>Shard family cap: the three egg shards of a hatch (real gravity, spin).</summary>
        public const int ShardMaxParticles = 3;
        /// <summary>
        /// Streak family cap (the Speed wind lines): 6 per metre x 0.25 s lifetime is 20 live at the boosted Warrior's 13 m/s
        /// and 24 at the boosted Speedy, so 18 thins the very fastest sprint by a quarter (streaks live 0.25 s, the density
        /// loss is not readable). Spec max was 24; the five families are pinned to sum 64 (7 + 16 + 20 + 3 + 18).
        /// </summary>
        public const int StreakMaxParticles = 18;

        public const string DustSystemName = "VFX_Dust";
        public const string FeatherSystemName = "VFX_Feather";
        public const string SparkleSystemName = "VFX_Sparkle";
        public const string ShardSystemName = "VFX_Shard";
        public const string StreakSystemName = "VFX_Streak";

        /// <summary>Cargo-full ring: emitted from a ticking loop in LateUpdate while the bird is full.</summary>
        private const float CargoRingRate = 12f;

        // ---- Runtime state ----------------------------------------------------

        /// <summary>The shared Fx_Particles material wired on the prefab (null on an unwired component).</summary>
        public Material ParticleMaterial => _particleMaterial;
        public Sprite DustSprite => _dustSprite;
        public Sprite SparkleSprite => _sparkleSprite;
        public Sprite[] FeatherSprites => _featherSprites;
        public Sprite[] ShardSprites => _shardSprites;
        public Sprite SpeedLineSprite => _speedLineSprite;

        private static Material s_sharedMaterial;

        private ChickenController _controller;
        private ChickenCombat     _combat;
        private ChickenCargo      _cargo;
        private AbilityController _abilities;

        private ParticleSystem _dustPS;
        private ParticleSystem _featherPS;
        private ParticleSystem _sparklePS;
        private ParticleSystem _shardPS;
        private ParticleSystem _streakPS;
        private ChickenAnimator _animator;
        private ParticleSystem.EmitParams _ep;   // cached: every emit rewrites it, nothing allocates

        // Effect presets built once from the serialized counts and colours.
        private FxEmit _hitBurst, _deathFeathers, _deathDust, _depositBurst, _abilityBurst, _cargoRing;

        private float _lastCargo = float.NaN;
        private bool  _wasRemoved;
        private bool  _wasCargoFull;
        private float _cargoRingAccum;

        // ── Speed body cue (D4): edge + distance accumulators, see ObserveSpeedBody ──
        private bool    _speedInitialized;
        private bool    _wasSpeedActive;
        private Vector3 _speedPrevPos;
        private float   _streakAccum, _speedDustAccum;
        private float   _leanBlend;
        private FxEmit  _speedDust, _speedEdge;

        // ── Replicated one-shot cast baseline (see ObserveCastEvent) ────────────
        // HitFeedback and AbilityRangeIndicator reset their equivalent "_castInitialized"
        // flags in Spawned() because they are NetworkBehaviours and Spawned() is the
        // documented point at which [Networked] properties become safe to read. ChickenVFX
        // is a plain MonoBehaviour with no Spawned hook, and Fusion instantiates a fresh
        // GameObject per chicken spawn (no INetworkObjectProvider/pool is registered
        // anywhere in this project), so Awake runs exactly once per spawn: the field's
        // default (false) already IS that seed; there is nothing to reset it from. The
        // first LateUpdate call after Awake is what actually captures the baseline id (see
        // ObserveCastEvent), by which point AbilityController.Spawned() has already run.
        private bool  _castInitialized;
        private byte  _lastCastEventId;
        private AbilityBaseSO _lastChargingAbility;

        // ---- Unity lifecycle --------------------------------------------------

        private void Awake()
        {
            _controller = GetComponent<ChickenController>();
            _combat     = GetComponent<ChickenCombat>();
            _cargo      = GetComponent<ChickenCargo>();
            _abilities  = GetComponent<AbilityController>();
            _animator   = GetComponent<ChickenAnimator>();

            if (_particleMaterial != null) s_sharedMaterial = _particleMaterial;
            var mat = _particleMaterial != null ? _particleMaterial : BuildMaterial();
            _dustPS    = BuildFamily(DustSystemName, mat, 2, DustMaxParticles, _dustSprite);
            _featherPS = BuildFamily(FeatherSystemName, mat, BodyFxSortingOrder, FeatherMaxParticles, _featherSprites);
            _sparklePS = BuildFamily(SparkleSystemName, mat, BodyFxSortingOrder, SparkleMaxParticles, _sparkleSprite);
            _shardPS   = BuildFamily(ShardSystemName, mat, BodyFxSortingOrder, ShardMaxParticles, _shardSprites);
            _streakPS  = BuildFamily(StreakSystemName, mat, BodyFxSortingOrder, StreakMaxParticles, _speedLineSprite);
            ConfigureStreakFamily(_streakPS);

            // Shards keep real gravity (the other families bake theirs into the launch velocity) and tumble: callers
            // emit them with Gravity 0 and the system pulls them down.
            var shardMain = _shardPS.main;
            shardMain.gravityModifier = 1f;
            var spin = _shardPS.rotationOverLifetime;
            spin.enabled = true;
            spin.separateAxes = false;
            spin.z = new ParticleSystem.MinMaxCurve(-2f * Mathf.PI, 2f * Mathf.PI);   // radians/s: +-360 deg/s

            // Air drag so feathers slow to a drift instead of flying off like sparks.
            var vel = _featherPS.limitVelocityOverLifetime;
            vel.enabled = true;
            vel.dampen  = 0.35f;
            vel.limit   = new ParticleSystem.MinMaxCurve(0.6f);

            BuildPresets();
        }

        private void BuildPresets()
        {
            _speedDust = new FxEmit
            {
                Count = 1, Color = new Color(0.98f, 0.94f, 0.84f, FeedbackTuning.SpeedDustAlpha),
                SizeMin = FeedbackTuning.SpeedDustSize, SizeMax = FeedbackTuning.SpeedDustSize,
                SpeedMin = 0f, SpeedMax = 0f, LifeMin = FeedbackTuning.SpeedDustLifeSeconds, LifeMax = FeedbackTuning.SpeedDustLifeSeconds,
                Shape = FxShape.Disc, Radius = 0.15f, Gravity = 0f,
            };
            // Colour overwritten per cast with the Speed accent.
            _speedEdge = new FxEmit
            {
                Count = FeedbackTuning.SpeedEdgeSparkleCount, Color = Color.white, SizeMin = 0.22f, SizeMax = 0.34f,
                SpeedMin = 0.8f, SpeedMax = 1.6f, LifeMin = FeedbackTuning.SpeedEdgeSparkleLifeSeconds, LifeMax = FeedbackTuning.SpeedEdgeSparkleLifeSeconds,
                Shape = FxShape.Disc, Radius = 0.25f, Gravity = 0f,
            };

            _hitBurst = new FxEmit
            {
                Count = _hitCount, Color = _hitColor, SizeMin = 0.24f, SizeMax = 0.40f,
                SpeedMin = 2.0f, SpeedMax = 5.0f, LifeMin = 0.15f, LifeMax = 0.30f,
                Shape = FxShape.Sphere, Radius = 0.20f, Gravity = 2.0f,
            };
            // Colour overwritten at removal with the class tint (the bird comes apart in its own colour).
            _deathFeathers = new FxEmit
            {
                Count = _deathFeatherCount, Color = Color.white, SizeMin = 0.30f, SizeMax = 0.50f,
                SpeedMin = 2.0f, SpeedMax = 4.5f, LifeMin = 0.50f, LifeMax = 0.80f,
                Shape = FxShape.Sphere, Radius = 0.30f, Gravity = 0.6f, RandomRotation = true,
            };
            _deathDust = new FxEmit
            {
                Count = _deathDustCount, Color = _deathDustColor, SizeMin = 0.30f, SizeMax = 0.50f,
                SpeedMin = 0.8f, SpeedMax = 1.8f, LifeMin = 0.35f, LifeMax = 0.55f,
                Shape = FxShape.Disc, Radius = 0.25f, Gravity = 0f,
            };
            _depositBurst = new FxEmit
            {
                Count = _depositCount, Color = _depositColor, SizeMin = 0.24f, SizeMax = 0.42f,
                SpeedMin = 2.5f, SpeedMax = 4.5f, LifeMin = 0.45f, LifeMax = 0.65f,
                Shape = FxShape.Cone, Radius = 0.10f, ConeAngle = 35f, Axis = Vector3.up, Gravity = -1.5f,
            };
            // Colour overwritten per cast with the ability accent.
            _abilityBurst = new FxEmit
            {
                Count = 16, Color = Color.white, SizeMin = 0.22f, SizeMax = 0.40f,
                SpeedMin = 1.5f, SpeedMax = 3.5f, LifeMin = 0.30f, LifeMax = 0.55f,
                Shape = FxShape.Sphere, Radius = 0.25f, Gravity = -0.8f,
            };
            _cargoRing = new FxEmit
            {
                Count = 1, Color = new Color(1.00f, 0.85f, 0.15f, 1f), SizeMin = 0.20f, SizeMax = 0.32f,
                SpeedMin = 0.30f, SpeedMax = 0.65f, LifeMin = 0.40f, LifeMax = 0.60f,
                Shape = FxShape.Disc, Radius = 0.38f, Gravity = -0.25f,
            };
        }

        private void LateUpdate()
        {
            if (_combat == null) return;

            bool isRemoved = _combat.IsRemoved;
            // A1: nothing emits on a hidden bird (Hide / Smoke Roost) for a remote peer; its owner still sees it all.
            bool hidden = HiddenVisibilityRules.IsHiddenFor(_controller);

            // Removal (death) burst. The old 7/s "stun orbit" dribble that also started here duplicated the stars
            // ChickenStateOverlays raises on the same IsRemoved flag and competed in the 1.62 m band: deleted (D1).
            if (isRemoved && !_wasRemoved && !hidden)
            {
                var feathers = _deathFeathers;
                feathers.Color = _controller != null ? _controller.ClassBodyTint : Color.white;
                EmitFeathers(in feathers, 0.7f);
                EmitDust(in _deathDust, 0.3f);
            }

            _wasRemoved = isRemoved;

            ObserveSpeedBody(hidden, isRemoved);

            // Ability activation: AccentColor burst + caster micro-shake on cast. Detection used to be a rising-edge
            // test on the old single ActiveSlot (since retired: abilities now run concurrently on a per-slot mask).
            // That was broken three ways: (1) a short ability can set and clear its active flag inside a single
            // tick, so LateUpdate never observes the non-Invalid value and the burst/shake silently never plays;
            // (2) back-to-back casts of two different slots produce no Invalid frame in between, so the second
            // cast is silent too; (3) there was no baseline seeding, so a peer that first renders a chicken
            // mid-ability (a late joiner) replayed a burst for a cast that had already happened.
            // HitFeedback.ObserveOwnCast and AbilityRangeIndicator.ObserveCast hit the exact same three failures and
            // were both fixed by keying off the replicated one-shot LastCastEventId instead of a slot transition:
            // this does the same. Do not revert to an active-flag edge-detect.
            if (_abilities != null) ObserveCastEvent(hidden);

            // Food deposit + cargo-full ring. [Networked] Cargo replicates to every peer so both effects fire on all
            // clients automatically: no extra RPC needed.
            if (_cargo != null)
            {
                float cargo       = _cargo.Cargo;
                bool  isCargoFull = _cargo.IsFull && !isRemoved && !hidden;   // hiding stops the loop, revealing restarts it

                // Deposit burst: cargo just dropped from a meaningful amount to empty.
                if (!float.IsNaN(_lastCargo) && _lastCargo > 1f && cargo < 0.1f && !isRemoved && !hidden)
                    EmitSparkles(in _depositBurst, 0.5f);

                // Cargo-full ring: a looping orbit that runs while the bird is full, emitted from this tick.
                if (isCargoFull)
                {
                    _cargoRingAccum += CargoRingRate * Time.deltaTime;
                    int n = (int)_cargoRingAccum;
                    _cargoRingAccum -= n;
                    for (int i = 0; i < n; i++) EmitSparkles(in _cargoRing, 0.65f);
                }
                else if (_wasCargoFull)
                {
                    _cargoRingAccum = 0f;
                }

                _lastCargo    = cargo;
                _wasCargoFull = isCargoFull;
            }
        }

        // ---- Speed body cue (D4, vfx.md 2.5) ---------------------------------------

        private void OnDisable()
        {
            if (_animator != null) _animator.ExtraPitchDegrees = 0f;
        }

        /// <summary>
        /// The Speed body cue, observed from replicated state (the slot active mask) and the bird's own planar motion: a
        /// local effect on every peer, no RPC. The first observation only seeds the edge baseline, so a late join into a
        /// running burst gets the streaks (state) but not the "go!" sparkles (edge). Streaks and stride dust are emitted by
        /// distance travelled (the root moves through Fusion, so rate-over-distance is accumulated here), placed along the
        /// path of the frame; emission stops the frame the slot goes inactive or the bird slows below half its MoveSpeed.
        /// Nothing emits on a hidden or faded bird. The +6 degree lean goes through <see cref="ChickenAnimator.ExtraPitchDegrees"/>
        /// (the animator lean saturates at the base MoveSpeed, so the 1.3x burst adds none of its own).
        /// </summary>
        private void ObserveSpeedBody(bool hidden, bool removed)
        {
            Vector3 pos = transform.position;
            Vector3 delta = pos - _speedPrevPos;
            Vector3 prev = _speedPrevPos;
            _speedPrevPos = pos;
            delta.y = 0f;

            var abObj = _abilities != null ? _abilities.Object : null;
            if (abObj == null || !abObj.IsValid || _controller == null) return;

            SpeedBurstAbilitySO speedSO = null;
            for (int i = 0; i < AbilityController.SlotCount; i++)
            {
                if (_abilities.GetSlot(i) is SpeedBurstAbilitySO s && _abilities.IsSlotActive(i)) { speedSO = s; break; }
            }
            bool active = speedSO != null && !removed;

            if (!_speedInitialized)
            {
                _speedInitialized = true;
                _wasSpeedActive = active;
                return;
            }

            bool suppressed = hidden || ControlStateVFX.IsFadedOpacity(_controller.VisualOpacity);
            bool reduced = PlayerPreferences.ReducedMotionEnabled;

            if (active && !_wasSpeedActive && !suppressed)
            {
                var edge = _speedEdge;
                edge.Color = speedSO.AccentColor;
                EmitSparkles(in edge, 0.1f);
            }
            _wasSpeedActive = active;

            float dt = Time.unscaledDeltaTime;
            float planar = dt > 1e-5f ? delta.magnitude / dt : 0f;
            var stats = _controller.Stats;
            bool running = active && stats != null && SpeedBodyCueRules.ShouldRun(true, planar, stats.MoveSpeed);

            if (running && !suppressed)
            {
                float step = SpeedBodyCueRules.StepMetres(delta.magnitude);
                Vector3 dir = delta / Mathf.Max(delta.magnitude, 1e-5f);
                int streaks = SpeedBodyCueRules.Take(ref _streakAccum, step, SpeedBodyCueRules.StreaksPerMetre(reduced));
                int dust = SpeedBodyCueRules.Take(ref _speedDustAccum, step, SpeedBodyCueRules.DustPerMetre(reduced));
                Color accent = speedSO.AccentColor;
                accent.a = FeedbackTuning.SpeedStreakAlpha;
                for (int i = 0; i < streaks; i++)
                    EmitStreak(Vector3.Lerp(prev, pos, (i + 1f) / (streaks + 1f)), dir, accent);
                for (int i = 0; i < dust; i++)
                    EmitDustAt(in _speedDust, Vector3.Lerp(prev, pos, (i + 1f) / (dust + 1f)) + Vector3.up * 0.05f);
            }
            else
            {
                _streakAccum = 0f;
                _speedDustAccum = 0f;
            }

            if (_animator != null)
            {
                _leanBlend = Mathf.MoveTowards(_leanBlend, running ? 1f : 0f, FeedbackTuning.SpeedLeanBlendPerSecond * Time.unscaledDeltaTime);
                _animator.ExtraPitchDegrees = FeedbackTuning.SpeedLeanExtraDegrees * _leanBlend;
            }
        }

        /// <summary>
        /// One wind streak left behind at hip height: a point in the 0.3 (across) x 0.1 (along) box at the bird's tail, with a
        /// tiny backward velocity (a zero-velocity Stretched Billboard has no direction to stretch along).
        /// </summary>
        private void EmitStreak(Vector3 rootPos, Vector3 moveDir, Color color)
        {
            if (_streakPS == null) return;
            Vector3 right = new Vector3(moveDir.z, 0f, -moveDir.x);
            Vector3 p = rootPos - moveDir * (FeedbackTuning.SpeedStreakTailOffset + Random.Range(-0.5f, 0.5f) * FeedbackTuning.SpeedStreakBoxAlong)
                        + right * (Random.Range(-0.5f, 0.5f) * FeedbackTuning.SpeedStreakBoxAcross);
            p.y = rootPos.y + Random.Range(FeedbackTuning.SpeedStreakMinHeight, FeedbackTuning.SpeedStreakMaxHeight);
            _ep.applyShapeToPosition = false;
            _ep.position = p;
            _ep.velocity = -moveDir * FeedbackTuning.SpeedStreakBackSpeed;
            _ep.startLifetime = FeedbackTuning.SpeedStreakLifeSeconds;
            _ep.startSize = FeedbackTuning.SpeedStreakSize;
            _ep.startColor = color;
            _ep.rotation = 0f;
            _streakPS.Emit(_ep, 1);
        }

        /// <summary>
        /// Streak family: Stretched Billboard (speed scale 0.12, length scale raised from the spec 1 so the streak reads
        /// as a line), size 0.30 to 0 and alpha 0.7 to 0 over the life (linear), instead of the shared late fade.
        /// </summary>
        private static void ConfigureStreakFamily(ParticleSystem ps)
        {
            if (ps == null) return;
            var rend = ps.GetComponent<ParticleSystemRenderer>();
            rend.renderMode = ParticleSystemRenderMode.Stretch;
            rend.velocityScale = FeedbackTuning.SpeedStreakSpeedScale;
            rend.lengthScale = FeedbackTuning.SpeedStreakLengthScale;
            rend.cameraVelocityScale = 0f;

            var sol = ps.sizeOverLifetime;
            sol.enabled = true;
            sol.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0f));

            var col = ps.colorOverLifetime;
            col.enabled = true;
            var g = new Gradient();
            g.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
            col.color = new ParticleSystem.MinMaxGradient(g);
        }

        // ---- Emit API (used by later batch-D items and HitFeedback) --------------

        /// <summary>World position of a point <paramref name="height"/> metres up the chicken root.</summary>
        public Vector3 FxPoint(float height) => transform.TransformPoint(new Vector3(0f, height, 0f));

        /// <summary>Emits <paramref name="e"/> from the Dust family at <paramref name="height"/> on this chicken.</summary>
        public void EmitDust(in FxEmit e, float height) => Emit(_dustPS, in e, FxPoint(height));

        /// <summary>Emits <paramref name="e"/> from the Feather family at <paramref name="height"/> on this chicken.</summary>
        public void EmitFeathers(in FxEmit e, float height) => Emit(_featherPS, in e, FxPoint(height));

        /// <summary>Emits <paramref name="e"/> from the Sparkle family at <paramref name="height"/> on this chicken.</summary>
        public void EmitSparkles(in FxEmit e, float height) => Emit(_sparklePS, in e, FxPoint(height));

        /// <summary>Emits <paramref name="e"/> from the Shard family (real gravity, spin) at the world point <paramref name="world"/>.</summary>
        public void EmitShards(in FxEmit e, Vector3 world) => Emit(_shardPS, in e, world);

        /// <summary>World-position variants, for effects that are not centred on the chicken root.</summary>
        public void EmitDustAt(in FxEmit e, Vector3 world) => Emit(_dustPS, in e, world);
        public void EmitFeathersAt(in FxEmit e, Vector3 world) => Emit(_featherPS, in e, world);
        public void EmitSparklesAt(in FxEmit e, Vector3 world) => Emit(_sparklePS, in e, world);

        private void Emit(ParticleSystem ps, in FxEmit e, Vector3 world)
        {
            if (ps == null || e.Count <= 0) return;
            _ep.applyShapeToPosition = false;
            for (int i = 0; i < e.Count; i++)
            {
                float life = Random.Range(e.LifeMin, e.LifeMax);
                Vector3 offset, dir;
                switch (e.Shape)
                {
                    case FxShape.Cone:
                    {
                        Vector2 d = Random.insideUnitCircle * e.Radius;
                        Vector3 axis = e.Axis.sqrMagnitude > 0f ? e.Axis.normalized : Vector3.up;
                        var rot = Quaternion.FromToRotation(Vector3.up, axis);
                        offset = rot * new Vector3(d.x, 0f, d.y);
                        float tilt = Random.Range(Mathf.Min(e.ConeMinAngle, e.ConeAngle), e.ConeAngle) * Mathf.Deg2Rad;
                        float az = Random.Range(0f, Mathf.PI * 2f);
                        dir = rot * new Vector3(Mathf.Sin(tilt) * Mathf.Cos(az), Mathf.Cos(tilt), Mathf.Sin(tilt) * Mathf.Sin(az));
                        break;
                    }
                    case FxShape.Disc:
                    {
                        Vector2 d = Random.insideUnitCircle * e.Radius;
                        offset = new Vector3(d.x, 0f, d.y);
                        dir = offset.sqrMagnitude > 1e-6f ? offset.normalized : Vector3.right;
                        break;
                    }
                    default:
                    {
                        offset = Random.insideUnitSphere * e.Radius;
                        dir = offset.sqrMagnitude > 1e-6f ? offset.normalized : Random.onUnitSphere;
                        break;
                    }
                }
                Vector3 v = dir * Random.Range(e.SpeedMin, e.SpeedMax);
                v.y -= 0.5f * e.Gravity * EarthG * life;   // gravity as an end-of-life-equivalent constant lift/drop
                _ep.position = world + offset;
                _ep.velocity = v;
                _ep.startLifetime = life;
                _ep.startSize = Random.Range(e.SizeMin, e.SizeMax);
                _ep.startColor = e.Color;
                _ep.rotation = e.RandomRotation ? Random.Range(0f, 360f) : 0f;
                ps.Emit(_ep, 1);
            }
        }

        /// <summary>
        /// Fires the AccentColor burst and the caster micro-shake off the replicated
        /// <c>LastCastEventId</c> one-shot. Baseline-seeded exactly like
        /// <c>HitFeedback.ObserveOwnCast</c> and <c>AbilityRangeIndicator.ObserveCast</c>,
        /// whose burst/flash halves of the same event this deliberately does not duplicate.
        /// </summary>
        private void ObserveCastEvent(bool hidden)
        {
            // TryActivate clears ChargingSlot in the same tick it fires, and a very short
            // ability's active flag may already have cleared by the time this observes the
            // event — so remember what was last aimed, same as HitFeedback/AbilityRangeIndicator.
            if (_abilities.ChargingSlot != 0)
            {
                var charging = _abilities.ChargingAbility;
                if (charging != null) _lastChargingAbility = charging;
            }

            byte id = _abilities.LastCastEventId;
            if (!_castInitialized)
            {
                // Seed from whatever id is already replicated so a late-joining peer never
                // plays a burst/shake for a cast that happened before it connected.
                _castInitialized = true;
                _lastCastEventId = id;
                return;
            }
            if (id == _lastCastEventId) return;
            _lastCastEventId = id;

            var ability = _abilities.LastCastAbility ?? _lastChargingAbility;

            // The burst needs AccentColor, so an unresolved ability means no burst. None on a hidden bird (A1).
            if (ability != null && !hidden)
            {
                var burst = _abilityBurst;
                burst.Color = ability.AccentColor;
                EmitSparkles(in burst, 1.0f);
            }

            // FEEDBACK.md §3.1's "Always" caster micro-shake: fires on EVERY cast, hit or
            // whiff, local player only. Stage 4's HitFeedback issues the same shake again
            // on a *landed* cast (§3.2's hit-confirm); both now read the identical
            // FeedbackTuning constants, and both are now keyed off this same
            // LastCastEventId, so MatchCamera.ApplyShake's max-wins makes the overlap a
            // no-op rather than a double punch. The literals that used to sit here
            // (0.18/0.22, 0.06/0.12) were the source those constants were derived from —
            // reading them back from FeedbackTuning is what makes a future re-tune actually
            // take effect on both call sites.
            //
            // Deliberately NOT gated on ability != null: unlike the burst, the shake does
            // not depend on AccentColor, and §3.1 wants it on every cast without exception.
            // Only the steal-flavoured magnitude needs the ability's Category, which is
            // unknowable in the rare case neither LastCastAbility nor the remembered charging
            // ability resolved (effectively: a late joiner whose very first observed cast
            // had no charging phase left to remember). Falling back to the base magnitude
            // there is a closer match to "always shake" than skipping it outright.
            if (_controller != null && _controller.HasInputAuthority && MatchCamera.Instance != null)
            {
                bool steal = ability != null && ability.Category == AbilityCategory.Steal;
                MatchCamera.Instance.ApplyShake(
                    steal ? FeedbackTuning.CasterStealShakeMagnitude
                          : FeedbackTuning.CasterMicroShakeMagnitude,
                    steal ? FeedbackTuning.CasterStealShakeDurationSeconds
                          : FeedbackTuning.CasterMicroShakeDurationSeconds);
            }
        }

        // ---- Shared material -------------------------------------------------

        /// <summary>
        /// The shared particle material. Once any chicken has woken this is the wired <c>Fx_Particles.mat</c> asset (the
        /// same instance for every system). Before the first chicken exists (the arena warm-up proxy) it falls back to a
        /// material built from the same shader and the same <see cref="ConfigureParticleMaterial"/> settings, so the shader
        /// variant it warms is the one the match draws. The fallback is never used by a wired chicken.
        /// </summary>
        public static Material BuildMaterial()
        {
            if (s_sharedMaterial != null) return s_sharedMaterial;
            Shader sh = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (sh == null) sh = Shader.Find("Particles/Standard Unlit");
            if (sh == null) sh = Shader.Find("Legacy Shaders/Particles/Alpha Blended Premultiply");
            if (sh == null) return null;
            var m = new Material(sh);
            ConfigureParticleMaterial(m);
            return m;
        }

        /// <summary>
        /// Settings of <c>Fx_Particles.mat</c>: alpha-blend transparent, ZWrite off, no soft particles, no camera fade, no
        /// distortion (all mobile-hostile). Vertex colour always multiplies in the URP particle shaders, so the per-particle
        /// start colour tint needs no switch. The texture is left empty: sprite mode feeds the atlas texture per renderer.
        /// Used by the editor asset creation and by the warm-up fallback so the two cannot drift.
        /// </summary>
        public static void ConfigureParticleMaterial(Material m)
        {
            if (m == null) return;
            if (m.HasProperty("_Surface")) m.SetFloat("_Surface", 1f);            // transparent
            if (m.HasProperty("_Blend")) m.SetFloat("_Blend", 0f);                // alpha
            if (m.HasProperty("_SrcBlend")) m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            if (m.HasProperty("_DstBlend")) m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            if (m.HasProperty("_SrcBlendAlpha")) m.SetFloat("_SrcBlendAlpha", (float)UnityEngine.Rendering.BlendMode.One);
            if (m.HasProperty("_DstBlendAlpha")) m.SetFloat("_DstBlendAlpha", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            if (m.HasProperty("_ZWrite")) m.SetFloat("_ZWrite", 0f);
            if (m.HasProperty("_SoftParticlesEnabled")) m.SetFloat("_SoftParticlesEnabled", 0f);
            if (m.HasProperty("_CameraFadingEnabled")) m.SetFloat("_CameraFadingEnabled", 0f);
            if (m.HasProperty("_DistortionEnabled")) m.SetFloat("_DistortionEnabled", 0f);
            if (m.HasProperty("_FlipbookBlending")) m.SetFloat("_FlipbookBlending", 0f);
            m.SetOverrideTag("RenderType", "Transparent");
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            m.DisableKeyword("_SOFTPARTICLES_ON");
            m.DisableKeyword("_FADING_ON");
            m.DisableKeyword("_DISTORTION_ON");
            m.DisableKeyword("_FLIPBOOKBLENDING_ON");
            m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        }

        /// <summary>
        /// Puts <paramref name="ps"/> in Texture Sheet Animation sprite mode over <paramref name="sprites"/>: one sprite
        /// per particle (a random pick when there are several), frozen for the whole life. Null entries are skipped; with
        /// none left the system stays untextured. Shared by every chicken-attached system (also <see cref="HitFeedback"/>).
        /// </summary>
        public static void ApplySprites(ParticleSystem ps, params Sprite[] sprites)
        {
            if (ps == null || sprites == null) return;
            int n = 0;
            var ts = ps.textureSheetAnimation;
            foreach (var sp in sprites)
            {
                if (sp == null) continue;
                if (n == 0) { ts.enabled = true; ts.mode = ParticleSystemAnimationMode.Sprites; }
                ts.AddSprite(sp);
                n++;
            }
            if (n == 0) return;
            ts.timeMode = ParticleSystemAnimationTimeMode.Lifetime;
            ts.numTilesX = 1;
            ts.numTilesY = 1;
            ts.cycleCount = 1;
            ts.frameOverTime = new ParticleSystem.MinMaxCurve(0f);
            ts.startFrame = n > 1 ? new ParticleSystem.MinMaxCurve(0f, 0.999f) : new ParticleSystem.MinMaxCurve(0f);
        }

        // ---- Particle-system factory ---------------------------------------------

        /// <summary>Sorting order of the body-height FX band: sparks, feathers, bursts (vfx.md 0.1).</summary>
        public const int BodyFxSortingOrder = 5;

        /// <summary>
        /// Builds one family system: a child under the chicken, world simulation space, zero gravity, no shape (positions and
        /// velocities come from <see cref="Emit"/>), shared material, sprite mode, an explicit maxParticles, prewarm off,
        /// alpha fade over the last quarter of life.
        /// </summary>
        private ParticleSystem BuildFamily(string goName, Material mat, int sortingOrder, int maxParticles, params Sprite[] sprites)
        {
            var go = new GameObject(goName);
            go.transform.SetParent(transform, worldPositionStays: false);

            var ps   = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop            = false;
            main.playOnAwake     = false;
            main.prewarm         = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.gravityModifier = 0f;
            main.maxParticles    = maxParticles;
            main.startSpeed      = 0f;

            var em = ps.emission;
            em.enabled = true;
            em.rateOverTime = 0f;
            var sh = ps.shape;
            sh.enabled = false;

            var rend = go.GetComponent<ParticleSystemRenderer>();
            rend.renderMode   = ParticleSystemRenderMode.Billboard;
            rend.sortingOrder = sortingOrder;
            if (mat != null) rend.sharedMaterial = mat;   // the shared asset, never an instance
            ApplySprites(ps, sprites);

            AddFadeOut(ps);
            ps.Play();   // stays playing: Emit() adds particles, there is no timeline
            return ps;
        }

        /// <summary>
        /// Attaches a <c>colorOverLifetime</c> module that fades alpha from 1 to 0 over the last 25% of each particle's
        /// life. RGB is held constant so the emitted start colour drives the tint.
        /// </summary>
        private static void AddFadeOut(ParticleSystem ps)
        {
            var col = ps.colorOverLifetime;
            col.enabled = true;
            var g = new Gradient();
            g.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 0.72f), new GradientAlphaKey(0f, 1f) });
            col.color = new ParticleSystem.MinMaxGradient(g);
        }
    }
}

using CluckWars.Abilities;
using CluckWars.Gameplay;
using UnityEngine;

namespace CluckWars.Visuals
{
    /// <summary>
    /// Local-only VFX for a chicken: procedurally-built particle systems for hit
    /// sparks, death burst, stun orbit, and food-deposit celebration. All effects
    /// are triggered by polling <c>[Networked]</c> state (HP, IsStunned, Cargo)
    /// in <c>LateUpdate</c> — no RPCs, fires on every peer automatically.
    /// </summary>
    /// <remarks>
    /// Particle materials use the URP Particles/Unlit shader with a fallback to
    /// the legacy Alpha Blended shader so the visuals work on both URP and
    /// Standard pipelines without manual asset setup.
    ///
    /// <b>Maestro</b>: add this component to the Chicken prefab alongside
    /// <see cref="ChickenVisuals"/>. No Inspector wiring required — all four
    /// particle systems are built and positioned automatically in <c>Awake</c>.
    /// Tweak colors and counts via the serialized fields if needed.
    /// </remarks>
    [RequireComponent(typeof(ChickenController))]
    public sealed class ChickenVFX : MonoBehaviour
    {
        // ---- Tunables (Inspector-tweakable) -----------------------------------

        [Header("Hit sparks")]
        [Tooltip("Particles ejected when this chicken takes a hit.")]
        [Min(1)] [SerializeField] private int   _hitCount  = 8;
        [SerializeField] private Color _hitColor = new Color(1.00f, 0.78f, 0.20f, 1f);

        [Header("Death burst")]
        [Tooltip("Particles ejected when this chicken dies.")]
        [Min(1)] [SerializeField] private int   _deathCount = 22;
        [SerializeField] private Color _deathColor = new Color(0.95f, 0.30f, 0.20f, 1f);

        [Header("Stun orbit")]
        [Tooltip("Particles that float around the head while this chicken is stunned.")]
        [SerializeField] private Color _stunColor = new Color(1.00f, 0.96f, 0.35f, 1f);
        [Min(0f)] [SerializeField] private float _stunOrbitRadius = 0.40f;
        [Min(0f)] [SerializeField] private float _stunHeadOffset  = 1.75f;

        [Header("Deposit burst")]
        [Tooltip("Upward burst of gold when cargo is deposited at the base.")]
        [Min(1)] [SerializeField] private int   _depositCount = 12;
        [SerializeField] private Color _depositColor = new Color(0.96f, 0.78f, 0.26f, 1f);

        // ---- Runtime state ----------------------------------------------------

        private ChickenController _controller;
        private ChickenCombat     _combat;
        private ChickenCargo      _cargo;
        private AbilityController _abilities;

        private ParticleSystem _hitPS;
        private ParticleSystem _deathPS;
        private ParticleSystem _stunPS;
        private ParticleSystem _depositPS;
        private ParticleSystem _abilityPS;
        private ParticleSystem _cargoFullPS;

        private float _lastHp    = float.NaN;
        private float _lastCargo = float.NaN;
        private bool  _wasStunned;
        private bool  _wasCargoFull;

        // ── Replicated one-shot cast baseline (see ObserveCastEvent) ────────────
        // HitFeedback and AbilityRangeIndicator reset their equivalent "_castInitialized"
        // flags in Spawned() because they are NetworkBehaviours and Spawned() is the
        // documented point at which [Networked] properties become safe to read. ChickenVFX
        // is a plain MonoBehaviour with no Spawned hook, and Fusion instantiates a fresh
        // GameObject per chicken spawn (no INetworkObjectProvider/pool is registered
        // anywhere in this project), so Awake runs exactly once per spawn — the field's
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

            var mat  = BuildMaterial();
            _hitPS       = BuildHitPS(mat);
            _deathPS     = BuildDeathPS(mat);
            _stunPS      = BuildStunPS(mat);
            _depositPS   = BuildDepositPS(mat);
            _abilityPS   = BuildAbilityPS(mat);
            _cargoFullPS = BuildCargoFullPS(mat);
        }

        private void LateUpdate()
        {
            if (_combat == null) return;

            bool isStunned = _combat.IsRemoved;

            // ── Death → stun begin ───────────────────────────────────────────
            if (isStunned && !_wasStunned)
            {
                _deathPS?.Play();
                _stunPS?.Play();
            }

            // ── Respawn → stun end ───────────────────────────────────────────
            if (!isStunned && _wasStunned)
                _stunPS?.Stop(true, ParticleSystemStopBehavior.StopEmitting);

            _wasStunned = isStunned;

            // ── Ability activation: AccentColor burst + caster micro-shake on cast ───
            // Detection used to be a rising-edge test on ActiveSlot (Invalid → non-Invalid
            // → ... → Invalid). That was broken three ways: (1) a short ability can set and
            // clear ActiveSlot inside a single tick, so LateUpdate never observes the
            // non-Invalid value and the burst/shake silently never plays; (2) back-to-back
            // casts of two different slots produce no Invalid frame in between, so the
            // second cast is silent too; (3) there was no baseline seeding, so a peer that
            // first renders a chicken mid-ability (a late joiner) replayed a burst for a
            // cast that had already happened. HitFeedback.ObserveOwnCast and
            // AbilityRangeIndicator.ObserveCast hit the exact same three failures and were
            // both fixed by keying off the replicated one-shot LastCastEventId instead of a
            // slot transition — this does the same. Do not revert to an ActiveSlot
            // edge-detect; it will silently reintroduce all three bugs.
            if (_abilities != null) ObserveCastEvent();

            // ── Food deposit + cargo-full ring ────────────────────────────────
            // [Networked] Cargo replicates to every peer so both effects fire
            // on all clients automatically — no extra RPC needed.
            if (_cargo != null)
            {
                float cargo       = _cargo.Cargo;
                bool  isCargoFull = _cargo.IsFull && !isStunned;

                // Deposit burst: cargo just dropped from a meaningful amount to empty.
                if (!float.IsNaN(_lastCargo) && _lastCargo > 1f && cargo < 0.1f && !isStunned)
                    _depositPS?.Play();

                // Cargo-full ring: looping orbit that turns on/off with capacity state.
                if (isCargoFull && !_wasCargoFull)
                    _cargoFullPS?.Play();
                else if (!isCargoFull && _wasCargoFull)
                    _cargoFullPS?.Stop(true, ParticleSystemStopBehavior.StopEmitting);

                _lastCargo    = cargo;
                _wasCargoFull = isCargoFull;
            }
        }

        /// <summary>
        /// Fires the AccentColor burst and the caster micro-shake off the replicated
        /// <c>LastCastEventId</c> one-shot. Baseline-seeded exactly like
        /// <c>HitFeedback.ObserveOwnCast</c> and <c>AbilityRangeIndicator.ObserveCast</c>,
        /// whose burst/flash halves of the same event this deliberately does not duplicate.
        /// </summary>
        private void ObserveCastEvent()
        {
            // TryActivate clears ChargingSlot in the same tick it fires, and a very short
            // ability's ActiveSlot may already have expired by the time this observes the
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

            var ability = _abilities.ActiveAbility ?? _lastChargingAbility;

            // The burst needs AccentColor, so an unresolved ability means no burst.
            if (ability != null && _abilityPS != null)
            {
                var main = _abilityPS.main;
                main.startColor = new ParticleSystem.MinMaxGradient(ability.AccentColor);
                _abilityPS.Play();
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
            // unknowable in the rare case neither ActiveAbility nor the remembered charging
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
        /// Returns a material suitable for particle rendering. Tries the URP
        /// unlit particles shader first; falls back to the legacy equivalent so
        /// it works on either render pipeline without manual setup.
        /// </summary>
        private static Material BuildMaterial()
        {
            Shader sh = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (sh == null) sh = Shader.Find("Particles/Standard Unlit");
            if (sh == null) sh = Shader.Find("Legacy Shaders/Particles/Alpha Blended Premultiply");
            return sh != null ? new Material(sh) : null;
        }

        // ---- Particle-system factory helpers ---------------------------------

        /// <summary>
        /// Creates a child <see cref="ParticleSystem"/> with common defaults applied.
        /// Individual Build* methods override only what differs.
        /// </summary>
        private ParticleSystem MakePS(string goName, Vector3 localOffset, Material mat,
            bool looping = false)
        {
            var go = new GameObject(goName);
            go.transform.SetParent(transform, worldPositionStays: false);
            go.transform.localPosition = localOffset;

            var ps   = go.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.loop            = looping;
            main.playOnAwake     = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;

            var rend = go.GetComponent<ParticleSystemRenderer>();
            rend.renderMode  = ParticleSystemRenderMode.Billboard;
            rend.sortingOrder = 2; // render in front of the chicken mesh
            if (mat != null) rend.material = mat;

            return ps;
        }

        /// <summary>
        /// Attaches a <c>colorOverLifetime</c> module that fades alpha from 1 to 0
        /// over the last 25% of each particle's lifetime. RGB is held constant so
        /// <c>main.startColor</c> drives the actual tint.
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

        // ---- Hit sparks -------------------------------------------------------

        private ParticleSystem BuildHitPS(Material mat)
        {
            var ps   = MakePS("VFX_Hit", new Vector3(0f, 0.5f, 0f), mat);
            var main = ps.main;
            main.startLifetime   = new ParticleSystem.MinMaxCurve(0.15f, 0.30f);
            main.startSpeed      = new ParticleSystem.MinMaxCurve(2.0f,  5.0f);
            main.startSize       = new ParticleSystem.MinMaxCurve(0.05f, 0.10f);
            main.startColor      = _hitColor;
            main.gravityModifier = 2.0f;
            main.maxParticles    = _hitCount * 2;

            var em = ps.emission;
            em.rateOverTime = 0;
            em.SetBursts(new[] { new ParticleSystem.Burst(0f, _hitCount) });

            var sh = ps.shape;
            sh.enabled   = true;
            sh.shapeType = ParticleSystemShapeType.Sphere;
            sh.radius    = 0.20f;

            AddFadeOut(ps);
            return ps;
        }

        // ---- Death burst ------------------------------------------------------

        private ParticleSystem BuildDeathPS(Material mat)
        {
            var ps   = MakePS("VFX_Death", new Vector3(0f, 0.5f, 0f), mat);
            var main = ps.main;
            main.startLifetime   = new ParticleSystem.MinMaxCurve(0.40f, 0.70f);
            main.startSpeed      = new ParticleSystem.MinMaxCurve(3.0f,  7.0f);
            main.startSize       = new ParticleSystem.MinMaxCurve(0.06f, 0.14f);
            main.startColor      = _deathColor;
            main.gravityModifier = 2.5f;
            main.maxParticles    = _deathCount * 2;

            var em = ps.emission;
            em.rateOverTime = 0;
            em.SetBursts(new[] { new ParticleSystem.Burst(0f, _deathCount) });

            var sh = ps.shape;
            sh.enabled   = true;
            sh.shapeType = ParticleSystemShapeType.Sphere;
            sh.radius    = 0.30f;

            AddFadeOut(ps);
            return ps;
        }

        // ---- Stun orbit -------------------------------------------------------

        private ParticleSystem BuildStunPS(Material mat)
        {
            // Positioned at head height; particles drift slightly upward then fade.
            var ps   = MakePS("VFX_Stun", new Vector3(0f, _stunHeadOffset, 0f), mat, looping: true);
            var main = ps.main;
            main.startLifetime   = new ParticleSystem.MinMaxCurve(0.45f, 0.65f);
            main.startSpeed      = new ParticleSystem.MinMaxCurve(0.3f,  0.7f);
            main.startSize       = new ParticleSystem.MinMaxCurve(0.04f, 0.08f);
            main.startColor      = _stunColor;
            main.gravityModifier = -0.5f; // float upward gently
            main.maxParticles    = 32;

            var em = ps.emission;
            em.rateOverTime = 7f;

            // Circle in the horizontal plane — gives a halo / orbit silhouette.
            var sh = ps.shape;
            sh.enabled          = true;
            sh.shapeType        = ParticleSystemShapeType.Circle;
            sh.radius           = _stunOrbitRadius;
            sh.radiusThickness  = 1f;

            AddFadeOut(ps);
            return ps;
        }

        // ---- Deposit burst ----------------------------------------------------

        private ParticleSystem BuildDepositPS(Material mat)
        {
            var ps   = MakePS("VFX_Deposit", new Vector3(0f, 0.5f, 0f), mat);
            var main = ps.main;
            main.startLifetime   = new ParticleSystem.MinMaxCurve(0.45f, 0.65f);
            main.startSpeed      = new ParticleSystem.MinMaxCurve(2.5f,  4.5f);
            main.startSize       = new ParticleSystem.MinMaxCurve(0.05f, 0.09f);
            main.startColor      = _depositColor;
            main.gravityModifier = -1.5f; // rise upward, linger briefly
            main.maxParticles    = _depositCount * 2;

            var em = ps.emission;
            em.rateOverTime = 0;
            em.SetBursts(new[] { new ParticleSystem.Burst(0f, _depositCount) });

            // Narrow upward cone — gold shower rising from the base.
            var sh = ps.shape;
            sh.enabled   = true;
            sh.shapeType = ParticleSystemShapeType.Cone;
            sh.angle     = 35f;
            sh.radius    = 0.10f;

            AddFadeOut(ps);
            return ps;
        }

        // ---- Ability accent burst ----------------------------------------------------

        /// <summary>
        /// One-shot spherical burst tinted with <see cref="AbilityBaseSO.AccentColor"/>.
        /// The color is overwritten dynamically each time an ability activates, so a
        /// single shared system covers all eight abilities without per-ability instances.
        /// </summary>
        private ParticleSystem BuildAbilityPS(Material mat)
        {
            var ps   = MakePS("VFX_Ability", new Vector3(0f, 1.0f, 0f), mat);
            var main = ps.main;
            main.startLifetime   = new ParticleSystem.MinMaxCurve(0.30f, 0.55f);
            main.startSpeed      = new ParticleSystem.MinMaxCurve(1.5f,  3.5f);
            main.startSize       = new ParticleSystem.MinMaxCurve(0.04f, 0.09f);
            main.startColor      = Color.white; // overwritten at play-time with AccentColor
            main.gravityModifier = -0.8f;       // particles float upward and outward
            main.maxParticles    = 32;

            var em = ps.emission;
            em.rateOverTime = 0;
            em.SetBursts(new[] { new ParticleSystem.Burst(0f, 16) });

            var sh = ps.shape;
            sh.enabled   = true;
            sh.shapeType = ParticleSystemShapeType.Sphere;
            sh.radius    = 0.25f;

            AddFadeOut(ps);
            return ps;
        }

        // ---- Cargo-full ring --------------------------------------------------

        /// <summary>
        /// Looping gold orbit that plays while the chicken is carrying a full load.
        /// Identical in shape to the stun ring but gold-tinted and at waist height,
        /// so players can spot at a glance whether a chicken is full and needs to
        /// return to base.
        /// </summary>
        private ParticleSystem BuildCargoFullPS(Material mat)
        {
            var ps   = MakePS("VFX_CargoFull", new Vector3(0f, 0.65f, 0f), mat, looping: true);
            var main = ps.main;
            main.startLifetime   = new ParticleSystem.MinMaxCurve(0.40f, 0.60f);
            main.startSpeed      = new ParticleSystem.MinMaxCurve(0.30f, 0.65f);
            main.startSize       = new ParticleSystem.MinMaxCurve(0.04f, 0.07f);
            main.startColor      = new Color(1.00f, 0.85f, 0.15f, 1f); // bright gold
            main.gravityModifier = -0.25f; // float very gently upward
            main.maxParticles    = 48;

            var em = ps.emission;
            em.rateOverTime = 12f;

            // Horizontal circle — same silhouette as the stun orbit but lower.
            var sh = ps.shape;
            sh.enabled          = true;
            sh.shapeType        = ParticleSystemShapeType.Circle;
            sh.radius           = 0.38f;
            sh.radiusThickness  = 1f;

            AddFadeOut(ps);
            return ps;
        }
    }
}

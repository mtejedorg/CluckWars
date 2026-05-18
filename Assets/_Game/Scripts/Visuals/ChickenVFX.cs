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

        private ParticleSystem _hitPS;
        private ParticleSystem _deathPS;
        private ParticleSystem _stunPS;
        private ParticleSystem _depositPS;

        private float _lastHp    = float.NaN;
        private float _lastCargo = float.NaN;
        private bool  _wasStunned;

        // ---- Unity lifecycle --------------------------------------------------

        private void Awake()
        {
            _controller = GetComponent<ChickenController>();
            _combat     = GetComponent<ChickenCombat>();
            _cargo      = GetComponent<ChickenCargo>();

            var mat  = BuildMaterial();
            _hitPS     = BuildHitPS(mat);
            _deathPS   = BuildDeathPS(mat);
            _stunPS    = BuildStunPS(mat);
            _depositPS = BuildDepositPS(mat);
        }

        private void LateUpdate()
        {
            if (_combat == null) return;

            float hp        = _combat.HP;
            bool  isStunned = _combat.IsStunned;

            // ── Hit sparks: HP decreased while alive ──────────────────────────
            if (!float.IsNaN(_lastHp) && hp < _lastHp - 0.01f && !isStunned)
                _hitPS?.Play();

            // ── Death → stun begin ───────────────────────────────────────────
            if (isStunned && !_wasStunned)
            {
                _deathPS?.Play();
                _stunPS?.Play();
            }

            // ── Respawn → stun end ───────────────────────────────────────────
            if (!isStunned && _wasStunned)
                _stunPS?.Stop(true, ParticleSystemStopBehavior.StopEmitting);

            _lastHp    = hp;
            _wasStunned = isStunned;

            // ── Food deposit: Cargo drops to near-zero while alive ─────────────
            // [Networked] Cargo replicates to every peer so this fires everywhere.
            if (_cargo != null)
            {
                float cargo = _cargo.Cargo;
                if (!float.IsNaN(_lastCargo) && _lastCargo > 1f && cargo < 0.1f && !isStunned)
                    _depositPS?.Play();
                _lastCargo = cargo;
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
    }
}

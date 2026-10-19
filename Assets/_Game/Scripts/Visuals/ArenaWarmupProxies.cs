using CluckWars.Logging;
using UnityEngine;

namespace CluckWars.Visuals
{
    /// <summary>
    /// One tiny live renderer per lazily-created runtime material, drawn inside the warm-up overview so the shader programs
    /// they need exist before the match asks for them (the first hit spark / cast burst / telegraph line would otherwise pay
    /// the compile or pipeline-state cost mid-match). Created at the start of warm-up step A, destroyed when it ends.
    /// </summary>
    /// <remarks>
    /// Covered: the URP unlit particle material every <see cref="ChickenVFX"/> burst and <see cref="HitFeedback"/> spark uses
    /// (both factories run the same <c>Shader.Find</c> chain, so one proxy warms both), and the Sprites/Default line material of
    /// <see cref="TelegraphShapes"/> (the ability telegraphs, <c>ControlStateVFX</c> rings). Skipped: the TextMesh font material
    /// of <c>FloatingCombatText</c> (the font atlas is a runtime texture, not a program), the ability-zone prefabs (networked,
    /// spawned by the first cast) and <c>RingGhost</c> (needs a ring overlapping a mound).
    /// </remarks>
    public sealed class ArenaWarmupProxies
    {
        private GameObject _root;
        private Material _particleMaterial, _lineMaterial;

        public bool IsAlive => _root != null;

        private const string Source = "ArenaReadiness";

        /// <summary>Builds the proxies at <paramref name="at"/> (arena centre, inside the overview frustum). Replaces any earlier set.
        /// A shader the factory cannot find (stripped from a build) is reported: that effect would compile mid-match instead.</summary>
        public void Create(Vector3 at, ILogService log)
        {
            Destroy();
            _root = new GameObject("ArenaWarmupProxies") { hideFlags = HideFlags.DontSave };
            _root.transform.position = at;

            _particleMaterial = ChickenVFX.BuildMaterial();
            if (_particleMaterial != null)
            {
                var go = new GameObject("ParticleProxy");
                go.transform.SetParent(_root.transform, false);
                var ps = go.AddComponent<ParticleSystem>();
                ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                var main = ps.main;
                main.playOnAwake = false;
                main.loop = false;
                main.duration = 1f;
                main.startLifetime = 60f;
                main.startSpeed = 0f;
                main.startSize = 0.05f;
                main.maxParticles = 1;
                main.simulationSpace = ParticleSystemSimulationSpace.World;
                var emission = ps.emission;
                emission.rateOverTime = 0f;
                emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 1) });
                go.GetComponent<ParticleSystemRenderer>().sharedMaterial = _particleMaterial;
                ps.Play();
            }
            else
            {
                log?.Warn(Source, "The URP unlit particle shader was not found: hit sparks and ability bursts cannot be warmed up " +
                    "(is 'Universal Render Pipeline/Particles/Unlit' stripped from the build?).");
            }

            _lineMaterial = TelegraphShapes.BuildLineMaterial();
            if (_lineMaterial != null)
            {
                var go = new GameObject("LineProxy");
                go.transform.SetParent(_root.transform, false);
                var lr = go.AddComponent<LineRenderer>();
                lr.useWorldSpace = true;
                lr.positionCount = 2;
                lr.SetPosition(0, at);
                lr.SetPosition(1, at + new Vector3(0.3f, 0f, 0f));
                lr.widthMultiplier = 0.05f;
                lr.sharedMaterial = _lineMaterial;
                lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                lr.receiveShadows = false;
            }
            else
            {
                log?.Warn(Source, "The Sprites/Default line shader was not found: ability telegraphs cannot be warmed up.");
            }
        }

        /// <summary>Removes the proxies and the materials they made. Safe when nothing was created.</summary>
        public void Destroy()
        {
            if (_root != null) Object.Destroy(_root);
            if (_particleMaterial != null) Object.Destroy(_particleMaterial);
            if (_lineMaterial != null) Object.Destroy(_lineMaterial);
            _root = null; _particleMaterial = null; _lineMaterial = null;
        }
    }
}

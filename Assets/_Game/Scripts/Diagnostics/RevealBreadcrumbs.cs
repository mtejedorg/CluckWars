using System;
using CluckWars.Logging;
using UnityEngine;
using UnityEngine.SceneManagement;
using Zenject;

namespace CluckWars.Diagnostics
{
    /// <summary>
    /// Always-on breadcrumb plumbing (test-session item 5): on every scene load it resets the per-scene reveal state and
    /// the first-use marks and records when the load finished (PerfProbe's <c>phase=loading</c>); every frame it drains
    /// <see cref="FirstUseMarkers"/> into <c>Reveal first-use &lt;name&gt;</c> Info lines. Bound non-lazy in
    /// <c>ProjectInstaller</c>. Read-only toward gameplay.
    /// </summary>
    public sealed class RevealBreadcrumbs : IInitializable, ITickable, IDisposable
    {
        private const string Source = "Reveal";
        private readonly ILogService _log;

        public RevealBreadcrumbs(ILogService log) { _log = log; }

        public void Initialize() => SceneManager.sceneLoaded += OnSceneLoaded;

        public void Dispose() => SceneManager.sceneLoaded -= OnSceneLoaded;

        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (mode != LoadSceneMode.Single) return;
            FirstUseMarkers.ResetForScene();
            RevealState.ResetForScene();
            RevealState.SceneLoadInFlight = false;
            RevealState.LastSceneLoadedRealtime = Time.realtimeSinceStartup;
        }

        public void Tick()
        {
            while (FirstUseMarkers.TryTake(out var kind, out int frame))
                _log.Info(Source, $"first-use {FirstUseMarkers.Name(kind)} frame={frame} scene={SceneManager.GetActiveScene().name} {RevealState.Describe()}");
        }
    }
}

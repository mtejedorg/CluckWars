using CluckWars.Logging;
using CluckWars.UI;
using UnityEngine;
using UnityEngine.SceneManagement;
using Zenject;

namespace CluckWars.Diagnostics
{
    /// <summary>
    /// Puts the <see cref="DebugHud"/> (F1 / five corner taps) into every loaded Game scene of a dev-tools build. The
    /// overlay is only placed in AbilityLab.unity; Game.unity has none, and the test session needs it on the phone in a
    /// match. The HUD is created in the scene (so it dies with it) after the scene's SceneContext has installed, which
    /// its scene-first self-injection needs (INetworkService is scene-scoped). Dev tools only; read-only.
    /// </summary>
    public sealed class DebugHudSpawner : MonoBehaviour
    {
        private const string Source = "Diagnostics";
        private const string GameSceneName = "Game";

        private ILogService _log;

        [Inject]
        public void Construct(ILogService log) => _log = log;

        private void Start()
        {
            if (_log == null) ProjectContext.Instance.Container.Inject(this);
            DiagnosticsObjects.KeepAcrossScenes(gameObject);
            SceneManager.sceneLoaded += OnSceneLoaded;
            // The Editor can enter Play Mode straight into Game.unity.
            var active = SceneManager.GetActiveScene();
            if (active.isLoaded) OnSceneLoaded(active, LoadSceneMode.Single);
        }

        private void OnDestroy() => SceneManager.sceneLoaded -= OnSceneLoaded;

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (scene.name != GameSceneName || FindFirstObjectByType<DebugHud>() != null) return;
            var go = new GameObject("DebugHud (diagnostics)");
            SceneManager.MoveGameObjectToScene(go, scene);
            go.AddComponent<DebugHud>();
            _log?.Debug(Source, "DebugHud added to the Game scene (F1, or five taps in the top-left corner with Developer Mode on).");
        }
    }
}

using CluckWars.Logging;
using CluckWars.Networking;
using CluckWars.Services;
using Fusion;
using UnityEngine;
using Zenject;

namespace CluckWars.Gameplay
{
    /// <summary>
    /// Phase-2 entry point for a match. Lives in the Game scene as a single GameObject
    /// with a serialized chicken prefab. On Start it kicks off a solo Fusion session,
    /// then spawns the local player's chicken — using the class chosen via
    /// <see cref="ISessionSelectionService"/> in the Bootstrap menu — when
    /// <c>OnPlayerJoined</c> fires.
    /// </summary>
    /// <remarks>
    /// In Phase 5+ this will also read the desired <c>GameMode</c> (Solo / Host / Join)
    /// from the menu instead of always going to Single.
    /// </remarks>
    public sealed class MatchBootstrapper : MonoBehaviour
    {
        private const string Source = "MatchBootstrap";

        [SerializeField] private NetworkObject _chickenPrefab;
        [SerializeField] private Transform[] _spawnPoints;

        private INetworkService _networkService;
        private ISessionSelectionService _selection;
        private ILogService _log;

        [Inject]
        public void Construct(INetworkService networkService, ISessionSelectionService selection, ILogService log)
        {
            _networkService = networkService;
            _selection = selection;
            _log = log;
        }

        private async void Start()
        {
            if (_chickenPrefab == null)
            {
                _log?.Error(Source, "Chicken prefab not assigned on MatchBootstrapper.");
                return;
            }

            _log?.Info(Source, $"Starting solo session. Selected class = {(_selection != null ? _selection.SelectedClass.ToString() : "(no selection service)")}.");
            _networkService.OnPlayerJoined += HandlePlayerJoined;
            await _networkService.StartSoloAsync();
            _log?.Debug(Source, "StartSoloAsync awaited; runner is up.");
        }

        private void OnDestroy()
        {
            if (_networkService != null)
                _networkService.OnPlayerJoined -= HandlePlayerJoined;
        }

        private void HandlePlayerJoined(NetworkRunner runner, PlayerRef player)
        {
            // Single mode: only the local player exists, so we always spawn.
            // Shared mode: each client spawns its own chicken (state authority follows).
            var isLocal = runner.GameMode == GameMode.Single || player == runner.LocalPlayer;
            _log?.Debug(Source, $"OnPlayerJoined player={player} isLocal={isLocal} mode={runner.GameMode}.");
            if (!isLocal) return;

            var pos = PickSpawnPosition();
            var chosenClass = _selection != null ? _selection.SelectedClass : ChickenClass.Warrior;
            _log?.Info(Source, $"Spawning chicken: class={chosenClass}, pos={pos}.");

            runner.Spawn(
                _chickenPrefab,
                pos,
                Quaternion.identity,
                player,
                onBeforeSpawned: (_, networkObject) =>
                {
                    // Set the Networked Class before Spawned() runs so every peer sees the
                    // chosen class on first read and can resolve stats / tint correctly.
                    var controller = networkObject.GetComponent<ChickenController>();
                    if (controller != null) controller.Class = chosenClass;
                });
        }

        private Vector3 PickSpawnPosition()
        {
            if (_spawnPoints == null || _spawnPoints.Length == 0)
                return Vector3.zero;

            var idx = Random.Range(0, _spawnPoints.Length);
            return _spawnPoints[idx] != null ? _spawnPoints[idx].position : Vector3.zero;
        }
    }
}

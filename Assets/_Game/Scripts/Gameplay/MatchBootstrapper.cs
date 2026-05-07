using CluckWars.Logging;
using CluckWars.Networking;
using CluckWars.Services;
using Fusion;
using UnityEngine;
using Zenject;

namespace CluckWars.Gameplay
{
    /// <summary>
    /// Entry point for a match. Lives in the Game scene as a single GameObject with a
    /// serialized chicken prefab. On Start it reads the user's mode + class from
    /// <see cref="ISessionSelectionService"/>, kicks off the matching Fusion session
    /// (Solo / Host / Join), and spawns the local player's chicken when
    /// <c>OnPlayerJoined</c> fires.
    /// </summary>
    /// <remarks>
    /// In Shared Mode, every joined player's <c>OnPlayerJoined</c> fires on every
    /// peer; we filter on <c>player == runner.LocalPlayer</c> so each client only
    /// spawns its own chicken. Remote chickens replicate automatically.
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

            var mode = _selection != null ? _selection.Mode : SessionMode.Solo;
            var sessionName = _selection != null ? _selection.SessionName : "cluck-lan";
            var chosenClass = _selection != null ? _selection.SelectedClass : ChickenClass.Warrior;

            _log?.Info(Source, $"Starting session: mode={mode}, session='{sessionName}', class={chosenClass}.");
            _networkService.OnPlayerJoined += HandlePlayerJoined;

            switch (mode)
            {
                case SessionMode.Host:
                    await _networkService.StartHostAsync(sessionName);
                    break;
                case SessionMode.Join:
                    await _networkService.JoinSessionAsync(sessionName);
                    break;
                case SessionMode.Solo:
                default:
                    await _networkService.StartSoloAsync();
                    break;
            }

            _log?.Debug(Source, $"Network start awaited (mode={mode}); runner is up.");
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

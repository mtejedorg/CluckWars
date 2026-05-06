using CluckWars.Networking;
using Fusion;
using UnityEngine;
using Zenject;

namespace CluckWars.Gameplay
{
    /// <summary>
    /// Phase-1 entry point for a match. Lives in the Game scene as a single GameObject
    /// with a serialized chicken prefab. On Start it kicks off a solo Fusion session,
    /// then spawns the local player's chicken when <c>OnPlayerJoined</c> fires.
    /// </summary>
    /// <remarks>
    /// In Phase 5+ this will read the desired <c>GameMode</c> (Solo / Host / Join)
    /// from a main-menu flow instead of always going to Single.
    /// </remarks>
    public sealed class MatchBootstrapper : MonoBehaviour
    {
        [SerializeField] private NetworkObject _chickenPrefab;
        [SerializeField] private Transform[] _spawnPoints;

        private INetworkService _networkService;

        [Inject]
        public void Construct(INetworkService networkService)
        {
            _networkService = networkService;
        }

        private async void Start()
        {
            if (_chickenPrefab == null)
            {
                Debug.LogError("[MatchBootstrapper] Chicken prefab not assigned.");
                return;
            }

            _networkService.OnPlayerJoined += HandlePlayerJoined;
            await _networkService.StartSoloAsync();
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
            if (!isLocal) return;

            var pos = PickSpawnPosition();
            runner.Spawn(_chickenPrefab, pos, Quaternion.identity, player);
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

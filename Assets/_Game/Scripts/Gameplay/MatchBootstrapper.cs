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
    ///
    /// Spawn positions come from <see cref="MapGenerator.SpawnPoints"/> when the
    /// scene has a <c>MapGenerator</c>; <see cref="_legacySpawnPoints"/> is a
    /// fallback for scenes without one. Players are pinned to corners by
    /// <c>PlayerId % count</c> so peers agree on positions without coordination.
    /// </remarks>
    public sealed class MatchBootstrapper : MonoBehaviour
    {
        private const string Source = "MatchBootstrap";

        [Tooltip("Legacy per-component override. If null, PrefabRegistry.Chicken is used.")]
        [SerializeField] private NetworkObject _chickenPrefab;

        [Tooltip("Optional fallback spawn points. Ignored when a MapGenerator is present in the scene — that becomes the source of truth.")]
        [SerializeField] private Transform[] _legacySpawnPoints;

        [Tooltip("Legacy per-component override. If null, PrefabRegistry.GameManager is used.")]
        [SerializeField] private NetworkObject _gameManagerPrefab;

        private MapGenerator _mapGenerator;

        private INetworkService _networkService;
        private ISessionSelectionService _selection;
        private PrefabRegistrySO _prefabRegistry;
        private ILogService _log;

        [Inject]
        public void Construct(INetworkService networkService, ISessionSelectionService selection, PrefabRegistrySO prefabRegistry, ILogService log)
        {
            _networkService = networkService;
            _selection = selection;
            _prefabRegistry = prefabRegistry;
            _log = log;
        }

        private NetworkObject ResolveChickenPrefab()
        {
            if (_prefabRegistry != null && _prefabRegistry.Chicken != null) return _prefabRegistry.Chicken;
            return _chickenPrefab;
        }

        private NetworkObject ResolveGameManagerPrefab()
        {
            if (_prefabRegistry != null && _prefabRegistry.GameManager != null) return _prefabRegistry.GameManager;
            return _gameManagerPrefab;
        }

        private async void Start()
        {
            if (ResolveChickenPrefab() == null)
            {
                _log?.Error(Source, "Chicken prefab not assigned (neither PrefabRegistry.Chicken nor legacy slot).");
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

            TrySpawnGameManager();
        }

        private void TrySpawnGameManager()
        {
            var gmPrefab = ResolveGameManagerPrefab();
            if (gmPrefab == null)
            {
                _log?.Warn(Source, "GameManager prefab not assigned — match has no timer / win condition.");
                return;
            }

            var runner = _networkService.Runner;
            if (runner == null) return;

            // Only the master client (or solo player) spawns the manager. In Shared
            // Mode, every client's local-player check passes for itself; the master
            // client is the one whose LocalPlayer equals runner.LocalPlayer AND is
            // first to spawn. We additionally guard so we don't end up with two
            // managers if joins race.
            if (runner.GameMode != GameMode.Single && !runner.IsSharedModeMasterClient)
            {
                _log?.Debug(Source, "Not master client; GameManager will replicate from the master.");
                return;
            }
            if (FindFirstObjectByType<GameManager>() != null)
            {
                _log?.Debug(Source, "GameManager already in scene; skipping spawn.");
                return;
            }

            _log?.Info(Source, "Master client spawning GameManager.");
            runner.Spawn(gmPrefab, Vector3.zero, Quaternion.identity);
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

            var pos = PickSpawnPosition(player);

            // Defensive: nudge spawn slightly up + per-player horizontally so that
            // even if PickSpawnPosition collapses to the same XZ (degenerate
            // SpawnPoints, all-zero fallback, …), two chickens don't spawn
            // perfectly overlapping. CharacterController vs CharacterController
            // collision otherwise leaves the losing chicken drifting in one
            // direction indefinitely — exactly the "non-hosts can't steer" bug.
            var safetyJitter = new Vector3(
                Mathf.Sin(player.PlayerId * 1.7f) * 0.25f,
                0.05f * (1 + Mathf.Abs(player.PlayerId)),  // tiny per-player vertical stagger
                Mathf.Cos(player.PlayerId * 1.7f) * 0.25f);
            pos += safetyJitter;

            var chosenClass = _selection != null ? _selection.SelectedClass : ChickenClass.Warrior;
            _log?.Info(Source,
                $"Spawning chicken: class={chosenClass}, " +
                $"player={player} (PlayerId={player.PlayerId}), " +
                $"pos={pos}, mapGen={(_mapGenerator != null ? "present" : "missing")}, " +
                $"spawnPoints={(_mapGenerator != null && _mapGenerator.SpawnPoints != null ? _mapGenerator.SpawnPoints.Count : 0)}.");

            runner.Spawn(
                ResolveChickenPrefab(),
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

        private Vector3 PickSpawnPosition(PlayerRef player)
        {
            // Prefer MapGenerator's computed corners. Lookup is cached; null check
            // re-resolves if the MapGenerator was added after Start ran.
            if (_mapGenerator == null) _mapGenerator = FindFirstObjectByType<MapGenerator>();
            if (_mapGenerator != null && _mapGenerator.SpawnPoints != null && _mapGenerator.SpawnPoints.Count > 0)
            {
                var points = _mapGenerator.SpawnPoints;
                // PlayerId-modulo so each peer deterministically picks the same
                // corner for a given player. PlayerId is non-negative for real
                // players, but the modulo guards against the solo-mode default
                // (PlayerId 0) just in case.
                int idx = Mathf.Abs(player.PlayerId) % points.Count;
                return points[idx];
            }

            // Legacy fallback for scenes without a MapGenerator.
            if (_legacySpawnPoints == null || _legacySpawnPoints.Length == 0)
                return Vector3.zero;

            int legacyIdx = Mathf.Abs(player.PlayerId) % _legacySpawnPoints.Length;
            return _legacySpawnPoints[legacyIdx] != null
                ? _legacySpawnPoints[legacyIdx].position
                : Vector3.zero;
        }
    }
}

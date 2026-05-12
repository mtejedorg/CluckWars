using System.Collections.Generic;
using CluckWars.Logging;
using CluckWars.Networking;
using Fusion;
using UnityEngine;
using Zenject;

namespace CluckWars.Gameplay
{
    /// <summary>
    /// Procedural map builder. Replaces hand-placed scene clutter with a single
    /// component drop. Two responsibilities, split by network role:
    ///
    /// <list type="bullet">
    ///   <item>Local on every peer: build the ground plane primitive + cache the
    ///         four corner spawn positions for <c>MatchBootstrapper</c>.</item>
    ///   <item>Master client only (after <c>OnRunnerReady</c>): <c>Runner.Spawn</c>
    ///         four <see cref="PlayerBase"/> instances at the corners, one bigger
    ///         <see cref="FoodPile"/> at the center, and a configurable number of
    ///         small piles around it. Bases / piles replicate to every peer via
    ///         normal Fusion state sync.</item>
    /// </list>
    /// </summary>
    /// <remarks>
    /// Programmer-art quality: the plane is a Unity primitive, piles inherit the
    /// existing prefab look. Phase 9 polish swaps in proper map art and randomizes
    /// pile placement with avoidance constraints.
    /// </remarks>
    public sealed class MapGenerator : MonoBehaviour
    {
        private const string Source = "MapGen";

        [Header("Prefabs (master spawns these via Runner.Spawn)")]
        [Tooltip("Legacy per-component override. If null, PrefabRegistry.PlayerBase is used.")]
        [SerializeField] private NetworkObject _basePrefab;
        [Tooltip("Legacy per-component override. If null, PrefabRegistry.FoodPile is used.")]
        [SerializeField] private NetworkObject _foodPilePrefab;

        [Header("Ground plane (local visual on every peer)")]
        [SerializeField] private Material _groundMaterial;
        [Tooltip("World-space side length of the square ground plane.")]
        [Min(5f)]
        [SerializeField] private float _planeSize = 30f;

        [Header("Boundary walls (local colliders on every peer)")]
        [Tooltip("Height of the perimeter walls. Way taller than the chicken to keep them in even at 2× Speed Burst.")]
        [Min(1f)]
        [SerializeField] private float _wallHeight = 5f;
        [Tooltip("Wall thickness. Thin walls + fast chickens can tunnel; 0.5m is safe.")]
        [Min(0.05f)]
        [SerializeField] private float _wallThickness = 0.5f;
        [Tooltip("Render the walls. Off by default (invisible boundary); flip on for debugging.")]
        [SerializeField] private bool _wallsVisible = false;
        [Tooltip("Optional material for the walls when _wallsVisible is true.")]
        [SerializeField] private Material _wallMaterial;

        [Header("Bases")]
        [Tooltip("How far each corner base sits from the center.")]
        [Min(2f)]
        [SerializeField] private float _baseCornerDistance = 12f;

        [Header("Food piles")]
        [Tooltip("Initial food in the central showcase pile (≈ 2× a small pile).")]
        [Min(5f)]
        [SerializeField] private float _centerPileAmount = 80f;

        [Tooltip("Center pile mesh scale multiplier — makes it visually bigger without changing gameplay rules beyond the food count.")]
        [Min(0.5f)]
        [SerializeField] private float _centerPileVisualScale = 1.5f;

        [Tooltip("Initial food in each small satellite pile (uses prefab default if 0).")]
        [Min(0f)]
        [SerializeField] private float _smallPileAmount = 30f;

        [Tooltip("How many small piles to scatter around the center.")]
        [Min(0)]
        [SerializeField] private int _smallPileCount = 4;

        [Tooltip("Inner / outer radius of the small-pile ring around the center.")]
        [Min(1f)]
        [SerializeField] private float _smallPileMinRadius = 4f;
        [Min(1f)]
        [SerializeField] private float _smallPileMaxRadius = 8f;

        private INetworkService _network;
        private PrefabRegistrySO _prefabRegistry;
        private ILogService _log;
        private Vector3[] _spawnPoints;
        private bool _runnerHandled;

        /// <summary>Four corner spawn positions, computed at <c>Awake</c>. Read by <c>MatchBootstrapper</c> if it wants per-base spawning.</summary>
        public IReadOnlyList<Vector3> SpawnPoints => _spawnPoints;

        [Inject]
        public void Construct(INetworkService network, PrefabRegistrySO prefabRegistry, ILogService log)
        {
            _network = network;
            _prefabRegistry = prefabRegistry;
            _log = log;
        }

        private NetworkObject ResolveBasePrefab()
        {
            if (_prefabRegistry != null && _prefabRegistry.PlayerBase != null) return _prefabRegistry.PlayerBase;
            return _basePrefab;
        }

        private NetworkObject ResolveFoodPilePrefab()
        {
            if (_prefabRegistry != null && _prefabRegistry.FoodPile != null) return _prefabRegistry.FoodPile;
            return _foodPilePrefab;
        }

        private void Awake()
        {
            ComputeSpawnPoints();
            BuildPlane();
            BuildBoundaryWalls();

            if (_network != null) _network.OnRunnerReady += HandleRunnerReady;
            else _log?.Warn(Source, "INetworkService not injected; bases/piles won't spawn.");
        }

        private void OnDestroy()
        {
            if (_network != null) _network.OnRunnerReady -= HandleRunnerReady;
        }

        // ---- Local plane + spawn points ---------------------------------------

        private void ComputeSpawnPoints()
        {
            float d = _baseCornerDistance;
            if (d < 1f)
            {
                // The "all spawns at origin" footgun — a zero / tiny corner
                // distance collapses every spawn point onto the same XZ and
                // stacks chickens, which then push each other into permanent
                // drift via CharacterController collision. Force a sane minimum
                // and shout about it.
                _log?.Warn(Source,
                    $"_baseCornerDistance={_baseCornerDistance} is too small. " +
                    "All spawn corners would collapse onto the origin and chickens would stack. " +
                    "Snapping to 12. Set a non-trivial value (≥ 4) in the inspector.");
                d = 12f;
            }
            _spawnPoints = new[]
            {
                new Vector3(+d, 0f, +d),
                new Vector3(-d, 0f, +d),
                new Vector3(-d, 0f, -d),
                new Vector3(+d, 0f, -d),
            };
        }

        private void BuildPlane()
        {
            // Unity's Plane primitive is 10 m square and faces up — scale to taste.
            var plane = GameObject.CreatePrimitive(PrimitiveType.Plane);
            plane.name = "GeneratedGround";
            plane.transform.SetParent(transform, worldPositionStays: false);
            plane.transform.localScale = Vector3.one * (_planeSize / 10f);

            if (_groundMaterial != null)
            {
                plane.GetComponent<Renderer>().sharedMaterial = _groundMaterial;
            }
        }

        private void BuildBoundaryWalls()
        {
            float half = _planeSize * 0.5f;
            float t = _wallThickness;
            float h = _wallHeight;

            // Each wall sits flush against an edge of the plane, extending out by
            // half its thickness so its inner face aligns with the plane edge.
            //                 +Z
            //         ┌────────────┐
            //         │   North    │
            //   ┌─────┴─────────────┴─────┐
            //   │                          │
            // West                       East
            //   │                          │
            //   └─────┬─────────────┬─────┘
            //         │   South    │
            //         └────────────┘  -Z
            CreateWall("Wall_North", new Vector3(0f, h * 0.5f, +half + t * 0.5f),
                new Vector3(_planeSize + 2f * t, h, t));
            CreateWall("Wall_South", new Vector3(0f, h * 0.5f, -half - t * 0.5f),
                new Vector3(_planeSize + 2f * t, h, t));
            CreateWall("Wall_East", new Vector3(+half + t * 0.5f, h * 0.5f, 0f),
                new Vector3(t, h, _planeSize));
            CreateWall("Wall_West", new Vector3(-half - t * 0.5f, h * 0.5f, 0f),
                new Vector3(t, h, _planeSize));
        }

        private void CreateWall(string name, Vector3 localPosition, Vector3 size)
        {
            var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wall.name = name;
            wall.transform.SetParent(transform, worldPositionStays: false);
            wall.transform.localPosition = localPosition;
            wall.transform.localScale = size;

            if (!_wallsVisible)
            {
                // Drop the renderer so the wall is invisible. Keep the BoxCollider
                // — that's what blocks the chicken's CharacterController.
                var mr = wall.GetComponent<MeshRenderer>();
                if (mr != null) mr.enabled = false;
            }
            else if (_wallMaterial != null)
            {
                var mr = wall.GetComponent<MeshRenderer>();
                if (mr != null) mr.sharedMaterial = _wallMaterial;
            }
        }

        // ---- Networked spawning (master only) ---------------------------------

        private void HandleRunnerReady(NetworkRunner runner)
        {
            if (_runnerHandled) return;
            _runnerHandled = true;

            // Solo runners are their own master. Shared mode: only the master client
            // spawns the bases / piles; every other peer picks them up via replication.
            bool isMaster = runner.GameMode == GameMode.Single || runner.IsSharedModeMasterClient;
            if (!isMaster)
            {
                _log?.Debug(Source, "Not master; bases / piles will replicate from the master.");
                return;
            }

            SpawnBases(runner);
            SpawnFoodPiles(runner);
        }

        private void SpawnBases(NetworkRunner runner)
        {
            var basePrefab = ResolveBasePrefab();
            if (basePrefab == null)
            {
                _log?.Warn(Source, "Base prefab not assigned (neither PrefabRegistry.PlayerBase nor legacy slot) — skipping base spawn.");
                return;
            }

            for (int i = 0; i < _spawnPoints.Length; i++)
            {
                // Place the base slightly inside the corner so the chicken can wrap
                // around it without bouncing the camera off the playfield edge.
                var pos = Vector3.Lerp(_spawnPoints[i], Vector3.zero, 0.15f);
                int cornerIndex = i;
                runner.Spawn(
                    basePrefab,
                    pos,
                    Quaternion.identity,
                    onBeforeSpawned: (_, networkObject) =>
                    {
                        // Tag with corner index so GameManager pairs the base with the
                        // player who spawned at the matching corner. SpawnPoints[i]
                        // and CornerIndex i agree by construction.
                        var pb = networkObject.GetComponent<PlayerBase>();
                        if (pb != null) pb.CornerIndex = cornerIndex;
                    });
            }
            _log?.Info(Source, $"Spawned {_spawnPoints.Length} corner bases.");
        }

        private void SpawnFoodPiles(NetworkRunner runner)
        {
            var pilePrefab = ResolveFoodPilePrefab();
            if (pilePrefab == null)
            {
                _log?.Warn(Source, "FoodPile prefab not assigned (neither PrefabRegistry.FoodPile nor legacy slot) — skipping pile spawn.");
                return;
            }

            // Center pile — bigger Amount + bigger visual.
            var centerObj = runner.Spawn(
                pilePrefab,
                Vector3.zero,
                Quaternion.identity,
                onBeforeSpawned: (_, networkObject) =>
                {
                    var pile = networkObject.GetComponent<FoodPile>();
                    if (pile != null)
                    {
                        pile.Amount = _centerPileAmount;
                        pile.MaxAmount = _centerPileAmount;
                    }
                });
            if (centerObj != null)
            {
                centerObj.transform.localScale = Vector3.one * _centerPileVisualScale;
            }

            // Small piles — evenly spaced around the ring with a jittered radius.
            for (int i = 0; i < _smallPileCount; i++)
            {
                float angle = (i / (float)_smallPileCount) * Mathf.PI * 2f;
                float radius = Random.Range(_smallPileMinRadius, _smallPileMaxRadius);
                var pos = new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);

                runner.Spawn(
                    pilePrefab,
                    pos,
                    Quaternion.identity,
                    onBeforeSpawned: (_, networkObject) =>
                    {
                        if (_smallPileAmount <= 0f) return; // let prefab default seed
                        var pile = networkObject.GetComponent<FoodPile>();
                        if (pile != null)
                        {
                            pile.Amount = _smallPileAmount;
                            pile.MaxAmount = _smallPileAmount;
                        }
                    });
            }
            _log?.Info(Source, $"Spawned center pile + {_smallPileCount} small piles.");
        }
    }
}

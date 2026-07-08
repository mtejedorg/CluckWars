using System.Collections.Generic;
using CluckWars.Logging;
using CluckWars.Networking;
using CluckWars.Services;
using Fusion;
using Unity.AI.Navigation;
using UnityEngine;
using UnityEngine.AI;
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

        [Header("Interior walls (visible cover — chases route around them)")]
        [Tooltip("Number of interior wall segments. 0 disables. Walls are LOCAL geometry that blocks movement, so online layouts are seeded from the session name — every peer builds the identical map.")]
        [Range(0, 12)]
        [SerializeField] private int _interiorWallCount = 6;
        [Tooltip("Min (x) / max (y) length of an interior wall segment.")]
        [SerializeField] private Vector2 _interiorWallLengthRange = new Vector2(3f, 6f);
        [Tooltip("Interior wall height. Low enough to see over in the iso view; must stay above the NavMesh step height (0.75) so bots can't path over.")]
        [Min(0.8f)]
        [SerializeField] private float _interiorWallHeight = 1.1f;
        [Tooltip("Keep-clear radius around the center pile, corner bases and nominal island positions so walls never seal off an objective.")]
        [Min(1f)]
        [SerializeField] private float _interiorWallClearance = 3f;
        [Tooltip("Tint for interior walls when no _wallMaterial is assigned. Desaturated per ART.md — map stays muted so chickens pop.")]
        [SerializeField] private Color _interiorWallColor = new Color(0.45f, 0.36f, 0.26f, 1f);

        [Header("Bases")]
        [Tooltip("How far each corner base sits from the center.")]
        [Min(2f)]
        [SerializeField] private float _baseCornerDistance = 12f;

        [Header("Food piles (GDD §3: center + personal + contested islands)")]
        [Tooltip("Initial food in the central pile — large, high risk, high reward (GDD §3: 60).")]
        [Min(5f)]
        [SerializeField] private float _centerPileAmount = 60f;

        [Tooltip("Center pile mesh scale multiplier — makes it visually bigger without changing gameplay rules beyond the food count.")]
        [Min(0.5f)]
        [SerializeField] private float _centerPileVisualScale = 1.5f;

        [Tooltip("Food in each player's personal island — small, relatively safe early game (GDD §3: 15).")]
        [Min(0f)]
        [SerializeField] private float _personalPileAmount = 15f;

        [Tooltip("Personal island position = Lerp(corner, center, inset). 0.35 puts it a few units in front of the base, toward the action.")]
        [Range(0.2f, 0.6f)]
        [SerializeField] private float _personalPileInset = 0.35f;

        [Tooltip("Food in each contested island between adjacent player pairs — designed to provoke early fights (GDD §3: 25).")]
        [Min(0f)]
        [SerializeField] private float _contestedPileAmount = 25f;

        [Tooltip("Contested island position = edge midpoint scaled toward center. 0.8 keeps it between the two neighbours but inside the walls.")]
        [Range(0.4f, 1f)]
        [SerializeField] private float _contestedEdgeInset = 0.8f;

        [Tooltip("Random XZ jitter applied to personal + contested island positions each match (GDD §3: positions randomized within constraints). Center pile never moves.")]
        [Min(0f)]
        [SerializeField] private float _pilePositionJitter = 1.5f;

        private INetworkService _network;
        private PrefabRegistrySO _prefabRegistry;
        private ISessionSelectionService _selection;
        private ILogService _log;
        private Vector3[] _spawnPoints;
        private bool _runnerHandled;

        /// <summary>Four corner spawn positions, computed at <c>Awake</c>. Read by <c>MatchBootstrapper</c> if it wants per-base spawning.</summary>
        public IReadOnlyList<Vector3> SpawnPoints => _spawnPoints;

        [Inject]
        public void Construct(INetworkService network, PrefabRegistrySO prefabRegistry, ISessionSelectionService selection, ILogService log)
        {
            _network = network;
            _prefabRegistry = prefabRegistry;
            _selection = selection;
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
            BuildInteriorWalls();
            BuildNavMesh();

            if (_network != null) _network.OnRunnerReady += HandleRunnerReady;
            else _log?.Warn(Source, "INetworkService not injected; bases/piles won't spawn.");
        }

        private void OnDestroy()
        {
            if (_network != null) _network.OnRunnerReady -= HandleRunnerReady;
        }

        // ---- Local plane + spawn points ---------------------------------------

        private const float BaseInsetFraction = 0.15f;

        private Vector3[] _corners; // raw corner positions; bases + spawn points both derive from these.

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

            _corners = new[]
            {
                new Vector3(+d, 0f, +d),
                new Vector3(-d, 0f, +d),
                new Vector3(-d, 0f, -d),
                new Vector3(+d, 0f, -d),
            };

            // Spawn points coincide with where SpawnBases will drop each base —
            // chickens spawn AT their corner's base (inside its deposit trigger
            // since the base collider is a trigger anyway). This pairs spawn
            // index N with the base whose CornerIndex == N by construction.
            _spawnPoints = new Vector3[_corners.Length];
            for (int i = 0; i < _corners.Length; i++)
            {
                _spawnPoints[i] = Vector3.Lerp(_corners[i], Vector3.zero, BaseInsetFraction);
            }
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

        // ---- Interior walls + NavMesh ------------------------------------------

        /// <summary>
        /// Places visible interior wall segments that block movement — chases
        /// become routing plays instead of pure speed races. Walls are LOCAL
        /// geometry on every peer, so online layouts are seeded from the session
        /// name (same determinism trick as MatchBootstrapper's corner
        /// permutation); solo just rolls a fresh layout each match.
        /// Rejection sampling keeps every wall clear of the center pile, the
        /// corner bases, and the nominal island positions so no objective is
        /// ever sealed off.
        /// </summary>
        private void BuildInteriorWalls()
        {
            if (_interiorWallCount <= 0) return;

            bool online = _selection != null && _selection.Mode != SessionMode.Solo;
            string sessionName = online ? _selection.SessionName : null;
            var rng = online
                ? new System.Random(SessionNameSeed(sessionName))
                : new System.Random();

            // Keep-clear points: center pile, corner bases, nominal (pre-jitter)
            // island positions. Pile jitter is ±1.5 and _interiorWallClearance
            // covers it.
            var keepClear = new List<Vector3> { Vector3.zero };
            for (int i = 0; i < _corners.Length; i++)
            {
                keepClear.Add(_spawnPoints[i]);
                keepClear.Add(Vector3.Lerp(_corners[i], Vector3.zero, _personalPileInset));
                keepClear.Add((_corners[i] + _corners[(i + 1) % _corners.Length]) * 0.5f * _contestedEdgeInset);
            }

            float minR = _interiorWallClearance + 1.5f;   // outside the center pile's clearance
            float maxR = _planeSize * 0.5f - 2f;          // inside the boundary walls
            int placed = 0;
            for (int attempt = 0; attempt < _interiorWallCount * 12 && placed < _interiorWallCount; attempt++)
            {
                float len   = Mathf.Lerp(_interiorWallLengthRange.x, _interiorWallLengthRange.y, (float)rng.NextDouble());
                float polar = (float)rng.NextDouble() * Mathf.PI * 2f;
                float r     = Mathf.Lerp(minR, maxR, (float)rng.NextDouble());
                var center  = new Vector3(Mathf.Cos(polar) * r, 0f, Mathf.Sin(polar) * r);
                float yaw   = (float)rng.NextDouble() * 180f;

                // Conservative clearance: point-to-wall-center distance must beat
                // the keep-clear radius plus the wall's half length.
                float keepOut = _interiorWallClearance + len * 0.5f;
                bool blocked = false;
                for (int p = 0; p < keepClear.Count && !blocked; p++)
                    if ((keepClear[p] - center).sqrMagnitude < keepOut * keepOut) blocked = true;
                if (blocked) continue;

                var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
                wall.name = $"InteriorWall_{placed}";
                wall.transform.SetParent(transform, worldPositionStays: false);
                wall.transform.localPosition = center + Vector3.up * (_interiorWallHeight * 0.5f);
                wall.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
                wall.transform.localScale = new Vector3(len, _interiorWallHeight, _wallThickness);
                var mr = wall.GetComponent<MeshRenderer>();
                if (mr != null)
                {
                    if (_wallMaterial != null) mr.sharedMaterial = _wallMaterial;
                    mr.material.color = _interiorWallColor; // per-wall instance; ≤12 walls
                }

                keepClear.Add(center); // walls also keep clear of each other
                placed++;
            }

            _log?.Info(Source, $"Built {placed}/{_interiorWallCount} interior walls " +
                $"(seed={(online ? $"session '{sessionName}'" : "solo-random")}).");
        }

        /// <summary>
        /// Bakes a runtime NavMesh from the generated geometry (physics colliders,
        /// so the invisible boundary walls count too). Bots path around interior
        /// walls via this mesh; FoodPiles spawn later and carve dynamically with
        /// a NavMeshObstacle on their blocker.
        /// </summary>
        private void BuildNavMesh()
        {
            var surface = gameObject.AddComponent<NavMeshSurface>();
            surface.collectObjects = CollectObjects.Children;
            surface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
            surface.BuildNavMesh();
            _log?.Info(Source, "NavMesh baked for bot pathing.");
        }

        /// <summary>
        /// Stable session-name hash — identical on every peer/platform. Kept in
        /// sync with <c>MatchBootstrapper.SessionNameSeed</c> (same polynomial);
        /// duplicated because both classes need it before any shared home exists.
        /// </summary>
        private static int SessionNameSeed(string s)
        {
            unchecked
            {
                int h = 17;
                foreach (char c in s) h = h * 31 + c;
                return h;
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
                // Bases sit at the spawn-point position by construction (see
                // ComputeSpawnPoints) — keeps chicken-spawn and base location
                // perfectly aligned so spawn corner N == base CornerIndex N.
                var pos = _spawnPoints[i];
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

            // GDD §3 layout — three strategic options per player:
            //   Secure:  personal island near own base (low yield, safe).
            //   Contest: large central pile (high risk, high reward).
            //   Invade:  rivals' islands / loaded carriers.
            // Center is fixed; personal + contested islands get per-match XZ jitter
            // (GDD: "food island positions are randomized within constraints").
            // Master-spawned NetworkObjects replicate, so plain Random is fine —
            // no cross-peer seeding needed.

            // Center pile — bigger Amount + bigger visual. Never moves.
            var centerObj = SpawnPile(runner, pilePrefab, Vector3.zero, _centerPileAmount);
            if (centerObj != null)
            {
                centerObj.transform.localScale = Vector3.one * _centerPileVisualScale;
            }

            // Personal islands — one in front of each base, on the base→center line.
            for (int i = 0; i < _corners.Length; i++)
            {
                var pos = Vector3.Lerp(_corners[i], Vector3.zero, _personalPileInset) + JitterXZ();
                SpawnPile(runner, pilePrefab, pos, _personalPileAmount);
            }

            // Contested islands — between each pair of adjacent corners, pulled
            // slightly toward the center so they sit inside the walls.
            for (int i = 0; i < _corners.Length; i++)
            {
                var mid = (_corners[i] + _corners[(i + 1) % _corners.Length]) * 0.5f;
                var pos = mid * _contestedEdgeInset + JitterXZ();
                SpawnPile(runner, pilePrefab, pos, _contestedPileAmount);
            }

            _log?.Info(Source, $"Spawned GDD layout: center ({_centerPileAmount}) + " +
                $"4 personal ({_personalPileAmount}) + 4 contested ({_contestedPileAmount}) piles. " +
                $"Total food = {_centerPileAmount + 4f * (_personalPileAmount + _contestedPileAmount):0}.");
        }

        private Vector3 JitterXZ()
        {
            var j = Random.insideUnitCircle * _pilePositionJitter;
            return new Vector3(j.x, 0f, j.y);
        }

        private NetworkObject SpawnPile(NetworkRunner runner, NetworkObject pilePrefab, Vector3 pos, float amount)
        {
            return runner.Spawn(
                pilePrefab,
                pos,
                Quaternion.identity,
                onBeforeSpawned: (_, networkObject) =>
                {
                    if (amount <= 0f) return; // let prefab default seed
                    var pile = networkObject.GetComponent<FoodPile>();
                    if (pile != null)
                    {
                        pile.Amount = amount;
                        pile.MaxAmount = amount;
                    }
                });
        }
    }
}

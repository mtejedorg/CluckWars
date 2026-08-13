using System.Collections.Generic;
using CluckWars.Logging;
using CluckWars.Networking;
using CluckWars.Services;
using Fusion;
using Unity.AI.Navigation;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
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
        [SerializeField] private float _planeSize = 38f;

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

        [Header("Interior terrain — Standard (wall segments; ADR 0003 Decision 5)")]
        [Tooltip("Number of Standard wall segments. 0 cleanly disables the class. Interior terrain is LOCAL geometry that blocks movement, so online layouts are seeded from the session name — every peer builds the identical map.")]
        [Range(0, 20)]
        [SerializeField] private int _standardObstacleCount = 8;
        [Tooltip("Min (x) / max (y) length of a Standard wall segment. Depth is _wallThickness.")]
        [SerializeField] private Vector2 _interiorWallLengthRange = new Vector2(3f, 6f);
        [Tooltip("Standard obstacle height. Low enough to see over in the iso view; must stay above the NavMesh step height (0.75) so bots can't path over.")]
        [Min(0.8f)]
        [SerializeField] private float _interiorWallHeight = 1.1f;
        [Tooltip("Tint for Standard obstacles. Desaturated per ART.md — map stays muted so chickens pop.")]
        [SerializeField] private Color _interiorWallColor = new Color(0.45f, 0.36f, 0.26f, 1f);

        [Header("Interior terrain — Low (crates / fences; Barge-able in Slice 2)")]
        [Tooltip("Number of Low obstacles. 0 cleanly disables the class.")]
        [Range(0, 16)]
        [SerializeField] private int _lowObstacleCount = 6;
        [Tooltip("Min (x) / max (y) footprint side of a Low obstacle. Width and depth are drawn independently, so crates come out slightly rectangular.")]
        [SerializeField] private Vector2 _lowObstacleFootprintRange = new Vector2(1.2f, 2.2f);
        [Tooltip("Low obstacle height. MUST stay above the NavMesh step height (0.75) or bots walk straight over it and it stops being terrain — 0.9 is deliberate. Low is distinguished from Standard by being Barge-able, NOT by being shorter to walk over.")]
        [Min(0.8f)]
        [SerializeField] private float _lowObstacleHeight = 0.9f;
        [Tooltip("Clearance multiplier for Low obstacles — cheap cover, so they may pack tighter than a wall segment.")]
        [Range(0.25f, 3f)]
        [SerializeField] private float _lowObstacleClearanceScale = 0.8f;
        [Tooltip("Tint for Low obstacles. Lighter crate wood, still muted.")]
        [SerializeField] private Color _lowObstacleColor = new Color(0.54f, 0.43f, 0.30f, 1f);

        [Header("Interior terrain — Tall (rocks / silos; Blink-only in Slice 2)")]
        [Tooltip("Number of Tall obstacles. 0 cleanly disables the class. Keep this low — Tall is a hard wall nothing but Blink answers.")]
        [Range(0, 8)]
        [SerializeField] private int _tallObstacleCount = 3;
        [Tooltip("Min (x) / max (y) footprint side of a Tall obstacle.")]
        [SerializeField] private Vector2 _tallObstacleFootprintRange = new Vector2(1.6f, 2.4f);
        [Tooltip("Tall obstacle height. Reads as a rock / silo — tall enough to be unmistakably un-vaultable at a glance.")]
        [Min(0.8f)]
        [SerializeField] private float _tallObstacleHeight = 2.5f;
        [Tooltip("Clearance multiplier for Tall obstacles. >1 on purpose: a Tall prop nothing but Blink can cross must never sit close enough to another obstacle or an objective to seal a lane. 1.5 is the measured ceiling — above ~1.6 no point on a 30m map is far enough from all 13 objectives and Tall silently stops placing.")]
        [Range(0.25f, 3f)]
        [SerializeField] private float _tallObstacleClearanceScale = 1.5f;
        [Tooltip("Tint for Tall obstacles. Cool grey stone — reads as 'not wood, not crossable'.")]
        [SerializeField] private Color _tallObstacleColor = new Color(0.37f, 0.38f, 0.41f, 1f);

        [Header("Interior terrain — shared placement rules")]
        [Tooltip("Walkable gap kept between every objective (food islands, corner bases/spawns) and the nearest obstacle SURFACE — not its centre. 2.2 is over two chicken diameters. Measured surface-to-surface at both ends: the obstacle contributes its real footprint rather than a bounding circle (or no long wall could ever go near an objective), and an island contributes its own footprint radius (or terrain would be placed inside the 7×4 centre island). Scaled per class.")]
        [Min(1f)]
        [SerializeField] private float _interiorWallClearance = 2.2f;
        [Tooltip("Minimum gap between two obstacles' footprints (not their centres). Must stay wider than a chicken (radius 0.5) or obstacles fuse into impassable clumps. Scaled per class.")]
        [Min(0.5f)]
        [SerializeField] private float _obstacleSpacing = 1.5f;
        [Tooltip("Margin between the sampling square and the boundary walls. Obstacles are sampled in SQUARE space (not a polar annulus) so they reach the dead corners behind the bases — ADR 0003 Decision 6.")]
        [Min(0.5f)]
        [SerializeField] private float _obstacleEdgeMargin = 1.5f;
        [Tooltip("Rejection-sampling attempt budget per requested obstacle. Placement is best-effort: if the map can't fit the requested density the generator places fewer and logs it rather than relaxing clearances.")]
        [Range(4, 80)]
        [SerializeField] private int _obstaclePlacementAttempts = 40;

        [Header("Bases")]
        [Tooltip("How far each corner base sits from the center.")]
        [Min(2f)]
        [SerializeField] private float _baseCornerDistance = 19f;

        [Header("Food piles (GDD §3: center + personal + contested islands)")]
        [Tooltip("Initial food in the central pile — large, high risk, high reward (GDD §3: 20).")]
        [Min(5f)]
        [SerializeField] private float _centerPileAmount = 20f;

        [Tooltip("Full world X,Z footprint of the centre island at 100% fill. Shrunk 35% from the original 12x10: piles are solid NavMesh-carving blockers, and at the old sizes they covered 25% of the 38x38 arena floor, which left chases nowhere to happen. See the 2026-08-13 Peck/four-slot spec.")]
        [SerializeField] private Vector2 _centerPileFootprint = new Vector2(7.8f, 6.5f);

        [Tooltip("Make the center pile permanent (ADR 0003 Decision 2b): it can never be drained below its floor and slowly regenerates, so it stays a solid obstacle and the one contested resource of the late game. Floor and regen rate are tuned on the FoodPile prefab.")]
        [SerializeField] private bool _centerPileIsPermanent = false;

        [Tooltip("Food in each player's personal doorstep island (T1 = 5 food).")]
        [Min(0f)]
        [SerializeField] private float _personalPileAmount = 5f;

        [Tooltip("Full world X,Z footprint of each personal island at 100% fill. Shrunk 35% from the original 5.5x4.6 — see _centerPileFootprint.")]
        [SerializeField] private Vector2 _personalPileFootprint = new Vector2(3.6f, 3.0f);

        [Tooltip("Personal island position = Lerp(corner, center, inset). 0.35 puts it a few units in front of the base, toward the action.")]
        [Range(0.2f, 0.6f)]
        [SerializeField] private float _personalPileInset = 0.3673f;

        [Tooltip("Food in each contested island between adjacent player pairs (T2 = 10 food).")]
        [Min(0f)]
        [SerializeField] private float _contestedPileAmount = 10f;

        [Tooltip("Full world X,Z footprint of each contested island at 100% fill. Shrunk 35% from the original 6.5x5.4 — see _centerPileFootprint.")]
        [SerializeField] private Vector2 _contestedPileFootprint = new Vector2(4.2f, 3.5f);

        [Tooltip("Contested island position = edge midpoint scaled toward center. 0.8 keeps it between the two neighbours but inside the walls.")]
        [Range(0.4f, 1f)]
        [SerializeField] private float _contestedEdgeInset = 0.7895f;

        [Tooltip("Random XZ jitter applied to personal + contested island positions each match (GDD §3: positions randomized within constraints). Center pile never moves. Kept small — the keep-clear disc must cover every jittered position, and piles now sit only 22.5° off the wall bearings, so a large jitter erases the walls entirely.")]
        [Min(0f)]
        [SerializeField] private float _pilePositionJitter = 0.4f;

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

        /// <summary>
        /// Half the arena's side length, published once the map is generated so
        /// systems that need arena bounds (notably <see cref="JumpResolver"/>, which
        /// must reject landings outside the play area) don't have to hardcode it.
        /// Falls back to the serialized default before <see cref="Awake"/> runs.
        /// </summary>
        public static float ArenaHalfSize { get; private set; } = 19f;

        [Header("Baked map scene")]
        [Tooltip("When true (the v0.5 default) the arena's static geometry is NOT generated at runtime — it is loaded from the baked map scene below, so it can be opened, inspected and hand-tuned in the Editor. Untick to fall back to the procedural path (used by the baker itself and by any future randomised map).")]
        [SerializeField] private bool _useBakedGeometry = true;

        [Tooltip("Name of the additively-loaded scene holding the baked arena geometry. Must be present in Build Settings.")]
        [SerializeField] private string _bakedMapSceneName = "Map";

        /// <summary>
        /// Parent for all generated static geometry. Defaults to this transform; the
        /// Editor baker points it at a detached root so the geometry can be moved into
        /// the map scene instead of living under the runtime component.
        /// </summary>
        private Transform _geometryRoot;

        private Transform GeometryRoot => _geometryRoot != null ? _geometryRoot : transform;

        /// <summary>
        /// Builds the arena's STATIC geometry — ground, boundary, interior walls. Contains
        /// nothing networked; bases and food piles are <c>NetworkObject</c>s spawned at
        /// runtime by <see cref="HandleRunnerReady"/> and are deliberately excluded, since
        /// baking them would break Fusion's spawn ownership.
        /// </summary>
        /// <summary>
        /// Radial distance of the four doorstep (T1) piles, on the corner diagonals.
        /// Shared by the runtime spawn and the baked zone markers so a marker can never
        /// drift away from the pile it represents.
        /// </summary>
        public const float PersonalPileRadius = 17f;

        /// <summary>Radial distance of the four contested (T2) piles, on the edge midpoints.</summary>
        public const float ContestedPileRadius = 15f;

        [Tooltip("Radius of the baked base-zone shadow. MUST mirror PlayerBase._depositRadius on the prefab — the shadow is meaningless if it doesn't match the actual deposit zone. Pinned by DataIntegrityTests.")]
        [Min(0.5f)]
        [SerializeField] private float _baseZoneRadius = 2.5f;

        /// <summary>Radius the baked base shadows are drawn at. See <c>PlayerBase.DepositRadius</c>.</summary>
        public float BaseZoneRadius => _baseZoneRadius;

        public void BuildStaticGeometry(
            Transform root = null,
            Material pileZoneMaterial = null,
            Material baseZoneMaterial = null)
        {
            _geometryRoot = root;
            ArenaHalfSize = _planeSize * 0.5f;
            ComputeSpawnPoints();
            BuildPlane();
            BuildBoundaryWalls();
            BuildInteriorObstacles();
            BuildPileZoneMarkers(pileZoneMaterial);
            BuildBaseZoneMarkers(baseZoneMaterial);
            _geometryRoot = null;
        }

        /// <summary>
        /// Flat dark discs marking each player base's deposit zone. Bases are
        /// <c>NetworkObject</c>s spawned at runtime, so without these the baked map gives
        /// no hint of where a quarter of the arena's meaning lives.
        /// </summary>
        /// <remarks>
        /// Drawn as CIRCLES, not ellipses like the pile zones: a base's deposit trigger is
        /// a radius check (<c>PlayerBase.DepositRadius</c>), so a circle is the honest
        /// shape. Collider-free for the same reason as the pile markers — see
        /// <see cref="BuildPileZoneMarkers"/>.
        ///
        /// Positioned at <c>_spawnPoints</c>, which is exactly where
        /// <see cref="SpawnBases"/> puts them, so shadow and base coincide by construction
        /// rather than by a copied coordinate.
        /// </remarks>
        private void BuildBaseZoneMarkers(Material material)
        {
            if (_spawnPoints == null) return;

            float diameter = _baseZoneRadius * 2f;
            for (int i = 0; i < _spawnPoints.Length; i++)
            {
                AddZoneMarker($"BaseZone_{i}", _spawnPoints[i],
                    new Vector2(diameter, diameter), material);
            }
        }

        /// <summary>
        /// Flat brown discs marking where each food pile will spawn — the map reads as if
        /// every pile were fully depleted. Purely decorative: the real piles are
        /// <c>NetworkObject</c>s spawned at runtime on top of these, and as a pile drains
        /// and shrinks its marker is progressively revealed underneath.
        /// </summary>
        /// <remarks>
        /// <b>Deliberately collider-free.</b> A collider here would be read as solid terrain
        /// by three separate systems: <c>CharacterController</c> movement, the NavMesh bake
        /// (which collects PhysicsColliders, so bots would path around empty ground), and
        /// <see cref="JumpResolver"/>'s overlap test, which treats any non-trigger collider
        /// as blocking and would make every pile zone reject a landing.
        ///
        /// Markers sit at the piles' NOMINAL positions. Piles jitter ±_pilePositionJitter
        /// each match, so a spawned pile is offset from its marker by up to that much —
        /// intended, since the marker denotes the zone rather than the exact footprint.
        /// </remarks>
        private void BuildPileZoneMarkers(Material material)
        {
            if (_corners == null) return;

            AddZoneMarker("PileZone_Center", Vector3.zero, _centerPileFootprint, material);

            for (int i = 0; i < _corners.Length; i++)
            {
                AddZoneMarker($"PileZone_Personal{i}",
                    _corners[i].normalized * PersonalPileRadius, _personalPileFootprint, material);
            }

            for (int i = 0; i < _corners.Length; i++)
            {
                var mid = (_corners[i] + _corners[(i + 1) % _corners.Length]) * 0.5f;
                AddZoneMarker($"PileZone_Contested{i}",
                    mid.normalized * ContestedPileRadius, _contestedPileFootprint, material);
            }
        }

        /// <summary>
        /// Creates one flat, collider-free zone decal. Shared by the pile and base
        /// markers — see their callers for why colliders are omitted.
        /// </summary>
        private void AddZoneMarker(string name, Vector3 position, Vector2 footprint, Material material)
        {
            var marker = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            marker.name = name;
            marker.transform.SetParent(GeometryRoot, worldPositionStays: false);

            // A Unity cylinder is 2 units tall, so a Y scale of 0.01 gives a 2 cm slab.
            // Lifted 1 cm so it rests ON the ground (top at y=0) rather than z-fighting it.
            marker.transform.localScale = new Vector3(footprint.x, 0.01f, footprint.y);
            marker.transform.localPosition = new Vector3(position.x, 0.01f, position.z);

            var collider = marker.GetComponent<Collider>();
            if (collider != null) DestroyImmediate(collider);

            if (material != null) marker.GetComponent<Renderer>().sharedMaterial = material;
        }

        private void Awake()
        {
            ArenaHalfSize = _planeSize * 0.5f;
            ComputeSpawnPoints();

            if (_useBakedGeometry)
            {
                EnsureBakedMapLoaded();
            }
            else
            {
                BuildPlane();
                BuildBoundaryWalls();
                BuildInteriorObstacles();
                BuildNavMesh();
            }

            if (_network != null) _network.OnRunnerReady += HandleRunnerReady;
            else _log?.Warn(Source, "INetworkService not injected; bases/piles won't spawn.");
        }

        /// <summary>
        /// Additively loads the baked map scene if it isn't already present. Uses the
        /// SYNCHRONOUS overload deliberately: the geometry and its NavMesh must exist
        /// before chickens spawn, and an async load would let the first frames run on an
        /// empty arena. The scene carries no <c>NetworkObject</c>s, so Fusion's
        /// <c>NetworkSceneManagerDefault</c> neither syncs nor unloads it — every peer
        /// loads its own identical copy locally.
        /// </summary>
        private void EnsureBakedMapLoaded()
        {
            var existing = SceneManager.GetSceneByName(_bakedMapSceneName);
            if (existing.IsValid() && existing.isLoaded)
            {
                _log?.Debug(Source, $"Baked map scene '{_bakedMapSceneName}' already loaded.");
                return;
            }

            if (!IsSceneInBuildSettings(_bakedMapSceneName))
            {
                // Failing loudly here beats a silently empty arena: with no ground the
                // chickens fall forever and every diagnosis starts from the wrong end.
                _log?.Error(Source,
                    $"Baked map scene '{_bakedMapSceneName}' is not in Build Settings — the arena will be EMPTY. " +
                    "Add it via Cluck Wars/Map/Bake Map Scene, or untick Use Baked Geometry to generate procedurally.");
                return;
            }

            SceneManager.LoadScene(_bakedMapSceneName, LoadSceneMode.Additive);
            _log?.Info(Source, $"Loaded baked map scene '{_bakedMapSceneName}'.");
        }

        /// <summary>
        /// Scans the actual build list by index. Preferred over
        /// <c>Application.CanStreamedLevelBeLoaded</c>, which was observed returning false
        /// in the Editor for a scene that IS registered — it does not reliably reflect a
        /// freshly-edited build settings list, and a false negative here would skip the
        /// load and leave the arena empty.
        /// </summary>
        private static bool IsSceneInBuildSettings(string sceneName)
        {
            int count = SceneManager.sceneCountInBuildSettings;
            for (int i = 0; i < count; i++)
            {
                string path = SceneUtility.GetScenePathByBuildIndex(i);
                if (System.IO.Path.GetFileNameWithoutExtension(path) == sceneName) return true;
            }
            return false;
        }

        private void OnDestroy()
        {
            if (_network != null) _network.OnRunnerReady -= HandleRunnerReady;
            DestroyObstacleMaterials();
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
            // Use Cube instead of Plane so the floor has a 1m thick BoxCollider.
            // This prevents CharacterControllers from tunneling through on slower mobile devices.
            var plane = GameObject.CreatePrimitive(PrimitiveType.Cube);
            plane.name = "GeneratedGround";
            plane.transform.SetParent(GeometryRoot, worldPositionStays: false);
            plane.transform.localScale = new Vector3(_planeSize, 1f, _planeSize);
            plane.transform.localPosition = new Vector3(0f, -0.5f, 0f);

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

        /// <summary>Creates a wall/obstacle cube collider, optionally rendered.</summary>
        /// <param name="forceVisible">
        /// Skips the <see cref="_wallsVisible"/> gate entirely. Boundary walls
        /// (<see cref="BuildBoundaryWalls"/>) leave this false so the "invisible
        /// horizon" behaviour is untouched. Interior/pinwheel terrain
        /// (<see cref="BuildInteriorObstacles"/>) passes true — that geometry is
        /// gameplay-critical and must always render, regardless of the boundary's
        /// debug-visibility toggle. The caller assigns the per-class tinted
        /// material immediately after this returns, so no material handling
        /// happens on the forced-visible path.
        /// </param>
        private GameObject CreateWall(string name, Vector3 localPosition, Vector3 size, bool forceVisible = false)
        {
            var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wall.name = name;
            wall.transform.SetParent(GeometryRoot, worldPositionStays: false);
            wall.transform.localPosition = localPosition;
            wall.transform.localScale = size;

            if (forceVisible)
            {
                return wall;
            }

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

            return wall;
        }

        // ---- Interior terrain + NavMesh ----------------------------------------

        /// <summary>Per-class placement recipe, assembled from the serialized fields.</summary>
        private readonly struct ObstacleSpec
        {
            public readonly ObstacleClass Class;
            public readonly int Count;
            public readonly Vector2 WidthRange;   // local X extent
            public readonly Vector2 DepthRange;   // local Z extent
            public readonly float Height;
            public readonly float ClearanceScale;
            public readonly Color Tint;

            public ObstacleSpec(ObstacleClass cls, int count, Vector2 widthRange, Vector2 depthRange,
                                float height, float clearanceScale, Color tint)
            {
                Class = cls;
                Count = count;
                WidthRange = widthRange;
                DepthRange = depthRange;
                Height = height;
                ClearanceScale = clearanceScale;
                Tint = tint;
            }
        }

        /// <summary>An already-placed obstacle, remembered so later obstacles keep clear of it.</summary>
        private readonly struct PlacedObstacle
        {
            public readonly Vector3 Position;
            public readonly float FootprintRadius;
            public PlacedObstacle(Vector3 position, float footprintRadius)
            {
                Position = position;
                FootprintRadius = footprintRadius;
            }
        }

        /// <summary>
        /// Something terrain must not crowd, plus the radius it occupies itself. Spawns and
        /// bases are points; food islands are not — the centre island is 7×4, so treating it
        /// as a point would let a crate be placed 2.2 m from the origin, i.e. buried inside
        /// the island and sealing the lanes around it.
        /// </summary>
        private readonly struct Objective
        {
            public readonly Vector3 Position;
            public readonly float Radius;
            public Objective(Vector3 position, float radius)
            {
                Position = position;
                Radius = radius;
            }
        }

        /// <summary>Circumscribed radius of a pile footprint — the same convention <see cref="PlacedObstacle"/> uses.</summary>
        private static float FootprintRadius(Vector2 size) =>
            0.5f * Mathf.Sqrt(size.x * size.x + size.y * size.y);

        private static readonly int SColorId     = Shader.PropertyToID("_Color");
        private static readonly int SBaseColorId = Shader.PropertyToID("_BaseColor");

        /// <summary>One shared material per <see cref="ObstacleClass"/>, created on first use.</summary>
        private Material[] _obstacleMaterials;

        /// <summary>
        /// Builds the interior terrain vocabulary (ADR 0003 Decision 5): Low crates,
        /// Standard wall segments and Tall rocks, each tagged with a
        /// <see cref="TerrainObstacle"/> that Slice 2's Vault / Barge / Blink query.
        /// Terrain is LOCAL geometry on every peer, so online layouts are seeded from
        /// the session name (same determinism trick as MatchBootstrapper's corner
        /// permutation); solo just rolls a fresh layout each match.
        ///
        /// Classes are processed in a FIXED order (Tall → Standard → Low) and every
        /// attempt draws its full sample from the RNG before any rejection test runs,
        /// so the RNG stream advances identically on every peer. Nothing here may
        /// branch on peer-local state or the maps desync.
        ///
        /// Rejection sampling keeps every obstacle clear of the center pile, the corner
        /// bases/spawns and the nominal island positions so no objective is ever sealed
        /// off. Placement is best-effort: when the map cannot fit the requested density
        /// the generator places fewer and says so, rather than relaxing the clearances.
        /// </summary>
        private void BuildInteriorObstacles()
        {
            bool online = _selection != null && _selection.Mode != SessionMode.Solo;
            string sessionName = online ? _selection.SessionName : null;
            int seed = online ? SessionNameSeed(sessionName) : new System.Random().Next();

            float half = _planeSize * 0.5f;
            float centerKeepClear = FootprintRadius(_centerPileFootprint);
            float baseKeepClear = 4f;
            var keepClearDiscs = ComputeBaseAndPileKeepClearDiscs(baseKeepClear);

            // Generate pinwheel wall segments. armThickness = _wallThickness — every
            // segment gets built with that same physical thickness below regardless of
            // its ObstacleClass, so the hub-plaza math needs to know it up front.
            var segments = PinwheelLayout.Build(half, _standardObstacleCount, seed, centerKeepClear, _wallThickness, keepClearDiscs);

            // ObstacleSpecs for the three classes to reuse shared materials and tints
            var specs = new[]
            {
                new ObstacleSpec(ObstacleClass.Tall, _tallObstacleCount,
                    _tallObstacleFootprintRange, _tallObstacleFootprintRange,
                    _tallObstacleHeight, _tallObstacleClearanceScale, _tallObstacleColor),
                new ObstacleSpec(ObstacleClass.Standard, _standardObstacleCount,
                    _interiorWallLengthRange, new Vector2(_wallThickness, _wallThickness),
                    _interiorWallHeight, 1f, _interiorWallColor),
                new ObstacleSpec(ObstacleClass.Low, _lowObstacleCount,
                    _lowObstacleFootprintRange, _lowObstacleFootprintRange,
                    _lowObstacleHeight, _lowObstacleClearanceScale, _lowObstacleColor),
            };

            for (int i = 0; i < segments.Length; i++)
            {
                var seg = segments[i];

                Vector2 a = seg.A;
                Vector2 b = seg.B;
                Vector2 delta = b - a;
                float length = delta.magnitude;
                Vector2 center2D = a + delta * 0.5f;
                Vector3 center = new Vector3(center2D.x, 0f, center2D.y);

                // Get spec matching segment class
                ObstacleSpec spec = GetSpecForClass(seg.Class, specs);

                // Height comes from spec. Thickness is _wallThickness for every arm
                // regardless of class — deliberately NOT forced thicker for visual bulk:
                // PinwheelLayout.Build's hub-plaza radius scales with whatever thickness is
                // passed in, so an artificially bold wall (e.g. a hardcoded 1.4 floor, tried
                // and reverted 2026-07-27) silently eats into the corridor clearance it
                // guarantees near the centre. If the pinwheel needs to read bolder, raise
                // _wallThickness itself so the clearance math accounts for it.
                float height = spec.Height;
                float wedgeThickness = _wallThickness;
                Vector3 size = new Vector3(length, height, wedgeThickness);

                // Create wall GameObject using CreateWall. forceVisible: true — interior
                // terrain must always render (see CreateWall's forceVisible doc), unlike
                // the boundary walls which stay gated on _wallsVisible.
                string wallName = $"Terrain_{seg.Class}_{i}";
                GameObject go = CreateWall(wallName, center + Vector3.up * (height * 0.5f), size, forceVisible: true);

                // Apply rotation
                float angleRad = Mathf.Atan2(delta.y, delta.x);
                float yaw = -angleRad * Mathf.Rad2Deg;
                go.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);

                // Add TerrainObstacle tag with ObstacleClass
                go.AddComponent<TerrainObstacle>().SetClass(seg.Class);

                // Assign URP class material (no per-wall clones)
                var mr = go.GetComponent<MeshRenderer>();
                if (mr != null)
                {
                    var mat = EnsureClassMaterial(spec, mr.sharedMaterial);
                    if (mat != null) mr.sharedMaterial = mat;
                }
            }

            string seedNote = online ? $"session '{sessionName}' (seed {seed})" : $"solo-random (seed {seed})";
            _log?.Info(Source, $"Interior terrain: placed {segments.Length} pinwheel obstacles. Seed={seedNote}.");
        }

        /// <summary>
        /// No-build discs the pinwheel arms must be pushed clear of, beyond the centre
        /// pile: the four bases (real corner positions — previously
        /// <c>PinwheelLayout.Build</c> guessed its own approximate base positions
        /// internally, which only matched the real ones by coincidence at the default
        /// arena size) plus every personal and contested food pile.
        /// </summary>
        /// <remarks>
        /// Piles get their per-match XZ jitter (<see cref="JitterXZ"/>) only once
        /// <see cref="SpawnFoodPiles"/> runs, which is after interior terrain is built —
        /// so this uses each pile's NOMINAL (pre-jitter) position and inflates its radius
        /// by <see cref="_pilePositionJitter"/> to cover every position the pile could
        /// actually land at, plus <see cref="PinwheelLayout.PileArmBuffer"/> so a stocked
        /// pile can never end up flush against an arm.
        /// </remarks>
        private KeepClearDisc[] ComputeBaseAndPileKeepClearDiscs(float baseKeepClear)
        {
            float personalRadius = FootprintRadius(_personalPileFootprint) + _pilePositionJitter + PinwheelLayout.PileArmBuffer;
            float contestedRadius = FootprintRadius(_contestedPileFootprint) + _pilePositionJitter + PinwheelLayout.PileArmBuffer;

            var discs = new KeepClearDisc[_corners.Length * 3]; // base + personal + contested per corner
            int idx = 0;
            for (int i = 0; i < _corners.Length; i++)
            {
                discs[idx++] = new KeepClearDisc(new Vector2(_corners[i].x, _corners[i].z), baseKeepClear);

                var personalNominal = Vector3.Lerp(_corners[i], Vector3.zero, _personalPileInset);
                discs[idx++] = new KeepClearDisc(new Vector2(personalNominal.x, personalNominal.z), personalRadius);

                var mid = (_corners[i] + _corners[(i + 1) % _corners.Length]) * 0.5f;
                var contestedNominal = mid * _contestedEdgeInset;
                discs[idx++] = new KeepClearDisc(new Vector2(contestedNominal.x, contestedNominal.z), contestedRadius);
            }
            return discs;
        }

        private ObstacleSpec GetSpecForClass(ObstacleClass cls, ObstacleSpec[] specs)
        {
            for (int i = 0; i < specs.Length; i++)
            {
                if (specs[i].Class == cls) return specs[i];
            }
            return specs[1]; // fallback to Standard
        }

        /// <summary>
        /// Rejection-samples <paramref name="spec"/>.Count obstacles into the arena.
        /// Returns how many actually fit. Positions are sampled in SQUARE space rather
        /// than the old polar annulus — a square's corners sit outside any radial bound,
        /// so the regions behind the bases used to be both dead space and obstacle-free
        /// (ADR 0003 Decision 6). Containment is then enforced by an exact
        /// rotated-bounds test against the boundary walls.
        /// </summary>
        /// <remarks>
        /// DEAD CODE as of the pinwheel rewrite (commit 6b27390): <see cref="BuildInteriorObstacles"/>
        /// now builds interior terrain from <c>PinwheelLayout.Build</c> and no longer calls this
        /// method — verified with a project-wide grep, only the declaration below matches. Left in
        /// place (not deleted) because <see cref="_interiorWallClearance"/>, <see cref="_obstacleSpacing"/>,
        /// <see cref="_obstacleEdgeMargin"/> and <see cref="_obstaclePlacementAttempts"/> are still
        /// serialized inspector fields on every scene's MapGenerator component, and this is the only
        /// consumer of them plus <see cref="DistanceToBox"/>/<see cref="FitsInsideBoundary"/>/
        /// <see cref="PlacedObstacle"/>/<see cref="Objective"/>. Removing it cleanly means also
        /// removing those four fields (touching scene YAML beyond this bug's scope) — flagging here
        /// instead so a future pass can decide whether to fully retire the rejection-sampling path
        /// or keep it as a fallback placement strategy.
        /// </remarks>
        private int PlaceObstacleClass(in ObstacleSpec spec, System.Random rng,
                                       List<Objective> objectives, List<PlacedObstacle> placed)
        {
            if (spec.Count <= 0) return 0;   // count 0 cleanly disables the class

            float half = _planeSize * 0.5f;
            float objectiveKeepOut = _interiorWallClearance * spec.ClearanceScale;
            float gap = _obstacleSpacing * spec.ClearanceScale;
            int budget = spec.Count * _obstaclePlacementAttempts;
            int made = 0;

            for (int attempt = 0; attempt < budget && made < spec.Count; attempt++)
            {
                // Draw the complete sample BEFORE any rejection test: the RNG stream must
                // advance by exactly five draws per attempt on every peer, or two clients
                // sharing a seed walk different sequences and build different maps.
                float width = Mathf.Lerp(spec.WidthRange.x, spec.WidthRange.y, (float)rng.NextDouble());
                float depth = Mathf.Lerp(spec.DepthRange.x, spec.DepthRange.y, (float)rng.NextDouble());
                float x     = Mathf.Lerp(-half, half, (float)rng.NextDouble());
                float z     = Mathf.Lerp(-half, half, (float)rng.NextDouble());
                float yaw   = (float)rng.NextDouble() * 180f;

                var center = new Vector3(x, 0f, z);
                float footprintRadius = FootprintRadius(new Vector2(width, depth));

                if (!FitsInsideBoundary(center, width, depth, yaw)) continue;

                // Clearance is measured to the obstacle's SURFACE, not to its bounding
                // circle. That matters: a 6×0.5 wall's bounding circle is 3 m, so a
                // radius test would refuse to let any long wall within 6 m of an
                // objective and the map physically cannot hold the target density.
                // Surface distance is both the honest reading of "keep-clear radius"
                // and what actually determines whether an objective stays approachable.
                bool blocked = false;
                for (int i = 0; i < objectives.Count && !blocked; i++)
                {
                    float surface = DistanceToBox(objectives[i].Position, center, yaw, width, depth);
                    if (surface - objectives[i].Radius < objectiveKeepOut) blocked = true;
                }
                if (blocked) continue;

                // Footprint-to-footprint gap: a long wall and a crate need the same
                // walkable lane between them regardless of their sizes.
                for (int i = 0; i < placed.Count && !blocked; i++)
                {
                    float surface = DistanceToBox(placed[i].Position, center, yaw, width, depth);
                    if (surface - placed[i].FootprintRadius < gap) blocked = true;
                }
                if (blocked) continue;

                var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                go.name = $"Terrain_{spec.Class}_{made}";
                go.transform.SetParent(GeometryRoot, worldPositionStays: false);
                go.transform.localPosition = center + Vector3.up * (spec.Height * 0.5f);
                go.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
                go.transform.localScale = new Vector3(width, spec.Height, depth);

                // The tag Slice 2's Vault / Barge / Blink read. Discrimination is by
                // class, never inferred from spec.Height.
                go.AddComponent<TerrainObstacle>().SetClass(spec.Class);

                var mr = go.GetComponent<MeshRenderer>();
                if (mr != null)
                {
                    var mat = EnsureClassMaterial(spec, mr.sharedMaterial);
                    if (mat != null) mr.sharedMaterial = mat;
                }

                placed.Add(new PlacedObstacle(center, footprintRadius));
                made++;
            }

            return made;
        }

        /// <summary>
        /// Shortest distance on the XZ plane from <paramref name="point"/> to the surface
        /// of a yaw-rotated box. 0 when the point is inside the box. Pure float maths with
        /// no branching on state, so every peer computes the same answer.
        /// </summary>
        private static float DistanceToBox(Vector3 point, Vector3 center, float yawDegrees, float width, float depth)
        {
            float rad = yawDegrees * Mathf.Deg2Rad;
            float c = Mathf.Cos(rad), s = Mathf.Sin(rad);
            float dx = point.x - center.x, dz = point.z - center.z;

            // World → box-local (yaw only; obstacles never pitch or roll).
            float lx = dx * c - dz * s;
            float lz = dx * s + dz * c;

            float ox = Mathf.Max(Mathf.Abs(lx) - width * 0.5f, 0f);
            float oz = Mathf.Max(Mathf.Abs(lz) - depth * 0.5f, 0f);
            return Mathf.Sqrt(ox * ox + oz * oz);
        }

        /// <summary>
        /// True when the yaw-rotated box fits strictly inside the boundary walls with
        /// <c>_obstacleEdgeMargin</c> to spare. Uses the exact axis-aligned extents of
        /// the rotated box, so a long wall angled into a corner is accepted or rejected
        /// on its real footprint rather than a conservative bounding circle.
        /// </summary>
        private bool FitsInsideBoundary(Vector3 center, float width, float depth, float yawDegrees)
        {
            float rad = yawDegrees * Mathf.Deg2Rad;
            float c = Mathf.Abs(Mathf.Cos(rad));
            float s = Mathf.Abs(Mathf.Sin(rad));
            float hx = 0.5f * (c * width + s * depth);
            float hz = 0.5f * (s * width + c * depth);

            // The boundary walls' inner faces sit exactly at ±_planeSize/2.
            float limit = _planeSize * 0.5f - _obstacleEdgeMargin;
            return Mathf.Abs(center.x) + hx <= limit
                && Mathf.Abs(center.z) + hz <= limit;
        }

        /// <summary>
        /// Returns the one shared material for this obstacle class, creating it on first
        /// use from <c>_wallMaterial</c> (or the primitive's own default) and tinting it.
        /// Deliberately NOT <c>mr.material</c>: that clones a material per object, which
        /// at ~17 obstacles costs 17 instances, breaks batching and adds GC churn on the
        /// Android target. Three shared materials keep the whole terrain set batchable.
        /// </summary>
        private Material EnsureClassMaterial(in ObstacleSpec spec, Material primitiveDefault)
        {
            _obstacleMaterials ??= new Material[(int)ObstacleClass.Tall + 1];

            int idx = (int)spec.Class;
            if (_obstacleMaterials[idx] != null) return _obstacleMaterials[idx];

            // Use _groundMaterial as a URP-compatible fallback if _wallMaterial is null,
            // to avoid pulling in the built-in Standard shader primitiveDefault.
            var template = _wallMaterial != null ? _wallMaterial :
                           (_groundMaterial != null ? _groundMaterial : primitiveDefault);
            if (template == null)
            {
                // Untinted grey terrain is a readability bug (players can't tell Low from
                // Tall), so surface it instead of shipping a silently colourless map.
                _log?.Warn(Source, $"No material template for {spec.Class} obstacles " +
                    "(_wallMaterial unassigned and the primitive has no default material) — " +
                    "terrain will render untinted and the three classes won't be distinguishable.");
                return null;
            }
            bool templateIsGroundFallback = _wallMaterial == null && template == _groundMaterial;

            var mat = new Material(template)
            {
                name = $"TerrainObstacle_{spec.Class}",
                hideFlags = HideFlags.DontSave,
            };

            // The ground-fallback path clones _groundMaterial, which carries the grass
            // albedo texture — a flat colour multiplied over a grass texture still reads
            // as "grass texture", not as a distinct tinted block. Obstacles are meant to be
            // differentiated by colour alone (Low/Standard/Tall), so strip the base map when
            // that's the template in play. Only that path: a deliberately assigned
            // _wallMaterial is left untouched, since its texture may be an intentional
            // design choice, not a borrowed ground fallback.
            if (templateIsGroundFallback)
            {
                if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", null);
                if (mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", null);
            }

            // URP Lit exposes _BaseColor; keep _Color set too for any Built-in-style
            // material someone drops into _wallMaterial.
            if (mat.HasProperty(SBaseColorId)) mat.SetColor(SBaseColorId, spec.Tint);
            if (mat.HasProperty(SColorId)) mat.SetColor(SColorId, spec.Tint);

            _obstacleMaterials[idx] = mat;
            return mat;
        }

        private void DestroyObstacleMaterials()
        {
            if (_obstacleMaterials == null) return;
            for (int i = 0; i < _obstacleMaterials.Length; i++)
            {
                if (_obstacleMaterials[i] != null) Destroy(_obstacleMaterials[i]);
                _obstacleMaterials[i] = null;
            }
        }

        /// <summary>
        /// Bakes a runtime NavMesh from the generated geometry (physics colliders,
        /// so the invisible boundary walls count too). Bots path around interior
        /// terrain via this mesh; FoodPiles spawn later and carve dynamically with
        /// a NavMeshObstacle on their blocker. The triangle count is logged because
        /// interior density directly drives both bake cost and bot pathing cost.
        /// </summary>
        private void BuildNavMesh()
        {
            var surface = gameObject.AddComponent<NavMeshSurface>();
            surface.collectObjects = CollectObjects.Children;
            surface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
            surface.BuildNavMesh();

            int triangles = NavMesh.CalculateTriangulation().indices.Length / 3;
            if (triangles <= 0)
            {
                // An empty navmesh means every bot falls back to standing still — never
                // let that pass as a successful bake.
                _log?.Warn(Source, "NavMesh bake produced 0 triangles — bots will not be able to path.");
            }
            else
            {
                _log?.Info(Source, $"NavMesh baked for bot pathing ({triangles} triangles).");
            }
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

            var loggedCoords = new System.Text.StringBuilder();
            loggedCoords.AppendLine("--- ORACLE PILE COORDINATES ---");
            loggedCoords.AppendLine($"Center: (0.00, 0.00) | Food: {_centerPileAmount:0.00}");

            // Center pile — bigger Amount + bigger visual. Never moves, and (ADR 0003
            // Decision 2b) never fully drains: the outer piles are consumed away over the
            // match, so the centre is what the endgame converges on.
            SpawnPile(runner, pilePrefab, Vector3.zero, _centerPileAmount, _centerPileFootprint, _centerPileIsPermanent);

            // Personal doorstep islands — 4 piles at radius 17 m on corner diagonals.
            for (int i = 0; i < _corners.Length; i++)
            {
                var pos = _corners[i].normalized * PersonalPileRadius + JitterXZ();
                SpawnPile(runner, pilePrefab, pos, _personalPileAmount, _personalPileFootprint);
                loggedCoords.AppendLine($"Personal {i}: ({pos.x:F2}, {pos.z:F2}) | Food: {_personalPileAmount:0.00}");
            }

            // Contested islands — 4 piles at radius 15 m on edge midpoints.
            for (int i = 0; i < _corners.Length; i++)
            {
                var mid = (_corners[i] + _corners[(i + 1) % _corners.Length]) * 0.5f;
                var pos = mid.normalized * ContestedPileRadius + JitterXZ();
                SpawnPile(runner, pilePrefab, pos, _contestedPileAmount, _contestedPileFootprint);
                loggedCoords.AppendLine($"Contested {i}: ({pos.x:F2}, {pos.z:F2}) | Food: {_contestedPileAmount:0.00}");
            }
            loggedCoords.AppendLine("-------------------------------");
            _log?.Info(Source, loggedCoords.ToString());

            _log?.Info(Source, $"Spawned GDD layout: center ({_centerPileAmount}" +
                $"{(_centerPileIsPermanent ? ", permanent" : "")}, " +
                $"{_centerPileFootprint.x:0.#}×{_centerPileFootprint.y:0.#}) + " +
                $"4 personal ({_personalPileAmount}, {_personalPileFootprint.x:0.#}×{_personalPileFootprint.y:0.#}) + " +
                $"4 contested ({_contestedPileAmount}, {_contestedPileFootprint.x:0.#}×{_contestedPileFootprint.y:0.#}) piles. " +
                $"Starting food = {_centerPileAmount + 4f * (_personalPileAmount + _contestedPileAmount):0}" +
                $"{(_centerPileIsPermanent ? " (+ centre regen)" : "")}.");
        }

        private Vector3 JitterXZ()
        {
            var j = Random.insideUnitCircle * _pilePositionJitter;
            return new Vector3(j.x, 0f, j.y);
        }

        /// <summary>
        /// Centre and outer piles share one prefab, so everything that differs between them
        /// is stamped here through <c>onBeforeSpawned</c> — that's the only way a
        /// <c>[Networked]</c> property is replicated from tick zero, before <c>Spawned()</c>
        /// runs on any peer and sizes the pile's footprint.
        /// </summary>
        private NetworkObject SpawnPile(NetworkRunner runner, NetworkObject pilePrefab, Vector3 pos, float amount, Vector2 footprint, bool permanent = false)
        {
            return runner.Spawn(
                pilePrefab,
                pos,
                Quaternion.identity,
                onBeforeSpawned: (_, networkObject) =>
                {
                    var pile = networkObject.GetComponent<FoodPile>();
                    if (pile == null)
                    {
                        // Silently skipping here would ship a centre pile that drains to
                        // zero with no visible cause — say so instead.
                        _log?.Warn(Source, $"Pile prefab '{pilePrefab.name}' has no FoodPile component; " +
                            "amount, footprint and the permanent flag were not applied.");
                        return;
                    }

                    if (amount > 0f)
                    {
                        pile.Amount = amount;
                        pile.MaxAmount = amount;
                    }
                    pile.FootprintSize = footprint;
                    pile.IsPermanent = permanent;
                });
        }

        public void ReArmForMasterPromotion()
        {
            if (!_runnerHandled) return;
            var runner = _network != null ? _network.Runner : null;
            if (runner == null || !runner.IsRunning) return;

            _log?.Info(Source, "Re-arming MapGenerator for Master promotion.");
            if (FindObjectsByType<PlayerBase>(FindObjectsSortMode.None).Length == 0)
            {
                SpawnBases(runner);
            }
            else
            {
                _log?.Debug(Source, "Bases already exist; skipping spawn during promotion.");
            }

            if (FindObjectsByType<FoodPile>(FindObjectsSortMode.None).Length == 0)
            {
                SpawnFoodPiles(runner);
            }
            else
            {
                _log?.Debug(Source, "Food piles already exist; skipping spawn during promotion.");
            }
        }
    }
}

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
        [Tooltip("World-space side length of the square ground plane. Scaled 38 -> 51.3 (x1.35) on 2026-08-14: travel TIME is held constant by scaling every move speed by the same factor, so the arena reads faster without changing the SCT axiom. Game.unity OVERRIDES this initializer — editing it here alone changes nothing at runtime.")]
        [Min(5f)]
        [SerializeField] private float _planeSize = 51.3f;

        [Header("Boundary walls (local colliders on every peer)")]
        [Tooltip("Height of the perimeter walls. Way taller than the chicken to keep them in even at 2× Speed Burst.")]
        [Min(1f)]
        [SerializeField] private float _wallHeight = 5f;
        [Tooltip("Thickness of every wall — boundary and interior alike. Thin walls + fast chickens can tunnel. Raised 0.5 -> 0.7 on 2026-08-19 so the pinwheel reads as masonry rather than a line on the floor at the enlarged arena size. SAFE TO CHANGE: BuildBoundaryWalls centres each boundary wall at half + t*0.5, so its INNER FACE stays at exactly ±_planeSize/2 regardless of thickness — the fence line and MapPropsRescaler.SolveFenceLayout are unaffected. It IS consumed by PinwheelLayout.SolveHubPlazaRadius, which is the point: a thicker arm now correctly pushes the hub plaza out instead of silently eating the corridor. Game.unity OVERRIDES this initializer.")]
        [Min(0.05f)]
        [SerializeField] private float _wallThickness = 0.7f;
        [Tooltip("Render the walls. Off by default (invisible boundary); flip on for debugging.")]
        [SerializeField] private bool _wallsVisible = false;
        [Tooltip("Optional material for the walls when _wallsVisible is true.")]
        [SerializeField] private Material _wallMaterial;

        [Header("Interior terrain — pinwheel arms (GDD §3.4)")]
        [Tooltip("Number of pinwheel arms. LOCKED AT 8 and provably optimal — see PinwheelLayout's bearing-alignment table. This is NOT a density lever: W=12 and W=20 land four arms EXACTLY on the corner diagonals, and W=16's best offset (11.25°) misses the contested pile's required 11.665° by 0.415°, so extra arms get clipped away by the very objectives they exist to route around. Add density with the scatter fields below instead. 0 cleanly disables interior walls. Interior terrain is LOCAL geometry that blocks movement, so online layouts are seeded from the session name — every peer builds the identical map.")]
        [Range(0, 20)]
        [SerializeField] private int _standardObstacleCount = 8;
        [Tooltip("Height of EVERY piece of interior terrain — arms and scatter alike. There are no height classes (GDD §3.5, locked: 'All jumps are teleports. There are no obstacle height classes.'); traversal is gated purely by footprint span. This single value is a PATHING FLOOR, not a traversal class: it must stay above the NavMesh step height (0.75) or bots walk over terrain and it stops being terrain. Raised 1.1 -> 1.40 on 2026-08-19 — 1.40 is the measured hard ceiling at which a player standing flush against a wall can still see their own chicken. Game.unity OVERRIDES this initializer.")]
        [Min(0.8f)]
        [SerializeField] private float _interiorWallHeight = 1.40f;
        [Tooltip("Tint for interior terrain. Desaturated per ART.md — the map stays muted so chickens pop. In practice this is the SCATTER's colour: the pinwheel arms are collider-only (hand-placed wall_segment art in MapProps is what you actually see), so they never consult it.")]
        [SerializeField] private Color _interiorWallColor = new Color(0.45f, 0.36f, 0.26f, 1f);

        [Header("Interior terrain — scatter cover (SectorScatter; span-gated, 4-fold symmetric)")]
        [Tooltip("Small crates per 90° SECTOR — total placed is 4x this, one per player, identical by construction. Span stays under JumpResolver.ShortDistance - 0.8, so every jump tier clears one square-on: these break sightlines and running lines, they do not gate traversal. 0 cleanly disables the role.")]
        [Range(0, 8)]
        [SerializeField] private int _coverPerSector = 1;
        [Tooltip("Min (x) / max (y) footprint side of a crate. Width and depth are drawn independently, so crates come out slightly rectangular.")]
        [SerializeField] private Vector2 _coverSpanRange = new Vector2(1.2f, 2.2f);

        [Tooltip("Short wall stubs per 90° SECTOR — total placed is 4x this. Span is drawn ABOVE JumpResolver.ShortDistance - 0.8 (4.2 m), so a Short 5 m jump cannot cross one square-on while a Normal 10 m always can — the only span-derived traversal gate the map would have. ⚠ SHIPPED AT 0 ON PURPOSE: measured, one stub per sector costs 13% of free-run length and lands the arena 16% TIGHTER than the 38 m reference (ArenaDensityMetricTests). The role is implemented and tested; it is off because the arena is already at the reference density with cover alone, not because it does not work. Turning it on is a deliberate density AND traversal-balance decision for Maestro.")]
        [Range(0, 4)]
        [SerializeField] private int _barrierPerSector = 0;
        [Tooltip("Min (x) / max (y) SPAN of a wall stub. The lower bound must stay above SectorScatter.MinBarrierSpan (4.2 m) or the role collapses into Cover and the distinction becomes decorative — pinned by SectorScatterTests. Depth is _wallThickness, so a stub reads as the same masonry as an arm.")]
        [SerializeField] private Vector2 _barrierSpanRange = new Vector2(4.6f, 6.4f);

        [Header("Interior terrain — shared placement rules")]
        [Tooltip("Walkable lane kept between a scatter footprint's SURFACE and everything else — the boundary, the pinwheel arms, every base/pile keep-clear disc, and every other scatter item. Must never drop below PinwheelLayout.MinCorridorWidth (2.0) or scatter can pinch a corridor shut; pinned by SectorScatterTests.")]
        [Min(PinwheelLayout.MinCorridorWidth)]
        [SerializeField] private float _scatterClearance = PinwheelLayout.MinCorridorWidth;
        [Tooltip("Rejection-sampling attempt budget per requested obstacle. Placement is best-effort: if the map can't fit the requested density the generator places fewer and LOGS IT rather than relaxing clearances. Read that log line after raising any count. Raised 40 -> 80 on 2026-08-19: at 40 a wall stub failed to place in 4 of 5 seeds — a silent density shortfall, not a full arena. At 80 every seed places the full authored count.")]
        [Range(4, 120)]
        [SerializeField] private int _obstaclePlacementAttempts = 80;

        [Header("Bases")]
        [Tooltip("How far each corner base sits from the center. Scaled 19 -> 25.65 (x1.35) with _planeSize, so the bases stay in the same relative spot. Game.unity OVERRIDES this initializer.")]
        [Min(2f)]
        [SerializeField] private float _baseCornerDistance = 25.65f;

        [Header("Food piles (GDD §3: center + personal + contested islands)")]
        [Tooltip("Initial food in the central pile — large, high risk, high reward (GDD §3: 20).")]
        [Min(5f)]
        [SerializeField] private float _centerPileAmount = 20f;

        [Tooltip("Full world X,Z footprint of the centre island at 100% fill. Shrunk 35% from the original 12x10: piles are solid NavMesh-carving blockers, and at the old sizes they covered 25% of the 38x38 arena floor, which left chases nowhere to happen. See the 2026-08-13 Peck/four-slot spec. HELD at its absolute size through the 2026-08-14 x1.35 arena rescale (Maestro: 'don't increase the Pile size for now, but save the value just in case') — the x1.35 value is 10.53 x 8.78.")]
        [SerializeField] private Vector2 _centerPileFootprint = new Vector2(7.8f, 6.5f);

        [Tooltip("Make the center pile permanent (ADR 0003 Decision 2b): it can never be drained below its floor and slowly regenerates, so it stays a solid obstacle and the one contested resource of the late game. Floor and regen rate are tuned on the FoodPile prefab.")]
        [SerializeField] private bool _centerPileIsPermanent = false;

        [Tooltip("Food in each player's personal doorstep island (T1 = 5 food).")]
        [Min(0f)]
        [SerializeField] private float _personalPileAmount = 5f;

        [Tooltip("Full world X,Z footprint of each personal island at 100% fill. Shrunk 35% from the original 5.5x4.6 — see _centerPileFootprint. HELD through the 2026-08-14 x1.35 arena rescale; the x1.35 value is 4.86 x 4.05.")]
        [SerializeField] private Vector2 _personalPileFootprint = new Vector2(3.6f, 3.0f);

        [Tooltip("Personal island position = Lerp(corner, center, inset). 0.35 puts it a few units in front of the base, toward the action.")]
        [Range(0.2f, 0.6f)]
        [SerializeField] private float _personalPileInset = 0.3673f;

        [Tooltip("Food in each contested island between adjacent player pairs (T2 = 10 food).")]
        [Min(0f)]
        [SerializeField] private float _contestedPileAmount = 10f;

        [Tooltip("Full world X,Z footprint of each contested island at 100% fill. Shrunk 35% from the original 6.5x5.4 — see _centerPileFootprint. HELD through the 2026-08-14 x1.35 arena rescale; the x1.35 value is 5.67 x 4.73.")]
        [SerializeField] private Vector2 _contestedPileFootprint = new Vector2(4.2f, 3.5f);

        [Tooltip("Contested island position = edge midpoint scaled toward center. 0.8 keeps it between the two neighbours but inside the walls.")]
        [Range(0.4f, 1f)]
        [SerializeField] private float _contestedEdgeInset = 0.7895f;

        [Tooltip("Random XZ jitter applied to personal + contested island positions each match (GDD §3: positions randomized within constraints). Center pile never moves. Kept small — the keep-clear disc must cover every jittered position, and piles now sit only 22.5° off the wall bearings, so a large jitter erases the walls entirely. Deliberately NOT scaled by the 2026-08-14 x1.35 arena rescale: this is a pile-local quantity and the piles keep their absolute size, so scaling it would inflate the keep-clear discs and clip the walls for no gain.")]
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
        public static float ArenaHalfSize { get; private set; } = 25.65f;

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
        /// Nominal (pre-jitter) position of a doorstep (T1) pile: a fraction
        /// <paramref name="inset"/> of the way from its corner toward the centre.
        /// </summary>
        /// <remarks>
        /// <b>Static and shared on purpose.</b> Until 2026-08-14 the pile positions existed
        /// TWICE in this file under two different parameterisations: the spawn and the zone
        /// markers used <c>corner.normalized * PersonalPileRadius</c> with a hardcoded 17 m,
        /// while the pinwheel keep-clear discs used <c>Lerp(corner, 0, _personalPileInset)</c>.
        /// They agreed only because 19 m corners at inset 0.3673 happen to land on r = 17 —
        /// a coincidence of the then-current arena size. Scaling the arena would have moved
        /// the keep-clear discs while leaving the piles behind, so terrain would have been
        /// built on top of the piles it is supposed to avoid. One formula now, called from
        /// the spawn, the markers, the keep-clear discs and the EditMode clearance tests.
        /// </remarks>
        public static Vector3 PersonalPileNominal(Vector3 corner, float inset) =>
            Vector3.Lerp(corner, Vector3.zero, inset);

        /// <summary>
        /// Nominal (pre-jitter) position of a contested (T2) pile: the midpoint of the edge
        /// between two adjacent corners, pulled toward the centre by <paramref name="inset"/>.
        /// See <see cref="PersonalPileNominal"/> for why this is shared.
        /// </summary>
        public static Vector3 ContestedPileNominal(Vector3 cornerA, Vector3 cornerB, float inset) =>
            (cornerA + cornerB) * 0.5f * inset;

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
                    PersonalPileNominal(_corners[i], _personalPileInset), _personalPileFootprint, material);
            }

            for (int i = 0; i < _corners.Length; i++)
            {
                AddZoneMarker($"PileZone_Contested{i}",
                    ContestedPileNominal(_corners[i], _corners[(i + 1) % _corners.Length], _contestedEdgeInset),
                    _contestedPileFootprint, material);
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

        /// <summary>
        /// How far each base/spawn is pulled in from its raw corner. Public because the
        /// EditMode clearance tests used to mirror it as a private literal, which is the
        /// same "one thing under two parameterisations" that caused the 2026-08-14 pile bug.
        /// </summary>
        public const float BaseInsetFraction = 0.15f;

        /// <summary>Radius of a base's no-build disc. Mirrored nowhere — read this.</summary>
        public const float BaseKeepClearRadius = 4f;

        /// <summary>
        /// Where a base and its player's spawn actually sit: a fraction
        /// <see cref="BaseInsetFraction"/> of the way from the corner toward the centre.
        /// One formula, called by <see cref="ComputeSpawnPoints"/>, by the keep-clear discs
        /// and by the tests — see <see cref="PersonalPileNominal"/> for why that matters.
        /// </summary>
        public static Vector3 SpawnPointNominal(Vector3 corner) =>
            Vector3.Lerp(corner, Vector3.zero, BaseInsetFraction);

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
                _spawnPoints[i] = SpawnPointNominal(_corners[i]);
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
                new Vector3(_planeSize + 2f * t, h, t), render: _wallsVisible);
            CreateWall("Wall_South", new Vector3(0f, h * 0.5f, -half - t * 0.5f),
                new Vector3(_planeSize + 2f * t, h, t), render: _wallsVisible);
            CreateWall("Wall_East", new Vector3(+half + t * 0.5f, h * 0.5f, 0f),
                new Vector3(t, h, _planeSize), render: _wallsVisible);
            CreateWall("Wall_West", new Vector3(-half - t * 0.5f, h * 0.5f, 0f),
                new Vector3(t, h, _planeSize), render: _wallsVisible);
        }

        /// <summary>Creates a wall/obstacle cube collider, rendered or collider-only.</summary>
        /// <param name="render">
        /// <b>Whether this box is drawn. The BoxCollider is kept either way</b> — that is
        /// what blocks the chicken's CharacterController, and dropping the renderer is
        /// exactly how the invisible boundary has always worked.
        /// <list type="bullet">
        ///   <item><b>Boundary walls</b> pass <see cref="_wallsVisible"/> (false by default):
        ///         the hand-placed fence in <c>MapProps</c> IS the visible boundary.</item>
        ///   <item><b>Pinwheel arms</b> pass <c>false</c> for the same reason. Each arm bearing
        ///         carries four hand-placed <c>wall_segment</c> props (<c>InnerWall_N_0..3</c>).
        ///         Until 2026-08-19 the generator also drew an untextured grey slab on top of
        ///         them, which won the depth test and reduced the finished art to a pair of
        ///         visible post-tops — the walls read as unbuilt geometry. Same defect the
        ///         boundary had already solved; same fix.</item>
        ///   <item><b>Scatter cover</b> passes <c>true</c>. It has no hand-placed art, so if the
        ///         generator does not draw it, nothing does.</item>
        /// </list>
        /// The caller assigns the tinted material after this returns, so no material handling
        /// happens on the rendered path here.
        /// </param>
        private GameObject CreateWall(string name, Vector3 localPosition, Vector3 size, bool render)
        {
            var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wall.name = name;
            wall.transform.SetParent(GeometryRoot, worldPositionStays: false);
            wall.transform.localPosition = localPosition;
            wall.transform.localScale = size;

            if (!render)
            {
                var mr = wall.GetComponent<MeshRenderer>();
                if (mr != null) mr.enabled = false;
            }

            return wall;
        }

        // ---- Interior terrain + NavMesh ----------------------------------------

        /// <summary>Circumscribed radius of a pile footprint.</summary>
        private static float FootprintRadius(Vector2 size) =>
            0.5f * Mathf.Sqrt(size.x * size.x + size.y * size.y);

        private static readonly int SColorId     = Shader.PropertyToID("_Color");
        private static readonly int SBaseColorId = Shader.PropertyToID("_BaseColor");

        /// <summary>
        /// The one shared terrain material, created on first use. This was an array indexed
        /// by <see cref="ObstacleClass"/> until 2026-08-19; the shipped map has only ever
        /// contained a single class, so two of its three slots were never filled.
        /// </summary>
        private Material _terrainMaterial;

        /// <summary>
        /// Builds the arena's permanent interior terrain: the eight pinwheel arms of
        /// GDD 3.4, plus <see cref="SectorScatter"/>'s 4-fold-symmetric cover.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Terrain is LOCAL geometry on every peer, so online layouts are seeded from the
        /// session name (same determinism trick as MatchBootstrapper's corner permutation);
        /// solo just rolls a fresh layout each match. Arms are built before scatter, and
        /// scatter draws its full sample before any rejection test, so the RNG stream
        /// advances identically on every peer. Nothing here may branch on peer-local state
        /// or the maps desync.
        /// </para>
        /// <para>
        /// <b>Arms are collider-only; scatter renders.</b> See <see cref="CreateWall"/>'s
        /// <c>render</c> parameter for why.
        /// </para>
        /// </remarks>
        private void BuildInteriorObstacles()
        {
            bool online = _selection != null && _selection.Mode != SessionMode.Solo;
            string sessionName = online ? _selection.SessionName : null;
            int seed = online ? SessionNameSeed(sessionName) : new System.Random().Next();

            float half = _planeSize * 0.5f;
            float centerKeepClear = FootprintRadius(_centerPileFootprint);
            var keepClearDiscs = ComputeBaseAndPileKeepClearDiscs();

            // _wallThickness is genuinely consumed by SolveHubPlazaRadius: it sets both the
            // ring around the centre pile and the radius at which adjacent arms stop
            // pinching, BOTH measured surface-to-surface. Until 2026-08-19 the parameter was
            // accepted and never read while a comment here asserted the opposite, so a
            // thicker arm silently ate the clearance the plaza exists to guarantee.
            var segments = PinwheelLayout.Build(
                half, _standardObstacleCount, seed, centerKeepClear, _wallThickness, keepClearDiscs);

            int armsBuilt = 0;
            for (int i = 0; i < segments.Length; i++)
            {
                var seg = segments[i];
                if (seg.A == seg.B) continue;   // clipped to nothing - don't build a husk

                Vector2 delta = seg.B - seg.A;
                Vector2 center2D = seg.A + delta * 0.5f;

                var go = CreateWall(
                    $"Terrain_{seg.Class}_{i}",
                    new Vector3(center2D.x, _interiorWallHeight * 0.5f, center2D.y),
                    new Vector3(delta.magnitude, _interiorWallHeight, _wallThickness),
                    render: false);

                go.transform.localRotation = Quaternion.Euler(0f, -Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg, 0f);
                go.AddComponent<TerrainObstacle>().SetClass(seg.Class);
                armsBuilt++;
            }

            var scatter = SectorScatter.Build(
                half, seed, BuildScatterSettings(), segments, _wallThickness, centerKeepClear, keepClearDiscs);

            foreach (var box in scatter.Boxes)
            {
                var go = CreateWall(
                    $"Terrain_{box.Role}_{ScatterName(box)}",
                    new Vector3(box.Center.x, _interiorWallHeight * 0.5f, box.Center.y),
                    new Vector3(box.Span, _interiorWallHeight, box.Depth),
                    render: true);

                go.transform.localRotation = Quaternion.Euler(0f, -box.YawDegrees, 0f);

                // Every piece of shipped terrain is tagged Standard. Traversal is gated by
                // SPAN (GDD 3.5), not by a class, so inventing a second tag here would
                // encode a distinction the design has deleted.
                go.AddComponent<TerrainObstacle>().SetClass(ObstacleClass.Standard);

                var mr = go.GetComponent<MeshRenderer>();
                if (mr != null)
                {
                    var mat = EnsureTerrainMaterial(mr.sharedMaterial);
                    if (mat != null) mr.sharedMaterial = mat;
                }
            }

            string seedNote = online ? $"session '{sessionName}' (seed {seed})" : $"solo-random (seed {seed})";
            _log?.Info(Source,
                $"Interior terrain: {armsBuilt}/{segments.Length} pinwheel arms (collider-only) + " +
                $"{scatter.Boxes.Length} scatter obstacles " +
                $"({scatter.CoverPlacedPerSector} cover + {scatter.BarrierPlacedPerSector} barrier per sector, x4). " +
                $"Seed={seedNote}.");

            if (scatter.UnderPlaced)
            {
                // Best-effort placement that silently under-delivers is exactly how a density
                // change looks like it landed while nothing actually moved. Say so.
                _log?.Warn(Source,
                    $"Scatter under-placed: asked for {scatter.CoverRequestedPerSector} cover + " +
                    $"{scatter.BarrierRequestedPerSector} barrier per sector, fitted " +
                    $"{scatter.CoverPlacedPerSector} + {scatter.BarrierPlacedPerSector}. " +
                    "The arena cannot hold that density at the current clearance - lower the counts, " +
                    "or raise _obstaclePlacementAttempts if you believe the budget is the limit. " +
                    "Clearances were NOT relaxed.");
            }
        }

        /// <summary>
        /// Stable per-object name suffix - the XZ position, so a diff of the baked scene is
        /// readable and an object keeps its identity across re-bakes of the same seed.
        /// </summary>
        private static string ScatterName(in ScatterBox box) =>
            $"{box.Center.x:0.0}_{box.Center.y:0.0}".Replace('-', 'n').Replace('.', 'p');

        private ScatterSettings BuildScatterSettings() => new ScatterSettings(
            _coverPerSector, _coverSpanRange,
            _barrierPerSector, _barrierSpanRange, _wallThickness,
            _scatterClearance, _obstaclePlacementAttempts);

        /// <summary>
        /// No-build discs the interior terrain must be pushed clear of, beyond the centre
        /// pile: the four bases plus every personal and contested food pile.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>Base discs are centred on the SPAWN POINT, not the raw corner</b> (fixed
        /// 2026-08-19). They used to sit on <c>_corners[i]</c> - 36.274 m out - while the
        /// base itself spawns at <see cref="SpawnPointNominal"/>, 30.833 m out: a 5.44 m
        /// error, and the exact "one thing under two parameterisations" class of bug that
        /// produced the 2026-08-14 pile-position failure. It was benign only because the
        /// arms clear either position by ~11.8 m perpendicular; scatter cover is placed far
        /// closer to the bases, so it stops being benign. Pinned by
        /// <c>MapClearanceTests.BaseKeepClearDiscs_AreCentredOnTheBases_NotTheRawCorners</c>.
        /// </para>
        /// <para>
        /// Piles get their per-match XZ jitter (<see cref="JitterXZ"/>) only once
        /// <see cref="SpawnFoodPiles"/> runs, which is after interior terrain is built - so
        /// this uses each pile's NOMINAL (pre-jitter) position and inflates its radius by
        /// <see cref="_pilePositionJitter"/> to cover every position the pile could actually
        /// land at, plus <see cref="PinwheelLayout.PileArmBuffer"/> so a stocked pile can
        /// never end up flush against terrain.
        /// </para>
        /// </remarks>
        private KeepClearDisc[] ComputeBaseAndPileKeepClearDiscs()
        {
            float personalRadius = FootprintRadius(_personalPileFootprint) + _pilePositionJitter + PinwheelLayout.PileArmBuffer;
            float contestedRadius = FootprintRadius(_contestedPileFootprint) + _pilePositionJitter + PinwheelLayout.PileArmBuffer;

            var discs = new KeepClearDisc[_corners.Length * 3]; // base + personal + contested per corner
            int idx = 0;
            for (int i = 0; i < _corners.Length; i++)
            {
                var baseNominal = SpawnPointNominal(_corners[i]);
                discs[idx++] = new KeepClearDisc(new Vector2(baseNominal.x, baseNominal.z), BaseKeepClearRadius);

                var personalNominal = PersonalPileNominal(_corners[i], _personalPileInset);
                discs[idx++] = new KeepClearDisc(new Vector2(personalNominal.x, personalNominal.z), personalRadius);

                var contestedNominal = ContestedPileNominal(_corners[i], _corners[(i + 1) % _corners.Length], _contestedEdgeInset);
                discs[idx++] = new KeepClearDisc(new Vector2(contestedNominal.x, contestedNominal.z), contestedRadius);
            }
            return discs;
        }

        /// <summary>
        /// Returns the one shared terrain material, creating it on first use from
        /// <c>_wallMaterial</c> (or the ground material) and tinting it.
        /// Deliberately NOT <c>mr.material</c>: that clones a material per object, which
        /// breaks batching and adds GC churn on the Android target. One shared material
        /// keeps the whole scatter set batchable.
        /// </summary>
        private Material EnsureTerrainMaterial(Material primitiveDefault)
        {
            if (_terrainMaterial != null) return _terrainMaterial;

            // Use _groundMaterial as a URP-compatible fallback if _wallMaterial is null,
            // to avoid pulling in the built-in Standard shader primitiveDefault.
            var template = _wallMaterial != null ? _wallMaterial :
                           (_groundMaterial != null ? _groundMaterial : primitiveDefault);
            if (template == null)
            {
                // Untinted grey terrain against grey ground is a readability bug, so surface
                // it instead of shipping a silently colourless map.
                _log?.Warn(Source, "No material template for interior terrain " +
                    "(_wallMaterial unassigned and the primitive has no default material) - " +
                    "scatter cover will render untinted and will not read as terrain.");
                return null;
            }
            bool templateIsGroundFallback = _wallMaterial == null && template == _groundMaterial;

            var mat = new Material(template)
            {
                name = "TerrainObstacle",
                hideFlags = HideFlags.DontSave,
            };

            // The ground-fallback path clones _groundMaterial, which carries the grass
            // albedo texture - a flat colour multiplied over a grass texture still reads
            // as "grass texture", not as a distinct tinted block. Scatter is meant to read
            // as built terrain, so strip the base map when that's the template in play.
            // Only that path: a deliberately assigned _wallMaterial is left untouched, since
            // its texture may be an intentional design choice, not a borrowed fallback.
            if (templateIsGroundFallback)
            {
                if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", null);
                if (mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", null);
            }

            // URP Lit exposes _BaseColor; keep _Color set too for any Built-in-style
            // material someone drops into _wallMaterial.
            if (mat.HasProperty(SBaseColorId)) mat.SetColor(SBaseColorId, _interiorWallColor);
            if (mat.HasProperty(SColorId)) mat.SetColor(SColorId, _interiorWallColor);

            _terrainMaterial = mat;
            return mat;
        }

        private void DestroyObstacleMaterials()
        {
            if (_terrainMaterial == null) return;
            Destroy(_terrainMaterial);
            _terrainMaterial = null;
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

            // Personal doorstep islands — 4 piles on the corner diagonals.
            for (int i = 0; i < _corners.Length; i++)
            {
                var pos = PersonalPileNominal(_corners[i], _personalPileInset) + JitterXZ();
                SpawnPile(runner, pilePrefab, pos, _personalPileAmount, _personalPileFootprint);
                loggedCoords.AppendLine($"Personal {i}: ({pos.x:F2}, {pos.z:F2}) | Food: {_personalPileAmount:0.00}");
            }

            // Contested islands — 4 piles on the edge midpoints.
            for (int i = 0; i < _corners.Length; i++)
            {
                var pos = ContestedPileNominal(_corners[i], _corners[(i + 1) % _corners.Length], _contestedEdgeInset)
                          + JitterXZ();
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

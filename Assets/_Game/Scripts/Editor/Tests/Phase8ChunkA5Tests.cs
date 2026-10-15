using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CluckWars.Tests
{
    /// <summary>
    /// Phase 8 A5 "Open lanes": the shipped <c>Map.unity</c> dressing never blocks or clutters a
    /// lane. Solid dressing stays clear of the pinwheel arm tips (the rim openings and hub
    /// mouths), the feed silos live outside the arena, the scarecrows are 4-fold symmetric,
    /// walk-through and clear of the piles, and every other walk-through prop inside the fence
    /// hugs an arm wall instead of standing in an open lane.
    /// </summary>
    /// <remarks>
    /// Props are identified by MESH (<c>feed_silo</c>, <c>scarecrow</c>, <c>wall_segment</c>), not by
    /// GameObject name, so renaming or re-parenting dressing can't silently drop it from a check.
    /// Footprints are measured from the props' mesh vertices in world space — the same thing the
    /// camera sees — and the arm walls from their baked colliders (<c>Terrain_Standard_*</c>).
    /// None of these checks read <c>MapGenerator</c>/<c>PinwheelLayout</c>: the scene is the subject.
    /// </remarks>
    public class Phase8ChunkA5Tests
    {
        internal const string MapScene = "Assets/_Game/Scenes/Map.unity";

        /// <summary>No collider-bearing dressing within this XZ distance of either end of an arm.</summary>
        private const float ArmTipClearance = 4f;
        /// <summary>Silos must sit at least this far out on X or Z (outside the fence and the hedge).</summary>
        private const float SiloMinCoord = 28f;
        /// <summary>Scarecrow centre to the nearest pile footprint edge.</summary>
        private const float ScarecrowPileClearance = 4f;
        /// <summary>Max distance from an arm's centre line to the NEAR edge of a hugging prop's footprint.</summary>
        private const float HugMaxNearEdge = 0.65f;
        /// <summary>Footprint may touch the panel face but not cut into it by more than this.</summary>
        private const float PanelTolerance = 0.02f;
        /// <summary>Props within this distance of a base-zone disc edge are base furniture, not lane clutter.</summary>
        private const float BaseFurnitureMargin = 1f;

        private static readonly Regex ArmCollider = new Regex(@"^Terrain_Standard_([0-9]+)$");

        private string _originalScenePath;

        [SetUp]
        public void CaptureOpenScene() => _originalScenePath = SceneManager.GetActiveScene().path;

        [TearDown]
        public void RestoreOpenScene()
        {
            if (string.IsNullOrEmpty(_originalScenePath))
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            else
                EditorSceneManager.OpenScene(_originalScenePath, OpenSceneMode.Single);
        }

        // ---------------------------------------------------------------- scene model

        internal sealed class Arm
        {
            public int Index;
            public Vector3 Hub, Tip, Dir, Normal;
            public float Length, HalfThickness;
        }

        internal sealed class Map
        {
            public Transform Geometry, Props, Surroundings;
            public List<Arm> Arms;
            public float ArenaHalf;
            /// <summary>Pile footprints as XZ centre + half extents, from the baked PileZone markers.</summary>
            public List<(string Name, Vector2 Centre, Vector2 Half)> Piles;
            public List<(Vector2 Centre, float Radius)> BaseZones;
            /// <summary>Every placed prop: the transform that owns a mesh-bearing child (or itself).</summary>
            public List<Transform> PropRoots;
        }

        internal static Map LoadMap()
        {
            var scene = EditorSceneManager.OpenScene(MapScene, OpenSceneMode.Single);
            var roots = scene.GetRootGameObjects();
            Transform Root(string n)
            {
                var go = roots.FirstOrDefault(g => g.name == n);
                Assert.IsNotNull(go, $"Map.unity has no {n} root.");
                return go.transform;
            }

            var map = new Map { Geometry = Root("GeneratedMapGeometry"), Props = Root("MapProps"), Surroundings = Root("MapSurroundings") };

            map.Arms = new List<Arm>();
            foreach (Transform t in map.Geometry)
            {
                var m = ArmCollider.Match(t.name);
                if (!m.Success) continue;
                var box = t.GetComponent<BoxCollider>();
                Assert.IsNotNull(box, $"{t.name} has no BoxCollider.");
                Vector3 size = Vector3.Scale(box.size, t.lossyScale);
                bool alongX = size.x >= size.z;
                Vector3 axis = alongX ? t.right : t.forward;
                axis.y = 0f;
                axis.Normalize();
                Vector3 c = t.TransformPoint(box.center);
                c.y = 0f;
                float half = (alongX ? size.x : size.z) * 0.5f;
                Vector3 p1 = c - axis * half, p2 = c + axis * half;
                if (p1.magnitude > p2.magnitude) (p1, p2) = (p2, p1);
                Vector3 dir = (p2 - p1).normalized;
                map.Arms.Add(new Arm
                {
                    Index = int.Parse(m.Groups[1].Value), Hub = p1, Tip = p2, Dir = dir,
                    Normal = new Vector3(-dir.z, 0f, dir.x), Length = half * 2f,
                    HalfThickness = (alongX ? size.z : size.x) * 0.5f,
                });
            }
            Assert.AreEqual(8, map.Arms.Count, "The pinwheel has 8 arm colliders (Terrain_Standard_0..7).");

            var ground = map.Geometry.Find("GeneratedGround");
            Assert.IsNotNull(ground, "Map.unity has no GeneratedMapGeometry/GeneratedGround.");
            map.ArenaHalf = ground.lossyScale.x * 0.5f;

            map.Piles = map.Geometry.Cast<Transform>().Where(t => t.name.StartsWith("PileZone_"))
                .Select(t => (t.name, new Vector2(t.position.x, t.position.z), new Vector2(t.lossyScale.x, t.lossyScale.z) * 0.5f))
                .ToList();
            Assert.AreEqual(9, map.Piles.Count, "Expected 9 baked pile markers (centre + 4 personal + 4 contested).");

            map.BaseZones = map.Geometry.Cast<Transform>().Where(t => t.name.StartsWith("BaseZone_"))
                .Select(t => (new Vector2(t.position.x, t.position.z), t.lossyScale.x * 0.5f)).ToList();
            Assert.AreEqual(4, map.BaseZones.Count, "Expected 4 baked base-zone discs.");

            map.PropRoots = new[] { map.Props, map.Surroundings }
                .SelectMany(r => r.GetComponentsInChildren<MeshFilter>(true))
                .Where(mf => mf.sharedMesh != null)
                .Select(mf => mf.transform.parent != null && mf.transform.parent != map.Props && mf.transform.parent != map.Surroundings
                    ? mf.transform.parent : mf.transform)
                .Distinct()
                .ToList();
            return map;
        }

        internal static string MeshOf(Transform prop) =>
            prop.GetComponentsInChildren<MeshFilter>(true).Select(mf => mf.sharedMesh != null ? mf.sharedMesh.name : "").FirstOrDefault() ?? "";

        private static IEnumerable<Transform> WithMesh(Map map, string mesh) => map.PropRoots.Where(p => MeshOf(p) == mesh);

        internal static List<Vector3> FootprintXZ(Transform prop)
        {
            var pts = new List<Vector3>();
            foreach (var mf in prop.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf.sharedMesh == null) continue;
                var m = mf.transform.localToWorldMatrix;
                foreach (var v in mf.sharedMesh.vertices)
                {
                    var w = m.MultiplyPoint3x4(v);
                    w.y = 0f;
                    pts.Add(w);
                }
            }
            return pts;
        }

        private static float DistanceToBox(Vector2 p, Vector2 centre, Vector2 half)
        {
            float dx = Mathf.Max(0f, Mathf.Abs(p.x - centre.x) - half.x);
            float dz = Mathf.Max(0f, Mathf.Abs(p.y - centre.y) - half.y);
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        internal static string Fmt(Vector3 p) => $"({p.x:F2}, {p.z:F2})";

        // ---------------------------------------------------------------- (a)

        [Test]
        public void ShippedMap_NoSolidDressing_NearAnArmTip()
        {
            var map = LoadMap();
            var tips = map.Arms.SelectMany(a => new[] { (a.Index, End: "hub", P: a.Hub), (a.Index, End: "rim", P: a.Tip) }).ToList();

            var offenders = new List<string>();
            foreach (var col in new[] { map.Props, map.Surroundings }.SelectMany(r => r.GetComponentsInChildren<Collider>(true)))
            {
                if (col.isTrigger) continue;
                // World XZ corners of the collider (exact for boxes, AABB otherwise).
                var corners = new List<Vector3>();
                if (col is BoxCollider box)
                {
                    for (int c = 0; c < 8; c++)
                        corners.Add(col.transform.TransformPoint(box.center + Vector3.Scale(box.size * 0.5f, new Vector3(
                            (c & 1) == 0 ? -1 : 1, (c & 2) == 0 ? -1 : 1, (c & 4) == 0 ? -1 : 1))));
                }
                else
                {
                    var b = col.bounds;
                    corners.Add(b.min);
                    corners.Add(b.max);
                }
                float minX = corners.Min(p => p.x), maxX = corners.Max(p => p.x);
                float minZ = corners.Min(p => p.z), maxZ = corners.Max(p => p.z);
                var centre = new Vector2((minX + maxX) * 0.5f, (minZ + maxZ) * 0.5f);
                var half = new Vector2((maxX - minX) * 0.5f, (maxZ - minZ) * 0.5f);

                foreach (var tip in tips)
                {
                    float d = DistanceToBox(new Vector2(tip.P.x, tip.P.z), centre, half);
                    if (d < ArmTipClearance)
                        offenders.Add($"{col.name} ({col.GetType().Name}) is {d:F2} m from arm {tip.Index}'s {tip.End} end {Fmt(tip.P)}");
                }
            }
            Assert.IsEmpty(offenders, "Solid dressing plugs an arm opening:\n" + string.Join("\n", offenders));
        }

        // ---------------------------------------------------------------- (b)

        [Test]
        public void ShippedMap_FeedSilos_StandOutsideTheArena()
        {
            var map = LoadMap();
            var silos = WithMesh(map, "feed_silo").ToList();
            Assert.GreaterOrEqual(silos.Count, 3, "Expected the barn silo plus the two relocated feed silos.");
            foreach (var silo in silos)
            {
                var p = silo.position;
                Assert.GreaterOrEqual(Mathf.Max(Mathf.Abs(p.x), Mathf.Abs(p.z)), SiloMinCoord,
                    $"{silo.name} at {Fmt(p)} is inside the arena/hedge band; silos belong in the surroundings.");
                float innermost = FootprintXZ(silo).Min(v => Mathf.Max(Mathf.Abs(v.x), Mathf.Abs(v.z)));
                Assert.Greater(innermost, map.ArenaHalf, $"{silo.name}'s footprint reaches inside the fence (innermost |coord| {innermost:F2}).");
            }
        }

        // ---------------------------------------------------------------- (c)

        [Test]
        public void ShippedMap_Scarecrows_AreFourFold_WalkThrough_AndClearOfPiles()
        {
            var map = LoadMap();
            var crows = WithMesh(map, "scarecrow").ToList();
            Assert.AreEqual(4, crows.Count, "One scarecrow per side of the arena.");

            foreach (var s in crows)
            {
                Assert.IsEmpty(s.GetComponentsInChildren<Collider>(true),
                    $"{s.name} has a collider: scarecrows are walk-through dressing (no NavMesh/JumpResolver blockers).");

                var p = new Vector2(s.position.x, s.position.z);
                foreach (var pile in map.Piles)
                {
                    float d = DistanceToBox(p, pile.Centre, pile.Half);
                    Assert.GreaterOrEqual(d, ScarecrowPileClearance, $"{s.name} at {Fmt(s.position)} is {d:F2} m from {pile.Name}'s edge.");
                }

                Assert.Less(FootprintXZ(s).Max(v => Mathf.Max(Mathf.Abs(v.x), Mathf.Abs(v.z))), map.ArenaHalf,
                    $"{s.name} pokes through the fence.");

                // 90° symmetry: rotating this scarecrow's pose by -90° yaw lands exactly on another one.
                var rot = Quaternion.Euler(0f, -90f, 0f);
                Vector3 target = rot * s.position;
                float targetYaw = s.eulerAngles.y - 90f;
                var match = crows.FirstOrDefault(o => o != s
                    && Vector2.Distance(new Vector2(o.position.x, o.position.z), new Vector2(target.x, target.z)) < 0.05f
                    && Mathf.Abs(Mathf.DeltaAngle(o.eulerAngles.y, targetYaw)) < 1f);
                Assert.IsNotNull(match, $"{s.name} at {Fmt(s.position)} yaw {s.eulerAngles.y:F1} has no 90°-rotated twin at {Fmt(target)} yaw {Mathf.Repeat(targetYaw, 360f):F1}.");
            }
        }

        // ---------------------------------------------------------------- (d)

        [Test]
        public void ShippedMap_WalkThroughClutter_HugsAnArmWall()
        {
            var map = LoadMap();
            var offenders = new List<string>();
            int checkedCount = 0;

            foreach (var prop in map.PropRoots)
            {
                string mesh = MeshOf(prop);
                if (mesh.StartsWith("wall_segment") || mesh == "scarecrow") continue; // fence/arm panels (incl. wall_segment_inner); scarecrows have their own rule
                if (prop.GetComponentsInChildren<Collider>(true).Length > 0) continue; // solid dressing: covered by the arm-tip rule
                var p = prop.position;
                var foot = FootprintXZ(prop);
                float spanX = foot.Max(v => v.x) - foot.Min(v => v.x), spanZ = foot.Max(v => v.z) - foot.Min(v => v.z);
                if (Mathf.Max(spanX, spanZ) > map.ArenaHalf) continue;                   // ground / hedge shells
                if (Mathf.Max(Mathf.Abs(p.x), Mathf.Abs(p.z)) >= map.ArenaHalf) continue; // outside the fence
                var p2 = new Vector2(p.x, p.z);
                if (map.BaseZones.Any(b => Vector2.Distance(p2, b.Centre) <= b.Radius + BaseFurnitureMargin)) continue; // base furniture

                checkedCount++;
                Arm best = null;
                float bestDist = float.MaxValue;
                foreach (var arm in map.Arms)
                {
                    float u = Mathf.Clamp(Vector3.Dot(new Vector3(p.x, 0f, p.z) - arm.Hub, arm.Dir), 0f, arm.Length);
                    float d = (arm.Hub + arm.Dir * u - new Vector3(p.x, 0f, p.z)).magnitude;
                    if (d < bestDist) { bestDist = d; best = arm; }
                }

                float along = Vector3.Dot(new Vector3(p.x, 0f, p.z) - best.Hub, best.Dir);
                float side = Mathf.Sign(Vector3.Dot(new Vector3(p.x, 0f, p.z) - best.Hub, best.Normal));
                // Signed offsets of every footprint vertex, flipped so the prop's side is positive.
                var offs = foot.Select(v => side * Vector3.Dot(v - best.Hub, best.Normal)).ToList();
                float nearEdge = offs.Min();

                float reach = foot.Max(v => Mathf.Max(Mathf.Abs(v.x), Mathf.Abs(v.z)));
                if (reach >= map.ArenaHalf)
                    offenders.Add($"{prop.name} {Fmt(p)}: pokes through the fence (|coord| {reach:F2} >= {map.ArenaHalf:F2}).");
                else if (along < 0f || along > best.Length)
                    offenders.Add($"{prop.name} {Fmt(p)}: beyond the end of arm {best.Index} (along {along:F2} of {best.Length:F2}).");
                else if (nearEdge > HugMaxNearEdge)
                    offenders.Add($"{prop.name} {Fmt(p)}: footprint edge {nearEdge:F2} m off arm {best.Index}'s centre line (> {HugMaxNearEdge}) — standing in an open lane.");
                else if (nearEdge < best.HalfThickness - PanelTolerance)
                    offenders.Add($"{prop.name} {Fmt(p)}: cuts {best.HalfThickness - nearEdge:F2} m into arm {best.Index}'s wall.");
            }

            Assert.Greater(checkedCount, 20, "Expected the arm-side dressing (InnerProps + fence dressing) to be checked.");
            Assert.IsEmpty(offenders, "Walk-through clutter must hug an arm wall:\n" + string.Join("\n", offenders));
        }
        // ---------------------------------------------------------------- (e) Phase 8 C9

        /// <summary>Dressing whose top is above this height (m) counts as "tall": it hides a chicken and reads as a blocker.</summary>
        private const float TallDressingHeight = 1.0f;
        /// <summary>So a 1.00 m prop (milk churn, hedge top) is not "taller than 1 m" by float noise.</summary>
        private const float TallTolerance = 0.02f;

        /// <summary>
        /// C9: tall dressing (scarecrows, coops, anything over 1 m) stays at least <see cref="ArmTipClearance"/> from
        /// both ends of every arm, collider or not - a walk-through scarecrow in a rim opening still hides chickens
        /// and reads as a plug. Distance is measured to each mesh's oriented box (its local bounds placed in world
        /// space), so a rotated prop is neither inflated (world AABB) nor shrunk to its pivot.
        /// </summary>
        [Test]
        public void ShippedMap_TallDressing_StaysClearOfArmTips_ColliderOrNot()
        {
            var map = LoadMap();
            var tips = map.Arms.SelectMany(a => new[] { (a.Index, End: "hub", P: a.Hub), (a.Index, End: "rim", P: a.Tip) }).ToList();
            var offenders = new List<string>();
            int tall = 0;
            foreach (var prop in map.PropRoots)
            {
                if (MeshOf(prop).StartsWith("wall_segment")) continue; // the fence / arm panels themselves
                var foot = FootprintXZ(prop);
                float span = Mathf.Max(foot.Max(v => v.x) - foot.Min(v => v.x), foot.Max(v => v.z) - foot.Min(v => v.z));
                if (span > map.ArenaHalf) continue; // ground / hedge shells
                var filters = prop.GetComponentsInChildren<MeshFilter>(true)
                    .Where(f => f.sharedMesh != null && f.GetComponent<Renderer>() != null).ToList();
                if (filters.Count == 0) continue;
                float top = filters.Max(f => f.GetComponent<Renderer>().bounds.max.y);
                if (top <= TallDressingHeight + TallTolerance) continue;
                tall++;
                foreach (var tip in tips)
                {
                    float d = filters.Min(f => DistanceToOrientedBoundsXZ(f, tip.P));
                    if (d < ArmTipClearance)
                        offenders.Add($"{prop.name} ({MeshOf(prop)}, {top:F2} m tall) is {d:F2} m from arm {tip.Index}'s {tip.End} end {Fmt(tip.P)}");
                }
            }
            Assert.GreaterOrEqual(tall, 8, "Expected at least the 4 scarecrows and 4 coops to be checked as tall dressing.");
            Assert.IsEmpty(offenders, "Tall dressing stands in an arm opening:\n" + string.Join("\n", offenders));
        }

        /// <summary>XZ distance from a point to a mesh's local bounds box placed in world space (exact for an upright, unsheared transform).</summary>
        private static float DistanceToOrientedBoundsXZ(MeshFilter f, Vector3 point)
        {
            var b = f.sharedMesh.bounds;
            var m = f.transform.localToWorldMatrix;
            Vector3 c = m.MultiplyPoint3x4(b.center);
            point.y = c.y;
            float sq = 0f;
            for (int i = 0; i < 3; i++)
            {
                Vector3 axis = m.GetColumn(i);
                float len = axis.magnitude;
                if (len < 1e-6f) continue;
                axis /= len;
                float d = Mathf.Abs(Vector3.Dot(point - c, axis)) - b.extents[i] * len;
                if (d > 0f) sq += d * d;
            }
            return Mathf.Sqrt(sq);
        }

        // ---------------------------------------------------------------- (f) Phase 8 C12

        /// <summary>A dressing mesh may touch an arm-wall panel but not cut into it by more than this.</summary>
        private const float WallPenetrationTolerance = 0.02f;

        /// <summary>
        /// C12: no InnerProp_* / Dress_* mesh pokes into an arm wall (any InnerWall_* panel, end posts included).
        /// </summary>
        /// <remarks>
        /// World AABBs are useless here: a prop yawed 67.5 degrees has an AABB up to ~40 % wider than itself, and so
        /// has the panel, so AABB overlap flags props that sit centimetres clear. Instead this is a separating-axis
        /// test between the two ACTUAL vertex clouds (i.e. their convex hulls) on 15 axes: the panel's 3 local axes,
        /// the prop's 3 local axes and their 9 cross products. If any axis separates them there is no overlap;
        /// otherwise the smallest interval overlap is the penetration depth. The hulls are never larger than the
        /// oriented boxes, so a prop flush against a panel face or standing past the end post passes, while
        /// wheelbarrow handles through the planks or a crate corner inside a post fail.
        /// </remarks>
        [Test]
        public void ShippedMap_Dressing_DoesNotPokeIntoArmWalls()
        {
            var map = LoadMap();
            var walls = map.Props.Cast<Transform>().Where(t => t.name.StartsWith("InnerWall_"))
                .SelectMany(t => t.GetComponentsInChildren<MeshFilter>(true))
                .Where(f => f.sharedMesh != null && f.GetComponent<MeshRenderer>() != null)
                .Select(f => (Name: f.transform.parent != null ? f.transform.parent.name : f.name, Hull: WorldVerts(f), Axes: AxesOf(f.transform)))
                .ToList();
            Assert.GreaterOrEqual(walls.Count, 40, "Expected the 8 arm-wall runs (7 panels each).");

            var props = map.Props.GetComponentsInChildren<Transform>(true)
                .Where(t => t.name.StartsWith("InnerProp_") || t.name.StartsWith("Dress_")).ToList();
            Assert.GreaterOrEqual(props.Count, 40, "Expected the InnerProp_* and Dress_* dressing.");

            var offenders = new List<string>();
            foreach (var prop in props)
            foreach (var f in prop.GetComponentsInChildren<MeshFilter>(true).Where(f => f.sharedMesh != null && f.GetComponent<Renderer>() != null))
            {
                var hull = WorldVerts(f);
                var axes = AxesOf(f.transform);
                foreach (var wall in walls)
                {
                    float depth = SatPenetration(hull, wall.Hull, axes, wall.Axes);
                    if (depth > WallPenetrationTolerance)
                        offenders.Add($"{prop.name} ({f.sharedMesh.name}) {Fmt(prop.position)} cuts {depth * 100f:F1} cm into {wall.Name}");
                }
            }
            Assert.IsEmpty(offenders, "Dressing pokes into an arm wall:\n" + string.Join("\n", offenders));
        }

        private static Vector3[] WorldVerts(MeshFilter f)
        {
            var m = f.transform.localToWorldMatrix;
            return f.sharedMesh.vertices.Select(v => m.MultiplyPoint3x4(v)).ToArray();
        }

        private static Vector3[] AxesOf(Transform t) => new[] { t.right, t.up, t.forward };

        /// <summary>Smallest interval overlap over the 15 SAT axes; &lt;= 0 means the hulls are separated.</summary>
        private static float SatPenetration(Vector3[] a, Vector3[] b, Vector3[] axesA, Vector3[] axesB)
        {
            var axes = new List<Vector3>(15);
            axes.AddRange(axesA);
            axes.AddRange(axesB);
            foreach (var x in axesA)
            foreach (var y in axesB)
            {
                var c = Vector3.Cross(x, y);
                if (c.sqrMagnitude > 1e-6f) axes.Add(c.normalized);
            }
            float best = float.MaxValue;
            foreach (var axis in axes)
            {
                Project(a, axis, out float aMin, out float aMax);
                Project(b, axis, out float bMin, out float bMax);
                float overlap = Mathf.Min(aMax, bMax) - Mathf.Max(aMin, bMin);
                if (overlap <= 0f) return overlap;
                best = Mathf.Min(best, overlap);
            }
            return best;
        }

        private static void Project(Vector3[] pts, Vector3 axis, out float min, out float max)
        {
            min = float.MaxValue;
            max = float.MinValue;
            foreach (var p in pts)
            {
                float d = Vector3.Dot(p, axis);
                if (d < min) min = d;
                if (d > max) max = d;
            }
        }
    }
}

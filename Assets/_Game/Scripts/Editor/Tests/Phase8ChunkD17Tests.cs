using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using CluckWars.EditorTools;
using CluckWars.Visuals;

namespace CluckWars.Tests
{
    /// <summary>
    /// Phase 8 D17 "Leftovers".
    /// <para>1. Nothing draws through the straw pad. The thin lines that cross the pad in the match-camera captures are the
    /// ability range overlay's cone outline (they survive with every DirtPaths_Base* hidden); the baked dirt paths all start
    /// outside the pad (<see cref="GroundDressingBaker.PathStartFromBase"/> 2.95 m, hub fork 4.1 m), and this pins that, plus the
    /// lift order pad &gt;= paths, ring above pad, as the safety net.</para>
    /// <para>2. The RENDERED <c>GeneratedGround</c> ends 0.1 m past the fence centre line (under the panel), so no lime strip
    /// shows in the fence-hedge gap. Scale, position and the BoxCollider (the floor, the arena hard limit, the NavMesh input) do
    /// not change.</para>
    /// </summary>
    public class Phase8ChunkD17Tests
    {
        private const string PlayerBasePrefab = "Assets/_Game/Prefabs/PlayerBase.prefab";
        private const float Eps = 1e-3f;

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

        [Test]
        public void BakedDirtPaths_NeverReachTheStrawPad()
        {
            var map = Phase8ChunkA5Tests.LoadMap();
            var ground = EditorSceneManager.GetActiveScene().GetRootGameObjects().First(g => g.name == GroundDressingBaker.GroundRootName);
            var filters = ground.GetComponentsInChildren<MeshFilter>(true).Where(f => f.name.StartsWith("DirtPaths_Base")).ToList();
            Assert.AreEqual(4, filters.Count);
            for (int k = 0; k < 4; k++)
            {
                var baseT = map.Geometry.Find($"BaseZone_{k}");
                var b = new Vector2(baseT.position.x, baseT.position.z);
                var f = filters.First(x => x.name == $"DirtPaths_Base{k}");
                var m = f.transform.localToWorldMatrix;
                float min = float.MaxValue;
                foreach (var v in f.sharedMesh.vertices)
                {
                    var w = m.MultiplyPoint3x4(v);
                    min = Mathf.Min(min, Vector2.Distance(new Vector2(w.x, w.z), b));
                }
                Assert.GreaterOrEqual(min, BasePadMesh.FloorRadius, $"{f.name}: a path vertex is {min:F2} m from base {k}, inside the {BasePadMesh.FloorRadius} m straw pad.");
            }
        }

        [Test]
        public void StrawPad_SitsOverThePaths_AndUnderItsRing()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerBasePrefab);
            Assert.IsNotNull(prefab, PlayerBasePrefab);
            var floor = prefab.GetComponentsInChildren<Transform>(true).First(t => t.name == "StrawFloor");
            var ring = prefab.GetComponentsInChildren<Transform>(true).First(t => t.name == "PlayerRing");
            float pathTop = GroundDressingBaker.PathLift + GroundDressingBaker.HubForkLift;
            Assert.GreaterOrEqual(floor.position.y, pathTop - Eps, "the pad must not sit under a path strip.");
            Assert.Greater(ring.position.y, floor.position.y, "the ring stays over the pad.");
        }

        [Test]
        public void GeneratedGround_RenderedBounds_EndUnderTheFence()
        {
            var map = Phase8ChunkA5Tests.LoadMap();
            var ground = map.Geometry.Find(ZoneMarkerBaker.GroundName);
            var r = ground.GetComponent<MeshRenderer>();
            var b = r.bounds;

            // The fence centre line per side, measured from the wall renderers (panels and posts are symmetric about it).
            float Line(string prefix, bool alongX, bool positive)
            {
                var rs = map.Props.Cast<Transform>().Where(t => t.name.StartsWith(prefix)).SelectMany(t => t.GetComponentsInChildren<Renderer>()).ToList();
                Assert.IsNotEmpty(rs, prefix);
                float lo = rs.Min(x => alongX ? x.bounds.min.x : x.bounds.min.z), hi = rs.Max(x => alongX ? x.bounds.max.x : x.bounds.max.z);
                Assert.AreEqual(positive ? 1f : -1f, Mathf.Sign(lo + hi), prefix + " is on the wrong side.");
                return Mathf.Abs((lo + hi) * 0.5f);
            }

            var sides = new[]
            {
                (name: "E", line: Line("Wall_E_", true, true), ground: b.max.x),
                (name: "W", line: Line("Wall_W_", true, false), ground: -b.min.x),
                (name: "N", line: Line("Wall_N_", false, true), ground: b.max.z),
                (name: "S", line: Line("Wall_S_", false, false), ground: -b.min.z),
            };
            foreach (var s in sides)
            {
                Assert.LessOrEqual(s.ground, s.line + ZoneMarkerBaker.GroundTrimBeyondFence + Eps, $"{s.name}: ground renders {s.ground - s.line:F3} m past the fence centre line.");
                Assert.GreaterOrEqual(s.ground, s.line + 0.05f, $"{s.name}: ground stops short of the fence ({s.ground - s.line:F3} m) and would leave a hole under it.");
            }
            Assert.AreEqual(ZoneMarkerBaker.GroundTrimHalf(ground.lossyScale.x), b.extents.x, Eps, "ground X extent");
            Assert.AreEqual(ZoneMarkerBaker.GroundTrimHalf(ground.lossyScale.x), b.extents.z, Eps, "ground Z extent");
        }

        [Test]
        public void GeneratedGround_FloorCollider_AndNavMeshInput_AreUnchanged()
        {
            var map = Phase8ChunkA5Tests.LoadMap();
            var ground = map.Geometry.Find(ZoneMarkerBaker.GroundName);
            Assert.AreEqual(new Vector3(51.3f, 1f, 51.3f), ground.localScale, "ground scale feeds ArenaHalf and the collider.");
            Assert.AreEqual(new Vector3(0f, -0.5f, 0f), ground.localPosition);
            Assert.AreEqual(25.65f, map.ArenaHalf, Eps, "arena hard limit");
            var box = ground.GetComponent<BoxCollider>();
            Assert.IsNotNull(box, "the floor collider");
            Assert.IsFalse(box.isTrigger);
            Assert.AreEqual(Vector3.zero, box.center);
            Assert.AreEqual(Vector3.one, box.size);
            Assert.AreEqual(25.65f, box.bounds.extents.x, Eps, "collider keeps the full floor");
            Assert.AreEqual(25.65f, box.bounds.extents.z, Eps);
            Assert.AreEqual(0f, box.bounds.max.y, Eps, "floor top at y 0");

            // The baked NavMesh still spans the floor (it is baked from this collider, never from the mesh).
            var tri = NavMesh.CalculateTriangulation();
            Assert.Greater(tri.vertices.Length, 0, "no baked NavMesh in Map.unity");
            float navMax = tri.vertices.Max(v => Mathf.Max(Mathf.Abs(v.x), Mathf.Abs(v.z)));
            Assert.LessOrEqual(navMax, map.ArenaHalf + Eps);
            Assert.Greater(navMax, 24f, "NavMesh no longer reaches the fence line.");
        }

        [Test]
        public void SurroundGrass_CoversTheTrimmedEdge_WithoutAVisibleStep()
        {
            var map = Phase8ChunkA5Tests.LoadMap();
            var ground = map.Geometry.Find(ZoneMarkerBaker.GroundName).GetComponent<MeshRenderer>().bounds;
            var surround = map.Surroundings.Find("SurroundGrass").GetComponent<MeshRenderer>().bounds;
            Assert.IsTrue(surround.Contains(new Vector3(ground.max.x, surround.center.y, ground.max.z)) && surround.Contains(new Vector3(ground.min.x, surround.center.y, ground.min.z)),
                "the surround grass must run under the whole trimmed edge.");
            var hedge = map.Surroundings.Find("ArenaHedge/Hedge_E").GetComponent<Renderer>().bounds;
            Assert.Greater(surround.max.x, hedge.max.x, "the surround grass ends before the hedge.");
            Assert.Greater(hedge.min.x, ground.max.x, "the hedge must stand outside the trimmed ground (the gap is surround grass).");
            float step = ground.max.y - surround.max.y;
            Assert.That(step, Is.InRange(0f, 0.02f), $"the ground top sits {step * 100f:F1} cm above the surround grass: more than a hairline step at the trimmed edge.");
        }

        [Test]
        public void TrimmedGroundMesh_KeepsTheTexelDensity_AndIsTheShippedAsset()
        {
            var map = Phase8ChunkA5Tests.LoadMap();
            var ground = map.Geometry.Find(ZoneMarkerBaker.GroundName);
            var mesh = ground.GetComponent<MeshFilter>().sharedMesh;
            Assert.AreEqual(ZoneMarkerBaker.GroundMeshAssetPath, AssetDatabase.GetAssetPath(mesh));

            float planeSize = ground.lossyScale.x;
            float f = ZoneMarkerBaker.GroundTrimHalf(planeSize) / (planeSize * 0.5f);
            Assert.Less(f, 1f);
            var fresh = ZoneMarkerBaker.BuildTrimmedGround(f);
            Assert.AreEqual(fresh.vertexCount, mesh.vertexCount, "baked ground mesh is stale - run Cluck Wars/Map/Apply Ground + Zone Fixes.");
            Assert.AreEqual(fresh.bounds.extents.x, mesh.bounds.extents.x, 1e-5f);

            // The top face spans UV 0..1 over the FULL floor, so one UV unit = one floor width: |u - 0.5| must equal |x| (unit cube space).
            var v = mesh.vertices; var uv = mesh.uv; var n = mesh.normals;
            int top = 0;
            for (int i = 0; i < v.Length; i++)
            {
                if (n[i].y < 0.99f) continue;
                top++;
                Assert.AreEqual(Mathf.Abs(v[i].x), Mathf.Abs(uv[i].x - 0.5f), 1e-5f, $"top vertex {i}: u");
                Assert.AreEqual(Mathf.Abs(v[i].z), Mathf.Abs(uv[i].y - 0.5f), 1e-5f, $"top vertex {i}: v");
            }
            Assert.AreEqual(4, top, "top face vertices");
        }
    }
}

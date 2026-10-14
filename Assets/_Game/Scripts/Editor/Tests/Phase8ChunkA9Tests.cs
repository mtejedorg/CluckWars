using System.Linq;
using CluckWars.EditorTools;
using CluckWars.Gameplay;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace CluckWars.Tests
{
    /// <summary>
    /// Phase 8 A9 "Cover cubes": in the shipped <c>Map.unity</c> every scatter cover
    /// (<c>GeneratedMapGeometry/Terrain_Cover_*</c>) keeps its solid collider, no longer renders its
    /// bare cube, and is drawn by a collider-free crate stack whose bounds ARE the collider - so
    /// players see exactly what blocks them.
    /// </summary>
    /// <remarks>
    /// Bounds are measured from the visual's actual renderers in world space and compared with the
    /// collider's world bounds - independently of <see cref="CoverBoxDresser"/>'s own maths.
    /// </remarks>
    public class Phase8ChunkA9Tests
    {
        private const string MapScene = "Assets/_Game/Scenes/Map.unity";
        private const float FootprintTolerance = 0.05f;
        private const float HeightTolerance = 0.10f;

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
        public void SolveGrid_PicksTheNearestWholeCount_NeverZero()
        {
            var crate = new Vector3(0.905f, 0.730f, 0.903f);
            Assert.AreEqual(new Vector3Int(1, 2, 2), CoverBoxDresser.SolveGrid(new Vector3(1.32f, 1.40f, 1.62f), crate));
            Assert.AreEqual(new Vector3Int(1, 1, 1), CoverBoxDresser.SolveGrid(new Vector3(0.2f, 0.2f, 0.2f), crate));
        }

        [Test]
        public void ShippedMap_EveryCover_IsDrawnByACrateStackMatchingItsCollider()
        {
            var scene = EditorSceneManager.OpenScene(MapScene, OpenSceneMode.Single);
            var geometry = scene.GetRootGameObjects().FirstOrDefault(g => g.name == "GeneratedMapGeometry");
            Assert.IsNotNull(geometry, "Map.unity has no GeneratedMapGeometry root.");

            var covers = geometry.transform.Cast<Transform>()
                .Where(t => t.name.StartsWith(CoverBoxDresser.CoverPrefix)).ToList();
            Assert.That(covers.Count, Is.GreaterThan(0), "Map.unity has no Terrain_Cover_* boxes.");
            Assert.AreEqual(0, covers.Count % 4, "Scatter cover is 4-fold symmetric.");

            foreach (var cover in covers)
            {
                var cols = cover.GetComponents<Collider>();
                Assert.AreEqual(1, cols.Length, $"{cover.name}: exactly one collider on the cover itself.");
                var box = cols[0] as BoxCollider;
                Assert.IsNotNull(box, $"{cover.name}: the cover's collider must be a BoxCollider.");
                Assert.IsTrue(box.enabled, $"{cover.name}: BoxCollider disabled.");
                Assert.IsFalse(box.isTrigger, $"{cover.name}: BoxCollider became a trigger.");
                Assert.IsNotNull(cover.GetComponent<TerrainObstacle>(), $"{cover.name}: lost its TerrainObstacle tag.");

                var own = cover.GetComponent<MeshRenderer>();
                Assert.IsTrue(own == null || !own.enabled, $"{cover.name}: still renders its bare cube.");

                var visual = cover.Find(CoverBoxDresser.VisualName);
                Assert.IsNotNull(visual, $"{cover.name}: no {CoverBoxDresser.VisualName} child.");
                Assert.AreEqual(0, visual.GetComponentsInChildren<Collider>(true).Length,
                    $"{cover.name}: the visual must not add colliders (NavMesh/traversal read colliders).");

                var rs = visual.GetComponentsInChildren<Renderer>(true);
                Assert.That(rs.Length, Is.GreaterThan(0), $"{cover.name}: visual has no renderers.");
                foreach (var r in rs)
                {
                    Assert.IsTrue(r.enabled && r.gameObject.activeInHierarchy, $"{cover.name}/{r.name}: renderer off.");
                    Assert.AreEqual(ShadowCastingMode.On, r.shadowCastingMode, $"{cover.name}/{r.name}: must cast shadows.");
                    Assert.IsTrue(r.gameObject.isStatic, $"{cover.name}/{r.name}: visual must be static.");
                }

                Bounds vb = rs[0].bounds;
                foreach (var r in rs) vb.Encapsulate(r.bounds);
                Bounds cb = box.bounds;

                // Compare in the cover's own (yawed) frame: transform the visual's corners into
                // box-local metres so a rotated box is not judged by its world AABB.
                Vector3 size = Vector3.Scale(box.size, cover.lossyScale);
                Vector3 min = Vector3.positiveInfinity, max = Vector3.negativeInfinity;
                foreach (var r in rs)
                {
                    var mf = r.GetComponent<MeshFilter>();
                    Assert.IsNotNull(mf, $"{cover.name}/{r.name}: no MeshFilter.");
                    foreach (var v in mf.sharedMesh.vertices)
                    {
                        Vector3 w = r.transform.TransformPoint(v);
                        Vector3 l = Vector3.Scale(cover.InverseTransformPoint(w) - box.center, cover.lossyScale);
                        min = Vector3.Min(min, l);
                        max = Vector3.Max(max, l);
                    }
                }
                Vector3 vs = max - min;
                Vector3 vc = (max + min) * 0.5f;

                Assert.That(vs.x, Is.EqualTo(size.x).Within(size.x * FootprintTolerance), $"{cover.name}: visual width vs collider.");
                Assert.That(vs.z, Is.EqualTo(size.z).Within(size.z * FootprintTolerance), $"{cover.name}: visual depth vs collider.");
                Assert.That(vs.y, Is.EqualTo(size.y).Within(size.y * HeightTolerance), $"{cover.name}: visual height vs collider.");
                Assert.That(vc.x, Is.EqualTo(0f).Within(size.x * FootprintTolerance), $"{cover.name}: visual off-centre on x.");
                Assert.That(vc.z, Is.EqualTo(0f).Within(size.z * FootprintTolerance), $"{cover.name}: visual off-centre on z.");
                Assert.That(min.y, Is.EqualTo(-size.y * 0.5f).Within(size.y * HeightTolerance), $"{cover.name}: visual does not sit on the ground.");

                // And the world AABBs agree too (catches a visual parented somewhere odd).
                Assert.That(Vector3.Distance(vb.center, cb.center), Is.LessThan(0.1f), $"{cover.name}: visual AABB centre drifted.");
            }
        }
    }
}

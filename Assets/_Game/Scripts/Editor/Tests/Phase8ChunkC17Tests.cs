using System.Linq;
using CluckWars.EditorTools;
using CluckWars.Gameplay;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CluckWars.Tests
{
    /// <summary>
    /// Phase 8 C17: the base zone discs (and, until C S1, the pile ones) are 64-sided (the built-in Cylinder has 20, which read
    /// as a polygon from the match camera). Same radius, height and winding as the primitive, so the
    /// existing scale / position maths is untouched.
    /// </summary>
    public class Phase8ChunkC17Tests
    {
        private const string MapScene = "Assets/_Game/Scenes/Map.unity";

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
        public void Build_SixtyFourSegments_HasTheExpectedTopology()
        {
            var m = ZoneDiscMesh.Build(64);
            // Two cap fans (64 tris each) + 64 side quads (2 tris each).
            Assert.AreEqual(64 * 4, m.triangles.Length / 3);
            // 2 cap centres + 2 x 64 cap rim points + 2 x 65 side points (the seam is split for UVs).
            Assert.AreEqual(2 + 2 * 64 + 2 * 65, m.vertexCount);
            Object.DestroyImmediate(m);
        }

        [Test]
        public void Build_MatchesTheBuiltInCylinderSize()
        {
            var m = ZoneDiscMesh.Build(64);
            var b = m.bounds;
            Assert.AreEqual(1f, b.size.x, 1e-3f);
            Assert.AreEqual(1f, b.size.z, 1e-3f);
            Assert.AreEqual(2f, b.size.y, 1e-3f);
            Assert.AreEqual(0f, b.center.magnitude, 1e-3f);
            Object.DestroyImmediate(m);
        }

        [Test]
        public void Build_WindingAgreesWithStoredNormals_EveryTriangle()
        {
            var m = ZoneDiscMesh.Build(64);
            var v = m.vertices; var n = m.normals; var t = m.triangles;
            for (int i = 0; i < t.Length; i += 3)
            {
                var faceNormal = Vector3.Cross(v[t[i + 1]] - v[t[i]], v[t[i + 2]] - v[t[i]]).normalized;
                var stored = (n[t[i]] + n[t[i + 1]] + n[t[i + 2]]).normalized;
                Assert.Greater(Vector3.Dot(faceNormal, stored), 0.9f, $"triangle {i / 3} is wound inside-out");
            }
            Object.DestroyImmediate(m);
        }

        [Test]
        public void MeshAsset_IsSixtyFourSided()
        {
            var m = AssetDatabase.LoadAssetAtPath<Mesh>(ZoneMarkerBaker.MeshAssetPath);
            Assert.IsNotNull(m, ZoneMarkerBaker.MeshAssetPath);
            Assert.AreEqual(64 * 4, m.triangles.Length / 3);
            var rim = m.vertices.Where(p => Mathf.Approximately(p.y, 1f) && p.sqrMagnitude > 0.1f)
                .Select(p => Mathf.RoundToInt(Mathf.Atan2(p.z, p.x) * Mathf.Rad2Deg * 10f) % 3600 + 3600).Select(a => a % 3600)
                .Distinct().Count();
            Assert.AreEqual(64, rim, "distinct rim points on the top face");
        }

        [Test]
        public void ShippedMap_EveryBaseZoneDisc_UsesTheSixtyFourSidedAsset_AtItsOriginalSize()
        {
            var scene = EditorSceneManager.OpenScene(MapScene, OpenSceneMode.Single);
            var geometry = scene.GetRootGameObjects().First(g => g.name == "GeneratedMapGeometry").transform;
            var zones = geometry.Cast<Transform>()
                .Where(t => t.name.StartsWith("BaseZone_")).ToList();
            // The nine PileZone_* discs moved to the irregular-oval mesh in Phase 8 C S1 (see Phase8ChunkS1Tests).
            Assert.AreEqual(4, zones.Count);

            var asset = AssetDatabase.LoadAssetAtPath<Mesh>(ZoneMarkerBaker.MeshAssetPath);
            foreach (var z in zones)
            {
                var mf = z.GetComponent<MeshFilter>();
                Assert.AreSame(asset, mf.sharedMesh, $"{z.name}: mesh");
                Assert.AreEqual(0.01f, z.localScale.y, 1e-6f, $"{z.name}: slab thickness unchanged");
                Assert.AreEqual(0.01f, z.localPosition.y, 1e-6f, $"{z.name}: lift unchanged");
                Assert.IsNull(z.GetComponent<Collider>(), $"{z.name}: zone markers stay collider-free");
            }
        }
    }
}

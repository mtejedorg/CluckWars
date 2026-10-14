using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CluckWars.Tests
{
    /// <summary>
    /// Phase 8 B4 "Inner-wall texel density": the arm walls are the fence panel squashed to 0.35
    /// in thickness, which squashed the texture on every face that spans the thickness (post tops,
    /// rail tops, ends). They use <c>wall_segment_inner</c>: the same vertices, with each such UV
    /// island compressed by 0.35 along the thickness direction so those faces sample the atlas at
    /// the same density as the rest. The outer fence keeps the original mesh.
    /// </summary>
    public class Phase8ChunkB4Tests
    {
        private const string MapScene = "Assets/_Game/Scenes/Map.unity";
        private const string SourceFbx = "Assets/Generated/wall_segment.fbx";
        private const string InnerMesh = "Assets/_Game/Art/Meshes/wall_segment_inner.asset";

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
        public void InnerMesh_IsTheFenceGeometry_WithUvsInsideTheAtlas()
        {
            var source = AssetDatabase.LoadAllAssetsAtPath(SourceFbx).OfType<Mesh>().FirstOrDefault();
            var inner = AssetDatabase.LoadAssetAtPath<Mesh>(InnerMesh);
            Assert.IsNotNull(source, SourceFbx + " has no mesh.");
            Assert.IsNotNull(inner, InnerMesh + " is missing.");
            Assert.AreEqual(source.vertexCount, inner.vertexCount, "Inner mesh must be the same geometry (regenerate it after re-exporting the fence).");
            CollectionAssert.AreEqual(source.triangles, inner.triangles, "Inner mesh triangles differ from the fence.");
            var sv = source.vertices; var iv = inner.vertices;
            for (int i = 0; i < sv.Length; i++)
                Assert.Less((sv[i] - iv[i]).magnitude, 1e-4f, $"Vertex {i} moved; only UVs may differ.");
            // Islands are only shrunk toward their own centroid, so the UVs never leave the source's UV range.
            var uv = inner.uv; var su = source.uv;
            float minU = su.Min(u => u.x), maxU = su.Max(u => u.x), minV = su.Min(u => u.y), maxV = su.Max(u => u.y);
            Assert.IsTrue(uv.All(u => u.x >= minU - 1e-4f && u.x <= maxU + 1e-4f && u.y >= minV - 1e-4f && u.y <= maxV + 1e-4f), "Inner UVs left the source UV range.");
            Assert.IsFalse(uv.SequenceEqual(source.uv), "Inner UVs are identical to the fence's - the density fix is missing.");
        }

        [Test]
        public void ShippedMap_ArmWallsUseInnerMesh_OuterFenceUsesTheOriginal()
        {
            var inner = AssetDatabase.LoadAssetAtPath<Mesh>(InnerMesh);
            var scene = EditorSceneManager.OpenScene(MapScene, OpenSceneMode.Single);
            var props = scene.GetRootGameObjects().First(g => g.name == "MapProps").transform;
            int innerCount = 0, outerCount = 0;
            foreach (Transform t in props)
            {
                bool isInner = t.name.StartsWith("InnerWall_"), isOuter = t.name.StartsWith("Wall_");
                if (!isInner && !isOuter) continue;
                foreach (var mf in t.GetComponentsInChildren<MeshFilter>(true))
                {
                    if (isInner) { innerCount++; Assert.AreSame(inner, mf.sharedMesh, $"{t.name} must use wall_segment_inner."); }
                    else { outerCount++; Assert.AreEqual("wall_segment", mf.sharedMesh.name, $"{t.name} must use the original fence mesh."); }
                }
            }
            Assert.Greater(innerCount, 40, "Expected the arm wall panels.");
            Assert.Greater(outerCount, 40, "Expected the outer fence panels.");
        }
    }
}

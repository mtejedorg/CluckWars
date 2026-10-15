using System.Linq;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CluckWars.Tests
{
    /// <summary>
    /// Phase 8 C10 "Crates mean cover": in the shipped <c>Map.unity</c> the crate mesh is the visual of the
    /// solid scatter covers (<c>GeneratedMapGeometry/Terrain_Cover_*</c>, A9) and nothing else. A loose crate
    /// in the dressing reads as cover a chicken can hide behind, but it is walk-through.
    /// </summary>
    public class Phase8ChunkC10Tests
    {
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

        private static bool UnderCover(Transform t)
        {
            for (var p = t; p != null; p = p.parent)
                if (p.name.StartsWith("Terrain_Cover_")) return true;
            return false;
        }

        private static string PathOf(Transform t) => t.parent == null ? t.name : PathOf(t.parent) + "/" + t.name;

        [Test]
        public void ShippedMap_CrateMesh_IsUsedOnlyByCoverVisuals()
        {
            var scene = EditorSceneManager.OpenScene(Phase8ChunkA5Tests.MapScene, OpenSceneMode.Single);
            var crates = scene.GetRootGameObjects()
                .SelectMany(g => g.GetComponentsInChildren<MeshFilter>(true))
                .Where(f => f.sharedMesh != null && f.sharedMesh.name == "crate")
                .ToList();

            Assert.GreaterOrEqual(crates.Count(f => UnderCover(f.transform)), 4,
                "Expected the crate stacks that dress the 4 Terrain_Cover_* boxes (A9).");
            var loose = crates.Where(f => !UnderCover(f.transform)).Select(f => PathOf(f.transform)).ToList();
            Assert.IsEmpty(loose, "Walk-through dressing uses the cover crate mesh (reads as cover):\n" + string.Join("\n", loose));
        }
    }
}

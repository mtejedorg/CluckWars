using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace CluckWars.Tests
{
    /// <summary>
    /// Phase 8 B3 "Tree LODs + far shadows": the surround oak has a billboard LOD1 that the
    /// orthographic match camera never selects (ortho LOD is distance-independent), and in the
    /// shipped <c>Map.unity</c> only the surround objects whose shadows can land in a match view
    /// still cast shadows (the barn, its silos and 25 of the 30 trees don't).
    /// </summary>
    /// <remarks>
    /// The set of shadow casters was chosen by rendering the match camera (ortho 5.6, pitch 30,
    /// all four corner yaws) along the fence with each surround object's shadow toggled: only
    /// Tree_6/10/12/17/29 changed more than ~50 px of a 320x180 frame. A new tree that should cast
    /// has to be added here deliberately.
    /// </remarks>
    public class Phase8ChunkB3Tests
    {
        private const string MapScene = "Assets/_Game/Scenes/Map.unity";
        private const string TreePrefab = "Assets/_Game/Prefabs/Props/Prop_oak_tree.prefab";
        /// <summary>Match camera half view height (MatchCamera._orthoSize).</summary>
        private const float MatchOrthoSize = 5.6f;
        private static readonly string[] ShadowCastingSurround = { "Tree_6", "Tree_10", "Tree_12", "Tree_17", "Tree_29" };

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
        public void OakTree_HasBillboardLod1_ThatTheMatchCameraNeverSelects()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(TreePrefab);
            Assert.IsNotNull(prefab, TreePrefab + " is missing.");
            var group = prefab.GetComponent<LODGroup>();
            Assert.IsNotNull(group, "Prop_oak_tree has no LODGroup.");
            var lods = group.GetLODs();
            Assert.AreEqual(2, lods.Length, "Expected LOD0 (mesh) + LOD1 (billboard), culled below LOD1.");
            Assert.IsTrue(lods[0].renderers.Length > 0 && lods[0].renderers.All(r => r != null && r.name != "Billboard"), "LOD0 must be the tree mesh.");
            Assert.AreEqual(1, lods[1].renderers.Length, "LOD1 must be the single billboard renderer.");
            var billboard = lods[1].renderers[0];
            Assert.AreEqual("Billboard", billboard.name);
            Assert.AreEqual(ShadowCastingMode.Off, billboard.shadowCastingMode, "The billboard must not cast (a flat quad's shadow is a sliver).");
            Assert.IsTrue(billboard.sharedMaterials.Length == 2 && billboard.sharedMaterials.All(m => m != null && m.IsKeywordEnabled("_ALPHATEST_ON")),
                "Billboard needs its two alpha-clipped view materials.");

            // Ortho LOD: relative height = world size / (2 * orthoSize). The smallest surround tree must
            // stay well above the LOD0 threshold or gameplay would show billboards.
            var scene = EditorSceneManager.OpenScene(MapScene, OpenSceneMode.Single);
            var trees = scene.GetRootGameObjects().First(g => g.name == "MapSurroundings").transform
                .Cast<Transform>().Where(t => t.name.StartsWith("Tree_")).ToList();
            Assert.Greater(trees.Count, 20, "Expected the surround trees.");
            float smallest = float.MaxValue;
            foreach (var t in trees)
            {
                var g = t.GetComponentInChildren<LODGroup>();
                Assert.IsNotNull(g, $"{t.name} lost the prefab's LODGroup.");
                smallest = Mathf.Min(smallest, g.size * g.transform.lossyScale.y);
            }
            float relative = smallest / (2f * MatchOrthoSize);
            Assert.Greater(relative, lods[0].screenRelativeTransitionHeight * 1.5f,
                $"Smallest tree ({smallest:F2} m) is only {relative:F2} of the match view; LOD0 threshold {lods[0].screenRelativeTransitionHeight} is too close.");
        }

        [Test]
        public void ShippedMap_OnlyTheSurroundObjectsThatShadeTheView_CastShadows()
        {
            var scene = EditorSceneManager.OpenScene(MapScene, OpenSceneMode.Single);
            var sur = scene.GetRootGameObjects().First(g => g.name == "MapSurroundings").transform;
            var offenders = sur.Cast<Transform>()
                .Where(t => t.GetComponentsInChildren<Renderer>(true).Any(r => r.shadowCastingMode != ShadowCastingMode.Off) != ShadowCastingSurround.Contains(t.name))
                .Select(t => t.name).ToList();
            Assert.IsEmpty(offenders, "Surround objects with the wrong shadow casting (expected casting only on " +
                                      string.Join(", ", ShadowCastingSurround) + "): " + string.Join(", ", offenders));
        }
    }
}

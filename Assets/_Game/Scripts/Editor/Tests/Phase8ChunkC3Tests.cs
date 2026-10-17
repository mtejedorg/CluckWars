using System.Linq;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CluckWars.Tests
{
    /// <summary>
    /// Phase 8 C3 "No package-default materials": the pile and base prefabs render with project matte
    /// materials (white base, so the property-block tint carries), and no gameplay renderer anywhere
    /// points at a material from <c>Packages/</c> or at nothing at all.
    /// </summary>
    /// <remarks>
    /// The bug class: both prefabs were left on the URP package's default <c>Lit.mat</c> (glossy,
    /// specular on) while A2 matted every other arena material, because they were not in A2's list.
    /// The matte settings themselves are pinned by <see cref="Phase8ChunkA2Tests"/> (FoodMound and
    /// PlayerBase are in its list). Disabled renderers are exempt: they are never drawn. In Map.unity
    /// that is the 4 boundary walls and 8 standard terrain blocks (collider-only boxes whose cube
    /// renderers the generator leaves disabled, still on the package Lit.mat), and A9's bare cover cubes.
    /// </remarks>
    public class Phase8ChunkC3Tests
    {
        private const string GameScene = "Assets/_Game/Scenes/Game.unity";
        private const string MapScene = "Assets/_Game/Scenes/Map.unity";
        private const string PrefabFolder = "Assets/_Game/Prefabs";

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

        private static readonly string[] BaseKitMaterials =
        {
            "Assets/_Game/Art/Materials/BaseKit_Straw.mat",
            "Assets/_Game/Art/Materials/BaseKit_PennantFlag.mat",
            "Assets/_Game/Art/Materials/BaseKit_PennantPole.mat",
        };

        [TestCase("Assets/_Game/Prefabs/FoodPile.prefab", "Assets/_Game/Art/Materials/FoodMound.mat")]
        [TestCase("Assets/_Game/Prefabs/PlayerBase.prefab", "Assets/_Game/Art/Materials/PlayerBase.mat")]
        public void Prefab_UsesItsProjectMatteMaterial(string prefabPath, string materialPath)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            Assert.IsNotNull(prefab, prefabPath);
            var renderers = prefab.GetComponentsInChildren<MeshRenderer>(true);
            Assert.That(renderers.Length, Is.GreaterThan(0), prefabPath + " has no MeshRenderer.");
            // The centre-pile glow halo (Phase 8 C S1) is the one non-matte renderer on the pile: an unlit project
            // material under Assets/, pinned in Phase8ChunkS1Tests.
            var visuals = prefab.GetComponent<CluckWars.Visuals.FoodPileVisuals>();
            var glow = visuals != null ? TestAssets.PrivateField<Renderer>(visuals, "_centreGlow") : null;
            foreach (var r in renderers)
                foreach (var m in r.sharedMaterials)
                {
                    if (r == glow) { StringAssert.StartsWith("Assets/", AssetDatabase.GetAssetPath(m), $"{prefabPath}/{r.name}"); continue; }
                    // The base kit (Phase 8 C S2): the ring keeps PlayerBase.mat; the straw floor and the pennant use their
                    // own matte BaseKit_* project materials (in the A2 matte list, pinned in Phase8ChunkS2Tests).
                    if (BaseKitMaterials.Contains(AssetDatabase.GetAssetPath(m)) && prefabPath.EndsWith("/PlayerBase.prefab")) continue;
                    // Phase 8 D8: the mound's second material is the colourless stencil mark for the ring ghost (pinned in Phase8ChunkD8RingGhostTests).
                    if (AssetDatabase.GetAssetPath(m) == "Assets/_Game/Art/Materials/MoundStencilMark.mat" && r.name == "MoundMesh") continue;
                    Assert.AreEqual(materialPath, AssetDatabase.GetAssetPath(m), $"{prefabPath}/{r.name}");
                }
        }

        // FoodMound.mat carries the neutral grain tile on purpose (Phase 8 C S1); that is pinned in Phase8ChunkS1Tests.
        [TestCase("Assets/_Game/Art/Materials/FoodMound.mat")]
        [TestCase("Assets/_Game/Art/Materials/PlayerBase.mat")]
        public void TintedMaterial_HasWhiteBase_SoThePropertyBlockCarriesTheColour(string path)
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            Assert.IsNotNull(m, path);
            Assert.AreEqual(Color.white, m.GetColor("_BaseColor"), path);
            if (!path.EndsWith("FoodMound.mat")) Assert.IsNull(m.GetTexture("_BaseMap"), path + ": a base texture would multiply the tint.");
        }

        [Test]
        public void GameAndMapScenes_EveryRenderer_UsesOnlyProjectMaterials()
        {
            EditorSceneManager.OpenScene(GameScene, OpenSceneMode.Single);
            EditorSceneManager.OpenScene(MapScene, OpenSceneMode.Additive);

            var bad = new List<string>();
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                foreach (var root in scene.GetRootGameObjects())
                    foreach (var r in root.GetComponentsInChildren<Renderer>(true))
                        Audit(r, scene.name, bad);
            }
            CollectionAssert.IsEmpty(bad, "Renderers on null / package materials:\n" + string.Join("\n", bad));
        }

        [Test]
        public void GameplayPrefabs_EveryRenderer_UsesOnlyProjectMaterials()
        {
            var bad = new List<string>();
            foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { PrefabFolder }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab == null) continue;
                foreach (var r in prefab.GetComponentsInChildren<Renderer>(true))
                    Audit(r, path, bad);
            }
            CollectionAssert.IsEmpty(bad, "Renderers on null / package materials:\n" + string.Join("\n", bad));
        }

        // Only mesh-drawing renderers: particle, trail and line renderers belong to the VFX systems.
        private static void Audit(Renderer r, string where, List<string> bad)
        {
            if (!(r is MeshRenderer) && !(r is SkinnedMeshRenderer)) return;
            if (!r.enabled) return;
            var mats = r.sharedMaterials;
            for (int i = 0; i < mats.Length; i++)
            {
                var path = mats[i] == null ? null : AssetDatabase.GetAssetPath(mats[i]);
                if (string.IsNullOrEmpty(path) || !path.StartsWith("Assets/"))
                    bad.Add($"{where}: {r.transform.name}[{i}] -> {(path ?? "null")}");
            }
        }
    }
}

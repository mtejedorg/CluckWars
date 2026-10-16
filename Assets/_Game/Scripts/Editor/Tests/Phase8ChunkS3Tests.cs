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
    /// Phase 8 chunk C Stage 3 S3a (D5 surround): the generated barn replaces the old <c>big_barn</c> mesh in
    /// <c>Prop_big_barn.prefab</c>, and the four flat <c>ArenaHedge</c> cubes in Map.unity are textured
    /// <c>hedge_strip</c> instances.
    /// </summary>
    public class Phase8ChunkS3Tests
    {
        private const string BarnPrefab = "Assets/_Game/Prefabs/Props/Prop_big_barn.prefab";
        private const string BarnFbx = "Assets/Generated/Env/barn.fbx";
        private const string BarnMaterial = "Assets/_Game/Art/Materials/Prop_big_barn.mat";
        private const string BarnAlbedo = "Assets/Generated/Env/Textures/barn_albedo.png";
        private const string HedgeFbx = "Assets/Generated/Env/hedge_strip.fbx";
        private const string HedgeSide = "Assets/_Game/Art/Materials/Hedge_Side.mat";
        private const string HedgeTop = "Assets/_Game/Art/Materials/Hedge_Top.mat";
        private const string HedgeMacro = "Assets/Generated/Env/Textures/hedge_macro_variation.png";
        private const string MapScene = "Assets/_Game/Scenes/Map.unity";

        /// <summary>Blender Z-up -> Unity Y-up node rotation the FBX imports with (270 deg about X).</summary>
        private static readonly Quaternion NodeRotation = Quaternion.Euler(270f, 0f, 0f);

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

        private static Mesh FbxMesh(string path) =>
            AssetDatabase.LoadAssetAtPath<GameObject>(path).GetComponentInChildren<MeshFilter>().sharedMesh;

        private static float AngleTo(Quaternion a, Quaternion b) => Quaternion.Angle(a, b);

        // ---- barn ------------------------------------------------------------------------------------

        [Test]
        public void BarnPrefab_UsesTheGeneratedMesh_WithTheMatteMaterial_AndNoShadow()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(BarnPrefab);
            Assert.IsNotNull(prefab);
            Assert.AreEqual(Vector3.one, prefab.transform.localScale, "root stays at scale 1");
            var filters = prefab.GetComponentsInChildren<MeshFilter>(true);
            Assert.AreEqual(1, filters.Length, "one barn mesh (the old big_barn with its silo is gone)");
            var f = filters[0];
            Assert.AreSame(FbxMesh(BarnFbx), f.sharedMesh, "barn mesh");
            Assert.AreSame(prefab.transform, f.transform.parent, "mesh child sits directly under the root");
            Assert.Less(AngleTo(NodeRotation, f.transform.localRotation), 0.1f, "child keeps the 270 deg X convention");
            Assert.AreEqual(Vector3.zero, f.transform.localPosition);

            var r = f.GetComponent<MeshRenderer>();
            Assert.AreEqual(1, r.sharedMaterials.Length);
            Assert.AreEqual(BarnMaterial, AssetDatabase.GetAssetPath(r.sharedMaterial));
            Assert.AreEqual(ShadowCastingMode.Off, r.shadowCastingMode, "B3: the barn is outside the fence and casts no shadow");
            Assert.AreEqual(0, prefab.GetComponentsInChildren<Collider>(true).Length, "no colliders on the surround barn");

            var m = r.sharedMaterial;
            Assert.AreEqual(BarnAlbedo, AssetDatabase.GetAssetPath(m.GetTexture("_BaseMap")));
            Assert.AreEqual(Color.white, m.GetColor("_BaseColor"));
            Assert.AreEqual(0f, m.GetFloat("_Smoothness"));
        }

        [Test]
        public void BarnAlbedo_Android1024_Standalone2048()
        {
            var ti = (TextureImporter)AssetImporter.GetAtPath(BarnAlbedo);
            var android = ti.GetPlatformTextureSettings("Android");
            Assert.IsTrue(android.overridden, "Android override on");
            Assert.AreEqual(1024, android.maxTextureSize);
            var pc = ti.GetPlatformTextureSettings("Standalone");
            Assert.AreEqual(2048, pc.overridden ? pc.maxTextureSize : ti.maxTextureSize);
            Assert.IsTrue(ti.mipmapEnabled);
        }

        // ---- hedges ----------------------------------------------------------------------------------

        private static readonly (string Name, Vector3 Pos, float Yaw)[] Hedges =
        {
            ("Hedge_N", new Vector3(0f, 0f, 28.8f), 0f),
            ("Hedge_S", new Vector3(0f, 0f, -28.8f), 180f),
            ("Hedge_E", new Vector3(28.8f, 0f, 0f), 90f),
            ("Hedge_W", new Vector3(-28.8f, 0f, 0f), 270f),
        };

        [Test]
        public void ArenaHedge_IsFourStrips_AtTheirTransforms_WithTheStripMesh_AndNoCubes()
        {
            var scene = EditorSceneManager.OpenScene(MapScene, OpenSceneMode.Single);
            var surround = scene.GetRootGameObjects().First(g => g.name == "MapSurroundings").transform;
            var hedgeRoot = surround.Find("ArenaHedge");
            Assert.IsNotNull(hedgeRoot, "MapSurroundings/ArenaHedge");
            Assert.AreEqual(4, hedgeRoot.childCount, "exactly the four strips");

            var strip = FbxMesh(HedgeFbx);
            foreach (var h in Hedges)
            {
                var t = hedgeRoot.Find(h.Name);
                Assert.IsNotNull(t, h.Name);
                Assert.Less(Vector3.Distance(h.Pos, t.position), 1e-3f, $"{h.Name} position");
                var expected = Quaternion.Euler(0f, h.Yaw, 0f) * NodeRotation;
                Assert.Less(AngleTo(expected, t.rotation), 0.1f, $"{h.Name} rotation (yaw {h.Yaw} on the 270 deg X node)");
                Assert.AreEqual(0.993f, t.localScale.x, 1e-4f, $"{h.Name} X scale");
                Assert.AreEqual(1f, t.localScale.y, 1e-4f);
                Assert.AreEqual(1f, t.localScale.z, 1e-4f);
                Assert.AreSame(strip, t.GetComponent<MeshFilter>().sharedMesh, $"{h.Name} mesh");
                var r = t.GetComponent<MeshRenderer>();
                CollectionAssert.AreEqual(new[] { HedgeSide, HedgeTop }, r.sharedMaterials.Select(AssetDatabase.GetAssetPath).ToArray(),
                    $"{h.Name} materials (submesh 0 = sides, 1 = crown)");
                Assert.AreEqual(ShadowCastingMode.Off, r.shadowCastingMode, $"{h.Name} shadows");
                Assert.IsNull(t.GetComponent<Collider>(), $"{h.Name}: the old cubes had no colliders; none added");
                // The crown is ~1 m above the ground, never sunk into it.
                Assert.AreEqual(0f, r.bounds.min.y, 0.05f, $"{h.Name} sits on the ground");
                Assert.AreEqual(1.1f, r.bounds.max.y, 0.1f, $"{h.Name} crown height");
            }

            var cubes = surround.GetComponentsInChildren<MeshFilter>(true)
                .Where(f => f.sharedMesh != null && f.sharedMesh.name == "Cube" && f.name.StartsWith("Hedge"))
                .Select(f => f.name).ToArray();
            CollectionAssert.IsEmpty(cubes, "no stray hedge cubes left");
        }

        [Test]
        public void HedgeMaterials_UseTheLeafTiles_WhiteBase()
        {
            var side = AssetDatabase.LoadAssetAtPath<Material>(HedgeSide);
            var top = AssetDatabase.LoadAssetAtPath<Material>(HedgeTop);
            Assert.AreEqual("Assets/Generated/Env/Textures/hedge_leaf_tile.png", AssetDatabase.GetAssetPath(side.GetTexture("_BaseMap")));
            Assert.AreEqual("Assets/Generated/Env/Textures/hedge_leaf_tile_top.png", AssetDatabase.GetAssetPath(top.GetTexture("_BaseMap")));
            foreach (var m in new[] { side, top })
            {
                Assert.AreEqual(Color.white, m.GetColor("_BaseColor"), m.name);
                Assert.AreEqual(Vector2.one, m.GetTextureScale("_BaseMap"), $"{m.name}: UVs are already in metres");
                // The macro map is only valid as a linear (sRGB off) Detail Albedo x2; if a material uses it, check it.
                if (m.IsKeywordEnabled("_DETAIL_MULX2"))
                    Assert.AreEqual(HedgeMacro, AssetDatabase.GetAssetPath(m.GetTexture("_DetailAlbedoMap")), m.name);
            }
        }

        [Test]
        public void HedgeMacroVariation_IsLinear()
        {
            var ti = (TextureImporter)AssetImporter.GetAtPath(HedgeMacro);
            Assert.IsFalse(ti.sRGBTexture, "a Detail Albedo x2 map must be sRGB OFF or it darkens the hedge ~2x");
        }
    }
}

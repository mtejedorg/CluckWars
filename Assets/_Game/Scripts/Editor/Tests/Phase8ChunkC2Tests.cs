using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using CluckWars.Abilities;
using CluckWars.Visuals;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace CluckWars.Tests
{
    /// <summary>
    /// Phase 8 C2 "Ghost fade": Invisibility, Smoke Roost and the stunned ghost never faded the chicken body because
    /// the chicken materials are URP Lit Opaque, which ignores the alpha ChickenVisuals writes into _BaseColor. The fix
    /// is a dithered opaque-queue clip (Chicken Ghost Lit, 4x4 Bayer shared with the fence fade) that ChickenVisuals
    /// swaps in only while opacity &lt; 0.99 and swaps back after, turning shadow casting off below 0.5.
    /// </summary>
    public class Phase8ChunkC2Tests
    {
        private const string ShaderPath = "Assets/_Game/Art/Shaders/ChickenGhostLit.shader";
        private const string GhostHlslPath = "Assets/_Game/Art/Shaders/CWChickenGhost.hlsl";
        private const string DitherPath = "Assets/_Game/Art/Shaders/CWDither.hlsl";
        private const string FenceHlslPath = "Assets/_Game/Art/Shaders/CWFenceFade.hlsl";
        private const string GhostMatPath = "Assets/_Game/Art/Materials/Chicken_Ghost.mat";
        private const string ChickenPrefabPath = "Assets/_Game/Prefabs/Chicken.prefab";
        private const string ShaderName = "Cluck Wars/Chicken Ghost Lit";

        private static readonly string[] ClassMaterials =
        {
            "Assets/_Game/Art/Materials/Chicken_Warrior.mat",
            "Assets/_Game/Art/Materials/Chicken_Speedy.mat",
            "Assets/_Game/Art/Materials/Chicken_Fatty.mat",
            "Assets/_Game/Art/Materials/Chicken_Assassin.mat",
        };

        // ---- shader ---------------------------------------------------------------------------------------

        [Test]
        public void GhostShader_Compiles_IsOpaque_AndClipsEveryDepthWritingPass()
        {
            var shader = AssetDatabase.LoadAssetAtPath<Shader>(ShaderPath);
            Assert.IsNotNull(shader, ShaderPath);
            Assert.AreEqual(ShaderName, shader.name);
            Assert.IsFalse(ShaderUtil.ShaderHasError(shader), "ghost shader has compile errors");
            Assert.IsTrue(shader.isSupported, "ghost shader not supported on the current graphics API");
            Assert.AreEqual(2000, shader.renderQueue, "the ghost stays in the opaque queue (no sorting, depth stays right)");

            string src = File.ReadAllText(ShaderPath);
            StringAssert.Contains("\"RenderType\" = \"Opaque\"", src);
            foreach (var pass in new[] { "ForwardLit", "ShadowCaster", "GBuffer", "DepthOnly", "DepthNormals" })
                StringAssert.Contains($"Name \"{pass}\"", src, $"missing pass {pass}");
            StringAssert.DoesNotContain("Name \"MotionVectors\"", src, "a clipped pixel must not leave a motion vector");

            // Every colour/depth/shadow writing pass clips: 5 wrappers, 5 includes.
            Assert.AreEqual(5, Regex.Matches(src, @"#include\s+""CWChickenGhost\.hlsl""").Count, "passes including the ghost clip");
            Assert.AreEqual(5, Regex.Matches(src, @"CW_GhostClip\(input\.positionCS\);").Count, "fragment wrappers calling the clip");
            foreach (var frag in new[] { "CWGhostLitPassFragment", "CWGhostShadowPassFragment", "CWGhostLitGBufferPassFragment",
                                         "CWGhostDepthOnlyFragment", "CWGhostDepthNormalsFragment" })
                StringAssert.Contains("#pragma fragment " + frag, src, $"{frag} is not the pass's fragment entry");

            // Skinning: the stock Lit vertex functions, no custom vertex path that could drop the skinned positions.
            StringAssert.Contains("#pragma vertex LitPassVertex", src);
            StringAssert.Contains("#pragma vertex ShadowPassVertex", src);
        }

        [Test]
        public void GhostClip_ReadsTheMpbAlpha_ThroughTheSharedBayerHelper()
        {
            string ghost = File.ReadAllText(GhostHlslPath);
            StringAssert.Contains("#include \"CWDither.hlsl\"", ghost);
            StringAssert.Contains("clip(_BaseColor.a - CW_Bayer4(positionCS.xy))", ghost,
                "the dither keeps _BaseColor.a of the pixels (the alpha ChickenVisuals pushes through its MPB)");

            string dither = File.ReadAllText(DitherPath);
            StringAssert.Contains("float CW_Bayer4(float2 pixel)", dither);

            string fence = File.ReadAllText(FenceHlslPath);
            StringAssert.Contains("#include \"CWDither.hlsl\"", fence, "the fence fade reuses the shared helper");
            StringAssert.DoesNotContain("float CW_Bayer4(", fence, "the Bayer helper must live in one place");
        }

        [TestCase(0.2f, 3)]  // Invisibility / Smoke Roost: a faint ghost, never a full cut-out
        [TestCase(0.4f, 6)]  // stunned
        [TestCase(0.98f, 16)]
        public void BayerKeep_MatchesTheOpacity(float opacity, int expectedSurvivors)
        {
            // CW_Bayer4 thresholds are (k + 0.5) / 16; a pixel survives when opacity - threshold >= 0.
            int survive = Enumerable.Range(0, 16).Count(k => opacity - (k + 0.5f) / 16f >= 0f);
            Assert.AreEqual(expectedSurvivors, survive);
        }

        // ---- material + wiring ----------------------------------------------------------------------------

        [Test]
        public void GhostMaterial_IsMatte_Instanced_AndKeywordCompatibleWithTheClassMaterials()
        {
            var ghost = AssetDatabase.LoadAssetAtPath<Material>(GhostMatPath);
            Assert.IsNotNull(ghost, GhostMatPath);
            StringAssert.StartsWith("Assets/", AssetDatabase.GetAssetPath(ghost));
            Assert.AreEqual(ShaderName, ghost.shader.name);
            Assert.IsTrue(ghost.enableInstancing, "Enable GPU Instancing");
            Assert.AreEqual(0f, ghost.GetFloat("_Surface"), "opaque surface");
            Assert.LessOrEqual(ghost.GetFloat("_Smoothness"), 0.1f, "matte");
            Assert.IsTrue(ghost.IsKeywordEnabled("_SPECULARHIGHLIGHTS_OFF"));
            Assert.IsTrue(ghost.IsKeywordEnabled("_ENVIRONMENTREFLECTIONS_OFF"));

            // The ghost twins copy each class material's keywords; the template must carry the same set so the
            // build keeps exactly that variant of the ghost shader.
            var ghostKw = ghost.shaderKeywords.OrderBy(k => k).ToArray();
            foreach (var path in ClassMaterials)
            {
                var m = AssetDatabase.LoadAssetAtPath<Material>(path);
                Assert.IsNotNull(m, path);
                CollectionAssert.AreEqual(m.shaderKeywords.OrderBy(k => k).ToArray(), ghostKw, $"{path}: keyword set differs from Chicken_Ghost.mat");
            }
        }

        [Test]
        public void ChickenPrefab_WiresTheGhostMaterial()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ChickenPrefabPath);
            var vis = prefab.GetComponent<ChickenVisuals>();
            Assert.IsNotNull(vis);
            var so = new SerializedObject(vis);
            Assert.AreEqual(AssetDatabase.LoadAssetAtPath<Material>(GhostMatPath), so.FindProperty("_ghostMaterial").objectReferenceValue,
                "ChickenVisuals._ghostMaterial must be Chicken_Ghost.mat, or Invisibility / Smoke Roost / stun never fade the body");
        }

        [Test]
        public void FadeAbilities_FadeBelowTheShadowThreshold()
        {
            // Owner and rivals read the same [Networked] VisualOpacity, so both see this value.
            var invis = AssetDatabase.LoadAssetAtPath<InvisibilityAbilitySO>("Assets/_Game/Data/Abilities/Invisibility.asset");
            var smoke = AssetDatabase.LoadAssetAtPath<SmokeRoostAbilitySO>("Assets/_Game/Data/Abilities/SmokeRoost.asset");
            Assert.IsNotNull(invis); Assert.IsNotNull(smoke);
            Assert.Greater(invis.Opacity, 0f, "0 would be a full cloak, not a ghost");
            Assert.Less(invis.Opacity, ChickenVisuals.GhostShadowOpacityThreshold);
            Assert.Less(smoke.Opacity, ChickenVisuals.GhostShadowOpacityThreshold);
        }

        // ---- swap / restore -------------------------------------------------------------------------------

        private GameObject _a, _b;

        [TearDown]
        public void TearDown()
        {
            if (_a != null) Object.DestroyImmediate(_a);
            if (_b != null) Object.DestroyImmediate(_b);
        }

        private static (GameObject go, ChickenVisuals vis, Renderer body) MakeChicken(Material bodyMat, ShadowCastingMode shadows)
        {
            var go = new GameObject("C2_TestChicken");
            var model = new GameObject("Model");
            model.transform.SetParent(go.transform);
            var body = model.AddComponent<SkinnedMeshRenderer>();
            body.sharedMaterial = bodyMat;
            body.shadowCastingMode = shadows;
            var vis = go.AddComponent<ChickenVisuals>();
            var so = new SerializedObject(vis);
            so.FindProperty("_ghostMaterial").objectReferenceValue = AssetDatabase.LoadAssetAtPath<Material>(GhostMatPath);
            so.ApplyModifiedPropertiesWithoutUndo();
            vis.SetModelRoot(model.transform);
            return (go, vis, body);
        }

        [Test]
        public void Opacity_BelowThreshold_SwapsToGhost_AndRestoresTheOriginalSharedMaterial()
        {
            var original = AssetDatabase.LoadAssetAtPath<Material>(ClassMaterials[0]);
            var (go, vis, body) = MakeChicken(original, ShadowCastingMode.TwoSided);
            _a = go;
            var tint = new Color(0.9f, 0.6f, 0.5f, 1f);

            vis.ApplyTint(tint, 0.3f);
            Assert.IsFalse(vis.IsGhosted, "opacity 1: plain material");
            Assert.AreSame(original, body.sharedMaterial);

            vis.ApplyTint(new Color(tint.r, tint.g, tint.b, 0.7f), 0.3f);
            Assert.IsTrue(vis.IsGhosted, "opacity 0.7 < 0.99: ghost");
            var ghost = body.sharedMaterial;
            Assert.AreEqual(ShaderName, ghost.shader.name);
            Assert.AreSame(original.GetTexture("_BaseMap"), ghost.GetTexture("_BaseMap"), "the ghost keeps the class texture");
            Assert.AreEqual(original.GetFloat("_Smoothness"), ghost.GetFloat("_Smoothness"), "the ghost keeps the matte look");
            Assert.IsTrue(ghost.enableInstancing);
            Assert.AreEqual(ShadowCastingMode.TwoSided, body.shadowCastingMode, "0.7 >= 0.5 keeps the shadow");

            var mpb = new MaterialPropertyBlock();
            body.GetPropertyBlock(mpb);
            Assert.AreEqual(0.7f, mpb.GetColor("_BaseColor").a, 1e-5f, "the clip reads the MPB alpha");

            vis.ApplyTint(new Color(tint.r, tint.g, tint.b, 0.2f), 0.3f);
            Assert.AreSame(ghost, body.sharedMaterial, "same twin while staying faded: no re-swap");
            Assert.AreEqual(ShadowCastingMode.Off, body.shadowCastingMode, "below 0.5: no shadow");

            vis.ApplyTint(new Color(tint.r, tint.g, tint.b, 0.6f), 0.3f);
            Assert.AreEqual(ShadowCastingMode.TwoSided, body.shadowCastingMode, "back above 0.5: shadow mode restored");

            vis.ApplyTint(new Color(tint.r, tint.g, tint.b, 0.2f), 0.3f);
            vis.ApplyTint(tint, 0.3f);
            Assert.IsFalse(vis.IsGhosted, "opacity 1: ghost removed");
            Assert.AreSame(original, body.sharedMaterial, "the ORIGINAL shared material comes back, not a copy");
            Assert.AreEqual(ShadowCastingMode.TwoSided, body.shadowCastingMode, "original shadow mode restored");
        }

        [Test]
        public void GhostTwin_IsSharedAcrossChickens_NotPerInstance()
        {
            var original = AssetDatabase.LoadAssetAtPath<Material>(ClassMaterials[1]);
            var a = MakeChicken(original, ShadowCastingMode.On);
            var b = MakeChicken(original, ShadowCastingMode.On);
            _a = a.go; _b = b.go;

            a.vis.ApplyTint(new Color(1f, 1f, 1f, 0.2f), 0f);
            b.vis.ApplyTint(new Color(1f, 1f, 1f, 0.4f), 0f);
            Assert.AreSame(a.body.sharedMaterial, b.body.sharedMaterial, "one ghost twin per class material, shared by every chicken");
            Assert.AreNotSame(original, a.body.sharedMaterial);
        }

        [Test]
        public void SetModelRoot_WhileGhosted_HandsTheOldModelItsMaterialsBack_AndGhostsTheNewOne()
        {
            var warrior = AssetDatabase.LoadAssetAtPath<Material>(ClassMaterials[0]);
            var (go, vis, oldBody) = MakeChicken(warrior, ShadowCastingMode.On);
            _a = go;
            vis.ApplyTint(new Color(1f, 1f, 1f, 0.2f), 0f);
            Assert.IsTrue(vis.IsGhosted);

            var fatty = AssetDatabase.LoadAssetAtPath<Material>(ClassMaterials[2]);
            var model2 = new GameObject("Model2");
            model2.transform.SetParent(go.transform);
            var newBody = model2.AddComponent<SkinnedMeshRenderer>();
            newBody.sharedMaterial = fatty;
            vis.SetModelRoot(model2.transform);

            Assert.AreSame(warrior, oldBody.sharedMaterial, "the old model got its own material back");
            Assert.AreEqual(ShadowCastingMode.On, oldBody.shadowCastingMode);
            Assert.AreEqual(ShaderName, newBody.sharedMaterial.shader.name, "still faded: the new model is ghosted too");
            Assert.AreSame(fatty.GetTexture("_BaseMap"), newBody.sharedMaterial.GetTexture("_BaseMap"));
            Assert.AreEqual(ShadowCastingMode.Off, newBody.shadowCastingMode);
        }
    }
}

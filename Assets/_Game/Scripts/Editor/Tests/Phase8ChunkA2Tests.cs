using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using CluckWars.Gameplay;

namespace CluckWars.Tests
{
    /// <summary>
    /// Phase 8 A2 "Matte, honest ground and props": the ground, arena props, terrain obstacles and
    /// zone markers are matte (low smoothness, no specular highlights, no environment
    /// reflections) and GPU-instanced; the player base tint uses a property block instead of
    /// cloning <c>Lit (Instance)</c> materials.
    /// </summary>
    /// <remarks>
    /// The bug class: URP Lit's defaults (smoothness 0.5, highlights + reflections on) made the
    /// grass floor and obstacles read as glossy plastic and washed the top-down view towards
    /// white under the warm sun. The settings are checked on both the float AND the keyword —
    /// URP Lit's shader reads only the keyword, so a material edited through YAML with the float
    /// flipped but the keyword missing would still render shiny.
    /// Materials on a custom shader (the fence-fade props) are deliberately out of scope.
    /// </remarks>
    public class Phase8ChunkA2Tests
    {
        private const float MaxSmoothness = 0.1f;
        private const string PlayerBaseSource = "Assets/_Game/Scripts/Gameplay/PlayerBase.cs";

        private static readonly string[] MatteMaterials =
        {
            "Assets/_Game/Materials/GrassFloor.mat",
            "Assets/_Game/Art/Materials/BaseZone.mat",
            "Assets/_Game/Art/Materials/PileZone.mat",
            "Assets/_Game/Art/Materials/TerrainObstacle.mat",
            "Assets/_Game/Art/Materials/TerrainObstacle_Standard.mat",
            "Assets/_Game/Art/Materials/Prop_barrel.mat",
            "Assets/_Game/Art/Materials/Prop_bush_round.mat",
            "Assets/_Game/Art/Materials/Prop_crate.mat",
            "Assets/_Game/Art/Materials/Prop_feed_sack.mat",
            "Assets/_Game/Art/Materials/Prop_feed_silo.mat",
            "Assets/_Game/Art/Materials/Prop_food_pile_large.mat",
            "Assets/_Game/Art/Materials/Prop_food_pile_medium.mat",
            "Assets/_Game/Art/Materials/Prop_food_pile_small.mat",
            "Assets/_Game/Art/Materials/Prop_hay_bale.mat",
            "Assets/_Game/Art/Materials/Prop_milk_churn.mat",
            "Assets/_Game/Art/Materials/Prop_nest_basket.mat",
            "Assets/_Game/Art/Materials/Prop_oak_tree.mat",
            "Assets/_Game/Art/Materials/Prop_oak_tree_Billboard_y45.mat",
            "Assets/_Game/Art/Materials/Prop_oak_tree_Billboard_y135.mat",
            "Assets/_Game/Art/Materials/Prop_scarecrow.mat",
            "Assets/_Game/Art/Materials/Prop_trough.mat",
            "Assets/_Game/Art/Materials/Prop_wheelbarrow.mat",
            "Assets/_Game/Art/Materials/FoodMound.mat",
            "Assets/_Game/Art/Materials/PlayerBase.mat",
            "Assets/_Game/Art/Materials/Surround_Grass.mat",
            "Assets/_Game/Art/Materials/Surround_Hedge.mat",
            "Assets/_Game/Art/Materials/BaseKit_Straw.mat",
            "Assets/_Game/Art/Materials/BaseKit_PennantFlag.mat",
            "Assets/_Game/Art/Materials/BaseKit_PennantPole.mat",
        };

        private static Material Load(string path)
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            Assert.IsNotNull(m, $"{path} is missing.");
            return m;
        }

        [TestCaseSource(nameof(MatteMaterials))]
        public void Material_IsMatteUrpLit(string path)
        {
            var m = Load(path);
            Assert.AreEqual("Universal Render Pipeline/Lit", m.shader.name, $"{path}: shader");
            Assert.LessOrEqual(m.GetFloat("_Smoothness"), MaxSmoothness, $"{path}: _Smoothness");
            Assert.AreEqual(0f, m.GetFloat("_SpecularHighlights"), $"{path}: _SpecularHighlights");
            Assert.IsTrue(m.IsKeywordEnabled("_SPECULARHIGHLIGHTS_OFF"), $"{path}: _SPECULARHIGHLIGHTS_OFF keyword");
            Assert.AreEqual(0f, m.GetFloat("_EnvironmentReflections"), $"{path}: _EnvironmentReflections");
            Assert.IsTrue(m.IsKeywordEnabled("_ENVIRONMENTREFLECTIONS_OFF"), $"{path}: _ENVIRONMENTREFLECTIONS_OFF keyword");
        }

        [TestCaseSource(nameof(MatteMaterials))]
        public void Material_HasGpuInstancing(string path)
        {
            Assert.IsTrue(Load(path).enableInstancing, $"{path}: Enable GPU Instancing");
        }

        /// <summary>Every Prop_/TerrainObstacle/zone material on URP Lit is in the list above.</summary>
        [Test]
        public void MatteList_CoversEveryArenaUrpLitMaterial()
        {
            var missing = AssetDatabase.FindAssets("t:Material", new[] { "Assets/_Game" })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(p =>
                {
                    var n = Path.GetFileNameWithoutExtension(p);
                    return n == "GrassFloor" || n.StartsWith("Prop_") || n.StartsWith("TerrainObstacle")
                        || n.StartsWith("BaseZone") || n.StartsWith("PileZone")
                        || n == "FoodMound" || n == "PlayerBase" || n.StartsWith("Surround_") || n.StartsWith("BaseKit_");
                })
                .Where(p => AssetDatabase.LoadAssetAtPath<Material>(p).shader.name == "Universal Render Pipeline/Lit")
                .Except(MatteMaterials)
                .ToArray();
            CollectionAssert.IsEmpty(missing, "Arena URP Lit materials not pinned as matte: " + string.Join(", ", missing));
        }

        [Test]
        public void ApplyTint_UsesPropertyBlock_AndKeepsSharedMaterial()
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            Assert.IsNotNull(shader);
            var shared = new Material(shader) { name = "A2TintProbe" };
            var go = new GameObject("A2TintProbe", typeof(MeshFilter), typeof(MeshRenderer));
            try
            {
                var r = go.GetComponent<MeshRenderer>();
                r.sharedMaterial = shared;

                var existing = new MaterialPropertyBlock();
                existing.SetFloat("_A2Unrelated", 3f);
                r.SetPropertyBlock(existing);

                var tint = new Color(0.9f, 0.2f, 0.1f, 1f);
                PlayerBase.ApplyTint(r, new MaterialPropertyBlock(), tint);

                Assert.AreSame(shared, r.sharedMaterial, "Tint must not swap the shared material.");
                var read = new MaterialPropertyBlock();
                r.GetPropertyBlock(read);
                Assert.AreEqual(tint, read.GetColor("_BaseColor"), "_BaseColor override");
                Assert.AreEqual(tint, read.GetColor("_Color"), "_Color override (URP Lit declares _Color)");
                Assert.AreEqual(3f, read.GetFloat("_A2Unrelated"), "Existing block overrides must survive.");
                Assert.AreEqual(Color.white, shared.GetColor("_BaseColor"), "Shared material must stay untinted.");
            }
            finally
            {
                Object.DestroyImmediate(go);
                Object.DestroyImmediate(shared);
            }
        }

        /// <summary>
        /// <c>renderer.material</c> clones a material per renderer; PlayerBase must never use it.
        /// Comment lines are ignored so the remark explaining the rule does not trip it.
        /// </summary>
        [Test]
        public void PlayerBase_NeverInstantiatesMaterials()
        {
            var offending = File.ReadAllLines(PlayerBaseSource)
                .Where(l => !l.TrimStart().StartsWith("//"))
                .Where(l => Regex.IsMatch(l, @"\.materials?\b"))
                .ToArray();
            CollectionAssert.IsEmpty(offending, "PlayerBase.cs reads renderer.material(s): " + string.Join(" | ", offending));
        }
    }
}

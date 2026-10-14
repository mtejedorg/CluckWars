using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using CluckWars.Visuals;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace CluckWars.Tests
{
    /// <summary>
    /// Phase 8 A10 "Fence fade": the see-through hole in the near fence / base coop is near-total at its centre
    /// (at most 10 % of the fence left), has a thin cream rim at its edge, is compiled only into the global
    /// <c>_CW_FENCE_FADE</c> variant that the driver enables only while the strength is above 0, and the two
    /// fence-fade materials are matte and GPU-instanced like the rest of the props (A2 skipped them).
    /// </summary>
    public class Phase8ChunkA10Tests
    {
        private const string ShaderPath = "Assets/_Game/Art/Shaders/FenceFadeLit.shader";
        private const string HlslPath = "Assets/_Game/Art/Shaders/CWFenceFade.hlsl";
        private const float MaxSmoothness = 0.1f;

        private static readonly string[] FenceMaterials =
        {
            "Assets/_Game/Art/Materials/Prop_wall_segment.mat",
            "Assets/_Game/Art/Materials/Prop_chicken_coop.mat",
        };

        // ---- keyword --------------------------------------------------------------------------------------

        [Test]
        public void Shader_DeclaresTheFadeKeyword_InEveryClippingPass()
        {
            string src = File.ReadAllText(ShaderPath);
            // ForwardLit, GBuffer, DepthOnly, DepthNormals: the four passes that include the fade code.
            int includes = Regex.Matches(src, @"#include\s+""CWFenceFade\.hlsl""").Count;
            int pragmas = Regex.Matches(src, @"#pragma\s+multi_compile(_fragment)?\s+_\s+" + FenceFadeRules.KeywordName + @"\b").Count;
            Assert.AreEqual(4, includes, "passes including CWFenceFade.hlsl");
            Assert.AreEqual(includes, pragmas, "every pass that includes the fade code declares the keyword");
            Assert.IsFalse(Regex.IsMatch(src, @"multi_compile_local[^\n]*" + FenceFadeRules.KeywordName),
                "the keyword is GLOBAL (the driver feeds globals; a keyword cannot go in an MPB)");

            var shader = AssetDatabase.LoadAssetAtPath<Shader>(ShaderPath);
            Assert.IsNotNull(shader);
            Assert.AreEqual(FenceFadeRules.ShaderName, shader.name);
            Assert.IsFalse(ShaderUtil.ShaderHasError(shader), "fence fade shader has compile errors");
            CollectionAssert.Contains(shader.keywordSpace.keywordNames, FenceFadeRules.KeywordName,
                "compiled shader knows the keyword");
        }

        [Test]
        public void Hlsl_GuardsTheClipAndRim_BehindTheKeyword()
        {
            string src = File.ReadAllText(HlslPath);
            int guard = src.IndexOf("#if defined(" + FenceFadeRules.KeywordName + ")", System.StringComparison.Ordinal);
            int clip = src.IndexOf("clip(", System.StringComparison.Ordinal);
            int elseBranch = src.IndexOf("#else", System.StringComparison.Ordinal);
            Assert.GreaterOrEqual(guard, 0, "no #if defined(_CW_FENCE_FADE) guard");
            Assert.Greater(clip, guard, "clip() must sit inside the keyword guard");
            Assert.Greater(elseBranch, clip, "the #else (plain Lit) branch must follow the clip code");
            StringAssert.Contains("_CW_FenceRimParams", src);
            StringAssert.Contains("_CW_FenceRimColor", src);
        }

        [TestCase(false, 0f, false, false)]
        [TestCase(true, 0f, true, false)]
        [TestCase(false, 0.01f, true, true)]
        [TestCase(true, 0.5f, false, true)]
        [TestCase(true, 1f, false, true)]
        [TestCase(false, -0.1f, false, false)]
        public void KeywordToggle_OnlyWhenStrengthCrossesZero(bool keywordOn, float strength, bool expectToggle, bool expectWant)
        {
            bool toggle = FenceFadeRules.KeywordToggle(keywordOn, strength, out bool want);
            Assert.AreEqual(expectToggle, toggle, "toggle");
            Assert.AreEqual(expectWant, want, "wanted state");
        }

        [Test]
        public void KeywordToggle_FadeInOutSequence_TogglesExactlyTwice()
        {
            bool on = false;
            int toggles = 0;
            float s = 0f;
            for (int frame = 0; frame < 120; frame++)
            {
                s = FenceFadeRules.StepStrength(s, frame >= 10 && frame < 60, 1f / 60f);
                if (FenceFadeRules.KeywordToggle(on, s, out bool want)) { on = want; toggles++; }
                Assert.AreEqual(s > 0f, on, $"frame {frame}: keyword follows strength > 0");
            }
            Assert.AreEqual(2, toggles, "one enable, one disable, no churn");
        }

        // ---- max fade + rim ---------------------------------------------------------------------------------

        [Test]
        public void MaxFade_LeavesAtMostTenPercentOfTheFence()
        {
            Assert.GreaterOrEqual(FenceFadeRules.MaxFade, 0.9f);
            Assert.Less(FenceFadeRules.MaxFade, 1f, "a faint screen-door stays so the wall still reads");
            // CW_Bayer4 thresholds are (k + 0.5) / 16; a pixel survives when threshold > fade.
            int survive = Enumerable.Range(0, 16).Count(k => (k + 0.5f) / 16f - FenceFadeRules.MaxFade - 1e-4f >= 0f);
            Assert.LessOrEqual(survive / 16f, 0.10f, $"{survive}/16 dither pixels survive at the hole centre");
            Assert.Greater(survive, 0, "not a full cut-out");
        }

        [Test]
        public void Rim_IsAThinBandAtTheHoleEdge()
        {
            float r = FenceFadeRules.OuterRadius;
            Assert.That(FenceFadeRules.RimWidth / r, Is.InRange(0.1f, 0.15f), "rim width vs hole radius");
            Assert.That(FenceFadeRules.RimCentre, Is.InRange(FenceFadeRules.InnerRadius, FenceFadeRules.OuterRadius));
            Assert.That(FenceFadeRules.RimIntensity, Is.InRange(0.2f, 0.6f), "subtle");

            const float bird = 10f, front = 8f;
            Assert.AreEqual(FenceFadeRules.RimIntensity, FenceFadeRules.RimWeight(1f, FenceFadeRules.RimCentre, front, bird), 1e-5f, "peak");
            Assert.AreEqual(0f, FenceFadeRules.RimWeight(1f, 0f, front, bird), 1e-5f, "no rim at the hole centre");
            Assert.AreEqual(0f, FenceFadeRules.RimWeight(1f, r + FenceFadeRules.RimWidth, front, bird), 1e-5f, "no rim outside the band");
            Assert.AreEqual(0f, FenceFadeRules.RimWeight(0f, FenceFadeRules.RimCentre, front, bird), 1e-5f, "strength 0 = no rim");
            Assert.AreEqual(0f, FenceFadeRules.RimWeight(1f, FenceFadeRules.RimCentre, 12f, bird), 1e-5f, "far fence (behind the bird) gets no rim");
        }

        [Test]
        public void RimParams_MatchTheRules()
        {
            var p = FenceFadeRules.RimParams;
            Assert.AreEqual(FenceFadeRules.RimCentre, p.x, 1e-6f);
            Assert.AreEqual(0.5f * FenceFadeRules.RimWidth, p.y, 1e-6f);
            Assert.AreEqual(FenceFadeRules.RimIntensity, p.z, 1e-6f);
            Assert.AreEqual(FenceFadeRules.MaxFade, FenceFadeRules.ShaderParams.z, 1e-6f);
            Color32 c = FenceFadeRules.RimColor;
            Assert.AreEqual(new Color32(0xFE, 0xF5, 0xE0, 0xFF), c, "the UI cream");
        }

        // ---- matte materials --------------------------------------------------------------------------------

        [TestCaseSource(nameof(FenceMaterials))]
        public void FenceMaterial_IsMatte_AndInstanced(string path)
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            Assert.IsNotNull(m, path);
            Assert.AreEqual(FenceFadeRules.ShaderName, m.shader.name, $"{path}: shader");
            Assert.LessOrEqual(m.GetFloat("_Smoothness"), MaxSmoothness, $"{path}: _Smoothness");
            Assert.AreEqual(0f, m.GetFloat("_SpecularHighlights"), $"{path}: _SpecularHighlights");
            Assert.IsTrue(m.IsKeywordEnabled("_SPECULARHIGHLIGHTS_OFF"), $"{path}: _SPECULARHIGHLIGHTS_OFF keyword");
            Assert.AreEqual(0f, m.GetFloat("_EnvironmentReflections"), $"{path}: _EnvironmentReflections");
            Assert.IsTrue(m.IsKeywordEnabled("_ENVIRONMENTREFLECTIONS_OFF"), $"{path}: _ENVIRONMENTREFLECTIONS_OFF keyword");
            Assert.IsTrue(m.enableInstancing, $"{path}: Enable GPU Instancing");
            Assert.IsFalse(m.IsKeywordEnabled(FenceFadeRules.KeywordName), $"{path}: the fade keyword is global, never baked on the material");
        }
    }
}

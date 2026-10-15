using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using CluckWars.Visuals;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Rendering;
using UnityEngine;
using UnityEngine.Rendering;

namespace CluckWars.Tests
{
    /// <summary>
    /// Phase 8 C16 "Fence-fade band": the dithered ramp between the hole's inner and outer radius was 0.06 screen
    /// heights, a wide stippled ring. It is now ~0.03 (inner radius 0.09 -> 0.12), with the hole size (outer radius),
    /// MaxFade and the cream rim unchanged. Also pins the fix for the FXC "use of potentially uninitialized variable"
    /// warning in <c>CW_FenceFadeClipRim</c> (one exit point, no early return ahead of the clip).
    /// </summary>
    public class Phase8ChunkC16Tests
    {
        private const string ShaderPath = "Assets/_Game/Art/Shaders/FenceFadeLit.shader";
        private const string HlslPath = "Assets/_Game/Art/Shaders/CWFenceFade.hlsl";
        private const float Bird = 10f, Front = 8f; // eye depths: a fragment well in front of the bird

        // ---- the band ---------------------------------------------------------------------------------------

        [Test]
        public void EdgeBand_IsAboutThreeHundredthsOfTheScreenHeight()
        {
            Assert.AreEqual(FenceFadeRules.OuterRadius - FenceFadeRules.InnerRadius, FenceFadeRules.EdgeBandWidth, 1e-6f);
            Assert.That(FenceFadeRules.EdgeBandWidth, Is.InRange(0.025f, 0.035f), "inner -> outer span, screen heights");
            Assert.Less(FenceFadeRules.EdgeBandWidth, 0.06f - 1e-4f, "narrower than the pre-C16 0.06 band");
        }

        [Test]
        public void HoleSize_AndMaxFade_AreUnchanged()
        {
            Assert.AreEqual(0.15f, FenceFadeRules.OuterRadius, 1e-6f, "outer radius (hole size)");
            Assert.AreEqual(0.92f, FenceFadeRules.MaxFade, 1e-6f, "MaxFade (A10)");
            Assert.AreEqual(0.02f, FenceFadeRules.RimWidth, 1e-6f, "rim width (A10)");
            Assert.AreEqual(0.45f, FenceFadeRules.RimIntensity, 1e-6f, "rim intensity (A10)");
        }

        [Test]
        public void Fade_IsFullInsideTheInnerRadius_AndSolidAtTheOuterRadius()
        {
            Assert.AreEqual(FenceFadeRules.MaxFade, FenceFadeRules.FragmentFade(1f, FenceFadeRules.InnerRadius, Front, Bird), 1e-5f);
            Assert.AreEqual(FenceFadeRules.MaxFade, FenceFadeRules.FragmentFade(1f, 0.5f * FenceFadeRules.InnerRadius, Front, Bird), 1e-5f);
            Assert.AreEqual(0f, FenceFadeRules.FragmentFade(1f, FenceFadeRules.OuterRadius, Front, Bird), 1e-5f);
            float mid = FenceFadeRules.FragmentFade(1f, FenceFadeRules.InnerRadius + 0.5f * FenceFadeRules.EdgeBandWidth, Front, Bird);
            Assert.That(mid, Is.GreaterThan(0f).And.LessThan(FenceFadeRules.MaxFade), "still a dithered (soft) edge, not a hard cut");
        }

        [Test]
        public void ShaderParams_CarryTheNarrowBand()
        {
            var p = FenceFadeRules.ShaderParams;
            Assert.AreEqual(FenceFadeRules.InnerRadius, p.x, 1e-6f);
            Assert.AreEqual(FenceFadeRules.OuterRadius, p.y, 1e-6f);
            Assert.AreEqual(FenceFadeRules.EdgeBandWidth, p.y - p.x, 1e-6f);
            Assert.AreEqual(FenceFadeRules.MaxFade, p.z, 1e-6f);
        }

        // ---- the rim ----------------------------------------------------------------------------------------

        [Test]
        public void Rim_SitsJustInsideTheOuterRadius()
        {
            float half = 0.5f * FenceFadeRules.RimWidth;
            Assert.Less(FenceFadeRules.RimCentre, FenceFadeRules.OuterRadius, "rim centre inside the outer edge");
            Assert.LessOrEqual(FenceFadeRules.RimCentre + half, FenceFadeRules.OuterRadius + 1e-6f, "rim band ends at the outer edge");
            Assert.Greater(FenceFadeRules.RimCentre, FenceFadeRules.InnerRadius, "rim on the dithered edge, not in the cleared hole");
        }

        [Test]
        public void Rim_StaysVisible_OnPicketsThatSurvive()
        {
            // At the rim centre most of the fence must survive the dither clip so the cream tint has pixels to sit on.
            float fade = FenceFadeRules.FragmentFade(1f, FenceFadeRules.RimCentre, Front, Bird);
            int survive = Enumerable.Range(0, 16).Count(k => (k + 0.5f) / 16f - fade - 1e-4f >= 0f);
            Assert.GreaterOrEqual(survive, 8, $"{survive}/16 dither pixels survive at the rim centre (fade {fade:0.000})");
            Assert.AreEqual(FenceFadeRules.RimIntensity,
                FenceFadeRules.RimWeight(1f, FenceFadeRules.RimCentre, Front, Bird), 1e-5f, "rim peak");
        }

        // ---- shader -----------------------------------------------------------------------------------------

        [Test]
        public void ClipRim_HasOneExitPoint()
        {
            string src = File.ReadAllText(HlslPath);
            var m = Regex.Match(src, @"float CW_FenceFadeClipRim\(float4 positionCS\)\s*\{(?<body>.*?)\n\}", RegexOptions.Singleline);
            Assert.IsTrue(m.Success, "CW_FenceFadeClipRim (keyword branch) not found");
            int returns = Regex.Matches(m.Groups["body"].Value, @"\breturn\b").Count;
            Assert.AreEqual(1, returns, "an early return ahead of clip() triggers the 'potentially uninitialized variable' warning");
        }

        [Test]
        public void FadeVariant_CompilesWithoutWarnings_OnGles3VulkanAndD3D()
        {
            var shader = AssetDatabase.LoadAssetAtPath<Shader>(ShaderPath);
            Assert.IsNotNull(shader, ShaderPath);
            var sub = ShaderUtil.GetShaderData(shader).GetSubshader(0);
            string[] passes = { "ForwardLit", "GBuffer", "DepthOnly", "DepthNormals" };
            string[] kw = { FenceFadeRules.KeywordName, "_SPECULARHIGHLIGHTS_OFF" };
            var problems = new StringBuilder();
            int compiled = 0;
            for (int p = 0; p < sub.PassCount; p++)
            {
                var pass = sub.GetPass(p);
                if (!passes.Contains(pass.Name)) continue;
                foreach (var plat in new[] { ShaderCompilerPlatform.GLES3x, ShaderCompilerPlatform.Vulkan, ShaderCompilerPlatform.D3D })
                {
                    if (pass.Name == "GBuffer" && plat == ShaderCompilerPlatform.GLES3x) continue; // excluded by design
                    var target = plat == ShaderCompilerPlatform.D3D ? BuildTarget.StandaloneWindows64 : BuildTarget.Android;
                    foreach (var stage in new[] { ShaderType.Vertex, ShaderType.Fragment })
                    {
                        var r = pass.CompileVariant(stage, kw, plat, target);
                        compiled++;
                        if (!r.Success) problems.AppendLine($"{pass.Name} {plat} {stage}: failed");
                        foreach (var msg in r.Messages)
                            problems.AppendLine($"{pass.Name} {plat} {stage}: {msg.severity} {msg.message} ({msg.file}:{msg.line})");
                    }
                }
            }
            Assert.Greater(compiled, 0, "no fence-fade passes compiled");
            Assert.IsEmpty(problems.ToString(), "shader compile messages");
        }
    }
}

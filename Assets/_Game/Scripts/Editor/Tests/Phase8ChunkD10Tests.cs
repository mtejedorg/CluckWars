using System.IO;
using System.Linq;
using System.Reflection;
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
    /// Phase 8 D10 "Fence-hole core stipple" (finding L10): the fade peaked at 0.92 (A10's MaxFade), so one 4x4 Bayer cell
    /// in 16 kept its fence pixel inside the hole: a dot grid over the chicken's legs and foot ring that crawled as the
    /// camera followed. Now an open hole is fully clear inside <see cref="FenceFadeRules.InnerRadius"/> (fade 1, no
    /// survivors) for the local bird, the D4 chicken slots and the E2 fixed targets alike (one shader function,
    /// <c>CW_FenceFadeAmount</c>, so the inner arm walls lose the stipple too); the dither lives only in the edge band
    /// (inner -> outer) and the cream rim (centre 0.14, width 0.02, 0.45) is unchanged.
    /// </summary>
    public class Phase8ChunkD10Tests
    {
        private const string ShaderPath = "Assets/_Game/Art/Shaders/FenceFadeLit.shader";
        private const string HlslPath = "Assets/_Game/Art/Shaders/CWFenceFade.hlsl";
        private const float Bird = 10f, Front = 8f; // eye depths: a fragment well in front of the bird

        // ---- C# twin of CWDither.hlsl CW_Bayer4 + the clip in CW_FenceFadeClipRim ---------------------------------

        private static float Frac(float v) => v - Mathf.Floor(v);
        private static float Bayer2(float x, float y) { x = Mathf.Floor(x); y = Mathf.Floor(y); return Frac(x * 0.5f + y * y * 0.75f); }
        private static float Bayer4(float x, float y) => Bayer2(0.5f * x, 0.5f * y) * 0.25f + Bayer2(x, y) + 1f / 32f;

        /// <summary>How many pixels of a 4x4 screen tile survive <c>clip(CW_Bayer4(px) - fade - 1e-4)</c>.</summary>
        private static int Survivors(float fade)
        {
            int n = 0;
            for (int y = 0; y < 4; y++)
                for (int x = 0; x < 4; x++)
                    if (Bayer4(x + 0.5f, y + 0.5f) - fade - 1e-4f >= 0f) n++;
            return n;
        }

        [Test]
        public void BayerTwin_CoversTheSixteenThresholds()
        {
            var t = Enumerable.Range(0, 16).Select(i => Bayer4(i % 4 + 0.5f, i / 4 + 0.5f)).OrderBy(v => v).ToArray();
            for (int k = 0; k < 16; k++) Assert.AreEqual((k + 0.5f) / 16f, t[k], 1e-6f, $"threshold {k}");
            Assert.AreEqual(1, Survivors(0.92f), "the pre-D10 peak (0.92) kept 1 pixel per 4x4 tile: the stipple");
        }

        // ---- core: fully clear ------------------------------------------------------------------------------------

        [TestCase(0f)]
        [TestCase(0.03f)]
        [TestCase(0.06f)]
        [TestCase(0.1f)]
        [TestCase(0.12f)] // exactly the inner radius
        public void LocalHole_Core_IsFullyClear(float screenDist)
        {
            float fade = FenceFadeRules.FragmentFade(1f, screenDist, Front, Bird);
            Assert.AreEqual(1f, fade, 1e-6f, "fade == 1 inside the inner radius");
            Assert.AreEqual(0, Survivors(fade), "no Bayer survivors in the core");
        }

        [Test]
        public void ChickenSlotHole_Core_IsFullyClear_OnOuterFences()
        {
            var noLocal = new FenceFadeRules.HoleSample(0f, 0f, Bird);
            foreach (float d in new[] { 0f, 0.5f * FenceFadeRules.InnerRadius, FenceFadeRules.InnerRadius })
            {
                var rival = new[] { new FenceFadeRules.HoleSample(1f, d, Bird) };
                float fade = FenceFadeRules.CombinedFragmentFade(true, Front, noLocal, rival);
                Assert.AreEqual(1f, fade, 1e-6f, $"D4 rival hole at {d}");
                Assert.AreEqual(0, Survivors(fade));
            }
        }

        [Test]
        public void FixedTargetHole_Core_IsFullyClear_AcrossItsWholeShiftedInnerRadius()
        {
            float shift = FenceFadeRules.TargetRadiusShift(2.5f, 11.2f); // the own pad, ~0.27 screen heights
            Assert.Greater(shift, 0.1f);
            foreach (float d in new[] { 0f, 0.1f, FenceFadeRules.InnerRadius + shift })
            {
                float fade = FenceFadeRules.ShiftedFragmentFade(1f, d, shift, Front, Bird);
                Assert.AreEqual(1f, fade, 1e-6f, $"E2 pad hole at {d}");
                Assert.AreEqual(0, Survivors(fade));
            }
        }

        [Test]
        public void CoreFade_IsOne_AndThereIsNoPeakCapLeft()
        {
            Assert.AreEqual(1f, FenceFadeRules.CoreFade);
            Assert.IsNull(typeof(FenceFadeRules).GetField("MaxFade", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static),
                "the 0.92 peak cap is gone (it was the stipple)");
            Assert.AreEqual(0f, FenceFadeRules.ShaderParams.z, 0f, "_CW_FenceFadeParams.z is reserved, not a cap");
        }

        [Test]
        public void StrengthAndDepthGate_StillScaleTheCore()
        {
            // Not part of the stipple: a ghosted rival (VisualOpacity 0.2) still opens only a ~20 % hole, the 0.15 s
            // ease still passes through partial fades, and the fence behind the bird never opens.
            Assert.AreEqual(0.2f, FenceFadeRules.FragmentFade(0.2f, 0f, Front, Bird), 1e-6f);
            Assert.AreEqual(0f, FenceFadeRules.FragmentFade(1f, 0f, 12f, Bird), 1e-6f, "behind the bird stays solid");
            Assert.AreEqual(0.5f, FenceFadeRules.FragmentFade(1f, 0f, Bird - 0.5f * FenceFadeRules.DepthSoftness, Bird), 1e-4f,
                "hugging the fence still ramps over the depth softness");
        }

        // ---- edge band: the dither ramp ---------------------------------------------------------------------------

        [Test]
        public void EdgeBand_RampsFromClearToSolid_WithDitherInBetween()
        {
            float prev = 1f;
            for (int i = 1; i < 10; i++)
            {
                float d = FenceFadeRules.InnerRadius + FenceFadeRules.EdgeBandWidth * i / 10f;
                float fade = FenceFadeRules.FragmentFade(1f, d, Front, Bird);
                Assert.That(fade, Is.GreaterThan(0f).And.LessThan(1f), $"band {i}/10 is dithered");
                Assert.Less(fade, prev, "monotone toward the outer edge");
                prev = fade;
            }
            Assert.AreEqual(0f, FenceFadeRules.FragmentFade(1f, FenceFadeRules.OuterRadius, Front, Bird), 1e-6f, "solid at the outer radius");
            int mid = Survivors(FenceFadeRules.FragmentFade(1f, FenceFadeRules.InnerRadius + 0.5f * FenceFadeRules.EdgeBandWidth, Front, Bird));
            Assert.That(mid, Is.InRange(6, 10), "mid-band is a half screen-door: the 'still a wall' cue lives here now");
            Assert.AreEqual(0.03f, FenceFadeRules.EdgeBandWidth, 1e-6f, "band width (C16) unchanged");
        }

        // ---- rim: unchanged ---------------------------------------------------------------------------------------

        [Test]
        public void Rim_IsUnchanged_AndStillSitsOnSurvivingPickets()
        {
            Assert.AreEqual(0.14f, FenceFadeRules.RimCentre, 1e-6f, "rim centre");
            Assert.AreEqual(0.02f, FenceFadeRules.RimWidth, 1e-6f, "rim width");
            Assert.AreEqual(0.45f, FenceFadeRules.RimIntensity, 1e-6f, "rim intensity");
            Assert.AreEqual(new Vector4(0.14f, 0.01f, 0.45f, 0f), FenceFadeRules.RimParams);
            Assert.AreEqual(0.45f, FenceFadeRules.RimWeight(1f, FenceFadeRules.RimCentre, Front, Bird), 1e-5f, "rim peak");
            Assert.AreEqual(0f, FenceFadeRules.RimWeight(1f, 0f, Front, Bird), 1e-6f, "no rim in the cleared core");
            // C16 measured ~12/16 surviving at the rim centre with the cap; without it the fade there is 0.259 (was 0.238).
            float fade = FenceFadeRules.FragmentFade(1f, FenceFadeRules.RimCentre, Front, Bird);
            Assert.AreEqual(12, Survivors(fade), $"rim-centre survivors (fade {fade:0.000})");
        }

        // ---- shader source ----------------------------------------------------------------------------------------

        [Test]
        public void Shader_FadeAmount_HasNoPeakCap_AndIsTheOnlyFadePathForEveryHole()
        {
            string src = File.ReadAllText(HlslPath);
            var m = Regex.Match(src, @"float CW_FenceFadeAmount\([^)]*\)\s*\{(?<body>.*?)\n\}", RegexOptions.Singleline);
            Assert.IsTrue(m.Success, "CW_FenceFadeAmount not found");
            string body = m.Groups["body"].Value;
            StringAssert.DoesNotContain("_CW_FenceFadeParams.z", body, "no peak cap in the core path");
            StringAssert.DoesNotContain("MaxFade", body);
            StringAssert.DoesNotContain("MaxFade", Regex.Replace(src, @"//[^\n]*", ""), "no MaxFade anywhere in the code");
            Assert.AreEqual(2, Regex.Matches(src, @"\bCW_FenceFadeAmount\(").Count,
                "one definition + one call (CW_FenceHole): local, D4, E2 and inner-wall holes share it");
            Assert.AreEqual(3, Regex.Matches(src, @"\bCW_FenceHole\(_CW_").Count, "local + chicken slots + fixed targets all go through CW_FenceHole");
            StringAssert.Contains("clip(CW_Bayer4(positionCS.xy) - fade - 1e-4);", src);
        }

        [Test]
        public void Shader_ClipRim_StaysSingleExit()
        {
            string src = File.ReadAllText(HlslPath);
            var m = Regex.Match(src, @"float CW_FenceFadeClipRim\(float4 positionCS\)\s*\{(?<body>.*?)\n\}", RegexOptions.Singleline);
            Assert.IsTrue(m.Success);
            Assert.AreEqual(1, Regex.Matches(m.Groups["body"].Value, @"\breturn\b").Count, "C16: one exit point");
        }

        [Test]
        public void Shader_NoNewKeywords()
        {
            string src = File.ReadAllText(ShaderPath) + File.ReadAllText(HlslPath);
            var cw = Regex.Matches(src, @"\b_CW_[A-Z_]+\b").Cast<Match>().Select(x => x.Value).Distinct().OrderBy(x => x).ToArray();
            CollectionAssert.AreEquivalent(new[] { "_CW_FENCE_ALL_BIRDS", "_CW_FENCE_FADE" }, cw);
        }

        [Test]
        public void FadeVariants_CompileClean_OnGles3VulkanAndD3D_InnerAndOuterMaterials()
        {
            var shader = AssetDatabase.LoadAssetAtPath<Shader>(ShaderPath);
            Assert.IsNotNull(shader, ShaderPath);
            Assert.IsFalse(ShaderUtil.ShaderHasError(shader));
            var sub = ShaderUtil.GetShaderData(shader).GetSubshader(0);
            string[] passes = { "ForwardLit", "GBuffer", "DepthOnly", "DepthNormals" };
            string[][] keywordSets =
            {
                new[] { FenceFadeRules.KeywordName },                                         // inner arm walls / coops
                new[] { FenceFadeRules.KeywordName, FenceFadeRules.AllBirdsKeywordName },    // outer runs (D4 + E2)
            };
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
                    foreach (var kw in keywordSets)
                    {
                        var r = pass.CompileVariant(ShaderType.Fragment, kw, plat, target);
                        compiled++;
                        if (!r.Success) problems.AppendLine($"{pass.Name} {plat} [{string.Join(",", kw)}]: failed");
                        foreach (var msg in r.Messages)
                            problems.AppendLine($"{pass.Name} {plat} [{string.Join(",", kw)}]: {msg.severity} {msg.message} ({msg.file}:{msg.line})");
                    }
                }
            }
            Assert.AreEqual(22, compiled, "4 passes x 3 APIs x 2 keyword sets, minus GBuffer on GLES3");
            Assert.IsEmpty(problems.ToString(), "shader compile messages");
        }
    }
}

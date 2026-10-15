using System.Linq;
using CluckWars.Visuals;
using NUnit.Framework;
using UnityEditor;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace CluckWars.Tests
{
    /// <summary>
    /// Phase 8 decision D7 (b) "PC post ON": the PC tier renders <c>CW_MatchProfile</c> (Neutral tonemapping, light
    /// bloom, subtle vignette) plus a PC-only exposure / contrast compensation; the Mobile tier renders no post at all.
    /// </summary>
    /// <remarks>
    /// The profile reaches the match camera as the RP assets' default volume profile (there are no Volume components
    /// in any scene): PC_RPAsset updates the volume stack every frame, MatchCamera.ApplyRenderTier turns the camera's
    /// renderPostProcessing on only under PC_RPAsset. Neutral tonemapping on this LDR-authored scene costs ~8% grass
    /// and ~18% food-pile luma vs Mobile (its shoulder compresses the bright pile); postExposure +0.3 with contrast +20
    /// brings PC to grass +3%, food -6% of Mobile (match-camera Rec.709 luma, Captures/env3d_after/C/D7/). Saturation
    /// is deliberately NOT overridden: contrast already restores it (grass +3%, food -7% vs Mobile).
    /// Bloom threshold 1 only sees HDR (>1) pixels, so PC_RPAsset must keep HDR on: ~4.5-5.4% of the frame is above 1.
    /// </remarks>
    public class Phase8ChunkD7Tests
    {
        private const string ProfilePath   = "Assets/Settings/CW_MatchProfile.asset";
        private const string PcRpAsset     = "Assets/Settings/PC_RPAsset.asset";
        private const string MobileRpAsset = "Assets/Settings/Mobile_RPAsset.asset";

        private const float CompPostExposure = 0.3f;
        private const float CompContrast     = 20f;

        [Test]
        public void PcPostProcessing_IsOn()
        {
            Assert.IsTrue(RenderTierPolicy.PcPostProcessingEnabled, "D7 (b): PC post-processing is ON.");
            Assert.IsTrue(RenderTierPolicy.PostProcessingFor(Rp(PcRpAsset)), "PC tier must render post-processing.");
        }

        [Test]
        public void MobileTier_NeverRendersPost()
        {
            var mobile = Rp(MobileRpAsset);
            Assert.AreNotEqual(RenderTierPolicy.PcRpAssetName, mobile.name);
            Assert.IsFalse(RenderTierPolicy.PostProcessingFor(mobile), "Mobile tier must never render post-processing.");
            Assert.IsFalse(RenderTierPolicy.PostProcessingFor((RenderPipelineAsset)null), "Unknown tier falls back to no post.");
            Assert.IsFalse(mobile.supportsHDR, "Mobile renders LDR (no post consumes an HDR buffer).");
            Assert.AreEqual(VolumeFrameworkUpdateMode.ViaScripting, mobile.volumeFrameworkUpdateMode,
                "Mobile has no post, so it must not pay for a per-frame volume stack update.");
        }

        [Test]
        public void PcTier_HdrOn_VolumesEveryFrame_ProfileIsTheMatchProfile()
        {
            var pc = Rp(PcRpAsset);
            Assert.IsTrue(pc.supportsHDR, "Bloom threshold >= 1 does nothing without an HDR colour buffer.");
            Assert.AreEqual(VolumeFrameworkUpdateMode.EveryFrame, pc.volumeFrameworkUpdateMode);
            Assert.AreEqual(ProfilePath, AssetDatabase.GetAssetPath(pc.volumeProfile),
                "The match profile reaches the camera as PC_RPAsset's default volume profile.");
        }

        [Test]
        public void Profile_HasOnlyTonemapBloomVignetteAndCompensation()
        {
            var names = Profile().components.Select(c => c.GetType().Name).OrderBy(n => n).ToArray();
            CollectionAssert.AreEqual(new[] { "Bloom", "ColorAdjustments", "Tonemapping", "Vignette" }, names,
                "D7: Neutral tonemapping + light bloom + vignette + the PC compensation, nothing else.");
            foreach (var c in Profile().components) Assert.IsTrue(c.active, $"{c.GetType().Name} is inactive");
        }

        [Test]
        public void Tonemapping_IsNeutral()
        {
            Assert.IsTrue(Profile().TryGet<Tonemapping>(out var tm));
            Assert.IsTrue(tm.mode.overrideState);
            Assert.AreEqual(TonemappingMode.Neutral, tm.mode.value);
        }

        [Test]
        public void Bloom_IsLight()
        {
            Assert.IsTrue(Profile().TryGet<Bloom>(out var b));
            Assert.IsTrue(b.threshold.overrideState && b.intensity.overrideState);
            Assert.GreaterOrEqual(b.threshold.value, 1f, "Bloom threshold >= 1: only HDR highlights bloom.");
            Assert.Greater(b.intensity.value, 0f);
            Assert.LessOrEqual(b.intensity.value, 0.25f, "Bloom intensity ~0.2 (light).");
            Assert.IsFalse(b.dirtTexture.overrideState && b.dirtTexture.value != null, "No lens dirt.");
        }

        [Test]
        public void Vignette_IsSubtle()
        {
            Assert.IsTrue(Profile().TryGet<Vignette>(out var v));
            Assert.IsTrue(v.intensity.overrideState);
            Assert.LessOrEqual(v.intensity.value, 0.2f);
        }

        [Test]
        public void Compensation_ExposureAndContrastOnly_Pinned()
        {
            Assert.IsTrue(Profile().TryGet<ColorAdjustments>(out var ca));
            Assert.IsTrue(ca.postExposure.overrideState);
            Assert.AreEqual(CompPostExposure, ca.postExposure.value, 1e-4f, "postExposure re-measured in D7; change only with new captures.");
            Assert.IsTrue(ca.contrast.overrideState);
            Assert.AreEqual(CompContrast, ca.contrast.value, 1e-4f, "contrast re-measured in D7; change only with new captures.");
            Assert.IsFalse(ca.saturation.overrideState, "Saturation is not part of the compensation.");
            Assert.IsFalse(ca.hueShift.overrideState, "No hue shift.");
            Assert.IsFalse(ca.colorFilter.overrideState, "No colour filter (it would tint the whole frame).");
        }

        private static VolumeProfile Profile()
        {
            var p = AssetDatabase.LoadAssetAtPath<VolumeProfile>(ProfilePath);
            Assert.IsNotNull(p, $"{ProfilePath} missing or not a VolumeProfile.");
            return p;
        }

        private static UniversalRenderPipelineAsset Rp(string path)
        {
            var a = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(path);
            Assert.IsNotNull(a, $"{path} failed to load (missing, or a corrupted / zeroed .asset).");
            return a;
        }
    }
}

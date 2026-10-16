using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace CluckWars.Tests
{
    /// <summary>
    /// Phase 8 D2 "PC dark tones": the D7 compensation (contrast 20) plus PC-only SSAO on a dark fence atlas rendered
    /// fence wood 13 % and shaded fence faces ~20 % darker on PC than on Mobile (shaded faces L* 6 PC / 8.5 Mobile).
    /// Fix: contrast 20 -> 10 (postExposure 0.3 -> 0.22 so grass / piles stay matched), a tiny LiftGammaGain lift,
    /// SSAO 0.4 -> 0.25, and the fence atlas' dark wood raised (linear-light gain, chromaticity kept) so shaded faces
    /// render L* >= 15 on both tiers. Match camera, seat 0, masked medians (tools/ui_capture/capture_d2_tones.py):
    /// PC / Mobile fence 0.95-0.96, shaded faces 1.02-1.03 (L* 15.6-15.7 / 15.3-15.4), shadow 0.99-1.03, grass 1.00,
    /// pile 1.03-1.04. Captures in Captures/env3d_after/D/D2/.
    /// </summary>
    public class Phase8ChunkD2Tests
    {
        private const string ProfilePath = "Assets/Settings/CW_MatchProfile.asset";
        private const string PcRenderer  = "Assets/Settings/PC_Renderer.asset";
        private const string FenceAtlas  = "Assets/Generated/PropTextures/wall_segment_albedo.png";

        private static readonly string[] FenceMaterials =
        {
            "Assets/_Game/Art/Materials/Prop_wall_segment.mat",
            "Assets/_Game/Art/Materials/Prop_wall_segment_Inner.mat",
        };

        // Every fence panel mesh that samples the atlas: outer, arm walls (B4 UVs) and both Performance Mode twins (S5).
        private static readonly string[] FenceMeshes =
        {
            "Assets/Generated/wall_segment.fbx",
            "Assets/Generated/wall_segment_perf.fbx",
            "Assets/_Game/Art/Meshes/wall_segment_inner.asset",
            "Assets/_Game/Art/Meshes/wall_segment_inner_perf.asset",
        };

        [Test]
        public void Profile_ContrastHalved_ExposureRetuned()
        {
            Assert.IsTrue(Profile().TryGet<ColorAdjustments>(out var ca));
            Assert.IsTrue(ca.contrast.overrideState);
            Assert.LessOrEqual(ca.contrast.value, 12f, "D2: contrast 20 crushed the dark wood on PC; keep it <= 12.");
            Assert.Greater(ca.contrast.value, 0f, "Some contrast is still needed against the Neutral tonemapper's flattening (D7).");
            Assert.That(ca.postExposure.value, Is.InRange(0.15f, 0.3f), "postExposure re-tuned with the contrast so grass / piles match Mobile.");
        }

        [Test]
        public void Profile_ShadowLift_IsTinyAndNeutral()
        {
            Assert.IsTrue(Profile().TryGet<LiftGammaGain>(out var lgg), "D2 shadows lift lives in CW_MatchProfile (LUT-baked, no per-pixel cost).");
            Assert.IsTrue(lgg.active);
            Assert.IsTrue(lgg.lift.overrideState);
            var l = lgg.lift.value;
            Assert.AreEqual(1f, l.x, 1e-4f); Assert.AreEqual(1f, l.y, 1e-4f); Assert.AreEqual(1f, l.z, 1e-4f);
            Assert.That(l.w, Is.GreaterThan(0f).And.LessThanOrEqualTo(0.01f),
                "Lift offset is a small black-floor lift (0.004 already adds ~10 % to shaded faces); colour stays neutral.");
            Assert.IsFalse(lgg.gamma.overrideState, "No gamma override.");
            Assert.IsFalse(lgg.gain.overrideState, "No gain override.");
        }

        [Test]
        public void PcRenderer_SsaoKept_IntensityReduced()
        {
            var rd = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(PcRenderer);
            Assert.IsNotNull(rd, PcRenderer + " failed to load.");
            var ssao = rd.rendererFeatures.OfType<ScreenSpaceAmbientOcclusion>().SingleOrDefault();
            Assert.IsNotNull(ssao, "SSAO stays on PC_Renderer (F1 haze relies on its depth prepass).");
            float intensity = new SerializedObject(ssao).FindProperty("m_Settings.Intensity").floatValue;
            Assert.That(intensity, Is.GreaterThan(0f).And.LessThanOrEqualTo(0.3f), "D2: SSAO 0.4 -> 0.25 (PC-only darkening of the fence).");
        }

        [Test]
        public void FenceMaterials_ShareTheAtlas()
        {
            var atlas = AssetDatabase.LoadAssetAtPath<Texture2D>(FenceAtlas);
            Assert.IsNotNull(atlas, FenceAtlas);
            foreach (var p in FenceMaterials)
            {
                var m = AssetDatabase.LoadAssetAtPath<Material>(p);
                Assert.IsNotNull(m, p);
                Assert.AreSame(atlas, m.GetTexture("_BaseMap"), p + " must sample the D2 fence atlas.");
            }
        }

        [Test]
        public void FenceAtlas_DarkestWoodFloor_OnEveryPanelMesh()
        {
            var decoded = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            try
            {
                Assert.IsTrue(ImageConversion.LoadImage(decoded, File.ReadAllBytes(Path.Combine(Path.GetDirectoryName(Application.dataPath), FenceAtlas))), FenceAtlas);
                foreach (var path in FenceMeshes)
                {
                    var mesh = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Mesh>().FirstOrDefault();
                    Assert.IsNotNull(mesh, path + " has no mesh.");
                    var l = CentroidLuma(mesh, decoded);
                    l.Sort();
                    float p5 = l[(int)(0.05f * (l.Count - 1))], med = l[l.Count / 2], max = l[l.Count - 1];
                    // B4 floor alone: p5 42.6 (shaded faces L* 6-8.5); D2: p5 ~70.
                    Assert.GreaterOrEqual(p5, 62f, $"{path}: darkest wood (p5 luma {p5:0.0}) below the D2 floor.");
                    Assert.LessOrEqual(med, 95f, $"{path}: median wood luma {med:0.0} - the fence must stay dark wood, not bleached.");
                    Assert.GreaterOrEqual(max, 180f, $"{path}: light wood (max luma {max:0.0}) must be untouched by the floor lift.");
                }
            }
            finally { Object.DestroyImmediate(decoded); }
        }

        // sRGB Rec.709 luma (0-255) of the atlas at every triangle's UV centroid.
        private static List<float> CentroidLuma(Mesh mesh, Texture2D tex)
        {
            var uv = mesh.uv; var tri = mesh.triangles;
            Assert.Greater(uv.Length, 0, mesh.name + " has no UVs.");
            var list = new List<float>(tri.Length / 3);
            for (int t = 0; t < tri.Length; t += 3)
            {
                var u = (uv[tri[t]] + uv[tri[t + 1]] + uv[tri[t + 2]]) / 3f;
                var c = tex.GetPixelBilinear(u.x, u.y);
                list.Add(255f * (0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b));
            }
            return list;
        }

        private static VolumeProfile Profile()
        {
            var p = AssetDatabase.LoadAssetAtPath<VolumeProfile>(ProfilePath);
            Assert.IsNotNull(p, $"{ProfilePath} missing or not a VolumeProfile.");
            return p;
        }
    }
}

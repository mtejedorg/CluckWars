using System;
using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace CluckWars.Tests
{
    /// <summary>
    /// Phase 8 decision E3 (a) "grass: same brightness, less saturation, less repetition" on <c>GrassFloor.mat</c>
    /// (the GeneratedGround cube in Map.unity; a Cube primitive's top face is UV 0..1, so the tiling lives on the material):
    /// the base map tiles 5x5 instead of 7x7, the tint keeps D8's brightness with ~20 % less chroma, and a stock URP Lit
    /// Detail Albedo x2 map (<c>grass_macro_variation.png</c>, one period across the whole floor) adds a large soft colour
    /// variation (olive / yellow vs deeper green, a few % luma) so the repeat no longer reads as a grid.
    /// </summary>
    /// <remarks>
    /// Gamma colour space: material colour x sRGB texel directly (see <see cref="Phase8ChunkD8Tests"/>), and the detail map's
    /// texel is used as stored (x2), so 127.5 is neutral. Rendered numbers (match camera, both tiers, Performance Mode off,
    /// sunlit grass by the D8 render-pair mask): luma 158.0 / 158.2 -> 157.7-158.4 / 156.5-157.8, CIELAB C* 67.0 / 69.7 ->
    /// 53.7-53.9 / 56.4-56.8 (PC / Mobile). Captures + tables: Captures/env3d_after/D/E3/.
    /// Anisotropic filtering: a TextureImporter has no per-platform aniso, so "aniso on PC only" is a quality-level property:
    /// the PC level forces anisotropic filtering on every texture (ForceEnable), the Mobile level (Android default) only
    /// honours each texture's own level (Enable) and the grass keeps level 1 (= off), so Android stays as it was.
    /// </remarks>
    public class Phase8ChunkE3Tests
    {
        private const string MacroPath = "Assets/Generated/Env/Textures/grass_macro_variation.png";

        /// <summary>D8's material-level grass luma (texture mean x (0.869, 0.827, 0.710)), what rendered at luma ~158.</summary>
        private const float D8MaterialLuma = 129.6f;
        /// <summary>D8's material-level CIELAB chroma of the same mean (60.4); E3 cuts it by at least 15 %.</summary>
        private const float D8MaterialChroma = 60.4f;

        private static Material Grass()
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>(Phase8ChunkD8Tests.GrassPath);
            Assert.IsNotNull(m, Phase8ChunkD8Tests.GrassPath + " is missing.");
            return m;
        }

        private static Color MeanOf(Color[] px)
        {
            double r = 0, g = 0, b = 0;
            foreach (var c in px) { r += c.r; g += c.g; b += c.b; }
            return new Color((float)(r / px.Length), (float)(g / px.Length), (float)(b / px.Length), 1f);
        }

        private static Color[] MacroTexels(out int width)
        {
            var decoded = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            try
            {
                Assert.IsTrue(ImageConversion.LoadImage(decoded, File.ReadAllBytes(
                    Path.Combine(Path.GetDirectoryName(Application.dataPath), MacroPath))), MacroPath);
                width = decoded.width;
                return decoded.GetPixels();
            }
            finally { Object.DestroyImmediate(decoded); }
        }

        [Test]
        public void GrassFloor_Retiled_FourToFive()
        {
            var m = Grass();
            var t = m.GetTextureScale("_BaseMap");
            Assert.AreEqual(t.x, t.y, 1e-4f, "square tiling");
            Assert.That(t.x, Is.InRange(4f, 5f), "E3: 7x7 -> 4-5");
            Assert.AreEqual(t, m.GetTextureScale("_MainTex"), "_MainTex mirrors _BaseMap");
            Assert.AreEqual("GrassCalm", m.GetTexture("_BaseMap").name, "same grass texture");
        }

        [Test]
        public void GrassFloor_KeepsD8Brightness_WithLessChroma()
        {
            var mean = MeanOf(Phase8ChunkD8Tests.TintedTexels(Grass()));
            float luma = Phase8ChunkD8Tests.Luma709(mean) * 255f;
            var lab = Phase8ChunkD8Tests.ToLab(mean);
            float chroma = Mathf.Sqrt(lab.y * lab.y + lab.z * lab.z);
            Assert.AreEqual(D8MaterialLuma, luma, 3f, "material-level luma stays at D8's (renders ~158)");
            Assert.LessOrEqual(chroma, D8MaterialChroma * 0.85f, $"chroma {chroma:0.0}: at least 15 % below D8's {D8MaterialChroma}");
            Assert.GreaterOrEqual(chroma, D8MaterialChroma * 0.7f, $"chroma {chroma:0.0}: not more than 30 % below D8 (grey / muddy)");
            float hue = Mathf.Atan2(lab.z, lab.y) * Mathf.Rad2Deg;
            Assert.That(hue, Is.InRange(120f, 140f), "still grass green (CIELAB hue)");
        }

        [Test]
        public void GrassFloor_HasTheMacroVariation_AsStockLitDetailAlbedoX2_OnePeriodAcrossTheFloor()
        {
            var m = Grass();
            Assert.AreEqual("Universal Render Pipeline/Lit", m.shader.name, "stock URP Lit, no custom shader");
            Assert.AreEqual(MacroPath, AssetDatabase.GetAssetPath(m.GetTexture("_DetailAlbedoMap")), "detail albedo map");
            Assert.IsTrue(m.IsKeywordEnabled("_DETAIL_MULX2"), "_DETAIL_MULX2 (plain x2, no extra scale maths)");
            Assert.IsFalse(m.IsKeywordEnabled("_DETAIL_SCALED"), "_DETAIL_SCALED off");
            Assert.AreEqual(1f, m.GetFloat("_DetailAlbedoMapScale"), 1e-4f, "scale 1 keeps the cheap _DETAIL_MULX2 path");
            Assert.IsNull(m.GetTexture("_DetailNormalMap"), "no detail normal map");
            Assert.IsNull(m.GetTexture("_DetailMask"), "no detail mask (default white)");
            // URP Lit samples the detail at baseUv * detailScale, baseUv = uv0 * base tiling: periods across the floor = product.
            float periods = m.GetTextureScale("_DetailAlbedoMap").x * m.GetTextureScale("_BaseMap").x;
            Assert.AreEqual(m.GetTextureScale("_DetailAlbedoMap").x, m.GetTextureScale("_DetailAlbedoMap").y, 1e-4f, "square detail tiling");
            Assert.That(periods, Is.InRange(0.99f, 2.01f), "one period spans at least half the arena (1 = the whole floor)");
            Assert.IsTrue(m.enableInstancing, "GPU instancing stays on");
        }

        [Test]
        public void MacroVariation_IsNeutralOnAverage_Subtle_Smooth_AndTiles()
        {
            var px = MacroTexels(out int w);
            int h = px.Length / w;
            Assert.AreEqual(w, h, "square");
            var mean = MeanOf(px);
            foreach (var (ch, v) in new[] { ("r", mean.r), ("g", mean.g), ("b", mean.b) })
                Assert.AreEqual(0.5f, v, 1.5f / 255f, $"mean {ch} is neutral for x2 (the floor's average colour is unchanged)");

            double maxDev = 0, covRb = 0, lumaDev2 = 0, step = 0, seam = 0;
            for (int i = 0; i < px.Length; i++)
            {
                var c = px[i];
                maxDev = Math.Max(maxDev, Math.Max(Math.Abs(c.r - 0.5), Math.Max(Math.Abs(c.g - 0.5), Math.Abs(c.b - 0.5))));
                covRb += (c.r - 0.5) * (c.b - 0.5);
                double dl = Phase8ChunkD8Tests.Luma709(c) * 2 - 1;
                lumaDev2 += dl * dl;
                int x = i % w, y = i / w;
                var right = px[y * w + (x + 1) % w];   // wraps: the seam counts like any other neighbour pair
                double d = Math.Abs(right.g - c.g);
                if (x == w - 1) seam = Math.Max(seam, d); else step = Math.Max(step, d);
            }
            Assert.LessOrEqual(maxDev * 2, 0.10, "every channel within +-10 % (subtle: no stains / mud)");
            Assert.LessOrEqual(Math.Sqrt(lumaDev2 / px.Length), 0.03, "luma variation RMS <= 3 %");
            Assert.Greater(Math.Sqrt(lumaDev2 / px.Length), 0.005, "but there is a variation");
            Assert.Less(covRb, 0, "warm patches are olive / yellow: red up where blue goes down");
            Assert.LessOrEqual(step, 2.5 / 255.0, "soft, low frequency (neighbour texels within dither + gradient)");
            Assert.LessOrEqual(seam, 2.5 / 255.0, "tiles seamlessly (the wrap seam is no harder than any other step)");
        }

        [Test]
        public void MacroVariation_Import_IsUncompressedLinearRepeat()
        {
            var ti = (TextureImporter)AssetImporter.GetAtPath(MacroPath);
            Assert.IsNotNull(ti, MacroPath);
            Assert.IsFalse(ti.sRGBTexture, "linear, like the hedge macro map (a gamma project samples it as stored either way)");
            Assert.AreEqual(TextureImporterCompression.Uncompressed, ti.textureCompression, "a +-8 % gradient bands under block compression");
            foreach (var platform in new[] { "Standalone", "Android", "iPhone" })
                Assert.IsFalse(ti.GetPlatformTextureSettings(platform).overridden, $"{platform}: no override re-compressing it");
            Assert.AreEqual(TextureWrapMode.Repeat, ti.wrapMode, "repeat");
            Assert.IsTrue(ti.mipmapEnabled, "mipmaps");
            Assert.LessOrEqual(ti.maxTextureSize, 256, "small: 256 x 256 RGB24");
        }

        [Test]
        public void Anisotropic_OnPcOnly_GrassKeepsLevelOneForMobile()
        {
            Assert.AreEqual((int)AnisotropicFiltering.ForceEnable, QualityLevelInt("PC", "anisotropicTextures"), "PC forces aniso on every texture");
            Assert.AreEqual((int)AnisotropicFiltering.Enable, QualityLevelInt("Mobile", "anisotropicTextures"), "Mobile honours the per-texture level");
            var ti = (TextureImporter)AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(Grass().GetTexture("_BaseMap")));
            Assert.LessOrEqual(ti.anisoLevel, 1, "GrassCalm level <= 1: no aniso on Mobile / Android (unchanged), forced on by the PC level");
        }

        private static int QualityLevelInt(string levelName, string field)
        {
            var qs = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/QualitySettings.asset");
            Assert.IsNotEmpty(qs, "QualitySettings.asset failed to load.");
            var levels = new SerializedObject(qs[0]).FindProperty("m_QualitySettings");
            for (int i = 0; i < levels.arraySize; i++)
            {
                var level = levels.GetArrayElementAtIndex(i);
                if (level.FindPropertyRelative("name").stringValue == levelName)
                    return level.FindPropertyRelative(field).intValue;
            }
            Assert.Fail($"No quality level named '{levelName}'.");
            return -1;
        }
    }
}

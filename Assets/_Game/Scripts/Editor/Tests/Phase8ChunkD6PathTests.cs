using System.IO;
using CluckWars.EditorTools;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace CluckWars.Tests
{
    /// <summary>
    /// Phase 8 D6 (audit L06): the worn dirt paths sit just BELOW the E3 grass instead of above it, and their wear holes run
    /// along the edges only (no "leopard spots" through the middle). Asserted on the shipped strip
    /// (<see cref="GroundDressingBaker.PathTexturePath"/>, V across the path) and the baker's band rule.
    /// (<c>Phase8ChunkD6Tests</c> is chunk C's Speedy model.)
    /// </summary>
    /// <remarks>
    /// Strip before -> after: kept-texel luma 139.0 -> 119.3 (x <see cref="GroundDressingBaker.PathAlbedoScale"/> 0.86) vs the grass
    /// material mean (GrassCalm x tint) 121.6. Match camera (Captures/env3d_after/D/D6, Performance Mode off): path / grass luma
    /// 1.06-1.08 -> 0.93-0.95 PC, 1.06-1.07 -> 0.92 Mobile (path ~145-150, grass ~157-158); holes in the path's inner 60 %
    /// 11-16 % -> 0-0.7 %.
    /// </remarks>
    public class Phase8ChunkD6PathTests
    {
        private static Color32[] Load(string path, out int w, out int h)
        {
            var t = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            try
            {
                Assert.IsTrue(ImageConversion.LoadImage(t, File.ReadAllBytes(Path.Combine(Path.GetDirectoryName(Application.dataPath), path))), path);
                w = t.width; h = t.height;
                return t.GetPixels32();
            }
            finally { Object.DestroyImmediate(t); }
        }

        private static float Luma(Color c) => 0.299f * c.r + 0.587f * c.g + 0.114f * c.b;

        private static Color GrassMaterialMean()
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>(Phase8ChunkD8Tests.GrassPath);
            Assert.IsNotNull(m, Phase8ChunkD8Tests.GrassPath);
            var tex = m.GetTexture("_BaseMap");
            Assert.IsNotNull(tex, "grass base map");
            var px = Load(AssetDatabase.GetAssetPath(tex), out _, out _);
            double r = 0, g = 0, b = 0;
            foreach (var c in px) { r += c.r; g += c.g; b += c.b; }
            var tint = m.GetColor("_BaseColor");
            return new Color((float)(r / px.Length / 255.0) * tint.r, (float)(g / px.Length / 255.0) * tint.g, (float)(b / px.Length / 255.0) * tint.b, 1f);
        }

        [Test]
        public void PathAlbedo_JustBelowTheGrass()
        {
            var px = Load(GroundDressingBaker.PathTexturePath, out _, out _);
            double sum = 0; int n = 0;
            foreach (var c in px)
                if (c.a / 255f >= GroundDressingBaker.PathCutoff) { sum += Luma(c); n++; }
            Assert.Greater(n, 10000);
            float path = (float)(sum / n), grass = Luma(GrassMaterialMean());   // Color32 -> Color: both 0..1
            Assert.Less(path, grass, $"path albedo luma {path * 255:0.0} must sit below the grass material's {grass * 255:0.0} (was 139.0: rendered ~7 % above the grass)");
            Assert.Greater(path, 0.85f * grass, $"path {path * 255:0.0} vs grass {grass * 255:0.0}: just below, not a dark stripe");
        }

        [Test]
        public void HoleBand_ZeroInTheCentre_FullAtTheEdges()
        {
            for (float v = 0.5f - GroundDressingBaker.PathHoleSolidHalfBand; v <= 0.5f + GroundDressingBaker.PathHoleSolidHalfBand; v += 0.01f)
                Assert.AreEqual(0f, GroundDressingBaker.PathHoleBand(v), 1e-6f, "no wear holes at v " + v);
            Assert.AreEqual(1f, GroundDressingBaker.PathHoleBand(0.1f), 1e-6f, "edge band keeps its holes");
            Assert.AreEqual(1f, GroundDressingBaker.PathHoleBand(0.9f), 1e-6f, "edge band keeps its holes");
            Assert.GreaterOrEqual(GroundDressingBaker.PathHoleSolidHalfBand, 0.24f, "solid band covers the inner 60 % of the ~0.8-wide visible strip");
        }

        [Test]
        public void PathStrip_InnerSixtyPercent_HasNoHoles()
        {
            var px = Load(GroundDressingBaker.PathTexturePath, out int w, out int h);
            int holes = 0, total = 0;
            for (int y = 0; y < h; y++)
            {
                float v = (y + 0.5f) / h;
                if (Mathf.Abs(v - 0.5f) >= 0.24f) continue;   // visible strip spans v ~0.1-0.9: inner 60 % = |v - 0.5| < 0.24
                for (int x = 0; x < w; x++) { total++; if (px[y * w + x].a / 255f < GroundDressingBaker.PathCutoff) holes++; }
            }
            Assert.Greater(total, 0);
            Assert.LessOrEqual(holes / (float)total, 0.002f, $"{holes} clipped texels in the path's centre band (the render showed 11-16 % holes there: leopard spots)");
        }
    }
}

using System;
using System.IO;
using CluckWars.UI;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace CluckWars.Tests
{
    /// <summary>
    /// Phase 8 decision D8 (a) "ground colour": the grass floor is toned down from the loudest surface on screen
    /// (match-camera grass luma ~190 -> ~158, a touch warmer) by a <c>_BaseColor</c> tint on GrassFloor.mat, and the
    /// cover crates move from P1-orange to a weathered brown by a tint on Prop_crate.mat. Textures are untouched.
    /// </summary>
    /// <remarks>
    /// The project renders in GAMMA colour space (ProjectSettings m_ActiveColorSpace 0), so a material colour multiplies
    /// the sRGB texel directly: the "material colour" here is texel (sRGB bytes, decoded from the PNG so no import setting
    /// matters) x tint, no linear conversion. Two crate colours are held &gt;= 15 CIEDE2000 from every PlayerPalette colour:
    /// the texture mean and the mean of its lightest decile. The lightest decile is the one that matters: those are the
    /// sun-facing planks, and before D8 they sat at dE00 9.6 from P1 orange (the rendered sunlit face measured 5.3).
    /// Rendered numbers (match camera, both tiers): Captures/env3d_after/C/D8/.
    /// </remarks>
    public class Phase8ChunkD8Tests
    {
        private const string GrassPath = "Assets/_Game/Materials/GrassFloor.mat";
        private const string CratePath = "Assets/_Game/Art/Materials/Prop_crate.mat";
        private const float MinCrateDeltaE = 15f;

        private static readonly Color GrassTint = new Color(0.869f, 0.827f, 0.71f, 1f);
        private static readonly Color CrateTint = new Color(0.61f, 0.69f, 0.84f, 1f);

        private static Material Load(string path)
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            Assert.IsNotNull(m, $"{path} is missing.");
            return m;
        }

        private static void AssertColor(Color expected, Color actual, string what)
        {
            Assert.AreEqual(expected.r, actual.r, 1e-3f, what + ".r");
            Assert.AreEqual(expected.g, actual.g, 1e-3f, what + ".g");
            Assert.AreEqual(expected.b, actual.b, 1e-3f, what + ".b");
            Assert.AreEqual(1f, actual.a, 1e-3f, what + ".a");
        }

        /// <summary>The material's base map decoded from its PNG, as sRGB texels times the material's _BaseColor
        /// (or bare, without the tint).</summary>
        private static Color[] TintedTexels(Material m, bool applyTint = true)
        {
            var tex = m.GetTexture("_BaseMap");
            Assert.IsNotNull(tex, $"{m.name} has no base map");
            string path = AssetDatabase.GetAssetPath(tex);
            Color tint = applyTint ? m.GetColor("_BaseColor") : Color.white;
            var decoded = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            try
            {
                Assert.IsTrue(ImageConversion.LoadImage(decoded, File.ReadAllBytes(
                    Path.Combine(Path.GetDirectoryName(Application.dataPath), path))), path);
                var px = decoded.GetPixels();
                for (int i = 0; i < px.Length; i++) px[i] = new Color(px[i].r * tint.r, px[i].g * tint.g, px[i].b * tint.b, 1f);
                return px;
            }
            finally { Object.DestroyImmediate(decoded); }
        }

        private static float Luma709(Color c) => 0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b;

        private static Color Mean(Color[] px, int from, int to, int[] order)
        {
            double r = 0, g = 0, b = 0;
            for (int k = from; k < to; k++) { var c = px[order[k]]; r += c.r; g += c.g; b += c.b; }
            int n = to - from;
            return new Color((float)(r / n), (float)(g / n), (float)(b / n), 1f);
        }

        /// <summary>(mean of all texels, mean of the lightest decile by Rec.709 luma).</summary>
        private static (Color mean, Color lightestDecile) Stats(Color[] px)
        {
            var order = new int[px.Length];
            var lum = new float[px.Length];
            for (int i = 0; i < px.Length; i++) { order[i] = i; lum[i] = Luma709(px[i]); }
            Array.Sort(order, (x, y) => lum[x].CompareTo(lum[y]));
            return (Mean(px, 0, px.Length, order), Mean(px, px.Length - px.Length / 10, px.Length, order));
        }

        [Test]
        public void GrassFloor_TintIsPinned_TextureUntouched()
        {
            var m = Load(GrassPath);
            AssertColor(GrassTint, m.GetColor("_BaseColor"), "GrassFloor _BaseColor");
            AssertColor(GrassTint, m.GetColor("_Color"), "GrassFloor _Color (legacy mirror)");
            Assert.AreEqual("GrassCalm", m.GetTexture("_BaseMap").name, "D8 is a tint, not a texture swap");
        }

        [Test]
        public void Crate_TintIsPinned_TextureUntouched()
        {
            var m = Load(CratePath);
            AssertColor(CrateTint, m.GetColor("_BaseColor"), "Prop_crate _BaseColor");
            AssertColor(CrateTint, m.GetColor("_Color"), "Prop_crate _Color (legacy mirror)");
            Assert.AreEqual("crate_albedo", m.GetTexture("_BaseMap").name, "D8 is a tint, not a texture swap");
        }

        [Test]
        public void GrassFloor_TintedColour_IsDimmerAndWarmerThanTheBareTexture()
        {
            var m = Load(GrassPath);
            var (mean, _) = Stats(TintedTexels(m));
            var (bare, _) = Stats(TintedTexels(m, applyTint: false));
            float bareLuma = Luma709(bare), bareRg = bare.r / bare.g, bareBg = bare.b / bare.g;
            float luma = Luma709(mean);
            // Material-level band (texture x tint, sRGB 0..1); the match camera renders it at luma ~158 of 255.
            Assert.That(luma * 255f, Is.InRange(120f, 140f), "tinted grass mean luma (material level)");
            Assert.Less(luma, bareLuma * 0.85f, "D8 dims the grass");
            Assert.Greater(mean.r / mean.g, bareRg, "warmer: more red relative to green (toward yellow)");
            Assert.Less(mean.b / mean.g, bareBg, "warmer: less blue relative to green");
            Assert.Greater(mean.g - mean.b, 0.35f, "still a saturated, cheerful green (not olive / grey)");
        }

        [Test]
        public void Crate_MaterialColour_IsWellAwayFromEveryPlayerColour()
        {
            var (mean, lightest) = Stats(TintedTexels(Load(CratePath)));
            var palette = new (string name, Color c)[]
            {
                ("P1", PlayerPalette.P1), ("P2", PlayerPalette.P2), ("P3", PlayerPalette.P3), ("P4", PlayerPalette.P4),
                ("Neutral", PlayerPalette.Neutral),
            };
            foreach (var (what, crate) in new[] { ("texture mean x tint", mean), ("lightest decile x tint (sunlit planks)", lightest) })
                foreach (var (name, c) in palette)
                {
                    float de = DeltaE2000(crate, c);
                    Assert.GreaterOrEqual(de, MinCrateDeltaE, $"crate {what} {crate} vs {name}: dE00 {de:0.0}");
                }
        }

        [TestCase(GrassPath)]
        [TestCase(CratePath)]
        public void Materials_StayMatteAndInstanced(string path)
        {
            // Same recipe as Phase8ChunkA2Tests (A2): D8 must not have re-saved them glossy.
            var m = Load(path);
            Assert.AreEqual("Universal Render Pipeline/Lit", m.shader.name, $"{path}: shader");
            Assert.LessOrEqual(m.GetFloat("_Smoothness"), 0.1f, $"{path}: _Smoothness");
            Assert.AreEqual(0f, m.GetFloat("_SpecularHighlights"), $"{path}: _SpecularHighlights");
            Assert.IsTrue(m.IsKeywordEnabled("_SPECULARHIGHLIGHTS_OFF"), $"{path}: _SPECULARHIGHLIGHTS_OFF keyword");
            Assert.AreEqual(0f, m.GetFloat("_EnvironmentReflections"), $"{path}: _EnvironmentReflections");
            Assert.IsTrue(m.IsKeywordEnabled("_ENVIRONMENTREFLECTIONS_OFF"), $"{path}: _ENVIRONMENTREFLECTIONS_OFF keyword");
            Assert.IsTrue(m.enableInstancing, $"{path}: GPU instancing");
        }

        [Test]
        public void DeltaE2000_MatchesThePublishedReferencePair()
        {
            // Sharma, Wu & Dalal (2005), pair 1.
            Assert.AreEqual(2.0425, DeltaE2000Lab(50, 2.6772, -79.7751, 50, 0, -82.7485), 1e-3);
        }

        // ---- CIEDE2000 (sRGB D65 -> CIELAB -> dE00), same formulation as AbilityPaletteTests ----------------

        internal static float DeltaE2000(Color a, Color b)
        {
            var la = ToLab(a); var lb = ToLab(b);
            return (float)DeltaE2000Lab(la.x, la.y, la.z, lb.x, lb.y, lb.z);
        }

        internal static Vector3 ToLab(Color c)
        {
            static double Lin(double v) => v <= 0.04045 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4);
            double r = Lin(c.r), g = Lin(c.g), bl = Lin(c.b);
            double x = (0.4124 * r + 0.3576 * g + 0.1805 * bl) / 0.95047;
            double y = 0.2126 * r + 0.7152 * g + 0.0722 * bl;
            double z = (0.0193 * r + 0.1192 * g + 0.9505 * bl) / 1.08883;
            static double F(double t) => t > 216.0 / 24389.0 ? Math.Cbrt(t) : (24389.0 / 27.0 * t + 16.0) / 116.0;
            double fx = F(x), fy = F(y), fz = F(z);
            return new Vector3((float)(116 * fy - 16), (float)(500 * (fx - fy)), (float)(200 * (fy - fz)));
        }

        private static double DeltaE2000Lab(double l1, double a1, double b1, double l2, double a2, double b2)
        {
            const double Deg = Math.PI / 180.0;
            double c1 = Math.Sqrt(a1 * a1 + b1 * b1), c2 = Math.Sqrt(a2 * a2 + b2 * b2);
            double cb7 = Math.Pow((c1 + c2) / 2, 7);
            double g = 0.5 * (1 - Math.Sqrt(cb7 / (cb7 + Math.Pow(25, 7))));
            double a1p = (1 + g) * a1, a2p = (1 + g) * a2;
            double c1p = Math.Sqrt(a1p * a1p + b1 * b1), c2p = Math.Sqrt(a2p * a2p + b2 * b2);
            double h1p = (Math.Atan2(b1, a1p) / Deg + 360) % 360, h2p = (Math.Atan2(b2, a2p) / Deg + 360) % 360;

            double dLp = l2 - l1, dCp = c2p - c1p, dhp = 0;
            if (c1p * c2p != 0)
            {
                dhp = h2p - h1p;
                if (dhp > 180) dhp -= 360; else if (dhp < -180) dhp += 360;
            }
            double dHp = 2 * Math.Sqrt(c1p * c2p) * Math.Sin(dhp / 2 * Deg);

            double lbp = (l1 + l2) / 2, cbp = (c1p + c2p) / 2, hbp;
            if (c1p * c2p == 0) hbp = h1p + h2p;
            else if (Math.Abs(h1p - h2p) <= 180) hbp = (h1p + h2p) / 2;
            else hbp = h1p + h2p < 360 ? (h1p + h2p + 360) / 2 : (h1p + h2p - 360) / 2;

            double t = 1 - 0.17 * Math.Cos((hbp - 30) * Deg) + 0.24 * Math.Cos(2 * hbp * Deg)
                       + 0.32 * Math.Cos((3 * hbp + 6) * Deg) - 0.20 * Math.Cos((4 * hbp - 63) * Deg);
            double dTheta = 30 * Math.Exp(-Math.Pow((hbp - 275) / 25, 2));
            double cbp7 = Math.Pow(cbp, 7);
            double rc = 2 * Math.Sqrt(cbp7 / (cbp7 + Math.Pow(25, 7)));
            double sl = 1 + 0.015 * Math.Pow(lbp - 50, 2) / Math.Sqrt(20 + Math.Pow(lbp - 50, 2));
            double sc = 1 + 0.045 * cbp, sh = 1 + 0.015 * cbp * t;
            double rt = -Math.Sin(2 * dTheta * Deg) * rc;
            double tl = dLp / sl, tc = dCp / sc, th = dHp / sh;
            return Math.Sqrt(tl * tl + tc * tc + th * th + rt * tc * th);
        }
    }
}

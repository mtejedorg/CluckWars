using System.Collections.Generic;
using System.IO;
using CluckWars.EditorTools;
using CluckWars.Gameplay;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace CluckWars.Tests
{
    /// <summary>
    /// Phase 8 D5 (audit L05): the ground decals stop competing with the play marks. Flowers: fewer dots, warm cream / yellow /
    /// a little pink instead of white, ~30 % darker. Mud: a dark wet blue-grey, out of the pile-disc brown family (the in-arena
    /// puddles were re-tinted, not removed). Asserted on the shipped atlas (<see cref="GroundDressingBaker.DecalAtlasPath"/>:
    /// flower half left, mud half right), with the texels the 0.4 alpha cutoff keeps.
    /// </summary>
    /// <remarks>
    /// Atlas before -> after: flower kept-texel luma 211.5 -> 148.0, near-white texels 52 % -> 0 %, warm-hue texels 91 % -> 99.5 %,
    /// mean HSV saturation 0.25 -> 0.56, dots (alpha blobs) 111 -> 64; mud mean (93, 68, 47) -> (60, 66, 76), CIEDE2000 vs the
    /// PileZone disc 1.7 -> 18.8. Match camera (Captures/env3d_after/D/D5): flower luma 194-207 -> 158-173 PC / 175-201 -> 138-167
    /// Mobile, dots -40 %; mud vs the visible pile disc dE00 1.4-4.2 -> 16.7-23.2. Sources: tools/art/d5_decals/make_d5_decals.py.
    /// </remarks>
    public class Phase8ChunkD5DecalTests
    {
        private const string PileZoneMaterial = "Assets/_Game/Art/Materials/PileZone.mat";
        private const string ColorSchemePath = "Assets/_Game/Data/ColorScheme.asset";
        private const int Half = 512;

        private static Color32[] Atlas(out int width, out int height)
        {
            var t = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            try
            {
                Assert.IsTrue(ImageConversion.LoadImage(t, File.ReadAllBytes(
                    Path.Combine(Path.GetDirectoryName(Application.dataPath), GroundDressingBaker.DecalAtlasPath))), GroundDressingBaker.DecalAtlasPath);
                width = t.width; height = t.height;
                return t.GetPixels32();
            }
            finally { Object.DestroyImmediate(t); }
        }

        private static bool Kept(Color32 c) => c.a / 255f >= GroundDressingBaker.DecalCutoff;
        private static float Luma(Color32 c) => 0.299f * c.r + 0.587f * c.g + 0.114f * c.b;

        /// <summary>Kept texels of one atlas half (0 = flower, 1 = mud).</summary>
        private static List<Color32> KeptTexels(int half)
        {
            var px = Atlas(out int w, out int h);
            Assert.AreEqual(2 * Half, w); Assert.AreEqual(Half, h);
            var list = new List<Color32>();
            for (int y = 0; y < h; y++)
                for (int x = half * Half; x < (half + 1) * Half; x++)
                    if (Kept(px[y * w + x])) list.Add(px[y * w + x]);
            Assert.Greater(list.Count, 1000, "atlas half " + half + " has almost no kept texels");
            return list;
        }

        private static Color Mean(List<Color32> px)
        {
            double r = 0, g = 0, b = 0;
            foreach (var c in px) { r += c.r; g += c.g; b += c.b; }
            return new Color((float)(r / px.Count / 255.0), (float)(g / px.Count / 255.0), (float)(b / px.Count / 255.0), 1f);
        }

        [Test]
        public void Baker_PacksTheD5Sources()
        {
            Assert.AreEqual("flower_patch_decal_d5.png", GroundDressingBaker.FlowerSource);
            Assert.AreEqual("mud_puddle_decal_d5.png", GroundDressingBaker.MudSource);
            foreach (var f in new[] { GroundDressingBaker.FlowerSource, GroundDressingBaker.MudSource })
                Assert.IsTrue(File.Exists(Path.Combine(Path.GetDirectoryName(Application.dataPath), "Assets/Generated/Env/Textures", f)), f + " missing");
        }

        [Test]
        public void Flowers_AboutThirtyPercentDarker()
        {
            var px = KeptTexels(0);
            double sum = 0; foreach (var c in px) sum += Luma(c);
            float mean = (float)(sum / px.Count);
            Assert.That(mean, Is.InRange(120f, 165f), $"flower kept-texel luma {mean:0.0} (was 211.5: the busiest marks in the frame; ~30 % darker = ~148)");
        }

        [Test]
        public void Flowers_WarmCreamYellowPink_NotWhite()
        {
            var px = KeptTexels(0);
            int white = 0, warm = 0; double sat = 0;
            foreach (var c in px)
            {
                if (c.r > 200 && c.g > 200 && c.b > 200) white++;
                Color.RGBToHSV(c, out float h, out float s, out _);
                if (h <= 60f / 360f || h >= 300f / 360f) warm++;
                sat += s;
            }
            Assert.LessOrEqual(white / (float)px.Count, 0.05f, "near-white flower texels (was 52 %)");
            Assert.GreaterOrEqual(warm / (float)px.Count, 0.95f, "hue band: cream / yellow / pink (0-60 deg or 300-360 deg)");
            Assert.GreaterOrEqual(sat / px.Count, 0.35, "mean saturation: cream and yellow, not white (was 0.25)");
        }

        [Test]
        public void Flowers_FewerDots()
        {
            var px = Atlas(out int w, out int h);
            var seen = new bool[Half * h];
            int dots = 0; var stack = new Stack<int>();
            for (int i = 0; i < Half * h; i++)
            {
                int x0 = i % Half, y0 = i / Half;
                if (seen[i] || !Kept(px[y0 * w + x0])) continue;
                dots++; seen[i] = true; stack.Push(i);
                while (stack.Count > 0)
                {
                    int j = stack.Pop(); int x = j % Half, y = j / Half;
                    for (int d = 0; d < 4; d++)
                    {
                        int nx = x + (d == 0 ? 1 : d == 1 ? -1 : 0), ny = y + (d == 2 ? 1 : d == 3 ? -1 : 0);
                        if (nx < 0 || ny < 0 || nx >= Half || ny >= h) continue;
                        int k = ny * Half + nx;
                        if (!seen[k] && Kept(px[ny * w + nx])) { seen[k] = true; stack.Push(k); }
                    }
                }
            }
            Assert.That(dots, Is.InRange(40, 75), $"flower dots {dots} (was 111; fewer, but still a patch)");
        }

        [Test]
        public void Mud_NotInThePileDiscFamily()
        {
            var mud = Mean(KeptTexels(1));
            var disc = AssetDatabase.LoadAssetAtPath<Material>(PileZoneMaterial);
            Assert.IsNotNull(disc, PileZoneMaterial);
            var discColour = disc.GetColor("_BaseColor");
            float dDisc = Phase8ChunkD8Tests.DeltaE2000(mud, discColour);
            Assert.GreaterOrEqual(dDisc, 12f, $"mud {mud} vs pile disc {discColour}: dE00 {dDisc:0.0} (was 1.7)");

            var scheme = AssetDatabase.LoadAssetAtPath<ColorSchemeSO>(ColorSchemePath);
            Assert.IsNotNull(scheme, ColorSchemePath);
            float dEmpty = Phase8ChunkD8Tests.DeltaE2000(mud, scheme.FoodPileEmpty);
            Assert.GreaterOrEqual(dEmpty, 12f, $"mud vs ColorScheme.FoodPileEmpty: dE00 {dEmpty:0.0}");
        }

        [Test]
        public void Mud_DarkWetBlueGrey()
        {
            var mud = Mean(KeptTexels(1));
            Assert.Greater(mud.b, mud.r, "blue-grey: B above R (the old brown had R 93 / B 47)");
            Assert.LessOrEqual(mud.grayscale, 0.35f, "dark (wet)");
            Color.RGBToHSV(mud, out _, out float s, out _);
            Assert.LessOrEqual(s, 0.35f, "a grey, not a saturated blue");
        }
    }
}

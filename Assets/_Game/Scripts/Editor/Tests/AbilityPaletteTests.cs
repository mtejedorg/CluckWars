using System;
using System.Linq;
using CluckWars.Abilities;
using CluckWars.Gameplay;
using CluckWars.UI;
using NUnit.Framework;
using UnityEngine;

namespace CluckWars.Tests
{
    /// <summary>
    /// Pins the menu's ability colour system (<see cref="AbilityPalette"/>, menu overhaul Phase 4,
    /// re-audit item 3): every shipped ability's hex colour is its category colour, the label on
    /// each category colour clears WCAG AA, and no category colour can be mistaken for a player.
    /// </summary>
    public sealed class AbilityPaletteTests
    {
        /// <summary>The player colours (the one palette, keyed by spawn corner).</summary>
        private static readonly Color[] Players = { PlayerPalette.P1, PlayerPalette.P2, PlayerPalette.P3, PlayerPalette.P4 };

        private static readonly AbilityCategory[] Categories =
            (AbilityCategory[])Enum.GetValues(typeof(AbilityCategory));

        [Test]
        public void EveryShippedAbility_HexColourIsItsCategoryFillColour()
        {
            var reg = TestAssets.Load<AbilityRegistrySO>(TestAssets.AbilityRegistryPath);
            var abilities = reg.All.Where(a => a != null).ToList();
            Assert.Greater(abilities.Count, 0, "registry is empty");
            foreach (var ab in abilities)
            {
                var hex = AbilityPalette.HexColor(ab);
                Assert.AreEqual(AbilityPalette.FillColor(ab.Category), hex, $"{ab.name} ({ab.Category})");
                foreach (var p in Players)
                    Assert.GreaterOrEqual(DeltaE2000(hex, p), AbilityPalette.MinPlayerDeltaE,
                        $"{ab.name}'s hex sits too close to player colour #{ColorUtility.ToHtmlStringRGB(p)}");
            }
        }

        [Test]
        public void CategoryColours_AreFarFromEveryPlayerColour()
        {
            foreach (var cat in Categories)
            foreach (var p in Players)
            {
                float de = DeltaE2000(AbilityPalette.CategoryColor(cat), p);
                Assert.GreaterOrEqual(de, AbilityPalette.MinPlayerDeltaE, $"{cat} vs player #{ColorUtility.ToHtmlStringRGB(p)}: dE00 {de:0.0}");
            }
        }

        [Test]
        public void CategoryColours_AreDistinctFromEachOther()
        {
            for (int i = 0; i < Categories.Length; i++)
            for (int j = i + 1; j < Categories.Length; j++)
            {
                float de = DeltaE2000(AbilityPalette.CategoryColor(Categories[i]), AbilityPalette.CategoryColor(Categories[j]));
                Assert.GreaterOrEqual(de, 25f, $"{Categories[i]} vs {Categories[j]}: dE00 {de:0.0}");
            }
        }

        [Test]
        public void CategoryLabels_ClearWcagAA()
        {
            foreach (var cat in Categories)
            {
                var bg = AbilityPalette.CategoryColor(cat);
                float ratio = AbilityPalette.ContrastRatio(AbilityPalette.InkOn(bg), bg);
                Assert.GreaterOrEqual(ratio, AbilityPalette.MinLabelContrast, $"{cat} label: {ratio:0.00}:1");
            }
        }

        [Test]
        public void Defense_TakesACreamLabel_WithMargin()
        {
            // Round-2 finding 9: Defense was a light moss with an ink label at 4.65:1; it is dark enough
            // now for the cream label every other category uses, with margin over AA.
            var bg = AbilityPalette.Defense;
            Assert.AreEqual(UiGfx.TextPrimary, AbilityPalette.InkOn(bg));
            Assert.GreaterOrEqual(AbilityPalette.ContrastRatio(UiGfx.TextPrimary, bg), 5f);
        }

        /// <summary>
        /// Round-3 finding 15: the Defense hex FILL is a mid moss, not the near-black text background. The fill
        /// keeps the same separations the text colour has: far from the other fills and from the players, a
        /// readable label, and colour-blind apart from the other categories.
        /// </summary>
        [Test]
        public void DefenseFill_IsMidMoss_DistinctFromTheOtherFills_AndTheDarkTextColourIsKept()
        {
            Assert.AreEqual(UiGfx.Hex32("30460c"), AbilityPalette.Defense, "text background stays the dark moss");
            Assert.AreNotEqual(AbilityPalette.Defense, AbilityPalette.DefenseFill);
            Assert.AreEqual(AbilityPalette.DefenseFill, AbilityPalette.FillColor(AbilityCategory.Defense));
            Assert.AreEqual(AbilityPalette.Defense, AbilityPalette.CategoryColor(AbilityCategory.Defense));
            foreach (var cat in new[] { AbilityCategory.Steal, AbilityCategory.Control, AbilityCategory.Utility })
                Assert.AreEqual(AbilityPalette.CategoryColor(cat), AbilityPalette.FillColor(cat), $"{cat} fill = its colour");

            Assert.Greater(ToLab(AbilityPalette.DefenseFill).x, 50f, "mid moss, not near-black (the dark one is L* 27)");
            Assert.GreaterOrEqual(AbilityPalette.ContrastRatio(AbilityPalette.InkOn(AbilityPalette.DefenseFill), AbilityPalette.DefenseFill),
                AbilityPalette.MinLabelContrast, "a label on the fill (icon monogram, slot badge) clears AA");
            foreach (var p in Players)
                Assert.GreaterOrEqual(DeltaE2000(AbilityPalette.DefenseFill, p), AbilityPalette.MinPlayerDeltaE);
            var fills = Categories.Select(AbilityPalette.FillColor).ToArray();
            for (int i = 0; i < fills.Length; i++)
            for (int j = i + 1; j < fills.Length; j++)
            {
                Assert.GreaterOrEqual(DeltaE2000(fills[i], fills[j]), 25f, $"{Categories[i]} vs {Categories[j]} fills");
                foreach (var m in new[] { Deuteranopia, Protanopia, Tritanopia })
                    Assert.GreaterOrEqual(DeltaE2000(Simulate(fills[i], m), Simulate(fills[j], m)), AbilityPalette.MinColourBlindDeltaE,
                        $"{Categories[i]} vs {Categories[j]} fills under CVD");
            }
        }

        /// <summary>
        /// Round-3 finding 11: P3 / P4 were 6.9 dE00 apart under deuteranopia and P2 / P4 9.0 under tritanopia.
        /// All six player pairs must keep <see cref="AbilityPalette.MinPlayerColourBlindDeltaE"/> under all three
        /// simulations (the same Machado matrices and CIEDE2000 the category test uses), and the set must be
        /// separated by lightness too: at least one clearly light and one clearly dark colour.
        /// </summary>
        [TestCase("deuteranopia")]
        [TestCase("protanopia")]
        [TestCase("tritanopia")]
        public void PlayerColours_StayApart_UnderColourBlindness(string kind)
        {
            var m = kind == "deuteranopia" ? Deuteranopia : kind == "protanopia" ? Protanopia : Tritanopia;
            for (int i = 0; i < Players.Length; i++)
            for (int j = i + 1; j < Players.Length; j++)
            {
                float de = DeltaE2000(Simulate(Players[i], m), Simulate(Players[j], m));
                Assert.GreaterOrEqual(de, AbilityPalette.MinPlayerColourBlindDeltaE,
                    $"P{i + 1} vs P{j + 1} under {kind}: dE00 {de:0.0}");
            }
        }

        [Test]
        public void PlayerColours_AreSeparatedByLightness_AndStayTheirHue()
        {
            var l = Players.Select(c => ToLab(c).x).ToArray();
            Assert.GreaterOrEqual(l.Max() - l.Min(), 20f, $"player lightness range L* {l.Min():0}..{l.Max():0}");
            Assert.GreaterOrEqual(l[0] - l[3], 20f, "P1 (light orange) clearly lighter than P4 (dark teal)");
            Color.RGBToHSV(Players[0], out float h1, out _, out _);
            Color.RGBToHSV(Players[1], out float h2, out _, out _);
            Color.RGBToHSV(Players[2], out float h3, out _, out _);
            Color.RGBToHSV(Players[3], out float h4, out _, out _);
            Assert.That(h1 * 360f, Is.InRange(20f, 45f), "P1 orange");
            Assert.That(h2 * 360f, Is.InRange(195f, 215f), "P2 blue");
            Assert.That(h3 * 360f, Is.InRange(330f, 345f), "P3 pink");
            Assert.That(h4 * 360f, Is.InRange(155f, 175f), "P4 teal");
            foreach (var p in Players)   // the pedestal tag / podium labels take whichever of cream / ink reads better
                Assert.GreaterOrEqual(AbilityPalette.ContrastRatio(AbilityPalette.InkOn(p), p), 3f, $"label on #{ColorUtility.ToHtmlStringRGB(p)}");
        }

        [Test]
        public void ColourBlindSimulation_Tritanopia_KeepsGreys()
        {
            var g = Simulate(new Color(0.5f, 0.5f, 0.5f), Tritanopia);
            Assert.AreEqual(0.5f, g.r, 0.01f); Assert.AreEqual(0.5f, g.g, 0.01f); Assert.AreEqual(0.5f, g.b, 0.01f);
        }

        /// <summary>
        /// Round-2 finding 9: Steal and Defense were 4.7 dE00 apart for a deuteranope. After a full-severity
        /// deuteranopia and protanopia simulation (Machado, Oliveira &amp; Fernandes 2009, applied in linear
        /// RGB), every pair of category colours must keep <see cref="AbilityPalette.MinColourBlindDeltaE"/>.
        /// </summary>
        [TestCase("deuteranopia")]
        [TestCase("protanopia")]
        public void CategoryColours_StayApart_UnderColourBlindness(string kind)
        {
            var m = kind == "deuteranopia" ? Deuteranopia : Protanopia;
            for (int i = 0; i < Categories.Length; i++)
            for (int j = i + 1; j < Categories.Length; j++)
            {
                var a = Simulate(AbilityPalette.CategoryColor(Categories[i]), m);
                var b = Simulate(AbilityPalette.CategoryColor(Categories[j]), m);
                float de = DeltaE2000(a, b);
                Assert.GreaterOrEqual(de, AbilityPalette.MinColourBlindDeltaE,
                    $"{Categories[i]} vs {Categories[j]} under {kind}: dE00 {de:0.0}");
            }
        }

        [Test]
        public void ColourBlindSimulation_KeepsGreysAndReproducesTheOldSteal_DefenseCollision()
        {
            // Machado matrices map achromatic colours to themselves (rows sum to ~1).
            var grey = new Color(0.5f, 0.5f, 0.5f);
            var g = Simulate(grey, Deuteranopia);
            Assert.AreEqual(0.5f, g.r, 0.01f); Assert.AreEqual(0.5f, g.g, 0.01f); Assert.AreEqual(0.5f, g.b, 0.01f);
            // And they reproduce the audit's finding on the old Defense moss.
            float old = DeltaE2000(Simulate(AbilityPalette.Steal, Deuteranopia), Simulate(UiGfx.Hex32("5f8a2c"), Deuteranopia));
            Assert.Less(old, AbilityPalette.MinColourBlindDeltaE, $"old Steal / Defense under deuteranopia: {old:0.0}");
        }

        // Machado, Oliveira & Fernandes (2009), severity 1.0, linear RGB.
        private static readonly double[,] Deuteranopia =
        {
            { 0.367322, 0.860646, -0.227968 },
            { 0.280085, 0.672501, 0.047413 },
            { -0.011820, 0.042940, 0.968881 },
        };
        private static readonly double[,] Protanopia =
        {
            { 0.152286, 1.052583, -0.204868 },
            { 0.114503, 0.786281, 0.099216 },
            { -0.003882, -0.048116, 1.051998 },
        };

        private static readonly double[,] Tritanopia =
        {
            { 1.255528, -0.076749, -0.178779 },
            { -0.078411, 0.930809, 0.147602 },
            { 0.004733, 0.691367, 0.303900 },
        };

        private static Color Simulate(Color c, double[,] m)
        {
            static double Lin(double v) => v <= 0.04045 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4);
            static float Gam(double v)
            {
                v = Math.Max(0, Math.Min(1, v));
                return (float)(v <= 0.0031308 ? 12.92 * v : 1.055 * Math.Pow(v, 1 / 2.4) - 0.055);
            }
            double r = Lin(c.r), g = Lin(c.g), b = Lin(c.b);
            return new Color(
                Gam(m[0, 0] * r + m[0, 1] * g + m[0, 2] * b),
                Gam(m[1, 0] * r + m[1, 1] * g + m[1, 2] * b),
                Gam(m[2, 0] * r + m[2, 1] * g + m[2, 2] * b));
        }

        [Test]
        public void DeltaE2000_MatchesThePublishedReferencePair()
        {
            // Sharma, Wu & Dalal (2005), pair 1: Lab(50, 2.6772, -79.7751) vs Lab(50, 0, -82.7485) = 2.0425.
            Assert.AreEqual(2.0425, DeltaE2000Lab(50, 2.6772, -79.7751, 50, 0, -82.7485), 1e-3);
            Assert.AreEqual(0.0, DeltaE2000(UiGfx.Hex32("7a3f8f"), UiGfx.Hex32("7a3f8f")), 1e-6);
        }

        // ---- CIEDE2000 (sRGB D65 -> CIELAB -> dE00) ------------------------------------------------

        private static float DeltaE2000(Color a, Color b)
        {
            var la = ToLab(a); var lb = ToLab(b);
            return (float)DeltaE2000Lab(la.x, la.y, la.z, lb.x, lb.y, lb.z);
        }

        private static Vector3 ToLab(Color c)
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

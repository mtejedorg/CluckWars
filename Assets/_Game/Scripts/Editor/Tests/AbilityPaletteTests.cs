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
        /// <summary>Player colours (Okabe-Ito, design token CW_PLAYERS_V3): orange, blue, pink, teal.</summary>
        private static readonly string[] PlayerHex = { "e8751a", "1a7fc4", "c4286f", "0d9e7a" };

        private static readonly AbilityCategory[] Categories =
            (AbilityCategory[])Enum.GetValues(typeof(AbilityCategory));

        [Test]
        public void EveryShippedAbility_HexColourIsItsCategoryColour()
        {
            var reg = TestAssets.Load<AbilityRegistrySO>(TestAssets.AbilityRegistryPath);
            var abilities = reg.All.Where(a => a != null).ToList();
            Assert.Greater(abilities.Count, 0, "registry is empty");
            foreach (var ab in abilities)
            {
                var hex = AbilityPalette.HexColor(ab);
                Assert.AreEqual(AbilityPalette.CategoryColor(ab.Category), hex, $"{ab.name} ({ab.Category})");
                foreach (var p in PlayerHex)
                    Assert.GreaterOrEqual(DeltaE2000(hex, UiGfx.Hex32(p)), AbilityPalette.MinPlayerDeltaE,
                        $"{ab.name}'s hex sits too close to player colour #{p}");
            }
        }

        [Test]
        public void CategoryColours_AreFarFromEveryPlayerColour()
        {
            foreach (var cat in Categories)
            foreach (var p in PlayerHex)
            {
                float de = DeltaE2000(AbilityPalette.CategoryColor(cat), UiGfx.Hex32(p));
                Assert.GreaterOrEqual(de, AbilityPalette.MinPlayerDeltaE, $"{cat} vs player #{p}: dE00 {de:0.0}");
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

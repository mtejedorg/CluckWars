using System.IO;
using System.Text.RegularExpressions;
using CluckWars.Gameplay;
using CluckWars.UI;
using NUnit.Framework;
using UnityEngine;

namespace CluckWars.Tests
{
    /// <summary>
    /// Round-2 finding 6 / 17: the podium geometry (winner 1.25x, crown on the head, short podiums
    /// centred, board sized to its rows) and the two-tone post-match feathers.
    /// </summary>
    public sealed class PodiumLayoutTests
    {
        private static string Read(string rel) =>
            File.ReadAllText(Path.Combine(Path.GetDirectoryName(Application.dataPath), rel));

        // ---- Scales ---------------------------------------------------------------------------------

        [Test]
        public void Winner_IsDrawnExactly125x_TheOthers_WhateverSizeEachStepGaveIt()
        {
            // The steps have different heights, so the drawn squares differ before scaling.
            float[] sides = { 260f, 230f, 300f };
            var k = PodiumLayout.ChickenScales(sides, winnerSlot: 1);
            float second = sides[0] * k[0], winner = sides[1] * k[1], third = sides[2] * k[2];
            Assert.AreEqual(second, third, 0.01f, "the non-winners are drawn at one common size");
            Assert.AreEqual(PodiumLayout.WinnerScale * second, winner, 0.01f);
            Assert.AreEqual(1.25f, PodiumLayout.WinnerScale);
        }

        [Test]
        public void NoWinner_EveryoneAtTheCommonSize_AndHiddenStepsAreLeftAlone()
        {
            var k = PodiumLayout.ChickenScales(new[] { 240f, 280f, float.NaN }, winnerSlot: -1);
            Assert.AreEqual(240f * k[0], 280f * k[1], 0.01f);
            Assert.AreEqual(1f, k[2], "a hidden / not-laid-out step keeps scale 1");
            CollectionAssert.AreEqual(new[] { 1f, 1f, 1f }, PodiumLayout.ChickenScales(new[] { float.NaN, 0f, float.NaN }, 1));
        }

        // ---- Crown ----------------------------------------------------------------------------------

        [Test]
        public void Crown_IsCentredOverTheHead_AndSitsOnTheComb_InATallOrAWideElement()
        {
            var head = new Vector2(0.5f, 0.04f);
            // Tall element: the square is the full width, standing on the bottom edge.
            var tall = PodiumLayout.CrownTopLeft(300f, 400f, head, 100f, 100f);
            Assert.AreEqual(150f, tall.x + 50f, 0.01f, "crown centre over the head");
            float headY = 100f + 0.04f * 300f;
            Assert.Less(tall.y, headY, "the crown starts above the head top");
            Assert.Greater(tall.y + 100f, headY, "and its lower part overlaps the comb (not floating)");
            Assert.AreEqual(headY, tall.y + 100f * (1f - PodiumLayout.CrownSink), 0.01f);
            // Wide element: the square is centred horizontally.
            var wide = PodiumLayout.CrownTopLeft(500f, 300f, new Vector2(0.6f, 0.04f), 100f, 100f);
            Assert.AreEqual(100f + 0.6f * 300f, wide.x + 50f, 0.01f);
        }

        [TestCase(ChickenClass.Warrior)]
        [TestCase(ChickenClass.Speedy)]
        [TestCase(ChickenClass.Fatty)]
        [TestCase(ChickenClass.Assassin)]
        public void StaticHeadAnchor_IsTheTopOfTheBirdInItsCheerRender(ChickenClass cls)
        {
            // Re-measures the PNG the winner's step shows with Performance Mode ON: a re-render that
            // moves the head fails here instead of floating the crown.
            string path = $"Assets/_Game/Art/UI/Chickens/Hero_{cls.ToString().ToLowerInvariant()}_cheer.png";
            var tex = new Texture2D(2, 2);
            try
            {
                Assert.IsTrue(tex.LoadImage(File.ReadAllBytes(Path.Combine(Path.GetDirectoryName(Application.dataPath), path))), path);
                int w = tex.width, h = tex.height;
                var px = tex.GetPixels32();   // row 0 = bottom
                int topRow = -1;
                for (int y = h - 1; y >= 0 && topRow < 0; y--)
                    for (int x = 0; x < w; x++) if (px[y * w + x].a > 128) { topRow = y; break; }
                Assert.GreaterOrEqual(topRow, 0, "render is empty");
                int band = Mathf.RoundToInt(h * 0.06f), minX = w, maxX = -1;
                for (int y = topRow; y > topRow - band && y >= 0; y--)
                    for (int x = 0; x < w; x++)
                        if (px[y * w + x].a > 128) { minX = Mathf.Min(minX, x); maxX = Mathf.Max(maxX, x); }

                var anchor = PodiumLayout.StaticHeadAnchor(cls);
                Assert.AreEqual((h - 1 - topRow) / (float)h, anchor.y, 0.015f, "anchor y = the top of the head");
                Assert.AreEqual((minX + maxX) * 0.5f / w, anchor.x, 0.02f, "anchor x = the middle of the head's top");
            }
            finally
            {
                Object.DestroyImmediate(tex);
            }
        }

        // ---- Layout guards (USS / UXML) -----------------------------------------------------------

        [Test]
        public void ShortPodium_DropsEmptySteps_SoTwoBirdsAreCentred()
        {
            string css = Read("Assets/UI/Styles/MatchOverlays.uss");
            StringAssert.Contains(".cw-me-pod--hidden { display: none; }", css,
                "visibility:hidden kept the empty 3rd step's width and pushed a 2-bird podium off-centre");
            StringAssert.Contains("justify-content: center", Regex.Match(css, @"\.cw-me-podium \{[^}]*\}").Value);
        }

        [Test]
        public void StandingsBoard_IsSizedToItsRows_NotStretchedToTheScreen()
        {
            string rule = Regex.Match(Read("Assets/UI/Styles/MatchOverlays.uss"), @"\.cw-me-right \{[^}]*\}").Value;
            StringAssert.Contains("align-self: center;", rule);
        }

        [Test]
        public void Podium_ChickensScaleFromTheirFeet_AndTheCrownIsPlacedInCode()
        {
            string css = Read("Assets/UI/Styles/MatchOverlays.uss");
            StringAssert.Contains("transform-origin: 50% 100%;", Regex.Match(css, @"\.cw-me-pod__chicken \{[^}]*\}").Value);
            string ctrl = Read("Assets/_Game/Scripts/UI/MatchOverlaysController.cs");
            StringAssert.Contains("PodiumLayout.CrownTopLeft(", ctrl);
            StringAssert.Contains("TryGetHeadAnchor(", ctrl);
            StringAssert.Contains("PodiumLayout.ChickenScales(", ctrl);
        }

        // ---- Feathers (finding 17) ------------------------------------------------------------------

        [Test]
        public void Feathers_AreCreamWithAWarmRim()
        {
            Color.RGBToHSV(MatchCelebration.FeatherRim, out float h, out float s, out float v);
            Assert.That(h * 360f, Is.InRange(20f, 45f), "rim hue: warm amber / gold-orange");
            Assert.Greater(s, 0.5f, "the rim is clearly warmer than the body");
            Color.RGBToHSV(MatchCelebration.FeatherCream, out _, out float cs, out float cv);
            Assert.Less(cs, 0.2f, "the body stays cream");
            Assert.Greater(cv, v, "the body is lighter than its rim");

            var m = Regex.Match(Read("Assets/UI/Styles/MatchOverlays.uss"), @"\.cw-fx-feather__body \{[^}]*scale: ([0-9.]+)");
            Assert.IsTrue(m.Success, ".cw-fx-feather__body must shrink the cream copy so the rim shows");
            Assert.That(float.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture), Is.InRange(0.8f, 0.92f),
                "subtle: a thin warm edge, not a second feather");
        }
    }
}

using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using CluckWars.Abilities;
using CluckWars.Localization;
using CluckWars.UI;
using CluckWars.Visuals;
using NUnit.Framework;
using UnityEditor;

namespace CluckWars.Tests
{
    /// <summary>
    /// K3, the hunter marker at 1.5x: the boxes in <c>MarkHud.uss</c> and the px constants in <see cref="FeedbackTuning"/> are
    /// scaled together, so these assert how they relate (ring inside the box, disc inside the ring, pointer inside the placement box,
    /// badges inside the box), not what they are. And the metres chip: one decimal under 10 m, red exactly beyond the shipped
    /// kill range, and the marker and the Hunt hex read the same function.
    /// </summary>
    public class MarkMarkerGeometryTests
    {
        private const string UssPath = "Assets/UI/Styles/MarkHud.uss";

        private static string Rule(string cls)
        {
            string uss = File.ReadAllText(UssPath);
            var m = Regex.Match(uss, @"\." + Regex.Escape(cls) + @"(?![\w-])[^{}]*\{([^}]*)\}");
            Assert.IsTrue(m.Success, $".{cls} has no rule in MarkHud.uss");
            return m.Groups[1].Value;
        }

        private static float Px(string cls, string property)
        {
            var m = Regex.Match(Rule(cls), @"(?<![\w-])" + Regex.Escape(property) + @"\s*:\s*(-?[0-9.]+)px");
            Assert.IsTrue(m.Success, $".{cls} has no {property} in px");
            return float.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
        }

        private static MarkKillAbilitySO Asset()
        {
            var so = AssetDatabase.LoadAssetAtPath<MarkKillAbilitySO>("Assets/_Game/Data/Abilities/MarkKill.asset");
            Assert.IsNotNull(so, "MarkKill.asset missing");
            return so;
        }

        [Test]
        public void Tuning_RingAndPointerStayInsideThePlacementBox()
        {
            Assert.AreEqual(FeedbackTuning.MarkMarkerHalfPx * 2f, FeedbackTuning.MarkMarkerBoxWidthPx, 1e-4f);
            Assert.LessOrEqual(FeedbackTuning.MarkRingRadiusPx + FeedbackTuning.MarkRingArcWidthPx * 0.5f, FeedbackTuning.MarkMarkerHalfPx,
                "the ring's outer edge is inside the box half-width");
            Assert.LessOrEqual(FeedbackTuning.MarkRingRadiusPx + FeedbackTuning.MarkPointerTipBeyondRingPx,
                FeedbackTuning.MarkMarkerBoxHeightPx - FeedbackTuning.MarkMarkerHalfPx, "the pointer tip is inside the box height below the centre");
            Assert.Less(FeedbackTuning.MarkRingArcWidthPx, FeedbackTuning.MarkRingTrackWidthPx, "the colour arc sits inside the ink track");
            Assert.Less(FeedbackTuning.MarkRingTrackWidthPx * 0.5f, FeedbackTuning.MarkRingRadiusPx);
            Assert.LessOrEqual(FeedbackTuning.MarkPointerBasePx * 0.5f, FeedbackTuning.MarkRingRadiusPx, "the pointer base is no wider than the ring");
            Assert.Greater(FeedbackTuning.MarkMarkerEdgeClearancePx, FeedbackTuning.MarkMarkerHalfPx, "the edge pin keeps the whole box in the clear viewport");
        }

        [Test]
        public void Uss_DiscRingAndBadgesRelateToTheBox()
        {
            float box = Px("cw-mark-marker", "width");
            Assert.AreEqual(box, Px("cw-mark-marker", "height"));
            Assert.AreEqual(box, Px("cw-mark-ring", "width"), "ring and pointer paint over the whole box");
            Assert.AreEqual(box, Px("cw-mark-pointer", "width"));
            Assert.AreEqual(box, FeedbackTuning.MarkMarkerBoxWidthPx, 1e-4f, "the placement box is the drawn box (K3b: the old 84 vs 96 gap is gone)");

            float disc = Px("cw-mark-disc", "width");
            Assert.AreEqual((box - disc) * 0.5f, Px("cw-mark-disc", "left"), 1e-4f, "disc centred");
            Assert.LessOrEqual(disc * 0.5f, FeedbackTuning.MarkRingRadiusPx, "the disc fits inside the ring's centre line");
            Assert.Greater(disc * 0.5f, FeedbackTuning.MarkRingRadiusPx - FeedbackTuning.MarkRingTrackWidthPx, "and covers the ring's inside");
            Assert.AreEqual(disc, Px("cw-mark-arm", "width"), "the count fills the disc");
            Assert.AreEqual(Px("cw-mark-disc", "left"), Px("cw-mark-arm", "left"));

            float badge = Px("cw-mark-lock", "width");
            Assert.LessOrEqual(Px("cw-mark-lock", "left") + badge, box, "the lock badge is inside the box");
            Assert.AreEqual(box, Px("cw-mark-lock", "left") + Px("cw-mark-paw", "left") + badge, 1e-4f, "the paw badge mirrors the lock badge");
            Assert.AreEqual(box, Px("cw-mark-lock", "top") + Px("cw-mark-paw", "top") + badge, 1e-4f);
            Assert.LessOrEqual(Px("cw-mark-skull", "width"), badge);
            Assert.LessOrEqual(Px("cw-mark-lock-glyph", "width"), badge);
            Assert.LessOrEqual(Px("cw-mark-star-glyph", "width"), badge);
            Assert.LessOrEqual(Px("cw-mark-shell-glyph", "width"), badge);
        }

        [Test]
        public void Chip_StaysAtLeastTheHudFloorAtTheAppearPop()
        {
            const float hudFloorPx = 28f;
            Assert.GreaterOrEqual(Px("cw-mark-distance", "font-size") * FeedbackTuning.MarkMarkerPopFrom, hudFloorPx);
            float box = Px("cw-mark-marker", "width");
            Assert.AreEqual(box * 0.5f, Px("cw-mark-distance", "left"), 1e-4f, "chip centred on the box (translate -50%)");
            StringAssert.Contains("translate: -50%", Rule("cw-mark-distance"));
        }

        [Test]
        public void ChipText_OneDecimalUnderTen()
        {
            Assert.AreEqual("6.2", HuntRules.MetresText(6.2f));
            Assert.AreEqual("6.3", HuntRules.MetresText(6.21f), "rounded up: a hair beyond the range never reads as the range");
            Assert.AreEqual("0.0", HuntRules.MetresText(0f));
            Assert.AreEqual("0.0", HuntRules.MetresText(-3f));
            Assert.AreEqual("9.9", HuntRules.MetresText(9.9f));
            Assert.AreEqual("10", HuntRules.MetresText(9.95f), "the decimal ends at 10");
            Assert.AreEqual("10", HuntRules.MetresText(10f));
            Assert.AreEqual("24", HuntRules.MetresText(23.6f));
            Assert.AreEqual("6.2 m", HuntRules.ChipText(HuntRules.MetresKey(6.2f)), "the shared mark.distance string");
            Assert.AreEqual(UiText.Format(UiKeys.MarkDistance, ("m", "24")), HuntRules.ChipText(HuntRules.MetresKey(24f)));
            Assert.AreEqual(HuntRules.MetresKey(6.2f), HuntRules.MetresKey(6.15f), "one key per displayed tenth");
        }

        [Test]
        public void ChipRed_IffBeyondShippedKillRange()
        {
            float range = Asset().KillRange;
            Assert.IsFalse(HuntRules.ChipRed(range, range), "at the range the pounce is still legal");
            Assert.IsFalse(HuntRules.ChipRed(0f, range));
            Assert.IsTrue(HuntRules.ChipRed(range + 0.01f, range));
            for (float d = 0f; d < 30f; d += 0.05f)
            {
                Assert.AreEqual(d > range, HuntRules.ChipRed(d, range), $"d={d}");
                // The words never contradict the colour: a red chip never reads at or below the range.
                if (HuntRules.ChipRed(d, range) && d < HuntRules.ChipDecimalBelowMetres - 0.1f)
                    Assert.Greater(float.Parse(HuntRules.MetresText(d), CultureInfo.InvariantCulture), range - 1e-3f, $"red at {d} reads {HuntRules.MetresText(d)}");
            }
        }

        [Test]
        public void MarkerAndHexChip_ShareOneTextFunction()
        {
            string marker = File.ReadAllText("Assets/_Game/Scripts/UI/MarkHudController.cs");
            string hex = File.ReadAllText("Assets/_Game/Scripts/Input/TouchControlsController.Hud.cs");
            foreach (var src in new[] { marker, hex })
            {
                StringAssert.Contains("HuntRules.MetresKey(", src);
                StringAssert.Contains("HuntRules.ChipText(", src);
                StringAssert.DoesNotContain("UiKeys.MarkDistance", src, "the chip number has one home, HuntRules");
            }
            // Same distance, same text: both sides take MarkOutputs.MarkerDistance.
            StringAssert.Contains("_huntOut.MarkerDistance", hex);
            StringAssert.Contains("o.MarkerDistance", marker);
        }

        [Test]
        public void Badge_WallAndEggShowWhileArming_StarOnlyWhenLive_OneAtATime()
        {
            // Arming: the blocker is None, the mode already says wall / egg.
            Assert.AreEqual(MarkerBadge.Lock, HuntRules.BadgeFor(MarkMarkerMode.Walled, HuntBlocker.None));
            Assert.AreEqual(MarkerBadge.Shell, HuntRules.BadgeFor(MarkMarkerMode.Shelled, HuntBlocker.None));
            Assert.AreEqual(MarkerBadge.None, HuntRules.BadgeFor(MarkMarkerMode.Hunt, HuntBlocker.None), "no star while arming");
            // Live.
            Assert.AreEqual(MarkerBadge.Lock, HuntRules.BadgeFor(MarkMarkerMode.Walled, HuntBlocker.Walled));
            Assert.AreEqual(MarkerBadge.Shell, HuntRules.BadgeFor(MarkMarkerMode.Shelled, HuntBlocker.Shelled));
            Assert.AreEqual(MarkerBadge.Star, HuntRules.BadgeFor(MarkMarkerMode.Hunt, HuntBlocker.NotStunned));
            Assert.AreEqual(MarkerBadge.None, HuntRules.BadgeFor(MarkMarkerMode.Hunt, HuntBlocker.TooFar), "too far is the red chip, no badge");
            Assert.AreEqual(MarkerBadge.None, HuntRules.BadgeFor(MarkMarkerMode.Kill, HuntBlocker.None));
            // Priority egg > wall > star, over every combination.
            foreach (MarkMarkerMode mode in System.Enum.GetValues(typeof(MarkMarkerMode)))
                foreach (HuntBlocker b in System.Enum.GetValues(typeof(HuntBlocker)))
                {
                    var got = HuntRules.BadgeFor(mode, b);
                    if (mode == MarkMarkerMode.Shelled || b == HuntBlocker.Shelled) Assert.AreEqual(MarkerBadge.Shell, got, $"{mode}/{b}");
                    else if (mode == MarkMarkerMode.Walled || b == HuntBlocker.Walled) Assert.AreEqual(MarkerBadge.Lock, got, $"{mode}/{b}");
                    else if (b == HuntBlocker.NotStunned) Assert.AreEqual(MarkerBadge.Star, got, $"{mode}/{b}");
                    else Assert.AreEqual(MarkerBadge.None, got, $"{mode}/{b}");
                }
        }

        [Test]
        public void Chip_HasAnInkPillBehindTheText()
        {
            string rule = Rule("cw-mark-distance");
            var m = Regex.Match(rule, @"background-color:\s*rgba\(\s*(\d+),\s*(\d+),\s*(\d+),\s*([0-9.]+)\s*\)");
            Assert.IsTrue(m.Success, "the chip has an rgba background");
            Assert.GreaterOrEqual(float.Parse(m.Groups[4].Value, CultureInfo.InvariantCulture), 0.8f, "ink at high alpha");
            Assert.Less(int.Parse(m.Groups[1].Value), 60, "dark");
            Assert.GreaterOrEqual(Px("cw-mark-distance", "border-top-left-radius"), Px("cw-mark-distance", "height") * 0.5f, "a pill");
            Assert.GreaterOrEqual(Px("cw-mark-distance", "font-size"), 34f, "the text keeps its size");
        }

        /// <summary>Everything drawn by a marker pinned at an edge (the 144 px box, the pointer's reach, the chip on its side) is inside the panel.</summary>
        [TestCase(2133f, 960f)]
        [TestCase(1920f, 1080f)]
        [TestCase(960f, 2133f)]
        [TestCase(1080f, 1920f)]
        public void PinnedAtEveryEdge_BoxPointerAndChipStayOnThePanel(float w, float h)
        {
            var panel = new UnityEngine.Vector2(w, h);
            float box = Px("cw-mark-marker", "width"), half = box * 0.5f;
            float chipHalfW = 70f;   // "999 m" at 34 px + padding
            float chipBelowTop = Px("cw-mark-distance", "top") - half, chipBelowH = Px("cw-mark-distance", "height");
            float chipAboveTop = Rule("cw-mark-distance--above").Contains("top: -48px") ? -48f - half : float.NaN;
            Assert.IsFalse(float.IsNaN(chipAboveTop), "the --above chip rule is where the test expects it");
            float pointerReach = FeedbackTuning.MarkRingRadiusPx + FeedbackTuning.MarkPointerTipBeyondRingPx;

            // The raw edge pins: centre on each edge (and in each corner) as the edge projection can hand it over, then clamped.
            foreach (var raw in new[] {
                new UnityEngine.Vector2(w * 0.5f, 0f), new UnityEngine.Vector2(w * 0.5f, h), new UnityEngine.Vector2(0f, h * 0.5f), new UnityEngine.Vector2(w, h * 0.5f),
                new UnityEngine.Vector2(0f, 0f), new UnityEngine.Vector2(w, h), new UnityEngine.Vector2(0f, h), new UnityEngine.Vector2(w, 0f) })
            {
                var at = MarkerPlacement.ClampPin(raw, panel);
                var bounds = new UnityEngine.Rect(0f, 0f, w, h);
                Assert.IsTrue(Contains(bounds, at.x - half, at.y - half, at.x + half, at.y + half), $"box at {raw}");
                Assert.IsTrue(Contains(bounds, at.x - pointerReach, at.y - pointerReach, at.x + pointerReach, at.y + pointerReach), $"pointer at {raw}");
                foreach (var bearing in new[] { UnityEngine.Vector2.up, UnityEngine.Vector2.down, UnityEngine.Vector2.left, UnityEngine.Vector2.right })
                {
                    bool above = MarkerPlacement.DistanceGoesAbove(at.y, h, bearing);
                    float top = at.y + (above ? chipAboveTop : chipBelowTop);
                    Assert.IsTrue(Contains(bounds, at.x - chipHalfW, top, at.x + chipHalfW, top + chipBelowH), $"chip (above={above}) at {raw}, bearing {bearing}");
                }
            }
            Assert.AreEqual(chipBelowTop + chipBelowH, MarkerPlacement.DistanceBelowExtentPx, 1e-4f, "the placement's below-extent constant matches the USS chip");
        }

        private static bool Contains(UnityEngine.Rect r, float x0, float y0, float x1, float y1) =>
            x0 >= r.xMin - 1e-3f && x1 <= r.xMax + 1e-3f && y0 >= r.yMin - 1e-3f && y1 <= r.yMax + 1e-3f;
    }
}

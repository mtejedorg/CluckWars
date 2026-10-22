using CluckWars.Abilities;
using CluckWars.Visuals;
using NUnit.Framework;
using UnityEngine;

namespace CluckWars.Tests
{
    /// <summary>
    /// Pins <see cref="ScreenEdgeProjection.ClampToEdge"/> — the pure half of the rival
    /// indicator's placement maths (playtest note 2 of 5, 2026-08-14).
    /// </summary>
    /// <remarks>
    /// The distinction these exist to protect is <b>on-screen judged against the FULL unit
    /// rect, placement clamped to the INSET rect</b>. Conflating the two is the natural
    /// mistake, and it is invisible in code review: it would make a rival's ground ring wink
    /// out and hand over to an edge chevron while the rival was still plainly in frame.
    /// Since WP7 the full rect is the CLEAR viewport (the frame minus the HUD info zones) when
    /// info zones are given; the tests at the bottom pin that form and the 50 % nameplate rule.
    /// </remarks>
    public class ScreenEdgeProjectionTests
    {
        private const float Margin = 0.045f;

        [Test]
        public void PointWellInsideTheFrame_IsOnScreen()
        {
            bool off = ScreenEdgeProjection.ClampToEdge(
                new Vector2(0.5f, 0.5f), false, Margin, out _, out _);

            Assert.That(off, Is.False);
        }

        [Test]
        public void PointInsideTheFrameButWithinTheMargin_IsStillOnScreen()
        {
            // 0.97 is inside the unit rect but outside the inset rect. It must NOT be
            // reported off-screen: the rival is visibly there, so it keeps its ground ring.
            bool off = ScreenEdgeProjection.ClampToEdge(
                new Vector2(0.97f, 0.5f), false, Margin, out _, out _);

            Assert.That(off, Is.False,
                "A rival inside the frame but near its edge is still on screen — the margin " +
                "governs where a chevron is DRAWN, not who counts as off screen.");
        }

        [Test]
        public void PointOutsideTheFrame_IsOffScreen_AndClampsIntoTheInsetRect()
        {
            bool off = ScreenEdgeProjection.ClampToEdge(
                new Vector2(1.4f, 0.5f), false, Margin, out Vector2 clamped, out _);

            Assert.That(off, Is.True);
            Assert.That(clamped.x, Is.EqualTo(1f - Margin).Within(1e-5f));
            Assert.That(clamped.y, Is.EqualTo(0.5f).Within(1e-5f));
        }

        [Test]
        public void BearingPointsFromTheClampedPointTowardTheRival()
        {
            ScreenEdgeProjection.ClampToEdge(
                new Vector2(1.6f, 1.6f), false, Margin, out Vector2 clamped, out Vector2 bearing);

            Vector2 expected = (new Vector2(1.6f, 1.6f) - clamped).normalized;

            Assert.That(bearing.x, Is.EqualTo(expected.x).Within(1e-4f));
            Assert.That(bearing.y, Is.EqualTo(expected.y).Within(1e-4f));
            Assert.That(bearing.magnitude, Is.EqualTo(1f).Within(1e-4f),
                "A caller may normalise this again; it must never be degenerate.");
        }

        [Test]
        public void ForceOffScreen_ReportsOffScreen_EvenForAPointDeadCentre()
        {
            // The behind-the-camera case: it projects to a perfectly ordinary viewport
            // coordinate while being somewhere the player cannot see.
            bool off = ScreenEdgeProjection.ClampToEdge(
                new Vector2(0.5f, 0.5f), true, Margin, out _, out Vector2 bearing);

            Assert.That(off, Is.True);
            Assert.That(bearing.magnitude, Is.EqualTo(1f).Within(1e-4f),
                "Centre-on-centre gives a zero first difference; the fallback must still " +
                "produce a unit bearing rather than a zero vector.");
        }

        [Test]
        public void AbsurdMargin_DegeneratesToTheCentre_RatherThanInvertingTheRange()
        {
            ScreenEdgeProjection.ClampToEdge(
                new Vector2(2f, 2f), false, 10f, out Vector2 clamped, out _);

            Assert.That(clamped.x, Is.EqualTo(0.5f).Within(0.02f));
            Assert.That(clamped.y, Is.EqualTo(0.5f).Within(0.02f));
        }

        [Test]
        public void EveryCorner_ClampsToItsOwnCorner()
        {
            var cases = new[]
            {
                (new Vector2(-1f, -1f), new Vector2(Margin, Margin)),
                (new Vector2( 2f, -1f), new Vector2(1f - Margin, Margin)),
                (new Vector2(-1f,  2f), new Vector2(Margin, 1f - Margin)),
                (new Vector2( 2f,  2f), new Vector2(1f - Margin, 1f - Margin)),
            };

            foreach (var (input, expected) in cases)
            {
                bool off = ScreenEdgeProjection.ClampToEdge(input, false, Margin,
                    out Vector2 clamped, out _);

                Assert.That(off, Is.True, $"{input} is outside the frame.");
                Assert.That(clamped.x, Is.EqualTo(expected.x).Within(1e-5f), $"x for {input}");
                Assert.That(clamped.y, Is.EqualTo(expected.y).Within(1e-5f), $"y for {input}");
            }
        }

        [Test]
        public void SafeArea_InsetsTheTopEdgeFurtherThanTheOthers()
        {
            // Measured 2026-08-15: with a uniform margin every off-screen chevron clamped to
            // y = 0.955, which is where the scoreboard and match timer live, so the feature
            // was hidden in exactly the case it exists for. The top edge must stay inset
            // further than the other three.
            var area = FeedbackTuning.RivalChevronSafeArea;

            Assert.That(area.yMax, Is.LessThan(1f - FeedbackTuning.RivalChevronViewportMargin),
                "The top edge must clear the HUD strip, not just the frame.");
            Assert.That(area.yMin, Is.EqualTo(FeedbackTuning.RivalChevronViewportMargin).Within(1e-5f),
                "The bottom edge has no HUD to clear and must keep the plain margin.");
            Assert.That(area.xMin, Is.EqualTo(FeedbackTuning.RivalChevronViewportMargin).Within(1e-5f));
            Assert.That(area.xMax, Is.EqualTo(1f - FeedbackTuning.RivalChevronViewportMargin).Within(1e-5f));
            Assert.That(area.yMin, Is.LessThan(area.yMax), "The band must not be inverted.");
        }

        [Test]
        public void OffScreenAbove_ClampsBelowTheHudStrip()
        {
            var area = FeedbackTuning.RivalChevronSafeArea;

            bool off = ScreenEdgeProjection.ClampToEdge(
                new Vector2(0.5f, 2f), false, area, out Vector2 clamped, out _);

            Assert.That(off, Is.True);
            Assert.That(clamped.y, Is.EqualTo(area.yMax).Within(1e-5f));
            Assert.That(clamped.y, Is.LessThan(0.9f),
                "A rival straight above must not be pinned under the scoreboard.");
        }

        [Test]
        public void InvertedSafeArea_CollapsesToCentre_RatherThanReturningGarbage()
        {
            bool off = ScreenEdgeProjection.ClampToEdge(
                new Vector2(2f, 2f), false, Rect.MinMaxRect(0.8f, 0.8f, 0.2f, 0.2f),
                out Vector2 clamped, out Vector2 bearing);

            Assert.That(off, Is.True);
            Assert.That(float.IsNaN(clamped.x), Is.False);
            Assert.That(float.IsNaN(clamped.y), Is.False);
            Assert.That(bearing.magnitude, Is.EqualTo(1f).Within(1e-4f));
        }

        [Test]
        public void RivalIndicatorOpacityGate_SuppressesAnInvisibleRival()
        {
            // The gate that actually broke. It was first written as 0.05, on the reasoning
            // that alpha is multiplied by opacity anyway — but InvisibilityAbilitySO.Opacity
            // ships at 0.2, ABOVE that cutoff, so a cloaked rival still drew a full chevron
            // at ~0.17 alpha. Off-screen that chevron is the ONLY signal of their direction,
            // which is exactly what the ability exists to deny.
            //
            // Asserted as a RELATIONSHIP against the ability's authored value rather than as
            // a literal, so re-tuning Invisibility cannot silently reopen the hole.
            var invisibility = ScriptableObject.CreateInstance<InvisibilityAbilitySO>();
            try
            {
                Assert.That(RivalIndicator.MinVisibleOpacityForTests,
                    Is.GreaterThan(invisibility.Opacity),
                    "A rival cloaked by Invisibility must fall BELOW the indicator threshold, " +
                    "or the rival indicator becomes a tracking beacon that cancels the ability.");
            }
            finally { Object.DestroyImmediate(invisibility); }
        }

        [Test]
        public void KillSwitchConstant_IsNotCompileTimeFolded()
        {
            // FeedbackTuning.RivalIndicatorsEnabled is static readonly rather than const on
            // purpose: a const bool folds at compile time and turns every guard reading it
            // into a CS0162 unreachable-code warning. Reading it here also means the whole
            // tuning block is referenced by the suite and cannot silently rot.
            Assert.That(FeedbackTuning.RivalRingRadius, Is.GreaterThan(0f));
            Assert.That(FeedbackTuning.RivalChevronSizeFraction, Is.GreaterThan(0f));
            Assert.That(FeedbackTuning.RivalChevronViewportMargin,
                Is.LessThan(FeedbackTuning.RivalChevronSizeFraction),
                "The margin only has to clear the chevron's own half-size; a margin larger " +
                "than the chevron would push indicators pointlessly far inboard.");
        }

        // ── Clear viewport (WP7, decisions C6 / F6): "off screen" = outside the screen minus the HUD info zones ──

        private static readonly Rect TopBand = Rect.MinMaxRect(0f, 0.75f, 1f, 1f);
        private static readonly Rect Strip = Rect.MinMaxRect(0.05f, 0.30f, 0.20f, 0.40f);
        private static readonly Vector2 Clr = new Vector2(0.03f, 0.04f);
        private static readonly Rect Band = ScreenEdgeProjection.UniformSafeArea(Margin);

        private static System.Collections.Generic.List<Rect> InfoZones(params Rect[] zones) =>
            new System.Collections.Generic.List<Rect>(zones);

        [Test]
        public void ClearViewport_UnderTheTopBand_IsOffScreen_AndPinnedAtTheBandsLowerEdge()
        {
            var info = InfoZones(TopBand);
            bool off = ScreenEdgeProjection.ClampToClearViewport(new Vector2(0.3f, 0.9f), false, Band, info, info, Clr,
                out Vector2 clamped, out Vector2 bearing);

            Assert.That(off, Is.True, "a rival under the HUD is as hidden as one past the frame");
            Assert.That(clamped.x, Is.EqualTo(0.3f).Within(1e-5f), "straight below it");
            Assert.That(clamped.y, Is.EqualTo(0.75f - Clr.y).Within(1e-5f), "at the band's lower edge, the arrow clear of it");
            Assert.That(Vector2.Dot(bearing, Vector2.up), Is.GreaterThan(0.9999f));
        }

        [Test]
        public void ClearViewport_AboveTheFrame_PinsAtTheSameLowerEdge_NotUnderTheBand()
        {
            var info = InfoZones(TopBand);
            ScreenEdgeProjection.ClampToClearViewport(new Vector2(0.6f, 1.5f), false, Band, info, info, Clr,
                out Vector2 clamped, out _);
            Assert.That(clamped.y, Is.EqualTo(0.75f - Clr.y).Within(1e-5f),
                "the old top-edge spot (1 - margin) is now under the HUD band");
            Assert.That(ScreenEdgeProjection.IsInsideAnyZone(clamped, info, Clr), Is.False);
        }

        [Test]
        public void ClearViewport_ARivalInTheClearArea_IsOnScreen_EvenRightBelowTheBand()
        {
            var info = InfoZones(TopBand, Strip);
            Assert.That(ScreenEdgeProjection.ClampToClearViewport(new Vector2(0.5f, 0.5f), false, Band, info, info, Clr, out _, out _), Is.False);
            Assert.That(ScreenEdgeProjection.ClampToClearViewport(new Vector2(0.5f, 0.749f), false, Band, info, info, Clr, out _, out _), Is.False);
            Assert.That(ScreenEdgeProjection.ClampToClearViewport(new Vector2(0.97f, 0.5f), false, Band, info, info, Clr, out _, out _), Is.False,
                "inside the frame but within the margin is still on screen (unchanged)");
        }

        [Test]
        public void ClearViewport_UnderTheStatusStrip_IsOffScreen_AndTheArrowLeavesTheStrip()
        {
            var info = InfoZones(TopBand, Strip);
            var target = new Vector2(0.12f, 0.35f);
            bool off = ScreenEdgeProjection.ClampToClearViewport(target, false, Band, info, info, Clr, out Vector2 clamped, out Vector2 bearing);
            Assert.That(off, Is.True);
            Assert.That(ScreenEdgeProjection.IsInsideAnyZone(clamped, info, Clr), Is.False, "never drawn on the strip (the CW-009 case)");
            Assert.That(Vector2.Dot(bearing, (target - clamped).normalized), Is.GreaterThan(0.9999f), "still aimed at the rival");
        }

        [Test]
        public void ClearViewport_WithNoInfoZones_IsTheOldUnitRectContract()
        {
            var none = InfoZones();
            foreach (var t in new[] { new Vector2(0.5f, 0.5f), new Vector2(0.97f, 0.5f), new Vector2(1.4f, 0.5f), new Vector2(-0.3f, 1.7f) })
            {
                bool a = ScreenEdgeProjection.ClampToClearViewport(t, false, Band, none, none, Clr, out Vector2 pa, out Vector2 ba);
                bool b = ScreenEdgeProjection.ClampToEdge(t, false, Band, out Vector2 pb, out Vector2 bb);
                Assert.That(a, Is.EqualTo(b), t.ToString());
                Assert.That(Vector2.Distance(pa, pb), Is.LessThan(1e-6f), t.ToString());
                Assert.That(Vector2.Distance(ba, bb), Is.LessThan(1e-6f), t.ToString());
            }
        }

        [Test]
        public void ClearBand_CutsOnlyFullWidthEdgeZones()
        {
            Rect cut = ScreenEdgeProjection.ClearBand(Band, InfoZones(TopBand, Strip), Clr);
            Assert.That(cut.yMax, Is.EqualTo(0.75f - Clr.y).Within(1e-5f), "the top band cuts the band");
            Assert.That(cut.yMin, Is.EqualTo(Band.yMin).Within(1e-5f), "an island zone (the strip) does not");
            Assert.That(cut.xMin, Is.EqualTo(Band.xMin).Within(1e-5f));
            Assert.That(cut.xMax, Is.EqualTo(Band.xMax).Within(1e-5f));

            Rect bottom = ScreenEdgeProjection.ClearBand(Band, InfoZones(Rect.MinMaxRect(0f, 0f, 1f, 0.2f)), Clr);
            Assert.That(bottom.yMin, Is.EqualTo(0.2f + Clr.y).Within(1e-5f), "a full-width bottom zone cuts from below");
        }

        // ── The 50 % rule: a rival mostly under an info zone hides its nameplate and gets the chevron ──

        [Test]
        public void Coverage_IsTheVisibleShareInsideTheZones()
        {
            var info = InfoZones(TopBand, Strip);
            Assert.That(ScreenEdgeProjection.InfoZoneCoverage(Rect.MinMaxRect(0.4f, 0.5f, 0.5f, 1.0f), info, out Vector2 c), Is.EqualTo(0.5f).Within(1e-5f));
            Assert.That(TopBand.Contains(c), Is.True, "the covered piece's centre is inside the zone");
            Assert.That(c.y, Is.EqualTo(0.875f).Within(1e-5f));

            Assert.That(ScreenEdgeProjection.InfoZoneCoverage(Rect.MinMaxRect(0.4f, 0.8f, 0.5f, 0.9f), info, out _), Is.EqualTo(1f).Within(1e-5f));
            Assert.That(ScreenEdgeProjection.InfoZoneCoverage(Rect.MinMaxRect(0.4f, 0.5f, 0.5f, 0.6f), info, out _), Is.EqualTo(0f));
            Assert.That(ScreenEdgeProjection.InfoZoneCoverage(Rect.MinMaxRect(0.4f, 0.5f, 0.5f, 1.5f), info, out _), Is.EqualTo(0.5f).Within(1e-5f),
                "only the VISIBLE part counts: the half above the frame is not under the HUD");
            Assert.That(ScreenEdgeProjection.InfoZoneCoverage(Rect.MinMaxRect(1.2f, 0.5f, 1.3f, 0.6f), info, out _), Is.EqualTo(0f), "nothing visible");
            Assert.That(ScreenEdgeProjection.InfoZoneCoverage(Rect.MinMaxRect(0.10f, 0.30f, 0.15f, 0.40f), info, out _), Is.EqualTo(1f).Within(1e-5f), "the strip counts");
        }

        [Test]
        public void UnderTheHud_FromHalf_WithExitHysteresis()
        {
            Assert.That(ScreenEdgeProjection.IsUnderInfoZone(0.5f, false), Is.True, ">= 50 % hides the nameplate");
            Assert.That(ScreenEdgeProjection.IsUnderInfoZone(0.49f, false), Is.False);
            Assert.That(ScreenEdgeProjection.IsUnderInfoZone(0.45f, true), Is.True, "once under, it stays under until below 40 %");
            Assert.That(ScreenEdgeProjection.IsUnderInfoZone(0.39f, true), Is.False);
            Assert.That(ScreenEdgeProjection.InfoZoneShowFraction, Is.LessThan(ScreenEdgeProjection.InfoZoneHideFraction));
        }

        [Test]
        public void UnderTheHud_ArrowFromTheCoveredPiece_SitsBelowTheBand_PointingUpAtTheRival()
        {
            // The RivalIndicator path: the body's feet are below the band, the nameplate and head are inside it.
            var info = InfoZones(TopBand);
            var mark = Rect.MinMaxRect(0.62f, 0.70f, 0.70f, 0.90f);   // 75 % inside
            float cover = ScreenEdgeProjection.InfoZoneCoverage(mark, info, out Vector2 covered);
            Assert.That(ScreenEdgeProjection.IsUnderInfoZone(cover, false), Is.True);

            bool off = ScreenEdgeProjection.ClampToClearViewport(covered, true, Band, info, info, Clr, out Vector2 clamped, out Vector2 bearing);
            Assert.That(off, Is.True);
            Assert.That(clamped.x, Is.EqualTo(0.66f).Within(1e-5f));
            Assert.That(clamped.y, Is.EqualTo(0.75f - Clr.y).Within(1e-5f), "at the zone's lower edge, not on the bird's feet");
            Assert.That(Vector2.Dot(bearing, Vector2.up), Is.GreaterThan(0.9999f));
        }
    }
}

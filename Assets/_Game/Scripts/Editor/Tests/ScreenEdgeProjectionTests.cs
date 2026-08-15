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
    }
}

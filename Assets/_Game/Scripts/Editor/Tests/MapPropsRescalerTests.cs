using CluckWars.EditorTools;
using NUnit.Framework;

namespace CluckWars.Tests
{
    /// <summary>
    /// Pins the fence layout rule in <see cref="MapPropsRescaler"/>.
    ///
    /// <para>
    /// The point of these is the AUTHORED ROUND-TRIP: fed the 38 m arena the dressing was
    /// hand-placed against, the rule must reproduce what is actually in <c>Map.unity</c>.
    /// A rule that merely "fits" some arena would pass a spacing sanity check while
    /// silently describing a different fence — the same class of hole that let a +20%
    /// move-speed drift ship green (see docs/STATE.md, 2026-08-13).
    /// </para>
    /// </summary>
    public class MapPropsRescalerTests
    {
        private const float AuthoredPlaneSize = 38f;
        private const float AuthoredFenceLine = 18.60f;
        private const int AuthoredPostCount = 10;

        private const float RescaledPlaneSize = 51.3f;
        private const float PanelWidth = 3.99f;
        private const float PanelHalfDepth = 0.245f;

        [Test]
        public void FenceLayout_AtTheAuthoredArenaSize_ReproducesTheAuthoredFence()
        {
            var layout = MapPropsRescaler.SolveFenceLayout(AuthoredPlaneSize);

            Assert.That(layout.Line, Is.EqualTo(AuthoredFenceLine).Within(0.001f),
                "The rule must land on the hand-authored fence line, not merely near it.");
            Assert.That(layout.PostCount, Is.EqualTo(AuthoredPostCount),
                "38 m must yield the 10 posts per side that are actually placed in Map.unity.");
        }

        [Test]
        public void FenceLayout_KeepsItsOuterFaceClearOfTheHardLimitByTheAuthoredStandoff()
        {
            // MapGenerator centres each boundary wall at half + t*0.5 with thickness t, so
            // the face pointing INTO the arena is exactly half. Taking the wall's centre
            // here instead is a real mistake that was made once and skewed every derived
            // number by 0.25 m.
            float authoredStandoff =
                AuthoredPlaneSize * 0.5f - (AuthoredFenceLine + PanelHalfDepth);

            var rescaled = MapPropsRescaler.SolveFenceLayout(RescaledPlaneSize);
            float rescaledStandoff =
                RescaledPlaneSize * 0.5f - (rescaled.Line + PanelHalfDepth);

            Assert.That(rescaledStandoff, Is.EqualTo(authoredStandoff).Within(0.001f),
                "The standoff is a clearance against a fixed-size panel — it must stay " +
                "absolute, or the chicken's body reaches further past the visible fence " +
                "than it was authored to.");
        }

        [Test]
        public void FenceLayout_SealsEveryCorner_ByCoveringExactlyTheFullSide()
        {
            foreach (float planeSize in new[] { AuthoredPlaneSize, RescaledPlaneSize, 70f })
            {
                var layout = MapPropsRescaler.SolveFenceLayout(planeSize);
                float endPostCentre = layout.Spacing * (layout.PostCount - 1) * 0.5f;
                float coveredEdge = endPostCentre + PanelWidth * 0.5f;

                Assert.That(coveredEdge, Is.EqualTo(layout.Line).Within(0.001f),
                    $"At {planeSize} m the end panel's edge must land on the perpendicular " +
                    "run's centre plane, which is what seals the corner without a tuned inset.");
            }
        }

        [Test]
        public void FenceLayout_NeverLeavesAGapBetweenPanels()
        {
            foreach (float planeSize in new[] { AuthoredPlaneSize, RescaledPlaneSize, 45f, 70f })
            {
                var layout = MapPropsRescaler.SolveFenceLayout(planeSize);

                Assert.That(layout.Spacing, Is.LessThanOrEqualTo(PanelWidth + 0.001f),
                    $"At {planeSize} m the fence would become a picket line with holes in it.");
            }
        }

        [Test]
        public void FenceLayout_KeepsOverlapBelowTheAuthoredAmount()
        {
            // Prop_wall_segment squashes its mesh ~3x along the run (m_LocalScale.x = 0.35),
            // so overlap doubles ~3x its own length in coplanar plank faces and z-fights.
            // Less overlap is strictly better here, provided there is no gap.
            const float AuthoredOverlap = PanelWidth - 3.72f;

            var rescaled = MapPropsRescaler.SolveFenceLayout(RescaledPlaneSize);
            float overlap = PanelWidth - rescaled.Spacing;

            Assert.That(overlap, Is.GreaterThanOrEqualTo(0f), "A negative overlap is a gap.");
            Assert.That(overlap, Is.LessThan(AuthoredOverlap),
                "The rescaled fence should z-fight less than the authored one, not more.");
        }
    }
}

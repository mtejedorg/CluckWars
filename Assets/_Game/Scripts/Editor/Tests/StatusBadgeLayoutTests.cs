using NUnit.Framework;
using UnityEngine;
using CluckWars.Visuals;

namespace CluckWars.Tests
{
    /// <summary>
    /// Feedback batch D0: the world status badge stack sits wholly above the nameplate pill (which spans roughly
    /// 1.48-1.93 around its 1.70 centre), under the floating-text floor, and pops spawn from that same floor.
    /// </summary>
    public sealed class StatusBadgeLayoutTests
    {
        /// <summary>Top of the nameplate pill with margin; the lowest the first badge row may reach.</summary>
        private const float NameplateClearance = 2.0f;

        private static float RowBottom => FeedbackTuning.StatusBadgeStackBaseHeight - FeedbackTuning.StatusBadgeGlyphSize * 0.5f;

        private static float StackTop =>
            FeedbackTuning.StatusBadgeStackBaseHeight
            + FeedbackTuning.StatusBadgeRowSpacing * (FeedbackTuning.StatusBadgeMaxRows - 1)
            + FeedbackTuning.StatusBadgeGlyphSize * 0.5f;

        [Test]
        public void FirstRowClearsTheNameplatePill()
        {
            Assert.GreaterOrEqual(RowBottom, NameplateClearance,
                "row 1 must sit above the nameplate pill so the badges never cover or garble the name");
        }

        [Test]
        public void StackTopStaysUnderTheFloatingTextFloor()
        {
            Assert.Less(StackTop, FeedbackTuning.FloatingTextSpawnHeight,
                "pops start above the full badge stack");
        }

        [Test]
        public void SelfPopSpawnsAtTheFloatingTextConstant()
        {
            var pivot = new Vector3(3f, 0.25f, -4f);
            Vector3 p = FloatingCombatText.PopSpawnPosition(pivot);
            Assert.AreEqual(FeedbackTuning.FloatingTextSpawnHeight, p.y - pivot.y, 1e-5f);
            Assert.AreEqual(pivot.x, p.x, 1e-5f);
            Assert.AreEqual(pivot.z, p.z, 1e-5f);
        }

        [Test]
        public void MarkMarkerClearsTheBadgeStack()
        {
            Assert.GreaterOrEqual(FeedbackTuning.MarkMarkerClearHeight, StackTop,
                "the on-screen mark marker must clear the badge stack");
        }
    }
}

using CluckWars.UI;
using NUnit.Framework;

namespace CluckWars.Tests
{
    /// <summary>The live-vs-static, render gating, texture sizing and motion decisions of the menu's
    /// live chicken stage (<see cref="MenuStagePolicy"/>).</summary>
    public sealed class MenuStagePolicyTests
    {
        [TestCase(false, false, true)]
        [TestCase(true, false, false)]   // Performance Mode ON: static renders
        [TestCase(false, true, false)]   // stage failed to come up: static fallback
        [TestCase(true, true, false)]
        public void WantsLive_OnlyWithPerformanceModeOffAndAWorkingStage(bool perf, bool failed, bool expected) =>
            Assert.AreEqual(expected, MenuStagePolicy.WantsLive(perf, failed));

        [Test]
        public void ShouldRender_RequiresLiveChickenVisiblePageAndARealSize()
        {
            Assert.IsTrue(MenuStagePolicy.ShouldRender(true, true, true, 512));
            Assert.IsFalse(MenuStagePolicy.ShouldRender(false, true, true, 512), "static mode never renders");
            Assert.IsFalse(MenuStagePolicy.ShouldRender(true, false, true, 512), "empty seat");
            Assert.IsFalse(MenuStagePolicy.ShouldRender(true, true, false, 512), "page not on screen");
            Assert.IsFalse(MenuStagePolicy.ShouldRender(true, true, true, 0), "element not laid out");
        }

        [Test]
        public void TextureSide_IsTheShorterSideInPixels_ClampedToTheCap()
        {
            Assert.AreEqual(300, MenuStagePolicy.TextureSide(310f, 300f, 1f));
            Assert.AreEqual(465, MenuStagePolicy.TextureSide(310f, 400f, 1.5f));
            Assert.AreEqual(MenuStagePolicy.MaxTextureSide, MenuStagePolicy.TextureSide(2000f, 2000f, 2f));
            Assert.AreEqual(256, MenuStagePolicy.TextureSide(900f, 900f, 1f, cap: 256));
        }

        [Test]
        public void TextureSide_IsZero_ForAnElementThatIsNotLaidOut()
        {
            Assert.AreEqual(0, MenuStagePolicy.TextureSide(float.NaN, 300f, 1f));
            Assert.AreEqual(0, MenuStagePolicy.TextureSide(0f, 0f, 1f));
            Assert.AreEqual(0, MenuStagePolicy.TextureSide(40f, 300f, 1f), "below the minimum side");
            Assert.AreEqual(0, MenuStagePolicy.TextureSide(300f, 300f, 0f));
        }

        [Test]
        public void NeedsRealloc_IgnoresLayoutJitter_ButFollowsRealResizes()
        {
            Assert.IsTrue(MenuStagePolicy.NeedsRealloc(0, 300), "first allocation");
            Assert.IsFalse(MenuStagePolicy.NeedsRealloc(300, 310), "a few pixels of jitter");
            Assert.IsTrue(MenuStagePolicy.NeedsRealloc(300, 600), "panel resized");
            Assert.IsFalse(MenuStagePolicy.NeedsRealloc(300, 0), "never reallocate to nothing");
        }

        [Test]
        public void Yaw_ReducedMotion_ParksAtTheRestView()
        {
            Assert.AreEqual(MenuStagePolicy.RestYaw, MenuStagePolicy.Yaw(12.3f, 0f, false, true));
            Assert.AreEqual(MenuStagePolicy.RestYaw, MenuStagePolicy.Yaw(12.3f, 1.7f, true, true));
        }

        [Test]
        public void Yaw_Turntable_StartsAtRestAndTurns_SwayStaysNearTheFrontView()
        {
            Assert.AreEqual(MenuStagePolicy.RestYaw, MenuStagePolicy.Yaw(0f, 0f, false, false), 1e-3f);
            Assert.AreEqual(MenuStagePolicy.RestYaw + MenuStagePolicy.TurntableDegreesPerSecond,
                MenuStagePolicy.Yaw(1f, 0f, false, false), 1e-3f);
            for (float t = 0f; t < 60f; t += 0.37f)
                Assert.That(MenuStagePolicy.Yaw(t, 0f, true, false),
                    Is.InRange(MenuStagePolicy.RestYaw - 25.01f, MenuStagePolicy.RestYaw + 25.01f));
        }

        [Test]
        public void HopHeight_IsOneParabola_PeakingMidHop()
        {
            Assert.AreEqual(0f, MenuStagePolicy.HopHeight(-0.1f));
            Assert.AreEqual(0f, MenuStagePolicy.HopHeight(0f));
            Assert.AreEqual(1f, MenuStagePolicy.HopHeight(MenuStagePolicy.HopDuration * 0.5f), 1e-4f);
            Assert.AreEqual(0f, MenuStagePolicy.HopHeight(MenuStagePolicy.HopDuration));
            Assert.Less(MenuStagePolicy.HopHeight(MenuStagePolicy.HopDuration * 0.2f), 1f);
        }
    }
}

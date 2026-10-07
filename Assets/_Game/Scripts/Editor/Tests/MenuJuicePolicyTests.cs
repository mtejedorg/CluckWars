using CluckWars.UI;
using NUnit.Framework;

namespace CluckWars.Tests
{
    /// <summary>Timings, curves and decisions of the menu juice (<see cref="MenuJuicePolicy"/>).</summary>
    public sealed class MenuJuicePolicyTests
    {
        // ---- Pop -------------------------------------------------------------------------------
        [Test]
        public void PopFactor_StartsAndEndsAtOne_SquashesThenOvershoots()
        {
            Assert.AreEqual(1f, MenuJuicePolicy.PopFactor(0f), 1e-4f);
            Assert.AreEqual(1f, MenuJuicePolicy.PopFactor(MenuJuicePolicy.PopSeconds), 1e-4f);
            Assert.AreEqual(1f, MenuJuicePolicy.PopFactor(5f), 1e-4f);
            Assert.AreEqual(MenuJuicePolicy.PopSquash, MenuJuicePolicy.PopFactor(MenuJuicePolicy.PopSeconds * 0.36f), 1e-3f);
            Assert.AreEqual(MenuJuicePolicy.PopOvershoot, MenuJuicePolicy.PopFactor(MenuJuicePolicy.PopSeconds * 0.72f), 1e-3f);
        }

        [Test]
        public void PopFactor_StaysWithinTheSquashAndOvershootBounds()
        {
            for (float t = 0f; t <= MenuJuicePolicy.PopSeconds; t += 0.002f)
            {
                float f = MenuJuicePolicy.PopFactor(t);
                Assert.GreaterOrEqual(f, MenuJuicePolicy.PopSquash - 1e-3f);
                Assert.LessOrEqual(f, MenuJuicePolicy.PopOvershoot + 1e-3f);
            }
        }

        // ---- Stagger ---------------------------------------------------------------------------
        [TestCase(1)] [TestCase(4)] [TestCase(9)] [TestCase(16)] [TestCase(40)]
        public void Stagger_NeverTakesLongerThan400ms(int count) =>
            Assert.Less(MenuJuicePolicy.StaggerTotal(count), 0.4f);

        [Test]
        public void Stagger_IsFortyMsApartWhileItFits_AndCompressesWhenItDoesNot()
        {
            Assert.AreEqual(0f, MenuJuicePolicy.StaggerDelay(0, 4));
            Assert.AreEqual(0.04f, MenuJuicePolicy.StaggerDelay(1, 4), 1e-5f);
            Assert.AreEqual(0.12f, MenuJuicePolicy.StaggerDelay(3, 4), 1e-5f);
            Assert.AreEqual(MenuJuicePolicy.StaggerMaxDelaySeconds, MenuJuicePolicy.StaggerDelay(15, 16), 1e-5f);
            Assert.Less(MenuJuicePolicy.StaggerDelay(1, 16), 0.04f, "dense lists tighten the step");
        }

        [Test]
        public void Stagger_DelaysNeverDecrease()
        {
            float prev = -1f;
            for (int i = 0; i < 12; i++)
            {
                float d = MenuJuicePolicy.StaggerDelay(i, 12);
                Assert.GreaterOrEqual(d, prev);
                prev = d;
            }
        }

        // ---- Fly -------------------------------------------------------------------------------
        [Test]
        public void Flight_EasesFromZeroToOne_AndTheArcIsZeroAtBothEnds()
        {
            Assert.AreEqual(0f, MenuJuicePolicy.EaseInOut(0f));
            Assert.AreEqual(1f, MenuJuicePolicy.EaseInOut(1f));
            Assert.AreEqual(0.5f, MenuJuicePolicy.EaseInOut(0.5f), 1e-5f);
            Assert.AreEqual(0f, MenuJuicePolicy.FlightArc(0f), 1e-5f);
            Assert.AreEqual(0f, MenuJuicePolicy.FlightArc(1f), 1e-5f);
            Assert.AreEqual(MenuJuicePolicy.FlightArcPx, MenuJuicePolicy.FlightArc(0.5f), 1e-4f);
            Assert.That(MenuJuicePolicy.FlightSeconds, Is.InRange(0.25f, 0.30f));
        }

        // ---- Stamp -----------------------------------------------------------------------------
        [Test]
        public void Stamp_SlamsFromBigAndTiltedToRest_HitFrameIsTheSlamEnd()
        {
            Assert.AreEqual(MenuJuicePolicy.StampFromScale, MenuJuicePolicy.StampScale(0f), 1e-4f);
            Assert.AreEqual(MenuJuicePolicy.StampImpactScale, MenuJuicePolicy.StampScale(MenuJuicePolicy.StampHitSeconds), 1e-3f);
            Assert.AreEqual(1f, MenuJuicePolicy.StampScale(MenuJuicePolicy.StampSeconds), 1e-4f);
            Assert.AreEqual(MenuJuicePolicy.StampFromDegrees, MenuJuicePolicy.StampDegrees(0f), 1e-4f);
            Assert.AreEqual(0f, MenuJuicePolicy.StampDegrees(MenuJuicePolicy.StampHitSeconds), 1e-4f);
            Assert.AreEqual(0f, MenuJuicePolicy.StampDegrees(2f), 1e-4f, "rests upright, same as the static layout");
            Assert.AreEqual(1f, MenuJuicePolicy.StampOpacity(MenuJuicePolicy.StampHitSeconds));
            Assert.AreEqual(MenuJuicePolicy.StampSlamSeconds, MenuJuicePolicy.StampHitSeconds);
        }

        // ---- Intro countdown cues ---------------------------------------------------------------
        [Test]
        public void CountdownSlam_PopsInBigAndRestsAtOne()
        {
            Assert.AreEqual(1.7f, MenuJuicePolicy.CountdownScale(0f), 1e-3f);
            Assert.AreEqual(1f, MenuJuicePolicy.CountdownScale(MenuJuicePolicy.CountdownPopSeconds), 1e-4f);
            Assert.AreEqual(1f, MenuJuicePolicy.CountdownScale(0.6f), 1e-4f);
        }

        [Test]
        public void IntroCues_TickOnEachNewWholeSecond_ThenOneGo()
        {
            var t = new IntroCueTracker();
            int n;
            Assert.AreEqual(IntroCueTracker.Cue.Tick, t.Observe(true, 2.95f, out n)); Assert.AreEqual(3, n);
            Assert.AreEqual(IntroCueTracker.Cue.None, t.Observe(true, 2.10f, out n), "same second: no repeat");
            Assert.AreEqual(IntroCueTracker.Cue.Tick, t.Observe(true, 1.99f, out n)); Assert.AreEqual(2, n);
            Assert.AreEqual(IntroCueTracker.Cue.Tick, t.Observe(true, 0.40f, out n)); Assert.AreEqual(1, n);
            Assert.AreEqual(IntroCueTracker.Cue.Go, t.Observe(false, 0f, out n));
            Assert.AreEqual(IntroCueTracker.Cue.None, t.Observe(false, 0f, out n), "GO fires once");
        }

        [Test]
        public void IntroCues_LateJoinerAfterGoGetsNoStrayGo_MidIntroStartsAtTheCurrentNumber()
        {
            var after = new IntroCueTracker();
            int n;
            Assert.AreEqual(IntroCueTracker.Cue.None, after.Observe(false, 0f, out n));

            var mid = new IntroCueTracker();
            Assert.AreEqual(IntroCueTracker.Cue.Tick, mid.Observe(true, 1.4f, out n)); Assert.AreEqual(2, n);
            Assert.AreEqual(IntroCueTracker.Cue.Tick, mid.Observe(true, 0.9f, out n)); Assert.AreEqual(1, n);
            Assert.AreEqual(IntroCueTracker.Cue.Go, mid.Observe(false, 0f, out n));
        }

        [Test]
        public void IntroCues_PlayAgainStartsAFreshIntro_AndAnAbortedOneNeverGoes()
        {
            var t = new IntroCueTracker();
            int n;
            t.Observe(true, 2.5f, out n); t.Observe(false, 0f, out n);          // first match: 3 ... GO
            Assert.AreEqual(IntroCueTracker.Cue.Tick, t.Observe(true, 2.9f, out n)); Assert.AreEqual(3, n);

            t.Reset();                                                         // session gone mid-intro
            Assert.AreEqual(IntroCueTracker.Cue.None, t.Observe(false, 0f, out n));
        }

        // ---- Particles / celebration -----------------------------------------------------------
        [Test]
        public void Burst_FadesOutAndSparklesVanishAtBothEnds()
        {
            Assert.AreEqual(1f, MenuJuicePolicy.BurstOpacity(0.2f), 1e-4f);
            Assert.AreEqual(0f, MenuJuicePolicy.BurstOpacity(1f), 1e-4f);
            Assert.AreEqual(0f, MenuJuicePolicy.SparkleScale(0f), 1e-4f);
            Assert.AreEqual(0f, MenuJuicePolicy.SparkleScale(1f), 1e-4f);
            Assert.AreEqual(1f, MenuJuicePolicy.SparkleScale(0.5f), 1e-4f);
            Assert.AreEqual(50f, MenuJuicePolicy.BurstDistance(1f, 50f), 1e-4f);
            Assert.LessOrEqual(MenuJuicePolicy.MaxBurstParticles, 6);
        }

        [Test]
        public void Drift_LoopsInsideTheScreenAndFadesAtTheSeam()
        {
            for (float t = 0f; t < 60f; t += 0.37f)
            {
                float f = MenuJuicePolicy.DriftFall(t, 9f, 0.3f);
                Assert.That(f, Is.InRange(0f, 1f));
            }
            Assert.AreEqual(MenuJuicePolicy.DriftFall(1f, 9f, 0.3f), MenuJuicePolicy.DriftFall(10f, 9f, 0.3f), 1e-4f, "one cycle later: same place");
            Assert.AreEqual(0f, MenuJuicePolicy.DriftOpacity(0f, 0.5f), 1e-5f);
            Assert.AreEqual(0.5f, MenuJuicePolicy.DriftOpacity(0.5f, 0.5f), 1e-5f);
            Assert.AreEqual(0f, MenuJuicePolicy.DriftOpacity(1f, 0.5f), 1e-5f);
            Assert.AreEqual(0f, MenuJuicePolicy.DriftSway(0f, 40f, 0.2f, 0f), 1e-5f);
        }
    }
}

using System.Collections.Generic;
using CluckWars.Visuals;
using NUnit.Framework;
using UnityEngine;

namespace CluckWars.Tests
{
    /// <summary>BL-232: pops at one bird stack upward from one shared spawn (every kit), capped;.</summary>
    public class FloatingTextStackTests
    {
        private const string Scripts = "Assets/_Game/Scripts/";

        [Test]
        public void TwoPopsAtOneBird_InTheSameFrame_StartAtDifferentHeights_AFixedStepApart()
        {
            var bird = new Vector3(3f, 2.5f, -4f);
            var live = new List<Vector3>();
            var first = FloatingCombatText.StackedSpawnPosition(bird, live);
            live.Add(first);
            var second = FloatingCombatText.StackedSpawnPosition(bird, live);

            Assert.AreEqual(bird.y, first.y, 1e-4f, "the first pop at a bird starts where it was asked");
            Assert.Greater(second.y, first.y, "the second pop starts higher");
            Assert.AreEqual(FeedbackTuning.FloatingTextStackStep, second.y - first.y, 1e-4f, "one fixed step");
            Assert.AreEqual(bird.x, second.x, 1e-4f);
            Assert.AreEqual(bird.z, second.z, 1e-4f);
        }

        [Test]
        public void TheStack_HoldsAtTheCap_AndFallsBackAsPopsExpire()
        {
            var bird = Vector3.zero;
            var live = new List<Vector3>();
            float top = 0f;
            for (int i = 0; i < FeedbackTuning.FloatingTextStackMax + 4; i++)
            {
                var p = FloatingCombatText.StackedSpawnPosition(bird, live);
                live.Add(p);
                top = Mathf.Max(top, p.y);
            }
            Assert.AreEqual(FeedbackTuning.FloatingTextStackMax * FeedbackTuning.FloatingTextStackStep, top, 1e-4f, "never more than the cap of steps");

            live.Clear();
            Assert.AreEqual(bird.y, FloatingCombatText.StackedSpawnPosition(bird, live).y, 1e-4f, "with the older pops gone the next one starts at the base again");
        }

        [Test]
        public void APopAtAnotherBird_IsNotLifted_ByThePopsAtThisOne()
        {
            var live = new List<Vector3> { new Vector3(0f, 2.5f, 0f), new Vector3(0f, 2.86f, 0f) };
            var far = new Vector3(FeedbackTuning.FloatingTextStackRadius + 2f, 2.5f, 0f);
            Assert.AreEqual(far.y, FloatingCombatText.StackedSpawnPosition(far, live).y, 1e-4f);
            Assert.AreEqual(0, FloatingCombatText.NearbyLive(far, live));
            Assert.AreEqual(2, FloatingCombatText.NearbyLive(Vector3.zero, live));
        }

        [Test]
        public void TheSharedSpawn_AppliesTheStack_AndItsNumbersLiveInTheTuning()
        {
            string src = RuntimeSourceGuardTests.Code(Scripts + "Visuals/FloatingCombatText.cs");
            StringAssert.Contains("worldPos = StackedSpawnPosition(worldPos, _liveOrigins);", src, "the one spawn every kit goes through lifts the pop");
            Assert.Greater(FeedbackTuning.FloatingTextStackStep, 0f);
            Assert.GreaterOrEqual(FeedbackTuning.FloatingTextStackMax, 1);
        }
    }
}

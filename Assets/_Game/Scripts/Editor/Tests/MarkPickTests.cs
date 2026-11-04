using System;
using System.Collections.Generic;
using System.IO;
using CluckWars.Abilities;
using CluckWars.Gameplay;
using NUnit.Framework;
using UnityEngine;

namespace CluckWars.Tests
{
    /// <summary>Death Mark v2: the random pick, the execute's numbers in data, and the rules that are gone.</summary>
    public class MarkPickTests
    {
        private const int A = 1, B = 2, C = 3;

        private static List<int> After(int last, int rivals, params int[] living)
        {
            var list = new List<int>(living);
            MarkPickRules.Filter(list, true, last, rivals);
            return list;
        }

        [Test]
        public void TheLastMark_IsNeverPickedAgain() => CollectionAssert.AreEqual(new[] { B, C }, After(A, 3, A, B, C));

        [Test]
        public void TheOnlyRival_CanBeMarkedAgain() => CollectionAssert.AreEqual(new[] { A }, After(A, 1, A));

        [Test]
        public void TheOthersBeingOutOfPlay_DoesNotReleaseTheLastMark() => CollectionAssert.IsEmpty(After(A, 3, A));

        [Test]
        public void NoHistory_RemovesNobody()
        {
            var list = new List<int> { A, B };
            MarkPickRules.Filter(list, false, A, 3);
            Assert.AreEqual(2, list.Count);
        }

        [TestCase(0, 0.5f, -1)]
        [TestCase(3, 0f, 0)]
        [TestCase(3, 0.5f, 1)]
        [TestCase(3, 0.999f, 2)]
        [TestCase(3, 1f, 2)]
        public void Pick_SpansTheCandidates(int count, float roll, int expected) =>
            Assert.AreEqual(expected, MarkPickRules.Pick(count, roll));

        // ---- Weighted pick: roll * w * w, highest wins -------------------------------------------------------------

        private static int Weighted(float[] w, float[] rolls, float fallback = 0.5f) => MarkPickRules.PickWeighted(w, rolls, fallback);

        [Test]
        public void FarFromHome_BeatsNearHome_MostOfTheTime()
        {
            var rng = new System.Random(20261104);
            var w = new[] { 0.9f, 0.3f };   // far, near
            int far = 0, near = 0;
            for (int i = 0; i < 10000; i++)
            {
                var rolls = new[] { (float)rng.NextDouble(), (float)rng.NextDouble() };
                if (Weighted(w, rolls) == 0) far++; else near++;
            }
            // Expected far share 1 - 0.09 / (2 * 0.81) = 94.4 %.
            Assert.Greater(far, 9200, $"far won {far}, near {near}");
            Assert.Greater(near, 200, "the near one still gets picked now and then: it is weighted, not ranked");
        }

        [Test]
        public void AtHome_IsNeverPicked_WhileAnotherCandidateIsAwayFromHome()
        {
            var rng = new System.Random(7);
            var w = new[] { 0f, 0.05f, 0f };
            for (int i = 0; i < 5000; i++)
            {
                var rolls = new[] { (float)rng.NextDouble(), (float)rng.NextDouble(), (float)rng.NextDouble() };
                if (rolls[1] <= 0f) continue;   // a 0 roll scores 0 too: that is the fallback case, tested apart
                Assert.AreEqual(1, Weighted(w, rolls, (float)rng.NextDouble()));
            }
        }

        [Test]
        public void EqualWeights_PickUniformly()
        {
            var rng = new System.Random(99);
            var w = new[] { 0.6f, 0.6f, 0.6f };
            var hits = new int[3];
            for (int i = 0; i < 9000; i++)
                hits[Weighted(w, new[] { (float)rng.NextDouble(), (float)rng.NextDouble(), (float)rng.NextDouble() })]++;
            foreach (int h in hits) Assert.That(h, Is.InRange(2700, 3300), $"hits {hits[0]}/{hits[1]}/{hits[2]}");
        }

        [Test]
        public void TheLastMark_IsNeverPicked_EvenWhenItIsTheFarthest()
        {
            var list = After(A, 3, A, B, C);   // A is the far one
            var weights = new List<float>();
            foreach (int id in list) weights.Add(id == B ? 0.2f : 0.1f);
            var rng = new System.Random(3);
            for (int i = 0; i < 2000; i++)
            {
                int idx = Weighted(weights.ToArray(), new[] { (float)rng.NextDouble(), (float)rng.NextDouble() });
                Assert.AreNotEqual(A, list[idx]);
            }
        }

        [Test]
        public void TheOnlyRival_IsPicked_EvenAtHome_AndEvenIfItWasTheLastMark()
        {
            var list = After(A, 1, A);
            CollectionAssert.AreEqual(new[] { A }, list);
            Assert.AreEqual(0, Weighted(new[] { 0f }, new[] { 0.7f }), "w = 0 scores 0: the uniform fallback still lands the mark");
        }

        [Test]
        public void EveryScoreZero_FallsBackToAUniformPick_AndNeverFailsWithACandidate()
        {
            // Everyone at home.
            Assert.AreEqual(0, Weighted(new[] { 0f, 0f, 0f }, new[] { 0.9f, 0.9f, 0.9f }, 0f));
            Assert.AreEqual(1, Weighted(new[] { 0f, 0f, 0f }, new[] { 0.9f, 0.9f, 0.9f }, 0.5f));
            Assert.AreEqual(2, Weighted(new[] { 0f, 0f, 0f }, new[] { 0.9f, 0.9f, 0.9f }, 0.999f));
            // Away from home, but every roll is 0.
            Assert.AreEqual(1, Weighted(new[] { 1f, 1f, 1f }, new[] { 0f, 0f, 0f }, 0.5f));
            // Never -1 while a candidate exists, whatever the numbers.
            var rng = new System.Random(11);
            for (int i = 0; i < 2000; i++)
            {
                int n = 1 + rng.Next(4);
                var w = new float[n]; var r = new float[n];
                for (int k = 0; k < n; k++) { w[k] = rng.Next(2) * (float)rng.NextDouble(); r[k] = rng.Next(3) == 0 ? 0f : (float)rng.NextDouble(); }
                int idx = Weighted(w, r, (float)rng.NextDouble());
                Assert.That(idx, Is.InRange(0, n - 1));
            }
            Assert.AreEqual(-1, Weighted(new float[0], new float[0]), "nobody is still nobody");
        }

        [Test]
        public void HomeFraction_IsTheDistanceOverTheBaseSpan_ClampedToUnit()
        {
            Assert.AreEqual(0f, MarkPickRules.HomeFraction(0f, 70f));
            Assert.AreEqual(0.5f, MarkPickRules.HomeFraction(35f, 70f), 1e-6f);
            Assert.AreEqual(1f, MarkPickRules.HomeFraction(90f, 70f), "beyond the span is as far as it gets");
            Assert.AreEqual(0f, MarkPickRules.HomeFraction(-1f, 70f));
            Assert.AreEqual(1f, MarkPickRules.HomeFraction(10f, 0f), "no base data: every candidate weighs the same");
            Assert.AreEqual(0.25f * 0.8f, MarkPickRules.Score(0.8f, 0.5f), 1e-6f);
        }

        [Test]
        public void ThePress_PicksWeighted_AndAnOutOfPlayRivalIsNeverACandidate_SourceGuard()
        {
            // GatherMarkCandidates needs a live Fusion runner, so the out-of-play exclusion is pinned on the source; the pure
            // contract (candidates in, filtered, scored) is tested above.
            var src = Execute;
            StringAssert.Contains("int pick = PickWeighted();", src);
            int gather = src.IndexOf("private void GatherMarkCandidates()", StringComparison.Ordinal);
            string body = src.Substring(gather, src.IndexOf("\n        }", gather, StringComparison.Ordinal) - gather);
            StringAssert.Contains("c.Combat != null && c.Combat.IsRemoved) continue;", body);
            StringAssert.Contains("c.IsDecoy", body);
            StringAssert.Contains("MarkPickRules.Filter(_candidates", body);
        }

        [Test]
        public void ShippedMarkKill_NumbersKeepTheirRelations()
        {
            // Rules, not values: every number here is meant to be retuned.
            var m = TestAssets.Load<MarkKillAbilitySO>(TestAssets.AbilitiesDir + "/MarkKill.asset");
            Assert.Greater(m.KillRange, 0f);
            Assert.Greater(m.WindowSeconds, 0f);
            Assert.GreaterOrEqual(m.SuccessCooldownSeconds, m.FailCooldownSeconds, "a miss must recover no slower than a kill");
        }

        [Test]
        public void TheVictimHasTimeToSeeTheMark_BeforeAKillCanQualify()
        {
            // Mark a bird already stunned, Kill at once: the removal's evidence needs the mark on the victim's authority first.
            var m = TestAssets.Load<MarkKillAbilitySO>(TestAssets.AbilitiesDir + "/MarkKill.asset");
            Assert.GreaterOrEqual(m.ArmSeconds, ExecuteRules.MaxLatencySeconds);
            Assert.Less(m.ArmSeconds, m.WindowSeconds);
        }

        [Test]
        public void TheMarkWindow_LeavesRoomForTheDrawAndAStunAfterTheArm()
        {
            // Rules for a short mark (Maestro 2026-11-04: 4 s): the draw ends inside the arm, and after the arm there is room
            // for the round-start-length hunt plus at least one Ambush stun (StunDuration) before expiry.
            var m = TestAssets.Load<MarkKillAbilitySO>(TestAssets.AbilitiesDir + "/MarkKill.asset");
            var ambush = TestAssets.Load<AmbushAbilitySO>(TestAssets.AbilitiesDir + "/Ambush.asset");
            Assert.LessOrEqual(CluckWars.Visuals.MarkPresentationModel.DrawDuration(false, m.ArmSeconds), m.ArmSeconds);
            Assert.Greater(m.WindowSeconds - m.ArmSeconds, ambush.StunDuration, "a mark shorter than one stun cannot be executed");
            Assert.AreEqual(m.WindowSeconds, AssassinExecute.WindowDuration, "the fallback (no asset registered) matches the asset");
        }

        [Test]
        public void TheMark_ImpliesNoRange()
        {
            var m = ScriptableObject.CreateInstance<MarkKillAbilitySO>();
            try
            {
                Assert.AreEqual(AbilityAimShape.None, m.AimShape);
                Assert.IsFalse(m.RequiresEnemyInRange, "the press is gated by IsUsable, a rival anywhere is enough");
                Assert.AreEqual(0f, m.IndicatorRange);
            }
            finally { UnityEngine.Object.DestroyImmediate(m); }
        }

        [Test]
        public void TheReceiverBounds_FollowTheTunedNumbers()
        {
            var claim = new ExecuteRules.RemovalClaim
            {
                Duration = ExecuteRules.RemovalDurationSeconds, AttackerResolved = true, SenderSpeaksForAttacker = true,
                AttackerHasExecute = true, MarkedThisVictim = true, VictimStunned = true,
                PlanarDistance = 12f, KillRange = 12f,
            };
            Assert.AreEqual(ExecuteRules.RemovalVerdict.Accepted, ExecuteRules.EvaluateRemoval(claim));

            claim.KillRange = 8f;
            Assert.AreEqual(ExecuteRules.RemovalVerdict.OutOfRange, ExecuteRules.EvaluateRemoval(claim));
        }

        private static string Execute => File.ReadAllText("Assets/_Game/Scripts/Gameplay/AssassinExecute.cs").Replace("\r\n", "\n");

        [Test]
        public void NothingBreaksAMark_ButExpiryAKillOrALostTarget_SourceGuard()
        {
            var src = Execute;
            int fun = src.IndexOf("public override void FixedUpdateNetwork()", StringComparison.Ordinal);
            string body = src.Substring(fun, src.IndexOf("\n        }", fun, StringComparison.Ordinal) - fun);
            Assert.AreEqual(2, CountOf(body, "FizzleMark()"), "lost target and expiry, nothing else");
            StringAssert.DoesNotContain("KillRange", body);
            StringAssert.DoesNotContain("Isolated", src);
            StringAssert.DoesNotContain("ColdTrail", src);
        }

        [Test]
        public void TheMarkPress_FreesTheSlot_AndTheRoundResetClearsTheHistory_SourceGuard()
        {
            var src = Execute;
            int mark = src.IndexOf("MarkedTarget = chosen.Id", StringComparison.Ordinal);
            Assert.Greater(src.IndexOf("TriggerCooldown(SlotIndex, 0f)", mark, StringComparison.Ordinal), mark);
            int reset = src.IndexOf("public void ResetForNewRound()", StringComparison.Ordinal);
            StringAssert.Contains("_hasLastMark = false", src.Substring(reset, src.IndexOf("\n        }", reset, StringComparison.Ordinal) - reset));
        }

        [TestCase(false, false, false, false, BotMarkStep.None)]
        [TestCase(true, false, false, true, BotMarkStep.Hunt)]
        [TestCase(true, false, true, false, BotMarkStep.Hunt)]
        [TestCase(true, false, true, true, BotMarkStep.Stun)]
        [TestCase(true, true, true, true, BotMarkStep.Kill)]
        public void NextMarkStep_HuntsThenStunsThenKills(bool marked, bool canKill, bool armed, bool stunInReach, BotMarkStep expected) =>
            Assert.AreEqual(expected, BotTactics.NextMarkStep(marked, canKill, armed, stunInReach));

        private static int CountOf(string text, string what)
        {
            int n = 0;
            for (int i = text.IndexOf(what, StringComparison.Ordinal); i >= 0; i = text.IndexOf(what, i + 1, StringComparison.Ordinal)) n++;
            return n;
        }
    }
}

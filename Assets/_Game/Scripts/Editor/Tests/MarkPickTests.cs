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

        [Test]
        public void ShippedMarkKill_CarriesTheBaselineNumbers()
        {
            var m = TestAssets.Load<MarkKillAbilitySO>(TestAssets.AbilitiesDir + "/MarkKill.asset");
            Assert.AreEqual(8f, m.KillRange);
            Assert.AreEqual(0f, m.ArmSeconds);
            Assert.AreEqual(12f, m.WindowSeconds);
            Assert.AreEqual(5f, m.FailCooldownSeconds);
            Assert.AreEqual(15f, m.SuccessCooldownSeconds);
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
        public void NothingBreaksAMark_ButExpiryAKillOrALostTarget()
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
        public void TheMarkPress_FreesTheSlot_AndTheRoundResetClearsTheHistory()
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

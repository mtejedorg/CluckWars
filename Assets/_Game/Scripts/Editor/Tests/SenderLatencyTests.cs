using System.Linq;
using CluckWars.Gameplay;
using NUnit.Framework;
using UnityEngine;

namespace CluckWars.Tests
{
    /// <summary>
    /// Playtest 2026-11-13 follow-up: <c>Runner.GetPlayerRtt</c> reads 0 for a remote player in Shared Mode, so a range
    /// margin built on the raw value is no margin. The peck was fixed in <c>fc0cd38f</c>; the steal and the deposit
    /// carried the same pattern. Every receiver now takes its latency from <c>ChickenCombat.SenderLatency</c>
    /// (0 for a local invoke, else floored at <see cref="ExecuteRules.MinRemoteLatencySeconds"/> and capped).
    /// </summary>
    public sealed class SenderLatencyTests
    {
        private const float SpeedyWalk = 12.15f;
        private const float WarriorWalk = 9.45f;
        private const float Reach = 5.16f;          // a steal reach of the shipped order of size
        private const float DepositRadius = 2.5f;

        private static float Floored => ExecuteRules.RemoteLatency(0f);

        [Test]
        public void TheFloor_IsWhatAZeroRttBecomes()
        {
            Assert.AreEqual(ExecuteRules.MinRemoteLatencySeconds, Floored, 0f);
            Assert.Greater(Floored, 0f);
        }

        // ---- steal (ChickenCargo.TryResolveThief) ---------------------------------------------------------------

        [Test]
        public void ARemoteSteal_FromASenderWhoseRttReadsZero_LandsAtTheEdgeOfReach()
        {
            float drift = SpeedyWalk + WarriorWalk;
            var victim = new Vector3(3f, 0.95f, -4f);
            // The thief passed its own scan at the reach; the victim's authority still sees it a little further out.
            foreach (float beyond in new[] { 0.05f, 0.5f, drift * Floored - 0.01f })
            {
                var thief = victim + new Vector3(Reach + beyond, 0f, 0f);
                Assert.IsFalse(StealRules.IsWithinStealRange(thief, victim, Reach, StealRules.RangeMargin(drift, 0f)),
                    $"+{beyond}: with the raw zero RTT there was no margin (the defect)");
                Assert.IsTrue(StealRules.IsWithinStealRange(thief, victim, Reach, StealRules.RangeMargin(drift, Floored)),
                    $"+{beyond}: the floored latency gives the drift margin");
            }
        }

        [TestCase(0f)]
        [TestCase(0.116f)]
        [TestCase(60f)]
        [TestCase(float.NaN)]
        public void AStealFromAcrossTheArena_IsRefused_WhateverTheSendersRtt(float measuredRtt)
        {
            float drift = SpeedyWalk + WarriorWalk;
            float margin = StealRules.RangeMargin(drift, ExecuteRules.RemoteLatency(measuredRtt));
            var victim = Vector3.zero;
            Assert.IsFalse(StealRules.IsWithinStealRange(new Vector3(30f, 0f, 0f), victim, Reach, margin), "across the arena");
            Assert.IsFalse(StealRules.IsWithinStealRange(
                new Vector3(Reach + drift * ExecuteRules.MaxLatencySeconds + 0.01f, 0f, 0f), victim, Reach, margin),
                "just past the largest allowance there is");
        }

        [Test]
        public void ALocalSteal_HasNoAllowance()
        {
            var victim = Vector3.zero;
            float margin = StealRules.RangeMargin(SpeedyWalk + WarriorWalk, 0f);
            Assert.IsTrue(StealRules.IsWithinStealRange(new Vector3(Reach, 0f, 0f), victim, Reach, margin));
            Assert.IsFalse(StealRules.IsWithinStealRange(new Vector3(Reach + 0.05f, 0f, 0f), victim, Reach, margin));
        }

        // ---- deposit (PlayerBase.TryResolveDepositor) -----------------------------------------------------------

        [Test]
        public void ARemoteDeposit_FromASenderWhoseRttReadsZero_IsBankedAtTheEdgeOfTheRadius()
        {
            var basePos = new Vector3(21.8f, 0f, -21.8f);
            float withFloor = BaseDepositRules.RangeMargin(SpeedyWalk, Floored);
            float raw = BaseDepositRules.RangeMargin(SpeedyWalk, 0f);
            Assert.AreEqual(SpeedyWalk * Floored, withFloor - raw, 1e-4f, "the floor adds exactly the drift over the minimum latency");

            // Just past what the flush window alone allows: refused on the raw zero, banked with the floor.
            var chicken = basePos + new Vector3(DepositRadius + raw + 0.5f, 0.95f, 0f);
            Assert.IsFalse(BaseDepositRules.IsWithinDepositRange(basePos, chicken, DepositRadius, raw), "the defect");
            Assert.IsTrue(BaseDepositRules.IsWithinDepositRange(basePos, chicken, DepositRadius, withFloor));
        }

        [TestCase(0f)]
        [TestCase(0.116f)]
        [TestCase(60f)]
        [TestCase(float.NaN)]
        public void ADepositFromAcrossTheArena_IsRefused_WhateverTheSendersRtt(float measuredRtt)
        {
            var basePos = Vector3.zero;
            float margin = BaseDepositRules.RangeMargin(SpeedyWalk, ExecuteRules.RemoteLatency(measuredRtt));
            Assert.IsFalse(BaseDepositRules.IsWithinDepositRange(basePos, new Vector3(30f, 0f, 0f), DepositRadius, margin));
            float widest = BaseDepositRules.RangeMargin(SpeedyWalk, ExecuteRules.MaxLatencySeconds);
            Assert.IsFalse(BaseDepositRules.IsWithinDepositRange(basePos, new Vector3(DepositRadius + widest + 0.01f, 0f, 0f), DepositRadius, margin),
                "just past the largest allowance there is");
        }

        // ---- wiring -------------------------------------------------------------------------------------------

        [TestCase("Assets/_Game/Scripts/Gameplay/ChickenCargo.cs", "private bool TryResolveThief(")]
        [TestCase("Assets/_Game/Scripts/Gameplay/PlayerBase.cs", "private bool TryResolveDepositor(")]
        [TestCase("Assets/_Game/Scripts/Gameplay/PlayerBase.cs", "private string DescribeRejection(")]
        [TestCase("Assets/_Game/Scripts/Gameplay/FoodPile.cs", "public void RPC_Drain(")]
        public void TheReceiver_TakesTheFlooredSenderLatency(string path, string method)
        {
            string body = AssassinExecuteCreditTests.MethodBody(path, method);
            StringAssert.Contains("ChickenCombat.SenderLatency(Runner, info)", body);
            StringAssert.DoesNotContain("GetPlayerRtt", body);
        }

        [Test]
        public void OnlyOnePlaceInGameplay_ReadsTheRawRtt()
        {
            var readers = RuntimeSourceGuardTests.RuntimeFiles()
                .Where(p => p.Contains("/Gameplay/") || p.Contains("/Abilities/") || p.Contains("/Networking/"))
                .Where(p => RuntimeSourceGuardTests.Code(p).Contains("GetPlayerRtt"))
                .ToList();
            CollectionAssert.AreEquivalent(new[] { "Assets/_Game/Scripts/Gameplay/ChickenCombat.cs" }, readers,
                "Runner.GetPlayerRtt reads 0 for a remote player in Shared Mode. Use ChickenCombat.SenderLatency / PeerLatency.");
            string combat = RuntimeSourceGuardTests.Code("Assets/_Game/Scripts/Gameplay/ChickenCombat.cs");
            Assert.AreEqual(1, System.Text.RegularExpressions.Regex.Matches(combat, "GetPlayerRtt").Count, "inside PeerLatency only");
            StringAssert.Contains("ExecuteRules.RemoteLatency((float)runner.GetPlayerRtt(peer))", combat);
        }
    }
}

using NUnit.Framework;
using UnityEngine;
using CluckWars.Balance;

namespace CluckWars.Tests
{
    /// <summary>
    /// EditMode tests for the Balance Oracle (spec: docs/superpowers/specs/
    /// 2026-07-24-cluck-wars-core-redesign-design.md §7). Pure-C# simulation, so every
    /// case here is hand-computable: travel = distance / speed, collect = units / rate,
    /// deposit = units / depositRate.
    /// </summary>
    public sealed class BalanceOracleTests
    {
        // One pile that is exactly one full load, one round trip.
        // base(0,0) -> pile(10,0) food 10; speed 10, cap 10, rate 10, deposit 10, W 10.
        // out 1s + collect 1s + back 1s + deposit 1s = 4.0s, 1 trip.
        [Test]
        public void Simulate_SingleFullLoad_OneTrip()
        {
            var map = new OracleMap
            {
                BasePos = new Vector2(0f, 0f),
                Piles = new[] { new OraclePile { Pos = new Vector2(10f, 0f), Food = 10f } }
            };
            var chicken = new OracleChicken
            {
                MoveSpeed = 10f, CargoCapacity = 10, CollectionRate = 10f, DepositRate = 10f
            };

            var result = BalanceOracle.Simulate(map, chicken, winTarget: 10f);

            Assert.IsTrue(result.ReachedTarget, "Should bank the target.");
            Assert.AreEqual(4.0f, result.SctSeconds, 0.01f);
            Assert.AreEqual(1, result.Trips);
        }

        // One 20-food pile, capacity 10 -> two identical round trips.
        // base(0,0) -> pile(10,0) food 20; speed 10, cap 10, rate 10, deposit 10, W 20.
        // trip1 4.0s (bank 10) + trip2 4.0s (bank 20) = 8.0s, 2 trips.
        [Test]
        public void Simulate_CapacityForcesTwoTrips()
        {
            var map = new OracleMap
            {
                BasePos = new Vector2(0f, 0f),
                Piles = new[] { new OraclePile { Pos = new Vector2(10f, 0f), Food = 20f } }
            };
            var chicken = new OracleChicken
            {
                MoveSpeed = 10f, CargoCapacity = 10, CollectionRate = 10f, DepositRate = 10f
            };

            var result = BalanceOracle.Simulate(map, chicken, winTarget: 20f);

            Assert.IsTrue(result.ReachedTarget);
            Assert.AreEqual(8.0f, result.SctSeconds, 0.01f);
            Assert.AreEqual(2, result.Trips);
        }

        // Two piles; the near one must be farmed first. Proves greedy-nearest ordering.
        // base(0,0); A(5,0) food 5; B(20,0) food 5; speed 5, cap 10, rate 5, deposit 5, W 10.
        // ->A 1s, collect 1s (carry 5, not full); ->B dist15 =3s, collect 1s (carry 10 full);
        // back from (20,0) 4s; deposit 10 -> 2s. total 12.0s, 1 trip.
        [Test]
        public void Simulate_GreedyPicksNearestPileFirst()
        {
            var map = new OracleMap
            {
                BasePos = new Vector2(0f, 0f),
                Piles = new[]
                {
                    new OraclePile { Pos = new Vector2(5f, 0f),  Food = 5f },
                    new OraclePile { Pos = new Vector2(20f, 0f), Food = 5f }
                }
            };
            var chicken = new OracleChicken
            {
                MoveSpeed = 5f, CargoCapacity = 10, CollectionRate = 5f, DepositRate = 5f
            };

            var result = BalanceOracle.Simulate(map, chicken, winTarget: 10f);

            Assert.IsTrue(result.ReachedTarget);
            Assert.AreEqual(12.0f, result.SctSeconds, 0.01f);
            Assert.AreEqual(1, result.Trips);
        }

        // Carrying 15 but only 10 needed to win — SCT must count deposit time for 10, not 15.
        // base(0,0) -> pile(10,0) food 15; speed 10, cap 15, rate 10, deposit 10, W 10.
        // out 1s + collect 15 (1.5s) + back 1s + deposit 10-of-15 (1.0s) = 4.5s, 1 trip.
        [Test]
        public void Simulate_WinCrossesMidDeposit_CountsOnlyNeeded()
        {
            var map = new OracleMap
            {
                BasePos = new Vector2(0f, 0f),
                Piles = new[] { new OraclePile { Pos = new Vector2(10f, 0f), Food = 15f } }
            };
            var chicken = new OracleChicken
            {
                MoveSpeed = 10f, CargoCapacity = 15, CollectionRate = 10f, DepositRate = 10f
            };

            var result = BalanceOracle.Simulate(map, chicken, winTarget: 10f);

            Assert.IsTrue(result.ReachedTarget);
            Assert.AreEqual(4.5f, result.SctSeconds, 0.01f);
            Assert.AreEqual(1, result.Trips);
        }

        // Map holds less food than the win target — the chicken can never bank W.
        // base(0,0) -> pile(10,0) food 5; W 10. Collects 5, deposits 5, then no food & empty.
        [Test]
        public void Simulate_NotEnoughFood_ReportsUnreachable()
        {
            var map = new OracleMap
            {
                BasePos = new Vector2(0f, 0f),
                Piles = new[] { new OraclePile { Pos = new Vector2(10f, 0f), Food = 5f } }
            };
            var chicken = new OracleChicken
            {
                MoveSpeed = 10f, CargoCapacity = 10, CollectionRate = 10f, DepositRate = 10f
            };

            var result = BalanceOracle.Simulate(map, chicken, winTarget: 10f);

            Assert.IsFalse(result.ReachedTarget, "Target exceeds total map food; must be unreachable.");
        }

        // --- Task 6: SCT target goalposts (spec §1) ------------------------------

        // A self-consistent PROVISIONAL fixture: one base at origin, nine piles laid on a
        // line so distances are hand-checkable. Total food = 40 (= W). Replace with the
        // shipped map coordinates once the map plan lands.
        private static OracleMap CanonicalFixture()
        {
            return new OracleMap
            {
                BasePos = new Vector2(0f, 0f),
                Piles = new[]
                {
                    new OraclePile { Pos = new Vector2(4f, 0f),  Food = 5f },  // T1 doorstep
                    new OraclePile { Pos = new Vector2(8f, 0f),  Food = 10f }, // T2
                    new OraclePile { Pos = new Vector2(12f, 0f), Food = 10f }, // T2
                    new OraclePile { Pos = new Vector2(16f, 0f), Food = 15f }, // T3-ish
                }
            };
        }

        [Test]
        public void SctTargets_TableMatchesSpec()
        {
            Assert.AreEqual(4, SctTargets.All.Length);
            var speedy = System.Array.Find(SctTargets.All, t => t.ClassName == "Speedy");
            Assert.AreEqual(30f, speedy.Seconds, 0.001f);
            Assert.AreEqual(4, speedy.Trips);
            var fatty = System.Array.Find(SctTargets.All, t => t.ClassName == "Fatty");
            Assert.AreEqual(30f, fatty.Seconds, 0.001f);
            Assert.AreEqual(2, fatty.Trips);
        }

        [Test]
        public void Within_AcceptsInsideTolerance_RejectsOutside()
        {
            var target = new SctTarget { ClassName = "X", Seconds = 30f, Trips = 4 };
            Assert.IsTrue(SctTargets.Within(
                new OracleResult { SctSeconds = 31.2f, Trips = 4, ReachedTarget = true }, target));
            Assert.IsFalse(SctTargets.Within(
                new OracleResult { SctSeconds = 31.2f, Trips = 3, ReachedTarget = true }, target),
                "Wrong trip count must fail even if seconds are inside tolerance.");
            Assert.IsFalse(SctTargets.Within(
                new OracleResult { SctSeconds = 33f, Trips = 4, ReachedTarget = true }, target),
                "Outside the ±1.5s window must fail.");
            Assert.IsFalse(SctTargets.Within(
                new OracleResult { SctSeconds = 30f, Trips = 4, ReachedTarget = false }, target),
                "Not reaching the target must fail regardless of seconds.");
        }

        // Demonstrates solving: candidate stats tuned to hit a 30s / 2-trip target on the
        // provisional fixture (W=40, cap 20 -> ceil(40/20)=2 trips). Proves the harness end to end.
        [Test]
        public void CanonicalFixture_CandidateStatsHitTarget()
        {
            var map = CanonicalFixture();
            var chicken = new OracleChicken
            {
                MoveSpeed = 4.2f, CargoCapacity = 20, CollectionRate = 3.0f, DepositRate = 6.0f
            };

            var result = BalanceOracle.Simulate(map, chicken, winTarget: 40f);

            Assert.IsTrue(result.ReachedTarget);
            Assert.AreEqual(2, result.Trips, "cap 20 over W 40 must be exactly 2 trips.");
        }
    }
}

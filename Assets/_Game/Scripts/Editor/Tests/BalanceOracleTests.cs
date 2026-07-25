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
        private static OracleMap Task8OracleMap()
        {
            return new OracleMap
            {
                BasePos = new Vector2(11f, 11f),
                Piles = new[]
                {
                    new OraclePile { Pos = new Vector2(8.93f, 8.37f),   Food = 5f },  // Personal0
                    new OraclePile { Pos = new Vector2(8.68f, -0.26f),  Food = 10f }, // Contested3
                    new OraclePile { Pos = new Vector2(0.00f, 0.00f),   Food = 20f }, // Center
                    new OraclePile { Pos = new Vector2(-1.05f, 9.88f),  Food = 10f }, // Contested0
                    new OraclePile { Pos = new Vector2(7.80f, -8.54f),  Food = 5f },  // Personal3
                    new OraclePile { Pos = new Vector2(-8.14f, 8.76f),  Food = 5f },  // Personal1
                    new OraclePile { Pos = new Vector2(-8.94f, 0.43f),  Food = 10f }, // Contested1
                    new OraclePile { Pos = new Vector2(-7.77f, -7.45f), Food = 5f },  // Personal2
                    new OraclePile { Pos = new Vector2(0.73f, -10.08f), Food = 10f }  // Contested2
                }
            };
        }

        [Test]
        public void Task8_RealMap_StatResolvePassesAllClasses()
        {
            var map = Task8OracleMap();

            // Pinned Capacities: Speedy 10, Fatty 35, Warrior 14, Assassin 10
            var speedy = new OracleChicken { MoveSpeed = 9.0f, CargoCapacity = 10, CollectionRate = 3.00f, DepositRate = 9.0f };
            var fatty   = new OracleChicken { MoveSpeed = 7.5f, CargoCapacity = 35, CollectionRate = 3.20f, DepositRate = 9.0f };
            var warrior = new OracleChicken { MoveSpeed = 7.5f, CargoCapacity = 14, CollectionRate = 2.60f, DepositRate = 9.0f };
            var assassin= new OracleChicken { MoveSpeed = 9.0f, CargoCapacity = 10, CollectionRate = 1.70f, DepositRate = 9.0f };

            var speedyRes   = BalanceOracle.Simulate(map, speedy, 40f);
            var fattyRes    = BalanceOracle.Simulate(map, fatty, 40f);
            var warriorRes  = BalanceOracle.Simulate(map, warrior, 40f);
            var assassinRes = BalanceOracle.Simulate(map, assassin, 40f);

            var speedyTarget   = System.Array.Find(SctTargets.All, t => t.ClassName == "Speedy");
            var fattyTarget    = System.Array.Find(SctTargets.All, t => t.ClassName == "Fatty");
            var warriorTarget  = System.Array.Find(SctTargets.All, t => t.ClassName == "Warrior");
            var assassinTarget = System.Array.Find(SctTargets.All, t => t.ClassName == "Assassin");

            Assert.IsTrue(SctTargets.Within(speedyRes, speedyTarget),     $"Speedy failed: {speedyRes.SctSeconds:0.1}s, {speedyRes.Trips} trips");
            Assert.IsTrue(SctTargets.Within(fattyRes, fattyTarget),       $"Fatty failed: {fattyRes.SctSeconds:0.1}s, {fattyRes.Trips} trips");
            Assert.IsTrue(SctTargets.Within(warriorRes, warriorTarget),   $"Warrior failed: {warriorRes.SctSeconds:0.1}s, {warriorRes.Trips} trips");
            Assert.IsTrue(SctTargets.Within(assassinRes, assassinTarget), $"Assassin failed: {assassinRes.SctSeconds:0.1}s, {assassinRes.Trips} trips");
        }

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

        private static OracleMap CanonicalFixture()
        {
            return new OracleMap
            {
                BasePos = new Vector2(0f, 0f),
                Piles = new[]
                {
                    new OraclePile { Pos = new Vector2(4f, 0f),  Food = 5f },
                    new OraclePile { Pos = new Vector2(8f, 0f),  Food = 10f },
                    new OraclePile { Pos = new Vector2(12f, 0f), Food = 10f },
                    new OraclePile { Pos = new Vector2(16f, 0f), Food = 15f },
                }
            };
        }
    }
}

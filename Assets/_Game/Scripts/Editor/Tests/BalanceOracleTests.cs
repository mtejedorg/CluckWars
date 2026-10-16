using NUnit.Framework;
using UnityEngine;
using CluckWars.Balance;
using CluckWars.Gameplay;

namespace CluckWars.Tests
{
    /// <summary>
    /// EditMode tests for the Balance Oracle (spec: docs/superpowers/specs/
    /// 2026-07-24-cluck-wars-core-redesign-design.md §7). Pure-C# simulation, so every
    /// case here is hand-computable: travel = walkable leg / speed (the centre distance minus the
    /// base deposit radius and the pile's oval radius + peck reach, floored at 0),
    /// collect = ceil(food / PeckAmount) * PeckCooldown, deposit = units / depositRate.
    /// <para>
    /// The collect term used to be <c>units / CollectionRate</c>. It changed when food
    /// stopped draining automatically and started coming out one Peck press at a time
    /// (spec: 2026-08-13-peck-foraging-and-four-slot-loadout-design.md §7.1).
    /// </para>
    /// </summary>
    public sealed class BalanceOracleTests
    {
        /// <summary>
        /// The SCT axiom, asserted against the <b>shipped</b> <see cref="ChickenStatsSO"/> and
        /// <see cref="MatchConfigSO"/> assets rather than against literals, on the shipped map
        /// (<see cref="ShippedMap.OracleMap"/>) from every one of the four seats. The map is
        /// reach-aware (Option B): it carries the prefab deposit radius, peck reach and oval
        /// footprints, so a change to arena size, base distance, pile footprints, pile food,
        /// deposit radius or peck reach moves these numbers and fails here.
        /// </summary>
        /// <remarks>
        /// ⚠️ <b>This test previously hardcoded its inputs</b> — four <c>OracleChicken</c>
        /// literals carrying the design values — which made it assert "the spec satisfies the
        /// spec" and left it structurally blind to the assets that actually ship. Move speeds
        /// drifted to 9/9/10.5/10.5 against a solved 7.5/7.5/9.0/9.0 (Fatty −1.8 s and Warrior
        /// −1.9 s, both outside tolerance) and this test stayed green throughout, because
        /// nothing here ever opened a <c>.asset</c> file. Found 2026-08-13 only because a
        /// playtest reported "chickens move too fast".
        ///
        /// <b>Never reintroduce literal stats here.</b> A test that restates its inputs cannot
        /// detect drift in the thing it is supposed to be guarding.
        /// </remarks>
        [Test]
        public void RealMap_ShippedClassAssets_SatisfyTheSctAxiom()
        {
            var config = TestAssets.Load<MatchConfigSO>(TestAssets.MatchConfigPath);
            var stats = TestAssets.LoadAllIn<ChickenStatsSO>(TestAssets.ClassesDir);

            // Every class must be either governed by a target or explicitly excused. A class
            // in neither list has no balance guard at all, which is exactly how the Assassin
            // would have slipped through when Peck stopped applying to it.
            foreach (var s in stats)
            {
                bool governed = System.Array.Exists(SctTargets.All, x => x.ClassName == s.DisplayName);
                Assert.IsTrue(governed || SctTargets.IsExempt(s.DisplayName),
                    $"{s.DisplayName} is in neither SctTargets.All nor SctTargets.Exempt, so nothing " +
                    "checks its stats. Add a target, or excuse it and say why in Exempt's doc comment.");
            }

            foreach (var target in SctTargets.All)
            {
                var s = stats.Find(x => x.DisplayName == target.ClassName);
                Assert.IsNotNull(s,
                    $"No ChickenStatsSO with DisplayName '{target.ClassName}'. SctTargets and the class " +
                    "assets are keyed by DisplayName — a rename on either side must be made on both.");

                var chicken = new OracleChicken
                {
                    MoveSpeed     = s.MoveSpeed,
                    CargoCapacity = s.CargoCapacity,
                    PeckAmount    = s.PeckAmount,
                    PeckCooldown  = s.PeckCooldown,
                    DepositRate   = config.DepositRatePerSecond,
                };

                for (int seat = 0; seat < 4; seat++)
                {
                    var result = BalanceOracle.Simulate(ShippedMap.OracleMap(seat), chicken, config.FoodTargetToWin);

                    TestContext.WriteLine($"SCT {target.ClassName} seat {seat}: {result.SctSeconds:0.00}s / {result.Trips} trips (target {target.Seconds}s / {target.Trips})");
                    Assert.IsTrue(SctTargets.Within(result, target, SctTargets.ShippedAssetToleranceSeconds),
                        $"{target.ClassName} (seat {seat}) misses its SCT target: got {result.SctSeconds:0.0}s / {result.Trips} trips, " +
                        $"want {target.Seconds}s / {target.Trips} trips (±{SctTargets.ShippedAssetToleranceSeconds}s shipped-asset band). " +
                        $"Shipped stats: move {s.MoveSpeed}, cap {s.CargoCapacity}, " +
                        $"peck {s.PeckAmount} per {s.PeckCooldown}s, " +
                        $"deposit {config.DepositRatePerSecond}, W {config.FoodTargetToWin}. " +
                        "Stats are solved AGAINST this axiom — re-solve them, do not relax the target.");
                }
            }
        }

        /// <summary>
        /// The map the axiom is solved on must be the map the game builds: nine piles carrying
        /// exactly twice the win target (80 of 40), the figure every pile amount in
        /// <c>Game.unity</c> is tuned to. A change to a pile amount without re-solving the
        /// classes (or to the win target without re-tuning the piles) lands here first.
        /// </summary>
        [Test]
        public void ShippedMap_TotalFood_IsTwiceTheWinTarget()
        {
            var config = TestAssets.Load<MatchConfigSO>(TestAssets.MatchConfigPath);
            float total = 0f;
            foreach (var p in ShippedMap.OracleMap(0).Piles) total += p.Food;
            Assert.AreEqual(2f * config.FoodTargetToWin, total, 0.01f,
                $"Game.unity authors {total} food but the win target is {config.FoodTargetToWin} (map should hold 2x).");
        }

        // ---- reach-aware legs (hand-computed) -----------------------------------------------

        private static OracleChicken TenEverything() => new OracleChicken
        {
            MoveSpeed = 1f, CargoCapacity = 10, PeckAmount = 10f, PeckCooldown = 0.001f, DepositRate = 1000f
        };

        /// <summary>Time of a single base -> pile -> base run with ~free pecking/deposit = 2 legs at speed 1.</summary>
        private static float RoundTrip(OracleMap map)
        {
            var r = BalanceOracle.Simulate(map, TenEverything(), winTarget: 10f);
            Assert.IsTrue(r.ReachedTarget);
            return r.SctSeconds - 0.001f - 10f / 1000f; // minus one peck cooldown and the deposit
        }

        [Test]
        public void Leg_SubtractsBaseRadius_PileRadius_AndReach()
        {
            // centre distance 10, base radius 2.5, 2x2 pile -> oval radius 1, reach 1  =>  10 - 2.5 - 2 = 5.5 each way
            var map = new OracleMap
            {
                BasePos = Vector2.zero, BaseRadius = 2.5f, CollectReach = 1f,
                Piles = new[] { new OraclePile { Pos = new Vector2(10f, 0f), Food = 10f, Footprint = new Vector2(2f, 2f) } },
            };
            Assert.AreEqual(2f * 5.5f, RoundTrip(map), 0.01f);
        }

        [Test]
        public void Leg_UsesTheOvalRadiusTowardTheOtherEnd()
        {
            // 4x2 oval, base on its +X side. Yaw 0: long axis points at the base -> radius 2. Yaw 90: short axis -> radius 1.
            OracleMap Map(float yaw) => new OracleMap
            {
                BasePos = Vector2.zero,
                Piles = new[] { new OraclePile { Pos = new Vector2(10f, 0f), Food = 10f, Footprint = new Vector2(4f, 2f), Yaw = yaw } },
            };
            Assert.AreEqual(2f * (10f - 2f), RoundTrip(Map(0f)), 0.01f, "long axis toward the base");
            Assert.AreEqual(2f * (10f - 1f), RoundTrip(Map(90f)), 0.01f, "short axis toward the base");
        }

        [Test]
        public void Leg_NeverGoesNegative_WhenTheRadiiOverlap()
        {
            var map = new OracleMap
            {
                BasePos = Vector2.zero, BaseRadius = 2.5f, CollectReach = 1f,
                Piles = new[] { new OraclePile { Pos = new Vector2(3f, 0f), Food = 10f, Footprint = new Vector2(2f, 2f) } },
            };
            Assert.AreEqual(0f, RoundTrip(map), 0.01f);
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
                MoveSpeed = 10f, CargoCapacity = 10, PeckAmount = 10f, PeckCooldown = 1f, DepositRate = 10f
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
                MoveSpeed = 10f, CargoCapacity = 10, PeckAmount = 10f, PeckCooldown = 1f, DepositRate = 10f
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
                MoveSpeed = 5f, CargoCapacity = 10, PeckAmount = 5f, PeckCooldown = 1f, DepositRate = 5f
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
                MoveSpeed = 10f, CargoCapacity = 15, PeckAmount = 15f, PeckCooldown = 1.5f, DepositRate = 10f
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
                MoveSpeed = 10f, CargoCapacity = 10, PeckAmount = 10f, PeckCooldown = 1f, DepositRate = 10f
            };

            var result = BalanceOracle.Simulate(map, chicken, winTarget: 10f);

            Assert.IsFalse(result.ReachedTarget, "Target exceeds total map food; must be unreachable.");
        }

        [Test]
        public void Simulate_PartialPeck_StillCostsAFullCooldown()
        {
            // THE defining difference from the old continuous model, and the reason the
            // Oracle had to be reworked at all: taking 5 food at 3-per-press is TWO presses,
            // not 5/3 of one. You pay for six food's worth of time and walk away with five.
            var map = new OracleMap
            {
                BasePos = new Vector2(0f, 0f),
                Piles = new[] { new OraclePile { Pos = new Vector2(10f, 0f), Food = 5f } }
            };
            var chicken = new OracleChicken
            {
                MoveSpeed = 10f, CargoCapacity = 10, PeckAmount = 3f, PeckCooldown = 1f, DepositRate = 10f
            };

            var result = BalanceOracle.Simulate(map, chicken, winTarget: 5f);

            // travel 1.0 + ceil(5/3) = 2 presses x 1.0 + return 1.0 + deposit 5/10 = 0.5
            Assert.IsTrue(result.ReachedTarget);
            Assert.AreEqual(4.5f, result.SctSeconds, 0.01f,
                "A continuous model would have charged 5/3 = 1.67s of collection and reported " +
                "4.17s. If this reads 4.17 the quantisation was lost.");
            Assert.AreEqual(1, result.Trips);
        }

        [Test]
        public void Simulate_ExactMultiple_DoesNotChargeAnExtraPeck()
        {
            // Guards the epsilon inside the ceiling. 6 food at 3-per-press is exactly two
            // presses; without the epsilon a float landing a hair above 2.0 charges a third,
            // which would quietly inflate every class's SCT by a cooldown per pile.
            var map = new OracleMap
            {
                BasePos = new Vector2(0f, 0f),
                Piles = new[] { new OraclePile { Pos = new Vector2(10f, 0f), Food = 6f } }
            };
            var chicken = new OracleChicken
            {
                MoveSpeed = 10f, CargoCapacity = 10, PeckAmount = 3f, PeckCooldown = 1f, DepositRate = 10f
            };

            var result = BalanceOracle.Simulate(map, chicken, winTarget: 6f);

            // travel 1.0 + exactly 2 presses x 1.0 + return 1.0 + deposit 6/10 = 0.6
            Assert.IsTrue(result.ReachedTarget);
            Assert.AreEqual(4.6f, result.SctSeconds, 0.01f,
                "6 food at 3 per press must be 2 presses, not 3.");
        }

        [Test]
        public void SctTargets_TableMatchesSpec()
        {
            Assert.AreEqual(3, SctTargets.All.Length,
                "Three farming classes are governed by SCT; the Assassin is excused via SctTargets.Exempt.");
            Assert.IsTrue(SctTargets.IsExempt("Assassin"),
                "The Assassin cannot forage, so it must stay excused from the SCT axiom.");
            var speedy = System.Array.Find(SctTargets.All, t => t.ClassName == "Speedy");
            Assert.AreEqual(30f, speedy.Seconds, 0.001f);
            Assert.AreEqual(4, speedy.Trips);
            var fatty = System.Array.Find(SctTargets.All, t => t.ClassName == "Fatty");
            Assert.AreEqual(30f, fatty.Seconds, 0.001f);
            Assert.AreEqual(2, fatty.Trips);
        }

        [Test]
        public void ToleranceBands_ShippedGuardIsTighterThanDesignBand()
        {
            Assert.AreEqual(1.5f, SctTargets.DefaultToleranceSeconds, 0.0001f, "GDD design band.");
            Assert.AreEqual(0.5f, SctTargets.ShippedAssetToleranceSeconds, 0.0001f, "Shipped-asset drift guard.");
            Assert.Less(SctTargets.ShippedAssetToleranceSeconds, SctTargets.DefaultToleranceSeconds);
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
                MoveSpeed = 4.2f, CargoCapacity = 20, PeckAmount = 3f, PeckCooldown = 1f, DepositRate = 6.0f
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

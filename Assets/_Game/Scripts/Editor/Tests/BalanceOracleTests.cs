using NUnit.Framework;
using UnityEngine;
using CluckWars.Balance;
using CluckWars.Gameplay;

namespace CluckWars.Tests
{
    /// <summary>
    /// EditMode tests for the Balance Oracle (spec: docs/superpowers/specs/
    /// 2026-07-24-cluck-wars-core-redesign-design.md §7). Pure-C# simulation, so every
    /// case here is hand-computable: travel = distance / speed,
    /// collect = ceil(food / PeckAmount) * PeckCooldown, deposit = units / depositRate.
    /// <para>
    /// The collect term used to be <c>units / CollectionRate</c>. It changed when food
    /// stopped draining automatically and started coming out one Peck press at a time
    /// (spec: 2026-08-13-peck-foraging-and-four-slot-loadout-design.md §7.1).
    /// </para>
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

        /// <summary>
        /// The SCT axiom, asserted against the <b>shipped</b> <see cref="ChickenStatsSO"/> and
        /// <see cref="MatchConfigSO"/> assets rather than against literals.
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
            var map = Task8OracleMap();
            var config = TestAssets.Load<MatchConfigSO>(TestAssets.MatchConfigPath);
            var stats = TestAssets.LoadAllIn<ChickenStatsSO>(TestAssets.ClassesDir);

            Assert.AreEqual(SctTargets.All.Length, stats.Count,
                $"Expected one ChickenStatsSO per SCT target in {TestAssets.ClassesDir}, found {stats.Count}.");

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

                var result = BalanceOracle.Simulate(map, chicken, config.FoodTargetToWin);

                Assert.IsTrue(SctTargets.Within(result, target),
                    $"{target.ClassName} misses its SCT target: got {result.SctSeconds:0.0}s / {result.Trips} trips, " +
                    $"want {target.Seconds}s / {target.Trips} trips (±{SctTargets.DefaultToleranceSeconds}s). " +
                    $"Shipped stats: move {s.MoveSpeed}, cap {s.CargoCapacity}, " +
                    $"peck {s.PeckAmount} per {s.PeckCooldown}s, " +
                    $"deposit {config.DepositRatePerSecond}, W {config.FoodTargetToWin}. " +
                    "Stats are solved AGAINST this axiom — re-solve them, do not relax the target.");
            }
        }

        /// <summary>
        /// The Oracle fixture's pile layout must stay in step with <see cref="MapGenerator"/>'s
        /// authored food amounts, or the axiom is solved against a map that is not the one
        /// being played. Guards the other half of the drift surface: §<see
        /// cref="RealMap_ShippedClassAssets_SatisfyTheSctAxiom"/> pins the chickens, this pins
        /// the map they farm.
        /// </summary>
        /// <remarks>
        /// Reads the values out of <c>Game.unity</c>'s YAML rather than off a MapGenerator
        /// instance, for two reasons. MapGenerator's <c>Awake</c> builds the plane, the walls,
        /// the obstacles and a NavMesh, so instantiating one in an EditMode test is a heavy
        /// side effect. More importantly the scene <b>overrides</b> the C# field initializers,
        /// so reading the code defaults would assert against numbers the game never uses.
        /// </remarks>
        [Test]
        public void OracleFixture_PileFood_MatchesTheAuthoredMap()
        {
            float centre    = TestAssets.SceneFloat("_centerPileAmount");
            float personal  = TestAssets.SceneFloat("_personalPileAmount");
            float contested = TestAssets.SceneFloat("_contestedPileAmount");

            float expected = centre + 4f * personal + 4f * contested;
            float actual = 0f;
            foreach (var p in Task8OracleMap().Piles) actual += p.Food;

            Assert.AreEqual(expected, actual, 0.01f,
                $"The Oracle fixture holds {actual} food but Game.unity authors {expected} " +
                $"(centre {centre} + 4x personal {personal} + 4x contested {contested}). " +
                "Every SCT number is solved on this map — update Task8OracleMap to match, " +
                "then re-solve the class stats against the new supply.");
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

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
        /// <summary>
        /// The map every SCT number is solved against. Coordinates were rescaled x1.35 on
        /// 2026-08-14 in lockstep with the arena (<c>_planeSize</c> 38 -> 51.3).
        /// </summary>
        /// <remarks>
        /// <b>Scaling this fixture is what makes the arena rescale SCT-neutral, and it is not
        /// optional.</b> Every travel term in <see cref="BalanceOracle"/> is
        /// <c>distance / speed</c>, so scaling the map and every <c>MoveSpeed</c> by the same
        /// factor leaves all three classes' clear times bit-identical. Scaling only one side
        /// moves them by 3–5 s, far outside the ±1.5 s tolerance. Measured, both ways, before
        /// the change was made.
        /// <para>
        /// ⚠️ <b>Known pre-existing drift, deliberately NOT fixed here.</b> These absolute
        /// coordinates are smaller than the arena the game actually builds: the base sits at
        /// r = 21.0 against a real spawn at r = 30.8, and the doorstep piles at r = 16.5
        /// against a real r = 22.9. The fixture predates the current
        /// <c>_baseCornerDistance</c> and was never re-derived. Correcting it would lengthen
        /// every trip and force a full re-solve of all three classes' speeds — a balance
        /// change, not a rescale — so it is out of scope for the x1.35 pass and flagged for
        /// <c>mechanics-designer</c>. <see cref="OracleFixture_Scale_TracksTheAuthoredArena"/>
        /// pins the ratio so the two can no longer drift further apart silently.
        /// </para>
        /// </remarks>
        private static OracleMap Task8OracleMap()
        {
            return new OracleMap
            {
                BasePos = new Vector2(14.85f, 14.85f),
                Piles = new[]
                {
                    new OraclePile { Pos = new Vector2(12.0555f, 11.2995f),   Food = 5f },  // Personal0
                    new OraclePile { Pos = new Vector2(11.7180f, -0.3510f),   Food = 10f }, // Contested3
                    new OraclePile { Pos = new Vector2(0.0000f, 0.0000f),     Food = 20f }, // Center
                    new OraclePile { Pos = new Vector2(-1.4175f, 13.3380f),   Food = 10f }, // Contested0
                    new OraclePile { Pos = new Vector2(10.5300f, -11.5290f),  Food = 5f },  // Personal3
                    new OraclePile { Pos = new Vector2(-10.9890f, 11.8260f),  Food = 5f },  // Personal1
                    new OraclePile { Pos = new Vector2(-12.0690f, 0.5805f),   Food = 10f }, // Contested1
                    new OraclePile { Pos = new Vector2(-10.4895f, -10.0575f), Food = 5f },  // Personal2
                    new OraclePile { Pos = new Vector2(0.9855f, -13.6080f),   Food = 10f }  // Contested2
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

        /// <summary>
        /// The Oracle fixture's GEOMETRY must stay in step with the arena the game builds.
        /// Companion to <see cref="OracleFixture_PileFood_MatchesTheAuthoredMap"/>, which
        /// pins the fixture's food but says nothing about its distances — and distance is
        /// half of every travel term the SCT axiom is solved from.
        /// </summary>
        /// <remarks>
        /// Asserts a dimensionless RATIO rather than absolute coordinates, deliberately.
        /// The fixture's absolute positions are known to be smaller than the real map (see
        /// <see cref="Task8OracleMap"/>), so an absolute check could only be written by
        /// restating the fixture's own numbers — the exact self-referential shape that let
        /// the 2026-08-13 speed drift ship. The ratio is the real invariant: resize the
        /// arena in <c>Game.unity</c> without rescaling this fixture (or the reverse) and
        /// this goes red, which is precisely the mistake that would silently invalidate
        /// every solved MoveSpeed.
        /// </remarks>
        [Test]
        public void OracleFixture_Scale_TracksTheAuthoredArena()
        {
            // Pinned when the fixture and the arena were last rescaled together (x1.35,
            // 2026-08-14). Unchanged by that pass, because both sides moved.
            const float expectedRatio = 0.6442f;

            float arenaHalfSize = TestAssets.SceneFloat("_planeSize") * 0.5f;
            Assert.Greater(arenaHalfSize, 0f, "Game.unity authors a non-positive _planeSize.");

            float furthestPile = 0f;
            foreach (var p in Task8OracleMap().Piles)
                furthestPile = Mathf.Max(furthestPile, p.Pos.magnitude);

            float ratio = furthestPile / arenaHalfSize;

            Assert.AreEqual(expectedRatio, ratio, 0.01f,
                $"The Oracle fixture's outermost pile sits at r={furthestPile:0.00} in a " +
                $"{arenaHalfSize * 2f:0.0} m arena (ratio {ratio:0.0000}, expected {expectedRatio:0.0000}). " +
                "The arena and the fixture have been scaled by different amounts, so every class's " +
                "SCT is now solved against a map the game does not build. Rescale Task8OracleMap by " +
                "the same factor as _planeSize and re-solve the MoveSpeeds — do not edit this ratio " +
                "to make the test pass.");
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

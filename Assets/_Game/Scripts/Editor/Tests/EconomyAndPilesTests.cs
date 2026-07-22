using System.Linq;
using NUnit.Framework;
using UnityEngine;
using CluckWars.Gameplay;

namespace CluckWars.Tests
{
    /// <summary>
    /// Guards ADR 0003's pile maths and the match economy those piles feed.
    /// </summary>
    /// <remarks>
    /// Two halves:
    ///
    /// 1. <b>Pure footprint / drain arithmetic</b> via <see cref="FoodPileMath"/>.
    ///    The <c>FoodPile</c> NetworkBehaviour itself is untestable in EditMode
    ///    (<c>Amount</c> is <c>[Networked]</c> and throws when read before
    ///    <c>Spawned</c>), so the arithmetic lives in a static helper the behaviour
    ///    delegates to. These tests pin the invariants ADR 0003 Slice 1 depends on:
    ///    the top bucket is exactly the authored size, stepping is quantised, and a
    ///    permanent pile never reaches step 0.
    ///
    /// 2. <b>Authored tunables</b> read off the real <c>FoodPile.prefab</c> and
    ///    <c>MatchConfig.asset</c>, including the blocker-vs-CollectRadius margin
    ///    that caused the 2026-06-01 game-breaking "collection silently stopped
    ///    working" bug and is called out in FoodPile's own tooltip.
    ///
    /// NOT covered: the drain RPC round trip, cargo crediting, the optimistic-credit
    /// duplication window, and the permanent pile's regen tick — all need a runner.
    /// </remarks>
    public sealed class EconomyAndPilesTests
    {
        // Values documented in FoodPile._blockerRadius's tooltip and in docs/STATE.md.
        private const float ChickenCapsuleRadius = 0.5f;
        private const float CharacterControllerSkin = 0.08f;

        private const int   Steps        = 4;
        private const float MinScale     = 0.45f;
        private const float PermFloorFrac = 0.6f;

        // ---- Footprint stepping (ADR 0003 Decision 2) ---------------------------

        [Test]
        public void FootprintStep_FullOrdinaryPile_IsTheTopStep()
        {
            Assert.AreEqual(Steps, FoodPileMath.FootprintStep(30f, 30f, 0f, Steps));
        }

        [Test]
        public void FootprintStep_EmptyOrdinaryPile_IsZero_SoItStopsBlocking()
        {
            // Step 0 is what deactivates the blocker and un-carves the NavMesh —
            // "draining a pile permanently opens a lane" depends entirely on this.
            Assert.AreEqual(0, FoodPileMath.FootprintStep(0f, 30f, 0f, Steps));
        }

        [Test]
        public void FootprintStep_AnyNonZeroAmount_StillBlocks()
        {
            // A hair of food left must still be step >= 1. If a nearly-empty pile
            // rounded to 0 it would stop blocking while still being collectable,
            // and chickens would stand inside the mesh.
            Assert.AreEqual(1, FoodPileMath.FootprintStep(0.01f, 30f, 0f, Steps));
        }

        [Test]
        public void FootprintStep_IsMonotonic_AsThePileDrains()
        {
            // The ADR's density pillar: the map only ever opens up. A non-monotonic
            // step would make a pile grow back as it drained.
            int previous = FoodPileMath.FootprintStep(30f, 30f, 0f, Steps);
            for (float amount = 30f; amount >= 0f; amount -= 0.25f)
            {
                int step = FoodPileMath.FootprintStep(amount, 30f, 0f, Steps);
                Assert.LessOrEqual(step, previous,
                    $"Footprint step grew from {previous} to {step} while draining to {amount}/30.");
                previous = step;
            }
        }

        [Test]
        public void FootprintStep_IsQuantised_ToExactlyTheConfiguredBucketCount()
        {
            // The whole point of stepping is to limit NavMeshObstacle re-carves.
            // If this produced more distinct values than Steps, bot pathfinding
            // would thrash exactly as the ADR warns.
            var distinct = Enumerable.Range(0, 601)
                .Select(i => FoodPileMath.FootprintStep(i * 0.05f, 30f, 0f, Steps))
                .Distinct()
                .ToList();

            // Steps buckets plus the empty (0) bucket.
            Assert.AreEqual(Steps + 1, distinct.Count,
                $"Expected {Steps} non-empty buckets + empty, got [{string.Join(",", distinct.OrderBy(x => x))}].");
        }

        [Test]
        public void FootprintScale_TopStep_IsExactlyOne()
        {
            // Load-bearing: the blocker/CollectRadius margin is derived at the
            // AUTHORED size. If the top bucket scaled above 1 the blocker would grow
            // past what shipped and edge collection would silently break again.
            Assert.AreEqual(1f, FoodPileMath.FootprintScale(Steps, Steps, MinScale), 1e-5f);
        }

        [Test]
        public void FootprintScale_LowestStep_IsTheConfiguredMinimum()
        {
            Assert.AreEqual(MinScale, FoodPileMath.FootprintScale(1, Steps, MinScale), 1e-5f);
        }

        [Test]
        public void FootprintScale_IsStrictlyIncreasing_AcrossSteps()
        {
            for (int s = 1; s < Steps; s++)
            {
                Assert.Less(FoodPileMath.FootprintScale(s, Steps, MinScale),
                            FoodPileMath.FootprintScale(s + 1, Steps, MinScale),
                            $"Scale did not increase from step {s} to {s + 1}.");
            }
        }

        [Test]
        public void ClampSteps_GuardsAStaleSerializedZero()
        {
            // A prefab authored before _footprintSteps existed deserialises it as 0,
            // which would divide by (steps - 1) == -1 in FootprintScale.
            Assert.AreEqual(2, FoodPileMath.ClampSteps(0));
            Assert.AreEqual(2, FoodPileMath.ClampSteps(1));
            Assert.AreEqual(4, FoodPileMath.ClampSteps(4));
        }

        // ---- Permanent pile (ADR 0003 Decision 2b) ------------------------------

        [Test]
        public void PermanentPile_AtItsFloor_HasNothingCollectable()
        {
            // THE bug this guards: cargo credits optimistically against Available.
            // If Available were computed from Amount instead, the centre pile would
            // be an infinite food source parked at its floor.
            float floorFrac = FoodPileMath.FloorFraction(true, PermFloorFrac);
            float floor = FoodPileMath.DrainFloor(60f, floorFrac);

            Assert.AreEqual(36f, floor, 1e-4f, "60% floor of a 60-food centre pile is 36.");
            Assert.AreEqual(0f, FoodPileMath.Available(floor, 60f, floorFrac), 1e-4f);
            Assert.AreEqual(24f, FoodPileMath.Available(60f, 60f, floorFrac), 1e-4f);
        }

        [Test]
        public void PermanentPile_AtItsFloor_StillBlocks()
        {
            // "never empties, never stops blocking" — the late game always has one
            // contested arena. Step 0 would deactivate the blocker.
            float floorFrac = FoodPileMath.FloorFraction(true, PermFloorFrac);
            int step = FoodPileMath.FootprintStep(FoodPileMath.DrainFloor(60f, floorFrac), 60f, floorFrac, Steps);

            Assert.AreEqual(1, step,
                "A permanent pile sitting at its floor must be at the smallest non-empty step, " +
                "not step 0 — otherwise the centre stops being an obstacle.");
        }

        [Test]
        public void PermanentPile_SpansEveryStep_OverItsUsableRange()
        {
            // Its size is meant to read as contest intensity. Normalising over
            // [0, max] instead of [floor, max] would pin it to the top two buckets.
            float floorFrac = FoodPileMath.FloorFraction(true, PermFloorFrac);
            float floor = FoodPileMath.DrainFloor(60f, floorFrac);

            var seen = Enumerable.Range(0, 241)
                .Select(i => floor + (60f - floor) * i / 240f)
                .Select(a => FoodPileMath.FootprintStep(a, 60f, floorFrac, Steps))
                .Distinct()
                .OrderBy(x => x)
                .ToList();

            CollectionAssert.AreEqual(Enumerable.Range(1, Steps).ToList(), seen,
                "A permanent pile should pass through every non-empty step between its floor and max.");
        }

        [Test]
        public void OrdinaryPile_HasNoFloor()
        {
            float floorFrac = FoodPileMath.FloorFraction(false, PermFloorFrac);
            Assert.AreEqual(0f, floorFrac, 1e-6f, "Only permanent piles have a drain floor.");
            Assert.AreEqual(30f, FoodPileMath.Available(30f, 30f, floorFrac), 1e-4f);
        }

        // ---- Draining -----------------------------------------------------------

        [Test]
        public void Drain_OverRequest_ClampsToWhatIsAvailable()
        {
            Assert.AreEqual(0f, FoodPileMath.Drain(5f, 30f, 0f, 999f), 1e-4f);
        }

        [Test]
        public void Drain_NeverBreachesAPermanentPilesFloor()
        {
            float floorFrac = FoodPileMath.FloorFraction(true, PermFloorFrac);
            Assert.AreEqual(36f, FoodPileMath.Drain(60f, 60f, floorFrac, 999f), 1e-4f,
                "A permanent pile drained by more than it holds must settle exactly on its floor.");
            Assert.AreEqual(36f, FoodPileMath.Drain(36f, 60f, floorFrac, 10f), 1e-4f,
                "Draining a pile already at its floor must be a no-op.");
        }

        [Test]
        public void Drain_IgnoresNonPositiveRequests()
        {
            Assert.AreEqual(30f, FoodPileMath.Drain(30f, 30f, 0f, 0f), 1e-4f);
            Assert.AreEqual(30f, FoodPileMath.Drain(30f, 30f, 0f, -5f), 1e-4f);
        }

        // ---- Authored prefab tunables ------------------------------------------

        private static FoodPile LoadPilePrefabComponent()
        {
            var go = TestAssets.Load<GameObject>(TestAssets.FoodPilePrefabPath);
            var pile = go.GetComponent<FoodPile>();
            Assert.IsNotNull(pile, "FoodPile.prefab has no FoodPile component.");
            return pile;
        }

        [Test]
        public void OuterPile_BlockerRadius_LeavesRoomToCollectFromTheEdge()
        {
            // The 2026-06-01 game-breaking bug: the blocker capsule grew past
            // CollectRadius minus the chicken capsule, so a chicken physically could
            // not get close enough to trigger collection. The blocker is a child of
            // the root, so the root's footprint scale multiplies it — at the top
            // step that scale is exactly 1 (asserted above).
            var pile = LoadPilePrefabComponent();
            float blocker = TestAssets.PrivateField<float>(pile, "_blockerRadius");
            float collect = pile.CollectRadius;

            float closestApproach = blocker + ChickenCapsuleRadius + CharacterControllerSkin;
            float margin = collect - closestApproach;

            Assert.Greater(margin, 0f,
                $"Outer pile: blocker {blocker} + chicken capsule {ChickenCapsuleRadius} + skin " +
                $"{CharacterControllerSkin} = {closestApproach}, but CollectRadius is only {collect}. " +
                "A chicken cannot get close enough to collect — food collection is broken.");
            Assert.Greater(margin, 0.25f,
                $"Outer pile collect margin is only {margin:0.000} m. Historically this bound has been " +
                "broken twice by growing the blocker; keep real headroom here.");
        }

        [Test]
        public void CentrePile_BlockerRadius_StillLeavesAPositiveCollectMargin()
        {
            // The centre pile is the same prefab scaled by MapGenerator's
            // _centerPileVisualScale, which scales the blocker too. STATE.md records
            // this margin as thin (~0.045 m) but positive and unchanged from what
            // already ships — so the assertion is the correctness bound, not a
            // comfort bound. Raising the visual scale must break this test.
            var pile = LoadPilePrefabComponent();
            float blocker = TestAssets.PrivateField<float>(pile, "_blockerRadius");
            float collect = pile.CollectRadius;
            float centreScale = CentrePileVisualScale();

            float closestApproach = blocker * centreScale + ChickenCapsuleRadius + CharacterControllerSkin;

            Assert.Less(closestApproach, collect,
                $"Centre pile: blocker {blocker} × visual scale {centreScale} = {blocker * centreScale:0.000}, " +
                $"closest approach {closestApproach:0.000} vs CollectRadius {collect}. The centre pile can " +
                "no longer be collected from. Either lower MapGenerator._centerPileVisualScale or raise " +
                "FoodPile._collectRadius.");
        }

        /// <summary>
        /// Reads MapGenerator's authored centre-pile scale out of Game.unity. The
        /// component lives in a scene, not on a prefab, so there is no asset to load
        /// — and opening the scene from an EditMode test would disturb whatever the
        /// developer has open.
        /// </summary>
        private static float CentrePileVisualScale()
        {
            string path = TestAssets.GameScenePath;
            Assert.IsTrue(System.IO.File.Exists(path), $"{path} not found on disk.");

            foreach (var line in System.IO.File.ReadLines(path))
            {
                int idx = line.IndexOf("_centerPileVisualScale:", System.StringComparison.Ordinal);
                if (idx < 0) continue;
                var raw = line.Substring(idx + "_centerPileVisualScale:".Length).Trim();
                Assert.IsTrue(float.TryParse(raw, System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out var value),
                    $"Could not parse '_centerPileVisualScale: {raw}' in {TestAssets.GameScenePath}.");
                return value;
            }

            Assert.Fail($"MapGenerator._centerPileVisualScale is not serialized in {TestAssets.GameScenePath}. " +
                        "Either the MapGenerator was removed from the scene or the field was renamed.");
            return 0f;
        }

        // ---- Match economy ------------------------------------------------------

        [Test]
        public void WinTarget_IsReachable_ByEveryClass_WithinTheMatchTimer()
        {
            // WS1 regression guard. Before the 2026-07-06 balance pass three of four
            // classes could not reach the target inside the timer even ignoring
            // travel, so every match ended on time and farming always beat fighting.
            // This is the loosest possible form of that check — pure collection time,
            // no travel, no contest — so a failure means the target is literally
            // unreachable, not merely hard.
            var cfg = TestAssets.Load<MatchConfigSO>(TestAssets.MatchConfigPath);
            var stats = TestAssets.LoadAllIn<ChickenStatsSO>(TestAssets.ClassesDir);
            Assert.IsNotEmpty(stats, "No ChickenStatsSO assets found.");

            foreach (var s in stats)
            {
                float pureCollectSeconds = cfg.FoodTargetToWin / s.CollectionRate;
                Assert.Less(pureCollectSeconds, cfg.MatchDurationSeconds,
                    $"{s.name}: collecting {cfg.FoodTargetToWin} food at {s.CollectionRate}/s takes " +
                    $"{pureCollectSeconds:0}s of pure standing-on-a-pile time, but a match is only " +
                    $"{cfg.MatchDurationSeconds:0}s. Travel and contest are on top of that, so the " +
                    "food-target win condition is dead for this class.");
            }
        }

        [Test]
        public void WinTarget_LeavesRoomForTravel_NotJustCollection()
        {
            // Design intent recorded in the WS1 pass: uncontested time-to-target
            // should sit near 70% of the timer, so both win conditions stay live.
            // Anything over 100% is covered by the test above; this one guards the
            // softer bound that pure collection alone must not eat the whole match.
            var cfg = TestAssets.Load<MatchConfigSO>(TestAssets.MatchConfigPath);

            foreach (var s in TestAssets.LoadAllIn<ChickenStatsSO>(TestAssets.ClassesDir))
            {
                float fraction = (cfg.FoodTargetToWin / s.CollectionRate) / cfg.MatchDurationSeconds;
                Assert.Less(fraction, 0.7f,
                    $"{s.name} spends {fraction:P0} of the match just standing on piles before travel is " +
                    "even counted. Either lower FoodTargetToWin or raise CollectionRate.");
            }
        }

        [Test]
        public void CargoCapacity_IsNeverLargerThanTheWinTarget()
        {
            // A single haul must not be able to win the match outright.
            var cfg = TestAssets.Load<MatchConfigSO>(TestAssets.MatchConfigPath);

            foreach (var s in TestAssets.LoadAllIn<ChickenStatsSO>(TestAssets.ClassesDir))
            {
                Assert.Less(s.CargoCapacity, cfg.FoodTargetToWin,
                    $"{s.name} can carry {s.CargoCapacity} food but the win target is only " +
                    $"{cfg.FoodTargetToWin} — one trip ends the match.");
            }
        }

        [Test]
        public void FullCargo_DepositsWellInsideTheDeathStunWindow()
        {
            // A stun drops cargo. If depositing a full load took longer than the
            // death stun, a rival could stun you, wait out their own travel time and
            // still beat you to the base — deposits would never resolve under
            // pressure. Fatty (largest cargo) is the binding case.
            var cfg = TestAssets.Load<MatchConfigSO>(TestAssets.MatchConfigPath);
            var stats = TestAssets.LoadAllIn<ChickenStatsSO>(TestAssets.ClassesDir);

            int largest = stats.Max(s => s.CargoCapacity);
            float depositSeconds = largest / cfg.DepositRatePerSecond;

            Assert.Less(depositSeconds, cfg.MatchDurationSeconds * 0.1f,
                $"Depositing a full {largest}-food load takes {depositSeconds:0.0}s at " +
                $"{cfg.DepositRatePerSecond}/s — over 10% of the whole match spent standing still at a base.");
        }
    }
}

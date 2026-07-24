using System.Collections.Generic;
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
    /// 2. <b>Authored tunables</b> read off the real <c>FoodPile.prefab</c>,
    ///    <c>Game.unity</c> and <c>MatchConfig.asset</c> — above all the
    ///    surface-collect margin that replaced the blocker-vs-CollectRadius bound
    ///    behind the 2026-06-01 game-breaking "collection silently stopped working"
    ///    bug. That old bound was fragile by construction: it coupled a pile's
    ///    collider size to a fixed centre-distance radius, so every time a pile grew
    ///    the margin shrank, and twice it went negative. Collection is now measured
    ///    from the pile's SURFACE, which makes the margin a constant at ANY pile
    ///    size — so these tests assert the strong invariant (touching ⇒ collectable,
    ///    for every authored size) rather than the old numeric near-miss.
    ///
    /// NOT covered: the drain RPC round trip, cargo crediting, the optimistic-credit
    /// duplication window, and the permanent pile's regen tick — all need a runner.
    /// </remarks>
    public sealed class EconomyAndPilesTests
    {
        // Chicken CharacterController geometry — documented in FoodPile._collectReach's
        // tooltip and in docs/STATE.md. Their sum is how close a chicken can physically
        // get to any solid surface.
        private const float ChickenCapsuleRadius = 0.5f;
        private const float CharacterControllerSkin = 0.08f;
        private const float ChickenClosestApproach = ChickenCapsuleRadius + CharacterControllerSkin;

        /// <summary>A chicken is 1.0 world unit wide, so world units == chicken widths.</summary>
        private const float ChickenWidth = 1.0f;

        /// <summary>NavMesh step height. A pile shorter than this stops being terrain.</summary>
        private const float NavMeshStepHeight = 0.75f;

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
            // The authored footprint IS the top bucket: a full pile must be exactly the
            // size MapGenerator asked for, not some multiple of it.
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

        // ---- Box-surface maths (the basis of size-independent collection) --------

        [Test]
        public void DistanceToBoxSurfaceXZ_InsideTheFootprint_IsZero()
        {
            // Standing on the island is trivially "at" it. A non-zero answer here would
            // make a chicken that walked onto a shrinking pile fall out of collect range.
            Assert.AreEqual(0f, FoodPileMath.DistanceToBoxSurfaceXZ(0f, 0f, 0f, 0f, 3.5f, 2f), 1e-5f);
            Assert.AreEqual(0f, FoodPileMath.DistanceToBoxSurfaceXZ(3.4f, 1.9f, 0f, 0f, 3.5f, 2f), 1e-5f,
                "A point just inside the corner is still inside.");
            Assert.AreEqual(0f, FoodPileMath.DistanceToBoxSurfaceXZ(3.5f, 2f, 0f, 0f, 3.5f, 2f), 1e-5f,
                "A point exactly on the surface is at distance 0, not epsilon-negative.");
        }

        [Test]
        public void DistanceToBoxSurfaceXZ_OffAFace_IsThePerpendicularGap()
        {
            // Off the +X face of a 7×4 island centred at the origin: half-extent 3.5.
            Assert.AreEqual(0.5f, FoodPileMath.DistanceToBoxSurfaceXZ(4f, 0f, 0f, 0f, 3.5f, 2f), 1e-5f);
            // Off the -Z face: half-extent 2.
            Assert.AreEqual(1.25f, FoodPileMath.DistanceToBoxSurfaceXZ(0f, -3.25f, 0f, 0f, 3.5f, 2f), 1e-5f);
            // Sliding along a face must not change the answer.
            Assert.AreEqual(0.5f, FoodPileMath.DistanceToBoxSurfaceXZ(4f, 1.9f, 0f, 0f, 3.5f, 2f), 1e-5f);
        }

        [Test]
        public void DistanceToBoxSurfaceXZ_OffACorner_IsTheDiagonalGap()
        {
            // 3-4-5: 3 past the +X face, 4 past the +Z face.
            Assert.AreEqual(5f, FoodPileMath.DistanceToBoxSurfaceXZ(6.5f, 6f, 0f, 0f, 3.5f, 2f), 1e-5f);
        }

        [Test]
        public void DistanceToBoxSurfaceXZ_HonoursAnOffCentreBox()
        {
            // Piles are jittered per match, so the centre argument is never (0,0) in practice.
            Assert.AreEqual(0.5f, FoodPileMath.DistanceToBoxSurfaceXZ(14f, -8f, 10f, -8f, 3.5f, 2f), 1e-5f);
        }

        [Test]
        public void DistanceToBoxSurfaceXZ_FarAway_IsNotClampedOrSaturated()
        {
            Assert.AreEqual(96.5f, FoodPileMath.DistanceToBoxSurfaceXZ(100f, 0f, 0f, 0f, 3.5f, 2f), 1e-4f);
        }

        // ---- The invariant that replaced the blocker-vs-CollectRadius margin -----

        /// <summary>
        /// Every pile size the game actually spawns, plus each one at its smallest
        /// non-empty footprint step. The point of the surface-relative rewrite is that
        /// this list can grow without the margin moving at all.
        /// </summary>
        private static IEnumerable<TestCaseData> AuthoredPileFootprints()
        {
            yield return new TestCaseData(SceneVector2("_centerPileFootprint")).SetName("centre island");
            yield return new TestCaseData(SceneVector2("_contestedPileFootprint")).SetName("contested island");
            yield return new TestCaseData(SceneVector2("_personalPileFootprint")).SetName("personal island");
            yield return new TestCaseData(SceneVector2("_centerPileFootprint") * MinScale)
                .SetName("centre island at its smallest non-empty step");
            yield return new TestCaseData(SceneVector2("_personalPileFootprint") * MinScale)
                .SetName("personal island at its smallest non-empty step");
            // Deliberately absurd: the invariant must not depend on the authored numbers.
            yield return new TestCaseData(new Vector2(40f, 25f)).SetName("an island far bigger than any we ship");
        }

        [TestCaseSource(nameof(AuthoredPileFootprints))]
        public void AChickenTouchingAPile_IsWithinCollectReach_AtAnySize(Vector2 footprint)
        {
            // THE invariant. The old bound compared a blocker radius against a fixed
            // centre-distance CollectRadius, so it degraded as piles grew and broke
            // outright at ~2 chicken-widths. Measuring to the SURFACE makes the closest
            // approach a constant — the chicken's own capsule + skin — for every size.
            var pile = LoadPilePrefabComponent();
            float reach = TestAssets.PrivateField<float>(pile, "_collectReach");

            float halfX = footprint.x * 0.5f;
            float halfZ = footprint.y * 0.5f;

            // Pressed against the middle of the long face, and against a corner: a
            // CharacterController cannot get nearer than its radius + skin either way.
            float faceContact = FoodPileMath.DistanceToBoxSurfaceXZ(
                halfX + ChickenClosestApproach, 0f, 0f, 0f, halfX, halfZ);
            float cornerContact = FoodPileMath.DistanceToBoxSurfaceXZ(
                halfX + ChickenClosestApproach * Mathf.Sqrt(0.5f),
                halfZ + ChickenClosestApproach * Mathf.Sqrt(0.5f),
                0f, 0f, halfX, halfZ);

            Assert.AreEqual(ChickenClosestApproach, faceContact, 1e-4f,
                $"A chicken touching a {footprint.x}×{footprint.y} pile's face should be exactly " +
                $"{ChickenClosestApproach} from its surface — the distance must not depend on pile size.");
            Assert.AreEqual(ChickenClosestApproach, cornerContact, 1e-4f,
                "Corner contact must give the same surface distance as face contact.");

            float margin = reach - faceContact;
            Assert.Greater(margin, 0f,
                $"A chicken pressed against a {footprint.x}×{footprint.y} pile is {faceContact:0.000} from its " +
                $"surface but _collectReach is only {reach}. It physically cannot get closer — collection is dead.");
            Assert.Greater(margin, 0.25f,
                $"Collect margin on a {footprint.x}×{footprint.y} pile is only {margin:0.000} m. This bound has " +
                "been broken twice historically; keep real headroom, and never trim _collectReach to fix a " +
                "different problem.");
        }

        [Test]
        public void CollectMargin_IsIdentical_ForTheSmallestAndLargestPileWeSpawn()
        {
            // Stated as its own test because it is the whole reason for the rewrite:
            // size-independence, not merely "each authored size happens to pass".
            var smallest = SceneVector2("_personalPileFootprint") * MinScale;
            var largest  = SceneVector2("_centerPileFootprint");

            float small = FoodPileMath.DistanceToBoxSurfaceXZ(
                smallest.x * 0.5f + ChickenClosestApproach, 0f, 0f, 0f, smallest.x * 0.5f, smallest.y * 0.5f);
            float large = FoodPileMath.DistanceToBoxSurfaceXZ(
                largest.x * 0.5f + ChickenClosestApproach, 0f, 0f, 0f, largest.x * 0.5f, largest.y * 0.5f);

            Assert.AreEqual(small, large, 1e-4f,
                "Surface-relative collection must give the same margin at every pile size. If these ever " +
                "differ, something reintroduced a centre-distance test.");
        }

        [Test]
        public void CentreIsland_ReadsAsAnIsland_NotAProp()
        {
            // Maestro's playtest ask, pinned in units the design speaks: chicken widths.
            var centre = SceneVector2("_centerPileFootprint");

            Assert.GreaterOrEqual(centre.x / ChickenWidth, 6f,
                $"The centre island is only {centre.x / ChickenWidth:0.0} chickens across. It is meant to read " +
                "as a landmark you route around (~7), not as a pile you step over.");
            Assert.GreaterOrEqual(centre.y / ChickenWidth, 3f,
                $"The centre island is only {centre.y / ChickenWidth:0.0} chickens deep.");
            Assert.Greater(centre.x, centre.y,
                "The centre island is authored wide-and-shallow so it reads as an island rather than a blob.");
        }

        [Test]
        public void OuterIslands_AreSmallerThanTheCentre()
        {
            // GDD §3's risk gradient is legible partly through size: centre > contested >
            // personal. Equal-sized piles would erase that read.
            var centre    = SceneVector2("_centerPileFootprint");
            var contested = SceneVector2("_contestedPileFootprint");
            var personal  = SceneVector2("_personalPileFootprint");

            Assert.Greater(centre.x * centre.y, contested.x * contested.y,
                "The contested islands must stay smaller than the centre island.");
            Assert.Greater(contested.x * contested.y, personal.x * personal.y,
                "The personal islands must stay smaller than the contested ones.");
        }

        [Test]
        public void PileHeight_StaysAboveTheNavMeshStepHeight()
        {
            // The pile's root Y scale IS the blocker height now. Drop it below the step
            // height and bots walk over piles — they stop being terrain entirely, which
            // is the whole point of ADR 0003 Decision 2.
            var pile = LoadPilePrefabComponent();
            float height = TestAssets.PrivateField<float>(pile, "_blockerHeight");

            Assert.Greater(height, NavMeshStepHeight,
                $"FoodPile._blockerHeight is {height}, at or under the NavMesh step height " +
                $"{NavMeshStepHeight}. Bots will path straight over piles.");
        }

        [Test]
        public void PrefabFootprintFallback_IsUsable_WhenTheSpawnerStampsNothing()
        {
            // FootprintSize is [Networked] and defaults to zero. Any pile spawned without
            // onBeforeSpawned (scene-placed, a future spawn path) falls back to this — a
            // zero here would collapse every such pile to a point.
            var pile = LoadPilePrefabComponent();
            var fallback = TestAssets.PrivateField<Vector2>(pile, "_footprintSize");

            Assert.Greater(fallback.x, 0f, "FoodPile._footprintSize.x must be positive — it is the un-stamped fallback.");
            Assert.Greater(fallback.y, 0f, "FoodPile._footprintSize.y must be positive — it is the un-stamped fallback.");
        }

        /// <summary>
        /// Reads one of MapGenerator's authored <c>Vector2</c> tunables out of Game.unity.
        /// The component lives in a scene, not on a prefab, so there is no asset to load —
        /// and opening the scene from an EditMode test would disturb whatever the developer
        /// has open.
        /// </summary>
        private static Vector2 SceneVector2(string fieldName)
        {
            string path = TestAssets.GameScenePath;
            Assert.IsTrue(System.IO.File.Exists(path), $"{path} not found on disk.");

            string key = fieldName + ":";
            foreach (var line in System.IO.File.ReadLines(path))
            {
                int idx = line.IndexOf(key, System.StringComparison.Ordinal);
                if (idx < 0) continue;

                var raw = line.Substring(idx + key.Length).Trim();          // "{x: 7, y: 4}"
                var match = System.Text.RegularExpressions.Regex.Match(raw,
                    @"x:\s*(-?[\d.eE+]+),\s*y:\s*(-?[\d.eE+]+)");
                Assert.IsTrue(match.Success, $"Could not parse '{fieldName}: {raw}' in {path} as a Vector2.");

                var culture = System.Globalization.CultureInfo.InvariantCulture;
                return new Vector2(
                    float.Parse(match.Groups[1].Value, culture),
                    float.Parse(match.Groups[2].Value, culture));
            }

            Assert.Fail($"MapGenerator.{fieldName} is not serialized in {path}. Either the MapGenerator was " +
                        "removed from the scene or the field was renamed — check every inspector slot too.");
            return Vector2.zero;
        }

        /// <summary>Same approach as <see cref="SceneVector2"/>, for a plain scalar field.</summary>
        private static float SceneFloat(string fieldName)
        {
            string path = TestAssets.GameScenePath;
            Assert.IsTrue(System.IO.File.Exists(path), $"{path} not found on disk.");

            string key = fieldName + ":";
            foreach (var line in System.IO.File.ReadLines(path))
            {
                int idx = line.IndexOf(key, System.StringComparison.Ordinal);
                if (idx < 0) continue;

                var raw = line.Substring(idx + key.Length).Trim();
                var culture = System.Globalization.CultureInfo.InvariantCulture;
                if (float.TryParse(raw, System.Globalization.NumberStyles.Float, culture, out var value))
                    return value;
            }

            Assert.Fail($"MapGenerator.{fieldName} is not serialized in {path}. Either the MapGenerator was " +
                        "removed from the scene or the field was renamed — check every inspector slot too.");
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
        public void PersonalPile_CannotFillEvenTheSmallestCargo_InOneVisit()
        {
            // Design intent (Maestro, 2026-07-24): the personal pile is safe but weak — a
            // trickle nobody contests, not a farm. If it could fill any class's cargo in
            // one visit, staying home would be a viable strategy and the whole point of the
            // outward-routing pillar (contested/centre piles as the real yield) collapses.
            float personalAmount = SceneFloat("_personalPileAmount");
            var stats = TestAssets.LoadAllIn<ChickenStatsSO>(TestAssets.ClassesDir);
            Assert.IsNotEmpty(stats, "No ChickenStatsSO assets found.");

            float smallestCargo = stats.Min(s => s.CargoCapacity);
            Assert.Less(personalAmount, smallestCargo,
                $"Personal pile carries {personalAmount} food, but the smallest cargo capacity in the " +
                $"roster is {smallestCargo} — that class (or any larger-cargo class) can fill up from " +
                "the 'safe' pile alone, which defeats the point of it being weak.");
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

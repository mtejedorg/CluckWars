using NUnit.Framework;
using UnityEngine;
using CluckWars.Gameplay;

namespace CluckWars.Tests
{
    public sealed class PinwheelLayoutTests
    {
        /// <summary>
        /// The SHIPPED arena's half-size, read from <c>Game.unity</c>. Every case in this
        /// file used to hardcode 15f or 19f; after the x1.35 rescale (2026-08-14) those
        /// literals described an arena that no longer exists, and — worse — an arena small
        /// enough that the enlarged <see cref="PinwheelLayout.HubPlazaRadius"/> collapses
        /// every arm to zero length, which several of these assertions would have passed
        /// vacuously.
        /// </summary>
        private static float ArenaHalfSize => ShippedMap.HalfSize;

        [Test] public void Build_IsDeterministicForSeed()
        {
            float half = ArenaHalfSize;
            var a = PinwheelLayout.Build(half, 8, seed: 42, centerKeepClear: 3f, armThickness: 0.5f);
            var b = PinwheelLayout.Build(half, 8, seed: 42, centerKeepClear: 3f, armThickness: 0.5f);
            Assert.AreEqual(a.Length, b.Length);
            for (int i = 0; i < a.Length; i++)
            {
                Assert.AreEqual(a[i].A, b[i].A);
                Assert.AreEqual(a[i].B, b[i].B);
                Assert.AreEqual(a[i].Class, b[i].Class);
            }
        }

        [Test] public void Build_ProducesOneSegmentPerWedge()
        {
            var segs = PinwheelLayout.Build(ArenaHalfSize, 8, seed: 1, centerKeepClear: 3f, armThickness: 0.5f);
            Assert.AreEqual(8, segs.Length);
        }

        [Test] public void Build_KeepsSegmentsOutOfCenterDisc()
        {
            var segs = PinwheelLayout.Build(ArenaHalfSize, 8, seed: 7, centerKeepClear: 3f, armThickness: 0.5f);
            foreach (var s in segs)
            {
                // Neither endpoint may sit inside the center keep-clear disc.
                Assert.GreaterOrEqual(s.A.magnitude, 3f, "segment endpoint inside center keep-clear");
                Assert.GreaterOrEqual(s.B.magnitude, 3f, "segment endpoint inside center keep-clear");
            }
        }

        [Test] public void Build_ZeroWedgesReturnsEmptyArray()
        {
            var segs = PinwheelLayout.Build(ArenaHalfSize, 0, seed: 1, centerKeepClear: 3f, armThickness: 0.5f);
            Assert.AreEqual(0, segs.Length);
        }

        [Test] public void Build_HonorsExtraKeepClearDiscs()
        {
            float half = ArenaHalfSize;

            // The disc is placed ON a real arm, part-way along its span. That placement is
            // the whole test: the previous version put it at r = 6, inside the hub plaza
            // where no arm has ever been built, so "no sample point intrudes" was true by
            // construction and the clipping code could have been deleted without failing.
            var unclipped = PinwheelLayout.Build(half, 8, seed: 3, centerKeepClear: 3f, armThickness: 0.5f);
            Vector2 armDir = unclipped[0].B.normalized;
            float armMid = Mathf.Lerp(unclipped[0].A.magnitude, unclipped[0].B.magnitude, 0.5f);

            var discs = new[] { new KeepClearDisc(armDir * armMid, 2f) };
            var segs = PinwheelLayout.Build(half, 8, seed: 3, centerKeepClear: 3f, armThickness: 0.5f, extraKeepClearDiscs: discs);

            // The mechanism must actually fire — otherwise the loop below proves nothing.
            Assert.Less((segs[0].B - segs[0].A).magnitude, (unclipped[0].B - unclipped[0].A).magnitude - 0.5f,
                "arm 0 runs straight through the keep-clear disc and was not clipped at all");

            foreach (var s in segs)
            {
                if (s.A == s.B) continue; // collapsed/zero-length — nothing to check

                // Sample points along the segment must never land inside the disc.
                for (float t = 0f; t <= 1f; t += 0.1f)
                {
                    Vector2 p = Vector2.Lerp(s.A, s.B, t);
                    float distToDisc = Vector2.Distance(p, discs[0].Center);
                    Assert.GreaterOrEqual(distToDisc, discs[0].Radius - 0.01f,
                        $"segment sample point {p} intrudes into keep-clear disc at {discs[0].Center} r={discs[0].Radius}");
                }
            }
        }

        /// <summary>Distance to the square boundary along a bearing (mirrors Build).</summary>
        private static float BoundaryRadius(Vector2 dir, float arenaHalfSize) =>
            arenaHalfSize / Mathf.Max(Mathf.Abs(dir.x), Mathf.Abs(dir.y));

        /// <summary>
        /// Asserts the OPENING invariant rather than absolute radii: even walls leave a
        /// 3 m gap at the rim, odd walls leave one at the hub. Absolute end radii are not
        /// fixed, because a wall reaches the boundary along its OWN jittered bearing and
        /// the square's boundary distance varies with angle.
        /// </summary>
        [Test] public void Build_AlternatesRimAndHubOpenings()
        {
            float half = ArenaHalfSize;
            var segs = PinwheelLayout.Build(half, 8, seed: 42, centerKeepClear: 3f, armThickness: 0.5f);
            Assert.AreEqual(8, segs.Length);

            float hubPlaza = PinwheelLayout.SolveHubPlazaRadius(3f, 0.5f, 8);

            for (int i = 0; i < 8; i++)
            {
                Vector2 dir = segs[i].B.normalized;
                float rStart = segs[i].A.magnitude;
                float rEnd   = segs[i].B.magnitude;
                float boundary = BoundaryRadius(dir, half);

                if (i % 2 == 0)
                {
                    Assert.AreEqual(hubPlaza, rStart, 0.1f, $"wall {i} should start at the plaza edge");
                    Assert.AreEqual(PinwheelLayout.OpeningWidth, boundary - rEnd, 0.15f,
                        $"wall {i} should leave a {PinwheelLayout.OpeningWidth} m opening at the RIM");
                }
                else
                {
                    Assert.AreEqual(hubPlaza + PinwheelLayout.OpeningWidth, rStart, 0.1f,
                        $"wall {i} should leave a {PinwheelLayout.OpeningWidth} m opening at the HUB");
                    Assert.AreEqual(boundary, rEnd, 0.15f, $"wall {i} should reach the boundary");
                }
            }
        }

        /// <summary>
        /// 4-fold symmetry is a FAIRNESS requirement in a 4-player FFA: a 90° rotation maps
        /// wall i onto wall i+2 (for 8 wedges), so those must be geometrically identical.
        /// Independent per-wall jitter silently violated this.
        /// </summary>
        [Test] public void Build_IsFourFoldSymmetric_SoEverySectorIsIdentical(
            [Values(0, 1, 7, 42, 12345)] int seed)
        {
            float half = ArenaHalfSize;
            var segs = PinwheelLayout.Build(half, 8, seed, centerKeepClear: 3f, armThickness: 0.5f);

            for (int i = 0; i < 8; i++)
            {
                var a = segs[i];
                var b = segs[(i + 2) % 8];

                Assert.AreEqual(a.A.magnitude, b.A.magnitude, 0.001f,
                    $"seed={seed}: wall {i} and {(i + 2) % 8} must share a start radius under 90° rotation");
                Assert.AreEqual(a.B.magnitude, b.B.magnitude, 0.001f,
                    $"seed={seed}: wall {i} and {(i + 2) % 8} must share an end radius under 90° rotation");

                // Bearings must differ by exactly 90°.
                float angA = Mathf.Atan2(a.B.y, a.B.x) * Mathf.Rad2Deg;
                float angB = Mathf.Atan2(b.B.y, b.B.x) * Mathf.Rad2Deg;
                float delta = Mathf.DeltaAngle(angA, angB);
                Assert.AreEqual(90f, Mathf.Abs(delta), 0.01f,
                    $"seed={seed}: wall {i} -> {(i + 2) % 8} bearing step must be 90°");
            }
        }

        /// <summary>
        /// Walls must survive the food piles' keep-clear discs at their REAL v0.5 sizes.
        /// This is the gap that let a broken map ship: pile discs clip walls without ever
        /// violating a corridor-clearance assertion, so the clearance tests stayed green
        /// while 27% of walls collapsed to nothing and the rest shrank to 1-3 m. Only a
        /// direct length check catches it.
        /// </summary>
        /// <remarks>
        /// The acceptable length is now DERIVED from the layout constants rather than pinned
        /// to a magic fraction of the arena. The previous version used 0.398 x half, which
        /// described the mis-scaled arm of 2026-08-14; after the 2026-08-19 revert the arms
        /// are 0.5756 x half, and the old bound would have passed with 40% of the arm missing.
        /// A fraction that has to be re-derived by hand every time the map changes is exactly
        /// the staleness this suite keeps getting caught by.
        /// </remarks>
        [Test] public void Build_WallsSurvivePileKeepClearDiscs_AtRealV05Sizes()
        {
            float half = ShippedMap.HalfSize;
            float thickness = ShippedMap.WallThickness;
            float centreKeepClear = ShippedMap.CentreKeepClear;
            var discs = ShippedMap.KeepClearDiscs();

            // Both wall types come out the same length, and it is a consequence of the
            // derivation rather than a tuned value (GDD 3.4): boundary distance along the
            // 22.5-degree bearing, less one opening, less the hub plaza.
            float hubPlaza = PinwheelLayout.SolveHubPlazaRadius(centreKeepClear, thickness, 8);
            float boundaryAlongBearing = half / Mathf.Cos(Mathf.PI / 8f);
            float designedLength = boundaryAlongBearing - PinwheelLayout.OpeningWidth - hubPlaza;

            // 86% of the designed length: EXACTLY the headroom the original absolute 6.5 m
            // bound gave against its 7.57 m arm. Carried over, never widened.
            float minAcceptable = designedLength * 0.86f;

            for (int seed = 0; seed < 40; seed++)
            {
                var segs = PinwheelLayout.Build(half, 8, seed, centreKeepClear, thickness, discs);
                for (int i = 0; i < segs.Length; i++)
                {
                    float len = (segs[i].B - segs[i].A).magnitude;
                    Assert.Greater(len, minAcceptable,
                        $"seed={seed}: wall {i} was clipped to {len:0.00} m by a pile keep-clear " +
                        $"disc - the designed length is {designedLength:0.00} m in this " +
                        $"{half * 2f:0.0} m arena. Check pile footprints, _pilePositionJitter and " +
                        "PinwheelLayout.JitterFraction.");
                }
            }
        }

        /// <summary>
        /// The hub plaza must actually CONSUME the arm thickness. Until 2026-08-19
        /// <c>armThickness</c> was a parameter of <c>Build</c> that the body never read, while
        /// a comment in <c>MapGenerator.BuildInteriorObstacles</c> asserted that "the hub-plaza
        /// radius scales with whatever thickness is passed in". It did not — so raising
        /// <c>_wallThickness</c> (which this pass does, 0.5 -> 0.7) silently ate into the
        /// corridor clearance the plaza exists to guarantee.
        /// </summary>
        [Test] public void SolveHubPlazaRadius_GrowsWithArmThickness()
        {
            // Chosen so the centre-pile term binds rather than the flat HubPlazaRadius floor,
            // which by construction cannot respond to thickness.
            const float bigCentrePile = 12f;
            float thin  = PinwheelLayout.SolveHubPlazaRadius(bigCentrePile, 0.5f, 8);
            float thick = PinwheelLayout.SolveHubPlazaRadius(bigCentrePile, 1.4f, 8);

            Assert.Greater(thick, thin,
                "a thicker arm must push the hub plaza OUT — otherwise its extra half-thickness " +
                "is taken out of the walkable ring around the centre pile.");
            Assert.AreEqual(0.45f, thick - thin, 0.0001f,
                "the plaza should grow by exactly half the extra thickness (surface-to-surface).");
        }

        /// <summary>
        /// At high wedge counts the binding constraint stops being the centre pile and becomes
        /// the arms pinching each other. Guards the third term of the solve, which nothing else
        /// exercises at the shipped W=8.
        /// </summary>
        [Test] public void SolveHubPlazaRadius_PushesOutWhenAdjacentArmsWouldPinch()
        {
            float plaza = PinwheelLayout.SolveHubPlazaRadius(centerKeepClear: 3f, armThickness: 1.4f, wedges: 20);
            float gapAtPlaza = 2f * plaza * Mathf.Sin(Mathf.PI / 20f) - 1.4f;

            Assert.GreaterOrEqual(gapAtPlaza, PinwheelLayout.MinCorridorWidth - 0.001f,
                "two adjacent arms pinch below MinCorridorWidth at the plaza edge");
        }

        /// <summary>
        /// <b>W = 8 is a proven global optimum, not a tuning choice.</b> Wall bearings are
        /// <c>(i + 0.5)·360/W</c>; every pile and base sits on a multiple of 45 degrees. The
        /// contested pile needs the nearest wall at least <c>asin(4.0936 / 20.2507)</c> =
        /// 11.665 degrees off its bearing or the wall is clipped away. Only W = 8 reaches the
        /// maximum possible 22.5-degree offset; W = 12 and W = 20 land four arms EXACTLY on the
        /// corner diagonals, and W = 16's best (11.25) misses the requirement by 0.415.
        /// <para>
        /// This test exists so that someone reaching for wedge count as a density lever gets a
        /// red bar with the reason in it, rather than a plausible-looking map whose new arms
        /// have been quietly deleted by the objectives they were meant to route around.
        /// </para>
        /// </summary>
        [Test] public void WedgeCount_Eight_IsTheGlobalOptimumForObjectiveBearingOffset()
        {
            // asin(contested disc radius / contested pile radius), from the shipped scene.
            float contestedRadius = Vector2.Distance(Vector2.zero, ContestedPileFlat());
            float contestedDisc = ShippedMap.FootprintRadius(ShippedMap.ContestedPileFootprint)
                                  + ShippedMap.PilePositionJitter + PinwheelLayout.PileArmBuffer;
            float requiredDegrees = Mathf.Asin(contestedDisc / contestedRadius) * Mathf.Rad2Deg;

            Assert.AreEqual(11.665f, requiredDegrees, 0.02f,
                "the required bearing offset moved — re-derive the wedge-count table in " +
                "PinwheelLayout's class remarks before trusting it.");

            var offsets = new System.Collections.Generic.Dictionary<int, float>();
            foreach (int w in new[] { 8, 12, 16, 20 }) offsets[w] = MinOffsetFrom45Multiples(w);

            Assert.AreEqual(22.5f, offsets[8], 0.001f, "W=8 should reach the maximum 22.5 offset");
            Assert.AreEqual(0f, offsets[12], 0.001f, "W=12 should land arms exactly on the diagonals");
            Assert.AreEqual(11.25f, offsets[16], 0.001f, "W=16's best offset should be 11.25");
            Assert.AreEqual(0f, offsets[20], 0.001f, "W=20 should land arms exactly on the diagonals");

            foreach (var kv in offsets)
            {
                if (kv.Key == 8) continue;
                Assert.Less(kv.Value, requiredDegrees,
                    $"W={kv.Key} reaches {kv.Value:0.###} degrees, which would clear the required " +
                    $"{requiredDegrees:0.###} — the optimality claim in PinwheelLayout's remarks is stale.");
            }
        }

        private static Vector2 ContestedPileFlat()
        {
            var corners = ShippedMap.Corners();
            var p = MapGenerator.ContestedPileNominal(corners[0], corners[1], ShippedMap.ContestedEdgeInset);
            return new Vector2(p.x, p.z);
        }

        /// <summary>Smallest angular distance from any wall bearing to any 45-degree multiple.</summary>
        private static float MinOffsetFrom45Multiples(int wedges)
        {
            float worst = float.MaxValue;
            for (int i = 0; i < wedges; i++)
            {
                float bearing = (i + 0.5f) * 360f / wedges;
                float nearest = Mathf.Round(bearing / 45f) * 45f;
                worst = Mathf.Min(worst, Mathf.Abs(Mathf.DeltaAngle(bearing, nearest)));
            }
            return worst;
        }

        /// <summary>
        /// The generator must stay correct for wedge counts other than 8. An earlier
        /// revision hardcoded a 45° bearing, so at 13 wedges walls 0 and 8 landed on the
        /// same line — overlapping geometry that the adjacent-pair clearance test could
        /// not see.
        /// </summary>
        [Test] public void Build_NeverProducesTwoWallsOnTheSameBearing(
            [Values(4, 6, 8, 10, 13, 16)] int wedges)
        {
            var segs = PinwheelLayout.Build(ArenaHalfSize, wedges, seed: 3, centerKeepClear: 3f, armThickness: 0.5f);

            for (int i = 0; i < segs.Length; i++)
            for (int j = i + 1; j < segs.Length; j++)
            {
                if (segs[i].A == segs[i].B || segs[j].A == segs[j].B) continue;
                float angI = Mathf.Atan2(segs[i].B.y, segs[i].B.x) * Mathf.Rad2Deg;
                float angJ = Mathf.Atan2(segs[j].B.y, segs[j].B.x) * Mathf.Rad2Deg;
                Assert.Greater(Mathf.Abs(Mathf.DeltaAngle(angI, angJ)), 1f,
                    $"wedges={wedges}: walls {i} and {j} sit on the same bearing");
            }
        }
    }
}

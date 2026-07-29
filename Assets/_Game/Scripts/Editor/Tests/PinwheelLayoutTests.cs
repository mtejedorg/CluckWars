using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using CluckWars.Gameplay;

namespace CluckWars.Tests
{
    public sealed class PinwheelLayoutTests
    {
        [Test] public void Build_IsDeterministicForSeed()
        {
            var a = PinwheelLayout.Build(15f, 8, seed: 42, centerKeepClear: 3f, armThickness: 0.5f);
            var b = PinwheelLayout.Build(15f, 8, seed: 42, centerKeepClear: 3f, armThickness: 0.5f);
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
            var segs = PinwheelLayout.Build(15f, 8, seed: 1, centerKeepClear: 3f, armThickness: 0.5f);
            Assert.AreEqual(8, segs.Length);
        }

        [Test] public void Build_KeepsSegmentsOutOfCenterDisc()
        {
            var segs = PinwheelLayout.Build(15f, 8, seed: 7, centerKeepClear: 3f, armThickness: 0.5f);
            foreach (var s in segs)
            {
                // Neither endpoint may sit inside the center keep-clear disc.
                Assert.GreaterOrEqual(s.A.magnitude, 3f, "segment endpoint inside center keep-clear");
                Assert.GreaterOrEqual(s.B.magnitude, 3f, "segment endpoint inside center keep-clear");
            }
        }

        [Test] public void Build_ZeroWedgesReturnsEmptyArray()
        {
            var segs = PinwheelLayout.Build(15f, 0, seed: 1, centerKeepClear: 3f, armThickness: 0.5f);
            Assert.AreEqual(0, segs.Length);
        }

        [Test] public void Build_HonorsExtraKeepClearDiscs()
        {
            // A disc sitting right where an arm would otherwise start must push that
            // arm's inner end out past the disc's far edge.
            var discs = new[] { new KeepClearDisc(new Vector2(6f, 0f), 2f) };
            var segs = PinwheelLayout.Build(19f, 8, seed: 3, centerKeepClear: 3f, armThickness: 0.5f, extraKeepClearDiscs: discs);

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
            const float half = 19f;
            var segs = PinwheelLayout.Build(half, 8, seed: 42, centerKeepClear: 3f, armThickness: 0.5f);
            Assert.AreEqual(8, segs.Length);

            float hubPlaza = Mathf.Max(PinwheelLayout.HubPlazaRadius,
                                       3f + PinwheelLayout.MinCorridorWidth);

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
            const float half = 19f;
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
        /// while 27% of walls collapsed to nothing and the rest shrank to 1–3 m. Only a
        /// direct length check catches it.
        /// </summary>
        [Test] public void Build_WallsSurvivePileKeepClearDiscs_AtRealV05Sizes()
        {
            const float half = 19f;
            const float pileJitter = 0.4f;   // MapGenerator._pilePositionJitter
            var centreFp    = new Vector2(12f, 10f);
            var personalFp  = new Vector2(5.5f, 4.6f);
            var contestedFp = new Vector2(6.5f, 5.4f);

            float FpRadius(Vector2 s) => 0.5f * Mathf.Sqrt(s.x * s.x + s.y * s.y);
            float centreKeepClear = FpRadius(centreFp);

            var corners = new[]
            {
                new Vector2(19f, 19f), new Vector2(-19f, 19f),
                new Vector2(-19f, -19f), new Vector2(19f, -19f),
            };

            var discs = new List<KeepClearDisc>();
            for (int i = 0; i < corners.Length; i++)
            {
                discs.Add(new KeepClearDisc(corners[i], 4f));
                discs.Add(new KeepClearDisc(corners[i].normalized * 17f,
                    FpRadius(personalFp) + pileJitter + PinwheelLayout.PileArmBuffer));
                Vector2 mid = (corners[i] + corners[(i + 1) % corners.Length]) * 0.5f;
                discs.Add(new KeepClearDisc(mid.normalized * 15f,
                    FpRadius(contestedFp) + pileJitter + PinwheelLayout.PileArmBuffer));
            }

            for (int seed = 0; seed < 40; seed++)
            {
                var segs = PinwheelLayout.Build(half, 8, seed, centreKeepClear, 0.5f, discs.ToArray());
                for (int i = 0; i < segs.Length; i++)
                {
                    float len = (segs[i].B - segs[i].A).magnitude;
                    Assert.Greater(len, 6.5f,
                        $"seed={seed}: wall {i} was clipped to {len:0.00} m by a pile keep-clear " +
                        "disc — the designed length is ~7.6 m. Check pile footprints, " +
                        "_pilePositionJitter and PinwheelLayout.JitterFraction.");
                }
            }
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
            var segs = PinwheelLayout.Build(19f, wedges, seed: 3, centerKeepClear: 3f, armThickness: 0.5f);

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

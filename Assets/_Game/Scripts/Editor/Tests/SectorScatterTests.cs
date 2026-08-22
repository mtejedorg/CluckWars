using NUnit.Framework;
using UnityEngine;
using CluckWars.Gameplay;

namespace CluckWars.Tests
{
    /// <summary>
    /// Coverage for <see cref="SectorScatter"/> — the 4-fold-symmetric scatter that replaced
    /// <c>MapGenerator.PlaceObstacleClass</c> on 2026-08-19.
    /// </summary>
    /// <remarks>
    /// The method it replaced was dead code, and could not have shipped: it rejection-sampled
    /// uniformly over the WHOLE arena, so a shared seed gave every peer the same map but not
    /// every player the same sector. These tests assert the fix STRUCTURALLY — the symmetry
    /// case compares exact floats, because a quarter turn is a sign flip and an integer
    /// addition, so a symmetry that holds only to a tolerance would mean the construction is
    /// wrong rather than that floats are imprecise.
    /// </remarks>
    public sealed class SectorScatterTests
    {
        private static readonly int[] Seeds = { 0, 1, 7, 42, 1234, 987654 };

        private static SectorScatter.Result BuildShipped(int seed) =>
            ShippedMap.Scatter(seed, ShippedMap.Arms(seed));

        /// <summary>
        /// Both roles forced ON, at the shipped spans and clearance.
        /// </summary>
        /// <remarks>
        /// <c>Game.unity</c> ships <c>_barrierPerSector = 0</c> — the free-run guardrail says
        /// the arena is already at reference density with cover alone. Every structural test
        /// below therefore runs against THIS configuration rather than the shipped one, or it
        /// would assert Barrier's properties over an empty set and pass vacuously. That is the
        /// exact failure the 2026-08-14 audit found in
        /// <c>Build_HonorsExtraKeepClearDiscs</c>, and it is not being repeated here. The
        /// clearance guarantees are monotone in obstacle count, so proving them at a HIGHER
        /// density than ships is strictly stronger.
        /// </remarks>
        private static SectorScatter.Result BuildExercised(int seed)
        {
            var settings = new ScatterSettings(
                coverPerSector: 2, ShippedMap.CoverSpanRange,
                barrierPerSector: 1, ShippedMap.BarrierSpanRange, ShippedMap.WallThickness,
                ShippedMap.ScatterClearance, ShippedMap.PlacementAttempts);

            return SectorScatter.Build(ShippedMap.HalfSize, seed, settings, ShippedMap.Arms(seed),
                                       ShippedMap.WallThickness, ShippedMap.CentreKeepClear,
                                       ShippedMap.KeepClearDiscs());
        }

        /// <summary>Both roles must actually appear, or every role assertion below is vacuous.</summary>
        [Test]
        public void TheExerciseConfiguration_ActuallyContainsBothRoles()
        {
            var boxes = BuildExercised(42).Boxes;
            Assert.IsTrue(System.Array.Exists(boxes, b => b.Role == ScatterRole.Cover), "no Cover placed");
            Assert.IsTrue(System.Array.Exists(boxes, b => b.Role == ScatterRole.Barrier), "no Barrier placed");
        }

        // ---- Symmetry by construction -------------------------------------------------

        /// <summary>
        /// Every group of four consecutive boxes is one candidate and its three quarter-turn
        /// images, in rotation order. Asserted with EXACT equality: <c>(x, z) -> (-z, x)</c>
        /// and <c>yaw -> yaw + 90</c> introduce no rounding, so this is symmetry proven by
        /// construction rather than observed from a lucky seed.
        /// </summary>
        [Test]
        public void Build_EmitsEachCandidateAsFourExactQuarterTurns([ValueSource(nameof(Seeds))] int seed)
        {
            var boxes = BuildExercised(seed).Boxes;
            Assert.AreEqual(0, boxes.Length % 4,
                "scatter must be emitted in quadruples or it cannot be 4-fold symmetric");

            for (int i = 0; i < boxes.Length; i += 4)
            {
                for (int q = 1; q < 4; q++)
                {
                    var expected = boxes[i + q - 1].RotatedQuarterTurn();
                    var actual = boxes[i + q];

                    Assert.AreEqual(expected.Center.x, actual.Center.x,
                        $"seed={seed}: box {i + q} is not the exact quarter turn of box {i + q - 1} (x)");
                    Assert.AreEqual(expected.Center.y, actual.Center.y,
                        $"seed={seed}: box {i + q} is not the exact quarter turn of box {i + q - 1} (z)");
                    Assert.AreEqual(expected.YawDegrees, actual.YawDegrees,
                        $"seed={seed}: box {i + q} yaw is not box {i + q - 1} yaw + 90");
                    Assert.AreEqual(expected.Span, actual.Span, $"seed={seed}: span differs across the quarter turn");
                    Assert.AreEqual(expected.Depth, actual.Depth, $"seed={seed}: depth differs across the quarter turn");
                    Assert.AreEqual(expected.Role, actual.Role, $"seed={seed}: role differs across the quarter turn");
                }
            }
        }

        /// <summary>
        /// The consequence players actually feel: each of the four 90-degree sectors holds the
        /// same number of each role. This is the fairness invariant, stated in the terms of
        /// the complaint rather than the terms of the implementation, so it would still fail
        /// if a future rewrite kept the quadruple shape but broke the sector split.
        /// </summary>
        [Test]
        public void Build_GivesEveryPlayerSectorTheSameCensus([ValueSource(nameof(Seeds))] int seed)
        {
            var boxes = BuildExercised(seed).Boxes;
            var census = new int[4, 2];

            foreach (var b in boxes)
            {
                float degrees = Mathf.Atan2(b.Center.y, b.Center.x) * Mathf.Rad2Deg;
                if (degrees < 0f) degrees += 360f;
                census[Mathf.Clamp((int)(degrees / 90f), 0, 3), (int)b.Role]++;
            }

            for (int role = 0; role < 2; role++)
            for (int sector = 1; sector < 4; sector++)
            {
                Assert.AreEqual(census[0, role], census[sector, role],
                    $"seed={seed}: sector {sector} holds a different number of {(ScatterRole)role} " +
                    "than sector 0 — that is a fairness bug in a 4-player FFA, not a cosmetic one");
            }
        }

        [Test]
        public void Build_IsDeterministicForSeed()
        {
            var a = BuildExercised(2026).Boxes;
            var b = BuildExercised(2026).Boxes;
            Assert.AreEqual(a.Length, b.Length);
            for (int i = 0; i < a.Length; i++)
            {
                Assert.AreEqual(a[i].Center, b[i].Center);
                Assert.AreEqual(a[i].Span, b[i].Span);
                Assert.AreEqual(a[i].YawDegrees, b[i].YawDegrees);
            }
        }

        // ---- Nothing may seal a route -------------------------------------------------

        /// <summary>
        /// Scatter must keep a full <see cref="PinwheelLayout.MinCorridorWidth"/> lane clear of
        /// the boundary, of every arm, and of every keep-clear disc. Measured surface-to-surface
        /// with independent geometry (an 8-way corner/edge sampling of the box outline), not by
        /// re-calling the builder's own predicate — otherwise the test proves only that the
        /// builder agrees with itself.
        /// </summary>
        [Test]
        public void Build_KeepsAMinCorridorLaneClearOfEverything([ValueSource(nameof(Seeds))] int seed)
        {
            float half = ShippedMap.HalfSize;
            float clearance = ShippedMap.ScatterClearance;
            var arms = ShippedMap.Arms(seed);
            var discs = ShippedMap.KeepClearDiscs();
            var boxes = BuildExercised(seed).Boxes;

            Assert.GreaterOrEqual(clearance, PinwheelLayout.MinCorridorWidth,
                "_scatterClearance in Game.unity is below MinCorridorWidth — scatter can pinch a corridor shut");

            foreach (var box in boxes)
            {
                foreach (var p in Outline(box))
                {
                    Assert.LessOrEqual(Mathf.Abs(p.x), half - clearance + 0.001f,
                        $"seed={seed}: scatter at {box.Center} comes within {clearance} m of the +/-X boundary");
                    Assert.LessOrEqual(Mathf.Abs(p.y), half - clearance + 0.001f,
                        $"seed={seed}: scatter at {box.Center} comes within {clearance} m of the +/-Z boundary");
                }

                Assert.GreaterOrEqual(DistanceToSurface(Vector2.zero, box), ShippedMap.CentreKeepClear + clearance - 0.001f,
                    $"seed={seed}: scatter at {box.Center} crowds the centre pile");

                foreach (var disc in discs)
                {
                    Assert.GreaterOrEqual(DistanceToSurface(disc.Center, box), disc.Radius + clearance - 0.001f,
                        $"seed={seed}: scatter at {box.Center} crowds the keep-clear disc at {disc.Center} (r={disc.Radius:0.00})");
                }

                float armHalf = ShippedMap.WallThickness * 0.5f;
                foreach (var arm in arms)
                {
                    if (arm.A == arm.B) continue;
                    for (int k = 0; k <= 100; k++)
                    {
                        var p = Vector2.Lerp(arm.A, arm.B, k / 100f);
                        Assert.GreaterOrEqual(DistanceToSurface(p, box), armHalf + clearance - 0.05f,
                            $"seed={seed}: scatter at {box.Center} pinches the corridor beside a pinwheel arm");
                    }
                }
            }
        }

        /// <summary>
        /// The corner pockets behind the bases must stay empty. ADR 0003 Decision 6 told
        /// placement to "deliberately reach into" them, but measured against the shipped
        /// arena the gap between a base's keep-clear disc and the arena corner is ~1.4 m,
        /// under MinCorridorWidth — a chicken cannot get in, so anything placed there is
        /// invisible clutter that only steals from the density budget. Enforced structurally
        /// (nothing satisfies both the disc rule and the boundary rule there), asserted here.
        /// </summary>
        [Test]
        public void Build_PlacesNothingInTheUnusableCornerPockets([ValueSource(nameof(Seeds))] int seed)
        {
            var corners = ShippedMap.Corners();
            var boxes = BuildExercised(seed).Boxes;

            foreach (var box in boxes)
            foreach (var corner in corners)
            {
                var spawn = MapGenerator.SpawnPointNominal(corner);
                var flatCorner = new Vector2(corner.x, corner.z);
                var flatSpawn = new Vector2(spawn.x, spawn.z);

                // "In the pocket" = further from the centre than the base is, on the base's
                // own side of the arena.
                bool beyondTheBase = box.Center.magnitude > flatSpawn.magnitude;
                bool onThisCornersSide = Vector2.Dot(box.Center.normalized, flatCorner.normalized) > 0.92f;

                Assert.IsFalse(beyondTheBase && onThisCornersSide,
                    $"seed={seed}: scatter at {box.Center} sits in the dead pocket behind the base at {flatSpawn}");
            }
        }

        // ---- Span bands mean something ------------------------------------------------

        /// <summary>
        /// The two roles differ by SPAN — which jump tier crosses them — and by nothing else.
        /// If a Barrier's span ever drops below <see cref="SectorScatter.MinBarrierSpan"/> the
        /// role collapses into Cover and the distinction becomes decorative, which is exactly
        /// the dead-concept failure GDD 3.5 deleted height classes to avoid.
        /// </summary>
        [Test]
        public void Build_BarriersGateTheShortJumpAndCoverNeverDoes([ValueSource(nameof(Seeds))] int seed)
        {
            float gate = SectorScatter.MinBarrierSpan;
            Assert.AreEqual(JumpResolver.ShortDistance - 0.8f, gate, 0.0001f,
                "MinBarrierSpan must stay tied to the Short jump tier and GDD 3.5's 0.8 m body clearance");

            foreach (var box in BuildExercised(seed).Boxes)
            {
                if (box.Role == ScatterRole.Barrier)
                {
                    Assert.Greater(box.Span, gate,
                        $"seed={seed}: a Barrier with span {box.Span:0.00} is crossed by a Short jump — " +
                        "it is Cover wearing a different name. Raise _barrierSpanRange.x in Game.unity.");
                }
                else
                {
                    Assert.Less(box.Span, gate,
                        $"seed={seed}: Cover with span {box.Span:0.00} gates the Short jump — " +
                        "Cover is meant to break lines, not traversal. Lower _coverSpanRange.y.");
                    Assert.Less(box.Depth, gate,
                        $"seed={seed}: Cover with depth {box.Depth:0.00} gates the Short jump taken side-on.");
                }
            }
        }

        [Test]
        public void Build_PlacesTheRequestedDensity_OrSaysSo()
        {
            var result = BuildShipped(4242);
            Assert.IsFalse(result.UnderPlaced,
                $"the arena could not fit the authored scatter density: asked for " +
                $"{result.CoverRequestedPerSector} cover + {result.BarrierRequestedPerSector} barrier per sector, " +
                $"fitted {result.CoverPlacedPerSector} + {result.BarrierPlacedPerSector}. " +
                "Placement is best-effort and does NOT relax clearances, so this is a silent " +
                "density shortfall unless the counts in Game.unity come down.");
        }

        [Test]
        public void Build_ZeroCountsCleanlyDisableTheRole()
        {
            var settings = new ScatterSettings(0, new Vector2(1.2f, 2.2f), 0, new Vector2(4.6f, 6.4f),
                                               0.7f, PinwheelLayout.MinCorridorWidth, 40);
            var result = SectorScatter.Build(ShippedMap.HalfSize, 1, settings, ShippedMap.Arms(1),
                                             ShippedMap.WallThickness, ShippedMap.CentreKeepClear,
                                             ShippedMap.KeepClearDiscs());
            Assert.AreEqual(0, result.Boxes.Length);
            Assert.IsFalse(result.UnderPlaced);
        }

        // ---- Local geometry, independent of the builder's own ------------------------

        private static Vector2[] Outline(in ScatterBox box)
        {
            float rad = box.YawDegrees * Mathf.Deg2Rad;
            var along = new Vector2(Mathf.Cos(rad), Mathf.Sin(rad)) * (box.Span * 0.5f);
            var across = new Vector2(-Mathf.Sin(rad), Mathf.Cos(rad)) * (box.Depth * 0.5f);
            return new[]
            {
                box.Center + along + across,
                box.Center + along - across,
                box.Center - along + across,
                box.Center - along - across,
            };
        }

        private static float DistanceToSurface(Vector2 point, in ScatterBox box)
        {
            float rad = box.YawDegrees * Mathf.Deg2Rad;
            float c = Mathf.Cos(rad), s = Mathf.Sin(rad);
            float dx = point.x - box.Center.x, dz = point.y - box.Center.y;
            float lx = dx * c + dz * s;
            float lz = -dx * s + dz * c;
            float ox = Mathf.Max(Mathf.Abs(lx) - box.Span * 0.5f, 0f);
            float oz = Mathf.Max(Mathf.Abs(lz) - box.Depth * 0.5f, 0f);
            return Mathf.Sqrt(ox * ox + oz * oz);
        }
    }
}

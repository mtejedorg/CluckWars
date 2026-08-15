using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using CluckWars.Gameplay;

namespace CluckWars.Tests
{
    /// <summary>
    /// Regression coverage for the 2026-07-27 pinwheel hub-pinch + pile-blocking fix
    /// ("chickens won't even fit where walls converge" / "piles block some paths").
    /// Rebuilds the same geometry <c>MapGenerator</c> builds — mirroring Game.unity's
    /// configured values post-revert — purely in C#/EditMode. No scene, no NavMesh, no
    /// Unity physics: closed-form clearance math plus a grid-based walkability flood fill.
    /// </summary>
    public sealed class MapClearanceTests
    {
        // ---- The map under test, READ from Game.unity ---------------------------------
        //
        // These were hand-mirrored literals until 2026-08-14, and by then most of them were
        // wrong: the centre footprint still said 12x10 against a shipped 7.8x6.5, and the
        // pile jitter said 1.5 against a shipped 0.4. The test was rigorously proving a map
        // nobody plays. Reading the scene is what makes it load-bearing — and it is the same
        // source MapGenerator itself uses, so the two cannot drift again.
        private static float ArenaHalfSize => TestAssets.SceneFloat("_planeSize") * 0.5f;
        private static float WallThickness => TestAssets.SceneFloat("_wallThickness");
        private static int StandardObstacleCount => Mathf.RoundToInt(TestAssets.SceneFloat("_standardObstacleCount"));
        private static float BaseCornerDistance => TestAssets.SceneFloat("_baseCornerDistance");
        private static Vector2 CenterPileFootprint => TestAssets.SceneVector2("_centerPileFootprint");
        private static Vector2 PersonalPileFootprint => TestAssets.SceneVector2("_personalPileFootprint");
        private static Vector2 ContestedPileFootprint => TestAssets.SceneVector2("_contestedPileFootprint");
        private static float PersonalPileInset => TestAssets.SceneFloat("_personalPileInset");
        private static float ContestedEdgeInset => TestAssets.SceneFloat("_contestedEdgeInset");
        private static float PilePositionJitter => TestAssets.SceneFloat("_pilePositionJitter");

        // The two values MapGenerator hardcodes rather than serializing, so there is no
        // scene entry to read. Still mirrors — keep them in step by hand.
        private const float BaseKeepClear = 4f;          // MapGenerator.ComputeBaseAndPileKeepClearDiscs
        private const float SpawnInsetFraction = 0.15f;  // MapGenerator.BaseInsetFraction

        private static float FootprintRadius(Vector2 size) => 0.5f * Mathf.Sqrt(size.x * size.x + size.y * size.y);

        private static Vector2 Flat(Vector3 v) => new Vector2(v.x, v.z);
        private static Vector3 Raise(Vector2 v) => new Vector3(v.x, 0f, v.y);

        private struct Box
        {
            public Vector2 Center;
            public float Width;  // extent along the box's own local X (its "length")
            public float Depth;  // extent along local Z (its "thickness")
            public float YawRad; // rotation of local X away from world +X
        }

        private static Vector2[] Corners()
        {
            float d = BaseCornerDistance;
            return new[]
            {
                new Vector2(+d, +d),
                new Vector2(-d, +d),
                new Vector2(-d, -d),
                new Vector2(+d, -d),
            };
        }

        /// <summary>Nominal (pre-jitter) doorstep-pile position — the generator's own formula.</summary>
        private static Vector2 PersonalPile(Vector2 corner) =>
            Flat(MapGenerator.PersonalPileNominal(Raise(corner), PersonalPileInset));

        /// <summary>Nominal (pre-jitter) contested-pile position — the generator's own formula.</summary>
        private static Vector2 ContestedPile(Vector2 cornerA, Vector2 cornerB) =>
            Flat(MapGenerator.ContestedPileNominal(Raise(cornerA), Raise(cornerB), ContestedEdgeInset));

        /// <summary>Mirrors MapGenerator.ComputeBaseAndPileKeepClearDiscs.</summary>
        private static KeepClearDisc[] KeepClearDiscs(Vector2[] corners)
        {
            float personalRadius = FootprintRadius(PersonalPileFootprint) + PilePositionJitter + PinwheelLayout.PileArmBuffer;
            float contestedRadius = FootprintRadius(ContestedPileFootprint) + PilePositionJitter + PinwheelLayout.PileArmBuffer;
            var discs = new List<KeepClearDisc>();
            for (int i = 0; i < corners.Length; i++)
            {
                discs.Add(new KeepClearDisc(corners[i], BaseKeepClear));
                discs.Add(new KeepClearDisc(PersonalPile(corners[i]), personalRadius));
                discs.Add(new KeepClearDisc(ContestedPile(corners[i], corners[(i + 1) % corners.Length]), contestedRadius));
            }
            return discs.ToArray();
        }

        private static List<Box> BuildWallBoxes(WallSegment[] segments, float armThickness)
        {
            var boxes = new List<Box>();
            foreach (var s in segments)
            {
                if (s.A == s.B) continue; // collapsed/nonexistent arm — nothing built
                Vector2 delta = s.B - s.A;
                float length = delta.magnitude;
                Vector2 center = s.A + delta * 0.5f;
                float yaw = Mathf.Atan2(delta.y, delta.x);
                boxes.Add(new Box { Center = center, Width = length, Depth = armThickness, YawRad = yaw });
            }
            return boxes;
        }

        /// <summary>Shortest XZ distance from a point to a yaw-rotated box's surface. 0 if inside.</summary>
        private static float DistanceToBoxSurface(Vector2 p, Box box)
        {
            float c = Mathf.Cos(-box.YawRad), s = Mathf.Sin(-box.YawRad);
            float dx = p.x - box.Center.x, dz = p.y - box.Center.y;
            float lx = dx * c - dz * s;
            float lz = dx * s + dz * c;
            float ox = Mathf.Max(Mathf.Abs(lx) - box.Width * 0.5f, 0f);
            float oz = Mathf.Max(Mathf.Abs(lz) - box.Depth * 0.5f, 0f);
            return Mathf.Sqrt(ox * ox + oz * oz);
        }

        /// <summary>
        /// For every seed / wedge-count / arm-thickness combination, checks the real physical
        /// gap between every pair of arms adjacent by generation index (the only pairs that can
        /// ever be close enough to pinch — see PinwheelLayout.Build's derivation) never drops
        /// below <see cref="PinwheelLayout.MinCorridorWidth"/>, sampled densely across whatever
        /// radius range the two arms actually coexist at. This is the direct test of the hub
        /// convergence fix, independent of the closed-form derivation's own correctness.
        /// </summary>
        [Test]
        public void MinimumCorridorClearance_HoldsAcrossManySeeds(
            [Values(4, 6, 8, 10, 13, 16)] int wedges,
            [Values(0.5f, 1.4f)] float armThickness)
        {
            var corners = Corners();
            var discs = KeepClearDiscs(corners);
            float centerKeepClear = FootprintRadius(CenterPileFootprint);
            float arenaHalfSize = ArenaHalfSize;   // hoisted: the accessor re-reads Game.unity

            for (int seed = 0; seed < 25; seed++)
            {
                var segs = PinwheelLayout.Build(arenaHalfSize, wedges, seed, centerKeepClear, armThickness, discs);

                for (int i = 0; i < segs.Length; i++)
                {
                    var a = segs[i];
                    var b = segs[(i + 1) % segs.Length];
                    if (a.A == a.B || b.A == b.B) continue; // one side doesn't exist — no pinch possible

                    Vector2 dirA = (a.B - a.A).normalized;
                    Vector2 dirB = (b.B - b.A).normalized;
                    float lo = Mathf.Max(a.A.magnitude, b.A.magnitude);
                    float hi = Mathf.Min(a.B.magnitude, b.B.magnitude);
                    if (lo > hi) continue; // arms never coexist at the same radius

                    float minGap = float.MaxValue;
                    const int samples = 25;
                    for (int k = 0; k <= samples; k++)
                    {
                        float r = Mathf.Lerp(lo, hi, k / (float)samples);
                        float gap = Vector2.Distance(dirA * r, dirB * r) - armThickness;
                        if (gap < minGap) minGap = gap;
                    }

                    Assert.GreaterOrEqual(minGap, PinwheelLayout.MinCorridorWidth - 0.05f,
                        $"wedges={wedges} thickness={armThickness} seed={seed}: arm {i}/{(i + 1) % segs.Length} " +
                        $"gap {minGap:0.00} below MinCorridorWidth {PinwheelLayout.MinCorridorWidth:0.00}");
                }
            }
        }

        /// <summary>
        /// Grid flood-fill (0.5m cells, chicken-radius clearance) from each base's spawn point,
        /// with every food pile at its FULL (solid, blocking) footprint simultaneously — the
        /// worst case for connectivity. Confirms the centre and every other base stay reachable
        /// across several representative seeds. Does not model PlayerBase colliders (not part of
        /// this bug) or bot pathing — a coarse but direct check that nothing gets fully sealed.
        /// </summary>
        [Test]
        public void AllBasesReachCenterAndEachOther_WithEveryPileFull()
        {
            var corners = Corners();
            var discs = KeepClearDiscs(corners);
            float centerKeepClear = FootprintRadius(CenterPileFootprint);

            var pileBoxes = new List<Box>
            {
                new Box { Center = Vector2.zero, Width = CenterPileFootprint.x, Depth = CenterPileFootprint.y, YawRad = 0f },
            };
            for (int i = 0; i < corners.Length; i++)
            {
                pileBoxes.Add(new Box { Center = PersonalPile(corners[i]), Width = PersonalPileFootprint.x, Depth = PersonalPileFootprint.y, YawRad = 0f });
                pileBoxes.Add(new Box { Center = ContestedPile(corners[i], corners[(i + 1) % corners.Length]), Width = ContestedPileFootprint.x, Depth = ContestedPileFootprint.y, YawRad = 0f });
            }

            int[] seeds = { 0, 1, 2, 17, 12345 };
            const float cell = 0.5f;

            // Hoisted out of the flood fill: every one of these accessors re-reads
            // Game.unity's YAML, and Walkable/GridToWorld run once per grid cell.
            float arenaHalfSize = ArenaHalfSize;
            float wallThickness = WallThickness;
            int standardObstacleCount = StandardObstacleCount;
            int dim = Mathf.CeilToInt((arenaHalfSize * 2f) / cell) + 1;

            foreach (int seed in seeds)
            {
                var segs = PinwheelLayout.Build(arenaHalfSize, standardObstacleCount, seed, centerKeepClear, wallThickness, discs);
                var allBoxes = new List<Box>(pileBoxes);
                allBoxes.AddRange(BuildWallBoxes(segs, wallThickness));

                bool Walkable(Vector2 p)
                {
                    if (Mathf.Abs(p.x) > arenaHalfSize - 0.5f || Mathf.Abs(p.y) > arenaHalfSize - 0.5f) return false;
                    foreach (var box in allBoxes)
                    {
                        if (DistanceToBoxSurface(p, box) < PinwheelLayout.ChickenRadius) return false;
                    }
                    return true;
                }

                Vector2 GridToWorld(int gx, int gy) => new Vector2(-arenaHalfSize + gx * cell, -arenaHalfSize + gy * cell);
                (int gx, int gy) WorldToGrid(Vector2 p) => (
                    Mathf.Clamp(Mathf.RoundToInt((p.x + arenaHalfSize) / cell), 0, dim - 1),
                    Mathf.Clamp(Mathf.RoundToInt((p.y + arenaHalfSize) / cell), 0, dim - 1));

                var visited = new bool[dim, dim];
                var start = WorldToGrid(Vector2.Lerp(corners[0], Vector2.zero, SpawnInsetFraction));
                var queue = new Queue<(int, int)>();
                if (Walkable(GridToWorld(start.gx, start.gy)))
                {
                    queue.Enqueue(start);
                    visited[start.gx, start.gy] = true;
                }

                int[] dx = { 1, -1, 0, 0 };
                int[] dy = { 0, 0, 1, -1 };
                while (queue.Count > 0)
                {
                    var (gx, gy) = queue.Dequeue();
                    for (int d = 0; d < 4; d++)
                    {
                        int nx = gx + dx[d], ny = gy + dy[d];
                        if (nx < 0 || ny < 0 || nx >= dim || ny >= dim || visited[nx, ny]) continue;
                        if (!Walkable(GridToWorld(nx, ny))) continue;
                        visited[nx, ny] = true;
                        queue.Enqueue((nx, ny));
                    }
                }

                bool Reached(Vector2 p)
                {
                    var (gx, gy) = WorldToGrid(p);
                    // Small neighbourhood tolerance for grid quantization near footprint edges.
                    for (int ox = -2; ox <= 2; ox++)
                    for (int oy = -2; oy <= 2; oy++)
                    {
                        int nx = gx + ox, ny = gy + oy;
                        if (nx >= 0 && ny >= 0 && nx < dim && ny < dim && visited[nx, ny]) return true;
                    }
                    return false;
                }

                // The centre pile is a solid blocker centred on the origin (its authored
                // footprint, whatever Game.unity currently says), so (0,0) itself is never
                // walkable by construction — asserting on it would always
                // fail regardless of layout quality. What actually matters for gameplay is that
                // a chicken can WALK UP TO the centre pile to collect from it, so we assert its
                // approach ring is reachable instead.
                float approachRadius = FootprintRadius(CenterPileFootprint) + PinwheelLayout.ChickenRadius + 0.5f;
                bool centreApproachable = false;
                for (int step = 0; step < 72 && !centreApproachable; step++)
                {
                    float a = step * (Mathf.PI * 2f / 72f);
                    var probe = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * approachRadius;
                    if (Walkable(probe) && Reached(probe)) centreApproachable = true;
                }
                Assert.IsTrue(centreApproachable,
                    $"seed={seed}: centre pile not approachable from base 0's spawn " +
                    $"(no walkable point on the r={approachRadius:0.00} ring was reached)");
                for (int i = 1; i < corners.Length; i++)
                {
                    Vector2 spawnI = Vector2.Lerp(corners[i], Vector2.zero, SpawnInsetFraction);
                    Assert.IsTrue(Reached(spawnI), $"seed={seed}: base {i} not reachable from base 0's spawn");
                }
            }
        }
    }
}

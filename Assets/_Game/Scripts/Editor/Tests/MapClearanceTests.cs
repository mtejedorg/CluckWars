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
        // Mirrors Game.unity's MapGenerator field values (v0.5 spec).
        private const float ArenaHalfSize = 19f;      // _planeSize 38 / 2
        private const float WallThickness = 0.5f;
        private const int StandardObstacleCount = 8;
        private const float BaseCornerDistance = 19f;
        private const float BaseKeepClear = 4f;
        private const float SpawnInsetFraction = 0.15f; // MapGenerator.BaseInsetFraction
        private static readonly Vector2 CenterPileFootprint = new Vector2(12f, 10f);
        private static readonly Vector2 PersonalPileFootprint = new Vector2(5.5f, 4.6f);
        private static readonly Vector2 ContestedPileFootprint = new Vector2(6.5f, 5.4f);
        private const float PersonalPileInset = 0.3673f;
        private const float ContestedEdgeInset = 0.7895f;
        private const float PilePositionJitter = 1.5f;

        private static float FootprintRadius(Vector2 size) => 0.5f * Mathf.Sqrt(size.x * size.x + size.y * size.y);

        private struct Box
        {
            public Vector2 Center;
            public float Width;  // extent along the box's own local X (its "length")
            public float Depth;  // extent along local Z (its "thickness")
            public float YawRad; // rotation of local X away from world +X
        }

        private static Vector2[] Corners() => new[]
        {
            new Vector2(BaseCornerDistance, BaseCornerDistance),
            new Vector2(-BaseCornerDistance, BaseCornerDistance),
            new Vector2(-BaseCornerDistance, -BaseCornerDistance),
            new Vector2(BaseCornerDistance, -BaseCornerDistance),
        };

        /// <summary>Mirrors MapGenerator.ComputeBaseAndPileKeepClearDiscs.</summary>
        private static KeepClearDisc[] KeepClearDiscs(Vector2[] corners)
        {
            float personalRadius = FootprintRadius(PersonalPileFootprint) + PilePositionJitter + PinwheelLayout.PileArmBuffer;
            float contestedRadius = FootprintRadius(ContestedPileFootprint) + PilePositionJitter + PinwheelLayout.PileArmBuffer;
            var discs = new List<KeepClearDisc>();
            for (int i = 0; i < corners.Length; i++)
            {
                discs.Add(new KeepClearDisc(corners[i], BaseKeepClear));
                Vector2 personal = corners[i].normalized * 17f;
                discs.Add(new KeepClearDisc(personal, personalRadius));
                Vector2 mid = (corners[i] + corners[(i + 1) % corners.Length]) * 0.5f;
                Vector2 contested = mid.normalized * 15f;
                discs.Add(new KeepClearDisc(contested, contestedRadius));
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

            for (int seed = 0; seed < 25; seed++)
            {
                var segs = PinwheelLayout.Build(ArenaHalfSize, wedges, seed, centerKeepClear, armThickness, discs);

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
                Vector2 personal = corners[i].normalized * 17f;
                pileBoxes.Add(new Box { Center = personal, Width = PersonalPileFootprint.x, Depth = PersonalPileFootprint.y, YawRad = 0f });
                Vector2 mid = (corners[i] + corners[(i + 1) % corners.Length]) * 0.5f;
                Vector2 contested = mid.normalized * 15f;
                pileBoxes.Add(new Box { Center = contested, Width = ContestedPileFootprint.x, Depth = ContestedPileFootprint.y, YawRad = 0f });
            }

            int[] seeds = { 0, 1, 2, 17, 12345 };
            const float cell = 0.5f;
            int dim = Mathf.CeilToInt((ArenaHalfSize * 2f) / cell) + 1;

            foreach (int seed in seeds)
            {
                var segs = PinwheelLayout.Build(ArenaHalfSize, StandardObstacleCount, seed, centerKeepClear, WallThickness, discs);
                var allBoxes = new List<Box>(pileBoxes);
                allBoxes.AddRange(BuildWallBoxes(segs, WallThickness));

                bool Walkable(Vector2 p)
                {
                    if (Mathf.Abs(p.x) > ArenaHalfSize - 0.5f || Mathf.Abs(p.y) > ArenaHalfSize - 0.5f) return false;
                    foreach (var box in allBoxes)
                    {
                        if (DistanceToBoxSurface(p, box) < PinwheelLayout.ChickenRadius) return false;
                    }
                    return true;
                }

                Vector2 GridToWorld(int gx, int gy) => new Vector2(-ArenaHalfSize + gx * cell, -ArenaHalfSize + gy * cell);
                (int gx, int gy) WorldToGrid(Vector2 p) => (
                    Mathf.Clamp(Mathf.RoundToInt((p.x + ArenaHalfSize) / cell), 0, dim - 1),
                    Mathf.Clamp(Mathf.RoundToInt((p.y + ArenaHalfSize) / cell), 0, dim - 1));

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

                // The centre pile is a PERMANENT solid 7×4 blocker centred on the origin, so
                // (0,0) itself is never walkable by construction — asserting on it would always
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

using UnityEngine;

namespace CluckWars.Gameplay
{
    public struct WallSegment
    {
        public Vector2 A;
        public Vector2 B;
        public ObstacleClass Class;

        public WallSegment(Vector2 a, Vector2 b, ObstacleClass cls)
        {
            A = a;
            B = b;
            Class = cls;
        }
    }

    /// <summary>
    /// A circular no-build zone a pinwheel arm must be pushed clear of — a base, a food
    /// pile's worst-case jittered footprint, or anything else that must never fuse with a
    /// wall and seal a route.
    /// </summary>
    public readonly struct KeepClearDisc
    {
        public readonly Vector2 Center;
        public readonly float Radius;

        public KeepClearDisc(Vector2 center, float radius)
        {
            Center = center;
            Radius = radius;
        }
    }

    /// <summary>
    /// Builds the radial "pinwheel" wall geometry for the arena interior (ADR 0003
    /// Decision 5).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Original version only guaranteed arms stayed outside a centre disc and away from
    /// the four bases — it never guaranteed a chicken could actually fit through the gap
    /// between two adjacent arms. Near the hub, where arms start close to the centre, the
    /// angular gap between neighbours (<c>2π / wedges</c>, narrowed further by per-arm
    /// jitter) closes to a physical gap smaller than a chicken, walling the centre off.
    /// That was reported 2026-07-27 as "chickens won't even fit where walls converge."
    /// </para>
    /// <para>
    /// <see cref="Build"/> now solves for a "hub plaza" radius — the smallest radius at
    /// which two worst-case-adjacent arms leave <see cref="MinCorridorWidth"/> of clear
    /// space between their surfaces — and truncates every arm's inner end to at least that
    /// radius. It also accepts a list of extra keep-clear discs (food piles, in addition to
    /// the four bases) so an arm can never fuse with an objective and seal a route either.
    /// </para>
    /// </remarks>
    public static class PinwheelLayout
    {
        // ---- Measured chicken footprint -----------------------------------------------
        // Source: Assets/_Game/Prefabs/Chicken.prefab, CharacterController component
        // (m_Radius: 0.4, m_SkinWidth: 0.08). Rescaled per GDD 3.3 (⌀0.8 m × 1.6 m tall).
        public const float ChickenControllerRadius = 0.4f;
        public const float ChickenControllerSkinWidth = 0.08f;

        /// <summary>Effective collision radius — the CharacterController pushes back at this distance.</summary>
        public const float ChickenRadius = ChickenControllerRadius + ChickenControllerSkinWidth; // 0.48
        public const float ChickenDiameter = ChickenRadius * 2f; // 0.96

        /// <summary>
        /// Extra room, beyond two chicken-widths, so two chickens passing each other don't
        /// scrape shoulders or clip the walls.
        /// </summary>
        public const float PassingMargin = 0.4f;

        /// <summary>Nominal body diameter, excluding skin width — the ⌀0.8 m of GDD 3.3.</summary>
        public const float ChickenBodyDiameter = ChickenControllerRadius * 2f; // 0.8

        /// <summary>
        /// Minimum walkable width for any pinwheel corridor: 2 × chicken ⌀ + passing margin
        /// = 2.0 m per GDD 3.3. Kept DERIVED rather than hardcoded so a future chicken
        /// rescale carries through here automatically — a stale literal is exactly the
        /// staleness class of bug that produced the 2026-07-27 hub-pinch regression.
        /// </summary>
        public const float MinCorridorWidth = ChickenBodyDiameter * 2f + PassingMargin; // 2.0

        /// <summary>
        /// Extra clearance kept between a food pile's surface and the nearest arm.
        /// </summary>
        public const float PileArmBuffer = ChickenDiameter; // 0.96

        private const float JitterFraction = 0.15f;

        /// <param name="arenaHalfSize">Half the square arena's side length (19 m for 38 m arena).</param>
        /// <param name="wedges">Number of pinwheel arms (8 for v0.5 layout).</param>
        /// <param name="seed">RNG seed — identical on every peer for online matches.</param>
        /// <param name="centerKeepClear">Radius of the centre food pile's footprint.</param>
        /// <param name="armThickness">Physical thickness of arms (0.5 m).</param>
        /// <param name="extraKeepClearDiscs">Additional no-build discs (bases, food piles).</param>
        public static WallSegment[] Build(
            float arenaHalfSize,
            int wedges,
            int seed,
            float centerKeepClear,
            float armThickness,
            KeepClearDisc[] extraKeepClearDiscs = null)
        {
            if (wedges <= 0) return System.Array.Empty<WallSegment>();

            var segments = new WallSegment[wedges];
            var rng = new System.Random(seed);
            float angleStep = 2f * Mathf.PI / wedges;

            // 8-sector radial wall topology (GDD 3.4 & ag_spec Phase 1):
            // 8 radial walls sit on 45° sector boundaries (22.5°, 67.5°, 112.5°, ...).
            // Alternating openings:
            // - 4 Outer-gap walls (i % 2 == 0): span radius 10.0 m to 17.6 m (length 7.6 m, 3 m opening at outer rim).
            // - 4 Inner-gap walls (i % 2 == 1): span radius 12.0 m to boundary 20.6 m (length 8.6 m, 3 m opening at inner hub).
            for (int i = 0; i < wedges; i++)
            {
                // Wall bearings sit at 22.5° off each diagonal/axis
                float baseAngle = (i * 45f + 22.5f) * Mathf.Deg2Rad;
                float jitter = (wedges != 8) ? (float)(rng.NextDouble() * 2.0 - 1.0) * angleStep * JitterFraction : 0f;
                float angle = baseAngle + jitter;
                Vector2 dir = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));

                float startDist;
                float endDist;

                if (wedges == 8)
                {
                    // Boundary along a 22.5° bearing on a square of half-extent 19 m is r = 19 / cos 22.5° = 20.5647 m (20.6 m).
                    float boundaryRadius = arenaHalfSize / Mathf.Cos(22.5f * Mathf.Deg2Rad);

                    if (i % 2 == 0)
                    {
                        // Outer-gap wall: r 10 m -> 17.6 m (length 7.6 m, 3 m opening at rim)
                        startDist = 10.0f;
                        endDist = 17.6f;
                    }
                    else
                    {
                        // Inner-gap wall: r 12 m -> boundary (20.6 m) (length 8.6 m, 3 m opening at hub)
                        startDist = 12.0f;
                        endDist = boundaryRadius;
                    }
                }
                else
                {
                    startDist = 10.0f;
                    endDist = arenaHalfSize - 1.0f;
                }

                if (extraKeepClearDiscs != null)
                {
                    for (int d = 0; d < extraKeepClearDiscs.Length; d++)
                    {
                        var disc = extraKeepClearDiscs[d];
                        ClipAgainstDisc(dir, disc.Center, disc.Radius, ref startDist, ref endDist);
                    }
                }

                if (startDist >= endDist)
                {
                    startDist = endDist = Mathf.Max(0f, endDist);
                }

                Vector2 posA = dir * startDist;
                Vector2 posB = dir * endDist;

                segments[i] = new WallSegment(posA, posB, ObstacleClass.Standard);
            }

            return segments;
        }

        private static void ClipAgainstDisc(Vector2 dir, Vector2 discCenter, float discRadius, ref float startDist, ref float endDist)
        {
            if (discRadius <= 0f) return;

            float dot = Vector2.Dot(dir, discCenter);
            float c = discCenter.sqrMagnitude - discRadius * discRadius;
            float disc = dot * dot - c;
            if (disc <= 0f) return;

            float sqrtDisc = Mathf.Sqrt(disc);
            float t1 = dot - sqrtDisc;
            float t2 = dot + sqrtDisc;
            if (t2 <= 0f) return;

            float entry = Mathf.Max(0f, t1);
            if (entry > startDist && entry < endDist)
            {
                endDist = entry;
            }
            else if (entry <= startDist && t2 >= startDist)
            {
                startDist = t2;
            }
        }
    }
}

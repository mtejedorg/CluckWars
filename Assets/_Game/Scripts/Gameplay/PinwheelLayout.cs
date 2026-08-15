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

        /// <summary>
        /// Walkable plaza radius around the centre pile (GDD 3.1). Used as a floor: the
        /// plaza always leaves at least <see cref="MinCorridorWidth"/> of ring between the
        /// centre pile's footprint and the innermost wall, so growing the centre pile can
        /// never seal the hub.
        /// <para>
        /// Scaled 10 -> 13.5 (x1.35) with the arena on 2026-08-14. This is ARENA geometry,
        /// not chicken geometry: it must keep the same share of the map, unlike
        /// <see cref="MinCorridorWidth"/> and friends above, which are derived from the
        /// chicken's footprint and are deliberately left alone — the chicken did not grow.
        /// </para>
        /// </summary>
        public const float HubPlazaRadius = 13.5f;

        /// <summary>
        /// Width of the single opening each wall carries (GDD 3.4). Scaled 3 -> 4.05 (x1.35)
        /// with the arena — it is a fraction of a wall's length, not a chicken clearance.
        /// </summary>
        public const float OpeningWidth = 4.05f;

        /// <summary>
        /// Angular jitter applied to wall bearings, as a fraction of the sector step.
        /// <b>Deliberately zero.</b> Piles sit at sector centres and walls on sector
        /// boundaries — the maximum separation the topology allows — and at the contested
        /// pile's radius that is only 5.74 m against a 5.59 m keep-clear disc, i.e. ~0.15 m
        /// of slack. Measured: any wall jitter swings a wall into the pile disc, which
        /// clips it away; at the old 0.15 the inner walls were shredded from 7.6 m down to
        /// 2.7 m and 27% of all walls collapsed entirely. Match-to-match variety comes from
        /// pile jitter instead; deterministic walls also make the map learnable.
        /// If this is ever raised, re-run the wall-length sweep — the tests alone will not
        /// catch it, because clipping shortens walls without violating clearance.
        /// </summary>
        private const float JitterFraction = 0f;

        /// <param name="arenaHalfSize">Half the square arena's side length (25.65 m for the 51.3 m arena).</param>
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

            // Radial wall topology (GDD 3.4). Walls sit on sector BOUNDARIES — offset half
            // a step from the sector centres — and alternate which end carries their single
            // opening: even indices open at the rim (the safe lap), odd indices open at the
            // hub (the risky lap). With a wedge count divisible by 4 this is 4-fold
            // symmetric, so every player's sector is identical.
            //
            // Everything below is DERIVED from arenaHalfSize / centerKeepClear rather than
            // hardcoded. An earlier revision special-cased wedges == 8 with literal radii
            // and a fixed 45° bearing, which silently produced OVERLAPPING walls for any
            // other count (at 13 wedges, wall 0 and wall 8 landed on the same bearing) and
            // ignored centerKeepClear entirely.
            float hubPlaza = Mathf.Max(HubPlazaRadius, centerKeepClear + MinCorridorWidth);

            // Jitter must repeat with 4-FOLD PERIOD, not vary per wall. A 90° rotation maps
            // wall i onto wall i + wedges/4, so those must share a jitter value or the four
            // players' sectors stop being identical — which is a fairness bug in a 4-player
            // FFA, not a cosmetic one. Only `period` independent values are drawn; every
            // quadrant then reuses them.
            int period = (wedges % 4 == 0) ? wedges / 4 : wedges;
            var jitters = new float[period];
            for (int j = 0; j < period; j++)
                jitters[j] = (float)(rng.NextDouble() * 2.0 - 1.0) * angleStep * JitterFraction;

            for (int i = 0; i < wedges; i++)
            {
                // Half-step offset puts the wall on the boundary between two sectors.
                float baseAngle = (i + 0.5f) * angleStep;
                float jitter = jitters[i % period];
                float angle = baseAngle + jitter;
                Vector2 dir = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));

                // Distance to the square boundary along this bearing.
                float boundaryRadius = arenaHalfSize /
                    Mathf.Max(Mathf.Abs(dir.x), Mathf.Abs(dir.y));

                float startDist;
                float endDist;
                if (i % 2 == 0)
                {
                    // Rim opening: wall stops OpeningWidth short of the boundary.
                    startDist = hubPlaza;
                    endDist   = boundaryRadius - OpeningWidth;
                }
                else
                {
                    // Hub opening: wall starts OpeningWidth beyond the plaza edge.
                    startDist = hubPlaza + OpeningWidth;
                    endDist   = boundaryRadius;
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

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
        // (m_Radius: 0.5, m_SkinWidth: 0.08). Verified directly against the prefab
        // 2026-07-27 — matches the figure already load-bearing in FoodPile.cs's
        // collect-reach comment ("capsule radius 0.5 + controller skin 0.08").
        public const float ChickenControllerRadius = 0.5f;
        public const float ChickenControllerSkinWidth = 0.08f;

        /// <summary>Effective collision radius — the CharacterController pushes back at this distance.</summary>
        public const float ChickenRadius = ChickenControllerRadius + ChickenControllerSkinWidth; // 0.58
        public const float ChickenDiameter = ChickenRadius * 2f; // 1.16

        /// <summary>
        /// Extra room, beyond two chicken-widths, so two chickens passing each other don't
        /// scrape shoulders or clip the walls — covers CharacterController skin slop and
        /// ordinary steering imprecision (bot pathing, joystick drift).
        /// </summary>
        public const float PassingMargin = 0.4f;

        /// <summary>
        /// Minimum walkable width for any pinwheel corridor. Cluck Wars is a 4-player FFA
        /// built around chasing and stealing — a lane that only barely admits one chicken
        /// single-file at a pinch point is a design failure, not just a rough edge. Sized
        /// for two chickens to pass abreast: two diameters plus <see cref="PassingMargin"/>.
        /// </summary>
        public const float MinCorridorWidth = ChickenDiameter * 2f + PassingMargin; // 2.72m

        /// <summary>
        /// Extra clearance kept between a food pile's surface and the nearest arm, so a
        /// stocked (solid) pile can never physically fuse with a wall. Deliberately smaller
        /// than <see cref="MinCorridorWidth"/> — a pile is meant to be routed AROUND (using
        /// the open ground the pinwheel already leaves elsewhere), not to have a full
        /// two-abreast lane reserved on every side of it. One chicken-width is enough to
        /// guarantee "never sealed" in practice — verified empirically by the map
        /// reachability/clearance regression tests, not just asserted by this margin's
        /// existence.
        /// </summary>
        public const float PileArmBuffer = ChickenDiameter;

        // Matches the per-arm jitter applied below: each arm's angle can drift up to this
        // fraction of the nominal angle step, in either direction.
        private const float JitterFraction = 0.15f;

        /// <param name="arenaHalfSize">Half the square arena's side length.</param>
        /// <param name="wedges">Number of pinwheel arms.</param>
        /// <param name="seed">RNG seed — identical on every peer for online matches.</param>
        /// <param name="centerKeepClear">
        /// Radius of the centre food pile's footprint. Arms are kept at least this far
        /// (+ a small margin) from the origin regardless of the hub-plaza calculation below,
        /// so a pile bigger than the plaza itself is still respected.
        /// </param>
        /// <param name="armThickness">
        /// Physical thickness every arm will be built with (<c>MapGenerator</c> applies this
        /// uniformly regardless of the segment's <see cref="ObstacleClass"/>). Required here
        /// because the hub-plaza radius is a function of it — a thicker arm needs a bigger
        /// plaza to leave the same corridor width.
        /// </param>
        /// <param name="extraKeepClearDiscs">
        /// Additional no-build discs (bases, food piles) an arm must be pushed clear of.
        /// May be null.
        /// </param>
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

            // Worst-case angular gap between two adjacent arms: each arm's angle can jitter
            // toward its neighbour by up to JitterFraction of the step, so two neighbours can
            // close the nominal gap between them by up to 2 * JitterFraction of the step.
            // With a single wedge there is no neighbour to pinch against.
            float minGapAngle = wedges > 1 ? angleStep * (1f - 2f * JitterFraction) : 0f;

            // Radius at which two arms separated by minGapAngle, each armThickness wide,
            // leave MinCorridorWidth of clear walking space between their surfaces.
            // Derived from the chord length between two same-radius points on the two arms'
            // centrelines, 2 * r * sin(minGapAngle / 2): solve
            //     2r * sin(θ/2) - armThickness == MinCorridorWidth
            // for r. The chord distance is a safe (slightly conservative) lower bound on the
            // true surface gap: each arm's thickness is measured perpendicular to ITS OWN
            // length, not to the chord, so the real encroachment on the chord is
            // <= armThickness. Chord length grows monotonically with r for a fixed angle, so
            // guaranteeing the corridor at this radius guarantees it for the rest of both
            // arms' length too — the pinch, if any, is always at the innermost point.
            float halfGap = minGapAngle * 0.5f;
            float sinHalfGap = Mathf.Sin(halfGap);
            float hubPlazaRadius = sinHalfGap > 0.0001f
                ? (MinCorridorWidth + armThickness) / (2f * sinHalfGap)
                : 0f;

            for (int i = 0; i < wedges; i++)
            {
                float baseAngle = i * angleStep;
                float jitter = (float)(rng.NextDouble() * 2.0 - 1.0) * angleStep * JitterFraction;
                float angle = baseAngle + jitter;
                Vector2 dir = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));

                float startDist = 0f;
                float endDist = arenaHalfSize - 1f; // stay inside the perimeter wall

                ClipAgainstDisc(dir, Vector2.zero, centerKeepClear + 0.2f, ref startDist, ref endDist);
                if (extraKeepClearDiscs != null)
                {
                    for (int d = 0; d < extraKeepClearDiscs.Length; d++)
                    {
                        var disc = extraKeepClearDiscs[d];
                        ClipAgainstDisc(dir, disc.Center, disc.Radius, ref startDist, ref endDist);
                    }
                }

                // Enforce the hub plaza floor regardless of what the disc clipping produced —
                // this is what actually fixes the convergence pinch.
                startDist = Mathf.Max(startDist, hubPlazaRadius);

                if (startDist >= endDist)
                {
                    // No room for a corridor-compliant arm in this direction. Collapse to a
                    // zero-length segment (MapGenerator then builds a wall with zero extent
                    // along its own length — effectively nothing) rather than the old
                    // behaviour of forcing a short stub that violated the very clearance this
                    // method exists to guarantee.
                    startDist = endDist = Mathf.Max(0f, endDist);
                }

                Vector2 posA = dir * startDist;
                Vector2 posB = dir * endDist;

                // Assign ObstacleClass in a seeded pattern: alternate Low, Standard, and
                // occasional Tall.
                double rVal = rng.NextDouble();
                ObstacleClass cls;
                if (rVal < 0.4) cls = ObstacleClass.Low;
                else if (rVal < 0.8) cls = ObstacleClass.Standard;
                else cls = ObstacleClass.Tall;

                segments[i] = new WallSegment(posA, posB, cls);
            }

            return segments;
        }

        /// <summary>
        /// Ray/disc clip along <paramref name="dir"/> from the origin: if the disc centred at
        /// <paramref name="discCenter"/> with <paramref name="discRadius"/> overlaps
        /// [<paramref name="startDist"/>, <paramref name="endDist"/>], shortens the segment
        /// from whichever end the disc encroaches on.
        /// </summary>
        private static void ClipAgainstDisc(Vector2 dir, Vector2 discCenter, float discRadius, ref float startDist, ref float endDist)
        {
            if (discRadius <= 0f) return;

            // Ray: P(t) = t * dir. Intersection with the circle centred at discCenter:
            // |t*dir - discCenter|^2 = discRadius^2  =>  t^2 - 2*t*(dir.discCenter) + |discCenter|^2 - discRadius^2 = 0
            float dot = Vector2.Dot(dir, discCenter);
            float c = discCenter.sqrMagnitude - discRadius * discRadius;
            float disc = dot * dot - c;
            if (disc <= 0f) return;

            float sqrtDisc = Mathf.Sqrt(disc);
            float t1 = dot - sqrtDisc;
            float t2 = dot + sqrtDisc;
            if (t2 <= 0f) return; // disc is entirely behind the ray origin

            float entry = Mathf.Max(0f, t1);
            if (entry > startDist && entry < endDist)
            {
                // Disc sits partway along the segment — stop the arm before it.
                endDist = entry;
            }
            else if (entry <= startDist && t2 >= startDist)
            {
                // Disc covers the segment's current start — push the start out past it.
                startDist = t2;
            }
        }
    }
}

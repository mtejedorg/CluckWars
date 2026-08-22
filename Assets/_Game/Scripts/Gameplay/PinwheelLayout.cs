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
    /// Builds the radial "pinwheel" wall geometry for the arena interior (GDD §3.4).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Original version only guaranteed arms stayed outside a centre disc and away from
    /// the four bases — it never guaranteed a chicken could actually fit through the gap
    /// between two adjacent arms. Near the hub, where arms start close to the centre, the
    /// angular gap between neighbours (<c>2π / wedges</c>) closes to a physical gap smaller
    /// than a chicken, walling the centre off. That was reported 2026-07-27 as "chickens
    /// won't even fit where walls converge."
    /// </para>
    /// <para>
    /// <see cref="Build"/> now solves for a "hub plaza" radius — see
    /// <see cref="SolveHubPlazaRadius"/>, which is a real three-term solve, not a floor —
    /// and truncates every arm's inner end to at least that radius. It also accepts a list
    /// of extra keep-clear discs (food piles, in addition to the four bases) so an arm can
    /// never fuse with an objective and seal a route either.
    /// </para>
    ///
    /// <para><b>Wedge count is a proven global optimum at 8. Do not treat it as a density
    /// lever.</b></para>
    ///
    /// <para>
    /// Wall bearings are <c>(i + 0.5)·360/W</c>; pile and base bearings are fixed at
    /// multiples of 45° (piles sit at sector centres, bases on the corner diagonals). The
    /// angular offset between a wall and the nearest objective is therefore a pure function
    /// of W, and the binding constraint is the contested pile: its keep-clear disc is
    /// 4.0936 m at r = 20.2507, so a wall must sit at least
    /// <c>asin(4.0936 / 20.2507) = 11.665°</c> off the objective bearing or it is clipped away.
    /// </para>
    ///
    /// <list type="table">
    ///   <listheader><term>W</term><description>best achievable offset from a 45° multiple</description></listheader>
    ///   <item><term>8</term><description><b>22.500° — the maximum the topology allows.</b> Clears the 11.665° requirement by 10.8°.</description></item>
    ///   <item><term>12</term><description>0.000° — four arms land EXACTLY on the corner diagonals.</description></item>
    ///   <item><term>16</term><description>11.250° — misses the 11.665° requirement by 0.415°.</description></item>
    ///   <item><term>20</term><description>0.000° — four arms land EXACTLY on the corner diagonals.</description></item>
    /// </list>
    ///
    /// <para>
    /// W = 8 is the only count that both maximises the offset and keeps every arm off an
    /// objective bearing. Raising W to add interior wall length does not work: the arms get
    /// clipped or deleted by the very objectives they are meant to route around, and the
    /// length gained is lost again. <b>Add density with scatter cover
    /// (<see cref="SectorScatter"/>), not with wedges.</b> Arm LENGTH is tuned through
    /// <see cref="HubPlazaRadius"/> and <see cref="OpeningWidth"/>, both of which are
    /// derived quantities with a stated derivation — see their docs before touching them.
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

        /// <summary>
        /// The XZ box a chicken's centre may occupy: the wall's inner face pulled in by the
        /// body radius, so the body rests against the surface instead of sinking into it.
        /// </summary>
        /// <remarks>
        /// Extracted from <c>ChickenMovement.ClampInsideArena</c> so the arithmetic that
        /// guarantees containment can be tested without a <c>CharacterController</c>, a scene
        /// or a physics step. The runtime path is entangled with Unity objects; this is not,
        /// and this is the half that can be wrong silently.
        /// </remarks>
        public static float ArenaClampLimit(float arenaHalfSize) =>
            arenaHalfSize - ChickenRadius;

        /// <summary>
        /// Clamps an XZ position into <see cref="ArenaClampLimit"/>. Y is returned untouched —
        /// vertical movement is gravity and jump arcs, which the arena bounds do not govern.
        /// A non-positive limit (a degenerate arena) returns the input unchanged rather than
        /// collapsing everything onto the origin.
        /// </summary>
        public static Vector3 ClampIntoArena(Vector3 position, float arenaHalfSize)
        {
            float limit = ArenaClampLimit(arenaHalfSize);
            if (limit <= 0f) return position;

            return new Vector3(
                Mathf.Clamp(position.x, -limit, limit),
                position.y,
                Mathf.Clamp(position.z, -limit, limit));
        }

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
        /// Walkable plaza radius around the centre pile (GDD §3.1, which tabulates "hub
        /// plaza edge" at 10). Used as a floor by <see cref="SolveHubPlazaRadius"/>.
        /// <para>
        /// <b>Reverted 13.5 → 10.0 on 2026-08-19.</b> The 2026-08-14 arena rescale scaled
        /// this x1.35 on the grounds that it was "ARENA geometry, not chicken geometry: it
        /// must keep the same share of the map". <b>That reasoning was wrong.</b> This value
        /// is only ever consumed as one term of
        /// <c>max(HubPlazaRadius, centreKeepClear + MinCorridorWidth + halfThickness, …)</c>,
        /// and <i>both</i> of the other terms are NON-ARENA quantities: <c>centreKeepClear</c>
        /// is the centre pile's footprint (deliberately HELD at absolute size through the
        /// rescale — Maestro: "don't increase the Pile size for now") and
        /// <see cref="MinCorridorWidth"/> is the chicken. A plaza sized as a share of the map
        /// while everything it is supposed to clear stayed absolute is a plaza sized against
        /// nothing — it just ate 3.5 m off the inner end of every arm, which is half of why
        /// the walls stopped reading on the enlarged map.
        /// </para>
        /// </summary>
        public const float HubPlazaRadius = 10.0f;

        /// <summary>
        /// Width of the single opening each wall carries (GDD §3.3's scale package: "Wall
        /// opening 3.0 m", listed one row under "Min corridor 2.0 m").
        /// <para>
        /// <b>Reverted 4.05 → 3.0 on 2026-08-19.</b> The 2026-08-14 rescale scaled this
        /// x1.35 on the grounds that "it is a fraction of a wall's length, not a chicken
        /// clearance". <b>That is backwards.</b> An opening is a chokepoint a chicken squeezes
        /// through: 3.0 m is exactly 1.5 × <see cref="MinCorridorWidth"/> — one chicken plus
        /// half again, i.e. a gap you commit to. 4.05 m is 2.03 × MinCorridorWidth, which is
        /// two chickens abreast plus a full body of slack, i.e. not a chokepoint at all. The
        /// opening never referenced wall length in any derivation; GDD §3.3 lists it in the
        /// chicken-clearance block.
        /// </para>
        /// </summary>
        public const float OpeningWidth = 3.0f;

        /// <summary>
        /// Angular jitter applied to wall bearings, as a fraction of the sector step.
        /// <b>Deliberately zero.</b> Piles sit at sector centres and walls on sector
        /// boundaries — the maximum separation the topology allows (22.5°, see the class
        /// remarks). At the contested pile's radius (20.2507 m) that puts the wall LINE
        /// 7.7496 m from the pile centre against a 4.0936 m keep-clear disc, i.e.
        /// <b>+3.656 m of slack</b>.
        /// <para>
        /// <b>Corrected 2026-08-19.</b> This comment (and GDD §3.4) previously claimed
        /// ~0.15 m of slack. That figure was computed against the PRE-SHRINK contested
        /// footprint (6.5 × 5.4, disc 5.585) at the pre-rescale radius (15 m, separation
        /// 5.740) — a map that has not shipped since 2026-08-13. Against what actually ships
        /// the margin is 24× larger.
        /// </para>
        /// <para>
        /// Jitter nevertheless stays at zero, and the reason is now the honest one rather
        /// than the arithmetic one: a fixed wall layout makes the map <b>learnable</b>, which
        /// GDD §3.4's "two laps" rule depends on ("left is the safe lap, right is the risky
        /// lap" is only a rule if it is the same every match). Match-to-match variety comes
        /// from pile jitter. If this is ever raised, re-run the wall-length sweep —
        /// <c>PinwheelLayoutTests.Build_WallsSurvivePileKeepClearDiscs_AtRealV05Sizes</c> is
        /// the only guard, because clipping shortens walls without violating clearance.
        /// </para>
        /// </summary>
        private const float JitterFraction = 0f;

        /// <summary>
        /// Smallest radius at which the pinwheel may start. Three independent constraints,
        /// whichever binds:
        /// <list type="number">
        ///   <item><see cref="HubPlazaRadius"/> — the authored plaza of GDD §3.1.</item>
        ///   <item>
        ///     <paramref name="centerKeepClear"/> + <see cref="MinCorridorWidth"/> +
        ///     half the arm thickness — a full walkable ring between the centre pile's
        ///     SURFACE and the arm's inner FACE, so growing the centre pile can never seal
        ///     the hub. The half-thickness term is what makes this a surface-to-surface
        ///     measurement rather than surface-to-centreline.
        ///   </item>
        ///   <item>
        ///     <c>(MinCorridorWidth + armThickness) / (2·sin(π/wedges))</c> — the radius at
        ///     which two ADJACENT arms' surfaces are still <see cref="MinCorridorWidth"/>
        ///     apart. Arms are radial, so their separation grows linearly with radius; below
        ///     this the pinwheel pinches shut. Binds only at high wedge counts (it is
        ///     3.53 m at W=8/t=0.7, but 10.87 m at W=20/t=1.4).
        ///   </item>
        /// </list>
        /// </summary>
        /// <remarks>
        /// Terms 2 and 3 are why <paramref name="armThickness"/> exists. Until 2026-08-19 it
        /// was accepted by <see cref="Build"/> and never read, while a comment in
        /// <c>MapGenerator.BuildInteriorObstacles</c> asserted that "the hub-plaza radius
        /// scales with whatever thickness is passed in" — it did not, so a thicker arm ate
        /// silently into the clearance both terms are supposed to guarantee.
        /// </remarks>
        public static float SolveHubPlazaRadius(float centerKeepClear, float armThickness, int wedges)
        {
            float fromCentrePile = centerKeepClear + MinCorridorWidth + armThickness * 0.5f;

            float fromAdjacentArms = 0f;
            if (wedges > 1)
            {
                float halfStep = Mathf.PI / wedges;
                fromAdjacentArms = (MinCorridorWidth + armThickness) / (2f * Mathf.Sin(halfStep));
            }

            return Mathf.Max(HubPlazaRadius, Mathf.Max(fromCentrePile, fromAdjacentArms));
        }

        /// <param name="arenaHalfSize">Half the square arena's side length (25.65 m for the 51.3 m arena).</param>
        /// <param name="wedges">
        /// Number of pinwheel arms. <b>8, and provably so</b> — see the class remarks for the
        /// bearing-alignment table. Other counts still build correct geometry (the tests sweep
        /// 4/6/8/10/13/16/20), they are just worse maps.
        /// </param>
        /// <param name="seed">RNG seed — identical on every peer for online matches.</param>
        /// <param name="centerKeepClear">Radius of the centre food pile's footprint.</param>
        /// <param name="armThickness">Physical thickness of arms. Consumed by <see cref="SolveHubPlazaRadius"/>.</param>
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
            float hubPlaza = SolveHubPlazaRadius(centerKeepClear, armThickness, wedges);

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

using System;
using UnityEngine;

namespace CluckWars.Gameplay
{
    /// <summary>
    /// Jump length tiers for teleport jump traversal (GDD v0.5 Section 3.5).
    /// </summary>
    public enum JumpLengthTier : byte
    {
        None   = 0,
        Short  = 1, // 3.25 m (Dive Bomb) - crosses walls, no pile
        Normal = 2, // 6.5 m  - crosses T1/T2, not the centre pile
        Big    = 3, // 11.7 m (Doppelganger) - crosses everything
    }

    /// <summary>
    /// Try-pattern query for the distance along a jump ray at which the first blocking
    /// surface is met. Declared as a named delegate rather than a <see cref="Func{T,TResult}"/>
    /// because <c>Func</c> cannot carry an <c>out</c> parameter.
    /// </summary>
    /// <param name="hitDistance">Distance from <paramref name="origin"/> to the near face.</param>
    /// <returns>True when something was hit within <paramref name="maxDistance"/>.</returns>
    public delegate bool NearFaceQuery(
        Vector3 origin,
        Vector3 direction,
        float maxDistance,
        float clearance,
        out float hitDistance);

    /// <summary>
    /// Result of a teleport jump resolution by <see cref="JumpResolver"/>.
    /// </summary>
    public struct JumpResult
    {
        public Vector3 LandingPoint;
        public bool Cleared;
        public float TargetDistance;
        public float EffectiveDistance;
    }

    /// <summary>
    /// Standalone, Unity-independent static resolver for length-based teleport jump traversal.
    /// Pure logic — EditMode testable without a scene scene or physics.
    /// </summary>
    public static class JumpResolver
    {
        // RESCALED x0.65 on 2026-08-20 to restore the traversal ladder. Was 5 / 10 / 18.
        //
        // These tiers are OBSTACLE-registered: the ladder's whole definition is which obstacle
        // each rung can cross (GDD 3.6 - "the map opens itself in stages"). The pile footprints
        // shrank 35% on 2026-08-13 and these did not follow, so the ladder had silently
        // collapsed - measured against the SHIPPED footprints (3.6x3.0 / 4.2x3.5 / 7.8x6.5) and
        // the real clearing rule (span + 2*BodyClearance, plus GetMaxExtension):
        //
        //   tier        arm 1.50   T1 4.40   T2 5.00   centre 8.60
        //   Short  5.0     yes       YES       YES         no      <- should fail both piles
        //   Normal 10.0    yes       yes       yes         YES     <- should fail the centre
        //   Big    18.0    yes       yes       yes         yes
        //
        // Short was crossing both small piles and Normal was crossing the centre pile on its
        // short axis, so every tier did what the tier above it was supposed to. Maestro,
        // 2026-08-20: "the jump is still too high."
        //
        //   tier        arm 1.50   T1 4.40   T2 5.00   centre 8.60
        //   Short  3.25    yes        no        no         no
        //   Normal 6.5     yes       yes       yes         no
        //   Big    11.7    yes       yes       yes        yes
        //
        // x0.65 is the piles' own shrink factor, so this restores the RELATIONSHIPS rather than
        // inventing new distances - the ladder is a property of tier-vs-obstacle, and only one
        // half of it had moved.
        //
        // If the saved x1.35 pile footprints are ever switched back on (see
        // MapGenerator._centerPileFootprint), rescale these in the SAME commit.
        // JumpResolverTests asserts these gates against the LIVE footprints, not literals.
        public const float ShortDistance  = 3.25f;
        public const float NormalDistance = 6.5f;
        public const float BigDistance    = 11.7f;

        /// <summary>
        /// Body clearance each side of a jump corridor (span needed = obstacle width + 0.8 m).
        /// </summary>
        /// <remarks>
        /// Derived, not a literal. This is the CharacterController's own radius WITHOUT skin
        /// width, so it is deliberately 0.08 m more permissive than
        /// <see cref="PinwheelLayout.ChickenRadius"/>, which <c>ChickenMovement.ClampInsideArena</c>
        /// uses as the arena limit. A jump may therefore land fractionally closer to a wall than
        /// the clamp allows and be nudged out on the next tick — harmless, and the clamp is the
        /// one that is right, because skin width is where the body actually stops.
        ///
        /// Pointing both at <see cref="PinwheelLayout.ChickenControllerRadius"/> keeps them one
        /// quantity with two definitions rather than two literals that agree by coincidence —
        /// the failure mode that produced the pile-position bug on 2026-08-14.
        /// </remarks>
        public const float BodyClearance        = PinwheelLayout.ChickenControllerRadius;
        public const float MaxAbsoluteTolerance = 0.8f;
        public const float MinToleranceFraction = 0.4f;
        public const float ToleranceFraction    = 0.12f;

        public static float GetNominalDistance(JumpLengthTier tier) => tier switch
        {
            JumpLengthTier.Short  => ShortDistance,
            JumpLengthTier.Normal => NormalDistance,
            JumpLengthTier.Big    => BigDistance,
            _                     => 0f,
        };

        /// <summary>
        /// Tolerance extension: max(12%, 0.4m) capped at 0.8m absolute.
        /// </summary>
        public static float GetMaxExtension(float nominalDistance)
        {
            float tolFraction = ToleranceFraction * nominalDistance;
            float tol = Mathf.Max(MinToleranceFraction, tolFraction);
            return Mathf.Min(MaxAbsoluteTolerance, tol);
        }

        public static bool IsInsideArena(Vector3 point, float arenaHalfSize, float clearance = BodyClearance)
        {
            float limit = arenaHalfSize - clearance;
            return Mathf.Abs(point.x) <= limit && Mathf.Abs(point.z) <= limit;
        }

        /// <summary>
        /// Resolves a teleport jump.
        /// </summary>
        /// <param name="arenaHalfSize">
        /// Half the arena's side length — landings outside it are rejected. <b>Required.</b>
        /// It used to default to a literal 19.0f, which went stale the moment the arena was
        /// rescaled (x1.35 on 2026-08-14) and would have silently confined every jump to the
        /// old, smaller square. A C# default must be a compile-time constant, so it cannot
        /// track <see cref="MapGenerator.ArenaHalfSize"/>; forcing callers to pass the live
        /// value is the only way this cannot rot again.
        /// </param>
        public static JumpResult Resolve(
            Vector3 origin,
            Vector3 direction,
            float nominalDistance,
            float arenaHalfSize,
            float bodyClearance = BodyClearance,
            Func<Vector3, float, bool> isPointBlocked = null,
            NearFaceQuery nearFaceDistance = null)
        {
            if (direction.sqrMagnitude < 0.0001f) direction = Vector3.forward;
            else direction = direction.normalized;

            float maxExt = GetMaxExtension(nominalDistance);
            bool cleared = false;
            float chosenDist = nominalDistance;

            // Check nominal distance first, then extended distances up to nominal + maxExt
            const int steps = 10;
            for (int i = 0; i <= steps; i++)
            {
                float testDist = nominalDistance + (maxExt * i / steps);
                Vector3 candidatePoint = origin + direction * testDist;

                bool inside = IsInsideArena(candidatePoint, arenaHalfSize, bodyClearance);
                bool blocked = isPointBlocked != null && isPointBlocked(candidatePoint, bodyClearance);

                if (inside && !blocked)
                {
                    cleared = true;
                    chosenDist = testDist;
                    break;
                }
            }

            if (cleared)
            {
                return new JumpResult
                {
                    LandingPoint = origin + direction * chosenDist,
                    Cleared = true,
                    TargetDistance = nominalDistance,
                    EffectiveDistance = chosenDist
                };
            }

            // Failure: travel as far as possible and stop flush against near face
            float nearFace = nominalDistance;
            if (nearFaceDistance != null && nearFaceDistance(origin, direction, nominalDistance, bodyClearance, out float hitDist))
            {
                nearFace = Mathf.Max(0f, hitDist - bodyClearance);
            }
            else
            {
                // Fallback: check arena boundary or obstacle line
                float boundaryLimit = arenaHalfSize - bodyClearance;
                float tx = direction.x > 0 ? (boundaryLimit - origin.x) / direction.x : (direction.x < 0 ? (-boundaryLimit - origin.x) / direction.x : float.MaxValue);
                float tz = direction.z > 0 ? (boundaryLimit - origin.z) / direction.z : (direction.z < 0 ? (-boundaryLimit - origin.z) / direction.z : float.MaxValue);
                float tBound = Mathf.Min(tx, tz);
                if (tBound > 0 && tBound < nominalDistance)
                {
                    nearFace = tBound;
                }
            }

            return new JumpResult
            {
                LandingPoint = origin + direction * nearFace,
                Cleared = false,
                TargetDistance = nominalDistance,
                EffectiveDistance = nearFace
            };
        }
    }
}

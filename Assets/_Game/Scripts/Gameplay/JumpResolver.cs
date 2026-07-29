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
        Short  = 1, // 5 m (Flying Peck)
        Normal = 2, // 10 m
        Big    = 3, // 18 m (Doppelganger)
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
        public const float ShortDistance  = 5.0f;
        public const float NormalDistance = 10.0f;
        public const float BigDistance    = 18.0f;

        /// <summary>0.4 m body clearance each side (span needed = obstacle width + 0.8 m).</summary>
        public const float BodyClearance        = 0.4f;
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
        public static JumpResult Resolve(
            Vector3 origin,
            Vector3 direction,
            float nominalDistance,
            float arenaHalfSize = 19.0f,
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

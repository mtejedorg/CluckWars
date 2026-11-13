using UnityEngine;

namespace CluckWars
{
    /// <summary>
    /// Ground-plane (XZ) vector maths, in one place (A20). The match is played on a flat arena: reach, range, aim and
    /// knockback are all measured ignoring height, because a chicken's pivot floats at capsule mid-height and a jump or
    /// a model scale must never change who is "in range".
    /// </summary>
    /// <remarks>
    /// Lives in <c>CluckWars.Core</c> and in the root namespace so every layer can call it without a using. Every
    /// planar distance, flatten and "direction away from" in gameplay and presentation code goes through here; do not
    /// write another private <c>PlanarDistance</c>. The expressions are the ones the copies used
    /// (<c>dx * dx + dz * dz</c>), so replacing a copy with a call does not change a result.
    /// </remarks>
    public static class Planar
    {
        /// <summary>Below this squared length (m^2) two points count as the same spot for <see cref="DirectionOr"/>.</summary>
        public const float CoincidentSqr = 0.001f;

        /// <summary><paramref name="v"/> with its height removed.</summary>
        public static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);

        /// <summary>XZ-flattened, normalised forward. Falls back to world forward for a zero-length input so callers never divide by zero.</summary>
        public static Vector3 Forward(Vector3 forward)
        {
            Vector3 flat = Flat(forward);
            return flat.sqrMagnitude < 0.0001f ? Vector3.forward : flat.normalized;
        }

        /// <summary>Squared XZ distance between two points.</summary>
        public static float SqrDistance(Vector3 a, Vector3 b)
        {
            float dx = a.x - b.x;
            float dz = a.z - b.z;
            return dx * dx + dz * dz;
        }

        /// <summary>XZ distance between two points.</summary>
        public static float Distance(Vector3 a, Vector3 b) => Mathf.Sqrt(SqrDistance(a, b));

        /// <summary>
        /// The unit ground direction from <paramref name="from"/> to <paramref name="to"/>; when the two are on the same
        /// spot (closer than <see cref="CoincidentSqr"/>), <paramref name="fallback"/> normalized as it is given (not
        /// flattened: the callers pass a chicken's forward). The "push away from the caster" direction of every shove.
        /// </summary>
        public static Vector3 DirectionOr(Vector3 from, Vector3 to, Vector3 fallback)
        {
            Vector3 dir = to - from;
            dir.y = 0f;
            if (dir.sqrMagnitude < CoincidentSqr) dir = fallback;
            return dir.normalized;
        }
    }
}

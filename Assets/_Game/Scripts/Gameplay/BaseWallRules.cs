using UnityEngine;

namespace CluckWars.Gameplay
{
    /// <summary>
    /// No ability crosses a claimed base's wall, in either direction. Pure: <c>PlayerBase.WallBetween</c> gathers the claimed
    /// bases and calls this; the eligibility path (<c>AbilityBaseSO.WouldAffect</c>) and the zone and receiver paths share it.
    /// </summary>
    /// <remarks>
    /// <b>One definition of "inside".</b> <see cref="IsInside"/> stops <see cref="InsideSlack"/> short of the keep-out radius, so a
    /// rival clamped onto the wall (and a proxy of it, whose interpolated path cuts a chord just under the radius) always reads
    /// outside and never flickers in a telegraph. An owner counts as home once it is that far in.
    /// Same-base pairs are not blocked: exactly one of the two must be inside.
    /// </remarks>
    public static class BaseWallRules
    {
        /// <summary>How far past the keep-out radius "inside" begins. Above a proxy's chord sag, below anything you can see.</summary>
        public const float InsideSlack = 0.05f;

        public static bool IsInside(Vector3 point, Vector2 center, float radius)
        {
            float r = radius - InsideSlack;
            if (r <= 0f) return false;
            float dx = point.x - center.x, dz = point.z - center.y;
            return dx * dx + dz * dz < r * r;
        }

        /// <summary>True when this one base's wall stands between <paramref name="a"/> and <paramref name="b"/>.</summary>
        public static bool Blocks(Vector3 a, Vector3 b, Vector2 center, float radius) =>
            IsInside(a, center, radius) != IsInside(b, center, radius);
    }
}

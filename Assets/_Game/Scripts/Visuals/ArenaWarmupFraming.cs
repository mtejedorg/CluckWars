using UnityEngine;

namespace CluckWars.Visuals
{
    /// <summary>
    /// The overview framing the arena warm-up renders with: the match camera's own rotation (same yaw / pitch, orthographic,
    /// same render target, post and haze), re-centred and zoomed so the WHOLE arena plus the surround band fits the screen,
    /// and pulled back along its axis so the near corners stay in front of it (the match's <c>nearClipPlane</c> is negative
    /// and its distance is only 15 m). Pure maths, so the fit is testable for any aspect ratio.
    /// </summary>
    public readonly struct ArenaWarmupFraming
    {
        /// <summary>3 % breathing room so no edge pixel of the box is cropped by rounding.</summary>
        private const float Padding = 1.03f;

        /// <summary>The nearest any framed point may be in front of the camera (metres).</summary>
        public const float MinDepth = 1f;

        public readonly Vector3 Focus;
        public readonly float OrthoSize;
        public readonly float Distance;

        private ArenaWarmupFraming(Vector3 focus, float orthoSize, float distance)
        {
            Focus = focus; OrthoSize = orthoSize; Distance = distance;
        }

        /// <param name="halfExtent">Half the arena side (<c>MapGenerator.ArenaHalfSize</c>).</param>
        /// <param name="surroundMargin">Extra metres around the arena that the surround art covers.</param>
        /// <param name="height">Height of the tallest surround prop (the box spans y = 0..height).</param>
        /// <param name="baseDistance">The match camera's normal back-off; the result never goes below it.</param>
        public static ArenaWarmupFraming Compute(float halfExtent, float surroundMargin, float height,
            float yaw, float pitch, float aspect, float baseDistance)
        {
            float h = Mathf.Max(1f, halfExtent + surroundMargin);
            var focus = new Vector3(0f, height * 0.5f, 0f);
            var rot = Quaternion.Euler(pitch, yaw, 0f);
            var right = rot * Vector3.right;
            var up = rot * Vector3.up;
            var fwd = rot * Vector3.forward;

            float maxX = 0f, maxY = 0f, minZ = 0f;
            for (int i = 0; i < 8; i++)
            {
                var corner = new Vector3((i & 1) == 0 ? -h : h, (i & 2) == 0 ? 0f : height, (i & 4) == 0 ? -h : h) - focus;
                maxX = Mathf.Max(maxX, Mathf.Abs(Vector3.Dot(corner, right)));
                maxY = Mathf.Max(maxY, Mathf.Abs(Vector3.Dot(corner, up)));
                minZ = Mathf.Min(minZ, Vector3.Dot(corner, fwd));
            }

            float ortho = Mathf.Max(maxY, maxX / Mathf.Max(aspect, 0.1f)) * Padding;
            float distance = Mathf.Max(baseDistance, MinDepth - minZ);
            return new ArenaWarmupFraming(focus, ortho, distance);
        }
    }
}

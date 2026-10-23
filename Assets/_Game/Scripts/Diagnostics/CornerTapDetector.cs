using UnityEngine;

namespace CluckWars.Diagnostics
{
    /// <summary>
    /// The on-device overlay toggle (test-session item 6): <see cref="TapsRequired"/> taps in the top-left corner within
    /// <see cref="WindowSeconds"/>. The corner is a square of <c>max(120 px, 8 % of the short side)</c>. A tap outside
    /// the corner, or one that leaves the window, starts the count again. Pure, tested.
    /// </summary>
    public sealed class CornerTapDetector
    {
        public const int TapsRequired = 5;
        public const float WindowSeconds = 2f;
        public const float CornerMinPx = 120f;
        public const float CornerShortSideFraction = 0.08f;

        private readonly float[] _times = new float[TapsRequired];
        private int _count;

        /// <summary>Corner square side in pixels for a screen of <paramref name="width"/> × <paramref name="height"/>.</summary>
        public static float CornerSize(float width, float height) =>
            Mathf.Max(CornerMinPx, CornerShortSideFraction * Mathf.Min(width, height));

        /// <summary>Input System screen coordinates: origin bottom-left, so "top" is near <paramref name="height"/>.</summary>
        public static bool InTopLeftCorner(Vector2 position, float width, float height)
        {
            float size = CornerSize(width, height);
            return position.x >= 0f && position.x <= size && position.y <= height && position.y >= height - size;
        }

        /// <summary>Registers one tap; true on the tap that completes the gesture (the count then restarts).</summary>
        public bool Register(Vector2 position, float width, float height, float time)
        {
            if (!InTopLeftCorner(position, width, height)) { _count = 0; return false; }

            // Drop taps that fell out of the window.
            int keep = 0;
            for (int i = 0; i < _count; i++)
                if (time - _times[i] <= WindowSeconds) _times[keep++] = _times[i];
            _count = keep;

            _times[_count++] = time;
            if (_count < TapsRequired) return false;
            _count = 0;
            return true;
        }

        public void Reset() => _count = 0;
    }
}

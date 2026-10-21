using System.Collections.Generic;

namespace CluckWars.Diagnostics
{
    /// <summary>
    /// At most one line per key per <see cref="IntervalSeconds"/>; what was held back is counted per key and reported
    /// with the next line that gets through. Pure, tested.
    /// </summary>
    public sealed class PerKeyRateLimiter
    {
        public readonly float IntervalSeconds;
        private readonly Dictionary<int, float> _lastAt = new Dictionary<int, float>();
        private readonly Dictionary<int, int> _suppressed = new Dictionary<int, int>();

        public PerKeyRateLimiter(float intervalSeconds) { IntervalSeconds = intervalSeconds; }

        /// <summary>True when a line for <paramref name="key"/> may go out now; <paramref name="suppressedSince"/> is how many were held back before it.</summary>
        public bool Allow(int key, float now, out int suppressedSince)
        {
            suppressedSince = 0;
            if (_lastAt.TryGetValue(key, out float last) && now - last < IntervalSeconds)
            {
                _suppressed.TryGetValue(key, out int n);
                _suppressed[key] = n + 1;
                return false;
            }
            _lastAt[key] = now;
            if (_suppressed.TryGetValue(key, out suppressedSince)) _suppressed[key] = 0;
            return true;
        }
    }
}

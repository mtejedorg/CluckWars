namespace CluckWars.Services
{
    /// <summary>
    /// One vibration pattern in Android's waveform convention: <c>[delay, on, off, on, ...]</c> milliseconds with a
    /// matching amplitude per segment (0 for the gaps, 1..255 for the buzzes).
    /// </summary>
    public sealed class HapticPattern
    {
        public readonly long[] TimingsMs;
        public readonly int[] Amplitudes;

        public HapticPattern(long[] timingsMs, int[] amplitudes)
        {
            TimingsMs = timingsMs;
            Amplitudes = amplitudes;
        }

        /// <summary>A single buzz (<c>[0, on]</c>): played with <c>VibrationEffect.createOneShot</c>.</summary>
        public bool IsOneShot => TimingsMs.Length == 2;

        public long TotalMs
        {
            get { long t = 0; for (int i = 0; i < TimingsMs.Length; i++) t += TimingsMs[i]; return t; }
        }
    }

    /// <summary>
    /// Pure haptic rules: which pattern each kind of event plays and when a buzz is allowed. No Unity objects, so
    /// EditMode tests pin both.
    /// </summary>
    public static class HapticRules
    {
        /// <summary>Minimum time between two buzzes (the brief's 200 ms): a flurry of hits is one buzz, not a drone.</summary>
        public const float MinGapSeconds = 0.2f;

        /// <summary>40 ms at a moderate amplitude (the brief caps a hit at 180).</summary>
        public static readonly HapticPattern Hit =
            new HapticPattern(new long[] { 0, 40 }, new[] { 0, 160 });

        /// <summary>Stun or knock-out: 60 ms on, 50 ms off, 90 ms on.</summary>
        public static readonly HapticPattern Stun =
            new HapticPattern(new long[] { 0, 60, 50, 90 }, new[] { 0, 230, 0, 230 });

        /// <summary>The edge-band cancel tick: 15 ms, light.</summary>
        public static readonly HapticPattern CancelArm =
            new HapticPattern(new long[] { 0, 15 }, new[] { 0, 80 });

        public static HapticPattern PatternFor(HapticKind kind) => kind switch
        {
            HapticKind.Stun      => Stun,
            HapticKind.CancelArm => CancelArm,
            _                    => Hit,
        };

        /// <summary>
        /// May a buzz of <paramref name="kind"/> play at <paramref name="now"/>, given the previous one? Yes when the
        /// gap has passed, or when it is a STRONGER kind than the one that just played: a hit and the stun it caused
        /// land on the same frame, and the stun must not be swallowed by the 40 ms hit buzz it follows (the
        /// platform replaces the running vibration, so the stun simply takes over).
        /// </summary>
        public static bool Allowed(float now, float lastTime, HapticKind lastKind, HapticKind kind)
        {
            if (float.IsNegativeInfinity(lastTime)) return true;
            if (now - lastTime >= MinGapSeconds) return true;
            return now >= lastTime && kind > lastKind;
        }
    }

    /// <summary>The rate-limit state: remembers the last buzz that played.</summary>
    public sealed class HapticLimiter
    {
        private float _lastTime = float.NegativeInfinity;
        private HapticKind _lastKind;

        /// <summary>True (and records the buzz) when <see cref="HapticRules.Allowed"/> permits it.</summary>
        public bool TryAcquire(float now, HapticKind kind)
        {
            if (!HapticRules.Allowed(now, _lastTime, _lastKind, kind)) return false;
            _lastTime = now;
            _lastKind = kind;
            return true;
        }
    }
}

using System;
using CluckWars.Gameplay;

namespace CluckWars.Diagnostics
{
    public enum HitchLevel : byte { None, Info, Warn }

    /// <summary>
    /// PerfProbe's rules (test-session item 2), pure so EditMode tests pin them: which frames are hitches and at what
    /// level, and which match phase a frame belongs to.
    /// </summary>
    public static class HitchRules
    {
        /// <summary>A frame longer than this is logged at Info.</summary>
        public const float InfoMs = 50f;
        /// <summary>A frame longer than this is logged at Warn, unless the hitch was expected.</summary>
        public const float WarnMs = 100f;

        /// <param name="expected">Scene loading or the arena cover is up: hitches are expected there and capped at Info.</param>
        public static HitchLevel Classify(float frameMs, bool expected)
        {
            if (float.IsNaN(frameMs) || frameMs <= InfoMs) return HitchLevel.None;
            return frameMs > WarnMs && !expected ? HitchLevel.Warn : HitchLevel.Info;
        }

        /// <summary>
        /// <c>loading</c> (a scene load in flight or just finished), <c>covered</c> (the arena cover is up),
        /// <c>reveal-fade</c> (the frame started before the cover's fade-out ended), <c>menu</c>
        /// (no match manager), <c>lobby</c> (multiplayer waiting room), <c>starting</c> (GET READY without the cover),
        /// <c>intro</c> (3-2-1), <c>active</c>, <c>ended</c>.
        /// </summary>
        public static string Phase(bool loading, bool covered, bool revealFade, bool hasManager, MatchState state, bool introActive)
        {
            if (loading) return "loading";
            if (covered) return "covered";
            if (revealFade) return "reveal-fade";
            if (!hasManager) return "menu";
            switch (state)
            {
                case MatchState.WaitingForPlayers: return "lobby";
                case MatchState.Starting:          return "starting";
                case MatchState.Active:            return introActive ? "intro" : "active";
                case MatchState.Ended:             return "ended";
                default:                           return state.ToString().ToLowerInvariant();
            }
        }

        /// <summary>Phases whose hitches are expected (capped at Info).</summary>
        public static bool IsExpectedPhase(string phase) => phase == "loading" || phase == "covered" || phase == "reveal-fade";
    }

    /// <summary>
    /// At most <see cref="MaxPerSecond"/> hitch lines per wall-clock second: a stutter storm (a stalled device, a
    /// long load) must not flood logcat and bury everything else. The lines that were held back are counted and the
    /// count is printed in the next 5 s summary (<c>suppressed=N</c>). Pure, tested.
    /// </summary>
    public sealed class HitchLineLimiter
    {
        public const int MaxPerSecond = 5;

        private float _windowStart = float.NegativeInfinity;
        private int _inWindow;

        /// <summary>Lines held back since the last <see cref="TakeSuppressed"/>.</summary>
        public int Suppressed { get; private set; }

        /// <summary>True when a hitch line may be logged at <paramref name="now"/> (seconds); false counts it as suppressed.</summary>
        public bool Allow(float now)
        {
            if (now - _windowStart >= 1f) { _windowStart = now; _inWindow = 0; }
            if (_inWindow < MaxPerSecond) { _inWindow++; return true; }
            Suppressed++;
            return false;
        }

        public int TakeSuppressed()
        {
            int n = Suppressed;
            Suppressed = 0;
            return n;
        }
    }

    /// <summary>
    /// Fixed-capacity ring of frame times (ms) with average / max / hitch counts and nearest-rank percentiles.
    /// No allocation after construction: the percentile sorts a preallocated scratch copy. Holds the most recent
    /// <c>capacity</c> frames; PerfProbe clears it after each 5 s summary.
    /// </summary>
    public sealed class FrameTimeWindow
    {
        private readonly float[] _ring;
        private readonly float[] _scratch;
        private int _next, _count;
        private double _sum;
        private float _max;
        private int _over50, _over100;

        public FrameTimeWindow(int capacity)
        {
            if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
            _ring = new float[capacity];
            _scratch = new float[capacity];
        }

        public int Capacity => _ring.Length;
        public int Count => _count;
        public float Average => _count > 0 ? (float)(_sum / _count) : 0f;
        public float Max => _max;
        /// <summary>Frames over 50 ms added since the last <see cref="Clear"/> (not limited to the ring's contents).</summary>
        public int HitchesOver50 => _over50;
        /// <summary>Frames over 100 ms added since the last <see cref="Clear"/>.</summary>
        public int HitchesOver100 => _over100;

        public void Add(float ms)
        {
            if (float.IsNaN(ms) || ms < 0f) return;
            if (_count == _ring.Length) _sum -= _ring[_next];
            else _count++;
            _ring[_next] = ms;
            _next = (_next + 1) % _ring.Length;
            _sum += ms;
            if (ms > _max) _max = ms;
            if (ms > HitchRules.InfoMs) _over50++;
            if (ms > HitchRules.WarnMs) _over100++;
        }

        /// <summary>Nearest-rank percentile (0..100) of the frames in the ring; 0 when empty.</summary>
        public float Percentile(float p)
        {
            if (_count == 0) return 0f;
            Array.Copy(_ring, _scratch, _count == _ring.Length ? _ring.Length : _count);
            // The ring fills from index 0 until it wraps, so the first _count entries are exactly its contents.
            Array.Sort(_scratch, 0, _count);
            int rank = (int)Math.Ceiling(p / 100.0 * _count);
            int idx = Math.Min(_count - 1, Math.Max(0, rank - 1));
            return _scratch[idx];
        }

        public void Clear()
        {
            _next = 0;
            _count = 0;
            _sum = 0d;
            _max = 0f;
            _over50 = 0;
            _over100 = 0;
        }
    }
}

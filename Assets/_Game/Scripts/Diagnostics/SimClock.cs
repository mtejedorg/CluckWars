using Fusion;
using UnityEngine;

namespace CluckWars.Diagnostics
{
    /// <summary>
    /// The clock of the dev-tools code (auto-play, the invariant probes): the Fusion runner's simulation time, in seconds,
    /// never <c>Time.unscaledTime</c> or <c>realtimeSinceStartup</c>. With <c>-cw_timescale N</c> (solo) the simulation runs
    /// N times faster than the wall clock, so anything on real time (a 0.5 s probe delay, a 0.8 s peck cadence, a 2 s stuck
    /// window) fires N times too late in game terms, and two speeds play different matches (BL-208). At x1 the two clocks
    /// agree, so nothing changes for a normal session. Before a runner exists it falls back to the unscaled clock.
    /// </summary>
    public static class SimClock
    {
        private static NetworkRunner _runner;

        /// <summary>The runner <see cref="Now()"/> reads; the driver binds it every frame it has one.</summary>
        public static void Bind(NetworkRunner runner) => _runner = runner;

        public static float Now() => Now(_runner);

        public static float Now(NetworkRunner runner) =>
            runner != null && runner.IsRunning ? (float)runner.SimulationTime : Time.unscaledTime;
    }

    /// <summary>
    /// Turns a stream of per-frame times into decisions on a fixed step of simulated time: <see cref="TryAdvance"/> is true
    /// at most once per step, and hands back the step boundary as the decision time, so the same simulated second yields the
    /// same decision times at any frame rate or time scale (a frame longer than a step simply decides once for it).
    /// </summary>
    public struct TickCadence
    {
        private long _index;
        private bool _started;

        public void Reset() { _started = false; }

        /// <param name="now">Simulation seconds.</param>
        /// <param name="stepSeconds">The decision step (a whole number of simulation ticks).</param>
        /// <param name="time">The boundary the decision is made for: <c>floor(now / step) * step</c>.</param>
        public bool TryAdvance(float now, float stepSeconds, out float time)
        {
            long index = (long)Mathf.Floor(now / stepSeconds);
            if (_started && index <= _index) { time = _index * stepSeconds; return false; }
            _started = true;
            _index = index;
            time = index * stepSeconds;
            return true;
        }
    }
}

using System;
using System.Globalization;
using CluckWars.Gameplay;
using CluckWars.Logging;
using UnityEngine;
using UnityEngine.Profiling;
using Zenject;

namespace CluckWars.Diagnostics
{
    /// <summary>
    /// Frame-hitch detector (test-session item 2, Source <c>Perf</c>). Every frame: <c>Time.unscaledDeltaTime</c> into a
    /// ring; a frame over 50 ms logs Info, over 100 ms Warn (capped at Info while a scene loads or the arena cover is up,
    /// where hitches are expected): <c>Hitch ms=.. frame=.. state=.. phase=.. abilities=.. gc=.. alloc=..KB</c>.
    /// Every 5 s one Info summary: <c>Perf avg=..ms p95=.. p99=.. max=.. hitches50=N hitches100=M fps=.. mem=R/MMB
    /// gc=.. cpu=..ms gpu=..ms</c> (FrameTimingManager; <c>n/a</c> when the platform or the build reports nothing).
    /// </summary>
    /// <remarks>
    /// Dev tools only (installed by <c>ProjectInstaller</c> when <see cref="DevTools.Available"/>). Zero per-frame
    /// allocation unless a line is actually logged: the ring, its percentile scratch and the FrameTiming buffer are
    /// preallocated. <c>alloc</c> is the frame-to-frame change of <c>Profiler.GetMonoUsedSizeLong</c> (negative after a
    /// collection). Reads match state only; writes nothing but <see cref="DiagnosticsReadout"/>.
    /// </remarks>
    [DefaultExecutionOrder(-1000)]
    public sealed class PerfProbe : MonoBehaviour
    {
        private const string Source = "Perf";
        private const float SummarySeconds = 5f;
        // The first frames after boot carry the player's own start-up; they are not a hitch of the game.
        private const int SkipFirstFrames = 3;

        private ILogService _log;
        private readonly FrameTimeWindow _window = new FrameTimeWindow(1024);
        private readonly FrameTiming[] _timings = new FrameTiming[1];

        private float _summaryStartedAt = -1f;
        private int _framesInSummary;
        private int _gcAtSummaryStart, _gcPrevFrame;
        private long _monoPrevFrame;
        private double _cpuSumMs, _gpuSumMs;
        private int _cpuSamples, _gpuSamples;

        [Inject]
        public void Construct(ILogService log) => _log = log;

        private void Start()
        {
            // Zenject injects this component straight after AddComponent (ProjectInstaller); self-inject only if it did not.
            if (_log == null) ProjectContext.Instance.Container.Inject(this);
            DiagnosticsObjects.KeepAcrossScenes(gameObject);
            _gcPrevFrame = _gcAtSummaryStart = GC.CollectionCount(0);
            _monoPrevFrame = Profiler.GetMonoUsedSizeLong();
            _summaryStartedAt = Time.realtimeSinceStartup;
        }

        private void Update()
        {
            if (_summaryStartedAt < 0f) return;
            float ms = Time.unscaledDeltaTime * 1000f;
            int gc = GC.CollectionCount(0);
            long mono = Profiler.GetMonoUsedSizeLong();

            FrameTimingManager.CaptureFrameTimings();
            if (FrameTimingManager.GetLatestTimings(1, _timings) > 0)
            {
                if (_timings[0].cpuFrameTime > 0d) { _cpuSumMs += _timings[0].cpuFrameTime; _cpuSamples++; }
                if (_timings[0].gpuFrameTime > 0d) { _gpuSumMs += _timings[0].gpuFrameTime; _gpuSamples++; }
            }

            if (Time.frameCount > SkipFirstFrames)
            {
                _window.Add(ms);
                _framesInSummary++;
                if (ms > HitchRules.InfoMs) DiagnosticsReadout.PerfHitchCount++;
                LogHitchIfAny(ms, gc - _gcPrevFrame, mono - _monoPrevFrame);
            }
            _gcPrevFrame = gc;
            _monoPrevFrame = mono;

            float now = Time.realtimeSinceStartup;
            if (now - _summaryStartedAt >= SummarySeconds) Summarize(now, gc);
        }

        private void LogHitchIfAny(float ms, int gcDelta, long allocDelta)
        {
            var gm = GameManager.Instance;
            bool hasManager = gm != null && gm.Object != null && gm.Object.IsValid;
            if (ms <= HitchRules.InfoMs) return;

            var state = hasManager ? gm.State : MatchState.WaitingForPlayers;
            string phase = HitchRules.Phase(RevealState.IsLoading(Time.realtimeSinceStartup), RevealState.CoverShown,
                hasManager, state, hasManager && gm.IsIntroActive);
            var level = HitchRules.Classify(ms, HitchRules.IsExpectedPhase(phase));
            if (level == HitchLevel.None) return;

            var msg = "Hitch ms=" + ms.ToString("0.0", CultureInfo.InvariantCulture) +
                      " frame=" + (Time.frameCount - 1).ToString(CultureInfo.InvariantCulture) +
                      " state=" + (hasManager ? state.ToString() : "menu") +
                      " phase=" + phase +
                      " abilities=" + LocalActiveAbilityCount().ToString(CultureInfo.InvariantCulture) +
                      " gc=" + gcDelta.ToString(CultureInfo.InvariantCulture) +
                      " alloc=" + FormatKb(allocDelta);
            if (level == HitchLevel.Warn) _log?.Warn(Source, msg);
            else _log?.Info(Source, msg);
        }

        private void Summarize(float now, int gc)
        {
            float elapsed = now - _summaryStartedAt;
            float p95 = _window.Percentile(95f);
            DiagnosticsReadout.PerfP95Ms = _window.Count > 0 ? p95 : -1f;

            if (_window.Count > 0)
            {
                long reservedMb = Profiler.GetTotalReservedMemoryLong() / (1024 * 1024);
                long monoMb = Profiler.GetMonoUsedSizeLong() / (1024 * 1024);
                _log?.Info(Source,
                    "Perf avg=" + _window.Average.ToString("0.0", CultureInfo.InvariantCulture) + "ms" +
                    " p95=" + p95.ToString("0.0", CultureInfo.InvariantCulture) +
                    " p99=" + _window.Percentile(99f).ToString("0.0", CultureInfo.InvariantCulture) +
                    " max=" + _window.Max.ToString("0.0", CultureInfo.InvariantCulture) +
                    " hitches50=" + _window.HitchesOver50.ToString(CultureInfo.InvariantCulture) +
                    " hitches100=" + _window.HitchesOver100.ToString(CultureInfo.InvariantCulture) +
                    " fps=" + (elapsed > 0f ? _framesInSummary / elapsed : 0f).ToString("0.0", CultureInfo.InvariantCulture) +
                    " mem=" + reservedMb.ToString(CultureInfo.InvariantCulture) + "/" + monoMb.ToString(CultureInfo.InvariantCulture) + "MB" +
                    " gc=" + (gc - _gcAtSummaryStart).ToString(CultureInfo.InvariantCulture) +
                    " cpu=" + AverageOrNa(_cpuSumMs, _cpuSamples) +
                    " gpu=" + AverageOrNa(_gpuSumMs, _gpuSamples));
            }

            _window.Clear();
            _framesInSummary = 0;
            _gcAtSummaryStart = gc;
            _cpuSumMs = _gpuSumMs = 0d;
            _cpuSamples = _gpuSamples = 0;
            _summaryStartedAt = now;
        }

        private static string AverageOrNa(double sum, int samples) =>
            samples > 0 ? (sum / samples).ToString("0.0", CultureInfo.InvariantCulture) + "ms" : "n/a";

        private static string FormatKb(long bytes) =>
            (bytes >= 0 ? "+" : "-") + (Math.Abs(bytes) / 1024).ToString(CultureInfo.InvariantCulture) + "KB";

        /// <summary>Running ability slots on the chicken this peer drives (0 with no local chicken).</summary>
        private static int LocalActiveAbilityCount()
        {
            var all = ChickenController.ActiveControllers;
            for (int i = 0; i < all.Count; i++)
            {
                var c = all[i];
                if (c == null || c.Object == null || !c.Object.IsValid || !c.HasInputAuthority || c.IsBot || c.IsDecoy) continue;
                var a = c.Abilities;
                if (a == null) return 0;
                int n = 0;
                for (int s = 0; s < AbilityController.SlotCount; s++) if (a.IsSlotActive(s)) n++;
                return n;
            }
            return 0;
        }
    }
}

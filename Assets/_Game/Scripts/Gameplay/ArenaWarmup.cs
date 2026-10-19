using System;

namespace CluckWars.Gameplay
{
    /// <summary>
    /// "Is the Editor still compiling shader variants?" Behind an interface so the warm-up rule is testable. The only real
    /// implementation is Editor-only (<c>ShaderUtil.anythingCompiling</c>: async shader compilation shows cyan placeholders
    /// until it finishes); every device build and every test uses <see cref="NullShaderCompileProbe"/> (always false).
    /// </summary>
    public interface IShaderCompileProbe
    {
        bool IsCompiling { get; }
    }

    /// <summary>Never compiling: device builds, and the default for tests.</summary>
    public sealed class NullShaderCompileProbe : IShaderCompileProbe
    {
        public static readonly NullShaderCompileProbe Instance = new NullShaderCompileProbe();
        public bool IsCompiling => false;
    }

    /// <summary>The warm-up's steps, in order.</summary>
    public enum WarmupStep
    {
        NotStarted = 0,
        /// <summary>A: whole-arena framing with the lazily-created variants forced on (ghost chickens, fence-fade keyword, runtime-material proxies).</summary>
        OverviewVariants = 1,
        /// <summary>B: the same framing with every override released (the plain materials).</summary>
        OverviewPlain = 2,
        /// <summary>C: back to follow mode, snapped (no swing).</summary>
        Restore = 3,
        Done = 4,
    }

    /// <summary>
    /// The deterministic arena warm-up as a pure step machine (presentation only): an overview of the whole arena with the
    /// variant states forced on (A), the same overview plain (B), then the camera restored and snapped (C). Each of A and B
    /// holds for at least <see cref="MinFrames"/> rendered frames and, in the Editor, until the compile probe reports
    /// nothing compiling; a step that waits longer than <see cref="StepCapSeconds"/> is ended anyway
    /// (<see cref="LastStepTimedOut"/>), so a stuck compile cannot hang the load. The driver applies the camera / material
    /// side effects when <see cref="StepChanged"/> fires and calls <see cref="Tick"/> once per rendered frame.
    /// </summary>
    public sealed class ArenaWarmupSequence
    {
        /// <summary>Rendered frames each overview step holds at least (the first one is the frame the step was entered on).</summary>
        public const int MinFrames = 3;

        /// <summary>A step that has run this long ends regardless of the compile probe.</summary>
        public const float StepCapSeconds = 6f;

        private readonly IShaderCompileProbe _probe;
        private int _framesInStep;
        private float _stepSeconds;

        public ArenaWarmupSequence(IShaderCompileProbe probe = null) => _probe = probe ?? NullShaderCompileProbe.Instance;

        public WarmupStep Step { get; private set; } = WarmupStep.NotStarted;

        /// <summary>True while a run is in progress (started and not yet done).</summary>
        public bool IsRunning => Step != WarmupStep.NotStarted && Step != WarmupStep.Done;

        /// <summary>Rendered frames spent in the two overview steps, over every run.</summary>
        public int OverviewFrames { get; private set; }

        /// <summary>Seconds the overview steps spent past their minimum frames waiting for the compile probe, over every run.</summary>
        public float CompileWaitSeconds { get; private set; }

        /// <summary>How many steps ended on <see cref="StepCapSeconds"/> instead of a quiet compile probe, over every run.</summary>
        public int CappedSteps { get; private set; }

        /// <summary>True when the step that just ended was cut by <see cref="StepCapSeconds"/> (valid inside <see cref="StepChanged"/>).</summary>
        public bool LastStepTimedOut { get; private set; }

        /// <summary>Raised on every transition: (from, to).</summary>
        public event Action<WarmupStep, WarmupStep> StepChanged;

        /// <summary>Starts a run (from the beginning, or again after <see cref="WarmupStep.Done"/>: a late class re-run).
        /// No-op while a run is in progress.</summary>
        public bool Begin()
        {
            if (IsRunning) return false;
            Enter(WarmupStep.OverviewVariants);
            return true;
        }

        /// <summary>Ends a run in progress at once (the overall cap passed): jumps to <see cref="WarmupStep.Done"/> through
        /// <see cref="WarmupStep.Restore"/>'s side effects, which the driver applies on the transition.</summary>
        public void Abort()
        {
            if (!IsRunning) return;
            LastStepTimedOut = false;
            Enter(WarmupStep.Done);
        }

        /// <summary>Feeds one rendered frame (call it on every frame after the one <see cref="Begin"/> ran on).</summary>
        public void Tick(float frameSeconds)
        {
            if (!IsRunning) return;
            _framesInStep++;
            _stepSeconds += frameSeconds < 0f ? 0f : frameSeconds;

            if (Step == WarmupStep.Restore)
            {
                if (_framesInStep >= 1) { LastStepTimedOut = false; Enter(WarmupStep.Done); }
                return;
            }

            bool atLeastMin = _framesInStep >= MinFrames;
            bool compiling = atLeastMin && _probe.IsCompiling;
            if (compiling) CompileWaitSeconds += frameSeconds < 0f ? 0f : frameSeconds;
            bool quiet = atLeastMin && !compiling;
            bool capped = !quiet && _stepSeconds >= StepCapSeconds;
            if (!quiet && !capped) return;

            LastStepTimedOut = capped;
            if (capped) CappedSteps++;
            OverviewFrames += _framesInStep;
            Enter(Step == WarmupStep.OverviewVariants ? WarmupStep.OverviewPlain : WarmupStep.Restore);
        }

        private void Enter(WarmupStep next)
        {
            var from = Step;
            if (next == WarmupStep.OverviewVariants) LastStepTimedOut = false;
            Step = next;
            _framesInStep = 0;
            _stepSeconds = 0f;
            StepChanged?.Invoke(from, next);
        }
    }

    /// <summary>The Editor-only compile probe; device builds always get <see cref="NullShaderCompileProbe"/>.</summary>
    public static class ShaderCompileProbe
    {
        public static IShaderCompileProbe Create()
        {
#if UNITY_EDITOR
            return new EditorShaderCompileProbe();
#else
            return NullShaderCompileProbe.Instance;
#endif
        }

#if UNITY_EDITOR
        private sealed class EditorShaderCompileProbe : IShaderCompileProbe
        {
            public bool IsCompiling => UnityEditor.ShaderUtil.anythingCompiling;
        }
#endif
    }
}

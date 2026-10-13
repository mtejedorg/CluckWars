using CluckWars.Gameplay;
using UnityEngine;

namespace CluckWars.Visuals
{
    /// <summary>
    /// The GET READY "this is you" marker (Phase 6 chunk 7f, round-3 finding 9): while the round is settling
    /// (<see cref="MatchState.Starting"/>, GET READY) and through the 3-2-1 countdown, the LOCAL bird's feet ring
    /// (<see cref="ChickenWorldBars"/>) grows, goes fully opaque and pulses, and its YOU nameplate
    /// (<see cref="ChickenNameplate"/>) is drawn larger — so the player finds their own bird before GO.
    /// It eases back to the normal ring / label after GO.
    /// </summary>
    /// <remarks>
    /// Local presentation only: every input is either already replicated (<see cref="GameManager.State"/>,
    /// the intro timer) or this peer's input authority. Reduced Motion keeps the emphasis (it is
    /// wayfinding, not juice) but drops the pulse and the eased size change: the ring is simply bigger.
    /// </remarks>
    public static class YouMarkerRules
    {
        /// <summary>Feet-ring scale while emphasised (static under Reduced Motion, the pulse's centre otherwise).</summary>
        public const float RingScale = 1.6f;

        /// <summary>Pulse amplitude around <see cref="RingScale"/>.</summary>
        public const float RingPulseAmplitude = 0.18f;

        /// <summary>Pulse rate in Hz. Slow enough to read as "breathing", not as an alarm.</summary>
        public const float RingPulseHz = 1.25f;

        /// <summary>Feet-ring alpha while emphasised (the normal ring is 0.75).</summary>
        public const float RingAlpha = 1f;

        /// <summary>YOU nameplate scale while emphasised.</summary>
        public const float NameplateScale = 1.45f;

        /// <summary>How long the marker takes to grow in at GET READY and settle back after GO.</summary>
        public const float EaseSeconds = 0.25f;

        /// <summary>
        /// True while the round is in GET READY or its countdown: <see cref="MatchState.Starting"/>, or
        /// <see cref="MatchState.Active"/> with the intro countdown still running. Never in the waiting room,
        /// in play or on the end screen.
        /// </summary>
        public static bool IsGetReady(MatchState state, bool introActive) =>
            state == MatchState.Starting || (state == MatchState.Active && introActive);

        /// <summary>Reads the live match state. False with no spawned <see cref="GameManager"/>.</summary>
        public static bool IsGetReadyNow()
        {
            var gm = GameManager.Instance;
            if (gm == null || gm.Object == null || !gm.Object.IsValid) return false;
            return IsGetReady(gm.State, gm.IsIntroActive);
        }

        /// <summary>Eases the emphasis 0..1 toward its target; Reduced Motion snaps.</summary>
        public static float StepEmphasis(float current, bool target, float deltaSeconds, bool reducedMotion)
        {
            float goal = target ? 1f : 0f;
            if (reducedMotion || EaseSeconds <= 0f) return goal;
            return Mathf.MoveTowards(Mathf.Clamp01(current), goal, Mathf.Max(0f, deltaSeconds) / EaseSeconds);
        }

        /// <summary>Ring scale multiplier for an emphasis 0..1 at time <paramref name="time"/>.</summary>
        public static float RingScaleAt(float emphasis, float time, bool reducedMotion)
        {
            float peak = reducedMotion
                ? RingScale
                : RingScale + RingPulseAmplitude * Mathf.Sin(time * RingPulseHz * 2f * Mathf.PI);
            return Mathf.Lerp(1f, peak, Mathf.Clamp01(emphasis));
        }

        /// <summary>Ring alpha for an emphasis 0..1, from the ring's normal alpha.</summary>
        public static float RingAlphaAt(float emphasis, float normalAlpha) =>
            Mathf.Lerp(normalAlpha, RingAlpha, Mathf.Clamp01(emphasis));

        /// <summary>Nameplate scale multiplier for an emphasis 0..1.</summary>
        public static float NameplateScaleAt(float emphasis) => Mathf.Lerp(1f, NameplateScale, Mathf.Clamp01(emphasis));
    }
}

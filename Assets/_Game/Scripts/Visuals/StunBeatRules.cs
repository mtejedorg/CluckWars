using CluckWars.Abilities;
using UnityEngine;

namespace CluckWars.Visuals
{
    /// <summary>
    /// Pure rules of the Stun beats (D10, vfx.md 2.2): the star pop-in and drop-off curves, the wing-slam arc gate and the bonk puff
    /// count. Pure so the curves, the gate and the Reduced Motion variants are testable from the shipped constants.
    /// </summary>
    public static class StunBeatRules
    {
        /// <summary>Star scale <paramref name="elapsed"/> seconds after the Stunned rising edge: 0 to the overshoot, settling at 1.
        /// Always 1 under Reduced Motion (alpha only).</summary>
        public static float PopScale(float elapsed, bool reducedMotion)
        {
            if (reducedMotion || elapsed >= FeedbackTuning.StunStarPopSeconds) return 1f;
            if (elapsed <= 0f) return 0f;
            float t = elapsed / FeedbackTuning.StunStarPopSeconds;
            float peak = FeedbackTuning.StunStarPopPeakFraction;
            if (t < peak)
            {
                float u = t / peak;
                return FeedbackTuning.StunStarPopOvershoot * (1f - (1f - u) * (1f - u));
            }
            float v = (t - peak) / (1f - peak);
            return Mathf.Lerp(FeedbackTuning.StunStarPopOvershoot, 1f, v * v * (3f - 2f * v));
        }

        /// <summary>Star alpha after the rising edge: 1, except the Reduced Motion fade-in.</summary>
        public static float PopAlpha(float elapsed, bool reducedMotion)
        {
            if (!reducedMotion) return 1f;
            return Mathf.Clamp01(elapsed / FeedbackTuning.StunStarFadeInSecondsReduced);
        }

        /// <summary>How long the drop-off lasts.</summary>
        public static float DropSeconds(bool reducedMotion) =>
            reducedMotion ? FeedbackTuning.StunStarDropSecondsReduced : FeedbackTuning.StunStarDropSeconds;

        /// <summary>Vertical offset (metres, 0 or negative) of the stars <paramref name="elapsed"/> seconds after the falling edge:
        /// free fall, quadratic in time, ending at the drop distance. Zero under Reduced Motion (fade in place).</summary>
        public static float DropOffset(float elapsed, bool reducedMotion)
        {
            if (reducedMotion) return 0f;
            float u = Mathf.Clamp01(elapsed / FeedbackTuning.StunStarDropSeconds);
            return -FeedbackTuning.StunStarDropMetres * u * u;
        }

        /// <summary>Star alpha <paramref name="elapsed"/> seconds after the falling edge (1 down to 0 over the drop).</summary>
        public static float DropAlpha(float elapsed, bool reducedMotion) =>
            1f - Mathf.Clamp01(elapsed / DropSeconds(reducedMotion));

        /// <summary>Is the drop-off finished?</summary>
        public static bool DropDone(float elapsed, bool reducedMotion) => elapsed >= DropSeconds(reducedMotion);

        /// <summary>The caster arcs are the wing-slam's own swing: only for the Stun job (<see cref="AbilityJob.Stun"/>, Wing Slam), never a Death Mark or Hunt stun.</summary>
        public static bool WhiffArcsApply(AbilityBaseSO ability) => ability.Is(AbilityJob.Stun);

        /// <summary>Bonk puff dust count: 4, or 2 under Reduced Motion.</summary>
        public static int BonkCount(bool reducedMotion) =>
            reducedMotion ? FeedbackTuning.StunBonkCountReduced : FeedbackTuning.StunBonkCount;

        /// <summary>Bonk puff rise speed: none under Reduced Motion (the puffs fade in place).</summary>
        public static float BonkSpeed(bool reducedMotion) => reducedMotion ? 0f : FeedbackTuning.StunBonkSpeed;
    }

    /// <summary>One frame of the stun stars: whether to show them, whether they orbit, and their scale, alpha and vertical offset.</summary>
    public readonly struct StunStarFrame
    {
        public readonly bool Visible, Orbit;
        public readonly float Scale, Alpha, OffsetY;
        public StunStarFrame(bool visible, bool orbit, float scale, float alpha, float offsetY)
        { Visible = visible; Orbit = orbit; Scale = scale; Alpha = alpha; OffsetY = offsetY; }
    }

    /// <summary>
    /// The stun stars' edge state machine (D10), pure so the pop-in, the drop-off and the mid-fall restart are testable without a
    /// chicken. Seeded on the first live observation (a late join into a running stun shows steady stars, never a pop-in); the rising
    /// edge starts the pop and cancels any drop in flight, the falling edge starts the drop.
    /// </summary>
    public struct StunStarTimeline
    {
        private bool _seeded, _was, _dropping;
        private float _popStart, _dropStart;

        public bool Dropping => _dropping;

        public StunStarFrame Step(bool live, bool stunned, float now, bool reducedMotion)
        {
            if (!live)
            {
                _seeded = false;
                _dropping = false;
                return new StunStarFrame(false, false, 1f, 1f, 0f);
            }
            if (!_seeded)
            {
                _seeded = true;
                _was = stunned;
                _popStart = -1f;
            }
            else if (stunned != _was)
            {
                _was = stunned;
                if (stunned) { _dropping = false; _popStart = now; }
                else         { _dropping = true;  _dropStart = now; }
            }

            if (stunned)
            {
                float e = _popStart < 0f ? float.MaxValue : now - _popStart;
                return new StunStarFrame(true, true, StunBeatRules.PopScale(e, reducedMotion), StunBeatRules.PopAlpha(e, reducedMotion), 0f);
            }
            if (_dropping)
            {
                float e = now - _dropStart;
                if (StunBeatRules.DropDone(e, reducedMotion))
                {
                    _dropping = false;
                    return new StunStarFrame(false, false, 1f, 0f, 0f);
                }
                return new StunStarFrame(true, false, 1f, StunBeatRules.DropAlpha(e, reducedMotion), StunBeatRules.DropOffset(e, reducedMotion));
            }
            return new StunStarFrame(false, false, 1f, 0f, 0f);
        }
    }
}

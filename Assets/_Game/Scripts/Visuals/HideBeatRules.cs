using UnityEngine;

namespace CluckWars.Visuals
{
    /// <summary>
    /// Pure rules of the Hide beats (D9, vfx.md 2.8): which edge of <c>VisualOpacity</c> a poof answers, the Reduced Motion
    /// variants, the local ring's spin and its end-of-Hide blink. Pure so the edge, the counts and the blink are testable from
    /// the shipped constants.
    /// </summary>
    public static class HideBeatRules
    {
        public enum Edge { None, Enter, Break }

        /// <summary>The poof edge between two observations of "is this bird faded" (<see cref="ControlStateVFX.IsFadedOpacity"/>).</summary>
        public static Edge PoofEdge(bool wasFaded, bool faded) =>
            faded == wasFaded ? Edge.None : (faded ? Edge.Enter : Edge.Break);

        public static int DustCount(bool reducedMotion) =>
            reducedMotion ? FeedbackTuning.HidePoofDustCountReduced : FeedbackTuning.HidePoofDustCount;

        public static int FeatherCount(bool reducedMotion) =>
            reducedMotion ? FeedbackTuning.HidePoofFeatherCountReduced : FeedbackTuning.HidePoofFeatherCount;

        /// <summary>Radial speed of the puffs: none under Reduced Motion (they fade in place).</summary>
        public static float DustSpeed(bool reducedMotion) => reducedMotion ? 0f : FeedbackTuning.HidePoofSpeed;

        public static float FeatherSpeed(bool reducedMotion) => reducedMotion ? 0f : FeedbackTuning.HidePoofFeatherSpeed;

        /// <summary>The ring is drawn under a bird this peer owns (not a decoy) while it is faded and in the match.</summary>
        public static bool ShowRing(bool isSelfTarget, bool faded, bool removed) => isSelfTarget && faded && !removed;

        /// <summary>Ring rotation about the vertical axis, degrees, at <paramref name="time"/>: 0.25 lap/s, static under Reduced Motion.</summary>
        public static float RingAngle(float time, bool reducedMotion) =>
            reducedMotion ? 0f : Mathf.Repeat(time * FeedbackTuning.HideRingLapsPerSecond * 360f, 360f);

        /// <summary>
        /// Alpha multiplier of the ring from the Hide slot's replicated remaining time (<c>&lt; 0</c> = unknown: lit). Lit above
        /// <see cref="FeedbackTuning.HideBlinkLeadSeconds"/>; inside it two blinks (dim then lit, one period each), or under
        /// Reduced Motion a steady dip.
        /// </summary>
        public static float BlinkAlpha(float remainingSeconds, bool reducedMotion)
        {
            if (remainingSeconds < 0f || remainingSeconds >= FeedbackTuning.HideBlinkLeadSeconds) return 1f;
            if (reducedMotion) return FeedbackTuning.HideBlinkSteadyAlpha;
            float into = FeedbackTuning.HideBlinkLeadSeconds - remainingSeconds;
            float phase = Mathf.Repeat(into, FeedbackTuning.HideBlinkPeriodSeconds) / FeedbackTuning.HideBlinkPeriodSeconds;
            return phase < 0.5f ? FeedbackTuning.HideBlinkDimAlpha : 1f;
        }

        /// <summary>How many blinks the lead window holds (2 with the shipped constants).</summary>
        public static int BlinkCount() => Mathf.RoundToInt(FeedbackTuning.HideBlinkLeadSeconds / FeedbackTuning.HideBlinkPeriodSeconds);
    }
}

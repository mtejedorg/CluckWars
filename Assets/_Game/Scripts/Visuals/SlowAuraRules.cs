using CluckWars.Abilities;
using UnityEngine;

namespace CluckWars.Visuals
{
    /// <summary>
    /// Pure rules of the Slow aura feedback (D8, vfx.md 2.4): which radius the outline, edge band and feather-fall use, when
    /// they are drawn, and the Reduced Motion variants. Pure so the derivation from the shipped asset, the gate and the
    /// budget arithmetic are testable without a scene.
    /// </summary>
    public static class SlowAuraRules
    {
        /// <summary>
        /// The aura radius every layer is drawn at: the asset's own <see cref="FeatherAuraAbilitySO.AuraRadius"/>, the value
        /// <c>AreaEffectSpec.AuraSlow</c> hands to the zone (so the outline is the real trigger edge). Never a literal.
        /// </summary>
        public static float Radius(FeatherAuraAbilitySO ability) => ability != null ? ability.AuraRadius : 0f;

        /// <summary>Feather spawn-disc radius: inside the edge band, so a feather never starts on the outline.</summary>
        public static float FeatherDiscRadius(float auraRadius) =>
            Mathf.Max(0.1f, auraRadius - FeedbackTuning.SlowAuraFeatherInsetMetres);

        /// <summary>Feather emission rate per second.</summary>
        public static float FeatherRate(bool reducedMotion) =>
            reducedMotion ? FeedbackTuning.SlowAuraFeatherRateReduced : FeedbackTuning.SlowAuraFeatherRate;

        /// <summary>Noise sway strength on the falling feathers: none under Reduced Motion (they fall straight).</summary>
        public static float FeatherNoiseStrength(bool reducedMotion) => reducedMotion ? 0f : FeedbackTuning.SlowAuraFeatherNoiseStrength;

        /// <summary>Edge band alpha.</summary>
        public static float BandAlpha(bool reducedMotion) =>
            reducedMotion ? FeedbackTuning.SlowAuraBandAlphaReduced : FeedbackTuning.SlowAuraBandAlpha;

        /// <summary>Laps per second of the outline's dash travel: none under Reduced Motion (a static dashed ring).</summary>
        public static float OutlineLapsPerSecond(bool reducedMotion) => reducedMotion ? 0f : FeedbackTuning.SlowAuraOutlineLapsPerSecond;

        /// <summary>The outline's rotation about the vertical axis, degrees, at <paramref name="time"/> seconds.</summary>
        public static float OutlineAngle(float time, bool reducedMotion) =>
            Mathf.Repeat(time * OutlineLapsPerSecond(reducedMotion) * 360f, 360f);

        /// <summary>Foot puff count per interval: halved under Reduced Motion.</summary>
        public static int PuffCount(bool reducedMotion) =>
            reducedMotion ? FeedbackTuning.SlowAuraPuffCount / 2 : FeedbackTuning.SlowAuraPuffCount;

        /// <summary>
        /// The aura is shown while the caster's Slow slot is active and the bird is in the match, and the caster is neither
        /// hidden for this viewer nor faded (an aura around a hidden caster would reveal them: nothing draws for the
        /// outline, the band, the feathers or the puffs).
        /// </summary>
        public static bool ShouldShow(bool slotActive, bool removed, bool hiddenForViewer, bool faded) =>
            slotActive && !removed && !hiddenForViewer && !faded;

        /// <summary>Live particles the aura system holds at steady state (rate x lifetime): the number the 24 cap is sized from.</summary>
        public static int SteadyStateLive(bool reducedMotion) =>
            Mathf.CeilToInt(FeatherRate(reducedMotion) * FeedbackTuning.SlowAuraFeatherLifeSeconds);

        /// <summary>Most foot-puff feathers alive at once: one batch per interval, each living the puff lifetime.</summary>
        public static int PuffPeakLive(bool reducedMotion) =>
            PuffCount(reducedMotion) * Mathf.CeilToInt(FeedbackTuning.SlowAuraPuffLifeSeconds / FeedbackTuning.SlowAuraPuffIntervalSeconds);
    }
}

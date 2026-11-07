using UnityEngine;

namespace CluckWars.Visuals
{
    /// <summary>
    /// Pure rules of the Shove beats (D6, vfx.md 2.3): when the victim skid runs, what the caster ring and the squash are under
    /// Reduced Motion. Pure so the window, the speed gate and the counts are testable from the shipped constants.
    /// </summary>
    public static class ShoveBeatRules
    {
        /// <summary>Is <paramref name="now"/> inside the skid window that opened at <paramref name="edgeTime"/> (a negative edge time = never)?</summary>
        public static bool SkidWindowOpen(float now, float edgeTime) =>
            edgeTime >= 0f && now - edgeTime < FeedbackTuning.ShoveSkidWindowSeconds;

        /// <summary>The skid runs inside the window while planar speed is above the gate; Reduced Motion has no skid at all.</summary>
        public static bool SkidShouldRun(bool reducedMotion, bool windowOpen, float planarSpeed) =>
            !reducedMotion && windowOpen && planarSpeed > FeedbackTuning.ShoveSkidMinSpeed;

        /// <summary>Skid puffs per metre of planar travel (0 under Reduced Motion).</summary>
        public static float SkidPerMetre(bool reducedMotion) => reducedMotion ? 0f : FeedbackTuning.ShoveSkidPerMetre;

        /// <summary>Limits <paramref name="wanted"/> puffs to what is left of this knockback's budget and spends it.</summary>
        public static int SpendSkidBudget(int wanted, ref int budget)
        {
            int n = Mathf.Clamp(wanted, 0, Mathf.Max(0, budget));
            budget -= n;
            return n;
        }

        /// <summary>Caster ring sparkles: 8, or 4 under Reduced Motion.</summary>
        public static int RingCount(bool reducedMotion) =>
            reducedMotion ? FeedbackTuning.ShoveRingCountReduced : FeedbackTuning.ShoveRingCount;

        /// <summary>Outward speed of the ring sparkles: none under Reduced Motion (they fade in place).</summary>
        public static float RingSpeed(bool reducedMotion) => reducedMotion ? 0f : FeedbackTuning.ShoveRingSpeed;

        /// <summary>
        /// The victim's extra model scale <paramref name="elapsed"/> seconds after the knockback edge: the squash easing back to
        /// 1 over its duration (ease-out). One (no squash) under Reduced Motion, before the edge and once it has finished.
        /// </summary>
        public static Vector3 SquashScale(float elapsed, bool reducedMotion)
        {
            if (reducedMotion || elapsed < 0f || elapsed >= FeedbackTuning.ShoveSquashSeconds) return Vector3.one;
            float t = elapsed / FeedbackTuning.ShoveSquashSeconds;
            float u = 1f - (1f - t) * (1f - t);
            return Vector3.Lerp(FeedbackTuning.ShoveSquashScale, Vector3.one, u);
        }

        /// <summary>
        /// Does the beat squash at all? Only on the skeletal path: the legacy procedural fallback already squashes on
        /// <c>TriggerHit</c> (Y by <c>_hitSquashDepth</c>), and the skeletal Hit clip is an empty placeholder with no scale curve.
        /// </summary>
        public static bool SquashApplies(bool skeletalActive) => skeletalActive;
    }
}

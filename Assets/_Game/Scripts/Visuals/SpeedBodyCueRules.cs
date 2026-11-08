namespace CluckWars.Visuals
{
    /// <summary>
    /// Pure rules of the Speed body cue (D4, vfx.md 2.5): when the streaks / stride dust / lean run, and how many
    /// particles a distance travelled earns. Pure so the trigger and the Reduced Motion variants are testable from the
    /// shipped constants.
    /// </summary>
    public static class SpeedBodyCueRules
    {
        /// <summary>Active Speed slot AND planar speed above half the class MoveSpeed.</summary>
        public static bool ShouldRun(bool speedSlotActive, float planarSpeed, float classMoveSpeed) =>
            speedSlotActive && planarSpeed > FeedbackTuning.SpeedCueRunFraction * classMoveSpeed;

        /// <summary>Streaks per metre: none under Reduced Motion.</summary>
        public static float StreaksPerMetre(bool reducedMotion) => reducedMotion ? 0f : FeedbackTuning.SpeedStreakPerMetre;

        /// <summary>Stride dust per metre: halved under Reduced Motion.</summary>
        public static float DustPerMetre(bool reducedMotion) =>
            reducedMotion ? FeedbackTuning.SpeedDustPerMetreReduced : FeedbackTuning.SpeedDustPerMetre;

        /// <summary>Active-edge sparkle count: halved, and fading in place (no outward speed), under Reduced Motion.</summary>
        public static int EdgeSparkleCount(bool reducedMotion) =>
            reducedMotion ? FeedbackTuning.SpeedEdgeSparkleCountReduced : FeedbackTuning.SpeedEdgeSparkleCount;

        /// <summary>Adds <paramref name="metres"/> x <paramref name="perMetre"/> to the accumulator and returns the whole
        /// particles earned (the remainder stays in the accumulator).</summary>
        public static int Take(ref float accumulator, float metres, float perMetre)
        {
            accumulator += metres * perMetre;
            int n = (int)accumulator;
            accumulator -= n;
            return n;
        }

        /// <summary>The planar distance of one frame, clamped so a teleport emits a bounded burst.</summary>
        public static float StepMetres(float planarDelta) => planarDelta > FeedbackTuning.SpeedCueMaxStepMetres
            ? FeedbackTuning.SpeedCueMaxStepMetres : planarDelta;
    }
}

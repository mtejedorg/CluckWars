using System.Collections.Generic;

namespace CluckWars.Progression
{
    /// <summary>
    /// Mastery: one point per evaluable round played as a role, turned into a level by the thresholds
    /// on <see cref="ProgressionConfigSO.MasteryRoundThresholds"/>. Pure and total.
    /// </summary>
    /// <remarks>
    /// <b>Mastery grants nothing.</b> It is a number next to a role and on the emblem, and that is the
    /// whole of it — progression never hands out a stat (Maestro's ruling, 2026-09-14). Points are
    /// rounds played, not rounds won, so the only way to lose mastery is for a round to stop being
    /// evaluable, and the only way to gain it is to play.
    /// </remarks>
    public static class MasteryRules
    {
        /// <summary>
        /// How many of <paramref name="thresholds"/> <paramref name="rounds"/> has reached. A null or
        /// empty table means no levels at all, never an exception.
        /// </summary>
        public static int LevelFor(int rounds, IReadOnlyList<int> thresholds)
        {
            if (thresholds == null) return 0;

            int level = 0;
            for (int i = 0; i < thresholds.Count; i++)
            {
                if (rounds >= thresholds[i]) level++;
            }

            return level;
        }

        /// <summary>The highest level <paramref name="thresholds"/> can reach.</summary>
        public static int MaxLevel(IReadOnlyList<int> thresholds) => thresholds?.Count ?? 0;

        /// <summary>
        /// Rounds still needed for the next level, or 0 at the top (or with no table). Never negative,
        /// whatever order the thresholds are authored in.
        /// </summary>
        public static int RoundsToNextLevel(int rounds, IReadOnlyList<int> thresholds)
        {
            int level = LevelFor(rounds, thresholds);
            if (thresholds == null || level >= thresholds.Count) return 0;

            int needed = thresholds[level] - rounds;
            return needed > 0 ? needed : 0;
        }
    }
}

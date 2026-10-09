namespace CluckWars.UI
{
    /// <summary>
    /// Which leaderboard rows the PHONE HUD shows (Phase 6, A11). Four rows cost a quarter of a phone screen's height,
    /// so by default only the leader and your own row are drawn; all four show on tap and automatically for the last
    /// <see cref="ShowAllSecondsLeft"/> seconds, when the standings decide the match. Desktop layouts always show all.
    /// Pure: no Unity objects.
    /// </summary>
    public static class CompactBoardRules
    {
        /// <summary>The closing stretch in which every row shows without a tap.</summary>
        public const float ShowAllSecondsLeft = 10f;

        /// <summary>How long a tap keeps the full board open before it folds back.</summary>
        public const float TapShowAllSeconds = 4f;

        /// <summary>
        /// True when every row should be visible: not the compact layout, a tap is keeping it open, or the match is in
        /// its last seconds. <paramref name="secondsLeft"/> is only meaningful while <paramref name="matchActive"/>.
        /// </summary>
        public static bool ShowAll(bool compact, bool tapOpen, bool matchActive, float secondsLeft) =>
            !compact || tapOpen || (matchActive && secondsLeft <= ShowAllSecondsLeft);

        /// <summary>
        /// Fills <paramref name="visible"/> (indexed by rank, 0 = leader) with the rows to draw. Compact: the leader
        /// and <paramref name="youRank"/>; when you ARE the leader, you and 2nd (the rival you are racing), so the
        /// board is never a single lonely row. <paramref name="youRank"/> is -1 when you are not listed.
        /// </summary>
        public static void SelectRows(int count, int youRank, bool showAll, bool[] visible)
        {
            for (int i = 0; i < visible.Length; i++)
                visible[i] = i < count && (showAll || IsCompactRow(i, count, youRank));
        }

        private static bool IsCompactRow(int rank, int count, int youRank)
        {
            if (rank == 0) return true;
            if (youRank < 0) return false;
            if (youRank == 0) return rank == 1 && count > 1;
            return rank == youRank;
        }
    }
}

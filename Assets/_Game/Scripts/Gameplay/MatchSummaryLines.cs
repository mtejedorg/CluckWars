using System.Globalization;

namespace CluckWars.Gameplay
{
    /// <summary>
    /// Pure text for the balance lines of the <c>MATCH SUMMARY</c> block that
    /// <c>GameManager.EndMatch</c> logs on the state authority (decision F7 / Q8, 2026-10-22).
    /// </summary>
    /// <remarks>
    /// The block is read by humans and by log greps after harness and playtest sessions, so the
    /// shapes are pinned by <c>MatchSummaryLinesTests</c>. The three additions exist so a session
    /// yields the numbers the 2026-10 balance decisions wait on:
    /// <list type="bullet">
    /// <item><b>Winner Deposited</b>: food the winner actually walked home. "Assassin wins with 0
    /// deposited" (the Spoiler bonus deciding a timer expiry, A3) is otherwise invisible, because
    /// the winner's total includes the bonus.</item>
    /// <item><b>Executes Landed</b>: the sum of credited kills (an execute is the only kill).</item>
    /// <item><b>Stuns Landed</b>: stuns that started or extended a victim's stun timer, counted on
    /// the victim's authority (A4: is Wing Slam at 8 s / 1.3 s "too often"?).</item>
    /// </list>
    /// Invariant culture throughout: a Spanish-locale device must not print "0,0".
    /// </remarks>
    public static class MatchSummaryLines
    {
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        /// <summary>
        /// <c>"Winner Deposited: 0.0 (Assassin, corner 2)"</c>, or <c>"Winner Deposited: n/a (...)"</c>
        /// when there is no winning corner or its chicken was not found.
        /// </summary>
        public static string WinnerDeposited(int winnerCorner, bool winnerFound, ChickenClass winnerClass, float deposited)
        {
            if (winnerCorner < 0) return "Winner Deposited: n/a (no winner)";
            if (!winnerFound) return string.Format(Inv, "Winner Deposited: n/a (no stats for corner {0})", winnerCorner);
            return string.Format(Inv, "Winner Deposited: {0:0.0} ({1}, corner {2})", deposited, winnerClass, winnerCorner);
        }

        /// <summary><c>"Executes Landed: 3"</c>.</summary>
        public static string ExecutesLanded(int total) => string.Format(Inv, "Executes Landed: {0}", total);

        /// <summary><c>"Stuns Landed: 7"</c>.</summary>
        public static string StunsLanded(int total) => string.Format(Inv, "Stuns Landed: {0}", total);

        /// <summary>
        /// One player row: <c>"  P3 (Assassin): Kills=1, Deposited=0.0, Stunned=2"</c>. The first two
        /// fields keep the pre-2026-10-22 shape so older greps still match.
        /// </summary>
        public static string PlayerRow(int homeCornerIndex, ChickenClass cls, int kills, float deposited, int stunsTaken) =>
            string.Format(Inv, "  P{0} ({1}): Kills={2}, Deposited={3:0.0}, Stunned={4}",
                homeCornerIndex + 1, cls, kills, deposited, stunsTaken);
    }
}

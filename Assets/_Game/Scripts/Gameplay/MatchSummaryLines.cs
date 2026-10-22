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
    /// <item><b>Stuns Landed (all sources, real players)</b>: stuns that started or extended a
    /// real (non-decoy) chicken's stun timer, from ANY stun ability (Wing Slam, Ambush, Belly Flop,
    /// Headbutt), counted on the victim's authority. It is not a Wing Slam count; it answers "how
    /// often are players stunned" (A4). Stuns on a Doppelganger decoy are not counted.</item>
    /// </list>
    /// Players are labelled <c>P{corner + 1}</c> everywhere in these lines, the same as the rows.
    /// Invariant culture throughout: a Spanish-locale device must not print "0,0".
    /// </remarks>
    public static class MatchSummaryLines
    {
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        /// <summary>
        /// <c>"Winner Deposited: 0.0 by P3 (Assassin)"</c> for the 0-based <paramref name="winnerCorner"/> 2,
        /// or <c>"Winner Deposited: n/a (...)"</c> when there is no winning corner or its chicken was not found.
        /// </summary>
        public static string WinnerDeposited(int winnerCorner, bool winnerFound, ChickenClass winnerClass, float deposited)
        {
            if (winnerCorner < 0) return "Winner Deposited: n/a (no winner)";
            if (!winnerFound) return string.Format(Inv, "Winner Deposited: n/a (no stats for {0})", PlayerLabel(winnerCorner));
            return string.Format(Inv, "Winner Deposited: {0:0.0} by {1} ({2})", deposited, PlayerLabel(winnerCorner), winnerClass);
        }

        /// <summary><c>"Executes Landed: 3"</c>.</summary>
        public static string ExecutesLanded(int total) => string.Format(Inv, "Executes Landed: {0}", total);

        /// <summary><c>"Stuns Landed (all sources, real players): 7"</c>. Prefix "Stuns Landed" kept stable for greps.</summary>
        public static string StunsLanded(int total) =>
            string.Format(Inv, "Stuns Landed (all sources, real players): {0}", total);

        /// <summary>The player label every summary line uses: 0-based corner 2 is <c>"P3"</c>.</summary>
        public static string PlayerLabel(int homeCornerIndex) => string.Format(Inv, "P{0}", homeCornerIndex + 1);

        /// <summary>
        /// One player row: <c>"  P3 (Assassin): Kills=1, Deposited=0.0, Stunned=2"</c>. The first two
        /// fields keep the pre-2026-10-22 shape so older greps still match.
        /// </summary>
        public static string PlayerRow(int homeCornerIndex, ChickenClass cls, int kills, float deposited, int stunsTaken) =>
            string.Format(Inv, "  {0} ({1}): Kills={2}, Deposited={3:0.0}, Stunned={4}",
                PlayerLabel(homeCornerIndex), cls, kills, deposited, stunsTaken);
    }
}

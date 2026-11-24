using System.Globalization;
using Fusion;

namespace CluckWars.Gameplay
{
    /// <summary>
    /// The match telemetry lines (BL-208, part 3 of Balance Oracle v2): one <c>Info</c> line per steal, Peck cut, kill and
    /// first contact, in the event vocabulary of the match simulator (<c>steal</c>, <c>peckcut</c>, <c>kill</c>,
    /// <c>contact</c>), under the <see cref="Source"/> tag. Each line is written by the peer whose authority already knows
    /// the fact (the victim's for a steal, the pecker's for a cut, the killer's for a kill, the chicken's own for a first
    /// contact), so across a session every event appears exactly once and a solo log holds all of them. Pure formatting:
    /// the callers decide when. <c>key=value</c> tokens, invariant culture, so a script can split them.
    /// </summary>
    public static class MatchTelemetry
    {
        public const string Source = "Telemetry";

        public const string CauseStun = "stun", CauseShove = "shove", CauseExecution = "execution", CauseEmptyPile = "empty-pile";

        /// <summary>The seat label the other probes use: <c>B&lt;corner&gt;</c> for a bot, <c>P&lt;playerId&gt;</c> for a human.</summary>
        public static string Seat(ChickenController c) =>
            c == null ? "?" : c.IsBot ? "B" + c.HomeCornerIndex.ToString(CultureInfo.InvariantCulture)
                                      : "P" + c.Object.InputAuthority.PlayerId.ToString(CultureInfo.InvariantCulture);

        public static string Steal(int tick, string thief, string victim, float amount) =>
            string.Format(CultureInfo.InvariantCulture, "steal tick={0} thief={1} victim={2} amount={3:0.00}", tick, thief, victim, amount);

        public static string PeckCut(int tick, string pecker, string @class, string cause) =>
            string.Format(CultureInfo.InvariantCulture, "peckcut tick={0} pecker={1} class={2} cause={3}", tick, pecker, @class, cause);

        /// <param name="bounty">The flat execute bounty (<c>MatchConfig.ExecuteBounty</c>).</param>
        /// <param name="victimCargo">What the victim carried and the execute drained (0 for a steal-proof victim).</param>
        /// <param name="gained">What the killer's cargo really took of the two: held to its free room, so <c>unfit</c> is what the execute paid in
        /// name only (the victim is removed with no cargo either way).</param>
        /// <param name="victimPecking">The victim had a Peck cast running when the Kill landed.</param>
        public static string Kill(int tick, string killer, string victim, float bounty, float victimCargo, float gained, bool victimPecking)
        {
            float payout = bounty + victimCargo;
            return string.Format(CultureInfo.InvariantCulture,
                "kill tick={0} killer={1} victim={2} payout={3:0.00} bounty={4:0.00} victim_cargo={5:0.00} gained={6:0.00} unfit={7:0.00} victim_pecking={8}",
                tick, killer, victim, payout, bounty, victimCargo, gained, payout > gained ? payout - gained : 0f, victimPecking ? 1 : 0);
        }

        /// <param name="seconds">Playable seconds since the round's GET READY ended.</param>
        public static string Contact(int tick, string seat, string rival, int round, float seconds, float distance) =>
            string.Format(CultureInfo.InvariantCulture, "contact tick={0} seat={1} rival={2} round={3} t={4:0.00} dist={5:0.00}",
                tick, seat, rival, round, seconds, distance);

        public static int Tick(NetworkRunner runner) => runner != null ? (int)runner.Tick : 0;
    }
}

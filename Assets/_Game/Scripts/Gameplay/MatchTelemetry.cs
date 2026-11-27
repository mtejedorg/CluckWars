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

        public const string LootMoveTimeout = "timeout", LootMoveGone = "gone", LootMoveEmpty = "empty",
            LootMoveHome = "home", LootMoveLight = "light";

        /// <summary>The telemetry reason of an unpaid end (<see cref="LootEnd"/>); an unnamed one reads "timeout" only for <see cref="LootEnd.Timeout"/>.</summary>
        public static string LootMoveReason(LootEnd end)
        {
            switch (end)
            {
                case LootEnd.Gone: return LootMoveGone;
                case LootEnd.Home: return LootMoveHome;
                case LootEnd.Empty: return LootMoveEmpty;
                case LootEnd.Light: return LootMoveLight;
                case LootEnd.Timeout: return LootMoveTimeout;
                default: return "none";
            }
        }

        /// <summary>A Thief's loot mark was drawn on <paramref name="victim"/> (written by the Thief's authority).</summary>
        /// <param name="candidates">Rivals in the draw.</param>
        /// <param name="chance">The victim's share of the lottery's weights, 0 to 1.</param>
        public static string LootDraw(int tick, string thief, string victim, int candidates, float chance) =>
            string.Format(CultureInfo.InvariantCulture, "lootdraw tick={0} thief={1} victim={2} candidates={3} chance={4:0.00}",
                tick, thief, victim, candidates, chance);

        /// <summary>A marked steal was paid (written by the Thief's authority once the victim confirmed): the food taken from the victim, the bounty that fitted
        /// and the bounty that did not (lost).</summary>
        public static string LootTake(int tick, string thief, string victim, float stolen, float bounty, float unfit) =>
            string.Format(CultureInfo.InvariantCulture, "loottake tick={0} thief={1} victim={2} stolen={3:0.00} bounty={4:0.00} unfit={5:0.00}",
                tick, thief, victim, stolen, bounty, unfit);

        /// <summary>A mark ended unpaid: <see cref="LootMoveTimeout"/> (old with Steal on cooldown), <see cref="LootMoveGone"/> (its bird left play), <see cref="LootMoveHome"/> (he crossed into his base), <see cref="LootMoveEmpty"/> (nothing to take) or <see cref="LootMoveLight"/> (little to take: the mark ended or moved to a closer bird).</summary>
        public static string LootMove(int tick, string thief, string victim, float age, string reason) =>
            string.Format(CultureInfo.InvariantCulture, "lootmove tick={0} thief={1} victim={2} age={3:0.00} reason={4}", tick, thief, victim, age, reason);

        /// <param name="seconds">Playable seconds since the round's GET READY ended.</param>
        public static string Contact(int tick, string seat, string rival, int round, float seconds, float distance) =>
            string.Format(CultureInfo.InvariantCulture, "contact tick={0} seat={1} rival={2} round={3} t={4:0.00} dist={5:0.00}",
                tick, seat, rival, round, seconds, distance);

        public static int Tick(NetworkRunner runner) => runner != null ? (int)runner.Tick : 0;
    }
}

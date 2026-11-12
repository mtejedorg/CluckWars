using UnityEngine;

namespace CluckWars.Gameplay
{
    /// <summary>
    /// The conservation rule for every food transfer between two state authorities: a peck (pile -> chicken), a steal
    /// (victim -> thief) and a deposit (chicken -> base). Pure and Fusion-free, like <see cref="StealRules"/>.
    /// The stateful half, which <c>ChickenCargo</c> runs as-is, is <see cref="FoodRequests{TKey}"/>.
    /// </summary>
    /// <remarks>
    /// <b>The owner of the food debits first and reports the ACTUAL amount; the receiver credits only what was
    /// reported.</b> The same shape the Assassin's execute loot already had (<c>RPC_TransferAllToBountyBag</c> ->
    /// <c>RPC_CreditExecuteLoot</c>). Before this (audit A02, decision D3a) the receiver credited itself from a stale
    /// local estimate and the owner clamped silently: two peckers on a nearly empty pile both got a full bite, two
    /// thieves on one victim both got its whole load, and a deposit the base refused was simply destroyed.
    /// <list type="bullet">
    /// <item><b>Request.</b> The receiver asks for <see cref="Request"/>, records it and gets a request id. Nothing
    /// is credited yet.</item>
    /// <item><b>Debit.</b> The owner's authority removes what it really has (<see cref="ServePeck"/>,
    /// <see cref="StealRules.SplitDrain"/>) and reports that amount with the same id. A refusal reports 0, so the
    /// request is closed at once instead of holding its room until it expires.</item>
    /// <item><b>Settle.</b> The receiver closes the request with that id and credits <see cref="Credit"/>: never more
    /// than it asked for. The id is what ties a report to its request; two requests open with one owner (a refused
    /// steal and a later, larger one) can no longer be settled against each other.</item>
    /// </list>
    /// A deposit runs the other way round (the chicken is the owner and debits its cargo first), so its settlement is
    /// either "banked" (the stat is counted) or a bounce that gives the food back.
    /// <para>
    /// <b>A report with no open request.</b> A pile's or a base's report comes from that object's own state
    /// authority, which the receiver verifies before settling, so a report whose request has expired or was
    /// overwritten is still honoured, capped at the most one request can be worth (one peck, one flush): the owner
    /// has already debited, and refusing would destroy the food. A steal report comes from an arbitrary client that
    /// merely owns the named victim, so there the request must be open (strict), or any chicken owner could feed any
    /// other chicken.
    /// </para>
    /// <para>
    /// <b>Capacity.</b> Open requests reserve their room: <see cref="FreeSpace"/> subtracts them, so a second
    /// request made before the first settles cannot overbook the hold. Whatever still does not fit on arrival
    /// (a late report, a bounce) goes to the bounty bag, which has no cap (<see cref="Place"/>). A settlement
    /// never creates or destroys food.
    /// </para>
    /// </remarks>
    public static class FoodTransferRules
    {
        /// <summary>
        /// How long a request waits for its settlement. The execute loot's window, for its reason: request and report
        /// are two relayed trips in Shared Mode. Also how long an unanswered request keeps its room reserved.
        /// </summary>
        public const float SettleWindowSeconds = StealRules.ExecuteLootWindowSeconds;

        /// <summary>
        /// Slots in a transfer ledger. The busiest one holds deposit flushes: one per
        /// <see cref="BaseDepositRules.FlushSeconds"/>, each open for at most <see cref="SettleWindowSeconds"/>, so 10
        /// can be open at once with no answer at all; 16 leaves room for the extra flush a base change sends.
        /// </summary>
        public const int LedgerSize = 16;

        /// <summary>A ledger of open requests (or, for deposits, of food sent and not yet acknowledged).</summary>
        public static PendingStealLedger<TKey> NewLedger<TKey>() where TKey : struct =>
            new PendingStealLedger<TKey>(SettleWindowSeconds, LedgerSize);

        /// <summary>Room a chicken may still ask food for: its capacity less what it holds and what it has already asked for.</summary>
        public static float FreeSpace(float capacity, float held, float reserved) =>
            Mathf.Max(0f, capacity - Mathf.Max(0f, held) - Mathf.Max(0f, reserved));

        /// <summary>What a receiver asks an owner for: what it wants, never more than its free room, never negative.</summary>
        public static float Request(float wanted, float freeSpace) =>
            Mathf.Max(0f, Mathf.Min(wanted, freeSpace));

        /// <summary>
        /// What a settlement is worth to the receiver: the reported amount, never more than the request it answers
        /// (or the cap, for a report with no open request) and never negative. A NaN report is worth nothing.
        /// </summary>
        public static float Credit(float requested, float reported) =>
            reported > 0f ? Mathf.Min(Mathf.Max(0f, requested), reported) : 0f;

        /// <summary>
        /// Where arriving food goes: the cargo up to its free room, the rest to the bounty bag, which has no cap and
        /// is banked first on the next deposit. The two parts always add up to <paramref name="amount"/>.
        /// </summary>
        public static void Place(float amount, float freeSpace, out float toCargo, out float toBag)
        {
            float food = Mathf.Max(0f, amount);
            toCargo = Mathf.Min(food, Mathf.Max(0f, freeSpace));
            toBag = food - toCargo;
        }

        /// <summary>
        /// Pile side of a peck: what the pile really hands over and what it holds afterwards. The request is clamped to
        /// <paramref name="maxPeck"/> (one bite of the pecker's class), nothing is served to a pecker that is not
        /// <paramref name="inReach"/>, and the pile's floor is never breached (<see cref="FoodPileMath.Drain"/>).
        /// </summary>
        public static float ServePeck(float requested, float maxPeck, bool inReach,
            float pileAmount, float pileMax, float floorFraction, out float pileAfter)
        {
            pileAfter = pileAmount;
            if (!inReach || !(requested > 0f) || !(maxPeck > 0f)) return 0f;

            float next = FoodPileMath.Drain(pileAmount, pileMax, floorFraction, Mathf.Min(requested, maxPeck));
            float served = pileAmount - next;
            if (!(served > 0f) || Mathf.Approximately(next, pileAmount)) return 0f;
            pileAfter = next;
            return served;
        }

        /// <summary>
        /// How far a pecker may stand from a pile's surface when its request is served: the pile's collect reach plus
        /// what the chicken can have moved while the request was in flight (as <see cref="StealRules.RangeMargin"/>).
        /// </summary>
        public static bool IsWithinPeckReach(float distanceToSurface, float collectReach, float driftSpeed, float latencySeconds) =>
            distanceToSurface <= Mathf.Max(0f, collectReach) + StealRules.RangeMargin(driftSpeed, latencySeconds);
    }

    /// <summary>How a report met the receiver's ledger.</summary>
    public enum SettleOutcome : byte
    {
        /// <summary>The report closed the request it names.</summary>
        Matched = 0,
        /// <summary>No such request is open (expired or overwritten), but the sender is the owner's verified authority: honoured, capped.</summary>
        Late = 1,
        /// <summary>No such request is open and the report is not honoured without one (steals).</summary>
        Refused = 2,
    }

    /// <summary>What one settlement does to the receiver's hold.</summary>
    public struct FoodSettlement
    {
        public SettleOutcome Outcome;
        /// <summary>Added to the cargo.</summary>
        public float ToCargo;
        /// <summary>Added to the bounty bag (what the cargo had no room for).</summary>
        public float ToBag;
        /// <summary>Deposits only: food the base banked, to count in the deposit stat.</summary>
        public float Banked;
        /// <summary>The amount the request was opened for (0 when none was open).</summary>
        public float Requested;

        public float Credited => ToCargo + ToBag;
    }

    /// <summary>
    /// One chicken's side of every food transfer, as its own state authority keeps it: the requests it has open
    /// (steals by victim, pecks by pile), the deposit flushes it has sent, and what each report does to its hold.
    /// Pure (the key is generic), so the EditMode suite drives the very code <c>ChickenCargo</c> runs.
    /// </summary>
    public sealed class FoodRequests<TKey> where TKey : struct
    {
        private readonly PendingStealLedger<TKey> _steals = FoodTransferRules.NewLedger<TKey>();
        private readonly PendingStealLedger<TKey> _pecks = FoodTransferRules.NewLedger<TKey>();
        private readonly PendingStealLedger<TKey> _deposits = FoodTransferRules.NewLedger<TKey>();

        /// <summary>Room held by requests that are open and unanswered (steals and pecks; a deposit reserves nothing).</summary>
        public float Reserved(float now) => _steals.Outstanding(now) + _pecks.Outstanding(now);

        /// <summary>Food sent to a base and neither banked nor bounced yet.</summary>
        public float DepositsInFlight(float now) => _deposits.Outstanding(now);

        /// <summary>Room the chicken may still ask food for.</summary>
        public float FreeSpace(float capacity, float cargo, float now) =>
            FoodTransferRules.FreeSpace(capacity, cargo, Reserved(now));

        /// <summary>Opens a peck on <paramref name="pile"/>. False (nothing to send) when there is no room.</summary>
        public bool AskPeck(TKey pile, float wanted, float capacity, float cargo, float now, out float amount, out ushort id) =>
            Ask(_pecks, pile, wanted, capacity, cargo, now, out amount, out id);

        /// <summary>Opens a steal on <paramref name="victim"/>. False (nothing to send) when there is no room.</summary>
        public bool AskSteal(TKey victim, float wanted, float capacity, float cargo, float now, out float amount, out ushort id) =>
            Ask(_steals, victim, wanted, capacity, cargo, now, out amount, out id);

        private bool Ask(PendingStealLedger<TKey> ledger, TKey owner, float wanted, float capacity, float cargo, float now,
            out float amount, out ushort id)
        {
            id = 0;
            amount = FoodTransferRules.Request(wanted, FreeSpace(capacity, cargo, now));
            if (amount <= 0f) return false;
            id = ledger.Open(owner, amount, now);
            return true;
        }

        /// <summary>Records a deposit flush of <paramref name="amount"/> (already debited from the hold) sent to <paramref name="playerBase"/>.</summary>
        public ushort SendDeposit(TKey playerBase, float amount, float now) => _deposits.Open(playerBase, amount, now);

        /// <summary>
        /// The pile's report for peck <paramref name="id"/>. The caller has verified the sender is the pile's own
        /// authority, so a report with no open request is still honoured, up to <paramref name="maxPeck"/>.
        /// </summary>
        public FoodSettlement SettlePeck(TKey pile, ushort id, float reported, float maxPeck, float capacity, float cargo, float now)
        {
            bool matched = _pecks.TryClose(pile, id, now, out float requested);
            return Credit(matched ? SettleOutcome.Matched : SettleOutcome.Late, requested,
                FoodTransferRules.Credit(matched ? requested : maxPeck, reported), capacity, cargo, now);
        }

        /// <summary>
        /// The victim's report for steal <paramref name="id"/>. Strict: with no such request open nothing is
        /// credited, because the sender is only known to own the named victim.
        /// </summary>
        public FoodSettlement SettleSteal(TKey victim, ushort id, float reported, float capacity, float cargo, float now)
        {
            if (!_steals.TryClose(victim, id, now, out float requested))
                return new FoodSettlement { Outcome = SettleOutcome.Refused };
            return Credit(SettleOutcome.Matched, requested, FoodTransferRules.Credit(requested, reported), capacity, cargo, now);
        }

        /// <summary>
        /// The base's answer to flush <paramref name="id"/>: <paramref name="banked"/> counts it in the stat, otherwise
        /// the food comes back. The caller has verified the sender is the base's own authority, so an answer with no
        /// open flush is still honoured, up to <paramref name="maxFlush"/>.
        /// </summary>
        public FoodSettlement SettleDeposit(TKey playerBase, ushort id, float reported, bool banked, float maxFlush,
            float capacity, float cargo, float now)
        {
            bool matched = _deposits.TryClose(playerBase, id, now, out float sent);
            var outcome = matched ? SettleOutcome.Matched : SettleOutcome.Late;
            float settled = FoodTransferRules.Credit(matched ? sent : maxFlush, reported);
            if (banked) return new FoodSettlement { Outcome = outcome, Requested = sent, Banked = settled };
            return Credit(outcome, sent, settled, capacity, cargo, now);
        }

        private FoodSettlement Credit(SettleOutcome outcome, float requested, float credit, float capacity, float cargo, float now)
        {
            FoodTransferRules.Place(credit, FreeSpace(capacity, cargo, now), out float toCargo, out float toBag);
            return new FoodSettlement { Outcome = outcome, Requested = requested, ToCargo = toCargo, ToBag = toBag };
        }

        /// <summary>Drops every open request and flush (new round).</summary>
        public void Clear()
        {
            _steals.Clear();
            _pecks.Clear();
            _deposits.Clear();
        }
    }
}

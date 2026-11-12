using UnityEngine;

namespace CluckWars.Gameplay
{
    /// <summary>
    /// The conservation rule for every food transfer between two state authorities: a peck (pile -> chicken), a steal
    /// (victim -> thief) and a deposit (chicken -> base). Pure and Fusion-free, like <see cref="StealRules"/>.
    /// </summary>
    /// <remarks>
    /// <b>The owner of the food debits first and reports the ACTUAL amount; the receiver credits only what was
    /// reported.</b> The same shape the Assassin's execute loot already had (<c>RPC_TransferAllToBountyBag</c> ->
    /// <c>RPC_CreditExecuteLoot</c>). Before this (audit A02, decision D3a) the receiver credited itself from a stale
    /// local estimate and the owner clamped silently: two peckers on a nearly empty pile both got a full bite, two
    /// thieves on one victim both got its whole load, and a deposit the base refused was simply destroyed.
    /// <list type="bullet">
    /// <item><b>Request.</b> The receiver asks for <see cref="Request"/> and records it in a
    /// <see cref="PendingStealLedger{TKey}"/> keyed by the owner. Nothing is credited yet.</item>
    /// <item><b>Debit.</b> The owner's authority removes what it really has (a pile: <c>FoodPileMath.Drain</c>; a
    /// chicken: <see cref="StealRules.SplitDrain"/>) and reports that amount, 0 included, so the request is closed.</item>
    /// <item><b>Settle.</b> The receiver consumes its oldest open request for that owner and credits
    /// <see cref="Credit"/>: never more than it asked for, so a report cannot be forged into extra food, and a report
    /// nobody asked for is refused.</item>
    /// </list>
    /// A deposit runs the other way round (the chicken is the owner and debits its cargo first), so its settlement is
    /// either "banked" (the stat is counted) or a bounce that gives the food back (<see cref="SplitRefund"/>).
    /// <para>
    /// <b>Capacity, and why there is no overflow.</b> Open requests reserve their room: <see cref="FreeSpace"/> subtracts
    /// them, so a second request made before the first settles cannot overbook the hold, and what arrives always
    /// fits. The one way the room can shrink in flight is a live loadout swap that lowers capacity (Ability Lab only);
    /// the credit is then still applied in full and the cargo sits above capacity until it is banked. Food is never
    /// created or destroyed by a settlement.
    /// </para>
    /// <para>
    /// <b>Accepted loss.</b> A report that arrives after <see cref="SettleWindowSeconds"/> finds no open request and is
    /// refused: the owner has debited and nobody is credited. That needs a 2.5 s stall on a two-hop message; the
    /// alternative (crediting unsolicited reports) is the forgery the ledger exists to stop.
    /// </para>
    /// </remarks>
    public static class FoodTransferRules
    {
        /// <summary>
        /// How long a request waits for its settlement. The execute loot's window, for its reason: request and report
        /// are two relayed trips in Shared Mode. Also how long an unanswered request keeps its room reserved.
        /// </summary>
        public const float SettleWindowSeconds = StealRules.ExecuteLootWindowSeconds;

        /// <summary>A ledger of open requests (or, for deposits, of food sent and not yet acknowledged).</summary>
        public static PendingStealLedger<TKey> NewLedger<TKey>() where TKey : struct =>
            new PendingStealLedger<TKey>(SettleWindowSeconds);

        /// <summary>Room a chicken may still ask food for: its capacity less what it holds and what it has already asked for.</summary>
        public static float FreeSpace(float capacity, float held, float reserved) =>
            Mathf.Max(0f, capacity - Mathf.Max(0f, held) - Mathf.Max(0f, reserved));

        /// <summary>What a receiver asks an owner for: what it wants, never more than its free room, never negative.</summary>
        public static float Request(float wanted, float freeSpace) =>
            Mathf.Max(0f, Mathf.Min(wanted, freeSpace));

        /// <summary>
        /// What a settlement is worth to the receiver: the reported amount, never more than the request it answers and
        /// never negative (a NaN report is worth nothing).
        /// </summary>
        public static float Credit(float requested, float reported) =>
            reported > 0f ? Mathf.Min(Mathf.Max(0f, requested), reported) : 0f;

        /// <summary>
        /// Where a bounced deposit goes back to: the cargo up to its free room (<see cref="FreeSpace"/>, so a refund never
        /// takes room an open request has reserved), the rest to the bounty bag, which has no cap and is banked first on
        /// the next deposit. The two parts always add up to <paramref name="refund"/>.
        /// </summary>
        public static void SplitRefund(float refund, float freeSpace, out float toCargo, out float toBag)
        {
            float amount = Mathf.Max(0f, refund);
            toCargo = Mathf.Min(amount, Mathf.Max(0f, freeSpace));
            toBag = amount - toCargo;
        }
    }
}

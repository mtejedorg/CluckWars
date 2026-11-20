namespace CluckWars.Gameplay
{
    /// <summary>The comeback effects the match authority can switch on for one chicken (one RPC; a bit per effect).</summary>
    [System.Flags]
    public enum ComebackGrant : byte
    {
        None          = 0,
        UnderdogSurge = 1 << 0,   // ChickenController.UnderdogSurgeActive
        // 1 << 1 was LeaderBounty (removed 2026-11-20, D7b): a stale bit from an older build is rejected by IsValid.
    }

    /// <summary>
    /// What a <c>ChickenController.RPC_GrantComeback</c> may do to the comeback flag, and which event a roll picks. Pure and Fusion-free.
    /// </summary>
    /// <remarks>
    /// A grant only ever switches a flag ON. Switching it off belongs to the round reset
    /// (<c>ChickenController.RPC_ResetControlStates</c>).
    /// </remarks>
    public static class ComebackRules
    {
        private const ComebackGrant Known = ComebackGrant.UnderdogSurge;

        /// <summary>The comeback events a roll can pick, in roll order (<see cref="EventForRoll"/>).</summary>
        public static readonly MatchEventKind[] Events =
        {
            MatchEventKind.GoldenPile, MatchEventKind.UnderdogSurge, MatchEventKind.Restock,
        };

        /// <summary>The event for <paramref name="roll"/>, an index in [0, <see cref="Events"/>.Length); out of range clamps.</summary>
        public static MatchEventKind EventForRoll(int roll) =>
            Events[roll < 0 ? 0 : roll >= Events.Length ? Events.Length - 1 : roll];

        /// <summary>True for a grant that names at least one effect and no bit this build does not know.</summary>
        public static bool IsValid(ComebackGrant grant) => grant != ComebackGrant.None && (grant & ~Known) == 0;

        /// <summary>The flag after <paramref name="grant"/>: the named effect on, otherwise as it was.</summary>
        public static void Apply(ComebackGrant grant, ref bool underdogSurge)
        {
            if (!IsValid(grant)) return;
            underdogSurge |= (grant & ComebackGrant.UnderdogSurge) != 0;
        }
    }
}

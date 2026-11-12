namespace CluckWars.Gameplay
{
    /// <summary>The comeback effects the match authority can switch on for one chicken (one RPC, one or both bits).</summary>
    [System.Flags]
    public enum ComebackGrant : byte
    {
        None          = 0,
        UnderdogSurge = 1 << 0,   // ChickenController.UnderdogSurgeActive
        LeaderBounty  = 1 << 1,   // ChickenController.LeaderBountyActive
    }

    /// <summary>
    /// What a <c>ChickenController.RPC_GrantComeback</c> may do to the two comeback flags. Pure and Fusion-free.
    /// </summary>
    /// <remarks>
    /// A grant only ever switches a flag ON. Switching them off belongs to the round reset
    /// (<c>ChickenController.RPC_ResetControlStates</c>), so a grant for one effect can never cancel the other.
    /// </remarks>
    public static class ComebackRules
    {
        private const ComebackGrant Known = ComebackGrant.UnderdogSurge | ComebackGrant.LeaderBounty;

        /// <summary>True for a grant that names at least one effect and no bit this build does not know.</summary>
        public static bool IsValid(ComebackGrant grant) => grant != ComebackGrant.None && (grant & ~Known) == 0;

        /// <summary>The flags after <paramref name="grant"/>: each named effect on, everything else as it was.</summary>
        public static void Apply(ComebackGrant grant, ref bool underdogSurge, ref bool leaderBounty)
        {
            if (!IsValid(grant)) return;
            underdogSurge |= (grant & ComebackGrant.UnderdogSurge) != 0;
            leaderBounty  |= (grant & ComebackGrant.LeaderBounty) != 0;
        }
    }
}

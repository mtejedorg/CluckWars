namespace CluckWars.Gameplay
{
    /// <summary>
    /// Which spawn corner a Shared-mode player takes (CW-233). Pure so the rule is tested without a runner.
    /// </summary>
    /// <remarks>
    /// The preferred slot comes from the player's own <c>PlayerId</c>, a value every peer agrees on the moment the
    /// player exists. It used to be the player's index in its local, PlayerId-sorted view of
    /// <c>runner.ActivePlayers</c>, which depends on which joins that peer had received yet: in session
    /// 20261022-100224 P3 saw {1,3} (index 1) and P2 saw {1,2,3} (index 1), so both spawned on corner 1 and the
    /// master warned "already claimed" every tick. PlayerIds 1..4 now map to slots 0..3 whatever the arrival order,
    /// matching the lobby, which colours seat i with slot i (<see cref="CornerAssignment.LobbySeatCorner"/>).
    /// <para>
    /// A rejoin gets a new PlayerId (5, 6, ...), which wraps onto an already used slot; <see cref="Resolve"/> then
    /// scans forward to the first corner no live chicken has stamped.
    /// </para>
    /// </remarks>
    public static class CornerSlotRules
    {
        /// <summary>The permutation slot (0..3) a player prefers: <c>(PlayerId - 1) mod 4</c>.</summary>
        public static int PreferredSlot(int playerId)
        {
            int slot = (playerId - 1) % CornerAssignment.Corners;
            return slot < 0 ? slot + CornerAssignment.Corners : slot;
        }

        /// <summary>
        /// The corner for <paramref name="preferredSlot"/>: <c>permutation[slot]</c> for the first slot from the
        /// preferred one onward whose corner is not set in <paramref name="occupiedCornerMask"/> (bit c = corner c
        /// taken). A null permutation is the identity (<c>MatchBootstrapper.ShuffledCorner</c>'s guard).
        /// Returns -1 when every corner is taken.
        /// </summary>
        public static int Resolve(int preferredSlot, int[] permutation, int occupiedCornerMask)
        {
            for (int offset = 0; offset < CornerAssignment.Corners; offset++)
            {
                int slot = (preferredSlot + offset) % CornerAssignment.Corners;
                int corner = permutation != null ? permutation[slot % permutation.Length] : slot;
                if ((occupiedCornerMask & (1 << corner)) == 0) return corner;
            }
            return -1;
        }
    }
}

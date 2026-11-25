namespace CluckWars.Gameplay
{
    /// <summary>
    /// One Enforcer's debt on one rival as the game keeps it (design 2026-11-25, 16.2): a count and the time it decays at, moved by the same
    /// <see cref="DebtRules"/> the match simulator calls. Pure and Fusion-free: <see cref="EnforcerDebts"/> reads a slot from its replicated arrays, applies
    /// one of these and writes it back.
    /// </summary>
    public static class DebtBook
    {
        /// <summary>A rival's debt: how many, and the simulation time at which the count goes to 0 (all at once) if nothing adds to it.</summary>
        public struct DebtSlot
        {
            public int Count;
            public float Until;
        }

        /// <summary>
        /// One more debt on the rival, stopping at <paramref name="cap"/> (<see cref="DebtRules.Add"/>); every gain, at the cap too, restarts the decay timer.
        /// A cap of 0 or less earns nothing and leaves the slot as it was.
        /// </summary>
        public static DebtSlot Gain(DebtSlot slot, int cap, float now, float seconds)
        {
            if (cap <= 0) return slot;
            return new DebtSlot { Count = DebtRules.Add(slot.Count, cap), Until = now + seconds };
        }

        /// <summary>
        /// The slot after time passed: once <see cref="DebtRules.Decayed"/> says the timer ran out, the whole count goes to 0 at once and
        /// <paramref name="lost"/> says how many were lost (0 when nothing decayed).
        /// </summary>
        public static DebtSlot Decay(DebtSlot slot, float now, out int lost)
        {
            if (slot.Count > 0 && DebtRules.Decayed(now, slot.Until))
            {
                lost = slot.Count;
                return default;
            }
            lost = 0;
            return slot;
        }

        /// <summary>An ask made at <paramref name="asked"/> that nobody answered by <paramref name="now"/> is dead once <paramref name="window"/> seconds passed.</summary>
        public static bool PendingExpired(float asked, float now, float window) => now - asked > window;

        /// <summary>
        /// A Steal that <paramref name="consumed"/> debts was answered with <paramref name="credited"/>: only a positive answer cashes them (the count falls by
        /// what the Steal used, never below 0, so a debt a Stun added inside the round trip is kept); an answer of 0 leaves the count untouched.
        /// </summary>
        public static DebtSlot Cash(DebtSlot slot, int consumed, float credited)
        {
            if (!(credited > 0f) || consumed <= 0) return slot;
            int left = slot.Count - consumed;
            if (left <= 0) return default;
            return new DebtSlot { Count = left, Until = slot.Until };
        }
    }
}

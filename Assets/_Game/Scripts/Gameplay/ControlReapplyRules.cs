namespace CluckWars.Gameplay
{
    /// <summary>
    /// What happens when a control effect lands on a chicken that is already under the same
    /// effect (A1, decisions 2026-10-21). Pure and Fusion-free so EditMode can pin every branch;
    /// <c>ChickenController.RPC_ApplyStun</c>, <c>RPC_ApplyRoot</c> and <c>RPC_ApplyAbilitySlow</c>
    /// call it after <c>ApplyPassiveControlDuration</c>, so the durations compared here are the
    /// ones the victim's passive already shortened.
    /// </summary>
    /// <remarks>
    /// <b>The bug this replaces.</b> Each RPC used to overwrite its timer outright, so a short
    /// effect landing on a long one cut it short: a 1.5 s Wing Slam stun followed by a 0.35 s
    /// Headbutt stagger left the victim stunned for 0.35 s. Re-applying control may extend it,
    /// never shorten it.
    /// </remarks>
    public static class ControlReapplyRules
    {
        /// <summary>
        /// Stun and root: the timer keeps whichever ends later, <c>max(remaining, incoming)</c>.
        /// NaN and negative inputs read as 0 (no time left / nothing incoming).
        /// </summary>
        public static float MergeTimer(float remaining, float incoming)
        {
            float r = Sanitize(remaining), i = Sanitize(incoming);
            return i > r ? i : r;
        }

        /// <summary>
        /// True when the incoming duration outlasts what is left, i.e. the timer must be
        /// restarted at <paramref name="incoming"/>. False keeps the running timer untouched,
        /// which is the only way to keep it exact (re-creating it would round to a tick).
        /// An incoming 0 (an immunity window, see <c>ApplyPassiveControlDuration</c>) never restarts.
        /// </summary>
        public static bool IncomingExtends(float remaining, float incoming) =>
            Sanitize(incoming) > Sanitize(remaining);

        /// <summary>A timed slow: the speed multiplier and the simulation time it ends at.</summary>
        public readonly struct TimedSlow
        {
            public readonly float Factor;
            public readonly double Until;

            public TimedSlow(float factor, double until)
            {
                Factor = factor;
                Until = until;
            }

            /// <summary>Running at <paramref name="now"/>: not yet ended and a real slow (factor in (0, 1)).</summary>
            public bool IsActiveAt(double now) => now < Until && Factor > 0f && Factor < 1f;
        }

        /// <summary>
        /// Timed slow: keeps the strongest factor (the smallest multiplier) and the later expiry.
        /// An incoming slow that is not active at <paramref name="now"/> (zero duration from an
        /// immunity window, or a factor outside (0, 1)) changes nothing; a current slow that has
        /// already ended is simply replaced.
        /// </summary>
        /// <remarks>
        /// <b>A deliberate simplification of GDD §7.4 "strongest wins".</b> A strong short slow
        /// followed by a weak long one keeps the strong factor until the weak one's later
        /// expiry, which is slightly stronger than tracking the two slows separately would be
        /// (the exact answer drops to the weak factor once the strong one ends). One timed-slow
        /// source ships today (Dust Kick, a single factor), so the case cannot arise between two
        /// different abilities yet; a two-slot slow list would be machinery for a case with no
        /// user. Revisit when a second timed slow with a different factor ships (fable review
        /// recommendation 9, 2026-10-22).
        /// </remarks>
        public static TimedSlow MergeSlow(TimedSlow current, TimedSlow incoming, double now)
        {
            if (!incoming.IsActiveAt(now)) return current;
            if (!current.IsActiveAt(now)) return incoming;
            float factor = incoming.Factor < current.Factor ? incoming.Factor : current.Factor;
            double until = incoming.Until > current.Until ? incoming.Until : current.Until;
            return new TimedSlow(factor, until);
        }

        private static float Sanitize(float seconds) => seconds > 0f ? seconds : 0f;   // NaN fails the comparison
    }
}

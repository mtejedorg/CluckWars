namespace CluckWars.Input
{
    /// <summary>
    /// J3 (spec 2026-11-06 E12a): while the Egg Shell slot is running AND in its egg phase, its hex shows the cracked egg and
    /// prints the egg's remaining seconds in tenths. Pure rules, no Unity objects; the HUD
    /// (<see cref="TouchControlsController"/>, touch arc and desktop / pad strip alike) feeds it the replicated slot state, the
    /// same read the status-badge row uses. The egg phase is the <c>ShieldRules</c> one (the run has <c>EggSeconds</c> or less
    /// left, matching <c>ChickenStatusBadges.ShellPhaseSeconds(egg: true, ...)</c>); nothing here decides gameplay.
    /// </summary>
    public static class EggPhaseHexRules
    {
        /// <summary>USS class laid over the Shield's normal hex icon class for the egg phase (the later rule wins).</summary>
        public const string EggIconClass = "cw-hex-icon--turtle-egg";

        /// <summary>
        /// Is the slot in its egg phase, and if so how many tenths of a second does it have left (rounded up, never 0)?
        /// <paramref name="tenths"/> is 0 outside the egg phase (inactive, or still walking).
        /// </summary>
        public static bool Resolve(bool slotActive, float remainingRunSeconds, float eggSeconds, out int tenths)
        {
            tenths = 0;
            if (!slotActive || remainingRunSeconds <= 0f || remainingRunSeconds > eggSeconds) return false;
            tenths = CasterStunCountdownRules.Tenths(remainingRunSeconds);
            return true;
        }

        /// <summary>"1.4s": the badge-row shape, invariant culture.</summary>
        public static string Format(int tenths) => CasterStunCountdownRules.Format(tenths) + "s";
    }
}

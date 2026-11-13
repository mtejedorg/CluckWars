using Fusion;

namespace CluckWars.Gameplay
{
    /// <summary>
    /// The one implementation of a timed effect window on a chicken (A16): how long is left, is it open, and the
    /// "a grant never cuts a running window short" rule. Every window of <c>ChickenController</c> (stun, root,
    /// control immunity, slow immunity, steal-proof) is a <c>[Networked] TickTimer</c> read and written through here;
    /// the properties stay on the controller, their bodies are these calls.
    /// </summary>
    public static class EffectWindow
    {
        /// <summary>Seconds left on <paramref name="timer"/>; 0 when it is not running or has expired.</summary>
        public static float Remaining(TickTimer timer, NetworkRunner runner) =>
            timer.ExpiredOrNotRunning(runner) ? 0f : (timer.RemainingTime(runner) ?? 0f);

        /// <summary>True while <paramref name="timer"/> runs. False without a runner (an unspawned chicken).</summary>
        public static bool IsOpen(TickTimer timer, NetworkRunner runner) =>
            runner != null && !timer.ExpiredOrNotRunning(runner);

        /// <summary>
        /// Should a grant of <paramref name="seconds"/> replace <paramref name="current"/>? True (with the new timer)
        /// when the grant outlasts what is still open (<see cref="ControlImmunityRules.ShouldReplace"/>); false
        /// without a runner, for a grant of 0 or less, or when the running window is the longer one.
        /// </summary>
        public static bool TryGrant(TickTimer current, NetworkRunner runner, float seconds, out TickTimer granted)
        {
            granted = current;
            if (runner == null || seconds <= 0f) return false;
            if (!ControlImmunityRules.ShouldReplace(Remaining(current, runner), seconds)) return false;
            granted = TickTimer.CreateFromSeconds(runner, seconds);
            return true;
        }
    }
}

using UnityEngine;

namespace CluckWars.Visuals
{
    /// <summary>
    /// The local player's "BACK IN" moment after an execute: raised by <c>ChickenCombat.Render</c> when the local chicken's
    /// removal ends (it is back at its base), read by the top bar's toast. Local presentation only.
    /// </summary>
    public static class LocalRespawnCue
    {
        /// <summary>Unscaled time of the last raise; negative infinity when never.</summary>
        public static float LastAt { get; private set; } = float.NegativeInfinity;

        public static void Raise() => LastAt = Time.unscaledTime;

        /// <summary>Seconds since the cue, for the toast's own timing.</summary>
        public static float SecondsSince => Time.unscaledTime - LastAt;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => LastAt = float.NegativeInfinity;
    }
}

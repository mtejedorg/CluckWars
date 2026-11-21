using UnityEngine;

namespace CluckWars.Gameplay
{
    /// <summary>
    /// Pure rules of the Peck cast (BL-206): how long it runs, what cuts it, how much food lands at its end.
    /// Fusion-free so EditMode tests can pin each decision without a runner.
    /// </summary>
    public static class PeckCastRules
    {
        /// <summary>Seconds one Peck runs for this class: <see cref="ChickenStatsSO.PeckCastSeconds"/> (read through
        /// BalanceBook like every class stat), or <paramref name="fallbackSeconds"/> when the stats are missing.</summary>
        public static float CastSeconds(ChickenStatsSO stats, float fallbackSeconds) =>
            stats != null && stats.PeckCastSeconds > 0f ? stats.PeckCastSeconds : fallbackSeconds;

        /// <summary>A stun or a shove cuts a running cast. A slow, a robbery or a self dodge never does.</summary>
        public static bool IsCut(bool stunned, bool knockedSinceLastTick) => stunned || knockedSinceLastTick;

        /// <summary>Food asked of the pile when the cast lands: the bite, bounded by the room left and what the pile
        /// still offers. Never negative.</summary>
        public static float LandingTake(float amount, float freeSpace, float available) =>
            Mathf.Max(0f, Mathf.Min(amount, Mathf.Min(freeSpace, available)));
    }
}

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

        /// <summary>A stun, a shove or a pile that has run out of food cuts a running cast. A slow, a robbery or a self dodge never does.</summary>
        public static bool IsCut(bool stunned, bool knockedSinceLastTick, bool targetGone) => stunned || knockedSinceLastTick || targetGone;

        /// <summary>Compares the chicken's shove id with the last one seen and always stores the current one, so a shove seen
        /// before the press never cuts a later cast. Returns true when the id changed (wraps 255 to 0 by design).</summary>
        public static bool ObserveKnock(ref byte seen, byte current)
        {
            bool knocked = current != seen;
            seen = current;
            return knocked;
        }

        /// <summary>The smallest <see cref="CluckWars.Abilities.PassiveAbilitySO.PeckCooldownMultiplier"/> any reader honours (a Peck every quarter of the class cooldown at the fastest).</summary>
        public const float MinCooldownMultiplier = 0.25f;

        /// <summary>
        /// The Peck cooldown of one chicken: the class cooldown times its passive's <see cref="CluckWars.Abilities.PassiveAbilitySO.PeckCooldownMultiplier"/>
        /// (1 for a chicken with no passive). The one place the game's <c>PeckAbilitySO</c> and the match simulator's kit builder both read it, so a balance
        /// delta that slows one kit's foraging reaches both. A multiplier below <see cref="MinCooldownMultiplier"/> counts as the minimum.
        /// </summary>
        public static float Cooldown(float classCooldown, float passiveMultiplier) =>
            classCooldown * Mathf.Max(passiveMultiplier, MinCooldownMultiplier);

        /// <summary>Food asked of the pile when the cast lands: the bite, bounded by the room left and what the pile
        /// still offers. Never negative.</summary>
        public static float LandingTake(float amount, float freeSpace, float available) =>
            Mathf.Max(0f, Mathf.Min(amount, Mathf.Min(freeSpace, available)));
    }
}

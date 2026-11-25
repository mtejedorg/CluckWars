using CluckWars.Abilities;

namespace CluckWars.Gameplay
{
    /// <summary>
    /// The Bully's hit-steal as numbers (design 2026-11-25): food a Stun, Shove or Slow that really affects a rival takes into the caster's ordinary cargo.
    /// Read once from the passive (<see cref="PassiveAbilitySO.HitSteal"/>), so the match simulator and, later, the game use the same values. Every
    /// amount 0 is off: the shipped Bully plays exactly as before.
    /// </summary>
    public struct HitStealFacts
    {
        public float Stun;
        public float Shove;
        public float Slow;
        /// <summary>Seconds a take locks the same victim against every hitter of the same caster.</summary>
        public float LockSeconds;

        public bool Active => Stun > 0f || Shove > 0f || Slow > 0f;

        /// <summary>Food one hit of <paramref name="job"/> asks for: 0 for every job but Stun, Shove and Slow (Peck and Jump never take).</summary>
        public float AmountFor(AbilityJob job)
        {
            switch (job)
            {
                case AbilityJob.Stun: return Stun;
                case AbilityJob.Shove: return Shove;
                case AbilityJob.Slow: return Slow;
                default: return 0f;
            }
        }

        public static HitStealFacts None => default;
    }

    /// <summary>The pure rules of the hit-steal, shared by the match simulator and the bot (<c>BotHitStealRules</c>).</summary>
    public static class HitStealRules
    {
        /// <summary>
        /// Food one hit takes: the amount, held to the caster's free room and to what the victim holds (<see cref="StealMath.Clamp"/>, the rule
        /// every steal shares). 0 when this victim is locked for this caster or the amount is not positive.
        /// </summary>
        public static float Take(float amount, float freeRoom, float victimStealable, bool locked)
        {
            if (locked || !(amount > 0f)) return 0f;
            return StealMath.Clamp(amount, freeRoom, victimStealable);
        }

        /// <summary>True while a victim's lock still runs at <paramref name="now"/>.</summary>
        public static bool Locked(float now, float lockUntil) => now < lockUntil;
    }
}

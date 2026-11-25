namespace CluckWars.Gameplay
{
    /// <summary>
    /// The bot's decisions about the Bully's hit-steal (design 2026-11-25, section 7), pure functions over facts. Called by the match simulator's P0
    /// (<c>ScriptedPolicy</c>) now and by <c>BotController</c> in the game slice, so what P0 measures is what the bot plays.
    /// </summary>
    public static class BotHitStealRules
    {
        public const int Stun = 0;
        public const int Shove = 1;
        public const int None = -1;

        /// <summary>Food one hit would take now: <see cref="HitStealRules.Take"/> with no refusal.</summary>
        public static float Gain(float amount, float victimStealable, float freeRoom, bool locked) =>
            HitStealRules.Take(amount, freeRoom, victimStealable, locked);

        /// <summary>Cast a ready Stun or Shove when some rival in its reach would give at least 1 food.</summary>
        public static bool CastForFood(float bestGainInReach) => bestGainInReach >= 1f;

        /// <summary>
        /// Which hitter to cast: <see cref="Stun"/> or <see cref="Shove"/>, by the larger total gain over the rivals it reaches. A tie goes to the
        /// shorter cooldown, which is the Shove's (kept so the Stun stays free for control). <see cref="None"/> when neither gains anything.
        /// </summary>
        public static int PickHitter(float stunGain, float shoveGain, float stunCooldown, float shoveCooldown)
        {
            if (stunGain <= 0f && shoveGain <= 0f) return None;
            if (stunGain > shoveGain) return Stun;
            if (shoveGain > stunGain) return Shove;
            return stunCooldown < shoveCooldown ? Stun : Shove;
        }

        /// <summary>Hunt a prey only if it would give at least 1 food and its lock ends before the walk to it does.</summary>
        public static bool WorthHunting(float gainIfUnlocked, float lockLeft, float walkSeconds) =>
            gainIfUnlocked >= 1f && lockLeft <= walkSeconds;
    }
}

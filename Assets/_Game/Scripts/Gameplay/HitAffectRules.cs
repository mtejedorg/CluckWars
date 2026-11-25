namespace CluckWars.Gameplay
{
    /// <summary>
    /// Whether a Stun or a Shove "affects" a rival (design 2026-11-25, 16.2): the one test the Bully's hit-steal and the Enforcer's debts share.
    /// The match simulator calls it inside one tick; in the game the caster calls it over the victim's replicated state to predict what the
    /// victim's own authority will decide, with the same primitives (<see cref="ControlReapplyRules.IncomingExtends"/> after the victim's passive
    /// shortened the stun, and the immune early return and knockback factor of <c>ApplyKnockback</c>). Pure, Fusion-free.
    /// </summary>
    public static class HitAffectRules
    {
        /// <summary>
        /// A Stun affects a rival that is not control-immune when its incoming seconds are above 0 and outlast the stun it already has
        /// (<see cref="ControlReapplyRules.IncomingExtends"/>); a stun that does not extend a longer one did not affect him.
        /// </summary>
        public static bool StunAffects(bool victimControlImmune, float victimStunLeft, float incomingSeconds) =>
            !victimControlImmune && incomingSeconds > 0f && ControlReapplyRules.IncomingExtends(victimStunLeft, incomingSeconds);

        /// <summary>
        /// A Shove affects a rival that is not control-immune when it still moves him after his knockback factor
        /// (<c>strength x max(0, factor)</c> above 0); a factor of 0 makes him immovable.
        /// </summary>
        public static bool ShoveMoves(bool victimControlImmune, float strength, float victimKnockbackFactor) =>
            !victimControlImmune && strength * (victimKnockbackFactor > 0f ? victimKnockbackFactor : 0f) > 0f;
    }
}

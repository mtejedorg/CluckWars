using UnityEngine;

namespace CluckWars.Gameplay
{
    /// <summary>
    /// When an ability's round-start cooldown (<see cref="Abilities.AbilityBaseSO.RoundStartCooldown"/>) runs out, pure. It is
    /// counted from GO, the tick the intro ends, so every round opens the same way: the first round and every PLAY AGAIN
    /// (<c>ResetWorldForNewRound</c> clears the slots, then <c>StartMatch</c> arms a fresh intro, and this applies on top).
    /// </summary>
    /// <remarks>
    /// Applied on the caster's state authority every tick while the intro runs (idempotent: the same expiry tick), so a slot
    /// clear or a loadout swap during the intro cannot wipe it. Never after GO: a chicken that spawns late or an authority that
    /// migrates mid-round must not restart a cooldown the round already paid.
    /// </remarks>
    public static class RoundStartCooldownRules
    {
        /// <summary>
        /// The tick the cooldown ends: <paramref name="seconds"/> after <paramref name="introEndTick"/> (GO). -1 when there is
        /// nothing to apply: no intro armed, a zero cooldown, or GO already behind <paramref name="nowTick"/>.
        /// </summary>
        public static int ExpiryTick(bool introArmed, int introEndTick, int nowTick, float seconds, float tickSeconds)
        {
            if (!introArmed || seconds <= 0f || tickSeconds <= 0f) return -1;
            if (nowTick > introEndTick) return -1;
            return introEndTick + Mathf.CeilToInt(seconds / tickSeconds - 1e-4f);
        }
    }
}

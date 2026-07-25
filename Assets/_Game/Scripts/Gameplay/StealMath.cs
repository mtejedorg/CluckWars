using UnityEngine;

namespace CluckWars.Gameplay
{
    /// <summary>The natural steal cap (spec §3.2): stolen = min(abilityValue, attacker
    /// free space, defender cargo), never negative. One implementation shared by every
    /// steal ability so the "loaded thief can't steal" rule can't drift per-ability.</summary>
    public static class StealMath
    {
        public static float Clamp(float abilityValue, float attackerFreeSpace, float defenderCargo)
            => Mathf.Max(0f, Mathf.Min(abilityValue, Mathf.Min(attackerFreeSpace, defenderCargo)));
    }
}

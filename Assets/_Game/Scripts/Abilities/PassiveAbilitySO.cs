using CluckWars.Gameplay;
using UnityEngine;

namespace CluckWars.Abilities
{
    /// <summary>
    /// Base ScriptableObject class for permanent class passives (ADR 0003 Decision 3).
    /// Passives live in a dedicated passive slot on <see cref="AbilityController"/>,
    /// have zero cooldown, and are registered on spawn via <see cref="OnActivate"/>.
    /// </summary>
    public abstract class PassiveAbilitySO : AbilityBaseSO
    {
        protected PassiveAbilitySO()
        {
            Duration = 0.05f;
            Cooldown = 0f;
            SlotKind = AbilitySlotKind.Character;
        }

        public override void OnDeactivate(AbilityContext ctx) { }
    }
}

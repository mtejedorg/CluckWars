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

        /// <summary>
        /// True for a class's <b>signature</b> passive — the one it gets by default and the
        /// one the GDD names as that class's identity (Warrior MIGHTY/Tough, Speedy SLIPPERY,
        /// Fatty IMMOVABLE, Assassin COMBO). False for the alternative fork.
        /// <para>
        /// This exists because "default passive" must NOT be "whichever passive happens to sit
        /// first in <c>AbilityRegistrySO.All</c>" — that array is authoring order, and it made
        /// Warrior default to Bracer and Speedy to Second Wind, both of which are alternatives.
        /// </para>
        /// </summary>
        public virtual bool IsSignature => false;

        public override void OnDeactivate(AbilityContext ctx) { }
    }
}

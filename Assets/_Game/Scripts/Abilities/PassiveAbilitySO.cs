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

        // ---- Effect hooks ---------------------------------------------------
        // Passives are permanent, so they cannot express their effect through
        // OnActivate/OnDeactivate the way an active ability does. Instead gameplay calls
        // these at its existing chokepoints. Base implementations are identity functions,
        // so a passive that overrides nothing is inert BY CHOICE rather than by accident.
        //
        // The four signature passives (Mighty/Slippery/Immovable/Combo) deliberately do NOT
        // override these — they are still driven by the older ChickenPassive enum path in
        // ChickenController, which predates this system and already works. Do not duplicate
        // their effects here or they will apply twice.

        /// <summary>Scale damage this chicken is about to deal. Called from <c>ChickenController.ApplyOutgoingDamage</c>.</summary>
        /// <param name="target">May be null when the caller has no single resolved target.</param>
        public virtual float ModifyOutgoingDamage(float amount, ChickenController self, ChickenController target) => amount;

        /// <summary>Scale damage this chicken is about to receive. Called from <c>ChickenCombat.RPC_ApplyDamage</c>.</summary>
        public virtual float ModifyIncomingDamage(float amount, ChickenController self) => amount;

        /// <summary>Scale the duration of an incoming control effect (slow/root). Called from <c>ChickenController</c>.</summary>
        public virtual float ModifyControlDuration(float seconds, ChickenController self) => seconds;

        public override void OnDeactivate(AbilityContext ctx) { }
    }
}

using CluckWars.Gameplay;
using UnityEngine;

namespace CluckWars.Abilities
{
    /// <summary>
    /// Base ScriptableObject for a class <b>specialization</b> — the permanent passive a
    /// player picks once at character select. ADR 0003 Decision 3 calls this "the class
    /// specialization": it lives in its own mandatory slot and never competes with an
    /// active ability for a button.
    /// </summary>
    /// <remarks>
    /// <b>Passives express themselves through the hooks below, never through OnActivate.</b>
    /// They are permanent, so there is no activation moment to hang an effect on; gameplay
    /// calls these at its existing chokepoints instead. Every base implementation is an
    /// identity function, so a passive that overrides nothing is inert <i>by choice</i>
    /// rather than by accident.
    ///
    /// <b>No per-owner state is available in here.</b> A <see cref="ScriptableObject"/> asset
    /// is shared by every chicken that equips it, so anything stored on <c>this</c> would be
    /// global across the whole match. Effects must be pure functions of their arguments.
    ///
    /// <para>
    /// ⚠️ <b>ModifyOutgoingDamage / ModifyIncomingDamage were deleted 2026-08-13.</b> They
    /// hooked <c>ChickenController.ApplyOutgoingDamage</c> and
    /// <c>ChickenCombat.RPC_ApplyDamage</c>, both of which were removed in the combat
    /// rewrite — GDD §2 is explicit that there is no HP and no damage. Three shipped
    /// passives (Mighty, Bracer, Opportunist) were built on them and were therefore
    /// completely inert, including Warrior's <i>signature</i>. Do not reintroduce a damage
    /// hook without reintroducing damage first.
    /// </para>
    /// </remarks>
    public abstract class PassiveAbilitySO : AbilityBaseSO
    {
        protected PassiveAbilitySO()
        {
            Duration = 0.05f;
            Cooldown = 0f;
            SlotKind = AbilitySlotKind.Character;
            Category = AbilityCategory.Utility;
        }

        /// <summary>
        /// True for a class's <b>signature</b> specialization — the one it gets by default.
        /// </summary>
        /// <remarks>
        /// This exists because "default passive" must NOT be "whichever passive happens to sit
        /// first in <c>AbilityRegistrySO.All</c>" — that array is authoring order, and it made
        /// Warrior default to Bracer and Speedy to Second Wind, i.e. both classes opened on
        /// the alternative fork.
        /// </remarks>
        public virtual bool IsSignature => false;

        // ---- Effect hooks -------------------------------------------------------
        // Each hook has exactly ONE production chokepoint, named in its doc comment. If you
        // add a hook, wire it at one place only — two call sites means the effect applies
        // twice for anyone who reads the code and assumes the other one is the real one.

        /// <summary>Scale the duration of an incoming control effect (slow / root / stun).
        /// Chokepoint: <c>ChickenController.ResolveControlSeconds</c>.</summary>
        public virtual float ModifyControlDuration(float seconds, ChickenController self) => seconds;

        /// <summary>Scale an incoming knockback impulse.
        /// Chokepoint: <c>ChickenController.ApplyKnockback</c>.</summary>
        public virtual float ModifyKnockback(float strength, ChickenController self) => strength;

        /// <summary>Adjust how much cargo this chicken can hold.
        /// Chokepoint: <c>ChickenCargo.Capacity</c>.</summary>
        public virtual float ModifyCargoCapacity(float capacity, ChickenController self) => capacity;

        /// <summary>
        /// Scale the cooldown this chicken is about to start on <paramref name="ability"/>.
        /// Chokepoint: <c>AbilityController.ResolveCooldownFor</c>.
        /// </summary>
        /// <remarks>
        /// The ability is passed in so a passive can exclude specific ones. Relentless does:
        /// Peck is on this list of cooldowns too, and a blanket reduction would quietly become
        /// a farming-throughput buff that sits OUTSIDE the Balance Oracle, breaking the SCT
        /// axiom for anyone who equipped it.
        /// </remarks>
        public virtual float ModifyCooldown(float seconds, AbilityBaseSO ability, ChickenController self) => seconds;

        /// <summary>Scale how much cargo this chicken takes per steal.
        /// Chokepoint: <c>AbilityBaseSO.ResolveStealAmount</c>, which every stealing ability calls.</summary>
        public virtual float ModifyStealAmount(float amount, ChickenController self) => amount;

        /// <summary>Scale how fast this chicken banks cargo at its base.
        /// Chokepoint: <c>ChickenCargo.ResolveDepositRate</c>.</summary>
        public virtual float ModifyDepositRate(float perSecond, ChickenController self) => perSecond;

        /// <summary>True if this chicken ignores the GDD §6.2 pile-slow.
        /// Chokepoint: <c>ChickenController.IsPileSlowed</c>.</summary>
        public virtual bool IgnoresPileSlow(ChickenController self) => false;

        /// <summary>
        /// Extra food banked if the match timer expires with <b>no</b> winner. Spoiler's whole
        /// mechanic. Chokepoint: <c>GameManager</c>'s timer-expiry resolution.
        /// </summary>
        public virtual int MatchEndBonusFood(ChickenController self) => 0;

        /// <summary>
        /// Called on the state authority each tick while a traversal window is open (not while
        /// unsticking). Lets a passive react to <i>how</i> its owner is moving through terrain.
        /// Chokepoint: <c>ChickenController</c>'s traversal tick.
        /// </summary>
        public virtual void OnTraversalTick(ChickenController self, TerrainTraversal tier) { }

        public override void OnActivate(AbilityContext ctx) { }
        public override void OnDeactivate(AbilityContext ctx) { }
    }
}

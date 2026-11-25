using System;
using CluckWars.Gameplay;
using CluckWars.Localization;
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
            Category = AbilityCategory.Utility;
        }

        /// <summary>
        /// Identity of this specialization — the key the Balance Editor and
        /// <see cref="PreEquippedLoadout"/> validation use. Exactly one passive asset per value.
        /// </summary>
        [Tooltip("Which subclass this specialization is. Unique across all passives; must match " +
                 "this passive's single AllowedClasses bit.")]
        public ChickenSubclass Subclass;

        [Tooltip("The four abilities this specialization plays in LoadoutMode.Fixed, in slot order. References to the " +
                 "pool assets, never copies. Checked by FixedKit (EditMode test + Balance Editor roster panel).")]
        public AbilityBaseSO[] Kit = Array.Empty<AbilityBaseSO>();

        // The Peck-slot / Signature-slot pre-equips are NOT stored here any more. The ability owns
        // the assignment (AbilityBaseSO.PeckSlotPreEquippedBy + SignaturePreEquippedBy), resolved by
        // PreEquippedLoadout.Resolve — so two abilities claiming one slot for a subclass is
        // visible data, flagged by validation, instead of one field silently overwriting another.
        // (Maestro, 2026-08-21: "we should make one ability the forced class ability" — this is
        // that mechanism, generalised to two slots per subclass.)

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

        // ---- Player-facing perk text -------------------------------------------------
        // Each specialization names its one-line summary (and optional longer detail) by
        // UiKeys key, and supplies the named numbers those templates use FROM ITS OWN
        // FIELDS. Nothing here is authored prose with a number typed into it, so changing a
        // value in a .asset changes the text; PerkTextTests fails if the two ever disagree.
        // If a perk's MECHANIC changes (not just its numbers), rewrite its template.

        /// <summary>Passives are described by <see cref="PerkLine"/>/<see cref="PerkDetail"/> (they need the MatchConfig), not by an ability template.</summary>
        public override string DescriptionText => Description;

        /// <summary>UiKeys template for the one-line perk summary, or null if this passive has none.</summary>
        public virtual string PerkLineKey => null;

        /// <summary>UiKeys template for the longer perk explanation, or null if there is none.</summary>
        public virtual string PerkDetailKey => null;

        /// <summary>
        /// The named arguments for <see cref="PerkLineKey"/> and <see cref="PerkDetailKey"/>,
        /// computed from this passive's fields. <paramref name="match"/> supplies the one value
        /// a passive does not own (Spoiler's win target); pass the injected MatchConfig.
        /// </summary>
        public virtual (string name, object value)[] PerkArgs(MatchConfigSO match) =>
            Array.Empty<(string, object)>();

        /// <summary>The one-line perk summary. Falls back to <c>Description</c> only for a passive with no template.</summary>
        public string PerkLine(MatchConfigSO match = null) =>
            PerkLineKey != null ? UiText.Format(PerkLineKey, PerkArgs(match)) : Description;

        /// <summary>The longer perk explanation. Falls back to <see cref="PerkLine"/> when there is no detail.</summary>
        public string PerkDetail(MatchConfigSO match = null) =>
            PerkDetailKey != null ? UiText.Format(PerkDetailKey, PerkArgs(match)) : PerkLine(match);

        /// <summary>Whole-percent rounding for perk numbers (0.25 becomes 25).</summary>
        protected static int Percent(float fraction) => Mathf.RoundToInt(fraction * 100f);

        // ---- Effect hooks -------------------------------------------------------
        // Each hook has exactly ONE production chokepoint, named in its doc comment. If you
        // add a hook, wire it at one place only — two call sites means the effect applies
        // twice for anyone who reads the code and assumes the other one is the real one.

        /// <summary>Scale the duration of an incoming timed control effect of <paramref name="kind"/> (a timed ability slow,
        /// a root or a stun). The kind lets a passive resist some kinds only: Slippery shortens slows and leaves stuns and
        /// roots whole (Maestro 2026-11-05); Bulwark and Relentless shorten every kind.
        /// Chokepoint: <c>ChickenController.ApplyPassiveControlDuration</c>.</summary>
        public virtual float ModifyControlDuration(float seconds, ControlKind kind, ChickenController self) => seconds;

        /// <summary>
        /// Scale the strength of an incoming ABILITY slow: <paramref name="factor"/> is the speed multiplier the slow sets
        /// (0.55 = 55% speed), and the return is the one this chicken actually moves at. Pile and bump slows never reach it.
        /// Chokepoint: <c>ChickenController.ApplyAbilitySlow</c>, the only writer of <see cref="SlowSource.Ability"/>, which
        /// both the timed ability slow (<c>RPC_ApplyAbilitySlow</c>) and every zone / aura slow (<c>CheckAbilityZoneSlow</c>)
        /// go through each tick.
        /// </summary>
        public virtual float ModifyIncomingSlowFactor(float factor, ChickenController self) => factor;

        /// <summary>
        /// Factor on the stuns and slows this chicken APPLIES to others (Relentless, 2026-11-05). 1 for every
        /// passive that does not lengthen its own crowd control.
        /// Chokepoint: <c>AbilityBaseSO.ResolveOutgoingControlSeconds</c>, which every stun / slow sender calls.
        /// </summary>
        /// <remarks>
        /// A constant per asset, not a hook with a <c>self</c> argument, on purpose: the receiver-side bound on the
        /// control RPCs (<c>ControlClaimRules.CeilingFor</c>) must know the worst case across the shipped registry
        /// with no caster to probe, exactly like <see cref="MaxStealMultiplier"/>. One value read by both the sender
        /// and the ceiling means the two can never disagree. A future passive whose factor varies with match state
        /// must split this into a hook and a declared maximum, as steals are.
        /// <para>
        /// Order with the victim's <see cref="ModifyControlDuration"/>: the caster's factor is applied first, on the
        /// caster's authority, before the RPC; the victim's shortening is applied on arrival. A Relentless stun on a
        /// Slippery victim lasts authored x 1.3 (Slippery no longer shortens stuns); a Relentless aura slow on a Slippery
        /// victim lives 3 x 1.3 = 3.9 s and slows it only for the first 0.6 x 3.9 = 2.34 s.
        /// </para>
        /// </remarks>
        public virtual float OutgoingControlMultiplier => 1f;

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

        /// <summary>
        /// The largest factor <see cref="ModifyStealAmount"/> can ever multiply a steal by.
        /// 1 for a passive that does not scale steals.
        /// </summary>
        /// <remarks>
        /// The receiver-side bound on <c>ChickenCargo.RPC_DrainStolen</c> is derived from this
        /// (<c>StealRules.MaxSingleSteal</c>), and the receiver has no caster to hand
        /// <see cref="ModifyStealAmount"/> — the RPC arrives on the <i>victim</i>. Declaring the
        /// ceiling separately is what lets the bound be read off the shipped registry without
        /// inventing a chicken to probe the hook with.
        /// <para>
        /// It is a ceiling and not the value itself so a future passive whose multiplier varies
        /// with match state (a comeback scaler, say) can still state its worst case.
        /// <c>StealRulesTests</c> pins every passive that overrides
        /// <see cref="ModifyStealAmount"/> against this override.
        /// </para>
        /// </remarks>
        public virtual float MaxStealMultiplier => 1f;

        /// <summary>Scale how fast this chicken banks cargo at its base.
        /// Chokepoint: <c>ChickenCargo.ResolveDepositRate</c>.</summary>
        public virtual float ModifyDepositRate(float perSecond, ChickenController self) => perSecond;

        /// <summary>True if this chicken ignores the GDD §6.2 pile-slow.
        /// Chokepoint: the pile-slow branch of <c>ChickenController.FixedUpdateNetwork</c>.</summary>
        public virtual bool IgnoresPileSlow(ChickenController self) => false;

        /// <summary>True if this chicken ignores the GDD §6.1 collision slow (brushing another chicken).
        /// Pure. Chokepoint: <c>ChickenController.CheckCollisionSlow</c>, which still runs its contact
        /// tracking and the Spine Coat steal-back; only the slow itself is skipped.</summary>
        public virtual bool IgnoresCollisionSlow(ChickenController self) => false;

        /// <summary>
        /// Extra food banked if the match timer expires with <b>no</b> winner. Spoiler's whole
        /// mechanic. Chokepoint: <c>GameManager</c>'s timer-expiry resolution.
        /// </summary>
        public virtual int MatchEndBonusFood(ChickenController self) => 0;

        /// <summary>
        /// The Thief's loot mark as numbers (BL-217); <see cref="LootMarkFacts.None"/> for every other passive. Read by the match simulator
        /// as a property, not called from the game: nothing in the game reads it yet.
        /// </summary>
        public virtual LootMarkFacts LootMark => LootMarkFacts.None;

        /// <summary>
        /// Multiplier on this passive's chickens' <b>Peck</b> cooldown (foraging cadence): 1 is the class cooldown (shipped on every passive), 1.25 a Peck
        /// 25 % slower. Read through <see cref="PeckCastRules.Cooldown"/> by <c>PeckAbilitySO.ResolveCooldown</c> and the match simulator's kit builder, so a
        /// balance delta can slow one kit's foraging without touching its class. Does not touch any other ability's cooldown (<see cref="ModifyCooldown"/>).
        /// </summary>
        [Tooltip("Multiplier on the Peck cooldown of this passive's chickens. 1 = the class cooldown (shipped). 1.25 = a Peck 25 percent slower.")]
        [Min(PeckCastRules.MinCooldownMultiplier)] public float PeckCooldownMultiplier = 1f;

        /// <summary>The Bully's hit-steal as numbers (design 2026-11-25); <see cref="HitStealFacts.None"/> for every other passive. Read by the
        /// match simulator as a property; nothing in the game reads it yet.</summary>
        public virtual HitStealFacts HitSteal => HitStealFacts.None;

        /// <summary>The Champion's bite as numbers; a fresh window of 0 (the default) is no bite. Read by the match simulator only.</summary>
        public virtual BiteFacts Bite => BiteFacts.None;

        /// <summary>The Champion's per-enemy trophies as numbers; <see cref="TrophyFacts.None"/> for every other passive. Read by the match simulator only.</summary>
        public virtual TrophyFacts Trophy => TrophyFacts.None;

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

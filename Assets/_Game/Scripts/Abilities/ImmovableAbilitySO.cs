using UnityEngine;

namespace CluckWars.Abilities
{
    /// <summary>
    /// <b>Fatty.</b> Plants himself. For a few seconds nothing stuns, roots, slows or shoves
    /// him at all.
    /// </summary>
    /// <remarks>
    /// <b>Immunity, not resistance, and the difference is the whole ability.</b> Bulwark
    /// already shortens incoming control by scaling its duration, and scaling can never reach
    /// zero — so a resistance-shaped version of this would just be a bigger Bulwark, i.e. a
    /// worse copy of a passive the same class already owns. Fatty's essence is the
    /// "slow but unstoppable force"; that only reads if there is a window where control simply
    /// does not land.
    ///
    /// <b>It does not clear control already on him.</b> Walking into a stomp and then pressing
    /// the button would make it a free escape from any mistake. It stops the NEXT one, so it
    /// has to be spent in anticipation — which is what makes it a read rather than a reflex.
    ///
    /// The immunity is enforced at two chokepoints in <c>ChickenController</c> rather than in
    /// this file: <c>ApplyPassiveControlDuration</c> (every slow, stun and root funnels through
    /// it) and <c>ApplyKnockback</c>. That placement means a control effect added later is
    /// immune by default instead of quietly bypassing this.
    ///
    /// <b>The counterplay is cargo.</b> Immunity does not stop theft — Snatch, Sneaky Steal and
    /// Dive Bomb still take from him while he stands there being unmovable. A Fatty who presses
    /// this while full is protecting the wrong thing.
    /// </remarks>
    [CreateAssetMenu(fileName = "Immovable",
        menuName = "Cluck Wars/Ability/Defense/Immovable", order = 7)]
    public sealed class ImmovableAbilitySO : AbilityBaseSO
    {
        public ImmovableAbilitySO()
        {
            Category = AbilityCategory.Defense;
            SlotKind = AbilitySlotKind.Character;
            AllowedClasses = ChickenClassFlags.Fatty;
            BotRole = BotRole.Defense;
            Duration = 3f;
            Cooldown = 12f;
        }

        protected override string DefaultIcon => "🧱";

        // Self-only: nothing is aimed at, so the indicator marks the caster's own ring.
        public override AbilityAimShape AimShape => AbilityAimShape.None;
        public override bool AffectsSelf => true;
        public override bool AffectsEnemies => false;

        public override void OnActivate(AbilityContext ctx)
        {
            // Granted for the full Duration rather than ticked, so the window survives the
            // caster being mid-anything when it opens.
            ctx.Controller.GrantControlImmunity(Duration);
        }

        /// <summary>
        /// Nothing to undo: the immunity is a TickTimer that expires on its own. Clearing it
        /// here would end the window early if the ability were ever deactivated by something
        /// other than its own duration running out.
        /// </summary>
        public override void OnDeactivate(AbilityContext ctx) { }
    }
}

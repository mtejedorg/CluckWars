using CluckWars.Gameplay;
using UnityEngine;

namespace CluckWars.Abilities
{
    [CreateAssetMenu(fileName = "SpineCoat",
        menuName = "Cluck Wars/Ability/Defense/Spine Coat", order = 3)]
    public sealed class SpineCoatAbilitySO : AbilityBaseSO
    {
        public SpineCoatAbilitySO()
        {
            Category = AbilityCategory.Defense;
            SlotKind = AbilitySlotKind.Character;
            AllowedClasses = ChickenClassFlags.Fatty;
        }

        [Tooltip("How much cargo to steal back on contact.")]
        [Min(1f)] public float StealBackAmount = 4f;

        protected override string DefaultIcon => "🦔";

        /// <summary>
        /// <inheritdoc cref="AbilityBaseSO.NominalStealAmount"/>
        /// </summary>
        /// <remarks>
        /// Spine Coat steals without an aim shape and without an <c>OnActivate</c> hit: it arms
        /// <c>ChickenController.StealBackAmount</c> and the drain fires later, from
        /// <c>CheckCollisionSlow</c>, on whoever walks into the wearer. Declaring the amount
        /// anyway is what keeps the receiver's bound honest — it is a real
        /// <c>RPC_DrainStolen</c> caller, just not one that resolves a target itself. Its reach
        /// contributes nothing to <c>StealRules.MaxReach</c> because
        /// <see cref="AbilityAimShape.None"/> has no radius, which is correct: contact range is
        /// far inside the pool's longest reach.
        /// </remarks>
        public override float NominalStealAmount => StealBackAmount;

        // Self-buff, no target area — marks the caster's own ring instead (FEEDBACK.md §2.2).
        public override AbilityAimShape AimShape => AbilityAimShape.None;
        public override bool AffectsSelf => true;
        public override bool AffectsEnemies => false;

        public override void OnActivate(AbilityContext ctx)
        {
            ctx.Controller.StealBackActive = true;
            ctx.Controller.StealBackAmount = StealBackAmount;
        }

        public override void OnDeactivate(AbilityContext ctx)
        {
            ctx.Controller.StealBackActive = false;
        }
    }
}

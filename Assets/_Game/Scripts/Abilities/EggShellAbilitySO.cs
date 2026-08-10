using UnityEngine;

namespace CluckWars.Abilities
{
    [CreateAssetMenu(fileName = "EggShell", menuName = "Cluck Wars/Ability/Egg Shell", order = 1)]
    public sealed class EggShellAbilitySO : AbilityBaseSO
    {
        protected override string DefaultIcon => "🥚";

        // Self-buff, no target area — marks the caster's own ring instead (FEEDBACK.md §2.2).
        public override AbilityAimShape AimShape => AbilityAimShape.None;
        public override bool AffectsSelf => true;
        public override bool AffectsEnemies => false;

        public override void OnActivate(AbilityContext ctx)
        {
            ctx.Controller.MovementLocked = true;
        }

        public override void OnDeactivate(AbilityContext ctx)
        {
            ctx.Controller.MovementLocked = false;
        }
    }
}

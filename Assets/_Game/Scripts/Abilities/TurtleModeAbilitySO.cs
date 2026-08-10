using UnityEngine;

namespace CluckWars.Abilities
{
    [CreateAssetMenu(fileName = "TurtleMode", menuName = "Cluck Wars/Ability/Turtle Mode", order = 2)]
    public sealed class TurtleModeAbilitySO : AbilityBaseSO
    {
        [Tooltip("Movement speed scalar while active. 0.25 = quarter speed, slow but still mobile.")]
        [Range(0.05f, 1f)] public float SpeedMultiplier = 0.25f;

        protected override string DefaultIcon => "🐢";

        // Self-buff, no target area — marks the caster's own ring instead (FEEDBACK.md §2.2).
        public override AbilityAimShape AimShape => AbilityAimShape.None;
        public override bool AffectsSelf => true;
        public override bool AffectsEnemies => false;

        public override void OnActivate(AbilityContext ctx)
        {
            ctx.Controller.MoveSpeedMultiplier = SpeedMultiplier;
        }

        public override void OnDeactivate(AbilityContext ctx)
        {
            ctx.Controller.MoveSpeedMultiplier = 1f;
        }
    }
}

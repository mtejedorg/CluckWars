using UnityEngine;

namespace CluckWars.Abilities
{
    [CreateAssetMenu(fileName = "EggShell", menuName = "Cluck Wars/Ability/Egg Shell", order = 1)]
    public sealed class EggShellAbilitySO : AbilityBaseSO
    {
        protected override string DefaultIcon => "🥚";

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

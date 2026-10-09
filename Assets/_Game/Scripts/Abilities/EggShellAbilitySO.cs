using UnityEngine;
using CluckWars.Localization;

namespace CluckWars.Abilities
{
    [CreateAssetMenu(fileName = "EggShell", menuName = "Cluck Wars/Ability/Egg Shell", order = 1)]
    public sealed class EggShellAbilitySO : AbilityBaseSO
    {
        public override string DescriptionKey => UiKeys.AbilityEggShellDesc;
        public override (string name, object value)[] DescriptionArgs() =>
            new (string, object)[] { ("secs", Num(Duration)) };


        // Self-buff, no target area — marks the caster's own ring instead (FEEDBACK.md §2.2).
        public override AbilityAimShape AimShape => AbilityAimShape.None;
        public override bool AffectsSelf => true;
        public override bool AffectsEnemies => false;

        public override void OnActivate(AbilityContext ctx)
        {
            ctx.Controller.Effects.SetMovementLock(ctx.Slot);
        }

        public override void OnDeactivate(AbilityContext ctx)
        {
            ctx.Controller.Effects.RemoveMovementLock(ctx.Slot);
        }
    }
}

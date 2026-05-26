using UnityEngine;

namespace CluckWars.Abilities
{
    /// <summary>
    /// Become invulnerable inside an egg — completely immobile while active. Defensive
    /// ability: blocks incoming damage but you can't reposition. Toggles
    /// <c>ChickenController.MovementLocked</c> + <c>ChickenController.DamageImmune</c>;
    /// both reset on deactivate.
    /// </summary>
    [CreateAssetMenu(fileName = "EggShell", menuName = "Cluck Wars/Ability/Egg Shell", order = 1)]
    public sealed class EggShellAbilitySO : AbilityBaseSO
    {
        protected override string DefaultIcon => "🥚";

        public override void OnActivate(AbilityContext ctx)
        {
            ctx.Controller.MovementLocked = true;
            ctx.Controller.DamageImmune = true;
        }

        public override void OnDeactivate(AbilityContext ctx)
        {
            ctx.Controller.MovementLocked = false;
            ctx.Controller.DamageImmune = false;
        }
    }
}

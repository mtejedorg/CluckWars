using UnityEngine;

namespace CluckWars.Abilities
{
    /// <summary>
    /// While active, any incoming damage is bounced back to the attacker. The
    /// reflector takes no damage themselves. Reactive ability — punishes anyone
    /// foolish enough to swing while you have it on. Toggles
    /// <c>ChickenController.ReflectDamage</c>.
    /// </summary>
    [CreateAssetMenu(fileName = "SpineCoat", menuName = "Cluck Wars/Ability/Spine Coat", order = 3)]
    public sealed class SpineCoatAbilitySO : AbilityBaseSO
    {
        public override void OnActivate(AbilityContext ctx)
        {
            ctx.Controller.ReflectDamage = true;
        }

        public override void OnDeactivate(AbilityContext ctx)
        {
            ctx.Controller.ReflectDamage = false;
        }
    }
}

using CluckWars.Gameplay;
using UnityEngine;

namespace CluckWars.Abilities
{
    /// <summary>
    /// Spine Coat — Defense ability. While active, any incoming damage is
    /// reflected back to the attacker (attacker takes the hit; defender takes
    /// none). Also applies a knockback impulse to push the attacker away.
    /// Toggles <see cref="ChickenController.ReflectDamage"/> and
    /// <see cref="ChickenController.SpineCoatKnockbackStrength"/>.
    /// </summary>
    /// <remarks>
    /// Cooldown tier: Medium. Default values are balance-pass placeholders.
    /// </remarks>
    [CreateAssetMenu(fileName = "SpineCoat",
        menuName = "Cluck Wars/Ability/Spine Coat", order = 3)]
    public sealed class SpineCoatAbilitySO : AbilityBaseSO
    {
        [Tooltip("Knockback impulse (world-units/sec) pushed onto the attacker on reflect. 0 = no knockback.")]
        [Min(0f)] public float ReflectKnockback = 8f;

        public override void OnActivate(AbilityContext ctx)
        {
            ctx.Controller.ReflectDamage               = true;
            ctx.Controller.SpineCoatKnockbackStrength  = ReflectKnockback;
        }

        public override void OnDeactivate(AbilityContext ctx)
        {
            ctx.Controller.ReflectDamage               = false;
            ctx.Controller.SpineCoatKnockbackStrength  = 0f;
        }
    }
}

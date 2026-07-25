using CluckWars.Gameplay;
using UnityEngine;

namespace CluckWars.Abilities
{
    [CreateAssetMenu(fileName = "SpineCoat",
        menuName = "Cluck Wars/Ability/Spine Coat", order = 3)]
    public sealed class SpineCoatAbilitySO : AbilityBaseSO
    {
        [Tooltip("Knockback impulse (world-units/sec) pushed onto the attacker on reflect. 0 = no knockback.")]
        [Min(0f)] public float ReflectKnockback = 8f;

        protected override string DefaultIcon => "🦔";

        public override void OnActivate(AbilityContext ctx)
        {
            // TODO(ability-plan): convert to steal/stun (Task 7 hooks steal-back)
        }

        public override void OnDeactivate(AbilityContext ctx) { }
    }
}

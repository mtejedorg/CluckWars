using CluckWars.Gameplay;
using UnityEngine;

namespace CluckWars.Abilities
{
    [CreateAssetMenu(fileName = "CluckShock",
        menuName = "Cluck Wars/Ability/Damage/Cluck Shock", order = 6)]
    public sealed class CluckShockAbilitySO : AbilityBaseSO
    {
        [Tooltip("Shock radius around the caster.")]
        [Min(0.5f)] public float ShockRadius = 2.5f;

        [Tooltip("Base damage placeholder.")]
        [Min(1f)] public float ShockDamage = 35f;

        protected override string DefaultIcon => "⚡";

        public override float IndicatorRange => ShockRadius;
        public override bool RequiresEnemyInRange => true;

        public override void OnActivate(AbilityContext ctx)
        {
            // TODO(ability-plan): convert to steal/stun
        }

        public override void OnDeactivate(AbilityContext ctx) { }
    }
}

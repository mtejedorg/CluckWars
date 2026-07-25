using CluckWars.Gameplay;
using UnityEngine;

namespace CluckWars.Abilities
{
    [CreateAssetMenu(fileName = "Peck",
        menuName = "Cluck Wars/Ability/Damage/Peck", order = 7)]
    public sealed class PeckAbilitySO : AbilityBaseSO
    {
        [Tooltip("Maximum distance to the target.")]
        [Min(0.5f)] public float PeckRange = 2.0f;

        [Tooltip("Base damage placeholder.")]
        [Min(1f)] public float PeckDamage = 28f;

        [Tooltip("Knockback impulse strength (world-units/sec) applied to the hit target.")]
        [Min(0f)] public float KnockbackStrength = 6f;

        protected override string DefaultIcon => "🐦";

        public override float IndicatorRange => PeckRange;
        public override bool RequiresEnemyInRange => true;

        public override void OnActivate(AbilityContext ctx)
        {
            // TODO(ability-plan): convert to steal/stun
        }

        public override void OnDeactivate(AbilityContext ctx) { }
    }
}

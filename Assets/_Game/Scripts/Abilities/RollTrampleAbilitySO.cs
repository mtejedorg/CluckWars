using CluckWars.Gameplay;
using UnityEngine;

namespace CluckWars.Abilities
{
    [CreateAssetMenu(fileName = "FlyingPeck", menuName = "Cluck Wars/Ability/Damage/Flying Peck", order = 5)]
    public sealed class RollTrampleAbilitySO : AbilityBaseSO
    {
        public RollTrampleAbilitySO()
        {
            TerrainTraversal = TerrainTraversal.Vault;
            SlotKind = AbilitySlotKind.Character;
            AllowedClasses = ChickenClassFlags.Warrior;
        }

        [Tooltip("How far in front of the caster the sweep is centered.")]
        [Min(0.5f)] public float ForwardOffset = 1.6f;

        [Tooltip("Sweep radius around the offset point. Generous — catches chickens slightly off-line.")]
        [Min(0.5f)] public float SweepRadius = 1.8f;

        [Tooltip("Base damage placeholder.")]
        [Min(1f)] public float TrampleDamage = 50f;

        protected override string DefaultIcon => "🪽";

        public override float IndicatorRange => ForwardOffset + SweepRadius;
        public override bool RequiresEnemyInRange => true;

        public override void OnActivate(AbilityContext ctx)
        {
            // TODO(ability-plan): convert to steal/stun
        }

        public override void OnDeactivate(AbilityContext ctx) { }
    }
}

using CluckWars.Gameplay;
using UnityEngine;

namespace CluckWars.Abilities
{
    [CreateAssetMenu(fileName = "FlyingPeck", menuName = "Cluck Wars/Ability/Steal/Flying Peck", order = 5)]
    public sealed class RollTrampleAbilitySO : AbilityBaseSO
    {
        public RollTrampleAbilitySO()
        {
            Category = AbilityCategory.Steal;
            TerrainTraversal = TerrainTraversal.Vault;
            SlotKind = AbilitySlotKind.Character;
            AllowedClasses = ChickenClassFlags.Warrior;
        }

        [Tooltip("How far in front of the caster the sweep is centered.")]
        [Min(0.5f)] public float ForwardOffset = 1.6f;

        [Tooltip("Sweep radius around the offset point. Generous — catches chickens slightly off-line.")]
        [Min(0.5f)] public float SweepRadius = 1.8f;

        [Tooltip("Amount of cargo to steal on contact.")]
        [Min(1f)] public float StealAmount = 8f;

        protected override string DefaultIcon => "🪽";

        public override float IndicatorRange => ForwardOffset + SweepRadius;
        public override bool RequiresEnemyInRange => true;

        public override bool IsUsable(ChickenController caster)
            => HasEnemyInRange(caster, ForwardOffset + SweepRadius, requireCargo: true);

        public override void OnActivate(AbilityContext ctx)
        {
            var thief = ctx.Controller;
            var thiefCargo = thief.Cargo;
            if (thiefCargo == null) return;

            var center = thief.transform.position + thief.transform.forward * ForwardOffset;
            var hits = Physics.OverlapSphere(center, SweepRadius, SearchMask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < hits.Length; i++)
            {
                var targetCtrl = hits[i].GetComponentInParent<ChickenController>();
                if (targetCtrl == null || targetCtrl == thief) continue;
                if (targetCtrl.Combat != null && targetCtrl.Combat.IsDead) continue;

                var targetCargo = targetCtrl.Cargo;
                if (targetCargo != null && targetCargo.Cargo > 0f)
                {
                    float freeSpace = thiefCargo.Capacity - thiefCargo.Cargo;
                    float stolen = StealMath.Clamp(StealAmount, freeSpace, targetCargo.Cargo);
                    if (stolen > 0f)
                    {
                        thiefCargo.Cargo += stolen;
                        targetCargo.RPC_DrainStolen(stolen);
                        break; // Steals on contact with first rival in lane
                    }
                }
            }
        }

        public override void OnDeactivate(AbilityContext ctx) { }
    }
}

using CluckWars.Gameplay;
using UnityEngine;

namespace CluckWars.Abilities
{
    [CreateAssetMenu(fileName = "Peck",
        menuName = "Cluck Wars/Ability/Steal/Peck", order = 7)]
    public sealed class PeckAbilitySO : AbilityBaseSO
    {
        public PeckAbilitySO()
        {
            Category = AbilityCategory.Steal;
            SlotKind = AbilitySlotKind.Common;
            AllowedClasses = ChickenClassFlags.All;
        }

        [Tooltip("Maximum distance to the target.")]
        [Min(0.5f)] public float PeckRange = 2.0f;

        [Tooltip("Amount of cargo to steal per hit target.")]
        [Min(1f)] public float StealAmount = 5f;

        [Tooltip("Knockback impulse strength (world-units/sec) applied to the hit target.")]
        [Min(0f)] public float KnockbackStrength = 6f;

        protected override string DefaultIcon => "🐦";

        public override float IndicatorRange => PeckRange;
        public override bool RequiresEnemyInRange => true;

        public override bool IsUsable(ChickenController caster)
            => HasEnemyInRange(caster, PeckRange, requireCargo: true);

        public override void OnActivate(AbilityContext ctx)
        {
            var thief = ctx.Controller;
            var thiefCargo = thief.Cargo;
            if (thiefCargo == null) return;

            var hits = Physics.OverlapSphere(thief.transform.position, PeckRange, SearchMask, QueryTriggerInteraction.Ignore);
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
                    }
                }

                if (KnockbackStrength > 0f)
                {
                    var dir = (targetCtrl.transform.position - thief.transform.position);
                    dir.y = 0f;
                    if (dir.sqrMagnitude < 0.001f) dir = thief.transform.forward;
                    targetCtrl.RPC_ApplyKnockback(dir.normalized * KnockbackStrength);
                }
            }
        }

        public override void OnDeactivate(AbilityContext ctx) { }
    }
}

using CluckWars.Gameplay;
using UnityEngine;

namespace CluckWars.Abilities
{
    /// <summary>
    /// Shared base class for AoE-around-self stun abilities (Ambush & Wing Slam).
    /// Calls target.Controller.RPC_ApplyStun(StunDuration) for each rival in radius.
    /// </summary>
    public abstract class StunBurstAbilitySO : AbilityBaseSO
    {
        public StunBurstAbilitySO()
        {
            Category = AbilityCategory.Control;
            SlotKind = AbilitySlotKind.Character;
        }

        [Tooltip("Stun radius around the caster.")]
        [Min(0.5f)] public float StunRadius = 2.0f;

        [Tooltip("Stun duration applied to hit rivals.")]
        [Min(0.1f)] public float StunDuration = 1.0f;

        public override float IndicatorRange => StunRadius;
        public override bool RequiresEnemyInRange => true;

        public override void OnActivate(AbilityContext ctx)
        {
            var caster = ctx.Controller;
            var center = caster.transform.position;
            var hits = Physics.OverlapSphere(center, StunRadius, SearchMask, QueryTriggerInteraction.Ignore);

            for (int i = 0; i < hits.Length; i++)
            {
                var targetCtrl = hits[i].GetComponentInParent<ChickenController>();
                if (targetCtrl == null || targetCtrl == caster) continue;
                if (targetCtrl.Combat != null && targetCtrl.Combat.IsDead) continue;

                targetCtrl.RPC_ApplyStun(StunDuration);
            }
        }

        public override void OnDeactivate(AbilityContext ctx) { }
    }
}

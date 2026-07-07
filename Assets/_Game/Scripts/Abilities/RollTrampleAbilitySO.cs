using CluckWars.Gameplay;
using UnityEngine;

namespace CluckWars.Abilities
{
    /// <summary>
    /// Flying Peck — damage ability. On activate, finds every chicken in a forward
    /// cone (approximated with an <see cref="Physics.OverlapSphere"/> centered
    /// ahead of the caster) and slams them with damage.
    /// </summary>
    /// <remarks>
    /// <b>Class name unchanged</b> — kept as <c>RollTrampleAbilitySO</c> so the
    /// existing <c>.asset</c> serialized reference (GUID-bound) remains valid.
    /// The forward dash + animation from the original v0.2 "Roll &amp; Trample"
    /// design are Part B work.
    ///
    /// <b>Tough passive:</b> caster's outgoing damage is scaled via
    /// <see cref="ChickenController.ApplyOutgoingDamage"/> before the RPC fires.
    /// </remarks>
    [CreateAssetMenu(fileName = "FlyingPeck", menuName = "Cluck Wars/Ability/Damage/Flying Peck", order = 5)]
    public sealed class RollTrampleAbilitySO : AbilityBaseSO
    {
        [Tooltip("How far in front of the caster the sweep is centered.")]
        [Min(0.5f)] public float ForwardOffset = 1.6f;

        [Tooltip("Sweep radius around the offset point. Generous — catches chickens slightly off-line.")]
        [Min(0.5f)] public float SweepRadius = 1.8f;

        [Tooltip("Base damage dealt to each chicken in the sweep. Scaled up by Warrior's Tough passive.")]
        [Min(1f)] public float TrampleDamage = 50f;

        protected override string DefaultIcon => "🪽";

        public override float IndicatorRange => ForwardOffset + SweepRadius;
        public override bool RequiresEnemyInRange => true;

        public override void OnActivate(AbilityContext ctx)
        {
            var caster = ctx.Controller;
            var casterCombat = caster.Combat;
            if (casterCombat == null) return;

            var center = caster.transform.position + caster.transform.forward * ForwardOffset;
            var hits = Physics.OverlapSphere(center, SweepRadius, ~0, QueryTriggerInteraction.Ignore);

            // Tough passive: Warrior deals extra damage with all damage abilities.
            float finalDamage = caster.ApplyOutgoingDamage(TrampleDamage);

            for (int i = 0; i < hits.Length; i++)
            {
                var target = hits[i].GetComponentInParent<ChickenCombat>();
                if (target == null || target == casterCombat || target.IsDead) continue;
                target.RPC_ApplyDamage(finalDamage, casterCombat.Id);
            }
        }

        public override void OnDeactivate(AbilityContext ctx)
        {
            // No persistent state to clear — the sweep was a one-shot at activation.
        }
    }
}

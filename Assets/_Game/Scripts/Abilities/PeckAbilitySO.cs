using CluckWars.Gameplay;
using UnityEngine;

namespace CluckWars.Abilities
{
    /// <summary>
    /// Peck — Damage ability. Instant short-range HP hit + minor knockback on
    /// the nearest enemy in range. Deals damage via
    /// <see cref="ChickenCombat.RPC_ApplyDamage"/> and pushes the target away via
    /// <see cref="ChickenController.RPC_ApplyKnockback"/>. Both respect passives
    /// (Tough scales damage; Immovable reduces knockback on the target).
    /// </summary>
    /// <remarks>
    /// Cooldown tier: Short (3–6 s). Default values are balance-pass placeholders.
    /// </remarks>
    [CreateAssetMenu(fileName = "Peck",
        menuName = "Cluck Wars/Ability/Damage/Peck", order = 7)]
    public sealed class PeckAbilitySO : AbilityBaseSO
    {
        [Tooltip("Maximum distance to the target.")]
        [Min(0.5f)] public float PeckRange = 2.0f;

        [Tooltip("Base damage. Scaled by Warrior's Tough passive.")]
        [Min(1f)] public float PeckDamage = 15f;

        [Tooltip("Knockback impulse strength (world-units/sec) applied to the hit target.")]
        [Min(0f)] public float KnockbackStrength = 6f;

        protected override string DefaultIcon => "🐦";

        public override void OnActivate(AbilityContext ctx)
        {
            var caster = ctx.Controller;
            var casterCombat = caster.Combat;
            if (casterCombat == null) return;

            // Find nearest enemy in range.
            ChickenCombat bestTarget = null;
            ChickenController bestCtrl = null;
            float bestSqr = PeckRange * PeckRange;

            var hits = Physics.OverlapSphere(
                caster.transform.position, PeckRange, ~0,
                QueryTriggerInteraction.Ignore);

            for (int i = 0; i < hits.Length; i++)
            {
                var targetCombat = hits[i].GetComponentInParent<ChickenCombat>();
                if (targetCombat == null || targetCombat == casterCombat || targetCombat.IsDead) continue;
                float sqr = (targetCombat.transform.position - caster.transform.position).sqrMagnitude;
                if (sqr < bestSqr)
                {
                    bestSqr    = sqr;
                    bestTarget = targetCombat;
                    bestCtrl   = targetCombat.GetComponent<ChickenController>();
                }
            }

            if (bestTarget == null) return;

            float finalDamage = caster.ApplyOutgoingDamage(PeckDamage);
            bestTarget.RPC_ApplyDamage(finalDamage, caster.Object.InputAuthority);

            // Knockback: direction away from caster.
            if (bestCtrl != null && KnockbackStrength > 0f)
            {
                var dir = (bestCtrl.transform.position - caster.transform.position);
                dir.y = 0f;
                if (dir.sqrMagnitude < 0.001f) dir = caster.transform.forward;
                bestCtrl.RPC_ApplyKnockback(dir.normalized * KnockbackStrength);
            }
        }

        public override void OnDeactivate(AbilityContext ctx) { }
    }
}

using CluckWars.Gameplay;
using UnityEngine;

namespace CluckWars.Abilities
{
    /// <summary>
    /// Cluck Shock — Damage ability. One-shot AoE HP burst centered on self.
    /// Scans every chicken in a sphere around the caster, applies damage to
    /// each via <see cref="ChickenCombat.RPC_ApplyDamage"/>. Warrior's Tough
    /// passive is applied via <see cref="ChickenController.ApplyOutgoingDamage"/>.
    /// </summary>
    /// <remarks>
    /// Cooldown tier: Medium (8–12 s). Default values are balance-pass placeholders.
    /// </remarks>
    [CreateAssetMenu(fileName = "CluckShock",
        menuName = "Cluck Wars/Ability/Damage/Cluck Shock", order = 6)]
    public sealed class CluckShockAbilitySO : AbilityBaseSO
    {
        [Tooltip("Shock radius around the caster.")]
        [Min(0.5f)] public float ShockRadius = 2.5f;

        [Tooltip("Base damage to each chicken in range. Scaled by Warrior's Tough passive.")]
        [Min(1f)] public float ShockDamage = 20f;

        protected override string DefaultIcon => "⚡";

        public override void OnActivate(AbilityContext ctx)
        {
            var caster = ctx.Controller;
            var casterCombat = caster.Combat;
            if (casterCombat == null) return;

            float finalDamage = caster.ApplyOutgoingDamage(ShockDamage);
            var hits = Physics.OverlapSphere(
                caster.transform.position, ShockRadius, ~0,
                QueryTriggerInteraction.Ignore);

            for (int i = 0; i < hits.Length; i++)
            {
                var target = hits[i].GetComponentInParent<ChickenCombat>();
                if (target == null || target == casterCombat || target.IsDead) continue;
                target.RPC_ApplyDamage(finalDamage, caster.Object.InputAuthority);
            }
        }

        public override void OnDeactivate(AbilityContext ctx) { }
    }
}

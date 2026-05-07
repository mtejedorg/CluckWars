using CluckWars.Gameplay;
using UnityEngine;

namespace CluckWars.Abilities
{
    /// <summary>
    /// One-shot offensive sweep: on activate, finds every chicken in a forward cone
    /// (approximated with an <see cref="Physics.OverlapSphere"/> centered ahead of
    /// the caster) and slams them with damage hefty enough to send them into death
    /// stun. Active duration is mostly cosmetic — the gameplay impact happens at
    /// activation time. <c>OnDeactivate</c> is a no-op.
    /// </summary>
    /// <remarks>
    /// Polishing the actual "roll" animation + a forward dash on the caster is
    /// Phase 9 work. For now the chicken stays put and only the AoE strike fires
    /// — enough to validate the mechanic and the AbilityController architecture.
    /// </remarks>
    [CreateAssetMenu(fileName = "RollTrample", menuName = "Cluck Wars/Ability/Roll & Trample", order = 5)]
    public sealed class RollTrampleAbilitySO : AbilityBaseSO
    {
        [Tooltip("How far in front of the caster the sweep is centered.")]
        [Min(0.5f)] public float ForwardOffset = 1.6f;

        [Tooltip("Sweep radius around the offset point. Generous — catches chickens slightly off-line.")]
        [Min(0.5f)] public float SweepRadius = 1.8f;

        [Tooltip("Damage dealt to each chicken in the sweep. Default = enough to instant-stun a full-HP target.")]
        [Min(1f)] public float TrampleDamage = 200f;

        public override void OnActivate(AbilityContext ctx)
        {
            var caster = ctx.Controller;
            var casterCombat = caster.Combat;
            if (casterCombat == null) return;

            var center = caster.transform.position + caster.transform.forward * ForwardOffset;
            var hits = Physics.OverlapSphere(center, SweepRadius, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < hits.Length; i++)
            {
                var target = hits[i].GetComponentInParent<ChickenCombat>();
                if (target == null || target == casterCombat || target.IsDead) continue;
                target.RPC_ApplyDamage(TrampleDamage, caster.Object.InputAuthority);
            }
        }

        public override void OnDeactivate(AbilityContext ctx)
        {
            // No persistent state to clear — the sweep was a one-shot at activation.
        }
    }
}

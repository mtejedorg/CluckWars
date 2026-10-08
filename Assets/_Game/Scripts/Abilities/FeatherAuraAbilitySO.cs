using CluckWars.Gameplay;
using UnityEngine;

namespace CluckWars.Abilities
{
    /// <summary>
    /// Feather Aura — Control ability. The caster emits a feather cloud for
    /// <see cref="AbilityBaseSO.Duration"/> seconds. Any chicken within
    /// <see cref="AuraRadius"/> metres is slowed by <see cref="AuraSlowFactor"/>
    /// while the aura is active.
    /// </summary>
    /// <remarks>
    /// No per-tick RPC overhead: the caster broadcasts <see cref="ChickenController.AuraSlowActive"/>,
    /// <see cref="ChickenController.AuraSlowRadius"/>, and <see cref="ChickenController.AuraSlowFactor"/>
    /// as <c>[Networked]</c> properties. Nearby chickens self-apply the slow in
    /// their own <c>FixedUpdateNetwork</c> via <c>CheckAuraSlow()</c>.
    ///
    /// Cooldown tier: Medium.
    /// </remarks>
    [CreateAssetMenu(fileName = "FeatherAura",
        menuName = "Cluck Wars/Ability/Control/Feather Aura", order = 10)]
    public sealed class FeatherAuraAbilitySO : AbilityBaseSO
    {
        [Tooltip("Radius of the slow aura around the caster.")]
        [Min(0.5f)] public float AuraRadius = 5.4f;

        [Tooltip("Speed multiplier applied to chickens inside the aura (< 1 = slower).")]
        [Range(0.1f, 0.9f)] public float AuraSlowFactor = 0.55f;

        public override AbilityAimShape AimShape => AbilityAimShape.Aura;
        public override float AimRadius => AuraRadius;

        public override void OnActivate(AbilityContext ctx)
        {
            ctx.Controller.Effects.SetAura(ctx.Slot, AuraSlowFactor, AuraRadius);
        }

        public override void OnDeactivate(AbilityContext ctx)
        {
            ctx.Controller.Effects.RemoveAura(ctx.Slot);
        }
    }
}

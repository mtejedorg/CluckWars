using CluckWars.Gameplay;
using Fusion;
using UnityEngine;

namespace CluckWars.Abilities
{
    /// <summary>
    /// Feather Trap — Control ability. Throws a feather cloud
    /// <see cref="ForwardOffset"/> metres ahead of the caster. Any chicken
    /// walking through the resulting <see cref="AbilityZone"/> is slowed by
    /// <see cref="SlowFactor"/> for the zone's lifetime. The zone
    /// auto-despawns after <see cref="ZoneDuration"/> seconds.
    /// </summary>
    /// <remarks>
    /// Slow is applied passively: each chicken's authority calls
    /// <see cref="ChickenController.CheckAbilityZoneSlow"/> every
    /// <c>FixedUpdateNetwork</c> tick — no per-tick RPC overhead.
    /// Speedy's Slippery passive does <em>not</em> reduce zone-slow duration
    /// (zones persist independently of the target). Cooldown tier: Medium.
    ///
    /// Requires <see cref="PrefabRegistrySO.AbilityZone"/> to be assigned.
    /// </remarks>
    [CreateAssetMenu(fileName = "FeatherTrap",
        menuName = "Cluck Wars/Ability/Control/Feather Trap", order = 9)]
    public sealed class FeatherTrapAbilitySO : AbilityBaseSO
    {
        [Tooltip("Distance in front of the caster where the zone center is placed.")]
        [Min(0f)] public float ForwardOffset = 2.5f;

        [Tooltip("Trigger radius of the slow zone.")]
        [Min(0.5f)] public float ZoneRadius = 2.0f;

        [Tooltip("How long the feather cloud persists (seconds) before auto-despawning.")]
        [Min(0.5f)] public float ZoneDuration = 5.0f;

        [Tooltip("Speed multiplier applied to chickens inside the zone (< 1 = slower).")]
        [Range(0.1f, 0.9f)] public float SlowFactor = 0.45f;

        protected override string DefaultIcon => "🪤";

        public override void OnActivate(AbilityContext ctx)
        {
            if (ctx.Runner == null || ctx.PrefabRegistry == null || ctx.PrefabRegistry.AbilityZone == null)
            {
                Debug.LogWarning("[FeatherTrap] Runner or AbilityZone prefab not available in context.");
                return;
            }

            var caster = ctx.Controller;
            var pos    = caster.transform.position + caster.transform.forward * ForwardOffset;
            pos.y      = caster.transform.position.y; // keep on ground plane

            // Capture SO values for the lambda (closures on 'this' can be stale if
            // the SO is unloaded, so capture into locals to be safe).
            float capDuration   = ZoneDuration;
            float capSlowFactor = SlowFactor;
            float capRadius     = ZoneRadius;

            ctx.Runner.Spawn(
                ctx.PrefabRegistry.AbilityZone,
                pos,
                Quaternion.identity,
                inputAuthority: null,
                onBeforeSpawned: (runner, networkObject) =>
                {
                    var zone = networkObject.GetComponent<AbilityZone>();
                    if (zone == null) return;
                    zone.Effect          = ZoneEffect.Slow;
                    zone.SlowFactor      = capSlowFactor;
                    zone.NetworkedRadius = capRadius;
                    zone.LifetimeTimer   = TickTimer.CreateFromSeconds(runner, capDuration);
                });
        }

        public override void OnDeactivate(AbilityContext ctx) { }
    }
}

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
        private const string Source = "FeatherTrap";

        [Tooltip("Distance in front of the caster where the zone center is placed.")]
        [Min(0f)] public float ForwardOffset = 4.5f;

        [Tooltip("Trigger radius of the slow zone.")]
        [Min(0.5f)] public float ZoneRadius = 3.6f;

        [Tooltip("How long the feather cloud persists (seconds) before auto-despawning.")]
        [Min(0.5f)] public float ZoneDuration = 5.0f;

        [Tooltip("Speed multiplier applied to chickens inside the zone (< 1 = slower).")]
        [Range(0.1f, 0.9f)] public float SlowFactor = 0.45f;

        protected override string DefaultIcon => "🪤";

        // Places a zone rather than hitting targets directly, so OnActivate has no
        // GatherTargets loop — this descriptor exists purely so the hold-to-aim
        // preview (Stage 3) can draw the zone's real footprint before it's thrown.
        public override AbilityAimShape AimShape => AbilityAimShape.ForwardCircle;
        public override float AimRadius => ZoneRadius;
        public override float AimForwardOffset => ForwardOffset;

        // The cast lands a cloud, not a hit — chickens are slowed later, by the zone. A trap
        // thrown at empty ground (the normal, correct play) reports 0 targets, so cast-time
        // hit/whiff styling must skip it entirely. See AbilityBaseSO.ReportsCastHits.
        public override bool PlacesZone => true;

        // Unassigned wiring, not a gameplay state — CanSpawnZone logs which reference is
        // missing. Sits in CanActivate rather than at the top of OnActivate so the refusal
        // lands before the cast and its cooldown are committed. The logger reaches an ability
        // through AbilityContext because SOs are assets Zenject cannot inject; read
        // AbilityContext.Log before reaching for anything shorter.
        public override bool CanActivate(AbilityContext ctx) => ctx.CanSpawnZone(Source, "Feather Trap");

        public override void OnActivate(AbilityContext ctx)
        {
            var caster = ctx.Controller;
            var pos    = caster.transform.position + caster.transform.forward * ForwardOffset;
            pos.y      = caster.transform.position.y; // keep on ground plane

            // Capture SO values for the lambda (closures on 'this' can be stale if
            // the SO is unloaded, so capture into locals to be safe).
            float capDuration   = ZoneDuration;
            float capSlowFactor = SlowFactor;
            float capRadius     = ZoneRadius;
            var   capOwner      = caster.Id;
            // Captured like the rest so the abandoned-spawn report below closes over the logger
            // rather than over ctx.
            var   capLog        = ctx.Log;

            ctx.Runner.Spawn(
                ctx.PrefabRegistry.AbilityZone,
                pos,
                Quaternion.identity,
                inputAuthority: null,
                onBeforeSpawned: (runner, networkObject) =>
                {
                    var zone = networkObject.GetComponent<AbilityZone>();
                    if (zone == null)
                    {
                        capLog?.Error(Source, "Feather Trap leaked a zone: the prefab wired to "
                            + "PrefabRegistrySO.AbilityZone has no AbilityZone component, so its lifetime "
                            + "timer was never set. The NetworkObject has ALREADY spawned and will now "
                            + "persist for the rest of the match doing nothing — fix the prefab.");
                        return;
                    }
                    zone.Effect          = ZoneEffect.Slow;
                    zone.OwnerChicken    = capOwner;
                    zone.SlowFactor      = capSlowFactor;
                    zone.NetworkedRadius = capRadius;
                    zone.LifetimeTimer   = TickTimer.CreateFromSeconds(runner, capDuration);
                });
        }

        public override void OnDeactivate(AbilityContext ctx) { }
    }
}

using CluckWars.Gameplay;
using Fusion;
using UnityEngine;

namespace CluckWars.Abilities
{
    /// <summary>
    /// Root Egg — Control ability. Places an egg zone at the caster's feet.
    /// The first chicken to step on it is rooted for <see cref="RootDuration"/>
    /// seconds; the zone then self-destructs (consumed on first trigger). If no
    /// chicken triggers it within <see cref="EggLifetime"/> seconds the egg
    /// despawns automatically.
    /// </summary>
    /// <remarks>
    /// Speedy's Slippery passive reduces the root duration applied to the target
    /// (handled inside <see cref="ChickenController.RPC_ApplyRoot"/>).
    ///
    /// Cooldown tier: Medium. Requires <see cref="PrefabRegistrySO.AbilityZone"/>
    /// to be assigned.
    /// </remarks>
    [CreateAssetMenu(fileName = "RootEgg",
        menuName = "Cluck Wars/Ability/Control/Root Egg", order = 11)]
    public sealed class RootEggAbilitySO : AbilityBaseSO
    {
        [Tooltip("Seconds before the egg despawns if no chicken triggers it.")]
        [Min(0.5f)] public float EggLifetime = 8.0f;

        [Tooltip("How long the triggered chicken is rooted (seconds, before Slippery reduction).")]
        [Min(0.1f)] public float RootDuration = 2.0f;

        [Tooltip("Trigger radius within which a chicken activates the root.")]
        [Min(0.3f)] public float EggRadius = 1.45f;

        protected override string DefaultIcon => "🌱";

        // Placed zone, not a direct hit — no GatherTargets loop in OnActivate, same
        // as Feather Trap. The egg spawns at the caster's own feet (offset 0); the
        // descriptor describes that placement rather than inventing a tuned offset.
        public override AbilityAimShape AimShape => AbilityAimShape.ForwardCircle;
        public override float AimRadius => EggRadius;
        public override float AimForwardOffset => 0f;

        // The cast never hits anyone — the egg does, later, in AbilityZone's own tick. With
        // AimForwardOffset 0 and AffectsSelf false the caster is the only chicken inside the
        // shape at cast time and is excluded, so LastCastHitCount is 0 on EVERY cast on open
        // ground. Without this the whiff styling fired on every correctly-placed egg.
        public override bool PlacesZone => true;

        public override void OnActivate(AbilityContext ctx)
        {
            if (ctx.Runner == null || ctx.PrefabRegistry == null || ctx.PrefabRegistry.AbilityZone == null)
            {
                // TODO(logging): SOs have no injected ILogService; surface this once
                // AbilityContext exposes the caster's logger. Do NOT add a service locator.
                return;
            }

            var pos = ctx.Controller.transform.position;

            float capLifetime      = EggLifetime;
            float capRootDuration  = RootDuration;
            float capRadius        = EggRadius;
            var   capOwner         = ctx.Controller.Id;

            ctx.Runner.Spawn(
                ctx.PrefabRegistry.AbilityZone,
                pos,
                Quaternion.identity,
                inputAuthority: null,
                onBeforeSpawned: (runner, networkObject) =>
                {
                    var zone = networkObject.GetComponent<AbilityZone>();
                    if (zone == null) return;
                    zone.Effect          = ZoneEffect.Root;
                    zone.OwnerChicken    = capOwner;
                    zone.RootDuration    = capRootDuration;
                    zone.NetworkedRadius = capRadius;
                    zone.LifetimeTimer   = TickTimer.CreateFromSeconds(runner, capLifetime);
                    // Consumed starts false — AbilityZone.FUN sets it true on first trigger.
                });
        }

        public override void OnDeactivate(AbilityContext ctx) { }
    }
}

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
        [Min(0.3f)] public float EggRadius = 0.8f;

        protected override string DefaultIcon => "🌱";

        public override void OnActivate(AbilityContext ctx)
        {
            if (ctx.Runner == null || ctx.PrefabRegistry == null || ctx.PrefabRegistry.AbilityZone == null)
            {
                Debug.LogWarning("[RootEgg] Runner or AbilityZone prefab not available in context.");
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

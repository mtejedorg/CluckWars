using CluckWars.Gameplay;
using Fusion;
using UnityEngine;

namespace CluckWars.Abilities
{
    /// <summary>
    /// <b>Assassin.</b> Drops a choking cloud at his own feet: he fades inside it, everyone
    /// else stumbles through it.
    /// </summary>
    /// <remarks>
    /// <b>The two halves are the design.</b> Invisibility already fades the Assassin, and
    /// Feather Trap already slows people — but neither one <i>creates the ambush</i>. This does
    /// both at the same place at the same time, so the cloud is somewhere rivals do not want to
    /// walk and cannot see into. That is "kill from the shadows" as a piece of ground rather
    /// than a status on a chicken.
    ///
    /// <b>Placed at the caster's feet, not thrown ahead</b> (<c>ForwardOffset</c> is 0, unlike
    /// Feather Trap's 4.5). Thrown, it would be a second Feather Trap for a class that already
    /// out-duels people; underfoot, it is a place to <i>stand</i> — which is what pairs with
    /// Mark/Kill's isolate-and-execute setup.
    ///
    /// <b>Concealment is a self-buff, disorientation is a zone, and they are separately
    /// timed.</b> The fade lasts this ability's <c>Duration</c>; the cloud persists for
    /// <see cref="ZoneDuration"/> and keeps slowing whoever enters after he has gone. So
    /// leaving early costs him the cover but not the trap.
    ///
    /// Reuses the existing <see cref="AbilityZone"/> infrastructure rather than inventing a
    /// concealment volume: the "breaks sightlines" half is carried by the caster's own
    /// <c>VisualOpacity</c>, exactly as Invisibility does it, so there is no second system to
    /// keep in sync. Note the same caveat Invisibility carries — opacity is a strong visual
    /// hint, not a true targeting cloak.
    /// </remarks>
    [CreateAssetMenu(fileName = "SmokeRoost",
        menuName = "Cluck Wars/Ability/Control/Smoke Roost", order = 7)]
    public sealed class SmokeRoostAbilitySO : AbilityBaseSO
    {
        public SmokeRoostAbilitySO()
        {
            Category = AbilityCategory.Control;
            SlotKind = AbilitySlotKind.Character;
            AllowedClasses = ChickenClassFlags.Assassin;
            BotRole = BotRole.Escape;
            Duration = 3.5f;
            Cooldown = 11f;
        }

        [Tooltip("Radius of the cloud.")]
        [Min(0.5f)] public float ZoneRadius = 4.0f;

        [Tooltip("How long the cloud keeps slowing people after it is dropped. Deliberately "
               + "longer than the caster's own fade, so the trap outlives the ambush.")]
        [Min(0.5f)] public float ZoneDuration = 5.5f;

        [Tooltip("Speed multiplier for anyone inside the cloud.")]
        [Range(0.1f, 0.9f)] public float SlowFactor = 0.55f;

        [Tooltip("Caster's alpha while concealed. Matches Invisibility's 0.2 - a ghostly "
               + "outline, not a true cloak.")]
        [Range(0f, 1f)] public float Opacity = 0.2f;

        protected override string DefaultIcon => "🌁";

        // The cast lands a cloud, not a hit. Chickens are slowed later, by the zone, so
        // cast-time hit/whiff styling must skip it entirely — dropping smoke on empty ground
        // is the normal, correct play. See AbilityBaseSO.ReportsCastHits.
        public override bool PlacesZone => true;

        public override AbilityAimShape AimShape => AbilityAimShape.SelfCircle;
        public override float AimRadius => ZoneRadius;
        public override bool AffectsSelf => true;

        public override void OnActivate(AbilityContext ctx)
        {
            var caster = ctx.Controller;

            // Half one: he fades. Cheap, and independent of the zone spawning at all.
            caster.VisualOpacity = Opacity;

            if (ctx.Runner == null || ctx.PrefabRegistry == null || ctx.PrefabRegistry.AbilityZone == null)
                return;

            // Half two: the cloud. Captured into locals — a closure over 'this' can go stale
            // if the SO is unloaded (same reason Feather Trap does it).
            float capDuration   = ZoneDuration;
            float capSlowFactor = SlowFactor;
            float capRadius     = ZoneRadius;
            var   capOwner      = caster.Id;

            var pos = caster.transform.position;

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
                    zone.OwnerChicken    = capOwner;
                    zone.SlowFactor      = capSlowFactor;
                    zone.NetworkedRadius = capRadius;
                    zone.LifetimeTimer   = TickTimer.CreateFromSeconds(runner, capDuration);
                });
        }

        public override void OnDeactivate(AbilityContext ctx)
        {
            ctx.Controller.VisualOpacity = 1f;
        }
    }
}

using CluckWars.Gameplay;
using UnityEngine;
using CluckWars.Localization;

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
        public override string DescriptionKey => UiKeys.AbilityFeatherTrapDesc;
        public override (string name, object value)[] DescriptionArgs() =>
            new (string, object)[] { ("secs", Num(ZoneDuration)), ("pct", Pct(SlowFactor)) };

        private const string Source = "FeatherTrap";

        [Tooltip("Distance in front of the caster where the zone center is placed.")]
        [Min(0f)] public float ForwardOffset = 4.5f;

        [Tooltip("Trigger radius of the slow zone.")]
        [Min(0.5f)] public float ZoneRadius = 3.6f;

        [Tooltip("How long the feather cloud persists (seconds) before auto-despawning.")]
        [Min(0.5f)] public float ZoneDuration = 5.0f;

        [Tooltip("Speed multiplier applied to chickens inside the zone (< 1 = slower).")]
        [Range(0.1f, 0.9f)] public float SlowFactor = 0.45f;

        // Places a zone rather than hitting targets directly, so OnActivate has no
        // GatherTargets loop — this descriptor exists purely so the hold-to-aim
        // preview (Stage 3) can draw the zone's real footprint before it's thrown.
        public override AbilityAimShape AimShape => AbilityAimShape.ForwardCircle;
        public override float AimRadius => ZoneRadius;
        public override float AimForwardOffset => ForwardOffset;

        // The cast lands a cloud, not a hit — chickens are slowed later, by the zone. A trap
        // thrown at empty ground (the normal, correct play) reports 0 targets, so cast-time
        // hit/whiff styling must skip it entirely: PlacesZone (derived from AreaKind = StaticAoE,
        // authored on the asset and defaulted here for fresh instances) does that. See
        // AbilityBaseSO.ReportsCastHits.
        public FeatherTrapAbilitySO() { AreaKind = AreaEffectKind.StaticAoE; }

        public override bool TryGetAreaEffect(out AreaEffectSpec spec)
        {
            spec = AreaEffectSpec.StaticSlow(ZoneRadius, ZoneDuration, SlowFactor, ForwardOffset);
            return true;
        }

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

            TryGetAreaEffect(out var spec);
            AreaEffectSpawner.Spawn(ctx, spec, pos, Source, "Feather Trap");
        }

        public override void OnDeactivate(AbilityContext ctx) { }
    }
}

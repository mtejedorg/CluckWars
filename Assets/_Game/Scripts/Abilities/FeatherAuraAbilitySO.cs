using CluckWars.Gameplay;
using UnityEngine;
using CluckWars.Localization;

namespace CluckWars.Abilities
{
    /// <summary>
    /// Feather Aura — Control ability. The caster emits a feather cloud for
    /// <see cref="AbilityBaseSO.Duration"/> seconds. Any chicken within
    /// <see cref="AuraRadius"/> metres is slowed by <see cref="AuraSlowFactor"/>
    /// while the aura is active.
    /// </summary>
    /// <remarks>
    /// <b>An Aura (WP4, B4):</b> the cast spawns one Caster-anchored <see cref="AbilityZone"/> through
    /// <see cref="AreaEffectSpawner"/>. Its centre is the caster as each peer sees them (no NetworkTransform, nothing
    /// parented under the chicken, F2); every victim's own authority applies the slow on the WhileInside channel in
    /// <c>ChickenController.CheckAbilityZoneSlow</c>, zero RPC, exactly like a placed Feather Trap. The zone ends with
    /// the casting slot (<see cref="AreaEffectSpec.EndsWithAbility"/>, <c>SourceSlot = ctx.Slot</c>), with
    /// <see cref="AbilityBaseSO.Duration"/> as its lifetime backstop, and when its caster is gone (B5). So
    /// <see cref="OnDeactivate"/> has nothing to undo.
    /// <para>
    /// <b>Feedback:</b> an Aura reports no cast-time hits (<see cref="AbilityBaseSO.ReportsCastHits"/> is false for every
    /// <see cref="AbilityBaseSO.AreaKind"/> other than None); the OnEnter channel confirms each rival once per aura life,
    /// including the rivals already inside when it was cast (Maestro's rule), from <c>AbilityZone.Render</c>.
    /// </para>
    /// <para>
    /// <b>No overlapping auras:</b> <c>Cooldown</c> (7 s) is longer than <c>Duration</c> (3 s) and the cooldown starts
    /// with the cast, so a second aura from the same caster cannot start while the first lives
    /// (<c>FeatherAuraZoneTests.Cooldown_OutlastsTheAura</c>). Bots need no "aura already up" guard for the same reason.
    /// </para>
    /// Cooldown tier: Medium.
    /// </remarks>
    [CreateAssetMenu(fileName = "FeatherAura",
        menuName = "Cluck Wars/Ability/Control/Feather Aura", order = 10)]
    public sealed class FeatherAuraAbilitySO : AbilityBaseSO
    {
        public override string DescriptionKey => UiKeys.AbilityFeatherAuraDesc;
        public override (string name, object value)[] DescriptionArgs() =>
            new (string, object)[] { ("r", Num(AuraRadius)), ("pct", Pct(AuraSlowFactor)), ("secs", Num(Duration)) };

        private const string Source = "FeatherAura";

        [Tooltip("Radius of the slow aura around the caster.")]
        [Min(0.5f)] public float AuraRadius = 5.4f;

        [Tooltip("Speed multiplier applied to chickens inside the aura (< 1 = slower).")]
        [Range(0.1f, 0.9f)] public float AuraSlowFactor = 0.55f;

        public override AbilityAimShape AimShape => AbilityAimShape.Aura;
        public override float AimRadius => AuraRadius;

        // Tagged as an Aura (authored on the asset, defaulted here for fresh instances).
        public FeatherAuraAbilitySO() { AreaKind = AreaEffectKind.Aura; }

        public override bool TryGetAreaEffect(out AreaEffectSpec spec)
        {
            spec = AreaEffectSpec.AuraSlow(AuraRadius, Duration, AuraSlowFactor);
            return true;
        }

        // Unassigned wiring, not a gameplay state: CanSpawnZone logs which reference is missing, before the cast and its
        // cooldown are committed (CONVENTIONS, the pre-commitment gate).
        public override bool CanActivate(AbilityContext ctx) => ctx.CanSpawnZone(Source, "Feather Aura");

        public override void OnActivate(AbilityContext ctx)
        {
            var caster = ctx.Controller;
            TryGetAreaEffect(out var spec);
            // An Aura's position only seeds its transform; its centre is the caster (AbilityZone.TryGetCenter).
            AreaEffectSpawner.Spawn(ctx, spec, AreaEffectSpawner.Placement(caster.transform.position, caster.transform.forward, spec),
                Source, "Feather Aura");
        }

        // Nothing to undo: the zone despawns itself on its first authority tick after this slot stops being active.
        public override void OnDeactivate(AbilityContext ctx) { }
    }
}

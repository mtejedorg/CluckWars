using CluckWars.Gameplay;
using UnityEngine;
using CluckWars.Localization;

namespace CluckWars.Abilities
{
    /// <summary>
    /// <b>Slow Area</b> (Maestro 2026-11-13; the former Slow, renamed in display only). A lasting slow area that outlasts the
    /// instant <see cref="SlowAbilitySO"/>. Its shape is the asset's <see cref="AbilityBaseSO.AreaKind"/>:
    /// <list type="bullet">
    ///   <item><b>Aura</b> (the shipped default): follows the caster, and ends with the casting slot. The Feather Aura mechanic.</item>
    ///   <item><b>Static AoE</b> (placed zone): stays on the ground where it was cast. Under RangeOnly it is centred on the
    ///   caster's position with no forward offset.</item>
    /// </list>
    /// </summary>
    /// <remarks>
    /// <b>An Aura (WP4, B4):</b> the cast spawns one Caster-anchored <see cref="AbilityZone"/> through
    /// <see cref="AreaEffectSpawner"/>. Its centre is the caster as each peer sees them (no NetworkTransform, nothing
    /// parented under the chicken, F2); every victim's own authority applies the slow on the WhileInside channel in
    /// <c>ChickenController.CheckAbilityZoneSlow</c>, zero RPC. The zone ends with the casting slot
    /// (<see cref="AreaEffectSpec.EndsWithAbility"/>), with <see cref="AbilityBaseSO.Duration"/> as its lifetime backstop,
    /// and when its caster is gone. So <see cref="OnDeactivate"/> has nothing to undo.
    /// <para>
    /// <b>A placed zone</b> is the Feather Trap's shape (a StaticAoE with a WhileInside slow, no RPC), with this ability's
    /// numbers. Its lifetime is the same run time the aura gets (<see cref="ResolveActiveDuration"/>), so Relentless
    /// lengthens it too.
    /// </para>
    /// <para>
    /// <b>Feedback:</b> an aura reports no cast-time hits (<see cref="AbilityBaseSO.ReportsCastHits"/> is false for every
    /// <see cref="AbilityBaseSO.AreaKind"/> other than None); the OnEnter channel confirms each rival once per aura life,
    /// including the rivals already inside when it was cast, from <c>AbilityZone.Render</c>. The aura ring
    /// (<c>SlowAuraFeedback</c>) draws only for an aura, never for a placed zone.
    /// </para>
    /// <para>
    /// <b>No overlapping auras:</b> <see cref="CanActivate"/> refuses while a live aura of this caster still ends with this
    /// slot. Pinned by <c>FeatherAuraZoneTests.ASecondAura_IsRefusedWhileTheFirstLives_EvenAtAQuarterCooldown</c>. Bots get
    /// the same refusal through <c>BotTryActivate</c>.
    /// </para>
    /// <para>
    /// <b>Relentless (2026-11-05):</b> the run time IS the slow it inflicts (<see cref="ActiveDurationIsOutgoingControl"/>),
    /// so a Relentless caster's outgoing-control factor lengthens the slot's active time and the zone's lifetime together.
    /// <see cref="TryGetAreaEffect"/> stays the authored spec (no caster); only the cast-time spec is scaled.
    /// </para>
    /// Cooldown tier: Medium.
    /// </remarks>
    [CreateAssetMenu(fileName = "FeatherAura",
        menuName = "Cluck Wars/Ability/Control/Feather Aura", order = 10)]
    public sealed class FeatherAuraAbilitySO : AbilityBaseSO
    {
        public override AbilityJob Job => AbilityJob.SlowArea;

        /// <summary>True for a placed zone (AreaKind StaticAoE); false for the caster-following aura.</summary>
        public bool IsPlaced => AreaKind == AreaEffectKind.StaticAoE;

        public override string DescriptionKey => IsPlaced ? UiKeys.AbilityFeatherAuraZoneDesc : UiKeys.AbilityFeatherAuraDesc;
        public override (string name, object value)[] DescriptionArgs() =>
            new (string, object)[] { ("r", Num(AuraRadius)), ("pct", Pct(AuraSlowFactor)), ("secs", Num(Duration)) };

        private const string Source = "FeatherAura";

        [Tooltip("Radius of the slow area (planar metres). 6.48 = 5.4 x1.2 (Maestro 2026-11-05).")]
        [Min(0.5f)] public float AuraRadius = 6.48f;

        [Tooltip("Speed multiplier applied to chickens inside the area (< 1 = slower).")]
        [Range(0.1f, 0.9f)] public float AuraSlowFactor = 0.55f;

        // An aura is authored as an Aura shape; a placed zone is a plain circle on the caster (no forward offset).
        public override AbilityAimShape AuthoredAimShape => IsPlaced ? AbilityAimShape.SelfCircle : AbilityAimShape.Aura;
        public override float AimRadius => AuraRadius;
        public override string AimRadiusField => nameof(AuraRadius);

        public FeatherAuraAbilitySO()
        {
            DisplayName = "Slow Area";
            ShortLabel = "AREA";
            Description = "For 5s, rivals within 6.5m of you move at 55% speed.";
            Category = AbilityCategory.Control;
            AllowedClasses = ChickenClassFlags.All;
            AreaKind = AreaEffectKind.Aura;
            Duration = 5f;
            Cooldown = 7f;
        }

        public override bool ActiveDurationIsOutgoingControl => true;

        public override bool TryGetAreaEffect(out AreaEffectSpec spec)
        {
            spec = Spec(Duration);
            return true;
        }

        /// <summary>The spec for a given run time: a placed zone, or the caster-following aura.</summary>
        private AreaEffectSpec Spec(float lifetime) => IsPlaced
            ? AreaEffectSpec.StaticSlow(AuraRadius, lifetime, AuraSlowFactor, 0f)
            : AreaEffectSpec.AuraSlow(AuraRadius, lifetime, AuraSlowFactor);

        /// <summary>
        /// The pre-commitment gate (CONVENTIONS): everything that would make <see cref="OnActivate"/>'s spawn fail is
        /// checked here, before the slot's cooldown is charged. A missing shape or unassigned wiring is a data or wiring
        /// bug and logs an Error; a previous aura from this slot still live is a legal one-tick state and only refuses.
        /// </summary>
        public override bool CanActivate(AbilityContext ctx)
        {
            if (!base.CanActivate(ctx)) return false;   // the zone prefab chain
            if (AreaKind == AreaEffectKind.None)
            {
                ctx.Log?.Error(Source, "Slow Area has no shape: set AreaKind to Aura or StaticAoE on the asset. Refused before the cast (no cooldown charged).");
                return false;
            }
            TryGetAreaEffect(out var spec);
            string refusal = AreaEffectSpawner.Refusal(spec, ctx.Slot);
            if (refusal != null)
            {
                ctx.Log?.Error(Source, $"Slow Area refused before the cast (no cooldown charged): {refusal}. Fix TryGetAreaEffect.");
                return false;
            }
            if (IsPlaced) return true;
            return ctx.Controller == null || !AbilityZone.HasLiveAuraFrom(ctx.Controller.Id, ctx.Slot);
        }

        /// <remarks>
        /// <c>AreaEffectSpawner.Spawn</c>'s false is not handled here on purpose: its only cause is a spec refusal, which
        /// <see cref="CanActivate"/> has already ruled out with the same <c>Refusal</c> call before the cooldown was
        /// charged. (A prefab without an <c>AbilityZone</c> component is the spawner's own Error, after the spawn committed.)
        /// </remarks>
        public override void OnActivate(AbilityContext ctx)
        {
            // The same run time AbilityController gave the slot (ResolveActiveDuration), so the zone and the slot end together.
            SpawnArea(ctx, Spec(ResolveActiveDuration(ctx.Controller)));
        }

        // OnDeactivate: the base's (nothing to undo). An aura despawns itself on its first authority tick after this slot stops being active.
    }
}

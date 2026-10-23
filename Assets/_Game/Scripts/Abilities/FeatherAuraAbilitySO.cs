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
    /// <b>No overlapping auras, explicitly:</b> <see cref="CanActivate"/> refuses while a live aura of this caster still
    /// ends with this slot (<see cref="AreaEffectRules.BlocksRecast"/>). The shipped numbers would already prevent it
    /// (Cooldown 7 s &gt; Duration 3 s, and a running slot refuses a re-cast), but a cooldown passive can shorten the
    /// cooldown below the aura (Relentless goes to x0.25), and the old zone only sees its slot end on its next authority
    /// tick. Pinned by <c>FeatherAuraZoneTests.ASecondAura_IsRefusedWhileTheFirstLives_EvenAtAQuarterCooldown</c>. Bots
    /// get the same refusal through <c>BotTryActivate</c>, so they need no extra "aura already up" guard.
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

        /// <summary>
        /// The pre-commitment gate (CONVENTIONS): everything that would make <see cref="OnActivate"/>'s spawn fail is
        /// checked here, before the slot's cooldown is charged. Unassigned wiring (<c>CanSpawnZone</c>) and a spec the
        /// spawner refuses are code/wiring bugs and log an Error; a previous aura from this slot still live is a legal
        /// one-tick state and only refuses (see the remarks).
        /// </summary>
        public override bool CanActivate(AbilityContext ctx)
        {
            if (!ctx.CanSpawnZone(Source, "Feather Aura")) return false;
            TryGetAreaEffect(out var spec);
            string refusal = AreaEffectSpawner.Refusal(spec, ctx.Slot);
            if (refusal != null)
            {
                ctx.Log?.Error(Source, $"Feather Aura refused before the cast (no cooldown charged): {refusal}. Fix TryGetAreaEffect.");
                return false;
            }
            return ctx.Controller == null || !AbilityZone.HasLiveAuraFrom(ctx.Controller.Id, ctx.Slot);
        }

        /// <remarks>
        /// <c>AreaEffectSpawner.Spawn</c>'s false is not handled here on purpose: its only cause is a spec refusal, which
        /// <see cref="CanActivate"/> has already ruled out with the same <c>Refusal</c> call before the cooldown was
        /// charged, so a false here cannot happen with this spec. (A prefab without an <c>AbilityZone</c> component is
        /// the spawner's own Error inside the pre-spawn callback, after the spawn committed; nothing to refund then.)
        /// </remarks>
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

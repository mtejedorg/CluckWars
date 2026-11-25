using CluckWars.Gameplay;
using UnityEngine;
using CluckWars.Localization;

namespace CluckWars.Abilities
{
    /// <summary>
    /// <b>Slow</b> (Maestro 2026-11-13): the instant slow. One cast slows every rival within <see cref="Radius"/> (planar
    /// distance, RangeOnly: no cone, no forward offset) for <see cref="SlowSeconds"/>. The slow is a TIMED slow: a rival
    /// stays slowed after leaving the circle.
    /// </summary>
    /// <remarks>
    /// The same path Dust Kick used: <see cref="EmittedControl"/> is a <see cref="ControlKind.Slow"/> sent through
    /// <see cref="AbilityBaseSO.SendControl"/>, so Slippery, Relentless, Bulwark and the Shield's walk and egg apply
    /// unchanged, and the victim's base-wall check (<c>ChickenController.JudgeControl</c>) and this ability's own
    /// <see cref="AbilityBaseSO.WouldAffect"/> both refuse a walled rival. The slowed state and its Snared badge come from
    /// the victim's <c>SlowSource.Ability</c>, so the feedback a Slow already gets is unchanged.
    /// <para>
    /// Targeting: a plain circle around the caster (<see cref="AbilityAimShape.SelfCircle"/>, which RangeOnly keeps).
    /// It does not place a zone, so it has no lasting area and <see cref="AreaEffectKind.None"/>.
    /// </para>
    /// Cooldown tier: Medium. Retired abilities are not affected by this class.
    /// </remarks>
    [CreateAssetMenu(fileName = "Slow",
        menuName = "Cluck Wars/Ability/Control/Slow", order = 11)]
    public sealed class SlowAbilitySO : AbilityBaseSO
    {
        public override AbilityJob Job => AbilityJob.Slow;

        public override string DescriptionKey => UiKeys.AbilitySlowDesc;
        public override (string name, object value)[] DescriptionArgs() =>
            new (string, object)[] { ("r", Num(Radius)), ("pct", Pct(SlowFactor)), ("secs", Num(SlowSeconds)) };

        [Tooltip("Reach of the slow around the caster (planar metres).")]
        [Min(0.5f)] public float Radius = 6.48f;

        [Tooltip("Speed multiplier a slowed rival moves at (< 1 = slower).")]
        [Range(0.1f, 0.9f)] public float SlowFactor = 0.55f;

        [Tooltip("How long a rival stays slowed after the cast. Runs on even after the rival leaves the circle. " +
                 "Slippery, Relentless and Bulwark change it through the normal timed-slow path.")]
        [Min(0.1f)] public float SlowSeconds = 3f;

        public SlowAbilitySO()
        {
            DisplayName = "Slow";
            ShortLabel = "SLOW";
            Description = "Slow every rival within 6.5m to 55% speed for 3s.";
            Category = AbilityCategory.Control;
            BotRole = BotRole.Control;
            AllowedClasses = ChickenClassFlags.All;
            Duration = 0.3f;   // the cast itself; the slow runs on SlowSeconds on each victim
            Cooldown = 6f;
        }

        public override float IndicatorRange => Radius;

        /// <summary>A burst: without a rival in reach it is a whiff, like the stun bursts.</summary>
        public override bool RequiresEnemyInRange => true;

        public override AbilityAimShape AuthoredAimShape => AbilityAimShape.SelfCircle;
        public override float AimRadius => Radius;
        public override string AimRadiusField => nameof(Radius);

        // The slow is the whole of the target effect (see AbilityBaseSO.TargetEffectIsPurelyControl).
        public override bool TargetEffectIsPurelyControl => true;

        public override ControlEmission EmittedControl => ControlEmission.Slow(SlowSeconds, SlowFactor);

        public override void OnActivate(AbilityContext ctx)
        {
            var caster = ctx.Controller;
            GatherTargets(caster, _scratch);
            for (int i = 0; i < _scratch.Count; i++)
            {
                bool lands = caster.PredictSlowLands(_scratch[i], EmittedControl);   // before the send: a victim this peer owns applies it inside SendControl
                SendControl(caster, _scratch[i]);
                ReportHit(caster, _scratch[i], AbilityJob.Slow, lands);   // priced only where a passive sets HitStealSlow (shipped 0)
            }
        }
    }
}

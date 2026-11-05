using CluckWars.Gameplay;
using UnityEngine;
using CluckWars.Localization;

namespace CluckWars.Abilities
{
    /// <summary>
    /// <b>Steal</b>, the one steal job (Maestro 2026-11-05, "one ability per job"): robs every cargo-carrier in reach.
    /// Under <see cref="TargetingMode.RangeOnly"/> that is every carrier within <see cref="SnatchRange"/> of the caster;
    /// the authored 140° arc only returns under Shaped. The shove is data: <see cref="KnockbackStrength"/> 0 (shipped)
    /// means none, and then the effect is a pure steal (<see cref="TargetEffectIncludesControl"/>). The Thief's bigger
    /// steal comes from its passive through <c>ResolveStealAmount</c>, not from this asset.
    /// Formerly "Peck" (renamed 2026-08-13), then "Snatch"; the asset GUID is unchanged, so every reference survived.
    /// No per-victim lock (that lived in the retired Sneaky Steal).
    /// </summary>
    [CreateAssetMenu(fileName = "Snatch",
        menuName = "Cluck Wars/Ability/Steal/Snatch", order = 7)]
    public sealed class SnatchAbilitySO : AbilityBaseSO
    {
        public override string DescriptionKey => UiKeys.AbilitySnatchDesc;
        public override (string name, object value)[] DescriptionArgs() =>
            new (string, object)[] { ("n", Num(StealAmount)), ("r", Num(SnatchRange)) };

        public SnatchAbilitySO()
        {
            DisplayName = "Steal";
            ShortLabel = "ROB";
            Description = "Robs 5 food from every rival within 5.16m.";
            Category = AbilityCategory.Steal;
            AllowedClasses = ChickenClassFlags.All;
            Duration = 0.3f;
            Cooldown = 5f;
        }

        [Tooltip("Reach: every carrier within this planar distance of the caster (RangeOnly), or along the 140° arc (Shaped). " +
                 "5.16 = 4.3 x1.2 (Maestro 2026-11-05).")]
        [Min(0.5f)] public float SnatchRange = 5.16f;

        [Tooltip("Amount of cargo to steal per hit target.")]
        [Min(1f)] public float StealAmount = 5f;

        [Tooltip("Knockback impulse (world-units/sec) on each robbed rival. 0 = no shove (shipped 2026-11-05: shoving is the Shove job).")]
        [Min(0f)] public float KnockbackStrength = 0f;

        public override float IndicatorRange => SnatchRange;
        public override bool RequiresEnemyInRange => true;

        /// <summary>
        /// The authored 140° forward arc (Shaped only; RangeOnly reads a circle of <see cref="SnatchRange"/>). Kept as the
        /// Shaped answer from 2026-08: a full circle meant "press it in a pile scrum and take from whoever is behind you".
        /// </summary>
        public override AbilityAimShape AuthoredAimShape => AbilityAimShape.Cone;
        public override float AimRadius => SnatchRange;
        public override float AuthoredAimConeAngle => 140f;

        public override float NominalStealAmount => StealAmount;

        public override bool TargetEffectIncludesSteal => true;
        public override bool TargetEffectIncludesControl => KnockbackStrength > 0f; // the shove, when there is one

        /// <summary>Only a cargo-carrier is a valid Snatch target — an empty-handed rival in range now gets the "immune / no-effect" telegraph instead of a knockback that used to land anyway.</summary>
        protected override bool ExtraTargetFilter(ChickenController caster, ChickenController candidate)
        {
            var cargo = candidate.Cargo;
            return cargo != null && cargo.Stealable > 0f;
        }

        public override void OnActivate(AbilityContext ctx)
        {
            var thief = ctx.Controller;
            var thiefCargo = thief.Cargo;
            if (thiefCargo == null) return;

            GatherTargets(thief, _scratch);
            for (int i = 0; i < _scratch.Count; i++)
            {
                var targetCtrl = _scratch[i];
                var targetCargo = targetCtrl.Cargo; // ExtraTargetFilter already guaranteed Stealable > 0

                // Steal-proof victim (Egg Shell / Turtle Mode): skip the steal BEFORE crediting
                // ourselves — the knockback below is unaffected (ApplyKnockback has its own
                // control-immunity check).
                float freeSpace = thiefCargo.Capacity - thiefCargo.Cargo;
                float stolen = StealRules.CanStealFrom(targetCtrl)
                    ? StealMath.Clamp(ResolveStealAmount(StealAmount, thief), freeSpace, targetCargo.Stealable)
                    : 0f;
                if (stolen > 0f)
                {
                    thiefCargo.CreditStealAndDrain(targetCargo, stolen);
                }

                if (KnockbackStrength > 0f)
                {
                    var dir = (targetCtrl.transform.position - thief.transform.position);
                    dir.y = 0f;
                    if (dir.sqrMagnitude < 0.001f) dir = thief.transform.forward;
                    targetCtrl.RPC_ApplyKnockback(dir.normalized * KnockbackStrength);
                }
            }
        }

        public override void OnDeactivate(AbilityContext ctx) { }
    }
}

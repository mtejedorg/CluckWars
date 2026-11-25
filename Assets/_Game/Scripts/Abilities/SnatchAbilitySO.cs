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
        public override AbilityJob Job => AbilityJob.Steal;

        public override string DescriptionKey => UiKeys.AbilitySnatchDesc;
        public override (string name, object value)[] DescriptionArgs() =>
            new (string, object)[] { ("n", Num(StealAmount)), ("r", Num(SnatchRange)) };

        public SnatchAbilitySO()
        {
            DisplayName = "Steal";
            ShortLabel = "ROB";
            Description = "Take 5 food from every rival within 5.2m.";
            Category = AbilityCategory.Steal;
            AllowedClasses = ChickenClassFlags.All;
            Duration = 0.3f;
            Cooldown = 4f;
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
        public override string AimRadiusField => nameof(SnatchRange);
        public override float AuthoredAimConeAngle => 140f;

        public override float NominalStealAmount => StealAmount;

        public override AbilityOracleFacts OracleFacts => new AbilityOracleFacts { StealAmount = StealAmount };

        public override float EmittedKnockback => KnockbackStrength;

        public override bool TargetEffectIncludesSteal => true;
        public override bool TargetEffectIncludesControl => KnockbackStrength > 0f; // the shove, when there is one

        /// <summary>
        /// Only a cargo-carrier is a valid Snatch target — an empty-handed rival in range now gets the "immune / no-effect" telegraph instead of a knockback that
        /// used to land anyway. The one exception is the rival carrying the caster's own loot mark (a Thief's, BL-217): the bounty is paid even with nothing to take.
        /// Every other kit has no mark, so for it nothing changes.
        /// </summary>
        protected override bool ExtraTargetFilter(ChickenController caster, ChickenController candidate)
        {
            var cargo = candidate.Cargo;
            if (cargo != null && cargo.Stealable > 0f) return true;
            var loot = caster != null ? caster.Loot : null;
            return loot != null && loot.IsMarking(candidate);
        }

        public override void OnActivate(AbilityContext ctx)
        {
            var thief = ctx.Controller;
            var thiefCargo = thief.Cargo;
            if (thiefCargo == null) return;

            GatherTargets(thief, _scratch);

            // The Thief's marked rival first (BL-217): his take and the loot bounty are one marked steal, authority-confirmed (ThiefLoot.TakeMarked), and they
            // get first call on the beak's room. Handled here so the loop below sees the same list; a kit with no mark skips this and plays as before.
            var loot = thief.Loot;
            int markedIndex = -1;
            if (loot != null && loot.MarkLive)
            {
                for (int i = 0; i < _scratch.Count; i++)
                    if (loot.IsMarking(_scratch[i])) { markedIndex = i; break; }
                if (markedIndex >= 0 && loot.TakeMarked(_scratch[markedIndex], ResolveStealAmount(StealAmount, thief)))
                    thief.KnockAway(_scratch[markedIndex], KnockbackStrength);
                else
                    markedIndex = -1;   // a shell or an open marked steal refused it: it is robbed like any rival, or not at all
            }

            for (int i = 0; i < _scratch.Count; i++)
            {
                if (i == markedIndex) continue;
                // A steal-proof victim (Egg Shell / Turtle Mode) gives nothing (StealFrom's gate); the shove below
                // is unaffected (ApplyKnockback has its own control-immunity check) and names the thief, so a base
                // wall refuses it exactly where it refuses the steal. Nothing is sent for a strength of 0 (shipped).
                // The Enforcer's hit (BL-226): a Steal that reaches a carrier stacks the fresh debt on a fresh rival before it asks, so the stack counts toward this
                // very Steal; the ask is then inflated by the debts on him. For every other kit both calls are plain (no facts, no debts).
                ReportHit(thief, _scratch[i], AbilityJob.Steal, StealRules.CanStealFrom(_scratch[i]));
                StealFrom(thief, _scratch[i], StealAmount, thief.Debts);
                thief.KnockAway(_scratch[i], KnockbackStrength);
            }
        }
    }
}

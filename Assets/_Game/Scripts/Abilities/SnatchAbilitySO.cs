using CluckWars.Gameplay;
using UnityEngine;

namespace CluckWars.Abilities
{
    /// <summary>
    /// Common steal: a forward arc that robs every cargo-carrier it touches and shoves
    /// them back. Formerly named "Peck" — renamed 2026-08-13 to free that name for the
    /// foraging ability, which is what a chicken pecking at a pile of grain actually is.
    /// The asset GUID is unchanged, so every registry and loadout reference survived.
    /// </summary>
    [CreateAssetMenu(fileName = "Snatch",
        menuName = "Cluck Wars/Ability/Steal/Snatch", order = 7)]
    public sealed class SnatchAbilitySO : AbilityBaseSO
    {
        public SnatchAbilitySO()
        {
            Category = AbilityCategory.Steal;
            SlotKind = AbilitySlotKind.Common;
            AllowedClasses = ChickenClassFlags.All;
        }

        [Tooltip("Maximum distance to the target, along the 140° forward arc.")]
        [Min(0.5f)] public float SnatchRange = 2.4f;

        [Tooltip("Amount of cargo to steal per hit target.")]
        [Min(1f)] public float StealAmount = 5f;

        [Tooltip("Knockback impulse strength (world-units/sec) applied to the hit target.")]
        [Min(0f)] public float KnockbackStrength = 6f;

        protected override string DefaultIcon => "🐦";

        public override float IndicatorRange => SnatchRange;
        public override bool RequiresEnemyInRange => true;

        /// <summary>
        /// Forward arc, not a ring. Snatch is a Common ability every class can equip and it
        /// robs <i>every</i> carrier it touches, so a full circle meant "press it in a
        /// pile scrum and take from whoever happens to be behind you". 140° still covers
        /// everything you are roughly facing — TurnSpeed re-aims in about 0.2 s — so this
        /// removes the free omnidirectional rob without turning a Common ability into a
        /// precision test. Range goes up (2.0 → 2.4 m) so the 1v1 case is unchanged to
        /// better; only the blind-side rob is gone.
        /// </summary>
        public override AbilityAimShape AimShape => AbilityAimShape.Cone;
        public override float AimRadius => SnatchRange;
        public override float AimConeAngle => 140f;

        /// <summary>Only a cargo-carrier is a valid Snatch target — an empty-handed rival in range now gets the "immune / no-effect" telegraph instead of a knockback that used to land anyway.</summary>
        protected override bool ExtraTargetFilter(ChickenController caster, ChickenController candidate)
        {
            var cargo = candidate.Cargo;
            return cargo != null && cargo.Cargo > 0f;
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
                var targetCargo = targetCtrl.Cargo; // ExtraTargetFilter already guaranteed Cargo > 0

                float freeSpace = thiefCargo.Capacity - thiefCargo.Cargo;
                float stolen = StealMath.Clamp(StealAmount, freeSpace, targetCargo.Cargo);
                if (stolen > 0f)
                {
                    thiefCargo.Cargo += stolen;
                    targetCargo.RPC_DrainStolen(stolen);
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

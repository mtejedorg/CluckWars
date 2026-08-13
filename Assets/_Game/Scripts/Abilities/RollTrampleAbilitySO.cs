using CluckWars.Gameplay;
using UnityEngine;

namespace CluckWars.Abilities
{
    [CreateAssetMenu(fileName = "DiveBomb", menuName = "Cluck Wars/Ability/Steal/Dive Bomb", order = 5)]
    public sealed class RollTrampleAbilitySO : AbilityBaseSO
    {
        public RollTrampleAbilitySO()
        {
            Category = AbilityCategory.Steal;
            TerrainTraversal = TerrainTraversal.Vault;
            SlotKind = AbilitySlotKind.Character;
            AllowedClasses = ChickenClassFlags.Warrior;
        }

        [Tooltip("Length of the peck lane, measured forward from the caster's PRE-jump " +
                 "position. With a Capsule aim shape this is the axis length, not a detached " +
                 "centre — total forward reach is ForwardOffset + SweepRadius.")]
        [Min(0.5f)] public float ForwardOffset = 5.0f;

        [Tooltip("Half-width of the peck lane. Generous — catches chickens slightly off-line.")]
        [Min(0.5f)] public float SweepRadius = 1.15f;

        [Tooltip("Amount of cargo to steal on contact.")]
        [Min(1f)] public float StealAmount = 8f;

        protected override string DefaultIcon => "🪽";

        public override float IndicatorRange => ForwardOffset + SweepRadius;
        public override bool RequiresEnemyInRange => true;

        /// <summary>
        /// The lane the peck sweeps through on its way past. Being a Capsule also decides
        /// <i>when</i> the shape resolves: <c>AbilityController.TryActivate</c> runs the
        /// target scan BEFORE this ability's teleport jump, not after (see
        /// <see cref="AbilityBaseSO.ResolvesBeforeJump"/>). Without that, a JumpTier of Short
        /// put the hit lane 5 m past the press point — the ability flew over everything it
        /// was aimed at and hit whatever happened to be at the far end.
        /// </summary>
        public override AbilityAimShape AimShape => AbilityAimShape.Capsule;
        public override float AimRadius => SweepRadius;
        public override float AimForwardOffset => ForwardOffset;

        /// <summary>Only a cargo-carrier is a valid Trample target.</summary>
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

            // Steals on contact with the nearest rival in the sweep — GatherTargets
            // sorts ascending, so index 0 is that rival.
            GatherTargets(thief, _scratch);
            if (_scratch.Count == 0) return;

            var targetCargo = _scratch[0].Cargo;
            if (targetCargo == null) return;

            float freeSpace = thiefCargo.Capacity - thiefCargo.Cargo;
            float stolen = StealMath.Clamp(ResolveStealAmount(StealAmount, thief), freeSpace, targetCargo.Cargo);
            if (stolen > 0f)
            {
                thiefCargo.Cargo += stolen;
                targetCargo.RPC_DrainStolen(stolen);
            }
        }

        public override void OnDeactivate(AbilityContext ctx) { }
    }
}

using CluckWars.Gameplay;
using UnityEngine;

namespace CluckWars.Abilities
{
    /// <summary>
    /// <b>Warrior, Bully signature.</b> Grabs a beakful of cargo off whoever is closest.
    /// </summary>
    /// <remarks>
    /// <b>Deliberately a worse Sneaky Steal.</b> Half the amount and shorter reach than the
    /// Assassin's version, because "expert in nothing" has to cost something measurable — the
    /// Warrior is allowed to steal, just never as well as the class whose whole income is theft.
    ///
    /// It is the <b>Bully</b> signature because Bully multiplies steal amount AND raises cargo
    /// capacity to hold it. Both halves matter: <see cref="ResolveStealAmount"/> is clamped to
    /// the thief's free space, so raising the take without the room to carry it would quietly
    /// do nothing for a Warrior already holding a load. Under Relentless (the frequency passive)
    /// this stays a small nibble — the specializations are meant to pull in different directions.
    /// </remarks>
    [CreateAssetMenu(fileName = "Scrap",
        menuName = "Cluck Wars/Ability/Steal/Scrap", order = 7)]
    public sealed class ScrapAbilitySO : AbilityBaseSO
    {
        public ScrapAbilitySO()
        {
            Category = AbilityCategory.Steal;
            SlotKind = AbilitySlotKind.Character;
            AllowedClasses = ChickenClassFlags.Warrior;
            BotRole = BotRole.Steal;
            Duration = 0.3f;
            Cooldown = 5f;
        }

        [Tooltip("Reach of the grab. Shorter than Sneaky Steal's 5.4 — the Warrior has to get "
               + "closer than the Assassin does.")]
        [Min(0.5f)] public float ScrapRange = 3.8f;

        [Tooltip("Cargo taken per grab, before Bully's multiplier. Half of Sneaky Steal's 6.")]
        [Min(1f)] public float StealAmount = 3f;

        protected override string DefaultIcon => "🪝";

        public override float IndicatorRange => ScrapRange;
        public override bool RequiresEnemyInRange => true;

        public override AbilityAimShape AimShape => AbilityAimShape.SingleTarget;
        public override float AimRadius => ScrapRange;

        public override float NominalStealAmount => StealAmount;

        /// <summary>Only a cargo-carrier is worth grabbing at.</summary>
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

            float spaceLeft = thiefCargo.Capacity - thiefCargo.Cargo;
            if (spaceLeft <= 0f) return;

            // GatherTargets sorts ascending by distance, so index 0 is the nearest carrier.
            GatherTargets(thief, _scratch);
            if (_scratch.Count == 0) return;

            var victim = _scratch[0].Cargo;
            if (victim == null) return;

            float take = Mathf.Min(ResolveStealAmount(StealAmount, thief), victim.Cargo, spaceLeft);
            if (take <= 0f) return;

            thiefCargo.Cargo += take;
            victim.RPC_DrainStolen(take, thief.Id);
        }

        public override void OnDeactivate(AbilityContext ctx) { }
    }
}

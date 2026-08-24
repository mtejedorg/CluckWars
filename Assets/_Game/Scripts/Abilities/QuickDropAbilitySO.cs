using UnityEngine;

namespace CluckWars.Abilities
{
    /// <summary>
    /// <b>Speedy.</b> Dumps the whole beakful into the base in one motion instead of unloading
    /// at a walk.
    /// </summary>
    /// <remarks>
    /// <b>This was a passive until 2026-08-23, and moving it is a balance fix, not a
    /// reorganisation.</b> As <c>DropAndGoPassiveSO</c> it multiplied deposit rate permanently
    /// and its own source flagged it at roughly <b>27% of Speedy's SCT</b> — the most
    /// SCT-distorting passive in the set. Passives are free: no slot, no cooldown, no decision.
    ///
    /// It also could not survive the specialization split. Speedy carried three passives against
    /// a UI that offers two, and pairing this one with Featherfoot would have given a single
    /// build both "never slow down at the pile" and "never slow down at base" — most of the
    /// class's SCT budget in one pick, for free.
    ///
    /// As an ability the same tempo now costs a slot AND a cooldown, so it competes with Dust
    /// Kick and Feint instead of arriving alongside them. Maestro: re-author it "so the tempo
    /// gain costs a slot".
    ///
    /// <b>Still SCT-sensitive.</b> It moves banking time directly, so its multiplier and
    /// duration belong to <c>BalanceOracle</c>, not to hand-tuning — the values here are a
    /// starting point deliberately well below the passive's old permanent 6x.
    /// </remarks>
    [CreateAssetMenu(fileName = "QuickDrop",
        menuName = "Cluck Wars/Ability/Utility/Quick Drop", order = 7)]
    public sealed class QuickDropAbilitySO : AbilityBaseSO
    {
        public QuickDropAbilitySO()
        {
            Category = AbilityCategory.Utility;
            SlotKind = AbilitySlotKind.Character;
            AllowedClasses = ChickenClassFlags.Speedy;
            // Bank, NOT Forage. It was Forage until 2026-08-23 and that quietly cost the
            // Speedy bot its entire income: BotController fires the Forage role while parked
            // at a pile, and TryGetReadySlotForRole returned the FIRST matching slot — so a
            // Speedy holding both Quick Drop and Peck burned a 12 s deposit-rate cooldown at
            // the pile, did nothing, and never pecked at all when Quick Drop sat in the lower
            // slot. Bank is fired at the base, which is the only place this ability does
            // anything.
            BotRole = BotRole.Bank;
            Duration = 2f;
            Cooldown = 12f;
        }

        [Tooltip("Deposit-rate multiplier while active. 4x against the retired passive's "
               + "permanent 6x - it is a burst you have to time at the base, not a standing "
               + "discount on every trip. Feeds Solo Clear Time: re-solve, never hand-tune.")]
        [Range(1.1f, 8f)] public float DepositRateMultiplier = 4f;

        protected override string DefaultIcon => "💰";

        // Self-only: nothing is aimed at, so the indicator marks the caster's own ring.
        public override AbilityAimShape AimShape => AbilityAimShape.None;
        public override bool AffectsSelf => true;
        public override bool AffectsEnemies => false;

        public override void OnActivate(AbilityContext ctx)
        {
            ctx.Controller.DepositRateMultiplier = DepositRateMultiplier;
        }

        public override void OnDeactivate(AbilityContext ctx)
        {
            ctx.Controller.DepositRateMultiplier = 1f;
        }
    }
}

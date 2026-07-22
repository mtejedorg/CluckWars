using CluckWars.Gameplay;
using UnityEngine;

namespace CluckWars.Abilities
{
    /// <summary>
    /// Assassin passive: bonus damage against impaired targets.
    /// </summary>
    [CreateAssetMenu(fileName = "Opportunist", menuName = "Cluck Wars/Passive/Opportunist", order = 10)]
    public sealed class OpportunistPassiveSO : PassiveAbilitySO
    {
        public OpportunistPassiveSO()
        {
            DisplayName = "Opportunist";
            ShortLabel = "OPPR";
            Category = AbilityCategory.Utility;
            SlotKind = AbilitySlotKind.Character;
            AllowedClasses = ChickenClassFlags.Assassin;
        }

        protected override string DefaultIcon => "🗡️";

        public override void OnActivate(AbilityContext ctx)
        {
            // Registered on spawn via AbilityController
        }
    
        /// <summary>
        /// Assassin alternative: +30% damage against a target that is already slowed,
        /// rooted or stunned — the payoff for the Deny corner of the mobility triangle.
        /// </summary>
        public override float ModifyOutgoingDamage(float amount, CluckWars.Gameplay.ChickenController self,
                                                   CluckWars.Gameplay.ChickenController target)
        {
            if (target == null) return amount;
            // 0.92 mirrors the threshold ChickenController uses to raise the Slowed VFX flag,
            // so "looks slowed" and "counts as slowed" can't drift apart.
            bool controlled = target.Rooted
                              || target.SlowMultiplier < 0.92f
                              || (target.Combat != null && target.Combat.IsStunned);
            return controlled ? amount * 1.30f : amount;
        }
}
}

using CluckWars.Gameplay;
using UnityEngine;

namespace CluckWars.Abilities
{
    /// <summary>
    /// <b>Warrior signature.</b> Abilities come back faster. The Warrior is the ability class,
    /// and its two specializations are the two axes an ability can improve on: frequency
    /// (this) or magnitude (Bully).
    /// </summary>
    /// <remarks>
    /// ⚠️ <b>Deliberately excludes Peck.</b> Peck is an ability with a cooldown like any
    /// other, so a blanket reduction would hand the Warrior ~25% more farming throughput —
    /// an effect that lives entirely outside the Balance Oracle and would break its SCT
    /// target for anyone who equipped this. Relentless is a combat passive; making it a
    /// farming passive by accident is exactly the kind of drift the Oracle exists to catch,
    /// and it would not catch this one because passives are measured outside the naked axiom.
    /// </remarks>
    [CreateAssetMenu(fileName = "Relentless", menuName = "Cluck Wars/Passive/Relentless", order = 15)]
    public sealed class RelentlessPassiveSO : PassiveAbilitySO
    {
        [Tooltip("Multiplier on ability cooldowns. 0.75 = 25% faster. Does NOT apply to Peck.")]
        [Range(0.25f, 1f)] public float CooldownMultiplier = 0.75f;

        public RelentlessPassiveSO()
        {
            DisplayName = "Relentless";
            ShortLabel = "RLNT";
            AllowedClasses = ChickenClassFlags.Warrior;
        }

        public override bool IsSignature => true;
        protected override string DefaultIcon => "\u26A1";

        public override float ModifyCooldown(float seconds, AbilityBaseSO ability, ChickenController self)
        {
            if (ability is PeckAbilitySO) return seconds; // foraging cadence is the Oracle's, not ours
            return seconds * CooldownMultiplier;
        }
    }
}

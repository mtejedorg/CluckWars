using CluckWars.Gameplay;
using CluckWars.Localization;
using UnityEngine;

namespace CluckWars.Abilities
{
    /// <summary>
    /// <b>Assassin alternative.</b> Every steal takes more. The Assassin cannot forage at
    /// all, so its entire income is predatory — this is the build that leans all the way
    /// into that rather than waiting for executes.
    /// </summary>
    [CreateAssetMenu(fileName = "Thief", menuName = "Cluck Wars/Passive/Thief", order = 18)]
    public sealed class ThiefPassiveSO : PassiveAbilitySO
    {
        [Tooltip("Multiplier on cargo taken per steal. The only steal passive since 2026-11-05: it is the " +
                 "Assassin's only sustained income between executes.")]
        [Min(1f)] public float StealMultiplier = 1.6f;

        [Header("Loot mark (BL-217: simulator only, nothing in the game reads these yet)")]
        [Tooltip("New food a marked steal adds to the Thief's bounty bag on top of what it takes. 0 switches the loot mark off " +
                 "(the shipped value: the game does not have the mechanic yet).")]
        [Min(0)] public int LootBounty = 0;
        [Tooltip("Seconds after which a mark moves to another rival. Checked only while Steal is on cooldown: with Steal ready the mark stays.")]
        [Min(1f)] public float LootSeconds = 12f;
        [Tooltip("The next mark appears this many seconds before Steal comes off cooldown (at once when that has already passed).")]
        [Range(0f, 6f)] public float LootLeadSeconds = 1f;
        [Tooltip("True: the marked take goes to the bounty bag (ignores the beak's room). False (a probe): it goes to the beak, held to its free room.")]
        public bool LootTakeToBag = true;
        [Tooltip("Lottery weight every rival has, however little it holds.")]
        [Min(0.01f)] public float LootBaseWeight = 4f;
        [Tooltip("Lottery weight per food banked at the rival's base (Maestro: it weighs a lot).")]
        [Min(0f)] public float LootBankedWeight = 1f;
        [Tooltip("Lottery weight per food the rival carries, beak plus bounty bag (Maestro: somewhat less than banked).")]
        [Min(0f)] public float LootCarriedWeight = 0.5f;
        [Tooltip("Banked plus carried above this share of the match's win target earns the over bonus (0.625 of 40 is Maestro's 25).")]
        [Range(0f, 2f)] public float LootOverFraction = 0.625f;
        [Tooltip("Extra lottery weight for a rival above the over threshold.")]
        [Min(0f)] public float LootOverBonus = 10f;

        [Header("End of the clock")]
        [Tooltip("Food banked at the base when the timer expires with no winner, before the winner is picked. The Thief's alone; 0 is the shipped value.")]
        [Min(0)] public int ClockPrize = 0;

        public ThiefPassiveSO()
        {
            DisplayName = "Thief";
            ShortLabel = "THEF";
            AllowedClasses = ChickenClassFlags.Assassin;
        }

        public override float ModifyStealAmount(float amount, ChickenController self) =>
            amount * StealMultiplier;

        public override float MaxStealMultiplier => StealMultiplier;

        public override int MatchEndBonusFood(ChickenController self) => ClockPrize;

        public override LootMarkFacts LootMark => new LootMarkFacts
        {
            IsThief = true,
            Bounty = LootBounty,
            Seconds = LootSeconds,
            LeadSeconds = LootLeadSeconds,
            TakeToBag = LootTakeToBag,
            OverFraction = LootOverFraction,
            Weights = new LootWeightTable
            {
                Base = LootBaseWeight, Banked = LootBankedWeight, Carried = LootCarriedWeight, OverBonus = LootOverBonus,
            },
        };

        public override string PerkLineKey => UiKeys.PerkThiefLine;
        public override (string name, object value)[] PerkArgs(MatchConfigSO match) =>
            new (string, object)[] { ("steal", Percent(StealMultiplier - 1f)) };
    }
}

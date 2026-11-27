using CluckWars.Gameplay;
using CluckWars.Localization;
using UnityEngine;

namespace CluckWars.Abilities
{
    /// <summary>
    /// <b>Assassin alternative.</b> The Thief's income is predatory (the Assassin cannot forage): one rival at a time carries his loot mark, and a Steal that
    /// reaches it takes its food and adds <see cref="LootBounty"/> of new food into his ordinary cargo, held to its room (BL-217, design 2026-11-23). The
    /// numbers live here, never on Steal, which the Warrior kits equip too. At the clock he banks <see cref="ClockPrize"/> before the winner is picked.
    /// </summary>
    [CreateAssetMenu(fileName = "Thief", menuName = "Cluck Wars/Passive/Thief", order = 18)]
    public sealed class ThiefPassiveSO : PassiveAbilitySO
    {
        [Tooltip("Multiplier on cargo taken per steal. Thief.asset ships 1 since 2026-11-24 (the loot mark replaced the 1.6x); the field stays so the " +
                 "simulator can sweep it.")]
        [Min(1f)] public float StealMultiplier = 1.6f;

        [Header("Loot mark (BL-217)")]
        [Tooltip("New food a marked steal adds to the Thief's cargo on top of what it takes, as far as it fits (the stolen food comes first). " +
                 "0 switches the loot mark off; Thief.asset ships 10.")]
        [Min(0)] public int LootBounty = 0;
        [Tooltip("Seconds after which a mark moves to another rival. Checked only while Steal is on cooldown: with Steal ready the mark stays.")]
        [Min(1f)] public float LootSeconds = 12f;
        [Tooltip("The next mark appears this many seconds before Steal comes off cooldown (at once when that has already passed).")]
        [Range(0f, 6f)] public float LootLeadSeconds = 1f;
        [Tooltip("Lottery weight every rival has, however little it holds.")]
        [Min(0.01f)] public float LootBaseWeight = 4f;
        [Tooltip("Lottery weight per food banked at the rival's base (Maestro: it weighs a lot).")]
        [Min(0f)] public float LootBankedWeight = 1f;
        [Tooltip("Lottery weight per food the rival carries, the food it holds (Maestro: somewhat less than banked).")]
        [Min(0f)] public float LootCarriedWeight = 0.5f;
        [Tooltip("Banked plus carried above this share of the match's win target earns the over bonus (0.625 of 40 is Maestro's 25).")]
        [Range(0f, 2f)] public float LootOverFraction = 0.625f;
        [Tooltip("Extra lottery weight for a rival above the over threshold.")]
        [Min(0f)] public float LootOverBonus = 10f;

        [Header("Loot mark rule v2 (2026-11-26)")]
        [Tooltip("Least food a rival must carry to be a candidate for the mark.")]
        [Min(0f)] public float LootMinCargo = 6f;
        [Tooltip("Last resort: when nobody carries LootMinCargo, any rival carrying at least this much can take the mark (the other filters stay). 0 switches the fallback off.")]
        [Min(0f)] public float LootFallbackMinCargo = 1f;
        [Tooltip("Stalled draw: when a draw is allowed and no rival carries anything for this many seconds, the mark lands on a random rival, empty beak included. 0 switches it off.")]
        [Min(0f)] public float LootStallSeconds = 5f;
        [Tooltip("Metres from his own base centre at which a rival weighs in full for the mark; closer he weighs less, down to LootHomeMinWeight at his base's wall. Soft: a bird at home still gets the mark when nobody else is eligible.")]
        [Min(0f)] public float LootHomeRadius = 8f;
        [Tooltip("Weight factor (0..1) of a rival at or inside his base's wall, on top of the lottery weight.")]
        [Range(0f, 1f)] public float LootHomeMinWeight = 0.05f;
        [Tooltip("A marked bird carrying less than this (but more than 0) is light: the mark leaves him while Steal is on cooldown, and when Steal is ready only if a candidate no farther than he is (plus LootSwitchSlackMeters) exists.")]
        [Min(0f)] public float LootKeepCargo = 3f;
        [Tooltip("Metres a candidate may be farther from the Thief than a light marked bird and still take the mark from him.")]
        [Min(0f)] public float LootSwitchSlackMeters = 3f;
        [Tooltip("No mark is drawn before the match has run this many seconds.")]
        [Min(0f)] public float LootOpeningSeconds = 5f;
        [Tooltip("No new mark is drawn within this many seconds of a mark ending unpaid.")]
        [Min(0f)] public float LootRedrawGapSeconds = 1.5f;

        [Header("Carrying")]
        [Tooltip("Capacity floor of the Thief's beak, like the Hoarder's: the Sneaky class stat is shared with the Spoiler and does not move. " +
                 "0 is no floor (the class capacity); Thief.asset ships 20. It sets how many marked steals one trip holds.")]
        [Min(0f)] public float MinimumCapacity = 0f;

        [Header("End of the clock")]
        [Tooltip("Food banked at the base when the timer expires with no winner, before the winner is picked. The Thief's alone; Thief.asset ships 20.")]
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

        public override float ModifyCargoCapacity(float capacity, ChickenController self) =>
            Mathf.Max(capacity, MinimumCapacity);

        public override int MatchEndBonusFood(ChickenController self) => ClockPrize;

        public override LootMarkFacts LootMark => new LootMarkFacts
        {
            IsThief = true,
            Bounty = LootBounty,
            Seconds = LootSeconds,
            LeadSeconds = LootLeadSeconds,
            OverFraction = LootOverFraction,
            MinCargo = LootMinCargo,
            FallbackMinCargo = LootFallbackMinCargo,
            StallSeconds = LootStallSeconds,
            HomeRadius = LootHomeRadius,
            HomeMinWeight = LootHomeMinWeight,
            KeepCargo = LootKeepCargo,
            SwitchSlackMeters = LootSwitchSlackMeters,
            OpeningSeconds = LootOpeningSeconds,
            RedrawGapSeconds = LootRedrawGapSeconds,
            Weights = new LootWeightTable
            {
                Base = LootBaseWeight, Banked = LootBankedWeight, Carried = LootCarriedWeight, OverBonus = LootOverBonus,
            },
        };

        public override string PerkLineKey => UiKeys.PerkThiefLine;
        public override string PerkDetailKey => UiKeys.PerkThiefDetail;

        // {goal} is the one number this passive does not own: the match's win target.
        // A null MatchConfig leaves {goal} unsupplied, which UiText renders as #goal# and logs.
        public override (string name, object value)[] PerkArgs(MatchConfigSO match) =>
            match != null
                ? new (string, object)[] { ("bounty", LootBounty), ("prize", ClockPrize), ("cap", Mathf.RoundToInt(MinimumCapacity)), ("goal", match.FoodTargetToWin) }
                : new (string, object)[] { ("bounty", LootBounty), ("prize", ClockPrize), ("cap", Mathf.RoundToInt(MinimumCapacity)) };
    }
}

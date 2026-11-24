using CluckWars.Gameplay;
using CluckWars.Localization;
using UnityEngine;

namespace CluckWars.Abilities
{
    /// <summary>
    /// <b>Assassin signature.</b> If the timer expires with nobody having reached the win
    /// target, the Assassin banks a bonus before the normal "most banked wins" resolution.
    /// </summary>
    /// <remarks>
    /// <b>The size is the whole design.</b> The core-redesign spec sets it at +15 precisely
    /// because that vaults a mid-pack Assassin over a typical stalemate leader (~25-35 held)
    /// but <i>cannot win from zero</i> — so "idle in a corner and stall the match" is never a
    /// winning line. Raising this until it can win from nothing turns the class into exactly
    /// that. Two Assassins in a lobby is well-defined: both get the bonus, normal resolution
    /// then decides.
    /// <para>
    /// The default reads <c>MatchConfig.SpoilerBounty</c>, which already ships at 15 — the
    /// value predates this passive, it just had nothing reading it until now.
    /// </para>
    /// </remarks>
    [CreateAssetMenu(fileName = "Spoiler", menuName = "Cluck Wars/Passive/Spoiler", order = 17)]
    public sealed class SpoilerPassiveSO : PassiveAbilitySO
    {
        [Tooltip("Food banked if the timer expires with no winner. Should match " +
                 "MatchConfig.SpoilerBounty. Big enough to beat a stalemate leader, never " +
                 "big enough to win from zero.")]
        [Min(0)] public int BonusFood = 15;

        [Header("Carrying")]
        [Tooltip("Capacity floor of the Spoiler's beak, like the Hoarder's: the Sneaky class stat is shared with the Thief and does not move. " +
                 "A kill's loot (the victim's cargo, then the execute bounty) is ordinary cargo held to this room (Maestro, 2026-11-24: " +
                 "no capacity-free bag, 25 or so). 0 would leave the class capacity.")]
        [Min(0f)] public float MinimumCapacity = 25f;

        public SpoilerPassiveSO()
        {
            DisplayName = "Assassin";
            ShortLabel = "ASSN";
            AllowedClasses = ChickenClassFlags.Assassin;
        }

        public override bool IsSignature => true;

        public override int MatchEndBonusFood(ChickenController self) => BonusFood;

        public override float ModifyCargoCapacity(float capacity, ChickenController self) =>
            Mathf.Max(capacity, MinimumCapacity);

        public override string PerkLineKey => UiKeys.PerkSpoilerLine;
        public override string PerkDetailKey => UiKeys.PerkSpoilerDetail;
        // {goal} is the one number this passive does not own: the match's win target.
        // A null MatchConfig leaves {goal} unsupplied, which UiText renders as #goal# and logs.
        public override (string name, object value)[] PerkArgs(MatchConfigSO match) =>
            match != null
                ? new (string, object)[] { ("food", BonusFood), ("cap", Mathf.RoundToInt(MinimumCapacity)), ("goal", match.FoodTargetToWin) }
                : new (string, object)[] { ("food", BonusFood), ("cap", Mathf.RoundToInt(MinimumCapacity)) };
    }
}

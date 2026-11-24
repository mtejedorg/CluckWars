using CluckWars.Gameplay;
using CluckWars.Localization;
using UnityEngine;

namespace CluckWars.Abilities
{
    /// <summary>
    /// <b>Assassin signature.</b> The Spoiler's passive is two things: the end-of-clock prize hook, which ships at 0 (Maestro, 2026-11-24, loot mark
    /// design D4: the clock prize is the Thief's alone now), and the beak floor. The text names what the Hunt already does, its pull toward the leader
    /// (<c>MarkKill.asset</c> <c>LeaderPickWeight</c>, read from the kit's own Hunt asset) and the capacity.
    /// </summary>
    /// <remarks>
    /// <b>The bonus hook stays.</b> <c>GameManager.AwardMatchEndBonuses</c> pays whatever <see cref="MatchEndBonusFood"/> returns before the winner is
    /// picked; with <see cref="BonusFood"/> 0 it pays nothing. The field stays so the match simulator and a balance delta can still sweep it, and so a
    /// future kit that wants a clock prize needs no new hook. A prize must stay small enough that it <i>cannot win from zero</i>.
    /// </remarks>
    [CreateAssetMenu(fileName = "Spoiler", menuName = "Cluck Wars/Passive/Spoiler", order = 17)]
    public sealed class SpoilerPassiveSO : PassiveAbilitySO
    {
        [Tooltip("Food banked if the timer expires with no winner. Spoiler.asset ships 0 since 2026-11-24 (the clock prize is the Thief's). " +
                 "If raised: big enough to beat a stalemate leader, never big enough to win from zero.")]
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

        /// <summary>The Hunt's pull toward the rival with the most food banked, read from the kit's own Hunt asset (never typed here).</summary>
        private float LeaderPull
        {
            get
            {
                if (Kit != null)
                    foreach (var ability in Kit)
                        if (ability is MarkKillAbilitySO hunt) return hunt.LeaderPickWeight;
                return 1f;
            }
        }

        public override (string name, object value)[] PerkArgs(MatchConfigSO match) =>
            new (string, object)[] { ("x", Num(LeaderPull)), ("cap", Mathf.RoundToInt(MinimumCapacity)) };
    }
}

using CluckWars.Gameplay;
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

        public SpoilerPassiveSO()
        {
            DisplayName = "Spoiler";
            ShortLabel = "SPOI";
            AllowedClasses = ChickenClassFlags.Assassin;
        }

        public override bool IsSignature => true;
        protected override string DefaultIcon => "\U0001F5A4";

        public override int MatchEndBonusFood(ChickenController self) => BonusFood;
    }
}

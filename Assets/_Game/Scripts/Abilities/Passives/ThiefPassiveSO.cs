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
        [Tooltip("Multiplier on cargo taken per steal. Higher than Bully's because it is the " +
                 "Assassin's only sustained income between executes.")]
        [Min(1f)] public float StealMultiplier = 1.6f;

        public ThiefPassiveSO()
        {
            DisplayName = "Thief";
            ShortLabel = "THEF";
            AllowedClasses = ChickenClassFlags.Assassin;
        }

        public override float ModifyStealAmount(float amount, ChickenController self) =>
            amount * StealMultiplier;

        public override float MaxStealMultiplier => StealMultiplier;

        public override string PerkLineKey => UiKeys.PerkThiefLine;
        public override (string name, object value)[] PerkArgs(MatchConfigSO match) =>
            new (string, object)[] { ("steal", Percent(StealMultiplier - 1f)) };
    }
}

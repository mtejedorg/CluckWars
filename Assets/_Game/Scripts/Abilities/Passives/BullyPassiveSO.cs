using CluckWars.Gameplay;
using CluckWars.Localization;
using UnityEngine;

namespace CluckWars.Abilities
{
    /// <summary>
    /// <b>Warrior alternative.</b> Steals bigger, and carries enough extra cargo to hold what
    /// it takes. The magnitude axis to Relentless's frequency axis.
    /// </summary>
    /// <remarks>
    /// The extra capacity is not a throwaway: a steal is clamped to the thief's free space,
    /// so raising the steal amount without raising capacity would quietly do nothing for a
    /// Warrior who is already carrying a load.
    /// </remarks>
    [CreateAssetMenu(fileName = "Bully", menuName = "Cluck Wars/Passive/Bully", order = 16)]
    public sealed class BullyPassiveSO : PassiveAbilitySO
    {
        [Tooltip("Multiplier on cargo taken per steal.")]
        [Min(1f)] public float StealMultiplier = 1.4f;

        [Tooltip("Extra cargo capacity, so the bigger steal has somewhere to go.")]
        [Min(0f)] public float BonusCapacity = 6f;

        public BullyPassiveSO()
        {
            DisplayName = "Bully";
            ShortLabel = "BULY";
            AllowedClasses = ChickenClassFlags.Warrior;
        }

        public override float ModifyStealAmount(float amount, ChickenController self) =>
            amount * StealMultiplier;

        public override float MaxStealMultiplier => StealMultiplier;

        public override float ModifyCargoCapacity(float capacity, ChickenController self) =>
            capacity + BonusCapacity;

        public override string PerkLineKey => UiKeys.PerkBullyLine;
        public override (string name, object value)[] PerkArgs(MatchConfigSO match) =>
            new (string, object)[] { ("steal", Percent(StealMultiplier - 1f)), ("cap", Mathf.RoundToInt(BonusCapacity)) };
    }
}

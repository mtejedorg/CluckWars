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
    /// <para>
    /// <b>Bonus cut 6 → 2 on 2026-10-22</b> (Maestro, decisions A2/F1; the steal ×1.4 stays).
    /// +6 made a Bully farm like a Speedy (Oracle 30.9 s / 2 trips against the Warrior's
    /// 35 s / 3). +2 is outside the ±0.5 s guard on the slow side, which is a greedy-router
    /// artifact rather than a real slowdown, so it is a DECLARED target in
    /// <c>SpecSctTargets</c> and the spec-aware Oracle test pins it. Changing this number
    /// changes that test.
    /// </para>
    /// </remarks>
    [CreateAssetMenu(fileName = "Bully", menuName = "Cluck Wars/Passive/Bully", order = 16)]
    public sealed class BullyPassiveSO : PassiveAbilitySO
    {
        [Tooltip("Multiplier on cargo taken per steal.")]
        [Min(1f)] public float StealMultiplier = 1.4f;

        [Tooltip("Extra cargo capacity, so the bigger steal has somewhere to go.")]
        [Min(0f)] public float BonusCapacity = 2f;

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

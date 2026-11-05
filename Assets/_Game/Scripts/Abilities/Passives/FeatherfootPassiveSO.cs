using CluckWars.Gameplay;
using CluckWars.Localization;
using UnityEngine;

namespace CluckWars.Abilities
{
    /// <summary>
    /// <b>Speedy alternative.</b> Banks faster, and is immune to the pile-slow and the collision slow, so a contested
    /// pile can be raided at full speed and a chaser cannot drag it down by bumping into it.
    /// </summary>
    /// <remarks>
    /// This passive got considerably stronger when foraging became Peck. Pile-slow used to
    /// bite only while brushing past; a chicken now stands on a pile for seconds at a time
    /// pressing Peck, so ignoring the slow is the difference between being catchable there
    /// and not. Flagged for the first balance pass rather than pre-nerfed on a guess.
    /// <para>
    /// The collision slow joined it on 2026-10-21 (A6, Maestro): both are ambient drag with
    /// nobody to blame, and Featherfoot is the "nothing slows my feet" fork. Ability slows
    /// (Feather Trap, Dust Kick, auras) still land. Needs a human playtest for chase feel.
    /// </para>
    /// <para>
    /// <b>Quick Drop folded in (Maestro 2026-11-05):</b> the retired ability's job is now this passive's permanent
    /// <see cref="DepositRateMultiplier"/> (x1.1, against the old 4x-for-2s burst; first shipped at x1.5, tuned down the same day by Maestro so the
    /// farming run lands on the Speedy SCT target: 29.62 s against 30 s +-0.5, no declared exception), read by the deposit chokepoint
    /// <c>ChickenCargo.ResolveDepositRate</c> on the authority. It moves banking time, so the Balance Oracle measures it
    /// through <see cref="ModifyDepositRate"/> (<c>RealMap_ShippedPassives_SatisfyTheSctAxiomOrADeclaredTarget</c>).
    /// </para>
    /// </remarks>
    [CreateAssetMenu(fileName = "Featherfoot", menuName = "Cluck Wars/Passive/Featherfoot", order = 11)]
    public sealed class FeatherfootPassiveSO : PassiveAbilitySO
    {
        [Tooltip("Permanent multiplier on banking speed at the own base (Quick Drop's job since 2026-11-05). 1.1 = 10% faster. " +
                 "Feeds Solo Clear Time: re-run the Balance Oracle, never hand-tune.")]
        [Range(1f, 4f)] public float DepositRateMultiplier = 1.1f;

        public FeatherfootPassiveSO()
        {
            DisplayName = "Featherfoot";
            ShortLabel = "FEAT";
            AllowedClasses = ChickenClassFlags.Speedy;
        }

        public override bool IgnoresPileSlow(ChickenController self) => true;

        public override bool IgnoresCollisionSlow(ChickenController self) => true;

        public override float ModifyDepositRate(float perSecond, ChickenController self) =>
            perSecond * DepositRateMultiplier;

        public override string PerkLineKey => UiKeys.PerkFeatherfootLine;
        public override string PerkDetailKey => UiKeys.PerkFeatherfootDetail;
        public override (string name, object value)[] PerkArgs(MatchConfigSO match) =>
            new (string, object)[] { ("pct", Percent(DepositRateMultiplier - 1f)) };
    }
}

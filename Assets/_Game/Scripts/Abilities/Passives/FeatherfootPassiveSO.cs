using CluckWars.Gameplay;
using CluckWars.Localization;
using UnityEngine;

namespace CluckWars.Abilities
{
    /// <summary>
    /// <b>Speedy alternative.</b> Immune to the pile-slow and the collision slow, so a contested
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
    /// </remarks>
    [CreateAssetMenu(fileName = "Featherfoot", menuName = "Cluck Wars/Passive/Featherfoot", order = 11)]
    public sealed class FeatherfootPassiveSO : PassiveAbilitySO
    {
        public FeatherfootPassiveSO()
        {
            DisplayName = "Featherfoot";
            ShortLabel = "FEAT";
            AllowedClasses = ChickenClassFlags.Speedy;
        }

        public override bool IgnoresPileSlow(ChickenController self) => true;

        public override bool IgnoresCollisionSlow(ChickenController self) => true;

        public override string PerkLineKey => UiKeys.PerkFeatherfootLine;
        public override string PerkDetailKey => UiKeys.PerkFeatherfootDetail;
    }
}

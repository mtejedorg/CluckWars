using CluckWars.Gameplay;
using UnityEngine;

namespace CluckWars.Abilities
{
    /// <summary>
    /// <b>Speedy alternative.</b> Immune to the pile-slow, so a contested pile can be raided
    /// at full speed.
    /// </summary>
    /// <remarks>
    /// This passive got considerably stronger when foraging became Peck. Pile-slow used to
    /// bite only while brushing past; a chicken now stands on a pile for seconds at a time
    /// pressing Peck, so ignoring the slow is the difference between being catchable there
    /// and not. Flagged for the first balance pass rather than pre-nerfed on a guess.
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

        protected override string DefaultIcon => "\U0001FAB6";

        public override bool IgnoresPileSlow(ChickenController self) => true;
    }
}

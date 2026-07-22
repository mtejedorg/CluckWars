using CluckWars.Gameplay;
using UnityEngine;

namespace CluckWars.Abilities
{
    /// <summary>
    /// Speedy passive: movement speed boost when health is low.
    /// </summary>
    [CreateAssetMenu(fileName = "SecondWind", menuName = "Cluck Wars/Passive/SecondWind", order = 10)]
    public sealed class SecondWindPassiveSO : PassiveAbilitySO
    {
        public SecondWindPassiveSO()
        {
            DisplayName = "Second Wind";
            ShortLabel = "WIND";
            Category = AbilityCategory.Utility;
            SlotKind = AbilitySlotKind.Character;
            AllowedClasses = ChickenClassFlags.Speedy;
        }

        protected override string DefaultIcon => "💨";

        public override void OnActivate(AbilityContext ctx)
        {
            // Registered on spawn via AbilityController
        }
    }
}

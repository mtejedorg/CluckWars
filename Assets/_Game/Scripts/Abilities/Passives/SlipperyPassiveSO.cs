using CluckWars.Gameplay;
using UnityEngine;

namespace CluckWars.Abilities
{
    /// <summary>
    /// Speedy passive: 40% control-state duration reduction.
    /// </summary>
    [CreateAssetMenu(fileName = "Slippery", menuName = "Cluck Wars/Passive/Slippery", order = 10)]
    public sealed class SlipperyPassiveSO : PassiveAbilitySO
    {
        public SlipperyPassiveSO()
        {
            DisplayName = "Slippery";
            ShortLabel = "SLIP";
            Category = AbilityCategory.Utility;
            SlotKind = AbilitySlotKind.Character;
            AllowedClasses = ChickenClassFlags.Speedy;
        }

        protected override string DefaultIcon => "👟";

        public override void OnActivate(AbilityContext ctx)
        {
            // Registered on spawn via AbilityController
        }
    }
}

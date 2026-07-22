using CluckWars.Gameplay;
using UnityEngine;

namespace CluckWars.Abilities
{
    /// <summary>
    /// Assassin passive: unlocks 3rd ability slot.
    /// </summary>
    [CreateAssetMenu(fileName = "Combo", menuName = "Cluck Wars/Passive/Combo", order = 10)]
    public sealed class ComboPassiveSO : PassiveAbilitySO
    {
        public ComboPassiveSO()
        {
            DisplayName = "Combo";
            ShortLabel = "CMBO";
            Category = AbilityCategory.Utility;
            SlotKind = AbilitySlotKind.Character;
            AllowedClasses = ChickenClassFlags.Assassin;
        }

        protected override string DefaultIcon => "⚡";

        public override void OnActivate(AbilityContext ctx)
        {
            // Registered on spawn via AbilityController
        }
    }
}

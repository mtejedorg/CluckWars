using CluckWars.Gameplay;
using UnityEngine;

namespace CluckWars.Abilities
{
    /// <summary>
    /// Warrior passive: 15% damage resistance.
    /// </summary>
    [CreateAssetMenu(fileName = "Bracer", menuName = "Cluck Wars/Passive/Bracer", order = 10)]
    public sealed class BracerPassiveSO : PassiveAbilitySO
    {
        public BracerPassiveSO()
        {
            DisplayName = "Bracer";
            ShortLabel = "BRCR";
            Category = AbilityCategory.Defense;
            SlotKind = AbilitySlotKind.Character;
            AllowedClasses = ChickenClassFlags.Warrior;
        }

        protected override string DefaultIcon => "🛡️";

        public override void OnActivate(AbilityContext ctx)
        {
            // Registered on spawn via AbilityController
        }
    }
}

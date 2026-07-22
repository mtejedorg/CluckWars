using CluckWars.Gameplay;
using UnityEngine;

namespace CluckWars.Abilities
{
    /// <summary>
    /// Fatty passive: 85% knockback reduction.
    /// </summary>
    [CreateAssetMenu(fileName = "Immovable", menuName = "Cluck Wars/Passive/Immovable", order = 10)]
    public sealed class ImmovablePassiveSO : PassiveAbilitySO
    {
        public ImmovablePassiveSO()
        {
            DisplayName = "Immovable";
            ShortLabel = "IMMV";
            Category = AbilityCategory.Defense;
            SlotKind = AbilitySlotKind.Character;
            AllowedClasses = ChickenClassFlags.Fatty;
        }

        protected override string DefaultIcon => "🗿";

        public override void OnActivate(AbilityContext ctx)
        {
            // Registered on spawn via AbilityController
        }
    }
}

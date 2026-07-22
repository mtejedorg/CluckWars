using CluckWars.Gameplay;
using UnityEngine;

namespace CluckWars.Abilities
{
    /// <summary>
    /// Warrior passive: +25% outgoing damage.
    /// </summary>
    [CreateAssetMenu(fileName = "Mighty", menuName = "Cluck Wars/Passive/Mighty", order = 10)]
    public sealed class MightyPassiveSO : PassiveAbilitySO
    {
        public MightyPassiveSO()
        {
            DisplayName = "Mighty";
            ShortLabel = "MGHT";
            Category = AbilityCategory.Damage;
            SlotKind = AbilitySlotKind.Character;
            AllowedClasses = ChickenClassFlags.Warrior;
        }

        protected override string DefaultIcon => "⚔️";

        public override void OnActivate(AbilityContext ctx)
        {
            // Registered on spawn via AbilityController
        }
    }
}

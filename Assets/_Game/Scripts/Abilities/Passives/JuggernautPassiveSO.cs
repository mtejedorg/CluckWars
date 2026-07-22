using CluckWars.Gameplay;
using UnityEngine;

namespace CluckWars.Abilities
{
    /// <summary>
    /// Fatty passive: reduced terrain/pile slow.
    /// </summary>
    [CreateAssetMenu(fileName = "Juggernaut", menuName = "Cluck Wars/Passive/Juggernaut", order = 10)]
    public sealed class JuggernautPassiveSO : PassiveAbilitySO
    {
        public JuggernautPassiveSO()
        {
            DisplayName = "Juggernaut";
            ShortLabel = "JUGG";
            Category = AbilityCategory.Defense;
            SlotKind = AbilitySlotKind.Character;
            AllowedClasses = ChickenClassFlags.Fatty;
        }

        protected override string DefaultIcon => "🪵";

        public override void OnActivate(AbilityContext ctx)
        {
            // Registered on spawn via AbilityController
        }
    }
}

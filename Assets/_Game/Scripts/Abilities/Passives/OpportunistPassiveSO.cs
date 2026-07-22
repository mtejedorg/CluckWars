using CluckWars.Gameplay;
using UnityEngine;

namespace CluckWars.Abilities
{
    /// <summary>
    /// Assassin passive: bonus damage against impaired targets.
    /// </summary>
    [CreateAssetMenu(fileName = "Opportunist", menuName = "Cluck Wars/Passive/Opportunist", order = 10)]
    public sealed class OpportunistPassiveSO : PassiveAbilitySO
    {
        public OpportunistPassiveSO()
        {
            DisplayName = "Opportunist";
            ShortLabel = "OPPR";
            Category = AbilityCategory.Utility;
            SlotKind = AbilitySlotKind.Character;
            AllowedClasses = ChickenClassFlags.Assassin;
        }

        protected override string DefaultIcon => "🗡️";

        public override void OnActivate(AbilityContext ctx)
        {
            // Registered on spawn via AbilityController
        }
    }
}

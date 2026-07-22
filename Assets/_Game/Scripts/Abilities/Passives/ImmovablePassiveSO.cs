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
        /// <summary>Signature passive for this class — the default (ADR 0003 Decision 3).</summary>
        public override bool IsSignature => true;


        protected override string DefaultIcon => "🗿";

        public override void OnActivate(AbilityContext ctx)
        {
            // Registered on spawn via AbilityController
        }
    }
}

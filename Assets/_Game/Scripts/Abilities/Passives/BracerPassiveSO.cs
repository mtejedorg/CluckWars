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
    
        /// <summary>Warrior alternative: 15% damage resistance (ADR 0003 passive pool).</summary>
        public override float ModifyIncomingDamage(float amount, CluckWars.Gameplay.ChickenController self)
            => amount * 0.85f;
}
}

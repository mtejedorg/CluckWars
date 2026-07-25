using CluckWars.Gameplay;
using UnityEngine;

namespace CluckWars.Abilities
{
    [CreateAssetMenu(fileName = "SpineCoat",
        menuName = "Cluck Wars/Ability/Defense/Spine Coat", order = 3)]
    public sealed class SpineCoatAbilitySO : AbilityBaseSO
    {
        public SpineCoatAbilitySO()
        {
            Category = AbilityCategory.Defense;
            SlotKind = AbilitySlotKind.Character;
            AllowedClasses = ChickenClassFlags.Warrior;
        }

        [Tooltip("How much cargo to steal back on contact.")]
        [Min(1f)] public float StealBackAmount = 4f;

        protected override string DefaultIcon => "🦔";

        public override void OnActivate(AbilityContext ctx)
        {
            ctx.Controller.StealBackActive = true;
            ctx.Controller.StealBackAmount = StealBackAmount;
        }

        public override void OnDeactivate(AbilityContext ctx)
        {
            ctx.Controller.StealBackActive = false;
        }
    }
}

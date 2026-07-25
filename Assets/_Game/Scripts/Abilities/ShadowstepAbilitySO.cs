using CluckWars.Gameplay;
using UnityEngine;

namespace CluckWars.Abilities
{
    /// <summary>
    /// Assassin Utility ability: short Blink dash phasing over walls along facing direction.
    /// </summary>
    [CreateAssetMenu(fileName = "Shadowstep", menuName = "Cluck Wars/Ability/Utility/Shadowstep", order = 11)]
    public sealed class ShadowstepAbilitySO : AbilityBaseSO
    {
        public ShadowstepAbilitySO()
        {
            DisplayName = "Shadowstep";
            ShortLabel = "STEP";
            Description = "Short blink dash phasing over walls along facing direction.";
            Category = AbilityCategory.Utility;
            TerrainTraversal = TerrainTraversal.Blink;
            SlotKind = AbilitySlotKind.Character;
            AllowedClasses = ChickenClassFlags.Assassin;
            Duration = 0.4f;
            Cooldown = 6f;
        }

        [Tooltip("Speed multiplier applied during the blink dash.")]
        [Range(1.5f, 4f)] public float SpeedMultiplier = 3.0f;

        protected override string DefaultIcon => "👤";

        public override void OnActivate(AbilityContext ctx)
        {
            ctx.Controller.MoveSpeedMultiplier = SpeedMultiplier;
        }

        public override void OnDeactivate(AbilityContext ctx)
        {
            ctx.Controller.MoveSpeedMultiplier = 1f;
        }
    }
}

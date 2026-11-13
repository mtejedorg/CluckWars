using UnityEngine;
using CluckWars.Localization;

namespace CluckWars.Abilities
{
    /// <summary>
    /// Greatly increases movement speed for the active duration. Mobility ability —
    /// good for escape or chase. Stacks via <c>ChickenController.MoveSpeedMultiplier</c>;
    /// scalar resets to 1 on deactivate.
    /// </summary>
    [CreateAssetMenu(fileName = "SpeedBurst", menuName = "Cluck Wars/Ability/Speed Burst", order = 0)]
    public sealed class SpeedBurstAbilitySO : AbilityBaseSO
    {
        public override AbilityJob Job => AbilityJob.Speed;

        public override string DescriptionKey => UiKeys.AbilitySpeedBurstDesc;
        public override (string name, object value)[] DescriptionArgs() =>
            new (string, object)[] { ("mult", Num(SpeedMultiplier)), ("secs", Num(Duration)) };

        public SpeedBurstAbilitySO()
        {
            DisplayName = "Speed";
            ShortLabel = "FAST";
            Description = "A 1.5s sprint at 1.3x movement speed.";
            Category = AbilityCategory.Utility;
            AllowedClasses = ChickenClassFlags.All;
            Duration = 1.5f;
            Cooldown = 4f;
        }

        [Tooltip("Multiplier applied to MoveSpeed while active.")]
        [Min(1f)] public float SpeedMultiplier = 1.3f;

        // Self-buff, no target area — marks the caster's own ring instead (FEEDBACK.md §2.2).
        public override AbilityAimShape AuthoredAimShape => AbilityAimShape.None;

        /// <summary>A movement ability: its direction follows where the player moves, never the aim
        /// (Phase 6 chunk 5, A7).</summary>
        public override bool FollowsMovementNotAim => true;
        public override bool AffectsSelf => true;
        public override bool AffectsEnemies => false;

        public override void OnActivate(AbilityContext ctx)
        {
            ctx.Controller.Effects.SetMoveSpeed(ctx.Slot, SpeedMultiplier);
        }

        public override void OnDeactivate(AbilityContext ctx)
        {
            ctx.Controller.Effects.RemoveMoveSpeed(ctx.Slot);
        }
    }
}

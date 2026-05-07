using UnityEngine;

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
        [Tooltip("Multiplier applied to MoveSpeed while active. 2.5 ≈ doubled top speed without feeling teleporty.")]
        [Min(1f)] public float SpeedMultiplier = 2.5f;

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

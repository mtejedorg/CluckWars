using UnityEngine;

namespace CluckWars.Abilities
{
    /// <summary>
    /// Slows the chicken way down but absorbs most incoming damage. Defensive ability
    /// for protecting cargo through a dangerous crossing. Sets
    /// <c>ChickenController.MoveSpeedMultiplier</c> + <c>DamageResistance</c> on
    /// activate; resets both on deactivate.
    /// </summary>
    [CreateAssetMenu(fileName = "TurtleMode", menuName = "Cluck Wars/Ability/Turtle Mode", order = 2)]
    public sealed class TurtleModeAbilitySO : AbilityBaseSO
    {
        [Tooltip("Movement speed scalar while active. 0.25 = quarter speed, slow but still mobile.")]
        [Range(0.05f, 1f)] public float SpeedMultiplier = 0.25f;

        [Tooltip("Damage reduction scalar while active. 0.8 = 80% of incoming damage absorbed.")]
        [Range(0f, 1f)] public float DamageResistance = 0.8f;

        public override void OnActivate(AbilityContext ctx)
        {
            ctx.Controller.MoveSpeedMultiplier = SpeedMultiplier;
            ctx.Controller.DamageResistance = DamageResistance;
        }

        public override void OnDeactivate(AbilityContext ctx)
        {
            ctx.Controller.MoveSpeedMultiplier = 1f;
            ctx.Controller.DamageResistance = 0f;
        }
    }
}

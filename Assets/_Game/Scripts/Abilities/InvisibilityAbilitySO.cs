using UnityEngine;

namespace CluckWars.Abilities
{
    /// <summary>
    /// Fades the chicken to near-invisible for the duration. Other chickens can
    /// still hit you if they aim well; opacity is a visual hint, not a true cloak
    /// (proper targeting penalties — break enemy auto-aim, hide HP bar — can land
    /// in a later polish pass). Mutates <c>ChickenController.VisualOpacity</c>,
    /// which is <c>[Networked]</c>, so the fade replicates to every peer's
    /// <c>ChickenVisuals.LateUpdate</c>.
    /// </summary>
    [CreateAssetMenu(fileName = "Invisibility", menuName = "Cluck Wars/Ability/Invisibility", order = 4)]
    public sealed class InvisibilityAbilitySO : AbilityBaseSO
    {
        [Tooltip("Alpha while active. 0 = totally invisible (fragile), 0.2 ≈ ghostly outline (readable).")]
        [Range(0f, 1f)] public float Opacity = 0.2f;

        public override void OnActivate(AbilityContext ctx)
        {
            ctx.Controller.VisualOpacity = Opacity;
        }

        public override void OnDeactivate(AbilityContext ctx)
        {
            ctx.Controller.VisualOpacity = 1f;
        }
    }
}

using UnityEngine;

namespace CluckWars.Abilities
{
    /// <summary>
    /// Fades the chicken to near-invisible for the duration. Other chickens can
    /// still hit you if they aim well; opacity is a visual hint, not a true cloak
    /// (Phase 9 will add proper targeting penalties). Mutates
    /// <c>ChickenController.VisualOpacity</c> which <c>ChickenVisuals</c> polls each
    /// frame. Note: opacity is a local property on the StateAuthority — remote
    /// peers see the visual via the chicken's standard tint replication, so
    /// invisibility is local-only (good enough for solo / Phase 6 demo).
    /// </summary>
    /// <remarks>
    /// Networked invisibility (so other players see the fade) requires either a
    /// <c>[Networked]</c> opacity field or piggybacking on an existing networked
    /// state change. Deferred to Phase 9 polish.
    /// </remarks>
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

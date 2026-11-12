using UnityEngine;

namespace CluckWars.Gameplay
{
    /// <summary>
    /// The pure geometry of a chicken's CharacterController capsule: where its bottom is, and how high the root
    /// pivot must sit for the capsule to stand on the ground. Gameplay needs both (seating a chicken at spawn and
    /// on a round reset, dropping the class model to the feet), so the maths lives here; the ground overlays'
    /// anchor (<c>ChickenGround</c>, presentation) forwards to it. No state, no scene access.
    /// </summary>
    public static class ChickenCapsule
    {
        /// <summary>Prefab capsule (height 1.6, centre 0) bottom, used only if the chicken
        /// somehow has no CharacterController. Never falls back to the root (that is the bug).</summary>
        public const float FallbackLocalFeetY = -0.8f;

        /// <summary>Local Y of the capsule bottom (the surface the capsule is modelled to touch).</summary>
        public static float LocalFeetY(CharacterController cc) =>
            cc != null ? cc.center.y - cc.height * 0.5f : FallbackLocalFeetY;

        /// <summary>
        /// World Y of the root pivot at which the chicken STANDS on ground at <paramref name="groundY"/>: the capsule bottom
        /// rests one skin width above it (the same resting gap <c>ChickenGround.LocalGroundY</c> models). CW-021 / CW-006: chickens
        /// used to be placed at the spawn point's Y (0.05) with the pivot at the capsule centre, i.e. half under the grass, and
        /// only the first <c>CharacterController.Move</c> (at GO) lifted them. Placing them at this height makes that Move a
        /// no-op, with no per-tick settle (which, moving a chicken whose transform had not caught up with a networked
        /// teleport, undid the round-reset teleports).
        /// </summary>
        public static float StandingPivotY(float capsuleCenterY, float capsuleHeight, float skinWidth, float rootScaleY, float groundY = 0f)
        {
            float scaleY = Mathf.Max(0.01f, Mathf.Abs(rootScaleY));
            return groundY + scaleY * (capsuleHeight * 0.5f - capsuleCenterY) + skinWidth;
        }
    }
}

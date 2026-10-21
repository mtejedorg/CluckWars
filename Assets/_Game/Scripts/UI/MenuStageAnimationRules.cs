using UnityEngine;

namespace CluckWars.UI
{
    /// <summary>
    /// What <see cref="MenuChickenStage"/> needs from a model before it samples clips onto it.
    /// </summary>
    /// <remarks>
    /// <see cref="AnimationClip.SampleAnimation"/> on a non-legacy clip only works outside the Editor
    /// when the target has an <see cref="Animator"/>; without one it logs "Non-Legacy animations
    /// cannot be sampled outside the Editor without an Animator" on every call (thousands of warnings
    /// per run, CW-207) and poses nothing in a player. The stage destroyed the imported Animator
    /// because it must not drive the model, so it was never posed on device/PC builds.
    /// An Animator with no controller drives nothing, so it is the lowest-risk way to make sampling legal.
    /// </remarks>
    public static class MenuStageAnimationRules
    {
        /// <summary>
        /// Guarantees <paramref name="model"/>'s root has an enabled, controller-less Animator and
        /// returns it. Keeps the imported Animator (and its Avatar) when there is one, otherwise adds one.
        /// </summary>
        public static Animator PrepareForSampling(GameObject model)
        {
            var animator = model.GetComponent<Animator>();
            if (animator == null) animator = model.AddComponent<Animator>();
            animator.runtimeAnimatorController = null;        // nothing drives the pose except SampleAnimation
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            animator.enabled = true;
            return animator;
        }
    }
}

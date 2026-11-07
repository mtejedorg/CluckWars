using CluckWars.Gameplay;
using UnityEngine;

namespace CluckWars.Visuals
{
    /// <summary>
    /// The one rule for how a hidden (faded) bird's world-space tells draw (spec 2026-11-06 section 3.2 / 4.8).
    /// "Faded" is <see cref="ControlStateVFX.IsFadedOpacity"/> and nothing else. A faded bird shows nothing
    /// to a remote peer; its own player sees it at <see cref="FeedbackTuning.LocalHiddenOpacityFloor"/> or
    /// more. Pure and allocation-free: called per frame by every tell renderer.
    /// </summary>
    public static class HiddenVisibilityRules
    {
        /// <summary>Alpha multiplier for a bird's tells. 1 when not faded (0 is the [Networked] "unset"
        /// default, not invisible); 0 for a faded remote bird; <c>max(op, floor)</c> for the faded local bird.</summary>
        public static float AlphaFor(float visualOpacity, bool isLocal)
        {
            if (!ControlStateVFX.IsFadedOpacity(visualOpacity)) return 1f;
            return isLocal ? Mathf.Max(visualOpacity, FeedbackTuning.LocalHiddenOpacityFloor) : 0f;
        }

        /// <summary><see cref="AlphaFor(float, bool)"/> read off a live chicken. A null or not-yet-spawned
        /// controller is treated as not faded (1).</summary>
        public static float AlphaFor(ChickenController controller)
        {
            if (controller == null) return 1f;
            var obj = controller.Object;
            if (obj == null || !obj.IsValid) return 1f;
            return AlphaFor(controller.VisualOpacity, controller.HasInputAuthority);
        }

        /// <summary>True when this peer must draw none of the bird's tells.</summary>
        public static bool IsHiddenFor(ChickenController controller) => AlphaFor(controller) <= 0f;
    }
}

using UnityEngine;
using UnityEngine.SceneManagement;

namespace CluckWars.Visuals
{
    /// <summary>Which full-screen pass the mark mood needs this frame.</summary>
    public enum MarkScreenPass : byte
    {
        /// <summary>All weights ~0: nothing enqueued, zero GPU cost.</summary>
        None = 0,
        /// <summary>Dim and / or red only: one blended full-screen triangle, no colour read.</summary>
        Overlay = 1,
        /// <summary>Cold &gt; 0: copy the scene colour, then desaturate + dim + red in one material blit.</summary>
        Graded = 2,
    }

    /// <summary>
    /// The Death Mark victim's full-screen mood: DIM (vignette darkness), COLD (violet darkness + desaturation),
    /// RED (a ~7% red tint inside the darkness). This class only holds the caller's already-faded weights;
    /// <see cref="MarkScreenFeature"/> (on PC_Renderer and Mobile_Renderer) reads them every frame and draws
    /// <c>CWMarkMood.shader</c> on the base game camera before post-processing, so every HUD layer stays on top.
    /// </summary>
    /// <remarks>
    /// <para>Pure state: no scene object, no reference to anything a scene owns, so a scene unload cannot leave it
    /// dangling. <see cref="Set"/> is allocation-free and safe every frame. Rendering happens only in Play mode
    /// (or while <see cref="RenderOutsidePlayMode"/> is set by a capture tool).</para>
    /// <para>Safety nets: statics reset on SubsystemRegistration (domain reload off), and any scene unload clears the
    /// weights; a caller that keeps calling <see cref="Set"/> every frame simply re-applies them next frame.</para>
    /// <para>Tuning: the Death Mark screen mood block in <see cref="FeedbackTuning"/>.</para>
    /// </remarks>
    public static class MarkScreenEffect
    {
        private static Vector3 _current;

        /// <summary>Last applied weights (x = dim01, y = cold01, z = red01), clamped to [0, 1].</summary>
        public static Vector3 Current => _current;

        /// <summary>The pass the current weights need.</summary>
        public static MarkScreenPass ActivePass => PassFor(_current);

        /// <summary>
        /// Lets an Editor capture tool render the effect outside Play mode. Reset on SubsystemRegistration; never set it
        /// from gameplay code.
        /// </summary>
        public static bool RenderOutsidePlayMode { get; set; }

        /// <summary>True when the feature may draw this frame at all (Play mode or a capture tool's preview).</summary>
        public static bool RenderingAllowed => Application.isPlaying || RenderOutsidePlayMode;

        /// <summary>dim01, cold01, red01 in [0,1], already faded by the caller. All three ~0 =&gt; fully off at zero per-frame GPU cost.</summary>
        public static void Set(float dim01, float cold01, float red01)
        {
            _current = new Vector3(Clamp01(dim01), Clamp01(cold01), Clamp01(red01));
        }

        /// <summary>Immediate off (scene unload, match end safety net).</summary>
        public static void Clear()
        {
            _current = Vector3.zero;
        }

        /// <summary>Pass selection, pure: None when every weight is under <see cref="FeedbackTuning.MarkMoodOffEpsilon"/>,
        /// Graded when cold is above it, Overlay otherwise.</summary>
        public static MarkScreenPass PassFor(Vector3 weights)
        {
            float eps = FeedbackTuning.MarkMoodOffEpsilon;
            if (weights.x < eps && weights.y < eps && weights.z < eps) return MarkScreenPass.None;
            return weights.y >= eps ? MarkScreenPass.Graded : MarkScreenPass.Overlay;
        }

        /// <summary>
        /// The shader uniforms for a set of weights, pure (the EditMode tests mirror the shader with these).
        /// dark.rgb = darkness colour; shape = (centre alpha, edge alpha, vignette inner, 1 / (outer - inner));
        /// red = (red colour rgb, red edge alpha); grade = (desaturation, red centre share, 0, 0).
        /// </summary>
        public static void Uniforms(Vector3 weights, out Vector4 dark, out Vector4 shape, out Vector4 red, out Vector4 grade)
        {
            float dim = Clamp01(weights.x), cold = Clamp01(weights.y), redW = Clamp01(weights.z);

            Color darkColour = Color.Lerp(FeedbackTuning.MarkMoodDimColour, FeedbackTuning.MarkMoodColdColour, cold);
            dark = new Vector4(darkColour.r, darkColour.g, darkColour.b, 1f);

            float centre = Mathf.Clamp01(dim * FeedbackTuning.MarkMoodDimCentre);
            float edge = Mathf.Clamp01(dim * (FeedbackTuning.MarkMoodDimEdge + cold * FeedbackTuning.MarkMoodColdEdgeBoost));
            float span = Mathf.Max(1e-4f, FeedbackTuning.MarkMoodVignetteOuter - FeedbackTuning.MarkMoodVignetteInner);
            shape = new Vector4(centre, edge, FeedbackTuning.MarkMoodVignetteInner, 1f / span);

            Color r = FeedbackTuning.MarkMoodRedColour;
            red = new Vector4(r.r, r.g, r.b, Mathf.Clamp01(redW * FeedbackTuning.MarkMoodRedAmount));

            grade = new Vector4(Mathf.Clamp01(cold * FeedbackTuning.MarkMoodColdDesaturation),
                FeedbackTuning.MarkMoodRedCentreShare, 0f, 0f);
        }

        // NaN-safe: NaN (a bad fade) reads as off, never as a stuck full-screen darkness.
        private static float Clamp01(float v) => v > 0f ? (v < 1f ? v : 1f) : 0f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _current = Vector3.zero;
            RenderOutsidePlayMode = false;
            SceneManager.sceneUnloaded -= OnSceneUnloaded;
            SceneManager.sceneUnloaded += OnSceneUnloaded;
        }

        private static void OnSceneUnloaded(Scene _) => Clear();
    }
}

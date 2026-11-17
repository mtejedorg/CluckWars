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
    /// The Death Mark's full-screen mood. The victim's: DIM (vignette darkness), COLD (violet darkness + desaturation),
    /// RED (a ~7% red tint inside the darkness). The hunter's (<see cref="SetHunt"/>): an edge-only mood, a dark edge and a
    /// red or yellow glow, centre untouched, never desaturated. One role is shown at a time; if both were ever set the
    /// victim's wins. This class only holds the caller's already-faded weights;
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
        private static float _huntEdge, _huntGlow;
        private static Color _huntColour;

        /// <summary>Last applied weights (x = dim01, y = cold01, z = red01), clamped to [0, 1].</summary>
        public static Vector3 Current => _current;

        /// <summary>The pass the current weights need.</summary>
        public static MarkScreenPass ActivePass => PassFor(_current, _huntEdge, _huntGlow);

        /// <summary>Last applied hunt mood: the dark edge weight, the glow alpha and the glow colour.</summary>
        public static (float Edge, float Glow, Color Colour) HuntCurrent => (_huntEdge, _huntGlow, _huntColour);

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

        /// <summary>The hunter's edge mood: <paramref name="edge01"/> the dark edge's weight, <paramref name="glow01"/> the glow's
        /// alpha at the corners (already faded by the caller), <paramref name="glowColour"/> its colour. Clamped like <see cref="Set"/>.</summary>
        public static void SetHunt(float edge01, Color glowColour, float glow01)
        {
            _huntEdge = Clamp01(edge01);
            _huntGlow = Clamp01(glow01);
            _huntColour = glowColour;
        }

        /// <summary>Immediate off, both moods (scene unload, match end safety net).</summary>
        public static void Clear()
        {
            _current = Vector3.zero;
            _huntEdge = _huntGlow = 0f;
            _huntColour = Color.clear;
        }

        /// <summary>Pass selection, pure: None when every weight is under <see cref="FeedbackTuning.MarkMoodOffEpsilon"/>,
        /// Graded when cold is above it, Overlay otherwise.</summary>
        public static MarkScreenPass PassFor(Vector3 weights) => PassFor(weights, 0f, 0f);

        /// <summary>Pass selection with the hunt mood: None only when the victim weights AND the hunt weights are all under
        /// <see cref="FeedbackTuning.MarkMoodOffEpsilon"/>; the hunt mood alone is always the blend-only Overlay, never Graded.</summary>
        public static MarkScreenPass PassFor(Vector3 weights, float huntEdge, float huntGlow)
        {
            float eps = FeedbackTuning.MarkMoodOffEpsilon;
            if (weights.x < eps && weights.y < eps && weights.z < eps)
                return huntEdge < eps && huntGlow < eps ? MarkScreenPass.None : MarkScreenPass.Overlay;
            return weights.y >= eps ? MarkScreenPass.Graded : MarkScreenPass.Overlay;
        }

        /// <summary>
        /// The shader uniforms for a set of weights, pure (the EditMode tests mirror the shader with these).
        /// dark.rgb = darkness colour; shape = (centre alpha, edge alpha, vignette inner, 1 / (outer - inner));
        /// red = (red colour rgb, red edge alpha); grade = (desaturation, red centre share, 0, 0).
        /// </summary>
        public static void Uniforms(Vector3 weights, out Vector4 dark, out Vector4 shape, out Vector4 red, out Vector4 grade) =>
            Uniforms(weights, 0f, Color.clear, 0f, out dark, out shape, out red, out grade);

        /// <summary>
        /// The shader uniforms for both moods. When every victim weight is under <see cref="FeedbackTuning.MarkMoodOffEpsilon"/>
        /// and the hunt mood is not, the same four vectors carry the hunter's edge: dark = the DIM colour, shape = (0, edge x
        /// <see cref="FeedbackTuning.MarkHuntMoodEdge"/>, hunt inner, 1 / (outer - inner)), the tint slot = (glow colour, glow alpha),
        /// grade = (0, glow centre share). The victim's mood wins if both are set.
        /// </summary>
        public static void Uniforms(Vector3 weights, float huntEdge, Color huntColour, float huntGlow,
            out Vector4 dark, out Vector4 shape, out Vector4 red, out Vector4 grade)
        {
            float eps = FeedbackTuning.MarkMoodOffEpsilon;
            bool victimOff = weights.x < eps && weights.y < eps && weights.z < eps;
            if (victimOff && (huntEdge >= eps || huntGlow >= eps))
            {
                var dim = FeedbackTuning.MarkMoodDimColour;
                dark = new Vector4(dim.r, dim.g, dim.b, 1f);
                float huntSpan = Mathf.Max(1e-4f, FeedbackTuning.MarkMoodVignetteOuter - FeedbackTuning.MarkHuntMoodVignetteInner);
                shape = new Vector4(0f, Clamp01(huntEdge) * FeedbackTuning.MarkHuntMoodEdge, FeedbackTuning.MarkHuntMoodVignetteInner, 1f / huntSpan);
                red = new Vector4(huntColour.r, huntColour.g, huntColour.b, Clamp01(huntGlow));
                grade = new Vector4(0f, FeedbackTuning.MarkHuntMoodGlowCentreShare, 0f, 0f);
                return;
            }
            VictimUniforms(weights, out dark, out shape, out red, out grade);
        }

        private static void VictimUniforms(Vector3 weights, out Vector4 dark, out Vector4 shape, out Vector4 red, out Vector4 grade)
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

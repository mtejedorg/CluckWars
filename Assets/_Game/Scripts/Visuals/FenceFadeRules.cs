using System.Collections.Generic;
using UnityEngine;

namespace CluckWars.Visuals
{
    /// <summary>
    /// Pure rules for the see-through fence near the local bird (Phase 6 chunk 7f, round-3 finding 9):
    /// at "3", GO! and in play the near fence could cover the player's own chicken down to its comb.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The fade itself is a dithered alpha-clip in <c>Assets/_Game/Art/Shaders/CWFenceFade.hlsl</c>, used by
    /// the fence material's shader (<see cref="ShaderName"/>). <see cref="FragmentFade"/> is the C# twin of
    /// <c>CW_FenceFadeAmount</c> so the shape of the hole is pinned by EditMode tests; keep the two identical.
    /// </para>
    /// <para>
    /// <b>Two gates, on purpose.</b> The CPU decides WHETHER the bird is behind a fence piece
    /// (<see cref="IsOccluded"/>, a few rays from the bird toward the camera against the fence renderers'
    /// bounds) and eases a strength 0..1 over <see cref="FadeSeconds"/>; the shader decides WHERE: only fence
    /// fragments within a screen-space circle around the bird AND closer to the camera than the bird fade, so
    /// the far fence and anything behind the bird stay solid.
    /// </para>
    /// <para>Local presentation only — nothing here is networked.</para>
    /// </remarks>
    public static class FenceFadeRules
    {
        /// <summary>The fence / wall shader every fence renderer and every base coop in <c>Map.unity</c> must use (the
        /// coop sits against the spawn point, so at GET READY it is what covered the bird).</summary>
        public const string ShaderName = "Cluck Wars/Fence Fade Lit";

        /// <summary>Fade in / out time when the bird goes behind or comes out from a fence (spec: ~0.15 s).</summary>
        public const float FadeSeconds = 0.15f;

        /// <summary>Hole radius where the fade is full, as a fraction of the screen height. At the match
        /// camera's ortho size 5.6 (11.2 m of view height) 0.09 is ~1 m: the whole bird.</summary>
        public const float InnerRadius = 0.09f;

        /// <summary>Where the fade has ramped back to solid, as a fraction of the screen height (~1.7 m).</summary>
        public const float OuterRadius = 0.15f;

        /// <summary>Peak fade. Below 1 leaves a faint screen-door of fence so the player still sees there is a
        /// wall between them and the open arena (it still blocks movement).</summary>
        public const float MaxFade = 0.85f;

        /// <summary>World units in front of the bird over which the depth gate ramps in. Keeps the edge of the
        /// hole soft when the bird hugs the fence instead of popping on the fence's own plane.</summary>
        public const float DepthSoftness = 0.35f;

        /// <summary>Body height assumed when the chicken has no collider to measure (metres).</summary>
        public const float FallbackBodyHeight = 1f;

        /// <summary>Fractions of the body height (feet = 0, top = 1) tested for occlusion: feet, body, comb.
        /// The bird counts as "behind the fence" as soon as any of them is covered — the reported case showed
        /// only the comb.</summary>
        public static readonly float[] OcclusionSampleFractions = { 0.05f, 0.5f, 0.95f };

        /// <summary>
        /// Eases the global fade strength toward 1 while the bird is covered and toward 0 when it is not,
        /// at a constant rate that crosses the whole range in <see cref="FadeSeconds"/>.
        /// </summary>
        public static float StepStrength(float current, bool occluded, float deltaSeconds)
        {
            float rate = FadeSeconds > 0f ? Mathf.Max(0f, deltaSeconds) / FadeSeconds : 1f;
            return Mathf.MoveTowards(Mathf.Clamp01(current), occluded ? 1f : 0f, rate);
        }

        /// <summary>
        /// True when a ray from any sample point on the bird toward the camera hits one of
        /// <paramref name="occluders"/> before reaching the camera.
        /// </summary>
        /// <param name="feet">Where the chicken touches the ground, world space.</param>
        /// <param name="bodyHeight">The chicken's height (its capsule's), metres.</param>
        /// <param name="towardCamera">Unit direction from the bird toward the camera (for the orthographic
        /// match camera, minus the camera's forward).</param>
        /// <param name="maxDistance">How far the ray may travel — the camera's distance.</param>
        /// <param name="occluders">World bounds of the fence renderers.</param>
        public static bool IsOccluded(Vector3 feet, float bodyHeight, Vector3 towardCamera, float maxDistance,
            IReadOnlyList<Bounds> occluders)
        {
            if (occluders == null || occluders.Count == 0) return false;
            if (towardCamera.sqrMagnitude < 1e-8f) return false;
            var dir = towardCamera.normalized;

            for (int h = 0; h < OcclusionSampleFractions.Length; h++)
            {
                var ray = new Ray(feet + Vector3.up * (OcclusionSampleFractions[h] * bodyHeight), dir);
                for (int i = 0; i < occluders.Count; i++)
                {
                    // IntersectRay reports 0 for an origin inside the box; a bird cannot stand inside a fence
                    // (it is solid), so counting that as covered is the right answer for a rotated wall's
                    // loose AABB too — the shader's depth gate still only fades what is in front.
                    if (occluders[i].IntersectRay(ray, out float d) && d <= maxDistance) return true;
                }
            }
            return false;
        }

        /// <summary>
        /// C# twin of <c>CW_FenceFadeAmount</c>: 0 keeps the fence fragment, 1 removes it. Distances on screen
        /// are fractions of the screen height; depths are linear eye depths (distance along the view axis).
        /// </summary>
        public static float FragmentFade(float strength, float screenDist, float fragmentEyeDepth, float birdEyeDepth)
        {
            float radial = 1f - Smoothstep(InnerRadius, OuterRadius, screenDist);
            float inFront = Smoothstep(0f, Mathf.Max(DepthSoftness, 1e-4f), birdEyeDepth - fragmentEyeDepth);
            return Mathf.Clamp01(strength) * MaxFade * radial * inFront;
        }

        /// <summary>The shader global <c>_CW_FenceFadeParams</c>.</summary>
        public static Vector4 ShaderParams => new Vector4(InnerRadius, OuterRadius, MaxFade, DepthSoftness);

        /// <summary>HLSL <c>smoothstep</c> (Unity's <c>Mathf.SmoothStep</c> is an interpolator, not this).</summary>
        public static float Smoothstep(float edge0, float edge1, float x)
        {
            float t = Mathf.Clamp01((x - edge0) / (edge1 - edge0));
            return t * t * (3f - 2f * t);
        }
    }
}

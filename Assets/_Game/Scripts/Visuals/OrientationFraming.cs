using UnityEngine;

namespace CluckWars.Visuals
{
    /// <summary>
    /// The match camera's framing per screen shape (PORTRAIT_AND_LANDSCAPE_SPEC §2, DECISIONS_2026-10-21 C1/C2). Pure
    /// maths: <see cref="MatchCamera"/> calls <see cref="Compute"/> every frame with the live camera aspect.
    /// </summary>
    /// <remarks>
    /// <para><b>Landscape (a ≥ 1)</b> is exactly today's camera: the authored ortho size (5.6), the authored near clip
    /// (−6) and no focus offset, at every aspect. Maestro chose this over the equal-area zoom for wide phones (C2:
    /// "Ensure it looks nice on all devices; area of vision is not as important as the game being fun and playable").</para>
    /// <para><b>Portrait (a &lt; 1)</b> keeps the ground's lateral half-width at <see cref="PortraitLateralHalfWidth"/>
    /// (the longest ability reach, 5.4 m, plus 0.2, so every in-range target is on screen at the landscape pixel
    /// density): s = min(5.6 / a, <see cref="PortraitMaxOrthoSize"/>). The chicken sits at viewport height
    /// fy = 0.5 + (B − T)/2 so the clear band between the HUD bands is centred on it (eased in over the
    /// <see cref="FocusBlendAspectBand"/> just below square, so the follow point is continuous at a = 1), and the near clip is pulled back
    /// so the surround trees poking up from below the bottom edge are never sliced: min(landscape near,
    /// distance − s/tan(pitch) − h/sin(pitch)), h = <see cref="TallestSceneryAboveFocus"/>.</para>
    /// <para><b>Why h/sin(pitch), not the spec's "− 6.2".</b> A point h above the ground sits h·sin(pitch) CLOSER to the
    /// camera and h·cos(pitch) higher on screen, so the tree whose top just reaches the bottom edge stands
    /// h·cos²/sin further back as well: its top is at view depth distance − s/tan − h/sin (−11.0 m at 30°, not −6.2).
    /// This is the same inequality Phase8ChunkA6Tests.NearPlane_DoesNotSliceTallPropsAtTheBottomEdge asserts for
    /// landscape; the spec's formula would leave 3.1 m of clearance in portrait where that guard asks for 5.5. At a = 1
    /// both branches give −6, so the rule is continuous.</para>
    /// <para>Geometry (orthographic, pitched): ground lateral half-extent L = s·a, ground depth half-extent
    /// D = s/sin(pitch) (2s at 30°), visible ground G = 4·L·D, and a viewport offset of Δy maps to Δy·2s/sin(pitch)
    /// metres of ground.</para>
    /// </remarks>
    public static class OrientationFraming
    {
        /// <summary>Portrait ground lateral half-width, metres: longest reach (StealRange / AuraRadius 5.4) + 0.2.</summary>
        public const float PortraitLateralHalfWidth = 5.6f;

        /// <summary>Portrait ortho-size cap (9:21 and taller show slightly less than 5.6 m to each side).</summary>
        public const float PortraitMaxOrthoSize = 12.5f;

        /// <summary>Top HUD band as a fraction of a portrait screen's height (top bar ≈ 268 px + safe top on 9:20).</summary>
        public const float PortraitTopBandFraction = 0.15f;

        /// <summary>Bottom HUD band as a fraction of a portrait screen's height (stick + hex arc ≈ 531 px on 9:20).</summary>
        public const float PortraitBottomBandFraction = 0.25f;

        /// <summary>
        /// Height above the camera focus (the chicken pivot) of the tallest scenery that can stand at the bottom screen
        /// edge: surround trees reach ~6.2 m, the pivot is ~0.9 m up. Same figure as Phase8ChunkA6Tests' near-plane guard.
        /// </summary>
        public const float TallestSceneryAboveFocus = 5.5f;

        public readonly struct Result
        {
            public readonly bool Portrait;
            public readonly float OrthoSize;
            public readonly float NearClip;
            /// <summary>Viewport height (0 bottom, 1 top) at which the followed chicken is drawn.</summary>
            public readonly float FocusViewportY;
            /// <summary>Metres the look-at point sits BEHIND the chicken along the camera's ground-forward, so the chicken
            /// is drawn at <see cref="FocusViewportY"/>. Zero in landscape.</summary>
            public readonly float FocusBackOffset;
            public readonly float LateralHalfExtent, DepthHalfExtent;
            public float GroundArea => 4f * LateralHalfExtent * DepthHalfExtent;

            public Result(bool portrait, float orthoSize, float nearClip, float focusViewportY, float focusBackOffset,
                float lateral, float depth)
            {
                Portrait = portrait; OrthoSize = orthoSize; NearClip = nearClip;
                FocusViewportY = focusViewportY; FocusBackOffset = focusBackOffset;
                LateralHalfExtent = lateral; DepthHalfExtent = depth;
            }
        }

        /// <summary>Viewport height of the chicken in portrait: the centre of the clear band between the HUD bands.</summary>
        public static float PortraitFocusViewportY => 0.5f + (PortraitBottomBandFraction - PortraitTopBandFraction) / 2f;

        /// <summary>
        /// Width of the aspect band just below square (1 − band .. 1) over which the focus offset eases in, so a Game View
        /// or a foldable passing through square does not jump the camera ~1.1 m. Every phone portrait (a ≤ 0.75) and
        /// every landscape aspect is outside it.
        /// </summary>
        public const float FocusBlendAspectBand = 0.1f;

        /// <summary>The chicken's viewport height at portrait <paramref name="aspect"/>: 0.5 at square, easing linearly to
        /// <see cref="PortraitFocusViewportY"/> by 1 − <see cref="FocusBlendAspectBand"/>.</summary>
        public static float PortraitFocusViewportYAt(float aspect)
        {
            float t = Mathf.Clamp01((1f - aspect) / FocusBlendAspectBand);
            return 0.5f + t * (PortraitFocusViewportY - 0.5f);
        }

        /// <summary>Aspect used when the framing is applied outside Play Mode (MatchCamera.OnValidate): always the
        /// landscape branch, so an Editor Game View left in portrait never writes portrait values into Game.unity.</summary>
        public const float EditTimeAspect = 16f / 9f;

        /// <summary>The aspect the framing should be computed for: the live camera aspect while playing, else
        /// <see cref="EditTimeAspect"/>.</summary>
        public static float FramingAspect(bool isPlaying, float cameraAspect) => isPlaying ? cameraAspect : EditTimeAspect;

        /// <summary>
        /// Framing for a camera of <paramref name="aspect"/> (W/H). <paramref name="landscapeOrthoSize"/> and
        /// <paramref name="landscapeNearClip"/> are the authored values (MatchCamera._orthoSize / _nearClipPlane), returned
        /// unchanged for every landscape aspect. A non-positive or NaN aspect is treated as 16:9.
        /// </summary>
        public static Result Compute(float aspect, float landscapeOrthoSize, float landscapeNearClip,
            float distance, float pitchDegrees)
        {
            if (!(aspect > 0f)) aspect = 16f / 9f;
            float pitch = Mathf.Clamp(pitchDegrees, 1f, 89f) * Mathf.Deg2Rad;
            float sinP = Mathf.Sin(pitch);

            if (aspect >= 1f)
            {
                float s = landscapeOrthoSize;
                return new Result(false, s, landscapeNearClip, 0.5f, 0f, s * aspect, s / sinP);
            }

            float ps = Mathf.Min(PortraitLateralHalfWidth / aspect, PortraitMaxOrthoSize);
            float near = Mathf.Min(landscapeNearClip, distance - ps / Mathf.Tan(pitch) - TallestSceneryAboveFocus / sinP);
            float fy = PortraitFocusViewportYAt(aspect);
            float back = (fy - 0.5f) * 2f * ps / sinP;
            return new Result(true, ps, near, fy, back, ps * aspect, ps / sinP);
        }
    }
}

#ifndef CW_FENCE_FADE_INCLUDED
#define CW_FENCE_FADE_INCLUDED

// Cluck Wars — see-through fence near the LOCAL bird (Phase 6 chunk 7f; rim + keyword Phase 8 A10).
//
// A dithered alpha-clip, not transparency: the fence stays in the opaque queue, keeps writing depth,
// costs no extra pass and needs no sorting. Every pass that writes colour or depth (ForwardLit, GBuffer,
// DepthOnly, DepthNormals) calls CW_FenceFadeClip with its SV_Position, so a depth prepass / depth-priming
// renderer can never keep a depth value for a fragment the colour pass threw away (that would hide the bird
// behind an invisible wall). The shadow caster does NOT clip: the fence's shadow is unchanged.
//
// Compiled only in the _CW_FENCE_FADE variant (a GLOBAL keyword: the inputs are globals, and a keyword cannot
// ride in a MaterialPropertyBlock). FenceFadeDriver enables it while the strength is above 0 and disables it
// when the strength returns to 0, so the fence renders the plain URP Lit path whenever no hole is open.
//
// Inputs are globals set by CluckWars.Visuals.FenceFadeDriver (local presentation only):
//   _CW_LocalBirdPos     xyz = the local bird's body centre (world), w = strength 0..1 (time-smoothed)
//   _CW_FenceFadeParams  x = inner radius, y = outer radius (both as a fraction of the screen HEIGHT; the
//                        dithered edge between them is FenceFadeRules.EdgeBandWidth, 0.03 since Phase 8 C16),
//                        z = max fade 0..1 (below 1 leaves a faint screen-door so the fence still reads),
//                        w = depth softness in world units (how far in front of the bird the fade ramps in)
//   _CW_FenceRimParams   x = rim centre radius, y = rim half width (fractions of the screen height),
//                        z = rim blend 0..1 toward _CW_FenceRimColor (ForwardLit only)
//   _CW_FenceRimColor    rgb = rim colour (the UI cream)
//
// Keep the math identical to FenceFadeRules.FragmentFade / RimWeight (EditMode-tested on the C# side).

float4 _CW_LocalBirdPos;
float4 _CW_FenceFadeParams;
float4 _CW_FenceRimParams;
float4 _CW_FenceRimColor;

#if defined(_CW_FENCE_FADE)

#include "CWDither.hlsl" // CW_Bayer4 (shared with the chicken ghost fade)

// Linear eye depth of an SV_Position.z, for both the orthographic match camera and any perspective one.
float CW_FragmentEyeDepth(float rawDepth)
{
    return IsPerspectiveProjection() ? LinearEyeDepth(rawDepth, _ZBufferParams) : LinearDepthToEyeDepth(rawDepth);
}

// Only fragments CLOSER to the camera than the bird fade (or get a rim); the far fence stays untouched.
float CW_FenceInFront(float fragEyeDepth, float birdEyeDepth)
{
    return smoothstep(0.0, max(_CW_FenceFadeParams.w, 1e-4), birdEyeDepth - fragEyeDepth);
}

// 0 = keep, 1 = fully faded. screenDist and the radii are fractions of the screen height.
float CW_FenceFadeAmount(float screenDist, float inFront)
{
    float radial = 1.0 - smoothstep(_CW_FenceFadeParams.x, _CW_FenceFadeParams.y, screenDist);
    return saturate(_CW_LocalBirdPos.w) * _CW_FenceFadeParams.z * radial * inFront;
}

// 0..1 weight of the thin rim band around the hole's outer edge.
float CW_FenceRimAmount(float screenDist, float inFront)
{
    float band = 1.0 - smoothstep(0.0, max(_CW_FenceRimParams.y, 1e-5), abs(screenDist - _CW_FenceRimParams.x));
    return saturate(_CW_LocalBirdPos.w) * _CW_FenceRimParams.z * band * inFront;
}

// Clips the hole and returns the rim weight for the surviving fragment (0 when strength is 0).
// One exit point on purpose (Phase 8 C16): an early `return` inside the UNITY_BRANCH ahead of the clip() made
// FXC/HLSLcc warn "use of potentially uninitialized variable" (the multiple returns are merged into a hidden
// return temp the compiler cannot prove is set on every path). Same behaviour: strength <= 0 skips all the
// work and returns 0.
float CW_FenceFadeClipRim(float4 positionCS)
{
    float rim = 0.0;

    UNITY_BRANCH
    if (_CW_LocalBirdPos.w > 0.0)
    {
        // Where the bird lands on screen. ComputeScreenPos-style mapping paired with GetNormalizedScreenSpaceUV is
        // the same pairing URP uses for its own screen-space textures, so both sides agree on any y-flip.
        float4 birdCS = TransformWorldToHClip(_CW_LocalBirdPos.xyz);
        float2 birdUV = (birdCS.xy * float2(0.5, 0.5 * _ProjectionParams.x) + 0.5 * birdCS.w) / max(birdCS.w, 1e-5);
        float2 fragUV = GetNormalizedScreenSpaceUV(positionCS);

        float2 screenPx = GetScaledScreenParams().xy;
        float screenDist = length((fragUV - birdUV) * screenPx) / max(screenPx.y, 1.0);

        float birdEye = -TransformWorldToView(_CW_LocalBirdPos.xyz).z;
        float fragEye = CW_FragmentEyeDepth(positionCS.z);
        float inFront = CW_FenceInFront(fragEye, birdEye);

        float fade = CW_FenceFadeAmount(screenDist, inFront);
        clip(CW_Bayer4(positionCS.xy) - fade - 1e-4);
        rim = CW_FenceRimAmount(screenDist, inFront);
    }

    return rim;
}

void CW_FenceFadeClip(float4 positionCS)
{
    CW_FenceFadeClipRim(positionCS);
}

// Tints a ForwardLit colour toward the rim colour by the rim weight.
half3 CW_FenceApplyRim(half3 color, float rim)
{
    return lerp(color, (half3)_CW_FenceRimColor.rgb, (half)saturate(rim));
}

#else // !_CW_FENCE_FADE: the plain URP Lit path, no clip, no rim.

float CW_FenceFadeClipRim(float4 positionCS) { return 0.0; }
void CW_FenceFadeClip(float4 positionCS) { }
half3 CW_FenceApplyRim(half3 color, float rim) { return color; }

#endif // _CW_FENCE_FADE

#endif // CW_FENCE_FADE_INCLUDED

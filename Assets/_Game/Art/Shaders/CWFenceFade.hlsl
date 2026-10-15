#ifndef CW_FENCE_FADE_INCLUDED
#define CW_FENCE_FADE_INCLUDED

// Cluck Wars — see-through fence near the LOCAL bird (Phase 6 chunk 7f; rim + keyword Phase 8 A10) and,
// on outer fences, near every other chicken behind them (Phase 8 D4).
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
// Phase 8 D4 — a hole for every chicken behind an OUTER fence (Maestro: "all OUTER fences fade when a chicken is
// behind them"). Only materials with the local keyword _CW_FENCE_ALL_BIRDS (Prop_wall_segment.mat: the boundary
// runs) read these; the inner arm walls (Prop_wall_segment_Inner.mat) and the coops keep the local-bird-only hole:
//   _CW_FenceBirds[CW_FENCE_MAX_BIRDS]  xyz = a chicken's body centre (world), w = its strength 0..1 (smoothed,
//                        already scaled by its networked VisualOpacity); rivals and decoys alike, compacted
//   _CW_FenceBirdCount   how many leading slots are live (0..CW_FENCE_MAX_BIRDS)
// Every hole has the local hole's exact shape, depth gate and rim; overlapping holes take the max.
//
// Keep the math identical to FenceFadeRules.FragmentFade / RimWeight / CombinedFragmentFade (EditMode-tested on the
// C# side), and CW_FENCE_MAX_BIRDS equal to FenceFadeRules.MaxBirdSlots.

#define CW_FENCE_MAX_BIRDS 8

float4 _CW_LocalBirdPos;
float4 _CW_FenceFadeParams;
float4 _CW_FenceRimParams;
float4 _CW_FenceRimColor;
float4 _CW_FenceBirds[CW_FENCE_MAX_BIRDS];
float _CW_FenceBirdCount;

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
float CW_FenceFadeAmount(float screenDist, float inFront, float strength)
{
    float radial = 1.0 - smoothstep(_CW_FenceFadeParams.x, _CW_FenceFadeParams.y, screenDist);
    return saturate(strength) * _CW_FenceFadeParams.z * radial * inFront;
}

// 0..1 weight of the thin rim band around the hole's outer edge.
float CW_FenceRimAmount(float screenDist, float inFront, float strength)
{
    float band = 1.0 - smoothstep(0.0, max(_CW_FenceRimParams.y, 1e-5), abs(screenDist - _CW_FenceRimParams.x));
    return saturate(strength) * _CW_FenceRimParams.z * band * inFront;
}

// One hole (bird: xyz world centre, w strength) as this fragment sees it; folds into fade / rim by max.
void CW_FenceHole(float4 bird, float2 fragUV, float2 screenPx, float fragEye, inout float fade, inout float rim)
{
    // Where the bird lands on screen. ComputeScreenPos-style mapping paired with GetNormalizedScreenSpaceUV is
    // the same pairing URP uses for its own screen-space textures, so both sides agree on any y-flip.
    float3 birdVS = TransformWorldToView(bird.xyz);
    float4 birdCS = TransformWViewToHClip(birdVS);
    float2 birdUV = (birdCS.xy * float2(0.5, 0.5 * _ProjectionParams.x) + 0.5 * birdCS.w) / max(birdCS.w, 1e-5);
    float screenDist = length((fragUV - birdUV) * screenPx) / max(screenPx.y, 1.0);

    float inFront = CW_FenceInFront(fragEye, -birdVS.z);
    fade = max(fade, CW_FenceFadeAmount(screenDist, inFront, bird.w));
    rim = max(rim, CW_FenceRimAmount(screenDist, inFront, bird.w));
}

// Clips the holes and returns the rim weight for the surviving fragment (0 when no hole is open).
// One exit point on purpose (Phase 8 C16): an early `return` inside the UNITY_BRANCH ahead of the clip() made
// FXC/HLSLcc warn "use of potentially uninitialized variable" (the multiple returns are merged into a hidden
// return temp the compiler cannot prove is set on every path). No hole open skips all the work and returns 0.
float CW_FenceFadeClipRim(float4 positionCS)
{
    float rim = 0.0;

    bool anyHole = _CW_LocalBirdPos.w > 0.0;
#if defined(_CW_FENCE_ALL_BIRDS)
    float birdCount = min(_CW_FenceBirdCount, (float)CW_FENCE_MAX_BIRDS);
    anyHole = anyHole || birdCount > 0.5;
#endif

    UNITY_BRANCH
    if (anyHole)
    {
        float2 fragUV = GetNormalizedScreenSpaceUV(positionCS);
        float2 screenPx = GetScaledScreenParams().xy;
        float fragEye = CW_FragmentEyeDepth(positionCS.z);
        float fade = 0.0;

        CW_FenceHole(_CW_LocalBirdPos, fragUV, screenPx, fragEye, fade, rim);

#if defined(_CW_FENCE_ALL_BIRDS)
        // Constant bound, unrolled, each slot behind a uniform branch: constant array indices (fine at shader
        // target 2.0 / GLES3), and slots past the live count cost one compare each.
        UNITY_UNROLL
        for (int i = 0; i < CW_FENCE_MAX_BIRDS; i++)
        {
            UNITY_BRANCH
            if ((float)i < birdCount)
                CW_FenceHole(_CW_FenceBirds[i], fragUV, screenPx, fragEye, fade, rim);
        }
#endif

        clip(CW_Bayer4(positionCS.xy) - fade - 1e-4);
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

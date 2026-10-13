#ifndef CW_FENCE_FADE_INCLUDED
#define CW_FENCE_FADE_INCLUDED

// Cluck Wars — see-through fence near the LOCAL bird (Phase 6 chunk 7f).
//
// A dithered alpha-clip, not transparency: the fence stays in the opaque queue, keeps writing depth,
// costs no extra pass and needs no sorting. Every pass that writes colour or depth (ForwardLit, GBuffer,
// DepthOnly, DepthNormals) calls CW_FenceFadeClip with its SV_Position, so a depth prepass / depth-priming
// renderer can never keep a depth value for a fragment the colour pass threw away (that would hide the bird
// behind an invisible wall). The shadow caster does NOT clip: the fence's shadow is unchanged.
//
// Inputs are globals set once per frame by CluckWars.Visuals.FenceFadeDriver (local presentation only):
//   _CW_LocalBirdPos     xyz = the local bird's body centre (world), w = strength 0..1 (time-smoothed)
//   _CW_FenceFadeParams  x = inner radius, y = outer radius (both as a fraction of the screen HEIGHT),
//                        z = max fade 0..1 (below 1 leaves a faint screen-door so the fence still reads),
//                        w = depth softness in world units (how far in front of the bird the fade ramps in)
// Strength 0 (menus, no local bird, bird in the open) is a uniform early-out.
//
// Keep the math identical to FenceFadeRules.FragmentFade (EditMode-tested on the C# side).

float4 _CW_LocalBirdPos;
float4 _CW_FenceFadeParams;

// 4x4 ordered-dither threshold in (0,1), from the fragment's pixel coordinate. Float-only (no integer ops or
// arrays) so it compiles the same on GLES3, Vulkan and D3D at the Lit passes' shader target.
float CW_Bayer2(float2 a)
{
    a = floor(a);
    return frac(a.x * 0.5 + a.y * a.y * 0.75);
}

float CW_Bayer4(float2 pixel)
{
    // Values {0..15}/16, offset half a step so a threshold never sits exactly on 0 or 1.
    return CW_Bayer2(0.5 * pixel) * 0.25 + CW_Bayer2(pixel) + 1.0 / 32.0;
}

// Linear eye depth of an SV_Position.z, for both the orthographic match camera and any perspective one.
float CW_FragmentEyeDepth(float rawDepth)
{
    return IsPerspectiveProjection() ? LinearEyeDepth(rawDepth, _ZBufferParams) : LinearDepthToEyeDepth(rawDepth);
}

// 0 = keep, 1 = fully faded. screenDist and the radii are fractions of the screen height.
float CW_FenceFadeAmount(float screenDist, float fragEyeDepth, float birdEyeDepth)
{
    float radial = 1.0 - smoothstep(_CW_FenceFadeParams.x, _CW_FenceFadeParams.y, screenDist);
    // Only fragments CLOSER to the camera than the bird fade; the far fence stays solid.
    float inFront = smoothstep(0.0, max(_CW_FenceFadeParams.w, 1e-4), birdEyeDepth - fragEyeDepth);
    return saturate(_CW_LocalBirdPos.w) * _CW_FenceFadeParams.z * radial * inFront;
}

void CW_FenceFadeClip(float4 positionCS)
{
    UNITY_BRANCH
    if (_CW_LocalBirdPos.w <= 0.0)
        return;

    // Where the bird lands on screen. ComputeScreenPos-style mapping paired with GetNormalizedScreenSpaceUV is
    // the same pairing URP uses for its own screen-space textures, so both sides agree on any y-flip.
    float4 birdCS = TransformWorldToHClip(_CW_LocalBirdPos.xyz);
    float2 birdUV = (birdCS.xy * float2(0.5, 0.5 * _ProjectionParams.x) + 0.5 * birdCS.w) / max(birdCS.w, 1e-5);
    float2 fragUV = GetNormalizedScreenSpaceUV(positionCS);

    float2 screenPx = GetScaledScreenParams().xy;
    float screenDist = length((fragUV - birdUV) * screenPx) / max(screenPx.y, 1.0);

    float birdEye = -TransformWorldToView(_CW_LocalBirdPos.xyz).z;
    float fragEye = CW_FragmentEyeDepth(positionCS.z);

    float fade = CW_FenceFadeAmount(screenDist, fragEye, birdEye);
    clip(CW_Bayer4(positionCS.xy) - fade - 1e-4);
}

#endif // CW_FENCE_FADE_INCLUDED

#ifndef CW_CHICKEN_GHOST_INCLUDED
#define CW_CHICKEN_GHOST_INCLUDED

// Cluck Wars — the chicken "ghost" fade (Phase 8 C2): Invisibility, Smoke Roost and the stunned ghost.
//
// A dithered alpha-clip in the OPAQUE queue, the same technique as the fence fade: no transparency, no sorting,
// depth stays correct. The amount kept is _BaseColor.a — the alpha ChickenVisuals already writes into the
// renderer's MaterialPropertyBlock (opacity 0.2 keeps ~3/16 of the pixels, 0.4 ~6/16). So no new per-frame
// writes: the MPB colour that tints the bird also drives its fade.
//
// Every pass that writes colour or depth (ForwardLit, GBuffer, DepthOnly, DepthNormals, ShadowCaster) calls
// CW_GhostClip with its SV_Position, so a clipped pixel leaves no depth / SSAO / shadow silhouette behind.
// ChickenVisuals swaps this material in only while opacity < 0.99 and turns shadow casting off below 0.5.
//
// Must be included AFTER LitInput.hlsl (for _BaseColor).

#include "CWDither.hlsl"

void CW_GhostClip(float4 positionCS)
{
    clip(_BaseColor.a - CW_Bayer4(positionCS.xy));
}

#endif // CW_CHICKEN_GHOST_INCLUDED

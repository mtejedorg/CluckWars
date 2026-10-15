#ifndef CW_DITHER_INCLUDED
#define CW_DITHER_INCLUDED

// Cluck Wars — shared 4x4 ordered (Bayer) dither threshold. Used by the fence fade (CWFenceFade.hlsl) and the chicken
// ghost fade (ChickenGhostLit.shader). A fragment survives when its threshold is below the amount you want kept, so
// "clip(keep - CW_Bayer4(positionCS.xy))" keeps about keep x 16 / 16 of the pixels in every 4x4 screen tile.

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

#endif // CW_DITHER_INCLUDED

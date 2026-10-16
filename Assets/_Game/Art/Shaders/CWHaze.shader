// Cluck Wars — PC / console surround haze (Phase 8 C F1). Full-screen triangle drawn by
// CluckWars.Visuals.CWHazeFeature (PC_Renderer only) after opaques, before transparents and post-processing.
// Reconstructs each pixel's world position from the camera depth texture (UNITY_MATRIX_I_VP: valid for the ORTHO
// match camera and for perspective capture cameras alike) and outputs (haze colour, amount) with alpha blending, so
// the colour buffer becomes lerp(colour, haze, amount) with no colour copy and no MSAA resolve.
// amount = min(max(d - rampStart, 0) * slope, max), d = max(|x - cx|, |z - cz|) — the same formula as
// HazeSettings.AmountAt (0 everywhere inside the fence). Background pixels (depth at the far plane) get the max.
// All constants are globals pushed by HazeSettings.PushShaderGlobals(); nothing is tuned in this file.
Shader "Cluck Wars/Haze"
{
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        ZWrite Off
        ZTest Always
        Cull Off
        // Colour: lerp toward the haze. Alpha: keep the destination's.
        Blend SrcAlpha OneMinusSrcAlpha, Zero One

        Pass
        {
            Name "CW Haze"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            // xyz = linear haze colour, w = max amount.
            float4 _CW_HazeColor;
            // xy = arena centre XZ, z = ramp start (square distance), w = max / (ramp end - ramp start).
            float4 _CW_HazeParams;

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 uv = input.texcoord;
                float rawDepth = SampleSceneDepth(uv);

                #if UNITY_REVERSED_Z
                    bool background = rawDepth <= 1e-6;
                    float deviceDepth = rawDepth;
                #else
                    bool background = rawDepth >= 1.0 - 1e-6;
                    // OpenGL-style clip space: map the [0, 1] depth texture value to [-1, 1] NDC.
                    float deviceDepth = lerp(UNITY_NEAR_CLIP_VALUE, 1.0, rawDepth);
                #endif

                float amount = _CW_HazeColor.w;
                if (!background)
                {
                    float3 worldPos = ComputeWorldSpacePosition(uv, deviceDepth, UNITY_MATRIX_I_VP);
                    float2 offset = abs(worldPos.xz - _CW_HazeParams.xy);
                    float squareDistance = max(offset.x, offset.y);
                    amount = min(max(squareDistance - _CW_HazeParams.z, 0.0) * _CW_HazeParams.w, _CW_HazeColor.w);
                }
                return half4(_CW_HazeColor.rgb, amount);
            }
            ENDHLSL
        }
    }
    Fallback Off
}

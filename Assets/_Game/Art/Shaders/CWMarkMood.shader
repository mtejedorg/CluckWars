// Cluck Wars — Death Mark victim's screen mood. Full-screen triangle drawn by CluckWars.Visuals.MarkScreenFeature
// (PC_Renderer and Mobile_Renderer) on the base game camera, before post-processing, so every HUD layer draws on top.
// Pass 0 "Overlay": blend only (no colour read). Premultiplied (dark vignette + faint red) over the scene; destination
//   alpha kept. Used whenever cold01 is ~0 — costs one blended full-screen triangle, merges into the native render pass.
// Pass 1 "Graded":  reads a copy of the scene colour (_BlitTexture), desaturates it by the COLD amount, then applies the
//   same layer. Opaque output. Used only while cold01 > 0.
// All values are uniforms set per frame by MarkScreenFeature from FeedbackTuning; nothing is tuned in this file.
Shader "Cluck Wars/Mark Mood"
{
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        ZWrite Off
        ZTest Always
        Cull Off

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

        // rgb = darkness colour (dim -> cold violet, already mixed by cold01 on the CPU).
        float4 _CW_MarkDark;
        // x = darkness alpha at the centre, y = at the corners, z = vignette inner radius, w = 1 / (outer - inner).
        float4 _CW_MarkShape;
        // rgb = red tint colour, w = red alpha at the corners (red01 * amount).
        float4 _CW_MarkRed;
        // x = desaturation amount (cold01 * desaturation), y = red centre share.
        float4 _CW_MarkGrade;

        // Premultiplied mood layer: rgb = colour * coverage, a = coverage. Darkness first, red composited over it.
        half4 MoodLayer(float2 uv)
        {
            // 0 at the centre, 1 at the corners (1.41421356 = 1 / length(0.5, 0.5)).
            float r = length(uv - 0.5) * 1.41421356;
            float t = saturate((r - _CW_MarkShape.z) * _CW_MarkShape.w);
            t = t * t * (3.0 - 2.0 * t);
            float darkA = lerp(_CW_MarkShape.x, _CW_MarkShape.y, t);
            float redA = _CW_MarkRed.w * lerp(_CW_MarkGrade.y, 1.0, t);
            float3 rgb = _CW_MarkDark.rgb * darkA * (1.0 - redA) + _CW_MarkRed.rgb * redA;
            float a = 1.0 - (1.0 - darkA) * (1.0 - redA);
            return half4(rgb, a);
        }
        ENDHLSL

        Pass
        {
            Name "CW Mark Mood Overlay"
            // Colour: premultiplied over. Alpha: keep the destination's.
            Blend One OneMinusSrcAlpha, Zero One

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragOverlay

            half4 FragOverlay(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                return MoodLayer(input.texcoord);
            }
            ENDHLSL
        }

        Pass
        {
            Name "CW Mark Mood Graded"
            Blend Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragGraded

            half4 FragGraded(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 uv = input.texcoord;
                half4 scene = SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_LinearClamp, uv, 0);
                // Gamma-space project: Rec.601 luma weights on the stored (sRGB) values.
                half luma = dot(scene.rgb, half3(0.299, 0.587, 0.114));
                scene.rgb = lerp(scene.rgb, luma.xxx, (half)_CW_MarkGrade.x);
                half4 layer = MoodLayer(uv);
                scene.rgb = scene.rgb * (1.0 - layer.a) + layer.rgb;
                return scene;
            }
            ENDHLSL
        }
    }
    Fallback Off
}

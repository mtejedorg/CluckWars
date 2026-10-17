// Cluck Wars — food-pile mound stencil mark (Phase 8 D8). Second material on the FoodPile MoundMesh renderer.
// Draws the mound again with ColorMask 0 / ZWrite Off / ZTest LEqual and sets stencil bit 3 (value 8, inside URP's
// user stencil bits 0-3) wherever the mound is the FRONT-MOST opaque surface. Queue Geometry+500 (2500, the last
// opaque queue) so it runs after every other opaque (chickens, fences, props at 2000-2450): where a chicken body or
// a fence is in front of the mound the depth test fails and the bit stays clear. The Ring Ghost shader reads the bit,
// so a foot ring hidden by the mound shows as a ghost there and nowhere else.
// No ShadowCaster / DepthOnly / DepthNormals pass: no shadow, no prepass cost, the colour buffer is untouched.
// One pass, no keywords (one variant per platform).
Shader "Cluck Wars/Mound Stencil Mark"
{
    Properties
    {
        _StencilRef("Stencil bit (mound mark)", Float) = 8
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry+500" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }

        Pass
        {
            Name "MoundStencilMark"
            Tags { "LightMode" = "SRPDefaultUnlit" }
            ColorMask 0
            ZWrite Off
            ZTest LEqual
            Cull Back
            Stencil
            {
                Ref [_StencilRef]
                WriteMask [_StencilRef]
                Comp Always
                Pass Replace
            }

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float _StencilRef;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; };

            Varyings Vert(Attributes input)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                return o;
            }

            half4 Frag(Varyings input) : SV_Target { return 0; }
            ENDHLSL
        }
    }
}

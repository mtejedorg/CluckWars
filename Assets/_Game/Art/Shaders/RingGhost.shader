// Cluck Wars — foot-ring ghost (Phase 8 D8). A SpriteRenderer child of each player-colour foot ring (IdentityRing,
// RivalRing; see GroundRingStyle) draws the same annulus sprite with this shader:
//   * ZTest Greater: only the ring pixels that something opaque is IN FRONT of (the part the normal ring loses);
//   * stencil Equal on bit 3 (value 8): only where that front-most surface is a food-pile mound (Mound Stencil Mark),
//     so the ghost never shows through chicken bodies, fences, props or the base;
//   * colour = the SpriteRenderer colour (the ring's player colour; alpha = ring alpha / its nominal alpha, synced
//     every frame by GroundRingStyle.SyncGhost) x _GhostAlpha, so a ring at its normal opacity has a 40 % ghost.
// Transparent queue, alpha blended, no depth write, unlit. One pass, no keywords (one variant per platform).
Shader "Cluck Wars/Ring Ghost"
{
    Properties
    {
        [PerRendererData] _MainTex("Sprite", 2D) = "white" {}
        _GhostAlpha("Ghost alpha (x ring alpha)", Range(0, 1)) = 0.4
        [Enum(UnityEngine.Rendering.CompareFunction)] _ZTest("ZTest", Float) = 5
        _StencilRef("Stencil bit (mound mark)", Float) = 8
    }
    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline"
               "IgnoreProjector" = "True" "PreviewType" = "Plane" "CanUseSpriteAtlas" = "True" }

        Pass
        {
            Name "RingGhost"
            Tags { "LightMode" = "SRPDefaultUnlit" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest [_ZTest]
            Cull Off
            Stencil
            {
                Ref [_StencilRef]
                ReadMask [_StencilRef]
                Comp Equal
                Pass Keep
            }

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                half _GhostAlpha;
                float _ZTest;
                float _StencilRef;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; half4 color : COLOR; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; half4 color : COLOR; float2 uv : TEXCOORD0; };

            Varyings Vert(Attributes input)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                // SpriteRenderer.color arrives in unity_SpriteColor (UnityPerDraw, Unity 6 SRP sprite path), not in the vertex colour.
                o.color = input.color * unity_SpriteColor;
                o.uv = input.uv;
                return o;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                half4 c = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv) * input.color;
                c.a *= _GhostAlpha;
                return c;
            }
            ENDHLSL
        }
    }
}

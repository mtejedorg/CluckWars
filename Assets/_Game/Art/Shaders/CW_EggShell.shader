// Cluck Wars - Egg Shell body shader (batch D / D3).
// Hand-written URP HLSL instead of a .shadergraph: a Lit surface is a few lines of UniversalFragmentPBR here, while a
// hand-authored ShaderGraph JSON is brittle, cannot be diffed or reviewed, and a bad node wiring fails silently pink.
// Opaque, two-sided (Cull Off so the inside of the open cup and the dropped cap shows), no emission, no normal map.
//
// Surface = albedo -> stripe bands -> crack lines -> ink rim:
//   albedo   = _BaseColor * speckle (Fx_EggSpeckle tiled 2x, frac()-wrapped so the Clamp import of the texture is irrelevant)
//   stripes  = two bands centred at 30 % and 70 % of the WHOLE egg height, 6 % tall, ~2 px ink edge, _StripeColor.rgb
//              (alpha = how much of the stripe shows; the driver fades it for the last-0.3 s jitter)
//   cracks   = step(Fx_EggCrackMask.r, _Crack): grey = order of appearance (0 = first, the seam), white = no crack;
//              cracked texels go to _InkColor with smoothness 0
//   rim      = (1 - saturate(N.V))^3 lerps the albedo 25 % toward _InkColor (cartoon edge, no outline pass)
//
// HEIGHT: EggShell.fbx maps UV.v = normalised height of the WHOLE egg (EggShell_Bottom v 0..0.7, EggShell_Top v 0.6..1),
// so the bands read uv.y and the per-half object-space pivot (the cap pivot sits at the seam) never matters.
// The same UV drives the crack mask, which is painted radiating from the seam (the black line at v ~ 0.55).
//
// Per-chicken values (_BaseColor, _Crack, _StripeColor) arrive through a MaterialPropertyBlock from ChickenShellVisuals.
// Every property lives in UnityPerMaterial (SRP-batcher layout); an MPB breaks batching on the shell renderers, which is
// accepted (at most 8 shell renderers in a match). No GPU instancing: the MPB values are not per-instance data here.
Shader "Cluck Wars/Egg Shell"
{
    Properties
    {
        [MainColor] _BaseColor("Base Color", Color) = (0.98, 0.94, 0.86, 1)
        [NoScaleOffset] _SpeckleTex("Speckle (Fx_EggSpeckle)", 2D) = "white" {}
        _SpeckleTiling("Speckle Tiling", Float) = 2
        _StripeColor("Stripe Color (player identity, MPB)", Color) = (1, 0.45, 0.2, 1)
        [NoScaleOffset] _CrackTex("Crack Mask (Fx_EggCrackMask)", 2D) = "white" {}
        _Crack("Crack 0..1 (MPB)", Range(0, 1)) = 0
        _InkColor("Ink (FeedbackPalette.InkColor)", Color) = (0.165, 0.102, 0.047, 1)
        _Smoothness("Smoothness", Range(0, 1)) = 0.35
        _RimStrength("Ink Rim Strength", Range(0, 1)) = 0.25
        _RimPower("Ink Rim Power", Range(1, 8)) = 3
        _StripeCenters("Stripe Centres (v)", Vector) = (0.30, 0.70, 0, 0)
        _StripeHalfHeight("Stripe Half Height (v)", Range(0.005, 0.1)) = 0.03
        _StripeEdgePixels("Stripe Ink Edge (px)", Range(0, 6)) = 2
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" "UniversalMaterialType" = "Lit" }
        LOD 300

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        TEXTURE2D(_SpeckleTex);   SAMPLER(sampler_SpeckleTex);
        TEXTURE2D(_CrackTex);     SAMPLER(sampler_CrackTex);

        CBUFFER_START(UnityPerMaterial)
            half4  _BaseColor;
            half4  _StripeColor;
            half4  _InkColor;
            float4 _StripeCenters;
            half   _SpeckleTiling;
            half   _Crack;
            half   _Smoothness;
            half   _RimStrength;
            half   _RimPower;
            half   _StripeHalfHeight;
            half   _StripeEdgePixels;
        CBUFFER_END
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            ZWrite On
            Cull Off

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex Vert
            #pragma fragment Frag

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile_fragment _ _LIGHT_COOKIES
            #pragma multi_compile _ _LIGHT_LAYERS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #include_with_pragmas "Packages/com.unity.render-pipelines.core/ShaderLibrary/FoveatedRenderingKeywords.hlsl"
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/RenderingLayers.hlsl"
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Fog.hlsl"

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                half3  normalWS   : TEXCOORD2;
                half4  fogAndVertexLight : TEXCOORD3; // x fog factor, yzw vertex lighting
            };

            Varyings Vert(Attributes input)
            {
                Varyings o = (Varyings)0;
                VertexPositionInputs p = GetVertexPositionInputs(input.positionOS.xyz);
                VertexNormalInputs n = GetVertexNormalInputs(input.normalOS);
                o.positionCS = p.positionCS;
                o.positionWS = p.positionWS;
                o.normalWS = half3(n.normalWS);
                o.uv = input.uv;
                half3 vertexLight = 0;
                #if defined(_ADDITIONAL_LIGHTS_VERTEX)
                    vertexLight = VertexLighting(p.positionWS, n.normalWS);
                #endif
                o.fogAndVertexLight = half4(ComputeFogFactor(p.positionCS.z), vertexLight);
                return o;
            }

            // Smooth 0..1 mask of |v - centre| < halfHeight, anti-aliased over one screen pixel.
            half BandMask(float v, float centre, float halfHeight, float aa)
            {
                return saturate((halfHeight - abs(v - centre)) / max(aa, 1e-5) + 0.5);
            }

            half4 Frag(Varyings i, FRONT_FACE_TYPE facing : FRONT_FACE_SEMANTIC) : SV_Target
            {
                // ---- albedo ----------------------------------------------------------------
                half speckle = SAMPLE_TEXTURE2D(_SpeckleTex, sampler_SpeckleTex, frac(i.uv * _SpeckleTiling)).r;
                half3 albedo = _BaseColor.rgb * speckle;

                // ---- stripes: bands in whole-egg height (uv.y), ink edge a fixed ~N pixels wide ----
                float v = i.uv.y;
                float px = max(fwidth(v), 1e-5);                 // one screen pixel in v
                float edge = px * _StripeEdgePixels;
                half bandA = saturate(BandMask(v, _StripeCenters.x, _StripeHalfHeight, px)
                                    + BandMask(v, _StripeCenters.y, _StripeHalfHeight, px));
                half withEdgeA = saturate(BandMask(v, _StripeCenters.x, _StripeHalfHeight + edge, px)
                                        + BandMask(v, _StripeCenters.y, _StripeHalfHeight + edge, px));
                half inkRing = saturate(withEdgeA - bandA);
                albedo = lerp(albedo, _InkColor.rgb, inkRing * _StripeColor.a);
                albedo = lerp(albedo, _StripeColor.rgb, bandA * _StripeColor.a);

                // ---- cracks -----------------------------------------------------------------
                half mask = SAMPLE_TEXTURE2D(_CrackTex, sampler_CrackTex, i.uv).r;
                half crack = step(mask, _Crack) * step(mask, 0.98h) * step(0.001h, _Crack);   // white (> 0.98) is "no crack"
                albedo = lerp(albedo, _InkColor.rgb, crack);
                half smoothness = lerp(_Smoothness, 0.0h, crack);

                // ---- normal (double-sided) and ink rim ---------------------------------------
                half3 normalWS = NormalizeNormalPerPixel(i.normalWS);
                normalWS *= IS_FRONT_VFACE(facing, 1.0h, -1.0h);
                half3 viewDirWS = GetWorldSpaceNormalizeViewDir(i.positionWS);
                half fres = pow(1.0h - saturate(dot(normalWS, viewDirWS)), _RimPower);
                albedo = lerp(albedo, _InkColor.rgb, saturate(fres) * _RimStrength);

                // ---- lighting ----------------------------------------------------------------
                InputData inputData = (InputData)0;
                inputData.positionWS = i.positionWS;
                inputData.positionCS = i.positionCS;
                inputData.normalWS = normalWS;
                inputData.viewDirectionWS = viewDirWS;
                #if defined(MAIN_LIGHT_CALCULATE_SHADOWS)
                    inputData.shadowCoord = TransformWorldToShadowCoord(i.positionWS);
                #else
                    inputData.shadowCoord = float4(0, 0, 0, 0);
                #endif
                inputData.fogCoord = i.fogAndVertexLight.x;
                inputData.vertexLighting = i.fogAndVertexLight.yzw;
                inputData.bakedGI = SampleSH(normalWS);
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(i.positionCS);
                inputData.shadowMask = half4(1, 1, 1, 1);

                SurfaceData s = (SurfaceData)0;
                s.albedo = albedo;
                s.metallic = 0;
                s.specular = half3(0, 0, 0);
                s.smoothness = smoothness;
                s.occlusion = 1;
                s.emission = 0;
                s.alpha = 1;
                s.normalTS = half3(0, 0, 1);

                half4 color = UniversalFragmentPBR(inputData, s);
                color.rgb = MixFog(color.rgb, inputData.fogCoord);
                color.a = 1;
                return color;
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }

            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull Off

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex ShadowVert
            #pragma fragment ShadowFrag
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;
            float3 _LightPosition;

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings { float4 positionCS : SV_POSITION; };

            Varyings ShadowVert(Attributes input)
            {
                Varyings o;
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);
                #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                    float3 lightDirectionWS = normalize(_LightPosition - positionWS);
                #else
                    float3 lightDirectionWS = _LightDirection;
                #endif
                float4 positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDirectionWS));
                o.positionCS = ApplyShadowClamping(positionCS);
                return o;
            }

            half4 ShadowFrag(Varyings i) : SV_Target { return 0; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }

            ZWrite On
            ColorMask R
            Cull Off

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex DepthVert
            #pragma fragment DepthFrag

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; };

            Varyings DepthVert(Attributes input)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                return o;
            }

            half DepthFrag(Varyings i) : SV_Target { return i.positionCS.z; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }

            ZWrite On
            Cull Off

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex NormVert
            #pragma fragment NormFrag
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/RenderingLayers.hlsl"

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings { float4 positionCS : SV_POSITION; half3 normalWS : TEXCOORD0; };

            Varyings NormVert(Attributes input)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                o.normalWS = half3(TransformObjectToWorldNormal(input.normalOS));
                return o;
            }

            void NormFrag(Varyings i, FRONT_FACE_TYPE facing : FRONT_FACE_SEMANTIC
                , out half4 outNormalWS : SV_Target0
            #ifdef _WRITE_RENDERING_LAYERS
                , out uint outRenderingLayers : SV_Target1
            #endif
            )
            {
                half3 n = NormalizeNormalPerPixel(i.normalWS) * IS_FRONT_VFACE(facing, 1.0h, -1.0h);
                outNormalWS = half4(n, 0.0);
                #ifdef _WRITE_RENDERING_LAYERS
                    outRenderingLayers = EncodeMeshRenderingLayer();
                #endif
            }
            ENDHLSL
        }
    }

    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}

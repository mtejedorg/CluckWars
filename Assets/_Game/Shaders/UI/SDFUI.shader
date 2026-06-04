Shader "CluckWars/UI/SDF"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        
        [Enum(RoundRect,0,Hexagon,1)] _Shape ("Shape", Float) = 0
        _Radius ("Corner Radius", Float) = 10
        _BorderWidth ("Border Width", Float) = 0
        _BorderColor ("Border Color", Color) = (0,0,0,1)
        
        _ShadowOffset ("Shadow Offset", Vector) = (0, -2, 0, 0)
        _ShadowColor ("Shadow Color", Color) = (0,0,0,0.5)
        _ShadowSoftness ("Shadow Softness", Float) = 2
        
        [HideInInspector] _StencilComp ("Stencil Comparison", Float) = 8
        [HideInInspector] _Stencil ("Stencil ID", Float) = 0
        [HideInInspector] _StencilOp ("Stencil Operation", Float) = 0
        [HideInInspector] _StencilWriteMask ("Stencil Write Mask", Float) = 255
        [HideInInspector] _StencilReadMask ("Stencil Read Mask", Float) = 255
        [HideInInspector] _ColorMask ("Color Mask", Float) = 15
    }

    SubShader
    {
        Tags
        {
            "Queue"="Transparent"
            "IgnoreProjector"="True"
            "RenderType"="Transparent"
            "PreviewType"="Plane"
            "CanUseSpriteAtlas"="True"
        }

        Stencil
        {
            Ref [_Stencil]
            Comp [_StencilComp]
            Pass [_StencilOp]
            ReadMask [_StencilReadMask]
            WriteMask [_StencilWriteMask]
        }

        Cull Off
        Lighting Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend SrcAlpha OneMinusSrcAlpha
        ColorMask [_ColorMask]

        Pass
        {
            Name "Default"
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 2.0

            #include "UnityCG.cginc"
            #include "UnityUI.cginc"

            struct appdata_t
            {
                float4 vertex   : POSITION;
                float4 color    : COLOR;
                float2 texcoord : TEXCOORD0;
                float4 texcoord1 : TEXCOORD1; // Used for Rect Size (Width, Height)
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 vertex   : SV_POSITION;
                fixed4 color    : COLOR;
                float2 texcoord  : TEXCOORD0;
                float2 rectSize  : TEXCOORD1; // Passed from script
                float4 worldPosition : TEXCOORD2;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            sampler2D _MainTex;
            fixed4 _Color;
            float _Shape;
            float _Radius;
            float _BorderWidth;
            fixed4 _BorderColor;
            float4 _ShadowOffset;
            fixed4 _ShadowColor;
            float _ShadowSoftness;
            float4 _ClipRect;

            v2f vert(appdata_t v)
            {
                v2f OUT;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);
                OUT.worldPosition = v.vertex;
                OUT.vertex = UnityObjectToClipPos(OUT.worldPosition);
                OUT.texcoord = v.texcoord;
                OUT.rectSize = v.texcoord1.xy; // Extracted from UV1
                OUT.color = v.color * _Color;
                return OUT;
            }
            
            // Signed Distance Field for a rounded box
            float sdRoundRect(float2 p, float2 b, float r)
            {
                float2 d = abs(p) - b + float2(r, r);
                return min(max(d.x, d.y), 0.0) + length(max(d, 0.0)) - r;
            }

            // Pointy Top Hexagon SDF
            float sdHexagonPointy(float2 p, float r)
            {
                p = p.yx;
                const float3 k = float3(-0.866025404, 0.5, 0.577350269);
                p = abs(p);
                p -= 2.0 * min(dot(k.xy, p), 0.0) * k.xy;
                p -= float2(clamp(p.x, -k.z * r, k.z * r), r);
                return length(p) * sign(p.y);
            }

            float sdShape(float2 p, float2 extents, float radius, float shapeType)
            {
                if (shapeType > 0.5) {
                    float r = extents.x - radius;
                    return sdHexagonPointy(p, r) - radius;
                } else {
                    return sdRoundRect(p, extents, radius);
                }
            }

            fixed4 frag(v2f IN) : SV_Target
            {
                // Local coordinate from center of rect in pixels
                float2 p = (IN.texcoord - 0.5) * IN.rectSize;
                float2 extents = IN.rectSize * 0.5;
                
                // Shadow
                float2 shadowP = p - _ShadowOffset.xy;
                float shadowDist = sdShape(shadowP, extents, _Radius, _Shape);
                float shadowAlpha = smoothstep(_ShadowSoftness, -_ShadowSoftness, shadowDist);
                fixed4 shadowCol = fixed4(_ShadowColor.rgb, _ShadowColor.a * shadowAlpha);
                
                // Main Body
                float dist = sdShape(p, extents, _Radius, _Shape);
                
                // Smooth antialiasing
                float aa = fwidth(dist);
                float bodyAlpha = smoothstep(aa, -aa, dist);
                
                // Border
                float borderAlpha = smoothstep(aa, -aa, dist + _BorderWidth);
                
                fixed4 texCol = tex2D(_MainTex, IN.texcoord);
                fixed4 fillCol = IN.color * texCol;
                
                // Blend Border and Fill
                fixed4 finalCol = lerp(_BorderColor, fillCol, borderAlpha);
                finalCol.a *= bodyAlpha;
                
                // Blend Shadow behind
                fixed4 outCol = lerp(shadowCol, finalCol, finalCol.a);
                outCol.a = max(finalCol.a, shadowCol.a);

                #ifdef UNITY_UI_CLIP_RECT
                outCol.a *= UnityGet2DClipping(IN.worldPosition.xy, _ClipRect);
                #endif
                
                return outCol;
            }
            ENDCG
        }
    }
}

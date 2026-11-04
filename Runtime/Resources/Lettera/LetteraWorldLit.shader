Shader "Lettera/World Lit"
{
    Properties
    {
        [HideInInspector] _SrcBlend ("Source Blend", Float) = 1
        [HideInInspector] _DstBlend ("Destination Blend", Float) = 10
        [HideInInspector] _LetteraPaintTex ("Paint Texture", 2D) = "white" {}
    }

    SubShader
    {
        PackageRequirements { "com.unity.render-pipelines.universal" }
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" "IgnoreProjector"="True" "RenderType"="Transparent" "PreviewType"="Plane" }

        Pass
        {
            Name "Text"
            Tags { "LightMode"="UniversalForward" }
            Cull Off
            ZWrite Off
            Blend [_SrcBlend] [_DstBlend]

            HLSLPROGRAM
            #pragma vertex TextVertex
            #pragma fragment TextFragment
            #pragma target 3.5
            #pragma require 2darray
            #pragma multi_compile_fog
            #pragma multi_compile_local _ LETTERA_STYLED
            #define LETTERA_LIT
            #include "LetteraWorldURP.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            Cull Off
            ZWrite On
            ColorMask 0

            HLSLPROGRAM
            #pragma vertex ShadowVertex
            #pragma fragment ShadowFragment
            #pragma target 3.5
            #pragma require 2darray
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #include "LetteraWorldURP.hlsl"
            ENDHLSL
        }
    }

    // The High Definition pipeline: its camera-relative space, its exposure, fog and light loop.
    SubShader
    {
        PackageRequirements { "com.unity.render-pipelines.high-definition" }
        Tags { "RenderPipeline"="HDRenderPipeline" "Queue"="Transparent" "IgnoreProjector"="True" "RenderType"="Transparent" "PreviewType"="Plane" }

        Pass
        {
            Name "Text"
            Tags { "LightMode"="ForwardOnly" }
            Cull Off
            ZWrite Off
            Blend [_SrcBlend] [_DstBlend]

            HLSLPROGRAM
            #pragma vertex TextVertex
            #pragma fragment TextFragment
            #pragma target 4.5
            #pragma only_renderers d3d11 playstation xboxone xboxseries vulkan metal switch switch2
            #pragma multi_compile_local _ LETTERA_STYLED
            #pragma multi_compile_fragment _ PROBE_VOLUMES_L1 PROBE_VOLUMES_L2
            #pragma multi_compile_fragment PUNCTUAL_SHADOW_LOW PUNCTUAL_SHADOW_MEDIUM PUNCTUAL_SHADOW_HIGH
            #pragma multi_compile_fragment DIRECTIONAL_SHADOW_LOW DIRECTIONAL_SHADOW_MEDIUM DIRECTIONAL_SHADOW_HIGH
            #pragma multi_compile_fragment AREA_SHADOW_MEDIUM AREA_SHADOW_HIGH
            #ifndef SHADER_STAGE_FRAGMENT
            #define SHADOW_LOW
            #endif
            #define LETTERA_LIT
            #include "LetteraWorldHDRP.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            Cull Off
            ZWrite On
            ColorMask 0

            HLSLPROGRAM
            #pragma vertex TextVertex
            #pragma fragment ShadowFragment
            #pragma target 4.5
            #pragma only_renderers d3d11 playstation xboxone xboxseries vulkan metal switch switch2
            #define LETTERA_SHADOW_PASS
            #define SHADERPASS SHADERPASS_SHADOWS
            #include "LetteraWorldHDRP.hlsl"
            ENDHLSL
        }
    }

    SubShader
    {
        Tags { "Queue"="Transparent" "IgnoreProjector"="True" "RenderType"="Transparent" "PreviewType"="Plane" }

        Pass
        {
            Name "Text"
            Tags { "LightMode"="ForwardBase" }
            Cull Off
            ZWrite Off
            Blend [_SrcBlend] [_DstBlend]

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.5
            #pragma require 2darray
            #pragma multi_compile_fog
            #pragma multi_compile_local _ LETTERA_STYLED

            #include "UnityCG.cginc"
            #include "UnityLightingCommon.cginc"
            #include "LetteraGlyph.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                float4 color : COLOR;
                float4 uv0 : TEXCOORD0;
                float4 uv1 : TEXCOORD1;
                #ifdef LETTERA_STYLED
                float4 uv2 : TEXCOORD2;
                float4 uv3 : TEXCOORD3;
                #endif
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                fixed4 color : COLOR;
                float4 uv0 : TEXCOORD0;
                float4 params : TEXCOORD1;
                float3 normal : TEXCOORD2;
                UNITY_FOG_COORDS(3)
                #ifdef LETTERA_STYLED
                float4 paint : TEXCOORD4;
                float4 extra : TEXCOORD5;
                #endif
                UNITY_VERTEX_OUTPUT_STEREO
            };

            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv0 = v.uv0;
                o.params = v.uv1;
                #ifdef LETTERA_STYLED
                o.paint = v.uv2;
                o.extra = v.uv3;
                #endif
                o.normal = UnityObjectToWorldNormal(v.normal);
                o.color = v.color;
                #ifndef UNITY_COLORSPACE_GAMMA
                o.color.rgb = GammaToLinearSpace(o.color.rgb);
                #endif
                UNITY_TRANSFER_FOG(o, o.vertex);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                #ifdef LETTERA_STYLED
                fixed4 color = LetteraShadeStyled(i.uv0, i.params, i.color, i.paint, i.extra, i.vertex.xy);
                #else
                fixed4 color = LetteraShadePlain(i.uv0, i.params.xy, i.color);
                #endif
                float3 normal = normalize(i.normal);
                // Text is two-sided, so light reaches whichever side faces it.
                color.rgb *= ShadeSH9(float4(normal, 1)) + _LightColor0.rgb * abs(dot(normal, _WorldSpaceLightPos0.xyz));
                UNITY_APPLY_FOG(i.fogCoord, color);
                color.rgb *= color.a;
                return color;
            }
            ENDCG
        }
    }

    Fallback "Lettera/World"
}

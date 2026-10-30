Shader "Lettera/World"
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
            Tags { "LightMode"="SRPDefaultUnlit" }
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

    SubShader
    {
        Tags { "Queue"="Transparent" "IgnoreProjector"="True" "RenderType"="Transparent" "PreviewType"="Plane" }

        Pass
        {
            Name "Text"
            Cull Off
            Lighting Off
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
            #include "LetteraGlyph.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
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
                UNITY_FOG_COORDS(2)
                #ifdef LETTERA_STYLED
                float4 paint : TEXCOORD3;
                float4 extra : TEXCOORD4;
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
                UNITY_APPLY_FOG(i.fogCoord, color);
                color.rgb *= color.a;
                return color;
            }
            ENDCG
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            Cull Off
            ZWrite On
            ColorMask 0

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.5
            #pragma require 2darray
            #pragma multi_compile_shadowcaster

            #include "UnityCG.cginc"
            #include "LetteraGlyph.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                float4 color : COLOR;
                float4 uv0 : TEXCOORD0;
                float4 uv1 : TEXCOORD1;
            };

            struct v2f
            {
                V2F_SHADOW_CASTER;
                float4 uv0 : TEXCOORD1;
                float2 params : TEXCOORD2;
                fixed4 color : COLOR;
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.uv0 = v.uv0;
                o.params = v.uv1.xy;
                o.color = v.color;
                TRANSFER_SHADOW_CASTER_NORMALOFFSET(o);
                return o;
            }

            float4 frag(v2f i) : SV_Target
            {
                clip(LetteraShadePlain(i.uv0, i.params.xy, i.color).a - 0.5);
                SHADOW_CASTER_FRAGMENT(i);
            }
            ENDCG
        }
    }
}

Shader "Lettera/UI"
{
    Properties
    {
        [PerRendererData] _MainTex ("Unused", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255
        _ColorMask ("Color Mask", Float) = 15
        [Toggle(UNITY_UI_ALPHACLIP)] _UseUIAlphaClip ("Use Alpha Clip", Float) = 0
        [HideInInspector] _SrcBlend ("Source Blend", Float) = 1
        [HideInInspector] _DstBlend ("Destination Blend", Float) = 10
        [HideInInspector] _LetteraPaintTex ("Paint Texture", 2D) = "white" {}
    }

    SubShader
    {
        Tags { "Queue"="Transparent" "IgnoreProjector"="True" "RenderType"="Transparent" "PreviewType"="Plane" "CanUseSpriteAtlas"="True" }

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
        Blend [_SrcBlend] [_DstBlend]
        ColorMask [_ColorMask]

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.5
            #pragma require 2darray
            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #pragma multi_compile_local _ UNITY_UI_ALPHACLIP
            #pragma multi_compile_local _ LETTERA_STYLED LETTERA_MERGED LETTERA_LAYERED LETTERA_GRAINED

            #include "UnityCG.cginc"
            #include "UnityUI.cginc"

            int _UIVertexColorAlwaysGammaSpace;
            // A merged quad's layer colors come in texture coordinates, so they take the conversion its vertex color takes.
            #define LETTERA_LAYER_RGB(rgb) ((_UIVertexColorAlwaysGammaSpace && !IsGammaSpace()) ? UIGammaToLinear(rgb) : (rgb))
            #include "LetteraGlyph.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float4 color : COLOR;
                float4 uv0 : TEXCOORD0;
                float4 uv1 : TEXCOORD1;
                #if defined(LETTERA_STYLED) || defined(LETTERA_MERGED)
                float4 uv2 : TEXCOORD2;
                float4 uv3 : TEXCOORD3;
                #endif
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            // Shared layers read colour and edge from the corner that leads each triangle.
            #ifdef LETTERA_LAYERED
            #define LETTERA_FLAT nointerpolation
            #else
            #define LETTERA_FLAT
            #endif

            struct v2f
            {
                float4 vertex : SV_POSITION;
                LETTERA_FLAT fixed4 color : COLOR;
                float4 uv0 : TEXCOORD0;
                LETTERA_FLAT float4 params : TEXCOORD1;
                #ifdef UNITY_UI_CLIP_RECT
                float4 mask : TEXCOORD3;
                #endif
                #if defined(LETTERA_STYLED) || defined(LETTERA_MERGED)
                float4 paint : TEXCOORD4;
                float4 extra : TEXCOORD5;
                #endif
                UNITY_VERTEX_OUTPUT_STEREO
            };

            fixed4 _Color;
            float4 _ClipRect;
            float _UIMaskSoftnessX;
            float _UIMaskSoftnessY;

            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv0 = v.uv0;
                o.params = v.uv1;
                #if defined(LETTERA_STYLED) || defined(LETTERA_MERGED)
                o.paint = v.uv2;
                o.extra = v.uv3;
                #endif

                #ifdef UNITY_UI_CLIP_RECT
                float2 pixelSize = o.vertex.w;
                pixelSize /= float2(1, 1) * abs(mul((float2x2)UNITY_MATRIX_P, _ScreenParams.xy));
                float4 clampedRect = clamp(_ClipRect, -2e10, 2e10);
                o.mask = float4(v.vertex.xy * 2 - clampedRect.xy - clampedRect.zw,
                    0.25 / (0.25 * half2(_UIMaskSoftnessX, _UIMaskSoftnessY) + abs(pixelSize.xy)));
                #endif

                if (_UIVertexColorAlwaysGammaSpace && !IsGammaSpace())
                    v.color.rgb = UIGammaToLinear(v.color.rgb);
                o.color = v.color * _Color;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                #if defined(LETTERA_MERGED)
                // Merged quads and plain glyphs only: a text drawn this way has nothing else.
                fixed4 color;
                UNITY_BRANCH
                if (i.uv0.w > 12.5) color = LetteraShadeMerged(i.uv0, i.params, i.color, i.paint, i.extra);
                else color = LetteraShadePlain(i.uv0, i.params.xy, i.color);
                #elif defined(LETTERA_LAYERED)
                // Distance glyphs only; a shadow's corner also says how far its field lies from the quad.
                fixed4 color = i.color;
                color.a *= LetteraEdge(LETTERA_SAMPLE_SDF(i.uv0.xy - i.params.zw, i.uv0.z).r, i.params.x, i.params.y);
                #elif defined(LETTERA_STYLED)
                fixed4 color = LetteraShadeStyled(i.uv0, i.params, i.color, i.paint, i.extra, i.vertex.xy);
                #else
                fixed4 color = LetteraShadePlain(i.uv0, i.params.xy, i.color);
                #endif

                #ifdef UNITY_UI_CLIP_RECT
                half2 m = saturate((_ClipRect.zw - _ClipRect.xy - abs(i.mask.xy)) * i.mask.zw);
                color.a *= m.x * m.y;
                #endif

                #ifdef UNITY_UI_ALPHACLIP
                clip(color.a - 0.001);
                #endif

                color.rgb *= color.a;
                return color;
            }
            ENDCG
        }
    }
}

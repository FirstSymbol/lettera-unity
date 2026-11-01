// An effect's own shader, made from Assets > Create > Lettera > Custom Effect. Only the function near the top was edited. It sits in Resources so that Shader.Find has it in a build.
Shader "Lettera Samples/Stripes"
{
    Properties
    {
        _Tint ("Tint", Color) = (1,1,1,1)
        [HideInInspector] _StencilComp ("Stencil Comparison", Float) = 8
        [HideInInspector] _Stencil ("Stencil ID", Float) = 0
        [HideInInspector] _StencilOp ("Stencil Operation", Float) = 0
        [HideInInspector] _StencilWriteMask ("Stencil Write Mask", Float) = 255
        [HideInInspector] _StencilReadMask ("Stencil Read Mask", Float) = 255
        [HideInInspector] _ColorMask ("Color Mask", Float) = 15
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "IgnoreProjector"="True" "RenderType"="Transparent" "PreviewType"="Plane" }
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
        Blend One OneMinusSrcAlpha
        ColorMask [_ColorMask]

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.5
            #pragma require 2darray
            #define LETTERA_STYLED
            #include "UnityCG.cginc"
            #include "Packages/com.firstsymbol.lettera/Runtime/Resources/Lettera/LetteraGlyph.cginc"

            fixed4 _Tint;

            struct appdata
            {
                float4 vertex : POSITION;
                float4 uv0 : TEXCOORD0;
                fixed4 color : COLOR;
            };

            struct LetteraCustomInput
            {
                float distance;     // 0.5 at the outline, more inside, less outside.
                fixed4 atlasColor;  // A color glyph's own color.
                fixed4 vertexColor; // The color the style asked for.
                float2 position;    // In the text's local units.
                float2 uv;          // In the atlas.
            };

            // Diagonal stripes in two colours, moving, inside the letters.
            fixed4 LetteraCustom(LetteraCustomInput i)
            {
                float coverage = saturate((i.distance - 0.5) / max(fwidth(i.distance), 0.0001) + 0.5);
                float band = frac((i.position.x + i.position.y) * 0.08 + _Time.y * 0.5);
                fixed3 color = band < 0.5 ? fixed3(1, 0.75, 0.2) : fixed3(0.95, 0.25, 0.45);
                return fixed4(color * coverage, coverage) * i.vertexColor.a * _Tint.a;
            }

            struct v2f
            {
                float4 vertex : SV_POSITION;
                float4 uv0 : TEXCOORD0;
                fixed4 color : COLOR;
                float2 position : TEXCOORD1;
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv0 = v.uv0;
                o.position = v.vertex.xy;
                o.color = v.color;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                LetteraCustomInput input;
                input.distance = LetteraDistance(i.uv0.xy, i.uv0.z, i.uv0.w);
                input.atlasColor = LETTERA_SAMPLE_COLOR(i.uv0.xy, i.uv0.z);
                input.vertexColor = i.color;
                input.position = i.position;
                input.uv = i.uv0.xy;
                return LetteraCustom(input);
            }
            ENDCG
        }
    }
}

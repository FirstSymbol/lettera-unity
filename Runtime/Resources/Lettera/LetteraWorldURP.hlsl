#ifndef LETTERA_WORLD_URP_INCLUDED
#define LETTERA_WORLD_URP_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

TEXTURE2D_ARRAY(_LetteraSdfAtlas);
SAMPLER(sampler_LetteraSdfAtlas);
TEXTURE2D_ARRAY(_LetteraColorAtlas);
SAMPLER(sampler_LetteraColorAtlas);
TEXTURE2D_ARRAY(_LetteraMsdfAtlas);
SAMPLER(sampler_LetteraMsdfAtlas);

#define LETTERA_COLOR4 half4
#define LETTERA_SAMPLE_SDF(uv, page) SAMPLE_TEXTURE2D_ARRAY(_LetteraSdfAtlas, sampler_LetteraSdfAtlas, uv, page)
#define LETTERA_SAMPLE_MSDF(uv, page) SAMPLE_TEXTURE2D_ARRAY(_LetteraMsdfAtlas, sampler_LetteraMsdfAtlas, uv, page)
#define LETTERA_SAMPLE_COLOR(uv, page) SAMPLE_TEXTURE2D_ARRAY(_LetteraColorAtlas, sampler_LetteraColorAtlas, uv, page)

#ifdef LETTERA_STYLED
TEXTURE2D(_LetteraRamps);
SAMPLER(sampler_LetteraRamps);
TEXTURE2D(_LetteraPaintTex);
SAMPLER(sampler_LetteraPaintTex);
#define LETTERA_SAMPLE_RAMP(uv) SAMPLE_TEXTURE2D_LOD(_LetteraRamps, sampler_LetteraRamps, uv, 0)
#define LETTERA_SAMPLE_PAINT(uv) SAMPLE_TEXTURE2D(_LetteraPaintTex, sampler_LetteraPaintTex, uv)
#endif

#include "LetteraShadeCore.hlsl"

struct Attributes
{
    float4 positionOS : POSITION;
    float3 normalOS : NORMAL;
    half4 color : COLOR;
    float4 uv0 : TEXCOORD0;
    float4 uv1 : TEXCOORD1;
    #ifdef LETTERA_STYLED
    float4 uv2 : TEXCOORD2;
    float4 uv3 : TEXCOORD3;
    #endif
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

struct Varyings
{
    float4 positionCS : SV_POSITION;
    half4 color : COLOR;
    float4 uv0 : TEXCOORD0;
    float3 params : TEXCOORD1;
    float3 normalWS : TEXCOORD2;
    float fog : TEXCOORD3;
    #ifdef LETTERA_STYLED
    float4 paint : TEXCOORD4;
    float4 extra : TEXCOORD5;
    #endif
    UNITY_VERTEX_OUTPUT_STEREO
};

Varyings TextVertex(Attributes input)
{
    Varyings output;
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
    output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
    output.uv0 = input.uv0;
    output.params = input.uv1.xyz;
    #ifdef LETTERA_STYLED
    output.paint = input.uv2;
    output.extra = input.uv3;
    #endif
    output.normalWS = TransformObjectToWorldNormal(input.normalOS);
    output.color = input.color;
    #ifndef UNITY_COLORSPACE_GAMMA
    output.color.rgb = SRGBToLinear(output.color.rgb);
    #endif
    output.fog = ComputeFogFactor(output.positionCS.z);
    return output;
}

half4 TextFragment(Varyings input) : SV_Target
{
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
    #ifdef LETTERA_STYLED
    half4 color = LetteraShadeStyled(input.uv0, input.params, input.color, input.paint, input.extra, input.positionCS.xy);
    #else
    half4 color = LetteraShadePlain(input.uv0, input.params.xy, input.color);
    #endif
    #ifdef LETTERA_LIT
    // Text is two-sided, so light reaches whichever side faces it.
    float3 normal = normalize(input.normalWS);
    Light light = GetMainLight();
    half3 ambient = SampleSH(normal);
    color.rgb *= ambient + light.color * abs(dot(normal, light.direction));
    #endif
    color.rgb = MixFog(color.rgb, input.fog);
    color.rgb *= color.a;
    return color;
}

float3 _LightDirection;
float3 _LightPosition;

Varyings ShadowVertex(Attributes input)
{
    Varyings output = (Varyings)0;
    UNITY_SETUP_INSTANCE_ID(input);
    float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
    float3 normalWS = TransformObjectToWorldNormal(input.normalOS);
    #if _CASTING_PUNCTUAL_LIGHT_SHADOW
    float3 lightDirection = normalize(_LightPosition - positionWS);
    #else
    float3 lightDirection = _LightDirection;
    #endif
    float4 positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDirection));
    #if UNITY_REVERSED_Z
    positionCS.z = min(positionCS.z, UNITY_NEAR_CLIP_VALUE);
    #else
    positionCS.z = max(positionCS.z, UNITY_NEAR_CLIP_VALUE);
    #endif
    output.positionCS = positionCS;
    output.uv0 = input.uv0;
    output.params = input.uv1.xyz;
    output.color = input.color;
    return output;
}

half4 ShadowFragment(Varyings input) : SV_Target
{
    clip(LetteraShadePlain(input.uv0, input.params.xy, input.color).a - 0.5);
    return 0;
}

#endif

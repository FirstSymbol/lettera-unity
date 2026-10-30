#ifndef LETTERA_WORLD_URP_INCLUDED
#define LETTERA_WORLD_URP_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

TEXTURE2D_ARRAY(_LetteraSdfAtlas);
SAMPLER(sampler_LetteraSdfAtlas);
TEXTURE2D_ARRAY(_LetteraColorAtlas);
SAMPLER(sampler_LetteraColorAtlas);

// uv0: atlas uv, page, mode (0 SDF, 1 color glyph, 2 color glyph silhouette, 3 solid).
// params: x = the SDF value of the edge, y = extra softness in SDF values.
half4 LetteraShade(float4 uv0, float2 params, half4 color)
{
    float mode = uv0.w;
    if (mode < 0.5)
    {
        float d = SAMPLE_TEXTURE2D_ARRAY(_LetteraSdfAtlas, sampler_LetteraSdfAtlas, uv0.xy, uv0.z).r;
        float w = max(fwidth(d) * 0.75, params.y) + 1e-5;
        color.a *= smoothstep(params.x - w, params.x + w, d);
        return color;
    }
    if (mode < 2.5)
    {
        half4 sampled = SAMPLE_TEXTURE2D_ARRAY(_LetteraColorAtlas, sampler_LetteraColorAtlas, uv0.xy, uv0.z);
        if (mode < 1.5)
        {
            sampled.a *= color.a;
            return sampled;
        }
        color.a *= sampled.a;
        return color;
    }
    return color;
}

struct Attributes
{
    float4 positionOS : POSITION;
    float3 normalOS : NORMAL;
    half4 color : COLOR;
    float4 uv0 : TEXCOORD0;
    float4 uv1 : TEXCOORD1;
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

struct Varyings
{
    float4 positionCS : SV_POSITION;
    half4 color : COLOR;
    float4 uv0 : TEXCOORD0;
    float2 params : TEXCOORD1;
    float3 normalWS : TEXCOORD2;
    float fog : TEXCOORD3;
    UNITY_VERTEX_OUTPUT_STEREO
};

Varyings TextVertex(Attributes input)
{
    Varyings output;
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
    output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
    output.uv0 = input.uv0;
    output.params = input.uv1.xy;
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
    half4 color = LetteraShade(input.uv0, input.params, input.color);
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
    output.params = input.uv1.xy;
    output.color = input.color;
    return output;
}

half4 ShadowFragment(Varyings input) : SV_Target
{
    clip(LetteraShade(input.uv0, input.params, input.color).a - 0.5);
    return 0;
}

#endif

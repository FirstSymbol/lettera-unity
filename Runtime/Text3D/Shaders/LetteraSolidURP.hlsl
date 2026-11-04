#ifndef LETTERA_SOLID_URP_INCLUDED
#define LETTERA_SOLID_URP_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

TEXTURE2D(_BaseMap);
SAMPLER(sampler_BaseMap);

CBUFFER_START(UnityPerMaterial)
    float4 _BaseMap_ST;
    half4 _BaseColor;
    half4 _EmissionColor;
    half _Metallic;
    half _Smoothness;
    half _LightInfluence;
    float _TextureSpace;
CBUFFER_END

// Opaque text fades through an ordered screen pattern, so it needs no sorting.
void LetteraDither(half alpha, float2 pixel)
{
    static const float pattern[16] = { 0.0625, 0.5625, 0.1875, 0.6875, 0.8125, 0.3125, 0.9375, 0.4375, 0.25, 0.75, 0.125, 0.625, 1.0, 0.5, 0.875, 0.375 };
    uint2 cell = (uint2)pixel % 4;
    clip(alpha - pattern[cell.y * 4 + cell.x] + 0.001);
}

#endif

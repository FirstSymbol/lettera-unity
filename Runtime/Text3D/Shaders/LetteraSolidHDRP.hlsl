#ifndef LETTERA_SOLID_HDRP_INCLUDED
#define LETTERA_SOLID_HDRP_INCLUDED

// What the High Definition pipeline's pass templates need from a material: which vertex streams it reads, and its surface.
// The passes are the pipeline's own, so lighting, shadows, probes, fog and the normal buffer are its own too.

#define ATTRIBUTES_NEED_NORMAL
#define ATTRIBUTES_NEED_TEXCOORD0
#define ATTRIBUTES_NEED_TEXCOORD1
#define ATTRIBUTES_NEED_COLOR

#define VARYINGS_NEED_POSITION_WS
#define VARYINGS_NEED_TANGENT_TO_WORLD
#define VARYINGS_NEED_TEXCOORD0
#define VARYINGS_NEED_TEXCOORD1
#define VARYINGS_NEED_COLOR

#include "Packages/com.unity.render-pipelines.high-definition/Runtime/RenderPipeline/ShaderPass/VaryingMesh.hlsl"
#include "Packages/com.unity.render-pipelines.high-definition/Runtime/Material/BuiltinUtilities.hlsl"
#include "Packages/com.unity.render-pipelines.high-definition/Runtime/Material/MaterialUtilities.hlsl"

// Opaque text fades through an ordered screen pattern, so it needs no sorting.
void LetteraDither(float alpha, float2 pixel)
{
    static const float pattern[16] = { 0.0625, 0.5625, 0.1875, 0.6875, 0.8125, 0.3125, 0.9375, 0.4375, 0.25, 0.75, 0.125, 0.625, 1.0, 0.5, 0.875, 0.375 };
    uint2 cell = (uint2)pixel % 4;
    clip(alpha - pattern[cell.y * 4 + cell.x] + 0.001);
}

void GetSurfaceAndBuiltinData(FragInputs input, float3 V, inout PositionInputs posInput, out SurfaceData surfaceData, out BuiltinData builtinData RAY_TRACING_OPTIONAL_PARAMETERS)
{
    float2 uv = _TextureSpace > 0.5 ? input.texCoord1.xy : input.texCoord0.xy;
    float4 color = input.color * _BaseColor * SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uv * _BaseMap_ST.xy + _BaseMap_ST.zw);
    LetteraDither(color.a, posInput.positionSS.xy);

    ZERO_INITIALIZE(SurfaceData, surfaceData);
    surfaceData.materialFeatures = MATERIALFEATUREFLAGS_LIT_STANDARD;
    surfaceData.baseColor = color.rgb * _LightInfluence;
    surfaceData.perceptualSmoothness = _Smoothness * _LightInfluence;
    surfaceData.metallic = _Metallic;
    surfaceData.ambientOcclusion = 1;
    surfaceData.specularOcclusion = 1;
    surfaceData.normalWS = normalize(input.tangentToWorld[2]);
    surfaceData.geomNormalWS = surfaceData.normalWS;
    surfaceData.tangentWS = Orthonormalize(input.tangentToWorld[0], surfaceData.normalWS);
    surfaceData.ior = 1.0;
    surfaceData.transmittanceColor = float3(1.0, 1.0, 1.0);
    surfaceData.atDistance = 1000000.0;

    InitBuiltinData(posInput, 1.0, surfaceData.normalWS, -input.tangentToWorld[2], input.texCoord1, input.texCoord2, builtinData);
    // Emission and the unlit share are colours as written, whatever the camera's exposure.
    builtinData.emissiveColor = (_EmissionColor.rgb + color.rgb * (1 - _LightInfluence)) * GetInverseCurrentExposureMultiplier();
    PostInitBuiltinData(V, posInput, surfaceData, builtinData);
}

#endif

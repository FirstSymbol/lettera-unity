#ifndef LETTERA_SOLID_HDRP_BASE_INCLUDED
#define LETTERA_SOLID_HDRP_BASE_INCLUDED

#define _DISABLE_DECALS
#define _DISABLE_SSR
#define _DISABLE_SSR_TRANSPARENT
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
#include "Packages/com.unity.render-pipelines.high-definition/Runtime/ShaderLibrary/ShaderVariables.hlsl"
#include "Packages/com.unity.render-pipelines.high-definition/Runtime/RenderPipeline/ShaderPass/FragInputs.hlsl"
#include "Packages/com.unity.render-pipelines.high-definition/Runtime/RenderPipeline/ShaderPass/ShaderPass.cs.hlsl"

TEXTURE2D(_BaseMap);
SAMPLER(sampler_BaseMap);

CBUFFER_START(UnityPerMaterial)
    float4 _BaseMap_ST;
    float4 _BaseColor;
    float4 _EmissionColor;
    float _Metallic;
    float _Smoothness;
    float _LightInfluence;
    float _TextureSpace;
CBUFFER_END

#endif

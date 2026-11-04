#ifndef LETTERA_WORLD_HDRP_INCLUDED
#define LETTERA_WORLD_HDRP_INCLUDED

// World text under the High Definition pipeline. Positions go through its camera-relative transforms, colours stay as written
// whatever the exposure, fog is its own, and lit text takes its light loop: every light, its shadows, probes and sky.

#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
#include "Packages/com.unity.render-pipelines.high-definition/Runtime/ShaderLibrary/ShaderVariables.hlsl"
#include "Packages/com.unity.render-pipelines.high-definition/Runtime/RenderPipeline/ShaderPass/FragInputs.hlsl"
#include "Packages/com.unity.render-pipelines.high-definition/Runtime/RenderPipeline/ShaderPass/ShaderPass.cs.hlsl"

#ifndef LETTERA_SHADOW_PASS
    #define _SURFACE_TYPE_TRANSPARENT
    #define _DISABLE_DECALS
    #define _DISABLE_SSR_TRANSPARENT
    // The pipeline's material code names these. Text blends premultiplied by its own pass state, so they only need to exist.
    static const float _BlendMode = 0;
    static const float _EnableBlendModePreserveSpecularLighting = 0;
    #ifdef LETTERA_LIT
        #define USE_CLUSTERED_LIGHTLIST
        #define SHADERPASS SHADERPASS_FORWARD
        #include "Packages/com.unity.render-pipelines.high-definition/Runtime/Material/Material.hlsl"
        #include "Packages/com.unity.render-pipelines.high-definition/Runtime/Lighting/Lighting.hlsl"
        #define HAS_LIGHTLOOP
        #include "Packages/com.unity.render-pipelines.high-definition/Runtime/Lighting/LightLoop/LightLoopDef.hlsl"
        #include "Packages/com.unity.render-pipelines.high-definition/Runtime/Material/Lit/Lit.hlsl"
        #include "Packages/com.unity.render-pipelines.high-definition/Runtime/Lighting/LightLoop/LightLoop.hlsl"
        #include "Packages/com.unity.render-pipelines.high-definition/Runtime/Material/BuiltinUtilities.hlsl"
    #else
        #define SHADERPASS SHADERPASS_FORWARD_UNLIT
        #include "Packages/com.unity.render-pipelines.high-definition/Runtime/Material/Material.hlsl"
    #endif
#endif

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
TEXTURE2D(_LetteraFilters);
SAMPLER(sampler_LetteraFilters);
#define LETTERA_SAMPLE_RAMP(uv) SAMPLE_TEXTURE2D_LOD(_LetteraRamps, sampler_LetteraRamps, uv, 0)
#define LETTERA_SAMPLE_PAINT(uv) SAMPLE_TEXTURE2D(_LetteraPaintTex, sampler_LetteraPaintTex, uv)
#define LETTERA_SAMPLE_FILTER(uv) SAMPLE_TEXTURE2D_LOD(_LetteraFilters, sampler_LetteraFilters, uv, 0)
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
    float4 params : TEXCOORD1;
    float3 normalWS : TEXCOORD2;
    float3 positionRWS : TEXCOORD3;
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
    output.positionRWS = TransformObjectToWorld(input.positionOS.xyz);
    output.positionCS = TransformWorldToHClip(output.positionRWS);
    output.uv0 = input.uv0;
    output.params = input.uv1;
    #ifdef LETTERA_STYLED
    output.paint = input.uv2;
    output.extra = input.uv3;
    #endif
    output.normalWS = TransformObjectToWorldNormal(input.normalOS);
    output.color = input.color;
    output.color.rgb = SRGBToLinear(output.color.rgb);
    return output;
}

#ifndef LETTERA_SHADOW_PASS

half4 TextFragment(Varyings input) : SV_Target
{
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
    #ifdef LETTERA_STYLED
    half4 color = LetteraShadeStyled(input.uv0, input.params, input.color, input.paint, input.extra, input.positionCS.xy);
    #else
    half4 color = LetteraShadePlain(input.uv0, input.params.xy, input.color);
    #endif
    float3 V = GetWorldSpaceNormalizeViewDir(input.positionRWS);
    uint2 tile = uint2(input.positionCS.xy) / GetTileSize();
    PositionInputs posInput = GetPositionInput(input.positionCS.xy, _ScreenSize.zw, input.positionCS.z, input.positionCS.w, input.positionRWS, tile);

    #ifdef LETTERA_LIT
    // Text is two-sided, so light reaches whichever side faces the viewer.
    float3 normal = normalize(input.normalWS);
    normal = dot(normal, V) < 0 ? -normal : normal;
    SurfaceData surfaceData;
    ZERO_INITIALIZE(SurfaceData, surfaceData);
    surfaceData.materialFeatures = MATERIALFEATUREFLAGS_LIT_STANDARD;
    surfaceData.baseColor = color.rgb;
    surfaceData.perceptualSmoothness = 0.25;
    surfaceData.ambientOcclusion = 1;
    surfaceData.specularOcclusion = 1;
    surfaceData.normalWS = normal;
    surfaceData.geomNormalWS = normal;
    surfaceData.tangentWS = Orthonormalize(float3(1, 0, 0), normal);
    surfaceData.ior = 1.0;
    surfaceData.transmittanceColor = float3(1.0, 1.0, 1.0);
    surfaceData.atDistance = 1000000.0;
    BuiltinData builtinData;
    InitBuiltinData(posInput, color.a, normal, -normal, float4(0, 0, 0, 0), float4(0, 0, 0, 0), builtinData);
    PostInitBuiltinData(V, posInput, surfaceData, builtinData);
    BSDFData bsdfData = ConvertSurfaceDataToBSDFData(input.positionCS.xy, surfaceData);
    PreLightData preLightData = GetPreLightData(V, posInput, bsdfData);
    LightLoopOutput lighting;
    LightLoop(V, posInput, preLightData, bsdfData, builtinData, LIGHT_FEATURE_MASK_FLAGS_TRANSPARENT, lighting);
    color.rgb = (lighting.diffuseLighting + lighting.specularLighting) * GetCurrentExposureMultiplier();
    #endif

    half4 result = half4(color.rgb * color.a, color.a);
    float3 fogColor, fogOpacity;
    if (EvaluateAtmosphericScattering(posInput, V, fogColor, fogOpacity))
        result.rgb = result.rgb * (1 - fogOpacity) + fogColor * result.a;
    return result;
}

#else

half4 ShadowFragment(Varyings input) : SV_Target
{
    clip(LetteraShadePlain(input.uv0, input.params.xy, input.color).a - 0.5);
    return 0;
}

#endif

#endif

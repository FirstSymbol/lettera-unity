#ifndef LETTERA_GLYPH_INCLUDED
#define LETTERA_GLYPH_INCLUDED

// The grained variant is the styled one with grain on gradients.
#if defined(LETTERA_GRAINED) && !defined(LETTERA_STYLED)
#define LETTERA_STYLED
#endif

UNITY_DECLARE_TEX2DARRAY(_LetteraSdfAtlas);
UNITY_DECLARE_TEX2DARRAY(_LetteraColorAtlas);
UNITY_DECLARE_TEX2DARRAY(_LetteraMsdfAtlas);

#define LETTERA_COLOR4 fixed4
#define LETTERA_SAMPLE_SDF(uv, page) UNITY_SAMPLE_TEX2DARRAY(_LetteraSdfAtlas, float3(uv, page))
#define LETTERA_SAMPLE_MSDF(uv, page) UNITY_SAMPLE_TEX2DARRAY(_LetteraMsdfAtlas, float3(uv, page))
#define LETTERA_SAMPLE_COLOR(uv, page) UNITY_SAMPLE_TEX2DARRAY(_LetteraColorAtlas, float3(uv, page))

#ifdef LETTERA_STYLED
sampler2D _LetteraRamps;
sampler2D _LetteraPaintTex;
sampler2D _LetteraFilters;
#define LETTERA_SAMPLE_FILTER(uv) tex2Dlod(_LetteraFilters, float4(uv, 0, 0))
#define LETTERA_SAMPLE_RAMP(uv) tex2Dlod(_LetteraRamps, float4(uv, 0, 0))
#define LETTERA_SAMPLE_PAINT(uv) tex2D(_LetteraPaintTex, uv)
#endif
#ifdef LETTERA_GRAINED
// Full precision: its steps are finer than a low precision sampler keeps.
sampler2D_float _LetteraRampsFine;
#define LETTERA_SAMPLE_RAMP_FINE(uv) tex2Dlod(_LetteraRampsFine, float4(uv, 0, 0))
#endif

#include "LetteraShadeCore.hlsl"

#endif

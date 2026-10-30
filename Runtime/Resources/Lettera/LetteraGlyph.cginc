#ifndef LETTERA_GLYPH_INCLUDED
#define LETTERA_GLYPH_INCLUDED

UNITY_DECLARE_TEX2DARRAY(_LetteraSdfAtlas);
UNITY_DECLARE_TEX2DARRAY(_LetteraColorAtlas);

// uv0: atlas uv, page, mode (0 SDF, 1 color glyph, 2 color glyph silhouette, 3 solid).
// params: x = the SDF value of the edge, y = extra softness in SDF values.
fixed4 LetteraShade(float4 uv0, float2 params, fixed4 color)
{
    float mode = uv0.w;
    if (mode < 0.5)
    {
        float d = UNITY_SAMPLE_TEX2DARRAY(_LetteraSdfAtlas, uv0.xyz).r;
        float w = max(fwidth(d) * 0.75, params.y) + 1e-5;
        color.a *= smoothstep(params.x - w, params.x + w, d);
        return color;
    }
    if (mode < 2.5)
    {
        fixed4 sampled = UNITY_SAMPLE_TEX2DARRAY(_LetteraColorAtlas, uv0.xyz);
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

#endif

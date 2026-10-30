#ifndef LETTERA_SHADE_CORE_INCLUDED
#define LETTERA_SHADE_CORE_INCLUDED

// The glyph shading every pass shares. The file that includes this one defines how textures are sampled:
//   LETTERA_SAMPLE_SDF(uv, page), LETTERA_SAMPLE_COLOR(uv, page), LETTERA_SAMPLE_RAMP(uv), LETTERA_SAMPLE_PAINT(uv)
// and the types of colors: LETTERA_COLOR4.
//
// uv0: atlas uv, page, mode (0 SDF, 1 color glyph, 2 color glyph silhouette, 3 solid, 4 inner shadow, 5 color glyph halo,
//      6 decoration line: position along and across it, line style; 7 rounded box: position from the center).
// uv1: x = the SDF value of the edge, y = extra softness in SDF values, z = the SDF value where a band ends (0 = no end).
// uv2 (styled): paint position xy, gradient row, paint code (0 solid, 1-9 gradient, 10 texture).
// uv3 (styled): inner shadow: uv offset xy; z = the SDF value of the shadow's edge. Halo: the glyph's tile in the atlas.
//      Line: x = softness. Box: corner radii, top left, top right, bottom right, bottom left.
// For a line, uv1 is thickness, how far a layer grows it, its length. For a box, half its size.

float LetteraEdge(float d, float edge, float softness)
{
    float w = max(fwidth(d) * 0.75, softness) + 1e-5;
    return smoothstep(edge - w, edge + w, d);
}

// Without a paint or a band: the only shading the plain tier needs, and the shape shadow casters use.
LETTERA_COLOR4 LetteraShadePlain(float4 uv0, float2 params, LETTERA_COLOR4 color)
{
    float mode = uv0.w;
    if (mode < 0.5 || (mode > 3.5 && mode < 4.5))
    {
        float d = LETTERA_SAMPLE_SDF(uv0.xy, uv0.z).r;
        color.a *= LetteraEdge(d, params.x, params.y);
        return color;
    }
    if (mode < 2.5)
    {
        LETTERA_COLOR4 sampled = LETTERA_SAMPLE_COLOR(uv0.xy, uv0.z);
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

#ifdef LETTERA_STYLED

// Interleaved gradient noise, so slow gradients do not show bands at 8 bits.
float LetteraDither(float2 screen)
{
    return frac(52.9829189 * frac(dot(screen, float2(0.06711056, 0.00583715)))) - 0.5;
}

float LetteraRampPosition(float2 position, int shape, int edge)
{
    float t = position.x;
    if (shape == 1) t = length(position);
    else if (shape == 2) return atan2(position.y, position.x) / 6.28318531 + 0.5;
    if (edge == 1) return frac(t);
    if (edge == 2) return 1 - abs(frac(t * 0.5) * 2 - 1);
    return t;
}

LETTERA_COLOR4 LetteraApplyPaint(float4 paint, LETTERA_COLOR4 color, float2 screen)
{
    if (paint.w < 0.5) return color;
    if (paint.w > 9.5) return color * LETTERA_SAMPLE_PAINT(paint.xy);
    int code = (int)(paint.w + 0.5) - 1;
    int shape = code / 3;
    int edge = code - shape * 3;
    LETTERA_COLOR4 ramp = LETTERA_SAMPLE_RAMP(float2(saturate(LetteraRampPosition(paint.xy, shape, edge)) * 0.99609375 + 0.001953125, paint.z));
    ramp += LetteraDither(screen) / 255.0;
    return color * ramp;
}

// The color glyph's alpha averaged around a point, reaching zero about one radius outside the glyph.
float LetteraHalo(float2 uv, float page, float2 radius, float4 tile, float2 screen)
{
    // A different turn at every pixel trades the taps' star pattern for fine noise.
    float turn = LetteraDither(screen) * 0.52359878;
    float sum = 0;
    for (int ring = 1; ring <= 3; ring++)
    {
        for (int i = 0; i < 12; i++)
        {
            float angle = (i + ring * 0.33) * 0.52359878 + turn;
            float2 at = uv + float2(cos(angle), sin(angle)) * radius * ring * 0.33333;
            float inside = step(tile.x, at.x) * step(tile.y, at.y) * step(at.x, tile.z) * step(at.y, tile.w);
            sum += LETTERA_SAMPLE_COLOR(at, page).a * inside;
        }
    }
    return saturate(sum / 18.0);
}

// Distance to a decoration line, negative inside. Every style is drawn from its formula, so it stays smooth at any zoom.
float LetteraLineDistance(float along, float across, float style, float thickness, float span)
{
    float halfWidth = thickness * 0.5;
    float inner = max(max(-along, along - span), 0);
    if (style < 0.5) return max(abs(across) - halfWidth, inner);
    if (style < 1.5) return max(abs(abs(across) - thickness) - halfWidth, inner);
    if (style < 2.5)
    {
        // Round dots, each centered in its period.
        float period = thickness * 2.0;
        float at = along - period * floor(along / period) - period * 0.5;
        return max(length(float2(at, across)) - halfWidth, inner);
    }
    if (style < 3.5)
    {
        float period = thickness * 6.0;
        float at = along - period * floor(along / period) - period * 0.5;
        return max(max(abs(at) - thickness * 2.0, abs(across) - halfWidth), inner);
    }
    // A sine wave: its height over its slope gives the distance to the curve.
    float wavelength = thickness * 6.0;
    float amplitude = thickness * 1.5;
    float k = 6.28318531 / wavelength;
    float wave = across - amplitude * sin(along * k);
    float slope = amplitude * k * cos(along * k);
    return max(abs(wave) / sqrt(1.0 + slope * slope) - halfWidth, inner);
}

// Distance to a box whose corners have their own radii.
float LetteraBoxDistance(float2 p, float2 size, float4 radii)
{
    float r = p.x < 0 ? (p.y > 0 ? radii.x : radii.w) : (p.y > 0 ? radii.y : radii.z);
    float2 q = abs(p) - size + r;
    return length(max(q, 0.0)) + min(max(q.x, q.y), 0.0) - r;
}

LETTERA_COLOR4 LetteraShadeStyled(float4 uv0, float3 params, LETTERA_COLOR4 color, float4 paint, float4 extra, float2 screen)
{
    float mode = uv0.w;
    if (mode > 6.5)
    {
        float d = LetteraBoxDistance(uv0.xy, params.xy, extra);
        float w = length(fwidth(uv0.xy)) * 0.7 + 1e-5;
        color.a *= saturate(0.5 - d / w);
        return LetteraApplyPaint(paint, color, screen);
    }
    if (mode > 5.5)
    {
        float d = LetteraLineDistance(uv0.x, uv0.y, uv0.z, params.x, params.z) - params.y;
        float w = length(fwidth(uv0.xy)) * 0.7 + 1e-5;
        color.a *= extra.x > 0 ? 1.0 - smoothstep(0.0, extra.x, d) : saturate(0.5 - d / w);
        return LetteraApplyPaint(paint, color, screen);
    }
    if (mode > 4.5)
    {
        color.a *= LetteraHalo(uv0.xy, uv0.z, params.xy, extra, screen);
        return LetteraApplyPaint(paint, color, screen);
    }
    if (mode < 0.5)
    {
        float d = LETTERA_SAMPLE_SDF(uv0.xy, uv0.z).r;
        float alpha = LetteraEdge(d, params.x, params.y);
        if (params.z > 0) alpha *= 1 - LetteraEdge(d, params.z, params.y);
        color.a *= alpha;
        return LetteraApplyPaint(paint, color, screen);
    }
    if (mode > 3.5)
    {
        // Inside the face, where the face moved by the offset does not reach.
        float d = LETTERA_SAMPLE_SDF(uv0.xy, uv0.z).r;
        float moved = LETTERA_SAMPLE_SDF(uv0.xy - extra.xy, uv0.z).r;
        color.a *= LetteraEdge(d, params.x, 0) * (1 - LetteraEdge(moved, extra.z, params.y));
        return LetteraApplyPaint(paint, color, screen);
    }
    if (mode < 1.5) return LetteraShadePlain(uv0, params.xy, color);
    if (mode < 2.5)
    {
        color.a *= LETTERA_SAMPLE_COLOR(uv0.xy, uv0.z).a;
        return LetteraApplyPaint(paint, color, screen);
    }
    return LetteraApplyPaint(paint, color, screen);
}

#endif
#endif

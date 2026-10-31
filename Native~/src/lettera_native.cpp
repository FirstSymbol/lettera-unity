// Lettera native layer: a thin C ABI over HarfBuzz.
// Only lt_* symbols are exported; HarfBuzz itself is compiled with hidden visibility.
//
// Units: every font created here uses scale == upem, so positions, advances,
// extents and outlines are reported in font design units. The managed side scales.

#include "hb.h"
#include "hb-ot.h"
#ifndef LT_NO_PAINT
#include "hb-raster.h"
#endif
#include <string.h>
#include <stdint.h>

#if defined(_WIN32)
#define LT_API extern "C" __declspec(dllexport)
#else
#define LT_API extern "C" __attribute__((visibility("default")))
#endif

#define LT_VERSION 2

struct LtGlyph
{
    uint32_t glyph;
    uint32_t cluster;
    int32_t xAdvance;
    int32_t yAdvance;
    int32_t xOffset;
    int32_t yOffset;
    uint32_t flags;
};

struct LtFeature
{
    uint32_t tag;
    uint32_t value;
    uint32_t start;
    uint32_t end;
};

LT_API int lt_version() { return LT_VERSION; }

LT_API const char* lt_harfbuzz_version() { return hb_version_string(); }

// ---------------------------------------------------------------- faces

LT_API int lt_face_count(const void* data, int length)
{
    hb_blob_t* blob = hb_blob_create((const char*)data, (unsigned)length, HB_MEMORY_MODE_READONLY, nullptr, nullptr);
    unsigned count = hb_face_count(blob);
    hb_blob_destroy(blob);
    return (int)count;
}

// The font bytes are copied, so the caller may release its buffer right away.
LT_API hb_face_t* lt_face_create(const void* data, int length, int index)
{
    hb_blob_t* blob = hb_blob_create((const char*)data, (unsigned)length, HB_MEMORY_MODE_DUPLICATE, nullptr, nullptr);
    hb_face_t* face = hb_face_create(blob, (unsigned)index);
    hb_blob_destroy(blob);
    if (hb_face_get_glyph_count(face) == 0)
    {
        hb_face_destroy(face);
        return nullptr;
    }
    hb_face_make_immutable(face);
    return face;
}

// Number of faces in a font file (collections hold several). 0 when it cannot be read.
LT_API int lt_file_face_count(const char* utf8Path)
{
    hb_blob_t* blob = hb_blob_create_from_file_or_fail(utf8Path);
    if (!blob) return 0;
    unsigned count = hb_face_count(blob);
    hb_blob_destroy(blob);
    return (int)count;
}

// Maps the file where the OS allows it, so a large system font costs no heap.
LT_API hb_face_t* lt_face_create_from_file(const char* utf8Path, int index)
{
    hb_blob_t* blob = hb_blob_create_from_file_or_fail(utf8Path);
    if (!blob) return nullptr;
    hb_face_t* face = hb_face_create(blob, (unsigned)index);
    hb_blob_destroy(blob);
    if (hb_face_get_glyph_count(face) == 0)
    {
        hb_face_destroy(face);
        return nullptr;
    }
    hb_face_make_immutable(face);
    return face;
}

LT_API void lt_face_destroy(hb_face_t* face) { hb_face_destroy(face); }

LT_API int lt_face_upem(hb_face_t* face) { return (int)hb_face_get_upem(face); }

LT_API int lt_face_glyph_count(hb_face_t* face) { return (int)hb_face_get_glyph_count(face); }

// Writes up to `capacity` codepoints and returns the total number the face maps.
LT_API int lt_face_collect_unicodes(hb_face_t* face, uint32_t* output, int capacity)
{
    hb_set_t* set = hb_set_create();
    hb_face_collect_unicodes(face, set);
    int total = (int)hb_set_get_population(set);
    if (output && capacity > 0)
    {
        hb_codepoint_t cp = HB_SET_VALUE_INVALID;
        int i = 0;
        while (i < capacity && hb_set_next(set, &cp))
            output[i++] = cp;
    }
    hb_set_destroy(set);
    return total;
}

// Copies a name-table entry as UTF-16. Returns the full length in code units.
LT_API int lt_face_name(hb_face_t* face, int nameId, uint16_t* output, int capacity)
{
    unsigned size = capacity > 0 ? (unsigned)capacity : 0;
    unsigned length = hb_ot_name_get_utf16(face, (hb_ot_name_id_t)nameId, hb_language_from_string("en", -1), &size, output);
    if (length == 0)
    {
        size = capacity > 0 ? (unsigned)capacity : 0;
        length = hb_ot_name_get_utf16(face, (hb_ot_name_id_t)nameId, HB_LANGUAGE_INVALID, &size, output);
    }
    return (int)length;
}

// Bit 0: COLR layers, 1: COLRv1 paint, 2: PNG strikes (sbix/CBDT), 3: SVG, 4: MATH table.
LT_API int lt_face_color_flags(hb_face_t* face)
{
    int flags = 0;
    if (hb_ot_color_has_layers(face)) flags |= 1;
    if (hb_ot_color_has_paint(face)) flags |= 2;
    if (hb_ot_color_has_png(face)) flags |= 4;
    if (hb_ot_color_has_svg(face)) flags |= 8;
    if (hb_ot_math_has_data(face)) flags |= 16;
    return flags;
}

// Variation axes: one tag and min, default, max (3 floats) per axis. Returns the axis count.
LT_API int lt_face_axes(hb_face_t* face, uint32_t* tags, float* ranges, int capacity)
{
    unsigned total = hb_ot_var_get_axis_count(face);
    if (tags && ranges && capacity > 0)
    {
        hb_ot_var_axis_info_t infos[64];
        unsigned count = capacity < 64 ? (unsigned)capacity : 64;
        hb_ot_var_get_axis_infos(face, 0, &count, infos);
        for (unsigned i = 0; i < count; i++)
        {
            tags[i] = infos[i].tag;
            ranges[i * 3 + 0] = infos[i].min_value;
            ranges[i * 3 + 1] = infos[i].default_value;
            ranges[i * 3 + 2] = infos[i].max_value;
        }
    }
    return (int)total;
}

// ---------------------------------------------------------------- fonts

LT_API hb_font_t* lt_font_create(hb_face_t* face)
{
    hb_font_t* font = hb_font_create(face);
    int upem = (int)hb_face_get_upem(face);
    hb_font_set_scale(font, upem, upem);
    return font;
}

LT_API void lt_font_destroy(hb_font_t* font) { hb_font_destroy(font); }

// Must be called before the font is shared between threads.
LT_API void lt_font_set_variations(hb_font_t* font, const uint32_t* tags, const float* values, int count)
{
    hb_variation_t vars[64];
    if (count > 64) count = 64;
    for (int i = 0; i < count; i++)
    {
        vars[i].tag = tags[i];
        vars[i].value = values[i];
    }
    hb_font_set_variations(font, vars, (unsigned)count);
}

LT_API void lt_font_set_synthetic(hb_font_t* font, float embolden, float slant)
{
    hb_font_set_synthetic_bold(font, embolden, embolden, false);
    hb_font_set_synthetic_slant(font, slant);
}

// Selects the bitmap strike used for PNG glyphs.
LT_API void lt_font_set_ppem(hb_font_t* font, int ppem) { hb_font_set_ppem(font, (unsigned)ppem, (unsigned)ppem); }

LT_API void lt_font_make_immutable(hb_font_t* font) { hb_font_make_immutable(font); }

// ascender, descender, lineGap (horizontal).
LT_API void lt_font_extents(hb_font_t* font, int32_t* output)
{
    hb_font_extents_t e;
    hb_font_get_h_extents(font, &e);
    output[0] = e.ascender;
    output[1] = e.descender;
    output[2] = e.line_gap;
}

// Returns 1 and writes the value when the font provides the metric (an hb_ot_metrics_tag_t).
LT_API int lt_font_metric(hb_font_t* font, uint32_t tag, int32_t* value)
{
    hb_position_t position = 0;
    hb_bool_t ok = hb_ot_metrics_get_position(font, (hb_ot_metrics_tag_t)tag, &position);
    *value = position;
    return ok ? 1 : 0;
}

// Style values from the font's own tables: tag 'wght' gives the CSS weight, 'slnt'/'ital' the slope.
LT_API float lt_font_style(hb_font_t* font, uint32_t tag)
{
    return hb_style_get_value(font, (hb_style_tag_t)tag);
}

LT_API uint32_t lt_font_glyph(hb_font_t* font, uint32_t codepoint, uint32_t selector)
{
    hb_codepoint_t glyph = 0;
    if (selector != 0)
    {
        if (hb_font_get_variation_glyph(font, codepoint, selector, &glyph)) return glyph;
    }
    return hb_font_get_nominal_glyph(font, codepoint, &glyph) ? glyph : 0;
}

// Writes 1/0 per codepoint. Returns the number of covered codepoints.
LT_API int lt_font_coverage(hb_font_t* font, const uint32_t* codepoints, int count, uint8_t* output)
{
    int covered = 0;
    for (int i = 0; i < count; i++)
    {
        hb_codepoint_t glyph;
        bool has = hb_font_get_nominal_glyph(font, codepoints[i], &glyph);
        output[i] = has ? 1 : 0;
        covered += has;
    }
    return covered;
}

LT_API int lt_glyph_advance(hb_font_t* font, uint32_t glyph) { return hb_font_get_glyph_h_advance(font, glyph); }

// xBearing, yBearing, width, height. Returns 0 when the glyph has no extents.
LT_API int lt_glyph_extents(hb_font_t* font, uint32_t glyph, int32_t* output)
{
    hb_glyph_extents_t e;
    if (!hb_font_get_glyph_extents(font, glyph, &e))
    {
        output[0] = output[1] = output[2] = output[3] = 0;
        return 0;
    }
    output[0] = e.x_bearing;
    output[1] = e.y_bearing;
    output[2] = e.width;
    output[3] = e.height;
    return 1;
}

// ---------------------------------------------------------------- shaping

static thread_local hb_buffer_t* t_buffer = nullptr;

static hb_buffer_t* shared_buffer()
{
    if (!t_buffer) t_buffer = hb_buffer_create();
    hb_buffer_clear_contents(t_buffer);
    return t_buffer;
}

// direction: 0 = auto, 4 = LTR, 5 = RTL, 6 = TTB, 7 = BTT (hb_direction_t values).
// script: OpenType/ISO-15924 tag as hb_script_t, 0 = guess.
// Clusters are indices into `text` (UTF-32). Context outside [start, start+length) is used for shaping.
// Returns the glyph count; when it exceeds `capacity`, returns -count and writes nothing.
static int shape_with_flags(hb_font_t* font, const uint32_t* text, int textLength, int start, int length,
                            int direction, uint32_t script, const char* language,
                            const LtFeature* features, int featureCount, LtGlyph* output, int capacity, hb_buffer_flags_t flags)
{
    hb_buffer_t* buffer = shared_buffer();
    hb_buffer_set_flags(buffer, flags);
    hb_buffer_set_cluster_level(buffer, HB_BUFFER_CLUSTER_LEVEL_MONOTONE_CHARACTERS);
    hb_buffer_add_utf32(buffer, text, textLength, (unsigned)start, length);
    if (direction != 0) hb_buffer_set_direction(buffer, (hb_direction_t)direction);
    if (script != 0) hb_buffer_set_script(buffer, (hb_script_t)script);
    if (language && language[0]) hb_buffer_set_language(buffer, hb_language_from_string(language, -1));
    hb_buffer_guess_segment_properties(buffer);

    hb_feature_t feats[64];
    if (featureCount > 64) featureCount = 64;
    for (int i = 0; i < featureCount; i++)
    {
        feats[i].tag = features[i].tag;
        feats[i].value = features[i].value;
        feats[i].start = features[i].start;
        feats[i].end = features[i].end;
    }
    hb_shape(font, buffer, feats, (unsigned)featureCount);

    unsigned count = 0;
    hb_glyph_info_t* infos = hb_buffer_get_glyph_infos(buffer, &count);
    hb_glyph_position_t* positions = hb_buffer_get_glyph_positions(buffer, nullptr);
    if ((int)count > capacity) return -(int)count;
    for (unsigned i = 0; i < count; i++)
    {
        output[i].glyph = infos[i].codepoint;
        output[i].cluster = infos[i].cluster;
        output[i].xAdvance = positions[i].x_advance;
        output[i].yAdvance = positions[i].y_advance;
        output[i].xOffset = positions[i].x_offset;
        output[i].yOffset = positions[i].y_offset;
        output[i].flags = hb_glyph_info_get_glyph_flags(&infos[i]);
    }
    return (int)count;
}

LT_API int lt_shape(hb_font_t* font, const uint32_t* text, int textLength, int start, int length,
                    int direction, uint32_t script, const char* language,
                    const LtFeature* features, int featureCount, LtGlyph* output, int capacity)
{
    return shape_with_flags(font, text, textLength, start, length, direction, script, language, features, featureCount, output, capacity, HB_BUFFER_FLAG_DEFAULT);
}

// The same, also saying where a run may be cut and joined again with no reshaping. That costs something, so plain shaping does not ask.
LT_API int lt_shape_flagged(hb_font_t* font, const uint32_t* text, int textLength, int start, int length,
                            int direction, uint32_t script, const char* language,
                            const LtFeature* features, int featureCount, LtGlyph* output, int capacity)
{
    return shape_with_flags(font, text, textLength, start, length, direction, script, language, features, featureCount, output, capacity, HB_BUFFER_FLAG_PRODUCE_UNSAFE_TO_CONCAT);
}

// ---------------------------------------------------------------- COLRv1

// 0 where this build has no raster code, so the caller falls through to what else the font offers.
LT_API int lt_glyph_has_paint(hb_face_t* face, uint32_t glyph)
{
#ifdef LT_NO_PAINT
    (void)face; (void)glyph;
    return 0;
#else
    return hb_ot_color_glyph_has_paint(face, glyph) ? 1 : 0;
#endif
}

// box: min x, min y, max x, max y in design units, y up. Returns 0 when the glyph has no extents.
LT_API int lt_glyph_paint_extents(hb_font_t* font, uint32_t glyph, float* box)
{
    hb_glyph_extents_t extents;
    if (!hb_font_get_glyph_extents(font, glyph, &extents)) return 0;
    if (extents.width == 0 || extents.height == 0) return 0;
    float x0 = (float)extents.x_bearing, x1 = (float)(extents.x_bearing + extents.width);
    float y0 = (float)extents.y_bearing, y1 = (float)(extents.y_bearing + extents.height);
    box[0] = x0 < x1 ? x0 : x1;
    box[1] = y0 < y1 ? y0 : y1;
    box[2] = x0 < x1 ? x1 : x0;
    box[3] = y0 < y1 ? y1 : y0;
    return 1;
}

// Paints one glyph into straight-alpha RGBA, the bottom row first, as a texture holds it. (originX, originY) is the image's bottom left
// corner in pixels of glyph space at this scale. Foreground is an hb_color_t, 0xBBGGRRAA. Returns 0 on failure with nothing written.
LT_API int lt_glyph_paint_rgba(hb_font_t* font, uint32_t glyph, float scale, int palette, uint32_t foreground,
                               int originX, int originY, uint8_t* rgba, int width, int height)
{
#ifdef LT_NO_PAINT
    (void)font; (void)glyph; (void)scale; (void)palette; (void)foreground; (void)originX; (void)originY; (void)rgba; (void)width; (void)height;
    return 0;
#else
    if (width <= 0 || height <= 0 || width > 4096 || height > 4096) return 0;
    hb_raster_paint_t* paint = hb_raster_paint_create_or_fail();
    if (!paint) return 0;
    hb_raster_paint_set_transform(paint, scale, 0, 0, scale, 0, 0);
    hb_raster_extents_t extents;
    extents.x_origin = originX;
    extents.y_origin = originY;
    extents.width = (unsigned)width;
    extents.height = (unsigned)height;
    extents.stride = 0;
    hb_raster_paint_set_extents(paint, &extents);
    hb_raster_paint_set_palette(paint, (unsigned)palette);
    hb_raster_paint_set_foreground(paint, (hb_color_t)foreground);
    int done = 0;
    if (hb_raster_paint_glyph_or_fail(paint, font, glyph))
    {
        hb_raster_image_t* image = hb_raster_paint_render(paint);
        if (image)
        {
            const uint8_t* source = hb_raster_image_get_buffer(image);
            hb_raster_extents_t got;
            hb_raster_image_get_extents(image, &got);
            if (source && hb_raster_image_get_format(image) == HB_RASTER_FORMAT_BGRA32 && (int)got.width == width && (int)got.height == height)
            {
                for (int y = 0; y < height; y++)
                {
                    const uint8_t* row = source + (size_t)y * got.stride;
                    uint8_t* out = rgba + (size_t)y * width * 4;
                    for (int x = 0; x < width; x++, row += 4, out += 4)
                    {
                        unsigned a = row[3];
                        if (a == 0) { out[0] = out[1] = out[2] = out[3] = 0; continue; }
                        // It holds premultiplied color, and the atlas does not.
                        out[0] = (uint8_t)((row[2] * 255u + a / 2) / a);
                        out[1] = (uint8_t)((row[1] * 255u + a / 2) / a);
                        out[2] = (uint8_t)((row[0] * 255u + a / 2) / a);
                        out[3] = (uint8_t)a;
                    }
                }
                done = 1;
            }
            hb_raster_paint_recycle_image(paint, image);
        }
    }
    hb_raster_paint_destroy(paint);
    return done;
#endif
}

// ---------------------------------------------------------------- outlines

// Command stream: op followed by its points.
//   0 move x y | 1 line x y | 2 quad cx cy x y | 3 cubic c1x c1y c2x c2y x y | 4 close
struct OutlineSink
{
    float* data;
    int capacity;
    int count;

    void push(float v)
    {
        if (count < capacity) data[count] = v;
        count++;
    }
};

static void move_to(hb_draw_funcs_t*, void* d, hb_draw_state_t*, float x, float y, void*)
{
    OutlineSink* s = (OutlineSink*)d;
    s->push(0); s->push(x); s->push(y);
}

static void line_to(hb_draw_funcs_t*, void* d, hb_draw_state_t*, float x, float y, void*)
{
    OutlineSink* s = (OutlineSink*)d;
    s->push(1); s->push(x); s->push(y);
}

static void quad_to(hb_draw_funcs_t*, void* d, hb_draw_state_t*, float cx, float cy, float x, float y, void*)
{
    OutlineSink* s = (OutlineSink*)d;
    s->push(2); s->push(cx); s->push(cy); s->push(x); s->push(y);
}

static void cubic_to(hb_draw_funcs_t*, void* d, hb_draw_state_t*, float c1x, float c1y, float c2x, float c2y, float x, float y, void*)
{
    OutlineSink* s = (OutlineSink*)d;
    s->push(3); s->push(c1x); s->push(c1y); s->push(c2x); s->push(c2y); s->push(x); s->push(y);
}

static void close_path(hb_draw_funcs_t*, void* d, hb_draw_state_t*, void*)
{
    OutlineSink* s = (OutlineSink*)d;
    s->push(4);
}

static hb_draw_funcs_t* draw_funcs()
{
    static hb_draw_funcs_t* funcs = []
    {
        hb_draw_funcs_t* f = hb_draw_funcs_create();
        hb_draw_funcs_set_move_to_func(f, move_to, nullptr, nullptr);
        hb_draw_funcs_set_line_to_func(f, line_to, nullptr, nullptr);
        hb_draw_funcs_set_quadratic_to_func(f, quad_to, nullptr, nullptr);
        hb_draw_funcs_set_cubic_to_func(f, cubic_to, nullptr, nullptr);
        hb_draw_funcs_set_close_path_func(f, close_path, nullptr, nullptr);
        hb_draw_funcs_make_immutable(f);
        return f;
    }();
    return funcs;
}

// Returns the number of floats the outline needs; data beyond `capacity` is dropped.
LT_API int lt_glyph_outline(hb_font_t* font, uint32_t glyph, float* output, int capacity)
{
    OutlineSink sink { output, output ? capacity : 0, 0 };
    hb_font_draw_glyph(font, glyph, draw_funcs(), &sink);
    return sink.count;
}

// ---------------------------------------------------------------- color

// COLRv0 layers: glyph id + palette index (0xFFFF = foreground color). Returns the total layer count.
LT_API int lt_glyph_color_layers(hb_face_t* face, uint32_t glyph, uint32_t* glyphs, uint32_t* colorIndices, int capacity)
{
    unsigned total = hb_ot_color_glyph_get_layers(face, glyph, 0, nullptr, nullptr);
    if (glyphs && capacity > 0)
    {
        hb_ot_color_layer_t layers[256];
        unsigned count = capacity < 256 ? (unsigned)capacity : 256;
        hb_ot_color_glyph_get_layers(face, glyph, 0, &count, layers);
        for (unsigned i = 0; i < count; i++)
        {
            glyphs[i] = layers[i].glyph;
            colorIndices[i] = layers[i].color_index;
        }
    }
    return (int)total;
}

// Colors as 0xAARRGGBB. Returns the total palette size; emoji fonts have thousands of entries.
LT_API int lt_palette_colors(hb_face_t* face, int palette, uint32_t* output, int capacity)
{
    unsigned total = hb_ot_color_palette_get_colors(face, (unsigned)palette, 0, nullptr, nullptr);
    if (output && capacity > 0)
    {
        unsigned count = (unsigned)capacity;
        hb_ot_color_palette_get_colors(face, (unsigned)palette, 0, &count, (hb_color_t*)output);
        for (unsigned i = 0; i < count; i++)
        {
            hb_color_t c = output[i]; // BGRA
            output[i] = ((uint32_t)hb_color_get_alpha(c) << 24) | ((uint32_t)hb_color_get_red(c) << 16) |
                        ((uint32_t)hb_color_get_green(c) << 8) | (uint32_t)hb_color_get_blue(c);
        }
    }
    return (int)total;
}

// Copies the PNG for the strike selected by lt_font_set_ppem. Returns the PNG byte length (0 = none).
LT_API int lt_glyph_png(hb_font_t* font, uint32_t glyph, uint8_t* output, int capacity)
{
    hb_blob_t* blob = hb_ot_color_glyph_reference_png(font, glyph);
    unsigned length = 0;
    const char* data = hb_blob_get_data(blob, &length);
    if (output && (int)length <= capacity && length > 0)
        memcpy(output, data, length);
    hb_blob_destroy(blob);
    return (int)length;
}

// Lettera's editor-only font tool: a thin C ABI over HarfBuzz's subsetter. Nothing here reaches a player.

#include "hb.h"
#include "hb-subset.h"
#include <stdint.h>
#include <stdlib.h>
#include <string.h>

#if defined(_WIN32)
#define LTS_API extern "C" __declspec(dllexport)
#else
#define LTS_API extern "C" __attribute__((visibility("default")))
#endif

enum
{
    LTS_KEEP_FEATURES = 1,
    LTS_KEEP_NAMES = 2,
    LTS_KEEP_GLYPH_NAMES = 4,
    LTS_KEEP_NOTDEF_OUTLINE = 8,
};

LTS_API int lts_version() { return 1; }

// Everything, rather than a default set that would quietly drop what a project asked for by name.
static void keep_all(hb_subset_input_t* input, hb_subset_sets_t which)
{
    hb_set_t* set = hb_subset_input_set(input, which);
    hb_set_clear(set);
    hb_set_invert(set);
}

// Keeps the given characters. Returns a new font the caller frees with lts_free, and null when nothing came of it.
LTS_API void* lts_subset(const void* data, int length, int faceIndex, const uint32_t* unicodes, int count, int options, int* outLength)
{
    *outLength = 0;
    hb_blob_t* blob = hb_blob_create((const char*)data, (unsigned)length, HB_MEMORY_MODE_READONLY, nullptr, nullptr);
    hb_face_t* face = hb_face_create(blob, (unsigned)faceIndex);
    hb_blob_destroy(blob);

    void* result = nullptr;
    hb_subset_input_t* input = hb_subset_input_create_or_fail();
    if (input)
    {
        hb_set_t* set = hb_subset_input_unicode_set(input);
        for (int i = 0; i < count; i++) hb_set_add(set, unicodes[i]);
        if (options & LTS_KEEP_FEATURES)
        {
            keep_all(input, HB_SUBSET_SETS_LAYOUT_FEATURE_TAG);
            keep_all(input, HB_SUBSET_SETS_LAYOUT_SCRIPT_TAG);
        }
        if (options & LTS_KEEP_NAMES)
        {
            keep_all(input, HB_SUBSET_SETS_NAME_ID);
            keep_all(input, HB_SUBSET_SETS_NAME_LANG_ID);
        }
        unsigned flags = HB_SUBSET_FLAGS_DEFAULT;
        if (options & LTS_KEEP_NAMES) flags |= HB_SUBSET_FLAGS_NAME_LEGACY;
        if (options & LTS_KEEP_GLYPH_NAMES) flags |= HB_SUBSET_FLAGS_GLYPH_NAMES;
        if (options & LTS_KEEP_NOTDEF_OUTLINE) flags |= HB_SUBSET_FLAGS_NOTDEF_OUTLINE;
        hb_subset_input_set_flags(input, flags);

        hb_face_t* subset = hb_subset_or_fail(face, input);
        if (subset)
        {
            hb_blob_t* out = hb_face_reference_blob(subset);
            unsigned size = 0;
            const char* bytes = hb_blob_get_data(out, &size);
            if (bytes && size > 0)
            {
                result = malloc(size);
                if (result)
                {
                    memcpy(result, bytes, size);
                    *outLength = (int)size;
                }
            }
            hb_blob_destroy(out);
            hb_face_destroy(subset);
        }
        hb_subset_input_destroy(input);
    }
    hb_face_destroy(face);
    return result;
}

LTS_API void lts_free(void* memory) { free(memory); }

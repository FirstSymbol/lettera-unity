using System;
using System.Runtime.InteropServices;

namespace FirstSymbol.Lettera
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct NativeGlyph
    {
        public uint Glyph;
        public uint Cluster;
        public int XAdvance;
        public int YAdvance;
        public int XOffset;
        public int YOffset;
        public uint Flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct NativeFeature
    {
        public uint Tag;
        public uint Value;
        public uint Start;
        public uint End;
    }

    /// <summary>P/Invoke surface of the lettera_native plugin (Native~/src/lettera_native.cpp).</summary>
    internal static unsafe class LetteraNative
    {
#if !UNITY_EDITOR && (UNITY_IOS || UNITY_TVOS || UNITY_WEBGL)
        const string Lib = "__Internal";
#else
        const string Lib = "lettera_native";
#endif
        public const int ExpectedVersion = 1;

        [DllImport(Lib)] public static extern int lt_version();
        [DllImport(Lib)] public static extern int lt_face_count(byte* data, int length);
        [DllImport(Lib)] public static extern IntPtr lt_face_create(byte* data, int length, int index);
        [DllImport(Lib)] public static extern int lt_file_face_count(byte* utf8Path);
        [DllImport(Lib)] public static extern IntPtr lt_face_create_from_file(byte* utf8Path, int index);
        [DllImport(Lib)] public static extern void lt_face_destroy(IntPtr face);
        [DllImport(Lib)] public static extern int lt_face_upem(IntPtr face);
        [DllImport(Lib)] public static extern int lt_face_glyph_count(IntPtr face);
        [DllImport(Lib)] public static extern int lt_face_name(IntPtr face, int nameId, char* output, int capacity);
        [DllImport(Lib)] public static extern int lt_face_color_flags(IntPtr face);
        [DllImport(Lib)] public static extern int lt_face_axes(IntPtr face, uint* tags, float* ranges, int capacity);
        [DllImport(Lib)] public static extern IntPtr lt_font_create(IntPtr face);
        [DllImport(Lib)] public static extern void lt_font_destroy(IntPtr font);
        [DllImport(Lib)] public static extern void lt_font_set_variations(IntPtr font, uint* tags, float* values, int count);
        [DllImport(Lib)] public static extern void lt_font_set_synthetic(IntPtr font, float embolden, float slant);
        [DllImport(Lib)] public static extern void lt_font_set_ppem(IntPtr font, int ppem);
        [DllImport(Lib)] public static extern void lt_font_make_immutable(IntPtr font);
        [DllImport(Lib)] public static extern void lt_font_extents(IntPtr font, int* output);
        [DllImport(Lib)] public static extern int lt_font_metric(IntPtr font, uint tag, int* value);
        [DllImport(Lib)] public static extern float lt_font_style(IntPtr font, uint tag);
        [DllImport(Lib)] public static extern uint lt_font_glyph(IntPtr font, uint codepoint, uint selector);
        [DllImport(Lib)] public static extern int lt_glyph_advance(IntPtr font, uint glyph);
        [DllImport(Lib)] public static extern int lt_glyph_extents(IntPtr font, uint glyph, int* output);
        [DllImport(Lib)] public static extern int lt_shape(IntPtr font, int* text, int textLength, int start, int length,
            int direction, uint script, byte* language, NativeFeature* features, int featureCount, NativeGlyph* output, int capacity);
        [DllImport(Lib)] public static extern int lt_glyph_outline(IntPtr font, uint glyph, float* output, int capacity);
        [DllImport(Lib)] public static extern int lt_glyph_color_layers(IntPtr face, uint glyph, uint* glyphs, uint* colorIndices, int capacity);
        [DllImport(Lib)] public static extern int lt_palette_colors(IntPtr face, int palette, uint* output, int capacity);
        [DllImport(Lib)] public static extern int lt_glyph_png(IntPtr font, uint glyph, byte* output, int capacity);

        public static uint Tag(string s) =>
            ((uint)s[0] << 24) | ((uint)s[1] << 16) | ((uint)s[2] << 8) | s[3];
    }
}

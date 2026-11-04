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
    internal struct NativeMathPart
    {
        public uint Glyph;
        public int StartConnector;
        public int EndConnector;
        public int FullAdvance;
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
#if !UNITY_EDITOR && (UNITY_IOS || UNITY_TVOS || UNITY_VISIONOS || UNITY_WEBGL)
        const string Lib = "__Internal";
#else
        const string Lib = "lettera_native";
#endif
        /// <summary>The oldest library these scripts can run against: an editor keeps the one it loaded until it restarts.</summary>
        public const int MinimumVersion = 1;

        /// <summary>The library this package ships.</summary>
        public const int CurrentVersion = 4;

        static int s_Version;

        /// <summary>What the loaded library says it is. Every entry newer than version 1 is called only behind this.</summary>
        public static int Version => s_Version != 0 ? s_Version : s_Version = VersionOverride > 0 ? VersionOverride : lt_version();

        /// <summary>Makes the library look older, for a test.</summary>
        internal static int VersionOverride
        {
            get => s_VersionOverride;
            set
            {
                s_VersionOverride = value;
                s_Version = 0;
            }
        }

        static int s_VersionOverride;

        [DllImport(Lib)] public static extern int lt_version();
        [DllImport(Lib)] public static extern int lt_face_count(byte* data, int length);
        [DllImport(Lib)] public static extern IntPtr lt_face_create(byte* data, int length, int index);
        // Version 4.
        [DllImport(Lib)] public static extern IntPtr lt_face_create_borrowed(byte* data, int length, int index);
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
        [DllImport(Lib)] public static extern int lt_face_collect_unicodes(IntPtr face, uint* output, int capacity);

        // Version 3.
        [DllImport(Lib)] public static extern int lt_math_has_data(IntPtr face);
        [DllImport(Lib)] public static extern void lt_math_constants(IntPtr font, int* out56);
        [DllImport(Lib)] public static extern void lt_math_glyph_info(IntPtr font, uint glyph, int* out3);
        [DllImport(Lib)] public static extern int lt_math_kern(IntPtr font, uint glyph, int corner, int height);
        [DllImport(Lib)] public static extern int lt_math_variants(IntPtr font, uint glyph, int vertical, uint* glyphs, int* advances, int capacity);
        [DllImport(Lib)] public static extern int lt_math_assembly(IntPtr font, uint glyph, int vertical, NativeMathPart* parts, int capacity, int* italic);
        [DllImport(Lib)] public static extern int lt_math_min_overlap(IntPtr font, int vertical);

        // Version 2.
        [DllImport(Lib)] public static extern int lt_shape_flagged(IntPtr font, int* text, int textLength, int start, int length,
            int direction, uint script, byte* language, NativeFeature* features, int featureCount, NativeGlyph* output, int capacity);
        [DllImport(Lib)] public static extern int lt_glyph_has_paint(IntPtr face, uint glyph);
        [DllImport(Lib)] public static extern int lt_glyph_paint_extents(IntPtr font, uint glyph, float* box);
        [DllImport(Lib)] public static extern int lt_glyph_paint_rgba(IntPtr font, uint glyph, float scale, int palette, uint foreground, int originX, int originY, byte* rgba, int width, int height);

        public static uint Tag(string s) =>
            ((uint)s[0] << 24) | ((uint)s[1] << 16) | ((uint)s[2] << 8) | s[3];
    }
}

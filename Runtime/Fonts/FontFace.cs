using System;
using System.Collections.Generic;
using System.Text;

namespace FirstSymbol.Lettera
{
    /// <summary>Metrics of a face in font design units.</summary>
    internal struct FaceMetrics
    {
        public int Upem, Ascender, Descender, LineGap, CapHeight, XHeight;
        public int UnderlineOffset, UnderlineSize, StrikeOffset, StrikeSize;
    }

    /// <summary>A shaping font: a face at one variation and synthesis setting. Immutable, safe across threads.</summary>
    internal sealed class FontInstance
    {
        static int s_NextId = 1;

        public readonly int Id = s_NextId++;
        public readonly FontFace Face;
        public readonly IntPtr Font;

        public FontInstance(FontFace face, IntPtr font)
        {
            Face = face;
            Font = font;
        }
    }

    /// <summary>One loaded font face, from memory or from a mapped system file.</summary>
    internal sealed unsafe class FontFace : IDisposable
    {
        const int ColorStrikePpem = 128;
        static readonly List<FontFace> s_All = new List<FontFace>();

        readonly Dictionary<long, FontInstance> m_Instances = new Dictionary<long, FontInstance>();
        readonly List<IntPtr> m_Fonts = new List<IntPtr>();

        public IntPtr Face { get; private set; }
        public FontInstance Regular { get; private set; }
        public FaceMetrics Metrics { get; private set; }
        public string Family { get; private set; } = "";
        public string StyleName { get; private set; } = "";
        public int Weight { get; private set; } = 400;
        public bool IsItalic { get; private set; }
        public bool HasColorLayers { get; private set; }
        public bool HasColorBitmaps { get; private set; }
        public bool HasWeightAxis { get; private set; }
        public float WeightAxisMin { get; private set; }
        public float WeightAxisMax { get; private set; }
        public bool IsVirtual { get; private set; }
        public bool IsColor => HasColorLayers || HasColorBitmaps || IsVirtual;

        FontFace(IntPtr face)
        {
            Face = face;
            int flags = LetteraNative.lt_face_color_flags(face);
            HasColorLayers = (flags & 1) != 0;
            HasColorBitmaps = (flags & 4) != 0;
            Regular = CreateInstance(-1, 0, 0);
            ReadInfo();
            s_All.Add(this);
        }

        FontFace(int upem, int ascender, int descender)
        {
            IsVirtual = true;
            Family = "Browser Emoji";
            Regular = new FontInstance(this, IntPtr.Zero);
            Metrics = new FaceMetrics
            {
                Upem = upem, Ascender = ascender, Descender = descender, CapHeight = ascender, XHeight = upem / 2,
                UnderlineOffset = -upem / 10, UnderlineSize = upem / 20, StrikeOffset = upem / 4, StrikeSize = upem / 20,
            };
        }

        /// <summary>A face with no font behind it, whose glyphs come from elsewhere.</summary>
        public static FontFace CreateVirtual(int upem, int ascender, int descender) => new FontFace(upem, ascender, descender);

        public static FontFace FromBytes(byte[] bytes, int index)
        {
            if (bytes == null || bytes.Length == 0) return null;
            fixed (byte* p = bytes)
            {
                IntPtr face = LetteraNative.lt_face_create(p, bytes.Length, index);
                return face == IntPtr.Zero ? null : new FontFace(face);
            }
        }

        public static FontFace FromFile(string path, int index)
        {
            byte[] utf8 = NullTerminated(path);
            fixed (byte* p = utf8)
            {
                IntPtr face = LetteraNative.lt_face_create_from_file(p, index);
                return face == IntPtr.Zero ? null : new FontFace(face);
            }
        }

        public static int CountFaces(byte[] bytes)
        {
            fixed (byte* p = bytes) return LetteraNative.lt_face_count(p, bytes.Length);
        }

        public static byte[] NullTerminated(string s)
        {
            int count = Encoding.UTF8.GetByteCount(s);
            var bytes = new byte[count + 1];
            Encoding.UTF8.GetBytes(s, 0, s.Length, bytes, 0);
            return bytes;
        }

        uint[] m_Palette;

        /// <summary>The first color palette as 0xAARRGGBB.</summary>
        public uint[] Palette
        {
            get
            {
                if (m_Palette != null) return m_Palette;
                int size = LetteraNative.lt_palette_colors(Face, 0, null, 0);
                m_Palette = new uint[size];
                if (size > 0)
                    fixed (uint* p = m_Palette)
                        LetteraNative.lt_palette_colors(Face, 0, p, size);
                return m_Palette;
            }
        }

        public bool HasGlyph(int codepoint) => IsVirtual || LetteraNative.lt_font_glyph(Regular.Font, (uint)codepoint, 0) != 0;

        /// <summary>Returns the instance for a weight axis value (-1 = default) and synthesis amounts.</summary>
        public FontInstance GetInstance(float weightAxis, float embolden, float slant)
        {
            if (IsVirtual || (weightAxis < 0 && embolden == 0 && slant == 0)) return Regular;
            long key = ((long)(int)(weightAxis * 8) << 40) ^ ((long)(int)(embolden * 4096) << 20) ^ (int)(slant * 4096);
            if (!m_Instances.TryGetValue(key, out var instance))
            {
                instance = CreateInstance(weightAxis, embolden, slant);
                m_Instances[key] = instance;
            }
            return instance;
        }

        FontInstance CreateInstance(float weightAxis, float embolden, float slant)
        {
            IntPtr font = LetteraNative.lt_font_create(Face);
            if (weightAxis >= 0)
            {
                uint tag = LetteraNative.Tag("wght");
                LetteraNative.lt_font_set_variations(font, &tag, &weightAxis, 1);
            }
            if (embolden != 0 || slant != 0) LetteraNative.lt_font_set_synthetic(font, embolden, slant);
            if (HasColorBitmaps) LetteraNative.lt_font_set_ppem(font, ColorStrikePpem);
            LetteraNative.lt_font_make_immutable(font);
            m_Fonts.Add(font);
            return new FontInstance(this, font);
        }

        void ReadInfo()
        {
            Family = ReadName(16);
            if (Family.Length == 0) Family = ReadName(1);
            StyleName = ReadName(17);
            if (StyleName.Length == 0) StyleName = ReadName(2);

            IntPtr font = Regular.Font;
            float weight = LetteraNative.lt_font_style(font, LetteraNative.Tag("wght"));
            Weight = weight > 0 ? (int)weight : 400;
            IsItalic = LetteraNative.lt_font_style(font, LetteraNative.Tag("ital")) > 0.5f ||
                       StyleName.IndexOf("Italic", StringComparison.OrdinalIgnoreCase) >= 0 ||
                       StyleName.IndexOf("Oblique", StringComparison.OrdinalIgnoreCase) >= 0;

            uint* tags = stackalloc uint[32];
            float* ranges = stackalloc float[96];
            int axes = LetteraNative.lt_face_axes(Face, tags, ranges, 32);
            uint wght = LetteraNative.Tag("wght");
            for (int i = 0; i < axes && i < 32; i++)
            {
                if (tags[i] != wght) continue;
                HasWeightAxis = true;
                WeightAxisMin = ranges[i * 3];
                WeightAxisMax = ranges[i * 3 + 2];
            }

            var m = new FaceMetrics { Upem = LetteraNative.lt_face_upem(Face) };
            int* extents = stackalloc int[3];
            LetteraNative.lt_font_extents(font, extents);
            m.Ascender = extents[0];
            m.Descender = extents[1];
            m.LineGap = extents[2];
            m.CapHeight = Metric(font, "cpht");
            if (m.CapHeight <= 0) m.CapHeight = MeasureHeight('H', m.Ascender);
            m.XHeight = Metric(font, "xhgt");
            if (m.XHeight <= 0) m.XHeight = MeasureHeight('x', m.Upem / 2);
            m.UnderlineOffset = Metric(font, "undo");
            m.UnderlineSize = Metric(font, "unds");
            m.StrikeOffset = Metric(font, "stro");
            m.StrikeSize = Metric(font, "strs");
            if (m.UnderlineSize <= 0) m.UnderlineSize = Math.Max(1, m.Upem / 20);
            if (m.UnderlineOffset == 0) m.UnderlineOffset = -m.Upem / 10;
            if (m.StrikeSize <= 0) m.StrikeSize = m.UnderlineSize;
            if (m.StrikeOffset == 0) m.StrikeOffset = m.XHeight / 2;
            Metrics = m;
        }

        static int Metric(IntPtr font, string tag)
        {
            int value;
            return LetteraNative.lt_font_metric(font, LetteraNative.Tag(tag), &value) != 0 ? value : 0;
        }

        int MeasureHeight(int codepoint, int fallback)
        {
            uint glyph = LetteraNative.lt_font_glyph(Regular.Font, (uint)codepoint, 0);
            int* e = stackalloc int[4];
            return glyph != 0 && LetteraNative.lt_glyph_extents(Regular.Font, glyph, e) != 0 && e[1] > 0 ? e[1] : fallback;
        }

        string ReadName(int nameId)
        {
            char* buffer = stackalloc char[128];
            int length = LetteraNative.lt_face_name(Face, nameId, buffer, 128);
            return length <= 0 ? "" : new string(buffer, 0, Math.Min(length, 128));
        }

        public void Dispose()
        {
            if (Face == IntPtr.Zero) return;
            foreach (IntPtr font in m_Fonts) LetteraNative.lt_font_destroy(font);
            m_Fonts.Clear();
            m_Instances.Clear();
            LetteraNative.lt_face_destroy(Face);
            Face = IntPtr.Zero;
            s_All.Remove(this);
        }

        /// <summary>Releases every native face (domain reload, tests).</summary>
        public static void DisposeAll()
        {
            for (int i = s_All.Count - 1; i >= 0; i--) s_All[i].Dispose();
        }
    }
}

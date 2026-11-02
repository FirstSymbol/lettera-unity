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
        public int SuperSize, SuperOffset, SubSize, SubOffset;
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

        readonly Dictionary<(long, int), FontInstance> m_Instances = new Dictionary<(long, int), FontInstance>();
        readonly Dictionary<long, bool> m_FeatureProbes = new Dictionary<long, bool>();
        AxisSet m_Pinned;
        uint[] m_AxisTags = new uint[0];
        float[] m_AxisRanges = new float[0];
        readonly List<IntPtr> m_Fonts = new List<IntPtr>();

        public IntPtr Face { get; private set; }
        public FontInstance Regular { get; private set; }
        public FaceMetrics Metrics { get; private set; }

        /// <summary>What the file says, whatever an asset wrote over it.</summary>
        public FaceMetrics NaturalMetrics { get; private set; }

        /// <summary>Vertical lines by hand; a null keeps the file's.</summary>
        public void OverrideMetrics(int? ascender, int? descender, int? lineGap)
        {
            FaceMetrics metrics = NaturalMetrics;
            if (ascender.HasValue) metrics.Ascender = ascender.Value;
            if (descender.HasValue) metrics.Descender = descender.Value;
            if (lineGap.HasValue) metrics.LineGap = lineGap.Value;
            Metrics = metrics;
        }

        internal struct AxisInfo
        {
            public string Tag;
            public float Min, Default, Max;
        }

        /// <summary>The design axes this face has, for an inspector.</summary>
        public AxisInfo[] Axes
        {
            get
            {
                var axes = new AxisInfo[m_AxisTags.Length];
                for (int i = 0; i < axes.Length; i++)
                {
                    uint tag = m_AxisTags[i];
                    axes[i] = new AxisInfo { Tag = new string(new[] { (char)(tag >> 24), (char)((tag >> 16) & 255), (char)((tag >> 8) & 255), (char)(tag & 255) }).TrimEnd(), Min = m_AxisRanges[i * 3], Default = m_AxisRanges[i * 3 + 1], Max = m_AxisRanges[i * 3 + 2] };
                }
                return axes;
            }
        }
        public string Family { get; private set; } = "";
        public string StyleName { get; private set; } = "";
        public int Weight { get; private set; } = 400;
        public bool IsItalic { get; private set; }
        public bool HasColorLayers { get; private set; }
        public bool HasColorBitmaps { get; private set; }

        /// <summary>The font holds COLRv1 glyphs. Whether a given glyph does is asked glyph by glyph.</summary>
        public bool HasColorPaint { get; private set; }
        public bool HasWeightAxis { get; private set; }
        public float WeightAxisMin { get; private set; }
        public float WeightAxisMax { get; private set; }
        public bool IsVirtual { get; private set; }
        public bool IsColor => HasColorLayers || HasColorBitmaps || HasColorPaint || IsVirtual;

        /// <summary>Where this face's bytes are.</summary>
        public LetteraFontSource Source { get; private set; } = LetteraFontSource.Virtual;

        public long RetainedBytes { get; private set; }
        public long MappedBytes { get; private set; }
        public int InstanceCount => m_Instances.Count + 1;
        public int LastUsedFrame { get; set; }
        bool m_WarnedInstances;

        internal static void CollectMemory(List<LetteraFontMemoryEntry> into)
        {
            foreach (FontFace face in s_All)
                into.Add(new LetteraFontMemoryEntry { Family = face.Family, Style = face.StyleName, Source = face.Source, RetainedBytes = face.RetainedBytes, MappedBytes = face.MappedBytes, InstanceCount = face.InstanceCount, LastUsedFrame = face.LastUsedFrame });
        }

        internal static void AddMemory(ref LetteraFontMemory.Sums sums)
        {
            foreach (FontFace face in s_All)
            {
                sums.Faces++;
                sums.Instances += face.InstanceCount;
                sums.RetainedBytes += face.RetainedBytes;
                sums.MappedBytes += face.MappedBytes;
            }
        }

        FontFace(IntPtr face)
        {
            Face = face;
            int flags = LetteraNative.lt_face_color_flags(face);
            HasColorLayers = (flags & 1) != 0;
            // An older library cannot paint these, so the font counts as what else it holds.
            HasColorPaint = (flags & 2) != 0 && LetteraNative.Version >= 2;
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
            NaturalMetrics = Metrics;
        }

        /// <summary>A face with no font behind it, whose glyphs come from elsewhere.</summary>
        public static FontFace CreateVirtual(int upem, int ascender, int descender) => new FontFace(upem, ascender, descender);

        // Held fixed for as long as the native face reads from the array, so the bytes exist once.
        System.Runtime.InteropServices.GCHandle m_Pin;

        public static FontFace FromBytes(byte[] bytes, int index, LetteraFontSource source = LetteraFontSource.InMemory)
        {
            if (bytes == null || bytes.Length == 0) return null;
            if (LetteraNative.Version >= 4)
            {
                var pin = System.Runtime.InteropServices.GCHandle.Alloc(bytes, System.Runtime.InteropServices.GCHandleType.Pinned);
                IntPtr borrowed = LetteraNative.lt_face_create_borrowed((byte*)pin.AddrOfPinnedObject(), bytes.Length, index);
                if (borrowed == IntPtr.Zero)
                {
                    pin.Free();
                    return null;
                }
                return new FontFace(borrowed) { Source = source, RetainedBytes = bytes.Length, m_Pin = pin };
            }
            fixed (byte* p = bytes)
            {
                // An older library copies them, whatever then happens to this array.
                IntPtr face = LetteraNative.lt_face_create(p, bytes.Length, index);
                if (face == IntPtr.Zero) return null;
                return new FontFace(face) { Source = source, RetainedBytes = bytes.Length };
            }
        }

        public static FontFace FromFile(string path, int index, LetteraFontSource source = LetteraFontSource.SystemMapped)
        {
            byte[] utf8 = NullTerminated(path);
            fixed (byte* p = utf8)
            {
                IntPtr face = LetteraNative.lt_face_create_from_file(p, index);
                if (face == IntPtr.Zero) return null;
                long length = 0;
                try
                {
                    length = new System.IO.FileInfo(path).Length;
                }
                catch (System.IO.IOException)
                {
                    // Only a figure: the face is open.
                }
                return new FontFace(face) { Source = source, MappedBytes = length };
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
        public FontInstance GetInstance(float weightAxis, float embolden, float slant, AxisSet axes = null)
        {
            if (IsVirtual || m_AxisTags.Length == 0) axes = null;
            if (IsVirtual || (weightAxis < 0 && embolden == 0 && slant == 0 && axes == null)) return Regular;
            var key = (((long)(int)(weightAxis * 8) << 40) ^ ((long)(int)(embolden * 4096) << 20) ^ (int)(slant * 4096), axes?.Id ?? 0);
            if (!m_Instances.TryGetValue(key, out var instance))
            {
                instance = CreateInstance(weightAxis, embolden, slant, axes);
                m_Instances[key] = instance;
                int limit = LetteraSettings.Instance.FontMemory.MaxInstancesPerFace;
                if (!m_WarnedInstances && limit > 0 && InstanceCount > limit)
                {
                    m_WarnedInstances = true;
                    UnityEngine.Debug.LogWarning($"Lettera: font '{Family}' holds over {limit} variation and synthesis instances. Nothing is evicted, so check for a setting that animates an axis in many small steps.");
                }
            }
            return instance;
        }

        FontInstance CreateInstance(float weightAxis, float embolden, float slant, AxisSet axes = null)
        {
            IntPtr font = LetteraNative.lt_font_create(Face);
            ApplyVariations(font, weightAxis, axes);
            if (embolden != 0 || slant != 0) LetteraNative.lt_font_set_synthetic(font, embolden, slant);
            if (HasColorBitmaps) LetteraNative.lt_font_set_ppem(font, ColorStrikePpem);
            LetteraNative.lt_font_make_immutable(font);
            m_Fonts.Add(font);
            return new FontInstance(this, font);
        }

        // Pinned values lie under what a range asks for; a weight applies unless the range names wght itself.
        void ApplyVariations(IntPtr font, float weightAxis, AxisSet axes)
        {
            AxisSet all = axes != null ? axes.Over(m_Pinned) : m_Pinned;
            int count = (all?.Entries.Length ?? 0) + 1;
            uint* tags = stackalloc uint[count];
            float* values = stackalloc float[count];
            int used = 0;
            uint weightTag = LetteraNative.Tag("wght");
            if (weightAxis >= 0 && (axes == null || !axes.Has(weightTag)))
            {
                tags[used] = weightTag;
                values[used++] = weightAxis;
            }
            if (all != null)
            {
                foreach (AxisSet.Entry entry in all.Entries)
                {
                    if (!TryAxis(entry, out float value)) continue;
                    tags[used] = entry.Tag;
                    values[used++] = value;
                }
            }
            if (used > 0) LetteraNative.lt_font_set_variations(font, tags, values, used);
        }

        bool TryAxis(in AxisSet.Entry entry, out float value)
        {
            value = 0;
            for (int i = 0; i < m_AxisTags.Length; i++)
            {
                if (m_AxisTags[i] != entry.Tag) continue;
                float min = m_AxisRanges[i * 3], standard = m_AxisRanges[i * 3 + 1], max = m_AxisRanges[i * 3 + 2];
                value = entry.Kind == AxisSet.Mode.PercentOfDefault ? standard * entry.Value * 0.01f
                    : entry.Kind == AxisSet.Mode.DeltaFromDefault ? standard + entry.Value : entry.Value;
                value = value < min ? min : value > max ? max : value;
                return true;
            }
            return false;
        }

        /// <summary>The font's pixel grid per em, or 0 for an ordinary font.</summary>
        public int PixelGrid;
        public bool PixelSnap = true;

        /// <summary>
        /// Finds the grid of a font drawn in pixels: the outlines of its glyphs are all straight lines, on coordinates that are
        /// multiples of one step that divides the em evenly. Any curve, or a step too fine to be a pixel, means an ordinary font.
        /// </summary>
        public int FindPixelGrid()
        {
            if (IsVirtual) return 0;
            int upem = Metrics.Upem;
            int step = 0;
            var commands = new float[4096];
            int found = 0;
            foreach (char c in "AHOIaeoxn0.")
            {
                uint glyph = LetteraNative.lt_font_glyph(Regular.Font, c, 0);
                if (glyph == 0) continue;
                int count;
                fixed (float* p = commands) count = LetteraNative.lt_glyph_outline(Regular.Font, glyph, p, commands.Length);
                if (count <= 0 || count > commands.Length) continue;
                found++;
                int i = 0;
                while (i < count)
                {
                    int op = (int)commands[i++];
                    if (op == 2 || op == 3) return 0;
                    int points = op == 4 ? 0 : 1;
                    for (int n = 0; n < points * 2; n++, i++)
                    {
                        float value = commands[i];
                        int whole = (int)Math.Round(value);
                        if (Math.Abs(value - whole) > 0.01f) return 0;
                        step = Gcd(step, Math.Abs(whole));
                    }
                }
            }
            if (found < 2 || step == 0 || upem % step != 0) return 0;
            int grid = upem / step;
            return grid >= 4 && grid <= 128 ? grid : 0;
        }

        static int Gcd(int a, int b)
        {
            while (b != 0)
            {
                int t = a % b;
                a = b;
                b = t;
            }
            return a;
        }

        /// <summary>Whether this face has axes at all.</summary>
        public bool IsVariable => m_AxisTags.Length > 0;

        /// <summary>A variant asset pins a point of the design space and overrides what the file states about itself.</summary>
        public void Customize(AxisSet pinned, string family, int weight, bool italic)
        {
            m_Pinned = pinned;
            if (pinned != null) Regular = CreateInstance(-1, 0, 0);
            if (!string.IsNullOrEmpty(family)) Family = family;
            if (weight > 0) Weight = weight;
            if (italic) IsItalic = true;
        }

        /// <summary>Whether shaping a codepoint with a feature on gives another glyph than with it off: the face really has it for this character.</summary>
        public bool ChangesGlyph(uint feature, int codepoint)
        {
            if (IsVirtual) return false;
            long key = ((long)feature << 32) | (uint)codepoint;
            if (m_FeatureProbes.TryGetValue(key, out bool known)) return known;
            NativeGlyph* output = stackalloc NativeGlyph[8];
            var on = new NativeFeature { Tag = feature, Value = 1, Start = 0, End = uint.MaxValue };
            int text = codepoint;
            uint script = FirstSymbol.Lettera.Unicode.LetteraUnicode.ScriptToOpenTypeTag(FirstSymbol.Lettera.Unicode.LetteraUnicode.GetScript(codepoint));
            int plainCount = LetteraNative.lt_shape(Regular.Font, &text, 1, 0, 1, 4, script, null, null, 0, output, 8);
            uint plain = plainCount > 0 ? output[0].Glyph : 0;
            int count = LetteraNative.lt_shape(Regular.Font, &text, 1, 0, 1, 4, script, null, &on, 1, output, 8);
            bool changed = plainCount > 0 && count > 0 && output[0].Glyph != plain && output[0].Glyph != 0;
            m_FeatureProbes[key] = changed;
            return changed;
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
            int axisCount = Math.Min(axes, 32);
            m_AxisTags = new uint[axisCount];
            m_AxisRanges = new float[axisCount * 3];
            for (int i = 0; i < axisCount; i++)
            {
                m_AxisTags[i] = tags[i];
                m_AxisRanges[i * 3] = ranges[i * 3];
                m_AxisRanges[i * 3 + 1] = ranges[i * 3 + 1];
                m_AxisRanges[i * 3 + 2] = ranges[i * 3 + 2];
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
            m.SuperSize = Metric(font, "spys");
            m.SuperOffset = Math.Abs(Metric(font, "spyo"));
            m.SubSize = Metric(font, "sbys");
            m.SubOffset = Math.Abs(Metric(font, "sbyo"));
            Metrics = m;
            NaturalMetrics = m;
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
            if (m_Pin.IsAllocated) m_Pin.Free();
            s_All.Remove(this);
        }

        /// <summary>Releases every native face (domain reload, tests).</summary>
        public static void DisposeAll()
        {
            for (int i = s_All.Count - 1; i >= 0; i--) s_All[i].Dispose();
        }
    }
}

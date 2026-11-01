#if !LETTERA_NO_MATH
using System;
using System.Collections.Generic;
using UnityEngine;

namespace FirstSymbol.Lettera
{
    /// <summary>OpenType's MATH constants, in the table's own order.</summary>
    internal enum MathConstant
    {
        ScriptPercentScaleDown, ScriptScriptPercentScaleDown, DelimitedSubFormulaMinHeight, DisplayOperatorMinHeight, MathLeading, AxisHeight, AccentBaseHeight, FlattenedAccentBaseHeight,
        SubscriptShiftDown, SubscriptTopMax, SubscriptBaselineDropMin, SuperscriptShiftUp, SuperscriptShiftUpCramped, SuperscriptBottomMin, SuperscriptBaselineDropMax, SubSuperscriptGapMin, SuperscriptBottomMaxWithSubscript, SpaceAfterScript,
        UpperLimitGapMin, UpperLimitBaselineRiseMin, LowerLimitGapMin, LowerLimitBaselineDropMin,
        StackTopShiftUp, StackTopDisplayStyleShiftUp, StackBottomShiftDown, StackBottomDisplayStyleShiftDown, StackGapMin, StackDisplayStyleGapMin,
        StretchStackTopShiftUp, StretchStackBottomShiftDown, StretchStackGapAboveMin, StretchStackGapBelowMin,
        FractionNumeratorShiftUp, FractionNumeratorDisplayStyleShiftUp, FractionDenominatorShiftDown, FractionDenominatorDisplayStyleShiftDown, FractionNumeratorGapMin, FractionNumDisplayStyleGapMin, FractionRuleThickness, FractionDenominatorGapMin, FractionDenomDisplayStyleGapMin,
        SkewedFractionHorizontalGap, SkewedFractionVerticalGap,
        OverbarVerticalGap, OverbarRuleThickness, OverbarExtraAscender, UnderbarVerticalGap, UnderbarRuleThickness, UnderbarExtraDescender,
        RadicalVerticalGap, RadicalDisplayStyleVerticalGap, RadicalRuleThickness, RadicalExtraAscender, RadicalKernBeforeDegree, RadicalKernAfterDegree, RadicalDegreeBottomRaisePercent,
        Count,
    }

    /// <summary>One piece of an assembled shape.</summary>
    internal struct MathPart
    {
        public uint Glyph;
        public float StartConnector, EndConnector, FullAdvance;
        public bool Extender;
    }

    /// <summary>What a font's MATH table says, in font units. A test gives its own.</summary>
    internal interface ILetteraMathData
    {
        void Constants(int[] into);
        int ItalicCorrection(uint glyph);
        int TopAccentAttachment(uint glyph);
        bool IsExtended(uint glyph);
        int Kern(uint glyph, int corner, int height);
        int Variants(uint glyph, bool vertical, uint[] glyphs, int[] advances);
        int Assembly(uint glyph, bool vertical, MathPart[] parts, out int italic);
        int MinOverlap(bool vertical);
    }

    /// <summary>The native library's reading of a MATH table. Every call stands behind the library's version, here and nowhere else.</summary>
    internal sealed unsafe class NativeMathData : ILetteraMathData
    {
        readonly FontInstance m_Instance;

        internal NativeMathData(FontInstance instance) => m_Instance = instance;

        internal static bool Available(FontFace face) => LetteraNative.Version >= 3 && face != null && !face.IsVirtual && LetteraNative.lt_math_has_data(face.Face) != 0;

        public void Constants(int[] into)
        {
            fixed (int* p = into) LetteraNative.lt_math_constants(m_Instance.Font, p);
        }

        int Info(uint glyph, int which)
        {
            int* values = stackalloc int[3];
            LetteraNative.lt_math_glyph_info(m_Instance.Font, glyph, values);
            return values[which];
        }

        public int ItalicCorrection(uint glyph) => Info(glyph, 0);
        public int TopAccentAttachment(uint glyph) => Info(glyph, 1);
        public bool IsExtended(uint glyph) => Info(glyph, 2) != 0;
        public int Kern(uint glyph, int corner, int height) => LetteraNative.lt_math_kern(m_Instance.Font, glyph, corner, height);

        public int Variants(uint glyph, bool vertical, uint[] glyphs, int[] advances)
        {
            fixed (uint* g = glyphs)
            fixed (int* a = advances)
                return LetteraNative.lt_math_variants(m_Instance.Font, glyph, vertical ? 1 : 0, g, a, glyphs.Length);
        }

        public int Assembly(uint glyph, bool vertical, MathPart[] parts, out int italic)
        {
            NativeMathPart* native = stackalloc NativeMathPart[parts.Length];
            int it = 0;
            int count = LetteraNative.lt_math_assembly(m_Instance.Font, glyph, vertical ? 1 : 0, native, parts.Length, &it);
            italic = it;
            int read = Mathf.Min(count, parts.Length);
            for (int i = 0; i < read; i++) parts[i] = new MathPart { Glyph = native[i].Glyph, StartConnector = native[i].StartConnector, EndConnector = native[i].EndConnector, FullAdvance = native[i].FullAdvance, Extender = (native[i].Flags & 1) != 0 };
            return read;
        }

        public int MinOverlap(bool vertical) => LetteraNative.lt_math_min_overlap(m_Instance.Font, vertical ? 1 : 0);
    }

    /// <summary>A face as math reads it: constants in em, and glyphs and their sizes, cached so a formula after its first costs no native call per character.</summary>
    internal sealed unsafe class LetteraMathFont
    {
        /// <summary>A test's own data, for any font.</summary>
        internal static ILetteraMathData Override;

        /// <summary>How many times a font was read, for a probe that a text with no formula costs nothing.</summary>
        internal static int Created { get; private set; }

        static readonly Dictionary<int, LetteraMathFont> s_Fonts = new Dictionary<int, LetteraMathFont>();

        internal readonly FontInstance Instance;
        internal readonly ILetteraMathData Data;
        internal readonly float Upem;
        readonly float[] m_Constants = new float[(int)MathConstant.Count];
        readonly Dictionary<long, uint> m_Glyphs = new Dictionary<long, uint>();
        readonly Dictionary<uint, (float minX, float minY, float maxX, float maxY, float advance)> m_Extents = new Dictionary<uint, (float, float, float, float, float)>();

        internal bool HasMathTable => Data != null;

        LetteraMathFont(FontInstance instance, ILetteraMathData data)
        {
            Created++;
            Instance = instance;
            Data = data;
            Upem = instance.Face.Metrics.Upem;
            if (data != null)
            {
                var raw = new int[(int)MathConstant.Count];
                data.Constants(raw);
                for (int i = 0; i < raw.Length; i++)
                    m_Constants[i] = i == (int)MathConstant.ScriptPercentScaleDown || i == (int)MathConstant.ScriptScriptPercentScaleDown || i == (int)MathConstant.RadicalDegreeBottomRaisePercent ? raw[i] : raw[i] / Upem;
                if (m_Constants[(int)MathConstant.ScriptPercentScaleDown] <= 0) m_Constants[(int)MathConstant.ScriptPercentScaleDown] = 71;
                if (m_Constants[(int)MathConstant.ScriptScriptPercentScaleDown] <= 0) m_Constants[(int)MathConstant.ScriptScriptPercentScaleDown] = 50;
            }
            else
            {
                Fallbacks(instance.Face.Metrics);
            }
        }

        internal static void ForgetAll()
        {
            s_Fonts.Clear();
            LetteraMathCache.Clear();
        }

        internal static LetteraMathFont For(FontFace face)
        {
            FontInstance instance = face.Regular;
            int key = instance.Id;
            if (s_Fonts.TryGetValue(key, out LetteraMathFont known) && known.Data == (Override ?? known.Data)) return known;
            ILetteraMathData data = Override ?? (NativeMathData.Available(face) ? new NativeMathData(instance) : null);
            var made = new LetteraMathFont(instance, data);
            s_Fonts[key] = made;
            return made;
        }

        internal static void ClearAll() => s_Fonts.Clear();

        // MathML Core's values for a font with no table, so a formula still has a shape.
        void Fallbacks(in FaceMetrics metrics)
        {
            float rule = metrics.UnderlineSize > 0 ? metrics.UnderlineSize / Upem : 0.05f;
            float xHeight = metrics.XHeight > 0 ? metrics.XHeight / Upem : 0.5f;
            float capHeight = metrics.CapHeight > 0 ? metrics.CapHeight / Upem : 0.7f;
            void Set(MathConstant c, float v) => m_Constants[(int)c] = v;
            Set(MathConstant.ScriptPercentScaleDown, 71);
            Set(MathConstant.ScriptScriptPercentScaleDown, 50.41f);
            Set(MathConstant.DelimitedSubFormulaMinHeight, 1.5f);
            Set(MathConstant.AxisHeight, xHeight / 2);
            Set(MathConstant.AccentBaseHeight, xHeight);
            Set(MathConstant.FlattenedAccentBaseHeight, capHeight);
            Set(MathConstant.SubscriptShiftDown, 0.2f);
            Set(MathConstant.SubscriptTopMax, xHeight * 0.8f);
            Set(MathConstant.SuperscriptShiftUp, 0.35f);
            Set(MathConstant.SuperscriptShiftUpCramped, 0.3f);
            Set(MathConstant.SuperscriptBottomMin, xHeight * 0.25f);
            Set(MathConstant.SubSuperscriptGapMin, rule * 4);
            Set(MathConstant.SuperscriptBottomMaxWithSubscript, xHeight * 0.8f);
            Set(MathConstant.SpaceAfterScript, 1f / 24);
            Set(MathConstant.StackGapMin, rule * 3);
            Set(MathConstant.StackDisplayStyleGapMin, rule * 7);
            Set(MathConstant.FractionNumeratorGapMin, rule);
            Set(MathConstant.FractionNumDisplayStyleGapMin, rule * 3);
            Set(MathConstant.FractionDenominatorGapMin, rule);
            Set(MathConstant.FractionDenomDisplayStyleGapMin, rule * 3);
            Set(MathConstant.FractionRuleThickness, rule);
            Set(MathConstant.OverbarVerticalGap, rule * 3);
            Set(MathConstant.OverbarRuleThickness, rule);
            Set(MathConstant.OverbarExtraAscender, rule);
            Set(MathConstant.UnderbarVerticalGap, rule * 3);
            Set(MathConstant.UnderbarRuleThickness, rule);
            Set(MathConstant.UnderbarExtraDescender, rule);
            Set(MathConstant.RadicalVerticalGap, rule * 1.25f);
            Set(MathConstant.RadicalDisplayStyleVerticalGap, rule + xHeight * 0.25f);
            Set(MathConstant.RadicalRuleThickness, rule);
            Set(MathConstant.RadicalExtraAscender, rule);
            Set(MathConstant.RadicalKernBeforeDegree, 5f / 18);
            Set(MathConstant.RadicalKernAfterDegree, -10f / 18);
            Set(MathConstant.RadicalDegreeBottomRaisePercent, 60);
            Set(MathConstant.StretchStackGapAboveMin, rule);
            Set(MathConstant.StretchStackGapBelowMin, rule);
        }

        /// <summary>A constant in em at full size. The percents stay percents.</summary>
        internal float C(MathConstant constant) => m_Constants[(int)constant];

        /// <summary>The glyph for a character in a script level, through the font's own substitutions. 0 when it has none.</summary>
        internal uint GlyphOf(int cp, int ssty, bool dotless = false, bool flattened = false)
        {
            long key = ((long)cp << 8) | (long)(ssty & 3) | (dotless ? 4L : 0) | (flattened ? 8L : 0);
            if (m_Glyphs.TryGetValue(key, out uint glyph)) return glyph;
            glyph = ShapeOne(cp, ssty, dotless, flattened);
            m_Glyphs[key] = glyph;
            return glyph;
        }

        uint ShapeOne(int cp, int ssty, bool dotless, bool flattened)
        {
            int text = cp;
            NativeFeature* features = stackalloc NativeFeature[3];
            int count = 0;
            if (ssty > 0) features[count++] = new NativeFeature { Tag = LetteraNative.Tag("ssty"), Value = (uint)ssty, Start = 0, End = uint.MaxValue };
            if (dotless) features[count++] = new NativeFeature { Tag = LetteraNative.Tag("dtls"), Value = 1, Start = 0, End = uint.MaxValue };
            if (flattened) features[count++] = new NativeFeature { Tag = LetteraNative.Tag("flac"), Value = 1, Start = 0, End = uint.MaxValue };
            NativeGlyph* output = stackalloc NativeGlyph[4];
            int shaped = LetteraNative.lt_shape(Instance.Font, &text, 1, 0, 1, 4, LetteraNative.Tag("math"), null, features, count, output, 4);
            return shaped >= 1 ? output[0].Glyph : 0;
        }

        /// <summary>A glyph's ink and advance, in em.</summary>
        internal (float minX, float minY, float maxX, float maxY, float advance) Extents(uint glyph)
        {
            if (m_Extents.TryGetValue(glyph, out (float, float, float, float, float) known)) return known;
            int* box = stackalloc int[4];
            float minX = 0, minY = 0, maxX = 0, maxY = 0;
            if (LetteraNative.lt_glyph_extents(Instance.Font, glyph, box) != 0)
            {
                float x0 = box[0], x1 = box[0] + box[2], y0 = box[1], y1 = box[1] + box[3];
                minX = Mathf.Min(x0, x1) / Upem;
                maxX = Mathf.Max(x0, x1) / Upem;
                minY = Mathf.Min(y0, y1) / Upem;
                maxY = Mathf.Max(y0, y1) / Upem;
            }
            float advance = LetteraNative.lt_glyph_advance(Instance.Font, glyph) / Upem;
            var result = (minX, minY, maxX, maxY, advance);
            m_Extents[glyph] = result;
            return result;
        }
    }
}
#endif

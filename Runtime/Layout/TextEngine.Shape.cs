using System;
using System.Collections.Generic;
using FirstSymbol.Lettera.Unicode;
using UnityEngine;

namespace FirstSymbol.Lettera
{
    internal sealed unsafe partial class TextEngine
    {
        struct Paragraph
        {
            public int Start, ContentEnd, End;
            public int Level;
        }

        struct Run
        {
            public int Start, End, GlyphStart, GlyphCount;
            public FontInstance Instance;
            public byte Level;
            public float UnitEm;        // Em per font unit, including font scale and normalization.
            public float SizeAbsolute, SizeMul, SizeAdd;
            public short Language;
            public UnicodeScript Script;
        }

        struct ShapedGlyph
        {
            public uint Glyph;
            public int Cluster, XAdvance, XOffset, YOffset;
        }

        static readonly Dictionary<string, byte[]> s_LanguageBytes = new Dictionary<string, byte[]>();

        readonly FontChain m_Chain = new FontChain();
        readonly List<Paragraph> m_Paragraphs = new List<Paragraph>();
        readonly List<Run> m_Runs = new List<Run>();
        ShapedGlyph[] m_Shaped = new ShapedGlyph[64];
        NativeGlyph[] m_NativeGlyphs = new NativeGlyph[256];
        int m_ShapedCount;
        byte[] m_Levels = new byte[64];
        bool[] m_Boundaries = new bool[64];
        LineBreakOpportunity[] m_Breaks = new LineBreakOpportunity[64];
        UnicodeScript[] m_Scripts = new UnicodeScript[64];
        int[] m_CpRun = new int[64];
        float[] m_CpAdvanceEm = new float[64];

        bool IsParagraphSeparator(int i, out int width)
        {
            int cp = m_Text[i];
            width = 1;
            if (cp == '\r' && i + 1 < m_TextLength && m_Text[i + 1] == '\n') { width = 2; return true; }
            return cp == '\n' || cp == '\r' || cp == 0x85 || cp == 0x2029;
        }

        void ShapeText()
        {
            int length = m_TextLength;
            EnsureSize(ref m_Levels, length + 1);
            EnsureSize(ref m_Boundaries, length + 2);
            EnsureSize(ref m_Breaks, length + 1);
            EnsureSize(ref m_Scripts, length + 1);
            EnsureSize(ref m_CpRun, length + 1);
            EnsureSize(ref m_CpAdvanceEm, length + 1);
            Array.Clear(m_CpAdvanceEm, 0, length + 1);

            LetteraTextData data = m_Host.Data;
            m_Chain.Build(data.Font, data.FontStack);

            m_Paragraphs.Clear();
            m_Runs.Clear();
            m_ShapedCount = 0;
            if (length == 0) return;

            int direction = m_Attributes.DirectionOverride >= 0 ? m_Attributes.DirectionOverride
                : data.BaseDirection == LetteraDirection.LeftToRight ? 0 : data.BaseDirection == LetteraDirection.RightToLeft ? 1 : 2;
            int baseLevel = direction == 2 ? -1 : direction;

            int start = 0;
            while (start <= length)
            {
                int i = start;
                int separator = 0;
                while (i < length && !IsParagraphSeparator(i, out separator)) i++;
                if (i >= length) separator = 0;
                AddParagraph(start, i, i + separator, baseLevel);
                if (i >= length) break;
                start = i + separator;
                if (start == length)
                {
                    // Text ending in a separator ends with an empty last paragraph.
                    AddParagraph(length, length, length, baseLevel);
                    break;
                }
            }
        }

        void AddParagraph(int start, int contentEnd, int end, int baseLevel)
        {
            var paragraph = new Paragraph { Start = start, ContentEnd = contentEnd, End = end, Level = baseLevel == 1 ? 1 : 0 };
            int count = end - start;
            if (count > 0)
            {
                var text = new ReadOnlySpan<int>(m_Text, start, count);
                paragraph.Level = Bidi.ResolveParagraph(text, baseLevel, new Span<byte>(m_Levels, start, count));
                GraphemeBreaker.FindBoundaries(text, new Span<bool>(m_Boundaries, start, count + 1));
                LineBreaker.FindBreaks(text, new Span<LineBreakOpportunity>(m_Breaks, start, count));
                ScriptItemizer.Resolve(text, new Span<UnicodeScript>(m_Scripts, start, count));
                BuildRuns(paragraph);
            }
            m_Paragraphs.Add(paragraph);
        }

        string LanguageOf(short index)
        {
            if (index >= 0) return m_Attributes.Languages[index];
            string language = m_Host.Data.Language;
            return !string.IsNullOrEmpty(language) && LanguageTags.IsWellFormed(language) ? language : null;
        }

        void BuildRuns(in Paragraph paragraph)
        {
            LetteraAttributes.Cp[] attrs = m_Attributes.Chars;
            var current = new Run { Start = -1 };
            int i = paragraph.Start;
            FontFace primary = m_Chain.PrimaryFace;
            while (i < paragraph.End)
            {
                int clusterEnd = i + 1;
                while (clusterEnd < paragraph.End && !m_Boundaries[clusterEnd]) clusterEnd++;
                var cluster = new ReadOnlySpan<int>(m_Text, i, clusterEnd - i);

                ref LetteraAttributes.Cp attr = ref attrs[i];
                string family = attr.Family >= 0 ? m_Attributes.FamilyRequests[attr.Family] : null;
                string language = LanguageOf(attr.Language);
                bool emoji = EmojiClassifier.IsEmojiCluster(m_Text, i, clusterEnd - i);
                FontFace face = m_Chain.Resolve(cluster, emoji, family, language, out FontChain.Family owner);
                FontInstance instance = face == null ? null : FontChain.Style(face, owner, attr.Weight, attr.BoldMode, attr.Italic, attr.Slant);

                bool same = current.Start >= 0 && current.Instance == instance && current.Level == m_Levels[i] && current.Script == m_Scripts[i] &&
                            current.Language == attr.Language && current.SizeAbsolute == attr.SizeAbsolute && current.SizeMul == attr.SizeMul && current.SizeAdd == attr.SizeAdd;
                if (!same)
                {
                    if (current.Start >= 0) FinishRun(ref current, i, paragraph);
                    current = new Run
                    {
                        Start = i, Instance = instance, Level = m_Levels[i], Script = m_Scripts[i], Language = attr.Language,
                        SizeAbsolute = attr.SizeAbsolute, SizeMul = attr.SizeMul, SizeAdd = attr.SizeAdd,
                        UnitEm = instance == null ? 0 : UnitEm(instance.Face, owner, primary),
                    };
                }
                i = clusterEnd;
            }
            if (current.Start >= 0) FinishRun(ref current, paragraph.End, paragraph);
        }

        static float UnitEm(FontFace face, FontChain.Family owner, FontFace primary)
        {
            float scale = 1f / face.Metrics.Upem;
            LetteraFontAsset asset = owner?.Asset;
            if (asset != null) scale *= asset.FontScale;
            bool normalize = asset == null || asset.NormalizeSize;
            if (normalize && primary != null && face != primary && !face.IsColor)
            {
                bool useCap = LetteraSettings.Instance.FontSizeMatchMetric == LetteraSizeMatchMetric.CapHeight;
                float wanted = useCap ? (float)primary.Metrics.CapHeight / primary.Metrics.Upem : (float)primary.Metrics.XHeight / primary.Metrics.Upem;
                float actual = useCap ? (float)face.Metrics.CapHeight / face.Metrics.Upem : (float)face.Metrics.XHeight / face.Metrics.Upem;
                if (wanted > 0 && actual > 0) scale *= Mathf.Clamp(wanted / actual, 0.75f, 1.35f);
            }
            return scale;
        }

        void FinishRun(ref Run run, int end, in Paragraph paragraph)
        {
            run.End = end;
            int index = m_Runs.Count;
            for (int i = run.Start; i < end; i++) m_CpRun[i] = index;
            run.GlyphStart = m_ShapedCount;
            if (run.Instance != null) ShapeRun(ref run, paragraph);
            m_Runs.Add(run);
        }

        void ShapeRun(ref Run run, in Paragraph paragraph)
        {
            string language = LanguageOf(run.Language);
            byte[] languageBytes = null;
            if (language != null && !s_LanguageBytes.TryGetValue(language, out languageBytes))
            {
                languageBytes = FontFace.NullTerminated(language);
                s_LanguageBytes[language] = languageBytes;
            }

            int direction = (run.Level & 1) != 0 ? 5 : 4;
            uint script = LetteraUnicode.ScriptToOpenTypeTag(run.Script);
            int count;
            fixed (int* text = &m_Text[paragraph.Start])
            fixed (byte* lang = languageBytes)
            {
                while (true)
                {
                    fixed (NativeGlyph* output = m_NativeGlyphs)
                        count = LetteraNative.lt_shape(run.Instance.Font, text, paragraph.End - paragraph.Start, run.Start - paragraph.Start, run.End - run.Start,
                            direction, script, lang, null, 0, output, m_NativeGlyphs.Length);
                    if (count >= 0) break;
                    m_NativeGlyphs = new NativeGlyph[-count * 2];
                }
            }

            EnsureSize(ref m_Shaped, m_ShapedCount + count);
            for (int i = 0; i < count; i++)
            {
                ref NativeGlyph g = ref m_NativeGlyphs[i];
                int cluster = (int)g.Cluster + paragraph.Start;
                m_Shaped[m_ShapedCount + i] = new ShapedGlyph { Glyph = g.Glyph, Cluster = cluster, XAdvance = g.XAdvance, XOffset = g.XOffset, YOffset = g.YOffset };
                m_CpAdvanceEm[cluster] += g.XAdvance * run.UnitEm;
            }
            run.GlyphCount = count;
            m_ShapedCount += count;
        }
    }
}

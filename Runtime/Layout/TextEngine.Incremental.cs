using System;
using FirstSymbol.Lettera.Unicode;

namespace FirstSymbol.Lettera
{
    // An edit of a plain text redoes the paragraphs from the one it touched. The parse splices the shown characters, shaping keeps every paragraph
    // before that one as it stands, and line breaking keeps their lines. The result is the same as doing all of it again: paragraphs shape and break alone.
    internal sealed partial class TextEngine
    {
        // Below this a text is redone whole: copying its source each parse would cost more than it saves.
        const int SpliceFromChars = 512;

        char[] m_PrevSource = Array.Empty<char>();
        string m_PrevString;            // The last source, when it was a string: a string never changes, so it is kept, not copied.
        int m_PrevSourceLength = -1;    // -1: the last parse was not one an edit can splice into.

        ReadOnlySpan<char> PrevSource => m_PrevString != null ? m_PrevString.AsSpan(0, m_PrevSourceLength) : new ReadOnlySpan<char>(m_PrevSource, 0, m_PrevSourceLength);

        int m_EditParse = -1;           // The parse that found the edit below.
        int m_EditStart;                // The first shown codepoint that may differ.

        struct ShapeKey : IEquatable<ShapeKey>
        {
            public int Chain, BaseLevel, Segmenters;
            public string Language;
            public bool Plain, Caches;

            public bool Equals(ShapeKey o) => Chain == o.Chain && BaseLevel == o.BaseLevel && Segmenters == o.Segmenters && Language == o.Language && Plain == o.Plain && Caches == o.Caches;
        }

        struct LayoutKey : IEquatable<LayoutKey>
        {
            public float Width, Size, Value, DefaultValue;
            public int Mode, DefaultMode, Leading;
            public bool Wrap, Plain;

            public bool Equals(LayoutKey o) => Width == o.Width && Size == o.Size && Value == o.Value && DefaultValue == o.DefaultValue && Mode == o.Mode &&
                                              DefaultMode == o.DefaultMode && Leading == o.Leading && Wrap == o.Wrap && Plain == o.Plain;
        }

        int m_ShapedAtParse = -1;
        ShapeKey m_LastShapeKey;
        int m_ResumedShape = -1;        // The shaping that resumed at a paragraph, and that paragraph.
        int m_ResumedParagraph;
        int m_LaidAtShape = -1;
        LayoutKey m_LastLayoutKey;

        /// <summary>How many shapings and layouts resumed at an edited paragraph. Tests read this.</summary>
        internal int ResumedShapes { get; private set; }
        internal int ResumedLayouts { get; private set; }

        int m_EditPrefix, m_EditSuffix;     // In source chars, found by FindEdit.

        void RememberSource(ReadOnlySpan<char> source, bool plain, bool spliced, string kept)
        {
            if (spliced) return;
            if (!plain || source.Length < SpliceFromChars)
            {
                m_PrevSourceLength = -1;
                m_PrevString = null;
                if (m_PrevSource.Length > 0 && source.Length < SpliceFromChars) m_PrevSource = Array.Empty<char>();
                return;
            }
            if (kept != null)
            {
                m_PrevString = kept;
                m_PrevSource = Array.Empty<char>();
                m_PrevSourceLength = kept.Length;
                return;
            }
            m_PrevString = null;
            if (m_PrevSource.Length < source.Length) m_PrevSource = new char[Math.Max(source.Length, m_PrevSource.Length * 3 / 2)];
            source.CopyTo(m_PrevSource);
            m_PrevSourceLength = source.Length;
        }

        static int CommonPrefix(ReadOnlySpan<char> a, ReadOnlySpan<char> b, int limit)
        {
            int i = 0;
            while (i + 64 <= limit && a.Slice(i, 64).SequenceEqual(b.Slice(i, 64))) i += 64;
            while (i < limit && a[i] == b[i]) i++;
            return i;
        }

        static int CommonSuffix(ReadOnlySpan<char> a, ReadOnlySpan<char> b, int limit)
        {
            int i = 0;
            while (i + 64 <= limit && a.Slice(a.Length - i - 64, 64).SequenceEqual(b.Slice(b.Length - i - 64, 64))) i += 64;
            while (i < limit && a[a.Length - 1 - i] == b[b.Length - 1 - i]) i++;
            return i;
        }

        // The one stretch where a plain source changed since the last parse, or false when it did not, or the last parse was no plain one.
        bool FindEdit(ReadOnlySpan<char> source)
        {
            int oldLength = m_PrevSourceLength;
            if (oldLength < 0 || source.Length < SpliceFromChars) return false;
            ReadOnlySpan<char> old = PrevSource;
            int limit = Math.Min(oldLength, source.Length);
            int prefix = CommonPrefix(old, source, limit);
            if (prefix == oldLength && prefix == source.Length) return false;
            int suffix = CommonSuffix(old, source, limit - prefix);
            // Never between the halves of a pair.
            if (prefix > 0 && char.IsHighSurrogate(old[prefix - 1])) prefix--;
            if (suffix > 0 && char.IsLowSurrogate(old[oldLength - suffix])) suffix--;
            m_EditPrefix = prefix;
            m_EditSuffix = suffix;
            return true;
        }

        // Old and new source are both plain, so shown codepoints are the source's: the stretch that changed is cut out and the new one put in.
        bool TrySplice(ReadOnlySpan<char> source, string kept)
        {
            int oldLength = m_PrevSourceLength;
            int prefix = m_EditPrefix, suffix = m_EditSuffix;
            int oldEnd = oldLength - suffix, newEnd = source.Length - suffix;

            int cpStart = RenderedIndexFromSource(prefix), cpOldEnd = RenderedIndexFromSource(oldEnd);
            if (cpStart > m_TextLength || cpOldEnd > m_TextLength || SourceAt(cpStart) != prefix || (cpOldEnd < m_TextLength && SourceAt(cpOldEnd) != oldEnd)) return false;

            int inserted = 0;
            bool pairs = false;
            for (int at = prefix; at < newEnd; at++)
            {
                pairs |= char.IsSurrogate(source[at]);
                if (char.IsHighSurrogate(source[at]) && at + 1 < source.Length && char.IsLowSurrogate(source[at + 1])) at++;
                inserted++;
            }
            int tail = m_TextLength - cpOldEnd;
            int newCount = cpStart + inserted + tail;
            int tailTo = cpStart + inserted;
            EnsureSize(ref m_Text, Math.Max(source.Length, newCount) + 4);
            // A text that was its source stays so while no surrogate comes in, and needs no index moved.
            bool identity = m_IdentityIndex && !pairs;
            if (identity)
            {
                if (tail > 0 && tailTo != cpOldEnd) Array.Copy(m_Text, cpOldEnd, m_Text, tailTo, tail);
            }
            else
            {
                if (m_IdentityIndex) SourceIndexArray();
                EnsureSize(ref m_SourceIndex, Math.Max(source.Length, newCount) + 4);
                if (tail > 0 && tailTo != cpOldEnd)
                {
                    Array.Copy(m_Text, cpOldEnd, m_Text, tailTo, tail);
                    Array.Copy(m_SourceIndex, cpOldEnd, m_SourceIndex, tailTo, tail);
                }
                int shift = newEnd - oldEnd;
                if (shift != 0)
                    for (int i = tailTo; i < newCount; i++)
                        m_SourceIndex[i] += shift;
            }

            int count = cpStart;
            for (int at = prefix; at < newEnd;)
            {
                int cp = source[at];
                int width = 1;
                if (char.IsHighSurrogate((char)cp) && at + 1 < source.Length && char.IsLowSurrogate(source[at + 1]))
                {
                    cp = char.ConvertToUtf32((char)cp, source[at + 1]);
                    width = 2;
                }
                else if (char.IsSurrogate((char)cp))
                {
                    cp = 0xFFFD;
                }
                m_Text[count] = cp;
                if (!identity) m_SourceIndex[count] = at;
                count++;
                at += width;
            }
            m_TextLength = newCount;
            if (!identity) m_SourceIndex[newCount] = source.Length;
            m_EditParse = ParseCount;
            m_EditStart = cpStart;

            if (kept != null)
            {
                m_PrevString = kept;
                m_PrevSourceLength = source.Length;
                return true;
            }
            if (m_PrevString != null)
            {
                // The last source was a string and this one is not: it is copied once.
                if (m_PrevSource.Length < source.Length) m_PrevSource = new char[Math.Max(source.Length, m_PrevSource.Length * 3 / 2)];
                source.CopyTo(m_PrevSource);
                m_PrevString = null;
                m_PrevSourceLength = source.Length;
                return true;
            }

            // The kept source takes the same edit, so nothing but the change is copied.
            if (m_PrevSource.Length < source.Length)
            {
                var grown = new char[Math.Max(source.Length, m_PrevSource.Length * 3 / 2)];
                Array.Copy(m_PrevSource, grown, oldLength);
                m_PrevSource = grown;
            }
            if (suffix > 0 && newEnd != oldEnd) Array.Copy(m_PrevSource, oldEnd, m_PrevSource, newEnd, suffix);
            source.Slice(prefix, newEnd - prefix).CopyTo(new Span<char>(m_PrevSource, prefix, newEnd - prefix));
            m_PrevSourceLength = source.Length;
            return true;
        }

        // Where shaping starts again: the start of the paragraph before the edit's first character, with all before it kept. 0 shapes everything.
        int ShapeResume(int baseLevel, LetteraTextData data)
        {
            bool plain = m_MatchContext.Ranges.Count == 0 && MaskGlyph == 0 && m_StageHooks.Count == 0 && m_InlineItems.Count == 0 && !m_Attributes.HasLayout;
            var key = new ShapeKey
            {
                Chain = m_Chain.Id, BaseLevel = baseLevel, Segmenters = LetteraWordSegmenters.Count, Language = data.Language, Plain = plain, Caches = LetteraCaches.Enabled,
            };
            bool resume = plain && m_EditParse == ParseCount && m_ShapedAtParse == ParseCount - 1 && key.Equals(m_LastShapeKey) && m_Paragraphs.Count > 0;
            m_LastShapeKey = key;
            m_ShapedAtParse = ParseCount;
            m_ResumedShape = -1;
            if (!resume) return 0;

            int at = Math.Max(m_EditStart - 1, 0);
            int low = 0, high = m_Paragraphs.Count - 1;
            while (low < high)
            {
                int middle = (low + high + 1) / 2;
                if (m_Paragraphs[middle].Start <= at) low = middle;
                else high = middle - 1;
            }
            int cut = m_Paragraphs[low].Start;
            if (low == 0 || cut <= 0) return 0;

            int firstRun = m_Runs.Count;
            while (firstRun > 0 && m_Runs[firstRun - 1].Start >= cut) firstRun--;
            if (firstRun < m_Runs.Count) m_ShapedCount = m_Runs[firstRun].GlyphStart;
            m_Runs.RemoveRange(firstRun, m_Runs.Count - firstRun);
            m_Paragraphs.RemoveRange(low, m_Paragraphs.Count - low);
            m_ResumedShape = ShapeCount;
            m_ResumedParagraph = low;
            ResumedShapes++;
            return cut;
        }

        // Where line breaking starts again: the paragraph shaping started again at, when nothing else that breaks lines changed.
        int LayoutResume(float size, float width, bool wrap, LetteraTextData data)
        {
            LetteraSettings settings = LetteraSettings.Instance;
            bool plain = m_Removals.Count == 0 && !data.AutoSize && !m_MarqueeScrolling && m_Attributes.Elides.Count == 0 && m_RevealSpans.Count == 0 &&
                         m_StageHooks.Count == 0 && !m_Attributes.HasLayout && m_Attributes.TrimTop == LetteraTrimTop.None && m_Attributes.TrimBottom == LetteraTrimBottom.None &&
                         m_FitGlyphScale == 1 && m_FitTracking == 0 && m_FitLineHeight == 1 && m_RubyItems.Count == 0 && m_InlineItems.Count == 0 &&
                         m_Attributes.LineGaps.Count == 0 && m_Attributes.ParagraphSpacings.Count == 0 && m_Attributes.LineHeights.Count == 0;
            var key = new LayoutKey
            {
                Width = width, Size = size, Wrap = wrap, Plain = plain, Mode = (int)data.LineHeightMode, Value = data.LineHeightValue, Leading = (int)data.LeadingDistribution,
                DefaultMode = (int)settings.DefaultLineHeightMode, DefaultValue = settings.DefaultLineHeightValue,
            };
            bool resume = plain && m_ResumedShape == ShapeCount && m_LaidAtShape == ShapeCount - 1 && key.Equals(m_LastLayoutKey);
            m_LastLayoutKey = key;
            m_LaidAtShape = ShapeCount;
            if (!resume) return 0;
            ResumedLayouts++;
            return m_ResumedParagraph;
        }
    }
}

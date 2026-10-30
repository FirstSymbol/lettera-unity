using System;
using System.Collections.Generic;
using FirstSymbol.Lettera.Unicode;
using UnityEngine;

namespace FirstSymbol.Lettera
{
    internal sealed unsafe partial class TextEngine
    {
        const float Epsilon = 0.001f;

        readonly List<LetteraLine> m_Lines = new List<LetteraLine>();
        readonly List<LetteraLine> m_MeasureLines = new List<LetteraLine>();
        Rect m_Box;
        float m_ContentWidth, m_ContentHeight;
        int m_RemoveStart = -1, m_RemoveEnd = -1;
        float m_MarkerEm;
        FontInstance m_MarkerInstance;
        int m_MarkerAnchor;
        readonly List<ShapedGlyph> m_MarkerGlyphs = new List<ShapedGlyph>();

        struct LineStats
        {
            public float MaxWidth, Height;
            public bool WordOverflow;
        }

        Rect ComputeBox(Vector4 padding, Rect rect)
        {
            float width = Mathf.Max(rect.width, 0), height = Mathf.Max(rect.height, 0);
            float xMin = rect.xMin + padding.x, yMin = rect.yMin + padding.y;
            float boxWidth = Mathf.Max(width - padding.x - padding.z, 0);
            float boxHeight = Mathf.Max(height - padding.y - padding.w, 0);
            if (height - padding.y - padding.w < 0) yMin = rect.yMax - padding.w; // Keeps its top edge.
            return new Rect(xMin, yMin, boxWidth, boxHeight);
        }

        float SizeOf(in Run run, float baseSize)
        {
            float size = (run.SizeAbsolute > 0 ? run.SizeAbsolute : baseSize) * run.SizeMul + run.SizeAdd;
            return size < 0.01f ? 0.01f : size;
        }

        void RunLayout()
        {
            LetteraTextData data = m_Host.Data;
            m_Box = ComputeBox(data.Padding, m_Host.LayoutRect);
            bool degenerateWidth = m_Box.width <= 0;
            bool wrap = data.WordWrap && !degenerateWidth;
            float width = degenerateWidth ? float.PositiveInfinity : m_Box.width;
            float height = m_Box.height <= 0 ? float.PositiveInfinity : m_Box.height;

            ClearRemoval();

            float size = data.FontSize;
            if (data.AutoSize) size = FitFontSize(data, width, height, wrap);
            CurrentFontSize = size;

            if (m_Attributes.Elides.Count > 0) ApplyElide(size, width, height, wrap);

            LineStats stats = BreakLines(m_Lines, size, width, wrap, true);
            m_ContentWidth = stats.MaxWidth;
            m_ContentHeight = stats.Height;
        }

        float FitFontSize(LetteraTextData data, float width, float height, bool wrap)
        {
            float max = data.MaxFontSize;
            float min = Mathf.Min(data.MinFontSize, max);
            if (Fits(max, width, height, wrap, false)) return max;
            if (!Fits(min, width, height, wrap, false)) return min;
            float low = min, high = max;
            for (int i = 0; i < 16 && high - low > 0.005f; i++)
            {
                float middle = (low + high) * 0.5f;
                if (Fits(middle, width, height, wrap, false)) low = middle;
                else high = middle;
            }
            return low;
        }

        bool Fits(float size, float width, float height, bool wrap, bool emergency)
        {
            LineStats stats = BreakLines(m_MeasureLines, size, width, wrap, emergency);
            return !stats.WordOverflow && stats.MaxWidth <= width + Epsilon && stats.Height <= height + Epsilon;
        }

        void ClearRemoval()
        {
            m_RemoveStart = m_RemoveEnd = -1;
            m_MarkerEm = 0;
            m_MarkerInstance = null;
            m_MarkerGlyphs.Clear();
        }

        // Removing clusters from one place makes the text fit: find the fewest, by bisection.
        void ApplyElide(float size, float width, float height, bool wrap)
        {
            if (Fits(size, width, height, wrap, true)) return;
            LetteraAttributes.ElideRequest request = m_Attributes.Elides[0];
            int start = request.Start, end = Mathf.Min(request.End, m_TextLength);
            if (end <= start) return;

            int clusters = 0;
            for (int i = start; i < end; i++)
                if (m_Boundaries[i])
                    clusters++;
            if (clusters == 0) return;

            int low = 1, high = clusters;
            while (low < high)
            {
                int middle = (low + high) / 2;
                SetRemoval(request, start, end, clusters, middle);
                if (Fits(size, width, height, wrap, true)) high = middle;
                else low = middle + 1;
            }
            SetRemoval(request, start, end, clusters, low);
        }

        void SetRemoval(in LetteraAttributes.ElideRequest request, int start, int end, int clusters, int remove)
        {
            int kept = clusters - remove;
            int skip = Mathf.RoundToInt(kept * request.Position);
            int a = start, seen = 0;
            while (a < end && seen < skip)
            {
                a++;
                if (a >= end || m_Boundaries[a]) seen++;
            }
            int b = a;
            seen = 0;
            while (b < end && seen < remove)
            {
                b++;
                if (b >= end || m_Boundaries[b]) seen++;
            }
            // Whitespace next to the marker would only leave a gap.
            while (a > start && LetteraUnicode.IsWhiteSpace(m_Text[a - 1])) a--;
            m_RemoveStart = a;
            m_RemoveEnd = b;
            if (request.Marker) PrepareMarker(a > 0 ? a - 1 : a);
        }

        void PrepareMarker(int anchor)
        {
            m_MarkerAnchor = Mathf.Min(anchor, m_TextLength - 1);
            m_MarkerGlyphs.Clear();
            m_MarkerEm = 0;
            m_MarkerInstance = null;
            if (m_Runs.Count == 0) return;
            Run run = m_Runs[m_CpRun[m_MarkerAnchor]];
            FontInstance instance = run.Instance;
            if (instance == null) return;

            int codepoint = 0x2026;
            int repeat = 1;
            if (!instance.Face.HasGlyph(codepoint))
            {
                Span<int> one = stackalloc int[1];
                one[0] = codepoint;
                FontFace face = m_Chain.Resolve(one, false, null, null, out _);
                if (face != null && face.HasGlyph(codepoint))
                {
                    instance = face.Regular;
                }
                else
                {
                    codepoint = '.';
                    repeat = 3;
                }
            }
            m_MarkerInstance = instance;
            uint glyph = LetteraNative.lt_font_glyph(instance.Font, (uint)codepoint, 0);
            int advance = LetteraNative.lt_glyph_advance(instance.Font, glyph);
            float unit = instance == run.Instance ? run.UnitEm : 1f / instance.Face.Metrics.Upem;
            for (int i = 0; i < repeat; i++)
            {
                m_MarkerGlyphs.Add(new ShapedGlyph { Glyph = glyph, Cluster = m_RemoveStart, XAdvance = advance });
                m_MarkerEm += advance * unit;
            }
        }

        bool IsRemoved(int cp) => cp >= m_RemoveStart && cp < m_RemoveEnd;

        LineStats BreakLines(List<LetteraLine> lines, float baseSize, float maxWidth, bool wrap, bool emergency)
        {
            lines.Clear();
            var stats = new LineStats();
            for (int p = 0; p < m_Paragraphs.Count; p++)
            {
                Paragraph paragraph = m_Paragraphs[p];
                if (m_RemoveStart >= 0 && paragraph.Start > m_RemoveStart && paragraph.ContentEnd <= m_RemoveEnd && paragraph.Start < m_RemoveEnd)
                    continue; // Wholly elided.

                int lineStart = paragraph.Start, lastBreak = -1;
                float x = 0, visible = 0, xAtBreak = 0;
                int i = paragraph.Start;
                while (i < paragraph.ContentEnd)
                {
                    int clusterEnd = i + 1;
                    while (clusterEnd < paragraph.ContentEnd && !m_Boundaries[clusterEnd]) clusterEnd++;

                    float width = 0;
                    bool space = true;
                    for (int c = i; c < clusterEnd; c++)
                    {
                        if (c == m_RemoveStart) width += m_MarkerEm * SizeOf(m_Runs[m_CpRun[m_MarkerAnchor]], baseSize);
                        if (IsRemoved(c)) { space &= false; continue; }
                        width += m_CpAdvanceEm[c] * SizeOf(m_Runs[m_CpRun[c]], baseSize);
                        space &= LetteraUnicode.IsWhiteSpace(m_Text[c]);
                    }

                    if (wrap && !space && x + width > maxWidth + Epsilon && x > 0)
                    {
                        if (lastBreak > lineStart)
                        {
                            AddLine(lines, ref stats, baseSize, lineStart, lastBreak, Math.Min(visibleBefore(lastBreak, paragraph), lastBreak), p, false);
                            x -= xAtBreak;
                            visible = Mathf.Max(visible - xAtBreak, 0);
                            lineStart = lastBreak;
                            lastBreak = -1;
                            continue;
                        }
                        if (emergency)
                        {
                            AddLine(lines, ref stats, baseSize, lineStart, i, i, p, false);
                            lineStart = i;
                            x = visible = 0;
                            continue;
                        }
                        stats.WordOverflow = true;
                    }

                    x += width;
                    if (!space) visible = x;
                    i = clusterEnd;

                    LineBreakOpportunity opportunity = m_Breaks[i - 1];
                    if (opportunity == LineBreakOpportunity.Mandatory && i < paragraph.ContentEnd)
                    {
                        AddLine(lines, ref stats, baseSize, lineStart, i, i - 1, p, true);
                        lineStart = i;
                        lastBreak = -1;
                        x = visible = 0;
                    }
                    else if (opportunity == LineBreakOpportunity.Allowed)
                    {
                        lastBreak = i;
                        xAtBreak = x;
                    }
                }
                AddLine(lines, ref stats, baseSize, lineStart, paragraph.End, visibleBefore(paragraph.ContentEnd, paragraph), p, true);
            }
            return stats;
        }

        // The end of the line's content with trailing whitespace hanging out of it.
        int visibleBefore(int end, in Paragraph paragraph)
        {
            while (end > paragraph.Start && LetteraUnicode.IsWhiteSpace(m_Text[end - 1]) && !IsRemoved(end - 1)) end--;
            return end;
        }

        void AddLine(List<LetteraLine> lines, ref LineStats stats, float baseSize, int start, int end, int visibleEnd, int paragraph, bool mandatory)
        {
            if (visibleEnd < start) visibleEnd = start;
            LetteraTextData data = m_Host.Data;
            LetteraSettings settings = LetteraSettings.Instance;
            LetteraLineHeightMode mode = data.LineHeightMode == LetteraLineHeightMode.Default ? settings.DefaultLineHeightMode : data.LineHeightMode;
            float value = data.LineHeightMode == LetteraLineHeightMode.Default ? settings.DefaultLineHeightValue : data.LineHeightValue;

            float width = 0, em = 0, runAscent = 0, runDescent = 0;
            for (int c = start; c < end && c < m_TextLength;)
            {
                Run run = m_Runs[m_CpRun[c]];
                int stop = Math.Min(run.End, end);
                float size = SizeOf(run, baseSize);
                if (size > em) em = size;
                if (run.Instance != null)
                {
                    FaceMetrics metrics = run.Instance.Face.Metrics;
                    runAscent = Mathf.Max(runAscent, metrics.Ascender * run.UnitEm * size);
                    runDescent = Mathf.Max(runDescent, -metrics.Descender * run.UnitEm * size);
                }
                for (int k = c; k < stop && k < visibleEnd; k++)
                    if (!IsRemoved(k))
                        width += m_CpAdvanceEm[k] * size;
                if (m_RemoveStart >= c && m_RemoveStart < stop) width += m_MarkerEm * size;
                c = stop;
            }
            if (em <= 0) em = EmptyLineSize(start, baseSize);

            FontFace primary = m_Chain.PrimaryFace;
            float ascent = primary != null ? (float)primary.Metrics.Ascender / primary.Metrics.Upem * em : em * 0.8f;
            float descent = primary != null ? -(float)primary.Metrics.Descender / primary.Metrics.Upem * em : em * 0.2f;
            float height;
            switch (mode)
            {
                case LetteraLineHeightMode.Content:
                    ascent = Mathf.Max(ascent, runAscent);
                    descent = Mathf.Max(descent, runDescent);
                    height = (ascent + descent) * value;
                    break;
                case LetteraLineHeightMode.Primary:
                    height = (ascent + descent) * value;
                    break;
                default:
                    height = value * em;
                    break;
            }
            float leading = height - (ascent + descent);
            float above = data.LeadingDistribution == LetteraLeading.Above ? leading : data.LeadingDistribution == LetteraLeading.Below ? 0 : leading * 0.5f;

            lines.Add(new LetteraLine
            {
                Start = start, End = end, VisibleEnd = visibleEnd, Paragraph = paragraph, Width = width, Height = height,
                Ascent = ascent + above, Descent = height - (ascent + above), EndedByMandatoryBreak = mandatory,
            });
            if (width > stats.MaxWidth) stats.MaxWidth = width;
            stats.Height += height;
        }

        float EmptyLineSize(int at, float baseSize)
        {
            if (m_Runs.Count == 0 || m_TextLength == 0) return baseSize;
            int index = Mathf.Clamp(at, 0, m_TextLength - 1);
            return SizeOf(m_Runs[m_CpRun[index]], baseSize);
        }

        // ------------------------------------------------------------- queries

        float QuerySize(bool useMax)
        {
            LetteraTextData data = m_Host.Data;
            return data.AutoSize ? (useMax ? data.MaxFontSize : Mathf.Min(data.MinFontSize, data.MaxFontSize)) : data.FontSize;
        }

        /// <summary>The widest line between forced breaks.</summary>
        public float GetMaxContentWidth()
        {
            EnsureMeasured();
            return BreakLines(m_MeasureLines, QuerySize(true), float.PositiveInfinity, false, false).MaxWidth;
        }

        /// <summary>The narrowest width that splits no word; with wrap off, the widest line.</summary>
        public float GetMinContentWidth()
        {
            EnsureMeasured();
            float size = QuerySize(false);
            if (!m_Host.Data.WordWrap) return BreakLines(m_MeasureLines, size, float.PositiveInfinity, false, false).MaxWidth;
            float widest = 0, word = 0;
            for (int i = 0; i < m_TextLength; i++)
            {
                if (!LetteraUnicode.IsWhiteSpace(m_Text[i])) word += m_CpAdvanceEm[i] * SizeOf(m_Runs[m_CpRun[i]], size);
                if (m_Breaks[i] != LineBreakOpportunity.None)
                {
                    if (word > widest) widest = word;
                    word = 0;
                }
            }
            return Mathf.Max(widest, word);
        }

        /// <summary>The height of the text at a width, excluding padding.</summary>
        public float GetHeightForWidth(float width)
        {
            EnsureMeasured();
            LetteraTextData data = m_Host.Data;
            float size = data.AutoSize ? CurrentFontSize > 0 ? CurrentFontSize : data.FontSize : data.FontSize;
            return BreakLines(m_MeasureLines, size, width, data.WordWrap && width > 0, true).Height;
        }

        /// <summary>Size of the laid-out text in a given box, excluding padding. Used by MeasureText.</summary>
        public Vector2 Measure(float size, float width, float height, bool wrap, bool autoSize, float minSize, float maxSize)
        {
            EnsureMeasured();
            if (autoSize)
            {
                if (Fits(maxSize, width, height, wrap, false))
                {
                    size = maxSize;
                }
                else
                {
                    float low = Mathf.Min(minSize, maxSize), high = maxSize;
                    for (int i = 0; i < 16 && high - low > 0.005f; i++)
                    {
                        float middle = (low + high) * 0.5f;
                        if (Fits(middle, width, height, wrap, false)) low = middle;
                        else high = middle;
                    }
                    size = low;
                }
            }
            LineStats stats = BreakLines(m_MeasureLines, size, width, wrap, true);
            return new Vector2(stats.MaxWidth, stats.Height);
        }
    }
}

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
        Rect m_Box;
        float m_ContentWidth, m_ContentHeight;
        float m_TrimTop;

        // What the last auto-size fit spends, on top of the shaped widths.
        float m_FitTracking, m_FitGlyphScale = 1, m_FitLineHeight = 1;

        struct Removal
        {
            public int Start, End, Anchor, Repeat, Advance;
            public float MarkerEm;
            public FontInstance Instance;
            public uint Glyph;
            public LayoutLength FadeDistance, FadeShift;
        }

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
            m_Box = ComputeBox(data.Padding, HostRect);
            bool degenerateWidth = m_Box.width <= 0;
            bool wrap = data.WordWrap && !degenerateWidth;
            float width = degenerateWidth ? float.PositiveInfinity : m_Box.width;
            float height = m_Box.height <= 0 ? float.PositiveInfinity : m_Box.height;

            ClearRemoval();
            ResetFit();

            float size = data.FontSize;
            // A document cannot be fitted without measuring all of it: it shows at the smallest size, and scrolls.
            if (data.AutoSize) size = IsWindowed ? data.MinFontSize : FitFontSize(data, width, height, wrap);
            CurrentFontSize = size;

            CollectRevealsOnce();
            ApplyRevealRemovals();
            ApplyHookRemovals();
            DecideMarquee(size, width);
            if (m_Aside.MarqueeScrolling) wrap = false;
            if (m_Attributes.Elides.Count > 0 && !SuspendElide && !m_Aside.MarqueeScrolling && !ClipsViewport) ApplyElide(size, width, height, wrap);

            m_Aside.LaidSize = size;
            m_Aside.LaidWidth = width;
            m_Aside.LaidWrap = wrap;
            // A long text has a line at least for each paragraph, and one each while its paragraphs wait: taking that room at once leaves
            // no list of every size on the way behind.
            int paragraphs = m_Paragraphs.Count;
            if (paragraphs > 64 && m_Lines.Capacity < paragraphs) m_Lines.Capacity = paragraphs + (paragraphs >> 3);
            LineStats stats = BreakLines(m_Lines, size, width, wrap, true, LayoutResume(size, width, wrap, data));
            m_ContentWidth = stats.MaxWidth;
            m_ContentHeight = stats.Height;
            RunLinesHooks(size, width, wrap);
            if (m_Aside.RevealSpans.Count > 0)
            {
                NumberRevealUnits();
                UpdateRevealTargets();
            }
        }

        float FitFontSize(LetteraTextData data, float width, float height, bool wrap)
        {
            float max = data.MaxFontSize;
            float min = Mathf.Min(data.MinFontSize, max);
            if (Fits(max, width, height, wrap, false)) return max;
            if (SpendBudgets(data, max, width, height, wrap)) return max;
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
            LineStats stats = BreakLines(m_Aside.MeasureLines, size, width, wrap, emergency);
            return !stats.WordOverflow && stats.MaxWidth <= width + Epsilon && stats.Height <= height + Epsilon;
        }

        void ResetFit()
        {
            m_FitTracking = 0;
            m_FitGlyphScale = 1;
            m_FitLineHeight = 1;
            foreach (LetteraFitBudget budget in m_Host.Data.FitBudgets)
                if (budget != null)
                    budget.Spent = 0;
        }

        // Tries each budget in list order at a size, spending the least that makes the text fit. False leaves them all fully spent.
        bool SpendBudgets(LetteraTextData data, float size, float width, float height, bool wrap)
        {
            foreach (LetteraFitBudget budget in data.FitBudgets)
            {
                if (budget == null) continue;
                SetBudget(budget, 1f);
                if (!Fits(size, width, height, wrap, false)) continue;
                float low = 0, high = 1;
                for (int i = 0; i < 10; i++)
                {
                    float middle = (low + high) * 0.5f;
                    SetBudget(budget, middle);
                    if (Fits(size, width, height, wrap, false)) high = middle;
                    else low = middle;
                }
                SetBudget(budget, high);
                return true;
            }
            return false;
        }

        void SetBudget(LetteraFitBudget budget, float fraction)
        {
            switch (budget)
            {
                case FitByTracking tracking:
                    m_FitTracking = -tracking.Limit * fraction;
                    budget.Spent = tracking.Limit * fraction;
                    break;
                case FitByGlyphWidth glyphs:
                    m_FitGlyphScale = 1 - glyphs.Limit * 0.01f * fraction;
                    budget.Spent = glyphs.Limit * fraction;
                    break;
                case FitByLineHeight lines:
                    m_FitLineHeight = 1 - (100 - lines.Limit) * 0.01f * fraction;
                    budget.Spent = 100 - (100 - lines.Limit) * fraction;
                    break;
            }
        }

        void ClearRemoval()
        {
            Empty(m_Aside.Removals);
        }

        // Each request removes clusters from its own range to make the text fit, the last range first.
        // The component's Overflow was added last, but it covers everything, so it is the last resort.
        void ApplyElide(float size, float width, float height, bool wrap)
        {
            ValueList<LetteraAttributes.ElideRequest> elides = m_Attributes.Elides;
            int component = m_Host.Data.Overflow != LetteraOverflow.Visible ? elides.Count - 1 : -1;
            for (int r = component >= 0 ? elides.Count - 2 : elides.Count - 1; r >= -1; r--)
            {
                int index = r >= 0 ? r : component;
                if (index < 0) break;
                if (Fits(size, width, height, wrap, true)) return;
                ElideOne(elides[index], size, width, height, wrap);
            }
        }

        // Find the fewest clusters to remove, by bisection.
        void ElideOne(in LetteraAttributes.ElideRequest request, float size, float width, float height, bool wrap)
        {
            int start = request.Start, end = Mathf.Min(request.End, m_TextLength);
            if (end <= start) return;

            int clusters = 0;
            for (int i = start; i < end; i++)
                if (m_Boundaries[i] && !IsRemoved(i))
                    clusters++;
            if (clusters == 0) return;

            int slot = m_Aside.Removals.Count;
            Own(ref m_Aside.Removals).Add(default);
            int low = 1, high = clusters;
            while (low < high)
            {
                int middle = (low + high) / 2;
                SetRemoval(slot, request, start, end, clusters, middle);
                if (Fits(size, width, height, wrap, true)) high = middle;
                else low = middle + 1;
            }
            SetRemoval(slot, request, start, end, clusters, low);
        }

        void SetRemoval(int slot, in LetteraAttributes.ElideRequest request, int start, int end, int clusters, int remove)
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
            var removal = new Removal { Start = a, End = b, FadeDistance = request.FadeDistance, FadeShift = request.FadeShift };
            if (request.Marker) PrepareMarker(ref removal, a > 0 ? a - 1 : a);
            m_Aside.Removals[slot] = removal;
        }

        void PrepareMarker(ref Removal removal, int anchor)
        {
            removal.Anchor = Mathf.Min(anchor, m_TextLength - 1);
            if (m_Runs.Count == 0) return;
            Run run = m_Runs[m_CpRun[removal.Anchor]];
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
            removal.Instance = instance;
            removal.Glyph = LetteraNative.lt_font_glyph(instance.Font, (uint)codepoint, 0);
            removal.Repeat = repeat;
            removal.Advance = LetteraNative.lt_glyph_advance(instance.Font, removal.Glyph);
            float unit = instance == run.Instance ? run.UnitEm : 1f / instance.Face.Metrics.Upem;
            removal.MarkerEm = removal.Advance * unit * repeat;
        }

        bool IsRemoved(int cp)
        {
            for (int i = 0; i < m_Aside.Removals.Count; i++)
                if (cp >= m_Aside.Removals[i].Start && cp < m_Aside.Removals[i].End)
                    return true;
            return false;
        }

        // The marker of the removal that starts at a codepoint, in local units.
        float MarkerWidthAt(int cp, float baseSize)
        {
            for (int i = 0; i < m_Aside.Removals.Count; i++)
                if (m_Aside.Removals[i].Start == cp && m_Aside.Removals[i].MarkerEm > 0)
                    return m_Aside.Removals[i].MarkerEm * SizeOf(m_Runs[m_CpRun[m_Aside.Removals[i].Anchor]], baseSize);
            return 0;
        }

        float IndentOf(int cp, float baseSize)
        {
            if (!m_Attributes.HasLayout || cp >= m_TextLength) return 0;
            ref LetteraAttributes.CpLayout layout = ref m_Attributes.Layout[cp];
            float size = SizeOf(m_Runs[m_CpRun[cp]], baseSize);
            float indent = layout.Indent.Of(size) + (m_Aside.ListItems.Count > 0 ? m_Aside.CpListIndent[cp] * size : 0);
            if (layout.IndentPercent != 0) indent += layout.IndentPercent * 0.01f * HostRect.width;
            return indent < 0 ? 0 : indent;
        }

        // A range that opens inside a line moves what follows it: the rise in indent at a codepoint.
        float IndentRiseAt(int cp, int lineStart, float baseSize)
        {
            if (!m_Attributes.HasLayout || cp <= lineStart) return 0;
            float here = IndentOf(cp, baseSize), before = IndentOf(cp - 1, baseSize);
            return here > before ? here - before : 0;
        }

        LineStats BreakLines(List<LetteraLine> lines, float baseSize, float maxWidth, bool wrap, bool emergency, int firstParagraph = 0)
        {
            var stats = new LineStats();
            if (firstParagraph > 0)
            {
                // The lines of the paragraphs before stay, and so does what they add up to.
                int keep = lines.Count;
                while (keep > 0 && lines[keep - 1].Paragraph >= firstParagraph) keep--;
                lines.RemoveRange(keep, lines.Count - keep);
                for (int l = 0; l < keep; l++)
                {
                    LetteraLine kept = lines[l];
                    if (kept.Width + kept.Margin > stats.MaxWidth) stats.MaxWidth = kept.Width + kept.Margin;
                    stats.Height += kept.Height + kept.GapBefore;
                }
            }
            else
            {
                lines.Clear();
            }
            if (!BreakInParallel(lines, ref stats, baseSize, maxWidth, wrap, emergency, firstParagraph, m_Paragraphs.Count))
                BreakParagraphs(lines, ref stats, baseSize, maxWidth, wrap, emergency, firstParagraph, m_Paragraphs.Count);
            if (m_Attributes.TrimTop != LetteraTrimTop.None || m_Attributes.TrimBottom != LetteraTrimBottom.None) ApplyTrim(lines, ref stats, baseSize);
            else m_TrimTop = 0;
            return stats;
        }

        void BreakParagraphs(List<LetteraLine> lines, ref LineStats stats, float baseSize, float maxWidth, bool wrap, bool emergency, int firstParagraph, int endParagraph) =>
            BreakParagraphs(lines, ref stats, baseSize, maxWidth, wrap, emergency, firstParagraph, endParagraph, CurrentLineFormat());

        void BreakParagraphs(List<LetteraLine> lines, ref LineStats stats, float baseSize, float maxWidth, bool wrap, bool emergency, int firstParagraph, int endParagraph, in LineFormat format)
        {
            // What a line keeps from the one before: workers each break a stretch, so it is no field.
            float pendingGap = 0;
            bool simple = m_Aside.Removals.Count == 0 && !m_Attributes.HasLayout;
            int sizedRun = -1;
            float runSize = 0;
            int previousParagraph = firstParagraph - 1;
            for (int p = firstParagraph; p < endParagraph; p++)
            {
                Paragraph paragraph = m_Paragraphs[p];
                if (paragraph.Pending)
                {
                    AddEstimatedLine(lines, ref stats, paragraph, p, baseSize, maxWidth, wrap);
                    continue;
                }
                if (m_Aside.Removals.Count > 0 && IsWhollyRemoved(paragraph)) continue;
                int firstLine = lines.Count;

                if (previousParagraph >= 0 && m_Attributes.ParagraphSpacings.Count > 0)
                    pendingGap += ParagraphGap(m_Paragraphs[previousParagraph], paragraph, baseSize);
                previousParagraph = p;

                int lineStart = paragraph.Start, lastBreak = -1;
                float x = 0, visible = 0, xAtBreak = 0;
                float margin = IndentOf(lineStart, baseSize);
                int i = paragraph.Start;
                while (i < paragraph.ContentEnd)
                {
                    int clusterEnd = i + 1;
                    while (clusterEnd < paragraph.ContentEnd && !m_Boundaries[clusterEnd]) clusterEnd++;

                    float width;
                    bool space = true;
                    if (simple)
                    {
                        // Nothing removed and no layout effect: a cluster is its codepoints' widths, at its run's size.
                        width = 0;
                        for (int c = i; c < clusterEnd; c++)
                        {
                            int run = m_CpRun[c];
                            if (run != sizedRun)
                            {
                                sizedRun = run;
                                runSize = SizeOf(m_Runs[run], baseSize);
                            }
                            width += CpWidth(c, runSize);
                            space &= LetteraUnicode.IsWhiteSpace(m_Text[c]) || (!m_NoExtras && m_CpDivider[c]);
                        }
                    }
                    else
                    {
                        width = IndentRiseAt(i, lineStart, baseSize);
                        for (int c = i; c < clusterEnd; c++)
                        {
                            width += MarkerWidthAt(c, baseSize);
                            if (IsRemoved(c)) { space &= false; continue; }
                            width += CpWidth(c, SizeOf(m_Runs[m_CpRun[c]], baseSize));
                            // A divider never forces a break by itself: it hangs out of a line it ends, like a space.
                            space &= LetteraUnicode.IsWhiteSpace(m_Text[c]) || (!m_NoExtras && m_CpDivider[c]);
                        }
                    }

                    if (wrap && !space && x + width > maxWidth - margin + Epsilon && x > 0)
                    {
                        if (lastBreak > lineStart)
                        {
                            AddLine(lines, ref stats, baseSize, lineStart, lastBreak, Math.Min(visibleBefore(lastBreak, paragraph), lastBreak), p, false, margin, format, ref pendingGap);
                            x -= xAtBreak;
                            visible = Mathf.Max(visible - xAtBreak, 0);
                            lineStart = lastBreak;
                            margin = IndentOf(lineStart, baseSize);
                            lastBreak = -1;
                            continue;
                        }
                        if (emergency)
                        {
                            AddLine(lines, ref stats, baseSize, lineStart, i, Math.Min(visibleBefore(i, paragraph), i), p, false, margin, format, ref pendingGap);
                            lineStart = i;
                            margin = IndentOf(lineStart, baseSize);
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
                        AddLine(lines, ref stats, baseSize, lineStart, i, i - 1, p, true, margin, format, ref pendingGap);
                        lineStart = i;
                        margin = IndentOf(lineStart, baseSize);
                        lastBreak = -1;
                        x = visible = 0;
                    }
                    else if (opportunity == LineBreakOpportunity.Allowed)
                    {
                        lastBreak = i;
                        xAtBreak = x;
                    }
                }
                AddLine(lines, ref stats, baseSize, lineStart, paragraph.End, visibleBefore(paragraph.ContentEnd, paragraph), p, true, margin, format, ref pendingGap);
                if (m_Deferring) LearnLines(lines, firstLine, baseSize, maxWidth, wrap);
            }
        }

        bool IsWhollyRemoved(in Paragraph paragraph)
        {
            foreach (Removal removal in m_Aside.Removals)
                if (paragraph.Start > removal.Start && paragraph.ContentEnd <= removal.End && paragraph.Start < removal.End)
                    return true;
            return false;
        }

        float ParagraphGap(in Paragraph above, in Paragraph below, float baseSize)
        {
            float gap = 0;
            if (above.Start < m_TextLength)
            {
                short index = m_Attributes.Layout[above.Start].ParagraphSpacing;
                if (index >= 0) gap += m_Attributes.ParagraphSpacings[index].After.Of(SizeOf(m_Runs[m_CpRun[above.Start]], baseSize));
            }
            if (below.Start < m_TextLength)
            {
                short index = m_Attributes.Layout[below.Start].ParagraphSpacing;
                if (index >= 0) gap += m_Attributes.ParagraphSpacings[index].Before.Of(SizeOf(m_Runs[m_CpRun[below.Start]], baseSize));
            }
            return gap;
        }

        // The first and last lines lose what lies beyond the chosen edges. Nothing drawn after shaping moves an edge.
        void ApplyTrim(List<LetteraLine> lines, ref LineStats stats, float baseSize)
        {
            m_TrimTop = 0;
            if (lines.Count == 0) return;
            LetteraLine first = lines[0], last = lines[lines.Count - 1];
            float top = 0;
            switch (m_Attributes.TrimTop)
            {
                case LetteraTrimTop.Ascent: top = first.Ascent - LineMetric(first, baseSize, 0); break;
                case LetteraTrimTop.CapHeight: top = first.Ascent - LineMetric(first, baseSize, 1); break;
                case LetteraTrimTop.XHeight: top = first.Ascent - LineMetric(first, baseSize, 2); break;
                case LetteraTrimTop.Ink: top = first.Ascent - InkOf(first, baseSize, true, first.Ascent); break;
            }
            float bottom = 0;
            switch (m_Attributes.TrimBottom)
            {
                case LetteraTrimBottom.Descent: bottom = last.Descent - LineMetric(last, baseSize, 3); break;
                case LetteraTrimBottom.Baseline: bottom = last.Descent; break;
                case LetteraTrimBottom.Ink: bottom = last.Descent - InkOf(last, baseSize, false, last.Descent); break;
            }
            m_TrimTop = top;
            stats.Height -= top + bottom;
        }

        // 0 ascent, 1 cap height, 2 x-height, 3 descent of the primary face at the line's largest size.
        float LineMetric(in LetteraLine line, float baseSize, int metric)
        {
            FontFace primary = m_Chain.PrimaryFace;
            float em = 0;
            for (int c = line.Start; c < line.End && c < m_TextLength; c++) em = Mathf.Max(em, SizeOf(m_Runs[m_CpRun[c]], baseSize));
            if (em <= 0) em = baseSize;
            if (primary == null) return metric == 3 ? em * 0.2f : em * 0.8f;
            FaceMetrics m = primary.Metrics;
            int units = metric == 0 ? m.Ascender : metric == 1 ? m.CapHeight : metric == 2 ? m.XHeight : -m.Descender;
            return (float)units / m.Upem * em;
        }

        // The highest ink above, or the lowest ink below, the baseline of a line; a line with no outlines keeps its ascent or descent.
        float InkOf(in LetteraLine line, float baseSize, bool up, float fallback)
        {
            float ink = float.MinValue;
            int* extents = stackalloc int[4];
            for (int r = 0; r < m_Runs.Count; r++)
            {
                Run run = m_Runs[r];
                if (run.Instance == null || run.Instance.Face.IsVirtual || run.End <= line.Start || run.Start >= line.End) continue;
                float unit = run.UnitEm * SizeOf(run, baseSize);
                for (int g = run.GlyphStart; g < run.GlyphStart + run.GlyphCount; g++)
                {
                    ref ShapedGlyph glyph = ref m_Shaped[g];
                    if (glyph.Cluster < line.Start || glyph.Cluster >= line.End) continue;
                    if (LetteraNative.lt_glyph_extents(run.Instance.Font, glyph.Glyph, extents) == 0) continue;
                    if (extents[3] == 0 && extents[2] == 0) continue;
                    // Extents are x bearing, y bearing, width, and a negative height.
                    float yOffset = glyph.YOffset * unit;
                    float top = extents[1] * unit + yOffset, bottom = (extents[1] + extents[3]) * unit + yOffset;
                    ink = Mathf.Max(ink, up ? top : -bottom);
                }
            }
            return ink == float.MinValue ? fallback : ink;
        }

        // The end of the line's content with trailing whitespace, and a divider it broke at, hanging out of it.
        int visibleBefore(int end, in Paragraph paragraph)
        {
            if (m_Attributes.Dividers.Count > 0 && end < paragraph.ContentEnd && IsDividerEnd(end, out int dividerStart)) end = dividerStart;
            while (end > paragraph.Start && LetteraUnicode.IsWhiteSpace(m_Text[end - 1]) && !IsRemoved(end - 1)) end--;
            return end;
        }

        // What every line of one breaking reads alike, read once: workers then touch no Unity object.
        struct LineFormat
        {
            public LetteraLineHeightMode Mode;
            public float Value;
            public int Leading;
            public bool HasPrimary;
            public int Ascender, Descender, Upem;
        }

        LineFormat CurrentLineFormat()
        {
            LetteraTextData data = m_Host.Data;
            LetteraSettings settings = SettingsNow;
            var format = new LineFormat
            {
                Mode = data.LineHeightMode == LetteraLineHeightMode.Default ? settings.DefaultLineHeightMode : data.LineHeightMode,
                Value = data.LineHeightMode == LetteraLineHeightMode.Default ? settings.DefaultLineHeightValue : data.LineHeightValue,
                Leading = (int)data.LeadingDistribution,
            };
            FontFace primary = m_Chain.PrimaryFace;
            if (primary != null)
            {
                FaceMetrics metrics = primary.Metrics;
                format.HasPrimary = true;
                format.Ascender = metrics.Ascender;
                format.Descender = metrics.Descender;
                format.Upem = metrics.Upem;
            }
            return format;
        }

        void AddLine(List<LetteraLine> lines, ref LineStats stats, float baseSize, int start, int end, int visibleEnd, int paragraph, bool mandatory, float margin, in LineFormat format, ref float pendingGap)
        {
            if (visibleEnd < start) visibleEnd = start;
            LetteraLineHeightMode mode = format.Mode;
            float value = format.Value;

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
                {
                    if (m_Attributes.HasLayout) width += IndentRiseAt(k, start, baseSize);
                    if (!IsRemoved(k)) width += CpWidth(k, size);
                }
                for (int k = c; k < stop; k++) width += MarkerWidthAt(k, baseSize);
                c = stop;
            }
            if (m_Attributes.HasLayout || m_FitTracking != 0) width -= TrailingTrackingOf(start, visibleEnd, baseSize);
            if (em <= 0) em = EmptyLineSize(start, baseSize);

            float ascent = format.HasPrimary ? (float)format.Ascender / format.Upem * em : em * 0.8f;
            float descent = format.HasPrimary ? -(float)format.Descender / format.Upem * em : em * 0.2f;
            float primaryAscent = ascent, primaryDescent = descent;
            float contentAscent = Mathf.Max(ascent, runAscent), contentDescent = Mathf.Max(descent, runDescent);
            int leadingMode = format.Leading;
            float height = HeightFor(mode, value, em, primaryAscent, primaryDescent, contentAscent, contentDescent);
            if (mode == LetteraLineHeightMode.Content) { ascent = contentAscent; descent = contentDescent; }

            if (m_Attributes.LineHeights.Count > 0)
            {
                // A line partly covered by a range takes the largest value any of its clusters asks for.
                float best = -1;
                for (int c = start; c < end && c < m_TextLength; c++)
                {
                    short index = m_Attributes.Layout[c].LineHeight;
                    if (index < 0) continue;
                    LineHeightSpec spec = m_Attributes.LineHeights[index];
                    LetteraLineHeightMode specMode = spec.Mode == LetteraLineHeightMode.Default ? mode : spec.Mode;
                    float requested = spec.IsAbsolute ? spec.Absolute.Of(em) : HeightFor(specMode, spec.Multiplier, em, primaryAscent, primaryDescent, contentAscent, contentDescent);
                    if (requested <= best) continue;
                    best = requested;
                    ascent = specMode == LetteraLineHeightMode.Content ? contentAscent : primaryAscent;
                    descent = specMode == LetteraLineHeightMode.Content ? contentDescent : primaryDescent;
                    if (spec.Leading >= 0) leadingMode = spec.Leading;
                }
                if (best >= 0) height = best;
            }
            height *= m_FitLineHeight;

            // What stands in the line grows it only past the room it already has, in every height mode.
            if (m_Aside.InlineItems.Count > 0)
            {
                float roomAbove = ascent, roomBelow = descent;
                InlineLineExtents(start, end, em, ref roomAbove, ref roomBelow);
                height += roomAbove - ascent + (roomBelow - descent);
                ascent = roomAbove;
                descent = roomBelow;
            }

            float leading = height - (ascent + descent);
            float above = leadingMode == (int)LetteraLeading.Above ? leading : leadingMode == (int)LetteraLeading.Below ? 0 : leading * 0.5f;

            float rubyOver = 0, rubyUnder = 0;
            if (m_Aside.RubyItems.Count > 0)
            {
                RubyExtents(start, end, em, out rubyOver, out rubyUnder, out _);
                height += rubyOver + rubyUnder;
                above += rubyOver;
            }

            float gap = lines.Count > 0 ? pendingGap : 0;
            if (lines.Count > 0 && m_Attributes.LineGaps.Count > 0)
            {
                LetteraLine before = lines[lines.Count - 1];
                foreach (LineGapSpec lineGap in m_Attributes.LineGaps)
                    if (lineGap.End > before.Start && lineGap.End <= before.End)
                        gap += lineGap.Amount.Of(em);
            }
            pendingGap = 0;

            lines.Add(new LetteraLine
            {
                Start = start, End = end, VisibleEnd = visibleEnd, Paragraph = paragraph, Width = width, Height = height,
                Ascent = ascent + above, Descent = height - (ascent + above), EndedByMandatoryBreak = mandatory,
                GapBefore = gap, Margin = margin, RubyOver = rubyOver, RubyUnder = rubyUnder,
            });
            if (width + margin > stats.MaxWidth) stats.MaxWidth = width + margin;
            stats.Height += height + gap;
        }

        static float HeightFor(LetteraLineHeightMode mode, float value, float em, float ascent, float descent, float contentAscent, float contentDescent)
        {
            switch (mode)
            {
                case LetteraLineHeightMode.Content: return (contentAscent + contentDescent) * value;
                case LetteraLineHeightMode.Primary: return (ascent + descent) * value;
                default: return value * em;
            }
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
            return BreakLines(m_Aside.MeasureLines, QuerySize(true), float.PositiveInfinity, false, false).MaxWidth;
        }

        /// <summary>The narrowest width that splits no word; with wrap off, the widest line.</summary>
        public float GetMinContentWidth()
        {
            EnsureMeasured();
            float size = QuerySize(false);
            if (!m_Host.Data.WordWrap) return BreakLines(m_Aside.MeasureLines, size, float.PositiveInfinity, false, false).MaxWidth;
            float widest = 0, word = 0;
            for (int i = 0; i < m_TextLength; i++)
            {
                if (!LetteraUnicode.IsWhiteSpace(m_Text[i])) word += CpWidth(i, SizeOf(m_Runs[m_CpRun[i]], size));
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
            // A layout asking at the text's own width gets the estimate while paragraphs wait; any other question waits for all of them.
            EnsureMeasured(false);
            LetteraTextData data = m_Host.Data;
            // Asked for the width the text has, the layout it is about to run answers, so a layout group's question costs no second line breaking.
            if (!data.AutoSize && width > 0 && m_Attributes.Elides.Count == 0 && m_Aside.Marquee == null && m_Aside.StageHooks.Count == 0 && m_Aside.RevealSpans.Count == 0 &&
                width == ComputeBox(data.Padding, m_Host.LayoutRect).width)
            {
                if (m_Dirty >= DirtyLevel.Layout)
                {
                    RunLayout();
                    m_EarlyChanges |= LetteraChanges.Layout;
                    m_Dirty = DirtyLevel.Positions;
                }
                if (m_Aside.Removals.Count == 0 && m_FitGlyphScale == 1 && m_FitTracking == 0 && m_FitLineHeight == 1) return m_ContentHeight;
            }
            if (m_PendingCount > 0) FinishForQuery();
            float size = data.AutoSize ? CurrentFontSize > 0 ? CurrentFontSize : data.FontSize : data.FontSize;
            return BreakLines(m_Aside.MeasureLines, size, width, data.WordWrap && width > 0, true).Height;
        }

        /// <summary>Size of the laid-out text in a given box, excluding padding. Used by MeasureText.</summary>
        public Vector2 Measure(float size, float width, float height, bool wrap, bool autoSize, float minSize, float maxSize)
        {
            EnsureMeasured();
            ResetFit();
            if (autoSize)
            {
                if (Fits(maxSize, width, height, wrap, false) || SpendBudgets(m_Host.Data, maxSize, width, height, wrap))
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
            LineStats stats = BreakLines(m_Aside.MeasureLines, size, width, wrap, true);
            return new Vector2(stats.MaxWidth, stats.Height);
        }
    }
}

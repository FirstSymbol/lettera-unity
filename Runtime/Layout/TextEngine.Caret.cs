using System;
using System.Collections.Generic;
using FirstSymbol.Lettera.Unicode;
using UnityEngine;

namespace FirstSymbol.Lettera
{
    /// <summary>
    /// Which of two possible spots an index means: at a soft wrap it is the end of a line or the start of the next,
    /// and at a change of direction the edge of the cluster before it or of the cluster after it.
    /// </summary>
    public enum LetteraCaretBias { Forward, Backward }

    // Where each index stands on screen. A text nobody selects never builds any of this.
    internal sealed partial class TextEngine
    {
        // The glyphs of one shaped cluster, on one line. Several grapheme clusters can share one in a ligature.
        struct CaretSegment
        {
            public int Start, End, Line;
            public float Left, Right, Baseline;
            public bool Rtl;
        }

        CaretSegment[] m_Segments = new CaretSegment[64];   // By line, each line in visual order.
        int[] m_SegmentStarts = new int[64];                // Keys and, below, indices of the segments in logical order.
        int[] m_LogicalOrder = new int[64];
        int[] m_LineSegments = new int[16];                 // The first segment of each line.
        int m_SegmentCount;
        bool m_SegmentsValid;

        void EnsureSegments()
        {
            if (m_SegmentsValid) return;
            m_SegmentsValid = true;
            m_SegmentCount = 0;
            EnsureSize(ref m_Segments, m_RealCount + 1);
            EnsureSize(ref m_SegmentStarts, m_RealCount + 1);
            EnsureSize(ref m_LogicalOrder, m_RealCount + 1);
            EnsureSize(ref m_LineSegments, m_Lines.Count + 1);

            bool curved = m_Attributes.HasCurve;
            int glyph = 0;
            for (int l = 0; l < m_Lines.Count; l++)
            {
                m_LineSegments[l] = m_SegmentCount;
                int first = m_SegmentCount;
                float baseline = m_Lines[l].Baseline;
                for (; glyph < m_RealCount && m_Placed[glyph].Line == l; glyph++)
                {
                    ref PlacedGlyph g = ref m_Placed[glyph];
                    if (g.Marker) continue;
                    float left = curved ? g.X : g.Pen, right = left + g.Advance;
                    if (m_SegmentCount > first && m_Segments[m_SegmentCount - 1].Start == g.Cluster)
                    {
                        ref CaretSegment last = ref m_Segments[m_SegmentCount - 1];
                        if (left < last.Left) last.Left = left;
                        if (right > last.Right) last.Right = right;
                        continue;
                    }
                    m_Segments[m_SegmentCount++] = new CaretSegment
                    {
                        Start = g.Cluster, End = g.Cluster + 1, Line = l, Left = left, Right = right,
                        Baseline = curved ? g.Y : baseline, Rtl = (m_Levels[g.Cluster] & 1) != 0,
                    };
                }
                // Justification opens space after a cluster, which belongs to it.
                if (!curved)
                    for (int s = first; s + 1 < m_SegmentCount; s++)
                        if (m_Segments[s + 1].Left > m_Segments[s].Right)
                            m_Segments[s].Right = m_Segments[s + 1].Left;
            }
            m_LineSegments[m_Lines.Count] = m_SegmentCount;

            for (int i = 0; i < m_SegmentCount; i++)
            {
                m_SegmentStarts[i] = m_Segments[i].Start;
                m_LogicalOrder[i] = i;
            }
            Array.Sort(m_SegmentStarts, m_LogicalOrder, 0, m_SegmentCount);
            for (int i = 0; i < m_SegmentCount; i++)
            {
                ref CaretSegment segment = ref m_Segments[m_LogicalOrder[i]];
                int end = i + 1 < m_SegmentCount ? m_SegmentStarts[i + 1] : m_TextLength;
                end = Math.Min(end, m_Lines[segment.Line].VisibleEnd);
                for (int r = 0; r < m_Removals.Count; r++)
                    if (m_Removals[r].Start > segment.Start && m_Removals[r].Start < end)
                        end = m_Removals[r].Start;
                segment.End = Math.Max(end, segment.Start + 1);
            }
        }

        // The segment holding a codepoint, or -1.
        int SegmentAt(int cp)
        {
            int low = 0, high = m_SegmentCount - 1, found = -1;
            while (low <= high)
            {
                int middle = (low + high) / 2;
                if (m_SegmentStarts[middle] <= cp)
                {
                    found = middle;
                    low = middle + 1;
                }
                else
                {
                    high = middle - 1;
                }
            }
            if (found < 0) return -1;
            int index = m_LogicalOrder[found];
            return cp < m_Segments[index].End ? index : -1;
        }

        public int LineOf(int index, LetteraCaretBias bias)
        {
            int count = m_Lines.Count;
            if (count == 0) return 0;
            int low = 0, high = count - 1;
            while (low < high)
            {
                int middle = (low + high + 1) / 2;
                if (m_Lines[middle].Start <= index) low = middle;
                else high = middle - 1;
            }
            if (bias == LetteraCaretBias.Backward && low > 0 && m_Lines[low].Start == index && !m_Lines[low - 1].EndedByMandatoryBreak) low--;
            return low;
        }

        // How far along a segment a stop inside it stands: a ligature's width is shared evenly by its grapheme clusters.
        float FractionInside(in CaretSegment segment, int index)
        {
            int total = 0, before = 0;
            for (int c = segment.Start; c < segment.End; c++)
            {
                if (!m_Boundaries[c]) continue;
                total++;
                if (c < index) before++;
            }
            return total > 0 ? (float)before / total : 0;
        }

        static float EdgeAt(in CaretSegment segment, float fraction) =>
            segment.Rtl ? segment.Right - (segment.Right - segment.Left) * fraction : segment.Left + (segment.Right - segment.Left) * fraction;

        bool SegmentOnLine(int cp, int line, out CaretSegment segment)
        {
            int index = cp >= 0 && cp < m_TextLength ? SegmentAt(cp) : -1;
            if (index >= 0 && m_Segments[index].Line == line)
            {
                segment = m_Segments[index];
                return true;
            }
            segment = default;
            return false;
        }

        // Trailing whitespace hangs out of a line and has no glyphs. Its width still moves the caret, up to the box's edge at a soft wrap.
        float HangingEdge(in LetteraLine line, int index)
        {
            bool rtl = (m_Paragraphs[line.Paragraph].Level & 1) != 0;
            float width = 0;
            for (int c = line.VisibleEnd; c < index && c < m_TextLength; c++)
                if (!IsLineBreakChar(m_Text[c]))
                    width += CpWidth(c, SizeOf(m_Runs[m_CpRun[c]], CurrentFontSize));
            if (rtl)
            {
                float x = line.X - width;
                return line.EndedByMandatoryBreak || m_Box.width <= 0 ? x : Mathf.Max(x, m_Box.xMin);
            }
            float right = line.X + line.Width + width;
            return line.EndedByMandatoryBreak || m_Box.width <= 0 ? right : Mathf.Min(right, m_Box.xMax);
        }

        static bool IsLineBreakChar(int cp) => cp == '\n' || cp == '\r' || cp == 0x85 || cp == 0x2028 || cp == 0x2029 || cp == 0x0B || cp == 0x0C;

        int SnapOutOfRemoved(int index)
        {
            for (int r = 0; r < m_Removals.Count; r++)
                if (index > m_Removals[r].Start && index < m_Removals[r].End)
                    return m_Removals[r].Start;
            return index;
        }

        // The horizontal spot of a caret, and the cluster that decides its height.
        float CaretX(int index, LetteraCaretBias bias, int lineIndex, out int cluster, out float baseline)
        {
            LetteraLine line = m_Lines[lineIndex];
            baseline = line.Baseline;
            cluster = -1;
            CaretSegment segment;
            if (bias == LetteraCaretBias.Backward && index > line.Start && SegmentOnLine(index - 1, lineIndex, out segment))
            {
                cluster = index - 1;
                baseline = segment.Baseline;
                return EdgeAt(segment, segment.End == index ? 1 : FractionInside(segment, index));
            }
            if (SegmentOnLine(index, lineIndex, out segment))
            {
                cluster = index;
                baseline = segment.Baseline;
                return EdgeAt(segment, FractionInside(segment, index));
            }
            if (index > line.VisibleEnd) return HangingEdge(line, index);
            if (index > line.Start && SegmentOnLine(index - 1, lineIndex, out segment))
            {
                cluster = index - 1;
                baseline = segment.Baseline;
                return EdgeAt(segment, 1);
            }
            if (line.VisibleEnd > line.Start) return HangingEdge(line, index);
            return line.X;
        }

        public bool TryGetCaretRect(int index, LetteraCaretBias bias, out Rect rect)
        {
            EnsureSegments();
            index = SnapOutOfRemoved(Math.Min(Math.Max(index, 0), m_TextLength));
            float x, baseline, ascent, descent;
            int cluster = -1;
            if (m_Lines.Count == 0)
            {
                EmptyTextCaret(out x, out baseline);
                PrimaryExtents(CurrentFontSize > 0 ? CurrentFontSize : m_Host.Data.FontSize, out ascent, out descent);
            }
            else
            {
                int lineIndex = LineOf(index, bias);
                x = CaretX(index, bias, lineIndex, out cluster, out baseline);
                if (cluster < 0) cluster = NeighbourOnLine(index, lineIndex);
                if (cluster >= 0 && m_Runs[m_CpRun[cluster]].Instance != null)
                {
                    Run run = m_Runs[m_CpRun[cluster]];
                    float size = SizeOf(run, CurrentFontSize);
                    FaceMetrics metrics = run.Instance.Face.Metrics;
                    ascent = metrics.Ascender * run.UnitEm * size;
                    descent = -metrics.Descender * run.UnitEm * size;
                    if (!m_Attributes.HasCurve) baseline += run.BaselineShift * CurrentFontSize;
                }
                else
                {
                    PrimaryExtents(EmptyLineSize(index, CurrentFontSize), out ascent, out descent);
                }
            }
            rect = new Rect(x, baseline - descent, 0, ascent + descent);
            return true;
        }

        // Size and font on an empty line or in hanging whitespace come from what stands nearest.
        int NeighbourOnLine(int index, int lineIndex)
        {
            LetteraLine line = m_Lines[lineIndex];
            if (index > line.Start && index - 1 < m_TextLength) return index - 1;
            return index < line.End && index < m_TextLength ? index : -1;
        }

        void PrimaryExtents(float size, out float ascent, out float descent)
        {
            FontFace primary = m_Chain.PrimaryFace;
            ascent = primary != null ? (float)primary.Metrics.Ascender / primary.Metrics.Upem * size : size * 0.8f;
            descent = primary != null ? -(float)primary.Metrics.Descender / primary.Metrics.Upem * size : size * 0.2f;
        }

        // Empty text lays out no line, and its measured size has to stay zero. A caret still needs somewhere to stand.
        void EmptyTextCaret(out float x, out float baseline)
        {
            LetteraTextData data = m_Host.Data;
            float size = CurrentFontSize > 0 ? CurrentFontSize : data.FontSize;
            var stats = new LineStats();
            m_MeasureLines.Clear();
            m_PendingGap = 0;
            AddLine(m_MeasureLines, ref stats, size, 0, 0, 0, 0, true, 0);
            LetteraLine line = m_MeasureLines[0];
            int direction = m_Attributes.DirectionOverride >= 0 ? m_Attributes.DirectionOverride : data.BaseDirection == LetteraDirection.RightToLeft ? 1 : 0;
            var paragraph = new Paragraph { Level = direction };
            x = AlignLine(data, line, paragraph, m_Host.ClipsOverflow, out _);
            float top = m_Box.yMax;
            if (data.VerticalAlignment == LetteraVerticalAlignment.Middle) top = m_Box.yMax - (m_Box.height - line.Height) * 0.5f;
            else if (data.VerticalAlignment == LetteraVerticalAlignment.Bottom) top = m_Box.yMin + line.Height;
            baseline = top - line.Ascent;
        }

        public void GetRangeRects(int start, int end, List<Rect> rects)
        {
            int first = rects.Count;
            ContentRangeRects(start, end, rects);
            if (m_WindowOffset == 0) return;
            for (int r = first; r < rects.Count; r++) rects[r] = ScrolledRect(rects[r]);
        }

        void ContentRangeRects(int start, int end, List<Rect> rects)
        {
            EnsureSegments();
            start = Math.Max(start, 0);
            end = Math.Min(end, m_TextLength);
            if (start >= end) return;
            for (int l = LineOf(start, LetteraCaretBias.Forward); l < m_Lines.Count; l++)
            {
                LetteraLine line = m_Lines[l];
                if (line.Start >= end) break;
                if (line.End <= start && line.End != line.Start) continue;
                float bottom = line.Baseline - line.Descent, height = line.Height;
                bool open = false;
                float left = 0, right = 0;

                for (int s = m_LineSegments[l]; s < m_LineSegments[l + 1]; s++)
                {
                    ref CaretSegment segment = ref m_Segments[s];
                    int from = Math.Max(segment.Start, start), to = Math.Min(segment.End, end);
                    if (from >= to) continue;
                    float a = EdgeAt(segment, from == segment.Start ? 0 : FractionInside(segment, from));
                    float b = EdgeAt(segment, to == segment.End ? 1 : FractionInside(segment, to));
                    float low = Mathf.Min(a, b), high = Mathf.Max(a, b);
                    if (m_Attributes.HasCurve)
                    {
                        rects.Add(new Rect(low, segment.Baseline - line.Descent, high - low, height));
                        continue;
                    }
                    if (open && Mathf.Abs(low - right) <= Epsilon)
                    {
                        right = high;
                        continue;
                    }
                    if (open) rects.Add(new Rect(left, bottom, right - left, height));
                    open = true;
                    left = low;
                    right = high;
                }
                if (open) rects.Add(new Rect(left, bottom, right - left, height));
                AddHangingRect(line, start, end, bottom, height, rects);
            }
        }

        // Selected trailing whitespace shows up to the box's edge, and a selected line break as a small tail, so an empty line shows too.
        void AddHangingRect(in LetteraLine line, int start, int end, float bottom, float height, List<Rect> rects)
        {
            int from = Math.Max(start, line.VisibleEnd), to = Math.Min(end, line.End);
            if (from >= to) return;
            bool rtl = (m_Paragraphs[line.Paragraph].Level & 1) != 0;
            float a = HangingEdge(line, from), b = HangingEdge(line, to);
            bool breakSelected = false;
            for (int c = from; c < to && !breakSelected; c++) breakSelected = IsLineBreakChar(m_Text[c]);
            if (breakSelected)
            {
                float tail = 0.25f * EmptyLineSize(line.Start, CurrentFontSize);
                b += rtl ? -tail : tail;
            }
            float low = Mathf.Min(a, b), high = Mathf.Max(a, b);
            if (high - low <= Epsilon) return;
            if (rects.Count > 0)
            {
                Rect last = rects[rects.Count - 1];
                if (last.y == bottom && (Mathf.Abs(last.xMax - low) <= Epsilon || Mathf.Abs(last.xMin - high) <= Epsilon))
                {
                    rects[rects.Count - 1] = Rect.MinMaxRect(Mathf.Min(last.xMin, low), bottom, Mathf.Max(last.xMax, high), bottom + height);
                    return;
                }
            }
            rects.Add(new Rect(low, bottom, high - low, height));
        }

        readonly List<Rect> m_RangeScratch = new List<Rect>();

        public bool TryGetRangeBounds(int start, int end, out Rect bounds)
        {
            m_RangeScratch.Clear();
            GetRangeRects(start, end, m_RangeScratch);
            bounds = default;
            if (m_RangeScratch.Count == 0) return false;
            bounds = m_RangeScratch[0];
            for (int i = 1; i < m_RangeScratch.Count; i++)
            {
                Rect r = m_RangeScratch[i];
                bounds = Rect.MinMaxRect(Mathf.Min(bounds.xMin, r.xMin), Mathf.Min(bounds.yMin, r.yMin), Mathf.Max(bounds.xMax, r.xMax), Mathf.Max(bounds.yMax, r.yMax));
            }
            return true;
        }

        /// <summary>The caret index at a local point: left half of a cluster gives its start, right half its end, mirrored in RTL.</summary>
        public int HitTestCaret(Vector2 point) => HitTestCaret(point, out _);

        public int HitTestCaret(Vector2 point, out LetteraCaretBias bias)
        {
            bias = LetteraCaretBias.Forward;
            if (m_Lines.Count == 0) return 0;
            point = MapToContent(point);
            EnsureSegments();
            if (m_Attributes.HasCurve) return HitTestCurved(point, out bias);

            int low = 0, high = m_Lines.Count - 1;
            while (low < high)
            {
                int middle = (low + high) / 2;
                LetteraLine candidate = m_Lines[middle];
                if (point.y >= candidate.Baseline - candidate.Descent) high = middle;
                else low = middle + 1;
            }
            return HitTestLine(low, point.x, out bias);
        }

        /// <summary>The caret on a given line nearest a horizontal position.</summary>
        public int HitTestLine(int lineIndex, float x, out LetteraCaretBias bias)
        {
            EnsureSegments();
            bias = LetteraCaretBias.Forward;
            if (m_Lines.Count == 0) return 0;
            lineIndex = Math.Min(Math.Max(lineIndex, 0), m_Lines.Count - 1);
            LetteraLine line = m_Lines[lineIndex];
            int first = m_LineSegments[lineIndex], stop = m_LineSegments[lineIndex + 1];
            if (first == stop)
            {
                if (line.VisibleEnd >= LineEndStop(line)) return line.Start;
            }
            bool rtl = (m_Paragraphs[line.Paragraph].Level & 1) != 0;
            float visualLeft = first < stop ? m_Segments[first].Left : line.X, visualRight = first < stop ? m_Segments[stop - 1].Right : line.X;

            // Past the end of the line, where trailing whitespace hangs.
            if (rtl ? x < visualLeft : x > visualRight)
            {
                bias = LetteraCaretBias.Backward;
                return HitHanging(line, x, rtl);
            }
            if (first == stop) return line.Start;
            if (x <= visualLeft) return StopAt(m_Segments[first], 0, out bias);
            if (x >= visualRight) return StopAt(m_Segments[stop - 1], 1, out bias);
            for (int s = first; s < stop; s++)
            {
                ref CaretSegment segment = ref m_Segments[s];
                if (x > segment.Right) continue;
                return StopInSegment(segment, x, out bias);
            }
            return StopAt(m_Segments[stop - 1], 1, out bias);
        }

        // A visual edge of a segment as a caret: 0 the left edge, 1 the right.
        static int StopAt(in CaretSegment segment, float visual, out LetteraCaretBias bias)
        {
            bool atStart = visual < 0.5f != segment.Rtl;
            bias = atStart ? LetteraCaretBias.Forward : LetteraCaretBias.Backward;
            return atStart ? segment.Start : segment.End;
        }

        int StopInSegment(in CaretSegment segment, float x, out LetteraCaretBias bias)
        {
            float width = segment.Right - segment.Left;
            float visual = width > 0 ? (x - segment.Left) / width : 0;
            int total = 0;
            for (int c = segment.Start; c < segment.End; c++)
                if (m_Boundaries[c])
                    total++;
            if (total <= 1) return StopAt(segment, visual < 0.5f ? 0 : 1, out bias);

            // A ligature: the nearest of its inner stops.
            float logical = segment.Rtl ? 1 - visual : visual;
            int wanted = Mathf.RoundToInt(logical * total);
            bias = wanted >= total ? LetteraCaretBias.Backward : LetteraCaretBias.Forward;
            if (wanted >= total) return segment.End;
            int seen = 0;
            for (int c = segment.Start; c < segment.End; c++)
            {
                if (!m_Boundaries[c]) continue;
                if (seen++ == wanted) return c;
            }
            return segment.End;
        }

        int HitHanging(in LetteraLine line, float x, bool rtl)
        {
            int end = LineEndStop(line);
            int best = Math.Min(line.VisibleEnd, end);
            float bestDistance = Mathf.Abs(HangingEdge(line, best) - x);
            for (int c = best + 1; c <= end; c++)
            {
                float distance = Mathf.Abs(HangingEdge(line, c) - x);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = c;
                }
            }
            return best;
        }

        // The last stop on a line: its end, or before the line break that ends it.
        int LineEndStop(in LetteraLine line)
        {
            int end = line.End;
            if (end > line.Start && end <= m_TextLength && IsLineBreakChar(m_Text[end - 1]))
            {
                end--;
                if (end > line.Start && m_Text[end] == '\n' && m_Text[end - 1] == '\r') end--;
            }
            return end;
        }

        int HitTestCurved(Vector2 point, out LetteraCaretBias bias)
        {
            bias = LetteraCaretBias.Forward;
            int best = -1;
            float bestDistance = float.MaxValue;
            for (int s = 0; s < m_SegmentCount; s++)
            {
                ref CaretSegment segment = ref m_Segments[s];
                var center = new Vector2((segment.Left + segment.Right) * 0.5f, segment.Baseline);
                float distance = (center - point).sqrMagnitude;
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = s;
                }
            }
            return best < 0 ? 0 : StopInSegment(m_Segments[best], point.x, out bias);
        }

        /// <summary>Whether a local point lies on a line's box, where an I-beam belongs.</summary>
        public bool IsOverLine(Vector2 point)
        {
            point = MapToContent(point);
            foreach (LetteraLine line in m_Lines)
            {
                if (point.y > line.Baseline + line.Ascent || point.y < line.Baseline - line.Descent) continue;
                if (point.x >= line.X && point.x <= line.X + line.Width) return true;
            }
            return false;
        }

        /// <summary>The rendered text, in codepoints. Current after EnsureParsed.</summary>
        public ReadOnlySpan<int> ShownCodepoints => new ReadOnlySpan<int>(m_Text, 0, m_TextLength);

        /// <summary>The direction of the paragraph holding an index. Current after EnsurePositioned.</summary>
        public bool ParagraphIsRtlAt(int index)
        {
            if (m_Paragraphs.Count == 0) return m_Attributes.DirectionOverride == 1 || (m_Attributes.DirectionOverride < 0 && m_Host.Data.BaseDirection == LetteraDirection.RightToLeft);
            for (int p = m_Paragraphs.Count - 1; p >= 0; p--)
                if (m_Paragraphs[p].Start <= index)
                    return (m_Paragraphs[p].Level & 1) != 0;
            return (m_Paragraphs[0].Level & 1) != 0;
        }
    }
}

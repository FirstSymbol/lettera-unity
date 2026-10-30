using System;
using System.Collections.Generic;
using FirstSymbol.Lettera.Unicode;
using UnityEngine;

namespace FirstSymbol.Lettera
{
    internal sealed partial class TextEngine
    {
        struct PlacedGlyph
        {
            public uint Glyph;
            public FontInstance Instance;
            public int Cluster, Line;
            public float X, Y, Advance, Unit, Size;
        }

        PlacedGlyph[] m_Placed = new PlacedGlyph[64];
        int m_PlacedCount;
        byte[] m_LineLevels = new byte[64];
        int[] m_VisualOrder = new int[64];

        public IReadOnlyList<LetteraLine> Lines => m_Lines;
        public int GlyphCount => m_PlacedCount;

        public LetteraGlyphInfo GetGlyph(int index)
        {
            PlacedGlyph g = m_Placed[index];
            return new LetteraGlyphInfo { Glyph = g.Glyph, Cluster = g.Cluster, Line = g.Line, X = g.X, Y = g.Y, Advance = g.Advance, Size = g.Size };
        }

        public Rect LayoutBounds { get; private set; }

        void PositionGlyphs()
        {
            LetteraTextData data = m_Host.Data;
            m_PlacedCount = 0;

            float top = m_Box.yMax;
            bool clips = m_Host.ClipsOverflow;
            if (m_Box.height > 0 || m_ContentHeight > 0)
            {
                bool overflowY = m_ContentHeight > m_Box.height + Epsilon;
                if (!(clips && overflowY))
                {
                    if (data.VerticalAlignment == LetteraVerticalAlignment.Middle) top = m_Box.yMax - (m_Box.height - m_ContentHeight) * 0.5f;
                    else if (data.VerticalAlignment == LetteraVerticalAlignment.Bottom) top = m_Box.yMin + m_ContentHeight;
                }
            }

            float y = top;
            float left = float.MaxValue, right = float.MinValue;
            for (int l = 0; l < m_Lines.Count; l++)
            {
                LetteraLine line = m_Lines[l];
                Paragraph paragraph = m_Paragraphs[line.Paragraph];
                float baseline = y - line.Ascent;
                float x = AlignLine(data, line, paragraph, clips, out float justifyExtra, out int justifyMode);
                line.X = x;
                line.Baseline = baseline;
                m_Lines[l] = line;
                if (line.Width > 0)
                {
                    if (x < left) left = x;
                    if (x + line.Width > right) right = x + line.Width;
                }
                PlaceLine(line, l, paragraph, x, baseline, justifyExtra, justifyMode);
                y -= line.Height;
            }
            if (left > right) left = right = m_Box.x;
            LayoutBounds = new Rect(left, top - m_ContentHeight, right - left, m_ContentHeight);
        }

        float AlignLine(LetteraTextData data, in LetteraLine line, in Paragraph paragraph, bool clips, out float justifyExtra, out int justifyMode)
        {
            justifyExtra = 0;
            justifyMode = 0;
            float free = m_Box.width - line.Width;
            bool rtl = (paragraph.Level & 1) != 0;

            float startEdge = rtl ? m_Box.xMax - line.Width : m_Box.xMin;
            if (clips && free < -Epsilon) return startEdge;

            switch (data.HorizontalAlignment)
            {
                case LetteraHorizontalAlignment.Center: return m_Box.xMin + free * 0.5f;
                case LetteraHorizontalAlignment.End: return rtl ? m_Box.xMin : m_Box.xMax - line.Width;
                case LetteraHorizontalAlignment.Left: return m_Box.xMin;
                case LetteraHorizontalAlignment.Right: return m_Box.xMax - line.Width;
                case LetteraHorizontalAlignment.Justify:
                    if (!line.EndedByMandatoryBreak && free > 0)
                    {
                        int separators = 0, clusters = 0;
                        for (int i = line.Start; i < line.VisibleEnd; i++)
                        {
                            if (IsRemoved(i)) continue;
                            if (LetteraUnicode.IsJustifiableSeparator(m_Text[i])) separators++;
                            if (m_Boundaries[i]) clusters++;
                        }
                        if (separators > 0) { justifyExtra = free / separators; justifyMode = 1; }
                        else if (clusters > 1) { justifyExtra = free / (clusters - 1); justifyMode = 2; }
                        if (justifyMode != 0) return m_Box.xMin;
                    }
                    return startEdge;
                default:
                    return startEdge;
            }
        }

        void PlaceLine(in LetteraLine line, int lineIndex, in Paragraph paragraph, float x, float baseline, float justifyExtra, int justifyMode)
        {
            int count = line.VisibleEnd - line.Start;
            if (count <= 0)
            {
                if (m_RemoveStart >= line.Start && m_RemoveStart <= line.End) PlaceMarker(ref x, baseline, lineIndex);
                return;
            }
            EnsureSize(ref m_LineLevels, count);
            EnsureSize(ref m_VisualOrder, count);
            var paragraphText = new ReadOnlySpan<int>(m_Text, paragraph.Start, paragraph.End - paragraph.Start);
            Bidi.ResolveLineLevels(paragraphText, new ReadOnlySpan<byte>(m_Levels, paragraph.Start, paragraph.End - paragraph.Start),
                line.Start - paragraph.Start, line.VisibleEnd - paragraph.Start, paragraph.Level, new Span<byte>(m_LineLevels, 0, count));
            Bidi.ReorderLine(new ReadOnlySpan<byte>(m_LineLevels, 0, count), new Span<int>(m_VisualOrder, 0, count));

            float size = CurrentFontSize;
            bool markerDone = false;
            int v = 0;
            while (v < count)
            {
                int logical = m_VisualOrder[v] + line.Start;
                if (IsRemoved(logical))
                {
                    if (!markerDone)
                    {
                        PlaceMarker(ref x, baseline, lineIndex);
                        markerDone = true;
                    }
                    v++;
                    continue;
                }

                // A fragment: consecutive logical codepoints of one run at one level.
                int runIndex = m_CpRun[logical];
                byte level = m_LineLevels[m_VisualOrder[v]];
                int low = logical, high = logical;
                int w = v + 1;
                while (w < count)
                {
                    int next = m_VisualOrder[w] + line.Start;
                    if (IsRemoved(next) || m_CpRun[next] != runIndex || m_LineLevels[m_VisualOrder[w]] != level) break;
                    if (next == high + 1) high = next;
                    else if (next == low - 1) low = next;
                    else break;
                    w++;
                }
                x = PlaceFragment(runIndex, low, high + 1, x, baseline, lineIndex, size, justifyExtra, justifyMode, line);
                v = w;
            }
            if (!markerDone && m_RemoveStart >= line.VisibleEnd && m_RemoveStart < line.End) PlaceMarker(ref x, baseline, lineIndex);
        }

        float PlaceFragment(int runIndex, int low, int high, float x, float baseline, int lineIndex, float baseSize, float justifyExtra, int justifyMode, in LetteraLine line)
        {
            Run run = m_Runs[runIndex];
            if (run.Instance == null) return x;
            float size = SizeOf(run, baseSize);
            float unit = run.UnitEm * size;
            int end = run.GlyphStart + run.GlyphCount;
            for (int i = FirstGlyph(run, low, high); i < end; i++)
            {
                ref ShapedGlyph g = ref m_Shaped[i];
                if (g.Cluster < low || g.Cluster >= high)
                {
                    if ((run.Level & 1) == 0 ? g.Cluster >= high : g.Cluster < low) break;
                    continue;
                }
                EnsureSize(ref m_Placed, m_PlacedCount + 1);
                float advance = g.XAdvance * unit;
                m_Placed[m_PlacedCount++] = new PlacedGlyph
                {
                    Glyph = g.Glyph, Instance = run.Instance, Cluster = g.Cluster, Line = lineIndex,
                    X = x + g.XOffset * unit, Y = baseline + g.YOffset * unit, Advance = advance, Unit = unit, Size = size,
                };
                x += advance;

                if (justifyMode != 0)
                {
                    bool lastOfCluster = i + 1 >= end || m_Shaped[i + 1].Cluster != g.Cluster;
                    if (lastOfCluster)
                    {
                        if (justifyMode == 1 && LetteraUnicode.IsJustifiableSeparator(m_Text[g.Cluster])) x += justifyExtra;
                        else if (justifyMode == 2 && g.Cluster + 1 < line.VisibleEnd) x += justifyExtra;
                    }
                }
            }
            return x;
        }

        // Clusters are monotonic within a run: bisect to the first glyph of the fragment.
        int FirstGlyph(in Run run, int low, int high)
        {
            bool rtl = (run.Level & 1) != 0;
            int a = run.GlyphStart, b = run.GlyphStart + run.GlyphCount;
            while (a < b)
            {
                int middle = (a + b) / 2;
                int cluster = m_Shaped[middle].Cluster;
                bool before = rtl ? cluster >= high : cluster < low;
                if (before) a = middle + 1;
                else b = middle;
            }
            return a;
        }

        void PlaceMarker(ref float x, float baseline, int lineIndex)
        {
            if (m_MarkerInstance == null) return;
            Run run = m_Runs[m_CpRun[m_MarkerAnchor]];
            float size = SizeOf(run, CurrentFontSize);
            float unit = (m_MarkerInstance == run.Instance ? run.UnitEm : 1f / m_MarkerInstance.Face.Metrics.Upem) * size;
            foreach (ShapedGlyph g in m_MarkerGlyphs)
            {
                EnsureSize(ref m_Placed, m_PlacedCount + 1);
                float advance = g.XAdvance * unit;
                m_Placed[m_PlacedCount++] = new PlacedGlyph
                {
                    Glyph = g.Glyph, Instance = m_MarkerInstance, Cluster = m_MarkerAnchor, Line = lineIndex,
                    X = x, Y = baseline, Advance = advance, Unit = unit, Size = size,
                };
                x += advance;
            }
        }

        // ------------------------------------------------------------- hit testing

        /// <summary>The glyph at a local point, within a distance.</summary>
        public bool HitTestGlyph(Vector2 point, float maxDistance, out LetteraHit hit)
        {
            hit = default;
            float best = maxDistance;
            bool found = false;
            for (int i = 0; i < m_PlacedCount; i++)
            {
                ref PlacedGlyph g = ref m_Placed[i];
                LetteraLine line = m_Lines[g.Line];
                var rect = new Rect(g.X, line.Baseline - line.Descent, g.Advance, line.Height);
                float dx = Mathf.Max(Mathf.Max(rect.xMin - point.x, 0), point.x - rect.xMax);
                float dy = Mathf.Max(Mathf.Max(rect.yMin - point.y, 0), point.y - rect.yMax);
                float distance = Mathf.Sqrt(dx * dx + dy * dy);
                if (distance > best || (found && distance == best)) continue;
                best = distance;
                found = true;
                ClusterRange(g.Cluster, out int start, out int end);
                hit = new LetteraHit { GlyphIndex = i, ClusterStart = start, ClusterEnd = end, LineIndex = g.Line, GlyphRect = rect };
            }
            return found;
        }

        void ClusterRange(int cp, out int start, out int end)
        {
            start = cp;
            while (start > 0 && !m_Boundaries[start]) start--;
            end = cp + 1;
            while (end < m_TextLength && !m_Boundaries[end]) end++;
        }

        /// <summary>The caret index at a local point: left half of a cluster gives its start, right half its end, mirrored in RTL.</summary>
        public int HitTestCaret(Vector2 point)
        {
            if (m_Lines.Count == 0 || m_PlacedCount == 0) return 0;
            int lineIndex = 0;
            for (int l = 0; l < m_Lines.Count; l++)
            {
                lineIndex = l;
                LetteraLine line = m_Lines[l];
                if (point.y >= line.Baseline - line.Descent) break;
            }
            int bestGlyph = -1;
            float bestDistance = float.MaxValue;
            for (int i = 0; i < m_PlacedCount; i++)
            {
                ref PlacedGlyph g = ref m_Placed[i];
                if (g.Line != lineIndex) continue;
                float distance = point.x < g.X ? g.X - point.x : point.x > g.X + g.Advance ? point.x - g.X - g.Advance : 0;
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    bestGlyph = i;
                }
            }
            if (bestGlyph < 0) return m_Lines[lineIndex].Start;
            ref PlacedGlyph hit = ref m_Placed[bestGlyph];
            ClusterRange(hit.Cluster, out int start, out int end);
            bool rtl = (m_Levels[hit.Cluster] & 1) != 0;
            bool leftHalf = point.x < hit.X + hit.Advance * 0.5f;
            return leftHalf != rtl ? start : end;
        }
    }
}

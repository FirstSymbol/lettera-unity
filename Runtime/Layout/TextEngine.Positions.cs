using System;
using System.Collections.Generic;
using FirstSymbol.Lettera.Unicode;
using UnityEngine;

namespace FirstSymbol.Lettera
{
    internal sealed partial class TextEngine
    {
        internal struct PlacedGlyph
        {
            public uint Glyph;
            public FontInstance Instance;
            public int Cluster, Line;
            public float X, Y, Advance, Unit, Size;
            public float XScale;    // Added to 1: justification, a fit and connecting strokes widen or narrow a glyph.
            public float Angle;     // Radians around the origin, on curved text.
            public float Pen;       // Where the cluster's advance starts, without the glyph's own offset.
            public ushort SpecialIndex;
            public byte Special;    // A formula's part: see AddSpecialQuad. 0 for everything else.
            public bool Marker;     // An elision marker: drawn in the line, but no part of the text.
        }

        PlacedGlyph[] m_Placed = Array.Empty<PlacedGlyph>();
        int m_PlacedCount;  // Glyphs of the text, then virtual ones, which only the mesh sees.
        int m_RealCount;
        byte[] m_LineLevels = Array.Empty<byte>();
        int[] m_VisualOrder = Array.Empty<int>();

        public IReadOnlyList<LetteraLine> Lines
        {
            get
            {
                Exact();
                return m_Lines;
            }
        }
        public int GlyphCount => m_RealCount;

        /// <summary>How far a glyph is turned, in radians, on curved text.</summary>
        internal float AngleOf(int index) => m_Placed[index].Angle;

        public LetteraGlyphInfo GetGlyph(int index)
        {
            PlacedGlyph g = m_Placed[index];
            return new LetteraGlyphInfo { Glyph = g.Glyph, Cluster = g.Cluster, Line = g.Line, X = g.X, Y = g.Y, Advance = g.Advance, Size = g.Size };
        }

        public Rect LayoutBounds { get; private set; }

        void PositionGlyphs()
        {
            PlaceGlyphs();
            m_WindowPlaced = true;
            m_ActiveRectsValid = false;
            m_SemanticsStale = true;
            RunPlacedHooks();
        }

        void PlaceGlyphs()
        {
            LetteraTextData data = m_Host.Data;
            m_PlacedCount = 0;
            m_SegmentsValid = false;
            m_VirtualGlyphs.Clear();
            // A box that only grew taller asks no new lines, only new places.
            if (m_Banded && !IsWindowed) m_Box = ComputeBox(data.Padding, m_Host.LayoutRect);

            float top = m_Box.yMax;
            bool clips = m_Host.ClipsOverflow || m_MarqueeScrolling || ClipsViewport;
            // A viewport's content starts at its top, however tall it is.
            if (!ClipsViewport && (m_Box.height > 0 || m_ContentHeight > 0))
            {
                bool overflowY = m_ContentHeight > m_Box.height + Epsilon;
                if (!(clips && overflowY))
                {
                    if (data.VerticalAlignment == LetteraVerticalAlignment.Middle) top = m_Box.yMax - (m_Box.height - m_ContentHeight) * 0.5f;
                    else if (data.VerticalAlignment == LetteraVerticalAlignment.Bottom) top = m_Box.yMin + m_ContentHeight;
                }
            }

            float y = top + m_TrimTop;
            float left = float.MaxValue, right = float.MinValue;
            StartBand();
            if (m_PendingCount > 0)
            {
                if (m_Banded) ShapeBand(y);
                else ProcessParagraphs(0, m_Paragraphs.Count);
            }
            // Nearly always one placed glyph per shaped one, so a long text does not grow through every size on the way.
            EnsureSize(ref m_Placed, m_ShapedCount);
            for (int l = 0; l < m_Lines.Count; l++)
            {
                LetteraLine line = m_Lines[l];
                Paragraph paragraph = m_Paragraphs[line.Paragraph];
                y -= line.GapBefore;
                float baseline = y - line.Ascent;
                float x = AlignLine(data, line, paragraph, clips, out LineStretch stretch);
                line.X = x;
                line.Baseline = baseline;
                m_Lines[l] = line;
                if (line.Width > 0)
                {
                    if (x < left) left = x;
                    if (x + line.Width > right) right = x + line.Width;
                }
                if (!paragraph.Pending && LineInBand(y, y - line.Height, l)) PlaceLine(line, l, paragraph, x, baseline, stretch);
                y -= line.Height;
            }
            m_RealCount = m_PlacedCount;
            if (m_ListItems.Count > 0) PlaceListMarkers();
            if (m_RubyItems.Count > 0) PlaceRubies();
            if (m_FormulaCount > 0) PlaceFormulas();
            foreach (PlacedGlyph glyph in m_VirtualGlyphs)
            {
                EnsureSize(ref m_Placed, m_PlacedCount + 1);
                m_Placed[m_PlacedCount++] = glyph;
            }
            if (m_Attributes.HasCurve) CurveLines();
            FinishBand(top + m_TrimTop, y);
            m_PlacedOnEstimates = m_PendingCount > 0 && m_BandFirst < m_BandEnd && PendingBefore(m_Lines[m_BandFirst].Paragraph);
            if (left > right) left = right = m_Box.x;
            LayoutBounds = new Rect(left, top - m_ContentHeight, right - left, m_ContentHeight);
        }

        /// <summary>How a line's free space is spent: on word spaces, between clusters and on glyph widths.</summary>
        struct LineStretch
        {
            public float WordExtra, ClusterExtra, GlyphScale;
            public static readonly LineStretch None = new LineStretch { GlyphScale = 1 };
        }

        AlignSpec AlignOf(in Paragraph paragraph)
        {
            if (!m_Attributes.HasLayout || paragraph.Start >= m_TextLength) return AlignSpec.Default;
            short index = m_Attributes.Layout[paragraph.Start].Align;
            return index >= 0 ? m_Attributes.Aligns[index] : AlignSpec.Default;
        }

        float AlignLine(LetteraTextData data, in LetteraLine line, in Paragraph paragraph, bool clips, out LineStretch stretch)
        {
            stretch = LineStretch.None;
            bool rtl = (paragraph.Level & 1) != 0;
            float boxLeft = rtl ? m_Box.xMin : m_Box.xMin + line.Margin;
            float boxRight = rtl ? m_Box.xMax - line.Margin : m_Box.xMax;
            float free = boxRight - boxLeft - line.Width;

            float startEdge = rtl ? boxRight - line.Width : boxLeft;
            if (clips && free < -Epsilon && AlignOf(paragraph).Alignment != (int)LetteraHorizontalAlignment.Justify && data.HorizontalAlignment != LetteraHorizontalAlignment.Justify) return startEdge;

            AlignSpec spec = AlignOf(paragraph);
            var alignment = spec.Alignment >= 0 ? (LetteraHorizontalAlignment)spec.Alignment : data.HorizontalAlignment;
            bool justified = alignment == LetteraHorizontalAlignment.Justify;
            if (justified && line.EndedByMandatoryBreak)
            {
                switch (spec.LastLine)
                {
                    case LetteraLastLine.End: alignment = LetteraHorizontalAlignment.End; justified = false; break;
                    case LetteraLastLine.Center: alignment = LetteraHorizontalAlignment.Center; justified = false; break;
                    case LetteraLastLine.Justify: break;
                    default: return startEdge;
                }
            }

            switch (alignment)
            {
                case LetteraHorizontalAlignment.Center: return boxLeft + free * 0.5f;
                case LetteraHorizontalAlignment.End: return rtl ? boxLeft : boxRight - line.Width;
                case LetteraHorizontalAlignment.Left: return boxLeft;
                case LetteraHorizontalAlignment.Right: return boxRight - line.Width;
                case LetteraHorizontalAlignment.Justify:
                    if (spec.Justify != LetteraJustifyMode.None && ComputeStretch(line, spec, free, out stretch)) return boxLeft;
                    return startEdge;
                default:
                    return startEdge;
            }
        }

        // A short line spends word spaces, then letter spacing, then glyph width, and whatever is left goes back to word spaces.
        // An overflowing line compresses the same way, down to the minimums.
        bool ComputeStretch(in LetteraLine line, in AlignSpec spec, float free, out LineStretch stretch)
        {
            stretch = LineStretch.None;
            if (Math.Abs(free) <= Epsilon) return false;
            int separators = 0, clusters = 0;
            float spaces = 0, size = 0;
            for (int i = line.Start; i < line.VisibleEnd; i++)
            {
                if (IsRemoved(i)) continue;
                float own = SizeOf(m_Runs[m_CpRun[i]], CurrentFontSize);
                if (own > size) size = own;
                if (LetteraUnicode.IsJustifiableSeparator(m_Text[i]))
                {
                    separators++;
                    spaces += m_CpAdvanceEm[i] * own;
                }
                if (m_Boundaries[i]) clusters++;
            }
            bool words = separators > 0 && spec.Justify != LetteraJustifyMode.InterCharacter;
            if (!words && (clusters < 2 || spec.Justify == LetteraJustifyMode.InterWord)) return false;

            if (free < 0)
            {
                if (!words) return false;
                float wordRoom = spaces * (100 - spec.MinWordSpace) * 0.01f;
                float shrinkWords = Math.Min(-free, wordRoom);
                stretch.WordExtra = -shrinkWords / separators;
                float left = -free - shrinkWords;
                if (left > 0 && spec.MinTracking < 0 && clusters > 1)
                {
                    float room = -spec.MinTracking * 0.01f * size * (clusters - 1);
                    float used = Math.Min(left, room);
                    stretch.ClusterExtra = -used / (clusters - 1);
                    left -= used;
                }
                if (left > 0 && spec.MinGlyphWidth < 100 && line.Width > 0)
                    stretch.GlyphScale = Math.Max(1 - left / line.Width, spec.MinGlyphWidth * 0.01f);
                return true;
            }

            if (!words)
            {
                stretch.ClusterExtra = free / (clusters - 1);
                return true;
            }

            float remaining = free;
            float wordBudget = float.IsPositiveInfinity(spec.MaxWordSpace) ? remaining : spaces * Math.Max(spec.MaxWordSpace - 100, 0) * 0.01f;
            float forWords = Math.Min(remaining, wordBudget);
            remaining -= forWords;
            if (remaining > 0 && spec.MaxTracking > 0 && clusters > 1)
            {
                float room = spec.MaxTracking * 0.01f * size * (clusters - 1);
                float used = Math.Min(remaining, room);
                stretch.ClusterExtra = used / (clusters - 1);
                remaining -= used;
            }
            if (remaining > 0 && spec.MaxGlyphWidth > 100 && line.Width > 0)
            {
                float room = line.Width * (spec.MaxGlyphWidth - 100) * 0.01f;
                float used = Math.Min(remaining, room);
                stretch.GlyphScale = 1 + used / line.Width;
                remaining -= used;
            }
            stretch.WordExtra = (forWords + remaining) / separators;
            return true;
        }

        void PlaceLine(in LetteraLine line, int lineIndex, in Paragraph paragraph, float x, float baseline, in LineStretch stretch)
        {
            int count = line.VisibleEnd - line.Start;
            if (count <= 0)
            {
                PlaceMarkersIn(line.Start, line.End + 1, ref x, baseline, lineIndex);
                return;
            }
            EnsureSize(ref m_LineLevels, count);
            EnsureSize(ref m_VisualOrder, count);
            var paragraphText = new ReadOnlySpan<int>(m_Text, paragraph.Start, paragraph.End - paragraph.Start);
            Bidi.ResolveLineLevels(paragraphText, new ReadOnlySpan<byte>(m_Levels, paragraph.Start, paragraph.End - paragraph.Start),
                line.Start - paragraph.Start, line.VisibleEnd - paragraph.Start, paragraph.Level, new Span<byte>(m_LineLevels, 0, count));
            Bidi.ReorderLine(new ReadOnlySpan<byte>(m_LineLevels, 0, count), new Span<int>(m_VisualOrder, 0, count));

            float size = CurrentFontSize;
            int v = 0;
            int markersPlaced = 0;
            while (v < count)
            {
                int logical = m_VisualOrder[v] + line.Start;
                int removal = RemovalOf(logical);
                if (removal >= 0)
                {
                    if ((markersPlaced & (1 << Math.Min(removal, 30))) == 0)
                    {
                        PlaceMarker(m_Removals[removal], ref x, baseline, lineIndex);
                        markersPlaced |= 1 << Math.Min(removal, 30);
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
                    if (m_Attributes.HasLayout && IndentRiseAt(next, line.Start, size) > 0) break;
                    if (next == high + 1) high = next;
                    else if (next == low - 1) low = next;
                    else break;
                    w++;
                }
                if (m_Attributes.HasLayout) x += IndentRiseAt(low, line.Start, size);
                x = PlaceFragment(runIndex, low, high + 1, x, baseline, lineIndex, size, stretch, line);
                v = w;
            }
            for (int r = 0; r < m_Removals.Count; r++)
            {
                if ((markersPlaced & (1 << Math.Min(r, 30))) != 0) continue;
                int start = m_Removals[r].Start;
                if (start >= line.VisibleEnd && start < line.End) PlaceMarker(m_Removals[r], ref x, baseline, lineIndex);
            }
        }

        int RemovalOf(int cp)
        {
            for (int i = 0; i < m_Removals.Count; i++)
                if (cp >= m_Removals[i].Start && cp < m_Removals[i].End)
                    return i;
            return -1;
        }

        void PlaceMarkersIn(int start, int end, ref float x, float baseline, int lineIndex)
        {
            foreach (Removal removal in m_Removals)
                if (removal.Start >= start && removal.Start < end)
                    PlaceMarker(removal, ref x, baseline, lineIndex);
        }

        // Called per fragment over indices the same pass sized; the tests run it with every check on.
        [Unity.IL2CPP.CompilerServices.Il2CppSetOption(Unity.IL2CPP.CompilerServices.Option.ArrayBoundsChecks, false), Unity.IL2CPP.CompilerServices.Il2CppSetOption(Unity.IL2CPP.CompilerServices.Option.NullChecks, false)]
        float PlaceFragment(int runIndex, int low, int high, float x, float baseline, int lineIndex, float baseSize, in LineStretch stretch, in LetteraLine line)
        {
            Run run = m_Runs[runIndex];
            if (run.Instance == null) return x;
            float size = SizeOf(run, baseSize);
            float unit = run.UnitEm * size;
            float glyphScale = m_FitGlyphScale * stretch.GlyphScale;
            bool rtl = (run.Level & 1) != 0;
            bool extras = m_Attributes.HasLayout || m_FitTracking != 0;
            int lastCluster = line.VisibleEnd - 1;
            while (lastCluster > line.Start && !m_Boundaries[lastCluster]) lastCluster--;
            int end = run.GlyphStart + run.GlyphCount;
            for (int i = FirstGlyph(run, low, high); i < end; i++)
            {
                ref ShapedGlyph g = ref m_Shaped[i];
                if (g.Cluster < low || g.Cluster >= high)
                {
                    if ((run.Level & 1) == 0 ? g.Cluster >= high : g.Cluster < low) break;
                    continue;
                }

                bool firstOfCluster = i == run.GlyphStart || m_Shaped[i - 1].Cluster != g.Cluster;
                bool lastOfCluster = i + 1 >= end || m_Shaped[i + 1].Cluster != g.Cluster;
                float before = 0, after = 0;
                if (extras && (firstOfCluster || lastOfCluster))
                {
                    // Space belongs to the logical end of a cluster: visually after it, or before it in a right-to-left run.
                    ExtrasOf(g.Cluster, i, run, end, size, g.Cluster == lastCluster || ClusterHoldsLineEnd(g.Cluster, lastCluster), out float lead, out float trail);
                    if (firstOfCluster) before = rtl ? lead + trail : lead;
                    if (lastOfCluster) after = rtl ? lead : lead + trail;
                    if (firstOfCluster && rtl && trail > 0 && m_CpKashida[g.Cluster]) AddKashida(run, x, trail, baseline, lineIndex, g.Cluster, unit);
                }
                x += before;

                EnsureSize(ref m_Placed, m_PlacedCount + 1);
                // A pixel font is not scaled to fit a line: its pixels stay square.
                FontFace face = run.Instance.Face;
                bool pixel = face.PixelGrid > 0;
                float scaleHere = pixel ? 1 : glyphScale;
                float advance = g.XAdvance * unit * scaleHere;
                float placedX = x + g.XOffset * unit, placedY = baseline + g.YOffset * unit + run.BaselineShift * baseSize;
                if (pixel && face.PixelSnap)
                {
                    placedX = SnapToGrid(placedX, size, face.PixelGrid);
                    placedY = SnapToGrid(placedY, size, face.PixelGrid);
                }
                m_Placed[m_PlacedCount++] = new PlacedGlyph
                {
                    Glyph = g.Glyph, Instance = run.Instance, Cluster = g.Cluster, Line = lineIndex,
                    X = placedX, Y = placedY, Advance = advance + after, Unit = unit, Size = size,
                    XScale = scaleHere - 1, Pen = x,
                };
                x += advance;
                if (extras && lastOfCluster && !rtl && after > 0 && m_CpKashida[g.Cluster]) AddKashida(run, x + (after - TrailOnly(g.Cluster, size)), TrailOnly(g.Cluster, size), baseline, lineIndex, g.Cluster, unit);
                x += after;

                if (lastOfCluster)
                {
                    if (stretch.WordExtra != 0 && LetteraUnicode.IsJustifiableSeparator(m_Text[g.Cluster])) x += stretch.WordExtra;
                    if (stretch.ClusterExtra != 0 && g.Cluster + 1 < line.VisibleEnd) x += stretch.ClusterExtra;
                }
            }
            return x;
        }

        float TrailOnly(int cluster, float size) => m_Attributes.HasLayout ? m_Attributes.Layout[cluster].Tracking.Of(size) : 0;

        bool ClusterHoldsLineEnd(int cluster, int lastCluster) => cluster <= lastCluster && (lastCluster == cluster || !HasBoundaryBetween(cluster, lastCluster));

        bool HasBoundaryBetween(int a, int b)
        {
            for (int i = a + 1; i <= b; i++)
                if (m_Boundaries[i])
                    return true;
            return false;
        }

        // The extra space of the codepoints a shaped cluster covers, which can be several in a ligature.
        void ExtrasOf(int cluster, int glyphIndex, in Run run, int end, float size, bool endsLine, out float lead, out float trail)
        {
            bool rtl = (run.Level & 1) != 0;
            int stop = run.End;
            if (!rtl)
            {
                for (int i = glyphIndex + 1; i < end; i++)
                {
                    if (m_Shaped[i].Cluster == cluster) continue;
                    stop = m_Shaped[i].Cluster;
                    break;
                }
            }
            else
            {
                for (int i = glyphIndex - 1; i >= run.GlyphStart; i--)
                {
                    if (m_Shaped[i].Cluster == cluster) continue;
                    stop = m_Shaped[i].Cluster;
                    break;
                }
            }
            float total = 0;
            lead = 0;
            for (int c = cluster; c < stop; c++)
            {
                total += m_CpExtraEm[c] * size + m_CpExtraUnits[c];
                if (m_FitTracking != 0 && m_Boundaries[c]) total += m_FitTracking * size;
                lead += m_CpLeadEm[c] * size + m_CpLeadUnits[c];
            }
            trail = total - lead * 2;
            if (endsLine)
            {
                float tracking = m_FitTracking * size + (m_Attributes.HasLayout ? m_Attributes.Layout[cluster].Tracking.Of(size) : 0);
                if (tracking != 0 && (tracking > 0) == (trail > 0)) trail -= tracking;
            }
        }

        // Virtual glyphs: they draw like the letter they follow but are no part of the text.
        List<PlacedGlyph> m_VirtualGlyphs = Rare<PlacedGlyph>.List;
        const int Tatweel = 0x0640;

        // A connecting stroke across the space tracking opened between joined letters.
        void AddKashida(in Run run, float x, float width, float baseline, int lineIndex, int cluster, float unit)
        {
            if (width <= 0) return;
            FontInstance instance = run.Instance;
            if (!instance.Face.HasGlyph(Tatweel))
            {
                Span<int> one = stackalloc int[1];
                one[0] = Tatweel;
                FontFace face = m_Chain.Resolve(one, false, null, null, out _);
                if (face == null || !face.HasGlyph(Tatweel)) return;
                instance = face.Regular;
                unit = run.Instance.Face == face ? unit : unit * run.Instance.Face.Metrics.Upem / face.Metrics.Upem;
            }
            uint glyph = LetteraNative.lt_font_glyph(instance.Font, Tatweel, 0);
            float advance = LetteraNative.lt_glyph_advance(instance.Font, glyph) * unit;
            if (advance <= 0) return;
            Own(ref m_VirtualGlyphs).Add(new PlacedGlyph
            {
                Glyph = glyph, Instance = instance, Cluster = cluster, Line = lineIndex, X = x, Y = baseline, Advance = 0, Unit = unit,
                Size = run.UnitEm > 0 ? unit / run.UnitEm : 0, XScale = width / advance - 1,
            });
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

        void PlaceMarker(in Removal removal, ref float x, float baseline, int lineIndex)
        {
            if (removal.Instance == null) return;
            Run run = m_Runs[m_CpRun[removal.Anchor]];
            float size = SizeOf(run, CurrentFontSize);
            float unit = (removal.Instance == run.Instance ? run.UnitEm : 1f / removal.Instance.Face.Metrics.Upem) * size;
            float advance = removal.Advance * unit;
            for (int i = 0; i < removal.Repeat; i++)
            {
                EnsureSize(ref m_Placed, m_PlacedCount + 1);
                m_Placed[m_PlacedCount++] = new PlacedGlyph
                {
                    Glyph = removal.Glyph, Instance = removal.Instance, Cluster = removal.Anchor, Line = lineIndex,
                    X = x, Y = baseline, Advance = advance, Unit = unit, Size = size, Pen = x, Marker = true,
                };
                x += advance;
            }
        }

        // Each line goes onto its own arc of one set of concentric circles, by arc length, with glyphs turned to the tangent.
        void CurveLines()
        {
            float radius = m_Attributes.CurveRadius.Of(CurrentFontSize);
            if (Mathf.Abs(radius) < 0.001f || float.IsInfinity(radius) || m_Lines.Count == 0) return;
            LetteraLine first = m_Lines[0];
            float anchorX = first.X + first.Width * m_Attributes.CurveAnchor;
            float centerY = first.Baseline - radius;
            for (int i = 0; i < m_PlacedCount; i++)
            {
                ref PlacedGlyph g = ref m_Placed[i];
                float middle = g.X + g.Advance * 0.5f;
                // A signed radius covers both bends: a negative one puts the center above and turns glyphs the other way.
                float lineRadius = radius + (g.Y - first.Baseline);
                float angle = (middle - anchorX) / radius;
                float sin = Mathf.Sin(angle), cos = Mathf.Cos(angle);
                float px = anchorX + lineRadius * sin;
                float py = centerY + lineRadius * cos;
                // The glyph's origin lies half an advance back along the tangent.
                g.Angle = -angle;
                g.X = px - g.Advance * 0.5f * cos;
                g.Y = py + g.Advance * 0.5f * sin;
            }
        }

        // ------------------------------------------------------------- hit testing

        /// <summary>The glyph at a local point, within a distance.</summary>
        public bool HitTestGlyph(Vector2 point, float maxDistance, out LetteraHit hit)
        {
            Exact();
            point = MapToContent(point);
            hit = default;
            float best = maxDistance;
            bool found = false;
            for (int i = 0; i < m_RealCount; i++)
            {
                ref PlacedGlyph g = ref m_Placed[i];
                LetteraLine line = m_Lines[g.Line];
                var rect = new Rect(g.X, line.Baseline - line.Descent, g.Advance, line.Height);
                Vector2 test = point;
                if (g.Angle != 0)
                {
                    // Into the glyph's own frame, where its box is straight.
                    float cos = Mathf.Cos(-g.Angle), sin = Mathf.Sin(-g.Angle);
                    Vector2 local = point - new Vector2(g.X, g.Y);
                    test = new Vector2(g.X + local.x * cos - local.y * sin, line.Baseline + local.x * sin + local.y * cos);
                }
                float dx = Mathf.Max(Mathf.Max(rect.xMin - test.x, 0), test.x - rect.xMax);
                float dy = Mathf.Max(Mathf.Max(rect.yMin - test.y, 0), test.y - rect.yMax);
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
    }
}

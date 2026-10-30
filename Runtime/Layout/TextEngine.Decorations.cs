using System.Collections.Generic;
using UnityEngine;

namespace FirstSymbol.Lettera
{
    // Decoration lines and highlights: shapes that follow the placed glyphs instead of being glyphs.
    internal sealed partial class TextEngine
    {
        const float ModeLine = 6, ModeRoundedBox = 7;

        // ------------------------------------------------------------- lines

        // Lines follow the glyphs in visual order: one line per fragment with the same setting.
        // A group draws the line's copy of that layer; no group draws the line itself.
        void AddDecorationLayer(LayerGroup group, bool overlay = false)
        {
            if (m_Attributes.Decorations.Count == 0) return;
            if (group != null && group.Layer != LayerKind.Shadow && group.Layer != LayerKind.Stroke && group.Layer != LayerKind.Glow) return;
            AddLinesOf(group, false, overlay);
            AddLinesOf(group, true, overlay);
        }

        void AddLinesOf(LayerGroup group, bool strike, bool overlay)
        {
            LetteraAttributes.Cp[] attrs = m_Attributes.Chars;
            int i = 0;
            while (i < m_RealCount)
            {
                ref PlacedGlyph first = ref m_Placed[i];
                ref LetteraAttributes.Cp attr = ref attrs[first.Cluster];
                int index = strike ? attr.Strike : attr.Underline;
                int layer = group != null ? group.Cover[first.Cluster] : 0;
                if (index == 0 || m_Attributes.Decorations[index - 1].Overlay != overlay)
                {
                    i++;
                    continue;
                }
                float left = first.X, right = first.X + first.Advance;
                int j = i + 1;
                while (j < m_RealCount)
                {
                    ref PlacedGlyph next = ref m_Placed[j];
                    ref LetteraAttributes.Cp nextAttr = ref attrs[next.Cluster];
                    if (next.Line != first.Line || (strike ? nextAttr.Strike : nextAttr.Underline) != index || next.Size != first.Size ||
                        nextAttr.Color.r != attr.Color.r || nextAttr.Color.g != attr.Color.g || nextAttr.Color.b != attr.Color.b || nextAttr.HasColor != attr.HasColor ||
                        (group != null && group.Cover[next.Cluster] != layer)) break;
                    left = Mathf.Min(left, next.X);
                    right = Mathf.Max(right, next.X + next.Advance);
                    j++;
                }
                if (group == null || layer != 0) AddDecorationLine(i, j, attr, index, strike, left, right, group, layer - 1);
                i = j;
            }
        }

        void AddDecorationLine(int firstGlyph, int endGlyph, in LetteraAttributes.Cp attr, int index, bool strike, float left, float right, LayerGroup group, int recordIndex)
        {
            ref PlacedGlyph glyph = ref m_Placed[firstGlyph];
            LetteraAttributes.DecorationSpec spec = m_Attributes.Decorations[index - 1];
            FaceMetrics metrics = glyph.Instance.Face.Metrics;
            float offset = spec.AutoOffset ? (strike ? metrics.StrikeOffset : metrics.UnderlineOffset) * glyph.Unit : spec.Offset.Of(glyph.Size);
            float thickness = spec.AutoThickness ? (strike ? metrics.StrikeSize : metrics.UnderlineSize) * glyph.Unit : spec.Thickness.Of(glyph.Size);
            if (thickness <= 0) return;
            float y = (glyph.Line < m_Lines.Count ? m_Lines[glyph.Line].Baseline : glyph.Y) + offset;

            Color32 baseColor = m_Host.BaseColor;
            Color32 glyphColor = attr.HasColor ? attr.Color : new Color32(baseColor.r, baseColor.g, baseColor.b, 255);
            Color32 color;
            ResolvedPaint paint;
            Rect rect;
            float dx = 0, dy = 0, grow = 0, soft = 0;
            if (group != null)
            {
                LayerRecord record = m_Attributes.Layers[recordIndex];
                float size = MeasureSize(record, glyph);
                paint = record.Paint;
                color = paint.UsesGlyphColor ? glyphColor : paint.Color;
                rect = PaintRect(record, glyph);
                switch (group.Layer)
                {
                    case LayerKind.Shadow:
                        dx = record.OffsetX.Of(size);
                        dy = record.OffsetY.Of(size);
                        grow = record.Dilate.Of(size);
                        break;
                    case LayerKind.Stroke:
                        float width = record.Width.Of(size);
                        if (width <= 0) return;
                        grow = width * (1 + record.Align) * 0.5f;
                        break;
                    case LayerKind.Glow:
                        soft = record.Width.Of(size);
                        if (soft <= 0) return;
                        grow = record.Dilate.Of(size);
                        break;
                }
            }
            else
            {
                paint = spec.Paint;
                color = paint.UsesGlyphColor ? glyphColor : paint.Color;
                rect = LinePaintRect(spec, glyph, left, right);
            }
            color.a = (byte)(color.a * attr.Alpha);
            UseSegment(paint);

            if (!spec.SkipInk)
            {
                EmitLine(left, right, y, thickness, spec.Style, grow, soft, dx, dy, left, color, paint, rect);
                return;
            }

            // The line stops where a glyph's ink crosses its band.
            float band = thickness * 0.5f + grow;
            float x = left;
            float gap = thickness;
            unsafe
            {
                int* extents = stackalloc int[4];
                for (int g = firstGlyph; g < endGlyph; g++)
                {
                    ref PlacedGlyph placed = ref m_Placed[g];
                    if (placed.Instance.Face.IsVirtual || LetteraNative.lt_glyph_extents(placed.Instance.Font, placed.Glyph, extents) == 0) continue;
                    float inkTop = placed.Y + extents[1] * placed.Unit, inkBottom = inkTop + extents[3] * placed.Unit;
                    if (inkBottom > y + band || inkTop < y - band) continue;
                    float inkLeft = placed.X + extents[0] * placed.Unit - gap, inkRight = placed.X + (extents[0] + extents[2]) * placed.Unit + gap;
                    if (inkLeft > x) EmitLine(x, inkLeft, y, thickness, spec.Style, grow, soft, dx, dy, left, color, paint, rect);
                    if (inkRight > x) x = inkRight;
                }
            }
            if (right > x) EmitLine(x, right, y, thickness, spec.Style, grow, soft, dx, dy, left, color, paint, rect);
        }

        Rect LinePaintRect(in LetteraAttributes.DecorationSpec spec, in PlacedGlyph glyph, float left, float right)
        {
            if (spec.Paint.IsSolid) return default;
            LetteraLine line = m_Lines[glyph.Line];
            switch (spec.Paint.Span)
            {
                case LetteraPaintSpan.Glyph: return new Rect(left, line.Baseline - line.Descent, right - left, line.Height);
                case LetteraPaintSpan.Line: return new Rect(line.X, line.Baseline - line.Descent, line.Width, line.Height);
                case LetteraPaintSpan.Range: return spec.RangeRect.x == float.MaxValue ? LayoutBounds : spec.RangeRect;
                default: return LayoutBounds;
            }
        }

        // A plain straight line stays a solid quad. Everything else computes its distance per pixel, so it stays smooth at any zoom.
        void EmitLine(float left, float right, float y, float thickness, LetteraLineStyle style, float grow, float soft, float dx, float dy, float origin, Color32 color, in ResolvedPaint paint, in Rect rect)
        {
            if (right <= left) return;
            if (style == LetteraLineStyle.Solid && soft <= 0 && !Styled)
            {
                float half = thickness * 0.5f + grow;
                AddSolid(left - grow + dx, y - half + dy, right + grow + dx, y + half + dy, color, paint, rect, dx, dy);
                return;
            }
            if (style == LetteraLineStyle.Solid && soft <= 0)
            {
                float half = thickness * 0.5f + grow;
                AddSolid(left - grow + dx, y - half + dy, right + grow + dx, y + half + dy, color, paint, rect, dx, dy);
                return;
            }

            float wave = style == LetteraLineStyle.Wavy ? thickness * 1.5f : style == LetteraLineStyle.Double ? thickness : 0;
            float reach = thickness * 0.5f + wave + grow + soft + 1;
            int v = Reserve();
            float x0 = left - grow - soft - 1 + dx, x1 = right + grow + soft + 1 + dx;
            float y0 = y - reach + dy, y1 = y + reach + dy;
            Vertices[v] = new Vector3(x0, y0);
            Vertices[v + 1] = new Vector3(x0, y1);
            Vertices[v + 2] = new Vector3(x1, y1);
            Vertices[v + 3] = new Vector3(x1, y0);
            // Position along the line from the fragment's start, and across it from its middle.
            float page = (float)style;
            float a0 = x0 - dx - origin, a1 = x1 - dx - origin;
            Uv0[v] = new Vector4(a0, -reach, page, ModeLine);
            Uv0[v + 1] = new Vector4(a0, reach, page, ModeLine);
            Uv0[v + 2] = new Vector4(a1, reach, page, ModeLine);
            Uv0[v + 3] = new Vector4(a1, -reach, page, ModeLine);
            var parameters = new Vector4(thickness, grow, right - origin, 0);
            Uv1[v] = Uv1[v + 1] = Uv1[v + 2] = Uv1[v + 3] = parameters;
            SetColor(v, color);
            SetPaint(v, paint, rect, dx, dy, new Vector4(soft, 0, 0, 0));
        }

        // ------------------------------------------------------------- highlights

        readonly List<Rect> m_RowRects = new List<Rect>();
        readonly List<int> m_RowLines = new List<int>();

        void DrawHighlights(List<LayerGroup> groups, bool above)
        {
            foreach (LayerGroup group in groups)
            {
                if (group.Layer != LayerKind.Highlight) continue;
                if (m_Attributes.Highlights[m_Attributes.Layers[FirstRecordOf(group)].Highlight].Order == LetteraHighlightOrder.Above != above) continue;
                DrawHighlightGroup(group);
            }
        }

        int FirstRecordOf(LayerGroup group)
        {
            for (int i = 0; i < m_Attributes.Length; i++)
                if (group.Cover[i] != 0)
                    return group.Cover[i] - 1;
            return 0;
        }

        void DrawHighlightGroup(LayerGroup group)
        {
            int i = 0;
            while (i < m_RealCount)
            {
                int cover = group.Cover[m_Placed[i].Cluster];
                if (cover == 0)
                {
                    i++;
                    continue;
                }
                LayerRecord record = m_Attributes.Layers[cover - 1];
                HighlightSpec spec = m_Attributes.Highlights[record.Highlight];
                if (spec.Look.Geometry == LetteraHighlightGeometry.Block)
                {
                    i = DrawBlock(group, cover, record, spec, i);
                    continue;
                }
                int j = i + 1;
                while (j < m_RealCount && group.Cover[m_Placed[j].Cluster] == cover && m_Placed[j].Line == m_Placed[i].Line)
                {
                    if (spec.Look.Geometry == LetteraHighlightGeometry.Glyph && m_Placed[j].Cluster != m_Placed[i].Cluster) break;
                    j++;
                }
                Rect figure = FragmentRect(i, j, spec);
                if (figure.width > 0)
                {
                    bool rtl = (m_Levels[m_Placed[i].Cluster] & 1) != 0;
                    int corners = (int)spec.Look.Corners;
                    if (spec.Look.Geometry == LetteraHighlightGeometry.Range)
                    {
                        // Only where the range logically begins and ends: a wrap stays square.
                        int low = int.MaxValue, high = -1;
                        for (int g = i; g < j; g++)
                        {
                            low = Mathf.Min(low, m_Placed[g].Cluster);
                            high = Mathf.Max(high, m_Placed[g].Cluster);
                        }
                        if (low > record.Start) corners &= ~(int)(LetteraCorners.TopStart | LetteraCorners.BottomStart);
                        if (!ClusterReachesEnd(high, record.End)) corners &= ~(int)(LetteraCorners.TopEnd | LetteraCorners.BottomEnd);
                    }
                    Vector4 radii = RadiiFor(corners, rtl, spec.Radius.Of(m_Placed[i].Size));
                    EmitBox(figure, figure, radii, m_Placed[i], record, spec);
                }
                i = j;
            }
        }

        bool ClusterReachesEnd(int cluster, int end)
        {
            for (int c = cluster + 1; c < end; c++)
                if (m_Boundaries[c])
                    return false;
            return true;
        }

        // All lines of one range merge into one contour: each row touches its neighbours where they overlap, and only exposed corners round.
        int DrawBlock(LayerGroup group, int cover, in LayerRecord record, in HighlightSpec spec, int start)
        {
            m_RowRects.Clear();
            m_RowLines.Clear();
            int i = start;
            while (i < m_RealCount && group.Cover[m_Placed[i].Cluster] == cover)
            {
                int line = m_Placed[i].Line;
                int j = i + 1;
                while (j < m_RealCount && group.Cover[m_Placed[j].Cluster] == cover && m_Placed[j].Line == line) j++;
                Rect row = FragmentRect(i, j, spec);
                if (m_RowLines.Count > 0 && m_RowLines[m_RowLines.Count - 1] == line)
                {
                    Rect last = m_RowRects[m_RowRects.Count - 1];
                    m_RowRects[m_RowRects.Count - 1] = Union(last, row.xMin, row.yMin, row.xMax, row.yMax);
                }
                else
                {
                    m_RowRects.Add(row);
                    m_RowLines.Add(line);
                }
                i = j;
            }

            float radius = spec.Radius.Of(m_Placed[start].Size);
            for (int r = 0; r < m_RowRects.Count; r++)
            {
                Rect row = m_RowRects[r];
                bool hasAbove = r > 0 && Overlap(row, m_RowRects[r - 1]);
                bool hasBelow = r + 1 < m_RowRects.Count && Overlap(row, m_RowRects[r + 1]);
                Rect quad = row;
                // Rows meet halfway across any space between them, so no seam or double blend shows.
                if (hasBelow)
                {
                    Rect below = m_RowRects[r + 1];
                    float middle = (row.yMin + below.yMax) * 0.5f;
                    quad.yMin = middle;
                }
                if (hasAbove)
                {
                    Rect aboveRow = m_RowRects[r - 1];
                    float middle = (aboveRow.yMin + row.yMax) * 0.5f;
                    quad.yMax = middle;
                }
                Rect above = hasAbove ? m_RowRects[r - 1] : default;
                Rect below2 = hasBelow ? m_RowRects[r + 1] : default;
                // tl, tr, br, bl
                var radii = new Vector4(
                    !hasAbove || row.xMin < above.xMin - Epsilon ? radius : 0,
                    !hasAbove || row.xMax > above.xMax + Epsilon ? radius : 0,
                    !hasBelow || row.xMax > below2.xMax + Epsilon ? radius : 0,
                    !hasBelow || row.xMin < below2.xMin - Epsilon ? radius : 0);
                // The shape's own edges run past the quad where rows join, so those edges have no antialiasing.
                Rect shape = quad;
                if (hasAbove) shape.yMax += 4;
                if (hasBelow) shape.yMin -= 4;
                EmitBox(quad, shape, radii, m_Placed[start], record, spec);
            }
            return i;
        }

        static bool Overlap(in Rect a, in Rect b) => a.xMin < b.xMax && b.xMin < a.xMax;

        static Vector4 RadiiFor(int corners, bool rtl, float radius)
        {
            int topLeft = (int)(rtl ? LetteraCorners.TopEnd : LetteraCorners.TopStart), topRight = (int)(rtl ? LetteraCorners.TopStart : LetteraCorners.TopEnd);
            int bottomRight = (int)(rtl ? LetteraCorners.BottomStart : LetteraCorners.BottomEnd), bottomLeft = (int)(rtl ? LetteraCorners.BottomEnd : LetteraCorners.BottomStart);
            return new Vector4((corners & topLeft) != 0 ? radius : 0, (corners & topRight) != 0 ? radius : 0, (corners & bottomRight) != 0 ? radius : 0, (corners & bottomLeft) != 0 ? radius : 0);
        }

        // The rectangle of a run of placed glyphs, by the highlight's height mode, with padding.
        Rect FragmentRect(int start, int end, in HighlightSpec spec)
        {
            float xMin = float.MaxValue, xMax = float.MinValue;
            float ascent = 0, descent = 0;
            for (int g = start; g < end; g++)
            {
                ref PlacedGlyph placed = ref m_Placed[g];
                xMin = Mathf.Min(xMin, placed.X);
                xMax = Mathf.Max(xMax, placed.X + placed.Advance);
                FaceMetrics metrics = placed.Instance.Face.Metrics;
                ascent = Mathf.Max(ascent, metrics.Ascender * placed.Unit);
                descent = Mathf.Max(descent, -metrics.Descender * placed.Unit);
            }
            LetteraLine line = m_Lines[m_Placed[start].Line];
            float bottom, top;
            switch (spec.Look.Height)
            {
                case LetteraHighlightHeight.LineBox:
                    bottom = line.Baseline - line.Descent;
                    top = line.Baseline + line.Ascent;
                    break;
                case LetteraHighlightHeight.LineAdvance:
                    bottom = line.Baseline - line.Descent;
                    top = line.Baseline + line.Ascent + line.GapBefore;
                    break;
                default:
                    bottom = line.Baseline - descent;
                    top = line.Baseline + ascent;
                    break;
            }
            float size = m_Placed[start].Size;
            return Rect.MinMaxRect(xMin - spec.PadLeft.Of(size), bottom - spec.PadBottom.Of(size), xMax + spec.PadRight.Of(size), top + spec.PadTop.Of(size));
        }

        void EmitBox(in Rect quad, in Rect shape, Vector4 radii, in PlacedGlyph glyph, in LayerRecord record, in HighlightSpec spec)
        {
            float limit = Mathf.Min(shape.width, shape.height) * 0.5f;
            radii = new Vector4(Mathf.Min(radii.x, limit), Mathf.Min(radii.y, limit), Mathf.Min(radii.z, limit), Mathf.Min(radii.w, limit));

            ref LetteraAttributes.Cp attr = ref m_Attributes.Chars[glyph.Cluster];
            Color32 baseColor = m_Host.BaseColor;
            Color32 color = record.Paint.UsesGlyphColor ? (attr.HasColor ? attr.Color : new Color32(baseColor.r, baseColor.g, baseColor.b, 255)) : record.Paint.Color;
            color.a = (byte)(color.a * attr.Alpha);

            Rect rect = default;
            if (!record.Paint.IsSolid)
            {
                switch (spec.PaintSpan)
                {
                    case LetteraHighlightPaintSpan.Glyph: rect = new Rect(glyph.X, quad.y, glyph.Advance, quad.height); break;
                    case LetteraHighlightPaintSpan.Line:
                        LetteraLine line = m_Lines[glyph.Line];
                        rect = new Rect(line.X, line.Baseline - line.Descent, line.Width, line.Height);
                        break;
                    case LetteraHighlightPaintSpan.Range: rect = record.RangeRect; break;
                    case LetteraHighlightPaintSpan.Block: rect = LayoutBounds; break;
                    default:
                        rect = spec.Look.Geometry == LetteraHighlightGeometry.Range || spec.Look.Geometry == LetteraHighlightGeometry.Block ? record.RangeRect : quad;
                        break;
                }
            }

            UseSegment(record.Paint);
            int v = Reserve();
            Vertices[v] = new Vector3(quad.xMin, quad.yMin);
            Vertices[v + 1] = new Vector3(quad.xMin, quad.yMax);
            Vertices[v + 2] = new Vector3(quad.xMax, quad.yMax);
            Vertices[v + 3] = new Vector3(quad.xMax, quad.yMin);
            // Positions relative to the shape's center.
            Vector2 center = shape.center;
            Uv0[v] = new Vector4(quad.xMin - center.x, quad.yMin - center.y, 0, ModeRoundedBox);
            Uv0[v + 1] = new Vector4(quad.xMin - center.x, quad.yMax - center.y, 0, ModeRoundedBox);
            Uv0[v + 2] = new Vector4(quad.xMax - center.x, quad.yMax - center.y, 0, ModeRoundedBox);
            Uv0[v + 3] = new Vector4(quad.xMax - center.x, quad.yMin - center.y, 0, ModeRoundedBox);
            var parameters = new Vector4(shape.width * 0.5f, shape.height * 0.5f, 0, 0);
            Uv1[v] = Uv1[v + 1] = Uv1[v + 2] = Uv1[v + 3] = parameters;
            SetColor(v, color);
            SetPaint(v, record.Paint, rect, 0, 0, radii);
        }
    }
}

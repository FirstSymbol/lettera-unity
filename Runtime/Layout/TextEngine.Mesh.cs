using System.Collections.Generic;
using UnityEngine;

namespace FirstSymbol.Lettera
{
    /// <summary>A stretch of the index buffer that needs its own material: a texture paint or a blend other than normal.</summary>
    internal struct MeshSegment
    {
        public int IndexStart, IndexCount;
        public LetteraBlend Blend;
        public Texture2D Texture;

        public bool IsDefault => Blend == LetteraBlend.Normal && Texture == null;
    }

    internal sealed partial class TextEngine
    {
        const float ModeSdf = 0, ModeColor = 1, ModeColorSilhouette = 2, ModeSolid = 3, ModeInnerShadow = 4, ModeColorHalo = 5;

        // How far a softness of 1 reaches, in em.
        const float SoftnessEm = GlyphAtlas.SpreadEm;

        // Vertex data of the current text. Alpha is kept apart from the base alpha, so a fade only rewrites colors.
        public Vector3[] Vertices = new Vector3[256];
        public Color32[] Colors = new Color32[256];
        public Vector4[] Uv0 = new Vector4[256];
        public Vector4[] Uv1 = new Vector4[256];
        public Vector4[] Uv2 = new Vector4[256];
        public Vector4[] Uv3 = new Vector4[256];
        public int[] Indices = new int[384];
        public int VertexCount, IndexCount;
        byte[] m_OwnAlpha = new byte[256];
        Bounds m_Bounds;

        // The text uses paints, bands or inner shadows, so it draws with the styled shader and needs UV2 and UV3.
        public bool Styled { get; private set; }
        public readonly List<MeshSegment> Segments = new List<MeshSegment>();
        LetteraBlend m_SegmentBlend;
        Texture2D m_SegmentTexture;
        int m_SegmentStart;

        AtlasGlyph[] m_PlacedAtlas = new AtlasGlyph[64];
        float[] m_PlacedReach = new float[64];
        readonly List<LayerGroup> m_Faces = new List<LayerGroup>();

        public Bounds MeshBounds => m_Bounds;

        static readonly ResolvedPaint s_NoPaint = new ResolvedPaint { Blend = LetteraBlend.Normal };

        void BuildMesh()
        {
            VertexCount = IndexCount = 0;
            Segments.Clear();
            m_SegmentBlend = LetteraBlend.Normal;
            m_SegmentTexture = null;
            m_SegmentStart = 0;
            GlyphAtlasRegistration.EnsureRegistered();

            List<LayerGroup> groups = m_Attributes.SortedGroups();
            Styled = m_Attributes.NeedsStyledShader();
            PrepareGlyphs(groups);
            PrepareFade();

            m_Faces.Clear();
            foreach (LayerGroup group in groups)
                if (group.Band == LetteraLayerBand.Face)
                    m_Faces.Add(group);

            foreach (LayerGroup group in groups)
                if (group.Band < LetteraLayerBand.Face)
                    DrawGroup(group);

            DrawFaces();
            foreach (LayerGroup group in m_Faces) DrawExtraFills(group);
            AddDecorationLayer(null);

            foreach (LayerGroup group in groups)
                if (group.Band > LetteraLayerBand.Face)
                    DrawGroup(group);

            FinishSegment();
            ComputeBounds();
            ApplyBaseColor();
        }

        static int PixelsPerEm(float size, float rasterScale) => size * rasterScale >= 72f ? 128 : 64;

        // Finds each glyph's reach and range rectangles before anything draws, so a glyph is rasterized once.
        void PrepareGlyphs(List<LayerGroup> groups)
        {
            EnsureSize(ref m_PlacedAtlas, m_PlacedCount);
            EnsureSize(ref m_PlacedReach, m_PlacedCount);
            System.Array.Clear(m_PlacedReach, 0, m_PlacedCount);
            List<LayerRecord> records = m_Attributes.Layers;
            for (int r = 0; r < records.Count; r++)
            {
                LayerRecord record = records[r];
                record.RangeRect = new Rect(float.MaxValue, float.MaxValue, 0, 0);
                records[r] = record;
            }

            foreach (LayerGroup group in groups)
            {
                int[] cover = group.Cover;
                for (int i = 0; i < m_PlacedCount; i++)
                {
                    ref PlacedGlyph g = ref m_Placed[i];
                    int index = cover[g.Cluster] - 1;
                    if (index < 0) continue;
                    LayerRecord record = records[index];
                    float reach = ReachOf(record, g);
                    if (reach > m_PlacedReach[i]) m_PlacedReach[i] = reach;
                    if (record.Paint.Span == LetteraPaintSpan.Range && !record.Paint.IsSolid)
                    {
                        LetteraLine line = m_Lines[g.Line];
                        Rect grown = Union(record.RangeRect, g.X, line.Baseline - line.Descent, g.X + g.Advance, line.Baseline + line.Ascent);
                        record.RangeRect = grown;
                        records[index] = record;
                    }
                }
            }

            float rasterScale = m_Host.RasterScale;
            for (int i = 0; i < m_PlacedCount; i++)
            {
                ref PlacedGlyph g = ref m_Placed[i];
                int density = PixelsPerEm(g.Size, rasterScale);
                byte boost = m_Attributes.Chars[g.Cluster].Boost;
                if (boost > 0) density = GlyphAtlas.Boost(g.Instance, g.Glyph, density << boost);
                m_PlacedAtlas[i] = GlyphAtlas.Get(g.Instance, g.Glyph, density, m_PlacedReach[i]);
            }

            // A halo around a color glyph blurs its alpha, which only the styled shader does.
            if (Styled) return;
            foreach (LayerGroup group in groups)
            {
                if (group.Layer != LayerKind.Glow) continue;
                for (int i = 0; i < m_PlacedCount; i++)
                {
                    if (m_PlacedAtlas[i].Kind != AtlasGlyph.Color || group.Cover[m_Placed[i].Cluster] == 0) continue;
                    Styled = true;
                    return;
                }
            }
        }

        static Rect Union(in Rect rect, float xMin, float yMin, float xMax, float yMax)
        {
            if (rect.x == float.MaxValue) return Rect.MinMaxRect(xMin, yMin, xMax, yMax);
            return Rect.MinMaxRect(Mathf.Min(rect.xMin, xMin), Mathf.Min(rect.yMin, yMin), Mathf.Max(rect.xMax, xMax), Mathf.Max(rect.yMax, yMax));
        }

        float MeasureSize(in LayerRecord record, in PlacedGlyph g) => record.FixedSize ? CurrentFontSize : g.Size;

        // How far beyond the outline a layer needs the distance field to reach, in em of the glyph.
        float ReachOf(in LayerRecord record, in PlacedGlyph g)
        {
            float size = MeasureSize(record, g);
            float scale = g.Size > 0 ? 1f / g.Size : 0;
            float soft = record.Softness * SoftnessEm;
            switch (record.Kind)
            {
                case LayerKind.Stroke:
                    float width = record.Width.Of(size) * scale;
                    return Mathf.Max(width * (1 + record.Align) * 0.5f, width * (1 - record.Align) * 0.5f) + soft;
                case LayerKind.Glow:
                    return Mathf.Abs(record.Dilate.Of(size) * scale) + record.Width.Of(size) * scale;
                case LayerKind.InnerShadow:
                    return Mathf.Max(Mathf.Abs(record.OffsetX.Of(size)), Mathf.Abs(record.OffsetY.Of(size))) * scale + Mathf.Abs(record.Dilate.Of(size) * scale) + soft;
                default:
                    return Mathf.Abs(record.Dilate.Of(size) * scale) + soft;
            }
        }

        void DrawGroup(LayerGroup group)
        {
            List<LayerRecord> records = m_Attributes.Layers;
            int[] cover = group.Cover;
            for (int i = 0; i < m_PlacedCount; i++)
            {
                int index = cover[m_Placed[i].Cluster] - 1;
                if (index >= 0) DrawLayer(i, records[index]);
            }
            AddDecorationLayer(group);
        }

        void DrawFaces()
        {
            List<LayerRecord> records = m_Attributes.Layers;
            LetteraAttributes.Cp[] attrs = m_Attributes.Chars;
            Color32 baseColor = m_Host.BaseColor;
            for (int i = 0; i < m_PlacedCount; i++)
            {
                ref PlacedGlyph g = ref m_Placed[i];
                ref AtlasGlyph atlas = ref m_PlacedAtlas[i];
                if (atlas.Kind == AtlasGlyph.Empty) continue;
                if (FirstFill(g.Cluster, atlas.Kind, out int record))
                {
                    DrawLayer(i, records[record]);
                    continue;
                }
                ref LetteraAttributes.Cp attr = ref attrs[g.Cluster];
                UseSegment(s_NoPaint);
                if (atlas.Kind == AtlasGlyph.Color)
                {
                    // Color glyphs keep their own colors; only alpha applies.
                    AddQuad(g, atlas, new Color32(255, 255, 255, (byte)(attr.Alpha * 255f)), ModeColor, 0.5f, 0, 0, s_NoPaint, default, 0, 0);
                }
                else
                {
                    Color32 color = attr.HasColor ? attr.Color : new Color32(baseColor.r, baseColor.g, baseColor.b, 255);
                    color.a = (byte)(color.a * attr.Alpha);
                    AddQuad(g, atlas, color, ModeSdf, 0.5f, 0, 0, s_NoPaint, default, 0, 0);
                }
            }
        }

        // The first fill replaces the face: it costs no extra quad.
        bool FirstFill(int cluster, byte kind, out int record)
        {
            record = -1;
            foreach (LayerGroup group in m_Faces)
            {
                int index = group.Cover[cluster] - 1;
                if (index < 0) continue;
                LayerRecord candidate = m_Attributes.Layers[index];
                if (candidate.Kind != LayerKind.Fill || (kind == AtlasGlyph.Color && !candidate.OnColorGlyphs)) continue;
                record = index;
                return true;
            }
            return false;
        }

        void DrawExtraFills(LayerGroup group)
        {
            List<LayerRecord> records = m_Attributes.Layers;
            for (int i = 0; i < m_PlacedCount; i++)
            {
                int index = group.Cover[m_Placed[i].Cluster] - 1;
                if (index < 0) continue;
                if (FirstFill(m_Placed[i].Cluster, m_PlacedAtlas[i].Kind, out int first) && first == index) continue;
                DrawLayer(i, records[index]);
            }
            if (group.Layer != LayerKind.Fill) AddDecorationLayer(group);
        }

        void DrawLayer(int glyphIndex, in LayerRecord record)
        {
            ref PlacedGlyph g = ref m_Placed[glyphIndex];
            ref AtlasGlyph atlas = ref m_PlacedAtlas[glyphIndex];
            if (atlas.Kind == AtlasGlyph.Empty) return;
            bool colorGlyph = atlas.Kind == AtlasGlyph.Color;
            if (colorGlyph && !record.OnColorGlyphs) return;

            ref LetteraAttributes.Cp attr = ref m_Attributes.Chars[g.Cluster];
            Color32 color = record.Paint.Color;
            if (record.Paint.UsesGlyphColor)
            {
                Color32 baseColor = m_Host.BaseColor;
                color = attr.HasColor ? attr.Color : new Color32(baseColor.r, baseColor.g, baseColor.b, 255);
            }
            color.a = (byte)(color.a * attr.Alpha);

            float size = MeasureSize(record, g);
            float toEm = g.Size > 0 ? 1f / g.Size : 0;
            float toSdf = 1f / (2f * atlas.Spread);
            float softness = record.Softness * SoftnessEm * toSdf;
            float mode = colorGlyph ? ModeColorSilhouette : ModeSdf;
            float threshold = 0.5f, upper = 0, dx = 0, dy = 0;
            Vector4 extra = default;

            switch (record.Kind)
            {
                case LayerKind.Fill:
                    threshold = 0.5f - record.Dilate.Of(size) * toEm * toSdf;
                    break;
                case LayerKind.Stroke:
                    float width = record.Width.Of(size) * toEm;
                    if (width <= 0) return;
                    threshold = 0.5f - width * (1 + record.Align) * 0.5f * toSdf;
                    if (record.Align < 1) upper = 0.5f + width * (1 - record.Align) * 0.5f * toSdf;
                    // Under an opaque face a stroke can run through the glyph, which leaves no seam at the edge.
                    // Under a see-through face the middle must stay empty.
                    else if (FaceIsSeeThrough(g.Cluster)) upper = 0.5f;
                    break;
                case LayerKind.Shadow:
                    dx = record.OffsetX.Of(size);
                    dy = record.OffsetY.Of(size);
                    threshold = 0.5f - record.Dilate.Of(size) * toEm * toSdf;
                    break;
                case LayerKind.Glow:
                    float radius = record.Width.Of(size) * toEm;
                    if (radius <= 0) return;
                    if (colorGlyph)
                    {
                        AddColorHalo(g, atlas, color, record, radius * g.Size);
                        return;
                    }
                    float ramp = radius * Mathf.Max(record.Softness, 0.01f);
                    threshold = 0.5f - (record.Dilate.Of(size) * toEm + radius - ramp * 0.5f) * toSdf;
                    softness = ramp * 0.5f * toSdf;
                    break;
                case LayerKind.InnerShadow:
                    if (colorGlyph) return;
                    mode = ModeInnerShadow;
                    float quadWidth = (atlas.Right - atlas.Left) * g.Unit, quadHeight = (atlas.Top - atlas.Bottom) * g.Unit;
                    extra.x = quadWidth > 0 ? record.OffsetX.Of(size) / quadWidth * (atlas.U1 - atlas.U0) : 0;
                    extra.y = quadHeight > 0 ? record.OffsetY.Of(size) / quadHeight * (atlas.V1 - atlas.V0) : 0;
                    extra.z = 0.5f + record.Dilate.Of(size) * toEm * toSdf;
                    break;
            }

            UseSegment(record.Paint);
            PlacedGlyph moved = g;
            moved.X += dx;
            moved.Y += dy;
            AddQuad(moved, atlas, color, mode, threshold, softness, upper, record.Paint, PaintRect(record, g), dx, dy, extra);
        }

        bool FaceIsSeeThrough(int cluster)
        {
            foreach (LayerGroup group in m_Faces)
            {
                int index = group.Cover[cluster] - 1;
                if (index < 0) continue;
                LayerRecord fill = m_Attributes.Layers[index];
                return fill.Kind == LayerKind.Fill && fill.Paint.IsSolid && fill.Paint.Color.a < 255;
            }
            return false;
        }

        // Color glyphs have no distance field: a halo grows the quad and blurs the alpha, staying inside the glyph's own tile.
        void AddColorHalo(in PlacedGlyph g, in AtlasGlyph atlas, Color32 color, in LayerRecord record, float radius)
        {
            float width = (atlas.Right - atlas.Left) * g.Unit, height = (atlas.Top - atlas.Bottom) * g.Unit;
            if (width <= 0 || height <= 0) return;
            float du = radius / width * (atlas.U1 - atlas.U0), dv = radius / height * (atlas.V1 - atlas.V0);
            UseSegment(record.Paint);
            int v = Reserve();
            float left = g.X + atlas.Left * g.Unit - radius, right = g.X + atlas.Right * g.Unit + radius;
            float bottom = g.Y + atlas.Bottom * g.Unit - radius, top = g.Y + atlas.Top * g.Unit + radius;
            Vertices[v] = new Vector3(left, bottom);
            Vertices[v + 1] = new Vector3(left, top);
            Vertices[v + 2] = new Vector3(right, top);
            Vertices[v + 3] = new Vector3(right, bottom);
            Uv0[v] = new Vector4(atlas.U0 - du, atlas.V0 - dv, atlas.Page, ModeColorHalo);
            Uv0[v + 1] = new Vector4(atlas.U0 - du, atlas.V1 + dv, atlas.Page, ModeColorHalo);
            Uv0[v + 2] = new Vector4(atlas.U1 + du, atlas.V1 + dv, atlas.Page, ModeColorHalo);
            Uv0[v + 3] = new Vector4(atlas.U1 + du, atlas.V0 - dv, atlas.Page, ModeColorHalo);
            var parameters = new Vector4(du, dv, 0, 0);
            Uv1[v] = Uv1[v + 1] = Uv1[v + 2] = Uv1[v + 3] = parameters;
            SetColor(v, color);
            SetPaint(v, record.Paint, PaintRect(record, g), 0, 0, new Vector4(atlas.U0, atlas.V0, atlas.U1, atlas.V1));
        }

        Rect PaintRect(in LayerRecord record, in PlacedGlyph g)
        {
            if (record.Paint.IsSolid) return default;
            LetteraLine line = m_Lines[g.Line];
            switch (record.Paint.Span)
            {
                case LetteraPaintSpan.Glyph: return new Rect(g.X, line.Baseline - line.Descent, g.Advance, line.Height);
                case LetteraPaintSpan.Line: return new Rect(line.X, line.Baseline - line.Descent, line.Width, line.Height);
                case LetteraPaintSpan.Range: return record.RangeRect;
                default: return LayoutBounds;
            }
        }

        // Where a point lies in a paint: a ramp position in x for a linear gradient, a position relative to the center otherwise.
        static Vector2 PaintPosition(in ResolvedPaint paint, in Rect rect, float x, float y)
        {
            float width = rect.width, height = rect.height;
            if (width <= 0 && height <= 0) return default;
            float qx = x - rect.center.x, qy = y - rect.center.y;
            float radians = paint.Angle * Mathf.Deg2Rad;
            float cos = Mathf.Cos(radians), sin = Mathf.Sin(radians);

            if (paint.Code == ResolvedPaint.CodeTexture)
            {
                float rx = qx * cos + qy * sin, ry = qy * cos - qx * sin;
                float aspect = paint.TextureAspect, spanAspect = height > 0 ? width / height : 1;
                float u = rx / Mathf.Max(width, 1e-4f), v = ry / Mathf.Max(height, 1e-4f);
                switch (paint.Fit)
                {
                    case LetteraPaintFit.Contain:
                        if (spanAspect > aspect) u *= spanAspect / aspect;
                        else v *= aspect / spanAspect;
                        break;
                    case LetteraPaintFit.Cover:
                        if (spanAspect > aspect) v *= aspect / spanAspect;
                        else u *= spanAspect / aspect;
                        break;
                    case LetteraPaintFit.Tile:
                        return new Vector2((u + 0.5f) * paint.Scale * spanAspect / aspect + paint.Offset.x, (v + 0.5f) * paint.Scale + paint.Offset.y);
                }
                return new Vector2(u / paint.Scale + 0.5f + paint.Offset.x, v / paint.Scale + 0.5f + paint.Offset.y);
            }

            if (paint.Shape == LetteraPaintShape.Linear)
            {
                float length = Mathf.Abs(width * cos) + Mathf.Abs(height * sin);
                float t = length > 0 ? 0.5f + (qx * cos + qy * sin) / length : 0;
                return new Vector2(t / paint.Scale + paint.Offset.x, 0);
            }

            float px = qx * cos + qy * sin, py = qy * cos - qx * sin;
            float radiusX = width * 0.5f, radiusY = height * 0.5f;
            if (paint.Fit == LetteraPaintFit.Contain || paint.Fit == LetteraPaintFit.Tile) radiusX = radiusY = Mathf.Min(radiusX, radiusY);
            else if (paint.Fit == LetteraPaintFit.Cover) radiusX = radiusY = Mathf.Max(radiusX, radiusY);
            radiusX = Mathf.Max(radiusX, 1e-4f);
            radiusY = Mathf.Max(radiusY, 1e-4f);
            float scale = paint.Fit == LetteraPaintFit.Tile ? 1f / paint.Scale : paint.Scale;
            return new Vector2(px / radiusX / scale - paint.Offset.x * 2, py / radiusY / scale - paint.Offset.y * 2);
        }

        void UseSegment(in ResolvedPaint paint)
        {
            LetteraBlend blend = paint.Blend == LetteraBlend.Inherit ? LetteraBlend.Normal : paint.Blend;
            if (blend == m_SegmentBlend && paint.Texture == m_SegmentTexture) return;
            FinishSegment();
            m_SegmentBlend = blend;
            m_SegmentTexture = paint.Texture;
            m_SegmentStart = IndexCount;
        }

        void FinishSegment()
        {
            if (IndexCount > m_SegmentStart)
                Segments.Add(new MeshSegment { IndexStart = m_SegmentStart, IndexCount = IndexCount - m_SegmentStart, Blend = m_SegmentBlend, Texture = m_SegmentTexture });
            m_SegmentStart = IndexCount;
        }

        void AddQuad(in PlacedGlyph g, in AtlasGlyph atlas, Color32 color, float mode, float threshold, float softness, float upper, in ResolvedPaint paint, in Rect paintRect, float paintDx, float paintDy, Vector4 extra = default)
        {
            float xUnit = g.Unit * (1 + g.XScale);
            float left = g.X + atlas.Left * xUnit, right = g.X + atlas.Right * xUnit;
            float bottom = g.Y + atlas.Bottom * g.Unit, top = g.Y + atlas.Top * g.Unit;
            int v = Reserve();
            Vertices[v] = new Vector3(left, bottom);
            Vertices[v + 1] = new Vector3(left, top);
            Vertices[v + 2] = new Vector3(right, top);
            Vertices[v + 3] = new Vector3(right, bottom);
            Uv0[v] = new Vector4(atlas.U0, atlas.V0, atlas.Page, mode);
            Uv0[v + 1] = new Vector4(atlas.U0, atlas.V1, atlas.Page, mode);
            Uv0[v + 2] = new Vector4(atlas.U1, atlas.V1, atlas.Page, mode);
            Uv0[v + 3] = new Vector4(atlas.U1, atlas.V0, atlas.Page, mode);
            var parameters = new Vector4(threshold, softness, upper, 0);
            Uv1[v] = Uv1[v + 1] = Uv1[v + 2] = Uv1[v + 3] = parameters;
            SetColor(v, color);
            if (Styled) SetPaint(v, paint, paintRect, paintDx, paintDy, extra);
            if (m_FadeActive && g.Line == m_FadeLine) ApplyFade(v);
        }

        // Alpha falls off towards a truncated edge, per vertex, so it runs smoothly across a glyph.
        bool m_FadeActive;
        int m_FadeLine;
        float m_FadeCut, m_FadeDistance, m_FadeSign;

        void PrepareFade()
        {
            m_FadeActive = false;
            foreach (Removal removal in m_Removals)
            {
                if (removal.FadeDistance.IsZero) continue;
                float size = CurrentFontSize;
                float distance = removal.FadeDistance.Of(size);
                if (distance <= 0) continue;
                // The cut is where the glyphs before the removal stop, on the line that holds it.
                for (int l = 0; l < m_Lines.Count; l++)
                {
                    LetteraLine line = m_Lines[l];
                    if (removal.Start < line.Start || removal.Start > line.End) continue;
                    bool rtl = (m_Paragraphs[line.Paragraph].Level & 1) != 0;
                    m_FadeActive = true;
                    m_FadeLine = l;
                    m_FadeSign = rtl ? -1 : 1;
                    m_FadeDistance = distance;
                    m_FadeCut = (rtl ? line.X : line.X + line.Width) - m_FadeSign * removal.FadeShift.Of(size);
                    return;
                }
            }
        }

        void ApplyFade(int v)
        {
            for (int i = 0; i < 4; i++)
            {
                float toCut = (m_FadeCut - Vertices[v + i].x) * m_FadeSign;
                float fade = Mathf.Clamp01(toCut / m_FadeDistance);
                Colors[v + i].a = (byte)(Colors[v + i].a * fade);
                m_OwnAlpha[v + i] = Colors[v + i].a;
            }
        }

        void AddSolid(float left, float bottom, float right, float top, Color32 color, in ResolvedPaint paint, in Rect paintRect, float paintDx, float paintDy)
        {
            int v = Reserve();
            Vertices[v] = new Vector3(left, bottom);
            Vertices[v + 1] = new Vector3(left, top);
            Vertices[v + 2] = new Vector3(right, top);
            Vertices[v + 3] = new Vector3(right, bottom);
            var uv = new Vector4(0, 0, 0, ModeSolid);
            Uv0[v] = Uv0[v + 1] = Uv0[v + 2] = Uv0[v + 3] = uv;
            Uv1[v] = Uv1[v + 1] = Uv1[v + 2] = Uv1[v + 3] = default;
            SetColor(v, color);
            if (Styled) SetPaint(v, paint, paintRect, paintDx, paintDy, default);
        }

        void SetPaint(int v, in ResolvedPaint paint, in Rect rect, float dx, float dy, Vector4 extra)
        {
            Uv3[v] = Uv3[v + 1] = Uv3[v + 2] = Uv3[v + 3] = extra;
            if (paint.IsSolid)
            {
                Uv2[v] = Uv2[v + 1] = Uv2[v + 2] = Uv2[v + 3] = default;
                return;
            }
            // The paint stays with the glyph when a layer moves it, so a shadow samples the same place as its face.
            for (int i = 0; i < 4; i++)
            {
                Vector3 p = Vertices[v + i];
                Vector2 position = PaintPosition(paint, rect, p.x - dx, p.y - dy);
                Uv2[v + i] = new Vector4(position.x, position.y, paint.RampV, paint.Code);
            }
        }

        void SetColor(int v, Color32 color)
        {
            Colors[v] = Colors[v + 1] = Colors[v + 2] = Colors[v + 3] = color;
            m_OwnAlpha[v] = m_OwnAlpha[v + 1] = m_OwnAlpha[v + 2] = m_OwnAlpha[v + 3] = color.a;
        }

        int Reserve()
        {
            int v = VertexCount;
            if (v + 4 > Vertices.Length)
            {
                int capacity = Vertices.Length * 2;
                System.Array.Resize(ref Vertices, capacity);
                System.Array.Resize(ref Colors, capacity);
                System.Array.Resize(ref Uv0, capacity);
                System.Array.Resize(ref Uv1, capacity);
                System.Array.Resize(ref Uv2, capacity);
                System.Array.Resize(ref Uv3, capacity);
                System.Array.Resize(ref m_OwnAlpha, capacity);
            }
            if (IndexCount + 6 > Indices.Length) System.Array.Resize(ref Indices, Indices.Length * 2);
            int i = IndexCount;
            Indices[i] = v;
            Indices[i + 1] = v + 1;
            Indices[i + 2] = v + 2;
            Indices[i + 3] = v + 2;
            Indices[i + 4] = v + 3;
            Indices[i + 5] = v;
            VertexCount += 4;
            IndexCount += 6;
            return v;
        }

        // Decoration lines follow the glyphs in visual order: one line per fragment with the same setting.
        // A group draws the line's copy of that layer; no group draws the line itself.
        void AddDecorationLayer(LayerGroup group)
        {
            if (m_Attributes.Decorations.Count == 0) return;
            if (group != null && group.Layer != LayerKind.Shadow && group.Layer != LayerKind.Stroke) return;
            AddLinesOf(group, false);
            AddLinesOf(group, true);
        }

        void AddLinesOf(LayerGroup group, bool strike)
        {
            LetteraAttributes.Cp[] attrs = m_Attributes.Chars;
            int i = 0;
            while (i < m_RealCount)
            {
                ref PlacedGlyph first = ref m_Placed[i];
                ref LetteraAttributes.Cp attr = ref attrs[first.Cluster];
                int index = strike ? attr.Strike : attr.Underline;
                int layer = group != null ? group.Cover[first.Cluster] : 0;
                if (index == 0)
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
                if (group == null || layer != 0) AddDecorationLine(first, attr, index, strike, left, right, group, layer - 1);
                i = j;
            }
        }

        void AddDecorationLine(in PlacedGlyph glyph, in LetteraAttributes.Cp attr, int index, bool strike, float left, float right, LayerGroup group, int recordIndex)
        {
            FaceMetrics metrics = glyph.Instance.Face.Metrics;
            float offset = (strike ? metrics.StrikeOffset : metrics.UnderlineOffset) * glyph.Unit;
            float thickness = (strike ? metrics.StrikeSize : metrics.UnderlineSize) * glyph.Unit;
            float y = glyph.Line < m_Lines.Count ? m_Lines[glyph.Line].Baseline : glyph.Y;
            float top = y + offset + thickness * 0.5f, bottom = y + offset - thickness * 0.5f;

            if (group != null)
            {
                LayerRecord record = m_Attributes.Layers[recordIndex];
                float size = MeasureSize(record, glyph);
                Color32 color = record.Paint.Color;
                if (record.Paint.UsesGlyphColor)
                {
                    Color32 own = m_Host.BaseColor;
                    color = attr.HasColor ? attr.Color : new Color32(own.r, own.g, own.b, 255);
                }
                color.a = (byte)(color.a * attr.Alpha);
                Rect rect = PaintRect(record, glyph);
                UseSegment(record.Paint);
                if (group.Layer == LayerKind.Shadow)
                {
                    float sx = record.OffsetX.Of(size), sy = record.OffsetY.Of(size);
                    AddSolid(left + sx, bottom + sy, right + sx, top + sy, color, record.Paint, rect, sx, sy);
                }
                else
                {
                    float width = record.Width.Of(size);
                    if (width <= 0) return;
                    float outer = width * (1 + record.Align) * 0.5f;
                    AddSolid(left - outer, bottom - outer, right + outer, top + outer, color, record.Paint, rect, 0, 0);
                }
                return;
            }

            LetteraAttributes.DecorationSpec spec = m_Attributes.Decorations[index - 1];
            Color32 baseColor = m_Host.BaseColor;
            Color32 lineColor = spec.HasColor ? spec.Color : attr.HasColor ? attr.Color : new Color32(baseColor.r, baseColor.g, baseColor.b, 255);
            lineColor.a = (byte)(lineColor.a * attr.Alpha);
            UseSegment(s_NoPaint);
            AddSolid(left, bottom, right, top, lineColor, s_NoPaint, default, 0, 0);
        }

        void ComputeBounds()
        {
            if (VertexCount == 0)
            {
                m_Bounds = new Bounds(m_Box.center, Vector3.zero);
                return;
            }
            float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
            for (int i = 0; i < VertexCount; i++)
            {
                Vector3 p = Vertices[i];
                if (p.x < minX) minX = p.x;
                if (p.x > maxX) maxX = p.x;
                if (p.y < minY) minY = p.y;
                if (p.y > maxY) maxY = p.y;
            }
            var center = new Vector3((minX + maxX) * 0.5f, (minY + maxY) * 0.5f);
            m_Bounds = new Bounds(center, new Vector3(maxX - minX, maxY - minY));
        }

        /// <summary>The fade path: rewrites vertex alpha only, without touching geometry.</summary>
        void ApplyBaseColor()
        {
            int alpha = m_Host.BaseColor.a;
            for (int i = 0; i < VertexCount; i++)
                Colors[i].a = (byte)(m_OwnAlpha[i] * alpha / 255);
        }

        /// <summary>Marks mesh dirty when the atlas lost its glyphs.</summary>
        public void OnAtlasCleared() => SetDirty(DirtyLevel.Mesh);
    }
}

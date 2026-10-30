using UnityEngine;

namespace FirstSymbol.Lettera
{
    internal sealed partial class TextEngine
    {
        const float ModeSdf = 0, ModeColor = 1, ModeColorSilhouette = 2, ModeSolid = 3;

        // Vertex data of the current text. Alpha is kept apart from the base alpha, so a fade only rewrites colors.
        public Vector3[] Vertices = new Vector3[256];
        public Color32[] Colors = new Color32[256];
        public Vector4[] Uv0 = new Vector4[256];
        public Vector4[] Uv1 = new Vector4[256];
        public int[] Indices = new int[384];
        public int VertexCount, IndexCount;
        byte[] m_OwnAlpha = new byte[256];
        Bounds m_Bounds;

        public Bounds MeshBounds => m_Bounds;

        void BuildMesh()
        {
            VertexCount = IndexCount = 0;
            GlyphAtlasRegistration.EnsureRegistered();
            LetteraAttributes.Cp[] attrs = m_Attributes.Chars;
            float rasterScale = m_Host.RasterScale;

            if (m_Attributes.Shadows.Count > 0)
            {
                for (int i = 0; i < m_PlacedCount; i++)
                {
                    ref PlacedGlyph g = ref m_Placed[i];
                    int index = attrs[g.Cluster].Shadow;
                    if (index != 0) AddShadow(g, m_Attributes.Shadows[index - 1], rasterScale);
                }
                AddDecorationLayer(rasterScale, DecorationLayer.Shadow);
            }

            if (m_Attributes.Outlines.Count > 0)
            {
                for (int i = 0; i < m_PlacedCount; i++)
                {
                    ref PlacedGlyph g = ref m_Placed[i];
                    int index = attrs[g.Cluster].Outline;
                    if (index != 0) AddOutline(g, m_Attributes.Outlines[index - 1], rasterScale);
                }
                AddDecorationLayer(rasterScale, DecorationLayer.Outline);
            }

            Color32 baseColor = m_Host.BaseColor;
            for (int i = 0; i < m_PlacedCount; i++)
            {
                ref PlacedGlyph g = ref m_Placed[i];
                AtlasGlyph atlas = GlyphAtlas.Get(g.Instance, g.Glyph, PixelsPerEm(g.Size, rasterScale));
                if (atlas.Kind == AtlasGlyph.Empty) continue;
                ref LetteraAttributes.Cp attr = ref attrs[g.Cluster];
                if (atlas.Kind == AtlasGlyph.Color)
                {
                    // Color glyphs keep their own colors; only alpha applies.
                    AddQuad(g, atlas, new Color32(255, 255, 255, (byte)(attr.Alpha * 255f)), ModeColor, 0.5f, 0);
                }
                else
                {
                    Color32 color = attr.HasColor ? attr.Color : new Color32(baseColor.r, baseColor.g, baseColor.b, 255);
                    color.a = (byte)(color.a * attr.Alpha);
                    AddQuad(g, atlas, color, ModeSdf, 0.5f, 0);
                }
            }
            AddDecorationLayer(rasterScale, DecorationLayer.Face);

            ComputeBounds();
            ApplyBaseColor();
        }

        static int PixelsPerEm(float size, float rasterScale) => size * rasterScale >= 72f ? 128 : 64;

        void AddShadow(in PlacedGlyph g, in LetteraAttributes.ShadowSpec spec, float rasterScale)
        {
            AtlasGlyph atlas = GlyphAtlas.Get(g.Instance, g.Glyph, PixelsPerEm(g.Size, rasterScale));
            if (atlas.Kind == AtlasGlyph.Empty) return;
            ref LetteraAttributes.Cp attr = ref m_Attributes.Chars[g.Cluster];
            Color32 color = spec.Color;
            color.a = (byte)(color.a * attr.Alpha);
            float threshold = 0.5f - spec.Dilate / (2f * GlyphAtlas.SpreadEm);
            PlacedGlyph shifted = g;
            shifted.X += spec.OffsetX * g.Size;
            shifted.Y += spec.OffsetY * g.Size;
            AddQuad(shifted, atlas, color, atlas.Kind == AtlasGlyph.Color ? ModeColorSilhouette : ModeSdf, threshold, spec.Softness * 0.5f);
        }

        void AddOutline(in PlacedGlyph g, in LetteraAttributes.OutlineSpec spec, float rasterScale)
        {
            AtlasGlyph atlas = GlyphAtlas.Get(g.Instance, g.Glyph, PixelsPerEm(g.Size, rasterScale));
            if (atlas.Kind != AtlasGlyph.Sdf) return;
            ref LetteraAttributes.Cp attr = ref m_Attributes.Chars[g.Cluster];
            Color32 color = spec.Color;
            color.a = (byte)(color.a * attr.Alpha);
            AddQuad(g, atlas, color, ModeSdf, OutlineThreshold(spec, g.Size), 0);
        }

        static float OutlineWidthEm(in LetteraAttributes.OutlineSpec spec, float size) =>
            Mathf.Min(spec.WidthEm + (size > 0 ? spec.WidthUnits / size : 0), GlyphAtlas.SpreadEm);

        static float OutlineThreshold(in LetteraAttributes.OutlineSpec spec, float size) =>
            0.5f - OutlineWidthEm(spec, size) / (2f * GlyphAtlas.SpreadEm);

        void AddQuad(in PlacedGlyph g, in AtlasGlyph atlas, Color32 color, float mode, float threshold, float softness)
        {
            float left = g.X + atlas.Left * g.Unit, right = g.X + atlas.Right * g.Unit;
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
            var extra = new Vector4(threshold, softness, 0, 0);
            Uv1[v] = Uv1[v + 1] = Uv1[v + 2] = Uv1[v + 3] = extra;
            SetColor(v, color);
        }

        void AddSolid(float left, float bottom, float right, float top, Color32 color)
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

        enum DecorationLayer { Shadow, Outline, Face }

        // Decoration lines follow the glyphs in visual order: one line per fragment with the same setting.
        void AddDecorationLayer(float rasterScale, DecorationLayer layer)
        {
            if (m_Attributes.Decorations.Count == 0) return;
            AddLinesOf(layer, false);
            AddLinesOf(layer, true);
        }

        void AddLinesOf(DecorationLayer layer, bool strike)
        {
            LetteraAttributes.Cp[] attrs = m_Attributes.Chars;
            int i = 0;
            while (i < m_PlacedCount)
            {
                ref PlacedGlyph first = ref m_Placed[i];
                ref LetteraAttributes.Cp attr = ref attrs[first.Cluster];
                byte index = strike ? attr.Strike : attr.Underline;
                if (index == 0)
                {
                    i++;
                    continue;
                }
                float left = first.X, right = first.X + first.Advance;
                int j = i + 1;
                while (j < m_PlacedCount)
                {
                    ref PlacedGlyph next = ref m_Placed[j];
                    ref LetteraAttributes.Cp nextAttr = ref attrs[next.Cluster];
                    if (next.Line != first.Line || (strike ? nextAttr.Strike : nextAttr.Underline) != index || next.Size != first.Size ||
                        nextAttr.Color.r != attr.Color.r || nextAttr.Color.g != attr.Color.g || nextAttr.Color.b != attr.Color.b || nextAttr.HasColor != attr.HasColor) break;
                    left = Mathf.Min(left, next.X);
                    right = Mathf.Max(right, next.X + next.Advance);
                    j++;
                }
                AddDecorationLine(first, attr, index, strike, left, right, layer);
                i = j;
            }
        }

        void AddDecorationLine(in PlacedGlyph glyph, in LetteraAttributes.Cp attr, int index, bool strike, float left, float right, DecorationLayer layer)
        {
            FaceMetrics metrics = glyph.Instance.Face.Metrics;
            float offset = (strike ? metrics.StrikeOffset : metrics.UnderlineOffset) * glyph.Unit;
            float thickness = (strike ? metrics.StrikeSize : metrics.UnderlineSize) * glyph.Unit;
            float y = glyph.Line < m_Lines.Count ? m_Lines[glyph.Line].Baseline : glyph.Y;
            float top = y + offset + thickness * 0.5f, bottom = y + offset - thickness * 0.5f;

            switch (layer)
            {
                case DecorationLayer.Shadow:
                    if (attr.Shadow == 0) return;
                    LetteraAttributes.ShadowSpec shadow = m_Attributes.Shadows[attr.Shadow - 1];
                    float sx = shadow.OffsetX * glyph.Size, sy = shadow.OffsetY * glyph.Size;
                    Color32 shadowColor = shadow.Color;
                    shadowColor.a = (byte)(shadowColor.a * attr.Alpha);
                    AddSolid(left + sx, bottom + sy, right + sx, top + sy, shadowColor);
                    return;
                case DecorationLayer.Outline:
                    if (attr.Outline == 0) return;
                    LetteraAttributes.OutlineSpec outline = m_Attributes.Outlines[attr.Outline - 1];
                    float w = OutlineWidthEm(outline, glyph.Size) * glyph.Size;
                    Color32 outlineColor = outline.Color;
                    outlineColor.a = (byte)(outlineColor.a * attr.Alpha);
                    AddSolid(left - w, bottom - w, right + w, top + w, outlineColor);
                    return;
                default:
                    LetteraAttributes.DecorationSpec spec = m_Attributes.Decorations[index - 1];
                    Color32 baseColor = m_Host.BaseColor;
                    Color32 color = spec.HasColor ? spec.Color : attr.HasColor ? attr.Color : new Color32(baseColor.r, baseColor.g, baseColor.b, 255);
                    color.a = (byte)(color.a * attr.Alpha);
                    AddSolid(left, bottom, right, top, color);
                    return;
            }
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

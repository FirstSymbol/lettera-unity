using System.Collections.Generic;
using FirstSymbol.Lettera.Unicode;
using UnityEngine;

namespace FirstSymbol.Lettera
{
    /// <summary>A stretch of the index buffer that needs its own material: a texture paint or a blend other than normal.</summary>
    internal struct MeshSegment
    {
        public int IndexStart, IndexCount;
        public LetteraBlend Blend;
        public Texture2D Texture;
        public Material Custom;

        public bool IsDefault => Blend == LetteraBlend.Normal && Texture == null && Custom == null;
    }

    internal sealed partial class TextEngine
    {
        const float ModeSdf = 0, ModeColor = 1, ModeColorSilhouette = 2, ModeSolid = 3, ModeInnerShadow = 4, ModeColorHalo = 5;
        const float ModeMedian = 8, ModeTrueDistance = 9, ModeInnerShadowMulti = 10, ModePixel = 11, ModeInnerShadowPixel = 12, ModeMerged = 13;

        // How far a softness of 1 reaches, in em.
        const float SoftnessEm = GlyphAtlas.SpreadEm;

        // Vertex data of the current text. Alpha is kept apart from the base alpha, so a fade only rewrites colors.
        public Vector3[] Vertices = System.Array.Empty<Vector3>();
        public Color32[] Colors = System.Array.Empty<Color32>();
        public Vector4[] Uv0 = System.Array.Empty<Vector4>();
        public Vector4[] Uv1 = System.Array.Empty<Vector4>();
        public Vector4[] Uv2 = System.Array.Empty<Vector4>();
        public Vector4[] Uv3 = System.Array.Empty<Vector4>();
        public int[] Indices = System.Array.Empty<int>();
        public int VertexCount, IndexCount;
        byte[] m_OwnAlpha = System.Array.Empty<byte>();
        Bounds m_Bounds;

        // The text uses paints, bands or inner shadows, so it draws with the styled shader and needs UV2 and UV3.
        public bool Styled { get; private set; }

        // The filter row the next quad gets.
        float m_FilterRow;
        public readonly List<MeshSegment> Segments = new List<MeshSegment>();
        LetteraBlend m_SegmentBlend;
        Texture2D m_SegmentTexture;
        Material m_SegmentCustom;
        int m_SegmentStart;

        AtlasGlyph[] m_PlacedAtlas = System.Array.Empty<AtlasGlyph>();
        float[] m_PlacedReach = System.Array.Empty<float>();
        readonly List<LayerGroup> m_Faces = new List<LayerGroup>();

        public Bounds MeshBounds => m_Bounds;

        // Where ink can show, inside the quads' margins, so a mask can cull a text whose ink lies outside it though its quads reach in.
        // Known only while every quad was counted and nothing moves vertices after the mesh is built.
        float m_InkMinX, m_InkMinY, m_InkMaxX, m_InkMaxY, m_InkMargin;
        int m_InkQuads;

        /// <summary>Whether <see cref="InkRect"/> holds for the current mesh.</summary>
        public bool InkKnown { get; private set; }

        /// <summary>The box outside which this mesh draws nothing, a pixel to spare, in local units.</summary>
        public Rect InkRect => Rect.MinMaxRect(m_InkMinX, m_InkMinY, m_InkMaxX, m_InkMaxY);

        // Only a Canvas text under a mask asks; the rest skip it per glyph.
        bool m_TrackInk;

        void AddInk(float left, float bottom, float right, float top)
        {
            if (!m_TrackInk) return;
            m_InkQuads++;
            if (left < m_InkMinX) m_InkMinX = left;
            if (bottom < m_InkMinY) m_InkMinY = bottom;
            if (right > m_InkMaxX) m_InkMaxX = right;
            if (top > m_InkMaxY) m_InkMaxY = top;
        }

        // A distance quad draws only as far beyond the outline as its edge, softness and anti-aliasing reach, which is less than its margin.
        void AddDistanceInk(float left, float bottom, float right, float top, in AtlasGlyph atlas, float xUnit, float yUnit, float size, float threshold, float softness)
        {
            if (!m_TrackInk) return;
            // The quad's margin is the field's reach, Spread em, which is Spread * upem in font units.
            float pad = yUnit > 0 ? atlas.Spread * size / yUnit : 0;
            float inset = pad - (0.5f - threshold + softness) * 2 * pad;
            float ix = inset * xUnit - m_InkMargin, iy = inset * yUnit - m_InkMargin;
            if (ix < 0 || atlas.Pixel || atlas.Kind == AtlasGlyph.Color) ix = 0;
            if (iy < 0 || atlas.Pixel || atlas.Kind == AtlasGlyph.Color) iy = 0;
            AddInk(left + ix, bottom + iy, right - ix, top - iy);
        }

        // Two screen pixels in the text's units, or 0 where a pixel's size is not known.
        float m_QuadMargin;

        /// <summary>Draws every distance quad as its whole tile, for a test that compares the two.</summary>
        internal static bool WholeTilesForTests;

        // A distance quad's tile reaches Spread em beyond the outline, so that any layer fits; a quad that shows less is cut to what it shows,
        // its edge, softness and anti-aliasing, and the GPU shades none of the rest. Its uv keeps the same mapping, so every pixel left reads what it read.
        bool CropToReach(in AtlasGlyph atlas, float xUnit, float yUnit, float size, float threshold, float softness, ref float left, ref float bottom, ref float right, ref float top,
                         ref float u0, ref float v0, ref float u1, ref float v1)
        {
            if (m_QuadMargin <= 0 || atlas.Pixel || atlas.Spread <= 0 || size <= 0 || yUnit <= 0) return false;
            float reach = (0.5f - threshold + softness) * 2 * atlas.Spread;
            float emX = size / yUnit * Mathf.Abs(xUnit);
            return CropSides(atlas, emX, size, reach * emX, reach * size, reach * emX, reach * size, ref left, ref bottom, ref right, ref top, ref u0, ref v0, ref u1, ref v1);
        }

        // The same with a reach of its own on each side, in the text's units, for a quad whose layers lie off center.
        bool CropSides(in AtlasGlyph atlas, float emX, float emY, float reachLeft, float reachBottom, float reachRight, float reachTop, ref float left, ref float bottom, ref float right, ref float top,
                       ref float u0, ref float v0, ref float u1, ref float v1)
        {
            float padX = atlas.Spread * emX, padY = atlas.Spread * emY;
            float l = padX - reachLeft - m_QuadMargin, r = padX - reachRight - m_QuadMargin;
            float b = padY - reachBottom - m_QuadMargin, t = padY - reachTop - m_QuadMargin;
            if (l < 0) l = 0;
            if (r < 0) r = 0;
            if (b < 0) b = 0;
            if (t < 0) t = 0;
            float width = right - left, height = top - bottom;
            bool any = false;
            if ((l > 0 || r > 0) && l + r < width)
            {
                float du = (u1 - u0) / width;
                left += l;
                right -= r;
                u0 += du * l;
                u1 -= du * r;
                any = true;
            }
            if ((b > 0 || t > 0) && b + t < height)
            {
                float dv = (v1 - v0) / height;
                bottom += b;
                top -= t;
                v0 += dv * b;
                v1 -= dv * t;
                any = true;
            }
            return any;
        }

        static readonly ResolvedPaint s_NoPaint = new ResolvedPaint { Blend = LetteraBlend.Normal };

        void BuildMesh()
        {
            VertexCount = IndexCount = 0;
            Segments.Clear();
            m_SegmentBlend = LetteraBlend.Normal;
            m_SegmentTexture = null;
            m_SegmentCustom = null;
            m_SegmentStart = 0;
            m_FilterRow = 0;
            Empty(m_Aside.SolidBoxes);
            m_AllTakeBase = true;
            m_RecordOwners = HasPoseConsumers || m_Aside.RevealSpans.Count > 0 || (m_Aside.Marquee != null && m_Aside.MarqueeScrolling) || NeedsRestState;
            m_Aside.WindowBaked = false;
            if (m_Aside.StageHooks.Count > 0) Empty(m_Aside.CoverQuads);
            m_CurrentOwner = -1;
            m_DrawBase = m_Host.BaseColor;
            m_QuadMargin = m_RectFrozen ? m_FrozenQuadMargin : QuadMargin();
            InkKnown = false;
            m_TrackInk = m_Host is LetteraText inked && inked.TracksInk;
            m_InkQuads = 0;
            m_InkMinX = m_InkMinY = float.MaxValue;
            m_InkMaxX = m_InkMaxY = float.MinValue;
            if (m_TrackInk)
            {
                float rasterScale = HostRasterScale;
                m_InkMargin = rasterScale > 0 ? 1f / rasterScale : 1f;
            }
            GlyphAtlasRegistration.EnsureRegistered();

            if (ShowsBars)
            {
                DrawBars();
                FinishSegment();
                ComputeBounds();
                ApplyBaseColor();
                m_WantsPose = false;
                return;
            }
            List<LayerGroup> groups = m_Attributes.SortedGroups();
            Styled = m_Attributes.NeedsStyledShader() || HasInlineSprites;
            PrepareGlyphs(groups);
            if (m_BatchMeshMissed) return;
            // One quad a glyph is the usual case, so a long text does not grow through every size on the way. A world text's first mesh takes what
            // its glyphs need, four at least: thousands of world labels are often made at once, and room for sixteen glyphs each was garbage enough
            // to start collections in their first frame. A Canvas label keeps room for sixteen, so one made again with more glyphs does not grow.
            int firstRoom = Vertices.Length == 0 && m_Host is LetteraWorldText ? FirstWorldVertices : FirstVertices;
            if (m_PlacedCount * 4 > Vertices.Length) GrowVertices(System.Math.Max(System.Math.Max(m_PlacedCount * 4, Vertices.Length * 2), firstRoom));
            if (m_PlacedCount * 6 > Indices.Length) System.Array.Resize(ref Indices, System.Math.Max(System.Math.Max(m_PlacedCount * 6, Indices.Length * 2), firstRoom * 3 / 2));
            PrepareFade();
            PrepareExtraMotions();

            m_Faces.Clear();
            foreach (LayerGroup group in groups)
                if (group.Band == LetteraLayerBand.Face && group.Layer != LayerKind.Highlight)
                    m_Faces.Add(group);

            DrawHighlights(groups, false);
            LayersMerged = false;
            LayersShared = false;
            if (m_Attributes.PaintOrder == LetteraPaintOrder.GlyphMajor)
            {
                ReserveExtraQuads();
                if (CanMergeLayers(groups)) DrawMerged();
                else DrawGlyphMajor(groups);
                AddDecorationLayer(null);
            }
            else if (CanShareLayers(groups))
            {
                DrawSharedLayers();
            }
            else
            {
                foreach (LayerGroup group in groups)
                    if (group.Band < LetteraLayerBand.Face && group.Layer != LayerKind.Highlight)
                        DrawGroup(group);

                ReserveExtraQuads();
                DrawFaces();
                if (m_BatchMeshMissed) return;
                foreach (LayerGroup group in m_Faces) DrawExtraFills(group);
                AddDecorationLayer(null);

                foreach (LayerGroup group in groups)
                    if (group.Band > LetteraLayerBand.Face && group.Layer != LayerKind.Highlight)
                        DrawGroup(group);
            }

            // Lines with Overlay on, and their layers, go above the glyphs they cross.
            foreach (LayerGroup group in groups) AddDecorationLayer(group, true);
            AddDecorationLayer(null, true);
            DrawHighlights(groups, true);

            DrawInlineSprites();
            DrawSpoilerCovers();
            RunMeshHooks();
            m_CurrentOwner = -1;
            FinishSegment();
            DuplicateForMarquee();
            ComputeBounds();
            ApplyBaseColor();
            if (m_RecordOwners)
            {
                PrepareRestState();
                BuildRevealBodies();
                if (m_WantsPose) RunPosePass();
            }
            else
            {
                m_WantsPose = false;
                BakeWindowOffset();
            }
            InkKnown = m_TrackInk && VertexCount > 0 && m_InkQuads * 4 == VertexCount && !m_RecordOwners && !m_Aside.WindowBaked && m_Aside.StageHooks.Count == 0 &&
                       m_Aside.MotionSpans.Count == 0 && m_Aside.MarqueeCopies <= 1 && !m_Aside.MarqueeScrolling;
            if (!m_BatchWorker) UpdateHubMembership();
        }

        bool m_RecordOwners;

        // The text's colour, read once a mesh: through the host it is a conversion per glyph.
        Color32 m_DrawBase;

        /// <summary>Moves a position through a pixel font's grid, as glyphs and effect offsets go when snapping is on.</summary>
        public static float SnapToGrid(float position, float size, int grid) => grid > 0 && size > 0 ? Mathf.Round(position * grid / size) * size / grid : position;

        static int PixelsPerEm(float size, float rasterScale) => size * rasterScale >= 72f ? 128 : 64;

        // Finds each glyph's reach and range rectangles before anything draws, so a glyph is rasterized once.
        void PrepareGlyphs(List<LayerGroup> groups)
        {
            EnsureSize(ref m_PlacedAtlas, m_PlacedCount);
            EnsureSize(ref m_PlacedReach, m_PlacedCount);
            System.Array.Clear(m_PlacedReach, 0, m_PlacedCount);
            ValueList<LayerRecord> records = m_Attributes.Layers;
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
                    if (reach > GlyphAtlas.MaxReachEm)
                    {
                        WarnReach(group.Key);
                        reach = GlyphAtlas.MaxReachEm;
                    }
                    if (reach > m_PlacedReach[i]) m_PlacedReach[i] = reach;
                    if ((record.Paint.Span == LetteraPaintSpan.Range || record.Kind == LayerKind.Highlight) && !record.Paint.IsSolid)
                    {
                        LetteraLine line = m_Lines[g.Line];
                        Rect grown = Union(record.RangeRect, g.X, line.Baseline - line.Descent, g.X + g.Advance, line.Baseline + line.Ascent);
                        record.RangeRect = grown;
                        records[index] = record;
                    }
                }
            }

            PrepareDecorationRects();

            float rasterScale = HostRasterScale;
            for (int i = 0; i < m_PlacedCount; i++)
            {
                ref PlacedGlyph g = ref m_Placed[i];
                int density = PixelsPerEm(g.Size, rasterScale);
                byte boost = m_Attributes.At(g.Cluster).Boost;
                // A thread takes only what the atlas holds already; anything it would make sends the mesh back to the main thread.
                if (m_BatchWorker)
                {
                    if (boost > 0 || g.Special != 0 || m_SolidsOnly ||
                        !GlyphAtlas.Peek(g.Instance, g.Glyph, density, m_PlacedReach[i], m_Host.Data.RenderMode == LetteraRenderMode.MSDF, out m_PlacedAtlas[i]))
                    {
                        m_BatchMeshMissed = true;
                        return;
                    }
                    continue;
                }
                if (boost > 0) density = GlyphAtlas.Boost(g.Instance, g.Glyph, density << boost);
                if (g.Special == SpecialRule)
                {
                    MathSpecial rule = m_Aside.MathSpecials[g.SpecialIndex];
                    m_PlacedAtlas[i] = m_SolidsOnly ? default : GlyphAtlas.GetRule(rule.Thickness, rule.Vertical, density, m_PlacedReach[i], m_Host.Data.RenderMode == LetteraRenderMode.MSDF);
                    continue;
                }
                if (m_SolidsOnly)
                {
                    // An outline becomes a solid, so it needs no tile. Only a color glyph goes on being drawn this way.
                    AtlasGlyph flat = g.Instance.Face.IsColor ? GlyphAtlas.Get(g.Instance, g.Glyph, density, 0, false) : default;
                    m_PlacedAtlas[i] = flat.Kind == AtlasGlyph.Color ? flat : default;
                    continue;
                }
                m_PlacedAtlas[i] = GlyphAtlas.Get(g.Instance, g.Glyph, density, m_PlacedReach[i], m_Host.Data.RenderMode == LetteraRenderMode.MSDF);
            }

            if (m_Attributes.HasUnion) PrepareUnions(rasterScale);

            // A halo around a color glyph blurs its alpha, which only the styled shader does; so does a line's glow.
            if (Styled) return;
            foreach (LayerGroup group in groups)
            {
                if (group.Layer != LayerKind.Glow) continue;
                if (m_Attributes.Decorations.Count > 0)
                {
                    Styled = true;
                    return;
                }
                for (int i = 0; i < m_PlacedCount; i++)
                {
                    if (m_PlacedAtlas[i].Kind != AtlasGlyph.Color || group.Cover[m_Placed[i].Cluster] == 0) continue;
                    Styled = true;
                    return;
                }
            }
        }

        // Glyphs in a union range whose ink overlaps the next one's merge into one atlas entry, drawn by the first of them.
        unsafe void PrepareUnions(float rasterScale)
        {
            int* first = stackalloc int[4];
            int* second = stackalloc int[4];
            bool multi = m_Host.Data.RenderMode == LetteraRenderMode.MSDF;
            int i = 0;
            while (i < m_RealCount)
            {
                ref PlacedGlyph g = ref m_Placed[i];
                if (!m_Attributes.At(g.Cluster).Union || !m_PlacedAtlas[i].HasDistance || m_PlacedAtlas[i].Pixel)
                {
                    i++;
                    continue;
                }
                int end = i + 1;
                float inkRight = float.MinValue;
                if (LetteraNative.lt_glyph_extents(g.Instance.Font, g.Glyph, first) != 0) inkRight = g.X + (first[0] + first[2]) * g.Unit;
                while (end < m_RealCount)
                {
                    ref PlacedGlyph next = ref m_Placed[end];
                    if (next.Line != g.Line || next.Instance != g.Instance || next.Size != g.Size || !m_Attributes.At(next.Cluster).Union || !m_PlacedAtlas[end].HasDistance) break;
                    if (LetteraNative.lt_glyph_extents(next.Instance.Font, next.Glyph, second) == 0 || second[2] == 0) break;
                    float inkLeft = next.X + second[0] * next.Unit;
                    if (inkLeft > inkRight + 0.01f * next.Size) break;
                    inkRight = Mathf.Max(inkRight, next.X + (second[0] + second[2]) * next.Unit);
                    end++;
                }
                if (end - i < 2)
                {
                    i = end;
                    continue;
                }
                GlyphAtlas.BeginUnion();
                float reach = 0;
                for (int k = i; k < end; k++)
                {
                    GlyphAtlas.AddUnionPart(m_Placed[k].Glyph, (m_Placed[k].X - g.X) / g.Unit, (m_Placed[k].Y - g.Y) / g.Unit);
                    reach = Mathf.Max(reach, m_PlacedReach[k]);
                }
                AtlasGlyph merged = GlyphAtlas.GetUnion(g.Instance, PixelsPerEm(g.Size, rasterScale), reach, multi);
                if (merged.Kind != AtlasGlyph.Empty)
                {
                    m_PlacedAtlas[i] = merged;
                    for (int k = i + 1; k < end; k++) m_PlacedAtlas[k] = default;
                }
                i = end;
            }
        }

        static readonly System.Runtime.CompilerServices.ConditionalWeakTable<object, object> s_ReachWarned = new System.Runtime.CompilerServices.ConditionalWeakTable<object, object>();

        static void WarnReach(object effect)
        {
            if (effect == null || s_ReachWarned.TryGetValue(effect, out _)) return;
            s_ReachWarned.Add(effect, effect);
            Debug.LogWarning($"Lettera: a {effect.GetType().Name} reaches farther than {GlyphAtlas.MaxReachEm} em from the glyph, so it is cut there.");
        }

        void PrepareDecorationRects()
        {
            ValueList<LetteraAttributes.DecorationSpec> decorations = m_Attributes.Decorations;
            if (decorations.Count == 0) return;
            for (int d = 0; d < decorations.Count; d++)
            {
                LetteraAttributes.DecorationSpec spec = decorations[d];
                spec.RangeRect = new Rect(float.MaxValue, float.MaxValue, 0, 0);
                decorations[d] = spec;
            }
            for (int i = 0; i < m_RealCount; i++)
            {
                ref PlacedGlyph g = ref m_Placed[i];
                ref LetteraAttributes.Cp attr = ref m_Attributes.At(g.Cluster);
                LetteraLine line = m_Lines[g.Line];
                for (int pass = 0; pass < 2; pass++)
                {
                    int index = (pass == 0 ? attr.Underline : attr.Strike) - 1;
                    if (index < 0 || decorations[index].Paint.IsSolid) continue;
                    LetteraAttributes.DecorationSpec spec = decorations[index];
                    spec.RangeRect = Union(spec.RangeRect, g.X, line.Baseline - line.Descent, g.X + g.Advance, line.Baseline + line.Ascent);
                    decorations[index] = spec;
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
            ValueList<LayerRecord> records = m_Attributes.Layers;
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
            if (PlainFaces()) DrawPlainFaces();
            else if (m_BatchWorker) m_BatchMeshMissed = true;
            else
                for (int i = 0; i < m_PlacedCount; i++) DrawFace(i);
        }

        // Faces with nothing on them but a colour: no fill to replace them, no filter, warp, paint, fade or motion. Most text is this.
        bool PlainFaces() => m_Faces.Count == 0 && m_Attributes.Filters.Count == 0 && m_Attributes.Warps.Count == 0 && !Styled && !m_FadeActive && !m_TrackBase &&
                             m_Aside.MotionSpans.Count == 0;

        // What DrawFace and AddQuad write for such a face, in one loop with nothing asked per glyph that the text answers once.
        // A formula's part, a colour glyph and a turned glyph take the usual way.
        [Unity.IL2CPP.CompilerServices.Il2CppSetOption(Unity.IL2CPP.CompilerServices.Option.ArrayBoundsChecks, false), Unity.IL2CPP.CompilerServices.Il2CppSetOption(Unity.IL2CPP.CompilerServices.Option.NullChecks, false)]
        void DrawPlainFaces()
        {
            UseSegment(s_NoPaint);
            m_FilterRow = 0;
            Color32 baseColor = m_DrawBase;
            var noColor = new Color32(baseColor.r, baseColor.g, baseColor.b, 255);
            PlacedGlyph[] placed = m_Placed;
            AtlasGlyph[] atlases = m_PlacedAtlas;
            for (int i = 0; i < m_PlacedCount; i++)
            {
                ref PlacedGlyph g = ref placed[i];
                ref AtlasGlyph atlas = ref atlases[i];
                if (atlas.Kind == AtlasGlyph.Empty) continue;
                if (g.Special != 0 || g.Angle != 0 || atlas.Kind == AtlasGlyph.Color)
                {
                    if (m_BatchWorker)
                    {
                        m_BatchMeshMissed = true;
                        return;
                    }
                    DrawFace(i);
                    continue;
                }
                m_CurrentOwner = i;
                ref LetteraAttributes.Cp attr = ref m_Attributes.At(g.Cluster);
                Color32 color = attr.HasColor ? attr.Color : noColor;
                color.a = (byte)(color.a * attr.Alpha);
                m_AllTakeBase &= !attr.HasColor;
                float mode = atlas.Pixel ? ModePixel : atlas.Kind == AtlasGlyph.Msdf ? ModeMedian : ModeSdf;
                float xUnit = g.Unit * (1 + g.XScale);
                float left = g.X + atlas.Left * xUnit, right = g.X + atlas.Right * xUnit;
                float bottom = g.Y + atlas.Bottom * g.Unit, top = g.Y + atlas.Top * g.Unit;
                float u0 = atlas.U0, v0 = atlas.V0, u1 = atlas.U1, v1 = atlas.V1;
                bool cropped = mode == ModeSdf && CropToReach(atlas, xUnit, g.Unit, g.Size, 0.5f, 0, ref left, ref bottom, ref right, ref top, ref u0, ref v0, ref u1, ref v1);
                int v = Reserve();
                Vertices[v] = new Vector3(left, bottom);
                Vertices[v + 1] = new Vector3(left, top);
                Vertices[v + 2] = new Vector3(right, top);
                Vertices[v + 3] = new Vector3(right, bottom);
                Uv0[v] = new Vector4(u0, v0, atlas.Page, mode);
                Uv0[v + 1] = new Vector4(u0, v1, atlas.Page, mode);
                Uv0[v + 2] = new Vector4(u1, v1, atlas.Page, mode);
                Uv0[v + 3] = new Vector4(u1, v0, atlas.Page, mode);
                var parameters = new Vector4(0.5f, 0, 0, 0);
                Uv1[v] = parameters;
                Uv1[v + 1] = parameters;
                Uv1[v + 2] = parameters;
                Uv1[v + 3] = parameters;
                Colors[v] = color;
                Colors[v + 1] = color;
                Colors[v + 2] = color;
                Colors[v + 3] = color;
                m_OwnAlpha[v] = m_OwnAlpha[v + 1] = m_OwnAlpha[v + 2] = m_OwnAlpha[v + 3] = color.a;
                if (!m_TrackInk) continue;
                if (cropped) AddInk(left, bottom, right, top);
                else AddDistanceInk(left, bottom, right, top, atlas, xUnit, g.Unit, g.Size, 0.5f, 0);
            }
        }

        // Called per glyph, over indices the same pass has just sized; the tests run these with every check on.
        [Unity.IL2CPP.CompilerServices.Il2CppSetOption(Unity.IL2CPP.CompilerServices.Option.ArrayBoundsChecks, false), Unity.IL2CPP.CompilerServices.Il2CppSetOption(Unity.IL2CPP.CompilerServices.Option.NullChecks, false)]
        void DrawFace(int i)
        {
            m_CurrentOwner = i;
            ref PlacedGlyph g = ref m_Placed[i];
            ref AtlasGlyph atlas = ref m_PlacedAtlas[i];
            if (atlas.Kind == AtlasGlyph.Empty) return;
            if (m_Aside.FaceQuad.Length > i && m_Aside.MotionSpans.Count > 0) m_Aside.FaceQuad[i] = VertexCount / 4;
            if (FirstFill(g.Cluster, atlas.Kind, out int record))
            {
                DrawLayer(i, m_Attributes.Layers[record]);
                return;
            }
            ref LetteraAttributes.Cp attr = ref m_Attributes.At(g.Cluster);
            Color32 baseColor = m_DrawBase;
            UseSegment(s_NoPaint);
            // The face lies below every filter in the list.
            m_FilterRow = FilterFor(g.Cluster, -1);
            if (atlas.Kind == AtlasGlyph.Color)
            {
                // Color glyphs keep their own colors; only alpha applies.
                AddQuad(g, atlas, new Color32(255, 255, 255, (byte)(attr.Alpha * 255f)), ModeColor, 0.5f, 0, 0, s_NoPaint, default, 0, 0);
            }
            else
            {
                Color32 color = attr.HasColor ? attr.Color : new Color32(baseColor.r, baseColor.g, baseColor.b, 255);
                color.a = (byte)(color.a * attr.Alpha);
                m_QuadTakesBase = !attr.HasColor;
                AddQuad(g, atlas, color, atlas.Pixel ? ModePixel : atlas.Kind == AtlasGlyph.Msdf ? ModeMedian : ModeSdf, 0.5f, 0, 0, s_NoPaint, default, 0, 0, WarpOf(g, atlas));
                m_QuadTakesBase = false;
            }
        }

        // Every layer of a glyph before the next glyph's. Texture paints and custom materials still split the text into segments, in this order.
        void DrawGlyphMajor(List<LayerGroup> groups)
        {
            ValueList<LayerRecord> records = m_Attributes.Layers;
            bool reverse = m_Attributes.PaintReverse;
            int at = reverse ? m_PlacedCount - 1 : 0;
            int count = 0;
            while (count < m_PlacedCount)
            {
                // A cursively connected stretch composes as one unit, so its joins stay clean either way.
                int stretch = 1;
                if (reverse)
                    while (at - stretch >= 0 && IsConnected(m_Placed[at - stretch].Cluster, m_Placed[at - stretch + 1].Cluster)) stretch++;
                int first = reverse ? at - stretch + 1 : at;
                for (int n = 0; n < stretch; n++)
                {
                    int i = first + n;
                    int cluster = m_Placed[i].Cluster;
                    foreach (LayerGroup group in groups)
                    {
                        if (group.Band >= LetteraLayerBand.Face || group.Layer == LayerKind.Highlight) continue;
                        int index = group.Cover[cluster] - 1;
                        if (index >= 0) DrawLayer(i, records[index]);
                    }
                    DrawFace(i);
                    foreach (LayerGroup group in m_Faces)
                    {
                        int index = group.Cover[cluster] - 1;
                        if (index < 0) continue;
                        if (FirstFill(cluster, m_PlacedAtlas[i].Kind, out int firstFill) && firstFill == index) continue;
                        DrawLayer(i, records[index]);
                    }
                    foreach (LayerGroup group in groups)
                    {
                        if (group.Band <= LetteraLayerBand.Face || group.Layer == LayerKind.Highlight) continue;
                        int index = group.Cover[cluster] - 1;
                        if (index >= 0) DrawLayer(i, records[index]);
                    }
                }
                count += stretch;
                at += reverse ? -stretch : stretch;
            }
        }

        // ---------------------------------------------------------------- merged layers

        // Glyph-major Canvas text with a stroke and a shadow draws each glyph as one quad: the shader composes face over stroke over shadow, as the
        // glyph's three quads blended in that order would. Only where that holds: plain distance-field glyphs, solid paints, normal blend, a stroke
        // wholly outside, a shadow that lies inside the glyph's quad and reads nothing of its neighbours in the atlas, and nothing else on the text.
        // Layer order is left apart: there a glyph's face cannot join its layers, which its neighbours' layers lie between, and joining only its
        // stroke and shadow saves a third of the quads while every quad then carries two more streams, which cost the GPU more than they saved.

        /// <summary>The last mesh drew its layers merged.</summary>
        internal bool LayersMerged { get; private set; }

        /// <summary>Draws layers as separate quads, for a test that compares the two.</summary>
        internal static bool NoMergedLayersForTests;

        /// <summary>Why the last mesh drew its layers apart, for tests.</summary>
        internal string LayersRefused { get; private set; }

        /// <summary>The glyph that made it so, or -1.</summary>
        internal int LayersRefusedAt { get; private set; }

        bool Refuse(string why, int at = -1)
        {
            LayersRefused = why;
            LayersRefusedAt = at;
            return false;
        }

        bool CanMergeLayers(List<LayerGroup> groups)
        {
            if (NoMergedLayersForTests || !(m_Host is LetteraText host) || !host.UsesDefaultMaterial || m_Faces.Count > 0 || m_Attributes.Filters.Count > 0 || m_Attributes.Warps.Count > 0 || m_Attributes.Decorations.Count > 0 ||
                m_FadeActive || m_RecordOwners || m_Aside.MotionSpans.Count > 0 || m_Aside.StageHooks.Count > 0 || HasInlineSprites || m_Host.Data.RenderMode == LetteraRenderMode.MSDF ||
                m_PlacedCount == 0)
                return Refuse("text");
            m_Aside.MergeStroke = m_Aside.MergeShadow = null;
            foreach (LayerGroup group in groups)
            {
                if (group.Layer == LayerKind.Stroke && group.Band == LetteraLayerBand.Rim && m_Aside.MergeStroke == null) m_Aside.MergeStroke = group;
                else if (group.Layer == LayerKind.Shadow && group.Band == LetteraLayerBand.Back && m_Aside.MergeShadow == null) m_Aside.MergeShadow = group;
                else return Refuse("layer");
            }
            if (m_Aside.MergeStroke == null && m_Aside.MergeShadow == null) return Refuse("none");
            ValueList<LayerRecord> records = m_Attributes.Layers;
            for (int i = 0; i < m_PlacedCount; i++)
            {
                ref PlacedGlyph g = ref m_Placed[i];
                ref AtlasGlyph atlas = ref m_PlacedAtlas[i];
                if (atlas.Kind == AtlasGlyph.Empty) continue;
                if (atlas.Kind != AtlasGlyph.Sdf || atlas.Pixel || atlas.Spread <= 0 || g.Special != 0 || g.Angle != 0 || g.Size <= 0) return Refuse("glyph", i);
                if (i > 0 && IsConnected(m_Placed[i - 1].Cluster, g.Cluster)) return Refuse("joined");
                int stroke = m_Aside.MergeStroke != null ? m_Aside.MergeStroke.Cover[g.Cluster] - 1 : -1;
                if (stroke >= 0 && (!MergeablePaint(records[stroke]) || records[stroke].Align != 1)) return Refuse("stroke", i);
                int shadow = m_Aside.MergeShadow != null ? m_Aside.MergeShadow.Cover[g.Cluster] - 1 : -1;
                if (shadow >= 0 && (!MergeablePaint(records[shadow]) || !ShadowFits(g, atlas, records[shadow]))) return Refuse("shadow", i);
            }
            LayersRefused = null;
            Styled = true;
            LayersMerged = true;
            return true;
        }

        static bool MergeablePaint(in LayerRecord record) =>
            record.Paint.IsSolid && record.Custom == null && (record.Paint.Blend == LetteraBlend.Normal || record.Paint.Blend == LetteraBlend.Inherit);

        // What the shadow shows lies inside the glyph's quad, and where it reads past the tile's edge every neighbour is too far from its outline to show.
        bool ShadowFits(in PlacedGlyph g, in AtlasGlyph atlas, in LayerRecord record)
        {
            float size = MeasureSize(record, g);
            float toEm = 1f / g.Size, toSdf = 1f / (2f * atlas.Spread);
            float offset = Mathf.Max(Mathf.Abs(SnappedOffset(g, record.OffsetX.Of(size))), Mathf.Abs(SnappedOffset(g, record.OffsetY.Of(size)))) * toEm;
            float threshold = Mathf.Max(0.002f, 0.5f - record.Dilate.Of(size) * toEm * toSdf);
            float softness = record.Softness * SoftnessEm * toSdf;
            return offset + (0.5f - threshold + softness) * 2 * atlas.Spread <= atlas.Spread && offset * toSdf < threshold - softness - 0.25f;
        }

        // Two bytes in one float, exact through interpolation: the shader rounds it back.
        static float Pack(byte high, byte low) => high + low / 256f;

        Color32 LayerColor(in LayerRecord record, in LetteraAttributes.Cp attr)
        {
            Color32 color = record.Paint.Color;
            if (record.Paint.UsesGlyphColor)
            {
                Color32 baseColor = m_DrawBase;
                color = attr.HasColor ? attr.Color : new Color32(baseColor.r, baseColor.g, baseColor.b, 255);
            }
            color.a = (byte)(color.a * attr.Alpha);
            return color;
        }

        // uv1: face red and green, stroke edge (0 none), shadow edge (0 none), shadow softness. uv2: stroke and shadow colors.
        // uv3: the shadow's offset in the atlas, the stroke's softness, face blue and alpha. Two bytes a float.
        // The vertex color stays white, so what a canvas multiplies into it, a tint or an alpha, reaches face and layers alike.
        void DrawMerged()
        {
            ValueList<LayerRecord> records = m_Attributes.Layers;
            UseSegment(s_NoPaint);
            m_FilterRow = 0;
            bool reverse = m_Attributes.PaintReverse;
            Color32 baseColor = m_DrawBase;
            var white = new Color32(255, 255, 255, 255);
            for (int n = 0; n < m_PlacedCount; n++)
            {
                int i = reverse ? m_PlacedCount - 1 - n : n;
                ref PlacedGlyph g = ref m_Placed[i];
                ref AtlasGlyph atlas = ref m_PlacedAtlas[i];
                if (atlas.Kind == AtlasGlyph.Empty) continue;
                m_CurrentOwner = i;
                ref LetteraAttributes.Cp attr = ref m_Attributes.At(g.Cluster);
                Color32 face = attr.HasColor ? attr.Color : new Color32(baseColor.r, baseColor.g, baseColor.b, 255);
                face.a = (byte)(face.a * attr.Alpha);
                float toEm = 1f / g.Size, toSdf = 1f / (2f * atlas.Spread);
                float xUnit = g.Unit * (1 + g.XScale);
                float left = g.X + atlas.Left * xUnit, right = g.X + atlas.Right * xUnit;
                float bottom = g.Y + atlas.Bottom * g.Unit, top = g.Y + atlas.Top * g.Unit;
                var parameters = new Vector4(Pack(face.r, face.g), 0, 0, 0);
                var colors = new Vector4();
                var extra = new Vector4(0, 0, 0, Pack(face.b, face.a));
                float offsetX = 0, offsetY = 0;
                int stroke = m_Aside.MergeStroke != null ? m_Aside.MergeStroke.Cover[g.Cluster] - 1 : -1;
                if (stroke >= 0)
                {
                    LayerRecord record = records[stroke];
                    float width = record.Width.Of(MeasureSize(record, g)) * toEm;
                    if (width > 0)
                    {
                        Color32 c = LayerColor(record, attr);
                        parameters.y = Mathf.Max(0.002f, 0.5f - width * toSdf);
                        extra.z = record.Softness * SoftnessEm * toSdf;
                        colors.x = Pack(c.r, c.g);
                        colors.y = Pack(c.b, c.a);
                    }
                }
                int shadow = m_Aside.MergeShadow != null ? m_Aside.MergeShadow.Cover[g.Cluster] - 1 : -1;
                if (shadow >= 0)
                {
                    LayerRecord record = records[shadow];
                    float size = MeasureSize(record, g);
                    Color32 c = LayerColor(record, attr);
                    parameters.z = Mathf.Max(0.002f, 0.5f - record.Dilate.Of(size) * toEm * toSdf);
                    parameters.w = record.Softness * SoftnessEm * toSdf;
                    colors.z = Pack(c.r, c.g);
                    colors.w = Pack(c.b, c.a);
                    offsetX = SnappedOffset(g, record.OffsetX.Of(size));
                    offsetY = SnappedOffset(g, record.OffsetY.Of(size));
                    extra.x = offsetX / (right - left) * (atlas.U1 - atlas.U0);
                    extra.y = offsetY / (top - bottom) * (atlas.V1 - atlas.V0);
                }
                float u0 = atlas.U0, v0 = atlas.V0, u1 = atlas.U1, v1 = atlas.V1;
                if (m_QuadMargin > 0)
                {
                    float emX = g.Size / g.Unit * Mathf.Abs(xUnit), emY = g.Size;
                    float strokeReach = parameters.y > 0 ? (0.5f - parameters.y + extra.z) * 2 * atlas.Spread : 0;
                    float sx = strokeReach * emX, sy = strokeReach * emY;
                    float rl = sx, rr = sx, rb = sy, rt = sy;
                    if (parameters.z > 0)
                    {
                        float shadowReach = (0.5f - parameters.z + parameters.w) * 2 * atlas.Spread;
                        rl = Mathf.Max(rl, shadowReach * emX - offsetX);
                        rr = Mathf.Max(rr, shadowReach * emX + offsetX);
                        rb = Mathf.Max(rb, shadowReach * emY - offsetY);
                        rt = Mathf.Max(rt, shadowReach * emY + offsetY);
                    }
                    CropSides(atlas, emX, emY, rl, rb, rr, rt, ref left, ref bottom, ref right, ref top, ref u0, ref v0, ref u1, ref v1);
                }
                int v = Reserve();
                Vertices[v] = new Vector3(left, bottom);
                Vertices[v + 1] = new Vector3(left, top);
                Vertices[v + 2] = new Vector3(right, top);
                Vertices[v + 3] = new Vector3(right, bottom);
                Uv0[v] = new Vector4(u0, v0, atlas.Page, ModeMerged);
                Uv0[v + 1] = new Vector4(u0, v1, atlas.Page, ModeMerged);
                Uv0[v + 2] = new Vector4(u1, v1, atlas.Page, ModeMerged);
                Uv0[v + 3] = new Vector4(u1, v0, atlas.Page, ModeMerged);
                Uv1[v] = Uv1[v + 1] = Uv1[v + 2] = Uv1[v + 3] = parameters;
                Uv2[v] = Uv2[v + 1] = Uv2[v + 2] = Uv2[v + 3] = colors;
                Uv3[v] = Uv3[v + 1] = Uv3[v + 2] = Uv3[v + 3] = extra;
                m_QuadTakesBase = false;
                SetColor(v, white);
                AddInk(left, bottom, right, top);
            }
        }

        // ---------------------------------------------------------------- shared layers

        // Layer-order Canvas text with a stroke and a shadow keeps its order, every shadow under every stroke under every face, and stores each glyph once:
        // four vertices, which the index buffer names three times, for the shadows, the strokes and the faces. A triangle's flat attributes come from
        // its provoking vertex, so each pass leads with a corner of its own and reads that corner's colour, edge, softness and shift. The quad covers all
        // three layers, and the shadow pass reads the field where the shadow's own quad would have read it. A frame then moves a third of the vertices.

        /// <summary>The last mesh drew its layers from one set of vertices a glyph.</summary>
        internal bool LayersShared { get; private set; }

        /// <summary>Draws layer order as separate quads, for a test that compares the two.</summary>
        internal static bool NoSharedLayersForTests;

        // Direct3D, Vulkan and Metal take flat attributes from a triangle's first vertex, OpenGL ES and WebGL from its last.
        // Desktop OpenGL can be set either way, so there the quads stay apart.
        static int s_Provoking = -1;

        static int Provoking
        {
            get
            {
                if (s_Provoking < 0)
                {
                    UnityEngine.Rendering.GraphicsDeviceType device = SystemInfo.graphicsDeviceType;
                    s_Provoking = device == UnityEngine.Rendering.GraphicsDeviceType.OpenGLES3 ? 1 : device == UnityEngine.Rendering.GraphicsDeviceType.OpenGLCore ? 2 : 0;
                }
                return s_Provoking;
            }
        }

        bool CanShareLayers(List<LayerGroup> groups)
        {
            if (NoSharedLayersForTests || Styled || !(m_Host is LetteraText host) || !host.UsesDefaultMaterial || m_Faces.Count > 0 || m_Attributes.Filters.Count > 0 ||
                m_Attributes.Warps.Count > 0 || m_Attributes.Decorations.Count > 0 || m_FadeActive || m_RecordOwners || m_Aside.MotionSpans.Count > 0 ||
                m_Aside.StageHooks.Count > 0 || HasInlineSprites || HasSpoilers || m_Aside.Marquee != null || m_Host.Data.RenderMode == LetteraRenderMode.MSDF ||
                m_PlacedCount == 0 || m_PlacedCount * 4 > LetteraText.MaxPartVertices || groups.Count == 0 || Provoking == 2)
                return false;
            m_Aside.MergeStroke = m_Aside.MergeShadow = null;
            foreach (LayerGroup group in groups)
            {
                if (group.Layer == LayerKind.Stroke && group.Band == LetteraLayerBand.Rim && m_Aside.MergeStroke == null) m_Aside.MergeStroke = group;
                else if (group.Layer == LayerKind.Shadow && group.Band == LetteraLayerBand.Back && m_Aside.MergeShadow == null) m_Aside.MergeShadow = group;
                else return false;
            }
            List<LayerRecord> records = m_Attributes.Layers;
            for (int i = 0; i < m_PlacedCount; i++)
            {
                ref PlacedGlyph g = ref m_Placed[i];
                ref AtlasGlyph atlas = ref m_PlacedAtlas[i];
                if (atlas.Kind == AtlasGlyph.Empty) continue;
                if (atlas.Kind != AtlasGlyph.Sdf || atlas.Pixel || atlas.Spread <= 0 || g.Special != 0 || g.Angle != 0 || g.Size <= 0) return false;
                int stroke = m_Aside.MergeStroke != null ? m_Aside.MergeStroke.Cover[g.Cluster] - 1 : -1;
                if (stroke >= 0 && (!MergeablePaint(records[stroke]) || records[stroke].Align != 1)) return false;
                int shadow = m_Aside.MergeShadow != null ? m_Aside.MergeShadow.Cover[g.Cluster] - 1 : -1;
                if (shadow >= 0 && (!MergeablePaint(records[shadow]) || !ShadowFits(g, atlas, records[shadow]))) return false;
            }
            return true;
        }

        // Each pass's two triangles over the quad, both led by that pass's corner: 0 the shadow's, 1 the stroke's, 2 the face's.
        static readonly int[] s_FirstProvokes = { 0, 1, 2, 0, 2, 3, 1, 2, 3, 1, 3, 0, 2, 3, 0, 2, 0, 1 };
        static readonly int[] s_LastProvokes = { 1, 2, 0, 2, 3, 0, 2, 3, 1, 3, 0, 1, 3, 0, 2, 0, 1, 2 };

        // uv1 per corner: edge, softness, and for the shadow its shift in the atlas. An edge of 1 marks a layer this glyph does not have.
        void DrawSharedLayers()
        {
            List<LayerRecord> records = m_Attributes.Layers;
            UseSegment(s_NoPaint);
            m_FilterRow = 0;
            Color32 baseColor = m_DrawBase;
            var noColor = new Color32(baseColor.r, baseColor.g, baseColor.b, 255);
            int first = VertexCount;
            int quads = 0;
            for (int i = 0; i < m_PlacedCount; i++)
                if (m_PlacedAtlas[i].Kind != AtlasGlyph.Empty) quads++;
            if (first + quads * 4 > Vertices.Length) GrowVertices(System.Math.Max(first + quads * 4, Vertices.Length * 2));
            if (IndexCount + quads * 18 > Indices.Length) System.Array.Resize(ref Indices, System.Math.Max(IndexCount + quads * 18, Indices.Length * 2));
            if (m_TrackBase && m_TakesBase.Length < Vertices.Length) System.Array.Resize(ref m_TakesBase, Vertices.Length);
            var none = new Vector4(1, 0, 0, 0);
            var faceParams = new Vector4(0.5f, 0, 0, 0);
            int strokes = 0, shadows = 0;
            int v = first;
            for (int i = 0; i < m_PlacedCount; i++)
            {
                ref PlacedGlyph g = ref m_Placed[i];
                ref AtlasGlyph atlas = ref m_PlacedAtlas[i];
                if (atlas.Kind == AtlasGlyph.Empty) continue;
                m_CurrentOwner = i;
                ref LetteraAttributes.Cp attr = ref m_Attributes.At(g.Cluster);
                Color32 face = attr.HasColor ? attr.Color : noColor;
                face.a = (byte)(face.a * attr.Alpha);
                float toEm = 1f / g.Size, toSdf = 1f / (2f * atlas.Spread);
                float xUnit = g.Unit * (1 + g.XScale);
                float left = g.X + atlas.Left * xUnit, right = g.X + atlas.Right * xUnit;
                float bottom = g.Y + atlas.Bottom * g.Unit, top = g.Y + atlas.Top * g.Unit;
                Vector4 shadowParams = none, strokeParams = none;
                Color32 shadowColor = default, strokeColor = default;
                float offsetX = 0, offsetY = 0, strokeEdge = 0, shadowEdge = 0, shadowSoft = 0, strokeSoft = 0;
                int stroke = m_Aside.MergeStroke != null ? m_Aside.MergeStroke.Cover[g.Cluster] - 1 : -1;
                if (stroke >= 0)
                {
                    LayerRecord record = records[stroke];
                    float width = record.Width.Of(MeasureSize(record, g)) * toEm;
                    if (width > 0)
                    {
                        strokeColor = LayerColor(record, attr);
                        strokeEdge = Mathf.Max(0.002f, 0.5f - width * toSdf);
                        strokeSoft = record.Softness * SoftnessEm * toSdf;
                        strokeParams = new Vector4(strokeEdge, strokeSoft, 0, 0);
                        strokes++;
                    }
                }
                int shadow = m_Aside.MergeShadow != null ? m_Aside.MergeShadow.Cover[g.Cluster] - 1 : -1;
                if (shadow >= 0)
                {
                    LayerRecord record = records[shadow];
                    float size = MeasureSize(record, g);
                    shadowColor = LayerColor(record, attr);
                    shadowEdge = Mathf.Max(0.002f, 0.5f - record.Dilate.Of(size) * toEm * toSdf);
                    shadowSoft = record.Softness * SoftnessEm * toSdf;
                    offsetX = SnappedOffset(g, record.OffsetX.Of(size));
                    offsetY = SnappedOffset(g, record.OffsetY.Of(size));
                    shadowParams = new Vector4(shadowEdge, shadowSoft, offsetX / (right - left) * (atlas.U1 - atlas.U0), offsetY / (top - bottom) * (atlas.V1 - atlas.V0));
                    shadows++;
                }
                float u0 = atlas.U0, v0 = atlas.V0, u1 = atlas.U1, v1 = atlas.V1;
                if (m_QuadMargin > 0)
                {
                    float emX = g.Size / g.Unit * Mathf.Abs(xUnit), emY = g.Size;
                    float strokeReach = strokeEdge > 0 ? (0.5f - strokeEdge + strokeSoft) * 2 * atlas.Spread : 0;
                    float sx = strokeReach * emX, sy = strokeReach * emY;
                    float rl = sx, rr = sx, rb = sy, rt = sy;
                    if (shadowEdge > 0)
                    {
                        float shadowReach = (0.5f - shadowEdge + shadowSoft) * 2 * atlas.Spread;
                        rl = Mathf.Max(rl, shadowReach * emX - offsetX);
                        rr = Mathf.Max(rr, shadowReach * emX + offsetX);
                        rb = Mathf.Max(rb, shadowReach * emY - offsetY);
                        rt = Mathf.Max(rt, shadowReach * emY + offsetY);
                    }
                    CropSides(atlas, emX, emY, rl, rb, rr, rt, ref left, ref bottom, ref right, ref top, ref u0, ref v0, ref u1, ref v1);
                }
                Vertices[v] = new Vector3(left, bottom);
                Vertices[v + 1] = new Vector3(left, top);
                Vertices[v + 2] = new Vector3(right, top);
                Vertices[v + 3] = new Vector3(right, bottom);
                Uv0[v] = new Vector4(u0, v0, atlas.Page, ModeSdf);
                Uv0[v + 1] = new Vector4(u0, v1, atlas.Page, ModeSdf);
                Uv0[v + 2] = new Vector4(u1, v1, atlas.Page, ModeSdf);
                Uv0[v + 3] = new Vector4(u1, v0, atlas.Page, ModeSdf);
                Uv1[v] = shadowParams;
                Uv1[v + 1] = strokeParams;
                Uv1[v + 2] = faceParams;
                Uv1[v + 3] = faceParams;
                SetCorner(v, shadowColor, false);
                SetCorner(v + 1, strokeColor, false);
                SetCorner(v + 2, face, !attr.HasColor);
                SetCorner(v + 3, face, !attr.HasColor);
                AddInk(left, bottom, right, top);
                v += 4;
            }
            VertexCount = v;
            int[] pattern = Provoking == 1 ? s_LastProvokes : s_FirstProvokes;
            int index = IndexCount;
            for (int pass = 0; pass < 3; pass++)
            {
                if ((pass == 0 && shadows == 0) || (pass == 1 && strokes == 0)) continue;
                for (int q = first; q < v; q += 4)
                {
                    // A glyph outside a layer's range is left out of that pass.
                    if (pass < 2 && Uv1[q + pass].x >= 1) continue;
                    for (int k = 0; k < 6; k++) Indices[index + k] = q + pattern[pass * 6 + k];
                    index += 6;
                }
            }
            IndexCount = index;
            LayersShared = true;
        }

        void SetCorner(int v, Color32 color, bool takesBase)
        {
            Colors[v] = color;
            m_OwnAlpha[v] = color.a;
            m_AllTakeBase = false;
            if (m_TrackBase) m_TakesBase[v] = takesBase;
        }

        bool IsConnected(int a, int b)
        {
            if (a < 0 || b < 0 || a >= m_TextLength || b >= m_TextLength) return false;
            return IsCursive(m_Scripts[a]) && IsCursive(m_Scripts[b]) && !LetteraUnicode.IsWhiteSpace(m_Text[a]) && !LetteraUnicode.IsWhiteSpace(m_Text[b]);
        }

        static readonly float[] s_FilterA = new float[20], s_FilterB = new float[20];

        // The filters of later styles that cover a codepoint, composed in style order; an inner range after an outer one.
        float FilterFor(int cluster, int style)
        {
            ValueList<LetteraAttributes.FilterRange> filters = m_Attributes.Filters;
            if (filters.Count == 0) return 0;
            bool any = false;
            for (int f = 0; f < filters.Count; f++)
            {
                LetteraAttributes.FilterRange filter = filters[f];
                if (filter.Style <= style || cluster < filter.Start || cluster >= filter.End) continue;
                if (!any)
                {
                    System.Array.Copy(filter.Matrix, s_FilterA, 20);
                    any = true;
                }
                else
                {
                    FilterAtlas.Compose(s_FilterA, filter.Matrix, s_FilterB);
                    System.Array.Copy(s_FilterB, s_FilterA, 20);
                }
            }
            return any ? FilterAtlas.RowOf(s_FilterA) : 0;
        }

        // A warp reads the field a little to the side per pixel; uv3 carries how far and how fine, in the tile's own uv.
        Vector4 WarpOf(in PlacedGlyph g, in AtlasGlyph atlas)
        {
            short index = m_Attributes.At(g.Cluster).Warp;
            if (index < 0 || !atlas.HasDistance || atlas.Pixel) return default;
            WarpSpec warp = m_Attributes.Warps[index];
            float height = atlas.Top - atlas.Bottom;
            if (height <= 0 || g.Unit <= 0) return default;
            FontFace face = g.Instance.Face;
            float heightEm = height / face.Metrics.Upem;
            float uvPerEm = (atlas.V1 - atlas.V0) / heightEm;
            return new Vector4(warp.Amplitude * uvPerEm, 1f / (warp.Scale * uvPerEm), warp.Phase, 1);
        }

        // The first fill replaces the face: it costs no extra quad.
        bool FirstFill(int cluster, byte kind, out int record)
        {
            record = -1;
            if (m_Faces.Count == 0) return false;
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
            ValueList<LayerRecord> records = m_Attributes.Layers;
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
            m_CurrentOwner = glyphIndex;
            ref PlacedGlyph g = ref m_Placed[glyphIndex];
            ref AtlasGlyph atlas = ref m_PlacedAtlas[glyphIndex];
            if (atlas.Kind == AtlasGlyph.Empty) return;
            bool colorGlyph = atlas.Kind == AtlasGlyph.Color;
            if (colorGlyph && !record.OnColorGlyphs) return;

            ref LetteraAttributes.Cp attr = ref m_Attributes.At(g.Cluster);
            m_FilterRow = FilterFor(g.Cluster, record.Style);
            Color32 color = record.Paint.Color;
            if (record.Paint.UsesGlyphColor)
            {
                Color32 baseColor = m_DrawBase;
                color = attr.HasColor ? attr.Color : new Color32(baseColor.r, baseColor.g, baseColor.b, 255);
            }
            color.a = (byte)(color.a * attr.Alpha);

            float size = MeasureSize(record, g);
            float toEm = g.Size > 0 ? 1f / g.Size : 0;
            float toSdf = 1f / (2f * atlas.Spread);
            float softness = record.Softness * SoftnessEm * toSdf;
            bool multi = atlas.Kind == AtlasGlyph.Msdf;
            // The median keeps corners sharp at the edge; beyond the edge only the true distance is smooth and round.
            float mode = colorGlyph ? ModeColorSilhouette : atlas.Pixel ? ModePixel : multi ? ModeMedian : ModeSdf;
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
                    extra = WarpOf(g, atlas);
                    threshold = 0.5f - width * (1 + record.Align) * 0.5f * toSdf;
                    if (record.Align < 1) upper = 0.5f + width * (1 - record.Align) * 0.5f * toSdf;
                    // Under an opaque face a stroke can run through the glyph, which leaves no seam at the edge.
                    // Under a see-through face the middle must stay empty.
                    else if (FaceIsSeeThrough(g.Cluster)) upper = 0.5f;
                    if (multi && !record.SharpCorners) mode = ModeTrueDistance;
                    break;
                case LayerKind.Shadow:
                    dx = SnappedOffset(g, record.OffsetX.Of(size));
                    dy = SnappedOffset(g, record.OffsetY.Of(size));
                    threshold = 0.5f - record.Dilate.Of(size) * toEm * toSdf;
                    if (multi && (record.Softness > 0 || threshold != 0.5f)) mode = ModeTrueDistance;
                    break;
                case LayerKind.Glow:
                    float radius = record.Width.Of(size) * toEm;
                    if (radius <= 0) return;
                    if (colorGlyph)
                    {
                        AddColorHalo(g, atlas, color, record, radius * g.Size);
                        return;
                    }
                    if (multi) mode = ModeTrueDistance;
                    float ramp = radius * Mathf.Max(record.Softness, 0.01f);
                    threshold = 0.5f - (record.Dilate.Of(size) * toEm + radius - ramp * 0.5f) * toSdf;
                    softness = ramp * 0.5f * toSdf;
                    break;
                case LayerKind.InnerShadow:
                    if (colorGlyph) return;
                    mode = atlas.Pixel ? ModeInnerShadowPixel : multi ? ModeInnerShadowMulti : ModeInnerShadow;
                    float quadWidth = (atlas.Right - atlas.Left) * g.Unit, quadHeight = (atlas.Top - atlas.Bottom) * g.Unit;
                    extra.x = quadWidth > 0 ? record.OffsetX.Of(size) / quadWidth * (atlas.U1 - atlas.U0) : 0;
                    extra.y = quadHeight > 0 ? record.OffsetY.Of(size) / quadHeight * (atlas.V1 - atlas.V0) : 0;
                    extra.z = 0.5f + record.Dilate.Of(size) * toEm * toSdf;
                    break;
            }

            if (record.Kind == LayerKind.Extrude)
            {
                DrawExtrude(g, atlas, record, mode, attr.Alpha, size, toEm, toSdf);
                return;
            }
            if (record.Kind != LayerKind.InnerShadow && extra.w == 0) extra = WarpOf(g, atlas);

            if (threshold < 0.002f) threshold = 0.002f;
            UseSegment(record.Paint, record.Custom);
            PlacedGlyph moved = g;
            moved.X += dx;
            moved.Y += dy;
            AddQuad(moved, atlas, color, mode, threshold, softness, upper, record.Paint, PaintRect(record, g), dx, dy, extra);
        }

        // Offsets of effects on a pixel font come in whole font pixels, so all of a line's glyphs sit on one lattice.
        static float SnappedOffset(in PlacedGlyph g, float offset)
        {
            FontFace face = g.Instance.Face;
            if (face.PixelGrid <= 0 || !face.PixelSnap) return offset;
            float cell = g.Size / face.PixelGrid;
            return cell > 0 ? Mathf.Round(offset / cell) * cell : offset;
        }

        // From the far end to the near one, so the nearest copy lies right under the face.
        void DrawExtrude(in PlacedGlyph g, in AtlasGlyph atlas, in LayerRecord record, float mode, float alpha, float size, float toEm, float toSdf)
        {
            float dx = SnappedOffset(g, record.OffsetX.Of(size)), dy = SnappedOffset(g, record.OffsetY.Of(size));
            float threshold = 0.5f - record.Dilate.Of(size) * toEm * toSdf;
            float softness = record.Softness * SoftnessEm * toSdf;
            UseSegment(record.Paint);
            for (int step = record.Steps; step >= 1; step--)
            {
                float t = (float)step / record.Steps;
                Color32 color = Color32.Lerp(record.Paint.Color, record.FarColor, t);
                color.a = (byte)(color.a * alpha);
                PlacedGlyph moved = g;
                moved.X += dx * t;
                moved.Y += dy * t;
                AddQuad(moved, atlas, color, mode, threshold, softness, 0, record.Paint, default, 0, 0);
            }
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
            var parameters = new Vector4(du, dv, 0, m_FilterRow);
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

        void UseSegment(in ResolvedPaint paint, Material custom = null)
        {
            LetteraBlend blend = paint.Blend == LetteraBlend.Inherit ? LetteraBlend.Normal : paint.Blend;
            // By reference: Unity's comparison asks the engine whether each is alive, and this runs per quad.
            if (blend == m_SegmentBlend && ReferenceEquals(paint.Texture, m_SegmentTexture) && ReferenceEquals(custom, m_SegmentCustom)) return;
            FinishSegment();
            m_SegmentBlend = blend;
            m_SegmentTexture = paint.Texture;
            m_SegmentCustom = custom;
            m_SegmentStart = IndexCount;
        }

        void FinishSegment()
        {
            if (IndexCount > m_SegmentStart)
                Segments.Add(new MeshSegment { IndexStart = m_SegmentStart, IndexCount = IndexCount - m_SegmentStart, Blend = m_SegmentBlend, Texture = m_SegmentTexture, Custom = m_SegmentCustom });
            m_SegmentStart = IndexCount;
        }

        // Called per glyph, over indices the same pass has just sized; the tests run these with every check on.
        [Unity.IL2CPP.CompilerServices.Il2CppSetOption(Unity.IL2CPP.CompilerServices.Option.ArrayBoundsChecks, false), Unity.IL2CPP.CompilerServices.Il2CppSetOption(Unity.IL2CPP.CompilerServices.Option.NullChecks, false)]
        void AddQuad(in PlacedGlyph g, in AtlasGlyph atlas, Color32 color, float mode, float threshold, float softness, float upper, in ResolvedPaint paint, in Rect paintRect, float paintDx, float paintDy, Vector4 extra = default)
        {
                        if (g.Special != 0)
            {
                AddSpecialQuad(g, atlas, color, mode, threshold, softness, upper, paint, paintRect, paintDx, paintDy, extra);
                return;
            }
            float xUnit = g.Unit * (1 + g.XScale);
            float left = atlas.Left * xUnit, right = atlas.Right * xUnit;
            float bottom = atlas.Bottom * g.Unit, top = atlas.Top * g.Unit;
            float u0 = atlas.U0, v0 = atlas.V0, u1 = atlas.U1, v1 = atlas.V1;
            bool distance = (mode == ModeSdf || mode == ModeMedian || mode == ModeTrueDistance) && extra.w == 0;
            // A median keeps corners sharp, and beyond the edge it does not fall off as the distance does.
            bool cropped = distance && mode != ModeMedian && m_FilterRow == 0 && CropToReach(atlas, xUnit, g.Unit, g.Size, threshold, softness, ref left, ref bottom, ref right, ref top, ref u0, ref v0, ref u1, ref v1);
            int v = Reserve();
            if (g.Angle == 0)
            {
                Vertices[v] = new Vector3(g.X + left, g.Y + bottom);
                Vertices[v + 1] = new Vector3(g.X + left, g.Y + top);
                Vertices[v + 2] = new Vector3(g.X + right, g.Y + top);
                Vertices[v + 3] = new Vector3(g.X + right, g.Y + bottom);
                if (m_TrackInk)
                {
                    if (distance && !cropped) AddDistanceInk(g.X + left, g.Y + bottom, g.X + right, g.Y + top, atlas, xUnit, g.Unit, g.Size, threshold, softness);
                    else AddInk(g.X + left, g.Y + bottom, g.X + right, g.Y + top);
                }
            }
            else
            {
                float cos = Mathf.Cos(g.Angle), sin = Mathf.Sin(g.Angle);
                Vertices[v] = new Vector3(g.X + left * cos - bottom * sin, g.Y + left * sin + bottom * cos);
                Vertices[v + 1] = new Vector3(g.X + left * cos - top * sin, g.Y + left * sin + top * cos);
                Vertices[v + 2] = new Vector3(g.X + right * cos - top * sin, g.Y + right * sin + top * cos);
                Vertices[v + 3] = new Vector3(g.X + right * cos - bottom * sin, g.Y + right * sin + bottom * cos);
            }
            Uv0[v] = new Vector4(u0, v0, atlas.Page, mode);
            Uv0[v + 1] = new Vector4(u0, v1, atlas.Page, mode);
            Uv0[v + 2] = new Vector4(u1, v1, atlas.Page, mode);
            Uv0[v + 3] = new Vector4(u1, v0, atlas.Page, mode);
            var parameters = new Vector4(threshold, softness, upper, m_FilterRow);
            Uv1[v] = Uv1[v + 1] = Uv1[v + 2] = Uv1[v + 3] = parameters;
            SetColor(v, color);
            if (Styled) SetPaint(v, paint, paintRect, paintDx, paintDy, extra);
            if (m_FadeActive && g.Line == m_Aside.FadeLine) ApplyFade(v);
        }

        // Alpha falls off towards a truncated edge, per vertex, so it runs smoothly across a glyph.
        bool m_FadeActive;

        void PrepareFade()
        {
            m_FadeActive = false;
            foreach (Removal removal in m_Aside.Removals)
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
                    m_Aside.FadeLine = l;
                    m_Aside.FadeSign = rtl ? -1 : 1;
                    m_Aside.FadeDistance = distance;
                    m_Aside.FadeCut = (rtl ? line.X : line.X + line.Width) - m_Aside.FadeSign * removal.FadeShift.Of(size);
                    return;
                }
            }
        }

        void ApplyFade(int v)
        {
            for (int i = 0; i < 4; i++)
            {
                float toCut = (m_Aside.FadeCut - Vertices[v + i].x) * m_Aside.FadeSign;
                float fade = Mathf.Clamp01(toCut / m_Aside.FadeDistance);
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
            AddInk(left, bottom, right, top);
            Uv0[v] = Uv0[v + 1] = Uv0[v + 2] = Uv0[v + 3] = uv;
            Uv1[v] = Uv1[v + 1] = Uv1[v + 2] = Uv1[v + 3] = new Vector4(0, 0, 0, m_FilterRow);
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

        // Called per glyph, over indices the same pass has just sized; the tests run these with every check on.
        [Unity.IL2CPP.CompilerServices.Il2CppSetOption(Unity.IL2CPP.CompilerServices.Option.ArrayBoundsChecks, false), Unity.IL2CPP.CompilerServices.Il2CppSetOption(Unity.IL2CPP.CompilerServices.Option.NullChecks, false)]
        void SetColor(int v, Color32 color)
        {
            Colors[v] = Colors[v + 1] = Colors[v + 2] = Colors[v + 3] = color;
            m_OwnAlpha[v] = m_OwnAlpha[v + 1] = m_OwnAlpha[v + 2] = m_OwnAlpha[v + 3] = color.a;
            m_AllTakeBase &= m_QuadTakesBase;
            if (m_TrackBase)
            {
                if (m_TakesBase.Length < Colors.Length) System.Array.Resize(ref m_TakesBase, Colors.Length);
                m_TakesBase[v] = m_TakesBase[v + 1] = m_TakesBase[v + 2] = m_TakesBase[v + 3] = m_QuadTakesBase;
            }
        }

        float QuadMargin() => m_Host is LetteraText pixelled && !WholeTilesForTests ? 2f * pixelled.PixelLength() : 0;

        // A label's first mesh takes room for sixteen glyphs, so a counter going from one digit to three does not grow its buffers in the frame it changes.
        const int FirstVertices = 64, FirstWorldVertices = 16;

        // Only a styled text ever reads the last two streams, and most texts are not styled.
        void GrowVertices(int capacity)
        {
            System.Array.Resize(ref Vertices, capacity);
            System.Array.Resize(ref Colors, capacity);
            System.Array.Resize(ref Uv0, capacity);
            System.Array.Resize(ref Uv1, capacity);
            System.Array.Resize(ref m_OwnAlpha, capacity);
        }

        // Called per glyph, over indices the same pass has just sized; the tests run these with every check on.
        [Unity.IL2CPP.CompilerServices.Il2CppSetOption(Unity.IL2CPP.CompilerServices.Option.ArrayBoundsChecks, false), Unity.IL2CPP.CompilerServices.Il2CppSetOption(Unity.IL2CPP.CompilerServices.Option.NullChecks, false)]
        int Reserve()
        {
            int v = VertexCount;
            if (VertexCount + 4 > Vertices.Length) GrowVertices(System.Math.Max(Vertices.Length * 2, FirstVertices));
            if (Styled && Uv2.Length < Vertices.Length)
            {
                System.Array.Resize(ref Uv2, Vertices.Length);
                System.Array.Resize(ref Uv3, Vertices.Length);
            }
            if (IndexCount + 6 > Indices.Length) System.Array.Resize(ref Indices, System.Math.Max(Indices.Length * 2, FirstVertices * 3 / 2));
            if (m_RecordOwners)
            {
                EnsureOwnerSize(v / 4 + 1);
                m_Aside.QuadOwner[v / 4] = m_CurrentOwner;
            }
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

        // Called per glyph, over indices the same pass has just sized; the tests run these with every check on.
        [Unity.IL2CPP.CompilerServices.Il2CppSetOption(Unity.IL2CPP.CompilerServices.Option.ArrayBoundsChecks, false), Unity.IL2CPP.CompilerServices.Il2CppSetOption(Unity.IL2CPP.CompilerServices.Option.NullChecks, false)]
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
        // Called per glyph, over indices the same pass has just sized; the tests run these with every check on.
        [Unity.IL2CPP.CompilerServices.Il2CppSetOption(Unity.IL2CPP.CompilerServices.Option.ArrayBoundsChecks, false), Unity.IL2CPP.CompilerServices.Il2CppSetOption(Unity.IL2CPP.CompilerServices.Option.NullChecks, false)]
        void ApplyBaseColor()
        {
            Color32 baseColor = m_Host.BaseColor;
            int alpha = baseColor.a;
            for (int i = 0; i < VertexCount; i++)
                Colors[i].a = (byte)(m_OwnAlpha[i] * alpha / 255);
            if (m_AllTakeBase || (m_TrackBase && m_TakesBase.Length >= VertexCount))
            {
                for (int i = 0; i < VertexCount; i++)
                {
                    if (!m_AllTakeBase && !m_TakesBase[i]) continue;
                    Colors[i].r = baseColor.r;
                    Colors[i].g = baseColor.g;
                    Colors[i].b = baseColor.b;
                }
            }
            if (!m_WantsPose || m_Dirty >= DirtyLevel.Mesh || m_Aside.RestColors.Length < VertexCount) return;
            // The fade goes under whatever a pose does to alpha.
            for (int i = 0; i < VertexCount; i++)
            {
                m_Aside.RestColors[i].a = Colors[i].a;
                if (m_AllTakeBase || (m_TrackBase && m_TakesBase[i]))
                {
                    m_Aside.RestColors[i].r = baseColor.r;
                    m_Aside.RestColors[i].g = baseColor.g;
                    m_Aside.RestColors[i].b = baseColor.b;
                }
            }
        }

        bool[] m_TakesBase = System.Array.Empty<bool>();
        bool m_TrackBase, m_QuadTakesBase;

        // Nearly always every quad takes the text's color, and then there is nothing to note per quad, so the first change needs no pass and no array either.
        bool m_AllTakeBase;

        /// <summary>
        /// What a change of the text's color needs. The first change draws again, noting which glyphs take that color, so a text that never changes it pays nothing;
        /// later ones only rewrite those glyphs. Anything else that draws in the text's color, such as a line, needs the full pass.
        /// </summary>
        public DirtyLevel LevelForColorChange()
        {
            bool others = m_Attributes.Decorations.Count > 0 || m_Attributes.Layers.Count > 0 || m_Attributes.Highlights.Count > 0 || m_Aside.MarqueeCopies > 1 || m_Aside.StageHooks.Count > 0;
            if (others) return DirtyLevel.Mesh;
            if (m_TrackBase || (m_AllTakeBase && VertexCount > 0)) return DirtyLevel.Tint;
            m_TrackBase = true;
            return DirtyLevel.Mesh;
        }

        /// <summary>Marks mesh dirty when the atlas lost its glyphs.</summary>
        public void OnAtlasCleared() => SetDirty(DirtyLevel.Mesh);
    }
}

using System;
using UnityEngine;

namespace FirstSymbol.Lettera
{
    // Plain faces of a long text, a stretch of glyphs per thread. Each such glyph is one quad, so where a stretch's quads start is known
    // from counting before anything is written, and each stretch writes only its own vertices and indices. What the text learns from its
    // quads (whether all take its colour, the ink's box) is gathered per stretch and joined after. The arrays come out as drawing in order leaves them.
    internal sealed partial class TextEngine
    {
        struct FaceStretch
        {
            public int From, To, FirstVertex, FirstIndex, Quads, LastDrawn;
            public bool AllTakeBase;
            public int InkQuads;
            public float InkMinX, InkMinY, InkMaxX, InkMaxY;
            public float MinX, MinY, MaxX, MaxY;
        }

        /// <summary>Every quad of the mesh is a plain face, whose shading parameters are all the same: an upload may keep those it already holds.</summary>
        internal bool UniformParams => m_Aside.TailCount == VertexCount && VertexCount > 0 && !m_WantsPose;

        bool DrawPlainFacesInParallel(Color32 noColor)
        {
            int count = m_PlacedCount;
            // A quad writes about what a codepoint's analysis does twice over.
            if (count < 2 || !LetteraWorkers.ShouldRun(count * 2)) return false;
            PlacedGlyph[] placed = m_Placed;
            AtlasGlyph[] atlases = m_PlacedAtlas;
            int stretches = StretchCount(count);
            if (m_Aside.FaceStretches.Length < stretches) Array.Resize(ref m_Aside.FaceStretches, stretches);
            int quads = 0;
            for (int s = 0; s < stretches; s++)
            {
                int from = (int)((long)count * s / stretches), to = (int)((long)count * (s + 1) / stretches);
                int own = 0;
                for (int i = from; i < to; i++)
                {
                    ref AtlasGlyph atlas = ref atlases[i];
                    if (atlas.Kind == AtlasGlyph.Empty) continue;
                    // A formula's part, a turned glyph and a colour glyph draw their own way, in order.
                    if (placed[i].Special != 0 || placed[i].Angle != 0 || atlas.Kind == AtlasGlyph.Color) return false;
                    own++;
                }
                ref FaceStretch stretch = ref m_Aside.FaceStretches[s];
                stretch.From = from;
                stretch.To = to;
                stretch.FirstVertex = VertexCount + quads * 4;
                stretch.FirstIndex = IndexCount + quads * 6;
                stretch.Quads = own;
                quads += own;
            }
            // Room as quad after quad would have grown it.
            while (VertexCount + quads * 4 > Vertices.Length) GrowVertices(Math.Max(Vertices.Length * 2, FirstVertices));
            while (IndexCount + quads * 6 > Indices.Length) Array.Resize(ref Indices, Math.Max(Indices.Length * 2, FirstVertices * 3 / 2));
            if (m_RecordOwners)
                while (m_Aside.QuadOwner.Length < VertexCount / 4 + quads)
                    EnsureOwnerSize(m_Aside.QuadOwner.Length + 1);
            m_Aside.FaceNoColor = noColor;
            // With nothing drawn before them, the stretches also do what the end of the mesh does over every vertex: the box and the text's alpha.
            bool first = VertexCount == 0;
            m_Aside.FaceAlpha = first ? m_Host.BaseColor.a : -1;
            SplitPasses[2]++;
            LetteraWorkers.Run(stretches, m_Aside.DrawOne ?? (m_Aside.DrawOne = DrawFaceStretch));
            for (int s = 0; s < stretches; s++)
            {
                ref FaceStretch stretch = ref m_Aside.FaceStretches[s];
                if (stretch.Quads == 0) continue;
                m_AllTakeBase &= stretch.AllTakeBase;
                m_CurrentOwner = stretch.LastDrawn;
                if (!m_TrackInk) continue;
                m_InkQuads += stretch.InkQuads;
                if (stretch.InkMinX < m_InkMinX) m_InkMinX = stretch.InkMinX;
                if (stretch.InkMinY < m_InkMinY) m_InkMinY = stretch.InkMinY;
                if (stretch.InkMaxX > m_InkMaxX) m_InkMaxX = stretch.InkMaxX;
                if (stretch.InkMaxY > m_InkMaxY) m_InkMaxY = stretch.InkMaxY;
            }
            VertexCount += quads * 4;
            IndexCount += quads * 6;
            if (first && quads > 0)
            {
                float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
                for (int s = 0; s < stretches; s++)
                {
                    ref FaceStretch stretch = ref m_Aside.FaceStretches[s];
                    if (stretch.Quads == 0) continue;
                    if (stretch.MinX < minX) minX = stretch.MinX;
                    if (stretch.MaxX > maxX) maxX = stretch.MaxX;
                    if (stretch.MinY < minY) minY = stretch.MinY;
                    if (stretch.MaxY > maxY) maxY = stretch.MaxY;
                }
                // As ComputeBounds makes it from the same extremes.
                var center = new Vector3((minX + maxX) * 0.5f, (minY + maxY) * 0.5f);
                m_Aside.TailBounds = new Bounds(center, new Vector3(maxX - minX, maxY - minY));
                m_Aside.TailCount = VertexCount;
            }
            return true;
        }

        // What DrawPlainFaces writes for each glyph of a stretch, with Reserve's indices and owners.
        [Unity.IL2CPP.CompilerServices.Il2CppSetOption(Unity.IL2CPP.CompilerServices.Option.ArrayBoundsChecks, false), Unity.IL2CPP.CompilerServices.Il2CppSetOption(Unity.IL2CPP.CompilerServices.Option.NullChecks, false)]
        void DrawFaceStretch(int index)
        {
            ref FaceStretch stretch = ref m_Aside.FaceStretches[index];
            PlacedGlyph[] placed = m_Placed;
            AtlasGlyph[] atlases = m_PlacedAtlas;
            Vector3[] vertices = Vertices;
            Vector4[] uv0 = Uv0, uv1 = Uv1;
            Color32[] colors = Colors;
            byte[] ownAlpha = m_OwnAlpha;
            int[] indices = Indices, owners = m_Aside.QuadOwner;
            bool recordOwners = m_RecordOwners, trackInk = m_TrackInk;
            int baseAlpha = m_Aside.FaceAlpha;
            float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
            float inkMargin = m_InkMargin;
            Color32 noColor = m_Aside.FaceNoColor;
            int v = stretch.FirstVertex, n = stretch.FirstIndex, last = -1;
            bool allTakeBase = true;
            int inkQuads = 0;
            float inkMinX = float.MaxValue, inkMinY = float.MaxValue, inkMaxX = float.MinValue, inkMaxY = float.MinValue;
            for (int i = stretch.From; i < stretch.To; i++)
            {
                ref PlacedGlyph g = ref placed[i];
                ref AtlasGlyph atlas = ref atlases[i];
                if (atlas.Kind == AtlasGlyph.Empty) continue;
                last = i;
                ref LetteraAttributes.Cp attr = ref m_Attributes.At(g.Cluster);
                Color32 color = attr.HasColor ? attr.Color : noColor;
                color.a = (byte)(color.a * attr.Alpha);
                allTakeBase &= !attr.HasColor;
                float mode = atlas.Pixel ? ModePixel : atlas.Kind == AtlasGlyph.Msdf ? ModeMedian : ModeSdf;
                float xUnit = g.Unit * (1 + g.XScale);
                float left = g.X + atlas.Left * xUnit, right = g.X + atlas.Right * xUnit;
                float bottom = g.Y + atlas.Bottom * g.Unit, top = g.Y + atlas.Top * g.Unit;
                if (recordOwners) owners[v / 4] = i;
                indices[n] = v;
                indices[n + 1] = v + 1;
                indices[n + 2] = v + 2;
                indices[n + 3] = v + 2;
                indices[n + 4] = v + 3;
                indices[n + 5] = v;
                vertices[v] = new Vector3(left, bottom);
                vertices[v + 1] = new Vector3(left, top);
                vertices[v + 2] = new Vector3(right, top);
                vertices[v + 3] = new Vector3(right, bottom);
                uv0[v] = new Vector4(atlas.U0, atlas.V0, atlas.Page, mode);
                uv0[v + 1] = new Vector4(atlas.U0, atlas.V1, atlas.Page, mode);
                uv0[v + 2] = new Vector4(atlas.U1, atlas.V1, atlas.Page, mode);
                uv0[v + 3] = new Vector4(atlas.U1, atlas.V0, atlas.Page, mode);
                var parameters = new Vector4(0.5f, 0, 0, 0);
                uv1[v] = parameters;
                uv1[v + 1] = parameters;
                uv1[v + 2] = parameters;
                uv1[v + 3] = parameters;
                ownAlpha[v] = ownAlpha[v + 1] = ownAlpha[v + 2] = ownAlpha[v + 3] = color.a;
                if (baseAlpha >= 0)
                {
                    color.a = (byte)(color.a * baseAlpha / 255);
                    if (left < minX) minX = left;
                    if (left > maxX) maxX = left;
                    if (right < minX) minX = right;
                    if (right > maxX) maxX = right;
                    if (bottom < minY) minY = bottom;
                    if (bottom > maxY) maxY = bottom;
                    if (top < minY) minY = top;
                    if (top > maxY) maxY = top;
                }
                colors[v] = color;
                colors[v + 1] = color;
                colors[v + 2] = color;
                colors[v + 3] = color;
                if (trackInk)
                {
                    DistanceInset(atlas, xUnit, g.Unit, g.Size, 0.5f, 0, inkMargin, out float ix, out float iy);
                    float a = left + ix, b = bottom + iy, c = right - ix, d = top - iy;
                    inkQuads++;
                    if (a < inkMinX) inkMinX = a;
                    if (b < inkMinY) inkMinY = b;
                    if (c > inkMaxX) inkMaxX = c;
                    if (d > inkMaxY) inkMaxY = d;
                }
                v += 4;
                n += 6;
            }
            stretch.LastDrawn = last;
            stretch.MinX = minX;
            stretch.MinY = minY;
            stretch.MaxX = maxX;
            stretch.MaxY = maxY;
            stretch.AllTakeBase = allTakeBase;
            stretch.InkQuads = inkQuads;
            stretch.InkMinX = inkMinX;
            stretch.InkMinY = inkMinY;
            stretch.InkMaxX = inkMaxX;
            stretch.InkMaxY = inkMaxY;
        }
    }
}

using System;
using System.Collections.Generic;
using FirstSymbol.Lettera.Unicode;
using UnityEngine;

namespace FirstSymbol.Lettera
{
    // Motions that show other glyphs than the text's, without shaping anything again. The extra quads exist from the mesh on,
    // with no area while idle, so a frame only moves them and the vertex count never changes.
    internal sealed partial class TextEngine
    {
        struct SymbolGlyph
        {
            public AtlasGlyph Atlas;
            public float AdvanceEm;
            public int Codepoint;
        }

        sealed class SymbolSet
        {
            public FontInstance Instance;
            public int Density;
            public float Reach;
            public string Alphabet;
            public SymbolGlyph[] Glyphs;
        }

        struct ExtraQuad
        {
            public int Quad, Glyph, Body, Span;
            public bool Second;
        }

        // Before the first quad of a mesh.
        void PrepareExtraMotions()
        {
            Empty(m_Aside.ExtraQuads);
            m_Aside.UsesRestUv0 = false;
            if (m_Aside.MotionSpans.Count == 0) return;
            EnsureExact(ref m_Aside.FaceQuad, m_PlacedCount);
            for (int i = 0; i < m_PlacedCount; i++) m_Aside.FaceQuad[i] = -1;
            for (int s = 0; s < m_Aside.MotionSpans.Count; s++) m_Aside.MotionSpans[s].Effect.ReserveQuads(this, m_Aside.MotionSpans[s]);
        }

        internal void PrepareSymbols(in MotionSpan span, string alphabet)
        {
            m_Aside.UsesRestUv0 = true;
            EnsureExact(ref m_Aside.GlyphSymbols, m_PlacedCount);
            float rasterScale = m_Host.RasterScale;
            bool multi = m_Host.Data.RenderMode == LetteraRenderMode.MSDF;
            for (int i = 0; i < m_PlacedCount; i++)
            {
                ref PlacedGlyph g = ref m_Placed[i];
                if (g.Cluster < span.Start || g.Cluster >= span.End) continue;
                m_Aside.GlyphSymbols[i] = SymbolsFor(g.Instance, PixelsPerEm(g.Size, rasterScale), m_PlacedReach[i], alphabet ?? "", multi);
            }
        }

        // A symbol costs the atlas only once.
        SymbolSet SymbolsFor(FontInstance instance, int density, float reach, string alphabet, bool multi)
        {
            foreach (SymbolSet known in m_Aside.SymbolSets)
                if (known.Instance == instance && known.Density == density && known.Reach == reach && known.Alphabet == alphabet)
                    return known;
            var list = new ValueList<SymbolGlyph>();
            for (int i = 0; i < alphabet.Length; i++)
            {
                int cp = char.ConvertToUtf32(alphabet, i);
                if (cp > 0xFFFF) i++;
                if (!instance.Face.HasGlyph(cp)) continue;
                uint glyph = LetteraNative.lt_font_glyph(instance.Font, (uint)cp, 0);
                AtlasGlyph atlas = GlyphAtlas.Get(instance, glyph, density, reach, multi);
                list.Add(new SymbolGlyph { Atlas = atlas, AdvanceEm = LetteraNative.lt_glyph_advance(instance.Font, glyph), Codepoint = cp });
            }
            var set = new SymbolSet { Instance = instance, Density = density, Reach = reach, Alphabet = alphabet, Glyphs = list.ToArray() };
            Own(ref m_Aside.SymbolSets).Add(set);
            return set;
        }

        void ReserveExtraQuads()
        {
            for (int s = 0; s < m_Aside.MotionSpans.Count; s++)
            {
                MotionSpan span = m_Aside.MotionSpans[s];
                bool fringes = span.Effect is GlitchMotion, roll = span.Effect is RollMotion;
                if (!fringes && !roll) continue;
                EnsureExact(ref m_Aside.CpBody, m_TextLength + 1);
                NumberBodies(span);
                for (int i = 0; i < m_PlacedCount; i++)
                {
                    ref PlacedGlyph g = ref m_Placed[i];
                    if (g.Cluster < span.Start || g.Cluster >= span.End) continue;
                    ref AtlasGlyph atlas = ref m_PlacedAtlas[i];
                    if (atlas.Kind == AtlasGlyph.Empty || atlas.Kind == AtlasGlyph.Color) continue;
                    if (roll)
                    {
                        AddExtra(i, s, m_Aside.CpBody[g.Cluster], true, new Color32(255, 255, 255, 255));
                        continue;
                    }
                    var glitch = (GlitchMotion)span.Effect;
                    AddExtra(i, s, m_Aside.CpBody[g.Cluster], false, glitch.FringeA);
                    AddExtra(i, s, m_Aside.CpBody[g.Cluster], true, glitch.FringeB);
                }
            }
        }

        void AddExtra(int glyph, int span, int body, bool second, Color32 color)
        {
            ref PlacedGlyph g = ref m_Placed[glyph];
            ref AtlasGlyph atlas = ref m_PlacedAtlas[glyph];
            ref LetteraAttributes.Cp attr = ref m_Attributes.At(g.Cluster);
            if (span < m_Aside.MotionSpans.Count && m_Aside.MotionSpans[span].Effect is RollMotion)
            {
                Color32 baseColor = m_Host.BaseColor;
                color = attr.HasColor ? attr.Color : new Color32(baseColor.r, baseColor.g, baseColor.b, 255);
            }
            color.a = (byte)(color.a * attr.Alpha);
            m_CurrentOwner = -1;
            UseSegment(s_NoPaint);
            m_FilterRow = 0;
            int quad = VertexCount / 4;
            AddQuad(g, atlas, color, atlas.Pixel ? ModePixel : atlas.Kind == AtlasGlyph.Msdf ? ModeMedian : ModeSdf, 0.5f, 0, 0, s_NoPaint, default, 0, 0);
            Own(ref m_Aside.ExtraQuads).Add(new ExtraQuad { Quad = quad, Glyph = glyph, Body = body, Span = span, Second = second });
        }

        void CollapseExtraQuads()
        {
            if (m_Aside.UsesRestUv0)
            {
                EnsureExact(ref m_Aside.RestUv0, VertexCount);
                Array.Copy(Uv0, m_Aside.RestUv0, VertexCount);
            }
            foreach (ExtraQuad extra in m_Aside.ExtraQuads)
            {
                int v = extra.Quad * 4;
                for (int i = 0; i < 4; i++) m_Aside.RestVertices[v + i] = Vertices[v + i];
                Collapse(extra.Quad);
            }
        }

        void Collapse(int quad)
        {
            int v = quad * 4;
            Vector3 p = Vertices[v];
            Vertices[v + 1] = Vertices[v + 2] = Vertices[v + 3] = p;
        }

        // ------------------------------------------------------------- glitch

        internal void WriteFringes(GlitchMotion effect, in MotionSpan span, float t)
        {
            int k = Mathf.FloorToInt(t * span.Effective.V1);
            bool any = false;
            foreach (ExtraQuad extra in m_Aside.ExtraQuads)
            {
                if (extra.Span < 0 || extra.Span >= m_Aside.MotionSpans.Count || m_Aside.MotionSpans[extra.Span].Effect != effect) continue;
                int v = extra.Quad * 4;
                any = true;
                if (!GlitchMotion.Burst(effect.Seed, extra.Body, k, span.Effective.V2) || span.Effective.V0 == 0)
                {
                    Vertices[v + 1] = Vertices[v + 2] = Vertices[v + 3] = Vertices[v];
                    continue;
                }
                float shift = (extra.Second ? 1 : -1) * span.Effective.V0 * m_Placed[extra.Glyph].Size;
                LetteraGlyphPose pose = m_Aside.GlyphPoses[extra.Glyph];
                for (int i = 0; i < 4; i++)
                {
                    Vector3 r = m_Aside.RestVertices[v + i];
                    Vertices[v + i] = new Vector3(pose.A * r.x + pose.C * r.y + pose.Tx + shift, pose.B * r.x + pose.D * r.y + pose.Ty);
                }
            }
            if (any) m_ExtraStreams |= StreamPositions;
        }

        // ------------------------------------------------------------- scramble

        internal void WriteScramble(ScrambleMotion effect, in MotionSpan span, float t)
        {
            if (m_Aside.GlyphSymbols.Length < m_PlacedCount) return;
            int end = Math.Min(span.End, m_TextLength);
            int clusters = 0;
            for (int c = span.Start; c < end; c++)
                if (m_Boundaries[c] && !LetteraUnicode.IsWhiteSpace(m_Text[c]))
                    clusters++;
            float settled = LetteraAccessibility.ReducedMotion && effect.ReducedMotionPolicy == LetteraReducedMotion.Rest ? 1 : effect.SettledAt(span.Effective, t);
            int settledCount = Mathf.FloorToInt(settled * clusters + 1e-4f);
            int k = Mathf.FloorToInt(t * span.Effective.V1);
            int quads = VertexCount / 4;
            int order = -1, lastCluster = -1;
            // Clusters in logical order: count them apart from the glyph order, which a right-to-left run reverses.
            EnsureExact(ref m_Aside.CpBody, m_TextLength + 1);
            int number = 0;
            for (int c = span.Start; c < end; c++)
            {
                if (m_Boundaries[c] && !LetteraUnicode.IsWhiteSpace(m_Text[c])) number++;
                m_Aside.CpBody[c] = LetteraUnicode.IsWhiteSpace(m_Text[c]) ? -1 : number - 1;
            }
            for (int q = 0; q < quads; q++)
            {
                int owner = m_Aside.QuadOwner[q];
                if (owner < 0 || owner >= m_PlacedCount) continue;
                ref PlacedGlyph g = ref m_Placed[owner];
                if (g.Cluster < span.Start || g.Cluster >= span.End) continue;
                int v = q * 4;
                order = m_Aside.CpBody[g.Cluster];
                bool show = order < 0 || order < settledCount || m_PlacedAtlas[owner].Kind == AtlasGlyph.Color;
                if (show)
                {
                    RestoreUv0(v);
                    continue;
                }
                bool face = q == m_Aside.FaceQuad[owner] && (lastCluster != g.Cluster || FirstGlyphOfClusterIs(owner));
                SymbolSet set = m_Aside.GlyphSymbols[owner];
                if (!face || set == null || set.Glyphs.Length == 0)
                {
                    RestoreUv0(v);
                    Vertices[v + 1] = Vertices[v + 2] = Vertices[v + 3] = Vertices[v];
                    continue;
                }
                lastCluster = g.Cluster;
                int pick = (int)(LetteraMotionNoise.Hash(effect.Seed, order, k) % (uint)set.Glyphs.Length);
                ref SymbolGlyph symbol = ref set.Glyphs[pick];
                PlaceSymbol(v, g, symbol, m_Aside.GlyphPoses[owner], 0, 1);
            }
            m_ExtraStreams |= StreamPositions | StreamUv0;
        }

        bool FirstGlyphOfClusterIs(int glyph)
        {
            ref PlacedGlyph g = ref m_Placed[glyph];
            for (int i = 0; i < glyph; i++)
                if (m_Placed[i].Cluster == g.Cluster)
                    return false;
            return true;
        }

        void RestoreUv0(int v)
        {
            if (m_Aside.RestUv0.Length < v + 4) return;
            Uv0[v] = m_Aside.RestUv0[v];
            Uv0[v + 1] = m_Aside.RestUv0[v + 1];
            Uv0[v + 2] = m_Aside.RestUv0[v + 2];
            Uv0[v + 3] = m_Aside.RestUv0[v + 3];
        }

        // A symbol centered in the cluster's cell, cut to a band of it so a wheel can show two at once.
        void PlaceSymbol(int v, in PlacedGlyph g, in SymbolGlyph symbol, in LetteraGlyphPose pose, float yOffset, float keep)
        {
            ref readonly AtlasGlyph atlas = ref symbol.Atlas;
            float unit = g.Unit;
            float inset = (g.Advance - symbol.AdvanceEm * unit) * 0.5f;
            float left = g.X + inset + atlas.Left * unit, right = g.X + inset + atlas.Right * unit;
            float bottom = g.Y + atlas.Bottom * unit + yOffset, top = g.Y + atlas.Top * unit + yOffset;
            float u0 = atlas.U0, u1 = atlas.U1, v0 = atlas.V0, v1 = atlas.V1;
            if (keep < 1)
            {
                // The cell's band stays still: what slides past its edge is cut, with the field aligned to the quad.
                FontFace face = g.Instance.Face;
                float cellTop = g.Y + face.Metrics.Ascender * unit, cellBottom = g.Y + face.Metrics.Descender * unit;
                if (top > cellTop && top > bottom)
                {
                    v1 = Mathf.Lerp(v0, v1, Mathf.InverseLerp(bottom, top, cellTop));
                    top = cellTop;
                }
                if (bottom < cellBottom && top > bottom)
                {
                    v0 = Mathf.Lerp(v0, v1, Mathf.InverseLerp(bottom, top, cellBottom));
                    bottom = cellBottom;
                }
                if (bottom >= top)
                {
                    Vector3 p = new Vector3(g.X, g.Y);
                    Vertices[v] = Vertices[v + 1] = Vertices[v + 2] = Vertices[v + 3] = p;
                    return;
                }
            }
            Vertices[v] = Posed(pose, left, bottom);
            Vertices[v + 1] = Posed(pose, left, top);
            Vertices[v + 2] = Posed(pose, right, top);
            Vertices[v + 3] = Posed(pose, right, bottom);
            float page = atlas.Page, mode = Uv0[v].w;
            Uv0[v] = new Vector4(u0, v0, page, mode);
            Uv0[v + 1] = new Vector4(u0, v1, page, mode);
            Uv0[v + 2] = new Vector4(u1, v1, page, mode);
            Uv0[v + 3] = new Vector4(u1, v0, page, mode);
        }

        static Vector3 Posed(in LetteraGlyphPose pose, float x, float y) => new Vector3(pose.A * x + pose.C * y + pose.Tx, pose.B * x + pose.D * y + pose.Ty);

        // ------------------------------------------------------------- roll

        internal void WriteRoll(RollMotion effect, in MotionSpan span, float t)
        {
            string wheel = effect.Wheel ?? "";
            if (wheel.Length == 0 || m_Aside.GlyphSymbols.Length < m_PlacedCount) return;
            float roll = LetteraAccessibility.ReducedMotion && effect.ReducedMotionPolicy == LetteraReducedMotion.Rest ? 0 : effect.RollAt(span.Effective, t);
            float lag = span.Effective.V2;
            // Clusters on the wheel count in reading order.
            int end = Math.Min(span.End, m_TextLength);
            int number = 0;
            EnsureExact(ref m_Aside.CpBody, m_TextLength + 1);
            for (int c = span.Start; c < end; c++) m_Aside.CpBody[c] = c < m_Text.Length && wheel.IndexOf((char)m_Text[c]) >= 0 ? number++ : -1;

            foreach (ExtraQuad extra in m_Aside.ExtraQuads)
            {
                if (extra.Span < 0 || extra.Span >= m_Aside.MotionSpans.Count || m_Aside.MotionSpans[extra.Span].Effect != effect) continue;
                ref PlacedGlyph g = ref m_Placed[extra.Glyph];
                int second = extra.Quad * 4;
                int face = m_Aside.FaceQuad[extra.Glyph];
                int first = face * 4;
                int order = m_Aside.CpBody[g.Cluster];
                SymbolSet set = m_Aside.GlyphSymbols[extra.Glyph];
                float r = order < 0 ? 0 : Mathf.Max(0, roll - order * lag);
                if (r <= 0 || set == null || set.Glyphs.Length != wheel.Length)
                {
                    RestoreUv0(first);
                    RestoreUv0(second);
                    Vertices[second] = Vertices[second + 1] = Vertices[second + 2] = Vertices[second + 3] = m_Aside.RestVertices[second];
                    continue;
                }
                int index = wheel.IndexOf((char)m_Text[g.Cluster]);
                int length = wheel.Length;
                float position = index - r;
                int low = Mathf.FloorToInt(position);
                float f = position - low;
                int a = ((low % length) + length) % length, b = (a + 1) % length;
                FontFace fontFace = g.Instance.Face;
                float cell = (fontFace.Metrics.Ascender - fontFace.Metrics.Descender) * g.Unit;
                LetteraGlyphPose pose = m_Aside.GlyphPoses[extra.Glyph];
                PlaceSymbol(first, g, set.Glyphs[a], pose, f * cell, 0);
                PlaceSymbol(second, g, set.Glyphs[b], pose, (f - 1) * cell, 0);
            }
            m_ExtraStreams |= StreamPositions | StreamUv0;
        }
    }
}

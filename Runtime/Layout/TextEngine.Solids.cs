using System;
using System.Collections.Generic;
using UnityEngine;

namespace FirstSymbol.Lettera
{
    /// <summary>What a host that draws glyphs as solids reads. Nothing else calls these, and nothing here runs for a flat text.</summary>
    internal sealed partial class TextEngine
    {
        internal const byte SpecialRuleForSolids = SpecialRule;

        bool m_SolidsOnly;

        internal ITextHost HostForTools => m_Host;

        /// <summary>The glyphs of the text as placed, then the virtual ones a formula adds.</summary>
        internal ReadOnlySpan<PlacedGlyph> PlacedSpan => new ReadOnlySpan<PlacedGlyph>(m_Placed, 0, m_PlacedCount);

        internal bool HasMotionForSolids => m_MotionSpans.Count > 0 || m_RevealSpans.Count > 0;

        internal List<LetteraAttributes.ExtraRange> ExtrasForSolids => m_Attributes.Extras;

        /// <summary>A color glyph, or what stands in for an inline item: there is no outline to build from.</summary>
        internal bool IsDrawnFlatForSolids(int glyph)
        {
            ref PlacedGlyph g = ref m_Placed[glyph];
            if (g.Special == 0 && glyph < m_RealCount && IsInlineCp(g.Cluster)) return true;
            return m_PlacedAtlas[glyph].Kind == AtlasGlyph.Color;
        }

        internal bool HasFlatForSolids => VertexCount > 0;

        internal string ShownTextForTests()
        {
            var text = new System.Text.StringBuilder();
            foreach (int cp in ShownCodepoints) text.Append(char.ConvertFromUtf32(cp));
            return text.ToString();
        }

        internal bool HasLayersForChip => m_Attributes.Layers.Count > 0;

        internal void RuleOfForSolids(int special, out float length, out float thickness, out bool vertical)
        {
            MathSpecial rule = m_MathSpecials[special];
            length = rule.Length;
            thickness = rule.Thickness;
            vertical = rule.Vertical;
        }

        internal LetteraGlyphPose PoseOfForSolids(int glyph) =>
            glyph >= 0 && glyph < m_GlyphPoses.Length && m_WantsPose ? m_GlyphPoses[glyph] : LetteraGlyphPose.Identity;

        /// <summary>A glyph's vertex color: what its range says, on the text's own, with its fade.</summary>
        internal Color32 SolidColorOf(int glyph)
        {
            ref PlacedGlyph g = ref m_Placed[glyph];
            LetteraAttributes.Cp attr = m_Attributes.At(g.Cluster);
            Color32 color = attr.HasColor ? attr.Color : m_Host.BaseColor;
            if (g.Special != 0 && m_MathSpecials[g.SpecialIndex].HasColor) color = m_MathSpecials[g.SpecialIndex].Color;
            color.a = (byte)(color.a * attr.Alpha * (attr.HasColor ? m_Host.BaseColor.a / 255f : 1));
            return color;
        }

        /// <summary>An underline or a strikethrough, which a solid text extrudes as a box.</summary>
        internal struct SolidBox
        {
            public Rect Rect;
            public Color32 Color;
            public float Em;
            public int Glyph;
        }

        List<SolidBox> m_SolidBoxes = Rare<SolidBox>.List;

        internal List<SolidBox> SolidBoxes => m_SolidBoxes;

        // The engine's own quads, which for a solid text hold only what has no outline.
        internal void FillFlatMesh(Mesh mesh, MeshRenderer renderer)
        {
            mesh.Clear(false);
            if (VertexCount == 0) return;
            mesh.indexFormat = VertexCount > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16;
            mesh.SetVertices(Vertices, 0, VertexCount);
            mesh.SetColors(Colors, 0, VertexCount);
            mesh.SetUVs(0, Uv0, 0, VertexCount);
            mesh.SetUVs(1, Uv1, 0, VertexCount);
            if (Styled)
            {
                mesh.SetUVs(2, Uv2, 0, VertexCount);
                mesh.SetUVs(3, Uv3, 0, VertexCount);
            }
            mesh.subMeshCount = 1;
            mesh.SetIndices(Indices, 0, IndexCount, MeshTopology.Triangles, 0, true);
            renderer.sharedMaterial = LetteraMaterials.Variant(LetteraMaterials.World, Styled, LetteraBlend.Normal, null);
        }
    }
}

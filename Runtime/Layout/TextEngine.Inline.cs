using System;
using System.Collections.Generic;
using UnityEngine;

namespace FirstSymbol.Lettera
{
    // Sprites and objects standing in the flow. Each one is a U+FFFC its own width wide: a sprite draws one quad in the text's mesh,
    // and an object lives outside it, placed where the character landed.
    internal sealed partial class TextEngine
    {
        internal struct InlineItem
        {
            public InlineEffect Effect;
            public LetteraInlineEntry Entry;
            public int Cp;
            public float Size, Advance, Lift, Above, Below, Turn;
            public Vector2 Pivot, BoxSize;
            public Color32 Tint;
            public bool Tinted, InheritColor;
        }

        readonly List<InlineItem> m_InlineItems = new List<InlineItem>();
        readonly Dictionary<int, int> m_InlineByCp = new Dictionary<int, int>();

        internal IReadOnlyList<InlineItem> InlineItems => m_InlineItems;
        public bool HasInline => m_InlineItems.Count > 0;

        bool HasInlineSprites
        {
            get
            {
                for (int i = 0; i < m_InlineItems.Count; i++)
                    if (m_InlineItems[i].Entry?.Sprite != null)
                        return true;
                return false;
            }
        }

        /// <summary>Raised after a mesh pass that placed inline objects, and after a pose moved them.</summary>
        public event Action InlinePlaced;

        // After effects ran. An item's width and line room reach layout through the same extras any spacing effect uses.
        void CollectInlineItems()
        {
            bool had = m_InlineItems.Count > 0;
            m_InlineItems.Clear();
            m_InlineByCp.Clear();
            List<LetteraAttributes.InlineRequest> requests = m_Attributes.InlineRequests;
            for (int r = 0; r < requests.Count; r++)
            {
                LetteraAttributes.InlineRequest request = requests[r];
                if (request.Start >= m_TextLength || m_Text[request.Start] != 0xFFFC) continue;
                InlineItem item = request.Effect.Resolve(request.Slots, request.Start);
                m_InlineByCp[item.Cp] = m_InlineItems.Count;
                m_InlineItems.Add(item);
            }
            if (m_InlineItems.Count > 0 || had) m_InlinePlacedPending = true;
        }

        bool m_InlinePlacedPending;

        // The extra width in em, for the cluster at a codepoint.
        internal bool TryInlineAdvance(int cp, out float em)
        {
            if (m_InlineItems.Count > 0 && m_InlineByCp.TryGetValue(cp, out int item))
            {
                em = m_InlineItems[item].Advance;
                return true;
            }
            em = 0;
            return false;
        }

        // Room a line keeps for what stands in it, in em of the size given.
        internal void InlineLineExtents(int start, int end, float size, ref float ascent, ref float descent)
        {
            if (m_InlineItems.Count == 0) return;
            for (int i = 0; i < m_InlineItems.Count; i++)
            {
                InlineItem item = m_InlineItems[i];
                if (item.Cp < start || item.Cp >= end) continue;
                ascent = Mathf.Max(ascent, item.Above * size);
                descent = Mathf.Max(descent, item.Below * size);
            }
        }

        /// <summary>A placed item's box in local space, at rest. False when it has no glyph, as when eliding took it.</summary>
        public bool TryInlineBox(int cp, out Rect box, out int glyph)
        {
            box = default;
            glyph = FirstGlyphOf(cp);
            if (glyph < 0 || !m_InlineByCp.TryGetValue(cp, out int index)) return false;
            ref PlacedGlyph g = ref m_Placed[glyph];
            InlineItem item = m_InlineItems[index];
            float em = g.Size;
            box = new Rect(g.X, g.Y + item.Lift * em, item.BoxSize.x * em, item.BoxSize.y * em);
            return true;
        }

        // Sprites draw with the face, one quad to a glyph with a texture paint, so motion, reveal and alpha need nothing further.
        void DrawInlineSprites()
        {
            if (m_InlineItems.Count == 0) return;
            for (int i = 0; i < m_InlineItems.Count; i++)
            {
                InlineItem item = m_InlineItems[i];
                Sprite sprite = item.Entry?.Sprite;
                if (sprite == null || !TryInlineBox(item.Cp, out Rect box, out int glyph)) continue;
                if (m_RecordOwners) m_CurrentOwner = glyph;
                Color32 tint = new Color32(255, 255, 255, (byte)(m_Attributes.Chars[item.Cp].Alpha * 255f));
                if (item.InheritColor) tint = TextColorOf(item.Cp);
                else if (item.Tinted) tint = new Color32(item.Tint.r, item.Tint.g, item.Tint.b, (byte)(item.Tint.a * m_Attributes.Chars[item.Cp].Alpha));
                AddSpriteQuad(box, sprite, tint);
                m_CurrentOwner = -1;
            }
        }

        Color32 TextColorOf(int cp)
        {
            LetteraAttributes.Cp attr = m_Attributes.Chars[cp];
            Color32 color = attr.HasColor ? attr.Color : new Color32(255, 255, 255, 255);
            color.a = (byte)(color.a * attr.Alpha);
            return color;
        }

        // What takes its width from the item, not from any font: shaped with no glyph to draw, in the primary face.
        void PrepareInlineShaping()
        {
            for (int i = 0; i < m_InlineItems.Count; i++) m_ShapeText[m_InlineItems[i].Cp] = 0xA0;
        }

        void AddInlineExtras()
        {
            for (int i = 0; i < m_InlineItems.Count; i++)
            {
                InlineItem item = m_InlineItems[i];
                m_CpExtraEm[item.Cp] += item.Advance - m_CpAdvanceEm[item.Cp];
            }
        }

        // No pixels of the fallback box go out, whatever a font does for this character.
        bool IsInlineCp(int cp) => m_InlineItems.Count > 0 && m_InlineByCp.ContainsKey(cp);

        void AddSpriteQuad(in Rect box, Sprite sprite, Color32 tint)
        {
            var paint = new ResolvedPaint { Code = ResolvedPaint.CodeTexture, Texture = sprite.texture, Blend = LetteraBlend.Normal };
            UseSegment(paint);
            int v = VertexCount;
            AddSolid(box.xMin, box.yMin, box.xMax, box.yMax, tint, s_NoPaint, default, 0, 0);
            Vector4 uv = UnityEngine.Sprites.DataUtility.GetOuterUV(sprite);
            float code = ResolvedPaint.CodeTexture;
            Uv2[v] = new Vector4(uv.x, uv.y, 0, code);
            Uv2[v + 1] = new Vector4(uv.x, uv.w, 0, code);
            Uv2[v + 2] = new Vector4(uv.z, uv.w, 0, code);
            Uv2[v + 3] = new Vector4(uv.z, uv.y, 0, code);
            UseSegment(s_NoPaint);
        }

        internal bool IsCpRemoved(int cp) => IsRemoved(cp);

        internal bool SpanHasInline(int start, int end)
        {
            for (int i = 0; i < m_InlineItems.Count; i++)
                if (m_InlineItems[i].Cp >= start && m_InlineItems[i].Cp < end)
                    return true;
            return false;
        }

        internal bool PosesObjects => m_WantsPose && HasMoving;

        void RaiseInlinePlaced()
        {
            if (m_InlineItems.Count == 0 && !m_InlinePlacedPending) return;
            m_InlinePlacedPending = false;
            InlinePlaced?.Invoke();
        }
    }
}

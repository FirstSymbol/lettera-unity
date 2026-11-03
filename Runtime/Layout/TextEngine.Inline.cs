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
            /// <summary>A formula's own box, shared by every text showing it.</summary>
            public LetteraMathLayout Math;
            public float MathScale;
            public bool MathDisplay;
            public string MathSource, Alt;
        }

        internal FontFace PrimaryFaceForMath => LetteraFontChains.Get(m_Host.Data.Font, m_Host.Data.FontStack).PrimaryFace;

        // Each thing a formula draws is a glyph only the mesh sees, so color, layers, reveal and motion reach it with nothing further.
        void PlaceFormulas()
        {
            Empty(m_Aside.MathSpecials);
            for (int i = 0; i < m_Aside.InlineItems.Count; i++)
            {
                InlineItem item = m_Aside.InlineItems[i];
                if (item.Math == null) continue;
                int glyph = FirstGlyphOf(item.Cp);
                if (glyph < 0) continue;
                PlacedGlyph at = m_Placed[glyph];
                float em = at.Size * item.MathScale;
                float cos = at.Angle != 0 ? Mathf.Cos(at.Angle) : 1, sin = at.Angle != 0 ? Mathf.Sin(at.Angle) : 0;
                LetteraMathItem[] items = item.Math.Items;
                for (int k = 0; k < items.Length; k++)
                {
                    LetteraMathItem part = items[k];
                    FontInstance instance = item.Math.Faces[part.Face];
                    float px = part.X * em, py = part.Y * em;
                    var placed = new PlacedGlyph
                    {
                        Glyph = part.Glyph, Instance = instance, Cluster = item.Cp, Line = at.Line,
                        X = at.X + px * cos - py * sin, Y = at.Y + px * sin + py * cos,
                        Size = em * part.Scale, Unit = em * part.Scale / instance.Face.Metrics.Upem, Angle = at.Angle, Pen = at.X, Marker = true,
                    };
                    bool colored = part.Color > 0 && part.Color <= item.Math.Colors.Length;
                    // A glyph holds this in the room a plain text already pays for. Past it, a part draws plain.
                    if (m_Aside.MathSpecials.Count >= ushort.MaxValue)
                    {
                        if (part.Kind != MathItemKind.Rule) Own(ref m_Aside.VirtualGlyphs).Add(placed);
                        continue;
                    }
                    if (part.Kind == MathItemKind.Rule)
                    {
                        // Thin rules read as missing, so one under a pixel draws at a pixel, around the same middle. Nothing moves in the layout.
                        // Only a canvas knows its pixels: a world unit is whatever a camera makes of it.
                        float least = m_Host.Owner is LetteraText ? LetteraSettings.Instance.Math.MinRulePixels / (Mathf.Max(m_Host.RasterScale, 0.01f) * Mathf.Max(em, 0.01f)) : 0;
                        if (part.Thickness < least)
                        {
                            float grow = (least - part.Thickness) / 2 * em;
                            if (part.Vertical) placed.X -= grow * cos;
                            else placed.Y -= grow * cos;
                            part.Thickness = least;
                        }
                        placed.Special = SpecialRule;
                        placed.Size = em;
                        placed.Unit = em / 1000;
                        placed.SpecialIndex = (ushort)m_Aside.MathSpecials.Count;
                        Own(ref m_Aside.MathSpecials).Add(new MathSpecial { Length = part.Length * 1000, Thickness = part.Thickness * 1000, Vertical = part.Vertical, HasColor = colored, Color = colored ? item.Math.Colors[part.Color - 1] : default });
                    }
                    else if (part.Cropped || colored)
                    {
                        placed.Special = part.Cropped ? SpecialCrop : SpecialColor;
                        placed.SpecialIndex = (ushort)m_Aside.MathSpecials.Count;
                        Own(ref m_Aside.MathSpecials).Add(new MathSpecial { Low = part.CropLow, High = part.CropHigh, Vertical = part.Vertical, HasColor = colored, Color = colored ? item.Math.Colors[part.Color - 1] : default });
                    }
                    Own(ref m_Aside.VirtualGlyphs).Add(placed);
                }
            }
        }

        const byte SpecialCrop = 1, SpecialRule = 2, SpecialColor = 3;

        // A rule's tile is a short rule, whose middle column holds the whole field across it. Its two ends draw as they are,
        // and between them that one column stretches, which is exact and not an approximation. A rule shorter than the tile takes its two ends cut.
        void AddRule(in MathSpecial special, in AtlasGlyph atlas, in PlacedGlyph plain, Color32 color, float mode, float threshold, float softness, float upper, ResolvedPaint paint, Rect paintRect, float paintDx, float paintDy, Vector4 extra)
        {
            bool vertical = special.Vertical;
            float low = vertical ? atlas.Bottom : atlas.Left, high = vertical ? atlas.Top : atlas.Right;
            float t0 = vertical ? atlas.V0 : atlas.U0, t1 = vertical ? atlas.V1 : atlas.U1;
            float span = high - low;
            if (span <= 0 || special.Length <= 0) return;
            float reach = -low, ink = span - 2 * reach;
            float half = Mathf.Min(ink, special.Length) / 2;
            float shareStart = (reach + half) / span;

            void Part(float from, float to, float texFrom, float texTo, in AtlasGlyph source, in PlacedGlyph glyph)
            {
                AtlasGlyph part = source;
                if (vertical)
                {
                    part.Bottom = from;
                    part.Top = to;
                    part.V0 = texFrom;
                    part.V1 = texTo;
                }
                else
                {
                    part.Left = from;
                    part.Right = to;
                    part.U0 = texFrom;
                    part.U1 = texTo;
                }
                AddQuad(glyph, part, color, mode, threshold, softness, upper, paint, paintRect, paintDx, paintDy, extra);
            }

            Part(low, half, t0, Mathf.Lerp(t0, t1, shareStart), atlas, plain);
            if (special.Length > ink)
            {
                float middle = (t0 + t1) / 2;
                Part(half, special.Length - half, middle, middle, atlas, plain);
            }
            Part(special.Length - half, special.Length + reach, Mathf.Lerp(t1, t0, shareStart), t1, atlas, plain);
        }

        struct MathSpecial
        {
            public float Low, High, Length, Thickness;
            public bool Vertical, HasColor;
            public Color32 Color;
        }

        /// <summary>A rule's three quads, a part cut at its seam, or a glyph in a color of its own. Plain text never comes here.</summary>
        void AddSpecialQuad(in PlacedGlyph g, in AtlasGlyph atlas, Color32 color, float mode, float threshold, float softness, float upper, in ResolvedPaint paint, in Rect paintRect, float paintDx, float paintDy, Vector4 extra)
        {
            MathSpecial special = m_Aside.MathSpecials[g.SpecialIndex];
            if (special.HasColor)
            {
                byte alpha = color.a;
                color = special.Color;
                color.a = (byte)(color.a * alpha / 255);
            }
            PlacedGlyph plain = g;
            plain.Special = 0;
            AtlasGlyph piece = atlas;
            switch (g.Special)
            {
                case SpecialCrop:
                    if (special.Vertical)
                    {
                        float span = atlas.Top - atlas.Bottom;
                        float low = Mathf.Max(special.Low, atlas.Bottom), high = Mathf.Min(special.High, atlas.Top);
                        if (high <= low || span <= 0) return;
                        piece.V0 = Mathf.Lerp(atlas.V0, atlas.V1, (low - atlas.Bottom) / span);
                        piece.V1 = Mathf.Lerp(atlas.V0, atlas.V1, (high - atlas.Bottom) / span);
                        piece.Bottom = low;
                        piece.Top = high;
                    }
                    else
                    {
                        float span = atlas.Right - atlas.Left;
                        float low = Mathf.Max(special.Low, atlas.Left), high = Mathf.Min(special.High, atlas.Right);
                        if (high <= low || span <= 0) return;
                        piece.U0 = Mathf.Lerp(atlas.U0, atlas.U1, (low - atlas.Left) / span);
                        piece.U1 = Mathf.Lerp(atlas.U0, atlas.U1, (high - atlas.Left) / span);
                        piece.Left = low;
                        piece.Right = high;
                    }
                    AddQuad(plain, piece, color, mode, threshold, softness, upper, paint, paintRect, paintDx, paintDy, extra);
                    return;
                case SpecialRule:
                    AddRule(special, atlas, plain, color, mode, threshold, softness, upper, paint, paintRect, paintDx, paintDy, extra);
                    return;
                default:
                    AddQuad(plain, piece, color, mode, threshold, softness, upper, paint, paintRect, paintDx, paintDy, extra);
                    return;
            }
        }

        internal IReadOnlyList<InlineItem> InlineItems => m_Aside.InlineItems;
        public bool HasInline => m_Aside.InlineItems.Count > 0;

        bool HasInlineSprites
        {
            get
            {
                for (int i = 0; i < m_Aside.InlineItems.Count; i++)
                    if (m_Aside.InlineItems[i].Entry?.Sprite != null)
                        return true;
                return false;
            }
        }

        /// <summary>Raised after a mesh pass that placed inline objects, and after a pose moved them.</summary>
        public event Action InlinePlaced;

        // After effects ran. An item's width and line room reach layout through the same extras any spacing effect uses.
        void CollectInlineItems()
        {
            bool had = m_Aside.InlineItems.Count > 0;
            Empty(m_Aside.InlineItems);
            m_Aside.InlineByCp.Clear();
            m_Aside.FormulaCount = 0;
            List<LetteraAttributes.InlineRequest> requests = m_Attributes.InlineRequests;
            for (int r = 0; r < requests.Count; r++)
            {
                LetteraAttributes.InlineRequest request = requests[r];
                if (request.Start >= m_TextLength || m_Text[request.Start] != 0xFFFC) continue;
                InlineItem item = request.Effect.ResolveIn(this, request.Slots, request.Start);
                if (item.Math != null) m_Aside.FormulaCount++;
                Own(ref m_Aside.InlineByCp)[item.Cp] = m_Aside.InlineItems.Count;
                Own(ref m_Aside.InlineItems).Add(item);
            }
            if (m_Aside.InlineItems.Count > 0 || had) m_InlinePlacedPending = true;
        }

        bool m_InlinePlacedPending;

        // The extra width in em, for the cluster at a codepoint.
        internal bool TryInlineAdvance(int cp, out float em)
        {
            if (m_Aside.InlineItems.Count > 0 && m_Aside.InlineByCp.TryGetValue(cp, out int item))
            {
                em = m_Aside.InlineItems[item].Advance;
                return true;
            }
            em = 0;
            return false;
        }

        // Room a line keeps for what stands in it, in em of the size given.
        internal void InlineLineExtents(int start, int end, float size, ref float ascent, ref float descent)
        {
            if (m_Aside.InlineItems.Count == 0) return;
            for (int i = 0; i < m_Aside.InlineItems.Count; i++)
            {
                InlineItem item = m_Aside.InlineItems[i];
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
            if (glyph < 0 || !m_Aside.InlineByCp.TryGetValue(cp, out int index)) return false;
            ref PlacedGlyph g = ref m_Placed[glyph];
            InlineItem item = m_Aside.InlineItems[index];
            float em = g.Size;
            box = new Rect(g.X, g.Y + item.Lift * em, item.BoxSize.x * em, item.BoxSize.y * em);
            return true;
        }

        // Sprites draw with the face, one quad to a glyph with a texture paint, so motion, reveal and alpha need nothing further.
        void DrawInlineSprites()
        {
            if (m_Aside.InlineItems.Count == 0) return;
            for (int i = 0; i < m_Aside.InlineItems.Count; i++)
            {
                InlineItem item = m_Aside.InlineItems[i];
                Sprite sprite = item.Entry?.Sprite;
                if (sprite == null || !TryInlineBox(item.Cp, out Rect box, out int glyph)) continue;
                if (m_RecordOwners) m_CurrentOwner = glyph;
                Color32 tint = new Color32(255, 255, 255, (byte)(m_Attributes.At(item.Cp).Alpha * 255f));
                if (item.InheritColor) tint = TextColorOf(item.Cp);
                else if (item.Tinted) tint = new Color32(item.Tint.r, item.Tint.g, item.Tint.b, (byte)(item.Tint.a * m_Attributes.At(item.Cp).Alpha));
                AddSpriteQuad(box, sprite, tint);
                m_CurrentOwner = -1;
            }
        }

        Color32 TextColorOf(int cp)
        {
            LetteraAttributes.Cp attr = m_Attributes.At(cp);
            Color32 color = attr.HasColor ? attr.Color : new Color32(255, 255, 255, 255);
            color.a = (byte)(color.a * attr.Alpha);
            return color;
        }

        // What takes its width from the item, not from any font: shaped with no glyph to draw, in the primary face.
        void PrepareInlineShaping()
        {
            for (int i = 0; i < m_Aside.InlineItems.Count; i++) m_ShapeText[m_Aside.InlineItems[i].Cp] = 0xA0;
        }

        void AddInlineExtras()
        {
            for (int i = 0; i < m_Aside.InlineItems.Count; i++)
            {
                InlineItem item = m_Aside.InlineItems[i];
                m_CpExtraEm[item.Cp] += item.Advance - m_CpAdvanceEm[item.Cp];
            }
        }

        // No pixels of the fallback box go out, whatever a font does for this character.
        bool IsInlineCp(int cp) => m_Aside.InlineItems.Count > 0 && m_Aside.InlineByCp.ContainsKey(cp);

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
            for (int i = 0; i < m_Aside.InlineItems.Count; i++)
                if (m_Aside.InlineItems[i].Cp >= start && m_Aside.InlineItems[i].Cp < end)
                    return true;
            return false;
        }

        internal bool SpanHasSprite(int start, int end)
        {
            for (int i = 0; i < m_Aside.InlineItems.Count; i++)
                if (m_Aside.InlineItems[i].Cp >= start && m_Aside.InlineItems[i].Cp < end && m_Aside.InlineItems[i].Math == null)
                    return true;
            return false;
        }

        internal bool SpanHasFormula(int start, int end)
        {
            if (m_Aside.FormulaCount == 0) return false;
            for (int i = 0; i < m_Aside.InlineItems.Count; i++)
                if (m_Aside.InlineItems[i].Cp >= start && m_Aside.InlineItems[i].Cp < end && m_Aside.InlineItems[i].Math != null)
                    return true;
            return false;
        }

        internal bool IsFormulaAt(int cp) => m_Aside.FormulaCount > 0 && m_Aside.InlineByCp.TryGetValue(cp, out int item) && m_Aside.InlineItems[item].Math != null;

        internal string FormulaSourceAt(int cp) => m_Aside.InlineByCp.TryGetValue(cp, out int item) ? m_Aside.InlineItems[item].MathSource : null;

        internal bool FormulaIsDisplay(int cp) => m_Aside.InlineByCp.TryGetValue(cp, out int item) && m_Aside.InlineItems[item].MathDisplay;

        /// <summary>What a formula says to a screen reader: its alternative text, else its source.</summary>
        internal string FormulaLabelAt(int cp)
        {
            if (!m_Aside.InlineByCp.TryGetValue(cp, out int item)) return null;
            return string.IsNullOrEmpty(m_Aside.InlineItems[item].Alt) ? m_Aside.InlineItems[item].MathSource : m_Aside.InlineItems[item].Alt;
        }

        internal void CollectFormulas(List<LetteraFormulaInfo> output)
        {
            for (int i = 0; i < m_Aside.InlineItems.Count; i++)
            {
                InlineItem item = m_Aside.InlineItems[i];
                if (item.Math == null) continue;
                int glyph = FirstGlyphOf(item.Cp);
                Rect box = default;
                if (glyph >= 0)
                {
                    PlacedGlyph at = m_Placed[glyph];
                    float em = at.Size * item.MathScale;
                    box = new Rect(at.X, at.Y - item.Math.Descent * em, item.Math.Width * em, (item.Math.Ascent + item.Math.Descent) * em);
                }
                output.Add(new LetteraFormulaInfo { CodePoint = item.Cp, Source = item.MathSource, Display = item.MathDisplay, Box = box, Failed = item.Math.Failed, Message = item.Math.Message, ErrorPosition = item.Math.ErrorPosition });
            }
        }

        internal bool PosesObjects => m_WantsPose && HasMoving;

        void RaiseInlinePlaced()
        {
            if (m_Aside.InlineItems.Count == 0 && !m_InlinePlacedPending) return;
            m_InlinePlacedPending = false;
            // Most texts never hold an inline object, so what looks after them comes with the first.
            if (InlinePlaced == null && m_Host is Component) LetteraInlineObjects.Attach(this, m_Host.Owner);
            InlinePlaced?.Invoke();
        }
    }
}

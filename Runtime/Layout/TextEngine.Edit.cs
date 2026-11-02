using System;
using System.Collections.Generic;
using UnityEngine;

namespace FirstSymbol.Lettera
{
    // What editing asks of the parse: how shown indices map to source text, and the views a field puts on it.
    internal sealed partial class TextEngine
    {
        bool m_Literal;
        int m_RevealSource = -1;
        int m_MaskGlyph;
        int m_ProtectStart, m_ProtectLength;

        /// <summary>The source shows as it is: no style applies and every character can be reached.</summary>
        public bool Literal
        {
            get => m_Literal;
            set
            {
                if (m_Literal == value) return;
                m_Literal = value;
                SetDirty(DirtyLevel.Parse);
            }
        }

        /// <summary>A source position whose enclosing ranges show their delimiters, or -1.</summary>
        public int RevealSource
        {
            get => m_RevealSource;
            set
            {
                if (m_RevealSource == value) return;
                m_RevealSource = value;
                SetDirty(DirtyLevel.Parse);
            }
        }

        /// <summary>Each cluster shapes as this codepoint, with indices unchanged. 0 shows the text itself.</summary>
        public int MaskGlyph
        {
            get => m_MaskGlyph;
            set
            {
                if (m_MaskGlyph == value) return;
                m_MaskGlyph = value;
                SetDirty(DirtyLevel.Shape);
            }
        }

        /// <summary>The range of the text a mask leaves readable: the running composition.</summary>
        public int MaskExceptStart { get; set; } = -1;
        public int MaskExceptEnd { get; set; } = -1;

        public bool SuspendElide
        {
            get => m_SuspendElide;
            set
            {
                if (m_SuspendElide == value) return;
                m_SuspendElide = value;
                SetDirty(DirtyLevel.Layout);
            }
        }

        bool m_SuspendElide;

        /// <summary>Source text no matcher may read as markup, such as a running composition.</summary>
        public void ProtectSource(int start, int length)
        {
            m_ProtectStart = start;
            m_ProtectLength = length;
        }

        void MaskParagraph(int start, int contentEnd)
        {
            for (int i = start; i < contentEnd; i++)
            {
                if (i >= MaskExceptStart && i < MaskExceptEnd) continue;
                m_ShapeText[i] = m_Boundaries[i] ? m_MaskGlyph : 0x200B;
            }
        }

        public int ShownLength => m_TextLength;

        /// <summary>A direction a field insists on, as 0 or 1, or -1.</summary>
        public int FieldDirection { get; set; } = -1;

        /// <summary>The color a cluster draws in, for a caret next to it.</summary>
        public Color GlyphColorAt(int shown)
        {
            if (shown >= 0 && shown < m_TextLength && m_Attributes.Chars != null && m_Attributes.Chars[shown].HasColor) return m_Attributes.Chars[shown].Color;
            return m_Host.BaseColor;
        }

        /// <summary>Where the source text of a shown codepoint starts.</summary>
        public int SourceStartOf(int shown) => m_SourceIndex[Math.Min(Math.Max(shown, 0), m_TextLength)];

        /// <summary>Where the source text of a shown codepoint ends.</summary>
        public int SourceEndOf(int shown)
        {
            if (TryGetReplacement(shown, out LetteraMatchContext.Claim claim)) return claim.Start + claim.Length;
            return m_SourceIndex[shown] + (m_Text[shown] > 0xFFFF ? 2 : 1);
        }

        bool TryGetReplacement(int shown, out LetteraMatchContext.Claim claim)
        {
            List<LetteraMatchContext.Claim> claims = m_MatchContext.Claims;
            int source = m_SourceIndex[shown];
            for (int i = 0; i < claims.Count; i++)
            {
                if (claims[i].Start != source || claims[i].Replacement == null || claims[i].Replacement.Length == 0) continue;
                claim = claims[i];
                return true;
            }
            claim = default;
            return false;
        }

        /// <summary>A matcher's replacement is one unit: false when an index stands inside none.</summary>
        public bool AtomicAround(int index, out int start, out int end)
        {
            start = end = index;
            if (index <= 0 || index >= m_TextLength) return false;
            if (m_SourceIndex[index] != m_SourceIndex[index - 1]) return false;
            if (!TryGetReplacement(index, out LetteraMatchContext.Claim claim)) return false;
            start = RenderedIndexFromSource(claim.Start);
            end = start + claim.Replacement.Length;
            return index > start && index < end;
        }

        /// <summary>The source spans that hold a shown range's characters, in order. Delimiters between them stay: removing them is a separate step.</summary>
        public void SourceSpansOf(int start, int end, List<Vector2Int> spans)
        {
            for (int i = start; i < end; i++)
            {
                int a = SourceStartOf(i), b = SourceEndOf(i);
                if (TryGetReplacement(i, out LetteraMatchContext.Claim claim)) i = Math.Max(i, RenderedIndexFromSource(claim.Start) + claim.Replacement.Length - 1);
                if (spans.Count > 0 && spans[spans.Count - 1].y == a) spans[spans.Count - 1] = new Vector2Int(spans[spans.Count - 1].x, b);
                else spans.Add(new Vector2Int(a, b));
            }
        }

        // Claims are sorted once the rendered text is built, so a range's delimiters are its first and last claim.
        bool DelimitersOf(int range, out LetteraMatchContext.Claim open, out LetteraMatchContext.Claim close)
        {
            open = close = default;
            bool hasOpen = false, hasClose = false;
            List<LetteraMatchContext.Claim> claims = m_MatchContext.Claims;
            for (int i = 0; i < claims.Count; i++)
            {
                if (claims[i].OpenRange == range) { open = claims[i]; hasOpen = true; }
                if (claims[i].CloseRange == range) { close = claims[i]; hasClose = true; }
            }
            return hasOpen && hasClose;
        }

        /// <summary>The source spans of ranges left with no characters, to remove after a deletion. Removes pairs only.</summary>
        public void EmptyRangeDelimiters(int shownAt, List<Vector2Int> spans)
        {
            List<LetteraMatchContext.RangeRecord> ranges = m_MatchContext.Ranges;
            for (int r = 0; r < ranges.Count; r++)
            {
                LetteraMatchContext.RangeRecord range = ranges[r];
                if (range.RenderedStart != shownAt || range.RenderedStart != range.RenderedEnd || range.WholeText || range.IsSpan || range.SelfClosing) continue;
                if (!DelimitersOf(r, out LetteraMatchContext.Claim open, out LetteraMatchContext.Claim close)) continue;
                if (open.Length == 0 || close.Length == 0 || open.Replacement != null || close.Replacement != null) continue;
                spans.Add(new Vector2Int(open.Start, open.Start + open.Length));
                spans.Add(new Vector2Int(close.Start, close.Start + close.Length));
            }
        }

        // Claims that hide the delimiters of a range around the position become empty, so those characters show and stay inside the range.
        void RevealRangesAt(LetteraMatchContext context, int position)
        {
            List<LetteraMatchContext.Claim> claims = context.Claims;
            for (int r = 0; r < context.Ranges.Count; r++)
            {
                int openIndex = -1, closeIndex = -1;
                for (int c = 0; c < claims.Count; c++)
                {
                    if (claims[c].OpenRange == r) openIndex = c;
                    if (claims[c].CloseRange == r) closeIndex = c;
                }
                if (openIndex < 0 || closeIndex < 0 || openIndex == closeIndex) continue;
                LetteraMatchContext.Claim open = claims[openIndex], close = claims[closeIndex];
                if (position < open.Start || position > close.Start + close.Length) continue;
                // The close claim moves past its delimiter, which keeps its characters inside the range.
                open.Length = 0;
                close.Start += close.Length;
                close.Length = 0;
                claims[openIndex] = open;
                claims[closeIndex] = close;
            }
        }

        /// <summary>The parsed ranges of the current text, as editing sees them.</summary>
        internal IReadOnlyList<LetteraMatchContext.RangeRecord> ParsedRanges => m_MatchContext.Ranges;
        internal IReadOnlyList<LetteraStyle> EffectiveStyles
        {
            get
            {
                if (m_StylesUncollected) CollectStyles();
                return m_EffectiveStyles;
            }
        }

        internal bool TryGetDelimiters(int range, out int openStart, out int openLength, out int closeStart, out int closeLength)
        {
            openStart = openLength = closeStart = closeLength = 0;
            if (!DelimitersOf(range, out LetteraMatchContext.Claim open, out LetteraMatchContext.Claim close)) return false;
            openStart = open.Start;
            openLength = open.Length;
            closeStart = close.Start;
            closeLength = close.Length;
            return true;
        }
    }
}

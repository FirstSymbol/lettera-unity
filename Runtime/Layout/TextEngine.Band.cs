using System.Collections.Generic;
using UnityEngine;

namespace FirstSymbol.Lettera
{
    // A long text places glyphs only on the lines near what can show. Every line is broken and measured all the same, so heights, lines and indices
    // cover the whole text; a query on a line outside the band places that line on its own, where the mesh never reads it.
    internal sealed partial class TextEngine
    {
        bool m_HasVisible;                  // The host said what of the text can show, as local y.
        float m_VisibleLow, m_VisibleHigh;
        bool m_Banded;                      // The last placement left lines out.
        float m_BandLow, m_BandHigh;        // The local y the placed lines cover.
        int m_BandFirst, m_BandEnd;         // The placed lines.
        float m_ContentLow, m_ContentHigh;

        struct ExtraLine
        {
            public int Line, Start, End;
        }

        // Lines placed for a query, after everything the mesh reads. The next placement forgets them.
        List<ExtraLine> m_ExtraLines = Rare<ExtraLine>.List;
        int m_ExtraEnd;

        internal bool IsBanded => m_Banded;
        internal int BandFirstLine => m_Banded ? m_BandFirst : 0;
        internal int BandEndLine => m_Banded ? m_BandEnd : m_Lines.Count;

        /// <summary>What decided the band, for a test's message.</summary>
        internal string BandReport =>
            $"visible {m_HasVisible} {m_VisibleLow:0}..{m_VisibleHigh:0}, band {m_BandLow:0}..{m_BandHigh:0}, lines {m_BandFirst}..{m_BandEnd}, estimates {m_EstAdvanceEm:0.000} em {m_EstLineEm:0.000} em {m_EstFill:0.00}";

        /// <summary>Whether a host should say what of this text shows: a text this long may place only part of its lines.</summary>
        internal bool MayBand => LetteraLongText.VisibleLinesOnly && !IsWindowed && (m_Banded || RawChars.Length >= LetteraLongText.FromGlyphs / 2);

        /// <summary>What of the text can show, as local y, or nothing to say all of it may.</summary>
        internal void SetVisible(bool has, float low, float high)
        {
            m_HasVisible = has;
            m_VisibleLow = low;
            m_VisibleHigh = high;
        }

        /// <summary>Whether what shows now lies inside what was placed.</summary>
        internal bool BandCovers(float low, float high)
        {
            if (!m_Banded) return true;
            low = Mathf.Max(low, m_ContentLow);
            high = Mathf.Min(high, m_ContentHigh);
            return low >= high || (low >= m_BandLow && high <= m_BandHigh);
        }

        bool BandApplies =>
            LetteraLongText.VisibleLinesOnly && !m_MarqueeScrolling && !m_Attributes.HasCurve && m_RevealSpans.Count == 0 && m_StageHooks.Count == 0 && m_InlineItems.Count == 0 &&
            (IsWindowed ? !ShowsBars : m_HasVisible && (m_ShapedCount >= LetteraLongText.FromGlyphs || m_PendingCount > 0) && !ClipsViewport);

        // A document's window reaches past its viewport so that a scroll is only an offset; of it, only the lines near the viewport are placed.
        const float WindowBandMargin = 0.25f;

        // The window's viewport, in the window's own y: a scroll moves it by the offset.
        bool WindowBandCovers()
        {
            float top = m_Box.yMax - m_WindowOffset;
            return BandCovers(top - m_Box.height, top);
        }

        void StartBand()
        {
            m_ExtraLines.Clear();
            m_Banded = BandApplies;
            m_BandFirst = int.MaxValue;
            m_BandEnd = 0;
            if (!m_Banded) return;
            if (IsWindowed)
            {
                float top = m_Box.yMax - m_WindowOffset, height = Mathf.Max(m_Box.height, CurrentFontSize * 4);
                m_BandLow = top - height * (1 + WindowBandMargin);
                m_BandHigh = top + height * WindowBandMargin;
                return;
            }
            float shown = Mathf.Max(m_VisibleHigh - m_VisibleLow, CurrentFontSize * 4);
            float margin = shown * Mathf.Max(LetteraLongText.Margin, 0);
            m_BandLow = m_VisibleLow - margin;
            m_BandHigh = m_VisibleHigh + margin;
        }

        bool LineInBand(float top, float bottom, int line)
        {
            if (!m_Banded) return true;
            if (top < m_BandLow || bottom > m_BandHigh) return false;
            if (line < m_BandFirst) m_BandFirst = line;
            if (line + 1 > m_BandEnd) m_BandEnd = line + 1;
            return true;
        }

        void FinishBand(float contentHigh, float contentLow)
        {
            m_ContentHigh = contentHigh;
            m_ContentLow = contentLow;
            if (m_BandFirst > m_BandEnd) m_BandFirst = m_BandEnd = 0;
            m_ExtraEnd = m_PlacedCount;
        }

        bool LinePlaced(int line)
        {
            if (!m_Banded || (line >= m_BandFirst && line < m_BandEnd)) return true;
            for (int i = 0; i < m_ExtraLines.Count; i++)
                if (m_ExtraLines[i].Line == line)
                    return true;
            return false;
        }

        int ExtraGlyphCount => m_ExtraLines.Count > 0 ? m_ExtraEnd - m_PlacedCount : 0;

        bool ExtraRange(int line, out int start, out int end)
        {
            for (int i = 0; i < m_ExtraLines.Count; i++)
            {
                if (m_ExtraLines[i].Line != line) continue;
                start = m_ExtraLines[i].Start;
                end = m_ExtraLines[i].End;
                return true;
            }
            start = end = 0;
            return false;
        }

        // A query reached a line the band left out: it is placed past what the mesh reads, for queries alone.
        void EnsureLinePlaced(int line)
        {
            if (line < 0 || line >= m_Lines.Count || LinePlaced(line)) return;
            if (m_ExtraLines.Count >= 8)
            {
                m_ExtraLines.Clear();
                m_ExtraEnd = m_PlacedCount;
            }
            int keepCount = m_PlacedCount, keepVirtual = m_VirtualGlyphs.Count;
            LetteraLine placed = m_Lines[line];
            Paragraph paragraph = m_Paragraphs[placed.Paragraph];
            bool clips = m_Host.ClipsOverflow || m_MarqueeScrolling || ClipsViewport;
            AlignLine(m_Host.Data, placed, paragraph, clips, out LineStretch stretch);
            m_PlacedCount = m_ExtraEnd;
            PlaceLine(placed, line, paragraph, placed.X, placed.Baseline, stretch);
            Own(ref m_ExtraLines).Add(new ExtraLine { Line = line, Start = m_ExtraEnd, End = m_PlacedCount });
            m_ExtraEnd = m_PlacedCount;
            m_PlacedCount = keepCount;
            if (m_VirtualGlyphs.Count > keepVirtual) m_VirtualGlyphs.RemoveRange(keepVirtual, m_VirtualGlyphs.Count - keepVirtual);
            m_SegmentsValid = false;
        }
    }
}

using System;
using FirstSymbol.Lettera.Unicode;

namespace FirstSymbol.Lettera
{
    // What layout effects add to the shaped widths. Extra space belongs to the first codepoint of its cluster.
    internal sealed partial class TextEngine
    {
        Cells<float> m_CpExtraEm, m_CpExtraUnits;
        Cells<float> m_CpLeadEm, m_CpLeadUnits;
        Cells<bool> m_CpKashida;
        Cells<bool> m_CpDivider;

        // A long text left for later has no layout effect, so all its extras are zero: it keeps none, and reads none.
        bool m_NoExtras;

        void EnsureExtras(int length, int from = 0)
        {
            bool none = m_Deferring;
            // What the arrays held before a text that kept none is not known to be zero.
            if (m_NoExtras) from = 0;
            m_NoExtras = none;
            if (none) return;
            ClearedExtras(length, from);
        }

        // Zeros from a codepoint on. A new block is zeros already, and what stood before that codepoint is zeros on every path that keeps it.
        void ClearedExtras(int length, int from)
        {
            if (m_ExtrasRoom < length)
            {
                AllocateExtras(Math.Max(Math.Max(length, SmallestBuffer), (m_ExtrasRoom + 1) * 2));
                return;
            }
            int count = length + 1 - from;
            m_CpDivider.Clear(from, count);
            m_CpExtraEm.Clear(from, count);
            m_CpExtraUnits.Clear(from, count);
            m_CpLeadEm.Clear(from, count);
            m_CpLeadUnits.Clear(from, count);
            m_CpKashida.Clear(from, count);
        }

        /// <summary>A codepoint's share of the line: its shaped advance, and what effects and a fit add.</summary>
        float CpWidth(int cp, float size)
        {
            if (m_NoExtras) return m_CpAdvanceEm[cp] * m_FitGlyphScale * size;
            float em = m_CpAdvanceEm[cp] * m_FitGlyphScale + m_CpExtraEm[cp];
            if (m_FitTracking != 0 && m_Boundaries[cp]) em += m_FitTracking;
            return em * size + m_CpExtraUnits[cp];
        }

        void ComputeExtras()
        {
            LetteraAttributes.CpLayout[] layout = m_Attributes.Layout;
            for (int p = 0; p < m_Paragraphs.Count; p++)
            {
                Paragraph paragraph = m_Paragraphs[p];
                int i = paragraph.Start;
                while (i < paragraph.ContentEnd)
                {
                    int end = i + 1;
                    while (end < paragraph.ContentEnd && !m_Boundaries[end]) end++;
                    ref LetteraAttributes.CpLayout attr = ref layout[i];

                    bool joined = IsCursive(m_Scripts[i]) && JoinsNext(i, end, paragraph.ContentEnd);
                    bool negative = attr.Tracking.Em < 0 || attr.Tracking.Units < 0;
                    if (!attr.Tracking.IsZero && !(joined && negative))
                    {
                        m_CpExtraEm[i] += attr.Tracking.Em;
                        m_CpExtraUnits[i] += attr.Tracking.Units;
                        m_CpKashida[i] = joined;
                    }
                    if (m_Text[i] == ' ' || m_Text[i] == 0xA0)
                    {
                        m_CpExtraEm[i] += attr.WordSpacing.Em;
                        m_CpExtraUnits[i] += attr.WordSpacing.Units;
                    }
                    i = end;
                }
            }
            ComputeCells();

            ApplyDividers();

            foreach (UnityEngine.Vector2Int range in m_Attributes.NoBreaks)
            {
                for (int c = range.x; c < range.y - 1; c++)
                    if (m_Breaks[c] != LineBreakOpportunity.Mandatory)
                        m_Breaks[c] = LineBreakOpportunity.None;
                if (range.x > 0 && m_Breaks[range.x - 1] != LineBreakOpportunity.Mandatory) m_Breaks[range.x - 1] = LineBreakOpportunity.Allowed;
                if (m_Breaks[range.y - 1] != LineBreakOpportunity.Mandatory) m_Breaks[range.y - 1] = LineBreakOpportunity.Allowed;
            }
        }

        // Lines break only between pieces while they fit. A piece too wide for the box still breaks inside by the emergency rule.
        void ApplyDividers()
        {
            System.Collections.Generic.List<UnityEngine.Vector2Int> dividers = m_Attributes.Dividers;
            if (dividers.Count == 0) return;
            for (int i = 0; i < m_TextLength; i++)
                if (m_Breaks[i] == LineBreakOpportunity.Allowed)
                    m_Breaks[i] = LineBreakOpportunity.None;
            foreach (UnityEngine.Vector2Int divider in dividers)
            {
                m_Breaks[divider.y - 1] = LineBreakOpportunity.Allowed;
                for (int i = divider.x; i < divider.y; i++) m_CpDivider[i] = true;
            }
        }

        // A divider that ends a line hangs out of it, like trailing whitespace.
        bool IsDividerEnd(int end, out int start)
        {
            foreach (UnityEngine.Vector2Int divider in m_Attributes.Dividers)
            {
                if (divider.y != end) continue;
                start = divider.x;
                return true;
            }
            start = end;
            return false;
        }

        // Each cluster's width becomes the cell's, half of the difference on each side. Tracking comes on top.
        void ComputeCells()
        {
            LetteraAttributes.CpLayout[] layout = m_Attributes.Layout;
            int i = 0;
            while (i < m_TextLength)
            {
                ref LetteraAttributes.CpLayout first = ref layout[i];
                if (!first.CellAuto && first.Cell.IsZero)
                {
                    i++;
                    continue;
                }

                float cellEm = first.Cell.Em, cellUnits = first.Cell.Units;
                if (first.CellAuto)
                {
                    float widest = 0;
                    for (int c = i; c < m_TextLength && layout[c].CellGroup == first.CellGroup; c++)
                    {
                        float width = 0;
                        int end = c;
                        do width += m_CpAdvanceEm[end++];
                        while (end < m_TextLength && !m_Boundaries[end]);
                        if (width > widest) widest = width;
                        c = end - 1;
                    }
                    cellEm = widest;
                    cellUnits = 0;
                }

                int group = first.CellGroup;
                LayoutLength cell = first.Cell;
                while (i < m_TextLength && (group != 0 ? layout[i].CellGroup == group : !layout[i].CellAuto && layout[i].Cell.Em == cell.Em && layout[i].Cell.Units == cell.Units))
                {
                    int end = i;
                    float advance = 0;
                    do advance += m_CpAdvanceEm[end++];
                    while (end < m_TextLength && !m_Boundaries[end]);
                    m_CpExtraEm[i] += cellEm - advance;
                    m_CpExtraUnits[i] += cellUnits;
                    m_CpLeadEm[i] = (cellEm - advance) * 0.5f;
                    m_CpLeadUnits[i] = cellUnits * 0.5f;
                    i = end;
                }
            }
        }

        static bool IsCursive(UnicodeScript script) =>
            script == UnicodeScript.Arabic || script == UnicodeScript.Syriac || script == UnicodeScript.Mongolian ||
            script == UnicodeScript.Nko || script == UnicodeScript.Mandaic;

        bool JoinsNext(int cp, int clusterEnd, int paragraphEnd)
        {
            if (clusterEnd >= paragraphEnd) return false;
            if (!IsLetter(m_Text[cp]) || !IsLetter(m_Text[clusterEnd])) return false;
            return !IsRightJoiningOnly(m_Text[cp]);
        }

        static bool IsLetter(int cp) => LetteraUnicode.GetScript(cp) != UnicodeScript.Common && !LetteraUnicode.IsMark(cp) && !LetteraUnicode.IsWhiteSpace(cp) && cp != 0x0621;

        // Arabic letters that connect to the letter before them only (or to none), from the Unicode joining types.
        static bool IsRightJoiningOnly(int cp) =>
            (cp >= 0x0622 && cp <= 0x0625) || cp == 0x0627 || cp == 0x0629 || (cp >= 0x062F && cp <= 0x0632) || cp == 0x0648 ||
            (cp >= 0x0671 && cp <= 0x0673) || (cp >= 0x0675 && cp <= 0x0677) || (cp >= 0x0688 && cp <= 0x0699) || cp == 0x06C0 ||
            (cp >= 0x06C3 && cp <= 0x06CB) || cp == 0x06CD || cp == 0x06CF || cp == 0x06D2 || cp == 0x06D3 || cp == 0x06D5 || cp == 0x06EE || cp == 0x06EF;

        // The tracking of the cluster that ends a line hangs out of it.
        float TrailingTrackingOf(int start, int visibleEnd, float baseSize)
        {
            if (visibleEnd <= start || visibleEnd > m_TextLength) return 0;
            int cp = visibleEnd - 1;
            while (cp > start && !m_Boundaries[cp]) cp--;
            if (IsRemoved(cp)) return 0;
            float size = SizeOf(m_Runs[m_CpRun[cp]], baseSize);
            float amount = m_FitTracking * size;
            if (m_Attributes.HasLayout)
            {
                ref LetteraAttributes.CpLayout attr = ref m_Attributes.Layout[cp];
                bool joinedNegative = (attr.Tracking.Em < 0 || attr.Tracking.Units < 0) && IsCursive(m_Scripts[cp]) && m_CpExtraEm[cp] == 0 && m_CpExtraUnits[cp] == 0;
                if (!joinedNegative) amount += attr.Tracking.Of(size);
            }
            return amount;
        }
    }
}

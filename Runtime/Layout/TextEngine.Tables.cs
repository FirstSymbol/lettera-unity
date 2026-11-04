using UnityEngine;

namespace FirstSymbol.Lettera
{
    // LaTeX tables in the flow: each is an inline item whose box depends on the room the text gives it.
    internal sealed partial class TextEngine
    {
        /// <summary>The size a table's room is counted in, before layout has chosen one.</summary>
        internal float TableSize => m_Host.Data.AutoSize && CurrentFontSize > 0 ? CurrentFontSize : m_Host.Data.FontSize;

        /// <summary>The text's width in em of a size: what a table may take. Infinity when the text has no width.</summary>
        internal float TableRoomEm(float size)
        {
            Rect box = ComputeBox(m_Host.Data.Padding, HostRect);
            return box.width > 0 ? box.width / Mathf.Max(size, 0.01f) : float.PositiveInfinity;
        }

        // A width or a size other than the one a table was laid for lays it again, and the line sees its new width through its extras.
        void RefitTables(float size, float width)
        {
            float room = width > 0 && !float.IsInfinity(width) ? width / Mathf.Max(size, 0.01f) : float.PositiveInfinity;
            for (int i = 0; i < m_Aside.InlineItems.Count; i++)
            {
                InlineItem item = m_Aside.InlineItems[i];
                if (item.Table == null || !(item.Effect is LetteraTableEffect effect)) continue;
                bool same = float.IsInfinity(room) ? float.IsInfinity(item.TableRoom) : Mathf.Abs(item.TableRoom - room) < 1f / 64;
                if (same) continue;
                FontFace text = PrimaryFaceForMath;
                if (text == null || text.IsVirtual) continue;
                float before = item.Advance;
                effect.Fit(ref item, text, room);
                if (!m_NoExtras && item.Cp < m_TextLength) m_CpExtraEm[item.Cp] += item.Advance - before;
                m_Aside.InlineItems[i] = item;
            }
        }
    }
}

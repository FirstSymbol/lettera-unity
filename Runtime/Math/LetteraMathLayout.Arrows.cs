#if !LETTERA_NO_MATH
using UnityEngine;

namespace FirstSymbol.Lettera
{
    internal sealed partial class LetteraMathBuilder
    {
        internal const int PairHarpoons = 1, PairArrows = 2, PairLongRight = 3, PairLongLeft = 4, PairBothWays = 5;

        // Two arrows one over the other, as chemistry's equilibria: each as long as asked, the shorter one of an unbalanced pair at 60%.
        // A font with no longer form of a harpoon gets its shaft lengthened by a rule, so the pair always spans its texts.
        Box ArrowPair(int kind, int fallback, float need, MathStyle style)
        {
            // A two-headed arrow is a right and a left one on one shaft, since fonts seldom hold a longer form of it.
            bool arrows = kind == PairArrows || kind == PairBothWays;
            int upper = arrows ? 0x2192 : 0x21C0, lower = arrows ? 0x2190 : 0x21BD;
            if (FindGlyph(upper, 0).Glyph == 0 || FindGlyph(lower, 0).Glyph == 0) return StretchTo(fallback, false, need, style);
            int start = m_Items.Count;
            float scale = ScaleOf(style);
            float gap = (kind == PairBothWays ? 0 : arrows ? 0.32f : 0.2f) * scale;
            Box top = Shaft(upper, kind == PairLongLeft ? need * 0.6f : need, style, arrows ? 0 : 1, out float topShaft);
            Move(top.First, top.End, 0, gap / 2 - topShaft);
            float topLow = top.InkLow + gap / 2 - topShaft, topHigh = top.InkHigh + gap / 2 - topShaft;
            Box bottom = Shaft(lower, kind == PairLongRight ? need * 0.6f : need, style, arrows ? 0 : -1, out float bottomShaft);
            Move(bottom.First, bottom.End, 0, -gap / 2 - bottomShaft);
            float bottomLow = bottom.InkLow - gap / 2 - bottomShaft, bottomHigh = bottom.InkHigh - gap / 2 - bottomShaft;
            float width = Mathf.Max(top.Width, bottom.Width);
            Move(top.First, top.End, (width - top.Width) / 2, 0);
            Move(bottom.First, bottom.End, (width - bottom.Width) / 2, 0);
            float high = Mathf.Max(topHigh, bottomHigh), low = Mathf.Min(topLow, bottomLow);
            return new Box { Width = width, Ascent = Mathf.Max(0, high), Descent = Mathf.Max(0, -low), InkLow = low, InkHigh = high, First = start, End = m_Items.Count };
        }

        // One arrow or harpoon at a length, with the height of its shaft: in the middle of an arrow's ink, at the bottom of a barb-up harpoon's, at the top of a barb-down one's.
        Box Shaft(int cp, float length, MathStyle style, int barb, out float shaft)
        {
            float scale = ScaleOf(style);
            GlyphRef glyph = FindGlyph(cp, 0);
            (float _, float minY, float _, float maxY, float _) = glyph.Font.Extents(glyph.Glyph);
            float low = minY * scale, high = maxY * scale;
            float t = C(MathConstant.FractionRuleThickness, style);
            shaft = barb > 0 ? low + t / 2 : barb < 0 ? high - t / 2 : (low + high) / 2;
            Box box = StretchTo(cp, false, length, style);
            box.InkLow = low;
            box.InkHigh = high;
            float extra = length - box.Width;
            if (extra <= 0.001f) return box;
            // The head stays at its end and a rule runs the rest of the way, into the glyph's own shaft.
            float overlap = Mathf.Min(0.15f * scale, box.Width / 3);
            bool right = cp == 0x2192 || cp == 0x21C0;
            if (right)
            {
                Move(box.First, box.End, extra, 0);
                Rule(0, shaft - t / 2, extra + overlap, t, false);
            }
            else Rule(box.Width - overlap, shaft - t / 2, extra + overlap, t, false);
            box.Width = length;
            box.End = m_Items.Count;
            return box;
        }
    }
}
#endif

using System;

namespace FirstSymbol.Lettera.Unicode
{
    public static class GraphemeBreaker
    {
        public static void FindBoundaries(ReadOnlySpan<int> text, Span<bool> boundaries)
        {
            int n = Math.Min(text.Length, boundaries.Length - 1);
            if (n <= 0)
            {
                if (boundaries.Length > 0) boundaries[0] = false;
                return;
            }

            boundaries[0] = true;
            boundaries[n] = true;
            var prev = UnicodeData.Gcb(text[0]);
            int regional = 0, picto = 0, incb = 0; // picto: 1 = ExtPict Extend*, 2 = + ZWJ. incb: 1 = consonant, 2 = + linker.

            for (int i = 1; i < n; i++)
            {
                int prevCp = text[i - 1], cp = text[i];
                var cur = UnicodeData.Gcb(cp);

                regional = prev == GraphemeBreakClass.RegionalIndicator ? regional + 1 : 0;

                if (UnicodeData.Flag(prevCp, UnicodeData.FExtPict)) picto = 1;
                else if (picto == 1 && prev == GraphemeBreakClass.Extend) { }
                else if (picto == 1 && prev == GraphemeBreakClass.ZWJ) picto = 2;
                else picto = 0;

                var prevInCB = UnicodeData.InCB(prevCp);
                if (prevInCB == IndicConjunctBreak.Consonant) incb = 1;
                else if (incb != 0 && prevInCB == IndicConjunctBreak.Linker) incb = 2;
                else if (incb != 0 && prevInCB != IndicConjunctBreak.Extend) incb = 0;

                boundaries[i] = IsBreak(prev, cur, regional, picto, incb, cp);
                prev = cur;
            }
        }

        static bool IsBreak(GraphemeBreakClass a, GraphemeBreakClass b, int regional, int picto, int incb, int cp)
        {
            if (a == GraphemeBreakClass.CR && b == GraphemeBreakClass.LF) return false; // GB3
            if (a == GraphemeBreakClass.Control || a == GraphemeBreakClass.CR || a == GraphemeBreakClass.LF) return true; // GB4
            if (b == GraphemeBreakClass.Control || b == GraphemeBreakClass.CR || b == GraphemeBreakClass.LF) return true; // GB5
            if (a == GraphemeBreakClass.L && (b == GraphemeBreakClass.L || b == GraphemeBreakClass.V || b == GraphemeBreakClass.LV || b == GraphemeBreakClass.LVT)) return false; // GB6
            if ((a == GraphemeBreakClass.LV || a == GraphemeBreakClass.V) && (b == GraphemeBreakClass.V || b == GraphemeBreakClass.T)) return false; // GB7
            if ((a == GraphemeBreakClass.LVT || a == GraphemeBreakClass.T) && b == GraphemeBreakClass.T) return false; // GB8
            if (b == GraphemeBreakClass.Extend || b == GraphemeBreakClass.ZWJ) return false; // GB9
            if (b == GraphemeBreakClass.SpacingMark) return false; // GB9a
            if (a == GraphemeBreakClass.Prepend) return false; // GB9b
            if (incb == 2 && UnicodeData.InCB(cp) == IndicConjunctBreak.Consonant) return false; // GB9c
            if (picto == 2 && UnicodeData.Flag(cp, UnicodeData.FExtPict)) return false; // GB11
            if (a == GraphemeBreakClass.RegionalIndicator && b == GraphemeBreakClass.RegionalIndicator && (regional & 1) == 1) return false; // GB12, GB13
            return true; // GB999
        }
    }
}

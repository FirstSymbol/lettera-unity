using System;
using W = FirstSymbol.Lettera.Unicode.WordBreakClass;

namespace FirstSymbol.Lettera.Unicode
{
    public static class WordBreaker
    {
        const W None = (W)255;

        static bool IsNewline(W c) => c == W.CR || c == W.LF || c == W.Newline;
        static bool IsIgnored(W c) => c == W.Extend || c == W.Format || c == W.ZWJ;
        static bool IsAH(W c) => c == W.ALetter || c == W.HebrewLetter;
        static bool IsMidNumLetQ(W c) => c == W.MidNumLet || c == W.SingleQuote;

        static W NextBase(ReadOnlySpan<int> text, int i, int n)
        {
            for (; i < n; i++)
            {
                var c = UnicodeData.Wb(text[i]);
                if (!IsIgnored(c)) return c;
            }
            return None;
        }

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
            W raw = UnicodeData.Wb(text[0]);
            W a = raw, before = None; // WB4-collapsed previous two classes
            int regional = a == W.RegionalIndicator ? 1 : 0;

            for (int i = 1; i < n; i++)
            {
                W c = UnicodeData.Wb(text[i]);
                bool brk;
                if (raw == W.CR && c == W.LF) brk = false; // WB3
                else if (IsNewline(raw) || IsNewline(c)) brk = true; // WB3a, WB3b
                else if (raw == W.ZWJ && UnicodeData.Flag(text[i], UnicodeData.FExtPict)) brk = false; // WB3c
                else if (raw == W.WSegSpace && c == W.WSegSpace) brk = false; // WB3d
                else if (IsIgnored(c)) { boundaries[i] = false; raw = c; continue; } // WB4
                else brk = IsBreak(a, before, c, regional, text, i + 1, n);

                boundaries[i] = brk;
                raw = c;
                // X (Extend | Format | ZWJ)* collapses to X, except after sot, CR, LF, Newline where the character stands alone.
                before = a;
                a = c;
                regional = c == W.RegionalIndicator ? regional + 1 : 0;
            }
        }

        static bool IsBreak(W a, W before, W b, int regional, ReadOnlySpan<int> text, int next, int n)
        {
            if (IsAH(a) && IsAH(b)) return false; // WB5
            if (IsAH(a) && (b == W.MidLetter || IsMidNumLetQ(b)) && IsAH(NextBase(text, next, n))) return false; // WB6
            if (IsAH(before) && (a == W.MidLetter || IsMidNumLetQ(a)) && IsAH(b)) return false; // WB7
            if (a == W.HebrewLetter && b == W.SingleQuote) return false; // WB7a
            if (a == W.HebrewLetter && b == W.DoubleQuote && NextBase(text, next, n) == W.HebrewLetter) return false; // WB7b
            if (before == W.HebrewLetter && a == W.DoubleQuote && b == W.HebrewLetter) return false; // WB7c
            if (a == W.Numeric && b == W.Numeric) return false; // WB8
            if (IsAH(a) && b == W.Numeric) return false; // WB9
            if (a == W.Numeric && IsAH(b)) return false; // WB10
            if (before == W.Numeric && (a == W.MidNum || IsMidNumLetQ(a)) && b == W.Numeric) return false; // WB11
            if (a == W.Numeric && (b == W.MidNum || IsMidNumLetQ(b)) && NextBase(text, next, n) == W.Numeric) return false; // WB12
            if (a == W.Katakana && b == W.Katakana) return false; // WB13
            if ((IsAH(a) || a == W.Numeric || a == W.Katakana || a == W.ExtendNumLet) && b == W.ExtendNumLet) return false; // WB13a
            if (a == W.ExtendNumLet && (IsAH(b) || b == W.Numeric || b == W.Katakana)) return false; // WB13b
            if (a == W.RegionalIndicator && b == W.RegionalIndicator && (regional & 1) == 1) return false; // WB15, WB16
            return true; // WB999
        }
    }
}

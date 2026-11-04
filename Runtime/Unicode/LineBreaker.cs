using System;
using C = FirstSymbol.Lettera.Unicode.LineBreakClass;

namespace FirstSymbol.Lettera.Unicode
{
    public enum LineBreakOpportunity : byte { None, Allowed, Mandatory }

    /// <summary>UAX #14 default algorithm, Unicode 18 rules. Rules are evaluated per unit, a unit being a base plus its LB9 CM/ZWJ.</summary>
    public static class LineBreaker
    {
        const C Sot = (C)255;
        const int DottedCircle = 0x25CC;

        // State of the unit before the position being evaluated.
        struct Prior
        {
            public C Cls, Before;       // classes of the last two units (Before may be Sot)
            public int Cp;              // base code point ('A' for an LB10 CM)
            public bool LastIsZwj;      // LB8a
            public bool EastAsian, BeforeEastAsian, BeforeIsAksara;
            public bool ZwRun, OpRun, PiRun, ClRun, B2Run; // "X SP*" contexts
            public int Num;             // LB25: 1 = NU (SY|IS)*, 2 = NU (SY|IS)* (CL|CP)
            public int Regional;        // LB30a: count of consecutive RI units
        }

        // LB1
        static C Resolve(int cp)
        {
            var c = UnicodeData.Lb(cp);
            switch (c)
            {
                case C.AI: case C.SG: case C.XX: return C.AL;
                case C.CJ: return C.NS;
                case C.SA:
                    var g = UnicodeData.Gc(cp);
                    return g == GeneralCategory.Mn || g == GeneralCategory.Mc ? C.CM : C.AL;
                default: return c;
            }
        }

        static bool IsEastAsian(int cp)
        {
            var e = UnicodeData.Ea(cp);
            return e == EastAsianWidth.F || e == EastAsianWidth.W || e == EastAsianWidth.H;
        }

        static bool IsCombining(C c) => c == C.CM || c == C.ZWJ;
        static bool IsHardBreak(C c) => c == C.BK || c == C.CR || c == C.LF || c == C.NL;
        static bool IsAksara(C c, int cp) => c == C.AK || c == C.AS || cp == DottedCircle;
        static bool IsPi(int cp) => UnicodeData.Gc(cp) == GeneralCategory.Pi;
        static bool IsPf(int cp) => UnicodeData.Gc(cp) == GeneralCategory.Pf;

        // Finds the unit following the one starting at i. Returns its base index, or n at eot.
        static int NextUnit(ReadOnlySpan<int> text, int i, int n)
        {
            for (i++; i < n; i++)
                if (!IsCombining(Resolve(text[i]))) break;
            return i;
        }

        public static void FindBreaks(ReadOnlySpan<int> text, Span<LineBreakOpportunity> result)
        {
            int n = Math.Min(text.Length, result.Length);
            if (n <= 0) return;

            var s = new Prior { Before = Sot };
            Advance(ref s, Resolve(text[0]), text[0], Sot);

            for (int i = 1; i < n; i++)
            {
                int cp = text[i];
                C b = Resolve(cp);
                bool combining = IsCombining(b);
                bool absorbed = combining && !IsHardBreak(s.Cls) && s.Cls != C.SP && s.Cls != C.ZW; // LB9
                result[i - 1] = Decide(in s, b, cp, absorbed, text, i, n);
                if (absorbed) s.LastIsZwj = b == C.ZWJ;
                else Advance(ref s, b, cp, s.Cls);
            }
            result[n - 1] = LineBreakOpportunity.Mandatory; // LB3
        }

        static void Advance(ref Prior s, C b, int cp, C a)
        {
            bool zwj = b == C.ZWJ;
            if (IsCombining(b)) { b = C.AL; cp = 'A'; } // LB10
            bool sp = b == C.SP;
            bool ea = IsEastAsian(cp);

            s.ZwRun = b == C.ZW || (sp && s.ZwRun);
            s.OpRun = b == C.OP || (sp && s.OpRun);
            bool piStart = b == C.QU && IsPi(cp) && (a == Sot || IsHardBreak(a) || a == C.OP || a == C.QU || a == C.GL || a == C.SP || a == C.ZW); // LB15a
            s.PiRun = piStart || (sp && s.PiRun);
            s.ClRun = b == C.CL || b == C.CP || (sp && s.ClRun);
            s.B2Run = b == C.B2 || (sp && s.B2Run);
            s.Num = b == C.NU ? 1
                : s.Num == 1 && (b == C.SY || b == C.IS) ? 1
                : s.Num == 1 && (b == C.CL || b == C.CP) ? 2 : 0;
            s.Regional = b == C.RI ? s.Regional + 1 : 0;

            s.Before = a;
            s.BeforeEastAsian = s.EastAsian;
            s.BeforeIsAksara = a != Sot && IsAksara(a, s.Cp);
            s.Cls = b;
            s.Cp = cp;
            s.EastAsian = ea;
            s.LastIsZwj = zwj;
        }

        static LineBreakOpportunity Decide(in Prior s, C b, int bCp, bool absorbed, ReadOnlySpan<int> text, int i, int n)
        {
            const LineBreakOpportunity No = LineBreakOpportunity.None, Yes = LineBreakOpportunity.Allowed, Must = LineBreakOpportunity.Mandatory;
            C a = s.Cls;
            int aCp = s.Cp;

            if (a == C.BK) return Must; // LB4
            if (a == C.CR && b == C.LF) return No; // LB5
            if (a == C.CR || a == C.LF || a == C.NL) return Must;
            if (IsHardBreak(b)) return No; // LB6
            if (b == C.SP || b == C.ZW) return No; // LB7
            if (s.ZwRun) return Yes; // LB8
            if (s.LastIsZwj) return No; // LB8a
            if (absorbed) return No; // LB9
            if (IsCombining(b)) { b = C.AL; bCp = 'A'; } // LB10

            if (b == C.WJ || a == C.WJ) return No; // LB11
            if (a == C.GL) return No; // LB12
            if (b == C.GL && a != C.SP && a != C.HY && a != C.HH) return No; // LB12a
            if (b == C.CL || b == C.CP || b == C.EX || b == C.SY) return No; // LB13
            if (s.OpRun) return No; // LB14
            if (s.PiRun) return No; // LB15a

            // The unit after b, only looked at when one of the rules below needs it.
            int nextIndex = -1;
            C next = Sot;

            if (b == C.QU || b == C.IS || b == C.OP || b == C.AK || b == C.AS || bCp == DottedCircle)
            {
                nextIndex = NextUnit(text, i, n);
                next = nextIndex < n ? Resolve(text[nextIndex]) : Sot;
            }

            if (b == C.QU && IsPf(bCp) && (next == Sot || next == C.SP || next == C.GL || next == C.WJ || next == C.CL || next == C.QU || next == C.CP
                || next == C.EX || next == C.IS || next == C.SY || IsHardBreak(next) || next == C.ZW)) return No; // LB15b
            if (a == C.SP && b == C.IS && next == C.NU) return Yes; // LB15c
            if (b == C.IS) return No; // LB15d
            if (s.ClRun && b == C.NS) return No; // LB16
            if (s.B2Run && b == C.B2) return No; // LB17
            if (a == C.SP) return Yes; // LB18

            if (b == C.QU && !IsPi(bCp)) return No; // LB19
            if (a == C.QU && !IsPf(aCp)) return No;

            bool bEA = IsEastAsian(bCp);
            if (b == C.QU && (!s.EastAsian || next == Sot || !IsEastAsian(text[nextIndex]))) return No; // LB19a
            if (a == C.QU && (!bEA || s.Before == Sot || !s.BeforeEastAsian)) return No;

            if (a == C.CB || b == C.CB) return Yes; // LB20
            if ((a == C.HY || a == C.HH) && (b == C.AL || b == C.HL) && (s.Before == Sot || IsHardBreak(s.Before) || s.Before == C.SP || s.Before == C.ZW
                || s.Before == C.CB || s.Before == C.GL)) return No; // LB20a
            if (b == C.BA || b == C.HH || b == C.HY || b == C.NS) return No; // LB21
            if (a == C.BB) return No;
            if (s.Before == C.HL && (a == C.HY || a == C.HH) && b != C.HL) return No; // LB21a
            if (a == C.SY && b == C.HL) return No; // LB21b
            if (b == C.IN) return No; // LB22

            bool aLetter = a == C.AL || a == C.HL, bLetter = b == C.AL || b == C.HL;
            if (aLetter && b == C.NU) return No; // LB23
            if (a == C.NU && bLetter) return No;
            if (a == C.PR && (b == C.ID || b == C.EB || b == C.EM)) return No; // LB23a
            if ((a == C.ID || a == C.EB || a == C.EM) && b == C.PO) return No;
            if ((a == C.PR || a == C.PO) && bLetter) return No; // LB24
            if (aLetter && (b == C.PR || b == C.PO)) return No;

            // LB25
            if (s.Num != 0 && (b == C.PO || b == C.PR)) return No;
            if ((a == C.PO || a == C.PR) && b == C.NU) return No;
            if ((a == C.PO || a == C.PR) && b == C.OP)
            {
                if (next == C.NU) return No;
                if (next == C.IS)
                {
                    int after = NextUnit(text, nextIndex, n);
                    if (after < n && Resolve(text[after]) == C.NU) return No;
                }
            }
            if ((a == C.HY || a == C.IS) && b == C.NU) return No;
            if (s.Num == 1 && b == C.NU) return No;

            // LB26
            if (a == C.JL && (b == C.JL || b == C.JV || b == C.H2 || b == C.H3)) return No;
            if ((a == C.JV || a == C.H2) && (b == C.JV || b == C.JT)) return No;
            if ((a == C.JT || a == C.H3) && b == C.JT) return No;
            // LB27
            if ((a == C.JL || a == C.JV || a == C.JT || a == C.H2 || a == C.H3) && b == C.PO) return No;
            if (a == C.PR && (b == C.JL || b == C.JV || b == C.JT || b == C.H2 || b == C.H3)) return No;

            if (aLetter && bLetter) return No; // LB28

            // LB28a
            bool aAks = IsAksara(a, aCp), bAks = IsAksara(b, bCp);
            if (a == C.AP && bAks) return No;
            if (aAks && (b == C.VF || b == C.VI)) return No;
            if (a == C.VI && s.BeforeIsAksara && (b == C.AK || bCp == DottedCircle)) return No;
            if (aAks && bAks && next == C.VF) return No;

            if (a == C.IS && bLetter) return No; // LB29
            if ((aLetter || a == C.NU) && b == C.OP && !bEA) return No; // LB30
            if (a == C.CP && !s.EastAsian && (bLetter || b == C.NU)) return No;
            if (a == C.RI && b == C.RI && (s.Regional & 1) == 1) return No; // LB30a
            if (b == C.EM && (a == C.EB || (UnicodeData.Flag(aCp, UnicodeData.FExtPict) && UnicodeData.Gc(aCp) == GeneralCategory.Cn))) return No; // LB30b
            return Yes; // LB31
        }
    }
}

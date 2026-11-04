using System;

namespace FirstSymbol.Lettera.Unicode
{
    public static class LetteraUnicode
    {
        public const string Version = "18.0.0";

        public static GeneralCategory GetCategory(int cp) => UnicodeData.Gc(cp);
        public static UnicodeScript GetScript(int cp) => (UnicodeScript)UnicodeData.Script(cp);

        public static bool HasScriptExtension(int cp, UnicodeScript s)
        {
            int set = UnicodeData.ScxSet(cp);
            return set == 0 ? UnicodeData.Script(cp) == (int)s : UnicodeData.ScxContains(set, (int)s);
        }

        public static BidiClass GetBidiClass(int cp) => UnicodeData.Bidi(cp);
        public static int GetMirror(int cp) => UnicodeData.Mirror(cp);
        public static BidiPairedBracketType GetPairedBracketType(int cp) => UnicodeData.Bracket(cp);
        public static int GetPairedBracket(int cp) => UnicodeData.Bracket(cp) == BidiPairedBracketType.None ? cp : UnicodeData.Mirror(cp);

        public static LineBreakClass GetLineBreakClass(int cp) => UnicodeData.Lb(cp);
        public static GraphemeBreakClass GetGraphemeBreakClass(int cp) => UnicodeData.Gcb(cp);
        public static WordBreakClass GetWordBreakClass(int cp) => UnicodeData.Wb(cp);
        public static EastAsianWidth GetEastAsianWidth(int cp) => UnicodeData.Ea(cp);
        public static IndicConjunctBreak GetIndicConjunctBreak(int cp) => UnicodeData.InCB(cp);

        public static bool IsExtendedPictographic(int cp) => UnicodeData.Flag(cp, UnicodeData.FExtPict);
        public static bool IsEmoji(int cp) => UnicodeData.Flag(cp, UnicodeData.FEmoji);
        public static bool IsEmojiPresentation(int cp) => UnicodeData.Flag(cp, UnicodeData.FEmojiPres);
        public static bool IsEmojiModifier(int cp) => UnicodeData.Flag(cp, UnicodeData.FEmojiMod);
        public static bool IsEmojiModifierBase(int cp) => UnicodeData.Flag(cp, UnicodeData.FEmojiBase);
        public static bool IsRegionalIndicator(int cp) => UnicodeData.Flag(cp, UnicodeData.FRegional);
        public static bool IsDefaultIgnorable(int cp) => UnicodeData.Flag(cp, UnicodeData.FDefaultIgnorable);
        public static bool IsWhiteSpace(int cp) => UnicodeData.Flag(cp, UnicodeData.FWhiteSpace);

        public static bool IsMark(int cp)
        {
            var c = UnicodeData.Gc(cp);
            return c == GeneralCategory.Mn || c == GeneralCategory.Mc || c == GeneralCategory.Me;
        }

        public static int ToUpper(int cp) => UnicodeData.Case(cp, 0);
        public static int ToLower(int cp) => UnicodeData.Case(cp, 1);
        public static int ToTitle(int cp) => UnicodeData.Case(cp, 2);

        public static bool IsJustifiableSeparator(int cp) =>
            cp == '\t' || (UnicodeData.Gc(cp) == GeneralCategory.Zs && cp != 0x00A0 && cp != 0x202F && cp != 0x2007);

        public static uint ScriptToOpenTypeTag(UnicodeScript s)
        {
            var tags = UnicodeData.ScriptTags;
            return (uint)s < (uint)tags.Length ? tags[(int)s] : tags[(int)UnicodeScript.Unknown];
        }

        public static int Utf16ToUtf32(ReadOnlySpan<char> s, Span<int> output, Span<int> utf16IndexOfEach)
        {
            int count = 0;
            for (int i = 0; i < s.Length && count < output.Length; i++)
            {
                int start = i, cp = s[i];
                if (cp >= 0xD800 && cp <= 0xDFFF)
                {
                    if (cp <= 0xDBFF && i + 1 < s.Length && s[i + 1] >= 0xDC00 && s[i + 1] <= 0xDFFF)
                        cp = 0x10000 + ((cp - 0xD800) << 10) + (s[++i] - 0xDC00);
                    else
                        cp = 0xFFFD;
                }
                output[count] = cp;
                if (count < utf16IndexOfEach.Length) utf16IndexOfEach[count] = start;
                count++;
            }
            return count;
        }
    }
}

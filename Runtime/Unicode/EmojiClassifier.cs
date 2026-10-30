using System;

namespace FirstSymbol.Lettera.Unicode
{
    public static class EmojiClassifier
    {
        const int VS15 = 0xFE0E, VS16 = 0xFE0F, Keycap = 0x20E3, Zwj = 0x200D, BlackFlag = 0x1F3F4, CancelTag = 0xE007F;

        public static bool IsEmojiCluster(ReadOnlySpan<int> text, int start, int length)
        {
            if (start < 0) { length += start; start = 0; }
            if (start >= text.Length || length <= 0) return false;
            var c = text.Slice(start, Math.Min(length, text.Length - start));

            int b = 0;
            while (b < c.Length - 1 && UnicodeData.Gcb(c[b]) == GraphemeBreakClass.Prepend) b++;
            int cp = c[b];
            int after = b + 1 < c.Length ? c[b + 1] : -1;

            // Tag sequence
            if (cp == BlackFlag && c.Length - b >= 3 && c[c.Length - 1] == CancelTag)
            {
                bool tags = true;
                for (int i = b + 1; i < c.Length - 1; i++)
                    tags &= c[i] >= 0xE0020 && c[i] <= 0xE007E;
                if (tags) return true;
            }

            // Keycap sequence
            if ((cp >= '0' && cp <= '9') || cp == '#' || cp == '*')
            {
                if (after == Keycap || (after == VS16 && b + 2 < c.Length && c[b + 2] == Keycap)) return true;
            }

            // ZWJ joining pictographs; modifier after a modifier base.
            for (int i = b + 1; i < c.Length; i++)
            {
                if (c[i] == Zwj && i + 1 < c.Length && UnicodeData.Flag(c[i + 1], UnicodeData.FExtPict))
                {
                    int p = i - 1;
                    if (p > b && (c[p] == VS16 || UnicodeData.Flag(c[p], UnicodeData.FEmojiMod))) p--;
                    if (UnicodeData.Flag(c[p], UnicodeData.FExtPict)) return true;
                }
                if (UnicodeData.Flag(c[i], UnicodeData.FEmojiMod) && UnicodeData.Flag(c[i - 1], UnicodeData.FEmojiBase)) return true;
            }

            if (after == VS15) return false;
            if (after == VS16) return UnicodeData.Flag(cp, UnicodeData.FExtPict) || UnicodeData.Flag(cp, UnicodeData.FEmoji);
            if (UnicodeData.Flag(cp, UnicodeData.FRegional)) return true; // Emoji_Presentation; pair or lone
            return UnicodeData.Flag(cp, UnicodeData.FEmojiPres);
        }
    }
}

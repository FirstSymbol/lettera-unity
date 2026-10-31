using System;

namespace FirstSymbol.Lettera.Unicode
{
    /// <summary>
    /// Caret stops in a text of codepoints: grapheme clusters, words and paragraphs. Only the paragraph around an index is
    /// looked at, so the cost does not grow with the text. For use on the main thread.
    /// </summary>
    public static class LetteraTextNavigator
    {
        static bool[] s_Boundaries = new bool[256];

        public static bool IsParagraphSeparator(int cp) => cp == '\n' || cp == '\r' || cp == 0x85 || cp == 0x2029;

        /// <summary>The paragraph around an index. Its end includes the separator that ends it.</summary>
        public static void ParagraphAt(ReadOnlySpan<int> text, int index, out int start, out int contentEnd, out int end)
        {
            index = Clamp(index, text.Length);
            start = index;
            while (start > 0 && !IsParagraphSeparator(text[start - 1])) start--;
            contentEnd = index;
            while (contentEnd < text.Length && !IsParagraphSeparator(text[contentEnd])) contentEnd++;
            end = contentEnd;
            if (end < text.Length)
            {
                end++;
                if (text[end - 1] == '\r' && end < text.Length && text[end] == '\n') end++;
            }
        }

        static int Clamp(int index, int length) => index < 0 ? 0 : index > length ? length : index;

        static Span<bool> GraphemesOf(ReadOnlySpan<int> text, int start, int end)
        {
            int count = end - start;
            if (s_Boundaries.Length < count + 1) s_Boundaries = new bool[Math.Max(count + 1, s_Boundaries.Length * 2)];
            var span = new Span<bool>(s_Boundaries, 0, count + 1);
            span.Clear();
            GraphemeBreaker.FindBoundaries(text.Slice(start, count), span);
            return span;
        }

        /// <summary>The next grapheme cluster boundary after an index.</summary>
        public static int NextStop(ReadOnlySpan<int> text, int index)
        {
            index = Clamp(index, text.Length);
            if (index >= text.Length) return text.Length;
            ParagraphAt(text, index, out int start, out _, out int end);
            Span<bool> boundaries = GraphemesOf(text, start, end);
            for (int i = index + 1; i < end; i++)
                if (boundaries[i - start])
                    return i;
            return end;
        }

        /// <summary>The nearest grapheme cluster boundary before an index.</summary>
        public static int PreviousStop(ReadOnlySpan<int> text, int index)
        {
            index = Clamp(index, text.Length);
            if (index <= 0) return 0;
            ParagraphAt(text, index - 1, out int start, out _, out int end);
            Span<bool> boundaries = GraphemesOf(text, start, end);
            for (int i = index - 1; i > start; i--)
                if (boundaries[i - start])
                    return i;
            return start;
        }

        /// <summary>An index moved onto a boundary: true when it is one already, or at either end of the text.</summary>
        public static bool IsStop(ReadOnlySpan<int> text, int index)
        {
            if (index <= 0 || index >= text.Length) return true;
            ParagraphAt(text, index, out int start, out _, out int end);
            if (index == start) return true;
            return GraphemesOf(text, start, end)[index - start];
        }

        static bool IsSpace(int cp) => LetteraUnicode.IsWhiteSpace(cp) || IsParagraphSeparator(cp);

        static bool IsUnspaced(int cp)
        {
            UnicodeScript script = LetteraUnicode.GetScript(cp);
            return script == UnicodeScript.Thai || script == UnicodeScript.Lao || script == UnicodeScript.Khmer || script == UnicodeScript.Myanmar;
        }

        static bool IsPunctuationLike(int cp) => cp < 0x10000 && (char.IsPunctuation((char)cp) || char.IsSymbol((char)cp));

        // Word boundaries of a paragraph, with what a person calls one word joined: scripts written without spaces as whole runs until
        // a dictionary can split them, and whitespace or repeats of one punctuation mark as one unit.
        static Span<bool> WordsOf(ReadOnlySpan<int> text, int start, int end)
        {
            int count = end - start;
            if (s_Boundaries.Length < count + 1) s_Boundaries = new bool[Math.Max(count + 1, s_Boundaries.Length * 2)];
            var span = new Span<bool>(s_Boundaries, 0, count + 1);
            span.Clear();
            WordBreaker.FindBoundaries(text.Slice(start, count), span);
            for (int i = start + 1; i < end; i++)
            {
                if (!span[i - start]) continue;
                int a = text[i - 1], b = text[i];
                if (IsParagraphSeparator(a) || IsParagraphSeparator(b)) continue;
                if ((IsSpace(a) && IsSpace(b)) || (IsUnspaced(a) && IsUnspaced(b)) || (a == b && IsPunctuationLike(a))) span[i - start] = false;
            }
            // Where a dictionary knows a script's words, they part what the line above joined.
            if (LetteraWordSegmenters.Count != 0)
            {
                if (s_Graphemes.Length < count + 1) s_Graphemes = new bool[s_Boundaries.Length];
                var graphemes = new Span<bool>(s_Graphemes, 0, count + 1);
                GraphemeBreaker.FindBoundaries(text.Slice(start, count), graphemes);
                LetteraWordSegmenters.FindWordStarts(text.Slice(start, count), graphemes, span, s_Scratch);
            }
            return span;
        }

        static bool[] s_Graphemes = new bool[64];
        static readonly LetteraSegmenterScratch s_Scratch = new LetteraSegmenterScratch();

        /// <summary>The word, whitespace run or punctuation run at an index, which is the one after it, or at the end of a paragraph the one before.</summary>
        public static void WordAt(ReadOnlySpan<int> text, int index, out int wordStart, out int wordEnd)
        {
            index = Clamp(index, text.Length);
            ParagraphAt(text, index, out int start, out int contentEnd, out _);
            wordStart = wordEnd = index;
            if (start == contentEnd) return;
            int at = index >= contentEnd ? contentEnd - 1 : index;
            Span<bool> boundaries = WordsOf(text, start, contentEnd);
            wordStart = at;
            while (wordStart > start && !boundaries[wordStart - start]) wordStart--;
            wordEnd = at + 1;
            while (wordEnd < contentEnd && !boundaries[wordEnd - start]) wordEnd++;
        }

        /// <summary>
        /// The word stop after an index. By default the start of the next word, as on Windows and Linux;
        /// with toWordEnd the end of the current or next word, as on Apple systems.
        /// </summary>
        public static int NextWordStop(ReadOnlySpan<int> text, int index, bool toWordEnd = false)
        {
            index = Clamp(index, text.Length);
            if (index >= text.Length) return text.Length;
            ParagraphAt(text, index, out int start, out int contentEnd, out int end);
            if (index >= contentEnd) return end >= text.Length ? text.Length : NextParagraphStop(text, end, toWordEnd);
            Span<bool> boundaries = WordsOf(text, start, contentEnd);
            int i = index;
            if (toWordEnd)
            {
                while (i < contentEnd && IsSpace(text[i])) i++;
                if (i >= contentEnd) return contentEnd;
                i++;
                while (i < contentEnd && !boundaries[i - start]) i++;
                return i;
            }
            if (!IsSpace(text[i]))
            {
                i++;
                while (i < contentEnd && !boundaries[i - start]) i++;
            }
            while (i < contentEnd && IsSpace(text[i])) i++;
            return i;
        }

        static int NextParagraphStop(ReadOnlySpan<int> text, int index, bool toWordEnd)
        {
            if (!toWordEnd) return index;
            return NextWordStop(text, index, true);
        }

        /// <summary>The start of the current or previous word.</summary>
        public static int PreviousWordStop(ReadOnlySpan<int> text, int index)
        {
            index = Clamp(index, text.Length);
            if (index <= 0) return 0;
            ParagraphAt(text, index - 1, out int start, out int contentEnd, out _);
            if (index > contentEnd) return contentEnd; // Just after a separator.
            if (start == contentEnd) return start;
            Span<bool> boundaries = WordsOf(text, start, contentEnd);
            int i = index;
            while (i > start && IsSpace(text[i - 1])) i--;
            if (i <= start) return start;
            i--;
            while (i > start && !boundaries[i - start]) i--;
            return i;
        }
    }
}

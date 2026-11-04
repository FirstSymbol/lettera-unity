using System;
using System.Collections.Generic;
using FirstSymbol.Lettera.Unicode;

namespace FirstSymbol.Lettera
{
    /// <summary>What a walk over a text steps by.</summary>
    public enum LetteraTextUnit
    {
        /// <summary>Grapheme clusters, what a reader takes for one character. A matcher's replacement is one.</summary>
        Cluster,
        /// <summary>Words by UAX #29 and the installed dictionaries. Whitespace and punctuation between them are left out.</summary>
        Word,
        /// <summary>Laid-out lines.</summary>
        Line,
        /// <summary>Paragraphs, each with the break that ends it.</summary>
        Paragraph,
    }

    /// <summary>
    /// The units of a text in order, as shown indices (the ones the selection uses), each [Start, End) and never empty. A unit that crosses the
    /// walk's range is cut to it. Use it in foreach, which allocates nothing. Main thread; valid until the text changes.
    /// </summary>
    public struct LetteraUnitWalk
    {
        readonly TextEngine m_Engine;
        readonly LetteraTextUnit m_Unit;
        readonly int m_From, m_To;
        int m_Start, m_End, m_At, m_Line, m_Parse;
        int m_StopsStart, m_StopsEnd, m_ParagraphEnd, m_Owner;
        bool m_Begun;

        // One paragraph's stops at a time, shared by all walks: a walk that finds another took them reads its paragraph again.
        static bool[] s_Stops = new bool[256];
        static int s_StopsOwner, s_NextOwner;

        internal LetteraUnitWalk(TextEngine engine, LetteraTextUnit unit, int start, int end)
        {
            m_Engine = engine;
            m_Unit = unit;
            m_From = start;
            m_To = end;
            m_Start = m_End = m_At = m_Line = m_Parse = m_StopsStart = m_StopsEnd = m_ParagraphEnd = m_Owner = 0;
            m_Begun = false;
        }

        public LetteraUnitWalk GetEnumerator() => this;

        /// <summary>The current unit's first shown index.</summary>
        public int Start => m_Start;

        /// <summary>The shown index just past the current unit.</summary>
        public int End => m_End;

        public (int Start, int End) Current => (m_Start, m_End);

        /// <summary>How many units the walk holds. Walks a copy, so this one stays where it is.</summary>
        public int Count()
        {
            LetteraUnitWalk walk = this;
            walk.m_Begun = false;
            int count = 0;
            while (walk.MoveNext()) count++;
            return count;
        }

        public bool MoveNext()
        {
            if (m_Engine == null) return false;
            if (m_Unit == LetteraTextUnit.Line) return NextLine();
            m_Engine.EnsureParsed();
            ReadOnlySpan<int> text = m_Engine.ShownCodepoints;
            int to = Math.Min(Math.Max(m_To, 0), text.Length);
            if (!m_Begun)
            {
                m_Begun = true;
                m_At = Math.Min(Math.Max(m_From, 0), text.Length);
                m_Parse = m_Engine.ParseCount;
                m_Owner = 0;
            }
            else if (m_Parse != m_Engine.ParseCount)
            {
                m_Parse = m_Engine.ParseCount;
                m_Owner = 0;
            }
            switch (m_Unit)
            {
                case LetteraTextUnit.Word: return NextWord(text, to);
                case LetteraTextUnit.Paragraph: return NextParagraph(text, to);
                default: return NextCluster(text, to);
            }
        }

        bool Yield(int start, int end) => Yield(start, end, end);

        bool Yield(int start, int end, int next)
        {
            m_Start = start;
            m_End = end;
            m_At = next;
            return true;
        }

        bool NextParagraph(ReadOnlySpan<int> text, int to)
        {
            if (m_At >= to) return false;
            LetteraTextNavigator.ParagraphAt(text, m_At, out _, out _, out int end);
            return Yield(m_At, Math.Min(Math.Max(end, m_At + 1), to));
        }

        bool NextCluster(ReadOnlySpan<int> text, int to)
        {
            if (m_At >= to) return false;
            Stops(text, m_At, false);
            int end = m_At + 1;
            while (end < m_StopsEnd && !s_Stops[end - m_StopsStart]) end++;
            // A replacement is selected and stepped over whole, so it counts as one.
            while (end < text.Length && m_Engine.AtomicAround(end, out _, out int whole) && whole > end) end = whole;
            return Yield(m_At, Math.Min(end, to));
        }

        bool NextWord(ReadOnlySpan<int> text, int to)
        {
            while (m_At < to)
            {
                if (!HasStops(m_At))
                {
                    LetteraTextNavigator.ParagraphAt(text, m_At, out _, out int contentEnd, out int end);
                    if (m_At >= contentEnd)
                    {
                        m_At = Math.Max(end, m_At + 1);
                        continue;
                    }
                    Stops(text, m_At, true);
                }
                int start = m_At;
                while (start > m_StopsStart && !s_Stops[start - m_StopsStart]) start--;
                int stop = m_At + 1;
                while (stop < m_StopsEnd && !s_Stops[stop - m_StopsStart]) stop++;
                // The break after a paragraph's content is no word, and the next paragraph reads its own stops.
                m_At = stop < m_StopsEnd ? stop : m_ParagraphEnd;
                if (IsWordLike(text, start, stop)) return Yield(Math.Max(start, m_From), Math.Min(stop, to), m_At);
            }
            return false;
        }

        // A word holds a letter, a digit or a pictograph; a run of spaces or of punctuation does not.
        static bool IsWordLike(ReadOnlySpan<int> text, int start, int end)
        {
            for (int i = start; i < end; i++)
            {
                int cp = text[i];
                GeneralCategory category = LetteraUnicode.GetCategory(cp);
                if (category <= GeneralCategory.Lo || (category >= GeneralCategory.Nd && category <= GeneralCategory.No) || LetteraUnicode.IsExtendedPictographic(cp)) return true;
            }
            return false;
        }

        bool HasStops(int index) => m_Owner != 0 && m_Owner == s_StopsOwner && index >= m_StopsStart && index < m_StopsEnd;

        // The stops of the paragraph around an index: its content's words, or the clusters of all of it with its break.
        void Stops(ReadOnlySpan<int> text, int index, bool words)
        {
            if (HasStops(index)) return;
            LetteraTextNavigator.ParagraphAt(text, index, out int start, out int contentEnd, out int end);
            int last = words ? contentEnd : end;
            int count = last - start;
            if (s_Stops.Length < count + 1) s_Stops = new bool[Math.Max(count + 1, s_Stops.Length * 2)];
            var span = new Span<bool>(s_Stops, 0, count + 1);
            if (words) LetteraTextNavigator.FindWordStops(text, start, last, span);
            else
            {
                span.Clear();
                GraphemeBreaker.FindBoundaries(text.Slice(start, count), span);
            }
            m_StopsStart = start;
            m_StopsEnd = last;
            m_ParagraphEnd = end;
            m_Owner = s_StopsOwner = ++s_NextOwner;
            if (s_NextOwner == int.MaxValue) s_NextOwner = 0;
        }

        bool NextLine()
        {
            if (!m_Begun)
            {
                m_Begun = true;
                m_Engine.EnsurePositioned();
                m_Line = 0;
            }
            IReadOnlyList<LetteraLine> lines = m_Engine.Lines;
            int to = Math.Min(Math.Max(m_To, 0), m_Engine.ShownLength), from = Math.Max(m_From, 0);
            while (m_Line < lines.Count)
            {
                LetteraLine line = lines[m_Line++];
                int start = Math.Max(line.Start, from), end = Math.Min(line.End, to);
                if (end > start) return Yield(start, end);
                if (line.Start >= to) break;
            }
            m_Line = lines.Count;
            return false;
        }
    }

    // Finding a string in what a text shows, in shown indices.
    internal static class LetteraTextFind
    {
        const int StackQuery = 256;

        public static int FindAll(TextEngine engine, UnityEngine.Component owner, string query, List<LetteraSearchHit> results, bool ignoreCase)
        {
            if (results == null) throw new ArgumentNullException(nameof(results));
            if (string.IsNullOrEmpty(query)) return 0;
            if (engine.IsWindowed) return FindInDocument(engine, owner, query, results, ignoreCase);
            engine.EnsureParsed();
            ReadOnlySpan<int> text = engine.ShownCodepoints;
            Span<int> pattern = query.Length <= StackQuery ? stackalloc int[query.Length] : new int[query.Length];
            int length = 0;
            for (int i = 0; i < query.Length; i++)
            {
                int cp = query[i];
                if (char.IsHighSurrogate(query[i]) && i + 1 < query.Length && char.IsLowSurrogate(query[i + 1])) cp = char.ConvertToUtf32(query[i], query[++i]);
                pattern[length++] = ignoreCase ? Fold(cp) : cp;
            }
            pattern = pattern.Slice(0, length);
            int found = 0, last = text.Length - length;
            for (int at = 0; at <= last;)
            {
                int k = 0;
                if (ignoreCase)
                    while (k < length && Fold(text[at + k]) == pattern[k]) k++;
                else
                    while (k < length && text[at + k] == pattern[k]) k++;
                if (k < length)
                {
                    at++;
                    continue;
                }
                results.Add(new LetteraSearchHit(at, at + length));
                found++;
                at += length;
            }
            return found;
        }

        // Simple case folding: ς and σ, or Σ and σ, compare equal through the capital.
        static int Fold(int cp) => cp < 0x80 ? (cp >= 'A' && cp <= 'Z' ? cp + 32 : cp) : LetteraUnicode.ToLower(LetteraUnicode.ToUpper(cp));

        static int FindInDocument(TextEngine engine, UnityEngine.Component owner, string query, List<LetteraSearchHit> results, bool ignoreCase)
        {
            var search = new LetteraDocumentSearch(engine.View.Rope, owner);
            search.Query = query;
            search.Comparison = ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            search.Scope = LetteraSearchScope.Shown;
            int before = results.Count;
            search.Start();
            while (search.Step(float.MaxValue, results)) { }
            return results.Count - before;
        }
    }
}

using System;
using System.Collections.Generic;

namespace FirstSymbol.Lettera.Unicode
{
    /// <summary>What a search borrows between calls, so it allocates nothing once warm. One per thread.</summary>
    public sealed class LetteraSegmenterScratch
    {
        internal long[] Cost = new long[256];
        internal int[] Back = new int[256];
        internal bool[] Unknown = new bool[256];
        internal bool[] Graphemes = new bool[256];

        internal void Ensure(int length)
        {
            if (Cost.Length >= length) return;
            int size = Math.Max(length, Cost.Length * 2);
            Cost = new long[size];
            Back = new int[size];
            Unknown = new bool[size];
            Graphemes = new bool[size];
        }
    }

    /// <summary>Finds where words start in a run of one script written without spaces.</summary>
    public interface ILetteraWordSegmenter
    {
        /// <summary>Sets boundaries[i - start] where a word starts in text[start..end], for i past start. Pure and thread-safe, given its own scratch.</summary>
        void FindWords(ReadOnlySpan<int> text, int start, int end, ReadOnlySpan<bool> graphemes, Span<bool> boundaries, LetteraSegmenterScratch scratch);
    }

    /// <summary>Which scripts have a segmenter. Empty until a project lists a dictionary, and then those scripts alone break by words.</summary>
    public static class LetteraWordSegmenters
    {
        static readonly Dictionary<UnicodeScript, ILetteraWordSegmenter> s_Table = new Dictionary<UnicodeScript, ILetteraWordSegmenter>();

        /// <summary>Rises when the table changes.</summary>
        public static int Generation { get; private set; }

        public static int Count => s_Table.Count;

        public static ILetteraWordSegmenter For(UnicodeScript script) => s_Table.Count != 0 && s_Table.TryGetValue(script, out ILetteraWordSegmenter segmenter) ? segmenter : null;

        public static void Set(UnicodeScript script, ILetteraWordSegmenter segmenter)
        {
            if (segmenter == null) s_Table.Remove(script);
            else s_Table[script] = segmenter;
            Generation++;
        }

        public static void Clear()
        {
            if (s_Table.Count == 0) return;
            s_Table.Clear();
            Generation++;
        }

        // The script whose run a character belongs to, for those written without spaces.
        internal static bool TryUnspacedScript(int cp, out UnicodeScript script)
        {
            if (cp >= 0xE00 && cp <= 0xE7F) script = UnicodeScript.Thai;
            else if (cp >= 0xE80 && cp <= 0xEFF) script = UnicodeScript.Lao;
            else if (cp >= 0x1000 && cp <= 0x109F) script = UnicodeScript.Myanmar;
            else if (cp >= 0x1780 && cp <= 0x17FF) script = UnicodeScript.Khmer;
            else
            {
                script = default;
                return false;
            }
            return true;
        }

        // Digits and the like share those blocks without being letters of any word.
        static bool JoinsRun(int cp, UnicodeScript script)
        {
            if (!TryUnspacedScript(cp, out UnicodeScript own) || own != script) return false;
            return !(cp >= 0xE50 && cp <= 0xE59) && !(cp >= 0xED0 && cp <= 0xED9) && !(cp >= 0x1040 && cp <= 0x1049) && !(cp >= 0x17E0 && cp <= 0x17E9) && cp != 0xE46;
        }

        /// <summary>Marks word starts in a paragraph's runs that have a dictionary. Returns false when no run had one.</summary>
        public static bool FindWordStarts(ReadOnlySpan<int> text, ReadOnlySpan<bool> graphemes, Span<bool> starts, LetteraSegmenterScratch scratch)
        {
            if (s_Table.Count == 0) return false;
            bool any = false;
            int i = 0;
            while (i < text.Length)
            {
                if (!TryUnspacedScript(text[i], out UnicodeScript script) || !JoinsRun(text[i], script))
                {
                    i++;
                    continue;
                }
                int end = i + 1;
                while (end < text.Length && JoinsRun(text[end], script)) end++;
                ILetteraWordSegmenter segmenter = For(script);
                if (segmenter != null && end - i > 1)
                {
                    segmenter.FindWords(text, i, end, graphemes.Slice(i, end - i + 1), starts.Slice(i, end - i + 1), scratch);
                    any = true;
                }
                i = end;
            }
            return any;
        }

        /// <summary>
        /// Opens line breaks at word starts. Only what UAX #14 left closed inside a run changes: a joiner splits a run, so nothing breaks beside it,
        /// and the line breaker's own result stands when no dictionary is listed.
        /// </summary>
        public static void ApplyToBreaks(ReadOnlySpan<int> text, ReadOnlySpan<bool> graphemes, Span<LineBreakOpportunity> breaks, LetteraSegmenterScratch scratch)
        {
            if (s_Table.Count == 0) return;
            scratch.Ensure(text.Length + 2);
            var starts = new Span<bool>(scratch.Graphemes, 0, text.Length + 1);
            starts.Clear();
            if (!FindWordStarts(text, graphemes, starts, scratch)) return;
            for (int i = 1; i < text.Length; i++)
                if (starts[i] && breaks[i - 1] == LineBreakOpportunity.None)
                    breaks[i - 1] = LineBreakOpportunity.Allowed;
        }
    }

    /// <summary>
    /// A word list as a minimal automaton, one flat array read in place. It fills the fewest unknown clusters, then the fewest words, and keeps unknown clusters
    /// together as one unit. Boundaries only ever fall between grapheme clusters.
    /// </summary>
    public sealed class LetteraWordAutomaton : ILetteraWordSegmenter
    {
        public const int FormatVersion = 1;
        const int HeaderInts = 5;

        readonly byte[] m_Data;
        readonly int m_Root, m_LowCodepoint;
        readonly ushort[] m_Symbols;

        /// <summary>Reads the format the builder writes. Throws when it is not that.</summary>
        public LetteraWordAutomaton(byte[] data)
        {
            if (data == null || data.Length < HeaderInts * 4 || data[0] != (byte)'L' || data[1] != (byte)'T' || data[2] != (byte)'W' || data[3] != (byte)'D')
                throw new ArgumentException("This is not a Lettera word dictionary.", nameof(data));
            if (ReadInt(data, 4) != FormatVersion) throw new ArgumentException($"This word dictionary has format {ReadInt(data, 4)}, and this version reads {FormatVersion}.", nameof(data));
            m_Data = data;
            int alphabet = ReadInt(data, 8);
            m_Root = ReadInt(data, 12);
            WordCount = ReadInt(data, 16);
            int at = HeaderInts * 4;
            int low = int.MaxValue, high = 0;
            for (int i = 0; i < alphabet; i++)
            {
                int cp = ReadInt(data, at + i * 4);
                low = Math.Min(low, cp);
                high = Math.Max(high, cp);
            }
            m_LowCodepoint = alphabet == 0 ? 0 : low;
            m_Symbols = new ushort[alphabet == 0 ? 0 : high - low + 1];
            for (int i = 0; i < alphabet; i++) m_Symbols[ReadInt(data, at + i * 4) - low] = (ushort)(i + 1);
        }

        public int WordCount { get; }

        static int ReadInt(byte[] d, int at) => d[at] | d[at + 1] << 8 | d[at + 2] << 16 | d[at + 3] << 24;

        int SymbolOf(int cp)
        {
            int index = cp - m_LowCodepoint;
            return index >= 0 && index < m_Symbols.Length ? m_Symbols[index] : 0;
        }

        // A state: one byte of flags, a count in a varint, then its labels sorted, then as many targets.
        bool Step(ref int state, int symbol)
        {
            byte[] d = m_Data;
            int at = state + 1;
            int count = 0, shift = 0;
            while (true)
            {
                byte b = d[at++];
                count |= (b & 0x7F) << shift;
                if (b < 0x80) break;
                shift += 7;
            }
            int low = 0, high = count - 1;
            while (low <= high)
            {
                int middle = (low + high) >> 1;
                int p = at + middle * 2;
                int label = d[p] | d[p + 1] << 8;
                if (label == symbol)
                {
                    state = ReadInt(d, at + count * 2 + middle * 4);
                    return true;
                }
                if (label < symbol) low = middle + 1;
                else high = middle - 1;
            }
            return false;
        }

        bool IsFinal(int state) => (m_Data[state] & 1) != 0;

        /// <summary>Whether a word is in the list.</summary>
        public bool Contains(ReadOnlySpan<int> word)
        {
            int state = m_Root;
            foreach (int cp in word)
            {
                int symbol = SymbolOf(cp);
                if (symbol == 0 || !Step(ref state, symbol)) return false;
            }
            return word.Length > 0 && IsFinal(state);
        }

        const long UnknownCost = 1L << 40;

        public void FindWords(ReadOnlySpan<int> text, int start, int end, ReadOnlySpan<bool> graphemes, Span<bool> boundaries, LetteraSegmenterScratch scratch)
        {
            int length = end - start;
            scratch.Ensure(length + 2);
            long[] cost = scratch.Cost;
            int[] back = scratch.Back;
            bool[] unknown = scratch.Unknown;
            for (int i = 0; i <= length; i++) cost[i] = long.MaxValue;
            cost[0] = 0;
            back[0] = -1;
            unknown[0] = false;

            for (int i = 0; i < length; i++)
            {
                long here = cost[i];
                if (here == long.MaxValue) continue;

                // Every word that starts here.
                int state = m_Root;
                for (int j = i; j < length; j++)
                {
                    int symbol = SymbolOf(text[start + j]);
                    if (symbol == 0 || !Step(ref state, symbol)) break;
                    int next = j + 1;
                    if (IsFinal(state) && (next == length || graphemes[next]))
                    {
                        long through = here + 1;
                        if (through < cost[next])
                        {
                            cost[next] = through;
                            back[next] = i;
                            unknown[next] = false;
                        }
                    }
                }

                // Or one unknown cluster. Joined to an unknown one just before, it is no further word.
                int after = i + 1;
                while (after < length && !graphemes[after]) after++;
                long stepped = here + UnknownCost + (unknown[i] ? 0 : 1);
                if (stepped < cost[after])
                {
                    cost[after] = stepped;
                    back[after] = i;
                    unknown[after] = true;
                }
            }

            for (int at = length; at > 0;)
            {
                int from = back[at];
                if (from <= 0) break;
                if (!(unknown[at] && unknown[from])) boundaries[from] = true;
                at = from;
            }
        }
    }
}

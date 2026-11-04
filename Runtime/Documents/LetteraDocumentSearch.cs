using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace FirstSymbol.Lettera
{
    public enum LetteraSearchScope
    {
        /// <summary>What a reader sees: a word split by a tag is one word.</summary>
        Shown,
        /// <summary>The source as written, markup and all.</summary>
        Source,
    }

    /// <summary>One place a search found: in chars of the document's source, or in shown indices from a text's own FindAll.</summary>
    public readonly struct LetteraSearchHit
    {
        public readonly int Start, End;

        public LetteraSearchHit(int start, int end)
        {
            Start = start;
            End = end;
        }
    }

    /// <summary>Finds text in a document a slice at a time, and reports where in the source. One per search.</summary>
    public sealed class LetteraDocumentSearch
    {
        readonly LetteraRope m_Rope;
        readonly TextEngine m_Parser;
        readonly DocumentTagWalk m_Walk;
        string m_Query = "";
        int m_Block, m_Version;
        bool m_Running;

        char[] m_Chars = new char[0];
        int[] m_Map = new int[0];

        // What the last block left unmatched at its end, since a hit can cross into the next.
        readonly List<char> m_CarryChars = new List<char>();
        readonly List<int> m_CarryMap = new List<int>();

        /// <param name="owner">A text whose styles decide what markup is, for the Shown scope.</param>
        public LetteraDocumentSearch(LetteraRope rope, Component owner = null)
        {
            m_Rope = rope ?? throw new ArgumentNullException(nameof(rope));
            if (owner is ITextComponent text)
            {
                m_Parser = text.SharedEngine;
                m_Walk = new DocumentTagWalk(rope);
            }
        }

        public string Query { get => m_Query; set { m_Query = value ?? ""; m_Running = false; } }
        public StringComparison Comparison { get; set; } = StringComparison.Ordinal;
        public LetteraSearchScope Scope { get; set; } = LetteraSearchScope.Source;

        public float Progress => m_Rope.BlockCount == 0 ? 1 : (float)m_Block / m_Rope.BlockCount;

        public void Start(int fromChar = 0)
        {
            m_Running = true;
            m_Block = m_Rope.BlockCount == 0 ? 0 : m_Rope.BlockAtChar(Math.Min(fromChar, Math.Max(m_Rope.Length - 1, 0)));
            m_Version = m_Rope.Version;
            m_CarryChars.Clear();
            m_CarryMap.Clear();
        }

        public void Cancel() => m_Running = false;

        /// <summary>Runs for about the budget, adding hits in order. False once it reached the end or was cancelled.</summary>
        public bool Step(float budgetMs, List<LetteraSearchHit> results)
        {
            if (!m_Running || m_Query.Length == 0) return false;
            if (m_Version != m_Rope.Version) Start(m_Rope.CharOfBlock(Math.Min(m_Block, Math.Max(m_Rope.BlockCount - 1, 0))));
            long started = System.Diagnostics.Stopwatch.GetTimestamp();
            do
            {
                if (m_Block >= m_Rope.BlockCount)
                {
                    m_Running = false;
                    return false;
                }
                ScanBlock(m_Block, results);
                m_Block++;
            }
            while (LetteraDocumentView.MillisecondsSince(started) < budgetMs);
            return m_Block < m_Rope.BlockCount;
        }

        void ScanBlock(int block, List<LetteraSearchHit> results)
        {
            int start = m_Rope.CharOfBlock(block), chars = m_Rope.BlockChars(block);
            int carried = m_CarryChars.Count;
            int count;
            if (Scope == LetteraSearchScope.Shown && m_Parser != null && m_Rope.BlockHasMarkup(block))
            {
                count = FillShown(block, start, chars, carried);
            }
            else
            {
                Ensure(carried + chars);
                m_Rope.CopyTo(start, new Span<char>(m_Chars, carried, chars));
                for (int i = 0; i < chars; i++) m_Map[carried + i] = start + i;
                count = carried + chars;
            }
            for (int i = 0; i < carried; i++)
            {
                m_Chars[i] = m_CarryChars[i];
                m_Map[i] = m_CarryMap[i];
            }

            var text = new ReadOnlySpan<char>(m_Chars, 0, count);
            int at = 0;
            int searchEnd = count;
            while (at <= searchEnd - m_Query.Length)
            {
                int found = text.Slice(at).IndexOf(m_Query.AsSpan(), Comparison);
                if (found < 0) break;
                int first = at + found, last = first + m_Query.Length - 1;
                results.Add(new LetteraSearchHit(m_Map[first], m_Map[last] + 1));
                at = first + m_Query.Length;
            }

            // Enough of the end to finish a match that crosses into the next block, past anything already reported.
            m_CarryChars.Clear();
            m_CarryMap.Clear();
            int keep = Math.Max(Math.Max(count - (m_Query.Length - 1), at), 0);
            for (int i = keep; i < count; i++)
            {
                m_CarryChars.Add(m_Chars[i]);
                m_CarryMap.Add(m_Map[i]);
            }
        }

        void Ensure(int size)
        {
            if (m_Chars.Length >= size) return;
            int capacity = Math.Max(size, m_Chars.Length * 2);
            Array.Resize(ref m_Chars, capacity);
            Array.Resize(ref m_Map, capacity);
        }

        // What a reader sees of a block, with each char's source position.
        int FillShown(int block, int start, int chars, int carried)
        {
            string prelude = m_Walk.PreludeThrough(block, m_Parser);
            var source = new char[prelude.Length + chars];
            prelude.CopyTo(0, source, 0, prelude.Length);
            m_Rope.CopyTo(start, new Span<char>(source, prelude.Length, chars));
            TextEngine.ParseForSearch(m_Parser, new string(source), out int[] shown, out int[] sourceIndex, out int length);
            Ensure(carried + length * 2);
            int count = carried;
            for (int i = 0; i < length; i++)
            {
                int position = start + sourceIndex[i] - prelude.Length;
                int cp = shown[i];
                if (cp > 0xFFFF)
                {
                    cp -= 0x10000;
                    m_Chars[count] = (char)(0xD800 + (cp >> 10));
                    m_Map[count++] = position;
                    m_Chars[count] = (char)(0xDC00 + (cp & 0x3FF));
                    m_Map[count++] = position + 1;
                }
                else
                {
                    m_Chars[count] = (char)cp;
                    m_Map[count++] = position;
                }
            }
            return count;
        }
    }
}

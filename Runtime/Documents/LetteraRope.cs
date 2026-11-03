using System;
using System.Collections.Generic;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Text;

namespace FirstSymbol.Lettera
{
    /// <summary>Which characters can start a markup match under some style list. A block without one needs no parse.</summary>
    public sealed class LetteraMarkupTriggers
    {
        readonly bool[] m_Ascii = new bool[128];
        readonly HashSet<int> m_Others = new HashSet<int>();

        public LetteraMarkupTriggers(params char[] characters)
        {
            foreach (char c in characters) Add(c);
        }

        public void Add(int codepoint)
        {
            if (codepoint < 128) m_Ascii[codepoint] = true;
            else m_Others.Add(codepoint);
        }

        internal bool Contains(int codepoint) => codepoint < 128 ? m_Ascii[codepoint] : m_Others.Contains(codepoint);
    }

    /// <summary>What one edit changed.</summary>
    public readonly struct LetteraRopePatch
    {
        public readonly int Start, Removed, Inserted, OldVersion, NewVersion;

        public LetteraRopePatch(int start, int removed, int inserted, int oldVersion, int newVersion)
        {
            Start = start;
            Removed = removed;
            Inserted = inserted;
            OldVersion = oldVersion;
            NewVersion = newVersion;
        }

        /// <summary>Where a position before the edit stands after it. Inside what was removed, it goes to the end of what was inserted.</summary>
        public int Map(int position) => position <= Start ? position : position >= Start + Removed ? position + Inserted - Removed : Start + Inserted;
    }

    [Flags]
    internal enum RopeBlockFlags : byte
    {
        None = 0, AsciiOnly = 1, HasMarkupTrigger = 2, HasStrongRtl = 4, HasUnspaced = 8, EndsParagraph = 16, InvalidSequences = 32,
    }

    /// <summary>
    /// A document's source, with its markup, in blocks of about 4k. Every query walks a tree of totals and reads at most one block, so it costs the same
    /// on a megabyte as on a line. Blocks never change once made: an edit replaces them, so a reader holding one never sees it torn.
    /// Positions are in UTF-16 units of the source.
    /// </summary>
    public sealed class LetteraRope : IDisposable
    {
        internal const int TargetChars = 4096, LimitChars = 16384, MergeBelow = 1024;
        const int FanOut = 16;

        internal sealed class Block
        {
            public char[] Owned;
            public RopeBytes Source;
            public long Offset;
            public int ByteLength;
            public int Chars, Codepoints, Paragraphs;
            public RopeBlockFlags Flags;
            public double Height;
            public bool Measured;

            public bool IsOwned => Source == null;
        }

        sealed class Node
        {
            public Node[] Kids;
            public Block[] Blocks;
            public int Count;
            public int Chars, Codepoints, Paragraphs, BlockTotal;
            public double Height;
            public bool IsLeaf => Blocks != null;

            public void Refresh()
            {
                Chars = Codepoints = Paragraphs = BlockTotal = 0;
                Height = 0;
                if (IsLeaf)
                {
                    BlockTotal = Count;
                    for (int i = 0; i < Count; i++)
                    {
                        Block b = Blocks[i];
                        Chars += b.Chars;
                        Codepoints += b.Codepoints;
                        Paragraphs += b.Paragraphs;
                        Height += b.Height;
                    }
                    return;
                }
                for (int i = 0; i < Count; i++)
                {
                    Node k = Kids[i];
                    Chars += k.Chars;
                    Codepoints += k.Codepoints;
                    Paragraphs += k.Paragraphs;
                    BlockTotal += k.BlockTotal;
                    Height += k.Height;
                }
            }
        }

        Node m_Root = new Node { Blocks = new Block[FanOut + 1] };
        readonly LetteraMarkupTriggers m_Triggers;
        readonly RopeBytes m_Bytes;
        readonly RopeBlockCache m_Cache;
        bool m_Disposed;

        LetteraRope(LetteraMarkupTriggers triggers, RopeBytes bytes)
        {
            m_Triggers = triggers;
            m_Bytes = bytes;
            m_Cache = new RopeBlockCache(UnityEngine.Application.platform == UnityEngine.RuntimePlatform.WebGLPlayer ? 32 : 128);
        }

        public static LetteraRope FromString(string text, LetteraMarkupTriggers triggers = null) => FromChars((text ?? "").AsSpan(), triggers);

        public static LetteraRope FromChars(ReadOnlySpan<char> text, LetteraMarkupTriggers triggers = null)
        {
            var rope = new LetteraRope(triggers, null);
            rope.AppendOwned(text);
            return rope;
        }

        /// <summary>Blocks point into the bytes, which stay undecoded until read. Invalid sequences read as U+FFFD.</summary>
        public static LetteraRope FromUtf8(byte[] bytes, LetteraMarkupTriggers triggers = null)
        {
            var source = new RopeArrayBytes(bytes ?? Array.Empty<byte>());
            return FromBytes(source, triggers, Utf8BomLength(source));
        }

        internal static LetteraRope FromBytes(RopeBytes source, LetteraMarkupTriggers triggers, long start)
        {
            var rope = new LetteraRope(triggers, source);
            RopeScanner.ScanUtf8(source, start, triggers, rope.AppendScanned);
            return rope;
        }

        internal static int Utf8BomLength(RopeBytes source)
        {
            if (source.Length < 3) return 0;
            Span<byte> head = stackalloc byte[3];
            source.Read(0, head);
            return head[0] == 0xEF && head[1] == 0xBB && head[2] == 0xBF ? 3 : 0;
        }

        void AppendOwned(ReadOnlySpan<char> text)
        {
            int at = m_Root.BlockTotal;
            List<Block> blocks = RopeScanner.SplitChars(text, m_Triggers);
            foreach (Block block in blocks) InsertBlock(at++, block);
        }

        void AppendScanned(Block block) => InsertBlock(m_Root.BlockTotal, block);

        public void Dispose()
        {
            if (m_Disposed) return;
            m_Disposed = true;
            m_Bytes?.Dispose();
        }

        // ------------------------------------------------------------- totals

        public int Length => m_Root.Chars;
        public int CodepointCount => m_Root.Codepoints;

        /// <summary>One more than the separators: an empty rope has one empty paragraph.</summary>
        public int ParagraphCount => m_Root.Paragraphs + 1;
        public int BlockCount => m_Root.BlockTotal;

        /// <summary>Rises with each edit.</summary>
        public int Version { get; private set; }

        public event Action<LetteraRopePatch> Changed;

        public double TotalHeight => m_Root.Height;

        // ------------------------------------------------------------- walking

        Block FindByChar(int position, out int blockIndex, out int chars, out int codepoints, out int paragraphs)
        {
            blockIndex = chars = codepoints = paragraphs = 0;
            Node node = m_Root;
            int left = position;
            while (!node.IsLeaf)
            {
                int i = 0;
                for (; i < node.Count - 1; i++)
                {
                    Node kid = node.Kids[i];
                    if (left < kid.Chars) break;
                    left -= kid.Chars;
                    chars += kid.Chars;
                    codepoints += kid.Codepoints;
                    paragraphs += kid.Paragraphs;
                    blockIndex += kid.BlockTotal;
                }
                node = node.Kids[i];
            }
            if (node.Count == 0) return null;
            int b = 0;
            for (; b < node.Count - 1; b++)
            {
                Block block = node.Blocks[b];
                if (left < block.Chars) break;
                left -= block.Chars;
                chars += block.Chars;
                codepoints += block.Codepoints;
                paragraphs += block.Paragraphs;
                blockIndex++;
            }
            return node.Blocks[b];
        }

        Block BlockAtIndex(int index, out int chars)
        {
            chars = 0;
            Node node = m_Root;
            int left = index;
            while (!node.IsLeaf)
            {
                int i = 0;
                for (; i < node.Count - 1; i++)
                {
                    Node kid = node.Kids[i];
                    if (left < kid.BlockTotal) break;
                    left -= kid.BlockTotal;
                    chars += kid.Chars;
                }
                node = node.Kids[i];
            }
            for (int b = 0; b < left; b++) chars += node.Blocks[b].Chars;
            return node.Blocks[left];
        }

        internal ReadOnlySpan<char> CharsOf(Block block)
        {
            if (block.IsOwned) return new ReadOnlySpan<char>(block.Owned, 0, block.Chars);
            return m_Cache.Decode(block);
        }

        void CheckPosition(int position, int limit)
        {
            if (position < 0 || position > limit) throw new ArgumentOutOfRangeException(nameof(position), $"{position} is outside 0..{limit}.");
        }

        // ------------------------------------------------------------- conversions

        public int CharToCodepoint(int position)
        {
            CheckPosition(position, Length);
            Block block = FindByChar(position, out _, out int chars, out int codepoints, out _);
            if (block == null) return 0;
            ReadOnlySpan<char> text = CharsOf(block);
            int inside = position - chars;
            for (int i = 0; i < inside; i++)
            {
                codepoints++;
                if (char.IsHighSurrogate(text[i]) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1])) i++;
            }
            return codepoints;
        }

        public int CodepointToChar(int codepoint)
        {
            CheckPosition(codepoint, CodepointCount);
            int chars = 0, left = codepoint;
            Node node = m_Root;
            while (!node.IsLeaf)
            {
                int i = 0;
                for (; i < node.Count - 1; i++)
                {
                    Node kid = node.Kids[i];
                    if (left < kid.Codepoints) break;
                    left -= kid.Codepoints;
                    chars += kid.Chars;
                }
                node = node.Kids[i];
            }
            if (node.Count == 0) return 0;
            int b = 0;
            for (; b < node.Count - 1; b++)
            {
                if (left < node.Blocks[b].Codepoints) break;
                left -= node.Blocks[b].Codepoints;
                chars += node.Blocks[b].Chars;
            }
            ReadOnlySpan<char> text = CharsOf(node.Blocks[b]);
            int at = 0;
            for (int c = 0; c < left && at < text.Length; c++)
            {
                if (char.IsHighSurrogate(text[at]) && at + 1 < text.Length && char.IsLowSurrogate(text[at + 1])) at++;
                at++;
            }
            return chars + at;
        }

        /// <summary>Which paragraph a position is in.</summary>
        public int CharToParagraph(int position)
        {
            CheckPosition(position, Length);
            Block block = FindByChar(position, out _, out int chars, out _, out int paragraphs);
            if (block == null) return 0;
            ReadOnlySpan<char> text = CharsOf(block);
            int inside = position - chars;
            for (int i = 0; i < inside; i++)
            {
                char c = text[i];
                if (c == '\r')
                {
                    if (i + 1 < text.Length && text[i + 1] == '\n')
                    {
                        // A position between the two is still before the end of the separator.
                        if (i + 1 >= inside) break;
                        i++;
                    }
                    paragraphs++;
                }
                else if (c == '\n' || c == '\u0085' || c == (char)0x2029) paragraphs++;
            }
            return paragraphs;
        }

        /// <summary>Where a paragraph starts.</summary>
        public int ParagraphToChar(int paragraph)
        {
            if (paragraph < 0 || paragraph >= ParagraphCount) throw new ArgumentOutOfRangeException(nameof(paragraph));
            if (paragraph == 0) return 0;
            // The start of paragraph n is just past separator n.
            int left = paragraph, chars = 0;
            Node node = m_Root;
            while (!node.IsLeaf)
            {
                int i = 0;
                for (; i < node.Count - 1; i++)
                {
                    Node kid = node.Kids[i];
                    if (left <= kid.Paragraphs) break;
                    left -= kid.Paragraphs;
                    chars += kid.Chars;
                }
                node = node.Kids[i];
            }
            int b = 0;
            for (; b < node.Count - 1; b++)
            {
                if (left <= node.Blocks[b].Paragraphs) break;
                left -= node.Blocks[b].Paragraphs;
                chars += node.Blocks[b].Chars;
            }
            ReadOnlySpan<char> text = CharsOf(node.Blocks[b]);
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
                else if (c != '\r' && c != '\n' && c != '\u0085' && c != (char)0x2029) continue;
                if (--left == 0) return chars + i + 1;
            }
            return Length;
        }

        public int BlockAtChar(int position)
        {
            CheckPosition(position, Length);
            FindByChar(position, out int index, out _, out _, out _);
            return index;
        }

        /// <summary>Where a block starts, in chars.</summary>
        public int CharOfBlock(int block)
        {
            if (block >= BlockCount) return Length;
            BlockAtIndex(block, out int chars);
            return chars;
        }

        public int BlockChars(int block) => BlockAtIndex(block, out _).Chars;

        internal void BlockTotals(int block, out int codepoints, out int paragraphs)
        {
            Block found = BlockAtIndex(block, out _);
            codepoints = found.Codepoints;
            paragraphs = found.Paragraphs;
        }

        internal double BlockHeight(int block) => BlockAtIndex(block, out _).Height;

        internal bool BlockHasMarkup(int block) => (BlockAtIndex(block, out _).Flags & RopeBlockFlags.HasMarkupTrigger) != 0;
        internal bool BlockEndsParagraph(int block) => (BlockAtIndex(block, out _).Flags & RopeBlockFlags.EndsParagraph) != 0;
        internal bool BlockHasInvalidSequences(int block) => (BlockAtIndex(block, out _).Flags & RopeBlockFlags.InvalidSequences) != 0;

        /// <summary>The block at a height down the document, and how far into it.</summary>
        public int BlockAtHeight(double height, out double offset)
        {
            int index = 0;
            double left = Math.Max(height, 0);
            Node node = m_Root;
            while (!node.IsLeaf)
            {
                int i = 0;
                for (; i < node.Count - 1; i++)
                {
                    Node kid = node.Kids[i];
                    if (left < kid.Height) break;
                    left -= kid.Height;
                    index += kid.BlockTotal;
                }
                node = node.Kids[i];
            }
            int b = 0;
            for (; b < node.Count - 1; b++)
            {
                if (left < node.Blocks[b].Height) break;
                left -= node.Blocks[b].Height;
                index++;
            }
            offset = left;
            return index;
        }

        public double HeightAtBlock(int block)
        {
            double height = 0;
            int left = block;
            Node node = m_Root;
            while (!node.IsLeaf)
            {
                int i = 0;
                for (; i < node.Count - 1; i++)
                {
                    Node kid = node.Kids[i];
                    if (left < kid.BlockTotal) break;
                    left -= kid.BlockTotal;
                    height += kid.Height;
                }
                node = node.Kids[i];
            }
            for (int b = 0; b < left && b < node.Count; b++) height += node.Blocks[b].Height;
            return height;
        }

        /// <summary>Sets a block's height, and the totals up the path to it.</summary>
        public void SetBlockHeight(int block, double height) => SetHeight(m_Root, block, height);

        /// <summary>How many blocks hold a height from a real layout.</summary>
        public int MeasuredBlocks { get; private set; }

        internal bool IsBlockMeasured(int block) => BlockAtIndex(block, out _).Measured;

        internal void MarkBlockMeasured(int block, bool measured)
        {
            Block found = BlockAtIndex(block, out _);
            if (found.Measured == measured) return;
            found.Measured = measured;
            MeasuredBlocks += measured ? 1 : -1;
        }

        internal void ForgetMeasures()
        {
            for (int b = 0; b < BlockCount; b++) BlockAtIndex(b, out _).Measured = false;
            MeasuredBlocks = 0;
        }

        /// <summary>What height a block just made by an edit starts with: codepoints and paragraphs in, an estimate out.</summary>
        internal Func<int, int, double> EstimateNewBlock;

        /// <summary>The first block the last edit made, for what keeps something per block.</summary>
        internal int LastEditFirstBlock { get; private set; }

        static void SetHeight(Node node, int index, double height)
        {
            if (node.IsLeaf)
            {
                node.Height += height - node.Blocks[index].Height;
                node.Blocks[index].Height = height;
                return;
            }
            for (int i = 0; i < node.Count; i++)
            {
                Node kid = node.Kids[i];
                if (index < kid.BlockTotal)
                {
                    double before = kid.Height;
                    SetHeight(kid, index, height);
                    node.Height += kid.Height - before;
                    return;
                }
                index -= kid.BlockTotal;
            }
        }

        /// <summary>Copies text out, decoding what it must.</summary>
        public void CopyTo(int start, Span<char> destination)
        {
            CheckPosition(start, Length);
            CheckPosition(start + destination.Length, Length);
            int written = 0;
            while (written < destination.Length)
            {
                Block block = FindByChar(start + written, out _, out int chars, out _, out _);
                ReadOnlySpan<char> text = CharsOf(block);
                int inside = start + written - chars;
                int take = Math.Min(text.Length - inside, destination.Length - written);
                text.Slice(inside, take).CopyTo(destination.Slice(written));
                written += take;
            }
        }

        public string ToString(int start, int length)
        {
            var chars = new char[length];
            CopyTo(start, chars);
            return new string(chars);
        }

        public override string ToString() => ToString(0, Length);

        // ------------------------------------------------------------- tree

        Node NewLeaf() => new Node { Blocks = new Block[FanOut + 1] };
        Node NewBranch() => new Node { Kids = new Node[FanOut + 1] };

        void InsertBlock(int index, Block block)
        {
            Node split = Insert(m_Root, index, block);
            if (split == null) return;
            Node root = NewBranch();
            root.Kids[0] = m_Root;
            root.Kids[1] = split;
            root.Count = 2;
            root.Refresh();
            m_Root = root;
        }

        Node Insert(Node node, int index, Block block)
        {
            if (node.IsLeaf)
            {
                Array.Copy(node.Blocks, index, node.Blocks, index + 1, node.Count - index);
                node.Blocks[index] = block;
                node.Count++;
                if (node.Count <= FanOut)
                {
                    node.Refresh();
                    return null;
                }
                Node right = NewLeaf();
                int half = node.Count / 2;
                right.Count = node.Count - half;
                Array.Copy(node.Blocks, half, right.Blocks, 0, right.Count);
                Array.Clear(node.Blocks, half, right.Count);
                node.Count = half;
                node.Refresh();
                right.Refresh();
                return right;
            }
            int i = 0;
            for (; i < node.Count - 1; i++)
            {
                if (index <= node.Kids[i].BlockTotal) break;
                index -= node.Kids[i].BlockTotal;
            }
            Node split = Insert(node.Kids[i], index, block);
            if (split != null)
            {
                Array.Copy(node.Kids, i + 1, node.Kids, i + 2, node.Count - i - 1);
                node.Kids[i + 1] = split;
                node.Count++;
            }
            if (node.Count <= FanOut)
            {
                node.Refresh();
                return null;
            }
            Node rightBranch = NewBranch();
            int middle = node.Count / 2;
            rightBranch.Count = node.Count - middle;
            Array.Copy(node.Kids, middle, rightBranch.Kids, 0, rightBranch.Count);
            Array.Clear(node.Kids, middle, rightBranch.Count);
            node.Count = middle;
            node.Refresh();
            rightBranch.Refresh();
            return rightBranch;
        }

        // Removal drops only what empties. Depth never grows from it, so queries stay logarithmic in the most blocks there ever were.
        void RemoveBlock(int index)
        {
            Remove(m_Root, index);
            while (!m_Root.IsLeaf && m_Root.Count == 1) m_Root = m_Root.Kids[0];
            if (!m_Root.IsLeaf && m_Root.Count == 0) m_Root = NewLeaf();
        }

        static void Remove(Node node, int index)
        {
            if (node.IsLeaf)
            {
                Array.Copy(node.Blocks, index + 1, node.Blocks, index, node.Count - index - 1);
                node.Count--;
                node.Blocks[node.Count] = null;
                node.Refresh();
                return;
            }
            int i = 0;
            for (; i < node.Count; i++)
            {
                if (index < node.Kids[i].BlockTotal) break;
                index -= node.Kids[i].BlockTotal;
            }
            Remove(node.Kids[i], index);
            if (node.Kids[i].Count == 0)
            {
                Array.Copy(node.Kids, i + 1, node.Kids, i, node.Count - i - 1);
                node.Count--;
                node.Kids[node.Count] = null;
            }
            node.Refresh();
        }

        // ------------------------------------------------------------- edits

        public void Insert(int position, ReadOnlySpan<char> text) => Replace(position, 0, text);
        public void Delete(int position, int length) => Replace(position, length, ReadOnlySpan<char>.Empty);

        public void SetText(ReadOnlySpan<char> text) => Replace(0, Length, text);

        /// <summary>Swaps a span. Only the blocks it touches are read or made again.</summary>
        public void Replace(int position, int removed, ReadOnlySpan<char> inserted)
        {
            CheckPosition(position, Length);
            CheckPosition(position + removed, Length);
            if (removed == 0 && inserted.IsEmpty) return;

            int total = BlockCount;
            int first = total == 0 ? 0 : BlockAtChar(position);
            // An edit at a block's edge belongs with the block before it when that one is still small enough to grow.
            int firstStart = total == 0 ? 0 : CharOfBlock(first);
            if (first > 0 && position == firstStart) first--;
            int last = total == 0 ? -1 : position + removed <= firstStart && removed == 0 ? first : BlockAtChar(Math.Max(position + removed - 1, 0));
            if (last < first && total > 0) last = first;

            // Pairs that an edit could join or part must end up in one block: CR with LF, and a surrogate with its other half.
            while (total > 0)
            {
                int start = CharOfBlock(first), end = CharOfBlock(last + 1);
                bool widened = false;
                if (first > 0 && position == start || first > 0 && StartsWithSecondHalf(first, inserted, position, start))
                {
                    first--;
                    widened = true;
                }
                if (last + 1 < total && (position + removed == end || LastIsFirstHalf(last, inserted, position, removed, end)))
                {
                    last++;
                    widened = true;
                }
                if (!widened) break;
                // One step each way covers any join: both neighbours are now inside.
                break;
            }

            int regionStart = total > 0 ? CharOfBlock(first) : 0;
            int regionEnd = total > 0 ? CharOfBlock(last + 1) : 0;

            // Small leftovers join a neighbour, so edits do not leave a trail of tiny blocks.
            if (total > 0 && regionEnd - regionStart - removed + inserted.Length < MergeBelow)
            {
                if (last + 1 < total)
                {
                    last++;
                    regionEnd = CharOfBlock(last + 1);
                }
                else if (first > 0)
                {
                    first--;
                    regionStart = CharOfBlock(first);
                }
            }

            int regionLength = regionEnd - regionStart;
            int newLength = regionLength - removed + inserted.Length;
            var merged = new char[newLength];
            int head = position - regionStart;
            if (head > 0) CopyTo(regionStart, new Span<char>(merged, 0, head));
            inserted.CopyTo(new Span<char>(merged, head, inserted.Length));
            int tail = regionEnd - (position + removed);
            if (tail > 0) CopyTo(position + removed, new Span<char>(merged, head + inserted.Length, tail));

            if (total > 0)
            {
                for (int i = last; i >= first; i--)
                {
                    if (BlockAtIndex(i, out _).Measured) MeasuredBlocks--;
                    RemoveBlock(i);
                }
            }

            List<Block> blocks = RopeScanner.SplitChars(merged, m_Triggers);
            int at = total > 0 ? first : 0;
            LastEditFirstBlock = at;
            foreach (Block block in blocks)
            {
                if (EstimateNewBlock != null) block.Height = EstimateNewBlock(block.Codepoints, block.Paragraphs);
                InsertBlock(at++, block);
            }

            int old = Version;
            Version++;
            Changed?.Invoke(new LetteraRopePatch(position, removed, inserted.Length, old, Version));
        }

        bool StartsWithSecondHalf(int block, ReadOnlySpan<char> inserted, int position, int blockStart)
        {
            if (position != blockStart) return false;
            return !inserted.IsEmpty && (inserted[0] == '\n' || char.IsLowSurrogate(inserted[0]));
        }

        bool LastIsFirstHalf(int block, ReadOnlySpan<char> inserted, int position, int removed, int blockEnd)
        {
            if (position + removed != blockEnd || inserted.IsEmpty) return false;
            char last = inserted[inserted.Length - 1];
            return last == '\r' || char.IsHighSurrogate(last);
        }

        /// <summary>A view of a span that keeps its content after later edits to this rope. Whole blocks are shared.</summary>
        public LetteraRope Slice(int start, int length)
        {
            CheckPosition(start, Length);
            CheckPosition(start + length, Length);
            var slice = new LetteraRope(m_Triggers, null);
            if (length == 0) return slice;
            int at = 0, position = start, end = start + length;
            while (position < end)
            {
                Block block = FindByChar(position, out int index, out int chars, out _, out _);
                bool whole = position == chars && chars + block.Chars <= end;
                if (whole)
                {
                    slice.InsertBlock(at++, new Block
                    {
                        Owned = block.Owned, Source = block.Source, Offset = block.Offset, ByteLength = block.ByteLength, Chars = block.Chars,
                        Codepoints = block.Codepoints, Paragraphs = block.Paragraphs, Flags = block.Flags,
                    });
                    position += block.Chars;
                    continue;
                }
                int take = Math.Min(chars + block.Chars, end) - position;
                ReadOnlySpan<char> text = CharsOf(block).Slice(position - chars, take);
                foreach (Block part in RopeScanner.SplitChars(text, m_Triggers)) slice.InsertBlock(at++, part);
                position += take;
            }
            return slice;
        }

        /// <summary>For tests: every block keeps to the size rules, and the totals add up.</summary>
        internal void CheckInvariants()
        {
            CheckNode(m_Root);
            for (int b = 0; b < BlockCount; b++)
            {
                Block block = BlockAtIndex(b, out _);
                if (block.Chars > LimitChars + 4) throw new InvalidOperationException($"Block {b} has {block.Chars} chars.");
                if (block.Chars == 0) throw new InvalidOperationException($"Block {b} is empty.");
                ReadOnlySpan<char> text = CharsOf(block);
                if (char.IsHighSurrogate(text[text.Length - 1]) && b + 1 < BlockCount && char.IsLowSurrogate(CharsOf(BlockAtIndex(b + 1, out _))[0]))
                    throw new InvalidOperationException($"Block {b} splits a surrogate pair.");
                if (text[text.Length - 1] == '\r' && b + 1 < BlockCount && CharsOf(BlockAtIndex(b + 1, out _))[0] == '\n')
                    throw new InvalidOperationException($"Block {b} splits a CRLF.");
            }
        }

        static void CheckNode(Node node)
        {
            int chars = node.Chars, cps = node.Codepoints, paragraphs = node.Paragraphs, total = node.BlockTotal;
            node.Refresh();
            if (chars != node.Chars || cps != node.Codepoints || paragraphs != node.Paragraphs || total != node.BlockTotal) throw new InvalidOperationException("A node's totals are out of date.");
            if (!node.IsLeaf)
                for (int i = 0; i < node.Count; i++)
                    CheckNode(node.Kids[i]);
        }
    }

    // ------------------------------------------------------------- bytes

    /// <summary>Where a document's undecoded bytes are.</summary>
    internal abstract class RopeBytes : IDisposable
    {
        public abstract long Length { get; }
        public abstract void Read(long offset, Span<byte> destination);
        public virtual void Dispose() { }
    }

    internal sealed class RopeArrayBytes : RopeBytes
    {
        readonly byte[] m_Bytes;
        public RopeArrayBytes(byte[] bytes) => m_Bytes = bytes;
        public override long Length => m_Bytes.Length;
        public override void Read(long offset, Span<byte> destination) => new ReadOnlySpan<byte>(m_Bytes, (int)offset, destination.Length).CopyTo(destination);
    }

    internal sealed unsafe class RopeMappedBytes : RopeBytes
    {
        readonly MemoryMappedFile m_File;
        readonly MemoryMappedViewAccessor m_View;
        readonly long m_Length;
        byte* m_Pointer;

        public RopeMappedBytes(string path, long length)
        {
            m_Length = length;
            // Opening the stream ourselves is what lets the file be shared for reading.
            var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            m_File = MemoryMappedFile.CreateFromFile(stream, null, 0, MemoryMappedFileAccess.Read, HandleInheritability.None, false);
            m_View = m_File.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read);
            m_View.SafeMemoryMappedViewHandle.AcquirePointer(ref m_Pointer);
        }

        public override long Length => m_Length;

        public override void Read(long offset, Span<byte> destination) => new ReadOnlySpan<byte>(m_Pointer + offset, destination.Length).CopyTo(destination);

        public override void Dispose()
        {
            if (m_Pointer != null)
            {
                m_View.SafeMemoryMappedViewHandle.ReleasePointer();
                m_Pointer = null;
            }
            m_View.Dispose();
            m_File.Dispose();
        }
    }

    internal sealed class RopeChunkedBytes : RopeBytes
    {
        readonly FileStream m_Stream;

        public RopeChunkedBytes(string path) => m_Stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 65536);

        public override long Length => m_Stream.Length;

        public override void Read(long offset, Span<byte> destination)
        {
            m_Stream.Position = offset;
            int total = 0;
            while (total < destination.Length)
            {
                int read = m_Stream.Read(destination.Slice(total));
                if (read <= 0) throw new EndOfStreamException();
                total += read;
            }
        }

        public override void Dispose() => m_Stream.Dispose();
    }

    // The recently read blocks, decoded. A mapped document stays undecoded except for these.
    internal sealed class RopeBlockCache
    {
        readonly int m_Capacity;
        readonly Dictionary<LetteraRope.Block, LinkedListNode<(LetteraRope.Block block, char[] chars)>> m_Lookup = new Dictionary<LetteraRope.Block, LinkedListNode<(LetteraRope.Block, char[])>>();
        readonly LinkedList<(LetteraRope.Block block, char[] chars)> m_Order = new LinkedList<(LetteraRope.Block, char[])>();

        public RopeBlockCache(int capacity) => m_Capacity = capacity;

        public ReadOnlySpan<char> Decode(LetteraRope.Block block)
        {
            if (m_Lookup.TryGetValue(block, out LinkedListNode<(LetteraRope.Block block, char[] chars)> node))
            {
                if (m_Order.First != node)
                {
                    m_Order.Remove(node);
                    m_Order.AddFirst(node);
                }
                return new ReadOnlySpan<char>(node.Value.chars, 0, block.Chars);
            }
            char[] chars;
            if (m_Order.Count >= m_Capacity)
            {
                // The oldest one's arrays and node go on to the newest, so reading on through a document allocates nothing.
                node = m_Order.Last;
                m_Order.RemoveLast();
                m_Lookup.Remove(node.Value.block);
                chars = node.Value.chars.Length >= block.Chars ? node.Value.chars : new char[Math.Max(block.Chars, LetteraRope.TargetChars * 2)];
                node.Value = (block, chars);
            }
            else
            {
                chars = new char[Math.Max(block.Chars, LetteraRope.TargetChars * 2)];
                node = new LinkedListNode<(LetteraRope.Block, char[])>((block, chars));
            }
            RopeScanner.DecodeUtf8(block, chars);
            m_Order.AddFirst(node);
            m_Lookup[block] = node;
            return new ReadOnlySpan<char>(chars, 0, block.Chars);
        }
    }

    // ------------------------------------------------------------- scanning

    // Finds where blocks end and what they hold. The same rules run on owned chars and on undecoded bytes, so both cut alike.
    internal static class RopeScanner
    {
        struct Mark
        {
            public long Position;
            public int Chars, Codepoints, Paragraphs;
            public bool Set;
        }

        struct Counter
        {
            public int Chars, Codepoints, Paragraphs;
            public RopeBlockFlags Flags;
            public Mark AtBreak, AtCluster;
            public long Less;
            public int LessChars;
            public bool InTag;
        }

        static bool IsExtender(int cp) =>
            cp >= 0x300 && cp <= 0x36F || cp == 0x200D || cp >= 0xFE00 && cp <= 0xFE0F || cp >= 0x1F3FB && cp <= 0x1F3FF || cp >= 0x1AB0 && cp <= 0x1AFF ||
            cp >= 0x20D0 && cp <= 0x20FF || cp >= 0xE0020 && cp <= 0xE007F || cp >= 0xE0100 && cp <= 0xE01EF;

        static bool IsStrongRtl(int cp) => cp >= 0x590 && cp <= 0x8FF || cp >= 0xFB1D && cp <= 0xFDFF || cp >= 0xFE70 && cp <= 0xFEFF || cp >= 0x10800 && cp <= 0x10FFF || cp >= 0x1E800 && cp <= 0x1EFFF;

        static bool IsUnspaced(int cp) => cp >= 0xE00 && cp <= 0xEFF || cp >= 0x1000 && cp <= 0x109F || cp >= 0x1780 && cp <= 0x17FF;

        static bool IsIdeograph(int cp) => cp >= 0x3040 && cp <= 0x30FF || cp >= 0x3400 && cp <= 0x9FFF || cp >= 0xAC00 && cp <= 0xD7AF || cp >= 0x20000 && cp <= 0x3FFFF;

        // Adds a codepoint that ends at a position. True when a block should end right after it.
        static bool Step(ref Counter c, int cp, int units, long before, long after, bool crBeforeLf, LetteraMarkupTriggers triggers)
        {
            // A cluster cannot be cut before something that extends it, or between CR and LF.
            if (!IsExtender(cp) && !(cp == '\n' && c.Chars > 0 && c.AtCluster.Set && c.AtCluster.Position == before && false))
                c.AtCluster = new Mark { Position = before, Chars = c.Chars, Codepoints = c.Codepoints, Paragraphs = c.Paragraphs, Set = c.Chars > 0 };

            c.Chars += units;
            c.Codepoints++;
            if (cp >= 128) c.Flags &= ~RopeBlockFlags.AsciiOnly;
            if (triggers == null || triggers.Contains(cp)) c.Flags |= RopeBlockFlags.HasMarkupTrigger;
            if (IsStrongRtl(cp)) c.Flags |= RopeBlockFlags.HasStrongRtl;
            if (IsUnspaced(cp) || IsIdeograph(cp)) c.Flags |= RopeBlockFlags.HasUnspaced;

            if (cp == '<')
            {
                c.InTag = true;
                c.Less = before;
                c.LessChars = c.Chars - units;
            }
            else if (cp == '>') c.InTag = false;

            bool separator = cp == '\n' || cp == 0x85 || cp == 0x2029 || (cp == '\r' && !crBeforeLf);
            if (separator)
            {
                c.Paragraphs++;
                if (c.Chars >= LetteraRope.TargetChars)
                {
                    c.Flags |= RopeBlockFlags.EndsParagraph;
                    return true;
                }
            }
            if (cp == '\r' && crBeforeLf) return false;
            bool tagOpen = c.InTag && c.Chars - c.LessChars < 256;
            if ((cp == ' ' || cp == '\t' || IsIdeograph(cp)) && !tagOpen)
                c.AtBreak = new Mark { Position = after, Chars = c.Chars, Codepoints = c.Codepoints, Paragraphs = c.Paragraphs, Set = true };
            return false;
        }

        static Mark ChooseCut(ref Counter c, long position)
        {
            if (c.AtBreak.Set) return c.AtBreak;
            if (c.AtCluster.Set) return c.AtCluster;
            return new Mark { Position = position, Chars = c.Chars, Codepoints = c.Codepoints, Paragraphs = c.Paragraphs, Set = true };
        }

        // After a cut the counter goes on with what lay past it. Flags stay as they were: a flag that says "may hold" is safe to keep.
        static void Rest(ref Counter c, in Mark cut, RopeBlockFlags keep)
        {
            c.Chars -= cut.Chars;
            c.Codepoints -= cut.Codepoints;
            c.Paragraphs -= cut.Paragraphs;
            c.Flags = keep & ~RopeBlockFlags.EndsParagraph;
            c.AtBreak = default;
            c.AtCluster = default;
            c.InTag = false;
        }

        /// <summary>Takes every character through Step, for a test that holds the stretches to it.</summary>
        internal static bool NoPlainStretches;

        static bool IsPlainAscii(char c) => c != '<' && c != '>' && c != '\n' && c != '\r';

        internal static List<LetteraRope.Block> SplitChars(ReadOnlySpan<char> text, LetteraMarkupTriggers triggers)
        {
            var blocks = new List<LetteraRope.Block>(text.Length / LetteraRope.TargetChars + 1);
            var c = new Counter { Flags = RopeBlockFlags.AsciiOnly };
            int start = 0, i = 0;

            void Emit(ReadOnlySpan<char> all, int from, int to, int cps, int paragraphs, RopeBlockFlags flags)
            {
                var owned = new char[to - from];
                all.Slice(from, to - from).CopyTo(owned);
                blocks.Add(new LetteraRope.Block { Owned = owned, Chars = to - from, Codepoints = cps, Paragraphs = paragraphs, Flags = flags });
            }

            while (i < text.Length)
            {
                char first = text[i];
                // A stretch of letters and spaces with nothing else Step looks at: each one starts a cluster and counts one, a space outside a tag
                // is a place to cut, and the block's limit is the only cut that can come.
                if (first < 128 && !NoPlainStretches && !c.InTag && IsPlainAscii(first))
                {
                    int limit = Math.Min(text.Length, i + LetteraRope.LimitChars - c.Chars);
                    int j = i, afterSpace = -1;
                    bool trigger = triggers == null;
                    while (j < limit)
                    {
                        char next = text[j];
                        if (next >= 128 || !IsPlainAscii(next)) break;
                        if (!trigger && triggers.Contains(next)) trigger = true;
                        j++;
                        if (next == ' ' || next == '\t') afterSpace = j;
                    }
                    if (j > i)
                    {
                        int n = j - i;
                        if (afterSpace >= 0)
                        {
                            int upTo = afterSpace - i;
                            c.AtBreak = new Mark { Position = afterSpace, Chars = c.Chars + upTo, Codepoints = c.Codepoints + upTo, Paragraphs = c.Paragraphs, Set = true };
                        }
                        c.AtCluster = new Mark { Position = j - 1, Chars = c.Chars + n - 1, Codepoints = c.Codepoints + n - 1, Paragraphs = c.Paragraphs, Set = c.Chars + n - 1 > 0 };
                        c.Chars += n;
                        c.Codepoints += n;
                        if (trigger) c.Flags |= RopeBlockFlags.HasMarkupTrigger;
                        i = j;
                        if (c.Chars >= LetteraRope.LimitChars)
                        {
                            Mark cut = ChooseCut(ref c, i);
                            Emit(text, start, (int)cut.Position, cut.Codepoints, cut.Paragraphs, c.Flags & ~RopeBlockFlags.EndsParagraph);
                            start = (int)cut.Position;
                            Rest(ref c, cut, c.Flags);
                        }
                        continue;
                    }
                }
                int cp = first, units = 1;
                if (char.IsHighSurrogate(first) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
                {
                    cp = char.ConvertToUtf32(first, text[i + 1]);
                    units = 2;
                }
                bool crLf = cp == '\r' && i + 1 < text.Length && text[i + 1] == '\n';
                bool end = Step(ref c, cp, units, i, i + units, crLf, triggers);
                i += units;
                if (end)
                {
                    Emit(text, start, i, c.Codepoints, c.Paragraphs, c.Flags);
                    start = i;
                    c = new Counter { Flags = RopeBlockFlags.AsciiOnly };
                }
                else if (c.Chars >= LetteraRope.LimitChars && !crLf)
                {
                    Mark cut = ChooseCut(ref c, i);
                    Emit(text, start, (int)cut.Position, cut.Codepoints, cut.Paragraphs, c.Flags & ~RopeBlockFlags.EndsParagraph);
                    start = (int)cut.Position;
                    Rest(ref c, cut, c.Flags);
                }
            }
            if (i > start) Emit(text, start, i, c.Codepoints, c.Paragraphs, c.Flags);
            return blocks;
        }

        // Decoding by maximal subparts, as Unicode recommends: each bad stretch reads as one U+FFFD, so counts and reads always agree.
        internal static int DecodeOne(ReadOnlySpan<byte> s, int i, out int length)
        {
            byte b0 = s[i];
            if (b0 < 0x80)
            {
                length = 1;
                return b0;
            }
            length = 1;
            int need, min1 = 0x80, max1 = 0xBF, cp;
            if (b0 >= 0xC2 && b0 <= 0xDF) { need = 1; cp = b0 & 0x1F; }
            else if (b0 >= 0xE0 && b0 <= 0xEF)
            {
                need = 2;
                cp = b0 & 0x0F;
                if (b0 == 0xE0) min1 = 0xA0;
                else if (b0 == 0xED) max1 = 0x9F;
            }
            else if (b0 >= 0xF0 && b0 <= 0xF4)
            {
                need = 3;
                cp = b0 & 0x07;
                if (b0 == 0xF0) min1 = 0x90;
                else if (b0 == 0xF4) max1 = 0x8F;
            }
            else return 0xFFFD;
            for (int k = 1; k <= need; k++)
            {
                if (i + k >= s.Length) return 0xFFFD;
                byte next = s[i + k];
                if (next < (k == 1 ? min1 : 0x80) || next > (k == 1 ? max1 : 0xBF)) return 0xFFFD;
                cp = cp << 6 | (next & 0x3F);
                length = k + 1;
            }
            return cp;
        }

        internal static void DecodeUtf8(LetteraRope.Block block, char[] output)
        {
            byte[] rented = System.Buffers.ArrayPool<byte>.Shared.Rent(block.ByteLength);
            var bytes = new Span<byte>(rented, 0, block.ByteLength);
            block.Source.Read(block.Offset, bytes);
            if ((block.Flags & RopeBlockFlags.AsciiOnly) != 0)
            {
                for (int a = 0; a < bytes.Length; a++) output[a] = (char)bytes[a];
            }
            else
            {
                int w = 0, i = 0;
                while (i < bytes.Length)
                {
                    int cp = DecodeOne(bytes, i, out int length);
                    i += length;
                    if (cp < 0x10000) output[w++] = (char)cp;
                    else
                    {
                        cp -= 0x10000;
                        output[w++] = (char)(0xD800 + (cp >> 10));
                        output[w++] = (char)(0xDC00 + (cp & 0x3FF));
                    }
                }
            }
            System.Buffers.ArrayPool<byte>.Shared.Return(rented);
        }

        // One pass over the whole file, keeping only totals. It reads a window at a time, so a truncated sequence at a window's end is read again with what follows.
        internal static void ScanUtf8(RopeBytes source, long begin, LetteraMarkupTriggers triggers, Action<LetteraRope.Block> add)
        {
            const int Window = 1 << 16;
            var buffer = new byte[Window];
            long total = source.Length, base0 = begin, blockStart = begin;
            var c = new Counter { Flags = RopeBlockFlags.AsciiOnly };

            void Emit(long to, int chars, int cps, int paragraphs, RopeBlockFlags flags) =>
                add(new LetteraRope.Block { Source = source, Offset = blockStart, ByteLength = (int)(to - blockStart), Chars = chars, Codepoints = cps, Paragraphs = paragraphs, Flags = flags });

            while (base0 < total)
            {
                int have = (int)Math.Min(Window, total - base0);
                source.Read(base0, new Span<byte>(buffer, 0, have));
                var span = new ReadOnlySpan<byte>(buffer, 0, have);
                bool last = base0 + have >= total;
                int i = 0;
                while (i < have)
                {
                    if (!last && i + 5 > have) break;
                    int cp = DecodeOne(span, i, out int length);
                    if (cp == 0xFFFD && !(span[i] == 0xEF && length == 3)) c.Flags |= RopeBlockFlags.InvalidSequences;
                    long before = base0 + i, after = before + length;
                    int units = cp >= 0x10000 ? 2 : 1;
                    bool crLf = cp == '\r' && i + 1 < have && span[i + 1] == '\n';
                    bool end = Step(ref c, cp, units, before, after, crLf, triggers);
                    i += length;
                    if (end)
                    {
                        Emit(after, c.Chars, c.Codepoints, c.Paragraphs, c.Flags);
                        blockStart = after;
                        c = new Counter { Flags = RopeBlockFlags.AsciiOnly };
                    }
                    else if (c.Chars >= LetteraRope.LimitChars && !crLf)
                    {
                        Mark cut = ChooseCut(ref c, after);
                        Emit(cut.Position, cut.Chars, cut.Codepoints, cut.Paragraphs, c.Flags & ~RopeBlockFlags.EndsParagraph);
                        blockStart = cut.Position;
                        Rest(ref c, cut, c.Flags);
                    }
                }
                base0 += i;
            }
            if (total > blockStart) Emit(total, c.Chars, c.Codepoints, c.Paragraphs, c.Flags);
        }
    }

    public enum LetteraFileEncoding { Auto, Utf8, Utf16, Utf32 }
    public enum LetteraFileBackend { Auto, Mapped, Chunked, InMemory }

    public struct LetteraFileOptions
    {
        public LetteraFileEncoding Encoding;
        public LetteraFileBackend Backend;
        public LetteraMarkupTriggers Triggers;
    }

    /// <summary>Opens a text file as a rope, with one scanning pass and nothing decoded up front.</summary>
    public static class LetteraFileSource
    {
        public static LetteraRope Load(string path, LetteraFileOptions options = default)
        {
            if (!File.Exists(path)) throw new FileNotFoundException("Lettera: no such document.", path);
            long length = new FileInfo(path).Length;
            LetteraFileEncoding encoding = options.Encoding;
            if (encoding == LetteraFileEncoding.Auto) encoding = Sniff(path, length);
            // Nothing is gained by mapping these two: they become owned blocks.
            if (encoding == LetteraFileEncoding.Utf16 || encoding == LetteraFileEncoding.Utf32)
            {
                if (length > int.MaxValue) throw new IOException($"Lettera: '{path}' is larger than a document can be.");
                return LetteraRope.FromString(DecodeWide(File.ReadAllBytes(path), encoding), options.Triggers);
            }
            if (length > int.MaxValue) throw new IOException($"Lettera: '{path}' is larger than a document can be.");

            LetteraFileBackend backend = options.Backend;
            if (backend == LetteraFileBackend.Auto) backend = UnityEngine.Application.platform == UnityEngine.RuntimePlatform.WebGLPlayer ? LetteraFileBackend.InMemory : LetteraFileBackend.Mapped;
            if (length == 0) return LetteraRope.FromString("", options.Triggers);
            RopeBytes bytes = Open(path, length, backend);
            return LetteraRope.FromBytes(bytes, options.Triggers, LetteraRope.Utf8BomLength(bytes));
        }

        static RopeBytes Open(string path, long length, LetteraFileBackend backend)
        {
            if (backend == LetteraFileBackend.InMemory) return new RopeArrayBytes(File.ReadAllBytes(path));
            if (backend == LetteraFileBackend.Chunked) return new RopeChunkedBytes(path);
            try
            {
                return new RopeMappedBytes(path, length);
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is NotSupportedException || e is PlatformNotSupportedException)
            {
                // A large mapping can fail on a small address space, which is what reading on demand is for.
                return new RopeChunkedBytes(path);
            }
        }

        static LetteraFileEncoding Sniff(string path, long length)
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            Span<byte> head = stackalloc byte[4];
            int read = stream.Read(head);
            if (read >= 4 && (head[0] == 0xFF && head[1] == 0xFE && head[2] == 0 && head[3] == 0 || head[0] == 0 && head[1] == 0 && head[2] == 0xFE && head[3] == 0xFF)) return LetteraFileEncoding.Utf32;
            if (read >= 2 && (head[0] == 0xFF && head[1] == 0xFE || head[0] == 0xFE && head[1] == 0xFF)) return LetteraFileEncoding.Utf16;
            return LetteraFileEncoding.Utf8;
        }

        static string DecodeWide(byte[] bytes, LetteraFileEncoding encoding)
        {
            bool big = encoding == LetteraFileEncoding.Utf16 ? bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF : bytes.Length >= 4 && bytes[0] == 0 && bytes[1] == 0 && bytes[2] == 0xFE && bytes[3] == 0xFF;
            bool bom = encoding == LetteraFileEncoding.Utf16 ? bytes.Length >= 2 && (big || bytes[0] == 0xFF && bytes[1] == 0xFE) : bytes.Length >= 4 && (big || bytes[0] == 0xFF && bytes[1] == 0xFE && bytes[2] == 0 && bytes[3] == 0);
            int skip = bom ? (encoding == LetteraFileEncoding.Utf16 ? 2 : 4) : 0;
            Encoding decoder = encoding == LetteraFileEncoding.Utf16 ? new UnicodeEncoding(big, false) : (Encoding)new UTF32Encoding(big, false);
            return decoder.GetString(bytes, skip, bytes.Length - skip);
        }
    }
}

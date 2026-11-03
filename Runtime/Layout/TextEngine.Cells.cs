using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using FirstSymbol.Lettera.Unicode;

namespace FirstSymbol.Lettera
{
    /// <summary>
    /// Plain values in native memory, read and written in place as an array is, its length checked as an array's is. A text keeps what it holds
    /// per codepoint in these, out of the collector's sight: a long text's megabytes neither start collections nor are marked by them, and the
    /// pages of a buffer that no paragraph reached yet are never touched.
    /// </summary>
    internal unsafe struct Cells<T> where T : unmanaged
    {
        internal T* Ptr;
        internal int Length;

        public ref T this[int index]
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                if ((uint)index >= (uint)Length) OutOfRange(index, Length);
                return ref Ptr[index];
            }
        }

        public Span<T> Slice(int start, int count)
        {
            if ((uint)start > (uint)Length || (uint)count > (uint)(Length - start)) OutOfRange(start + count, Length);
            return new Span<T>(Ptr + start, count);
        }

        public void Clear(int start, int count) => Slice(start, count).Clear();

        public static implicit operator ReadOnlySpan<T>(Cells<T> cells) => new ReadOnlySpan<T>(cells.Ptr, cells.Length);

        // For a fixed statement: an empty buffer gives a null pointer, as an empty array does.
        public ref T GetPinnableReference() => ref *Ptr;

        [MethodImpl(MethodImplOptions.NoInlining)]
        static void OutOfRange(int index, int length) => throw new IndexOutOfRangeException("Index " + index + " outside a buffer of " + length);
    }

    // An engine has no end of its own and is dropped as any object is, so what frees its native buffers is a small object of its own beside it,
    // and not the engine: a finalizer on the engine would keep all it references alive for one more collection.
    internal sealed class CellBlocks
    {
        internal IntPtr Text, Shape, Extras;

        ~CellBlocks()
        {
            Free(ref Text);
            Free(ref Shape);
            Free(ref Extras);
        }

        internal static void Free(ref IntPtr block)
        {
            if (block == IntPtr.Zero) return;
            Marshal.FreeHGlobal(block);
            block = IntPtr.Zero;
        }
    }

    internal sealed unsafe partial class TextEngine
    {
        // Three blocks a text: its codepoints, what shaping writes per codepoint, and what layout effects add. Each comes whole from one allocation.
        CellBlocks m_Blocks;
        int m_ShapeRoom = -1, m_ExtrasRoom = -1;

        CellBlocks Blocks => m_Blocks ?? (m_Blocks = new CellBlocks());

        static IntPtr Allocate(long bytes) => Marshal.AllocHGlobal((IntPtr)Math.Max(bytes, 16));

        // Grows the codepoints to at least a size, keeping what they hold. What is new reads as zeros, as a new array's would.
        void EnsureText(int size)
        {
            if (m_Text.Length >= size) return;
            int room = Math.Max(Math.Max(size, SmallestBuffer), m_Text.Length * 2);
            IntPtr block = Allocate(room * 4L);
            var text = new Cells<int> { Ptr = (int*)block, Length = room };
            int kept = m_Text.Length;
            if (kept > 0) Buffer.MemoryCopy(m_Text.Ptr, text.Ptr, room * 4L, kept * 4L);
            text.Clear(kept, room - kept);
            CellBlocks blocks = Blocks;
            CellBlocks.Free(ref blocks.Text);
            blocks.Text = block;
            m_Text = text;
        }

        // What shaping writes per codepoint, each to room + 1, in one block. With fresh set, what is new is left as it comes: a long text left for
        // later writes every value of a paragraph before anything reads it, and pages of the block that no paragraph reaches stay untouched.
        void AllocateShape(int room, bool fresh)
        {
            int n = room + 1;
            long bytes = n * 17L;
            IntPtr block = Allocate(bytes);
            byte* at = (byte*)block;
            var shapeText = new Cells<int> { Ptr = (int*)at, Length = n }; at += n * 4L;
            var cpRun = new Cells<int> { Ptr = (int*)at, Length = n }; at += n * 4L;
            var advance = new Cells<float> { Ptr = (float*)at, Length = n }; at += n * 4L;
            var scripts = new Cells<UnicodeScript> { Ptr = (UnicodeScript*)at, Length = n }; at += n * 2L;
            var levels = new Cells<byte> { Ptr = at, Length = n }; at += n;
            var boundaries = new Cells<bool> { Ptr = (bool*)at, Length = n }; at += n;
            var breaks = new Cells<LineBreakOpportunity> { Ptr = (LineBreakOpportunity*)at, Length = n };
            int kept = m_ShapeRoom + 1;
            if (kept > 0)
            {
                Keep(m_ShapeText, shapeText, kept, fresh);
                Keep(m_CpRun, cpRun, kept, fresh);
                Keep(m_CpAdvanceEm, advance, kept, fresh);
                Keep(m_Scripts, scripts, kept, fresh);
                Keep(m_Levels, levels, kept, fresh);
                Keep(m_Boundaries, boundaries, kept, fresh);
                Keep(m_Breaks, breaks, kept, fresh);
            }
            else if (!fresh)
            {
                new Span<byte>((void*)block, (int)bytes).Clear();
            }
            m_ShapeText = shapeText;
            m_CpRun = cpRun;
            m_CpAdvanceEm = advance;
            m_Scripts = scripts;
            m_Levels = levels;
            m_Boundaries = boundaries;
            m_Breaks = breaks;
            CellBlocks blocks = Blocks;
            CellBlocks.Free(ref blocks.Shape);
            blocks.Shape = block;
            m_ShapeRoom = room;
        }

        static void Keep<T>(Cells<T> from, Cells<T> to, int kept, bool fresh) where T : unmanaged
        {
            Buffer.MemoryCopy(from.Ptr, to.Ptr, (long)to.Length * sizeof(T), (long)kept * sizeof(T));
            if (!fresh) to.Clear(kept, to.Length - kept);
        }

        // What layout effects add per codepoint, each to room + 1, in one block that starts as zeros.
        void AllocateExtras(int room)
        {
            int n = room + 1;
            long bytes = n * 18L;
            IntPtr block = Allocate(bytes);
            new Span<byte>((void*)block, (int)bytes).Clear();
            byte* at = (byte*)block;
            m_CpExtraEm = new Cells<float> { Ptr = (float*)at, Length = n }; at += n * 4L;
            m_CpExtraUnits = new Cells<float> { Ptr = (float*)at, Length = n }; at += n * 4L;
            m_CpLeadEm = new Cells<float> { Ptr = (float*)at, Length = n }; at += n * 4L;
            m_CpLeadUnits = new Cells<float> { Ptr = (float*)at, Length = n }; at += n * 4L;
            m_CpKashida = new Cells<bool> { Ptr = (bool*)at, Length = n }; at += n;
            m_CpDivider = new Cells<bool> { Ptr = (bool*)at, Length = n };
            CellBlocks blocks = Blocks;
            CellBlocks.Free(ref blocks.Extras);
            blocks.Extras = block;
            m_ExtrasRoom = room;
        }

        /// <summary>
        /// Gives back the native buffers of an engine made for one measure or one parse, at once rather than when the collector finds it: native
        /// memory does not hasten a collection, so many such engines would otherwise hold it for long. The engine parses again if used.
        /// </summary>
        internal void ReleaseCells()
        {
            if (m_Blocks == null) return;
            CellBlocks.Free(ref m_Blocks.Text);
            CellBlocks.Free(ref m_Blocks.Shape);
            CellBlocks.Free(ref m_Blocks.Extras);
            m_Text = default;
            m_ShapeText = m_CpRun = default;
            m_CpAdvanceEm = default;
            m_Scripts = default;
            m_Levels = default;
            m_Boundaries = default;
            m_Breaks = default;
            m_CpExtraEm = m_CpExtraUnits = m_CpLeadEm = m_CpLeadUnits = default;
            m_CpKashida = m_CpDivider = default;
            m_ShapeRoom = m_ExtrasRoom = -1;
            m_TextLength = 0;
            m_PrevSourceLength = -1;
            SetDirty(DirtyLevel.Parse);
        }
    }
}

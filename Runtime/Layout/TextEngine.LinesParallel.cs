using System;
using System.Collections.Generic;

namespace FirstSymbol.Lettera
{
    // A long text's lines, a stretch of paragraphs per thread. With nothing that carries from one paragraph to the next (spacing, line gaps,
    // indents, removals, inline objects, ruby), a paragraph's lines read only its own cells. Each stretch writes lines of its own; they are joined
    // in reading order and the sums are made in that order too, so the result is what breaking in order gives, to the last bit.
    internal sealed partial class TextEngine
    {
        struct BreakStretch
        {
            public int First, End;
            public bool WordOverflow;
            public List<LetteraLine> Lines;
        }

        /// <summary>How often lines, places and faces went across the threads. Tests read this.</summary>
        internal static readonly int[] SplitPasses = new int[3];

        // Several stretches a thread, so a thread that wakes late still finds work, and the main thread, which starts at once, takes more.
        static int StretchCount(int items) => Math.Min(items, (LetteraWorkers.WorkerCount + 1) * 4);

        bool BreaksInParallel(int first, int end)
        {
            if (end - first < 2 || m_Deferring || m_PendingCount > 0) return false;
            if (m_Aside.Removals.Count > 0 || m_Attributes.HasLayout || m_Attributes.ParagraphSpacings.Count > 0 || m_Attributes.LineGaps.Count > 0 ||
                m_Attributes.LineHeights.Count > 0 || m_Aside.InlineItems.Count > 0 || m_Aside.RubyItems.Count > 0) return false;
            return LetteraWorkers.ShouldRun(m_Paragraphs[end - 1].End - m_Paragraphs[first].Start);
        }

        bool BreakInParallel(List<LetteraLine> lines, ref LineStats stats, float baseSize, float maxWidth, bool wrap, bool emergency, int first, int end)
        {
            if (!BreaksInParallel(first, end)) return false;
            int paragraphs = end - first;
            int count = StretchCount(paragraphs);
            if (m_Aside.Stretches.Length < count) Array.Resize(ref m_Aside.Stretches, count);
            // Stretches of about the same number of codepoints, each at least one paragraph.
            int from = m_Paragraphs[first].Start, total = m_Paragraphs[end - 1].End - from;
            int made = 0, p = first;
            while (p < end && made < count)
            {
                int stretchEnd = made == count - 1 ? end : p + 1;
                long target = (long)total * (made + 1) / count;
                while (stretchEnd < end && end - stretchEnd > count - made - 1 && m_Paragraphs[stretchEnd - 1].End - from < target) stretchEnd++;
                ref BreakStretch stretch = ref m_Aside.Stretches[made++];
                stretch.First = p;
                stretch.End = stretchEnd;
                stretch.WordOverflow = false;
                if (stretch.Lines == null) stretch.Lines = new List<LetteraLine>(16);
                p = stretchEnd;
            }
            m_Aside.StretchSize = baseSize;
            m_Aside.StretchWidth = maxWidth;
            m_Aside.StretchWrap = wrap;
            m_Aside.StretchEmergency = emergency;
            m_Aside.StretchFormat = CurrentLineFormat();
            SplitPasses[0]++;
            LetteraWorkers.Run(made, m_Aside.BreakOne ?? (m_Aside.BreakOne = BreakOneStretch));
            for (int s = 0; s < made; s++)
            {
                ref BreakStretch stretch = ref m_Aside.Stretches[s];
                List<LetteraLine> own = stretch.Lines;
                // One by one: AddRange copies through an array of its own on some runtimes.
                for (int l = 0; l < own.Count; l++)
                {
                    LetteraLine line = own[l];
                    lines.Add(line);
                    if (line.Width + line.Margin > stats.MaxWidth) stats.MaxWidth = line.Width + line.Margin;
                    stats.Height += line.Height + line.GapBefore;
                }
                if (stretch.WordOverflow) stats.WordOverflow = true;
                own.Clear();
            }
            return true;
        }

        // What a window's threads use, taken when its document opens, so the first fast scroll grows nothing.
        void ReserveStretches()
        {
            if (!LetteraWorkers.ShouldRun(LetteraWorkers.ParallelFromCodepoints)) return;
            int count = StretchCount(int.MaxValue);
            if (m_Aside.Stretches.Length < count) Array.Resize(ref m_Aside.Stretches, count);
            for (int s = 0; s < count; s++)
                if (m_Aside.Stretches[s].Lines == null)
                    m_Aside.Stretches[s].Lines = new List<LetteraLine>(16);
            if (m_Aside.FaceStretches.Length < count) Array.Resize(ref m_Aside.FaceStretches, count);
            if (m_Aside.PlaceSinks.Length < count) Array.Resize(ref m_Aside.PlaceSinks, count);
            for (int s = 0; s < count; s++)
                if (m_Aside.PlaceSinks[s] == null)
                    m_Aside.PlaceSinks[s] = new PlaceSink();
            if (m_Aside.PlaceItems.Length < 256) Array.Resize(ref m_Aside.PlaceItems, 256);
            if (m_Aside.AtlasMemos == null) m_Aside.AtlasMemos = new AtlasMemo[AtlasMemoSize];
        }

        void BreakOneStretch(int index)
        {
            ref BreakStretch stretch = ref m_Aside.Stretches[index];
            stretch.Lines.Clear();
            var stats = new LineStats();
            BreakParagraphs(stretch.Lines, ref stats, m_Aside.StretchSize, m_Aside.StretchWidth, m_Aside.StretchWrap, m_Aside.StretchEmergency, stretch.First, stretch.End, m_Aside.StretchFormat);
            stretch.WordOverflow = stats.WordOverflow;
        }
    }
}

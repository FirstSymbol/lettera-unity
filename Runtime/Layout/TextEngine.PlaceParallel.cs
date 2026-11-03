using System;

namespace FirstSymbol.Lettera
{
    // The lines a long text places, a stretch per thread. Where each line stands is worked out in order first. A line all left to right places one
    // glyph per shaped glyph of its runs, in order, so how many glyphs each line puts down is known before any is placed: each stretch then writes
    // straight into its own part of the text's buffer, and the result is placing line after line, to the last bit. Removals, per-character extras
    // and lines with right-to-left text keep the one path.
    internal sealed partial class TextEngine
    {
        // Where a line's glyphs go: the text's own buffer, from a place onwards.
        internal sealed class PlaceSink
        {
            public PlacedGlyph[] Placed = Array.Empty<PlacedGlyph>();
            public int Count, End;
            public byte[] Levels = Array.Empty<byte>();
            public int[] Order = Array.Empty<int>();
            public int FirstItem, EndItem;
        }

        struct PlaceItem
        {
            public int Line, Glyphs;
            public float X, Baseline;
            public LineStretch Stretch;
        }

        bool PlacesInParallel => m_TextLength >= LetteraWorkers.ParallelFromCodepoints / 2 && m_Aside.Removals.Count == 0 && !m_Attributes.HasLayout && m_FitTracking == 0 &&
                                 LetteraWorkers.ShouldRun(LetteraWorkers.ParallelFromCodepoints);

        void AddPlaceItem(int line, float x, float baseline, in LineStretch stretch)
        {
            if (m_Aside.PlaceItems.Length <= m_Aside.PlaceItemCount) Array.Resize(ref m_Aside.PlaceItems, Math.Max(64, m_Aside.PlaceItems.Length * 2));
            m_Aside.PlaceItems[m_Aside.PlaceItemCount++] = new PlaceItem { Line = line, X = x, Baseline = baseline, Stretch = stretch };
        }

        // How many glyphs a line all left to right places, or -1 when it is not one.
        int GlyphsOfPlainLine(in LetteraLine line)
        {
            Paragraph paragraph = m_Paragraphs[line.Paragraph];
            if (paragraph.Level != 0 || !AllZero(m_Levels, line.Start, line.VisibleEnd)) return -1;
            int glyphs = 0;
            for (int c = line.Start; c < line.VisibleEnd;)
            {
                int runIndex = m_CpRun[c];
                int stop = c + 1;
                while (stop < line.VisibleEnd && m_CpRun[stop] == runIndex) stop++;
                Run run = m_Runs[runIndex];
                if (run.Instance != null)
                {
                    if ((run.Level & 1) != 0) return -1;
                    // Clusters only grow along a left-to-right run: the fragment's glyphs are those from its first cluster up to the next fragment's.
                    glyphs += FirstGlyph(run, stop, stop) - FirstGlyph(run, c, stop);
                }
                c = stop;
            }
            return glyphs;
        }

        void PlaceItems()
        {
            int items = m_Aside.PlaceItemCount;
            PlaceItem[] list = m_Aside.PlaceItems;
            int total = 0;
            for (int i = 0; i < items && total >= 0; i++)
            {
                int glyphs = GlyphsOfPlainLine(m_Lines[list[i].Line]);
                list[i].Glyphs = glyphs;
                total = glyphs < 0 ? -1 : total + glyphs;
            }
            // Placing a glyph costs about what two codepoints' analysis does.
            if (items < 2 || total < 0 || !LetteraWorkers.ShouldRun(total * 2))
            {
                PlaceItemsInOrder(0);
                return;
            }
            int stretches = StretchCount(items);
            if (m_Aside.PlaceSinks.Length < stretches) Array.Resize(ref m_Aside.PlaceSinks, stretches);
            // The buffer grows as glyph after glyph would have grown it.
            while (m_Placed.Length < m_PlacedCount + total) EnsureSize(ref m_Placed, m_Placed.Length + 1);
            int made = 0, at = 0, seen = 0, offset = m_PlacedCount;
            while (at < items && made < stretches)
            {
                PlaceSink sink = m_Aside.PlaceSinks[made] ?? (m_Aside.PlaceSinks[made] = new PlaceSink());
                long target = (long)total * (made + 1) / stretches;
                int end = at, glyphs = 0;
                do
                {
                    glyphs += list[end].Glyphs;
                    end++;
                } while (end < items && (made == stretches - 1 || (seen + glyphs < target && items - end > stretches - made - 1)));
                sink.Placed = m_Placed;
                sink.Count = offset;
                sink.End = offset + glyphs;
                sink.FirstItem = at;
                sink.EndItem = end;
                seen += glyphs;
                offset += glyphs;
                made++;
                at = end;
            }
            SplitPasses[1]++;
            LetteraWorkers.Run(made, m_Aside.PlaceOne ?? (m_Aside.PlaceOne = PlaceStretch));
            bool exact = true;
            for (int s = 0; s < made; s++)
            {
                PlaceSink sink = m_Aside.PlaceSinks[s];
                exact &= sink.Count == sink.End && ReferenceEquals(sink.Placed, m_Placed);
                sink.Placed = Array.Empty<PlacedGlyph>();
            }
            if (exact)
            {
                m_PlacedCount = offset;
                return;
            }
            // Counted wrong: never seen, and placing in order is always right.
            PlaceItemsInOrder(0);
        }

        void PlaceItemsInOrder(int from)
        {
            PlaceItem[] list = m_Aside.PlaceItems;
            for (int i = from; i < m_Aside.PlaceItemCount; i++) PlaceOneItem(list[i], null);
        }

        void PlaceOneItem(in PlaceItem item, PlaceSink sink)
        {
            LetteraLine line = m_Lines[item.Line];
            PlaceLine(line, item.Line, m_Paragraphs[line.Paragraph], item.X, item.Baseline, item.Stretch, sink);
        }

        void PlaceStretch(int index)
        {
            PlaceSink sink = m_Aside.PlaceSinks[index];
            PlaceItem[] list = m_Aside.PlaceItems;
            for (int i = sink.FirstItem; i < sink.EndItem && sink.Count <= sink.End; i++) PlaceOneItem(list[i], sink);
        }
    }
}

using System;
using System.Collections.Generic;
using FirstSymbol.Lettera.Unicode;
using UnityEngine;

namespace FirstSymbol.Lettera
{
    // A long text shapes and breaks, when it is set, only its first paragraphs and those near what shows. Each other paragraph stands as one line of an
    // estimated height, and is finished later, a slice a frame, in reading order. What needs an exact answer, such as Lines, a caret or a measure,
    // finishes all of them first. Runs, glyphs and lines stay in reading order throughout, so a finished text is the same as one built whole.
    internal sealed partial class TextEngine
    {
        bool m_Deferring;
        int m_DeferBaseLevel;
        int m_PendingCount;
        int m_FirstPending;             // No paragraph before this one waits.

        // What estimates read, learned from the paragraphs done so far.
        float m_EstAdvanceEm, m_EstLineEm, m_EstFill;
        double m_SeenAdvanceEm, m_SeenChars, m_SeenLineEm, m_SeenLines, m_SeenUsed, m_SeenRoom;

        float m_LaidSize, m_LaidWidth;
        bool m_LaidWrap;
        bool m_PlacedOnEstimates;       // A paragraph above the placed lines still waits, so they stand where estimates put them.
        int m_EstimateVersion;

        readonly List<LetteraLine> m_SpliceLines = new List<LetteraLine>();
        readonly List<Run> m_RunScratch = new List<Run>();
        ShapedGlyph[] m_GlyphScratch = Array.Empty<ShapedGlyph>();

        // The first paragraphs are shaped at once whatever shows: they teach the estimates, and a text read from its top needs them anyway.
        const int SampleCodepoints = 1024;

        // What one step of finishing takes before it looks at the clock.
        const int StepCodepoints = 2048;

        /// <summary>How many paragraphs wait to be shaped.</summary>
        internal int PendingParagraphs => m_PendingCount;

        /// <summary>The last shaping started from the first paragraph, not from an edit.</summary>
        internal bool ShapedAfresh { get; private set; }

        /// <summary>How many glyphs are shaped so far. Tests read this.</summary>
        internal int ShapedGlyphCount => m_ShapedCount;

        /// <summary>Changes whenever the text's height may have changed by finishing.</summary>
        internal int EstimateVersion => m_EstimateVersion;

        bool DeferApplies(int length, LetteraTextData data) =>
            LetteraLongText.FinishLater && LetteraLongText.VisibleLinesOnly && m_HasVisible && length >= LetteraLongText.FromGlyphs && !IsWindowed && !ClipsViewport &&
            MaskGlyph == 0 && RevealSource < 0 && m_StageHooks.Count == 0 && m_InlineItems.Count == 0 && !m_Attributes.HasLayout && !m_Attributes.HasCurve &&
            m_Attributes.Elides.Count == 0 && m_Attributes.Marquee == null && m_Attributes.RevealRequests.Count == 0 && m_Attributes.LineHeights.Count == 0 &&
            m_Attributes.LineGaps.Count == 0 && m_Attributes.ParagraphSpacings.Count == 0 && m_Attributes.TrimTop == LetteraTrimTop.None &&
            m_Attributes.TrimBottom == LetteraTrimBottom.None && !data.AutoSize && data.VerticalAlignment == LetteraVerticalAlignment.Top &&
            data.Overflow == LetteraOverflow.Visible;

        void ForgetEstimates()
        {
            m_SeenAdvanceEm = m_SeenChars = m_SeenLineEm = m_SeenLines = m_SeenUsed = m_SeenRoom = 0;
            m_EstAdvanceEm = 0.5f;
            m_EstLineEm = Mathf.Max(EstimatedLineHeight / Mathf.Max(m_Host.Data.FontSize, 0.01f), 0.5f);
            m_EstFill = 0.9f;
            m_FirstPending = 0;
        }

        void CountPending()
        {
            int count = 0;
            for (int p = 0; p < m_Paragraphs.Count; p++)
                if (m_Paragraphs[p].Pending)
                    count++;
            m_PendingCount = count;
            m_FirstPending = 0;
            m_EstimateVersion++;
        }

        bool PendingBefore(int paragraph)
        {
            for (int p = m_FirstPending; p < paragraph && p < m_Paragraphs.Count; p++)
                if (m_Paragraphs[p].Pending)
                    return true;
            return false;
        }

        // The first codepoint from here that may separate paragraphs, found with no call per codepoint. The caller checks it.
        unsafe int NextSeparator(int from, int length)
        {
            fixed (int* text = m_Text)
            {
                int i = from;
                while (i < length)
                {
                    int c = text[i];
                    if (c <= 0x2029 && (c == '\n' || c == '\r' || c == 0x85 || c == 0x2029)) return i;
                    i++;
                }
                return length;
            }
        }

        void ShapeSample()
        {
            int end = 0, chars = 0;
            while (end < m_Paragraphs.Count && chars < SampleCodepoints)
            {
                chars += m_Paragraphs[end].End - m_Paragraphs[end].Start;
                end++;
            }
            ShapeAllPending(0, end);
        }

        // ------------------------------------------------------------- estimates

        void AddEstimatedLine(List<LetteraLine> lines, ref LineStats stats, in Paragraph paragraph, int index, float baseSize, float maxWidth, bool wrap)
        {
            float lineHeight = m_EstLineEm * baseSize;
            int count = 1;
            int chars = paragraph.ContentEnd - paragraph.Start;
            if (wrap && chars > 0 && !float.IsInfinity(maxWidth) && maxWidth > 0)
                count = Mathf.Max(1, Mathf.CeilToInt(chars * m_EstAdvanceEm * baseSize / (maxWidth * m_EstFill)));
            float height = count * lineHeight;
            lines.Add(new LetteraLine
            {
                Start = paragraph.Start, End = paragraph.End, VisibleEnd = paragraph.Start, Paragraph = index, Height = height, Ascent = 0, Descent = height,
                EndedByMandatoryBreak = true,
            });
            stats.Height += height;
        }

        void LearnLines(List<LetteraLine> lines, int first, float baseSize, float maxWidth, bool wrap)
        {
            int count = lines.Count - first;
            if (count <= 0 || baseSize <= 0) return;
            for (int l = first; l < lines.Count; l++) m_SeenLineEm += lines[l].Height / baseSize;
            m_SeenLines += count;
            m_EstLineEm = (float)(m_SeenLineEm / m_SeenLines);
            if (!wrap || count < 2 || float.IsInfinity(maxWidth) || maxWidth <= 0) return;
            for (int l = first; l < lines.Count - 1; l++) m_SeenUsed += lines[l].Width;
            m_SeenRoom += (count - 1) * (double)maxWidth;
            m_EstFill = Mathf.Clamp((float)(m_SeenUsed / m_SeenRoom), 0.5f, 1f);
        }

        // ------------------------------------------------------------- shaping what waits

        // What shaping writes per codepoint, up to a codepoint. A long text left for later grows these as it is shaped, not all at once.
        // They grow a quarter past what is asked: shaping near what shows asks a little more each round, and doubling seven arrays of a long text
        // for that is megabytes. A text left for later never takes much more than its length.
        void EnsureShapeBuffers(int size)
        {
            if (m_Levels.Length >= size && m_Boundaries.Length >= size + 1 && m_Breaks.Length >= size && m_Scripts.Length >= size && m_CpRun.Length >= size &&
                m_CpAdvanceEm.Length >= size && m_ShapeText.Length >= size) return;
            int room = Math.Max(Math.Max(size + size / 4, m_Levels.Length * 2), SmallestBuffer);
            if (m_Deferring) room = Math.Min(room, Math.Max(size, m_TextLength + 1 + (m_TextLength >> 3)));
            Grow(ref m_Levels, room);
            Grow(ref m_Boundaries, room + 1);
            Grow(ref m_Breaks, room);
            Grow(ref m_Scripts, room);
            Grow(ref m_CpRun, room);
            Grow(ref m_CpAdvanceEm, room);
            Grow(ref m_ShapeText, room);
        }

        // Finishing writes to the end of the text, so its first step takes that room at once: growing by halves through every size on the way
        // leaves about as much garbage again as the text needs, and that is collected in some later frame. Past a few megabytes, taking it all
        // in one frame would cost that frame more than its slice, and the buffers grow as before.
        const int ReserveAtOnceCodepoints = 1 << 17;

        void ReserveForFinishing()
        {
            if (m_TextLength > ReserveAtOnceCodepoints) return;
            EnsureShapeBuffers(m_TextLength + 1);
            int glyphs = m_TextLength + (m_TextLength >> 3);
            if (m_Shaped.Length < glyphs) Array.Resize(ref m_Shaped, glyphs);
        }

        static void Grow<T>(ref T[] array, int size)
        {
            if (array.Length < size) Array.Resize(ref array, size);
        }

        // The first run that starts at or after a codepoint, among the first runs.
        int RunAtOrAfter(int cp, int limit)
        {
            int low = 0, high = limit;
            while (low < high)
            {
                int middle = (low + high) >> 1;
                if (m_Runs[middle].Start < cp) low = middle + 1;
                else high = middle;
            }
            return low;
        }

        void ShapeAllPending(int first, int end)
        {
            int p = Math.Max(first, 0);
            while (p < end)
            {
                while (p < end && !m_Paragraphs[p].Pending) p++;
                int q = p;
                while (q < end && m_Paragraphs[q].Pending) q++;
                if (p < q) ShapePending(p, q);
                p = q;
            }
        }

        // Shapes paragraphs that all wait, side by side, at the end of the runs and glyphs, then moves those into reading order.
        // Many codepoints at once go across the workers, as a whole text does; the result is the same either way.
        void ShapePending(int first, int end)
        {
            EnsureShapeBuffers(m_Paragraphs[end - 1].End + 1);
            int runs = m_Runs.Count, glyphs = m_ShapedCount;
            int insertRun = RunAtOrAfter(m_Paragraphs[first].Start, runs);
            LetteraAttributes attributes = m_Attributes;
            int baseLevel = m_DeferBaseLevel;
            for (int p = first; p < end; p++)
            {
                Paragraph paragraph = m_Paragraphs[p];
                for (int i = paragraph.Start; i < paragraph.End; i++)
                {
                    int cp = m_Text[i];
                    byte map = attributes.At(i).CaseMap;
                    m_ShapeText[i] = map == 0 ? cp : map == 1 ? LetteraUnicode.ToUpper(cp) : LetteraUnicode.ToLower(cp);
                }
                Array.Clear(m_CpAdvanceEm, paragraph.Start, paragraph.End - paragraph.Start);
            }
            int chars = m_Paragraphs[end - 1].End - m_Paragraphs[first].Start;
            if (end - first > 1 && ShapesInParallel(chars)) ShapePendingInParallel(first, end, runs, glyphs);
            else
            {
                for (int p = first; p < end; p++)
                {
                    Paragraph paragraph = m_Paragraphs[p];
                    paragraph.Pending = false;
                    paragraph.Level = baseLevel == 1 ? 1 : 0;
                    ShapeParagraph(ref paragraph, baseLevel);
                    if (paragraph.ContentEnd == paragraph.Start && baseLevel < 0 && p > 0) paragraph.Level = m_Paragraphs[p - 1].Level;
                    m_Paragraphs[p] = paragraph;
                }
            }
            for (int p = first; p < end; p++)
            {
                Paragraph paragraph = m_Paragraphs[p];
                m_PendingCount--;
                double advance = 0;
                for (int i = paragraph.Start; i < paragraph.ContentEnd; i++) advance += m_CpAdvanceEm[i];
                m_SeenAdvanceEm += advance;
                m_SeenChars += paragraph.ContentEnd - paragraph.Start;
            }
            // An empty paragraph after these took its direction from one that waited.
            for (int p = end; p < m_Paragraphs.Count && baseLevel < 0; p++)
            {
                Paragraph next = m_Paragraphs[p];
                if (next.Pending || next.ContentEnd != next.Start) break;
                next.Level = m_Paragraphs[p - 1].Level;
                m_Paragraphs[p] = next;
            }
            if (m_SeenChars > 0) m_EstAdvanceEm = (float)(m_SeenAdvanceEm / m_SeenChars);
            if (insertRun < runs) MoveIntoPlace(insertRun, runs, glyphs);
        }

        // The runs and glyphs past the old ends go before the run at insertRun, and every index that pointed past it moves on.
        void MoveIntoPlace(int insertRun, int runs, int glyphs)
        {
            int newRuns = m_Runs.Count - runs, newGlyphs = m_ShapedCount - glyphs;
            int insertGlyph = m_Runs[insertRun].GlyphStart;
            if (newGlyphs > 0)
            {
                EnsureSize(ref m_GlyphScratch, newGlyphs);
                Array.Copy(m_Shaped, glyphs, m_GlyphScratch, 0, newGlyphs);
                Array.Copy(m_Shaped, insertGlyph, m_Shaped, insertGlyph + newGlyphs, glyphs - insertGlyph);
                Array.Copy(m_GlyphScratch, 0, m_Shaped, insertGlyph, newGlyphs);
            }
            m_RunScratch.Clear();
            for (int r = runs; r < m_Runs.Count; r++)
            {
                Run run = m_Runs[r];
                run.GlyphStart += insertGlyph - glyphs;
                int index = insertRun + r - runs;
                for (int i = run.Start; i < run.End; i++) m_CpRun[i] = index;
                m_RunScratch.Add(run);
            }
            m_Runs.RemoveRange(runs, newRuns);
            for (int r = insertRun; r < runs; r++)
            {
                Run run = m_Runs[r];
                run.GlyphStart += newGlyphs;
                m_Runs[r] = run;
                for (int i = run.Start; i < run.End; i++) m_CpRun[i] += newRuns;
            }
            m_Runs.InsertRange(insertRun, m_RunScratch);
            m_RunScratch.Clear();
        }

        // ------------------------------------------------------------- lines of what was shaped

        int FirstLineOf(int paragraph)
        {
            int low = 0, high = m_Lines.Count;
            while (low < high)
            {
                int middle = (low + high) >> 1;
                if (m_Lines[middle].Paragraph < paragraph) low = middle + 1;
                else high = middle;
            }
            return low;
        }

        // Shapes what waits in a stretch of paragraphs and puts their lines where their estimates stood. Returns the first line that moved, or -1.
        int ProcessParagraphs(int first, int end)
        {
            int moved = -1;
            int p = Math.Max(first, 0);
            while (p < end)
            {
                while (p < end && !m_Paragraphs[p].Pending) p++;
                int q = p;
                while (q < end && m_Paragraphs[q].Pending) q++;
                if (p < q)
                {
                    ShapePending(p, q);
                    int at = SpliceLines(p, q);
                    if (moved < 0) moved = at;
                }
                p = q;
            }
            return moved;
        }

        int SpliceLines(int first, int end)
        {
            int at = FirstLineOf(first);
            int count = end - first;
            float old = 0;
            for (int l = at; l < at + count; l++) old += m_Lines[l].Height + m_Lines[l].GapBefore;
            m_SpliceLines.Clear();
            var stats = new LineStats();
            BreakParagraphs(m_SpliceLines, ref stats, m_LaidSize, m_LaidWidth, m_LaidWrap, true, first, end);
            m_Lines.RemoveRange(at, count);
            m_Lines.InsertRange(at, m_SpliceLines);
            m_ContentHeight += stats.Height - old;
            if (stats.MaxWidth > m_ContentWidth) m_ContentWidth = stats.MaxWidth;
            if (m_PendingCount == 0)
            {
                // Added up in reading order, as breaking all of it at once does, so the last bit is the same too.
                float height = 0;
                for (int l = 0; l < m_Lines.Count; l++) height += m_Lines[l].Height + m_Lines[l].GapBefore;
                m_ContentHeight = height;
            }
            int shift = m_SpliceLines.Count - count;
            m_SpliceLines.Clear();
            m_EstimateVersion++;
            ShiftPlacedLines(at + count, shift);
            return at;
        }

        // Lines came in above placed glyphs: their line numbers move, and nothing else about them does.
        void ShiftPlacedLines(int from, int shift)
        {
            if (shift == 0) return;
            for (int i = 0; i < m_PlacedCount; i++)
                if (m_Placed[i].Line >= from)
                    m_Placed[i].Line += shift;
            if (m_BandFirst < m_BandEnd)
            {
                if (m_BandFirst >= from) m_BandFirst += shift;
                if (m_BandEnd >= from) m_BandEnd += shift;
            }
            m_ExtraLines.Clear();
            m_ExtraEnd = m_PlacedCount;
            m_SegmentsValid = false;
            m_ActiveRectsValid = false;
        }

        // The paragraphs whose estimates reach into the band are shaped, again until none does: each that is done moves those below it.
        void ShapeBand(float top)
        {
            for (int round = 0; round < 16 && m_PendingCount > 0; round++)
            {
                float y = top;
                int first = -1, end = -1;
                for (int l = 0; l < m_Lines.Count; l++)
                {
                    LetteraLine line = m_Lines[l];
                    y -= line.GapBefore;
                    float bottom = y - line.Height;
                    if (bottom > m_BandHigh)
                    {
                        y = bottom;
                        continue;
                    }
                    if (y < m_BandLow) break;
                    if (m_Paragraphs[line.Paragraph].Pending)
                    {
                        if (first < 0) first = line.Paragraph;
                        end = line.Paragraph + 1;
                    }
                    y = bottom;
                }
                if (first < 0) return;
                ProcessParagraphs(first, end);
            }
        }

        // ------------------------------------------------------------- finishing

        // A query wants everything exact: whatever waits is shaped now, and placed again at the next rebuild.
        void FinishForQuery()
        {
            if (m_PendingCount == 0) return;
            if (m_Dirty < DirtyLevel.Layout)
            {
                ProcessParagraphs(m_FirstPending, m_Paragraphs.Count);
                if (m_Dirty < DirtyLevel.Positions) m_Dirty = DirtyLevel.Positions;
            }
            else
            {
                ShapeAllPending(m_FirstPending, m_Paragraphs.Count);
            }
            m_FirstPending = m_Paragraphs.Count;
            m_PlacedOnEstimates = false;
            m_EstimateVersion++;
        }

        // A query that reads lines or places finishes everything first. Inside a rebuild it reads what is there.
        void Exact()
        {
            if (m_PendingCount > 0 && !m_InPasses) EnsurePositioned();
        }

        /// <summary>
        /// Finishes waiting paragraphs in reading order until the clock passes a deadline, a step at least. True when the placed lines must be placed again:
        /// everything above them is exact now, and they stood on estimates.
        /// </summary>
        internal bool FinishStep(long deadline)
        {
            if (m_PendingCount == 0 || m_Dirty >= DirtyLevel.Layout || m_InPasses) return false;
            ReserveForFinishing();
            // With workers, a step is one slice for each of them and the main thread.
            int step = LetteraWorkers.ShouldRun(LetteraWorkers.ParallelFromCodepoints) ? Math.Max(StepCodepoints * (LetteraWorkers.WorkerCount + 1), LetteraWorkers.ParallelFromCodepoints) : StepCodepoints;
            do
            {
                int first = m_FirstPending;
                while (first < m_Paragraphs.Count && !m_Paragraphs[first].Pending) first++;
                m_FirstPending = first;
                if (first >= m_Paragraphs.Count) break;
                int end = first, chars = 0;
                while (end < m_Paragraphs.Count && m_Paragraphs[end].Pending && chars < step)
                {
                    chars += m_Paragraphs[end].End - m_Paragraphs[end].Start;
                    end++;
                }
                ProcessParagraphs(first, end);
                m_FirstPending = end;
            } while (m_PendingCount > 0 && System.Diagnostics.Stopwatch.GetTimestamp() < deadline);
            LayoutBounds = new Rect(LayoutBounds.x, m_Box.yMax - m_ContentHeight, LayoutBounds.width, m_ContentHeight);
            // Done, every line gets its place, as a whole build gives it.
            if (m_PendingCount == 0) return true;
            if (!m_PlacedOnEstimates) return false;
            if (m_BandFirst < m_BandEnd && m_BandFirst < m_Lines.Count && PendingBefore(m_Lines[m_BandFirst].Paragraph)) return false;
            m_PlacedOnEstimates = false;
            return true;
        }
    }
}

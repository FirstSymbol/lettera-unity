using System;
using System.Buffers;
using System.Collections.Generic;
using FirstSymbol.Lettera.Unicode;

namespace FirstSymbol.Lettera
{
    // A big text's shaping, split by what may run together: analysis of each paragraph writes only that paragraph's cells, and each run's shaping only its own.
    // Finding faces and the caches stay on the main thread. The result is the same, byte for byte, as shaping in order.
    internal sealed unsafe partial class TextEngine
    {
        struct ParagraphSlot
        {
            public int Start, ContentEnd, End, Level, FirstRun, RunCount;
            public long Hash;
            public bool Cacheable, Hit;
            // The entry a hit found: nothing changes the cache between finding it and restoring it.
            public ParagraphEntry* Entry;
            public RestoreCopy Copy;
        }

        struct DeferredRun
        {
            public int RunIndex, ParagraphStart, ParagraphLength;
            public ShapedGlyph[] Glyphs;
            public int Count;
        }

        [ThreadStatic] static NativeGlyph[] s_WorkerGlyphs;
        [ThreadStatic] static LetteraSegmenterScratch s_WorkerScratch;

        bool ShapesInParallel(int length) => length >= LetteraWorkers.ParallelFromCodepoints && LetteraWorkers.ShouldRun(length) && MaskGlyph == 0 && m_Aside.StageHooks.Count == 0;

        void ShapeParagraphsInParallel(int length, int baseLevel)
        {
            // Where each paragraph is, and which the caches already hold.
            m_Aside.Slots.Clear();
            int start = 0;
            while (start <= length)
            {
                int i = start;
                int separator = 0;
                while (i < length && !IsParagraphSeparator(i, out separator)) i++;
                if (i >= length) separator = 0;
                AddSlot(start, i, i + separator, baseLevel);
                if (i >= length) break;
                start = i + separator;
                if (start == length)
                {
                    AddSlot(length, length, length, baseLevel);
                    break;
                }
            }

            // Every paragraph no cache had is analyzed, side by side. When the caches had them all, no thread is woken for nothing.
            if (AnySlotToAnalyze()) LetteraWorkers.Run(m_Aside.Slots.Count, m_Aside.AnalyzeOne ?? (m_Aside.AnalyzeOne = AnalyzeSlot));

            // Faces are found in order, and hits go in where they belong.
            m_Aside.DeferShaping = true;
            m_Aside.Deferred.Clear();
            string language = LanguageOf(-1);
            for (int s = 0; s < m_Aside.Slots.Count; s++)
            {
                ParagraphSlot slot = m_Aside.Slots[s];
                var paragraph = new Paragraph { Start = slot.Start, ContentEnd = slot.ContentEnd, End = slot.End, Level = slot.Level };
                slot.FirstRun = m_Runs.Count;
                if (slot.Hit)
                {
                    slot.Copy = RestoreHead(slot.Entry, ref paragraph, slot.Start, slot.End - slot.Start);
                }
                else if (slot.End > slot.Start)
                {
                    m_Aside.DeferParagraphStart = slot.Start;
                    m_Aside.DeferParagraphLength = slot.End - slot.Start;
                    BuildRuns(paragraph);
                }
                slot.RunCount = m_Runs.Count - slot.FirstRun;
                m_Aside.Slots[s] = slot;
                if (slot.ContentEnd == slot.Start && baseLevel < 0 && m_Paragraphs.Count > 0) paragraph.Level = m_Paragraphs[m_Paragraphs.Count - 1].Level;
                m_Paragraphs.Add(paragraph);
            }
            m_Aside.DeferShaping = false;

            // What hits copy into the text's cells, side by side: each writes only its own paragraph's cells and glyphs.
            LetteraWorkers.Run(m_Aside.Slots.Count, m_Aside.RestoreOne ?? (m_Aside.RestoreOne = RestoreSlot));

            // Runs shape side by side, each into its own buffer.
            LetteraWorkers.Run(m_Aside.Deferred.Count, m_Aside.ShapeOne ?? (m_Aside.ShapeOne = ShapeDeferred));

            // Then all of it comes together in reading order, as shaping one by one would have left it.
            MergeShaped();
            StoreMissedParagraphs(baseLevel, language);
        }

        void RestoreSlot(int index)
        {
            ParagraphSlot slot = m_Aside.Slots[index];
            if (slot.Hit) RestoreCells(slot.Copy);
        }

        bool AnySlotToAnalyze()
        {
            for (int s = 0; s < m_Aside.Slots.Count; s++)
            {
                ParagraphSlot slot = m_Aside.Slots[s];
                if (!slot.Hit && slot.End > slot.Start) return true;
            }
            return false;
        }

        void AddSlot(int start, int contentEnd, int end, int baseLevel)
        {
            int count = end - start;
            var slot = new ParagraphSlot { Start = start, ContentEnd = contentEnd, End = end, Level = baseLevel == 1 ? 1 : 0 };
            if (count > 0 && ParagraphCacheApplies(count))
            {
                slot.Cacheable = true;
                slot.Hash = HashParagraph(start, count, baseLevel, LanguageOf(-1));
                slot.Entry = FindParagraph(slot.Hash, start, count, baseLevel, LanguageOf(-1));
                slot.Hit = slot.Entry != null;
                if (!slot.Hit) LetteraCaches.s_Stats.Misses++;
            }
            m_Aside.Slots.Add(slot);
        }

        void AnalyzeSlot(int index)
        {
            ParagraphSlot slot = m_Aside.Slots[index];
            int count = slot.End - slot.Start;
            if (count <= 0 || slot.Hit) return;
            int start = slot.Start;
            ReadOnlySpan<int> text = m_Text.Slice(start, count);
            GraphemeBreaker.FindBoundaries(text, m_Boundaries.Slice(start, count + 1));
            slot.Level = Bidi.ResolveParagraph(text, slot.Level == 1 && BaseLevelForSlots == 1 ? 1 : BaseLevelForSlots, m_Levels.Slice(start, count));
            LineBreaker.FindBreaks(text, m_Breaks.Slice(start, count));
            if (LetteraWordSegmenters.Count != 0)
                LetteraWordSegmenters.ApplyToBreaks(text, m_Boundaries.Slice(start, count + 1), m_Breaks.Slice(start, count), s_WorkerScratch ?? (s_WorkerScratch = new LetteraSegmenterScratch()));
            ScriptItemizer.Resolve(text, m_Scripts.Slice(start, count));
            lock (m_Aside.Slots) m_Aside.Slots[index] = slot;
        }

        int BaseLevelForSlots;

        void ShapeDeferred(int index)
        {
            DeferredRun deferred = m_Aside.Deferred[index];
            Run run = m_Runs[deferred.RunIndex];
            string language = LanguageOf(run.Language);
            byte[] languageBytes = null;
            if (language != null) lock (s_LanguageBytes) languageBytes = s_LanguageBytes[language];

            int direction = (run.Level & 1) != 0 ? 5 : 4;
            uint script = LetteraUnicode.ScriptToOpenTypeTag(run.Script);
            FeatureSet set = run.Features >= 0 ? m_Attributes.FeatureSets[run.Features] : null;
            int featureCount = (set?.Tags.Length ?? 0) + (run.FaceFeature != 0 ? 1 : 0);
            NativeFeature* features = stackalloc NativeFeature[Math.Min(featureCount, 64) + 1];
            int used = 0;
            if (set != null)
                for (int f = 0; f < set.Tags.Length && used < 63; f++)
                    features[used++] = new NativeFeature { Tag = set.Tags[f], Value = set.Values[f], Start = 0, End = uint.MaxValue };
            if (run.FaceFeature != 0)
                features[used++] = new NativeFeature { Tag = run.FaceFeature == 1 ? s_Smcp : run.FaceFeature == 2 ? s_Sups : s_Subs, Value = 1, Start = 0, End = uint.MaxValue };

            NativeGlyph[] glyphs = s_WorkerGlyphs ?? (s_WorkerGlyphs = new NativeGlyph[1024]);
            int count;
            fixed (int* text = &m_ShapeText[deferred.ParagraphStart])
            fixed (byte* lang = languageBytes)
            {
                while (true)
                {
                    fixed (NativeGlyph* output = glyphs)
                        count = LetteraNative.lt_shape(run.Instance.Font, text, deferred.ParagraphLength, run.Start - deferred.ParagraphStart, run.End - run.Start, direction, script, lang, used > 0 ? features : null, used, output, glyphs.Length);
                    if (count >= 0) break;
                    glyphs = s_WorkerGlyphs = new NativeGlyph[-count * 2];
                }
            }

            ShapedGlyph[] shaped = ArrayPool<ShapedGlyph>.Shared.Rent(Math.Max(count, 1));
            for (int i = 0; i < count; i++)
            {
                ref NativeGlyph g = ref glyphs[i];
                int cluster = (int)g.Cluster + deferred.ParagraphStart;
                shaped[i] = new ShapedGlyph { Glyph = g.Glyph, Cluster = cluster, XAdvance = g.XAdvance, XOffset = g.XOffset, YOffset = g.YOffset };
                m_CpAdvanceEm[cluster] += g.XAdvance * run.UnitEm;
            }
            deferred.Glyphs = shaped;
            deferred.Count = count;
            lock (m_Aside.Deferred) m_Aside.Deferred[index] = deferred;
        }

        // Hits were copied in as they came, so runs that waited are put between them here.
        void MergeShaped()
        {
            int total = m_ShapedCount;
            foreach (DeferredRun deferred in m_Aside.Deferred) total += deferred.Count;
            EnsureSize(ref m_Aside.ShapedMerged, total);
            int at = 0, next = 0;
            for (int r = 0; r < m_Runs.Count; r++)
            {
                Run run = m_Runs[r];
                int glyphs;
                if (next < m_Aside.Deferred.Count && m_Aside.Deferred[next].RunIndex == r)
                {
                    DeferredRun deferred = m_Aside.Deferred[next++];
                    Array.Copy(deferred.Glyphs, 0, m_Aside.ShapedMerged, at, deferred.Count);
                    glyphs = deferred.Count;
                    ArrayPool<ShapedGlyph>.Shared.Return(deferred.Glyphs);
                }
                else
                {
                    Array.Copy(m_Shaped, run.GlyphStart, m_Aside.ShapedMerged, at, run.GlyphCount);
                    glyphs = run.GlyphCount;
                }
                run.GlyphStart = at;
                run.GlyphCount = glyphs;
                m_Runs[r] = run;
                at += glyphs;
            }
            ShapedGlyph[] swap = m_Shaped;
            m_Shaped = m_Aside.ShapedMerged;
            m_Aside.ShapedMerged = swap;
            m_ShapedCount = at;
        }

        void StoreMissedParagraphs(int baseLevel, string language)
        {
            for (int s = 0; s < m_Aside.Slots.Count; s++)
            {
                ParagraphSlot slot = m_Aside.Slots[s];
                if (!slot.Cacheable || slot.Hit || slot.RunCount == 0) continue;
                int firstGlyph = m_Runs[slot.FirstRun].GlyphStart;
                StoreParagraph(slot.Hash, new Paragraph { Start = slot.Start, ContentEnd = slot.ContentEnd, End = slot.End, Level = m_Paragraphs[s].Level }, slot.Start, slot.End - slot.Start, baseLevel, language, slot.FirstRun, firstGlyph, slot.FirstRun + slot.RunCount);
            }
        }

        // Paragraphs a long text left for later, shaped side by side as a whole text is, at the end of the runs and glyphs. The caller moves them into place.
        void ShapePendingInParallel(int first, int end, int runs, int glyphs)
        {
            int baseLevel = m_Aside.DeferBaseLevel;
            m_Aside.Slots.Clear();
            for (int p = first; p < end; p++)
            {
                Paragraph paragraph = m_Paragraphs[p];
                AddSlot(paragraph.Start, paragraph.ContentEnd, paragraph.End, baseLevel);
            }
            BaseLevelForSlots = baseLevel;
            LetteraWorkers.Run(m_Aside.Slots.Count, m_Aside.AnalyzeOne ?? (m_Aside.AnalyzeOne = AnalyzeSlot));

            m_Aside.DeferShaping = true;
            m_Aside.Deferred.Clear();
            string language = LanguageOf(-1);
            for (int s = 0; s < m_Aside.Slots.Count; s++)
            {
                ParagraphSlot slot = m_Aside.Slots[s];
                var paragraph = new Paragraph { Start = slot.Start, ContentEnd = slot.ContentEnd, End = slot.End, Level = slot.Level };
                slot.FirstRun = m_Runs.Count;
                if (slot.Hit)
                {
                    RestoreCells(RestoreHead(slot.Entry, ref paragraph, slot.Start, slot.End - slot.Start));
                }
                else if (slot.End > slot.Start)
                {
                    m_Aside.DeferParagraphStart = slot.Start;
                    m_Aside.DeferParagraphLength = slot.End - slot.Start;
                    BuildRuns(paragraph);
                }
                slot.RunCount = m_Runs.Count - slot.FirstRun;
                m_Aside.Slots[s] = slot;
                int p = first + s;
                if (slot.ContentEnd == slot.Start && baseLevel < 0 && p > 0) paragraph.Level = m_Paragraphs[p - 1].Level;
                m_Paragraphs[p] = paragraph;
            }
            m_Aside.DeferShaping = false;
            LetteraWorkers.Run(m_Aside.Deferred.Count, m_Aside.ShapeOne ?? (m_Aside.ShapeOne = ShapeDeferred));

            // Restored runs went in as they came, and shaped ones wait in their own buffers: all of them in reading order after the glyphs before.
            int total = m_ShapedCount - glyphs;
            foreach (DeferredRun deferred in m_Aside.Deferred) total += deferred.Count;
            EnsureSize(ref m_Aside.ShapedMerged, total);
            int at = 0, next = 0;
            for (int r = runs; r < m_Runs.Count; r++)
            {
                Run run = m_Runs[r];
                int count;
                if (next < m_Aside.Deferred.Count && m_Aside.Deferred[next].RunIndex == r)
                {
                    DeferredRun deferred = m_Aside.Deferred[next++];
                    Array.Copy(deferred.Glyphs, 0, m_Aside.ShapedMerged, at, deferred.Count);
                    count = deferred.Count;
                    ArrayPool<ShapedGlyph>.Shared.Return(deferred.Glyphs);
                }
                else
                {
                    Array.Copy(m_Shaped, run.GlyphStart, m_Aside.ShapedMerged, at, run.GlyphCount);
                    count = run.GlyphCount;
                }
                run.GlyphStart = glyphs + at;
                run.GlyphCount = count;
                m_Runs[r] = run;
                at += count;
            }
            m_Aside.Deferred.Clear();
            EnsureSize(ref m_Shaped, glyphs + at);
            Array.Copy(m_Aside.ShapedMerged, 0, m_Shaped, glyphs, at);
            m_ShapedCount = glyphs + at;

            for (int s = 0; s < m_Aside.Slots.Count; s++)
            {
                ParagraphSlot slot = m_Aside.Slots[s];
                if (!slot.Cacheable || slot.Hit || slot.RunCount == 0) continue;
                StoreParagraph(slot.Hash, m_Paragraphs[first + s], slot.Start, slot.End - slot.Start, baseLevel, language, slot.FirstRun, m_Runs[slot.FirstRun].GlyphStart,
                    slot.FirstRun + slot.RunCount);
            }
        }

        // Where a run would shape now, with a paragraph-relative start: it waits, and what workers need from the main thread is ready for it.
        void DeferShaping(ref Run run)
        {
            string language = LanguageOf(run.Language);
            if (language != null && !s_LanguageBytes.ContainsKey(language)) s_LanguageBytes[language] = FontFace.NullTerminated(language);
            run.GlyphStart = 0;
            run.GlyphCount = 0;
            m_Aside.Deferred.Add(new DeferredRun { RunIndex = m_Runs.Count, ParagraphStart = m_Aside.DeferParagraphStart, ParagraphLength = m_Aside.DeferParagraphLength });
        }
    }
}

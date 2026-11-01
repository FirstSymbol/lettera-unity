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
        }

        struct DeferredRun
        {
            public int RunIndex, ParagraphStart, ParagraphLength;
            public ShapedGlyph[] Glyphs;
            public int Count;
        }

        readonly List<ParagraphSlot> m_Slots = new List<ParagraphSlot>();
        readonly List<DeferredRun> m_Deferred = new List<DeferredRun>();
        bool m_DeferShaping;
        int m_DeferParagraphStart, m_DeferParagraphLength;
        ShapedGlyph[] m_ShapedMerged = Array.Empty<ShapedGlyph>();
        [ThreadStatic] static NativeGlyph[] s_WorkerGlyphs;
        [ThreadStatic] static LetteraSegmenterScratch s_WorkerScratch;
        Action<int> m_AnalyzeOne, m_ShapeOne;

        bool ShapesInParallel(int length) => length >= LetteraWorkers.ParallelFromCodepoints && LetteraWorkers.ShouldRun(length) && MaskGlyph == 0 && m_StageHooks.Count == 0;

        void ShapeParagraphsInParallel(int length, int baseLevel)
        {
            // Where each paragraph is, and which the caches already hold.
            m_Slots.Clear();
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

            // Every paragraph no cache had is analyzed, side by side.
            LetteraWorkers.Run(m_Slots.Count, m_AnalyzeOne ?? (m_AnalyzeOne = AnalyzeSlot));

            // Faces are found in order, and hits go in where they belong.
            m_DeferShaping = true;
            m_Deferred.Clear();
            string language = LanguageOf(-1);
            for (int s = 0; s < m_Slots.Count; s++)
            {
                ParagraphSlot slot = m_Slots[s];
                var paragraph = new Paragraph { Start = slot.Start, ContentEnd = slot.ContentEnd, End = slot.End, Level = slot.Level };
                slot.FirstRun = m_Runs.Count;
                if (slot.Hit)
                {
                    TryRestoreParagraph(slot.Hash, ref paragraph, slot.Start, slot.End - slot.Start, baseLevel, language);
                }
                else if (slot.End > slot.Start)
                {
                    m_DeferParagraphStart = slot.Start;
                    m_DeferParagraphLength = slot.End - slot.Start;
                    BuildRuns(paragraph);
                }
                slot.RunCount = m_Runs.Count - slot.FirstRun;
                m_Slots[s] = slot;
                if (slot.ContentEnd == slot.Start && baseLevel < 0 && m_Paragraphs.Count > 0) paragraph.Level = m_Paragraphs[m_Paragraphs.Count - 1].Level;
                m_Paragraphs.Add(paragraph);
            }
            m_DeferShaping = false;

            // Runs shape side by side, each into its own buffer.
            LetteraWorkers.Run(m_Deferred.Count, m_ShapeOne ?? (m_ShapeOne = ShapeDeferred));

            // Then all of it comes together in reading order, as shaping one by one would have left it.
            MergeShaped();
            StoreMissedParagraphs(baseLevel, language);
        }

        void AddSlot(int start, int contentEnd, int end, int baseLevel)
        {
            int count = end - start;
            var slot = new ParagraphSlot { Start = start, ContentEnd = contentEnd, End = end, Level = baseLevel == 1 ? 1 : 0 };
            if (count > 0 && ParagraphCacheApplies(count))
            {
                slot.Cacheable = true;
                slot.Hash = HashParagraph(start, count, baseLevel, LanguageOf(-1));
                slot.Hit = ParagraphIsCached(slot.Hash, start, count, baseLevel, LanguageOf(-1));
                if (!slot.Hit) LetteraCaches.s_Stats.Misses++;
            }
            m_Slots.Add(slot);
        }

        void AnalyzeSlot(int index)
        {
            ParagraphSlot slot = m_Slots[index];
            int count = slot.End - slot.Start;
            if (count <= 0 || slot.Hit) return;
            int start = slot.Start;
            var text = new ReadOnlySpan<int>(m_Text, start, count);
            GraphemeBreaker.FindBoundaries(text, new Span<bool>(m_Boundaries, start, count + 1));
            slot.Level = Bidi.ResolveParagraph(text, slot.Level == 1 && BaseLevelForSlots == 1 ? 1 : BaseLevelForSlots, new Span<byte>(m_Levels, start, count));
            LineBreaker.FindBreaks(text, new Span<LineBreakOpportunity>(m_Breaks, start, count));
            if (LetteraWordSegmenters.Count != 0)
                LetteraWordSegmenters.ApplyToBreaks(text, new ReadOnlySpan<bool>(m_Boundaries, start, count + 1), new Span<LineBreakOpportunity>(m_Breaks, start, count), s_WorkerScratch ?? (s_WorkerScratch = new LetteraSegmenterScratch()));
            ScriptItemizer.Resolve(text, new Span<UnicodeScript>(m_Scripts, start, count));
            lock (m_Slots) m_Slots[index] = slot;
        }

        int BaseLevelForSlots;

        void ShapeDeferred(int index)
        {
            DeferredRun deferred = m_Deferred[index];
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
            lock (m_Deferred) m_Deferred[index] = deferred;
        }

        // Hits were copied in as they came, so runs that waited are put between them here.
        void MergeShaped()
        {
            int total = m_ShapedCount;
            foreach (DeferredRun deferred in m_Deferred) total += deferred.Count;
            EnsureSize(ref m_ShapedMerged, total);
            int at = 0, next = 0;
            for (int r = 0; r < m_Runs.Count; r++)
            {
                Run run = m_Runs[r];
                int glyphs;
                if (next < m_Deferred.Count && m_Deferred[next].RunIndex == r)
                {
                    DeferredRun deferred = m_Deferred[next++];
                    Array.Copy(deferred.Glyphs, 0, m_ShapedMerged, at, deferred.Count);
                    glyphs = deferred.Count;
                    ArrayPool<ShapedGlyph>.Shared.Return(deferred.Glyphs);
                }
                else
                {
                    Array.Copy(m_Shaped, run.GlyphStart, m_ShapedMerged, at, run.GlyphCount);
                    glyphs = run.GlyphCount;
                }
                run.GlyphStart = at;
                run.GlyphCount = glyphs;
                m_Runs[r] = run;
                at += glyphs;
            }
            ShapedGlyph[] swap = m_Shaped;
            m_Shaped = m_ShapedMerged;
            m_ShapedMerged = swap;
            m_ShapedCount = at;
        }

        void StoreMissedParagraphs(int baseLevel, string language)
        {
            for (int s = 0; s < m_Slots.Count; s++)
            {
                ParagraphSlot slot = m_Slots[s];
                if (!slot.Cacheable || slot.Hit || slot.RunCount == 0) continue;
                int firstGlyph = m_Runs[slot.FirstRun].GlyphStart;
                StoreParagraph(slot.Hash, new Paragraph { Start = slot.Start, ContentEnd = slot.ContentEnd, End = slot.End, Level = m_Paragraphs[s].Level }, slot.Start, slot.End - slot.Start, baseLevel, language, slot.FirstRun, firstGlyph, slot.FirstRun + slot.RunCount);
            }
        }

        // Where a run would shape now, with a paragraph-relative start: it waits, and what workers need from the main thread is ready for it.
        void DeferShaping(ref Run run)
        {
            string language = LanguageOf(run.Language);
            if (language != null && !s_LanguageBytes.ContainsKey(language)) s_LanguageBytes[language] = FontFace.NullTerminated(language);
            run.GlyphStart = 0;
            run.GlyphCount = 0;
            m_Deferred.Add(new DeferredRun { RunIndex = m_Runs.Count, ParagraphStart = m_DeferParagraphStart, ParagraphLength = m_DeferParagraphLength });
        }
    }
}

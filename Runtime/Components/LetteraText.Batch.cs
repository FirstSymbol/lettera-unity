using System;
using System.Collections.Generic;

namespace FirstSymbol.Lettera
{
    public partial class LetteraText
    {
        // Texts asked to draw again since the last batch, each once. The first geometry update of the canvases' rebuild takes them all.
        static List<LetteraText> s_Queued = new List<LetteraText>(), s_Taking = new List<LetteraText>();
        static readonly List<LetteraText> s_Candidates = new List<LetteraText>(), s_Quiet = new List<LetteraText>();
        static TextEngine[] s_Batch = new TextEngine[64];
        static readonly Action<int> s_BatchOne = i => s_Batch[i].BatchPasses();
        static bool s_Batching;

        // Where a batch's time goes on the main thread: parsing while the threads work, then waiting for them. Tests read this.
        internal static readonly long[] BatchTicks = new long[3];
        [NonSerialized] bool m_Queued;

        void QueueForBatch()
        {
            if (m_Queued || !LetteraWorkers.BatchShortTexts) return;
            m_Queued = true;
            s_Queued.Add(this);
        }

        // A parse that fails is left to the text's own rebuild, which meets it again and reports it as ever.
        static bool Begin(LetteraText text)
        {
            try { return text.m_Engine.BeginBatch(text.rectTransform.rect, ((ITextHost)text).RasterScale); }
            catch (Exception) { return false; }
        }

        static void TakeBatch()
        {
            if (s_Batching) return;
            s_Batching = true;
            // Swapped, so that a text queued while another is parsed here waits for the next batch.
            List<LetteraText> queued = s_Queued;
            s_Queued = s_Taking;
            s_Taking = queued;
            foreach (LetteraText text in queued) text.m_Queued = false;
            int count = 0;
            try
            {
                if (!LetteraWorkers.ShouldBatch(queued.Count)) return;
                foreach (LetteraText text in queued)
                {
                    // Most texts queued in a frame of colour or motion have no layout to do, and are let go before anything asks Unity.
                    TextEngine engine = text.m_Engine;
                    if (engine == null || (!text.m_HasAnimator && engine.Dirty < DirtyLevel.Layout)) continue;
                    // Graphic's rebuild skips a culled text, so it waits as it would have.
                    if (text == null || !text.IsActive() || text.canvasRenderer.cull) continue;
                    if (text.m_HasAnimator) engine.SyncSerializedFields();
                    if (engine.MayBatch) s_Candidates.Add(text);
                }
                if (!LetteraWorkers.ShouldBatch(s_Candidates.Count)) return;
                if (s_Batch.Length < s_Candidates.Count) s_Batch = new TextEngine[Math.Max(s_Candidates.Count, s_Batch.Length * 2)];
                LetteraWorkers.TextBatches++;
                TextEngine.s_BatchSettings = LetteraSettings.Instance;
                // A parse that may run a project's code, a resolver's, a hold's or a style's, is done before any thread starts.
                foreach (LetteraText text in s_Candidates)
                {
                    if (text.m_Engine.ParsesQuietly) s_Quiet.Add(text);
                    else if (Begin(text)) s_Batch[count++] = text.m_Engine;
                }
                // The threads wake while the main thread parses the rest, and each text goes to them as soon as it is parsed.
                long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
                LetteraWorkers.Open(s_BatchOne);
                try
                {
                    LetteraWorkers.Publish(count);
                    foreach (LetteraText text in s_Quiet)
                    {
                        if (!Begin(text)) continue;
                        s_Batch[count++] = text.m_Engine;
                        LetteraWorkers.Publish(count);
                    }
                }
                finally
                {
                    long t1 = System.Diagnostics.Stopwatch.GetTimestamp();
                    LetteraWorkers.Close();
                    long t2 = System.Diagnostics.Stopwatch.GetTimestamp();
                    BatchTicks[0] += t1 - t0;
                    BatchTicks[1] += t2 - t1;
                }
            }
            finally
            {
                TextEngine.s_BatchSettings = null;
                long t3 = System.Diagnostics.Stopwatch.GetTimestamp();
                for (int i = 0; i < count; i++)
                {
                    s_Batch[i].EndBatch();
                    s_Batch[i] = null;
                }
                BatchTicks[2] += System.Diagnostics.Stopwatch.GetTimestamp() - t3;
                queued.Clear();
                s_Candidates.Clear();
                s_Quiet.Clear();
                s_Batching = false;
            }
        }
    }
}

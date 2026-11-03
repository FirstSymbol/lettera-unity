using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using NUnit.Framework;
using UnityEngine;

namespace FirstSymbol.Lettera.Editor.Tests
{
    /// <summary>Threads change how long a rebuild takes, and nothing else.</summary>
    public class WorkerTests : TextFixture
    {
        [TearDown]
        public void Restore()
        {
            LetteraWorkers.Mode = LetteraWorkerMode.Auto;
            LetteraWorkers.ParallelFromCodepoints = 4096;
            LetteraWorkers.WorkerCount = -1;
            LetteraCaches.Enabled = true;
            LetteraCaches.Clear();
        }

        [Test]
        public void EveryIndexRunsOnceAcrossThreads()
        {
            LetteraWorkers.WorkerCount = 4;
            var seen = new int[5000];
            var threads = new System.Collections.Generic.HashSet<int>();
            LetteraWorkers.Run(seen.Length, i =>
            {
                Interlocked.Increment(ref seen[i]);
                lock (threads) threads.Add(Thread.CurrentThread.ManagedThreadId);
                Thread.SpinWait(200);
            });
            foreach (int count in seen) Assert.AreEqual(1, count);
            Assert.Greater(threads.Count, 1, "only one thread worked");
        }

        [Test]
        public void AFailureComesOutOnTheMainThreadAndThePoolGoesOn()
        {
            LetteraWorkers.WorkerCount = 2;
            Assert.Throws<InvalidOperationException>(() => LetteraWorkers.Run(50, i =>
            {
                if (i == 17) throw new ArgumentException("from a stage");
            }));
            int sum = 0;
            LetteraWorkers.Run(100, i => Interlocked.Add(ref sum, 1));
            Assert.AreEqual(100, sum);
        }

        [Test]
        public void ShutdownLeavesNoThreadAndTheNextBatchStartsAgain()
        {
            LetteraWorkers.WorkerCount = 2;
            LetteraWorkers.Run(10, _ => { });
            Assert.IsTrue(LetteraWorkers.HasThreads);
            LetteraWorkers.Shutdown();
            Assert.IsFalse(LetteraWorkers.HasThreads);
            int sum = 0;
            LetteraWorkers.Run(10, i => Interlocked.Add(ref sum, 1));
            Assert.AreEqual(10, sum);
        }

        [Test]
        public void ASmallTextStartsNoThread()
        {
            LetteraWorkers.Mode = LetteraWorkerMode.Parallel;
            LetteraWorkers.Shutdown();
            int before = LetteraWorkers.ParallelBatches;
            Make("A small text");
            Assert.AreEqual(before, LetteraWorkers.ParallelBatches);
            Assert.IsFalse(LetteraWorkers.HasThreads);
        }

        [Test]
        public void EditModeStaysInlineByDefault()
        {
            LetteraWorkers.Shutdown();
            int before = LetteraWorkers.ParallelBatches;
            Make(Big("words here "));
            Assert.AreEqual(before, LetteraWorkers.ParallelBatches);
        }

        static string Big(string line, int paragraphs = 300)
        {
            var body = new StringBuilder();
            for (int i = 0; i < paragraphs; i++) body.Append(line).Append(i).Append('\n');
            return body.ToString();
        }

        struct Snapshot
        {
            public Vector3[] Vertices;
            public Color32[] Colors;
            public Vector4[] Uv0;
            public int[] Indices;
            public int Lines;
        }

        static Snapshot Take(LetteraWorldText text)
        {
            text.ForceRebuild();
            TextEngine e = text.Engine;
            var shot = new Snapshot { Vertices = new Vector3[e.VertexCount], Colors = new Color32[e.VertexCount], Uv0 = new Vector4[e.VertexCount], Indices = new int[e.IndexCount], Lines = e.LineCountForHooks };
            Array.Copy(e.Vertices, shot.Vertices, e.VertexCount);
            Array.Copy(e.Colors, shot.Colors, e.VertexCount);
            Array.Copy(e.Uv0, shot.Uv0, e.VertexCount);
            Array.Copy(e.Indices, shot.Indices, e.IndexCount);
            return shot;
        }

        static readonly string[] s_Corpus =
        {
            "Kerning AVATAR To Ty fi ffl office ",
            "مرحبا بالعالم هذا ",
            "שלום עולם abc גם ",
            "नमस्ते दुनिया ",
            "Mixed abc مرحبا def שלום ",
            "<b>bold</b> and <i>italic</i> and <size=30>larger</size> ",
            "<otf=-liga>office</otf> and <smallcaps>Caps</smallcaps> <color=#f00>red</color> ",
        };

        Snapshot Built(string body, LetteraFontStack stack, bool cache)
        {
            LetteraCaches.Enabled = cache;
            LetteraWorldText label = Make("", 600, 5000);
            label.Font = null;
            label.FontStack = stack;
            label.Text = body;
            return Take(label);
        }

        static void AssertSame(in Snapshot a, in Snapshot b, string what)
        {
            Assert.AreEqual(a.Lines, b.Lines, what + " lines");
            CollectionAssert.AreEqual(a.Vertices, b.Vertices, what + " vertices");
            CollectionAssert.AreEqual(a.Uv0, b.Uv0, what + " uvs");
            CollectionAssert.AreEqual(a.Indices, b.Indices, what + " indices");
            for (int i = 0; i < a.Colors.Length; i++) Assert.AreEqual(a.Colors[i], b.Colors[i], what + " colors");
        }

        [TestCase(1)]
        [TestCase(2)]
        [TestCase(4)]
        public void ParallelGivesExactlyWhatInlineGives(int workers)
        {
            LetteraFontStack stack = Stack(Latin, Arabic, Hebrew, Devanagari);
            LetteraWorkers.WorkerCount = workers;
            foreach (string line in s_Corpus)
            {
                string body = Big(line, 400);
                LetteraWorkers.Mode = LetteraWorkerMode.Inline;
                LetteraCaches.Clear();
                Snapshot inline = Built(body, stack, false);

                LetteraWorkers.Mode = LetteraWorkerMode.Parallel;
                int batches = LetteraWorkers.ParallelBatches;
                LetteraCaches.Clear();
                AssertSame(inline, Built(body, stack, false), $"no cache, {workers} workers: {line}");
                Assert.Greater(LetteraWorkers.ParallelBatches, batches, "it ran inline");
                LetteraCaches.Clear();
                AssertSame(inline, Built(body, stack, true), $"filling the cache: {line}");
                AssertSame(inline, Built(body, stack, true), $"from the cache: {line}");
            }
        }

        [Test]
        public void ItIsFasterOnABigText()
        {
            var body = new StringBuilder();
            for (int i = 0; i < 1500; i++) body.Append("Some different words in the paragraph number ").Append(i).Append(" of this rather long body of text\n");
            string text = body.ToString();
            LetteraWorkers.WorkerCount = 4;
            LetteraWorldText label = Make("", 600, 5000);
            double Time(LetteraWorkerMode mode)
            {
                LetteraWorkers.Mode = mode;
                LetteraCaches.Enabled = false;
                label.Text = text + (int)mode;
                label.ForceRebuild();
                double best = double.MaxValue;
                for (int run = 0; run < 5; run++)
                {
                    label.SetDirty(DirtyLevel.Shape);
                    System.Array.Clear(TextEngine.PassTicks, 0, TextEngine.PassTicks.Length);
                    TextEngine.TimePasses = true;
                    label.ForceRebuild();
                    TextEngine.TimePasses = false;
                    best = Math.Min(best, TextEngine.PassTicks[1] * 1000.0 / System.Diagnostics.Stopwatch.Frequency);
                }
                return best;
            }
            double inline = Time(LetteraWorkerMode.Inline), parallel = Time(LetteraWorkerMode.Parallel);
            Debug.Log($"LETTERA_BUDGET shape_{text.Length}cp inline={inline:0.00}ms parallel_4={parallel:0.00}ms");
            Assert.Less(parallel, inline, "threads made it slower");
        }

        static string Sentence(int i)
        {
            const string Words = "The quick brown fox jumps over a lazy dog and then runs back home again ";
            return Words.Substring(i % 20, 12 + i * 7 % 29);
        }

        // Lines, then the mesh, of each window a fast scroll builds: lean ones, which break and draw a viewport of new text each step.
        List<string> ScrollShots(string page, LetteraWorkerMode mode)
        {
            LetteraWorkers.Mode = mode;
            LetteraWorldText text = Make("", 800, 600, null, 14);
            text.OpenDocument(LetteraRope.FromString(page), true);
            text.ForceRebuild();
            var shots = new List<string>();
            double step = text.View.ViewportHeight * 1.7;
            for (int i = 1; i <= 12; i++)
            {
                text.View.ScrollTo(i * step);
                text.ForceRebuild();
                Assert.IsTrue(text.View.IsLean, "a lean window");
                TextEngine e = text.Engine;
                var shot = new StringBuilder();
                for (int l = 0; l < e.LineCountForHooks; l++)
                {
                    LetteraLine line = e.LineForHooks(l);
                    shot.Append(line.Start).Append(' ').Append(line.End).Append(' ').Append(line.VisibleEnd).Append(' ').Append(line.Paragraph).Append(' ')
                        .Append(Bits(line.X)).Append(Bits(line.Baseline)).Append(Bits(line.Width)).Append(Bits(line.Height)).Append(Bits(line.Ascent)).Append(Bits(line.Descent))
                        .Append(Bits(line.GapBefore)).Append(Bits(line.Margin)).Append(line.EndedByMandatoryBreak).Append('\n');
                }
                Snapshot mesh = Take(text);
                shot.Append(e.VertexCount).Append(' ').Append(e.IndexCount).Append(' ').Append(Bits(e.MeshBounds.center.x)).Append(Bits(e.MeshBounds.center.y))
                    .Append(Bits(e.MeshBounds.size.x)).Append(Bits(e.MeshBounds.size.y)).Append('\n');
                for (int v = 0; v < mesh.Vertices.Length; v++)
                    shot.Append(Bits(mesh.Vertices[v].x)).Append(Bits(mesh.Vertices[v].y)).Append(Bits(mesh.Uv0[v].x)).Append(Bits(mesh.Uv0[v].y)).Append(mesh.Colors[v].ToString()).Append('\n');
                foreach (int index in mesh.Indices) shot.Append(index).Append(',');
                shots.Add(shot.ToString());
            }
            return shots;
        }

        static string Bits(float value) => System.BitConverter.SingleToInt32Bits(value).ToString("x8") + " ";

        [TestCase(1)]
        [TestCase(4)]
        public void ALeanDocumentWindowIsTheSameAcrossWorkers(int workers)
        {
            var page = new StringBuilder();
            int n = 0;
            while (page.Length < 300 * 1024) page.Append(Sentence(n++)).Append(n % 8 == 0 ? "\n" : " ");
            LetteraWorkers.WorkerCount = workers;
            // A window here holds about 5000 codepoints; this keeps it across the threads whatever the harness's threshold.
            LetteraWorkers.ParallelFromCodepoints = 1024;
            List<string> inline = ScrollShots(page.ToString(), LetteraWorkerMode.Inline);
            var before = (int[])TextEngine.SplitPasses.Clone();
            List<string> parallel = ScrollShots(page.ToString(), LetteraWorkerMode.Parallel);
            Assert.Greater(TextEngine.SplitPasses[0], before[0], "lines were broken in order");
            Assert.Greater(TextEngine.SplitPasses[1], before[1], "glyphs were placed in order");
            Assert.Greater(TextEngine.SplitPasses[2], before[2], "faces were drawn in order");
            Assert.AreEqual(inline.Count, parallel.Count);
            for (int i = 0; i < inline.Count; i++) Assert.AreEqual(inline[i], parallel[i], "window " + i);
        }

        // A text under a mask notes where its ink lies while it draws: the threads' boxes join into the same box.
        string MaskedShot(string body, LetteraWorkerMode mode)
        {
            LetteraWorkers.Mode = mode;
            var canvas = new GameObject("Canvas", typeof(Canvas));
            Created.Add(canvas);
            var mask = new GameObject("Mask", typeof(RectTransform), typeof(UnityEngine.UI.RectMask2D));
            mask.transform.SetParent(canvas.transform, false);
            ((RectTransform)mask.transform).sizeDelta = new Vector2(900, 2000);
            var go = new GameObject("Label", typeof(RectTransform));
            go.transform.SetParent(mask.transform, false);
            ((RectTransform)go.transform).sizeDelta = new Vector2(800, 1900);
            var text = go.AddComponent<LetteraText>();
            text.Font = Latin;
            text.FontSize = 14;
            text.Text = body;
            Canvas.ForceUpdateCanvases();
            Canvas.ForceUpdateCanvases();
            TextEngine e = text.Engine;
            Assert.IsTrue(text.TracksInk, "under a mask");
            var shot = new StringBuilder();
            shot.Append(e.InkKnown).Append(' ').Append(Bits(e.InkRect.xMin)).Append(Bits(e.InkRect.yMin)).Append(Bits(e.InkRect.xMax)).Append(Bits(e.InkRect.yMax)).Append(e.VertexCount).Append('\n');
            for (int v = 0; v < e.VertexCount; v++) shot.Append(Bits(e.Vertices[v].x)).Append(Bits(e.Vertices[v].y)).Append(e.Colors[v].ToString()).Append('\n');
            return shot.ToString();
        }

        [Test]
        public void AMaskedTextNotesTheSameInkAcrossWorkers()
        {
            var body = new StringBuilder();
            for (int i = 0; body.Length < 6000; i++) body.Append(Sentence(i)).Append(i % 8 == 0 ? "\n" : " ");
            LetteraWorkers.WorkerCount = 4;
            LetteraWorkers.ParallelFromCodepoints = 512;
            string inline = MaskedShot(body.ToString(), LetteraWorkerMode.Inline);
            int drawn = TextEngine.SplitPasses[2];
            string parallel = MaskedShot(body.ToString(), LetteraWorkerMode.Parallel);
            Assert.Greater(TextEngine.SplitPasses[2], drawn, "faces were drawn in order");
            Assert.IsTrue(inline.StartsWith("True"), "the ink is known: " + inline.Substring(0, 60));
            Assert.AreEqual(inline, parallel);
        }

        [Test]
        public void ThreadedTextEndsTheSameThroughEdits()
        {
            LetteraFontStack stack = Stack(Latin, Arabic);
            var random = new System.Random(4);
            LetteraWorkers.WorkerCount = 4;
            for (int step = 0; step < 40; step++)
            {
                var body = new StringBuilder();
                int lines = random.Next(60, 400);
                for (int i = 0; i < lines; i++) body.Append(s_Corpus[random.Next(s_Corpus.Length)]).Append(random.Next(1000)).Append('\n');
                LetteraWorkers.Mode = LetteraWorkerMode.Inline;
                LetteraCaches.Clear();
                Snapshot inline = Built(body.ToString(), stack, true);
                LetteraWorkers.Mode = LetteraWorkerMode.Parallel;
                LetteraCaches.Clear();
                AssertSame(inline, Built(body.ToString(), stack, true), "at step " + step);
            }
        }
    }
}

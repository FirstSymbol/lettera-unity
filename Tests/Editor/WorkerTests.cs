using System;
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

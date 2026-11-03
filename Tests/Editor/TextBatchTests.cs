using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace FirstSymbol.Lettera.Editor.Tests
{
    /// <summary>Short texts changed in one frame go through one batch on the threads, and each draws what it draws alone.</summary>
    public class TextBatchTests : TextFixture
    {
        readonly List<LetteraText> m_Labels = new List<LetteraText>();

        [SetUp]
        public void Begin()
        {
            m_Labels.Clear();
            Debug.Log("LETTERA_TEST start " + TestContext.CurrentContext.Test.Name);
        }

        [TearDown]
        public void Restore()
        {
            LetteraWorkers.Mode = LetteraWorkerMode.Auto;
            LetteraWorkers.WorkerCount = -1;
            LetteraWorkers.BatchShortTexts = true;
            LetteraWorkers.BatchFromTexts = 8;
            LetteraCaches.Enabled = true;
            LetteraCaches.Clear();
            TextEngine.BatchMeshes = true;
        }

        static readonly string[] s_Texts =
        {
            "0", "42", "Score: 01234", "", " ", "Kerning AVATAR To Ty fi ffl office",
            "A label long enough to wrap onto more lines in its box", "Two\nparagraphs", "Overflowing with an ellipsis at its end, too long to fit",
            "مرحبا بالعالم", "Mixed abc مرحبا def שלום", "<b>bold</b> and <i>italic</i>", "<color=#f00>red</color> <size=30>big</size>",
            "<u>under</u> <s>struck</s>", "nine 9", "ten 10", "eleven 11", "Three\n\nparagraphs",
        };

        void MakeLabels()
        {
            LetteraFontStack stack = Stack(Latin, Arabic, Hebrew);
            var root = new GameObject("Canvas", typeof(RectTransform), typeof(Canvas));
            Created.Add(root);
            root.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
            ((RectTransform)root.transform).sizeDelta = new Vector2(800, 800);
            for (int i = 0; i < s_Texts.Length; i++)
            {
                var go = new GameObject("Label " + i, typeof(RectTransform));
                go.transform.SetParent(root.transform, false);
                ((RectTransform)go.transform).sizeDelta = new Vector2(180, 60);
                var label = go.AddComponent<LetteraText>();
                label.raycastTarget = false;
                label.Font = null;
                label.FontStack = stack;
                label.FontSize = 18;
                if (i % 3 == 1) label.HorizontalAlignment = LetteraHorizontalAlignment.Center;
                if (i % 5 == 2) label.VerticalAlignment = LetteraVerticalAlignment.Middle;
                if (i == 6) label.AutoSize = true;
                if (i == 8) label.Overflow = LetteraOverflow.Ellipsis;
                if (i == 10) label.WordWrap = false;
                label.Text = "x";
                m_Labels.Add(label);
            }
            Canvas.ForceUpdateCanvases();
        }

        struct Shot
        {
            public Vector3[] Vertices;
            public Color32[] Colors;
            public Vector4[] Uv0, Uv1;
            public int[] Indices;
            public int Lines, Parses, Shapes;
            public Rect Bounds;
            public LetteraChanges Changes;
            public float Size;
            public int MeshVertices;
        }

        static T[] Copy<T>(T[] from, int count)
        {
            var to = new T[count];
            Array.Copy(from, to, count);
            return to;
        }

        List<Shot> Frame(Func<int, string> text) => Frame(i => { m_Labels[i].Text = text(i); });

        List<Shot> Frame(Action<int> change)
        {
            var parses = new int[m_Labels.Count];
            var shapes = new int[m_Labels.Count];
            for (int i = 0; i < m_Labels.Count; i++)
            {
                TextEngine e = m_Labels[i].Engine;
                parses[i] = e.ParseCount;
                shapes[i] = e.ShapeCount;
                change(i);
            }
            var changes = new LetteraChanges[m_Labels.Count];
            for (int i = 0; i < m_Labels.Count; i++)
            {
                int index = i;
                m_Labels[i].Rebuilt += c => changes[index] = c;
            }
            Canvas.ForceUpdateCanvases();
            var shots = new List<Shot>();
            for (int i = 0; i < m_Labels.Count; i++)
            {
                LetteraText label = m_Labels[i];
                TextEngine e = label.Engine;
                shots.Add(new Shot
                {
                    Vertices = Copy(e.Vertices, e.VertexCount), Colors = Copy(e.Colors, e.VertexCount), Uv0 = Copy(e.Uv0, e.VertexCount), Uv1 = Copy(e.Uv1, e.VertexCount),
                    Indices = Copy(e.Indices, e.IndexCount), Lines = label.Lines.Count, Bounds = label.LayoutBounds, Changes = changes[i], Size = label.CurrentFontSize,
                    Parses = e.ParseCount - parses[i], Shapes = e.ShapeCount - shapes[i], MeshVertices = label.MeshForTests != null ? label.MeshForTests.vertexCount : -1,
                });
            }
            return shots;
        }

        static void AssertSame(List<Shot> alone, List<Shot> batched)
        {
            Assert.AreEqual(alone.Count, batched.Count);
            for (int i = 0; i < alone.Count; i++)
            {
                Shot a = alone[i], b = batched[i];
                string what = "text " + i + ": " + s_Texts[i];
                Assert.AreEqual(a.Lines, b.Lines, what + " lines");
                Assert.AreEqual(a.Bounds, b.Bounds, what + " bounds");
                Assert.AreEqual(a.Size, b.Size, what + " size");
                Assert.AreEqual(a.Changes, b.Changes, what + " changes reported");
                Assert.AreEqual(a.Parses, b.Parses, what + " parses");
                Assert.AreEqual(a.Shapes, b.Shapes, what + " shapes");
                Assert.AreEqual(a.MeshVertices, b.MeshVertices, what + " uploaded vertices");
                CollectionAssert.AreEqual(a.Vertices, b.Vertices, what + " vertices");
                CollectionAssert.AreEqual(a.Uv0, b.Uv0, what + " uv0");
                CollectionAssert.AreEqual(a.Uv1, b.Uv1, what + " uv1");
                CollectionAssert.AreEqual(a.Indices, b.Indices, what + " indices");
                for (int v = 0; v < a.Colors.Length; v++) Assert.AreEqual(a.Colors[v], b.Colors[v], what + " colors");
            }
        }

        [TestCase(1, true)]
        [TestCase(4, true)]
        [TestCase(4, false)]
        public void ABatchDrawsWhatEachTextDrawsAlone(int workers, bool meshes)
        {
            LetteraWorkers.WorkerCount = workers;
            TextEngine.BatchMeshes = meshes;
            MakeLabels();
            LetteraWorkers.Mode = LetteraWorkerMode.Inline;
            List<Shot> alone = Frame(i => s_Texts[i]);
            Frame(i => "other " + i);

            LetteraWorkers.Mode = LetteraWorkerMode.Parallel;
            int batches = LetteraWorkers.TextBatches, threads = LetteraWorkers.ParallelBatches, done = TextEngine.BatchedTexts, meshed = TextEngine.BatchedMeshes;
            List<Shot> batched = Frame(i => s_Texts[i]);
            Debug.Log($"batched {TextEngine.BatchedTexts - done} texts, {TextEngine.BatchedMeshes - meshed} meshes, of {s_Texts.Length}");
            if (meshes) Assert.Greater(TextEngine.BatchedMeshes - meshed, s_Texts.Length / 2, "few meshes were built on the threads");
            else Assert.AreEqual(meshed, TextEngine.BatchedMeshes);
            Assert.AreEqual(batches + 1, LetteraWorkers.TextBatches, "the frame made no batch");
            Assert.Greater(LetteraWorkers.ParallelBatches, threads, "the batch stayed on the main thread");
            // Every text the cache holds goes through, all but the one with an ellipsis.
            Assert.AreEqual(s_Texts.Length - 1, TextEngine.BatchedTexts - done, "texts the batch took");
            AssertSame(alone, batched);

            // Again, as a text changes back and forth each frame.
            Frame(i => "other " + i);
            AssertSame(alone, Frame(i => s_Texts[i]));
        }

        // A paragraph no cache holds is shaped by the main thread: the text leaves the batch and rebuilds alone, counted as it would be.
        [Test]
        public void ATextShapedAnewRebuildsAloneTheSame()
        {
            LetteraWorkers.WorkerCount = 4;
            MakeLabels();
            LetteraCaches.Statistics Run(LetteraWorkerMode mode, out List<Shot> shots)
            {
                LetteraWorkers.Mode = LetteraWorkerMode.Inline;
                LetteraCaches.Clear();
                Frame(i => s_Texts[i]);
                LetteraWorkers.Mode = mode;
                LetteraCaches.ResetStats();
                // The first paragraph of the paragraphs' texts is held, the last is new.
                shots = Frame(i => s_Texts[i] + "!");
                return LetteraCaches.Stats;
            }
            LetteraCaches.Statistics alone = Run(LetteraWorkerMode.Inline, out List<Shot> aloneShots);
            int done = TextEngine.BatchedTexts;
            LetteraCaches.Statistics batched = Run(LetteraWorkerMode.Parallel, out List<Shot> batchedShots);
            Assert.AreEqual(done, TextEngine.BatchedTexts, "a text with a new paragraph went through");
            AssertSame(aloneShots, batchedShots);
            Assert.AreEqual(alone.Hits, batched.Hits, "hits");
            Assert.AreEqual(alone.Misses, batched.Misses, "misses");
            Assert.AreEqual(alone.ShapeCalls, batched.ShapeCalls, "shaping calls");
        }

        // Shaping comes from the cache, but the glyphs at a new size are not in the atlas yet: the main thread makes them and builds the mesh.
        [Test]
        public void AGlyphTheAtlasLacksIsMadeOnTheMainThread()
        {
            LetteraWorkers.WorkerCount = 4;
            MakeLabels();
            LetteraWorkers.Mode = LetteraWorkerMode.Inline;
            Frame(i => s_Texts[i]);
            LetteraWorkers.Mode = LetteraWorkerMode.Parallel;
            int done = TextEngine.BatchedTexts, meshed = TextEngine.BatchedMeshes;
            List<Shot> batched = Frame(i => m_Labels[i].FontSize = 90);
            Assert.Greater(TextEngine.BatchedTexts, done, "no text went through");
            // Only texts with no ink to make, the empty one and the space, build theirs there.
            Assert.LessOrEqual(TextEngine.BatchedMeshes - meshed, 3, "a mesh with new glyphs was built on a thread");
            LetteraWorkers.Mode = LetteraWorkerMode.Inline;
            List<Shot> alone = Frame(i => m_Labels[i].SetDirty(DirtyLevel.Layout));
            for (int i = 0; i < alone.Count; i++)
            {
                Shot a = alone[i];
                a.Changes = batched[i].Changes;
                alone[i] = a;
            }
            AssertSame(alone, batched);
        }

        [Test]
        public void HitsCountAsTheyDoAlone()
        {
            LetteraWorkers.WorkerCount = 4;
            MakeLabels();
            LetteraWorkers.Mode = LetteraWorkerMode.Inline;
            Frame(i => s_Texts[i]);
            Frame(i => "other " + i);
            LetteraCaches.ResetStats();
            Frame(i => s_Texts[i]);
            LetteraCaches.Statistics alone = LetteraCaches.Stats;
            Frame(i => "other " + i);
            LetteraWorkers.Mode = LetteraWorkerMode.Parallel;
            LetteraCaches.ResetStats();
            Frame(i => s_Texts[i]);
            LetteraCaches.Statistics batched = LetteraCaches.Stats;
            Assert.Greater(alone.Hits, 0);
            Assert.AreEqual(alone.Hits, batched.Hits, "hits");
            Assert.AreEqual(alone.Misses, batched.Misses, "misses");
            Assert.AreEqual(alone.Bytes, batched.Bytes, "bytes");
        }

        [Test]
        public void FewTextsStayOnTheMainThread()
        {
            LetteraWorkers.Mode = LetteraWorkerMode.Parallel;
            MakeLabels();
            int batches = LetteraWorkers.TextBatches;
            for (int i = 0; i < 3; i++) m_Labels[i].Text = "few " + i;
            Canvas.ForceUpdateCanvases();
            Assert.AreEqual(batches, LetteraWorkers.TextBatches);
            Assert.AreEqual("few 2", m_Labels[2].CleanText);
        }

        [Test]
        public void TheSwitchKeepsEveryTextAlone()
        {
            LetteraWorkers.Mode = LetteraWorkerMode.Parallel;
            LetteraWorkers.BatchShortTexts = false;
            MakeLabels();
            Frame(i => s_Texts[i]);
            Frame(i => "other " + i);
            int batches = LetteraWorkers.TextBatches;
            Frame(i => s_Texts[i]);
            Assert.AreEqual(batches, LetteraWorkers.TextBatches);
        }

        [Test]
        public void EditModeKeepsEveryTextAloneByDefault()
        {
            MakeLabels();
            Frame(i => s_Texts[i]);
            Frame(i => "other " + i);
            int batches = LetteraWorkers.TextBatches;
            Frame(i => s_Texts[i]);
            Assert.AreEqual(batches, LetteraWorkers.TextBatches);
        }

        // A text's own query between the batch and its mesh, here another text's listener, reads it placed and new.
        [Test]
        public void AQueryBeforeTheMeshReadsTheNewText()
        {
            LetteraWorkers.Mode = LetteraWorkerMode.Parallel;
            LetteraWorkers.WorkerCount = 4;
            MakeLabels();
            Frame(i => s_Texts[i]);
            Frame(i => "other " + i);
            LetteraText first = m_Labels[0], last = m_Labels[m_Labels.Count - 1];
            string seen = null;
            int lines = -1;
            first.Rebuilt += _ =>
            {
                if (seen != null) return;
                seen = last.CleanText;
                lines = last.Lines.Count;
            };
            int done = TextEngine.BatchedTexts;
            for (int i = 0; i < m_Labels.Count; i++) m_Labels[i].Text = s_Texts[i];
            Canvas.ForceUpdateCanvases();
            Assert.Greater(TextEngine.BatchedTexts, done, "no batch");
            Assert.AreEqual(s_Texts[s_Texts.Length - 1], seen);
            Assert.AreEqual(3, lines);
        }

        // The frame of 64 changing counters, alone and in a batch, in turns. Only logged: an editor's timings move with what else the machine does.
        [Test]
        public void ManyChangingLabelsTakeTheirTurnsInABatch()
        {
            var root = new GameObject("Canvas", typeof(RectTransform), typeof(Canvas));
            Created.Add(root);
            for (int i = 0; i < 64; i++)
            {
                var go = new GameObject("Counter", typeof(RectTransform));
                go.transform.SetParent(root.transform, false);
                ((RectTransform)go.transform).sizeDelta = new Vector2(100, 30);
                var label = go.AddComponent<LetteraText>();
                label.raycastTarget = false;
                label.Font = Latin;
                label.FontSize = 18;
                label.Text = "0";
                m_Labels.Add(label);
            }
            var strings = new string[1000];
            for (int i = 0; i < strings.Length; i++) strings[i] = i.ToString();
            int tick = 0;
            var clock = new System.Diagnostics.Stopwatch();
            double Median(LetteraWorkerMode mode, int frames, bool passes)
            {
                LetteraWorkers.Mode = mode;
                var times = new List<double>();
                Array.Clear(TextEngine.PassTicks, 0, TextEngine.PassTicks.Length);
                for (int f = 0; f < frames; f++)
                {
                    tick++;
                    for (int i = 0; i < m_Labels.Count; i++) m_Labels[i].Text = strings[(tick + i) % strings.Length];
                    TextEngine.TimePasses = passes;
                    clock.Restart();
                    Canvas.ForceUpdateCanvases();
                    times.Add(clock.Elapsed.TotalMilliseconds);
                    TextEngine.TimePasses = false;
                }
                times.Sort();
                return times[times.Count / 2];
            }
            LetteraWorkers.WorkerCount = 4;
            Median(LetteraWorkerMode.Inline, 1100, false);
            Median(LetteraWorkerMode.Parallel, 100, false);
            double inline = 0, batched = 0, placedOnly = 0;
            for (int round = 0; round < 3; round++)
            {
                inline += Median(LetteraWorkerMode.Inline, 200, false) / 3;
                TextEngine.BatchMeshes = false;
                placedOnly += Median(LetteraWorkerMode.Parallel, 200, false) / 3;
                TextEngine.BatchMeshes = true;
                batched += Median(LetteraWorkerMode.Parallel, 200, false) / 3;
            }
            Array.Clear(LetteraText.BatchTicks, 0, 3);
            int mainItems = LetteraWorkers.MainItems, doneBefore = TextEngine.BatchedTexts;
            Median(LetteraWorkerMode.Parallel, 200, false);
            double Ms(long ticks) => ticks * 1000.0 / System.Diagnostics.Stopwatch.Frequency / 200;
            string shares = $" batch a frame: parse while threads work={Ms(LetteraText.BatchTicks[0]):0.000} wait={Ms(LetteraText.BatchTicks[1]):0.000} end={Ms(LetteraText.BatchTicks[2]):0.000} main did {LetteraWorkers.MainItems - mainItems} of {TextEngine.BatchedTexts - doneBefore}";
            Median(LetteraWorkerMode.Inline, 200, true);
            var split = new System.Text.StringBuilder();
            string[] names = { "parse", "shape", "layout", "positions", "mesh" };
            for (int p = 0; p < 5; p++) split.Append(' ').Append(names[p]).Append('=').Append((TextEngine.PassTicks[p] * 1000.0 / System.Diagnostics.Stopwatch.Frequency / 200).ToString("0.000"));
            Debug.Log($"LETTERA_BUDGET counters_64_frame inline={inline:0.000}ms batched={batched:0.000}ms meshes_on_main={placedOnly:0.000}ms workers={LetteraWorkers.WorkerCount}; inline passes a frame, ms:{split};{shares}");
            Assert.Greater(TextEngine.BatchedTexts, 0);
        }
    }
}

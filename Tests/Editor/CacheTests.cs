using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace FirstSymbol.Lettera.Editor.Tests
{
    /// <summary>The shared caches: a result from them equals a fresh one, and what they save shows in the counters.</summary>
    public class CacheTests : TextFixture
    {
        [SetUp]
        public void Fresh()
        {
            LetteraCaches.Enabled = true;
            LetteraCaches.ForceCollisions = false;
            LetteraCaches.Clear();
            LetteraCaches.ResetStats();
        }

        [TearDown]
        public void Restore()
        {
            LetteraCaches.Enabled = true;
            LetteraCaches.ForceCollisions = false;
            LetteraCaches.ParagraphCacheBytes = 8L << 20;
            LetteraCaches.Clear();
        }

        const string Sentence = "The quick brown fox jumps over.";

        struct Snapshot
        {
            public Vector3[] Vertices;
            public Color32[] Colors;
            public int Lines;
        }

        static Snapshot Take(LetteraWorldText text)
        {
            text.ForceRebuild();
            TextEngine e = text.Engine;
            var vertices = new Vector3[e.VertexCount];
            var colors = new Color32[e.VertexCount];
            System.Array.Copy(e.Vertices, vertices, e.VertexCount);
            System.Array.Copy(e.Colors, colors, e.VertexCount);
            return new Snapshot { Vertices = vertices, Colors = colors, Lines = e.LineCountForHooks };
        }

        static void AssertSame(Snapshot a, Snapshot b, string what)
        {
            Assert.AreEqual(a.Lines, b.Lines, what + " lines");
            CollectionAssert.AreEqual(a.Vertices, b.Vertices, what + " vertices");
            Assert.AreEqual(a.Colors.Length, b.Colors.Length, what + " colors");
            for (int i = 0; i < a.Colors.Length; i++) Assert.AreEqual(a.Colors[i], b.Colors[i], what + " color");
        }

        [Test]
        public void ManyLabelsOfOneTextShapeOnce()
        {
            for (int i = 0; i < 50; i++) Make(Sentence);
            Assert.AreEqual(1, LetteraCaches.Stats.ShapeCalls);
            Assert.AreEqual(49, LetteraCaches.Stats.Hits);
        }

        [Test]
        public void RepeatedParagraphsInOneTextShapeOnce()
        {
            var body = new System.Text.StringBuilder();
            for (int i = 0; i < 100; i++) body.Append(Sentence).Append('\n');
            Make(body.ToString());
            // Each one ends in its separator but the last, which is empty and not cached.
            Assert.AreEqual(1, LetteraCaches.Stats.ShapeCalls);
        }

        static readonly string[] s_Corpus =
        {
            "Kerning AVATAR To Ty fi ffl office",
            "مرحبا بالعالم هذا نص",
            "שלום עולם abc גם",
            "नमस्ते दुनिया क्ष",
            "Mixed abc مرحبا def שלום 123",
            "<b>bold words</b> and <i>italic words</i> and <size=30>larger words</size>",
            "<smallcaps>Small Caps Here</smallcaps> and <sup>superior</sup> text",
            "<otf=-liga>office affinity</otf> and <otf=+smcp>features on</otf>",
            "First paragraph here\nSecond paragraph here\r\nThird paragraph here\n\nFifth paragraph",
            "<wave>moving words here</wave> and <color=#f00>red words here</color>",
        };

        [Test]
        public void ACachedResultEqualsAFreshOneAcrossScripts()
        {
            LetteraFontStack stack = Stack(Latin, Arabic, Hebrew, Devanagari);
            foreach (string sample in s_Corpus)
            {
                LetteraCaches.Enabled = false;
                LetteraWorldText plain = Make("", font: Latin);
                plain.Font = null;
                plain.FontStack = stack;
                plain.Text = sample;
                Snapshot fresh = Take(plain);

                LetteraCaches.Enabled = true;
                LetteraCaches.Clear();
                LetteraWorldText first = Make("", font: Latin);
                first.Font = null;
                first.FontStack = stack;
                first.Text = sample;
                AssertSame(fresh, Take(first), "a miss: " + sample);
                LetteraWorldText second = Make("", font: Latin);
                second.Font = null;
                second.FontStack = stack;
                second.Text = sample;
                AssertSame(fresh, Take(second), "a hit: " + sample);
            }
        }

        [Test]
        public void AColorChangeDoesNotTouchItAndAWeightChangeDoes()
        {
            LetteraWorldText text = Make("<color=#f00>" + Sentence + "</color>");
            LetteraCaches.Statistics before = LetteraCaches.Stats;
            text.Text = "<color=#0f0>" + Sentence + "</color>";
            text.ForceRebuild();
            Assert.AreEqual(before.Misses, LetteraCaches.Stats.Misses, "a color shapes the same");
            text.Text = "<b><color=#0f0>" + Sentence + "</color></b>";
            text.ForceRebuild();
            Assert.Greater(LetteraCaches.Stats.Misses, before.Misses, "a weight does not");
        }

        [Test]
        public void TheSameCharactersInAnotherFontAreNotTheSameEntry()
        {
            LetteraWorldText a = Make(Sentence, font: Latin);
            LetteraWorldText b = Make(Sentence, font: Hebrew);
            Assert.AreEqual(0, LetteraCaches.Stats.Hits);
            Assert.AreNotEqual(a.Engine.ShapedGlyphIdForTests(0), 0u);
            Assert.AreEqual(2, LetteraCaches.Stats.Misses);
            _ = b;
        }

        [Test]
        public void ACollisionNeverDrawsTheWrongText()
        {
            Snapshot wantedA, wantedB;
            LetteraCaches.Enabled = false;
            wantedA = Take(Make("Alpha alpha alpha"));
            wantedB = Take(Make("Bravo bravo bravo"));
            LetteraCaches.Enabled = true;
            LetteraCaches.Clear();
            LetteraCaches.ForceCollisions = true;
            AssertSame(wantedA, Take(Make("Alpha alpha alpha")), "first");
            AssertSame(wantedB, Take(Make("Bravo bravo bravo")), "second, which hashes like the first");
            AssertSame(wantedA, Take(Make("Alpha alpha alpha")), "first again");
        }

        [Test]
        public void TheBudgetHolds()
        {
            LetteraCaches.ParagraphCacheBytes = 64 * 1024;
            for (int i = 0; i < 400; i++) Make("Some words that differ here " + i);
            Assert.LessOrEqual(LetteraCaches.Stats.Bytes, 64 * 1024);
            Assert.Greater(LetteraCaches.Stats.Evictions, 0);
        }

        [Test]
        public void ClearFreesItAndAFontChangeMakesOldEntriesUnreachable()
        {
            LetteraWorldText text = Make(Sentence);
            Assert.Greater(LetteraCaches.Stats.Bytes, 0);
            LetteraCaches.Clear();
            Assert.AreEqual(0, LetteraCaches.Stats.Bytes);
            text.SetDirty(DirtyLevel.Shape);
            text.ForceRebuild();
            long shapes = LetteraCaches.Stats.ShapeCalls;
            LetteraSettings.Instance.DisableEmoji = LetteraSettings.Instance.DisableEmoji;
            text.SetDirty(DirtyLevel.Shape);
            text.ForceRebuild();
            Assert.Greater(LetteraCaches.Stats.ShapeCalls, shapes);
        }

        [Test]
        public void TextsOfOneFontSetShareTheirChain()
        {
            LetteraWorldText a = Make(Sentence);
            LetteraWorldText b = Make("Other words altogether");
            Assert.AreSame(a.Engine.ChainForTests, b.Engine.ChainForTests);
        }

        [Test]
        public void ASizeChangeShapesNothing()
        {
            LetteraWorldText text = Make(Sentence);
            long shapes = LetteraCaches.Stats.ShapeCalls;
            text.FontSize = 40;
            text.ForceRebuild();
            Assert.AreEqual(shapes, LetteraCaches.Stats.ShapeCalls);
        }

        [Test]
        public void AHitAllocatesNothing()
        {
            LetteraWorldText text = Make(Sentence);
            for (int i = 0; i < 3; i++)
            {
                text.SetDirty(DirtyLevel.Shape);
                text.ForceRebuild();
            }
            // Only this thread: the editor's own threads allocate at times; the all-thread figure is for the message.
            using var recorder = Unity.Profiling.ProfilerRecorder.StartNew(Unity.Profiling.ProfilerCategory.Memory, "GC Allocated In Frame", 1, Unity.Profiling.ProfilerRecorderOptions.Default | Unity.Profiling.ProfilerRecorderOptions.CollectOnlyOnCurrentThread);
            using var everyThread = Unity.Profiling.ProfilerRecorder.StartNew(Unity.Profiling.ProfilerCategory.Memory, "GC Allocated In Frame");
            long control = recorder.CurrentValue;
            s_Control = new byte[64];
            Assert.GreaterOrEqual(recorder.CurrentValue - control, 32, "the allocation counter does not work here");
            long quietest = long.MaxValue, anyThread = long.MaxValue;
            for (int window = 0; window < 5; window++)
            {
                long before = recorder.CurrentValue, beforeAll = everyThread.CurrentValue;
                for (int i = 0; i < 100; i++)
                {
                    text.SetDirty(DirtyLevel.Shape);
                    text.ForceRebuild();
                }
                quietest = System.Math.Min(quietest, recorder.CurrentValue - before); anyThread = System.Math.Min(anyThread, everyThread.CurrentValue - beforeAll);
            }
            Assert.Less(quietest, 200, $"{quietest} bytes on this thread ({anyThread} on all threads)");
        }

        static byte[] s_Control;

        [Test]
        public void StoringNewParagraphsAllocatesNothing()
        {
            LetteraWorldText text = Make("Score: 00000");
            var strings = new string[600];
            for (int i = 0; i < strings.Length; i++) strings[i] = "Score: " + (10000 + i).ToString("00000");
            for (int i = 0; i < 100; i++)
            {
                text.SetText(strings[i]);
                text.ForceRebuild();
            }
            // Only this thread: the editor's own threads allocate at times; the all-thread figure is for the message.
            using var recorder = Unity.Profiling.ProfilerRecorder.StartNew(Unity.Profiling.ProfilerCategory.Memory, "GC Allocated In Frame", 1, Unity.Profiling.ProfilerRecorderOptions.Default | Unity.Profiling.ProfilerRecorderOptions.CollectOnlyOnCurrentThread);
            using var everyThread = Unity.Profiling.ProfilerRecorder.StartNew(Unity.Profiling.ProfilerCategory.Memory, "GC Allocated In Frame");
            long control = recorder.CurrentValue;
            s_Control = new byte[64];
            Assert.GreaterOrEqual(recorder.CurrentValue - control, 32, "the allocation counter does not work here");
            long misses = LetteraCaches.Stats.Misses;
            long quietest = long.MaxValue, anyThread = long.MaxValue;
            for (int window = 0; window < 5; window++)
            {
                long before = recorder.CurrentValue, beforeAll = everyThread.CurrentValue;
                for (int i = 0; i < 100; i++)
                {
                    text.SetText(strings[100 + window * 100 + i]);
                    text.ForceRebuild();
                }
                quietest = System.Math.Min(quietest, recorder.CurrentValue - before); anyThread = System.Math.Min(anyThread, everyThread.CurrentValue - beforeAll);
            }
            Assert.AreEqual(500, LetteraCaches.Stats.Misses - misses, "each was a new paragraph, and was stored");
            // It was about 2 KB for each, in arrays rented from a pool that could not hold them all.
            Assert.Less(quietest, 200, $"{quietest} bytes on this thread ({anyThread} on all threads)");
            long hits = LetteraCaches.Stats.Hits;
            text.SetText(strings[333]);
            text.ForceRebuild();
            Assert.AreEqual(hits + 1, LetteraCaches.Stats.Hits, "and each can be found");
        }

        [Test]
        public void EntriesGoingAndComingKeepEveryOtherFindable()
        {
            LetteraCaches.ParagraphCacheBytes = 64 * 1024;
            LetteraCaches.Clear();
            for (int i = 0; i < 600; i++) Make("Some words that differ here " + i);
            Assert.Greater(LetteraCaches.Stats.Evictions, 0);
            Assert.LessOrEqual(LetteraCaches.Stats.Bytes, 64 * 1024);
            int kept = TextEngine.ParagraphCount;
            Assert.Greater(kept, 10);
            long hits = LetteraCaches.Stats.Hits;
            for (int i = 600 - kept; i < 600; i++) Make("Some words that differ here " + i);
            Assert.AreEqual(hits + kept, LetteraCaches.Stats.Hits, "every entry still held is found, after many were taken from among them");
            long misses = LetteraCaches.Stats.Misses;
            Make("Some words that differ here 0");
            Assert.AreEqual(misses + 1, LetteraCaches.Stats.Misses, "and one taken out is not");
            LetteraCaches.Clear();
            Assert.AreEqual(0, TextEngine.ParagraphCount);
            Assert.AreEqual(0, LetteraCaches.Stats.Bytes);
        }

        [Test]
        public void AParagraphOfThousandsOfCharactersComesBackTheSame()
        {
            string body = new string('x', 6000) + " " + new string('y', 6000) + " مرحبا بالعالم";
            Snapshot wanted;
            LetteraCaches.Enabled = false;
            wanted = Take(Make(body));
            LetteraCaches.Enabled = true;
            Take(Make(body));
            long hits = LetteraCaches.Stats.Hits;
            AssertSame(wanted, Take(Make(body)), "again");
            Assert.AreEqual(hits + 1, LetteraCaches.Stats.Hits);
        }

        // A stored run holds every field of a run but its font, language and features, which come back from elsewhere.
        [Test]
        public void AStoredRunKeepsEveryFieldOfARun()
        {
            const System.Reflection.BindingFlags Fields = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;
            var run = new List<string>();
            foreach (var field in typeof(TextEngine).GetNestedType("Run", System.Reflection.BindingFlags.NonPublic).GetFields(Fields))
                if (field.Name != "Language" && field.Name != "Features")
                    run.Add(field.Name);
            var stored = new List<string>();
            foreach (var field in typeof(TextEngine).GetNestedType("StoredRun", System.Reflection.BindingFlags.NonPublic).GetFields(Fields)) stored.Add(field.Name);
            run.Sort();
            stored.Sort();
            CollectionAssert.AreEqual(run, stored);
        }
    }
}

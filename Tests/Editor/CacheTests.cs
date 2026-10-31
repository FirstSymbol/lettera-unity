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
            using var recorder = Unity.Profiling.ProfilerRecorder.StartNew(Unity.Profiling.ProfilerCategory.Memory, "GC Allocated In Frame");
            long control = recorder.CurrentValue;
            s_Control = new byte[64];
            Assert.GreaterOrEqual(recorder.CurrentValue - control, 32, "the allocation counter does not work here");
            long quietest = long.MaxValue;
            for (int window = 0; window < 5; window++)
            {
                long before = recorder.CurrentValue;
                for (int i = 0; i < 100; i++)
                {
                    text.SetDirty(DirtyLevel.Shape);
                    text.ForceRebuild();
                }
                quietest = System.Math.Min(quietest, recorder.CurrentValue - before);
            }
            Assert.Less(quietest, 200);
        }

        static byte[] s_Control;
    }
}

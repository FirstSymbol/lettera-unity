using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace FirstSymbol.Lettera.Editor.Tests
{
    /// <summary>Batched emoji, what a page's system means, what a system asks, and where a selection stands.</summary>
    public class SmallerPromisesTests : EditingFixture
    {
        // Paints each cluster one flat color, from its text, so that any mix-up of places shows.
        class OneByOne : IEmojiRenderer
        {
            public int Reads;
            public float Measure(string cluster, int pixels) => pixels;

            public void Render(string cluster, int pixels, int width, int height, int baselineY, byte[] output)
            {
                Reads++;
                Fill(output, 0, width, width, height, cluster);
            }

            protected static void Fill(byte[] output, int fromX, int canvasWidth, int width, int height, string cluster, int fromY = 0)
            {
                byte value = (byte)(cluster.GetHashCode() | 1);
                for (int y = 0; y < height; y++)
                    for (int x = 0; x < width; x++)
                    {
                        int at = ((fromY + y) * canvasWidth + fromX + x) * 4;
                        output[at] = value;
                        output[at + 1] = (byte)(255 - value);
                        output[at + 2] = 7;
                        output[at + 3] = 255;
                    }
            }
        }

        sealed class Batched : OneByOne, IEmojiBatchRenderer
        {
            public int Batches, Drawn;

            public void RenderBatch(string[] clusters, int start, int count, int[] positions, int pixels, int canvasWidth, int canvasHeight, byte[] output)
            {
                Batches++;
                Drawn += count;
                int ascent = Mathf.CeilToInt(BrowserEmoji.AscentEm * pixels);
                int height = ascent + Mathf.CeilToInt(BrowserEmoji.DescentEm * pixels);
                for (int i = 0; i < count; i++) Fill(output, positions[i * 2], canvasWidth, pixels, height, clusters[start + i], positions[i * 2 + 1] - ascent);
            }
        }

        static uint[] Clusters(int count)
        {
            var glyphs = new uint[count];
            for (int i = 0; i < count; i++) glyphs[i] = BrowserEmoji.GlyphFor(new[] { 0x1F300 + i }, out _);
            return glyphs;
        }

        static void WithRenderer(IEmojiRenderer renderer, System.Action body)
        {
            IEmojiRenderer was = BrowserEmoji.Renderer;
            BrowserEmoji.Reset();
            BrowserEmoji.Renderer = renderer;
            try
            {
                body();
            }
            finally
            {
                BrowserEmoji.Reset();
                BrowserEmoji.Renderer = was;
            }
        }

        [Test]
        public void SixtyFourNewEmojiReadTheCanvasOnce()
        {
            var renderer = new Batched();
            WithRenderer(renderer, () =>
            {
                FontInstance instance = BrowserEmoji.Face.Regular;
                foreach (uint glyph in Clusters(64)) Assert.AreEqual(AtlasGlyph.Color, GlyphAtlas.Get(instance, glyph, 64).Kind, "the rect is valid at once");
                Assert.AreEqual(0, renderer.Batches, "no pixels yet");
                GlyphAtlas.Flush();
                Assert.AreEqual(1, renderer.Batches);
                Assert.AreEqual(64, renderer.Drawn);
                GlyphAtlas.Flush();
                Assert.AreEqual(1, renderer.Batches, "nothing left to do");
            });
        }

        [Test]
        public void ARendererWithNoBatchStillDrawsEachOne()
        {
            var renderer = new OneByOne();
            WithRenderer(renderer, () =>
            {
                foreach (uint glyph in Clusters(8)) GlyphAtlas.Get(BrowserEmoji.Face.Regular, glyph, 64);
                GlyphAtlas.Flush();
                Assert.AreEqual(8, renderer.Reads);
            });
        }

        [Test]
        public void TheBatchPutsEachClusterWhereItsGlyphSays()
        {
            byte[] Snapshot(IEmojiRenderer renderer, uint[][] holder)
            {
                byte[] pixels = null;
                WithRenderer(renderer, () =>
                {
                    GlyphAtlas.Clear();
                    uint[] glyphs = Clusters(12);
                    foreach (uint glyph in glyphs) GlyphAtlas.Get(BrowserEmoji.Face.Regular, glyph, 64);
                    GlyphAtlas.Flush();
                    pixels = GlyphAtlas.ColorPixelsForTests(0);
                });
                return pixels;
            }
            byte[] one = Snapshot(new OneByOne(), null), many = Snapshot(new Batched(), null);
            CollectionAssert.AreEqual(one, many);
        }

        [Test]
        public void AClearTakesWhatWasStillToBeDrawn()
        {
            var renderer = new Batched();
            WithRenderer(renderer, () =>
            {
                foreach (uint glyph in Clusters(4)) GlyphAtlas.Get(BrowserEmoji.Face.Regular, glyph, 64);
                GlyphAtlas.Clear();
                GlyphAtlas.Flush();
                Assert.AreEqual(0, renderer.Batches);
            });
        }

        [TestCase("iPhone", 5, LetteraPlatformFamily.Apple)]
        [TestCase("MacIntel", 5, LetteraPlatformFamily.Apple)]
        [TestCase("MacIntel", 0, LetteraPlatformFamily.Apple)]
        [TestCase("macOS", 0, LetteraPlatformFamily.Apple)]
        [TestCase("Win32", 0, LetteraPlatformFamily.Pc)]
        [TestCase("Windows", 10, LetteraPlatformFamily.Pc)]
        [TestCase("Linux armv8l", 5, LetteraPlatformFamily.Pc)]
        [TestCase("Android", 5, LetteraPlatformFamily.Pc)]
        [TestCase("", 0, LetteraPlatformFamily.Pc)]
        public void APageSaysWhichKeys(string platform, int touch, LetteraPlatformFamily family)
        {
            Assert.AreEqual(family, LetteraKeyTable.FamilyFromPage(platform, touch));
        }

        [Test]
        public void AScriptsValueWinsOverTheSystem()
        {
            LetteraAccessibility.ResetForTests();
            try
            {
                LetteraAccessibility.FromSystem(true, true);
                Assert.IsTrue(LetteraAccessibility.ReducedMotion);
                Assert.IsTrue(LetteraAccessibility.SteadyCaret);
                LetteraAccessibility.ReducedMotion = false;
                LetteraAccessibility.FromSystem(true, false);
                Assert.IsFalse(LetteraAccessibility.ReducedMotion, "the script said so");
                Assert.IsFalse(LetteraAccessibility.SteadyCaret, "the system still decides this one");
            }
            finally
            {
                LetteraAccessibility.ResetForTests();
            }
        }

        [Test]
        public void PublicGeometryAgreesWithWhatItWraps()
        {
            LetteraEditable field = Field("hello world");
            LetteraSelectable selectable = field.Selectable;
            selectable.PlaceCaret(3);
            Assert.IsTrue(selectable.CaretOnScreen(out Vector2 caret));
            Assert.IsTrue(selectable.TryCaretOnScreen(selectable.EventCameraGuess(), out Rect box));
            Assert.AreEqual(box.center.x, caret.x, 0.01f);
            Assert.AreEqual(box.yMin, caret.y, 0.01f);
            Assert.IsFalse(selectable.SelectionOnScreen(out _), "a caret has no selection");
            Assert.IsFalse(selectable.HandlesOnScreen(out _, out _));
            selectable.Select(2, 7);
            Assert.IsTrue(selectable.SelectionOnScreen(out Rect selection));
            Assert.IsTrue(selectable.TrySelectionOnScreen(selectable.EventCameraGuess(), out Rect same));
            Assert.AreEqual(same, selection);
            Assert.IsTrue(selectable.HandlesOnScreen(out Vector2 start, out Vector2 end));
            Assert.Less(start.x, end.x);
            Assert.IsTrue(selectable.LineOnScreen(0, out _));
            Assert.IsFalse(selectable.LineOnScreen(99, out _));
            Assert.IsFalse(selectable.IsMenuShown);
        }
    }
}

using NUnit.Framework;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.UI;

namespace FirstSymbol.Lettera.Editor.Tests
{
    /// <summary>What a text holds per codepoint lives in native memory, and what thousands of labels each make stays small.</summary>
    public class NativeBufferTests : TextFixture
    {
        bool m_Saved, m_SavedLater;

        [SetUp]
        public void SaveSwitch()
        {
            m_Saved = LetteraLongText.VisibleLinesOnly;
            m_SavedLater = LetteraLongText.FinishLater;
            LetteraLongText.VisibleLinesOnly = true;
            LetteraLongText.FinishLater = true;
        }

        [TearDown]
        public void RestoreSwitch()
        {
            LetteraLongText.VisibleLinesOnly = m_Saved;
            LetteraLongText.FinishLater = m_SavedLater;
        }

        // A world canvas, a 800 by 600 viewport that clips, and a text in it moved up by down, so the viewport shows that far into it.
        LetteraText InViewport(string source, float down)
        {
            var canvas = new GameObject("Canvas", typeof(RectTransform), typeof(Canvas));
            Created.Add(canvas);
            canvas.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
            ((RectTransform)canvas.transform).sizeDelta = new Vector2(4000, 4000);
            var view = new GameObject("Viewport", typeof(RectTransform), typeof(RectMask2D));
            view.transform.SetParent(canvas.transform, false);
            ((RectTransform)view.transform).sizeDelta = new Vector2(800, 600);
            var go = new GameObject("Text", typeof(RectTransform));
            go.transform.SetParent(view.transform, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 1);
            rect.sizeDelta = new Vector2(800, 100000);
            rect.anchoredPosition = new Vector2(0, down);
            var text = go.AddComponent<LetteraText>();
            text.Font = Latin;
            text.FontSize = 14;
            text.Text = source;
            Canvas.ForceUpdateCanvases();
            text.ForceRebuild();
            return text;
        }

        static TextEngine EngineOf(Component text) => ((ITextComponent)text).SharedEngine;

        [Test]
        public void SettingALongTextKeepsItsCodepointsAndShapingOffTheManagedHeap()
        {
            string source = LongTextTests.Page(512 * 1024);
            // A view a third of the way in: the band's shaping reaches that far into the text.
            LetteraText text = InViewport("", 30000);
            using var recorder = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame");
            long control = recorder.CurrentValue;
            System.GC.KeepAlive(new byte[32]);
            Assert.GreaterOrEqual(recorder.CurrentValue - control, 32, "the allocation counter does not work here");

            // What the editor makes of a changed serialized field is not the engine's, so only the rebuild is counted.
            text.Text = source;
            long before = recorder.CurrentValue;
            text.ForceRebuild();
            long allocated = recorder.CurrentValue - before;
            TextEngine engine = EngineOf(text);
            Assert.Greater(engine.PendingParagraphs, 0, "most of it waits");
            Assert.Greater(engine.BandFirstLine, 100, "the view is deep in the text");
            // Its codepoints at 4 bytes each and shaping's 17 a codepoint up to the band were 3.8 MB here as managed arrays. Lines and paragraphs stay.
            Assert.Less(allocated, source.Length / 2, "allocated " + allocated + " bytes for " + source.Length + " characters");

            LetteraText.FinishSomeNow();
            while (engine.PendingParagraphs > 0) LetteraText.FinishSomeNow();
            text.ForceRebuild();
            LetteraLongText.FinishLater = false;
            LetteraText whole = InViewport(source, 30000);
            Assert.AreEqual(whole.Lines.Count, text.Lines.Count, "finished, it has the lines a whole build has");
            Assert.AreEqual(whole.preferredHeight, text.preferredHeight, 0.01f);
            Assert.AreEqual(whole.GlyphCount, text.GlyphCount);
        }

        [Test]
        public void ATextSetAgainShorterAndLongerReadsWhatItWasGiven()
        {
            LetteraText text = InViewport(LongTextTests.Page(64 * 1024), 0);
            text.Text = "Short, then long again";
            text.ForceRebuild();
            Assert.AreEqual("Short, then long again", text.RenderedSourceText);
            Assert.AreEqual(1, text.Lines.Count);
            string again = LongTextTests.Page(80 * 1024);
            text.Text = again;
            text.ForceRebuild();
            text.Text = again + " and one more word";
            text.ForceRebuild();
            LetteraLongText.FinishLater = false;
            LetteraText whole = InViewport(again + " and one more word", 0);
            Assert.AreEqual(whole.Lines.Count, text.Lines.Count);
            Assert.AreEqual(whole.GlyphCount, text.GlyphCount);
        }

        [Test]
        public void MeasuringGivesItsBuffersBackAndMeasuresTheSameEachTime()
        {
            LetteraText text = InViewport("", 0);
            Vector2 first = TextEngine.MeasureWith(text, ((ITextHost)text).Data, LongTextTests.Page(20000), 14, 800, 100000, true, false, 0, 0);
            for (int i = 0; i < 20; i++)
                Assert.AreEqual(first, TextEngine.MeasureWith(text, ((ITextHost)text).Data, LongTextTests.Page(20000), 14, 800, 100000, true, false, 0, 0));
            Assert.Greater(first.y, 100);
        }

        // Thousands of world labels are often made at once: room for sixteen glyphs each was garbage enough to start collections in their first frame.
        [Test]
        public void AWorldLabelsFirstMeshHasRoomForItsGlyphsAndACanvasLabelForSixteen()
        {
            LetteraWorldText world = Make("Label 7");
            Assert.Greater(EngineOf(world).VertexCount, 0);
            Assert.LessOrEqual(EngineOf(world).Vertices.Length, 7 * 4);
            LetteraText label = InViewport("Label 7", 0);
            Assert.AreEqual(64, EngineOf(label).Vertices.Length);
        }
    }
}

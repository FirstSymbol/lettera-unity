using System.Collections.Generic;
using System.Text;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;

namespace FirstSymbol.Lettera.Editor.Tests
{
    /// <summary>A long text places and draws only the lines near what shows, and nothing a caller reads differs from placing all of it.</summary>
    public class LongTextTests : TextFixture
    {
        bool m_Saved, m_SavedLater;
        float m_SavedBudget;

        [SetUp]
        public void SaveSwitch()
        {
            m_Saved = LetteraLongText.VisibleLinesOnly;
            m_SavedLater = LetteraLongText.FinishLater;
            m_SavedBudget = LetteraLongText.FinishMillisecondsPerFrame;
        }

        [TearDown]
        public void RestoreSwitch()
        {
            LetteraLongText.VisibleLinesOnly = m_Saved;
            LetteraLongText.FinishLater = m_SavedLater;
            LetteraLongText.FinishMillisecondsPerFrame = m_SavedBudget;
        }

        internal static string Page(int bytes, string at = null, int where = -1)
        {
            const string Words = "The quick brown fox jumps over a lazy dog and then runs back home again ";
            var page = new StringBuilder(bytes + 80);
            int i = 0;
            while (page.Length < bytes)
            {
                if (where >= 0 && page.Length >= where && at != null)
                {
                    page.Append(at).Append(' ');
                    at = null;
                }
                page.Append(Words, i % 20, 12 + i * 7 % 29);
                i++;
                page.Append(i % 8 == 0 ? '\n' : ' ');
            }
            return page.ToString();
        }

        // A world canvas, a 800 by 600 viewport that clips, and a text in it whose top meets the viewport's.
        LetteraText Make(string source, out RectTransform viewport, float down = 0)
        {
            var canvas = new GameObject("Canvas", typeof(RectTransform), typeof(Canvas));
            Track(canvas);
            canvas.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
            ((RectTransform)canvas.transform).sizeDelta = new Vector2(4000, 4000);
            var view = new GameObject("Viewport", typeof(RectTransform), typeof(RectMask2D));
            view.transform.SetParent(canvas.transform, false);
            viewport = (RectTransform)view.transform;
            viewport.sizeDelta = new Vector2(800, 600);
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

        void Track(GameObject go) => Created.Add(go);

        static TextEngine EngineOf(LetteraText text) => ((ITextComponent)text).SharedEngine;

        [Test]
        public void ALongTextPlacesOnlyTheLinesNearWhatShowsAndEverythingElseStaysExact()
        {
            string source = Page(100 * 1024);
            LetteraLongText.VisibleLinesOnly = false;
            LetteraText whole = Make(source, out _);
            TextEngine all = EngineOf(whole);
            LetteraLongText.VisibleLinesOnly = true;
            LetteraText part = Make(source, out _);
            TextEngine some = EngineOf(part);

            Assert.IsFalse(all.IsBanded);
            Assert.IsTrue(some.IsBanded, "a 100 KB text in a 600 high viewport places part of its lines");
            Assert.Less(some.VertexCount * 10, all.VertexCount, "it draws a small part of the glyphs");
            Assert.AreEqual(0, part.PartCount, "what it draws fits one renderer");

            Assert.AreEqual(whole.Lines.Count, part.Lines.Count, "every line is still broken");
            for (int l = 0; l < whole.Lines.Count; l += 37)
            {
                Assert.AreEqual(whole.Lines[l].Start, part.Lines[l].Start);
                Assert.AreEqual(whole.Lines[l].Baseline, part.Lines[l].Baseline, 0.001f);
                Assert.AreEqual(whole.Lines[l].X, part.Lines[l].X, 0.001f);
            }
            Assert.AreEqual(whole.preferredHeight, part.preferredHeight, 0.01f, "preferred height is the whole text's");
            Assert.AreEqual(whole.LayoutBounds, part.LayoutBounds);

            // Caret, hit and selection far below what shows read the same as with every glyph placed.
            int far = source.Length - 300;
            Assert.IsTrue(whole.TryGetCaretRect(far, LetteraCaretBias.Forward, out Rect a));
            Assert.IsTrue(part.TryGetCaretRect(far, LetteraCaretBias.Forward, out Rect b));
            Assert.AreEqual(a, b, "a caret off screen");
            Vector2 point = new Vector2(a.x + 3, a.center.y);
            Assert.AreEqual(whole.HitTestCaret(point, out _), part.HitTestCaret(point, out _), "a hit off screen");

            var wholeRects = new List<Rect>();
            var partRects = new List<Rect>();
            whole.GetRangeRects(far - 2000, far, wholeRects);
            part.GetRangeRects(far - 2000, far, partRects);
            Assert.Greater(wholeRects.Count, 3);
            Near(Bounds(wholeRects), Bounds(partRects), "a selection off screen covers the same");
            Near(wholeRects[0], partRects[0], "its first line, which starts inside a line");
            Near(wholeRects[wholeRects.Count - 1], partRects[partRects.Count - 1], "its last line");
            Assert.AreEqual(source, part.Text, "all of the text is there to copy");
        }

        static void Near(Rect a, Rect b, string what)
        {
            Assert.AreEqual(a.xMin, b.xMin, 0.01f, what);
            Assert.AreEqual(a.xMax, b.xMax, 0.01f, what);
            Assert.AreEqual(a.yMin, b.yMin, 0.01f, what);
            Assert.AreEqual(a.yMax, b.yMax, 0.01f, what);
        }

        static Rect Bounds(List<Rect> rects)
        {
            Rect r = rects[0];
            foreach (Rect each in rects) r = Rect.MinMaxRect(Mathf.Min(r.xMin, each.xMin), Mathf.Min(r.yMin, each.yMin), Mathf.Max(r.xMax, each.xMax), Mathf.Max(r.yMax, each.yMax));
            return r;
        }

        [Test]
        public void ScrollingPlacesTheLinesThatComeIntoViewAndALinkThereAnswers()
        {
            LetteraLongText.VisibleLinesOnly = true;
            const string Link = "<link=https://example.com>far away</link>";
            string source = Page(100 * 1024, Link, 90 * 1024);
            LetteraText text = Make(source, out RectTransform viewport);
            TextEngine engine = EngineOf(text);
            Assert.IsTrue(engine.IsBanded);
            int linkStart = text.RenderedIndexFromSource(source.IndexOf(Link, System.StringComparison.Ordinal) + Link.IndexOf('>') + 1);
            var rects = new List<Rect>();
            text.GetRangeRects(linkStart, linkStart + 8, rects);
            Assert.AreEqual(1, rects.Count);
            int lineOfLink = text.GetLineIndex(linkStart, LetteraCaretBias.Forward);
            Assert.Greater(lineOfLink, engine.BandEndLine, "the link starts far below the placed lines");

            // The text moves up until the link's line stands in the viewport.
            var rect = (RectTransform)text.transform;
            rect.anchoredPosition = new Vector2(0, -rects[0].center.y - 300);
            LetteraText.CheckBandsNow();
            Canvas.ForceUpdateCanvases();
            text.ForceRebuild();
            Assert.LessOrEqual(engine.BandFirstLine, lineOfLink);
            Assert.Greater(engine.BandEndLine, lineOfLink, "the lines that came into view are placed");
            Assert.AreNotEqual(0, engine.HitTestActive(rects[0].center, 0), "the link answers where it now shows");
            Assert.Greater(engine.VertexCount, 0);

            // Moving back draws the top again.
            rect.anchoredPosition = Vector2.zero;
            LetteraText.CheckBandsNow();
            Canvas.ForceUpdateCanvases();
            text.ForceRebuild();
            Assert.AreEqual(0, engine.BandFirstLine);
        }

        [Test]
        public void AShortTextAndAWorldCanvasWithNoMaskPlaceEverything()
        {
            LetteraLongText.VisibleLinesOnly = true;
            LetteraText text = Make(Page(2 * 1024), out _);
            Assert.IsFalse(EngineOf(text).IsBanded, "a short text is placed whole");

            var canvas = new GameObject("World Canvas", typeof(RectTransform), typeof(Canvas));
            Track(canvas);
            canvas.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
            var go = new GameObject("Text", typeof(RectTransform));
            go.transform.SetParent(canvas.transform, false);
            ((RectTransform)go.transform).sizeDelta = new Vector2(800, 100000);
            var free = go.AddComponent<LetteraText>();
            free.Font = Latin;
            free.FontSize = 14;
            free.Text = Page(100 * 1024);
            Canvas.ForceUpdateCanvases();
            free.ForceRebuild();
            Assert.IsFalse(EngineOf(free).IsBanded, "a world canvas shows anywhere, so all of it is placed");
        }

        // Every line of an edited text against the same text built from nothing.
        void SameAsFresh(LetteraWorldText edited, string what)
        {
            LetteraWorldText fresh = Make(edited.Text, 800, 100000, Latin, 14);
            Assert.AreEqual(fresh.Lines.Count, edited.Lines.Count, what + ": lines");
            for (int l = 0; l < fresh.Lines.Count; l++)
            {
                LetteraLine a = fresh.Lines[l], b = edited.Lines[l];
                Assert.AreEqual(a.Start, b.Start, what + ": line " + l);
                Assert.AreEqual(a.End, b.End, what + ": line " + l);
                Assert.AreEqual(a.Width, b.Width, 0.001f, what + ": width of line " + l);
                Assert.AreEqual(a.Baseline, b.Baseline, 0.001f, what + ": baseline of line " + l);
            }
            Assert.AreEqual(fresh.GlyphCount, edited.GlyphCount, what + ": glyphs");
            Assert.AreEqual(fresh.LayoutBounds, edited.LayoutBounds, what);
            for (int i = 0; i < fresh.GlyphCount; i += 97)
            {
                LetteraGlyphInfo a = fresh.GetGlyph(i), b = edited.GetGlyph(i);
                Assert.AreEqual(a.Glyph, b.Glyph, what + ": glyph " + i);
                Assert.AreEqual(a.X, b.X, 0.001f, what + ": glyph " + i);
                Assert.AreEqual(a.Y, b.Y, 0.001f, what + ": glyph " + i);
            }
            Object.DestroyImmediate(fresh.gameObject);
        }

        [Test]
        public void AnEditRedoesOnlyFromItsParagraphAndEndsAsAFreshBuildWould()
        {
            string page = Page(40 * 1024);
            LetteraWorldText text = Make(page, 800, 100000, Latin, 14);
            TextEngine engine = text.Engine;
            int shapes = engine.ResumedShapes, layouts = engine.ResumedLayouts;

            text.Text = page + "x";
            text.ForceRebuild();
            Assert.AreEqual(shapes + 1, engine.ResumedShapes, "typing at the end resumes shaping");
            Assert.AreEqual(layouts + 1, engine.ResumedLayouts, "and line breaking");
            SameAsFresh(text, "typed at the end");

            int middle = page.IndexOf('\n', page.Length / 2);
            string edits = text.Text.Insert(middle - 5, "inserted words ");
            text.Text = edits;
            text.ForceRebuild();
            SameAsFresh(text, "typed in the middle");

            Assert.AreEqual('\n', edits[middle + 15]);
            text.Text = edits.Remove(middle + 15, 1);
            text.ForceRebuild();
            SameAsFresh(text, "a line break removed, two paragraphs joined");

            text.Text = text.Text.Insert(200, "\n");
            text.ForceRebuild();
            SameAsFresh(text, "a paragraph split near the start");

            text.Text = text.Text.Insert(3000, "\U0001F600 שלום ");
            text.ForceRebuild();
            SameAsFresh(text, "a pair of surrogates and right-to-left letters");

            text.Text = text.Text.Remove(text.Text.Length - 1000, 1000);
            text.ForceRebuild();
            SameAsFresh(text, "the end cut");
            Assert.Greater(engine.ResumedShapes, shapes + 4, "each of them resumed");

            text.Text = "<b>" + text.Text;
            text.ForceRebuild();
            SameAsFresh(text, "markup comes in and all of it is done again");
        }

        // ------------------------------------------------------------- paragraphs left for later

        LetteraText MakeWhole(string source, float down = 0)
        {
            LetteraLongText.FinishLater = false;
            LetteraText whole = Make(source, out _, down);
            LetteraLongText.FinishLater = true;
            return whole;
        }

        // Finishes a text a slice at a time, as frames of a player would, and gives the longest slice in milliseconds.
        static double FinishBySlices(LetteraText text, out int slices)
        {
            TextEngine engine = EngineOf(text);
            var clock = new System.Diagnostics.Stopwatch();
            double longest = 0;
            slices = 0;
            while (engine.PendingParagraphs > 0 && slices < 100000)
            {
                clock.Restart();
                LetteraText.FinishSomeNow();
                longest = System.Math.Max(longest, clock.Elapsed.TotalMilliseconds);
                slices++;
            }
            text.ForceRebuild();
            return longest;
        }

        static void SameAsWhole(LetteraText whole, LetteraText part, string what)
        {
            IReadOnlyList<LetteraLine> a = whole.Lines, b = part.Lines;
            Assert.AreEqual(a.Count, b.Count, what + ": lines");
            for (int l = 0; l < a.Count; l++)
            {
                Assert.AreEqual(a[l].Start, b[l].Start, what + ": start of line " + l);
                Assert.AreEqual(a[l].End, b[l].End, what + ": end of line " + l);
                Assert.AreEqual(a[l].Width, b[l].Width, 0.001f, what + ": width of line " + l);
                Assert.AreEqual(a[l].Baseline, b[l].Baseline, 0.001f, what + ": baseline of line " + l);
                Assert.AreEqual(a[l].X, b[l].X, 0.001f, what + ": x of line " + l);
            }
            Assert.AreEqual(whole.preferredHeight, part.preferredHeight, 0.01f, what + ": preferred height");
            Assert.AreEqual(whole.LayoutBounds, part.LayoutBounds, what + ": layout bounds");
            Assert.AreEqual(whole.GlyphCount, part.GlyphCount, what + ": placed glyphs");
            for (int i = 0; i < whole.GlyphCount; i++)
            {
                LetteraGlyphInfo g = whole.GetGlyph(i), h = part.GetGlyph(i);
                Assert.AreEqual(g.Glyph, h.Glyph, what + ": glyph " + i);
                Assert.AreEqual(g.Cluster, h.Cluster, what + ": cluster of glyph " + i);
                Assert.AreEqual(g.X, h.X, 0.001f, what + ": x of glyph " + i);
                Assert.AreEqual(g.Y, h.Y, 0.001f, what + ": y of glyph " + i);
            }
        }

        [Test]
        public void ALongTextShapesWhatShowsAtOnceAndFinishesInSlicesToWhatAWholeBuildMakes()
        {
            string source = Page(100 * 1024);
            LetteraLongText.VisibleLinesOnly = true;
            LetteraText whole = MakeWhole(source);
            LetteraText part = Make(source, out _);
            TextEngine engine = EngineOf(part), all = EngineOf(whole);
            Assert.AreEqual(0, all.PendingParagraphs);
            Assert.Greater(engine.PendingParagraphs, 100, "most paragraphs wait");
            Assert.IsTrue(part.IsFinishing);
            Assert.Less(engine.ShapedGlyphCount * 10, all.ShapedGlyphCount, "a small part is shaped when it is set");
            Assert.Greater(engine.VertexCount, 0, "what shows is drawn");
            Assert.AreEqual(all.VertexCount, engine.VertexCount, "the same lines are drawn as with every paragraph done");

            LetteraLongText.FinishMillisecondsPerFrame = 1;
            double longest = FinishBySlices(part, out int slices);
            Assert.AreEqual(0, engine.PendingParagraphs);
            Assert.IsFalse(part.IsFinishing);
            Assert.Greater(slices, 3, "finishing took several frames");
            Assert.Less(longest, 30, "no slice took long");
            SameAsWhole(whole, part, "finished in slices");
        }

        [Test]
        public void LinesAndCaretsAskedMidwayFinishAndAreExact()
        {
            string source = Page(100 * 1024);
            LetteraLongText.VisibleLinesOnly = true;
            LetteraText whole = MakeWhole(source);
            LetteraText part = Make(source, out _);
            TextEngine engine = EngineOf(part);
            Assert.Greater(engine.PendingParagraphs, 0);
            float estimate = part.preferredHeight;
            Assert.Greater(engine.PendingParagraphs, 0, "a layout's question is answered by the estimate");
            Assert.AreEqual(whole.preferredHeight, estimate, whole.preferredHeight * 0.25f, "the estimate is near");
            Assert.AreEqual(whole.Lines.Count, part.Lines.Count, "Lines asked midway is exact");
            Assert.AreEqual(0, engine.PendingParagraphs, "asking finished everything");
            int far = source.Length - 300;
            Assert.IsTrue(whole.TryGetCaretRect(far, LetteraCaretBias.Forward, out Rect a));
            Assert.IsTrue(part.TryGetCaretRect(far, LetteraCaretBias.Forward, out Rect b));
            Assert.AreEqual(a, b, "a caret far below");
            part.ForceRebuild();
            SameAsWhole(whole, part, "finished by a query");

            LetteraText other = Make(source, out _);
            Assert.Greater(EngineOf(other).PendingParagraphs, 0);
            var point = new Vector2(100, -40000);
            Assert.AreEqual(whole.HitTestCaret(point, out _), other.HitTestCaret(point, out _), "a hit far below");
            Assert.AreEqual(0, EngineOf(other).PendingParagraphs);
        }

        [Test]
        public void AViewDeepInALongTextShowsAtOnceAndLandsWhereAWholeBuildPutsIt()
        {
            string source = Page(200 * 1024);
            LetteraLongText.VisibleLinesOnly = true;
            const float Down = 20000;
            LetteraText whole = MakeWhole(source, Down);
            LetteraText part = Make(source, out _, Down);
            TextEngine engine = EngineOf(part);
            Assert.Greater(engine.PendingParagraphs, 0);
            Assert.Greater(engine.VertexCount, 0, "lines deep in the text are drawn when it is set");
            Assert.Greater(engine.BandFirstLine, 0);
            FinishBySlices(part, out _);
            SameAsWhole(whole, part, "a deep view, finished");
        }

        [Test]
        public void TypingWhileFinishingKeepsWhatWasDoneAndEndsAsAWholeBuild()
        {
            string source = Page(100 * 1024);
            LetteraLongText.VisibleLinesOnly = true;
            LetteraText part = Make(source, out _);
            TextEngine engine = EngineOf(part);
            LetteraText.FinishSomeNow();
            int waiting = engine.PendingParagraphs;
            Assert.Greater(waiting, 0);
            part.Text = source + "x";
            part.ForceRebuild();
            Assert.AreEqual(waiting, engine.PendingParagraphs, "typing at the end redoes only the last paragraph");
            part.Text = source.Insert(500, "inserted ");
            part.ForceRebuild();
            Assert.Greater(engine.PendingParagraphs, 0);
            FinishBySlices(part, out _);
            SameAsWhole(MakeWhole(part.Text), part, "typed while finishing");
        }

        // ------------------------------------------------------------- what decides the band

        // A canvas of its own kind, a viewport in its middle, and a long text whose lines run through it from far above.
        LetteraText MakeUnder(RenderMode mode, bool stencil, float angle, float scale, out Canvas canvas)
        {
            var root = new GameObject("Canvas", typeof(RectTransform), typeof(Canvas));
            Track(root);
            canvas = root.GetComponent<Canvas>();
            canvas.renderMode = mode;
            if (mode == RenderMode.ScreenSpaceCamera)
            {
                var eye = new GameObject("Camera", typeof(Camera));
                Track(eye);
                eye.transform.position = new Vector3(0, 0, -100);
                canvas.worldCamera = eye.GetComponent<Camera>();
                canvas.planeDistance = 50;
            }
            if (mode == RenderMode.WorldSpace) ((RectTransform)root.transform).sizeDelta = new Vector2(4000, 4000);
            var view = new GameObject("Viewport", typeof(RectTransform));
            view.transform.SetParent(root.transform, false);
            ((RectTransform)view.transform).sizeDelta = new Vector2(600, 300);
            view.transform.localRotation = Quaternion.Euler(0, 0, angle);
            view.transform.localScale = Vector3.one * scale;
            if (stencil)
            {
                view.AddComponent<Image>();
                view.AddComponent<Mask>().showMaskGraphic = false;
            }
            else
            {
                view.AddComponent<RectMask2D>();
            }
            var go = new GameObject("Text", typeof(RectTransform));
            go.transform.SetParent(view.transform, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 1);
            rect.sizeDelta = new Vector2(600, 100000);
            rect.anchoredPosition = new Vector2(0, 150 + 12000);
            var text = go.AddComponent<LetteraText>();
            text.Font = Latin;
            text.FontSize = 14;
            text.Text = Page(200 * 1024);
            Canvas.ForceUpdateCanvases();
            text.ForceRebuild();
            return text;
        }

        // What stands in the middle of the viewport is drawn when the text is set, and once finished, every line is where a whole build puts it.
        void MiddleIsDrawn(RenderMode mode, bool stencil, float angle, float scale, string what)
        {
            LetteraLongText.FinishLater = false;
            LetteraText whole = MakeUnder(mode, stencil, angle, scale, out _);
            LetteraLongText.FinishLater = true;
            Vector2 local = whole.transform.InverseTransformPoint(whole.transform.parent.position);
            int middle = whole.HitTestCaret(local, out _);

            LetteraText part = MakeUnder(mode, stencil, angle, scale, out _);
            TextEngine engine = EngineOf(part);
            what += " (" + engine.BandReport + ")";
            Assert.Greater(engine.PendingParagraphs, 0, what + ": paragraphs wait");
            Assert.Greater(engine.VertexCount, 0, what + ": something is drawn");
            bool drawn = false;
            for (int i = 0; i < part.GlyphCount && !drawn; i++) drawn = System.Math.Abs(part.GetGlyph(i).Cluster - middle) < 200;
            Assert.IsTrue(drawn, what + ": what stands in the middle is drawn when set");
            FinishBySlices(part, out _);
            LetteraLongText.FinishLater = false;
            LetteraText again = MakeUnder(mode, stencil, angle, scale, out _);
            LetteraLongText.FinishLater = true;
            SameAsWhole(again, part, what);
        }

        [Test]
        public void TheLinesInViewAreDrawnUnderAStencilMaskACameraCanvasAndATurnedScaledViewport()
        {
            LetteraLongText.VisibleLinesOnly = true;
            MiddleIsDrawn(RenderMode.ScreenSpaceOverlay, true, 0, 1, "stencil mask on an overlay canvas");
            MiddleIsDrawn(RenderMode.ScreenSpaceCamera, false, 0, 1, "camera canvas");
            MiddleIsDrawn(RenderMode.ScreenSpaceCamera, true, 0, 1, "stencil mask on a camera canvas");
            MiddleIsDrawn(RenderMode.WorldSpace, false, 30, 0.5f, "a viewport turned and scaled down");
            MiddleIsDrawn(RenderMode.ScreenSpaceOverlay, false, 90, 2, "a viewport turned on its side and scaled up");
        }

        [Test]
        public void FinishingAcrossWorkersEndsAsAWholeBuild()
        {
            LetteraWorkerMode saved = LetteraWorkers.Mode;
            LetteraWorkers.Mode = LetteraWorkerMode.Parallel;
            try
            {
                string source = Page(200 * 1024);
                LetteraLongText.VisibleLinesOnly = true;
                LetteraText whole = MakeWhole(source, 20000);
                int batches = LetteraWorkers.ParallelBatches;
                LetteraText part = Make(source, out _, 20000);
                Assert.Greater(EngineOf(part).PendingParagraphs, 0);
                FinishBySlices(part, out _);
                Assert.Greater(LetteraWorkers.ParallelBatches, batches, "finishing went across the workers");
                SameAsWhole(whole, part, "finished across workers");
            }
            finally
            {
                LetteraWorkers.Mode = saved;
            }
        }
    }
}

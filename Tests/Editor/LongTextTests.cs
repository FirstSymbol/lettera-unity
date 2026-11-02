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
        bool m_Saved;

        [SetUp]
        public void SaveSwitch() => m_Saved = LetteraLongText.VisibleLinesOnly;

        [TearDown]
        public void RestoreSwitch() => LetteraLongText.VisibleLinesOnly = m_Saved;

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
        LetteraText Make(string source, out RectTransform viewport)
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
            rect.anchoredPosition = Vector2.zero;
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
    }
}

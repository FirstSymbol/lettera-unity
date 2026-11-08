using System.Collections.Generic;
using System.Text;
using NUnit.Framework;
using UnityEngine;

namespace FirstSymbol.Lettera.Editor.Tests
{
    public class DocumentFeatureCanvasTests
    {
        [Test]
        public void AJumpBeforeTheFirstLayoutAsksTheCanvasForNothingFromInsideItsRebuild()
        {
            var canvasObject = new GameObject("Canvas", typeof(Canvas));
            try
            {
                var go = new GameObject("Text", typeof(RectTransform));
                go.transform.SetParent(canvasObject.transform, false);
                ((RectTransform)go.transform).sizeDelta = new Vector2(600, 300);
                var text = go.AddComponent<LetteraText>();
                var body = new System.Text.StringBuilder();
                for (int i = 0; i < 4000; i++) body.Append("Line ").Append(i).Append(" of a document.\n");
                LetteraRope rope = LetteraRope.FromString(body.ToString());
                text.OpenDocument(rope, takeOwnership: true);
                text.View.ScrollToChar(rope.Length / 2, LetteraRevealAlign.Center);
                // A log of an error here fails the test by itself.
                Canvas.ForceUpdateCanvases();
                Assert.Greater(((ITextComponent)text).SharedEngine.VertexCount, 0);
                text.CloseDocument();
            }
            finally
            {
                Object.DestroyImmediate(canvasObject);
            }
        }
    }

    /// <summary>What stays across windows, reading a document whole, and bars.</summary>
    public class DocumentFeatureTests : EditingFixture
    {
        static string Lines(int count)
        {
            var body = new StringBuilder(count * 20);
            for (int i = 0; i < count; i++) body.Append("Line ").Append(i).Append(" of some words\n");
            return body.ToString();
        }

        LetteraSelectable DocReader(string text)
        {
            LetteraWorldText label = Make("", 600, 200);
            label.OpenDocument(LetteraRope.FromString(text), true);
            label.ForceRebuild();
            var selectable = label.gameObject.AddComponent<LetteraSelectable>();
            selectable.Activate();
            return selectable;
        }

        // ------------------------------------------------------------- selection

        [Test]
        public void TheSelectionStaysThroughAFarScrollAndComesBack()
        {
            string body = Lines(5000);
            LetteraSelectable reader = DocReader(body);
            LetteraWorldText text = reader.GetComponent<LetteraWorldText>();
            reader.Select(5, 20);
            Assert.AreEqual((5, 20), reader.DocumentSelection);
            text.View.ScrollToChar(body.Length / 2);
            text.ForceRebuild();
            Assert.Greater(text.View.WindowStart, 1000);
            Assert.AreEqual((5, 20), reader.DocumentSelection, "it stays");
            Assert.IsTrue(reader.Selection.IsCaret, "nothing of it shows here");
            text.View.ScrollTo(0);
            text.ForceRebuild();
            Assert.AreEqual(5, reader.Selection.Start);
            Assert.AreEqual(20, reader.Selection.End, "it is back");
        }

        [Test]
        public void ASelectionAcrossTheDocumentCopiesItAllWithMarkupGone()
        {
            var body = new StringBuilder("<b>");
            var plain = new StringBuilder();
            for (int i = 0; i < 4000; i++)
            {
                string line = "Line " + i + " words\n";
                body.Append(line);
                plain.Append(line);
            }
            body.Append("</b>");
            LetteraSelectable reader = DocReader(body.ToString());
            reader.SetDocumentSelection(3, body.Length);
            Assert.AreEqual(plain.ToString(), reader.SelectedPlainText());
        }

        [Test]
        public void TheSelectionFollowsAnEditAbove()
        {
            string body = Lines(2000);
            LetteraSelectable reader = DocReader(body);
            LetteraWorldText text = reader.GetComponent<LetteraWorldText>();
            reader.SetDocumentSelection(100, 110);
            text.View.Rope.Insert(0, "added ");
            Assert.AreEqual((106, 116), reader.DocumentSelection);
        }

        [Test]
        public void APlainTextsSelectionIsAsBefore()
        {
            LetteraSelectable reader = Reader("Some plain words");
            reader.Select(2, 6);
            Assert.AreEqual((2, 6), reader.DocumentSelection);
            Assert.AreEqual("me p", reader.SelectedPlainText());
        }

        // ------------------------------------------------------------- range identity

        static string Spoilers(int count)
        {
            var body = new StringBuilder();
            for (int i = 0; i < count; i++) body.Append("Row ").Append(i).Append(" <spoiler>secret ").Append(i).Append("</spoiler>\n");
            return body.ToString();
        }

        [Test]
        public void ARevealedSpoilerIsStillRevealedAfterAFarScroll()
        {
            string body = Spoilers(4000);
            LetteraWorldText text = Make("", 600, 200);
            text.OpenDocument(LetteraRope.FromString(body), true);
            text.ForceRebuild();
            LetteraRangeRouter router = LetteraRangeRouter.Of((Component)text);
            int id = text.Engine.ActiveRanges[0].Id;
            router.SetUncovered(id, true);
            text.View.ScrollToChar(body.Length / 2);
            text.ForceRebuild();
            foreach (TextEngine.ActiveRange range in text.Engine.ActiveRanges) Assert.IsFalse(router.IsUncovered(range.Id), "far away, nothing is revealed");
            text.View.ScrollTo(0);
            text.ForceRebuild();
            Assert.AreEqual(id, text.Engine.ActiveRanges[0].Id, "it comes back as itself");
            Assert.IsTrue(router.IsUncovered(id));
        }

        [Test]
        public void NoIdRepeatsAcrossScrolls()
        {
            string body = Spoilers(3000);
            LetteraWorldText text = Make("", 600, 200);
            text.OpenDocument(LetteraRope.FromString(body), true);
            text.ForceRebuild();
            var random = new System.Random(2);
            var live = new HashSet<int>();
            var seen = new HashSet<int>();
            for (int step = 0; step < 60; step++)
            {
                text.View.ScrollToChar(random.Next(body.Length));
                text.ForceRebuild();
                var now = new HashSet<int>();
                foreach (TextEngine.ActiveRange range in text.Engine.ActiveRanges)
                {
                    Assert.IsTrue(now.Add(range.Id), "two live ranges share an id");
                    if (!live.Contains(range.Id)) Assert.IsTrue(seen.Add(range.Id), "a retired id came back");
                }
                live = now;
            }
        }

        [Test]
        public void HoverEndsWhenAWheelCarriesALinkOutOfTheWindow()
        {
            var body = new StringBuilder("<link=https://a.io>here</link>\n");
            body.Append(Lines(5000));
            LetteraWorldText text = Make("", 600, 200);
            text.OpenDocument(LetteraRope.FromString(body.ToString()), true);
            text.ForceRebuild();
            LetteraRangeRouter router = LetteraRangeRouter.Of((Component)text);
            int exits = 0;
            router.All.Exited += _ => exits++;
            var rects = new List<Rect>();
            text.GetRangeRects(0, 4, rects);
            LetteraSelectable.ClassifyOverride = _ => LetteraPointerKind.Mouse;
            router.PointerInside(new UnityEngine.EventSystems.PointerEventData(null) { position = rects[0].center, pointerId = -1 }, true);
            Assert.AreNotEqual(0, router.Hovered);
            text.View.ScrollTo(text.View.MaxDocumentY / 2);
            text.ForceRebuild();
            LetteraMotionHub.Tick(0.01f, 0.01f);
            Assert.AreEqual(0, router.Hovered);
            Assert.AreEqual(1, exits, "the pointer left the link once");
            router.PointerInside(new UnityEngine.EventSystems.PointerEventData(null) { position = rects[0].center, pointerId = -1 }, false);
        }

        // ------------------------------------------------------------- search

        static List<LetteraSearchHit> SearchAll(LetteraDocumentSearch search)
        {
            var hits = new List<LetteraSearchHit>();
            search.Start();
            while (search.Step(0.1f, hits)) { }
            return hits;
        }

        [Test]
        public void SearchAgreesWithABruteForceAcrossBlocks()
        {
            string body = Lines(6000) + "needle" + Lines(6000) + "needle";
            var search = new LetteraDocumentSearch(LetteraRope.FromString(body)) { Query = "Line 59" };
            List<LetteraSearchHit> hits = SearchAll(search);
            var wanted = new List<int>();
            for (int at = body.IndexOf("Line 59", System.StringComparison.Ordinal); at >= 0; at = body.IndexOf("Line 59", at + 7, System.StringComparison.Ordinal)) wanted.Add(at);
            Assert.AreEqual(wanted.Count, hits.Count);
            for (int i = 0; i < hits.Count; i++) Assert.AreEqual(wanted[i], hits[i].Start);
        }

        [Test]
        public void AHitAcrossABlockEdgeIsFound()
        {
            LetteraRope rope = LetteraRope.FromString(Lines(3000));
            int edge = rope.CharOfBlock(5);
            rope.Replace(edge - 3, 6, "NEEDLE");
            var search = new LetteraDocumentSearch(rope) { Query = "NEEDLE" };
            List<LetteraSearchHit> hits = SearchAll(search);
            Assert.AreEqual(1, hits.Count);
            Assert.AreEqual(edge - 3, hits[0].Start);
        }

        [Test]
        public void ShownFindsAWordSplitByATagAndSourceDoesNot()
        {
            const string Body = "start <b>he</b>llo end";
            LetteraWorldText text = Make(Body);
            LetteraRope rope = LetteraRope.FromString(Body);
            var shown = new LetteraDocumentSearch(rope, text) { Query = "hello", Scope = LetteraSearchScope.Shown };
            List<LetteraSearchHit> hits = SearchAll(shown);
            Assert.AreEqual(1, hits.Count);
            Assert.AreEqual(Body.IndexOf("he</b>", System.StringComparison.Ordinal), hits[0].Start);
            Assert.AreEqual(Body.IndexOf("llo", System.StringComparison.Ordinal) + 3, hits[0].End);
            var source = new LetteraDocumentSearch(rope, text) { Query = "hello", Scope = LetteraSearchScope.Source };
            Assert.AreEqual(0, SearchAll(source).Count);
        }

        [Test]
        public void CaseCanBeIgnored()
        {
            var search = new LetteraDocumentSearch(LetteraRope.FromString("One TWO three")) { Query = "two", Comparison = System.StringComparison.OrdinalIgnoreCase };
            Assert.AreEqual(1, SearchAll(search).Count);
        }

        [Test]
        public void AStepKeepsNearItsBudget()
        {
            var rope = LetteraRope.FromString(Lines(100000));
            double worst = 0;
            int steps = 0;
            // A step that overruns does so on every pass; one preempted by a busy machine only on some.
            for (int attempt = 0; attempt < 5; attempt++)
            {
                var search = new LetteraDocumentSearch(rope) { Query = "no such thing" };
                var hits = new List<LetteraSearchHit>();
                search.Start();
                worst = 0;
                steps = 0;
                while (true)
                {
                    var clock = System.Diagnostics.Stopwatch.StartNew();
                    bool more = search.Step(1, hits);
                    worst = System.Math.Max(worst, clock.Elapsed.TotalMilliseconds);
                    steps++;
                    if (!more) break;
                }
                if (worst < 3 + 2) break;
            }
            Debug.Log($"LETTERA_BUDGET search_step_worst_ms={worst:0.00} steps={steps}");
            Assert.Less(worst, 3 + 2, "a step ran far past its budget");
        }

        // ------------------------------------------------------------- bars

        [Test]
        public void TooSmallToReadItDrawsBarsAndNoGlyphs()
        {
            LetteraWorldText text = Make("", 600, 400);
            text.OpenDocument(LetteraRope.FromString(Lines(20000)), true);
            text.ForceRebuild();
            int shapes = text.Engine.ShapeCount;
            float projected = 3;
            text.View.ProjectedEmOverride = () => projected;
            LetteraMotionHub.Tick(0.01f, 0.01f);
            text.ForceRebuild();
            Assert.IsTrue(text.View.IsShownAsBars);
            Assert.AreEqual(shapes, text.Engine.ShapeCount, "bars shape nothing");
            Assert.Greater(((ITextComponent)text).SharedEngine.VertexCount, 0);
            Assert.Less(text.Engine.VertexCount / 4, 400 / 2);

            // At the edge a slow zoom does not flicker.
            projected = 4.2f;
            LetteraMotionHub.Tick(0.01f, 0.01f);
            Assert.IsTrue(text.View.IsShownAsBars, "still within the margin");
            projected = 5;
            LetteraMotionHub.Tick(0.01f, 0.01f);
            text.ForceRebuild();
            Assert.IsFalse(text.View.IsShownAsBars);
            Assert.Greater(text.Engine.VertexCount / 4, 400 / 2, "the text is back");
        }

        [Test]
        public void BarsTakeNoPointer()
        {
            LetteraWorldText text = Make("", 600, 400);
            text.OpenDocument(LetteraRope.FromString("<link=https://a.io>here</link>\n" + Lines(50)), true);
            text.ForceRebuild();
            LetteraRangeRouter router = LetteraRangeRouter.Of((Component)text);
            var rects = new List<Rect>();
            text.GetRangeRects(0, 4, rects);
            Vector2 point = rects[0].center;
            Assert.AreNotEqual(0, router.HitTest(point, null));
            text.View.ProjectedEmOverride = () => 2;
            LetteraMotionHub.Tick(0.01f, 0.01f);
            text.ForceRebuild();
            Assert.AreEqual(0, router.HitTest(point, null));
        }

        [Test]
        public void BarsCanBeTurnedOff()
        {
            LetteraWorldText text = Make("", 600, 400);
            text.OpenDocument(LetteraRope.FromString(Lines(100)), true);
            text.ForceRebuild();
            text.View.BarsEnabled = false;
            text.View.ProjectedEmOverride = () => 1;
            LetteraMotionHub.Tick(0.01f, 0.01f);
            Assert.IsFalse(text.View.IsShownAsBars);
        }
    }
}

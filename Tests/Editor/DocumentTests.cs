using System.Collections.Generic;
using System.Text;
using NUnit.Framework;
using UnityEngine;

namespace FirstSymbol.Lettera.Editor.Tests
{
    /// <summary>Documents through a window: what is materialized, where it stands, and what a scroll costs.</summary>
    public class DocumentTests : TextFixture
    {
        static string Lines(int count, string prefix = "Line ")
        {
            var body = new StringBuilder(count * 16);
            for (int i = 0; i < count; i++) body.Append(prefix).Append(i).Append(" of some words\n");
            return body.ToString();
        }

        LetteraWorldText Doc(string text, float height = 200, float width = 600)
        {
            LetteraWorldText label = Make("", width, height);
            label.OpenDocument(LetteraRope.FromString(text), true);
            label.ForceRebuild();
            return label;
        }

        [Test]
        public void APlainTextHasNoViewAndNothingScrolls()
        {
            LetteraWorldText text = Make("Plain");
            Assert.IsFalse(text.IsWindowed);
            Assert.IsNull(text.View);
            Assert.AreEqual(0, text.Engine.WindowOffset);
        }

        [Test]
        public void OnlyTheWindowIsMaterialized()
        {
            string body = Lines(20000);
            LetteraWorldText text = Doc(body);
            Assert.IsTrue(text.IsWindowed);
            Assert.Less(text.View.WindowLength, body.Length / 10);
            Assert.Less(text.Engine.VertexCount / 4, 14000);
            Assert.Greater(text.Engine.VertexCount, 0);
        }

        [Test]
        public void AScrollInsideTheWindowIsOnlyAPose()
        {
            LetteraWorldText text = Doc(Lines(5000));
            int rebuilds = text.RebuildCount, poses = text.PoseCount;
            Vector3 before = text.Engine.Vertices[0];
            text.View.ScrollBy(new Vector2(0, 1));
            Assert.AreEqual(rebuilds, text.RebuildCount, "a rebuild");
            Assert.Greater(text.PoseCount, poses);
            Assert.AreEqual(before.y + 1, text.Engine.Vertices[0].y, 1e-3f);
        }

        [Test]
        public void HitTestsFollowTheScroll()
        {
            LetteraWorldText text = Doc(Lines(5000), 200);
            var rects = new List<Rect>();
            text.GetRangeRects(0, 1, rects);
            float y0 = rects[0].center.y;
            text.View.ScrollBy(new Vector2(0, 7));
            rects.Clear();
            text.GetRangeRects(0, 1, rects);
            Assert.AreEqual(y0 + 7, rects[0].center.y, 1e-3f);
            // The same screen point now reads what scrolled there.
            Assert.AreEqual(0, text.Engine.HitTestCaret(rects[0].center, out _) / 2);
        }

        [Test]
        public void AFarJumpShowsWhatIsThere()
        {
            string body = Lines(20000);
            LetteraWorldText text = Doc(body);
            int target = body.IndexOf("Line 15000 ");
            text.View.ScrollToChar(target);
            text.ForceRebuild();
            Assert.LessOrEqual(text.View.WindowStart, target);
            Assert.Greater(text.View.WindowStart + text.View.WindowLength, target);
            Assert.AreEqual(target, text.View.Anchor.DocChar);
            // The anchor's line stands at the viewport's top.
            Assert.IsTrue(text.Engine.TryWindowLineTop(target, out float top));
            Assert.AreEqual(top, text.Engine.WindowOffset, 1f);
        }

        [Test]
        public void ScrollingThroughAWholeDocumentReadsTheSameAsAPlainRender()
        {
            string body = Lines(1500);
            LetteraWorldText plain = Make(body, 600, 100000);
            plain.ForceRebuild();
            float plainLine = plain.Engine.LineCountForHooks > 1 ? plain.Engine.LineForHooks(0).Height : 0;

            LetteraWorldText text = Doc(body, 200);
            text.View.FinishBackgroundForTests();
            text.ForceRebuild();
            Assert.AreEqual(plain.Engine.LineCountForHooks * plainLine, text.View.Rope.TotalHeight, plainLine * 2, "the document's height once measured");
            for (int line = 0; line < 1490; line += 97)
            {
                text.View.ScrollTo(line * plainLine);
                text.ForceRebuild();
                Assert.AreEqual(line * plainLine, text.View.DocumentY, plainLine, "at line " + line);
                int anchor = text.View.Anchor.DocChar;
                Assert.AreEqual(plain.Engine.DocCharOfLineForTests(line), anchor, 40, "what shows at line " + line);
            }
        }

        [Test]
        public void ACorrectionAboveMovesNothingUnderTheViewport()
        {
            LetteraWorldText text = Doc(Lines(5000));
            text.View.ScrollTo(20000);
            text.ForceRebuild();
            int anchor = text.View.Anchor.DocChar;
            Assert.IsTrue(text.Engine.TryWindowLineTop(anchor, out float lineTop));
            float shown = lineTop - text.Engine.WindowOffset;
            double y = text.View.DocumentY;
            text.View.FinishBackgroundForTests();
            text.ForceRebuild();
            Assert.AreEqual(anchor, text.View.Anchor.DocChar);
            Assert.IsTrue(text.Engine.TryWindowLineTop(anchor, out float after));
            Assert.AreEqual(shown, after - text.Engine.WindowOffset, 0.01f, "what is under the viewport moved");
            Assert.AreNotEqual(y, text.View.DocumentY, "measuring what lies above should correct the position");
        }

        [Test]
        public void ATagOpenedBeforeTheWindowStillApplies()
        {
            var body = new StringBuilder("<color=#ff0000>");
            body.Append(Lines(4000));
            body.Append("</color>tail");
            LetteraWorldText text = Doc(body.ToString());
            text.View.FinishBackgroundForTests();
            text.View.ScrollToChar(body.Length / 2);
            text.ForceRebuild();
            Assert.Greater(text.View.WindowStart, 1000);
            Color32 color = text.Engine.Colors[0];
            Assert.AreEqual(255, color.r);
            Assert.AreEqual(0, color.g, "the color opened long before the window");
            string shown = new string(' ', 0);
            for (int i = 0; i < 6; i++) shown += char.ConvertFromUtf32(text.Engine.ShownCodepoints[i]);
            StringAssert.DoesNotContain("<", shown);
        }

        [Test]
        public void NoParseOpenedBeforeTheWindowKeepsTagsLiteral()
        {
            var body = new StringBuilder("<noparse>");
            body.Append(Lines(4000, "<b>Line "));
            LetteraWorldText text = Doc(body.ToString());
            text.View.FinishBackgroundForTests();
            text.View.ScrollToChar(body.Length / 2);
            text.ForceRebuild();
            Assert.Greater(text.View.WindowStart, 1000);
            bool literal = false;
            System.ReadOnlySpan<int> shown = text.Engine.ShownCodepoints;
            for (int i = 0; i < shown.Length; i++) literal |= shown[i] == '<';
            Assert.IsTrue(literal, "<b> should show as text");
        }

        [Test]
        public void OneHugeParagraphStaysUnderTheCap()
        {
            var body = new StringBuilder();
            for (int i = 0; i < 400000; i++) body.Append(i % 6 == 5 ? ' ' : 'a');
            LetteraWorldText text = Doc(body.ToString());
            Assert.LessOrEqual(text.Engine.VertexCount / 4, LetteraSettings.Instance.Documents.MaxWindowCodepoints);
            for (int step = 0; step < 6; step++)
            {
                text.View.ScrollTo(text.View.MaxDocumentY * step / 5);
                text.ForceRebuild();
                Assert.LessOrEqual(text.Engine.VertexCount / 4, LetteraSettings.Instance.Documents.MaxWindowCodepoints);
            }
        }

        [Test]
        public void AnEditAboveTheViewportKeepsWhatShows()
        {
            string body = Lines(5000);
            LetteraWorldText text = Doc(body);
            text.View.ScrollToChar(body.IndexOf("Line 3000 "));
            text.ForceRebuild();
            int anchor = text.View.Anchor.DocChar;
            string under = text.View.Rope.ToString(anchor, 12);
            text.View.Rope.Delete(0, body.IndexOf("Line 100 "));
            text.ForceRebuild();
            Assert.AreEqual(under, text.View.Rope.ToString(text.View.Anchor.DocChar, 12));
        }

        [Test]
        public void AMarqueeIsRefusedOnAWindow()
        {
            UnityEngine.TestTools.LogAssert.ignoreFailingMessages = true;
            LetteraWorldText text = Make("", 200, 100);
            text.Styles.Add(LetteraStyle.ForWholeText(new MarqueeEffect()));
            text.OpenDocument(LetteraRope.FromString(Lines(500)), true);
            text.ForceRebuild();
            UnityEngine.TestTools.LogAssert.ignoreFailingMessages = false;
            Assert.IsFalse(text.Engine.IsMarqueeScrolling);
        }

        [Test]
        public void ClosingGoesBackToTheSerializedText()
        {
            LetteraWorldText text = Make("Serialized");
            text.OpenDocument(LetteraRope.FromString(Lines(100)), true);
            text.ForceRebuild();
            text.CloseDocument();
            text.ForceRebuild();
            Assert.IsFalse(text.IsWindowed);
            Assert.AreEqual(10, text.Engine.ShownCodepoints.Length);
        }

        [Test]
        public void AWiderViewportKeepsTheAnchorsCharacter()
        {
            string body = Lines(5000);
            LetteraWorldText text = Doc(body);
            text.View.ScrollToChar(body.IndexOf("Line 2000 "));
            text.ForceRebuild();
            int anchor = text.View.Anchor.DocChar;
            ((RectTransform)text.transform).sizeDelta = new Vector2(900, 200);
            text.OnRectChanged(new Vector2(900, 200));
            text.ForceRebuild();
            text.ForceRebuild();
            Assert.AreEqual(anchor, text.View.Anchor.DocChar, 30);
        }

        [Test]
        public void RandomScrollsAndResizesThrowNothingAndKeepTheCap()
        {
            var random = new System.Random(3);
            var body = new StringBuilder();
            for (int i = 0; i < 6000; i++)
            {
                if (random.Next(20) == 0) body.Append("<b>");
                body.Append("words ").Append(i);
                if (random.Next(20) == 0) body.Append("</b>");
                body.Append('\n');
            }
            LetteraWorldText text = Doc(body.ToString());
            for (int step = 0; step < 600; step++)
            {
                switch (random.Next(4))
                {
                    case 0: text.View.ScrollTo(random.NextDouble() * text.View.MaxDocumentY); break;
                    case 1: text.View.ScrollBy(new Vector2(0, random.Next(-300, 300))); break;
                    case 2: text.View.ScrollToChar(random.Next(text.View.Rope.Length)); break;
                    default:
                        float width = random.Next(100, 900);
                        ((RectTransform)text.transform).sizeDelta = new Vector2(width, 200);
                        text.OnRectChanged(new Vector2(width, 200));
                        break;
                }
                text.ForceRebuild();
                Assert.LessOrEqual(text.Engine.VertexCount / 4, LetteraSettings.Instance.Documents.MaxWindowCodepoints + 64);
            }
        }

        [Test]
        public void TheWalkPassesBlocksWithNoTriggerAlmostForFree()
        {
            var body = new StringBuilder();
            for (int i = 0; i < 200000; i++) body.Append("Plain words only here.\n");
            LetteraRope rope = LetteraRope.FromString(body.ToString(), new LetteraMarkupTriggers('<'));
            LetteraWorldText text = Make("", 600, 200);
            text.OpenDocument(rope, true);
            var clock = System.Diagnostics.Stopwatch.StartNew();
            var walk = new DocumentTagWalk(rope);
            while (walk.Advance(text.Engine, double.MaxValue)) { }
            UnityEngine.Debug.Log($"LETTERA_BUDGET walk_{rope.BlockCount}_blocks_ms={clock.Elapsed.TotalMilliseconds:0.00}");
            Assert.Less(clock.Elapsed.TotalMilliseconds, 50);
        }

        [Test]
        public void AScrollPastTheWholeWindowBuildsALeanOneThatGrowsBackWhenScrollingStops()
        {
            LetteraWorldText text = Doc(Lines(20000), 200);
            int full = text.View.WindowLength;
            double step = text.View.ViewportHeight * 4;
            int lean = 0;
            for (int i = 1; i <= 6; i++)
            {
                text.View.ScrollTo(i * step);
                text.ForceRebuild();
                Assert.IsTrue(text.View.IsLean, "a scroll past the window leaves no margin worth building");
                lean = text.View.WindowLength;
                Assert.Less(lean, full, "a lean window holds less than one with margins");
                // What shows is placed, from the viewport's top to its bottom, and nothing far past it.
                float low = float.MaxValue, high = float.MinValue;
                for (int v = 0; v < text.Engine.VertexCount; v++)
                {
                    low = Mathf.Min(low, text.Engine.Vertices[v].y);
                    high = Mathf.Max(high, text.Engine.Vertices[v].y);
                }
                Assert.Greater(high, 100 - 40, "the viewport's top line is drawn");
                Assert.Less(low, -100 + 40, "the viewport's bottom line is drawn");
                Assert.Less(high, 100 + 80);
                Assert.Greater(low, -100 - 80);
            }
            int anchor = text.View.Anchor.DocChar;
            var client = (IMotionClient)text.View;
            client.TickMotion(0.016f, 0.016f);
            client.TickMotion(0.016f, 0.016f);
            text.ForceRebuild();
            Assert.IsFalse(text.View.IsLean, "two frames with no scroll give the window its margins again");
            Assert.Greater(text.View.WindowLength, lean);
            Assert.AreEqual(anchor, text.View.Anchor.DocChar, "the same line stays at the top");

            // A slow scroll from a lean window builds one with margins, and scrolls inside it after that.
            text.View.ScrollTo(text.View.DocumentY + step);
            text.ForceRebuild();
            Assert.IsTrue(text.View.IsLean);
            text.View.ScrollBy(new Vector2(0, 5));
            text.ForceRebuild();
            Assert.IsFalse(text.View.IsLean, "a scroll that stays near the window gets margins");
            int rebuilds = text.RebuildCount;
            text.View.ScrollBy(new Vector2(0, 5));
            text.ForceRebuild();
            Assert.AreEqual(rebuilds, text.RebuildCount, "and moves inside them as a pose");
        }
    }

    /// <summary>The scroller on a Canvas text, on the fixture's fake clock.</summary>
    public class DocumentScrollerTests : TextFixture
    {
        readonly List<GameObject> m_Made = new List<GameObject>();

        [TearDown]
        public void Clean()
        {
            foreach (GameObject go in m_Made)
                if (go != null)
                    Object.DestroyImmediate(go);
            m_Made.Clear();
        }

        LetteraDocumentScroller Scroller(string text, bool document)
        {
            var canvasObject = new GameObject("Canvas", typeof(Canvas));
            m_Made.Add(canvasObject);
            canvasObject.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            var viewObject = new GameObject("View", typeof(RectTransform));
            viewObject.transform.SetParent(canvasObject.transform, false);
            var viewRect = (RectTransform)viewObject.transform;
            viewRect.sizeDelta = new Vector2(400, 200);
            var textObject = new GameObject("Text", typeof(RectTransform));
            textObject.transform.SetParent(viewObject.transform, false);
            var textRect = (RectTransform)textObject.transform;
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.sizeDelta = Vector2.zero;
            var label = textObject.AddComponent<LetteraText>();
            label.Font = Latin;
            label.FontSize = 20;
            if (document) label.OpenDocument(LetteraRope.FromString(text), true);
            else label.Text = text;
            var scroller = viewObject.AddComponent<LetteraDocumentScroller>();
            label.ForceRebuild();
            return scroller;
        }

        static string Lines(int count)
        {
            var body = new StringBuilder();
            for (int i = 0; i < count; i++) body.Append("Line ").Append(i).Append('\n');
            return body.ToString();
        }

        [TestCase(true)]
        [TestCase(false)]
        public void TheWheelMovesAndNoRebuildRuns(bool document)
        {
            LetteraDocumentScroller scroller = Scroller(Lines(2000), document);
            LetteraText text = scroller.Text;
            text.ForceRebuild();
            int rebuilds = text.RebuildCount;
            var wheel = new UnityEngine.EventSystems.PointerEventData(null) { scrollDelta = new Vector2(0, -1) };
            scroller.OnScroll(wheel);
            Assert.Greater(scroller.Position, 0);
            Assert.AreEqual(rebuilds, text.RebuildCount);
        }

        [Test]
        public void AtTheStartAWheelUpGoesToAnEnclosingView()
        {
            LetteraDocumentScroller scroller = Scroller(Lines(2000), true);
            var counter = scroller.transform.parent.gameObject.AddComponent<WheelCounter>();
            var wheel = new UnityEngine.EventSystems.PointerEventData(null) { scrollDelta = new Vector2(0, 1) };
            scroller.OnScroll(wheel);
            Assert.AreEqual(1, counter.Wheels);
            scroller.PassWheelAtEdges = false;
            scroller.OnScroll(wheel);
            Assert.AreEqual(1, counter.Wheels);
        }

        sealed class WheelCounter : MonoBehaviour, UnityEngine.EventSystems.IScrollHandler
        {
            public int Wheels;
            public void OnScroll(UnityEngine.EventSystems.PointerEventData data) => Wheels++;
        }

        [Test]
        public void ToTheEndAndBack()
        {
            LetteraDocumentScroller scroller = Scroller(Lines(2000), true);
            scroller.ScrollToEnd();
            scroller.Text.ForceRebuild();
            Assert.AreEqual(scroller.Text.View.MaxDocumentY, scroller.Text.View.DocumentY, 1);
            scroller.ScrollToStart();
            scroller.Text.ForceRebuild();
            Assert.AreEqual(0, scroller.Text.View.DocumentY, 1);
        }

        [Test]
        public void AScrollbarHidesWhenThereIsNothingToScrollAndComesBack()
        {
            LetteraDocumentScroller scroller = Scroller("Short", false);
            var bar = new GameObject("Bar", typeof(RectTransform)).AddComponent<UnityEngine.UI.Scrollbar>();
            bar.transform.SetParent(scroller.transform, false);
            scroller.VerticalScrollbar = bar;
            Assert.IsFalse(bar.gameObject.activeSelf);
            scroller.Text.Text = Lines(200);
            scroller.Text.ForceRebuild();
            Assert.IsTrue(bar.gameObject.activeSelf);
            Assert.Less(bar.size, 1);
        }

        [Test]
        public void ItsInspectorIsOurOwn()
        {
            LetteraDocumentScroller scroller = Scroller("Short", false);
            UnityEditor.Editor editor = UnityEditor.Editor.CreateEditor(scroller);
            UnityEngine.UIElements.VisualElement root = editor.CreateInspectorGUI();
            Assert.IsTrue(root.ClassListContains("lt-root"));
            Object.DestroyImmediate(editor);
        }
    }

    /// <summary>What depends on positions stays right when positions run ahead of the frame's rebuild.</summary>
    public class PositionFollowerTests : EditingFixture
    {
        [Test]
        public void HitAreasFollowAWidthChangeThatRanOutsideARebuild()
        {
            LetteraWorldText text = Make("<link=https://a.io>here</link>", 400, 100);
            text.ForceRebuild();
            LetteraRangeRouter router = LetteraRangeRouter.Of((Component)text);
            var rects = new List<Rect>();
            text.GetRangeRects(0, 4, rects);
            Assert.AreNotEqual(0, router.HitTest(rects[0].center, null));
            ((RectTransform)text.transform).sizeDelta = new Vector2(1000, 100);
            text.OnRectChanged(new Vector2(1000, 100));
            text.Engine.EnsurePositioned();
            rects.Clear();
            text.GetRangeRects(0, 4, rects);
            Assert.AreNotEqual(0, router.HitTest(rects[0].center, null), "the hit area stayed where the text was");
        }
    }
}

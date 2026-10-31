using System;
using System.Collections.Generic;
using FirstSymbol.Lettera.Unicode;
using NUnit.Framework;
using UnityEngine;

namespace FirstSymbol.Lettera.Editor.Tests
{
    /// <summary>Where carets and ranges stand on screen, and which stops the navigator finds.</summary>
    public class CaretGeometryTests : TextFixture
    {
        static float CaretX(LetteraWorldText text, int index, LetteraCaretBias bias)
        {
            Assert.IsTrue(text.TryGetCaretRect(index, bias, out Rect rect));
            return rect.x;
        }

        static int[] Cps(string text)
        {
            var list = new List<int>();
            for (int i = 0; i < text.Length; i += char.IsSurrogatePair(text, i) ? 2 : 1) list.Add(char.ConvertToUtf32(text, i));
            return list.ToArray();
        }

        LetteraWorldText Mixed(string value, float width = 1000)
        {
            LetteraWorldText text = Make("", width);
            text.Font = null;
            text.FontStack = Stack(Latin, Arabic, Hebrew);
            text.Text = value;
            text.ForceRebuild();
            return text;
        }

        [Test]
        public void EmptyTextHasACaret()
        {
            LetteraWorldText text = Make("");
            Assert.AreEqual(0, text.HitTestCaret(Vector2.zero, out _));
            Assert.IsTrue(text.TryGetCaretRect(0, LetteraCaretBias.Forward, out Rect rect));
            Assert.Greater(rect.height, 0);
            Assert.AreEqual(((RectTransform)text.transform).rect.xMin, rect.x, 0.01f);
            Assert.AreEqual(((RectTransform)text.transform).rect.yMax, rect.yMax, 10f);
            Assert.AreEqual(0, text.Lines.Count, "The caret must not change what the empty text measures.");
        }

        [Test]
        public void EmptyTextCaretFollowsAlignmentAndDirection()
        {
            LetteraWorldText text = Make("", 200, 100);
            text.HorizontalAlignment = LetteraHorizontalAlignment.Center;
            text.VerticalAlignment = LetteraVerticalAlignment.Middle;
            text.TryGetCaretRect(0, LetteraCaretBias.Forward, out Rect center);
            Assert.AreEqual(0, center.x, 0.01f);
            Assert.AreEqual(0, center.center.y, 10f);
            text.HorizontalAlignment = LetteraHorizontalAlignment.Start;
            text.BaseDirection = LetteraDirection.RightToLeft;
            text.TryGetCaretRect(0, LetteraCaretBias.Forward, out Rect rtl);
            Assert.AreEqual(100, rtl.x, 0.01f);
        }

        [Test]
        public void ACaretAfterATrailingBreakStandsBelow()
        {
            LetteraWorldText text = Make("ab\n");
            text.TryGetCaretRect(0, LetteraCaretBias.Forward, out Rect first);
            text.TryGetCaretRect(3, LetteraCaretBias.Forward, out Rect last);
            Assert.Greater(last.height, 0);
            Assert.Less(last.y, first.y - 5);
            Assert.AreEqual(first.x, last.x, 0.01f);
        }

        [Test]
        public void AnEmptyLineHoldsItsOwnCaret()
        {
            LetteraWorldText text = Make("abc\n\ndef");
            text.TryGetCaretRect(0, LetteraCaretBias.Forward, out Rect abc);
            text.TryGetCaretRect(4, LetteraCaretBias.Forward, out Rect empty);
            text.TryGetCaretRect(5, LetteraCaretBias.Forward, out Rect def);
            Assert.Less(empty.y, abc.y - 5);
            Assert.Less(def.y, empty.y - 5);
            Assert.AreEqual(1, text.GetLineIndex(4, LetteraCaretBias.Forward));
        }

        [Test]
        public void ASoftWrapHasTwoSpots()
        {
            LetteraWorldText text = Make("aaaa bbbb", 70);
            Assert.AreEqual(2, text.Lines.Count);
            Assert.AreEqual(5, text.Lines[1].Start);
            float end = CaretX(text, 5, LetteraCaretBias.Backward), start = CaretX(text, 5, LetteraCaretBias.Forward);
            Assert.Greater(end, start + 20);
            Assert.AreEqual(text.Lines[1].X, start, 0.01f);
            Assert.AreEqual(0, text.GetLineIndex(5, LetteraCaretBias.Backward));
            Assert.AreEqual(1, text.GetLineIndex(5, LetteraCaretBias.Forward));

            LetteraLine first = text.Lines[0], second = text.Lines[1];
            int hit = text.HitTestCaret(new Vector2(first.X + first.Width + 30, first.Baseline), out LetteraCaretBias bias);
            Assert.AreEqual(5, hit);
            Assert.AreEqual(LetteraCaretBias.Backward, bias);
            hit = text.HitTestCaret(new Vector2(second.X + 1, second.Baseline), out bias);
            Assert.AreEqual(5, hit);
            Assert.AreEqual(LetteraCaretBias.Forward, bias);
        }

        [Test]
        public void TrailingSpacesMoveTheCaret()
        {
            LetteraWorldText text = Make("ab  ");
            float atB = CaretX(text, 2, LetteraCaretBias.Forward), one = CaretX(text, 3, LetteraCaretBias.Forward), two = CaretX(text, 4, LetteraCaretBias.Forward);
            Assert.Greater(one, atB + 1);
            Assert.Greater(two, one + 1);
        }

        [Test]
        public void RtlCaretAtTheEndStandsOnTheLeftOfTheLastLetter()
        {
            LetteraWorldText text = Mixed("שלום");
            Assert.AreEqual(4, text.CleanText.Length);
            LetteraLine line = text.Lines[0];
            Assert.AreEqual(line.X, CaretX(text, 4, LetteraCaretBias.Forward), 1f);
            Assert.AreEqual(line.X + line.Width, CaretX(text, 0, LetteraCaretBias.Forward), 1f);
        }

        [Test]
        public void ADirectionChangeHasTwoSpotsAndARangeCanSplit()
        {
            LetteraWorldText text = Mixed("abc אבג def");
            float backward = CaretX(text, 4, LetteraCaretBias.Backward), forward = CaretX(text, 4, LetteraCaretBias.Forward);
            Assert.Greater(forward, backward + 5);

            var rects = new List<Rect>();
            text.GetRangeRects(2, 5, rects);
            Assert.AreEqual(2, rects.Count);
            Assert.AreEqual(CaretX(text, 2, LetteraCaretBias.Forward), rects[0].xMin, 0.01f);
            Assert.AreEqual(backward, rects[0].xMax, 0.01f);
            Assert.AreEqual(forward, rects[1].xMax, 0.01f);
            Assert.Greater(rects[1].xMin, rects[0].xMax + 5, "the unselected letters stand between the two");
        }

        [Test]
        public void ARangeCoversLinesAndStaysOnOneRectOnAPlainLine()
        {
            LetteraWorldText text = Make("aaaa bbbb", 70);
            var rects = new List<Rect>();
            text.GetRangeRects(1, 7, rects);
            Assert.AreEqual(2, rects.Count, string.Join(";", rects));
            Assert.Greater(rects[0].y, rects[1].y);
            Assert.AreEqual(text.Lines[0].Height, rects[0].height, 0.01f);
            Assert.IsTrue(text.TryGetRangeBounds(1, 7, out Rect bounds));
            Assert.AreEqual(rects[1].yMin, bounds.yMin, 0.01f);
            Assert.AreEqual(rects[0].yMax, bounds.yMax, 0.01f);
        }

        [Test]
        public void ASelectedEmptyLineStillShows()
        {
            LetteraWorldText text = Make("a\n\nb");
            var rects = new List<Rect>();
            text.GetRangeRects(2, 3, rects);
            Assert.AreEqual(1, rects.Count);
            Assert.Greater(rects[0].width, 1);
        }

        [Test]
        public void ALigatureSharesItsWidthBetweenItsLetters()
        {
            LetteraWorldText text = Make("fi");
            float a = CaretX(text, 0, LetteraCaretBias.Forward), b = CaretX(text, 1, LetteraCaretBias.Forward), c = CaretX(text, 2, LetteraCaretBias.Forward);
            Assert.Greater(b, a + 1);
            Assert.Greater(c, b + 1);
            LetteraLine line = text.Lines[0];
            Assert.AreEqual(1, text.HitTestCaret(new Vector2(b + 0.2f, line.Baseline), out _));
            Assert.AreEqual(0, text.HitTestCaret(new Vector2(a + 0.2f, line.Baseline), out _));
            Assert.AreEqual(2, text.HitTestCaret(new Vector2(c - 0.2f, line.Baseline), out _));
        }

        [Test]
        public void AQueryOnACleanTextRunsNoPass()
        {
            LetteraWorldText text = Make("<b>Some</b> text\nwith lines");
            int rebuilds = text.RebuildCount, parses = text.ParseCount;
            var rects = new List<Rect>();
            for (int i = 0; i < 20; i++)
            {
                text.TryGetCaretRect(i, LetteraCaretBias.Forward, out _);
                text.GetRangeRects(0, i, rects);
                text.HitTestCaret(new Vector2(i, i), out _);
            }
            Assert.AreEqual(rebuilds, text.RebuildCount);
            Assert.AreEqual(parses, text.ParseCount);
        }

        [Test]
        public void AQueryOnAChangedTextSeesTheNewTextAndTheRebuildStillReportsIt()
        {
            LetteraWorldText text = Make("a");
            text.Text = "aaaaaa";
            text.TryGetCaretRect(6, LetteraCaretBias.Forward, out Rect rect);
            Assert.Greater(rect.x, ((RectTransform)text.transform).rect.xMin + 20);
            text.ForceRebuild();
            LetteraChanges seen = text.Engine.LastChanges;
            Assert.IsTrue((seen & LetteraChanges.Text) != 0, seen.ToString());
            Assert.IsTrue((seen & LetteraChanges.Mesh) != 0, seen.ToString());
        }

        [Test]
        public void ElidedClustersHaveNoRectsAndNoStop()
        {
            LetteraWorldText text = Make("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", 100, 30);
            text.Overflow = LetteraOverflow.Ellipsis;
            text.ForceRebuild();
            var rects = new List<Rect>();
            text.GetRangeRects(20, 30, rects);
            Assert.AreEqual(0, rects.Count, string.Join(";", rects));
            text.TryGetCaretRect(25, LetteraCaretBias.Forward, out Rect inside);
            Assert.LessOrEqual(inside.x, ((RectTransform)text.transform).rect.xMax + 0.01f);
        }

        // ------------------------------------------------------------- navigator

        [Test]
        public void StopsSkipClusters()
        {
            int[] text = Cps("a\U0001F44D\U0001F3FDb");
            var stops = new List<int>();
            for (int i = 0; i < text.Length; i = LetteraTextNavigator.NextStop(text, i)) stops.Add(i);
            stops.Add(text.Length);
            CollectionAssert.AreEqual(new[] { 0, 1, 3, 4 }, stops);
            Assert.AreEqual(1, LetteraTextNavigator.PreviousStop(text, 3));
            Assert.IsFalse(LetteraTextNavigator.IsStop(text, 2));
            Assert.IsTrue(LetteraTextNavigator.IsStop(text, 3));
        }

        [TestCase("don't go", 1, 0, 5)]
        [TestCase("x don’t", 3, 2, 7)]
        [TestCase("a 3.14 b", 3, 2, 6)]
        [TestCase("1,000", 2, 0, 5)]
        [TestCase("foo_bar", 4, 0, 7)]
        [TestCase("foo.bar", 1, 0, 7)]
        [TestCase("a  b", 1, 1, 3)]
        [TestCase("a  b", 2, 1, 3)]
        [TestCase("a!!!b", 2, 1, 4)]
        [TestCase("日本語", 1, 1, 2)]
        [TestCase("カタカナ", 1, 0, 4)]
        [TestCase("สวัสดี x", 2, 0, 6)]
        [TestCase("ab", 2, 0, 2)]
        [TestCase("ab\ncd", 2, 0, 2)]
        public void WordsAreWhatAPersonCallsWords(string value, int index, int start, int end)
        {
            LetteraTextNavigator.WordAt(Cps(value), index, out int a, out int b);
            Assert.AreEqual((start, end), (a, b));
        }

        [Test]
        public void WordStopsFollowThePlatform()
        {
            int[] text = Cps("foo bar  baz");
            Assert.AreEqual(4, LetteraTextNavigator.NextWordStop(text, 0));
            Assert.AreEqual(9, LetteraTextNavigator.NextWordStop(text, 4));
            Assert.AreEqual(12, LetteraTextNavigator.NextWordStop(text, 9));
            Assert.AreEqual(3, LetteraTextNavigator.NextWordStop(text, 0, true));
            Assert.AreEqual(7, LetteraTextNavigator.NextWordStop(text, 3, true));
            Assert.AreEqual(9, LetteraTextNavigator.PreviousWordStop(text, 12));
            Assert.AreEqual(4, LetteraTextNavigator.PreviousWordStop(text, 9));
            Assert.AreEqual(4, LetteraTextNavigator.PreviousWordStop(text, 6));
            Assert.AreEqual(0, LetteraTextNavigator.PreviousWordStop(text, 1));
        }

        [Test]
        public void WordStopsCrossParagraphsOneAtATime()
        {
            int[] text = Cps("ab\ncd");
            Assert.AreEqual(2, LetteraTextNavigator.NextWordStop(text, 0));
            Assert.AreEqual(3, LetteraTextNavigator.NextWordStop(text, 2));
            Assert.AreEqual(2, LetteraTextNavigator.PreviousWordStop(text, 3));
            Assert.AreEqual(0, LetteraTextNavigator.PreviousWordStop(text, 2));
        }

        [Test]
        public void ParagraphIncludesItsBreak()
        {
            int[] text = Cps("ab\ncd");
            LetteraTextNavigator.ParagraphAt(text, 1, out int start, out int content, out int end);
            Assert.AreEqual((0, 2, 3), (start, content, end));
            LetteraTextNavigator.ParagraphAt(text, 4, out start, out content, out end);
            Assert.AreEqual((3, 5, 5), (start, content, end));
        }

        [Test]
        public void TheNavigatorIsFreeAfterWarmUp()
        {
            int[] text = Cps("Some words and more words here");
            LetteraTextNavigator.NextWordStop(text, 0);
            long before = GC.GetTotalMemory(false);
            for (int i = 0; i < 10000; i++)
            {
                LetteraTextNavigator.NextWordStop(text, i % text.Length);
                LetteraTextNavigator.PreviousStop(text, i % text.Length);
            }
            Assert.Less(GC.GetTotalMemory(false) - before, 16 * 1024);
        }
    }
}

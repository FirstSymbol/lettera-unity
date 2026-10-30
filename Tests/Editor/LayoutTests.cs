using NUnit.Framework;
using UnityEngine;

namespace FirstSymbol.Lettera.Editor.Tests
{
    public class LayoutTests : TextFixture
    {
        const string Sentence = "The quick brown fox jumps over the lazy dog";

        [Test]
        public void WrapBreaksAtWords()
        {
            LetteraWorldText text = Make(Sentence, width: 150);
            Assert.Greater(text.Lines.Count, 1);
            foreach (LetteraLine line in text.Lines) Assert.LessOrEqual(line.Width, 150.01f);
        }

        [Test]
        public void NoWrapKeepsOneLine()
        {
            LetteraWorldText text = Make(Sentence, width: 150);
            text.WordWrap = false;
            text.ForceRebuild();
            Assert.AreEqual(1, text.Lines.Count);
            Assert.Greater(text.Lines[0].Width, 150);
        }

        [Test]
        public void LongWordBreaksAtClustersWhenNothingElseFits()
        {
            LetteraWorldText text = Make(new string('m', 40), width: 100);
            Assert.Greater(text.Lines.Count, 1);
            foreach (LetteraLine line in text.Lines) Assert.LessOrEqual(line.Width, 100.01f);
        }

        [Test]
        public void TrailingWhitespaceHangs()
        {
            float plain = WidthOf(Make("word"));
            float spaced = WidthOf(Make("word     "));
            Assert.AreEqual(plain, spaced, 0.01f);
        }

        [Test]
        public void HardBreaksMakeLines()
        {
            Assert.AreEqual(3, Make("a\nb\r\nc").Lines.Count);
            Assert.AreEqual(2, Make("a<br>b").Lines.Count);
            Assert.AreEqual(2, Make("a\n").Lines.Count, "a trailing separator ends with an empty line");
            Assert.AreEqual(0, Make("").Lines.Count);
        }

        [TestCase(LetteraHorizontalAlignment.Start, -200f)]
        [TestCase(LetteraHorizontalAlignment.Left, -200f)]
        public void LeftAligned(LetteraHorizontalAlignment alignment, float expected)
        {
            LetteraWorldText text = Make("abc", width: 400);
            text.HorizontalAlignment = alignment;
            text.ForceRebuild();
            Assert.AreEqual(expected, text.Lines[0].X, 0.01f);
        }

        [Test]
        public void CenterAndRightAlign()
        {
            LetteraWorldText text = Make("abc", width: 400);
            float width = text.Lines[0].Width;
            text.HorizontalAlignment = LetteraHorizontalAlignment.Center;
            text.ForceRebuild();
            Assert.AreEqual(-width * 0.5f, text.Lines[0].X, 0.01f);
            text.HorizontalAlignment = LetteraHorizontalAlignment.Right;
            text.ForceRebuild();
            Assert.AreEqual(200 - width, text.Lines[0].X, 0.01f);
        }

        [Test]
        public void JustifyStretchesAllButTheLastLine()
        {
            LetteraWorldText text = Make(Sentence, width: 150);
            text.HorizontalAlignment = LetteraHorizontalAlignment.Justify;
            text.ForceRebuild();
            int firstLineGlyphs = 0;
            float lastX = 0, lastAdvance = 0;
            for (int i = 0; i < text.GlyphCount; i++)
            {
                LetteraGlyphInfo g = text.GetGlyph(i);
                if (g.Line != 0) continue;
                firstLineGlyphs++;
                lastX = g.X;
                lastAdvance = g.Advance;
            }
            Assert.Greater(firstLineGlyphs, 0);
            Assert.AreEqual(75f, lastX + lastAdvance, 1f, "the first line reaches the right edge");
            LetteraLine last = text.Lines[text.Lines.Count - 1];
            Assert.AreEqual(-75f, last.X, 0.01f, "the last line stays start-aligned");
        }

        [Test]
        public void VerticalAlignmentMovesTheBlock()
        {
            LetteraWorldText text = Make("abc", width: 400, height: 400);
            float top = text.Lines[0].Baseline;
            text.VerticalAlignment = LetteraVerticalAlignment.Bottom;
            text.ForceRebuild();
            float bottom = text.Lines[0].Baseline;
            text.VerticalAlignment = LetteraVerticalAlignment.Middle;
            text.ForceRebuild();
            float middle = text.Lines[0].Baseline;
            Assert.Less(bottom, middle);
            Assert.Less(middle, top);
            Assert.AreEqual((top + bottom) * 0.5f, middle, 0.01f);
        }

        [Test]
        public void PaddingShrinksTheBox()
        {
            LetteraWorldText text = Make("abc", width: 400);
            text.Padding = new Vector4(50, 0, 0, 0);
            text.ForceRebuild();
            Assert.AreEqual(-150f, text.Lines[0].X, 0.01f);
        }

        [Test]
        public void AutoSizeFindsTheLargestFit()
        {
            LetteraWorldText text = Make("Fits", width: 200, height: 100);
            text.AutoSize = true;
            text.MinFontSize = 5;
            text.MaxFontSize = 500;
            text.ForceRebuild();
            float size = text.CurrentFontSize;
            Assert.Greater(size, 20);
            Assert.Less(size, 500);
            Assert.LessOrEqual(WidthOf(text), 200.01f);
            text.MaxFontSize = size + 2;
            text.ForceRebuild();
            Assert.AreEqual(size, text.CurrentFontSize, 0.02f, "the search converges on one value");
        }

        [Test]
        public void AutoSizeShrinksRatherThanSplittingAWord()
        {
            LetteraWorldText text = Make("Extraordinarily", width: 100, height: 400);
            text.AutoSize = true;
            text.MinFontSize = 2;
            text.MaxFontSize = 100;
            text.ForceRebuild();
            Assert.AreEqual(1, text.Lines.Count);
            Assert.LessOrEqual(WidthOf(text), 100.01f);
        }

        [Test]
        public void AutoSizeGrowsWithTheRect()
        {
            LetteraWorldText text = Make("Grow", width: 100, height: 100);
            text.AutoSize = true;
            text.MinFontSize = 2;
            text.MaxFontSize = 300;
            text.ForceRebuild();
            float small = text.CurrentFontSize;
            ((RectTransform)text.transform).sizeDelta = new Vector2(300, 300);
            text.SetDirty(DirtyLevel.Layout);
            text.ForceRebuild();
            Assert.Greater(text.CurrentFontSize, small * 1.5f);
        }

        LetteraWorldText Elided(string content, float width, float position, LetteraOverflow overflow = LetteraOverflow.Ellipsis)
        {
            LetteraWorldText text = Make(content, width: width, height: 100);
            text.WordWrap = false;
            text.Overflow = overflow;
            text.OverflowPosition = position;
            text.ForceRebuild();
            return text;
        }

        [Test]
        public void EllipsisAtEndKeepsTheStart()
        {
            LetteraWorldText text = Elided(Sentence, 150, 1);
            Assert.LessOrEqual(WidthOf(text), 150.01f);
            Assert.AreEqual(0, text.GetGlyph(0).Cluster);
            Assert.Less(text.GlyphCount, Sentence.Length);
        }

        [Test]
        public void EllipsisAtStartKeepsTheEnd()
        {
            LetteraWorldText text = Elided(Sentence, 150, 0);
            Assert.LessOrEqual(WidthOf(text), 150.01f);
            Assert.AreEqual(Sentence.Length - 1, text.GetGlyph(text.GlyphCount - 1).Cluster);
        }

        [Test]
        public void EllipsisInTheMiddleKeepsBothEnds()
        {
            LetteraWorldText text = Elided(Sentence, 150, 0.5f);
            Assert.LessOrEqual(WidthOf(text), 150.01f);
            Assert.AreEqual(0, text.GetGlyph(0).Cluster);
            Assert.AreEqual(Sentence.Length - 1, text.GetGlyph(text.GlyphCount - 1).Cluster);
        }

        [Test]
        public void EllipsisRemovesNothingWhenTextFits()
        {
            LetteraWorldText text = Elided("Short", 400, 1);
            Assert.AreEqual(5, text.GlyphCount);
        }

        [Test]
        public void TruncateFitsWithoutAMarker()
        {
            LetteraWorldText marker = Elided(Sentence, 150, 1);
            LetteraWorldText none = Elided(Sentence, 150, 1, LetteraOverflow.Truncate);
            Assert.LessOrEqual(WidthOf(none), 150.01f);
            Assert.Greater(none.GlyphCount, marker.GlyphCount - 2, "without the marker at least as much text remains");
        }

        [Test]
        public void LargerBoxNeverShowsLess()
        {
            int previous = 0;
            for (float width = 60; width < 400; width += 20)
            {
                int count = Elided(Sentence, width, 1).GlyphCount;
                Assert.GreaterOrEqual(count, previous, $"width {width}");
                previous = count;
            }
        }

        [Test]
        public void EllipsisHonoursHeightWithWrap()
        {
            LetteraWorldText text = Make(Sentence + " " + Sentence, width: 150, height: 60);
            text.Overflow = LetteraOverflow.Ellipsis;
            text.ForceRebuild();
            float total = 0;
            foreach (LetteraLine line in text.Lines) total += line.Height;
            Assert.LessOrEqual(total, 60.01f);
        }

        [Test]
        public void ScaledLineHeightFollowsTheLargestSize()
        {
            LetteraWorldText text = Make("a");
            text.LineHeightMode = LetteraLineHeightMode.Scaled;
            text.LineHeightValue = 2f;
            text.ForceRebuild();
            Assert.AreEqual(40f, text.Lines[0].Height, 0.01f);
            text.Text = "a<size=200%>b</size>";
            text.ForceRebuild();
            Assert.AreEqual(80f, text.Lines[0].Height, 0.01f);
        }

        [Test]
        public void ContentAndPrimaryHeightsUseFontMetrics()
        {
            LetteraWorldText text = Make("a");
            text.LineHeightMode = LetteraLineHeightMode.Primary;
            text.LineHeightValue = 1f;
            text.ForceRebuild();
            float primary = text.Lines[0].Height;
            Assert.Greater(primary, 20f, "Noto's ascent and descent exceed the em");
            text.LineHeightValue = 0.5f;
            text.ForceRebuild();
            Assert.AreEqual(primary * 0.5f, text.Lines[0].Height, 0.01f, "small values are honoured, not clamped");
        }

        [Test]
        public void MeasureMatchesLayout()
        {
            LetteraWorldText text = Make(Sentence, width: 150);
            Vector2 measured = text.MeasureText();
            float height = 0;
            foreach (LetteraLine line in text.Lines) height += line.Height;
            Assert.AreEqual(WidthOf(text), measured.x, 0.01f);
            Assert.AreEqual(height, measured.y, 0.01f);
            Assert.AreEqual(height, text.GetHeightForWidth(150), 0.01f);
        }

        [Test]
        public void MeasureDoesNotChangeWhatIsShown()
        {
            LetteraWorldText text = Make("abc");
            int glyphs = text.GlyphCount;
            text.MeasureText(new LetteraMeasureOptions { Text = "something much longer" });
            Assert.AreEqual(glyphs, text.GlyphCount);
        }

        [Test]
        public void ContentWidths()
        {
            LetteraWorldText text = Make("aaa bbbbbb");
            Assert.Less(text.GetMinContentWidth(), text.GetMaxContentWidth());
            Assert.AreEqual(WidthOf(Make("bbbbbb")), text.GetMinContentWidth(), 0.01f);
        }

        [Test]
        public void HitTestFindsGlyphAndCaret()
        {
            LetteraWorldText text = Make("abcd", width: 400);
            LetteraGlyphInfo c = text.GetGlyph(2);
            float baseline = text.Lines[0].Baseline;
            Assert.IsTrue(text.HitTestGlyph(new Vector2(c.X + c.Advance * 0.5f, baseline + 2), 0f, out LetteraHit hit));
            Assert.AreEqual(2, hit.ClusterStart);
            Assert.AreEqual(3, hit.ClusterEnd);
            Assert.IsFalse(text.HitTestGlyph(new Vector2(c.X, baseline + 5000), 1f, out _));
        }

        [Test]
        public void DegenerateRectStillLaysOut()
        {
            LetteraWorldText text = Make("Centered", width: 0, height: 0);
            text.HorizontalAlignment = LetteraHorizontalAlignment.Center;
            text.ForceRebuild();
            Assert.AreEqual(1, text.Lines.Count);
            Assert.AreEqual(-text.Lines[0].Width * 0.5f, text.Lines[0].X, 0.01f);
        }
    }
}

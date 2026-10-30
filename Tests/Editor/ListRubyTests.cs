using System.Diagnostics;
using System.Text;
using NUnit.Framework;
using UnityEngine;

namespace FirstSymbol.Lettera.Editor.Tests
{
    public class ListRubyTests : TextFixture
    {
        const float Tol = 0.01f;

        static int VirtualQuads(LetteraWorldText text) => text.Engine.VertexCount / 4 - CountDrawable(text);

        static int CountDrawable(LetteraWorldText text)
        {
            int count = 0;
            for (int i = 0; i < text.GlyphCount; i++)
                if (!char.IsWhiteSpace(text.CleanText[Mathf.Min(text.GetGlyph(i).Cluster, text.CleanText.Length - 1)]))
                    count++;
            return count;
        }

        LetteraWorldText Ordered(string markup, LetteraListPlacement placement = LetteraListPlacement.Inside, float width = 1000)
        {
            LetteraWorldText text = Make("", width);
            text.UseGlobalStylePreset = false;
            text.Styles.Add(LetteraStyle.ForMatcher(new ListItemMatcher(), new ListEffect { Ordered = true, Placement = placement }));
            text.MarkStylesDirty();
            text.Text = markup;
            text.ForceRebuild();
            return text;
        }

        // ------------------------------------------------------------- lists

        [Test]
        public void ItemsAreParagraphsAndMarkersAreNotText()
        {
            LetteraWorldText text = Ordered("<li>a</li><li>b</li><li>c</li>");
            Assert.AreEqual(3, text.Lines.Count);
            Assert.AreEqual("a\nb\nc", text.CleanText);
            Assert.AreEqual(3, text.GlyphCount, "The letters, no marker.");
            Assert.Greater(VirtualQuads(text), 0, "Markers are in the mesh.");
        }

        [Test]
        public void ItemsCountUp()
        {
            LetteraWorldText text = Ordered("<li>a</li>\n<li>b</li>\n<li>c</li>");
            // Each marker is a digit and a period, so three items draw six marker glyphs.
            Assert.AreEqual(6, VirtualQuads(text));
        }

        [Test]
        public void NumberSlotSetsTheCounter()
        {
            LetteraWorldText text = Ordered("<li=0,9>a</li>\n<li>b</li>");
            Assert.AreEqual(5, VirtualQuads(text), "9. is two glyphs, 10. is three.");
        }

        [Test]
        public void ContentStartsAfterTheMarker()
        {
            LetteraWorldText plain = Make("a");
            LetteraWorldText text = Make("<li>a</li>");
            Assert.Greater(text.GetGlyph(0).X, plain.GetGlyph(0).X + 2);
        }

        [Test]
        public void NestedLevelIndentsFurther()
        {
            LetteraWorldText text = Make("<li>a</li>\n<li=1>b</li>");
            Assert.Greater(text.Lines[1].X, text.Lines[0].X + 2);
        }

        [Test]
        public void OutsideItemsShareAColumn()
        {
            var markup = new StringBuilder();
            for (int i = 0; i < 12; i++) markup.Append("<li>a</li>\n");
            markup.Length -= 1;
            LetteraWorldText text = Ordered(markup.ToString(), LetteraListPlacement.Outside);
            Assert.AreEqual(text.Lines[0].X, text.Lines[11].X, Tol, "1. and 10. start their text at one x.");
        }

        [Test]
        public void InsideItemsMoveWithTheirMarker()
        {
            var markup = new StringBuilder();
            for (int i = 0; i < 12; i++) markup.Append("<li>a</li>\n");
            markup.Length -= 1;
            LetteraWorldText text = Ordered(markup.ToString());
            Assert.Greater(text.Lines[11].X, text.Lines[0].X + 1);
        }

        [Test]
        public void WrappedLinesAlignWithTheContent()
        {
            LetteraWorldText text = Make("<li>aaaa aaaa aaaa aaaa</li>", 100);
            Assert.Greater(text.Lines.Count, 1);
            Assert.AreEqual(text.Lines[0].X, text.Lines[1].X, Tol);
        }

        [Test]
        public void RtlMarkersGoOnTheRight()
        {
            LetteraWorldText text = Make("<li>א</li>", 300, 100, Hebrew);
            TextEngine e = text.Engine;
            float rightmost = float.MinValue, glyph = text.GetGlyph(0).X;
            for (int v = 0; v < e.VertexCount; v++) rightmost = Mathf.Max(rightmost, e.Vertices[v].x);
            Assert.Greater(rightmost, glyph + text.GetGlyph(0).Advance + 1);
        }

        [Test]
        public void ThePlainParagraphEndsTheList()
        {
            LetteraWorldText text = Ordered("<li>a</li>\nplain\n<li>b</li>");
            Assert.AreEqual(4, VirtualQuads(text), "Both are 1. since the list restarted.");
        }

        [Test]
        public void MarkerTextReplacesTheBullet()
        {
            LetteraWorldText text = Make("<li=0,,\"→\">a</li>", 300, 100, Latin);
            Assert.DoesNotThrow(() => text.ForceRebuild());
        }

        [Test]
        public void MarkdownListFeedsTheEffect()
        {
            LetteraWorldText text = Make("", 500);
            text.Styles.Add(LetteraStyle.ForMatcher(new MarkdownListMatcher(), new ListEffect()));
            text.MarkStylesDirty();
            text.Text = "3. x\n4. y";
            text.ForceRebuild();
            Assert.AreEqual("x\ny", text.CleanText);
            Assert.AreEqual(4, VirtualQuads(text), "3. and 4. are two glyphs each.");
        }

        [Test]
        public void HiddenItemsLoseTheirMarkers()
        {
            LetteraWorldText all = Make("<li>a</li>\n<li>b</li>", 300, 500);
            int markers = VirtualQuads(all);
            LetteraWorldText text = Make("<li>a</li>\n<li>b</li>", 300, 500);
            text.AutoSize = false;
            Assert.AreEqual(markers, VirtualQuads(text));
        }

        [Test]
        public void HitTestDoesNotFindAMarker()
        {
            LetteraWorldText text = Make("<li>a</li>");
            Rect bounds = text.Engine.LayoutBounds;
            Assert.IsTrue(text.Engine.HitTestGlyph(new Vector2(bounds.xMin - 100, bounds.center.y), 1000, out LetteraHit hit));
            Assert.AreEqual(0, hit.GlyphIndex);
        }

        [Test]
        public void AutoSizeCountsTheMargin()
        {
            LetteraWorldText plain = Make("aaaaaaaaaa", 200, 400);
            plain.AutoSize = true;
            plain.MaxFontSize = 72;
            plain.MinFontSize = 5;
            plain.WordWrap = false;
            plain.ForceRebuild();
            LetteraWorldText listed = Make("<li>aaaaaaaaaa</li>", 200, 400);
            listed.AutoSize = true;
            listed.MaxFontSize = 72;
            listed.MinFontSize = 5;
            listed.WordWrap = false;
            listed.ForceRebuild();
            Assert.Less(listed.CurrentFontSize, plain.CurrentFontSize);
        }

        // ------------------------------------------------------------- ruby

        LetteraWorldText Kanji(string markup)
        {
            // Latin stands in for the base and reading, since the test fonts have no kanji.
            LetteraWorldText text = Make(markup);
            return text;
        }

        [Test]
        public void ShorthandKeepsTheReadingOutOfTheText()
        {
            LetteraWorldText text = Kanji("<ruby=abc>XY</ruby>");
            Assert.AreEqual("XY", text.CleanText);
            Assert.AreEqual(2, text.GlyphCount);
            Assert.AreEqual(5, text.Engine.VertexCount / 4, "Two letters and a three-letter reading.");
        }

        [Test]
        public void ReadingIsSmallerAndAbove()
        {
            LetteraWorldText text = Kanji("<ruby=a>XXXX</ruby>");
            TextEngine e = text.Engine;
            float baseTop = float.MinValue;
            for (int v = 0; v < 16; v++) baseTop = Mathf.Max(baseTop, e.Vertices[v].y);
            int reading = e.VertexCount - 4;
            Assert.Greater(e.Vertices[reading].y, text.GetGlyph(0).Y, "Above the baseline of the base.");
            float readingHeight = e.Vertices[reading + 1].y - e.Vertices[reading].y;
            Assert.Less(readingHeight, baseTop - e.Vertices[0].y, "Smaller than the base.");
        }

        [Test]
        public void OneReadingPerBasePart()
        {
            LetteraWorldText text = Kanji("<ruby>X<rt>a</rt>Y<rt>b</rt></ruby>");
            Assert.AreEqual("XY", text.CleanText);
            Assert.AreEqual(4, text.Engine.VertexCount / 4);
        }

        [Test]
        public void ParenthesesInRpGoAway()
        {
            LetteraWorldText text = Kanji("<ruby>X<rp>(</rp><rt>a</rt><rp>)</rp></ruby>");
            Assert.AreEqual("X", text.CleanText);
        }

        [Test]
        public void UnclosedReadingEndsAtTheRubyEnd()
        {
            LetteraWorldText text = Kanji("<ruby>X<rt>a</ruby>");
            Assert.AreEqual("X", text.CleanText);
            Assert.AreEqual(2, text.Engine.VertexCount / 4);
        }

        [Test]
        public void ReadingsOnALineShareABaseline()
        {
            LetteraWorldText text = Kanji("<ruby=a>X</ruby><size=200%><ruby=a>X</ruby></size>");
            TextEngine e = text.Engine;
            int a = e.VertexCount - 8, b = e.VertexCount - 4;
            float yA = e.Vertices[a].y, yB = e.Vertices[b].y;
            Assert.AreEqual(yA, yB, 4f, "Both readings hang from one line, though their sizes differ.");
            Assert.AreEqual(1, text.Lines.Count);
        }

        [Test]
        public void LineGrowsForTheReading()
        {
            Assert.Greater(Kanji("<ruby=a>X</ruby>").Lines[0].Height, Kanji("X").Lines[0].Height);
            Assert.AreEqual(Kanji("<ruby=a>X</ruby>").Lines[0].Height, Kanji("<ruby=a>X</ruby>").Engine.GetHeightForWidth(1000), Tol);
        }

        [Test]
        public void UnderPlacesBelow()
        {
            LetteraWorldText text = Make("");
            text.Styles.Add(LetteraStyle.ForMatcher(new RubyMatcher { Name = "ru" }, new RubyEffect { Position = LetteraRubyPosition.Under }));
            text.MarkStylesDirty();
            text.Text = "<ru=a>XXXX</ru>";
            text.ForceRebuild();
            TextEngine e = text.Engine;
            Assert.Less(e.Vertices[e.VertexCount - 4].y, text.GetGlyph(0).Y);
        }

        [Test]
        public void WideReadingWidensTheBase()
        {
            float plain = WidthOf(Kanji("X"));
            LetteraWorldText text = Kanji("<ruby=aaaaaaaaaaaaaaaaaaaa>X</ruby>");
            Assert.Greater(WidthOf(text), plain + 5);
        }

        [Test]
        public void ArabicReadingJoins()
        {
            LetteraWorldText text = Make("");
            text.Font = null;
            text.FontStack = Stack(Latin, Arabic);
            text.Text = "<ruby=سلم>XY</ruby>";
            text.ForceRebuild();
            LetteraWorldText alone = Make("سلم", 1000, 500, Arabic);
            Assert.AreEqual(alone.GlyphCount + 2, text.Engine.VertexCount / 4);
            Assert.AreEqual("XY", text.CleanText);
        }

        [Test]
        public void TextWithNoCloserStaysLiteral()
        {
            Assert.AreEqual("<ruby>a", Kanji("<ruby>a").CleanText);
        }

        [Test]
        public void EmptyRubyDisappears() => Assert.AreEqual("", Kanji("<ruby></ruby>").CleanText);

        [Test]
        public void MalformedRubyTerminatesQuickly()
        {
            var markup = new StringBuilder();
            for (int i = 0; i < 10000; i++) markup.Append("<ruby>");
            var clock = Stopwatch.StartNew();
            LetteraWorldText text = Make(markup.ToString());
            clock.Stop();
            Assert.Greater(text.CleanText.Length, 0);
            Assert.Less(clock.ElapsedMilliseconds, 2000);
        }
    }
}

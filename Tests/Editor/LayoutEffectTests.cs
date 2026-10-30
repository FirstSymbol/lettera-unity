using NUnit.Framework;
using UnityEngine;

namespace FirstSymbol.Lettera.Editor.Tests
{
    public class LayoutEffectTests : TextFixture
    {
        const float Tol = 0.01f;

        static float X(LetteraWorldText text, int glyph) => text.GetGlyph(glyph).X;

        // ------------------------------------------------------------- tracking

        [Test]
        public void TrackingAddsAfterEachClusterButTheLast()
        {
            LetteraWorldText plain = Make("abc");
            LetteraWorldText text = Make("<tracking=5>abc</tracking>");
            Assert.AreEqual(X(plain, 1) + 5, X(text, 1), Tol);
            Assert.AreEqual(X(plain, 2) + 10, X(text, 2), Tol);
            Assert.AreEqual(WidthOf(plain) + 10, WidthOf(text), Tol, "The last cluster's tracking hangs out of the line.");
        }

        [Test]
        public void TrackingInEmScalesWithTheSize()
        {
            LetteraWorldText plain = Make("ab", size: 40);
            LetteraWorldText text = Make("<tracking=0.1em>ab</tracking>", size: 40);
            Assert.AreEqual(X(plain, 1) + 4, X(text, 1), Tol);
        }

        [Test]
        public void NegativeTrackingTightens()
        {
            LetteraWorldText plain = Make("abc");
            LetteraWorldText text = Make("<tracking=-1>abc</tracking>");
            Assert.AreEqual(WidthOf(plain) - 2, WidthOf(text), Tol);
        }

        [Test]
        public void TrackingBreaksLigatures() => Assert.AreEqual(2, Make("<tracking=1>fi</tracking>").GlyphCount);

        [Test]
        public void TrackingMovesWrapPoints()
        {
            float width = WidthOf(Make("aaaa aaaa")) + 2;
            Assert.AreEqual(1, Make("aaaa aaaa", width).Lines.Count);
            Assert.AreEqual(2, Make("<tracking=5>aaaa aaaa</tracking>", width).Lines.Count);
        }

        [Test]
        public void MeasureMatchesTrackedWidth()
        {
            LetteraWorldText text = Make("<tracking=5>abcd</tracking>");
            Assert.AreEqual(WidthOf(text), text.MeasureText(new LetteraMeasureOptions { MaxWidth = 1000 }).x, Tol);
        }

        [Test]
        public void ArabicKeepsItsJoinsAndGetsConnectingStrokes()
        {
            const string word = "سلم";
            LetteraWorldText plain = Make(word, font: Arabic);
            LetteraWorldText text = Make("<tracking=10>" + word + "</tracking>", font: Arabic);
            CollectionAssert.AreEqual(Glyphs(plain), Glyphs(text), "Every joining form stays.");
            Assert.Greater(text.Engine.VertexCount / 4, plain.Engine.VertexCount / 4, "Strokes are in the mesh.");
            Assert.AreEqual(plain.GlyphCount, text.GlyphCount, "Strokes are not glyphs of the text.");
            Assert.AreEqual(WidthOf(plain) + 20, WidthOf(text), Tol);
        }

        [Test]
        public void HitTestOverAConnectingStrokeFindsALetter()
        {
            LetteraWorldText text = Make("<tracking=10>سلم</tracking>", font: Arabic);
            Rect bounds = text.Engine.LayoutBounds;
            Assert.IsTrue(text.Engine.HitTestGlyph(new Vector2(bounds.center.x, bounds.center.y), 1000, out LetteraHit hit));
            Assert.GreaterOrEqual(hit.GlyphIndex, 0);
            Assert.Less(hit.GlyphIndex, text.GlyphCount);
        }

        [Test]
        public void NegativeTrackingLeavesJoinedLettersAlone()
        {
            const string word = "سلم";
            Assert.AreEqual(WidthOf(Make(word, font: Arabic)), WidthOf(Make("<tracking=-3>" + word + "</tracking>", font: Arabic)), Tol);
        }

        [Test]
        public void TrackingInRtlGoesOnTheVisualLeft()
        {
            LetteraWorldText plain = Make("אב", font: Hebrew);
            LetteraWorldText text = Make("<tracking=5>אב</tracking>", font: Hebrew);
            float gapPlain = X(plain, 1) - X(plain, 0), gap = X(text, 1) - X(text, 0);
            Assert.AreEqual(5f, Mathf.Abs(gap) - Mathf.Abs(gapPlain), Tol);
        }

        // ------------------------------------------------------------- word spacing

        [Test]
        public void WordSpacingWidensTheSpaceOnly()
        {
            LetteraWorldText plain = Make("a b", size: 40);
            LetteraWorldText text = Make("<word-spacing=0.3em>a b</word-spacing>", size: 40);
            Assert.AreEqual(X(plain, 2) + 12, X(text, 2), Tol);
            Assert.AreEqual(X(plain, 1), X(text, 1), Tol);
        }

        [Test]
        public void OtherSpacesDoNotChange()
        {
            Assert.AreEqual(WidthOf(Make("a b")), WidthOf(Make("<word-spacing=5>a b</word-spacing>")), Tol);
            Assert.AreEqual(WidthOf(Make("a\tb")), WidthOf(Make("<word-spacing=5>a\tb</word-spacing>")), Tol);
        }

        [Test]
        public void WordSpacingCanOverlap() => Assert.DoesNotThrow(() => Make("<word-spacing=-500>a b</word-spacing>"));

        [Test]
        public void WordAndTrackingAddUp()
        {
            LetteraWorldText plain = Make("a b");
            LetteraWorldText text = Make("<tracking=1><word-spacing=2>a b</word-spacing></tracking>");
            Assert.AreEqual(X(plain, 2) + 4, X(text, 2), Tol);
        }

        // ------------------------------------------------------------- cells

        [Test]
        public void AutoCellMeasuresTheWidestGlyph()
        {
            LetteraWorldText plain = Make("iW");
            LetteraWorldText text = Make("<cell=auto>iW</cell>");
            float w = plain.GetGlyph(1).Advance;
            Assert.AreEqual(2 * w, WidthOf(text), Tol);
            float i = plain.GetGlyph(0).Advance;
            Assert.AreEqual((w - i) * 0.5f, X(text, 0) - text.Engine.LayoutBounds.xMin, 0.5f, "The narrow glyph is centered.");
        }

        [Test]
        public void FixedCellInEm() => Assert.AreEqual(40f, WidthOf(Make("<cell=1em>iW</cell>", size: 20)), Tol);

        [Test]
        public void TrackingAddsOnTopOfACell()
        {
            LetteraWorldText text = Make("<cell=auto><tracking=2>WW</tracking></cell>");
            Assert.AreEqual(2 * Make("W").GetGlyph(0).Advance + 2, WidthOf(text), Tol);
        }

        // ------------------------------------------------------------- line height, spacing, gaps

        [Test]
        public void ParagraphsCanHaveDifferentLineHeights()
        {
            LetteraWorldText text = Make("<line-height=1>a</line-height>\n<line-height=3>b</line-height>");
            Assert.Less(text.Lines[0].Height, text.Lines[1].Height);
            Assert.AreEqual(20f, text.Lines[0].Height, Tol);
            Assert.AreEqual(60f, text.Lines[1].Height, Tol);
        }

        [Test]
        public void SmallLineHeightOverlapsWithoutAClamp()
        {
            Assert.AreEqual(4f, Make("<line-height=0.2>a</line-height>").Lines[0].Height, Tol);
        }

        [Test]
        public void AbsoluteLineHeight()
        {
            Assert.AreEqual(33f, Make("<line-height=33px>a</line-height>").Lines[0].Height, Tol);
            Assert.AreEqual(40f, Make("<line-height=200%>a</line-height>").Lines[0].Height, Tol);
        }

        [Test]
        public void HeightForWidthMatchesPerRangeLineHeights()
        {
            LetteraWorldText text = Make("<line-height=3>a</line-height>");
            Assert.AreEqual(text.Lines[0].Height, text.Engine.GetHeightForWidth(1000), Tol);
        }

        [Test]
        public void ParagraphSpacingAddsBetweenParagraphsOnly()
        {
            LetteraWorldText plain = Make("a\nb\nc");
            LetteraWorldText text = Make("<para-spacing=12,5>a\nb\nc</para-spacing>");
            float plainGap = plain.Lines[0].Baseline - plain.Lines[1].Baseline;
            Assert.AreEqual(plainGap + 17, text.Lines[0].Baseline - text.Lines[1].Baseline, Tol);
            Assert.AreEqual(plainGap + 17, text.Lines[1].Baseline - text.Lines[2].Baseline, Tol);
            Assert.AreEqual(plain.Engine.GetHeightForWidth(1000) + 34, text.Engine.GetHeightForWidth(1000), Tol);
        }

        [Test]
        public void SingleParagraphHasNoSpacing()
        {
            Assert.AreEqual(Make("a").Engine.GetHeightForWidth(1000), Make("<para-spacing=12,5>a</para-spacing>").Engine.GetHeightForWidth(1000), Tol);
        }

        [Test]
        public void LineBreakIsNotAParagraph()
        {
            Assert.AreEqual(Make("a<br>b").Engine.GetHeightForWidth(1000), Make("<para-spacing=12>a<br>b</para-spacing>").Engine.GetHeightForWidth(1000), Tol);
        }

        [Test]
        public void ParagraphSpacingInEmAndNegative()
        {
            LetteraWorldText plain = Make("a\nb");
            float gap = plain.Lines[0].Baseline - plain.Lines[1].Baseline;
            LetteraWorldText em = Make("<para-spacing=0.5em>a\nb</para-spacing>");
            Assert.AreEqual(gap + 10, em.Lines[0].Baseline - em.Lines[1].Baseline, Tol);
            LetteraWorldText negative = Make("<para-spacing=-5>a\nb</para-spacing>");
            Assert.AreEqual(gap - 5, negative.Lines[0].Baseline - negative.Lines[1].Baseline, Tol);
        }

        [Test]
        public void LineGapMovesTheFollowingLine()
        {
            LetteraWorldText plain = Make("ab<br>c");
            LetteraWorldText text = Make("a<line-gap=20>b</line-gap><br>c");
            float gap = plain.Lines[0].Baseline - plain.Lines[1].Baseline;
            Assert.AreEqual(gap + 20, text.Lines[0].Baseline - text.Lines[1].Baseline, Tol);
        }

        [Test]
        public void LineGapAtTheEndAddsNothing()
        {
            Assert.AreEqual(Make("ab").Engine.GetHeightForWidth(1000), Make("<line-gap=20>ab</line-gap>").Engine.GetHeightForWidth(1000), Tol);
        }

        // ------------------------------------------------------------- no-break

        [Test]
        public void NoBreakMovesTheRangeWhole()
        {
            float width = WidthOf(Make("aaaabb")) + 1;
            LetteraWorldText text = Make("aaaa<nobr>bb cc</nobr>", width);
            Assert.AreEqual(4, text.Lines[1].Start);
        }

        [Test]
        public void ExplicitBreakInsideStillApplies() => Assert.AreEqual(2, Make("<nobr>a<br>b</nobr>").Lines.Count);

        [Test]
        public void TooWideNoBreakRangeStillSplits()
        {
            float width = WidthOf(Make("aaa"));
            LetteraWorldText text = Make("<nobr>aaaaaaaaa aaaaaa</nobr>", width);
            Assert.Greater(text.Lines.Count, 1);
            foreach (LetteraLine line in text.Lines) Assert.LessOrEqual(line.Width, width + 0.01f);
        }

        // ------------------------------------------------------------- indent

        [Test]
        public void IndentMovesTheLine()
        {
            Assert.AreEqual(X(Make("a"), 0) + 20, X(Make("<indent=20>a</indent>"), 0), Tol);
        }

        [Test]
        public void NestedIndentsAddUp()
        {
            Assert.AreEqual(X(Make("a"), 0) + 30, X(Make("<indent=20><indent=10>a</indent></indent>"), 0), Tol);
        }

        [Test]
        public void IndentOpenedMidLineShiftsWhatFollows()
        {
            Assert.AreEqual(X(Make("xy"), 1) + 20, X(Make("x<indent=20>y</indent>"), 1), Tol);
        }

        [Test]
        public void WrappedRangeIndentsEachLine()
        {
            LetteraWorldText text = Make("<indent=20>aaaa aaaa aaaa</indent>", 80);
            Assert.Greater(text.Lines.Count, 1);
            float start = text.Engine.LayoutBounds.xMin;
            foreach (LetteraLine line in text.Lines) Assert.AreEqual(20f, line.Margin, Tol);
            Assert.AreEqual(X(Make("a", 80), 0) + 20, X(text, 0), Tol);
            Assert.AreEqual(start, text.Lines[1].X, Tol);
        }

        [Test]
        public void PercentIndentIsOfTheRectWidth()
        {
            Assert.AreEqual(X(Make("a", 300), 0) + 30, X(Make("<indent=10%>a</indent>", 300), 0), Tol);
        }

        [Test]
        public void IndentNarrowsWhatFits()
        {
            float width = WidthOf(Make("aaaa aaaa")) + 2;
            Assert.AreEqual(1, Make("aaaa aaaa", width).Lines.Count);
            Assert.AreEqual(2, Make("<indent=20>aaaa aaaa</indent>", width).Lines.Count);
        }

        [Test]
        public void NegativeIndentStopsAtTheBoxEdge()
        {
            Assert.AreEqual(X(Make("a"), 0), X(Make("<indent=-50>a</indent>"), 0), Tol);
        }

        [Test]
        public void RtlIndentComesFromTheRight()
        {
            LetteraWorldText plain = Make("א", font: Hebrew);
            LetteraWorldText text = Make("<indent=20>א</indent>", font: Hebrew);
            Assert.AreEqual(X(plain, 0) - 20, X(text, 0), Tol);
        }

        // ------------------------------------------------------------- alignment

        [Test]
        public void ParagraphsTakeTheirOwnAlignment()
        {
            LetteraWorldText text = Make("<align=center>a</align>\n<align=right>b</align>", 300);
            LetteraLine center = text.Lines[0], right = text.Lines[1];
            Assert.AreEqual(150f, center.X + center.Width * 0.5f - text.Engine.LayoutBounds.xMin + (text.Engine.LayoutBounds.xMin + 150f), 200f);
            Assert.AreEqual(150f, right.X + right.Width, 1f, "Flush with the right edge of a rect centered on the origin.");
            Assert.AreEqual(0f, center.X + center.Width * 0.5f, 1f);
        }

        const string Sentence = "aaaa bbbb cccc dddd eeee ffff";

        [Test]
        public void DefaultBudgetsMatchPlainJustify()
        {
            LetteraWorldText plain = Make(Sentence, 200);
            plain.HorizontalAlignment = LetteraHorizontalAlignment.Justify;
            plain.ForceRebuild();
            LetteraWorldText text = Make("<align=justify>" + Sentence + "</align>", 200);
            Assert.AreEqual(plain.GlyphCount, text.GlyphCount);
            for (int i = 0; i < plain.GlyphCount; i++) Assert.AreEqual(X(plain, i), X(text, i), Tol, "glyph " + i);
        }

        [Test]
        public void LastLineCanJustify()
        {
            LetteraWorldText text = Make("<align=justify,auto,justify>aaa bbb</align>", 300);
            LetteraLine line = text.Lines[0];
            float last = text.GetGlyph(text.GlyphCount - 1).X + text.GetGlyph(text.GlyphCount - 1).Advance;
            Assert.AreEqual(150f, last, 1f, "The last line reaches the right edge.");
            Assert.AreEqual(-150f, line.X, 1f);
        }

        [Test]
        public void JustifyNoneTurnsStretchingOff()
        {
            LetteraWorldText plain = Make(Sentence, 200);
            LetteraWorldText text = Make("<align=justify,none>" + Sentence + "</align>", 200);
            for (int i = 0; i < plain.GlyphCount; i++) Assert.AreEqual(X(plain, i), X(text, i), Tol);
        }

        [Test]
        public void BudgetsSpendWordsThenLetters()
        {
            var align = new AlignEffect { Alignment = LetteraHorizontalAlignment.Justify, MaxWordSpace = 120, MaxTracking = 2 };
            LetteraWorldText text = Make("", 150);
            text.Styles.Add(LetteraStyle.ForWholeText(align));
            text.MarkStylesDirty();
            text.Text = "aa bb cccccccccccccccccccccccc";
            text.ForceRebuild();
            LetteraWorldText plain = Make("aa bb", 200);
            float letterGap = (X(text, 1) - X(text, 0)) - (X(plain, 1) - X(plain, 0));
            Assert.Greater(letterGap, 0f, "Letter spacing is used once word spaces reach their limit.");
            Assert.AreEqual(0.02f * 20, letterGap, 0.01f);
        }

        [Test]
        public void OverflowingLineCompressesItsWordSpaces()
        {
            LetteraWorldText plain = Make("aaa bbb");
            float fits = WidthOf(plain);
            var align = new AlignEffect { Alignment = LetteraHorizontalAlignment.Justify };
            LetteraWorldText text = Make("", fits - 0.5f);
            text.WordWrap = false;
            text.Styles.Add(LetteraStyle.ForWholeText(align));
            text.MarkStylesDirty();
            text.Text = "aaa bbb\n";
            text.ForceRebuild();
            Assert.DoesNotThrow(() => text.ForceRebuild());
        }

        // ------------------------------------------------------------- trim

        [Test]
        public void TrimRemovesSpaceAroundTheBlock()
        {
            float full = Make("X").Engine.GetHeightForWidth(1000);
            LetteraWorldText text = Make("<trim=capheight,baseline>X</trim>");
            float trimmed = text.Engine.GetHeightForWidth(1000);
            FaceMetrics metrics = Latin.LoadFace().Metrics;
            Assert.Less(trimmed, full);
            Assert.AreEqual((float)metrics.CapHeight / metrics.Upem * 20, trimmed, 0.1f);
        }

        [Test]
        public void TrimmedTextCentersOnItsCapHeight()
        {
            LetteraWorldText text = Make("<trim=capheight,baseline>X</trim>", 300, 200);
            text.VerticalAlignment = LetteraVerticalAlignment.Middle;
            text.ForceRebuild();
            FaceMetrics metrics = Latin.LoadFace().Metrics;
            float cap = (float)metrics.CapHeight / metrics.Upem * 20;
            float baseline = text.Lines[0].Baseline;
            Assert.AreEqual(100f - (baseline + cap), baseline + 100f, 0.5f, "Box top to cap top equals baseline to box bottom.");
        }

        [Test]
        public void InkTrimFollowsTheGlyphs()
        {
            float low = Make("<trim=ink,ink>x</trim>").Engine.GetHeightForWidth(1000);
            float tall = Make("<trim=ink,ink>Xg</trim>").Engine.GetHeightForWidth(1000);
            Assert.Less(low, tall);
        }

        [Test]
        public void StrokeDoesNotChangeTheTrim()
        {
            Assert.AreEqual(Make("<trim=ink,ink>x</trim>").Engine.GetHeightForWidth(1000),
                Make("<trim=ink,ink><stroke=#000,0.2>x</stroke></trim>").Engine.GetHeightForWidth(1000), Tol);
        }

        [Test]
        public void TrimOnEmptyTextIsNoError() => Assert.DoesNotThrow(() => Make("<trim=ink,ink></trim>"));

        // ------------------------------------------------------------- fit budgets

        LetteraWorldText Fit(float boxWidth, float size, params LetteraFitBudget[] budgets)
        {
            LetteraWorldText text = Make("", boxWidth, 100);
            text.WordWrap = false;
            text.AutoSize = true;
            text.MaxFontSize = size;
            text.MinFontSize = 5;
            text.FitBudgets.AddRange(budgets);
            text.Text = "abcdefghij";
            text.ForceRebuild();
            return text;
        }

        [Test]
        public void GlyphWidthBudgetKeepsTheSize()
        {
            float natural = WidthOf(Make("abcdefghij", size: 40));
            var budget = new FitByGlyphWidth { Limit = 2 };
            LetteraWorldText text = Fit(natural * 0.985f, 40, budget);
            Assert.AreEqual(40f, text.CurrentFontSize, Tol);
            Assert.Greater(budget.Spent, 1f);
            Assert.LessOrEqual(budget.Spent, 2f);
            Assert.LessOrEqual(WidthOf(text), natural * 0.985f + 0.01f);
            Assert.Less(text.GetGlyph(0).Advance, Make("a", size: 40).GetGlyph(0).Advance);
        }

        [Test]
        public void WithoutABudgetTheSizeShrinks()
        {
            float natural = WidthOf(Make("abcdefghij", size: 40));
            Assert.Less(Fit(natural * 0.985f, 40).CurrentFontSize, 40f);
        }

        [Test]
        public void TrackingBudgetTightens()
        {
            float natural = WidthOf(Make("abcdefghij", size: 40));
            var budget = new FitByTracking { Limit = 0.02f };
            LetteraWorldText text = Fit(natural - 3, 40, budget);
            Assert.AreEqual(40f, text.CurrentFontSize, Tol);
            Assert.Greater(budget.Spent, 0f);
        }

        [Test]
        public void BudgetTooSmallFallsBackToSize()
        {
            float natural = WidthOf(Make("abcdefghij", size: 40));
            Assert.Less(Fit(natural * 0.5f, 40, new FitByGlyphWidth { Limit = 2 }).CurrentFontSize, 40f);
        }

        [Test]
        public void EmptyBudgetListMatchesPlainAutoSize()
        {
            float natural = WidthOf(Make("abcdefghij", size: 40));
            Assert.AreEqual(Fit(natural * 0.8f, 40).CurrentFontSize, Fit(natural * 0.8f, 40).CurrentFontSize, 1e-4f);
        }

        [Test]
        public void MeasureEqualsDrawnWidthUnderABudget()
        {
            float natural = WidthOf(Make("abcdefghij", size: 40));
            LetteraWorldText text = Fit(natural * 0.985f, 40, new FitByGlyphWidth { Limit = 2 });
            Assert.AreEqual(WidthOf(text), text.MeasureText(new LetteraMeasureOptions { MaxWidth = natural * 0.985f }).x, 0.05f);
        }

        // ------------------------------------------------------------- eliding

        const string Long = "The quick brown fox jumps over the lazy dog";

        [Test]
        public void TwoRangesCanBothElide()
        {
            LetteraWorldText text = Make($"<ellipsis>{Long}</ellipsis> <ellipsis>{Long}</ellipsis>", 200);
            text.WordWrap = false;
            text.ForceRebuild();
            Assert.LessOrEqual(WidthOf(text), 200.01f);
            int markers = 0;
            uint marker = Make("…").GetGlyph(0).Glyph;
            for (int i = 0; i < text.GlyphCount; i++)
                if (text.GetGlyph(i).Glyph == marker)
                    markers++;
            Assert.AreEqual(2, markers);
        }

        [Test]
        public void ComponentOverflowAppliesAlongsideATagRange()
        {
            LetteraWorldText text = Make($"<ellipsis>abc</ellipsis> {Long}", 150);
            text.WordWrap = false;
            text.Overflow = LetteraOverflow.Truncate;
            text.ForceRebuild();
            Assert.LessOrEqual(WidthOf(text), 150.01f);
        }

        [Test]
        public void TruncateFadeReachesZeroAtTheCut()
        {
            LetteraWorldText text = Make($"<truncate=1,20>{Long}</truncate>", 200);
            text.WordWrap = false;
            text.ForceRebuild();
            TextEngine e = text.Engine;
            float cut = text.Lines[0].X + text.Lines[0].Width;
            bool faded = false, full = false;
            for (int v = 0; v < e.VertexCount; v++)
            {
                float toCut = cut - e.Vertices[v].x;
                if (toCut <= 0) Assert.AreEqual(0, e.Colors[v].a, "Past the cut: x " + e.Vertices[v].x);
                if (toCut > 0 && toCut < 20) faded |= e.Colors[v].a < 255 && e.Colors[v].a > 0;
                if (toCut >= 20) full |= e.Colors[v].a == 255;
            }
            Assert.IsTrue(faded);
            Assert.IsTrue(full);
        }

        [Test]
        public void TextThatFitsDoesNotFade()
        {
            TextEngine e = Make("<truncate=1,20>abc</truncate>").Engine;
            for (int v = 0; v < e.VertexCount; v++) Assert.AreEqual(255, e.Colors[v].a);
        }
    }
}

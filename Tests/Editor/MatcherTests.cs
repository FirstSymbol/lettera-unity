using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace FirstSymbol.Lettera.Editor.Tests
{
    public class MatcherTests : TextFixture
    {
        // A recording effect: which rendered ranges it was applied to.
        sealed class Recorder : LetteraEffect
        {
            public readonly List<(int start, int end)> Ranges = new List<(int, int)>();
            public readonly List<string> First = new List<string>();

            public override void Apply(LetteraAttributes attributes, int start, int end, in LetteraSlots slots)
            {
                Ranges.Add((start, end));
                First.Add(slots[0]);
            }
        }

        LetteraWorldText With(string markup, LetteraMatcher matcher, out Recorder recorder)
        {
            recorder = new Recorder();
            LetteraWorldText text = Make("");
            text.Styles.Add(LetteraStyle.ForMatcher(matcher, recorder));
            text.MarkStylesDirty();
            text.Text = markup;
            text.ForceRebuild();
            recorder.Ranges.Sort();
            return text;
        }

        // ------------------------------------------------------------- markdown markers

        [TestCase("**a**", "a", 0, 1)]
        [TestCase("x **ab** y", "x ab y", 2, 4)]
        public void BoldMarkersWrapARange(string markup, string clean, int start, int end)
        {
            LetteraWorldText text = With(markup, new MarkdownMarkerMatcher("**"), out Recorder r);
            Assert.AreEqual(clean, text.CleanText);
            Assert.AreEqual((start, end), r.Ranges[0]);
        }

        [TestCase("5 * 6 * 7")]
        [TestCase("**a")]
        [TestCase("a**")]
        public void InvalidMarkersStayLiteral(string markup)
        {
            string marker = markup.Contains("**") ? "**" : "*";
            LetteraWorldText text = With(markup, new MarkdownMarkerMatcher(marker), out Recorder r);
            Assert.AreEqual(markup, text.CleanText);
            Assert.AreEqual(0, r.Ranges.Count);
        }

        [Test]
        public void UnderscoreDoesNotToggleInsideAWord()
        {
            LetteraWorldText text = With("my_var_name", new MarkdownMarkerMatcher("_"), out Recorder r);
            Assert.AreEqual("my_var_name", text.CleanText);
            Assert.AreEqual(0, r.Ranges.Count);
        }

        [Test]
        public void UnderscoreWrapsAWord()
        {
            LetteraWorldText text = With("a _b_ c", new MarkdownMarkerMatcher("_"), out Recorder r);
            Assert.AreEqual("a b c", text.CleanText);
            Assert.AreEqual((2, 3), r.Ranges[0]);
        }

        [Test]
        public void TripleStarIsBoldAndItalic()
        {
            var bold = new Recorder();
            var italic = new Recorder();
            LetteraWorldText text = Make("");
            text.Styles.Add(LetteraStyle.ForMatcher(new MarkdownMarkerMatcher("**"), bold));
            text.Styles.Add(LetteraStyle.ForMatcher(new MarkdownMarkerMatcher("*"), italic));
            text.MarkStylesDirty();
            text.Text = "***a***";
            text.ForceRebuild();
            Assert.AreEqual("a", text.CleanText);
            Assert.AreEqual(1, bold.Ranges.Count);
            Assert.AreEqual(1, italic.Ranges.Count);
        }

        [Test]
        public void EscapeStopsAMarker()
        {
            LetteraWorldText text = Make("");
            var r = new Recorder();
            text.Styles.Add(LetteraStyle.ForMatcher(new MarkdownMarkerMatcher("*"), r));
            text.MarkStylesDirty();
            text.Text = @"\*a\*";
            text.ForceRebuild();
            Assert.AreEqual("*a*", text.CleanText);
            Assert.AreEqual(0, r.Ranges.Count);
        }

        [Test]
        public void PairDoesNotCrossAParagraph()
        {
            LetteraWorldText text = With("*a\nb*", new MarkdownMarkerMatcher("*"), out Recorder r);
            Assert.AreEqual(0, r.Ranges.Count);
            Assert.AreEqual("*a\nb*", text.CleanText);
        }

        // ------------------------------------------------------------- lists in markdown

        [Test]
        public void ListItemsLoseTheirMarkers()
        {
            LetteraWorldText text = With("- a\n- b", new MarkdownListMatcher(), out Recorder r);
            Assert.AreEqual("a\nb", text.CleanText);
            Assert.AreEqual(2, r.Ranges.Count);
            Assert.AreEqual("0", r.First[0]);
        }

        [Test]
        public void IndentSetsTheLevel()
        {
            With("- a\n  - c", new MarkdownListMatcher(), out Recorder r);
            Assert.AreEqual("1", r.First[1]);
        }

        [Test]
        public void MidLineDashIsLiteral()
        {
            LetteraWorldText text = With("a - b", new MarkdownListMatcher(), out Recorder r);
            Assert.AreEqual("a - b", text.CleanText);
            Assert.AreEqual(0, r.Ranges.Count);
        }

        [Test]
        public void PlainLineIsNotAnItem()
        {
            With("- a\nplain", new MarkdownListMatcher(), out Recorder r);
            Assert.AreEqual(1, r.Ranges.Count);
        }

        [Test]
        public void StarAtLineStartIsAListNotEmphasis()
        {
            LetteraWorldText text = Make("");
            var list = new Recorder();
            var emphasis = new Recorder();
            text.Styles.Add(LetteraStyle.ForMatcher(new MarkdownMarkerMatcher("*"), emphasis));
            text.Styles.Add(LetteraStyle.ForMatcher(new MarkdownListMatcher(), list));
            text.MarkStylesDirty();
            text.Text = "* a";
            text.ForceRebuild();
            Assert.AreEqual(1, list.Ranges.Count);
            Assert.AreEqual(0, emphasis.Ranges.Count);
        }

        // ------------------------------------------------------------- links and code

        [Test]
        public void MarkdownLinkKeepsItsText()
        {
            LetteraWorldText text = With("[Docs](https://example.org/a)", new MarkdownLinkMatcher(), out Recorder r);
            Assert.AreEqual("Docs", text.CleanText);
            Assert.AreEqual("https://example.org/a", r.First[0]);
            var values = new List<(int, int, string)>();
            text.Engine.GetPrimaryValues<Recorder>(values);
            Assert.AreEqual("https://example.org/a", values[0].Item3);
        }

        [Test]
        public void BracketsWithoutAUrlAreLiteral()
        {
            Assert.AreEqual("[x]", With("[x]", new MarkdownLinkMatcher(), out _).CleanText);
        }

        [Test]
        public void UrlWithParenthesesBalances()
        {
            With("[x](http://a.io/b(c)d)", new MarkdownLinkMatcher(), out Recorder r);
            Assert.AreEqual("http://a.io/b(c)d", r.First[0]);
        }

        [Test]
        public void CodeSpanProtectsItsContent()
        {
            LetteraWorldText text = Make("");
            text.Styles.Add(LetteraStyle.ForMatcher(new CodeSpanMatcher(), new NoEffect()));
            text.MarkStylesDirty();
            text.Text = "`<b>`";
            text.ForceRebuild();
            Assert.AreEqual("<b>", text.CleanText);
        }

        [Test]
        public void DoubleBackticksHoldASingleOne()
        {
            Assert.AreEqual("a`b", With("``a`b``", new CodeSpanMatcher(), out _).CleanText);
        }

        [Test]
        public void UnequalBackticksStayLiteral()
        {
            Assert.AreEqual("``a`", With("``a`", new CodeSpanMatcher(), out _).CleanText);
        }

        [Test]
        public void CodeSpanProtectsMarkersToo()
        {
            LetteraWorldText text = Make("");
            var bold = new Recorder();
            text.Styles.Add(LetteraStyle.ForMatcher(new MarkdownMarkerMatcher("**"), bold));
            text.Styles.Add(LetteraStyle.ForMatcher(new CodeSpanMatcher(), new NoEffect()));
            text.MarkStylesDirty();
            text.Text = "`**a**`";
            text.ForceRebuild();
            Assert.AreEqual("**a**", text.CleanText);
            Assert.AreEqual(0, bold.Ranges.Count);
        }

        // ------------------------------------------------------------- bare URLs

        [Test]
        public void UrlEndsBeforeTrailingPunctuation()
        {
            LetteraWorldText text = With("see https://a.io/b.", new BareUrlMatcher(), out Recorder r);
            Assert.AreEqual("see https://a.io/b.", text.CleanText);
            Assert.AreEqual((4, 18), r.Ranges[0]);
            Assert.AreEqual("https://a.io/b", r.First[0]);
        }

        [Test]
        public void WwwMatches() => Assert.AreEqual(1, Count("www.a.io", new BareUrlMatcher()));

        [Test]
        public void PrefixInsideAWordDoesNotMatch() => Assert.AreEqual(0, Count("xhttp://a", new BareUrlMatcher()));

        [Test]
        public void UnbalancedParenthesisIsDropped()
        {
            With("(https://a.io/b)", new BareUrlMatcher(), out Recorder r);
            Assert.AreEqual("https://a.io/b", r.First[0]);
        }

        [Test]
        public void NoParseWinsOverDetection()
        {
            LetteraWorldText text = Make("");
            var r = new Recorder();
            text.Styles.Add(LetteraStyle.ForMatcher(new BareUrlMatcher(), r));
            text.MarkStylesDirty();
            text.Text = "<noparse>https://a.io</noparse>";
            text.ForceRebuild();
            Assert.AreEqual(0, r.Ranges.Count);
        }

        int Count(string markup, LetteraMatcher matcher)
        {
            With(markup, matcher, out Recorder r);
            return r.Ranges.Count;
        }

        // ------------------------------------------------------------- triggers, literals, groups, void tags

        [Test]
        public void TriggerFindsAMention()
        {
            LetteraWorldText text = With("hi @anna!", new TriggerWordMatcher("@"), out Recorder r);
            Assert.AreEqual((3, 8), r.Ranges[0]);
            Assert.AreEqual("anna", r.First[0]);
            Assert.AreEqual("hi @anna!", text.CleanText);
        }

        [Test]
        public void EmailIsNotAMention() => Assert.AreEqual(0, Count("a@b.io", new TriggerWordMatcher("@")));

        [Test]
        public void TwoTriggersWorkTogether()
        {
            LetteraWorldText text = Make("");
            var at = new Recorder();
            var hash = new Recorder();
            text.Styles.Add(LetteraStyle.ForMatcher(new TriggerWordMatcher("@"), at));
            text.Styles.Add(LetteraStyle.ForMatcher(new TriggerWordMatcher("#"), hash));
            text.MarkStylesDirty();
            text.Text = "@a #b";
            text.ForceRebuild();
            Assert.AreEqual(1, at.Ranges.Count);
            Assert.AreEqual(1, hash.Ranges.Count);
        }

        [Test]
        public void ExplicitMarkupClaimsBeforeADetector()
        {
            LetteraWorldText text = Make("");
            var r = new Recorder();
            text.Styles.Add(LetteraStyle.ForMatcher(new TriggerWordMatcher("@"), r));
            text.MarkStylesDirty();
            text.Text = "<noparse>@a</noparse> @b";
            text.ForceRebuild();
            Assert.AreEqual(1, r.Ranges.Count);
            Assert.AreEqual("b", r.First[0]);
        }

        [Test]
        public void LiteralWithReplacement()
        {
            LetteraWorldText text = With("a :) b", new LiteralMatcher(":)") { Replacement = "☺" }, out Recorder r);
            Assert.AreEqual("a ☺ b", text.CleanText);
            Assert.AreEqual((2, 3), r.Ranges[0]);
        }

        [Test]
        public void LiteralIsCaseSensitive() => Assert.AreEqual(0, Count("ABC", new LiteralMatcher("abc")));

        [Test]
        public void LongestLiteralWins()
        {
            With("abc", new LiteralMatcher("ab", "abc"), out Recorder r);
            Assert.AreEqual((0, 3), r.Ranges[0]);
        }

        [Test]
        public void GroupGivesOneEffectSeveralSources()
        {
            With("<i>a</i> *b*", new MatcherGroup(new TagMatcher("i"), new MarkdownMarkerMatcher("*")), out Recorder r);
            Assert.AreEqual(2, r.Ranges.Count);
        }

        [Test]
        public void GroupClonesItsMatchers()
        {
            var group = new MatcherGroup(new LiteralMatcher("a"));
            var copy = (MatcherGroup)group.Clone();
            ((LiteralMatcher)copy.Matchers[0]).Patterns.Add("b");
            Assert.AreEqual(1, ((LiteralMatcher)group.Matchers[0]).Patterns.Count);
        }

        [Test]
        public void VoidTagIsAZeroLengthRange()
        {
            LetteraWorldText text = With("a<x>b", new VoidTagMatcher("x"), out Recorder r);
            Assert.AreEqual("ab", text.CleanText);
            Assert.AreEqual((1, 1), r.Ranges[0]);
        }

        [Test]
        public void StrayVoidCloserIsStripped()
        {
            Assert.AreEqual("ab", With("a</x>b", new VoidTagMatcher("x"), out _).CleanText);
        }

        // ------------------------------------------------------------- dividers

        [Test]
        public void DividerShowsItsStringInAWideBox()
        {
            LetteraWorldText text = Make("Red<divider>Blue");
            Assert.AreEqual("Red | Blue", text.CleanText);
            Assert.AreEqual(1, text.Lines.Count);
        }

        [Test]
        public void DividerDisappearsAtABreak()
        {
            float width = WidthOf(Make("Blue")) + 5;
            LetteraWorldText text = Make("Red<divider>Blue", width);
            Assert.AreEqual(2, text.Lines.Count);
            Assert.AreEqual(3, text.Lines[0].VisibleEnd, "The bar hangs out of the first line.");
            Assert.AreEqual(WidthOf(Make("Red")) < width, true);
            Assert.Less(text.Lines[0].Width, WidthOf(Make("Red | ")));
        }

        [Test]
        public void QuotedOverrideKeepsItsSpaces()
        {
            Assert.AreEqual("a ◆ b", Make("a<divider=\" ◆ \">b").CleanText);
        }

        [Test]
        public void LinesBreakOnlyAtDividers()
        {
            float width = WidthOf(Make("aa bb")) + 1;
            LetteraWorldText text = Make("aa bb<divider>cc dd", width);
            Assert.AreEqual(2, text.Lines.Count, "The piece stays whole though it has a space inside.");
        }

        [Test]
        public void LongPieceWrapsAndTheNextStartsANewLine()
        {
            float width = WidthOf(Make("aaa")) + 1;
            LetteraWorldText text = Make("aaaaaaaa<divider>b", width);
            Assert.Greater(text.Lines.Count, 2);
        }

        // ------------------------------------------------------------- link tag

        [Test]
        public void LinkRecordsAUrlAndHasNoLook()
        {
            LetteraWorldText text = Make("<link=https://a.io>x</link>");
            Assert.AreEqual("x", text.CleanText);
            Assert.AreEqual(new Color32(255, 255, 255, 255), text.Engine.Colors[0]);
            var values = new List<(int, int, string)>();
            text.Engine.GetPrimaryValues<LinkEffect>(values);
            Assert.AreEqual("https://a.io", values[0].Item3);
        }

        // ------------------------------------------------------------- nothing hangs on bad input

        [TestCase("**")]
        [TestCase("***")]
        [TestCase("`")]
        [TestCase("[")]
        [TestCase("[](")]
        [TestCase("- ")]
        [TestCase("<divider")]
        [TestCase("@")]
        [TestCase("http://")]
        public void MalformedInputDoesNotThrow(string markup)
        {
            LetteraWorldText text = Make("");
            text.Styles.Add(LetteraStyle.ForMatcher(new MarkdownMarkerMatcher("*"), new NoEffect()));
            text.Styles.Add(LetteraStyle.ForMatcher(new MarkdownMarkerMatcher("**"), new NoEffect()));
            text.Styles.Add(LetteraStyle.ForMatcher(new MarkdownListMatcher(), new NoEffect()));
            text.Styles.Add(LetteraStyle.ForMatcher(new MarkdownLinkMatcher(), new NoEffect()));
            text.Styles.Add(LetteraStyle.ForMatcher(new CodeSpanMatcher(), new NoEffect()));
            text.Styles.Add(LetteraStyle.ForMatcher(new BareUrlMatcher(), new NoEffect()));
            text.Styles.Add(LetteraStyle.ForMatcher(new TriggerWordMatcher("@"), new NoEffect()));
            text.MarkStylesDirty();
            text.Text = markup;
            Assert.DoesNotThrow(() => text.ForceRebuild());
        }
    }
}

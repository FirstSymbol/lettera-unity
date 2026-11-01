using NUnit.Framework;
using UnityEngine;

namespace FirstSymbol.Lettera.Editor.Tests
{
    /// <summary>What the first text of a session pays for, and what a first long text must not.</summary>
    public class FirstTextTests : TextFixture
    {
        [Test]
        public void PrepareMakesTheGlyphsAndAnyTextThenFindsThem()
        {
            GlyphAtlas.Clear();
            GlyphAtlas.Flush();
            int before = GlyphAtlas.GlyphCount;
            LetteraTextWarmUp.Prepare(Latin, "abc<d>");
            int made = GlyphAtlas.GlyphCount - before;
            Assert.AreEqual(6, made, "each character once, a tag's signs among them");
            LetteraWorldText text = Make("cab");
            Assert.AreEqual(before + made, GlyphAtlas.GlyphCount, "a text of those adds none");
            Assert.AreEqual(3, text.GlyphCount);
        }

        [Test]
        public void PrepareTakesNoTextOutOfTheScene()
        {
            int listeners = LetteraTextListeners.Count;
            LetteraTextWarmUp.Prepare(Latin);
            Assert.AreEqual(listeners, LetteraTextListeners.Count);
            Assert.Greater(GlyphAtlas.GlyphCount, 90);
        }

        [Test]
        public void ALineBreakAsksNoFontAtAll()
        {
            bool saved = LetteraSettings.Instance.DisableSystemFontFallback;
            try
            {
                // With the search allowed, a line feed still goes to the primary face and leaves the lines as they were.
                LetteraWorldText off = Make("one
two
three");
                float heightOff = off.Lines[0].Height, secondOff = off.Lines[1].Baseline;
                int linesOff = off.Lines.Count;
                LetteraSettings.Instance.DisableSystemFontFallback = false;
                LetteraWorldText on = Make("one
two
three");
                Assert.AreEqual(linesOff, on.Lines.Count);
                Assert.AreEqual(heightOff, on.Lines[0].Height);
                Assert.AreEqual(secondOff, on.Lines[1].Baseline);
                Assert.AreSame(Latin.LoadFace(), on.Engine.ChainForTests.PrimaryFace);
            }
            finally
            {
                LetteraSettings.Instance.DisableSystemFontFallback = saved;
            }
        }

        [Test]
        public void APlainTextSharesWhatItOnlyReads()
        {
            LetteraWorldText a = Make("plain words");
            LetteraWorldText b = Make("other plain words");
            Assert.AreSame(a.Engine.AttributesForTests, b.Engine.AttributesForTests);
            a.Text = "<b>now with a tag</b>";
            a.ForceRebuild();
            Assert.AreNotSame(a.Engine.AttributesForTests, b.Engine.AttributesForTests);
            Assert.AreEqual("now with a tag", a.CleanText);
            Assert.AreEqual(1, a.Engine.AttributesForTests.Chars[0].Weight > 0 ? 1 : 0, "its own attributes hold the tag");
            Assert.AreEqual(0, b.Engine.AttributesForTests.Chars[0].Weight, "and the shared ones do not");
            a.Text = "plain again";
            a.ForceRebuild();
            Assert.AreSame(a.Engine.AttributesForTests, b.Engine.AttributesForTests);
        }

        [Test]
        public void AStyleAddedToATextReachesItsPlainWords()
        {
            LetteraWorldText text = Make("plain words");
            var fill = new FillEffect();
            fill.SetPaint(LetteraPaint.Of(LetteraGradient.Of(Color.red, Color.blue)));
            text.Styles.Add(LetteraStyle.ForWholeText(fill));
            text.MarkStylesDirty();
            text.ForceRebuild();
            Assert.IsTrue(text.Engine.Styled);
        }

        [Test]
        public void AStyleAddedToTheDefaultsIsStillAsked()
        {
            var fill = new FillEffect();
            fill.SetPaint(LetteraPaint.Of(LetteraGradient.Of(Color.red, Color.blue)));
            LetteraStyle added = LetteraStyle.ForWholeText(fill);
            LetteraDefaultStyles.Styles.Add(added);
            try
            {
                LetteraWorldText text = Make("plain words");
                Assert.IsTrue(text.Engine.Styled);
            }
            finally
            {
                LetteraDefaultStyles.Styles.Remove(added);
            }
            LetteraWorldText after = Make("plain words");
            Assert.IsFalse(after.Engine.Styled);
        }

        [Test]
        public void ABackslashStillEscapes()
        {
            Assert.AreEqual("<b>", Make("\<b>").CleanText);
            Assert.AreEqual("a*b", Make("a\*b").CleanText);
        }
    }
}

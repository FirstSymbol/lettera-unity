using NUnit.Framework;

namespace FirstSymbol.Lettera.Editor.Tests
{
    public class ShapingTests : TextFixture
    {
        [Test]
        public void NativePluginHasExpectedVersion()
        {
            Assert.AreEqual(LetteraNative.ExpectedVersion, LetteraNative.lt_version());
        }

        [Test]
        public void ArabicLettersTakeContextualForms()
        {
            // The same letter alone and inside a word uses a different glyph.
            uint alone = Glyphs(Make("م", font: Arabic))[0];
            var word = Glyphs(Make("مرحبا", font: Arabic));
            Assert.GreaterOrEqual(word.Count, 5);
            CollectionAssert.DoesNotContain(word, alone);
        }

        [Test]
        public void ColorBoundaryDoesNotBreakArabicJoining()
        {
            var plain = Glyphs(Make("مرحبا", font: Arabic));
            var colored = Glyphs(Make("<color=red>مر</color>حبا", font: Arabic));
            CollectionAssert.AreEqual(plain, colored);
        }

        [Test]
        public void LatinLigatureFormsOneGlyph()
        {
            Assert.AreEqual(1, Make("fi").GlyphCount);
            Assert.AreEqual(2, Make("f‌i").GlyphCount - 1, "ZWNJ prevents the ligature; it stays as an invisible glyph");
        }

        [Test]
        public void DevanagariConjunctUsesFewerGlyphsThanCodepoints()
        {
            Assert.Less(Make("क्ष", font: Devanagari).GlyphCount, 3);
        }

        [Test]
        public void HebrewRunsRightToLeft()
        {
            LetteraWorldText text = Make("אבג", font: Hebrew);
            Assert.AreEqual(3, text.GlyphCount);
            Assert.AreEqual(2, text.GetGlyph(0).Cluster, "the leftmost glyph is the last letter");
            Assert.AreEqual(0, text.GetGlyph(2).Cluster);
            Assert.Less(text.GetGlyph(0).X, text.GetGlyph(2).X);
        }

        [Test]
        public void MixedDirectionKeepsLtrThenRtlInLtrParagraph()
        {
            LetteraWorldText text = Make("ab אב", font: Latin);
            text.FontStack = Stack(Hebrew);
            text.ForceRebuild();
            Assert.AreEqual(0, text.GetGlyph(0).Cluster);
            Assert.AreEqual(1, text.GetGlyph(1).Cluster);
            Assert.AreEqual(2, text.GetGlyph(2).Cluster);
            Assert.AreEqual(4, text.GetGlyph(3).Cluster, "Hebrew reverses");
            Assert.AreEqual(3, text.GetGlyph(4).Cluster);
        }

        [Test]
        public void AutoDirectionFollowsFirstStrongCharacterPerParagraph()
        {
            LetteraWorldText text = Make("אב\nab", width: 400, font: Hebrew);
            text.FontStack = Stack(Latin);
            text.ForceRebuild();
            Assert.AreEqual(2, text.Lines.Count);
            // Start alignment: the Hebrew paragraph hugs the right edge, the Latin one the left.
            Assert.Greater(text.Lines[0].X, text.Lines[1].X + 100);
        }

        [Test]
        public void ForcedDirectionOverridesAuto()
        {
            LetteraWorldText text = Make("abc", width: 400);
            float left = text.Lines[0].X;
            text.BaseDirection = LetteraDirection.RightToLeft;
            text.ForceRebuild();
            Assert.Greater(text.Lines[0].X, left + 100);
        }

        [Test]
        public void FontStackFallsThroughToCoveringFamily()
        {
            LetteraWorldText text = Make("a م", font: Latin);
            text.FontStack = Stack(Arabic);
            text.ForceRebuild();
            Assert.AreNotEqual(0u, text.GetGlyph(2).Glyph, "the Arabic family covers the letter");
        }

        [Test]
        public void MissingEverywhereUsesNotdefWhenFallbackIsOff()
        {
            LetteraWorldText text = Make("م", font: Latin);
            Assert.AreEqual(0u, text.GetGlyph(0).Glyph);
        }

        [Test]
        public void SyntheticBoldWidensAdvances()
        {
            float regular = WidthOf(Make("hello"));
            float bold = WidthOf(Make("<b>hello</b>"));
            Assert.Greater(bold, regular);
        }

        [Test]
        public void SyntheticItalicKeepsAdvances()
        {
            Assert.AreEqual(WidthOf(Make("hello")), WidthOf(Make("<i>hello</i>")), 0.01f);
        }

        [Test]
        public void LoneSurrogateAndControlCharactersDoNotThrow()
        {
            Assert.DoesNotThrow(() => Make("a\uD800b\u0001￿c"));
        }

        [Test]
        public void HugeTextDoesNotThrow()
        {
            Assert.DoesNotThrow(() => Make(new string('x', 200000), width: 500));
        }
    }
}

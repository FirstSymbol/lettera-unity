using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;

namespace FirstSymbol.Lettera.Editor.Tests
{
    /// <summary>Spaces between fonts, a font's own spacing and its glyph overrides.</summary>
    public class FontTuningTests : TextFixture
    {
        const string ColorFont = "test_glyphs-glyf_colr_1.ttf";
        const string Symbol = "\U000F0100";

        // A font of its own, so a setting made here touches no other test.
        LetteraFont Copy(LetteraFont source)
        {
            LetteraFont copy = LetteraFont.CreateFromBytes(source.ReadBytes());
            Created.Add(copy);
            return copy;
        }

        static float AdvanceAt(LetteraWorldText text, int cluster)
        {
            for (int i = 0; i < text.GlyphCount; i++)
                if (text.GetGlyph(i).Cluster == cluster)
                    return text.GetGlyph(i).Advance;
            Assert.Fail("no glyph for cluster " + cluster);
            return 0;
        }

        float LatinSpace() => AdvanceAt(Make("a a"), 1);

        // F6: the browser's rule. A space next to a letter only another font has takes the first font of the list that has a space.
        [Test]
        public void ASpaceBesideAnotherFontsLetterTakesTheFirstFontWithASpace()
        {
            float latin = LatinSpace();
            LetteraFont wide = Copy(Hebrew);
            wide.NormalizeSize = false;
            wide.FontScale = 2;
            LetteraWorldText text = Make("");
            text.FontStack = Stack(wide);
            text.Text = "א א a";
            text.ForceRebuild();
            Assert.AreEqual(latin, AdvanceAt(text, 1), 0.01f, "between two Hebrew letters");
            Assert.AreEqual(latin, AdvanceAt(text, 3), 0.01f, "between Hebrew and Latin");
            Assert.AreEqual(2 * AdvanceAt(Make("א", font: Hebrew), 0), AdvanceAt(text, 0), 0.05f, "the letters are the wide font's");
        }

        [Test]
        public void ASpaceAfterASymbolKeepsTheTextsWidth()
        {
            float latin = LatinSpace();
            LetteraFont symbols = Copy(Font(ColorFont));
            Assume.That(symbols.LoadFace().HasGlyph(0xF0100) && !symbols.LoadFace().HasGlyph(' '));
            LetteraWorldText text = Make("");
            text.FontStack = Stack(symbols);
            text.Text = Symbol + " a " + Symbol + " " + Symbol;
            text.ForceRebuild();
            Assert.AreEqual(latin, AdvanceAt(text, 1), 0.01f);
            Assert.AreEqual(latin, AdvanceAt(text, 3), 0.01f);
            Assert.AreEqual(latin, AdvanceAt(text, 5), 0.01f, "between two symbols");
        }

        [Test]
        public void APrimaryWithoutASpaceTakesTheNextFontsSpace()
        {
            LetteraFont symbols = Copy(Font(ColorFont));
            LetteraFont wide = Copy(Hebrew);
            wide.NormalizeSize = false;
            wide.FontScale = 2;
            float wideSpace = AdvanceAt(Make("א א", font: wide), 1);
            LetteraWorldText text = Make("", font: symbols);
            text.FontStack = Stack(wide, Latin);
            text.Text = Symbol + " " + Symbol + " a";
            text.ForceRebuild();
            Assert.AreEqual(wideSpace, AdvanceAt(text, 1), 0.01f, "the first font with a space is the second");
            Assert.AreEqual(wideSpace, AdvanceAt(text, 3), 0.01f, "the same before a Latin letter");
        }

        static float Sum(LetteraWorldText text)
        {
            float sum = 0;
            for (int i = 0; i < text.GlyphCount; i++) sum += text.GetGlyph(i).Advance;
            return sum;
        }

        // F1: a face's own space width, for spaces and no-break spaces, in font units.
        [Test]
        public void AFontSetsItsOwnSpaceWidth()
        {
            float natural = LatinSpace();
            LetteraFont font = Copy(Latin);
            font.SpaceWidth = 520;
            LetteraWorldText text = Make("a a a", font: font);
            Assert.AreEqual(natural * 2, AdvanceAt(text, 1), 0.01f, "the file's space is 260 units");
            Assert.AreEqual(natural * 2, AdvanceAt(text, 3), 0.01f, "a no-break space too");
            Assert.AreEqual(AdvanceAt(Make("a"), 0), AdvanceAt(text, 0), 0.001f, "letters keep theirs");
            font.SpaceWidth = 0;
            text.ForceRebuild();
            Assert.AreEqual(natural, AdvanceAt(text, 1), 0.01f, "0 gives the file's back");
        }

        [Test]
        public void ASpacingOffsetOpensEveryAdvanceButJoinedScripts()
        {
            const string word = "office";
            float plain = Sum(Make(word));
            int glyphs = Make(word).GlyphCount;
            LetteraFont font = Copy(Latin);
            font.SpacingOffset = 50;
            LetteraWorldText text = Make(word, font: font);
            Assert.AreEqual(plain + glyphs * 50 * 20 / 1000f, Sum(text), 0.01f, "50 units at 20 px is 1 px a glyph");

            LetteraFont arabic = Copy(Arabic);
            float arabicPlain = Sum(Make("مرحبا", font: arabic));
            arabic.SpacingOffset = 50;
            Assert.AreEqual(arabicPlain, Sum(Make("مرحبا", font: arabic)), 0.01f, "joined letters stay joined");
        }

        static uint GlyphOf(LetteraFontAsset font, int codepoint) => LetteraNative.lt_font_glyph(font.LoadFace().Regular.Font, (uint)codepoint, 0);

        // F2: one glyph's advance and offset, by its character.
        [Test]
        public void AGlyphOverrideMovesOneGlyphAndWidensIt()
        {
            LetteraWorldText plain = Make("bab");
            LetteraFont font = Copy(Latin);
            var tuned = LetteraGlyphOverride.For('a');
            tuned.Advance = 100;
            tuned.Offset = new Vector2Int(50, 200);
            font.GlyphOverrides.Add(tuned);
            font.GlyphOverridesChanged();
            LetteraWorldText text = Make("bab", font: font);
            Assert.AreEqual(plain.GetGlyph(1).Advance + 2, text.GetGlyph(1).Advance, 0.01f, "100 units at 20 px");
            Assert.AreEqual(plain.GetGlyph(0).Advance, text.GetGlyph(0).Advance, 0.001f, "other glyphs keep theirs");
            Assert.AreEqual(plain.GetGlyph(2).X + 2, text.GetGlyph(2).X, 0.01f, "the next glyph moves by the advance only");
            Assert.AreEqual(plain.GetGlyph(1).X + 1, text.GetGlyph(1).X, 0.01f, "the glyph is drawn 50 units right");
            Assert.AreEqual(4, Mathf.Abs(text.GetGlyph(1).Y - plain.GetGlyph(1).Y), 0.01f, "and 200 units up");
        }

        [Test]
        public void AGlyphOverrideSizesAndRastersOneGlyph()
        {
            LetteraFont plain = Copy(Latin);
            LetteraFont font = Copy(Latin);
            var tuned = LetteraGlyphOverride.For('a');
            tuned.Scale = 2;
            tuned.PixelsPerEm = 128;
            font.GlyphOverrides.Add(tuned);
            font.GlyphOverridesChanged();
            AtlasGlyph before = GlyphAtlas.Get(plain.LoadFace().Regular, GlyphOf(plain, 'a'), 32);
            AtlasGlyph after = GlyphAtlas.Get(font.LoadFace().Regular, GlyphOf(font, 'a'), 32);
            AtlasGlyph other = GlyphAtlas.Get(font.LoadFace().Regular, GlyphOf(font, 'b'), 32);
            Assert.AreEqual((before.Right - before.Left) * 2, after.Right - after.Left, 1f, "drawn twice the size");
            Assert.AreEqual((before.Top - before.Bottom) * 2, after.Top - after.Bottom, 1f);
            float texelsBefore = (before.U1 - before.U0) * GlyphAtlas.SdfPageSize, texelsAfter = (after.U1 - after.U0) * GlyphAtlas.SdfPageSize;
            Assert.AreEqual(4, texelsAfter / texelsBefore, 0.3f, "128 pixels per em against the 32 asked");
            AtlasGlyph otherPlain = GlyphAtlas.Get(plain.LoadFace().Regular, GlyphOf(plain, 'b'), 32);
            Assert.AreEqual(otherPlain.Right - otherPlain.Left, other.Right - other.Left, 0.01f, "other glyphs as they were");

            // Taken away, the glyph is made again as designed.
            font.GlyphOverrides.Clear();
            font.GlyphOverridesChanged();
            AtlasGlyph again = GlyphAtlas.Get(font.LoadFace().Regular, GlyphOf(font, 'a'), 32);
            Assert.AreEqual(before.Right - before.Left, again.Right - again.Left, 0.01f);
        }

        [Test]
        public void TheFontInspectorShowsSpacingAndOverrides()
        {
            LetteraFont font = ScriptableObject.CreateInstance<LetteraFont>();
            Created.Add(font);
            font.SetFontData(Latin.ReadBytes());
            var editor = UnityEditor.Editor.CreateEditor(font);
            Created.Add(editor);
            UnityEngine.UIElements.VisualElement root = editor.CreateInspectorGUI();
            var labels = new System.Collections.Generic.List<string>();
            root.Query<UnityEngine.UIElements.Label>().ForEach(l => labels.Add(l.text));
            CollectionAssert.Contains(labels, "Spacing Offset");
            CollectionAssert.Contains(labels, "Space Width");
            CollectionAssert.Contains(labels, "Glyph Overrides");
        }
    }
}

using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;

namespace FirstSymbol.Lettera.Editor.Tests
{
    /// <summary>F5: a font's own raster detail, for hairline and calligraphic faces.</summary>
    public class RasterDetailTests : TextFixture
    {
        [SetUp]
        public void ClearAtlas() => GlyphAtlas.Clear();

        // A font of its own, so a setting made here touches no other test.
        LetteraFont Copy(LetteraFont source)
        {
            LetteraFont copy = LetteraFont.CreateFromBytes(source.ReadBytes());
            Created.Add(copy);
            return copy;
        }

        static uint GlyphOf(LetteraFontAsset font, int codepoint) => LetteraNative.lt_font_glyph(font.LoadFace().Regular.Font, (uint)codepoint, 0);

        // How many atlas texels the glyph's tile spans across: it grows with the density the glyph was drawn at.
        static float Texels(LetteraFontAsset font, int codepoint, int asked)
        {
            AtlasGlyph entry = GlyphAtlas.Get(font.LoadFace().Regular, GlyphOf(font, codepoint), asked);
            Assert.AreNotEqual(AtlasGlyph.Empty, entry.Kind);
            return (entry.U1 - entry.U0) * GlyphAtlas.SdfPageSize;
        }

        [Test]
        public void EachStepDrawsTheFontsGlyphsFiner()
        {
            LetteraFont plain = Copy(Latin);
            LetteraFont font = Copy(Latin);
            float standard = Texels(plain, 'a', 64);
            Assert.AreEqual(0, LetteraFontAsset.PixelsPerEmOf(LetteraRasterDetail.Standard));

            font.RasterDetail = LetteraRasterDetail.Fine;
            float fine = Texels(font, 'a', 64);
            font.RasterDetail = LetteraRasterDetail.Finest;
            float finest = Texels(font, 'a', 64);
            TestContext.WriteLine($"'a' across: {standard:0.#} texels at Standard, {fine:0.#} at Fine, {finest:0.#} at Finest");
            Assert.AreEqual(2, fine / standard, 0.2f, "Fine: 128 pixels per em for text that asks 64");
            Assert.AreEqual(4, finest / standard, 0.4f, "Finest: 256");
            Assert.AreEqual(Texels(plain, 'a', 256), finest, 0.5f, "the same tile a text asking 256 gets");

            // A floor, not a size: text that asks more keeps it, and Standard again draws as the text asks.
            font.RasterDetail = LetteraRasterDetail.Fine;
            Assert.AreEqual(Texels(plain, 'a', 128), Texels(font, 'a', 128), 0.5f, "large text asks 128 already");
            Assert.AreEqual(Texels(plain, 'a', 256), Texels(font, 'a', 256), 0.5f);
            font.RasterDetail = LetteraRasterDetail.Standard;
            Assert.AreEqual(standard, Texels(font, 'a', 64), 0.5f);
        }

        [Test]
        public void TextWithAFinestFontDrawsFromFinerTiles()
        {
            LetteraFont font = Copy(Latin);
            LetteraWorldText plain = Make("l", font: Copy(Latin));
            font.RasterDetail = LetteraRasterDetail.Finest;
            LetteraWorldText fine = Make("l", font: font);
            float Span(LetteraWorldText text) => (text.Engine.Uv0[2].x - text.Engine.Uv0[0].x) * GlyphAtlas.SdfPageSize;
            TestContext.WriteLine($"'l' at 20 units: {Span(plain):0.#} texels across, {Span(fine):0.#} with Finest");
            Assert.AreEqual(4, Span(fine) / Span(plain), 0.4f);
            Assert.AreEqual(plain.GetGlyph(0).Advance, fine.GetGlyph(0).Advance, 1e-4f, "layout as it was");
            // The quad's margin rounds to whole texels of the tile, so it moves by a fraction of a texel at the coarser density.
            Assert.AreEqual(plain.Engine.Vertices[2].x - plain.Engine.Vertices[0].x, fine.Engine.Vertices[2].x - fine.Engine.Vertices[0].x, 20f / 64, "about the same quad on screen");

            // Back to Standard, the text draws from the usual tiles again.
            font.RasterDetail = LetteraRasterDetail.Standard;
            fine.ForceRebuild();
            Assert.AreEqual(Span(plain), Span(fine), 0.5f);
        }

        [Test]
        public void AGlyphOverridesOwnDensityWinsAndABoostRaisesPastTheFont()
        {
            LetteraFont plain = Copy(Latin);
            LetteraFont font = Copy(Latin);
            font.RasterDetail = LetteraRasterDetail.Finest;
            var own = LetteraGlyphOverride.For('a');
            own.PixelsPerEm = 64;
            font.GlyphOverrides.Add(own);
            font.GlyphOverridesChanged();
            Assert.AreEqual(Texels(plain, 'a', 64), Texels(font, 'a', 64), 0.5f, "the override's 64");
            Assert.AreEqual(Texels(plain, 'b', 256), Texels(font, 'b', 64), 0.5f, "the font's 256 for the rest");

            // A range's boost raises a glyph for every text, and the larger of the two holds.
            font.RasterDetail = LetteraRasterDetail.Fine;
            FontInstance instance = font.LoadFace().Regular;
            GlyphAtlas.Boost(instance, GlyphOf(font, 'c'), 256);
            Assert.AreEqual(Texels(plain, 'c', 256), Texels(font, 'c', 64), 0.5f);
        }

        [Test]
        public void AVariantKeepsItsOwnDetail()
        {
            var variant = ScriptableObject.CreateInstance<LetteraFontVariant>();
            Created.Add(variant);
            variant.Source = Copy(Latin);
            float standard = Texels(variant, 'a', 64);
            variant.RasterDetail = LetteraRasterDetail.Fine;
            Assert.AreEqual(2, Texels(variant, 'a', 64) / standard, 0.2f);
            Assert.AreEqual(standard, Texels(variant.Source, 'a', 64), 0.5f, "its source draws as before");
        }

        [Test]
        public void ASystemFontKeepsItsOwnDetail()
        {
            var font = ScriptableObject.CreateInstance<LetteraSystemFont>();
            Created.Add(font);
            font.Common = new[] { "Arial", "Segoe UI", "DejaVu Sans", "Helvetica" };
            if (font.LoadFace() == null || font.ResolveFailed) Assert.Ignore("no common system font is installed here");
            float standard = Texels(font, 'a', 64);
            font.RasterDetail = LetteraRasterDetail.Finest;
            Assert.AreEqual(4, Texels(font, 'a', 64) / standard, 0.4f);
        }

        [Test]
        public void TheFontInspectorsShowTheDetailCard()
        {
            LetteraFont font = Copy(Latin);
            var variant = ScriptableObject.CreateInstance<LetteraFontVariant>();
            var system = ScriptableObject.CreateInstance<LetteraSystemFont>();
            Created.Add(variant);
            Created.Add(system);
            foreach (Object asset in new Object[] { font, variant, system })
            {
                var editor = UnityEditor.Editor.CreateEditor(asset);
                Created.Add(editor);
                VisualElement root = editor.CreateInspectorGUI();
                var labels = new System.Collections.Generic.List<string>();
                root.Query<Label>().ForEach(l => labels.Add(l.text));
                CollectionAssert.Contains(labels, "RASTER DETAIL", asset.GetType().Name);
                CollectionAssert.Contains(labels, "Finest", asset.GetType().Name);
            }
        }
    }
}

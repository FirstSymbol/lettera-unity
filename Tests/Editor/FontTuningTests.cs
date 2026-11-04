using NUnit.Framework;
using FirstSymbol.Lettera.Outlines;
using Unity.Jobs;
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

        // F4: pages of 16 bits cost twice the memory of 8 and draw the same text the same.
        [TestCase(LetteraRenderMode.SDF)]
        [TestCase(LetteraRenderMode.MSDF)]
        public void SixteenBitPagesCostTwiceAndDrawTheSame(LetteraRenderMode mode)
        {
            var background = new Color(0.1f, 0.1f, 0.1f, 1);
            int set = mode == LetteraRenderMode.SDF ? 0 : 1;
            GlyphAtlas.Clear();
            LetteraWorldText text = Make("", 300, 60, null, 24);
            text.RenderMode = mode;
            text.Text = "Sixteen bits, the same picture";
            text.ForceRebuild();
            Texture2D narrow = Render(300, 60, background);
            Assert.AreEqual(8, mode == LetteraRenderMode.SDF ? GlyphAtlas.SdfDepth : GlyphAtlas.MsdfDepth);
            long narrowBytes = GlyphAtlas.PageBytes;
            (int, int) narrowPages = (GlyphAtlas.SdfPageCount, GlyphAtlas.MsdfPageCount);
            UnityEngine.Experimental.Rendering.GraphicsFormat narrowFormat = GlyphAtlas.TextureOf(set).graphicsFormat;
            try
            {
                GlyphAtlas.DepthForTests = 16;
                GlyphAtlas.Clear();
                GlyphAtlas.Flush();
                text.ForceRebuild();
                Texture2D wide = Render(300, 60, background);
                Assert.AreEqual(16, mode == LetteraRenderMode.SDF ? GlyphAtlas.SdfDepth : GlyphAtlas.MsdfDepth);
                Assert.AreEqual(mode == LetteraRenderMode.SDF ? UnityEngine.Experimental.Rendering.GraphicsFormat.R16_UNorm : UnityEngine.Experimental.Rendering.GraphicsFormat.R16G16B16A16_UNorm, GlyphAtlas.TextureOf(set).graphicsFormat);
                long wideBytes = GlyphAtlas.PageBytes;
                TestContext.WriteLine($"{mode}: pages {narrowBytes / 1048576f:0.##} MB at 8 bits ({narrowFormat}), {wideBytes / 1048576f:0.##} MB at 16");
                Assert.AreEqual(narrowPages, (GlyphAtlas.SdfPageCount, GlyphAtlas.MsdfPageCount), "as many pages");
                long sdfPage = (long)GlyphAtlas.SdfPageSize * GlyphAtlas.SdfPageSize, msdfPage = (long)GlyphAtlas.MsdfPageSize * GlyphAtlas.MsdfPageSize * 4;
                Assert.AreEqual(narrowBytes + sdfPage * narrowPages.Item1 + msdfPage * narrowPages.Item2, wideBytes, "each distance-field page costs twice");
                // An 8-bit step is 1/255 of the field's reach, which the edge's smoothing turns into a few levels of coverage: the two differ by that, and no more.
                float largest = 0, inkNarrow = 0, inkWide = 0;
                for (int y = 0; y < 60; y++)
                    for (int x = 0; x < 300; x++)
                    {
                        largest = Mathf.Max(largest, Mathf.Abs(narrow.GetPixel(x, y).r - wide.GetPixel(x, y).r));
                        inkNarrow += narrow.GetPixel(x, y).r;
                        inkWide += wide.GetPixel(x, y).r;
                    }
                TestContext.WriteLine($"largest difference {largest * 255:0.#} of 255, ink {inkNarrow:0.#} against {inkWide:0.#}");
                Assert.LessOrEqual(largest, 16 / 255f);
                Assert.AreEqual(inkNarrow, inkWide, inkNarrow * 0.005f);
            }
            finally
            {
                GlyphAtlas.DepthForTests = 0;
                GlyphAtlas.Clear();
            }
            Assert.AreEqual(8, GlyphAtlas.SdfDepth);
        }

        // Far from the edge 8 bits hold a step per 1/255 of the reach; 16 bits keep the field smooth there.
        [Test]
        public unsafe void SixteenBitsKeepTheFarFieldSmooth()
        {
            const int size = 64;
            var lines = new NativeList<float4>(8, Unity.Collections.Allocator.TempJob);
            lines.Add(new float4(24, 24, 40, 24));
            lines.Add(new float4(40, 24, 40, 40));
            lines.Add(new float4(40, 40, 24, 40));
            lines.Add(new float4(24, 40, 24, 24));
            var narrow = new byte[size * size];
            var wide = new ushort[size * size];
            var tiles = new Unity.Collections.NativeArray<SdfTile>(1, Unity.Collections.Allocator.TempJob);
            try
            {
                fixed (byte* target = narrow) tiles[0] = new SdfTile { Target = target, Stride = size, Width = size, Height = size, LineCount = 4, Spread = 20 };
                new SdfTileJob { Tiles = tiles, Lines = lines.AsArray() }.Run(1);
                fixed (ushort* target = wide) tiles[0] = new SdfTile { Target = (byte*)target, Stride = size * 2, Width = size, Height = size, LineCount = 4, Spread = 20, Wide = true };
                new SdfTileJob { Tiles = tiles, Lines = lines.AsArray() }.Run(1);
            }
            finally
            {
                tiles.Dispose();
                lines.Dispose();
            }
            // The true field of the square, against what each depth keeps of it.
            double narrowError = 0, wideError = 0;
            for (int row = 0; row < size; row++)
            {
                for (int column = 0; column < size; column++)
                {
                    float px = column + 0.5f, py = row + 0.5f;
                    float dx = Mathf.Max(24 - px, px - 40), dy = Mathf.Max(24 - py, py - 40);
                    float distance = dx <= 0 && dy <= 0 ? -Mathf.Max(dx, dy) : -new Vector2(Mathf.Max(dx, 0), Mathf.Max(dy, 0)).magnitude;
                    float exact = Mathf.Clamp01(0.5f + distance * 0.5f / 20);
                    int i = row * size + column;
                    Assert.AreEqual(narrow[i] / 255f, wide[i] / 65535f, 0.6f / 255, "the same field");
                    narrowError += Mathf.Abs(narrow[i] / 255f - exact);
                    wideError += Mathf.Abs(wide[i] / 65535f - exact);
                }
            }
            TestContext.WriteLine($"mean error {narrowError / (size * size) * 255:0.###} of 1/255 at 8 bits, {wideError / (size * size) * 255:0.###} at 16");
            Assert.Less(wideError, narrowError / 20);
        }
    }
}

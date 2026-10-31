using System;
using System.IO;
using NUnit.Framework;
using UnityEngine;

namespace FirstSymbol.Lettera.Editor.Tests
{
    /// <summary>COLRv1 through the atlas. The pixels are checked against Chromium outside Unity, since Chromium is the reference; here is how they arrive.</summary>
    public class ColorPaintTests : TextFixture
    {
        const string TestFont = "test_glyphs-glyf_colr_1.ttf";

        static void NeedsPaint()
        {
            if (LetteraNative.Version < 2) Assert.Inconclusive("This editor still holds the older native plugin, which cannot paint COLRv1. A restart loads the new one.");
        }

        static byte[] FontBytes()
        {
            string[] found = UnityEditor.AssetDatabase.FindAssets("FirstSymbol.Lettera.Editor.Tests t:asmdef");
            string asmdef = Path.GetFullPath(UnityEditor.AssetDatabase.GUIDToAssetPath(found[0]));
            return File.ReadAllBytes(Path.Combine(Path.GetDirectoryName(asmdef), "..", "Fonts~", TestFont));
        }

        [Test]
        public void AFontOfPaintsSaysSo()
        {
            NeedsPaint();
            FontFace face = Font(TestFont).LoadFace();
            Assert.IsTrue(face.HasColorPaint);
            Assert.IsTrue(face.IsColor);
        }

        [Test]
        public void APaintedGlyphTakesItsPlaceInTheColorAtlas()
        {
            NeedsPaint();
            FontFace face = Font(TestFont).LoadFace();
            FontInstance instance = face.Regular;
            uint glyph = LetteraNative.lt_font_glyph(instance.Font, 0xF0100, 0);
            Assert.AreNotEqual(0u, glyph);
            Assert.AreNotEqual(0, LetteraNative.lt_glyph_has_paint(face.Face, glyph));
            int before = GlyphAtlas.PaintedGlyphs;
            AtlasGlyph first = GlyphAtlas.Get(instance, glyph, 64);
            Assert.AreEqual(AtlasGlyph.Color, first.Kind);
            Assert.AreEqual(before + 1, GlyphAtlas.PaintedGlyphs);
            Assert.Greater(first.Right, first.Left);
            Assert.Greater(first.Top, first.Bottom);
            GlyphAtlas.Get(instance, glyph, 64);
            Assert.AreEqual(before + 1, GlyphAtlas.PaintedGlyphs, "the second time comes from the atlas");
        }

        [Test]
        public void AGlyphWithoutAPaintGoesTheOtherWay()
        {
            NeedsPaint();
            FontFace face = Font(TestFont).LoadFace();
            FontInstance instance = face.Regular;
            int before = GlyphAtlas.PaintedGlyphs;
            // The notdef of this font holds no paint.
            Assert.AreEqual(0, LetteraNative.lt_glyph_has_paint(face.Face, 0));
            GlyphAtlas.Get(instance, 0, 64);
            Assert.AreEqual(before, GlyphAtlas.PaintedGlyphs);
        }

        [Test]
        public void AnOlderLibraryDrawsWhatElseTheFontHolds()
        {
            int was = LetteraNative.VersionOverride;
            try
            {
                LetteraNative.VersionOverride = 1;
                Assert.AreEqual(1, LetteraNative.Version);
                byte[] data = FontBytes();
                LetteraFont font = LetteraFont.CreateFromBytes(data);
                FontFace face = font.LoadFace();
                Assert.IsFalse(face.HasColorPaint);
                FontInstance instance = face.Regular;
                Assert.DoesNotThrow(() => GlyphAtlas.Get(instance, 1, 64));
            }
            finally
            {
                LetteraNative.VersionOverride = was;
            }
        }

        [Test]
        public void BrokenFontsNeverCrashAndFallThrough()
        {
            NeedsPaint();
            byte[] clean = FontBytes();
            var random = new System.Random(11);
            for (int run = 0; run < 100; run++)
            {
                var bytes = (byte[])clean.Clone();
                for (int flip = 0; flip < 40; flip++) bytes[random.Next(256, bytes.Length)] ^= (byte)(1 << random.Next(8));
                LetteraFont font = LetteraFont.CreateFromBytes(bytes);
                FontFace face = font.LoadFace();
                if (face == null) continue;
                FontInstance instance = face.Regular;
                for (uint glyph = 0; glyph < 16; glyph++) GlyphAtlas.Get(instance, glyph, 64);
            }
        }

        [Test]
        public void SixtyFourGlyphsPaintInTime()
        {
            NeedsPaint();
            FontFace face = Font(TestFont).LoadFace();
            FontInstance instance = face.Regular;
            var clock = System.Diagnostics.Stopwatch.StartNew();
            int painted = GlyphAtlas.PaintedGlyphs;
            for (uint glyph = 1; glyph < 200 && GlyphAtlas.PaintedGlyphs - painted < 64; glyph++) GlyphAtlas.Get(instance, glyph, 64);
            clock.Stop();
            TestContext.WriteLine($"{GlyphAtlas.PaintedGlyphs - painted} glyphs in {clock.ElapsedMilliseconds} ms");
            Assert.Less(clock.ElapsedMilliseconds, 1000);
        }
    }
}

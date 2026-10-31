using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using FirstSymbol.Lettera.Editor;

namespace FirstSymbol.Lettera.Editor.Tests
{
    public class FontSubsetTests : TextFixture
    {
        static byte[] FontFile(string file)
        {
            string asmdef = AssetDatabase.GUIDToAssetPath(AssetDatabase.FindAssets("FirstSymbol.Lettera.Editor.Tests t:asmdef")[0]);
            return File.ReadAllBytes(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(asmdef)), "..", "Fonts~", file));
        }

        static LetteraFont Stored(string file)
        {
            var font = ScriptableObject.CreateInstance<LetteraFont>();
            font.hideFlags = HideFlags.DontSave;
            font.SetFontData(FontFile(file));
            return font;
        }

        [SetUp]
        public void NeedsTheTool()
        {
            if (!LetteraFontSubset.IsAvailable) Assert.Inconclusive("This platform's editor has no copy of the subset tool.");
        }

        static bool Covers(LetteraFont font, int codepoint) => font.LoadFace().HasGlyph(codepoint);

        static LetteraFont Made(LetteraSubsetResult result)
        {
            Assert.IsTrue(result.Succeeded, result.Refused);
            return LetteraFont.CreateFromBytes(result.Bytes);
        }

        [Test]
        public void KeepingLatinMakesItSmallerAndKeepsWhatWasAskedFor()
        {
            LetteraFont source = Stored("NotoSans-Regular.ttf");
            var clock = System.Diagnostics.Stopwatch.StartNew();
            LetteraSubsetResult result = LetteraFontSubset.Run(source, new LetteraSubsetOptions { Characters = LetteraFontSubset.Presets["Basic Latin"] });
            TestContext.WriteLine($"{clock.ElapsedMilliseconds} ms, {result.BytesBefore} to {result.BytesAfter} bytes, {result.GlyphsBefore} to {result.GlyphsAfter} glyphs");
            Assert.Less(clock.ElapsedMilliseconds, 5000);
            LetteraFont made = Made(result);
            Assert.Less(result.BytesAfter, result.BytesBefore);
            Assert.IsTrue(Covers(made, 'A'));
            Assert.IsFalse(Covers(made, 0x0416), "Cyrillic went");
            Assert.AreEqual(source.FamilyName, made.FamilyName, "names survive");
        }

        [Test]
        public void LigaturesAndSmallCapsStay()
        {
            LetteraFont source = Stored("NotoSans-Regular.ttf");
            LetteraFont made = Made(LetteraFontSubset.Run(source, new LetteraSubsetOptions { Characters = LetteraFontSubset.Presets["Basic Latin"] }));
            Assert.AreEqual(Shape(source, "ffi office", null), Shape(made, "ffi office", null), "same number of glyphs after ligatures");
            Assert.AreEqual(Shape(source, "abc", "smcp"), Shape(made, "abc", "smcp"));
        }

        static unsafe int Shape(LetteraFont font, string text, string feature)
        {
            FontFace face = font.LoadFace();
            var codepoints = new int[text.Length];
            for (int i = 0; i < text.Length; i++) codepoints[i] = text[i];
            var output = new NativeGlyph[64];
            var features = new NativeFeature[1];
            if (feature != null) features[0] = new NativeFeature { Tag = LetteraNative.Tag(feature), Value = 1, Start = 0, End = uint.MaxValue };
            fixed (int* t = codepoints)
            fixed (NativeGlyph* o = output)
            fixed (NativeFeature* f = features)
                return LetteraNative.lt_shape(face.Regular.Font, t, text.Length, 0, text.Length, 0, 0, null, f, feature != null ? 1 : 0, o, output.Length);
        }

        [Test]
        public void TheMissingGlyphBoxStays()
        {
            LetteraFont made = Made(LetteraFontSubset.Run(Stored("NotoSans-Regular.ttf"), new LetteraSubsetOptions { Characters = "abc" }));
            Assert.Greater(OutlineOf(made, 0), 0);
        }

        static unsafe int OutlineOf(LetteraFont font, uint glyph) => LetteraNative.lt_glyph_outline(font.LoadFace().Regular.Font, glyph, null, 0);

        [Test]
        public void RemovingTakesOnlyThose()
        {
            LetteraFont made = Made(LetteraFontSubset.Run(Stored("NotoSans-Regular.ttf"), new LetteraSubsetOptions { Mode = LetteraSubsetMode.Remove, Characters = "U+0400-04FF" }));
            Assert.IsFalse(Covers(made, 0x0416));
            Assert.IsTrue(Covers(made, 'A'));
            Assert.IsTrue(Covers(made, 0x03A9), "Greek stayed");
        }

        [Test]
        public void AFontOfPaintsKeepsItsColors()
        {
            LetteraSubsetResult result = LetteraFontSubset.Run(Stored("test_glyphs-glyf_colr_1.ttf"), new LetteraSubsetOptions { Characters = "U+F0100-F0110" });
            LetteraFont made = Made(result);
            Assert.AreNotEqual(0, LetteraNative.lt_face_color_flags(made.LoadFace().Face) & 2);
            Assert.IsTrue(Covers(made, 0xF0100));
        }

        [Test]
        public void NothingLeftIsRefused()
        {
            LetteraSubsetResult result = LetteraFontSubset.Run(Stored("NotoSans-Regular.ttf"), new LetteraSubsetOptions { Characters = "U+1F600" });
            Assert.IsFalse(result.Succeeded);
            StringAssert.Contains("empty", result.Refused);
        }

        [Test]
        public void ABrokenRangeIsSaidOutLoud()
        {
            LetteraSubsetResult result = LetteraFontSubset.Run(Stored("NotoSans-Regular.ttf"), new LetteraSubsetOptions { Characters = "U+ZZ" });
            Assert.IsFalse(result.Succeeded);
            Assert.IsNotNull(result.Refused);
            var set = new HashSet<uint>();
            Assert.IsTrue(LetteraFontSubset.ParseCharacters("aU+0041-0043 U+1F600", set, out _));
            CollectionAssert.IsSupersetOf(set, new uint[] { 'a', 0x41, 0x42, 0x43, 0x1F600 });
            Assert.IsFalse(LetteraFontSubset.ParseCharacters("U+0043-0041", new HashSet<uint>(), out _), "backwards");
        }

        // A font with the table directory of a bitmap font: a verdict needs no more than that.
        static byte[] WithTables(params string[] tags)
        {
            var bytes = new byte[12 + tags.Length * 16];
            bytes[4] = 0;
            bytes[5] = (byte)tags.Length;
            for (int i = 0; i < tags.Length; i++)
                for (int c = 0; c < 4; c++)
                    bytes[12 + i * 16 + c] = (byte)tags[i][c];
            return bytes;
        }

        [Test]
        public void BitmapsAloneAreRefused()
        {
            var font = ScriptableObject.CreateInstance<LetteraFont>();
            font.hideFlags = HideFlags.DontSave;
            font.SetFontData(WithTables("EBDT", "EBLC", "cmap"));
            LetteraSubsetResult result = LetteraFontSubset.Run(font, new LetteraSubsetOptions { Characters = "a" });
            Assert.IsFalse(result.Succeeded);
            StringAssert.Contains("bitmaps", result.Refused);
        }

        [Test]
        public void UndoBringsItBack()
        {
            LetteraFont font = Stored("NotoSans-Regular.ttf");
            int before = font.OriginalSize;
            LetteraSubsetResult result = LetteraFontSubset.Run(font, new LetteraSubsetOptions { Characters = "abc" });
            LetteraFontSubset.Replace(font, result.Bytes);
            Assert.Less(font.OriginalSize, before);
            Undo.PerformUndo();
            Assert.AreEqual(before, font.OriginalSize);
        }

        [Test]
        public void SavingAsLeavesTheSourceAlone()
        {
            LetteraFont font = Stored("NotoSans-Regular.ttf");
            int before = font.OriginalSize;
            LetteraSubsetResult result = LetteraFontSubset.Run(font, new LetteraSubsetOptions { Characters = "abc" });
            const string folder = "Assets/LetteraSubsetTest";
            AssetDatabase.CreateFolder("Assets", "LetteraSubsetTest");
            try
            {
                LetteraFont made = LetteraFontSubset.SaveAs(font, result.Bytes, folder + "/Made.asset");
                Assert.AreEqual(result.Bytes.Length, made.OriginalSize);
                Assert.AreEqual(before, font.OriginalSize);
            }
            finally
            {
                AssetDatabase.DeleteAsset(folder);
            }
        }
    }
}

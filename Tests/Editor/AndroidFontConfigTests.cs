using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;

namespace FirstSymbol.Lettera.Editor.Tests
{
    /// <summary>Android's font configuration, read from fixture files in the device's formats. No device is involved.</summary>
    public class AndroidFontConfigTests
    {
        static string Fixture(string file)
        {
            string asmdef = AssetDatabase.GUIDToAssetPath(AssetDatabase.FindAssets("FirstSymbol.Lettera.Editor.Tests t:asmdef")[0]);
            return File.ReadAllText(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(asmdef)), "..", "AndroidFonts~", file));
        }

        static AndroidFontConfig Config(string file = "fonts.xml") => AndroidFontConfig.Parse(Fixture(file));

        // Every file is found, under a folder of the test's own.
        static string Locate(string file) => "/fixture/fonts/" + file;

        static string[] Languages(List<AndroidFontConfig.Family> families) => families.Select(f => string.Join(" ", f.Languages) + (f.Variant != null ? "/" + f.Variant : "")).ToArray();

        [Test]
        public void NamedFamiliesAliasesAndWeightsAreRead()
        {
            AndroidFontConfig config = Config();
            AndroidFontConfig.Family sans = config.Named("sans-serif", out int weight);
            Assert.NotNull(sans);
            Assert.AreEqual(0, weight);
            Assert.AreEqual(5, sans.Fonts.Count);
            Assert.AreEqual("Roboto-Regular.ttf", sans.Fonts[0].File, "the file name, without the axes inside the element");
            Assert.AreEqual(100, sans.Fonts[0].Weight);
            Assert.IsTrue(sans.Fonts[4].Italic);

            Assert.AreSame(sans, config.Named("sans-serif-light", out weight));
            Assert.AreEqual(300, weight, "an alias's weight");
            Assert.AreSame(sans, config.Named("Arial", out _), "aliases ignore case");
            Assert.AreSame(sans, config.Named("Roboto", out _), "Roboto means the sans-serif role");
            Assert.AreSame(sans, config.Named("Noto Sans", out _));
            Assert.AreSame(config.Named("serif", out _), config.Named("times", out _));
            Assert.IsNull(config.Named("no-such-family", out _));
        }

        [Test]
        public void AWeightPicksTheNearestFontByTheCssRules()
        {
            AndroidFontConfig config = Config();
            AndroidFontConfig.Family sans = config.Named("sans-serif", out _);
            Assert.AreEqual(300, AndroidFontConfig.Pick(sans, 300, false).Weight);
            Assert.AreEqual(400, AndroidFontConfig.Pick(sans, 0, false).Weight, "no weight is 400");
            Assert.AreEqual(700, AndroidFontConfig.Pick(sans, 600, false).Weight, "above 500 looks up first");
            Assert.AreEqual(400, AndroidFontConfig.Pick(sans, 500, false).Weight, "500 looks down when nothing is between 500 and itself");
            Assert.AreEqual(300, AndroidFontConfig.Pick(sans, 350, false).Weight, "below 400 looks down first");
            Assert.IsTrue(AndroidFontConfig.Pick(sans, 400, true).Italic);
            AndroidFontConfig.Family serif = config.Named("serif", out _);
            Assert.AreEqual("NotoSerif-Bold.ttf", AndroidFontConfig.Pick(serif, 700, false).File);
        }

        [Test]
        public void ChineseJapaneseAndKoreanFollowTheDevicesLanguages()
        {
            AndroidFontConfig config = Config();
            Assert.AreEqual("zh-Hans", config.Fallbacks(new[] { "en-US" }, false).First(f => f.Languages.Length > 0 && !f.Languages[0].StartsWith("und")).Languages[0], "a device in English keeps the file's order");
            Assert.AreEqual("ja", config.Fallbacks(new[] { "ja-JP" }, false)[0].Languages[0]);
            Assert.AreEqual("ko", config.Fallbacks(new[] { "ko-KR" }, false)[0].Languages[0]);
            Assert.AreEqual("zh-Hant zh-Bopo", string.Join(" ", config.Fallbacks(new[] { "zh-TW" }, false)[0].Languages), "Taiwan reads Traditional");
            Assert.AreEqual("zh-Hant zh-Bopo", string.Join(" ", config.Fallbacks(new[] { "zh-Hant-HK" }, false)[0].Languages));
            Assert.AreEqual("zh-Hans", config.Fallbacks(new[] { "zh-CN" }, false)[0].Languages[0]);
            string[] second = Languages(config.Fallbacks(new[] { "en-GB", "ko-KR", "ja-JP" }, false));
            Assert.AreEqual("ko", second[0], "the device's second language before its third");
            Assert.AreEqual("ja", second[1]);
            Assert.AreEqual("und-Arab/compact", second[2], "the rest keep their order");

            // As files: the face of the collection each language takes, and no serif stand-in.
            List<(string path, int index)> files = LetteraSystemFonts.AndroidFallbackFiles(config, new[] { "ja-JP" }, false, Locate);
            Assert.AreEqual(("/fixture/fonts/NotoSansCJK-Regular.ttc", 0), files[0]);
            CollectionAssert.Contains(files, ("/fixture/fonts/NotoSansCJK-Regular.ttc", 2));
            CollectionAssert.DoesNotContain(files.Select(f => f.path).ToList(), "/fixture/fonts/NotoSerifCJK-Regular.ttc");
            Assert.AreEqual(("/fixture/fonts/NotoSansCJK-Regular.ttc", 2), LetteraSystemFonts.AndroidFallbackFiles(config, new[] { "zh-CN" }, false, Locate)[0]);
            Assert.AreEqual("ja", config.ForLanguage("ja-JP")[0].Languages[0], "a text's own language finds its family");
        }

        [Test]
        public void Android15DrawsTheTallVariants()
        {
            AndroidFontConfig config = Config();
            string[] compact = Languages(config.Fallbacks(new[] { "en" }, false));
            string[] tall = Languages(config.Fallbacks(new[] { "en" }, true));
            CollectionAssert.Contains(compact, "und-Arab/compact");
            CollectionAssert.DoesNotContain(compact, "und-Arab/elegant");
            CollectionAssert.Contains(tall, "und-Arab/elegant");
            CollectionAssert.DoesNotContain(tall, "und-Thai/compact");
            CollectionAssert.Contains(tall, "und-Hebr", "a family with no variant serves both");
            Assert.IsTrue(AndroidFontConfig.UsesTallVariants(35, 35));
            Assert.IsFalse(AndroidFontConfig.UsesTallVariants(35, 34), "an app made for an older system keeps the compact ones");
            Assert.IsFalse(AndroidFontConfig.UsesTallVariants(34, 35));

            AndroidFontConfig newer = Config("font_fallback.xml");
            Assert.AreEqual("NotoNaskhArabic-Regular.ttf", LetteraSystemFonts.AndroidFallbackFiles(newer, new[] { "en" }, true, Locate)[0].path.Substring("/fixture/fonts/".Length));
            Assert.AreSame(newer.Named("sans-serif", out _), newer.Named("sans-serif-black", out int black));
            Assert.AreEqual(900, black);
        }

        [Test]
        public void AVendorsFamiliesComeOnTop()
        {
            AndroidFontConfig config = Config();
            config.Customize(Fixture("fonts_customization.xml"));
            Assert.AreEqual("VendorSans-Bold.ttf", AndroidFontConfig.Pick(config.Named("vendor-sans-bold", out int weight), weight, false).File);
            Assert.AreEqual("VendorMono-Regular.ttf", config.Named("monospace", out _).Fonts[0].File, "a vendor's family of a known name replaces it");
            Assert.AreEqual("Roboto-Regular.ttf", config.Named("sans-serif", out _).Fonts[0].File, "the others stay");
            List<AndroidFontConfig.Family> japanese = config.Fallbacks(new[] { "ja" }, false);
            Assert.AreEqual("VendorJapanese-Regular.ttf", japanese[0].Fonts[0].File, "prepended before the system's own");
            Assert.AreEqual("NotoSansCJK-Regular.ttc", japanese[1].Fonts[0].File);
        }

        [Test]
        public void TheOldestFormatStillReads()
        {
            AndroidFontConfig config = Config("fallback_fonts.xml");
            List<(string path, int index)> files = LetteraSystemFonts.AndroidFallbackFiles(config, new[] { "en" }, false, Locate);
            CollectionAssert.AreEqual(new[] { "DroidNaskh-Regular.ttf", "DroidSansHebrew-Regular.ttf", "MTLmr3m.ttf", "DroidSansFallback.ttf" }, files.Select(f => Path.GetFileName(f.path)).ToArray());
            Assert.AreEqual("MTLmr3m.ttf", Path.GetFileName(LetteraSystemFonts.AndroidFallbackFiles(config, new[] { "ja" }, false, Locate)[0].path), "its language is on the file");
        }

        [Test]
        public void ADefaultFamilyIsItsFilesOnceWithTheRegularFirst()
        {
            AndroidFontConfig config = Config();
            List<(string path, int index)> sans = LetteraSystemFonts.AndroidFamilyFiles(config.Named("sans-serif", out _), Locate);
            CollectionAssert.AreEqual(new[] { ("/fixture/fonts/Roboto-Regular.ttf", 0), ("/fixture/fonts/Roboto-Italic.ttf", 0) }, sans, "one variable file holds every weight");
            List<(string path, int index)> serif = LetteraSystemFonts.AndroidFamilyFiles(config.Named("serif", out _), f => f == "NotoSerif-Bold.ttf" ? null : Locate(f));
            Assert.AreEqual(2, serif.Count, "a missing file is passed over");
        }

        [Test]
        public void LocalesReadTheirScript()
        {
            Assert.AreEqual("Hant", AndroidFontConfig.Locale.Of("zh-HK").Script);
            Assert.AreEqual("Hant", AndroidFontConfig.Locale.Of("zh_TW").Script);
            Assert.AreEqual("Hans", AndroidFontConfig.Locale.Of("zh").Script);
            Assert.AreEqual("Hans", AndroidFontConfig.Locale.Of("zh-Hans-HK").Script, "a script named wins over the region");
            Assert.AreEqual("he", AndroidFontConfig.Locale.Of("iw-IL").Language);
            Assert.IsNull(AndroidFontConfig.Locale.Of("").Language);
        }

        [Test]
        public void BrokenConfigurationsReadAsFarAsTheyGo()
        {
            Assert.AreEqual(0, AndroidFontConfig.Parse("").Families.Count);
            Assert.AreEqual(0, AndroidFontConfig.Parse("<familyset><family name=\"x\"><font weight=").Families.Count);
            AndroidFontConfig cut = AndroidFontConfig.Parse("<familyset><!-- <family name=\"hidden\"><font>A.ttf</font></family> --><family name=\"a\"><font weight=\"bold\">A&amp;B.ttf</font></family><family lang=\"ja\"><font>J.ttf");
            Assert.AreEqual(1, cut.Families.Count);
            Assert.AreEqual("A&B.ttf", cut.Families[0].Fonts[0].File);
            Assert.AreEqual(400, cut.Families[0].Fonts[0].Weight, "a weight that is not a number is 400");
        }
    }
}

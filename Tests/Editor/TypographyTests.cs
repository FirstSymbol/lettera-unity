using System.IO;
using NUnit.Framework;
using UnityEngine;

namespace FirstSymbol.Lettera.Editor.Tests
{
    public class TypographyTests : TextFixture
    {
        static float AdvanceOf(LetteraWorldText text, int index) => text.GetGlyph(index).Advance;

        LetteraWorldText Stacked(string markup, params LetteraFontAsset[] fonts)
        {
            LetteraWorldText text = Make("");
            text.Font = null;
            text.FontStack = Stack(fonts);
            text.Text = markup;
            text.ForceRebuild();
            return text;
        }

        // ------------------------------------------------------------- case

        [Test]
        public void UppercaseShapesCapitalsAndKeepsTheText()
        {
            LetteraWorldText upper = Make("<uppercase>abc</uppercase>");
            LetteraWorldText capitals = Make("ABC");
            CollectionAssert.AreEqual(Glyphs(capitals), Glyphs(upper));
            Assert.AreEqual("abc", upper.CleanText);
        }

        [Test]
        public void SimpleMappingsKeepSharpS()
        {
            LetteraWorldText upper = Make("<uppercase>straße</uppercase>");
            Assert.AreEqual("straße", upper.CleanText);
            CollectionAssert.AreEqual(Glyphs(Make("STRAßE")), Glyphs(upper));
            Assert.AreEqual(6, upper.GlyphCount);
        }

        [Test]
        public void LowercaseShapesSmallLetters() => CollectionAssert.AreEqual(Glyphs(Make("abc")), Glyphs(Make("<lowercase>ABC</lowercase>")));

        [Test]
        public void CaseLeavesArabicAlone()
        {
            const string word = "مرحبا";
            CollectionAssert.AreEqual(Glyphs(Make(word, font: Arabic)), Glyphs(Make("<uppercase>" + word + "</uppercase>", font: Arabic)));
        }

        // ------------------------------------------------------------- features

        [Test]
        public void KerningCanBeTurnedOff()
        {
            float kerned = WidthOf(Make("AV"));
            float unkerned = WidthOf(Make("<otf=kern 0>AV</otf>"));
            Assert.Greater(unkerned, kerned + 0.01f);
        }

        [Test]
        public void LigatureCanBeTurnedOff()
        {
            int with = Make("fi").GlyphCount;
            Assert.AreEqual(1, with, "Noto Sans forms the fi ligature.");
            Assert.AreEqual(2, Make("<otf=-liga>fi</otf>").GlyphCount);
        }

        [TestCase("onum")]
        [TestCase("+onum")]
        [TestCase("onum 1")]
        [TestCase("onum:1")]
        [TestCase("onum=1")]
        public void FeatureSyntaxForms(string token)
        {
            Assert.IsTrue(OpenTypeEffect.TryParse(token, out uint tag, out uint value));
            Assert.AreEqual(LetteraNative.Tag("onum"), tag);
            Assert.AreEqual(1u, value);
        }

        [TestCase("-dlig", 0u)]
        [TestCase("cv02 3", 3u)]
        public void FeatureValues(string token, uint expected)
        {
            Assert.IsTrue(OpenTypeEffect.TryParse(token, out _, out uint value));
            Assert.AreEqual(expected, value);
        }

        [TestCase("")]
        [TestCase("toolong")]
        [TestCase("cv02 x")]
        public void MalformedFeaturesAreIgnored(string token) => Assert.IsFalse(OpenTypeEffect.TryParse(token, out _, out _));

        [Test]
        public void MalformedTokenLeavesTheOthersWorking()
        {
            Assert.AreEqual(2, Make("<otf=toolong,-liga>fi</otf>").GlyphCount);
        }

        [Test]
        public void UnknownFeatureChangesNothing()
        {
            CollectionAssert.AreEqual(Glyphs(Make("abc")), Glyphs(Make("<otf=zzzz>abc</otf>")));
        }

        [Test]
        public void NestedFeaturesInnermostWins()
        {
            LetteraWorldText text = Make("<otf=-liga>fi<otf=liga>fi</otf></otf>");
            Assert.AreEqual(3, text.GlyphCount);
        }

        [Test]
        public void EqualFeatureSetsDoNotSplitRuns()
        {
            LetteraWorldText split = Make("<otf=liga 1>f</otf><otf=liga 1>i</otf>");
            Assert.AreEqual(1, split.GlyphCount, "The ligature forms across two ranges with the same set.");
            Assert.AreEqual(2, Make("<otf=liga 1>f</otf><otf=liga 0>i</otf>").GlyphCount, "Different sets do split.");
        }

        [Test]
        public void ArabicJoinsAcrossAFeatureBoundary()
        {
            const string word = "مرحبا";
            CollectionAssert.AreEqual(Glyphs(Make(word, font: Arabic)), Glyphs(Make("مر<otf=zzzz>ح</otf>با", font: Arabic)));
        }

        // ------------------------------------------------------------- small caps

        [Test]
        public void SmallCapsSynthesizeOnAFaceWithoutThem()
        {
            FontFace face = Latin.LoadFace();
            Assume.That(!face.ChangesGlyph(LetteraNative.Tag("smcp"), 'a'), "This copy of Noto Sans has real small caps.");
            LetteraWorldText text = Make("<smallcaps>aA1</smallcaps>");
            CollectionAssert.AreEqual(Glyphs(Make("AA1")), Glyphs(text), "Small letters use the capital glyph.");
            Assert.AreEqual(20f * 0.8f, text.GetGlyph(0).Size, 1e-3f);
            Assert.AreEqual(20f, text.GetGlyph(1).Size, 1e-3f, "A capital stays full size.");
            Assert.AreEqual(20f, text.GetGlyph(2).Size, 1e-3f, "A digit stays full size.");
            Assert.AreEqual("aA1", text.CleanText);
        }

        [Test]
        public void SmallCapsUseTheFontsOwnWhenItHasThem()
        {
            FontFace face = Latin.LoadFace();
            Assume.That(face.ChangesGlyph(LetteraNative.Tag("smcp"), 'a'), "This copy of Noto Sans has no small caps.");
            LetteraWorldText text = Make("<smallcaps>a</smallcaps>b");
            LetteraWorldText plain = Make("ab");
            Assert.AreNotEqual(plain.GetGlyph(0).Glyph, text.GetGlyph(0).Glyph);
            Assert.AreEqual(plain.GetGlyph(1).Glyph, text.GetGlyph(1).Glyph, "The feature does not spread past the range.");
            Assert.AreEqual(20f, text.GetGlyph(0).Size, 1e-3f);
        }

        // ------------------------------------------------------------- super and subscript

        [Test]
        public void SubscriptLowersOrUsesInferiors()
        {
            LetteraWorldText plain = Make("H2O");
            LetteraWorldText text = Make("H<sub>2</sub>O");
            Assert.AreEqual("H2O", text.CleanText);
            bool real = Latin.LoadFace().ChangesGlyph(LetteraNative.Tag("subs"), '2');
            if (real)
            {
                Assert.AreNotEqual(plain.GetGlyph(1).Glyph, text.GetGlyph(1).Glyph);
            }
            else
            {
                Assert.Less(text.GetGlyph(1).Size, 20f);
                Assert.Less(text.GetGlyph(1).Y, plain.GetGlyph(1).Y);
            }
        }

        [Test]
        public void SuperscriptRaisesOrUsesSuperiors()
        {
            LetteraWorldText plain = Make("x2");
            LetteraWorldText text = Make("x<sup>2</sup>");
            bool real = Latin.LoadFace().ChangesGlyph(LetteraNative.Tag("sups"), '2');
            if (real) Assert.AreNotEqual(plain.GetGlyph(1).Glyph, text.GetGlyph(1).Glyph);
            else Assert.Greater(text.GetGlyph(1).Y, plain.GetGlyph(1).Y);
        }

        [Test]
        public void SubscriptAfterAHebrewRunStillApplies()
        {
            LetteraWorldText text = Stacked("שלום H<sub>2</sub>O", Latin, Hebrew);
            LetteraWorldText plain = Stacked("שלום H2O", Latin, Hebrew);
            bool same = true;
            for (int i = 0; i < text.GlyphCount; i++)
                same &= text.GetGlyph(i).Glyph == plain.GetGlyph(i).Glyph && Mathf.Approximately(text.GetGlyph(i).Y, plain.GetGlyph(i).Y) && Mathf.Approximately(text.GetGlyph(i).Size, plain.GetGlyph(i).Size);
            Assert.IsFalse(same);
        }

        // ------------------------------------------------------------- axes

        static LetteraFont s_Variable;

        // Windows ships a variable font with weight and width; other machines skip these tests.
        static LetteraFont Variable()
        {
            if (s_Variable != null) return s_Variable;
            string path = Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.Windows), "Fonts", "bahnschrift.ttf");
            Assume.That(File.Exists(path), "No variable system font here.");
            s_Variable = LetteraFont.CreateFromBytes(File.ReadAllBytes(path));
            return s_Variable;
        }

        [Test]
        public void WidthAxisNarrowsAdvances()
        {
            LetteraFont font = Variable();
            float regular = WidthOf(Make("HHHH", font: font));
            float narrow = WidthOf(Make("<axes=~,75>HHHH</axes>", font: font));
            Assert.Less(narrow, regular - 0.5f);
        }

        // How wide the first glyph's ink is, in a font whose advances may not change with weight.
        static float QuadWidth(LetteraWorldText text) => text.Engine.Vertices[2].x - text.Engine.Vertices[0].x;

        [Test]
        public void WeightAxisChangesTheOutline()
        {
            LetteraFont font = Variable();
            Assert.AreNotEqual(QuadWidth(Make("H", font: font)), QuadWidth(Make("<axes=700>H</axes>", font: font)));
        }

        [Test]
        public void OutOfRangeValuesClamp()
        {
            LetteraFont font = Variable();
            Assert.AreEqual(QuadWidth(Make("<axes=700>H</axes>", font: font)), QuadWidth(Make("<axes=99999>H</axes>", font: font)), 1e-3f);
        }

        [Test]
        public void AxisFormsAgree()
        {
            LetteraFont font = Variable();
            float absolute = QuadWidth(Make("<axes=700>H</axes>", font: font));
            Assert.AreNotEqual(QuadWidth(Make("H", font: font)), absolute);
            Assert.AreEqual(absolute, QuadWidth(Make("<axes=+300>H</axes>", font: font)), 1e-3f, "A delta from the default of 400.");
            Assert.AreEqual(absolute, QuadWidth(Make("<axes=175%>H</axes>", font: font)), 1e-3f, "A percent of the default.");
            Assert.AreEqual(absolute, QuadWidth(Make("<axes=wght 700>H</axes>", font: font)), 1e-3f, "A named pair.");
        }

        [Test]
        public void StaticFontIgnoresAxes()
        {
            CollectionAssert.AreEqual(Glyphs(Make("abc")), Glyphs(Make("<axes=900,50>abc</axes>")));
            Assert.AreEqual(WidthOf(Make("abc")), WidthOf(Make("<axes=900,50>abc</axes>")));
        }

        [Test]
        public void TwoInstancesAreTwoAtlasEntries()
        {
            LetteraFont font = Variable();
            int before = GlyphAtlas.GlyphCount;
            Make("<axes=301>Q</axes>", font: font);
            int one = GlyphAtlas.GlyphCount;
            Make("<axes=302>Q</axes>", font: font);
            Assert.Greater(one, before);
            Assert.Greater(GlyphAtlas.GlyphCount, one);
        }

        // ------------------------------------------------------------- variants

        [Test]
        public void VariantsOfOneFileRenderDifferentWeights()
        {
            LetteraFont font = Variable();
            var light = ScriptableObject.CreateInstance<LetteraFontVariant>();
            var heavy = ScriptableObject.CreateInstance<LetteraFontVariant>();
            light.Source = heavy.Source = font;
            light.Axes.Add(new LetteraFontVariant.AxisValue { Tag = "wght", Value = 300 });
            heavy.Axes.Add(new LetteraFontVariant.AxisValue { Tag = "wght", Value = 700 });
            Assert.AreNotEqual(QuadWidth(Make("H", font: light)), QuadWidth(Make("H", font: heavy)));
            Object.DestroyImmediate(light);
            Object.DestroyImmediate(heavy);
        }

        [Test]
        public void VariantWithoutASourceFallsBackWithoutAnException()
        {
            var variant = ScriptableObject.CreateInstance<LetteraFontVariant>();
            Assert.IsTrue(variant.HasError);
            Assert.DoesNotThrow(() => Make("abc", font: variant));
            Object.DestroyImmediate(variant);
        }

        [Test]
        public void VariantOverridesWhatTheFileStates()
        {
            var variant = ScriptableObject.CreateInstance<LetteraFontVariant>();
            variant.Source = Latin;
            variant.Family = "Renamed";
            variant.Weight = 800;
            Assert.AreEqual("Renamed", variant.FamilyName);
            Object.DestroyImmediate(variant);
        }

        // ------------------------------------------------------------- size match and boost

        [Test]
        public void SizeMatchMetricChangesTheFallbacksSize()
        {
            // Only a fallback whose two ratios differ from the primary's in different ways can tell the metrics apart.
            LetteraFontAsset fallback = null;
            string word = "";
            FaceMetrics primary = Latin.LoadFace().Metrics;
            float wanted = (float)primary.XHeight / primary.CapHeight;
            foreach ((LetteraFontAsset font, string sample) in new (LetteraFontAsset, string)[] { (Devanagari, "देव"), (Arabic, "سلام"), (Hebrew, "שלום") })
            {
                FaceMetrics m = font.LoadFace().Metrics;
                if (Mathf.Abs((float)m.XHeight / m.CapHeight - wanted) < 0.01f) continue;
                fallback = font;
                word = sample;
                break;
            }
            Assume.That(fallback != null, "All test fonts share the Latin proportions.");
            LetteraWorldText x = Stacked("<size-match=XHeight>a</size-match>" + word, Latin, fallback);
            LetteraWorldText cap = Stacked("<size-match=CapHeight>a</size-match>" + word, Latin, fallback);
            float a = 0, b = 0;
            for (int i = 0; i < x.GlyphCount; i++)
            {
                a += x.GetGlyph(i).Advance;
                b += cap.GetGlyph(i).Advance;
            }
            Assert.AreNotEqual(a, b);
        }

        [Test]
        public void BoostRaisesDensityForEveryUser()
        {
            GlyphAtlas.Clear();
            var boost = new ResolutionEffect { Boost = 1 };
            LetteraWorldText text = Make("");
            text.Styles.Add(LetteraStyle.ForTag(boost, "k"));
            text.MarkStylesDirty();
            text.Text = "<k>A</k>";
            text.ForceRebuild();
            Vector4 boosted = text.Engine.Uv0[2] - text.Engine.Uv0[0];
            Vector4 second = Make("A").Engine.Uv0[2] - Make("A").Engine.Uv0[0];
            Assert.AreEqual(boosted.x, second.x, 1e-6f, "A second text reuses the sharper entry.");

            int before = GlyphAtlas.GlyphCount;
            Make("A");
            Assert.AreEqual(before, GlyphAtlas.GlyphCount);

            GlyphAtlas.Clear();
            Vector4 normal = Make("B").Engine.Uv0[2] - Make("B").Engine.Uv0[0];
            Assert.Greater(boosted.x, normal.x * 1.2f, "The tile covers more of the page.");
        }

        // ------------------------------------------------------------- run details

        [Test]
        public void TaggedTextKeepsItsAdvancesWithoutEffectsOnThem()
        {
            LetteraWorldText plain = Make("abc");
            LetteraWorldText text = Make("a<otf=zzzz>b</otf>c");
            for (int i = 0; i < 3; i++) Assert.AreEqual(AdvanceOf(plain, i), AdvanceOf(text, i), 1e-3f);
        }
    }
}

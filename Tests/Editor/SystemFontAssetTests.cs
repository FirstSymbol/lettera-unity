using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;

namespace FirstSymbol.Lettera.Editor.Tests
{
    /// <summary>A system font asset's weight and its overrides, per platform. Needs a machine with Arial or Segoe UI; elsewhere it is ignored.</summary>
    public class SystemFontAssetTests : TextFixture
    {
        LetteraSystemFont Named(params string[] names)
        {
            var font = ScriptableObject.CreateInstance<LetteraSystemFont>();
            Created.Add(font);
            font.Common = names;
            if (font.LoadFace() == null || font.ResolveFailed) Assert.Ignore("none of " + string.Join(", ", names) + " is installed here");
            return font;
        }

        // The settings of the platform the editor runs on, as a player there would read them.
        static LetteraSystemFontPlatform Here(LetteraSystemFont font) => font.PlatformSettings() ?? throw new IgnoreException("this platform has no settings");

        static FontInstance Drawn(LetteraFontAsset font, int codepoint, int weight = 0)
        {
            FontChain chain = LetteraFontChains.Get(font, null);
            FontFace face = chain.Resolve(new[] { codepoint }, false, null, null, out FontChain.Family family);
            return FontChain.Style(face, family, FontChain.WeightFor(weight, family), 0, false, -1);
        }

        [TearDown]
        public void Forget()
        {
            LetteraSystemFonts.WeightShift = 0;
            LetteraFontChains.Invalidate();
        }

        // F3: a weight per platform picks the installed cut nearest to it.
        [Test]
        public void APlatformWeightPicksTheNearestCut()
        {
            LetteraSystemFont font = Named("Arial");
            Assert.AreEqual(400, Drawn(font, 'a').Face.Weight, "no weight: the face as it is");
            Here(font).Weight = 700;
            font.PlatformSettingsChanged();
            FontInstance bold = Drawn(font, 'a');
            Assert.GreaterOrEqual(bold.Face.Weight, 700, "Arial Bold, not a synthesized one");
            Assert.AreEqual(font.LoadFace().Family, bold.Face.Family);
            Assert.AreEqual(400, Drawn(font, 'a', 400).Face.Weight, "a text's own weight still wins");

            LetteraWorldText text = Make("mmmm", font: font);
            float boldWidth = text.GetGlyph(3).X;
            Here(font).Weight = 0;
            font.Weight = 0;
            font.PlatformSettingsChanged();
            text.ForceRebuild();
            Assert.Less(text.GetGlyph(3).X, boldWidth, "the bold cut is wider");
        }

        [Test]
        public void ALightWeightTakesALightCut()
        {
            LetteraSystemFont font = Named("Segoe UI");
            font.Weight = 300;
            FontInstance light = Drawn(font, 'a');
            if (light.Face.Weight == 400) Assert.Ignore("this machine has no Segoe UI Light");
            Assert.AreEqual(300, light.Face.Weight);
        }

        [Test]
        public void FacesTheSystemFallsBackToFollowTheWeight()
        {
            LetteraSettings.Instance.DisableSystemFontFallback = false;
            LetteraSystemFont font = Named("Arial");
            font.Weight = 700;
            FontChain chain = LetteraFontChains.Get(font, null);
            FontFace face = chain.Resolve(new[] { 0x0915 }, false, null, null, out FontChain.Family family);
            if (face == null || face == chain.PrimaryFace) Assert.Ignore("no system font here covers Devanagari");
            Assert.AreEqual(700, FontChain.WeightFor(0, family), "a substituted face draws at the asset's weight");
            FontInstance drawn = FontChain.Style(face, family, FontChain.WeightFor(0, family), 0, false, -1);
            Assert.IsTrue(drawn != face.Regular || face.Weight >= 700, "made bold, by an axis or by synthesis");
        }

        [Test]
        public void MetricsAndRasterCanDifferOnThisPlatform()
        {
            LetteraSystemFont font = Named("Arial");
            FaceMetrics natural = font.LoadFace().NaturalMetrics;
            LetteraSystemFontPlatform here = Here(font);
            here.OverrideMetrics = true;
            here.Ascender = natural.Ascender + 100;
            here.Descender = natural.Descender;
            here.LineGap = 0;
            here.OverrideRaster = true;
            here.PixelsPerEm = 16;
            font.PlatformSettingsChanged();
            FontFace face = font.LoadFace();
            Assert.AreEqual(natural.Ascender + 100, face.Metrics.Ascender);
            Assert.AreEqual(16, face.PixelGrid);
            Assert.AreNotSame(LetteraSystemFonts.FindNamed("Arial"), face, "the asset's own face: the system's copy keeps its lines");
            Assert.AreEqual(natural.Ascender, LetteraSystemFonts.FindNamed("Arial").Metrics.Ascender);

            here.OverrideMetrics = false;
            here.OverrideRaster = false;
            font.PlatformSettingsChanged();
            Assert.AreEqual(natural.Ascender, font.LoadFace().Metrics.Ascender, "off takes the asset's own again");
            Assert.AreEqual(0, font.LoadFace().PixelGrid);
        }

        // S6: a system's bold text setting shifts the weight of system faces only.
        [Test]
        public void TheSystemsWeightShiftMovesItsOwnFaces()
        {
            LetteraSystemFont font = Named("Arial");
            LetteraSystemFonts.WeightShift = 300;
            LetteraFontChains.Invalidate();
            Assert.GreaterOrEqual(Drawn(font, 'a').Face.Weight, 700, "regular text of a system font draws bold");
            Assert.AreEqual(400, Drawn(Latin, 'a').Face.Weight, "a project's own font does not move");
            LetteraSettings.Instance.DisableSystemFontFallback = false;
            LetteraFontChains.Invalidate();
            FontChain chain = LetteraFontChains.Get(font, null);
            FontFace face = chain.Resolve(new[] { 0x0915 }, false, null, null, out FontChain.Family family);
            if (face != null && face != chain.PrimaryFace) Assert.AreEqual(700, FontChain.WeightFor(0, family), "a substituted face is shifted once");
            Assert.AreEqual(1000, FontChain.Shifted(900, 300));
            Assert.AreEqual(700, FontChain.Shifted(0, 300));
            Assert.AreEqual(0, FontChain.Shifted(0, 0));
        }

        [Test]
        public void TheInspectorShowsWeightAndPlatformOverrides()
        {
            LetteraSystemFont font = Named("Arial");
            var editor = UnityEditor.Editor.CreateEditor(font);
            Created.Add(editor);
            VisualElement root = editor.CreateInspectorGUI();
            var labels = new System.Collections.Generic.List<string>();
            root.Query<Label>().ForEach(l => labels.Add(l.text));
            CollectionAssert.Contains(labels, "Weight Here");
            CollectionAssert.Contains(labels, "Space Width");
        }
    }
}

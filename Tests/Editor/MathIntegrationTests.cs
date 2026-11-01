#if !LETTERA_NO_MATH
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace FirstSymbol.Lettera.Editor.Tests
{
    /// <summary>A formula among everything else a text does: the clipboard, screen readers, motion, its font's way into a build.</summary>
    public class MathIntegrationTests : TextFixture
    {
        LetteraWorldText Built(string source)
        {
            LetteraWorldText text = Make(source, 2000, 2000);
            text.ForceRebuild();
            return text;
        }

        [Test]
        public void GetFormulasSaysWhatItDraws()
        {
            LetteraWorldText text = Built("a <math>x^2</math> b <math>\\frac{</math>");
            var found = new List<LetteraFormulaInfo>();
            text.GetFormulas(found);
            Assert.AreEqual(2, found.Count);
            Assert.AreEqual("x^2", found[0].Source);
            Assert.IsFalse(found[0].Failed);
            Assert.Greater(found[0].Box.width, 0);
            Assert.Greater(found[0].Box.height, 0);
            Assert.IsTrue(found[1].Failed);
            Assert.IsNotEmpty(found[1].Message);
        }

        [Test]
        public void EachClipboardFormatHasTheFormulaInItsOwnWords()
        {
            LetteraWorldText text = Built("a<math>x^2</math>b");
            var selectable = text.gameObject.AddComponent<LetteraSelectable>();
            text.ForceRebuild();
            selectable.SelectAll();
            List<LetteraClipItem> items = LetteraClipboard.SelectionItems(selectable);
            string Of(string format)
            {
                foreach (LetteraClipItem item in items)
                    if (item.Format == format)
                        return System.Text.Encoding.UTF8.GetString(item.Bytes);
                return null;
            }
            Assert.AreEqual("a$x^2$b", Of(LetteraClipFormat.PlainText));
            StringAssert.Contains("<math>x^2</math>", Of(LetteraClipFormat.Lettera));
            StringAssert.Contains("$x^2$", Of(LetteraClipFormat.Markdown));
            string html = Of(LetteraClipFormat.Html);
            StringAssert.Contains("<math", html);
            StringAssert.Contains("application/x-tex", html);
            StringAssert.Contains("x^2</annotation>", html);
            Assert.AreEqual(-1, html.IndexOf((char)0xE000), "no mark is left where a formula stood");
        }

        [Test]
        public void ASpanAroundAFormulaKeepsItsEndsOnTheClipboard()
        {
            LetteraWorldText text = Built("<b>a<math>x^2</math>b</b>c");
            var selectable = text.gameObject.AddComponent<LetteraSelectable>();
            text.ForceRebuild();
            selectable.SelectAll();
            List<LetteraClipItem> items = LetteraClipboard.SelectionItems(selectable);
            foreach (LetteraClipItem item in items)
            {
                if (item.Format != LetteraClipFormat.Markdown) continue;
                StringAssert.Contains("**a$x^2$b**", System.Text.Encoding.UTF8.GetString(item.Bytes));
                return;
            }
            Assert.Fail("No Markdown went out.");
        }

        [Test]
        public void TheMarkupIsWellFormedForAPage()
        {
            foreach (string source in MathOracleDump.Sheet)
            {
                string mathml = LetteraMathMarkup.ToClipboardMathML(source, true);
                Assert.IsNotNull(mathml, source);
                var document = new System.Xml.XmlDocument();
                Assert.DoesNotThrow(() => document.LoadXml(mathml), source);
            }
            Assert.IsNull(LetteraMathMarkup.ToMathML("\\frac{", false));
            StringAssert.Contains("&lt;", LetteraMathMarkup.ToClipboardMathML("a<b", false));
        }

        [Test]
        public void AMovingFormulaMovesWhole()
        {
            LetteraWorldText text = Built("<spin><math>\\frac{a}{\\sqrt{b}}</math></spin>");
            int quads = text.Engine.VertexCount / 4;
            var rest = new Vector3[text.Engine.VertexCount];
            System.Array.Copy(text.Engine.Vertices, rest, rest.Length);
            text.Engine.TickPose(0.25f, 0.25f);
            Vector3[] moved = text.Engine.Vertices;
            Assert.Greater(Vector3.Distance(rest[0], moved[0]), 0.01f, "it moved");
            Assert.Greater(quads, 3);
            // One rigid motion takes every quad there: two distances between corners of different quads are kept.
            for (int q = 1; q < quads; q++)
            {
                float was = Vector3.Distance(rest[0], rest[q * 4]);
                float now = Vector3.Distance(moved[0], moved[q * 4]);
                Assert.AreEqual(was, now, 0.05f, "quad " + q);
            }
        }

        [Test]
        public void TheSpanOfSemanticsSaysMath()
        {
            LetteraWorldText text = Built("<math=inline,x squared>x^2</math>");
            Assert.IsTrue(text.Engine.IsFormulaAt(0));
            Assert.AreEqual("x squared", text.Engine.FormulaLabelAt(0));
            LetteraWorldText plain = Built("<math>y</math>");
            Assert.AreEqual("y", plain.Engine.FormulaLabelAt(0));
        }

        [Test]
        public void TheBundledFontIsThereAndReferencedByNothing()
        {
            LetteraFontAsset font = LetteraMathMenu.BundledFont();
            Assert.IsNotNull(font, "the bundled math font");
            var packed = (LetteraFont)font;
            Assert.AreEqual(657440, packed.OriginalSize);
            Assert.Less(packed.CompressedSize, 360 * 1024);
            string path = AssetDatabase.GetAssetPath(font);
            StringAssert.DoesNotContain("/Resources/", path);
            Assert.IsNull(LetteraSettings.Instance.Math.Font, "the default settings name no math font");
            Assert.IsTrue(File.Exists(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(path)), "OFL-NotoSansMath.txt")), "its licence is beside it");
            string folder = Path.GetDirectoryName(Path.GetFullPath(path));
            int assets = 0;
            foreach (string file in Directory.GetFiles(folder))
                if (!file.EndsWith(".meta"))
                    assets++;
            Assert.AreEqual(2, assets, "only this font and its licence live there");
        }

        [Test]
        public void ARebuildAfterFontsAreGoneDoesNothing()
        {
            // As in a domain reload, when an inspector closing makes Unity rebuild a canvas after Lettera has released its fonts. Before this was guarded, it crashed the editor.
            LetteraWorldText text = Built("before");
            TextEngine.FontsGone = true;
            try
            {
                text.Text = "after";
                Assert.IsFalse(text.Engine.Rebuild());
                Assert.AreNotEqual(DirtyLevel.None, text.Engine.Dirty, "the work waits for the fonts");
            }
            finally
            {
                TextEngine.FontsGone = false;
            }
            text.ForceRebuild();
            Assert.Greater(text.GlyphCount, 0);
        }

        [Test]
        public void ASystemMathFontAnswersWhereThereIsOne()
        {
            if (LetteraNative.Version < 3) Assert.Inconclusive("Needs the library that reads MATH.");
            FontFace face = LetteraMathEffect.ResolveFace(null, Latin.LoadFace());
            if (face == Latin.LoadFace() || !NativeMathData.Available(face)) Assert.Inconclusive("No system math font on this machine.");
            LetteraMathFont math = LetteraMathFont.For(face);
            Assert.Greater(math.C(MathConstant.ScriptPercentScaleDown), 0);
            LetteraMathLayout tall = LetteraMathCache.Get("\\left(\\frac{\\frac{a}{b}}{\\frac{c}{d}}\\right)", true, face, face);
            Assert.IsFalse(tall.Failed);
            Assert.Greater(tall.Ascent + tall.Descent, 2);
        }

        [Test]
        public void TheBuildCheckWarnsOnlyWhereItMatters()
        {
            string folder = Path.Combine(Path.GetTempPath(), "LetteraMathCheck");
            Directory.CreateDirectory(folder);
            string math = Path.Combine(folder, "math.unity"), plain = Path.Combine(folder, "plain.unity");
            File.WriteAllText(math, "m_Text: a <math>x</math>\n");
            File.WriteAllText(plain, "m_Text: nothing here\n");
            LetteraFontAsset saved = LetteraSettings.Instance.Math.Font;
            LetteraSettings.Instance.Math.Font = null;
            try
            {
                Assert.IsNotNull(LetteraMathBuildCheck.Check(BuildTarget.WebGL, new[] { math }));
                Assert.IsNotNull(LetteraMathBuildCheck.Check(BuildTarget.Android, new[] { plain, math }));
                Assert.IsNull(LetteraMathBuildCheck.Check(BuildTarget.WebGL, new[] { plain }));
                Assert.IsNull(LetteraMathBuildCheck.Check(BuildTarget.StandaloneWindows64, new[] { math }));
            }
            finally
            {
                LetteraSettings.Instance.Math.Font = saved;
                Directory.Delete(folder, true);
            }
        }
    }
}
#endif

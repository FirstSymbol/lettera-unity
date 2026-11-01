using System.IO;
using System.Text;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace FirstSymbol.Lettera.Editor.Tests
{
    /// <summary>Writes each row of the sheet's boxes and MathML, for the page that compares them with Chromium's. Only with the library that reads MATH, and only when a driver asks by its folder.</summary>
    public class MathOracleDump : TextFixture
    {
        internal static readonly string[] Sheet =
        {
            "x", "x+y", "x=y", "a,b", "f(x)", "-x", "a+-b", "\\alpha\\beta\\gamma", "\\sin x", "\\log_2 8",
            "x^2", "x_2", "x_1^2", "x^{y^z}", "x_{i_j}", "f'", "f''", "\\frac{a}{b}", "\\frac{a+b}{c+d}", "\\frac{\\frac{a}{b}}{c}", "\\frac{1}{\\frac{1}{x}}",
            "\\binom{n}{k}", "\\sqrt{x}", "\\sqrt{x^2+y^2}", "\\sqrt[3]{x}", "\\sqrt{\\frac{a}{b}}",
            "\\sum_{i=0}^n i", "\\prod_{k=1}^n k", "\\int_0^1 x", "\\oint_C", "\\lim_{x\\to 0} f",
            "\\left(x\\right)", "\\left(\\frac{a}{b}\\right)", "\\left[\\frac{a}{\\frac{b}{c}}\\right]", "\\left\\{x\\right\\}", "\\left|x\\right|", "\\left\\langle x\\right\\rangle", "\\big(\\Big(\\bigg(\\Bigg(",
            "\\hat{x}", "\\vec{v}", "\\bar{a}", "\\tilde{n}", "\\dot{x}", "\\widehat{abc}", "\\overline{abc}", "\\underline{abc}", "\\overbrace{abc}", "\\underbrace{abc}", "\\xrightarrow{abc}",
            "\\begin{matrix}a&b\\\\c&d\\end{matrix}", "\\begin{pmatrix}a&b\\\\c&d\\end{pmatrix}", "\\begin{bmatrix}1&0\\\\0&1\\end{bmatrix}", "\\begin{cases}a&b\\\\c&d\\end{cases}",
            "\\mathbb{R}", "\\mathcal{L}", "\\mathbf{x}", "\\mathfrak{g}", "\\text{word}", "\\infty\\partial\\nabla", "\\leq\\geq\\neq\\approx",
        };

        [Test]
        public void WriteTheSheet()
        {
            string folder = System.Environment.GetEnvironmentVariable("LETTERA_MATH_ORACLE");
            if (string.IsNullOrEmpty(folder)) Assert.Inconclusive("Only for the driver of the Chromium comparison.");
            if (LetteraNative.Version < 3) Assert.Inconclusive("Needs the library that reads MATH.");
            LetteraFont font = Font("NotoSansMath-Regular.ttf");
            FontFace face = font.LoadFace();
            Assert.IsTrue(NativeMathData.Available(face));
            var json = new StringBuilder("[");
            for (int i = 0; i < Sheet.Length; i++)
            {
                foreach (bool display in new[] { false, true })
                {
                    LetteraMathLayout layout = LetteraMathCache.Get(Sheet[i], display, face, face);
                    string mathml = LetteraMathMarkup.ToMathML(Sheet[i], display);
                    if (json.Length > 1) json.Append(',');
                    json.Append("{\"id\":\"").Append(i).Append(display ? "d" : "t").Append("\",\"source\":").Append(Quote(Sheet[i])).Append(",\"mathml\":").Append(Quote(mathml)).Append(",\"width\":").Append(layout.Width.ToString(System.Globalization.CultureInfo.InvariantCulture)).Append(",\"ascent\":").Append(layout.InkAscent.ToString(System.Globalization.CultureInfo.InvariantCulture)).Append(",\"descent\":").Append(layout.InkDescent.ToString(System.Globalization.CultureInfo.InvariantCulture)).Append(",\"failed\":").Append(layout.Failed ? "true" : "false").Append('}');
                }
            }
            json.Append(']');
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "ours.json"), json.ToString());
            string found = AssetDatabase.GUIDToAssetPath(AssetDatabase.FindAssets("FirstSymbol.Lettera.Editor.Tests t:asmdef")[0]);
            File.Copy(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(found)), "..", "Fonts~", "NotoSansMath-Regular.ttf"), Path.Combine(folder, "font.ttf"), true);
        }

        static string Quote(string text)
        {
            var q = new StringBuilder("\"");
            foreach (char c in text ?? "")
            {
                if (c == '"' || c == '\\') q.Append('\\').Append(c);
                else if (c < 32) q.Append("\\u").Append(((int)c).ToString("x4"));
                else q.Append(c);
            }
            return q.Append('"').ToString();
        }
    }
}

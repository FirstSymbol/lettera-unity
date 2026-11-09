using System;
using System.Globalization;
using FirstSymbol.Lettera.Editor.Cli;
using NUnit.Framework;
using UnityEngine;

namespace FirstSymbol.Lettera.Editor.Tests
{
    /// <summary>On a machine whose culture writes a decimal comma, a number in a tag, a formula or a file reads the same as everywhere.</summary>
    public class CultureTests : TextFixture
    {
        static readonly string[] Cultures = { "ru-RU", "de-DE", "fr-FR" };

        static readonly string[] Markups =
        {
            "<size=1.5em>big</size> text",
            "a<cspace=0.25em>spaced</cspace>b",
            "<shadow=#000,0.1,-0.1>shadow</shadow>",
            "<stroke=#F00,0.08>stroke</stroke>",
            "<mark=#FF08,block,fromgeometry,0.25>marked</mark> words",
            "<line-height=1.5>one\ntwo</line-height>",
            "<indent=1.5em>indented paragraph that is long enough to wrap onto a second line</indent>",
            "<sup>up</sup> and <sub>down</sub>",
        };

        static void In(string name, Action action)
        {
            CultureInfo saved = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(name);
                if (name != "en-US") Assert.AreEqual(",", CultureInfo.CurrentCulture.NumberFormat.NumberDecimalSeparator, name + " writes a comma");
                action();
            }
            finally { CultureInfo.CurrentCulture = saved; }
        }

        float[] Geometry(string markup)
        {
            LetteraWorldText text = Make(markup, 300, 200, null, 24);
            var numbers = new System.Collections.Generic.List<float> { text.GlyphCount, WidthOf(text), text.Lines.Count };
            for (int i = 0; i < text.GlyphCount; i++)
            {
                LetteraGlyphInfo g = text.GetGlyph(i);
                numbers.Add(g.X);
                numbers.Add(g.Y);
                numbers.Add(g.Advance);
                numbers.Add(g.Size);
            }
            numbers.Add(text.Engine.VertexCount);
            return numbers.ToArray();
        }

        [Test]
        public void MarkupLaysOutTheSameInEveryCulture([ValueSource(nameof(Markups))] string markup)
        {
            float[] expected = null;
            In("en-US", () => expected = Geometry(markup));
            foreach (string name in Cultures)
            {
                float[] got = null;
                In(name, () => got = Geometry(markup));
                CollectionAssert.AreEqual(expected, got, $"{markup} in {name}");
            }
        }

        [Test]
        public void ATagWithAFractionIsNotIgnored()
        {
            float plain = 0, large = 0;
            In("ru-RU", () =>
            {
                plain = WidthOf(Make("big text", 600, 200, null, 24));
                large = WidthOf(Make("<size=1.5em>big</size> text", 600, 200, null, 24));
            });
            Assert.Greater(large, plain * 1.2f, "the 1.5em was read as 0 or 1");
        }

        [Test]
        public void SlotsAndLengthsReadInvariant()
        {
            foreach (string name in Cultures)
                In(name, () =>
                {
                    Assert.IsTrue(LetteraSlots.TryNumber("0.25", out float number), name);
                    Assert.AreEqual(0.25f, number);
                    Assert.IsFalse(LetteraSlots.TryNumber("0,25", out _), name + ": a comma is not a decimal point in a tag");
                    Assert.IsTrue(LetteraLength.TryParse("1.5em", out LetteraLength length), name);
                    Assert.AreEqual(1.5f, length.Value);
                    Assert.IsTrue(LetteraLength.TryParse("-12.5%", out length), name);
                    Assert.AreEqual(-12.5f, length.Value);
                });
        }

        [Test]
        public void TheCommandLineReadsAndWritesNumbersInvariant()
        {
            foreach (string name in Cultures)
                In(name, () =>
                {
                    Assert.AreEqual(0.5, (double)LetteraCliJson.Parse("0.5"), name);
                    Assert.AreEqual("0.5", LetteraCliJson.Write(0.5), name);
                    Assert.AreEqual("[0.25,1.5]", LetteraCliJson.Write(new System.Collections.Generic.List<object> { 0.25, 1.5 }).Replace(" ", ""), name);
                });
        }

        [Test]
        public void AFormulaLaysOutTheSameInEveryCulture([Values(@"a\hspace{1.5em}b", @"\frac{1}{2}\kern{0.5em}x", @"\sqrt[3]{x}\,\mathrm{d}x", @"\begin{array}{cc}1&2.5\\3&4\end{array}")] string formula)
        {
            float expected = 0;
            In("en-US", () => expected = WidthOf(Make("a<math>" + formula + "</math>b", 600, 200, null, 24)));
            foreach (string name in Cultures)
            {
                float got = 0;
                In(name, () => got = WidthOf(Make("a<math>" + formula + "</math>b", 600, 200, null, 24)));
                Assert.AreEqual(expected, got, 0f, $"{formula} in {name}");
            }
        }

        [Test]
        public void StylesFromAClipboardReadInvariant()
        {
            const string html = "<span style=\"color:rgb(255,0,0);font-size:12.5pt\">a</span><span style=\"color:red;font-size:1.5rem\">b</span>";
            string Read()
            {
                var builder = new System.Text.StringBuilder();
                foreach (LetteraPastedSpan span in LetteraHtmlCodec.Read(html).Spans) builder.Append(span.Start).Append(' ').Append(span.End).Append(' ').Append(span.EffectKey).Append(' ').Append(span.Raw).Append(';');
                return builder.ToString();
            }
            string expected = null;
            In("en-US", () => expected = Read());
            StringAssert.Contains("16.6", expected, "12.5pt is 16.67 px");
            foreach (string name in Cultures)
                In(name, () => Assert.AreEqual(expected, Read(), name));
        }
    }
}

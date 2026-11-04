#if !LETTERA_NO_MATH
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using NUnit.Framework;
using UnityEngine;

namespace FirstSymbol.Lettera.Editor.Tests
{
    /// <summary>The notation and the boxes, on a font's fallback spacing and on a fake table. True MATH constants against Chromium run in the separate oracle.</summary>
    public class MathTests : TextFixture
    {
        [SetUp]
        public void Fresh()
        {
            LetteraMathCache.Clear();
            LetteraMath.ForgetWarnings();
            UnityEngine.TestTools.LogAssert.ignoreFailingMessages = false;
        }

        [TearDown]
        public void Back()
        {
            LetteraMathFont.Override = null;
            LetteraMathCache.Clear();
        }

        static MathTree Good(string source)
        {
            MathTree tree = LetteraMathParser.Parse(source);
            Assert.IsNull(tree.Error, source + ": " + tree.Error);
            return tree;
        }

        static void Bad(string source, string part = null)
        {
            MathTree tree = LetteraMathParser.Parse(source);
            Assert.IsNotNull(tree.Error, source);
            if (part != null) StringAssert.Contains(part, tree.Error);
        }

        static string Shape(MathTree tree, int node = -1)
        {
            if (node < 0) node = tree.Root;
            MathNode n = tree.Nodes[node];
            var text = new StringBuilder();
            void Kids(int first)
            {
                for (int c = first; c >= 0; c = tree.Nodes[c].Next) text.Append(Shape(tree, c));
            }
            switch (n.Kind)
            {
                case MathNodeKind.List: text.Append('('); Kids(n.A); text.Append(')'); break;
                case MathNodeKind.Char: text.Append(char.ConvertFromUtf32(n.Cp)); break;
                case MathNodeKind.Scripts:
                    text.Append("S[").Append(n.A >= 0 ? Shape(tree, n.A) : "").Append('^').Append(n.B >= 0 ? Shape(tree, n.B) : "").Append('_').Append(n.C >= 0 ? Shape(tree, n.C) : "").Append(']');
                    break;
                case MathNodeKind.Fraction: text.Append("F[").Append(Shape(tree, n.A)).Append('/').Append(Shape(tree, n.B)).Append(']'); break;
                case MathNodeKind.Radical: text.Append("R[").Append(Shape(tree, n.A)).Append(']'); break;
                default: text.Append(n.Kind.ToString()); break;
            }
            return text.ToString();
        }

        // ---------------------------------------------------------------- grammar

        [TestCase("x^23", "(S[x^2_]3)")]
        [TestCase("x^{23}", "(S[x^(23)_])")]
        [TestCase("\\frac12", "(F[1/2])")]
        [TestCase("\\frac{a}{b}", "(F[(a)/(b)])")]
        [TestCase("\\sqrt2", "(R[2])")]
        [TestCase("x_1^2", "(S[x^2_1])")]
        [TestCase("x^2_1", "(S[x^2_1])")]
        [TestCase("{}^n", "(S[()^n_])")]
        [TestCase("% only a comment\n", "()")]
        [TestCase("   ", "()")]
        [TestCase("", "()")]
        public void ItReadsAsTeXReadsIt(string source, string shape)
        {
            Assert.AreEqual(shape, Shape(Good(source)));
        }

        [Test]
        public void PrimesAreOneSuperscript()
        {
            string one = Shape(Good("f'"));
            StringAssert.Contains(char.ConvertFromUtf32(0x2032), one);
            StringAssert.Contains(char.ConvertFromUtf32(0x2033), Shape(Good("f''")));
            StringAssert.Contains(char.ConvertFromUtf32(0x2034), Shape(Good("f'''")));
            Good("f'^2");
            Good("{f'}^2");
        }

        [TestCase("x^2^3", "superscript")]
        [TestCase("x_2_3", "subscript")]
        [TestCase("\\frac{a}", "missing")]
        [TestCase("{a", "brace")]
        [TestCase("a}", "brace")]
        [TestCase("\\left( a", "right")]
        [TestCase("a \\right)", "left")]
        [TestCase("a & b", "&")]
        [TestCase("a \\\\ b", "line break")]
        [TestCase("\\nosuch", "no command")]
        [TestCase("\\begin{nosuch}x\\end{nosuch}", "no environment")]
        [TestCase("\\begin{matrix}x", "\\end")]
        [TestCase("\\begin{matrix}x\\end{cases}", "closes")]
        [TestCase("\\ce{H2O $x}", "\\ce")]
        [TestCase("\\def\\a{b}", "no command")]
        [TestCase("\\newcommand{\\a}{b}", "no command")]
        public void ItRefusesWhatItCannotRead(string source, string part)
        {
            Bad(source, part);
        }

        [Test]
        public void EveryLimitHoldsAtAndPastItsValue()
        {
            Good(new string(' ', LetteraMathLimits.SourceLength - 1) + "x");
            Bad(new string('x', LetteraMathLimits.SourceLength + 1), "longer");

            string deep = new string('{', 28) + new string('}', 28);
            Good(deep);
            Bad(new string('{', 40) + new string('}', 40), "nests");

            Bad(new string('x', LetteraMathLimits.Nodes + 2), "parts");

            Good("\\begin{matrix}" + string.Join("&", new string[LetteraMathLimits.Columns]) + "\\end{matrix}");
            Bad("\\begin{matrix}" + string.Join("&", new string[LetteraMathLimits.Columns + 2]) + "\\end{matrix}", "columns");
            Bad("\\begin{matrix}" + string.Join("\\\\", new string[LetteraMathLimits.Rows + 2]) + "\\end{matrix}", "rows");

            Bad("\\" + new string('a', LetteraMathLimits.CommandName + 1), "name");
            Assert.IsTrue(LetteraMathParser.TryLength("999999em", out float clamped));
            Assert.AreEqual(LetteraMathLimits.LengthEm, clamped);
            Assert.IsFalse(LetteraMathParser.TryLength("10furlong", out _));
            Assert.IsTrue(LetteraMathParser.TryLength("10pt", out float point));
            Assert.AreEqual(1, point, 0.001f);
            Assert.IsTrue(LetteraMathParser.TryLength("16px", out float pixel));
            Assert.AreEqual(1, pixel, 0.001f);
            Assert.IsTrue(LetteraMathParser.TryLength("18mu", out float mu));
            Assert.AreEqual(1, mu, 0.001f);
        }

        [Test]
        public void HostileInputNeverThrowsOrHangs()
        {
            var random = new System.Random(7);
            string[] tokens = { "\\frac", "{", "}", "^", "_", "\\sqrt[", "]", "\\left(", "\\right)", "\\middle|", "&", "\\\\", "\\begin{matrix}", "\\end{matrix}", "x", "\\alpha", "'", "%", "\\text{", "\\begin{array}{c|c}", "\\hline", "$", "\\", "\\color{red}", "\\textcolor{red}", "\\hspace{1em}", "\\tag{1}", "\\not", "\\overbrace", "\\xrightarrow[", "\\substack{" };
            for (int run = 0; run < 20000; run++)
            {
                var text = new StringBuilder();
                int count = random.Next(0, 24);
                for (int i = 0; i < count; i++)
                {
                    if (random.Next(4) == 0) text.Append((char)random.Next(1, 0xFFFF));
                    else text.Append(tokens[random.Next(tokens.Length)]);
                }
                Assert.DoesNotThrow(() => LetteraMathParser.Parse(text.ToString()), text.ToString());
            }
        }

        static double Cost(string source)
        {
            double best = double.MaxValue;
            for (int run = 0; run < 3; run++)
            {
                var clock = Stopwatch.StartNew();
                LetteraMathParser.Parse(source);
                clock.Stop();
                best = Math.Min(best, clock.Elapsed.TotalMilliseconds);
            }
            return best;
        }

        [TestCase("{")]
        [TestCase("\\sqrt[")]
        [TestCase("\\left(")]
        [TestCase("^")]
        [TestCase("\\begin{matrix}")]
        [TestCase("&")]
        [TestCase("x")]
        [TestCase("\\alpha")]
        [TestCase("\\frac")]
        [TestCase("\\text{")]
        [TestCase("}")]
        [TestCase("\\color{red}")]
        public void AdversarialInputStaysLinear(string token)
        {
            string small = Repeat(token, 8000), large = Repeat(token, 16000);
            Cost(small);
            double a = Cost(small), b = Cost(large);
            Assert.Less(b, Math.Max(a * 2.5, a + 2), $"{token}: {a:0.000} ms then {b:0.000} ms");
        }

        static string Repeat(string token, int length)
        {
            var text = new StringBuilder(length + token.Length);
            while (text.Length + token.Length <= length) text.Append(token);
            return text.ToString();
        }

        [Test]
        public void TheSymbolsAreThereAndRight()
        {
            Assert.GreaterOrEqual(LetteraMathSymbols.Count, 450);
            var expected = new (string, int)[]
            {
                ("epsilon", 0x3F5), ("varepsilon", 0x3B5), ("phi", 0x3D5), ("varphi", 0x3C6), ("leq", 0x2264), ("geq", 0x2265), ("mid", 0x2223), ("setminus", 0x2216), ("emptyset", 0x2205), ("infty", 0x221E),
                ("partial", 0x2202), ("nabla", 0x2207), ("forall", 0x2200), ("exists", 0x2203), ("in", 0x2208), ("notin", 0x2209), ("subset", 0x2282), ("supset", 0x2283), ("cup", 0x222A), ("cap", 0x2229),
                ("times", 0xD7), ("div", 0xF7), ("cdot", 0x22C5), ("pm", 0xB1), ("to", 0x2192), ("rightarrow", 0x2192), ("gets", 0x2190), ("Rightarrow", 0x21D2), ("iff", 0x27FA), ("mapsto", 0x21A6),
                ("sum", 0x2211), ("prod", 0x220F), ("int", 0x222B), ("oint", 0x222E), ("langle", 0x27E8), ("rangle", 0x27E9), ("lfloor", 0x230A), ("rceil", 0x2309), ("ldots", 0x2026), ("cdots", 0x22EF),
            };
            foreach ((string name, int cp) in expected)
            {
                Assert.IsTrue(LetteraMathSymbols.TryFind(name, out int found, out _), name);
                Assert.AreEqual(cp, found, name);
            }
            Assert.IsTrue(LetteraMathSymbols.TryFind("sum", out _, out MathClass sum));
            Assert.AreEqual(MathClass.Op, sum);
            Assert.IsTrue(LetteraMathSymbols.TryFind("leq", out _, out MathClass rel));
            Assert.AreEqual(MathClass.Rel, rel);
            Assert.IsTrue(LetteraMathSymbols.TryFind("langle", out _, out MathClass open));
            Assert.AreEqual(MathClass.Open, open);
        }

        // ---------------------------------------------------------------- in a text

        LetteraWorldText Text(string source) => Make(source, 2000, 2000);

        static string Shown(LetteraWorldText label)
        {
            var text = new StringBuilder();
            foreach (int cp in label.Engine.ShownCodepoints) text.Append(char.ConvertFromUtf32(cp));
            return text.ToString();
        }

        [Test]
        public void AFormulaIsOneObjectInTheLine()
        {
            LetteraWorldText label = Text("a<math>x^2</math>b");
            label.ForceRebuild();
            Assert.AreEqual("a￼b", Shown(label));
            LetteraWorldText plain = Text("ab");
            plain.ForceRebuild();
            Assert.Greater(label.Engine.Lines[0].Width, plain.Engine.Lines[0].Width);
        }

        [Test]
        public void AWidthChangesByExactlyTheFormulas()
        {
            LetteraWorldText label = Text("a<math>x+y</math>b");
            LetteraWorldText plain = Text("ab");
            label.ForceRebuild();
            plain.ForceRebuild();
            LetteraMathLayout layout = LetteraMathCache.Get("x+y", false, Latin.LoadFace(), Latin.LoadFace());
            Assert.AreEqual(plain.Engine.Lines[0].Width + layout.Width * label.FontSize, label.Engine.Lines[0].Width, 0.05f);
        }

        [Test]
        public void TheCachedBoxSurvivesASizeChange()
        {
            LetteraWorldText label = Text("<math>\\frac{a}{b}</math>");
            label.ForceRebuild();
            int layouts = LetteraMathBuilder.Layouts;
            label.FontSize *= 2;
            label.ForceRebuild();
            Assert.AreEqual(layouts, LetteraMathBuilder.Layouts, "no math ran for a new size");
        }

        [Test]
        public void ATallFormulaOpensItsLineAndALittleOneDoesNot()
        {
            LetteraWorldText plain = Text("ab");
            LetteraWorldText small = Text("a<math>x</math>b");
            LetteraWorldText tall = Text("a<math>\\frac{a}{\\frac{b}{\\frac{c}{d}}}</math>b");
            plain.ForceRebuild();
            small.ForceRebuild();
            tall.ForceRebuild();
            Assert.AreEqual(plain.Engine.Lines[0].Height, small.Engine.Lines[0].Height, 0.01f);
            Assert.Greater(tall.Engine.Lines[0].Height, plain.Engine.Lines[0].Height);
        }

        [Test]
        public void AnUnclosedTagStaysText()
        {
            LetteraWorldText label = Text("a<math>x");
            label.ForceRebuild();
            Assert.AreEqual("a<math>x", Shown(label));
        }

        [Test]
        public void AStrayCloserGoes()
        {
            LetteraWorldText label = Text("a</math>b");
            label.ForceRebuild();
            Assert.AreEqual("ab", Shown(label));
        }

        [Test]
        public void NoParseAndEscapesProtectTheTag()
        {
            LetteraWorldText off = Text("<noparse><math>x</math></noparse>");
            off.ForceRebuild();
            Assert.AreEqual("<math>x</math>", Shown(off));
            LetteraWorldText escaped = Text("\\<math>x\\</math>");
            escaped.ForceRebuild();
            Assert.AreEqual("<math>x</math>", Shown(escaped));
        }

        [Test]
        public void WhatIsInsideIsTheFormulas()
        {
            LetteraWorldText label = Text("<math>a<b \\, <i>c</i></math>");
            label.ForceRebuild();
            Assert.AreEqual("￼", Shown(label));
        }

        [Test]
        public void AFailingFormulaFailsAlone()
        {
            string seen = null;
            Action<string, int, string> listener = (source, position, message) => seen = source;
            LetteraMath.Failed += listener;
            try
            {
                UnityEngine.TestTools.LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("could not be read"));
                LetteraWorldText label = Text("before <math>\\nosuch</math> after");
                label.ForceRebuild();
                Assert.AreEqual("\\nosuch", seen);
                Assert.Greater(label.GlyphCount, 10, "the words around it draw");
            }
            finally
            {
                LetteraMath.Failed -= listener;
            }
        }

        [Test]
        public void AnEmptyFormulaIsNothing()
        {
            LetteraWorldText label = Text("a<math></math>b");
            LetteraWorldText plain = Text("ab");
            label.ForceRebuild();
            plain.ForceRebuild();
            Assert.AreEqual(plain.Engine.Lines[0].Width, label.Engine.Lines[0].Width, 0.05f);
        }

        [Test]
        public void ADisplayFormulaStandsOnItsOwnLine()
        {
            LetteraWorldText label = Text("a<math=display>x</math>b");
            label.ForceRebuild();
            Assert.AreEqual(3, label.Engine.Lines.Count);
            LetteraWorldText alone = Text("<math=display>x</math>");
            alone.ForceRebuild();
            Assert.AreEqual(1, alone.Engine.Lines.Count, "no empty line when it already stands alone");
        }

        [Test]
        public void CopyGivesTheSource()
        {
            LetteraWorldText label = Text("a<math>x^2</math>b");
            label.ForceRebuild();
            Assert.AreEqual("$x^2$", label.Engine.InlineAltAt(1));
        }

        [Test]
        public void TheSpanMatcherIsOptAndCareful()
        {
            LetteraStyle Dollars() => LetteraStyle.ForMatcher(new LetteraMathSpanMatcher { Dollar = true }, new LetteraMathEffect());
            LetteraWorldText label = Text("a $x$ b");
            label.Styles.Add(Dollars());
            label.MarkStylesDirty();
            label.ForceRebuild();
            Assert.AreEqual("a ￼ b", Shown(label));
            LetteraWorldText money = Text("$20 and $30");
            money.Styles.Add(Dollars());
            money.MarkStylesDirty();
            money.ForceRebuild();
            Assert.AreEqual("$20 and $30", Shown(money), "a digit after the closer means money");
            LetteraWorldText open = Text("a \\(x");
            open.Styles.Add(LetteraStyle.ForMatcher(new LetteraMathSpanMatcher(), new LetteraMathEffect()));
            open.MarkStylesDirty();
            open.ForceRebuild();
            Assert.AreEqual("a (x", Shown(open), "the escape rule took the backslash; nothing was opened");
        }

        [Test]
        public void AFloodOfUnclosedTagsStaysLinear()
        {
            string Flood(int count) => Repeat("<math>", count * 6);
            double Run(string text)
            {
                var clock = Stopwatch.StartNew();
                LetteraWorldText label = Text(text);
                label.ForceRebuild();
                return clock.Elapsed.TotalMilliseconds;
            }
            Run(Flood(1000));
            double a = Run(Flood(4000)), b = Run(Flood(40000));
            Assert.Less(b, a * 30, $"{a} then {b}");
        }

        [Test]
        public void ATextWithNoFormulaRunsNoMath()
        {
            LetteraMathCache.Clear();
            int created = LetteraMathFont.Created;
            LetteraWorldText label = Text("plain <b>text</b> with no formula");
            label.ForceRebuild();
            Assert.AreEqual(created, LetteraMathFont.Created);
        }

        // ---------------------------------------------------------------- spacing

        static float Spacing(MathClass a, MathClass b, bool tight) => LetteraMathBuilder.SpaceBetween(a, b, tight);

        [Test]
        public void AtomsSpaceAsTeXsTableSays()
        {
            const float Thin = 3f / 18, Med = 4f / 18, Thick = 5f / 18;
            Assert.AreEqual(0, Spacing(MathClass.Ord, MathClass.Ord, false));
            Assert.AreEqual(Thin, Spacing(MathClass.Ord, MathClass.Op, false));
            Assert.AreEqual(Thin, Spacing(MathClass.Ord, MathClass.Op, true), "thin stays in scripts");
            Assert.AreEqual(Med, Spacing(MathClass.Ord, MathClass.Bin, false));
            Assert.AreEqual(0, Spacing(MathClass.Ord, MathClass.Bin, true));
            Assert.AreEqual(Thick, Spacing(MathClass.Ord, MathClass.Rel, false));
            Assert.AreEqual(0, Spacing(MathClass.Ord, MathClass.Rel, true));
            Assert.AreEqual(0, Spacing(MathClass.Ord, MathClass.Open, false));
            Assert.AreEqual(0, Spacing(MathClass.Rel, MathClass.Rel, false));
            Assert.AreEqual(Thin, Spacing(MathClass.Punct, MathClass.Ord, false));
            Assert.AreEqual(Thin, Spacing(MathClass.Inner, MathClass.Op, true));
            Assert.AreEqual(Thin, Spacing(MathClass.Close, MathClass.Op, false));
            Assert.AreEqual(0, Spacing(MathClass.Open, MathClass.Inner, false));
        }

        LetteraMathLayout Lay(string source, bool display = false)
        {
            FontFace face = Latin.LoadFace();
            return LetteraMathCache.Get(source, display, face, face);
        }

        [Test]
        public void ABinaryWithNothingToBindReadsAsOrdinary()
        {
            // With its pair, an operator takes two medium spaces. At a list's start or after another operator, none.
            float x = Lay("x").Width, plus = Lay("+").Width;
            Assert.AreEqual(2 * x + plus + 2 * 4f / 18, Lay("x+x").Width, 0.01f);
            Assert.AreEqual(plus + x, Lay("+x").Width, 0.01f, "first in a list");
            Assert.AreEqual(x + plus, Lay("x+").Width, 0.01f, "last in a list");
            Assert.AreEqual(Lay("(+x)").Width, Lay("(").Width + plus + x + Lay(")").Width, 0.01f, "after an opening bracket");
        }

        [Test]
        public void ScriptsTakeTheirScale()
        {
            float scale = LetteraMathFont.For(Latin.LoadFace()).C(MathConstant.ScriptPercentScaleDown) / 100;
            LetteraMathLayout layout = Lay("x^2");
            Assert.AreEqual(2, layout.Items.Length);
            Assert.AreEqual(1, layout.Items[0].Scale, 0.001f);
            Assert.AreEqual(scale, layout.Items[1].Scale, 0.001f);
            Assert.Greater(layout.Items[1].Y, 0);
            LetteraMathLayout under = Lay("x_2");
            Assert.Less(under.Items[1].Y, 0);
        }

        [Test]
        public void AFractionsBarRunsOverTheWiderPart()
        {
            LetteraMathLayout layout = Lay("\\frac{abc}{d}");
            int rules = 0;
            foreach (LetteraMathItem item in layout.Items)
            {
                if (item.Kind != MathItemKind.Rule) continue;
                rules++;
                Assert.AreEqual(layout.Width, item.Length, 0.001f);
            }
            Assert.AreEqual(1, rules);
            Assert.Greater(layout.Ascent, 0);
            Assert.Greater(layout.Descent, 0);
        }

        [Test]
        public void EmptyPartsOfAFractionAreFine()
        {
            Assert.IsFalse(Lay("\\frac{}{x}").Failed);
            Assert.IsFalse(Lay("\\frac{x}{}").Failed);
        }

        [Test]
        public void EachConstructDraws()
        {
            string[] sources =
            {
                "\\sqrt{x}", "\\sqrt[3]{x}", "\\binom{n}{k}", "\\left(\\frac{a}{b}\\right)^2", "\\left\\{x\\middle|y\\right\\}", "\\big( \\Big[ \\bigg\\{ \\Bigg|",
                "\\begin{pmatrix}a&b\\\\c&d\\end{pmatrix}", "\\begin{cases}a&b\\\\c&d\\end{cases}", "\\begin{aligned}a&=b\\\\c&=d\\end{aligned}", "\\begin{array}{c|c}a&b\\\\\\hline c&d\\end{array}", "\\begin{gathered}a\\\\b\\end{gathered}",
                "\\hat{x} \\tilde{a} \\vec{v} \\bar{i}", "\\widehat{abc}", "\\overline{ab} \\underline{cd}", "\\overbrace{abc}^{n} \\underbrace{def}_{m}", "\\xrightarrow[b]{a}", "\\overset{a}{b} \\underset{a}{b} \\stackrel{a}{=}",
                "\\mathbf{x} \\mathbb{R} \\mathcal{L} \\mathfrak{g} \\mathsf{s} \\mathtt{t} \\mathrm{d}x", "\\text{word} \\operatorname{rank} \\sin x \\lim_{n \\to \\infty}",
                "\\sum_{i=0}^n \\prod_i \\int_0^1 x\\,dx", "\\boxed{x} \\phantom{y} \\hphantom{y} \\vphantom{y} \\smash{y} \\not=", "\\textcolor{red}{x} {\\color{blue} y}", "\\displaystyle\\sum_i", "\\dfrac{a}{b} \\tfrac{a}{b} \\cfrac{a}{b}", "\\quad\\qquad\\,\\:\\;\\!\\ \\hspace{1em}", "\\Large x \\tiny y",
            };
            foreach (string source in sources)
            {
                MathTree tree = LetteraMathParser.Parse(source);
                Assert.IsNull(tree.Error, source + ": " + tree.Error);
                LetteraMathLayout layout = Lay(source);
                Assert.IsFalse(layout.Failed, source);
                Assert.Greater(layout.Width, 0, source);
            }
        }

        [Test]
        public void TheDeepestFormulaLaysOutLinearly()
        {
            var deep = new StringBuilder();
            for (int i = 0; i < 28; i++) deep.Append("\\frac{a}{");
            deep.Append('x');
            deep.Append('}', 28);
            var clock = Stopwatch.StartNew();
            Assert.IsFalse(Lay(deep.ToString()).Failed);
            Assert.Less(clock.ElapsedMilliseconds, 200);
        }

        [Test]
        public void NestedDelimitersDoNotExplode()
        {
            var deep = new StringBuilder();
            for (int i = 0; i < 28; i++) deep.Append("\\left(a\\middle|");
            deep.Append('x');
            for (int i = 0; i < 28; i++) deep.Append("\\right)");
            var clock = Stopwatch.StartNew();
            Assert.IsFalse(Lay(deep.ToString()).Failed);
            Assert.Less(clock.ElapsedMilliseconds, 500);
        }

        [Test]
        public void AlphabetsMapWithTheirHoles()
        {
            Assert.AreEqual(0x210E, LetteraMathAlphabets.Map('h', MathAlphabet.Default));
            Assert.AreEqual(0x1D44E, LetteraMathAlphabets.Map('a', MathAlphabet.Default));
            Assert.AreEqual(0x1D434, LetteraMathAlphabets.Map('A', MathAlphabet.Default));
            Assert.AreEqual('a', LetteraMathAlphabets.Map('a', MathAlphabet.Roman));
            Assert.AreEqual(0x211D, LetteraMathAlphabets.Map('R', MathAlphabet.DoubleStruck));
            Assert.AreEqual(0x1D538, LetteraMathAlphabets.Map('A', MathAlphabet.DoubleStruck));
            Assert.AreEqual(0x2112, LetteraMathAlphabets.Map('L', MathAlphabet.Script));
            Assert.AreEqual(0x212D, LetteraMathAlphabets.Map('C', MathAlphabet.Fraktur));
            Assert.AreEqual(0x1D400, LetteraMathAlphabets.Map('A', MathAlphabet.Bold));
            Assert.AreEqual('1', LetteraMathAlphabets.Map('1', MathAlphabet.Default), "digits stay upright");
        }

        [Test]
        public void HostileFormulasThroughLayoutNeverThrow()
        {
            var random = new System.Random(11);
            string[] tokens = { "\\frac", "{", "}", "^", "_", "\\sqrt[", "]", "\\left(", "\\right)", "\\middle|", "&", "\\\\", "\\begin{pmatrix}", "\\end{pmatrix}", "x", "\\alpha", "'", "\\text{a}", "\\hline", "\\not", "\\overbrace", "\\xrightarrow[", "\\substack{", "\\sum", "\\int", "\\hat", "\\widehat", "\\phantom", "\\smash", "\\boxed", "\\color{red}" };
            FontFace face = Latin.LoadFace();
            int previous = 0;
            for (int run = 0; run < 2000; run++)
            {
                var text = new StringBuilder();
                int count = random.Next(0, 16);
                for (int i = 0; i < count; i++) text.Append(tokens[random.Next(tokens.Length)]);
                UnityEngine.TestTools.LogAssert.ignoreFailingMessages = true;
                Assert.DoesNotThrow(() => LetteraMathCache.Get(text.ToString(), run % 2 == 0, face, face), text.ToString());
                previous += count;
            }
            UnityEngine.TestTools.LogAssert.ignoreFailingMessages = false;
            Assert.Greater(previous, 0);
        }

        [Test]
        public void MeasureNeedsNoText()
        {
            Vector3 measured = LetteraMath.Measure("\\frac{a}{b}", false, Latin, 20);
            Assert.Greater(measured.x, 0);
            Assert.Greater(measured.y, 0);
            Assert.Greater(measured.z, 0);
            Assert.IsTrue(LetteraMath.Validate("x^2", out _, out _));
            Assert.IsFalse(LetteraMath.Validate("x^", out string message, out int position));
            Assert.IsNotNull(message);
            Assert.GreaterOrEqual(position, 0);
        }

        sealed class FakeData : ILetteraMathData
        {
            public readonly int[] Values = new int[(int)MathConstant.Count];
            public FakeData()
            {
                Values[(int)MathConstant.ScriptPercentScaleDown] = 70;
                Values[(int)MathConstant.ScriptScriptPercentScaleDown] = 50;
                Values[(int)MathConstant.AxisHeight] = 250;
                Values[(int)MathConstant.FractionRuleThickness] = 50;
                Values[(int)MathConstant.RadicalRuleThickness] = 50;
                Values[(int)MathConstant.SuperscriptShiftUp] = 400;
                Values[(int)MathConstant.SubscriptShiftDown] = 200;
            }
            public void Constants(int[] into) => Array.Copy(Values, into, Values.Length);
            public int ItalicCorrection(uint glyph) => 0;
            public int TopAccentAttachment(uint glyph) => 0;
            public bool IsExtended(uint glyph) => false;
            public int Kern(uint glyph, int corner, int height) => 0;
            public int Variants(uint glyph, bool vertical, uint[] glyphs, int[] advances) => 0;
            public int Assembly(uint glyph, bool vertical, MathPart[] parts, out int italic)
            {
                italic = 0;
                return 0;
            }
            public int MinOverlap(bool vertical) => 0;
        }

        LetteraMathLayout LayWith(ILetteraMathData data, string source, bool display = false)
        {
            LetteraMathCache.Clear();
            LetteraMathFont.Override = data;
            return Lay(source, display);
        }

        [Test]
        public void AConstantMovesWhatItShouldByExactlyThatMuch()
        {
            var data = new FakeData();
            float Sup() => LayWith(data, "x^2").Items[1].Y;
            float before = Sup();
            data.Values[(int)MathConstant.SuperscriptShiftUp] += 100;
            Assert.AreEqual(before + 0.1f, Sup(), 0.001f, "superscript shift");

            float Sub() => LayWith(data, "x_2").Items[1].Y;
            data.Values[(int)MathConstant.SubscriptShiftDown] = 2000;
            Assert.AreEqual(-2, Sub(), 0.001f, "subscript shift");

            float Bar() => FindRule(LayWith(data, "\\frac{a}{b}")).Y;
            before = Bar();
            data.Values[(int)MathConstant.AxisHeight] += 100;
            Assert.AreEqual(before + 0.1f, Bar(), 0.001f, "axis height");

            float Thickness() => FindRule(LayWith(data, "\\frac{a}{b}")).Thickness;
            data.Values[(int)MathConstant.FractionRuleThickness] = 80;
            Assert.AreEqual(0.08f, Thickness(), 0.001f, "bar thickness");

            float Num() => LayWith(data, "\\frac{a}{b}").Items[0].Y;
            before = Num();
            data.Values[(int)MathConstant.FractionNumeratorShiftUp] = 2000;
            Assert.AreEqual(2, Num(), 0.001f, "numerator shift");
            Assert.AreNotEqual(before, Num());

            float Display() => LayWith(data, "\\frac{a}{b}", true).Items[0].Y;
            data.Values[(int)MathConstant.FractionNumeratorDisplayStyleShiftUp] = 3000;
            Assert.AreEqual(3, Display(), 0.001f, "display shift");

            float Over() => FindRule(LayWith(data, "\\overline{x}")).Y;
            before = Over();
            data.Values[(int)MathConstant.OverbarVerticalGap] += 100;
            Assert.AreEqual(before + 0.1f, Over(), 0.001f, "overbar gap");

            float After() => LayWith(data, "x^2").Width;
            before = After();
            data.Values[(int)MathConstant.SpaceAfterScript] += 100;
            Assert.AreEqual(before + 0.1f, After(), 0.001f, "space after a script");
        }

        static LetteraMathItem FindRule(LetteraMathLayout layout)
        {
            foreach (LetteraMathItem item in layout.Items)
                if (item.Kind == MathItemKind.Rule)
                    return item;
            Assert.Fail("no rule");
            return default;
        }

        sealed class AssemblyData : ILetteraMathData
        {
            readonly FakeData m_Base = new FakeData();
            readonly uint m_Top, m_Middle, m_Bottom;
            public AssemblyData(uint top, uint middle, uint bottom)
            {
                m_Top = top;
                m_Middle = middle;
                m_Bottom = bottom;
            }
            public void Constants(int[] into) => m_Base.Constants(into);
            public int ItalicCorrection(uint glyph) => 0;
            public int TopAccentAttachment(uint glyph) => 0;
            public bool IsExtended(uint glyph) => false;
            public int Kern(uint glyph, int corner, int height) => 0;
            public int Variants(uint glyph, bool vertical, uint[] glyphs, int[] advances) => 0;
            public int Assembly(uint glyph, bool vertical, MathPart[] parts, out int italic)
            {
                italic = 0;
                parts[0] = new MathPart { Glyph = m_Bottom, StartConnector = 0, EndConnector = 200, FullAdvance = 1000 };
                parts[1] = new MathPart { Glyph = m_Middle, StartConnector = 200, EndConnector = 200, FullAdvance = 1000, Extender = true };
                parts[2] = new MathPart { Glyph = m_Top, StartConnector = 200, EndConnector = 0, FullAdvance = 1000 };
                return 3;
            }
            public int MinOverlap(bool vertical) => 50;
        }

        [Test]
        public void AnAssemblyReachesItsTargetWithPartsThatMeet()
        {
            FontFace face = Latin.LoadFace();
            uint glyph = LetteraNative.lt_font_glyph(face.Regular.Font, 'x', 0);
            LetteraMathLayout layout = LayWith(new AssemblyData(glyph, glyph, glyph), "\\left(\\begin{matrix}a\\\\b\\\\c\\\\d\\\\e\\\\f\\\\g\\\\h\\\\i\\\\j\\\\k\\\\l\\end{matrix}\\right)");
            int parts = 0;
            foreach (LetteraMathItem item in layout.Items)
                if (item.Cropped)
                    parts++;
            Assert.Greater(parts, 3, "extenders were repeated");
            Assert.Greater(layout.Ascent + layout.Descent, 10);
        }

        [Test]
        public void NoTableAndNoPiecesScalesAtMostThreeTimes()
        {
            LetteraMathFont.Override = null;
            LetteraMathLayout layout = Lay("\\left(\\begin{matrix}a\\\\b\\\\c\\\\d\\\\e\\\\f\\\\g\\\\h\\\\i\\\\j\\\\k\\\\l\\end{matrix}\\right)");
            foreach (LetteraMathItem item in layout.Items) Assert.LessOrEqual(item.Scale, 3.001f);
            Assert.IsFalse(layout.Failed);
        }
    }
}
#endif

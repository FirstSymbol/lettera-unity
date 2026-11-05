#if !LETTERA_NO_MATH
using System;
using System.Collections.Generic;
using System.Text;
using NUnit.Framework;
using UnityEngine;

namespace FirstSymbol.Lettera.Editor.Tests
{
    /// <summary>Display environments written in ordinary text with nothing around them, and their peace with tables.</summary>
    public class MathEnvironmentTests : TextFixture
    {
        [SetUp]
        public void Fresh() => LetteraTableCache.Clear();

        [TearDown]
        public void Back() => LetteraTableCache.Clear();

        static string Shown(LetteraWorldText label)
        {
            var text = new StringBuilder();
            foreach (int cp in label.Engine.ShownCodepoints) text.Append(char.ConvertFromUtf32(cp));
            return text.ToString();
        }

        static List<LetteraFormulaInfo> Formulas(LetteraWorldText text)
        {
            text.ForceRebuild();
            var found = new List<LetteraFormulaInfo>();
            text.GetFormulas(found);
            return found;
        }

        static List<string> Labels(LetteraWorldText text)
        {
            text.ForceRebuild();
            var tags = new List<(int, string, Rect)>();
            text.Engine.CollectFormulaTags(tags);
            var labels = new List<string>();
            foreach ((int _, string label, Rect _) in tags) labels.Add(label);
            return labels;
        }

        [Test]
        public void ABareAlignIsADisplayFormulaNumberedPerRow()
        {
            const string align = @"\begin{align} a &= b \\ c &= d \\ e &= f \notag \end{align}";
            LetteraWorldText text = Make("Before " + align + " after", 1200, 2000);
            Assert.AreEqual("Before \n\uFFFC\n after", Shown(text), "on a line of its own");
            List<LetteraFormulaInfo> formulas = Formulas(text);
            Assert.AreEqual(1, formulas.Count);
            Assert.IsTrue(formulas[0].Display);
            Assert.IsFalse(formulas[0].Failed, formulas[0].Message);
            Assert.AreEqual(align, formulas[0].Source, "the rows' \\\\ stay the formula's, and copying gives the source");
            CollectionAssert.AreEqual(new[] { "(1)", "(2)" }, Labels(text));
            Assert.AreEqual("x\n\uFFFC", Shown(Make("x\n" + align)), "no empty line where the source gave one");
        }

        [Test]
        public void EachEnvironmentAndItsStarredFormNumberAsInAFormula()
        {
            LetteraWorldText text = Make(
                @"\begin{equation} E = mc^2 \end{equation}" +
                @"\begin{equation*} x \end{equation*}" +
                @"\begin{gather} p \\ q \end{gather}" +
                @"\begin{gather*} r \end{gather*}" +
                @"\begin{multline} a + b \\ + c \end{multline}" +
                @"\begin{multline*} d \\ e \end{multline*}" +
                @"\begin{align*} u &= v \end{align*}" +
                @"\begin{displaymath} w \end{displaymath}" +
                @"<math=display>\begin{equation} y \end{equation}</math>", 1200, 4000);
            List<LetteraFormulaInfo> formulas = Formulas(text);
            Assert.AreEqual(9, formulas.Count);
            foreach (LetteraFormulaInfo formula in formulas)
            {
                Assert.IsTrue(formula.Display, formula.Source);
                Assert.IsFalse(formula.Failed, formula.Source + ": " + formula.Message);
            }
            CollectionAssert.AreEqual(new[] { "(1)", "(2)", "(3)", "(4)", "(5)" }, Labels(text), "one count for bare and tagged formulas alike");
            Assert.AreEqual(@"\begin{equation} y \end{equation}", formulas[8].Source, "what a tag wraps is the tag's");
        }

        [Test]
        public void WhatIsNotAClosedEnvironmentStaysText()
        {
            Assert.AreEqual(@"\begin{align} a &= b", Shown(Make(@"\begin{align} a &= b")), "no \\end");
            Assert.AreEqual(@"\begin{align} a \end{gather}", Shown(Make(@"\begin{align} a \end{gather}")), "the wrong \\end");
            Assert.AreEqual(@"\begin{aligned} a \end{aligned}", Shown(Make(@"\begin{aligned} a \end{aligned}")), "only a display environment stands alone");
            Assert.AreEqual(@"\begin{equation}x\end{equation}", Shown(Make(@"\\begin{equation}x\end{equation}")), "an escaped backslash");
            Assert.AreEqual(0, Formulas(Make(@"\begin{align} a &= b")).Count);
        }

        [Test]
        public void ATableAndAFormulaEachKeepTheirOwn()
        {
            LetteraWorldText side = Make(@"\begin{equation} x \end{equation}" + "and " + @"\begin{tabular}{c} a \\ b \end{tabular}", 1200, 2000);
            List<LetteraFormulaInfo> both = Formulas(side);
            Assert.AreEqual(2, both.Count, "a formula and a table side by side");
            Assert.IsTrue(both[0].Source.StartsWith(@"\begin{equation}"));
            Assert.IsTrue(both[1].Source.StartsWith(@"\begin{tabular}"));

            const string inTable = @"\begin{tabular}{l} \begin{equation} x \end{equation} \end{tabular}";
            LetteraWorldText table = Make("A " + inTable + " B", 1200, 2000);
            Assert.AreEqual("A \uFFFC B", Shown(table), "an environment inside a table is the table's");
            List<LetteraFormulaInfo> one = Formulas(table);
            Assert.AreEqual(1, one.Count);
            Assert.AreEqual(inTable, one[0].Source);

            const string inFormula = @"\begin{gather} x \\ \begin{tabular}{c} y \end{tabular} \end{gather}";
            LetteraWorldText formula = Make("A " + inFormula + " B", 1200, 2000);
            Assert.AreEqual("A \n\uFFFC\n B", Shown(formula), "a table inside a formula is the formula's");
            List<LetteraFormulaInfo> outer = Formulas(formula);
            Assert.AreEqual(1, outer.Count);
            Assert.AreEqual(inFormula, outer[0].Source);
        }

        [Test]
        public void DelimitersAroundAnEnvironmentKeepIt()
        {
            LetteraWorldText text = Make(@"a $$\begin{equation} x \end{equation}$$ b", 1200, 2000);
            text.Styles.Add(LetteraStyle.ForMatcher(new LetteraMathSpanMatcher { Dollar = true }, new LetteraMathEffect()));
            text.MarkStylesDirty();
            text.ForceRebuild();
            Assert.AreEqual("a \n\uFFFC\n b", Shown(text), "no stray dollars");
            List<LetteraFormulaInfo> formulas = Formulas(text);
            Assert.AreEqual(1, formulas.Count);
            Assert.AreEqual(@"\begin{equation} x \end{equation}", formulas[0].Source);
        }

        sealed class Counting : LetteraMathEnvironmentMatcher
        {
            public int Asked;

            public override void Find(LetteraMatchContext context)
            {
                Asked++;
                base.Find(context);
            }
        }

        [Test]
        public void ATextWithNoBackslashAsksNoMatcher()
        {
            Assert.IsTrue(new LetteraMathEnvironmentMatcher().StartsAtMarkup);
            var counting = new Counting();
            LetteraStyle added = LetteraStyle.ForMatcher(counting, new LetteraMathEffect());
            LetteraDefaultStyles.Styles.Add(added);
            try
            {
                LetteraWorldText plain = Make("equation align gather multline, written as words");
                Assert.AreEqual(0, counting.Asked);
                Assert.IsFalse(plain.Engine.Styled);
                Make(@"one \begin{equation} x \end{equation}");
                Assert.GreaterOrEqual(counting.Asked, 1);
            }
            finally
            {
                LetteraDefaultStyles.Styles.Remove(added);
            }
        }

        [Test]
        public void AFloodOfUnclosedEnvironmentsStaysLinear()
        {
            double Cost(int count)
            {
                var text = new StringBuilder();
                for (int i = 0; i < count; i++) text.Append(i % 2 == 0 ? @"\begin{align} a " : @"\begin{tabular}{c} b ");
                var matcher = new LetteraMathEnvironmentMatcher();
                var context = new LetteraMatchContext();
                double best = double.MaxValue;
                for (int run = 0; run < 3; run++)
                {
                    context.Begin(text.ToString().AsSpan());
                    var clock = System.Diagnostics.Stopwatch.StartNew();
                    matcher.Find(context);
                    best = System.Math.Min(best, clock.Elapsed.TotalMilliseconds);
                }
                return best;
            }
            Cost(1000);
            double a = Cost(4000), b = Cost(40000);
            Assert.Less(b, System.Math.Max(a * 25, a + 5), $"{a:0.000} ms then {b:0.000} ms");
        }
    }
}
#endif

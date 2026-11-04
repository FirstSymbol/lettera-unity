#if !LETTERA_NO_MATH
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace FirstSymbol.Lettera.Editor.Tests
{
    /// <summary>Equation numbers: which rows get one, how a text counts, and where each stands.</summary>
    public class MathNumberTests : TextFixture
    {
        const float Width = 1200;

        LetteraWorldText Built(string source, float width = Width)
        {
            LetteraWorldText text = Make(source, width, 2000);
            text.ForceRebuild();
            return text;
        }

        static List<(int Cp, string Label, Rect Box)> Tags(LetteraWorldText text)
        {
            text.ForceRebuild();
            var tags = new List<(int, string, Rect)>();
            text.Engine.CollectFormulaTags(tags);
            return tags;
        }

        static List<LetteraFormulaInfo> Formulas(LetteraWorldText text)
        {
            var found = new List<LetteraFormulaInfo>();
            text.GetFormulas(found);
            return found;
        }

        static List<string> Labels(List<(int Cp, string Label, Rect Box)> tags)
        {
            var labels = new List<string>();
            foreach ((int _, string label, Rect _) in tags) labels.Add(label);
            return labels;
        }

        [Test]
        public void AlignNumbersEachRowAtTheRightEdgeCentredOnItsRow()
        {
            LetteraWorldText text = Built(@"<math=display>\begin{align} a &= b \\ c &= d \\ e &= f \notag \\ g &= h \tag{*} \end{align}</math>");
            List<(int Cp, string Label, Rect Box)> tags = Tags(text);
            CollectionAssert.AreEqual(new[] { "(1)", "(2)", "(*)" }, Labels(tags));
            Rect formula = Formulas(text)[0].Box;
            float right = ((RectTransform)text.transform).rect.xMax;
            foreach ((int _, string label, Rect box) in tags)
            {
                Assert.AreEqual(right, box.xMax, 0.01f, label + " stands at the right edge");
                Assert.Greater(box.xMin, formula.xMax, label + " clears the formula");
                Assert.Greater(box.width, 0);
            }
            // Four rows of one height: each row is a quarter of the box, and a tag's middle is its row's.
            float row = formula.height / 4;
            float[] rows = { 0, 1, 3 };
            for (int i = 0; i < tags.Count; i++)
                Assert.AreEqual(formula.yMax - row * (rows[i] + 0.5f), tags[i].Box.center.y, row * 0.2f, tags[i].Label + " on its row");
        }

        [Test]
        public void EachEnvironmentNumbersAsAmsmathDoesAndATextCountsOnce()
        {
            LetteraWorldText text = Built(
                @"<math=display>\begin{equation} E = mc^2 \end{equation}</math>" +
                @"<math=display>\begin{gather*} x \\ y \tag{A} \end{gather*}</math>" +
                @"<math=display>\begin{multline} a + b \\ + c \end{multline}</math>" +
                @"<math=display>\begin{gather} p \\ q \end{gather}</math>" +
                @"<math=display>x^2 \tag*{star}</math>" +
                @"<math=display>\begin{align*} u &= v \end{align*}</math>" +
                @"and <math>y \tag{inline}</math> inline.");
            CollectionAssert.AreEqual(new[] { "(1)", "(A)", "(2)", "(3)", "(4)", "star" }, Labels(Tags(text)));

            List<LetteraFormulaInfo> formulas = Formulas(text);
            CollectionAssert.AreEqual(new[] { "(1)" }, formulas[0].Tags);
            Assert.IsNull(formulas[5].Tags, "a starred environment takes no number");
            Assert.IsNull(formulas[6].Tags, "an inline formula never does");

            List<(int Cp, string Label, Rect Box)> tags = Tags(text);
            Assert.AreEqual(formulas[0].Box.center.y, tags[0].Box.center.y, formulas[0].Box.height * 0.1f, "an equation's number is centred on it");
            Assert.Less(tags[2].Box.center.y, formulas[2].Box.center.y, "multline numbers its last line");
            Assert.AreEqual(formulas[4].Box.center.y, tags[5].Box.center.y, formulas[4].Box.height * 0.2f);

            LetteraWorldText other = Built(@"<math=display>\begin{equation} F = ma \end{equation}</math>");
            CollectionAssert.AreEqual(new[] { "(1)" }, Labels(Tags(other)), "each text counts from 1");
        }

        [Test]
        public void SetCounterStartsTheCountWhereItIsSaid()
        {
            LetteraWorldText text = Built(
                @"<math=display>\setcounter{equation}{4}\begin{gather} a \\ b \end{gather}</math>" +
                @"Text <math>\setcounter{equation}{9}</math> between." +
                @"<math=display>\begin{equation} c \end{equation}</math>");
            CollectionAssert.AreEqual(new[] { "(5)", "(6)", "(10)" }, Labels(Tags(text)));
            Assert.IsFalse(LetteraMath.Validate(@"\setcounter{page}{2} x", out _, out _));
            Assert.IsFalse(LetteraMath.Validate(@"\setcounter{equation}{-1} x", out _, out _));
            Assert.IsFalse(LetteraMath.Validate(@"a \tag{1} \tag{2}", out _, out _), "one tag to a line");
            Assert.IsTrue(LetteraMath.Validate(@"\begin{align} a \tag{1} \\ b \tag{2} \end{align}", out _, out _));
        }

        [Test]
        public void ANumberFollowsTheEdgeAndTheReadingSide()
        {
            LetteraWorldText text = Built(@"Before <math=display>\begin{equation} x + y \end{equation}</math> after");
            var rect = (RectTransform)text.transform;
            Assert.AreEqual(rect.rect.xMax, Tags(text)[0].Box.xMax, 0.01f);
            rect.sizeDelta = new Vector2(Width * 2, 2000);
            text.OnRectChanged(rect.rect.size);
            Assert.AreEqual(rect.rect.xMax, Tags(text)[0].Box.xMax, 0.01f, "a wider box moves the number with its edge");
            text.BaseDirection = LetteraDirection.RightToLeft;
            Rect box = Tags(text)[0].Box;
            Assert.AreEqual(rect.rect.xMin, box.xMin, 0.01f, "a right-to-left text puts it on the left");
            Assert.Less(box.xMax, Formulas(text)[0].Box.xMin);
        }

        [Test]
        public void ANumberDrawsGlyphsOfItsFormula()
        {
            LetteraWorldText plain = Built(@"<math=display>\begin{equation*} x \end{equation*}</math>");
            LetteraWorldText numbered = Built(@"<math=display>\begin{equation} x \end{equation}</math>");
            float right = ((RectTransform)numbered.transform).rect.xMax;
            Assert.Less(plain.InkBounds.max.x, right - 100);
            Assert.Greater(numbered.InkBounds.max.x, right - 20, "the number is drawn at the edge");
            int cp = Formulas(numbered)[0].CodePoint;
            numbered.Engine.EnsurePositioned();
            int clustered = 0;
            foreach (TextEngine.PlacedGlyph glyph in numbered.Engine.PlacedForTests)
                if (glyph.Cluster == cp && glyph.X > right - 100) clustered++;
            Assert.GreaterOrEqual(clustered, 3, "the parentheses and the digit belong to the formula, so selection and color reach them");
        }
    }
}
#endif

#if !LETTERA_NO_MATH
using NUnit.Framework;

namespace FirstSymbol.Lettera.Editor.Tests
{
    /// <summary>A formula in a field: the caret steps over it, a key takes it whole, undo brings it back, and paste from Lettera's own format makes one again.</summary>
    public class MathEditingTests : EditingFixture
    {
        [Test]
        public void TheCaretStepsOverAFormulaInOneKey()
        {
            LetteraEditable field = Field("a<math>x^2</math>b");
            field.Selectable.PlaceCaret(1);
            Press(field, LetteraKeyCode.Right);
            Assert.AreEqual(2, field.Selectable.Selection.Head, "one key, one formula");
            Press(field, LetteraKeyCode.Left);
            Assert.AreEqual(1, field.Selectable.Selection.Head);
        }

        [Test]
        public void BackspaceTakesAFormulaWholeAndUndoBringsItBack()
        {
            const string source = "a<math>x^2</math>b";
            LetteraEditable field = Field(source);
            field.Selectable.PlaceCaret(2);
            Press(field, LetteraKeyCode.Backspace);
            StringAssert.DoesNotContain("math", field.Source);
            StringAssert.DoesNotContain("x^2", field.Source);
            Assert.AreEqual("ab", field.Source);
            Assert.IsTrue(field.Undo());
            Assert.AreEqual(source, field.Source);
        }

        [Test]
        public void AFormulaCopiesAndPastesBackAsAFormula()
        {
            LetteraEditable from = Field("a<math>x^2</math>b");
            from.Selectable.SelectAll();
            Assert.IsTrue(from.Selectable.Copy());
            StringAssert.Contains("a$x^2$b", Clip.Text(LetteraClipFormat.PlainText));
            LetteraEditable to = Field("");
            to.Paste();
            StringAssert.Contains("<math>x^2</math>", to.Source);
        }
    }
}
#endif

using NUnit.Framework;

namespace FirstSymbol.Lettera.Editor.Tests
{
    /// <summary>In play, a field's edit waits in the engine as characters; the text's string is made when something reads it.</summary>
    public class FieldSourceTests : EditingFixture
    {
        [SetUp]
        public void AsInPlay() => TextEngine.PendingSourceInEditMode = true;

        [TearDown]
        public void Back() => TextEngine.PendingSourceInEditMode = false;

        static LetteraTextData DataOf(LetteraEditable field) => ((ITextHost)field.GetComponent<LetteraWorldText>()).Data;

        [Test]
        public void TypingWritesNoStringUntilTheTextIsRead()
        {
            LetteraEditable field = Field("Hello");
            LetteraWorldText label = field.GetComponent<LetteraWorldText>();
            Type(field, " world");
            label.ForceRebuild();
            Assert.AreEqual("Hello", DataOf(field).Text, "the saved string waits");
            Assert.AreEqual("Hello world", field.PlainText, "what shows is what was typed");
            Assert.AreEqual(11, field.Length);
            Assert.AreEqual("Hello world", label.Text, "reading the text makes its string");
            Assert.AreEqual("Hello world", DataOf(field).Text);
            Type(field, "!");
            Assert.AreEqual("Hello world!", ((ITextComponent)label).SourceText);
        }

        [Test]
        public void SettingTheTextReplacesAWaitingEdit()
        {
            LetteraEditable field = Field("Hello");
            LetteraWorldText label = field.GetComponent<LetteraWorldText>();
            Type(field, "!");
            // The same string as the saved one, which the edit had moved away from, still takes effect.
            label.Text = "Hello";
            label.ForceRebuild();
            Assert.AreEqual("Hello", field.PlainText);
            Assert.AreEqual("Hello", label.Text);
            Type(field, "?");
            label.Text = "Bye";
            label.ForceRebuild();
            Assert.AreEqual("Bye", field.PlainText);
        }

        [Test]
        public void UndoAndRuntimeTextWorkOnAWaitingEdit()
        {
            LetteraEditable field = Field("Hello");
            LetteraWorldText label = field.GetComponent<LetteraWorldText>();
            Type(field, "abc");
            Press(field, LetteraKeyCode.Z, Ctrl);
            label.ForceRebuild();
            Assert.AreEqual("Hello", field.PlainText);
            Type(field, "x");
            label.SetText("Runtime");
            label.ForceRebuild();
            Assert.AreEqual("Runtime", label.Engine.CleanText, "runtime text shows over the field's source");
            Assert.AreEqual("Hellox", label.Text, "and the source stays what was typed");
        }

        [Test]
        public void ALongFieldSplicesItsEditsAndShowsTheSameAsBuiltWhole()
        {
            var body = new System.Text.StringBuilder();
            for (int i = 0; i < 120; i++) body.Append("Line ").Append(i).Append(" of a field\n");
            LetteraEditable field = Field(body.ToString(), 600, 4000);
            LetteraWorldText label = field.GetComponent<LetteraWorldText>();
            field.Selectable.PlaceCaret(field.Length / 2);
            Type(field, "typed in the middle");
            label.ForceRebuild();
            string typed = label.Text;
            LetteraWorldText whole = Make(typed, 600, 4000);
            whole.ForceRebuild();
            Assert.AreEqual(whole.Engine.CleanText, label.Engine.CleanText);
            Assert.AreEqual(whole.Engine.LineCountForHooks, label.Engine.LineCountForHooks);
            Assert.AreEqual(whole.GlyphCount, label.GlyphCount);
        }
    }
}

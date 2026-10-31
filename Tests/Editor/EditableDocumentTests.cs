using System.Text;
using NUnit.Framework;
using UnityEngine;

namespace FirstSymbol.Lettera.Editor.Tests
{
    /// <summary>A field over a document: edits land in the rope, in the right place, and undo finds them again.</summary>
    public class EditableDocumentTests : EditingFixture
    {
        static string Lines(int count)
        {
            var body = new StringBuilder(count * 20);
            for (int i = 0; i < count; i++) body.Append("Line ").Append(i).Append(" of some words\n");
            return body.ToString();
        }

        LetteraEditable DocField(string text)
        {
            LetteraWorldText label = Make("", 600, 200);
            var field = label.gameObject.AddComponent<LetteraEditable>();
            label.OpenDocument(LetteraRope.FromString(text), true);
            label.ForceRebuild();
            field.Selectable.Activate();
            field.Selectable.PlaceCaret(0);
            return field;
        }

        static LetteraWorldText Label(LetteraEditable field) => field.GetComponent<LetteraWorldText>();

        [Test]
        public void TypingGoesIntoTheRopeAndTheFileTextStaysUntouched()
        {
            LetteraEditable field = DocField(Lines(2000));
            LetteraWorldText label = Label(field);
            string before = label.Text;
            Type(field, "abc");
            Assert.AreEqual("abcLine 0 of", label.Document.ToString(0, 12));
            Assert.AreEqual(before, label.Text, "the serialized text");
        }

        [Test]
        public void AnEditFarDownLandsWhereTheWindowIs()
        {
            string body = Lines(5000);
            LetteraEditable field = DocField(body);
            LetteraWorldText label = Label(field);
            int target = body.IndexOf("Line 3000 ", System.StringComparison.Ordinal);
            label.View.ScrollToChar(target);
            label.ForceRebuild();
            field.Selectable.PlaceCaret(label.Engine.ShownOfDocument(target));
            Type(field, "XY");
            string now = label.Document.ToString();
            Assert.AreEqual(body.Insert(target, "XY"), now);
        }

        [Test]
        public void ManyEditsAgreeWithAPlainString()
        {
            var random = new System.Random(8);
            string body = Lines(3000);
            var model = new StringBuilder(body);
            LetteraEditable field = DocField(body);
            LetteraWorldText label = Label(field);
            for (int step = 0; step < 300; step++)
            {
                int target = random.Next(model.Length);
                label.View.ScrollToChar(target);
                label.ForceRebuild();
                int shown = label.Engine.ShownOfDocument(target);
                field.Selectable.PlaceCaret(shown);
                int at = label.Engine.DocCharOf(field.Selectable.Selection.Head);
                if (random.Next(3) == 0 && at > 0 && model[at - 1] != '\n' && at < model.Length)
                {
                    Press(field, LetteraKeyCode.Backspace);
                    model.Remove(at - 1, 1);
                }
                else
                {
                    Type(field, "q");
                    model.Insert(at, 'q');
                }
                label.ForceRebuild();
                if (step % 25 == 0) Assert.AreEqual(model.ToString(), label.Document.ToString(), "at step " + step);
            }
            Assert.AreEqual(model.ToString(), label.Document.ToString());
        }

        [Test]
        public void UndoFindsItsEditAfterAFarScroll()
        {
            string body = Lines(5000);
            LetteraEditable field = DocField(body);
            LetteraWorldText label = Label(field);
            // One word: an undo takes back a word at a time.
            Type(field, "first");
            string typed = label.Document.ToString();
            Assert.AreNotEqual(body, typed);
            label.View.ScrollToChar(body.Length / 2);
            label.ForceRebuild();
            Assert.Greater(label.View.WindowStart, 1000);
            Assert.IsTrue(field.Undo());
            label.ForceRebuild();
            Assert.AreEqual(body, label.Document.ToString(), "undone in its own place");
            Assert.Less(label.View.WindowStart, 1000, "the view went there");
            Assert.IsTrue(field.Redo());
            Assert.AreEqual(typed, label.Document.ToString());
        }

        [Test]
        public void AnEditLeavesATagOpenedBeforeTheWindowInForce()
        {
            var body = new StringBuilder("<color=#ff0000>");
            body.Append(Lines(4000));
            LetteraEditable field = DocField(body.ToString());
            LetteraWorldText label = Label(field);
            label.View.FinishBackgroundForTests();
            label.View.ScrollToChar(body.Length / 2);
            label.ForceRebuild();
            Assert.AreEqual(0, label.Engine.Colors[0].g, "red before the edit");
            field.Selectable.PlaceCaret(3);
            Type(field, "q");
            label.ForceRebuild();
            Assert.AreEqual(0, label.Engine.Colors[0].g, "still red after it: nothing flashed");
            StringAssert.Contains("q", label.Document.ToString(label.View.WindowStart, 40));
        }

        [Test]
        public void AnEditReachingPastTheWindowIsRefusedWholeNotHalfDone()
        {
            UnityEngine.TestTools.LogAssert.ignoreFailingMessages = true;
            string body = Lines(5000);
            LetteraEditable field = DocField(body);
            LetteraWorldText label = Label(field);
            field.Selectable.SetDocumentSelection(0, body.Length);
            Press(field, LetteraKeyCode.Delete);
            UnityEngine.TestTools.LogAssert.ignoreFailingMessages = false;
            Assert.AreEqual(body, label.Document.ToString(), "nothing went");
        }

        [Test]
        public void AKeystrokeInABigDocumentCostsTheSameAtEitherEnd()
        {
            string body = Lines(500000);
            LetteraEditable field = DocField(body);
            LetteraWorldText label = Label(field);
            double Round(int docChar)
            {
                label.View.ScrollToChar(docChar);
                label.ForceRebuild();
                field.Selectable.PlaceCaret(label.Engine.ShownOfDocument(docChar));
                var clock = System.Diagnostics.Stopwatch.StartNew();
                for (int i = 0; i < 10; i++)
                {
                    Now += 1;
                    Type(field, "x");
                    label.ForceRebuild();
                }
                return clock.Elapsed.TotalMilliseconds / 10;
            }
            Round(10);
            double start = Round(10), end = Round(label.Document.Length - 10);
            Debug.Log($"LETTERA_BUDGET keystroke_doc_{body.Length / (1 << 20)}MB start={start:0.00}ms end={end:0.00}ms");
            Assert.Less(end, start * 3 + 5, "the cost grew with the position");
        }

        [Test]
        public void AnInsertBelowTheViewRebuildsNothing()
        {
            string body = Lines(5000);
            LetteraEditable field = DocField(body);
            LetteraWorldText label = Label(field);
            label.ForceRebuild();
            int rebuilds = label.RebuildCount;
            double extent = label.View.Rope.TotalHeight;
            label.Document.Insert(label.Document.Length, Lines(500));
            label.ForceRebuild();
            Assert.AreEqual(rebuilds, label.RebuildCount, "what shows did not change");
            Assert.Greater(label.View.Rope.TotalHeight, extent, "but there is more to scroll to");
        }

        [Test]
        public void ARopeEditFromCodeInTheWindowShowsAtOnce()
        {
            LetteraEditable field = DocField(Lines(100));
            LetteraWorldText label = Label(field);
            label.Document.Insert(0, "Added ");
            label.ForceRebuild();
            Assert.AreEqual('A', label.Engine.ShownCodepoints[0]);
        }

        [Test]
        public void APlainFieldIsAsItWas()
        {
            LetteraEditable field = Field("Plain");
            Type(field, "x");
            Assert.AreEqual("Plainx", field.Source);
            Assert.IsTrue(field.Undo());
            Assert.AreEqual("Plain", field.Source);
        }
    }
}

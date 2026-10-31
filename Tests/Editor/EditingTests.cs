using System;
using System.Collections.Generic;
using System.Text;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace FirstSymbol.Lettera.Editor.Tests
{
    /// <summary>The document, its undo, the selection and the keys.</summary>
    public class EditingTests : EditingFixture
    {
        const string Family = "\U0001F468‍\U0001F469‍\U0001F467";

        // ------------------------------------------------------------- components

        [Test]
        public void AddingEditableAddsSelectable()
        {
            LetteraWorldText label = Make("a");
            var field = label.gameObject.AddComponent<LetteraEditable>();
            Assert.IsNotNull(label.GetComponent<LetteraSelectable>());
            Assert.AreSame(field, field.Selectable.Editable);
        }

        [Test]
        public void WithoutATextItIsAnErrorAndNothingElse()
        {
            var go = new GameObject("No Text", typeof(RectTransform));
            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("needs a LetteraText"));
            go.AddComponent<LetteraEditable>();
            UnityEngine.Object.DestroyImmediate(go);
        }

        [Test]
        public void ATextWithoutTheComponentsCostsTheSameAsBefore()
        {
            LetteraWorldText plain = Make("Some text");
            int vertices = plain.Engine.VertexCount, rebuilds = plain.RebuildCount;
            plain.ForceRebuild();
            Assert.AreEqual(rebuilds, plain.RebuildCount);
            Assert.AreEqual(vertices, plain.Engine.VertexCount);
            Assert.AreEqual(0, plain.GetComponentsInChildren<Transform>().Length - 1);
        }

        [Test]
        public void ReadOnlyRefusesEditsAndKeepsCopyAndNavigation()
        {
            LetteraEditable field = Field("abc");
            int patched = 0;
            field.Patched += _ => patched++;
            field.ReadOnly = true;
            Type(field, "x");
            Press(field, LetteraKeyCode.Backspace);
            Press(field, LetteraKeyCode.Z, Ctrl);
            Assert.IsFalse(field.Cut());
            Assert.IsFalse(field.Paste());
            Assert.AreEqual("abc", field.Source);
            Assert.AreEqual(0, patched);
            Press(field, LetteraKeyCode.A, Ctrl);
            Assert.AreEqual(3, field.Selectable.Selection.Length);
            Press(field, LetteraKeyCode.C, Ctrl);
            Assert.AreEqual("abc", Clip.Text(LetteraClipFormat.PlainText));
        }

        [Test]
        public void EventsComeInOrderAndValueIsNewByThen()
        {
            LetteraEditable field = Field("");
            var log = new List<string>();
            field.Patched += p => log.Add($"patched v{field.Version} {field.PlainText} +{p.Inserted}");
            field.ValueChanged += s => log.Add("value " + s);
            field.EditReported += r => log.Add("reason " + r);
            Type(field, "a");
            CollectionAssert.AreEqual(new[] { "patched v1 a +1", "value a", "reason user.type" }, log);
        }

        [Test]
        public void ShownTextChangesAtMostOncePerFrame()
        {
            LetteraEditable field = Field("");
            int values = 0, shown = 0;
            field.ValueChanged += _ => values++;
            field.ShownTextChanged += () => shown++;
            Type(field, "abc");
            field.Selectable.Tick();
            Assert.AreEqual(3, values);
            Assert.AreEqual(1, shown);
            field.Selectable.Tick();
            Assert.AreEqual(1, shown);
        }

        [Test]
        public void ReadingTheValueAllocatesNothing()
        {
            LetteraEditable field = Field("Some text with \U0001F600");
            Span<char> buffer = stackalloc char[64];
            using var recorder = Unity.Profiling.ProfilerRecorder.StartNew(Unity.Profiling.ProfilerCategory.Memory, "GC Allocated In Frame");
            long quietest = long.MaxValue;
            for (int window = 0; window < 5; window++)
            {
                long before = recorder.CurrentValue;
                for (int i = 0; i < 1000; i++)
                {
                    field.CopyTo(buffer);
                    field.Equals("Some text".AsSpan());
                    _ = field.Length;
                }
                quietest = Math.Min(quietest, recorder.CurrentValue - before);
            }
            Assert.Less(quietest, 200);
            Assert.AreEqual(field.PlainText.Length, field.CopyTo(buffer));
            Assert.IsTrue(field.Equals(field.PlainText.AsSpan()));
            Assert.IsFalse(field.Equals("Some".AsSpan()));
        }

        // ------------------------------------------------------------- document

        [Test]
        public void AnEditChangesOnlyWhatItSplices()
        {
            LetteraEditable field = Field("a  <b>bc</b>");
            Assert.AreEqual("a  bc", field.PlainText);
            Assert.AreEqual(5, field.Length);
            field.Selectable.PlaceCaret(0);
            Type(field, "x");
            Assert.AreEqual("xa  <b>bc</b>", field.Source);
        }

        [Test]
        public void SettingTheSameSourceDoesNothing()
        {
            LetteraEditable field = Field("abc");
            Type(field, "d");
            field.Selectable.Select(1, 2);
            int events = 0;
            field.ValueChanged += _ => events++;
            Assert.IsFalse(field.SetSource("abcd"));
            Assert.AreEqual(0, events);
            Assert.IsTrue(field.CanUndo);
            Assert.AreEqual(1, field.Selectable.Selection.Start);
        }

        [Test]
        public void SanitizingNormalizesAndDropsAndNulRefusesAll()
        {
            LetteraEditable field = Field("");
            field.InsertText("a\r\nb\0");
            Assert.AreEqual("", field.Source);
            field.InsertText("a\r\nb\u0007");
            Assert.AreEqual("a\nb", field.Source);
            field.InsertText("\tc\rd");
            Assert.AreEqual("a\nb\tc\nd", field.Source);
        }

        [Test]
        public void DeletingTakesWholeClusters()
        {
            LetteraEditable field = Field(Family);
            Press(field, LetteraKeyCode.Backspace);
            Assert.AreEqual("", field.Source);

            field.SetSource(Family + "é");
            field.Selectable.PlaceCaret(0);
            Press(field, LetteraKeyCode.Delete);
            Assert.AreEqual("é", field.Source);
            Press(field, LetteraKeyCode.Delete);
            Assert.AreEqual("", field.Source);
        }

        [Test]
        public void SeveralDeletesInOneFrameNeedNoLayout()
        {
            LetteraEditable field = Field("abcd");
            field.DeleteBack();
            field.DeleteBack();
            field.DeleteBack();
            Assert.AreEqual("a", field.Source);
        }

        [Test]
        public void DeletingTheLastStyledCharacterTakesTheTags()
        {
            LetteraEditable field = Field("a<b>b</b>c");
            field.Selectable.PlaceCaret(2);
            Press(field, LetteraKeyCode.Backspace);
            Assert.AreEqual("ac", field.Source);
            Assert.AreEqual(1, field.Selectable.Selection.Head);
            field.Undo();
            Assert.AreEqual("a<b>b</b>c", field.Source);
            Assert.AreEqual(2, field.Selectable.Selection.Head);
        }

        [Test]
        public void DeletingAcrossATagKeepsItBalanced()
        {
            LetteraEditable field = Field("ab<b>cd</b>ef");
            field.Selectable.Select(1, 3);
            Press(field, LetteraKeyCode.Delete);
            Assert.AreEqual("a<b>d</b>ef", field.Source);
            Assert.AreEqual("adef", field.PlainText);
        }

        [Test]
        public void AScriptWritingTheComponentCountsAsSetSource()
        {
            LetteraEditable field = Field("abc");
            Type(field, "d");
            int values = 0;
            field.ValueChanged += _ => values++;
            field.GetComponent<LetteraWorldText>().Text = "xy";
            Assert.AreEqual(1, values);
            Assert.IsFalse(field.CanUndo);
            Assert.AreEqual(2, field.Selectable.Selection.Head);
        }

        [Test]
        public void RebaseKeepsLocalUndo()
        {
            LetteraEditable field = Field("");
            Type(field, "hello");
            Assert.IsTrue(field.SetSource("hello world", LetteraHistoryMode.Rebase));
            Assert.IsTrue(field.CanUndo);
            field.Undo();
            Assert.AreEqual(" world", field.Source);
        }

        [Test]
        public void RecordMakesASingleStep()
        {
            LetteraEditable field = Field("abc");
            field.SetSource("xyz", LetteraHistoryMode.Record);
            Assert.AreEqual("xyz", field.Source);
            field.Undo();
            Assert.AreEqual("abc", field.Source);
            field.Redo();
            Assert.AreEqual("xyz", field.Source);
        }

        [Test]
        public void TransposeSwapsClusters()
        {
            LetteraEditable field = Field("abc");
            field.Selectable.PlaceCaret(1);
            field.TransposeClusters();
            Assert.AreEqual("bac", field.Source);
            Assert.AreEqual(2, field.Selectable.Selection.Head);
            field.Selectable.PlaceCaret(3);
            field.TransposeClusters();
            Assert.AreEqual("bca", field.Source);
            field.SetSource("a");
            field.Selectable.PlaceCaret(1);
            Assert.IsFalse(field.TransposeClusters());
        }

        // ------------------------------------------------------------- undo

        [Test]
        public void TypingUndoesByWord()
        {
            LetteraEditable field = Field("");
            Type(field, "hello world");
            field.Undo();
            Assert.AreEqual("hello", field.Source);
            field.Undo();
            Assert.AreEqual("", field.Source);
            field.Redo();
            Assert.AreEqual("hello", field.Source);
        }

        [Test]
        public void APauseStartsANewStep()
        {
            LetteraEditable field = Field("");
            Type(field, "ab");
            Now += 1;
            Type(field, "cd");
            field.Undo();
            Assert.AreEqual("ab", field.Source);
        }

        [Test]
        public void HeldBackspaceUndoesAtOnceAndBothDirectionsDoNot()
        {
            LetteraEditable field = Field("abcdef");
            for (int i = 0; i < 6; i++) Press(field, LetteraKeyCode.Backspace);
            field.Undo();
            Assert.AreEqual("abcdef", field.Source);

            field.Selectable.PlaceCaret(3);
            Press(field, LetteraKeyCode.Backspace);
            Press(field, LetteraKeyCode.Delete);
            field.Undo();
            Assert.AreEqual("abdef", field.Source);
        }

        [Test]
        public void PasteNeverMerges()
        {
            LetteraEditable field = Field("");
            Clip.Write(new[] { new LetteraClipItem(LetteraClipFormat.PlainText, "x") });
            field.Paste();
            field.Paste();
            Assert.AreEqual("xx", field.Source);
            field.Undo();
            Assert.AreEqual("x", field.Source);
        }

        [Test]
        public void MovingTheCaretEndsMerging()
        {
            LetteraEditable field = Field("");
            Type(field, "a");
            field.Selectable.PlaceCaret(0);
            Type(field, "b");
            field.Undo();
            Assert.AreEqual("a", field.Source);
        }

        [Test]
        public void ReplacingASelectionIsAStepOfItsOwn()
        {
            LetteraEditable field = Field("abc");
            Type(field, "d");
            field.Selectable.Select(0, 4);
            Type(field, "xy");
            Assert.AreEqual("xy", field.Source);
            field.Undo();
            Assert.AreEqual("abcd", field.Source);
            Assert.AreEqual(4, field.Selectable.Selection.Length);
        }

        [Test]
        public void TheMemoryLimitDropsTheOldestAndKeepsTheNewest()
        {
            int old = LetteraSettings.Instance.Editing.UndoMemoryLimit;
            LetteraSettings.Instance.Editing.UndoMemoryLimit = 1024;
            try
            {
                LetteraEditable field = Field("");
                for (int i = 0; i < 100; i++)
                {
                    Now += 1;
                    field.InsertText("0123456789012345678901234567890123456789");
                }
                Assert.IsTrue(field.CanUndo);
                Assert.Less(field.UndoDepth, 100);
                string before = field.Source;
                field.Undo();
                Assert.Less(field.Source.Length, before.Length);
            }
            finally
            {
                LetteraSettings.Instance.Editing.UndoMemoryLimit = old;
            }
        }

        [Test]
        public void AGroupIsOneStepAndAnEmptyOneIsNone()
        {
            LetteraEditable field = Field("");
            using (field.BeginHistoryGroup())
            {
                field.InsertText("a");
                using (field.BeginHistoryGroup()) field.InsertText("b");
                field.InsertText("c");
            }
            Assert.AreEqual(1, field.UndoDepth);
            field.Undo();
            Assert.AreEqual("", field.Source);
            field.Redo();
            using (field.BeginHistoryGroup()) { }
            Assert.AreEqual(1, field.UndoDepth);
        }

        [Test]
        public void TheHistoryStoreAllocatesNothingPerKeystroke()
        {
            var history = new LetteraEditHistory();
            var source = new StringBuilder("abc");
            for (int i = 0; i < 2000; i++)
            {
                history.BeginEdit(LetteraEditHistory.Kind.Typing, LetteraEditHistory.TokenClass.Word, LetteraSelectionRange.AtCaret(0), i, 0.5f);
                history.AddSplice(0, source, 0, "x");
                history.EndEdit(LetteraSelectionRange.AtCaret(1), 0);
            }
            history.Clear();
            using var recorder = Unity.Profiling.ProfilerRecorder.StartNew(Unity.Profiling.ProfilerCategory.Memory, "GC Allocated In Frame");
            long quietest = long.MaxValue;
            for (int window = 0; window < 5; window++)
            {
                history.Clear();
                long before = recorder.CurrentValue;
                for (int i = 0; i < 1000; i++)
                {
                    history.BeginEdit(LetteraEditHistory.Kind.Typing, LetteraEditHistory.TokenClass.Word, LetteraSelectionRange.AtCaret(0), i, 0.5f);
                    history.AddSplice(0, source, 0, "x");
                    history.EndEdit(LetteraSelectionRange.AtCaret(1), 0);
                }
                quietest = Math.Min(quietest, recorder.CurrentValue - before);
            }
            Assert.Less(quietest, 200);
        }

        // ------------------------------------------------------------- selection

        [Test]
        public void IndicesPullToTheEnds()
        {
            LetteraSelectable text = Reader("abc");
            text.Select(-5, 999);
            Assert.AreEqual((0, 3), (text.Selection.Start, text.Selection.End));
        }

        [Test]
        public void ACaretCannotLandInsideACluster()
        {
            LetteraSelectable text = Reader("a\U0001F44D\U0001F3FDb");
            text.PlaceCaret(2);
            Assert.IsTrue(text.Selection.Head == 1 || text.Selection.Head == 3, text.Selection.ToString());
        }

        [Test]
        public void AnEndpointNeverCollapsesAndSwapsPastTheOtherEnd()
        {
            LetteraSelectable text = Reader("abcdef");
            text.Select(2, 4);
            text.DragEndpoint(false, 2);
            Assert.AreEqual((2, 3), (text.Selection.Anchor, text.Selection.Head));
            text.DragEndpoint(false, 0);
            Assert.AreEqual(2, text.Selection.Anchor);
            Assert.AreEqual(0, text.Selection.Head);
            Assert.GreaterOrEqual(text.Selection.Length, 1);
        }

        [Test]
        public void FiltersClampVetoAndRunInOrder()
        {
            LetteraSelectable text = Reader("abcdefghij");
            var order = new List<int>();
            void First(ref LetteraSelectionProposal p) => order.Add(1);
            void Second(ref LetteraSelectionProposal p)
            {
                order.Add(2);
                p.Wanted = new LetteraSelectionRange(0, 3);
            }
            text.AddSelectionFilter(Second, 5);
            text.AddSelectionFilter(First, 1);
            Assert.IsTrue(text.SelectAll());
            Assert.AreEqual(3, text.Selection.End);
            CollectionAssert.AreEqual(new[] { 1, 2 }, order);
            text.RemoveSelectionFilter(Second);
            int changes = 0;
            text.SelectionChanged += _ => changes++;
            void Veto(ref LetteraSelectionProposal p) => p.Veto = true;
            text.AddSelectionFilter(Veto);
            Assert.IsFalse(text.SelectAll());
            Assert.AreEqual(0, changes);
        }

        [Test]
        public void TypingAsksNoFilter()
        {
            LetteraEditable field = Field("");
            bool asked = false;
            void Filter(ref LetteraSelectionProposal p) => asked = true;
            field.Selectable.AddSelectionFilter(Filter);
            Type(field, "x");
            Assert.IsFalse(asked);
            Assert.AreEqual(1, field.Selectable.Selection.Head);
        }

        [Test]
        public void ASelectionChangeRebuildsNothing()
        {
            LetteraSelectable text = Reader("Some text\nwith lines");
            text.Tick();
            LetteraWorldText label = text.GetComponent<LetteraWorldText>();
            label.ForceRebuild();
            int rebuilds = label.RebuildCount;
            for (int i = 0; i < 20; i++)
            {
                text.Select(0, i);
                text.Tick();
                text.CollapseSelection();
                text.Tick();
            }
            Assert.AreEqual(rebuilds, label.RebuildCount);
        }

        // ------------------------------------------------------------- moving

        [Test]
        public void ArrowsMoveLogicallyByParagraphDirection()
        {
            LetteraEditable ltr = MixedField("abc");
            ltr.Selectable.PlaceCaret(1);
            Press(ltr, LetteraKeyCode.Right);
            Assert.AreEqual(2, ltr.Selectable.Selection.Head);

            LetteraEditable rtl = MixedField("אבג");
            rtl.Selectable.PlaceCaret(1);
            Press(rtl, LetteraKeyCode.Right);
            Assert.AreEqual(0, rtl.Selectable.Selection.Head);
            Press(rtl, LetteraKeyCode.Left);
            Assert.AreEqual(1, rtl.Selectable.Selection.Head);
        }

        [Test]
        public void ARangeCollapsesWithoutMoving()
        {
            LetteraEditable field = Field("abcdef");
            field.Selectable.Select(2, 4);
            Press(field, LetteraKeyCode.Left);
            Assert.AreEqual(2, field.Selectable.Selection.Head);
            Assert.IsTrue(field.Selectable.Selection.IsCaret);
            field.Selectable.Select(2, 4);
            Press(field, LetteraKeyCode.Right);
            Assert.AreEqual(4, field.Selectable.Selection.Head);
        }

        [Test]
        public void VerticalMovesKeepTheirColumn()
        {
            LetteraEditable field = Field("long long long\nab\nlong long long");
            field.Selectable.PlaceCaret(10);
            Press(field, LetteraKeyCode.Down);
            Assert.AreEqual(17, field.Selectable.Selection.Head);
            Press(field, LetteraKeyCode.Down);
            Assert.AreEqual(28, field.Selectable.Selection.Head);
            Press(field, LetteraKeyCode.Left);
            Press(field, LetteraKeyCode.Up);
            Assert.AreEqual(17, field.Selectable.Selection.Head);
            Press(field, LetteraKeyCode.Up);
            Assert.AreEqual(9, field.Selectable.Selection.Head, "a new column after the left move");
        }

        [Test]
        public void UpFromTheFirstLineGoesToTheStartAndDownFromTheLastToTheEnd()
        {
            LetteraEditable field = Field("abc\ndef");
            field.Selectable.PlaceCaret(2);
            Press(field, LetteraKeyCode.Up);
            Assert.AreEqual(0, field.Selectable.Selection.Head);
            field.Selectable.PlaceCaret(5);
            Press(field, LetteraKeyCode.Down);
            Assert.AreEqual(7, field.Selectable.Selection.Head);
        }

        [Test]
        public void DownLandsOnAnEmptyLine()
        {
            LetteraEditable field = Field("abc\n\ndef");
            field.Selectable.PlaceCaret(1);
            Press(field, LetteraKeyCode.Down);
            Assert.AreEqual(4, field.Selectable.Selection.Head);
        }

        // ------------------------------------------------------------- key table

        static readonly object[] s_PcKeys =
        {
            new object[] { LetteraKeyCode.Left, LetteraKeyModifiers.None, LetteraEditVerb.MoveCharPrev },
            new object[] { LetteraKeyCode.Right, Shift, LetteraEditVerb.ExtendCharNext },
            new object[] { LetteraKeyCode.Left, Ctrl, LetteraEditVerb.MoveWordPrev },
            new object[] { LetteraKeyCode.Right, Ctrl | Shift, LetteraEditVerb.ExtendWordNext },
            new object[] { LetteraKeyCode.Home, LetteraKeyModifiers.None, LetteraEditVerb.MoveLineStart },
            new object[] { LetteraKeyCode.End, Shift, LetteraEditVerb.ExtendLineEnd },
            new object[] { LetteraKeyCode.Up, Ctrl, LetteraEditVerb.MoveParagraphStart },
            new object[] { LetteraKeyCode.Down, Ctrl | Shift, LetteraEditVerb.ExtendParagraphEnd },
            new object[] { LetteraKeyCode.Up, LetteraKeyModifiers.None, LetteraEditVerb.MoveUp },
            new object[] { LetteraKeyCode.Down, Shift, LetteraEditVerb.ExtendDown },
            new object[] { LetteraKeyCode.PageUp, LetteraKeyModifiers.None, LetteraEditVerb.MovePageUp },
            new object[] { LetteraKeyCode.PageDown, Shift, LetteraEditVerb.ExtendPageDown },
            new object[] { LetteraKeyCode.Home, Ctrl, LetteraEditVerb.MoveDocStart },
            new object[] { LetteraKeyCode.End, Ctrl | Shift, LetteraEditVerb.ExtendDocEnd },
            new object[] { LetteraKeyCode.A, Ctrl, LetteraEditVerb.SelectAll },
            new object[] { LetteraKeyCode.Backspace, LetteraKeyModifiers.None, LetteraEditVerb.DeleteBack },
            new object[] { LetteraKeyCode.Backspace, Shift, LetteraEditVerb.DeleteBack },
            new object[] { LetteraKeyCode.Delete, LetteraKeyModifiers.None, LetteraEditVerb.DeleteForward },
            new object[] { LetteraKeyCode.Backspace, Ctrl, LetteraEditVerb.DeleteWordBack },
            new object[] { LetteraKeyCode.Delete, Ctrl, LetteraEditVerb.DeleteWordForward },
            new object[] { LetteraKeyCode.Backspace, Ctrl | Shift, LetteraEditVerb.DeleteToLineStart },
            new object[] { LetteraKeyCode.Delete, Ctrl | Shift, LetteraEditVerb.DeleteToLineEnd },
            new object[] { LetteraKeyCode.C, Ctrl, LetteraEditVerb.Copy },
            new object[] { LetteraKeyCode.Insert, Ctrl, LetteraEditVerb.Copy },
            new object[] { LetteraKeyCode.X, Ctrl, LetteraEditVerb.Cut },
            new object[] { LetteraKeyCode.Delete, Shift, LetteraEditVerb.Cut },
            new object[] { LetteraKeyCode.V, Ctrl, LetteraEditVerb.Paste },
            new object[] { LetteraKeyCode.Insert, Shift, LetteraEditVerb.Paste },
            new object[] { LetteraKeyCode.V, Ctrl | Shift, LetteraEditVerb.PastePlain },
            new object[] { LetteraKeyCode.Z, Ctrl, LetteraEditVerb.Undo },
            new object[] { LetteraKeyCode.Y, Ctrl, LetteraEditVerb.Redo },
            new object[] { LetteraKeyCode.Z, Ctrl | Shift, LetteraEditVerb.Redo },
            new object[] { LetteraKeyCode.Enter, LetteraKeyModifiers.None, LetteraEditVerb.InsertNewline },
            new object[] { LetteraKeyCode.Enter, Shift, LetteraEditVerb.InsertNewline },
            new object[] { LetteraKeyCode.KeypadEnter, LetteraKeyModifiers.None, LetteraEditVerb.InsertNewline },
            new object[] { LetteraKeyCode.Tab, LetteraKeyModifiers.None, LetteraEditVerb.Pass },
            new object[] { LetteraKeyCode.Escape, LetteraKeyModifiers.None, LetteraEditVerb.Cancel },
            new object[] { LetteraKeyCode.Menu, LetteraKeyModifiers.None, LetteraEditVerb.OpenMenu },
            new object[] { LetteraKeyCode.F10, Shift, LetteraEditVerb.OpenMenu },
            new object[] { LetteraKeyCode.A, Ctrl | Alt, LetteraEditVerb.None },
        };

        static readonly object[] s_AppleKeys =
        {
            new object[] { LetteraKeyCode.Left, LetteraKeyModifiers.None, LetteraEditVerb.MoveCharPrev },
            new object[] { LetteraKeyCode.B, Ctrl, LetteraEditVerb.MoveCharPrev },
            new object[] { LetteraKeyCode.F, Ctrl, LetteraEditVerb.MoveCharNext },
            new object[] { LetteraKeyCode.Left, Alt, LetteraEditVerb.MoveWordPrev },
            new object[] { LetteraKeyCode.Right, Alt | Shift, LetteraEditVerb.ExtendWordNext },
            new object[] { LetteraKeyCode.Left, Cmd, LetteraEditVerb.MoveLineStart },
            new object[] { LetteraKeyCode.Right, Cmd | Shift, LetteraEditVerb.ExtendLineEnd },
            new object[] { LetteraKeyCode.A, Ctrl, LetteraEditVerb.MoveLineStart },
            new object[] { LetteraKeyCode.E, Ctrl, LetteraEditVerb.MoveLineEnd },
            new object[] { LetteraKeyCode.Up, Alt, LetteraEditVerb.MoveParagraphStart },
            new object[] { LetteraKeyCode.P, Ctrl, LetteraEditVerb.MoveUp },
            new object[] { LetteraKeyCode.N, Ctrl, LetteraEditVerb.MoveDown },
            new object[] { LetteraKeyCode.Up, Cmd, LetteraEditVerb.MoveDocStart },
            new object[] { LetteraKeyCode.Down, Cmd | Shift, LetteraEditVerb.ExtendDocEnd },
            new object[] { LetteraKeyCode.Home, LetteraKeyModifiers.None, LetteraEditVerb.ScrollDocStart },
            new object[] { LetteraKeyCode.End, LetteraKeyModifiers.None, LetteraEditVerb.ScrollDocEnd },
            new object[] { LetteraKeyCode.A, Cmd, LetteraEditVerb.SelectAll },
            new object[] { LetteraKeyCode.H, Ctrl, LetteraEditVerb.DeleteBack },
            new object[] { LetteraKeyCode.D, Ctrl, LetteraEditVerb.DeleteForward },
            new object[] { LetteraKeyCode.Backspace, Alt, LetteraEditVerb.DeleteWordBack },
            new object[] { LetteraKeyCode.Delete, Alt, LetteraEditVerb.DeleteWordForward },
            new object[] { LetteraKeyCode.Backspace, Cmd, LetteraEditVerb.DeleteToLineStart },
            new object[] { LetteraKeyCode.Delete, Cmd, LetteraEditVerb.DeleteToLineEnd },
            new object[] { LetteraKeyCode.K, Ctrl, LetteraEditVerb.DeleteToParagraphEnd },
            new object[] { LetteraKeyCode.T, Ctrl, LetteraEditVerb.Transpose },
            new object[] { LetteraKeyCode.C, Cmd, LetteraEditVerb.Copy },
            new object[] { LetteraKeyCode.X, Cmd, LetteraEditVerb.Cut },
            new object[] { LetteraKeyCode.V, Cmd, LetteraEditVerb.Paste },
            new object[] { LetteraKeyCode.V, Cmd | Shift, LetteraEditVerb.PastePlain },
            new object[] { LetteraKeyCode.Z, Cmd, LetteraEditVerb.Undo },
            new object[] { LetteraKeyCode.Z, Cmd | Shift, LetteraEditVerb.Redo },
            new object[] { LetteraKeyCode.F10, Shift, LetteraEditVerb.None },
        };

        [TestCaseSource(nameof(s_PcKeys))]
        public void PcTable(LetteraKeyCode key, LetteraKeyModifiers modifiers, LetteraEditVerb verb) => Assert.AreEqual(verb, LetteraKeyTable.Resolve(key, modifiers, LetteraPlatformFamily.Pc));

        [TestCaseSource(nameof(s_AppleKeys))]
        public void AppleTable(LetteraKeyCode key, LetteraKeyModifiers modifiers, LetteraEditVerb verb) => Assert.AreEqual(verb, LetteraKeyTable.Resolve(key, modifiers, LetteraPlatformFamily.Apple));

        [Test]
        public void ReadOnlyTextOnlyRunsWhatItCan()
        {
            LetteraSelectable text = Reader("abcd");
            text.PlaceCaret(0);
            text.Input.Key(LetteraKeyCode.Right, Shift);
            Assert.AreEqual(1, text.Selection.Head);
            text.Input.Key(LetteraKeyCode.C, Ctrl);
            Assert.AreEqual("a", Clip.Text(LetteraClipFormat.PlainText));
            text.Input.Key(LetteraKeyCode.Delete, LetteraKeyModifiers.None);
            text.Input.Key(LetteraKeyCode.V, Ctrl);
            text.Input.Key(LetteraKeyCode.Z, Ctrl);
            Assert.AreEqual("abcd", text.GetComponent<LetteraWorldText>().Text);
            text.Input.Key(LetteraKeyCode.Right, LetteraKeyModifiers.None);
            Assert.AreEqual(1, text.Selection.Head, "no caret to move on read-only text");
        }

        [Test]
        public void WordKeysFollowThePlatform()
        {
            LetteraEditable field = Field("foo bar");
            field.Selectable.PlaceCaret(0);
            Press(field, LetteraKeyCode.Right, Ctrl);
            Assert.AreEqual(4, field.Selectable.Selection.Head);
            LetteraKeyTable.FamilyOverride = LetteraPlatformFamily.Apple;
            field.Selectable.PlaceCaret(0);
            Press(field, LetteraKeyCode.Right, Alt);
            Assert.AreEqual(3, field.Selectable.Selection.Head);
            Press(field, LetteraKeyCode.Left, Cmd);
            Assert.AreEqual(0, field.Selectable.Selection.Head);
            field.Selectable.PlaceCaret(5);
            Press(field, LetteraKeyCode.A, Ctrl);
            Assert.AreEqual(0, field.Selectable.Selection.Head);
            Assert.IsTrue(field.Selectable.Selection.IsCaret, "on Apple systems this goes to the line's start, it selects nothing");
        }

        [Test]
        public void HomeAndEndAreTheLinesAndSelectionReasonsTell()
        {
            LetteraEditable field = Field("aaaa bbbb", 70);
            var reasons = new List<string>();
            field.Selectable.SelectionChanged += c => reasons.Add(c.Reason);
            field.Selectable.PlaceCaret(7);
            Press(field, LetteraKeyCode.Home);
            Assert.AreEqual(5, field.Selectable.Selection.Head);
            Press(field, LetteraKeyCode.End);
            Assert.AreEqual(9, field.Selectable.Selection.Head);
            Press(field, LetteraKeyCode.Left, Shift);
            Press(field, LetteraKeyCode.A, Ctrl);
            Type(field, "z");
            CollectionAssert.AreEqual(new[] { "sel.set", "sel.key", "sel.key", "sel.key.extend", "sel.all", "sel.edit" }, reasons);
        }

        // ------------------------------------------------------------- pointer

        LetteraSelectable ClickTarget(string text, out LetteraWorldText label, out Func<int, Vector2> at, float width = 1000)
        {
            LetteraSelectable selectable = Reader(text, width);
            LetteraWorldText own = selectable.GetComponent<LetteraWorldText>();
            label = own;
            own.ForceRebuild();
            at = index =>
            {
                own.TryGetCaretRect(index, LetteraCaretBias.Forward, out Rect r);
                Vector3 world = own.transform.TransformPoint(new Vector3(r.x, r.center.y));
                return RectTransformUtility.WorldToScreenPoint(null, world);
            };
            return selectable;
        }

        [Test]
        public void ClicksCountUpToParagraphAndBack()
        {
            LetteraSelectable text = ClickTarget("one two three\nfour", out _, out Func<int, Vector2> at);
            Vector2 point = at(5);
            var data = Pointer(point + new Vector2(2, 0));
            text.OnPointerDown(data);
            text.OnPointerUp(data);
            Assert.IsTrue(text.Selection.IsCaret);
            Assert.AreEqual(5, text.Selection.Head);
            Now += 0.1;
            text.OnPointerDown(data);
            text.OnPointerUp(data);
            Assert.AreEqual((4, 7), (text.Selection.Start, text.Selection.End));
            Now += 0.1;
            text.OnPointerDown(data);
            text.OnPointerUp(data);
            Assert.AreEqual((0, 14), (text.Selection.Start, text.Selection.End));
            Now += 0.1;
            text.OnPointerDown(data);
            text.OnPointerUp(data);
            Assert.IsTrue(text.Selection.IsCaret, "a fourth click is a caret again");
        }

        [Test]
        public void ATwoClickDragExtendsByWholeWordsBothWays()
        {
            LetteraSelectable text = ClickTarget("one two three four", out _, out Func<int, Vector2> at);
            var data = Pointer(at(5) + new Vector2(2, 0));
            text.OnPointerDown(data);
            text.OnPointerUp(data);
            Now += 0.1;
            text.OnPointerDown(data);
            data.position = at(10) + new Vector2(2, 0);
            text.OnDrag(data);
            Assert.AreEqual((4, 13), (text.Selection.Start, text.Selection.End));
            data.position = at(1) + new Vector2(2, 0);
            text.OnDrag(data);
            Assert.AreEqual((0, 7), (text.Selection.Start, text.Selection.End), "the first word stays selected going back");
            text.OnPointerUp(data);
        }

        [Test]
        public void ShiftClickExtendsFromTheAnchor()
        {
            LetteraSelectable text = ClickTarget("one two three", out _, out Func<int, Vector2> at);
            var reasons = new List<string>();
            text.SelectionChanged += c => reasons.Add(c.Reason);
            var data = Pointer(at(2));
            text.OnPointerDown(data);
            text.OnPointerUp(data);
            Now += 2;
            LetteraSelectable.s_ShiftOverride = true;
            data.position = at(6);
            text.OnPointerDown(data);
            text.OnPointerUp(data);
            Assert.AreEqual((2, 6), (text.Selection.Anchor, text.Selection.Head));
            Assert.AreEqual("sel.pointer", reasons[0]);
            Assert.AreEqual("sel.pointer.extend", reasons[reasons.Count - 1]);
        }

        [Test]
        public void HidingStopsInputAndKeepsTheSelection()
        {
            LetteraSelectable text = ClickTarget("abc", out LetteraWorldText label, out Func<int, Vector2> at);
            text.Select(0, 2);
            label.Hide();
            var data = Pointer(at(1));
            text.OnPointerDown(data);
            Assert.AreEqual(2, text.Selection.Head);
        }

        [Test]
        public void SecondFingerCancelsWithoutAClick()
        {
            LetteraSelectable.ClassifyOverride = _ => LetteraPointerKind.Touch;
            LetteraEditable field = Field("one two three");
            LetteraSelectable text = field.Selectable;
            text.Select(0, 3);
            int changes = 0;
            text.SelectionChanged += _ => changes++;
            var first = Pointer(new Vector2(2, 0), id: 0);
            text.OnPointerDown(first);
            var second = Pointer(new Vector2(4, 0), id: 1);
            text.OnPointerDown(second);
            text.OnPointerUp(first);
            Assert.AreEqual(0, changes);
        }

        // ------------------------------------------------------------- caret

        [Test]
        public void TheCaretBlinksOnItsOwnClockAndStopsAfterTheTimeout()
        {
            LetteraEditable field = Field("");
            var seen = new HashSet<bool>();
            for (int i = 0; i < 40; i++)
            {
                Now += 0.1;
                field.Selectable.Tick();
                seen.Add(field.CaretVisibleNow);
            }
            Assert.AreEqual(2, seen.Count);
            Now += 100;
            for (int i = 0; i < 20; i++)
            {
                Now += 0.1;
                Assert.IsTrue(field.CaretVisibleNow);
            }
            Type(field, "x");
            Now += 0.6;
            Assert.IsFalse(field.CaretVisibleNow, "a key starts the blink again");
        }

        [Test]
        public void SteadyCaretsDoNotBlink()
        {
            LetteraEditable field = Field("");
            LetteraCaret.PrefersSteady = true;
            for (int i = 0; i < 20; i++)
            {
                Now += 0.1;
                Assert.IsTrue(field.CaretVisibleNow);
            }
        }

        [Test]
        public void AnIdleFieldRebuildsNothing()
        {
            LetteraEditable field = Field("Some text");
            LetteraWorldText label = field.GetComponent<LetteraWorldText>();
            field.Selectable.Tick();
            label.ForceRebuild();
            int rebuilds = label.RebuildCount;
            using var recorder = Unity.Profiling.ProfilerRecorder.StartNew(Unity.Profiling.ProfilerCategory.Memory, "GC Allocated In Frame");
            long quietest = long.MaxValue;
            for (int window = 0; window < 5; window++)
            {
                long before = recorder.CurrentValue;
                for (int i = 0; i < 120; i++) field.Selectable.Tick();
                quietest = Math.Min(quietest, recorder.CurrentValue - before);
            }
            Assert.AreEqual(rebuilds, label.RebuildCount);
            Assert.Less(quietest, 200);
        }

        [Test]
        public void StateComesBackWithoutHistory()
        {
            LetteraEditable field = Field("abc");
            field.Selectable.Select(1, 2);
            LetteraEditable.State state = field.CaptureState();
            Type(field, "xyz");
            int depth = field.UndoDepth;
            field.RestoreState(state);
            Assert.AreEqual("abc", field.Source);
            Assert.AreEqual((1, 2), (field.Selectable.Selection.Anchor, field.Selectable.Selection.Head));
            Assert.AreEqual(depth, field.UndoDepth);
        }

        // ------------------------------------------------------------- bad input

        [Test]
        public void RandomEditsNeverBreakTheField()
        {
            string failure = null;
            void OnLog(string message, string stack, LogType type)
            {
                if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) failure = failure ?? message;
            }
            LogAssert.ignoreFailingMessages = true;
            Application.logMessageReceived += OnLog;
            var random = new System.Random(4242);
            string[] pieces = { "a", "b ", "<b>", "</b>", "<i>", "</i>", "fi", "א", "م", Family, "\n", " ", "é", "\\", "<", ">", "<br>", "<noparse>", "</noparse>", "<divider>", "<u>" };
            LetteraEditable field = Field("", 300);
            LetteraSelectable s = field.Selectable;
            for (int round = 0; round < 3000; round++)
            {
                try
                {
                    Now += random.NextDouble();
                    switch (random.Next(12))
                    {
                        case 0: case 1: case 2: case 3: field.InsertText(pieces[random.Next(pieces.Length)], "user.type"); break;
                        case 4: field.DeleteBack(); break;
                        case 5: field.DeleteForward(); break;
                        case 6: s.Select(random.Next(-2, field.Length + 3), random.Next(-2, field.Length + 3)); break;
                        case 7: s.PlaceCaret(random.Next(0, field.Length + 1)); break;
                        case 8: field.Undo(); break;
                        case 9: field.Redo(); break;
                        case 10: field.DeleteWordBack(); break;
                        default:
                            field.Selectable.Input.Key((LetteraKeyCode)random.Next(1, 19), (LetteraKeyModifiers)random.Next(0, 8));
                            break;
                    }
                    s.Tick();
                }
                catch (Exception e)
                {
                    Assert.Fail($"round {round} on '{field.Source}' threw {e}");
                }
                Assert.LessOrEqual(s.Selection.End, field.Length, field.Source);
                Assert.GreaterOrEqual(s.Selection.Start, 0);
            }
            Application.logMessageReceived -= OnLog;
            LogAssert.ignoreFailingMessages = false;
            Assert.IsNull(failure, failure);
        }
    }
}

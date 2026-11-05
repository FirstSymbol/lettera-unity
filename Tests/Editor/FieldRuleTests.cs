using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace FirstSymbol.Lettera.Editor.Tests
{
    /// <summary>Rules, filters, validators, decorators, formatting and composition.</summary>
    public class FieldRuleTests : EditingFixture
    {
        const string Family = "\U0001F468‍\U0001F469‍\U0001F467";

        [Serializable]
        class Rewrite : LetteraFieldRule
        {
            public string From, To;
            public override void FilterEdit(ref LetteraEditProposal edit)
            {
                if (edit.Text == From) edit.Text = To;
            }
        }

        [Serializable]
        class Refuse : LetteraFieldRule
        {
            public bool SawRejected;
            public override void FilterEdit(ref LetteraEditProposal edit)
            {
                if (edit.Text == "n") edit.Reject();
            }
        }

        [Serializable]
        class Watcher : LetteraFieldRule
        {
            public bool SawRejected;
            public override void FilterEdit(ref LetteraEditProposal edit) => SawRejected |= edit.Rejected;
        }

        LetteraEditable With(string text, params LetteraFieldRule[] rules)
        {
            LetteraEditable field = Field(text);
            foreach (LetteraFieldRule rule in rules) field.AddRule(rule);
            return field;
        }

        LetteraText Label()
        {
            var go = new GameObject("Label", typeof(RectTransform));
            go.transform.SetParent(CanvasRoot, false);
            return go.AddComponent<LetteraText>();
        }

        Transform m_CanvasRoot;

        Transform CanvasRoot
        {
            get
            {
                if (m_CanvasRoot != null) return m_CanvasRoot;
                var go = new GameObject("Canvas", typeof(Canvas));
                m_ToDestroy.Add(go);
                m_CanvasRoot = go.transform;
                return m_CanvasRoot;
            }
        }

        readonly List<GameObject> m_ToDestroy = new List<GameObject>();

        [TearDown]
        public void Clean()
        {
            foreach (GameObject go in m_ToDestroy) UnityEngine.Object.DestroyImmediate(go);
            m_ToDestroy.Clear();
            m_CanvasRoot = null;
        }

        // ------------------------------------------------------------- pipeline

        [Test]
        public void RulesRunInListOrder()
        {
            LetteraEditable a = With("", new Rewrite { From = "a", To = "b" }, new Rewrite { From = "b", To = "c" });
            Type(a, "a");
            Assert.AreEqual("c", a.Source);
            LetteraEditable b = With("", new Rewrite { From = "b", To = "c" }, new Rewrite { From = "a", To = "b" });
            Type(b, "a");
            Assert.AreEqual("b", b.Source);
        }

        [Test]
        public void ARejectionSticksAndLeavesNoTrace()
        {
            var after = new Watcher();
            LetteraEditable field = With("", new Refuse(), after);
            int values = 0, rejected = 0;
            field.ValueChanged += _ => values++;
            field.EditRejected += _ => rejected++;
            Type(field, "n");
            Assert.IsTrue(after.SawRejected);
            Assert.AreEqual(0, values);
            Assert.AreEqual(1, rejected);
            Assert.IsFalse(field.CanUndo);
        }

        [Test]
        public void TurningRulesOnAndOffRestoresTheField()
        {
            LetteraEditable field = Field("<b>x</b>");
            Assert.AreEqual("x", field.ShownText);
            var password = new PasswordRule();
            for (int i = 0; i < 100; i++)
            {
                field.AddRule(password);
                field.RemoveRule(password);
            }
            Assert.AreEqual("x", field.ShownText);
            Assert.AreEqual("<b>x</b>", field.Source);
            field.AddRule(password);
            Assert.AreEqual(8, field.Length, "the markup is literal under a password");
            password.Enabled = false;
            Assert.AreEqual(1, field.Length);
            password.Enabled = true;
            Assert.AreEqual(8, field.Length);
        }

        [Test]
        public void APresetIsCopiedAndItsChangesReachLiveFields()
        {
            var preset = ScriptableObject.CreateInstance<LetteraFieldPreset>();
            preset.Rules.Add(new MaxLengthRule { Limit = 2 });
            LetteraEditable one = Field(""), two = Field("");
            one.AddPreset(preset);
            two.AddPreset(preset);
            one.GetRule<MaxLengthRule>().Limit = 5;
            Assert.AreEqual(2, ((MaxLengthRule)preset.Rules[0]).Limit);
            Assert.AreEqual(2, two.GetRule<MaxLengthRule>().Limit);
            ((MaxLengthRule)preset.Rules[0]).Limit = 3;
            preset.NotifyChanged();
            Assert.AreEqual(3, two.GetRule<MaxLengthRule>().Limit);
            UnityEngine.Object.DestroyImmediate(preset);
        }

        // ------------------------------------------------------------- single line, password

        [Test]
        public void SingleLineJoinsBreaksAndSubmits()
        {
            LetteraEditable field = With("", new SingleLineRule());
            Clip.Write(new[] { new LetteraClipItem(LetteraClipFormat.PlainText, "a\n\nb") });
            field.Paste();
            Assert.AreEqual("a b", field.PlainText);
            int submits = 0;
            LetteraBlurReason? blur = null;
            field.Submitted += _ => submits++;
            field.FocusLost += r => blur = r;
            Press(field, LetteraKeyCode.Enter);
            Assert.AreEqual(1, submits);
            Assert.AreEqual(LetteraBlurReason.Submit, blur);
            Assert.AreEqual("a b", field.PlainText);
        }

        [Test]
        public void SingleLineCanStayFocused()
        {
            LetteraEditable field = With("", new SingleLineRule { StayFocusedOnSubmit = true });
            Press(field, LetteraKeyCode.Enter);
            Assert.IsTrue(field.Selectable.HasFocus);
        }

        [Test]
        public void HoldingEnterSubmitsOnce()
        {
            LetteraEditable field = With("", new SingleLineRule { StayFocusedOnSubmit = true });
            int submits = 0;
            field.Submitted += _ => submits++;
            field.Selectable.Input.Key(LetteraKeyCode.Enter, LetteraKeyModifiers.None);
            field.Selectable.Input.Key(LetteraKeyCode.Enter, LetteraKeyModifiers.None, true);
            field.Selectable.Input.Key(LetteraKeyCode.Enter, LetteraKeyModifiers.None, true);
            Assert.AreEqual(1, submits);
        }

        [Test]
        public void APasswordMasksByClusterAndHoldsAPlainValue()
        {
            var password = new PasswordRule();
            LetteraEditable field = With("", password);
            Type(field, "ab<b>");
            Assert.AreEqual("ab<b>", field.Source);
            Assert.AreEqual("ab<b>", field.PlainText);
            Assert.AreEqual(new string('•', 5), field.ShownText);
            Assert.AreEqual(5, field.Selectable.Selection.Head);
            LetteraWorldText label = field.GetComponent<LetteraWorldText>();
            label.ForceRebuild();
            int parses = label.ParseCount;
            password.ShowValue = true;
            label.ForceRebuild();
            Assert.AreEqual("ab<b>", field.ShownText);
            Assert.AreEqual(5, field.Selectable.Selection.Head);
            Assert.AreEqual(parses, label.ParseCount);
            password.ShowValue = false;

            field.SetSource(Family);
            Assert.AreEqual("•", field.ShownText);
        }

        [Test]
        public void APasswordKeepsItselfOffTheClipboard()
        {
            var password = new PasswordRule();
            LetteraEditable field = With("secret", password);
            field.Selectable.SelectAll();
            Assert.IsFalse(field.Selectable.Copy());
            Assert.IsFalse(field.Cut());
            Assert.AreEqual(0, Clip.Writes);
            password.CopyAllowed = true;
            Assert.IsTrue(field.Selectable.Copy());
            Assert.AreEqual(1, Clip.Writes);
        }

        [Test]
        public void APasswordFieldForgetsItsHistoryOnBlur()
        {
            LetteraEditable field = With("", new PasswordRule());
            Type(field, "abc");
            Assert.IsTrue(field.CanUndo);
            field.Selectable.Deactivate();
            Assert.IsFalse(field.CanUndo);
        }

        [Test]
        public void ADoubleClickOnAPasswordRevealsNoWords()
        {
            LetteraEditable field = With("ab cd", new PasswordRule());
            field.Selectable.SelectWordAt(1);
            Assert.AreEqual(5, field.Selectable.Selection.Length);
        }

        [Test]
        public void ACompositionShowsUnmasked()
        {
            LetteraEditable field = With("ab", new PasswordRule());
            field.Selectable.Input.Composition("x", 1, null);
            Assert.AreEqual("••x", field.ShownText);
            field.Selectable.Input.CompositionEnd(true);
            Assert.AreEqual("•••", field.ShownText);
            Assert.AreEqual("abx", field.Source);
        }

        // ------------------------------------------------------------- max length

        [Test]
        public void TheLimitNeverCutsACluster()
        {
            LetteraEditable field = With("", new MaxLengthRule { Limit = 3, Unit = LetteraCountUnit.Graphemes, CountIn = LetteraCountSpace.Plain });
            field.InsertText(Family + Family);
            field.InsertText("abc");
            Assert.AreEqual(Family + Family + "a", field.Source);
        }

        [Test]
        public void TheLimitCountsBytesToo()
        {
            LetteraEditable field = With("", new MaxLengthRule { Limit = 3, Unit = LetteraCountUnit.Utf8, CountIn = LetteraCountSpace.Plain });
            field.InsertText("aé");
            Assert.AreEqual("aé", field.Source);
            field.InsertText("€");
            Assert.AreEqual("aé", field.Source);
        }

        [Test]
        public void AtTheLimitAReplacementFitsAndBackspaceWorks()
        {
            LetteraEditable field = With("abc", new MaxLengthRule { Limit = 3 });
            Type(field, "d");
            Assert.AreEqual("abc", field.Source);
            field.Selectable.Select(1, 2);
            Type(field, "x");
            Assert.AreEqual("axc", field.Source);
            Press(field, LetteraKeyCode.Backspace);
            Assert.AreEqual("ac", field.Source);
        }

        [Test]
        public void ASourceLimitCountsMarkup()
        {
            LetteraEditable field = With("<b>a</b>", new MaxLengthRule { Limit = 9, CountIn = LetteraCountSpace.Source });
            field.InsertText("xy");
            Assert.AreEqual("<b>ax</b>", field.Source);
        }

        // ------------------------------------------------------------- pattern

        [TestCase("1234567890", "(123) 456-7890")]
        [TestCase("1", "(1")]
        [TestCase("1234", "(123) 4")]
        [TestCase("a1b2", "(12")]
        [TestCase("12345678901234", "(123) 456-7890")]
        public void APatternFormats(string typed, string shown)
        {
            var mask = new PatternMaskRule { Pattern = "(###) ###-####" };
            LetteraEditable field = With("", mask);
            Clip.Write(new[] { new LetteraClipItem(LetteraClipFormat.PlainText, typed) });
            field.Paste();
            Assert.AreEqual(shown, field.PlainText);
            Assert.AreEqual(field.PlainText.Length, field.Selectable.Selection.Head);
        }

        [Test]
        public void APatternDeletesThroughItsLiterals()
        {
            var mask = new PatternMaskRule { Pattern = "(###) ###-####" };
            LetteraEditable field = With("", mask);
            Type(field, "1234");
            Assert.AreEqual("(123) 4", field.PlainText);
            Assert.AreEqual("1234", mask.RawValue);
            Press(field, LetteraKeyCode.Backspace);
            Assert.AreEqual("(123", field.PlainText);
            Press(field, LetteraKeyCode.Backspace);
            Assert.AreEqual("(12", field.PlainText);
        }

        [Test]
        public void APatternReflowsAMiddleEditAndUndoesAsOneStep()
        {
            var mask = new PatternMaskRule { Pattern = "(###) ###-####" };
            LetteraEditable field = With("", mask);
            Type(field, "123456");
            Assert.AreEqual("(123) 456", field.PlainText);
            field.Selectable.PlaceCaret(2);
            Now += 1;
            Type(field, "9");
            Assert.AreEqual("(192) 345-6", field.PlainText);
            Assert.AreEqual(3, field.Selectable.Selection.Head);
            field.Undo();
            Assert.AreEqual("(123) 456", field.PlainText);
            Assert.AreEqual(2, field.Selectable.Selection.Head);
        }

        [Test]
        public void SetRawValueFillsThePattern()
        {
            var mask = new PatternMaskRule { Pattern = "##/##" };
            LetteraEditable field = With("", mask);
            mask.SetRawValue("1231");
            Assert.AreEqual("12/31", field.PlainText);
            Assert.AreEqual("1231", mask.RawValue);
        }

        // ------------------------------------------------------------- case, keys

        [Test]
        public void CaseRulesUseSimpleMappings()
        {
            LetteraEditable upper = With("", new LetterCaseRule { Mode = LetteraLetterCase.Upper });
            Type(upper, "straße");
            Assert.AreEqual("STRAßE", upper.Source);
            LetteraEditable title = With("", new LetterCaseRule { Mode = LetteraLetterCase.Title });
            Type(title, "hello wORLD");
            Assert.AreEqual("Hello WORLD", title.Source);
        }

        [Test]
        public void EnterKeyRuleChoosesWhichEnterSubmits()
        {
            LetteraEditable enter = With("", new EnterKeyRule { Key = LetteraSubmitKey.Enter, KeepFocus = true });
            int submits = 0;
            enter.Submitted += _ => submits++;
            Press(enter, LetteraKeyCode.Enter, Shift);
            Assert.AreEqual("\n", enter.Source);
            Press(enter, LetteraKeyCode.Enter);
            Assert.AreEqual(1, submits);
            Assert.IsTrue(enter.Selectable.HasFocus);

            LetteraEditable primary = With("", new EnterKeyRule { Key = LetteraSubmitKey.PrimaryEnter });
            int primarySubmits = 0;
            primary.Submitted += _ => primarySubmits++;
            Press(primary, LetteraKeyCode.Enter);
            Assert.AreEqual("\n", primary.Source);
            Assert.AreEqual(0, primarySubmits);
            Press(primary, LetteraKeyCode.Enter, Ctrl);
            Assert.AreEqual(1, primarySubmits);
        }

        [Test]
        public void TabRuleMovesFocusOrInserts()
        {
            LetteraEditable second = Field("");
            var rule = new TabKeyRule { Mode = LetteraTabMode.MoveFocus, NextField = second.gameObject };
            LetteraEditable first = With("", rule);
            LetteraBlurReason? reason = null;
            first.FocusLost += r => reason = r;
            Press(first, LetteraKeyCode.Tab);
            Assert.AreEqual(LetteraBlurReason.Tab, reason);
            Assert.AreSame(second.Selectable, LetteraSelectable.Current);

            LetteraEditable spaces = With("", new TabKeyRule { Mode = LetteraTabMode.InsertSpaces, Spaces = 2 });
            Press(spaces, LetteraKeyCode.Tab);
            Assert.AreEqual("  ", spaces.Source);

            LetteraEditable none = Field("");
            Assert.IsFalse(none.Selectable.HandleKey(LetteraKeyCode.Tab, LetteraKeyModifiers.None, false), "with no rule the key goes on");
        }

        [Test]
        public void CancelRuleCanBringBackTheText()
        {
            LetteraEditable field = Field("abc");
            field.AddRule(new ReleaseOnCancelRule { RestoreOnCancel = true });
            field.Selectable.Deactivate();
            field.Selectable.Activate();
            field.Selectable.PlaceCaret(3);
            Type(field, "xyz");
            int cancelled = 0;
            field.Cancelled += () => cancelled++;
            Press(field, LetteraKeyCode.Escape);
            Assert.AreEqual("abc", field.Source);
            Assert.AreEqual(1, cancelled);
            Assert.IsFalse(field.Selectable.HasFocus);
        }

        [Test]
        public void SelectAllOnFocusSelects()
        {
            LetteraEditable field = With("abc", new SelectAllOnFocusRule());
            field.Selectable.Deactivate();
            field.Selectable.Activate();
            Assert.AreEqual(3, field.Selectable.Selection.Length);
        }

        // ------------------------------------------------------------- filters

        [Test]
        public void ANumberFilterJudgesTheResult()
        {
            LetteraEditable field = With("", new WholeNumberCharFilter());
            Type(field, "12a");
            Assert.AreEqual("12", field.Source);
            Clip.Write(new[] { new LetteraClipItem(LetteraClipFormat.PlainText, "3a") });
            field.Paste();
            Assert.AreEqual("12", field.Source);
            field.GetRule<WholeNumberCharFilter>().PasteMode = LetteraPasteFilterMode.KeepLegal;
            field.Paste();
            Assert.AreEqual("123", field.Source);
        }

        [Test]
        public void ReplacingTheBadCharacterIsLegalAndDeletingAlwaysIs()
        {
            LetteraEditable field = Field("1a2");
            field.AddRule(new WholeNumberCharFilter());
            field.Selectable.Select(1, 2);
            Type(field, "3");
            Assert.AreEqual("132", field.Source);
            field.SetSource("1a2");
            field.Selectable.PlaceCaret(1);
            Press(field, LetteraKeyCode.Delete);
            Assert.AreEqual("12", field.Source);
        }

        [Test]
        public void NegativesAndDecimalsAllowWhatTypingPassesThrough()
        {
            LetteraEditable whole = With("", new WholeNumberCharFilter { NegativeAllowed = true });
            Type(whole, "-5-");
            Assert.AreEqual("-5", whole.Source);
            LetteraEditable real = With("", new DecimalCharFilter());
            Type(real, "1.5.");
            Assert.AreEqual("1.5", real.Source);
            Type(real, ",");
            Assert.AreEqual("1.5", real.Source);
            LetteraEditable comma = With("", new DecimalCharFilter { Separators = "," });
            Type(comma, "1,5");
            Assert.AreEqual("1,5", comma.Source);
        }

        [Test]
        public void EmailAndNameFiltersCheckCharacters()
        {
            LetteraEditable mail = With("", new EmailCharFilter());
            Type(mail, "a@b.c@");
            Assert.AreEqual("a@b.c", mail.Source);
            LetteraEditable name = With("", new PersonNameCharFilter());
            name.InsertText("O’Brien");
            Assert.AreEqual("O’Brien", name.Source);
            name.SetSource("");
            name.InsertText("Анна-Мария");
            Assert.AreEqual(10, name.Length);
            name.SetSource("");
            name.InsertText("R2D2");
            Assert.AreEqual("", name.Source);
        }

        [Test]
        public void AFilterSeesACompositionOnlyAtItsCommit()
        {
            LetteraEditable field = With("", new WholeNumberCharFilter());
            field.Selectable.Input.Composition("日", 1, null);
            Assert.AreEqual("日", field.ShownText);
            field.Selectable.Input.CompositionEnd(true);
            Assert.AreEqual("", field.Source);
            Assert.AreEqual("", field.ShownText);
        }

        // ------------------------------------------------------------- validators

        [Serializable]
        class NotEmpty : LetteraValueCheck
        {
            public static int Runs;
            public override LetteraCheckResult Check(ILetteraTextView value)
            {
                Runs++;
                return value.Length == 0 ? new LetteraCheckResult(LetteraCheckResult.Invalid, "Needed") : new LetteraCheckResult(null);
            }
        }

        [Test]
        public void AValidatorRunsWhenItIsToldTo()
        {
            NotEmpty.Runs = 0;
            var rule = new ValidationRule { When = LetteraCheckTime.OnBlur };
            rule.Checks.Add(new NotEmpty());
            LetteraEditable field = With("", rule);
            int changes = 0;
            field.CheckChanged += _ => changes++;
            Type(field, "a");
            Press(field, LetteraKeyCode.Backspace);
            Assert.AreEqual(0, NotEmpty.Runs);
            field.Selectable.Deactivate();
            Assert.AreEqual(1, NotEmpty.Runs);
            Assert.AreEqual(LetteraCheckResult.Invalid, field.LastCheck.Status);
            Assert.AreEqual("Needed", field.LastCheck.Message);
            Assert.AreEqual(1, changes);

            rule.When = LetteraCheckTime.OnChange;
            field.Selectable.Activate();
            Type(field, "a");
            Assert.IsTrue(field.LastCheck.IsValid);
            Assert.AreEqual(2, changes);
        }

        [Test]
        public void ALateResultForOldTextIsDropped()
        {
            LetteraEditable field = Field("a");
            int version = field.Version;
            Type(field, "b");
            field.PublishCheck(version, new LetteraCheckResult(LetteraCheckResult.Invalid));
            Assert.IsTrue(field.LastCheck.IsValid);
            field.PublishCheck(field.Version, new LetteraCheckResult(LetteraCheckResult.Invalid));
            Assert.IsFalse(field.LastCheck.IsValid);
        }

        // ------------------------------------------------------------- decorators

        [Test]
        public void AHintFollowsEmptiness()
        {
            LetteraText hint = Label();
            LetteraEditable field = With("", new HintDecorator { Target = hint });
            Assert.IsTrue(hint.IsVisible);
            field.Selectable.Input.Composition("a", 1, null);
            Assert.IsFalse(hint.IsVisible);
            field.Selectable.Input.CompositionEnd(false);
            Assert.IsTrue(hint.IsVisible);
            Assert.IsTrue(hint.gameObject.activeSelf);
            Type(field, "a");
            Assert.IsFalse(hint.IsVisible);
        }

        [Test]
        public void ALabelRaisesAndSnapsWhenMotionIsReduced()
        {
            LetteraText label = Label();
            var rule = new RaisedLabelDecorator { Label = label.rectTransform, Seconds = 0.2f };
            LetteraEditable field = With("", rule);
            field.Selectable.Deactivate();
            field.Selectable.Tick();
            Now += 1;
            field.Selectable.Tick();
            Assert.AreEqual(0, rule.Progress, 0.001f);
            field.Selectable.Activate();
            Now += 0.1;
            field.Selectable.Tick();
            float middle = rule.Progress;
            Assert.Greater(middle, 0.2f);
            Assert.Less(middle, 0.8f);
            field.Selectable.Deactivate();
            Now += 0.01;
            field.Selectable.Tick();
            Assert.AreEqual(middle, rule.Progress, 0.1f, "interrupting continues from where it was");
            Now += 1;
            field.Selectable.Tick();
            Assert.AreEqual(0, rule.Progress, 0.001f);
            LetteraAccessibility.ReducedMotion = true;
            field.Selectable.Activate();
            Assert.AreEqual(1, rule.Progress, 0.001f);
        }

        [Test]
        public void HelperShowsTheCheck()
        {
            LetteraText helper = Label();
            var rule = new ValidationRule { When = LetteraCheckTime.OnChange };
            rule.Checks.Add(new NotEmpty());
            LetteraEditable field = With("x", rule, new HelperTextDecorator { Target = helper, HelperText = "Name" });
            Assert.AreEqual("Name", helper.Text);
            Press(field, LetteraKeyCode.Backspace);
            Assert.AreEqual("Needed", helper.Text);
            Type(field, "a");
            Assert.AreEqual("Name", helper.Text);
            field.SetCheck(new LetteraCheckResult(LetteraCheckResult.Pending, "Wait"));
            Assert.AreEqual("Name", helper.Text);
        }

        [Test]
        public void ACounterAgreesWithTheLimit()
        {
            LetteraText counter = Label();
            LetteraEditable field = With("", new MaxLengthRule { Limit = 5, Unit = LetteraCountUnit.Graphemes, CountIn = LetteraCountSpace.Plain }, new LengthCounterDecorator { Target = counter, CapColor = Color.red });
            field.InsertText(Family + Family);
            Assert.AreEqual("2/5", counter.Text);
            field.InsertText("abc");
            Assert.AreEqual("5/5", counter.Text);
            Assert.AreEqual(Color.red, counter.color);

            LetteraText other = Label();
            LetteraEditable source = With("<b>a</b>", new MaxLengthRule { Limit = 20, Unit = LetteraCountUnit.Codepoints, CountIn = LetteraCountSpace.Source }, new LengthCounterDecorator { Target = other });
            Assert.AreEqual("8/20", other.Text);
        }

        // ------------------------------------------------------------- markup views

        [Test]
        public void HiddenMarkupHasNoStops()
        {
            LetteraEditable field = Field("a<b>b</b>c");
            Assert.AreEqual(3, field.Length);
            field.Selectable.PlaceCaret(0);
            var stops = new List<int> { 0 };
            for (int i = 0; i < 3; i++)
            {
                Press(field, LetteraKeyCode.Right);
                stops.Add(field.Selectable.Selection.Head);
            }
            CollectionAssert.AreEqual(new[] { 0, 1, 2, 3 }, stops);
        }

        [Test]
        public void SourceViewShowsEverythingAndKeepsTheSelection()
        {
            LetteraEditable field = Field("a<b>b</b>c");
            field.Selectable.Select(1, 2);
            field.MarkupView = LetteraMarkupView.Source;
            Assert.AreEqual(10, field.Length);
            Assert.AreEqual("a<b>b</b>c", field.PlainText);
            Assert.AreEqual("b", field.Selectable.SelectedPlainText());
            field.MarkupView = LetteraMarkupView.Hidden;
            Assert.AreEqual("b", field.Selectable.SelectedPlainText());
        }

        [Test]
        public void RevealShowsTagsAroundTheCaret()
        {
            LetteraEditable field = Field("a<b>b</b>c");
            field.MarkupView = LetteraMarkupView.RevealAtCaret;
            field.Selectable.PlaceCaret(2);
            Assert.AreEqual("a<b>b</b>c", field.ShownText);
            field.Selectable.PlaceCaret(0);
            Assert.AreEqual("abc", field.ShownText);
            field.MarkupView = LetteraMarkupView.Hidden;
        }

        [Test]
        public void TypedMarkupBecomesMarkupOrStaysText()
        {
            LetteraEditable markup = Field("");
            Type(markup, "<b>x</b>");
            Assert.AreEqual("x", markup.PlainText);
            Assert.AreEqual(1, markup.Selectable.Selection.Head);

            LetteraEditable text = Field("");
            text.TypedMarkup = LetteraTypedMarkup.AsText;
            Type(text, "<b>x</b>");
            Assert.AreEqual("<b>x</b>", text.PlainText);
            Assert.AreEqual(8, text.Selectable.Selection.Head);
        }

        [Test]
        public void PastePoliciesDecideAboutMarkup()
        {
            LetteraEditable field = Field("");
            Clip.Write(new[] { new LetteraClipItem(LetteraClipFormat.PlainText, "<b>x</b>") });
            field.Paste();
            Assert.AreEqual("<b>x</b>", field.PlainText, "literal by default in the hidden view");
            field.SetSource("");
            field.PlainPaste = LetteraPlainPaste.AsMarkup;
            field.Paste();
            Assert.AreEqual("x", field.PlainText);
            field.SetSource("");
            field.PlainPaste = LetteraPlainPaste.AsText;
            field.Paste();
            Assert.AreEqual("<b>x</b>", field.PlainText);
        }

        // ------------------------------------------------------------- formatting

        [Test]
        public void BoldWritesAndMergesAndSplits()
        {
            LetteraEditable field = Field("abcd");
            field.Selectable.Select(1, 3);
            Assert.IsTrue(field.ToggleEffect<BoldEffect>());
            Assert.AreEqual("a<b>bc</b>d", field.Source);
            field.Selectable.Select(0, 4);
            field.ApplyEffect<BoldEffect>();
            Assert.AreEqual("<b>abcd</b>", field.Source);
            field.Selectable.Select(2, 3);
            field.RemoveEffect<BoldEffect>();
            Assert.AreEqual("<b>ab</b>c<b>d</b>", field.Source);
            field.Undo();
            Assert.AreEqual("<b>abcd</b>", field.Source);
            Assert.AreEqual((2, 3), (field.Selectable.Selection.Start, field.Selectable.Selection.End));
        }

        [Test]
        public void ACommandUsesTheFieldsOwnSyntaxAndNeverAddsAStyle()
        {
            LetteraEditable field = Field("abc");
            LetteraWorldText label = field.GetComponent<LetteraWorldText>();
            label.UseGlobalStylePreset = false;
            label.Styles.Add(LetteraStyle.ForMatcher(new MarkdownMarkerMatcher("**"), new BoldEffect()));
            label.MarkStylesDirty();
            field.Selectable.SelectAll();
            Assert.IsTrue(field.ToggleEffect<BoldEffect>());
            Assert.AreEqual("**abc**", field.Source);
            Assert.IsFalse(field.ToggleEffect<ItalicEffect>());
            Assert.AreEqual("**abc**", field.Source);
        }

        [Test]
        public void CrossingRangesRenderBoth()
        {
            LetteraEditable field = Field("abcd");
            field.Selectable.Select(0, 2);
            field.ApplyEffect<BoldEffect>();
            field.Selectable.Select(1, 3);
            field.ApplyEffect<ItalicEffect>();
            Assert.AreEqual("abcd", field.PlainText);
            field.Selectable.Select(1, 2);
            Assert.IsTrue(field.IsEffectActive<BoldEffect>());
            Assert.IsTrue(field.IsEffectActive<ItalicEffect>());
        }

        [Test]
        public void AtACaretACommandWaitsForTheNextText()
        {
            LetteraEditable field = Field("a");
            field.ToggleEffect<BoldEffect>();
            Assert.IsTrue(field.IsEffectActive<BoldEffect>());
            Type(field, "x");
            Assert.AreEqual("a<b>x</b>", field.Source);
            Assert.AreEqual(2, field.Selectable.Selection.Head);

            LetteraEditable moved = Field("ab");
            moved.ToggleEffect<BoldEffect>();
            moved.Selectable.PlaceCaret(1);
            Assert.IsFalse(moved.IsEffectActive<BoldEffect>());
            Type(moved, "x");
            Assert.AreEqual("axb", moved.Source);
        }

        [Test]
        public void TypingInheritsExceptAtALinksEnd()
        {
            LetteraEditable bold = Field("<b>a</b>");
            Type(bold, "x");
            Assert.AreEqual("<b>ax</b>", bold.Source);
            LetteraEditable link = Field("<a=https://a.io>x</a>");
            Type(link, "y");
            Assert.AreEqual("<a=https://a.io>x</a>y", link.Source);
        }

        [Test]
        public void ClearStylesTakesEverythingWritable()
        {
            LetteraEditable field = Field("<b><i>abc</i></b>");
            field.Selectable.SelectAll();
            field.ClearStyles();
            Assert.AreEqual("abc", field.Source);
            field.Undo();
            Assert.AreEqual("<b><i>abc</i></b>", field.Source);
        }

        [Test]
        public void ParameterReadsBackOnAUniformSelection()
        {
            LetteraEditable field = Field("<color=#F00>ab</color><color=#0F0>cd</color>");
            field.Selectable.Select(0, 2);
            Assert.IsTrue(field.TryGetEffectParameter<ColorEffect>(out string value));
            Assert.AreEqual("#F00", value);
            field.Selectable.Select(0, 4);
            Assert.IsFalse(field.TryGetEffectParameter<ColorEffect>(out _));
        }

        [Test]
        public void AColorOverPartOfAnotherSplits()
        {
            LetteraEditable field = Field("<color=#F00>abc</color>");
            field.Selectable.Select(1, 2);
            field.ApplyEffect<ColorEffect>("#0F0");
            Assert.AreEqual("abc", field.PlainText);
            field.Selectable.Select(0, 1);
            field.TryGetEffectParameter<ColorEffect>(out string first);
            field.Selectable.Select(1, 2);
            field.TryGetEffectParameter<ColorEffect>(out string second);
            field.Selectable.Select(2, 3);
            field.TryGetEffectParameter<ColorEffect>(out string third);
            Assert.AreEqual(("#F00", "#0F0", "#F00"), (first, second, third));
        }

        [Test]
        public void ShortcutsRunWhereTheyApplyAndPassOtherwise()
        {
            LetteraEditable field = With("abc", new FormattingRule());
            field.Selectable.SelectAll();
            Press(field, LetteraKeyCode.B, Ctrl);
            Assert.AreEqual("<b>abc</b>", field.Source);
            Press(field, LetteraKeyCode.Backslash, Ctrl);
            Assert.AreEqual("abc", field.Source);
        }

        [Test]
        public void AnArrowLeavesAFormatBeforeItMoves()
        {
            LetteraEditable field = With("<b>a</b>c", new FormattingRule());
            field.Selectable.PlaceCaret(1);
            Assert.IsTrue(field.IsEffectActive<BoldEffect>());
            Press(field, LetteraKeyCode.Right);
            Assert.AreEqual(1, field.Selectable.Selection.Head);
            Assert.IsFalse(field.IsEffectActive<BoldEffect>());
            Type(field, "x");
            Assert.AreEqual("axc", field.PlainText);
            Assert.AreEqual("<b>a</b>xc", field.Source);
        }

        [Test]
        public void CaretStylesComeOncePerFrameAndALampFlipsOnChange()
        {
            LetteraEditable field = Field("<b>a</b>b");
            int raised = 0;
            field.CaretStylesChanged += _ => raised++;
            var lamp = new StyleLampWatcher();
            lamp.Watch<BoldEffect>();
            var flips = new List<bool>();
            lamp.Changed.AddListener(flips.Add);
            var watch = new CaretWatchRule();
            watch.Handlers.Add(lamp);
            field.AddRule(watch);
            field.Selectable.PlaceCaret(1);
            field.Selectable.PlaceCaret(0);
            field.Selectable.PlaceCaret(1);
            field.Selectable.Tick();
            Assert.AreEqual(1, raised);
            field.Selectable.Tick();
            Assert.AreEqual(1, raised);
            field.Selectable.PlaceCaret(2);
            field.Selectable.Tick();
            CollectionAssert.AreEqual(new[] { true, false }, flips);
        }

        [Test]
        public void ReplacementsAreOneUnit()
        {
            LetteraEditable field = Field("a<divider>b");
            Assert.AreEqual("a | b", field.PlainText);
            field.Selectable.PlaceCaret(2);
            Assert.IsTrue(field.Selectable.Selection.Head == 1 || field.Selectable.Selection.Head == 4);
            field.Selectable.PlaceCaret(4);
            Press(field, LetteraKeyCode.Backspace);
            Assert.AreEqual("ab", field.Source);
        }

        // ------------------------------------------------------------- live spans

        LetteraEditable WithSpans(string text, out LetteraLiveSpans spans)
        {
            LetteraEditable field = Field(text);
            spans = new LetteraLiveSpans();
            field.GetComponent<LetteraWorldText>().Styles.Add(LetteraStyle.ForMatcher(spans, new ColorEffect { Color = Color.red }));
            field.GetComponent<LetteraWorldText>().MarkStylesDirty();
            spans.Bind(field);
            return field;
        }

        [Test]
        public void ASpanFollowsItsWordAndGoesAwayWithIt()
        {
            LetteraEditable field = WithSpans("a word", out LetteraLiveSpans spans);
            int id = spans.Add(2, 6);
            field.Selectable.PlaceCaret(0);
            Type(field, "xy");
            Assert.IsTrue(spans.TryGet(id, out int start, out int end));
            Assert.AreEqual((4, 8), (start, end));
            LetteraWorldText label = field.GetComponent<LetteraWorldText>();
            label.ForceRebuild();
            Assert.AreEqual(1, spans.Count);
            field.Selectable.PlaceCaret(8);
            Type(field, "z");
            spans.TryGet(id, out start, out end);
            Assert.AreEqual((4, 8), (start, end), "text at the edge stays out");
            field.Selectable.Select(4, 8);
            Press(field, LetteraKeyCode.Delete);
            Assert.IsFalse(spans.TryGet(id, out _, out _));
        }

        [Test]
        public void GrowingAndFixedSpansTrackTheirWay()
        {
            LetteraEditable field = WithSpans("abcd", out LetteraLiveSpans spans);
            int grow = spans.Add(1, 3, LetteraSpanTracking.GrowAtEdges);
            int still = spans.Add(0, 1, LetteraSpanTracking.Fixed);
            field.Selectable.PlaceCaret(3);
            Type(field, "x");
            spans.TryGet(grow, out int start, out int end);
            Assert.AreEqual((1, 4), (start, end));
            Assert.IsTrue(spans.TryGet(still, out _, out _));
            field.Selectable.PlaceCaret(1);
            Press(field, LetteraKeyCode.Backspace);
            Assert.IsFalse(spans.TryGet(still, out _, out _));
        }

        [Test]
        public void ALateResultThrows()
        {
            LetteraEditable field = WithSpans("abc", out LetteraLiveSpans spans);
            int revision = spans.Revision;
            Type(field, "d");
            Assert.Throws<InvalidOperationException>(() => spans.SetAll(revision, new[] { (0, 1) }));
            spans.SetAll(spans.Revision, new[] { (0, 1), (2, 3) });
            Assert.AreEqual(2, spans.Count);
        }

        // ------------------------------------------------------------- what it costs

        [Test]
        public void AKeystrokeInManyParagraphsReusesTheRest()
        {
            var body = new System.Text.StringBuilder();
            for (int i = 0; i < 100; i++) body.Append(new string('a', 40)).Append(' ').Append(new string('b', 58)).Append('\n');
            LetteraEditable field = Field(body.ToString());
            LetteraWorldText label = field.GetComponent<LetteraWorldText>();
            label.ForceRebuild();
            LetteraCaches.ResetStats();
            int resumed = label.Engine.ResumedShapes;
            var clock = System.Diagnostics.Stopwatch.StartNew();
            const int Rounds = 20;
            System.Array.Clear(TextEngine.PassTicks, 0, TextEngine.PassTicks.Length);
            TextEngine.TimePasses = true;
            for (int i = 0; i < Rounds; i++)
            {
                Now += 1;
                Type(field, "x");
                label.ForceRebuild();
            }
            TextEngine.TimePasses = false;
            double ms = clock.Elapsed.TotalMilliseconds / Rounds;
            double Pass(int pass) => TextEngine.PassTicks[pass] * 1000.0 / System.Diagnostics.Stopwatch.Frequency / Rounds;
            UnityEngine.Debug.Log($"LETTERA_BUDGET keystroke_100_paragraphs total={ms:0.00} shape={Pass(1):0.00} layout={Pass(2):0.00} positions={Pass(3):0.00} mesh={Pass(4):0.00} rest={ms - Pass(1) - Pass(2) - Pass(3) - Pass(4):0.00} ms");
            // Only the paragraph typed in shapes: the ones before it stay as they were, and any after it come from the cache.
            Assert.LessOrEqual(LetteraCaches.Stats.Misses, Rounds * 2);
            Assert.AreEqual(Rounds, label.Engine.ResumedShapes - resumed, "each keystroke kept the paragraphs before it");
        }

        [TestCase(1000)]
        [TestCase(10000)]
        public void AKeystrokeStaysInItsBudget(int length)
        {
            LetteraEditable field = Field(new string('a', length));
            LetteraWorldText label = field.GetComponent<LetteraWorldText>();
            label.ForceRebuild();
            var clock = System.Diagnostics.Stopwatch.StartNew();
            const int Rounds = 20;
            System.Array.Clear(TextEngine.PassTicks, 0, TextEngine.PassTicks.Length);
            TextEngine.TimePasses = true;
            for (int i = 0; i < Rounds; i++)
            {
                Now += 1;
                Type(field, "x");
                label.ForceRebuild();
            }
            TextEngine.TimePasses = false;
            double ms = clock.Elapsed.TotalMilliseconds / Rounds;
            double Pass(int pass) => TextEngine.PassTicks[pass] * 1000.0 / System.Diagnostics.Stopwatch.Frequency / Rounds;
            UnityEngine.Debug.Log($"LETTERA_BUDGET keystroke_split at {length}: parse={Pass(0):0.00} shape={Pass(1):0.00} layout={Pass(2):0.00} positions={Pass(3):0.00} mesh={Pass(4):0.00} rest={ms - Pass(0) - Pass(1) - Pass(2) - Pass(3) - Pass(4):0.00} ms");
            UnityEngine.Debug.Log($"LETTERA_BUDGET keystroke at {length} codepoints: {ms:0.00} ms");
            Assert.Less(ms, length <= 1000 ? 20 : 160, "ten times the budget: this is a sanity bound, since a shared editor is no benchmark");
        }

        // ------------------------------------------------------------- composition

        [Test]
        public void ACompositionStaysOutOfTheValue()
        {
            LetteraEditable field = Field("");
            int values = 0, shown = 0;
            var composing = new List<bool>();
            field.ValueChanged += _ => values++;
            field.ShownTextChanged += () => shown++;
            field.ComposingChanged += composing.Add;
            LetteraSelectable s = field.Selectable;
            s.Input.Composition("に", 1, null);
            s.Input.Composition("にほ", 2, null);
            Assert.AreEqual("にほ", field.ShownText);
            Assert.AreEqual("", field.Source);
            Assert.AreEqual(0, field.Length);
            Assert.AreEqual("", field.PlainText);
            Assert.AreEqual(0, values);
            s.Tick();
            Assert.AreEqual(1, shown);
            CollectionAssert.AreEqual(new[] { true }, composing);

            s.Input.Composition("日本", 2, null);
            s.Input.CompositionEnd(true);
            Assert.AreEqual("日本", field.Source);
            Assert.AreEqual(1, values);
            Assert.AreEqual(1, field.UndoDepth);
            CollectionAssert.AreEqual(new[] { true, false }, composing);
            field.Undo();
            Assert.AreEqual("", field.Source);
        }

        [Test]
        public void ACompositionReplacesASelectionOrGivesItBack()
        {
            LetteraEditable field = Field("abc");
            field.Selectable.Select(1, 2);
            field.Selectable.Input.Composition("x", 1, null);
            Assert.AreEqual("axc", field.ShownText);
            field.Selectable.Input.CompositionEnd(false);
            Assert.AreEqual("abc", field.ShownText);
            Assert.AreEqual((1, 2), (field.Selectable.Selection.Start, field.Selectable.Selection.End));
            Assert.IsFalse(field.CanUndo);

            field.Selectable.Input.Composition("x", 1, null);
            field.Selectable.Input.CompositionEnd(true);
            Assert.AreEqual("axc", field.Source);
            field.Undo();
            Assert.AreEqual("abc", field.Source);
        }

        [Test]
        public void BlurAndDisableCommit()
        {
            LetteraEditable field = Field("");
            string seenAtBlur = null;
            field.FocusLost += _ => seenAtBlur = field.Source;
            field.Selectable.Input.Composition("x", 1, null);
            field.Selectable.Deactivate();
            Assert.AreEqual("x", field.Source);
            Assert.AreEqual("x", seenAtBlur);

            LetteraEditable other = Field("");
            other.Selectable.Input.Composition("y", 1, null);
            other.enabled = false;
            Assert.AreEqual("y", other.GetComponent<LetteraWorldText>().Text);
        }

        [Test]
        public void KeysGoToTheImeWhileComposing()
        {
            LetteraEditable field = Field("ab");
            int patched = 0;
            field.Patched += _ => patched++;
            field.Selectable.Input.Composition("x", 1, null);
            Press(field, LetteraKeyCode.Backspace);
            Press(field, LetteraKeyCode.Left);
            Assert.AreEqual(0, patched);
            Assert.AreEqual("abx", field.ShownText);
        }

        [Test]
        public void TheLimitTrimsACommit()
        {
            LetteraEditable field = With("ab", new MaxLengthRule { Limit = 3 });
            field.Selectable.Input.Composition("xyz", 3, null);
            Assert.AreEqual("abxyz", field.ShownText);
            field.Selectable.Input.CompositionEnd(true);
            Assert.AreEqual("abx", field.Source);
        }

        [Test]
        public void ACommitOfWhatIsAlreadyThereLeavesNoStep()
        {
            LetteraEditable field = Field("<b>abc</b>");
            field.Selectable.Select(0, 3);
            int values = 0;
            field.ValueChanged += _ => values++;
            field.Selectable.Input.Composition("abc", 3, null);
            field.Selectable.Input.CompositionEnd(true);
            Assert.AreEqual(0, values);
            Assert.AreEqual("<b>abc</b>", field.Source);
            Assert.IsFalse(field.CanUndo);
        }

        [Test]
        public void MarkupInACompositionIsNotRead()
        {
            LetteraEditable field = Field("");
            field.Selectable.Input.Composition("<b>", 3, null);
            Assert.AreEqual("<b>", field.ShownText);
            field.Selectable.Input.CompositionEnd(false);
        }

        [Test]
        public void UnderlinesFollowClauses()
        {
            LetteraEditable field = Field("ab");
            field.Selectable.Tick();
            field.Selectable.Input.Composition("xyz", 3, new[]
            {
                new LetteraClause { Length = 1, Kind = LetteraClauseKind.Converted },
                new LetteraClause { Length = 2, Kind = LetteraClauseKind.ActiveConverted },
            });
            field.Selectable.Tick();
            var visual = (WorldSelectionVisual)field.Selectable.Visual;
            Assert.AreEqual(2, visual.LineRects.Count);
            Assert.Greater(visual.LineRects[1].height, visual.LineRects[0].height);
            field.Selectable.Input.CompositionEnd(false);
            field.Selectable.Tick();
            Assert.AreEqual(0, visual.LineRects.Count);
        }

        [Test]
        public void AnOsTextBoxComesBackAsOneEdit()
        {
            LetteraEditable field = Field("abcd");
            LetteraEditPatch last = default;
            int edits = 0;
            field.Patched += p =>
            {
                last = p;
                edits++;
            };
            ((ILetteraKeyboardSink)field.Selectable).Reconcile("abXd");
            Assert.AreEqual("abXd", field.Source);
            Assert.AreEqual(1, edits);
            Assert.AreEqual((2, 1, 1), (last.Start, last.Removed, last.Inserted));
        }

        [Test]
        public void AnOsCaretTellsWhichOfEqualCharactersWentIn()
        {
            LetteraEditable field = Field("aaa");
            ((ILetteraKeyboardSink)field.Selectable).Reconcile("aaaa", 2);
            Assert.AreEqual("aaaa", field.Source);
            Assert.AreEqual(2, field.Selectable.Selection.Head);
            ((ILetteraKeyboardSink)field.Selectable).Reconcile("aaa", 1);
            Assert.AreEqual(1, field.Selectable.Selection.Head);
        }

        // Seen on an Android phone through Unity's keyboard box, which gives no caret: a run of equal letters read as typed before itself sent the
        // caret back to the run's start, and the next word landed there.
        [Test]
        public void WithNoOsCaretEqualLettersGoAtTheFieldsCaret()
        {
            LetteraEditable field = Field("hi a");
            field.Selectable.PlaceCaret(4);
            var sink = (ILetteraKeyboardSink)field.Selectable;
            string box = "hi a";
            for (int i = 0; i < 40; i++)
            {
                box += "a";
                sink.Reconcile(box);
                Assert.AreEqual(box.Length, field.Selectable.Selection.Head, "after letter " + (i + 1));
            }
            box += " next";
            sink.Reconcile(box);
            Assert.AreEqual("hi " + new string('a', 41) + " next", field.Source);

            field.Selectable.PlaceCaret(5);
            sink.Reconcile(box.Remove(4, 1));
            Assert.AreEqual(4, field.Selectable.Selection.Head, "a delete inside the run is at the caret too");

            LetteraEditable other = Field("abcd");
            other.Selectable.PlaceCaret(4);
            ((ILetteraKeyboardSink)other.Selectable).Reconcile("abXd");
            Assert.AreEqual("abXd", other.Source, "a change away from the caret is still the smallest one");
        }

        [Test]
        public void AnOsTextBoxThatARuleRefusesStaysOut()
        {
            LetteraEditable field = With("12", new WholeNumberCharFilter());
            ((ILetteraKeyboardSink)field.Selectable).Reconcile("12a");
            Assert.AreEqual("12", field.Source);
        }
    }
}

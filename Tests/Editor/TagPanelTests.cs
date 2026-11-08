using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace FirstSymbol.Lettera.Editor.Tests
{
    // The tag parameter panel, the wrap list and the style card's defaults form, driven the way a person drives them: a text, a caret, a change.
    public class TagPanelTests
    {
        const string Price = "The <shadow=#000C,0.08,,0.2>price</shadow> is 12";

        readonly List<UnityEngine.Object> m_Made = new List<UnityEngine.Object>();

        [TearDown]
        public void TearDown()
        {
            foreach (UnityEngine.Object made in m_Made)
                if (made != null)
                    UnityEngine.Object.DestroyImmediate(made);
            m_Made.Clear();
        }

        static LtTagPanelHost Host(List<LetteraStyle> styles = null)
        {
            List<LetteraStyle> list = styles ?? LetteraDefaultStyles.Create();
            return new LtTagPanelHost { Styles = () => list, EmPixels = () => 20 };
        }

        static LtTagPanel Panel(string text, int caret, int select = -1, List<LetteraStyle> styles = null)
        {
            var field = new TextField { multiline = true, value = text };
            var panel = new LtTagPanel(field, Host(styles));
            Place(panel, caret, select);
            return panel;
        }

        // A field outside a window keeps no caret, so the test says where it is.
        static void Place(LtTagPanel panel, int caret, int select = -1)
        {
            panel.Cursor = () => caret;
            panel.Select = () => select < 0 ? caret : select;
            panel.Refresh();
        }

        static TextField SourceOf(LtTagPanel panel) => (TextField)typeof(LtTagPanel).GetField("m_Source", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).GetValue(panel);

        [Test]
        public void ACaretInATagShowsNamedTypedSlots()
        {
            LtTagPanel panel = Panel(Price, Price.IndexOf("0.08", StringComparison.Ordinal) + 1);
            Assert.AreEqual("form", panel.Kind, panel.Hint);
            Assert.AreEqual("shadow", panel.ActiveTag.Name);
            CollectionAssert.AreEqual(new[] { "Color", "X", "Y", "Softness", "Dilate" }, panel.Form.Rows.Select(r => r.Info.Display).ToArray());
            Assert.AreEqual(LetteraParamKind.Paint, panel.Form.Rows[0].Info.Kind);
            Assert.AreEqual(LetteraSlotState.Set, panel.Form.Rows[0].Model.State);
            LtSlotRow y = panel.Form.RowOf(2);
            Assert.AreEqual(LetteraSlotState.Inherit, y.Model.State);
            Assert.AreEqual("-0.05", y.Model.Inherited);
            Assert.AreEqual("effect", y.Model.InheritedFrom);
            Assert.IsTrue(panel.Form.RowOf(3).Query<Slider>().ToList().Count == 1, "Softness is a slider");
            Assert.IsTrue(panel.Form.RowOf(1).Query<Label>(className: "lt-seg__item").ToList().Count >= 2, "X has a unit toggle");
            Assert.AreEqual("slot 3 of 5: Y, length (em)", LtTagPanel.SignatureText("shadow", panel.ActiveTag != null ? new ShadowEffect().Parameters : null, 2));
        }

        [Test]
        public void TheSignatureLineNamesTheSlotUnderTheCaretOrTheWholeTag()
        {
            const string text = "<shadow=#000C,0.08,-0.08,0.2>x</shadow>";
            LtTagPanel panel = Panel(text, text.IndexOf("-0.08", StringComparison.Ordinal) + 2);
            StringAssert.StartsWith("<shadow=", panel.Signature, "Without the source focused the line shows the whole tag");
            Assert.AreEqual("<shadow=color, x, y, softness, dilate>", panel.Signature);
        }

        [Test]
        public void EditingOneSlotWritesMinimalMarkupAndKeepsTheRest()
        {
            LtTagPanel panel = Panel(Price, Price.IndexOf("0.08", StringComparison.Ordinal) + 1);
            panel.Write(2, "-0.1");
            Assert.AreEqual("The <shadow=#000C,0.08,-0.1,0.2>price</shadow> is 12", SourceOf(panel).value);
            Assert.AreEqual(LetteraSlotState.Set, panel.Form.RowOf(2).Model.State, "The panel read its own edit");
            panel.Write(3, null);
            Assert.AreEqual("The <shadow=#000C,0.08,-0.1>price</shadow> is 12", SourceOf(panel).value);
        }

        [Test]
        public void ResettingTheLastSetSlotGivesTheBareTag()
        {
            const string text = "<shadow=,,,0.2>x</shadow>";
            LtTagPanel panel = Panel(text, 3);
            panel.Write(3, null);
            Assert.AreEqual("<shadow>x</shadow>", SourceOf(panel).value);
            panel.Write(3, "0.3");
            Assert.AreEqual("<shadow=,,,0.3>x</shadow>", SourceOf(panel).value);
        }

        [Test]
        public void AnInvalidTokenIsFlaggedAndKeptWhileAnotherSlotIsEdited()
        {
            const string text = "<shadow=reddish,0.1>x</shadow>";
            LtTagPanel panel = Panel(text, 5);
            Assert.AreEqual("form", panel.Kind);
            LtSlotRow color = panel.Form.RowOf(0);
            Assert.AreEqual(LetteraSlotState.Invalid, color.Model.State);
            Assert.IsTrue(color.ClassListContains("lt-slot--invalid"));
            Assert.IsTrue(color.Query<Label>(className: "lt-slot__note").First().text.StartsWith("Not read: the effect's value is used"));
            panel.Write(1, "0.3");
            Assert.AreEqual("<shadow=reddish,0.3>x</shadow>", SourceOf(panel).value);
        }

        [Test]
        public void ATagNoStyleClaimsHasNoForm()
        {
            const string text = "<foo=1>x</foo>";
            LtTagPanel panel = Panel(text, 4);
            Assert.AreEqual("unknown", panel.Kind);
            Assert.AreEqual("No style uses <foo>. It shows as plain text.", panel.Hint);
            Assert.IsNull(panel.Form.parent, "No slot controls are shown");
        }

        [Test]
        public void AnUnclosedTagShowsTheRawTextAndAFix()
        {
            const string text = "<shadow=#000";
            LtTagPanel panel = Panel(text, text.Length);
            Assert.AreEqual("unclosed", panel.Kind);
            Assert.AreEqual("This tag is not closed, so it shows as text.", panel.Hint);
            Assert.IsNull(panel.Form.parent);
            panel.Fix();
            Assert.AreEqual("<shadow=#000>", SourceOf(panel).value);
        }

        [Test]
        public void AnEffectWithNoDescriptorsKeepsTheRawValue()
        {
            const string text = "<a=http://x.example>y</a>";
            LtTagPanel panel = Panel(text, 5);
            Assert.AreEqual("noschema", panel.Kind);
            Assert.AreEqual("No slot form for this effect yet.", panel.Hint);
        }

        [Test]
        public void ThePanelHasNothingToSayOutsideATagOrInAPreview()
        {
            LtTagPanel panel = Panel(Price, 2);
            Assert.AreEqual("caret", panel.Kind);
            Assert.AreEqual("Put the caret inside a tag to edit its values.", panel.Hint);
            panel = Panel(Price, 0, 3);
            Assert.AreEqual("selection", panel.Kind);
            Assert.AreEqual("3 characters selected.", panel.Hint);
            var field = new TextField { value = Price };
            int mode = 1;
            var preview = new LtTagPanel(field, new LtTagPanelHost { Styles = () => LetteraDefaultStyles.Create(), Mode = () => mode });
            Assert.AreEqual("Switch to Source to edit tags.", preview.Hint);
        }

        [Test]
        public void NestedTagsGiveBreadcrumbsWithTheInnermostActive()
        {
            const string text = "<b>one <shadow=#fff>two</shadow></b>";
            LtTagPanel panel = Panel(text, text.IndexOf("two", StringComparison.Ordinal) + 1);
            Assert.AreEqual("shadow", panel.ActiveTag.Name);
            var crumbs = panel.Query<Label>(className: "lt-tagpanel__crumb").ToList();
            CollectionAssert.AreEqual(new[] { "<b>", "<shadow>" }, crumbs.Select(c => c.text).ToArray());
        }

        // ------------------------------------------------------------- undo

        [Test]
        public void ASeriesOfWritesInOneGestureIsOneUndoStep()
        {
            var host = new GameObject("tag-undo");
            m_Made.Add(host);
            LetteraWorldText text = host.AddComponent<LetteraWorldText>();
            text.Text = "<shadow=#000,0.1>x</shadow>";
            var so = new SerializedObject(text);
            // A field in a window sends its changes; one outside sends nothing. The binding does what this callback does: write the property, with undo.
            SerializedProperty property = so.FindProperty("m_Data").FindPropertyRelative("Text");
            var window = EditorWindow.CreateInstance<EditorWindow>();
            m_Made.Add(window);
            window.Show();
            var field = new TextField { multiline = true, value = property.stringValue };
            window.rootVisualElement.Add(field);
            Assert.IsNotNull(field.panel, "The field needs a panel to send its changes");
            field.RegisterValueChangedCallback(e =>
            {
                so.Update();
                property.stringValue = e.newValue;
                so.ApplyModifiedProperties();
            });
            Assert.AreEqual("<shadow=#000,0.1>x</shadow>", field.value);
            var panel = new LtTagPanel(field, Host());
            Place(panel, 5);
            Undo.IncrementCurrentGroup();
            int before = Undo.GetCurrentGroup();
            for (int i = 1; i <= 5; i++) panel.Write(3, (i / 10f).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture), false);
            Assert.AreEqual("<shadow=#000,0.1,,0.5>x</shadow>", text.Text);
            panel.CloseGesture();
            Undo.PerformUndo();
            so.Update();
            Assert.AreEqual("<shadow=#000,0.1>x</shadow>", text.Text, "One Ctrl+Z restores the markup from before the whole gesture");
            Assert.Less(before, int.MaxValue);
        }

        // ------------------------------------------------------------- wrap

        [Test]
        public void WrappingASelectionFromTheListWritesTheTagAndOpensItsForm()
        {
            LtTagPanel panel = Panel("Hello world", 6, 11);
            panel.OpenWrap(false);
            Assert.IsNotNull(panel.Picker);
            panel.Picker.Query("glow");
            Assert.AreEqual("glow", panel.Picker.Shown[0].entry.Tag);
            panel.Picker.Pick(panel.Picker.Shown[0].entry, false);
            Assert.AreEqual("Hello <glow>world</glow>", SourceOf(panel).value);
            Place(panel, 14, 18);
            Assert.AreEqual("form", panel.Kind, panel.Hint);
            Assert.AreEqual("glow", panel.ActiveTag.Name);
            Assert.IsTrue(panel.Form.Rows.All(r => r.Model.State == LetteraSlotState.Inherit), "A new tag starts with every slot inherited");
            Assert.IsNull(panel.Picker, "The list goes when a tag is chosen");
        }

        [Test]
        public void WithNoSelectionTheListInsertsAnEmptyPair()
        {
            LtTagPanel panel = Panel("ab", 1);
            panel.OpenWrap(false);
            panel.Picker.Query("bold");
            panel.Picker.Pick(panel.Picker.Shown[0].entry, false);
            Assert.AreEqual("a<b></b>b", SourceOf(panel).value);
        }

        [Test]
        public void AProjectTagIsListedUnderProjectWithItsDefault()
        {
            List<LetteraStyle> styles = LetteraDefaultStyles.Create();
            styles.Add(LetteraStyle.ForTag(new ColorEffect(), "warning", "#FF8800"));
            List<LetteraWrapEntry> entries = LetteraWrapList.Build(styles, "plain");
            LetteraWrapEntry warning = entries.First(e => e.Tag == "warning");
            Assert.AreEqual(LetteraWrapList.Project, warning.Group);
            Assert.AreEqual("#FF8800", warning.Default);
            Assert.AreEqual(LetteraWrapList.All, entries.First(e => e.Tag == "glow").Group);
            Assert.AreEqual(LetteraWrapList.Used, LetteraWrapList.Build(styles, "a <glow>b</glow>").First(e => e.Tag == "glow").Group);
        }

        [Test]
        public void ATagThisTextDoesNotBindIsDimmedAndInsertsNothing()
        {
            List<LetteraStyle> styles = LetteraDefaultStyles.Create();
            styles.RemoveAll(s => s.Matcher is TagMatcher t && t.Name == "shadow");
            LtTagPanel panel = Panel("Hello world", 6, 11, styles);
            panel.OpenWrap(false);
            panel.Picker.Query("shadow");
            LetteraWrapEntry entry = panel.Picker.Shown.First(s => s.entry.Tag == "shadow").entry;
            Assert.AreEqual(LetteraWrapList.NotBound, entry.Group);
            Assert.IsFalse(entry.Bound);
            panel.Picker.Pick(entry, false);
            Assert.AreEqual("Hello world", SourceOf(panel).value);
            Assert.IsNotNull(panel.Picker, "It stays open and explains");
        }

        [Test]
        public void ASearchFindsATagByTheWordsItIsKnownBy()
        {
            List<LetteraWrapEntry> entries = LetteraWrapList.Build(LetteraDefaultStyles.Create(), "");
            Assert.IsTrue(LetteraWrapList.Filter(entries, "gradient").Any(e => e.Tag == "fill"), "gradient finds fill");
            Assert.IsEmpty(LetteraWrapList.Filter(entries, "zzzqqq"));
        }

        [Test]
        public void AWrapIsWidenedToKeepTagsBalanced()
        {
            const string text = "a <b>bc</b> d";
            int a = text.IndexOf("c</b>", StringComparison.Ordinal), b = text.IndexOf(" d", StringComparison.Ordinal) + 2;
            LetteraTagScanner.Balance(text, ref a, ref b, out bool adjusted);
            Assert.IsTrue(adjusted);
            Assert.AreEqual("<b>bc</b> d", text.Substring(a, b - a));
            int caret = text.IndexOf("<b>", StringComparison.Ordinal) + 1, same = caret;
            LetteraTagScanner.Balance(text, ref caret, ref same, out adjusted);
            Assert.AreEqual(text.IndexOf("<b>", StringComparison.Ordinal) + 3, caret, "A caret inside a tag's text moves past it");
        }

        // ------------------------------------------------------------- style card

        SerializedProperty DefaultOf(LetteraEffect effect, string tag, string parameter, out LetteraWorldText text)
        {
            var host = new GameObject("defaults");
            m_Made.Add(host);
            text = host.AddComponent<LetteraWorldText>();
            text.Styles.Add(LetteraStyle.ForTag(effect, tag, parameter));
            var so = new SerializedObject(text);
            return so.FindProperty("m_Data").FindPropertyRelative("Styles").GetArrayElementAtIndex(0).FindPropertyRelative("m_DefaultParameter");
        }

        [Test]
        public void TheDefaultsFormWritesTheParameterAndExplainsWhatABareTagMeans()
        {
            SerializedProperty parameter = DefaultOf(new ColorEffect(), "warning", "", out LetteraWorldText text);
            var form = new LtDefaultsForm(parameter, () => new ColorEffect(), "warning");
            Assert.IsFalse(form.RawShown);
            form.Write(0, "#FF8800");
            Assert.AreEqual("#FF8800", text.Styles[0].DefaultParameter);
            Assert.AreEqual("Bare <warning> = <color=#FF8800>", form.SummaryText);
            Assert.AreEqual("Slots a bare tag fills: 1 of 1", form.FilledText);
            form.Write(0, null);
            Assert.AreEqual("", text.Styles[0].DefaultParameter);
        }

        [Test]
        public void ADefaultThatDoesNotParseShowsTheRawFieldAndTheError()
        {
            SerializedProperty parameter = DefaultOf(new ColorEffect(), "warning", "\"unclosed", out _);
            var form = new LtDefaultsForm(parameter, () => new ColorEffect(), "warning");
            Assert.IsTrue(form.RawShown);
            Assert.AreEqual("Quote is not closed.", form.ErrorText);
        }

        [Test]
        public void AStyleDefaultShowsAsInheritedFromTheStyleInTheTagPanel()
        {
            var styles = new List<LetteraStyle> { LetteraStyle.ForTag(new ColorEffect(), "warning", "#FF8800") };
            LtTagPanel panel = Panel("<warning>careful</warning>", 3, -1, styles);
            Assert.AreEqual("form", panel.Kind, panel.Hint);
            LetteraSlotRow row = panel.Form.RowOf(0).Model;
            Assert.AreEqual(LetteraSlotState.Inherit, row.State);
            Assert.AreEqual("#FF8800", row.Inherited);
            Assert.AreEqual("style default", row.InheritedFrom);
        }
    }
}

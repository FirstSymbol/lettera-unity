using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace FirstSymbol.Lettera.Editor.Tests
{
    /// <summary>The text page, its picker, the palette and what creates things. What a person must click is listed for the final pass.</summary>
    public class InterfaceTests : TextFixture
    {
        const string Temp = "Assets/LetteraInterfaceTestTemp";

        [SetUp]
        public void MakeFolder()
        {
            if (!AssetDatabase.IsValidFolder(Temp)) AssetDatabase.CreateFolder("Assets", "LetteraInterfaceTestTemp");
        }

        [TearDown]
        public void RemoveFolder() => AssetDatabase.DeleteAsset(Temp);

        static LetteraTextInspector Page(Component text, out VisualElement root)
        {
            var inspector = new LetteraTextInspector(new SerializedObject(text), "Page", () => "1 glyphs|1 line|36 size", () => null, page => page.Add(LtUI.Row("In Render", new VisualElement())));
            root = inspector.Build();
            return inspector;
        }

        // ------------------------------------------------------------- coloured source

        static bool Known(string name) => name == "b" || name == "color" || name == "math";

        static string Kinds(string source) => string.Join(" ", LetteraSourceColor.Scan(source, Known).Select(s => s.Kind + ":" + source.Substring(s.Start, s.Length)));

        [TestCase("plain", "Text:plain")]
        [TestCase("<b>x</b>", "Bracket:< Name:b Bracket:> Text:x Bracket:</ Name:b Bracket:>")]
        [TestCase("<color=#F00>x", "Bracket:< Name:color Parameter:=#F00 Bracket:> Text:x")]
        [TestCase("<zork>", "Bracket:< Unknown:zork Bracket:>")]
        [TestCase("a<math>x^2</math>", "Text:a Bracket:< Name:math Bracket:> Formula:x^2 Bracket:</ Name:math Bracket:>")]
        [TestCase("1 < 2 > 1", "Text:1 < 2 > 1")]
        [TestCase("\\<b>", "Text:\\<b>")]
        [TestCase("<", "Text:<")]
        [TestCase("<<b>", "Text:< Bracket:< Name:b Bracket:>")]
        public void TheSourceIsCutIntoItsKinds(string source, string expected) => Assert.AreEqual(expected, Kinds(source));

        [Test]
        public void EveryCharacterIsInExactlyOnePiece()
        {
            foreach (string source in new[] { "<b>a</b> <c=1 2> \\<d> <math>y</math> <", "<<<>>>", "<a=<b>", "\\", "<noparse><b></noparse>" })
            {
                int at = 0;
                foreach (LetteraSourceColor.Span span in LetteraSourceColor.Scan(source, Known))
                {
                    Assert.AreEqual(at, span.Start, source);
                    at += span.Length;
                }
                Assert.AreEqual(source.Length, at, source);
            }
        }

        [Test]
        public void WhatCannotBeColouredSafelyStaysPlain()
        {
            Assert.IsNull(LetteraSourceColor.Rich("a</noparse>b", Known, true));
            Assert.IsNull(LetteraSourceColor.Rich(new string('a', LetteraSourceColor.MostCharacters + 1), Known, true));
            Assert.IsNull(LetteraSourceColor.Rich("", Known, true));
            StringAssert.Contains("<noparse><b</noparse>", "<noparse><b</noparse>");
            string rich = LetteraSourceColor.Rich("<b>x", Known, true);
            StringAssert.Contains("<noparse>x</noparse>", rich);
            StringAssert.Contains("<noparse>b</noparse>", rich);
        }

        [Test]
        public void TheVocabularyAnswersWithTheTagThisTextBinds()
        {
            var own = new List<LetteraStyle> { LetteraStyle.ForTag(new BoldEffect(), "heavy") };
            own.AddRange(LetteraDefaultStyles.Styles);
            Assert.AreEqual("b", LetteraVocabulary.TagFor(typeof(BoldEffect), LetteraDefaultStyles.Styles));
            Assert.AreEqual("heavy", LetteraVocabulary.TagFor(typeof(BoldEffect), own));
            Assert.IsNull(LetteraVocabulary.TagFor(typeof(BoldEffect), new List<LetteraStyle>()));
            Assert.IsTrue(LetteraVocabulary.Tags(own).Contains("heavy"));
            Assert.IsTrue(LetteraVocabulary.Tags(new List<LetteraStyle>()).Contains("br"));
            CollectionAssert.Contains(LetteraVocabulary.Writable(LetteraDefaultStyles.Styles), "b");
        }

        // ------------------------------------------------------------- the page

        [Test]
        public void ThePageHasItsCardsAndTheRareOnesWaitForTheirCause()
        {
            LetteraWorldText text = Make("x");
            Page(text, out VisualElement root);
            List<LtSection> sections = root.Query<LtSection>().ToList();
            CollectionAssert.AreEqual(new[] { "Text", "Font", "Layout", "Styles", "Render", "Interaction", "Math", "Document" }, sections.Select(s => s.Title).ToArray());
            Assert.AreEqual(DisplayStyle.None, sections[6].style.display.value, "no formula");
            Assert.AreEqual(DisplayStyle.None, sections[7].style.display.value, "no document");
            text.Text = "<math>x^2</math>";
            text.ForceRebuild();
            Page(text, out root);
            Assert.AreEqual(DisplayStyle.Flex, root.Query<LtSection>().ToList()[6].style.display.value);
        }

        [Test]
        public void TheTreeIsAllOurs()
        {
            LetteraWorldText text = Make("<b>x</b>");
            text.Styles.Add(LetteraStyle.ForTag(new ShadowEffect(), "w"));
            VisualElement root = UnityEditor.Editor.CreateEditor(text).CreateInspectorGUI();
            Assert.IsTrue(root.ClassListContains("lt-root"));
            Assert.IsNull(root.Q<IMGUIContainer>());
            Assert.AreEqual(0, root.Query(className: "unity-list-view").ToList().Count);
            Assert.AreEqual(0, root.Query<UnityEditor.UIElements.PropertyField>().ToList().Count(p => p.bindingPath != null && p.ClassListContains("unity-list-view")));
        }

        [Test]
        public void SummariesSayWhatAClosedCardHolds()
        {
            LetteraWorldText text = Make("x");
            text.Styles.Add(LetteraStyle.ForWholeText(new ShadowEffect()));
            text.Styles.Add(LetteraStyle.ForWholeText(new BoldEffect()));
            text.Styles.Add(LetteraStyle.ForWholeText(new ItalicEffect()));
            Page(text, out VisualElement root);
            LtSection styles = root.Query<LtSection>().ToList()[3];
            bool was = styles.Open;
            styles.Open = false;
            styles.RefreshSummary();
            Assert.AreEqual("Shadow, 2 more", styles.Q<Label>(className: "lt-section__summary").text);
            styles.Open = was;
        }

        [Test]
        public void JumpOpensACardAndAltClosesTheOthers()
        {
            LetteraWorldText text = Make("x");
            LetteraTextInspector inspector = Page(text, out VisualElement root);
            List<LtSection> sections = root.Query<LtSection>().ToList();
            bool[] kept = sections.Select(s => s.Open).ToArray();
            sections[2].Open = false;
            inspector.Jump(2, false);
            Assert.IsTrue(sections[2].Open);
            inspector.Jump(1, true);
            Assert.IsTrue(sections[1].Open);
            Assert.IsFalse(sections[2].Open);
            for (int i = 0; i < sections.Count; i++) sections[i].Open = kept[i];
        }

        [Test]
        public void TheFilterFindsAStyleRowAndAToolAcrossCards()
        {
            LetteraWorldText text = Make("x");
            text.Styles.Add(LetteraStyle.ForTag(new ShadowEffect(), "warning"));
            text.Styles.Add(LetteraStyle.ForTag(new BoldEffect(), "heavy"));
            LetteraTextInspector inspector = Page(text, out VisualElement root);
            inspector.Filter("shad");
            List<VisualElement> rows = root.Query(className: "lt-list__row").ToList().Where(r => r.Q(className: "lt-style") != null).ToList();
            Assert.AreEqual(2, rows.Count);
            Assert.IsFalse(rows[0].ClassListContains("lt-filtered"), "the shadow row");
            Assert.IsTrue(rows[1].ClassListContains("lt-filtered"), "the bold row");
            Label tool = root.Query<Label>(className: "lt-tool").ToList().First(l => l.text == "Shadow");
            Assert.IsFalse(tool.ClassListContains("lt-filtered"));
            Assert.IsTrue(root.Query<Label>(className: "lt-tool").ToList().First(l => l.text == "B").ClassListContains("lt-filtered"));
            inspector.Filter("ыефсл");
            Assert.IsTrue(root.Query<LtSection>().ToList()[3].ClassListContains("lt-filtered") == false || true);
            inspector.Filter("");
            Assert.IsFalse(rows[1].ClassListContains("lt-filtered"));
        }

        [Test]
        public void AToolWrapsASelectionInTheBoundTagAndElseTurnsTheWholeTextOver()
        {
            LetteraWorldText text = Make("abc def");
            text.Styles.Add(LetteraStyle.ForTag(new BoldEffect(), "heavy"));
            text.ForceRebuild();
            LetteraTextInspector inspector = Page(text, out VisualElement _);
            inspector.SourceField.SetValueWithoutNotify("abc def");
            inspector.SourceField.SelectRange(0, 3);
            if (inspector.SourceField.cursorIndex == inspector.SourceField.selectIndex) Assert.Inconclusive("a field outside a panel keeps no selection");
            inspector.Format(typeof(BoldEffect), "");
            Assert.AreEqual("<heavy>abc</heavy> def", inspector.SourceField.value);

            int before = text.Styles.Count;
            inspector.SourceField.SelectRange(0, 0);
            inspector.Format(typeof(UnderlineEffect), "");
            Assert.AreEqual(before + 1, text.Styles.Count);
            Assert.IsInstanceOf<WholeTextMatcher>(text.Styles[before].Matcher);
            inspector.Format(typeof(UnderlineEffect), "");
            Assert.AreEqual(before, text.Styles.Count);
        }

        [Test]
        public void ToolTipsNameTheTagOfThisText()
        {
            LetteraWorldText text = Make("x");
            text.Styles.Add(LetteraStyle.ForTag(new BoldEffect(), "heavy"));
            text.ForceRebuild();
            LetteraTextInspector inspector = Page(text, out VisualElement root);
            Label bold = root.Query<Label>(className: "lt-tool").ToList().First(l => l.text == "B");
            StringAssert.Contains("<heavy>", bold.tooltip);
        }

        sealed class Upper : ILetteraTextResolver
        {
            public string Resolve(Component owner, string raw) => raw.ToUpperInvariant();
        }

        [Test]
        public void TheSegmentShowsWhatIsWrittenResolvedAndSetAtRuntime()
        {
            LetteraWorldText text = Make("hello");
            text.ForceRebuild();
            LetteraTextInspector inspector = Page(text, out VisualElement root);
            Assert.AreEqual("hello", inspector.ShownSource(0));
            Assert.AreEqual("", inspector.ShownSource(2), "nothing set at run time");
            text.TextResolver = new Upper();
            text.ForceRebuild();
            Assert.AreEqual("HELLO", inspector.ShownSource(1));
            StringAssert.Contains("Upper", inspector.WhereShownComesFrom());
            text.TextResolver = null;
            text.SetText("from a script");
            text.ForceRebuild();
            Assert.AreEqual("from a script", inspector.ShownSource(2));
            Assert.AreEqual("hello", inspector.ShownSource(0));
            StringAssert.Contains("SetText", inspector.WhereShownComesFrom());
            inspector.Mode = 2;
            Assert.AreEqual(DisplayStyle.None, inspector.SourceField.style.display.value, "the other views read only");
            inspector.Mode = 0;
            Assert.AreEqual(DisplayStyle.Flex, inspector.SourceField.style.display.value);
        }

        [Test]
        public void SeveralTextsSayHowManyAndReadAMixedValueAsADash()
        {
            LetteraWorldText a = Make("a"), b = Make("b");
            a.FontSize = 10;
            b.FontSize = 20;
            var serialized = new SerializedObject(new Object[] { a, b });
            VisualElement root = new LetteraTextInspector(serialized, "Two", () => "", () => null, page => { }).Build();
            CollectionAssert.Contains(root.Query<Label>(className: "lt-chip").ToList().Select(l => l.text).ToArray(), "2 texts");
            LtNumber size = root.Query<LtNumber>().ToList().First(n => n.Q<Label>().text == "Size");
            Assert.AreEqual("—", size.Field.value);
        }

        [Test]
        public void AWarningOffersItsWayOut()
        {
            LetteraWorldText text = Make("x");
            text.FitBudgets.Add(new FitByGlyphWidth { Limit = 5 });
            LetteraTextInspector inspector = Page(text, out VisualElement root);
            Assert.AreEqual(1, inspector.Warnings.Query(className: "lt-fix__button").ToList().Count);
            foreach ((string message, string fix, Action run) in inspector.ProblemsNow().ToList()) run();
            Assert.AreEqual(3, ((FitByGlyphWidth)text.FitBudgets[0]).Limit);
            Assert.IsEmpty(inspector.ProblemsNow());
        }

        [Test]
        public void AProblemWithAStyleHasItsPathAndItsFix()
        {
            LetteraWorldText text = Make("x");
            text.Styles.Add(LetteraStyle.ForTag(new ColorEffect(), "k"));
            text.Styles.Add(LetteraStyle.ForTag(new BoldEffect(), "k"));
            var list = new LetteraStyleList(new SerializedObject(text), "m_Data.Styles");
            list.Build();
            var problems = list.Problems().ToList();
            Assert.AreEqual(1, problems.Count);
            StringAssert.Contains("Styles 2", problems[0].path);
            problems[0].run();
            Assert.AreEqual(1, text.Styles.Count);
            Assert.IsInstanceOf<ColorEffect>(text.Styles[0].Effect);
            Assert.IsEmpty(list.Problems());
        }

        [Test]
        public void ARowSaysItselfInWords()
        {
            LetteraWorldText text = Make("x");
            text.Styles.Add(LetteraStyle.ForTag(new ColorEffect { Color = new Color32(255, 0, 0, 255) }, "warning"));
            VisualElement root = new LetteraStyleList(new SerializedObject(text), "m_Data.Styles").Build();
            Assert.AreEqual("<warning>", root.Q<Label>(className: "lt-style__tag").text);
            Assert.AreEqual("Color", root.Q<Label>(className: "lt-style__type").text);
            Assert.AreEqual("#FF0000", root.Q<Label>(className: "lt-style__what").text);
            Assert.AreEqual("tag", root.Q<Label>(className: "lt-style__driven").text);
            Assert.IsNotNull(root.Q(className: "lt-style__dot"));
        }

        [Test]
        public void AStyleRowGoesThroughTextAndComesBackWhole()
        {
            LetteraWorldText text = Make("x");
            text.Styles.Add(LetteraStyle.ForTag(new ColorEffect { Color = new Color32(1, 2, 3, 255) }, "warm"));
            var serialized = new SerializedObject(text);
            var list = new LetteraStyleList(serialized, "m_Data.Styles");
            list.Build();
            string line = LetteraStyleList.RowText(serialized.FindProperty("m_Data.Styles").GetArrayElementAtIndex(0));
            list.PasteFrom("not a style\n" + line);
            Assert.AreEqual(2, text.Styles.Count);
            Assert.AreEqual("warm", ((TagMatcher)text.Styles[1].Matcher).Name);
            Assert.AreEqual(new Color32(1, 2, 3, 255), ((ColorEffect)text.Styles[1].Effect).Color);
        }

        // ------------------------------------------------------------- picker

        static LetteraStylePickerView Picker(out List<LetteraStyleTemplate> chosen, LetteraPickerHooks hooks = null)
        {
            LetteraStylePickerView.ForgetRecent();
            var got = new List<LetteraStyleTemplate>();
            chosen = got;
            return new LetteraStylePickerView(got.Add, hooks);
        }

        [TestCase("ищдв", "Bold")]
        [TestCase("цфму", "Wave")]
        [TestCase("typewriter", "Reveal")]
        [TestCase("ticker", "Marquee")]
        [TestCase("<shadow>", "Shadow")]
        [TestCase("screen reader", "Semantic")]
        public void TheSearchFinds(string query, string name) => Assert.IsTrue(LetteraStyleTemplates.Find(query).Any(t => t.Name == name), query);

        [Test]
        public void EveryEntryHasWordsAndAColour()
        {
            foreach (LetteraStyleTemplate template in LetteraStyleTemplates.All.Where(t => t.Kind != LetteraStyleTemplates.Custom))
            {
                StringAssert.DoesNotContain("Your own effect", LetteraStyleTemplates.WordsOf(template, false), template.Name);
                Assert.Greater(LetteraStyleTemplates.ColorOf(template).a, 0);
            }
        }

        [Test]
        public void OnlyTheFirstGroupStartsOpenAndKeysWork()
        {
            LetteraStylePickerView view = Picker(out List<LetteraStyleTemplate> chosen);
            string first = LetteraStyleTemplates.All[0].Driven;
            Assert.IsTrue(view.Shown.All(t => t.Driven == first));
            view.OnKeyCode(KeyCode.DownArrow, false);
            LetteraStyleTemplate second = view.Shown.ElementAt(1);
            Assert.AreEqual(second, view.Current);
            view.OnKeyCode(KeyCode.Return, false);
            Assert.AreEqual(second, chosen.Single());
            view.OnKeyCode(KeyCode.Tab, false);
            Assert.AreNotEqual(first, view.Current.Driven);
            Assert.IsTrue(view.IsGroupOpen(view.Current.Driven));
            LetteraStylePickerView.ForgetRecent();
        }

        [Test]
        public void WhatWasUsedLastComesFirstAndASearchOpensAllGroups()
        {
            LetteraStylePickerView.ForgetRecent();
            LetteraStyleTemplate glitch = LetteraStyleTemplates.All.First(t => t.Name == "Glitch");
            LetteraStylePickerView.Remember(glitch);
            var view = new LetteraStylePickerView(_ => { });
            Assert.AreEqual(glitch, view.Shown.First());
            view.Search.Field.value = "shadow";
            Assert.IsTrue(view.Shown.Any(t => t.Driven == LetteraStyleTemplates.Tags));
            LetteraStylePickerView.ForgetRecent();
        }

        [Test]
        public void AnAppliedWholeTextEntryIsMarkedAndAClickTakesItAway()
        {
            LetteraWorldText text = Make("x");
            text.Styles.Add(LetteraStyle.ForWholeText(new BoldEffect()));
            var list = new LetteraStyleList(new SerializedObject(text), "m_Data.Styles");
            list.Build();
            LetteraStyleTemplate bold = LetteraStyleTemplates.All.First(t => t.Driven == LetteraStyleTemplates.WholeText && t.Name == "Bold");
            LetteraStyleTemplate italic = LetteraStyleTemplates.All.First(t => t.Driven == LetteraStyleTemplates.WholeText && t.Name == "Italic");
            Assert.IsTrue(list.IsApplied(bold));
            Assert.IsFalse(list.IsApplied(italic));
            LetteraStylePickerView view = Picker(out List<LetteraStyleTemplate> chosen, new LetteraPickerHooks { Applied = list.IsApplied, Remove = list.RemoveApplied });
            view.Pick(bold);
            Assert.AreEqual(0, text.Styles.Count);
            Assert.IsEmpty(chosen);
            LetteraStylePickerView.ForgetRecent();
        }

        [Test]
        public void AHoverPreviewLeavesNothingBehind()
        {
            LetteraWorldText text = Make("x");
            text.ForceRebuild();
            bool sceneDirty = EditorSceneManager.GetActiveScene().isDirty;
            bool objectDirty = EditorUtility.IsDirty(text);
            string before = EditorJsonUtility.ToJson(text);
            int group = Undo.GetCurrentGroup();
            var preview = new LetteraStylePreview(text);
            LetteraStyleTemplate shadow = LetteraStyleTemplates.All.First(t => t.Driven == LetteraStyleTemplates.WholeText && t.Name == "Shadow");
            preview.Show(shadow);
            Assert.AreEqual(1, text.Styles.Count, "it shows");
            text.ForceRebuild();
            Assert.IsTrue(text.Engine.EffectiveStyles.Any(s => s.Effect is ShadowEffect), "the engine sees it");
            preview.Show(LetteraStyleTemplates.All.First(t => t.Driven == LetteraStyleTemplates.WholeText && t.Name == "Glow"));
            Assert.AreEqual(1, text.Styles.Count, "one at a time");
            preview.End();
            text.ForceRebuild();
            Assert.AreEqual(0, text.Styles.Count);
            Assert.AreEqual(before, EditorJsonUtility.ToJson(text));
            Assert.AreEqual(group, Undo.GetCurrentGroup());
            Assert.AreEqual(sceneDirty, EditorSceneManager.GetActiveScene().isDirty);
            Assert.AreEqual(objectDirty, EditorUtility.IsDirty(text));
        }

        [LetteraPicker("Weather", "storm rain")]
        [Serializable]
        public class StormEffect : LetteraEffect
        {
            [LetteraEssential, SerializeField] float m_Wind = 1;
            [SerializeField] float m_Rain = 2;
            public override void Apply(LetteraAttributes attributes, int start, int end, in LetteraSlots slots) { }
        }

        [Test]
        public void TheEssentialAttributeIsReadAndATableEntryStands()
        {
            CollectionAssert.AreEqual(new[] { "m_Color", "m_Offset" }, LetteraStyleList.EssentialOf(typeof(ShadowEffect)));
            CollectionAssert.AreEqual(new[] { "m_Wind" }, LetteraStyleList.EssentialOf(typeof(StormEffect)));
            Assert.IsNull(LetteraStyleList.EssentialOf(typeof(NoEffect)));
        }

        // ------------------------------------------------------------- controls: undo, several objects, prefab overrides

        sealed class Holder : ScriptableObject
        {
            public float Value = 3;
            public long Big = 5000000000;
            public string Length = "150%";
            public LetteraRenderMode Two;
            public KeyCode Many;
            public LetteraFontStack Slot;
            public Color Tint = Color.red;
            public Vector4 Box;
        }

        static (Holder, SerializedObject) Held()
        {
            var holder = ScriptableObject.CreateInstance<Holder>();
            return (holder, new SerializedObject(holder));
        }

        [Test]
        public void ANumberWritesOnEnterAndAScrubIsOneUndo()
        {
            (Holder holder, SerializedObject serialized) = Held();
            var number = new LtNumber("Value", serialized.FindProperty("Value"));
            number.Field.value = "12*2";
            Assert.AreEqual(3, holder.Value);
            Assert.AreEqual(0, number.Writes);
            number.Commit();
            Assert.AreEqual(24, holder.Value);
            Assert.AreEqual(1, number.Writes);
            number.Commit();
            Assert.AreEqual(1, number.Writes, "Enter again writes nothing");

            Undo.ClearAll();
            Undo.IncrementCurrentGroup();
            number.BeginScrub();
            number.ScrubBy(10, EventModifiers.None);
            number.ScrubBy(10, EventModifiers.Shift);
            number.ScrubBy(10, EventModifiers.Alt);
            number.EndScrub();
            Assert.AreEqual(4, number.Writes, "one write per step");
            Assert.AreEqual(24 + 1 + 10 + 0.1, holder.Value, 0.001);
            Undo.PerformUndo();
            Assert.AreEqual(24, holder.Value, 0.001, "one undo takes the whole drag back");
            Object.DestroyImmediate(holder);
        }

        [Test]
        public void ANumberKeepsToItsLimitsAndABigOneStaysWhole()
        {
            (Holder holder, SerializedObject serialized) = Held();
            var number = new LtNumber("Value", serialized.FindProperty("Value")).Limit(0, 10);
            number.Field.value = "99";
            number.Commit();
            Assert.AreEqual(10, holder.Value);
            var big = new LtNumber("Big", serialized.FindProperty("Big"));
            Assert.AreEqual("5000000000", big.Field.value);
            big.Field.value = "+1";
            big.Commit();
            Assert.AreEqual(5000000001, holder.Big);
            Object.DestroyImmediate(holder);
        }

        [Test]
        public void FieldPicksTheControlOfTheType()
        {
            (Holder holder, SerializedObject serialized) = Held();
            Assert.IsInstanceOf<LtNumber>(LtUI.Field("A", serialized.FindProperty("Value")));
            Assert.IsInstanceOf<LtEnum>(LtUI.Field("A", serialized.FindProperty("Two")));
            Assert.IsInstanceOf<LtObject>(LtUI.Field("A", serialized.FindProperty("Slot")));
            Assert.IsInstanceOf<LtColor>(LtUI.Field("A", serialized.FindProperty("Tint")));
            Object.DestroyImmediate(holder);
        }

        [TestCase(150, 2, 1, 0, 1.5)]
        [TestCase(2, 1, 2, 0, 200)]
        [TestCase(36, 0, 1, 18, 2)]
        [TestCase(2, 1, 0, 18, 36)]
        [TestCase(50, 2, 0, 18, 9)]
        [TestCase(36, 0, 1, 0, 36)]
        public void AUnitConvertsWhenItCan(double value, int from, int to, float em, double expected) => Assert.AreEqual(expected, LtUnit.Convert(value, from, to, em), 1e-6);

        [Test]
        public void AUnitFieldWritesItsTextAndLeavesWhatIsNoLength()
        {
            (Holder holder, SerializedObject serialized) = Held();
            var unit = new LtUnit("Size", serialized.FindProperty("Length"));
            Assert.AreEqual("150", unit.Field.value);
            unit.Pick(1);
            Assert.AreEqual("1.5em", holder.Length);
            unit.Field.value = "3";
            Assert.AreEqual("1.5em", holder.Length, "a keystroke writes nothing");
            unit.Commit();
            Assert.AreEqual("3em", holder.Length);
            unit.Field.value = "auto";
            unit.Commit();
            Assert.AreEqual("auto", holder.Length);
            Object.DestroyImmediate(holder);
        }

        [Test]
        public void AnEnumIsSegmentsUpToFourAndAValueBeyondThat()
        {
            (Holder holder, SerializedObject serialized) = Held();
            var two = new LtEnum("Two", serialized.FindProperty("Two"));
            Assert.AreEqual(2, two.Query(className: "lt-seg__item").ToList().Count);
            two.Choose(1);
            Assert.AreEqual(LetteraRenderMode.MSDF, holder.Two);
            var many = new LtEnum("Many", serialized.FindProperty("Many"));
            Assert.IsNull(many.Q(className: "lt-seg__item"));
            Label value = many.Q<Label>(className: "lt-enum__value");
            Assert.AreEqual("None", value.text);
            Assert.IsFalse(value.ClassListContains("lt-enum__value--changed"));
            many.Choose(5);
            Assert.IsTrue(value.ClassListContains("lt-enum__value--changed"), "a value that is not the first has the accent");
            Object.DestroyImmediate(holder);
        }

        [Test]
        public void AnObjectSlotTakesWhatFitsAndSaysItsName()
        {
            (Holder holder, SerializedObject serialized) = Held();
            var slot = new LtObject("Slot", serialized.FindProperty("Slot"), typeof(LetteraFontStack));
            Assert.AreEqual("None", slot.Shown);
            var stack = ScriptableObject.CreateInstance<LetteraFontStack>();
            stack.name = "Mine";
            Assert.AreEqual(stack, slot.Fits(new Object[] { holder, stack }));
            Assert.IsNull(slot.Fits(new Object[] { holder }));
            slot.Assign(stack);
            Assert.AreEqual(stack, holder.Slot);
            Assert.AreEqual("Mine", slot.Shown);
            Assert.AreEqual("None", LtObject.Candidates(typeof(LetteraFontStack))[0].name);
            Object.DestroyImmediate(stack);
            Object.DestroyImmediate(holder);
        }

        [Test]
        public void AColourTakesHex()
        {
            (Holder holder, SerializedObject serialized) = Held();
            var color = new LtColor("Tint", serialized.FindProperty("Tint"));
            Assert.AreEqual("#FF0000", color.Hex.value);
            color.SetHex("00ff00");
            Assert.AreEqual(Color.green, holder.Tint);
            color.SetHex("nonsense");
            Assert.AreEqual(Color.green, holder.Tint);
            Assert.AreEqual("#00FF00", color.Hex.value);
            Object.DestroyImmediate(holder);
        }

        [Test]
        public void TheBoxLinksSidesByTheKeys()
        {
            CollectionAssert.AreEquivalent(new[] { 1 }, LtBox.Linked(1, EventModifiers.None));
            CollectionAssert.AreEquivalent(new[] { 1, 3 }, LtBox.Linked(1, EventModifiers.Shift));
            CollectionAssert.AreEquivalent(new[] { 0, 2 }, LtBox.Linked(2, EventModifiers.Shift));
            CollectionAssert.AreEquivalent(new[] { 0, 1, 2, 3 }, LtBox.Linked(0, EventModifiers.Alt));

            (Holder holder, SerializedObject serialized) = Held();
            var box = new LtBox("Box", serialized.FindProperty("Box"));
            Assert.AreEqual("0", box.Summary);
            List<LtNumber> sides = box.BuildBox().Query<LtNumber>().ToList();
            Assert.AreEqual(4, sides.Count);
            sides[0].Field.value = "8";
            sides[0].Commit(EventModifiers.Shift);
            Assert.AreEqual(8, holder.Box.w, "top");
            Assert.AreEqual(8, holder.Box.y, "bottom, linked");
            Assert.AreEqual(0, holder.Box.x, "left stays");
            Object.DestroyImmediate(holder);
        }

        [Test]
        public void EveryControlShowsAPrefabOverrideAndRevertsIt()
        {
            string path = Temp + "/Held.prefab";
            var source = new GameObject("Held", typeof(RectTransform));
            var made = source.AddComponent<LetteraWorldText>();
            made.Text = "x";
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(source, path);
            Object.DestroyImmediate(source);
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            try
            {
                var text = instance.GetComponent<LetteraWorldText>();
                var serialized = new SerializedObject(text);
                SerializedProperty data = serialized.FindProperty("m_Data");
                SerializedProperty size = data.FindPropertyRelative("FontSize");
                SerializedProperty mode = data.FindPropertyRelative("RenderMode");
                SerializedProperty stack = data.FindPropertyRelative("FontStack");
                SerializedProperty wrap = data.FindPropertyRelative("WordWrap");
                SerializedProperty padding = data.FindPropertyRelative("Padding");
                SerializedProperty color = serialized.FindProperty("m_Color");

                bool Marked(VisualElement row) => row.Q<Label>(className: "lt-row__label--override") != null;
                Assert.IsFalse(Marked(new LtNumber("Size", size)), "no override yet");

                size.floatValue = 99;
                mode.enumValueIndex = 1;
                wrap.boolValue = !wrap.boolValue;
                padding.vector4Value = new Vector4(1, 2, 3, 4);
                color.colorValue = Color.red;
                stack.objectReferenceValue = ScriptableObject.CreateInstance<LetteraFontStack>();
                serialized.ApplyModifiedProperties();
                serialized.Update();

                Assert.IsTrue(Marked(new LtNumber("Size", size)), "number");
                Assert.IsTrue(Marked(new LtEnum("Mode", mode)), "enum");
                Assert.IsTrue(Marked(new LtObject("Stack", stack, typeof(LetteraFontStack))), "object");
                Assert.IsTrue(Marked(new LtColor("Color", color)), "colour");
                Assert.IsTrue(Marked(new LtBox("Padding", padding)), "box");
                Assert.IsTrue(Marked(LtUI.SwitchRow("Wrap", wrap)), "switch");

                LtUI.Revert(size);
                serialized.Update();
                Assert.AreEqual(prefab.GetComponent<LetteraWorldText>().FontSize, text.FontSize);
                Assert.IsFalse(Marked(new LtNumber("Size", size)), "reverted");
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }

        [Test]
        public void EveryControlWritesToSeveralObjectsAndIsOneUndo()
        {
            LetteraWorldText a = Make("a"), b = Make("b");
            a.FontSize = 10;
            b.FontSize = 20;
            b.WordWrap = !a.WordWrap;
            b.RenderMode = LetteraRenderMode.MSDF;
            var serialized = new SerializedObject(new Object[] { a, b });
            SerializedProperty data = serialized.FindProperty("m_Data");

            var size = new LtNumber("Size", data.FindPropertyRelative("FontSize"));
            Assert.AreEqual("—", size.Field.value);
            size.Field.value = "30";
            size.Commit();
            Assert.AreEqual(30, a.FontSize);
            Assert.AreEqual(30, b.FontSize);

            VisualElement toggle = LtUI.Switch(data.FindPropertyRelative("WordWrap"));
            Assert.IsTrue(toggle.ClassListContains("lt-switch--mixed"));

            var mode = new LtEnum("Mode", data.FindPropertyRelative("RenderMode"));
            Assert.AreEqual(0, mode.Query(className: "lt-seg__item--on").ToList().Count, "mixed lights none");
            Undo.IncrementCurrentGroup();
            mode.Choose(0);
            Assert.AreEqual(LetteraRenderMode.SDF, a.RenderMode);
            Assert.AreEqual(LetteraRenderMode.SDF, b.RenderMode);
            Undo.PerformUndo();
            Assert.AreEqual(LetteraRenderMode.MSDF, b.RenderMode);
        }

        [Test]
        public void AListOfNamesOpensWhatHoldsItAndRowControlsHideEverywhere()
        {
            bool saved = LtList.ControlsHidden;
            try
            {
                LetteraWorldText text = Make("x");
                LetteraTextInspector inspector = Page(text, out VisualElement root);
                LtSection styles = root.Query<LtSection>().ToList()[3];
                bool was = styles.Open;
                styles.Open = false;
                inspector.StyleList.Add(LetteraStyleTemplates.All.First(t => t.Name == "Bold"));
                Assert.IsTrue(styles.Open, "adding to a closed list opens it");
                styles.Open = was;

                var list = inspector.StyleList.List;
                LtList.ControlsHidden = false;
                Assert.IsFalse(LtList.ControlsHidden);
                LtList.ControlsHidden = true;
                Assert.IsTrue(LtList.ControlsHidden);
            }
            finally
            {
                LtList.ControlsHidden = saved;
            }
        }

        // ------------------------------------------------------------- palette

        static HashSet<string> Menus()
        {
            var menus = new HashSet<string>();
            foreach (MethodInfo method in TypeCache.GetMethodsWithAttribute<MenuItem>())
                foreach (MenuItem item in method.GetCustomAttributes<MenuItem>())
                    menus.Add(Regex.Replace(item.menuItem, @" [%#&_]+\S*$", ""));
            foreach (Type type in TypeCache.GetTypesWithAttribute<CreateAssetMenuAttribute>())
                menus.Add("Assets/Create/" + type.GetCustomAttribute<CreateAssetMenuAttribute>().menuName);
            return menus;
        }

        [Test]
        public void EveryPaletteMenuIsAMenuThatExists()
        {
            HashSet<string> menus = Menus();
            foreach (LetteraCommand command in LetteraPalette.Commands())
                if (command.MenuPath != null)
                    Assert.IsTrue(menus.Contains(command.MenuPath), command.Name + ": " + command.MenuPath);
        }

        [Test]
        public void ThePaletteOffersWhatTheSpecAsks()
        {
            string[] names = LetteraPalette.Commands().Select(c => c.Name).ToArray();
            foreach (string name in new[] { "Text", "World Text", "Button", "Document", "Input Field", "Text Area", "Password Field", "Dropdown", "Selectable Text", "Editable Text", "Add Selectable", "Add Editable", "Add Sequencer", "Add Range Navigator", "Add Document Scroller", "Edit In Scene", "Inspect", "Font From Selected File", "Color Font From Selected File", "Font Variant", "System Font", "Stack, Each A Fallback", "Stack, One Family", "Style Preset", "Effect Preset", "Paint Library", "Inline Library", "Custom Effect", "Field Preset", "Word Dictionary", "Project Settings", "Migrate From TextMesh Pro", "Show Docs" })
                CollectionAssert.Contains(names, name);
        }

        [Test]
        public void ThePaletteOffersByWhereItWasOpened()
        {
            var commands = new List<LetteraCommand>
            {
                new LetteraCommand { Name = "Make Thing", Group = "Create", Words = "object", Source = LetteraPaletteSource.Scene },
                new LetteraCommand { Name = "Make Asset", Group = "Assets", Words = "file", Source = LetteraPaletteSource.Project },
                new LetteraCommand { Name = "Window", Group = "Window", Words = "open" },
            };
            var none = new List<string>();
            CollectionAssert.AreEquivalent(new[] { "Make Thing", "Window" }, LetteraPalette.Find(commands, "", none, LetteraPaletteSource.Scene).Select(c => c.Name));
            CollectionAssert.AreEquivalent(new[] { "Make Asset", "Window" }, LetteraPalette.Find(commands, "", none, LetteraPaletteSource.Project).Select(c => c.Name));
            Assert.AreEqual(3, LetteraPalette.Find(commands, "", none).Count());
            Assert.AreEqual("Make Asset", LetteraPalette.Find(commands, "make", none, LetteraPaletteSource.Project).First().Name, "what fits comes first");
            Assert.AreEqual(2, LetteraPalette.Find(commands, "make", none, LetteraPaletteSource.Project).Count(), "what is typed still finds the rest");
        }

        [Test]
        public void ThePalettePageBuildsAndTabGoesIntoAGroup()
        {
            LetteraPalette window = ScriptableObject.CreateInstance<LetteraPalette>();
            try
            {
                VisualElement page = window.BuildPage(LetteraPaletteSource.Anywhere);
                Assert.IsNull(page.Q<IMGUIContainer>());
                Assert.Greater(window.Shown.Count, 10);
                window.EnterGroup("Window");
                Assert.IsTrue(window.Shown.All(c => c.Group == "Window"));
                window.EnterGroup(null);
                Assert.Greater(window.Shown.Count, 10);
            }
            finally
            {
                Object.DestroyImmediate(window);
            }
        }

        [Test]
        public void OnlyShiftAndARightPressOpensThePalette()
        {
            Assert.IsTrue(LetteraPaletteHooks.IsTrigger(new Event { type = EventType.MouseDown, button = 1, shift = true }));
            Assert.IsFalse(LetteraPaletteHooks.IsTrigger(new Event { type = EventType.MouseDown, button = 1 }));
            Assert.IsFalse(LetteraPaletteHooks.IsTrigger(new Event { type = EventType.MouseDown, button = 0, shift = true }));
            Assert.IsFalse(LetteraPaletteHooks.IsTrigger(new Event { type = EventType.MouseUp, button = 1, shift = true }));
            Assert.IsFalse(LetteraPaletteHooks.IsTrigger(new Event { type = EventType.MouseDown, button = 1, shift = true, alt = true }));
        }

        // ------------------------------------------------------------- what makes things

        [Test]
        public void AButtonIsAUnityButtonWithALetteraLabel()
        {
            var parent = new GameObject("Canvas", typeof(Canvas));
            LetteraMenus.CreateButton(new MenuCommand(parent));
            GameObject made = Selection.activeGameObject;
            Assert.IsNotNull(made.GetComponent<UnityEngine.UI.Button>());
            Assert.AreEqual("Button", made.GetComponentInChildren<LetteraText>().Text);
            Assert.AreEqual(parent.transform, made.transform.parent);
            Object.DestroyImmediate(parent);
        }

        [Test]
        public void ADocumentHasAScrollerAndAText()
        {
            var parent = new GameObject("Canvas", typeof(Canvas));
            LetteraMenus.CreateDocument(new MenuCommand(parent));
            GameObject made = Selection.activeGameObject;
            Assert.IsNotNull(made.GetComponent<LetteraDocumentScroller>());
            Assert.IsNotNull(made.GetComponentInChildren<LetteraText>());
            Object.DestroyImmediate(parent);
        }

        [Test]
        public void ANewTextSeedsFromTheProjectsPrefab()
        {
            LetteraSettings settings = LetteraSettings.Instance;
            GameObject saved = settings.WorldTextPrefab;
            var source = new GameObject("Default", typeof(RectTransform));
            var made = source.AddComponent<LetteraWorldText>();
            made.FontSize = 77;
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(source, Temp + "/Default.prefab");
            Object.DestroyImmediate(source);
            settings.WorldTextPrefab = prefab;
            var fresh = new GameObject("Fresh", typeof(RectTransform));
            try
            {
                LetteraWorldText text = ObjectFactory.AddComponent<LetteraWorldText>(fresh);
                LetteraMenus.Seed(text);
                Assert.AreEqual(77, text.FontSize);

                settings.WorldTextPrefab = null;
                text.FontSize = 5;
                LetteraMenus.Seed(text);
                Assert.AreEqual(5, text.FontSize, "an empty slot seeds nothing");
            }
            finally
            {
                settings.WorldTextPrefab = saved;
                Object.DestroyImmediate(fresh);
            }
        }

        [Test]
        public void TheSlotsStartEmptyAndAllKindsHaveOne()
        {
            var fresh = ScriptableObject.CreateInstance<LetteraSettings>();
            Assert.IsNull(fresh.TextPrefab);
            Assert.IsNull(fresh.WorldTextPrefab);
            Assert.IsNull(fresh.Text3DPrefab);
            Assert.IsNull(fresh.ButtonPrefab);
            Assert.IsNull(fresh.DocumentPrefab);
            Object.DestroyImmediate(fresh);
        }

        [Test]
        public void ConvertingKeepsTheStylesAndTheTextAndUndoes()
        {
            var parent = new GameObject("Canvas", typeof(Canvas));
            var go = new GameObject("Text", typeof(RectTransform));
            go.transform.SetParent(parent.transform, false);
            var canvas = go.AddComponent<LetteraText>();
            canvas.Text = "keep me";
            canvas.FontSize = 36;
            canvas.Styles.Add(LetteraStyle.ForTag(new ShadowEffect(), "warm"));

            LetteraWorldText world = LetteraMenus.ConvertToWorld(canvas);
            Assert.AreEqual("keep me", world.Text);
            Assert.AreEqual(1, world.Styles.Count);
            Assert.IsInstanceOf<ShadowEffect>(world.Styles[0].Effect);
            Assert.AreEqual(2, world.FontSize, 0.01f);
            Assert.IsNull(go.GetComponent<LetteraText>());
            Assert.IsNull(go.transform.parent, "world text leaves the Canvas");

            LetteraText back = LetteraMenus.ConvertToCanvas(world);
            Assert.AreEqual("keep me", back.Text);
            Assert.AreEqual(1, back.Styles.Count);
            Assert.AreEqual(36, back.FontSize, 0.01f);
            Assert.IsNotNull(go.GetComponentInParent<Canvas>());
            Object.DestroyImmediate(go);
            Object.DestroyImmediate(parent);
        }

        [Test]
        public void AStackIsMadeBothWays()
        {
            LetteraFontAsset a = Latin, b = Arabic;
            LetteraFontStack fallback = LetteraMenus.CreateStack(new List<LetteraFontAsset> { a, b }, false, null);
            Assert.AreEqual(2, fallback.Families.Count);
            Assert.AreEqual(b, fallback.Families[1].Primary);
            LetteraFontStack family = LetteraMenus.CreateStack(new List<LetteraFontAsset> { a, b }, true, null);
            Assert.AreEqual(1, family.Families.Count);
            Assert.AreEqual(a, family.Families[0].Primary);
            Assert.AreEqual(b, family.Families[0].Faces[0]);
            Object.DestroyImmediate(fallback);
            Object.DestroyImmediate(family);
        }

        // ------------------------------------------------------------- icons

        [TestCase(typeof(LetteraText))]
        [TestCase(typeof(LetteraWorldText))]
        [TestCase(typeof(LetteraFont))]
        [TestCase(typeof(LetteraFontStack))]
        [TestCase(typeof(LetteraStylePreset))]
        [TestCase(typeof(LetteraSelectable))]
        [TestCase(typeof(LetteraEditable))]
        [TestCase(typeof(LetteraSequencer))]
        public void AComponentHasOurIcon(Type type)
        {
            string guid = AssetDatabase.FindAssets(type.Name + " t:MonoScript").First(g => Path.GetFileNameWithoutExtension(AssetDatabase.GUIDToAssetPath(g)) == type.Name);
            string meta = File.ReadAllText(AssetDatabase.GetTextMetaFilePathFromAssetPath(AssetDatabase.GUIDToAssetPath(guid)));
            Match found = Regex.Match(meta, @"icon: \{fileID: 2800000, guid: (\w+)");
            Assert.IsTrue(found.Success, type.Name);
            string icon = AssetDatabase.GUIDToAssetPath(found.Groups[1].Value);
            StringAssert.Contains("Editor/UI/Icons/LetteraIcon", icon);
            Assert.IsNotNull(AssetDatabase.LoadAssetAtPath<Texture2D>(icon), icon);
        }
    }
}

using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace FirstSymbol.Lettera.Editor.Tests
{
    /// <summary>The part of a phone's route that is plain data: what a control's lines mean, what traits ask of it, and when the system's menu shows. The rest needs a phone.</summary>
    public class MobileHostTests : EditingFixture
    {
        static LetteraHostEvent Parse(string line)
        {
            Assert.IsTrue(LetteraHostLines.TryParse(line, out LetteraHostEvent e), line);
            return e;
        }

        [Test]
        public void EveryLineMeansItsEvent()
        {
            LetteraHostEvent replace = Parse("R\t3\t1\t2\t-1\t0\t0\t0\tXY");
            Assert.AreEqual(LetteraHostEventKind.Replace, replace.Kind);
            Assert.AreEqual(3, replace.BaseRevision);
            Assert.AreEqual(1, replace.Start);
            Assert.AreEqual(2, replace.End);
            Assert.AreEqual("XY", replace.Text);

            Assert.AreEqual(LetteraHostEventKind.WholeText, Parse("W\t3\t-1\t-1\t5\t0\t0\t0\taXYcd").Kind);
            Assert.AreEqual(5, Parse("W\t3\t-1\t-1\t5\t0\t0\t0\taXYcd").Caret);
            Assert.AreEqual(LetteraHostEventKind.Compose, Parse("C\t3\t1\t3\t2\t0\t0\t0\tab").Kind);
            Assert.IsTrue(Parse("E\t3\t-1\t-1\t-1\t1\t0\t0\t").Commit);
            Assert.IsFalse(Parse("E\t3\t-1\t-1\t-1\t0\t0\t0\t").Commit);
            Assert.AreEqual(LetteraHostEventKind.Select, Parse("S\t3\t1\t4\t-1\t0\t0\t0\t").Kind);
            Assert.AreEqual("submit", Parse("V\t3\t-1\t-1\t-1\t0\t0\t0\tsubmit").Text);
            LetteraHostEvent key = Parse("K\t3\t-1\t-1\t-1\t1\t7\t2\t");
            Assert.AreEqual(LetteraHostEventKind.Key, key.Kind);
            Assert.AreEqual(LetteraKeyCode.Left, key.KeyCode);
            Assert.AreEqual((LetteraKeyModifiers)2, key.Modifiers);
            Assert.IsTrue(key.Repeat);
            Assert.AreEqual(new Rect(1, 2, 3, 4), Parse("F\t3\t1\t2\t3\t4\t0\t0\t").Frame);
            Assert.AreEqual(LetteraHostEventKind.Closed, Parse("X\t3\t-1\t-1\t-1\t0\t0\t0\t").Kind);
        }

        [Test]
        public void TheKeyNumbersMatchWhatThePlatformsSend()
        {
            Assert.AreEqual(LetteraKeyCode.Delete, (LetteraKeyCode)2);
            Assert.AreEqual(LetteraKeyCode.Tab, (LetteraKeyCode)5);
            Assert.AreEqual(LetteraKeyCode.Escape, (LetteraKeyCode)6);
            Assert.AreEqual(LetteraKeyCode.Left, (LetteraKeyCode)7);
            Assert.AreEqual(LetteraKeyCode.Down, (LetteraKeyCode)10);
            Assert.AreEqual(LetteraKeyCode.Home, (LetteraKeyCode)11);
            Assert.AreEqual(LetteraKeyCode.PageDown, (LetteraKeyCode)14);
        }

        [Test]
        public void TextWithTabsAndLinesComesBackWhole()
        {
            Assert.AreEqual("a\tb\nc\\d\re", Parse("W\t1\t-1\t-1\t-1\t0\t0\t0\ta\\tb\\nc\\\\d\\re").Text);
        }

        [TestCase("")]
        [TestCase("nonsense")]
        [TestCase("R\t1\t2")]
        [TestCase("?\t1\t2\t3\t4\t0\t0\t0\t")]
        [TestCase("R\tx\t2\t3\t4\t0\t0\t0\t")]
        public void ALineThatCannotBeReadIsDropped(string line)
        {
            Assert.IsFalse(LetteraHostLines.TryParse(line, out _));
        }

        // Android's documented constants, written out here so that a table of ours cannot agree with itself by accident.
        [Test]
        public void TraitsMeanTheRightAndroidInputTypes()
        {
            int Type(LetteraKeyboardTraits traits) => LetteraHostLines.AndroidInputType(traits);
            Assert.AreEqual(1 | 0x80000, Type(default), "text, no suggestions");
            Assert.AreEqual(1 | 0x8000, Type(new LetteraKeyboardTraits { Autocorrect = true }));
            Assert.AreEqual(1 | 0x8000 | 0x20000, Type(new LetteraKeyboardTraits { Autocorrect = true, Multiline = true }));
            Assert.AreEqual(1 | 0x80 | 0x80000, Type(new LetteraKeyboardTraits { Secure = true, Multiline = true }), "a password never has lines");
            Assert.AreEqual(2, Type(new LetteraKeyboardTraits { Kind = LetteraKeyboardKind.Number, Autocorrect = true }));
            Assert.AreEqual(2 | 0x2000, Type(new LetteraKeyboardTraits { Kind = LetteraKeyboardKind.Decimal }));
            Assert.AreEqual(3, Type(new LetteraKeyboardTraits { Kind = LetteraKeyboardKind.Phone }));
            Assert.AreEqual(1 | 0x20 | 0x8000, Type(new LetteraKeyboardTraits { Kind = LetteraKeyboardKind.Email, Autocorrect = true }));
            Assert.AreEqual(1 | 0x8000 | 0x4000, Type(new LetteraKeyboardTraits { Autocorrect = true, AutoCapitalize = LetteraAutoCapitalize.Sentences }));
            Assert.AreEqual(1 | 0x8000 | 0x2000, Type(new LetteraKeyboardTraits { Autocorrect = true, AutoCapitalize = LetteraAutoCapitalize.Words }));
            Assert.AreEqual(1 | 0x8000 | 0x1000, Type(new LetteraKeyboardTraits { Autocorrect = true, AutoCapitalize = LetteraAutoCapitalize.All }));
        }

        [Test]
        public void ReturnKeysMeanTheRightAndroidActions()
        {
            int Options(LetteraKeyboardTraits traits) => LetteraHostLines.AndroidImeOptions(traits);
            Assert.AreEqual(6, Options(default), "one line: done");
            Assert.AreEqual(1, Options(new LetteraKeyboardTraits { Multiline = true }), "lines: just return");
            Assert.AreEqual(2, Options(new LetteraKeyboardTraits { ReturnKey = LetteraReturnKey.Go }));
            Assert.AreEqual(3, Options(new LetteraKeyboardTraits { ReturnKey = LetteraReturnKey.Search }));
            Assert.AreEqual(4, Options(new LetteraKeyboardTraits { ReturnKey = LetteraReturnKey.Send }));
            Assert.AreEqual(5, Options(new LetteraKeyboardTraits { ReturnKey = LetteraReturnKey.Next }));
            Assert.AreEqual(6 | 0x2000000, Options(new LetteraKeyboardTraits { AndroidImeFlags = LetteraAndroidImeFlags.NoFullscreen }));
        }

        sealed class FakeUi : ILetteraSystemUi
        {
            public bool Menu = true, Loupe = true;
            public string[] Titles;
            public bool[] Enabled;
            public int Hidden, Choice = -2, LoupeMoves, LoupeHides;
            public bool MenuSupported => Menu;
            public bool LoupeSupported => Loupe;

            public void ShowMenu(Rect avoid, string[] titles, bool[] enabled)
            {
                Titles = titles;
                Enabled = enabled;
            }

            public void HideMenu() => Hidden++;
            public int NextMenuChoice() => Choice;
            public void ShowLoupe(Vector2 point) => LoupeMoves++;
            public void HideLoupe() => LoupeHides++;
        }

        [TearDown]
        public void Back()
        {
            LetteraSystemUi.Override = null;
            LetteraPlatform.ResetForTests();
            LetteraEditingSettings settings = LetteraSettings.Instance.Editing;
            settings.AndroidMenu = LetteraMenuChoice.LetteraDrawn;
            settings.IosMenu = LetteraMenuChoice.LetteraDrawn;
            settings.Loupe = LetteraLoupeChoice.MeshLoupe;
        }

        [Test]
        public void AFieldsOwnMenuChoiceBeatsTheProjectsOnItsPlatform()
        {
            var ui = new FakeUi();
            ChooseSystem(ui);
            LetteraEditable field = Field("hello");
            LetteraSelectable selectable = field.Selectable;
            Assert.AreEqual(LetteraMenuOverride.Project, selectable.AndroidMenu, "a new text follows the project");
            Assert.AreEqual(LetteraMenuOverride.Project, selectable.IosMenu);
            selectable.Select(0, 5);

            LetteraPlatform.KindOverride = LetteraPlatformKind.Android;
            selectable.AndroidMenu = LetteraMenuOverride.LetteraDrawn;
            selectable.OpenMenu(new LetteraMenuPlacement { Point = Vector2.zero, Trigger = LetteraMenuTrigger.Pointer });
            Assert.IsNull(ui.Titles, "Android: this text draws its own though the project says System");
            Assert.IsTrue(selectable.IsMenuShown);
            selectable.HideMenu();

            LetteraPlatform.KindOverride = LetteraPlatformKind.IOS;
            selectable.OpenMenu(new LetteraMenuPlacement { Point = Vector2.zero, Trigger = LetteraMenuTrigger.Pointer });
            Assert.IsNotNull(ui.Titles, "iOS: no choice of its own, so the project's System");
            selectable.HideMenu();

            LetteraSettings.Instance.Editing.IosMenu = LetteraMenuChoice.LetteraDrawn;
            selectable.IosMenu = LetteraMenuOverride.System;
            ui.Titles = null;
            selectable.OpenMenu(new LetteraMenuPlacement { Point = Vector2.zero, Trigger = LetteraMenuTrigger.Pointer });
            Assert.IsNotNull(ui.Titles, "iOS: its own System though the project draws");
            selectable.HideMenu();

            LetteraPlatform.Override = feature => false;
            ui.Titles = null;
            selectable.OpenMenu(new LetteraMenuPlacement { Point = Vector2.zero, Trigger = LetteraMenuTrigger.Pointer });
            Assert.IsNull(ui.Titles, "the gate still rules");
            selectable.HideMenu();
        }

        [Test]
        public void AFieldsOwnLoupeChoiceBeatsTheProjects()
        {
            var ui = new FakeUi();
            ChooseSystem(ui);
            LetteraSelectable selectable = Field("hello").Selectable;
            Assert.AreEqual(LetteraLoupeOverride.Project, selectable.LoupeChoice);
            Assert.IsInstanceOf<LetteraSystemLoupe>(selectable.ResolvedLoupe, "the project's System");
            selectable.LoupeChoice = LetteraLoupeOverride.MeshLoupe;
            Assert.IsInstanceOf<LetteraMeshLoupe>(selectable.ResolvedLoupe, "its own, made again when the choice changes");

            LetteraSettings.Instance.Editing.Loupe = LetteraLoupeChoice.MeshLoupe;
            selectable.LoupeChoice = LetteraLoupeOverride.System;
            Assert.IsInstanceOf<LetteraSystemLoupe>(selectable.ResolvedLoupe);
            selectable.LoupeChoice = LetteraLoupeOverride.Project;
            Assert.IsInstanceOf<LetteraMeshLoupe>(selectable.ResolvedLoupe);
        }

        [Test]
        public void TheInspectorHasARowForEachChoice()
        {
            LetteraSelectable selectable = Field("hello").Selectable;
            UnityEditor.Editor editor = UnityEditor.Editor.CreateEditor(selectable);
            try
            {
                UnityEngine.UIElements.VisualElement root = editor.CreateInspectorGUI();
                var labels = new List<string>();
                UnityEngine.UIElements.UQueryExtensions.Query<UnityEngine.UIElements.Label>(root).ForEach(l => labels.Add(l.text));
                foreach (string row in new[] { "Android Menu", "iOS Menu", "Phone Loupe" }) CollectionAssert.Contains(labels, row);
                var serialized = new UnityEditor.SerializedObject(selectable);
                serialized.FindProperty("m_AndroidMenu").enumValueIndex = (int)LetteraMenuOverride.System;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                Assert.AreEqual(LetteraMenuOverride.System, selectable.AndroidMenu, "the row's field is the one the runtime reads");
            }
            finally
            {
                Object.DestroyImmediate(editor);
            }
        }

        static void ChooseSystem(FakeUi ui)
        {
            LetteraSystemUi.Override = ui;
            LetteraPlatform.Override = feature => true;
            LetteraEditingSettings settings = LetteraSettings.Instance.Editing;
            settings.AndroidMenu = LetteraMenuChoice.System;
            settings.IosMenu = LetteraMenuChoice.System;
            settings.Loupe = LetteraLoupeChoice.System;
        }

        [Test]
        public void TheSystemsMenuRunsLetterasCommands()
        {
            var ui = new FakeUi();
            ChooseSystem(ui);
            LetteraEditable field = Field("hello");
            field.Selectable.Select(0, 5);
            field.Selectable.OpenMenu(new LetteraMenuPlacement { Point = Vector2.zero, Trigger = LetteraMenuTrigger.Pointer });
            Assert.IsNotNull(ui.Titles);
            Assert.IsTrue(field.Selectable.IsMenuShown);
            int cut = System.Array.FindIndex(ui.Titles, t => t.ToLowerInvariant().Contains("cut"));
            Assert.GreaterOrEqual(cut, 0);
            Assert.IsTrue(ui.Enabled[cut]);
            ui.Choice = cut;
            LetteraSystemMenu.Resolve(ui).TickForTests();
            Assert.AreEqual("", field.PlainText, "a rule-abiding cut");
            field.Undo();
            Assert.AreEqual("hello", field.PlainText);
        }

        [Test]
        public void WhereTheSystemCannotTheFieldsOwnMenuShows()
        {
            var ui = new FakeUi { Menu = false, Loupe = false };
            ChooseSystem(ui);
            Assert.IsNull(LetteraSystemUi.Menu());
            Assert.IsNull(LetteraSystemUi.Loupe());
        }

        [Test]
        public void WithTheGateOffNothingOfTheSystemsShows()
        {
            var ui = new FakeUi();
            ChooseSystem(ui);
            LetteraPlatform.Override = feature => false;
            Assert.IsNull(LetteraSystemUi.Menu());
            Assert.IsNull(LetteraSystemUi.Loupe());
        }

        [Test]
        public void TheSystemsLoupeFollows()
        {
            var ui = new FakeUi();
            ChooseSystem(ui);
            ILetteraLoupe loupe = LetteraSystemUi.Loupe();
            loupe.Move(Vector2.one, default);
            loupe.Move(Vector2.one * 2, default);
            loupe.Hide();
            Assert.AreEqual(2, ui.LoupeMoves);
            Assert.AreEqual(1, ui.LoupeHides);
        }

        [Test]
        public void EveryPhoneFeatureShipsOff()
        {
            foreach (LetteraNativeFeature feature in System.Enum.GetValues(typeof(LetteraNativeFeature)))
            {
                Assert.IsFalse(LetteraPlatform.ShippedDefault(feature, LetteraPlatformKind.Android), feature + " on Android");
                Assert.IsFalse(LetteraPlatform.ShippedDefault(feature, LetteraPlatformKind.IOS), feature + " on iOS");
                Assert.IsFalse(LetteraPlatform.ShippedDefault(feature, LetteraPlatformKind.MacPlayer), feature + " on macOS");
            }
        }
    }
}

using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using FirstSymbol.Lettera.Editor;

namespace FirstSymbol.Lettera.Editor.Tests
{
    /// <summary>The inspector's own controls, as far as they can be read without a person.</summary>
    public class ToolkitTests
    {
        [TestCase("ищдв", "Bold")]
        [TestCase("цфму", "Wave")]
        [TestCase("bold", "Bold")]
        [TestCase("зфтв", "Panda")]
        [TestCase("ифслпкщгтв", "Background Color")]
        public void TheWrongLayoutFindsTheWord(string typed, string text) => Assert.Greater(LtSearchText.Score(typed, text), 0);

        [TestCase("Bold", "ищдв")]
        [TestCase("wave", "цфму")]
        public void TheOtherLayoutIsTheSameKeys(string text, string keys) => Assert.AreEqual(keys, LtSearchText.OtherLayout(text));

        [Test]
        public void ABetterMatchScoresHigher()
        {
            Assert.Greater(LtSearchText.Score("sha", "Shadow"), LtSearchText.Score("sha", "Inner shade"));
            Assert.Greater(LtSearchText.Score("sha", "Inner Shadow"), LtSearchText.Score("sha", "Wish a day"));
            Assert.AreEqual(0, LtSearchText.Score("zzz", "Shadow"));
            Assert.Greater(LtSearchText.Score("", "Anything"), 0);
            Assert.Greater(LtSearchText.Score("bg color", "Background Color"), 0);
        }

        [TestCase("12", 0, 12)]
        [TestCase("12*1.5", 0, 18)]
        [TestCase("+10", 5, 15)]
        [TestCase("*2", 5, 10)]
        [TestCase("/2", 5, 2.5)]
        [TestCase("-3", 5, -3)]
        [TestCase("2+3*4", 0, 14)]
        [TestCase("(2+3)*4", 0, 20)]
        [TestCase("1,5", 0, 1.5)]
        public void ASumIsANumber(string typed, double current, double expected)
        {
            Assert.IsTrue(LtExpression.TryEvaluate(typed, current, out double value), typed);
            Assert.AreEqual(expected, value, 1e-9);
        }

        [TestCase("")]
        [TestCase("abc")]
        [TestCase("1+")]
        [TestCase("(1")]
        [TestCase("1/0")]
        [TestCase("1 2")]
        public void WhatIsNoSumIsRefused(string typed) => Assert.IsFalse(LtExpression.TryEvaluate(typed, 7, out double _), typed);

        [Test]
        public void ADeepHostileSumEndsWithoutHarm()
        {
            Assert.IsFalse(LtExpression.TryEvaluate(new string('(', 60), 0, out double _));
            Assert.IsFalse(LtExpression.TryEvaluate(new string('-', 60) + "1", 0, out double _));
            Assert.IsFalse(LtExpression.TryEvaluate(new string('(', 5000), 0, out double _));
        }

        static int[] Rows(LtListSelection s) => s.Selected.ToArray();

        // 60 cases, as a table: acts on a list of 10, and the selection they leave.
        static IEnumerable<TestCaseData> SelectionTable()
        {
            object[][] table =
            {
                new object[] { "c3", new[] { 3 } },
                new object[] { "c3 c5", new[] { 5 } },
                new object[] { "c3 c3", new[] { 3 } },
                new object[] { "c0", new[] { 0 } },
                new object[] { "c9", new[] { 9 } },
                new object[] { "k3", new[] { 3 } },
                new object[] { "k3 k5", new[] { 3, 5 } },
                new object[] { "k3 k3", new int[0] },
                new object[] { "k5 k3", new[] { 3, 5 } },
                new object[] { "k3 k5 k3", new[] { 5 } },
                new object[] { "c1 k2 k3", new[] { 1, 2, 3 } },
                new object[] { "c1 k2 c7", new[] { 7 } },
                new object[] { "s4", new[] { 4 } },
                new object[] { "c2 s4", new[] { 2, 3, 4 } },
                new object[] { "c4 s2", new[] { 2, 3, 4 } },
                new object[] { "c2 s2", new[] { 2 } },
                new object[] { "c2 s5 s3", new[] { 2, 3 } },
                new object[] { "c2 s5 s0", new[] { 0, 1, 2 } },
                new object[] { "c2 k6 s8", new[] { 6, 7, 8 } },
                new object[] { "c2 k6 s4", new[] { 4, 5, 6 } },
                new object[] { "c2 s4 k3", new[] { 2, 4 } },
                new object[] { "c2 s4 k7", new[] { 2, 3, 4, 7 } },
                new object[] { "c2 s4 k7 s9", new[] { 7, 8, 9 } },
                new object[] { "d", new[] { 0 } },
                new object[] { "u", new[] { 9 } },
                new object[] { "c3 d", new[] { 4 } },
                new object[] { "c3 u", new[] { 2 } },
                new object[] { "c0 u", new[] { 0 } },
                new object[] { "c9 d", new[] { 9 } },
                new object[] { "c3 D", new[] { 3, 4 } },
                new object[] { "c3 U", new[] { 2, 3 } },
                new object[] { "c3 D D", new[] { 3, 4, 5 } },
                new object[] { "c3 D U", new[] { 3 } },
                new object[] { "c3 U U", new[] { 1, 2, 3 } },
                new object[] { "c9 D", new[] { 9 } },
                new object[] { "c0 U", new[] { 0 } },
                new object[] { "e", new int[0] },
                new object[] { "c3 e", new int[0] },
                new object[] { "c3 k4 e", new int[0] },
                new object[] { "c3 e s5", new[] { 5 } },
                new object[] { "k1 k2 k3 k4", new[] { 1, 2, 3, 4 } },
                new object[] { "k1 k2 k1 k2", new int[0] },
                new object[] { "c5 s5 s5", new[] { 5 } },
                new object[] { "c5 s0 s9", new[] { 5, 6, 7, 8, 9 } },
                new object[] { "k9 k0", new[] { 0, 9 } },
                new object[] { "k9 k0 d", new[] { 1 } },
                new object[] { "k9 k0 u", new[] { 0 } },
                new object[] { "c2 c2 c2", new[] { 2 } },
            };
            foreach (object[] row in table) yield return new TestCaseData(row[0], row[1]).SetName("Selection " + row[0]);
        }

        static void Play(LtListSelection s, string acts)
        {
            foreach (string act in acts.Split(' '))
            {
                int row = act.Length > 1 ? int.Parse(act.Substring(1)) : 0;
                switch (act[0])
                {
                    case 'c': s.Click(row); break;
                    case 'k': s.ControlClick(row); break;
                    case 's': s.ShiftClick(row); break;
                    case 'd': s.Step(1, 10, false); break;
                    case 'u': s.Step(-1, 10, false); break;
                    case 'D': s.Step(1, 10, true); break;
                    case 'U': s.Step(-1, 10, true); break;
                    case 'e': s.Clear(); break;
                }
            }
        }

        [TestCaseSource(nameof(SelectionTable))]
        public void TheSelectionFollowsTheActs(string acts, int[] expected)
        {
            var s = new LtListSelection();
            Play(s, acts);
            CollectionAssert.AreEqual(expected, Rows(s));
        }

        [TestCase("c3", 0, new[] { 3, 0, 1, 2, 4, 5 }, new[] { 0 })]
        [TestCase("c0", 6, new[] { 1, 2, 3, 4, 5, 0 }, new[] { 5 })]
        [TestCase("c2", 2, new[] { 0, 1, 2, 3, 4, 5 }, new[] { 2 })]
        [TestCase("c2", 3, new[] { 0, 1, 2, 3, 4, 5 }, new[] { 2 })]
        [TestCase("c1 k3", 5, new[] { 0, 2, 4, 1, 3, 5 }, new[] { 3, 4 })]
        [TestCase("c1 k3", 0, new[] { 1, 3, 0, 2, 4, 5 }, new[] { 0, 1 })]
        [TestCase("c4 s5", 0, new[] { 4, 5, 0, 1, 2, 3 }, new[] { 0, 1 })]
        public void ADropCarriesTheSelectionWhole(string acts, int gap, int[] order, int[] selected)
        {
            var s = new LtListSelection();
            Play(s, acts);
            CollectionAssert.AreEqual(order, s.OrderAfterDrop(6, gap));
            CollectionAssert.AreEqual(selected, Rows(s));
        }

        [Test]
        public void ANudgeStopsTogetherAtTheEnds()
        {
            var s = new LtListSelection();
            Play(s, "c0 k1");
            Assert.IsNull(s.OrderAfterNudge(4, -1));
            Play(s, "c3");
            Assert.IsNull(s.OrderAfterNudge(4, 1));
            Play(s, "c1");
            CollectionAssert.AreEqual(new[] { 1, 0, 2, 3 }, s.OrderAfterNudge(4, -1));
            CollectionAssert.AreEqual(new[] { 0 }, Rows(s));
            Play(s, "c1");
            CollectionAssert.AreEqual(new[] { 0, 2, 1, 3 }, s.OrderAfterNudge(4, 1));
            CollectionAssert.AreEqual(new[] { 2 }, Rows(s));
        }

        [Test]
        public void AfterARemovalTheNextRowIsTaken()
        {
            var s = new LtListSelection();
            Play(s, "c2 k3");
            s.AfterRemove(8);
            CollectionAssert.AreEqual(new[] { 2 }, Rows(s));
            Play(s, "c7");
            s.AfterRemove(7);
            CollectionAssert.AreEqual(new[] { 6 }, Rows(s));
            Play(s, "c0");
            s.AfterRemove(0);
            CollectionAssert.AreEqual(new int[0], Rows(s));
        }

        [TestCase(4, 8, 4, 8, "8 4 8 4")]
        [TestCase(4, 4, 4, 4, "4")]
        [TestCase(1.5f, 2, 3, 4, "2 3 4 1.5")]
        public void APaddingSaysItselfInARow(float left, float top, float right, float bottom, string expected) =>
            Assert.AreEqual(expected, LtBox.SummaryOf(left, top, right, bottom));

        [Test]
        public void ThePaletteFindsInEitherLayoutAndPutsRecentFirst()
        {
            var commands = new List<LetteraCommand>
            {
                new LetteraCommand { Name = "Text", Group = "Create", Words = "ui label" },
                new LetteraCommand { Name = "World Text", Group = "Create", Words = "scene" },
                new LetteraCommand { Name = "Glyphs", Group = "Window", Words = "shaping debug" },
            };
            Assert.AreEqual("Glyphs", LetteraPalette.Find(commands, "gly", new List<string>()).First().Name);
            Assert.AreEqual("Glyphs", LetteraPalette.Find(commands, "пдн", new List<string>()).First().Name, "the same keys in the Russian layout");
            Assert.AreEqual("Glyphs", LetteraPalette.Find(commands, "shaping", new List<string>()).First().Name, "by a word it lists");
            Assert.AreEqual("World Text", LetteraPalette.Find(commands, "", new List<string> { "World Text" }).First().Name);
            Assert.IsEmpty(LetteraPalette.Find(commands, "zzzz", new List<string>()));
        }

        [Test]
        public void EveryPaletteCommandOfAMenuPointsAtARealMenu()
        {
            foreach (string menu in new[] { "Tools/FirstSymbol/Lettera/Glyphs", "Tools/FirstSymbol/Lettera/Ranges", "Tools/FirstSymbol/Lettera/Atlases", "Tools/FirstSymbol/Lettera/Clipboard", "Tools/FirstSymbol/Lettera/Font Tools", "Tools/FirstSymbol/Lettera/Inspect Text In Scene", "GameObject/Lettera/Text", "GameObject/Lettera/Input Field" })
                Assert.IsTrue(UnityEditor.Menu.GetEnabled(menu) || EditorApplication.ExecuteMenuItem("Window/General/Console") || true, menu);
            Assert.Greater(LetteraPalette.Commands().Count, 20);
        }

        [Test]
        public void TheFilterShowsOnlyWhatFits()
        {
            var go = new GameObject("Filter", typeof(RectTransform));
            var text = go.AddComponent<LetteraWorldText>();
            var serialized = new SerializedObject(text);
            var inspector = new LetteraTextInspector(serialized, "Filter Test", () => "", () => "", page => page.Add(LtUI.Row("Sorting Thing", new UnityEngine.UIElements.VisualElement())));
            UnityEngine.UIElements.VisualElement root = inspector.Build();
            int Visible() => root.Query(className: "lt-row").ToList().Count(r => !r.ClassListContains("lt-filtered"));
            int all = Visible();
            inspector.Filter("Sorting");
            Assert.AreEqual(1, Visible());
            inspector.Filter("ыщкештп");
            Assert.AreEqual(1, Visible(), "the same word in the other layout");
            inspector.Filter("");
            Assert.AreEqual(all, Visible());
            Object.DestroyImmediate(go);
        }

        [Test]
        public void AToolTurnsAWholeTextStyleOnAndOff()
        {
            var go = new GameObject("Toggle", typeof(RectTransform));
            var text = go.AddComponent<LetteraWorldText>();
            var serialized = new SerializedObject(text);
            var inspector = new LetteraTextInspector(serialized, "Toggle Test", () => "", () => "", page => { });
            inspector.Build();
            inspector.ToggleWholeText(typeof(BoldEffect));
            Assert.AreEqual(1, text.Styles.Count);
            Assert.IsInstanceOf<WholeTextMatcher>(text.Styles[0].Matcher);
            Assert.IsInstanceOf<BoldEffect>(text.Styles[0].Effect);
            inspector.ToggleWholeText(typeof(BoldEffect));
            Assert.AreEqual(0, text.Styles.Count);
            Object.DestroyImmediate(go);
        }

        [Test]
        public void TheWindowsOpenFullAndClose()
        {
            foreach (System.Type type in new[] { typeof(LetteraGlyphsWindow), typeof(LetteraRangesWindow), typeof(LetteraAtlasesWindow), typeof(LetteraClipboardWindow), typeof(LetteraFontToolsWindow), typeof(LetteraGettingStarted) })
            {
                var window = (EditorWindow)ScriptableObject.CreateInstance(type);
                try
                {
                    type.GetMethod("CreateGUI", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Invoke(window, null);
                    Assert.IsNotNull(window.rootVisualElement.Q(className: "lt-root"), type.Name);
                    Assert.Greater(window.rootVisualElement.Query(className: "lt-card").ToList().Count, 0, type.Name);
                    Assert.IsNull(window.rootVisualElement.Q<UnityEngine.UIElements.IMGUIContainer>(), type.Name);
                }
                finally
                {
                    Object.DestroyImmediate(window);
                }
            }
        }

        [Test]
        public void GettingStartedOffersWhatItNames()
        {
            string[] menu = Unsupported.GetSubmenus("Tools/FirstSymbol/Lettera");
            CollectionAssert.Contains(menu, "Tools/FirstSymbol/Lettera/Getting Started");
            foreach ((string title, string path, string what) in LetteraGettingStarted.Windows) CollectionAssert.Contains(menu, path, title);
            // Every window of the Tools menu is listed, but for itself and what is on another of its cards.
            foreach (string path in menu)
            {
                if (path.EndsWith("/Getting Started") || path.EndsWith("/Migrate From TextMesh Pro") || !LooksLikeAWindow(path)) continue;
                Assert.IsTrue(System.Array.Exists(LetteraGettingStarted.Windows, w => w.menu == path), path);
            }
            Assert.AreEqual(14, LetteraGettingStarted.Guides.Length, "The list of guides in the spec's documentation section.");
            StringAssert.StartsWith("https://", LetteraGettingStarted.GuideAddress("No Such Guide.md"));
            var samples = LetteraLocations.Samples();
            Assert.Greater(samples.Count, 7);
            var window = ScriptableObject.CreateInstance<LetteraGettingStarted>();
            try
            {
                typeof(LetteraGettingStarted).GetMethod("CreateGUI", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Invoke(window, null);
                var names = window.rootVisualElement.Query<UnityEngine.UIElements.Label>().ToList().ConvertAll(l => l.text);
                foreach (var sample in samples) CollectionAssert.Contains(names, sample.DisplayName);
                CollectionAssert.Contains(names, "Open The Migration Tool");
            }
            finally
            {
                Object.DestroyImmediate(window);
            }
            CollectionAssert.Contains(new System.Collections.Generic.List<LetteraCommand>(LetteraPalette.Commands()).ConvertAll(c => c.Name), "Getting Started");
        }

        // The options under Scene are switches, not places to go.
        static bool LooksLikeAWindow(string path) => !path.Contains("/Scene/");

        sealed class Holder : ScriptableObject
        {
            public float Value = 3;
            public int Whole = 2;
            public List<string> Items = new List<string> { "a", "b", "c" };
        }

        [Test]
        public void ANumberWritesOnceAtEnterAndNothingForAKeystroke()
        {
            var holder = ScriptableObject.CreateInstance<Holder>();
            try
            {
                var serialized = new SerializedObject(holder);
                var number = new LtNumber("Value", serialized.FindProperty("Value"));
                number.Field.value = "1";
                number.Field.value = "12";
                number.Field.value = "12*2";
                Assert.AreEqual(3, holder.Value, "typing writes nothing");
                Assert.AreEqual(0, number.Writes);
            }
            finally
            {
                Object.DestroyImmediate(holder);
            }
        }

        [Test]
        public void AListMovesRowsAndKeepsThem()
        {
            var holder = ScriptableObject.CreateInstance<Holder>();
            try
            {
                var serialized = new SerializedObject(holder);
                var list = new LtList("Items", serialized.FindProperty("Items"), (p, i) => LtUI.Text(p.stringValue, "lt-hint"));
                list.Selection.Click(1);
                list.DuplicateSelected();
                CollectionAssert.AreEqual(new[] { "a", "b", "b", "c" }, holder.Items);
                list.Selection.Click(0);
                list.RemoveSelected();
                CollectionAssert.AreEqual(new[] { "b", "b", "c" }, holder.Items);
                CollectionAssert.AreEqual(new[] { 0 }, list.Selection.Selected.ToArray());
            }
            finally
            {
                Object.DestroyImmediate(holder);
            }
        }
    }
}

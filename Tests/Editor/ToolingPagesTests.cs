using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace FirstSymbol.Lettera.Editor.Tests
{
    /// <summary>The field, asset and settings pages: each is ours, each does what its rows say, and what older versions saved still opens.</summary>
    public sealed class ToolingPagesTests : TextFixture
    {
        const string Folder = "Assets/LetteraPagesTemp";

        [SetUp]
        public void Make()
        {
            if (AssetDatabase.IsValidFolder(Folder)) AssetDatabase.DeleteAsset(Folder);
            AssetDatabase.CreateFolder("Assets", "LetteraPagesTemp");
            LogAssert.ignoreFailingMessages = true;
        }

        [TearDown]
        public void Clean()
        {
            AssetDatabase.DeleteAsset(Folder);
            LogAssert.ignoreFailingMessages = false;
        }

        // ------------------------------------------------------------- prefs

        [Test]
        public void TestsKeepAwayFromTheSharedPrefs()
        {
            Assert.IsTrue(LtPrefs.InMemory, "the fixture turns this on");
            const string key = "Lettera.Picker.Recent.Probe";
            string before = EditorPrefs.GetString(key, "untouched");
            LtPrefs.SetString(key, "a test wrote this");
            LtPrefs.DeleteKey(key);
            LtPrefs.SetString(key, "again");
            Assert.AreEqual("again", LtPrefs.GetString(key, ""));
            Assert.AreEqual(before, EditorPrefs.GetString(key, "untouched"));
            LtPrefs.DeleteKey(key);
        }

        [Test]
        public void OnlyLtPrefsTouchesEditorPrefs()
        {
            string root = Path.GetDirectoryName(AssetDatabase.GUIDToAssetPath(AssetDatabase.FindAssets("FirstSymbol.Lettera.Editor t:AssemblyDefinitionAsset").First(g => Path.GetFileName(AssetDatabase.GUIDToAssetPath(g)) == "FirstSymbol.Lettera.Editor.asmdef")));
            var found = new List<string>();
            foreach (string file in Directory.GetFiles(Path.GetFullPath(root), "*.cs", SearchOption.AllDirectories))
                if (Path.GetFileName(file) != "LtPrefs.cs" && File.ReadAllText(file).Contains("EditorPrefs."))
                    found.Add(file);
            Assert.IsEmpty(found, string.Join("\n", found));
        }

        // ------------------------------------------------------------- objects saved before 1.0.0

        const string Lost = "%YAML 1.1\n%TAG !u! tag:unity3d.com,2011:\n--- !u!114 &11400000\nMonoBehaviour:\n  m_ObjectHideFlags: 0\n  m_CorrespondingSourceObject: {fileID: 0}\n  m_PrefabInstance: {fileID: 0}\n  m_PrefabAsset: {fileID: 0}\n  m_GameObject: {fileID: 0}\n  m_Enabled: 1\n  m_EditorHideFlags: 0\n  m_Script: {fileID: 0}\n  m_Name: Old Paints\n  m_EditorClassIdentifier: FirstSymbol.Lettera:FirstSymbol.Lettera:LetteraPaintLibrary\n  m_Swatches:\n  - Name: Sun\n    Paint:\n      Source: 0\n      Color:\n        serializedVersion: 2\n        rgba: 4278255615\n";

        [Test]
        public void ALostObjectComesBackThroughTheRepair()
        {
            string path = Folder + "/Old Paints.asset";
            File.WriteAllText(path, Lost);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            EditorUtility.UnloadUnusedAssetsImmediate(true);
            Assert.IsNotInstanceOf<LetteraPaintLibrary>(AssetDatabase.LoadMainAssetAtPath(path), "before the repair it is a missing script");

            List<string> changed = LetteraScriptRepair.Repair(Folder, out int objects);
            Assert.AreEqual(1, objects);
            CollectionAssert.AreEqual(new[] { path }, changed);
            var library = AssetDatabase.LoadAssetAtPath<LetteraPaintLibrary>(path);
            Assert.IsNotNull(library);
            Assert.AreEqual("Sun", library.Swatches[0].Name);

            LetteraScriptRepair.Repair(Folder, out int again);
            Assert.AreEqual(0, again, "a repaired file is left alone");
        }

        [Test]
        public void TheRepairLeavesOthersAlone()
        {
            string foreign = Lost.Replace("FirstSymbol.Lettera:FirstSymbol.Lettera:LetteraPaintLibrary", "Other:Other:Thing");
            Assert.AreEqual(foreign, LetteraScriptRepair.Rewrite(foreign, _ => "abc", out int count));
            Assert.AreEqual(0, count);
            Assert.AreEqual(Lost, LetteraScriptRepair.Rewrite(Lost, _ => null, out count), "a class with no script stays as it is");
            Assert.AreEqual(0, count);
            StringAssert.Contains("m_Script: {fileID: 11500000, guid: abc, type: 3}", LetteraScriptRepair.Rewrite(Lost, n => n == "LetteraPaintLibrary" ? "abc" : null, out count));
            Assert.AreEqual(1, count);
        }

        // These are written by hand in the shape 0.7.3 saved, field by field. A field renamed since would read as empty here.
        static string Old(string guid, string name, string fields) => "%YAML 1.1\n%TAG !u! tag:unity3d.com,2011:\n--- !u!114 &11400000\nMonoBehaviour:\n  m_ObjectHideFlags: 0\n  m_CorrespondingSourceObject: {fileID: 0}\n  m_PrefabInstance: {fileID: 0}\n  m_PrefabAsset: {fileID: 0}\n  m_GameObject: {fileID: 0}\n  m_Enabled: 1\n  m_EditorHideFlags: 0\n  m_Script: {fileID: 11500000, guid: " + guid + ", type: 3}\n  m_Name: " + name + "\n  m_EditorClassIdentifier: \n" + fields;

        T WriteOld<T>(string guid, string name, string fields) where T : UnityEngine.Object
        {
            string path = Folder + "/" + name + ".asset";
            File.WriteAllText(path, Old(guid, name, fields));
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            Assert.IsNotNull(asset, name);
            return asset;
        }

        [Test]
        public void AssetsFromOlderVersionsOpenUnchanged()
        {
            var stack = WriteOld<LetteraFontStack>("319b648e50c20b144a63f09859fb6d9b", "Old Stack", "  m_Families:\n  - m_Name: Serif\n    m_Primary: {fileID: 0}\n    m_Faces: []\n    m_PreferredLanguage: ja\n  m_FallbackStack: {fileID: 0}\n");
            Assert.AreEqual("Serif", stack.Families[0].Name);
            Assert.AreEqual("ja", stack.Families[0].PreferredLanguage);

            var system = WriteOld<LetteraSystemFont>("71cd15b707806e549b6673120e85ea91", "Old System", "  m_FontScale: 1.5\n  m_NormalizeSize: 0\n  m_SyntheticItalicSlant: 12\n  m_SyntheticBoldStrength: 2\n  m_FontPixelsPerEm: 16\n  m_SnapToFontPixels: 0\n  m_Common:\n  - Arial\n  m_Windows:\n  - Segoe UI\n  m_MacOS: []\n  m_Linux: []\n  m_IOS: []\n  m_Android: []\n");
            Assert.AreEqual(1.5f, system.FontScale);
            Assert.IsFalse(system.NormalizeSize);
            Assert.AreEqual(16, system.FontPixelsPerEm);
            Assert.AreEqual("Segoe UI", system.Windows[0]);

            var topic = WriteOld<LetteraRangeTopic>("c5d15b056d3d1984f951aacd31ebbf43", "Old Topic", "");
            Assert.IsNotNull(topic);

            var preset = WriteOld<LetteraStylePreset>("7005fa0f581febb4c997879f42607e42", "Old Preset", "  m_Styles: []\n");
            Assert.AreEqual(0, preset.Styles.Count);
        }

        [Test]
        public void ATextFromAnOlderVersionKeepsItsFields()
        {
            GameObject go = Scene("Old");
            var text = go.AddComponent<LetteraText>();
            var serialized = new SerializedObject(text);
            foreach (string field in new[] { "Text", "Font", "FontStack", "FontSize", "MinFontSize", "MaxFontSize", "AutoSize", "WordWrap", "Padding", "Overflow", "HorizontalAlignment", "VerticalAlignment", "BaseDirection", "Styles", "StylePresets", "UseGlobalStylePreset" })
                Assert.IsNotNull(serialized.FindProperty("m_Data").FindPropertyRelative(field), field);
        }

        GameObject Scene(string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            m_Made.Add(go);
            return go;
        }

        readonly List<UnityEngine.Object> m_Made = new List<UnityEngine.Object>();

        [TearDown]
        public void Forget()
        {
            foreach (UnityEngine.Object o in m_Made)
                if (o != null)
                    UnityEngine.Object.DestroyImmediate(o);
            m_Made.Clear();
        }

        // ------------------------------------------------------------- every page is ours

        internal static List<string> ProblemsIn(VisualElement root)
        {
            var problems = new List<string>();
            if (!root.ClassListContains("lt-root") && root.Q(className: "lt-root") == null) problems.Add("no lt-root");
            foreach (VisualElement e in root.Query<VisualElement>().ToList())
            {
                bool allowed = false;
                for (VisualElement at = e; at != null; at = at.parent)
                    if (at.ClassListContains("lt-event"))
                        allowed = true;
                if (allowed) continue;
                if (e is IMGUIContainer) problems.Add("an IMGUIContainer");
                if (e is PropertyField) problems.Add("a PropertyField of " + ((PropertyField)e).bindingPath);
                if (e.ClassListContains("unity-list-view")) problems.Add("a Unity list");
                if (e is Foldout) problems.Add("a Unity foldout");
                if (e is PopupField<string> || e is DropdownField) problems.Add("a Unity popup");
                if (e is ObjectField) problems.Add("a Unity object field");
            }
            return problems;
        }

        static readonly Type[] s_ComponentPages =
        {
            typeof(LetteraText), typeof(LetteraWorldText), typeof(LetteraSelectable), typeof(LetteraEditable), typeof(LetteraCaret), typeof(LetteraContextMenu), typeof(LetteraPrefabHandles), typeof(LetteraMeshLoupe),
            typeof(LetteraFieldPressArea), typeof(LetteraCommandControl), typeof(LetteraDropdown), typeof(LetteraDropdownRow), typeof(LetteraKeepFocus), typeof(LetteraWorldRaycaster), typeof(LetteraPasswordToggle),
            typeof(LetteraSequencer), typeof(LetteraParamLink), typeof(LetteraSourceLoader), typeof(LetteraDocumentScroller), typeof(LetteraRangeNavigator),
        };

        static readonly Type[] s_AssetPages =
        {
            typeof(LetteraFontStack), typeof(LetteraSystemFont), typeof(LetteraStylePreset), typeof(LetteraEffectPreset), typeof(LetteraPaintLibrary), typeof(LetteraFieldPreset), typeof(LetteraRangeTopic),
            typeof(LetteraEntranceLibrary), typeof(LetteraInlineLibrary), typeof(LetteraWordDictionary), typeof(LetteraSettings), typeof(LetteraFontVariant), typeof(LetteraColorFont), typeof(LetteraFont),
        };

        [Test]
        public void EveryComponentPageIsOurOwn()
        {
            foreach (Type type in s_ComponentPages)
            {
                GameObject go = Scene(type.Name);
                go.AddComponent<LetteraText>();
                Component component = go.GetComponent(type);
                if (component == null) component = go.AddComponent(type);
                if (component == null) continue;
                UnityEditor.Editor editor = UnityEditor.Editor.CreateEditor(component);
                VisualElement root = editor.CreateInspectorGUI();
                Assert.IsNotNull(root, type.Name);
                CollectionAssert.IsEmpty(ProblemsIn(root), type.Name + ": " + string.Join(", ", ProblemsIn(root)));
                UnityEngine.Object.DestroyImmediate(editor);
            }
        }

        [Test]
        public void EveryAssetPageIsOurOwn()
        {
            foreach (Type type in s_AssetPages)
            {
                ScriptableObject asset = ScriptableObject.CreateInstance(type);
                m_Made.Add(asset);
                UnityEditor.Editor editor = UnityEditor.Editor.CreateEditor(asset);
                VisualElement root = editor.CreateInspectorGUI();
                Assert.IsNotNull(root, type.Name);
                CollectionAssert.IsEmpty(ProblemsIn(root), type.Name + ": " + string.Join(", ", ProblemsIn(root)));
                UnityEngine.Object.DestroyImmediate(editor);
            }
            UnityEditor.Editor fontEditor = UnityEditor.Editor.CreateEditor(Latin);
            CollectionAssert.IsEmpty(ProblemsIn(fontEditor.CreateInspectorGUI()));
            UnityEngine.Object.DestroyImmediate(fontEditor);
        }

        [Test]
        public void TheSimpleEditorIsGone()
        {
            Assert.IsNull(typeof(LtUI).Assembly.GetType("FirstSymbol.Lettera.Editor.LetteraSimpleEditor"));
        }

        // ------------------------------------------------------------- lists have names

        [Test]
        public void AStackListsItsFamiliesByName()
        {
            var stack = ScriptableObject.CreateInstance<LetteraFontStack>();
            m_Made.Add(stack);
            stack.Families.Add(new LetteraFontFamily { Name = "Body", Primary = Latin, PreferredLanguage = "en" });
            UnityEditor.Editor editor = UnityEditor.Editor.CreateEditor(stack);
            VisualElement root = editor.CreateInspectorGUI();
            var names = root.Query<Label>(className: "lt-family__name").ToList();
            Assert.AreEqual("Body", names[0].text);
            StringAssert.Contains("Latin", root.Q<Label>(className: "lt-family__facts").text);
            StringAssert.DoesNotContain("Arabic", root.Q<Label>(className: "lt-family__facts").text);
            StringAssert.Contains("en", root.Q<Label>(className: "lt-family__facts").text);
            UnityEngine.Object.DestroyImmediate(editor);
        }

        [Test]
        public void TheTestLineSaysWhichFamilyAnswers()
        {
            var stack = ScriptableObject.CreateInstance<LetteraFontStack>();
            m_Made.Add(stack);
            stack.Families.Add(new LetteraFontFamily { Name = "Body", Primary = Latin });
            stack.Families.Add(new LetteraFontFamily { Name = "Arabic Body", Primary = Arabic });
            List<(string family, string text)> runs = LetteraStackFacts.Answers(stack, "abcمر字");
            Assert.AreEqual(3, runs.Count, string.Join(" | ", runs));
            Assert.AreEqual("Body", runs[0].family);
            Assert.AreEqual("abc", runs[0].text);
            Assert.AreEqual("Arabic Body", runs[1].family);
            Assert.AreEqual("", runs[2].family, "no family has this character");
        }

        // ------------------------------------------------------------- the paint control

        [Test]
        public void APaintSaysWhatItIsAndFollowsItsValue()
        {
            var library = ScriptableObject.CreateInstance<LetteraPaintLibrary>();
            m_Made.Add(library);
            library.Swatches.Add(new LetteraSwatch("Sun", LetteraPaint.Solid(new Color32(255, 0, 0, 255))));
            library.Swatches.Add(new LetteraSwatch("Sky", LetteraPaint.Of(LetteraGradient.Of(new Color32(0, 0, 0, 255), new Color32(255, 255, 255, 255)))));
            var serialized = new SerializedObject(library);
            SerializedProperty first = serialized.FindProperty("m_Swatches").GetArrayElementAtIndex(0).FindPropertyRelative("Paint");
            var paint = new LtPaint("Paint", first);
            Assert.AreEqual("#FF0000FF", paint.Words);
            var gradient = new LtPaint("Paint", serialized.FindProperty("m_Swatches").GetArrayElementAtIndex(1).FindPropertyRelative("Paint"));
            Assert.AreEqual("Gradient, 2 stops", gradient.Words);
            VisualElement page = gradient.BuildPage();
            Assert.IsNotNull(page.Q(className: "lt-gradient__bar"), "the popup holds the stops");
            Assert.AreEqual(2, page.Query(className: "lt-gradient__stop").ToList().Count);
            CollectionAssert.IsEmpty(ProblemsIn(LtUI.El("lt-root", page)));
        }

        [Test]
        public void ANamedPaintOffersTheLibraryWithSwatches()
        {
            var library = ScriptableObject.CreateInstance<LetteraPaintLibrary>();
            m_Made.Add(library);
            library.Swatches.Add(new LetteraSwatch("Sun", LetteraPaint.Solid(new Color32(255, 0, 0, 255))));
            library.Swatches.Add(new LetteraSwatch("Sky", LetteraPaint.Solid(new Color32(0, 0, 255, 255))));
            var text = Scene("T").AddComponent<LetteraText>();
            var fill = new FillEffect { Catalog = LetteraPaintCatalog.Library, Library = library };
            fill.Paint.Kind = LetteraPaintChoiceKind.Named;
            fill.Paint.SwatchName = "Sky";
            text.Styles.Add(LetteraStyle.ForWholeText(fill));
            var serialized = new SerializedObject(text);
            SerializedProperty effect = serialized.FindProperty("m_Data").FindPropertyRelative("Styles").GetArrayElementAtIndex(0).FindPropertyRelative("m_Effect");
            SerializedProperty choice = LetteraStyleList.Children(effect).First(c => c.type == nameof(LetteraPaintChoice));
            var paint = new LtPaint("Paint", choice, fill);
            Assert.AreEqual("Sky", paint.Words);
            VisualElement page = paint.BuildPage();
            var rows = page.Query(className: "lt-swatches__row").ToList();
            Assert.AreEqual(2, rows.Count);
            Assert.IsTrue(rows[1].ClassListContains("lt-swatches__row--on"));
            Assert.IsNotNull(rows[0].Q(className: "lt-paint__swatch"));
        }

        // ------------------------------------------------------------- Try it

        [Test]
        public void TryItRunsTheRules()
        {
            var rules = new List<LetteraFieldRule> { new MaxLengthRule { Limit = 3 }, new LetterCaseRule() };
            Assert.AreEqual("ABC", LetteraRuleTrial.Run(rules, null, "abcdef", out string note));
            StringAssert.Contains("turned away", note);
            Assert.AreEqual("12", LetteraRuleTrial.Run(new List<LetteraFieldRule> { new WholeNumberCharFilter() }, null, "1a2", out _));
            Assert.IsEmpty(Resources.FindObjectsOfTypeAll<GameObject>().Where(g => g.name == "Lettera Rule Trial"), "nothing is left");
        }

        [Test]
        public void TryItLeavesTheSceneClean()
        {
            UnityEngine.SceneManagement.Scene scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            bool before = scene.isDirty;
            LetteraRuleTrial.Run(new List<LetteraFieldRule> { new SingleLineRule() }, null, "a", out _);
            Assert.AreEqual(before, scene.isDirty);
        }

        // ------------------------------------------------------------- the rule builder

        [Test]
        public void ARuleReadsAsChipsAndOffersWhatItCanChange()
        {
            var text = Scene("T").AddComponent<LetteraText>();
            text.Text = "<link=a>x</link>";
            var color = new ColorEffect();
            text.Styles.Add(LetteraStyle.ForWholeText(color));
            var range = new ActionEffect();
            range.Rules.Add(new LetteraRangeRule { When = new SignalIs(LetteraSignals.Pressed) });
            range.Rules[0].Changes.Add(new LetteraRangeChange { Target = color, Parameter = "", ToNumber = 1 });
            text.Styles.Add(LetteraStyle.ForTag(range, "go"));
            var serialized = new SerializedObject(text);
            SerializedProperty rule = serialized.FindProperty("m_Data").FindPropertyRelative("Styles").GetArrayElementAtIndex(1).FindPropertyRelative("m_Effect").FindPropertyRelative("m_Rules").GetArrayElementAtIndex(0);
            VisualElement page = LetteraRuleBuilder.Build(rule);
            var chips = page.Query<Label>(className: "lt-chip--click").ToList();
            Assert.AreEqual(new[] { "Hover", "Press", "Focus", "Disabled" }, chips.Select(c => c.text).ToArray());
            Assert.IsTrue(chips[1].ClassListContains("lt-chip--accent"));
            Assert.IsNotNull(page.Q(className: "lt-list"));
            List<(string name, LetteraEffect effect)> beside = LetteraRuleBuilder.EffectsBeside(rule);
            Assert.AreEqual(2, beside.Count);
            Assert.AreSame(color, beside[0].effect);
        }

        // ------------------------------------------------------------- font page

        [Test]
        public void AFontPageSaysWhereItsBytesLive()
        {
            string words = LetteraFontEditor.WhereBytesLive(Latin);
            StringAssert.Contains("Desktop", words);
            StringAssert.Contains("Android", words);
            UnityEditor.Editor editor = UnityEditor.Editor.CreateEditor(Latin);
            VisualElement root = editor.CreateInspectorGUI();
            var titles = root.Query<Label>(className: "lt-card__title").ToList().Select(l => l.text).ToList();
            foreach (string card in new[] { "STATUS", "RENDERING", "PIXEL FONT", "METRICS", "VARIABLE AXES", "GLYPHS", "SUBSET", "FIND USES", "SHARED ATLASES" })
                CollectionAssert.Contains(titles, card);
            Assert.Greater(root.Query(className: "lt-glyph-cell").ToList().Count, 3, "the grid shows letters of this font");
            UnityEngine.Object.DestroyImmediate(editor);
        }

        [Test]
        public void MetricsByHandChangeLayoutAndResetTakesThemBack()
        {
            LetteraFont font = LetteraFont.CreateFromBytes(Latin.ReadBytes());
            m_Made.Add(font);
            LetteraWorldText text = World("A\nB", font);
            ((ITextHost)text).Data.LineHeightMode = LetteraLineHeightMode.Primary;
            text.ForceRebuild();
            float height = text.Engine.LayoutBounds.height;
            FontFace face = font.LoadFace();
            FaceMetrics natural = face.NaturalMetrics;

            var serialized = new SerializedObject(font);
            serialized.FindProperty("m_OverrideMetrics").boolValue = true;
            serialized.FindProperty("m_MetricAscender").intValue = natural.Ascender * 2;
            serialized.FindProperty("m_MetricDescender").intValue = natural.Descender;
            serialized.FindProperty("m_MetricLineGap").intValue = natural.LineGap;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            font.FontScale = font.FontScale;
            Assert.AreEqual(natural.Ascender * 2, face.Metrics.Ascender);
            text.Engine.SetDirty(DirtyLevel.Shape);
            text.ForceRebuild();
            Assert.Greater(text.Engine.LayoutBounds.height, height, "the taller ascender moves the lines");

            serialized.Update();
            serialized.FindProperty("m_OverrideMetrics").boolValue = false;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            font.FontScale = font.FontScale;
            Assert.AreEqual(natural.Ascender, face.Metrics.Ascender);
        }

        LetteraWorldText World(string value, LetteraFont font)
        {
            var text = Scene("World").AddComponent<LetteraWorldText>();
            ((RectTransform)text.transform).sizeDelta = new Vector2(400, 400);
            text.Font = font;
            text.Text = value;
            return text;
        }

        [Test]
        public void AxesAreReadFromTheFont()
        {
            FontFace face = Latin.LoadFace();
            foreach (FontFace.AxisInfo axis in face.Axes)
            {
                Assert.AreEqual(4, axis.Tag.Length);
                Assert.LessOrEqual(axis.Min, axis.Max);
            }
            Assert.AreEqual(face.IsVariable, face.Axes.Length > 0);
        }

        [Test]
        public void AGlyphGridSkipsSpacesAndWalksSurrogates()
        {
            CollectionAssert.AreEqual(new[] { 'a', 0x1F600, 'b' }, LetteraGlyphGrid.Codepoints("a \U0001F600 b").ToArray());
        }

        [Test]
        public void FindUsesFindsARealReference()
        {
            var preset = ScriptableObject.CreateInstance<LetteraStylePreset>();
            string presetPath = Folder + "/Used.asset";
            AssetDatabase.CreateAsset(preset, presetPath);
            var other = ScriptableObject.CreateInstance<LetteraFieldPreset>();
            GameObject go = Scene("Holder");
            go.AddComponent<LetteraText>().StylePresets.Add(preset);
            PrefabUtility.SaveAsPrefabAsset(go, Folder + "/Holder.prefab");
            AssetDatabase.SaveAssets();
            UnityEngine.Object.DestroyImmediate(other);
            List<(string path, long bytes)> found = LetteraFindUses.Scan(Folder, AssetDatabase.AssetPathToGUID(presetPath), presetPath);
            Assert.AreEqual(1, found.Count);
            Assert.AreEqual(Folder + "/Holder.prefab", found[0].path);
            Assert.Greater(found[0].bytes, 0);
        }

        // ------------------------------------------------------------- settings

        [Test]
        public void TheSettingsPageHasItsCards()
        {
            var settings = ScriptableObject.CreateInstance<LetteraSettings>();
            m_Made.Add(settings);
            VisualElement root = LetteraSettingsPage.Build(settings, (a, b, c) => LtUI.Text(a, "lt-row__label"));
            var titles = root.Query<LtSection>().ToList().Select(s => s.Title).ToList();
            foreach (string card in new[] { "Defaults", "Selection & Editing", "Animation & Interaction", "Documents", "Native Platform", "Font Memory", "Atlases", "Math", "3D", "Commands", "In Your Build", "Tools" })
                CollectionAssert.Contains(titles, card);
            CollectionAssert.IsEmpty(ProblemsIn(root), string.Join(", ", ProblemsIn(root)));
            Assert.IsNotNull(root.Query<Label>(className: "lt-add").ToList().FirstOrDefault(l => l.text == "Select Settings Asset"));
            var all = root.Query<LtSection>().ToList();
            LtFilter.Apply(all, "Default Font");
            Assert.IsFalse(all.First(s => s.Title == "Defaults").ClassListContains("lt-filtered"));
            Assert.IsTrue(all.First(s => s.Title == "Math").ClassListContains("lt-filtered"));
            LtFilter.Apply(all, "");
            Assert.IsFalse(all.First(s => s.Title == "Math").ClassListContains("lt-filtered"));
        }

        [Test]
        public void TheSettingsAssetOpensOnePlace()
        {
            var settings = ScriptableObject.CreateInstance<LetteraSettings>();
            m_Made.Add(settings);
            UnityEditor.Editor editor = UnityEditor.Editor.CreateEditor(settings);
            VisualElement root = editor.CreateInspectorGUI();
            Assert.AreEqual(0, root.Query(className: "lt-row").ToList().Count, "nothing to edit here");
            Assert.AreEqual("Open Project Settings", root.Q<Label>(className: "lt-add").text);
            UnityEngine.Object.DestroyImmediate(editor);
        }

        [Test]
        public void InYourBuildSaysWhatAndWhy()
        {
            var settings = ScriptableObject.CreateInstance<LetteraSettings>();
            m_Made.Add(settings);
            List<LetteraBuildScan.Row> empty = LetteraBuildScan.Rows(settings, null);
            Assert.IsTrue(empty.Any(r => r.Kind == "Font" && !r.Included && r.Why.Contains("empty")));
            Assert.IsTrue(empty.Any(r => r.Name == "Formulas"));
            settings.DefaultFont = Latin;
            List<LetteraBuildScan.Row> rows = LetteraBuildScan.Rows(settings, null);
            LetteraBuildScan.Row font = rows.First(r => r.Name == Latin.name);
            Assert.IsTrue(font.Included);
            StringAssert.Contains("Default Font", font.Why);
            Assert.GreaterOrEqual(font.Bytes, 0);
            Assert.IsTrue(rows[0].Included, "what is included comes first");
            var paints = ScriptableObject.CreateInstance<LetteraPaintLibrary>();
            AssetDatabase.CreateAsset(paints, Folder + "/Paints.asset");
            AssetDatabase.SaveAssets();
            settings.Paints = paints;
            Assert.Greater(LetteraBuildScan.Rows(settings, null).First(r => r.Kind == "Paint Library").Bytes, 0, "a saved asset has a size");
        }

        [Test]
        public void InYourBuildReadsScenes()
        {
            var preset = ScriptableObject.CreateInstance<LetteraStylePreset>();
            AssetDatabase.CreateAsset(preset, Folder + "/In Scene.asset");
            GameObject go = Scene("Holder");
            go.AddComponent<LetteraText>().StylePresets.Add(preset);
            string prefab = Folder + "/Scene Stand-in.prefab";
            PrefabUtility.SaveAsPrefabAsset(go, prefab);
            AssetDatabase.SaveAssets();
            var settings = ScriptableObject.CreateInstance<LetteraSettings>();
            m_Made.Add(settings);
            LetteraBuildScan.Row row = LetteraBuildScan.Rows(settings, new[] { prefab }).First(r => r.Name == "In Scene");
            StringAssert.Contains("Scene Stand-in", row.Why);
        }

        // ------------------------------------------------------------- scene

        [Test]
        public void TheEyeHidesBatchedWorldText()
        {
            LetteraWorldText text = World("hidden", Latin);
            text.ForceRebuild();
            bool saved = LetteraSceneOptions.FollowVisibility;
            try
            {
                LetteraSceneOptions.FollowVisibility = true;
                Assert.IsFalse(text.Hidden);
                SceneVisibilityManager.instance.Hide(text.gameObject, false);
                LetteraSceneOptions.Refresh();
                Assert.IsTrue(text.Hidden, "hidden by the eye");
                LetteraSceneOptions.FollowVisibility = false;
                Assert.IsFalse(text.Hidden, "the toggle turns this off");
                LetteraSceneOptions.FollowVisibility = true;
                SceneVisibilityManager.instance.Show(text.gameObject, false);
                LetteraSceneOptions.Refresh();
                Assert.IsFalse(text.Hidden, "back with the eye");
            }
            finally
            {
                SceneVisibilityManager.instance.Show(text.gameObject, false);
                LetteraSceneOptions.FollowVisibility = saved;
            }
        }

        [Test]
        public void InkIsWhereTheMeshIs()
        {
            Assert.AreEqual(Rect.zero, LetteraSceneHandles.InkRect(null));
            LetteraWorldText text = World("Ink", Latin);
            text.ForceRebuild();
            Rect ink = LetteraSceneHandles.InkRect(text.Engine);
            Assert.Greater(ink.width, 0);
            Assert.Greater(ink.height, 0);
            Assert.AreEqual(text.Engine.MeshBounds.min.x, ink.xMin, 0.001f);
        }

        [Test]
        public void PaddingOutlineUsesTheSameSidesAsTheBox()
        {
            Rect inside = LetteraSceneHandles.Inside(new Rect(0, 0, 100, 50), new Vector4(1, 2, 3, 4));
            Assert.AreEqual(1, inside.xMin);
            Assert.AreEqual(2, inside.yMin);
            Assert.AreEqual(97, inside.xMax);
            Assert.AreEqual(46, inside.yMax);
        }

        // ------------------------------------------------------------- no reference names, no reference art

        [Test]
        public void IconsAreOursAndNamedOurs()
        {
            string asmdef = AssetDatabase.GUIDToAssetPath(AssetDatabase.FindAssets("FirstSymbol.Lettera.Editor t:AssemblyDefinitionAsset").First(g => Path.GetFileName(AssetDatabase.GUIDToAssetPath(g)) == "FirstSymbol.Lettera.Editor.asmdef"));
            string icons = Path.Combine(Path.GetFullPath(Path.GetDirectoryName(asmdef)), "UI", "Icons");
            foreach (string file in Directory.GetFiles(icons, "*.png")) StringAssert.StartsWith("LetteraIcon", Path.GetFileName(file));
        }
    }
}

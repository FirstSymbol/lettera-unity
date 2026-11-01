using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using FirstSymbol.Lettera.Editor.Cli;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Overlays;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace FirstSymbol.Lettera.Editor.Tests
{
    /// <summary>A session driven through the events a Scene view gives it. Real keys, a real mouse and a real IME are for a person.</summary>
    public class SceneEditTests : EditingFixture
    {
        const string Folder = "Assets/LetteraSceneEditTest";

        Camera m_Camera;
        RenderTexture m_Target;

        [SetUp]
        public void SetUp()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            Directory.CreateDirectory(Folder);
            AssetDatabase.ImportAsset(Folder);
        }

        [TearDown]
        public void TearDown()
        {
            LetteraSceneEdit.Current?.End(LetteraSceneEditEnd.Done);
            LetteraSceneEdit.MenuOverride = null;
            LetteraSceneEditInput.CompositionOverride = null;
            TextEngine.FontsGone = false;
            Selection.activeObject = null;
            if (m_Target != null) Object.DestroyImmediate(m_Target);
            if (m_Camera != null) Object.DestroyImmediate(m_Camera.gameObject);
            if (StageUtility.GetCurrentStage() is PrefabStage) StageUtility.GoToMainStage();
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            AssetDatabase.DeleteAsset(Folder);
        }

        LetteraSceneEdit Begin(MonoBehaviour text)
        {
            LetteraSceneEdit session = LetteraSceneEdit.Start(text, out string why);
            Assert.IsNotNull(session, why);
            return session;
        }

        static void Keys(LetteraSceneEdit session, string text)
        {
            foreach (char c in text)
            {
                Event e = c == ' ' ? Event.KeyboardEvent("space") : Event.KeyboardEvent(c.ToString());
                e.character = c;
                session.Key(e);
            }
        }

        static bool Press(LetteraSceneEdit session, string key) => session.Key(Event.KeyboardEvent(key));

        static int HelperCount() => Resources.FindObjectsOfTypeAll<GameObject>().Count(g => g.name == LetteraSceneEdit.HelperName);

        Camera SceneCamera()
        {
            if (m_Camera != null) return m_Camera;
            m_Target = new RenderTexture(800, 600, 0);
            m_Camera = new GameObject("Test Camera").AddComponent<Camera>();
            m_Camera.targetTexture = m_Target;
            m_Camera.orthographic = true;
            m_Camera.orthographicSize = 300;
            m_Camera.transform.position = new Vector3(0, 0, -100);
            return m_Camera;
        }

        Vector2 ScreenOf(LetteraWorldText text, int index)
        {
            text.Engine.EnsurePositioned();
            Assert.IsTrue(text.Engine.TryGetCaretRect(index, LetteraCaretBias.Forward, out Rect caret));
            return RectTransformUtility.WorldToScreenPoint(SceneCamera(), text.transform.TransformPoint(caret.center));
        }

        // ------------------------------------------------------------------------------------- AC 1

        [Test]
        public void TypingReachesTheTextAndOneUndoTakesItAllBack()
        {
            LetteraWorldText text = Make("Hello");
            Undo.IncrementCurrentGroup();
            Undo.RecordObject(text, "before");
            text.Text = "Hello there";
            Undo.FlushUndoRecordObjects();
            Undo.IncrementCurrentGroup();

            LetteraSceneEdit session = Begin(text);
            Keys(session, " and");
            Now += 100;
            Keys(session, " then");
            Now += 100;
            Keys(session, " more");
            Assert.AreEqual("Hello there and then more", text.Text, "live, as it is typed");
            session.End(LetteraSceneEditEnd.Done);
            Assert.AreEqual("Hello there and then more", text.Text);
            Assert.IsNull(LetteraSceneEdit.Current);

            Undo.PerformUndo();
            Assert.AreEqual("Hello there", text.Text, "one step for the whole session, and no further");
            Undo.PerformRedo();
            Assert.AreEqual("Hello there and then more", text.Text);
        }

        [Test]
        public void InsideASessionUndoIsTheField()
        {
            LetteraWorldText text = Make("Hello");
            LetteraSceneEdit session = Begin(text);
            Keys(session, " abc");
            Now += 100;
            Press(session, "left");
            Keys(session, " x");
            Assert.AreEqual("Hello ab xc", text.Text);
            Assert.IsTrue(session.Key(CtrlKey("z")));
            Assert.AreEqual("Hello abc", text.Text, "a burst goes back as one, and the session goes on");
            Assert.IsNotNull(LetteraSceneEdit.Current);
            session.End(LetteraSceneEditEnd.Done);
            Undo.PerformUndo();
            Assert.AreEqual("Hello", text.Text);
        }

        static Event CtrlKey(string key)
        {
            Event e = Event.KeyboardEvent(key);
            e.control = true;
            e.character = '\0';
            return e;
        }

        [Test]
        public void SelectionAndHelpersAndASavedFile()
        {
            LetteraWorldText text = Make("Hello brave world");
            string path = Folder + "/Saved.unity";
            Assert.IsTrue(EditorSceneManager.SaveScene(text.gameObject.scene, path));
            int componentsBefore = text.GetComponents<Component>().Length;

            LetteraSceneEdit session = Begin(text);
            Assert.AreEqual(1, HelperCount());
            Assert.AreEqual(HideFlags.HideAndDontSave, session.Helper.hideFlags, "with no hole for a build either");
            Assert.AreEqual(HideFlags.HideAndDontSave, session.Field.hideFlags);
            Assert.AreEqual(HideFlags.HideAndDontSave, session.Selectable.hideFlags);
            Assert.AreEqual(componentsBefore, text.GetComponents<Component>().Length, "nothing is added to what is edited");
            Assert.AreEqual(0, text.transform.childCount);

            session.Selectable.Select(6, 11);
            Keys(session, "bold");
            Assert.AreEqual("Hello bold world", text.Text);

            // A save in the middle holds the value and no helper.
            Assert.IsTrue(EditorSceneManager.SaveScene(text.gameObject.scene, path));
            string yaml = File.ReadAllText(path);
            StringAssert.Contains("Hello bold world", yaml);
            StringAssert.DoesNotContain(LetteraSceneEdit.HelperName, yaml);
            Assert.AreEqual(1, Regex.Matches(yaml, "--- !u!114 ").Count, "one script in the file: the text");

            Keys(session, "er");
            session.End(LetteraSceneEditEnd.Done);
            Assert.AreEqual(0, HelperCount());
            Assert.AreEqual(componentsBefore, text.GetComponents<Component>().Length);
            Assert.IsNull(LetteraSelectable.Current);
            Assert.IsTrue(EditorSceneManager.SaveScene(text.gameObject.scene, path));
            yaml = File.ReadAllText(path);
            StringAssert.Contains("Hello bolder world", yaml);
            Assert.AreEqual(1, Regex.Matches(yaml, "--- !u!114 ").Count);
        }

        // ------------------------------------------------------------------------------------- AC 5

        [Test]
        public void StartingAndLeavingWithoutAChangeLeavesTheSceneClean()
        {
            LetteraWorldText text = Make("Hello");
            Assert.IsTrue(EditorSceneManager.SaveScene(text.gameObject.scene, Folder + "/Clean.unity"));
            Assert.IsFalse(text.gameObject.scene.isDirty);
            int group = Undo.GetCurrentGroup();

            LetteraSceneEdit session = Begin(text);
            Assert.IsFalse(text.gameObject.scene.isDirty, "starting");
            session.Selectable.SelectAll();
            Press(session, "left");
            session.PointerDown(ScreenOf(text, 2), SceneCamera());
            session.PointerUp(ScreenOf(text, 2));
            session.Tick();
            Assert.IsFalse(text.gameObject.scene.isDirty, "moving around");
            session.End(LetteraSceneEditEnd.Escape);
            Assert.IsFalse(text.gameObject.scene.isDirty, "leaving");
            Assert.AreEqual(group, Undo.GetCurrentGroup(), "and no undo step");
        }

        [Test]
        public void TheFirstChangeGivesTheSceneSomethingToSave()
        {
            LetteraWorldText text = Make("Hello");
            Assert.IsTrue(EditorSceneManager.SaveScene(text.gameObject.scene, Folder + "/Dirty.unity"));
            LetteraSceneEdit session = Begin(text);
            Keys(session, "!");
            Assert.IsTrue(text.gameObject.scene.isDirty, "or a quit in the middle would lose it without asking");
        }

        // ------------------------------------------------------------------------------------- AC 2: what must not happen

        [Test]
        public void ADuplicateHasNoHelper()
        {
            LetteraWorldText text = Make("Hello");
            LetteraSceneEdit session = Begin(text);
            Keys(session, " typed");
            GameObject copy = Object.Instantiate(text.gameObject);
            try
            {
                Assert.IsNull(copy.GetComponent<LetteraSelectable>());
                Assert.IsNull(copy.GetComponent<LetteraEditable>());
                Assert.AreEqual(0, copy.transform.childCount);
                Assert.AreEqual(1, HelperCount());
            }
            finally
            {
                Object.DestroyImmediate(copy);
            }
        }

        [Test]
        public void RemovingTheComponentEndsTheSession([Values(false, true)] bool throughUndo)
        {
            LetteraWorldText text = Make("Hello");
            LetteraSceneEdit session = Begin(text);
            Keys(session, " typed");
            if (throughUndo) Undo.DestroyObjectImmediate(text);
            else Object.DestroyImmediate(text);

            session.Selectable.Tick();
            session.Tick();
            Assert.IsNull(LetteraSceneEdit.Current);
            Assert.AreEqual(LetteraSceneEditEnd.TextGone, session.EndedBy);
            Assert.AreEqual(0, HelperCount());
            Assert.IsNull(LetteraSelectable.Current);
            if (!throughUndo) return;
            GameObject go = Selection.activeGameObject;
            Undo.PerformUndo();
            var back = go.GetComponent<LetteraWorldText>();
            Assert.IsNotNull(back);
            Assert.AreEqual("Hello typed", back.Text, "what was typed comes back with it");
        }

        [Test]
        public void RemovingTheObjectEndsTheSession()
        {
            LetteraWorldText text = Make("Hello");
            LetteraSceneEdit session = Begin(text);
            Object.DestroyImmediate(text.gameObject);
            session.Tick();
            Assert.AreEqual(LetteraSceneEditEnd.TextGone, session.EndedBy);
            Assert.AreEqual(0, HelperCount());
        }

        [Test]
        public void APrefabInstanceGetsAnOverride()
        {
            GameObject source = Make("Prefab text").gameObject;
            string path = Folder + "/Text.prefab";
            GameObject asset = PrefabUtility.SaveAsPrefabAsset(source, path);
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(asset);
            var text = instance.GetComponent<LetteraWorldText>();
            text.Font = Latin;

            LetteraSceneEdit session = Begin(text);
            Assert.IsEmpty(PrefabUtility.GetAddedComponents(instance), "a session adds nothing to an instance");
            Assert.IsEmpty(PrefabUtility.GetAddedGameObjects(instance));
            Keys(session, " typed");
            session.End(LetteraSceneEditEnd.Done);

            PropertyModification change = PrefabUtility.GetPropertyModifications(instance).First(m => m.target != null && m.propertyPath == "m_Data.Text");
            Assert.AreEqual("Prefab text typed", change.value);
            Assert.AreEqual("Prefab text", AssetDatabase.LoadAssetAtPath<GameObject>(path).GetComponent<LetteraWorldText>().Text, "the asset stays");
            Assert.IsEmpty(PrefabUtility.GetAddedComponents(instance));
            Object.DestroyImmediate(instance);
        }

        [Test]
        public void APrefabInstanceHasItsOverrideForASaveInTheMiddle()
        {
            GameObject source = Make("Prefab text").gameObject;
            GameObject asset = PrefabUtility.SaveAsPrefabAsset(source, Folder + "/Text.prefab");
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(asset);
            var text = instance.GetComponent<LetteraWorldText>();
            string scene = Folder + "/Instance.unity";
            Assert.IsTrue(EditorSceneManager.SaveScene(instance.scene, scene));

            LetteraSceneEdit session = Begin(text);
            Keys(session, " typed");
            Assert.IsTrue(EditorSceneManager.SaveScene(instance.scene, scene));
            StringAssert.Contains("Prefab text typed", File.ReadAllText(scene));
            session.End(LetteraSceneEditEnd.Done);
            Object.DestroyImmediate(instance);
        }

        [Test]
        public void InPrefabModeThePrefabIsEdited()
        {
            GameObject source = Make("Prefab text").gameObject;
            string path = Folder + "/Staged.prefab";
            PrefabUtility.SaveAsPrefabAsset(source, path);
            PrefabStage stage = PrefabStageUtility.OpenPrefab(path);
            var text = stage.prefabContentsRoot.GetComponent<LetteraWorldText>();
            text.Font = Latin;

            LetteraSceneEdit session = Begin(text);
            Keys(session, " typed");
            session.End(LetteraSceneEditEnd.Done);
            Assert.AreEqual("Prefab text typed", text.Text);
            Assert.IsTrue(stage.scene.isDirty);
            Assert.IsEmpty(PrefabUtility.GetPropertyModifications(text.gameObject) ?? new PropertyModification[0], "the prefab's own value, not an override");
            Assert.AreEqual(0, HelperCount());
            Undo.PerformUndo();
            Assert.AreEqual("Prefab text", text.Text);
        }

        [Test]
        public void APrefabOnDiskDoesNotStart()
        {
            GameObject asset = PrefabUtility.SaveAsPrefabAsset(Make("Prefab text").gameObject, Folder + "/Disk.prefab");
            Assert.IsNull(LetteraSceneEdit.Start(asset.GetComponent<LetteraWorldText>(), out string why));
            StringAssert.Contains("Prefab Mode", why);
        }

        [Test]
        public void SeveralSelectedDoesNotStartAndSaysWhy()
        {
            LetteraWorldText a = Make("A"), b = Make("B");
            StringAssert.Contains("Several", LetteraSceneEdit.WhyNot(new Object[] { a.gameObject, b.gameObject }));
            Assert.IsFalse(LetteraSceneEditView.Begin(new Object[] { a.gameObject, b.gameObject }));
            StringAssert.Contains("Several", LetteraSceneEditView.LastWhy);
            Assert.IsNull(LetteraSceneEdit.Current);
            Assert.IsNull(LetteraSceneEdit.WhyNot(new Object[] { a.gameObject }));
            Assert.IsNull(LetteraSceneEdit.WhyNot(new Object[] { a }));
            StringAssert.Contains("Select", LetteraSceneEdit.WhyNot(new Object[0]));
            GameObject bare = new GameObject("Bare");
            StringAssert.Contains("no Lettera text", LetteraSceneEdit.WhyNot(new Object[] { bare }));
            Object.DestroyImmediate(bare);
        }

        sealed class Upper : ILetteraTextResolver
        {
            public string Resolve(Component owner, string raw) => raw.ToUpperInvariant();
        }

        [Test]
        public void WhatIsNotTheWrittenValueDoesNotStart()
        {
            LetteraWorldText document = Make("");
            document.OpenDocument(LetteraRope.FromString("A long story"), true);
            document.ForceRebuild();
            Assert.IsNull(LetteraSceneEdit.Start(document, out string why));
            StringAssert.Contains("document", why);

            LetteraWorldText resolved = Make("written");
            resolved.TextResolver = new Upper();
            Assert.IsNull(LetteraSceneEdit.Start(resolved, out why));
            StringAssert.Contains("resolver", why);

            LetteraWorldText set = Make("written");
            set.SetText("from a script");
            Assert.IsNull(LetteraSceneEdit.Start(set, out why));
            StringAssert.Contains("SetText", why);

            LetteraWorldText inactive = Make("written");
            inactive.gameObject.SetActive(false);
            Assert.IsNull(LetteraSceneEdit.Start(inactive, out why));
            StringAssert.Contains("inactive", why);
            Assert.AreEqual(0, HelperCount());
        }

        [Serializable]
        sealed class RefuseAll : LetteraFieldRule
        {
            public static int Asked;

            public override void FilterEdit(ref LetteraEditProposal edit)
            {
                Asked++;
                edit.Reject();
            }
        }

        [Serializable]
        sealed class Shout : LetteraEditCommand
        {
            public static int Ran;
            public override string FallbackTitle => "Shout it";
            public override bool CanRun(in LetteraCommandContext c) => c.CanEdit;
            public override void Execute(in LetteraCommandContext c) => Ran++;
        }

        [Test]
        public void AFieldOnTheObjectKeepsItsRulesToItself()
        {
            LetteraWorldText text = Make("Hello");
            var field = text.gameObject.AddComponent<LetteraEditable>();
            field.Rules.Add(new RefuseAll());
            field.enabled = false;
            field.enabled = true;
            field.MarkupView = LetteraMarkupView.Source;
            var heard = new List<string>();
            field.ValueChanged += heard.Add;
            RefuseAll.Asked = 0;
            int depth = field.UndoDepth;

            LetteraSceneEdit session = Begin(text);
            Assert.AreNotSame(field, session.Field);
            Keys(session, " typed");
            Assert.AreEqual("Hello typed", text.Text);
            Assert.AreEqual(0, RefuseAll.Asked, "not its rules");
            Assert.IsEmpty(heard, "it hears at the end");
            Assert.IsFalse(text.Engine.Literal, "the session shows as it says");
            session.End(LetteraSceneEditEnd.Done);
            Assert.AreEqual(1, heard.Count);
            Assert.AreEqual("Hello typed", heard[0]);
            Assert.IsTrue(text.Engine.Literal, "its view comes back");
            Assert.AreEqual(depth, field.UndoDepth);
        }

        [Test]
        public void APasswordFieldShowsItsWrittenValueAndGoesBackToItsMask()
        {
            LetteraWorldText text = Make("secret");
            text.Engine.MaskGlyph = '*';
            LetteraSceneEdit session = Begin(text);
            Assert.AreEqual(0, text.Engine.MaskGlyph);
            session.End(LetteraSceneEditEnd.Done);
            Assert.AreEqual('*', text.Engine.MaskGlyph);
            text.Engine.MaskGlyph = 0;
        }

        // ------------------------------------------------------------------------------------- AC 3: what ends it

        [Test]
        public void TheWaysOut()
        {
            LetteraWorldText text = Make("Hello");

            // Esc
            LetteraSceneEdit session = Begin(text);
            Keys(session, "a");
            Assert.IsTrue(Press(session, "escape"));
            Assert.AreEqual(LetteraSceneEditEnd.Escape, session.EndedBy);
            Assert.AreEqual("Helloa", text.Text, "what was typed stays");

            // Selection
            session = Begin(text);
            Keys(session, "b");
            GameObject other = new GameObject("Other");
            Selection.activeGameObject = other;
            session.Tick();
            Assert.AreEqual(LetteraSceneEditEnd.SelectionChanged, session.EndedBy);
            Assert.AreEqual("Helloab", text.Text);
            Object.DestroyImmediate(other);

            // Entering Play Mode
            session = Begin(text);
            Keys(session, "c");
            LetteraSceneEditView.PlayModeChanged(PlayModeStateChange.ExitingEditMode);
            Assert.AreEqual(LetteraSceneEditEnd.PlayMode, session.EndedBy);
            Assert.AreEqual("Helloabc", text.Text);
            Assert.AreEqual(0, HelperCount());

            // The bar's button
            session = Begin(text);
            session.End(LetteraSceneEditEnd.Done);
            Assert.IsNull(LetteraSceneEdit.Current);
            Assert.AreEqual(0, HelperCount());
        }

        [Test]
        public void ADomainReloadEndsItWithTheTextKeptAndNoFontTouched()
        {
            LetteraWorldText text = Make("Hello");
            LetteraSceneEdit session = Begin(text);
            Keys(session, " typed");
            LetteraSceneEditInput.CompositionOverride = () => "ime";
            session.Tick();
            Assert.IsTrue(session.Field.IsComposing);

            // The fonts go first, or after: this is what a reload may do to the session, and it may not shape.
            TextEngine.FontsGone = true;
            LetteraSceneEditView.BeforeReload();
            Assert.AreEqual(LetteraSceneEditEnd.DomainReload, session.EndedBy);
            Assert.AreEqual(0, HelperCount());
            TextEngine.FontsGone = false;
            StringAssert.StartsWith("Hello typed", text.Text);
        }

        [Test]
        public void ChangedByAnotherHandTheSessionStepsAside()
        {
            LetteraWorldText text = Make("Hello");
            LetteraSceneEdit session = Begin(text);
            Keys(session, " typed");
            var serialized = new SerializedObject(text);
            serialized.FindProperty("m_Data.Text").stringValue = "From the inspector";
            serialized.ApplyModifiedProperties();
            session.Tick();
            Assert.AreEqual(LetteraSceneEditEnd.ChangedElsewhere, session.EndedBy);
            Assert.AreEqual("From the inspector", text.Text);
        }

        [Test]
        public void ClosingASceneCommitsFirst()
        {
            LetteraWorldText text = Make("Hello");
            LetteraSceneEdit session = Begin(text);
            Keys(session, "!");
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            Assert.IsNull(LetteraSceneEdit.Current);
            Assert.AreEqual(LetteraSceneEditEnd.Done, session.EndedBy);
            Assert.AreEqual(0, HelperCount());
        }

        [Test]
        public void ALeftoverHelperIsSwept()
        {
            GameObject orphan = EditorUtility.CreateGameObjectWithHideFlags(LetteraSceneEdit.HelperName, HideFlags.HideAndDontSave);
            orphan.SetActive(false);
            orphan.AddComponent<LetteraSelectable>();
            Assert.AreEqual(1, LetteraSceneEdit.Sweep());
            Assert.AreEqual(0, HelperCount());
            LetteraSceneEdit session = Begin(Make("x"));
            Assert.AreEqual(0, LetteraSceneEdit.Sweep(), "a running session's stays");
            session.End(LetteraSceneEditEnd.Done);
        }

        // ------------------------------------------------------------------------------------- keys

        [Test]
        public void PlainKeysAreTheTextsAndChordsOnlyWhenTheyMeanSomething()
        {
            LetteraWorldText text = Make("Hello");
            LetteraSceneEdit session = Begin(text);

            Event move = Event.KeyboardEvent("w");
            move.character = '\0';
            Assert.IsTrue(session.Key(move), "the half of a key that has no character is kept from the Move tool too");
            Assert.AreEqual("Hello", text.Text);
            foreach (string key in new[] { "w", "e", "r", "t", "f", "q", "1" })
                Assert.IsTrue(session.Key(Event.KeyboardEvent(key)), key);
            Assert.AreEqual("Hellowertfq1", text.Text);

            Assert.IsFalse(session.Key(CtrlKey("s")), "saving still works");
            Assert.IsFalse(session.Key(CtrlKey("p")), "and playing");
            Assert.IsFalse(session.Key(Event.KeyboardEvent("f5")));
            Assert.IsTrue(session.Key(CtrlKey("a")), "a field's chords are the field's");
            Assert.IsFalse(session.Selectable.Selection.IsCaret);
            Assert.IsTrue(session.Key(CtrlKey("c")));
            Assert.AreEqual("Hellowertfq1", Clip.Text(LetteraClipFormat.PlainText));
            Assert.IsTrue(Press(session, "backspace"));
            Assert.AreEqual("", text.Text);
            Assert.IsTrue(session.Key(CtrlKey("v")));
            Assert.AreEqual("Hellowertfq1", text.Text);
            Assert.IsFalse(session.Key(new Event { type = EventType.KeyUp, keyCode = KeyCode.A }), "only presses");
        }

        [Test]
        public void TheEditorsCommandsAreTheFieldsWhileItRuns()
        {
            LetteraWorldText text = Make("Hello");
            LetteraSceneEdit session = Begin(text);
            foreach (string command in new[] { "Copy", "Cut", "Paste", "SelectAll", "Delete", "SoftDelete", "Duplicate", "FrameSelected" })
                Assert.IsTrue(session.Command(command, false), command);
            Assert.IsFalse(session.Command("Something Else", false));

            session.Command("SelectAll", true);
            Assert.AreEqual(5, session.Selectable.Selection.Length);
            session.Command("SoftDelete", true);
            Assert.AreEqual("", text.Text, "the key that would delete the object deletes the text");
            Assert.IsNotNull(text);
            session.Command("Duplicate", true);
            Assert.AreEqual(1, Object.FindObjectsByType<LetteraWorldText>(FindObjectsSortMode.None).Length);
        }

        [Test]
        public void AnEnterIsANewLineAndAChordCanTypeThroughAltGr()
        {
            LetteraWorldText text = Make("Hello");
            LetteraSceneEdit session = Begin(text);
            Press(session, "return");
            Assert.AreEqual("Hello\n", text.Text);
            Event altGr = Event.KeyboardEvent("e");
            altGr.control = true;
            altGr.alt = true;
            altGr.character = '€';
            Assert.IsTrue(session.Key(altGr));
            Assert.AreEqual("Hello\n€", text.Text);
        }

        [Test]
        public void ASurrogatePairTypesAsOne()
        {
            LetteraWorldText text = Make("");
            LetteraSceneEdit session = Begin(text);
            foreach (char half in "\U0001F600")
            {
                var e = new Event { type = EventType.KeyDown, character = half };
                Assert.IsTrue(session.Key(e));
            }
            Assert.AreEqual("\U0001F600", text.Text);
        }

        // ------------------------------------------------------------------------------------- the pointer

        [Test]
        public void AClickPlacesTheCaretUnderIt()
        {
            LetteraWorldText text = Make("Hello brave world");
            LetteraSceneEdit session = Begin(text);
            for (int index = 0; index <= 12; index += 3)
            {
                Now += 10;
                Vector2 at = ScreenOf(text, index);
                Assert.IsTrue(session.Contains(at, SceneCamera()));
                session.PointerDown(at, SceneCamera());
                session.PointerUp(at);
                Assert.AreEqual(index, session.Selectable.Selection.Head, "index " + index);
                Assert.IsTrue(session.Selectable.Selection.IsCaret);
            }
        }

        [Test]
        public void AClickFindsTheCaretOnATurnedAndScaledText()
        {
            LetteraWorldText text = Make("Hello brave world");
            text.transform.position = new Vector3(40, -30, 5);
            text.transform.rotation = Quaternion.Euler(0, 25, 10);
            text.transform.localScale = Vector3.one * 0.5f;
            LetteraSceneEdit session = Begin(text);
            Vector2 at = ScreenOf(text, 6);
            session.PointerDown(at, SceneCamera());
            session.PointerUp(at);
            Assert.AreEqual(6, session.Selectable.Selection.Head);
        }

        [Test]
        public void ClicksSelectByUnitAndDragKeepsTheUnit()
        {
            LetteraWorldText text = Make("Hello brave world\nSecond line");
            LetteraSceneEdit session = Begin(text);
            Vector2 inBrave = ScreenOf(text, 8);

            session.PointerDown(inBrave, SceneCamera());
            session.PointerUp(inBrave);
            Now += 0.05;
            session.PointerDown(inBrave, SceneCamera());
            Assert.AreEqual((6, 11), (session.Selectable.Selection.Start, session.Selectable.Selection.End), "a double click is a word");
            session.PointerDrag(ScreenOf(text, 14));
            Assert.AreEqual((6, 17), (session.Selectable.Selection.Start, session.Selectable.Selection.End), "and dragging goes by words");
            session.PointerUp(ScreenOf(text, 14));

            Now += 10;
            session.PointerDown(inBrave, SceneCamera());
            session.PointerUp(inBrave);
            Now += 0.05;
            session.PointerDown(inBrave, SceneCamera());
            session.PointerUp(inBrave);
            Now += 0.05;
            session.PointerDown(inBrave, SceneCamera());
            session.PointerUp(inBrave);
            Assert.AreEqual(0, session.Selectable.Selection.Start, "a third is a paragraph");
            Assert.AreEqual(18, session.Selectable.Selection.End);

            Now += 10;
            session.PointerDown(ScreenOf(text, 2), SceneCamera());
            session.PointerDrag(ScreenOf(text, 5));
            session.PointerUp(ScreenOf(text, 5));
            Assert.AreEqual((2, 5), (session.Selectable.Selection.Start, session.Selectable.Selection.End), "one click's drag goes by characters");
        }

        [Test]
        public void LinksDoNotAnswerWhileWriting()
        {
            LetteraWorldText text = Make("A <a=https://x>link</a> here");
            LetteraSceneEdit session = Begin(text);
            LetteraSelectable.s_LinkModifierOverride = true;
            try
            {
                Vector2 at = ScreenOf(text, 3);
                session.PointerDown(at, SceneCamera());
                Assert.IsFalse(session.Selectable.Claimed, "the press is the caret's, even with what makes a link answer in a field");
                session.PointerUp(at);
            }
            finally
            {
                LetteraSelectable.s_LinkModifierOverride = null;
            }
            Assert.AreEqual(3, session.Selectable.Selection.Head);
        }

        [Test]
        public void TheVisualIsRectanglesOfTheText()
        {
            LetteraWorldText text = Make("Hello brave world");
            LetteraSceneEdit session = Begin(text);
            session.Selectable.PlaceCaret(3);
            session.Selectable.Tick();
            Assert.IsTrue(session.Visual.CaretOn);
            text.Engine.TryGetCaretRect(3, LetteraCaretBias.Forward, out Rect caret);
            Assert.AreEqual(caret, session.Visual.Caret);

            session.Selectable.Select(0, 5);
            session.Selectable.Tick();
            Assert.AreEqual(1, session.Visual.Rects.Count);
            Assert.Greater(session.Visual.Rects[0].width, 0);
            Assert.AreEqual(0, text.transform.childCount, "drawn by the view, with nothing under the text");
        }

        [Test]
        public void AltAndDoubleClickFindsTheNearestText()
        {
            LetteraWorldText far = Make("Far");
            far.transform.position = new Vector3(0, 0, 50);
            LetteraWorldText near = Make("Near");
            near.transform.position = new Vector3(0, 0, 10);
            LetteraWorldText off = Make("Off");
            off.transform.position = new Vector3(5000, 0, 1);
            Vector2 centre = RectTransformUtility.WorldToScreenPoint(SceneCamera(), Vector3.zero);
            Assert.AreSame(near, LetteraSceneEditView.TextAt(new MonoBehaviour[] { far, near, off }, centre, SceneCamera()));
            Assert.IsNull(LetteraSceneEditView.TextAt(new MonoBehaviour[] { off }, centre, SceneCamera()));
        }

        // ------------------------------------------------------------------------------------- Canvas

        [Test]
        public void CanvasTextToo()
        {
            var canvasObject = new GameObject("Canvas", typeof(Canvas));
            Canvas canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            var go = new GameObject("Label", typeof(RectTransform));
            go.transform.SetParent(canvasObject.transform, false);
            ((RectTransform)go.transform).sizeDelta = new Vector2(600, 200);
            var text = go.AddComponent<LetteraText>();
            text.Font = Latin;
            text.FontSize = 30;
            text.Text = "Canvas words";
            text.ForceRebuild();
            int children = go.transform.childCount, components = go.GetComponents<Component>().Length;

            LetteraSceneEdit session = Begin(text);
            Keys(session, " typed");
            Assert.AreEqual("Canvas words typed", text.Text);
            TextEngine engine = ((ITextComponent)text).SharedEngine;
            engine.EnsurePositioned();
            engine.TryGetCaretRect(7, LetteraCaretBias.Forward, out Rect caret);
            Vector2 at = RectTransformUtility.WorldToScreenPoint(SceneCamera(), go.transform.TransformPoint(caret.center));
            session.PointerDown(at, SceneCamera());
            session.PointerUp(at);
            Assert.AreEqual(7, session.Selectable.Selection.Head);
            session.Selectable.Select(0, 6);
            session.Selectable.Tick();
            Assert.AreEqual(children, go.transform.childCount, "no overlay graphic under the text");
            Assert.AreEqual(components, go.GetComponents<Component>().Length);
            session.End(LetteraSceneEditEnd.Done);
            Undo.PerformUndo();
            Assert.AreEqual("Canvas words", text.Text);
            Object.DestroyImmediate(canvasObject);
        }

        // ------------------------------------------------------------------------------------- AC 4: composition, by a fake reader

        [Test]
        public void ACompositionShowsAndCommitsWithItsCharactersAfterTheEnd()
        {
            LetteraWorldText text = Make("Hello ");
            string reading = "";
            LetteraSceneEditInput.CompositionOverride = () => reading;
            LetteraSceneEdit session = Begin(text);

            reading = "ni";
            session.Tick();
            Assert.IsTrue(session.Field.IsComposing);
            Assert.AreEqual("Hello ni", session.Field.ShownText, "beside the caret");
            Assert.AreEqual("Hello ", session.Field.PlainText, "and not in the value");
            reading = "nih";
            session.Tick();
            Assert.AreEqual("Hello nih", session.Field.ShownText);

            reading = "";
            session.Tick();
            Assert.IsFalse(session.Field.IsComposing);
            Assert.AreEqual("Hello ", text.Text, "the composition went away");
            Keys(session, "日本");
            Assert.AreEqual("Hello 日本", text.Text, "and its result came as characters");
            session.End(LetteraSceneEditEnd.Done);
            Undo.PerformUndo();
            Assert.AreEqual("Hello ", text.Text);
        }

        [Test]
        public void ACompositionsCharactersMayComeBeforeItsEnd()
        {
            LetteraWorldText text = Make("Hello ");
            string reading = "";
            LetteraSceneEditInput.CompositionOverride = () => reading;
            LetteraSceneEdit session = Begin(text);
            reading = "ni";
            session.Tick();
            Keys(session, "日");
            Assert.AreEqual("Hello ", session.Field.PlainText);
            reading = "";
            session.Tick();
            Assert.AreEqual("Hello 日", text.Text);
            Assert.IsFalse(session.Field.IsComposing);
        }

        [Test]
        public void EscDuringACompositionLeavesTheSessionUp()
        {
            LetteraWorldText text = Make("Hello");
            string reading = "ni";
            LetteraSceneEditInput.CompositionOverride = () => reading;
            LetteraSceneEdit session = Begin(text);
            session.Tick();
            Assert.IsTrue(session.Field.IsComposing);
            Press(session, "escape");
            Assert.IsNotNull(LetteraSceneEdit.Current, "the composition is the system's to cancel");
            reading = "";
            session.Tick();
            Assert.AreEqual("Hello", text.Text);
        }

        [Test]
        public void EndingDuringACompositionCommitsIt()
        {
            LetteraWorldText text = Make("Hello");
            LetteraSceneEditInput.CompositionOverride = () => "ni";
            LetteraSceneEdit session = Begin(text);
            session.Tick();
            session.End(LetteraSceneEditEnd.Done);
            Assert.AreEqual("Helloni", text.Text);
            Assert.IsFalse(text.Engine.TextOverride != LetteraTextOverride.None, "nothing is left in place of the value");
        }

        // ------------------------------------------------------------------------------------- the bar and the menu

        [Test]
        public void TheBarFormatsAndLooksLikeOurs()
        {
            Assert.IsTrue(TypeCache.GetTypesWithAttribute<OverlayAttribute>().Contains(typeof(LetteraSceneEditBar)), "so the overlay menu lists it");
            Assert.IsFalse(typeof(ITransientOverlay).IsAssignableFrom(typeof(LetteraSceneEditBar)), "a transient overlay stays out of that menu");

            LetteraWorldText text = Make("Hello brave world");
            LetteraSceneEdit session = Begin(text);
            VisualElement bar = new LetteraSceneEditBar().CreatePanelContent();
            Assert.IsTrue(bar.ClassListContains("lt-root"));
            Assert.AreEqual(0, bar.Query<IMGUIContainer>().ToList().Count);
            foreach (string name in new[] { "lt-scene-b", "lt-scene-i", "lt-scene-u", "lt-scene-s" }) Assert.IsNotNull(bar.Q(name), name);
            Assert.IsNotNull(bar.Q(className: "lt-scene-color"));
            StringAssert.Contains("Editing", bar.Q<Label>("lt-scene-chip").text);

            session.Selectable.Select(6, 11);
            Assert.IsTrue(session.CanFormat(typeof(BoldEffect)));
            session.ToggleFormat(typeof(BoldEffect));
            Assert.AreEqual("Hello <b>brave</b> world", text.Text);
            session.Selectable.Select(0, 5);
            session.ApplyColor(new Color(1, 0, 0));
            StringAssert.Contains("<color=#FF0000>Hello</color>", text.Text);
            session.ShowsMarkup = true;
            Assert.IsTrue(text.Engine.Literal);
            Assert.AreEqual(text.Text, session.Field.ShownText, "the tags themselves show");
            session.ShowsMarkup = false;
            Assert.IsFalse(text.Engine.Literal);
            session.End(LetteraSceneEditEnd.Done);
            Assert.IsFalse(text.Engine.Literal);
            Undo.PerformUndo();
            Assert.AreEqual("Hello brave world", text.Text, "formatting is in the one step");
        }

        [Test]
        public void TheBarSaysWhyWhenItCouldNotStart()
        {
            LetteraWorldText a = Make("A"), b = Make("B");
            LetteraSceneEditView.Begin(new Object[] { a.gameObject, b.gameObject });
            VisualElement bar = new LetteraSceneEditBar().CreatePanelContent();
            StringAssert.Contains("Several", bar.Q<Label>("lt-scene-chip").text);
        }

        [Test]
        public void TheTextsOwnTagsAreOffered()
        {
            LetteraWorldText text = Make("Hello");
            text.Styles.Add(LetteraStyle.ForTag(new ShadowEffect(), "warning"));
            text.MarkStylesDirty();
            text.ForceRebuild();
            LetteraSceneEdit session = Begin(text);
            List<(string tag, Type effect)> tags = session.CommonTags();
            Assert.AreEqual("warning", tags[0].tag, "the text's own come first");
            Assert.IsFalse(tags.Any(t => t.effect == typeof(BoldEffect) || t.effect == typeof(SizeEffect)), "what has a button of its own or needs a value does not");
            session.Selectable.SelectAll();
            session.ToggleFormat(tags[0].effect);
            Assert.AreEqual("<warning>Hello</warning>", text.Text);
        }

        [Test]
        public void TheRightClickOffersTheFieldsCommandsThenFormatting()
        {
            LetteraWorldText text = Make("Hello brave world");
            var selectable = text.gameObject.AddComponent<LetteraSelectable>();
            selectable.Commands.Add(new Shout());
            Shout.Ran = 0;

            LetteraSceneEdit session = Begin(text);
            IReadOnlyList<LetteraSceneEdit.MenuLine> shown = null;
            LetteraSceneEdit.MenuOverride = lines => shown = lines;
            session.RightClick(ScreenOf(text, 7), SceneCamera());
            Assert.IsNotNull(shown, "the editor's menu, and no object on a game canvas");
            Assert.AreEqual((6, 11), (session.Selectable.Selection.Start, session.Selectable.Selection.End), "as in a player, the word under it");

            List<string> titles = shown.Where(l => !l.Separator).Select(l => l.Title).ToList();
            var expected = new List<string>();
            foreach (LetteraEditCommand command in LetteraSettings.Instance.Editing.Commands) expected.Add(command.Title);
            expected.Add("Shout it");
            CollectionAssert.AreEqual(expected, titles.Take(expected.Count).ToList(), "the same titles, custom commands included");
            int separator = shown.ToList().FindIndex(l => l.Separator);
            Assert.AreEqual(expected.Count, separator);
            StringAssert.StartsWith("Format/Bold", shown[separator + 1].Title);

            LetteraSceneEdit.MenuLine copy = shown.First(l => l.Title == new CopyCommand().Title);
            Assert.IsTrue(copy.Enabled);
            copy.Run();
            Assert.AreEqual("brave", Clip.Text(LetteraClipFormat.PlainText));
            shown.First(l => l.Title == "Shout it").Run();
            Assert.AreEqual(1, Shout.Ran);
            shown.First(l => l.Title == "Format/Bold").Run();
            Assert.AreEqual("Hello <b>brave</b> world", text.Text);
            shown.First(l => l.Title == new SelectAllCommand().Title).Run();
            Assert.AreEqual(session.Field.Length, session.Selectable.Selection.Length);
            Assert.IsFalse(selectable.HasFocus, "what is on the object stays out of it");
        }

        [Test]
        public void TheMenuKeyGoesToTheEditorsMenuToo()
        {
            LetteraWorldText text = Make("Hello");
            LetteraSceneEdit session = Begin(text);
            int shown = 0;
            LetteraSceneEdit.MenuOverride = _ => shown++;
            session.Selectable.Input.Key(LetteraKeyCode.Menu, LetteraKeyModifiers.None);
            Assert.AreEqual(1, shown);
        }

        // ------------------------------------------------------------------------------------- ways in

        [Test]
        public void TheCommandLineStartsOrSaysWhy()
        {
            LetteraWorldText text = Make("Hello");
            text.name = "Spoken";
            object answer = LetteraCli.EditInScene("Spoken");
            var d = (Dictionary<string, object>)answer;
            Assert.IsFalse(d.ContainsKey("error"), LetteraCliJson.Write(answer));
            Assert.AreSame(text.gameObject, Selection.activeGameObject);
            if (SceneView.lastActiveSceneView == null)
            {
                Assert.AreEqual(false, d["started"]);
                StringAssert.Contains("Scene view", (string)d["why"]);
            }
            else
            {
                Assert.AreEqual(true, d["started"]);
                Assert.IsNotNull(LetteraSceneEdit.Current);
            }
        }

        [Test]
        public void TheMenuAndTheShortcutExist()
        {
            Assert.IsTrue(TypeCache.GetMethodsWithAttribute<MenuItem>().Any(m => m.GetCustomAttributes(typeof(MenuItem), false).Cast<MenuItem>().Any(a => a.menuItem == "Tools/Lettera/Edit Text In Scene")));
            UnityEditor.ShortcutManagement.ShortcutBinding binding = UnityEditor.ShortcutManagement.ShortcutManager.instance.GetShortcutBinding("Lettera/Edit Text In Scene");
            Assert.AreEqual(KeyCode.F2, binding.keyCombinationSequence.First().keyCode);
            Assert.IsFalse(binding.keyCombinationSequence.First().action, "no modifier");
        }

    }
}

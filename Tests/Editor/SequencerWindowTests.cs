using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace FirstSymbol.Lettera.Editor.Tests
{
    /// <summary>The window's model by itself, then the window through its panel's events.</summary>
    public class SequencerWindowTests : TextFixture
    {
        const string TempScene = "Assets/_LetteraSequencerWindow.unity";

        LetteraSequencerWindow m_Window;
        string m_Clip = "";
        Func<string> m_SavedRead;
        Action<string> m_SavedWrite;

        [SetUp]
        public void Fake()
        {
            m_SavedRead = LetteraSequencerWindow.ReadClip;
            m_SavedWrite = LetteraSequencerWindow.WriteClip;
            LetteraSequencerWindow.ReadClip = () => m_Clip;
            LetteraSequencerWindow.WriteClip = text => m_Clip = text;
        }

        [TearDown]
        public void Drop()
        {
            LetteraSequencerWindow.ReadClip = m_SavedRead;
            LetteraSequencerWindow.WriteClip = m_SavedWrite;
            if (m_Window != null) m_Window.Close();
            m_Window = null;
            if (File.Exists(TempScene))
            {
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                AssetDatabase.DeleteAsset(TempScene);
            }
        }

        // ------------------------------------------------------------- the view

        [Test]
        public void ZoomKeepsTheMomentUnderThePointer()
        {
            var view = new SequencerView { PixelsPerSecond = 100, ScrollSeconds = 2 };
            float moment = view.T(300);
            view.ZoomAt(300, 1.7f);
            Assert.AreEqual(170, view.PixelsPerSecond, 1e-3f);
            Assert.AreEqual(moment, view.T(300), 1e-3f);
        }

        [Test]
        public void ZoomAndPanStopAtTheirEnds()
        {
            var view = new SequencerView();
            for (int i = 0; i < 200; i++) view.ZoomAt(100, 2);
            Assert.AreEqual(SequencerView.MaxScale, view.PixelsPerSecond);
            for (int i = 0; i < 200; i++) view.ZoomAt(100, 0.5f);
            Assert.AreEqual(SequencerView.MinScale, view.PixelsPerSecond);
            view.Pan(10000, 10000);
            Assert.AreEqual(0, view.ScrollSeconds);
            Assert.AreEqual(0, view.ScrollY);
        }

        [TestCase(80f, 1f)]
        [TestCase(30f, 5f)]
        [TestCase(700f, 0.1f)]
        public void TicksAreRound(float scale, float step)
        {
            Assert.AreEqual(step, new SequencerView { PixelsPerSecond = scale }.TickStep(), 1e-4f);
        }

        [Test]
        public void FitShowsEverything()
        {
            var view = new SequencerView { ScrollSeconds = 5 };
            view.Fit(10, 500);
            Assert.AreEqual(0, view.ScrollSeconds);
            Assert.Less(view.X(10), 500);
            Assert.Greater(view.X(10), 450);
        }

        // ------------------------------------------------------------- the model

        static LetteraCue Cue(float start, float unit, int lane = 0, float from = 0, float to = 1) => new LetteraCue
        {
            Start = start,
            UnitSeconds = unit,
            Lane = lane,
            From = LetteraValue.Of(from),
            To = LetteraValue.Of(to),
            Target = new LetteraParamTarget { Parameter = "Amplitude" },
        };

        static SequencerModel Model(params LetteraCue[] cues)
        {
            var list = new List<LetteraCue>(cues);
            return new SequencerModel(() => list);
        }

        static List<LetteraCue> CuesOf(SequencerModel model)
        {
            var field = typeof(SequencerModel).GetField("m_Cues", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            return ((Func<List<LetteraCue>>)field.GetValue(model))();
        }

        static LetteraParamKind Number(LetteraCue _) => LetteraParamKind.Float;

        static void Drag(SequencerModel model, int cue, SequencerZone zone, float fromX, float toX, int fromRow = 0, int toRow = 0, bool free = false)
        {
            model.Click(cue, false);
            model.BeginDrag(cue, zone, fromX, fromRow);
            model.UpdateDrag(toX, toRow, free);
            model.EndDrag();
        }

        [Test]
        public void AHitFindsTheBodyAndEachEdge()
        {
            SequencerModel model = Model(Cue(1, 2));
            model.View.PixelsPerSecond = 100;
            Assert.AreEqual(0, model.Hit(200, 10, out SequencerZone zone));
            Assert.AreEqual(SequencerZone.Body, zone);
            model.Hit(102, 10, out zone);
            Assert.AreEqual(SequencerZone.Start, zone);
            model.Hit(298, 10, out zone);
            Assert.AreEqual(SequencerZone.End, zone);
            Assert.AreEqual(-1, model.Hit(50, 10, out _));
            Assert.AreEqual(-1, model.Hit(200, 40, out _));
        }

        [Test]
        public void ADragMovesInTimeAndAcrossRows()
        {
            SequencerModel model = Model(Cue(1, 2));
            model.View.PixelsPerSecond = 100;
            Drag(model, 0, SequencerZone.Body, 200, 333, 0, 2, true);
            LetteraCue cue = CuesOf(model)[0];
            Assert.AreEqual(2.33f, cue.Start, 1e-3f);
            Assert.AreEqual(2, cue.Lane);
            Assert.AreEqual(2, cue.UnitSeconds, 1e-3f);
        }

        [Test]
        public void ADragStopsAtZeroAndTheSelectionStaysTogether()
        {
            SequencerModel model = Model(Cue(1, 1), Cue(3, 1, 1));
            model.View.PixelsPerSecond = 100;
            model.SelectAll();
            model.BeginDrag(1, SequencerZone.Body, 350, 1);
            model.UpdateDrag(0, 0, true);
            model.EndDrag();
            List<LetteraCue> cues = CuesOf(model);
            Assert.AreEqual(0, cues[0].Start, 1e-3f);
            Assert.AreEqual(2, cues[1].Start, 1e-3f, "the distance between them held");
            Assert.AreEqual(0, cues[0].Lane);
            Assert.AreEqual(1, cues[1].Lane, "rows stop together");
        }

        [Test]
        public void EdgesResizeAndKeepTheOtherEnd()
        {
            SequencerModel model = Model(Cue(1, 2));
            model.View.PixelsPerSecond = 100;
            LetteraCue cue = CuesOf(model)[0];
            Drag(model, 0, SequencerZone.End, 300, 350, 0, 0, true);
            Assert.AreEqual(1, cue.Start, 1e-3f);
            Assert.AreEqual(2.5f, cue.UnitSeconds, 1e-3f);
            Drag(model, 0, SequencerZone.Start, 100, 150, 0, 0, true);
            Assert.AreEqual(1.5f, cue.Start, 1e-3f);
            Assert.AreEqual(2, cue.UnitSeconds, 1e-3f);
            Drag(model, 0, SequencerZone.Start, 150, 2000, 0, 0, true);
            Assert.AreEqual(SequencerModel.MinUnit, cue.UnitSeconds, 1e-3f, "it cannot turn inside out");
            Assert.AreEqual(3.5f, cue.Start + cue.UnitSeconds, 1e-3f);
        }

        [Test]
        public void ADragSnapsToOtherCuesAndThePlayheadAndCtrlLetsItGo()
        {
            SequencerModel model = Model(Cue(1, 1), Cue(4.03f, 1, 1));
            model.View.PixelsPerSecond = 100;
            LetteraCue cue = CuesOf(model)[0];

            // Its end lands near the other's start.
            Drag(model, 0, SequencerZone.Body, 150, 150 + 200);
            Assert.AreEqual(3.03f, cue.Start, 1e-3f);

            cue.Start = 1;
            model.Playhead = 2.46f;
            Drag(model, 0, SequencerZone.Body, 150, 150 + 144);
            Assert.AreEqual(2.46f, cue.Start, 1e-3f, "the playhead");

            cue.Start = 1;
            Drag(model, 0, SequencerZone.Body, 150, 150 + 144, 0, 0, true);
            Assert.AreEqual(2.44f, cue.Start, 1e-3f, "free");

            cue.Start = 1;
            model.SnapOn = false;
            Drag(model, 0, SequencerZone.Body, 150, 150 + 144);
            Assert.AreEqual(2.44f, cue.Start, 1e-3f, "snap off");
        }

        [Test]
        public void ASnapToATickNeedsToBeNear()
        {
            SequencerModel model = Model(Cue(0.5f, 0.25f));
            model.View.PixelsPerSecond = 100;
            LetteraCue cue = CuesOf(model)[0];
            Drag(model, 0, SequencerZone.Body, 60, 60 + 151);
            Assert.AreEqual(2f, cue.Start, 1e-3f);
        }

        [Test]
        public void AMarqueeTakesWhatItTouches()
        {
            SequencerModel model = Model(Cue(0, 1), Cue(2, 1), Cue(0, 1, 1), Cue(5, 1, 2));
            model.View.PixelsPerSecond = 100;
            model.Marquee(50, 5, 250, 40, false);
            CollectionAssert.AreEqual(new[] { 0, 1, 2 }, model.Selected);
            model.Marquee(510, 60, 520, 70, true);
            CollectionAssert.AreEqual(new[] { 0, 1, 2, 3 }, model.Selected);
            model.Marquee(900, 0, 950, 10, false);
            Assert.AreEqual(0, model.Selected.Count);
        }

        [Test]
        public void ControlAddsAndTakesAway()
        {
            SequencerModel model = Model(Cue(0, 1), Cue(2, 1));
            model.Click(0, false);
            model.Click(1, true);
            CollectionAssert.AreEqual(new[] { 0, 1 }, model.Selected);
            model.Click(0, true);
            CollectionAssert.AreEqual(new[] { 1 }, model.Selected);
        }

        [Test]
        public void SplitMeetsAtTheValueItHadThere()
        {
            SequencerModel model = Model(Cue(1, 4, 2, 0, 8));
            Assert.IsNull(model.SplitSelected(2, Number));
            List<LetteraCue> cues = CuesOf(model);
            Assert.AreEqual(2, cues.Count);
            Assert.AreEqual((1f, 1f, 0f, 2f), (cues[0].Start, cues[0].UnitSeconds, cues[0].From.Number, cues[0].To.Number));
            Assert.AreEqual((2f, 3f, 2f, 8f), (cues[1].Start, cues[1].UnitSeconds, cues[1].From.Number, cues[1].To.Number));
            Assert.AreEqual(2, cues[1].Lane);
            CollectionAssert.AreEqual(new[] { 1 }, model.Selected);
        }

        [Test]
        public void SplitOfAnAddingCueDoesNotCountTwice()
        {
            LetteraCue cue = Cue(0, 4, 0, 0, 8);
            cue.Mix = LetteraMix.Add;
            Assert.IsNull(SequencerModel.Split(cue, 1, LetteraParamKind.Float, out LetteraCue second));
            Assert.AreEqual(2, cue.To.Number, 1e-3f);
            Assert.AreEqual((0f, 6f), (second.From.Number, second.To.Number), "2 held, 6 more");
        }

        [Test]
        public void SplitOfAMultiplyingCueComposes()
        {
            LetteraCue cue = Cue(0, 4, 0, 1, 3);
            cue.Mix = LetteraMix.Multiply;
            Assert.IsNull(SequencerModel.Split(cue, 2, LetteraParamKind.Float, out LetteraCue second));
            Assert.AreEqual(2, cue.To.Number, 1e-3f);
            Assert.AreEqual(1, second.From.Number, 1e-3f);
            Assert.AreEqual(1.5f, second.To.Number, 1e-3f);
        }

        [Test]
        public void SplitOfAColorMeetsAtItsColor()
        {
            LetteraCue cue = Cue(0, 2);
            cue.From = LetteraValue.Of(new Color32(0, 0, 0, 255));
            cue.To = LetteraValue.Of(new Color32(200, 100, 0, 255));
            Assert.IsNull(SequencerModel.Split(cue, 1, LetteraParamKind.Color, out LetteraCue second));
            Assert.AreEqual(100, cue.To.Color.r);
            Assert.AreEqual(100, second.From.Color.r);
            Assert.AreEqual(200, second.To.Color.r);
        }

        [Test]
        public void WhatCannotBeSplitSaysWhy()
        {
            LetteraCue staggered = Cue(0, 2);
            staggered.StepSeconds = 0.1f;
            StringAssert.Contains("staggered", SequencerModel.Split(staggered, 1, LetteraParamKind.Float, out _));
            StringAssert.Contains("not inside", SequencerModel.Split(Cue(0, 2), 5, LetteraParamKind.Float, out _));
            SequencerModel model = Model(Cue(0, 1));
            StringAssert.Contains("not inside", model.SplitSelected(5, Number));
        }

        [Test]
        public void DuplicateFollowsAndCopyPasteGoesThroughText()
        {
            SequencerModel model = Model(Cue(1, 2, 3, 4, 5), Cue(6, 1));
            List<LetteraCue> cues = CuesOf(model);
            model.Click(0, false);
            model.Duplicate();
            Assert.AreEqual(3, cues.Count);
            Assert.AreEqual(3, cues[2].Start, 1e-3f);
            Assert.AreEqual(3, cues[2].Lane);
            Assert.AreEqual(4, cues[2].From.Number);
            Assert.AreNotSame(cues[0], cues[2]);
            CollectionAssert.AreEqual(new[] { 2 }, model.Selected);

            model.Click(0, false);
            model.Click(1, true);
            string text = model.CopyText();
            Assert.IsTrue(SequencerModel.CanPaste(text));
            Assert.IsTrue(model.Paste(text, 10));
            Assert.AreEqual(5, cues.Count);
            Assert.AreEqual(10, cues[3].Start, 1e-3f);
            Assert.AreEqual(15, cues[4].Start, 1e-3f, "distances kept");
            CollectionAssert.AreEqual(new[] { 3, 4 }, model.Selected);
            Assert.IsFalse(model.Paste("nothing of ours", 0));
            Assert.IsFalse(model.Paste("LetteraCues:{ broken", 0));
        }

        [Test]
        public void MuteGoesAllTheWayFirstAndDeleteLeavesTheRest()
        {
            SequencerModel model = Model(Cue(0, 1), Cue(1, 1), Cue(2, 1));
            List<LetteraCue> cues = CuesOf(model);
            cues[0].Muted = true;
            model.SelectAll();
            model.ToggleMute();
            Assert.IsTrue(cues.All(c => c.Muted));
            model.ToggleMute();
            Assert.IsFalse(cues.Any(c => c.Muted));
            LetteraCue last = cues[2];
            model.ClearSelection();
            model.Click(0, false);
            model.Click(1, true);
            model.Delete();
            Assert.AreEqual(1, cues.Count);
            Assert.AreSame(last, cues[0]);
            Assert.AreEqual(0, model.Selected.Count);
        }

        [Test]
        public void ANewCueFindsAFreeRow()
        {
            SequencerModel model = Model(Cue(0, 2), Cue(0, 2, 1));
            Assert.AreEqual(2, model.FreeRow(1));
            Assert.AreEqual(0, model.FreeRow(5));
            Assert.AreEqual(2, model.RowCount);
        }

        // ------------------------------------------------------------- the running sequencer

        LetteraSequencer Sequenced(out LetteraWorldText text, params LetteraCue[] cues)
        {
            text = Make("");
            text.Styles.Clear();
            text.Styles.Add(LetteraStyle.ForTag(new WaveMotion(), "go"));
            text.Text = "<go>wave</go>";
            text.ForceRebuild();
            var sequencer = text.gameObject.AddComponent<LetteraSequencer>();
            sequencer.Cues.AddRange(cues);
            return sequencer;
        }

        [Test]
        public void ACueThatLeavesTheListLetsGoOfItsHold()
        {
            LetteraSequencer sequencer = Sequenced(out LetteraWorldText text, Cue(0, 1), Cue(1, 1));
            sequencer.Seek(0.5f);
            Assert.AreEqual(2, text.Engine.HoldCount);
            sequencer.Cues.RemoveAt(0);
            sequencer.Seek(0.5f);
            Assert.AreEqual(1, text.Engine.HoldCount);
            sequencer.EndPreview();
            Assert.AreEqual(0, text.Engine.HoldCount);
        }

        [Test]
        public void APreviewNeverWritesTheField()
        {
            LetteraSequencer sequencer = Sequenced(out _, Cue(0, 2));
            var serialized = new SerializedObject(sequencer);
            serialized.FindProperty("m_Progress").floatValue = 0.25f;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            sequencer.Seek(1);
            Assert.AreEqual(0.5f, sequencer.Progress, 1e-3f);
            sequencer.Progress = 0.75f;
            sequencer.Stop();
            serialized.Update();
            Assert.AreEqual(0.25f, serialized.FindProperty("m_Progress").floatValue, 1e-4f);
            sequencer.EndPreview();
            Assert.AreEqual(0.25f, sequencer.Progress, 1e-4f);
            Assert.AreEqual(0.5f, sequencer.Playhead, 1e-3f, "and it stands where the field says");
        }

        [Test]
        public void AFieldWrittenByACurveStillMovesIt()
        {
            LetteraSequencer sequencer = Sequenced(out LetteraWorldText text, Cue(0, 2));
            var serialized = new SerializedObject(sequencer);
            serialized.FindProperty("m_Progress").floatValue = 0.5f;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            Assert.AreEqual(0.5f, sequencer.Progress, 1e-3f, "what it reads, before any tick");
            sequencer.SendMessage("OnDidApplyAnimationProperties");
            Assert.AreEqual(1f, sequencer.Playhead, 1e-3f);
            Assert.AreEqual(1, text.Engine.HoldCount);
        }

        // ------------------------------------------------------------- the window

        LetteraSequencerWindow Open(LetteraSequencer sequencer)
        {
            m_Window = ScriptableObject.CreateInstance<LetteraSequencerWindow>();
            m_Window.Show();
            m_Window.SetTarget(sequencer);
            // The first event through a panel lays it out, and a first layout fits the view to the cues.
            if (m_Window.TracksElement.panel != null) Pointer<PointerMoveEvent>(m_Window.TracksElement, EventType.MouseMove, Vector2.zero);
            return m_Window;
        }

        static void AssertOurs(VisualElement root)
        {
            Assert.IsTrue(root.ClassListContains("lt-root"));
            Assert.AreEqual(0, root.Query<IMGUIContainer>().ToList().Count);
            Assert.AreEqual(0, root.Query(className: "unity-list-view").ToList().Count);
        }

        static void Pointer<T>(VisualElement element, EventType type, Vector2 local, int button = 0, EventModifiers modifiers = EventModifiers.None, int clicks = 1)
            where T : PointerEventBase<T>, new()
        {
            var imgui = new Event { type = type, mousePosition = element.LocalToWorld(local), button = button, modifiers = modifiers, clickCount = clicks };
            using (T e = PointerEventBase<T>.GetPooled(imgui))
            {
                e.target = element;
                element.SendEvent(e);
            }
        }

        static void Down(VisualElement element, Vector2 at, EventModifiers modifiers = EventModifiers.None, int clicks = 1) => Pointer<PointerDownEvent>(element, EventType.MouseDown, at, 0, modifiers, clicks);
        static void Move(VisualElement element, Vector2 at, EventModifiers modifiers = EventModifiers.None) => Pointer<PointerMoveEvent>(element, EventType.MouseDrag, at, 0, modifiers);
        static void Up(VisualElement element, Vector2 at, EventModifiers modifiers = EventModifiers.None) => Pointer<PointerUpEvent>(element, EventType.MouseUp, at, 0, modifiers);

        static void Key(VisualElement element, KeyCode key, EventModifiers modifiers = EventModifiers.None)
        {
            using (KeyDownEvent e = KeyDownEvent.GetPooled('\0', key, modifiers))
            {
                e.target = element;
                element.SendEvent(e);
            }
        }

        void Panel()
        {
            if (m_Window.TracksElement.panel == null) Assert.Ignore("This editor gave the window no panel, as a batch one may. Run it in a visible one.");
        }

        Scene SavedScene(out LetteraSequencer sequencer, out LetteraWorldText text, params LetteraCue[] cues)
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            sequencer = Sequenced(out text, cues);
            Scene scene = SceneManager.GetActiveScene();
            Assert.IsTrue(EditorSceneManager.SaveScene(scene, TempScene));
            Assert.IsFalse(scene.isDirty);
            return scene;
        }

        [Test]
        public void TheWindowIsOurOwnAndSaysWhatToDoWhenEmpty()
        {
            m_Window = ScriptableObject.CreateInstance<LetteraSequencerWindow>();
            m_Window.Show();
            m_Window.EnsureBuilt();
            AssertOurs(m_Window.RootElement);
            Assert.IsTrue(m_Window.RootElement.Query<Label>().ToList().Any(l => l.text.Contains("Select a text")));
        }

        [Test]
        public void ADragThroughThePanelMovesACueInOneUndo()
        {
            LetteraSequencer sequencer = Sequenced(out _, Cue(1, 2));
            Open(sequencer);
            Panel();
            VisualElement tracks = m_Window.TracksElement;
            m_Window.Model.View.PixelsPerSecond = 100;
            m_Window.Model.SnapOn = false;
            Undo.IncrementCurrentGroup();
            Down(tracks, new Vector2(200, 10));
            CollectionAssert.AreEqual(new[] { 0 }, m_Window.Model.Selected);
            Move(tracks, new Vector2(220, 10));
            Move(tracks, new Vector2(260, 36));
            Move(tracks, new Vector2(300, 36));
            Up(tracks, new Vector2(300, 36));
            Assert.AreEqual(2, sequencer.Cues[0].Start, 1e-3f);
            Assert.AreEqual(1, sequencer.Cues[0].Lane);
            Undo.PerformUndo();
            Assert.AreEqual(1, sequencer.Cues[0].Start, 1e-3f, "all of the drag in one step");
            Assert.AreEqual(0, sequencer.Cues[0].Lane);
            Undo.PerformRedo();
            Assert.AreEqual(2, sequencer.Cues[0].Start, 1e-3f);
        }

        [Test]
        public void AnEdgeThroughThePanelResizes()
        {
            LetteraSequencer sequencer = Sequenced(out _, Cue(1, 2));
            Open(sequencer);
            Panel();
            VisualElement tracks = m_Window.TracksElement;
            m_Window.Model.View.PixelsPerSecond = 100;
            m_Window.Model.SnapOn = false;
            Down(tracks, new Vector2(298, 10));
            Move(tracks, new Vector2(320, 10));
            Move(tracks, new Vector2(348, 10));
            Up(tracks, new Vector2(348, 10));
            Assert.AreEqual(2.5f, sequencer.Cues[0].UnitSeconds, 1e-2f);
            Assert.AreEqual(1, sequencer.Cues[0].Start, 1e-3f);
        }

        [Test]
        public void AMarqueeThroughThePanelSelectsAndTheSharedFieldsShow()
        {
            LetteraSequencer sequencer = Sequenced(out _, Cue(0, 1), Cue(2, 1), Cue(5, 1));
            Open(sequencer);
            Panel();
            VisualElement tracks = m_Window.TracksElement;
            m_Window.Model.View.PixelsPerSecond = 100;
            Down(tracks, new Vector2(50, 60));
            Move(tracks, new Vector2(100, 40));
            Move(tracks, new Vector2(260, 2));
            Up(tracks, new Vector2(260, 2));
            CollectionAssert.AreEqual(new[] { 0, 1 }, m_Window.Model.Selected);
            VisualElement shared = m_Window.DetailElement.Q("lt-seq-shared");
            Assert.IsNotNull(shared);
            Assert.AreEqual("—", shared.Q<TextField>("lt-seq-field-Start").value, "they differ");
            Assert.AreEqual("1", shared.Q<TextField>("lt-seq-field-Length").value, "they agree");
            AssertOurs(m_Window.RootElement);
        }

        [Test]
        public void ADoubleClickOpensTheFullEditorInTheWindow()
        {
            LetteraSequencer sequencer = Sequenced(out _, Cue(1, 2));
            Open(sequencer);
            Panel();
            VisualElement tracks = m_Window.TracksElement;
            m_Window.Model.View.PixelsPerSecond = 100;
            Down(tracks, new Vector2(200, 10), EventModifiers.None, 2);
            Up(tracks, new Vector2(200, 10));
            Assert.IsNotNull(m_Window.DetailElement.Q("lt-seq-full"));
            AssertOurs(m_Window.RootElement);
        }

        [Test]
        public void EachEditFromTheKeysIsOneUndo()
        {
            LetteraSequencer sequencer = Sequenced(out _, Cue(1, 4, 0, 0, 8));
            Open(sequencer);
            Panel();
            VisualElement root = m_Window.RootElement, tracks = m_Window.TracksElement;
            m_Window.Model.View.PixelsPerSecond = 100;
            Down(tracks, new Vector2(300, 10));
            Up(tracks, new Vector2(300, 10));
            sequencer.Seek(2);
            m_Window.Model.Playhead = 2;

            int Count() => sequencer.Cues.Count;
            void Check(KeyCode key, EventModifiers modifiers, Func<bool> done, string what)
            {
                Undo.IncrementCurrentGroup();
                int before = Count();
                string state = JsonUtility.ToJson(sequencer);
                Key(root, key, modifiers);
                Assert.IsTrue(done(), what);
                Undo.PerformUndo();
                Assert.AreEqual(before, Count(), what + " undone");
                StringAssert.AreEqualIgnoringCase(state, JsonUtility.ToJson(sequencer), what + " undone whole");
                m_Window.Model.ClampSelection();
                m_Window.Model.Selected.Clear();
                m_Window.Model.Selected.Add(0);
            }

            Check(KeyCode.S, EventModifiers.None, () => Count() == 2, "split");
            Check(KeyCode.D, EventModifiers.Control, () => Count() == 2, "duplicate");
            Check(KeyCode.M, EventModifiers.None, () => sequencer.Cues[0].Muted, "mute");
            Check(KeyCode.Delete, EventModifiers.None, () => Count() == 0, "delete");
            Key(root, KeyCode.C, EventModifiers.Control);
            StringAssert.StartsWith("LetteraCues:", m_Clip);
            Check(KeyCode.V, EventModifiers.Control, () => Count() == 2, "paste");
        }

        [Test]
        public void TheToolbarSaysEachShortcut()
        {
            LetteraSequencer sequencer = Sequenced(out _, Cue(1, 2));
            Open(sequencer);
            List<string> tips = m_Window.RootElement.Query<Label>(className: "lt-tool").ToList().Select(l => l.tooltip).ToList();
            foreach (string key in new[] { "(Space)", "(S)", "(Ctrl+D)", "(Ctrl+C)", "(Ctrl+V)", "(M)", "(Delete)", "(F)" })
                Assert.IsTrue(tips.Any(t => t != null && t.Contains(key)), key);
        }

        [Test]
        public void SpacePlaysAndPauses()
        {
            LetteraSequencer sequencer = Sequenced(out _, Cue(0, 2));
            Open(sequencer);
            Panel();
            Key(m_Window.RootElement, KeyCode.Space);
            Assert.IsTrue(sequencer.IsPlaying);
            Key(m_Window.RootElement, KeyCode.Space);
            Assert.IsFalse(sequencer.IsPlaying);
        }

        [Test]
        public void TheRulerScrubsWithNoRebuildAndNoChangeToTheScene()
        {
            Scene scene = SavedScene(out LetteraSequencer sequencer, out LetteraWorldText text, Cue(0, 4));
            Open(sequencer);
            Panel();
            VisualElement ruler = m_Window.RulerElement;
            m_Window.Model.View.PixelsPerSecond = 100;
            text.ForceRebuild();
            int rebuilds = text.RebuildCount, parses = text.Engine.ParseCount;
            Down(ruler, new Vector2(100, 5));
            Assert.AreEqual(1, sequencer.Playhead, 1e-2f);
            for (int x = 110; x <= 390; x += 7) Move(ruler, new Vector2(x, 5));
            Up(ruler, new Vector2(390, 5));
            Assert.AreEqual(3.9f, sequencer.Playhead, 1e-2f);
            LetteraMotionHub.Tick(0.0001f, 0.0001f);
            text.ForceRebuild();
            Assert.AreEqual(3.9f / 4, text.Engine.MotionValueForTests(0, 0), 1e-2f, "it shows");
            Assert.AreEqual(rebuilds, text.RebuildCount, "no rebuild");
            Assert.AreEqual(parses, text.Engine.ParseCount);
            Assert.IsFalse(scene.isDirty, "the scene is as it was saved");
            m_Window.Close();
            m_Window = null;
            Assert.AreEqual(0, text.Engine.HoldCount, "closing lets go of everything");
            Assert.IsFalse(LetteraMotionPreview.IsOn);
            Assert.IsFalse(scene.isDirty);
        }

        [Test]
        public void AnEditDoesDirtyTheScene()
        {
            Scene scene = SavedScene(out LetteraSequencer sequencer, out _, Cue(0, 4));
            Open(sequencer);
            Panel();
            m_Window.Model.SelectAll();
            Key(m_Window.RootElement, KeyCode.M);
            Assert.IsTrue(sequencer.Cues[0].Muted);
            Assert.IsTrue(scene.isDirty);
        }

        [Test]
        public void TheAssemblyForTimelineComesOnlyWithTimeline()
        {
            string[] loaded = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetName().Name).ToArray();
            bool timeline = loaded.Contains("Unity.Timeline");
            Assert.AreEqual(timeline, loaded.Contains("FirstSymbol.Lettera.Timeline"));
            Assert.AreEqual(timeline, loaded.Contains("FirstSymbol.Lettera.Timeline.Editor"));
        }
    }
}

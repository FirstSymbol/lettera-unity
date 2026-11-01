using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Timeline;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace FirstSymbol.Lettera.Timeline.Editor.Tests
{
    /// <summary>Menu entries, labels, inspectors and the binding picker.</summary>
    public class TimelineEditorTests : TimelineFixture
    {
        static void AssertOurs(VisualElement root, string what)
        {
            Assert.IsNotNull(root, what);
            Assert.IsTrue(root.ClassListContains("lt-root"), what);
            Assert.AreEqual(0, root.Query<IMGUIContainer>().ToList().Count, what);
            Assert.AreEqual(0, root.Query(className: "unity-list-view").ToList().Count, what);
        }

        [Test]
        public void EveryTrackIsInTheLetteraGroupWithItsColor()
        {
            foreach (System.Type track in new[] { typeof(LetteraSequenceTrack), typeof(LetteraParamTrack), typeof(LetteraTextTrack) })
            {
                var name = (DisplayNameAttribute)System.Attribute.GetCustomAttribute(track, typeof(DisplayNameAttribute));
                StringAssert.StartsWith("Lettera/", name.DisplayName);
                Assert.IsNotNull(System.Attribute.GetCustomAttribute(track, typeof(TrackColorAttribute)), track.Name);
                Assert.IsNotNull(System.Attribute.GetCustomAttribute(track, typeof(TrackClipTypeAttribute)), track.Name);
            }
        }

        [Test]
        public void EveryTrackHasItsIcon()
        {
            TimelineAsset timeline = Timeline();
            var tracks = new (TrackEditor editor, TrackAsset track)[]
            {
                (new LetteraSequenceTrackEditor(), timeline.CreateTrack<LetteraSequenceTrack>(null, "S")),
                (new LetteraParamTrackEditor(), timeline.CreateTrack<LetteraParamTrack>(null, "P")),
                (new LetteraTextTrackEditor(), timeline.CreateTrack<LetteraTextTrack>(null, "T")),
            };
            foreach ((TrackEditor editor, TrackAsset track) in tracks)
                Assert.IsNotNull(editor.GetTrackOptions(track, null).icon, track.name);
            Assert.AreNotSame(LetteraTrackIcons.Of('S'), LetteraTrackIcons.Of('T'));
        }

        [Test]
        public void AClipsLabelSaysWhatItDoes()
        {
            TimelineAsset timeline = Timeline();
            TimelineClip line = timeline.CreateTrack<LetteraTextTrack>(null, "T").CreateClip<LetteraTextShot>();
            ((LetteraTextShot)line.asset).Text = "Run!";
            ((LetteraTextShot)line.asset).Typewriter = true;
            new LetteraTextShotEditor().OnClipChanged(line);
            Assert.AreEqual("Types: Run!", line.displayName);

            TimelineClip sequence = timeline.CreateTrack<LetteraSequenceTrack>(null, "S").CreateClip<LetteraSequenceShot>();
            ((LetteraSequenceShot)sequence.asset).From = 0.25f;
            ((LetteraSequenceShot)sequence.asset).To = 0.75f;
            new LetteraSequenceShotEditor().OnClipChanged(sequence);
            Assert.AreEqual("25% to 75%", sequence.displayName);

            TimelineClip param = timeline.CreateTrack<LetteraParamTrack>(null, "P").CreateClip<LetteraParamShot>();
            ((LetteraParamShot)param.asset).To = 2.5f;
            new LetteraParamShotEditor().OnClipChanged(param);
            Assert.AreEqual("0 to 2.5", param.displayName);
        }

        [Test]
        public void ClipInspectorsAreOurOwn()
        {
            TimelineAsset timeline = Timeline();
            var shots = new Object[]
            {
                timeline.CreateTrack<LetteraTextTrack>(null, "T").CreateClip<LetteraTextShot>().asset,
                timeline.CreateTrack<LetteraSequenceTrack>(null, "S").CreateClip<LetteraSequenceShot>().asset,
                timeline.CreateTrack<LetteraParamTrack>(null, "P").CreateClip<LetteraParamShot>().asset,
            };
            foreach (Object shot in shots)
            {
                UnityEditor.Editor editor = UnityEditor.Editor.CreateEditor(shot);
                VisualElement root = editor.CreateInspectorGUI();
                AssertOurs(root, shot.GetType().Name);
                Assert.IsNotNull(root.Q<Label>("lt-shot-line"), shot.GetType().Name);
                Object.DestroyImmediate(editor);
            }
        }

        [Test]
        public void TheUnitRowShowsOnlyWithATypewriter()
        {
            TimelineAsset timeline = Timeline();
            var shot = (LetteraTextShot)timeline.CreateTrack<LetteraTextTrack>(null, "T").CreateClip<LetteraTextShot>().asset;
            UnityEditor.Editor editor = UnityEditor.Editor.CreateEditor(shot);
            VisualElement root = editor.CreateInspectorGUI();
            VisualElement unit = root.Query<Label>(className: "lt-row__label").ToList().First(l => l.text == "Unit").parent;
            Assert.AreEqual(DisplayStyle.None, unit.style.display.value);
            Object.DestroyImmediate(editor);
        }

        LetteraWorldText Named(string name)
        {
            LetteraWorldText text = Make("x");
            text.gameObject.name = name;
            return text;
        }

        GameObject Plain(string name) => Own(new GameObject(name));

        [Test]
        public void ThePickerOffersOnlyWhatFitsAndSaysWhatItWillAdd()
        {
            NewScene();
            LetteraWorldText bare = Named("Bare text");
            LetteraWorldText sequenced = Named("Sequenced text");
            sequenced.gameObject.AddComponent<LetteraSequencer>();
            GameObject plain = Plain("Plain object");
            TimelineAsset timeline = Timeline();

            var track = timeline.CreateTrack<LetteraSequenceTrack>(null, "S");
            List<LetteraTrackBinding.Candidate> choices = LetteraTrackBinding.Candidates(track);
            Assert.AreEqual(2, choices.Count);
            Assert.IsTrue(choices.First(c => c.Label == "Bare text").NeedsAdd);
            Assert.IsFalse(choices.First(c => c.Label == "Sequenced text").NeedsAdd);
            Assert.IsFalse(choices.Any(c => c.Label == "Plain object"));
            Assert.IsFalse(LetteraTrackBinding.Accepts(track, plain));
            Assert.IsTrue(LetteraTrackBinding.Accepts(track, bare.gameObject));

            var lines = timeline.CreateTrack<LetteraTextTrack>(null, "T");
            Assert.AreEqual(2, LetteraTrackBinding.Candidates(lines).Count);
            Assert.IsFalse(LetteraTrackBinding.Candidates(lines).Any(c => c.NeedsAdd));
            StringAssert.Contains("no Lettera text", LetteraTrackBinding.Problem(lines, plain));
            Assert.IsNull(LetteraTrackBinding.Problem(lines, bare.gameObject));
        }

        [Test]
        public void ABindingWithNoSequencerAddsOneInOneUndo()
        {
            NewScene();
            LetteraWorldText bare = Named("Bare text");
            TimelineAsset timeline = Timeline();
            var track = timeline.CreateTrack<LetteraSequenceTrack>(null, "S");
            PlayableDirector director = Director(timeline);
            LetteraTrackBinding.Candidate choice = LetteraTrackBinding.Candidates(track).First(c => c.Label == "Bare text");
            Object bound = LetteraTrackBinding.Bind(director, track, choice);
            Assert.IsInstanceOf<LetteraSequencer>(bound);
            Assert.AreSame(bound, director.GetGenericBinding(track));
            Assert.IsNotNull(bare.GetComponent<LetteraSequencer>());
            Undo.PerformUndo();
            Assert.IsNull(bare.GetComponent<LetteraSequencer>());
        }

        [Test]
        public void TheTrackInspectorListsTheChoicesAndAClickBinds()
        {
            NewScene();
            LetteraWorldText bare = Named("Bare text");
            TimelineAsset timeline = Timeline();
            var track = timeline.CreateTrack<LetteraTextTrack>(null, "T");
            PlayableDirector director = Director(timeline);
            var editor = (LetteraTrackInspector)UnityEditor.Editor.CreateEditor(track);
            VisualElement root = editor.Build(director);
            AssertOurs(root, "track");
            List<Label> rows = root.Query<Label>("lt-binding-choice").ToList();
            Assert.AreEqual(1, rows.Count);
            Assert.AreEqual("Bare text", rows[0].text);
            Object.DestroyImmediate(editor);

            var empty = (LetteraTrackInspector)UnityEditor.Editor.CreateEditor(track);
            VisualElement none = empty.Build(null);
            AssertOurs(none, "no director");
            Assert.AreEqual(0, none.Query<Label>("lt-binding-choice").ToList().Count);
            Object.DestroyImmediate(empty);
            Assert.AreEqual("Bare text", bare.name);
        }

        [Test]
        public void ATextTrackIsSavedWithItsClips()
        {
            var timeline = ScriptableObject.CreateInstance<TimelineAsset>();
            AssetDatabase.CreateAsset(timeline, TempTimeline);
            var track = timeline.CreateTrack<LetteraTextTrack>(null, "T");
            track.CreateClip<LetteraTextShot>();
            AssetDatabase.SaveAssets();
            Assert.IsNotNull(MonoScript.FromScriptableObject(track));
            Assert.IsNotNull(MonoScript.FromScriptableObject((ScriptableObject)track.GetClips().First().asset));
        }
    }
}

using System.Collections.Generic;
using System.IO;
using System.Linq;
using FirstSymbol.Lettera.Editor.Tests;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using UnityEngine.SceneManagement;
using UnityEngine.Timeline;
using Object = UnityEngine.Object;

namespace FirstSymbol.Lettera.Timeline.Editor.Tests
{
    /// <summary>What was left compiled and never played: tracks through a real director, preview restore, real curves.</summary>
    public class TimelineProofTests : TimelineFixture
    {
        static TimelineClip Shot(LetteraSequenceTrack track, float from, float to, double start, double duration)
        {
            TimelineClip clip = track.CreateClip<LetteraSequenceShot>();
            var shot = (LetteraSequenceShot)clip.asset;
            shot.From = from;
            shot.To = to;
            clip.start = start;
            clip.duration = duration;
            return clip;
        }

        PlayableDirector Directed(LetteraSequencer sequencer, out LetteraSequenceTrack track)
        {
            TimelineAsset timeline = Timeline();
            track = timeline.CreateTrack<LetteraSequenceTrack>(null, "Sequence");
            PlayableDirector director = Director(timeline);
            director.SetGenericBinding(track, sequencer);
            return director;
        }

        [Test]
        public void ASequenceTrackDrivesProgress()
        {
            LetteraSequencer sequencer = Sequenced(out LetteraWorldText text);
            PlayableDirector director = Directed(sequencer, out LetteraSequenceTrack track);
            Shot(track, 0, 1, 0, 4);
            director.RebuildGraph();
            At(director, 1);
            Assert.AreEqual(0.25f, sequencer.Progress, 1e-3f);
            Assert.AreEqual(0.25f, Held(text), 1e-3f);
            At(director, 2);
            Assert.AreEqual(0.5f, sequencer.Progress, 1e-3f);
            Assert.AreEqual(0.5f, Held(text), 1e-3f);
        }

        [Test]
        public void AParamTrackDrivesTheLink()
        {
            LetteraParamLink link = Linked(out LetteraWorldText text);
            TimelineAsset timeline = Timeline();
            var track = timeline.CreateTrack<LetteraParamTrack>(null, "Param");
            TimelineClip clip = track.CreateClip<LetteraParamShot>();
            clip.duration = 2;
            var shot = (LetteraParamShot)clip.asset;
            shot.From = 0;
            shot.To = 4;
            PlayableDirector director = Director(timeline);
            director.SetGenericBinding(track, link);
            director.RebuildGraph();
            At(director, 1);
            Assert.AreEqual(2, link.Value, 1e-3f);
            Assert.AreEqual(2, Held(text), 1e-3f);
        }

        [Test]
        public void TwoClipsBlendByWeight()
        {
            LetteraSequencer sequencer = Sequenced(out _);
            PlayableDirector director = Directed(sequencer, out LetteraSequenceTrack track);
            TimelineClip first = Shot(track, 0.2f, 0.2f, 0, 3);
            TimelineClip second = Shot(track, 0.8f, 0.8f, 2, 3);
            first.blendOutDuration = 1;
            second.blendInDuration = 1;
            director.RebuildGraph();
            At(director, 1);
            Assert.AreEqual(0.2f, sequencer.Progress, 1e-3f);
            At(director, 2.5);
            Assert.AreEqual(0.5f, sequencer.Progress, 0.02f);
            At(director, 4);
            Assert.AreEqual(0.8f, sequencer.Progress, 1e-3f);
        }

        [Test]
        public void SpeedRunsARampFaster()
        {
            LetteraSequencer sequencer = Sequenced(out _);
            PlayableDirector director = Directed(sequencer, out LetteraSequenceTrack track);
            TimelineClip clip = Shot(track, 0, 1, 0, 4);
            clip.timeScale = 2;
            director.RebuildGraph();
            At(director, 1);
            Assert.AreEqual(0.5f, sequencer.Progress, 1e-3f);
            At(director, 3);
            Assert.AreEqual(1f, sequencer.Progress, 1e-3f);
        }

        [Test]
        public void ClipInStartsPartWay()
        {
            LetteraSequencer sequencer = Sequenced(out _);
            PlayableDirector director = Directed(sequencer, out LetteraSequenceTrack track);
            TimelineClip clip = Shot(track, 0, 1, 0, 4);
            clip.clipIn = 1;
            director.RebuildGraph();
            At(director, 0);
            Assert.AreEqual(0.25f, sequencer.Progress, 1e-3f);
        }

        [Test]
        public void AMutedTrackWritesNothing()
        {
            LetteraSequencer sequencer = Sequenced(out _);
            sequencer.Progress = 0.1f;
            PlayableDirector director = Directed(sequencer, out LetteraSequenceTrack track);
            Shot(track, 0, 1, 0, 4);
            track.muted = true;
            director.RebuildGraph();
            At(director, 2);
            Assert.AreEqual(0.1f, sequencer.Progress, 1e-3f);
        }

        [Test]
        public void AGapHoldsTheLastValue()
        {
            LetteraSequencer sequencer = Sequenced(out _);
            PlayableDirector director = Directed(sequencer, out LetteraSequenceTrack track);
            Shot(track, 0, 0.5f, 0, 1);
            Shot(track, 0.8f, 1, 5, 1);
            director.RebuildGraph();
            At(director, 0.99);
            At(director, 3);
            Assert.AreEqual(0.5f, sequencer.Progress, 0.01f);
        }

        // ------------------------------------------------------------- preview

        static void WriteField(Object target, string name, float value)
        {
            var serialized = new SerializedObject(target);
            serialized.FindProperty(name).floatValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        static float ReadField(Object target, string name) => new SerializedObject(target).FindProperty(name).floatValue;

        [Test]
        public void LeavingPreviewGivesBackTheSerializedValueAndTheSceneStaysClean()
        {
            LetteraSequencer sequencer = Sequenced(out LetteraWorldText text);
            WriteField(sequencer, "m_Progress", 0.25f);
            sequencer.Clock = LetteraClock.Unscaled;
            float restAmplitude = new WaveMotion().Amplitude;
            PlayableDirector director = Directed(sequencer, out LetteraSequenceTrack track);
            Shot(track, 0, 1, 0, 4);
            Scene scene = CleanScene(text.gameObject, director.gameObject);

            var driver = Own(ScriptableObject.CreateInstance<AnimationModeDriver>());
            AnimationMode.StartAnimationMode(driver);
            try
            {
                AnimationMode.BeginSampling();
                ((IPropertyPreview)director.playableAsset).GatherProperties(director, new PreviewCollector());
                AnimationMode.EndSampling();
                director.RebuildGraph();
                At(director, 2);
                Assert.AreEqual(0.5f, sequencer.Progress, 1e-3f);
                Assert.AreEqual(0.5f, Held(text), 1e-3f);
                director.Stop();
            }
            finally
            {
                AnimationMode.StopAnimationMode(driver);
            }
            Assert.AreEqual(0.25f, ReadField(sequencer, "m_Progress"), 1e-4f, "the serialized value");
            Assert.AreEqual(0.25f, sequencer.Progress, 1e-4f, "what it reads");
            Assert.AreEqual(LetteraClock.Unscaled, sequencer.Clock, "its own clock");
            Assert.AreEqual(restAmplitude, Held(text), 1e-3f, "every value goes back");
            Assert.AreEqual(0, text.Engine.HoldCount);
            Assert.IsFalse(scene.isDirty, "the scene");
        }

        [Test]
        public void WithoutAPreviewModeTheGraphLeavingStillGivesItAllBack()
        {
            LetteraSequencer sequencer = Sequenced(out LetteraWorldText text);
            WriteField(sequencer, "m_Progress", 0.25f);
            PlayableDirector director = Directed(sequencer, out LetteraSequenceTrack track);
            Shot(track, 0, 1, 0, 4);
            director.RebuildGraph();
            At(director, 2);
            director.Stop();
            Assert.AreEqual(0.25f, ReadField(sequencer, "m_Progress"), 1e-4f);
            Assert.AreEqual(0, text.Engine.HoldCount);
        }

        [Test]
        public void ALinkGoesBackWhenThePreviewEnds()
        {
            LetteraParamLink link = Linked(out LetteraWorldText text);
            WriteField(link, "m_Value", 0.75f);
            TimelineAsset timeline = Timeline();
            var track = timeline.CreateTrack<LetteraParamTrack>(null, "Param");
            TimelineClip clip = track.CreateClip<LetteraParamShot>();
            clip.duration = 2;
            ((LetteraParamShot)clip.asset).From = 4;
            ((LetteraParamShot)clip.asset).To = 4;
            PlayableDirector director = Director(timeline);
            director.SetGenericBinding(track, link);
            director.RebuildGraph();
            At(director, 1);
            Assert.AreEqual(4, Held(text), 1e-3f);
            director.Stop();
            Assert.AreEqual(0.75f, link.Value, 1e-3f);
            Assert.AreEqual(0.75f, Held(text), 1e-3f);
        }

        // ------------------------------------------------------------- saved assets

        [Test]
        public void TracksAndClipsSurviveBeingSaved()
        {
            var timeline = ScriptableObject.CreateInstance<TimelineAsset>();
            AssetDatabase.CreateAsset(timeline, TempTimeline);
            var sequence = timeline.CreateTrack<LetteraSequenceTrack>(null, "S");
            sequence.CreateClip<LetteraSequenceShot>();
            var param = timeline.CreateTrack<LetteraParamTrack>(null, "P");
            param.CreateClip<LetteraParamShot>();
            AssetDatabase.SaveAssets();
            Assert.IsNotNull(MonoScript.FromScriptableObject(sequence), "sequence track");
            Assert.IsNotNull(MonoScript.FromScriptableObject(param), "param track");
            Assert.IsNotNull(MonoScript.FromScriptableObject((ScriptableObject)sequence.GetClips().First().asset), "sequence clip");
            Assert.IsNotNull(MonoScript.FromScriptableObject((ScriptableObject)param.GetClips().First().asset), "param clip");
            StringAssert.DoesNotContain("guid: 00000000000000000000000000000000", File.ReadAllText(TempTimeline));
        }

        // ------------------------------------------------------------- real curves

        PlayableGraph Curve(GameObject go, AnimationClip clip, out AnimationClipPlayable playable)
        {
            var animator = go.AddComponent<Animator>();
            playable = AnimationPlayableUtilities.PlayClip(animator, clip, out PlayableGraph graph);
            graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            playable.SetPlayState(PlayState.Paused);
            return graph;
        }

        [Test]
        public void AnAlphaCurveOnWorldTextIsAnAlphaPass()
        {
            LetteraWorldText text = Make("abc");
            var clip = Own(new AnimationClip());
            clip.SetCurve("", typeof(LetteraWorldText), "m_Color.a", AnimationCurve.Linear(0, 1, 1, 0));
            PlayableGraph graph = Curve(text.gameObject, clip, out AnimationClipPlayable playable);
            try
            {
                text.ForceRebuild();
                int parses = text.Engine.ParseCount, shapes = text.ShapeCount;
                playable.SetTime(0.5);
                graph.Evaluate();
                Assert.AreEqual(DirtyLevel.Alpha, text.Engine.Dirty, "OnDidApplyAnimationProperties ran and asked only for alpha");
                text.ForceRebuild();
                Assert.AreEqual(0.5f, text.Engine.Colors[0].a / 255f, 0.02f);
                Assert.AreEqual(parses, text.Engine.ParseCount);
                Assert.AreEqual(shapes, text.ShapeCount);
            }
            finally
            {
                graph.Destroy();
            }
        }

        [Test]
        public void ACurveOnProgressMovesTheSequencer()
        {
            LetteraSequencer sequencer = Sequenced(out LetteraWorldText text);
            var clip = Own(new AnimationClip());
            clip.SetCurve("", typeof(LetteraSequencer), "m_Progress", AnimationCurve.Linear(0, 0, 1, 1));
            PlayableGraph graph = Curve(text.gameObject, clip, out AnimationClipPlayable playable);
            try
            {
                playable.SetTime(0.5);
                graph.Evaluate();
                Assert.AreEqual(0.5f, sequencer.Progress, 1e-3f);
                Assert.AreEqual(0.5f, Held(text), 1e-3f);
            }
            finally
            {
                graph.Destroy();
            }
        }

        [Test]
        public void ACurveOnALinkDrivesItsParameter()
        {
            LetteraParamLink link = Linked(out LetteraWorldText text);
            var clip = Own(new AnimationClip());
            clip.SetCurve("", typeof(LetteraParamLink), "m_Value", AnimationCurve.Linear(0, 0, 1, 2));
            PlayableGraph graph = Curve(text.gameObject, clip, out AnimationClipPlayable playable);
            try
            {
                playable.SetTime(0.5);
                graph.Evaluate();
                Assert.AreEqual(1f, link.Value, 1e-3f);
                Assert.AreEqual(1f, Held(text), 1e-3f);
            }
            finally
            {
                graph.Destroy();
            }
        }
    }
}

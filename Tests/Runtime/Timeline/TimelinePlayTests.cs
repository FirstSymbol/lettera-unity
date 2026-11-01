using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using UnityEngine.TestTools;
using UnityEngine.Timeline;
using Object = UnityEngine.Object;

namespace FirstSymbol.Lettera.Timeline.Tests
{
    /// <summary>Tracks and curves by real time in a running player loop.</summary>
    public class TimelinePlayTests
    {
        readonly List<Object> m_Made = new List<Object>();

        T Own<T>(T thing) where T : Object
        {
            m_Made.Add(thing);
            return thing;
        }

        [TearDown]
        public void Drop()
        {
            foreach (Object made in m_Made)
                if (made != null)
                    Object.Destroy(made);
            m_Made.Clear();
        }

        LetteraWorldText Text(string body)
        {
            var go = Own(new GameObject("Text", typeof(RectTransform)));
            ((RectTransform)go.transform).sizeDelta = new Vector2(800, 200);
            var text = go.AddComponent<LetteraWorldText>();
            text.Styles.Add(LetteraStyle.ForTag(new WaveMotion(), "go"));
            text.Text = body;
            return text;
        }

        PlayableDirector Playing(TimelineAsset timeline, TrackAsset track, Object binding)
        {
            var go = Own(new GameObject("Director"));
            var director = go.AddComponent<PlayableDirector>();
            director.playOnAwake = false;
            director.timeUpdateMode = DirectorUpdateMode.GameTime;
            director.extrapolationMode = DirectorWrapMode.Hold;
            director.playableAsset = timeline;
            director.SetGenericBinding(track, binding);
            director.Play();
            return director;
        }

        static IEnumerator WaitFor(System.Func<bool> done, float seconds)
        {
            float until = Time.realtimeSinceStartup + seconds;
            while (!done() && Time.realtimeSinceStartup < until) yield return null;
        }

        [UnityTest]
        public IEnumerator ASequenceTrackPlaysByTime()
        {
            LetteraWorldText text = Text("<go>wave</go>");
            yield return null;
            var sequencer = text.gameObject.AddComponent<LetteraSequencer>();
            sequencer.Cues.Add(new LetteraCue { Target = new LetteraParamTarget { Style = 0, Parameter = "Amplitude" }, To = LetteraValue.Of(1), UnitSeconds = 1 });
            TimelineAsset timeline = Own(ScriptableObject.CreateInstance<TimelineAsset>());
            var track = timeline.CreateTrack<LetteraSequenceTrack>(null, "S");
            track.CreateClip<LetteraSequenceShot>().duration = 0.6;
            PlayableDirector director = Playing(timeline, track, sequencer);
            yield return WaitFor(() => director.time > 0.2, 5);
            Assert.Greater(sequencer.Progress, 0.1f, "it follows the clock");
            Assert.AreEqual(director.time / 0.6, sequencer.Progress, 0.25, "and the track");
            Assert.IsTrue(sequencer.Driven);
            yield return WaitFor(() => director.time >= 0.59, 5);
            Assert.AreEqual(1, sequencer.Progress, 0.1f);
        }

        [UnityTest]
        public IEnumerator AParamTrackDrivesTheLinkByTime()
        {
            LetteraWorldText text = Text("<go>wave</go>");
            yield return null;
            var link = text.gameObject.AddComponent<LetteraParamLink>();
            link.Target.Style = 0;
            link.Target.Parameter = "Amplitude";
            TimelineAsset timeline = Own(ScriptableObject.CreateInstance<TimelineAsset>());
            var track = timeline.CreateTrack<LetteraParamTrack>(null, "P");
            TimelineClip clip = track.CreateClip<LetteraParamShot>();
            clip.duration = 0.5;
            ((LetteraParamShot)clip.asset).To = 2;
            Playing(timeline, track, link);
            float seen = 0;
            yield return WaitFor(() => (seen = link.Value) > 0.4f, 5);
            Assert.Greater(seen, 0.4f);
            Assert.IsNull(link.Problem);
        }

        [UnityTest]
        public IEnumerator ATextTrackSpeaksInTurnAndGivesBackAtTheEnd()
        {
            LetteraWorldText text = Text("own");
            yield return null;
            TimelineAsset timeline = Own(ScriptableObject.CreateInstance<TimelineAsset>());
            var track = timeline.CreateTrack<LetteraTextTrack>(null, "T");
            TimelineClip first = track.CreateClip<LetteraTextShot>();
            first.duration = 0.3;
            ((LetteraTextShot)first.asset).Text = "first";
            TimelineClip second = track.CreateClip<LetteraTextShot>();
            second.start = 0.3;
            second.duration = 0.3;
            ((LetteraTextShot)second.asset).Text = "second";
            ((LetteraTextShot)second.asset).Typewriter = true;
            PlayableDirector director = Playing(timeline, track, text.gameObject);
            director.extrapolationMode = DirectorWrapMode.None;
            yield return WaitFor(() => director.time > 0.05, 5);
            yield return null;
            Assert.AreEqual("first", ((ITextComponent)text).SharedEngine.RenderedSourceText);
            yield return WaitFor(() => director.time > 0.4, 5);
            yield return null;
            Assert.AreEqual("second", ((ITextComponent)text).SharedEngine.RenderedSourceText);
            Assert.AreEqual("own", text.Text);
            yield return WaitFor(() => director.state != PlayState.Playing, 5);
            yield return null;
            text.ForceRebuild();
            Assert.AreEqual("own", ((ITextComponent)text).SharedEngine.RenderedSourceText);
        }

        [UnityTest]
        public IEnumerator AnAlphaCurveRunsByTimeAsAnAlphaPass()
        {
            LetteraWorldText text = Text("abc");
            yield return null;
            text.ForceRebuild();
            int parses = text.Engine.ParseCount, shapes = text.ShapeCount;
            AnimationClip clip = Own(new AnimationClip());
            clip.SetCurve("", typeof(LetteraWorldText), "m_Color.a", AnimationCurve.Linear(0, 1, 0.5f, 0));
            var animator = text.gameObject.AddComponent<Animator>();
            AnimationPlayableUtilities.PlayClip(animator, clip, out PlayableGraph graph);
            try
            {
                float until = Time.realtimeSinceStartup + 5;
                while (text.color.a > 0.3f && Time.realtimeSinceStartup < until) yield return null;
                yield return null;
                Assert.Less(text.color.a, 0.6f);
                text.ForceRebuild();
                Assert.Less(text.Engine.Colors[0].a, (byte)200, "it reached the mesh");
                Assert.AreEqual(parses, text.Engine.ParseCount);
                Assert.AreEqual(shapes, text.ShapeCount);
            }
            finally
            {
                graph.Destroy();
            }
        }

        [UnityTest]
        public IEnumerator CurvesOnProgressAndALinkRunByTime()
        {
            LetteraWorldText text = Text("<go>wave</go>");
            yield return null;
            var sequencer = text.gameObject.AddComponent<LetteraSequencer>();
            sequencer.Cues.Add(new LetteraCue { Target = new LetteraParamTarget { Style = 0, Parameter = "Amplitude" }, To = LetteraValue.Of(1), UnitSeconds = 2 });
            var link = text.gameObject.AddComponent<LetteraParamLink>();
            link.Target.Style = 0;
            link.Target.Parameter = "Amplitude";
            link.Mix = LetteraMix.Add;
            AnimationClip clip = Own(new AnimationClip());
            clip.SetCurve("", typeof(LetteraSequencer), "m_Progress", AnimationCurve.Linear(0, 0, 0.5f, 1));
            clip.SetCurve("", typeof(LetteraParamLink), "m_Value", AnimationCurve.Linear(0, 0, 0.5f, 3));
            var animator = text.gameObject.AddComponent<Animator>();
            AnimationPlayableUtilities.PlayClip(animator, clip, out PlayableGraph graph);
            try
            {
                float until = Time.realtimeSinceStartup + 5;
                while (sequencer.Progress < 0.5f && Time.realtimeSinceStartup < until) yield return null;
                yield return null;
                Assert.Greater(sequencer.Progress, 0.5f);
                Assert.Greater(link.Value, 1.5f);
                Assert.Greater(sequencer.Playhead, 1f, "the field became a position");
            }
            finally
            {
                graph.Destroy();
            }
        }
    }
}

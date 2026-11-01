using System;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.SceneManagement;
using UnityEngine.Timeline;

namespace FirstSymbol.Lettera.Timeline.Editor.Tests
{
    /// <summary>A line from a clip: never in the field, back on leaving, and scrubbing is a pose.</summary>
    public class TextTrackTests : TimelineFixture
    {
        PlayableDirector Lines(LetteraWorldText text, out LetteraTextTrack track)
        {
            TimelineAsset timeline = Timeline();
            track = timeline.CreateTrack<LetteraTextTrack>(null, "Lines");
            PlayableDirector director = Director(timeline);
            director.SetGenericBinding(track, text.gameObject);
            return director;
        }

        static LetteraTextShot Line(LetteraTextTrack track, string words, double start, double duration, bool typewriter = false)
        {
            TimelineClip clip = track.CreateClip<LetteraTextShot>();
            var shot = (LetteraTextShot)clip.asset;
            shot.Text = words;
            shot.Typewriter = typewriter;
            clip.start = start;
            clip.duration = duration;
            return shot;
        }

        static float Alpha(LetteraWorldText text, int quad) => text.Engine.Colors[quad * 4].a / 255f;

        [Test]
        public void AClipSetsTheShownTextAndNotTheField()
        {
            LetteraWorldText text = Make("own words");
            PlayableDirector director = Lines(text, out LetteraTextTrack track);
            Line(track, "from the clip", 0, 2);
            director.RebuildGraph();
            At(director, 1);
            text.ForceRebuild();
            Assert.AreEqual("from the clip", text.Engine.RenderedSourceText);
            Assert.AreEqual("own words", text.Text);
            Assert.AreEqual("own words", new SerializedObject(text).FindProperty("m_Data.Text").stringValue);
            Assert.AreEqual(LetteraTextOverride.Resolver, text.Engine.TextOverride);
        }

        [Test]
        public void TwoClipsSpeakInTurnAndAGapGivesTheOwnTextBack()
        {
            LetteraWorldText text = Make("own");
            PlayableDirector director = Lines(text, out LetteraTextTrack track);
            Line(track, "first", 0, 1);
            Line(track, "second", 2, 1);
            director.RebuildGraph();
            At(director, 0.5);
            text.ForceRebuild();
            Assert.AreEqual("first", text.Engine.RenderedSourceText);
            At(director, 1.5);
            text.ForceRebuild();
            Assert.AreEqual("own", text.Engine.RenderedSourceText);
            At(director, 2.5);
            text.ForceRebuild();
            Assert.AreEqual("second", text.Engine.RenderedSourceText);
        }

        [Test]
        public void LeavingGivesBackAResolverItFound()
        {
            LetteraWorldText text = Make("own");
            var own = new Upper();
            text.TextResolver = own;
            PlayableDirector director = Lines(text, out LetteraTextTrack track);
            Line(track, "line", 0, 2);
            director.RebuildGraph();
            At(director, 1);
            text.ForceRebuild();
            Assert.AreEqual("line", text.Engine.RenderedSourceText);
            Leave(director);
            text.ForceRebuild();
            Assert.AreSame(own, text.TextResolver);
            Assert.AreEqual("OWN", text.Engine.RenderedSourceText);
        }

        sealed class Upper : ILetteraTextResolver
        {
            public string Resolve(Component owner, string raw) => raw.ToUpperInvariant();
        }

        [Test]
        public void ItReachesACanvasTextToo()
        {
            var root = Own(new GameObject("Canvas", typeof(Canvas)));
            var go = new GameObject("Text", typeof(RectTransform));
            go.transform.SetParent(root.transform, false);
            var text = go.AddComponent<LetteraText>();
            text.Font = Latin;
            text.Text = "own";
            TimelineAsset timeline = Timeline();
            var track = timeline.CreateTrack<LetteraTextTrack>(null, "Lines");
            Line(track, "line", 0, 2);
            PlayableDirector director = Director(timeline);
            director.SetGenericBinding(track, go);
            director.RebuildGraph();
            At(director, 1);
            text.ForceRebuild();
            Assert.AreEqual("line", ((ITextComponent)text).SharedEngine.RenderedSourceText);
            Assert.AreEqual("own", text.Text);
        }

        [Test]
        public void ATypewriterShowsTheLeadingShareOverTheClip()
        {
            LetteraWorldText text = Make("own");
            PlayableDirector director = Lines(text, out LetteraTextTrack track);
            Line(track, "abcd", 0, 4, true);
            director.RebuildGraph();
            At(director, 0);
            text.ForceRebuild();
            At(director, 2);
            text.ForceRebuild();
            Assert.AreEqual(16, text.Engine.VertexCount, "hidden glyphs keep their vertices");
            Assert.AreEqual((1f, 1f, 0f, 0f), (Alpha(text, 0), Alpha(text, 1), Alpha(text, 2), Alpha(text, 3)));
            At(director, 3.9999);
            text.ForceRebuild();
            Assert.AreEqual(1f, Alpha(text, 3));
            Leave(director);
            text.ForceRebuild();
            Assert.AreEqual("own", text.Engine.RenderedSourceText);
            Assert.IsNull(text.Engine.ClipStyle);
        }

        [Test]
        public void ScrubbingWithinAClipDoesNoRebuild()
        {
            LetteraWorldText text = Make("own");
            PlayableDirector director = Lines(text, out LetteraTextTrack track);
            Line(track, "a typewriter line of some length", 0, 10, true);
            director.RebuildGraph();
            At(director, 0.1);
            text.ForceRebuild();
            int rebuilds = text.RebuildCount, parses = text.Engine.ParseCount, shapes = text.ShapeCount;
            for (int i = 0; i < 200; i++) At(director, 0.2 + (i % 97) * 0.1);
            text.ForceRebuild();
            Assert.AreEqual(rebuilds, text.RebuildCount, "rebuilds");
            Assert.AreEqual(parses, text.Engine.ParseCount, "parses");
            Assert.AreEqual(shapes, text.ShapeCount, "shapes");
        }

        [Test]
        public void ScrubbingASequenceDoesNoRebuildAndItsCostIsShown()
        {
            LetteraSequencer sequencer = Sequenced(out LetteraWorldText text);
            TimelineAsset timeline = Timeline();
            var track = timeline.CreateTrack<LetteraSequenceTrack>(null, "Sequence");
            track.CreateClip<LetteraSequenceShot>().duration = 10;
            PlayableDirector director = Director(timeline);
            director.SetGenericBinding(track, sequencer);
            director.RebuildGraph();
            At(director, 0.1);
            text.ForceRebuild();
            int rebuilds = text.RebuildCount, parses = text.Engine.ParseCount;

            for (int i = 0; i < 20; i++) sequencer.Progress = i / 20f;
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 1000; i++)
            {
                sequencer.Progress = (i % 97) / 97f;
                LetteraMotionHub.Tick(0, 0);
            }
            long progressBytes = GC.GetAllocatedBytesForCurrentThread() - before;

            for (int i = 0; i < 20; i++) At(director, i * 0.1);
            before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 1000; i++) At(director, (i % 97) * 0.1);
            long directorBytes = GC.GetAllocatedBytesForCurrentThread() - before;
            text.ForceRebuild();

            Debug.Log($"LETTERA_NUM scrub progress_bytes_per_1000={progressBytes} director_bytes_per_1000={directorBytes}");
            TestContext.WriteLine($"LETTERA_NUM scrub progress_bytes_per_1000={progressBytes} director_bytes_per_1000={directorBytes}");
            Assert.AreEqual(rebuilds, text.RebuildCount, "rebuilds");
            Assert.AreEqual(parses, text.Engine.ParseCount, "parses");
            Assert.AreEqual(0, progressBytes, "setting progress and ticking allocates nothing");
        }

        [Test]
        public void PreviewOfALineLeavesTheSceneClean()
        {
            NewScene();
            LetteraWorldText text = Make("own");
            PlayableDirector director = Lines(text, out LetteraTextTrack track);
            Line(track, "line", 0, 4, true);
            Scene scene = CleanScene();
            director.RebuildGraph();
            for (double t = 0; t < 4; t += 0.25) At(director, t);
            text.ForceRebuild();
            Assert.IsFalse(scene.isDirty, "while it plays");
            Leave(director);
            text.ForceRebuild();
            Assert.AreEqual("own", text.Engine.RenderedSourceText);
            Assert.IsNull(text.TextResolver);
            Assert.IsFalse(scene.isDirty, "after");
        }

        [Test]
        public void ALineChangingUnderASequenceTrackIsFine()
        {
            LetteraSequencer sequencer = Sequenced(out LetteraWorldText text);
            TimelineAsset timeline = Timeline();
            var lines = timeline.CreateTrack<LetteraTextTrack>(null, "Lines");
            Line(lines, "<go>one</go>", 0, 1);
            Line(lines, "<go>two</go>", 1, 1);
            var motion = timeline.CreateTrack<LetteraSequenceTrack>(null, "Motion");
            motion.CreateClip<LetteraSequenceShot>().duration = 2;
            PlayableDirector director = Director(timeline);
            director.SetGenericBinding(lines, text.gameObject);
            director.SetGenericBinding(motion, sequencer);
            director.RebuildGraph();
            At(director, 0.5);
            At(director, 1.5);
            Assert.AreEqual(1f, Held(text), 0.26f);
            text.ForceRebuild();
            Assert.AreEqual("<go>two</go>", text.Engine.RenderedSourceText);
        }

        [Test]
        public void ATextGoneBeforeTheGraphBreaksNothing()
        {
            LetteraWorldText text = Make("own");
            PlayableDirector director = Lines(text, out LetteraTextTrack track);
            Line(track, "line", 0, 4);
            director.RebuildGraph();
            At(director, 1);
            UnityEngine.Object.DestroyImmediate(text.gameObject);
            Assert.DoesNotThrow(() => Leave(director));
        }

        [Test]
        public void ClipsDescribeThemselvesForTheirLabels()
        {
            Assert.AreEqual("Hello there", new LetteraTextShotProbe("Hello there", false).Describe());
            Assert.AreEqual("Types: Hello there", new LetteraTextShotProbe("Hello there", true).Describe());
            StringAssert.EndsWith("…", new LetteraTextShotProbe(new string('x', 60), false).Describe());
        }

        readonly struct LetteraTextShotProbe
        {
            readonly string m_Words;
            readonly bool m_Typed;

            public LetteraTextShotProbe(string words, bool typed)
            {
                m_Words = words;
                m_Typed = typed;
            }

            public string Describe()
            {
                var shot = ScriptableObject.CreateInstance<LetteraTextShot>();
                shot.Text = m_Words;
                shot.Typewriter = m_Typed;
                string line = shot.Describe();
                UnityEngine.Object.DestroyImmediate(shot);
                return line;
            }
        }
    }
}

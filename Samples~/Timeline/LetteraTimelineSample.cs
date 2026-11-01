using FirstSymbol.Lettera.Timeline;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;
using UnityEngine.UI;

namespace FirstSymbol.Lettera.Samples
{
    /// <summary>
    /// A small cut scene, built from code: a line typed by a clip, another shown whole, a sequencer and a linked parameter played by tracks.
    /// Add this to an empty object in an empty scene and press Play. In your own project you would make the same tracks in the Timeline window, under Add Track, Lettera.
    /// </summary>
    public class LetteraTimelineSample : MonoBehaviour
    {
        public LetteraText Line { get; private set; }
        public LetteraSequencer Sequencer { get; private set; }
        public LetteraParamLink Link { get; private set; }
        public PlayableDirector Director { get; private set; }

        void Start()
        {
            var canvasObject = new GameObject("Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasObject.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            var go = new GameObject("Line", typeof(RectTransform));
            go.transform.SetParent(canvasObject.transform, false);
            ((RectTransform)go.transform).sizeDelta = new Vector2(800, 200);

            // The text's own words are what it goes back to once no clip speaks.
            Line = go.AddComponent<LetteraText>();
            Line.FontSize = 40;
            Line.HorizontalAlignment = LetteraHorizontalAlignment.Center;
            Line.VerticalAlignment = LetteraVerticalAlignment.Middle;
            Line.Styles.Add(LetteraStyle.ForWholeText(new WaveMotion { Amplitude = 0 }));
            Line.Text = "The scene is over.";

            Sequencer = go.AddComponent<LetteraSequencer>();
            Sequencer.Cues.Add(new LetteraCue
            {
                Target = new LetteraParamTarget { Style = 0, Parameter = "Amplitude" },
                From = LetteraValue.Of(0),
                To = LetteraValue.Of(0.3f),
                UnitSeconds = 1,
            });

            Link = go.AddComponent<LetteraParamLink>();
            Link.Target.Style = 0;
            Link.Target.Parameter = "Rate";

            var timeline = ScriptableObject.CreateInstance<TimelineAsset>();

            var lines = timeline.CreateTrack<LetteraTextTrack>(null, "Lines");
            AddLine(lines, "They are right behind us.", 0, 1.5, true);
            AddLine(lines, "Run!", 1.5, 1, false);

            var motion = timeline.CreateTrack<LetteraSequenceTrack>(null, "Motion");
            motion.CreateClip<LetteraSequenceShot>().duration = 2.5;

            var rate = timeline.CreateTrack<LetteraParamTrack>(null, "Rate");
            TimelineClip ramp = rate.CreateClip<LetteraParamShot>();
            ramp.duration = 2.5;
            var shot = (LetteraParamShot)ramp.asset;
            shot.From = 1;
            shot.To = 4;

            Director = gameObject.AddComponent<PlayableDirector>();
            Director.playOnAwake = false;
            Director.extrapolationMode = DirectorWrapMode.None;
            Director.playableAsset = timeline;
            Director.SetGenericBinding(lines, go);
            Director.SetGenericBinding(motion, Sequencer);
            Director.SetGenericBinding(rate, Link);
            Director.Play();
        }

        static void AddLine(LetteraTextTrack track, string words, double start, double duration, bool typed)
        {
            TimelineClip clip = track.CreateClip<LetteraTextShot>();
            clip.start = start;
            clip.duration = duration;
            var shot = (LetteraTextShot)clip.asset;
            shot.Text = words;
            shot.Typewriter = typed;
        }
    }
}

using System.ComponentModel;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace FirstSymbol.Lettera.Timeline
{
    /// <summary>Plays a sequencer on a timeline.</summary>
    [DisplayName("Lettera/Sequence Track")]
    [TrackColor(0.36f, 0.58f, 0.95f)]
    [TrackClipType(typeof(LetteraSequenceShot))]
    [TrackBindingType(typeof(LetteraSequencer))]
    public class LetteraSequenceTrack : TrackAsset
    {
        public override Playable CreateTrackMixer(PlayableGraph graph, GameObject go, int inputCount) =>
            ScriptPlayable<LetteraSequenceMixer>.Create(graph, inputCount);

        public override void GatherProperties(PlayableDirector director, IPropertyCollector driver)
        {
            var sequencer = director.GetGenericBinding(this) as LetteraSequencer;
            if (sequencer != null) driver.AddFromName<LetteraSequencer>(sequencer.gameObject, "m_Progress");
            base.GatherProperties(director, driver);
        }
    }
}

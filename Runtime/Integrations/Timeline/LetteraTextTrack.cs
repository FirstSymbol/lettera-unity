using System.ComponentModel;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace FirstSymbol.Lettera.Timeline
{
    /// <summary>The lines of a cut scene: each clip says what the bound object's text shows while it plays. Bound to the object, because a Canvas text, a world text and a 3D text share no other base.</summary>
    [DisplayName("Lettera/Text Track")]
    [TrackColor(0.24f, 0.81f, 0.56f)]
    [TrackClipType(typeof(LetteraTextShot))]
    [TrackBindingType(typeof(GameObject))]
    public class LetteraTextTrack : TrackAsset
    {
        public override Playable CreateTrackMixer(PlayableGraph graph, GameObject go, int inputCount) =>
            ScriptPlayable<LetteraTextMixer>.Create(graph, inputCount);
    }
}

using System;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace FirstSymbol.Lettera.Timeline
{
    /// <summary>A span of a timeline that plays a sequencer from one progress to another.</summary>
    [Serializable]
    public class LetteraSequenceShot : PlayableAsset, ITimelineClipAsset
    {
        [Range(0, 1)] public float From;
        [Range(0, 1)] public float To = 1;

        public ClipCaps clipCaps => ClipCaps.Blending | ClipCaps.SpeedMultiplier | ClipCaps.ClipIn;

        /// <summary>What it does, in the few words a clip's label has room for.</summary>
        public string Describe() => From == 0 && To == 1 ? "Plays" : From == 1 && To == 0 ? "Plays back" : From == To ? $"Holds {From * 100:0}%" : $"{From * 100:0}% to {To * 100:0}%";

        public override Playable CreatePlayable(PlayableGraph graph, GameObject owner)
        {
            ScriptPlayable<LetteraRampBehaviour> playable = ScriptPlayable<LetteraRampBehaviour>.Create(graph);
            LetteraRampBehaviour behaviour = playable.GetBehaviour();
            behaviour.From = From;
            behaviour.To = To;
            return playable;
        }
    }
}

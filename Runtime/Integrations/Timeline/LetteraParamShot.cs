using System;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace FirstSymbol.Lettera.Timeline
{
    /// <summary>A span of a timeline that takes a linked parameter from one value to another.</summary>
    [Serializable]
    public class LetteraParamShot : PlayableAsset, ITimelineClipAsset
    {
        public float From;
        public float To = 1;
        public AnimationCurve Curve = AnimationCurve.Linear(0, 0, 1, 1);

        public ClipCaps clipCaps => ClipCaps.Blending | ClipCaps.SpeedMultiplier | ClipCaps.ClipIn;

        /// <summary>What it does, in the few words a clip's label has room for.</summary>
        public string Describe() => From == To ? $"Holds {From:0.##}" : $"{From:0.##} to {To:0.##}";

        public override Playable CreatePlayable(PlayableGraph graph, GameObject owner)
        {
            ScriptPlayable<LetteraRampBehaviour> playable = ScriptPlayable<LetteraRampBehaviour>.Create(graph);
            LetteraRampBehaviour behaviour = playable.GetBehaviour();
            behaviour.From = From;
            behaviour.To = To;
            behaviour.Curve = Curve;
            return playable;
        }
    }
}

using System;
using System.Collections.Generic;
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

        public override Playable CreatePlayable(PlayableGraph graph, GameObject owner)
        {
            ScriptPlayable<LetteraRampBehaviour> playable = ScriptPlayable<LetteraRampBehaviour>.Create(graph);
            LetteraRampBehaviour behaviour = playable.GetBehaviour();
            behaviour.From = From;
            behaviour.To = To;
            return playable;
        }
    }

    /// <summary>A span of a timeline that takes a linked parameter from one value to another.</summary>
    [Serializable]
    public class LetteraParamShot : PlayableAsset, ITimelineClipAsset
    {
        public float From;
        public float To = 1;
        public AnimationCurve Curve = AnimationCurve.Linear(0, 0, 1, 1);

        public ClipCaps clipCaps => ClipCaps.Blending | ClipCaps.SpeedMultiplier | ClipCaps.ClipIn;

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

    /// <summary>One clip's value at the playhead.</summary>
    public class LetteraRampBehaviour : PlayableBehaviour
    {
        public float From, To = 1;
        public AnimationCurve Curve;

        internal float ValueAt(Playable playable)
        {
            double duration = playable.GetDuration();
            float t = duration > 0 ? Mathf.Clamp01((float)(playable.GetTime() / duration)) : 1;
            if (Curve != null) t = Curve.Evaluate(t);
            return Mathf.LerpUnclamped(From, To, t);
        }
    }

    // What overlapping clips of a track mix to, by their weights. No clip under the playhead writes nothing.
    internal static class RampMix
    {
        internal static bool Weighted(Playable mixer, out float value)
        {
            float sum = 0, total = 0;
            int inputs = mixer.GetInputCount();
            for (int i = 0; i < inputs; i++)
            {
                float weight = mixer.GetInputWeight(i);
                if (weight <= 0) continue;
                var input = (ScriptPlayable<LetteraRampBehaviour>)mixer.GetInput(i);
                sum += input.GetBehaviour().ValueAt(input) * weight;
                total += weight;
            }
            value = total > 0 ? sum / total : 0;
            return total > 0;
        }
    }

    /// <summary>Drives a sequencer's progress. It runs on a manual clock while the graph plays, and gets its own back after.</summary>
    public class LetteraSequenceMixer : PlayableBehaviour
    {
        LetteraSequencer m_Bound;
        LetteraClock m_Clock;

        public override void ProcessFrame(Playable playable, FrameData info, object playerData)
        {
            var sequencer = playerData as LetteraSequencer;
            if (sequencer == null) return;
            if (m_Bound != sequencer)
            {
                Restore();
                m_Bound = sequencer;
                m_Clock = sequencer.Clock;
                sequencer.Clock = LetteraClock.Manual;
            }
            if (RampMix.Weighted(playable, out float progress)) sequencer.Progress = progress;
        }

        void Restore()
        {
            if (m_Bound != null) m_Bound.Clock = m_Clock;
            m_Bound = null;
        }

        public override void OnPlayableDestroy(Playable playable)
        {
            if (m_Bound != null && !Application.isPlaying) m_Bound.EndPreview();
            Restore();
        }
    }

    /// <summary>Drives a parameter link's value.</summary>
    public class LetteraParamMixer : PlayableBehaviour
    {
        public override void ProcessFrame(Playable playable, FrameData info, object playerData)
        {
            var link = playerData as LetteraParamLink;
            if (link == null) return;
            if (RampMix.Weighted(playable, out float value)) link.Value = value;
        }
    }

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

    [TrackColor(0.55f, 0.42f, 0.95f)]
    [TrackClipType(typeof(LetteraParamShot))]
    [TrackBindingType(typeof(LetteraParamLink))]
    public class LetteraParamTrack : TrackAsset
    {
        public override Playable CreateTrackMixer(PlayableGraph graph, GameObject go, int inputCount) =>
            ScriptPlayable<LetteraParamMixer>.Create(graph, inputCount);

        public override void GatherProperties(PlayableDirector director, IPropertyCollector driver)
        {
            var link = director.GetGenericBinding(this) as LetteraParamLink;
            if (link != null) driver.AddFromName<LetteraParamLink>(link.gameObject, "m_Value");
            base.GatherProperties(director, driver);
        }
    }
}

using System;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace FirstSymbol.Lettera.Timeline
{
    /// <summary>A span of a timeline in which a text says this line, with its own field left as it is.</summary>
    [Serializable]
    public class LetteraTextShot : PlayableAsset, ITimelineClipAsset
    {
        [TextArea(2, 6)] public string Text = "";
        [Tooltip("Shows the line bit by bit over the clip's length.")]
        public bool Typewriter;
        [Tooltip("What the typewriter shows at a time.")]
        public LetteraRevealUnit Unit;

        public ClipCaps clipCaps => ClipCaps.SpeedMultiplier | ClipCaps.ClipIn;

        public override double duration => 3;

        /// <summary>What it does, in the few words a clip's label has room for.</summary>
        public string Describe()
        {
            string line = (Text ?? "").Replace('\n', ' ').Trim();
            if (line.Length == 0) return Typewriter ? "Empty line, typed" : "Empty line";
            if (line.Length > 28) line = line.Substring(0, 27) + "\u2026";
            return Typewriter ? "Types: " + line : line;
        }

        public override Playable CreatePlayable(PlayableGraph graph, GameObject owner)
        {
            ScriptPlayable<LetteraTextBehaviour> playable = ScriptPlayable<LetteraTextBehaviour>.Create(graph);
            LetteraTextBehaviour behaviour = playable.GetBehaviour();
            behaviour.Text = Text ?? "";
            behaviour.Typewriter = Typewriter;
            behaviour.Unit = Unit;
            return playable;
        }
    }
}

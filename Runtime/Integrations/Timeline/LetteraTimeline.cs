using UnityEngine;
using UnityEngine.Playables;

namespace FirstSymbol.Lettera.Timeline
{
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

    // In edit mode nothing ticks the hub while a track scrubs, and scrubbing has to show.
    internal static class TrackPreview
    {
        internal static void Show()
        {
            if (Application.isPlaying) return;
            LetteraMotionHub.Tick(0, 0);
            LetteraWorldBatcher.Update();
#if UNITY_EDITOR
            UnityEditor.EditorApplication.QueuePlayerLoopUpdate();
#endif
        }
    }

    /// <summary>Drives a sequencer's progress. Ticks leave it alone while the graph plays, and in edit mode leaving gives back everything.</summary>
    public class LetteraSequenceMixer : PlayableBehaviour
    {
        LetteraSequencer m_Bound;

        public override void ProcessFrame(Playable playable, FrameData info, object playerData)
        {
            var sequencer = playerData as LetteraSequencer;
            if (sequencer == null) return;
            if (m_Bound != sequencer)
            {
                Leave();
                m_Bound = sequencer;
                sequencer.Driven = true;
            }
            if (!RampMix.Weighted(playable, out float progress)) return;
            sequencer.Progress = progress;
            TrackPreview.Show();
        }

        void Leave()
        {
            if (m_Bound != null)
            {
                m_Bound.Driven = false;
                if (!Application.isPlaying) m_Bound.EndPreview();
            }
            m_Bound = null;
        }

        public override void OnPlayableDestroy(Playable playable) => Leave();
    }

    /// <summary>Drives a parameter link's value. In edit mode leaving gives back the one it had.</summary>
    public class LetteraParamMixer : PlayableBehaviour
    {
        LetteraParamLink m_Bound;
        float m_Rest;

        public override void ProcessFrame(Playable playable, FrameData info, object playerData)
        {
            var link = playerData as LetteraParamLink;
            if (link == null) return;
            if (m_Bound != link)
            {
                Leave();
                m_Bound = link;
                m_Rest = link.Value;
            }
            if (!RampMix.Weighted(playable, out float value)) return;
            link.Value = value;
            TrackPreview.Show();
        }

        void Leave()
        {
            if (m_Bound != null && !Application.isPlaying) m_Bound.Value = m_Rest;
            m_Bound = null;
        }

        public override void OnPlayableDestroy(Playable playable) => Leave();
    }

    /// <summary>One line of a cut scene: what a text says while this clip plays. It answers as the text's resolver, so its own field is never touched.</summary>
    public class LetteraTextBehaviour : PlayableBehaviour, ILetteraTextResolver
    {
        public string Text = "";
        public bool Typewriter;
        public LetteraRevealUnit Unit;

        string ILetteraTextResolver.Resolve(Component owner, string raw) => Text;
    }

    /// <summary>Lends a text the line of the strongest clip under the playhead, and gives it back where there is none. A change of clip parses once; scrubbing within one moves only the frontier, a pose.</summary>
    public class LetteraTextMixer : PlayableBehaviour
    {
        static readonly string[] s_Reach = new string[1001];

        ITextComponent m_Text;
        ILetteraTextResolver m_OwnResolver;
        LetteraTextBehaviour m_Shown;
        LetteraStyle m_Style;
        RevealEffect m_Reveal;
        int m_Reached = -1;

        // A frontier moves a thousand steps at most, so each step's words are made once.
        static string Reach(int thousandths)
        {
            string reach = s_Reach[thousandths];
            if (reach != null) return reach;
            return s_Reach[thousandths] = (thousandths * 0.1f).ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) + "%";
        }

        static ITextComponent Find(object binding)
        {
            if (binding is GameObject go) return go != null ? go.GetComponent<ITextComponent>() : null;
            if (binding is Component component) return component != null ? component.GetComponent<ITextComponent>() : null;
            return null;
        }

        public override void ProcessFrame(Playable playable, FrameData info, object playerData)
        {
            ITextComponent text = Find(playerData);
            if (!ReferenceEquals(text, m_Text))
            {
                Leave();
                m_Text = text;
            }
            if (text == null) return;

            LetteraTextBehaviour strongest = null;
            float share = 0, best = 0;
            int inputs = playable.GetInputCount();
            for (int i = 0; i < inputs; i++)
            {
                float weight = playable.GetInputWeight(i);
                if (weight <= best) continue;
                best = weight;
                var input = (ScriptPlayable<LetteraTextBehaviour>)playable.GetInput(i);
                strongest = input.GetBehaviour();
                double duration = input.GetDuration();
                share = duration > 0 ? Mathf.Clamp01((float)(input.GetTime() / duration)) : 1;
            }
            if (strongest == null)
            {
                Leave();
                return;
            }
            if (strongest != m_Shown) Enter(strongest);
            if (strongest.Typewriter)
            {
                int thousandths = Mathf.RoundToInt(share * 1000);
                if (thousandths != m_Reached)
                {
                    m_Reached = thousandths;
                    m_Reveal.Reach = Reach(thousandths);
                }
            }
            TrackPreview.Show();
        }

        void Enter(LetteraTextBehaviour clip)
        {
            TextEngine engine = m_Text.SharedEngine;
            if (m_Shown == null) m_OwnResolver = engine.Resolver;
            m_Shown = clip;
            engine.Resolver = clip;
            if (clip.Typewriter)
            {
                if (m_Reveal == null)
                {
                    m_Reveal = new RevealEffect();
                    m_Style = LetteraStyle.ForWholeText(m_Reveal);
                }
                m_Reveal.Unit = clip.Unit;
                m_Reached = -1;
                engine.ClipStyle = m_Style;
            }
            else engine.ClipStyle = null;
            m_Text.Dirty(DirtyLevel.Parse);
        }

        void Leave()
        {
            if (m_Shown == null) return;
            m_Shown = null;
            // The text may have gone first, with the graph coming down after it.
            if (m_Text is Object alive && alive == null) return;
            TextEngine engine = m_Text.SharedEngine;
            engine.Resolver = m_OwnResolver;
            engine.ClipStyle = null;
            m_OwnResolver = null;
            m_Text.Dirty(DirtyLevel.Parse);
            TrackPreview.Show();
        }

        public override void OnPlayableDestroy(Playable playable) => Leave();
    }
}

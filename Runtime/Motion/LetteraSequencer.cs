using System;
using System.Collections.Generic;
using UnityEngine;

namespace FirstSymbol.Lettera
{
    public enum LetteraRepeat { Restart, Retrace, Return, Accumulate }

    /// <summary>A value of a parameter's own kind.</summary>
    [Serializable]
    public struct LetteraValue
    {
        public float Number;
        public Color32 Color;
        public Vector2 Vector;
        public string Text;

        public static LetteraValue Of(float number) => new LetteraValue { Number = number };
        public static LetteraValue Of(Color32 color) => new LetteraValue { Color = color };
        public static LetteraValue Of(Vector2 vector) => new LetteraValue { Vector = vector };
        public static LetteraValue Of(string text) => new LetteraValue { Text = text };
    }

    /// <summary>Where a parameter lives: a style of the text, a path down its composites, and the parameter's id.</summary>
    [Serializable]
    public class LetteraParamTarget
    {
        [Tooltip("The style's position among the text's own styles and presets.")]
        public int Style;
        [Tooltip("Which child to take at each composite on the way down. Empty is the style's own effect.")]
        public int[] Node = new int[0];
        public string Parameter = "";

        /// <summary>Finds the effect now. Null when it is gone.</summary>
        internal LetteraEffect Resolve(TextEngine engine)
        {
            LetteraEffect effect = engine.EffectAt(Style);
            if (Node == null) return effect;
            for (int i = 0; i < Node.Length && effect != null; i++)
            {
                if (!(effect is CompositeEffect composite) || Node[i] < 0 || Node[i] >= composite.Children.Count) return null;
                effect = composite.Children[Node[i]];
            }
            return effect;
        }

        internal static LetteraParamInfo Find(LetteraEffect effect, string id)
        {
            if (effect == null) return null;
            IReadOnlyList<LetteraParamInfo> table = effect.Parameters;
            for (int i = 0; i < table.Count; i++)
                if (table[i].Id == id)
                    return table[i];
            return null;
        }
    }

    /// <summary>Which ranges of an effect a cue or a link takes: the stored form of a query, built again on each bind so it follows edits.</summary>
    [Serializable]
    public class LetteraRangeSelect
    {
        public string Label = "", Primary = "", Token = "";
        [Min(0)] public int TokenSlot;
        public bool BareOnly;
        [Tooltip("Keep ranges that overlap this span of rendered text. An end of 0 or less takes no notice of it.")]
        public int OverlapStart, OverlapEnd;
        [Min(0)] public int Skip;
        [Tooltip("At most this many; 0 takes them all.")]
        [Min(0)] public int Take;

        internal LetteraRangeQuery Build(TextEngine engine, LetteraEffect effect)
        {
            var query = new LetteraRangeQuery(engine, effect);
            if (!string.IsNullOrEmpty(Label)) query = query.WithLabel(Label);
            if (!string.IsNullOrEmpty(Primary)) query = query.WithPrimary(Primary);
            if (!string.IsNullOrEmpty(Token)) query = query.WithToken(TokenSlot, Token);
            if (BareOnly) query = query.Bare();
            if (OverlapEnd > 0) query = query.Overlapping(OverlapStart, OverlapEnd);
            if (Skip > 0) query = query.Skip(Skip);
            if (Take > 0) query = query.Take(Take);
            return query;
        }
    }

    /// <summary>One window on a sequencer's timeline: a parameter going from one value to another across the chosen ranges.</summary>
    [Serializable]
    public class LetteraCue
    {
        [Min(0)] public float Start;
        [Tooltip("The row it shows on. It means nothing at run time.")]
        [Min(0)] public int Lane;
        public LetteraParamTarget Target = new LetteraParamTarget();
        public LetteraRangeSelect Select = new LetteraRangeSelect();
        public LetteraValue From, To;
        public LetteraMix Mix;
        public int Priority;
        [Tooltip("How long one range's ramp takes.")]
        [Min(0)] public float UnitSeconds = 1;
        [Tooltip("The delay from one range's start to the next's, in text order.")]
        [Min(0)] public float StepSeconds;
        public LetteraEase Curve = LetteraEase.Linear;
        public bool Muted;

        [NonSerialized] internal LetteraEffect BoundEffect;
        [NonSerialized] internal LetteraParamInfo BoundInfo;
        [NonSerialized] internal object Hold;
        [NonSerialized] internal int Later = -1;
        [NonSerialized] internal bool HasEarlier;

        /// <summary>Why it cannot run, or null. For the inspector.</summary>
        public string Problem { get; internal set; }

        internal IDisposable HoldHandle => Hold as IDisposable;
    }

    /// <summary>Ramps any published parameter of any effect of a text over one shared timeline. All of it goes through holds, so no field is ever written.</summary>
    [AddComponentMenu("Lettera/Sequencer")]
    [DisallowMultipleComponent]
    [ExecuteAlways]
    public class LetteraSequencer : MonoBehaviour, IMotionClient
    {
        [SerializeField] List<LetteraCue> m_Cues = new List<LetteraCue>();
        [SerializeField] LetteraClock m_Clock = LetteraClock.Scaled;
        [Tooltip("How many passes. -1 repeats until stopped.")]
        [SerializeField] int m_Cycles = 1;
        [SerializeField] LetteraRepeat m_Repeat;
        [Tooltip("A fixed length; cues past it still extend it. 0 takes it from the cues alone.")]
        [SerializeField, Min(0)] float m_Length;
        [SerializeField] bool m_PlayOnEnable;
        [SerializeField] float m_Speed = 1;
        [SerializeField] bool m_HonorReducedMotion = true;
        [Tooltip("Where the first pass stands, from 0 to 1. Curves and tracks can drive this.")]
        [SerializeField, Range(0, 1)] float m_Progress;

        TextEngine m_Engine;
        ITextComponent m_Text;
        float m_Time;
        int m_Pass;
        float m_AppliedProgress = -1;
        // Where it stands. In edit mode only this moves, so that a preview leaves the serialized field alone.
        float m_Shown;
        bool m_Playing, m_InHub, m_Laid, m_Stale;
        // Who holds what, apart from the list itself: an undo or an edit can replace the list's cues under a live hold.
        readonly List<LetteraCue> m_Holding = new List<LetteraCue>();

        public List<LetteraCue> Cues => m_Cues;
        public LetteraClock Clock { get => m_Clock; set => m_Clock = value; }
        public int Cycles { get => m_Cycles; set => m_Cycles = value; }
        public LetteraRepeat Repeat { get => m_Repeat; set => m_Repeat = value; }
        public float Length { get => m_Length; set => m_Length = Mathf.Max(0, value); }
        public bool PlayOnEnable { get => m_PlayOnEnable; set => m_PlayOnEnable = value; }
        public float Speed { get => m_Speed; set => m_Speed = value; }
        public bool HonorReducedMotion { get => m_HonorReducedMotion; set => m_HonorReducedMotion = value; }
        public bool IsPlaying => m_Playing;

        public event Action Played, Paused, Stopped, CycleEnded, Finished;

        internal TextEngine Engine
        {
            get
            {
                if (m_Engine != null) return m_Engine;
                m_Text = GetComponent<LetteraText>() as ITextComponent ?? GetComponent<LetteraWorldText>();
                m_Engine = m_Text?.SharedEngine;
                return m_Engine;
            }
        }

        /// <summary>The length now, which follows how many ranges each cue matches.</summary>
        public float ResolvedLength
        {
            get
            {
                BindAll();
                float length = m_Length;
                foreach (LetteraCue cue in m_Cues)
                    if (!cue.Muted)
                        length = Mathf.Max(length, EndOf(cue));
                return length;
            }
        }

        /// <summary>Normalized position. Setting it shows exactly that moment of the first pass.</summary>
        public float Progress
        {
            get => m_Progress != m_AppliedProgress ? m_Progress : m_Shown;
            set
            {
                float length = ResolvedLength;
                Show(Mathf.Clamp01(value));
                m_Pass = 0;
                m_Time = m_Shown * length;
                Evaluate();
            }
        }

        /// <summary>The position in seconds. A pass that runs back reads as its mirrored position, never past the end.</summary>
        public float Playhead => Mirrored(m_Pass) ? ResolvedLength - m_Time : m_Time;

        /// <summary>A track or a tool has the clock: no tick moves it, and nothing is written to say so.</summary>
        internal bool Driven { get; set; }

        /// <summary>Where this cue ends now, with its staggered members.</summary>
        internal float EndOfCue(LetteraCue cue) => EndOf(cue);

        void Show(float progress)
        {
            m_Shown = progress;
            if (Application.isPlaying) m_Progress = progress;
            m_AppliedProgress = m_Progress;
        }

        bool Mirrored(int pass) => (m_Repeat == LetteraRepeat.Retrace || m_Repeat == LetteraRepeat.Return) && (pass & 1) == 1;

        void OnEnable()
        {
            m_AppliedProgress = m_Progress;
            m_Shown = m_Progress;
            if (m_PlayOnEnable && Application.isPlaying) Play();
        }

        void OnDisable()
        {
            m_Playing = false;
            LeaveHub();
            ReleaseAll();
        }

        void OnDestroy() => ReleaseAll();

        void EnterHub()
        {
            if (m_InHub) return;
            m_InHub = true;
            LetteraMotionHub.Add(this);
        }

        void LeaveHub()
        {
            if (!m_InHub) return;
            m_InHub = false;
            LetteraMotionHub.Remove(this);
        }

        public void Play()
        {
            if (Engine == null) return;
            if (m_HonorReducedMotion && LetteraAccessibility.ReducedMotion)
            {
                JumpToEnd();
                return;
            }
            float length = ResolvedLength;
            // Played again after an end, it starts over from the side it runs from.
            if (m_Speed >= 0 && m_Time >= length && (m_Cycles >= 0 && m_Pass >= m_Cycles - 1)) { m_Time = 0; m_Pass = 0; }
            else if (m_Speed < 0 && m_Time <= 0 && m_Pass == 0) m_Time = length;
            m_Playing = true;
            EnterHub();
            Evaluate();
            Played?.Invoke();
        }

        public void Pause()
        {
            if (!m_Playing) return;
            m_Playing = false;
            LeaveHub();
            Paused?.Invoke();
        }

        /// <summary>Back to the start. The cues stay in charge, showing their first values.</summary>
        public void Stop()
        {
            m_Playing = false;
            LeaveHub();
            m_Time = 0;
            m_Pass = 0;
            Show(0);
            Evaluate();
            Stopped?.Invoke();
        }

        /// <summary>Moves to a time of the first pass.</summary>
        public void Seek(float seconds)
        {
            m_Pass = 0;
            m_Time = Mathf.Clamp(seconds, 0, ResolvedLength);
            SyncProgress();
            Evaluate();
        }

        /// <summary>Moves on by this much, in any clock. This is the only thing that moves a manual one.</summary>
        public void Advance(float seconds) => Step(seconds * m_Speed);

        /// <summary>Builds every hold again, against the styles as they are.</summary>
        public void Rebind()
        {
            ReleaseAll();
            BindAll();
            Evaluate();
        }

        bool IMotionClient.TickMotion(float scaled, float unscaled)
        {
            if (!m_Playing) return (m_InHub = false);
            if (m_HonorReducedMotion && LetteraAccessibility.ReducedMotion)
            {
                JumpToEnd();
                return (m_InHub = false);
            }
            if (m_Clock != LetteraClock.Manual && !Driven) Step((m_Clock == LetteraClock.Scaled ? scaled : unscaled) * m_Speed);
            if (!m_Playing) return (m_InHub = false);
            return true;
        }

        void JumpToEnd()
        {
            m_Playing = false;
            LeaveHub();
            int passes = m_Cycles < 0 ? 1 : Mathf.Max(m_Cycles, 1);
            m_Pass = passes - 1;
            m_Time = m_Speed < 0 ? 0 : ResolvedLength;
            SyncProgress();
            Evaluate();
            Finished?.Invoke();
        }

        void Step(float delta)
        {
            float length = ResolvedLength;
            int passes = m_Cycles < 0 ? int.MaxValue : Mathf.Max(m_Cycles, 1);
            m_Time += delta;
            bool finished = false;
            if (length <= 0)
            {
                m_Time = 0;
                finished = m_Cycles >= 0;
            }
            else
            {
                while (m_Time >= length)
                {
                    if (m_Pass + 1 >= passes)
                    {
                        m_Time = length;
                        finished = true;
                        break;
                    }
                    m_Time -= length;
                    m_Pass++;
                    CycleEnded?.Invoke();
                }
                while (m_Time < 0)
                {
                    if (m_Pass <= 0)
                    {
                        m_Time = 0;
                        finished = true;
                        break;
                    }
                    m_Time += length;
                    m_Pass--;
                    CycleEnded?.Invoke();
                }
            }
            SyncProgress();
            Evaluate();
            if (!finished) return;
            bool was = m_Playing;
            m_Playing = false;
            LeaveHub();
            if (was) Finished?.Invoke();
        }

        void SyncProgress()
        {
            float length = ResolvedLength;
            Show(length > 0 ? Mathf.Clamp01(Playhead / length) : 0);
        }

        // A curve wrote the field: this is how it gets here.
        void OnDidApplyAnimationProperties() => ApplyProgressIfMoved();

        void LateUpdate()
        {
            // The message does not reach every path that writes the field. A compare costs nothing when it has.
            if (m_Playing) return;
            ApplyProgressIfMoved();
            if (m_Stale && m_Holding.Count > 0) Rebind();
        }

        void OnValidate()
        {
            // An edit or an undo: it may have replaced the cues. What moved the field is seen by the compare.
            if (m_Holding.Count > 0) m_Stale = true;
        }

        void ApplyProgressIfMoved()
        {
            if (m_Progress == m_AppliedProgress) return;
            m_AppliedProgress = m_Progress;
            Progress = m_Progress;
        }

        static float EndOf(LetteraCue cue)
        {
            int members = Math.Max(MembersOf(cue), 1);
            return cue.Start + cue.UnitSeconds + cue.StepSeconds * (members - 1);
        }

        static int MembersOf(LetteraCue cue)
        {
            switch (cue.Hold)
            {
                case LetteraHold<float> hold: return hold.Count;
                case LetteraHold<Color32> hold: return hold.Count;
                case LetteraHold<string> hold: return hold.Count;
                default: return 0;
            }
        }

        void Release(LetteraCue cue)
        {
            cue.HoldHandle?.Dispose();
            m_Holding.Remove(cue);
            cue.Hold = null;
            cue.BoundEffect = null;
            cue.BoundInfo = null;
        }

        void ReleaseAll()
        {
            for (int i = m_Holding.Count - 1; i >= 0; i--) Release(m_Holding[i]);
            m_Stale = false;
        }

        // A cue that left the list, by an edit or an undo, must not keep its hold.
        void ReleaseOrphans()
        {
            int live = 0;
            foreach (LetteraCue cue in m_Cues)
                if (cue.Hold != null)
                    live++;
            if (live == m_Holding.Count) return;
            for (int i = m_Holding.Count - 1; i >= 0; i--)
                if (!m_Cues.Contains(m_Holding[i]))
                    Release(m_Holding[i]);
        }

        // Runs every tick, so a style that came back or went away is noticed with no message; the walk allocates nothing.
        void BindAll()
        {
            if (Engine == null) return;
            if (m_Stale) ReleaseAll();
            ReleaseOrphans();
            foreach (LetteraCue cue in m_Cues) Bind(cue);
            if (m_Laid && m_Cues.Count == m_LaidCount) return;
            m_Laid = true;
            m_LaidCount = m_Cues.Count;
            LayOut();
        }

        int m_LaidCount;

        void Bind(LetteraCue cue)
        {
            if (cue.Muted)
            {
                if (cue.Hold != null) Release(cue);
                cue.Problem = null;
                return;
            }
            LetteraEffect effect = cue.Target.Resolve(m_Engine);
            if (effect == cue.BoundEffect && cue.Hold != null) return;
            if (cue.Hold != null) Release(cue);
            LetteraParamInfo info = LetteraParamTarget.Find(effect, cue.Target.Parameter);
            if (effect == null || info == null)
            {
                cue.Problem = effect == null ? "The effect it points at is gone." : $"The effect has no parameter '{cue.Target.Parameter}'.";
                return;
            }
            if (info.Slot < 0)
            {
                cue.Problem = $"'{cue.Target.Parameter}' has no slot for a hold to reach.";
                return;
            }
            cue.Problem = null;
            cue.BoundEffect = effect;
            cue.BoundInfo = info;
            LetteraRangeQuery query = cue.Select.Build(m_Engine, effect);
            LetteraHoldComposition composition = cue.Mix == LetteraMix.Add ? LetteraHoldComposition.Add : cue.Mix == LetteraMix.Multiply ? LetteraHoldComposition.Multiply : LetteraHoldComposition.Replace;
            switch (info.Kind)
            {
                case LetteraParamKind.Float:
                case LetteraParamKind.Int:
                    cue.Hold = query.Hold<float>(info.Slot, composition, info.Level, cue.Priority);
                    break;
                case LetteraParamKind.Color:
                    cue.Hold = query.Hold<Color32>(info.Slot, composition, info.Level, cue.Priority);
                    break;
                default:
                    cue.Hold = query.Hold<string>(info.Slot, LetteraHoldComposition.Replace, info.Level, cue.Priority);
                    break;
            }
            m_Holding.Add(cue);
            m_Laid = false;
        }

        // Which later cue takes each one over.
        void LayOut()
        {
            for (int a = 0; a < m_Cues.Count; a++)
            {
                LetteraCue cue = m_Cues[a];
                cue.Later = -1;
                cue.HasEarlier = false;
                if (cue.Mix != LetteraMix.Replace) continue;
                float best = float.MaxValue;
                for (int b = 0; b < m_Cues.Count; b++)
                {
                    LetteraCue other = m_Cues[b];
                    if (other == cue || other.Muted || other.Mix != LetteraMix.Replace || other.Priority != cue.Priority) continue;
                    if (other.Target.Parameter != cue.Target.Parameter || other.Target.Style != cue.Target.Style || !SameNode(other.Target.Node, cue.Target.Node)) continue;
                    if (other.Start > cue.Start && other.Start < best)
                    {
                        best = other.Start;
                        cue.Later = b;
                    }
                    if (other.Start < cue.Start) cue.HasEarlier = true;
                }
            }
        }

        static bool SameNode(int[] a, int[] b)
        {
            int la = a?.Length ?? 0, lb = b?.Length ?? 0;
            if (la != lb) return false;
            for (int i = 0; i < la; i++)
                if (a[i] != b[i])
                    return false;
            return true;
        }

        void Evaluate()
        {
            if (Engine == null) return;
            BindAll();
            bool mirrored = Mirrored(m_Pass);
            float length = ResolvedLength;
            float time = mirrored ? length - m_Time : m_Time;
            foreach (LetteraCue cue in m_Cues)
            {
                if (cue.Hold == null) continue;
                // The latest started drives: one that has not begun waits while an earlier one holds the parameter.
                bool yielded = (cue.Later >= 0 && time >= m_Cues[cue.Later].Start) || (cue.HasEarlier && time < cue.Start);
                switch (cue.Hold)
                {
                    case LetteraHold<float> hold:
                        hold.Suspended = yielded;
                        if (!yielded) WriteNumbers(cue, hold, time, mirrored);
                        break;
                    case LetteraHold<Color32> hold:
                        hold.Suspended = yielded;
                        if (!yielded) WriteColors(cue, hold, time, mirrored);
                        break;
                    case LetteraHold<string> hold:
                        hold.Suspended = yielded;
                        if (!yielded) WriteDiscrete(cue, hold, time, mirrored);
                        break;
                }
            }
        }

        // Where a member's ramp stands. On a Return pass each curve reads from its own beginning.
        float EasedAt(LetteraCue cue, int member, float time, bool mirrored)
        {
            float start = cue.Start + cue.StepSeconds * member;
            float unit = cue.UnitSeconds > 0 ? Mathf.Clamp01((time - start) / cue.UnitSeconds) : time >= start ? 1 : 0;
            if (mirrored && m_Repeat == LetteraRepeat.Return) return 1 - LetteraEntrance.Eased(cue.Curve, 1 - unit);
            return LetteraEntrance.Eased(cue.Curve, unit);
        }

        int Passes => m_Repeat == LetteraRepeat.Accumulate ? m_Pass : 0;

        void WriteNumbers(LetteraCue cue, LetteraHold<float> hold, float time, bool mirrored)
        {
            float from = cue.From.Number, to = cue.To.Number, travel = (to - from) * Passes;
            bool whole = cue.BoundInfo.Kind == LetteraParamKind.Int;
            int count = hold.Count;
            for (int m = 0; m < count; m++)
            {
                float value = Mathf.LerpUnclamped(from, to, EasedAt(cue, m, time, mirrored)) + travel;
                hold.SetValue(m, whole ? Mathf.Round(value) : value);
            }
            hold.PrimeValue(from + travel);
        }

        void WriteColors(LetteraCue cue, LetteraHold<Color32> hold, float time, bool mirrored)
        {
            int count = hold.Count;
            for (int m = 0; m < count; m++)
            {
                float e = EasedAt(cue, m, time, mirrored);
                Color32 value = Color32.LerpUnclamped(cue.From.Color, cue.To.Color, e);
                if (Passes > 0) value = cue.To.Color;
                hold.SetValue(m, value);
            }
            hold.PrimeValue(cue.From.Color);
        }

        void WriteDiscrete(LetteraCue cue, LetteraHold<string> hold, float time, bool mirrored)
        {
            int count = hold.Count;
            for (int m = 0; m < count; m++)
                hold.SetValue(m, DiscreteText(cue.BoundInfo, EasedAt(cue, m, time, mirrored) < 0.5f && Passes == 0 ? cue.From : cue.To));
            hold.PrimeValue(DiscreteText(cue.BoundInfo, cue.From));
        }

        internal static string DiscreteText(LetteraParamInfo info, in LetteraValue value)
        {
            switch (info.Kind)
            {
                case LetteraParamKind.Bool: return value.Number >= 0.5f ? "true" : "false";
                case LetteraParamKind.Vector2: return value.Vector.x.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + " " + value.Vector.y.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
                case LetteraParamKind.Enum: return Mathf.RoundToInt(value.Number).ToString(System.Globalization.CultureInfo.InvariantCulture);
                default: return value.Text ?? "";
            }
        }

        /// <summary>Ends an edit-mode preview: every value goes back to the cascade, and nothing was ever written.</summary>
        public void EndPreview()
        {
            m_Playing = false;
            LeaveHub();
            float length = ResolvedLength;
            ReleaseAll();
            m_Pass = 0;
            m_AppliedProgress = m_Progress;
            m_Shown = m_Progress;
            m_Time = m_Shown * length;
        }
    }

    /// <summary>Gives one effect parameter a field that curves, tracks and tweens can reach. An Animator cannot bind into an effect's list directly.</summary>
    [AddComponentMenu("Lettera/Parameter Link")]
    [ExecuteAlways]
    public class LetteraParamLink : MonoBehaviour, IMotionClient
    {
        [Tooltip("The text. Empty looks on this object, then up.")]
        [SerializeField] Component m_Text;
        [SerializeField] LetteraParamTarget m_Target = new LetteraParamTarget();
        [SerializeField] LetteraRangeSelect m_Select = new LetteraRangeSelect();
        [SerializeField] LetteraMix m_Mix;
        [SerializeField] int m_Priority;
        [SerializeField] float m_Value;
        [SerializeField] Color m_Color = Color.white;
        [SerializeField] Vector2 m_Vector;

        TextEngine m_Engine;
        object m_Hold;
        LetteraEffect m_Bound;
        LetteraParamInfo m_Info;
        float m_LastValue = float.NaN;
        Color m_LastColor;
        Vector2 m_LastVector;
        bool m_InHub;

        public LetteraParamTarget Target => m_Target;
        public LetteraRangeSelect Select => m_Select;
        public LetteraMix Mix { get => m_Mix; set { m_Mix = value; Unbind(); } }
        public int Priority { get => m_Priority; set { m_Priority = value; Unbind(); } }
        public float Value { get => m_Value; set { m_Value = value; Apply(); } }
        public Color ColorValue { get => m_Color; set { m_Color = value; Apply(); } }
        public Vector2 VectorValue { get => m_Vector; set { m_Vector = value; Apply(); } }

        /// <summary>Why it cannot run, or null.</summary>
        public string Problem { get; private set; }

        public Component Text { get => m_Text; set { m_Text = value; m_Engine = null; Unbind(); } }

        TextEngine Engine
        {
            get
            {
                if (m_Engine != null) return m_Engine;
                if (m_Text is ITextComponent assigned) return m_Engine = assigned.SharedEngine;
                var canvas = GetComponentInParent<LetteraText>();
                if (canvas != null) return m_Engine = ((ITextComponent)canvas).SharedEngine;
                var world = GetComponentInParent<LetteraWorldText>();
                return m_Engine = world != null ? ((ITextComponent)world).SharedEngine : null;
            }
        }

        void OnEnable()
        {
            m_LastValue = float.NaN;
            m_InHub = true;
            LetteraMotionHub.Add(this);
        }

        void OnDisable()
        {
            m_InHub = false;
            LetteraMotionHub.Remove(this);
            Unbind();
        }

        void OnDidApplyAnimationProperties() => Apply();

        bool IMotionClient.TickMotion(float scaled, float unscaled)
        {
            if (!m_InHub) return false;
            Apply();
            return true;
        }

        void Unbind()
        {
            (m_Hold as IDisposable)?.Dispose();
            m_Hold = null;
            m_Bound = null;
            m_Info = null;
            m_LastValue = float.NaN;
        }

        void Apply()
        {
            TextEngine engine = Engine;
            if (engine == null)
            {
                Problem = "No text to drive.";
                return;
            }
            LetteraEffect effect = m_Target.Resolve(engine);
            if (effect != m_Bound || m_Hold == null) Bind(engine, effect);
            if (m_Hold == null) return;
            switch (m_Hold)
            {
                case LetteraHold<float> hold:
                    if (m_Value == m_LastValue) return;
                    m_LastValue = m_Value;
                    hold.Value = m_Info.Kind == LetteraParamKind.Int ? Mathf.Round(m_Value) : m_Value;
                    break;
                case LetteraHold<Color32> hold:
                    if (m_Color == m_LastColor && !float.IsNaN(m_LastValue)) return;
                    m_LastColor = m_Color;
                    m_LastValue = 0;
                    hold.Value = m_Color;
                    break;
                case LetteraHold<string> hold:
                    if (m_Value == m_LastValue && m_Vector == m_LastVector) return;
                    m_LastValue = m_Value;
                    m_LastVector = m_Vector;
                    hold.Value = LetteraSequencer.DiscreteText(m_Info, new LetteraValue { Number = m_Value, Vector = m_Vector });
                    break;
            }
        }

        void Bind(TextEngine engine, LetteraEffect effect)
        {
            Unbind();
            LetteraParamInfo info = LetteraParamTarget.Find(effect, m_Target.Parameter);
            if (effect == null || info == null || info.Slot < 0)
            {
                Problem = effect == null ? "The effect it points at is gone." : $"The effect has no parameter '{m_Target.Parameter}' a hold can reach.";
                return;
            }
            Problem = null;
            m_Bound = effect;
            m_Info = info;
            LetteraRangeQuery query = m_Select.Build(engine, effect);
            LetteraHoldComposition composition = m_Mix == LetteraMix.Add ? LetteraHoldComposition.Add : m_Mix == LetteraMix.Multiply ? LetteraHoldComposition.Multiply : LetteraHoldComposition.Replace;
            switch (info.Kind)
            {
                case LetteraParamKind.Float:
                case LetteraParamKind.Int:
                    m_Hold = query.Hold<float>(info.Slot, composition, info.Level, m_Priority);
                    break;
                case LetteraParamKind.Color:
                    m_Hold = query.Hold<Color32>(info.Slot, composition, info.Level, m_Priority);
                    break;
                default:
                    m_Hold = query.Hold<string>(info.Slot, LetteraHoldComposition.Replace, info.Level, m_Priority);
                    break;
            }
        }
    }
}

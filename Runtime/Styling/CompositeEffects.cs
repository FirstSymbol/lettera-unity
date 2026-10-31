using System;
using System.Collections.Generic;
using UnityEngine;

namespace FirstSymbol.Lettera
{
    /// <summary>
    /// Several effects in one style slot, sharing its ranges. A tag's value is split by semicolons, one segment per child, and each child
    /// splits its own by commas: &lt;badge=(mailto:a@b.io);teal;wavy&gt;. A child that splits again takes its list in parentheses, and one level
    /// of them comes off on the way. A value with no semicolon goes to the first child.
    /// </summary>
    [Serializable]
    public class CompositeEffect : LetteraEffect
    {
        [SerializeReference] List<LetteraEffect> m_Children = new List<LetteraEffect>();

        public CompositeEffect() { }

        public CompositeEffect(params LetteraEffect[] children) => m_Children.AddRange(children);

        public List<LetteraEffect> Children => m_Children;

        public override bool ExtendsOnType
        {
            get
            {
                foreach (LetteraEffect child in m_Children)
                    if (child != null && !child.ExtendsOnType)
                        return false;
                return true;
            }
        }

        public override LetteraEffect Clone()
        {
            var copy = (CompositeEffect)base.Clone();
            copy.m_Children = new List<LetteraEffect>(m_Children.Count);
            foreach (LetteraEffect child in m_Children) copy.m_Children.Add(child?.Clone());
            return copy;
        }

        internal override void CollectChildren(List<LetteraEffect> results)
        {
            foreach (LetteraEffect child in m_Children)
                if (child != null)
                    results.Add(child);
        }

        public override void Apply(LetteraAttributes attributes, int start, int end, in LetteraSlots slots)
        {
            List<string> tag = SplitSegments(slots.Raw);
            List<string> defaults = SplitSegments(slots.DefaultRaw);
            for (int i = 0; i < m_Children.Count; i++)
            {
                LetteraEffect child = m_Children[i];
                if (child == null) continue;
                string segment = tag != null && i < tag.Count ? tag[i] : null;
                string fallback = defaults != null && i < defaults.Count ? defaults[i] : null;
                attributes.BeginChildEffect(child, i);
                try
                {
                    child.Apply(attributes, start, end, slots.ForChild(child, segment, fallback));
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                }
            }
        }

        /// <summary>Splits on semicolons outside parentheses and quotes. An empty segment is null; one level of parentheses around a segment comes off.</summary>
        internal static List<string> SplitSegments(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return null;
            var segments = new List<string>(4);
            int depth = 0, start = 0;
            char quote = '\0';
            for (int i = 0; i <= raw.Length; i++)
            {
                char c = i < raw.Length ? raw[i] : ';';
                if (quote != '\0')
                {
                    if (i == raw.Length || c == quote) quote = '\0';
                    if (i < raw.Length) continue;
                }
                else if (i < raw.Length && (c == '"' || c == '\''))
                {
                    quote = c;
                    continue;
                }
                else if (c == '(' && HasCloser(raw, i)) depth++;
                else if (c == ')' && depth > 0) depth--;
                if ((c == ';' && depth == 0) || i == raw.Length)
                {
                    string segment = raw.Substring(start, i - start).Trim();
                    segments.Add(segment.Length == 0 ? null : Unwrap(segment));
                    start = i + 1;
                }
            }
            return segments;
        }

        static bool HasCloser(string text, int open)
        {
            int depth = 0;
            for (int i = open; i < text.Length; i++)
            {
                if (text[i] == '(') depth++;
                else if (text[i] == ')' && --depth == 0) return true;
            }
            return false;
        }

        static string Unwrap(string segment)
        {
            if (segment.Length < 2 || segment[0] != '(' || segment[segment.Length - 1] != ')') return segment;
            int depth = 0;
            for (int i = 0; i < segment.Length - 1; i++)
            {
                if (segment[i] == '(') depth++;
                else if (segment[i] == ')' && --depth == 0) return segment;
            }
            return segment.Substring(1, segment.Length - 2);
        }
    }

    /// <summary>A reusable effect, usually a composite, as an asset. Each user runs its own copy; editing the asset reaches all of them.</summary>
    [CreateAssetMenu(menuName = "Lettera/Effect Preset", fileName = "Lettera Effect Preset")]
    public class LetteraEffectPreset : ScriptableObject
    {
        /// <summary>Raised when a preset changes, so texts using it rebuild.</summary>
        public static event Action<LetteraEffectPreset> PresetChanged;

        [SerializeReference] LetteraEffect m_Root;

        internal int Version { get; private set; }

        public LetteraEffect Root { get => m_Root; set { m_Root = value; NotifyChanged(); } }

        /// <summary>Call after editing the root from a script.</summary>
        public void NotifyChanged()
        {
            Version++;
            PresetChanged?.Invoke(this);
        }

        void OnValidate() => NotifyChanged();
    }

    /// <summary>Applies a preset asset. With no preset it does nothing and warns once. Its parameters go to the preset's root.</summary>
    [Serializable]
    public class PresetEffect : LetteraEffect
    {
        [SerializeField] LetteraEffectPreset m_Preset;

        [NonSerialized] LetteraEffect m_Instance;
        [NonSerialized] int m_InstanceVersion = -1;
        [NonSerialized] bool m_Warned;

        [ThreadStatic] static List<LetteraEffectPreset> s_Applying;
        static readonly HashSet<LetteraEffectPreset> s_WarnedCycles = new HashSet<LetteraEffectPreset>();

        public PresetEffect() { }

        public PresetEffect(LetteraEffectPreset preset) => m_Preset = preset;

        public LetteraEffectPreset Preset
        {
            get => m_Preset;
            set
            {
                m_Preset = value;
                m_Instance = null;
            }
        }

        /// <summary>This user's own copy of the preset's root. Changing it leaves other users alone.</summary>
        public LetteraEffect Instance
        {
            get
            {
                if (m_Preset == null || m_Preset.Root == null) return null;
                if (m_Instance == null || m_InstanceVersion != m_Preset.Version)
                {
                    m_Instance = m_Preset.Root.Clone();
                    m_InstanceVersion = m_Preset.Version;
                }
                return m_Instance;
            }
        }

        public override LetteraEffect Clone()
        {
            var copy = (PresetEffect)base.Clone();
            copy.m_Instance = null;
            copy.m_InstanceVersion = -1;
            return copy;
        }

        internal override void CollectChildren(List<LetteraEffect> results)
        {
            if (Entering(out bool enter))
            {
                LetteraEffect root = Instance;
                if (root != null) results.Add(root);
            }
            if (enter) Leave();
        }

        bool Entering(out bool entered)
        {
            entered = false;
            if (m_Preset == null) return false;
            if (s_Applying == null) s_Applying = new List<LetteraEffectPreset>(4);
            if (s_Applying.Contains(m_Preset))
            {
                if (s_WarnedCycles.Add(m_Preset)) Debug.LogWarning($"Lettera: effect preset '{m_Preset.name}' reaches itself; the loop is broken there.", m_Preset);
                return false;
            }
            s_Applying.Add(m_Preset);
            entered = true;
            return true;
        }

        static void Leave() => s_Applying.RemoveAt(s_Applying.Count - 1);

        public override void Apply(LetteraAttributes attributes, int start, int end, in LetteraSlots slots)
        {
            if (m_Preset == null || m_Preset.Root == null)
            {
                if (!m_Warned) Debug.LogWarning("Lettera: a preset effect has no preset, so it does nothing.");
                m_Warned = true;
                return;
            }
            if (!Entering(out bool entered)) return;
            try
            {
                LetteraEffect root = Instance;
                attributes.BeginChildEffect(root, 0);
                root.Apply(attributes, start, end, slots.ForChild(root, slots.Raw, slots.DefaultRaw));
            }
            finally
            {
                if (entered) Leave();
            }
        }
    }

    public enum LetteraHoldComposition : byte { Replace, Add, Multiply }

    /// <summary>One value a hold adds to a parameter of one range.</summary>
    internal struct HoldEntry
    {
        public object Effect;
        public int Slot;
        public LetteraHoldComposition Composition;
        public string Token;
        public float Number;
        public Color32 Color;
        public bool HasNumber, HasColor;
    }

    /// <summary>What a query knows about a range. Valid until the next rebuild.</summary>
    public struct LetteraRangeInfo
    {
        /// <summary>The range's position in text order among all ranges, and the codepoints it covers in the rendered text.</summary>
        public int Index, Start, End;
        public string Primary, Label;
        internal string[] Slots;

        /// <summary>The tag's token at a position, or null.</summary>
        public string Token(int slot) => Slots != null && slot < Slots.Length ? Slots[slot] : null;

        public bool IsBare => Slots == null || Slots.Length == 0;
    }

    /// <summary>
    /// The ranges of one effect, filtered. Every filter returns a new query; filters combine with AND, and results stay in text order.
    /// A query runs again each time it is read, so it stays right after the text changes.
    /// </summary>
    public sealed class LetteraRangeQuery
    {
        readonly TextEngine m_Engine;
        readonly LetteraEffect m_Effect;
        readonly Func<LetteraRangeInfo, bool>[] m_Filters;
        readonly int m_Skip, m_Take;

        internal LetteraRangeQuery(TextEngine engine, LetteraEffect effect, Func<LetteraRangeInfo, bool>[] filters = null, int skip = 0, int take = int.MaxValue)
        {
            m_Engine = engine;
            m_Effect = effect;
            m_Filters = filters ?? new Func<LetteraRangeInfo, bool>[0];
            m_Skip = skip;
            m_Take = take;
        }

        LetteraRangeQuery With(Func<LetteraRangeInfo, bool> filter)
        {
            var filters = new Func<LetteraRangeInfo, bool>[m_Filters.Length + 1];
            Array.Copy(m_Filters, filters, m_Filters.Length);
            filters[m_Filters.Length] = filter;
            return new LetteraRangeQuery(m_Engine, m_Effect, filters, m_Skip, m_Take);
        }

        public LetteraRangeQuery WithLabel(string label) => With(r => r.Label == label);
        public LetteraRangeQuery WithPrimary(string primary) => With(r => r.Primary == primary);
        public LetteraRangeQuery WithToken(int slot, string token) => With(r => r.Token(slot) == token);
        public LetteraRangeQuery Bare() => With(r => r.IsBare);
        public LetteraRangeQuery Overlapping(int start, int end) => With(r => r.Start < end && start < r.End);
        public LetteraRangeQuery Inside(int start, int end) => With(r => r.Start >= start && r.End <= end);
        public LetteraRangeQuery Matching(Func<LetteraRangeInfo, bool> predicate) => With(predicate);
        public LetteraRangeQuery Skip(int count) => new LetteraRangeQuery(m_Engine, m_Effect, m_Filters, m_Skip + count, m_Take);
        public LetteraRangeQuery Take(int count) => new LetteraRangeQuery(m_Engine, m_Effect, m_Filters, m_Skip, Math.Min(m_Take, count));

        /// <summary>Adds the matches to a list, in text order.</summary>
        public void Collect(List<LetteraRangeInfo> results)
        {
            m_Engine.EnsureParsed();
            m_Engine.ForEachRange(m_Effect, results, m_Filters, m_Skip, m_Take);
        }

        /// <summary>Takes over a parameter of the matches until released, without writing the effect's field. The level is what a change of this parameter needs.</summary>
        public LetteraHold<T> Hold<T>(int slot, LetteraHoldComposition composition = LetteraHoldComposition.Replace, DirtyLevel level = DirtyLevel.Mesh, int priority = 0) =>
            new LetteraHold<T>(m_Engine, m_Effect, this, slot, composition, level, priority);
    }

    /// <summary>
    /// A value composed over the cascade (field, default, tag) for a query's ranges, until it is released. The matches are found again after
    /// each parse: new ranges take the shared value, and ones no longer matching go back to the cascade.
    /// </summary>
    public sealed class LetteraHold<T> : IDisposable, IHoldSource
    {
        readonly TextEngine m_Engine;
        readonly object m_Effect;
        readonly LetteraRangeQuery m_Query;
        readonly int m_Slot;
        readonly DirtyLevel m_Level;
        readonly List<LetteraRangeInfo> m_Members = new List<LetteraRangeInfo>();
        readonly Dictionary<int, T> m_Single = new Dictionary<int, T>();
        T m_Value;
        bool m_HasValue;
        bool m_Disposed;
        bool m_Suspended;

        /// <summary>Raised when membership changes after a parse.</summary>
        public event Action Changed;

        public int Priority { get; }
        public LetteraHoldComposition Composition { get; }

        internal LetteraHold(TextEngine engine, object effect, LetteraRangeQuery query, int slot, LetteraHoldComposition composition, DirtyLevel level, int priority)
        {
            if (composition != LetteraHoldComposition.Replace && !(typeof(T) == typeof(float) || typeof(T) == typeof(int) || typeof(T) == typeof(Color32)))
                throw new ArgumentException("Only numbers and colors can add or multiply.");
            m_Engine = engine;
            m_Effect = effect;
            m_Query = query;
            m_Slot = slot;
            m_Level = level;
            Composition = composition;
            Priority = priority;
            engine.AddHold(this);
            Refresh();
        }

        void CheckAlive()
        {
            if (m_Disposed) throw new ObjectDisposedException(nameof(LetteraHold<T>));
        }

        /// <summary>The value every member has, unless one was set apart.</summary>
        public T Value
        {
            get
            {
                CheckAlive();
                return m_Value;
            }
            set
            {
                CheckAlive();
                m_Value = value;
                m_HasValue = true;
                m_Single.Clear();
                m_Engine.ChangeHeld(m_Level);
            }
        }

        public int Count
        {
            get
            {
                CheckAlive();
                return m_Members.Count;
            }
        }

        /// <summary>Sets one member's value, until the next parse.</summary>
        public void SetValue(int member, T value)
        {
            CheckAlive();
            m_Single[m_Members[member].Index] = value;
            m_HasValue = true;
            m_Engine.ChangeHeld(m_Level);
        }

        /// <summary>A member's value from the tag, under this hold; default when the tag gives none.</summary>
        public string Underlying(int member)
        {
            CheckAlive();
            return m_Members[member].Token(m_Slot);
        }

        /// <summary>Takes the values away without losing membership.</summary>
        public bool Suspended
        {
            get => m_Suspended;
            set
            {
                CheckAlive();
                if (m_Suspended == value) return;
                m_Suspended = value;
                m_Engine.ChangeHeld(m_Level);
            }
        }

        /// <summary>Gives the parameter back to the cascade. Using the hold after this throws.</summary>
        public void Release() => Dispose();

        public void Dispose()
        {
            if (m_Disposed) return;
            m_Disposed = true;
            m_Engine.RemoveHold(this);
            m_Engine.ChangeHeld(m_Level);
        }

        void Refresh()
        {
            int before = m_Members.Count;
            m_Members.Clear();
            m_Single.Clear();
            m_Query.Collect(m_Members);
            if (before != m_Members.Count) Changed?.Invoke();
        }

        void IHoldSource.Refresh() => Refresh();

        void IHoldSource.Contribute(int rangeIndex, List<HoldEntry> results)
        {
            if (m_Suspended || !m_HasValue) return;
            bool isMember = false;
            foreach (LetteraRangeInfo member in m_Members)
            {
                if (member.Index != rangeIndex) continue;
                isMember = true;
                break;
            }
            if (!isMember) return;
            T value = m_Single.TryGetValue(rangeIndex, out T single) ? single : m_Value;
            var entry = new HoldEntry { Effect = m_Effect, Slot = m_Slot, Composition = Composition };
            switch (value)
            {
                case Color32 color:
                    entry.Color = color;
                    entry.HasColor = true;
                    entry.Token = "#" + ColorUtility.ToHtmlStringRGBA(color);
                    break;
                case float number:
                    entry.Number = number;
                    entry.HasNumber = true;
                    entry.Token = number.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
                    break;
                case int whole:
                    entry.Number = whole;
                    entry.HasNumber = true;
                    entry.Token = whole.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    break;
                case bool flag:
                    entry.Token = flag ? "true" : "false";
                    break;
                default:
                    entry.Token = value?.ToString();
                    break;
            }
            results.Add(entry);
        }
    }

    internal interface IHoldSource
    {
        int Priority { get; }
        void Refresh();
        void Contribute(int rangeIndex, List<HoldEntry> results);
    }
}

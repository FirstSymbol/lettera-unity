using System;
using System.Collections.Generic;

namespace FirstSymbol.Lettera
{
    internal sealed partial class TextEngine
    {
        readonly LetteraMatchContext m_MatchContext = new LetteraMatchContext();
        readonly LetteraAttributes m_Attributes = new LetteraAttributes();
        readonly List<LetteraStyle> m_EffectiveStyles = new List<LetteraStyle>();
        readonly List<int> m_MatcherOrder = new List<int>();
        readonly List<int> m_RangeOrder = new List<int>();

        // The rendered text as UTF-32, and for each codepoint its index in the source text.
        int[] m_Text = new int[64];
        int[] m_SourceIndex = new int[64];
        int m_TextLength;

        void CollectStyles()
        {
            m_EffectiveStyles.Clear();
            if (Literal) return;
            LetteraTextData data = m_Host.Data;
            int hostStyles = 0;
            if (m_Host.HostStyles != null)
            {
                foreach (LetteraStyle style in m_Host.HostStyles) m_EffectiveStyles.Add(style);
                hostStyles = m_EffectiveStyles.Count;
            }
            m_EffectiveStyles.AddRange(data.Styles);
            foreach (LetteraStylePreset preset in data.StylePresets)
                if (preset != null)
                    m_EffectiveStyles.AddRange(preset.Styles);
            if (data.UseGlobalStylePreset)
            {
                LetteraStylePreset global = LetteraSettings.Instance.GlobalStylePreset;
                m_EffectiveStyles.AddRange(global != null ? global.Styles : LetteraDefaultStyles.Styles);
            }
            if (m_OwnCopies) ReplaceSharedStylesWithCopies(hostStyles + data.Styles.Count);
        }

        // Shared styles are used directly until someone asks for this text's effects to change them.
        bool m_OwnCopies;
        readonly Dictionary<LetteraStyle, LetteraStyle> m_Copies = new Dictionary<LetteraStyle, LetteraStyle>();

        void ReplaceSharedStylesWithCopies(int ownStyles)
        {
            for (int i = ownStyles; i < m_EffectiveStyles.Count; i++)
            {
                LetteraStyle shared = m_EffectiveStyles[i];
                if (shared == null) continue;
                if (!m_Copies.TryGetValue(shared, out LetteraStyle copy))
                {
                    copy = shared.Clone();
                    m_Copies[shared] = copy;
                }
                m_EffectiveStyles[i] = copy;
            }
        }

        /// <summary>Forgets this text's copies of shared styles, because the originals changed.</summary>
        public void InvalidateStyleCopies() => m_Copies.Clear();

        void EnsureOwnCopies()
        {
            if (m_OwnCopies) return;
            m_OwnCopies = true;
            CollectStyles();
        }

        void Parse()
        {
            ParseCount++;
            m_Reapply = false;
            ReadOnlySpan<char> source = RawChars;
            if (m_Resolver != null)
            {
                m_ResolvedText = m_Resolver.Resolve(m_Host.Owner, RawText) ?? "";
                source = m_ResolvedText.AsSpan();
            }

            CollectStyles();
            LetteraMatchContext context = m_MatchContext;
            context.Begin(source);
            if (m_ProtectLength > 0 && m_ProtectStart + m_ProtectLength <= source.Length) context.Protect(m_ProtectStart, m_ProtectLength);

            // Higher priority first; the sort is stable so earlier styles win ties.
            m_MatcherOrder.Clear();
            for (int i = 0; i < m_EffectiveStyles.Count; i++)
            {
                LetteraStyle style = m_EffectiveStyles[i];
                if (style != null && style.Enabled && style.Matcher != null) m_MatcherOrder.Add(i);
            }
            SortMatchers();
            context.HasEscapes = false;
            foreach (int index in m_MatcherOrder) context.HasEscapes |= m_EffectiveStyles[index].Matcher is EscapeMatcher;
            foreach (int index in m_MatcherOrder)
            {
                context.CurrentStyle = index;
                m_EffectiveStyles[index].Matcher.Find(context);
            }

            if (RevealSource >= 0) RevealRangesAt(context, RevealSource);
            BuildRenderedText(source, context);
            ResolveRangeEnds(context);
            AssignRangeIds(context);
            m_RangesApplied = false;
            foreach (IHoldSource hold in m_Holds) hold.Refresh();
            ApplyEffects(context);
            m_RangesApplied = true;
        }

        void ResolveRangeEnds(LetteraMatchContext context)
        {
            List<LetteraMatchContext.RangeRecord> ranges = context.Ranges;
            for (int i = 0; i < ranges.Count; i++)
            {
                LetteraMatchContext.RangeRecord r = ranges[i];
                if (r.WholeText) { r.RenderedStart = 0; r.RenderedEnd = m_TextLength; }
                else if (r.IsSpan)
                {
                    r.RenderedStart = Math.Min(Math.Max(r.SpanStart, 0), m_TextLength);
                    r.RenderedEnd = Math.Min(r.RenderedStart + Math.Max(r.SpanLength, 0), m_TextLength);
                }
                else if (r.RenderedEnd < 0) r.RenderedEnd = m_TextLength; // An unclosed tag runs to the end.
                ranges[i] = r;
            }
        }

        // ------------------------------------------------------------- queries and holds

        readonly List<IHoldSource> m_Holds = new List<IHoldSource>();
        readonly List<HoldEntry> m_HoldEntries = new List<HoldEntry>();
        bool m_RangesApplied;

        internal void AddHold(IHoldSource hold)
        {
            m_Holds.Add(hold);
            m_Holds.Sort((a, b) => a.Priority.CompareTo(b.Priority));
        }

        internal void RemoveHold(IHoldSource hold) => m_Holds.Remove(hold);

        internal void ChangeHeld(DirtyLevel level)
        {
            if (level == DirtyLevel.Pose)
            {
                m_PoseDirty = true;
                return;
            }
            ReapplyEffects(level);
            m_Host.RequestRebuild(level);
        }

        List<HoldEntry> HoldsOf(int rangeIndex)
        {
            if (m_Holds.Count == 0) return null;
            m_HoldEntries.Clear();
            foreach (IHoldSource hold in m_Holds) hold.Contribute(rangeIndex, m_HoldEntries);
            return m_HoldEntries.Count > 0 ? m_HoldEntries : null;
        }

        /// <summary>Makes the ranges current, for a query before the frame's rebuild.</summary>
        public void EnsureParsed()
        {
            if (m_Dirty < DirtyLevel.Parse) return;
            Parse();
            m_EarlyChanges |= LetteraChanges.Text;
            m_Dirty = DirtyLevel.Shape;
        }

        static bool Contains(LetteraEffect root, LetteraEffect target, int depth)
        {
            if (root == target) return true;
            if (root == null || depth > 16) return false;
            s_Children.Clear();
            root.CollectChildren(s_Children);
            var children = s_Children.ToArray();
            foreach (LetteraEffect child in children)
                if (Contains(child, target, depth + 1))
                    return true;
            return false;
        }

        static readonly List<LetteraEffect> s_Children = new List<LetteraEffect>();

        internal void ForEachRange(LetteraEffect effect, List<LetteraRangeInfo> results, Func<LetteraRangeInfo, bool>[] filters, int skip, int take)
        {
            List<LetteraMatchContext.RangeRecord> ranges = m_MatchContext.Ranges;
            var order = new List<int>(ranges.Count);
            for (int i = 0; i < ranges.Count; i++)
                if (Contains(m_EffectiveStyles[ranges[i].StyleIndex]?.Effect, effect, 0))
                    order.Add(i);
            order.Sort((a, b) => ranges[a].RenderedStart != ranges[b].RenderedStart ? ranges[a].RenderedStart.CompareTo(ranges[b].RenderedStart) : a.CompareTo(b));
            foreach (int index in order)
            {
                LetteraMatchContext.RangeRecord r = ranges[index];
                var info = new LetteraRangeInfo { Index = index, Id = RangeIdOf(index), Start = r.RenderedStart, End = r.RenderedEnd, Primary = r.Primary, Label = r.Label, Slots = r.Slots };
                bool passes = true;
                foreach (Func<LetteraRangeInfo, bool> filter in filters) passes &= filter(info);
                if (!passes) continue;
                if (skip > 0)
                {
                    skip--;
                    continue;
                }
                if (take-- <= 0) break;
                results.Add(info);
            }
        }

        void BuildRenderedText(ReadOnlySpan<char> source, LetteraMatchContext context)
        {
            List<LetteraMatchContext.Claim> claims = context.Claims;
            if (claims.Count > 1) claims.Sort(s_ByStart);

            EnsureSize(ref m_Text, source.Length + 4);
            EnsureSize(ref m_SourceIndex, source.Length + 4);
            int count = 0;
            int at = 0;
            int nextClaim = 0;
            List<LetteraMatchContext.RangeRecord> ranges = context.Ranges;
            while (at <= source.Length)
            {
                int stop = nextClaim < claims.Count ? claims[nextClaim].Start : source.Length;
                while (at < stop)
                {
                    int cp = source[at];
                    int width = 1;
                    if (char.IsHighSurrogate((char)cp) && at + 1 < source.Length && char.IsLowSurrogate(source[at + 1]))
                    {
                        cp = char.ConvertToUtf32((char)cp, source[at + 1]);
                        width = 2;
                    }
                    else if (char.IsSurrogate((char)cp))
                    {
                        cp = 0xFFFD;
                    }
                    m_Text[count] = cp;
                    m_SourceIndex[count] = at;
                    count++;
                    at += width;
                }
                if (nextClaim >= claims.Count) break;

                LetteraMatchContext.Claim claim = claims[nextClaim++];
                if (claim.Length == 0 && claim.Start > at) at = claim.Start;
                if (claim.OpenRange >= 0)
                {
                    LetteraMatchContext.RangeRecord range = ranges[claim.OpenRange];
                    range.RenderedStart = count;
                    ranges[claim.OpenRange] = range;
                }
                if (claim.Replacement != null)
                {
                    EnsureSize(ref m_Text, count + claim.Replacement.Length + (source.Length - at) + 4);
                    EnsureSize(ref m_SourceIndex, count + claim.Replacement.Length + (source.Length - at) + 4);
                    foreach (char c in claim.Replacement)
                    {
                        m_Text[count] = c;
                        m_SourceIndex[count] = claim.Start;
                        count++;
                    }
                }
                if (claim.CloseRange >= 0)
                {
                    LetteraMatchContext.RangeRecord range = ranges[claim.CloseRange];
                    range.RenderedEnd = count;
                    ranges[claim.CloseRange] = range;
                }
                if (claim.Start + claim.Length > at) at = claim.Start + claim.Length;
            }
            m_TextLength = count;
            m_SourceIndex[count] = source.Length;
        }

        // At one position, what ends comes before what starts, so adjacent ranges do not overlap. Longer claims come last,
        // because empty ones mark positions before the text a longer one takes away.
        static readonly Comparison<LetteraMatchContext.Claim> s_ByStart = (a, b) =>
        {
            if (a.Start != b.Start) return a.Start.CompareTo(b.Start);
            if (a.Length != b.Length) return a.Length.CompareTo(b.Length);
            bool aCloses = a.OpenRange < 0, bCloses = b.OpenRange < 0;
            return aCloses == bCloses ? 0 : aCloses ? -1 : 1;
        };

        void ApplyEffects(LetteraMatchContext context)
        {
            LetteraAttributes attributes = m_Attributes;
            attributes.Reset(m_TextLength);
            List<LetteraMatchContext.RangeRecord> ranges = context.Ranges;
            ClearStageHooks();

            // Outer ranges first, so inner ones refine them: innermost wins.
            m_RangeOrder.Clear();
            for (int i = 0; i < ranges.Count; i++) m_RangeOrder.Add(i);
            if (ranges.Count > 1) m_RangeOrder.Sort(m_CompareRanges ?? (m_CompareRanges = CompareRanges));

            foreach (int index in m_RangeOrder)
            {
                LetteraMatchContext.RangeRecord r = ranges[index];
                LetteraStyle style = m_EffectiveStyles[r.StyleIndex];
                if (style.Effect == null) continue;
                attributes.BeginEffect(style.Effect, r.StyleIndex);
                attributes.CurrentRange = index;
                RegisterStages(style.Effect, r.RenderedStart, r.RenderedEnd);
                try
                {
                    List<HoldEntry> holds = HoldsOf(index);
                    style.Effect.Apply(attributes, r.RenderedStart, r.RenderedEnd, new LetteraSlots(r.Slots, style.ParsedDefault(), r.Raw, style.DefaultParameter, holds, style.Effect));
                }
                catch (Exception e)
                {
                    UnityEngine.Debug.LogException(e, m_Host.Owner);
                }
            }

            LetteraTextData data = m_Host.Data;
            if (data.Overflow != LetteraOverflow.Visible)
                attributes.RequestElide(0, m_TextLength, data.OverflowPosition, data.Overflow == LetteraOverflow.Ellipsis);
            CollectMotions();
            CollectActiveRanges();
            CollectInlineItems();
        }

        Comparison<int> m_CompareRanges;

        int CompareRanges(int a, int b)
        {
            List<LetteraMatchContext.RangeRecord> ranges = m_MatchContext.Ranges;
            int byStart = ranges[a].RenderedStart.CompareTo(ranges[b].RenderedStart);
            if (byStart != 0) return byStart;
            int byEnd = ranges[b].RenderedEnd.CompareTo(ranges[a].RenderedEnd);
            return byEnd != 0 ? byEnd : a.CompareTo(b);
        }

        // Stable, so earlier styles keep winning ties.
        void SortMatchers()
        {
            List<int> list = m_MatcherOrder;
            for (int i = 1; i < list.Count; i++)
            {
                int value = list[i];
                int priority = m_EffectiveStyles[value].Matcher.Priority;
                int j = i - 1;
                while (j >= 0 && m_EffectiveStyles[list[j]].Matcher.Priority < priority)
                {
                    list[j + 1] = list[j];
                    j--;
                }
                list[j + 1] = value;
            }
        }

        public int SourceIndexFromRendered(int rendered)
        {
            if (m_TextLength == 0) return 0;
            return m_SourceIndex[Math.Min(Math.Max(rendered, 0), m_TextLength)];
        }

        public int RenderedIndexFromSource(int source)
        {
            int low = 0, high = m_TextLength;
            while (low < high)
            {
                int middle = (low + high) / 2;
                if (m_SourceIndex[middle] < source) low = middle + 1;
                else high = middle;
            }
            return low;
        }

        /// <summary>The rendered text without markup. A new string; prefer it for tooling, not per frame.</summary>
        public string CleanText
        {
            get
            {
                var builder = new System.Text.StringBuilder(m_TextLength);
                for (int i = 0; i < m_TextLength; i++) builder.Append(char.ConvertFromUtf32(m_Text[i] >= 0xD800 && m_Text[i] < 0xE000 ? 0xFFFD : m_Text[i]));
                return builder.ToString();
            }
        }

        /// <summary>The value a range stands for, such as a URL, for ranges whose effect is of a type. Results are in text order.</summary>
        public void GetPrimaryValues<T>(List<(int start, int end, string primary)> results) where T : class
        {
            List<LetteraMatchContext.RangeRecord> ranges = m_MatchContext.Ranges;
            for (int i = 0; i < ranges.Count; i++)
            {
                LetteraMatchContext.RangeRecord r = ranges[i];
                if (m_EffectiveStyles[r.StyleIndex]?.Effect is T) results.Add((r.RenderedStart, r.RenderedEnd, r.Primary ?? (r.Slots != null && r.Slots.Length > 0 ? r.Slots[0] : null)));
            }
            results.Sort((a, b) => a.start.CompareTo(b.start));
        }

        public T GetEffect<T>() where T : class
        {
            EnsureOwnCopies();
            var results = new List<T>(1);
            foreach (LetteraStyle style in m_EffectiveStyles)
            {
                Gather(style?.Effect, results, 0);
                if (results.Count > 0) return results[0];
            }
            return null;
        }

        public void GetEffects<T>(List<T> results) where T : class
        {
            EnsureOwnCopies();
            foreach (LetteraStyle style in m_EffectiveStyles) Gather(style?.Effect, results, 0);
        }

        // Children of composites and presets count too.
        static void Gather<T>(LetteraEffect effect, List<T> results, int depth) where T : class
        {
            if (effect == null || depth > 16) return;
            if (effect is T match) results.Add(match);
            var children = new List<LetteraEffect>();
            effect.CollectChildren(children);
            foreach (LetteraEffect child in children) Gather(child, results, depth + 1);
        }
    }
}

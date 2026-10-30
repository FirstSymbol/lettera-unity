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
            LetteraTextData data = m_Host.Data;
            m_EffectiveStyles.AddRange(data.Styles);
            foreach (LetteraStylePreset preset in data.StylePresets)
                if (preset != null)
                    m_EffectiveStyles.AddRange(preset.Styles);
            if (data.UseGlobalStylePreset)
            {
                LetteraStylePreset global = LetteraSettings.Instance.GlobalStylePreset;
                m_EffectiveStyles.AddRange(global != null ? global.Styles : LetteraDefaultStyles.Styles);
            }
        }

        void Parse()
        {
            string raw = RawText;
            m_ResolvedText = m_Resolver != null ? m_Resolver.Resolve(m_Host.Owner, raw) ?? "" : raw;
            string source = m_ResolvedText;

            CollectStyles();
            LetteraMatchContext context = m_MatchContext;
            context.Begin(source);

            // Higher priority first; the sort is stable so earlier styles win ties.
            m_MatcherOrder.Clear();
            for (int i = 0; i < m_EffectiveStyles.Count; i++)
            {
                LetteraStyle style = m_EffectiveStyles[i];
                if (style != null && style.Enabled && style.Matcher != null) m_MatcherOrder.Add(i);
            }
            InsertionSort(m_MatcherOrder, (a, b) => m_EffectiveStyles[b].Matcher.Priority.CompareTo(m_EffectiveStyles[a].Matcher.Priority));
            foreach (int index in m_MatcherOrder)
            {
                context.CurrentStyle = index;
                m_EffectiveStyles[index].Matcher.Find(context);
            }

            BuildRenderedText(source, context);
            ApplyEffects(context);
        }

        void BuildRenderedText(string source, LetteraMatchContext context)
        {
            List<LetteraMatchContext.Claim> claims = context.Claims;
            claims.Sort((a, b) => a.Start.CompareTo(b.Start));

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
                at = claim.Start + claim.Length;
            }
            m_TextLength = count;
            m_SourceIndex[count] = source.Length;
        }

        void ApplyEffects(LetteraMatchContext context)
        {
            LetteraAttributes attributes = m_Attributes;
            attributes.Reset(m_TextLength);
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

            // Outer ranges first, so inner ones refine them: innermost wins.
            m_RangeOrder.Clear();
            for (int i = 0; i < ranges.Count; i++) m_RangeOrder.Add(i);
            m_RangeOrder.Sort((a, b) =>
            {
                int byStart = ranges[a].RenderedStart.CompareTo(ranges[b].RenderedStart);
                if (byStart != 0) return byStart;
                int byEnd = ranges[b].RenderedEnd.CompareTo(ranges[a].RenderedEnd);
                return byEnd != 0 ? byEnd : a.CompareTo(b);
            });

            foreach (int index in m_RangeOrder)
            {
                LetteraMatchContext.RangeRecord r = ranges[index];
                LetteraStyle style = m_EffectiveStyles[r.StyleIndex];
                if (style.Effect == null) continue;
                try
                {
                    style.Effect.Apply(attributes, r.RenderedStart, r.RenderedEnd, new LetteraSlots(r.Slots, style.ParsedDefault()));
                }
                catch (Exception e)
                {
                    UnityEngine.Debug.LogException(e, m_Host.Owner);
                }
            }

            LetteraTextData data = m_Host.Data;
            if (data.Overflow != LetteraOverflow.Visible)
                attributes.RequestElide(0, m_TextLength, data.OverflowPosition, data.Overflow == LetteraOverflow.Ellipsis);
        }

        static void InsertionSort(List<int> list, Comparison<int> compare)
        {
            for (int i = 1; i < list.Count; i++)
            {
                int value = list[i];
                int j = i - 1;
                while (j >= 0 && compare(list[j], value) > 0)
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

        public T GetEffect<T>() where T : class
        {
            foreach (LetteraStyle style in m_EffectiveStyles)
                if (style?.Effect is T match)
                    return match;
            return null;
        }

        public void GetEffects<T>(List<T> results) where T : class
        {
            foreach (LetteraStyle style in m_EffectiveStyles)
                if (style?.Effect is T match)
                    results.Add(match);
        }
    }
}

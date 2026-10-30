using System;
using System.Collections.Generic;
using UnityEngine;

namespace FirstSymbol.Lettera
{
    /// <summary>
    /// Finds ranges in the source text. A matcher claims source positions and emits ranges; positions a
    /// matcher claimed cannot be claimed by another. Custom matchers derive from this class.
    /// </summary>
    [Serializable]
    public abstract class LetteraMatcher
    {
        /// <summary>Higher priority claims first. Equal priority: the earlier style wins.</summary>
        public virtual int Priority => 0;

        /// <summary>Scans the source text and reports claims and ranges to the context.</summary>
        public abstract void Find(LetteraMatchContext context);

        /// <summary>A copy for one text. Override when the matcher holds a list or another nested object.</summary>
        public virtual LetteraMatcher Clone() => (LetteraMatcher)MemberwiseClone();
    }

    /// <summary>What a matcher sees and reports while the source text is parsed.</summary>
    public sealed class LetteraMatchContext
    {
        internal struct Claim
        {
            public int Start, Length;
            public string Replacement;
            public int OpenRange;   // A range index that starts here, or -1.
            public int CloseRange;  // A range index that ends here, or -1.
        }

        internal struct RangeRecord
        {
            public int StyleIndex;
            public int RenderedStart, RenderedEnd;
            public string[] Slots;
            public string Raw;      // The tag's value before it was split into slots.
            public string Label;
            public string Primary;  // A URL or a name the range stands for.
            public bool WholeText;
            public int SpanStart, SpanLength;
            public bool IsSpan;
            public bool SelfClosing;
        }

        internal readonly List<Claim> Claims = new List<Claim>();
        internal readonly List<RangeRecord> Ranges = new List<RangeRecord>();
        readonly List<int> m_LessThan = new List<int>();
        bool[] m_Blocked = new bool[256];
        char[] m_Chars = new char[256];
        int m_Length;
        string m_Source;
        internal int CurrentStyle;

        /// <summary>The source text being parsed. Reading this makes no copy.</summary>
        public ReadOnlySpan<char> Text => new ReadOnlySpan<char>(m_Chars, 0, m_Length);

        /// <summary>The source text as a string. It is built on first use, so prefer <see cref="Text"/>.</summary>
        public string Source => m_Source ?? (m_Source = new string(m_Chars, 0, m_Length));

        /// <summary>Positions of every '&lt;' in the source, in order.</summary>
        public IReadOnlyList<int> LessThanPositions => m_LessThan;

        internal void Begin(ReadOnlySpan<char> source)
        {
            m_Source = null;
            m_Length = source.Length;
            Claims.Clear();
            Ranges.Clear();
            m_LessThan.Clear();
            if (m_Chars.Length < source.Length)
            {
                int capacity = Math.Max(source.Length, m_Chars.Length * 2);
                m_Chars = new char[capacity];
                m_Blocked = new bool[capacity];
            }
            else
            {
                Array.Clear(m_Blocked, 0, source.Length);
            }
            source.CopyTo(m_Chars);
            for (int i = 0; i < source.Length; i++)
                if (source[i] == '<')
                    m_LessThan.Add(i);
        }

        /// <summary>True when no other matcher holds any position of the span.</summary>
        public bool IsFree(int start, int length)
        {
            for (int i = start; i < start + length; i++)
                if (m_Blocked[i])
                    return false;
            return true;
        }

        /// <summary>Keeps a span as literal text that no later matcher may claim.</summary>
        public void Protect(int start, int length)
        {
            for (int i = start; i < start + length; i++) m_Blocked[i] = true;
        }

        /// <summary>Removes a span from the rendered text, or swaps it for a replacement.</summary>
        public void Replace(int start, int length, string replacement = null)
        {
            Protect(start, length);
            Claims.Add(new Claim { Start = start, Length = length, Replacement = replacement, OpenRange = -1, CloseRange = -1 });
        }

        /// <summary>Removes a tag and opens a range for the current style. Returns its handle for <see cref="CloseRange"/>.</summary>
        public int OpenRange(int start, int length, string[] slots, string label = null, bool selfClosing = false, string raw = null)
        {
            Protect(start, length);
            int index = Ranges.Count;
            Ranges.Add(new RangeRecord { StyleIndex = CurrentStyle, RenderedStart = -1, RenderedEnd = -1, Slots = slots, Raw = raw, Label = label, SelfClosing = selfClosing });
            Claims.Add(new Claim { Start = start, Length = length, OpenRange = index, CloseRange = selfClosing ? index : -1 });
            return index;
        }

        /// <summary>Removes a closing tag and ends a range opened by <see cref="OpenRange"/>.</summary>
        public void CloseRange(int handle, int start, int length)
        {
            Protect(start, length);
            Claims.Add(new Claim { Start = start, Length = length, OpenRange = -1, CloseRange = handle });
        }

        /// <summary>Puts text into the rendered text at a source position, removing nothing.</summary>
        public void InsertAt(int position, string text) => Claims.Add(new Claim { Start = position, Length = 0, Replacement = text, OpenRange = -1, CloseRange = -1 });

        /// <summary>A range over source text other matchers can still claim inside, such as tags in a ruby base.</summary>
        public int AddSoftRange(int start, int length, string[] slots)
        {
            int index = Ranges.Count;
            Ranges.Add(new RangeRecord { StyleIndex = CurrentStyle, RenderedStart = -1, RenderedEnd = -1, Slots = slots });
            Claims.Add(new Claim { Start = start, Length = 0, OpenRange = index, CloseRange = -1 });
            Claims.Add(new Claim { Start = start + length, Length = 0, OpenRange = -1, CloseRange = index });
            return index;
        }

        /// <summary>Ends a range at a source position, without removing anything there.</summary>
        public void CloseRangeAt(int handle, int position) => Claims.Add(new Claim { Start = position, Length = 0, OpenRange = -1, CloseRange = handle });

        /// <summary>Gives a range the value it stands for, such as a URL.</summary>
        public void SetPrimary(int handle, string primary)
        {
            RangeRecord range = Ranges[handle];
            range.Primary = primary;
            Ranges[handle] = range;
        }

        /// <summary>A range over source text that stays visible. Slots and a primary value are optional.</summary>
        public int AddRawRange(int start, int length, string[] slots, string primary)
        {
            int index = Ranges.Count;
            Ranges.Add(new RangeRecord { StyleIndex = CurrentStyle, RenderedStart = -1, RenderedEnd = -1, Slots = slots, Primary = primary });
            Claims.Add(new Claim { Start = start, Length = 0, OpenRange = index, CloseRange = -1 });
            Claims.Add(new Claim { Start = start + length, Length = 0, OpenRange = -1, CloseRange = index });
            Protect(start, length);
            return index;
        }

        /// <summary>Swaps source text for a replacement, with a range over what replaced it.</summary>
        public int ReplaceAndRange(int start, int length, string replacement, string[] slots, string label)
        {
            Protect(start, length);
            int index = Ranges.Count;
            Ranges.Add(new RangeRecord { StyleIndex = CurrentStyle, RenderedStart = -1, RenderedEnd = -1, Slots = slots, Label = label });
            Claims.Add(new Claim { Start = start, Length = length, Replacement = replacement, OpenRange = index, CloseRange = index });
            return index;
        }

        /// <summary>A range over the whole rendered text.</summary>
        public void AddWholeText(string[] slots = null)
        {
            Ranges.Add(new RangeRecord { StyleIndex = CurrentStyle, WholeText = true, Slots = slots, RenderedStart = -1, RenderedEnd = -1 });
        }

        /// <summary>A range given in rendered codepoint indices; clamped to the text.</summary>
        public void AddSpan(int start, int length, string[] slots = null)
        {
            Ranges.Add(new RangeRecord { StyleIndex = CurrentStyle, IsSpan = true, SpanStart = start, SpanLength = length, Slots = slots, RenderedStart = -1, RenderedEnd = -1 });
        }
    }

    /// <summary>Binds an effect to &lt;name&gt;...&lt;/name&gt;. See the markup section of the manual for the grammar.</summary>
    [Serializable]
    public class TagMatcher : LetteraMatcher
    {
        [SerializeField] string m_Name = "";

        public TagMatcher() { }
        public TagMatcher(string name) => m_Name = name;

        public string Name { get => m_Name; set => m_Name = value; }

        readonly List<int> m_Open = new List<int>();

        public override void Find(LetteraMatchContext context)
        {
            if (string.IsNullOrEmpty(m_Name)) return;
            IReadOnlyList<int> positions = context.LessThanPositions;
            if (positions.Count == 0) return;
            ReadOnlySpan<char> source = context.Text;
            m_Open.Clear();
            for (int p = 0; p < positions.Count; p++)
            {
                int at = positions[p];
                if (!context.IsFree(at, 1)) continue;
                if (at + 1 < source.Length && source[at + 1] == '/')
                {
                    if (TagSyntax.MatchClose(source, at, m_Name, out int closeLength) && m_Open.Count > 0 && context.IsFree(at, closeLength))
                    {
                        context.CloseRange(m_Open[m_Open.Count - 1], at, closeLength);
                        m_Open.RemoveAt(m_Open.Count - 1);
                    }
                    continue;
                }
                if (!TagSyntax.MatchOpen(source, at, m_Name, out int length, out string[] slots, out string label, out bool selfClosing, out string raw)) continue;
                if (!context.IsFree(at, length)) continue;
                int handle = context.OpenRange(at, length, slots, label, selfClosing, raw);
                if (!selfClosing) m_Open.Add(handle);
            }
        }
    }

    /// <summary>The tag grammar: &lt;name #label=slot,slot/&gt; and &lt;/name&gt;.</summary>
    internal static class TagSyntax
    {
        static bool IsNameChar(char c) => c < 128 && (char.IsLetterOrDigit(c) || c == '-' || c == '_');

        static bool MatchName(ReadOnlySpan<char> source, int at, string name, out int end)
        {
            end = at;
            int n = name.Length;
            if (at + n > source.Length) return false;
            for (int i = 0; i < n; i++)
            {
                char a = source[at + i], b = name[i];
                if (a == b) continue;
                if (a >= 'A' && a <= 'Z') a = (char)(a + 32);
                if (b >= 'A' && b <= 'Z') b = (char)(b + 32);
                if (a != b) return false;
            }
            end = at + n;
            return end >= source.Length || !IsNameChar(source[end]);
        }

        public static bool MatchClose(ReadOnlySpan<char> source, int at, string name, out int length)
        {
            length = 0;
            if (at + 2 >= source.Length || source[at] != '<' || source[at + 1] != '/') return false;
            if (!MatchName(source, at + 2, name, out int end)) return false;
            while (end < source.Length && source[end] == ' ') end++;
            if (end >= source.Length || source[end] != '>') return false;
            length = end + 1 - at;
            return true;
        }

        public static bool MatchOpen(ReadOnlySpan<char> source, int at, string name, out int length, out string[] slots, out string label, out bool selfClosing) =>
            MatchOpen(source, at, name, out length, out slots, out label, out selfClosing, out _);

        public static bool MatchOpen(ReadOnlySpan<char> source, int at, string name, out int length, out string[] slots, out string label, out bool selfClosing, out string raw)
        {
            length = 0;
            slots = null;
            raw = null;
            label = null;
            selfClosing = false;
            if (source[at] != '<' || at + 1 >= source.Length) return false;
            if (!MatchName(source, at + 1, name, out int i)) return false;

            // Optional #label.
            int cursor = i;
            while (cursor < source.Length && source[cursor] == ' ') cursor++;
            if (cursor < source.Length && source[cursor] == '#')
            {
                int labelStart = ++cursor;
                while (cursor < source.Length && IsNameChar(source[cursor])) cursor++;
                if (cursor == labelStart) return false;
                label = source.Slice(labelStart, cursor - labelStart).ToString();
                i = cursor;
            }
            while (i < source.Length && source[i] == ' ') i++;
            if (i >= source.Length) return false;

            if (source[i] == '=')
            {
                i++;
                int valueStart = i;
                if (!ParseSlots(source, ref i, true, out slots)) return false;
                raw = source.Slice(valueStart, i - valueStart).Trim().ToString();
            }
            while (i < source.Length && source[i] == ' ') i++;
            if (i < source.Length && source[i] == '/')
            {
                selfClosing = true;
                i++;
            }
            if (i >= source.Length || source[i] != '>') return false;
            length = i + 1 - at;
            return true;
        }

        /// <summary>Splits a parameter string outside a tag with the tag's rules. Empty slots are null.</summary>
        public static string[] SplitSlots(string value)
        {
            if (string.IsNullOrEmpty(value)) return null;
            int i = 0;
            ParseSlots(value.AsSpan(), ref i, false, out string[] slots);
            return slots;
        }

        // The index past a parenthesis group that opens at 'open', or -1 when it does not close inside the value.
        static int SkipGroup(ReadOnlySpan<char> source, int open, bool inTag)
        {
            int depth = 0;
            for (int i = open; i < source.Length; i++)
            {
                char c = source[i];
                if (c == '(') depth++;
                else if (c == ')' && --depth == 0) return i + 1;
                else if (inTag && c == '>') return -1;
            }
            return -1;
        }

        static bool ParseSlots(ReadOnlySpan<char> source, ref int i, bool inTag, out string[] slots)
        {
            var list = new List<string>(4);
            slots = null;
            while (true)
            {
                while (i < source.Length && source[i] == ' ') i++;
                if (i >= source.Length)
                {
                    if (inTag) return false;
                    list.Add(null);
                    break;
                }
                char quote = source[i];
                if (quote == '"' || quote == '\'')
                {
                    int close = source.Slice(i + 1).IndexOf(quote);
                    if (close < 0) return false;
                    list.Add(source.Slice(i + 1, close).ToString());
                    i += close + 2;
                    while (i < source.Length && source[i] == ' ') i++;
                }
                else
                {
                    int start = i;
                    while (i < source.Length && source[i] != ',' && !(inTag && source[i] == '>'))
                    {
                        // A '/' right before '>' closes the tag rather than belonging to the value.
                        if (inTag && source[i] == '/' && i + 1 < source.Length && source[i + 1] == '>') break;
                        // A comma inside balanced parentheses stays in the slot, so rgb(9,8,7) is one token.
                        if (source[i] == '(')
                        {
                            int after = SkipGroup(source, i, inTag);
                            if (after > 0)
                            {
                                i = after;
                                continue;
                            }
                        }
                        i++;
                    }
                    ReadOnlySpan<char> slot = source.Slice(start, i - start).Trim();
                    list.Add(slot.Length == 0 ? null : slot.ToString());
                }
                if (i < source.Length && source[i] == ',')
                {
                    i++;
                    continue;
                }
                break;
            }
            slots = list.ToArray();
            return true;
        }
    }

    /// <summary>&lt;noparse&gt;...&lt;/noparse&gt;: the content stays literal. A missing closer protects the rest.</summary>
    [Serializable]
    public class NoParseMatcher : LetteraMatcher
    {
        public override int Priority => int.MaxValue;

        public override void Find(LetteraMatchContext context)
        {
            IReadOnlyList<int> positions = context.LessThanPositions;
            if (positions.Count == 0) return;
            ReadOnlySpan<char> source = context.Text;
            for (int p = 0; p < positions.Count; p++)
            {
                int at = positions[p];
                if (!context.IsFree(at, 1)) continue;
                if (!TagSyntax.MatchOpen(source, at, "noparse", out int length, out _, out _, out _)) continue;
                context.Replace(at, length);
                int contentStart = at + length;
                int close = -1, closeLength = 0;
                for (int q = p + 1; q < positions.Count; q++)
                {
                    if (!TagSyntax.MatchClose(source, positions[q], "noparse", out closeLength)) continue;
                    close = positions[q];
                    break;
                }
                int contentEnd = close >= 0 ? close : source.Length;
                context.Protect(contentStart, contentEnd - contentStart);
                if (close < 0) return;
                context.Replace(close, closeLength);
                while (p + 1 < positions.Count && positions[p + 1] < close + closeLength) p++;
            }
        }
    }

    /// <summary>A backslash before ASCII punctuation yields that character literally.</summary>
    [Serializable]
    public class EscapeMatcher : LetteraMatcher
    {
        public override int Priority => int.MaxValue - 1;

        public override void Find(LetteraMatchContext context)
        {
            ReadOnlySpan<char> source = context.Text;
            for (int i = 0; i + 1 < source.Length; i++)
            {
                if (source[i] != '\\' || !context.IsFree(i, 2)) continue;
                char next = source[i + 1];
                if (next >= 128 || !(char.IsPunctuation(next) || char.IsSymbol(next))) continue;
                context.Replace(i, 1);
                context.Protect(i + 1, 1);
                i++;
            }
        }
    }

    /// <summary>&lt;br&gt;, &lt;br/&gt; and &lt;br /&gt; insert a line break inside the paragraph. A stray &lt;/br&gt; is stripped.</summary>
    [Serializable]
    public class LineBreakMatcher : LetteraMatcher
    {
        const string LineSeparator = "\u2028";

        public override int Priority => 1;

        public override void Find(LetteraMatchContext context)
        {
            IReadOnlyList<int> positions = context.LessThanPositions;
            if (positions.Count == 0) return;
            ReadOnlySpan<char> source = context.Text;
            for (int p = 0; p < positions.Count; p++)
            {
                int at = positions[p];
                if (!context.IsFree(at, 1)) continue;
                if (TagSyntax.MatchOpen(source, at, "br", out int length, out _, out _, out _) && context.IsFree(at, length))
                    context.Replace(at, length, LineSeparator);
                else if (TagSyntax.MatchClose(source, at, "br", out length) && context.IsFree(at, length))
                    context.Replace(at, length);
            }
        }
    }

    /// <summary>One range over the whole rendered text.</summary>
    [Serializable]
    public class WholeTextMatcher : LetteraMatcher
    {
        public override void Find(LetteraMatchContext context) => context.AddWholeText();
    }

    /// <summary>Authored spans in rendered codepoint indices.</summary>
    [Serializable]
    public class SpanMatcher : LetteraMatcher
    {
        [SerializeField] List<Vector2Int> m_Spans = new List<Vector2Int>();

        public SpanMatcher() { }
        public SpanMatcher(int start, int length) => m_Spans.Add(new Vector2Int(start, length));

        /// <summary>Each span is (start, length).</summary>
        public List<Vector2Int> Spans => m_Spans;

        public override LetteraMatcher Clone()
        {
            var copy = (SpanMatcher)base.Clone();
            copy.m_Spans = new List<Vector2Int>(m_Spans);
            return copy;
        }

        public override void Find(LetteraMatchContext context)
        {
            foreach (Vector2Int span in m_Spans) context.AddSpan(span.x, span.y);
        }
    }
}

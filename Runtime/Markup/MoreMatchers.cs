using System;
using System.Collections.Generic;
using FirstSymbol.Lettera.Unicode;
using UnityEngine;

namespace FirstSymbol.Lettera
{
    /// <summary>&lt;name&gt; and &lt;name=value&gt; are always one self-closing insertion, and a stray &lt;/name&gt; is stripped.</summary>
    [Serializable]
    public class VoidTagMatcher : LetteraMatcher
    {
        [SerializeField] string m_Name = "";

        public VoidTagMatcher() { }
        public VoidTagMatcher(string name) => m_Name = name;

        public string Name { get => m_Name; set => m_Name = value; }

        public override void Find(LetteraMatchContext context)
        {
            if (string.IsNullOrEmpty(m_Name)) return;
            IReadOnlyList<int> positions = context.LessThanPositions;
            ReadOnlySpan<char> source = context.Text;
            for (int p = 0; p < positions.Count; p++)
            {
                int at = positions[p];
                if (!context.IsFree(at, 1)) continue;
                if (TagSyntax.MatchClose(source, at, m_Name, out int closeLength))
                {
                    if (context.IsFree(at, closeLength)) context.Replace(at, closeLength);
                    continue;
                }
                if (!TagSyntax.MatchOpen(source, at, m_Name, out int length, out string[] slots, out string label, out _, out string raw)) continue;
                if (!context.IsFree(at, length)) continue;
                context.OpenRange(at, length, slots, label, true, raw);
            }
        }
    }

    /// <summary>
    /// Inserts a separator string at &lt;name&gt;. Lines break only at separators while the pieces fit, and a separator where a
    /// line breaks is not drawn. Its slot overrides the string for one occurrence: &lt;divider=" · "&gt;.
    /// </summary>
    [Serializable]
    public class DividerMatcher : LetteraMatcher
    {
        [SerializeField] string m_Name = "divider";
        [SerializeField] string m_Text = " | ";

        public DividerMatcher() { }
        public DividerMatcher(string name) => m_Name = name;

        public string Name { get => m_Name; set => m_Name = value; }
        public string Text { get => m_Text; set => m_Text = value; }

        public override int Priority => 1;

        public override void Find(LetteraMatchContext context)
        {
            if (string.IsNullOrEmpty(m_Name)) return;
            IReadOnlyList<int> positions = context.LessThanPositions;
            ReadOnlySpan<char> source = context.Text;
            for (int p = 0; p < positions.Count; p++)
            {
                int at = positions[p];
                if (!context.IsFree(at, 1)) continue;
                if (TagSyntax.MatchClose(source, at, m_Name, out int closeLength))
                {
                    if (context.IsFree(at, closeLength)) context.Replace(at, closeLength);
                    continue;
                }
                if (!TagSyntax.MatchOpen(source, at, m_Name, out int length, out string[] slots, out string label, out _, out _)) continue;
                if (!context.IsFree(at, length)) continue;
                string text = slots != null && slots.Length > 0 && !string.IsNullOrEmpty(slots[0]) ? slots[0] : m_Text;
                context.ReplaceAndRange(at, length, text ?? "", slots, label);
            }
        }
    }

    /// <summary>The layout rule of a divider: its range is one breakable, droppable unit.</summary>
    [Serializable]
    public class DividerEffect : LetteraEffect
    {
        public override void Apply(LetteraAttributes attributes, int start, int end, in LetteraSlots slots) => attributes.AddDivider(start, end);
    }

    /// <summary>
    /// CommonMark's emphasis rules for one marker: it opens only when left-flanking and closes only when right-flanking;
    /// _ never toggles inside a word; characters stay literal when no valid pair exists; a pair does not cross a paragraph break.
    /// </summary>
    [Serializable]
    public class MarkdownMarkerMatcher : LetteraMatcher
    {
        [SerializeField] string m_Marker = "*";
        [SerializeField] string m_DefaultParameter = "";

        public MarkdownMarkerMatcher() { }
        public MarkdownMarkerMatcher(string marker) => m_Marker = marker;

        public string Marker { get => m_Marker; set => m_Marker = value; }
        public string DefaultParameter { get => m_DefaultParameter; set => m_DefaultParameter = value; }

        public override bool CanWrap => !string.IsNullOrEmpty(m_Marker);
        public override void WriteOpen(System.Text.StringBuilder target, string parameter, string label) => target.Append(m_Marker);
        public override void WriteClose(System.Text.StringBuilder target) => target.Append(m_Marker);

        /// <summary>A longer marker claims first, so ** goes before *.</summary>
        public override int Priority => string.IsNullOrEmpty(m_Marker) ? 0 : Math.Min(m_Marker.Length + 1, 1000);

        public override void Find(LetteraMatchContext context)
        {
            if (string.IsNullOrEmpty(m_Marker)) return;
            ReadOnlySpan<char> source = context.Text;
            int size = m_Marker.Length;
            string[] slots = TagSyntax.SplitSlots(m_DefaultParameter);
            int open = -1;
            int i = 0;
            while (i + size <= source.Length)
            {
                if (source[i] == '\n' || source[i] == '\r')
                {
                    open = -1;
                    i++;
                    continue;
                }
                if (!Matches(source, i) || !context.IsFree(i, size))
                {
                    i++;
                    continue;
                }
                // A run of the marker's character longer than the marker: use its outermost pair, so *** gives ** and *.
                bool left = LeftFlanking(source, i, size), right = RightFlanking(source, i, size);
                bool underscore = m_Marker[0] == '_';
                bool canOpen = left && (!underscore || !right || IsPunctuationBefore(source, i));
                bool canClose = right && (!underscore || !left || IsPunctuationAfter(source, i, size));
                if (open >= 0 && canClose)
                {
                    int handle = context.OpenRange(open, size, slots);
                    context.CloseRange(handle, i, size);
                    open = -1;
                    i += size;
                    continue;
                }
                if (canOpen)
                {
                    open = i;
                    i += size;
                    continue;
                }
                i += size;
            }
        }

        bool Matches(ReadOnlySpan<char> source, int at)
        {
            for (int i = 0; i < m_Marker.Length; i++)
                if (source[at + i] != m_Marker[i])
                    return false;
            return true;
        }

        static bool IsSpace(int c) => c < 0 || LetteraUnicode.IsWhiteSpace(c);

        static bool IsPunctuation(int c)
        {
            if (c < 0) return false;
            if (c < 128) return char.IsPunctuation((char)c) || char.IsSymbol((char)c);
            return c < 0x10000 && (char.IsPunctuation((char)c) || char.IsSymbol((char)c));
        }

        static int Before(ReadOnlySpan<char> s, int at) => at > 0 ? s[at - 1] : -1;
        static int After(ReadOnlySpan<char> s, int at) => at < s.Length ? s[at] : -1;

        static bool LeftFlanking(ReadOnlySpan<char> s, int at, int size)
        {
            int before = Before(s, at), after = After(s, at + size);
            if (IsSpace(after)) return false;
            return !IsPunctuation(after) || IsSpace(before) || IsPunctuation(before);
        }

        static bool RightFlanking(ReadOnlySpan<char> s, int at, int size)
        {
            int before = Before(s, at), after = After(s, at + size);
            if (IsSpace(before)) return false;
            return !IsPunctuation(before) || IsSpace(after) || IsPunctuation(after);
        }

        static bool IsPunctuationBefore(ReadOnlySpan<char> s, int at) => IsPunctuation(Before(s, at));
        static bool IsPunctuationAfter(ReadOnlySpan<char> s, int at, int size) => IsPunctuation(After(s, at + size));
    }

    /// <summary>
    /// Line-start list markers: - , * , + , 1. and 1) . The marker characters are removed, the number feeds the list effect's slot 2,
    /// and the source indent sets the level, one for each 2 spaces or tab.
    /// </summary>
    [Serializable]
    public class MarkdownListMatcher : LetteraMatcher
    {
        public override int Priority => 500;

        public override void Find(LetteraMatchContext context)
        {
            ReadOnlySpan<char> source = context.Text;
            int lineStart = 0;
            while (lineStart <= source.Length)
            {
                int lineEnd = lineStart;
                while (lineEnd < source.Length && source[lineEnd] != '\n' && source[lineEnd] != '\r') lineEnd++;
                TryItem(context, source, lineStart, lineEnd);
                if (lineEnd >= source.Length) break;
                lineStart = lineEnd + 1;
                if (source[lineEnd] == '\r' && lineStart < source.Length && source[lineStart] == '\n') lineStart++;
            }
        }

        static void TryItem(LetteraMatchContext context, ReadOnlySpan<char> source, int start, int end)
        {
            int i = start, spaces = 0;
            while (i < end && (source[i] == ' ' || source[i] == '\t'))
            {
                spaces += source[i] == '\t' ? 2 : 1;
                i++;
            }
            if (i >= end) return;
            int markerStart = i;
            string number = null;
            char c = source[i];
            if (c == '-' || c == '*' || c == '+')
            {
                i++;
            }
            else if (c >= '0' && c <= '9')
            {
                while (i < end && source[i] >= '0' && source[i] <= '9' && i - markerStart < 9) i++;
                if (i >= end || (source[i] != '.' && source[i] != ')')) return;
                number = source.Slice(markerStart, i - markerStart).ToString();
                i++;
            }
            else
            {
                return;
            }
            if (i >= end || source[i] != ' ') return;
            i++;
            if (!context.IsFree(start, i - start)) return;
            var slots = new[] { (spaces / 2).ToString(System.Globalization.CultureInfo.InvariantCulture), number, null, number != null ? "ordered" : null };
            int handle = context.OpenRange(start, i - start, slots);
            context.CloseRangeAt(handle, end);
        }
    }

    /// <summary>[text](url): keeps the text and gives the URL as the range's primary value and slot 1. No titles.</summary>
    [Serializable]
    public class MarkdownLinkMatcher : LetteraMatcher
    {
        public override int Priority => 400;

        public override void Find(LetteraMatchContext context)
        {
            ReadOnlySpan<char> source = context.Text;
            for (int i = 0; i < source.Length; i++)
            {
                if (source[i] != '[' || !context.IsFree(i, 1)) continue;
                int depth = 0, close = -1;
                for (int j = i; j < source.Length; j++)
                {
                    char c = source[j];
                    if (c == '\n') break;
                    if (c == '\\') { j++; continue; }
                    if (c == '[') depth++;
                    else if (c == ']' && --depth == 0)
                    {
                        close = j;
                        break;
                    }
                }
                if (close < 0 || close + 1 >= source.Length || source[close + 1] != '(') continue;
                int parens = 0, urlEnd = -1;
                for (int j = close + 1; j < source.Length; j++)
                {
                    char c = source[j];
                    if (c == '\n' || c == ' ') break;
                    if (c == '\\') { j++; continue; }
                    if (c == '(') parens++;
                    else if (c == ')' && --parens == 0)
                    {
                        urlEnd = j;
                        break;
                    }
                }
                if (urlEnd < 0 || !context.IsFree(close, urlEnd + 1 - close)) continue;
                string url = source.Slice(close + 2, urlEnd - close - 2).ToString();
                int handle = context.OpenRange(i, 1, new[] { url }, null, false, url);
                context.SetPrimary(handle, url);
                context.CloseRange(handle, close, urlEnd + 1 - close);
                i = urlEnd;
            }
        }
    }

    /// <summary>`code`: balanced backtick runs as CommonMark defines them. The content stays literal; a run with no equal closer stays literal text.</summary>
    [Serializable]
    public class CodeSpanMatcher : LetteraMatcher
    {
        public override int Priority => int.MaxValue - 2;

        public override void Find(LetteraMatchContext context)
        {
            ReadOnlySpan<char> source = context.Text;
            int i = 0;
            while (i < source.Length)
            {
                if (source[i] != '`' || !context.IsFree(i, 1))
                {
                    i++;
                    continue;
                }
                int run = RunAt(source, i);
                int close = -1;
                for (int j = i + run; j < source.Length; j++)
                {
                    if (source[j] != '`') continue;
                    int other = RunAt(source, j);
                    if (other == run)
                    {
                        close = j;
                        break;
                    }
                    j += other - 1;
                }
                if (close < 0)
                {
                    i += run;
                    continue;
                }
                int handle = context.OpenRange(i, run, null);
                context.Protect(i + run, close - i - run);
                context.CloseRange(handle, close, run);
                i = close + run;
            }
        }

        static int RunAt(ReadOnlySpan<char> source, int at)
        {
            int end = at;
            while (end < source.Length && source[end] == '`') end++;
            return end - at;
        }
    }

    /// <summary>
    /// Finds http, https, ftp, ftps, file, mailto and tel links and www. names. The text stays visible, and the match is the range's
    /// primary value and slot 1. A link ends as in GitHub's extended autolinks: without trailing punctuation or an unbalanced parenthesis.
    /// </summary>
    [Serializable]
    public class BareUrlMatcher : LetteraMatcher
    {
        static readonly string[] s_Prefixes = { "http://", "https://", "ftp://", "ftps://", "file://", "mailto:", "tel:", "www." };

        public override int Priority => -100;

        public override void Find(LetteraMatchContext context)
        {
            ReadOnlySpan<char> source = context.Text;
            int i = 0;
            while (i < source.Length)
            {
                int length = MatchAt(source, i);
                if (length <= 0 || !context.IsFree(i, length))
                {
                    i++;
                    continue;
                }
                string url = source.Slice(i, length).ToString();
                context.AddRawRange(i, length, new[] { url }, url);
                i += length;
            }
        }

        static int MatchAt(ReadOnlySpan<char> s, int at)
        {
            if (at > 0 && (char.IsLetterOrDigit(s[at - 1]) || s[at - 1] == '_')) return 0;
            foreach (string prefix in s_Prefixes)
            {
                if (at + prefix.Length >= s.Length) continue;
                if (!s.Slice(at, prefix.Length).Equals(prefix.AsSpan(), StringComparison.OrdinalIgnoreCase)) continue;
                int end = at + prefix.Length;
                while (end < s.Length && !char.IsWhiteSpace(s[end]) && s[end] != '<' && s[end] != '>') end++;
                while (end > at + prefix.Length)
                {
                    char last = s[end - 1];
                    if (last == '?' || last == '!' || last == '.' || last == ',' || last == ':' || last == '*' || last == '_' || last == '~' || last == ';' || last == '\'' || last == '"')
                    {
                        end--;
                        continue;
                    }
                    if (last == ')' && !Balanced(s, at, end))
                    {
                        end--;
                        continue;
                    }
                    break;
                }
                return end > at + prefix.Length ? end - at : 0;
            }
            return 0;
        }

        static bool Balanced(ReadOnlySpan<char> s, int start, int end)
        {
            int depth = 0;
            for (int i = start; i < end; i++)
            {
                if (s[i] == '(') depth++;
                else if (s[i] == ')') depth--;
            }
            return depth >= 0;
        }
    }

    /// <summary>Finds a trigger character followed by a word, such as a mention or a hashtag. The word is the primary value; the trigger stays visible.</summary>
    [Serializable]
    public class TriggerWordMatcher : LetteraMatcher
    {
        [SerializeField] string m_Trigger = "@";

        public TriggerWordMatcher() { }
        public TriggerWordMatcher(string trigger) => m_Trigger = trigger;

        public string Trigger { get => m_Trigger; set => m_Trigger = value; }

        public override int Priority => -100;

        static bool IsWordChar(char c) => char.IsLetterOrDigit(c) || c == '_' || char.GetUnicodeCategory(c) == System.Globalization.UnicodeCategory.NonSpacingMark;

        public override void Find(LetteraMatchContext context)
        {
            if (string.IsNullOrEmpty(m_Trigger)) return;
            char trigger = m_Trigger[0];
            ReadOnlySpan<char> source = context.Text;
            for (int i = 0; i < source.Length; i++)
            {
                if (source[i] != trigger) continue;
                if (i > 0 && IsWordChar(source[i - 1])) continue;
                int end = i + 1;
                while (end < source.Length && IsWordChar(source[end])) end++;
                if (end == i + 1 || !context.IsFree(i, end - i)) continue;
                string word = source.Slice(i + 1, end - i - 1).ToString();
                context.AddRawRange(i, end - i, new[] { word }, word);
                i = end - 1;
            }
        }
    }

    /// <summary>Fixed strings. Longer patterns win an overlap. An optional replacement shows in place of what matched.</summary>
    [Serializable]
    public class LiteralMatcher : LetteraMatcher
    {
        [SerializeField] List<string> m_Patterns = new List<string>();
        [SerializeField] string m_Replacement = "";

        public LiteralMatcher() { }

        public LiteralMatcher(params string[] patterns) => m_Patterns.AddRange(patterns);

        public List<string> Patterns => m_Patterns;
        public string Replacement { get => m_Replacement; set => m_Replacement = value; }

        public override LetteraMatcher Clone()
        {
            var copy = (LiteralMatcher)base.Clone();
            copy.m_Patterns = new List<string>(m_Patterns);
            return copy;
        }

        public override void Find(LetteraMatchContext context)
        {
            ReadOnlySpan<char> source = context.Text;
            for (int i = 0; i < source.Length; i++)
            {
                int best = 0;
                foreach (string pattern in m_Patterns)
                {
                    if (string.IsNullOrEmpty(pattern) || pattern.Length <= best || i + pattern.Length > source.Length) continue;
                    if (source.Slice(i, pattern.Length).SequenceEqual(pattern.AsSpan())) best = pattern.Length;
                }
                if (best == 0 || !context.IsFree(i, best)) continue;
                if (string.IsNullOrEmpty(m_Replacement)) context.AddRawRange(i, best, null, null);
                else context.ReplaceAndRange(i, best, m_Replacement, null, null);
                i += best - 1;
            }
        }
    }

    /// <summary>Several matchers under one effect, so one style can react to &lt;i&gt;, &lt;em&gt; and *…*.</summary>
    [Serializable]
    public class MatcherGroup : LetteraMatcher
    {
        [SerializeReference] List<LetteraMatcher> m_Matchers = new List<LetteraMatcher>();

        public MatcherGroup() { }

        public MatcherGroup(params LetteraMatcher[] matchers) => m_Matchers.AddRange(matchers);

        public List<LetteraMatcher> Matchers => m_Matchers;

        public override int Priority
        {
            get
            {
                int highest = int.MinValue;
                foreach (LetteraMatcher matcher in m_Matchers)
                    if (matcher != null && matcher.Priority > highest)
                        highest = matcher.Priority;
                return highest == int.MinValue ? 0 : highest;
            }
        }

        public override LetteraMatcher Clone()
        {
            var copy = (MatcherGroup)base.Clone();
            copy.m_Matchers = new List<LetteraMatcher>(m_Matchers.Count);
            foreach (LetteraMatcher matcher in m_Matchers) copy.m_Matchers.Add(matcher?.Clone());
            return copy;
        }

        public override void Find(LetteraMatchContext context)
        {
            foreach (LetteraMatcher matcher in m_Matchers) matcher?.Find(context);
        }
    }

    /// <summary>A range that opens a URL. It has no look of its own; compose it with color and underline. Slot: 1 URL.</summary>
    [Serializable]
    public class LinkEffect : LetteraRangeEffect
    {
        [Tooltip("Opens the URL when the range is activated. Off leaves that to whoever listens.")]
        [SerializeField] bool m_OpenOnActivate = true;

        public bool OpenOnActivate { get => m_OpenOnActivate; set => m_OpenOnActivate = value; }

        public event Action<string> Followed, PointedAt;
        public event Action PointerLeft;

        public override bool ExtendsOnType => false;

        internal bool HasFollowers => Followed != null || Events.HasActivated;

        public override void OnEvent(LetteraRangeEvent e)
        {
            if (e.Kind == LetteraRangeEventKind.Activated) Followed?.Invoke(e.Primary);
            else if (e.Kind == LetteraRangeEventKind.Entered) PointedAt?.Invoke(e.Primary);
            else if (e.Kind == LetteraRangeEventKind.Exited) PointerLeft?.Invoke();
        }

        public override void OnDefault(LetteraRangeEvent e)
        {
            if (e.Kind == LetteraRangeEventKind.Activated && m_OpenOnActivate) LetteraLinks.Open(e.Primary);
        }
    }

    /// <summary>Does nothing, for a matcher whose ranges only need to exist.</summary>
    [Serializable]
    public class NoEffect : LetteraEffect
    {
        public override void Apply(LetteraAttributes attributes, int start, int end, in LetteraSlots slots) { }
    }
}

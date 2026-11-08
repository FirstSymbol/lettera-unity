using System;
using System.Collections.Generic;

namespace FirstSymbol.Lettera.Editor
{
    /// <summary>One slot of an open tag as written: the span of its token, quotes included, and the value the parser reads from it.</summary>
    internal readonly struct LetteraTagSlot
    {
        public readonly int Start, End;
        public readonly string Value;

        public LetteraTagSlot(int start, int end, string value)
        {
            Start = start;
            End = end;
            Value = value;
        }

        public bool IsEmpty => End == Start;
    }

    /// <summary>An open tag found in a text, with its close tag when there is one. Offsets are into the scanned string.</summary>
    internal sealed class LetteraTagSpan
    {
        public string Name, Label;
        /// <summary>The open tag: from its '&lt;' to past its '&gt;'.</summary>
        public int Start, End;
        /// <summary>Where the name ends, or the label when there is one. A value is written from here.</summary>
        public int HeadEnd;
        /// <summary>The '=' of the value, or -1.</summary>
        public int EqualsAt = -1;
        /// <summary>Where the tokens begin after the '=', and where the last one ends.</summary>
        public int ValueStart, ValueEnd;
        public bool SelfClosing;
        public int CloseStart = -1, CloseEnd = -1;
        public readonly List<LetteraTagSlot> Slots = new List<LetteraTagSlot>(4);

        public bool HasClose => CloseStart >= 0;

        public string OpenText(string source) => source.Substring(Start, End - Start);

        public string Slot(int index) => index >= 0 && index < Slots.Count ? Slots[index].Value : null;

        public string Raw(string source, int index) => index >= 0 && index < Slots.Count ? source.Substring(Slots[index].Start, Slots[index].End - Slots[index].Start) : "";
    }

    /// <summary>What is wrong with markup under the caret that has no '&gt;' to end it, or no quote to end a slot.</summary>
    internal enum LetteraTagProblem { None, NotClosed, QuoteNotClosed }

    /// <summary>
    /// Finds the open tags of a text with the runtime's own tag reader, so quotes and parentheses mean here what they mean there,
    /// and gives the span of every slot so that an edit can leave the rest of the text alone.
    /// </summary>
    internal static class LetteraTagScanner
    {
        static bool IsNameChar(char c) => c < 128 && (char.IsLetterOrDigit(c) || c == '-' || c == '_');

        static string NameAt(string text, int at)
        {
            int i = at;
            while (i < text.Length && IsNameChar(text[i])) i++;
            return i == at ? null : text.Substring(at, i - at);
        }

        /// <summary>Every tag from <paramref name="from"/> to <paramref name="to"/>. A text longer than a window is scanned around the caret by the caller.</summary>
        public static List<LetteraTagSpan> Scan(string text, int from = 0, int to = -1)
        {
            var tags = new List<LetteraTagSpan>();
            if (string.IsNullOrEmpty(text)) return tags;
            if (to < 0 || to > text.Length) to = text.Length;
            from = Math.Max(0, from);
            var open = new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);
            int i = from;
            while (i < to)
            {
                char c = text[i];
                if (c == '\\' && i + 1 < text.Length && text[i + 1] == '<')
                {
                    i += 2;
                    continue;
                }
                if (c != '<')
                {
                    i++;
                    continue;
                }
                if (i + 1 < text.Length && text[i + 1] == '/')
                {
                    string closing = NameAt(text, i + 2);
                    if (closing != null && TagSyntax.MatchClose(text.AsSpan(), i, closing, out int closeLength))
                    {
                        if (open.TryGetValue(closing, out List<int> stack) && stack.Count > 0)
                        {
                            LetteraTagSpan owner = tags[stack[stack.Count - 1]];
                            stack.RemoveAt(stack.Count - 1);
                            owner.CloseStart = i;
                            owner.CloseEnd = i + closeLength;
                        }
                        i += closeLength;
                        continue;
                    }
                    i++;
                    continue;
                }
                string name = NameAt(text, i + 1);
                if (name == null || !TagSyntax.MatchOpen(text.AsSpan(), i, name, out int length, out string[] _, out string label, out bool selfClosing))
                {
                    i++;
                    continue;
                }
                LetteraTagSpan tag = Describe(text, i, name, length, label, selfClosing);
                tags.Add(tag);
                if (name.Equals("noparse", StringComparison.OrdinalIgnoreCase) && !selfClosing)
                {
                    int end = FindNoParseEnd(text, tag.End);
                    if (end < 0)
                    {
                        i = tag.End;
                        continue;
                    }
                    int closeAt = end;
                    TagSyntax.MatchClose(text.AsSpan(), closeAt, "noparse", out int noParseClose);
                    tag.CloseStart = closeAt;
                    tag.CloseEnd = closeAt + noParseClose;
                    i = tag.CloseEnd;
                    continue;
                }
                if (!selfClosing)
                {
                    if (!open.TryGetValue(name, out List<int> list)) open[name] = list = new List<int>();
                    list.Add(tags.Count - 1);
                }
                i += length;
            }
            return tags;
        }

        static int FindNoParseEnd(string text, int from)
        {
            for (int p = text.IndexOf('<', from); p >= 0; p = text.IndexOf('<', p + 1))
                if (TagSyntax.MatchClose(text.AsSpan(), p, "noparse", out _))
                    return p;
            return -1;
        }

        // Where the tag's pieces lie. The parser already decided it is a tag; this only measures it.
        static LetteraTagSpan Describe(string text, int at, string name, int length, string label, bool selfClosing)
        {
            var tag = new LetteraTagSpan { Name = name, Label = label, Start = at, End = at + length, SelfClosing = selfClosing };
            int i = at + 1 + name.Length;
            tag.HeadEnd = i;
            int cursor = i;
            while (cursor < text.Length && text[cursor] == ' ') cursor++;
            if (label != null && cursor < text.Length && text[cursor] == '#')
            {
                cursor++;
                while (cursor < text.Length && IsNameChar(text[cursor])) cursor++;
                tag.HeadEnd = i = cursor;
            }
            while (i < text.Length && text[i] == ' ') i++;
            if (i < text.Length && text[i] == '=')
            {
                tag.EqualsAt = i;
                ReadSlots(text, tag);
            }
            return tag;
        }

        // The same walk as the runtime's slot splitter, keeping the spans.
        static void ReadSlots(string text, LetteraTagSpan tag)
        {
            int i = tag.EqualsAt + 1;
            tag.ValueStart = i;
            int lastEnd = i;
            while (true)
            {
                while (i < text.Length && text[i] == ' ') i++;
                if (i >= text.Length) break;
                char quote = text[i];
                if (quote == '"' || quote == '\'')
                {
                    int close = text.IndexOf(quote, i + 1);
                    if (close < 0) break;
                    tag.Slots.Add(new LetteraTagSlot(i, close + 1, text.Substring(i + 1, close - i - 1)));
                    lastEnd = close + 1;
                    i = close + 1;
                    while (i < text.Length && text[i] == ' ') i++;
                }
                else
                {
                    int start = i;
                    while (i < text.Length && text[i] != ',' && text[i] != '>')
                    {
                        if (text[i] == '/' && i + 1 < text.Length && text[i + 1] == '>') break;
                        if (text[i] == '(')
                        {
                            int after = SkipGroup(text, i);
                            if (after > 0)
                            {
                                i = after;
                                continue;
                            }
                        }
                        i++;
                    }
                    int end = i;
                    while (end > start && char.IsWhiteSpace(text[end - 1])) end--;
                    int begin = start;
                    while (begin < end && char.IsWhiteSpace(text[begin])) begin++;
                    if (begin == end)
                    {
                        // An empty slot sits where its token would begin, after the spaces.
                        tag.Slots.Add(new LetteraTagSlot(start, start, null));
                        lastEnd = start;
                    }
                    else
                    {
                        tag.Slots.Add(new LetteraTagSlot(begin, end, text.Substring(begin, end - begin)));
                        lastEnd = end;
                    }
                }
                if (i < text.Length && text[i] == ',')
                {
                    i++;
                    continue;
                }
                break;
            }
            tag.ValueEnd = lastEnd;
        }

        static int SkipGroup(string text, int open)
        {
            int depth = 0;
            for (int i = open; i < text.Length; i++)
            {
                char c = text[i];
                if (c == '(') depth++;
                else if (c == ')' && --depth == 0) return i + 1;
                else if (c == '>') return -1;
            }
            return -1;
        }

        /// <summary>The tags that hold the caret, outermost first. A caret inside an open or close tag holds that tag; between them it holds it too.</summary>
        public static List<LetteraTagSpan> Enclosing(List<LetteraTagSpan> tags, int caret)
        {
            var found = new List<LetteraTagSpan>(4);
            foreach (LetteraTagSpan tag in tags)
            {
                bool inOpen = caret > tag.Start && caret < tag.End;
                bool between = !tag.SelfClosing && tag.HasClose && caret >= tag.End && caret <= tag.CloseStart;
                bool inClose = tag.HasClose && caret > tag.CloseStart && caret < tag.CloseEnd;
                if (inOpen || between || inClose) found.Add(tag);
            }
            // Tags are in the order they open, so the last of the ones that hold the caret is the innermost.
            return found;
        }

        /// <summary>The slot a caret in an open tag's value sits in, or -1 when it is in the name or outside.</summary>
        public static int SlotAt(LetteraTagSpan tag, int caret)
        {
            if (tag.EqualsAt < 0 || caret <= tag.EqualsAt || caret >= tag.End) return -1;
            for (int i = 0; i < tag.Slots.Count; i++)
                if (caret <= tag.Slots[i].End)
                    return i;
            return tag.Slots.Count - 1;
        }

        /// <summary>Whether the caret sits in a tag that was begun and not ended: no '&gt;', or a quote that never closes.</summary>
        public static LetteraTagProblem Diagnose(string text, int caret, out int start)
        {
            start = -1;
            if (string.IsNullOrEmpty(text)) return LetteraTagProblem.None;
            caret = Math.Max(0, Math.Min(caret, text.Length));
            int p = caret - 1;
            int limit = Math.Max(0, caret - 400);
            for (; p >= limit; p--)
            {
                char c = text[p];
                if (c == '>' || c == '\n') return LetteraTagProblem.None;
                if (c == '<') break;
            }
            if (p < limit || text[p] != '<') return LetteraTagProblem.None;
            if (p > 0 && text[p - 1] == '\\') return LetteraTagProblem.None;
            string name = NameAt(text, p + 1);
            if (name == null) return LetteraTagProblem.None;
            if (TagSyntax.MatchOpen(text.AsSpan(), p, name, out _, out _, out _, out _)) return LetteraTagProblem.None;
            start = p;
            // A quote opened after the '=' with no partner to its right is the problem to name.
            int eq = text.IndexOf('=', p);
            if (eq >= 0 && eq < caret)
            {
                for (int i = eq + 1; i < text.Length; i++)
                {
                    char c = text[i];
                    if (c == '"' || c == '\'')
                    {
                        if (text.IndexOf(c, i + 1) < 0) return LetteraTagProblem.QuoteNotClosed;
                        i = text.IndexOf(c, i + 1);
                    }
                    else if (c == '>' || c == '\n')
                    {
                        break;
                    }
                }
            }
            return LetteraTagProblem.NotClosed;
        }

        /// <summary>The text with the problem mended: a quote closed at the end of its slot, or a '&gt;' put at the end of the line.</summary>
        public static string Mend(string text, int start, LetteraTagProblem problem, out int at, out string inserted)
        {
            at = start;
            inserted = "";
            if (problem == LetteraTagProblem.None || start < 0 || start >= text.Length) return text;
            int eol = text.IndexOf('\n', start);
            if (eol < 0) eol = text.Length;
            if (problem == LetteraTagProblem.QuoteNotClosed)
            {
                int eq = text.IndexOf('=', start);
                int i = eq + 1;
                while (i < eol && text[i] != '"' && text[i] != '\'') i++;
                if (i >= eol) return text;
                char quote = text[i];
                int slotEnd = i + 1;
                while (slotEnd < eol && text[slotEnd] != ',' && text[slotEnd] != '>') slotEnd++;
                at = slotEnd;
                inserted = quote.ToString();
                return text.Insert(at, inserted);
            }
            at = eol;
            inserted = ">";
            return text.Insert(at, inserted);
        }
    }
}

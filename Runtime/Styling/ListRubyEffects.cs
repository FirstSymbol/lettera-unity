using System;
using System.Collections.Generic;
using UnityEngine;

namespace FirstSymbol.Lettera
{
    public enum LetteraListPlacement : byte { Inside, Outside }
    public enum LetteraNumberStyle : byte { Decimal, LowerAlpha, UpperAlpha, LowerRoman, UpperRoman }
    public enum LetteraRubyPosition : byte { Over, Under }
    public enum LetteraRubyAlign : byte { SpaceAround, Center, SpaceBetween, Start }

    internal struct ListItemSpec
    {
        public int Start, End, Level, Number;
        public bool HasNumber, Ordered;
        public string Marker;
        public LetteraListPlacement Placement;
        public List<string> Bullets;
        public List<LetteraNumberStyle> NumberStyles;
    }

    internal struct RubySpec
    {
        public int Start, End;
        public string Reading;
        public LetteraRubyPosition Position;
        public LetteraRubyAlign Align;
        public float Scale;
    }

    /// <summary>
    /// A list item. Slots: 1 level, 2 number (empty counts on), 3 marker text (replaces the bullet or number and does not count), 4 "ordered".
    /// Markers are no part of the text; each item is a paragraph.
    /// </summary>
    [Serializable]
    public class ListEffect : LetteraEffect
    {
        [SerializeField] LetteraListPlacement m_Placement;
        [SerializeField] bool m_Ordered;
        [SerializeField] List<string> m_Bullets = new List<string> { "•", "◦", "▪" };
        [SerializeField] List<LetteraNumberStyle> m_NumberStyles = new List<LetteraNumberStyle> { LetteraNumberStyle.Decimal };

        public LetteraListPlacement Placement { get => m_Placement; set => m_Placement = value; }
        public bool Ordered { get => m_Ordered; set => m_Ordered = value; }
        public List<string> Bullets => m_Bullets;
        public List<LetteraNumberStyle> NumberStyles => m_NumberStyles;

        public override LetteraEffect Clone()
        {
            var copy = (ListEffect)base.Clone();
            copy.m_Bullets = new List<string>(m_Bullets);
            copy.m_NumberStyles = new List<LetteraNumberStyle>(m_NumberStyles);
            return copy;
        }

        public override void Apply(LetteraAttributes attributes, int start, int end, in LetteraSlots slots)
        {
            attributes.AddListItem(new ListItemSpec
            {
                Start = start, End = end,
                Level = Mathf.Max(slots.Int(0, 0), 0),
                HasNumber = LetteraSlots.TryNumber(slots[1], out float number),
                Number = (int)number,
                Marker = slots[2],
                Ordered = m_Ordered || slots[3] == "ordered" || LetteraSlots.TryNumber(slots[1], out _),
                Placement = m_Placement,
                Bullets = m_Bullets,
                NumberStyles = m_NumberStyles,
            });
        }
    }

    /// <summary>&lt;li&gt;...&lt;/li&gt; items. Each opens a new paragraph if the text before it did not end one.</summary>
    [Serializable]
    public class ListItemMatcher : LetteraMatcher
    {
        internal override bool StartsAtMarkup => true;

        [SerializeField] string m_Name = "li";

        public ListItemMatcher() { }
        public ListItemMatcher(string name) => m_Name = name;

        public string Name { get => m_Name; set => m_Name = value; }

        readonly List<int> m_Open = new List<int>();

        public override void Find(LetteraMatchContext context)
        {
            if (string.IsNullOrEmpty(m_Name)) return;
            IReadOnlyList<int> positions = context.LessThanPositions;
            ReadOnlySpan<char> source = context.Text;
            m_Open.Clear();
            for (int p = 0; p < positions.Count; p++)
            {
                int at = positions[p];
                if (!context.IsFree(at, 1)) continue;
                if (TagSyntax.MatchClose(source, at, m_Name, out int closeLength))
                {
                    if (!context.IsFree(at, closeLength)) continue;
                    if (m_Open.Count > 0)
                    {
                        context.CloseRange(m_Open[m_Open.Count - 1], at, closeLength);
                        m_Open.RemoveAt(m_Open.Count - 1);
                    }
                    else
                    {
                        context.Replace(at, closeLength);
                    }
                    continue;
                }
                if (!TagSyntax.MatchOpen(source, at, m_Name, out int length, out string[] slots, out string label, out bool selfClosing, out string raw)) continue;
                if (!context.IsFree(at, length)) continue;
                // An item left open ends where the next one starts.
                if (m_Open.Count > 0)
                {
                    context.CloseRangeAt(m_Open[m_Open.Count - 1], at);
                    m_Open.RemoveAt(m_Open.Count - 1);
                }
                if (at > 0 && source[at - 1] != '\n' && source[at - 1] != '\r') context.InsertAt(at, "\n");
                int handle = context.OpenRange(at, length, slots, label, selfClosing, raw);
                if (!selfClosing) m_Open.Add(handle);
            }
        }
    }

    /// <summary>
    /// &lt;ruby&gt;base&lt;rt&gt;reading&lt;/rt&gt;&lt;/ruby&gt;, one reading per base part, or the shorthand &lt;ruby=reading&gt;base&lt;/ruby&gt;.
    /// &lt;rp&gt; content is removed, as in HTML. A text with no closing &lt;/ruby&gt; stays literal.
    /// </summary>
    [Serializable]
    public class RubyMatcher : LetteraMatcher
    {
        internal override bool StartsAtMarkup => true;

        [SerializeField] string m_Name = "ruby";

        public string Name { get => m_Name; set => m_Name = value; }

        public override void Find(LetteraMatchContext context)
        {
            if (string.IsNullOrEmpty(m_Name)) return;
            IReadOnlyList<int> positions = context.LessThanPositions;
            ReadOnlySpan<char> source = context.Text;
            int p = 0;
            while (p < positions.Count)
            {
                int at = positions[p];
                if (!context.IsFree(at, 1) || !TagSyntax.MatchOpen(source, at, m_Name, out int length, out string[] slots, out _, out bool selfClosing, out _) || selfClosing)
                {
                    p++;
                    continue;
                }
                // The nearest closer: everything up to it belongs to this ruby, so scanning never repeats.
                int close = -1, closeLength = 0, q = p + 1;
                for (; q < positions.Count; q++)
                {
                    if (!TagSyntax.MatchClose(source, positions[q], m_Name, out closeLength)) continue;
                    close = positions[q];
                    break;
                }
                if (close < 0) return;
                if (!context.IsFree(at, length) || !context.IsFree(close, closeLength))
                {
                    p = q + 1;
                    continue;
                }
                context.Replace(at, length);
                ParseBody(context, source, at + length, close, slots != null && slots.Length > 0 ? slots[0] : null, positions, p + 1, q);
                context.Replace(close, closeLength);
                p = q + 1;
            }
        }

        static void ParseBody(LetteraMatchContext context, ReadOnlySpan<char> source, int start, int end, string shorthand, IReadOnlyList<int> positions, int firstTag, int lastTag)
        {
            int baseStart = start;
            bool any = false;
            for (int t = firstTag; t < lastTag; t++)
            {
                int at = positions[t];
                if (at < baseStart || !context.IsFree(at, 1)) continue;
                if (TagSyntax.MatchOpen(source, at, "rp", out int rpLength, out _, out _, out _))
                {
                    int rpEnd = ContentEnd(source, at + rpLength, end, positions, t + 1, lastTag, out int rpSkip);
                    context.Replace(at, rpEnd + rpSkip - at);
                    continue;
                }
                if (!TagSyntax.MatchOpen(source, at, "rt", out int rtLength, out _, out _, out _)) continue;
                int contentStart = at + rtLength;
                int contentEnd = ContentEnd(source, contentStart, end, positions, t + 1, lastTag, out int skip);
                string reading = source.Slice(contentStart, contentEnd - contentStart).ToString();
                if (at > baseStart || any) context.AddSoftRange(baseStart, at - baseStart, new[] { reading });
                context.Replace(at, contentEnd + skip - at);
                baseStart = contentEnd + skip;
                any = true;
            }
            if (!any && !string.IsNullOrEmpty(shorthand)) context.AddSoftRange(start, end - start, new[] { shorthand });
        }

        // A reading ends at its closer, or at the next rt, rp or the ruby's end.
        static int ContentEnd(ReadOnlySpan<char> source, int start, int end, IReadOnlyList<int> positions, int firstTag, int lastTag, out int closerLength)
        {
            closerLength = 0;
            for (int t = firstTag; t < lastTag; t++)
            {
                int at = positions[t];
                if (at < start) continue;
                if (TagSyntax.MatchClose(source, at, "rt", out closerLength) || TagSyntax.MatchClose(source, at, "rp", out closerLength)) return at;
                closerLength = 0;
                if (TagSyntax.MatchOpen(source, at, "rt", out _, out _, out _, out _) || TagSyntax.MatchOpen(source, at, "rp", out _, out _, out _, out _)) return at;
            }
            return end;
        }
    }

    /// <summary>A small reading above or below its base. Slot 1 is the reading the matcher found.</summary>
    [Serializable]
    public class RubyEffect : LetteraEffect
    {
        [SerializeField] LetteraRubyPosition m_Position;
        [SerializeField] LetteraRubyAlign m_Align;
        [SerializeField, Min(0.01f)] float m_Scale = 0.5f;

        public LetteraRubyPosition Position { get => m_Position; set => m_Position = value; }
        public LetteraRubyAlign Align { get => m_Align; set => m_Align = value; }
        public float Scale { get => m_Scale; set => m_Scale = value; }

        public override void Apply(LetteraAttributes attributes, int start, int end, in LetteraSlots slots)
        {
            string reading = slots[0];
            if (string.IsNullOrEmpty(reading) || end <= start) return;
            attributes.AddRuby(new RubySpec { Start = start, End = end, Reading = reading, Position = m_Position, Align = m_Align, Scale = m_Scale });
        }
    }
}

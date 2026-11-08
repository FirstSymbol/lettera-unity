using System;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace FirstSymbol.Lettera.Editor
{
    /// <summary>What a piece of a text's source is, for colouring. Written for the eye: the engine's own parser stays the judge of what a tag does.</summary>
    internal static class LetteraSourceColor
    {
        public enum Kind { Text, Bracket, Name, Unknown, Parameter, Formula }

        public struct Span
        {
            public int Start, Length;
            public Kind Kind;
            public Span(int start, int length, Kind kind)
            {
                Start = start;
                Length = length;
                Kind = kind;
            }
        }

        /// <summary>Longer than this, the plain field: colouring is for reading, and nobody reads this much in an inspector.</summary>
        internal const int MostCharacters = 20000;

        /// <summary>The pieces of a source, in order, each character in exactly one.</summary>
        public static List<Span> Scan(string source, Func<string, bool> known)
        {
            var spans = new List<Span>();
            if (string.IsNullOrEmpty(source)) return spans;
            int at = 0, plain = 0;

            void Flush(int end)
            {
                if (end > plain) spans.Add(new Span(plain, end - plain, Kind.Text));
            }

            while (at < source.Length)
            {
                char c = source[at];
                if (c == '\\' && at + 1 < source.Length)
                {
                    at += 2;
                    continue;
                }
                if (c != '<')
                {
                    at++;
                    continue;
                }
                int close = source.IndexOf('>', at + 1);
                int nextOpen = source.IndexOf('<', at + 1);
                if (close < 0 || (nextOpen >= 0 && nextOpen < close))
                {
                    at++;
                    continue;
                }
                int nameStart = at + 1;
                if (nameStart < close && source[nameStart] == '/') nameStart++;
                int nameEnd = nameStart;
                while (nameEnd < close && (char.IsLetterOrDigit(source[nameEnd]) || source[nameEnd] == '-' || source[nameEnd] == '_')) nameEnd++;
                if (nameEnd == nameStart || !char.IsLetter(source[nameStart]))
                {
                    at++;
                    continue;
                }
                string name = source.Substring(nameStart, nameEnd - nameStart);
                Flush(at);
                spans.Add(new Span(at, nameStart - at, Kind.Bracket));
                spans.Add(new Span(nameStart, nameEnd - nameStart, known(name) ? Kind.Name : Kind.Unknown));
                if (close > nameEnd) spans.Add(new Span(nameEnd, close - nameEnd, Kind.Parameter));
                spans.Add(new Span(close, 1, Kind.Bracket));
                at = plain = close + 1;

                // What is inside these is no markup.
                bool closing = nameStart > 0 && source[nameStart - 1] == '/';
                if (!closing && (name.Equals("math", StringComparison.OrdinalIgnoreCase) || name.Equals("noparse", StringComparison.OrdinalIgnoreCase)))
                {
                    string end = "</" + name;
                    int stop = source.IndexOf(end, at, StringComparison.OrdinalIgnoreCase);
                    if (stop < 0) stop = source.Length;
                    if (stop > at) spans.Add(new Span(at, stop - at, name.Equals("math", StringComparison.OrdinalIgnoreCase) ? Kind.Formula : Kind.Text));
                    at = plain = stop;
                }
            }
            Flush(source.Length);
            return spans;
        }

        /// <summary>Unity's rich text for a source, or null when a source cannot be written safely in it, and the plain field stays.</summary>
        public static string Rich(string source, Func<string, bool> known, bool dark)
        {
            if (string.IsNullOrEmpty(source) || source.Length > MostCharacters) return null;
            if (source.IndexOf("</noparse", StringComparison.OrdinalIgnoreCase) >= 0) return null;
            var text = new StringBuilder(source.Length * 3);
            foreach (Span span in Scan(source, known))
            {
                string color = ColorOf(span.Kind, dark);
                if (color != null) text.Append("<color=").Append(color).Append('>');
                text.Append("<noparse>").Append(source, span.Start, span.Length).Append("</noparse>");
                if (color != null) text.Append("</color>");
            }
            return text.ToString();
        }

        static string ColorOf(Kind kind, bool dark)
        {
            switch (kind)
            {
                case Kind.Bracket: return dark ? "#8A8794" : "#8A8794";
                case Kind.Name: return dark ? "#B79CFF" : "#6D3FF0";
                case Kind.Unknown: return dark ? "#FF8A7A" : "#C7301E";
                case Kind.Parameter: return dark ? "#7CC8E8" : "#0B6E99";
                case Kind.Formula: return dark ? "#6FD6A8" : "#0E7A4C";
                default: return null;
            }
        }
    }

    /// <summary>What a text's markup can say right now: the tags it binds and to which effect.</summary>
    internal static class LetteraVocabulary
    {
        /// <summary>What the engine reads without a matcher that names them.</summary>
        static readonly string[] s_Always = { "br", "noparse", "li", "ruby", "rt", "rp", "divider", "math", "sprite", "obj" };

        /// <summary>All this text's tags.</summary>
        public static HashSet<string> Tags(IEnumerable<LetteraStyle> styles)
        {
            var tags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string always in s_Always) tags.Add(always);
            foreach (LetteraStyle style in styles)
            {
                if (style == null || !style.Enabled) continue;
                if (style.Matcher is TagMatcher tag && !string.IsNullOrEmpty(tag.Name)) tags.Add(tag.Name);
                else if (style.Matcher is VoidTagMatcher mark && !string.IsNullOrEmpty(mark.Name)) tags.Add(mark.Name);
            }
            return tags;
        }

        /// <summary>The tag this text writes for an effect: its own first, as the styles stand in order. Null when none is bound.</summary>
        public static string TagFor(Type effect, IEnumerable<LetteraStyle> styles)
        {
            string inComposite = null;
            foreach (LetteraStyle style in styles)
            {
                if (style == null || !style.Enabled || !(style.Matcher is TagMatcher tag) || !tag.CanWrap || string.IsNullOrEmpty(tag.Name) || style.Effect == null) continue;
                if (style.Effect.GetType() == effect) return tag.Name;
                if (inComposite == null && style.Effect is CompositeEffect group)
                    foreach (LetteraEffect child in group.Children)
                        if (child != null && child.GetType() == effect)
                            inComposite = tag.Name;
            }
            return inComposite;
        }

        /// <summary>Tags a person can write here, without what only the engine's own machinery uses, sorted as they are bound.</summary>
        public static List<string> Writable(IEnumerable<LetteraStyle> styles)
        {
            var list = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (LetteraStyle style in styles)
            {
                if (style == null || !style.Enabled || !(style.Matcher is TagMatcher tag) || string.IsNullOrEmpty(tag.Name)) continue;
                if (seen.Add(tag.Name)) list.Add(tag.Name);
            }
            return list;
        }
    }

    /// <summary>Puts a style on a text for as long as the picker's pointer rests on it. Touches no serialized data, no undo and no dirty flag: the live list only.</summary>
    internal sealed class LetteraStylePreview
    {
        readonly UnityEngine.Object m_Target;
        LetteraStyle m_Shown;

        public LetteraStylePreview(UnityEngine.Object target)
        {
            m_Target = target;
            AssemblyReloadEvents.beforeAssemblyReload += End;
        }

        public bool IsShowing => m_Shown != null;

        public void Show(LetteraStyleTemplate template)
        {
            Clear();
            if (template?.Matcher == null || !(m_Target is ITextHost host) || m_Target == null) return;
            m_Shown = LetteraStyle.ForMatcher(template.Matcher(), template.Effect?.Invoke());
            host.Data.Styles.Add(m_Shown);
            Refresh();
        }

        public void Clear()
        {
            if (m_Shown == null) return;
            if (m_Target != null && m_Target is ITextHost host) host.Data.Styles.Remove(m_Shown);
            m_Shown = null;
            Refresh();
        }

        public void End()
        {
            Clear();
            AssemblyReloadEvents.beforeAssemblyReload -= End;
        }

        void Refresh()
        {
            if (m_Target == null) return;
            (m_Target as ITextComponent)?.Dirty(DirtyLevel.Parse);
            EditorApplication.QueuePlayerLoopUpdate();
            SceneView.RepaintAll();
        }
    }
}

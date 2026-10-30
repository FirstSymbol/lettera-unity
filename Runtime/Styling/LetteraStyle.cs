using System;
using System.Collections.Generic;
using UnityEngine;

namespace FirstSymbol.Lettera
{
    /// <summary>A matcher that finds ranges, and an effect applied to them.</summary>
    [Serializable]
    public class LetteraStyle
    {
        [SerializeReference] LetteraMatcher m_Matcher;
        [SerializeReference] LetteraEffect m_Effect;
        [SerializeField] bool m_Enabled = true;
        [Tooltip("Parameters used when a tag gives none, in the tag's slot syntax.")]
        [SerializeField] string m_DefaultParameter = "";

        [NonSerialized] string[] m_ParsedDefault;
        [NonSerialized] string m_ParsedFrom;

        public LetteraMatcher Matcher { get => m_Matcher; set => m_Matcher = value; }
        public LetteraEffect Effect { get => m_Effect; set => m_Effect = value; }
        public bool Enabled { get => m_Enabled; set => m_Enabled = value; }
        public string DefaultParameter { get => m_DefaultParameter; set => m_DefaultParameter = value; }

        public static LetteraStyle ForTag(LetteraEffect effect, string tagName, string defaultParameter = null) =>
            new LetteraStyle { m_Matcher = new TagMatcher(tagName), m_Effect = effect, m_DefaultParameter = defaultParameter ?? "" };

        public static LetteraStyle ForWholeText(LetteraEffect effect, string parameter = null) =>
            new LetteraStyle { m_Matcher = new WholeTextMatcher(), m_Effect = effect, m_DefaultParameter = parameter ?? "" };

        public static LetteraStyle ForSpan(LetteraEffect effect, int start, int length, string parameter = null) =>
            new LetteraStyle { m_Matcher = new SpanMatcher(start, length), m_Effect = effect, m_DefaultParameter = parameter ?? "" };

        public static LetteraStyle ForMatcher(LetteraMatcher matcher, LetteraEffect effect = null) =>
            new LetteraStyle { m_Matcher = matcher, m_Effect = effect };

        internal string[] ParsedDefault()
        {
            if (m_ParsedFrom == m_DefaultParameter) return m_ParsedDefault;
            m_ParsedFrom = m_DefaultParameter;
            if (string.IsNullOrEmpty(m_DefaultParameter))
            {
                m_ParsedDefault = null;
                return null;
            }
            string[] parts = m_DefaultParameter.Split(',');
            for (int i = 0; i < parts.Length; i++)
            {
                parts[i] = parts[i].Trim();
                if (parts[i].Length == 0) parts[i] = null;
            }
            m_ParsedDefault = parts;
            return parts;
        }
    }

    /// <summary>The vocabulary used until a project assigns its own global style preset.</summary>
    public static class LetteraDefaultStyles
    {
        static List<LetteraStyle> s_Styles;

        /// <summary>The built-in styles: b, i, size, color, alpha, font, lang, u, s, outline, shadow, ellipsis, truncate, dir, br, noparse and escapes.</summary>
        public static List<LetteraStyle> Styles => s_Styles ?? (s_Styles = Create());

        /// <summary>A fresh copy, e.g. to fill a preset asset.</summary>
        public static List<LetteraStyle> Create() => new List<LetteraStyle>
        {
            LetteraStyle.ForMatcher(new NoParseMatcher()),
            LetteraStyle.ForMatcher(new EscapeMatcher()),
            LetteraStyle.ForMatcher(new LineBreakMatcher()),
            LetteraStyle.ForTag(new BoldEffect(), "b"),
            LetteraStyle.ForTag(new BoldEffect(), "strong"),
            LetteraStyle.ForTag(new ItalicEffect(), "i"),
            LetteraStyle.ForTag(new ItalicEffect(), "em"),
            LetteraStyle.ForTag(new SizeEffect(), "size"),
            LetteraStyle.ForTag(new ColorEffect(), "color"),
            LetteraStyle.ForTag(new AlphaEffect(), "alpha"),
            LetteraStyle.ForTag(new FontEffect(), "font"),
            LetteraStyle.ForTag(new LanguageEffect(), "lang"),
            LetteraStyle.ForTag(new UnderlineEffect(), "u"),
            LetteraStyle.ForTag(new StrikethroughEffect(), "s"),
            LetteraStyle.ForTag(new StrikethroughEffect(), "strikethrough"),
            LetteraStyle.ForTag(new OutlineEffect(), "outline"),
            LetteraStyle.ForTag(new ShadowEffect(), "shadow"),
            LetteraStyle.ForTag(new EllipsisEffect(), "ellipsis"),
            LetteraStyle.ForTag(new TruncateEffect(), "truncate"),
            LetteraStyle.ForTag(new DirectionEffect(), "dir"),
        };
    }
}

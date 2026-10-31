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

        /// <summary>A deep copy, so changing it affects only its owner.</summary>
        internal LetteraStyle Clone() => new LetteraStyle
        {
            m_Matcher = m_Matcher?.Clone(),
            m_Effect = m_Effect?.Clone(),
            m_Enabled = m_Enabled,
            m_DefaultParameter = m_DefaultParameter,
        };

        internal string[] ParsedDefault()
        {
            if (m_ParsedFrom == m_DefaultParameter) return m_ParsedDefault;
            m_ParsedFrom = m_DefaultParameter;
            m_ParsedDefault = TagSyntax.SplitSlots(m_DefaultParameter);
            return m_ParsedDefault;
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
            LetteraStyle.ForTag(new StrokeEffect(), "stroke"),
            LetteraStyle.ForTag(new FillEffect(), "fill"),
            LetteraStyle.ForTag(new GlowEffect(), "glow"),
            LetteraStyle.ForTag(new InnerShadowEffect(), "inner-shadow"),
            LetteraStyle.ForTag(new HighlightEffect(), "mark"),
            LetteraStyle.ForTag(new ExtrudeEffect(), "extrude"),
            LetteraStyle.ForTag(new FilterEffect(), "filter"),
            LetteraStyle.ForTag(new UnionEffect(), "union"),
            LetteraStyle.ForTag(new WarpEffect(), "warp"),
            LetteraStyle.ForTag(new CurveEffect(), "curve"),
            LetteraStyle.ForTag(new UppercaseEffect(), "uppercase"),
            LetteraStyle.ForTag(new LowercaseEffect(), "lowercase"),
            LetteraStyle.ForTag(new SmallCapsEffect(), "smallcaps"),
            LetteraStyle.ForTag(new SuperSubEffect(), "sup", "super"),
            LetteraStyle.ForTag(new SuperSubEffect(), "sub", "sub"),
            LetteraStyle.ForTag(new OpenTypeEffect(), "otf"),
            LetteraStyle.ForTag(new AxesEffect(), "axes"),
            LetteraStyle.ForTag(new SizeMatchEffect(), "size-match"),
            LetteraStyle.ForMatcher(new DividerMatcher(), new DividerEffect()),
            LetteraStyle.ForMatcher(new ListItemMatcher(), new ListEffect()),
            LetteraStyle.ForMatcher(new RubyMatcher(), new RubyEffect()),
            LetteraStyle.ForTag(new LinkEffect(), "link"),
            LetteraStyle.ForTag(new SpoilerEffect(), "spoiler"),
            LetteraStyle.ForMatcher(new InlineMediaMatcher("sprite"), new InlineEffect()),
            LetteraStyle.ForMatcher(new InlineMediaMatcher("obj"), new InlineEffect()),
            LetteraStyle.ForTag(new CompositeEffect(new LinkEffect(), new ColorEffect { Color = new Color32(77, 163, 255, 255) }, new UnderlineEffect()), "a"),
            LetteraStyle.ForTag(new WaveMotion(), "wave"),
            LetteraStyle.ForTag(new WobbleMotion(), "wobble"),
            LetteraStyle.ForTag(new ShakeMotion(), "shake"),
            LetteraStyle.ForTag(new BounceMotion(), "bounce"),
            LetteraStyle.ForTag(new FloatMotion(), "float"),
            LetteraStyle.ForTag(new SpinMotion(), "spin"),
            LetteraStyle.ForTag(new PulseMotion(), "pulse"),
            LetteraStyle.ForTag(new PendulumMotion(), "pendulum"),
            LetteraStyle.ForTag(new GlitchMotion(), "glitch"),
            LetteraStyle.ForTag(new ScrambleMotion(), "scramble"),
            LetteraStyle.ForTag(new RollMotion(), "roll"),
            LetteraStyle.ForTag(new DeformMotion(), "deform"),
            LetteraStyle.ForTag(new RevealEffect(), "reveal"),
            LetteraStyle.ForTag(new TrackingEffect(), "tracking"),
            LetteraStyle.ForTag(new WordSpacingEffect(), "word-spacing"),
            LetteraStyle.ForTag(new CellWidthEffect(), "cell"),
            LetteraStyle.ForTag(new LineHeightEffect(), "line-height"),
            LetteraStyle.ForTag(new ParagraphSpacingEffect(), "para-spacing"),
            LetteraStyle.ForTag(new LineGapEffect(), "line-gap"),
            LetteraStyle.ForTag(new NoBreakEffect(), "nobr"),
            LetteraStyle.ForTag(new IndentEffect(), "indent"),
            LetteraStyle.ForTag(new AlignEffect(), "align"),
            LetteraStyle.ForTag(new BoxTrimEffect(), "trim"),
            LetteraStyle.ForTag(new ShadowEffect(), "shadow"),
            LetteraStyle.ForTag(new EllipsisEffect(), "ellipsis"),
            LetteraStyle.ForTag(new TruncateEffect(), "truncate"),
            LetteraStyle.ForTag(new DirectionEffect(), "dir"),
        };
    }
}

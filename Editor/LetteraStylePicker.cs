using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace FirstSymbol.Lettera.Editor
{
    /// <summary>A way to start a style: how it is driven, what it does, and the matcher and effect it begins with.</summary>
    internal sealed class LetteraStyleTemplate
    {
        public string Driven, Kind, Name, Keywords;
        public Func<LetteraMatcher> Matcher;
        public Func<LetteraEffect> Effect;
    }

    /// <summary>Every template, grouped first by how a style is driven, then by kind.</summary>
    internal static class LetteraStyleTemplates
    {
        public const string WholeText = "Whole Text", Tags = "Tags", InlineTags = "Inline Tags", Markdown = "Markdown", AutoDetect = "Auto-detect", Protection = "Protection";
        public const string Basics = "Basics", Type = "Type", Lines = "Lines", Look = "Look", Layout = "Layout", Links = "Links", Utility = "Utility", Custom = "Custom";

        static List<LetteraStyleTemplate> s_All;

        static readonly (string kind, string name, string tag, Func<LetteraEffect> effect)[] s_Effects =
        {
            (Basics, "Color", "color", () => new ColorEffect()),
            (Basics, "Size", "size", () => new SizeEffect()),
            (Basics, "Bold", "b", () => new BoldEffect()),
            (Basics, "Italic", "i", () => new ItalicEffect()),
            (Basics, "Alpha", "alpha", () => new AlphaEffect()),
            (Basics, "Font", "font", () => new FontEffect()),
            (Type, "Uppercase", "uppercase", () => new UppercaseEffect()),
            (Type, "Lowercase", "lowercase", () => new LowercaseEffect()),
            (Type, "Small Caps", "smallcaps", () => new SmallCapsEffect()),
            (Type, "Superscript", "sup", () => new SuperSubEffect { Placement = LetteraSuperSubPlacement.Super }),
            (Type, "Subscript", "sub", () => new SuperSubEffect { Placement = LetteraSuperSubPlacement.Sub }),
            (Type, "OpenType Features", "otf", () => new OpenTypeEffect()),
            (Type, "Variable Axes", "axes", () => new AxesEffect()),
            (Type, "Language", "lang", () => new LanguageEffect()),
            (Type, "Resolution Boost", "boost", () => new ResolutionEffect()),
            (Type, "Size Match", "size-match", () => new SizeMatchEffect()),
            (Lines, "Tracking", "tracking", () => new TrackingEffect()),
            (Lines, "Word Spacing", "word-spacing", () => new WordSpacingEffect()),
            (Lines, "Cell Width", "cell", () => new CellWidthEffect()),
            (Lines, "Line Height", "line-height", () => new LineHeightEffect()),
            (Lines, "Paragraph Spacing", "para-spacing", () => new ParagraphSpacingEffect()),
            (Lines, "Line Gap", "line-gap", () => new LineGapEffect()),
            (Lines, "No Break", "nobr", () => new NoBreakEffect()),
            (Look, "Fill", "fill", () => new FillEffect()),
            (Look, "Gradient Fill", "gradient", NewGradientFill),
            (Look, "Stroke", "stroke", () => new StrokeEffect()),
            (Look, "Outline", "outline", () => new OutlineEffect()),
            (Look, "Shadow", "shadow", () => new ShadowEffect()),
            (Look, "Glow", "glow", () => new GlowEffect()),
            (Look, "Inner Shadow", "inner-shadow", () => new InnerShadowEffect()),
            (Look, "Extrude", "extrude", () => new ExtrudeEffect()),
            (Look, "Color Filter", "filter", () => new FilterEffect(new GrayscaleFilter())),
            (Look, "Union", "union", () => new UnionEffect()),
            (Look, "Warp", "warp", () => new WarpEffect()),
            (Look, "Underline", "u", () => new UnderlineEffect()),
            (Look, "Strikethrough", "s", () => new StrikethroughEffect()),
            (Look, "Highlight", "mark", () => new HighlightEffect()),
            (Look, "Custom Material", "material", () => new MaterialEffect()),
            (Look, "Paint Order", "order", () => new PaintOrderEffect()),
            (Layout, "Align", "align", () => new AlignEffect()),
            (Layout, "Indent", "indent", () => new IndentEffect()),
            (Layout, "Curve", "curve", () => new CurveEffect()),
            (Layout, "Text Box Trim", "trim", () => new BoxTrimEffect()),
            (Layout, "Ellipsis", "ellipsis", () => new EllipsisEffect()),
            (Layout, "Truncate", "truncate", () => new TruncateEffect()),
            (Layout, "Direction", "dir", () => new DirectionEffect()),
            (Links, "Link", "link", () => new LinkEffect()),
            (Links, "Link With Look", "a", () => new CompositeEffect(new LinkEffect(), new ColorEffect { Color = new Color32(77, 163, 255, 255) }, new UnderlineEffect())),
            (Utility, "Composite", "group", () => new CompositeEffect()),
            (Utility, "Effect Preset", "preset", () => new PresetEffect()),
            (Utility, "No Effect", "mark-only", () => new NoEffect()),
        };

        static LetteraEffect NewGradientFill()
        {
            var fill = new FillEffect();
            fill.SetPaint(LetteraPaint.Of(LetteraGradient.Of(new Color32(255, 200, 40, 255), new Color32(240, 40, 120, 255)), LetteraPaintSpan.Range));
            return fill;
        }

        public static IReadOnlyList<LetteraStyleTemplate> All => s_All ?? (s_All = Build());

        static List<LetteraStyleTemplate> Build()
        {
            var all = new List<LetteraStyleTemplate>();
            var known = new HashSet<Type>();
            foreach ((string kind, string name, string tag, Func<LetteraEffect> effect) in s_Effects)
            {
                known.Add(effect().GetType());
                all.Add(new LetteraStyleTemplate { Driven = WholeText, Kind = kind, Name = name, Matcher = () => new WholeTextMatcher(), Effect = effect });
                string tagName = tag;
                all.Add(new LetteraStyleTemplate { Driven = Tags, Kind = kind, Name = name, Keywords = "<" + tag + ">", Matcher = () => new TagMatcher(tagName), Effect = effect });
            }
            foreach (Type type in TypeCache.GetTypesDerivedFrom<LetteraEffect>().Where(t => !t.IsAbstract && !known.Contains(t) && !t.IsNested && t.GetConstructor(System.Type.EmptyTypes) != null).OrderBy(t => t.Name))
            {
                Type effectType = type;
                string name = LetteraStyleList.Pretty(type.Name, "Effect");
                string tag = name.ToLowerInvariant();
                if (effectType == typeof(ListEffect) || effectType == typeof(RubyEffect) || effectType == typeof(DividerEffect)) continue;
                all.Add(new LetteraStyleTemplate { Driven = WholeText, Kind = Custom, Name = name, Matcher = () => new WholeTextMatcher(), Effect = () => (LetteraEffect)Activator.CreateInstance(effectType) });
                all.Add(new LetteraStyleTemplate { Driven = Tags, Kind = Custom, Name = name, Keywords = "<" + tag + ">", Matcher = () => new TagMatcher(tag), Effect = () => (LetteraEffect)Activator.CreateInstance(effectType) });
            }

            void Add(string driven, string kind, string name, string keywords, Func<LetteraMatcher> matcher, Func<LetteraEffect> effect) =>
                all.Add(new LetteraStyleTemplate { Driven = driven, Kind = kind, Name = name, Keywords = keywords, Matcher = matcher, Effect = effect });

            Add(InlineTags, Layout, "Divider", "<divider>", () => new DividerMatcher(), () => new DividerEffect());
            Add(InlineTags, Lines, "List Item", "<li>", () => new ListItemMatcher(), () => new ListEffect());
            Add(InlineTags, Type, "Ruby", "<ruby> <rt>", () => new RubyMatcher(), () => new RubyEffect());
            Add(InlineTags, Utility, "Void Tag", "<x>", () => new VoidTagMatcher("x"), () => new NoEffect());
            Add(InlineTags, Lines, "Line Break", "<br>", () => new LineBreakMatcher(), null);

            Add(Markdown, Basics, "Bold", "**", () => new MarkdownMarkerMatcher("**"), () => new BoldEffect());
            Add(Markdown, Basics, "Italic", "*", () => new MarkdownMarkerMatcher("*"), () => new ItalicEffect());
            Add(Markdown, Basics, "Italic", "_", () => new MarkdownMarkerMatcher("_"), () => new ItalicEffect());
            Add(Markdown, Look, "Strikethrough", "~~", () => new MarkdownMarkerMatcher("~~"), () => new StrikethroughEffect());
            Add(Markdown, Look, "Underline", "++", () => new MarkdownMarkerMatcher("++"), () => new UnderlineEffect());
            Add(Markdown, Lines, "List", "- 1.", () => new MarkdownListMatcher(), () => new ListEffect());
            Add(Markdown, Links, "Link", "[text](url)", () => new MarkdownLinkMatcher(), () => new CompositeEffect(new LinkEffect(), new ColorEffect { Color = new Color32(77, 163, 255, 255) }, new UnderlineEffect()));
            Add(Markdown, Utility, "Code Span", "`code`", () => new CodeSpanMatcher(), () => new NoEffect());

            Add(AutoDetect, Links, "Bare URL", "https www", () => new BareUrlMatcher(), () => new CompositeEffect(new LinkEffect(), new ColorEffect { Color = new Color32(77, 163, 255, 255) }, new UnderlineEffect()));
            Add(AutoDetect, Links, "Mention", "@name", () => new TriggerWordMatcher("@"), () => new ColorEffect { Color = new Color32(77, 163, 255, 255) });
            Add(AutoDetect, Links, "Hashtag", "#tag", () => new TriggerWordMatcher("#"), () => new ColorEffect { Color = new Color32(77, 163, 255, 255) });
            Add(AutoDetect, Utility, "Literal Strings", "words", () => new LiteralMatcher(), () => new ColorEffect());

            Add(Protection, Utility, "No Parse", "<noparse>", () => new NoParseMatcher(), null);
            Add(Protection, Utility, "Escape", "\\<", () => new EscapeMatcher(), null);
            Add(Protection, Utility, "Code Span", "`code`", () => new CodeSpanMatcher(), () => new NoEffect());
            return all;
        }

        /// <summary>The templates whose name, group or keywords contain a search, in order.</summary>
        public static List<LetteraStyleTemplate> Find(string search)
        {
            if (string.IsNullOrWhiteSpace(search)) return All.ToList();
            return All.Where(t => Matches(t.Name, search) || Matches(t.Kind, search) || Matches(t.Driven, search) || Matches(t.Keywords, search)).ToList();
        }

        static bool Matches(string text, string search) => text != null && text.IndexOf(search.Trim(), StringComparison.OrdinalIgnoreCase) >= 0;
    }

    /// <summary>A small searchable window of style templates.</summary>
    internal sealed class LetteraStylePicker : EditorWindow
    {
        Action<LetteraStyleTemplate> m_Chosen;
        ScrollView m_List;

        public static void Open(Rect anchor, Action<LetteraStyleTemplate> chosen)
        {
            var window = CreateInstance<LetteraStylePicker>();
            window.m_Chosen = chosen;
            window.ShowAsDropDown(GUIUtility.GUIToScreenRect(anchor), new Vector2(Mathf.Max(anchor.width, 300), 420));
        }

        void CreateGUI()
        {
            VisualElement root = LtUI.Root();
            root.AddToClassList("lt-picker");
            root.style.flexGrow = 1;
            var search = new TextField { value = "" };
            search.AddToClassList("lt-picker__search");
            m_List = new ScrollView();
            m_List.style.flexGrow = 1;
            root.Add(search);
            root.Add(m_List);
            rootVisualElement.Add(root);
            search.RegisterValueChangedCallback(e => Fill(e.newValue));
            Fill("");
            search.schedule.Execute(() => search.Focus());
        }

        void Fill(string text)
        {
            m_List.Clear();
            string driven = null, kind = null;
            foreach (LetteraStyleTemplate template in LetteraStyleTemplates.Find(text))
            {
                if (template.Driven != driven)
                {
                    driven = template.Driven;
                    kind = null;
                    m_List.Add(LtUI.Text(driven.ToUpperInvariant(), "lt-picker__group"));
                }
                if (template.Kind != kind)
                {
                    kind = template.Kind;
                    m_List.Add(LtUI.Text(kind, "lt-picker__kind"));
                }
                LetteraStyleTemplate chosen = template;
                VisualElement item = LtUI.El("lt-picker__item", LtUI.Text(template.Name, "lt-picker__name"), LtUI.Text(template.Keywords ?? "", "lt-picker__keys"));
                item.RegisterCallback<ClickEvent>(_ =>
                {
                    m_Chosen?.Invoke(chosen);
                    Close();
                });
                m_List.Add(item);
            }
        }
    }
}

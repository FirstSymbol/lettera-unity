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

        public const string Animation = "Animation", Interactive = "Interactive", Inline = "Inline", Semantic = "Semantic";

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
                var home = (LetteraPickerAttribute)Attribute.GetCustomAttribute(effectType, typeof(LetteraPickerAttribute));
                string customKind = home != null && !string.IsNullOrEmpty(home.Group) ? home.Group : Custom;
                string words = home?.Words;
                all.Add(new LetteraStyleTemplate { Driven = WholeText, Kind = customKind, Name = name, Keywords = words, Matcher = () => new WholeTextMatcher(), Effect = () => (LetteraEffect)Activator.CreateInstance(effectType) });
                all.Add(new LetteraStyleTemplate { Driven = Tags, Kind = customKind, Name = name, Keywords = "<" + tag + "> " + words, Matcher = () => new TagMatcher(tag), Effect = () => (LetteraEffect)Activator.CreateInstance(effectType) });
            }

            void Add(string driven, string kind, string name, string keywords, Func<LetteraMatcher> matcher, Func<LetteraEffect> effect) =>
                all.Add(new LetteraStyleTemplate { Driven = driven, Kind = kind, Name = name, Keywords = keywords, Matcher = matcher, Effect = effect });

            // One a tag or the whole text can drive, found also by the words people look for it under.
            void Moving(string kind, string name, string tag, string words, Func<LetteraEffect> effect)
            {
                all.RemoveAll(t => t.Kind == Custom && t.Effect != null && t.Effect().GetType() == effect().GetType());
                Add(WholeText, kind, name, words, () => new WholeTextMatcher(), effect);
                Add(Tags, kind, name, "<" + tag + "> " + words, () => new TagMatcher(tag), effect);
            }

            Moving(Animation, "Wave", "wave", "sine ripple", () => new WaveMotion());
            Moving(Animation, "Wobble", "wobble", "jelly", () => new WobbleMotion());
            Moving(Animation, "Shake", "shake", "jitter tremble", () => new ShakeMotion());
            Moving(Animation, "Bounce", "bounce", "hop", () => new BounceMotion());
            Moving(Animation, "Float", "float", "drift hover", () => new FloatMotion());
            Moving(Animation, "Spin", "spin", "rotate", () => new SpinMotion());
            Moving(Animation, "Pulse", "pulse", "breathe scale", () => new PulseMotion());
            Moving(Animation, "Pendulum", "pendulum", "swing", () => new PendulumMotion());
            Moving(Animation, "Deform", "deform", "warp bend", () => new DeformMotion());
            Moving(Animation, "Glitch", "glitch", "rgb split", () => new GlitchMotion());
            Moving(Animation, "Scramble", "scramble", "decode random", () => new ScrambleMotion());
            Moving(Animation, "Roll", "roll", "odometer counter slot", () => new RollMotion());
            Moving(Animation, "Reveal", "reveal", "typewriter appear hide", () => new RevealEffect());
            Moving(Animation, "Marquee", "marquee", "ticker scroll crawl", () => new MarqueeEffect());
            Moving(Interactive, "Action", "action", "click button event", () => new ActionEffect());
            Moving(Interactive, "Spoiler", "spoiler", "hidden cover secret", () => new SpoilerEffect());
            Moving(Interactive, "Range Tint", "tint", "hover feedback", () => new RangeTintEffect());
            Moving(Semantic, "Semantic", "semantic", "accessibility screen reader role", () => new SemanticEffect());
            all.RemoveAll(t => t.Kind == Custom && t.Effect != null && t.Effect() is InlineEffect);
            Add(InlineTags, Inline, "Sprite", "<sprite> image icon emoji", () => new InlineMediaMatcher("sprite"), () => new InlineEffect());
            Add(InlineTags, Inline, "Object", "<obj> prefab widget", () => new InlineMediaMatcher("obj"), () => new InlineEffect());

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
            Add(AutoDetect, Links, "Mention", "@name", () => new TriggerWordMatcher("@"), () => new CompositeEffect(new ActionEffect(), new ColorEffect { Color = new Color32(77, 163, 255, 255) }));
            Add(AutoDetect, Links, "Hashtag", "#tag", () => new TriggerWordMatcher("#"), () => new CompositeEffect(new ActionEffect(), new ColorEffect { Color = new Color32(77, 163, 255, 255) }));
            Add(AutoDetect, Utility, "Literal Strings", "words", () => new LiteralMatcher(), () => new ColorEffect());

            Add(Protection, Utility, "No Parse", "<noparse>", () => new NoParseMatcher(), null);
            Add(Protection, Utility, "Escape", "\\<", () => new EscapeMatcher(), null);
            Add(Protection, Utility, "Code Span", "`code`", () => new CodeSpanMatcher(), () => new NoEffect());
            return all;
        }

        /// <summary>The templates a search fits, in their order: by name, group, keywords or what they do, in either keyboard layout.</summary>
        public static List<LetteraStyleTemplate> Find(string search)
        {
            if (string.IsNullOrWhiteSpace(search)) return All.ToList();
            return All.Where(t => Fits(search, t)).ToList();
        }

        static bool Fits(string search, LetteraStyleTemplate t) =>
            LtSearchText.Score(search, t.Name) > 0
            || Contained(search, t.Kind) || Contained(search, t.Driven) || Contained(search, t.Keywords) || Contained(search, WordOf(t.Name));

        // In the longer texts, letters scattered about prove nothing: a word has to be there.
        static bool Contained(string search, string text) => !string.IsNullOrEmpty(text) && LtSearchText.Score(search, text) >= 30;

        // A line in words for each name. Tags and whole text share it: the example says how each is written.
        static readonly Dictionary<string, string> s_Words = new Dictionary<string, string>
        {
            { "Color", "Paints letters in one colour." },
            { "Size", "Changes the size against the text around it." },
            { "Bold", "Heavier letters, from the family or drawn thicker." },
            { "Italic", "Slanted letters, from the family or leaned over." },
            { "Alpha", "Makes letters more transparent." },
            { "Font", "Another family for this part." },
            { "Uppercase", "Shows capitals without changing what is written." },
            { "Lowercase", "Shows small letters without changing what is written." },
            { "Small Caps", "Small capitals in place of small letters." },
            { "Superscript", "Raised and smaller, as in x squared." },
            { "Subscript", "Lowered and smaller, as in H2O." },
            { "OpenType Features", "Turns a font's own features on or off, such as ligatures." },
            { "Variable Axes", "Sets a variable font's weight, width and other axes." },
            { "Language", "Tells shaping which language this part is in." },
            { "Resolution Boost", "A sharper atlas for large or zoomed text." },
            { "Size Match", "Evens out sizes between fonts by their x-height." },
            { "Tracking", "Space between letters." },
            { "Word Spacing", "Space between words." },
            { "Cell Width", "Every letter in a cell of the same width, as in a table of numbers." },
            { "Line Height", "The height of the lines in this part." },
            { "Paragraph Spacing", "Space before and after a paragraph." },
            { "Line Gap", "Space added between lines." },
            { "No Break", "Keeps this part on one line." },
            { "Fill", "Paints letters with a colour, gradient or texture." },
            { "Gradient Fill", "A gradient across this part." },
            { "Stroke", "A line around each letter, centred on its edge." },
            { "Outline", "A line outside each letter." },
            { "Shadow", "A soft copy behind the text." },
            { "Glow", "A soft light around the letters." },
            { "Inner Shadow", "A shadow inside the letters." },
            { "Extrude", "Fake depth, by copies drawn behind." },
            { "Color Filter", "Grayscale, sepia and other filters over the letters." },
            { "Union", "Merges overlapping layers of letters into one." },
            { "Warp", "Bends the letters by a wave." },
            { "Underline", "A line under the text." },
            { "Strikethrough", "A line through the text." },
            { "Highlight", "A marker behind the text." },
            { "Custom Material", "Draws this part with your own material." },
            { "Paint Order", "Which layer of the text goes on top." },
            { "Align", "Aligns this paragraph apart from the rest." },
            { "Indent", "Moves lines in from the edge." },
            { "Curve", "Lays the text along an arc." },
            { "Text Box Trim", "Cuts the space above the first line and below the last." },
            { "Ellipsis", "Marks where text may be cut with three dots." },
            { "Truncate", "Marks where text may be cut." },
            { "Direction", "Left to right or right to left for this part." },
            { "Link", "Makes this part a link." },
            { "Link With Look", "A link in a link's colour, underlined." },
            { "Composite", "Several effects under one tag." },
            { "Effect Preset", "Effects kept in an asset, shared by many." },
            { "No Effect", "Finds the text and changes nothing: for scripts to read." },
            { "Wave", "Letters rise and fall in a wave." },
            { "Wobble", "Letters shake softly, like jelly." },
            { "Shake", "Letters tremble." },
            { "Bounce", "Letters hop." },
            { "Float", "Letters drift up and down." },
            { "Spin", "Letters turn round." },
            { "Pulse", "Letters grow and shrink." },
            { "Pendulum", "Letters swing." },
            { "Deform", "Bends the letters over time." },
            { "Glitch", "Colour channels split and jump." },
            { "Scramble", "Random letters settle into the text." },
            { "Roll", "Letters roll into place like a counter." },
            { "Reveal", "Letters appear one by one, as a typewriter." },
            { "Marquee", "The text scrolls along, as a ticker." },
            { "Action", "A click on this part calls your code." },
            { "Spoiler", "Hidden under a cover until clicked." },
            { "Range Tint", "Changes colour under the pointer." },
            { "Semantic", "Tells screen readers what this part is." },
            { "Sprite", "An image inside the text." },
            { "Object", "A prefab inside the text." },
            { "Divider", "A line across the text." },
            { "List Item", "A list row with its marker." },
            { "Ruby", "Small reading notes over the text." },
            { "Void Tag", "A tag that stands alone, for scripts to find." },
            { "Line Break", "A new line within a paragraph." },
            { "List", "Lines that start with - or 1. become a list." },
            { "Code Span", "What is between backticks stays as written." },
            { "Bare URL", "Web addresses become links by themselves." },
            { "Mention", "Words after @ become clickable." },
            { "Hashtag", "Words after # become clickable." },
            { "Literal Strings", "Words you list get this style wherever they are." },
            { "No Parse", "What is inside is shown as written." },
            { "Escape", "A backslash shows the next sign as written." },
        };

        static string WordOf(string name) => s_Words.TryGetValue(name, out string words) ? words : null;

        /// <summary>The line an entry shows: what it does, and how it is written.</summary>
        public static string WordsOf(LetteraStyleTemplate t, bool applied)
        {
            string words = WordOf(t.Name) ?? "Your own effect.";
            if (applied) return words + " On now: a click takes it away.";
            if (t.Driven == WholeText) return words + " For all of this text.";
            return words;
        }

        static readonly Dictionary<string, Color> s_KindColors = new Dictionary<string, Color>
        {
            { Basics, new Color32(120, 170, 255, 255) },
            { Type, new Color32(90, 200, 150, 255) },
            { Lines, new Color32(80, 195, 205, 255) },
            { Look, new Color32(165, 125, 255, 255) },
            { Layout, new Color32(235, 180, 80, 255) },
            { Links, new Color32(95, 175, 240, 255) },
            { Utility, new Color32(150, 150, 160, 255) },
            { Custom, new Color32(150, 150, 160, 255) },
            { Animation, new Color32(240, 120, 175, 255) },
            { Interactive, new Color32(245, 150, 90, 255) },
            { Inline, new Color32(190, 170, 120, 255) },
            { Semantic, new Color32(140, 200, 110, 255) },
        };

        public static Color ColorOf(LetteraStyleTemplate t) => s_KindColors.TryGetValue(t.Kind, out Color color) ? color : s_KindColors[Utility];

        static Dictionary<System.Type, Color> s_EffectColors;

        /// <summary>A small mark of what family an effect is of.</summary>
        public static Color ColorOfEffect(System.Type effect)
        {
            if (s_EffectColors == null)
            {
                s_EffectColors = new Dictionary<System.Type, Color>();
                foreach (LetteraStyleTemplate t in All)
                {
                    if (t.Effect == null) continue;
                    System.Type type = t.Effect().GetType();
                    if (!s_EffectColors.ContainsKey(type)) s_EffectColors[type] = ColorOf(t);
                }
            }
            return s_EffectColors.TryGetValue(effect, out Color color) ? color : s_KindColors[Utility];
        }
    }

    /// <summary>What a picker asks of whoever opened it. All optional.</summary>
    internal sealed class LetteraPickerHooks
    {
        public Func<LetteraStyleTemplate, bool> Applied;
        public Action<LetteraStyleTemplate> Remove;
        public Action<LetteraStyleTemplate> Hover;
        public Action HoverEnd;
        public Action Closed;
    }

    /// <summary>The picker's page: groups by how a style is driven, each entry with its colour, a line in words and an example. Keys: arrows, Enter, Esc, Tab.</summary>
    internal sealed class LetteraStylePickerView : VisualElement
    {
        const string RecentKey = "Lettera.Picker.Recent";
        const int MostRecent = 6;

        readonly Action<LetteraStyleTemplate> m_Chosen;
        readonly LetteraPickerHooks m_Hooks;
        readonly ScrollView m_List = new ScrollView();
        readonly List<(LetteraStyleTemplate template, VisualElement row)> m_Rows = new List<(LetteraStyleTemplate, VisualElement)>();
        readonly HashSet<string> m_OpenGroups = new HashSet<string>();
        readonly Dictionary<string, VisualElement> m_GroupHeads = new Dictionary<string, VisualElement>();
        readonly IVisualElementScheduledItem m_HoverTimer;
        LetteraStyleTemplate m_Hovered;
        int m_At = -1;
        string m_Query = "";

        public LtSearch Search { get; }
        internal IEnumerable<LetteraStyleTemplate> Shown => m_Rows.Select(r => r.template);
        internal LetteraStyleTemplate Current => m_At >= 0 && m_At < m_Rows.Count ? m_Rows[m_At].template : null;
        internal bool IsGroupOpen(string group) => m_OpenGroups.Contains(group);

        public LetteraStylePickerView(Action<LetteraStyleTemplate> chosen, LetteraPickerHooks hooks = null)
        {
            m_Chosen = chosen;
            m_Hooks = hooks ?? new LetteraPickerHooks();
            AddToClassList("lt-picker");
            style.flexGrow = 1;
            Search = new LtSearch("What should it do? Any layout");
            Search.Changed += SetQuery;
            Add(Search);
            m_List.style.flexGrow = 1;
            Add(m_List);
            RegisterCallback<KeyDownEvent>(OnKey, TrickleDown.TrickleDown);
            m_HoverTimer = schedule.Execute(() =>
            {
                if (m_Hovered != null) m_Hooks.Hover?.Invoke(m_Hovered);
            });
            m_HoverTimer.Pause();
            m_OpenGroups.Add(LetteraStyleTemplates.All[0].Driven);
            Fill();
            Search.Field.schedule.Execute(() => Search.Field.Focus());
        }

        internal void SetQuery(string query)
        {
            m_Query = query ?? "";
            Fill();
        }

        internal void OnKeyCode(KeyCode key, bool shift)
        {
            switch (key)
            {
                case KeyCode.UpArrow:
                    Move(-1);
                    break;
                case KeyCode.DownArrow:
                    Move(1);
                    break;
                case KeyCode.Return:
                case KeyCode.KeypadEnter:
                    if (Current != null) Pick(Current);
                    break;
                case KeyCode.Tab:
                    NextGroup(shift ? -1 : 1);
                    break;
            }
        }

        void OnKey(KeyDownEvent e)
        {
            if (e.keyCode != KeyCode.UpArrow && e.keyCode != KeyCode.DownArrow && e.keyCode != KeyCode.Return && e.keyCode != KeyCode.KeypadEnter && e.keyCode != KeyCode.Tab) return;
            OnKeyCode(e.keyCode, e.shiftKey);
            e.StopPropagation();
            focusController?.IgnoreEvent(e);
        }

        void Move(int delta)
        {
            if (m_Rows.Count == 0) return;
            m_At = Mathf.Clamp(m_At + delta, 0, m_Rows.Count - 1);
            Paint();
        }

        // Tab goes into the next group: it opens, and the first of it is taken.
        void NextGroup(int delta)
        {
            List<string> groups = LetteraStyleTemplates.All.Select(t => t.Driven).Distinct().ToList();
            string at = Current != null ? Current.Driven : groups[0];
            int index = (groups.IndexOf(at) + delta + groups.Count) % groups.Count;
            m_OpenGroups.Add(groups[index]);
            Fill();
            m_At = m_Rows.FindIndex(r => r.template.Driven == groups[index]);
            Paint();
        }

        void Paint()
        {
            for (int i = 0; i < m_Rows.Count; i++) m_Rows[i].row.EnableInClassList("lt-picker__item--on", i == m_At);
            if (m_List.panel != null && m_At >= 0 && m_At < m_Rows.Count) m_List.ScrollTo(m_Rows[m_At].row);
        }

        void Fill()
        {
            m_List.Clear();
            m_Rows.Clear();
            m_GroupHeads.Clear();
            bool searching = !string.IsNullOrWhiteSpace(m_Query);
            List<LetteraStyleTemplate> found = LetteraStyleTemplates.Find(m_Query);

            if (!searching)
            {
                List<LetteraStyleTemplate> recent = Recent();
                if (recent.Count > 0)
                {
                    m_List.Add(LtUI.Text("RECENT", "lt-picker__group lt-picker__group--flat"));
                    foreach (LetteraStyleTemplate template in recent) AddEntry(template);
                }
            }

            // The list alternates ways of driving a style, so a group is gathered, not read off in a row.
            foreach (IGrouping<string, LetteraStyleTemplate> group in found.GroupBy(t => t.Driven))
            {
                AddGroup(group.Key, searching);
                if (!searching && !m_OpenGroups.Contains(group.Key)) continue;
                foreach (IGrouping<string, LetteraStyleTemplate> kind in group.GroupBy(t => t.Kind))
                {
                    m_List.Add(LtUI.Text(kind.Key, "lt-picker__kind"));
                    foreach (LetteraStyleTemplate template in kind) AddEntry(template);
                }
            }
            if (found.Count == 0) m_List.Add(LtUI.Text("Nothing by that name. Try a word for what it does.", "lt-hint"));
            m_At = m_Rows.Count > 0 ? (searching ? 0 : Mathf.Clamp(m_At, 0, m_Rows.Count - 1)) : -1;
            Paint();
        }

        void AddGroup(string group, bool searching)
        {
            bool open = searching || m_OpenGroups.Contains(group);
            VisualElement chevron = new LtIcon(LtIcon.Kind.Chevron);
            chevron.AddToClassList("lt-picker__chevron");
            chevron.EnableInClassList("lt-picker__chevron--closed", !open);
            VisualElement head = LtUI.El("lt-picker__head", chevron, LtUI.Text(group.ToUpperInvariant(), "lt-picker__group"));
            head.RegisterCallback<ClickEvent>(_ =>
            {
                if (!m_OpenGroups.Remove(group)) m_OpenGroups.Add(group);
                Fill();
            });
            m_GroupHeads[group] = head;
            m_List.Add(head);
        }

        void AddEntry(LetteraStyleTemplate template)
        {
            bool applied = m_Hooks.Applied != null && m_Hooks.Applied(template);
            VisualElement dot = LtUI.El("lt-style__dot");
            dot.style.backgroundColor = LetteraStyleTemplates.ColorOf(template);
            VisualElement title = LtUI.El("lt-picker__title", dot, LtUI.Text(template.Name, "lt-picker__name"), LtUI.El("lt-style__space"), LtUI.Text(ExampleOf(template), "lt-picker__keys"));
            if (applied) title.Add(LtUI.Text("on", "lt-chip lt-chip--accent"));
            VisualElement item = LtUI.El("lt-picker__item", title, LtUI.Text(LetteraStyleTemplates.WordsOf(template, applied), "lt-picker__words"));
            item.tooltip = applied ? "On this text: a click takes it away." : null;
            item.RegisterCallback<ClickEvent>(_ => Pick(template));
            item.RegisterCallback<PointerEnterEvent>(_ =>
            {
                m_Hovered = template;
                m_HoverTimer.ExecuteLater(150);
            });
            item.RegisterCallback<PointerLeaveEvent>(_ =>
            {
                m_Hovered = null;
                m_HoverTimer.Pause();
                m_Hooks.HoverEnd?.Invoke();
            });
            m_Rows.Add((template, item));
            m_List.Add(item);
        }

        // How it is written. Whole text is written no way at all, and what follows a tag is only there for the search.
        internal static string ExampleOf(LetteraStyleTemplate template)
        {
            string words = template.Keywords;
            if (string.IsNullOrEmpty(words) || template.Driven == LetteraStyleTemplates.WholeText) return "";
            if (words[0] != '<') return words;
            int end = words.IndexOf('>');
            return end > 0 ? words.Substring(0, end + 1) : words;
        }

        internal void Pick(LetteraStyleTemplate template)
        {
            m_Hooks.HoverEnd?.Invoke();
            if (m_Hooks.Applied != null && m_Hooks.Applied(template))
            {
                m_Hooks.Remove?.Invoke(template);
            }
            else
            {
                Remember(template);
                m_Chosen?.Invoke(template);
            }
            Done?.Invoke();
        }

        public event Action Done;

        static string KeyOf(LetteraStyleTemplate t) => t.Driven + "|" + t.Name + "|" + t.Keywords;

        static List<LetteraStyleTemplate> Recent()
        {
            var list = new List<LetteraStyleTemplate>();
            foreach (string key in EditorPrefs.GetString(RecentKey, "").Split('\n'))
            {
                LetteraStyleTemplate found = LetteraStyleTemplates.All.FirstOrDefault(t => KeyOf(t) == key);
                if (found != null) list.Add(found);
            }
            return list;
        }

        internal static void Remember(LetteraStyleTemplate template)
        {
            var keys = EditorPrefs.GetString(RecentKey, "").Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries).ToList();
            keys.Remove(KeyOf(template));
            keys.Insert(0, KeyOf(template));
            if (keys.Count > MostRecent) keys.RemoveRange(MostRecent, keys.Count - MostRecent);
            EditorPrefs.SetString(RecentKey, string.Join("\n", keys));
        }

        internal static void ForgetRecent() => EditorPrefs.DeleteKey(RecentKey);
    }

    /// <summary>The window that holds the picker: of one size, on the side with room, gone with Esc or a click elsewhere.</summary>
    internal sealed class LetteraStylePicker : EditorWindow
    {
        static readonly Vector2 s_Size = new Vector2(340, 460);
        Action<LetteraStyleTemplate> m_Chosen;
        LetteraPickerHooks m_Hooks;

        public static void Open(Rect anchor, Action<LetteraStyleTemplate> chosen, LetteraPickerHooks hooks = null)
        {
            var window = CreateInstance<LetteraStylePicker>();
            window.m_Chosen = chosen;
            window.m_Hooks = hooks;
            window.ShowAsDropDown(GUIUtility.GUIToScreenRect(anchor), s_Size);
        }

        void CreateGUI()
        {
            VisualElement root = LtUI.Root();
            root.AddToClassList("lt-popup");
            root.style.flexGrow = 1;
            var view = new LetteraStylePickerView(m_Chosen, m_Hooks);
            view.Done += Close;
            root.Add(view);
            root.RegisterCallback<KeyDownEvent>(e =>
            {
                if (e.keyCode != KeyCode.Escape) return;
                Close();
                e.StopPropagation();
            }, TrickleDown.TrickleDown);
            rootVisualElement.Add(root);
        }

        void OnDestroy() => m_Hooks?.Closed?.Invoke();
    }
}

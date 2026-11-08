using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEditor;
using UnityEditor.ShortcutManagement;
using UnityEngine;
using UnityEngine.UIElements;

namespace FirstSymbol.Lettera.Editor
{
    /// <summary>What the panel needs from the inspector that hosts it.</summary>
    internal sealed class LtTagPanelHost
    {
        public Func<IReadOnlyList<LetteraStyle>> Styles = () => null;
        public Func<float> EmPixels = () => 0;
        /// <summary>0 when the written source is shown, else a preview.</summary>
        public Func<int> Mode = () => 0;
        public Func<bool> Multiple = () => false;
        public Func<bool> ReadOnly = () => false;
        public Action<Type> OpenStyle = _ => { };
    }

    /// <summary>
    /// The strip under the text field that shows the tag the caret is in, with a row for each of its slots. An edit goes back into the text as
    /// the least markup, one undo step per gesture, and the caret stays where it was.
    /// </summary>
    internal sealed class LtTagPanel : VisualElement
    {
        const int WindowFrom = 20000, Window = 2000;

        readonly TextField m_Source;
        readonly LtTagPanelHost m_Host;
        readonly LtGesture m_Gesture;
        readonly VisualElement m_Head, m_Crumbs, m_Markup, m_Body, m_Wrap, m_Info, m_Examples;
        readonly Label m_Title, m_Effect, m_Signature, m_Copy, m_Menu, m_About;
        readonly LtTagForm m_Form;

        string m_Text = "";
        int m_Cursor = -1, m_Select = -1;
        bool m_Dirty = true;
        int m_PinStart = -1, m_ActiveStart = -1, m_ActiveOrdinal;
        string m_ActiveName;
        LetteraTagSpan m_Active;
        List<LetteraTagSpan> m_Enclosing = new List<LetteraTagSpan>();
        LetteraEffect m_ActiveEffect;
        LtWrapPicker m_Picker;
        int m_WrapFrom, m_WrapTo;
        bool m_SourceFocused;
        string m_ShownKey;
        IVisualElementScheduledItem m_Job;

        /// <summary>Where the caret and the other end of the selection are. The text field's, unless a test says otherwise.</summary>
        internal Func<int> Cursor, Select;

        public static LtTagPanel Focused { get; private set; }

        /// <summary>The tag the panel edits now, for tests.</summary>
        public LetteraTagSpan ActiveTag => m_Active;
        public LtTagForm Form => m_Form;
        public LtWrapPicker Picker => m_Picker;
        public string Hint { get; private set; } = "";
        public string Kind { get; private set; } = "";

        /// <summary>The sentence under the markup that says what the tag does, for tests.</summary>
        public string About => m_About.text;

        /// <summary>The markups offered under it, as the tag would be written, for tests.</summary>
        public IReadOnlyList<string> ExampleMarkups => m_ExampleMarkups;

        readonly List<string> m_ExampleMarkups = new List<string>();
        readonly List<Action> m_ExampleActions = new List<Action>();

        /// <summary>Applies the nth example, as a click on it does.</summary>
        public void TryExample(int index) => m_ExampleActions[index]();

        public LtTagPanel(TextField source, LtTagPanelHost host)
        {
            m_Source = source;
            Cursor = () => source.cursorIndex;
            Select = () => source.selectIndex;
            m_Host = host;
            AddToClassList("lt-tagpanel");
            m_Gesture = new LtGesture(this);
            m_Title = LtUI.Text("TAG", "lt-tagpanel__title");
            m_Crumbs = LtUI.El("lt-tagpanel__crumbs");
            m_Effect = LtUI.Text("", "lt-tagpanel__effect");
            m_Copy = LtUI.Clickable("Copy", "lt-chip lt-chip--click lt-tagpanel__button", "Copy the open tag's markup.", CopyTag);
            m_Menu = LtUI.Clickable("...", "lt-chip lt-chip--click lt-tagpanel__button", "Reset every slot, select the tag's range, or open its style.", OpenMenu);
            m_Head = LtUI.El("lt-tagpanel__head", m_Title, m_Crumbs, LtUI.El("lt-tagpanel__space"), m_Effect, m_Copy, m_Menu);
            m_Markup = LtUI.El("lt-tagpanel__markup");
            m_Body = LtUI.El("lt-tagpanel__body");
            m_Wrap = LtUI.El("lt-tagpanel__wrap");
            m_About = LtUI.Text("", "lt-tagpanel__about");
            m_Examples = LtUI.El("lt-tagpanel__examples");
            m_Info = LtUI.El("lt-tagpanel__info", m_About, m_Examples);
            m_Signature = LtUI.Text("", "lt-tagpanel__signature");
            m_Form = new LtTagForm(() => m_Host.EmPixels());
            m_Form.SlotChanged += Write;
            m_Form.Suggest = info => LetteraSlotSuggestions.For(info, m_ActiveEffect);
            m_Form.Holding += m_Gesture.Hold;
            Add(m_Wrap);
            Add(m_Head);
            Add(m_Markup);
            Add(m_Info);
            Add(m_Body);
            Add(m_Signature);

            source.RegisterCallback<FocusInEvent>(_ =>
            {
                m_SourceFocused = true;
                Focused = this;
                m_Dirty = true;
            });
            source.RegisterCallback<FocusOutEvent>(_ =>
            {
                m_SourceFocused = false;
                if (Focused == this && !Inside(this)) Focused = this;
            });
            source.RegisterValueChangedCallback(_ => m_Dirty = true);
            RegisterCallback<AttachToPanelEvent>(_ =>
            {
                m_Job?.Pause();
                m_Job = schedule.Execute(Tick).Every(100);
            });
            RegisterCallback<DetachFromPanelEvent>(_ =>
            {
                m_Job?.Pause();
                if (Focused == this) Focused = null;
            });
            Refresh();
        }

        bool Inside(VisualElement element)
        {
            VisualElement focused = element.focusController?.focusedElement as VisualElement;
            return focused != null && element.Contains(focused);
        }

        // ------------------------------------------------------------- following the caret

        void Tick()
        {
            bool editingHere = m_SourceFocused || Inside(this);
            if (!editingHere && !m_Dirty) return;
            string text = m_Source.value ?? "";
            int cursor = Cursor(), select = Select();
            bool moved = cursor != m_Cursor || select != m_Select;
            if (!m_Dirty && !moved && string.Equals(text, m_Text, StringComparison.Ordinal)) return;
            // The caret moving in the source lets go of a chosen breadcrumb; the panel being edited keeps its tag.
            if (moved && m_SourceFocused) m_PinStart = -1;
            if (!m_SourceFocused && Inside(this) && !m_Dirty && !string.Equals(text, m_Text, StringComparison.Ordinal)) m_Dirty = true;
            Refresh();
        }

        /// <summary>Reads the text and caret again and draws what they hold.</summary>
        public void Refresh()
        {
            m_Dirty = false;
            SetEnabled(!m_Host.ReadOnly());
            m_Text = m_Source.value ?? "";
            m_Cursor = Cursor();
            m_Select = Select();
            m_Cursor = Mathf.Clamp(m_Cursor, 0, m_Text.Length);
            m_Select = Mathf.Clamp(m_Select, 0, m_Text.Length);

            if (m_Host.Mode() != 0)
            {
                Clear("Switch to Source to edit tags.");
                return;
            }
            if (m_Host.Multiple())
            {
                Clear("Select one text.");
                return;
            }

            int from = 0, to = m_Text.Length;
            if (m_Text.Length > WindowFrom)
            {
                from = Mathf.Max(0, m_Cursor - Window);
                to = Mathf.Min(m_Text.Length, m_Cursor + Window);
            }
            List<LetteraTagSpan> tags = LetteraTagScanner.Scan(m_Text, from, to);
            bool followCaret = m_SourceFocused || m_ActiveStart < 0 || !Inside(this);
            LetteraTagSpan active = null;
            if (followCaret)
            {
                m_Enclosing = LetteraTagScanner.Enclosing(tags, m_Cursor);
                if (m_PinStart >= 0)
                    foreach (LetteraTagSpan tag in m_Enclosing)
                        if (tag.Start == m_PinStart)
                            active = tag;
                if (active == null && m_Enclosing.Count > 0) active = m_Enclosing[m_Enclosing.Count - 1];
            }
            else
            {
                active = FindAgain(tags);
                m_Enclosing = LetteraTagScanner.Enclosing(tags, active != null ? active.Start + 1 : m_Cursor);
                if (active == null)
                {
                    followCaret = true;
                    m_Enclosing = LetteraTagScanner.Enclosing(tags, m_Cursor);
                    if (m_Enclosing.Count > 0) active = m_Enclosing[m_Enclosing.Count - 1];
                }
            }
            m_Active = active;
            if (active == null)
            {
                m_ActiveStart = -1;
                ShowNoTag();
                return;
            }
            m_ActiveStart = active.Start;
            m_ActiveName = active.Name;
            m_ActiveOrdinal = 0;
            foreach (LetteraTagSpan tag in tags)
            {
                if (tag == active) break;
                if (string.Equals(tag.Name, active.Name, StringComparison.OrdinalIgnoreCase)) m_ActiveOrdinal++;
            }
            ShowTag(active);
        }

        // After an edit or an undo: the same tag by place, else the same one among those of its name.
        LetteraTagSpan FindAgain(List<LetteraTagSpan> tags)
        {
            LetteraTagSpan byOrdinal = null;
            int seen = 0;
            foreach (LetteraTagSpan tag in tags)
            {
                if (tag.Start == m_ActiveStart && string.Equals(tag.Name, m_ActiveName, StringComparison.OrdinalIgnoreCase)) return tag;
                if (string.Equals(tag.Name, m_ActiveName, StringComparison.OrdinalIgnoreCase) && seen++ == m_ActiveOrdinal) byOrdinal = tag;
            }
            return byOrdinal;
        }

        // ------------------------------------------------------------- what the panel shows

        void Clear(string hint)
        {
            m_Active = null;
            m_Enclosing.Clear();
            Say(hint, "hint");
            m_Head.style.display = DisplayStyle.None;
            m_Markup.style.display = DisplayStyle.None;
            m_Signature.style.display = DisplayStyle.None;
            m_Info.style.display = DisplayStyle.None;
        }

        void Say(string text, string kind)
        {
            Kind = kind;
            Hint = text;
            m_Body.Clear();
            m_Body.Add(LtUI.Text(text, "lt-hint lt-tagpanel__hint"));
        }

        void ShowNoTag()
        {
            m_Info.style.display = DisplayStyle.None;
            m_Head.style.display = DisplayStyle.None;
            m_Markup.style.display = DisplayStyle.None;
            m_Signature.style.display = DisplayStyle.None;
            m_Form.RemoveFromHierarchy();
            LetteraTagProblem problem = LetteraTagScanner.Diagnose(m_Text, m_Cursor, out int start);
            m_Body.Clear();
            if (problem != LetteraTagProblem.None)
            {
                Kind = problem == LetteraTagProblem.NotClosed ? "unclosed" : "quote";
                Hint = problem == LetteraTagProblem.NotClosed ? "This tag is not closed, so it shows as text." : "Quote is not closed.";
                m_Head.style.display = DisplayStyle.Flex;
                m_Copy.style.display = m_Menu.style.display = DisplayStyle.None;
                m_Title.text = "TAG";
                m_Crumbs.Clear();
                m_Effect.text = "";
                m_Body.Add(new LtFix(Hint, null, "Fix", () => Mend(problem, start)));
                int rawFrom = start, rawTo = m_Text.IndexOf('\n', start);
                if (rawTo < 0) rawTo = m_Text.Length;
                var raw = new TextField { value = m_Text.Substring(rawFrom, Mathf.Min(rawTo - rawFrom, 200)), isReadOnly = true };
                raw.AddToClassList("lt-text-input");
                raw.AddToClassList("lt-tagpanel__raw");
                m_Body.Add(raw);
                return;
            }
            Kind = m_Cursor != m_Select ? "selection" : "caret";
            Hint = m_Cursor != m_Select
                ? Math.Abs(m_Cursor - m_Select).ToString(CultureInfo.InvariantCulture) + " characters selected."
                : "Put the caret inside a tag to edit its values.";
            VisualElement row = LtUI.El("lt-tagpanel__hintrow", LtUI.Text("TAG", "lt-tagpanel__title"), LtUI.Text(Hint, "lt-hint lt-tagpanel__hint"));
            VisualElement button = LtUI.Clickable("Wrap in tag...  Ctrl+.", "lt-fix__button lt-tagpanel__wrapbutton", "Choose a tag to wrap the selection in, or to insert at the caret.", () => OpenWrap(false));
            row.Add(button);
            m_Body.Add(row);
        }

        static readonly Dictionary<string, string> s_Syntax = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "br", "A line break. It takes no values and has no closing tag." },
            { "noparse", "Everything inside is shown as written: no tag in it is read." },
            { "rt", "The reading inside a <ruby>, as in <ruby>kanji<rt>reading</rt></ruby>. It takes no values." },
            { "rp", "A bracket for readers that cannot draw ruby. It is removed and takes no values." },
        };

        // A name a style answers to: a tag, or the name one of the built-in matchers reads.
        internal static string NameOf(LetteraMatcher matcher)
        {
            switch (matcher)
            {
                case TagMatcher tag: return tag.Name;
                case ListItemMatcher item: return item.Name;
                case RubyMatcher ruby: return ruby.Name;
                case DividerMatcher divider: return divider.Name;
                case InlineMediaMatcher media: return media.Name;
                case LetteraMathTagMatcher math: return math.Name;
                default: return null;
            }
        }

        internal static LetteraStyle StyleFor(IReadOnlyList<LetteraStyle> styles, string name)
        {
            LetteraStyle style = null;
            if (styles == null) return null;
            foreach (LetteraStyle candidate in styles)
            {
                if (candidate == null || !candidate.Enabled || candidate.Effect == null) continue;
                string matched = NameOf(candidate.Matcher);
                if (string.IsNullOrEmpty(matched) || !string.Equals(matched, name, StringComparison.OrdinalIgnoreCase)) continue;
                if (style == null || (!LetteraTagModel.HasSchema(style.Effect.Parameters) && LetteraTagModel.HasSchema(candidate.Effect.Parameters))) style = candidate;
            }
            return style;
        }

        void ShowTag(LetteraTagSpan tag)
        {
            IReadOnlyList<LetteraStyle> styles = m_Host.Styles();
            HashSet<string> known = LetteraVocabulary.Tags(styles ?? new List<LetteraStyle>());
            LetteraStyle style = StyleFor(styles, tag.Name);
            m_ActiveEffect = style?.Effect;

            m_Head.style.display = DisplayStyle.Flex;
            m_Signature.style.display = DisplayStyle.Flex;
            m_Copy.style.display = m_Menu.style.display = DisplayStyle.Flex;
            BuildCrumbs(tag);
            m_Effect.text = style?.Effect != null ? EffectLabel(style.Effect) : "";
            string key = tag.Start + "|" + tag.OpenText(m_Text);

            if (!known.Contains(tag.Name))
            {
                Kind = "unknown";
                Hint = "No style uses <" + tag.Name + ">. It shows as plain text.";
                m_Markup.style.display = DisplayStyle.None;
                m_Signature.style.display = DisplayStyle.None;
                m_Info.style.display = DisplayStyle.None;
                m_Form.RemoveFromHierarchy();
                m_Body.Clear();
                m_Body.Add(LtUI.Text(Hint, "lt-note lt-tagpanel__notice"));
                return;
            }

            BuildMarkup(tag);
            LetteraEffect effect = style?.Effect;
            string syntax = null;
            if (effect == null) s_Syntax.TryGetValue(tag.Name, out syntax);
            ShowInfo(tag, effect, syntax);
            string value = tag.EqualsAt >= 0 ? m_Text.Substring(tag.EqualsAt + 1, tag.ValueEnd - tag.EqualsAt - 1) : "";

            IReadOnlyList<LetteraParamInfo> described = effect?.Parameters;
            bool hasForm = effect != null && !(effect is CompositeEffect) && LetteraTagModel.HasSchema(described);

            if (effect is CompositeEffect composite)
            {
                ShowComposite(tag, style, composite, value);
                return;
            }

            if (!hasForm)
            {
                m_Form.RemoveFromHierarchy();
                m_Signature.text = "";
                m_Signature.style.display = DisplayStyle.None;
                m_Body.Clear();
                if (syntax != null || (effect != null && effect.TakesNoParameters))
                {
                    Kind = "none";
                    Hint = "This tag takes no parameters.";
                    m_Body.Add(LtUI.Text(Hint, "lt-tagpanel__none"));
                    if (value.Length > 0) m_Body.Add(new LtFix("A value after = is ignored by this tag.", null, "Remove it", () => WriteRaw("")));
                    return;
                }
                if (effect != null && effect.BuiltInFacts != null && effect.BuiltInFacts.Delegates)
                {
                    Kind = "delegate";
                    Hint = "This tag hands its value on to the preset's effect.";
                    m_Body.Add(LtUI.Text(Hint + " Write that effect's values as text.", "lt-hint lt-tagpanel__hint"));
                    AddRaw(value);
                    return;
                }
                Kind = "noschema";
                Hint = "No slot form for this effect yet.";
                m_Body.Add(LtUI.Text(Hint + " Edit its value as text.", "lt-hint lt-tagpanel__hint"));
                if (effect != null)
                {
                    m_Body.Add(LtUI.Text("Its author has not said what its values mean. An effect names its slots by overriding Parameters; the Styles guide, section Your own effect, shows how.", "lt-tagpanel__why"));
                    m_Body.Add(LtUI.Clickable("Open the Styles guide", "lt-chip lt-chip--click lt-tagpanel__example", "Opens Documentation~/Styles.md.", OpenGuide));
                }
                AddRaw(value);
                return;
            }

            Kind = "form";
            Hint = "";
            string[] defaults = style.ParsedDefault();
            List<LetteraSlotRow> rows = LetteraTagModel.Rows(tag, described, style.Effect, defaults);
            int count = LetteraTagModel.SlotCount(described);
            List<string> extras = null;
            for (int i = count; i < tag.Slots.Count; i++)
                (extras ?? (extras = new List<string>())).Add("Extra " + (i + 1) + ": " + (tag.Raw(m_Text, i).Length == 0 ? "empty" : tag.Raw(m_Text, i)));
            if (m_Form.parent != m_Body)
            {
                m_Body.Clear();
                m_Body.Add(m_Form);
            }
            m_Form.Show(described, rows, extras);
            int slot = m_SourceFocused ? LetteraTagScanner.SlotAt(tag, m_Cursor) : -1;
            m_Form.Mark(slot);
            UpdateSignature(tag, described, slot, count);
            m_ShownKey = key;
        }

        void AddRaw(string value)
        {
            var raw = new TextField { value = value, isDelayed = true };
            raw.AddToClassList("lt-text-input");
            raw.AddToClassList("lt-tagpanel__raw");
            raw.RegisterValueChangedCallback(e => WriteRaw(e.newValue));
            m_Body.Add(raw);
        }

        static void OpenGuide()
        {
            UnityEditor.PackageManager.PackageInfo info = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(LetteraText).Assembly);
            if (info == null) return;
            string path = Path.Combine(info.resolvedPath, "Documentation~", "Styles.md");
            if (File.Exists(path)) EditorUtility.OpenWithDefaultApp(path);
        }

        // What the tag does, and markups to try. A click on one writes its value into the tag.
        void ShowInfo(LetteraTagSpan tag, LetteraEffect effect, string syntax)
        {
            string about = syntax ?? effect?.Description;
            IReadOnlyList<LetteraTagExample> examples = effect != null ? effect.Examples : null;
            if (effect is CompositeEffect group && (examples == null || examples.Count == 0)) examples = PartExamples(group);
            m_ExampleMarkups.Clear();
            m_ExampleActions.Clear();
            m_Examples.Clear();
            m_About.text = about ?? "";
            m_About.style.display = string.IsNullOrEmpty(about) ? DisplayStyle.None : DisplayStyle.Flex;
            bool any = examples != null && examples.Count > 0;
            m_Info.style.display = any || !string.IsNullOrEmpty(about) ? DisplayStyle.Flex : DisplayStyle.None;
            if (!any) return;
            string current = tag.EqualsAt >= 0 ? m_Text.Substring(tag.EqualsAt + 1, tag.ValueEnd - tag.EqualsAt - 1).Trim() : "";
            m_Examples.Add(LtUI.Text("Try", "lt-tagpanel__trylabel"));
            foreach (LetteraTagExample example in examples)
            {
                LetteraTagExample chosen = example;
                string markup = "<" + tag.Name + (chosen.Value.Length > 0 ? "=" + chosen.Value : "") + ">";
                Action apply = () => WriteRaw(chosen.Value);
                Label chip = LtUI.Clickable(markup, "lt-chip lt-chip--click lt-tagpanel__example", chosen.Note + ". Click to write this value into the tag.", apply);
                chip.EnableInClassList("lt-chip--accent", string.Equals(current, chosen.Value, StringComparison.Ordinal));
                VisualElement row = LtUI.El("lt-tagpanel__examplerow", chip, LtUI.Text(chosen.Note, "lt-tagpanel__why"));
                m_Examples.Add(row);
                m_ExampleMarkups.Add(markup);
                m_ExampleActions.Add(apply);
            }
        }

        // A composite has no examples of its own: the first example of each part, one part more at a time.
        static IReadOnlyList<LetteraTagExample> PartExamples(CompositeEffect composite)
        {
            var parts = new List<string>();
            var names = new List<string>();
            foreach (LetteraEffect child in composite.Children)
            {
                if (child == null) continue;
                string first = null;
                foreach (LetteraTagExample example in child.Examples)
                    if (example.Value.Length > 0)
                    {
                        first = example.Value;
                        break;
                    }
                if (first == null) continue;
                parts.Add(first);
                names.Add(EffectLabel(child).Split('\u00b7')[0].Trim().ToLowerInvariant());
            }
            var list = new List<LetteraTagExample>();
            for (int count = 1; count <= parts.Count && count <= 2; count++)
                list.Add(new LetteraTagExample(ComposeParts(parts.GetRange(0, count)), "Fills the first " + (count == 1 ? "part: " + names[0] : count + " parts: " + string.Join(", ", names.GetRange(0, count)))));
            return list;
        }

        // A composite's value is one part per effect, split by semicolons. Each part gets a field, named for its effect.
        void ShowComposite(LetteraTagSpan tag, LetteraStyle style, CompositeEffect composite, string value)
        {
            Kind = "composite";
            Hint = "";
            m_Form.RemoveFromHierarchy();
            m_Body.Clear();
            m_Signature.style.display = DisplayStyle.None;
            List<string> parts = CompositeEffect.SplitSegments(value) ?? new List<string>();
            string defaults = style != null ? style.DefaultParameter : null;
            List<string> fallback = CompositeEffect.SplitSegments(defaults);
            m_Body.Add(LtUI.Text("One value in parts, written with ; between them. Each part goes to one effect and holds that effect's own values, with commas between.", "lt-tagpanel__why"));
            for (int i = 0; i < composite.Children.Count; i++)
            {
                LetteraEffect child = composite.Children[i];
                if (child == null) continue;
                int index = i;
                var names = new List<string>();
                if (child.Parameters != null)
                    foreach (LetteraParamInfo info in child.Parameters)
                        if (info.Slot >= 0 && string.IsNullOrEmpty(info.Group)) names.Add(info.Display.ToLowerInvariant());
                string takes = child.TakesNoParameters || names.Count == 0 ? "Takes no values here." : "Values: " + string.Join(", ", names) + ".";
                string what = (child.Description != null ? child.Description + " " : "") + takes;
                var field = new TextField { value = index < parts.Count ? parts[index] ?? "" : "", isDelayed = true };
                field.AddToClassList("lt-text-input");
                field.AddToClassList("lt-tagpanel__raw");
                string inherited = fallback != null && index < fallback.Count ? fallback[index] : null;
                field.tooltip = inherited != null ? "Empty keeps the style's own: " + inherited : "Empty keeps the effect's own values.";
                field.RegisterValueChangedCallback(e => WritePart(index, e.newValue));
                VisualElement part = LtUI.El("lt-tagpanel__part", LtUI.Text((index + 1) + "  " + EffectLabel(child), "lt-tagpanel__partname"), LtUI.Text(what, "lt-tagpanel__partwhat"), field);
                m_Body.Add(part);
            }
        }

        /// <summary>Sets one part of a composite tag's value, as typing into its field does.</summary>
        internal void WritePart(int index, string text)
        {
            List<string> now = CompositeEffect.SplitSegments(CurrentValue()) ?? new List<string>();
            while (now.Count <= index) now.Add(null);
            now[index] = string.IsNullOrWhiteSpace(text) ? null : text.Trim();
            WriteRaw(ComposeParts(now));
        }

        string CurrentValue()
        {
            LetteraTagSpan tag = Current();
            return tag != null && tag.EqualsAt >= 0 ? m_Text.Substring(tag.EqualsAt + 1, tag.ValueEnd - tag.EqualsAt - 1) : "";
        }

        internal static string ComposeParts(List<string> parts)
        {
            int last = parts.Count - 1;
            while (last >= 0 && string.IsNullOrEmpty(parts[last])) last--;
            var pieces = new List<string>();
            for (int i = 0; i <= last; i++)
            {
                string part = parts[i] ?? "";
                // A part that holds a semicolon is wrapped, as the splitter takes one level of parentheses off.
                if (part.IndexOf(';') >= 0) part = "(" + part + ")";
                pieces.Add(part);
            }
            return string.Join(";", pieces);
        }

        static string EffectLabel(LetteraEffect effect)
        {
            string name = LetteraStyleList.Pretty(effect.GetType().Name, "Effect");
            foreach (LetteraStyleTemplate template in LetteraStyleTemplates.All)
            {
                if (template.Driven != LetteraStyleTemplates.Tags || template.Effect == null) continue;
                try
                {
                    if (template.Effect().GetType() == effect.GetType()) return template.Name + " · " + template.Kind;
                }
                catch (Exception)
                {
                    // Not every template can be made out of context.
                }
            }
            return name;
        }

        void BuildCrumbs(LetteraTagSpan active)
        {
            m_Crumbs.Clear();
            var chain = m_Enclosing.Count > 0 && m_Enclosing.Contains(active) ? m_Enclosing : new List<LetteraTagSpan> { active };
            foreach (LetteraTagSpan tag in chain)
            {
                LetteraTagSpan chosen = tag;
                Label chip = LtUI.Clickable("<" + tag.Name + ">", "lt-chip lt-chip--click lt-tagpanel__crumb", "Edit this tag.", () =>
                {
                    m_PinStart = chosen.Start;
                    m_Dirty = true;
                    Refresh();
                });
                chip.EnableInClassList("lt-chip--accent", tag == active);
                m_Crumbs.Add(chip);
            }
            if (!string.IsNullOrEmpty(active.Label))
            {
                Label label = LtUI.Text("#" + active.Label, "lt-chip lt-tagpanel__label");
                label.tooltip = "The label code uses to find this range.";
                m_Crumbs.Add(label);
            }
        }

        // The open tag as it will be written. Each value is a piece; a click on one goes to its row.
        void BuildMarkup(LetteraTagSpan tag)
        {
            m_Markup.style.display = DisplayStyle.Flex;
            m_Markup.Clear();
            int slotAtCaret = LetteraTagScanner.SlotAt(tag, m_Cursor);
            if (tag.EqualsAt < 0)
            {
                m_Markup.Add(LtUI.Text(tag.OpenText(m_Text), "lt-tagpanel__tok"));
                return;
            }
            m_Markup.Add(LtUI.Text(m_Text.Substring(tag.Start, tag.EqualsAt + 1 - tag.Start), "lt-tagpanel__tok lt-tagpanel__tok--tag"));
            for (int i = 0; i < tag.Slots.Count; i++)
            {
                int index = i;
                string raw = tag.Raw(m_Text, i);
                Label token = LtUI.Clickable(raw.Length == 0 ? "·" : raw, "lt-tagpanel__tok lt-tagpanel__tok--slot", "Slot " + (i + 1), () => m_Form.FocusSlot(index));
                token.EnableInClassList("lt-tagpanel__tok--on", slotAtCaret == i);
                token.EnableInClassList("lt-tagpanel__tok--empty", raw.Length == 0);
                m_Markup.Add(token);
                if (i + 1 < tag.Slots.Count)
                    m_Markup.Add(LtUI.Text(m_Text.Substring(tag.Slots[i].End, tag.Slots[i + 1].Start - tag.Slots[i].End), "lt-tagpanel__tok lt-tagpanel__tok--tag"));
            }
            m_Markup.Add(LtUI.Text(m_Text.Substring(tag.ValueEnd, tag.End - tag.ValueEnd), "lt-tagpanel__tok lt-tagpanel__tok--tag"));
        }

        void UpdateSignature(LetteraTagSpan tag, IReadOnlyList<LetteraParamInfo> described, int slot, int count) =>
            m_Signature.text = SignatureText(tag.Name, described, slot);

        /// <summary>The line under the panel: the slot the caret is in, or else the tag's whole shape.</summary>
        internal static string SignatureText(string tag, IReadOnlyList<LetteraParamInfo> described, int slot)
        {
            LetteraParamInfo info = null;
            int main = 0;
            foreach (LetteraParamInfo candidate in described)
            {
                if (candidate.Slot == slot) info = candidate;
                if (candidate.Slot >= 0 && string.IsNullOrEmpty(candidate.Group)) main++;
            }
            string hint = info == null || string.IsNullOrEmpty(info.Hint) ? "" : ". " + info.Hint;
            if (info != null && !string.IsNullOrEmpty(info.Group)) return "slot " + (slot + 1) + ", " + info.Group.ToLowerInvariant() + " setting: " + info.Display + ", " + LetteraSlotValue.Describe(info) + hint;
            if (info != null) return "slot " + (slot + 1) + " of " + main + ": " + info.Display + ", " + LetteraSlotValue.Describe(info) + hint;
            var names = new List<string>();
            foreach (LetteraParamInfo candidate in described)
                if (candidate.Slot >= 0 && string.IsNullOrEmpty(candidate.Group))
                    names.Add(candidate.Display.ToLowerInvariant());
            return "<" + tag + "=" + string.Join(", ", names) + ">";
        }

        public string Signature => m_Signature.text;

        // ------------------------------------------------------------- writing

        // Typing and dragging are one step until they pause; a click or a reset is a step of its own.
        internal void Write(int slot, string token) => Write(slot, token, token == null || !(m_Form.Editing || m_Gesture.IsHeld));

        internal void Write(int slot, string token, bool discrete)
        {
            LetteraTagSpan tag = Current();
            if (tag == null) return;
            LetteraTextEdit edit = LetteraTagWriter.SetSlot(m_Text, tag, slot, token);
            if (!edit.Ok)
            {
                Hint = "That value cannot be written inside a tag: it holds both kinds of quote.";
                m_Signature.text = Hint;
                return;
            }
            if (edit.Text == m_Text) return;
            Apply(edit, discrete);
        }

        void WriteRaw(string value)
        {
            LetteraTagSpan tag = Current();
            if (tag == null) return;
            string text = m_Text;
            LetteraTextEdit edit;
            string raw = (value ?? "").Trim();
            if (tag.EqualsAt >= 0)
                edit = raw.Length == 0
                    ? new LetteraTextEdit(text.Remove(tag.EqualsAt, tag.ValueEnd - tag.EqualsAt), tag.EqualsAt, tag.ValueEnd - tag.EqualsAt, 0)
                    : new LetteraTextEdit(text.Substring(0, tag.EqualsAt + 1) + raw + text.Substring(tag.ValueEnd), tag.EqualsAt + 1, tag.ValueEnd - tag.EqualsAt - 1, raw.Length);
            else if (raw.Length == 0) return;
            else edit = new LetteraTextEdit(text.Insert(tag.HeadEnd, "=" + raw), tag.HeadEnd, 0, raw.Length + 1);
            Apply(edit, true);
        }

        void CopyTag()
        {
            LetteraTagSpan tag = Current();
            if (tag != null) GUIUtility.systemCopyBuffer = tag.OpenText(m_Text);
        }

        void OpenMenu()
        {
            LetteraTagSpan tag = Current();
            if (tag == null) return;
            var menu = new GenericMenu();
            menu.AddItem(new GUIContent("Reset all to inherit"), false, ResetAll);
            menu.AddItem(new GUIContent("Select tag range"), false, () =>
            {
                LetteraTagSpan now = Current();
                if (now == null) return;
                m_Source.Focus();
                m_Source.SelectRange(now.End, now.HasClose ? now.CloseStart : now.End);
            });
            Type effect = m_ActiveEffect?.GetType();
            if (effect != null) menu.AddItem(new GUIContent("Open style"), false, () => m_Host.OpenStyle(effect));
            else menu.AddDisabledItem(new GUIContent("Open style"));
            menu.ShowAsContext();
        }

        /// <summary>Back to the bare tag: every slot inherits.</summary>
        public void ResetAll()
        {
            LetteraTagSpan tag = Current();
            if (tag == null) return;
            LetteraTextEdit edit = LetteraTagWriter.Clear(m_Text, tag);
            if (edit.Ok && edit.Removed > 0) Apply(edit, true);
        }

        LetteraTagSpan Current()
        {
            m_Text = m_Source.value ?? "";
            return FindAgain(LetteraTagScanner.Scan(m_Text, 0, m_Text.Length));
        }

        void Apply(LetteraTextEdit edit, bool discrete)
        {
            int cursor = Cursor(), select = Select();
            m_Gesture.Run(() =>
            {
                m_Source.value = edit.Text;
            }, discrete);
            if (m_SourceFocused) m_Source.SelectRange(edit.Map(cursor), edit.Map(select));
            m_Dirty = true;
            Refresh();
        }

        /// <summary>What the Fix button does: closes the tag or the quote the caret is in.</summary>
        internal void Fix()
        {
            m_Text = m_Source.value ?? "";
            LetteraTagProblem problem = LetteraTagScanner.Diagnose(m_Text, Cursor(), out int start);
            if (problem != LetteraTagProblem.None) Mend(problem, start);
        }

        internal void CloseGesture() => m_Gesture.Close();

        void Mend(LetteraTagProblem problem, int start)
        {
            string mended = LetteraTagScanner.Mend(m_Text, start, problem, out int at, out string inserted);
            if (mended == m_Text) return;
            Apply(new LetteraTextEdit(mended, at, 0, inserted.Length), true);
        }

        // ------------------------------------------------------------- wrap

        /// <summary>The Ctrl+. key: into the tag's form when the caret is in one, else the wrap list.</summary>
        public void Shortcut()
        {
            if (m_Host.Mode() != 0 || m_Host.Multiple()) return;
            Refresh();
            if (m_Active != null && Kind == "form") m_Form.FocusSlot(Mathf.Max(0, LetteraTagScanner.SlotAt(m_Active, m_Cursor)));
            else OpenWrap(false);
        }

        public void OpenWrap(bool edit)
        {
            if (m_Host.ReadOnly()) return;
            CloseWrap();
            m_WrapFrom = Mathf.Min(Cursor(), Select());
            m_WrapTo = Mathf.Max(Cursor(), Select());
            m_WrapFrom = Mathf.Clamp(m_WrapFrom, 0, (m_Source.value ?? "").Length);
            m_WrapTo = Mathf.Clamp(m_WrapTo, m_WrapFrom, (m_Source.value ?? "").Length);
            var all = LetteraWrapList.Build(m_Host.Styles(), m_Source.value);
            int a = m_WrapFrom, b = m_WrapTo;
            LetteraTagScanner.Balance(m_Source.value ?? "", ref a, ref b, out bool adjusted);
            m_Picker = new LtWrapPicker(all, adjusted ? "Adjusted to keep tags balanced." : null);
            m_Picker.Chosen += (entry, editNow) =>
            {
                Wrap(entry.Tag, a, b);
                CloseWrap();
                if (editNow) schedule.Execute(() => m_Form.FocusSlot(0)).ExecuteLater(150);
            };
            m_Picker.Closed += () =>
            {
                m_Picker = null;
                m_Source.Focus();
            };
            m_Wrap.Add(m_Picker);
        }

        public void CloseWrap()
        {
            if (m_Picker == null) return;
            LtWrapPicker picker = m_Picker;
            m_Picker = null;
            picker.RemoveFromHierarchy();
        }

        void Wrap(string tag, int a, int b)
        {
            string text = m_Source.value ?? "";
            a = Mathf.Clamp(a, 0, text.Length);
            b = Mathf.Clamp(b, a, text.Length);
            string open = "<" + tag + ">", close = "</" + tag + ">";
            string wrapped = text.Substring(0, a) + open + text.Substring(a, b - a) + close + text.Substring(b);
            m_Gesture.Run(() => m_Source.value = wrapped, true);
            m_Source.Focus();
            m_Source.SelectRange(a + open.Length, b + open.Length);
            m_Dirty = true;
            m_PinStart = -1;
            Refresh();
        }
    }

    /// <summary>The key that opens the wrap list. It can be rebound under Edit, Shortcuts.</summary>
    internal static class LetteraTagShortcuts
    {
        [Shortcut("Lettera/Wrap In Tag", KeyCode.Period, ShortcutModifiers.Action)]
        static void WrapInTag() => LtTagPanel.Focused?.Shortcut();
    }
}

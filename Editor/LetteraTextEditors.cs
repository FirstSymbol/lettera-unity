using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace FirstSymbol.Lettera.Editor
{
    /// <summary>The custom inspector all text components share: one page of cards under a header that says how the text stands.</summary>
    internal sealed class LetteraTextInspector
    {
        const string PreviewSizeKey = "Lettera.Inspector.PreviewSize", ColoredKey = "Lettera.Inspector.ColoredSource", TallKey = "Lettera.Inspector.TallSource";

        readonly SerializedObject m_Object;
        readonly SerializedProperty m_Data;
        readonly Func<string> m_Stats;
        readonly Func<string> m_Override;
        readonly string m_Title;
        readonly Action<VisualElement> m_BuildRenderPage;
        readonly VisualElement m_Root = LtUI.Root();
        readonly List<LtSection> m_Sections = new List<LtSection>();
        Label m_Subtitle;
        VisualElement m_Chips, m_Warnings;
        TextField m_TextField;
        Label m_SceneEditWhy;
        LetteraStyleList m_StyleList;
        string m_ChipsShown, m_WarningsShown;

        public LetteraTextInspector(SerializedObject serialized, string title, Func<string> stats, Func<string> overrideReason, Action<VisualElement> buildRenderPage)
        {
            m_Object = serialized;
            m_Data = serialized.FindProperty("m_Data");
            m_Title = title;
            m_Stats = stats;
            m_Override = overrideReason;
            m_BuildRenderPage = buildRenderPage;
        }

        SerializedProperty P(string name) => m_Data.FindPropertyRelative(name);
        TextEngine Engine => (m_Object.targetObject as ITextComponent)?.SharedEngine;
        internal TextField SourceField => m_TextField;
        internal LetteraStyleList StyleList => m_StyleList;
        internal VisualElement Warnings => m_Warnings;

        public VisualElement Build()
        {
            m_Root.Add(BuildHeader());
            m_Root.Add(BuildJump());

            string prefix = m_Title.Replace(" ", "");
            m_Sections.Add(new LtSection(prefix + ".text", "Text", FirstLine).With(BuildEditor()));
            m_Sections.Add(new LtSection(prefix + ".font", "Font", FontSummary).With(BuildFontPage()));
            m_Sections.Add(new LtSection(prefix + ".layout", "Layout", () => ((LetteraHorizontalAlignment)P("HorizontalAlignment").enumValueIndex) + (P("WordWrap").boolValue ? ", wraps" : ", one line")).With(BuildLayoutPage()));
            m_Sections.Add(new LtSection(prefix + ".styles", "Styles", StylesSummary).With(BuildStylesPage()));
            VisualElement render = LtUI.El("lt-page");
            m_BuildRenderPage(render);
            m_Sections.Add(new LtSection(prefix + ".render", "Render").With(render));
            m_Sections.Add(new LtSection(prefix + ".interaction", "Interaction").With(LetteraEditingMenus.InteractionCard(m_Object.targetObjects)));
            foreach (LtSection section in m_Sections) m_Root.Add(section);
            AddWhenPresent(prefix);

            m_Root.schedule.Execute(RefreshHeader).Every(400);
            RefreshHeader();
            return m_Root;
        }

        // Cards that show only when the text has what they speak of.
        void AddWhenPresent(string prefix)
        {
            if (Engine == null) return;
            TextEngine engine = Engine;
            var formulas = new List<LetteraFormulaInfo>();
            var math = new LtSection(prefix + ".math", "Math", () =>
            {
                formulas.Clear();
                engine.CollectFormulas(formulas);
                return formulas.Count + (formulas.Count == 1 ? " formula" : " formulas");
            }).With(LetteraMathInspector.Card(engine.CollectFormulas));
            var status = new LtStatus()
                .Line("Size", () => engine.View == null ? "" : engine.View.Rope.Length.ToString("N0", CultureInfo.InvariantCulture) + " characters, " + engine.View.Rope.ParagraphCount.ToString("N0", CultureInfo.InvariantCulture) + " paragraphs")
                .Line("Measured", () => engine.View == null || engine.View.Rope.BlockCount == 0 ? "" : Mathf.RoundToInt(100f * engine.View.MeasuredBlockCount / engine.View.Rope.BlockCount) + "%, " + engine.View.MeasuredBlockCount + " of " + engine.View.Rope.BlockCount + " blocks",
                    () => engine.View != null && engine.View.MeasuredBlockCount < engine.View.Rope.BlockCount ? LtStatus.State.Warn : LtStatus.State.Good)
                .Line("Window", () => engine.View == null ? "" : engine.View.WindowLength.ToString("N0", CultureInfo.InvariantCulture) + " characters from " + engine.View.WindowStart.ToString("N0", CultureInfo.InvariantCulture));
            var document = new LtSection(prefix + ".document", "Document").With(status);
            void Refresh()
            {
                formulas.Clear();
                engine.CollectFormulas(formulas);
                math.style.display = formulas.Count > 0 ? DisplayStyle.Flex : DisplayStyle.None;
                bool open = engine.View != null;
                document.style.display = open ? DisplayStyle.Flex : DisplayStyle.None;
                if (open) status.Refresh();
            }
            Refresh();
            m_Root.schedule.Execute(Refresh).Every(500);
            m_Sections.Add(math);
            m_Sections.Add(document);
            m_Root.Add(math);
            m_Root.Add(document);
        }

        string FirstLine()
        {
            string text = P("Text").stringValue ?? "";
            int line = text.IndexOf('\n');
            if (line >= 0) text = text.Substring(0, line);
            return text.Length > 40 ? text.Substring(0, 40) + "…" : text;
        }

        string FontSummary()
        {
            string size = P("AutoSize").boolValue ? "auto, " + P("MinFontSize").floatValue.ToString("0.##") + " to " + P("MaxFontSize").floatValue.ToString("0.##") : P("FontSize").floatValue.ToString("0.##");
            return FontLabel() + ", " + size;
        }

        string StylesSummary()
        {
            SerializedProperty styles = P("Styles");
            if (styles.arraySize == 0) return "None";
            object effect = styles.GetArrayElementAtIndex(0).FindPropertyRelative("m_Effect").managedReferenceValue;
            string first = effect == null ? "Style" : LetteraStyleList.Pretty(effect.GetType().Name, "Effect");
            return styles.arraySize == 1 ? first : first + ", " + (styles.arraySize - 1) + " more";
        }

        // Everything on one page: this row goes to a card, and the filter finds a setting wherever it is.
        VisualElement BuildJump()
        {
            VisualElement holder = LtUI.El("lt-jump-holder");
            VisualElement jump = LtUI.El("lt-jump");
            foreach (string name in new[] { "Text", "Font", "Layout", "Styles", "Render", "Interaction" })
            {
                int index = jump.childCount;
                Label item = LtUI.Text(name, "lt-jump__item");
                item.tooltip = "Goes to this card. Alt closes the others.";
                item.RegisterCallback<ClickEvent>(e => Jump(index, e.altKey));
                jump.Add(item);
            }
            holder.Add(jump);
            var search = new LtSearch("Find a setting");
            search.Changed += Filter;
            holder.Add(search);
            return holder;
        }

        internal void Jump(int index, bool closeOthers)
        {
            if (index >= m_Sections.Count) return;
            LtSection section = m_Sections[index];
            section.Open = true;
            if (closeOthers)
                foreach (LtSection other in m_Sections)
                    if (other != section)
                        other.Open = false;
            m_Root.GetFirstAncestorOfType<ScrollView>()?.ScrollTo(section);
        }

        // Hides what does not fit by a class, so that what a row shows by its own rule stays that row's own business.
        internal void Filter(string query)
        {
            bool filtering = !string.IsNullOrWhiteSpace(query);
            foreach (LtSection section in m_Sections)
            {
                bool titleFits = filtering && LtSearchText.Score(query, section.Title) > 0;
                bool any = false;
                foreach (VisualElement row in section.Content.Query(className: "lt-row").ToList())
                {
                    bool fits = !filtering || titleFits || RowFits(row, query);
                    row.EnableInClassList("lt-filtered", !fits);
                    any |= fits;
                }
                foreach (VisualElement row in section.Content.Query(className: "lt-list__row").ToList())
                {
                    bool fits = !filtering || titleFits || WordsFit(row, query);
                    row.EnableInClassList("lt-filtered", !fits);
                    any |= fits;
                }
                foreach (VisualElement tool in section.Content.Query(className: "lt-tool").ToList())
                {
                    bool fits = !filtering || titleFits || (tool is Label label && LtSearchText.Score(query, label.text) >= 30) || (!string.IsNullOrEmpty(tool.tooltip) && LtSearchText.Score(query, tool.tooltip) >= 60);
                    tool.EnableInClassList("lt-filtered", !fits);
                    any |= fits;
                }
                foreach (VisualElement source in section.Content.Query(className: "lt-source-box").ToList())
                    source.EnableInClassList("lt-filtered", filtering && !titleFits);
                section.EnableInClassList("lt-filtered", filtering && !any && !titleFits);
                section.ForceOpen(filtering && (any || titleFits));
            }
        }

        static bool RowFits(VisualElement row, string query)
        {
            if (!string.IsNullOrEmpty(row.tooltip) && LtSearchText.Score(query, row.tooltip) >= 60) return true;
            foreach (Label label in row.Query<Label>().ToList())
                if ((label.ClassListContains("lt-row__label") || label.ClassListContains("lt-inline__label")) && LtSearchText.Score(query, label.text) > 0)
                    return true;
            return false;
        }

        static bool WordsFit(VisualElement row, string query)
        {
            foreach (Label label in row.Query<Label>().ToList())
                if ((label.ClassListContains("lt-style__tag") || label.ClassListContains("lt-style__type") || label.ClassListContains("lt-row__label") || label.ClassListContains("lt-object__name")) && LtSearchText.Score(query, label.text) > 0)
                    return true;
            return false;
        }

        // ------------------------------------------------------------- header

        VisualElement BuildHeader()
        {
            m_Subtitle = LtUI.Text("", "lt-subtitle");
            m_Chips = LtUI.El("lt-chips");
            VisualElement info = LtUI.El("lt-header__info", LtUI.Text(m_Title, "lt-title"), m_Subtitle, m_Chips);
            VisualElement top = LtUI.El("lt-header__top", LtUI.Text("Lt", "lt-mark"), info);
            m_Warnings = LtUI.El("lt-warnings");
            VisualElement header = LtUI.El("lt-header lt-header--stack", top, m_Warnings);
            return header;
        }

        void RefreshHeader()
        {
            if (m_Object.targetObject == null) return;
            m_Object.UpdateIfRequiredOrScript();
            m_Subtitle.text = FontWhy();
            m_Subtitle.tooltip = m_Subtitle.text;

            string key = ChipKey(out List<(string text, string classes, string tip, Action click)> chips);
            if (key != m_ChipsShown)
            {
                m_ChipsShown = key;
                m_Chips.Clear();
                foreach ((string text, string classes, string tip, Action click) in chips)
                {
                    Label chip = click != null ? LtUI.Clickable(text, classes, tip, click) : LtUI.Text(text, classes);
                    if (click == null) chip.tooltip = tip;
                    m_Chips.Add(chip);
                }
            }
            RefreshWarnings();
            foreach (LtSection section in m_Sections) section.RefreshSummary();
            RefreshTools();
            RefreshSource();
            string why = LetteraSceneEdit.WhyNot(m_Object.targetObjects);
            m_SceneEditWhy.text = why ?? "";
            m_SceneEditWhy.style.display = why != null ? DisplayStyle.Flex : DisplayStyle.None;
        }

        string ChipKey(out List<(string, string, string, Action)> chips)
        {
            chips = new List<(string, string, string, Action)>();
            int count = m_Object.targetObjects.Length;
            if (count > 1)
            {
                chips.Add((count + " texts", "lt-chip", "Every row writes to all of them, and a value that differs reads as —.", null));
            }
            else
            {
                foreach (string stat in m_Stats().Split('|'))
                    if (stat.Length > 0)
                        chips.Add((stat, "lt-chip", null, null));
                TextEngine engine = Engine;
                string level = engine != null ? LevelOf(engine.LastChanges) : null;
                if (level != null) chips.Add((level, "lt-chip", "How much of this text the last rebuild did again.", null));
            }
            string reason = m_Override();
            if (!string.IsNullOrEmpty(reason))
            {
                chips.Add((reason, "lt-chip lt-chip--orange", WhereShownComesFrom(), null));
                if (Engine != null && (Engine.TextOverride == LetteraTextOverride.RuntimeText || Engine.TextOverride == LetteraTextOverride.Both))
                    chips.Add(("Clear", "lt-chip lt-chip--click", "Forgets the text set by SetText, so that this shows what is written.", ClearRuntime));
            }
            var tags = new HashSet<string>();
            SerializedProperty styles = P("Styles");
            for (int i = 0; i < styles.arraySize; i++)
            {
                if (!(styles.GetArrayElementAtIndex(i).FindPropertyRelative("m_Matcher").managedReferenceValue is TagMatcher tag) || string.IsNullOrEmpty(tag.Name)) continue;
                if (!tags.Add(tag.Name)) chips.Add(("<" + tag.Name + "> is bound twice", "lt-chip lt-chip--orange", "The later one wins. The Styles card has the way out.", null));
            }
            return string.Join("|", chips.Select(c => c.Item1));
        }

        static string LevelOf(LetteraChanges changes)
        {
            if ((changes & LetteraChanges.Text) != 0) return "rebuilt: parse";
            if ((changes & LetteraChanges.Layout) != 0) return "rebuilt: layout";
            if ((changes & LetteraChanges.Positions) != 0) return "rebuilt: positions";
            if ((changes & LetteraChanges.Mesh) != 0) return "rebuilt: mesh";
            return null;
        }

        void ClearRuntime()
        {
            foreach (UnityEngine.Object target in m_Object.targetObjects)
            {
                if (!(target is ITextComponent text)) continue;
                text.SharedEngine.ClearRuntimeText();
                text.Dirty(DirtyLevel.Parse);
            }
        }

        // Each one in the words of what is wrong, and with the way out where one click can do it.
        void RefreshWarnings()
        {
            var items = new List<(string message, string fix, Action run)>(ProblemsNow());
            string key = string.Join("|", items.Select(i => i.message));
            if (key == m_WarningsShown) return;
            m_WarningsShown = key;
            m_Warnings.Clear();
            foreach ((string message, string fix, Action run) in items)
            {
                Action after = run;
                m_Warnings.Add(new LtFix(message, null, fix, after == null ? (Action)null : () =>
                {
                    after();
                    m_Object.Update();
                    RefreshHeader();
                }));
            }
        }

        internal IEnumerable<(string message, string fix, Action run)> ProblemsNow()
        {
            SerializedProperty sizeProperty = P("FontSize");
            if (P("Font").objectReferenceValue is LetteraFontAsset font && font.FontPixelsPerEm > 0 && !P("AutoSize").boolValue)
            {
                float size = sizeProperty.floatValue, cells = size / font.FontPixelsPerEm;
                if (Mathf.Abs(cells - Mathf.Round(cells)) > 0.001f)
                {
                    float even = Mathf.Max(1, Mathf.Round(cells)) * font.FontPixelsPerEm;
                    yield return ($"size is not a multiple of {font.FontPixelsPerEm}: pixels will not be even", "Use " + even.ToString("0.##"), () =>
                    {
                        m_Object.Update();
                        P("FontSize").floatValue = even;
                        m_Object.ApplyModifiedProperties();
                    });
                }
            }
            if (!P("AutoSize").boolValue && sizeProperty.floatValue < 1)
            {
                yield return ("A size under 1 draws too little to read.", "Use 1", () =>
                {
                    m_Object.Update();
                    P("FontSize").floatValue = 1;
                    m_Object.ApplyModifiedProperties();
                });
            }
            SerializedProperty budgets = P("FitBudgets");
            for (int i = 0; i < budgets.arraySize; i++)
            {
                if (budgets.GetArrayElementAtIndex(i).managedReferenceValue is FitByGlyphWidth width && width.Limit > 3)
                {
                    int index = i;
                    yield return ("glyph compression above 3% shows", "Use 3%", () =>
                    {
                        m_Object.Update();
                        P("FitBudgets").GetArrayElementAtIndex(index).FindPropertyRelative("m_Limit").floatValue = 3;
                        m_Object.ApplyModifiedProperties();
                    });
                }
            }
        }

        string FontLabel()
        {
            var font = P("Font").objectReferenceValue;
            var stack = P("FontStack").objectReferenceValue;
            if (font != null && stack != null) return $"{font.name} + {stack.name}";
            if (font != null) return font.name;
            if (stack != null) return stack.name;
            return "System font";
        }

        static string s_SystemFamily;

        // Which font a text really uses, and why that one.
        string FontWhy()
        {
            if (P("Font").hasMultipleDifferentValues || P("FontStack").hasMultipleDifferentValues) return "Fonts differ";
            if (P("Font").objectReferenceValue != null || P("FontStack").objectReferenceValue != null) return FontLabel();
            LetteraFontAsset project = LetteraSettings.Instance.DefaultFont;
            if (project != null) return project.name + ", project default, no font set";
            if (s_SystemFamily == null)
            {
                FontFace[] faces = LetteraSystemFonts.DefaultFaces();
                s_SystemFamily = faces != null && faces.Length > 0 && faces[0] != null && !string.IsNullOrEmpty(faces[0].Family) ? faces[0].Family : "System font";
            }
            return s_SystemFamily + ", system, no font set";
        }

        // ------------------------------------------------------------- text editor

        readonly List<(Label tool, Type effect, string name, string open, string close)> m_Toggles = new List<(Label, Type, string, string, string)>();
        VisualElement m_SourceBox, m_ShownBox;
        Label m_Coloured, m_Shown, m_ShownWhy;
        Label[] m_ModeItems;
        TextElement m_FieldText;
        int m_Mode;
        bool m_Tall;
        int m_SelectionA, m_SelectionB;
        bool m_HasSelection;
        string m_RichFor;
        Label m_NoTag;

        bool ColoredOn
        {
            get => EditorPrefs.GetBool(ColoredKey, true);
            set => EditorPrefs.SetBool(ColoredKey, value);
        }

        float PreviewSize
        {
            get => EditorPrefs.GetFloat(PreviewSizeKey, 12);
            set => EditorPrefs.SetFloat(PreviewSizeKey, value);
        }

        VisualElement BuildEditor()
        {
            // Tools about how the source is shown.
            VisualElement show = LtUI.El("lt-toolbar lt-toolbar--show");
            m_Tall = EditorPrefs.GetBool(TallKey, false);
            VisualElement expand = IconTool(LtIcon.Kind.Expand, "A taller text field", ToggleTall);
            expand.name = "lt-expand";
            expand.EnableInClassList("lt-tool--on", m_Tall);
            show.Add(expand);
            VisualElement modes = LtUI.Choice(new[] { "Written", "Resolved", "Runtime" }, () => m_Mode, SetMode, out m_ModeItems);
            modes.AddToClassList("lt-source-modes");
            modes.tooltip = "Which text this shows. Written is what is edited here. Resolved and Runtime read what a resolver or SetText made of it.";
            show.Add(modes);
            var size = new Slider(9, 24) { value = PreviewSize };
            size.AddToClassList("lt-source-size");
            size.tooltip = "The size of this field's text only. The component's own size stays.";
            size.RegisterValueChangedCallback(e =>
            {
                PreviewSize = e.newValue;
                ApplyPreviewSize();
            });
            show.Add(size);
            VisualElement eye = IconTool(LtIcon.Kind.Eye, "Colours the source: tags dim, parameters and formulas tinted, an unknown tag marked.", null);
            eye.name = "lt-eye";
            eye.EnableInClassList("lt-tool--on", ColoredOn);
            eye.RegisterCallback<ClickEvent>(_ =>
            {
                ColoredOn = !ColoredOn;
                eye.EnableInClassList("lt-tool--on", ColoredOn);
                m_RichFor = null;
                RefreshSource();
            });
            show.Add(eye);
            VisualElement scene = IconTool(LtIcon.Kind.Scene, "Edit In Scene: type into this text where it stands. F2 there does the same, and Esc ends it.", () => LetteraSceneEditView.Begin(m_Object.targetObjects));
            scene.name = "lt-scene-edit";
            show.Add(scene);

            // Formatting: on a selection it wraps, on none it turns a style on for the whole text.
            VisualElement format = LtUI.El("lt-toolbar");
            format.Add(Toggle("B", "lt-tool lt-tool--bold", "Bold", typeof(BoldEffect), "", ""));
            format.Add(Toggle("I", "lt-tool lt-tool--italic", "Italic", typeof(ItalicEffect), "", ""));
            format.Add(Toggle("U", "lt-tool", "Underline", typeof(UnderlineEffect), "", ""));
            format.Add(Toggle("S", "lt-tool", "Strikethrough", typeof(StrikethroughEffect), "", ""));
            format.Add(LtUI.El("lt-tool-gap"));
            format.Add(Toggle("Color", "lt-tool", "Color", typeof(ColorEffect), "=#FFCC00", ""));
            format.Add(Toggle("Size", "lt-tool", "Size", typeof(SizeEffect), "=150%", ""));
            format.Add(Toggle("Stroke", "lt-tool", "Stroke", typeof(StrokeEffect), "", ""));
            format.Add(Toggle("Shadow", "lt-tool", "Shadow", typeof(ShadowEffect), "", ""));
            format.Add(Toggle("Highlight", "lt-tool", "Highlight", typeof(HighlightEffect), "", ""));
            format.Add(Toggle("Link", "lt-tool", "Link", typeof(LinkEffect), "=https://", ""));
            format.Add(LtUI.El("lt-tool-gap"));
            format.Add(LtUI.Clickable("↵", "lt-tool", "Line break inside the paragraph", () => Wrap("<br>", "")));
            Label more = LtUI.Clickable("More", "lt-tool", "All styles, with a search", null);
            more.RegisterCallback<ClickEvent>(_ => OpenMore(more));
            format.Add(more);

            m_TextField = new TextField { multiline = true };
            m_TextField.AddToClassList("lt-source");
            m_TextField.selectAllOnFocus = false;
            m_TextField.selectAllOnMouseUp = false;
            m_TextField.BindProperty(P("Text"));
            m_TextField.EnableInClassList("lt-source--tall", m_Tall);
            m_FieldText = m_TextField.Q<TextElement>();
            m_Coloured = new Label { enableRichText = true, pickingMode = PickingMode.Ignore };
            m_Coloured.AddToClassList("lt-source__colors");
            if (m_FieldText?.parent != null)
            {
                m_FieldText.parent.Add(m_Coloured);
                m_FieldText.RegisterCallback<GeometryChangedEvent>(_ => PlaceColours());
            }
            m_TextField.RegisterValueChangedCallback(_ => RefreshSource());

            m_Shown = new Label { enableRichText = true };
            m_Shown.AddToClassList("lt-source__shown");
            m_Shown.selection.isSelectable = true;
            m_ShownWhy = LtUI.Text("", "lt-hint lt-source__why");
            m_ShownBox = LtUI.El("lt-source__shown-box", m_Shown, m_ShownWhy);
            m_ShownBox.style.display = DisplayStyle.None;
            m_SourceBox = LtUI.El("lt-source-box", m_TextField, m_ShownBox);

            m_NoTag = LtUI.Text("", "lt-hint lt-source__why");
            m_NoTag.style.display = DisplayStyle.None;
            m_SceneEditWhy = LtUI.Text("", "lt-chip lt-chip--orange");
            ApplyPreviewSize();
            RefreshSource();
            return LtUI.El("lt-editor", show, format, m_SourceBox, m_NoTag, m_SceneEditWhy);
        }

        static VisualElement IconTool(LtIcon.Kind kind, string tip, Action click)
        {
            VisualElement tool = LtUI.El("lt-tool lt-tool--icon", new LtIcon(kind));
            tool.tooltip = tip;
            if (click != null) tool.RegisterCallback<ClickEvent>(_ => click());
            return tool;
        }

        void ToggleTall()
        {
            m_Tall = !m_Tall;
            EditorPrefs.SetBool(TallKey, m_Tall);
            m_TextField.EnableInClassList("lt-source--tall", m_Tall);
            m_Root.Q("lt-expand")?.EnableInClassList("lt-tool--on", m_Tall);
        }

        void ApplyPreviewSize()
        {
            float size = PreviewSize;
            if (m_FieldText != null) m_FieldText.style.fontSize = size;
            m_Coloured.style.fontSize = size;
            m_Shown.style.fontSize = size;
            m_RichFor = null;
        }

        void PlaceColours()
        {
            Rect box = m_FieldText.layout;
            m_Coloured.style.left = box.x;
            m_Coloured.style.top = box.y;
            m_Coloured.style.width = box.width;
            m_Coloured.style.height = box.height;
            m_Coloured.style.paddingLeft = m_FieldText.resolvedStyle.paddingLeft;
            m_Coloured.style.paddingTop = m_FieldText.resolvedStyle.paddingTop;
            m_Coloured.style.paddingRight = m_FieldText.resolvedStyle.paddingRight;
            m_Coloured.style.paddingBottom = m_FieldText.resolvedStyle.paddingBottom;
            m_Coloured.style.unityTextAlign = m_FieldText.resolvedStyle.unityTextAlign;
        }

        bool KnownTag(string name) => m_KnownTags.Contains(name);

        readonly HashSet<string> m_KnownTags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int m_KnownFor = -1;

        void RefreshKnownTags()
        {
            TextEngine engine = Engine;
            if (engine == null) return;
            engine.EnsureParsed();
            int hash = 17;
            IReadOnlyList<LetteraStyle> styles = engine.EffectiveStyles;
            for (int i = 0; i < styles.Count; i++) hash = hash * 31 + (styles[i]?.GetHashCode() ?? 0);
            if (hash == m_KnownFor) return;
            m_KnownFor = hash;
            m_KnownTags.Clear();
            foreach (string tag in LetteraVocabulary.Tags(styles)) m_KnownTags.Add(tag);
            m_RichFor = null;
        }

        internal string ShownSource(int mode)
        {
            TextEngine engine = Engine;
            if (mode == 0 || engine == null) return P("Text").stringValue ?? "";
            if (mode == 1) return engine.ResolvedText ?? "";
            bool runtime = engine.TextOverride == LetteraTextOverride.RuntimeText || engine.TextOverride == LetteraTextOverride.Both;
            return runtime ? engine.RawText : "";
        }

        internal string WhereShownComesFrom()
        {
            TextEngine engine = Engine;
            if (engine == null) return "";
            string resolver = engine.Resolver != null ? engine.Resolver.GetType().Name : null;
            switch (engine.TextOverride)
            {
                case LetteraTextOverride.RuntimeText: return "Set by SetText from a script. What is written here comes back when that is cleared.";
                case LetteraTextOverride.Resolver: return "Made from what is written by the resolver " + resolver + ".";
                case LetteraTextOverride.Both: return "Set by SetText, then changed by the resolver " + resolver + ".";
                default: return "Nothing overrides what is written here.";
            }
        }

        void SetMode(int mode)
        {
            m_Mode = mode;
            m_TextField.style.display = mode == 0 ? DisplayStyle.Flex : DisplayStyle.None;
            m_ShownBox.style.display = mode == 0 ? DisplayStyle.None : DisplayStyle.Flex;
            m_RichFor = null;
            RefreshSource();
        }

        internal int Mode
        {
            get => m_Mode;
            set
            {
                SetMode(value);
                for (int i = 0; i < m_ModeItems.Length; i++) m_ModeItems[i].EnableInClassList("lt-seg__item--on", i == value);
            }
        }

        // The field keeps its own text and caret. Its letters go clear, and a label of the same size and box draws them in colour above it.
        void RefreshSource()
        {
            if (m_Coloured == null) return;
            RefreshKnownTags();
            TextEngine engine = Engine;
            LetteraTextOverride over = engine != null ? engine.TextOverride : LetteraTextOverride.None;
            bool runtime = over == LetteraTextOverride.RuntimeText || over == LetteraTextOverride.Both;
            bool resolved = over == LetteraTextOverride.Resolver || over == LetteraTextOverride.Both;
            m_ModeItems[1].EnableInClassList("lt-seg__item--over", resolved);
            m_ModeItems[2].EnableInClassList("lt-seg__item--over", runtime);

            bool dark = EditorGUIUtility.isProSkin;
            if (m_Mode == 0)
            {
                string value = m_TextField.value;
                bool mixed = P("Text").hasMultipleDifferentValues;
                string key = value + "|" + ColoredOn;
                if (key != m_RichFor)
                {
                    m_RichFor = key;
                    string rich = ColoredOn && !mixed ? LetteraSourceColor.Rich(value, KnownTag, dark) : null;
                    m_Coloured.text = rich ?? "";
                    m_Coloured.style.display = rich != null ? DisplayStyle.Flex : DisplayStyle.None;
                    if (m_FieldText != null)
                    {
                        if (rich != null) m_FieldText.style.color = new Color(0, 0, 0, 0);
                        else m_FieldText.style.color = StyleKeyword.Null;
                    }
                }
                return;
            }

            string shown = ShownSource(m_Mode);
            string shownKey = m_Mode + "|" + shown;
            m_ShownWhy.text = m_Mode == 2 && !runtime ? "Nothing has been set at run time." : WhereShownComesFrom();
            if (shownKey == m_RichFor) return;
            m_RichFor = shownKey;
            string richShown = LetteraSourceColor.Rich(shown, KnownTag, dark);
            m_Shown.enableRichText = richShown != null;
            m_Shown.text = richShown ?? shown;
        }

        void OpenMore(VisualElement anchor)
        {
            var preview = new LetteraStylePreview(m_Object.targetObject);
            LetteraStylePicker.Open(anchor.worldBound, template =>
            {
                m_StyleList.Add(template);
                RefreshTools();
            }, new LetteraPickerHooks
            {
                Applied = m_StyleList.IsApplied,
                Remove = template =>
                {
                    m_StyleList.RemoveApplied(template);
                    RefreshTools();
                },
                Hover = template => preview.Show(template),
                HoverEnd = () => preview.Clear(),
                Closed = () => preview.End(),
            });
        }

        Label Toggle(string text, string classes, string name, Type effect, string open, string close)
        {
            Label tool = LtUI.Clickable(text, classes, name, null);
            tool.RegisterCallback<PointerDownEvent>(_ => RememberSelection(), TrickleDown.TrickleDown);
            tool.RegisterCallback<ClickEvent>(e =>
            {
                if (e.altKey) OpenStyleOf(effect);
                else Format(effect, open);
            });
            m_Toggles.Add((tool, effect, name, open, close));
            return tool;
        }

        // A click moves the focus away, and the selection with it: it is read before.
        void RememberSelection()
        {
            m_SelectionA = Mathf.Min(m_TextField.cursorIndex, m_TextField.selectIndex);
            m_SelectionB = Mathf.Max(m_TextField.cursorIndex, m_TextField.selectIndex);
            m_HasSelection = m_SelectionA != m_SelectionB;
        }

        /// <summary>What a tool does: with a selection it wraps it in the tag this text binds to the effect, and with none it turns a whole-text style over.</summary>
        internal void Format(Type effect, string parameter)
        {
            if (!m_HasSelection)
                RememberSelection();
            if (!m_HasSelection)
            {
                ToggleWholeText(effect);
                return;
            }
            m_HasSelection = false;
            string tag = BoundTag(effect);
            if (tag == null)
            {
                string name = LetteraStyleList.Pretty(effect.Name, "Effect");
                m_NoTag.text = "No tag does " + name + " on this text. Turn on Global Preset, or add a style with a tag.";
                m_NoTag.style.display = DisplayStyle.Flex;
                return;
            }
            m_NoTag.style.display = DisplayStyle.None;
            WrapRange(m_SelectionA, m_SelectionB, "<" + tag + parameter + ">", "</" + tag + ">");
        }

        string BoundTag(Type effect)
        {
            TextEngine engine = Engine;
            if (engine == null) return null;
            engine.EnsureParsed();
            return LetteraVocabulary.TagFor(effect, engine.EffectiveStyles);
        }

        void OpenStyleOf(Type effect)
        {
            int found = FindWholeText(effect);
            if (found < 0) found = FindAny(effect);
            if (found >= 0) m_StyleList.OpenRow(found);
        }

        int FindAny(Type effect)
        {
            SerializedProperty styles = P("Styles");
            for (int i = 0; i < styles.arraySize; i++)
                if (styles.GetArrayElementAtIndex(i).FindPropertyRelative("m_Effect").managedReferenceValue?.GetType() == effect)
                    return i;
            return -1;
        }

        int FindWholeText(Type effect)
        {
            SerializedProperty styles = P("Styles");
            for (int i = 0; i < styles.arraySize; i++)
            {
                SerializedProperty style = styles.GetArrayElementAtIndex(i);
                if (style.FindPropertyRelative("m_Matcher").managedReferenceValue is WholeTextMatcher && style.FindPropertyRelative("m_Effect").managedReferenceValue?.GetType() == effect) return i;
            }
            return -1;
        }

        internal void ToggleWholeText(Type effect)
        {
            m_Object.Update();
            SerializedProperty styles = P("Styles");
            int found = FindWholeText(effect);
            if (found >= 0)
            {
                styles.DeleteArrayElementAtIndex(found);
            }
            else
            {
                styles.InsertArrayElementAtIndex(styles.arraySize);
                SerializedProperty added = styles.GetArrayElementAtIndex(styles.arraySize - 1);
                added.FindPropertyRelative("m_Matcher").managedReferenceValue = new WholeTextMatcher();
                added.FindPropertyRelative("m_Effect").managedReferenceValue = Activator.CreateInstance(effect);
                added.FindPropertyRelative("m_Enabled").boolValue = true;
                SerializedProperty parameter = added.FindPropertyRelative("m_DefaultParameter");
                if (parameter != null) parameter.stringValue = "";
            }
            m_Object.ApplyModifiedProperties();
            m_StyleList?.Rebuild();
            RefreshTools();
        }

        void RefreshTools()
        {
            foreach ((Label tool, Type effect, string name, string open, string _) in m_Toggles)
            {
                tool.EnableInClassList("lt-tool--on", FindWholeText(effect) >= 0);
                string tag = BoundTag(effect);
                string wraps = tag != null ? "On a selection: wraps it in <" + tag + open + ">." : "On a selection: no tag does this on this text yet.";
                tool.tooltip = name + ". " + wraps + " With none selected: turns it on or off for the whole text. Alt opens its row.";
            }
        }

        // Surrounds the field's selection with markup.
        void Wrap(string open, string close)
        {
            string value = m_TextField.value ?? "";
            int a = Mathf.Clamp(Mathf.Min(m_TextField.cursorIndex, m_TextField.selectIndex), 0, value.Length);
            int b = Mathf.Clamp(Mathf.Max(m_TextField.cursorIndex, m_TextField.selectIndex), 0, value.Length);
            WrapRange(a, b, open, close);
        }

        void WrapRange(int a, int b, string open, string close)
        {
            string value = m_TextField.value ?? "";
            a = Mathf.Clamp(a, 0, value.Length);
            b = Mathf.Clamp(b, a, value.Length);
            m_TextField.value = value.Substring(0, a) + open + value.Substring(a, b - a) + close + value.Substring(b);
            m_TextField.Focus();
            m_TextField.SelectRange(a + open.Length, b + open.Length);
        }

        // ------------------------------------------------------------- font

        static readonly (string code, string name)[] s_Languages =
        {
            ("en", "English"), ("ru", "Russian"), ("uk", "Ukrainian"), ("de", "German"), ("fr", "French"), ("es", "Spanish"), ("it", "Italian"), ("pt", "Portuguese"), ("pl", "Polish"), ("tr", "Turkish"),
            ("ar", "Arabic"), ("fa", "Persian"), ("ur", "Urdu"), ("he", "Hebrew"), ("hi", "Hindi"), ("bn", "Bengali"), ("ta", "Tamil"), ("th", "Thai"), ("vi", "Vietnamese"), ("id", "Indonesian"),
            ("ja", "Japanese"), ("ko", "Korean"), ("zh-Hans", "Chinese, Simplified"), ("zh-Hant", "Chinese, Traditional"), ("el", "Greek"), ("hy", "Armenian"), ("ka", "Georgian"), ("my", "Burmese"), ("km", "Khmer"), ("lo", "Lao"),
        };

        VisualElement BuildFontPage()
        {
            VisualElement page = new VisualElement();
            page.Add(new LtObject("Font", P("Font"), typeof(LetteraFontAsset), "The primary face. Empty uses the default font of the project, then of the operating system."));
            page.Add(new LtObject("Stack", P("FontStack"), typeof(LetteraFontStack), "Families and fallbacks tried in order."));
            page.Add(new LtEnum("Render Mode", P("RenderMode"), "SDF is one channel and small. MSDF keeps corners sharp, for a little more memory."));

            var fixedSize = new LtNumber("Size", P("FontSize"), "Size in pixels on a Canvas, in units in the world.", 1).Limit(0.01, 100000);
            VisualElement autoSize = LtUI.El("lt-row--pair lt-auto-sizes",
                new LtNumber("Min", P("MinFontSize"), "The smallest it may go.", 1).Limit(0.01, 100000),
                new LtNumber("Max", P("MaxFontSize"), "The largest it may go.", 1).Limit(0.01, 100000),
                CurrentSize());
            VisualElement sizeRow = LtUI.El("lt-row lt-row--pair", fixedSize, autoSize, LtUI.InlineSwitch("Auto", P("AutoSize"), "Auto Size: the largest size at which the text fits its box."));
            ShowWhen(fixedSize, P("AutoSize"), p => !p.boolValue);
            ShowWhen(autoSize, P("AutoSize"), p => p.boolValue);
            page.Add(sizeRow);
            VisualElement budgets = BuildFitBudgets();
            ShowWhen(budgets, P("AutoSize"), p => p.boolValue);
            page.Add(budgets);

            SerializedProperty color = m_Object.FindProperty("m_Color");
            if (color != null) page.Add(new LtColor("Color", color));

            page.Add(new LtEnum("Direction", P("BaseDirection"), "Auto picks each paragraph's direction from its first strong character.", new[] { "Auto", "LTR", "RTL" }));
            page.Add(LanguageRow());
            return page;
        }

        VisualElement CurrentSize()
        {
            Label value = LtUI.Text("", "lt-auto-current");
            value.tooltip = "The size it fits at now.";
            void Refresh()
            {
                TextEngine engine = Engine;
                value.text = engine != null && engine.CurrentFontSize > 0 ? "= " + engine.CurrentFontSize.ToString("0.#", CultureInfo.InvariantCulture) : "";
            }
            value.schedule.Execute(Refresh).Every(500);
            Refresh();
            return value;
        }

        VisualElement LanguageRow()
        {
            SerializedProperty language = P("Language").Copy();
            var field = new TextField { isDelayed = true };
            field.AddToClassList("lt-text-input");
            field.AddToClassList("lt-language__value");
            field.BindProperty(language);
            VisualElement open = LtUI.El("lt-object__button", new LtIcon(LtIcon.Kind.Chevron));
            open.tooltip = "Common languages";
            open.RegisterCallback<ClickEvent>(_ =>
            {
                var items = new List<(string, string, object)>();
                foreach ((string code, string name) in s_Languages) items.Add((name, code, code));
                LtPopup.Show(field, new Vector2(Mathf.Max(field.worldBound.width, 240), 300), () => new LtChoiceList(items, chosen =>
                {
                    language.serializedObject.Update();
                    language.stringValue = (string)chosen;
                    language.serializedObject.ApplyModifiedProperties();
                    LtPopup.CloseCurrent();
                }));
            });
            return LtUI.Row("Language", LtUI.El("lt-language", field, open), "BCP 47 tag such as ar, ja or zh-Hans. Selects localized glyph forms and font preferences.", language);
        }

        // Ways to save room before the font size drops, spent in this order.
        VisualElement BuildFitBudgets()
        {
            var holder = new VisualElement();
            void Rebuild()
            {
                holder.Clear();
                m_Object.Update();
                SerializedProperty budgets = P("FitBudgets");
                for (int i = 0; i < budgets.arraySize; i++)
                {
                    int index = i;
                    SerializedProperty budget = budgets.GetArrayElementAtIndex(i);
                    if (budget.managedReferenceValue == null) continue;
                    VisualElement row = LtUI.El("lt-budget");
                    foreach (SerializedProperty child in LetteraStyleList.Children(budget))
                        row.Add(LtUI.Field(LetteraStyleList.Pretty(budget.managedReferenceValue.GetType().Name, "") + " limit", child.Copy()));
                    row.Add(LtUI.Clickable("×", "lt-icon-btn lt-icon-btn--danger", "Remove", () =>
                    {
                        m_Object.Update();
                        P("FitBudgets").DeleteArrayElementAtIndex(index);
                        m_Object.ApplyModifiedProperties();
                        Rebuild();
                    }));
                    holder.Add(row);
                }
                Label add = LtUI.Clickable("+ Fit Budget", "lt-add lt-add--small", "Save room before reducing the size", () =>
                {
                    var menu = new GenericMenu();
                    foreach (Type type in new[] { typeof(FitByTracking), typeof(FitByGlyphWidth), typeof(FitByLineHeight) })
                    {
                        Type chosen = type;
                        menu.AddItem(new GUIContent(type.Name), false, () =>
                        {
                            m_Object.Update();
                            SerializedProperty list = P("FitBudgets");
                            list.InsertArrayElementAtIndex(list.arraySize);
                            list.GetArrayElementAtIndex(list.arraySize - 1).managedReferenceValue = Activator.CreateInstance(chosen);
                            m_Object.ApplyModifiedProperties();
                            Rebuild();
                        });
                    }
                    menu.ShowAsContext();
                });
                holder.Add(add);
            }
            Rebuild();
            return holder;
        }

        // ------------------------------------------------------------- layout

        VisualElement BuildLayoutPage()
        {
            VisualElement page = new VisualElement();
            VisualElement wrapRow = LtUI.El("lt-row lt-row--pair",
                LtUI.InlineSwitch("Wrap", P("WordWrap"), "Word Wrap: lines break to fit the box."),
                new LtEnum("Overflow", P("Overflow"), "What happens to text past the box.", new[] { "Visible", "Ellipsis", "Truncate" }));
            page.Add(wrapRow);
            var position = new LtNumber("Remove At", P("OverflowPosition"), "Where text goes away: 0 start, 0.5 middle, 1 end.", 0.01f).Limit(0, 1);
            ShowWhen(position, P("Overflow"), p => p.enumValueIndex != (int)LetteraOverflow.Visible);
            page.Add(position);

            VisualElement align = LtUI.El("lt-align",
                LtUI.IconSegments(P("HorizontalAlignment"), new[]
                {
                    (LtIcon.Kind.AlignStart, "Start: follows the paragraph's direction"),
                    (LtIcon.Kind.AlignCenter, "Center"),
                    (LtIcon.Kind.AlignEnd, "End: follows the paragraph's direction"),
                    (LtIcon.Kind.AlignLeft, "Left"),
                    (LtIcon.Kind.AlignRight, "Right"),
                    (LtIcon.Kind.AlignJustify, "Justify"),
                }),
                LtUI.IconSegments(P("VerticalAlignment"), new[]
                {
                    (LtIcon.Kind.AlignTop, "Top"),
                    (LtIcon.Kind.AlignMiddle, "Middle"),
                    (LtIcon.Kind.AlignBottom, "Bottom"),
                }));
            page.Add(LtUI.Row("Align", align, "Horizontal and vertical alignment."));

            var value = new LtNumber("", P("LineHeightValue"), "The multiplier of the mode.", 0.05f);
            value.AddToClassList("lt-line-value");
            ShowWhen(value, P("LineHeightMode"), p => p.enumValueIndex != (int)LetteraLineHeightMode.Default);
            page.Add(LtUI.Row("Line Height", LtUI.El("lt-line-height",
                    new LtEnum("", P("LineHeightMode"), "Scaled follows the font size; Content fits every font on the line; Primary uses the primary font only.", new[] { "Project", "Scaled", "Content", "Primary" }),
                    value),
                "Line height: mode and value."));
            page.Add(new LtEnum("Extra Space", P("LeadingDistribution"), "Where a line's extra height goes.", new[] { "Half", "Above", "Below" }));
            page.Add(new LtBox("Padding", P("Padding"), "Room inside the box: top, right, bottom, left."));
            return page;
        }

        // ------------------------------------------------------------- styles

        VisualElement BuildStylesPage()
        {
            VisualElement page = new VisualElement();
            m_StyleList = new LetteraStyleList(m_Object, m_Data.propertyPath + ".Styles");
            page.Add(m_StyleList.Build());

            Label count = LtUI.Text("", "lt-chip");
            void RefreshCount()
            {
                LetteraStylePreset global = LetteraSettings.Instance.GlobalStylePreset;
                count.text = (global != null ? global.Styles.Count : LetteraDefaultStyles.Styles.Count) + " styles";
                count.tooltip = global != null ? "From the project's " + global.name + "." : "Lettera's own tags: nothing to set up.";
            }
            RefreshCount();
            VisualElement globalRow = LtUI.SwitchRow("Global Preset", P("UseGlobalStylePreset"), "Apply the project's styles after this text's own. Off shows all tags literally.");
            globalRow.Add(count);

            VisualElement vocabulary = LtUI.El("lt-vocab");
            LetteraStylePreset preset = LetteraSettings.Instance.GlobalStylePreset;
            foreach (string tag in LetteraVocabulary.Writable(preset != null ? preset.Styles : LetteraDefaultStyles.Styles))
            {
                string name = tag;
                vocabulary.Add(LtUI.Clickable("<" + tag + ">", "lt-vocab__tag", "Puts <" + tag + "> around what is selected in the text.", () =>
                {
                    m_Sections[0].Open = true;
                    Wrap("<" + name + ">", "</" + name + ">");
                }));
            }
            ShowWhen(vocabulary, P("UseGlobalStylePreset"), p => p.boolValue);

            page.Add(LtUI.Card("Shared",
                globalRow,
                vocabulary,
                PresetList()));
            return page;
        }

        VisualElement PresetList() => new LtList("Style Presets", P("StylePresets"), (element, index) => new LtObject("", element, typeof(LetteraStylePreset)), "No shared style lists on this text.");

        internal static void ShowWhen(VisualElement element, SerializedProperty property, Func<SerializedProperty, bool> condition)
        {
            SerializedProperty bound = property.Copy();
            void Refresh(SerializedProperty p) => element.style.display = condition(bound) ? DisplayStyle.Flex : DisplayStyle.None;
            Refresh(bound);
            element.TrackPropertyValue(bound, Refresh);
        }
    }

    /// <summary>The base of a text component's inspector. A subclass of a text component gets the same page by deriving from this, and adds to the Render card.</summary>
    public abstract class LetteraTextEditorBase : UnityEditor.Editor
    {
        /// <summary>The header's name.</summary>
        protected abstract string InspectorTitle { get; }

        /// <summary>Fills the Render card, which differs by component.</summary>
        protected abstract void BuildRender(VisualElement page);

        public override VisualElement CreateInspectorGUI()
        {
            var text = (ITextComponent)target;
            return new LetteraTextInspector(serializedObject, InspectorTitle,
                () => StatsOf(text.SharedEngine.GlyphCount, text.SharedEngine.Lines.Count, text.SharedEngine.CurrentFontSize > 0 ? text.SharedEngine.CurrentFontSize : serializedObject.FindProperty("m_Data").FindPropertyRelative("FontSize").floatValue),
                () => OverrideOf(text.SharedEngine.TextOverride),
                BuildRender).Build();
        }

        internal static string StatsOf(int glyphs, int lines, float size) =>
            $"{glyphs} glyphs|{lines} {(lines == 1 ? "line" : "lines")}|{size:0.##} size";

        internal static string OverrideOf(LetteraTextOverride reason)
        {
            switch (reason)
            {
                case LetteraTextOverride.RuntimeText: return "runtime text";
                case LetteraTextOverride.Resolver: return "resolved text";
                case LetteraTextOverride.Both: return "runtime + resolved text";
                default: return null;
            }
        }

        internal static VisualElement Holder(VisualElement control)
        {
            var holder = new VisualElement();
            holder.style.flexDirection = FlexDirection.Row;
            holder.Add(control);
            return holder;
        }

        /// <summary>A row that says how a text takes pointers, by the route it has.</summary>
        protected VisualElement RouteRow() => new LtEnum("Pointer", serializedObject.FindProperty("m_Route"), "What a pointer on this text does: passes through, hits the whole box, or only its ranges.");

        protected VisualElement Prop(string label, string name, string tooltip = null) => LtUI.Field(label, serializedObject.FindProperty(name), tooltip);
    }

    [CustomEditor(typeof(LetteraText)), CanEditMultipleObjects]
    internal sealed class LetteraTextEditor : LetteraTextEditorBase
    {
        protected override string InspectorTitle => "Lettera Text";

        void OnSceneGUI()
        {
            var text = (LetteraText)target;
            LetteraSceneHandles.Draw(text, text.Padding, text.Styles, text.FontSize);
            if (text.raycastPadding != Vector4.zero) LetteraSceneHandles.DrawRaycast(text, text.raycastPadding);
        }

        protected override void BuildRender(VisualElement page)
        {
            page.Add(LtUI.El("lt-row lt-row--pair",
                LtUI.InlineSwitch("Raycast", serializedObject.FindProperty("m_RaycastTarget"), "Raycast Target: this text takes pointers."),
                LtUI.InlineSwitch("Maskable", serializedObject.FindProperty("m_Maskable"), "A Mask or Rect Mask over it clips it.")));
            page.Add(new LtBox("Raycast Padding", serializedObject.FindProperty("m_RaycastPadding"), "How far the area that takes pointers lies inside the box, or outside when negative. The Scene view shows it as an outline."));
            page.Add(RouteRow());
        }
    }

    [CustomEditor(typeof(LetteraWorldText)), CanEditMultipleObjects]
    internal sealed class LetteraWorldTextEditor : LetteraTextEditorBase
    {
        protected override string InspectorTitle => "Lettera World Text";

        void OnSceneGUI()
        {
            var text = (LetteraWorldText)target;
            LetteraSceneHandles.Draw(text, text.Padding, text.Styles, text.FontSize);
        }

        protected override void BuildRender(VisualElement page)
        {
            SerializedProperty P(string name) => serializedObject.FindProperty(name);
            page.Add(LetteraSortingLayerRow.Build(P("m_SortingLayerID")));
            page.Add(LtUI.El("lt-row lt-row--pair",
                new LtNumber("Order", P("m_SortingOrder"), "Order within the sorting layer."),
                new LtNumber("Batch", P("m_BatchGroup"), "Batch group: texts in different groups never merge.")));
            page.Add(LtUI.SwitchRow("Standalone", P("m_Standalone"), "Draw through an own renderer, sorted by its own distance to the camera."));
            page.Add(LtUI.SwitchRow("Lit", P("m_Lit"), "Light the text with the ambient and main light."));
            page.Add(LtUI.SwitchRow("Cast Shadows", P("m_CastShadows")));
            page.Add(LtUI.SwitchRow("Raycast Target", P("m_RaycastTarget")));
            page.Add(RouteRow());
            page.Add(new LtFix("Nothing can click this text: no camera has a World Raycaster.", NeedsRaycaster, "Add To Camera", AddRaycaster, "Adds a World Raycaster to the main camera."));
        }

        bool NeedsRaycaster()
        {
            foreach (UnityEngine.Object o in targets)
            {
                if (!(o is LetteraWorldText text) || !text.RaycastTarget || !text.gameObject.scene.IsValid()) continue;
                if (text.GetComponent<LetteraSelectable>() == null && LetteraMotionScan.Of(text.Styles).Links == 0) continue;
                if (UnityEngine.Object.FindAnyObjectByType<LetteraWorldRaycaster>() == null) return true;
            }
            return false;
        }

        static void AddRaycaster()
        {
            Camera camera = Camera.main != null ? Camera.main : UnityEngine.Object.FindAnyObjectByType<Camera>();
            if (camera != null) Undo.AddComponent<LetteraWorldRaycaster>(camera.gameObject);
        }
    }

    /// <summary>The sorting layer, by name.</summary>
    internal static class LetteraSortingLayerRow
    {
        public static VisualElement Build(SerializedProperty id)
        {
            SerializedProperty bound = id.Copy();
            Label value = LtUI.Text("", "lt-enum__value");
            void Refresh(SerializedProperty _) => value.text = bound.hasMultipleDifferentValues ? "—" : SortingLayer.IDToName(bound.intValue);
            value.RegisterCallback<ClickEvent>(_ =>
            {
                var items = new List<(string, string, object)>();
                foreach (SortingLayer layer in SortingLayer.layers) items.Add((layer.name, null, layer.id));
                LtPopup.Show(value, new Vector2(Mathf.Max(value.worldBound.width, 200), Mathf.Min(44 + 24 * items.Count, 300)), () => new LtChoiceList(items, chosen =>
                {
                    bound.serializedObject.Update();
                    bound.intValue = (int)chosen;
                    bound.serializedObject.ApplyModifiedProperties();
                    Refresh(bound);
                    LtPopup.CloseCurrent();
                }, -1, items.Count > 8));
            });
            Refresh(bound);
            value.TrackPropertyValue(bound, Refresh);
            return LtUI.Row("Sorting Layer", value, "Which layer this draws in, by the names set in Project Settings > Tags and Layers.", bound);
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace FirstSymbol.Lettera.Editor
{
    /// <summary>The custom inspector both text components share: header, text editor and tabbed settings.</summary>
    internal sealed class LetteraTextInspector
    {
        static int s_LastTab;

        readonly SerializedObject m_Object;
        readonly SerializedProperty m_Data;
        readonly Func<string> m_Stats;
        readonly Func<string> m_Override;
        readonly string m_Title;
        readonly Action<VisualElement> m_BuildRenderPage;
        readonly VisualElement m_Root = LtUI.Root();
        readonly List<VisualElement> m_Pages = new List<VisualElement>();
        readonly List<VisualElement> m_Tabs = new List<VisualElement>();
        Label m_Subtitle;
        VisualElement m_Chips;
        VisualElement m_StyleList;
        Label m_StyleBadge;
        TextField m_TextField;

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

        public VisualElement Build()
        {
            m_Root.Add(BuildHeader());
            m_Root.Add(BuildEditor());

            VisualElement tabs = LtUI.El("lt-tabs");
            m_Root.Add(tabs);
            AddTab(tabs, "Font", BuildFontPage());
            AddTab(tabs, "Layout", BuildLayoutPage());
            m_StyleBadge = LtUI.Text("0", "lt-tab__badge");
            AddTab(tabs, "Styles", BuildStylesPage(), m_StyleBadge);
            VisualElement render = LtUI.El("lt-page");
            m_BuildRenderPage(render);
            AddTab(tabs, "Render", render);
            SelectTab(Mathf.Clamp(s_LastTab, 0, m_Pages.Count - 1));
            m_Root.Add(LetteraEditingMenus.InteractionCard(m_Object.targetObjects));

            m_Root.schedule.Execute(RefreshHeader).Every(400);
            RefreshHeader();
            return m_Root;
        }

        void AddTab(VisualElement tabs, string name, VisualElement page, VisualElement badge = null)
        {
            int index = m_Pages.Count;
            VisualElement tab = LtUI.El("lt-tab", LtUI.Text(name, "lt-tab__label"));
            if (badge != null) tab.Add(badge);
            tab.RegisterCallback<ClickEvent>(_ => SelectTab(index));
            tabs.Add(tab);
            m_Tabs.Add(tab);
            page.AddToClassList("lt-page");
            m_Pages.Add(page);
            m_Root.Add(page);
        }

        void SelectTab(int index)
        {
            s_LastTab = index;
            for (int i = 0; i < m_Pages.Count; i++)
            {
                m_Pages[i].style.display = i == index ? DisplayStyle.Flex : DisplayStyle.None;
                m_Tabs[i].EnableInClassList("lt-tab--active", i == index);
            }
        }

        // ------------------------------------------------------------- header

        VisualElement BuildHeader()
        {
            m_Subtitle = LtUI.Text("", "lt-subtitle");
            m_Chips = LtUI.El("lt-chips");
            VisualElement info = LtUI.El("lt-header__info", LtUI.Text(m_Title, "lt-title"), m_Subtitle, m_Chips);
            return LtUI.El("lt-header", LtUI.Text("Lt", "lt-mark"), info);
        }

        void RefreshHeader()
        {
            if (m_Object.targetObject == null) return;
            m_Object.UpdateIfRequiredOrScript();
            string fontName = FontLabel();
            m_Subtitle.text = fontName;

            m_Chips.Clear();
            foreach (string stat in m_Stats().Split('|'))
                m_Chips.Add(LtUI.Text(stat, "lt-chip"));
            string reason = m_Override();
            if (!string.IsNullOrEmpty(reason)) m_Chips.Add(LtUI.Text(reason, "lt-chip lt-chip--orange"));
            foreach (string warning in Warnings()) m_Chips.Add(LtUI.Text(warning, "lt-chip lt-chip--orange"));
            if (m_StyleBadge != null) m_StyleBadge.text = P("Styles").arraySize.ToString();
        }

        // What a user would otherwise find out only by looking closely at the result.
        IEnumerable<string> Warnings()
        {
            if (P("Font").objectReferenceValue is LetteraFontAsset font && font.FontPixelsPerEm > 0 && !P("AutoSize").boolValue)
            {
                float size = P("FontSize").floatValue, cells = size / font.FontPixelsPerEm;
                if (Mathf.Abs(cells - Mathf.Round(cells)) > 0.001f) yield return $"size is not a multiple of {font.FontPixelsPerEm}: pixels will not be even";
            }
            SerializedProperty budgets = P("FitBudgets");
            for (int i = 0; i < budgets.arraySize; i++)
                if (budgets.GetArrayElementAtIndex(i).managedReferenceValue is FitByGlyphWidth width && width.Limit > 3)
                    yield return "glyph compression above 3% shows";

            var tags = new HashSet<string>();
            SerializedProperty styles = P("Styles");
            for (int i = 0; i < styles.arraySize; i++)
            {
                if (!(styles.GetArrayElementAtIndex(i).FindPropertyRelative("m_Matcher").managedReferenceValue is TagMatcher tag) || string.IsNullOrEmpty(tag.Name)) continue;
                if (!tags.Add(tag.Name)) yield return $"<{tag.Name}> is bound twice";
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

        // ------------------------------------------------------------- text editor

        VisualElement BuildEditor()
        {
            VisualElement toolbar = LtUI.El("lt-toolbar");
            toolbar.Add(LtUI.Clickable("B", "lt-tool lt-tool--bold", "Bold", () => Wrap("<b>", "</b>")));
            toolbar.Add(LtUI.Clickable("I", "lt-tool lt-tool--italic", "Italic", () => Wrap("<i>", "</i>")));
            toolbar.Add(LtUI.Clickable("U", "lt-tool", "Underline", () => Wrap("<u>", "</u>")));
            toolbar.Add(LtUI.Clickable("S", "lt-tool", "Strikethrough", () => Wrap("<s>", "</s>")));
            toolbar.Add(LtUI.El("lt-tool-gap"));
            toolbar.Add(LtUI.Clickable("Color", "lt-tool", "Color", () => Wrap("<color=#FFCC00>", "</color>")));
            toolbar.Add(LtUI.Clickable("Size", "lt-tool", "Relative size", () => Wrap("<size=150%>", "</size>")));
            toolbar.Add(LtUI.Clickable("Outline", "lt-tool", "Outline", () => Wrap("<outline=#000000,0.08>", "</outline>")));
            toolbar.Add(LtUI.Clickable("Shadow", "lt-tool", "Shadow", () => Wrap("<shadow>", "</shadow>")));
            toolbar.Add(LtUI.El("lt-tool-gap"));
            toolbar.Add(LtUI.Clickable("↵", "lt-tool", "Line break inside the paragraph", () => Wrap("<br>", "")));

            m_TextField = new TextField { multiline = true };
            m_TextField.AddToClassList("lt-text-input");
            m_TextField.selectAllOnFocus = false;
            m_TextField.selectAllOnMouseUp = false;
            m_TextField.BindProperty(P("Text"));
            return LtUI.El("lt-editor", toolbar, m_TextField);
        }

        // Surrounds the field's selection with markup.
        void Wrap(string open, string close)
        {
            string value = m_TextField.value ?? "";
            int a = Mathf.Clamp(Mathf.Min(m_TextField.cursorIndex, m_TextField.selectIndex), 0, value.Length);
            int b = Mathf.Clamp(Mathf.Max(m_TextField.cursorIndex, m_TextField.selectIndex), 0, value.Length);
            m_TextField.value = value.Substring(0, a) + open + value.Substring(a, b - a) + close + value.Substring(b);
            m_TextField.Focus();
            m_TextField.SelectRange(a + open.Length, b + open.Length);
        }

        // ------------------------------------------------------------- pages

        VisualElement BuildFontPage()
        {
            VisualElement page = new VisualElement();
            page.Add(LtUI.Card("Font",
                LtUI.Field("Font", P("Font"), "The primary face. Empty uses the default font of the project, then of the operating system."),
                LtUI.Field("Font Stack", P("FontStack"), "Families and fallbacks tried in order."),
                LtUI.Field("Render Mode", P("RenderMode"))));

            VisualElement autoRows = new VisualElement();
            autoRows.Add(LtUI.Field("Min Size", P("MinFontSize")));
            autoRows.Add(LtUI.Field("Max Size", P("MaxFontSize")));
            autoRows.Add(BuildFitBudgets());
            VisualElement sizeRow = LtUI.Row("Size", LtUI.SizeSlider(P("FontSize"), 1, 200));
            ShowWhen(autoRows, P("AutoSize"), p => p.boolValue);
            ShowWhen(sizeRow, P("AutoSize"), p => !p.boolValue);
            page.Add(LtUI.Card("Size",
                LtUI.Row("Auto Size", WithSpacer(LtUI.Switch(P("AutoSize"))), "Uses the largest size at which the text fits its box."),
                sizeRow, autoRows));

            page.Add(LtUI.Card("Language",
                LtUI.Row("Direction", LtUI.Segments(P("BaseDirection"), "Auto", "LTR", "RTL"), "Auto picks each paragraph's direction from its first strong character."),
                LtUI.Field("Language", P("Language"), "BCP 47 tag such as ar, ja or zh-Hans. Selects localized glyph forms and font preferences.")));
            return page;
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

        VisualElement BuildLayoutPage()
        {
            VisualElement page = new VisualElement();
            page.Add(LtUI.Card("Alignment",
                LtUI.Row("Horizontal", LtUI.IconSegments(P("HorizontalAlignment"), new[]
                {
                    (LtIcon.Kind.AlignStart, "Start: follows the paragraph's direction"),
                    (LtIcon.Kind.AlignCenter, "Center"),
                    (LtIcon.Kind.AlignEnd, "End: follows the paragraph's direction"),
                    (LtIcon.Kind.AlignLeft, "Left"),
                    (LtIcon.Kind.AlignRight, "Right"),
                    (LtIcon.Kind.AlignJustify, "Justify"),
                })),
                LtUI.Row("Vertical", LtUI.IconSegments(P("VerticalAlignment"), new[]
                {
                    (LtIcon.Kind.AlignTop, "Top"),
                    (LtIcon.Kind.AlignMiddle, "Middle"),
                    (LtIcon.Kind.AlignBottom, "Bottom"),
                }))));

            VisualElement position = LtUI.Row("Remove At", LtUI.SizeSlider(P("OverflowPosition"), 0, 1), "Where text goes away: 0 start, 0.5 middle, 1 end.");
            ShowWhen(position, P("Overflow"), p => p.enumValueIndex != (int)LetteraOverflow.Visible);
            page.Add(LtUI.Card("Wrapping",
                LtUI.Row("Word Wrap", WithSpacer(LtUI.Switch(P("WordWrap")))),
                LtUI.Row("Overflow", LtUI.Segments(P("Overflow"), "Visible", "Ellipsis", "Truncate")),
                position));

            VisualElement value = LtUI.Field("Multiplier", P("LineHeightValue"));
            ShowWhen(value, P("LineHeightMode"), p => p.enumValueIndex != (int)LetteraLineHeightMode.Default);
            page.Add(LtUI.Card("Line Height",
                LtUI.Row("Mode", LtUI.Segments(P("LineHeightMode"), "Project", "Scaled", "Content", "Primary"),
                    "Scaled follows the font size; Content fits every font on the line; Primary uses the primary font only."),
                value,
                LtUI.Row("Extra Space", LtUI.Segments(P("LeadingDistribution"), "Half", "Above", "Below"))));

            page.Add(LtUI.Card("Box", LtUI.Row("Padding", LtUI.Padding(P("Padding")))));
            return page;
        }

        VisualElement BuildStylesPage()
        {
            VisualElement page = new VisualElement();
            page.Add(new LetteraStyleList(m_Object, m_Data.propertyPath + ".Styles").Build());

            VisualElement vocabulary = LtUI.El("lt-vocab");
            foreach (string tag in new[]
            {
                "b", "i", "size", "color", "alpha", "font", "lang", "u", "s", "br", "noparse", "dir", "ellipsis", "truncate",
                "fill", "stroke", "outline", "shadow", "glow", "inner-shadow", "extrude", "filter", "union", "warp", "mark",
                "uppercase", "lowercase", "smallcaps", "sup", "sub", "otf", "axes", "size-match", "ruby",
                "tracking", "word-spacing", "cell", "line-height", "para-spacing", "line-gap", "nobr", "li", "indent", "align", "trim", "curve",
                "link", "a", "divider",
            })
                vocabulary.Add(LtUI.Text("<" + tag + ">", "lt-vocab__tag"));
            ShowWhen(vocabulary, P("UseGlobalStylePreset"), p => p.boolValue);

            page.Add(LtUI.Card("Shared Styles",
                LtUI.Row("Global Preset", WithSpacer(LtUI.Switch(P("UseGlobalStylePreset"))), "Apply the project's styles after this text's own. Off shows all tags literally."),
                vocabulary,
                PresetList()));
            return page;
        }

        VisualElement PresetList()
        {
            var list = new PropertyField(P("StylePresets"), "Style Presets");
            list.BindProperty(P("StylePresets"));
            list.tooltip = "Style lists shared between texts.";
            return list;
        }

        // A switch sits at the left of its row's field area.
        static VisualElement WithSpacer(VisualElement control)
        {
            var holder = new VisualElement();
            holder.style.flexDirection = FlexDirection.Row;
            holder.Add(control);
            return holder;
        }

        static void ShowWhen(VisualElement element, SerializedProperty property, Func<SerializedProperty, bool> condition)
        {
            void Refresh(SerializedProperty p) => element.style.display = condition(p) ? DisplayStyle.Flex : DisplayStyle.None;
            Refresh(property);
            element.TrackPropertyValue(property.Copy(), Refresh);
        }
    }

    [CustomEditor(typeof(LetteraText)), CanEditMultipleObjects]
    internal sealed class LetteraTextEditor : UnityEditor.Editor
    {
        void OnSceneGUI()
        {
            var text = (LetteraText)target;
            LetteraSceneHandles.Draw(text, text.Padding, text.Styles, text.FontSize);
        }

        public override VisualElement CreateInspectorGUI()
        {
            var text = (LetteraText)target;
            return new LetteraTextInspector(serializedObject, "Lettera Text",
                () => StatsOf(text.GlyphCount, text.Lines.Count, text.CurrentFontSize),
                () => OverrideOf(text.TextOverrideReason),
                page =>
                {
                    page.Add(LtUI.Card("Appearance", LtUI.Field("Color", serializedObject.FindProperty("m_Color"))));
                    page.Add(LtUI.Card("Canvas",
                        LtUI.Row("Raycast Target", Holder(LtUI.Switch(serializedObject.FindProperty("m_RaycastTarget")))),
                        LtUI.Row("Maskable", Holder(LtUI.Switch(serializedObject.FindProperty("m_Maskable"))))));
                }).Build();
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
    }

    [CustomEditor(typeof(LetteraWorldText)), CanEditMultipleObjects]
    internal sealed class LetteraWorldTextEditor : UnityEditor.Editor
    {
        void OnSceneGUI()
        {
            var text = (LetteraWorldText)target;
            LetteraSceneHandles.Draw(text, text.Padding, text.Styles, text.FontSize);
        }

        public override VisualElement CreateInspectorGUI()
        {
            var text = (LetteraWorldText)target;
            SerializedProperty P(string name) => serializedObject.FindProperty(name);
            return new LetteraTextInspector(serializedObject, "Lettera World Text",
                () => LetteraTextEditor.StatsOf(text.GlyphCount, text.Lines.Count, text.CurrentFontSize),
                () => LetteraTextEditor.OverrideOf(text.TextOverrideReason),
                page =>
                {
                    page.Add(LetteraMathInspector.Card(text.Engine.CollectFormulas));
                    page.Add(LtUI.Card("Appearance", LtUI.Field("Color", P("m_Color"))));
                    page.Add(LtUI.Card("Sorting",
                        LtUI.Field("Sorting Layer ID", P("m_SortingLayerID")),
                        LtUI.Field("Order", P("m_SortingOrder")),
                        LtUI.Field("Batch Group", P("m_BatchGroup"), "Texts in different groups never merge.")));
                    page.Add(LtUI.Card("Renderer",
                        LtUI.Row("Standalone", LetteraTextEditor.Holder(LtUI.Switch(P("m_Standalone"))), "Draw through an own renderer, sorted by its own distance to the camera."),
                        LtUI.Row("Lit", LetteraTextEditor.Holder(LtUI.Switch(P("m_Lit"))), "Light the text with the ambient and main light."),
                        LtUI.Row("Cast Shadows", LetteraTextEditor.Holder(LtUI.Switch(P("m_CastShadows")))),
                        LtUI.Row("Raycast Target", LetteraTextEditor.Holder(LtUI.Switch(P("m_RaycastTarget"))))));
                }).Build();
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace FirstSymbol.Lettera.Editor
{
    /// <summary>An editable list of style cards, for a component's styles and for style preset assets.</summary>
    internal sealed class LetteraStyleList
    {
        // What a new style shows straight away. Other parameters appear once changed, or picked from its menu.
        static readonly Dictionary<Type, string[]> s_Essential = new Dictionary<Type, string[]>
        {
            { typeof(FillEffect), new[] { "m_Paint" } },
            { typeof(StrokeEffect), new[] { "m_Paint", "m_Width" } },
            { typeof(OutlineEffect), new[] { "m_Color", "m_Width" } },
            { typeof(ShadowEffect), new[] { "m_Color", "m_Offset" } },
            { typeof(GlowEffect), new[] { "m_Paint", "m_Radius" } },
            { typeof(InnerShadowEffect), new[] { "m_Paint", "m_Offset" } },
            { typeof(ExtrudeEffect), new[] { "m_Offset", "m_NearColor", "m_FarColor" } },
            { typeof(UnderlineEffect), new[] { "m_Paint", "m_LineStyle" } },
            { typeof(StrikethroughEffect), new[] { "m_Paint", "m_LineStyle" } },
            { typeof(HighlightEffect), new[] { "m_Paint", "m_Look" } },
            { typeof(ColorEffect), new[] { "m_Color" } },
            { typeof(BoldEffect), new[] { "m_Weight" } },
            { typeof(SizeEffect), new[] { "m_Size" } },
            { typeof(AlphaEffect), new[] { "m_Alpha" } },
            { typeof(FontEffect), new[] { "m_Family" } },
            { typeof(TrackingEffect), new[] { "m_Amount" } },
            { typeof(WordSpacingEffect), new[] { "m_Amount" } },
            { typeof(CellWidthEffect), new[] { "m_Width" } },
            { typeof(LineHeightEffect), new[] { "m_Value" } },
            { typeof(ParagraphSpacingEffect), new[] { "m_After", "m_Before" } },
            { typeof(LineGapEffect), new[] { "m_Amount" } },
            { typeof(IndentEffect), new[] { "m_Amount" } },
            { typeof(AlignEffect), new[] { "m_Alignment" } },
            { typeof(CurveEffect), new[] { "m_Radius" } },
            { typeof(BoxTrimEffect), new[] { "m_TopEdge", "m_BottomEdge" } },
            { typeof(MaterialEffect), new[] { "m_Material", "m_Order" } },
            { typeof(FilterEffect), new[] { "m_Filter", "m_Intensity" } },
            { typeof(WarpEffect), new[] { "m_Amplitude", "m_Scale" } },
            { typeof(PresetEffect), new[] { "m_Preset" } },
            { typeof(CompositeEffect), new[] { "m_Children" } },
        };

        /// <summary>The essentials of an effect: the table's, or for a custom effect the fields marked [LetteraEssential].</summary>
        internal static string[] EssentialOf(Type effect)
        {
            if (s_Essential.TryGetValue(effect, out string[] named)) return named;
            var marked = new List<string>();
            for (Type at = effect; at != null && at != typeof(object); at = at.BaseType)
                foreach (FieldInfo field in at.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                    if (field.IsDefined(typeof(LetteraEssentialAttribute), false))
                        marked.Add(field.Name);
            return marked.Count > 0 ? marked.ToArray() : null;
        }

        static LetteraStyle s_Clipboard;

        readonly SerializedObject m_Object;
        readonly string m_Path;
        readonly Dictionary<int, HashSet<string>> m_Added = new Dictionary<int, HashSet<string>>();
        readonly HashSet<int> m_Open = new HashSet<int>();
        LtList m_List;
        VisualElement m_Problems;

        public LetteraStyleList(SerializedObject serialized, string propertyPath)
        {
            m_Object = serialized;
            m_Path = propertyPath;
        }

        SerializedProperty Styles => m_Object.FindProperty(m_Path);

        public LtList List => m_List;

        public VisualElement Build()
        {
            var root = new VisualElement();
            m_List = new LtList("Styles", Styles, BuildCard, "No styles here yet. Add one, or rely on the shared styles.", OpenPicker, "+ Add Style");
            m_List.HeaderTools.Add(LtUI.Clickable("Paste", "lt-add lt-add--small lt-add--quiet", "Paste a copied style", Paste));
            m_List.RowToText = RowText;
            m_List.PasteText = PasteFrom;
            m_List.Changed += () =>
            {
                m_Added.Clear();
                m_Open.Clear();
                RefreshProblems();
            };
            m_List.RowMenuExtra = (menu, index) =>
            {
                menu.AddItem(new GUIContent("Copy Style"), false, () => Copy(Styles.GetArrayElementAtIndex(index)));
                if (index > 0) menu.AddItem(new GUIContent("Move Up"), false, () => Move(index, index - 1));
                if (index < Styles.arraySize - 1) menu.AddItem(new GUIContent("Move Down"), false, () => Move(index, index + 1));
            };
            root.Add(m_List);
            m_Problems = LtUI.El("lt-problems");
            root.Add(m_Problems);
            RefreshProblems();
            return root;
        }

        void OpenPicker(LtList list)
        {
            var preview = new LetteraStylePreview(m_Object.targetObject);
            LetteraStylePicker.Open(list.worldBound, chosen => Add(chosen), new LetteraPickerHooks
            {
                Applied = IsApplied,
                Remove = RemoveApplied,
                Hover = template => preview.Show(template),
                HoverEnd = () => preview.Clear(),
                Closed = () => preview.End(),
            });
        }

        /// <summary>A picker's entry for a whole-text style is marked when the text has it, and a click takes it away.</summary>
        internal bool IsApplied(LetteraStyleTemplate template) => FindApplied(template) >= 0;

        int FindApplied(LetteraStyleTemplate template)
        {
            if (template.Driven != LetteraStyleTemplates.WholeText || template.Effect == null) return -1;
            Type effect = template.Effect().GetType();
            SerializedProperty styles = Styles;
            for (int i = 0; i < styles.arraySize; i++)
            {
                SerializedProperty style = styles.GetArrayElementAtIndex(i);
                if (style.FindPropertyRelative("m_Matcher").managedReferenceValue is WholeTextMatcher && style.FindPropertyRelative("m_Effect").managedReferenceValue?.GetType() == effect) return i;
            }
            return -1;
        }

        internal void RemoveApplied(LetteraStyleTemplate template)
        {
            int found = FindApplied(template);
            if (found >= 0) Remove(found);
        }

        public void Add(LetteraStyleTemplate template)
        {
            m_Object.Update();
            SerializedProperty styles = Styles;
            styles.InsertArrayElementAtIndex(styles.arraySize);
            SerializedProperty style = styles.GetArrayElementAtIndex(styles.arraySize - 1);
            style.FindPropertyRelative("m_Matcher").managedReferenceValue = template.Matcher?.Invoke();
            style.FindPropertyRelative("m_Effect").managedReferenceValue = template.Effect?.Invoke();
            style.FindPropertyRelative("m_Enabled").boolValue = true;
            style.FindPropertyRelative("m_DefaultParameter").stringValue = "";
            m_Object.ApplyModifiedProperties();
            Undo.SetCurrentGroupName("Add Lettera Style");
            m_Open.Add(styles.arraySize - 1);
            m_List?.OpenEnclosing();
            Rebuild();
        }

        void Paste()
        {
            if (s_Clipboard != null) Write(s_Clipboard.Clone());
            else PasteFrom(EditorGUIUtility.systemCopyBuffer);
        }

        // A row travels as text, so that it can go through an email and come back whole.
        internal static string RowText(SerializedProperty style) => JsonUtility.ToJson(Read(style));

        internal void PasteFrom(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            foreach (string line in text.Split('\n'))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                try
                {
                    var style = JsonUtility.FromJson<LetteraStyle>(line);
                    if (style != null && (style.Matcher != null || style.Effect != null)) Write(style);
                }
                catch (ArgumentException)
                {
                    // Whatever else was on the clipboard is not a style.
                }
            }
        }

        void Write(LetteraStyle copy)
        {
            m_Object.Update();
            SerializedProperty styles = Styles;
            styles.InsertArrayElementAtIndex(styles.arraySize);
            SerializedProperty style = styles.GetArrayElementAtIndex(styles.arraySize - 1);
            style.FindPropertyRelative("m_Matcher").managedReferenceValue = copy.Matcher;
            style.FindPropertyRelative("m_Effect").managedReferenceValue = copy.Effect;
            style.FindPropertyRelative("m_Enabled").boolValue = copy.Enabled;
            style.FindPropertyRelative("m_DefaultParameter").stringValue = copy.DefaultParameter;
            m_Object.ApplyModifiedProperties();
            Rebuild();
        }

        static LetteraStyle Read(SerializedProperty style) => new LetteraStyle
        {
            Matcher = style.FindPropertyRelative("m_Matcher").managedReferenceValue as LetteraMatcher,
            Effect = style.FindPropertyRelative("m_Effect").managedReferenceValue as LetteraEffect,
            Enabled = style.FindPropertyRelative("m_Enabled").boolValue,
            DefaultParameter = style.FindPropertyRelative("m_DefaultParameter").stringValue,
        };

        void Copy(SerializedProperty style) => s_Clipboard = Read(style).Clone();

        public void Rebuild()
        {
            m_List?.Rebuild();
            RefreshProblems();
        }

        /// <summary>Opens a row's parameters and brings it into view, for a tool's Alt+click.</summary>
        internal void OpenRow(int index)
        {
            m_Open.Add(index);
            m_List?.OpenEnclosing();
            Rebuild();
            if (m_List != null)
            {
                m_List.Selection.Click(index);
                m_List.Rebuild();
                VisualElement row = m_List.Query(className: "lt-list__row").AtIndex(index);
                if (row != null) row.schedule.Execute(() => row.GetFirstAncestorOfType<ScrollView>()?.ScrollTo(row));
            }
        }

        // Things in this list that will not do what they seem to, with the way out of each.
        internal IEnumerable<(string path, string message, string fix, Action run)> Problems()
        {
            SerializedProperty styles = Styles;
            var tags = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < styles.arraySize; i++)
            {
                SerializedProperty style = styles.GetArrayElementAtIndex(i);
                int index = i;
                object matcher = style.FindPropertyRelative("m_Matcher").managedReferenceValue;
                object effect = style.FindPropertyRelative("m_Effect").managedReferenceValue;
                string path = "Styles " + (i + 1) + ": ";
                if (matcher == null)
                {
                    yield return (path + "no matcher", "This style matches nothing, so it does nothing.", "Remove", () => Remove(index));
                    continue;
                }
                if (matcher is TagMatcher tag && string.IsNullOrEmpty(tag.Name))
                    yield return (path + "tag", "This tag has no name, so nothing can write it.", "Open", () => OpenRow(index));
                if (matcher is TagMatcher named && !string.IsNullOrEmpty(named.Name))
                {
                    if (tags.TryGetValue(named.Name, out int first))
                        yield return (path + "<" + named.Name + ">", "<" + named.Name + "> is bound twice, here and at row " + (first + 1) + ": this one wins.", "Remove", () => Remove(index));
                    else tags[named.Name] = i;
                }
                if (effect == null && !(matcher is EscapeMatcher) && !(matcher is NoParseMatcher) && !(matcher is LineBreakMatcher))
                    yield return (path + MatcherLabel(style.FindPropertyRelative("m_Matcher")), "This style has no effect, so it finds its text and changes nothing.", "Open", () => OpenRow(index));
            }
        }

        void RefreshProblems()
        {
            if (m_Problems == null) return;
            m_Problems.Clear();
            m_Object.UpdateIfRequiredOrScript();
            foreach ((string path, string message, string fix, Action run) in Problems())
                m_Problems.Add(new LtFix(path + ". " + message, null, fix, () =>
                {
                    run();
                    RefreshProblems();
                }));
        }

        VisualElement BuildCard(SerializedProperty style, int index)
        {
            SerializedProperty matcher = style.FindPropertyRelative("m_Matcher");
            SerializedProperty effect = style.FindPropertyRelative("m_Effect");
            SerializedProperty enabled = style.FindPropertyRelative("m_Enabled");

            VisualElement card = LtUI.El("lt-style");
            VisualElement body = LtUI.El("lt-style__body");
            body.style.display = m_Open.Contains(index) ? DisplayStyle.Flex : DisplayStyle.None;

            void RefreshEnabled(SerializedProperty p) => card.EnableInClassList("lt-style--off", !p.boolValue);
            RefreshEnabled(enabled);
            card.TrackPropertyValue(enabled, RefreshEnabled);

            VisualElement header = LtUI.El("lt-style__header");
            VisualElement chevron = LtUI.El("lt-icon-btn lt-style__chevron", new LtIcon(LtIcon.Kind.Chevron));
            chevron.tooltip = "Shows this style's parameters. A double click on the row does the same.";
            void Paint() => chevron.EnableInClassList("lt-style__chevron--open", body.style.display == DisplayStyle.Flex);
            void Toggle()
            {
                bool open = body.style.display == DisplayStyle.None;
                body.style.display = open ? DisplayStyle.Flex : DisplayStyle.None;
                if (open) m_Open.Add(index);
                else m_Open.Remove(index);
                Paint();
            }
            chevron.RegisterCallback<ClickEvent>(e =>
            {
                e.StopPropagation();
                Toggle();
            });
            Paint();
            header.Add(chevron);
            header.Add(LtUI.Switch(enabled));
            object matcherValue = matcher.managedReferenceValue;
            object effectValue = effect.managedReferenceValue;
            header.Add(LtUI.Text(MatcherLabel(matcher), "lt-style__tag"));
            header.Add(LtUI.Text(effectValue == null ? "No effect" : Pretty(effectValue.GetType().Name, "Effect"), "lt-style__type"));
            string words = Summary(effectValue);
            if (words.Length > 0) header.Add(LtUI.Text(words, "lt-style__what"));
            header.Add(LtUI.El("lt-style__space"));
            header.Add(LtUI.Text(DrivenBy(matcherValue), "lt-chip lt-style__driven"));
            VisualElement dot = LtUI.El("lt-style__dot");
            if (effectValue != null) dot.style.backgroundColor = LetteraStyleTemplates.ColorOfEffect(effectValue.GetType());
            header.Add(dot);
            header.Add(LtUI.Clickable("×", "lt-icon-btn lt-icon-btn--danger", "Remove", () => Remove(index)));
            header.RegisterCallback<ClickEvent>(e =>
            {
                if (e.clickCount == 2) Toggle();
            });
            card.Add(header);

            bool tagBound = matcherValue is TagMatcher || matcherValue is VoidTagMatcher;
            body.Add(TypePicker("Matcher", matcher, typeof(LetteraMatcher), "Matcher"));
            AddAllChildren(body, matcher);
            body.Add(TypePicker("Effect", effect, typeof(LetteraEffect), "Effect"));
            AddEffectParameters(body, effect, index, tagBound);
            body.Add(LtUI.Field("Defaults", style.FindPropertyRelative("m_DefaultParameter"), "Parameters used when a tag gives none, in the tag's syntax."));
            card.Add(body);
            return card;
        }

        internal static string DrivenBy(object matcher)
        {
            switch (matcher)
            {
                case null: return "none";
                case WholeTextMatcher _: return "whole text";
                case TagMatcher _: case VoidTagMatcher _: return "tag";
                case MarkdownMarkerMatcher _: case MarkdownListMatcher _: case MarkdownLinkMatcher _: return "markdown";
                case BareUrlMatcher _: case TriggerWordMatcher _: case LiteralMatcher _: return "auto";
                default: return "rule";
            }
        }

        // The few effects whose main value says more than their name.
        internal static string Summary(object effect)
        {
            switch (effect)
            {
                case ColorEffect color: return "#" + ColorUtility.ToHtmlStringRGB(color.Color);
                case SizeEffect size: return size.Size;
                default: return "";
            }
        }

        // Essential parameters when the effect is not driven by tags, anything changed from its default, and what was picked from the menu.
        void AddEffectParameters(VisualElement parent, SerializedProperty effect, int index, bool tagBound)
        {
            object value = effect.managedReferenceValue;
            if (value == null) return;
            object defaults = null;
            try { defaults = Activator.CreateInstance(value.GetType()); }
            catch (MissingMethodException) { }
            string[] essential = EssentialOf(value.GetType());
            m_Added.TryGetValue(index, out HashSet<string> added);

            var hidden = new List<SerializedProperty>();
            int shown = 0;
            foreach (SerializedProperty child in Children(effect))
            {
                bool show = (!tagBound && (essential == null ? shown < 2 : essential.Contains(child.name)))
                            || (added != null && added.Contains(child.name))
                            || (defaults != null && Differs(value, defaults, child.name));
                if (show)
                {
                    parent.Add(LetteraPaintField.For(child, value));
                    shown++;
                }
                else
                {
                    hidden.Add(child);
                }
            }
            if (hidden.Count == 0) return;
            VisualElement more = LtUI.Clickable("+ Parameter", "lt-add lt-add--small", "Show another parameter of this effect", () =>
            {
                var menu = new GenericMenu();
                foreach (SerializedProperty child in hidden)
                {
                    string name = child.name;
                    menu.AddItem(new GUIContent(child.displayName), false, () =>
                    {
                        if (!m_Added.TryGetValue(index, out HashSet<string> set)) m_Added[index] = set = new HashSet<string>();
                        set.Add(name);
                        m_Open.Add(index);
                        Rebuild();
                    });
                }
                menu.ShowAsContext();
            });
            parent.Add(more);
        }

        // Fields compare as their JSON, so a nested paint counts as changed only when something in it changed.
        static bool Differs(object value, object defaults, string fieldName)
        {
            FieldInfo field = FindField(value.GetType(), fieldName);
            if (field == null) return false;
            object a = field.GetValue(value), b = field.GetValue(defaults);
            if (a == null || b == null) return a != b;
            if (a is UnityEngine.Object || b is UnityEngine.Object) return !Equals(a, b);
            if (a.GetType().IsClass && !(a is string)) return JsonUtility.ToJson(a) != JsonUtility.ToJson(b);
            return !a.Equals(b);
        }

        static FieldInfo FindField(Type type, string name)
        {
            for (; type != null; type = type.BaseType)
            {
                FieldInfo field = type.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.DeclaredOnly);
                if (field != null) return field;
            }
            return null;
        }

        internal static string MatcherLabel(SerializedProperty matcher)
        {
            object value = matcher.managedReferenceValue;
            switch (value)
            {
                case TagMatcher tag: return "<" + tag.Name + ">";
                case VoidTagMatcher tag: return "<" + tag.Name + ">";
                case MarkdownMarkerMatcher marker: return marker.Marker;
                case TriggerWordMatcher trigger: return trigger.Trigger;
                case null: return "None";
                default: return Pretty(value.GetType().Name, "Matcher");
            }
        }

        VisualElement TypePicker(string label, SerializedProperty reference, Type baseType, string suffix)
        {
            Type[] types = TypeCache.GetTypesDerivedFrom(baseType).Where(t => !t.IsAbstract && !t.IsNested && t.GetConstructor(Type.EmptyTypes) != null).OrderBy(t => t.Name).ToArray();
            var names = new List<string> { "None" };
            names.AddRange(types.Select(t => Pretty(t.Name, suffix)));
            object value = reference.managedReferenceValue;
            int current = value == null ? 0 : Array.IndexOf(types, value.GetType()) + 1;
            var popup = new PopupField<string>(names, Mathf.Max(current, 0));
            SerializedProperty bound = reference.Copy();
            popup.RegisterValueChangedCallback(e =>
            {
                int chosen = names.IndexOf(e.newValue);
                bound.serializedObject.Update();
                bound.managedReferenceValue = chosen <= 0 ? null : Activator.CreateInstance(types[chosen - 1]);
                bound.serializedObject.ApplyModifiedProperties();
                Rebuild();
            });
            return LtUI.Row(label, popup);
        }

        internal static IEnumerable<SerializedProperty> Children(SerializedProperty reference)
        {
            if (reference.propertyType == SerializedPropertyType.ManagedReference && reference.managedReferenceValue == null) yield break;
            SerializedProperty end = reference.GetEndProperty();
            SerializedProperty child = reference.Copy();
            bool enter = true;
            while (child.NextVisible(enter) && !SerializedProperty.EqualContents(child, end))
            {
                enter = false;
                yield return child.Copy();
            }
        }

        static void AddAllChildren(VisualElement parent, SerializedProperty reference)
        {
            object value = reference.managedReferenceValue;
            foreach (SerializedProperty child in Children(reference)) parent.Add(LetteraPaintField.For(child, value));
        }

        void Move(int from, int to)
        {
            SerializedProperty styles = Styles;
            if (to < 0 || to >= styles.arraySize) return;
            m_Object.Update();
            styles.MoveArrayElement(from, to);
            m_Object.ApplyModifiedProperties();
            m_Added.Clear();
            m_Open.Clear();
            Rebuild();
        }

        void Remove(int index)
        {
            m_Object.Update();
            Styles.DeleteArrayElementAtIndex(index);
            m_Object.ApplyModifiedProperties();
            m_Added.Clear();
            m_Open.Clear();
            Rebuild();
        }

        internal static string Pretty(string typeName, string suffix) =>
            typeName.EndsWith(suffix, StringComparison.Ordinal) ? typeName.Substring(0, typeName.Length - suffix.Length) : typeName;
    }

    /// <summary>The controls for a paint: its source, gradient stops and how it lies on the text. Rows that would do nothing stay hidden.</summary>
    internal static class LetteraPaintField
    {
        /// <summary>The right control for an effect's field.</summary>
        public static VisualElement For(SerializedProperty property, object owner)
        {
            switch (property.type)
            {
                case nameof(LetteraPaintChoice): return Choice(property, owner as LetteraLayerEffect);
                case nameof(LetteraPaint): return Paint(property);
                case nameof(LetteraGradient): return Gradient(property);
            }
            if (IsLength(property, owner)) return new LtUnit(property.displayName, property.Copy(), property.tooltip);
            return LtUI.Field(property.displayName, property.Copy(), property.tooltip);
        }

        // A string that holds a length gets a number and a unit. What holds "auto" until set says so by its type.
        static bool IsLength(SerializedProperty property, object owner)
        {
            if (property.propertyType != SerializedPropertyType.String) return false;
            if (owner is CellWidthEffect && property.name == "m_Width") return true;
            return LetteraLength.TryParse(property.stringValue, out LetteraLength length) && length.Kind != LetteraLength.Unit.Bare;
        }

        // A nested polymorphic field, such as a color filter: a type menu and its fields.
        static VisualElement ManagedReference(SerializedProperty property)
        {
            SerializedProperty bound = property.Copy();
            var holder = new VisualElement();
            void Refresh()
            {
                holder.Clear();
                Type baseType = ManagedBaseType(bound);
                Type[] types = baseType == null ? new Type[0] : TypeCache.GetTypesDerivedFrom(baseType).Where(t => !t.IsAbstract && t.GetConstructor(Type.EmptyTypes) != null).OrderBy(t => t.Name).ToArray();
                var names = new List<string> { "None" };
                names.AddRange(types.Select(t => t.Name));
                object value = bound.managedReferenceValue;
                int current = value == null ? 0 : Array.IndexOf(types, value.GetType()) + 1;
                var popup = new PopupField<string>(names, Mathf.Max(current, 0));
                popup.RegisterValueChangedCallback(e =>
                {
                    int chosen = names.IndexOf(e.newValue);
                    bound.serializedObject.Update();
                    bound.managedReferenceValue = chosen <= 0 ? null : Activator.CreateInstance(types[chosen - 1]);
                    bound.serializedObject.ApplyModifiedProperties();
                    Refresh();
                });
                holder.Add(LtUI.Row(bound.displayName, popup));
                foreach (SerializedProperty child in LetteraStyleList.Children(bound)) holder.Add(For(child, bound.managedReferenceValue));
            }
            Refresh();
            return holder;
        }

        static Type ManagedBaseType(SerializedProperty property)
        {
            string[] parts = property.managedReferenceFieldTypename.Split(' ');
            return parts.Length == 2 ? Type.GetType(parts[1] + ", " + parts[0]) : null;
        }

        static VisualElement Choice(SerializedProperty choice, LetteraLayerEffect effect)
        {
            SerializedProperty kind = choice.FindPropertyRelative("Kind").Copy();
            SerializedProperty inline = choice.FindPropertyRelative("Inline").Copy();
            SerializedProperty swatch = choice.FindPropertyRelative("SwatchName").Copy();
            SerializedProperty settings = choice.FindPropertyRelative("Settings").Copy();

            VisualElement holder = LtUI.El("lt-paint");
            var panel = new VisualElement();
            void Refresh(SerializedProperty _ = null)
            {
                panel.Clear();
                switch ((LetteraPaintChoiceKind)kind.enumValueIndex)
                {
                    case LetteraPaintChoiceKind.Inline:
                        panel.Add(Paint(inline));
                        break;
                    case LetteraPaintChoiceKind.Named:
                        panel.Add(NamedSwatch(swatch, effect));
                        panel.Add(Settings(settings, LetteraPaintSourceKind.Gradient, LetteraPaintShape.Inherit));
                        break;
                }
            }
            holder.Add(LtUI.Row("Paint", LtUI.Segments(kind, "Default", "Inline", "Named")));
            holder.Add(panel);
            Refresh();
            holder.TrackPropertyValue(kind, Refresh);
            return holder;
        }

        static VisualElement NamedSwatch(SerializedProperty swatch, LetteraLayerEffect effect)
        {
            var names = new List<string>();
            List<LetteraSwatch> list = null;
            if (effect != null)
            {
                switch (effect.Catalog)
                {
                    case LetteraPaintCatalog.Own: list = effect.OwnSwatches; break;
                    case LetteraPaintCatalog.Library: list = effect.Library != null ? effect.Library.Swatches : null; break;
                    default: list = LetteraSettings.Instance.Paints != null ? LetteraSettings.Instance.Paints.Swatches : null; break;
                }
            }
            if (list != null) names.AddRange(list.Where(s => s != null && !string.IsNullOrEmpty(s.Name)).Select(s => s.Name));
            if (names.Count == 0) return LtUI.Field("Swatch", swatch, "A name in the paint library this effect reads.");
            string current = swatch.stringValue;
            if (!names.Contains(current)) names.Insert(0, current);
            var popup = new PopupField<string>(names, names.IndexOf(current));
            SerializedProperty bound = swatch.Copy();
            popup.RegisterValueChangedCallback(e =>
            {
                bound.serializedObject.Update();
                bound.stringValue = e.newValue;
                bound.serializedObject.ApplyModifiedProperties();
            });
            return LtUI.Row("Swatch", popup);
        }

        public static VisualElement Paint(SerializedProperty paint)
        {
            SerializedProperty source = paint.FindPropertyRelative("Source").Copy();
            SerializedProperty color = paint.FindPropertyRelative("Color").Copy();
            SerializedProperty gradient = paint.FindPropertyRelative("Gradient").Copy();
            SerializedProperty texture = paint.FindPropertyRelative("Texture").Copy();
            SerializedProperty settings = paint.FindPropertyRelative("Settings").Copy();

            var holder = new VisualElement();
            var panel = new VisualElement();
            void Refresh(SerializedProperty _ = null)
            {
                panel.Clear();
                var kind = (LetteraPaintSourceKind)source.enumValueIndex;
                switch (kind)
                {
                    case LetteraPaintSourceKind.Solid: panel.Add(LtUI.Field("Color", color)); break;
                    case LetteraPaintSourceKind.Gradient: panel.Add(Gradient(gradient)); break;
                    case LetteraPaintSourceKind.Texture: panel.Add(LtUI.Field("Texture", texture)); break;
                }
                panel.Add(Settings(settings, kind, (LetteraPaintShape)settings.FindPropertyRelative("Shape").enumValueIndex));
            }
            holder.Add(LtUI.Row("Source", LtUI.Segments(source, "Solid", "Gradient", "Texture")));
            holder.Add(panel);
            Refresh();
            holder.TrackPropertyValue(source, Refresh);
            return holder;
        }

        static VisualElement Settings(SerializedProperty settings, LetteraPaintSourceKind kind, LetteraPaintShape shape)
        {
            var holder = new VisualElement();
            holder.Add(LtUI.Row("Blend", LtUI.Segments(settings.FindPropertyRelative("Blend").Copy(), "Inherit", "Normal", "Multiply", "Screen", "Add", "Excl")));
            if (kind == LetteraPaintSourceKind.Solid) return holder;

            holder.Add(LtUI.Row("Span", LtUI.Segments(settings.FindPropertyRelative("Span").Copy(), "Inherit", "Glyph", "Line", "Range", "Block"), "The rectangle the paint is measured against."));
            if (kind == LetteraPaintSourceKind.Gradient) holder.Add(LtUI.Row("Shape", LtUI.Segments(settings.FindPropertyRelative("Shape").Copy(), "Inherit", "Linear", "Radial", "Conic")));
            // A linear gradient has no aspect to fit; a conic one has no edge, and a texture takes its edge from the asset.
            if (kind == LetteraPaintSourceKind.Texture || shape == LetteraPaintShape.Radial || shape == LetteraPaintShape.Conic)
                holder.Add(LtUI.Row("Fit", LtUI.Segments(settings.FindPropertyRelative("Fit").Copy(), "Inherit", "Stretch", "Contain", "Cover", "Tile")));
            if (kind == LetteraPaintSourceKind.Gradient && shape != LetteraPaintShape.Conic)
                holder.Add(LtUI.Row("Edge", LtUI.Segments(settings.FindPropertyRelative("Edge").Copy(), "Inherit", "Clamp", "Repeat", "Mirror")));
            holder.Add(Optional("Angle", settings.FindPropertyRelative("Angle"), 0));
            holder.Add(Optional("Scale", settings.FindPropertyRelative("Scale"), 1));
            return holder;
        }

        // A number that may be left unset: NaN means no value at this level.
        static VisualElement Optional(string label, SerializedProperty property, float whenSet)
        {
            SerializedProperty bound = property.Copy();
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            var field = new FloatField();
            field.style.flexGrow = 1;
            VisualElement toggle = LtUI.El("lt-switch", LtUI.El("lt-switch__knob"));
            void Refresh()
            {
                bool set = !float.IsNaN(bound.floatValue);
                toggle.EnableInClassList("lt-switch--on", set);
                field.SetEnabled(set);
                field.SetValueWithoutNotify(set ? bound.floatValue : whenSet);
            }
            toggle.RegisterCallback<ClickEvent>(_ =>
            {
                bound.serializedObject.Update();
                bound.floatValue = float.IsNaN(bound.floatValue) ? whenSet : float.NaN;
                bound.serializedObject.ApplyModifiedProperties();
                Refresh();
            });
            field.RegisterValueChangedCallback(e =>
            {
                bound.serializedObject.Update();
                bound.floatValue = e.newValue;
                bound.serializedObject.ApplyModifiedProperties();
            });
            row.Add(toggle);
            row.Add(field);
            Refresh();
            return LtUI.Row(label, row);
        }

        static readonly Color32[] s_Preview = new Color32[128];

        public static VisualElement Gradient(SerializedProperty gradient)
        {
            SerializedProperty stops = gradient.FindPropertyRelative("Stops").Copy();
            SerializedProperty mode = gradient.FindPropertyRelative("Mode").Copy();
            var holder = LtUI.El("lt-gradient");
            VisualElement preview = LtUI.El("lt-gradient__bar");
            var rows = new VisualElement();
            Texture2D image = null;

            void RefreshPreview()
            {
                if (preview.panel == null && image != null) return;
                var value = new LetteraGradient { Mode = (LetteraGradientMode)mode.enumValueIndex };
                for (int i = 0; i < stops.arraySize; i++)
                {
                    SerializedProperty stop = stops.GetArrayElementAtIndex(i);
                    value.Stops.Add(new LetteraGradientStop(stop.FindPropertyRelative("Position").floatValue, stop.FindPropertyRelative("Color").colorValue));
                }
                if (value.Stops.Count == 0) return;
                value.Stops.Sort((a, b) => a.Position.CompareTo(b.Position));
                if (image == null) image = new Texture2D(s_Preview.Length, 1, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave, wrapMode = TextureWrapMode.Clamp };
                GradientAtlas.Bake(value, s_Preview);
                image.SetPixels32(s_Preview);
                image.Apply(false);
                preview.style.backgroundImage = image;
            }

            void RefreshRows()
            {
                rows.Clear();
                for (int i = 0; i < stops.arraySize; i++)
                {
                    int index = i;
                    SerializedProperty stop = stops.GetArrayElementAtIndex(i);
                    VisualElement row = LtUI.El("lt-gradient__stop");
                    var color = new PropertyField(stop.FindPropertyRelative("Color").Copy(), "");
                    color.BindProperty(stop.FindPropertyRelative("Color").Copy());
                    color.style.flexGrow = 1;
                    var position = new Slider(0, 1);
                    position.BindProperty(stop.FindPropertyRelative("Position").Copy());
                    position.style.flexGrow = 2;
                    row.Add(color);
                    row.Add(position);
                    row.Add(LtUI.Clickable("×", "lt-icon-btn lt-icon-btn--danger", "Remove this stop", () =>
                    {
                        stops.serializedObject.Update();
                        stops.DeleteArrayElementAtIndex(index);
                        stops.serializedObject.ApplyModifiedProperties();
                        RefreshRows();
                        RefreshPreview();
                    }));
                    rows.Add(row);
                }
            }

            holder.Add(preview);
            holder.Add(rows);
            holder.Add(LtUI.Clickable("+ Stop", "lt-add lt-add--small", "Add a color stop", () =>
            {
                stops.serializedObject.Update();
                int at = stops.arraySize;
                stops.InsertArrayElementAtIndex(at);
                SerializedProperty added = stops.GetArrayElementAtIndex(at);
                added.FindPropertyRelative("Position").floatValue = 1;
                added.FindPropertyRelative("Color").colorValue = Color.white;
                stops.serializedObject.ApplyModifiedProperties();
                RefreshRows();
                RefreshPreview();
            }));
            holder.Add(LtUI.Row("Mix In", LtUI.Segments(mode, "sRGB", "Linear", "Oklab", "Stepped")));
            RefreshRows();
            RefreshPreview();
            holder.TrackPropertyValue(gradient.Copy(), _ => RefreshPreview());
            return holder;
        }
    }
}

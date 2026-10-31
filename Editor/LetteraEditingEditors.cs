using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEngine.UIElements;

namespace FirstSymbol.Lettera.Editor
{
    /// <summary>A header and cards of rows, the frame every Lettera inspector shares.</summary>
    internal static class LtFrame
    {
        public static VisualElement Header(VisualElement root, string title, string subtitle)
        {
            root.Add(LtUI.El("lt-header", LtUI.Text("Lt", "lt-mark"), LtUI.El("lt-header__info", LtUI.Text(title, "lt-title"), LtUI.Text(subtitle, "lt-subtitle"))));
            VisualElement page = LtUI.El("lt-page");
            root.Add(page);
            return page;
        }

        public static string Nice(string name)
        {
            if (name.StartsWith("m_", StringComparison.Ordinal)) name = name.Substring(2);
            return ObjectNames.NicifyVariableName(name);
        }

        /// <summary>A labelled row, with this inspector's own switch for a bool.</summary>
        public static VisualElement Row(SerializedProperty property) =>
            property.propertyType == SerializedPropertyType.Boolean ? LtUI.Row(Nice(property.name), LtUI.Switch(property), property.tooltip) : LtUI.Field(Nice(property.name), property, property.tooltip);

        /// <summary>A card with a row for each named field. An empty list takes every visible field the others left.</summary>
        public static VisualElement Card(SerializedObject serialized, string title, params string[] names)
        {
            var rows = new List<VisualElement>();
            foreach (string name in names)
            {
                SerializedProperty property = serialized.FindProperty(name);
                if (property != null) rows.Add(Row(property));
            }
            return LtUI.Card(title, rows.ToArray());
        }

        public static VisualElement AllFields(SerializedObject serialized, string title, params string[] skip)
        {
            var rows = new List<VisualElement>();
            SerializedProperty property = serialized.GetIterator();
            bool enter = true;
            while (property.NextVisible(enter))
            {
                enter = false;
                if (property.name == "m_Script" || skip.Contains(property.name)) continue;
                rows.Add(Row(property));
            }
            return LtUI.Card(title, rows.ToArray());
        }
    }

    /// <summary>Lists types to add, with a search box and groups.</summary>
    internal sealed class LetteraTypePicker : EditorWindow
    {
        Action<Type> m_Chosen;
        List<(string group, string title, Type type)> m_All;
        VisualElement m_List;

        public static void Open(Rect anchor, List<(string group, string title, Type type)> items, Action<Type> chosen)
        {
            var window = CreateInstance<LetteraTypePicker>();
            window.m_All = items;
            window.m_Chosen = chosen;
            window.ShowAsDropDown(GUIUtility.GUIToScreenRect(anchor), new Vector2(280, 360));
        }

        void CreateGUI()
        {
            VisualElement root = LtUI.Root();
            root.style.flexGrow = 1;
            rootVisualElement.Add(root);
            var search = new TextField();
            search.AddToClassList("lt-row__field");
            root.Add(search);
            var scroll = new ScrollView();
            scroll.style.flexGrow = 1;
            root.Add(scroll);
            m_List = scroll.contentContainer;
            search.RegisterValueChangedCallback(e => Fill(e.newValue));
            Fill("");
            search.schedule.Execute(() => search.Focus());
        }

        void Fill(string query)
        {
            m_List.Clear();
            foreach ((string group, string title, Type type) in Matching(m_All, query))
            {
                Type chosen = type;
                m_List.Add(LtUI.Clickable(group + "  /  " + title, "lt-tool", chosen.FullName, () =>
                {
                    m_Chosen?.Invoke(chosen);
                    Close();
                }));
            }
        }

        public static IEnumerable<(string group, string title, Type type)> Matching(IEnumerable<(string group, string title, Type type)> items, string query) =>
            items.Where(i => string.IsNullOrEmpty(query) || i.title.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0 || i.group.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0);
    }

    /// <summary>A list of serialized references: add through a picker, reorder, turn off, remove.</summary>
    internal sealed class LetteraReferenceList : VisualElement
    {
        readonly SerializedObject m_Object;
        readonly string m_Path;
        readonly Func<List<(string, string, Type)>> m_Types;
        readonly Func<int, string> m_Warning;
        readonly VisualElement m_Rows = new VisualElement();

        public LetteraReferenceList(SerializedProperty list, Func<List<(string, string, Type)>> types, Func<int, string> warning = null)
        {
            m_Object = list.serializedObject;
            m_Path = list.propertyPath;
            m_Types = types;
            m_Warning = warning;
            Add(m_Rows);
            Add(LtUI.Clickable("+ Add", "lt-tool", "Add one", () =>
            {
                Rect anchor = worldBound;
                LetteraTypePicker.Open(new Rect(anchor.x, anchor.yMax - 20, anchor.width, 20), m_Types(), AddOf);
            }));
            Rebuild();
            this.TrackPropertyValue(m_Object.FindProperty(m_Path).FindPropertyRelative("Array.size"), _ => Rebuild());
        }

        SerializedProperty List => m_Object.FindProperty(m_Path);

        internal void AddOf(Type type)
        {
            m_Object.Update();
            SerializedProperty list = List;
            int index = list.arraySize;
            list.InsertArrayElementAtIndex(index);
            list.GetArrayElementAtIndex(index).managedReferenceValue = Activator.CreateInstance(type);
            m_Object.ApplyModifiedProperties();
            Rebuild();
        }

        void Move(int from, int to)
        {
            m_Object.Update();
            List.MoveArrayElement(from, to);
            m_Object.ApplyModifiedProperties();
            Rebuild();
        }

        void Remove(int index)
        {
            m_Object.Update();
            List.DeleteArrayElementAtIndex(index);
            m_Object.ApplyModifiedProperties();
            Rebuild();
        }

        internal void Rebuild()
        {
            m_Rows.Clear();
            m_Object.UpdateIfRequiredOrScript();
            SerializedProperty list = List;
            for (int i = 0; i < list.arraySize; i++)
            {
                int index = i;
                SerializedProperty element = list.GetArrayElementAtIndex(i);
                object value = element.managedReferenceValue;
                if (value == null) continue;
                Type type = value.GetType();
                var info = type.GetCustomAttribute<LetteraRuleInfoAttribute>();
                string title = info != null ? info.Title : ObjectNames.NicifyVariableName(type.Name);

                VisualElement head = LtUI.El("lt-row", LtUI.Text(title, "lt-row__label"));
                SerializedProperty enabled = element.FindPropertyRelative("m_Enabled");
                if (enabled != null) head.Add(LtUI.Switch(enabled));
                head.Add(LtUI.Clickable("▲", "lt-tool", "Earlier", () => { if (index > 0) Move(index, index - 1); }));
                head.Add(LtUI.Clickable("▼", "lt-tool", "Later", () => { if (index + 1 < List.arraySize) Move(index, index + 1); }));
                head.Add(LtUI.Clickable("×", "lt-tool", "Remove", () => Remove(index)));
                VisualElement card = LtUI.Card("", head);

                string warning = m_Warning?.Invoke(index);
                if (!string.IsNullOrEmpty(warning)) card.Add(LtUI.Text(warning, "lt-chip lt-chip--orange"));

                SerializedProperty child = element.Copy();
                SerializedProperty end = element.GetEndProperty();
                bool enter = true;
                while (child.NextVisible(enter) && !SerializedProperty.EqualContents(child, end))
                {
                    enter = false;
                    if (child.name == "m_Enabled") continue;
                    card.Add(LtFrame.Row(child.Copy()));
                }
                m_Rows.Add(card);
            }
        }
    }

    internal static class LetteraRuleCatalog
    {
        static List<(string, string, Type)> s_Rules, s_Commands;

        public static List<(string, string, Type)> Rules
        {
            get
            {
                if (s_Rules != null) return s_Rules;
                s_Rules = new List<(string, string, Type)>();
                foreach (Type type in TypeCache.GetTypesDerivedFrom<LetteraFieldRule>())
                {
                    if (type.IsAbstract || type.IsGenericType) continue;
                    var info = type.GetCustomAttribute<LetteraRuleInfoAttribute>();
                    s_Rules.Add(((info != null ? info.Group : LetteraRuleGroup.Custom).ToString(), info != null ? info.Title : ObjectNames.NicifyVariableName(type.Name), type));
                }
                s_Rules.Sort((a, b) => a.Item1 != b.Item1 ? string.CompareOrdinal(a.Item1, b.Item1) : string.CompareOrdinal(a.Item2, b.Item2));
                return s_Rules;
            }
        }

        public static List<(string, string, Type)> Commands
        {
            get
            {
                if (s_Commands != null) return s_Commands;
                s_Commands = new List<(string, string, Type)>();
                foreach (Type type in TypeCache.GetTypesDerivedFrom<LetteraEditCommand>())
                    if (!type.IsAbstract)
                        s_Commands.Add(("Commands", ObjectNames.NicifyVariableName(type.Name).Replace(" Command", ""), type));
                s_Commands.Sort((a, b) => string.CompareOrdinal(a.Item2, b.Item2));
                return s_Commands;
            }
        }

        /// <summary>Order mistakes that are easy to make and hard to see.</summary>
        public static string OrderWarning(SerializedProperty list, int index)
        {
            object rule = list.GetArrayElementAtIndex(index).managedReferenceValue;
            for (int later = index + 1; later < list.arraySize; later++)
            {
                object other = list.GetArrayElementAtIndex(later).managedReferenceValue;
                if (rule is MaxLengthRule && other is SingleLineRule) return "Place this after Single Line: the line breaks it removes would count.";
                if (rule is PatternMaskRule && !(other is LetteraFieldDecorator)) return "A mask rewrites the whole value: rules after it judge the formatted text.";
            }
            return null;
        }
    }

    [CustomEditor(typeof(LetteraSelectable)), CanEditMultipleObjects]
    internal sealed class LetteraSelectableEditor : UnityEditor.Editor
    {
        public override VisualElement CreateInspectorGUI()
        {
            VisualElement root = LtUI.Root();
            VisualElement page = LtFrame.Header(root, "Selectable", "Selection, copy, gestures, handles and menu");
            VisualElement warnings = LtUI.El("lt-chips");
            page.Add(warnings);
            page.Add(LtFrame.Card(serializedObject, "Highlight", "m_UseProjectHighlightColor", "m_HighlightColor", "m_HighlightOrder", "m_InactiveHighlight"));
            page.Add(LtFrame.Card(serializedObject, "Behaviour", "m_DragAutoScroll", "m_SelectOnRightClick", "m_Viewport"));
            page.Add(LtFrame.Card(serializedObject, "Touch", "m_HandlesComponent", "m_LoupeComponent", "m_NoHandles", "m_NoLoupe", "m_MenuPresenter"));
            page.Add(LtUI.Card("Commands", new LetteraReferenceList(serializedObject.FindProperty("m_Commands"), () => LetteraRuleCatalog.Commands)));

            void Refresh()
            {
                if (target == null) return;
                warnings.Clear();
                foreach (string warning in Warnings((LetteraSelectable)target)) warnings.Add(LtUI.Text(warning, "lt-chip lt-chip--orange"));
            }
            Refresh();
            root.schedule.Execute(Refresh).Every(1000);
            return root;
        }

        internal static IEnumerable<string> Warnings(LetteraSelectable selectable)
        {
            if (selectable.GetComponent<LetteraText>() == null && selectable.GetComponent<LetteraWorldText>() == null) yield return "Needs a Lettera Text on this object";
            if (selectable.GetComponent<LetteraWorldText>() != null && Camera.main != null && Camera.main.GetComponent<LetteraWorldRaycaster>() == null) yield return "No World Raycaster on the main camera: this text gets no pointer events";
            if (selectable.gameObject.scene.IsValid() && UnityEngine.Object.FindAnyObjectByType<EventSystem>() == null) yield return "No EventSystem in the scene";
            Canvas canvas = selectable.GetComponentInParent<Canvas>();
            if (canvas != null && canvas.GetComponentInParent<GraphicRaycaster>() == null) yield return "No Graphic Raycaster on the canvas: this text gets no pointer events";
        }
    }

    [CustomEditor(typeof(LetteraEditable)), CanEditMultipleObjects]
    internal sealed class LetteraEditableEditor : UnityEditor.Editor
    {
        public override VisualElement CreateInspectorGUI()
        {
            VisualElement root = LtUI.Root();
            VisualElement page = LtFrame.Header(root, "Editable", "Document, undo, input rules, caret and paste");
            VisualElement live = LtUI.El("lt-chips");
            page.Add(live);
            page.Add(LtFrame.Card(serializedObject, "Value", "m_ReadOnly", "m_Interactable"));
            SerializedProperty rules = serializedObject.FindProperty("m_Rules");
            string rulesPath = rules.propertyPath;
            page.Add(LtUI.Card("Rules", new LetteraReferenceList(rules, () => LetteraRuleCatalog.Rules, i => LetteraRuleCatalog.OrderWarning(serializedObject.FindProperty(rulesPath), i))));
            page.Add(LtFrame.Card(serializedObject, "Presets", "m_RulePresets"));
            page.Add(LtFrame.Card(serializedObject, "Markup", "m_MarkupView", "m_TypedMarkup", "m_PlainPaste", "m_PasteKeepsStyles"));
            page.Add(LtFrame.Card(serializedObject, "Caret", "m_Caret"));

            void Refresh()
            {
                if (target == null) return;
                live.Clear();
                if (!Application.isPlaying) return;
                var field = (LetteraEditable)target;
                LetteraSelectionRange s = field.Selectable.Selection;
                live.Add(LtUI.Text($"selection {s.Start}..{s.End}", "lt-chip"));
                live.Add(LtUI.Text($"undo {field.UndoDepth}", "lt-chip"));
                if (field.IsComposing) live.Add(LtUI.Text("composing", "lt-chip"));
                if (!field.LastCheck.IsValid) live.Add(LtUI.Text(field.LastCheck.Status, "lt-chip lt-chip--orange"));
            }
            Refresh();
            root.schedule.Execute(Refresh).Every(400);
            return root;
        }
    }

    /// <summary>The same frame for the smaller components.</summary>
    internal abstract class LetteraSimpleEditor : UnityEditor.Editor
    {
        protected abstract string Title { get; }
        protected abstract string Subtitle { get; }
        protected virtual string[] Hidden => Array.Empty<string>();

        public override VisualElement CreateInspectorGUI()
        {
            VisualElement root = LtUI.Root();
            VisualElement page = LtFrame.Header(root, Title, Subtitle);
            page.Add(LtFrame.AllFields(serializedObject, "", Hidden));
            return root;
        }
    }

    [CustomEditor(typeof(LetteraCaret)), CanEditMultipleObjects]
    internal sealed class LetteraCaretEditor : LetteraSimpleEditor
    {
        protected override string Title => "Caret";
        protected override string Subtitle => "Draws the caret of a field";
        protected override string[] Hidden => new[] { "m_Material", "m_RaycastTarget", "m_RaycastPadding", "m_Maskable", "m_OnCullStateChanged" };
    }

    [CustomEditor(typeof(LetteraContextMenu)), CanEditMultipleObjects]
    internal sealed class LetteraContextMenuEditor : LetteraSimpleEditor
    {
        protected override string Title => "Context Menu";
        protected override string Subtitle => "UI objects you own, one per command or one row for all";
    }

    [CustomEditor(typeof(LetteraPrefabHandles)), CanEditMultipleObjects]
    internal sealed class LetteraHandlesEditor : LetteraSimpleEditor
    {
        protected override string Title => "Touch Handles";
        protected override string Subtitle => "Selection ends a finger can drag";
    }

    [CustomEditor(typeof(LetteraMeshLoupe)), CanEditMultipleObjects]
    internal sealed class LetteraLoupeEditor : LetteraSimpleEditor
    {
        protected override string Title => "Mesh Loupe";
        protected override string Subtitle => "Magnifies the text under a finger";
    }

    [CustomEditor(typeof(LetteraCommandControl)), CanEditMultipleObjects]
    internal sealed class LetteraCommandControlEditor : UnityEditor.Editor
    {
        public override VisualElement CreateInspectorGUI()
        {
            VisualElement root = LtUI.Root();
            VisualElement page = LtFrame.Header(root, "Command Control", "A button or toggle that runs a command");
            page.Add(LtFrame.Card(serializedObject, "", "m_Target"));
            var names = new List<string> { "None" };
            var types = new List<Type> { null };
            foreach ((string _, string title, Type type) in LetteraRuleCatalog.Commands)
            {
                names.Add(title);
                types.Add(type);
            }
            object current = serializedObject.FindProperty("m_Command").managedReferenceValue;
            var popup = new PopupField<string>(names, Mathf.Max(types.IndexOf(current?.GetType()), 0));
            popup.RegisterValueChangedCallback(e =>
            {
                serializedObject.Update();
                Type type = types[names.IndexOf(e.newValue)];
                serializedObject.FindProperty("m_Command").managedReferenceValue = type != null ? Activator.CreateInstance(type) : null;
                serializedObject.ApplyModifiedProperties();
            });
            page.Add(LtUI.Card("", LtUI.Row("Command", popup)));
            return root;
        }
    }

    [CustomEditor(typeof(LetteraDropdown)), CanEditMultipleObjects]
    internal sealed class LetteraDropdownEditor : LetteraSimpleEditor
    {
        protected override string Title => "Dropdown";
        protected override string Subtitle => "Pick one of several options";
    }

    [CustomEditor(typeof(LetteraDropdownRow)), CanEditMultipleObjects]
    internal sealed class LetteraDropdownRowEditor : LetteraSimpleEditor
    {
        protected override string Title => "Dropdown Row";
        protected override string Subtitle => "One line of a dropdown's list";
    }

    [CustomEditor(typeof(LetteraKeepFocus)), CanEditMultipleObjects]
    internal sealed class LetteraKeepFocusEditor : LetteraSimpleEditor
    {
        protected override string Title => "Keep Focus";
        protected override string Subtitle => "Pressing this object does not end editing";
    }

    [CustomEditor(typeof(LetteraWorldRaycaster)), CanEditMultipleObjects]
    internal sealed class LetteraWorldRaycasterEditor : LetteraSimpleEditor
    {
        protected override string Title => "World Raycaster";
        protected override string Subtitle => "Gives world text pointer events";
    }

    [CustomEditor(typeof(LetteraPasswordToggle)), CanEditMultipleObjects]
    internal sealed class LetteraPasswordToggleEditor : LetteraSimpleEditor
    {
        protected override string Title => "Password Toggle";
        protected override string Subtitle => "Shows and hides a password field's value";
    }

    [CustomEditor(typeof(LetteraFieldPreset)), CanEditMultipleObjects]
    internal sealed class LetteraFieldPresetEditor : UnityEditor.Editor
    {
        public override VisualElement CreateInspectorGUI()
        {
            VisualElement root = LtUI.Root();
            VisualElement page = LtFrame.Header(root, "Field Preset", "One rule list for many fields");
            page.Add(LtUI.Card("Rules", new LetteraReferenceList(serializedObject.FindProperty("m_Rules"), () => LetteraRuleCatalog.Rules)));
            return root;
        }
    }

    /// <summary>Menu items for selectable text and ready-made fields.</summary>
    internal static class LetteraEditingMenus
    {
        static void Finish(GameObject go)
        {
            Undo.RegisterCreatedObjectUndo(go, "Create " + go.name);
            Selection.activeGameObject = go;
        }

        static void FromSlot(MenuCommand command, GameObject slot, Func<Transform, GameObject> build)
        {
            Transform parent = LetteraMenus.FindOrCreateCanvas(command.context as GameObject);
            GameObject go = slot != null ? (GameObject)PrefabUtility.InstantiatePrefab(slot, parent) : build(parent);
            Finish(go);
        }

        [MenuItem("GameObject/Lettera/Selectable Text", false, 12)]
        static void Selectable(MenuCommand c) => FromSlot(c, LetteraSettings.Instance.Editing.SelectablePrefab, p => LetteraFieldFactory.CreateSelectableText(p));

        [MenuItem("GameObject/Lettera/Editable Text", false, 13)]
        static void Editable(MenuCommand c) => FromSlot(c, LetteraSettings.Instance.Editing.EditablePrefab, p => LetteraFieldFactory.CreateEditableText(p));

        [MenuItem("GameObject/Lettera/Input Field", false, 14)]
        static void Input(MenuCommand c) => FromSlot(c, LetteraSettings.Instance.Editing.InputFieldPrefab, p => LetteraFieldFactory.CreateInputField(p));

        [MenuItem("GameObject/Lettera/Text Area", false, 15)]
        static void Area(MenuCommand c) => FromSlot(c, LetteraSettings.Instance.Editing.TextAreaPrefab, p => LetteraFieldFactory.CreateTextArea(p));

        [MenuItem("GameObject/Lettera/Password Field", false, 16)]
        static void Password(MenuCommand c) => FromSlot(c, LetteraSettings.Instance.Editing.PasswordFieldPrefab, p => LetteraFieldFactory.CreatePasswordField(p));

        [MenuItem("GameObject/Lettera/Dropdown", false, 17)]
        static void Dropdown(MenuCommand c) => FromSlot(c, LetteraSettings.Instance.Editing.DropdownPrefab, p => LetteraFieldFactory.CreateDropdown(p));

        [MenuItem("GameObject/Lettera/World Selectable Text", false, 18)]
        static void WorldSelectable(MenuCommand command)
        {
            var go = new GameObject("World Selectable Text", typeof(RectTransform));
            GameObjectUtility.SetParentAndAlign(go, command.context as GameObject);
            ((RectTransform)go.transform).sizeDelta = new Vector2(20, 5);
            var text = go.AddComponent<LetteraWorldText>();
            text.FontSize = 2f;
            text.Text = "Selectable text";
            go.AddComponent<LetteraSelectable>();
            Finish(go);
            Camera camera = Camera.main;
            if (camera != null && camera.GetComponent<LetteraWorldRaycaster>() == null) Undo.AddComponent<LetteraWorldRaycaster>(camera.gameObject);
            if (UnityEngine.Object.FindAnyObjectByType<EventSystem>() == null) LetteraMenus.CreateEventSystem();
        }

        /// <summary>The text inspector's card: which interaction a text has, with one click to change it.</summary>
        public static VisualElement InteractionCard(UnityEngine.Object[] targets)
        {
            VisualElement row = LtUI.El("lt-chips");
            bool previewing = false;
            row.RegisterCallback<DetachFromPanelEvent>(_ =>
            {
                if (!previewing) return;
                previewing = false;
                LetteraMotionPreview.End();
            });
            void Refresh()
            {
                row.Clear();
                row.Add(Chip("Selectable", typeof(LetteraSelectable)));
                row.Add(Chip("Editable", typeof(LetteraEditable)));
                row.Add(Chip("Sequencer", typeof(LetteraSequencer)));
                row.Add(Chip("Range Navigator", typeof(LetteraRangeNavigator)));
                var first = targets.Length > 0 ? targets[0] as Component : null;
                IEnumerable<LetteraStyle> styles = first is LetteraText canvas ? canvas.Styles : first is LetteraWorldText world ? world.Styles : null;
                if (styles == null) return;
                LetteraMotionScan.Counts counts = LetteraMotionScan.Of(styles);
                void Count(string title, int value)
                {
                    if (value > 0) row.Add(LtUI.Text(title + " " + value, "lt-chip lt-chip--accent"));
                }
                Count("Links", counts.Links);
                Count("Rules", counts.Rules);
                Count("Motion", counts.Motion);
                Count("Inline", counts.Inline);
                Count("Semantics", counts.Semantics);
                row.Add(LtUI.Clickable(previewing ? "■ Stop Preview" : "▶ Preview Motion", "lt-chip", "Runs motion in edit mode. Nothing is written, and it ends with this inspector.", () =>
                {
                    previewing = !previewing;
                    if (previewing) LetteraMotionPreview.Begin();
                    else LetteraMotionPreview.End();
                    Refresh();
                }));
                foreach (string warning in LetteraMotionScan.Warnings(first, styles)) row.Add(LtUI.Text(warning, "lt-chip lt-chip--orange"));
            }
            VisualElement Chip(string title, Type type)
            {
                bool has = targets.All(t => t != null && ((Component)t).GetComponent(type) != null);
                return LtUI.Clickable((has ? "✓ " : "+ ") + title, has ? "lt-chip" : "lt-chip lt-chip--orange", has ? "Remove " + title : "Add " + title, () =>
                {
                    foreach (UnityEngine.Object t in targets)
                    {
                        var component = (Component)t;
                        Component existing = component.GetComponent(type);
                        if (has)
                        {
                            if (type == typeof(LetteraSelectable))
                            {
                                Component editable = component.GetComponent<LetteraEditable>();
                                if (editable != null) Undo.DestroyObjectImmediate(editable);
                            }
                            if (existing != null) Undo.DestroyObjectImmediate(existing);
                        }
                        else if (existing == null)
                        {
                            Undo.AddComponent(component.gameObject, type);
                        }
                    }
                    Refresh();
                });
            }
            Refresh();
            return LtUI.Card("Interaction", row);
        }
    }
}

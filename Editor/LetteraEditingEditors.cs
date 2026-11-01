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
        public static VisualElement Row(SerializedProperty property) => LtUI.Field(Nice(property.name), property, property.tooltip);

        /// <summary>A type's name in words, without what every one of its kind ends in.</summary>
        public static string Words(Type type)
        {
            string name = type.Name;
            foreach (string suffix in new[] { "Effect", "Rule", "Source" })
                if (name.Length > suffix.Length && name.EndsWith(suffix, StringComparison.Ordinal))
                    name = name.Substring(0, name.Length - suffix.Length);
            return ObjectNames.NicifyVariableName(name);
        }

        /// <summary>A row of a list by the name it holds, or by its place while it has none.</summary>
        public static string NameOr(SerializedProperty row, string nameField, string fallback)
        {
            string name = row.FindPropertyRelative(nameField)?.stringValue;
            return string.IsNullOrEmpty(name) ? fallback : name;
        }

        /// <summary>A setting and the switch that goes with it, on one row.</summary>
        public static VisualElement WithSwitch(VisualElement main, string label, SerializedProperty property, string tooltip = null) =>
            LtUI.El("lt-row lt-row--pair", main, LtUI.InlineSwitch(label, property, tooltip));

        /// <summary>The whole page of a small component or asset: what it is for in a line, and its fields on a card.</summary>
        public static VisualElement Small(SerializedObject serialized, string title, string words, params string[] hidden)
        {
            VisualElement root = LtUI.Root();
            VisualElement page = Header(root, title, words);
            VisualElement fields = AllFields(serialized, "", hidden);
            if (fields.childCount > 0) page.Add(fields);
            return root;
        }

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

    /// <summary>A list of serialized references, on the house's list: add through a grouped picker, drag, turn off at a row's end, remove.</summary>
    internal sealed class LetteraReferenceList : VisualElement
    {
        readonly SerializedObject m_Object;
        readonly string m_Path;
        readonly Func<List<(string, string, Type)>> m_Types;
        readonly Func<int, string> m_Warning;
        readonly LtList m_List;

        public LetteraReferenceList(SerializedProperty list, Func<List<(string, string, Type)>> types, Func<int, string> warning = null, string title = null, string empty = "None yet.")
        {
            m_Object = list.serializedObject;
            m_Path = list.propertyPath;
            m_Types = types;
            m_Warning = warning;
            m_List = new LtList(title, list, MakeRow, empty, _ => OpenPicker());
            m_List.RowToText = element => element.managedReferenceValue != null ? element.managedReferenceValue.GetType().Name + " " + JsonUtility.ToJson(element.managedReferenceValue) : "";
            Add(m_List);
        }

        SerializedProperty List => m_Object.FindProperty(m_Path);

        void OpenPicker() => LetteraTypePicker.Open(m_List, m_Types(), LetteraRuleCatalog.Words, AddOf);

        internal void AddOf(Type type)
        {
            m_Object.Update();
            SerializedProperty list = List;
            int index = list.arraySize;
            list.InsertArrayElementAtIndex(index);
            list.GetArrayElementAtIndex(index).managedReferenceValue = Activator.CreateInstance(type);
            m_Object.ApplyModifiedProperties();
            Rebuild();
            m_List.OpenEnclosing();
        }

        internal void Rebuild() => m_List.Rebuild();

        VisualElement MakeRow(SerializedProperty element, int index)
        {
            object value = element.managedReferenceValue;
            if (value == null) return LtUI.Text("A rule whose class is gone.", "lt-hint");
            Type type = value.GetType();
            var info = type.GetCustomAttribute<LetteraRuleInfoAttribute>();
            string title = info != null ? info.Title : LtFrame.Words(type);

            VisualElement body = LtUI.El("lt-ref-row");
            SerializedProperty enabled = element.FindPropertyRelative("m_Enabled");
            VisualElement head = LtUI.El("lt-ref-row__head", LtUI.Text(title, "lt-row__label lt-ref-row__title"));
            string words = LetteraRuleCatalog.Words(type);
            if (!string.IsNullOrEmpty(words)) head.tooltip = words;
            if (enabled != null) head.Add(LtUI.Switch(enabled));
            head.Add(LtUI.Clickable("×", "lt-icon-btn lt-icon-btn--danger", "Remove", () =>
            {
                m_List.Selection.Click(index);
                m_List.RemoveSelected();
            }));
            body.Add(head);

            string warning = m_Warning?.Invoke(index);
            if (!string.IsNullOrEmpty(warning)) body.Add(new LtFix(warning, null));

            SerializedProperty child = element.Copy();
            SerializedProperty end = element.GetEndProperty();
            bool enter = true;
            while (child.NextVisible(enter) && !SerializedProperty.EqualContents(child, end))
            {
                enter = false;
                if (child.name == "m_Enabled") continue;
                body.Add(LtFrame.Row(child.Copy()));
            }
            return body;
        }
    }

    internal static class LetteraRuleCatalog
    {
        static List<(string, string, Type)> s_Rules, s_Commands;

        static readonly Dictionary<Type, string> s_Words = new Dictionary<Type, string>
        {
            { typeof(SingleLineRule), "Stays on one line: line breaks are taken out." },
            { typeof(PasswordRule), "Shows a mask, and refuses to copy." },
            { typeof(MaxLengthRule), "Takes no more than so many characters." },
            { typeof(PatternMaskRule), "Fits what is typed to a pattern, such as a phone number." },
            { typeof(LetterCaseRule), "Turns what is typed to upper, lower or title case." },
            { typeof(SelectAllOnFocusRule), "Selects everything when the field gets focus." },
            { typeof(ReleaseOnCancelRule), "Esc ends editing, and puts the old value back." },
            { typeof(EnterKeyRule), "What Enter does: a new line, or submit." },
            { typeof(TabKeyRule), "What Tab does: a tab, spaces, or the next field." },
            { typeof(KeyboardTraitsRule), "Which keyboard a phone offers, and its correction." },
            { typeof(KeyboardLiftRule), "Lifts the field above a phone's keyboard." },
            { typeof(AlphaNumericCharFilter), "Only letters and digits." },
            { typeof(WholeNumberCharFilter), "Only a whole number." },
            { typeof(DecimalCharFilter), "Only a number with a decimal point." },
            { typeof(EmailCharFilter), "Only what an e-mail address can hold." },
            { typeof(PersonNameCharFilter), "Only what a name can hold, with capitals where they go." },
            { typeof(ValidationRule), "Checks the value, and says what is wrong with it." },
            { typeof(HintDecorator), "A line that shows while the field is empty." },
            { typeof(RaisedLabelDecorator), "A label that lies in the field, and rises when it fills." },
            { typeof(HelperTextDecorator), "A line under the field, which an error replaces." },
            { typeof(LengthCounterDecorator), "Counts what was typed against the limit." },
        };

        /// <summary>What a rule does, in a line. A custom one says it through a Tooltip attribute.</summary>
        public static string Words(Type type)
        {
            if (s_Words.TryGetValue(type, out string words)) return words;
            return type.GetCustomAttribute<TooltipAttribute>()?.tooltip;
        }

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
            VisualElement warnings = LtUI.El("lt-warnings");
            page.Add(warnings);

            SerializedProperty P(string name) => serializedObject.FindProperty(name);
            VisualElement color = LtUI.Field("Color", P("m_HighlightColor"), "This selection's own color.");
            LetteraTextInspector.ShowWhen(color, P("m_UseProjectHighlightColor"), p => !p.boolValue);
            page.Add(LtUI.Card("Highlight",
                LtUI.Field("Project Color", P("m_UseProjectHighlightColor"), "Takes the color from Project Settings > Lettera."),
                color,
                LtUI.Field("Order", P("m_HighlightOrder"), "Whether the highlight lies under the text or over it."),
                LtUI.Field("When Not Focused", P("m_InactiveHighlight"), "What a selection does while this text has no focus.")));
            page.Add(LtUI.Card("Behaviour",
                LtUI.Field("Scroll While Dragging", P("m_DragAutoScroll"), "A drag past the edge scrolls the view."),
                LtUI.Field("Select On Right Click", P("m_SelectOnRightClick"), "A right click on no selection selects the word under it."),
                LtUI.Field("Viewport", P("m_Viewport"), "What a scrolled text is clipped by. Handles hide outside it.")));
            page.Add(LtUI.Card("Touch And Menu",
                LtFrame.WithSwitch(LtUI.Field("Handles", P("m_HandlesComponent"), "Selection ends a finger can drag. Empty uses the project's."), "None", P("m_NoHandles"), "No handles on this text at all."),
                LtFrame.WithSwitch(LtUI.Field("Loupe", P("m_LoupeComponent"), "Magnifies under a finger. Empty uses the project's."), "None", P("m_NoLoupe"), "No loupe on this text at all."),
                LtUI.Field("Menu", P("m_MenuPresenter"), "Draws the right-click and long-press menu. Empty uses the project's."),
                new LtStatus().Line("Android Menu", () => LetteraSettings.Instance.Editing.AndroidMenu.ToString()).Line("iOS Menu", () => LetteraSettings.Instance.Editing.IosMenu.ToString()).Line("Loupe", () => LetteraSettings.Instance.Editing.Loupe.ToString()).RefreshNow()));
            page.Add(LtUI.Card("", new LetteraReferenceList(P("m_Commands"), () => LetteraRuleCatalog.Commands, null, "Commands", "No commands: the menu stays empty.")));

            string shown = null;
            void Refresh()
            {
                if (target == null) return;
                var selectable = (LetteraSelectable)target;
                List<string> now = Warnings(selectable).ToList();
                string key = string.Join("\n", now);
                if (key == shown) return;
                shown = key;
                warnings.Clear();
                foreach (string warning in now)
                {
                    Action fix = FixFor(selectable, warning, out string label);
                    warnings.Add(new LtFix(warning, null, label, fix == null ? (Action)null : () =>
                    {
                        fix();
                        shown = null;
                        Refresh();
                    }));
                }
            }
            Refresh();
            root.schedule.Execute(Refresh).Every(1000);
            return root;
        }

        static Action FixFor(LetteraSelectable selectable, string warning, out string label)
        {
            label = "Add";
            if (warning.Contains("EventSystem")) return LetteraMenus.CreateEventSystem;
            if (warning.Contains("World Raycaster") && Camera.main != null) return () => Undo.AddComponent<LetteraWorldRaycaster>(Camera.main.gameObject);
            if (warning.Contains("Graphic Raycaster"))
            {
                Canvas canvas = selectable.GetComponentInParent<Canvas>();
                if (canvas != null) return () => Undo.AddComponent<GraphicRaycaster>(canvas.gameObject);
            }
            return null;
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

    /// <summary>What a set of rules would do to typed text, through a real field on a hidden object that lives for one trial.</summary>
    internal static class LetteraRuleTrial
    {
        public static string Run(IEnumerable<LetteraFieldRule> rules, IEnumerable<LetteraFieldPreset> presets, string typed, out string note)
        {
            note = null;
            GameObject go = EditorUtility.CreateGameObjectWithHideFlags("Lettera Rule Trial", HideFlags.HideAndDontSave, typeof(RectTransform));
            try
            {
                go.AddComponent<LetteraText>();
                go.AddComponent<LetteraSelectable>();
                var field = go.AddComponent<LetteraEditable>();
                foreach (LetteraFieldRule rule in rules)
                    if (rule != null)
                        field.Rules.Add(rule.Clone());
                if (presets != null)
                    foreach (LetteraFieldPreset preset in presets)
                        if (preset != null)
                            field.RulePresets.Add(preset);
                field.enabled = false;
                field.enabled = true;
                int refused = 0;
                foreach (string piece in Clusters(typed))
                    if (!field.InsertText(piece, "user.type"))
                        refused++;
                if (refused > 0) note = refused + (refused == 1 ? " character turned away" : " characters turned away");
                return field.PlainText;
            }
            catch (Exception e)
            {
                note = "A rule needs a running field for this: " + e.Message;
                return "";
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        static IEnumerable<string> Clusters(string text)
        {
            System.Globalization.TextElementEnumerator walk = System.Globalization.StringInfo.GetTextElementEnumerator(text ?? "");
            while (walk.MoveNext()) yield return walk.GetTextElement();
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
            SerializedProperty P(string name) => serializedObject.FindProperty(name);
            page.Add(LtUI.Card("Value",
                LtUI.El("lt-row lt-row--pair",
                    LtUI.InlineSwitch("Read Only", P("m_ReadOnly"), "Selects and copies, and takes no typing."),
                    LtUI.InlineSwitch("Interactable", P("m_Interactable"), "Off: takes neither focus nor typing.")),
                LtUI.Field("Caret", P("m_Caret"), "What draws the caret. Empty makes one when the field first needs it.")));
            SerializedProperty rules = P("m_Rules");
            string rulesPath = rules.propertyPath;
            page.Add(LtUI.Card("", new LetteraReferenceList(rules, () => LetteraRuleCatalog.Rules, i => LetteraRuleCatalog.OrderWarning(serializedObject.FindProperty(rulesPath), i), "Rules", "No rules: this field takes whatever is typed.")));
            page.Add(LtUI.Card("", LtUI.Field("Field Presets", P("m_RulePresets"), "Rule lists shared between fields.")));
            page.Add(TryIt());
            page.Add(LtUI.Card("Markup And Paste",
                LtUI.Field("Markup Shown As", P("m_MarkupView")),
                LtUI.Field("Typed Markup", P("m_TypedMarkup")),
                LtUI.Field("Plain Paste", P("m_PlainPaste")),
                LtUI.Field("Paste Keeps Styles", P("m_PasteKeepsStyles"))));

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

        // Types into a field made for the moment. Nothing here touches this component.
        VisualElement TryIt()
        {
            var input = new TextField { value = "" };
            input.AddToClassList("lt-text-input");
            input.textEdition.placeholder = "Type here";
            Label result = LtUI.Text("", "lt-row__value");
            Label note = LtUI.Text("", "lt-hint");
            note.style.display = DisplayStyle.None;
            input.RegisterValueChangedCallback(e =>
            {
                var field = (LetteraEditable)target;
                result.text = LetteraRuleTrial.Run(field.Rules, field.RulePresets, e.newValue, out string words);
                note.text = words ?? "";
                note.style.display = words != null ? DisplayStyle.Flex : DisplayStyle.None;
            });
            return LtUI.Card("Try It", LtUI.Row("Typed", input, "What this field's rules do to typing. Nothing is kept."), LtUI.Row("Becomes", result), note);
        }
    }

    [CustomEditor(typeof(LetteraCaret)), CanEditMultipleObjects]
    internal sealed class LetteraCaretEditor : UnityEditor.Editor
    {
        public override VisualElement CreateInspectorGUI() => LtFrame.Small(serializedObject, "Caret", "Draws the caret of a field", "m_Material", "m_RaycastTarget", "m_RaycastPadding", "m_Maskable", "m_OnCullStateChanged");
    }

    [CustomEditor(typeof(LetteraContextMenu)), CanEditMultipleObjects]
    internal sealed class LetteraContextMenuEditor : UnityEditor.Editor
    {
        public override VisualElement CreateInspectorGUI() => LtFrame.Small(serializedObject, "Context Menu", "UI objects you own, one per command or one row for all");
    }

    [CustomEditor(typeof(LetteraPrefabHandles)), CanEditMultipleObjects]
    internal sealed class LetteraHandlesEditor : UnityEditor.Editor
    {
        public override VisualElement CreateInspectorGUI() => LtFrame.Small(serializedObject, "Touch Handles", "Selection ends a finger can drag");
    }

    [CustomEditor(typeof(LetteraMeshLoupe)), CanEditMultipleObjects]
    internal sealed class LetteraLoupeEditor : UnityEditor.Editor
    {
        public override VisualElement CreateInspectorGUI() => LtFrame.Small(serializedObject, "Mesh Loupe", "Magnifies the text under a finger");
    }

    [CustomEditor(typeof(LetteraFieldPressArea)), CanEditMultipleObjects]
    internal sealed class LetteraFieldPressAreaEditor : UnityEditor.Editor
    {
        public override VisualElement CreateInspectorGUI() => LtFrame.Small(serializedObject, "Press Area", "A press here gives its field the focus, so that a box takes a click as its text does");
    }

    [CustomEditor(typeof(LetteraCommandControl)), CanEditMultipleObjects]
    internal sealed class LetteraCommandControlEditor : UnityEditor.Editor
    {
        public override VisualElement CreateInspectorGUI()
        {
            VisualElement root = LtUI.Root();
            VisualElement page = LtFrame.Header(root, "Command Control", "A button or toggle that runs a command");
            page.Add(LtFrame.Card(serializedObject, "", "m_Target"));
            var items = new List<(string, string, object)> { ("None", null, null) };
            foreach ((string _, string title, Type type) in LetteraRuleCatalog.Commands) items.Add((title, null, type));
            page.Add(LtUI.Card("", new LtPick("Command", () =>
            {
                object current = serializedObject.FindProperty("m_Command").managedReferenceValue;
                return current == null ? "None" : LtFrame.Words(current.GetType()).Replace(" Command", "");
            }, () => items, chosen =>
            {
                serializedObject.Update();
                serializedObject.FindProperty("m_Command").managedReferenceValue = chosen != null ? Activator.CreateInstance((Type)chosen) : null;
                serializedObject.ApplyModifiedProperties();
            }, "What this control runs.")));
            return root;
        }
    }

    [CustomEditor(typeof(LetteraDropdown)), CanEditMultipleObjects]
    internal sealed class LetteraDropdownEditor : UnityEditor.Editor
    {
        public override VisualElement CreateInspectorGUI()
        {
            VisualElement root = LtUI.Root();
            VisualElement page = LtFrame.Header(root, "Dropdown", "Pick one of several options");
            SerializedProperty P(string name) => serializedObject.FindProperty(name);
            page.Add(LtUI.Card("", new LtList("Options", P("m_Options"), (option, index) => LtUI.Field("", option), "No options yet.")));
            page.Add(LtUI.Card("Value", LtUI.Field("Value", P("m_Value"), "The chosen option, counted from 0.")));
            page.Add(LtFrame.Card(serializedObject, "Shown In", "m_CaptionText", "m_CaptionImage", "m_Placeholder"));
            page.Add(LtFrame.Card(serializedObject, "List", "m_Template", "m_RowPrototype", "m_ParseMarkup", "m_CloseOnSelect", "m_FadeSeconds"));
            page.Add(LtFrame.Card(serializedObject, "Events", "m_OnValueChanged"));
            return root;
        }
    }

    [CustomEditor(typeof(LetteraDropdownRow)), CanEditMultipleObjects]
    internal sealed class LetteraDropdownRowEditor : UnityEditor.Editor
    {
        public override VisualElement CreateInspectorGUI() => LtFrame.Small(serializedObject, "Dropdown Row", "One line of a dropdown's list");
    }

    [CustomEditor(typeof(LetteraKeepFocus)), CanEditMultipleObjects]
    internal sealed class LetteraKeepFocusEditor : UnityEditor.Editor
    {
        public override VisualElement CreateInspectorGUI() => LtFrame.Small(serializedObject, "Keep Focus", "Pressing this object does not end editing");
    }

    [CustomEditor(typeof(LetteraWorldRaycaster)), CanEditMultipleObjects]
    internal sealed class LetteraWorldRaycasterEditor : UnityEditor.Editor
    {
        public override VisualElement CreateInspectorGUI() => LtFrame.Small(serializedObject, "World Raycaster", "Gives world text pointer events", "m_Blockers");
    }

    [CustomEditor(typeof(LetteraPasswordToggle)), CanEditMultipleObjects]
    internal sealed class LetteraPasswordToggleEditor : UnityEditor.Editor
    {
        public override VisualElement CreateInspectorGUI() => LtFrame.Small(serializedObject, "Password Toggle", "Shows and hides a password field's value");
    }

    [CustomEditor(typeof(LetteraFieldPreset)), CanEditMultipleObjects]
    internal sealed class LetteraFieldPresetEditor : UnityEditor.Editor
    {
        public override VisualElement CreateInspectorGUI()
        {
            VisualElement root = LtUI.Root();
            VisualElement page = LtFrame.Header(root, "Field Preset", "One rule list for many fields");
            page.Add(LtUI.Card("", new LetteraReferenceList(serializedObject.FindProperty("m_Rules"), () => LetteraRuleCatalog.Rules, null, "Rules", "No rules yet.")));
            page.Add(LetteraFindUses.Card(target));
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
        internal static void Selectable(MenuCommand c) => FromSlot(c, LetteraSettings.Instance.Editing.SelectablePrefab, p => LetteraFieldFactory.CreateSelectableText(p));

        [MenuItem("GameObject/Lettera/Editable Text", false, 13)]
        internal static void Editable(MenuCommand c) => FromSlot(c, LetteraSettings.Instance.Editing.EditablePrefab, p => LetteraFieldFactory.CreateEditableText(p));

        [MenuItem("GameObject/Lettera/Input Field", false, 14)]
        internal static void Input(MenuCommand c) => FromSlot(c, LetteraSettings.Instance.Editing.InputFieldPrefab, p => LetteraFieldFactory.CreateInputField(p));

        [MenuItem("GameObject/Lettera/Text Area", false, 15)]
        internal static void Area(MenuCommand c) => FromSlot(c, LetteraSettings.Instance.Editing.TextAreaPrefab, p => LetteraFieldFactory.CreateTextArea(p));

        [MenuItem("GameObject/Lettera/Password Field", false, 16)]
        internal static void Password(MenuCommand c) => FromSlot(c, LetteraSettings.Instance.Editing.PasswordFieldPrefab, p => LetteraFieldFactory.CreatePasswordField(p));

        [MenuItem("GameObject/Lettera/Dropdown", false, 17)]
        internal static void Dropdown(MenuCommand c) => FromSlot(c, LetteraSettings.Instance.Editing.DropdownPrefab, p => LetteraFieldFactory.CreateDropdown(p));

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
                row.Add(Chip("Navigator", typeof(LetteraRangeNavigator)));
                row.Add(Chip("Scroller", typeof(LetteraDocumentScroller)));
                var first = targets.Length > 0 ? targets[0] as Component : null;
                IEnumerable<LetteraStyle> styles = (first as ITextHost)?.Data.Styles;
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
                row.Add(LtUI.Clickable(previewing ? "■ Stop Preview" : "▶ Preview Motion", "lt-chip lt-chip--click", "Runs motion in edit mode. Nothing is written, and it ends with this inspector.", () =>
                {
                    previewing = !previewing;
                    if (previewing) LetteraMotionPreview.Begin();
                    else LetteraMotionPreview.End();
                    Refresh();
                }));
                foreach (string warning in LetteraMotionScan.Warnings(first, styles)) row.Add(LtUI.Text(warning, "lt-chip lt-chip--orange"));
            }
            // Off: adds it. On: a click goes to it, and its small cross takes it away.
            VisualElement Chip(string title, Type type)
            {
                bool has = targets.All(t => t != null && ((Component)t).GetComponent(type) != null);
                if (!has)
                {
                    return LtUI.Clickable("+ " + title, "lt-chip lt-chip--click", "Adds " + title + " to this object.", () =>
                    {
                        foreach (UnityEngine.Object t in targets)
                        {
                            var component = (Component)t;
                            if (component.GetComponent(type) == null) Undo.AddComponent(component.gameObject, type);
                        }
                        Refresh();
                    });
                }
                Label go = LtUI.Clickable(title, "lt-chip__part", "Goes to " + title + " on this object.", () =>
                {
                    var component = (Component)targets[0];
                    Component existing = component.GetComponent(type);
                    if (existing == null) return;
                    UnityEditorInternal.InternalEditorUtility.SetIsInspectorExpanded(existing, true);
                    EditorGUIUtility.PingObject(existing);
                    ActiveEditorTracker.sharedTracker.ForceRebuild();
                });
                Label remove = LtUI.Clickable("×", "lt-chip__part lt-chip__remove", "Removes " + title + " from this object.", () =>
                {
                    foreach (UnityEngine.Object t in targets)
                    {
                        var component = (Component)t;
                        if (type == typeof(LetteraSelectable))
                        {
                            Component editable = component.GetComponent<LetteraEditable>();
                            if (editable != null) Undo.DestroyObjectImmediate(editable);
                        }
                        Component existing = component.GetComponent(type);
                        if (existing != null) Undo.DestroyObjectImmediate(existing);
                    }
                    Refresh();
                });
                return LtUI.El("lt-chip lt-chip--accent lt-chip--pair", go, remove);
            }
            Refresh();
            return LtUI.Card("", row);
        }
    }
}

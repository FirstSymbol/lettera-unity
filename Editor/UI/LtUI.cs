using System;
using System.Reflection;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace FirstSymbol.Lettera.Editor
{
    /// <summary>Building blocks of the Lettera inspector, styled by LetteraInspector.uss. Public, so that a custom effect's or a text subclass's drawer can use them.</summary>
    public static class LtUI
    {
        static StyleSheet s_Sheet;

        static StyleSheet Sheet
        {
            get
            {
                if (s_Sheet != null) return s_Sheet;
                foreach (string guid in AssetDatabase.FindAssets("LetteraInspector t:StyleSheet"))
                {
                    s_Sheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(AssetDatabase.GUIDToAssetPath(guid));
                    if (s_Sheet != null) break;
                }
                return s_Sheet;
            }
        }

        public static VisualElement Root()
        {
            var root = new VisualElement();
            if (Sheet != null) root.styleSheets.Add(Sheet);
            root.AddToClassList("lt-root");
            if (!EditorGUIUtility.isProSkin) root.AddToClassList("lt--light");
            return root;
        }

        public static VisualElement El(string classes, params VisualElement[] children)
        {
            var element = new VisualElement();
            foreach (string c in classes.Split(' '))
                if (c.Length > 0)
                    element.AddToClassList(c);
            foreach (VisualElement child in children) element.Add(child);
            return element;
        }

        public static Label Text(string text, string classes)
        {
            // Markup is what this inspector shows, so labels must not interpret it.
            var label = new Label(text) { enableRichText = false };
            foreach (string c in classes.Split(' '))
                if (c.Length > 0)
                    label.AddToClassList(c);
            return label;
        }

        public static Label Clickable(string text, string classes, string tooltip, Action onClick)
        {
            Label label = Text(text, classes);
            label.tooltip = tooltip;
            label.RegisterCallback<ClickEvent>(e =>
            {
                e.StopPropagation();
                onClick?.Invoke();
            });
            return label;
        }

        public static VisualElement Card(string title, params VisualElement[] children)
        {
            VisualElement card = El("lt-card");
            if (!string.IsNullOrEmpty(title)) card.Add(Text(title.ToUpperInvariant(), "lt-card__title"));
            foreach (VisualElement child in children) card.Add(child);
            return card;
        }

        /// <summary>A label and a control, side by side. Given the property, it shows a prefab override and an animated value as Unity's own rows do.</summary>
        public static VisualElement Row(string label, VisualElement field, string tooltip = null, SerializedProperty mark = null)
        {
            VisualElement row = El("lt-row");
            row.tooltip = tooltip;
            Label caption = null;
            if (!string.IsNullOrEmpty(label))
            {
                caption = Text(label, "lt-row__label");
                row.Add(caption);
            }
            field.AddToClassList("lt-row__field");
            row.Add(field);
            if (mark != null) Mark(row, caption, mark);
            return row;
        }

        /// <summary>Bold label on a prefab override, with Revert on the right click; a tint on an animated value.</summary>
        public static void Mark(VisualElement row, Label label, SerializedProperty property)
        {
            SerializedProperty bound = property.Copy();
            void Refresh(SerializedProperty _)
            {
                if (bound.serializedObject.targetObject == null) return;
                label?.EnableInClassList("lt-row__label--override", bound.prefabOverride);
                row.EnableInClassList("lt-row--override", bound.prefabOverride);
                row.EnableInClassList("lt-row--animated", AnimationMode.InAnimationMode() && AnimationMode.IsPropertyAnimated(bound.serializedObject.targetObject, bound.propertyPath));
            }
            Refresh(bound);
            row.TrackPropertyValue(bound, Refresh);
            row.AddManipulator(new ContextualMenuManipulator(e =>
            {
                if (bound.prefabOverride) e.menu.AppendAction("Revert", _ => Revert(bound));
            }));
        }

        /// <summary>What the right click's Revert does.</summary>
        public static void Revert(SerializedProperty property)
        {
            property.serializedObject.Update();
            PrefabUtility.RevertPropertyOverride(property, InteractionMode.UserAction);
            property.serializedObject.Update();
        }

        /// <summary>An editor for any property, in a labelled row, in the house's own control for its type.</summary>
        public static VisualElement Field(string label, SerializedProperty property, string tooltip = null)
        {
            SerializedProperty bound = property.Copy();
            switch (bound.propertyType)
            {
                case SerializedPropertyType.Integer:
                case SerializedPropertyType.Float:
                {
                    var number = new LtNumber(label, bound, tooltip);
                    FieldInfo info = FieldOf(bound, out Type _);
                    if (info != null)
                    {
                        var range = info.GetCustomAttribute<RangeAttribute>();
                        if (range != null) number.Limit(range.min, range.max);
                        var least = info.GetCustomAttribute<MinAttribute>();
                        if (least != null) number.Limit(least.min, float.PositiveInfinity);
                    }
                    return number;
                }
                case SerializedPropertyType.Boolean:
                    return SwitchRow(label, bound, tooltip);
                case SerializedPropertyType.Enum:
                    return new LtEnum(label, bound, tooltip);
                case SerializedPropertyType.ObjectReference:
                {
                    FieldOf(bound, out Type type);
                    return new LtObject(label, bound, type, tooltip);
                }
                case SerializedPropertyType.Color:
                    return new LtColor(label, bound, tooltip);
                case SerializedPropertyType.String:
                {
                    var text = new TextField { isDelayed = true };
                    text.AddToClassList("lt-text-input");
                    text.BindProperty(bound);
                    return Row(label, text, tooltip, bound);
                }
                case SerializedPropertyType.ManagedReference:
                    return LetteraPaintField.ManagedReference(bound);
                case SerializedPropertyType.Generic:
                    if (bound.isArray) return DefaultList(label, bound, tooltip);
                    return Group(label, bound, tooltip);
                default:
                {
                    var field = new PropertyField(bound, "");
                    field.BindProperty(bound);
                    return Row(label, field, tooltip);
                }
            }
        }

        static VisualElement DefaultList(string label, SerializedProperty array, string tooltip)
        {
            var list = new LtList(label, array, (element, index) => Field("", element), "None yet.");
            list.tooltip = tooltip;
            return list;
        }

        static VisualElement Group(string label, SerializedProperty property, string tooltip)
        {
            VisualElement group = El("lt-group");
            group.tooltip = tooltip;
            if (!string.IsNullOrEmpty(label)) group.Add(Text(label, "lt-group__title"));
            SerializedProperty end = property.GetEndProperty();
            SerializedProperty child = property.Copy();
            bool enter = true;
            while (child.NextVisible(enter) && !SerializedProperty.EqualContents(child, end))
            {
                enter = false;
                group.Add(Field(child.displayName, child, child.tooltip));
            }
            return group;
        }

        /// <summary>The C# field behind a property, and the type it holds: the element's, for a list.</summary>
        internal static FieldInfo FieldOf(SerializedProperty property, out Type held)
        {
            held = null;
            UnityEngine.Object target = property.serializedObject.targetObject;
            if (target == null) return null;
            Type type = target.GetType();
            FieldInfo info = null;
            string[] parts = property.propertyPath.Split('.');
            string path = "";
            for (int i = 0; i < parts.Length; i++)
            {
                string part = parts[i];
                path = i == 0 ? part : path + "." + part;
                if (part == "Array")
                {
                    if (i + 1 >= parts.Length) break;
                    i++;
                    path += "." + parts[i];
                    type = ElementOf(type);
                }
                else
                {
                    info = null;
                    for (Type at = type; at != null && info == null; at = at.BaseType)
                        info = at.GetField(part, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                    if (info == null) return null;
                    type = info.FieldType;
                }
                if (info != null && info.IsDefined(typeof(SerializeReference), false) && i + 1 < parts.Length)
                {
                    SerializedProperty reference = property.serializedObject.FindProperty(path);
                    if (reference != null && reference.propertyType == SerializedPropertyType.ManagedReference && reference.managedReferenceValue != null)
                        type = reference.managedReferenceValue.GetType();
                }
            }
            held = property.isArray && property.propertyType != SerializedPropertyType.String ? ElementOf(type) : type;
            return info;
        }

        static Type ElementOf(Type type)
        {
            if (type.IsArray) return type.GetElementType();
            return type.IsGenericType ? type.GetGenericArguments()[0] : type;
        }

        /// <summary>A row with a switch on it. A click anywhere on the row turns it over.</summary>
        public static VisualElement SwitchRow(string label, SerializedProperty property, string tooltip = null)
        {
            SerializedProperty bound = property.Copy();
            VisualElement control = Switch(bound);
            VisualElement holder = El("lt-switch-holder", control);
            VisualElement row = Row(label, holder, tooltip, bound);
            row.AddToClassList("lt-row--toggle");
            row.RegisterCallback<ClickEvent>(e => Flip(bound));
            return row;
        }

        /// <summary>A label and a switch to stand beside something else on one row.</summary>
        public static VisualElement InlineSwitch(string label, SerializedProperty property, string tooltip = null)
        {
            SerializedProperty bound = property.Copy();
            VisualElement control = Switch(bound);
            Label caption = Text(label, "lt-inline__label");
            VisualElement inline = El("lt-inline", caption, control);
            inline.tooltip = tooltip;
            inline.RegisterCallback<ClickEvent>(e =>
            {
                e.StopPropagation();
                Flip(bound);
            });
            Mark(inline, caption, bound);
            return inline;
        }

        static void Flip(SerializedProperty bound)
        {
            bound.serializedObject.Update();
            bound.boolValue = bound.hasMultipleDifferentValues || !bound.boolValue;
            bound.serializedObject.ApplyModifiedProperties();
        }

        /// <summary>A two-state switch bound to a bool property.</summary>
        public static VisualElement Switch(SerializedProperty property)
        {
            SerializedProperty bound = property.Copy();
            VisualElement track = El("lt-switch", El("lt-switch__knob"));
            void Refresh(SerializedProperty p)
            {
                track.EnableInClassList("lt-switch--mixed", bound.hasMultipleDifferentValues);
                track.EnableInClassList("lt-switch--on", !bound.hasMultipleDifferentValues && bound.boolValue);
            }
            Refresh(bound);
            track.TrackPropertyValue(bound, Refresh);
            track.RegisterCallback<ClickEvent>(e =>
            {
                e.StopPropagation();
                Flip(bound);
                Refresh(bound);
            });
            return track;
        }

        /// <summary>A switch for something that is no property, such as a preference of this user.</summary>
        public static VisualElement Toggle(Func<bool> get, Action<bool> set)
        {
            VisualElement track = El("lt-switch", El("lt-switch__knob"));
            track.EnableInClassList("lt-switch--on", get());
            track.RegisterCallback<ClickEvent>(e =>
            {
                e.StopPropagation();
                set(!get());
                track.EnableInClassList("lt-switch--on", get());
            });
            return track;
        }

        /// <summary>A segmented control bound to an enum property; one label per option.</summary>
        public static VisualElement Segments(SerializedProperty property, params string[] options)
        {
            SerializedProperty bound = property.Copy();
            VisualElement seg = El("lt-seg");
            var items = new Label[options.Length];
            for (int i = 0; i < options.Length; i++)
            {
                int index = i;
                items[i] = Clickable(options[i], "lt-seg__item", null, () =>
                {
                    bound.serializedObject.Update();
                    bound.enumValueIndex = index;
                    bound.serializedObject.ApplyModifiedProperties();
                });
                seg.Add(items[i]);
            }
            void Refresh(SerializedProperty p)
            {
                for (int i = 0; i < items.Length; i++) items[i].EnableInClassList("lt-seg__item--on", !bound.hasMultipleDifferentValues && i == bound.enumValueIndex);
            }
            Refresh(bound);
            seg.TrackPropertyValue(bound, Refresh);
            return seg;
        }

        /// <summary>A segmented control that is no property. Returns the items, to colour one.</summary>
        public static VisualElement Choice(string[] options, Func<int> get, Action<int> set, out Label[] items)
        {
            VisualElement seg = El("lt-seg");
            var made = new Label[options.Length];
            void Paint()
            {
                for (int i = 0; i < made.Length; i++) made[i].EnableInClassList("lt-seg__item--on", i == get());
            }
            for (int i = 0; i < options.Length; i++)
            {
                int index = i;
                made[i] = Clickable(options[i], "lt-seg__item", null, () =>
                {
                    set(index);
                    Paint();
                });
                seg.Add(made[i]);
            }
            Paint();
            items = made;
            return seg;
        }

        /// <summary>A segmented control of alignment icons bound to an enum property.</summary>
        public static VisualElement IconSegments(SerializedProperty property, (LtIcon.Kind kind, string tip)[] options)
        {
            SerializedProperty bound = property.Copy();
            VisualElement seg = El("lt-seg lt-seg--icons");
            var items = new VisualElement[options.Length];
            for (int i = 0; i < options.Length; i++)
            {
                int index = i;
                VisualElement item = El("lt-seg__item");
                item.tooltip = options[i].tip;
                item.Add(new LtIcon(options[i].kind));
                item.RegisterCallback<ClickEvent>(_ =>
                {
                    bound.serializedObject.Update();
                    bound.enumValueIndex = index;
                    bound.serializedObject.ApplyModifiedProperties();
                });
                items[i] = item;
                seg.Add(item);
            }
            void Refresh(SerializedProperty p)
            {
                for (int i = 0; i < items.Length; i++) items[i].EnableInClassList("lt-seg__item--on", !bound.hasMultipleDifferentValues && i == bound.enumValueIndex);
            }
            Refresh(bound);
            seg.TrackPropertyValue(bound, Refresh);
            return seg;
        }

        /// <summary>A slider with a number field, both bound to a float property.</summary>
        public static VisualElement SizeSlider(SerializedProperty property, float min, float max)
        {
            VisualElement row = El("lt-size-row");
            var slider = new Slider(min, max);
            slider.AddToClassList("lt-size-row__slider");
            slider.BindProperty(property);
            var number = new FloatField();
            number.AddToClassList("lt-size-row__value");
            number.AddToClassList("lt-text-input");
            number.BindProperty(property);
            row.Add(slider);
            row.Add(number);
            return row;
        }

        /// <summary>Four compact fields for a Vector4 padding: left, bottom, right, top.</summary>
        public static VisualElement Padding(SerializedProperty property)
        {
            VisualElement box = El("lt-padding");
            foreach ((string axis, string caption) in new[] { ("x", "L"), ("w", "T"), ("z", "R"), ("y", "B") })
            {
                var field = new FloatField(caption);
                field.AddToClassList("lt-padding__cell");
                field.BindProperty(property.FindPropertyRelative(axis));
                box.Add(field);
            }
            return box;
        }
    }

    /// <summary>A small icon drawn with vector strokes, so it stays sharp at any display scale.</summary>
    public sealed class LtIcon : VisualElement
    {
        public enum Kind { AlignStart, AlignCenter, AlignEnd, AlignLeft, AlignRight, AlignJustify, AlignTop, AlignMiddle, AlignBottom, Expand, Eye, GoTo, Pick, Scene, Chevron, More }

        readonly Kind m_Kind;

        public LtIcon(Kind kind)
        {
            m_Kind = kind;
            AddToClassList("lt-icon");
            style.width = 14;
            style.height = 14;
            style.flexShrink = 0;
            pickingMode = PickingMode.Ignore;
            generateVisualContent += Draw;
        }

        void Draw(MeshGenerationContext context)
        {
            Painter2D p = context.painter2D;
            p.strokeColor = resolvedStyle.color;
            p.lineWidth = 1.5f;
            p.lineCap = LineCap.Round;

            void Line(float x0, float y0, float x1, float y1)
            {
                p.BeginPath();
                p.MoveTo(new Vector2(x0, y0));
                p.LineTo(new Vector2(x1, y1));
                p.Stroke();
            }

            // Text lines of different lengths; x gives where the short ones sit.
            void Horizontal(float shortStart, float shortEnd)
            {
                Line(1.5f, 3, 12.5f, 3);
                Line(shortStart, 7, shortEnd, 7);
                Line(1.5f, 11, 12.5f, 11);
            }

            void Circle(float x, float y, float radius)
            {
                p.BeginPath();
                p.Arc(new Vector2(x, y), radius, Angle.Degrees(0), Angle.Degrees(360));
                p.Stroke();
            }

            switch (m_Kind)
            {
                case Kind.AlignLeft: Horizontal(1.5f, 8); break;
                case Kind.AlignRight: Horizontal(6, 12.5f); break;
                case Kind.AlignCenter: Horizontal(3.5f, 10.5f); break;
                case Kind.AlignJustify: Horizontal(1.5f, 12.5f); break;
                case Kind.AlignStart:
                    Horizontal(3, 8);
                    Line(0.5f, 1, 0.5f, 13);
                    break;
                case Kind.AlignEnd:
                    Horizontal(6, 11);
                    Line(13.5f, 1, 13.5f, 13);
                    break;
                case Kind.AlignTop:
                    Line(1.5f, 1.5f, 12.5f, 1.5f);
                    Line(3.5f, 5, 10.5f, 5);
                    Line(3.5f, 8, 10.5f, 8);
                    break;
                case Kind.AlignMiddle:
                    Line(3.5f, 5, 10.5f, 5);
                    Line(3.5f, 9, 10.5f, 9);
                    Line(1.5f, 1.5f, 12.5f, 1.5f);
                    Line(1.5f, 12.5f, 12.5f, 12.5f);
                    break;
                case Kind.AlignBottom:
                    Line(3.5f, 6, 10.5f, 6);
                    Line(3.5f, 9, 10.5f, 9);
                    Line(1.5f, 12.5f, 12.5f, 12.5f);
                    break;
                case Kind.Expand:
                    Line(8.5f, 1.5f, 12.5f, 1.5f);
                    Line(12.5f, 1.5f, 12.5f, 5.5f);
                    Line(12.5f, 1.5f, 8, 6);
                    Line(5.5f, 12.5f, 1.5f, 12.5f);
                    Line(1.5f, 12.5f, 1.5f, 8.5f);
                    Line(1.5f, 12.5f, 6, 8);
                    break;
                case Kind.Eye:
                    p.BeginPath();
                    p.MoveTo(new Vector2(1, 7));
                    p.QuadraticCurveTo(new Vector2(7, 0.5f), new Vector2(13, 7));
                    p.QuadraticCurveTo(new Vector2(7, 13.5f), new Vector2(1, 7));
                    p.Stroke();
                    Circle(7, 7, 1.8f);
                    break;
                case Kind.GoTo:
                    Line(2, 7, 12, 7);
                    Line(8, 3, 12, 7);
                    Line(8, 11, 12, 7);
                    break;
                case Kind.Pick:
                    Circle(7, 7, 5);
                    Circle(7, 7, 1.2f);
                    break;
                case Kind.Scene:
                    Line(7, 1.5f, 12.5f, 4.5f);
                    Line(12.5f, 4.5f, 12.5f, 10);
                    Line(12.5f, 10, 7, 12.5f);
                    Line(7, 12.5f, 1.5f, 10);
                    Line(1.5f, 10, 1.5f, 4.5f);
                    Line(1.5f, 4.5f, 7, 1.5f);
                    Line(1.5f, 4.5f, 7, 7.5f);
                    Line(12.5f, 4.5f, 7, 7.5f);
                    Line(7, 7.5f, 7, 12.5f);
                    break;
                case Kind.Chevron:
                    Line(3.5f, 5.5f, 7, 9);
                    Line(7, 9, 10.5f, 5.5f);
                    break;
                case Kind.More:
                    Circle(2.5f, 7, 0.6f);
                    Circle(7, 7, 0.6f);
                    Circle(11.5f, 7, 0.6f);
                    break;
            }
        }
    }
}

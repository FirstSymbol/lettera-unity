using System;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace FirstSymbol.Lettera.Editor
{
    /// <summary>Building blocks of the Lettera inspector, styled by LetteraInspector.uss.</summary>
    internal static class LtUI
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
            foreach (string c in classes.Split(' ')) element.AddToClassList(c);
            foreach (VisualElement child in children) element.Add(child);
            return element;
        }

        public static Label Text(string text, string classes)
        {
            // Markup is what this inspector shows, so labels must not interpret it.
            var label = new Label(text) { enableRichText = false };
            foreach (string c in classes.Split(' ')) label.AddToClassList(c);
            return label;
        }

        public static Label Clickable(string text, string classes, string tooltip, Action onClick)
        {
            Label label = Text(text, classes);
            label.tooltip = tooltip;
            label.RegisterCallback<ClickEvent>(e =>
            {
                e.StopPropagation();
                onClick();
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

        /// <summary>A label and a control, side by side.</summary>
        public static VisualElement Row(string label, VisualElement field, string tooltip = null)
        {
            VisualElement row = El("lt-row");
            row.tooltip = tooltip;
            row.Add(Text(label, "lt-row__label"));
            field.AddToClassList("lt-row__field");
            row.Add(field);
            return row;
        }

        /// <summary>An editor for any property, in a labelled row.</summary>
        public static VisualElement Field(string label, SerializedProperty property, string tooltip = null)
        {
            var field = new PropertyField(property, "");
            field.BindProperty(property);
            return Row(label, field, tooltip);
        }

        /// <summary>A two-state switch bound to a bool property.</summary>
        public static VisualElement Switch(SerializedProperty property)
        {
            SerializedProperty bound = property.Copy();
            VisualElement track = El("lt-switch", El("lt-switch__knob"));
            void Refresh(SerializedProperty p) => track.EnableInClassList("lt-switch--on", p.boolValue);
            Refresh(bound);
            track.TrackPropertyValue(bound, Refresh);
            track.RegisterCallback<ClickEvent>(_ =>
            {
                bound.serializedObject.Update();
                bound.boolValue = !bound.boolValue;
                bound.serializedObject.ApplyModifiedProperties();
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
                for (int i = 0; i < items.Length; i++) items[i].EnableInClassList("lt-seg__item--on", i == p.enumValueIndex);
            }
            Refresh(bound);
            seg.TrackPropertyValue(bound, Refresh);
            return seg;
        }

        /// <summary>A segmented control of alignment icons bound to an enum property.</summary>
        public static VisualElement IconSegments(SerializedProperty property, (LtIcon.Kind kind, string tip)[] options)
        {
            SerializedProperty bound = property.Copy();
            VisualElement seg = El("lt-seg");
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
                for (int i = 0; i < items.Length; i++) items[i].EnableInClassList("lt-seg__item--on", i == p.enumValueIndex);
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
    internal sealed class LtIcon : VisualElement
    {
        public enum Kind { AlignStart, AlignCenter, AlignEnd, AlignLeft, AlignRight, AlignJustify, AlignTop, AlignMiddle, AlignBottom }

        readonly Kind m_Kind;

        public LtIcon(Kind kind)
        {
            m_Kind = kind;
            style.width = 14;
            style.height = 14;
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

            switch (m_Kind)
            {
                case Kind.AlignLeft: Horizontal(1.5f, 8); break;
                case Kind.AlignRight: Horizontal(6, 12.5f); break;
                case Kind.AlignCenter: Horizontal(3.5f, 10.5f); break;
                case Kind.AlignJustify: Horizontal(1.5f, 12.5f); break;
                case Kind.AlignStart:
                    Horizontal(1.5f, 8);
                    Line(0.5f, 1, 0.5f, 13);
                    break;
                case Kind.AlignEnd:
                    Horizontal(6, 12.5f);
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
            }
        }
    }
}

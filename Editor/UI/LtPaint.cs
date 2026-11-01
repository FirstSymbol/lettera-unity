using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace FirstSymbol.Lettera.Editor
{
    /// <summary>A paint in one row: a swatch of its color, gradient or texture and a word on what it is. A click opens the whole of it: kind, stops, how it lies on the text.</summary>
    public sealed class LtPaint : VisualElement
    {
        const int SwatchWidth = 64;
        static readonly Color32[] s_Pixels = new Color32[SwatchWidth];

        readonly SerializedProperty m_Property;
        readonly LetteraLayerEffect m_Effect;
        readonly bool m_IsChoice;
        readonly VisualElement m_Slot, m_Swatch;
        readonly Label m_Words;
        Texture2D m_Ramp;

        public LtPaint(string label, SerializedProperty property, object owner = null, string tooltip = null)
        {
            m_Property = property.Copy();
            m_Effect = owner as LetteraLayerEffect;
            m_IsChoice = property.type == nameof(LetteraPaintChoice);
            AddToClassList("lt-row");
            AddToClassList("lt-paint-row");
            this.tooltip = tooltip;
            Label caption = LtUI.Text(label ?? "", "lt-row__label");
            if (!string.IsNullOrEmpty(label)) Add(caption);
            m_Slot = LtUI.El("lt-row__field lt-paint__slot");
            m_Swatch = LtUI.El("lt-paint__swatch");
            m_Words = LtUI.Text("", "lt-paint__words");
            m_Slot.Add(m_Swatch);
            m_Slot.Add(m_Words);
            m_Slot.tooltip = "Opens this paint.";
            m_Slot.RegisterCallback<ClickEvent>(e =>
            {
                e.StopPropagation();
                Open();
            });
            Add(m_Slot);
            Refresh(m_Property);
            this.TrackPropertyValue(m_Property, Refresh);
            LtUI.Mark(this, caption, m_Property);
            RegisterCallback<DetachFromPanelEvent>(_ =>
            {
                if (m_Ramp != null) Object.DestroyImmediate(m_Ramp);
                m_Ramp = null;
            });
        }

        internal string Words => m_Words.text;

        /// <summary>What the popup holds, also for a test or a picture.</summary>
        internal VisualElement BuildPage()
        {
            var scroll = new ScrollView();
            scroll.style.flexGrow = 1;
            scroll.Add(m_IsChoice ? LetteraPaintField.Choice(m_Property, m_Effect) : LetteraPaintField.Paint(m_Property));
            return scroll;
        }

        void Open() => LtPopup.Show(m_Slot, new Vector2(Mathf.Max(m_Slot.worldBound.width, 340), 420), BuildPage);

        void Refresh(SerializedProperty _)
        {
            if (m_Property.serializedObject.targetObject == null) return;
            if (m_Property.hasMultipleDifferentValues)
            {
                m_Words.text = "—";
                Show(null);
                return;
            }
            LetteraPaint paint;
            string words;
            if (m_IsChoice)
            {
                var kind = (LetteraPaintChoiceKind)m_Property.FindPropertyRelative("Kind").enumValueIndex;
                if (kind == LetteraPaintChoiceKind.Default)
                {
                    m_Words.text = "This effect's own color";
                    Show(null);
                    return;
                }
                if (kind == LetteraPaintChoiceKind.Named)
                {
                    string name = m_Property.FindPropertyRelative("SwatchName").stringValue;
                    LetteraSwatch swatch = LetteraPaintField.SwatchesOf(m_Effect)?.FirstOrDefault(s => s != null && s.Name == name);
                    m_Words.text = string.IsNullOrEmpty(name) ? "No swatch chosen" : swatch != null ? name : name + ", not found";
                    Show(swatch?.Paint);
                    return;
                }
                paint = ReadPaint(m_Property.FindPropertyRelative("Inline"));
            }
            else
            {
                paint = ReadPaint(m_Property);
            }
            words = Describe(paint);
            m_Words.text = words;
            Show(paint);
        }

        /// <summary>A paint in a few words.</summary>
        internal static string Describe(LetteraPaint paint)
        {
            switch (paint.Source)
            {
                case LetteraPaintSourceKind.Gradient:
                    int stops = paint.Gradient?.Stops.Count ?? 0;
                    return "Gradient, " + stops + (stops == 1 ? " stop" : " stops");
                case LetteraPaintSourceKind.Texture:
                    return paint.Texture != null ? paint.Texture.name : "No texture";
                default:
                    return "#" + ColorUtility.ToHtmlStringRGBA(paint.Color);
            }
        }

        internal static LetteraPaint ReadPaint(SerializedProperty property)
        {
            var paint = new LetteraPaint
            {
                Source = (LetteraPaintSourceKind)property.FindPropertyRelative("Source").enumValueIndex,
                Color = property.FindPropertyRelative("Color").colorValue,
                Texture = property.FindPropertyRelative("Texture").objectReferenceValue as Texture2D,
                Gradient = LetteraPaintField.ReadGradient(property.FindPropertyRelative("Gradient")),
            };
            return paint;
        }

        void Show(LetteraPaint paint) => Paint(m_Swatch, paint, ref m_Ramp);

        /// <summary>Puts a paint on an element's background. Nothing to show leaves it empty, which reads as none.</summary>
        internal static void Paint(VisualElement swatch, LetteraPaint paint, ref Texture2D ramp)
        {
            swatch.style.backgroundImage = StyleKeyword.None;
            swatch.style.backgroundColor = Color.clear;
            swatch.EnableInClassList("lt-paint__swatch--none", paint == null);
            if (paint == null) return;
            switch (paint.Source)
            {
                case LetteraPaintSourceKind.Solid:
                    swatch.style.backgroundColor = (Color)paint.Color;
                    break;
                case LetteraPaintSourceKind.Texture:
                    if (paint.Texture != null) swatch.style.backgroundImage = paint.Texture;
                    else swatch.EnableInClassList("lt-paint__swatch--none", true);
                    break;
                default:
                    if (paint.Gradient == null || paint.Gradient.Stops.Count == 0)
                    {
                        swatch.EnableInClassList("lt-paint__swatch--none", true);
                        return;
                    }
                    if (ramp == null) ramp = new Texture2D(SwatchWidth, 1, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave, wrapMode = TextureWrapMode.Clamp };
                    var sorted = paint.Gradient.Copy();
                    sorted.Stops.Sort((a, b) => a.Position.CompareTo(b.Position));
                    GradientAtlas.Bake(sorted, s_Pixels);
                    ramp.SetPixels32(s_Pixels);
                    ramp.Apply(false);
                    swatch.style.backgroundImage = ramp;
                    break;
            }
        }
    }

    /// <summary>The names of a paint library as rows, each with its own swatch.</summary>
    internal sealed class LtSwatchList : VisualElement
    {
        readonly List<Texture2D> m_Ramps = new List<Texture2D>();

        public LtSwatchList(List<LetteraSwatch> swatches, SerializedProperty name)
        {
            SerializedProperty bound = name.Copy();
            AddToClassList("lt-swatches");
            foreach (LetteraSwatch swatch in swatches.Where(s => s != null && !string.IsNullOrEmpty(s.Name)))
            {
                string chosen = swatch.Name;
                VisualElement dot = LtUI.El("lt-paint__swatch");
                Texture2D ramp = null;
                LtPaint.Paint(dot, swatch.Paint, ref ramp);
                if (ramp != null) m_Ramps.Add(ramp);
                VisualElement row = LtUI.El("lt-swatches__row", dot, LtUI.Text(chosen, "lt-paint__words"));
                row.EnableInClassList("lt-swatches__row--on", bound.stringValue == chosen);
                row.RegisterCallback<ClickEvent>(e =>
                {
                    e.StopPropagation();
                    bound.serializedObject.Update();
                    bound.stringValue = chosen;
                    bound.serializedObject.ApplyModifiedProperties();
                    foreach (VisualElement other in Children()) other.RemoveFromClassList("lt-swatches__row--on");
                    row.AddToClassList("lt-swatches__row--on");
                });
                Add(row);
            }
            RegisterCallback<DetachFromPanelEvent>(_ =>
            {
                foreach (Texture2D ramp in m_Ramps) Object.DestroyImmediate(ramp);
                m_Ramps.Clear();
            });
        }
    }
}

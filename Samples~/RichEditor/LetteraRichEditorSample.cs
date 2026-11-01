using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace FirstSymbol.Lettera.Samples
{
    /// <summary>
    /// A small word processor: a page, a toolbar that follows the caret, the keys you expect, and paste that keeps formatting.
    /// Ctrl or Cmd with B, I, U, Z and Y, and with C, X and V. Markup stays hidden while you type, and the field's value is still plain markup. Add this to an empty object in an empty scene and press Play.
    /// </summary>
    public class LetteraRichEditorSample : MonoBehaviour
    {
        const string Opening = "<size=150%><b>A page of your own</b></size>\n\nSelect words and press <b>B</b>, <i>I</i> or <u>U</u> above, or the keys. With nothing selected, a button says how the <b>next</b> words you type will look.\n\nPaste from a browser or a word processor, and its <color=#C0392B>colors</color>, <mark=#FFE27A>marks</mark> and <s>strikes</s> come with it.";

        LetteraEditable m_Field;
        LetteraText m_Status;
        RectTransform m_Bar;
        float m_BarX = 8;
        readonly List<(Image image, Type effect)> m_Toggles = new List<(Image, Type)>();
        static readonly Color Off = new Color(0.27f, 0.3f, 0.38f), On = new Color(0.25f, 0.5f, 0.95f);

        /// <summary>The field, for code around the sample.</summary>
        public LetteraEditable Field => m_Field;

        void Start()
        {
            if (FindAnyObjectByType<EventSystem>() == null)
            {
                var events = new GameObject("EventSystem", typeof(EventSystem));
                Type module = Type.GetType("UnityEngine.InputSystem.UI.InputSystemUIInputModule, Unity.InputSystem");
                if (module != null) events.AddComponent(module);
                else events.AddComponent<StandaloneInputModule>();
            }
            var canvasObject = new GameObject("Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasObject.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280, 720);
            var desk = canvasObject.AddComponent<Image>();
            desk.color = new Color(0.16f, 0.17f, 0.2f);
            desk.raycastTarget = false;

            m_Bar = Rect(canvasObject.transform, "Toolbar", new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, 52), new Vector2(0, -26));
            var barImage = m_Bar.gameObject.AddComponent<Image>();
            barImage.color = new Color(0.12f, 0.13f, 0.16f);
            barImage.raycastTarget = false;

            // The page is the field. A paper colour and dark words, in a column of a page's width.
            RectTransform pageRect = Rect(canvasObject.transform, "Page", new Vector2(0.5f, 0), new Vector2(0.5f, 1), new Vector2(820, -112), new Vector2(0, 4));
            GameObject area = LetteraFieldFactory.CreateTextArea(pageRect, "Write something...");
            var areaRect = (RectTransform)area.transform;
            areaRect.anchorMin = Vector2.zero;
            areaRect.anchorMax = Vector2.one;
            areaRect.offsetMin = areaRect.offsetMax = Vector2.zero;
            area.GetComponent<Image>().color = new Color(0.98f, 0.97f, 0.94f);
            m_Field = area.GetComponentInChildren<LetteraEditable>();
            var words = m_Field.GetComponent<LetteraText>();
            words.color = new Color(0.12f, 0.12f, 0.14f);
            words.FontSize = 22;
            words.Padding = new Vector4(40, 32, 40, 32);
            m_Field.AddRule(new FormattingRule());
            m_Field.SetSource(Opening);

            Toggle<BoldEffect>("B");
            Toggle<ItalicEffect>("I");
            Toggle<UnderlineEffect>("U");
            Toggle<StrikethroughEffect>("S");
            Gap();
            Act("Big", 58, () => m_Field.ApplyEffect<SizeEffect>("150%"));
            Act("Normal", 78, () => m_Field.RemoveEffect<SizeEffect>());
            Gap();
            Swatch(new Color(0.75f, 0.22f, 0.17f), "#C0392B");
            Swatch(new Color(0.15f, 0.45f, 0.8f), "#2673CC");
            Swatch(new Color(0.12f, 0.12f, 0.14f), null);
            Toggle<HighlightEffect>("Mark", 66, "#FFE27A");
            Gap();
            Act("Clear", 66, () => m_Field.ClearStyles());
            Gap();
            Act("Undo", 62, () => m_Field.Undo());
            Act("Redo", 62, () => m_Field.Redo());

            RectTransform statusRect = Rect(canvasObject.transform, "Status", new Vector2(0, 0), new Vector2(1, 0), new Vector2(-48, 28), new Vector2(0, 16));
            m_Status = statusRect.gameObject.AddComponent<LetteraText>();
            m_Status.FontSize = 15;
            m_Status.raycastTarget = false;
            m_Status.color = new Color(0.72f, 0.74f, 0.8f);
            m_Status.VerticalAlignment = LetteraVerticalAlignment.Middle;

            m_Field.ValueChanged += _ => Count();
            m_Field.CaretStylesChanged += Follow;
            Count();
        }

        // What covers the caret lights its button.
        void Follow(LetteraCaretStyles styles)
        {
            foreach ((Image image, Type effect) in m_Toggles)
            {
                bool on = false;
                foreach (LetteraEffect present in styles.Effects) on |= effect.IsInstanceOfType(present);
                foreach (LetteraEffect present in styles.Pending) on |= effect.IsInstanceOfType(present);
                image.color = on ? On : Off;
            }
        }

        void Count()
        {
            string plain = m_Field.PlainText;
            int count = 0;
            bool inside = false;
            foreach (char c in plain)
            {
                bool letter = !char.IsWhiteSpace(c);
                if (letter && !inside) count++;
                inside = letter;
            }
            m_Status.Text = "<noparse>" + count + " words, " + plain.Length + " characters. The value is markup: " + m_Field.Source.Length + " characters of it.</noparse>";
        }

        static RectTransform Rect(Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax, Vector2 size, Vector2 position)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.sizeDelta = size;
            rect.anchoredPosition = position;
            return rect;
        }

        void Gap() => m_BarX += 14;

        Image Button(string title, float width, Action action)
        {
            RectTransform rect = Rect(m_Bar, title, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(width, 36), new Vector2(m_BarX + width / 2, 0));
            m_BarX += width + 4;
            var image = rect.gameObject.AddComponent<Image>();
            image.color = Off;
            // Pressing the toolbar must not end editing.
            rect.gameObject.AddComponent<LetteraKeepFocus>();
            rect.gameObject.AddComponent<Button>().onClick.AddListener(() => action());
            if (string.IsNullOrEmpty(title)) return image;
            RectTransform labelRect = Rect(rect, "Label", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            var label = labelRect.gameObject.AddComponent<LetteraText>();
            label.FontSize = 18;
            label.raycastTarget = false;
            label.HorizontalAlignment = LetteraHorizontalAlignment.Center;
            label.VerticalAlignment = LetteraVerticalAlignment.Middle;
            label.Text = title;
            return image;
        }

        void Toggle<T>(string title, float width = 40, string parameter = null) where T : LetteraEffect
        {
            Image image = Button(title, width, () => m_Field.ToggleEffect<T>(parameter));
            m_Toggles.Add((image, typeof(T)));
        }

        void Act(string title, float width, Action action) => Button(title, width, action);

        void Swatch(Color color, string parameter)
        {
            Image image = Button("", 32, () =>
            {
                if (parameter == null) m_Field.RemoveEffect<ColorEffect>();
                else m_Field.ApplyEffect<ColorEffect>(parameter);
            });
            image.color = color;
        }
    }
}

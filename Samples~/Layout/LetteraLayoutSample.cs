using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace FirstSymbol.Lettera.Samples
{
    /// <summary>
    /// One box of text and a control for everything that lays it out: alignment, wrap, overflow, auto size, line height, trim, a curved baseline, padding.
    /// Add this to an empty object in an empty scene and press Play.
    /// </summary>
    public class LetteraLayoutSample : MonoBehaviour
    {
        const string Words = "Text wraps at word boundaries and stays aligned to its box. Change a control, and the box answers in the same frame.\nA second paragraph, shorter, to show the space between them.";

        LetteraText m_Text;
        RectTransform m_Box, m_Controls;
        float m_ControlTop = 12;
        bool m_Trim;
        float m_Curve;

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
            canvasObject.AddComponent<Image>().color = new Color(0.1f, 0.11f, 0.14f);

            m_Controls = Panel(canvasObject.transform, "Controls", new Vector2(0, 0), new Vector2(0, 1), new Vector2(360, 0), new Vector2(180, 0), new Color(0.15f, 0.16f, 0.2f));
            m_Box = Panel(canvasObject.transform, "Box", new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(760, 560), new Vector2(760, 0), new Color(0.22f, 0.24f, 0.3f));

            var go = new GameObject("Text", typeof(RectTransform));
            go.transform.SetParent(m_Box, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            m_Text = go.AddComponent<LetteraText>();
            m_Text.FontSize = 30;
            m_Text.raycastTarget = false;
            Compose();

            Cycle<LetteraHorizontalAlignment>("Horizontal", new[] { LetteraHorizontalAlignment.Start, LetteraHorizontalAlignment.Center, LetteraHorizontalAlignment.End, LetteraHorizontalAlignment.Justify }, value => m_Text.HorizontalAlignment = value);
            Cycle<LetteraVerticalAlignment>("Vertical", new[] { LetteraVerticalAlignment.Top, LetteraVerticalAlignment.Middle, LetteraVerticalAlignment.Bottom }, value => m_Text.VerticalAlignment = value);
            Cycle<string>("Wrap", new[] { "On", "Off" }, value => m_Text.WordWrap = value == "On");
            Cycle<LetteraOverflow>("Overflow", new[] { LetteraOverflow.Visible, LetteraOverflow.Ellipsis, LetteraOverflow.Truncate }, value => m_Text.Overflow = value);
            Cycle<string>("Auto Size", new[] { "Off", "On" }, value => m_Text.AutoSize = value == "On");
            Cycle<string>("Trim To Caps And Baseline", new[] { "Off", "On" }, value =>
            {
                m_Trim = value == "On";
                Compose();
            });
            Slide("Size", 10, 72, 30, value => m_Text.FontSize = value);
            Slide("Line Height", 0.8f, 2.4f, 1.4f, value =>
            {
                m_Text.LineHeightMode = LetteraLineHeightMode.Scaled;
                m_Text.LineHeightValue = value;
            });
            Slide("Padding", 0, 80, 0, value => m_Text.Padding = new Vector4(value, value, value, value));
            Slide("Box Height", 60, 560, 560, value => m_Box.sizeDelta = new Vector2(m_Box.sizeDelta.x, value));
            Slide("Curve, 0 Is Straight", 0, 1, 0, value =>
            {
                m_Curve = value;
                Compose();
            });
        }

        void Compose()
        {
            string words = Words;
            if (m_Curve > 0) words = "<curve=" + (2000 - 1700 * m_Curve).ToString("0", System.Globalization.CultureInfo.InvariantCulture) + ",0.5>" + words + "</curve>";
            if (m_Trim) words = "<trim=capheight,baseline>" + words + "</trim>";
            m_Text.Text = words;
        }

        static RectTransform Panel(Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax, Vector2 size, Vector2 position, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            go.GetComponent<Image>().color = color;
            go.GetComponent<Image>().raycastTarget = false;
            var rect = (RectTransform)go.transform;
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.sizeDelta = size;
            rect.anchoredPosition = position;
            return rect;
        }

        RectTransform Slot(float height)
        {
            var go = new GameObject("Slot", typeof(RectTransform));
            go.transform.SetParent(m_Controls, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = new Vector2(0, 1);
            rect.anchorMax = new Vector2(1, 1);
            rect.pivot = new Vector2(0.5f, 1);
            rect.anchoredPosition = new Vector2(0, -m_ControlTop);
            rect.sizeDelta = new Vector2(-24, height);
            m_ControlTop += height + 8;
            return rect;
        }

        static LetteraText Caption(Transform parent, string words, LetteraHorizontalAlignment alignment = LetteraHorizontalAlignment.Start)
        {
            var go = new GameObject("Caption", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            var text = go.AddComponent<LetteraText>();
            text.FontSize = 17;
            text.raycastTarget = false;
            text.WordWrap = false;
            text.HorizontalAlignment = alignment;
            text.VerticalAlignment = LetteraVerticalAlignment.Middle;
            text.Text = words;
            return text;
        }

        // A button that goes round its values, and says which one is on.
        void Cycle<T>(string title, T[] values, Action<T> apply)
        {
            RectTransform slot = Slot(36);
            slot.gameObject.AddComponent<Image>().color = new Color(0.27f, 0.3f, 0.38f);
            LetteraText caption = Caption(slot, title + ": " + values[0], LetteraHorizontalAlignment.Center);
            int index = 0;
            slot.gameObject.AddComponent<Button>().onClick.AddListener(() =>
            {
                index = (index + 1) % values.Length;
                caption.Text = title + ": " + values[index];
                apply(values[index]);
            });
        }

        void Slide(string title, float min, float max, float value, Action<float> apply)
        {
            RectTransform slot = Slot(46);
            var top = new GameObject("Label", typeof(RectTransform));
            top.transform.SetParent(slot, false);
            var topRect = (RectTransform)top.transform;
            topRect.anchorMin = new Vector2(0, 1);
            topRect.anchorMax = new Vector2(1, 1);
            topRect.pivot = new Vector2(0.5f, 1);
            topRect.sizeDelta = new Vector2(0, 24);
            LetteraText caption = Caption(top.transform, title);

            GameObject sliderObject = DefaultControls.CreateSlider(new DefaultControls.Resources());
            var rect = (RectTransform)sliderObject.transform;
            rect.SetParent(slot, false);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = new Vector2(1, 0);
            rect.pivot = new Vector2(0.5f, 0);
            rect.sizeDelta = new Vector2(0, 18);
            var slider = sliderObject.GetComponent<Slider>();
            slider.minValue = min;
            slider.maxValue = max;
            slider.value = value;
            slider.onValueChanged.AddListener(v =>
            {
                caption.Text = title + ": " + v.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
                apply(v);
            });
        }
    }
}

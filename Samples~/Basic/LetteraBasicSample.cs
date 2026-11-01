using FirstSymbol.Lettera;
using UnityEngine;
using UnityEngine.UI;

namespace FirstSymbol.Lettera.Samples
{
    /// <summary>
    /// Add this to an empty GameObject and press Play. It builds a Canvas showing what Lettera does with
    /// no fonts in the project: every script comes from the operating system's fonts.
    /// </summary>
    public class LetteraBasicSample : MonoBehaviour
    {
        LetteraText m_Counter;
        int m_Count;
        float m_NextTick;

        void Start()
        {
            var canvasObject = new GameObject("Canvas", typeof(Canvas), typeof(CanvasScaler));
            canvasObject.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280, 720);
            Transform parent = canvasObject.transform;

            // A browser exposes no fonts. On the web a text with no font has Latin, Cyrillic and Greek, and other scripts need a font assigned.
            Add(parent, 40, 60, Application.platform == RuntimePlatform.WebGLPlayer
                ? "Hello, мир, Γειά σου 👨‍👩‍👧‍👦👍🏽"
                : "Hello, мир, مرحبا, שלום, 你好, こんにちは, 안녕, नमस्ते 👨‍👩‍👧‍👦👍🏽");
            Add(parent, 110, 60, "<b>Bold</b> <i>Italic</i> <size=150%>Big</size> <color=#F66>Color</color> <u>Underline</u> <s>Strike</s>");
            Add(parent, 180, 70, "<outline=#D33,0.08>Outline</outline>  <shadow=#000C,0.08,-0.08,0.2>Shadow</shadow>");

            LetteraText wrapped = Add(parent, 260, 130, "Text wraps at word boundaries and justifies to both edges of its box, while the last line of a paragraph stays aligned to its start.");
            wrapped.HorizontalAlignment = LetteraHorizontalAlignment.Justify;

            LetteraText elided = Add(parent, 400, 50, "This line is too long for its box, so the end is replaced by a marker and the rest of it is never seen at all");
            elided.WordWrap = false;
            elided.Overflow = LetteraOverflow.Ellipsis;

            LetteraText fitted = Add(parent, 460, 90, "Auto size fills this box");
            fitted.AutoSize = true;
            fitted.MaxFontSize = 200;

            m_Counter = Add(parent, 560, 60, "");
        }

        static LetteraText Add(Transform parent, float top, float height, string content)
        {
            var go = new GameObject("Text", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = new Vector2(0, 1);
            rect.anchorMax = new Vector2(1, 1);
            rect.pivot = new Vector2(0.5f, 1);
            rect.anchoredPosition = new Vector2(0, -top);
            rect.sizeDelta = new Vector2(-80, height);
            var text = go.AddComponent<LetteraText>();
            text.FontSize = 30;
            text.Text = content;
            return text;
        }

        void Update()
        {
            if (Time.unscaledTime < m_NextTick) return;
            m_NextTick = Time.unscaledTime + 0.1f;
            m_Counter.SetText($"Runtime text: {++m_Count}");
        }
    }
}

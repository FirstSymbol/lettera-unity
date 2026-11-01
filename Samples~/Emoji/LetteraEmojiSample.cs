using UnityEngine;
using UnityEngine.UI;

namespace FirstSymbol.Lettera.Samples
{
    /// <summary>
    /// Flags, joined sequences, skin tones, keycaps, alpha and color on emoji, and a color font of your own.
    /// Add this to an empty object in an empty scene and press Play. The emoji come from the operating system, or the browser on the web, so they look like that device's.
    /// </summary>
    public class LetteraEmojiSample : MonoBehaviour
    {
        RectTransform m_Root;
        float m_Top = 16;
        LetteraText m_Fading;

        void Start()
        {
            var canvasObject = new GameObject("Canvas", typeof(Canvas), typeof(CanvasScaler));
            canvasObject.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280, 720);
            m_Root = (RectTransform)canvasObject.transform;
            canvasObject.AddComponent<Image>().color = new Color(0.1f, 0.11f, 0.14f);

            Row("Flags. Windows' own emoji font draws these as two letters.", "\U0001F1FA\U0001F1E6 \U0001F1EF\U0001F1F5 \U0001F1E7\U0001F1F7 \U0001F1E8\U0001F1E6 \U0001F1F0\U0001F1EA");
            Row("Joined into one: a family, a job, a rainbow flag. A caret and Backspace take each as one.", "\U0001F468\u200D\U0001F469\u200D\U0001F467\u200D\U0001F466 \U0001F469\u200D\U0001F4BB \U0001F9D1\u200D\U0001F680 \U0001F469\U0001F3FD\u200D\U0001F527 \U0001F3F3\uFE0F\u200D\U0001F308");
            Row("Skin tones", "\U0001F44D \U0001F44D\U0001F3FB \U0001F44D\U0001F3FC \U0001F44D\U0001F3FD \U0001F44D\U0001F3FE \U0001F44D\U0001F3FF");
            Row("Keycaps, and the same character as text and as emoji", "1\uFE0F\u20E3 #\uFE0F\u20E3 \u2764\uFE0E \u2764\uFE0F \u2600\uFE0E \u2600\uFE0F");
            Row("In a sentence, with markup around it", "Words \U0001F389 and <b>bold \U0001F600</b>, <size=150%>big \U0001F680</size>, <u>underlined \U0001F355</u>");
            Row("Alpha reaches an emoji", "<alpha=100%>\U0001F600</alpha> <alpha=75%>\U0001F600</alpha> <alpha=50%>\U0001F600</alpha> <alpha=25%>\U0001F600</alpha>");
            Row("A color leaves an emoji as it is drawn, and tints the words", "<color=#FF7755>Red words \U0001F600 stay red</color>");

            // A color font is a font asset of its kind. Its emoji come ahead of the system's, on the web too.
            var colorFont = LetteraFont.CreateFromBytes<LetteraColorFont>(Resources.Load<TextAsset>("LetteraSampleColor").bytes);
            colorFont.name = "Lettera Sample Color";
            LetteraText own = Row("A font of your own, COLRv1, with gradients. This small one was drawn for the sample.", "\U0001F31F \u2764 \U0001F525 \U0001F3AF");
            own.Font = colorFont;
            m_Fading = Row("The same font, fading as a whole", "\U0001F31F \u2764 \U0001F525 \U0001F3AF");
            m_Fading.Font = colorFont;
        }

        void Update()
        {
            if (m_Fading != null) m_Fading.color = new Color(1, 1, 1, 0.55f + 0.45f * Mathf.Sin(Time.time * 2));
        }

        LetteraText Row(string caption, string content)
        {
            Line(caption, 15, 22).color = new Color(0.65f, 0.68f, 0.75f);
            m_Top += 22;
            LetteraText text = Line(content, 32, 48);
            m_Top += 50;
            return text;
        }

        LetteraText Line(string content, float size, float height)
        {
            var go = new GameObject("Line", typeof(RectTransform));
            go.transform.SetParent(m_Root, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = new Vector2(0, 1);
            rect.anchorMax = new Vector2(1, 1);
            rect.pivot = new Vector2(0.5f, 1);
            rect.anchoredPosition = new Vector2(0, -m_Top);
            rect.sizeDelta = new Vector2(-48, height);
            var text = go.AddComponent<LetteraText>();
            text.FontSize = size;
            text.raycastTarget = false;
            text.Text = content;
            return text;
        }
    }
}

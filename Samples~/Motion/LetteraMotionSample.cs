using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace FirstSymbol.Lettera.Samples
{
    /// <summary>
    /// Every motion, a typewriter, a ticker, a link, a spoiler, a hover rule and an inline sprite, built from code.
    /// Add this to an empty object in an empty scene and press Play.
    /// </summary>
    public class LetteraMotionSample : MonoBehaviour
    {
        RectTransform m_Column;
        LetteraText m_Typed;
        RevealEffect m_Reveal;

        void Update()
        {
            // A bare number counts units: twelve a second, then a pause, then again. A sequencer or a Timeline clip can drive this as well.
            float reach = Mathf.Repeat(Time.time * 12, 60);
            m_Reveal.Reach = reach.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture);
            m_Typed.MarkEffectsDirty(DirtyLevel.Pose);
        }

        void Start()
        {
            var canvasObject = new GameObject("Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasObject.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            if (FindAnyObjectByType<EventSystem>() == null)
            {
                var events = new GameObject("EventSystem", typeof(EventSystem));
                System.Type module = System.Type.GetType("UnityEngine.InputSystem.UI.InputSystemUIInputModule, Unity.InputSystem");
                if (module != null) events.AddComponent(module);
                else events.AddComponent<StandaloneInputModule>();
            }

            m_Column = new GameObject("Column", typeof(RectTransform), typeof(VerticalLayoutGroup)).GetComponent<RectTransform>();
            m_Column.SetParent(canvasObject.transform, false);
            m_Column.anchorMin = m_Column.anchorMax = m_Column.pivot = new Vector2(0.5f, 1);
            m_Column.anchoredPosition = new Vector2(0, -16);
            m_Column.sizeDelta = new Vector2(640, 640);
            var layout = m_Column.GetComponent<VerticalLayoutGroup>();
            layout.spacing = 8;
            layout.childControlWidth = true;
            layout.childForceExpandHeight = false;

            // The default tags work with nothing set up.
            Row("<wave>wave</wave>  <wobble>wobble</wobble>  <shake>shake</shake>  <bounce>bounce</bounce>");
            Row("<float>float</float>  <spin>spin</spin>  <pulse>pulse</pulse>  <pendulum>pendulum</pendulum>");
            Row("<glitch>glitch</glitch>  <scramble>scramble</scramble>  <roll>1234</roll>");

            // A typewriter: reach says how much has appeared.
            m_Typed = Row("");
            m_Reveal = new RevealEffect { Reach = "0" };
            m_Typed.Styles.Add(LetteraStyle.ForWholeText(m_Reveal));
            m_Typed.Text = "This line arrives one letter at a time.";

            // A ticker, which runs only when the text does not fit.
            LetteraText ticker = Row("");
            ticker.WordWrap = false;
            ticker.Styles.Add(LetteraStyle.ForWholeText(new MarqueeEffect()));
            ticker.Text = "A long line that does not fit in its box, so it scrolls past and comes round again, on and on, for as long as it stays too wide.";

            // A link and a spoiler. Clicking one opens the page, and the other lifts its cover.
            Row("A <a=https://unity.com>link</a> that opens, and a <spoiler>secret</spoiler> under a cover.");

            // A rule: while the pointer is over the range, a tint goes to full weight, with no pass of the rebuild.
            LetteraText hover = Row("");
            var tint = new RangeTintEffect { Color = new Color(1f, 0.55f, 0.2f, 1f) };
            var action = new ActionEffect();
            action.Rules.Add(new LetteraRangeRule
            {
                When = new SignalIs(LetteraSignals.Hovered),
                Envelope = LetteraEnvelopeKind.Transition,
                Changes = { new LetteraRangeChange { Target = tint, Parameter = "Weight", ToNumber = 1 } },
            });
            action.Events.Activated += e => Debug.Log("Clicked: " + e.Primary);
            hover.Styles.Add(LetteraStyle.ForTag(new CompositeEffect(action, tint), "hot"));
            hover.Text = "Point at <hot=first>this</hot> or at <hot=second>that</hot>.";

            // An inline sprite is one character of the text, so it wraps, selects and moves with its neighbours.
            LetteraText inline = Row("");
            var effect = new InlineEffect();
            effect.Items.Add(new LetteraInlineEntry { Key = "dot", Sprite = MakeDot(), Alt = "dot" });
            inline.Styles.Add(LetteraStyle.ForMatcher(new InlineMediaMatcher("pic"), effect));
            inline.Text = "Inline <wave>sprites <pic=dot> move</wave> with the text.";
        }

        LetteraText Row(string markup)
        {
            var go = new GameObject("Row", typeof(RectTransform));
            go.transform.SetParent(m_Column, false);
            var text = go.AddComponent<LetteraText>();
            text.FontSize = 24;
            go.AddComponent<LayoutElement>().preferredHeight = 44;
            text.Text = markup;
            return text;
        }

        static Sprite MakeDot()
        {
            const int size = 32;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float d = new Vector2(x - size / 2f + 0.5f, y - size / 2f + 0.5f).magnitude;
                    texture.SetPixel(x, y, new Color(0.35f, 0.75f, 1f, Mathf.Clamp01(size / 2f - d)));
                }
            texture.Apply();
            return Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
        }
    }
}

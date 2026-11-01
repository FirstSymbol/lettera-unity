using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace FirstSymbol.Lettera.Samples
{
    /// <summary>
    /// System fonts, a stack, a variable font's axis, OpenType features, a pixel font and an icon font, each made from code.
    /// The bytes come from this sample's Resources folder. Inter is under the SIL Open Font License, and the pixel and icon fonts were drawn for the sample.
    /// In a project of your own, select a font file and use Assets > Create > Lettera > Font Asset From Selection. Add this to an empty object in an empty scene and press Play.
    /// </summary>
    public class LetteraFontsSample : MonoBehaviour
    {
        RectTransform m_Root;
        float m_Top = 10;
        LetteraText m_System, m_SystemSays;
        LetteraSystemFont m_SystemFont;
        LetteraText m_Weight;

        static LetteraFont Load(string name)
        {
            LetteraFont font = LetteraFont.CreateFromBytes(Resources.Load<TextAsset>(name).bytes);
            font.name = name;
            return font;
        }

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
            m_Root = (RectTransform)canvasObject.transform;
            canvasObject.AddComponent<Image>().color = new Color(0.1f, 0.11f, 0.14f);

            LetteraFont inter = Load("LetteraSampleInter");
            LetteraFont icons = Load("LetteraSampleIcons");
            LetteraFont pixel = Load("LetteraSamplePixel");

            // A system font names what to look for, per platform. Nothing is loaded until a text uses it.
            m_SystemFont = ScriptableObject.CreateInstance<LetteraSystemFont>();
            m_SystemFont.Windows = new[] { "Georgia", "Times New Roman" };
            m_SystemFont.MacOS = new[] { "Georgia", "Times" };
            m_SystemFont.IOS = new[] { "Georgia" };
            m_SystemFont.Android = new[] { "Noto Serif", "serif" };
            m_SystemFont.Linux = new[] { "DejaVu Serif", "Liberation Serif" };
            m_SystemSays = Caption("A system font");
            m_System = Line("The quick brown fox, from a font this machine has", 28, 38);
            m_System.Font = m_SystemFont;

            // A stack tries its families in order, for each character.
            var stack = ScriptableObject.CreateInstance<LetteraFontStack>();
            stack.Families.Add(new LetteraFontFamily { Primary = inter });
            stack.Families.Add(new LetteraFontFamily { Primary = icons });
            Caption("A stack: words from the first family, the icons from the second, in one line");
            Line("Saved \uE002 Playing \uE004 Next \uE006 Liked \uE001", 28, 38).FontStack = stack;

            // A variable font: the tag asks for a place on an axis, and a variant asset names one.
            Caption("A variable font's weight axis: by tag, by a variant asset, and by this slider");
            Line("<axes=wght 100>Thin</axes> <axes=wght 400>Regular</axes> <axes=wght 900>Black</axes>", 30, 40).Font = inter;
            var variant = ScriptableObject.CreateInstance<LetteraFontVariant>();
            variant.Source = inter;
            variant.Axes.Add(new LetteraFontVariant.AxisValue { Tag = "wght", Value = 700 });
            LetteraText named = Line("A variant asset at 700, with no tag", 22, 30);
            named.Font = variant;
            m_Weight = Line("<axes=wght 400>Weight 400</axes>", 30, 40);
            m_Weight.Font = inter;
            Slider(100, 900, 400, value => m_Weight.Text = "<axes=wght " + Mathf.RoundToInt(value) + ">Weight " + Mathf.RoundToInt(value) + "</axes>");

            // OpenType features, by four-letter tag.
            Caption("OpenType features: plain, then tabular figures, slashed zero, fractions, a stylistic set, and no contextual forms");
            Line("1111 0 1/2 a 1x2  /  <otf=tnum>1111</otf> <otf=zero>0</otf> <otf=frac>1/2</otf> <otf=ss07>a</otf> <otf=-calt>1x2</otf>", 28, 38).Font = inter;

            // A pixel font: at multiples of its grid, every font pixel lands on whole screen pixels.
            pixel.FontPixelsPerEm = 8;
            pixel.SnapToFontPixels = true;
            Caption("A pixel font, drawn for this sample, at 16, 24 and 40, which are multiples of its grid of 8");
            LetteraText small = Line("PIXELS, EXACT.", 16, 22);
            small.Font = pixel;
            LetteraText middle = Line("PIXELS, EXACT.", 24, 30);
            middle.Font = pixel;
            LetteraText big = Line("<color=#FFC24D>PIXELS, EXACT.</color>", 40, 46);
            big.Font = pixel;

            // Icons are characters: they wrap, select and take color and size like any other.
            Caption("An icon font, drawn for this sample. Icons are characters, and take color, size and effects");
            LetteraText row = Line("<color=#FFC24D>\uE000</color> <color=#FF6680>\uE001</color> <color=#66DD88>\uE002</color> <size=150%>\uE003 \uE004</size> \uE005 <outline=#39F,0.06>\uE006</outline> <color=#6CF>\uE007</color>", 40, 62);
            row.Font = icons;
        }

        void Update()
        {
            if (m_SystemSays == null || m_SystemFont == null) return;
            string found = m_SystemFont.ResolvedFamily;
            string says = string.IsNullOrEmpty(found) ? "A system font. None of the names it tries is here, so this is the default font. The web has no system fonts at all." : "A system font: found " + found;
            if (m_SystemSays.Text != says) m_SystemSays.Text = says;
        }

        LetteraText Caption(string words)
        {
            LetteraText caption = Line(words, 15, 20);
            caption.color = new Color(0.65f, 0.68f, 0.75f);
            return caption;
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
            m_Top += height + 2;
            var text = go.AddComponent<LetteraText>();
            text.FontSize = size;
            text.WordWrap = false;
            text.raycastTarget = false;
            text.Text = content;
            return text;
        }

        void Slider(float min, float max, float value, Action<float> apply)
        {
            GameObject sliderObject = DefaultControls.CreateSlider(new DefaultControls.Resources());
            var rect = (RectTransform)sliderObject.transform;
            rect.SetParent(m_Root, false);
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1);
            rect.anchoredPosition = new Vector2(24, -m_Top);
            rect.sizeDelta = new Vector2(420, 18);
            m_Top += 22;
            var slider = sliderObject.GetComponent<Slider>();
            slider.minValue = min;
            slider.maxValue = max;
            slider.value = value;
            slider.onValueChanged.AddListener(v => apply(v));
        }
    }
}

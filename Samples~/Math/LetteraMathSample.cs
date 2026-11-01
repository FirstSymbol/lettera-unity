using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace FirstSymbol.Lettera.Samples
{
    /// <summary>
    /// Formulas in a paragraph and by themselves, every construct with its source beside it, a field that shows what is typed, and formulas under color, size, stroke and motion.
    /// Add this to an empty object in an empty scene and press Play.
    /// Assign a math font to see true math layout, such as the bundled LetteraMathNotoSans. Only this sample's build then carries it.
    /// </summary>
    public class LetteraMathSample : MonoBehaviour
    {
        [Tooltip("The font of this sample's formulas. Empty uses the project's setting, then a system math font, then fallback layout.")]
        public LetteraFontAsset MathFont;

        RectTransform m_Column;

        static readonly string[] Constructs =
        {
            "x^2 + y_1", "\\frac{a+b}{c}", "\\binom{n}{k}", "\\sqrt{x}", "\\sqrt[3]{x+1}",
            "\\sum_{i=0}^n i^2", "\\int_0^1 x\\,dx", "\\lim_{x\\to 0} f(x)", "\\prod_{k=1}^n k",
            "\\left(\\frac{a}{b}\\right)", "\\left[\\frac{a}{\\frac{b}{c}}\\right]", "\\left\\{x\\right\\}", "\\big(\\Big(\\bigg(\\Bigg(",
            "\\hat{x} \\vec{v} \\bar{a} \\tilde{n}", "\\widehat{abc}", "\\overline{abc}", "\\underbrace{abc}", "\\xrightarrow{abc}",
            "\\begin{pmatrix}a&b\\\\c&d\\end{pmatrix}", "\\begin{cases}a&b\\\\c&d\\end{cases}", "\\begin{aligned}x&=1\\\\y&=2\\end{aligned}",
            "\\mathbb{R} \\mathcal{L} \\mathbf{x} \\mathfrak{g}", "\\alpha\\beta\\gamma \\leq \\infty",
        };

        void Start()
        {
            if (FindAnyObjectByType<EventSystem>() == null)
            {
                var events = new GameObject("EventSystem", typeof(EventSystem));
                System.Type module = System.Type.GetType("UnityEngine.InputSystem.UI.InputSystemUIInputModule, Unity.InputSystem");
                if (module != null) events.AddComponent(module);
                else events.AddComponent<StandaloneInputModule>();
            }
            var canvasObject = new GameObject("Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasObject.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;

            var scroll = new GameObject("Scroll", typeof(RectTransform), typeof(Image), typeof(ScrollRect), typeof(Mask));
            var scrollRect = (RectTransform)scroll.transform;
            scrollRect.SetParent(canvasObject.transform, false);
            scrollRect.anchorMin = Vector2.zero;
            scrollRect.anchorMax = new Vector2(0.72f, 1);
            scrollRect.offsetMin = scrollRect.offsetMax = Vector2.zero;
            scroll.GetComponent<Image>().color = new Color(0.1f, 0.11f, 0.13f);

            m_Column = new GameObject("Column", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter)).GetComponent<RectTransform>();
            m_Column.SetParent(scrollRect, false);
            m_Column.anchorMin = new Vector2(0.05f, 1);
            m_Column.anchorMax = new Vector2(0.95f, 1);
            m_Column.pivot = new Vector2(0.5f, 1);
            m_Column.sizeDelta = Vector2.zero;
            var layout = m_Column.GetComponent<VerticalLayoutGroup>();
            layout.spacing = 10;
            layout.childControlWidth = true;
            layout.childForceExpandHeight = false;
            m_Column.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var scroller = scroll.GetComponent<ScrollRect>();
            scroller.content = m_Column;
            scroller.horizontal = false;

            Row("<b>A formula in a sentence</b>");
            // The web's default font has no Hebrew: assign one that has, and this line can run both ways there too.
            string between = Application.platform == RuntimePlatform.WebGLPlayer ? "Between words" : "Between שלום and שלום";
            Row("Energy is <math>E = mc^2</math>, and <math>\\frac{1}{2}mv^2</math> is the part that moves. " + between + ", it stays <math>x+1</math>, whichever way the line runs.");
            Row("<b>By itself</b>");
            Row("<math=display>\\int_0^\\infty e^{-x^2} dx = \\frac{\\sqrt{\\pi}}{2}</math>");

            Row("<b>Each construct, with its source</b>");
            foreach (string source in Constructs)
            {
                Row("<noparse>" + source + "</noparse>", 16, new Color(0.6f, 0.65f, 0.72f));
                Row("<math>" + source + "</math>");
            }

            Row("<b>A formula that cannot be read says so, and the words around it stay</b>");
            Row("before <math>\\frac{a</math> after");

            Row("<b>Type a formula</b>");
            AddField();

            Row("<b>It takes what text takes</b>");
            Row("<color=#ff6677><math>\\frac{a}{\\sqrt{b}}</math></color>  <size=48><math>\\frac{a}{b}</math></size>  <stroke=#3399ff,0.06><math>\\frac{a}{b}</math></stroke>");
            Row("<spin><math>\\frac{a}{b}</math></spin>  moves whole, and  <wave><math>x^2</math> words</wave>");

            // Beside the page, a formula in world space with no Canvas.
            Camera seeing = Camera.main != null ? Camera.main : FindAnyObjectByType<Camera>();
            if (seeing == null) seeing = new GameObject("Camera", typeof(Camera)).GetComponent<Camera>();
            var world = new GameObject("World Text", typeof(RectTransform));
            world.transform.position = seeing.ViewportToWorldPoint(new Vector3(0.86f, 0.5f, 10));
            world.transform.rotation = seeing.transform.rotation;
            ((RectTransform)world.transform).sizeDelta = new Vector2(5, 3);
            LetteraWorldText flat = world.AddComponent<LetteraWorldText>();
            flat.FontSize = 0.45f;
            flat.HorizontalAlignment = LetteraHorizontalAlignment.Center;
            flat.VerticalAlignment = LetteraVerticalAlignment.Middle;
            Use(flat.Styles);
            flat.Text = "<math=display>\\sum_{i=0}^n i = \\frac{n(n+1)}{2}</math>";
        }

        // The font goes on this sample's own formula style, which comes first, so that nothing changes in the project.
        void Use(System.Collections.Generic.List<LetteraStyle> styles)
        {
            if (MathFont == null) return;
            styles.Insert(0, LetteraStyle.ForMatcher(new LetteraMathTagMatcher(), new LetteraMathEffect { Font = MathFont }));
        }

        LetteraText Row(string text, float size = 26, Color? color = null)
        {
            var go = new GameObject("Row", typeof(RectTransform));
            go.transform.SetParent(m_Column, false);
            LetteraText label = go.AddComponent<LetteraText>();
            label.FontSize = size;
            label.color = color ?? Color.white;
            Use(label.Styles);
            label.Text = text;
            return label;
        }

        void AddField()
        {
            GameObject field = LetteraFieldFactory.CreateInputField(m_Column);
            var layout = field.AddComponent<LayoutElement>();
            layout.preferredHeight = 52;
            LetteraText shown = Row("");
            LetteraEditable editable = field.GetComponentInChildren<LetteraEditable>();
            editable.Source = "\\frac{1}{x^2}";
            editable.ValueChanged += value => shown.Text = "<math>" + value + "</math>";
            shown.Text = "<math>" + editable.Source + "</math>";
        }
    }
}

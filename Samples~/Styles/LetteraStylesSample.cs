using UnityEngine;
using UnityEngine.UI;

namespace FirstSymbol.Lettera.Samples
{
    /// <summary>
    /// A gallery: every layer an effect can draw, gradients, filters, an effect preset, a style preset, a tag of your own and an effect with its own shader, each beside the markup that asks for it.
    /// Add this to an empty object in an empty scene and press Play.
    /// </summary>
    public class LetteraStylesSample : MonoBehaviour
    {
        RectTransform m_Root;
        int m_Cell;
        const int Columns = 3;
        const float CellWidth = 412, CellHeight = 84;

        void Start()
        {
            var canvasObject = new GameObject("Canvas", typeof(Canvas), typeof(CanvasScaler));
            canvasObject.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280, 720);
            m_Root = (RectTransform)canvasObject.transform;
            canvasObject.AddComponent<Image>().color = new Color(0.33f, 0.37f, 0.45f);

            // The tags of the default vocabulary, which need no setup.
            Cell("<outline=#D33,0.08>Outline</outline>");
            Cell("<stroke=#39F,0.08,outside>Stroke</stroke>");
            Cell("<shadow=#000C,0.08,-0.08,0.2>Shadow</shadow>");
            Cell("<glow=#4CF,0.3>Glow</glow>");
            Cell("<inner-shadow=#D22,0.06,-0.06,0.08>Inner Shadow</inner-shadow>");
            Cell("<extrude=0.1,-0.1,#FA0,#410>Extrude</extrude>");
            Cell("<mark=#FC08>Marked</mark> <mark=#6CF8,line>line</mark>");
            Cell("<warp=0.05,6,1>Warped outline</warp>");
            Cell("<stroke=#F60,0.1><union>Union</union></stroke>");

            // Filters reach what lies below them.
            Cell("<filter=1,grayscale><color=#F44>Grayscale</color> \U0001F600</filter>");
            Cell("<filter=1,invert><color=#F44>Invert</color> \U0001F600</filter>");
            Cell("<filter=1,sepia><color=#4AF>Sepia</color> \U0001F600</filter>");

            // A gradient can run across a whole range or restart on each glyph.
            var sunset = LetteraGradient.Of(new Color32(255, 170, 40, 255), new Color32(240, 60, 130, 255), new Color32(70, 110, 255, 255));
            var across = new FillEffect();
            across.SetPaint(LetteraPaint.Of(sunset, LetteraPaintSpan.Range));
            Cell("<sunset>Across the range</sunset>", LetteraStyle.ForTag(across, "sunset"));
            var each = new FillEffect();
            each.SetPaint(LetteraPaint.Of(sunset, LetteraPaintSpan.Glyph));
            Cell("<perglyph>On each glyph</perglyph>", LetteraStyle.ForTag(each, "perglyph"));

            // A tag of your own: a style binds a name to an effect, and no tag means anything otherwise.
            Cell("<warning>A tag of my own</warning>", LetteraStyle.ForTag(new ColorEffect(), "warning", "#FF8800"));

            // An effect preset is several effects under one name, and many texts can share it.
            var brandFill = new FillEffect();
            brandFill.SetPaint(LetteraPaint.Of(LetteraGradient.Of(new Color32(120, 230, 255, 255), new Color32(60, 100, 255, 255)), LetteraPaintSpan.Range));
            var brand = ScriptableObject.CreateInstance<LetteraEffectPreset>();
            brand.Root = new CompositeEffect(brandFill, new StrokeEffect { Width = 0.05f });
            Cell("<brand>An effect preset</brand>", LetteraStyle.ForTag(new PresetEffect(brand), "brand"));

            // A style preset is a list of styles, shared by reference.
            var preset = ScriptableObject.CreateInstance<LetteraStylePreset>();
            preset.Styles.Add(LetteraStyle.ForTag(new ColorEffect(), "note", "#7DE38D"));
            LetteraText usesPreset = Cell("<note>A style preset's tag</note>");
            usesPreset.StylePresets.Add(preset);
            usesPreset.MarkStylesDirty();

            // An effect with its own shader, made with Assets > Create > Lettera > Custom Effect.
            Shader shader = Shader.Find("Lettera Samples/Stripes");
            if (shader != null) Cell("<stripes>Its own shader</stripes>", LetteraStyle.ForTag(new MaterialEffect { Material = new Material(shader), Order = LetteraMaterialOrder.Replace }, "stripes"));
        }

        LetteraText Cell(string markup, LetteraStyle style = null)
        {
            int column = m_Cell % Columns, row = m_Cell / Columns;
            m_Cell++;
            float x = 16 + column * (CellWidth + 8), y = 12 + row * (CellHeight + 10);
            // Styles first, so that the first rebuild knows the tag.
            LetteraText shown = Text(x, y, 52, 34, style, markup);
            LetteraText source = Text(x, y + 54, 26, 13, null, "<noparse>" + markup + "</noparse>");
            source.color = new Color(0.8f, 0.83f, 0.88f);
            return shown;
        }

        LetteraText Text(float x, float y, float height, float size, LetteraStyle style, string content)
        {
            var go = new GameObject("Cell", typeof(RectTransform));
            go.transform.SetParent(m_Root, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1);
            rect.anchoredPosition = new Vector2(x, -y);
            rect.sizeDelta = new Vector2(CellWidth, height);
            var text = go.AddComponent<LetteraText>();
            text.FontSize = size;
            text.raycastTarget = false;
            if (style != null) text.Styles.Add(style);
            text.Text = content;
            return text;
        }
    }
}

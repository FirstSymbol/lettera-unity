using UnityEngine;
using UnityEngine.UI;

namespace FirstSymbol.Lettera
{
    /// <summary>Builds ready-made fields from UI objects you can restyle. A project can name prefabs of its own in the settings.</summary>
    public static class LetteraFieldFactory
    {
        static readonly Color s_Background = new Color(0.13f, 0.14f, 0.17f, 1), s_Text = new Color(0.93f, 0.94f, 0.96f, 1), s_Hint = new Color(0.93f, 0.94f, 0.96f, 0.45f);
        const float FontSize = 18;

        static RectTransform Stretch(GameObject go, Transform parent, float inset = 0)
        {
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(inset, inset);
            rect.offsetMax = new Vector2(-inset, -inset);
            return rect;
        }

        static LetteraText MakeText(string name, Transform parent, Color color, bool wrap)
        {
            var go = new GameObject(name, typeof(RectTransform));
            Stretch(go, parent);
            var text = go.AddComponent<LetteraText>();
            text.FontSize = FontSize;
            text.WordWrap = wrap;
            text.color = color;
            text.Text = "";
            return text;
        }

        static GameObject MakeRoot(string name, Transform parent, Vector2 size)
        {
            var root = new GameObject(name, typeof(RectTransform), typeof(Image));
            var rect = (RectTransform)root.transform;
            rect.SetParent(parent, false);
            rect.sizeDelta = size;
            root.GetComponent<Image>().color = s_Background;
            root.AddComponent<LayoutElement>().preferredHeight = size.y;
            return root;
        }

        static RectTransform MakeViewport(Transform parent)
        {
            var viewport = new GameObject("Viewport", typeof(RectTransform), typeof(RectMask2D));
            return Stretch(viewport, parent, 8);
        }

        static LetteraEditable MakeField(RectTransform viewport, bool wrap, string hint, out LetteraText hintText)
        {
            hintText = MakeText("Hint", viewport, s_Hint, wrap);
            hintText.Text = hint;
            hintText.raycastTarget = false;
            LetteraText text = MakeText("Text", viewport, s_Text, wrap);
            var field = text.gameObject.AddComponent<LetteraEditable>();
            field.TypedMarkup = LetteraTypedMarkup.AsText;
            return field;
        }

        /// <summary>One line: Enter submits.</summary>
        public static GameObject CreateInputField(Transform parent, string hint = "Enter text...")
        {
            GameObject root = MakeRoot("Input Field", parent, new Vector2(320, 40));
            RectTransform viewport = MakeViewport(root.transform);
            LetteraEditable field = MakeField(viewport, false, hint, out LetteraText hintText);
            VerticalMiddle(field, hintText);
            field.AddRule(new SingleLineRule());
            field.AddRule(new HintDecorator { Target = hintText });
            return root;
        }

        static void VerticalMiddle(LetteraEditable field, LetteraText hint)
        {
            field.GetComponent<LetteraText>().VerticalAlignment = LetteraVerticalAlignment.Middle;
            hint.VerticalAlignment = LetteraVerticalAlignment.Middle;
        }

        /// <summary>Several lines that wrap and scroll inside the field.</summary>
        public static GameObject CreateTextArea(Transform parent, string hint = "Enter text...")
        {
            GameObject root = MakeRoot("Text Area", parent, new Vector2(320, 140));
            RectTransform viewport = MakeViewport(root.transform);
            LetteraEditable field = MakeField(viewport, true, hint, out LetteraText hintText);
            var rect = (RectTransform)field.transform;
            rect.anchorMin = new Vector2(0, 1);
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 1);
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            var fitter = field.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            field.AddRule(new HintDecorator { Target = hintText });
            root.AddComponent<LetteraFieldPressArea>().Field = field;
            return root;
        }

        /// <summary>One line of dots, with a button that shows the value.</summary>
        public static GameObject CreatePasswordField(Transform parent, string hint = "Password")
        {
            GameObject root = MakeRoot("Password Field", parent, new Vector2(320, 40));
            RectTransform viewport = MakeViewport(root.transform);
            viewport.offsetMax = new Vector2(-44, -8);
            LetteraEditable field = MakeField(viewport, false, hint, out LetteraText hintText);
            VerticalMiddle(field, hintText);
            field.AddRule(new PasswordRule());
            field.AddRule(new SelectAllOnFocusRule());
            field.AddRule(new HintDecorator { Target = hintText });

            var eye = new GameObject("Show", typeof(RectTransform), typeof(Image), typeof(Button));
            var eyeRect = (RectTransform)eye.transform;
            eyeRect.SetParent(root.transform, false);
            eyeRect.anchorMin = new Vector2(1, 0);
            eyeRect.anchorMax = Vector2.one;
            eyeRect.pivot = new Vector2(1, 0.5f);
            eyeRect.sizeDelta = new Vector2(40, 0);
            eyeRect.anchoredPosition = Vector2.zero;
            eye.GetComponent<Image>().color = new Color(1, 1, 1, 0.08f);
            LetteraText glyph = MakeText("Label", eye.transform, s_Text, false);
            glyph.Text = "•";
            glyph.HorizontalAlignment = LetteraHorizontalAlignment.Center;
            glyph.VerticalAlignment = LetteraVerticalAlignment.Middle;
            glyph.raycastTarget = false;
            eye.AddComponent<LetteraPasswordToggle>().Field = field;
            return root;
        }

        /// <summary>A text with selection only.</summary>
        public static GameObject CreateSelectableText(Transform parent, string text = "Selectable text")
        {
            LetteraText label = MakeText("Selectable Text", parent, Color.white, true);
            var rect = label.rectTransform;
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(320, 60);
            label.Text = text;
            label.gameObject.AddComponent<LetteraSelectable>();
            return label.gameObject;
        }

        /// <summary>A bare editable text with no chrome.</summary>
        public static GameObject CreateEditableText(Transform parent, string text = "Editable text")
        {
            LetteraText label = MakeText("Editable Text", parent, Color.white, true);
            var rect = label.rectTransform;
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(320, 60);
            label.Text = text;
            label.gameObject.AddComponent<LetteraEditable>();
            return label.gameObject;
        }

        /// <summary>Pick one of several options, with shaping, emoji and bidi in each.</summary>
        public static GameObject CreateDropdown(Transform parent)
        {
            GameObject root = MakeRoot("Dropdown", parent, new Vector2(240, 40));
            LetteraText caption = MakeText("Caption", root.transform, s_Text, false);
            caption.rectTransform.offsetMin = new Vector2(10, 0);
            caption.rectTransform.offsetMax = new Vector2(-30, 0);
            caption.VerticalAlignment = LetteraVerticalAlignment.Middle;
            caption.raycastTarget = false;
            LetteraText arrow = MakeText("Arrow", root.transform, s_Text, false);
            arrow.rectTransform.anchorMin = new Vector2(1, 0);
            arrow.rectTransform.pivot = new Vector2(1, 0.5f);
            arrow.rectTransform.sizeDelta = new Vector2(26, 0);
            arrow.rectTransform.anchoredPosition = Vector2.zero;
            arrow.Text = "▾";
            arrow.VerticalAlignment = LetteraVerticalAlignment.Middle;
            arrow.raycastTarget = false;

            var template = new GameObject("Template", typeof(RectTransform), typeof(Image), typeof(ScrollRect));
            var templateRect = (RectTransform)template.transform;
            templateRect.SetParent(root.transform, false);
            templateRect.anchorMin = new Vector2(0, 0);
            templateRect.anchorMax = new Vector2(1, 0);
            templateRect.pivot = new Vector2(0.5f, 1);
            templateRect.sizeDelta = new Vector2(0, 240);
            template.GetComponent<Image>().color = s_Background;

            var viewport = new GameObject("Viewport", typeof(RectTransform), typeof(RectMask2D));
            RectTransform viewportRect = Stretch(viewport, template.transform);
            var content = new GameObject("Content", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            var contentRect = (RectTransform)content.transform;
            contentRect.SetParent(viewportRect, false);
            contentRect.anchorMin = new Vector2(0, 1);
            contentRect.anchorMax = Vector2.one;
            contentRect.pivot = new Vector2(0.5f, 1);
            contentRect.sizeDelta = Vector2.zero;
            var layout = content.GetComponent<VerticalLayoutGroup>();
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandHeight = false;
            content.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var scroll = template.GetComponent<ScrollRect>();
            scroll.viewport = viewportRect;
            scroll.content = contentRect;
            scroll.horizontal = false;

            var rowObject = new GameObject("Row", typeof(RectTransform), typeof(Image), typeof(Toggle), typeof(LayoutElement));
            var rowRect = (RectTransform)rowObject.transform;
            rowRect.SetParent(contentRect, false);
            rowObject.GetComponent<LayoutElement>().minHeight = 34;
            var background = rowObject.GetComponent<Image>();
            background.color = Color.white;
            var toggle = rowObject.GetComponent<Toggle>();
            toggle.targetGraphic = background;
            ColorBlock colors = toggle.colors;
            colors.normalColor = new Color(1, 1, 1, 0);
            colors.highlightedColor = new Color(0.2f, 0.565f, 1f, 1);
            colors.selectedColor = new Color(0.2f, 0.565f, 1f, 0.6f);
            colors.pressedColor = new Color(0.15f, 0.45f, 0.85f, 1);
            colors.disabledColor = new Color(1, 1, 1, 0);
            colors.fadeDuration = 0;
            toggle.colors = colors;
            LetteraText label = MakeText("Label", rowObject.transform, s_Text, false);
            label.rectTransform.offsetMin = new Vector2(26, 0);
            label.rectTransform.offsetMax = new Vector2(-8, 0);
            label.VerticalAlignment = LetteraVerticalAlignment.Middle;
            label.raycastTarget = false;
            LetteraText check = MakeText("Check", rowObject.transform, s_Text, false);
            check.rectTransform.anchorMax = new Vector2(0, 1);
            check.rectTransform.pivot = new Vector2(0, 0.5f);
            check.rectTransform.sizeDelta = new Vector2(24, 0);
            check.rectTransform.offsetMin = new Vector2(6, 0);
            check.Text = "✓";
            check.VerticalAlignment = LetteraVerticalAlignment.Middle;
            check.raycastTarget = false;
            var row = rowObject.AddComponent<LetteraDropdownRow>();
            row.Label = label;
            row.Check = check.gameObject;
            row.Toggle = toggle;
            template.SetActive(false);

            var dropdown = root.AddComponent<LetteraDropdown>();
            dropdown.CaptionText = caption;
            dropdown.Template = templateRect;
            dropdown.RowPrototype = row;
            dropdown.targetGraphic = root.GetComponent<Image>();
            return root;
        }
    }
}

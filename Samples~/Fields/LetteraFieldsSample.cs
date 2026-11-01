using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace FirstSymbol.Lettera.Samples
{
    /// <summary>
    /// Fields built from code: a chat box, a checked e-mail field, a password, a rich text area with shortcuts and a dropdown.
    /// Add this to an empty object in an empty scene and press Play.
    /// </summary>
    public class LetteraFieldsSample : MonoBehaviour
    {
        /// <summary>The address check: filters keep characters out, and this tells about the whole value.</summary>
        [System.Serializable]
        class HasDomain : LetteraValueCheck
        {
            public override LetteraCheckResult Check(ILetteraTextView value) =>
                value.PlainText.Contains("@") && value.PlainText.Contains(".") ? new LetteraCheckResult(null) : new LetteraCheckResult(LetteraCheckResult.Invalid, "This does not look like an address");
        }

        void Start()
        {
            var canvasObject = new GameObject("Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            if (FindAnyObjectByType<EventSystem>() == null)
            {
                var events = new GameObject("EventSystem", typeof(EventSystem));
                System.Type module = System.Type.GetType("UnityEngine.InputSystem.UI.InputSystemUIInputModule, Unity.InputSystem");
                if (module != null) events.AddComponent(module);
                else events.AddComponent<StandaloneInputModule>();
            }

            RectTransform column = new GameObject("Column", typeof(RectTransform), typeof(VerticalLayoutGroup)).GetComponent<RectTransform>();
            column.SetParent(canvas.transform, false);
            column.anchorMin = column.anchorMax = column.pivot = new Vector2(0.5f, 1);
            column.anchoredPosition = new Vector2(0, -20);
            column.sizeDelta = new Vector2(420, 620);
            var layout = column.GetComponent<VerticalLayoutGroup>();
            layout.spacing = 12;
            layout.childControlWidth = true;
            layout.childForceExpandHeight = false;

            // Enter sends and keeps the focus, Shift+Enter breaks the line, and there is a limit with a counter.
            GameObject chatRoot = LetteraFieldFactory.CreateTextArea(column, "Message");
            LetteraEditable chat = chatRoot.GetComponentInChildren<LetteraEditable>();
            chat.AddRule(new EnterKeyRule { Key = LetteraSubmitKey.Enter, KeepFocus = true });
            chat.AddRule(new MaxLengthRule { Limit = 200, CountIn = LetteraCountSpace.Plain });
            LetteraText counter = Label(column, "");
            chat.AddRule(new LengthCounterDecorator { Target = counter });
            chat.Submitted += text =>
            {
                Debug.Log("Sent: " + text);
                chat.SetSource("");
            };

            // The filter and the check differ: one refuses characters, the other reports on the value when the field is left.
            GameObject mailRoot = LetteraFieldFactory.CreateInputField(column, "E-mail");
            LetteraEditable mail = mailRoot.GetComponentInChildren<LetteraEditable>();
            mail.AddRule(new EmailCharFilter());
            var validation = new ValidationRule { When = LetteraCheckTime.OnBlur };
            validation.Checks.Add(new HasDomain());
            mail.AddRule(validation);
            mail.AddRule(new HelperTextDecorator { Target = Label(column, ""), HelperText = "Used to sign in" });

            LetteraFieldFactory.CreatePasswordField(column);

            // Ctrl or Cmd with B, I, U; markup stays hidden, and pasted HTML keeps its formatting.
            GameObject richRoot = LetteraFieldFactory.CreateTextArea(column, "Formatted text");
            LetteraEditable rich = richRoot.GetComponentInChildren<LetteraEditable>();
            rich.AddRule(new FormattingRule());

            GameObject dropdownRoot = LetteraFieldFactory.CreateDropdown(column);
            var dropdown = dropdownRoot.GetComponent<LetteraDropdown>();
            dropdown.AddOptions(new[] { "<b>Bold</b> option", "مرحبا", "Emoji \U0001F389" });
            dropdown.OnValueChanged.AddListener(value => Debug.Log("Chose " + value));

            Label(column, "Selectable: select me with the mouse, and copy.").gameObject.AddComponent<LetteraSelectable>();
        }

        static LetteraText Label(Transform parent, string text)
        {
            var go = new GameObject("Label", typeof(RectTransform), typeof(LayoutElement));
            go.transform.SetParent(parent, false);
            go.GetComponent<LayoutElement>().preferredHeight = 24;
            var label = go.AddComponent<LetteraText>();
            label.FontSize = 15;
            label.Text = text;
            return label;
        }
    }
}

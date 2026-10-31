using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace FirstSymbol.Lettera
{
    /// <summary>One choice of a dropdown.</summary>
    [Serializable]
    public class LetteraDropdownOption
    {
        [TextArea(1, 3)] public string Text = "";
        public Sprite Image;
        public bool Disabled;

        public LetteraDropdownOption() { }
        public LetteraDropdownOption(string text) => Text = text;
    }

    /// <summary>One line of a dropdown's list. Works in a list of your own too.</summary>
    [AddComponentMenu("Lettera/Dropdown Row")]
    public class LetteraDropdownRow : MonoBehaviour
    {
        [SerializeField] LetteraText m_Label;
        [SerializeField] Image m_Icon;
        [SerializeField] GameObject m_Check;
        [SerializeField] Toggle m_Toggle;

        public LetteraText Label { get => m_Label; set => m_Label = value; }
        public Image Icon { get => m_Icon; set => m_Icon = value; }
        public GameObject Check { get => m_Check; set => m_Check = value; }
        public Toggle Toggle { get => m_Toggle; set => m_Toggle = value; }

        public void Bind(LetteraDropdownOption option, bool isCurrent)
        {
            if (m_Label != null) m_Label.Text = option.Text;
            if (m_Icon != null)
            {
                m_Icon.sprite = option.Image;
                m_Icon.enabled = option.Image != null;
            }
            if (m_Check != null) m_Check.SetActive(isCurrent);
            if (m_Toggle != null)
            {
                m_Toggle.SetIsOnWithoutNotify(isCurrent);
                m_Toggle.interactable = !option.Disabled;
            }
        }
    }

    /// <summary>A control that lets a user pick one option. The text goes through Lettera: shaping, emoji, bidi and markup.</summary>
    [AddComponentMenu("Lettera/Dropdown")]
    public class LetteraDropdown : Selectable, IPointerClickHandler, ISubmitHandler, ICancelHandler
    {
        [SerializeField] List<LetteraDropdownOption> m_Options = new List<LetteraDropdownOption>();
        [SerializeField] int m_Value;
        [SerializeField] LetteraText m_CaptionText;
        [SerializeField] Image m_CaptionImage;
        [SerializeField] RectTransform m_Template;
        [SerializeField] LetteraDropdownRow m_RowPrototype;
        [SerializeField] LetteraText m_Placeholder;
        [SerializeField] bool m_ParseMarkup = true;
        [SerializeField] bool m_CloseOnSelect = true;
        [SerializeField, Min(0)] float m_FadeSeconds = 0.15f;
        [SerializeField] IntEvent m_OnValueChanged = new IntEvent();

        [Serializable] public class IntEvent : UnityEvent<int> { }

        GameObject m_List, m_Blocker;
        readonly List<LetteraDropdownRow> m_Rows = new List<LetteraDropdownRow>();
        int m_Highlight = -1;
        CanvasGroup m_ListGroup;
        double m_FadeStart;
        bool m_Fading;

        public event Action Opened, Closed;

        public List<LetteraDropdownOption> Options => m_Options;
        public LetteraText CaptionText { get => m_CaptionText; set { m_CaptionText = value; RefreshCaption(); } }
        public Image CaptionImage { get => m_CaptionImage; set { m_CaptionImage = value; RefreshCaption(); } }
        public RectTransform Template { get => m_Template; set => m_Template = value; }
        public LetteraDropdownRow RowPrototype { get => m_RowPrototype; set => m_RowPrototype = value; }
        public LetteraText Placeholder { get => m_Placeholder; set { m_Placeholder = value; RefreshCaption(); } }
        public bool ParseMarkup { get => m_ParseMarkup; set => m_ParseMarkup = value; }
        public bool CloseOnSelect { get => m_CloseOnSelect; set => m_CloseOnSelect = value; }
        public float FadeSeconds { get => m_FadeSeconds; set => m_FadeSeconds = value; }
        public IntEvent OnValueChanged => m_OnValueChanged;
        public bool IsOpen => m_List != null;
        internal int HighlightIndex => m_Highlight;
        internal IReadOnlyList<LetteraDropdownRow> Rows => m_Rows;
        internal RectTransform ListRect => m_List != null ? (RectTransform)m_List.transform : null;

        /// <summary>The chosen option. -1 means none, and what is out of range counts as none.</summary>
        public int Value
        {
            get => m_Value;
            set
            {
                int clamped = Clamp(value);
                if (clamped == m_Value) return;
                m_Value = clamped;
                RefreshCaption();
                m_OnValueChanged.Invoke(m_Value);
            }
        }

        int Clamp(int value) => value < 0 || value >= m_Options.Count ? -1 : value;

        public void SetValueWithoutNotify(int value)
        {
            m_Value = Clamp(value);
            RefreshCaption();
        }

        public void AddOptions(IEnumerable<string> texts)
        {
            foreach (string text in texts) m_Options.Add(new LetteraDropdownOption(text));
            RefreshCaption();
        }

        public void AddOptions(IEnumerable<LetteraDropdownOption> options)
        {
            m_Options.AddRange(options);
            RefreshCaption();
        }

        public void ClearOptions()
        {
            m_Options.Clear();
            m_Value = -1;
            RefreshCaption();
        }

        protected override void OnEnable()
        {
            base.OnEnable();
            if (m_Template != null) m_Template.gameObject.SetActive(false);
            if (Application.isPlaying || m_CaptionText != null) RefreshCaption();
        }

        protected override void OnDisable()
        {
            base.OnDisable();
            Hide();
        }

        public void RefreshCaption()
        {
            int value = Clamp(m_Value);
            LetteraDropdownOption option = value >= 0 ? m_Options[value] : null;
            if (m_CaptionText != null)
            {
                if (option != null) SetLabel(m_CaptionText, option.Text);
                else m_CaptionText.Text = "";
            }
            if (m_CaptionImage != null)
            {
                m_CaptionImage.sprite = option?.Image;
                m_CaptionImage.enabled = option?.Image != null;
            }
            if (m_Placeholder != null)
            {
                if (option == null) m_Placeholder.Show();
                else m_Placeholder.Hide();
            }
        }

        void SetLabel(LetteraText label, string text)
        {
            label.Text = m_ParseMarkup ? text : "<noparse>" + text + "</noparse>";
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (eventData.button != PointerEventData.InputButton.Left) return;
            Toggle();
        }

        public void OnSubmit(BaseEventData eventData)
        {
            if (IsOpen && m_Highlight >= 0) Choose(m_Highlight);
            else Toggle();
        }

        public void OnCancel(BaseEventData eventData) => Hide();

        void Toggle()
        {
            if (!IsActive() || !IsInteractable()) return;
            if (IsOpen) Hide();
            else Show();
        }

        /// <summary>Opens the list.</summary>
        public void Show()
        {
            if (IsOpen || !IsActive() || !IsInteractable() || m_Template == null) return;
            Canvas canvas = GetComponentInParent<Canvas>();
            if (canvas == null) return;
            Canvas root = canvas.rootCanvas;

            m_Blocker = new GameObject("Lettera Dropdown Blocker", typeof(RectTransform), typeof(Canvas), typeof(GraphicRaycaster), typeof(Image), typeof(Button));
            m_Blocker.hideFlags = HideFlags.DontSave;
            m_Blocker.transform.SetParent(root.transform, false);
            var blockerRect = (RectTransform)m_Blocker.transform;
            blockerRect.anchorMin = Vector2.zero;
            blockerRect.anchorMax = Vector2.one;
            blockerRect.offsetMin = blockerRect.offsetMax = Vector2.zero;
            Canvas blockerCanvas = m_Blocker.GetComponent<Canvas>();
            blockerCanvas.overrideSorting = true;
            blockerCanvas.sortingOrder = 29000;
            m_Blocker.GetComponent<Image>().color = Color.clear;
            m_Blocker.GetComponent<Button>().onClick.AddListener(Hide);

            m_List = Instantiate(m_Template.gameObject, root.transform);
            m_List.hideFlags = HideFlags.DontSave;
            m_List.name = "Lettera Dropdown List";
            m_List.SetActive(true);
            Canvas listCanvas = m_List.GetComponent<Canvas>();
            if (listCanvas == null) listCanvas = m_List.AddComponent<Canvas>();
            listCanvas.overrideSorting = true;
            listCanvas.sortingOrder = 29001;
            listCanvas.additionalShaderChannels |= root.additionalShaderChannels;
            if (m_List.GetComponent<GraphicRaycaster>() == null) m_List.AddComponent<GraphicRaycaster>();
            m_ListGroup = m_List.GetComponent<CanvasGroup>();
            if (m_ListGroup == null) m_ListGroup = m_List.AddComponent<CanvasGroup>();

            FillRows();
            PlaceList(root);
            m_Highlight = Clamp(m_Value);
            if (LetteraAccessibility.ReducedMotion || m_FadeSeconds <= 0)
            {
                m_ListGroup.alpha = 1;
            }
            else
            {
                m_ListGroup.alpha = 0;
                m_FadeStart = LetteraEditClock.Now;
                m_Fading = true;
            }
            Opened?.Invoke();
        }

        void FillRows()
        {
            m_Rows.Clear();
            LetteraDropdownRow prototype = m_List.GetComponentInChildren<LetteraDropdownRow>(true);
            if (prototype == null) return;
            prototype.gameObject.SetActive(false);
            for (int i = 0; i < m_Options.Count; i++)
            {
                LetteraDropdownRow row = Instantiate(prototype, prototype.transform.parent);
                row.hideFlags = HideFlags.DontSave;
                row.gameObject.SetActive(true);
                LetteraDropdownOption option = m_Options[i];
                var shown = new LetteraDropdownOption { Text = m_ParseMarkup ? option.Text : "<noparse>" + option.Text + "</noparse>", Image = option.Image, Disabled = option.Disabled };
                row.Bind(shown, i == Clamp(m_Value));
                int index = i;
                if (row.Toggle != null) row.Toggle.onValueChanged.AddListener(_ => Choose(index));
                else row.gameObject.AddComponent<LetteraMenuClick>().Action = () => Choose(index);
                m_Rows.Add(row);
            }
        }

        // Under the control, or above it where only that fits, and wholly on the screen.
        void PlaceList(Canvas root)
        {
            var list = (RectTransform)m_List.transform;
            var own = (RectTransform)transform;
            var rootRect = (RectTransform)root.transform;
            LayoutRebuilder.ForceRebuildLayoutImmediate(list);
            var corners = new Vector3[4];
            own.GetWorldCorners(corners);
            Vector2 bottomLeft = rootRect.InverseTransformPoint(corners[0]), topRight = rootRect.InverseTransformPoint(corners[2]);
            Rect area = rootRect.rect;
            float height = list.rect.height;
            float width = Mathf.Max(topRight.x - bottomLeft.x, 1);
            list.anchorMin = list.anchorMax = new Vector2(rootRect.pivot.x, rootRect.pivot.y);
            list.pivot = new Vector2(0, 1);
            list.sizeDelta = new Vector2(width, height);
            float y = bottomLeft.y;
            if (y - height < area.yMin && topRight.y + height <= area.yMax) y = topRight.y + height;
            y = Mathf.Clamp(y, area.yMin + height, area.yMax);
            float x = Mathf.Clamp(bottomLeft.x, area.xMin, Mathf.Max(area.xMax - width, area.xMin));
            list.anchoredPosition = new Vector2(x, y);
        }

        /// <summary>Closes the list, changing nothing.</summary>
        public void Hide()
        {
            if (!IsOpen) return;
            SelectionVisuals.Destroy(m_List);
            SelectionVisuals.Destroy(m_Blocker);
            m_List = m_Blocker = null;
            m_Rows.Clear();
            m_Highlight = -1;
            m_Fading = false;
            Closed?.Invoke();
        }

        void Update()
        {
            if (!m_Fading || m_ListGroup == null) return;
            float t = (float)(LetteraEditClock.Now - m_FadeStart) / Mathf.Max(m_FadeSeconds, 0.0001f);
            m_ListGroup.alpha = Mathf.Clamp01(t);
            if (t >= 1) m_Fading = false;
        }

        internal void Choose(int index)
        {
            if (index < 0 || index >= m_Options.Count || m_Options[index].Disabled)
            {
                if (index >= 0 && index < m_Rows.Count) m_Rows[index].Bind(m_Options[index], index == m_Value);
                return;
            }
            Value = index;
            if (m_CloseOnSelect) Hide();
            else
                for (int i = 0; i < m_Rows.Count; i++)
                    m_Rows[i].Bind(m_Options[i], i == index);
        }

        public override void OnMove(AxisEventData eventData)
        {
            if (!IsOpen)
            {
                base.OnMove(eventData);
                return;
            }
            if (eventData.moveDir == MoveDirection.Up) Step(-1);
            else if (eventData.moveDir == MoveDirection.Down) Step(1);
            eventData.Use();
        }

        /// <summary>Moves the highlight in an open list, past disabled options.</summary>
        internal void Step(int direction)
        {
            int count = m_Options.Count;
            if (count == 0) return;
            int at = m_Highlight;
            for (int i = 0; i < count; i++)
            {
                at = at < 0 && direction < 0 ? count - 1 : (at + direction + count) % count;
                if (m_Options[at].Disabled) continue;
                m_Highlight = at;
                if (at < m_Rows.Count && m_Rows[at].Toggle != null && EventSystem.current != null && !EventSystem.current.alreadySelecting) EventSystem.current.SetSelectedGameObject(m_Rows[at].Toggle.gameObject);
                return;
            }
        }

        /// <summary>Jumps to the next option starting with a letter.</summary>
        public void JumpToLetter(char letter)
        {
            letter = char.ToLowerInvariant(letter);
            int count = m_Options.Count;
            int start = IsOpen ? m_Highlight : m_Value;
            for (int i = 1; i <= count; i++)
            {
                int at = (Mathf.Max(start, -1) + i + count) % count;
                string text = m_Options[at].Text;
                if (m_Options[at].Disabled || string.IsNullOrEmpty(text) || char.ToLowerInvariant(text[0]) != letter) continue;
                if (IsOpen) m_Highlight = at;
                else Value = at;
                return;
            }
        }
    }
}

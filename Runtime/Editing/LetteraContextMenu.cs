using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace FirstSymbol.Lettera
{
    /// <summary>The shipped menu: UI objects you own, one for each command or one row for all of them.</summary>
    [AddComponentMenu("Lettera/Context Menu")]
    public class LetteraContextMenu : LetteraMenuPresenter
    {
        public enum Unavailable { Hide, Disable }

        /// <summary>One object in the layout.</summary>
        [Serializable]
        public class Item
        {
            public GameObject Target;
            [Tooltip("Which command. Empty makes this object the row for every command no other item binds.")]
            [SerializeReference] public LetteraEditCommand Command;
            public LetteraText Label;
            public Unavailable IfUnavailable;
        }

        [SerializeField] List<Item> m_Items = new List<Item>();
        [SerializeField] RectTransform m_Panel;
        [SerializeField] GameObject m_Blocker;

        readonly List<GameObject> m_Copies = new List<GameObject>();
        readonly ValueList<(GameObject target, int entry)> m_Shown = new ValueList<(GameObject, int)>();
        Action<int> m_Invoke;
        CanvasGroup m_Group;
        bool m_Visible;
        int m_Highlight = -1;

        public List<Item> Items => m_Items;
        public RectTransform Panel { get => m_Panel; set => m_Panel = value; }
        public GameObject Blocker { get => m_Blocker; set => m_Blocker = value; }
        public override bool IsVisible => m_Visible;

        /// <summary>Whether the menu holds the keyboard: everything but touch.</summary>
        public LetteraMenuTrigger LastTrigger { get; private set; }

        /// <summary>Titles of what is shown, in order.</summary>
        internal int ShownCount => m_Shown.Count;
        internal GameObject ShownTarget(int index) => m_Shown[index].target;
        internal int HighlightIndex => m_Highlight;

        CanvasGroup Group
        {
            get
            {
                if (m_Group != null) return m_Group;
                RectTransform panel = m_Panel != null ? m_Panel : (RectTransform)transform;
                m_Group = panel.GetComponent<CanvasGroup>();
                if (m_Group == null) m_Group = panel.gameObject.AddComponent<CanvasGroup>();
                return m_Group;
            }
        }

        public override void Show(in LetteraMenuPlacement placement, IReadOnlyList<LetteraMenuEntry> entries, Action<int> invoke)
        {
            m_Invoke = invoke;
            LastTrigger = placement.Trigger;
            ClearCopies();
            m_Shown.Clear();
            var bound = new bool[entries.Count];
            Item row = null;
            foreach (Item item in m_Items)
            {
                if (item.Target == null) continue;
                if (item.Command == null)
                {
                    row = item;
                    item.Target.SetActive(false);
                    continue;
                }
                int entry = -1;
                for (int i = 0; i < entries.Count; i++)
                    if (item.Command.SameAs(entries[i].Command))
                        entry = i;
                bool has = entry >= 0;
                if (has) bound[entry] = true;
                bool usable = has && entries[entry].IsEnabled;
                item.Target.SetActive(usable || (item.IfUnavailable == Unavailable.Disable));
                Bind(item.Target, item.Label, has ? entries[entry] : default, entry, usable);
            }
            if (row != null)
            {
                for (int i = 0; i < entries.Count; i++)
                {
                    if (bound[i] || (!entries[i].IsEnabled && row.IfUnavailable == Unavailable.Hide)) continue;
                    GameObject copy = Instantiate(row.Target, row.Target.transform.parent);
                    copy.hideFlags = HideFlags.DontSave;
                    copy.SetActive(true);
                    m_Copies.Add(copy);
                    Bind(copy, FindLabel(copy, row), entries[i], i, entries[i].IsEnabled);
                }
            }
            if (m_Shown.Count == 0)
            {
                Hide();
                return;
            }
            m_Visible = true;
            CanvasGroup group = Group;
            group.alpha = 1;
            group.interactable = true;
            group.blocksRaycasts = true;
            if (m_Blocker != null) m_Blocker.SetActive(placement.Trigger != LetteraMenuTrigger.Touch);
            Place(placement);
            foreach ((GameObject target, int entry) in m_Shown) LetteraWebGestures.Arm(entries[entry].Command, target.transform as RectTransform);
            m_Highlight = -1;
            if (placement.Trigger == LetteraMenuTrigger.Keyboard) MoveHighlight(1);
        }

        LetteraText FindLabel(GameObject copy, Item row)
        {
            if (row.Label == null) return null;
            string path = RelativePath(row.Target.transform, row.Label.transform);
            Transform found = path.Length == 0 ? copy.transform : copy.transform.Find(path);
            return found != null ? found.GetComponent<LetteraText>() : null;
        }

        static string RelativePath(Transform root, Transform child)
        {
            string path = "";
            for (Transform t = child; t != null && t != root; t = t.parent) path = path.Length == 0 ? t.name : t.name + "/" + path;
            return path;
        }

        void Bind(GameObject target, LetteraText label, in LetteraMenuEntry entry, int index, bool usable)
        {
            if (index >= 0)
            {
                if (label != null) label.Text = entry.Title;
                m_Shown.Add((target, index));
            }
            foreach (Selectable selectable in target.GetComponentsInChildren<Selectable>(true)) selectable.interactable = usable;
            if (target.GetComponent<LetteraKeepFocus>() == null) target.AddComponent<LetteraKeepFocus>();
            var button = target.GetComponent<Button>();
            var toggle = target.GetComponent<Toggle>();
            if (button != null)
            {
                button.onClick.RemoveAllListeners();
                if (usable) button.onClick.AddListener(() => Run(index));
            }
            else if (toggle != null)
            {
                toggle.onValueChanged.RemoveAllListeners();
                toggle.SetIsOnWithoutNotify(entry.IsOn);
                if (usable) toggle.onValueChanged.AddListener(_ => Run(index));
            }
            else
            {
                var click = target.GetComponent<LetteraMenuClick>();
                if (click == null) click = target.AddComponent<LetteraMenuClick>();
                click.Action = usable ? () => Run(index) : (Action)null;
            }
        }

        internal void Run(int entry) => m_Invoke?.Invoke(entry);

        void Place(in LetteraMenuPlacement placement)
        {
            RectTransform panel = m_Panel != null ? m_Panel : (RectTransform)transform;
            LayoutRebuilder.ForceRebuildLayoutImmediate(panel);
            Canvas canvas = panel.GetComponentInParent<Canvas>();
            float scale = canvas != null && canvas.scaleFactor > 0 ? canvas.scaleFactor : 1;
            Vector2 size = panel.rect.size * scale;
            Vector2 point = placement.Point;
            float x = point.x, y = point.y;
            if (placement.Avoid.size != Vector2.zero)
            {
                // Above what it must keep clear of, or below where only that fits.
                x = placement.Avoid.center.x - size.x * 0.5f;
                y = placement.Avoid.yMax + size.y + 8 <= Screen.height ? placement.Avoid.yMax + size.y + 8 : placement.Avoid.yMin - 8;
            }
            else if (placement.Trigger == LetteraMenuTrigger.Keyboard && y - size.y < 0)
            {
                y += size.y;
            }
            x = Mathf.Clamp(x, 0, Mathf.Max(Screen.width - size.x, 0));
            y = Mathf.Clamp(y, Mathf.Min(size.y, Screen.height), Screen.height);
            panel.pivot = new Vector2(0, 1);
            panel.anchorMin = panel.anchorMax = Vector2.zero;
            panel.anchoredPosition = new Vector2(x, y) / scale;
        }

        void ClearCopies()
        {
            foreach (GameObject copy in m_Copies) SelectionVisuals.Destroy(copy);
            m_Copies.Clear();
        }

        public override void Hide()
        {
            if (!m_Visible && m_Group == null) return;
            m_Visible = false;
            m_Highlight = -1;
            CanvasGroup group = Group;
            group.alpha = 0;
            group.interactable = false;
            group.blocksRaycasts = false;
            if (m_Blocker != null) m_Blocker.SetActive(false);
            foreach ((GameObject target, int _) in m_Shown)
                if (target != null) LetteraWebGestures.Disarm(target.transform as RectTransform);
            ClearCopies();
        }

        public override bool HandleKey(LetteraKeyCode key, LetteraKeyModifiers modifiers)
        {
            if (!m_Visible || LastTrigger == LetteraMenuTrigger.Touch) return false;
            switch (key)
            {
                case LetteraKeyCode.Escape: Hide(); return true;
                case LetteraKeyCode.Up: MoveHighlight(-1); return true;
                case LetteraKeyCode.Down: MoveHighlight(1); return true;
                case LetteraKeyCode.Home:
                    m_Highlight = -1;
                    MoveHighlight(1);
                    return true;
                case LetteraKeyCode.End:
                    m_Highlight = m_Shown.Count;
                    MoveHighlight(-1);
                    return true;
                case LetteraKeyCode.Enter:
                case LetteraKeyCode.KeypadEnter:
                    if (m_Highlight >= 0 && m_Highlight < m_Shown.Count) Run(m_Shown[m_Highlight].entry);
                    return true;
                default:
                    return true;
            }
        }

        void MoveHighlight(int step)
        {
            if (m_Shown.Count == 0) return;
            int start = m_Highlight;
            for (int i = 0; i < m_Shown.Count; i++)
            {
                m_Highlight = ((m_Highlight < 0 && step < 0 ? 0 : m_Highlight) + step + m_Shown.Count) % m_Shown.Count;
                var selectable = m_Shown[m_Highlight].target.GetComponent<Selectable>();
                if (selectable == null || selectable.interactable)
                {
                    if (selectable != null && EventSystem.current != null && !EventSystem.current.alreadySelecting) selectable.OnPointerEnter(new PointerEventData(EventSystem.current));
                    if (start >= 0 && start < m_Shown.Count && start != m_Highlight)
                    {
                        var previous = m_Shown[start].target.GetComponent<Selectable>();
                        if (previous != null && EventSystem.current != null) previous.OnPointerExit(new PointerEventData(EventSystem.current));
                    }
                    return;
                }
            }
        }

        /// <summary>The menu every field gets unless a project names another: built here, so no asset has to ship.</summary>
        internal static LetteraContextMenu CreateDefault()
        {
            Canvas overlay = LetteraTouchOverlay.Canvas;
            var root = new GameObject("Lettera Context Menu", typeof(RectTransform)) { hideFlags = HideFlags.DontSave };
            root.transform.SetParent(overlay.transform, false);
            Stretch((RectTransform)root.transform);

            var blocker = new GameObject("Blocker", typeof(RectTransform), typeof(Image)) { hideFlags = HideFlags.DontSave };
            blocker.transform.SetParent(root.transform, false);
            Stretch((RectTransform)blocker.transform);
            blocker.GetComponent<Image>().color = Color.clear;
            blocker.AddComponent<LetteraKeepFocus>();

            var panelObject = new GameObject("Panel", typeof(RectTransform), typeof(Image), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter)) { hideFlags = HideFlags.DontSave };
            panelObject.transform.SetParent(root.transform, false);
            panelObject.GetComponent<Image>().color = new Color(0.16f, 0.17f, 0.2f, 0.98f);
            var layout = panelObject.GetComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(4, 4, 4, 4);
            layout.spacing = 1;
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            var fit = panelObject.GetComponent<ContentSizeFitter>();
            fit.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            panelObject.AddComponent<LetteraKeepFocus>();

            var rowObject = new GameObject("Row", typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement)) { hideFlags = HideFlags.DontSave };
            rowObject.transform.SetParent(panelObject.transform, false);
            rowObject.GetComponent<Image>().color = new Color(1, 1, 1, 0);
            rowObject.GetComponent<LayoutElement>().minWidth = 140;
            // A finger needs a taller row than a pointer.
            rowObject.GetComponent<LayoutElement>().minHeight = Application.isMobilePlatform ? 44 : 32;
            ColorBlock colors = rowObject.GetComponent<Button>().colors;
            colors.normalColor = new Color(1, 1, 1, 0);
            colors.highlightedColor = new Color(0.2f, 0.565f, 1f, 1f);
            colors.pressedColor = new Color(0.15f, 0.45f, 0.85f, 1f);
            colors.selectedColor = colors.normalColor;
            colors.disabledColor = new Color(1, 1, 1, 0);
            colors.fadeDuration = 0;
            rowObject.GetComponent<Button>().colors = colors;

            var labelObject = new GameObject("Label", typeof(RectTransform)) { hideFlags = HideFlags.DontSave };
            labelObject.transform.SetParent(rowObject.transform, false);
            var labelRect = (RectTransform)labelObject.transform;
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = new Vector2(10, 0);
            labelRect.offsetMax = new Vector2(-10, 0);
            var label = labelObject.AddComponent<LetteraText>();
            label.FontSize = 16;
            label.WordWrap = false;
            label.VerticalAlignment = LetteraVerticalAlignment.Middle;
            label.raycastTarget = false;
            label.color = new Color(0.93f, 0.94f, 0.96f, 1);

            var menu = root.AddComponent<LetteraContextMenu>();
            menu.m_Panel = (RectTransform)panelObject.transform;
            menu.m_Blocker = blocker;
            menu.m_Items.Add(new Item { Target = rowObject, Label = label, IfUnavailable = Unavailable.Hide });
            blocker.AddComponent<LetteraMenuClick>().Action = menu.Hide;
            rowObject.SetActive(false);
            blocker.SetActive(false);
            menu.Group.alpha = 0;
            menu.Group.blocksRaycasts = false;
            return menu;
        }

        static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
        }
    }
}

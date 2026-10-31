using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace FirstSymbol.Lettera
{
    /// <summary>What a command can act on.</summary>
    public readonly struct LetteraCommandContext
    {
        public readonly LetteraSelectable Selectable;
        public readonly LetteraEditable Editable;

        public LetteraCommandContext(LetteraSelectable selectable)
        {
            Selectable = selectable;
            Editable = selectable != null ? selectable.Editable : null;
        }

        public bool CanEdit => Editable != null && !Editable.ReadOnly;
        public bool CanCopy => Selectable != null && (Editable == null || Editable.CanCopy);
        public bool HasSelection => Selectable != null && !Selectable.Selection.IsCaret;

        /// <summary>Holds the selection for an answer that comes later, such as a translation.</summary>
        public LetteraHeldSelection HoldSelection() => new LetteraHeldSelection(Editable);
    }

    /// <summary>A selection held for later. Replacing it does nothing if the text changed meanwhile.</summary>
    public sealed class LetteraHeldSelection
    {
        readonly LetteraEditable m_Field;
        readonly int m_Version, m_Start, m_End;

        internal LetteraHeldSelection(LetteraEditable field)
        {
            m_Field = field;
            if (field == null) return;
            m_Version = field.Version;
            m_Start = field.Selectable.Selection.Start;
            m_End = field.Selectable.Selection.End;
        }

        public bool Replace(string text)
        {
            if (m_Field == null || m_Field.Version != m_Version) return false;
            return m_Field.Replace(m_Start, m_End, text, "user.type");
        }
    }

    /// <summary>Something a user can run on a text: from the menu, a button or a shortcut. Commands are the one path for all of those.</summary>
    [Serializable]
    public abstract class LetteraEditCommand
    {
        [Tooltip("Empty uses the command's own name.")]
        [SerializeField] string m_Title = "";

        /// <summary>Supplies titles from elsewhere, such as a localization table. Asked first.</summary>
        public static Func<LetteraEditCommand, string> TitleSource;

        public string Title
        {
            get
            {
                string fromSource = TitleSource?.Invoke(this);
                if (!string.IsNullOrEmpty(fromSource)) return fromSource;
                return string.IsNullOrEmpty(m_Title) ? FallbackTitle : m_Title;
            }
            set => m_Title = value;
        }

        public virtual string FallbackTitle
        {
            get
            {
                string name = GetType().Name;
                if (name.EndsWith("Command", StringComparison.Ordinal)) name = name.Substring(0, name.Length - 7);
                var builder = new System.Text.StringBuilder(name.Length + 4);
                for (int i = 0; i < name.Length; i++)
                {
                    if (i > 0 && char.IsUpper(name[i])) builder.Append(' ').Append(char.ToLowerInvariant(name[i]));
                    else builder.Append(name[i]);
                }
                return builder.ToString();
            }
        }

        public abstract bool CanRun(in LetteraCommandContext context);
        public abstract void Execute(in LetteraCommandContext context);
        public virtual bool IsOn(in LetteraCommandContext context) => false;

        /// <summary>The menu stays up after this command.</summary>
        public virtual bool StaysOpen => false;

        /// <summary>Whether a menu item bound to this command shows that one.</summary>
        public virtual bool SameAs(LetteraEditCommand other) => other != null && other.GetType() == GetType();

        internal virtual void Expand(List<LetteraEditCommand> results) => results.Add(this);

        public static List<LetteraEditCommand> DefaultProjectList() => new List<LetteraEditCommand>
        {
            new CutCommand(), new CopyCommand(), new PasteCommand(), new PastePlainCommand(), new SelectAllCommand(),
        };
    }

    [Serializable] public class CutCommand : LetteraEditCommand
    {
        public override bool CanRun(in LetteraCommandContext c) => c.CanEdit && c.HasSelection && c.CanCopy;
        public override void Execute(in LetteraCommandContext c) => c.Editable.Cut();
    }

    [Serializable] public class CopyCommand : LetteraEditCommand
    {
        public override bool CanRun(in LetteraCommandContext c) => c.HasSelection && c.CanCopy;
        public override void Execute(in LetteraCommandContext c) => c.Selectable.Copy();
    }

    [Serializable] public class PasteCommand : LetteraEditCommand
    {
        public override bool CanRun(in LetteraCommandContext c) => c.CanEdit && LetteraClipboard.HasText;
        public override void Execute(in LetteraCommandContext c) => c.Editable.RunPaste();
    }

    [Serializable] public class PastePlainCommand : LetteraEditCommand
    {
        public override string FallbackTitle => "Paste as plain text";
        public override bool CanRun(in LetteraCommandContext c) => c.CanEdit && LetteraClipboard.HasRichText;
        public override void Execute(in LetteraCommandContext c) => c.Editable.PastePlain();
    }

    [Serializable] public class SelectAllCommand : LetteraEditCommand
    {
        public override bool StaysOpen => true;
        public override bool CanRun(in LetteraCommandContext c) => c.Selectable != null && c.Selectable.Selection.Length < c.Selectable.Shown.Length;
        public override void Execute(in LetteraCommandContext c) => c.Selectable.SelectAll();
    }

    [Serializable] public class UndoCommand : LetteraEditCommand
    {
        public override bool CanRun(in LetteraCommandContext c) => c.CanEdit && c.Editable.CanUndo;
        public override void Execute(in LetteraCommandContext c) => c.Editable.Undo();
    }

    [Serializable] public class RedoCommand : LetteraEditCommand
    {
        public override bool CanRun(in LetteraCommandContext c) => c.CanEdit && c.Editable.CanRedo;
        public override void Execute(in LetteraCommandContext c) => c.Editable.Redo();
    }

    /// <summary>Turns a style on or off, for a field that carries that style. By default bold.</summary>
    [Serializable] public class ToggleEffectCommand : LetteraEditCommand
    {
        [Tooltip("The full name of the effect type.")]
        [SerializeField] string m_EffectType = typeof(BoldEffect).FullName;
        [SerializeField] string m_Parameter = "";
        Type m_Resolved;

        public string EffectType { get => m_EffectType; set { m_EffectType = value; m_Resolved = null; } }
        public string Parameter { get => m_Parameter; set => m_Parameter = value; }
        Type Resolved => m_Resolved ?? (m_Resolved = string.IsNullOrEmpty(m_EffectType) ? null : typeof(LetteraEffect).Assembly.GetType(m_EffectType));
        public override string FallbackTitle => Resolved != null ? Resolved.Name.Replace("Effect", "") : "Style";
        public override bool StaysOpen => true;

        public override bool CanRun(in LetteraCommandContext c) => c.Editable != null && Resolved != null && c.Editable.CanFormat(Resolved);
        public override void Execute(in LetteraCommandContext c) => c.Editable.ToggleEffect(Resolved, string.IsNullOrEmpty(m_Parameter) ? null : m_Parameter, "user.format");
        public override bool IsOn(in LetteraCommandContext c) => c.Editable != null && Resolved != null && c.Editable.IsEffectActive(Resolved);
        public override bool SameAs(LetteraEditCommand other) => other is ToggleEffectCommand t && t.m_EffectType == m_EffectType;
    }

    [Serializable] public class ClearStylesCommand : LetteraEditCommand
    {
        public override string FallbackTitle => "Clear formatting";
        public override bool CanRun(in LetteraCommandContext c) => c.CanEdit;
        public override void Execute(in LetteraCommandContext c) => c.Editable.ClearStyles("user.format");
    }

    /// <summary>Opens a site with the selected text, such as a search.</summary>
    [Serializable] public class WebLookupCommand : LetteraEditCommand
    {
        [Tooltip("{0} becomes the selected text, encoded for a URL.")]
        [SerializeField] string m_Url = "https://www.google.com/search?q={0}";

        /// <summary>Replaces how a URL opens. Tests use this.</summary>
        public static Action<string> Opener;

        public string Url { get => m_Url; set => m_Url = value; }
        public override string FallbackTitle => "Search the web";
        public override bool CanRun(in LetteraCommandContext c) => c.HasSelection && c.CanCopy && !string.IsNullOrEmpty(m_Url);

        public override void Execute(in LetteraCommandContext c)
        {
            string url = string.Format(m_Url, Uri.EscapeDataString(c.Selectable.SelectedPlainText()));
            if (Opener != null) Opener(url);
            else Application.OpenURL(url);
        }
    }

    /// <summary>The system's share sheet. It keeps its place in a list until a platform can answer.</summary>
    [Serializable] public class ShareTextCommand : LetteraEditCommand
    {
        /// <summary>Set by what can share on this platform.</summary>
        public static Action<string> Sharer;

        public override string FallbackTitle => "Share";
        public override bool CanRun(in LetteraCommandContext c) => Sharer != null && c.HasSelection && c.CanCopy;
        public override void Execute(in LetteraCommandContext c) => Sharer?.Invoke(c.Selectable.SelectedPlainText());
    }

    /// <summary>The system's own text actions. It keeps its place in a list until a platform can answer.</summary>
    [Serializable] public class PlatformActionsCommand : LetteraEditCommand
    {
        public override string FallbackTitle => "More";
        public override bool CanRun(in LetteraCommandContext c) => false;
        public override void Execute(in LetteraCommandContext c) { }
    }

    /// <summary>Stands for the list in the project settings, so that one change reaches every field.</summary>
    [Serializable] public class ProjectCommandList : LetteraEditCommand
    {
        public override bool CanRun(in LetteraCommandContext c) => false;
        public override void Execute(in LetteraCommandContext c) { }

        internal override void Expand(List<LetteraEditCommand> results)
        {
            foreach (LetteraEditCommand command in LetteraSettings.Instance.Editing.Commands)
                if (command != null && !(command is ProjectCommandList))
                    command.Expand(results);
        }
    }

    public enum LetteraMenuTrigger { Touch, Pointer, Keyboard }

    /// <summary>Where a menu goes.</summary>
    public struct LetteraMenuPlacement
    {
        /// <summary>The asked-for point, in screen pixels.</summary>
        public Vector2 Point;

        /// <summary>A screen rectangle to keep clear of, such as the selection. Zero size for none.</summary>
        public Rect Avoid;
        public LetteraMenuTrigger Trigger;
        public Camera Camera;
    }

    /// <summary>One line of a menu.</summary>
    public struct LetteraMenuEntry
    {
        public string Title;
        public LetteraEditCommand Command;
        public bool IsEnabled, StaysOpen, IsOn;
    }

    /// <summary>Shows menu entries. Subclass for a radial or animated one: it only has to show them and invoke the chosen one.</summary>
    public abstract class LetteraMenuPresenter : MonoBehaviour
    {
        public abstract void Show(in LetteraMenuPlacement placement, IReadOnlyList<LetteraMenuEntry> entries, Action<int> invoke);
        public abstract void Hide();
        public abstract bool IsVisible { get; }

        /// <summary>A key while the menu holds the keyboard. True when the menu took it.</summary>
        public virtual bool HandleKey(LetteraKeyCode key, LetteraKeyModifiers modifiers) => false;
    }

    /// <summary>A shared screen-space canvas for what must not be clipped by a field: handles, menus and the loupe.</summary>
    internal static class LetteraTouchOverlay
    {
        static Canvas s_Canvas;

        public static Canvas Canvas
        {
            get
            {
                if (s_Canvas != null) return s_Canvas;
                var go = new GameObject("Lettera Touch Overlay", typeof(RectTransform)) { hideFlags = HideFlags.DontSave };
                if (Application.isPlaying) UnityEngine.Object.DontDestroyOnLoad(go);
                s_Canvas = go.AddComponent<Canvas>();
                s_Canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                s_Canvas.sortingOrder = 30000;
                s_Canvas.additionalShaderChannels |= AdditionalCanvasShaderChannels.TexCoord1 | AdditionalCanvasShaderChannels.TexCoord2 | AdditionalCanvasShaderChannels.TexCoord3;
                go.AddComponent<GraphicRaycaster>();
                return s_Canvas;
            }
        }

        public static void DestroyForTests()
        {
            if (s_Canvas != null) SelectionVisuals.Destroy(s_Canvas.gameObject);
            s_Canvas = null;
        }
    }

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
        readonly List<(GameObject target, int entry)> m_Shown = new List<(GameObject, int)>();
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
            rowObject.GetComponent<LayoutElement>().minHeight = 32;
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

    /// <summary>Runs an action on a click, on an object with no Button.</summary>
    [AddComponentMenu("")]
    internal sealed class LetteraMenuClick : MonoBehaviour, IPointerClickHandler
    {
        public Action Action;
        public void OnPointerClick(PointerEventData eventData) => Action?.Invoke();
    }

    /// <summary>Binds a Button or a Toggle to a command, for a toolbar. Without a target it acts on the field that has the keyboard.</summary>
    [AddComponentMenu("Lettera/Command Control")]
    public class LetteraCommandControl : MonoBehaviour
    {
        [SerializeReference] LetteraEditCommand m_Command;
        [SerializeField] LetteraSelectable m_Target;

        Button m_Button;
        Toggle m_Toggle;
        LetteraSelectable m_Listening;

        public LetteraEditCommand Command { get => m_Command; set { m_Command = value; Refresh(); } }
        public LetteraSelectable Target { get => m_Target; set { m_Target = value; Refresh(); } }

        LetteraSelectable Effective => m_Target != null ? m_Target : LetteraSelectable.Current;

        void OnEnable()
        {
            m_Button = GetComponent<Button>();
            m_Toggle = GetComponent<Toggle>();
            if (m_Button != null) m_Button.onClick.AddListener(Run);
            if (m_Toggle != null) m_Toggle.onValueChanged.AddListener(OnToggle);
            if (GetComponent<LetteraKeepFocus>() == null) gameObject.AddComponent<LetteraKeepFocus>();
            LetteraClipboard.Written += Refresh;
            LetteraWebGestures.Arm(m_Command, transform as RectTransform);
            Refresh();
        }

        void OnDisable()
        {
            if (m_Button != null) m_Button.onClick.RemoveListener(Run);
            if (m_Toggle != null) m_Toggle.onValueChanged.RemoveListener(OnToggle);
            LetteraClipboard.Written -= Refresh;
            LetteraWebGestures.Disarm(transform as RectTransform);
            Listen(null);
        }

        void OnApplicationFocus(bool focus)
        {
            if (focus) Refresh();
        }

        void LateUpdate()
        {
            // The field that has the keyboard changes without telling a toolbar.
            if (Effective != m_Listening) Refresh();
        }

        void OnToggle(bool on) => Run();

        internal void Run()
        {
            LetteraSelectable target = Effective;
            if (m_Command == null || target == null) return;
            var context = new LetteraCommandContext(target);
            if (m_Command.CanRun(context)) m_Command.Execute(context);
            Refresh();
        }

        void Listen(LetteraSelectable target)
        {
            if (m_Listening == target) return;
            if (m_Listening != null)
            {
                m_Listening.SelectionChanged -= OnSelection;
                if (m_Listening.Editable != null) m_Listening.Editable.CaretStylesChanged -= OnStyles;
            }
            m_Listening = target;
            if (target != null)
            {
                target.SelectionChanged += OnSelection;
                if (target.Editable != null) target.Editable.CaretStylesChanged += OnStyles;
            }
        }

        void OnSelection(LetteraSelectionChange change) => Refresh();
        void OnStyles(LetteraCaretStyles styles) => Refresh();

        void Refresh()
        {
            LetteraSelectable target = Effective;
            Listen(target);
            bool usable = false, on = false;
            if (m_Command != null && target != null)
            {
                var context = new LetteraCommandContext(target);
                usable = m_Command.CanRun(context);
                on = m_Command.IsOn(context);
            }
            if (m_Button != null) m_Button.interactable = usable;
            if (m_Toggle != null)
            {
                m_Toggle.interactable = usable;
                m_Toggle.SetIsOnWithoutNotify(on);
            }
        }
    }

    public partial class LetteraSelectable
    {
        [SerializeReference] List<LetteraEditCommand> m_Commands = new List<LetteraEditCommand> { new ProjectCommandList() };
        [SerializeField] LetteraMenuPresenter m_MenuPresenter;
        LetteraMenuPresenter m_Menu;
        readonly List<LetteraEditCommand> m_MenuCommands = new List<LetteraEditCommand>();
        readonly List<LetteraMenuEntry> m_MenuEntries = new List<LetteraMenuEntry>();
        LetteraMenuPlacement m_MenuPlacement;

        static LetteraContextMenu s_DefaultMenu;

        /// <summary>What this text's menu offers, in order. An empty list means no menu.</summary>
        public List<LetteraEditCommand> Commands => m_Commands;

        /// <summary>What shows the menu. Empty uses the project's, then the built-in one.</summary>
        public LetteraMenuPresenter MenuPresenter { get => m_MenuPresenter; set => m_MenuPresenter = value; }

        /// <summary>Raised when a menu is about to open. Set Handled to stop the built-in one.</summary>
        public event MenuRequest MenuRequested;

        public delegate void MenuRequest(in LetteraMenuPlacement placement, ref bool handled);

        LetteraMenuPresenter ResolvePresenter()
        {
            if (m_MenuPresenter != null) return m_MenuPresenter;
            LetteraMenuPresenter system = LetteraSystemUi.Menu();
            if (system != null) return system;
            LetteraMenuPresenter project = LetteraSettings.Instance.Editing.MenuPrefab;
            if (project != null)
            {
                if (project.gameObject.scene.IsValid()) return project;
                if (s_ProjectMenu == null || s_ProjectMenuPrefab != project)
                {
                    s_ProjectMenu = Instantiate(project, LetteraTouchOverlay.Canvas.transform);
                    s_ProjectMenuPrefab = project;
                }
                return s_ProjectMenu;
            }
            if (s_DefaultMenu == null) s_DefaultMenu = LetteraContextMenu.CreateDefault();
            return s_DefaultMenu;
        }

        static LetteraMenuPresenter s_ProjectMenu, s_ProjectMenuPrefab;

        internal IReadOnlyList<LetteraMenuEntry> MenuEntries => m_MenuEntries;

        void BuildMenuEntries()
        {
            m_MenuCommands.Clear();
            m_MenuEntries.Clear();
            foreach (LetteraEditCommand command in m_Commands) command?.Expand(m_MenuCommands);
            var context = new LetteraCommandContext(this);
            foreach (LetteraEditCommand command in m_MenuCommands)
            {
                bool can = command.CanRun(context);
                m_MenuEntries.Add(new LetteraMenuEntry { Title = command.Title, Command = command, IsEnabled = can, StaysOpen = command.StaysOpen, IsOn = command.IsOn(context) });
            }
        }

        /// <summary>Opens the menu with its placement chosen by the kind of input.</summary>
        public void RequestMenu(Vector2 screenPoint, Camera camera = null)
        {
            bool touching = m_Touch && (m_Pressed || m_LongPressed);
            OpenMenu(new LetteraMenuPlacement { Point = screenPoint, Trigger = touching ? LetteraMenuTrigger.Touch : LetteraMenuTrigger.Pointer, Camera = camera });
        }

        internal void OpenMenu(LetteraMenuPlacement placement)
        {
            bool handled = false;
            MenuRequested?.Invoke(placement, ref handled);
            if (handled) return;
            BuildMenuEntries();
            bool any = false;
            foreach (LetteraMenuEntry entry in m_MenuEntries) any |= entry.IsEnabled;
            if (!any)
            {
                HideMenu();
                return;
            }
            m_MenuPlacement = placement;
            m_Menu = ResolvePresenter();
            m_Menu.Show(placement, m_MenuEntries, RunMenuEntry);
        }

        void RunMenuEntry(int index)
        {
            if (index < 0 || index >= m_MenuEntries.Count) return;
            LetteraMenuEntry entry = m_MenuEntries[index];
            if (!entry.IsEnabled) return;
            entry.Command.Execute(new LetteraCommandContext(this));
            if (entry.StaysOpen)
            {
                if (m_Menu != null && m_Menu.IsVisible) OpenMenu(m_MenuPlacement);
                return;
            }
            HideMenu();
            // A finger's command leaves a caret on Android and the selection on Apple systems.
            if (m_MenuPlacement.Trigger == LetteraMenuTrigger.Touch && LetteraKeyTable.Family == LetteraPlatformFamily.Pc && !m_Selection.IsCaret)
                SetSelectionUnchecked(LetteraSelectionRange.AtCaret(m_Selection.Head, m_Selection.Bias), "sel.clear");
        }

        internal void HideMenu()
        {
            if (m_Menu != null) m_Menu.Hide();
            m_Menu = null;
        }

        internal void HideMenuOnEdit()
        {
            HideMenu();
            HideHandlesOnEdit();
        }

        void OpenMenuFromKeyboard()
        {
            Camera camera = EventCameraGuess();
            Engine.EnsurePositioned();
            if (!TryCaretOnScreen(camera, out Rect caret)) return;
            OpenMenu(new LetteraMenuPlacement { Point = new Vector2(caret.x, caret.yMin), Trigger = LetteraMenuTrigger.Keyboard, Camera = camera });
        }

        void ToggleMenuAtCaret()
        {
            if (m_Menu != null && m_Menu.IsVisible)
            {
                HideMenu();
                return;
            }
            ShowTouchMenu();
        }

        void ShowTouchMenu()
        {
            Camera camera = m_PointerCamera != null ? m_PointerCamera : EventCameraGuess();
            Rect avoid = default;
            if (!TrySelectionOnScreen(camera, out avoid)) TryCaretOnScreen(camera, out avoid);
            if (m_Handles is ILetteraHandleBounds bounds) avoid = bounds.Grow(avoid);
            OpenMenu(new LetteraMenuPlacement { Point = avoid.center, Avoid = avoid.size == Vector2.zero ? new Rect(avoid.position, Vector2.one) : avoid, Trigger = LetteraMenuTrigger.Touch, Camera = camera });
        }

        internal Camera EventCameraGuess()
        {
            Canvas canvas = GetComponentInParent<Canvas>();
            if (canvas != null) return canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
            return Camera.main;
        }

        internal bool IsPressed => m_Pressed;

        bool m_SelectAllScheduled;

        // A press selects by itself right after it takes the focus, so what a rule wants comes last.
        internal void ScheduleSelectAll()
        {
            if (m_Pressed) m_SelectAllScheduled = true;
            else SelectAll();
        }

    }
}

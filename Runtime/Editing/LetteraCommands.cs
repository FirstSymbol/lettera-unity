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

        internal void BuildMenuEntries()
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
            m_MenuPlacement = placement;
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
            m_Menu = ResolvePresenter();
            m_Menu.Show(placement, m_MenuEntries, RunMenuEntry);
        }

        internal void RunMenuEntry(int index)
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

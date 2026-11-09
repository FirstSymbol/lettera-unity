using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.ShortcutManagement;
using UnityEngine;
using UnityEngine.UIElements;

namespace FirstSymbol.Lettera.Editor
{
    /// <summary>Where the palette was opened from: what it offers first depends on this.</summary>
    internal enum LetteraPaletteSource { Anywhere, Scene, Project }

    /// <summary>One thing the palette can do.</summary>
    internal sealed class LetteraCommand
    {
        public string Name, Group, Words, Keys;

        /// <summary>The menu item it runs, if it is one.</summary>
        public string MenuPath;

        /// <summary>Anywhere: always offered. Scene or Project: when opened there, or by the key.</summary>
        public LetteraPaletteSource Source = LetteraPaletteSource.Anywhere;
        public Action Run;
        public Func<bool> Available;
    }

    /// <summary>
    /// Everything Lettera can make or open, by typing. Ctrl+Alt+L, or Shift and a right click in the Hierarchy, the Scene view and the Project window.
    /// In whichever layout the keyboard is in: ищдв finds Bold. What was used last comes first.
    /// </summary>
    internal sealed class LetteraPalette : EditorWindow
    {
        const string RecentKey = "Lettera.Palette.Recent";
        const int MaxShown = 60;
        static readonly Vector2 s_Size = new Vector2(440, 400);

        /// <summary>The object new things go under, when the palette was opened on one.</summary>
        internal static GameObject s_Parent;

        readonly List<LetteraCommand> m_Shown = new List<LetteraCommand>();
        List<LetteraCommand> m_All;
        ScrollView m_List;
        LtSearch m_Search;
        Label m_GroupChip;
        LetteraPaletteSource m_Source;
        string m_Group;
        int m_At;

        [Shortcut("Lettera/Commands", KeyCode.L, ShortcutModifiers.Action | ShortcutModifiers.Alt)]
        [MenuItem("Tools/FirstSymbol/Lettera/Commands...", false, 0)]
        internal static void Open()
        {
            Rect main = EditorGUIUtility.GetMainWindowPosition();
            Open(LetteraPaletteSource.Anywhere, Selection.activeGameObject, new Vector2(main.center.x - s_Size.x / 2, main.y + 90));
        }

        /// <summary>Opens at a point on the screen, for what was right clicked.</summary>
        internal static LetteraPalette Open(LetteraPaletteSource source, GameObject parent, Vector2 screen)
        {
            s_Parent = parent;
            var window = CreateInstance<LetteraPalette>();
            window.titleContent = new GUIContent("Lettera");
            window.m_Source = source;
            window.ShowAsDropDown(new Rect(screen.x, screen.y, 1, 1), s_Size);
            return window;
        }

        internal VisualElement BuildPage(LetteraPaletteSource source)
        {
            m_Source = source;
            m_All = Commands();
            VisualElement root = LtUI.Root();
            root.AddToClassList("lt-popup");
            root.style.flexGrow = 1;
            if (source != LetteraPaletteSource.Project) root.Add(LtUI.Text(ParentLine(), "lt-hint lt-palette__parent"));
            VisualElement top = LtUI.El("lt-palette__top");
            m_GroupChip = LtUI.Clickable("", "lt-chip lt-chip--accent lt-chip--click", "Backspace or a click leaves this group.", () => EnterGroup(null));
            m_GroupChip.style.display = DisplayStyle.None;
            top.Add(m_GroupChip);
            m_Search = new LtSearch("Type what you need, in any layout. Tab goes into a group");
            m_Search.style.flexGrow = 1;
            m_Search.Changed += Refresh;
            top.Add(m_Search);
            root.Add(top);
            m_List = new ScrollView();
            m_List.style.flexGrow = 1;
            root.Add(m_List);
            root.RegisterCallback<KeyDownEvent>(OnKey, TrickleDown.TrickleDown);
            Refresh("");
            return root;
        }

        void CreateGUI()
        {
            rootVisualElement.Add(BuildPage(m_Source));
            m_Search.Field.schedule.Execute(() => m_Search.Field.Focus());
        }

        /// <summary>Says where a thing made now would land.</summary>
        static string ParentLine()
        {
            if (s_Parent == null) return "New objects go at the top of the scene, or under a Canvas when they need one.";
            string path = s_Parent.name;
            for (Transform at = s_Parent.transform.parent; at != null; at = at.parent) path = at.name + "/" + path;
            return "New objects go under " + path;
        }

        void OnKey(KeyDownEvent e)
        {
            switch (e.keyCode)
            {
                case KeyCode.Escape:
                    Close();
                    break;
                case KeyCode.UpArrow:
                    Move(-1);
                    break;
                case KeyCode.DownArrow:
                    Move(1);
                    break;
                case KeyCode.Return:
                case KeyCode.KeypadEnter:
                    if (m_At >= 0 && m_At < m_Shown.Count) Execute(m_Shown[m_At]);
                    break;
                case KeyCode.Tab:
                    if (m_At >= 0 && m_At < m_Shown.Count) EnterGroup(m_Shown[m_At].Group);
                    e.StopPropagation();
                    m_Search.focusController?.IgnoreEvent(e);
                    return;
                case KeyCode.Backspace when string.IsNullOrEmpty(m_Search.Value) && m_Group != null:
                    EnterGroup(null);
                    break;
                default:
                    return;
            }
            e.StopPropagation();
        }

        internal void EnterGroup(string group)
        {
            m_Group = group;
            m_GroupChip.text = group ?? "";
            m_GroupChip.style.display = group != null ? DisplayStyle.Flex : DisplayStyle.None;
            m_Search.Field.value = "";
            Refresh("");
            m_Search.Field.schedule.Execute(() => m_Search.Field.Focus());
        }

        void Move(int delta)
        {
            if (m_Shown.Count == 0) return;
            m_At = Mathf.Clamp(m_At + delta, 0, m_Shown.Count - 1);
            Paint();
        }

        internal IReadOnlyList<LetteraCommand> Shown => m_Shown;

        internal void Refresh(string query)
        {
            m_Shown.Clear();
            IEnumerable<LetteraCommand> found = Find(m_All, query, Recent(), m_Source);
            if (m_Group != null) found = found.Where(c => c.Group == m_Group);
            m_Shown.AddRange(found.Take(MaxShown));
            m_At = 0;
            m_List.Clear();
            foreach (LetteraCommand command in m_Shown)
            {
                VisualElement row = LtUI.El("lt-palette__row");
                row.Add(LtUI.Text(command.Name, "lt-palette__name"));
                if (!string.IsNullOrEmpty(command.Keys)) row.Add(LtUI.Text(command.Keys, "lt-palette__keys"));
                row.Add(LtUI.Text(command.Group, "lt-palette__group"));
                LetteraCommand chosen = command;
                row.RegisterCallback<ClickEvent>(_ => Execute(chosen));
                m_List.Add(row);
            }
            if (m_Shown.Count == 0) m_List.Add(LtUI.Text("Nothing by that name. Try another word.", "lt-hint"));
            Paint();
        }

        void Paint()
        {
            for (int i = 0; i < m_Shown.Count; i++) m_List[i].EnableInClassList("lt-palette__row--on", i == m_At);
            if (m_List.panel != null && m_At < m_Shown.Count && m_Shown.Count > 0) m_List.ScrollTo(m_List[m_At]);
        }

        void Execute(LetteraCommand command)
        {
            Remember(command.Name);
            Close();
            command.Run();
        }

        /// <summary>Best first; with nothing typed, what was used last. A window offers what fits it; what is typed may still find the rest.</summary>
        internal static IEnumerable<LetteraCommand> Find(List<LetteraCommand> all, string query, List<string> recent, LetteraPaletteSource source = LetteraPaletteSource.Anywhere)
        {
            bool typed = !string.IsNullOrWhiteSpace(query);
            var ranked = new List<(LetteraCommand command, int score)>();
            foreach (LetteraCommand command in all)
            {
                if (command.Available != null && !command.Available()) continue;
                bool fitsHere = source == LetteraPaletteSource.Anywhere || command.Source == LetteraPaletteSource.Anywhere || command.Source == source;
                if (!typed && !fitsHere) continue;
                int score = Math.Max(LtSearchText.Score(query, command.Name) * 2, LtSearchText.Score(query, command.Words));
                if (score == 0) continue;
                if (fitsHere && command.Source == source && source != LetteraPaletteSource.Anywhere) score += 40;
                int place = recent.IndexOf(command.Name);
                if (place >= 0) score += 20 - Math.Min(place, 19);
                ranked.Add((command, score));
            }
            return ranked.OrderByDescending(r => r.score).ThenBy(r => r.command.Name, StringComparer.Ordinal).Select(r => r.command);
        }

        static List<string> Recent() => LtPrefs.GetString(RecentKey, "").Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries).ToList();

        static void Remember(string name)
        {
            List<string> recent = Recent();
            recent.Remove(name);
            recent.Insert(0, name);
            if (recent.Count > 12) recent.RemoveRange(12, recent.Count - 12);
            LtPrefs.SetString(RecentKey, string.Join("\n", recent));
        }

        static LetteraCommand Menu(string name, string group, string words, string menu, LetteraPaletteSource source = LetteraPaletteSource.Anywhere, string keys = null) =>
            new LetteraCommand { Name = name, Group = group, Words = words, MenuPath = menu, Source = source, Keys = keys, Run = () => EditorApplication.ExecuteMenuItem(menu) };

        static LetteraCommand Do(string name, string group, string words, Action run, Func<bool> available = null, LetteraPaletteSource source = LetteraPaletteSource.Anywhere, string keys = null) =>
            new LetteraCommand { Name = name, Group = group, Words = words, Run = run, Available = available, Source = source, Keys = keys };

        static LetteraCommand Create(string name, string words, string menu, Action<MenuCommand> make) =>
            new LetteraCommand { Name = name, Group = "Create", Words = words, MenuPath = menu, Source = LetteraPaletteSource.Scene, Run = () => make(new MenuCommand(s_Parent)) };

        static LetteraCommand Asset(string name, string words, string menu, Func<bool> available = null) =>
            new LetteraCommand { Name = name, Group = "Assets", Words = words, MenuPath = menu, Source = LetteraPaletteSource.Project, Available = available, Run = () => EditorApplication.ExecuteMenuItem(menu) };

        static IEnumerable<GameObject> SelectedTexts() => Selection.gameObjects.Where(g => g.GetComponent<ITextComponent>() != null);

        static void AddToSelected<T>() where T : Component
        {
            foreach (GameObject go in SelectedTexts())
                if (go.GetComponent<T>() == null)
                    Undo.AddComponent<T>(go);
        }

        static string KeysOf(string shortcut)
        {
            try
            {
                string keys = ShortcutManager.instance.GetShortcutBinding(shortcut).ToString();
                return string.IsNullOrEmpty(keys) ? null : keys;
            }
            catch (ArgumentException)
            {
                return null;
            }
        }

        /// <summary>The whole list. A module's own commands join it through <see cref="ILetteraPaletteSource"/>.</summary>
        internal static List<LetteraCommand> Commands()
        {
            bool Text() => SelectedTexts().Any();
            bool Fonts() => LetteraMenus.HasFontsSelected();
            var all = new List<LetteraCommand>
            {
                Create("Text", "ui canvas label tmp textmeshpro", "GameObject/Lettera/Text", LetteraMenus.CreateText),
                Create("World Text", "3d scene mesh worldspace", "GameObject/Lettera/World Text", LetteraMenus.CreateWorldText),
                Create("Button", "ui click press", "GameObject/Lettera/Button", LetteraMenus.CreateButton),
                Create("Document", "long book scroll rope big", "GameObject/Lettera/Document", LetteraMenus.CreateDocument),
                Create("Selectable Text", "copy select", "GameObject/Lettera/Selectable Text", LetteraEditingMenus.Selectable),
                Create("Editable Text", "edit type", "GameObject/Lettera/Editable Text", LetteraEditingMenus.Editable),
                Create("Input Field", "field form login", "GameObject/Lettera/Input Field", LetteraEditingMenus.Input),
                Create("Text Area", "multiline field notes", "GameObject/Lettera/Text Area", LetteraEditingMenus.Area),
                Create("Password Field", "secret login", "GameObject/Lettera/Password Field", LetteraEditingMenus.Password),
                Create("Dropdown", "list options combo select", "GameObject/Lettera/Dropdown", LetteraEditingMenus.Dropdown),

                Do("Add Selectable", "Selected text", "copy selection", AddToSelected<LetteraSelectable>, Text, LetteraPaletteSource.Scene),
                Do("Add Editable", "Selected text", "typing field", AddToSelected<LetteraEditable>, Text, LetteraPaletteSource.Scene),
                Do("Add Sequencer", "Selected text", "cues motion timeline typewriter", AddToSelected<LetteraSequencer>, Text, LetteraPaletteSource.Scene),
                Do("Add Range Navigator", "Selected text", "links keyboard gamepad focus", AddToSelected<LetteraRangeNavigator>, Text, LetteraPaletteSource.Scene),
                Do("Add Document Scroller", "Selected text", "scroll drag inertia ticker", AddToSelected<LetteraDocumentScroller>, Text, LetteraPaletteSource.Scene),
                Do("Edit In Scene", "Selected text", "type caret write where it stands", () => LetteraSceneEditView.Begin(Selection.objects), Text, LetteraPaletteSource.Scene, "F2"),
                Do("Inspect", "Selected text", "overlay boxes baselines bounds debug probe", () => EditorApplication.ExecuteMenuItem("Tools/FirstSymbol/Lettera/Inspect Text In Scene"), Text, LetteraPaletteSource.Scene, KeysOf("Lettera/Inspect Text In Scene")),
                Do("Save As Default", "Selected text", "prefab new text project", () => LetteraMenus.SaveAsDefault(Selection.activeGameObject), Text, LetteraPaletteSource.Scene),

                Asset("Font From Selected File", "create ttf otf import", "Assets/Create/Lettera/Font Asset From Selection", Fonts),
                Asset("Color Font From Selected File", "create emoji", "Assets/Create/Lettera/Color Font Asset From Selection", Fonts),
                Asset("Font Variant", "create axes weight", "Assets/Create/Lettera/Font Variant"),
                Asset("System Font", "create os installed", "Assets/Create/Lettera/System Font Asset"),
                Asset("Stack, Each A Fallback", "create fonts from selection family", "Assets/Create/Lettera/Font Stack From Selection, Each A Fallback", LetteraMenus.HasStackFontsSelected),
                Asset("Stack, One Family", "create fonts from selection faces", "Assets/Create/Lettera/Font Stack From Selection, One Family", LetteraMenus.HasStackFontsSelected),
                Asset("Empty Font Stack", "create family fallback", "Assets/Create/Lettera/Font Stack"),
                Asset("Style Preset", "create shared list", "Assets/Create/Lettera/Style Preset"),
                Asset("Effect Preset", "create shared", "Assets/Create/Lettera/Effect Preset"),
                Asset("Paint Library", "create swatch color gradient", "Assets/Create/Lettera/Paint Library"),
                Asset("Inline Library", "create sprite emoji", "Assets/Create/Lettera/Inline Library"),
                Asset("Field Preset", "create input rules", "Assets/Create/Lettera/Field Preset"),
                Asset("Word Dictionary", "create segmentation thai", "Assets/Create/Lettera/Word Dictionary"),
                Asset("Custom Effect", "create script code", "Assets/Create/Lettera/Custom Effect"),

                Menu("Glyphs", "Window", "shaping font harfbuzz fallback debug", "Tools/FirstSymbol/Lettera/Glyphs"),
                Menu("Ranges", "Window", "styles markup tags debug problems", "Tools/FirstSymbol/Lettera/Ranges"),
                Menu("Atlases", "Window", "sdf memory pages texture", "Tools/FirstSymbol/Lettera/Atlases"),
                Menu("Clipboard", "Window", "copy paste formats html", "Tools/FirstSymbol/Lettera/Clipboard"),
                Menu("Font Tools", "Window", "create subset dictionary font asset batch", "Tools/FirstSymbol/Lettera/Font Tools"),
                Menu("Sequencer", "Window", "cues timeline rows drag split motion", "Tools/FirstSymbol/Lettera/Sequencer"),
                Menu("Inspect Text In Scene", "Window", "overlay boxes baselines bounds debug probe", "Tools/FirstSymbol/Lettera/Inspect Text In Scene", keys: KeysOf("Lettera/Inspect Text In Scene")),
                Menu("Edit Text In Scene", "Window", "type caret f2 write where it stands bar", "Tools/FirstSymbol/Lettera/Edit Text In Scene", keys: "F2"),
                Menu("Repair Saved Objects", "Window", "missing script older version lost fix", "Tools/FirstSymbol/Lettera/Repair Saved Objects"),
                Menu("Migrate From TextMesh Pro", "Window", "tmp upgrade convert", "Tools/FirstSymbol/Lettera/Migrate From TextMesh Pro"),
                Menu("Use Bundled Math Font", "Math", "formula noto", "Lettera/Math/Use Bundled Font"),
                Do("Project Settings", "Settings", "defaults preferences", () => SettingsService.OpenProjectSettings("Project/FirstSymbol/Lettera")),
                Do("Preferences", "Settings", "this user hook right click", () => SettingsService.OpenUserPreferences("Preferences/Lettera")),
                Menu("Getting Started", "Help", "guide start docs samples first new", "Tools/FirstSymbol/Lettera/Getting Started"),
                Do("Show Docs", "Help", "readme guide manual", () => { string[] found = AssetDatabase.FindAssets("README t:TextAsset", new[] { "Packages/com.firstsymbol.lettera" }); if (found.Length > 0) EditorGUIUtility.PingObject(AssetDatabase.LoadAssetAtPath<TextAsset>(AssetDatabase.GUIDToAssetPath(found[0]))); }),
            };
            foreach (string target in new[] { "Android", "iOS", "Web", "Windows", "macOS" })
                all.Add(Menu("Device Check for " + target, "Build", "kit phone test", "Lettera/Device Check/Build for " + target));
            foreach (Type type in TypeCache.GetTypesDerivedFrom<ILetteraPaletteSource>())
            {
                if (type.IsAbstract) continue;
                all.AddRange(((ILetteraPaletteSource)Activator.CreateInstance(type)).Commands());
            }
            return all;
        }
    }

    /// <summary>A way for a module, such as solid text and the migrator, to add to the palette without the palette naming it.</summary>
    internal interface ILetteraPaletteSource
    {
        IEnumerable<LetteraCommand> Commands();
    }

    /// <summary>Shift and a right click in the Hierarchy, the Scene view and the Project window opens the palette there. A preference turns it off, and the key stays the sure way.</summary>
    [InitializeOnLoad]
    internal static class LetteraPaletteHooks
    {
        const string EnabledKey = "Lettera.Palette.ShiftRightClick";
        static bool s_SwallowContext;

        internal static bool Enabled
        {
            get => LtPrefs.GetBool(EnabledKey, true);
            set => LtPrefs.SetBool(EnabledKey, value);
        }

        static LetteraPaletteHooks()
        {
            EditorApplication.hierarchyWindowItemOnGUI += OnHierarchyItem;
            EditorApplication.projectWindowItemOnGUI += OnProjectItem;
            SceneView.duringSceneGui += OnScene;
        }

        /// <summary>Whether this event is the press that opens the palette.</summary>
        internal static bool IsTrigger(Event e) => e != null && IsTrigger(e.type, e.button, e.modifiers);

        internal static bool IsTrigger(EventType type, int button, EventModifiers keys)
        {
            const EventModifiers Held = EventModifiers.Shift | EventModifiers.Alt | EventModifiers.Control | EventModifiers.Command;
            return type == EventType.MouseDown && button == 1 && (keys & Held) == EventModifiers.Shift;
        }

        static void OnHierarchyItem(int id, Rect row)
        {
            Event e = Event.current;
            if (!Enabled || e == null || !row.Contains(e.mousePosition)) return;
            Handle(e, LetteraPaletteSource.Scene, EditorUtility.EntityIdToObject(id) as GameObject);
        }

        static void OnProjectItem(string guid, Rect row)
        {
            Event e = Event.current;
            if (!Enabled || e == null || !row.Contains(e.mousePosition)) return;
            Handle(e, LetteraPaletteSource.Project, null);
        }

        static void OnScene(SceneView view)
        {
            Event e = Event.current;
            if (!Enabled || e == null) return;
            Handle(e, LetteraPaletteSource.Scene, Selection.activeGameObject);
        }

        // The menu Unity would open comes as its own event after the press, so that one goes too.
        static void Handle(Event e, LetteraPaletteSource source, GameObject parent)
        {
            if (s_SwallowContext && (e.type == EventType.ContextClick || (e.type == EventType.MouseUp && e.button == 1)))
            {
                if (e.type == EventType.ContextClick) s_SwallowContext = false;
                e.Use();
                return;
            }
            if (!IsTrigger(e)) return;
            s_SwallowContext = true;
            Vector2 screen = GUIUtility.GUIToScreenPoint(e.mousePosition);
            e.Use();
            EditorApplication.delayCall += () => LetteraPalette.Open(source, parent, screen);
        }

        [SettingsProvider]
        static SettingsProvider Preferences() => new SettingsProvider("Preferences/Lettera", SettingsScope.User)
        {
            label = "Lettera",
            keywords = new HashSet<string>(new[] { "Lettera", "Palette", "Commands", "Right Click", "Shift" }),
            activateHandler = (_, root) =>
            {
                VisualElement page = LtUI.Root();
                page.Add(LtUI.Card("Commands",
                    LtUI.Row("Shift + Right Click", LtUI.El("lt-switch-holder", LtUI.Toggle(() => Enabled, value => Enabled = value)), "Opens the commands from the Hierarchy, the Scene view and the Project window. Off leaves Unity's own menu alone. Ctrl+Alt+L works either way."),
                    LtUI.Row("Row Controls", LtUI.El("lt-switch-holder", LtUI.Toggle(() => !LtList.ControlsHidden, value => LtList.ControlsHidden = !value)), "Remove and the like on the rows of Lettera's lists. Alt+R over a list does the same.")));
                root.Add(page);
            },
        };
    }
}

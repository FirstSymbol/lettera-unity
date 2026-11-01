using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.ShortcutManagement;
using UnityEngine;
using UnityEngine.UIElements;

namespace FirstSymbol.Lettera.Editor
{
    /// <summary>One thing the palette can do.</summary>
    internal sealed class LetteraCommand
    {
        public string Name, Group, Words;
        public Action Run;
        public Func<bool> Available;
    }

    /// <summary>
    /// Everything Lettera can make or open, by typing. Ctrl+Alt+L, in whichever layout the keyboard is in: ищдв finds Bold.
    /// What was used last comes first.
    /// </summary>
    internal sealed class LetteraPalette : EditorWindow
    {
        const string RecentKey = "Lettera.Palette.Recent";
        const int MaxShown = 60;

        readonly List<LetteraCommand> m_Shown = new List<LetteraCommand>();
        List<LetteraCommand> m_All;
        ScrollView m_List;
        LtSearch m_Search;
        int m_At;

        [Shortcut("Lettera/Commands", KeyCode.L, ShortcutModifiers.Action | ShortcutModifiers.Alt)]
        [MenuItem("Tools/Lettera/Commands...", false, 0)]
        internal static void Open()
        {
            var window = CreateInstance<LetteraPalette>();
            window.titleContent = new GUIContent("Lettera");
            Rect main = EditorGUIUtility.GetMainWindowPosition();
            var size = new Vector2(440, 400);
            window.ShowAsDropDown(new Rect(main.center.x - size.x / 2, main.y + 90, 1, 1), size);
        }

        void CreateGUI()
        {
            m_All = Commands();
            VisualElement root = LtUI.Root();
            root.AddToClassList("lt-popup");
            root.style.flexGrow = 1;
            m_Search = new LtSearch("Type what you need, in any layout");
            m_Search.Changed += Refresh;
            root.Add(m_Search);
            m_List = new ScrollView();
            m_List.style.flexGrow = 1;
            root.Add(m_List);
            root.RegisterCallback<KeyDownEvent>(OnKey, TrickleDown.TrickleDown);
            rootVisualElement.Add(root);
            Refresh("");
            m_Search.Field.schedule.Execute(() => m_Search.Field.Focus());
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
                default:
                    return;
            }
            e.StopPropagation();
        }

        void Move(int delta)
        {
            if (m_Shown.Count == 0) return;
            m_At = Mathf.Clamp(m_At + delta, 0, m_Shown.Count - 1);
            Paint();
        }

        void Refresh(string query)
        {
            m_Shown.Clear();
            m_Shown.AddRange(Find(m_All, query, Recent()).Take(MaxShown));
            m_At = 0;
            m_List.Clear();
            foreach (LetteraCommand command in m_Shown)
            {
                VisualElement row = LtUI.El("lt-palette__row");
                row.Add(LtUI.Text(command.Name, "lt-palette__name"));
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
            if (m_At < m_Shown.Count && m_Shown.Count > 0) m_List.ScrollTo(m_List[m_At]);
        }

        void Execute(LetteraCommand command)
        {
            Remember(command.Name);
            Close();
            command.Run();
        }

        /// <summary>Best first; with nothing typed, what was used last.</summary>
        internal static IEnumerable<LetteraCommand> Find(List<LetteraCommand> all, string query, List<string> recent)
        {
            var ranked = new List<(LetteraCommand command, int score)>();
            foreach (LetteraCommand command in all)
            {
                if (command.Available != null && !command.Available()) continue;
                int score = Math.Max(LtSearchText.Score(query, command.Name) * 2, LtSearchText.Score(query, command.Words));
                if (score == 0) continue;
                int place = recent.IndexOf(command.Name);
                if (place >= 0) score += 20 - Math.Min(place, 19);
                ranked.Add((command, score));
            }
            return ranked.OrderByDescending(r => r.score).ThenBy(r => r.command.Name, StringComparer.Ordinal).Select(r => r.command);
        }

        static List<string> Recent() => EditorPrefs.GetString(RecentKey, "").Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries).ToList();

        static void Remember(string name)
        {
            List<string> recent = Recent();
            recent.Remove(name);
            recent.Insert(0, name);
            if (recent.Count > 12) recent.RemoveRange(12, recent.Count - 12);
            EditorPrefs.SetString(RecentKey, string.Join("\n", recent));
        }

        static bool MenuExists(string path) => true;

        static LetteraCommand Menu(string name, string group, string words, string menu) =>
            new LetteraCommand { Name = name, Group = group, Words = words, Run = () => EditorApplication.ExecuteMenuItem(menu) };

        static LetteraCommand Do(string name, string group, string words, Action run, Func<bool> available = null) =>
            new LetteraCommand { Name = name, Group = group, Words = words, Run = run, Available = available };

        static IEnumerable<GameObject> SelectedTexts() => Selection.gameObjects.Where(g => g.GetComponent<LetteraText>() != null || g.GetComponent<LetteraWorldText>() != null);

        static void AddToSelected<T>(string undo) where T : Component
        {
            foreach (GameObject go in SelectedTexts())
                if (go.GetComponent<T>() == null)
                    Undo.AddComponent<T>(go);
        }

        /// <summary>The whole list. A project's own commands could join it here.</summary>
        internal static List<LetteraCommand> Commands()
        {
            bool Text() => SelectedTexts().Any();
            var all = new List<LetteraCommand>
            {
                Menu("Text", "Create", "ui canvas label tmp textmeshpro", "GameObject/Lettera/Text"),
                Menu("World Text", "Create", "3d scene mesh worldspace", "GameObject/Lettera/World Text"),
                Menu("Selectable Text", "Create", "copy select", "GameObject/Lettera/Selectable Text"),
                Menu("Editable Text", "Create", "edit type", "GameObject/Lettera/Editable Text"),
                Menu("Input Field", "Create", "field form login", "GameObject/Lettera/Input Field"),
                Menu("Text Area", "Create", "multiline field notes", "GameObject/Lettera/Text Area"),
                Menu("Password Field", "Create", "secret login", "GameObject/Lettera/Password Field"),
                Menu("Dropdown", "Create", "list options combo select", "GameObject/Lettera/Dropdown"),
                Do("Add Selectable", "Selected text", "copy selection", () => AddToSelected<LetteraSelectable>("Add Selectable"), Text),
                Do("Add Editable", "Selected text", "typing field", () => AddToSelected<LetteraEditable>("Add Editable"), Text),
                Menu("Glyphs", "Window", "shaping font harfbuzz fallback debug", "Tools/Lettera/Glyphs"),
                Menu("Ranges", "Window", "styles markup tags debug problems", "Tools/Lettera/Ranges"),
                Menu("Atlases", "Window", "sdf memory pages texture", "Tools/Lettera/Atlases"),
                Menu("Clipboard", "Window", "copy paste formats html", "Tools/Lettera/Clipboard"),
                Menu("Font Tools", "Window", "create subset dictionary font asset batch", "Tools/Lettera/Font Tools"),
                Menu("Sequencer", "Window", "cues timeline rows drag split motion", "Tools/Lettera/Sequencer"),
                Menu("Inspect Text In Scene", "Window", "overlay boxes baselines bounds debug probe", "Tools/Lettera/Inspect Text In Scene"),
                Menu("Use Bundled Math Font", "Math", "formula noto", "Lettera/Math/Use Bundled Font"),
                Do("Project Settings", "Settings", "defaults preferences", () => SettingsService.OpenProjectSettings("Project/Lettera")),
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
}

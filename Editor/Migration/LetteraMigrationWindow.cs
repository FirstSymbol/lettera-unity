using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace FirstSymbol.Lettera.Editor.Migration
{
    /// <summary>Tools > Lettera > Migrate From TextMesh Pro. Scans when asked, and changes only what the button says.</summary>
    internal sealed class LetteraMigrationWindow : EditorWindow
    {
        const int MostRows = 200;

        [MenuItem("Tools/Lettera/Migrate From TextMesh Pro", false, 40)]
        static void Open() => GetWindow<LetteraMigrationWindow>("Lettera Migration").minSize = new Vector2(360, 400);

        LetteraMigrationRun m_Run;
        VisualElement m_Page, m_Tree, m_Findings, m_Excluded, m_Losses, m_Log;
        Label m_Overview, m_Narrowed;
        // The folder the findings are narrowed to, empty for all; and the folders opened in the tree, for this window's life.
        string m_Folder = "";
        readonly HashSet<string> m_OpenFolders = new HashSet<string>();
        bool m_TreeTouched;

        internal LetteraMigrationRun Run => m_Run ?? (m_Run = new LetteraMigrationRun());

        internal string Folder
        {
            get => m_Folder;
            set
            {
                m_Folder = value ?? "";
                Refresh();
            }
        }

        /// <summary>For a test: a run whose exclusions live somewhere harmless.</summary>
        internal void UseRun(LetteraMigrationRun run)
        {
            m_Run = run;
            if (m_Page != null) Refresh();
        }

        void CreateGUI()
        {
            VisualElement root = LtUI.Root();
            root.style.flexGrow = 1;
            var scroll = new ScrollView();
            scroll.style.flexGrow = 1;
            root.Add(scroll);
            rootVisualElement.Add(root);
            m_Page = LtFrame.Header(scroll.contentContainer, "Migrate From TextMesh Pro", "Nothing happens until you press a button");
            m_Overview = LtUI.Text("Scanning reads the project and changes nothing. Version control is the real undo: commit first.", "lt-hint");
            m_Overview.style.whiteSpace = WhiteSpace.Normal;
            m_Page.Add(LtUI.Card("Overview", m_Overview,
                new LtFix("Asset Serialization must be Force Text, so that scenes and prefabs can be read.", () => !LetteraMigrationRun.ForceText, "Open The Setting", () => SettingsService.OpenProjectSettings("Project/Editor")),
                LtUI.Clickable("Scan", "lt-add", "Reads every scene, prefab, script and font in Assets, but for what is excluded.", Scan),
                LtUI.Clickable("1. Create Fonts", "lt-add lt-add--small", "Each TextMesh Pro font gets a Lettera font beside its source file. Excluded fonts are left alone.", () => Step(() => Run.MigrateFonts())),
                LtUI.Clickable("2. Convert Scenes And Prefabs", "lt-add lt-add--small", "Rewrites their files. An open scene with changes asks first. A component keeps its identity, so references stay. Excluded files are left alone.", () => Step(() => Run.MigrateComponents())),
                LtUI.Clickable("3. Rewrite Scripts", "lt-add lt-add--small", "Rewrites the C# that uses TextMesh Pro, with a backup in Library/Lettera/ScriptBackups. Excluded files are left alone.", () => Step(() => Run.ApplyScripts())),
                LtUI.Clickable("Copy The Report", "lt-add lt-add--small", "Everything found, done and not carried, as text.", () => EditorGUIUtility.systemCopyBuffer = Run.Report())));
            m_Tree = LtUI.El("lt-tree");
            m_Page.Add(LtUI.Card("Folders", LtUI.Text("Click a folder to see only what is in it, and again to see all. Its right click excludes it.", "lt-hint"), m_Tree));
            m_Narrowed = LtUI.Text("", "lt-hint");
            m_Findings = LtUI.El("lt-findings");
            m_Page.Add(LtUI.Card("Findings", m_Narrowed, m_Findings));
            m_Excluded = LtUI.El("lt-excluded");
            m_Page.Add(LtUI.Card("Excluded", LtUI.Text("No stage touches these, and a scan does not go into an excluded folder. Kept in " + LetteraMigrationExclusions.DefaultFile + ", which is meant to be committed.", "lt-hint"), m_Excluded));
            m_Losses = LtUI.El("lt-losses");
            m_Page.Add(LtUI.Card("Not Carried", m_Losses));
            m_Log = LtUI.El("lt-log");
            m_Page.Add(LtUI.Card("Log", m_Log));
            Refresh();
        }

        void Scan()
        {
            if (!LetteraMigrationRun.ForceText) return;
            Run.Scan();
            Refresh();
        }

        void Step(Action action)
        {
            if (Run.Findings.Count == 0) Run.Scan();
            action();
            Refresh();
        }

        void Refresh()
        {
            if (m_Page == null) return;
            LetteraMigrationRun run = Run;
            if (m_Folder.Length > 0 && !run.Findings.Any(f => LetteraFolderTree.InFolder(f.Path, m_Folder))) m_Folder = "";
            ShowTree(run);
            ShowFindings(run);
            ShowExcluded(run);
            m_Losses.Clear();
            m_Log.Clear();
            foreach (string loss in run.Losses.Take(MostRows)) m_Losses.Add(LtUI.Text(loss, "lt-hint"));
            if (run.Losses.Count == 0) m_Losses.Add(LtUI.Text("Nothing yet.", "lt-hint"));
            foreach (string line in run.Log.Skip(Math.Max(0, run.Log.Count - 100))) m_Log.Add(LtUI.Text(line, "lt-hint"));
            int excluded = run.Findings.Count(f => f.Excluded);
            m_Overview.text = run.Findings.Count == 0
                ? "Scanning reads the project and changes nothing. Version control is the real undo: commit first."
                : $"{run.Findings.Count} files hold something of TextMesh Pro{(excluded > 0 ? $", {excluded} excluded" : "")}. {run.Losses.Count} things not carried so far.";
        }

        void ShowTree(LetteraMigrationRun run)
        {
            m_Tree.Clear();
            if (run.Findings.Count == 0)
            {
                m_Tree.Add(LtUI.Text("The folders that hold findings show here after a scan.", "lt-hint"));
                return;
            }
            LetteraFolderTree tree = LetteraFolderTree.Build(run.Findings.Select(f => f.Path));
            if (!m_TreeTouched)
            {
                // Opened down to where folders start to branch, so that the first look shows something to choose.
                foreach (LetteraFolderTree.Node root in tree.Roots)
                {
                    LetteraFolderTree.Node node = root;
                    m_OpenFolders.Add(node.Path);
                    while (node.Children.Count == 1)
                    {
                        node = node.Children[0];
                        m_OpenFolders.Add(node.Path);
                    }
                }
            }
            m_Tree.Add(TreeRow("All Findings", "", run.Findings.Count, 0, false, false));
            foreach (LetteraFolderTree.Node node in tree.Roots) AddNode(node);
        }

        void AddNode(LetteraFolderTree.Node node)
        {
            bool open = m_OpenFolders.Contains(node.Path);
            m_Tree.Add(TreeRow(node.Name, node.Path, node.Count, node.Depth, node.Children.Count > 0, open));
            if (!open) return;
            foreach (LetteraFolderTree.Node child in node.Children) AddNode(child);
        }

        VisualElement TreeRow(string name, string path, int count, int depth, bool hasChildren, bool open)
        {
            VisualElement row = LtUI.El("lt-tree__row");
            row.name = "lt-tree-" + (path.Length == 0 ? "all" : path);
            row.tooltip = path.Length == 0 ? "Every finding." : path;
            row.style.paddingLeft = 4 + 12 * depth;
            row.EnableInClassList("lt-tree__row--on", path == m_Folder);
            Label chevron = LtUI.Text(hasChildren ? (open ? "▾" : "▸") : "", "lt-tree__chevron");
            if (hasChildren)
            {
                chevron.RegisterCallback<ClickEvent>(e =>
                {
                    e.StopPropagation();
                    m_TreeTouched = true;
                    if (!m_OpenFolders.Remove(path)) m_OpenFolders.Add(path);
                    Refresh();
                });
            }
            row.Add(chevron);
            row.Add(LtUI.Text(name, "lt-tree__name"));
            row.Add(LtUI.Text(count.ToString(), "lt-chip"));
            row.RegisterCallback<ClickEvent>(_ => Folder = path == m_Folder ? "" : path);
            if (path.Length > 0)
            {
                row.AddManipulator(new ContextualMenuManipulator(e =>
                {
                    e.menu.AppendAction("Show Only This Folder", _ => Folder = path);
                    e.menu.AppendAction("Exclude This Folder", _ =>
                    {
                        Run.ExcludeFolder(path);
                        Refresh();
                    });
                    e.menu.AppendAction("Show In Project", _ => EditorGUIUtility.PingObject(AssetDatabase.LoadMainAssetAtPath(path)), AssetDatabase.IsValidFolder(path) ? DropdownMenuAction.Status.Normal : DropdownMenuAction.Status.Disabled);
                }));
            }
            return row;
        }

        void ShowFindings(LetteraMigrationRun run)
        {
            m_Findings.Clear();
            List<LetteraTmpFinding> shown = run.Findings.Where(f => LetteraFolderTree.InFolder(f.Path, m_Folder)).ToList();
            m_Narrowed.text = m_Folder.Length == 0 ? "" : $"{shown.Count} of {run.Findings.Count}, in {m_Folder}.";
            m_Narrowed.style.display = m_Folder.Length == 0 ? DisplayStyle.None : DisplayStyle.Flex;
            int count = 0;
            foreach (LetteraTmpFinding finding in shown)
            {
                if (++count > MostRows)
                {
                    m_Findings.Add(LtUI.Text($"And {shown.Count - MostRows} more, in the report. A folder narrows the list.", "lt-hint"));
                    break;
                }
                m_Findings.Add(FindingRow(run, finding));
            }
            if (run.Findings.Count == 0) m_Findings.Add(LtUI.Text("Press Scan.", "lt-hint"));
        }

        VisualElement FindingRow(LetteraMigrationRun run, LetteraTmpFinding finding)
        {
            var row = LtUI.El("lt-range lt-finding");
            row.EnableInClassList("lt-finding--excluded", finding.Excluded);
            VisualElement head = LtUI.El("lt-finding__head");
            head.Add(LtUI.Text($"{finding.Status}  {finding.Kind}  {finding.Path}", "lt-row__value lt-finding__path"));
            bool own = run.Exclusions.Contains(finding.Path);
            if (!finding.Excluded)
            {
                head.Add(LtUI.Clickable("Exclude", "lt-icon-btn lt-icon-btn--text", "Leaves this file out of every stage, now and in later scans, for everyone who shares the project.", () =>
                {
                    run.Exclude(finding);
                    Refresh();
                }));
            }
            else if (own)
            {
                head.Add(LtUI.Clickable("Include", "lt-icon-btn lt-icon-btn--text", "Takes this file back into the run.", () =>
                {
                    run.Include(finding.Path);
                    Refresh();
                }));
            }
            row.Add(head);
            string counts = finding.Texts + finding.Fields + finding.Helpers > 0 ? $"{finding.Texts} texts, {finding.Fields} fields, {finding.Helpers} helpers" : "";
            string note = finding.Excluded && !own ? "In an excluded folder." : finding.Note;
            if (counts.Length > 0 || note != null) row.Add(LtUI.Text(counts + (note != null ? (counts.Length > 0 ? " " : "") + note : ""), "lt-hint"));
            string path = finding.Path;
            row.tooltip = path;
            row.RegisterCallback<ClickEvent>(_ => EditorGUIUtility.PingObject(AssetDatabase.LoadMainAssetAtPath(path)));
            return row;
        }

        void ShowExcluded(LetteraMigrationRun run)
        {
            m_Excluded.Clear();
            IReadOnlyList<string> paths = run.Exclusions.Paths;
            if (paths.Count == 0)
            {
                m_Excluded.Add(LtUI.Text("Nothing is excluded. A finding's Exclude leaves out one file, and a folder's right click a whole folder.", "lt-hint"));
                return;
            }
            foreach (string path in paths.ToList())
            {
                VisualElement head = LtUI.El("lt-finding__head lt-range");
                head.Add(LtUI.Text(path, "lt-row__value lt-finding__path"));
                head.tooltip = path;
                head.Add(LtUI.Clickable("Include", "lt-icon-btn lt-icon-btn--text", "Takes it back into the run. A folder's files come with the next scan.", () =>
                {
                    run.Include(path);
                    Refresh();
                }));
                m_Excluded.Add(head);
            }
        }
    }
}

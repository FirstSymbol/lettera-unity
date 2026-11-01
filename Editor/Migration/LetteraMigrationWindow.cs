using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UIElements;

namespace FirstSymbol.Lettera.Editor.Migration
{
    /// <summary>Tools > Lettera > Migrate From TextMesh Pro. Scans when asked, and changes only what the button says.</summary>
    internal sealed class LetteraMigrationWindow : EditorWindow
    {
        [MenuItem("Tools/Lettera/Migrate From TextMesh Pro", false, 40)]
        static void Open() => GetWindow<LetteraMigrationWindow>("Lettera Migration").minSize = new Vector2(360, 400);

        readonly LetteraMigrationRun m_Run = new LetteraMigrationRun();
        VisualElement m_Page, m_Findings, m_Losses, m_Log;
        Label m_Overview;

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
                LtUI.Clickable("Scan", "lt-add", "Reads every scene, prefab, script and font in Assets.", Scan),
                LtUI.Clickable("1. Create Fonts", "lt-add lt-add--small", "Each TextMesh Pro font gets a Lettera font beside its source file.", () => Step(() => m_Run.MigrateFonts())),
                LtUI.Clickable("2. Convert Scenes And Prefabs", "lt-add lt-add--small", "Rewrites their files. An open scene with changes asks first. A component keeps its identity, so references stay.", () => Step(() => m_Run.MigrateComponents())),
                LtUI.Clickable("3. Rewrite Scripts", "lt-add lt-add--small", "Rewrites the C# that uses TextMesh Pro, with a backup in Library/Lettera/ScriptBackups.", () => Step(() => m_Run.ApplyScripts())),
                LtUI.Clickable("Copy The Report", "lt-add lt-add--small", "Everything found, done and not carried, as text.", () => EditorGUIUtility.systemCopyBuffer = m_Run.Report())));
            m_Findings = LtUI.El("lt-findings");
            m_Page.Add(LtUI.Card("Findings", m_Findings));
            m_Losses = LtUI.El("lt-losses");
            m_Page.Add(LtUI.Card("Not Carried", m_Losses));
            m_Log = LtUI.El("lt-log");
            m_Page.Add(LtUI.Card("Log", m_Log));
            Show();
        }

        void Scan()
        {
            if (!LetteraMigrationRun.ForceText) return;
            m_Run.Scan();
            Show();
        }

        void Step(Action action)
        {
            if (m_Run.Findings.Count == 0) m_Run.Scan();
            action();
            Show();
        }

        void Show()
        {
            m_Findings.Clear();
            m_Losses.Clear();
            m_Log.Clear();
            int shown = 0;
            foreach (LetteraTmpFinding finding in m_Run.Findings)
            {
                if (++shown > 200)
                {
                    m_Findings.Add(LtUI.Text($"And {m_Run.Findings.Count - 200} more, in the report.", "lt-hint"));
                    break;
                }
                var row = LtUI.El("lt-range");
                row.Add(LtUI.Text($"{finding.Status}  {finding.Kind}  {finding.Path}", "lt-row__value"));
                string counts = finding.Texts + finding.Fields + finding.Helpers > 0 ? $"{finding.Texts} texts, {finding.Fields} fields, {finding.Helpers} helpers" : "";
                if (counts.Length > 0 || finding.Note != null) row.Add(LtUI.Text(counts + (finding.Note != null ? " " + finding.Note : ""), "lt-hint"));
                string path = finding.Path;
                row.RegisterCallback<ClickEvent>(_ => EditorGUIUtility.PingObject(AssetDatabase.LoadMainAssetAtPath(path)));
                m_Findings.Add(row);
            }
            if (m_Run.Findings.Count == 0) m_Findings.Add(LtUI.Text("Press Scan.", "lt-hint"));
            foreach (string loss in m_Run.Losses.Take(200)) m_Losses.Add(LtUI.Text(loss, "lt-hint"));
            if (m_Run.Losses.Count == 0) m_Losses.Add(LtUI.Text("Nothing yet.", "lt-hint"));
            foreach (string line in m_Run.Log.Skip(Math.Max(0, m_Run.Log.Count - 100))) m_Log.Add(LtUI.Text(line, "lt-hint"));
            m_Overview.text = m_Run.Findings.Count == 0 ? "Scanning reads the project and changes nothing. Version control is the real undo: commit first." : $"{m_Run.Findings.Count} files hold something of TextMesh Pro. {m_Run.Losses.Count} things not carried so far.";
        }
    }
}

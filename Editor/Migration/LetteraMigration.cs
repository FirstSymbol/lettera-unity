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
    /// <summary>What a scan found in one file.</summary>
    [Serializable]
    public class LetteraTmpFinding
    {
        public enum FileKind { Scene, Prefab, Script, Definition, Font, Material, Other }

        public string Path;
        public FileKind Kind;
        public int Texts, Fields, Helpers, Overrides;
        public string Status = "Pending";
        public string Note;
    }

    /// <summary>A pass over a project: scanning, then each stage. Nothing runs by itself, nothing deletes, and git is the undo.</summary>
    public sealed class LetteraMigrationRun
    {
        const string FontAssetGuid = "71c1514a6bd24e1e882cebbe1904ce04";
        static readonly string[] Markers = { LetteraTmpYaml.GuidCanvas, LetteraTmpYaml.GuidWorld, LetteraTmpYaml.GuidInput, LetteraTmpYaml.GuidDropdown, LetteraTmpYaml.GuidSubMeshUi, LetteraTmpYaml.GuidSubMesh };

        public readonly List<LetteraTmpFinding> Findings = new List<LetteraTmpFinding>();
        public readonly List<string> Losses = new List<string>();
        public readonly List<string> Log = new List<string>();
        public readonly Dictionary<string, string> FontMap = new Dictionary<string, string>();

        public static bool ForceText => EditorSettings.serializationMode == SerializationMode.ForceText;

        static IEnumerable<string> Files(string root)
        {
            var pending = new Stack<string>();
            pending.Push(root);
            while (pending.Count > 0)
            {
                string folder = pending.Pop();
                foreach (string sub in Directory.GetDirectories(folder))
                {
                    string name = Path.GetFileName(sub);
                    if (name.StartsWith(".") || name.EndsWith("~") || name == "Library" || name == "Temp" || name == "Logs" || name == "obj") continue;
                    if (name == "Lettera" && folder.Replace('\\', '/').EndsWith("Plugins")) continue;
                    if (name == "TextMesh Pro" && File.Exists(Path.Combine(sub, "Resources", "TMP Settings.asset"))) continue;
                    pending.Push(sub);
                }
                foreach (string file in Directory.GetFiles(folder)) yield return file.Replace('\\', '/');
            }
        }

        static int Count(string text, string what)
        {
            int count = 0, at = 0;
            while ((at = text.IndexOf(what, at, StringComparison.Ordinal)) >= 0)
            {
                count++;
                at += what.Length;
            }
            return count;
        }

        /// <summary>Reads the project, and changes nothing.</summary>
        public void Scan(string root = "Assets")
        {
            Findings.Clear();
            foreach (string file in Files(root))
            {
                string extension = Path.GetExtension(file).ToLowerInvariant();
                LetteraTmpFinding found = null;
                try
                {
                    if (extension == ".unity" || extension == ".prefab")
                    {
                        string text = File.ReadAllText(file);
                        if (!text.StartsWith("%YAML", StringComparison.Ordinal))
                        {
                            Findings.Add(new LetteraTmpFinding { Path = file, Kind = LetteraTmpFinding.FileKind.Other, Status = "Unreadable", Note = "Not text: set Asset Serialization to Force Text." });
                            continue;
                        }
                        int texts = Count(text, LetteraTmpYaml.GuidCanvas) + Count(text, LetteraTmpYaml.GuidWorld);
                        int fields = Count(text, LetteraTmpYaml.GuidInput) + Count(text, LetteraTmpYaml.GuidDropdown);
                        int helpers = Count(text, LetteraTmpYaml.GuidSubMesh) + Count(text, LetteraTmpYaml.GuidSubMeshUi);
                        int overrides = Count(text, "propertyPath: m_text") + Count(text, "propertyPath: m_fontSize") + Count(text, "propertyPath: m_fontColor");
                        if (texts + fields + helpers + overrides > 0)
                            found = new LetteraTmpFinding { Path = file, Kind = extension == ".unity" ? LetteraTmpFinding.FileKind.Scene : LetteraTmpFinding.FileKind.Prefab, Texts = texts, Fields = fields, Helpers = helpers, Overrides = overrides };
                    }
                    else if (extension == ".cs")
                    {
                        string text = File.ReadAllText(file);
                        if (text.IndexOf("TMPro", StringComparison.Ordinal) >= 0 || text.IndexOf("TMP_", StringComparison.Ordinal) >= 0 || text.IndexOf("TextMeshPro", StringComparison.Ordinal) >= 0)
                            found = new LetteraTmpFinding { Path = file, Kind = LetteraTmpFinding.FileKind.Script };
                    }
                    else if (extension == ".asmdef")
                    {
                        string text = File.ReadAllText(file);
                        if (text.IndexOf("Unity.TextMeshPro", StringComparison.Ordinal) >= 0)
                            found = new LetteraTmpFinding { Path = file, Kind = LetteraTmpFinding.FileKind.Definition };
                    }
                    else if (extension == ".asset")
                    {
                        // Only the head: a font asset's atlas tables are long, and its script is near the top.
                        using (var reader = new StreamReader(file))
                        {
                            var head = new char[4096];
                            int read = reader.Read(head, 0, head.Length);
                            if (new string(head, 0, read).IndexOf(FontAssetGuid, StringComparison.Ordinal) >= 0)
                                found = new LetteraTmpFinding { Path = file, Kind = LetteraTmpFinding.FileKind.Font };
                        }
                    }
                }
                catch (IOException e)
                {
                    found = new LetteraTmpFinding { Path = file, Kind = LetteraTmpFinding.FileKind.Other, Status = "Unreadable", Note = e.Message };
                }
                if (found != null) Findings.Add(found);
            }
            Log.Add($"Scanned: {Findings.Count} files hold something of TextMesh Pro.");
        }

        static string SourceFontOf(string assetPath)
        {
            string text = File.ReadAllText(assetPath);
            foreach (string key in new[] { "m_SourceFontFile_EditorRef", "m_SourceFontFile", "sourceFontFile" })
            {
                string reference = LetteraTmpYaml.Field(text, key);
                if (reference == null) continue;
                int at = reference.IndexOf("guid: ", StringComparison.Ordinal);
                if (at < 0) continue;
                string guid = new string(reference.Skip(at + 6).TakeWhile(char.IsLetterOrDigit).ToArray());
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!string.IsNullOrEmpty(path)) return path;
            }
            string fileGuid = LetteraTmpYaml.Field(text, "m_SourceFontFileGUID");
            if (!string.IsNullOrEmpty(fileGuid))
            {
                string path = AssetDatabase.GUIDToAssetPath(fileGuid);
                if (!string.IsNullOrEmpty(path)) return path;
            }
            return null;
        }

        /// <summary>Stage one. Each TextMesh Pro font gets a Lettera font beside its source file. Nothing is deleted.</summary>
        public int MigrateFonts()
        {
            int made = 0;
            foreach (LetteraTmpFinding finding in Findings.Where(f => f.Kind == LetteraTmpFinding.FileKind.Font))
            {
                string source = SourceFontOf(finding.Path);
                string guid = AssetDatabase.AssetPathToGUID(finding.Path);
                if (source == null)
                {
                    finding.Status = "Needs You";
                    finding.Note = "Its source font file was not found. Texts using it take the project's default.";
                    Losses.Add(finding.Path + ": " + finding.Note);
                    continue;
                }
                string target = Path.ChangeExtension(source, null) + " Lettera.asset";
                var existing = AssetDatabase.LoadAssetAtPath<LetteraFont>(target);
                if (existing == null)
                {
                    if (source.StartsWith("Packages/"))
                    {
                        Directory.CreateDirectory("Assets/Lettera/Fonts");
                        target = "Assets/Lettera/Fonts/" + Path.GetFileNameWithoutExtension(source) + " Lettera.asset";
                        existing = AssetDatabase.LoadAssetAtPath<LetteraFont>(target);
                    }
                }
                if (existing == null)
                {
                    existing = ScriptableObject.CreateInstance<LetteraFont>();
                    existing.SetFontData(File.ReadAllBytes(source));
                    AssetDatabase.CreateAsset(existing, target);
                    made++;
                    Log.Add("Font: " + target);
                }
                FontMap[guid] = AssetDatabase.AssetPathToGUID(target);
                finding.Status = "Done";
            }
            AssetDatabase.SaveAssets();
            return made;
        }

        static string GuidOfScript(string name)
        {
            foreach (string guid in AssetDatabase.FindAssets(name + " t:MonoScript"))
                if (Path.GetFileNameWithoutExtension(AssetDatabase.GUIDToAssetPath(guid)) == name)
                    return guid;
            return null;
        }

        /// <summary>Stage two. Rewrites the files of scenes and prefabs. An open scene with changes is asked about first, and Cancel stops the pass.</summary>
        public bool MigrateComponents(bool ask = true)
        {
            var yaml = new LetteraTmpYaml { LetteraCanvasGuid = GuidOfScript("LetteraText"), LetteraWorldGuid = GuidOfScript("LetteraWorldText") };
            foreach (KeyValuePair<string, string> pair in FontMap) yaml.FontMap[pair.Key] = pair.Value;
            if (ask && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                Log.Add("Stopped: a scene with changes was not settled.");
                return false;
            }
            var reopen = new List<string>();
            for (int i = 0; i < UnityEngine.SceneManagement.SceneManager.sceneCount; i++)
            {
                string path = UnityEngine.SceneManagement.SceneManager.GetSceneAt(i).path;
                if (!string.IsNullOrEmpty(path)) reopen.Add(path);
            }
            bool touchedOpen = false;
            AssetDatabase.StartAssetEditing();
            try
            {
                foreach (LetteraTmpFinding finding in Findings.Where(f => f.Kind == LetteraTmpFinding.FileKind.Prefab || f.Kind == LetteraTmpFinding.FileKind.Scene))
                {
                    int lossesBefore = yaml.Losses.Count;
                    string text = File.ReadAllText(finding.Path);
                    string done = yaml.Convert(text, finding.Path);
                    if (done != text)
                    {
                        File.WriteAllText(finding.Path, done, new UTF8Encoding(false));
                        Log.Add("Converted: " + finding.Path);
                        touchedOpen |= reopen.Contains(finding.Path);
                    }
                    finding.Status = finding.Fields > 0 ? "Fields Remain" : "Done";
                    if (finding.Fields > 0) finding.Note = finding.Fields + " input fields or dropdowns stay on TextMesh Pro, with their texts.";
                }
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
            }
            Losses.AddRange(yaml.Losses);
            AssetDatabase.Refresh();
            if (touchedOpen)
            {
                EditorSceneManager.OpenScene(reopen[0], OpenSceneMode.Single);
                for (int i = 1; i < reopen.Count; i++) EditorSceneManager.OpenScene(reopen[i], OpenSceneMode.Additive);
            }
            Log.Add($"Components: {yaml.Converted} swapped, {yaml.Removed} helpers removed, {yaml.SkippedInFields} left inside fields.");
            return true;
        }

        /// <summary>Stage three. Preview: every script as it would be, and what remains to do by hand.</summary>
        public List<(string path, string before, string after, List<LetteraTmpNote> notes)> PreviewScripts()
        {
            var previews = new List<(string, string, string, List<LetteraTmpNote>)>();
            foreach (LetteraTmpFinding finding in Findings.Where(f => f.Kind == LetteraTmpFinding.FileKind.Script))
            {
                string before = File.ReadAllText(finding.Path);
                var notes = new List<LetteraTmpNote>();
                string after = LetteraTmpScript.Rewrite(before, notes);
                previews.Add((finding.Path, before, after, notes));
            }
            return previews;
        }

        /// <summary>Applies the rewrite, with a backup outside the Project window. Assembly definitions go first.</summary>
        public int ApplyScripts()
        {
            int written = 0;
            foreach (LetteraTmpFinding finding in Findings.Where(f => f.Kind == LetteraTmpFinding.FileKind.Definition))
            {
                string text = File.ReadAllText(finding.Path);
                if (text.IndexOf("FirstSymbol.Lettera", StringComparison.Ordinal) >= 0) continue;
                File.WriteAllText(finding.Path, text.Replace("\"Unity.TextMeshPro\"", "\"Unity.TextMeshPro\",\n        \"FirstSymbol.Lettera\""), new UTF8Encoding(false));
                finding.Status = "Done";
                written++;
            }
            string backups = "Library/Lettera/ScriptBackups";
            Directory.CreateDirectory(backups);
            foreach ((string path, string before, string after, List<LetteraTmpNote> notes) in PreviewScripts())
            {
                foreach (LetteraTmpNote note in notes) Losses.Add($"{path}:{note.Line}: {note.Message}");
                if (after == before) continue;
                File.WriteAllText(Path.Combine(backups, path.Replace('/', '_')), before, new UTF8Encoding(false));
                File.WriteAllText(path, after, new UTF8Encoding(false));
                written++;
                Log.Add("Script: " + path);
            }
            AssetDatabase.Refresh();
            return written;
        }

        /// <summary>Everything found and done, as text for a person or a review.</summary>
        public string Report()
        {
            var text = new StringBuilder("# TextMesh Pro migration\n\n");
            foreach (LetteraTmpFinding finding in Findings) text.Append($"- {finding.Status}: {finding.Path}{(finding.Note != null ? " (" + finding.Note + ")" : "")}\n");
            text.Append("\n## Not carried\n\n");
            foreach (string loss in Losses) text.Append("- ").Append(loss).Append('\n');
            text.Append("\n## Log\n\n");
            foreach (string line in Log) text.Append("- ").Append(line).Append('\n');
            return text.ToString();
        }
    }

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

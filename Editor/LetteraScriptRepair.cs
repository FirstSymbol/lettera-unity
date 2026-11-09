using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace FirstSymbol.Lettera.Editor
{
    /// <summary>
    /// Before 1.0.0 fifteen of Lettera's classes shared a script file. A saved one lost its script: the file holds an empty script reference, and the object comes back as a missing script.
    /// Unity also writes the class's name beside it, so a file saved by Unity 6 says what it was, and this writes the reference back.
    /// </summary>
    internal static class LetteraScriptRepair
    {
        static readonly Regex s_Lost = new Regex(@"m_Script: \{fileID: 0\}(\r?\n  m_Name: [^\r\n]*\r?\n  m_EditorClassIdentifier: FirstSymbol\.Lettera[\w.]*:FirstSymbol\.Lettera[\w.]*:(\w+))", RegexOptions.Compiled);

        /// <summary>The text with each lost reference written back, by a function from a class's name to its script's guid. Pure.</summary>
        internal static string Rewrite(string text, Func<string, string> guidOf, out int repaired)
        {
            int count = 0;
            string result = s_Lost.Replace(text, match =>
            {
                string guid = guidOf(match.Groups[2].Value);
                if (string.IsNullOrEmpty(guid)) return match.Value;
                count++;
                return "m_Script: {fileID: 11500000, guid: " + guid + ", type: 3}" + match.Groups[1].Value;
            });
            repaired = count;
            return result;
        }

        internal static string GuidOfClass(string className)
        {
            foreach (string guid in AssetDatabase.FindAssets(className + " t:MonoScript"))
                if (Path.GetFileNameWithoutExtension(AssetDatabase.GUIDToAssetPath(guid)) == className)
                    return guid;
            return null;
        }

        /// <summary>Repairs the files below a folder, and says which it changed.</summary>
        internal static List<string> Repair(string folder, out int objects)
        {
            var changed = new List<string>();
            int total = 0;
            var guids = new Dictionary<string, string>();
            string Cached(string name)
            {
                if (!guids.TryGetValue(name, out string guid)) guids[name] = guid = GuidOfClass(name);
                return guid;
            }
            foreach (string pattern in new[] { "*.asset", "*.prefab", "*.unity" })
            {
                foreach (string path in Directory.GetFiles(folder, pattern, SearchOption.AllDirectories))
                {
                    string text;
                    try
                    {
                        text = File.ReadAllText(path);
                    }
                    catch (IOException)
                    {
                        continue;
                    }
                    if (!text.Contains("m_Script: {fileID: 0}")) continue;
                    string next = Rewrite(text, Cached, out int repaired);
                    if (repaired == 0) continue;
                    File.WriteAllText(path, next);
                    total += repaired;
                    string project = path.Replace('\\', '/');
                    changed.Add(project);
                    AssetDatabase.ImportAsset(project, ImportAssetOptions.ForceUpdate);
                }
            }
            objects = total;
            return changed;
        }

        [MenuItem("Tools/FirstSymbol/Lettera/Repair Saved Objects", false, 60)]
        static void Run()
        {
            if (!EditorUtility.DisplayDialog("Repair saved Lettera objects?", "Looks through Assets for Lettera objects saved by a version before 1.0.0 that came back as missing scripts, and writes their script back. Files change on disk, so version control is the undo. A scene open with unsaved changes should be saved first.", "Repair", "Cancel")) return;
            List<string> changed = Repair("Assets", out int objects);
            if (changed.Count == 0)
            {
                Debug.Log("Lettera: found no lost script in Assets.");
                return;
            }
            Debug.Log("Lettera: repaired " + objects + " objects in " + changed.Count + " files:\n" + string.Join("\n", changed));
        }
    }
}

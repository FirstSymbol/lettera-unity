using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace FirstSymbol.Lettera.Tests
{
    /// <summary>Unity finds a saved object's class through a script file of the class's name. A class sharing a file comes back from disk as a missing script, and in memory nothing shows it.</summary>
    public sealed class ScriptIdentityTests
    {
        const string Folder = "Assets/LetteraScriptIdentityTemp";

        static IEnumerable<Type> SavedTypes()
        {
            foreach (System.Reflection.Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                string name = assembly.GetName().Name;
                if (!name.StartsWith("FirstSymbol.Lettera", StringComparison.Ordinal) || name.Contains("Tests")) continue;
                foreach (Type type in assembly.GetTypes())
                {
                    if (type.IsAbstract || type.ContainsGenericParameters) continue;
                    if (typeof(MonoBehaviour).IsAssignableFrom(type) || typeof(ScriptableObject).IsAssignableFrom(type)) yield return type;
                }
            }
        }

        [Test]
        public void Diag()
        {
            AssetDatabase.CreateFolder("Assets", "LetteraScriptIdentityTemp");
            var lines = new List<string>();
            try
            {
                foreach (Type type in SavedTypes().OrderBy(t => t.FullName))
                {
                    if (typeof(UnityEditor.Editor).IsAssignableFrom(type)) continue;
                    string line = type.FullName + (type.IsPublic ? " public " : " internal ") + type.BaseType.Name;
                    try
                    {
                        MonoScript script = null;
                        string path = null;
                        if (typeof(MonoBehaviour).IsAssignableFrom(type))
                        {
                            var go = new GameObject("x", typeof(RectTransform));
                            var c = (MonoBehaviour)go.AddComponent(type);
                            script = c != null ? MonoScript.FromMonoBehaviour(c) : null;
                            path = Folder + "/" + type.Name + ".prefab";
                            PrefabUtility.SaveAsPrefabAsset(go, path);
                            UnityEngine.Object.DestroyImmediate(go);
                        }
                        else if (!typeof(EditorWindow).IsAssignableFrom(type))
                        {
                            ScriptableObject so = ScriptableObject.CreateInstance(type);
                            script = MonoScript.FromScriptableObject(so);
                            path = Folder + "/" + type.Name + ".asset";
                            AssetDatabase.CreateAsset(so, path);
                            AssetDatabase.SaveAssets();
                        }
                        else
                        {
                            ScriptableObject so = ScriptableObject.CreateInstance(type);
                            script = MonoScript.FromScriptableObject(so);
                            UnityEngine.Object.DestroyImmediate(so);
                        }
                        line += " | script " + (script == null ? "NONE" : script.name + " at " + AssetDatabase.GetAssetPath(script));
                        if (path != null)
                        {
                            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
                            string yaml = File.ReadAllText(path);
                            string found = string.Join(" ", Regex.Matches(yaml, @"m_Script: \{[^}]*\}").Cast<Match>().Select(m => m.Value));
                            line += " | " + found + " | reload " + AssetDatabase.LoadMainAssetAtPath(path)?.GetType().Name;
                        }
                    }
                    catch (Exception e) { line += " | threw " + e.GetType().Name + " " + e.Message; }
                    lines.Add(line);
                }
                foreach (string shared in new[] { "Editing/LetteraCommands", "Editing/LetteraDropdown", "Styling/CompositeEffects", "Motion/LetteraReveal", "Editing/LetteraFieldRule", "Interaction/LetteraInline", "Editing/LetteraSelectionVisuals", "Editing/LetteraTouch", "Styling/LetteraPaint", "Motion/LetteraSequencer", "Editing/LetteraFieldFactory", "Interaction/LetteraRanges", "Interaction/LetteraLinks" })
                {
                    var script = AssetDatabase.LoadAssetAtPath<MonoScript>("Packages/com.firstsymbol.lettera/Runtime/" + shared + ".cs");
                    lines.Add("FILE " + shared + " -> " + (script == null ? "null" : script.GetClass()?.FullName ?? "no class"));
                }
            }
            finally
            {
                AssetDatabase.DeleteAsset(Folder);
                File.WriteAllLines(@"D:\UnityBuilds\LetteraClean_Out\script_diag.txt", lines);
            }
        }
    }
}

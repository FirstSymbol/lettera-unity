using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace FirstSymbol.Lettera.Tests
{
    /// <summary>Unity finds a saved object's class through a script file of the class's name. A class sharing a file comes back from disk as a missing script, and in memory nothing shows it.</summary>
    public sealed class ScriptIdentityTests
    {
        const string Folder = "Assets/LetteraScriptIdentityTemp";

        static IEnumerable<Type> SavedTypes()
        {
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                string name = assembly.GetName().Name;
                if (!name.StartsWith("FirstSymbol.Lettera", StringComparison.Ordinal) || name.Contains("Tests")) continue;
                foreach (Type type in assembly.GetTypes())
                {
                    if (type.IsAbstract || type.ContainsGenericParameters || typeof(UnityEditor.Editor).IsAssignableFrom(type)) continue;
                    if (typeof(MonoBehaviour).IsAssignableFrom(type) || typeof(ScriptableObject).IsAssignableFrom(type)) yield return type;
                }
            }
        }

        [SetUp]
        public void SetUp()
        {
            if (AssetDatabase.IsValidFolder(Folder)) AssetDatabase.DeleteAsset(Folder);
            AssetDatabase.CreateFolder("Assets", "LetteraScriptIdentityTemp");
        }

        [TearDown]
        public void TearDown()
        {
            AssetDatabase.DeleteAsset(Folder);
            LogAssert.ignoreFailingMessages = false;
        }

        static string FileOf(string guid) => Path.GetFileNameWithoutExtension(AssetDatabase.GUIDToAssetPath(guid));

        [Test]
        public void TheListHoldsWhatItShould()
        {
            var names = SavedTypes().Select(t => t.Name).ToList();
            CollectionAssert.IsSupersetOf(names, new[] { "LetteraText", "LetteraPaintLibrary", "LetteraContextMenu", "LetteraGlyphsWindow" });
            Assert.Greater(names.Count, 40);
        }

        [Test]
        public void EveryClassHasAScriptOfItsOwnName()
        {
            LogAssert.ignoreFailingMessages = true;
            var lost = new List<string>();
            foreach (Type type in SavedTypes())
            {
                MonoScript script = null;
                if (typeof(MonoBehaviour).IsAssignableFrom(type))
                {
                    var go = new GameObject("x", typeof(RectTransform));
                    var added = (MonoBehaviour)go.AddComponent(type);
                    if (added != null) script = MonoScript.FromMonoBehaviour(added);
                    UnityEngine.Object.DestroyImmediate(go);
                }
                else
                {
                    ScriptableObject made = ScriptableObject.CreateInstance(type);
                    script = MonoScript.FromScriptableObject(made);
                    UnityEngine.Object.DestroyImmediate(made);
                }
                // A component in a shared file gets a script object with no file behind it, so a null check is not enough.
                string path = script != null ? AssetDatabase.GetAssetPath(script) : null;
                if (string.IsNullOrEmpty(path) || Path.GetFileNameWithoutExtension(path) != type.Name) lost.Add(type.FullName + " -> " + path);
            }
            Assert.IsEmpty(lost, string.Join("\n", lost));
        }

        [Test]
        public void EveryClassComesBackFromDisk()
        {
            LogAssert.ignoreFailingMessages = true;
            var lost = new List<string>();
            foreach (Type type in SavedTypes())
            {
                if (typeof(EditorWindow).IsAssignableFrom(type)) continue;
                string path;
                if (typeof(MonoBehaviour).IsAssignableFrom(type))
                {
                    var go = new GameObject("x", typeof(RectTransform));
                    go.AddComponent(type);
                    path = Folder + "/" + type.Name + ".prefab";
                    PrefabUtility.SaveAsPrefabAsset(go, path);
                    UnityEngine.Object.DestroyImmediate(go);
                }
                else
                {
                    path = Folder + "/" + type.Name + ".asset";
                    AssetDatabase.CreateAsset(ScriptableObject.CreateInstance(type), path);
                    AssetDatabase.SaveAssets();
                }
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
                EditorUtility.UnloadUnusedAssetsImmediate(true);
                var guids = Regex.Matches(File.ReadAllText(path), @"m_Script: \{([^}]*)\}").Cast<Match>().Select(m => m.Groups[1].Value).ToList();
                if (guids.Any(g => g.Contains("fileID: 0"))) lost.Add(type.Name + " saved no script");
                bool found = guids.Any(g =>
                {
                    Match guid = Regex.Match(g, @"guid: (\w+)");
                    return guid.Success && FileOf(guid.Groups[1].Value) == type.Name;
                });
                if (!found) lost.Add(type.Name + " saved under another file");

                UnityEngine.Object back = AssetDatabase.LoadMainAssetAtPath(path);
                bool holds = back is GameObject prefab ? prefab.GetComponent(type) != null : type.IsInstanceOfType(back);
                if (!holds) lost.Add(type.Name + " did not load");
            }
            Assert.IsEmpty(lost, string.Join("\n", lost));
        }

        // A range topic has always been saved through this guid, when it was the first class of LetteraRanges.cs.
        [Test]
        public void ATopicSavedBeforeTheMoveStillLoads()
        {
            Assert.AreEqual("LetteraRangeTopic", FileOf("c5d15b056d3d1984f951aacd31ebbf43"));
            string path = Folder + "/Old Topic.asset";
            File.WriteAllText(path, Yaml("{fileID: 11500000, guid: c5d15b056d3d1984f951aacd31ebbf43, type: 3}", "Old Topic", "LetteraRangeTopic", ""));
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            Assert.IsInstanceOf<LetteraRangeTopic>(AssetDatabase.LoadMainAssetAtPath(path));
        }

        static string Yaml(string script, string name, string type, string fields) =>
            "%YAML 1.1\n%TAG !u! tag:unity3d.com,2011:\n--- !u!114 &11400000\nMonoBehaviour:\n  m_ObjectHideFlags: 0\n  m_CorrespondingSourceObject: {fileID: 0}\n  m_PrefabInstance: {fileID: 0}\n  m_PrefabAsset: {fileID: 0}\n  m_GameObject: {fileID: 0}\n  m_Enabled: 1\n  m_EditorHideFlags: 0\n  m_Script: " + script + "\n  m_Name: " + name + "\n  m_EditorClassIdentifier: FirstSymbol.Lettera:FirstSymbol.Lettera:" + type + "\n" + fields;

        // Measures what an older asset does today.
        [Test]
        public void Diag()
        {
            string path = Folder + "/Old Paints.asset";
            File.WriteAllText(path, Yaml("{fileID: 0}", "Old Paints", "LetteraPaintLibrary", "  m_Swatches: []\n"));
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            EditorUtility.UnloadUnusedAssetsImmediate(true);
            UnityEngine.Object back = AssetDatabase.LoadMainAssetAtPath(path);
            var serialized = back != null ? new SerializedObject(back) : null;
            File.WriteAllLines(@"D:\UnityBuilds\LetteraClean_Out\script_diag2.txt", new[]
            {
                "type " + back?.GetType().FullName,
                "script " + serialized?.FindProperty("m_Script")?.objectReferenceValue,
                "yaml " + Regex.Match(File.ReadAllText(path), @"m_Script: \{[^}]*\}").Value,
            });
        }
    }
}

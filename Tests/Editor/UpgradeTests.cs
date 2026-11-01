using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using FirstSymbol.Lettera.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace FirstSymbol.Lettera.Tests
{
    /// <summary>Files as a version before the stamp wrote them, which is how every 0.x project holds them.</summary>
    public sealed class UpgradeTests
    {
        const string Folder = "Assets/LetteraUpgradeTemp";

        // A style preset saved by 0.7.3: no stamp on the asset or its style.
        const string OldPreset = @"%YAML 1.1
%TAG !u! tag:unity3d.com,2011:
--- !u!114 &11400000
MonoBehaviour:
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {fileID: 0}
  m_PrefabInstance: {fileID: 0}
  m_PrefabAsset: {fileID: 0}
  m_GameObject: {fileID: 0}
  m_Enabled: 1
  m_EditorHideFlags: 0
  m_Script: {fileID: 11500000, guid: SCRIPT, type: 3}
  m_Name: Old Preset
  m_EditorClassIdentifier:
  m_Styles:
  - m_Matcher:
      rid: -2
    m_Effect:
      rid: -2
    m_Enabled: 0
    m_DefaultParameter: kept
  references:
    version: 2
    RefIds:
    - rid: -2
      type: {class: , ns: , asm: }
";

        // A text in a prefab, saved by 0.7.3.
        const string OldPrefab = @"%YAML 1.1
%TAG !u! tag:unity3d.com,2011:
--- !u!1 &100
GameObject:
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {fileID: 0}
  m_PrefabInstance: {fileID: 0}
  m_PrefabAsset: {fileID: 0}
  serializedVersion: 6
  m_Component:
  - component: {fileID: 101}
  - component: {fileID: 102}
  m_Layer: 0
  m_Name: Old Text
  m_TagString: Untagged
  m_Icon: {fileID: 0}
  m_NavMeshLayer: 0
  m_StaticEditorFlags: 0
  m_IsActive: 1
--- !u!224 &101
RectTransform:
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {fileID: 0}
  m_PrefabInstance: {fileID: 0}
  m_PrefabAsset: {fileID: 0}
  m_GameObject: {fileID: 100}
  m_LocalRotation: {x: 0, y: 0, z: 0, w: 1}
  m_LocalPosition: {x: 0, y: 0, z: 0}
  m_LocalScale: {x: 1, y: 1, z: 1}
  m_ConstrainProportionsScale: 0
  m_Children: []
  m_Father: {fileID: 0}
  m_LocalEulerAnglesHint: {x: 0, y: 0, z: 0}
  m_AnchorMin: {x: 0.5, y: 0.5}
  m_AnchorMax: {x: 0.5, y: 0.5}
  m_AnchoredPosition: {x: 0, y: 0}
  m_SizeDelta: {x: 100, y: 100}
  m_Pivot: {x: 0.5, y: 0.5}
--- !u!114 &102
MonoBehaviour:
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {fileID: 0}
  m_PrefabInstance: {fileID: 0}
  m_PrefabAsset: {fileID: 0}
  m_GameObject: {fileID: 100}
  m_Enabled: 1
  m_EditorHideFlags: 0
  m_Script: {fileID: 11500000, guid: SCRIPT, type: 3}
  m_Name:
  m_EditorClassIdentifier:
  m_Color: {r: 1, g: 0.5, b: 0.25, a: 1}
  m_Lit: 1
";

        [SetUp]
        public void SetUp()
        {
            AssetDatabase.DeleteAsset(Folder);
            AssetDatabase.CreateFolder("Assets", "LetteraUpgradeTemp");
            LetteraUpgrades.RunPending();
        }

        [TearDown]
        public void TearDown()
        {
            AssetDatabase.DeleteAsset(Folder);
            LetteraUpgrades.RunPending();
        }

        static string GuidOf(string className) => AssetDatabase.FindAssets(className + " t:MonoScript").First(g => Path.GetFileNameWithoutExtension(AssetDatabase.GUIDToAssetPath(g)) == className);

        static string Write(string name, string content, string className)
        {
            string path = Folder + "/" + name;
            File.WriteAllText(path, content.Replace("SCRIPT", GuidOf(className)));
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            return path;
        }

        [Test]
        public void AnOldFileLoadsWithItsValuesAndZero()
        {
            string path = Write("Old.asset", OldPreset, nameof(LetteraStylePreset));
            var preset = AssetDatabase.LoadAssetAtPath<LetteraStylePreset>(path);
            Assert.AreEqual(0, preset.LetteraSerializedVersion);
            Assert.AreEqual(1, preset.Styles.Count);
            Assert.AreEqual(0, preset.Styles[0].LetteraSerializedVersion);
            Assert.AreEqual("kept", preset.Styles[0].DefaultParameter);
            Assert.IsFalse(preset.Styles[0].Enabled);
        }

        [Test]
        public void WithNoStepAnOldFileStaysCleanAndUnchanged()
        {
            string path = Write("Old.asset", OldPreset, nameof(LetteraStylePreset));
            byte[] before = File.ReadAllBytes(path);
            var preset = AssetDatabase.LoadAssetAtPath<LetteraStylePreset>(path);
            Assert.GreaterOrEqual(LetteraUpgrades.RunPending(), 2);
            Assert.AreEqual(LetteraSerializedVersions.Current, preset.LetteraSerializedVersion);
            Assert.AreEqual(LetteraSerializedVersions.Current, preset.Styles[0].LetteraSerializedVersion);
            Assert.IsFalse(EditorUtility.IsDirty(preset), "Opening an old asset dirties nothing.");
            CollectionAssert.AreEqual(before, File.ReadAllBytes(path));
        }

        [Test]
        public void AStepRunsOnceOnAssetAndStyleAndTheNextSaveWritesTheStamp()
        {
            var seen = new List<object>();
            Action<object> step = target =>
            {
                seen.Add(target);
                if (target is LetteraStyle style) style.DefaultParameter = "upgraded";
            };
            LetteraUpgrades.Register(0, LetteraSerializedVersions.Current, step);
            try
            {
                string path = Write("Old.asset", OldPreset, nameof(LetteraStylePreset));
                var preset = AssetDatabase.LoadAssetAtPath<LetteraStylePreset>(path);
                LetteraUpgrades.RunPending();
                Assert.Contains(preset, seen);
                Assert.Contains(preset.Styles[0], seen);
                Assert.AreEqual("upgraded", preset.Styles[0].DefaultParameter);
                Assert.IsTrue(EditorUtility.IsDirty(preset));
                int count = seen.Count;
                LetteraUpgrades.RunPending();
                Assert.AreEqual(count, seen.Count, "Twice would be a step run on what it already did.");
                AssetDatabase.SaveAssets();
                string saved = File.ReadAllText(path);
                StringAssert.Contains("LetteraSerializedVersion: " + LetteraSerializedVersions.Current, saved);
                StringAssert.Contains("m_DefaultParameter: upgraded", saved);
            }
            finally
            {
                LetteraUpgrades.Unregister(step);
            }
        }

        [Test]
        public void WhatHasNotBeenUpgradedDoesNotSayItWas()
        {
            string path = Write("Old.asset", OldPreset, nameof(LetteraStylePreset));
            var preset = AssetDatabase.LoadAssetAtPath<LetteraStylePreset>(path);
            var serialized = new SerializedObject(preset);
            Assert.AreEqual(0, serialized.FindProperty("LetteraSerializedVersion").intValue);
        }

        [Test]
        public void AFailedStepLeavesItOlder()
        {
            Action<object> step = _ => throw new InvalidOperationException("on purpose");
            LetteraUpgrades.Register(0, LetteraSerializedVersions.Current, step);
            try
            {
                string path = Write("Old.asset", OldPreset, nameof(LetteraStylePreset));
                var preset = AssetDatabase.LoadAssetAtPath<LetteraStylePreset>(path);
                UnityEngine.TestTools.LogAssert.ignoreFailingMessages = true;
                LetteraUpgrades.RunPending();
                Assert.AreEqual(0, preset.LetteraSerializedVersion);
            }
            finally
            {
                UnityEngine.TestTools.LogAssert.ignoreFailingMessages = false;
                LetteraUpgrades.Unregister(step);
            }
        }

        [Test]
        public void AnOldPrefabsTextKeepsItsValues()
        {
            string path = Write("Old.prefab", OldPrefab, nameof(LetteraWorldText));
            var text = AssetDatabase.LoadAssetAtPath<GameObject>(path).GetComponent<LetteraWorldText>();
            Assert.AreEqual(0, text.LetteraSerializedVersion);
            Assert.IsTrue(text.Lit);
            Assert.AreEqual(new Color(1, 0.5f, 0.25f, 1), text.Color);
            LetteraUpgrades.RunPending();
            Assert.AreEqual(LetteraSerializedVersions.Current, text.LetteraSerializedVersion);
            Assert.IsFalse(EditorUtility.IsDirty(text));
        }

        [Test]
        public void SomethingNewIsSavedWithTheStamp()
        {
            var stack = ScriptableObject.CreateInstance<LetteraFontStack>();
            string path = Folder + "/New.asset";
            AssetDatabase.CreateAsset(stack, path);
            AssetDatabase.SaveAssets();
            StringAssert.Contains("LetteraSerializedVersion: " + LetteraSerializedVersions.Current, File.ReadAllText(path));
        }

        [Test]
        public void EverySavedKindHoldsAStamp()
        {
            var kinds = new List<Type> { typeof(LetteraText), typeof(LetteraWorldText), typeof(LetteraStyle), Type.GetType("FirstSymbol.Lettera.LetteraText3D, FirstSymbol.Lettera.Text3D") };
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                string name = assembly.GetName().Name;
                if (name != "FirstSymbol.Lettera" && name != "FirstSymbol.Lettera.Text3D") continue;
                kinds.AddRange(assembly.GetTypes().Where(t => typeof(ScriptableObject).IsAssignableFrom(t) && !t.IsAbstract));
            }
            Assert.Greater(kinds.Count, 14);
            foreach (Type kind in kinds)
            {
                Assert.IsTrue(typeof(ILetteraStamped).IsAssignableFrom(kind), kind.Name);
                Assert.IsTrue(typeof(ISerializationCallbackReceiver).IsAssignableFrom(kind), kind.Name);
                bool found = false;
                for (Type t = kind; t != null && !found; t = t.BaseType) found = t.GetField("LetteraSerializedVersion", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly) != null;
                Assert.IsTrue(found, kind.Name);
            }
        }

        [Test]
        public void ARegisterWithNoProgressIsRefused()
        {
            Assert.Throws<ArgumentException>(() => LetteraUpgrades.Register(1, 1, _ => { }));
            Assert.Throws<ArgumentNullException>(() => LetteraUpgrades.Register(0, 1, null));
        }
    }
}

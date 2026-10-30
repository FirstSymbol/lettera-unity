using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace FirstSymbol.Lettera.Editor
{
    /// <summary>Menu items that create text objects and font assets.</summary>
    internal static class LetteraMenus
    {
        [MenuItem("GameObject/Lettera/Text", false, 10)]
        static void CreateText(MenuCommand command)
        {
            GameObject prefab = LetteraSettings.Instance.TextPrefab;
            Transform parent = FindOrCreateCanvas(command.context as GameObject);
            GameObject go;
            if (prefab != null)
            {
                go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            }
            else
            {
                go = new GameObject("Text (Lettera)", typeof(RectTransform));
                go.transform.SetParent(parent, false);
                ((RectTransform)go.transform).sizeDelta = new Vector2(220, 50);
                var text = go.AddComponent<LetteraText>();
                text.Text = "New Text";
            }
            Finish(go);
        }

        [MenuItem("GameObject/Lettera/World Text", false, 11)]
        static void CreateWorldText(MenuCommand command)
        {
            GameObject prefab = LetteraSettings.Instance.WorldTextPrefab;
            var parent = command.context as GameObject;
            GameObject go;
            if (prefab != null)
            {
                go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent != null ? parent.transform : null);
            }
            else
            {
                go = new GameObject("World Text (Lettera)", typeof(RectTransform));
                GameObjectUtility.SetParentAndAlign(go, parent);
                ((RectTransform)go.transform).sizeDelta = new Vector2(20, 5);
                var text = go.AddComponent<LetteraWorldText>();
                text.FontSize = 2f;
                text.Text = "New Text";
            }
            Finish(go);
        }

        static void Finish(GameObject go)
        {
            Undo.RegisterCreatedObjectUndo(go, "Create " + go.name);
            Selection.activeGameObject = go;
        }

        static Transform FindOrCreateCanvas(GameObject context)
        {
            if (context != null)
            {
                Canvas parentCanvas = context.GetComponentInParent<Canvas>();
                if (parentCanvas != null) return context.transform;
            }
            var canvas = UnityEngine.Object.FindAnyObjectByType<Canvas>();
            if (canvas == null)
            {
                var canvasObject = new GameObject("Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
                canvas = canvasObject.GetComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                Undo.RegisterCreatedObjectUndo(canvasObject, "Create Canvas");
            }
            if (UnityEngine.Object.FindAnyObjectByType<EventSystem>() == null)
            {
                var events = new GameObject("EventSystem", typeof(EventSystem));
                Undo.RegisterCreatedObjectUndo(events, "Create EventSystem");
            }
            return canvas.transform;
        }

        static bool IsFontPath(string path)
        {
            string extension = Path.GetExtension(path).ToLowerInvariant();
            return extension == ".ttf" || extension == ".otf" || extension == ".ttc" || extension == ".otc";
        }

        static IEnumerable<string> SelectedFontPaths()
        {
            foreach (string guid in Selection.assetGUIDs)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (IsFontPath(path)) yield return path;
            }
        }

        [MenuItem("Assets/Create/Lettera/Font Asset From Selection", true)]
        static bool ValidateCreateFontAssets()
        {
            foreach (string _ in SelectedFontPaths()) return true;
            return false;
        }

        [MenuItem("Assets/Create/Lettera/Font Asset From Selection", false, 90)]
        static void CreateFontAssets()
        {
            foreach (string path in new List<string>(SelectedFontPaths())) CreateFontAsset<LetteraFont>(path);
        }

        [MenuItem("Assets/Create/Lettera/Color Font Asset From Selection", true)]
        static bool ValidateCreateColorFontAssets() => ValidateCreateFontAssets();

        [MenuItem("Assets/Create/Lettera/Color Font Asset From Selection", false, 91)]
        static void CreateColorFontAssets()
        {
            foreach (string path in new List<string>(SelectedFontPaths())) CreateFontAsset<LetteraColorFont>(path);
        }

        /// <summary>Stores a font file in a new asset next to it and returns that asset.</summary>
        public static T CreateFontAsset<T>(string fontPath) where T : LetteraFont
        {
            var asset = ScriptableObject.CreateInstance<T>();
            asset.SetFontData(File.ReadAllBytes(fontPath));
            string target = AssetDatabase.GenerateUniqueAssetPath(Path.ChangeExtension(fontPath, null) + " Lettera.asset");
            AssetDatabase.CreateAsset(asset, target);
            AssetDatabase.SaveAssets();
            Selection.activeObject = asset;
            return asset;
        }
    }

    /// <summary>Project Settings > Lettera.</summary>
    internal static class LetteraSettingsProvider
    {
        const string AssetPath = "Assets/Resources/" + LetteraSettings.ResourceName + ".asset";

        [SettingsProvider]
        static SettingsProvider Create() => new SettingsProvider("Project/Lettera", SettingsScope.Project)
        {
            guiHandler = _ =>
            {
                var settings = Resources.Load<LetteraSettings>(LetteraSettings.ResourceName);
                if (settings == null)
                {
                    EditorGUILayout.HelpBox("This project uses Lettera's defaults.", MessageType.Info);
                    if (GUILayout.Button("Create Settings Asset"))
                    {
                        Directory.CreateDirectory("Assets/Resources");
                        AssetDatabase.CreateAsset(ScriptableObject.CreateInstance<LetteraSettings>(), AssetPath);
                        AssetDatabase.SaveAssets();
                    }
                    return;
                }
                var serialized = new SerializedObject(settings);
                serialized.Update();
                SerializedProperty property = serialized.GetIterator();
                bool enter = true;
                while (property.NextVisible(enter))
                {
                    enter = false;
                    if (property.name == "m_Script") continue;
                    EditorGUILayout.PropertyField(property, true);
                }
                serialized.ApplyModifiedProperties();
            },
            keywords = new HashSet<string>(new[] { "Lettera", "Text", "Font", "Emoji", "Fallback" }),
        };
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEngine.UIElements;

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

        internal static Transform FindOrCreateCanvas(GameObject context)
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
            if (UnityEngine.Object.FindAnyObjectByType<EventSystem>() == null) CreateEventSystem();
            return canvas.transform;
        }

        // Without an input module UI gets no input. Which one a project can use depends on its input handling.
        internal static void CreateEventSystem()
        {
            var events = new GameObject("EventSystem", typeof(EventSystem));
#if ENABLE_INPUT_SYSTEM
            Type module = Type.GetType("UnityEngine.InputSystem.UI.InputSystemUIInputModule, Unity.InputSystem");
            if (module != null) events.AddComponent(module);
            else events.AddComponent<StandaloneInputModule>();
#else
            events.AddComponent<StandaloneInputModule>();
#endif
            Undo.RegisterCreatedObjectUndo(events, "Create EventSystem");
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
            activateHandler = (_, root) => root.Add(Build()),
            keywords = new HashSet<string>(new[] { "Lettera", "Text", "Font", "Emoji", "Fallback", "Caret", "Selection", "Undo", "Clipboard", "Commands" }),
        };

        internal static VisualElement Build()
        {
            VisualElement root = LtUI.Root();
            VisualElement page = LtFrame.Header(root, "Lettera", "Project-wide defaults");
            var settings = Resources.Load<LetteraSettings>(LetteraSettings.ResourceName);
            if (settings == null)
            {
                page.Add(LtUI.Card("", LtUI.Text("This project uses Lettera's defaults.", "lt-subtitle"), LtUI.Clickable("Create Settings Asset", "lt-tool", "Create it in Assets/Resources", () =>
                {
                    Directory.CreateDirectory("Assets/Resources");
                    AssetDatabase.CreateAsset(ScriptableObject.CreateInstance<LetteraSettings>(), AssetPath);
                    AssetDatabase.SaveAssets();
                    VisualElement parent = root.parent;
                    root.RemoveFromHierarchy();
                    parent?.Add(Build());
                })));
                return root;
            }
            var serialized = new SerializedObject(settings);
            page.Add(LtFrame.AllFields(serialized, "Text", "m_Editing", "m_Motion", "m_Documents", "m_Platform", "m_FontMemory"));
            SerializedProperty editing = serialized.FindProperty("m_Editing");
            var rows = new List<VisualElement>();
            SerializedProperty child = editing.Copy();
            SerializedProperty end = editing.GetEndProperty();
            bool enter = true;
            while (child.NextVisible(enter) && !SerializedProperty.EqualContents(child, end))
            {
                enter = false;
                if (child.name == "Commands") continue;
                rows.Add(LtFrame.Row(child.Copy()));
            }
            page.Add(LtUI.Card("Selection & Editing", rows.ToArray()));
            var motionRows = new List<VisualElement>();
            SerializedProperty motion = serialized.FindProperty("m_Motion");
            SerializedProperty motionChild = motion.Copy();
            SerializedProperty motionEnd = motion.GetEndProperty();
            bool motionEnter = true;
            while (motionChild.NextVisible(motionEnter) && !SerializedProperty.EqualContents(motionChild, motionEnd))
            {
                motionEnter = false;
                motionRows.Add(LtFrame.Row(motionChild.Copy()));
            }
            page.Add(LtUI.Card("Animation & Interaction", motionRows.ToArray()));
            var documentRows = new List<VisualElement>();
            SerializedProperty documents = serialized.FindProperty("m_Documents");
            SerializedProperty documentChild = documents.Copy();
            SerializedProperty documentEnd = documents.GetEndProperty();
            bool documentEnter = true;
            while (documentChild.NextVisible(documentEnter) && !SerializedProperty.EqualContents(documentChild, documentEnd))
            {
                documentEnter = false;
                documentRows.Add(LtFrame.Row(documentChild.Copy()));
            }
            page.Add(LtUI.Card("Documents", documentRows.ToArray()));
            var platformRows = new List<VisualElement>();
            SerializedProperty platform = serialized.FindProperty("m_Platform");
            SerializedProperty platformChild = platform.Copy();
            SerializedProperty platformEnd = platform.GetEndProperty();
            bool platformEnter = true;
            while (platformChild.NextVisible(platformEnter) && !SerializedProperty.EqualContents(platformChild, platformEnd))
            {
                platformEnter = false;
                platformRows.Add(LtFrame.Row(platformChild.Copy()));
            }
            page.Add(LtUI.Card("Platform", platformRows.ToArray()));
            var fontRows = new List<VisualElement>();
            SerializedProperty fontMemory = serialized.FindProperty("m_FontMemory");
            SerializedProperty fontChild = fontMemory.Copy();
            SerializedProperty fontEnd = fontMemory.GetEndProperty();
            bool fontEnter = true;
            while (fontChild.NextVisible(fontEnter) && !SerializedProperty.EqualContents(fontChild, fontEnd))
            {
                fontEnter = false;
                fontRows.Add(LtFrame.Row(fontChild.Copy()));
            }
            page.Add(LtUI.Card("Font Memory", fontRows.ToArray()));
            page.Add(LtUI.Card("Commands", new LetteraReferenceList(editing.FindPropertyRelative("Commands"), () => LetteraRuleCatalog.Commands)));
            return root;
        }
    }
}

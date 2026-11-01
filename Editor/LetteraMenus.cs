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
    [InitializeOnLoad]
    internal static class LetteraMenus
    {
        static LetteraMenus()
        {
            LetteraSettings.SeedFromDefault = Seed;
        }

        // What a project's default prefab says about a new text, on Add Component and on Reset.
        internal static void Seed(Component added)
        {
            GameObject slot = SlotFor(added);
            if (slot == null) return;
            Component from = slot.GetComponentInChildren(added.GetType(), true);
            if (from == null || from == added) return;
            EditorJsonUtility.FromJsonOverwrite(EditorJsonUtility.ToJson(from), added);
        }

        static GameObject SlotFor(Component component)
        {
            LetteraSettings settings = LetteraSettings.Instance;
            switch (component)
            {
                case LetteraText _: return settings.TextPrefab;
                case LetteraWorldText _: return settings.WorldTextPrefab;
                default: return component.GetType().Name == "LetteraText3D" ? settings.Text3DPrefab : null;
            }
        }

        /// <summary>A prefab of this object, kept in the project and put in the slot of its kind, so that a new text starts out like it.</summary>
        internal static GameObject SaveAsDefault(GameObject go)
        {
            if (go == null) return null;
            LetteraSettings settings = EnsureSettings();
            if (settings == null) return null;
            string folder = "Assets/Lettera Defaults";
            if (!AssetDatabase.IsValidFolder(folder)) AssetDatabase.CreateFolder("Assets", "Lettera Defaults");
            string path = AssetDatabase.GenerateUniqueAssetPath(folder + "/" + go.name + ".prefab");
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(go, path);
            if (prefab == null) return null;
            var serialized = new SerializedObject(settings);
            string slot = go.GetComponent<LetteraText>() != null ? "m_TextPrefab" : go.GetComponent<LetteraWorldText>() != null ? "m_WorldTextPrefab" : "m_Text3DPrefab";
            serialized.FindProperty(slot).objectReferenceValue = prefab;
            serialized.ApplyModifiedProperties();
            AssetDatabase.SaveAssets();
            return prefab;
        }

        // Writing a slot is the edit that asks for a settings asset.
        internal static LetteraSettings EnsureSettings()
        {
            var settings = Resources.Load<LetteraSettings>(LetteraSettings.ResourceName);
            if (settings != null) return settings;
            Directory.CreateDirectory("Assets/Resources");
            settings = ScriptableObject.CreateInstance<LetteraSettings>();
            AssetDatabase.CreateAsset(settings, "Assets/Resources/" + LetteraSettings.ResourceName + ".asset");
            AssetDatabase.SaveAssets();
            return settings;
        }

        [MenuItem("GameObject/Lettera/Text", false, 10)]
        internal static void CreateText(MenuCommand command)
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
        internal static void CreateWorldText(MenuCommand command)
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

        [MenuItem("GameObject/Lettera/Button", false, 19)]
        internal static void CreateButton(MenuCommand command)
        {
            GameObject prefab = LetteraSettings.Instance.ButtonPrefab;
            Transform parent = FindOrCreateCanvas(command.context as GameObject);
            GameObject go;
            if (prefab != null)
            {
                go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            }
            else
            {
                go = new GameObject("Button (Lettera)", typeof(RectTransform), typeof(CanvasRenderer), typeof(UnityEngine.UI.Image), typeof(UnityEngine.UI.Button));
                go.transform.SetParent(parent, false);
                ((RectTransform)go.transform).sizeDelta = new Vector2(160, 40);
                var image = go.GetComponent<UnityEngine.UI.Image>();
                image.sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
                image.type = UnityEngine.UI.Image.Type.Sliced;
                var labelObject = new GameObject("Label", typeof(RectTransform));
                labelObject.transform.SetParent(go.transform, false);
                var rect = (RectTransform)labelObject.transform;
                rect.anchorMin = Vector2.zero;
                rect.anchorMax = Vector2.one;
                rect.offsetMin = rect.offsetMax = Vector2.zero;
                var label = labelObject.AddComponent<LetteraText>();
                label.Text = "Button";
                label.FontSize = 18;
                label.color = new Color32(32, 32, 36, 255);
                label.HorizontalAlignment = LetteraHorizontalAlignment.Center;
                label.VerticalAlignment = LetteraVerticalAlignment.Middle;
                label.raycastTarget = false;
            }
            Finish(go);
        }

        [MenuItem("GameObject/Lettera/Document", false, 20)]
        internal static void CreateDocument(MenuCommand command)
        {
            GameObject prefab = LetteraSettings.Instance.DocumentPrefab;
            Transform parent = FindOrCreateCanvas(command.context as GameObject);
            GameObject go;
            if (prefab != null)
            {
                go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            }
            else
            {
                go = new GameObject("Document (Lettera)", typeof(RectTransform), typeof(RectMask2D));
                go.transform.SetParent(parent, false);
                ((RectTransform)go.transform).sizeDelta = new Vector2(480, 320);
                var textObject = new GameObject("Text", typeof(RectTransform));
                textObject.transform.SetParent(go.transform, false);
                var rect = (RectTransform)textObject.transform;
                rect.anchorMin = Vector2.zero;
                rect.anchorMax = Vector2.one;
                rect.offsetMin = rect.offsetMax = Vector2.zero;
                var text = textObject.AddComponent<LetteraText>();
                text.Text = "A long text goes in by OpenDocument, from a script or a Source Loader.";
                text.FontSize = 18;
                go.AddComponent<LetteraDocumentScroller>();
            }
            Finish(go);
        }

        static void Finish(GameObject go)
        {
            Undo.RegisterCreatedObjectUndo(go, "Create " + go.name);
            Selection.activeGameObject = go;
        }

        // ------------------------------------------------------------- a text's right click

        static string TextOf(Component text) => ((ITextComponent)text).SourceText;

        [MenuItem("CONTEXT/LetteraText/Copy Text")]
        [MenuItem("CONTEXT/LetteraWorldText/Copy Text")]
        static void CopyText(MenuCommand command) => EditorGUIUtility.systemCopyBuffer = TextOf((Component)command.context);

        [MenuItem("CONTEXT/LetteraText/Paste Text")]
        [MenuItem("CONTEXT/LetteraWorldText/Paste Text")]
        static void PasteText(MenuCommand command)
        {
            var serialized = new SerializedObject(command.context);
            serialized.FindProperty("m_Data").FindPropertyRelative("Text").stringValue = EditorGUIUtility.systemCopyBuffer;
            serialized.ApplyModifiedProperties();
        }

        [MenuItem("CONTEXT/LetteraText/Edit In Scene")]
        [MenuItem("CONTEXT/LetteraWorldText/Edit In Scene")]
        static void EditInScene(MenuCommand command) => LetteraSceneEditView.Begin(new[] { command.context });

        [MenuItem("CONTEXT/LetteraText/Inspect In Scene")]
        [MenuItem("CONTEXT/LetteraWorldText/Inspect In Scene")]
        static void InspectInScene(MenuCommand command)
        {
            Selection.activeObject = ((Component)command.context).gameObject;
            if (!LetteraInspectOverlay.Enabled) EditorApplication.ExecuteMenuItem("Tools/Lettera/Inspect Text In Scene");
        }

        [MenuItem("CONTEXT/LetteraText/Save As Default")]
        [MenuItem("CONTEXT/LetteraWorldText/Save As Default")]
        static void SaveDefault(MenuCommand command) => SaveAsDefault(((Component)command.context).gameObject);

        [MenuItem("CONTEXT/LetteraText/Convert To World Text")]
        static void ToWorld(MenuCommand command) => ConvertToWorld((LetteraText)command.context);

        [MenuItem("CONTEXT/LetteraText/Convert To World Text", true)]
        static bool CanToWorld(MenuCommand command) => ((Component)command.context).GetComponent<LetteraSelectable>() == null;

        [MenuItem("CONTEXT/LetteraWorldText/Convert To Canvas Text")]
        static void ToCanvas(MenuCommand command) => ConvertToCanvas((LetteraWorldText)command.context);

        [MenuItem("CONTEXT/LetteraWorldText/Convert To Canvas Text", true)]
        static bool CanToCanvas(MenuCommand command) => ((Component)command.context).GetComponent<LetteraSelectable>() == null;

        // A world unit is much larger than a Canvas pixel, so a converted text keeps its look by scaling its numbers.
        const float PixelsPerWorldUnit = 18;

        internal static LetteraWorldText ConvertToWorld(LetteraText text)
        {
            if (text == null || text.GetComponent<LetteraSelectable>() != null) return null;
            GameObject go = text.gameObject;
            string json = EditorJsonUtility.ToJson(text);
            Color color = text.color;
            Undo.SetCurrentGroupName("Convert To World Text");
            int group = Undo.GetCurrentGroup();
            var rect = (RectTransform)go.transform;
            Undo.RecordObject(rect, "Convert To World Text");
            Vector2 size = rect.rect.size;
            Undo.DestroyObjectImmediate(text);
            var renderer = go.GetComponent<CanvasRenderer>();
            if (renderer != null) Undo.DestroyObjectImmediate(renderer);
            if (go.GetComponentInParent<Canvas>() != null) Undo.SetTransformParent(go.transform, null, "Convert To World Text");
            var world = Undo.AddComponent<LetteraWorldText>(go);
            EditorJsonUtility.FromJsonOverwrite(json, world);
            world.Color = color;
            world.FontSize /= PixelsPerWorldUnit;
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = size / PixelsPerWorldUnit;
            rect.localScale = Vector3.one;
            Undo.CollapseUndoOperations(group);
            return world;
        }

        internal static LetteraText ConvertToCanvas(LetteraWorldText text)
        {
            if (text == null || text.GetComponent<LetteraSelectable>() != null) return null;
            GameObject go = text.gameObject;
            string json = EditorJsonUtility.ToJson(text);
            Color color = text.Color;
            Undo.SetCurrentGroupName("Convert To Canvas Text");
            int group = Undo.GetCurrentGroup();
            var rect = (RectTransform)go.transform;
            Undo.RecordObject(rect, "Convert To Canvas Text");
            Vector2 size = rect.rect.size;
            Undo.DestroyObjectImmediate(text);
            if (go.GetComponentInParent<Canvas>() == null) Undo.SetTransformParent(go.transform, FindOrCreateCanvas(null), "Convert To Canvas Text");
            var canvasText = Undo.AddComponent<LetteraText>(go);
            EditorJsonUtility.FromJsonOverwrite(json, canvasText);
            canvasText.color = color;
            canvasText.FontSize *= PixelsPerWorldUnit;
            rect.localPosition = Vector3.zero;
            rect.localRotation = Quaternion.identity;
            rect.localScale = Vector3.one;
            rect.sizeDelta = size * PixelsPerWorldUnit;
            Undo.CollapseUndoOperations(group);
            return canvasText;
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

        internal static bool HasFontsSelected()
        {
            foreach (string _ in SelectedFontPaths()) return true;
            return false;
        }

        // Fonts for a stack: font assets, and font files, which become assets first.
        static List<LetteraFontAsset> SelectedStackFonts(bool create)
        {
            var fonts = new List<LetteraFontAsset>();
            foreach (string guid in Selection.assetGUIDs)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var asset = AssetDatabase.LoadAssetAtPath<LetteraFontAsset>(path);
                if (asset != null) fonts.Add(asset);
                else if (IsFontPath(path) && create) fonts.Add(CreateFontAsset<LetteraFont>(path));
                else if (IsFontPath(path)) fonts.Add(null);
            }
            return fonts;
        }

        internal static bool HasStackFontsSelected() => SelectedStackFonts(false).Count > 0;

        /// <summary>A stack from fonts: each its own family, tried in turn; or one family whose first is the primary and the rest its faces.</summary>
        internal static LetteraFontStack CreateStack(List<LetteraFontAsset> fonts, bool oneFamily, string path)
        {
            var stack = ScriptableObject.CreateInstance<LetteraFontStack>();
            if (oneFamily)
            {
                var family = new LetteraFontFamily { Primary = fonts[0] };
                for (int i = 1; i < fonts.Count; i++) family.Faces.Add(fonts[i]);
                stack.Families.Add(family);
            }
            else
            {
                foreach (LetteraFontAsset font in fonts) stack.Families.Add(new LetteraFontFamily { Primary = font });
            }
            if (path != null)
            {
                AssetDatabase.CreateAsset(stack, AssetDatabase.GenerateUniqueAssetPath(path));
                AssetDatabase.SaveAssets();
                Selection.activeObject = stack;
            }
            return stack;
        }

        static void StackFromSelection(bool oneFamily)
        {
            string first = AssetDatabase.GUIDToAssetPath(Selection.assetGUIDs[0]);
            List<LetteraFontAsset> fonts = SelectedStackFonts(true);
            CreateStack(fonts, oneFamily, Path.GetDirectoryName(first) + "/" + Path.GetFileNameWithoutExtension(first) + " Stack.asset");
        }

        [MenuItem("Assets/Create/Lettera/Font Stack From Selection, Each A Fallback", true)]
        static bool ValidateStackFallbacks() => HasStackFontsSelected();

        [MenuItem("Assets/Create/Lettera/Font Stack From Selection, Each A Fallback", false, 92)]
        static void StackFallbacks() => StackFromSelection(false);

        [MenuItem("Assets/Create/Lettera/Font Stack From Selection, One Family", true)]
        static bool ValidateStackFamily() => HasStackFontsSelected();

        [MenuItem("Assets/Create/Lettera/Font Stack From Selection, One Family", false, 93)]
        static void StackFamily() => StackFromSelection(true);

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

        // A scripting define on all targets. Turning one over recompiles everything, so it asks first.
        static VisualElement DefineRow(string label, string define, string tip)
        {
            var toggle = new UnityEngine.UIElements.Toggle { value = HasDefine(define) };
            toggle.RegisterValueChangedCallback(e =>
            {
                if (!EditorUtility.DisplayDialog(label, (e.newValue ? "Sets " : "Clears ") + define + " on every build target, which recompiles all scripts.", "Do It", "Cancel"))
                {
                    toggle.SetValueWithoutNotify(e.previousValue);
                    return;
                }
                SetDefine(define, e.newValue);
            });
            return LtUI.Row(label, toggle, tip);
        }

        static readonly UnityEditor.Build.NamedBuildTarget[] s_Targets =
        {
            UnityEditor.Build.NamedBuildTarget.Standalone, UnityEditor.Build.NamedBuildTarget.Android, UnityEditor.Build.NamedBuildTarget.iOS, UnityEditor.Build.NamedBuildTarget.WebGL,
        };

        static bool HasDefine(string define) => System.Array.IndexOf(PlayerSettings.GetScriptingDefineSymbols(s_Targets[0]).Split(';'), define) >= 0;

        static void SetDefine(string define, bool on)
        {
            foreach (UnityEditor.Build.NamedBuildTarget target in s_Targets)
            {
                var symbols = new List<string>(PlayerSettings.GetScriptingDefineSymbols(target).Split(new[] { ';' }, System.StringSplitOptions.RemoveEmptyEntries));
                symbols.Remove(define);
                if (on) symbols.Add(define);
                PlayerSettings.SetScriptingDefineSymbols(target, string.Join(";", symbols));
            }
        }

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
            page.Add(LtFrame.AllFields(serialized, "Text", "m_Math", "m_Solid3D", "m_Editing", "m_Motion", "m_Documents", "m_Platform", "m_FontMemory"));
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
            // What each gate means where this project is built right now. Words like "has not been seen to work" are there on purpose.
            foreach (LetteraNativeFeature feature in System.Enum.GetValues(typeof(LetteraNativeFeature)))
                platformRows.Add(LtUI.Row(feature + " Here", LtUI.Text(LetteraPlatform.Why(feature), "lt-row__value")));
            page.Add(LtUI.Card("Native Platform", platformRows.ToArray()));
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
            SerializedProperty math = serialized.FindProperty("m_Math");
            page.Add(LtUI.Card("Math",
                LtUI.Field("Font", math.FindPropertyRelative("Font"), "The font of formulas. It costs its size in every build once set here: 343 KB for the bundled one. Empty uses the text's font if it holds a math table, then a system math font, then fallback layout."),
                LtUI.Field("Scale", math.FindPropertyRelative("Scale"), "A formula against the text around it."),
                LtUI.Field("Errors", math.FindPropertyRelative("Errors"), "What a formula that cannot be read shows."),
                LtUI.Field("Delimiter Coverage", math.FindPropertyRelative("DelimiterFactor"), "How much a stretched delimiter covers of what it wraps."),
                LtUI.Field("Least Rule Width", math.FindPropertyRelative("MinRulePixels"), "The least a fraction bar draws, in device pixels. 0 turns this off."),
                LtUI.Field("Cache", math.FindPropertyRelative("CacheBytes"), "The most laid-out formulas kept, in bytes."),
                DefineRow("Compile Out Formulas", "LETTERA_NO_MATH", "Removes formula code from builds. <math> then shows its source as text.")));
            SerializedProperty solid = serialized.FindProperty("m_Solid3D");
            page.Add(LtUI.Card("3D",
                LtUI.Field("Default Material", solid.FindPropertyRelative("DefaultMaterial"), "For a Text 3D made by a script with no material. Set, it puts the solid shader in every build."),
                LtUI.Field("Solid Cache", solid.FindPropertyRelative("CacheBytes"), "The most glyph solids kept, in bytes."),
                LtUI.Field("Most Vertices", solid.FindPropertyRelative("MaxVertices"), "The most vertices one text draws."),
                LtUI.Text("Render pipeline: " + (UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline != null ? UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline.GetType().Name : "Built-in"), "lt-subtitle"),
                DefineRow("Compile Out 3D Text", "LETTERA_NO_3D", "Removes the 3D module's assemblies from this project.")));
            page.Add(LtUI.Card("Commands", new LetteraReferenceList(editing.FindPropertyRelative("Commands"), () => LetteraRuleCatalog.Commands)));
            return root;
        }
    }
}

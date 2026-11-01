using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace FirstSymbol.Lettera.Editor
{
    /// <summary>What this project would carry into a build, and why: answers "what does this cost me?" from the settings and the scenes in the build.</summary>
    internal static class LetteraBuildScan
    {
        internal struct Row
        {
            public string Kind, Name, Why;
            public bool Included;
            public long Bytes;
        }

        static long SizeOf(UnityEngine.Object asset)
        {
            if (asset is LetteraFont font && font.CompressedSize > 0) return font.CompressedSize;
            string path = asset != null ? AssetDatabase.GetAssetPath(asset) : null;
            return !string.IsNullOrEmpty(path) && File.Exists(path) ? new FileInfo(path).Length : 0;
        }

        static bool HasDefine(string define) => Array.IndexOf(PlayerSettings.GetScriptingDefineSymbols(UnityEditor.Build.NamedBuildTarget.Standalone).Split(';'), define) >= 0;

        /// <summary>The rows, from the settings. With scenes, also what each brings by reference.</summary>
        internal static List<Row> Rows(LetteraSettings settings, IEnumerable<string> scenes)
        {
            var rows = new List<Row>();
            void Setting(string kind, UnityEngine.Object asset, string name, string slot)
            {
                rows.Add(new Row { Kind = kind, Name = asset != null ? asset.name : name, Included = asset != null, Bytes = SizeOf(asset), Why = asset != null ? "Set as " + slot + " in these settings" : slot + " is empty" });
            }
            Setting("Font", settings.DefaultFont, "Default font", "Default Font");
            Setting("Font", settings.WebGLFallbackFont, "Web fallback font", "Web Fallback Font");
            Setting("Font", settings.Math.Font, "Math font", "the Math card's Font");
            Setting("Style Preset", settings.GlobalStylePreset, "Global style preset", "Global Style Preset");
            Setting("Paint Library", settings.Paints, "Paint library", "Paints");
            Setting("Shader", settings.Solid3D.DefaultMaterial != null ? settings.Solid3D.DefaultMaterial.shader : null, "Solid shader", "the 3D card's Default Material");
            foreach (LetteraWordDictionary dictionary in settings.Dictionaries)
                if (dictionary != null)
                    rows.Add(new Row { Kind = "Dictionary", Name = dictionary.name, Included = true, Bytes = dictionary.Data != null ? SizeOf(dictionary.Data) : 0, Why = "Listed in these settings" });
            foreach (string shader in new[] { "LetteraUI", "LetteraWorld", "LetteraWorldLit" })
                rows.Add(new Row { Kind = "Shader", Name = shader, Included = true, Bytes = 0, Why = "Always: it lives in the package's Resources" });
            rows.Add(new Row { Kind = "Font", Name = "LetteraWebFont", Included = true, Bytes = 0, Why = "Always: it lives in the package's Resources, for the web" });
            bool math = !HasDefine("LETTERA_NO_MATH"), solid = !HasDefine("LETTERA_NO_3D");
            rows.Add(new Row { Kind = "Module", Name = "Formulas", Included = math, Why = math ? "On unless LETTERA_NO_MATH is set" : "LETTERA_NO_MATH is set" });
            rows.Add(new Row { Kind = "Module", Name = "3D text", Included = solid, Why = solid ? "On unless LETTERA_NO_3D is set" : "LETTERA_NO_3D is set" });
            rows.Add(new Row { Kind = "Module", Name = "Inspect overlay", Included = false, Why = "Only in development builds" });
            foreach (string scene in scenes ?? Enumerable.Empty<string>())
            {
                foreach (string dependency in AssetDatabase.GetDependencies(scene, true))
                {
                    UnityEngine.Object asset = AssetDatabase.LoadMainAssetAtPath(dependency);
                    string kind = asset is LetteraFontAsset ? "Font" : asset is LetteraWordDictionary ? "Dictionary" : asset is Shader && dependency.Contains("Lettera") ? "Shader" : asset is LetteraStylePreset ? "Style Preset" : asset is LetteraPaintLibrary ? "Paint Library" : null;
                    if (kind == null || rows.Any(r => r.Name == asset.name && r.Included)) continue;
                    rows.Add(new Row { Kind = kind, Name = asset.name, Included = true, Bytes = SizeOf(asset), Why = "Used by " + Path.GetFileNameWithoutExtension(scene) });
                }
            }
            rows.Sort((a, b) => a.Included != b.Included ? (a.Included ? -1 : 1) : string.CompareOrdinal(a.Kind, b.Kind));
            return rows;
        }

        internal static IEnumerable<string> ScenesInBuild() => EditorBuildSettings.scenes.Where(s => s.enabled && File.Exists(s.path)).Select(s => s.path);

        public static VisualElement Table(LetteraSettings settings)
        {
            VisualElement table = LtUI.El("lt-build");
            Label sum = LtUI.Text("", "lt-hint");
            void Fill(IEnumerable<string> scenes)
            {
                table.Clear();
                List<Row> rows = Rows(settings, scenes);
                foreach (Row row in rows)
                {
                    VisualElement line = LtUI.El("lt-build__row", LtUI.El(row.Included ? "lt-status__dot lt-status__dot--good" : "lt-status__dot"), LtUI.Text(row.Kind + ": " + row.Name, "lt-build__name"), LtUI.Text(row.Bytes > 0 ? row.Bytes / 1024 + " KB" : "", "lt-build__size"));
                    line.tooltip = row.Why;
                    VisualElement holder = LtUI.El("lt-build__item", line, LtUI.Text(row.Why, "lt-build__why"));
                    table.Add(holder);
                }
                long total = rows.Where(r => r.Included).Sum(r => r.Bytes);
                sum.text = rows.Count(r => r.Included) + " included, " + total / 1024 + " KB that can be counted. Native libraries and code are not in this sum.";
            }
            Fill(null);
            return LtUI.El("lt-list", sum, table, LtUI.Clickable("Rescan Scenes", "lt-add lt-add--small", "Reads what the scenes in the build refer to, which may take a moment.", () => Fill(ScenesInBuild())));
        }
    }

    /// <summary>Project Settings > Lettera, as cards that close, with a filter.</summary>
    internal static class LetteraSettingsPage
    {
        static void AddAll(VisualElement card, SerializedProperty group, params string[] skip)
        {
            SerializedProperty child = group.Copy();
            SerializedProperty end = group.GetEndProperty();
            bool enter = true;
            while (child.NextVisible(enter) && !SerializedProperty.EqualContents(child, end))
            {
                enter = false;
                if (Array.IndexOf(skip, child.name) >= 0) continue;
                card.Add(LtFrame.Row(child.Copy()));
            }
        }

        static void Fields(VisualElement card, SerializedObject serialized, params (string label, string field)[] rows)
        {
            foreach ((string label, string field) in rows)
            {
                SerializedProperty property = serialized.FindProperty(field);
                if (property != null) card.Add(LtUI.Field(label, property, property.tooltip));
            }
        }

        internal static VisualElement Build(LetteraSettings settings, Func<string, string, string, VisualElement> defineRow)
        {
            VisualElement root = LtUI.Root();
            VisualElement page = LtFrame.Header(root, "Lettera", "Project-wide defaults");
            var serialized = new SerializedObject(settings);
            var sections = new List<LtSection>();
            var filter = new LtSearch("Find a setting");
            filter.Changed += query => LtFilter.Apply(sections, query);
            page.Add(filter);

            LtSection Section(string key, string title, params VisualElement[] children)
            {
                var section = new LtSection("settings." + key, title).With(children);
                sections.Add(section);
                page.Add(section);
                return section;
            }

            VisualElement defaults = LtUI.El("lt-list");
            Fields(defaults, serialized,
                ("Default Font", "m_DefaultFont"), ("No System Fallback", "m_DisableSystemFontFallback"), ("No Emoji", "m_DisableEmoji"), ("Global Style Preset", "m_GlobalStylePreset"),
                ("Line Height Mode", "m_DefaultLineHeightMode"), ("Line Height", "m_DefaultLineHeightValue"), ("Size Match", "m_FontSizeMatchMetric"), ("Web Fallback Font", "m_WebGLFallbackFont"), ("Paints", "m_Paints"), ("Dictionaries", "m_Dictionaries"));
            VisualElement prefabs = LtUI.El("lt-group", LtUI.Text("A filled slot is what creating instantiates, and Add Component seeds from it.", "lt-hint"));
            Fields(prefabs, serialized, ("Text", "m_TextPrefab"), ("World Text", "m_WorldTextPrefab"), ("Text 3D", "m_Text3DPrefab"), ("Button", "m_ButtonPrefab"), ("Document", "m_DocumentPrefab"));
            defaults.Add(prefabs);
            Section("defaults", "Defaults", defaults);

            VisualElement editing = LtUI.El("lt-list");
            SerializedProperty editingGroup = serialized.FindProperty("m_Editing");
            AddAll(editing, editingGroup, "Commands");
            Section("editing", "Selection & Editing", editing);

            VisualElement motion = LtUI.El("lt-list");
            AddAll(motion, serialized.FindProperty("m_Motion"));
            Section("motion", "Animation & Interaction", motion);

            VisualElement documents = LtUI.El("lt-list");
            AddAll(documents, serialized.FindProperty("m_Documents"));
            Section("documents", "Documents", documents);

            VisualElement platform = LtUI.El("lt-list");
            AddAll(platform, serialized.FindProperty("m_Platform"));
            // What each gate means where this project is built right now. Words like "has not been seen to work" are there on purpose.
            foreach (LetteraNativeFeature feature in Enum.GetValues(typeof(LetteraNativeFeature)))
                platform.Add(LtUI.Row(feature + " Here", LtUI.Text(LetteraPlatform.Why(feature), "lt-row__value")));
            Section("platform", "Native Platform", platform);

            VisualElement fonts = LtUI.El("lt-list");
            AddAll(fonts, serialized.FindProperty("m_FontMemory"));
            Section("fontmemory", "Font Memory", fonts);

            VisualElement atlases = LtUI.El("lt-list", new LtStatus()
                .Line("Page Size", () => "2048 by 2048, fixed")
                .Line("Depth", () => "SDF 8-bit, MSDF 8-bit, color RGBA, fixed")
                .Line("In Use Now", () =>
                {
                    long bytes = 0;
                    int pages = 0;
                    for (int set = 0; set < 3; set++)
                    {
                        Texture2DArray array = GlyphAtlas.TextureOf(set);
                        if (array == null) continue;
                        pages += array.depth;
                        bytes += UnityEngine.Profiling.Profiler.GetRuntimeMemorySizeLong(array);
                    }
                    return pages + " pages, " + bytes / 1024 + " KB";
                })
                .RefreshNow(), LtUI.Clickable("Open Atlases", "lt-add lt-add--small", "The pages themselves, with what each holds.", LetteraAtlasesWindow.Open));
            Section("atlases", "Atlases", atlases);

            SerializedProperty math = serialized.FindProperty("m_Math");
            Section("math", "Math",
                LtUI.Field("Font", math.FindPropertyRelative("Font"), "The font of formulas. It costs its size in every build once set here: 343 KB for the bundled one. Empty uses the text's font if it holds a math table, then a system math font, then fallback layout."),
                LtUI.Field("Scale", math.FindPropertyRelative("Scale"), "A formula against the text around it."),
                LtUI.Field("Errors", math.FindPropertyRelative("Errors"), "What a formula that cannot be read shows."),
                LtUI.Field("Delimiter Coverage", math.FindPropertyRelative("DelimiterFactor"), "How much a stretched delimiter covers of what it wraps."),
                LtUI.Field("Least Rule Width", math.FindPropertyRelative("MinRulePixels"), "The least a fraction bar draws, in device pixels. 0 turns this off."),
                LtUI.Field("Cache", math.FindPropertyRelative("CacheBytes"), "The most laid-out formulas kept, in bytes."),
                defineRow("Compile Out Formulas", "LETTERA_NO_MATH", "Removes formula code from builds. <math> then shows its source as text."));

            SerializedProperty solid = serialized.FindProperty("m_Solid3D");
            Section("solid", "3D",
                LtUI.Field("Default Material", solid.FindPropertyRelative("DefaultMaterial"), "For a Text 3D made by a script with no material. Set, it puts the solid shader in every build."),
                LtUI.Field("Solid Cache", solid.FindPropertyRelative("CacheBytes"), "The most glyph solids kept, in bytes."),
                LtUI.Field("Most Vertices", solid.FindPropertyRelative("MaxVertices"), "The most vertices one text draws."),
                LtUI.Text("Render pipeline: " + (UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline != null ? UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline.GetType().Name : "Built-in"), "lt-subtitle"),
                defineRow("Compile Out 3D Text", "LETTERA_NO_3D", "Removes the 3D module's assemblies from this project."));

            Section("commands", "Commands", new LetteraReferenceList(editingGroup.FindPropertyRelative("Commands"), () => LetteraRuleCatalog.Commands, null, "Commands", "No commands: menus stay empty."));
            Section("build", "In Your Build", LetteraBuildScan.Table(settings));
            Section("tools", "Tools",
                LtUI.Row("Scene Outlines", LtUI.El("lt-switch-holder", LtUI.Toggle(() => LetteraSceneOptions.Outlines, v => LetteraSceneOptions.Outlines = v)), "Padding, pointer area and ink on a selected text, in the Scene view. Yours alone: it is kept per user."),
                LtUI.Row("Follow The Hierarchy's Eye", LtUI.El("lt-switch-holder", LtUI.Toggle(() => LetteraSceneOptions.FollowVisibility, v => LetteraSceneOptions.FollowVisibility = v)), "Hiding an object with the eye hides its batched world text too, until Play. Yours alone: it is kept per user."),
                LtUI.Row("Shift And Right Click Opens The Palette", LtUI.El("lt-switch-holder", LtUI.Toggle(() => LetteraPaletteHooks.Enabled, v => LetteraPaletteHooks.Enabled = v)), "In the Hierarchy, the Scene view and the Project window. Yours alone: it is kept per user."));
            page.Add(LtUI.Clickable("Select Settings Asset", "lt-add lt-add--small", "Shows the asset these settings live in, in the Project window.", () =>
            {
                Selection.activeObject = settings;
                EditorGUIUtility.PingObject(settings);
            }));
            return root;
        }
    }
}

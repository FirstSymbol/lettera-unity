using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.PackageManager.UI;
using UnityEngine;
using UnityEngine.UIElements;

namespace FirstSymbol.Lettera.Editor
{
    /// <summary>Where to start: the guides, the samples, the migration tool and every window. A new project is told once, by a line in the console and no popup.</summary>
    internal sealed class LetteraGettingStarted : LetteraToolWindow
    {
        internal const string PackageName = "com.firstsymbol.lettera";
        const string OnlineDocs = "https://github.com/FirstSymbol/lettera-unity/blob/main/Documentation~/";
        const string ToldKey = "Lettera.GettingStartedTold";

        internal static readonly (string file, string title, string what, bool russian)[] Guides =
        {
            ("Lettera", "Lettera", "What it is, installing, five minutes", true),
            ("GettingStarted", "Getting Started", "Text on screen, fonts, markup, styles, fields, motion, documents, math, 3D", true),
            ("Fonts", "Fonts", "Assets, stacks, system fonts, memory, subsets, pixel and color fonts, the web", true),
            ("Styles", "Styles", "Matchers and effects, presets, paints, your own effect and shader", true),
            ("Fields", "Fields", "Selection, editing, rules, clipboard, composition, native routes", true),
            ("Motion", "Motion", "Motions, the sequencer, Timeline, ranges, interaction, inline", true),
            ("Documents", "Documents", "Very long text: the rope, the window, the scroller, search", true),
            ("Math", "Math", "Formulas in text and by themselves", true),
            ("Text3D", "3D Text", "Solid lit text", true),
            ("Tools", "Tools", "Every window, the overlay, editing in the scene, the palette, the keys", true),
            ("Migration", "Migration", "From TextMesh Pro: the order, what converts, what does not", true),
            ("Comparison", "Comparison", "Against TextMesh Pro, measured", true),
            ("Platforms", "Platforms", "What has run where, plainly", true),
            ("AgentGuide", "Agent Guide", "Lettera from the command line, for a coding agent", false),
        };

        internal static readonly (string title, string menu, string what)[] Windows =
        {
            ("Commands", "Tools/FirstSymbol/Lettera/Commands...", "Everything here and more, by typing. Ctrl+Alt+L"),
            ("Glyphs", "Tools/FirstSymbol/Lettera/Glyphs", "What a font and the shaper made of a text"),
            ("Ranges", "Tools/FirstSymbol/Lettera/Ranges", "Which styles reach which characters, and problems with a fix"),
            ("Atlases", "Tools/FirstSymbol/Lettera/Atlases", "What the atlases hold and what they cost"),
            ("Clipboard", "Tools/FirstSymbol/Lettera/Clipboard", "Every format a copy wrote"),
            ("Font Tools", "Tools/FirstSymbol/Lettera/Font Tools", "Font assets, subsets and dictionaries in one place"),
            ("Sequencer", "Tools/FirstSymbol/Lettera/Sequencer", "Cues on rows"),
            ("Inspect Text In Scene", "Tools/FirstSymbol/Lettera/Inspect Text In Scene", "Boxes and baselines over a text"),
            ("Edit Text In Scene", "Tools/FirstSymbol/Lettera/Edit Text In Scene", "Type into a text where it stands. F2"),
            ("Repair Saved Objects", "Tools/FirstSymbol/Lettera/Repair Saved Objects", "Assets from an older version that came back as missing scripts"),
        };

        [MenuItem("Tools/FirstSymbol/Lettera/Getting Started", false, 1)]
        internal static void Open() => GetWindow<LetteraGettingStarted>("Lettera").minSize = new Vector2(320, 360);

        protected override string Title => "Getting Started";
        protected override string Subtitle => "The guides, the samples, and where each tool lives";

        [InitializeOnLoadMethod]
        static void TellOnce()
        {
            if (Application.isBatchMode || EditorUserSettings.GetConfigValue(ToldKey) == "1") return;
            EditorUserSettings.SetConfigValue(ToldKey, "1");
            EditorApplication.delayCall += () => Debug.Log("Lettera is installed. Tools > FirstSymbol > Lettera > Getting Started has the guides, the samples and the migration tool.");
        }

        protected override void Build()
        {
            Page.Add(LtUI.Card("From TextMesh Pro",
                LtUI.Text("Scans a project, says what will change, and converts it with a way back.", "lt-hint"),
                Button("Open The Migration Tool", "Tools > FirstSymbol > Lettera > Migrate From TextMesh Pro", () => EditorApplication.ExecuteMenuItem("Tools/FirstSymbol/Lettera/Migrate From TextMesh Pro"))));

            VisualElement guides = LtUI.Card("Guides");
            foreach ((string file, string title, string what, bool russian) in Guides)
            {
                VisualElement row = Line(title, what);
                row.Add(Button("Read", "Opens " + file + ".md", () => OpenGuide(file + ".md")));
                if (russian) row.Add(Button("RU", "Opens " + file + ".ru.md", () => OpenGuide(file + ".ru.md")));
                guides.Add(row);
            }
            Page.Add(guides);

            VisualElement samples = LtUI.Card("Samples");
            IEnumerable<Sample> found = Sample.FindByPackage(PackageName, null);
            bool any = false;
            foreach (Sample sample in found)
            {
                any = true;
                samples.Add(SampleRow(sample));
            }
            if (!any) samples.Add(LtUI.Text("No samples found. Window > Package Manager has them under Lettera.", "lt-hint"));
            Page.Add(samples);

            VisualElement tools = LtUI.Card("Windows");
            foreach ((string title, string menu, string what) in Windows)
            {
                VisualElement row = Line(title, what);
                row.Add(Button("Open", menu.Replace("/", " > "), () => EditorApplication.ExecuteMenuItem(menu)));
                tools.Add(row);
            }
            Page.Add(tools);

            Page.Add(LtUI.Card("Settings",
                Button("Project Settings", "Project Settings > FirstSymbol > Lettera", () => SettingsService.OpenProjectSettings("Project/FirstSymbol/Lettera")),
                Button("Preferences", "Preferences > Lettera", () => SettingsService.OpenUserPreferences("Preferences/Lettera"))));
        }

        static VisualElement SampleRow(Sample sample)
        {
            VisualElement row = Line(sample.displayName, sample.description);
            Label act = null;
            void Refresh()
            {
                act.text = sample.isImported ? "Show" : "Import";
                act.tooltip = sample.isImported ? "Finds it in the Project window." : "Copies it into Assets/Samples.";
            }
            act = Button("", "", () =>
            {
                if (!sample.isImported)
                {
                    sample.Import(Sample.ImportOptions.HideImportWindow);
                    AssetDatabase.Refresh();
                    Refresh();
                    return;
                }
                string path = sample.importPath.Replace('\\', '/');
                int at = path.IndexOf("/Assets/", System.StringComparison.Ordinal);
                if (at >= 0) EditorGUIUtility.PingObject(AssetDatabase.LoadMainAssetAtPath(path.Substring(at + 1)));
            });
            Refresh();
            row.Add(act);
            return row;
        }

        static VisualElement Line(string title, string what)
        {
            VisualElement row = LtUI.El("lt-start");
            VisualElement words = LtUI.El("lt-start__words", LtUI.Text(title, "lt-row__value"));
            Label hint = LtUI.Text(what, "lt-hint");
            hint.style.whiteSpace = WhiteSpace.Normal;
            words.Add(hint);
            row.Add(words);
            return row;
        }

        static Label Button(string text, string tooltip, System.Action action) => LtUI.Clickable(text, "lt-add lt-add--small", tooltip, action);

        /// <summary>The copy in the package when there is one, else the repository's.</summary>
        internal static string GuideAddress(string file)
        {
            var package = UnityEditor.PackageManager.PackageInfo.FindForAssetPath("Packages/" + PackageName);
            string local = package == null ? null : Path.Combine(package.resolvedPath, "Documentation~", file);
            return local != null && File.Exists(local) ? "file:///" + local.Replace('\\', '/') : OnlineDocs + file;
        }

        static void OpenGuide(string file) => Application.OpenURL(GuideAddress(file));
    }
}

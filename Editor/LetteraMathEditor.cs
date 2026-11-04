#if !LETTERA_NO_MATH
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace FirstSymbol.Lettera.Editor
{
    /// <summary>Choosing the font formulas use, and saying so before a build where they would look worse.</summary>
    internal static class LetteraMathMenu
    {
        internal const string BundledGuidName = "LetteraMathNotoSans";

        internal static LetteraFontAsset BundledFont()
        {
            foreach (string guid in AssetDatabase.FindAssets(BundledGuidName + " t:LetteraFont"))
            {
                var font = AssetDatabase.LoadAssetAtPath<LetteraFontAsset>(AssetDatabase.GUIDToAssetPath(guid));
                if (font != null) return font;
            }
            return null;
        }

        [MenuItem("Lettera/Math/Use Bundled Font")]
        static void UseBundledFont()
        {
            LetteraFontAsset font = BundledFont();
            if (font == null)
            {
                Debug.LogError("Lettera: the bundled math font is not in the package.");
                return;
            }
            if (!EditorUtility.DisplayDialog("Use the bundled math font", "Noto Sans Math becomes the font of every formula in this project. It costs 343 KB in every build, on every platform, whether a formula is shown or not.\n\nA font set on one style costs only where that style is used.", "Use It", "Cancel")) return;
            Apply(font);
        }

        internal static void Apply(LetteraFontAsset font)
        {
            LetteraSettings settings = LetteraSettings.Instance;
            Undo.RecordObject(settings, "Use Bundled Math Font");
            settings.Math.Font = font;
            EditorUtility.SetDirty(settings);
            LetteraMathFont.ForgetAll();
        }
    }

    /// <summary>A formula on a platform with no math font lays out with fallback spacing. That is worth one line in the build log, and is never a failure.</summary>
    internal sealed class LetteraMathBuildCheck : IPreprocessBuildWithReport
    {
        public int callbackOrder => 0;

        public void OnPreprocessBuild(BuildReport report)
        {
            string warning = Check(report.summary.platform, ScenesInBuild());
            if (warning != null) Debug.LogWarning(warning);
        }

        static IEnumerable<string> ScenesInBuild()
        {
            foreach (EditorBuildSettingsScene scene in EditorBuildSettings.scenes)
                if (scene.enabled)
                    yield return scene.path;
        }

        internal static string Check(BuildTarget target, IEnumerable<string> scenes)
        {
            if (target != BuildTarget.WebGL && target != BuildTarget.Android && target != BuildTarget.iOS && target != BuildTarget.tvOS && target != BuildTarget.VisionOS) return null;
            if (LetteraSettings.Instance.Math.Font != null) return null;
            foreach (string path in scenes)
                if (MentionsMath(path))
                    return $"Lettera: {path} holds a formula, and {target} has no system math font. Its formulas will use fallback layout. Lettera > Math > Use Bundled Font sets one, or a math font on the formula's style.";
            return null;
        }

        // Reads the scene as text, a line at a time: no scene is loaded and nothing is held.
        internal static bool MentionsMath(string path)
        {
            if (!File.Exists(path)) return false;
            using (var reader = new StreamReader(path))
            {
                string line;
                while ((line = reader.ReadLine()) != null)
                    if (line.IndexOf("<math", System.StringComparison.Ordinal) >= 0 || line.IndexOf("LetteraMathEffect", System.StringComparison.Ordinal) >= 0)
                        return true;
            }
            return false;
        }
    }
}
#endif

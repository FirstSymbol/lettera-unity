using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace FirstSymbol.Lettera.Editor
{
    /// <summary>Puts the fonts of a build into files of their own beside it, where the target packs them. No asset on disk changes.</summary>
    internal sealed class LetteraFontBuildProcessor : BuildPlayerProcessor, IPostprocessBuildWithReport
    {
        const string Folder = "Library/LetteraPayloads";

        public override int callbackOrder => 0;

        public override void PrepareForBuild(BuildPlayerContext context)
        {
            Clear();
            // A build that fails never reaches the step after it, and a later bundle build must not find these sets still filled.
            EditorApplication.delayCall += Clear;
            LetteraFont web = WebFontLeftOut(context.BuildPlayerOptions.target);
            if (web != null) LetteraFont.LeftOutOfThisBuild.Add(web);
            LetteraFontPackaging setting = LetteraSettings.Instance.FontMemory.Packaging(context.BuildPlayerOptions.target);
            if (setting != LetteraFontPackaging.Packed) return;

            Directory.CreateDirectory(Folder);
            var catalog = new StringBuilder();
            foreach (LetteraFont font in FindFonts(context.BuildPlayerOptions.scenes))
            {
                if (font.KeepInline || !font.HasData || LetteraFont.LeftOutOfThisBuild.Contains(font)) continue;
                byte[] payload = font.PayloadForBuild();
                if (payload == null) continue;
                string hash = LetteraFont.HashOf(payload);
                string path = Path.Combine(Folder, hash + ".ltp");
                if (!File.Exists(path)) File.WriteAllBytes(path, payload);
                if (LetteraFont.PackedInThisBuild.Add(hash))
                {
                    context.AddAdditionalPathToStreamingAssets(path, LetteraFontCache.PackedFolder + "/" + hash + ".ltp");
                    catalog.Append(hash).Append(' ').Append(font.name).Append('\n');
                }
            }
            if (catalog.Length == 0) return;
            string list = Path.Combine(Folder, "catalog.txt");
            File.WriteAllText(list, catalog.ToString());
            context.AddAdditionalPathToStreamingAssets(list, LetteraFontCache.PackedFolder + "/catalog.txt");
        }

        public void OnPostprocessBuild(BuildReport report) => Clear();

        static void Clear()
        {
            LetteraFont.PackedInThisBuild.Clear();
            LetteraFont.LeftOutOfThisBuild.Clear();
        }

        /// <summary>
        /// The package's font for browsers, where a target always has fonts of its own: a text there reaches it only when the system offers
        /// none. Linux keeps it, for a machine with no fonts installed, and so does a project that names it as its web fallback.
        /// </summary>
        internal static LetteraFont WebFontLeftOut(BuildTarget target)
        {
            if (target != BuildTarget.StandaloneWindows && target != BuildTarget.StandaloneWindows64 && target != BuildTarget.StandaloneOSX
                && target != BuildTarget.Android && target != BuildTarget.iOS && target != BuildTarget.tvOS && target != BuildTarget.VisionOS) return null;
            var web = Resources.Load<LetteraFont>(FontChain.WebFontResource);
            if (web == null || LetteraSettings.Instance.WebGLFallbackFont == web || LetteraSettings.Instance.DefaultFont == web) return null;
            return web;
        }

        // A font missed here stays inside its asset, and still works.
        static IEnumerable<LetteraFont> FindFonts(string[] scenes)
        {
            var roots = new List<string>();
            if (scenes != null) roots.AddRange(scenes);
            string settings = AssetDatabase.GetAssetPath(LetteraSettings.Instance);
            if (!string.IsNullOrEmpty(settings)) roots.Add(settings);
            foreach (string guid in AssetDatabase.FindAssets("t:Object", new[] { "Assets" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (path.Contains("/Resources/")) roots.Add(path);
            }
            foreach (string guid in AssetDatabase.FindAssets("t:LetteraFont"))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (path.Contains("/Resources/")) roots.Add(path);
            }
            var seen = new HashSet<string>();
            foreach (string dependency in AssetDatabase.GetDependencies(roots.ToArray(), true))
            {
                if (!seen.Add(dependency) || !dependency.EndsWith(".asset")) continue;
                if (AssetDatabase.GetMainAssetTypeAtPath(dependency) == null) continue;
                var font = AssetDatabase.LoadAssetAtPath<LetteraFont>(dependency);
                if (font != null) yield return font;
            }
        }
    }
}

using System;
using UnityEditor;

namespace FirstSymbol.Lettera.Editor
{
    /// <summary>
    /// A Windows player takes the native library of the architecture it is built for. Unity copies a plugin of every CPU a platform has
    /// into each build, so an Intel 64-bit player carried the ARM64 library too, 1.4 MB it can never load.
    /// </summary>
    [InitializeOnLoad]
    static class LetteraPluginFilter
    {
        const string Arm64 = "bd0ba913dcf0d044eb5a878d9b8e4c1d", Intel64 = "392c6439636f97c4d932df360bc872e6";

        static LetteraPluginFilter()
        {
            Filter(Arm64, "ARM64");
            Filter(Intel64, "x64");
        }

        static void Filter(string guid, string architecture)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (string.IsNullOrEmpty(path) || !(AssetImporter.GetAtPath(path) is PluginImporter importer)) return;
            importer.SetIncludeInBuildDelegate(_ =>
            {
                string built = WindowsArchitecture();
                // Unknown, or both in one player: keep it, as before.
                return string.IsNullOrEmpty(built) || built.IndexOf(architecture, StringComparison.Ordinal) >= 0;
            });
        }

        // What the active Windows build settings say, as x64, ARM64 or x64ARM64; empty when it cannot be read.
        static string WindowsArchitecture()
        {
            try
            {
                foreach (System.Reflection.Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
                {
                    Type type = assembly.GetType("UnityEditor.WindowsStandalone.UserBuildSettings", false);
                    object value = type?.GetProperty("architecture", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)?.GetValue(null);
                    if (value != null) return value.ToString();
                }
            }
            catch (Exception)
            {
                // Read below instead.
            }
            return EditorUserBuildSettings.GetPlatformSettings("Standalone", "Architecture") ?? "";
        }
    }
}

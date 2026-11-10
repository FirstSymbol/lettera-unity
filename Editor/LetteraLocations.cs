using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Compilation;

namespace FirstSymbol.Lettera.Editor
{
    /// <summary>Where Lettera's own files are. It may be a package or a plain folder under Assets, so nothing is found by a fixed path.</summary>
    internal static class LetteraLocations
    {
        const string RuntimeAssembly = "FirstSymbol.Lettera";
        static string s_Root;

        /// <summary>The project-relative folder that holds package.json: Packages/com.firstsymbol.lettera or a folder under Assets.</summary>
        internal static string Root
        {
            get
            {
                if (s_Root != null && File.Exists(Path.Combine(s_Root, "package.json"))) return s_Root;
                string asmdef = CompilationPipeline.GetAssemblyDefinitionFilePathFromAssemblyName(RuntimeAssembly);
                string dir = string.IsNullOrEmpty(asmdef) ? null : Path.GetDirectoryName(asmdef).Replace('\\', '/');
                while (!string.IsNullOrEmpty(dir))
                {
                    if (File.Exists(Path.Combine(AbsoluteOf(dir), "package.json"))) return s_Root = dir;
                    int slash = dir.LastIndexOf('/');
                    if (slash <= 0) break;
                    dir = dir.Substring(0, slash);
                }
                return s_Root = null;
            }
        }

        /// <summary>The folder on disk, or null when Lettera cannot be located.</summary>
        internal static string AbsoluteRoot => Root == null ? null : AbsoluteOf(Root);

        /// <summary>True when Lettera is installed as a package rather than as a folder under Assets.</summary>
        internal static bool IsPackage => Root != null && Root.StartsWith("Packages/", System.StringComparison.Ordinal);

        /// <summary>A path inside Lettera, project-relative, for the AssetDatabase.</summary>
        internal static string AssetPath(string relative) => Root + "/" + relative;

        /// <summary>A path inside Lettera, on disk.</summary>
        internal static string DiskPath(string relative) => Path.Combine(AbsoluteRoot, relative);

        static string AbsoluteOf(string projectRelative)
        {
            var info = UnityEditor.PackageManager.PackageInfo.FindForAssetPath(projectRelative);
            if (info != null && !string.IsNullOrEmpty(info.resolvedPath)) return info.resolvedPath;
            return Path.GetFullPath(projectRelative);
        }

        /// <summary>A sample the package ships: from the package manager when this is a package, else read from package.json.</summary>
        internal sealed class SampleInfo
        {
            public string DisplayName;
            public string Description;
            public string SourceFolder;
            public string ImportPath;
            public bool IsImported => AssetDatabase.IsValidFolder(ImportPath) || Directory.Exists(ImportPath);
            public System.Action Import;
        }

        [System.Serializable] class SampleEntry { public string displayName; public string description; public string path; }
        [System.Serializable] class Manifest { public string version; public SampleEntry[] samples; }

        internal static List<SampleInfo> Samples()
        {
            var list = new List<SampleInfo>();
            if (IsPackage)
            {
                foreach (var sample in UnityEditor.PackageManager.UI.Sample.FindByPackage(LetteraGettingStarted.PackageName, null))
                {
                    var s = sample;
                    list.Add(new SampleInfo
                    {
                        DisplayName = s.displayName,
                        Description = s.description,
                        ImportPath = s.importPath.Replace('\\', '/'),
                        Import = () => s.Import(UnityEditor.PackageManager.UI.Sample.ImportOptions.HideImportWindow),
                    });
                }
                if (list.Count > 0) return list;
            }
            string root = AbsoluteRoot;
            if (root == null || !File.Exists(Path.Combine(root, "package.json"))) return list;
            var manifest = UnityEngine.JsonUtility.FromJson<Manifest>(File.ReadAllText(Path.Combine(root, "package.json")));
            if (manifest == null || manifest.samples == null) return list;
            foreach (SampleEntry entry in manifest.samples)
            {
                string source = Path.Combine(root, entry.path);
                if (!Directory.Exists(source)) continue;
                string target = "Assets/Samples/Lettera/" + manifest.version + "/" + entry.displayName;
                list.Add(new SampleInfo
                {
                    DisplayName = entry.displayName,
                    Description = entry.description,
                    SourceFolder = source,
                    ImportPath = target,
                    Import = () => CopyFolder(source, target),
                });
            }
            return list;
        }

        static void CopyFolder(string source, string target)
        {
            Directory.CreateDirectory(target);
            foreach (string file in Directory.GetFiles(source))
            {
                if (file.EndsWith(".meta", System.StringComparison.Ordinal)) continue;
                File.Copy(file, Path.Combine(target, Path.GetFileName(file)), true);
            }
            foreach (string dir in Directory.GetDirectories(source)) CopyFolder(dir, Path.Combine(target, Path.GetFileName(dir)));
        }
    }
}

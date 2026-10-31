#if UNITY_IOS
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.iOS.Xcode;

namespace FirstSymbol.Lettera.Editor
{
    /// <summary>
    /// An iOS build holds the stub by default, so code nothing here could compile cannot fail a project's Xcode build. Turning IosNativeInBuild on swaps the real file in, in the exported project alone,
    /// which works however the package is installed, since an installed package's own settings cannot be changed.
    /// </summary>
    internal sealed class LetteraIosBuildProcessor : IPostprocessBuildWithReport
    {
        const string Real = "LetteraPlatformIOS.mm", Stub = "LetteraPlatformIOSStub.mm";

        public int callbackOrder => 100;

        public void OnPostprocessBuild(BuildReport report)
        {
            if (report.summary.platform != BuildTarget.iOS || !LetteraSettings.Instance.Platform.IosNativeInBuild) return;
            string root = report.summary.outputPath;
            string projectPath = PBXProject.GetPBXProjectPath(root);
            var project = new PBXProject();
            project.ReadFromFile(projectPath);
            string target = project.GetUnityFrameworkTargetGuid();

            foreach (string guid in AssetDatabase.FindAssets("LetteraPlatformIOSStub"))
            {
                string stubPath = AssetDatabase.GUIDToAssetPath(guid);
                if (!stubPath.EndsWith(Stub)) continue;
                string source = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(stubPath)), Real);
                const string exported = "Libraries/Lettera/" + Real;
                Directory.CreateDirectory(Path.Combine(root, "Libraries", "Lettera"));
                File.Copy(source, Path.Combine(root, exported), true);
                project.AddFileToBuild(target, project.AddFile(exported, exported));
                foreach (string file in Directory.GetFiles(root, Stub, SearchOption.AllDirectories))
                {
                    string relative = file.Substring(root.Length).TrimStart('/', '\\').Replace('\\', '/');
                    string existing = project.FindFileGuidByProjectPath(relative);
                    if (existing != null) project.RemoveFile(existing);
                    File.Delete(file);
                }
                break;
            }
            project.WriteToFile(projectPath);
        }
    }
}
#endif

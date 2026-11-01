using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace FirstSymbol.Lettera.Editor
{
    /// <summary>Builds the device check with every native route on, for that build only: the project's own settings come back as they were.</summary>
    internal static class LetteraDeviceCheckMenu
    {
        const string SceneFolder = "Assets/LetteraDeviceCheckBuild";

        [MenuItem("Lettera/Device Check/Build for Android")] static void Android() => Build(BuildTarget.Android, BuildTargetGroup.Android, "lettera-device-check.apk");
        [MenuItem("Lettera/Device Check/Build for iOS")] static void Ios() => Build(BuildTarget.iOS, BuildTargetGroup.iOS, "lettera-device-check-ios");
        [MenuItem("Lettera/Device Check/Build for Web")] static void Web() => Build(BuildTarget.WebGL, BuildTargetGroup.WebGL, "lettera-device-check-web");
        [MenuItem("Lettera/Device Check/Build for Windows")] static void Windows() => Build(BuildTarget.StandaloneWindows64, BuildTargetGroup.Standalone, "lettera-device-check-windows/check.exe");
        [MenuItem("Lettera/Device Check/Build for macOS")] static void Mac() => Build(BuildTarget.StandaloneOSX, BuildTargetGroup.Standalone, "lettera-device-check-mac.app");

        static Type CheckType()
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type type = assembly.GetType("FirstSymbol.Lettera.Samples.LetteraDeviceCheck");
                if (type != null) return type;
            }
            return null;
        }

        /// <summary>Turns every native route on, runs the body, and puts the settings back whatever happens.</summary>
        internal static void WithNativeOn(Action body)
        {
            LetteraSettings settings = LetteraSettings.Instance;
            string before = EditorJsonUtility.ToJson(settings);
            bool wasDirty = EditorUtility.IsDirty(settings);
            try
            {
                LetteraPlatformSettings platform = settings.Platform;
                platform.TextInput = platform.Replica = platform.Clipboard = platform.SystemMenu = platform.SystemLoupe = platform.PasteControl = platform.OsPreferences = LetteraGate.On;
                platform.IosNativeInBuild = true;
                settings.Editing.AndroidMenu = LetteraMenuChoice.System;
                settings.Editing.IosMenu = LetteraMenuChoice.System;
                settings.Editing.Loupe = LetteraLoupeChoice.System;
                body();
            }
            finally
            {
                EditorJsonUtility.FromJsonOverwrite(before, settings);
                if (!wasDirty) EditorUtility.ClearDirty(settings);
            }
        }

        /// <summary>The platforms a build can be asked for by name.</summary>
        internal static readonly System.Collections.Generic.Dictionary<string, (BuildTarget target, BuildTargetGroup group, string output)> Platforms = new System.Collections.Generic.Dictionary<string, (BuildTarget, BuildTargetGroup, string)>
        {
            ["android"] = (BuildTarget.Android, BuildTargetGroup.Android, "lettera-device-check.apk"),
            ["ios"] = (BuildTarget.iOS, BuildTargetGroup.iOS, "lettera-device-check-ios"),
            ["web"] = (BuildTarget.WebGL, BuildTargetGroup.WebGL, "lettera-device-check-web"),
            ["windows"] = (BuildTarget.StandaloneWindows64, BuildTargetGroup.Standalone, "lettera-device-check-windows/check.exe"),
            ["mac"] = (BuildTarget.StandaloneOSX, BuildTargetGroup.Standalone, "lettera-device-check-mac.app"),
        };

        static void Build(BuildTarget target, BuildTargetGroup group, string output)
        {
            if (CheckType() == null)
            {
                EditorUtility.DisplayDialog("Lettera Device Check", MissingSample, "OK");
                return;
            }
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            Build(target, group, output, out _, out _);
        }

        const string MissingSample = "Import the Device Check sample first, from the Package Manager's Samples tab.";

        /// <summary>Asks nothing, so a caller settles unsaved scenes first. Returns why it could not start, or null.</summary>
        internal static string Build(BuildTarget target, BuildTargetGroup group, string output, out string result, out string file)
        {
            result = null;
            file = null;
            Type check = CheckType();
            if (check == null) return MissingSample;
            string path = SceneFolder + "/DeviceCheck.unity";
            Directory.CreateDirectory(SceneFolder);
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var camera = new GameObject("Camera", typeof(Camera));
            camera.GetComponent<Camera>().clearFlags = CameraClearFlags.SolidColor;
            camera.GetComponent<Camera>().backgroundColor = new Color(0.1f, 0.11f, 0.13f);
            new GameObject("Device Check", check);
            EditorSceneManager.SaveScene(scene, path);
            string full = Path.GetFullPath(Path.Combine("Builds", output));
            Directory.CreateDirectory(Path.GetDirectoryName(full));
            string outcome = null;
            try
            {
                WithNativeOn(() =>
                {
                    BuildReport report = BuildPipeline.BuildPlayer(new BuildPlayerOptions { scenes = new[] { path }, locationPathName = full, target = target, targetGroup = group });
                    outcome = report.summary.result.ToString();
                    Debug.Log($"Lettera: device check for {target}: {report.summary.result}, at {full}");
                });
            }
            finally
            {
                AssetDatabase.DeleteAsset(SceneFolder);
            }
            result = outcome;
            file = full;
            return null;
        }
    }
}

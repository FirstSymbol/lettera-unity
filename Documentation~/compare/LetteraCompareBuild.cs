// Builds the comparison's Windows player. Imports TextMesh Pro's essentials, so that its shaders are in the build.
using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class LetteraCompareBuild
{
    public static void Run()
    {
        string[] args = Environment.GetCommandLineArgs();
        string output = args[Array.IndexOf(args, "-out") + 1];
        if (!Directory.Exists("Assets/TextMesh Pro"))
        {
            string package = Directory.GetDirectories("Library/PackageCache", "com.unity.ugui*").First();
            AssetDatabase.ImportPackage(Path.Combine(package, "Package Resources", "TMP Essential Resources.unitypackage"), false);
            AssetDatabase.Refresh();
        }
        // A material per set of keywords the effects sheet uses, or a player has the shader without them.
        Directory.CreateDirectory("Assets/Compare/Resources");
        Shader sdf = Shader.Find("TextMeshPro/Distance Field");
        if (sdf != null)
        {
            foreach ((string name, string[] keywords) in new[]
            {
                ("Outline", new[] { "OUTLINE_ON" }),
                ("Underlay", new[] { "UNDERLAY_ON" }),
                ("Glow", new[] { "GLOW_ON" }),
                ("Inner", new[] { "UNDERLAY_INNER" }),
                ("All", new[] { "OUTLINE_ON", "UNDERLAY_ON", "GLOW_ON" }),
            })
            {
                string asset = "Assets/Compare/Resources/Tmp_" + name + ".mat";
                if (File.Exists(asset)) continue;
                AssetDatabase.CreateAsset(new Material(sdf) { shaderKeywords = keywords }, asset);
            }
            AssetDatabase.SaveAssets();
        }
        const string path = "Assets/Compare.unity";
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene(), path);
        bool il2cpp = Environment.GetEnvironmentVariable("LETTERA_COMPARE_BACKEND") == "il2cpp";
        PlayerSettings.SetScriptingBackend(UnityEditor.Build.NamedBuildTarget.Standalone, il2cpp ? ScriptingImplementation.IL2CPP : ScriptingImplementation.Mono2x);
        PlayerSettings.SetIl2CppCompilerConfiguration(UnityEditor.Build.NamedBuildTarget.Standalone, Il2CppCompilerConfiguration.Release);
        // A release player reports GPU time only when asked to at build time.
        PlayerSettings.enableFrameTimingStats = true;
        PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
        PlayerSettings.defaultScreenWidth = 1200;
        PlayerSettings.defaultScreenHeight = 700;
        // A font made from a file at runtime asks for a dynamic atlas's shader by name.
        var graphics = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>("ProjectSettings/GraphicsSettings.asset");
        BuildReport report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = new[] { path },
            target = BuildTarget.StandaloneWindows64,
            locationPathName = output,
            options = Environment.GetEnvironmentVariable("LETTERA_DEV") == "1" ? BuildOptions.Development : BuildOptions.None,
        });
        Debug.Log("LETTERA_COMPARE_BUILD " + report.summary.result + " errors=" + report.summary.totalErrors);
    }
}

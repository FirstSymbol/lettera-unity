using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEditor.Rendering;

namespace FirstSymbol.Lettera.Editor.Tests
{
    /// <summary>Every pass of every package shader compiles for the WebGPU graphics API, with no keyword and with each of its own keywords.</summary>
    public class WebGpuShaderTests
    {
        static IEnumerable<Shader> PackageShaders()
        {
            foreach (string guid in AssetDatabase.FindAssets("t:Shader", new[] { "Packages/com.firstsymbol.lettera" }))
            {
                var shader = AssetDatabase.LoadAssetAtPath<Shader>(AssetDatabase.GUIDToAssetPath(guid));
                if (shader != null) yield return shader;
            }
        }

        static int CompileAll(ShaderCompilerPlatform platform, List<string> failures)
        {
            int compiled = 0;
            foreach (Shader shader in PackageShaders())
            {
                ShaderData data = ShaderUtil.GetShaderData(shader);
                var sets = new List<string[]> { new string[0] };
                foreach (string keyword in shader.keywordSpace.keywordNames) sets.Add(new[] { keyword });
                for (int s = 0; s < data.SubshaderCount; s++)
                {
                    ShaderData.Subshader sub = data.GetSubshader(s);
                    for (int p = 0; p < sub.PassCount; p++)
                    {
                        ShaderData.Pass pass = sub.GetPass(p);
                        foreach (ShaderType stage in new[] { ShaderType.Vertex, ShaderType.Fragment })
                        {
                            if (!pass.HasShaderStage(stage)) continue;
                            foreach (string[] keywords in sets)
                            {
                                ShaderData.VariantCompileInfo info = pass.CompileVariant(stage, keywords, platform, BuildTarget.WebGL, false);
                                compiled++;
                                if (info.Success) continue;
                                var text = new System.Text.StringBuilder();
                                foreach (ShaderMessage m in info.Messages) text.Append(m.message).Append(' ');
                                failures.Add($"{shader.name} subshader {s} pass {p} ({pass.Name}) {stage} [{string.Join(" ", keywords)}]: {text}");
                            }
                        }
                    }
                }
            }
            return compiled;
        }

        [Test]
        public void EveryPassCompilesForWebGpu()
        {
            var failures = new List<string>();
            int compiled = CompileAll(ShaderCompilerPlatform.WebGPU, failures);
            TestContext.Out.WriteLine($"WebGPU: {compiled} variants compiled, {failures.Count} failed");
            Assert.Greater(compiled, 20);
            Assert.IsEmpty(failures, string.Join("\n", failures));
        }
    }
}

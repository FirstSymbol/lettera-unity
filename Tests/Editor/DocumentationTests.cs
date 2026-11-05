using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor;

namespace FirstSymbol.Lettera.Editor.Tests
{
    // Keeps the guides true: every C# block in Documentation~ and the READMEs compiles against the package as it stands, and every tag a
    // tag table names is one the default vocabulary reads. Blocks go to the C# compiler the editor ships, run apart from the editor, so a
    // block that fails costs no domain reload and leaves nothing behind.
    public class DocumentationTests
    {
        // A block may use these names without declaring them, as a guide's prose does: "label" is a text the reader already has.
        const string Context = @"using UnityEngine;
using FirstSymbol.Lettera;

namespace LetteraDocs
{
    public abstract class DocContext : MonoBehaviour
    {
        protected LetteraText label, text;
        protected LetteraWorldText worldText;
        protected LetteraText3D solid;
        protected LetteraEditable field;
        protected GameObject go, scrollerObject;
        protected Camera cam;
        protected Ray ray;
        protected string path;
        protected Color32 red, orange, pink, blue;
        protected StrokeEffect stroke;
        protected ColorEffect color;
        protected LetteraFontAsset fontAsset;
    }
}
";

        const string Usings = "using System;\nusing System.Collections.Generic;\nusing UnityEngine;\nusing FirstSymbol.Lettera;\n";
        const string NoCompile = "<!-- no-compile -->";
        // Fragments by design: a member shown out of its class, a block that needs a package this project may lack. Keep them few.
        const int MostSkipped = 8;

        static readonly Regex s_TypeDeclaration = new Regex(@"^(\[[^\]]*\]\s*)*((public|internal|sealed|static|abstract|partial)\s+)*(class|struct|interface|enum|record)\s+\w", RegexOptions.Compiled);
        static readonly Regex s_Error = new Regex(@"^(?<file>.*?)\((?<line>\d+),(?<col>\d+)\): error (?<code>CS\d+): (?<message>.*)$", RegexOptions.Compiled);

        struct Block
        {
            public string Doc;
            public int Line;
            public string Code;
        }

        static string PackageRoot()
        {
            var info = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(LetteraText).Assembly);
            Assert.IsNotNull(info, "Lettera is not a package in this project");
            return info.resolvedPath;
        }

        static List<string> Docs(string root)
        {
            var docs = Directory.GetFiles(root, "README*.md").ToList();
            string folder = Path.Combine(root, "Documentation~");
            if (Directory.Exists(folder)) docs.AddRange(Directory.GetFiles(folder, "*.md"));
            docs.Sort(StringComparer.Ordinal);
            return docs;
        }

        static List<Block> Blocks(string doc, ref int skipped)
        {
            var blocks = new List<Block>();
            string[] lines = File.ReadAllLines(doc);
            for (int i = 0; i < lines.Length; i++)
            {
                if (lines[i].Trim() != "```csharp") continue;
                bool skip = false;
                for (int p = i - 1; p >= 0; p--)
                {
                    if (lines[p].Trim().Length == 0) continue;
                    skip = lines[p].Trim() == NoCompile;
                    break;
                }
                int start = i + 1, end = start;
                while (end < lines.Length && lines[end].Trim() != "```") end++;
                Assert.Less(end, lines.Length, $"{Path.GetFileName(doc)}:{i + 1}: a csharp block never closes");
                if (skip) skipped++;
                else blocks.Add(new Block { Doc = doc, Line = start + 1, Code = string.Join("\n", lines, start, end - start) });
                i = end;
            }
            return blocks;
        }

        // A block of statements becomes a method's body; a block declaring types stands in a namespace of its own. #line sends every error
        // back to the guide's own line.
        static string Wrap(Block block, string name)
        {
            var head = new StringBuilder(Usings);
            var body = new List<string>();
            int bodyLine = block.Line;
            bool inHead = true;
            foreach (string line in block.Code.Split('\n'))
            {
                string t = line.Trim();
                if (inHead && t.StartsWith("using ") && t.EndsWith(";") && !t.StartsWith("using var") && !t.Contains("("))
                {
                    head.Append(t).Append('\n');
                    bodyLine++;
                    continue;
                }
                if (inHead && t.Length == 0) { bodyLine++; continue; }
                inHead = false;
                body.Add(line);
            }
            bool declares = body.Any(l => s_TypeDeclaration.IsMatch(l));
            string file = block.Doc.Replace('\\', '/');
            var s = new StringBuilder(head.ToString());
            s.Append("namespace LetteraDocs.").Append(name).Append("\n{\n");
            if (!declares)
                s.Append("class Stub : LetteraDocs.DocContext\n{\n").Append(block.Code.Contains("await ") ? "async void Run()\n" : "void Run()\n").Append("{\n");
            s.Append("#line ").Append(bodyLine).Append(" \"").Append(file).Append("\"\n");
            foreach (string line in body) s.Append(line).Append('\n');
            s.Append("#line default\n");
            if (!declares) s.Append("}\n}\n");
            s.Append("}\n");
            return s.ToString();
        }

        static List<string> References()
        {
            string data = EditorApplication.applicationContentsPath;
            string api = Path.Combine(data, "UnityReferenceAssemblies", "unity-4.8-api");
            var refs = new List<string>();
            foreach (string name in new[] { "mscorlib", "System", "System.Core", "System.Xml", "System.Xml.Linq", "System.Numerics", "System.Runtime.Serialization", "System.Net.Http", "System.Data", "System.Drawing", "System.IO.Compression", "Microsoft.CSharp" })
                refs.Add(Path.Combine(api, name + ".dll"));
            refs.AddRange(Directory.GetFiles(Path.Combine(api, "Facades"), "*.dll"));
            string modules = Path.Combine(data, "Managed", "UnityEngine");
            refs.AddRange(Directory.GetFiles(modules, "UnityEngine*.dll"));
            refs.AddRange(Directory.GetFiles(modules, "UnityEditor*.dll"));
            string scripts = Path.GetFullPath("Library/ScriptAssemblies");
            refs.AddRange(Directory.GetFiles(scripts, "*.dll").Where(f =>
            {
                string n = Path.GetFileNameWithoutExtension(f);
                return !n.EndsWith(".Tests") && !n.StartsWith("Assembly-CSharp");
            }));
            return refs.Where(File.Exists).ToList();
        }

        // What the compiler said, by the file each error names. Runs the editor's own Roslyn on the .NET it ships with.
        static (Dictionary<string, List<string>> errors, string output) Compile(IList<(string name, string source)> files)
        {
            string data = EditorApplication.applicationContentsPath;
            string dotnet = Path.Combine(data, "NetCoreRuntime", UnityEngine.Application.platform == UnityEngine.RuntimePlatform.WindowsEditor ? "dotnet.exe" : "dotnet");
            string csc = Path.Combine(data, "DotNetSdkRoslyn", "csc.dll");
            Assert.IsTrue(File.Exists(dotnet) && File.Exists(csc), "The editor's C# compiler was not found at " + csc);
            string dir = Path.GetFullPath("Temp/LetteraDocBlocks");
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
            Directory.CreateDirectory(dir);
            var rsp = new StringBuilder();
            rsp.Append("-target:library\n-nostdlib+\n-langversion:9.0\n-preferreduilang:en-US\n-nologo\n-warn:0\n-unsafe\n");
            rsp.Append("-out:\"").Append(Path.Combine(dir, "LetteraDocBlocks.dll")).Append("\"\n");
            foreach (string r in References()) rsp.Append("-r:\"").Append(r).Append("\"\n");
            foreach (var (name, source) in files)
            {
                string p = Path.Combine(dir, name + ".cs");
                File.WriteAllText(p, source);
                rsp.Append('"').Append(p).Append("\"\n");
            }
            string rspPath = Path.Combine(dir, "blocks.rsp");
            File.WriteAllText(rspPath, rsp.ToString());
            var info = new ProcessStartInfo(dotnet, $"\"{csc}\" -noconfig @\"{rspPath}\"")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            info.EnvironmentVariables["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
            string output;
            using (Process process = Process.Start(info))
            {
                var stderr = process.StandardError.ReadToEndAsync();
                output = process.StandardOutput.ReadToEnd();
                Assert.IsTrue(process.WaitForExit(240000), "The compiler did not finish in four minutes");
                output += stderr.Result;
            }
            var errors = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (string raw in output.Split('\n'))
            {
                string line = raw.TrimEnd('\r');
                Match m = s_Error.Match(line);
                string key = m.Success ? m.Groups["file"].Value.Replace('\\', '/') : line.Contains(": error ") ? "" : null;
                if (key == null) continue;
                if (!errors.TryGetValue(key, out List<string> list)) errors[key] = list = new List<string>();
                list.Add(line);
            }
            return (errors, output);
        }

        [Test]
        public void EveryCSharpBlockCompiles()
        {
            string root = PackageRoot();
            int skipped = 0;
            var blocks = new List<Block>();
            foreach (string doc in Docs(root)) blocks.AddRange(Blocks(doc, ref skipped));
            Assert.Greater(blocks.Count, 0, "No csharp block was found under " + root);
            Assert.LessOrEqual(skipped, MostSkipped, $"{skipped} blocks are marked {NoCompile}; keep fragments few");

            // The route is proved each run: a block that must fail does, and one that must pass does.
            var files = new List<(string, string)>
            {
                ("Context", Context),
                ("CanaryGood", Wrap(new Block { Doc = "canary-good", Line = 1, Code = "label.Text = \"fine\";" }, "CanaryGood")),
                ("CanaryBad", Wrap(new Block { Doc = "canary-bad", Line = 1, Code = "label.Text = 5;\nlabel.NoSuchMember();" }, "CanaryBad")),
            };
            for (int i = 0; i < blocks.Count; i++) files.Add(("Block" + i, Wrap(blocks[i], "Block" + i)));
            var (errors, output) = Compile(files);

            Assert.IsFalse(errors.ContainsKey(""), "The compiler failed outside any block:\n" + output);
            Assert.IsTrue(errors.Keys.Any(k => k.EndsWith("canary-bad")), "A block that cannot compile went through, so nothing was really compiled:\n" + output);
            Assert.IsFalse(errors.Keys.Any(k => k.EndsWith("canary-good")), "A block that must compile failed, so the route itself is broken:\n" + output);

            var failures = errors.Where(e => !e.Key.EndsWith("canary-bad") && !e.Key.EndsWith("canary-good")).SelectMany(e => e.Value).ToList();
            TestContext.WriteLine($"{blocks.Count} blocks compiled from {Docs(root).Count} files, {skipped} marked {NoCompile}");
            Assert.IsEmpty(failures, "Blocks that do not compile:\n" + string.Join("\n", failures));
        }

        // The names the default vocabulary answers to. A text 3D adds its own range tags, which only its guide may list.
        static HashSet<string> Vocabulary(bool solids)
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            void Add(IEnumerable<LetteraStyle> styles)
            {
                foreach (LetteraStyle style in styles)
                {
                    LetteraMatcher matcher = style.Matcher;
                    if (matcher == null) continue;
                    if (matcher.GetType().GetProperty("Name", BindingFlags.Public | BindingFlags.Instance)?.GetValue(matcher) is string name && name.Length > 0) names.Add(name);
                    if (matcher is NoParseMatcher) names.Add("noparse");
                    if (matcher is LineBreakMatcher) names.Add("br");
                    if (matcher is RubyMatcher) { names.Add("rt"); names.Add("rp"); }
                }
            }
            Add(LetteraDefaultStyles.Create());
            if (solids)
            {
                Type type = Type.GetType("FirstSymbol.Lettera.LetteraSolidEffects, FirstSymbol.Lettera.Text3D");
                Assert.IsNotNull(type, "The 3D module is not in this project");
                var styles = (IEnumerable<LetteraStyle>)type.GetProperty("Styles", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);
                Add(styles);
            }
            return names;
        }

        static readonly Regex s_Tag = new Regex(@"`[^`]*`", RegexOptions.Compiled);
        static readonly Regex s_TagName = new Regex(@"<([A-Za-z][A-Za-z0-9-]*)", RegexOptions.Compiled);

        // A tag table is one whose first column is headed Tag (Тег in Russian); every tag in that column must be one Lettera reads.
        [Test]
        public void EveryTagInATagTableIsInTheVocabulary()
        {
            var plain = Vocabulary(false);
            var withSolids = Vocabulary(true);
            // The check can fail: a solid's tag is not in the shared vocabulary, and a made-up one is in neither.
            Assert.IsTrue(plain.Contains("b") && plain.Contains("math") && plain.Contains("rt"));
            Assert.IsFalse(plain.Contains("depth") || withSolids.Contains("no-such-tag"));
            Assert.IsTrue(withSolids.Contains("depth") && withSolids.Contains("revolve"));
            var unknown = new List<string>();
            int checkedTags = 0;
            foreach (string doc in Docs(PackageRoot()))
            {
                HashSet<string> vocabulary = Path.GetFileName(doc).StartsWith("Text3D") ? withSolids : plain;
                string[] lines = File.ReadAllLines(doc);
                for (int i = 0; i < lines.Length; i++)
                {
                    string first = FirstCell(lines[i]);
                    if (first == null || (first != "Tag" && first != "Тег" && first != "Tags" && first != "Теги")) continue;
                    for (i += 2; i < lines.Length && lines[i].TrimStart().StartsWith("|"); i++)
                    {
                        foreach (Match code in s_Tag.Matches(FirstCell(lines[i]) ?? ""))
                            foreach (Match tag in s_TagName.Matches(code.Value))
                            {
                                checkedTags++;
                                if (!vocabulary.Contains(tag.Groups[1].Value)) unknown.Add($"{Path.GetFileName(doc)}:{i + 1}: <{tag.Groups[1].Value}>");
                            }
                    }
                }
            }
            Assert.Greater(checkedTags, 40, "Too few tags were found to check; did the tables move?");
            Assert.IsEmpty(unknown, "Tags the vocabulary does not read:\n" + string.Join("\n", unknown));
        }

        static string FirstCell(string line)
        {
            string t = line.Trim();
            if (!t.StartsWith("|")) return null;
            int end = t.IndexOf('|', 1);
            while (end > 0 && t[end - 1] == '\\') end = t.IndexOf('|', end + 1);
            return end < 0 ? null : t.Substring(1, end - 1).Trim();
        }
    }
}

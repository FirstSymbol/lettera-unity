using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace FirstSymbol.Lettera.Unicode.Tests
{
    public class UnicodeConformanceTests
    {
        const int MaxReported = 8;
        const string Break = "÷", NoBreak = "×";

        sealed class Tally
        {
            public int Total, Passed;
            public readonly List<string> Failures = new List<string>();

            public void Add(bool ok, Func<string> describe)
            {
                Total++;
                if (ok) Passed++;
                else if (Failures.Count < MaxReported) Failures.Add(describe());
            }

            public void Check(string name, double requiredPercent)
            {
                double pct = Total == 0 ? 0 : 100.0 * Passed / Total;
                string summary = $"{name}: {Passed}/{Total} passed ({pct:F4}%)";
                Debug.Log(summary);
                TestContext.Out.WriteLine(summary);
                Assert.That(Total, Is.GreaterThan(0), name + ": no test cases parsed");
                if (Passed == Total) return;
                string detail = summary + "\nFirst failures:\n" + string.Join("\n", Failures);
                if (pct < requiredPercent) Assert.Fail(detail);
                Debug.LogWarning(detail);
            }
        }

        static IEnumerable<string> ReadLines(string fileName)
        {
            string guid = AssetDatabase.FindAssets("FirstSymbol.Lettera.Unicode.Tests t:asmdef")[0];
            string asmdef = FileUtil.GetPhysicalPath(AssetDatabase.GUIDToAssetPath(guid));
            string path = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(asmdef)), "Conformance~", fileName + ".gz");
            using var file = File.OpenRead(path);
            using var gzip = new GZipStream(file, CompressionMode.Decompress);
            using var reader = new StreamReader(gzip, Encoding.UTF8);
            string line;
            while ((line = reader.ReadLine()) != null)
                yield return line;
        }

        static int Hex(string s) => int.Parse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture);

        static readonly char[] Spaces = { ' ', '\t' };

        // Parses "÷ 0061 × 0308 ÷": cps gets the code points; marks[k] says whether a break is expected before cps[k].
        static bool ParseBreakLine(string line, List<int> cps, List<bool> marks)
        {
            cps.Clear();
            marks.Clear();
            int hash = line.IndexOf('#');
            string data = (hash >= 0 ? line.Substring(0, hash) : line).Trim();
            if (data.Length == 0) return false;
            foreach (string token in data.Split(Spaces, StringSplitOptions.RemoveEmptyEntries))
            {
                if (token == Break) marks.Add(true);
                else if (token == NoBreak) marks.Add(false);
                else cps.Add(Hex(token));
            }
            return true;
        }

        static string BoolsToString(ReadOnlySpan<bool> b)
        {
            var sb = new StringBuilder();
            foreach (bool v in b) sb.Append(v ? Break : NoBreak);
            return sb.ToString();
        }

        delegate void BoundaryFinder(ReadOnlySpan<int> text, Span<bool> boundaries);

        static void RunBoundaryFile(string file, BoundaryFinder finder, double required)
        {
            var tally = new Tally();
            var cps = new List<int>();
            var marks = new List<bool>();
            int number = 0;
            foreach (string line in ReadLines(file))
            {
                number++;
                if (!ParseBreakLine(line, cps, marks)) continue;
                int[] text = cps.ToArray();
                var got = new bool[text.Length + 1];
                finder(text, got);
                bool ok = got.Length == marks.Count;
                for (int i = 0; ok && i < got.Length; i++) ok = got[i] == marks[i];
                int n = number;
                tally.Add(ok, () => $"line {n}: {line}\n  got {BoolsToString(got)}");
            }
            tally.Check(file, required);
        }

        [Test]
        public void GraphemeBreakTest() => RunBoundaryFile("GraphemeBreakTest.txt", GraphemeBreaker.FindBoundaries, 100.0);

        [Test]
        public void WordBreakTest() => RunBoundaryFile("WordBreakTest.txt", WordBreaker.FindBoundaries, 100.0);

        [Test]
        public void LineBreakTest()
        {
            var tally = new Tally();
            var cps = new List<int>();
            var marks = new List<bool>();
            int number = 0;
            foreach (string line in ReadLines("LineBreakTest.txt"))
            {
                number++;
                if (!ParseBreakLine(line, cps, marks)) continue;
                int[] text = cps.ToArray();
                var got = new LineBreakOpportunity[text.Length];
                LineBreaker.FindBreaks(text, got);
                bool ok = marks.Count == text.Length + 1;
                for (int i = 0; ok && i < got.Length; i++) ok = (got[i] != LineBreakOpportunity.None) == marks[i + 1];
                int n = number;
                tally.Add(ok, () =>
                {
                    var sb = new StringBuilder();
                    foreach (var g in got) sb.Append(g == LineBreakOpportunity.None ? NoBreak : Break);
                    return $"line {n}: {line}\n  got after each: {sb}";
                });
            }
            tally.Check("LineBreakTest.txt", 100.0);
        }

        static readonly Dictionary<string, int> ClassSample = new Dictionary<string, int>
        {
            {"L", 'a'}, {"R", 0x05D0}, {"AL", 0x0627}, {"EN", '0'}, {"ES", '+'}, {"ET", '#'}, {"AN", 0x0660}, {"CS", ','},
            {"NSM", 0x0300}, {"BN", 0x00AD}, {"B", 0x2029}, {"S", '\t'}, {"WS", ' '}, {"ON", '!'}, {"LRE", 0x202A},
            {"LRO", 0x202D}, {"RLE", 0x202B}, {"RLO", 0x202E}, {"PDF", 0x202C}, {"LRI", 0x2066}, {"RLI", 0x2067},
            {"FSI", 0x2068}, {"PDI", 0x2069},
        };

        // 'levels' uses -1 for x; 'order' lists logical indices without x entries. Returns null on success.
        static string CheckBidi(int[] text, int baseLevel, int expectedPara, int[] levels, int[] order)
        {
            int n = text.Length;
            var para = new byte[n];
            var line = new byte[n];
            var visual = new int[n];
            int got = Bidi.ResolveParagraph(text, baseLevel, para);
            if (expectedPara >= 0 && got != expectedPara) return $"paragraph level {got}, expected {expectedPara}";
            Bidi.ResolveLineLevels(text, para, 0, n, got, line);
            for (int i = 0; i < n; i++)
                if (levels[i] >= 0 && levels[i] != line[i])
                    return $"levels [{string.Join(" ", line)}], index {i} expected {levels[i]}";
            Bidi.ReorderLine(line, visual);
            int k = 0;
            for (int i = 0; i < n; i++)
            {
                if (levels[visual[i]] < 0) continue;
                if (k >= order.Length || order[k] != visual[i]) return $"visual order [{string.Join(" ", visual)}]";
                k++;
            }
            return k == order.Length ? null : $"visual order [{string.Join(" ", visual)}]";
        }

        static int[] ParseLevels(string field)
        {
            var parts = field.Split(Spaces, StringSplitOptions.RemoveEmptyEntries);
            var r = new int[parts.Length];
            for (int i = 0; i < parts.Length; i++) r[i] = parts[i] == "x" ? -1 : int.Parse(parts[i], CultureInfo.InvariantCulture);
            return r;
        }

        static int[] ParseInts(string field)
        {
            var parts = field.Split(Spaces, StringSplitOptions.RemoveEmptyEntries);
            var r = new int[parts.Length];
            for (int i = 0; i < parts.Length; i++) r[i] = int.Parse(parts[i], CultureInfo.InvariantCulture);
            return r;
        }

        [Test]
        public void BidiCharacterTest()
        {
            var tally = new Tally();
            int number = 0;
            foreach (string line in ReadLines("BidiCharacterTest.txt"))
            {
                number++;
                if (line.Length == 0 || line[0] == '#') continue;
                var f = line.Split(';');
                var hex = f[0].Split(Spaces, StringSplitOptions.RemoveEmptyEntries);
                var text = new int[hex.Length];
                for (int i = 0; i < hex.Length; i++) text[i] = Hex(hex[i]);
                int dir = int.Parse(f[1], CultureInfo.InvariantCulture);
                string err = CheckBidi(text, dir == 2 ? -1 : dir, int.Parse(f[2], CultureInfo.InvariantCulture), ParseLevels(f[3]), ParseInts(f[4]));
                int n = number;
                tally.Add(err == null, () => $"line {n}: {line}\n  got {err}");
            }
            tally.Check("BidiCharacterTest.txt", 100.0);
        }

        [Test]
        public void BidiTest()
        {
            var tally = new Tally();
            int number = 0;
            int[] levels = null, order = null;
            foreach (string line in ReadLines("BidiTest.txt"))
            {
                number++;
                if (line.Length == 0 || line[0] == '#') continue;
                if (line.StartsWith("@Levels:", StringComparison.Ordinal)) { levels = ParseLevels(line.Substring(8)); continue; }
                if (line.StartsWith("@Reorder:", StringComparison.Ordinal)) { order = ParseInts(line.Substring(9)); continue; }
                if (line[0] == '@') continue;
                int semi = line.IndexOf(';');
                var classes = line.Substring(0, semi).Split(Spaces, StringSplitOptions.RemoveEmptyEntries);
                var text = new int[classes.Length];
                for (int i = 0; i < classes.Length; i++) text[i] = ClassSample[classes[i]];
                int bits = int.Parse(line.Substring(semi + 1).Trim(), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                for (int b = 0; b < 3; b++)
                {
                    if ((bits & (1 << b)) == 0) continue;
                    int baseLevel = b == 0 ? -1 : b - 1; // 1 = auto, 2 = LTR, 4 = RTL
                    string err = CheckBidi(text, baseLevel, -1, levels, order);
                    int n = number, bb = b;
                    tally.Add(err == null, () => $"line {n}: {line} (bit {1 << bb}, base {baseLevel}) expected levels [{string.Join(" ", levels)}] order [{string.Join(" ", order)}]\n  got {err}");
                }
            }
            tally.Check("BidiTest.txt", 100.0);
        }
    }
}

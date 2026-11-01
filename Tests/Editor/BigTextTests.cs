using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using NUnit.Framework;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace FirstSymbol.Lettera.Editor.Tests
{
    /// <summary>A long text in a plain component costs memory in proportion to its length. It once ran a machine out of memory at 100 KB.</summary>
    public class BigTextTests : TextFixture
    {
        static long Used()
        {
            using (Process self = Process.GetCurrentProcess()) return self.PrivateMemorySize64 / (1024 * 1024);
        }

        static string Page(int bytes)
        {
            const string Words = "The quick brown fox jumps over a lazy dog and then runs back home again ";
            var page = new StringBuilder(bytes + 80);
            int i = 0;
            while (page.Length < bytes)
            {
                page.Append(Words, i % 20, 12 + i * 7 % 29);
                i++;
                page.Append(i % 8 == 0 ? '\n' : ' ');
            }
            return page.ToString();
        }

        [Test]
        public void MemoryGrowsWithLengthAndNoFaster()
        {
            // A runaway here takes the whole machine with it, so a thread ends this process before that.
            string trace = Path.Combine(Path.GetTempPath(), "lettera_bigtext.txt");
            long limit = Used() + 6000;
            bool done = false;
            string stage = "start";
            var guard = new Thread(() =>
            {
                while (!done)
                {
                    Thread.Sleep(50);
                    if (Used() <= limit) continue;
                    File.WriteAllText(trace, "Stopped at " + Used() + " MB in " + stage);
                    Process.GetCurrentProcess().Kill();
                }
            }) { IsBackground = true };
            guard.Start();
            try
            {
                var report = new StringBuilder();
                foreach (int size in new[] { 10, 20, 40, 80, 160 })
                {
                    LetteraWorldText text = Make("", 800, 100000, Latin, 14);
                    stage = size + " KB parse";
                    long before = Used();
                    text.Text = Page(size * 1024);
                    text.Engine.EnsureParsed();
                    long parsed = Used();
                    stage = size + " KB rebuild";
                    var clock = Stopwatch.StartNew();
                    text.ForceRebuild();
                    report.Append($"{size} KB: parse +{parsed - before} MB, rebuild +{Used() - parsed} MB, {clock.ElapsedMilliseconds} ms, {text.GlyphCount} glyphs, {text.Engine.VertexCount} vertices\n");
                    File.WriteAllText(trace, report.ToString());
                    UnityEngine.Object.DestroyImmediate(text.gameObject);
                    Assert.Less(Used() - before, 40 * size + 400, "memory for " + size + " KB\n" + report);
                }
                Debug.Log("LETTERA_BIGTEXT\n" + report);
            }
            finally
            {
                done = true;
            }
        }
    }
}

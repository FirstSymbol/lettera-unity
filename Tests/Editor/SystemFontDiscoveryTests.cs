using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace FirstSymbol.Lettera.Editor.Tests
{
    /// <summary>The system's font files are found once, and tried in the same order as ever.</summary>
    public class SystemFontDiscoveryTests : TextFixture
    {
        static readonly BindingFlags Hidden = BindingFlags.NonPublic | BindingFlags.Static;

        static object Call(string method, params object[] arguments) => typeof(LetteraSystemFonts).GetMethod(method, Hidden).Invoke(null, arguments);

        [TearDown]
        public void Forget() => LetteraSystemFonts.Reset();

        // The order fallbacks try files in, worked out the way it was before it was found in one pass: by each priority stem in turn, then the rest.
        static List<string> OrderAsBefore(List<string> files, string[] priority)
        {
            var order = new List<string>();
            var added = new HashSet<string>();
            void Add(string file)
            {
                string name = Path.GetFileName(file);
                if (name.IndexOf("Bold", StringComparison.OrdinalIgnoreCase) >= 0 || name.IndexOf("Italic", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    name.IndexOf("Oblique", StringComparison.OrdinalIgnoreCase) >= 0) return;
                order.Add(file);
            }
            foreach (string stem in priority)
                foreach (string file in files)
                    if (Path.GetFileNameWithoutExtension(file).StartsWith(stem, StringComparison.OrdinalIgnoreCase) && added.Add(file))
                        Add(file);
            foreach (string file in files)
                if (added.Add(file))
                    Add(file);
            return order;
        }

        [Test]
        public void FallbacksAreTriedInTheSameOrder()
        {
            if (!LetteraSystemFonts.IsSupported) Assert.Ignore("no system fonts here");
            LetteraSystemFonts.Reset();
            var files = (List<string>)Call("AllFiles");
            if (files.Count == 0) Assert.Ignore("no font files on this machine");
            var priority = (string[])Call("FallbackPriority", Call("Host"));
            var order = (List<string>)typeof(LetteraSystemFonts).GetField("s_Order", Hidden).GetValue(null);
            CollectionAssert.AreEqual(OrderAsBefore(files, priority), order);

            // A file by its stem is the first of that name, as a walk over all of them found it.
            foreach (string stem in new[] { "segoeui", "SEGOEUI", "arial", "seguiemj", "no-such-font", "NotoSans-Regular", "Helvetica" })
            {
                string walked = null;
                foreach (string file in files)
                    if (string.Equals(Path.GetFileNameWithoutExtension(file), stem, StringComparison.OrdinalIgnoreCase))
                    {
                        walked = file;
                        break;
                    }
                Assert.AreEqual(walked, Call("FindFile", stem), stem);
            }
        }

        [Test]
        public void ADiscoveryOnAnotherThreadIsTheOneUsed()
        {
            if (!LetteraSystemFonts.IsSupported) Assert.Ignore("no system fonts here");
            LetteraSystemFonts.Reset();
            var expected = new List<string>((List<string>)Call("AllFiles"));
            LetteraSystemFonts.Reset();
            LetteraSettings.Instance.DisableSystemFontFallback = false;
            Action discover = LetteraSystemFonts.DiscoveryAside();
            Assert.NotNull(discover);
            var thread = new System.Threading.Thread(() => discover());
            thread.Start();
            // Asked while the thread may still be at it: the main thread waits for its answer rather than making a second.
            var files = (List<string>)Call("AllFiles");
            thread.Join();
            CollectionAssert.AreEqual(expected, files);
            Assert.AreSame(files, Call("AllFiles"));
            LetteraSettings.Instance.DisableSystemFontFallback = true;
            Assert.IsNull(LetteraSystemFonts.DiscoveryAside(), "fallbacks off, nothing to find");
        }

        static double Time(Action action)
        {
            var clock = Stopwatch.StartNew();
            action();
            return clock.Elapsed.TotalMilliseconds;
        }

        // What the first character no font of the project has costs: only logged, since it is the machine's font folder.
        [Test]
        public void TheFirstFallbackCostIsLogged()
        {
            if (!LetteraSystemFonts.IsSupported) Assert.Ignore("no system fonts here");
            LetteraSettings.Instance.DisableSystemFontFallback = false;
            var log = new System.Text.StringBuilder();
            for (int round = 0; round < 3; round++)
            {
                LetteraSystemFonts.Reset();
                double files = Time(() => Call("AllFiles"));
                double entries = Time(() => Call("EnsureEntries"));
                double find = Time(() => LetteraSystemFonts.FindFallback(0x0645, null));
                log.Append($" [files={files:0.00} entries={entries:0.00} find={find:0.00}]");
            }
            var root = new GameObject("Canvas", typeof(RectTransform), typeof(Canvas));
            Created.Add(root);
            for (int round = 0; round < 3; round++)
            {
                LetteraSystemFonts.Reset();
                LetteraFontChains.Invalidate();
                var labels = new List<LetteraText>();
                var clock = Stopwatch.StartNew();
                for (int i = 0; i < 100; i++)
                {
                    var go = new GameObject("Label", typeof(RectTransform));
                    go.transform.SetParent(root.transform, false);
                    ((RectTransform)go.transform).sizeDelta = new Vector2(400, 30);
                    var label = go.AddComponent<LetteraText>();
                    label.raycastTarget = false;
                    label.Font = Latin;
                    label.FontSize = 18;
                    label.Text = "مرحبا بالعالم Hello " + i;
                    labels.Add(label);
                }
                double made = clock.Elapsed.TotalMilliseconds;
                labels[0].ForceRebuild();
                double first = clock.Elapsed.TotalMilliseconds - made;
                for (int i = 1; i < labels.Count; i++) labels[i].ForceRebuild();
                log.Append($" [row={clock.Elapsed.TotalMilliseconds:0.00} made={made:0.00} first={first:0.00}]");
                foreach (LetteraText label in labels) UnityEngine.Object.DestroyImmediate(label.gameObject);
            }
            UnityEngine.Debug.Log("LETTERA_BUDGET first_fallback" + log);
            Assert.Pass();
        }
    }
}

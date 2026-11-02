using System.Diagnostics;
using System.Text;
using NUnit.Framework;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace FirstSymbol.Lettera.Editor.Tests
{
    // Measures, for work in progress; never part of a run unless asked by name.
    [Explicit]
    public class ProbeTests : TextFixture
    {
        static string Page(int bytes)
        {
            var page = new StringBuilder();
            int i = 0;
            const string Words = "The quick brown fox jumps over a lazy dog and then runs back home again ";
            while (page.Length < bytes)
            {
                page.Append(Words, i % 20, 12 + i * 7 % 29);
                i++;
                page.Append(i % 8 == 0 ? '\n' : ' ');
            }
            return page.ToString();
        }

        [Test]
        public void ProbeDocumentScroll()
        {
            var canvas = new GameObject("Canvas", typeof(RectTransform), typeof(Canvas));
            Created.Add(canvas);
            canvas.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            var view = new GameObject("View", typeof(RectTransform));
            view.transform.SetParent(canvas.transform, false);
            ((RectTransform)view.transform).sizeDelta = new Vector2(800, 600);
            var go = new GameObject("Label", typeof(RectTransform));
            go.transform.SetParent(view.transform, false);
            ((RectTransform)go.transform).sizeDelta = new Vector2(800, 600);
            var label = go.AddComponent<LetteraText>();
            label.Font = Latin;
            label.FontSize = 14;
            label.OpenDocument(LetteraRope.FromString(Page(1024 * 1024)), takeOwnership: true);
            var scroller = view.AddComponent<LetteraDocumentScroller>();
            scroller.Text = label;
            label.ForceRebuild();
            scroller.ScrollToEnd();
            double end = scroller.Position;
            scroller.ScrollToStart();
            label.ForceRebuild();
            var engine = ((ITextComponent)label).SharedEngine;
            var clientOf = (IMotionClient)engine.View;
            var report = new StringBuilder();
            var clock = new Stopwatch();
            double move = 0, rebuild = 0, tick = 0;
            long bytesMove = 0, bytesRebuild = 0, bytesTick = 0, largest = 0;
            TextEngine.TimePasses = true;
            System.Array.Clear(TextEngine.PassTicks, 0, 5);
            for (int step = 1; step <= 240; step++)
            {
                long a = System.GC.GetAllocatedBytesForCurrentThread();
                clock.Restart();
                scroller.Position = step / 240.0 * end;
                move += clock.Elapsed.TotalMilliseconds;
                long b = System.GC.GetAllocatedBytesForCurrentThread();
                clock.Restart();
                label.ForceRebuild();
                rebuild += clock.Elapsed.TotalMilliseconds;
                long c = System.GC.GetAllocatedBytesForCurrentThread();
                clock.Restart();
                clientOf.TickMotion(0.016f, 0.016f);
                tick += clock.Elapsed.TotalMilliseconds;
                long d = System.GC.GetAllocatedBytesForCurrentThread();
                bytesMove += b - a;
                bytesRebuild += c - b;
                bytesTick += d - c;
                if (d - a > largest)
                {
                    largest = d - a;
                    report.Append($"step {step}: move {b - a} rebuild {c - b} tick {d - c}\n");
                }
            }
            TextEngine.TimePasses = false;
            double f = 1000.0 / Stopwatch.Frequency;
            report.Append($"per step ms: move {move / 240:0.000} rebuild {rebuild / 240:0.000} tick {tick / 240:0.000}; bytes: move {bytesMove / 240} rebuild {bytesRebuild / 240} tick {bytesTick / 240}\n");
            report.Append($"passes ms per step: parse {TextEngine.PassTicks[0] * f / 240:0.000} shape {TextEngine.PassTicks[1] * f / 240:0.000} layout {TextEngine.PassTicks[2] * f / 240:0.000} positions {TextEngine.PassTicks[3] * f / 240:0.000} mesh {TextEngine.PassTicks[4] * f / 240:0.000}\n");
            report.Append($"window {engine.WindowLength} chars, glyphs {engine.GlyphCount}\n");
            Debug.Log("LETTERA_PROBE\n" + report);
        }

        [Test]
        public void ProbeLongSet()
        {
            var canvas = new GameObject("Canvas", typeof(RectTransform), typeof(Canvas));
            Created.Add(canvas);
            canvas.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            var report = new StringBuilder();
            foreach (int size in new[] { 100, 1000 })
            {
                string page = Page(size * 1024);
                for (int round = 0; round < 2; round++)
                {
                    var go = new GameObject("Label", typeof(RectTransform));
                    go.transform.SetParent(canvas.transform, false);
                    ((RectTransform)go.transform).sizeDelta = new Vector2(800, 100000);
                    var label = go.AddComponent<LetteraText>();
                    label.Font = Latin;
                    label.FontSize = 14;
                    label.Text = "";
                    Canvas.ForceUpdateCanvases();
                    TextEngine.TimePasses = true;
                    System.Array.Clear(TextEngine.PassTicks, 0, 5);
                    long a = System.GC.GetAllocatedBytesForCurrentThread();
                    var clock = Stopwatch.StartNew();
                    label.Text = page;
                    label.ForceRebuild();
                    double ms = clock.Elapsed.TotalMilliseconds;
                    long bytes = System.GC.GetAllocatedBytesForCurrentThread() - a;
                    TextEngine.TimePasses = false;
                    double f = 1000.0 / Stopwatch.Frequency;
                    var engine = ((ITextComponent)label).SharedEngine;
                    report.Append($"{size} KB round {round}: {ms:0.00} ms, {bytes / 1024} KB; parse {TextEngine.PassTicks[0] * f:0.00} shape {TextEngine.PassTicks[1] * f:0.00} layout {TextEngine.PassTicks[2] * f:0.00} positions {TextEngine.PassTicks[3] * f:0.00} mesh {TextEngine.PassTicks[4] * f:0.00}; pending {engine.PendingParagraphs}, shaped {engine.ShapedGlyphCount}, placed {engine.GlyphCount}\n");
                    Object.DestroyImmediate(go);
                }
            }
            Debug.Log("LETTERA_PROBE\n" + report);
        }
    }
}

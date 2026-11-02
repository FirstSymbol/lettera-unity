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

        static string Passes(int n)
        {
            double f = 1000.0 / Stopwatch.Frequency;
            long[] t = TextEngine.PassTicks;
return $"parse {t[0] * f / n:0.000} shape {t[1] * f / n:0.000} layout {t[2] * f / n:0.000} positions {t[3] * f / n:0.000} mesh {t[4] * f / n:0.000}";
        }

        static void StartPasses()
        {
            TextEngine.TimePasses = true;
            System.Array.Clear(TextEngine.PassTicks, 0, 5);
        }

        GameObject MakeCanvas()
        {
            var canvas = new GameObject("Canvas", typeof(RectTransform), typeof(Canvas));
            Created.Add(canvas);
            canvas.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            return canvas;
        }

        LetteraText Label(Transform parent, string text, float size, Vector2 box)
        {
            var go = new GameObject("Label", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            ((RectTransform)go.transform).sizeDelta = box;
            var label = go.AddComponent<LetteraText>();
            label.raycastTarget = false;
            label.Font = Latin;
            label.FontSize = size;
            label.Text = text;
            return label;
        }

        [Test]
        public void ProbeDocumentScroll()
        {
            GameObject canvas = MakeCanvas();
            var view = new GameObject("View", typeof(RectTransform));
            view.transform.SetParent(canvas.transform, false);
            ((RectTransform)view.transform).sizeDelta = new Vector2(800, 600);
            LetteraText label = Label(view.transform, "", 14, new Vector2(800, 600));
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
            long g0 = System.GC.GetTotalMemory(false);
            var junk = new byte[10000];
            report.Append($"allocation check: {System.GC.GetTotalMemory(false) - g0} for {junk.Length}; ");
            var clock = new Stopwatch();
            for (int round = 0; round < 2; round++)
            {
                double move = 0, rebuild = 0, tick = 0;
                long bytesMove = 0, bytesRebuild = 0, bytesTick = 0, largest = 0;
                int windows = 0, glyphs = 0, length = 0, uncovered = 0;
                StartPasses();
                for (int step = 1; step <= 240; step++)
                {
                    int startBefore = engine.WindowStart;
                    long a = System.GC.GetTotalMemory(false);
                    clock.Restart();
                    scroller.Position = step / 240.0 * end;
                    move += clock.Elapsed.TotalMilliseconds;
                    long b = System.GC.GetTotalMemory(false);
                    clock.Restart();
                    Canvas.ForceUpdateCanvases();
                    GlyphAtlas.Flush();
                    rebuild += clock.Elapsed.TotalMilliseconds;
                    long c = System.GC.GetTotalMemory(false);
                    clock.Restart();
                    clientOf.TickMotion(0.016f, 0.016f);
                    tick += clock.Elapsed.TotalMilliseconds;
                    long d = System.GC.GetTotalMemory(false);
                    if (engine.WindowStart != startBefore) windows++;
                    float low = float.MaxValue, high = float.MinValue;
                    for (int v = 0; v < engine.VertexCount; v++)
                    {
                        low = Mathf.Min(low, engine.Vertices[v].y);
                        high = Mathf.Max(high, engine.Vertices[v].y);
                    }
                    if ((high < 300 - 20 || low > -300 + 20) && step < 235)
                    {
                        uncovered++;
                        if (uncovered < 6) report.Append($"round {round} step {step}: ink {low:0}..{high:0}, lean {engine.View.IsLean}, window {engine.WindowLength}\n");
                    }
                    glyphs += engine.GlyphCount;
                    length += engine.WindowLength;
                    bytesMove += b - a;
                    bytesRebuild += c - b;
                    bytesTick += d - c;
                    if (d - a > largest)
                    {
                        largest = d - a;
                        report.Append($"round {round} step {step}: move {b - a} rebuild {c - b} tick {d - c}\n");
                    }
                }
                TextEngine.TimePasses = false;
                report.Append($"round {round} per step ms: move {move / 240:0.000} rebuild {rebuild / 240:0.000} tick {tick / 240:0.000}; bytes: move {bytesMove / 240} rebuild {bytesRebuild / 240} tick {bytesTick / 240}\n");
                report.Append($"  passes ms per step: {Passes(240)}\n");
                report.Append($"  uncovered {uncovered}, new windows {windows}, mean window {length / 240} chars, mean placed glyphs {glyphs / 240}, vertices now {engine.VertexCount}\n");
                scroller.ScrollToStart();
                Canvas.ForceUpdateCanvases();
            }
            Debug.Log("LETTERA_PROBE\n" + report);
        }

        [Test]
        public void ProbeLongSet()
        {
            GameObject canvas = MakeCanvas();
            var report = new StringBuilder();
            foreach (int size in new[] { 100, 1000 })
            {
                string page = Page(size * 1024);
                for (int round = 0; round < 2; round++)
                {
                    LetteraText label = Label(canvas.transform, "", 14, new Vector2(800, 100000));
                    Canvas.ForceUpdateCanvases();
                    StartPasses();
                    int gcs = System.GC.CollectionCount(0);
                    long a = System.GC.GetTotalMemory(false);
                    var clock = Stopwatch.StartNew();
                    label.Text = page;
                    label.ForceRebuild();
                    double ms = clock.Elapsed.TotalMilliseconds;
                    long bytes = System.GC.GetTotalMemory(false) - a;
                    clock.Restart();
                    Canvas.ForceUpdateCanvases();
                    double frame = clock.Elapsed.TotalMilliseconds;
                    TextEngine.TimePasses = false;
                    var engine = ((ITextComponent)label).SharedEngine;
                    report.Append($"{size} KB round {round}: {ms:0.00} ms (+{frame:0.00} frame), {bytes / 1024} KB, gen0 collections {System.GC.CollectionCount(0) - gcs}; {Passes(1)}; pending {engine.PendingParagraphs}, shaped {engine.ShapedGlyphCount}, placed {engine.GlyphCount}\n");
                    Object.DestroyImmediate(label.gameObject);
                }
            }
            Debug.Log("LETTERA_PROBE\n" + report);
        }

        [Test]
        public void ProbeKeystroke()
        {
            GameObject canvas = MakeCanvas();
            var body = new StringBuilder();
            for (int i = 0; body.Length < 2000; i++)
            {
                const string Words = "The quick brown fox jumps over a lazy dog and then runs back home again ";
                body.Append(Words, i % 20, 12 + i * 7 % 29).Append(i % 8 == 7 ? "\n" : " ");
            }
            body.Length = 2000;
            LetteraText label = Label(canvas.transform, body.ToString(), 18, new Vector2(1100, 640));
            label.raycastTarget = true;
            var field = label.gameObject.AddComponent<LetteraEditable>();
            field.Selectable.Activate();
            label.ForceRebuild();
            field.Selectable.Select(field.Length, field.Length);
            for (int i = 0; i < 5; i++) Canvas.ForceUpdateCanvases();
            var report = new StringBuilder();
            var clock = new Stopwatch();
            for (int round = 0; round < 2; round++)
            {
                double insert = 0, frame = 0;
                long bytesInsert = 0, bytesFrame = 0, largest = 0;
                StartPasses();
                for (int k = 0; k < 120; k++)
                {
                    long a = System.GC.GetTotalMemory(false);
                    clock.Restart();
                    field.InsertText(((char)('a' + k % 26)).ToString(), "user.type");
                    insert += clock.Elapsed.TotalMilliseconds;
                    long b = System.GC.GetTotalMemory(false);
                    clock.Restart();
                    Canvas.ForceUpdateCanvases();
                    GlyphAtlas.Flush();
                    frame += clock.Elapsed.TotalMilliseconds;
                    long c = System.GC.GetTotalMemory(false);
                    bytesInsert += b - a;
                    bytesFrame += c - b;
                    if (c - a > largest)
                    {
                        largest = c - a;
                        report.Append($"round {round} key {k}: insert {b - a} frame {c - b}\n");
                    }
                }
                TextEngine.TimePasses = false;
                report.Append($"round {round} per key ms: insert {insert / 120:0.000} frame {frame / 120:0.000}; bytes insert {bytesInsert / 120} frame {bytesFrame / 120}\n  {Passes(120)}\n");
            }
            Debug.Log("LETTERA_PROBE\n" + report);
        }

        [Test]
        public void ProbeSixtyFourLabels()
        {
            GameObject canvas = MakeCanvas();
            var labels = new LetteraText[64];
            for (int i = 0; i < 64; i++) labels[i] = Label(canvas.transform, "0", 18, new Vector2(100, 30));
            Canvas.ForceUpdateCanvases();
            var strings = new string[1000];
            for (int i = 0; i < strings.Length; i++) strings[i] = i.ToString();
            var report = new StringBuilder();
            var clock = new Stopwatch();
            int tick = 0;
            for (int round = 0; round < 3; round++)
            {
                double set = 0, frame = 0;
                long bytes = 0;
                StartPasses();
                for (int f = 0; f < 240; f++)
                {
                    tick++;
                    long a = System.GC.GetTotalMemory(false);
                    clock.Restart();
                    for (int i = 0; i < 64; i++) labels[i].Text = strings[(tick + i) % strings.Length];
                    set += clock.Elapsed.TotalMilliseconds;
                    clock.Restart();
                    Canvas.ForceUpdateCanvases();
                    GlyphAtlas.Flush();
                    frame += clock.Elapsed.TotalMilliseconds;
                    bytes += System.GC.GetTotalMemory(false) - a;
                }
                TextEngine.TimePasses = false;
                report.Append($"round {round} per frame ms: set {set / 240:0.000} canvas {frame / 240:0.000}; bytes {bytes / 240}\n  {Passes(240)}\n");
            }
            Debug.Log("LETTERA_PROBE\n" + report);
        }
        [Test]
        public void ProbeHarnessLongRows()
        {
            GameObject canvas = MakeCanvas();
            var report = new StringBuilder();
            var clock = new Stopwatch();
            for (int round = 0; round < 3; round++)
            {
                foreach (int size in new[] { 10, 10, 100, 1000 })
                {
                    string page = Page(size * 1024);
                    LetteraText label = Label(canvas.transform, "", 14, new Vector2(800, 100000));
                    Canvas.ForceUpdateCanvases();
                    int gcs = System.GC.CollectionCount(0);
                    clock.Restart();
                    label.Text = page;
                    label.ForceRebuild();
                    double set = clock.Elapsed.TotalMilliseconds;
                    Canvas.ForceUpdateCanvases();
                    report.Append($"round {round} {size} KB: set {set:0.00} ms, with frame {clock.Elapsed.TotalMilliseconds:0.00} ms, collections {System.GC.CollectionCount(0) - gcs}\n");
                    Object.DestroyImmediate(label.gameObject);
                }
            }
            Debug.Log("LETTERA_PROBE\n" + report);
        }
    }
}

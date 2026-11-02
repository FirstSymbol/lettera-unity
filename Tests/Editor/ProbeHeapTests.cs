using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace FirstSymbol.Lettera.Editor.Tests
{
    // Probe (temporary): what a label costs at birth, whether a destroyed one is let go, and what a long text allocates per pass.
    public class ProbeHeapTests : TextFixture
    {
        static readonly Unity.Profiling.ProfilerRecorder s_Frame = Unity.Profiling.ProfilerRecorder.StartNew(Unity.Profiling.ProfilerCategory.Memory, "GC Allocated In Frame");
        static string Sentence(int i)
        {
            const string Words = "The quick brown fox jumps over a lazy dog and then runs back home again ";
            return Words.Substring(i % 20, 12 + i * 7 % 29);
        }

        Transform Canvas()
        {
            var go = new GameObject("Canvas", typeof(RectTransform), typeof(Canvas));
            Created.Add(go);
            go.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            return go.transform;
        }

        LetteraText Label(Transform parent, string text, float size, Vector2 box, long[] steps = null)
        {
            long t0 = TextEngine.ProbeAllocated();
            var go = new GameObject("Label", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            ((RectTransform)go.transform).sizeDelta = box;
            long t1 = TextEngine.ProbeAllocated();
            var label = go.AddComponent<LetteraText>();
            long t2 = TextEngine.ProbeAllocated();
            label.raycastTarget = false;
            label.Font = Latin;
            label.FontSize = size;
            long t3 = TextEngine.ProbeAllocated();
            label.Text = text;
            long t4 = TextEngine.ProbeAllocated();
            if (steps != null)
            {
                steps[0] += t1 - t0; steps[1] += t2 - t1; steps[2] += t3 - t2; steps[3] += t4 - t3;
            }
            return label;
        }

        [UnityTest]
        public IEnumerator ProbeLabels()
        {
            Transform canvas = Canvas();
            var steps = new long[6];
            var labels = new List<LetteraText>();
            for (int i = 0; i < 50; i++) labels.Add(Label(canvas, Sentence(i), 18, new Vector2(400, 30)));
            foreach (LetteraText l in labels) l.ForceRebuild();
            yield return null;
            foreach (LetteraText l in labels) UnityEngine.Object.DestroyImmediate(l.gameObject);
            labels.Clear();
            GC.Collect();
            for (int i = 0; i < 1000; i++) labels.Add(Label(canvas, Sentence(i), 18, new Vector2(400, 30), steps));
            TextEngine.TimePasses = true;
            Array.Clear(TextEngine.PassBytes, 0, TextEngine.PassBytes.Length);
            long before = TextEngine.ProbeAllocated();
            foreach (LetteraText l in labels) l.ForceRebuild();
            steps[4] = TextEngine.ProbeAllocated() - before;
            TextEngine.TimePasses = false;
            Debug.Log("LETTERA_PROBE label passes " + string.Join(" ", Array.ConvertAll(TextEngine.PassBytes, b => b / 1000)));
            UnityEngine.Canvas.ForceUpdateCanvases();
            yield return null;
            steps[5] = s_Frame.LastValue;
            Debug.Log($"LETTERA_PROBE per label: go {steps[0] / 1000} add {steps[1] / 1000} font {steps[2] / 1000} text {steps[3] / 1000} rebuild {steps[4] / 1000} frame {steps[5] / 1000}");
            var weak = new List<WeakReference>();
            foreach (LetteraText l in labels) weak.Add(new WeakReference(((ITextComponent)l).SharedEngine));
            var weakLabels = new List<WeakReference>();
            foreach (LetteraText l in labels) weakLabels.Add(new WeakReference(l));
            long heapBefore = GC.GetTotalMemory(true);
            foreach (LetteraText l in labels) UnityEngine.Object.DestroyImmediate(l.gameObject);
            labels.Clear();
            yield return null;
            yield return null;
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            long heapAfter = GC.GetTotalMemory(true);
            int alive = 0, aliveLabels = 0;
            foreach (WeakReference w in weak) if (w.IsAlive) alive++;
            foreach (WeakReference w in weakLabels) if (w.IsAlive) aliveLabels++;
            Debug.Log($"LETTERA_PROBE destroyed 1000: engines alive {alive}, labels alive {aliveLabels}, heap {heapBefore / 1024} KB to {heapAfter / 1024} KB");
        }

        [UnityTest]
        public IEnumerator ProbeLongText()
        {
            Transform canvas = Canvas();
            var page = new StringBuilder();
            foreach (int size in new[] { 10, 10, 100, 1000 })
            {
                page.Clear();
                int i = 0;
                while (page.Length < size * 1024) page.Append(Sentence(i++)).Append(i % 8 == 0 ? "\n" : " ");
                string text = page.ToString();
                LetteraText big = Label(canvas, "", 14, new Vector2(800, 100000));
                yield return null;
                TextEngine.TimePasses = true;
                Array.Clear(TextEngine.PassTicks, 0, TextEngine.PassTicks.Length);
                Array.Clear(TextEngine.PassBytes, 0, TextEngine.PassBytes.Length);
                long before = TextEngine.ProbeAllocated();
                big.Text = text;
                big.ForceRebuild();
                long set = TextEngine.ProbeAllocated() - before;
                string passes = string.Join(" ", TextEngine.PassBytes);
                Array.Clear(TextEngine.PassBytes, 0, TextEngine.PassBytes.Length);
                var frames = new List<long>();
                for (int f = 0; f < 8; f++)
                {
                    UnityEngine.Canvas.ForceUpdateCanvases();
                    yield return null;
                    frames.Add(s_Frame.LastValue);
                }
                TextEngine.TimePasses = false;
                Debug.Log($"LETTERA_PROBE long {size} KB: set {set / 1024} KB passes [{passes}] then frames KB {string.Join(" ", frames.ConvertAll(x => x / 1024))} later passes [{string.Join(" ", TextEngine.PassBytes)}] pending {((ITextComponent)big).SharedEngine.PendingParagraphs}");
                UnityEngine.Object.DestroyImmediate(big.gameObject);
            }
        }


        static Dictionary<string, int> Lengths(object o, string prefix, Dictionary<string, int> into)
        {
            if (o == null) return into;
            foreach (var f in o.GetType().GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public))
            {
                object v = f.GetValue(o);
                if (v is Array a) into[prefix + f.Name] = a.Length;
                else if (v is System.Collections.ICollection c && v.GetType().GetProperty("Capacity") != null) into[prefix + f.Name] = (int)v.GetType().GetProperty("Capacity").GetValue(v);
            }
            return into;
        }

        [UnityTest]
        public IEnumerator ProbeScroll()
        {
            Transform canvas = Canvas();
            var page = new StringBuilder();
            int n = 0;
            while (page.Length < 1000 * 1024) page.Append(Sentence(n++)).Append(n % 8 == 0 ? '\n' : ' ');
            var view = new GameObject("View", typeof(RectTransform));
            view.transform.SetParent(canvas, false);
            ((RectTransform)view.transform).sizeDelta = new Vector2(800, 600);
            LetteraText label = Label(view.transform, "", 14, new Vector2(800, 600));
            label.OpenDocument(LetteraRope.FromString(page.ToString()), takeOwnership: true);
            var scroller = view.AddComponent<LetteraDocumentScroller>();
            scroller.Text = label;
            label.ForceRebuild();
            scroller.ScrollToEnd();
            double end = scroller.Position;
            scroller.ScrollToStart();
            for (int i = 0; i < 5; i++) { UnityEngine.Canvas.ForceUpdateCanvases(); yield return null; }
            TextEngine engine = ((ITextComponent)label).SharedEngine;
            var log = new StringBuilder();
            for (int step = 1; step <= 240; step++)
            {
                var before = Lengths(engine, "e.", new Dictionary<string, int>());
                Lengths(engine.View, "v.", before);
                Lengths(label, "l.", before);
                Lengths(scroller, "s.", before);
                Lengths(typeof(TextEngine).GetField("m_BlockMeasure", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).GetValue(engine), "m.", before);
                TextEngine.TimePasses = true;
                Array.Clear(TextEngine.PassBytes, 0, 5);
                long a0 = TextEngine.ProbeAllocated();
                scroller.Position = step / 240.0 * end;
                long a1 = TextEngine.ProbeAllocated();
                UnityEngine.Canvas.ForceUpdateCanvases();
                long a2 = TextEngine.ProbeAllocated();
                var after = Lengths(engine, "e.", new Dictionary<string, int>());
                Lengths(engine.View, "v.", after);
                Lengths(label, "l.", after);
                Lengths(scroller, "s.", after);
                Lengths(typeof(TextEngine).GetField("m_BlockMeasure", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).GetValue(engine), "m.", after);
                yield return null;
                long frame = s_Frame.LastValue;
                if (frame > 20000 || a2 - a0 > 20000)
                {
                    log.Append($"step {step} frame {frame} move {a1 - a0} canvas {a2 - a1} lean {engine.View.IsLean} window {engine.WindowStart}+{engine.WindowLength} passes {string.Join(",", TextEngine.PassBytes)}:");
                    foreach (var kv in after)
                        if (!before.TryGetValue(kv.Key, out int was) || was != kv.Value) log.Append($" {kv.Key} {was}->{kv.Value}");
                    log.Append(';');
                }
            }
            Debug.Log("LETTERA_PROBE scroll " + log);
        }

    }
}

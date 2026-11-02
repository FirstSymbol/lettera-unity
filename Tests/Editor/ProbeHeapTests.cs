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
            long t0 = GC.GetAllocatedBytesForCurrentThread();
            var go = new GameObject("Label", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            ((RectTransform)go.transform).sizeDelta = box;
            long t1 = GC.GetAllocatedBytesForCurrentThread();
            var label = go.AddComponent<LetteraText>();
            long t2 = GC.GetAllocatedBytesForCurrentThread();
            label.raycastTarget = false;
            label.Font = Latin;
            label.FontSize = size;
            long t3 = GC.GetAllocatedBytesForCurrentThread();
            label.Text = text;
            long t4 = GC.GetAllocatedBytesForCurrentThread();
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
            long before = GC.GetAllocatedBytesForCurrentThread();
            foreach (LetteraText l in labels) l.ForceRebuild();
            steps[4] = GC.GetAllocatedBytesForCurrentThread() - before;
            before = GC.GetAllocatedBytesForCurrentThread();
            UnityEngine.Canvas.ForceUpdateCanvases();
            yield return null;
            steps[5] = GC.GetAllocatedBytesForCurrentThread() - before;
            Debug.Log($"LETTERA_PROBE per label: go {steps[0] / 1000} add {steps[1] / 1000} font {steps[2] / 1000} text {steps[3] / 1000} rebuild {steps[4] / 1000} frame {steps[5] / 1000}");
            var weak = new List<WeakReference>();
            foreach (LetteraText l in labels) weak.Add(new WeakReference(((ITextComponent)l).SharedEngine));
            var weakLabels = new List<WeakReference>();
            foreach (LetteraText l in labels) weakLabels.Add(new WeakReference(l));
            long heapBefore = GC.GetTotalMemory(true);
            foreach (LetteraText l in labels) UnityEngine.Object.Destroy(l.gameObject);
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
                long before = GC.GetAllocatedBytesForCurrentThread();
                big.Text = text;
                big.ForceRebuild();
                long set = GC.GetAllocatedBytesForCurrentThread() - before;
                string passes = string.Join(" ", TextEngine.PassBytes);
                Array.Clear(TextEngine.PassBytes, 0, TextEngine.PassBytes.Length);
                var frames = new List<long>();
                for (int f = 0; f < 8; f++)
                {
                    before = GC.GetAllocatedBytesForCurrentThread();
                    UnityEngine.Canvas.ForceUpdateCanvases();
                    LetteraMotionHubTick();
                    yield return null;
                    frames.Add(GC.GetAllocatedBytesForCurrentThread() - before);
                }
                TextEngine.TimePasses = false;
                Debug.Log($"LETTERA_PROBE long {size} KB: set {set / 1024} KB passes [{passes}] then frames KB {string.Join(" ", frames.ConvertAll(x => x / 1024))} later passes [{string.Join(" ", TextEngine.PassBytes)}] pending {((ITextComponent)big).SharedEngine.PendingParagraphs}");
                UnityEngine.Object.DestroyImmediate(big.gameObject);
            }
        }

        static void LetteraMotionHubTick() { }
    }
}

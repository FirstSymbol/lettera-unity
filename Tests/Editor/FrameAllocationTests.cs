using System;
using System.Collections.Generic;
using System.Text;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;

namespace FirstSymbol.Lettera.Editor.Tests
{
    /// <summary>Frames of text that changes all the time allocate nothing, the first of them included.</summary>
    public class FrameAllocationTests : TextFixture
    {
        readonly List<GameObject> m_Roots = new List<GameObject>();

        [TearDown]
        public void DestroyRoots()
        {
            foreach (GameObject root in m_Roots)
                if (root != null)
                    UnityEngine.Object.DestroyImmediate(root);
            m_Roots.Clear();
        }

        Transform MakeCanvas()
        {
            var go = new GameObject("Canvas", typeof(Canvas));
            m_Roots.Add(go);
            return go.transform;
        }

        LetteraText Label(Transform parent, string body, Vector2 size)
        {
            var go = new GameObject("Label", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            ((RectTransform)go.transform).sizeDelta = size;
            var text = go.AddComponent<LetteraText>();
            text.raycastTarget = false;
            text.Font = Latin;
            text.FontSize = 18;
            text.Text = body;
            return text;
        }

        static void Frame()
        {
            Canvas.ForceUpdateCanvases();
            GlyphAtlas.Flush();
        }

        // Each frame's allocation, less what a frame with nothing to do allocates here, and the worst frames by name.
        static void AssertFrames(Action<int> change, int frames, long budget)
        {
            for (int i = 0; i < 5; i++) Frame();
            long quiet = long.MaxValue;
            for (int i = 0; i < 5; i++)
            {
                long before = GC.GetAllocatedBytesForCurrentThread();
                Frame();
                quiet = Math.Min(quiet, GC.GetAllocatedBytesForCurrentThread() - before);
            }
            var bytes = new long[frames];
            for (int i = 0; i < frames; i++)
            {
                long before = GC.GetAllocatedBytesForCurrentThread();
                change(i);
                Frame();
                bytes[i] = GC.GetAllocatedBytesForCurrentThread() - before - quiet;
            }
            long largest = 0;
            var report = new StringBuilder();
            for (int i = 0; i < frames; i++)
            {
                largest = Math.Max(largest, bytes[i]);
                if (bytes[i] > budget && report.Length < 600) report.Append(" frame ").Append(i).Append(": ").Append(bytes[i]);
            }
            Assert.LessOrEqual(largest, budget, "a quiet frame allocates " + quiet + ";" + report);
        }

        [Test]
        public void SixtyFourScoreLabelsAllocateNothingInAnyFrame()
        {
            Transform canvas = MakeCanvas();
            var labels = new List<LetteraText>();
            for (int i = 0; i < 64; i++) labels.Add(Label(canvas, "Score: 00000", new Vector2(200, 30)));
            var strings = new string[1000];
            for (int i = 0; i < strings.Length; i++) strings[i] = "Score: " + (i * 37).ToString("00000");
            AssertFrames(frame =>
            {
                for (int i = 0; i < labels.Count; i++) labels[i].SetText(strings[(frame + 1 + i) % strings.Length]);
            }, 240, 256);
        }

        [Test]
        public void SixtyFourCountersAllocateNothingInAnyFrame()
        {
            Transform canvas = MakeCanvas();
            var labels = new List<LetteraText>();
            for (int i = 0; i < 64; i++) labels.Add(Label(canvas, "0", new Vector2(100, 30)));
            var strings = new string[1000];
            for (int i = 0; i < strings.Length; i++) strings[i] = i.ToString();
            AssertFrames(frame =>
            {
                for (int i = 0; i < labels.Count; i++) labels[i].Text = strings[(frame + 1 + i) % strings.Length];
            }, 240, 256);
        }

        [Test]
        public void AColumnScrollingUnderAMaskAllocatesNothingInAnyFrame()
        {
            Transform canvas = MakeCanvas();
            var view = new GameObject("Mask", typeof(RectTransform), typeof(RectMask2D));
            view.transform.SetParent(canvas, false);
            ((RectTransform)view.transform).sizeDelta = new Vector2(800, 400);
            var content = new GameObject("Content", typeof(RectTransform));
            content.transform.SetParent(view.transform, false);
            var rect = (RectTransform)content.transform;
            const string Words = "The quick brown fox jumps over a lazy dog and then runs back home again ";
            for (int i = 0; i < 200; i++)
            {
                LetteraText label = Label(content.transform, Words.Substring(i % 20, 12 + i * 7 % 29), new Vector2(760, 26));
                ((RectTransform)label.transform).anchoredPosition = new Vector2(0, 180 - i * 30);
            }
            AssertFrames(frame => rect.anchoredPosition = new Vector2(0, Mathf.PingPong((frame + 1) * 12f, 5600)), 240, 256);
        }

        // The row R10 of the targets: a 1 MB document scrolled a new viewport each frame, across the threads as a player does.
        [Test]
        public void AFastScrollThroughALongDocumentAllocatesNothingInAnyFrame()
        {
            LetteraWorkerMode mode = LetteraWorkers.Mode;
            LetteraWorkers.Mode = LetteraWorkerMode.Parallel;
            try
            {
                Transform canvas = MakeCanvas();
                var view = new GameObject("View", typeof(RectTransform));
                view.transform.SetParent(canvas, false);
                ((RectTransform)view.transform).sizeDelta = new Vector2(800, 600);
                LetteraText label = Label(view.transform, "", new Vector2(800, 600));
                label.FontSize = 14;
                const string Words = "The quick brown fox jumps over a lazy dog and then runs back home again ";
                var page = new StringBuilder();
                for (int i = 0; page.Length < 1000 * 1024; ) page.Append(Words.Substring(i % 20, 12 + i * 7 % 29)).Append(++i % 8 == 0 ? '\n' : ' ');
                label.OpenDocument(LetteraRope.FromString(page.ToString()), true);
                var scroller = view.AddComponent<LetteraDocumentScroller>();
                scroller.Text = label;
                label.ForceRebuild();
                scroller.ScrollToEnd();
                double end = scroller.Position;
                scroller.ScrollToStart();
                AssertFrames(frame => scroller.Position = (frame + 1) / 240.0 * end, 240, 4096);
            }
            finally
            {
                LetteraWorkers.Mode = mode;
            }
        }
    }
}

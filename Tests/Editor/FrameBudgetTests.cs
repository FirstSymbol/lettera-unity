using System.Collections;
using System.Collections.Generic;
using System.Text;
using NUnit.Framework;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.TestTools;

namespace FirstSymbol.Lettera.Editor.Tests
{
    /// <summary>What a frame allocates, read from the engine's own counter over real frames, less what an idle frame of the editor allocates.</summary>
    public class FrameBudgetTests : TextFixture
    {
        static readonly ProfilerRecorder s_Gc = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame");
        static byte[] s_Control;

        static string Body(int length)
        {
            var body = new StringBuilder();
            const string Words = "The quick brown fox jumps over a lazy dog and then runs back home again ";
            for (int i = 0; body.Length < length; i++) body.Append(Words, i % 20, 12 + i * 7 % 29).Append(i % 8 == 7 ? '\n' : ' ');
            body.Length = length;
            return body.ToString();
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

        Transform Canvas()
        {
            var go = new GameObject("Canvas", typeof(RectTransform), typeof(Canvas));
            Created.Add(go);
            go.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            return go.transform;
        }

        static long Median(List<long> values)
        {
            values.Sort();
            return values[values.Count / 2];
        }

        IEnumerator Idle(List<long> into)
        {
            for (int i = 0; i < 20; i++)
            {
                UnityEngine.Canvas.ForceUpdateCanvases();
                yield return null;
                into.Add(s_Gc.LastValue);
            }
        }

        [UnityTest]
        public IEnumerator AKeystrokeInALongFieldInPlayMakesNoStringOfTheText()
        {
            Assume.That(s_Gc.Valid, "the allocation counter");
            TextEngine.PendingSourceInEditMode = true;
            try
            {
                LetteraText label = Label(Canvas(), Body(2000), 18, new Vector2(1100, 640));
                label.raycastTarget = true;
                var field = label.gameObject.AddComponent<LetteraEditable>();
                field.Selectable.Activate();
                label.ForceRebuild();
                field.Selectable.Select(field.Length, field.Length);
                var keys = new string[26];
                for (int i = 0; i < 26; i++) keys[i] = ((char)('a' + i)).ToString();
                var idle = new List<long>();
                yield return Idle(idle);
                var typed = new List<long>();
                for (int k = 0; k < 40; k++)
                {
                    field.InsertText(keys[k % 26], "user.type");
                    UnityEngine.Canvas.ForceUpdateCanvases();
                    yield return null;
                    typed.Add(s_Gc.LastValue);
                }
                long extra = Median(typed) - Median(idle);
                Debug.Log($"LETTERA_BUDGET keystroke_2000_bytes={extra}");
                // The text is 4 KB as a string: a keystroke stays far below that.
                Assert.Less(extra, 1024, "a keystroke allocates no copy of the text");
                Assert.AreEqual(2040, field.Length);
            }
            finally
            {
                TextEngine.PendingSourceInEditMode = false;
            }
        }

        [UnityTest]
        public IEnumerator ScrollingADocumentFasterThanItsWindowAllocatesNoBuffer()
        {
            Assume.That(s_Gc.Valid, "the allocation counter");
            Transform canvas = Canvas();
            var view = new GameObject("View", typeof(RectTransform));
            view.transform.SetParent(canvas, false);
            ((RectTransform)view.transform).sizeDelta = new Vector2(800, 600);
            LetteraText label = Label(view.transform, "", 14, new Vector2(800, 600));
            label.OpenDocument(LetteraRope.FromString(Body(300 * 1024)), takeOwnership: true);
            var scroller = view.AddComponent<LetteraDocumentScroller>();
            scroller.Text = label;
            label.ForceRebuild();
            var idle = new List<long>();
            yield return Idle(idle);
            // Warm: the first lean windows size what they need.
            double step = label.View.ViewportHeight * 1.8;
            for (int i = 0; i < 10; i++)
            {
                scroller.Position += step;
                UnityEngine.Canvas.ForceUpdateCanvases();
                yield return null;
            }
            var scrolled = new List<long>();
            int lean = 0;
            for (int i = 0; i < 40; i++)
            {
                scroller.Position += step;
                lean += label.View.IsLean ? 1 : 0;
                UnityEngine.Canvas.ForceUpdateCanvases();
                yield return null;
                scrolled.Add(s_Gc.LastValue);
            }
            long extra = Median(scrolled) - Median(idle);
            Debug.Log($"LETTERA_BUDGET document_fling_bytes={extra} lean={lean}");
            Assert.AreEqual(40, lean, "every window of a fling is lean");
            // A lean window's text is some 10 KB as characters: a frame stays far below that, and the player counts none of it.
            Assert.Less(extra, 1024, "a frame that builds a new window allocates no buffer of it");
        }

        [UnityTest]
        public IEnumerator ScrollingADocumentToItsEndGrowsNoBufferOnTheWay()
        {
            Assume.That(s_Gc.Valid, "the allocation counter");
            Transform canvas = Canvas();
            var view = new GameObject("View", typeof(RectTransform));
            view.transform.SetParent(canvas, false);
            ((RectTransform)view.transform).sizeDelta = new Vector2(800, 600);
            LetteraText label = Label(view.transform, "", 14, new Vector2(800, 600));
            label.OpenDocument(LetteraRope.FromString(Body(1000 * 1024)), takeOwnership: true);
            var scroller = view.AddComponent<LetteraDocumentScroller>();
            scroller.Text = label;
            label.ForceRebuild();
            scroller.ScrollToEnd();
            double end = scroller.Position;
            scroller.ScrollToStart();
            for (int i = 0; i < 5; i++)
            {
                UnityEngine.Canvas.ForceUpdateCanvases();
                yield return null;
            }
            // The comparison's scroll: the whole document in 240 frames. The last windows are short, and the last one takes its margins again.
            long control = s_Gc.CurrentValue;
            s_Control = new byte[40000];
            control = s_Gc.CurrentValue - control;
            Assume.That(control, Is.GreaterThanOrEqualTo(40000), "the counter counts inside a frame");
            long largest = 0;
            int at = 0;
            for (int step = 1; step <= 240; step++)
            {
                long before = s_Gc.CurrentValue;
                scroller.Position = step / 240.0 * end;
                UnityEngine.Canvas.ForceUpdateCanvases();
                long made = s_Gc.CurrentValue - before;
                if (made > largest)
                {
                    largest = made;
                    at = step;
                }
                yield return null;
            }
            Debug.Log($"LETTERA_BUDGET document_scroll_largest={largest} at={at}");
            Assert.Less(largest, 32 * 1024, "no window on the way, nor the last, grows a buffer");
        }
    }
}

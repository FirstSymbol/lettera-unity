using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

namespace FirstSymbol.Lettera.Tests
{
    /// <summary>Motion and ranges in a running player loop: delivery through a real EventSystem, and what a frame costs.</summary>
    public class MotionPlayTests
    {
        sealed class FakeOpener : ILetteraLinkOpener
        {
            public readonly List<string> Opened = new List<string>();
            public void Open(string url) => Opened.Add(url);
        }

        GameObject m_Canvas, m_Events;
        FakeOpener m_Opener;

        [SetUp]
        public void SetUp()
        {
            m_Canvas = new GameObject("Canvas", typeof(Canvas), typeof(GraphicRaycaster));
            m_Canvas.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            m_Events = new GameObject("Events", typeof(EventSystem));
            m_Opener = new FakeOpener();
            LetteraLinks.Opener = m_Opener;
            LetteraSelectable.ClassifyOverride = _ => LetteraPointerKind.Mouse;
        }

        [TearDown]
        public void TearDown()
        {
            LetteraLinks.Opener = null;
            LetteraSelectable.ClassifyOverride = null;
            LetteraMotionClock.Override = null;
            Object.Destroy(m_Canvas);
            Object.Destroy(m_Events);
        }

        LetteraText MakeText(string markup, Transform parent = null, float width = 600, float height = 80)
        {
            var go = new GameObject("Text", typeof(RectTransform));
            go.transform.SetParent(parent != null ? parent : m_Canvas.transform, false);
            ((RectTransform)go.transform).sizeDelta = new Vector2(width, height);
            var text = go.AddComponent<LetteraText>();
            text.FontSize = 24;
            text.Text = markup;
            return text;
        }

        static Vector2 ScreenOf(LetteraText text, int start, int end)
        {
            var rects = new List<Rect>();
            text.GetRangeRects(start, end, rects);
            return text.transform.TransformPoint(rects[0].center);
        }

        static PointerEventData Data(Vector2 point) =>
            new PointerEventData(EventSystem.current) { position = point, pressPosition = point, button = PointerEventData.InputButton.Left, pointerId = -1 };

        sealed class ClickCounter : MonoBehaviour, IPointerClickHandler, IPointerDownHandler, IPointerUpHandler
        {
            public int Downs, Ups, Clicks;
            public void OnPointerDown(PointerEventData e) => Downs++;
            public void OnPointerUp(PointerEventData e) => Ups++;
            public void OnPointerClick(PointerEventData e) => Clicks++;
        }

        [UnityTest]
        public IEnumerator AHiddenInputArrivesWithARangeAndNotBefore()
        {
            LetteraText plain = MakeText("plain");
            yield return null;
            Assert.IsNull(plain.GetComponent<LetteraRangeInput>());
            LetteraText linked = MakeText("<a=https://a.io>here</a>");
            yield return null;
            var input = linked.GetComponent<LetteraRangeInput>();
            Assert.IsNotNull(input);
            Assert.IsTrue((input.hideFlags & HideFlags.HideInInspector) != 0);
        }

        [UnityTest]
        public IEnumerator UnderAParentThePressOutsideTheLinkGoesUpAndOnItStays()
        {
            var parent = new GameObject("Parent", typeof(RectTransform));
            parent.transform.SetParent(m_Canvas.transform, false);
            var counter = parent.AddComponent<ClickCounter>();
            LetteraText text = MakeText("<a=https://a.io>link</a> and a long tail of words", parent.transform);
            yield return null;
            text.ForceRebuild();
            var input = text.GetComponent<LetteraRangeInput>();

            Vector2 on = ScreenOf(text, 0, 4);
            input.OnPointerDown(Data(on));
            input.OnPointerUp(Data(on));
            input.OnPointerClick(Data(on));
            Assert.AreEqual(1, m_Opener.Opened.Count);
            Assert.AreEqual(0, counter.Downs, "the parent does not hear a press a link took");

            Vector2 off = ScreenOf(text, 12, 16);
            input.OnPointerDown(Data(off));
            input.OnPointerUp(Data(off));
            input.OnPointerClick(Data(off));
            Assert.AreEqual(1, m_Opener.Opened.Count);
            Assert.AreEqual(1, counter.Downs);
            Assert.AreEqual(1, counter.Ups);
            Assert.AreEqual(1, counter.Clicks);
        }

        [UnityTest]
        public IEnumerator TheRangesRouteLetsAPressThroughWhereThereIsNoRange()
        {
            LetteraText text = MakeText("<a=https://a.io>link</a> and a long tail of words");
            yield return null;
            text.ForceRebuild();
            Vector2 on = ScreenOf(text, 0, 4), off = ScreenOf(text, 12, 16);
            Assert.IsTrue(text.Raycast(on, null));
            Assert.IsTrue(text.Raycast(off, null));
            text.Route = LetteraRoute.Ranges;
            Assert.IsTrue(text.Raycast(on, null));
            Assert.IsFalse(text.Raycast(off, null));
            text.Route = LetteraRoute.None;
            Assert.IsFalse(text.Raycast(on, null));
        }

        [UnityTest]
        public IEnumerator AWaveMovesOnItsOwnWithNoRebuild()
        {
            LetteraText text = MakeText("<wave>some waving words</wave>");
            yield return null;
            yield return null;
            int rebuilds = text.RebuildCount;
            int poses = text.PoseCount;
            for (int i = 0; i < 10; i++) yield return null;
            Assert.AreEqual(rebuilds, text.RebuildCount);
            Assert.Greater(text.PoseCount, poses);
        }

        // ------------------------------------------------------------- budgets

        // GC.GetAllocatedBytesForCurrentThread always reads 0 on Unity's Mono, so this uses the live profiler counter, with a control.
        // The rest of the editor allocates at times too, so it takes the quietest of a few windows, all inside one frame.
        static long AllocatedBy(Action action)
        {
            // Only this thread: the editor's own threads allocate at times; the all-thread figure is for the message.
            using var recorder = Unity.Profiling.ProfilerRecorder.StartNew(Unity.Profiling.ProfilerCategory.Memory, "GC Allocated In Frame", 1, Unity.Profiling.ProfilerRecorderOptions.Default | Unity.Profiling.ProfilerRecorderOptions.CollectOnlyOnCurrentThread);
            using var everyThread = Unity.Profiling.ProfilerRecorder.StartNew(Unity.Profiling.ProfilerCategory.Memory, "GC Allocated In Frame");
            long control = recorder.CurrentValue;
            s_Control = new byte[64];
            Assert.GreaterOrEqual(recorder.CurrentValue - control, 32, "the allocation counter does not work here");
            long quietest = long.MaxValue, anyThread = long.MaxValue;
            for (int window = 0; window < 5; window++)
            {
                long before = recorder.CurrentValue, beforeAll = everyThread.CurrentValue;
                action();
                quietest = Math.Min(quietest, recorder.CurrentValue - before); anyThread = System.Math.Min(anyThread, everyThread.CurrentValue - beforeAll);
            }
            return quietest;
        }

        static byte[] s_Control;

        static double MillisecondsOf(int repeats, Action action)
        {
            var clock = Stopwatch.StartNew();
            for (int i = 0; i < repeats; i++) action();
            clock.Stop();
            return clock.Elapsed.TotalMilliseconds / repeats;
        }

        static string Words(int glyphs)
        {
            var builder = new System.Text.StringBuilder(glyphs);
            for (int i = 0; i < glyphs; i++) builder.Append(i % 7 == 6 ? ' ' : (char)('a' + i % 26));
            return builder.ToString();
        }

        // The default vocabulary comes after a text's own styles, and these texts have none.
        static int StyleIndexOfWave()
        {
            IReadOnlyList<LetteraStyle> styles = LetteraDefaultStyles.Styles;
            for (int i = 0; i < styles.Count; i++)
                if (styles[i].Effect is WaveMotion)
                    return i;
            return -1;
        }

        [UnityTest]
        public IEnumerator BudgetsOfAFrame()
        {
            LetteraText waving = MakeText("<wave>" + Words(2000) + "</wave>", null, 1600, 800);
            yield return null;
            yield return null;
            waving.ForceRebuild();
            for (int i = 0; i < 5; i++) LetteraMotionHub.Tick(0.016f, 0.016f);
            long waveBytes = AllocatedBy(() =>
            {
                for (int i = 0; i < 60; i++) LetteraMotionHub.Tick(0.016f, 0.016f);
            });
            double waveMs = MillisecondsOf(60, () => LetteraMotionHub.Tick(0.016f, 0.016f));
            Debug.Log($"LETTERA_BUDGET pose_2000_wave_ms={waveMs:0.000} bytes_per_60={waveBytes}");
            Object.Destroy(waving.gameObject);
            yield return null;

            var links = new System.Text.StringBuilder();
            for (int i = 0; i < 200; i++) links.Append("<link=https://a.io/").Append(i).Append(">w</link> ");
            LetteraText many = MakeText(links.ToString(), null, 1600, 800);
            yield return null;
            many.ForceRebuild();
            LetteraRangeRouter router = LetteraRangeRouter.Of((Component)many);
            Vector2 point = ScreenOf(many, 200, 201);
            router.HitTest(point, null);
            long hitBytes = AllocatedBy(() =>
            {
                for (int i = 0; i < 300; i++) router.HitTest(point, null);
            });
            double hitMicro = MillisecondsOf(300, () => router.HitTest(point, null)) * 1000;
            Debug.Log($"LETTERA_BUDGET hit_200_ranges_us={hitMicro:0.0} bytes_per_300={hitBytes}");
            Object.Destroy(many.gameObject);
            yield return null;

            LetteraText sequenced = MakeText("<wave>" + Words(100) + "</wave>", null, 1600, 200);
            yield return null;
            sequenced.ForceRebuild();
            var sequencer = sequenced.gameObject.AddComponent<LetteraSequencer>();
            for (int c = 0; c < 20; c++)
            {
                sequencer.Cues.Add(new LetteraCue
                {
                    Target = new LetteraParamTarget { Style = StyleIndexOfWave(), Parameter = "Amplitude" },
                    From = LetteraValue.Of(0),
                    To = LetteraValue.Of(0.5f),
                    Mix = LetteraMix.Add,
                    UnitSeconds = 100,
                });
            }
            sequencer.Play();
            for (int i = 0; i < 5; i++) LetteraMotionHub.Tick(0.016f, 0.016f);
            long cueBytes = AllocatedBy(() =>
            {
                for (int i = 0; i < 60; i++) LetteraMotionHub.Tick(0.016f, 0.016f);
            });
            double cueMs = MillisecondsOf(60, () => LetteraMotionHub.Tick(0.016f, 0.016f));
            Debug.Log($"LETTERA_BUDGET sequencer_20_cues_ms={cueMs:0.000} bytes_per_60={cueBytes}");
            Assert.Less(waveBytes, 200, "a wave allocates per frame");
            Assert.Less(hitBytes, 200, "a hit test allocates");
            Assert.Less(cueBytes, 200, "a playing sequencer allocates");
        }
    }
}

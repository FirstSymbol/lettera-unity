using System;
using System.Collections.Generic;
using FirstSymbol.Lettera.Editor;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace FirstSymbol.Lettera.Editor.Tests
{
    /// <summary>Walking and counting a text by units, and finding a string in it.</summary>
    public class TextUnitTests : EditingFixture
    {
        readonly List<GameObject> m_Objects = new List<GameObject>();

        [TearDown]
        public void Clean()
        {
            LetteraWordDictionaries.Unregister(LetteraDictionaryScript.Thai);
            foreach (GameObject go in m_Objects)
                if (go != null)
                    Object.DestroyImmediate(go);
            m_Objects.Clear();
        }

        static List<(int, int)> Walk(LetteraUnitWalk walk)
        {
            var spans = new List<(int, int)>();
            foreach ((int start, int end) in walk) spans.Add((start, end));
            return spans;
        }

        LetteraText CanvasText(string text, float width = 400)
        {
            var root = new GameObject("Canvas", typeof(Canvas));
            m_Objects.Add(root);
            var go = new GameObject("Text", typeof(RectTransform));
            go.transform.SetParent(root.transform, false);
            ((RectTransform)go.transform).sizeDelta = new Vector2(width, 200);
            var label = go.AddComponent<LetteraText>();
            label.Font = Latin;
            label.FontSize = 20;
            label.Text = text;
            label.ForceRebuild();
            return label;
        }

        // ------------------------------------------------------------- units

        [Test]
        public void ClustersKeepMarksEmojiAndBreaksWhole()
        {
            LetteraWorldText text = Make("éa\U0001F44D\U0001F3FD\nb");
            CollectionAssert.AreEqual(new[] { (0, 2), (2, 3), (3, 5), (5, 6), (6, 7) }, Walk(text.Units(LetteraTextUnit.Cluster)));
            Assert.AreEqual(5, text.CountUnits(LetteraTextUnit.Cluster));
        }

        [Test]
        public void WordsFollowTheWordRulesAndLeaveOutSpacesAndPunctuation()
        {
            LetteraWorldText text = Make("Hello, world! can't 3.14 \U0001F600\nsecond  line.");
            CollectionAssert.AreEqual(new[] { (0, 5), (7, 12), (14, 19), (20, 24), (25, 26), (27, 33), (35, 39) }, Walk(text.Units(LetteraTextUnit.Word)));
            Assert.AreEqual(7, text.CountUnits(LetteraTextUnit.Word));
        }

        [Test]
        public void WordsFollowAnInstalledDictionary()
        {
            LetteraWorldText text = Make("ฉันกินข้าว ok");
            Assert.AreEqual(2, text.CountUnits(LetteraTextUnit.Word), "with no dictionary a Thai run is one word");
            LetteraWordDictionaries.Register(LetteraDictionaryScript.Thai, LetteraWordDictionaryBuilder.Build(new[] { "ฉัน", "กิน", "ข้าว" }, out _));
            text.SetDirty(DirtyLevel.Shape);
            CollectionAssert.AreEqual(new[] { (0, 3), (3, 6), (6, 10), (11, 13) }, Walk(text.Units(LetteraTextUnit.Word)));
        }

        [Test]
        public void ParagraphsHoldTheirBreakAndATrailingBreakOpensNone()
        {
            LetteraWorldText text = Make("ab\n\ncd\n");
            CollectionAssert.AreEqual(new[] { (0, 3), (3, 4), (4, 7) }, Walk(text.Units(LetteraTextUnit.Paragraph)));
            LetteraWorldText empty = Make("");
            foreach (LetteraTextUnit unit in Enum.GetValues(typeof(LetteraTextUnit))) Assert.AreEqual(0, empty.CountUnits(unit), unit.ToString());
        }

        [Test]
        public void LinesAreTheLaidOutLines()
        {
            LetteraWorldText text = Make("one two three four five six seven", 120);
            text.ForceRebuild();
            IReadOnlyList<LetteraLine> lines = text.Lines;
            Assert.Greater(lines.Count, 2);
            List<(int, int)> spans = Walk(text.Units(LetteraTextUnit.Line));
            Assert.AreEqual(lines.Count, spans.Count);
            for (int i = 0; i < lines.Count; i++) Assert.AreEqual((lines[i].Start, lines[i].End), spans[i]);
            int middle = lines[1].Start + 1;
            CollectionAssert.AreEqual(new[] { (middle, lines[1].End), (lines[2].Start, lines[2].Start + 1) }, Walk(text.Units(LetteraTextUnit.Line, middle, lines[2].Start + 1)));
        }

        [Test]
        public void ARangeCutsTheUnitsThatCrossIt()
        {
            LetteraWorldText text = Make("Hello, world! 42\nnext");
            CollectionAssert.AreEqual(new[] { (2, 5), (7, 9) }, Walk(text.Units(LetteraTextUnit.Word, 2, 9)));
            Assert.AreEqual(0, text.CountUnits(LetteraTextUnit.Word, 5, 7), "only a comma and a space");
            Assert.AreEqual(7, text.CountUnits(LetteraTextUnit.Cluster, 2, 9));
            CollectionAssert.AreEqual(new[] { (14, 17), (17, 19) }, Walk(text.Units(LetteraTextUnit.Paragraph, 14, 19)));
            Assert.AreEqual(4, text.CountUnits(LetteraTextUnit.Word, -5, 1000), "a range past the ends is held to them");
        }

        [Test]
        public void AWalkInsideAWalkGivesTheSameUnits()
        {
            LetteraWorldText text = Make("alpha beta\ngamma delta epsilon");
            var nested = new List<(int, int)>();
            int clusters = 0;
            foreach ((int start, int end) in text.Units(LetteraTextUnit.Word))
            {
                nested.Add((start, end));
                clusters += text.CountUnits(LetteraTextUnit.Cluster, start, end);
            }
            CollectionAssert.AreEqual(Walk(text.Units(LetteraTextUnit.Word)), nested);
            Assert.AreEqual(26, clusters);
        }

        [Test]
        public void WalkingAndCountingAllocateNothing()
        {
            var body = new System.Text.StringBuilder();
            for (int i = 0; i < 200; i++) body.Append("Line ").Append(i).Append(" holds é some words, and \U0001F44D.\n");
            LetteraWorldText text = Make(body.ToString(), 400, 20000);
            text.ForceRebuild();
            var units = (LetteraTextUnit[])Enum.GetValues(typeof(LetteraTextUnit));
            int sum = 0;
            foreach (LetteraTextUnit unit in units) sum += text.CountUnits(unit) + text.CountUnits(unit, 100, 3000);
            int again = 0;
            long allocated = Quietest(() =>
            {
                again = 0;
                foreach (LetteraTextUnit unit in units)
                {
                    again += text.CountUnits(unit) + text.CountUnits(unit, 100, 3000);
                    foreach ((int start, int end) in text.Units(unit, 50, 500)) again += end > start ? 0 : 1;
                }
            }, 20);
            Assert.AreEqual(sum, again);
            Assert.Less(allocated, 200, "bytes in 20 rounds of every walk");
            Assert.AreEqual(200, text.CountUnits(LetteraTextUnit.Paragraph));
            Assert.AreEqual(200 * 8, text.CountUnits(LetteraTextUnit.Word));
        }

        // ------------------------------------------------------------- finding

        [Test]
        public void FindAllOnACanvasTextReportsShownIndices()
        {
            LetteraText text = CanvasText("<b>Cat</b> cat CAT caT");
            var hits = new List<LetteraSearchHit>();
            Assert.AreEqual(1, text.FindAll("cat", hits));
            Assert.AreEqual((4, 7), (hits[0].Start, hits[0].End));
            hits.Clear();
            Assert.AreEqual(4, text.FindAll("cat", hits, true));
            for (int i = 0; i < 4; i++) Assert.AreEqual((i * 4, i * 4 + 3), (hits[i].Start, hits[i].End));
            Assert.AreEqual(0, text.FindAll("", hits));
        }

        [Test]
        public void FindAllOnWorldTextsAndFields()
        {
            LetteraWorldText text = Make("aaaa x\U0001F600y\U0001F600 ΟΔΟΣ οδος");
            var hits = new List<LetteraSearchHit>();
            text.FindAll("aa", hits);
            CollectionAssert.AreEqual(new[] { (0, 2), (2, 4) }, Hits(hits), "no overlaps");
            hits.Clear();
            text.FindAll("\U0001F600", hits);
            CollectionAssert.AreEqual(new[] { (6, 7), (8, 9) }, Hits(hits), "codepoints, as the selection counts");
            hits.Clear();
            Assert.AreEqual(2, text.FindAll("οδοσ", hits, true), "a final sigma folds as a sigma");

            LetteraEditable field = Field("find me, find you");
            hits.Clear();
            Assert.AreEqual(2, field.GetComponent<LetteraWorldText>().FindAll("FIND", hits, true));
            field.Selectable.Select(hits[1].Start, hits[1].End);
            Assert.AreEqual("find", field.Selectable.SelectedPlainText());
        }

        [Test]
        public void FindAllOnADocumentReadsAllOfIt()
        {
            var body = new System.Text.StringBuilder();
            for (int i = 0; i < 3000; i++) body.Append("Row ").Append(i).Append('\n');
            body.Append("needle");
            string source = body.ToString();
            LetteraWorldText text = Make("");
            text.OpenDocument(LetteraRope.FromString(source), true);
            var hits = new List<LetteraSearchHit>();
            Assert.AreEqual(1, text.FindAll("NEEDLE", hits, true));
            Assert.AreEqual(source.Length - 6, hits[0].Start);
        }

        [Test]
        public void FindAllAllocatesNothingForAShortQuery()
        {
            var body = new System.Text.StringBuilder();
            for (int i = 0; i < 300; i++) body.Append("The quick brown fox jumps over the lazy dog. ");
            LetteraWorldText text = Make(body.ToString(), 400, 20000);
            text.ForceRebuild();
            var hits = new List<LetteraSearchHit>(1024);
            text.FindAll("the", hits, true);
            hits.Clear();
            int found = 0;
            long allocated = Quietest(() =>
            {
                hits.Clear();
                found = text.FindAll("fox", hits) + text.FindAll("THE LAZY", hits, true);
            }, 20);
            Assert.AreEqual(600, found);
            Assert.Less(allocated, 200, "bytes in 20 rounds");
        }

        // The least a body allocated over a few windows of rounds, by the profiler's counter, which must see a control allocation first.
        static long Quietest(Action body, int rounds)
        {
            using var recorder = Unity.Profiling.ProfilerRecorder.StartNew(Unity.Profiling.ProfilerCategory.Memory, "GC Allocated In Frame");
            long control = recorder.CurrentValue;
            for (int i = 0; i < rounds; i++) GC.KeepAlive(new int[64]);
            Assert.Greater(recorder.CurrentValue - control, 200, "the counter sees an allocation");
            long quietest = long.MaxValue;
            for (int window = 0; window < 5; window++)
            {
                long before = recorder.CurrentValue;
                for (int i = 0; i < rounds; i++) body();
                quietest = Math.Min(quietest, recorder.CurrentValue - before);
            }
            return quietest;
        }

        static List<(int, int)> Hits(List<LetteraSearchHit> hits)
        {
            var list = new List<(int, int)>();
            foreach (LetteraSearchHit hit in hits) list.Add((hit.Start, hit.End));
            return list;
        }
    }
}

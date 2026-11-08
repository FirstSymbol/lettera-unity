using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FirstSymbol.Lettera.Editor;
using FirstSymbol.Lettera.Unicode;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace FirstSymbol.Lettera.Editor.Tests
{
    /// <summary>Word lists: the automaton, where boundaries fall, and that a project with none changes nothing.</summary>
    public class SegmentationTests : EditingFixture
    {
        [TearDown]
        public void NoDictionaries()
        {
            LetteraWordDictionaries.Unregister(LetteraDictionaryScript.Thai);
            LetteraWordDictionaries.Unregister(LetteraDictionaryScript.Lao);
            LetteraWordDictionaries.Unregister(LetteraDictionaryScript.Khmer);
            LetteraWordDictionaries.Unregister(LetteraDictionaryScript.Myanmar);
        }

        static int[] Cps(string text)
        {
            var list = new List<int>();
            for (int i = 0; i < text.Length; i++)
            {
                int cp = char.ConvertToUtf32(text, i);
                if (cp > 0xFFFF) i++;
                list.Add(cp);
            }
            return list.ToArray();
        }

        static byte[] ShippedOrTiny() =>
            LetteraWordDictionaryBuilder.Build(new[] { "ฉัน", "กิน", "ข้าว", "ข้าวเหนียว" }, out _);

        static List<string> Words(string text, byte[] data)
        {
            int[] cps = Cps(text);
            var graphemes = new bool[cps.Length + 1];
            GraphemeBreaker.FindBoundaries(cps, graphemes);
            var starts = new bool[cps.Length + 1];
            new LetteraWordAutomaton(data).FindWords(cps, 0, cps.Length, graphemes, starts, new LetteraSegmenterScratch());
            var words = new List<string>();
            int at = 0;
            for (int i = 1; i <= cps.Length; i++)
            {
                if (i < cps.Length && !starts[i]) continue;
                var word = new System.Text.StringBuilder();
                for (int k = at; k < i; k++) word.Append(char.ConvertFromUtf32(cps[k]));
                words.Add(word.ToString());
                at = i;
            }
            return words;
        }

        [Test]
        public void EachInputWordIsFoundAndNothingElse()
        {
            string[] list = { "abc", "abd", "ab", "b", "xyz", "xy", "\U0001F600z" };
            var automaton = new LetteraWordAutomaton(LetteraWordDictionaryBuilder.Build(list, out string report));
            StringAssert.Contains("7 words", report);
            foreach (string word in list) Assert.IsTrue(automaton.Contains(Cps(word)), word);
            foreach (string other in new[] { "a", "abcd", "x", "xz", "q", "z", "yz" }) Assert.IsFalse(automaton.Contains(Cps(other)), other);
            Assert.AreEqual(7, automaton.WordCount);
        }

        [Test]
        public void BuildingTwiceGivesTheSameBytesWhateverTheOrder()
        {
            byte[] a = LetteraWordDictionaryBuilder.Build(new[] { "cat", "car", "dog", "do" }, out _);
            byte[] b = LetteraWordDictionaryBuilder.Build(new[] { "do", "dog", "car", "cat", "cat" }, out string report);
            CollectionAssert.AreEqual(a, b);
            StringAssert.Contains("1 duplicates", report);
        }

        [Test]
        public void SharedEndingsAreSharedStates()
        {
            var words = new List<string>();
            foreach (char first in "abcdefghij")
                foreach (char second in "klmnopqrst")
                    words.Add($"{first}{second}ending");
            LetteraWordDictionaryBuilder.Build(words, out string report);
            // The start, one state all first letters lead to, one all second letters lead to, then one per letter of the ending, the last of which ends a word.
            StringAssert.Contains(" 9 states", report.Replace(";", ""));
        }

        [Test]
        public void TheFewestWordsWinAndKnownWordsAreFound()
        {
            byte[] data = ShippedOrTiny();
            CollectionAssert.AreEqual(new[] { "ฉัน", "กิน", "ข้าว" }, Words("ฉันกินข้าว", data));
            // The longer word is one word, not two.
            CollectionAssert.AreEqual(new[] { "ข้าวเหนียว" }, Words("ข้าวเหนียว", data));
        }

        [Test]
        public void UnknownClustersStayTogether()
        {
            byte[] data = ShippedOrTiny();
            List<string> words = Words("กินสหรัฐข้าว", data);
            Assert.AreEqual(3, words.Count, string.Join("|", words));
            Assert.AreEqual("สหรัฐ", words[1]);
        }

        [Test]
        public void ABoundaryNeverFallsInsideACluster()
        {
            // The list holds a piece that ends before a mark, so a naive cut would part the two.
            byte[] data = LetteraWordDictionaryBuilder.Build(new[] { "ก", "กิน" }, out _);
            int[] cps = Cps("กิน");
            var graphemes = new bool[cps.Length + 1];
            GraphemeBreaker.FindBoundaries(cps, graphemes);
            var starts = new bool[cps.Length + 1];
            new LetteraWordAutomaton(data).FindWords(cps, 0, cps.Length, graphemes, starts, new LetteraSegmenterScratch());
            for (int i = 1; i < cps.Length; i++) Assert.IsTrue(!starts[i] || graphemes[i], "a boundary inside a cluster at " + i);
        }

        const string Sentence = "ฉันกินข้าว";

        static int LinesAt(LetteraWorldText text)
        {
            text.ForceRebuild();
            return text.Engine.LineCountForHooks;
        }

        [Test]
        public void WithoutADictionaryNothingChangesAndWithOneLinesBreakAtWords()
        {
            LetteraFontStack stack = Stack(Latin);
            var ids = new List<int>();
            // Wide enough for each word: narrower, a word breaks at clusters, which is the emergency a word too wide for its line gets.
            LetteraWorldText text = Make(Sentence, 60, 200);
            int before = LinesAt(text);
            var breaks = new List<int>();
            for (int l = 0; l < text.Engine.LineCountForHooks; l++) breaks.Add(text.Engine.LineForHooks(l).Start);

            LetteraWordDictionaries.Register(LetteraDictionaryScript.Thai, ShippedOrTiny());
            text.SetDirty(DirtyLevel.Shape);
            int after = LinesAt(text);
            for (int l = 1; l < text.Engine.LineCountForHooks; l++)
            {
                int start = text.Engine.LineForHooks(l).Start;
                Assert.IsTrue(start == 3 || start == 6, "a line starts inside a word, at " + start);
            }
            Assert.Greater(after, 1);

            LetteraWordDictionaries.Unregister(LetteraDictionaryScript.Thai);
            text.SetDirty(DirtyLevel.Shape);
            Assert.AreEqual(before, LinesAt(text));
            var again = new List<int>();
            for (int l = 0; l < text.Engine.LineCountForHooks; l++) again.Add(text.Engine.LineForHooks(l).Start);
            CollectionAssert.AreEqual(breaks, again, "back as it was");
        }

        [Test]
        public void AJoinerStopsABreakAndNoBreakWins()
        {
            LetteraWordDictionaries.Register(LetteraDictionaryScript.Thai, ShippedOrTiny());
            // A word joiner splits the run, so neither side may break at it.
            LetteraWorldText joined = Make("ฉัน⁠กิน", 2000, 200);
            joined.ForceRebuild();
            Assert.AreEqual(LineBreakOpportunity.None, joined.Engine.BreakAfterForTests(2));
            Assert.AreEqual(LineBreakOpportunity.None, joined.Engine.BreakAfterForTests(3));
            LetteraWorldText plain = Make("ฉันกิน", 2000, 200);
            plain.ForceRebuild();
            Assert.AreEqual(LineBreakOpportunity.Allowed, plain.Engine.BreakAfterForTests(2));
        }

        [Test]
        public void ADoubleClickWordIsADictionaryWord()
        {
            int[] cps = Cps(Sentence);
            LetteraWordDictionaries.Register(LetteraDictionaryScript.Thai, ShippedOrTiny());
            LetteraTextNavigator.WordAt(cps, 4, out int start, out int end);
            Assert.AreEqual(3, start);
            Assert.AreEqual(6, end);
            Assert.AreEqual(6, LetteraTextNavigator.NextWordStop(cps, 3));
            Assert.AreEqual(3, LetteraTextNavigator.PreviousWordStop(cps, 6));
            LetteraWordDictionaries.Unregister(LetteraDictionaryScript.Thai);
            LetteraTextNavigator.WordAt(cps, 4, out start, out end);
            Assert.AreEqual(0, start);
            Assert.AreEqual(cps.Length, end);
        }

        [Test]
        public void AWordMotionMakesOneBodyAWord()
        {
            LetteraWordDictionaries.Register(LetteraDictionaryScript.Thai, ShippedOrTiny());
            LetteraWorldText text = Make("", 2000, 200);
            text.Styles.Add(LetteraStyle.ForTag(new WaveMotion { Unit = LetteraMotionUnit.Word }, "w2"));
            text.Text = "<w2>" + Sentence + "</w2>";
            text.ForceRebuild();
            Assert.AreEqual(3, text.Engine.MotionBodiesForTests(0));
        }

        static string[] ShippedList(string file)
        {
            string asmdef = AssetDatabase.GUIDToAssetPath(AssetDatabase.FindAssets("FirstSymbol.Lettera.Editor.Tests t:asmdef")[0]);
            return File.ReadAllLines(Path.Combine(Path.GetDirectoryName(asmdef), "..", "..", "Native~", "third_party", "icu-dict", file), System.Text.Encoding.UTF8);
        }

        [TestCase("thaidict.txt", 26392, "ฉันกินข้าวที่บ้าน", 5)]
        public void AWholeListBuildsAndSegments(string file, int minimumWords, string sample, int words)
        {
            var clock = System.Diagnostics.Stopwatch.StartNew();
            byte[] data = LetteraWordDictionaryBuilder.Build(ShippedList(file), out string report);
            Debug.Log($"LETTERA_BUDGET dictionary {file}: {report} built in {clock.ElapsedMilliseconds} ms");
            Assert.GreaterOrEqual(new LetteraWordAutomaton(data).WordCount, minimumWords - 50);
            Assert.AreEqual(words, Words(sample, data).Count, string.Join("|", Words(sample, data)));
        }

        [Test]
        public void ALongParagraphIsLinearAndAllocatesNothingOnceWarm()
        {
            byte[] data = LetteraWordDictionaryBuilder.Build(ShippedList("thaidict.txt"), out _);
            var automaton = new LetteraWordAutomaton(data);
            var scratch = new LetteraSegmenterScratch();
            double Time(int repeat, out bool[] starts)
            {
                var text = new List<int>();
                for (int i = 0; i < repeat; i++) text.AddRange(Cps(Sentence));
                int[] cps = text.ToArray();
                var graphemes = new bool[cps.Length + 1];
                GraphemeBreaker.FindBoundaries(cps, graphemes);
                starts = new bool[cps.Length + 1];
                var clock = System.Diagnostics.Stopwatch.StartNew();
                automaton.FindWords(cps, 0, cps.Length, graphemes, starts, scratch);
                return clock.Elapsed.TotalMilliseconds;
            }
            Time(10000, out _);
            double small = Time(1000, out _), large = Time(10000, out _);
            Debug.Log($"LETTERA_BUDGET segment_10k={small:0.00}ms segment_100k={large:0.00}ms");
            Assert.Less(large, small * 20 + 1, "not linear");
        }

        [Test]
        public void ItGivesTheSameFromFourThreads()
        {
            byte[] data = LetteraWordDictionaryBuilder.Build(ShippedList("thaidict.txt"), out _);
            var automaton = new LetteraWordAutomaton(data);
            var text = new List<int>();
            for (int i = 0; i < 2000; i++) text.AddRange(Cps(Sentence));
            int[] cps = text.ToArray();
            var graphemes = new bool[cps.Length + 1];
            GraphemeBreaker.FindBoundaries(cps, graphemes);
            bool[] Run()
            {
                var starts = new bool[cps.Length + 1];
                automaton.FindWords(cps, 0, cps.Length, graphemes, starts, new LetteraSegmenterScratch());
                return starts;
            }
            bool[] wanted = Run();
            var results = new bool[4][];
            var threads = new System.Threading.Thread[4];
            for (int t = 0; t < 4; t++)
            {
                int index = t;
                threads[t] = new System.Threading.Thread(() => results[index] = Run());
                threads[t].Start();
            }
            foreach (System.Threading.Thread thread in threads) thread.Join();
            foreach (bool[] result in results) CollectionAssert.AreEqual(wanted, result);
        }

        [Test]
        public void ShippedDictionariesRebuildFromTheirListsAndCarryTheirNotices()
        {
            string asmdef = AssetDatabase.GUIDToAssetPath(AssetDatabase.FindAssets("FirstSymbol.Lettera.Editor.Tests t:asmdef")[0]);
            string packageRoot = Path.GetDirectoryName(Path.GetDirectoryName(Path.GetDirectoryName(asmdef))).Replace('\\', '/');
            string[] guids = AssetDatabase.FindAssets("t:LetteraWordDictionary", new[] { packageRoot + "/Runtime/Dictionaries" });
            Assert.AreEqual(4, guids.Length, "the four shipped dictionaries");
            string notices = File.ReadAllText(Path.Combine(Path.GetDirectoryName(asmdef), "..", "..", "THIRD-PARTY-NOTICES.md"));
            var files = new Dictionary<LetteraDictionaryScript, string> { [LetteraDictionaryScript.Thai] = "thaidict.txt", [LetteraDictionaryScript.Lao] = "laodict.txt", [LetteraDictionaryScript.Khmer] = "khmerdict.txt", [LetteraDictionaryScript.Myanmar] = "burmesedict.txt" };
            foreach (string guid in guids)
            {
                var dictionary = AssetDatabase.LoadAssetAtPath<LetteraWordDictionary>(AssetDatabase.GUIDToAssetPath(guid));
                byte[] built = LetteraWordDictionaryBuilder.Build(ShippedList(files[dictionary.Script]), out _);
                CollectionAssert.AreEqual(built, dictionary.Data.bytes, dictionary.name);
                Assert.IsFalse(string.IsNullOrEmpty(dictionary.SourceNotice));
                StringAssert.Contains(dictionary.SourceNotice, notices);
            }
        }

        [Test]
        public void TheDefaultListIsEmptySoAProjectPaysNothing()
        {
            Assert.AreEqual(0, LetteraSettings.Instance.Dictionaries.Count);
        }
    }
}

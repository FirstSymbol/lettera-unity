using System;
using System.Collections.Generic;
using NUnit.Framework;
using Unity.Profiling;

namespace FirstSymbol.Lettera.Unicode.Tests
{
    public class UnicodeUnitTests
    {
        static bool Emoji(params int[] cluster) => EmojiClassifier.IsEmojiCluster(cluster, 0, cluster.Length);

        [Test]
        public void EmojiClusters()
        {
            Assert.IsTrue(Emoji(0x1F1FA, 0x1F1F8), "flag");
            Assert.IsTrue(Emoji(0x1F468, 0x200D, 0x1F469, 0x200D, 0x1F467), "ZWJ family");
            Assert.IsTrue(Emoji(0x1F44B, 0x1F3FD), "skin tone");
            Assert.IsTrue(Emoji('1', 0xFE0F, 0x20E3), "keycap with VS16");
            Assert.IsTrue(Emoji('#', 0x20E3), "keycap without VS16");
            Assert.IsTrue(Emoji(0x2764, 0xFE0F), "heart + VS16");
            Assert.IsFalse(Emoji(0x2764, 0xFE0E), "heart + VS15");
            Assert.IsFalse(Emoji(0x2764), "bare text-presentation heart");
            Assert.IsFalse(Emoji(0x00A9), "copyright without VS16");
            Assert.IsTrue(Emoji(0x00A9, 0xFE0F), "copyright with VS16");
            Assert.IsTrue(Emoji(0x1F600), "Emoji_Presentation");
            Assert.IsFalse(Emoji(0x1F600, 0xFE0E), "Emoji_Presentation + VS15");
            Assert.IsTrue(Emoji(0x1F3F4, 0xE0067, 0xE0062, 0xE0065, 0xE006E, 0xE0067, 0xE007F), "tag flag");
            Assert.IsFalse(Emoji('a'));
            Assert.IsFalse(Emoji('1'));
            Assert.IsFalse(EmojiClassifier.IsEmojiCluster(new[] { 0x1F600 }, 5, 1), "start out of range");
            Assert.IsFalse(EmojiClassifier.IsEmojiCluster(new[] { 0x1F600 }, 0, 0), "empty");
        }

        static UnicodeScript[] Itemize(string s)
        {
            var cps = new int[s.Length];
            int n = LetteraUnicode.Utf16ToUtf32(s, cps, default);
            var r = new UnicodeScript[n];
            ScriptItemizer.Resolve(new ReadOnlySpan<int>(cps, 0, n), r);
            return r;
        }

        [Test]
        public void ScriptItemizerArabicDigitsBrackets()
        {
            var ar = UnicodeScript.Arabic;
            var la = UnicodeScript.Latin;
            // "abc (مر) 12 مر"
            var r = Itemize("abc (مر) 12 مر");
            CollectionAssert.AreEqual(new[] { la, la, la, la, la, ar, ar, la, la, la, la, la, ar, ar }, r);

            // Arabic text, digits, brackets with Latin inside: the closing bracket returns to the opener's script.
            r = Itemize("مر 123 (abc) م");
            CollectionAssert.AreEqual(new[] { ar, ar, ar, ar, ar, ar, ar, ar, la, la, la, ar, ar, ar }, r);

            // Common at the start of the text takes the following script.
            r = Itemize("123 م");
            CollectionAssert.AreEqual(new[] { ar, ar, ar, ar, ar }, r);

            // Text of neutrals only stays Common.
            r = Itemize("1 2");
            CollectionAssert.AreEqual(new[] { UnicodeScript.Common, UnicodeScript.Common, UnicodeScript.Common }, r);

            // Script_Extensions: U+3001 stays with Han, moves into a following Han run from Latin; U+0640 stays in Arabic.
            r = Itemize("日、");
            CollectionAssert.AreEqual(new[] { UnicodeScript.Han, UnicodeScript.Han }, r);
            r = Itemize("a、日");
            CollectionAssert.AreEqual(new[] { la, UnicodeScript.Han, UnicodeScript.Han }, r);
            r = Itemize("مـم");
            CollectionAssert.AreEqual(new[] { ar, ar, ar }, r);
        }

        [Test]
        public void Utf16Conversion()
        {
            var o = new int[8];
            var idx = new int[8];
            int n = LetteraUnicode.Utf16ToUtf32("a😀\uD83Db\uDE00", o, idx);
            Assert.AreEqual(5, n);
            CollectionAssert.AreEqual(new[] { 'a', 0x1F600, 0xFFFD, 'b', 0xFFFD }, new ReadOnlySpan<int>(o, 0, n).ToArray());
            CollectionAssert.AreEqual(new[] { 0, 1, 3, 4, 5 }, new ReadOnlySpan<int>(idx, 0, n).ToArray());

            Assert.AreEqual(1, LetteraUnicode.Utf16ToUtf32("\uD83D", o, default));
            Assert.AreEqual(0xFFFD, o[0]);
            Assert.AreEqual(2, LetteraUnicode.Utf16ToUtf32("abc", new int[2], default), "output too short clips");
        }

        [Test]
        public void SimpleCaseMaps()
        {
            Assert.AreEqual('A', LetteraUnicode.ToUpper('a'));
            Assert.AreEqual('a', LetteraUnicode.ToLower('A'));
            Assert.AreEqual('A', LetteraUnicode.ToTitle('a'));
            Assert.AreEqual(0x01C5, LetteraUnicode.ToTitle(0x01C6), "dz digraph title");
            Assert.AreEqual(0x01C4, LetteraUnicode.ToUpper(0x01C6));
            Assert.AreEqual(0x0416, LetteraUnicode.ToUpper(0x0436));
            Assert.AreEqual(0x10428, LetteraUnicode.ToLower(0x10400));
            Assert.AreEqual(0xDF, LetteraUnicode.ToUpper(0xDF), "sharp s has no simple upper mapping");
            Assert.AreEqual(0x5D0, LetteraUnicode.ToUpper(0x5D0));
            Assert.AreEqual(-5, LetteraUnicode.ToUpper(-5), "bad input");
            Assert.AreEqual(0x110000, LetteraUnicode.ToLower(0x110000), "bad input");
        }

        [Test]
        public void Properties()
        {
            Assert.AreEqual(GeneralCategory.Lu, LetteraUnicode.GetCategory('A'));
            Assert.AreEqual(GeneralCategory.Lo, LetteraUnicode.GetCategory(0x4E01), "CJK range");
            Assert.AreEqual(GeneralCategory.Cn, LetteraUnicode.GetCategory(0x10FFFF + 1), "bad input");
            Assert.AreEqual(GeneralCategory.Cs, LetteraUnicode.GetCategory(0xD800));
            Assert.AreEqual(UnicodeScript.Arabic, LetteraUnicode.GetScript(0x0627));
            Assert.AreEqual(UnicodeScript.Common, LetteraUnicode.GetScript(' '));
            Assert.AreEqual(UnicodeScript.Inherited, LetteraUnicode.GetScript(0x0300));
            Assert.AreEqual(UnicodeScript.Unknown, LetteraUnicode.GetScript(0x0378));
            Assert.IsTrue(LetteraUnicode.HasScriptExtension(0x0640, UnicodeScript.Arabic));
            Assert.IsTrue(LetteraUnicode.HasScriptExtension(0x0640, UnicodeScript.Syriac));
            Assert.IsFalse(LetteraUnicode.HasScriptExtension(0x0640, UnicodeScript.Latin));
            Assert.IsTrue(LetteraUnicode.HasScriptExtension('a', UnicodeScript.Latin));
            Assert.AreEqual(BidiClass.AL, LetteraUnicode.GetBidiClass(0x0627));
            Assert.AreEqual(BidiClass.R, LetteraUnicode.GetBidiClass(0x05FF), "unassigned Hebrew block default");
            Assert.AreEqual(BidiClass.ET, LetteraUnicode.GetBidiClass(0x20CF), "unassigned currency block default");
            Assert.AreEqual(')', LetteraUnicode.GetMirror('('));
            Assert.AreEqual('a', LetteraUnicode.GetMirror('a'));
            Assert.AreEqual(BidiPairedBracketType.Open, LetteraUnicode.GetPairedBracketType('['));
            Assert.AreEqual(']', LetteraUnicode.GetPairedBracket('['));
            Assert.AreEqual(LineBreakClass.SA, LetteraUnicode.GetLineBreakClass(0x0E01), "raw class");
            Assert.AreEqual(LineBreakClass.AI, LetteraUnicode.GetLineBreakClass(0x00A7), "raw class");
            Assert.AreEqual(GraphemeBreakClass.LV, LetteraUnicode.GetGraphemeBreakClass(0xAC00));
            Assert.AreEqual(WordBreakClass.Katakana, LetteraUnicode.GetWordBreakClass(0x30A2));
            Assert.AreEqual(EastAsianWidth.W, LetteraUnicode.GetEastAsianWidth(0x4E00));
            Assert.IsTrue(LetteraUnicode.IsRegionalIndicator(0x1F1E6));
            Assert.IsTrue(LetteraUnicode.IsDefaultIgnorable(0x200D));
            Assert.IsTrue(LetteraUnicode.IsWhiteSpace(0x2003));
            Assert.IsTrue(LetteraUnicode.IsMark(0x0300));
            Assert.IsFalse(LetteraUnicode.IsMark('a'));
            Assert.IsTrue(LetteraUnicode.IsJustifiableSeparator(' '));
            Assert.IsTrue(LetteraUnicode.IsJustifiableSeparator('\t'));
            Assert.IsTrue(LetteraUnicode.IsJustifiableSeparator(0x2003));
            Assert.IsFalse(LetteraUnicode.IsJustifiableSeparator(0x00A0));
            Assert.IsFalse(LetteraUnicode.IsJustifiableSeparator(0x202F));
            Assert.IsFalse(LetteraUnicode.IsJustifiableSeparator(0x2007));
            Assert.AreEqual(0x41726162u, LetteraUnicode.ScriptToOpenTypeTag(UnicodeScript.Arabic));
            Assert.AreEqual(0x5A797979u, LetteraUnicode.ScriptToOpenTypeTag(UnicodeScript.Common));
            Assert.AreEqual(0x5A696E68u, LetteraUnicode.ScriptToOpenTypeTag(UnicodeScript.Inherited));
            Assert.AreEqual(0x5A7A7A7Au, LetteraUnicode.ScriptToOpenTypeTag(UnicodeScript.Unknown));
            Assert.AreEqual(0x4C61746Eu, LetteraUnicode.ScriptToOpenTypeTag(UnicodeScript.Latin));
        }

        [Test]
        public void BadInputDoesNotThrow()
        {
            int[] bad = { -1, int.MinValue, 0x110000, int.MaxValue, 0xD800 };
            var levels = new byte[bad.Length];
            var bounds = new bool[bad.Length + 1];
            var line = new LineBreakOpportunity[bad.Length];
            var scripts = new UnicodeScript[bad.Length];

            Bidi.ResolveParagraph(bad, -1, levels);
            GraphemeBreaker.FindBoundaries(bad, bounds);
            WordBreaker.FindBoundaries(bad, bounds);
            LineBreaker.FindBreaks(bad, line);
            ScriptItemizer.Resolve(bad, scripts);
            EmojiClassifier.IsEmojiCluster(bad, 0, bad.Length);

            // Empty and undersized spans.
            Assert.AreEqual(1, Bidi.ResolveParagraph(default, 1, default));
            Assert.AreEqual(0, Bidi.ResolveParagraph(default, -1, default));
            Bidi.ResolveParagraph(bad, -1, new byte[2]);
            Bidi.ResolveLineLevels(bad, new byte[bad.Length], 3, 1, 0, new byte[4]);
            Bidi.ResolveLineLevels(bad, new byte[bad.Length], -2, 100, 0, new byte[2]);
            Bidi.ReorderLine(new byte[3], new int[1]);
            GraphemeBreaker.FindBoundaries(default, default);
            GraphemeBreaker.FindBoundaries(bad, new bool[2]);
            WordBreaker.FindBoundaries(bad, new bool[2]);
            LineBreaker.FindBreaks(bad, new LineBreakOpportunity[1]);
            ScriptItemizer.Resolve(bad, new UnicodeScript[1]);
            EmojiClassifier.IsEmojiCluster(bad, -1, 100);
        }

        static int[] MixedText(int length)
        {
            string fragment = "Hello, world! مرحبا 123 (שלום) 日本語、カタカナ \U0001F468‍\U0001F469‍\U0001F467 \U0001F1FA\U0001F1F8 ❤️ ❤︎ 1️⃣ \U0001F44B\U0001F3FD ⁧abc⁩ ‫def‬ न्त $1,234.50 \"q\" \r\n";
            var tmp = new int[fragment.Length];
            int n = LetteraUnicode.Utf16ToUtf32(fragment, tmp, default);
            var r = new int[length];
            for (int i = 0; i < length; i++) r[i] = tmp[i % n];
            return r;
        }

        [Test]
        public void NoAllocationAfterWarmUp()
        {
            // GC.GetAllocatedBytesForCurrentThread always reads 0 on Unity's Mono, so this uses the live profiler counter.
            // Control: the counter must see even a small allocation. The test stays within one frame.
            using var recorder = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame");
            long before = recorder.CurrentValue;
            GC.KeepAlive(new byte[32]);
            Assert.That(recorder.CurrentValue - before, Is.GreaterThanOrEqualTo(32), "allocation counter is not functional");

            int[] text = MixedText(1000);
            int[] backing = new int[1000];
            int[] indices = new int[1000];
            byte[] levels = new byte[1000], line = new byte[1000];
            int[] visual = new int[1000];
            bool[] bounds = new bool[1001];
            var breaks = new LineBreakOpportunity[1000];
            var scripts = new UnicodeScript[1000];
            string utf16 = new string('x', 500) + "😀\uD83D" + new string('م', 400);

            void Exercise()
            {
                int para = Bidi.ResolveParagraph(text, -1, levels);
                Bidi.ResolveLineLevels(text, levels, 0, 1000, para, line);
                Bidi.ReorderLine(line, visual);
                GraphemeBreaker.FindBoundaries(text, bounds);
                for (int i = 0; i < 1000; i++)
                {
                    int e = i + 1;
                    while (e < 1000 && !bounds[e]) e++;
                    EmojiClassifier.IsEmojiCluster(text, i, e - i);
                    i = e - 1;
                }
                WordBreaker.FindBoundaries(text, bounds);
                LineBreaker.FindBreaks(text, breaks);
                ScriptItemizer.Resolve(text, scripts);
                LetteraUnicode.Utf16ToUtf32(utf16, backing, indices);
                for (int i = 0; i < 1000; i++)
                {
                    int cp = text[i];
                    LetteraUnicode.GetCategory(cp);
                    LetteraUnicode.GetScript(cp);
                    LetteraUnicode.HasScriptExtension(cp, UnicodeScript.Arabic);
                    LetteraUnicode.GetBidiClass(cp);
                    LetteraUnicode.GetMirror(cp);
                    LetteraUnicode.GetLineBreakClass(cp);
                    LetteraUnicode.IsEmoji(cp);
                    LetteraUnicode.IsMark(cp);
                    LetteraUnicode.ToUpper(cp);
                    LetteraUnicode.ScriptToOpenTypeTag(UnicodeScript.Latin);
                }
            }

            Exercise();
            Exercise();

            before = recorder.CurrentValue;
            Exercise();
            long allocated = recorder.CurrentValue - before;
            Assert.AreEqual(0, allocated, "bytes allocated after warm-up");
        }

        [Test]
        public void ReorderSimple()
        {
            // "abc ابج" with base LTR
            int[] text = { 'a', ' ', 0x0627, 0x0628 };
            var levels = new byte[4];
            var line = new byte[4];
            var visual = new int[4];
            Assert.AreEqual(0, Bidi.ResolveParagraph(text, -1, levels));
            Bidi.ResolveLineLevels(text, levels, 0, 4, 0, line);
            CollectionAssert.AreEqual(new byte[] { 0, 0, 1, 1 }, line);
            Bidi.ReorderLine(line, visual);
            CollectionAssert.AreEqual(new[] { 0, 1, 3, 2 }, visual);
        }

        [Test]
        public void LineBreakUsage()
        {
            var cps = new List<int>();
            foreach (char c in "ab cd\ne") cps.Add(c);
            var r = new LineBreakOpportunity[cps.Count];
            LineBreaker.FindBreaks(cps.ToArray(), r);
            Assert.AreEqual(LineBreakOpportunity.None, r[0]);
            Assert.AreEqual(LineBreakOpportunity.Allowed, r[2]);
            Assert.AreEqual(LineBreakOpportunity.Mandatory, r[5]);
            Assert.AreEqual(LineBreakOpportunity.Mandatory, r[6]);
        }
    }
}

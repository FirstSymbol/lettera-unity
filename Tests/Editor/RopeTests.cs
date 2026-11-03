using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using NUnit.Framework;

namespace FirstSymbol.Lettera.Editor.Tests
{
    /// <summary>The rope against a plain model, and its files.</summary>
    public class RopeTests
    {
        readonly List<string> m_Files = new List<string>();
        readonly List<LetteraRope> m_Open = new List<LetteraRope>();

        LetteraRope Open(byte[] bytes)
        {
            LetteraRope rope = LetteraFileSource.Load(TempFile(bytes));
            m_Open.Add(rope);
            return rope;
        }

        string TempFile(byte[] bytes)
        {
            string path = Path.GetTempFileName();
            File.WriteAllBytes(path, bytes);
            m_Files.Add(path);
            return path;
        }

        [TearDown]
        public void Clean()
        {
            foreach (LetteraRope rope in m_Open) rope.Dispose();
            m_Open.Clear();
            foreach (string path in m_Files)
                if (File.Exists(path))
                    File.Delete(path);
            m_Files.Clear();
        }

        static int ModelCodepoints(string text, int end)
        {
            int count = 0;
            for (int i = 0; i < end; i++)
            {
                count++;
                if (char.IsHighSurrogate(text[i]) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
                {
                    if (i + 1 < end) i++;
                }
            }
            return count;
        }

        static int ModelParagraphs(string text, int end)
        {
            int count = 0;
            for (int i = 0; i < end; i++)
            {
                char c = text[i];
                if (c == '\r')
                {
                    if (i + 1 < text.Length && text[i + 1] == '\n')
                    {
                        if (i + 1 >= end) break;
                        i++;
                    }
                    count++;
                }
                else if (c == '\n' || c == '\u0085' || c == (char)0x2029) count++;
            }
            return count;
        }

        static void AssertMatches(LetteraRope rope, string model, Random random)
        {
            Assert.AreEqual(model.Length, rope.Length);
            rope.CheckInvariants();
            Assert.AreEqual(ModelCodepoints(model, model.Length), rope.CodepointCount);
            Assert.AreEqual(ModelParagraphs(model, model.Length) + 1, rope.ParagraphCount);
            for (int probe = 0; probe < 4; probe++)
            {
                int at = random.Next(model.Length + 1);
                Assert.AreEqual(ModelParagraphs(model, at), rope.CharToParagraph(at), "paragraph at " + at);
                // A position inside a pair has no codepoint of its own.
                if (at > 0 && at < model.Length && char.IsLowSurrogate(model[at]) && char.IsHighSurrogate(model[at - 1])) at--;
                int cp = rope.CharToCodepoint(at);
                Assert.AreEqual(ModelCodepoints(model, at), cp, "codepoint at " + at);
                Assert.AreEqual(at, rope.CodepointToChar(cp), "char of codepoint " + cp);
            }
        }

        static string Blocks(LetteraRope rope)
        {
            var shot = new StringBuilder();
            for (int b = 0; b < rope.BlockCount; b++)
            {
                rope.BlockTotals(b, out int codepoints, out int paragraphs);
                shot.Append(rope.CharOfBlock(b)).Append(' ').Append(rope.BlockChars(b)).Append(' ').Append(codepoints).Append(' ').Append(paragraphs).Append(' ')
                    .Append(rope.BlockHasMarkup(b)).Append(rope.BlockEndsParagraph(b)).Append('\n');
            }
            return shot.ToString();
        }

        // Plain letters go through in stretches: the blocks, their counts and their flags are what one character at a time gives.
        [Test]
        public void PlainStretchesCutBlocksAsCharacterByCharacterDoes()
        {
            var random = new Random(7);
            string[] pieces = { "word ", "Long", "\n", "\r\n", "<b>", "</b>", "\t", "é", "שלום", "😀", "\u0301", "x", "12345678901234567890", "\u2029", "> <" };
            var triggers = new LetteraMarkupTriggers('<');
            try
            {
                for (int round = 0; round < 12; round++)
                {
                    var text = new StringBuilder();
                    // Some rounds hold stretches past the block limit with no space, so the limit cuts inside a stretch.
                    int length = round % 3 == 0 ? 70000 : 30000;
                    while (text.Length < length)
                    {
                        if (round % 3 == 0 && random.Next(40) == 0) text.Append('a', random.Next(17000, 20000));
                        else text.Append(pieces[random.Next(pieces.Length)]);
                    }
                    string body = text.ToString();
                    foreach (LetteraMarkupTriggers set in new[] { null, triggers })
                    {
                        RopeScanner.NoPlainStretches = true;
                        string slow = Blocks(LetteraRope.FromString(body, set));
                        RopeScanner.NoPlainStretches = false;
                        Assert.AreEqual(slow, Blocks(LetteraRope.FromString(body, set)), "round " + round);
                    }
                }
            }
            finally
            {
                RopeScanner.NoPlainStretches = false;
            }
        }

        [Test]
        public void RandomEditsAgreeWithAPlainString()
        {
            var random = new Random(11);
            string[] pieces = { "abc", "word ", "\n", "\r\n", "\r", "مر", "\U0001F600", "x", "<b>", "</b>", new string('y', 900), new string('z', 5000), "é", "\u2029" };
            var model = new StringBuilder();
            LetteraRope rope = LetteraRope.FromString("");
            for (int step = 0; step < 6000; step++)
            {
                string text = model.ToString();
                int at = SafePosition(text, random.Next(text.Length + 1));
                int removeEnd = SafePosition(text, Math.Min(text.Length, at + (random.Next(3) == 0 ? random.Next(6000) : random.Next(20))));
                int removed = random.Next(3) == 0 ? removeEnd - at : 0;
                string inserted = removed == 0 || random.Next(2) == 0 ? pieces[random.Next(pieces.Length)] : "";
                if (removed == 0 && inserted.Length == 0) continue;
                rope.Replace(at, removed, inserted);
                model.Remove(at, removed).Insert(at, inserted);
                if (step % 20 == 0)
                {
                    text = model.ToString();
                    Assert.AreEqual(text, rope.ToString(), "content at step " + step);
                    AssertMatches(rope, text, random);
                }
                if (model.Length > 400000)
                {
                    model.Clear();
                    rope.SetText(ReadOnlySpan<char>.Empty);
                }
            }
        }

        // An edit never parts a surrogate pair here: a text can hold a lone half, but no position inside a pair exists for an edit to come from.
        static int SafePosition(string text, int at) => at > 0 && at < text.Length && char.IsLowSurrogate(text[at]) && char.IsHighSurrogate(text[at - 1]) ? at - 1 : at;

        [Test]
        public void ABigStringMakesBlocksOfTheRightSize()
        {
            var body = new StringBuilder();
            for (int i = 0; i < 4000; i++) body.Append("A paragraph of a few words here.\n");
            LetteraRope rope = LetteraRope.FromString(body.ToString());
            rope.CheckInvariants();
            Assert.Greater(rope.BlockCount, 20);
            Assert.AreEqual(4001, rope.ParagraphCount);
            Assert.AreEqual(body.ToString(), rope.ToString());
            int middle = rope.ParagraphToChar(2000);
            Assert.AreEqual(2000 * 33, middle);
            Assert.AreEqual(2000, rope.CharToParagraph(middle));
        }

        [Test]
        public void OneLongLineIsCutWithoutSplittingPairs()
        {
            var body = new StringBuilder();
            for (int i = 0; i < 200000; i++) body.Append(i % 3 == 0 ? "\U0001F600" : "ab");
            LetteraRope rope = LetteraRope.FromString(body.ToString());
            rope.CheckInvariants();
            Assert.AreEqual(body.ToString(), rope.ToString());
        }

        [Test]
        public void CrLfAcrossAnEditEdgeStaysOneSeparator()
        {
            LetteraRope rope = LetteraRope.FromString(new string('a', 5000) + "\r");
            Assert.AreEqual(2, rope.ParagraphCount);
            rope.Insert(rope.Length, "\n");
            Assert.AreEqual(2, rope.ParagraphCount);
            rope.CheckInvariants();
        }

        [Test]
        public void AnEditPatchMapsPositions()
        {
            LetteraRope rope = LetteraRope.FromString("hello world");
            LetteraRopePatch patch = default;
            rope.Changed += p => patch = p;
            rope.Replace(6, 5, "there!");
            Assert.AreEqual(6, patch.Start);
            Assert.AreEqual(5, patch.Removed);
            Assert.AreEqual(6, patch.Inserted);
            Assert.AreEqual(3, patch.Map(3));
            Assert.AreEqual(12, patch.Map(8));
            Assert.AreEqual(1, rope.Version);
        }

        [Test]
        public void HeightsSumAndFind()
        {
            var body = new StringBuilder();
            for (int i = 0; i < 2000; i++) body.Append("A paragraph of a few words here.\n");
            LetteraRope rope = LetteraRope.FromString(body.ToString());
            for (int b = 0; b < rope.BlockCount; b++) rope.SetBlockHeight(b, 10 + b);
            double total = 0;
            for (int b = 0; b < rope.BlockCount; b++)
            {
                Assert.AreEqual(total, rope.HeightAtBlock(b), 1e-6);
                Assert.AreEqual(b, rope.BlockAtHeight(total + 1, out double offset));
                Assert.AreEqual(1, offset, 1e-6);
                total += 10 + b;
            }
            Assert.AreEqual(total, rope.TotalHeight, 1e-6);
        }

        [Test]
        public void ASliceKeepsItsContentAfterEdits()
        {
            string text = new string('a', 20000) + "middle" + new string('b', 20000);
            LetteraRope rope = LetteraRope.FromString(text);
            LetteraRope slice = rope.Slice(100, 30000);
            string wanted = text.Substring(100, 30000);
            rope.Replace(200, 25000, "gone");
            Assert.AreEqual(wanted, slice.ToString());
        }

        // ------------------------------------------------------------- files

        static byte[] Utf8(string text) => new UTF8Encoding(false).GetBytes(text);

        [TestCase(LetteraFileBackend.Mapped)]
        [TestCase(LetteraFileBackend.Chunked)]
        [TestCase(LetteraFileBackend.InMemory)]
        public void EachBackendReadsTheSame(LetteraFileBackend backend)
        {
            var body = new StringBuilder();
            for (int i = 0; i < 3000; i++) body.Append("Line مر \U0001F600 ").Append(i).Append("\r\n");
            string text = body.ToString();
            using LetteraRope rope = LetteraFileSource.Load(TempFile(Utf8(text)), new LetteraFileOptions { Backend = backend });
            rope.CheckInvariants();
            Assert.AreEqual(text.Length, rope.Length);
            Assert.AreEqual(text, rope.ToString());
            Assert.AreEqual(3001, rope.ParagraphCount);
            AssertMatches(rope, text, new Random(1));
        }

        [Test]
        public void AByteOrderMarkReadsTheSameAndWideFilesToo()
        {
            const string Text = "abc مر \U0001F600\n";
            var withBom = new List<byte> { 0xEF, 0xBB, 0xBF };
            withBom.AddRange(Utf8(Text));
            Assert.AreEqual(Text, Open(withBom.ToArray()).ToString());
            Assert.AreEqual(Text, Open(new UnicodeEncoding(false, true).GetPreamble().Concat2(new UnicodeEncoding(false, true).GetBytes(Text))).ToString());
            Assert.AreEqual(Text, Open(new UnicodeEncoding(true, true).GetPreamble().Concat2(new UnicodeEncoding(true, true).GetBytes(Text))).ToString());
            Assert.AreEqual(Text, Open(new UTF32Encoding(false, true).GetPreamble().Concat2(new UTF32Encoding(false, true).GetBytes(Text))).ToString());
        }

        [TestCase(new byte[] { 0x61, 0x80, 0x62 }, "a�b")]
        [TestCase(new byte[] { 0xC0, 0x80 }, "��")]
        [TestCase(new byte[] { 0x61, 0xE2, 0x82 }, "a�")]
        [TestCase(new byte[] { 0xF0, 0x9F, 0x98, 0x41 }, "�A")]
        [TestCase(new byte[] { 0xED, 0xA0, 0x80 }, "���")]
        public void InvalidUtf8ReadsAsReplacement(byte[] bytes, string wanted)
        {
            LetteraRope rope = LetteraRope.FromUtf8(bytes);
            Assert.AreEqual(wanted, rope.ToString());
            Assert.AreEqual(wanted.Length, rope.Length);
        }

        [Test]
        public void InvalidBytesInALongFileLeavePositionsRight()
        {
            var bytes = new List<byte>();
            var model = new StringBuilder();
            var random = new Random(5);
            for (int i = 0; i < 40000; i++)
            {
                int kind = random.Next(50);
                if (kind == 0)
                {
                    bytes.Add(0xFF);
                    model.Append('�');
                }
                else if (kind == 1)
                {
                    bytes.AddRange(Utf8("\U0001F600"));
                    model.Append("\U0001F600");
                }
                else
                {
                    bytes.Add((byte)'q');
                    model.Append('q');
                }
            }
            using LetteraRope rope = LetteraFileSource.Load(TempFile(bytes.ToArray()));
            rope.CheckInvariants();
            Assert.AreEqual(model.ToString(), rope.ToString());
        }

        [Test]
        public void EditingAMappedRopeLeavesTheFileAndDisposeReleasesIt()
        {
            byte[] bytes = Utf8(new string('a', 20000));
            string path = TempFile(bytes);
            LetteraRope rope = LetteraFileSource.Load(path, new LetteraFileOptions { Backend = LetteraFileBackend.Mapped });
            rope.Insert(100, "edit");
            Assert.AreEqual(new string('a', 100) + "edit" + new string('a', 19900), rope.ToString());
            CollectionAssert.AreEqual(bytes, File.ReadAllBytes(path));
            rope.Dispose();
            File.Delete(path);
            Assert.IsFalse(File.Exists(path));
        }

        [Test]
        public void AnEmptyFileIsOneEmptyParagraph()
        {
            using LetteraRope rope = LetteraFileSource.Load(TempFile(new byte[0]));
            Assert.AreEqual(0, rope.Length);
            Assert.AreEqual(1, rope.ParagraphCount);
        }

        [Test]
        public void TriggersMarkOnlyBlocksThatCanHoldMarkup()
        {
            var body = new StringBuilder();
            for (int i = 0; i < 2000; i++) body.Append("Plain words only here.\n");
            body.Append("<b>tag</b>\n");
            for (int i = 0; i < 2000; i++) body.Append("Plain words only here.\n");
            LetteraRope rope = LetteraRope.FromString(body.ToString(), new LetteraMarkupTriggers('<'));
            int marked = 0;
            for (int b = 0; b < rope.BlockCount; b++)
                if (rope.BlockHasMarkup(b))
                    marked++;
            Assert.AreEqual(1, marked);
        }

        [Test]
        public void QueriesCostAboutTheSameOnASmallAndAHugeRopeAndAllocateNothing()
        {
            var small = LetteraRope.FromString(new string('x', 100 * 1024));
            var big = new StringBuilder();
            for (int i = 0; i < 512; i++) big.Append(new string('x', 200000)).Append('\n');
            var huge = LetteraRope.FromString(big.ToString());
            double Time(LetteraRope rope)
            {
                var clock = System.Diagnostics.Stopwatch.StartNew();
                int sum = 0;
                for (int i = 0; i < 2000; i++) sum += rope.BlockAtChar(i * 37 % rope.Length) + rope.BlockAtHeight(i, out _);
                clock.Stop();
                return sum >= 0 ? clock.Elapsed.TotalMilliseconds : -1;
            }
            Time(small);
            Time(huge);
            double a = Time(small), b = Time(huge);
            UnityEngine.Debug.Log($"LETTERA_BUDGET rope_query_100kb={a:0.000}ms rope_query_100mb={b:0.000}ms per 2000");
            Assert.Less(b, a * 8 + 1, "the query grew with the document");

            using var recorder = Unity.Profiling.ProfilerRecorder.StartNew(Unity.Profiling.ProfilerCategory.Memory, "GC Allocated In Frame");
            long control = recorder.CurrentValue;
            s_Control = new byte[64];
            Assert.GreaterOrEqual(recorder.CurrentValue - control, 32, "the allocation counter does not work here");
            long quietest = long.MaxValue;
            for (int window = 0; window < 5; window++)
            {
                long before = recorder.CurrentValue;
                for (int i = 0; i < 1000; i++)
                {
                    int at = i * 997 % huge.Length;
                    huge.CharToCodepoint(at);
                    huge.CharToParagraph(at);
                    huge.BlockAtHeight(i, out _);
                }
                quietest = Math.Min(quietest, recorder.CurrentValue - before);
            }
            Assert.Less(quietest, 200);
        }

        static byte[] s_Control;
    }

    static class ByteArrayExtensions
    {
        public static byte[] Concat2(this byte[] a, byte[] b)
        {
            var all = new byte[a.Length + b.Length];
            a.CopyTo(all, 0);
            b.CopyTo(all, a.Length);
            return all;
        }
    }
}

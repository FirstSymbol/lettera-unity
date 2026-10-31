using System;
using System.Collections.Generic;
using System.Text;
using NUnit.Framework;
using UnityEngine;

namespace FirstSymbol.Lettera.Editor.Tests
{
    /// <summary>The clipboard around a platform's host, against a fake one, and the parts of Windows' formats that are plain data.</summary>
    public class PlatformClipboardTests : EditingFixture
    {
        sealed class FakeHost : ILetteraClipboardHost
        {
            public readonly List<LetteraClipItem> Held = new List<LetteraClipItem>();
            public bool Busy, Throws;
            public int Reads;
            public LetteraClipPresence Presence = LetteraClipPresence.Text;

            public bool TryWrite(IReadOnlyList<LetteraClipItem> items)
            {
                if (Throws) throw new InvalidOperationException("broken");
                if (Busy) return false;
                Held.Clear();
                Held.AddRange(items);
                return true;
            }

            public bool TryRead(List<LetteraClipItem> items)
            {
                Reads++;
                if (Throws) throw new InvalidOperationException("broken");
                if (Busy) return false;
                items.AddRange(Held);
                return true;
            }

            public LetteraClipPresence Probe() => Presence;
        }

        sealed class AsyncClipboard : ILetteraClipboardBackend, ILetteraClipboardAsyncOnly, ILetteraClipboardProbe
        {
            public List<LetteraClipItem> Items = new List<LetteraClipItem>();
            public int AsyncReads, SyncReads;
            public void Write(IReadOnlyList<LetteraClipItem> items) { }
            public void Read(List<LetteraClipItem> items) => SyncReads++;
            public void ReadAsync(Action<List<LetteraClipItem>> done)
            {
                AsyncReads++;
                done(Items);
            }
            public LetteraClipPresence Probe() => LetteraClipPresence.Unknown;
        }

        static List<LetteraClipItem> Plain(string text) => new List<LetteraClipItem> { new LetteraClipItem(LetteraClipFormat.PlainText, text) };

        [Test]
        public void AHostGetsEveryFormatInOneWrite()
        {
            var host = new FakeHost();
            LetteraClipboard.Backend = new LetteraPlatformClipboard(host);
            LetteraEditable field = Field("<b>bold</b> rest");
            field.Selectable.SelectAll();
            Assert.IsTrue(field.Selectable.Copy());
            var formats = new List<string>();
            foreach (LetteraClipItem item in host.Held) formats.Add(item.Format);
            CollectionAssert.Contains(formats, LetteraClipFormat.PlainText);
            CollectionAssert.Contains(formats, LetteraClipFormat.Html);
            CollectionAssert.Contains(formats, LetteraClipFormat.Lettera);
        }

        [Test]
        public void ABusyHostFallsBackToPlainTextAndTellsWhy()
        {
            var host = new FakeHost { Busy = true };
            string held = GUIUtility.systemCopyBuffer;
            try
            {
                LetteraClipboard.Backend = new LetteraPlatformClipboard(host);
                LetteraClipboard.Write(Plain("through the fallback"));
                Assert.AreEqual("through the fallback", GUIUtility.systemCopyBuffer);
                var items = new List<LetteraClipItem>();
                LetteraClipboard.Backend.Read(items);
                Assert.AreEqual("through the fallback", items[0].Text);
            }
            finally
            {
                GUIUtility.systemCopyBuffer = held;
            }
        }

        [Test]
        public void AHostThatThrowsDoesNotTakeTheFieldWithIt()
        {
            var host = new FakeHost { Throws = true };
            string held = GUIUtility.systemCopyBuffer;
            try
            {
                LetteraClipboard.Backend = new LetteraPlatformClipboard(host);
                LetteraEditable field = Field("text");
                field.Selectable.SelectAll();
                Assert.DoesNotThrow(() => field.Selectable.Copy());
                Assert.DoesNotThrow(() => field.Paste());
            }
            finally
            {
                GUIUtility.systemCopyBuffer = held;
            }
        }

        [Test]
        public void TheLastCopyKeepsItsFormatsWhenAHostHeldOnlyItsText()
        {
            var host = new FakeHost();
            var clipboard = new LetteraPlatformClipboard(host);
            clipboard.Write(new List<LetteraClipItem> { new LetteraClipItem(LetteraClipFormat.PlainText, "a\nb"), new LetteraClipItem(LetteraClipFormat.Lettera, "{}") });
            host.Held.Clear();
            host.Held.AddRange(Plain("a\r\nb"));
            var items = new List<LetteraClipItem>();
            clipboard.Read(items);
            Assert.AreEqual(2, items.Count);
            host.Held.Clear();
            host.Held.AddRange(Plain("someone else's"));
            items.Clear();
            clipboard.Read(items);
            Assert.AreEqual(1, items.Count);
            Assert.AreEqual("someone else's", items[0].Text);
        }

        [Test]
        public void AskingWhatIsThereReadsNothing()
        {
            var host = new FakeHost { Presence = LetteraClipPresence.Rich };
            LetteraClipboard.Backend = new LetteraPlatformClipboard(host);
            Assert.IsTrue(LetteraClipboard.HasText);
            Assert.IsTrue(LetteraClipboard.HasRichText);
            host.Presence = LetteraClipPresence.Image;
            Assert.IsFalse(LetteraClipboard.HasText);
            host.Presence = LetteraClipPresence.Empty;
            Assert.IsFalse(LetteraClipboard.HasText);
            Assert.AreEqual(0, host.Reads);
        }

        [Test]
        public void NotKnowingStillOffersPaste()
        {
            LetteraClipboard.Backend = new AsyncClipboard();
            Assert.IsTrue(LetteraClipboard.HasText);
            Assert.IsFalse(LetteraClipboard.HasRichText);
        }

        [Test]
        public void AClipboardThatOnlyReadsLaterPastesThroughTheCommand()
        {
            var clipboard = new AsyncClipboard { Items = Plain("later") };
            LetteraClipboard.Backend = clipboard;
            LetteraEditable field = Field("");
            UnityEngine.TestTools.LogAssert.ignoreFailingMessages = true;
            Assert.IsFalse(field.Paste());
            UnityEngine.TestTools.LogAssert.ignoreFailingMessages = false;
            Assert.AreEqual(0, clipboard.SyncReads);
            new PasteCommand().Execute(new LetteraCommandContext(field.Selectable));
            Assert.AreEqual(1, clipboard.AsyncReads);
            Assert.AreEqual("later", field.Source);
        }

        [Test]
        public void TheCarriedFormatCountsOnlyWhenItSaysWhatTheHtmlShows()
        {
            string own = LetteraTextCodec.Write("hello", new List<LetteraPastedSpan> { new LetteraPastedSpan { Start = 0, End = 5, EffectKey = typeof(BoldEffect).FullName } });
            var clip = new LetteraParsedClip();
            Assert.IsTrue(LetteraHtmlCodec.TryReadCarried(LetteraHtmlCodec.CarryOwnFormat("<p>hello</p>", own), clip));
            Assert.AreEqual("hello", clip.Text);
            Assert.AreEqual(1, clip.Spans.Count);

            // Text the user cannot see must not be what gets pasted.
            Assert.IsFalse(LetteraHtmlCodec.TryReadCarried(LetteraHtmlCodec.CarryOwnFormat("<p>something else</p>", own), new LetteraParsedClip()));
        }

        [Test]
        public void ABrokenCarriedFormatIsJustHtml()
        {
            Assert.IsFalse(LetteraHtmlCodec.TryReadCarried("<span data-lettera=\"@@@not base64\">x</span>", new LetteraParsedClip()));
            Assert.IsFalse(LetteraHtmlCodec.TryReadCarried("<span data-lettera=\"" + Convert.ToBase64String(Encoding.UTF8.GetBytes("{\"version\":9}")) + "\">x</span>", new LetteraParsedClip()));
            Assert.IsFalse(LetteraHtmlCodec.TryReadCarried("<span data-lettera=\"unclosed", new LetteraParsedClip()));
            LetteraEditable field = Field("");
            field.PasteItems(new[] { new LetteraClipItem(LetteraClipFormat.Html, "<span data-lettera=\"@@@\"><b>seen</b></span>") });
            StringAssert.Contains("seen", field.PlainText);
        }

        [Test]
        public void CfHtmlOffsetsAreBytesAndPointAtTheFragment()
        {
            const string fragment = "<b>Привет 🙂 café</b>";
            byte[] data = LetteraCfHtml.Encode(fragment);
            string header = Encoding.ASCII.GetString(data, 0, 120);
            StringAssert.StartsWith("Version:0.9", header);
            Assert.AreEqual(fragment, LetteraCfHtml.Decode(data));
            int start = Value(header, "StartFragment:"), end = Value(header, "EndFragment:"), html = Value(header, "StartHTML:"), stop = Value(header, "EndHTML:");
            Assert.AreEqual(fragment, Encoding.UTF8.GetString(data, start, end - start));
            StringAssert.StartsWith("<html>", Encoding.UTF8.GetString(data, html, 6));
            Assert.AreEqual(data.Length, stop);
        }

        static int Value(string header, string name)
        {
            int at = header.IndexOf(name, StringComparison.Ordinal) + name.Length;
            return int.Parse(header.Substring(at, 10));
        }

        [Test]
        public void CfHtmlFromOthersReadsByItsMarkersWhenTheHeaderLies()
        {
            string page = "Version:0.9\r\nStartHTML:0000000999\r\nEndHTML:0000009999\r\nStartFragment:0000099999\r\nEndFragment:0000999999\r\n<html><body><!--StartFragment--><i>x</i><!--EndFragment--></body></html>";
            string read = LetteraCfHtml.Decode(Encoding.UTF8.GetBytes(page));
            LetteraParsedClip clip = LetteraHtmlCodec.Read(read);
            Assert.AreEqual("x", clip.Text);
            Assert.AreEqual(1, clip.Spans.Count);
        }

        [Test]
        public void HostileCfHtmlIsNothingAndNeverThrows()
        {
            Assert.IsNull(LetteraCfHtml.Decode(null));
            Assert.IsNull(LetteraCfHtml.Decode(new byte[0]));
            Assert.IsNull(LetteraCfHtml.Decode(Encoding.ASCII.GetBytes("Version:0.9\r\nStartFragment:")));
            Assert.DoesNotThrow(() => LetteraCfHtml.Decode(Encoding.ASCII.GetBytes("StartFragment:99999999999999\r\nEndFragment:-5\r\n")));
            Assert.DoesNotThrow(() => LetteraCfHtml.Decode(Encoding.ASCII.GetBytes("StartFragment:0000000010\r\nEndFragment:0000000005\r\n")));
            var random = new System.Random(7);
            for (int i = 0; i < 500; i++)
            {
                var bytes = new byte[random.Next(0, 300)];
                random.NextBytes(bytes);
                Assert.DoesNotThrow(() => LetteraCfHtml.Decode(bytes));
            }
        }

        [Test]
        public void ABitmapWithAHugeDeclaredSizeIsRefusedBeforeAnyAllocation()
        {
            var dib = new byte[64];
            BitConverter.GetBytes(40).CopyTo(dib, 0);
            BitConverter.GetBytes(100000).CopyTo(dib, 4);
            BitConverter.GetBytes(100000).CopyTo(dib, 8);
            dib[14] = 32;
            Assert.IsFalse(LetteraDib.TryRead(dib, int.MaxValue, out _, out _, out _));
            BitConverter.GetBytes(int.MaxValue).CopyTo(dib, 4);
            BitConverter.GetBytes(int.MinValue + 1).CopyTo(dib, 8);
            Assert.IsFalse(LetteraDib.TryRead(dib, int.MaxValue, out _, out _, out _));
            BitConverter.GetBytes(4).CopyTo(dib, 4);
            BitConverter.GetBytes(4).CopyTo(dib, 8);
            Assert.IsFalse(LetteraDib.TryRead(dib, 16 * 1024 * 1024, out _, out _, out _), "fewer bytes than its rows");
            Assert.IsFalse(LetteraDib.TryRead(new byte[10], 1024, out _, out _, out _));
        }

        [Test]
        public void ABitmapRoundTripsWithItsColorsAndOrder()
        {
            byte[] rgba = { 255, 0, 0, 255, 0, 255, 0, 128, 0, 0, 255, 255, 10, 20, 30, 40 };
            byte[] dib = LetteraDib.WriteV5(2, 2, rgba);
            Assert.IsTrue(LetteraDib.TryRead(dib, 1024, out int w, out int h, out byte[] read));
            Assert.AreEqual(2, w);
            Assert.AreEqual(2, h);
            CollectionAssert.AreEqual(rgba, read);
        }

        [Test]
        public void AnOldBottomUpBitmapComesOutTheRightWayUp()
        {
            // Two rows of one pixel, 24 bits, each row padded to four bytes: the bottom row comes first in the file.
            var dib = new byte[40 + 8];
            BitConverter.GetBytes(40).CopyTo(dib, 0);
            BitConverter.GetBytes(1).CopyTo(dib, 4);
            BitConverter.GetBytes(2).CopyTo(dib, 8);
            dib[14] = 24;
            dib[40] = 1; dib[41] = 2; dib[42] = 3;
            dib[44] = 4; dib[45] = 5; dib[46] = 6;
            Assert.IsTrue(LetteraDib.TryRead(dib, 1024, out _, out _, out byte[] rgba));
            CollectionAssert.AreEqual(new byte[] { 6, 5, 4, 255, 3, 2, 1, 255 }, rgba);
        }
    }
}

using System;
using System.Collections.Generic;
using System.Text;
using NUnit.Framework;
using UnityEngine;

namespace FirstSymbol.Lettera.Editor.Tests
{
    /// <summary>The gates and the guard, and one protocol against a fake control: all of this is what a real control runs against.</summary>
    public class PlatformTests : EditingFixture
    {
        sealed class FakeHost : ILetteraNativeHost
        {
            public readonly Queue<LetteraHostEvent> Events = new Queue<LetteraHostEvent>();
            public readonly List<LetteraMirrorState> Pushes = new List<LetteraMirrorState>();
            public LetteraMirrorState State;
            public bool Opened, Closed, ThrowOnOpen;
            public int Places;

            public bool IsAvailable => true;

            public void Open(in LetteraMirrorState state, in LetteraKeyboardTraits traits, LetteraHostMode mode)
            {
                if (ThrowOnOpen) throw new InvalidOperationException("no control");
                Opened = true;
                State = state;
            }

            public void Push(in LetteraMirrorState state)
            {
                State = state;
                Pushes.Add(state);
            }

            public void Place(Rect caret, Rect field) => Places++;
            public void Reconfigure(in LetteraKeyboardTraits traits) { }
            public void Close() => Closed = true;

            public int Drain(LetteraHostEvent[] into)
            {
                int count = 0;
                while (Events.Count > 0 && count < into.Length) into[count++] = Events.Dequeue();
                return count;
            }

            // As a real control does: the edit shows in the control at once, before the field has seen it.
            public void Replace(int start, int end, string text)
            {
                Events.Enqueue(new LetteraHostEvent { Kind = LetteraHostEventKind.Replace, BaseRevision = State.Revision, Start = start, End = end, Text = text });
                string held = State.Text ?? "";
                start = Mathf.Clamp(start, 0, held.Length);
                end = Mathf.Clamp(end, start, held.Length);
                State.Text = held.Substring(0, start) + text + held.Substring(end);
                State.SelectionStart = State.SelectionEnd = start + text.Length;
            }
        }

        FakeHost m_Host;

        [SetUp]
        public void Prepare()
        {
            LetteraPlatform.ResetForTests();
            LetteraPlatformGuard.ResetForTests();
            m_Host = new FakeHost();
        }

        [TearDown]
        public void Clean()
        {
            LetteraInput.Unregister(MakeMirror);
            LetteraPlatform.ResetForTests();
            LetteraPlatformGuard.ResetForTests();
        }

        ILetteraInputSource MakeMirror() => new LetteraMirrorSource(m_Host);

        LetteraEditable Mirrored(string text = "")
        {
            LetteraInput.Register(MakeMirror, 100);
            return Field(text);
        }

        static void Pump(LetteraEditable field) => field.Selectable.PumpInputForTests();

        // ------------------------------------------------------------- gates

        [TestCase(LetteraNativeFeature.TextInput, LetteraPlatformKind.Android, false)]
        [TestCase(LetteraNativeFeature.TextInput, LetteraPlatformKind.IOS, false)]
        [TestCase(LetteraNativeFeature.TextInput, LetteraPlatformKind.Web, true)]
        [TestCase(LetteraNativeFeature.TextInput, LetteraPlatformKind.WindowsPlayer, true)]
        [TestCase(LetteraNativeFeature.TextInput, LetteraPlatformKind.WindowsEditor, false)]
        [TestCase(LetteraNativeFeature.Clipboard, LetteraPlatformKind.WindowsEditor, true)]
        [TestCase(LetteraNativeFeature.Clipboard, LetteraPlatformKind.MacPlayer, false)]
        [TestCase(LetteraNativeFeature.SystemMenu, LetteraPlatformKind.Android, false)]
        [TestCase(LetteraNativeFeature.PasteControl, LetteraPlatformKind.IOS, false)]
        [TestCase(LetteraNativeFeature.Replica, LetteraPlatformKind.Web, true)]
        [TestCase(LetteraNativeFeature.TextInput, LetteraPlatformKind.TvOS, false)]
        [TestCase(LetteraNativeFeature.Clipboard, LetteraPlatformKind.VisionOS, false)]
        [TestCase(LetteraNativeFeature.Clipboard, LetteraPlatformKind.Uwp, false)]
        [TestCase(LetteraNativeFeature.TextInput, LetteraPlatformKind.Uwp, false)]
        public void AutoReadsTheTable(LetteraNativeFeature feature, LetteraPlatformKind kind, bool wanted)
        {
            LetteraPlatform.KindOverride = kind;
            Assert.AreEqual(wanted, LetteraPlatform.IsActive(feature));
        }

        [Test]
        public void WhySaysWhy()
        {
            LetteraPlatform.KindOverride = LetteraPlatformKind.Android;
            StringAssert.Contains("Off by default", LetteraPlatform.Why(LetteraNativeFeature.TextInput));
            LetteraPlatform.KindOverride = LetteraPlatformKind.Web;
            StringAssert.Contains("On by default", LetteraPlatform.Why(LetteraNativeFeature.TextInput));
            LetteraPlatform.MarkFailed(LetteraNativeFeature.TextInput, "no box");
            StringAssert.Contains("no box", LetteraPlatform.Why(LetteraNativeFeature.TextInput));
            Assert.IsFalse(LetteraPlatform.IsActive(LetteraNativeFeature.TextInput));
        }

        // ------------------------------------------------------------- guard

        sealed class FakeFiles : LetteraPlatformGuard.IFiles
        {
            public readonly Dictionary<string, string> Files = new Dictionary<string, string>();
            public bool Unwritable;
            public string Read(string name) => Files.TryGetValue(name, out string text) ? text : null;
            public void Write(string name, string text)
            {
                if (!Unwritable) Files[name] = text;
            }
            public void Delete(string name) => Files.Remove(name);
        }

        static bool Launch(FakeFiles files, string version)
        {
            LetteraPlatformGuard.ResetForTests();
            LetteraPlatformGuard.Files = files;
            LetteraPlatformGuard.Version = () => version;
            return LetteraPlatformGuard.Tripped(LetteraNativeFeature.TextInput);
        }

        [Test]
        public void TwoUnfinishedUsesTurnAFeatureOffAndOneDoesNot()
        {
            LetteraPlatform.KindOverride = LetteraPlatformKind.Android;
            var files = new FakeFiles();
            Assert.IsFalse(Launch(files, "1"));
            LetteraPlatformGuard.Enter(LetteraNativeFeature.TextInput);
            Assert.IsFalse(Launch(files, "1"), "one is forgiven");
            LetteraPlatformGuard.Enter(LetteraNativeFeature.TextInput);
            Assert.IsTrue(Launch(files, "1"), "two in a row");
            Assert.IsFalse(Launch(files, "2"), "a new version gets a new chance");
        }

        [Test]
        public void ACleanEndForgivesAndAnUnwritableFolderChangesNothing()
        {
            LetteraPlatform.KindOverride = LetteraPlatformKind.IOS;
            var files = new FakeFiles();
            Launch(files, "1");
            LetteraPlatformGuard.Enter(LetteraNativeFeature.TextInput);
            LetteraPlatformGuard.Leave(LetteraNativeFeature.TextInput);
            LetteraPlatformGuard.Enter(LetteraNativeFeature.TextInput);
            Assert.IsFalse(Launch(files, "1"));

            var stuck = new FakeFiles { Unwritable = true };
            Launch(stuck, "1");
            LetteraPlatformGuard.Enter(LetteraNativeFeature.TextInput);
            LetteraPlatformGuard.Enter(LetteraNativeFeature.TextInput);
            Assert.IsFalse(Launch(stuck, "1"));
        }

        [Test]
        public void TheGuardDoesNotRunOnWindowsOrTheWeb()
        {
            LetteraPlatform.KindOverride = LetteraPlatformKind.Web;
            var files = new FakeFiles();
            Launch(files, "1");
            LetteraPlatformGuard.Enter(LetteraNativeFeature.TextInput);
            CollectionAssert.IsEmpty(files.Files);
        }

        // ------------------------------------------------------------- the protocol

        [Test]
        public void AReplaceIsUsedExactlyEvenAmongRepeatedLetters()
        {
            LetteraEditable field = Mirrored("aaaa");
            Assert.IsTrue(m_Host.Opened);
            m_Host.Replace(2, 3, "b");
            Pump(field);
            Assert.AreEqual("aaba", field.Source);
            Assert.AreEqual("aaba", m_Host.State.Text, "the control follows");
        }

        [Test]
        public void OffsetsAreUtf16AcrossSurrogatesAndJoiners()
        {
            string family = "\U0001F468‍\U0001F469";
            LetteraEditable field = Mirrored("x" + family + "y");
            m_Host.Replace(1 + family.Length, 2 + family.Length, "Z");
            Pump(field);
            Assert.AreEqual("x" + family + "Z", field.Source);
            field.Selectable.PlaceCaret(field.Length);
            Pump(field);
            Assert.AreEqual(1 + family.Length + 1, m_Host.State.SelectionEnd);
        }

        [Test]
        public void WholeTextGivesTheEditItMeans()
        {
            LetteraEditable field = Mirrored("hello");
            m_Host.Events.Enqueue(new LetteraHostEvent { Kind = LetteraHostEventKind.WholeText, BaseRevision = m_Host.State.Revision, Text = "helloo", Caret = 6 });
            Pump(field);
            Assert.AreEqual("helloo", field.Source);
        }

        [Test]
        public void ARefusedEditIsNotInTheValueAndTheControlIsPutRight()
        {
            LetteraEditable field = Mirrored("abc");
            field.AddRule(new MaxLengthRule { Limit = 3, CountIn = LetteraCountSpace.Plain });
            int pushes = m_Host.Pushes.Count;
            m_Host.Replace(3, 3, "d");
            Pump(field);
            Assert.AreEqual("abc", field.Source);
            Assert.Greater(m_Host.Pushes.Count, pushes);
            Assert.AreEqual("abc", m_Host.State.Text);
        }

        // Seen on an Android phone: forty fast taps left twenty letters, and the next word landed after the first of them.
        [TestCase(1)]
        [TestCase(3)]
        [TestCase(40)]
        public void FastTypingKeepsEveryLetterAndTheCaret(int perFrame)
        {
            LetteraEditable field = Mirrored("hi");
            field.Selectable.PlaceCaret(2);
            Pump(field);
            int pushes = m_Host.Pushes.Count;
            for (int i = 0; i < 40; i++)
            {
                m_Host.Replace(m_Host.State.SelectionStart, m_Host.State.SelectionEnd, "a");
                if ((i + 1) % perFrame == 0) Pump(field);
            }
            Pump(field);
            Assert.AreEqual("hi" + new string('a', 40), field.Source);
            Assert.AreEqual(pushes, m_Host.Pushes.Count, "what the control already shows is not said back");
            m_Host.Replace(m_Host.State.SelectionStart, m_Host.State.SelectionEnd, " next");
            Pump(field);
            Assert.AreEqual("hi" + new string('a', 40) + " next", field.Source);
            Assert.AreEqual(field.Source.Length, field.Selectable.Selection.Head);
        }

        [Test]
        public void ReadOnlyRefusesWithNoLoop()
        {
            LetteraEditable field = Mirrored("abc");
            field.ReadOnly = true;
            m_Host.Replace(0, 0, "z");
            Pump(field);
            Pump(field);
            Pump(field);
            Assert.AreEqual("abc", field.Source);
            Assert.LessOrEqual(m_Host.Pushes.Count, 2, "a push loop");
        }

        [Test]
        public void AnEventOneRevisionOldLandsWhereItMeantAndAVeryOldOneIsDropped()
        {
            LetteraEditable field = Mirrored("abcdef");
            int old = m_Host.State.Revision;
            field.SetSource("XXabcdef");
            Pump(field);
            m_Host.Events.Enqueue(new LetteraHostEvent { Kind = LetteraHostEventKind.Replace, BaseRevision = old, Start = 3, End = 3, Text = "q" });
            Pump(field);
            Assert.AreEqual("XXabcqdef", field.Source, "mapped through what was pushed since");

            int ancient = m_Host.State.Revision;
            for (int i = 0; i < 40; i++)
            {
                Type(field, "w");
                Pump(field);
            }
            string before = field.Source;
            int pushes = m_Host.Pushes.Count;
            m_Host.Events.Enqueue(new LetteraHostEvent { Kind = LetteraHostEventKind.Replace, BaseRevision = ancient, Start = 0, End = 0, Text = "lost" });
            Pump(field);
            Assert.AreEqual(before, field.Source, "a lost character, never corrupted text");
            Assert.Greater(m_Host.Pushes.Count, pushes, "the state goes back");
        }

        [Test]
        public void NoPushDuringACompositionAndCommitIsOneUndo()
        {
            LetteraEditable field = Mirrored("ab");
            field.Selectable.PlaceCaret(2);
            Pump(field);
            int pushes = m_Host.Pushes.Count;
            m_Host.Events.Enqueue(new LetteraHostEvent { Kind = LetteraHostEventKind.Compose, BaseRevision = m_Host.State.Revision, Text = "ni", Caret = 2 });
            Pump(field);
            Assert.AreEqual("ab", field.Source, "out of the value until it commits");
            Assert.AreEqual(pushes, m_Host.Pushes.Count);
            m_Host.Events.Enqueue(new LetteraHostEvent { Kind = LetteraHostEventKind.Compose, BaseRevision = m_Host.State.Revision, Text = "你", Caret = 1 });
            m_Host.Events.Enqueue(new LetteraHostEvent { Kind = LetteraHostEventKind.ComposeEnd, BaseRevision = m_Host.State.Revision, Commit = true });
            Pump(field);
            Assert.AreEqual("ab你", field.Source);
            Assert.AreEqual("ab你", m_Host.State.Text);
            Assert.IsTrue(field.Undo());
            Assert.AreEqual("ab", field.Source);
        }

        [Test]
        public void AProgrammaticChangeDuringACompositionEndsItWithNoDoubleInsert()
        {
            LetteraEditable field = Mirrored("ab");
            m_Host.Events.Enqueue(new LetteraHostEvent { Kind = LetteraHostEventKind.Compose, BaseRevision = m_Host.State.Revision, Text = "x", Caret = 1 });
            Pump(field);
            field.SetSource("new");
            m_Host.Events.Enqueue(new LetteraHostEvent { Kind = LetteraHostEventKind.ComposeEnd, BaseRevision = m_Host.State.Revision, Commit = false });
            Pump(field);
            Assert.AreEqual("new", field.Source);
            Assert.AreEqual("new", m_Host.State.Text);
        }

        [Test]
        public void AReopenedWordIsReplacedNotInsertedInFront()
        {
            LetteraEditable field = Mirrored("my helo there");
            m_Host.Events.Enqueue(new LetteraHostEvent { Kind = LetteraHostEventKind.Compose, BaseRevision = m_Host.State.Revision, Start = 3, End = 7, Text = "hello", Caret = 5 });
            Pump(field);
            m_Host.Events.Enqueue(new LetteraHostEvent { Kind = LetteraHostEventKind.ComposeEnd, BaseRevision = m_Host.State.Revision, Commit = true });
            Pump(field);
            Assert.AreEqual("my hello there", field.Source);
            Assert.IsTrue(field.Undo());
            Assert.AreEqual("my helo there", field.Source, "one undo step");
        }

        [Test]
        public void ALongFieldSendsOnlyAWindowAroundTheCaretAndKeepsItRight()
        {
            var body = new StringBuilder();
            for (int i = 0; i < 25000; i++) body.Append("word ");
            LetteraEditable field = Mirrored(body.ToString());
            field.Selectable.PlaceCaret(60000);
            Pump(field);
            Assert.LessOrEqual(m_Host.State.Text.Length, LetteraMirrorSource.WindowUnits + 4);
            Assert.Greater(m_Host.State.Offset, 0);
            int windowCaret = m_Host.State.SelectionStart;
            Assert.AreEqual(60000, m_Host.State.Offset + windowCaret);
            m_Host.Replace(windowCaret, windowCaret, "Q");
            Pump(field);
            Assert.AreEqual('Q', field.Source[60000]);
            Assert.AreEqual(body.Length + 1, field.Source.Length, "the value is intact");

            // Close to an edge, it moves.
            int offset = m_Host.State.Offset;
            field.Selectable.PlaceCaret(offset + 50);
            Pump(field);
            Assert.Less(m_Host.State.Offset, offset);
        }

        [Test]
        public void AShortFieldHasOffsetZero()
        {
            LetteraEditable field = Mirrored(new string('a', 4000));
            field.Selectable.PlaceCaret(3000);
            Pump(field);
            Assert.AreEqual(0, m_Host.State.Offset);
            Assert.AreEqual(4000, m_Host.State.Text.Length);
        }

        [Test]
        public void SelectAllOnAPartialMirrorMeansTheWholeField()
        {
            LetteraEditable field = Mirrored(new string('a', 8000));
            m_Host.Events.Enqueue(new LetteraHostEvent { Kind = LetteraHostEventKind.Verb, Text = "selectAll" });
            Pump(field);
            Assert.AreEqual(0, field.Selectable.Selection.Start);
            Assert.AreEqual(8000, field.Selectable.Selection.End);
        }

        [Test]
        public void ASelectionFromTheControlIsNotSaidBackToIt()
        {
            LetteraEditable field = Mirrored("hello");
            int pushes = m_Host.Pushes.Count;
            m_Host.Events.Enqueue(new LetteraHostEvent { Kind = LetteraHostEventKind.Select, BaseRevision = m_Host.State.Revision, Start = 1, End = 3 });
            Pump(field);
            Pump(field);
            Assert.AreEqual(1, field.Selectable.Selection.Start);
            Assert.AreEqual(3, field.Selectable.Selection.End);
            Assert.AreEqual(pushes, m_Host.Pushes.Count, "an echo");
        }

        [Test]
        public void WithNoChangeNothingIsSent()
        {
            LetteraEditable field = Mirrored("hello");
            Pump(field);
            int pushes = m_Host.Pushes.Count;
            for (int i = 0; i < 20; i++) Pump(field);
            Assert.AreEqual(pushes, m_Host.Pushes.Count);
        }

        // Seen on an iPhone: a host that says the same at every focus filled the device check's report with thirty equal lines.
        [Test]
        public void ASaidThingIsListedOnce()
        {
            string line = "an unlikely message " + System.Guid.NewGuid();
            int before = LetteraPlatform.Reports.Count;
            for (int i = 0; i < 5; i++) LetteraPlatform.Report(LetteraNativeFeature.TextInput, line);
            Assert.AreEqual(before + 1, LetteraPlatform.Reports.Count);
            LetteraPlatform.Report(LetteraNativeFeature.Replica, line);
            Assert.AreEqual(before + 2, LetteraPlatform.Reports.Count, "another feature saying it is another line");
        }

        [Test]
        public void AHostThatThrowsLeavesTheFieldWorking()
        {
            m_Host.ThrowOnOpen = true;
            string reported = null;
            LetteraPlatform.Reported += (_, message) => reported = message;
            LetteraEditable field = Mirrored("hello");
            Assert.AreEqual("no control", reported);
            Type(field, "x");
            StringAssert.Contains("x", field.Source);
            Assert.IsFalse(LetteraPlatform.IsActive(LetteraNativeFeature.TextInput));
        }

        [Test]
        public void ManyRandomEventsMatchAModel()
        {
            var random = new System.Random(6);
            LetteraEditable field = Mirrored("start");
            var model = new StringBuilder("start");
            for (int step = 0; step < 20000; step++)
            {
                switch (random.Next(5))
                {
                    case 0:
                    case 1:
                    {
                        int at = random.Next(model.Length + 1);
                        int end = System.Math.Min(model.Length, at + random.Next(3));
                        string text = new string((char)('a' + random.Next(26)), random.Next(3));
                        if (at == end && text.Length == 0) break;
                        m_Host.Replace(at, end, text);
                        model.Remove(at, end - at).Insert(at, text);
                        Pump(field);
                        break;
                    }
                    case 2:
                        field.Selectable.PlaceCaret(random.Next(field.Length + 1));
                        Pump(field);
                        break;
                    case 3:
                    {
                        int a = random.Next(model.Length + 1), b = random.Next(model.Length + 1);
                        m_Host.Events.Enqueue(new LetteraHostEvent { Kind = LetteraHostEventKind.Select, BaseRevision = m_Host.State.Revision, Start = System.Math.Min(a, b), End = System.Math.Max(a, b) });
                        Pump(field);
                        break;
                    }
                    default:
                        Pump(field);
                        break;
                }
                if (model.Length > 200)
                {
                    field.SetSource("start");
                    model.Clear().Append("start");
                    Pump(field);
                }
                if (step % 200 == 0) Assert.AreEqual(model.ToString(), field.Source, "at step " + step);
            }
            Assert.AreEqual(model.ToString(), field.Source);
        }

        // ------------------------------------------------------------- plugin import settings

        static UnityEditor.PluginImporter Plugin(string relative)
        {
            var importer = UnityEditor.AssetImporter.GetAtPath(LetteraLocations.AssetPath("Plugins/" + relative)) as UnityEditor.PluginImporter;
            Assert.IsNotNull(importer, relative);
            return importer;
        }

        [Test]
        public void TvOSAndVisionOSTakeTheirOwnLibraryAndTheCoreTextLookup()
        {
            Assert.IsTrue(Plugin("tvOS/lettera_native.xcframework").GetCompatibleWithPlatform(UnityEditor.BuildTarget.tvOS));
            Assert.IsFalse(Plugin("tvOS/lettera_native.xcframework").GetCompatibleWithPlatform(UnityEditor.BuildTarget.iOS));
            Assert.IsTrue(Plugin("visionOS/lettera_native.xcframework").GetCompatibleWithPlatform(UnityEditor.BuildTarget.VisionOS));
            Assert.IsFalse(Plugin("visionOS/lettera_native.xcframework").GetCompatibleWithPlatform(UnityEditor.BuildTarget.tvOS));
            Assert.IsFalse(Plugin("iOS/lettera_native.xcframework").GetCompatibleWithPlatform(UnityEditor.BuildTarget.tvOS));
            var fonts = Plugin("iOS/LetteraFontsIOS.mm");
            foreach (var target in new[] { UnityEditor.BuildTarget.iOS, UnityEditor.BuildTarget.tvOS, UnityEditor.BuildTarget.VisionOS })
            {
                Assert.IsTrue(fonts.GetCompatibleWithPlatform(target), target.ToString());
                Assert.AreEqual("CoreText;", fonts.GetPlatformData(target, "FrameworkDependencies"), target.ToString());
            }
            Assert.IsFalse(Plugin("iOS/LetteraPlatformIOS.mm").GetCompatibleWithPlatform(UnityEditor.BuildTarget.tvOS), "UIKit text input stays iOS only");
        }

        [Test]
        public void UwpTakesTheWindowsLibraryOfItsCpu()
        {
            var intel = Plugin("Windows/x86_64/lettera_native.dll");
            var arm = Plugin("Windows/ARM64/lettera_native.dll");
            Assert.IsTrue(intel.GetCompatibleWithPlatform(UnityEditor.BuildTarget.WSAPlayer));
            Assert.AreEqual("X64", intel.GetPlatformData(UnityEditor.BuildTarget.WSAPlayer, "CPU"));
            Assert.IsTrue(arm.GetCompatibleWithPlatform(UnityEditor.BuildTarget.WSAPlayer));
            Assert.AreEqual("ARM64", arm.GetPlatformData(UnityEditor.BuildTarget.WSAPlayer, "CPU"));
            Assert.IsFalse(Plugin("Editor/Windows/x86_64/lettera_fonttools.dll").GetCompatibleWithPlatform(UnityEditor.BuildTarget.WSAPlayer), "the font tool stays in the editor");
        }
    }
}

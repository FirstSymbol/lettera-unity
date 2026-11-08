using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace FirstSymbol.Lettera.Editor.Tests
{
    /// <summary>Copy and paste in all formats, and the commands and menus around them.</summary>
    public class ClipboardTests : EditingFixture
    {
        readonly List<GameObject> m_Objects = new List<GameObject>();

        [TearDown]
        public void Clean()
        {
            foreach (GameObject go in m_Objects)
                if (go != null)
                    Object.DestroyImmediate(go);
            m_Objects.Clear();
            WebLookupCommand.Opener = null;
            LetteraEditCommand.TitleSource = null;
        }

        static string Html(LetteraParsedClip clip)
        {
            var builder = new StringBuilder(clip.Text.Replace('\n', '|').Replace((char)0x2028, '~'));
            foreach (LetteraPastedSpan span in clip.Spans) builder.Append($" [{span.Start},{span.End} {Short(span.EffectKey)}{(span.Raw != null ? " " + span.Raw : "")}]");
            return builder.ToString();
        }

        static string Short(string key) => key.Substring(key.LastIndexOf('.') + 1).Replace("Effect", "");

        static void Paste(LetteraEditable field, params (string format, string text)[] items)
        {
            var list = new List<LetteraClipItem>();
            foreach ((string format, string text) in items) list.Add(new LetteraClipItem(format, text));
            field.PasteItems(list);
        }

        // ------------------------------------------------------------- copy

        [Test]
        public void ACopyWritesEveryFormatOnce()
        {
            LetteraSelectable text = Reader("a<b>b</b>c");
            text.SelectAll();
            text.Copy();
            Assert.AreEqual(1, Clip.Writes);
            Assert.AreEqual("abc", Clip.Text(LetteraClipFormat.PlainText));
            StringAssert.Contains("<b>b</b>", Clip.Text(LetteraClipFormat.Html));
            Assert.AreEqual("a**b**c", Clip.Text(LetteraClipFormat.Markdown));
            StringAssert.Contains("BoldEffect", Clip.Text(LetteraClipFormat.Lettera));
        }

        [Test]
        public void APartialCopyClosesWhatItCuts()
        {
            LetteraSelectable text = Reader("<b>abc</b>");
            text.Select(1, 2);
            text.Copy();
            StringAssert.Contains("<b>b</b>", Clip.Text(LetteraClipFormat.Html));
            var clip = new LetteraParsedClip();
            Assert.IsTrue(LetteraTextCodec.TryRead(Clip.Text(LetteraClipFormat.Lettera), clip));
            Assert.AreEqual((0, 1), (clip.Spans[0].Start, clip.Spans[0].End));
        }

        [Test]
        public void AnEmptySelectionLeavesTheClipboardAlone()
        {
            LetteraSelectable text = Reader("abc");
            Assert.IsFalse(text.Copy());
            Assert.AreEqual(0, Clip.Writes);
        }

        [Test]
        public void TheSourceViewCopiesWhatItShowsAsPlainText()
        {
            LetteraEditable field = Field("<b>a</b>");
            field.MarkupView = LetteraMarkupView.Source;
            field.Selectable.SelectAll();
            field.Selectable.Copy();
            Assert.AreEqual("<b>a</b>", Clip.Text(LetteraClipFormat.PlainText));
            Assert.IsNull(Clip.Text(LetteraClipFormat.Html));
        }

        [Test]
        public void PlainTextKeepsCaseAndBreaksAndNoVirtualText()
        {
            LetteraSelectable text = Reader("<uppercase>abc</uppercase>");
            text.SelectAll();
            text.Copy();
            Assert.AreEqual("abc", Clip.Text(LetteraClipFormat.PlainText));
            LetteraSelectable ruby = Reader("<ruby=x>a</ruby><li>b</li>");
            ruby.SelectAll();
            ruby.Copy();
            Assert.AreEqual("a\nb", Clip.Text(LetteraClipFormat.PlainText), "a list item is its own paragraph, and neither a reading nor a marker is text");
        }

        // ------------------------------------------------------------- round trips

        [Test]
        public void OwnFormatRoundTripsExactly()
        {
            const string source = "<b>a</b><color=#F00>b</color>";
            LetteraEditable from = Field(source), to = Field("");
            from.Selectable.SelectAll();
            from.Selectable.Copy();
            to.Selectable.Activate();
            to.Paste();
            Assert.AreEqual(source, to.Source);
        }

        [Test]
        public void StylesArriveInTheDestinationsSyntax()
        {
            LetteraEditable from = Field("");
            LetteraWorldText own = from.GetComponent<LetteraWorldText>();
            own.UseGlobalStylePreset = false;
            own.Styles.Add(LetteraStyle.ForMatcher(new MarkdownMarkerMatcher("**"), new BoldEffect()));
            own.MarkStylesDirty();
            from.SetSource("x**y**");
            from.Selectable.SelectAll();
            from.Selectable.Copy();

            LetteraEditable to = Field("");
            to.Paste();
            Assert.AreEqual("x<b>y</b>", to.Source);

            LetteraEditable none = Field("");
            LetteraWorldText plain = none.GetComponent<LetteraWorldText>();
            plain.UseGlobalStylePreset = false;
            plain.MarkStylesDirty();
            none.Paste();
            Assert.AreEqual("xy", none.Source, "a paste never adds a style");
        }

        [Test]
        public void HtmlRoundTrips()
        {
            const string source = "<b>a</b><i>b</i><u>c</u><s>d</s><color=#FF0000>e</color><size=20>f</size><link=https://a.io>g</link><sup>h</sup>";
            LetteraEditable from = Field(source);
            from.Selectable.SelectAll();
            from.Selectable.Copy();
            string html = Clip.Text(LetteraClipFormat.Html);
            LetteraParsedClip clip = LetteraHtmlCodec.Read(html);
            Assert.AreEqual("abcdefgh", clip.Text);
            string result = Html(clip);
            foreach (string part in new[] { "[0,1 Bold]", "[1,2 Italic]", "[2,3 Underline]", "[3,4 Strikethrough]", "[4,5 Color #FF0000]", "[5,6 Size 20]", "[6,7 Link https://a.io]", "[7,8 SuperSub:Super]" })
                StringAssert.Contains(part, result);
        }

        [Test]
        public void MarkdownRoundTripsWhatItCanSay()
        {
            LetteraSelectable text = Reader("<b>a</b><i>b</i><s>c</s><a=https://a.io>d</a><color=#F00>e</color>*");
            text.SelectAll();
            text.Copy();
            string markdown = Clip.Text(LetteraClipFormat.Markdown);
            Assert.AreEqual("**a***b*~~c~~[d](https://a.io)e\\*", markdown);
            LetteraParsedClip clip = LetteraMarkdownCodec.Read(markdown);
            Assert.AreEqual("abcde*", clip.Text);
            string result = Html(clip);
            StringAssert.Contains("[0,1 Bold]", result);
            StringAssert.Contains("[1,2 Italic]", result);
            StringAssert.Contains("[2,3 Strikethrough]", result);
            StringAssert.Contains("[3,4 Link https://a.io]", result);
            Assert.AreEqual(4, clip.Spans.Count);
        }

        // ------------------------------------------------------------- reading html

        [Test]
        public void WordsHtmlComesOutClean()
        {
            const string word = "<html xmlns:o=\"urn\"><head><style>p {color:red}</style></head><body><!--[if gte mso 9]><xml>x</xml><![endif]-->\n" +
                                "<p class=MsoNormal style='mso-margin-top:0'><span style='mso-bidi-font-size:11pt'>One</span><o:p></o:p></p>\n\n\n" +
                                "<p class=MsoNormal><b>Two</b>\n  words<o:p></o:p></p></body></html>";
            LetteraParsedClip clip = LetteraHtmlCodec.Read(word);
            Assert.AreEqual("One\nTwo words", clip.Text);
            Assert.AreEqual(1, clip.Spans.Count);
            Assert.AreEqual((4, 7), (clip.Spans[0].Start, clip.Spans[0].End));
        }

        [Test]
        public void DocsHtmlBoldsOnlyWhatIsBold()
        {
            const string docs = "<b style=\"font-weight:normal\" id=\"docs-internal-guid\"><span style=\"font-weight:400\">plain </span><span style=\"font-weight:700\">bold</span></b>";
            LetteraParsedClip clip = LetteraHtmlCodec.Read(docs);
            Assert.AreEqual("plain bold", clip.Text);
            Assert.AreEqual(1, clip.Spans.Count);
            Assert.AreEqual((6, 10), (clip.Spans[0].Start, clip.Spans[0].End));
        }

        [TestCase("&amp;&#x41;&#66;&nbsp;&lt;", "&AB <")]
        [TestCase("a&nope;b", "a&nope;b")]
        [TestCase("a &amp b", "a &amp b")]
        [TestCase("<b><i>x</b>y</i>", "xy")]
        [TestCase("a<br>b", "a~b")]
        [TestCase("<p>a</p><p>b</p>", "a|b")]
        [TestCase("<div>a<div>b</div></div>c", "a|b|c")]
        [TestCase("<pre>a  b\n c</pre>", "a  b| c")]
        [TestCase("  a   b  ", "a b")]
        [TestCase("<script>alert(1)</script>x", "x")]
        [TestCase("a <b", "a")]
        [TestCase("a < b", "a < b")]
        public void HtmlText(string html, string expected) => Assert.AreEqual(expected, LetteraHtmlCodec.Read(html).Text.Replace('\n', '|').Replace((char)0x2028, '~').TrimEnd('|'));

        [Test]
        public void HtmlUnitsAndColors()
        {
            LetteraParsedClip clip = LetteraHtmlCodec.Read("<span style=\"color:rgb(255,0,0);font-size:12pt\">a</span><span style=\"color:red;font-size:1.5rem\">b</span>");
            string result = Html(clip);
            StringAssert.Contains("Color #FF0000", result);
            StringAssert.Contains("Size 16", result);
            StringAssert.Contains("Size 24", result);
        }

        [Test]
        public void BadLinksLoseTheLinkAndKeepTheText()
        {
            LetteraParsedClip clip = LetteraHtmlCodec.Read("<a href=\"javascript:x\">y</a>");
            Assert.AreEqual("y", clip.Text);
            Assert.AreEqual(0, clip.Spans.Count);
            Assert.AreEqual(1, LetteraHtmlCodec.Read("<a href='mailto:a@b.c'>y</a>").Spans.Count);
        }

        [Test]
        public void FragmentMarkersCount()
        {
            const string cf = "Version:0.9\n<html><body>before<!--StartFragment--><b>in</b><!--EndFragment-->after</body></html>";
            Assert.AreEqual("in", LetteraHtmlCodec.Read(cf).Text);
        }

        [Test]
        public void HostileInputStaysLinear()
        {
            var deep = new StringBuilder();
            for (int i = 0; i < 100000; i++) deep.Append("<div>");
            deep.Append('x');
            var clock = Stopwatch.StartNew();
            Assert.AreEqual("x", LetteraHtmlCodec.Read(deep.ToString()).Text.Trim());
            Assert.Less(clock.ElapsedMilliseconds, 5000);

            clock.Restart();
            LetteraHtmlCodec.Read(new string('<', 4 * 1024 * 1024));
            Assert.Less(clock.ElapsedMilliseconds, 5000);

            clock.Restart();
            var flood = new StringBuilder();
            for (int i = 0; i < 400000; i++) flood.Append("&amp");
            LetteraHtmlCodec.Read(flood.ToString());
            Assert.Less(clock.ElapsedMilliseconds, 5000);

            clock.Restart();
            LetteraMarkdownCodec.Read(new string('[', 1000000) + new string('*', 100000));
            Assert.Less(clock.ElapsedMilliseconds, 5000);
        }

        // ------------------------------------------------------------- paste choices

        [Test]
        public void TheRichestFormatWins()
        {
            LetteraEditable field = Field("");
            Paste(field, (LetteraClipFormat.PlainText, "p"), (LetteraClipFormat.Markdown, "m"), (LetteraClipFormat.Html, "<b>h</b>"));
            Assert.AreEqual("<b>h</b>", field.Source);
            field.SetSource("");
            Paste(field, (LetteraClipFormat.PlainText, "p"), (LetteraClipFormat.Markdown, "**m**"));
            Assert.AreEqual("<b>m</b>", field.Source);
            field.SetSource("");
            Paste(field, (LetteraClipFormat.PlainText, "p"), (LetteraClipFormat.Lettera, "{\"version\":99,\"text\":\"later\"}"));
            Assert.AreEqual("p", field.Source, "a later version falls to the next format");
        }

        [Test]
        public void KeepingStylesCanBeOff()
        {
            LetteraEditable field = Field("");
            field.PasteKeepsStyles = false;
            Paste(field, (LetteraClipFormat.PlainText, "p"), (LetteraClipFormat.Html, "<b>h</b>"));
            Assert.AreEqual("p", field.Source);
        }

        [Test]
        public void PastePlainTakesTheLocalStyle()
        {
            LetteraEditable field = Field("<b>a</b>");
            Clip.Write(new[] { new LetteraClipItem(LetteraClipFormat.PlainText, "x"), new LetteraClipItem(LetteraClipFormat.Html, "<i>x</i>") });
            field.PastePlain();
            Assert.AreEqual("<b>ax</b>", field.Source);
        }

        [Test]
        public void HtmlOverTheByteLimitFallsToTheNextFormat()
        {
            int old = LetteraSettings.Instance.Editing.PasteHtmlMaxBytes;
            LetteraSettings.Instance.Editing.PasteHtmlMaxBytes = 4;
            try
            {
                LetteraEditable field = Field("");
                Paste(field, (LetteraClipFormat.PlainText, "p"), (LetteraClipFormat.Html, "<b>h</b>"));
                Assert.AreEqual("p", field.Source);
            }
            finally
            {
                LetteraSettings.Instance.Editing.PasteHtmlMaxBytes = old;
            }
        }

        [Test]
        public void RulesApplyToPastedText()
        {
            LetteraEditable field = Field("");
            field.AddRule(new MaxLengthRule { Limit = 2, CountIn = LetteraCountSpace.Plain });
            Paste(field, (LetteraClipFormat.PlainText, "abcdef"));
            Assert.AreEqual("ab", field.Source);
        }

        [Serializable]
        class TakeImages : AttachmentRule
        {
            public byte[] Seen;
            public override void OfferAttachment(LetteraAttachment attachment)
            {
                if (attachment.TryGetData(LetteraClipFormat.Png, out Seen)) attachment.Handled = true;
            }
        }

        [Test]
        public void AttachmentsComeFirst()
        {
            var rule = new TakeImages();
            LetteraEditable field = Field("");
            field.AddRule(rule);
            field.PasteItems(new[] { new LetteraClipItem(LetteraClipFormat.Png, new byte[] { 1 }), new LetteraClipItem(LetteraClipFormat.PlainText, "p") });
            Assert.IsNotNull(rule.Seen);
            Assert.AreEqual("", field.Source);
            rule.Enabled = false;
            field.PasteItems(new[] { new LetteraClipItem(LetteraClipFormat.Png, new byte[] { 1 }), new LetteraClipItem(LetteraClipFormat.PlainText, "p") });
            Assert.AreEqual("p", field.Source);
        }

        [Test]
        public void TheShadowCarriesStylesUntilTheSystemTextChanges()
        {
            string saved = GUIUtility.systemCopyBuffer;
            LetteraClipboard.Backend = new LetteraSystemClipboard();
            LetteraEditable from = Field("<b>x</b>"), to = Field("");
            from.Selectable.SelectAll();
            from.Selectable.Copy();
            Assert.AreEqual("x", GUIUtility.systemCopyBuffer);
            to.Selectable.Activate();
            to.Paste();
            Assert.AreEqual("<b>x</b>", to.Source);
            to.SetSource("");
            GUIUtility.systemCopyBuffer = "other";
            to.Paste();
            Assert.AreEqual("other", to.Source);
            GUIUtility.systemCopyBuffer = saved;
        }

        // ------------------------------------------------------------- commands

        static List<string> Offered(LetteraSelectable text)
        {
            var offered = new List<string>();
            text.RequestMenu(Vector2.zero);
            foreach (LetteraMenuEntry entry in text.MenuEntries)
                if (entry.IsEnabled)
                    offered.Add(entry.Command.GetType().Name.Replace("Command", ""));
            text.HideMenu();
            return offered;
        }

        [Test]
        public void WhatTheMenuOffersDependsOnTheField()
        {
            LetteraEditable field = Field("abc");
            field.Selectable.Select(0, 1);
            Clip.Write(new[] { new LetteraClipItem(LetteraClipFormat.PlainText, "x") });
            CollectionAssert.AreEqual(new[] { "Cut", "Copy", "Paste", "SelectAll" }, Offered(field.Selectable));

            LetteraSelectable reader = Reader("abc");
            reader.Select(0, 1);
            CollectionAssert.AreEqual(new[] { "Copy", "SelectAll" }, Offered(reader));

            LetteraEditable password = Field("abc");
            password.AddRule(new PasswordRule());
            password.Selectable.Select(0, 1);
            CollectionAssert.AreEqual(new[] { "Paste", "SelectAll" }, Offered(password.Selectable));

            Clip.Write(new[] { new LetteraClipItem(LetteraClipFormat.PlainText, "x"), new LetteraClipItem(LetteraClipFormat.Html, "<b>x</b>") });
            CollectionAssert.Contains(Offered(field.Selectable), "PastePlain");
        }

        [Test]
        public void AnEmptyListMeansNoMenu()
        {
            LetteraSelectable reader = Reader("abc");
            reader.Commands.Clear();
            reader.SelectAll();
            bool asked = false;
            reader.RequestMenu(Vector2.zero);
            Assert.IsFalse(asked);
            Assert.AreEqual(0, reader.MenuEntries.Count);
        }

        [Serializable]
        class Custom : LetteraEditCommand
        {
            public static int Ran;
            public override bool CanRun(in LetteraCommandContext c) => true;
            public override void Execute(in LetteraCommandContext c) => Ran++;
        }

        sealed class FakeMenu : LetteraMenuPresenter
        {
            public IReadOnlyList<LetteraMenuEntry> Entries;
            public Action<int> Invoke;
            public LetteraMenuPlacement Placement;
            bool m_Visible;
            public override bool IsVisible => m_Visible;

            public override void Show(in LetteraMenuPlacement placement, IReadOnlyList<LetteraMenuEntry> entries, Action<int> invoke)
            {
                Placement = placement;
                Entries = entries;
                Invoke = invoke;
                m_Visible = true;
            }

            public override void Hide() => m_Visible = false;
        }

        FakeMenu Menu()
        {
            var go = new GameObject("Fake Menu");
            m_Objects.Add(go);
            return go.AddComponent<FakeMenu>();
        }

        [Test]
        public void AProjectCommandReachesFieldsThatListTheProjects()
        {
            List<LetteraEditCommand> commands = LetteraSettings.Instance.Editing.Commands;
            var custom = new Custom();
            commands.Add(custom);
            try
            {
                LetteraSelectable reader = Reader("abc");
                FakeMenu menu = Menu();
                reader.MenuPresenter = menu;
                reader.RequestMenu(Vector2.zero);
                bool found = false;
                foreach (LetteraMenuEntry entry in menu.Entries) found |= entry.Command == custom;
                Assert.IsTrue(found);
                reader.Commands.Clear();
                reader.Commands.Add(new CopyCommand());
                reader.SelectAll();
                reader.RequestMenu(Vector2.zero);
                Assert.AreEqual(1, menu.Entries.Count);
            }
            finally
            {
                commands.Remove(custom);
            }
        }

        // The lead's rule for Apple: a long press in a field places a caret, and its menu offers Select, which takes the word at the caret.
        [Test]
        public void SelectIsOfferedOnAppleTouchAtACaretAndTakesTheWord()
        {
            LetteraKeyTable.FamilyOverride = LetteraPlatformFamily.Apple;
            try
            {
                LetteraEditable field = Field("one two three");
                FakeMenu menu = Menu();
                field.Selectable.MenuPresenter = menu;
                field.Selectable.Commands.Clear();
                field.Selectable.Commands.Add(new SelectCommand());
                field.Selectable.Commands.Add(new SelectAllCommand());
                field.Selectable.Select(5, 5);
                field.Selectable.OpenMenu(new LetteraMenuPlacement { Trigger = LetteraMenuTrigger.Touch });
                Assert.AreEqual("Select", menu.Entries[0].Title);
                Assert.IsTrue(menu.Entries[0].IsEnabled && menu.Entries[1].IsEnabled, "Select and Select all");
                menu.Invoke(0);
                Assert.AreEqual((4, 7), (field.Selectable.Selection.Start, field.Selectable.Selection.End));

                field.Selectable.Select(5, 5);
                field.Selectable.OpenMenu(new LetteraMenuPlacement { Trigger = LetteraMenuTrigger.Pointer });
                Assert.IsFalse(menu.Entries[0].IsEnabled, "a pointer's menu has no Select");

                LetteraKeyTable.FamilyOverride = LetteraPlatformFamily.Pc;
                field.Selectable.Select(5, 5);
                field.Selectable.OpenMenu(new LetteraMenuPlacement { Trigger = LetteraMenuTrigger.Touch });
                Assert.IsFalse(menu.Entries[0].IsEnabled, "nor does Android's");
            }
            finally
            {
                LetteraKeyTable.FamilyOverride = LetteraPlatformFamily.Pc;
            }
        }

        [Test]
        public void SelectAllKeepsTheMenuAndOthersTakeItDown()
        {
            LetteraEditable field = Field("abc");
            FakeMenu menu = Menu();
            field.Selectable.MenuPresenter = menu;
            field.Selectable.Commands.Clear();
            field.Selectable.Commands.Add(new SelectAllCommand());
            field.Selectable.Commands.Add(new CopyCommand());
            field.Selectable.RequestMenu(Vector2.zero);
            menu.Invoke(0);
            Assert.AreEqual(3, field.Selectable.Selection.Length);
            Assert.IsTrue(menu.IsVisible);
            menu.Invoke(1);
            Assert.IsFalse(menu.IsVisible);
        }

        [Test]
        public void AnEditTakesTheMenuDown()
        {
            LetteraEditable field = Field("abc");
            FakeMenu menu = Menu();
            field.Selectable.MenuPresenter = menu;
            field.Selectable.RequestMenu(Vector2.zero);
            Assert.IsTrue(menu.IsVisible);
            Type(field, "x");
            Assert.IsFalse(menu.IsVisible);
        }

        [Test]
        public void TheKeyboardOpensItOnPcAndNotOnApple()
        {
            LetteraEditable field = Field("abc");
            FakeMenu menu = Menu();
            field.Selectable.MenuPresenter = menu;
            Press(field, LetteraKeyCode.F10, Shift);
            Assert.IsTrue(menu.IsVisible);
            Assert.AreEqual(LetteraMenuTrigger.Keyboard, menu.Placement.Trigger);
            menu.Hide();
            LetteraKeyTable.FamilyOverride = LetteraPlatformFamily.Apple;
            Press(field, LetteraKeyCode.F10, Shift);
            Assert.IsFalse(menu.IsVisible);
        }

        [Test]
        public void MenuRequestedCanTakeOver()
        {
            LetteraEditable field = Field("abc");
            FakeMenu menu = Menu();
            field.Selectable.MenuPresenter = menu;
            field.Selectable.MenuRequested += (in LetteraMenuPlacement placement, ref bool handled) => handled = true;
            field.Selectable.RequestMenu(Vector2.zero);
            Assert.IsFalse(menu.IsVisible);
        }

        [Test]
        public void HeldSelectionDropsItsReplacementIfTheTextMoved()
        {
            LetteraEditable field = Field("abc");
            field.Selectable.Select(0, 1);
            LetteraHeldSelection held = new LetteraCommandContext(field.Selectable).HoldSelection();
            Assert.IsTrue(held.Replace("x"));
            Assert.AreEqual("xbc", field.Source);
            field.Selectable.Select(0, 1);
            held = new LetteraCommandContext(field.Selectable).HoldSelection();
            Type(field, "y");
            Assert.IsFalse(held.Replace("z"));
            Assert.AreEqual("ybc", field.Source);
        }

        [Test]
        public void WebLookupEncodesAndStaysOffPasswords()
        {
            string opened = null;
            WebLookupCommand.Opener = url => opened = url;
            var command = new WebLookupCommand { Url = "https://x.io/?q={0}" };
            LetteraEditable field = Field("a b&c");
            field.Selectable.SelectAll();
            var context = new LetteraCommandContext(field.Selectable);
            Assert.IsTrue(command.CanRun(context));
            command.Execute(context);
            Assert.AreEqual("https://x.io/?q=a%20b%26c", opened);
            field.AddRule(new PasswordRule());
            field.Selectable.SelectAll();
            Assert.IsFalse(command.CanRun(new LetteraCommandContext(field.Selectable)));
        }

        [Test]
        public void TitlesCanComeFromElsewhere()
        {
            var command = new CopyCommand();
            Assert.AreEqual("Copy", command.Title);
            LetteraEditCommand.TitleSource = c => "Kopieren";
            Assert.AreEqual("Kopieren", command.Title);
            command.Title = "Local";
            LetteraEditCommand.TitleSource = null;
            Assert.AreEqual("Local", command.Title);
            Assert.AreEqual("Select all", new SelectAllCommand().Title);
        }

        [Test]
        public void EachCommandHasAKeyForATable()
        {
            CollectionAssert.AreEqual(new[] { "Cut", "Copy", "Paste", "PastePlain", "Select", "SelectAll" }, LetteraEditCommand.DefaultProjectList().ConvertAll(c => c.TitleKey));
            Assert.AreEqual("Bold", new ToggleEffectCommand().TitleKey);
            Assert.AreEqual("WebLookup", new WebLookupCommand().TitleKey);
            Assert.AreEqual("Mine", new CopyCommand { Title = "Mine" }.TitleKey, "a title written on it");
        }

        static bool HasEntry(LetteraEditable field, string title)
        {
            foreach (LetteraMenuEntry entry in field.Selectable.MenuEntries)
                if (entry.Title == title)
                    return true;
            return false;
        }

        [Test]
        public void AnOpenMenuTakesNewTitles()
        {
            LetteraEditable field = Field("hello");
            field.Selectable.Select(0, 5);
            field.Selectable.OpenMenu(new LetteraMenuPlacement { Point = Vector2.zero, Trigger = LetteraMenuTrigger.Pointer });
            Assert.IsTrue(field.Selectable.IsMenuShown);
            Assert.IsTrue(HasEntry(field, "Copy"));
            LetteraEditCommand.TitleSource = c => c is CopyCommand ? "Kopieren" : null;
            LetteraEditCommand.RefreshTitles();
            Assert.IsTrue(field.Selectable.IsMenuShown);
            Assert.IsTrue(HasEntry(field, "Kopieren"), "the new language without closing it");
            field.Selectable.HideMenu();
            LetteraEditCommand.TitleSource = null;
            LetteraEditCommand.RefreshTitles();
        }
    }
}

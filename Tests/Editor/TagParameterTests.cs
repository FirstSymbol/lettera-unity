using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;

namespace FirstSymbol.Lettera.Editor.Tests
{
    // The pure parts of the tag parameter panel: finding a tag and its slots, writing one slot back as the least markup, and the slot
    // descriptors the panel is drawn from, checked against what the effects really read.
    public class TagParameterTests
    {
        const string Price = "The <shadow=#000C,0.08,,0.2>price</shadow> is 12";

        static LetteraTagSpan First(string text, string name = null) => LetteraTagScanner.Scan(text).First(t => name == null || t.Name == name);

        static string Set(string text, int slot, string value, string name = null)
        {
            LetteraTagSpan tag = First(text, name);
            LetteraTextEdit edit = LetteraTagWriter.SetSlot(text, tag, slot, value);
            Assert.IsTrue(edit.Ok, "The edit failed for " + text);
            return edit.Text;
        }

        // ------------------------------------------------------------- scanner

        [Test]
        public void ScannerFindsTheOpenTagItsSlotsAndItsClose()
        {
            LetteraTagSpan tag = First(Price);
            Assert.AreEqual("shadow", tag.Name);
            Assert.AreEqual("<shadow=#000C,0.08,,0.2>", tag.OpenText(Price));
            Assert.AreEqual(4, tag.Slots.Count);
            CollectionAssert.AreEqual(new[] { "#000C", "0.08", null, "0.2" }, tag.Slots.Select(s => s.Value).ToArray());
            Assert.AreEqual("#000C", tag.Raw(Price, 0));
            Assert.AreEqual("", tag.Raw(Price, 2));
            Assert.AreEqual("</shadow>", Price.Substring(tag.CloseStart, tag.CloseEnd - tag.CloseStart));
        }

        [Test]
        public void ScannerAgreesWithTheRuntimeOnWhatEverySlotSays()
        {
            string[] values =
            {
                "a", "a,b", "a, b ,c", ",,", ",", "\"a,b\",c", "'q\"q',,x", " \"x\" , y", "rgb(9,8,7),z", "(1,2", "a/b", "#fff,   ,3", "\"\"", "x,\"y\"",
                "1.5em,\"two words\",(a,(b,c)),", "ü,ж,😀", "0.1,-0.2,,,0.3",
            };
            foreach (string value in values)
            {
                string text = "pre <t=" + value + ">mid</t> post";
                int at = text.IndexOf('<');
                Assert.IsTrue(TagSyntax.MatchOpen(text.AsSpan(), at, "t", out _, out string[] expected, out _, out _), "The runtime does not read: " + value);
                LetteraTagSpan tag = First(text, "t");
                CollectionAssert.AreEqual(expected, tag.Slots.Select(s => s.Value).ToArray(), "Slots differ for: " + value);
                Assert.IsTrue(tag.HasClose);
            }
        }

        [Test]
        public void ScannerKeepsTheLabelSelfClosingAndNesting()
        {
            const string text = "<b>a<i>b</i>c</b> <br/> <shadow #price=1>x</shadow> <sprite=coin/>";
            List<LetteraTagSpan> tags = LetteraTagScanner.Scan(text);
            CollectionAssert.AreEqual(new[] { "b", "i", "br", "shadow", "sprite" }, tags.Select(t => t.Name).ToArray());
            Assert.IsTrue(tags[2].SelfClosing && !tags[2].HasClose);
            Assert.AreEqual("price", tags[3].Label);
            Assert.AreEqual("coin", tags[4].Slots[0].Value);
            List<LetteraTagSpan> inside = LetteraTagScanner.Enclosing(tags, text.IndexOf('b', 5));
            CollectionAssert.AreEqual(new[] { "b", "i" }, inside.Select(t => t.Name).ToArray(), "The inner tag is last");
            Assert.AreEqual(0, LetteraTagScanner.Enclosing(tags, text.IndexOf(" <br/>", StringComparison.Ordinal)).Count(t => t.Name == "i"));
        }

        [Test]
        public void ScannerSkipsEscapedAndNoParseMarkup()
        {
            List<LetteraTagSpan> tags = LetteraTagScanner.Scan("\\<b>x <noparse><i>raw</i></noparse><u>y</u>");
            CollectionAssert.AreEqual(new[] { "noparse", "u" }, tags.Select(t => t.Name).ToArray());
        }

        [Test]
        public void CaretInsideAnOpenTagHoldsThatTagAndItsSlot()
        {
            List<LetteraTagSpan> tags = LetteraTagScanner.Scan(Price);
            int caret = Price.IndexOf("0.08", StringComparison.Ordinal) + 2;
            Assert.AreEqual("shadow", LetteraTagScanner.Enclosing(tags, caret).Last().Name);
            Assert.AreEqual(1, LetteraTagScanner.SlotAt(tags[0], caret));
            Assert.AreEqual(-1, LetteraTagScanner.SlotAt(tags[0], tags[0].Start + 3), "The name is no slot");
            Assert.AreEqual(2, LetteraTagScanner.SlotAt(tags[0], Price.IndexOf(",,", StringComparison.Ordinal) + 1));
            Assert.AreEqual(0, LetteraTagScanner.Enclosing(tags, 2).Count, "Plain text holds no tag");
            Assert.AreEqual("shadow", LetteraTagScanner.Enclosing(tags, Price.IndexOf("price", StringComparison.Ordinal) + 1).Single().Name);
        }

        [Test]
        public void AnUnclosedTagAndAnUnclosedQuoteAreNamedAndMended()
        {
            string open = "x <shadow=#000";
            Assert.AreEqual(LetteraTagProblem.NotClosed, LetteraTagScanner.Diagnose(open, open.Length, out int start));
            string mended = LetteraTagScanner.Mend(open, start, LetteraTagProblem.NotClosed, out _, out _);
            Assert.AreEqual("x <shadow=#000>", mended);
            Assert.AreEqual(LetteraTagProblem.None, LetteraTagScanner.Diagnose(mended, mended.Length - 3, out _));

            string quote = "<shadow=\"abc,1>x";
            Assert.AreEqual(LetteraTagProblem.QuoteNotClosed, LetteraTagScanner.Diagnose(quote, 10, out start));
            string closed = LetteraTagScanner.Mend(quote, start, LetteraTagProblem.QuoteNotClosed, out _, out _);
            Assert.IsTrue(TagSyntax.MatchOpen(closed.AsSpan(), 0, "shadow", out _, out string[] slots, out _, out _), closed);
            Assert.AreEqual("abc", slots[0]);

            Assert.AreEqual(LetteraTagProblem.None, LetteraTagScanner.Diagnose(Price, 8, out _));
            Assert.AreEqual(LetteraTagProblem.None, LetteraTagScanner.Diagnose("a < b and c", 6, out _), "A lone less-than is plain text");
        }

        // ------------------------------------------------------------- writer

        [Test]
        public void SettingOneSlotKeepsEveryOtherTokenAsWritten()
        {
            Assert.AreEqual("The <shadow=#000C,0.08,-0.1,0.2>price</shadow> is 12", Set(Price, 2, "-0.1"));
            Assert.AreEqual("<shadow= '#000C' , 1,,0.2 >x</shadow>", Set("<shadow= '#000C' , 0.08,,0.2 >x</shadow>", 1, "1"));
            Assert.AreEqual("<shadow=#000C,0.08,,0.2>", Set("<shadow=#000C,0.08,,0.2>", 1, "0.08"), "Writing what is there changes nothing");
        }

        [Test]
        public void ResettingTheLastSetSlotTrimsTrailingEmptiesAndTheEqualsSign()
        {
            Assert.AreEqual("<shadow>x</shadow>", Set("<shadow=,,,0.2>x</shadow>", 3, null));
            Assert.AreEqual("<shadow=#000>x</shadow>", Set("<shadow=#000,0.1,>x</shadow>", 1, null));
            Assert.AreEqual("<shadow>", Set("<shadow=#000>", 0, null));
            Assert.AreEqual("<x/>", Set("<x=1/>", 0, null));
        }

        [Test]
        public void SettingALaterSlotPadsEarlierOnesWithEmptySlots()
        {
            Assert.AreEqual("<shadow=,,,0.3>x</shadow>", Set("<shadow>x</shadow>", 3, "0.3"));
            Assert.AreEqual("<shadow=#fff,,0.3>x", Set("<shadow=#fff>x", 2, "0.3"));
            Assert.AreEqual("<shadow #price=1>", Set("<shadow #price>", 0, "1"));
            Assert.AreEqual("<x=1/>", Set("<x/>", 0, "1"));
            Assert.AreEqual("<shadow #price=,2>x", Set("<shadow #price>x", 1, "2"));
        }

        [Test]
        public void ValuesAreQuotedOnlyWhenTheTagReaderNeedsIt()
        {
            Assert.AreEqual("<t=\"a,b\">", Set("<t>", 0, "a,b"));
            Assert.AreEqual("<t=rgb(9,8,7)>", Set("<t>", 0, "rgb(9,8,7)"), "A comma inside parentheses stays");
            Assert.AreEqual("<t=\" a\">", Set("<t>", 0, " a"));
            Assert.AreEqual("<t=say \"hi\">", Set("<t>", 0, "say \"hi\""), "A quote inside needs no quoting");
            Assert.AreEqual("<t='\"hi\" there'>", Set("<t>", 0, "\"hi\" there"));
            Assert.AreEqual("<t=\"a>b\">", Set("<t>", 0, "a>b"));
            Assert.AreEqual("<t=\"a/\">", Set("<t>", 0, "a/"));
            Assert.IsFalse(LetteraTagWriter.SetSlot("<t>", First("<t>"), 0, "\"it's\" both").Ok, "No quote carries both kinds");
            Assert.AreEqual("<t=ж😀>", Set("<t>", 0, "ж😀"));
        }

        [Test]
        public void WrittenMarkupReadsBackWithTheRuntimeParser()
        {
            var random = new System.Random(12345);
            string[] pool = { "", "a", "#F80", "0.25", "-0.1em", "a,b", "rgb(1,2,3)", " pad ", "x y", "q\"q", "ж", "(", "a/", "{x}", "1;2" };
            for (int round = 0; round < 300; round++)
            {
                int count = random.Next(0, 6);
                string[] expected = new string[count];
                string text = "[<t>]";
                for (int step = 0; step < 8; step++)
                {
                    int slot = random.Next(0, 6);
                    string value = pool[random.Next(pool.Length)];
                    LetteraTagSpan tag = First(text, "t");
                    LetteraTextEdit edit = LetteraTagWriter.SetSlot(text, tag, slot, value);
                    if (!edit.Ok) continue;
                    var model = new List<string>(expected);
                    while (model.Count <= slot) model.Add(null);
                    model[slot] = value.Length == 0 ? null : value;
                    while (model.Count > 0 && model[model.Count - 1] == null) model.RemoveAt(model.Count - 1);
                    text = edit.Text;
                    expected = model.ToArray();

                    LetteraTagSpan after = First(text, "t");
                    string[] read = after.Slots.Select(s => s.Value).ToArray();
                    CollectionAssert.AreEqual(expected, read, "After setting " + slot + " to '" + value + "': " + text);
                    Assert.IsTrue(text.StartsWith("[<t") && text.EndsWith("]"), "Nothing outside the tag changes: " + text);
                }
            }
        }

        [Test]
        public void EditingOneSlotLeavesTheRestOfTheTextByteForByte()
        {
            const string text = "head <b>x</b> <shadow= \"#000C\" ,0.08 ,, 0.2 >price</shadow> tail <i=2>y</i>";
            LetteraTagSpan tag = First(text, "shadow");
            LetteraTextEdit edit = LetteraTagWriter.SetSlot(text, tag, 1, "0.5");
            Assert.AreEqual(text.Substring(0, edit.At), edit.Text.Substring(0, edit.At));
            Assert.AreEqual(text.Substring(edit.At + edit.Removed), edit.Text.Substring(edit.At + edit.Inserted));
            Assert.AreEqual("head <b>x</b> <shadow= \"#000C\" ,0.5 ,, 0.2 >price</shadow> tail <i=2>y</i>", edit.Text);
        }

        [Test]
        public void ACaretFollowsTheTextAnEditMoves()
        {
            LetteraTextEdit edit = LetteraTagWriter.SetSlot(Price, First(Price), 3, "0.123456");
            int tail = Price.IndexOf("price", StringComparison.Ordinal);
            Assert.AreEqual(edit.Text.IndexOf("price", StringComparison.Ordinal), edit.Map(tail));
            Assert.AreEqual(3, edit.Map(3));
        }

        [Test]
        public void NumbersAndColoursAreWrittenInvariantAndShort()
        {
            Assert.AreEqual("0.2", LetteraTagWriter.FormatNumber(0.2f));
            Assert.AreEqual("-0.08", LetteraTagWriter.FormatNumber(-0.08000001));
            Assert.AreEqual("0", LetteraTagWriter.FormatNumber(-0.00000001));
            Assert.AreEqual("12", LetteraTagWriter.FormatNumber(12.0));
            Assert.AreEqual("0.333", LetteraTagWriter.FormatNumber(1.0 / 3, 3));
            Assert.AreEqual("#336699", LetteraTagWriter.FormatColor(new Color32(0x33, 0x66, 0x99, 255)));
            Assert.AreEqual("#33669980", LetteraTagWriter.FormatColor(new Color32(0x33, 0x66, 0x99, 128)));
            var culture = System.Globalization.CultureInfo.CurrentCulture;
            try
            {
                System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("ru-RU");
                Assert.AreEqual("0.5", LetteraTagWriter.FormatNumber(0.5));
            }
            finally { System.Globalization.CultureInfo.CurrentCulture = culture; }
        }

        // ------------------------------------------------------------- slot values

        [Test]
        public void ATokenIsClassifiedByWhatTheEffectReads()
        {
            LetteraParamInfo[] shadow = LetteraBuiltInSlots.Shadow;
            Assert.AreEqual(LetteraSlotState.Inherit, LetteraSlotValue.Classify(shadow[0], null));
            Assert.AreEqual(LetteraSlotState.Set, LetteraSlotValue.Classify(shadow[0], "#000C"));
            Assert.AreEqual(LetteraSlotState.Set, LetteraSlotValue.Classify(shadow[0], "sunset"), "A paint may name a swatch");
            Assert.AreEqual(LetteraSlotState.Invalid, LetteraSlotValue.Classify(LetteraBuiltInSlots.Color[0], "reddish"));
            Assert.AreEqual(LetteraSlotState.Set, LetteraSlotValue.Classify(shadow[1], "-0.1em"));
            Assert.AreEqual(LetteraSlotState.Invalid, LetteraSlotValue.Classify(shadow[1], "abc"));
            Assert.AreEqual(LetteraSlotState.OutOfRange, LetteraSlotValue.Classify(shadow[3], "1.5"));
            Assert.AreEqual(LetteraSlotState.Set, LetteraSlotValue.Classify(LetteraBuiltInSlots.Alpha[0], "50%"));
            Assert.AreEqual(LetteraSlotState.Set, LetteraSlotValue.Classify(LetteraBuiltInSlots.Alpha[0], "#80"), "Alpha reads a hex pair");
            Assert.AreEqual(LetteraSlotState.Invalid, LetteraSlotValue.Classify(LetteraBuiltInSlots.Size[0], "24px"), "Size reads no px suffix");
            Assert.AreEqual(LetteraSlotState.Set, LetteraSlotValue.Classify(LetteraBuiltInSlots.Size[0], "+10"));
            Assert.AreEqual(LetteraSlotState.Set, LetteraSlotValue.Classify(LetteraBuiltInSlots.Line[4], "on"), "Bool tokens are the parser's");
            Assert.AreEqual(LetteraSlotState.Invalid, LetteraSlotValue.Classify(LetteraBuiltInSlots.Line[4], "yes"));
            Assert.AreEqual(LetteraSlotState.Set, LetteraSlotValue.Classify(LetteraBuiltInSlots.Stroke[2], "0.5"));
            Assert.AreEqual(LetteraSlotState.Set, LetteraSlotValue.Classify(LetteraBuiltInSlots.Stroke[2], "OUTSIDE"));
            Assert.AreEqual(LetteraSlotState.Set, LetteraSlotValue.Classify(LetteraBuiltInSlots.Bold[1], "f"));
            Assert.AreEqual(LetteraSlotState.Set, LetteraSlotValue.Classify(LetteraBuiltInSlots.Stroke[4], "s"), "A unique initial names an enum member");
        }

        [Test]
        public void AnEmptySlotShowsTheStyleDefaultThenTheEffectsOwnValue()
        {
            var shadow = new ShadowEffect();
            string y = LetteraSlotDefaults.Inherited(LetteraBuiltInSlots.Shadow[2], shadow, null, out string from);
            Assert.AreEqual("-0.05", y);
            Assert.AreEqual(LetteraSlotDefaults.FromEffect, from);
            y = LetteraSlotDefaults.Inherited(LetteraBuiltInSlots.Shadow[2], shadow, new[] { null, null, "-0.3" }, out from);
            Assert.AreEqual("-0.3", y);
            Assert.AreEqual(LetteraSlotDefaults.FromStyle, from);
            Assert.AreEqual("#00000080", LetteraSlotDefaults.Inherited(LetteraBuiltInSlots.Shadow[0], shadow, null, out _));
        }

        // ------------------------------------------------------------- schema against the effects

        static IEnumerable<LetteraEffect> Described() => new LetteraEffect[]
        {
            new BoldEffect(), new ItalicEffect(), new SizeEffect(), new ColorEffect(), new AlphaEffect(), new FontEffect(), new LanguageEffect(),
            new UnderlineEffect(), new StrikethroughEffect(), new OutlineEffect(), new ShadowEffect(), new GlowEffect(), new StrokeEffect(), new FillEffect(),
            new HighlightEffect(), new TrackingEffect(), new LineHeightEffect(), new AlignEffect(), new IndentEffect(), new SuperSubEffect(),
        };

        // Runs the effect with a sample in every described slot and records the positions it asks for.
        static HashSet<int> Reads(LetteraEffect effect, string[] tokens)
        {
            var read = new HashSet<int>();
            var attributes = new LetteraAttributes();
            attributes.Reset(8);
            attributes.BeginEffect(effect, 0);
            LetteraSlots.Probe = index => read.Add(index);
            try { effect.Apply(attributes, 0, 5, new LetteraSlots(tokens, null)); }
            finally { LetteraSlots.Probe = null; }
            return read;
        }

        static string[] Samples(IReadOnlyList<LetteraParamInfo> described, int variant = 0)
        {
            var tokens = new string[LetteraTagModel.SlotCount(described)];
            foreach (LetteraParamInfo info in described) tokens[info.Slot] = LetteraSlotValue.Sample(info, variant);
            return tokens;
        }

        [Test]
        public void EveryDescriptorIsAPositionTheEffectReadsAndNothingElseIsRead()
        {
            foreach (LetteraEffect effect in Described())
            {
                IReadOnlyList<LetteraParamInfo> described = effect.Parameters;
                string name = effect.GetType().Name;
                Assert.IsTrue(LetteraTagModel.HasSchema(described), name + " has no descriptors");
                var slots = described.Select(d => d.Slot).ToList();
                CollectionAssert.AreEqual(Enumerable.Range(0, slots.Count).ToList(), slots.OrderBy(s => s).ToList(), name + ": slots are dense and unique");

                HashSet<int> read = Reads(effect, Samples(described));
                foreach (LetteraParamInfo info in described)
                    Assert.IsTrue(read.Contains(info.Slot), $"{name} never reads slot {info.Slot} ({info.Display})");
                // A layer's paint settings follow its own slots.
                int own = described.Count;
                foreach (int index in read)
                    Assert.IsTrue(index < own || effect is LetteraLayerEffect, $"{name} reads slot {index}, which no descriptor names");
            }
        }

        [Test]
        public void EveryDescriptorKeepsItsWordsAndItsDefaultsTrue()
        {
            foreach (LetteraEffect effect in Described())
            {
                string name = effect.GetType().Name;
                foreach (LetteraParamInfo info in effect.Parameters)
                {
                    string at = name + "." + info.Display;
                    Assert.IsFalse(string.IsNullOrEmpty(info.Display), at);
                    Assert.IsFalse(string.IsNullOrEmpty(info.Hint), at + " has no hint");
                    Assert.IsTrue(LetteraSlotValue.Reads(info, LetteraSlotValue.Sample(info, 0)), at + " does not read its own sample");
                    Assert.IsTrue(LetteraSlotValue.Reads(info, LetteraSlotValue.Sample(info, 1)), at + " does not read its second sample");
                    if (info.Kind == LetteraParamKind.Enum || info.Kind == LetteraParamKind.Choice)
                        Assert.IsTrue(info.Choices != null && info.Choices.Length > 1, at + " has no choices");
                    if (info.Kind == LetteraParamKind.Length)
                        Assert.AreNotEqual(LetteraSlotUnits.None, info.Units, at + " says nothing of its units");
                    if (!string.IsNullOrEmpty(info.DefaultField))
                        Assert.IsTrue(LetteraSlotDefaults.TryRead(effect, info.DefaultField, info, out _), at + ": no field " + info.DefaultField);
                    else
                        Assert.IsFalse(string.IsNullOrEmpty(info.Default), at + " says nothing of what an empty slot gives");
                }
            }
        }

        [Test]
        public void EnumChoicesAreTheMembersOfTheEnumsTheEffectsReadAndNamesTheParserTakes()
        {
            Assert.AreEqual(Enum.GetNames(typeof(LetteraLineStyle)), LetteraBuiltInSlots.Line[1].Choices);
            Assert.AreEqual(Enum.GetNames(typeof(LetteraHorizontalAlignment)), LetteraBuiltInSlots.Align[0].Choices);
            Assert.AreEqual(Enum.GetNames(typeof(LetteraHighlightGeometry)), LetteraBuiltInSlots.Mark[1].Choices);
            Assert.AreEqual(Enum.GetNames(typeof(LetteraStrokeCorners)), LetteraBuiltInSlots.Stroke[4].Choices);
            // The parser takes a name as the panel writes it, and the effect gets the member the name means.
            var slots = new LetteraSlots(new[] { null, "Dashed" }, null);
            Assert.AreEqual(LetteraLineStyle.Dashed, slots.Enum(1, LetteraLineStyle.Solid));
        }

        [Test]
        public void SlotsReadWhatThePanelWritesForEachKind()
        {
            // A sample set in one slot at a time changes what the effect makes, so a descriptor naming the wrong position would show here.
            var shadow = new ShadowEffect();
            LetteraAttributes plain = Apply(shadow, new string[5]);
            for (int slot = 1; slot < 5; slot++)
            {
                var tokens = new string[5];
                tokens[slot] = slot == 3 ? "0.9" : "0.7em";
                LetteraAttributes changed = Apply(shadow, tokens);
                Assert.AreNotEqual(Fingerprint(plain), Fingerprint(changed), "Slot " + slot + " of shadow did nothing");
            }
        }

        static LetteraAttributes Apply(LetteraEffect effect, string[] tokens)
        {
            var attributes = new LetteraAttributes();
            attributes.Reset(8);
            attributes.BeginEffect(effect, 0);
            effect.Apply(attributes, 0, 5, new LetteraSlots(tokens, null));
            return attributes;
        }

        static string Fingerprint(LetteraAttributes attributes)
        {
            var text = new System.Text.StringBuilder();
            foreach (LayerRecord l in attributes.Layers) text.Append(l.OffsetX.Em).Append(',').Append(l.OffsetY.Em).Append(',').Append(l.Softness).Append(',').Append(l.Dilate.Em).Append('|');
            return text.ToString();
        }

        [Test]
        public void ParametersDoNotAllocateWhenAsked()
        {
            foreach (LetteraEffect effect in Described())
                Assert.AreSame(effect.Parameters, effect.Parameters, effect.GetType().Name);
        }

        [Test]
        public void ACustomEffectGetsRowsFromItsOwnDescriptors()
        {
            var effect = new AlertEffect();
            var tag = First("<alert=#F00,1.5>x</alert>");
            List<LetteraSlotRow> rows = LetteraTagModel.Rows(tag, effect.Parameters, effect, null);
            Assert.AreEqual(2, rows.Count);
            Assert.AreEqual("Grow", rows[1].Info.Display);
            Assert.AreEqual(LetteraSlotState.Set, rows[1].State);
            Assert.AreEqual("1.25", LetteraSlotDefaults.Inherited(rows[1].Info, effect, null, out _), "The live field is the inherited value");
        }

        [Serializable]
        sealed class AlertEffect : LetteraEffect
        {
            [SerializeField] Color32 m_Color = new Color32(255, 0, 0, 255);
            [SerializeField] float m_Grow = 1.25f;

            public override IReadOnlyList<LetteraParamInfo> Parameters => s_Slots;

            static readonly LetteraParamInfo[] s_Slots =
            {
                LetteraParamInfo.ForSlot(0, "Color", LetteraParamKind.Color, "Text colour", defaultField: "m_Color"),
                LetteraParamInfo.ForSlot(1, "Grow", LetteraParamKind.Float, "How much bigger", 1, 2, 0.05f, defaultField: "m_Grow"),
            };

            public override void Apply(LetteraAttributes attributes, int start, int end, in LetteraSlots slots)
            {
                attributes.SetColor(start, end, slots.Color(0, m_Color));
                attributes.MultiplySize(start, end, slots.Float(1, m_Grow));
            }
        }

        // ------------------------------------------------------------- README table against the descriptors

        static readonly (string tag, LetteraEffect effect)[] s_Documented =
        {
            ("b", new BoldEffect()), ("strong", new BoldEffect()), ("i", new ItalicEffect()), ("em", new ItalicEffect()), ("size", new SizeEffect()),
            ("color", new ColorEffect()), ("alpha", new AlphaEffect()), ("font", new FontEffect()), ("lang", new LanguageEffect()),
            ("u", new UnderlineEffect()), ("s", new StrikethroughEffect()), ("outline", new OutlineEffect()), ("shadow", new ShadowEffect()),
            ("fill", new FillEffect()), ("stroke", new StrokeEffect()), ("glow", new GlowEffect()), ("mark", new HighlightEffect()),
            ("tracking", new TrackingEffect()), ("line-height", new LineHeightEffect()), ("indent", new IndentEffect()), ("align", new AlignEffect()),
            ("sup", new SuperSubEffect()), ("sub", new SuperSubEffect()),
        };

        // The README names a tag's parameters in the order the effect reads them, using the descriptors' own names. A change to either fails here.
        [Test]
        public void TheReadmeTableNamesEachDescribedTagsSlotsInOrder()
        {
            string readme = File.ReadAllText(Path.Combine(PackageRoot(), "README.md"));
            string[] lines = readme.Split('\n');
            int checkedTags = 0;
            var problems = new List<string>();
            foreach (var documented in s_Documented)
            {
                string row = lines.FirstOrDefault(l => l.StartsWith("| `<") && Regex.IsMatch(FirstCell(l), "`<" + Regex.Escape(documented.tag) + ">`"));
                if (row == null)
                {
                    problems.Add("<" + documented.tag + "> has no README row of its own");
                    continue;
                }
                string cell = row.Split('|').Length > 3 ? row.Split('|')[3].ToLowerInvariant() : "";
                int from = 0;
                foreach (LetteraParamInfo info in documented.effect.Parameters.OrderBy(p => p.Slot))
                {
                    Match match = Regex.Match(cell.Substring(from), @"(?<![\p{L}\d])" + Regex.Escape(info.Display.ToLowerInvariant()) + @"(?![\p{L}\d])");
                    if (!match.Success)
                    {
                        problems.Add($"<{documented.tag}>: the README does not name slot {info.Slot + 1} '{info.Display}' after the earlier ones: {cell.Trim()}");
                        break;
                    }
                    from += match.Index + match.Length;
                    if (info.Group != null || info.Slot >= 7) break;
                }
                checkedTags++;
            }
            Assert.AreEqual(s_Documented.Length, checkedTags, string.Join("\n", problems));
            Assert.IsEmpty(problems, string.Join("\n", problems));
        }

        static string FirstCell(string line)
        {
            string[] cells = line.Split('|');
            return cells.Length > 1 ? cells[1] : "";
        }

        static string PackageRoot()
        {
            var info = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(LetteraText).Assembly);
            Assert.IsNotNull(info, "Lettera is not a package in this project");
            return info.resolvedPath;
        }
    }
}

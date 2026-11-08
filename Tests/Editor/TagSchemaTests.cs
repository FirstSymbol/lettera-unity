using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace FirstSymbol.Lettera.Editor.Tests
{
    // Every built-in effect says what it does and what its tag takes, or says on purpose that it takes nothing. The words come from one schema;
    // these tests run each effect to prove the schema is true, and read the docs' tables to prove they agree with it.
    public class TagSchemaTests
    {
        // Effects whose slots are read by a matcher or the engine, not by Apply: the schema is checked, the reads cannot be probed.
        static readonly HashSet<string> s_NoProbe = new HashSet<string>
        {
            "MaterialEffect", "LetteraMathEffect", "LetteraTableEffect", "DividerEffect", "CompositeEffect", "PresetEffect", "InlineEffect",
        };

        static List<Type> BuiltInTypes() => TypeCache.GetTypesDerivedFrom<LetteraEffect>()
            .Where(t => !t.IsAbstract && !t.IsNested && t.IsPublic && t.GetConstructor(Type.EmptyTypes) != null)
            .Where(t => t.Assembly.GetName().Name.StartsWith("FirstSymbol.Lettera", StringComparison.Ordinal) && !t.Assembly.GetName().Name.Contains("Tests"))
            .OrderBy(t => t.Name, StringComparer.Ordinal).ToList();

        // One instance of each type, and one of each kind for a type that is several tags (the 3D deformers).
        static IEnumerable<LetteraEffect> Instances()
        {
            foreach (Type type in BuiltInTypes())
            {
                var made = (LetteraEffect)Activator.CreateInstance(type);
                System.Reflection.FieldInfo kind = type.GetField("Kind", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                if (kind != null && kind.FieldType.IsEnum)
                {
                    foreach (object value in Enum.GetValues(kind.FieldType))
                    {
                        var each = (LetteraEffect)Activator.CreateInstance(type);
                        kind.SetValue(each, value);
                        yield return each;
                    }
                }
                else
                {
                    yield return made;
                }
            }
        }

        static string NameOf(LetteraEffect effect)
        {
            System.Reflection.FieldInfo kind = effect.GetType().GetField("Kind", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
            return effect.GetType().Name + (kind != null && kind.FieldType.IsEnum ? " (" + kind.GetValue(effect) + ")" : "");
        }

        static List<LetteraParamInfo> Slots(LetteraEffect effect) => effect.Parameters.Where(p => p.Slot >= 0).OrderBy(p => p.Slot).ToList();

        static string[] Samples(List<LetteraParamInfo> described, int variant = 0)
        {
            var tokens = new string[described.Count == 0 ? 0 : described.Max(d => d.Slot) + 1];
            foreach (LetteraParamInfo info in described) tokens[info.Slot] = LetteraSlotValue.Sample(info, variant);
            return tokens;
        }

        // Runs the effect with these tokens and records the positions it asks for.
        static HashSet<int> Reads(LetteraEffect effect, string[] tokens)
        {
            var read = new HashSet<int>();
            LetteraSlots.Probe = index => read.Add(index);
            try
            {
                var slots = new LetteraSlots(tokens, null);
                if (effect is LetteraMotionEffect motion)
                {
                    var values = default(LetteraMotionValues);
                    motion.Resolve(slots, ref values);
                }
                else
                {
                    var attributes = new LetteraAttributes();
                    attributes.Reset(8);
                    attributes.BeginEffect(effect, 0);
                    effect.Apply(attributes, 0, 5, slots);
                }
            }
            finally { LetteraSlots.Probe = null; }
            return read;
        }

        // ------------------------------------------------------------- nothing is left undescribed

        [Test]
        public void EveryBuiltInEffectDescribesItselfOrSaysItTakesNothing()
        {
            var problems = new List<string>();
            int seen = 0;
            foreach (LetteraEffect effect in Instances())
            {
                seen++;
                string name = NameOf(effect);
                bool schema = LetteraTagModel.HasSchema(effect.Parameters);
                bool none = effect.TakesNoParameters;
                bool delegates = effect.BuiltInFacts != null && effect.BuiltInFacts.Delegates;
                if (string.IsNullOrWhiteSpace(effect.Description)) problems.Add(name + " has no description");
                int ways = (schema ? 1 : 0) + (none ? 1 : 0) + (delegates ? 1 : 0);
                if (ways == 0) problems.Add(name + " is not described yet: it has no slots, and does not say that it takes none");
                if (ways > 1) problems.Add(name + " says more than one of: has slots, takes none, hands its value on");
                if (schema && effect.Examples.Count == 0) problems.Add(name + " has slots but no example markup");
                if (!schema && !delegates && effect.Examples.Any(e => e.Value.Length > 0)) problems.Add(name + " takes no values but its examples write one");
            }
            Assert.Greater(seen, 70, "The scan found too few effects: " + seen);
            Assert.IsEmpty(problems, string.Join("\n", problems));
        }

        [Test]
        public void EveryBuiltInEffectTypeIsInTheTableOrDescribesItselfInCode()
        {
            // A type in the table needs no code of its own; the others (3D, motions) override. Either way the type is not skipped.
            foreach (Type type in LetteraBuiltInTags.Described)
                Assert.IsTrue(typeof(LetteraEffect).IsAssignableFrom(type), type.Name);
            foreach (Type type in BuiltInTypes())
            {
                var effect = (LetteraEffect)Activator.CreateInstance(type);
                Assert.IsTrue(effect.Description != null, type.Name + " has no description");
            }
        }

        [Test]
        public void EveryDescribedSlotIsTrueToTheEffectAndWrittenForAPerson()
        {
            var problems = new List<string>();
            foreach (LetteraEffect effect in Instances())
            {
                string name = NameOf(effect);
                List<LetteraParamInfo> described = Slots(effect);
                if (described.Count == 0) continue;
                CollectionAssert.AreEqual(Enumerable.Range(0, described.Count).ToList(), described.Select(d => d.Slot).ToList(), name + ": slots are dense and unique");
                foreach (LetteraParamInfo info in described)
                {
                    string at = name + "." + info.Display;
                    if (string.IsNullOrEmpty(info.Hint)) problems.Add(at + " has no hint");
                    if (string.IsNullOrEmpty(LetteraSlotValue.Help(info))) problems.Add(at + " has no help text");
                    if (!LetteraSlotValue.Reads(info, LetteraSlotValue.Sample(info, 0)) || !LetteraSlotValue.Reads(info, LetteraSlotValue.Sample(info, 1))) problems.Add(at + " does not read its own sample");
                    if ((info.Kind == LetteraParamKind.Enum || info.Kind == LetteraParamKind.Choice) && (info.Choices == null || info.Choices.Length < 2)) problems.Add(at + " has no choices");
                    if (info.Kind == LetteraParamKind.Length && info.Units == LetteraSlotUnits.None) problems.Add(at + " says nothing of its units");
                    if (info.Kind == LetteraParamKind.String && string.IsNullOrEmpty(info.Example)) problems.Add(at + " is text with no example");
                    if (!string.IsNullOrEmpty(info.DefaultField))
                    {
                        if (!LetteraSlotDefaults.TryRead(effect, info.DefaultField, info, out _)) problems.Add(at + ": no field " + info.DefaultField);
                    }
                    else if (string.IsNullOrEmpty(info.Default))
                    {
                        problems.Add(at + " says nothing of what an empty slot gives");
                    }
                    string example = LetteraSlotValue.ExampleOf(info);
                    if (info.Kind != LetteraParamKind.String && (string.IsNullOrEmpty(example) || LetteraSlotValue.Classify(info, example) != LetteraSlotState.Set)) problems.Add(at + ": its example '" + example + "' is not read as set");
                }
            }
            Assert.IsEmpty(problems, string.Join("\n", problems));
        }

        [Test]
        public void EveryDescribedSlotIsReadByTheEffectAndNothingElseIs()
        {
            var problems = new List<string>();
            foreach (LetteraEffect effect in Instances())
            {
                string name = NameOf(effect);
                if (s_NoProbe.Contains(effect.GetType().Name)) continue;
                List<LetteraParamInfo> described = Slots(effect);
                HashSet<int> read;
                try
                {
                    read = Reads(effect, described.Count > 0 ? Samples(described) : new[] { "x", "1", "#fff", "a" });
                }
                catch (Exception e)
                {
                    problems.Add(name + " threw while it was run: " + e.GetType().Name + " " + e.Message);
                    continue;
                }
                foreach (LetteraParamInfo info in described)
                    if (!read.Contains(info.Slot))
                        problems.Add($"{name} never reads slot {info.Slot} ({info.Display})");
                foreach (int index in read)
                    if (!described.Any(d => d.Slot == index))
                        problems.Add($"{name} reads slot {index}, which no descriptor names");
            }
            Assert.IsEmpty(problems, string.Join("\n", problems));
        }

        [Test]
        public void AnInlineItemReadsEveryDescribedSlot()
        {
            var effect = new InlineEffect { Lookup = key => new LetteraInlineEntry { Key = key } };
            List<LetteraParamInfo> described = Slots(effect);
            var read = new HashSet<int>();
            LetteraSlots.Probe = index => read.Add(index);
            try { effect.Resolve(new LetteraSlots(Samples(described), null), 0); }
            finally { LetteraSlots.Probe = null; }
            foreach (LetteraParamInfo info in described) Assert.IsTrue(read.Contains(info.Slot), "Slot " + info.Slot + " (" + info.Display + ") is never read");
            CollectionAssert.IsSubsetOf(read, described.Select(d => d.Slot).ToList());
        }

        [Test]
        public void TheExamplesAreReadByTheirOwnSchema()
        {
            var problems = new List<string>();
            foreach (LetteraEffect effect in Instances())
            {
                string name = NameOf(effect);
                List<LetteraParamInfo> described = Slots(effect);
                foreach (LetteraTagExample example in effect.Examples)
                {
                    if (string.IsNullOrEmpty(example.Note)) problems.Add(name + ": example '" + example.Value + "' says nothing of what it gives");
                    if (example.Value.Length == 0) continue;
                    string[] tokens = TagSyntax.SplitSlots(example.Value);
                    for (int i = 0; i < tokens.Length; i++)
                    {
                        if (tokens[i] == null) continue;
                        LetteraParamInfo info = described.FirstOrDefault(d => d.Slot == i);
                        if (info == null)
                        {
                            problems.Add(name + ": example '" + example.Value + "' writes slot " + (i + 1) + ", which is not described");
                            continue;
                        }
                        LetteraSlotState state = LetteraSlotValue.Classify(info, tokens[i]);
                        if (state == LetteraSlotState.Invalid || state == LetteraSlotState.OutOfRange)
                            problems.Add(name + ": example '" + example.Value + "' slot " + (i + 1) + " (" + info.Display + ") is " + state);
                    }
                }
            }
            Assert.IsEmpty(problems, string.Join("\n", problems));
        }

        [Test]
        public void AnEffectThatSaysItTakesNothingReadsNothing()
        {
            var problems = new List<string>();
            foreach (LetteraEffect effect in Instances())
            {
                if (!effect.TakesNoParameters || s_NoProbe.Contains(effect.GetType().Name)) continue;
                HashSet<int> read = Reads(effect, new[] { "x", "1", "#fff", "a" });
                if (read.Count > 0) problems.Add(NameOf(effect) + " says it takes nothing but reads slot " + read.Min());
            }
            Assert.IsEmpty(problems, string.Join("\n", problems));
        }

        [Test]
        public void AnEffectOfYourOwnIsNeitherDescribedNorSaysItTakesNothing()
        {
            var own = new MineEffect();
            Assert.IsFalse(LetteraTagModel.HasSchema(own.Parameters));
            Assert.IsFalse(own.TakesNoParameters);
            Assert.IsNull(own.Description);
            Assert.AreEqual(0, own.Examples.Count);
        }

        [Serializable]
        sealed class MineEffect : LetteraEffect
        {
            public override void Apply(LetteraAttributes attributes, int start, int end, in LetteraSlots slots) { }
        }

        [Test]
        public void AnEffectOfYourOwnMayDeclareWhatItTakesNothingMeans()
        {
            var own = new QuietEffect();
            Assert.IsTrue(own.TakesNoParameters);
            Assert.AreEqual("Does nothing, quietly.", own.Description);
        }

        [Serializable]
        sealed class QuietEffect : LetteraEffect
        {
            public override bool TakesNoParameters => true;
            public override string Description => "Does nothing, quietly.";
            public override void Apply(LetteraAttributes attributes, int start, int end, in LetteraSlots slots) { }
        }

        // ------------------------------------------------------------- angles

        [Test]
        public void AnAngleCarriesItsDegreeSignAndStaysABareNumber()
        {
            var turn = new WobbleMotion().Parameters.First(p => p.Id == "Turn");
            Assert.AreEqual("°", turn.UnitLabel);
            Assert.AreEqual(LetteraParamKind.Float, turn.Kind, "A rule or a link can still drive it as a number");
            Assert.AreEqual(LetteraSlotState.Set, LetteraSlotValue.Classify(turn, "12"));
            StringAssert.Contains("degrees", LetteraSlotValue.Takes(turn));
            foreach (var pair in new (LetteraEffect effect, string id)[] { (new PendulumMotion(), "Turn"), (new ShakeMotion(), "Turn") })
                Assert.AreEqual("°", pair.effect.Parameters.First(p => p.Id == pair.id).UnitLabel, pair.effect.GetType().Name);
            Assert.AreEqual("°", new FillEffect().Parameters.First(p => p.Display == "Paint angle").UnitLabel);
            Assert.AreEqual("°", new InlineEffect().Parameters.First(p => p.Display == "Turn").UnitLabel);
        }

        [Test]
        public void TheCurveTagTakesARadiusNotAnAngle()
        {
            var curve = new CurveEffect();
            Assert.AreEqual("Radius", curve.Parameters[0].Display);
            Assert.AreEqual(LetteraParamKind.Length, curve.Parameters[0].Kind);
            StringAssert.Contains("not an angle", curve.Description);
        }

        [Test]
        public void ANameSlotOffersTheNamesTheTextKnows()
        {
            var inline = new InlineEffect();
            inline.Items.Add(new LetteraInlineEntry { Key = "coin" });
            inline.Items.Add(new LetteraInlineEntry { Key = "heart" });
            LetteraParamInfo name = inline.Parameters[0];
            Assert.AreEqual("inline", name.Suggests);
            CollectionAssert.IsSubsetOf(new[] { "coin", "heart" }, LetteraSlotSuggestions.For(name, inline).ToList());
            var reveal = new RevealEffect();
            reveal.Entries.Add(new LetteraEntranceEntry { Name = "mine" });
            IReadOnlyList<string> entrances = LetteraSlotSuggestions.For(reveal.Parameters[0], reveal);
            CollectionAssert.IsSubsetOf(new[] { "mine", "fade", "pop" }, entrances.ToList());
        }

        [Test]
        public void TheHelpOfASlotSaysWhatItTakesWithoutRepeatingItself()
        {
            LetteraParamInfo radius = new CurveEffect().Parameters[0];
            string help = LetteraSlotValue.Help(radius);
            StringAssert.Contains("Takes a length. A bare number means local units.", help);
            StringAssert.Contains("You can also write 0.2em or 50%.", help);
            StringAssert.EndsWith("Example: 180.", help);
            LetteraParamInfo shadowX = new ShadowEffect().Parameters[1];
            StringAssert.Contains("A bare number means em.", LetteraSlotValue.Help(shadowX));
            StringAssert.DoesNotContain("em; px; em", LetteraSlotValue.Help(shadowX));
            LetteraParamInfo color = new ColorEffect().Parameters[0];
            StringAssert.DoesNotContain("Takes a colour", LetteraSlotValue.Help(color), "A colour's hint is not said twice");
        }

        // ------------------------------------------------------------- the docs agree with the schema

        static string PackageRoot()
        {
            var info = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(LetteraText).Assembly);
            Assert.IsNotNull(info, "Lettera is not a package in this project");
            return info.resolvedPath;
        }

        // Every table row whose first cell holds tags: the tags, and the last cell, which names the parameters.
        static List<(string[] tags, string parameters, string file)> DocRows()
        {
            string root = PackageRoot();
            var rows = new List<(string[], string, string)>();
            foreach (string file in new[] { "README.md", "Documentation~/Motion.md", "Documentation~/Math.md" })
            {
                foreach (string line in File.ReadAllLines(Path.Combine(root, file)))
                {
                    if (!line.StartsWith("| `<", StringComparison.Ordinal)) continue;
                    string[] cells = line.Split('|');
                    if (cells.Length < 4) continue;
                    string[] tags = Regex.Matches(cells[1], "`<([a-z][a-z0-9-]*)>`").Cast<Match>().Select(m => m.Groups[1].Value).ToArray();
                    if (tags.Length == 0) continue;
                    rows.Add((tags, cells[cells.Length - 2].Trim().Replace("`", "").ToLowerInvariant(), file));
                }
            }
            return rows;
        }

        // The names a schema gives, in slot order. A run such as Feature 1 to Feature 4 counts once.
        static List<string> NamesOf(LetteraEffect effect)
        {
            var names = new List<string>();
            string stem = null;
            foreach (LetteraParamInfo info in Slots(effect))
            {
                if (!string.IsNullOrEmpty(info.Group)) break;
                string display = info.Display.ToLowerInvariant();
                Match numbered = Regex.Match(display, @"^(.*\D) (\d+)$");
                if (numbered.Success)
                {
                    if (numbered.Groups[1].Value == stem) continue;
                    stem = numbered.Groups[1].Value;
                }
                names.Add(display);
            }
            return names;
        }

        static Dictionary<string, LetteraEffect> DefaultTags()
        {
            var tags = new Dictionary<string, LetteraEffect>(StringComparer.OrdinalIgnoreCase);
            foreach (LetteraStyle style in LetteraDefaultStyles.Create())
            {
                string name = LtTagPanel.NameOf(style.Matcher);
                if (!string.IsNullOrEmpty(name) && style.Effect != null && !tags.ContainsKey(name)) tags[name] = style.Effect;
            }
            return tags;
        }

        static readonly string[] s_SyntaxTags = { "br", "noparse", "rt", "rp" };

        [Test]
        public void EveryTagOfTheDefaultVocabularyHasADocumentedRowThatNamesItsSlotsInOrder()
        {
            Dictionary<string, LetteraEffect> vocabulary = DefaultTags();
            List<(string[] tags, string parameters, string file)> rows = DocRows();
            var problems = new List<string>();
            var all = vocabulary.Keys.Concat(s_SyntaxTags).Distinct().ToList();
            foreach (string tag in all)
            {
                var mine = rows.Where(r => r.tags.Contains(tag)).ToList();
                if (mine.Count == 0)
                {
                    problems.Add("<" + tag + "> has no row in the README or the Motion or Math guide");
                    continue;
                }
                foreach (var row in mine)
                {
                    vocabulary.TryGetValue(tag, out LetteraEffect effect);
                    if (effect is CompositeEffect) continue;
                    List<string> names = effect != null ? NamesOf(effect) : new List<string>();
                    bool none = effect == null || effect.TakesNoParameters;
                    // A row of several tags is one set of slots, so the tags in it must agree.
                    foreach (string other in row.tags)
                    {
                        if (other == tag || !vocabulary.TryGetValue(other, out LetteraEffect otherEffect) || otherEffect is CompositeEffect) continue;
                        if (!NamesOf(otherEffect).SequenceEqual(names) || otherEffect.TakesNoParameters != none)
                            problems.Add($"{row.file}: <{tag}> and <{other}> share a row but take different slots; give each its own row");
                    }
                    if (none)
                    {
                        if (!Regex.IsMatch(row.parameters, @"(^|[^\p{L}])none([^\p{L}]|$)"))
                            problems.Add($"{row.file}: <{tag}> takes no parameters, so its row should say 'none': {row.parameters}");
                        continue;
                    }
                    int from = 0;
                    foreach (string slot in names)
                    {
                        Match match = Regex.Match(row.parameters.Substring(from), @"(?<![\p{L}\d])" + Regex.Escape(slot) + @"(?![\p{L}\d])");
                        if (!match.Success)
                        {
                            problems.Add($"{row.file}: <{tag}> does not name slot '{slot}' after the earlier ones: {row.parameters}");
                            break;
                        }
                        from += match.Index + match.Length;
                    }
                }
            }
            Assert.IsEmpty(problems, string.Join("\n", problems));
        }

        [Test]
        public void TheEffectsTableOfTheStylesGuideNamesEveryBuiltInEffect()
        {
            string docs = string.Join("\n", Directory.GetFiles(Path.Combine(PackageRoot(), "Documentation~"), "*.md").Where(f => !f.EndsWith(".ru.md")).Select(File.ReadAllText));
            var missing = BuiltInTypes().Where(t => !t.Assembly.GetName().Name.EndsWith("Text3D", StringComparison.Ordinal) && !docs.Contains(t.Name)).Select(t => t.Name).ToList();
            Assert.IsEmpty(missing, "Not named in any guide: " + string.Join(", ", missing));
        }

        [Test]
        public void TheRussianReadmeKeepsTheSameTagRows()
        {
            string root = PackageRoot();
            string[] english = File.ReadAllLines(Path.Combine(root, "README.md")).Where(l => l.StartsWith("| `<", StringComparison.Ordinal)).Select(l => l.Split('|')[1].Trim()).ToArray();
            string[] russian = File.ReadAllLines(Path.Combine(root, "README.ru.md")).Where(l => l.StartsWith("| `<", StringComparison.Ordinal)).Select(l => l.Split('|')[1].Trim()).ToArray();
            CollectionAssert.AreEqual(english, russian, "The tag column of the two READMEs differs");
        }
    }
}

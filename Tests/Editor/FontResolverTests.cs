using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace FirstSymbol.Lettera.Editor.Tests
{
    /// <summary>Looking a font up by a CSS-like family list: the order names are tried in, what each platform's generic names stand for, and what is left when nothing is found. The machine's fonts are put aside for the fixture fonts.</summary>
    public class FontResolverTests : TextFixture
    {
        LetteraFontAsset m_SavedDefault, m_SavedWeb;
        readonly List<LetteraFontAsset> m_Registered = new List<LetteraFontAsset>();

        [SetUp]
        public void PutTheMachineAside()
        {
            LetteraSettings settings = LetteraSettings.Instance;
            m_SavedDefault = settings.DefaultFont;
            m_SavedWeb = settings.WebGLFallbackFont;
            LetteraSystemFonts.FamiliesForTests = new Dictionary<string, List<LetteraSystemFonts.FamilyCut>>();
            UseHost(LetteraSystemFonts.Os.Linux);
        }

        // The host looks like another; what was found for the last one is forgotten.
        static void UseHost(LetteraSystemFonts.Os? os)
        {
            LetteraSystemFonts.HostForTests = os;
            LetteraSystemFonts.Reset();
            LetteraFontResolver.ClearCache();
        }

        [TearDown]
        public void PutTheMachineBack()
        {
            foreach (LetteraFontAsset font in m_Registered) LetteraFontResolver.Unregister(font);
            m_Registered.Clear();
            LetteraSystemFonts.FamiliesForTests = null;
            UseHost(null);
            foreach (string generic in new[] { "serif", "sans-serif", "monospace", "cursive", "fantasy" }) LetteraFontResolver.SetGenericFamilies(generic);
            LetteraSettings settings = LetteraSettings.Instance;
            settings.DefaultFont = m_SavedDefault;
            settings.WebGLFallbackFont = m_SavedWeb;
            settings.ProjectFonts.Clear();
            settings.ProjectFontStacks.Clear();
            LetteraFontResolver.ClearCache();
        }

        static string FixturePath(string file)
        {
            string asmdef = AssetDatabase.GUIDToAssetPath(AssetDatabase.FindAssets("FirstSymbol.Lettera.Editor.Tests t:asmdef")[0]);
            return Path.GetFullPath(Path.Combine(Path.GetDirectoryName(asmdef), "..", "Fonts~", file));
        }

        // The machine "has" this family: the fixture file stands for it.
        static void Install(string family, string file = "NotoSans-Regular.ttf", int weight = 400, bool italic = false, int min = 0, int max = 0)
        {
            string key = LetteraSystemFonts.FoldFamily(family);
            if (!LetteraSystemFonts.FamiliesForTests.TryGetValue(key, out List<LetteraSystemFonts.FamilyCut> cuts)) LetteraSystemFonts.FamiliesForTests[key] = cuts = new List<LetteraSystemFonts.FamilyCut>();
            cuts.Add(new LetteraSystemFonts.FamilyCut { Path = FixturePath(file), Weight = weight, Italic = italic, WeightMin = min, WeightMax = max });
            LetteraFontResolver.ClearCache();
        }

        void Register(LetteraFontAsset font, string family)
        {
            LetteraFontResolver.Register(font, family);
            m_Registered.Add(font);
        }

        LetteraFontVariant Variant(string family, int weight, bool italic)
        {
            var variant = ScriptableObject.CreateInstance<LetteraFontVariant>();
            Created.Add(variant);
            variant.Source = Latin;
            variant.Family = family;
            variant.Weight = weight;
            variant.Italic = italic;
            return variant;
        }

        [Test]
        public void AProjectFontIsFoundByItsFamilyName()
        {
            Register(Latin, "Inter");
            LetteraResolvedFont found = LetteraFontResolver.Resolve("inter");
            Assert.AreEqual(LetteraFontMatch.Project, found.Source);
            Assert.AreSame(Latin, found.Font);
            Assert.AreEqual("inter", found.Family);
            Assert.AreEqual("Noto Sans", found.FontFamily);
            Assert.IsTrue(found.IsFound);
        }

        [Test]
        public void ANamelessProjectFontIsFoundByTheFamilyItStates()
        {
            Register(Latin, null);
            Assert.AreSame(Latin, LetteraFontResolver.Resolve("Noto Sans").Font, "the file says Noto Sans");
            Assert.AreSame(Latin, LetteraFontResolver.Resolve("NotoSans").Font, "spaces and case do not matter");
        }

        [Test]
        public void TheProjectIsAskedBeforeTheSystemForTheSameName()
        {
            Register(Latin, "Inter");
            Install("Inter", "NotoSansArabic-Regular.ttf");
            Assert.AreEqual(LetteraFontMatch.Project, LetteraFontResolver.Resolve("Inter").Source);
            LetteraFontResolver.Unregister(Latin);
            LetteraFontResolver.ClearCache();
            LetteraResolvedFont system = LetteraFontResolver.Resolve("Inter");
            Assert.AreEqual(LetteraFontMatch.System, system.Source);
            Assert.AreEqual("Noto Sans Arabic", system.FontFamily);
        }

        [Test]
        public void TheListIsTriedLeftToRight()
        {
            Install("Segoe UI", "NotoSansHebrew-Regular.ttf");
            Register(Latin, "Inter");
            Assert.AreEqual("Segoe UI", LetteraFontResolver.Resolve("Missing, Segoe UI, Inter").Family, "the system font is listed before the project's");
            Assert.AreEqual("Inter", LetteraFontResolver.Resolve("Missing, Inter, Segoe UI").Family);
        }

        [Test]
        public void QuotesAndSpacesInTheListAreRead()
        {
            Install("Segoe UI");
            Install("Foo, Bar", "NotoSansHebrew-Regular.ttf");
            Assert.AreEqual("Segoe UI", LetteraFontResolver.Resolve("  \"Segoe UI\" ,serif").Family);
            Assert.AreEqual("Segoe UI", LetteraFontResolver.Resolve("'Segoe UI'").Family);
            Assert.AreEqual("Foo, Bar", LetteraFontResolver.Resolve("\"Foo, Bar\", Segoe UI").Family, "a comma inside quotes is part of the name");
        }

        [TestCase("Windows", "Segoe UI", "Times New Roman", "Consolas")]
        [TestCase("Apple", "SF Pro", "Times", "Menlo")]
        [TestCase("Mobile", "SF Pro", "Times New Roman", "Menlo")]
        [TestCase("Android", "sans-serif", "serif", "monospace")]
        [TestCase("Linux", "Noto Sans", "Noto Serif", "Noto Sans Mono")]
        public void GenericNamesStandForEachPlatformsOwnFonts(string host, string sans, string serif, string mono)
        {
            UseHost((LetteraSystemFonts.Os)Enum.Parse(typeof(LetteraSystemFonts.Os), host));
            var list = new List<string>();
            Assert.IsTrue(LetteraFontResolver.GetGenericFamilies("sans-serif", list));
            Assert.AreEqual(sans, list[0]);
            LetteraFontResolver.GetGenericFamilies("serif", list);
            Assert.AreEqual(serif, list[0]);
            LetteraFontResolver.GetGenericFamilies("monospace", list);
            Assert.AreEqual(mono, list[0]);
            Assert.IsFalse(LetteraFontResolver.GetGenericFamilies("Arial", list));

            // The second family of the list is the one installed: the first is passed over.
            LetteraFontResolver.GetGenericFamilies("serif", list);
            Install(list[1], "NotoSansHebrew-Regular.ttf");
            LetteraResolvedFont found = LetteraFontResolver.Resolve("serif");
            Assert.AreEqual(LetteraFontMatch.Generic, found.Source);
            Assert.AreEqual("serif", found.Family);
            Assert.AreEqual("Noto Sans Hebrew", found.FontFamily);

            // The first, when it is installed, wins.
            Install(list[0], "NotoSansArabic-Regular.ttf");
            Assert.AreEqual("Noto Sans Arabic", LetteraFontResolver.Resolve("serif").FontFamily);
        }

        [Test]
        public void GenericNamesAreKeywordsOnlyWhenNotQuoted()
        {
            Install("Noto Sans");
            Assert.AreEqual(LetteraFontMatch.Generic, LetteraFontResolver.Resolve("sans-serif").Source);
            Assert.AreEqual(LetteraFontMatch.Generic, LetteraFontResolver.Resolve("system-ui").Source);
            Assert.AreEqual(LetteraFontMatch.Generic, LetteraFontResolver.Resolve("SANS-SERIF").Source);
            Assert.AreEqual(LetteraFontMatch.Fallback, LetteraFontResolver.Resolve("\"sans-serif\"").Source, "quoted, it is a family called that");
        }

        [Test]
        public void AProjectFontCanStandInForAGenericName()
        {
            Register(Latin, "serif");
            Install("Noto Serif", "NotoSansHebrew-Regular.ttf");
            LetteraResolvedFont found = LetteraFontResolver.Resolve("serif");
            Assert.AreEqual(LetteraFontMatch.Project, found.Source);
            Assert.AreSame(Latin, found.Font);
        }

        [Test]
        public void GenericFamiliesCanBeSetByTheProject()
        {
            Install("My Serif", "NotoSansArabic-Regular.ttf");
            LetteraFontResolver.SetGenericFamilies("serif", "Nothing Here", "My Serif");
            Assert.AreEqual("Noto Sans Arabic", LetteraFontResolver.Resolve("serif").FontFamily);
            LetteraFontResolver.SetGenericFamilies("serif");
            var list = new List<string>();
            LetteraFontResolver.GetGenericFamilies("serif", list);
            Assert.AreEqual("Noto Serif", list[0], "no families gives the platform's back");
        }

        [Test]
        public void WhenNothingIsFoundTheProjectsDefaultFontIsTheFallback()
        {
            LetteraSettings.Instance.DefaultFont = Arabic;
            LetteraResolvedFont found = LetteraFontResolver.Resolve("Nothing, serif");
            Assert.AreEqual(LetteraFontMatch.Fallback, found.Source);
            Assert.AreSame(Arabic, found.Font);
            Assert.AreEqual("", found.Family);
            Assert.AreSame(Arabic, LetteraFontResolver.Resolve("").Font, "an empty list is the fallback");
            Assert.AreSame(Arabic, LetteraFontResolver.Resolve(null).Font);
        }

        [Test]
        public void WithoutDefaultFontTheFallbackIsTheSystemsDefaultFace()
        {
            LetteraSettings.Instance.DefaultFont = null;
            UseHost(null);
            if (!LetteraSystemFonts.IsSupported || LetteraSystemFonts.DefaultFaces().Length == 0) Assert.Ignore("this machine has no default face");
            LetteraResolvedFont found = LetteraFontResolver.Resolve("Nothing Like This Exists");
            Assert.AreEqual(LetteraFontMatch.Fallback, found.Source);
            Assert.AreEqual(LetteraSystemFonts.DefaultFaces()[0].Family, found.FontFamily);
        }

        [Test]
        public void OnTheWebThereAreNoSystemFontsAndTheWebFallbackFontIsUsed()
        {
            UseHost(LetteraSystemFonts.Os.None);
            LetteraSettings.Instance.DefaultFont = null;
            LetteraSettings.Instance.WebGLFallbackFont = Hebrew;
            LetteraFontResolver.ClearCache();
            Install("Arial");
            LetteraResolvedFont found = LetteraFontResolver.Resolve("Arial, serif, monospace");
            Assert.AreEqual(LetteraFontMatch.Fallback, found.Source, "the system's fonts are not there for any name");
            Assert.AreSame(Hebrew, found.Font);

            LetteraSettings.Instance.WebGLFallbackFont = null;
            LetteraFontResolver.ClearCache();
            LetteraResolvedFont bundled = LetteraFontResolver.Resolve("Arial");
            Assert.AreEqual(LetteraFontMatch.Fallback, bundled.Source);
            Assert.IsTrue(bundled.IsFound, "the font the package ships for the web");

            Register(Latin, "Inter");
            Assert.AreEqual(LetteraFontMatch.Project, LetteraFontResolver.Resolve("Inter").Source, "a project's font works on the web");
        }

        [Test]
        public void NothingAtAllGivesNone()
        {
            UseHost(LetteraSystemFonts.Os.None);
            LetteraSettings.Instance.DefaultFont = null;
            LetteraSettings.Instance.WebGLFallbackFont = null;
            LetteraFontResolver.ClearCache();
            var shipped = Resources.Load<LetteraFont>(FontChain.WebFontResource);
            if (shipped != null) Assert.Ignore("the package ships a web font, which is always there");
            LetteraResolvedFont found = LetteraFontResolver.Resolve("Arial");
            Assert.AreEqual(LetteraFontMatch.None, found.Source);
            Assert.IsFalse(found.IsFound);
        }

        [Test]
        public void TheNearestWeightAndSlopeOfAProjectFamilyIsPicked()
        {
            LetteraFontVariant bold = Variant("Test Sans", 700, false);
            LetteraFontVariant italic = Variant("Test Sans", 400, true);
            LetteraFontVariant light = Variant("Test Sans", 300, false);
            Register(bold, "Test Sans");
            Register(italic, "Test Sans");
            Register(light, "Test Sans");
            Register(Latin, "Test Sans");

            Assert.AreSame(bold, LetteraFontResolver.Resolve("Test Sans", 700).Font);
            Assert.AreSame(bold, LetteraFontResolver.Resolve("Test Sans", 900).Font, "heavier than any: the heaviest");
            Assert.AreSame(light, LetteraFontResolver.Resolve("Test Sans", 200).Font, "lighter than any: the lightest");
            Assert.AreSame(Latin, LetteraFontResolver.Resolve("Test Sans", 400).Font);
            Assert.AreSame(Latin, LetteraFontResolver.Resolve("Test Sans").Font, "400 and upright by default");
            LetteraResolvedFont slanted = LetteraFontResolver.Resolve("Test Sans", 400, true);
            Assert.AreSame(italic, slanted.Font);
            Assert.IsTrue(slanted.CutItalic);
            Assert.IsFalse(slanted.NeedsSyntheticItalic);
            LetteraResolvedFont boldItalic = LetteraFontResolver.Resolve("Test Sans", 700, true);
            Assert.AreSame(italic, boldItalic.Font, "an italic cut beats a bold upright one");
            Assert.IsTrue(boldItalic.NeedsSyntheticBold);
            Assert.AreEqual(700, boldItalic.Weight);
            Assert.AreEqual(400, boldItalic.CutWeight);
        }

        [Test]
        public void ASingleCutNeedsSyntheticBoldAndItalic()
        {
            Register(Latin, "Plain");
            LetteraResolvedFont found = LetteraFontResolver.Resolve("Plain", 700, true);
            Assert.AreSame(Latin, found.Font);
            Assert.IsTrue(found.NeedsSyntheticBold);
            Assert.IsTrue(found.NeedsSyntheticItalic);
            LetteraResolvedFont regular = LetteraFontResolver.Resolve("Plain", 500);
            Assert.IsFalse(regular.NeedsSyntheticBold, "500 is close enough to 400");
            Assert.IsFalse(regular.NeedsSyntheticItalic);
        }

        [Test]
        public void AFontStacksFamilyIsFoundWithItsStack()
        {
            LetteraFontVariant bold = Variant("Brand", 700, false);
            var stack = ScriptableObject.CreateInstance<LetteraFontStack>();
            Created.Add(stack);
            stack.Families.Add(new LetteraFontFamily { Name = "Brand", Primary = Latin, Faces = { bold } });
            LetteraSettings.Instance.ProjectFontStacks.Add(stack);
            LetteraFontResolver.ClearCache();
            LetteraResolvedFont regular = LetteraFontResolver.Resolve("Brand");
            Assert.AreSame(stack, regular.Stack);
            Assert.AreSame(Latin, regular.Font);
            LetteraResolvedFont heavy = LetteraFontResolver.Resolve("brand", 700);
            Assert.AreSame(bold, heavy.Font);
            Assert.AreSame(stack, heavy.Stack);
            Assert.AreEqual(LetteraFontMatch.Project, heavy.Source);
        }

        [Test]
        public void TheSettingsFontListIsRead()
        {
            LetteraSettings.Instance.ProjectFonts.Add(new LetteraProjectFont { Family = "Listed", Font = Hebrew });
            LetteraFontResolver.ClearCache();
            LetteraResolvedFont found = LetteraFontResolver.Resolve("Listed");
            Assert.AreEqual(LetteraFontMatch.Project, found.Source);
            Assert.AreSame(Hebrew, found.Font);
        }

        [Test]
        public void ARegisteredFontCanBeTakenBack()
        {
            Assert.AreEqual(LetteraFontMatch.Fallback, LetteraFontResolver.Resolve("Zed").Source);
            Register(Latin, "Zed");
            Assert.AreEqual(LetteraFontMatch.Project, LetteraFontResolver.Resolve("Zed").Source, "the cache does not hide a new font");
            Assert.IsTrue(LetteraFontResolver.Unregister(Latin));
            Assert.IsFalse(LetteraFontResolver.Unregister(Latin));
            Assert.AreEqual(LetteraFontMatch.Fallback, LetteraFontResolver.Resolve("Zed").Source);
        }

        [Test]
        public void ATextPicksTheFirstFontThatCoversIt()
        {
            if (Hebrew.LoadFace().HasGlyph('a')) Assert.Ignore("the Hebrew fixture has Latin letters");
            Register(Hebrew, "Hebrew Face");
            Register(Latin, "Latin Face");
            Assert.AreSame(Hebrew, LetteraFontResolver.Resolve("Hebrew Face, Latin Face", 400, false, "שלום").Font);
            LetteraResolvedFont latin = LetteraFontResolver.Resolve("Hebrew Face, Latin Face", 400, false, "hello world");
            Assert.AreSame(Latin, latin.Font);
            Assert.IsTrue(latin.CoversText);
            Assert.AreSame(Hebrew, LetteraFontResolver.Resolve("Hebrew Face, Latin Face").Font, "no text: the first");

            // Neither covers all of it: the one that covers more, and it says so.
            LetteraResolvedFont mixed = LetteraFontResolver.Resolve("Hebrew Face, Latin Face", 400, false, "abc שלוםא");
            Assert.AreSame(Hebrew, mixed.Font);
            Assert.IsFalse(mixed.CoversText);
            Assert.AreSame(Latin, LetteraFontResolver.Resolve("Hebrew Face, Latin Face", 400, false, "abcd ש").Font);
        }

        [Test]
        public void LineBreaksAndJoinersNeedNoGlyph()
        {
            Register(Latin, "Latin Face");
            Assert.IsTrue(LetteraFontResolver.Resolve("Latin Face", 400, false, "a\nb\t‍c").CoversText);
        }

        [Test]
        public void ASecondLookupAllocatesNothing()
        {
            Register(Latin, "Inter");
            Install("Segoe UI");
            const string list = "Missing, Segoe UI, Inter, sans-serif";
            LetteraResolvedFont first = LetteraFontResolver.Resolve(list, 700, false, "Hello, world");
            LetteraFontResolver.Resolve(list, 700, false, "Hello, world");
            long before = GC.GetAllocatedBytesForCurrentThread();
            LetteraResolvedFont again = default;
            for (int i = 0; i < 200; i++) again = LetteraFontResolver.Resolve(list, 700, false, "Hello, world");
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.AreEqual(0, allocated);
            Assert.AreSame(first.Font, again.Font);
        }

        [Test]
        public void AnInstalledFamilyOfTheMachineIsFoundAndDraws()
        {
            LetteraSystemFonts.FamiliesForTests = null;
            UseHost(null);
            if (!LetteraSystemFonts.IsSupported) Assert.Ignore("no system fonts here");
            LetteraResolvedFont found = LetteraFontResolver.Resolve("Arial, Liberation Sans, DejaVu Sans, Helvetica", 400, false, "Hello");
            if (found.Source != LetteraFontMatch.System) Assert.Ignore("none of the common families is installed here");
            Assert.IsTrue(found.CoversText);
            Assert.IsInstanceOf<LetteraSystemFont>(found.Font);
            LetteraWorldText text = Make("Hello", font: found.Font);
            Assert.AreEqual(5, text.GlyphCount);

            LetteraResolvedFont bold = LetteraFontResolver.Resolve(found.Family, 700);
            Assert.GreaterOrEqual(bold.CutWeight, found.CutWeight, "a bold cut, where the family has one");
            if (bold.CutWeight >= 700) Assert.IsFalse(bold.NeedsSyntheticBold);
        }

        [Test]
        public void ARealGenericNameResolvesOnThisMachine()
        {
            LetteraSystemFonts.FamiliesForTests = null;
            UseHost(null);
            if (!LetteraSystemFonts.IsSupported) Assert.Ignore("no system fonts here");
            LetteraResolvedFont sans = LetteraFontResolver.Resolve("sans-serif", 400, false, "Hello");
            Assert.IsTrue(sans.IsFound);
            Assert.IsTrue(sans.Source == LetteraFontMatch.Generic || sans.Source == LetteraFontMatch.Fallback);
            LetteraResolvedFont mono = LetteraFontResolver.Resolve("monospace", 400, false, "Hello");
            Assert.IsTrue(mono.IsFound);
        }

        [Test]
        public void AVariableFontIsDrawnAtTheWeightAsked()
        {
            // The fixture file is not variable; its listing says the range, which is all the choice reads.
            Install("Range Sans", "NotoSans-Regular.ttf", 400, false, 100, 900);
            LetteraResolvedFont found = LetteraFontResolver.Resolve("Range Sans", 650);
            Assert.AreEqual(650, found.CutWeight);
            Assert.IsFalse(found.NeedsSyntheticBold);
            Assert.AreEqual(900, LetteraFontResolver.Resolve("Range Sans", 1000).CutWeight);
        }

        [Test]
        public void ACutOfTheSystemIsOneFaceWithNoOthers()
        {
            Install("Pair Sans", "NotoSans-Regular.ttf", 400);
            Install("Pair Sans", "NotoSansHebrew-Regular.ttf", 700);
            LetteraResolvedFont heavy = LetteraFontResolver.Resolve("Pair Sans", 700);
            Assert.AreEqual("Noto Sans Hebrew", heavy.FontFamily);
            Assert.AreEqual(700, heavy.CutWeight);
            var system = (LetteraSystemFont)heavy.Font;
            Assert.AreEqual(Path.GetFullPath(FixturePath("NotoSansHebrew-Regular.ttf")), Path.GetFullPath(system.ResolvedPath));
            var cuts = new List<FontFace>();
            system.AddCuts(cuts);
            Assert.AreEqual(0, cuts.Count);
            Assert.AreEqual(400, LetteraFontResolver.Resolve("Pair Sans", 400).CutWeight);
        }
    }
}

using System;
using System.Collections.Generic;
using FirstSymbol.Lettera.Unicode;
using UnityEngine;

namespace FirstSymbol.Lettera
{
    /// <summary>Where a font a lookup found came from.</summary>
    public enum LetteraFontMatch : byte
    {
        /// <summary>Nothing was found: the project has no font, the system has none, and no fallback font is set.</summary>
        None,

        /// <summary>A font of the project: one in the project's font list or a stack of it, or one registered at run time.</summary>
        Project,

        /// <summary>An installed font of the system, by the family name the list gave.</summary>
        System,

        /// <summary>The platform's font for a generic family such as serif, sans-serif or monospace.</summary>
        Generic,

        /// <summary>The fallback: the project's default font, else the system's default face, else on the web the web fallback font.</summary>
        Fallback,
    }

    /// <summary>What <see cref="LetteraFontResolver"/> found.</summary>
    public readonly struct LetteraResolvedFont
    {
        internal LetteraResolvedFont(LetteraFontAsset font, LetteraFontStack stack, LetteraFontMatch source, string family, string fontFamily, int weight, bool italic, int cutWeight, bool cutItalic, bool covers)
        {
            Font = font;
            Stack = stack;
            Source = source;
            Family = family ?? "";
            FontFamily = fontFamily ?? "";
            Weight = weight;
            Italic = italic;
            CutWeight = cutWeight;
            CutItalic = cutItalic;
            CoversText = covers;
        }

        /// <summary>The font to draw with: the cut of the family nearest the weight and slope asked. Null when <see cref="Source"/> is None. Set it as a text's <c>Font</c>.</summary>
        public LetteraFontAsset Font { get; }

        /// <summary>The font stack of the project the family was found in, or null for a font alone or the system's.</summary>
        public LetteraFontStack Stack { get; }

        public LetteraFontMatch Source { get; }

        /// <summary>The name from the list that matched, as written there without quotes; empty for the fallback.</summary>
        public string Family { get; }

        /// <summary>The family the font itself states, such as "Segoe UI" when the list said "sans-serif".</summary>
        public string FontFamily { get; }

        /// <summary>The weight asked for, 1 to 1000, as CSS has it: 400 regular, 700 bold.</summary>
        public int Weight { get; }

        /// <summary>Whether italic was asked for.</summary>
        public bool Italic { get; }

        /// <summary>The weight of <see cref="Font"/>. For a variable font, the weight asked for within the axis it reaches; draw it with <c>&lt;b=Weight&gt;</c> to get there.</summary>
        public int CutWeight { get; }

        /// <summary>Whether <see cref="Font"/> is an italic or oblique cut.</summary>
        public bool CutItalic { get; }

        /// <summary>Whether the font has a glyph for every character of the text asked about. False means the best of the fonts that came close; Lettera's own fallback draws the rest.</summary>
        public bool CoversText { get; }

        /// <summary>True when a font was found.</summary>
        public bool IsFound => Font != null;

        /// <summary>The cut is much lighter than the weight asked for: a text asking for bold makes the face heavier itself.</summary>
        public bool NeedsSyntheticBold => Weight >= 600 && CutWeight < Weight - 100;

        /// <summary>Italic was asked for and the cut is upright: a text asking for italic slants it itself.</summary>
        public bool NeedsSyntheticItalic => Italic && !CutItalic;
    }

    /// <summary>
    /// Finds the font for a CSS-like font-family list, a weight, a slope and a text: <c>Resolve("Inter, Segoe UI, sans-serif", 700, false, text)</c>.
    /// The list is tried left to right. For each name the project is asked first (its font list and font stacks), then the system's fonts, found by
    /// family name. A generic name (serif, sans-serif, monospace, cursive, fantasy, system-ui) stands for the platform's own fonts of that kind,
    /// tried in order. Last comes the fallback: the project's default font, else the system's default face, else on the web the web fallback font.
    /// The first font that has a glyph for every character of the text wins; when none does, the one that covers most. Everything is cached
    /// by list, weight and slope: the second call with the same arguments allocates nothing. Call it from the main thread.
    /// </summary>
    public static class LetteraFontResolver
    {
        sealed class Candidate
        {
            public LetteraFontAsset Asset;
            public LetteraFontStack Stack;
            public LetteraFontMatch Source;
            public string Family, FontFamily;
            public int CutWeight;
            public bool CutItalic;
        }

        enum ItemKind : byte { Named, Keyword, Member, SystemDefault, Fallback }

        sealed class Item
        {
            public string Name, Family;
            public ItemKind Kind;
            public bool Tried;
            public Candidate Found;
        }

        sealed class Plan
        {
            public Item[] Items;
            public int Weight;
            public bool Italic;
        }

        struct PlanKey : IEquatable<PlanKey>
        {
            public string Families;
            public int Weight;
            public bool Italic;

            public bool Equals(PlanKey other) => Weight == other.Weight && Italic == other.Italic && string.Equals(Families, other.Families, StringComparison.Ordinal);
            public override bool Equals(object obj) => obj is PlanKey other && Equals(other);
            public override int GetHashCode() => ((Families.GetHashCode() * 397) ^ Weight) * 2 + (Italic ? 1 : 0);
        }

        struct ProjectEntry
        {
            public LetteraFontAsset Asset;
            public LetteraFontStack Stack;
        }

        const int MaxPlans = 512;

        static readonly Dictionary<PlanKey, Plan> s_Plans = new Dictionary<PlanKey, Plan>();
        static Dictionary<string, List<ProjectEntry>> s_Project;
        static readonly Dictionary<string, LetteraSystemFont> s_Cuts = new Dictionary<string, LetteraSystemFont>();
        static readonly List<(string family, LetteraFontAsset font)> s_Registered = new List<(string, LetteraFontAsset)>();
        static readonly List<LetteraFontStack> s_RegisteredStacks = new List<LetteraFontStack>();
        static readonly Dictionary<string, string[]> s_GenericOverrides = new Dictionary<string, string[]>();
        static bool s_Hooked;

        /// <summary>
        /// The font for a font-family list, weight and slope that covers a text. The list is CSS's: names separated by commas, quoted if they
        /// hold spaces, generic names unquoted. An empty list gives the fallback. The weight is CSS's, 100 to 900, and is clamped to 1 to 1000.
        /// An empty text is covered by the first font found.
        /// </summary>
        public static LetteraResolvedFont Resolve(string families, int weight = 400, bool italic = false, ReadOnlySpan<char> text = default)
        {
            Hook();
            weight = Mathf.Clamp(weight, 1, 1000);
            var key = new PlanKey { Families = families ?? "", Weight = weight, Italic = italic };
            if (!s_Plans.TryGetValue(key, out Plan plan))
            {
                if (s_Plans.Count >= MaxPlans) s_Plans.Clear();
                plan = Parse(key.Families, weight, italic);
                s_Plans[key] = plan;
            }
            for (int attempt = 0; ; attempt++)
            {
                if (TryPick(plan, text, out LetteraResolvedFont picked) || attempt > 0) return picked;
                // A font that was destroyed under the cache: forget it all and look again.
                Invalidate();
                plan = Parse(key.Families, weight, italic);
                s_Plans[key] = plan;
            }
        }

        /// <summary>Adds a font to the lookups at run time, as a font in the project's list, under a family name; null reads the family from the font.</summary>
        public static void Register(LetteraFontAsset font, string family = null)
        {
            if (font == null) throw new ArgumentNullException(nameof(font));
            s_Registered.Add((family, font));
            Invalidate();
        }

        /// <summary>Adds a font stack to the lookups at run time: its families are found by name.</summary>
        public static void Register(LetteraFontStack stack)
        {
            if (stack == null) throw new ArgumentNullException(nameof(stack));
            s_RegisteredStacks.Add(stack);
            Invalidate();
        }

        /// <summary>Takes back what <see cref="Register(LetteraFontAsset, string)"/> added for this font. Returns whether it was there.</summary>
        public static bool Unregister(LetteraFontAsset font)
        {
            int removed = s_Registered.RemoveAll(entry => entry.font == font);
            if (removed > 0) Invalidate();
            return removed > 0;
        }

        /// <summary>Takes back what <see cref="Register(LetteraFontStack)"/> added.</summary>
        public static bool Unregister(LetteraFontStack stack)
        {
            bool removed = s_RegisteredStacks.Remove(stack);
            if (removed) Invalidate();
            return removed;
        }

        /// <summary>
        /// Sets the families a generic name stands for, in the order they are tried, replacing the platform's. No families gives back the platform's.
        /// The generic names are serif, sans-serif, monospace, cursive, fantasy and system-ui.
        /// </summary>
        public static void SetGenericFamilies(string generic, params string[] families)
        {
            string key = GenericKey(generic);
            if (key == null) throw new ArgumentException("not a generic family name: " + generic, nameof(generic));
            if (families == null || families.Length == 0) s_GenericOverrides.Remove(key);
            else s_GenericOverrides[key] = (string[])families.Clone();
            Invalidate();
        }

        /// <summary>Writes the families a generic name stands for on this platform, in the order they are tried; false if the name is not generic.</summary>
        public static bool GetGenericFamilies(string generic, List<string> into)
        {
            if (into == null) throw new ArgumentNullException(nameof(into));
            into.Clear();
            string key = GenericKey(generic);
            if (key == null) return false;
            into.AddRange(GenericList(key));
            return true;
        }

        /// <summary>Forgets what was found, so the next lookup reads the project's font list and the system's fonts again. Call it after changing <see cref="LetteraSettings.ProjectFonts"/> or installing a font.</summary>
        public static void ClearCache()
        {
            Invalidate();
            LetteraSystemFonts.ForgetFamilies();
        }

        /// <summary>Reads the names of the system's font files now, which a first lookup of a family that is not one of a file's name would do and which takes some tens of milliseconds. Browsers have no system fonts and nothing happens.</summary>
        public static void PrepareSystemFonts() => LetteraSystemFonts.PrepareFamilies();

        internal static void Invalidate()
        {
            s_Plans.Clear();
            s_Project = null;
            // The assets for installed cuts are kept: they hold only a path.
        }

        static void Hook()
        {
            if (s_Hooked) return;
            s_Hooked = true;
            LetteraFontAsset.FontAssetChanged += _ => Invalidate();
            LetteraFontStack.StackChanged += _ => Invalidate();
            LetteraSettings.Changed += Invalidate;
        }

        // ---- the list ----

        static Plan Parse(string families, int weight, bool italic)
        {
            var items = new List<Item>();
            int i = 0;
            while (i < families.Length)
            {
                while (i < families.Length && (families[i] == ',' || char.IsWhiteSpace(families[i]))) i++;
                if (i >= families.Length) break;
                bool quoted = families[i] == '"' || families[i] == '\'';
                string name;
                if (quoted)
                {
                    char quote = families[i++];
                    int end = families.IndexOf(quote, i);
                    if (end < 0) end = families.Length;
                    name = families.Substring(i, end - i).Trim();
                    i = Math.Min(end + 1, families.Length);
                    while (i < families.Length && families[i] != ',') i++;
                }
                else
                {
                    int end = families.IndexOf(',', i);
                    if (end < 0) end = families.Length;
                    name = families.Substring(i, end - i).Trim();
                    i = end;
                }
                if (name.Length == 0) continue;
                string generic = quoted ? null : GenericKey(name);
                if (generic == null)
                {
                    items.Add(new Item { Name = name, Kind = ItemKind.Named });
                    continue;
                }
                items.Add(new Item { Name = name, Kind = ItemKind.Keyword });
                foreach (string member in GenericList(generic)) items.Add(new Item { Name = name, Kind = ItemKind.Member, Family = member });
                if (generic == "sans-serif") items.Add(new Item { Name = name, Kind = ItemKind.SystemDefault });
            }
            items.Add(new Item { Name = "", Kind = ItemKind.Fallback });
            return new Plan { Items = items.ToArray(), Weight = weight, Italic = italic };
        }

        // ---- picking ----

        static bool TryPick(Plan plan, ReadOnlySpan<char> text, out LetteraResolvedFont result)
        {
            Candidate best = null;
            int bestCovered = -1;
            Item[] items = plan.Items;
            for (int n = 0; n < items.Length; n++)
            {
                Item item = items[n];
                Candidate candidate = Find(item, plan);
                if (candidate == null) continue;
                if (candidate.Asset == null)
                {
                    result = default;
                    return false;
                }
                int covered = Covered(candidate.Asset, text, out int total);
                if (covered == total)
                {
                    result = Make(candidate, plan, true);
                    return true;
                }
                if (covered > bestCovered)
                {
                    best = candidate;
                    bestCovered = covered;
                }
            }
            result = best != null ? Make(best, plan, false) : new LetteraResolvedFont(null, null, LetteraFontMatch.None, "", "", plan.Weight, plan.Italic, 0, false, false);
            return true;
        }

        static LetteraResolvedFont Make(Candidate c, Plan plan, bool covers) =>
            new LetteraResolvedFont(c.Asset, c.Stack, c.Source, c.Family, c.FontFamily, plan.Weight, plan.Italic, c.CutWeight, c.CutItalic, covers);

        static Candidate Find(Item item, Plan plan)
        {
            if (item.Tried) return item.Found;
            item.Tried = true;
            switch (item.Kind)
            {
                case ItemKind.Named:
                case ItemKind.Keyword:
                    item.Found = FromProject(item.Name, plan) ?? (item.Kind == ItemKind.Named ? FromSystem(item.Name, item.Name, LetteraFontMatch.System, plan) : null);
                    break;
                case ItemKind.Member:
                    item.Found = FromSystem(item.Family, item.Name, LetteraFontMatch.Generic, plan);
                    break;
                case ItemKind.SystemDefault:
                    item.Found = FromDefault(plan, LetteraFontMatch.Generic, item.Name);
                    break;
                default:
                    item.Found = FromFallback(plan);
                    break;
            }
            return item.Found;
        }

        // Counts the characters of the text the font has a glyph for. Line breaks, controls and joiners need none.
        static int Covered(LetteraFontAsset asset, ReadOnlySpan<char> text, out int total)
        {
            total = 0;
            if (text.IsEmpty) return 0;
            FontFace face = asset.LoadFace();
            int covered = 0;
            for (int i = 0; i < text.Length; i++)
            {
                int cp = text[i];
                if (char.IsHighSurrogate(text[i]) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
                {
                    cp = char.ConvertToUtf32(text[i], text[i + 1]);
                    i++;
                }
                if (cp < 0x20 || (cp >= 0x7F && cp < 0xA0) || LetteraUnicode.IsDefaultIgnorable(cp)) continue;
                total++;
                if (face != null && face.HasGlyph(cp)) covered++;
            }
            return covered;
        }

        // ---- the project ----

        static Candidate FromProject(string name, Plan plan)
        {
            if (s_Project == null) s_Project = BuildProject();
            if (!s_Project.TryGetValue(LetteraSystemFonts.FoldFamily(name), out List<ProjectEntry> entries)) return null;
            ProjectEntry best = default;
            int bestScore = int.MaxValue, bestWeight = 0;
            bool bestItalic = false;
            string bestFamily = "";
            foreach (ProjectEntry entry in entries)
            {
                if (entry.Asset == null) continue;
                FontFace face = entry.Asset.LoadFace();
                if (face == null) continue;
                int weight = face.Weight, min = 0, max = 0;
                if (face.HasWeightAxis)
                {
                    min = (int)face.WeightAxisMin;
                    max = (int)face.WeightAxisMax;
                }
                int reach = max > 0 ? Mathf.Clamp(plan.Weight, min, max) : weight;
                int score = FontChain.WeightPenalty(plan.Weight, reach) + (face.IsItalic != plan.Italic ? 10000 : 0);
                if (score >= bestScore) continue;
                bestScore = score;
                best = entry;
                bestWeight = reach;
                bestItalic = face.IsItalic;
                bestFamily = face.Family;
            }
            if (best.Asset == null) return null;
            return new Candidate { Asset = best.Asset, Stack = best.Stack, Source = LetteraFontMatch.Project, Family = name, FontFamily = bestFamily, CutWeight = bestWeight, CutItalic = bestItalic };
        }

        static Dictionary<string, List<ProjectEntry>> BuildProject()
        {
            var index = new Dictionary<string, List<ProjectEntry>>();
            void Add(string family, LetteraFontAsset asset, LetteraFontStack stack)
            {
                if (asset == null) return;
                if (string.IsNullOrEmpty(family)) family = asset.FamilyName;
                string key = LetteraSystemFonts.FoldFamily(family);
                if (key.Length == 0) return;
                if (!index.TryGetValue(key, out List<ProjectEntry> list)) index[key] = list = new List<ProjectEntry>();
                list.Add(new ProjectEntry { Asset = asset, Stack = stack });
            }
            void AddStack(LetteraFontStack stack)
            {
                var visited = new HashSet<LetteraFontStack>();
                for (LetteraFontStack s = stack; s != null && visited.Add(s); s = s.FallbackStack)
                {
                    foreach (LetteraFontFamily family in s.Families)
                    {
                        if (family == null || family.Primary == null) continue;
                        string name = family.Name;
                        Add(name, family.Primary, s);
                        foreach (LetteraFontAsset face in family.Faces) Add(name, face, s);
                    }
                }
            }
            LetteraSettings settings = LetteraSettings.Instance;
            foreach (LetteraProjectFont entry in settings.ProjectFonts)
                if (entry != null)
                    Add(entry.Family, entry.Font, null);
            foreach (LetteraFontStack stack in settings.ProjectFontStacks) AddStack(stack);
            foreach ((string family, LetteraFontAsset font) in s_Registered) Add(family, font, null);
            foreach (LetteraFontStack stack in s_RegisteredStacks) AddStack(stack);
            Add(null, settings.DefaultFont, null);
            return index;
        }

        // ---- the system ----

        static Candidate FromSystem(string name, string written, LetteraFontMatch source, Plan plan)
        {
            List<LetteraSystemFonts.FamilyCut> cuts = LetteraSystemFonts.FamilyCuts(name);
            if (cuts == null || cuts.Count == 0) return null;
            return FromCuts(cuts, plan, source, written, null);
        }

        static Candidate FromCuts(List<LetteraSystemFonts.FamilyCut> cuts, Plan plan, LetteraFontMatch source, string written, string familyOverride)
        {
            int bestScore = int.MaxValue, bestReach = 0;
            LetteraSystemFonts.FamilyCut best = default;
            foreach (LetteraSystemFonts.FamilyCut cut in cuts)
            {
                int reach = cut.WeightMax > 0 ? Mathf.Clamp(plan.Weight, cut.WeightMin, cut.WeightMax) : cut.Weight;
                int score = FontChain.WeightPenalty(plan.Weight, reach) + (cut.Italic != plan.Italic ? 10000 : 0);
                if (score >= bestScore) continue;
                bestScore = score;
                best = cut;
                bestReach = reach;
            }
            if (best.Path == null) return null;
            // A variable font is drawn at the weight asked; any other as it is.
            int pinned = best.WeightMax > 0 ? bestReach : 0;
            string id = best.Path + "|" + best.Index + "|" + pinned;
            if (!s_Cuts.TryGetValue(id, out LetteraSystemFont asset) || asset == null)
            {
                asset = ScriptableObject.CreateInstance<LetteraSystemFont>();
                asset.hideFlags = HideFlags.HideAndDontSave;
                asset.name = "Lettera system font " + System.IO.Path.GetFileName(best.Path);
                asset.Pin(best.Path, best.Index, pinned);
                s_Cuts[id] = asset;
            }
            FontFace face = asset.LoadFace();
            if (face == null) return null;
            return new Candidate { Asset = asset, Source = source, Family = written, FontFamily = familyOverride ?? face.Family, CutWeight = bestReach, CutItalic = best.Italic };
        }

        // The system's default family, cut for the weight and slope.
        static Candidate FromDefault(Plan plan, LetteraFontMatch source, string written)
        {
            FontFace[] faces = LetteraSystemFonts.DefaultFaces();
            if (faces.Length == 0) return null;
            var cuts = new List<LetteraSystemFonts.FamilyCut>(faces.Length);
            foreach (FontFace face in faces)
            {
                if (face.FilePath == null) continue;
                cuts.Add(new LetteraSystemFonts.FamilyCut
                {
                    Path = face.FilePath, Index = face.FileIndex, Weight = face.Weight, Italic = face.IsItalic,
                    WeightMin = face.HasWeightAxis ? (int)face.WeightAxisMin : 0, WeightMax = face.HasWeightAxis ? (int)face.WeightAxisMax : 0,
                });
            }
            return cuts.Count > 0 ? FromCuts(cuts, plan, source, written, null) : null;
        }

        static Candidate FromFallback(Plan plan)
        {
            LetteraSettings settings = LetteraSettings.Instance;
            if (settings.DefaultFont != null)
            {
                FontFace face = settings.DefaultFont.LoadFace();
                if (face != null)
                    return new Candidate { Asset = settings.DefaultFont, Source = LetteraFontMatch.Fallback, Family = "", FontFamily = face.Family, CutWeight = face.Weight, CutItalic = face.IsItalic };
            }
            if (LetteraSystemFonts.IsSupported)
            {
                Candidate system = FromDefault(plan, LetteraFontMatch.Fallback, "");
                if (system != null) return system;
            }
            LetteraFontAsset web = settings.WebGLFallbackFont != null ? settings.WebGLFallbackFont : Resources.Load<LetteraFont>(FontChain.WebFontResource);
            FontFace webFace = web != null ? web.LoadFace() : null;
            if (webFace == null) return null;
            return new Candidate { Asset = web, Source = LetteraFontMatch.Fallback, Family = "", FontFamily = webFace.Family, CutWeight = webFace.Weight, CutItalic = webFace.IsItalic };
        }

        // ---- generic families ----

        // The keyword in its one spelling, or null for a name that is no generic family.
        static string GenericKey(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            switch (name.Trim().ToLowerInvariant())
            {
                case "sans-serif":
                case "system-ui":
                case "ui-sans-serif":
                case "ui-rounded":
                case "-apple-system":
                case "blinkmacsystemfont": return "sans-serif";
                case "serif":
                case "ui-serif":
                case "math":
                case "fangsong": return "serif";
                case "monospace":
                case "ui-monospace": return "monospace";
                case "cursive": return "cursive";
                case "fantasy": return "fantasy";
                default: return null;
            }
        }

        static string[] GenericList(string key)
        {
            if (s_GenericOverrides.TryGetValue(key, out string[] custom)) return custom;
            switch (LetteraSystemFonts.Host())
            {
                case LetteraSystemFonts.Os.Windows:
                    switch (key)
                    {
                        case "serif": return new[] { "Times New Roman", "Georgia", "Cambria", "Constantia" };
                        case "monospace": return new[] { "Consolas", "Courier New", "Lucida Console" };
                        case "cursive": return new[] { "Segoe Script", "Comic Sans MS", "Ink Free" };
                        case "fantasy": return new[] { "Impact", "Segoe Print", "Gabriola" };
                        default: return new[] { "Segoe UI", "Arial", "Tahoma", "Verdana", "Calibri" };
                    }
                case LetteraSystemFonts.Os.Apple:
                    switch (key)
                    {
                        case "serif": return new[] { "Times", "Times New Roman", "Georgia", "Palatino" };
                        case "monospace": return new[] { "Menlo", "Monaco", "Courier New", "Courier" };
                        case "cursive": return new[] { "Apple Chancery", "Snell Roundhand", "Zapfino" };
                        case "fantasy": return new[] { "Papyrus", "Impact" };
                        default: return new[] { "SF Pro", "Helvetica Neue", "Helvetica", "Arial", "Lucida Grande" };
                    }
                case LetteraSystemFonts.Os.Mobile:
                    switch (key)
                    {
                        case "serif": return new[] { "Times New Roman", "Georgia" };
                        case "monospace": return new[] { "Menlo", "Courier New", "Courier" };
                        case "cursive": return new[] { "Snell Roundhand", "Marker Felt" };
                        case "fantasy": return new[] { "Papyrus", "Marker Felt" };
                        default: return new[] { "SF Pro", "Helvetica Neue", "Helvetica", "Arial" };
                    }
                case LetteraSystemFonts.Os.Android:
                    switch (key)
                    {
                        case "serif": return new[] { "serif", "Noto Serif", "Droid Serif" };
                        case "monospace": return new[] { "monospace", "Droid Sans Mono", "Cutive Mono", "Roboto Mono" };
                        case "cursive": return new[] { "cursive", "Dancing Script" };
                        case "fantasy": return new[] { "casual", "Coming Soon" };
                        default: return new[] { "sans-serif", "Roboto", "Noto Sans", "Droid Sans" };
                    }
                case LetteraSystemFonts.Os.Linux:
                    switch (key)
                    {
                        case "serif": return new[] { "Noto Serif", "DejaVu Serif", "Liberation Serif", "Tinos", "FreeSerif" };
                        case "monospace": return new[] { "Noto Sans Mono", "DejaVu Sans Mono", "Liberation Mono", "Ubuntu Mono", "Cousine", "FreeMono" };
                        case "cursive": return new[] { "Comic Neue", "Z003", "URW Chancery L" };
                        case "fantasy": return new[] { "Impact", "Purisa" };
                        default: return new[] { "Noto Sans", "DejaVu Sans", "Liberation Sans", "Ubuntu", "Arimo", "Cantarell", "FreeSans" };
                    }
                default:
                    return Array.Empty<string>();
            }
        }
    }
}

using System;
using System.Collections.Generic;
using FirstSymbol.Lettera.Unicode;
using UnityEngine;

namespace FirstSymbol.Lettera
{
    /// <summary>
    /// The ordered faces one text can use, and the rules choosing among them per cluster: language
    /// preference, requested family, the fallback chain, the OS cascade and finally .notdef.
    /// </summary>
    internal sealed class FontChain
    {
        internal sealed class Family
        {
            public string Name = "";
            public string Language = "";
            public LetteraFontAsset Asset;

            /// <summary>The CSS weight a text with no weight of its own draws this family at; 0 draws its faces as they are.</summary>
            public int Weight;

            /// <summary>What the system adds to every weight of its own faces, as a user's bold text setting does; 0 for none.</summary>
            public int Shift;

            public readonly List<FontFace> Faces = new List<FontFace>();
            public readonly List<LetteraFontAsset> FaceAssets = new List<LetteraFontAsset>();
        }

        internal const string WebFontResource = "Lettera/LetteraWebFont";

        /// <summary>Never reused, so what was shaped with a chain can be told from what another one would give.</summary>
        public int Id;

        /// <summary>What a text has until its first shaping.</summary>
        public static readonly FontChain Empty = new FontChain();

        readonly List<Family> m_Families = new List<Family>();
        readonly Dictionary<long, FontFace> m_Cache = new Dictionary<long, FontFace>();
        readonly HashSet<string> m_WarnedFamilies = new HashSet<string>();
        FontFace m_ColorFace;
        Family m_System;

        public Family Primary { get; private set; }
        public FontFace PrimaryFace => Primary != null && Primary.Faces.Count > 0 ? Primary.Faces[0] : null;
        public FontFace ColorFace => m_ColorFace;

        public void Build(LetteraFontAsset font, LetteraFontStack stack)
        {
            m_Families.Clear();
            m_Cache.Clear();
            m_Simple.Clear();
            m_ColorFace = null;
            Primary = null;
            m_System = null;

            if (font != null) AddAsset(font, null, "");
            var visited = new HashSet<LetteraFontStack>();
            for (LetteraFontStack s = stack; s != null && visited.Add(s); s = s.FallbackStack)
            {
                foreach (LetteraFontFamily family in s.Families)
                {
                    if (family == null || family.Primary == null) continue;
                    Family added = AddAsset(family.Primary, family.Name, family.PreferredLanguage);
                    if (added == null) continue;
                    foreach (LetteraFontAsset extra in family.Faces)
                    {
                        FontFace face = extra != null ? extra.LoadFace() : null;
                        if (face == null) continue;
                        if (face.IsColor) { m_ColorFace = m_ColorFace ?? face; continue; }
                        added.Faces.Add(face);
                        added.FaceAssets.Add(extra);
                    }
                }
            }

            LetteraSettings settings = LetteraSettings.Instance;
            if (m_Families.Count == 0 && settings.DefaultFont != null) AddAsset(settings.DefaultFont, null, "");
            if (m_Families.Count == 0)
            {
                FontFace[] defaults = LetteraSystemFonts.DefaultFaces();
                if (defaults.Length > 0)
                {
                    int shift = LetteraSystemFonts.WeightShift;
                    var family = new Family { Name = defaults[0].Family, Shift = shift, Weight = Shifted(0, shift) };
                    foreach (FontFace face in defaults)
                    {
                        family.Faces.Add(face);
                        family.FaceAssets.Add(null);
                    }
                    m_Families.Add(family);
                }
                else
                {
                    // Browsers expose no OS fonts, so the web gets a font of its own.
                    LetteraFontAsset web = settings.WebGLFallbackFont != null ? settings.WebGLFallbackFont : Resources.Load<LetteraFont>(WebFontResource);
                    if (web != null) AddAsset(web, null, "");
                }
            }
            Primary = m_Families.Count > 0 ? m_Families[0] : null;
            // Faces the system substitutes draw at the weight of the system font they stand in for, shifted as the system shifts its own.
            int systemShift = LetteraSystemFonts.WeightShift;
            m_SystemWeight = Primary != null && Primary.Asset is LetteraSystemFont ? Primary.Weight : Shifted(0, systemShift);
            m_SystemShift = systemShift;
        }

        int m_SystemWeight, m_SystemShift;

        /// <summary>A weight with the system's shift on it; 0 stays 0 when there is no shift.</summary>
        internal static int Shifted(int weight, int shift)
        {
            if (shift == 0) return weight;
            int shifted = (weight > 0 ? weight : 400) + shift;
            return shifted < 1 ? 1 : shifted > 1000 ? 1000 : shifted;
        }

        /// <summary>The weight a cluster of a family is drawn at: its own request, else the family's, shifted as the system asks for its faces.</summary>
        internal static int WeightFor(int requested, Family family)
        {
            if (family == null) return requested;
            if (requested <= 0) return family.Weight;
            return family.Shift == 0 ? requested : Shifted(requested, family.Shift);
        }

        Family AddAsset(LetteraFontAsset asset, string name, string language)
        {
            FontFace face = asset.LoadFace();
            if (face == null) return null;
            if (face.IsColor && asset is LetteraColorFont)
            {
                m_ColorFace = m_ColorFace ?? face;
                return null;
            }
            var family = new Family { Name = string.IsNullOrEmpty(name) ? face.Family : name, Language = language ?? "", Asset = asset };
            family.Faces.Add(face);
            family.FaceAssets.Add(asset);
            if (asset is LetteraSystemFont)
            {
                family.Shift = LetteraSystemFonts.WeightShift;
                family.Weight = Shifted(asset.DefaultWeight, family.Shift);
                int before = family.Faces.Count;
                asset.AddCuts(family.Faces);
                for (int i = before; i < family.Faces.Count; i++) family.FaceAssets.Add(asset);
            }
            m_Families.Add(family);
            return family;
        }

        Family FindFamily(string name)
        {
            foreach (Family family in m_Families)
                if (string.Equals(family.Name, name, StringComparison.OrdinalIgnoreCase))
                    return family;
            if (m_WarnedFamilies.Add(name)) Debug.LogWarning($"Lettera: this text has no font family named '{name}'.");
            return null;
        }

        /// <summary>The face for a cluster, or the primary face (its .notdef) when nothing covers it.</summary>
        public FontFace Resolve(ReadOnlySpan<int> cluster, bool emoji, string requestedFamily, string language, out Family family)
        {
            LetteraSettings settings = LetteraSettings.Instance;
            family = null;

            // Nearly every cluster is one codepoint with no special request, so that case is remembered.
            bool simple = cluster.Length == 1 && !emoji && string.IsNullOrEmpty(requestedFamily) && string.IsNullOrEmpty(language);
            if (simple && m_Simple.TryGetValue(cluster[0], out SimpleEntry known))
            {
                family = known.Family;
                return known.Face;
            }
            FontFace resolved = ResolveSlow(cluster, emoji, requestedFamily, language, settings, ref family);
            if (simple) m_Simple[cluster[0]] = new SimpleEntry { Face = resolved, Family = family };
            return resolved;
        }

        struct SimpleEntry
        {
            public FontFace Face;
            public Family Family;
        }

        readonly Dictionary<int, SimpleEntry> m_Simple = new Dictionary<int, SimpleEntry>();

        FontFace ResolveSlow(ReadOnlySpan<int> cluster, bool emoji, string requestedFamily, string language, LetteraSettings settings, ref Family family)
        {
            int baseCp = 0;
            foreach (int cp in cluster)
            {
                if (LetteraUnicode.IsDefaultIgnorable(cp) || LetteraUnicode.IsMark(cp)) continue;
                baseCp = cp;
                break;
            }
            if (baseCp == 0) baseCp = cluster.Length > 0 ? cluster[0] : ' ';

            // A line break draws nothing, and few fonts have a glyph for one. Asking the system for it woke the whole font search in the first text with a second line.
            if (baseCp == '\n' || baseCp == '\r' || baseCp == 0x0B || baseCp == 0x0C || baseCp == 0x85 || baseCp == 0x2028 || baseCp == 0x2029)
            {
                family = Primary;
                return PrimaryFace;
            }

            if (emoji && !settings.DisableEmoji)
            {
                if (m_ColorFace != null && m_ColorFace.HasGlyph(baseCp)) return m_ColorFace;
                FontFace system = LetteraSystemFonts.EmojiFace();
                if (system != null) return system;
            }

            Family requested = string.IsNullOrEmpty(requestedFamily) ? null : FindFamily(requestedFamily);

            // Language preference, then the requested family, then the chain in order.
            if (!string.IsNullOrEmpty(language))
            {
                foreach (Family f in m_Families)
                {
                    if (f.Language.Length == 0 || !LanguageMatches(language, f.Language)) continue;
                    if (Covers(f.Faces[0], cluster)) { family = f; return f.Faces[0]; }
                }
            }
            if (requested != null && Covers(requested.Faces[0], cluster)) { family = requested; return requested.Faces[0]; }
            foreach (Family f in m_Families)
            {
                if (!Covers(f.Faces[0], cluster)) continue;
                family = f;
                return f.Faces[0];
            }
            // Nothing covers all of it: the base decides and marks shape as well as they can.
            foreach (Family f in m_Families)
            {
                if (!f.Faces[0].HasGlyph(baseCp)) continue;
                family = f;
                return f.Faces[0];
            }

            if (!settings.DisableSystemFontFallback)
            {
                long key = ((long)(language?.GetHashCode() ?? 0) << 32) | (uint)baseCp;
                if (!m_Cache.TryGetValue(key, out FontFace system))
                {
                    system = LetteraSystemFonts.FindFallback(baseCp, language);
                    m_Cache[key] = system;
                }
                if (system != null)
                {
                    m_System = m_System ?? new Family { Weight = m_SystemWeight, Shift = m_SystemShift };
                    family = m_System;
                    return system;
                }
            }
            family = Primary;
            return PrimaryFace;
        }

        // zh matches zh-Hans; zh-Hans does not match zh.
        static bool LanguageMatches(string run, string preferred)
        {
            if (run.Length < preferred.Length) return false;
            if (string.Compare(run, 0, preferred, 0, preferred.Length, StringComparison.OrdinalIgnoreCase) != 0) return false;
            return run.Length == preferred.Length || run[preferred.Length] == '-';
        }

        static bool Covers(FontFace face, ReadOnlySpan<int> cluster)
        {
            foreach (int cp in cluster)
                if (!LetteraUnicode.IsDefaultIgnorable(cp) && !face.HasGlyph(cp))
                    return false;
            return true;
        }

        /// <summary>Picks a cut of the family for a weight and slope, using real faces first and synthesis otherwise.</summary>
        public static FontInstance Style(FontFace face, Family family, int weight, int boldMode, bool italic, float slantPercent, AxisSet axes = null)
        {
            FontFace chosen = face;
            LetteraFontAsset asset = null;
            if (family != null && family.Faces.Count > 0 && family.Faces[0] == face)
            {
                int best = BestFace(family, weight > 0 ? weight : 400, italic);
                if (boldMode != 1) chosen = family.Faces[best];
                asset = family.FaceAssets[boldMode != 1 ? best : 0];
            }

            int requested = weight > 0 ? weight : -1;
            float axis = -1;
            float embolden = 0;
            if (requested > 0)
            {
                if (boldMode != 1 && chosen.HasWeightAxis)
                {
                    axis = Mathf.Clamp(requested, chosen.WeightAxisMin, chosen.WeightAxisMax);
                }
                else if (boldMode != 2 && requested >= 600 && chosen.Weight < requested - 100 && !chosen.IsColor)
                {
                    float strength = asset != null ? asset.SyntheticBoldStrength : 1f;
                    embolden = strength * (requested - chosen.Weight) / 300f / 24f;
                }
            }

            float slant = 0;
            if (italic && !chosen.IsItalic && !chosen.IsColor)
                slant = (slantPercent >= 0 ? slantPercent : asset != null ? asset.SyntheticItalicSlant : 20f) / 100f;

            return chosen.GetInstance(axis, embolden, slant, axes);
        }

        // CSS Fonts nearest weight matching, restricted to the requested slope when possible.
        static int BestFace(Family family, int weight, bool italic)
        {
            int best = 0;
            int bestScore = int.MaxValue;
            for (int i = 0; i < family.Faces.Count; i++)
            {
                FontFace face = family.Faces[i];
                if (face.IsColor) continue;
                int w = face.HasWeightAxis ? Mathf.Clamp(weight, (int)face.WeightAxisMin, (int)face.WeightAxisMax) : face.Weight;
                int score = WeightPenalty(weight, w);
                if (face.IsItalic != italic) score += 10000;
                if (score < bestScore)
                {
                    bestScore = score;
                    best = i;
                }
            }
            return best;
        }

        internal static int WeightPenalty(int wanted, int actual)
        {
            if (actual == wanted) return 0;
            if (wanted >= 400 && wanted <= 500)
            {
                if (actual >= wanted && actual <= 500) return actual - wanted;
                if (actual < wanted) return 1000 + wanted - actual;
                return 2000 + actual - wanted;
            }
            if (wanted < 400) return actual < wanted ? wanted - actual : 1000 + actual - wanted;
            return actual > wanted ? actual - wanted : 1000 + wanted - actual;
        }
    }
}

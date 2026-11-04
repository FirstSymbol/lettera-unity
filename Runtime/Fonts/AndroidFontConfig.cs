using System;
using System.Collections.Generic;
using System.Text;

namespace FirstSymbol.Lettera
{
    /// <summary>
    /// Android's system font configuration, read the way the OS reads it: named families with their weights and aliases, then the
    /// fallback families in order, by language and by the compact or tall variant. Read without System.Xml, which a player does not carry.
    /// Covers fonts.xml, Android 15's font_fallback.xml, the old fallback_fonts.xml and a vendor's fonts_customization.xml.
    /// </summary>
    internal sealed class AndroidFontConfig
    {
        internal sealed class Font
        {
            public string File = "";
            public int Index;
            public int Weight = 400;
            public bool Italic;

            /// <summary>The named family this font stands in for, as a serif CJK face does for serif; null for every family.</summary>
            public string FallbackFor;
        }

        internal sealed class Family
        {
            public string Name;
            public string[] Languages = Array.Empty<string>();

            /// <summary>"compact", "elegant" or null for both.</summary>
            public string Variant;
            public readonly List<Font> Fonts = new List<Font>();
        }

        internal struct Alias
        {
            public string Name, To;
            public int Weight;
        }

        public readonly List<Family> Families = new List<Family>();
        public readonly List<Alias> Aliases = new List<Alias>();

        /// <summary>The configuration files a device may hold, in the order they are read; the first that exists of the first three is the base.</summary>
        internal static readonly string[] BaseFiles = { "/system/etc/font_fallback.xml", "/system/etc/fonts.xml", "/system/etc/fallback_fonts.xml" };
        internal const string CustomizationFile = "/product/etc/fonts_customization.xml";

        public static AndroidFontConfig Parse(string xml)
        {
            var config = new AndroidFontConfig();
            config.Read(xml, false);
            return config;
        }

        /// <summary>A vendor's additions: new named families, and families for a language put before or after the system's.</summary>
        public void Customize(string xml) => Read(xml, true);

        void Read(string xml, bool customizing)
        {
            var reader = new Reader(xml ?? "");
            Family family = null;
            Font font = null;
            string operation = null;
            var text = new StringBuilder();
            while (reader.Next(out Reader.Kind kind, out string name))
            {
                if (kind == Reader.Kind.Text)
                {
                    if (font != null) text.Append(name);
                    continue;
                }
                if (kind == Reader.Kind.Open || kind == Reader.Kind.Empty)
                {
                    switch (name)
                    {
                        case "family":
                            family = new Family
                            {
                                Name = Lower(reader.Attribute("name")),
                                Languages = Split(reader.Attribute("lang")),
                                Variant = Lower(reader.Attribute("variant")),
                            };
                            operation = customizing ? reader.Attribute("customizationType") + "|" + reader.Attribute("operation") : null;
                            if (kind == Reader.Kind.Empty) family = null;
                            break;
                        case "font":
                            font = new Font
                            {
                                Index = Number(reader.Attribute("index"), 0),
                                Weight = Number(reader.Attribute("weight"), 400),
                                Italic = string.Equals(reader.Attribute("style"), "italic", StringComparison.OrdinalIgnoreCase),
                                FallbackFor = Lower(reader.Attribute("fallbackFor")),
                            };
                            text.Clear();
                            // The old format names the file in an attribute of its own.
                            string file = reader.Attribute("file");
                            if (file != null) text.Append(file);
                            if (kind == Reader.Kind.Empty) Close(ref font, family, text);
                            break;
                        case "file":
                            // fallback_fonts.xml: <family><fileset><file lang="ja">MTLmr3m.ttf</file></fileset></family>.
                            font = new Font();
                            text.Clear();
                            string language = reader.Attribute("lang");
                            if (family != null && language != null && family.Languages.Length == 0) family.Languages = Split(language);
                            break;
                        case "axis":
                            // A variable font's point of design space: a weight given here is the one drawn.
                            if (font != null && reader.Attribute("tag") == "wght") font.Weight = Number(reader.Attribute("stylevalue"), font.Weight);
                            break;
                        case "alias":
                            string aliasName = Lower(reader.Attribute("name")), to = Lower(reader.Attribute("to"));
                            if (aliasName != null && to != null)
                            {
                                Aliases.RemoveAll(a => a.Name == aliasName);
                                Aliases.Add(new Alias { Name = aliasName, To = to, Weight = Number(reader.Attribute("weight"), 0) });
                            }
                            break;
                    }
                    continue;
                }
                switch (name)
                {
                    case "font":
                    case "file":
                        Close(ref font, family, text);
                        break;
                    case "family":
                        if (family != null) Add(family, operation);
                        family = null;
                        break;
                }
            }
        }

        static void Close(ref Font font, Family family, StringBuilder text)
        {
            if (font == null) return;
            font.File = text.ToString().Trim();
            if (family != null && font.File.Length > 0) family.Fonts.Add(font);
            font = null;
        }

        void Add(Family family, string operation)
        {
            if (family.Fonts.Count == 0) return;
            if (family.Name != null)
            {
                // A vendor's named family replaces one of the same name.
                int known = Families.FindIndex(f => f.Name == family.Name);
                if (known >= 0) Families[known] = family;
                else Families.Insert(NamedEnd(), family);
                return;
            }
            if (operation != null && operation.StartsWith("new-locale-family", StringComparison.Ordinal) && operation.EndsWith("|prepend", StringComparison.Ordinal))
            {
                Families.Insert(NamedEnd(), family);
                return;
            }
            Families.Add(family);
        }

        // Named families come first, so the fallbacks after them keep their order.
        int NamedEnd()
        {
            int end = 0;
            while (end < Families.Count && Families[end].Name != null) end++;
            return end;
        }

        /// <summary>The family a name means, through aliases, with the weight an alias asks for or 0. Roboto, Noto Sans and Droid Sans mean the sans-serif role.</summary>
        public Family Named(string name, out int weight)
        {
            weight = 0;
            string key = Lower(name);
            if (key == null) return null;
            for (int hops = 0; hops < 8; hops++)
            {
                foreach (Family family in Families)
                    if (family.Name == key)
                        return family;
                bool followed = false;
                foreach (Alias alias in Aliases)
                {
                    if (alias.Name != key) continue;
                    if (alias.Weight > 0 && weight == 0) weight = alias.Weight;
                    key = alias.To;
                    followed = true;
                    break;
                }
                if (followed) continue;
                if (key == "roboto" || key == "noto sans" || key == "notosans" || key == "droid sans" || key == "droidsans")
                {
                    key = "sans-serif";
                    continue;
                }
                return null;
            }
            return null;
        }

        /// <summary>The font of a family nearest a weight and slope, by the CSS rules; null for an empty family.</summary>
        public static Font Pick(Family family, int weight, bool italic, string fallbackFor = null)
        {
            Font best = null;
            int bestScore = int.MaxValue;
            foreach (Font font in family.Fonts)
            {
                if (font.FallbackFor != null && font.FallbackFor != fallbackFor) continue;
                int score = Penalty(weight > 0 ? weight : 400, font.Weight) + (font.Italic != italic ? 10000 : 0);
                if (score >= bestScore) continue;
                bestScore = score;
                best = font;
            }
            return best;
        }

        static int Penalty(int wanted, int actual)
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

        /// <summary>
        /// The fallback families in the order the OS tries them for text in the device's languages: the file's order, with families of
        /// a language the device reads moved ahead by how early it lists that language, so Han takes the Japanese face on a Japanese device
        /// and the Simplified Chinese one in China. Only one of a script's compact and tall variants is kept.
        /// </summary>
        public List<Family> Fallbacks(IList<string> deviceLanguages, bool tall)
        {
            var kept = new List<Family>();
            foreach (Family family in Families)
            {
                if (family.Name != null) continue;
                if (family.Variant == "elegant" && !tall) continue;
                if (family.Variant == "compact" && tall) continue;
                kept.Add(family);
            }
            var ranks = new int[kept.Count];
            for (int i = 0; i < kept.Count; i++) ranks[i] = Rank(kept[i], deviceLanguages);
            // A stable sort: equal ranks keep the file's order.
            var order = new List<int>(kept.Count);
            for (int i = 0; i < kept.Count; i++) order.Add(i);
            order.Sort((a, b) => ranks[a] != ranks[b] ? ranks[a].CompareTo(ranks[b]) : a.CompareTo(b));
            var sorted = new List<Family>(kept.Count);
            foreach (int i in order) sorted.Add(kept[i]);
            return sorted;
        }

        /// <summary>The families for a text's own language, best first.</summary>
        public List<Family> ForLanguage(string language)
        {
            var found = new List<Family>();
            if (string.IsNullOrEmpty(language)) return found;
            var one = new[] { language };
            foreach (Family family in Families)
                if (family.Name == null && Rank(family, one) == 0)
                    found.Add(family);
            return found;
        }

        // Families named for no language ("und-Arab") keep their place; one whose language the device lists ranks by that position.
        static int Rank(Family family, IList<string> deviceLanguages)
        {
            if (deviceLanguages == null) return int.MaxValue;
            for (int d = 0; d < deviceLanguages.Count; d++)
            {
                Locale device = Locale.Of(deviceLanguages[d]);
                if (device.Language == null) continue;
                foreach (string tag in family.Languages)
                {
                    Locale own = Locale.Of(tag);
                    if (own.Language == null || own.Language == "und") continue;
                    if (own.Language != device.Language) continue;
                    if (own.Script != null && device.Script != null && own.Script != device.Script) continue;
                    return d;
                }
            }
            return int.MaxValue;
        }

        /// <summary>A language tag's language and script, the script found from the region where Chinese leaves it out.</summary>
        internal struct Locale
        {
            public string Language, Script;

            public static Locale Of(string tag)
            {
                var locale = new Locale();
                if (string.IsNullOrEmpty(tag)) return locale;
                string[] parts = tag.Replace('_', '-').Split('-');
                locale.Language = parts[0].ToLowerInvariant();
                if (locale.Language == "iw") locale.Language = "he";
                string region = null;
                for (int i = 1; i < parts.Length; i++)
                {
                    if (parts[i].Length == 4) locale.Script = char.ToUpperInvariant(parts[i][0]) + parts[i].Substring(1).ToLowerInvariant();
                    else if (parts[i].Length == 2 || parts[i].Length == 3) region = region ?? parts[i].ToUpperInvariant();
                }
                if (locale.Language == "zh" && locale.Script == null)
                    locale.Script = region == "TW" || region == "HK" || region == "MO" ? "Hant" : "Hans";
                if (locale.Language == "zh" && locale.Script == "Bopo") locale.Script = "Hant";
                return locale;
            }
        }

        /// <summary>Whether this device draws the tall variants: Android 15 does for an app made for it.</summary>
        public static bool UsesTallVariants(int sdk, int targetSdk) => sdk >= 35 && targetSdk >= 35;

        static string Lower(string value) => string.IsNullOrEmpty(value) ? null : value.Trim().ToLowerInvariant();

        static string[] Split(string value) => string.IsNullOrWhiteSpace(value) ? Array.Empty<string>() : value.Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries);

        static int Number(string value, int fallback)
        {
            if (string.IsNullOrEmpty(value)) return fallback;
            return float.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float number) ? (int)Math.Round(number) : fallback;
        }

        /// <summary>Tags, their attributes and the text between them; comments, declarations and CDATA are passed over.</summary>
        sealed class Reader
        {
            public enum Kind { Open, Close, Empty, Text }

            readonly string m_Xml;
            int m_At;
            readonly Dictionary<string, string> m_Attributes = new Dictionary<string, string>();

            public Reader(string xml) => m_Xml = xml;

            public string Attribute(string name) => m_Attributes.TryGetValue(name, out string value) ? value : null;

            public bool Next(out Kind kind, out string name)
            {
                kind = Kind.Text;
                name = null;
                m_Attributes.Clear();
                while (m_At < m_Xml.Length)
                {
                    if (m_Xml[m_At] != '<')
                    {
                        int end = m_Xml.IndexOf('<', m_At);
                        if (end < 0) end = m_Xml.Length;
                        name = Decode(m_Xml.Substring(m_At, end - m_At));
                        m_At = end;
                        return true;
                    }
                    if (Skip("<!--", "-->") || Skip("<?", "?>") || Skip("<![CDATA[", "]]>") || Skip("<!", ">")) continue;
                    int close = m_Xml.IndexOf('>', m_At);
                    if (close < 0)
                    {
                        m_At = m_Xml.Length;
                        return false;
                    }
                    string inside = m_Xml.Substring(m_At + 1, close - m_At - 1);
                    m_At = close + 1;
                    if (inside.StartsWith("/", StringComparison.Ordinal))
                    {
                        kind = Kind.Close;
                        name = inside.Substring(1).Trim();
                        return true;
                    }
                    kind = Kind.Open;
                    if (inside.EndsWith("/", StringComparison.Ordinal))
                    {
                        kind = Kind.Empty;
                        inside = inside.Substring(0, inside.Length - 1);
                    }
                    int space = 0;
                    while (space < inside.Length && !char.IsWhiteSpace(inside[space])) space++;
                    name = inside.Substring(0, space);
                    ReadAttributes(inside, space);
                    return true;
                }
                return false;
            }

            bool Skip(string start, string end)
            {
                if (string.CompareOrdinal(m_Xml, m_At, start, 0, start.Length) != 0) return false;
                int found = m_Xml.IndexOf(end, m_At + start.Length, StringComparison.Ordinal);
                m_At = found < 0 ? m_Xml.Length : found + end.Length;
                return true;
            }

            void ReadAttributes(string inside, int at)
            {
                while (at < inside.Length)
                {
                    while (at < inside.Length && char.IsWhiteSpace(inside[at])) at++;
                    int equals = inside.IndexOf('=', at);
                    if (equals < 0) return;
                    string key = inside.Substring(at, equals - at).Trim();
                    at = equals + 1;
                    while (at < inside.Length && char.IsWhiteSpace(inside[at])) at++;
                    if (at >= inside.Length) return;
                    char quote = inside[at];
                    if (quote != '"' && quote != '\'') return;
                    int end = inside.IndexOf(quote, at + 1);
                    if (end < 0) return;
                    m_Attributes[key] = Decode(inside.Substring(at + 1, end - at - 1));
                    at = end + 1;
                }
            }

            static string Decode(string text) => text.IndexOf('&') < 0 ? text :
                text.Replace("&lt;", "<").Replace("&gt;", ">").Replace("&quot;", "\"").Replace("&apos;", "'").Replace("&amp;", "&");
        }
    }
}

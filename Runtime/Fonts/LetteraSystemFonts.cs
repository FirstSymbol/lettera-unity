using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace FirstSymbol.Lettera
{
    /// <summary>
    /// Finds fonts the operating system ships. Files are discovered once and opened (mapped) only when a
    /// codepoint needs them. Uses Application.platform, so an editor targeting another platform still
    /// resolves the host machine's fonts.
    /// </summary>
    public static class LetteraSystemFonts
    {
        enum Os { None, Windows, Apple, Mobile, Android, Linux }

        sealed class Entry
        {
            public string Path;
            public int Index;
            public bool Tried;
            public FontFace Face;

            public FontFace Load()
            {
                if (Tried) return Face;
                Tried = true;
                try { Face = FontFace.FromFile(Path, Index); }
                catch (Exception e) { Debug.LogWarning($"Lettera: cannot open system font {Path}: {e.Message}"); }
                return Face;
            }
        }

        static List<Entry> s_Entries;
        static readonly Dictionary<string, Entry> s_ByKey = new Dictionary<string, Entry>();
        static readonly Dictionary<long, FontFace> s_Fallback = new Dictionary<long, FontFace>();
        static readonly Dictionary<string, FontFace> s_Named = new Dictionary<string, FontFace>();
        static FontFace[] s_DefaultFaces;
        static FontFace s_Emoji;
        static bool s_EmojiSearched;

        /// <summary>True when this platform exposes OS font files at all (WebGL does not).</summary>
        public static bool IsSupported => Host() != Os.None;

        static Os Host()
        {
            switch (Application.platform)
            {
                case RuntimePlatform.WindowsEditor:
                case RuntimePlatform.WindowsPlayer:
                case RuntimePlatform.WSAPlayerX64:
                case RuntimePlatform.WSAPlayerARM: return Os.Windows;
                case RuntimePlatform.OSXEditor:
                case RuntimePlatform.OSXPlayer: return Os.Apple;
                case RuntimePlatform.IPhonePlayer:
                case RuntimePlatform.tvOS:
                case RuntimePlatform.VisionOS: return Os.Mobile;
                case RuntimePlatform.Android: return Os.Android;
                case RuntimePlatform.LinuxEditor:
                case RuntimePlatform.LinuxPlayer: return Os.Linux;
                default: return Os.None;
            }
        }

        static string[] Directories(Os os)
        {
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            switch (os)
            {
                case Os.Windows:
                    return new[]
                    {
                        Path.Combine(Environment.GetEnvironmentVariable("WINDIR") ?? @"C:\Windows", "Fonts"),
                        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Microsoft\Windows\Fonts"),
                    };
                case Os.Apple:
                    return new[] { "/System/Library/Fonts", "/Library/Fonts", Path.Combine(home, "Library/Fonts") };
                case Os.Mobile:
                    return new[] { "/System/Library/Fonts" };
                case Os.Android:
                    return new[] { "/system/fonts", "/product/fonts", "/system/font", "/data/fonts" };
                case Os.Linux:
                    return new[] { "/usr/share/fonts", "/usr/local/share/fonts", Path.Combine(home, ".fonts"), Path.Combine(home, ".local/share/fonts") };
                default:
                    return Array.Empty<string>();
            }
        }

        // File name stems tried first, in order, before any other file in the font folders.
        static string[] FallbackPriority(Os os)
        {
            switch (os)
            {
                case Os.Windows:
                    return new[]
                    {
                        "segoeui", "arial", "tahoma", "nirmala", "ebrima", "leelawui", "gadugi", "javatext", "himalaya", "mmrtext",
                        "msyh", "yugothm", "malgun", "msjh", "seguisym", "seguihis", "taile", "phagspa", "mvboli", "sylfaen", "times",
                    };
                case Os.Apple:
                case Os.Mobile:
                    return new[]
                    {
                        "SFNS", "SFUI", "Helvetica", "GeezaPro", "ArialHB", "Kohinoor", "Thonburi", "PingFang",
                        "Hiragino", "AppleSDGothicNeo", "AppleSymbols", "Apple Symbols", "LastResort",
                    };
                case Os.Android:
                    return AndroidOrder();
                default:
                    return new[]
                    {
                        "NotoSans-Regular", "DejaVuSans", "LiberationSans-Regular", "Ubuntu-R", "NotoSansArabic", "NotoSansHebrew",
                        "NotoSansDevanagari", "NotoSansThai", "NotoSansCJK", "NotoSerifCJK", "WenQuanYi", "wqy",
                    };
            }
        }

        // The device's fallback order, read from its font configuration without an XML dependency.
        static string[] AndroidOrder()
        {
            var order = new List<string> { "Roboto" };
            foreach (string config in new[] { "/product/etc/fonts_customization.xml", "/system/etc/fonts.xml", "/system/etc/fallback_fonts.xml" })
            {
                try
                {
                    if (!File.Exists(config)) continue;
                    string xml = File.ReadAllText(config);
                    int close = 0;
                    while ((close = xml.IndexOf("</font>", close, StringComparison.Ordinal)) >= 0)
                    {
                        int open = xml.LastIndexOf('>', close);
                        if (open >= 0)
                        {
                            string stem = Path.GetFileNameWithoutExtension(xml.Substring(open + 1, close - open - 1).Trim());
                            if (stem.Length > 0 && !order.Contains(stem)) order.Add(stem);
                        }
                        close += 7;
                    }
                }
                catch (Exception) { } // Vendor configs differ; a missing one only changes the order.
            }
            return order.ToArray();
        }

        // What the font folders hold, found once: the files in name order, each one's stem, the first file of each stem, and the order fallbacks try them in.
        static List<string> s_Files;
        static string[] s_Stems;
        static Dictionary<string, string> s_ByStem;
        static List<string> s_Order;
        static readonly object s_Discovery = new object();

        static List<string> AllFiles()
        {
            lock (s_Discovery)
            {
                if (s_Files == null) Discover(Host());
                return s_Files;
            }
        }

        /// <summary>
        /// What finds the system fonts, for a player's start-up thread: none of it touches Unity, and the first text that needs a fallback
        /// then only opens a file. Null where nothing would be found or fallbacks are off. The main thread waits for it only if it asks first.
        /// </summary>
        internal static Action DiscoveryAside()
        {
            Os os = Host();
            if (os == Os.None || LetteraSettings.Instance.DisableSystemFontFallback) return null;
            return () =>
            {
                lock (s_Discovery)
                    if (s_Files == null)
                        Discover(os);
            };
        }

        static void Discover(Os os)
        {
            var files = new List<string>();
            foreach (string directory in Directories(os))
            {
                try
                {
                    if (!Directory.Exists(directory)) continue;
                    foreach (string file in Directory.GetFiles(directory, "*", SearchOption.AllDirectories))
                        if (file.EndsWith(".ttf", StringComparison.OrdinalIgnoreCase) || file.EndsWith(".otf", StringComparison.OrdinalIgnoreCase) ||
                            file.EndsWith(".ttc", StringComparison.OrdinalIgnoreCase) || file.EndsWith(".otc", StringComparison.OrdinalIgnoreCase))
                            files.Add(file);
                }
                catch (Exception) { } // A sandbox may hide a folder; the rest still count.
            }
            files.Sort(StringComparer.OrdinalIgnoreCase);

            var stems = new string[files.Count];
            var byStem = new Dictionary<string, string>(files.Count, StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < files.Count; i++)
            {
                stems[i] = Path.GetFileNameWithoutExtension(files[i]);
                if (!byStem.ContainsKey(stems[i])) byStem[stems[i]] = files[i];
            }

            // Files whose stem begins with a priority stem come first, by the first such stem, in name order within it; then all the others.
            string[] priority = FallbackPriority(os);
            var first = new int[files.Count];
            var counts = new int[priority.Length + 1];
            for (int i = 0; i < files.Count; i++)
            {
                int p = 0;
                while (p < priority.Length && !stems[i].StartsWith(priority[p], StringComparison.OrdinalIgnoreCase)) p++;
                first[i] = p;
                counts[p]++;
            }
            var order = new List<string>(files.Count);
            for (int p = 0; p <= priority.Length; p++)
            {
                if (counts[p] == 0) continue;
                for (int i = 0; i < files.Count; i++)
                {
                    if (first[i] != p) continue;
                    string name = Path.GetFileName(files[i]);
                    // Bold and italic cuts are never better fallbacks than the regular cut.
                    if (name.IndexOf("Bold", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        name.IndexOf("Italic", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        name.IndexOf("Oblique", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                    order.Add(files[i]);
                }
            }

            s_Stems = stems;
            s_ByStem = byStem;
            s_Order = order;
            s_Files = files;
        }

        static void EnsureEntries()
        {
            if (s_Entries != null) return;
            AllFiles();
            var entries = new List<Entry>(s_Order.Count);
            foreach (string file in s_Order) entries.Add(GetEntry(file, 0));
            s_Entries = entries;
        }

        static Entry GetEntry(string path, int index)
        {
            string key = path + "|" + index;
            if (!s_ByKey.TryGetValue(key, out var entry))
            {
                entry = new Entry { Path = path, Index = index };
                s_ByKey[key] = entry;
            }
            return entry;
        }

        // Stems of the default UI family: regular first, then its other cuts.
        static string[] DefaultStems()
        {
            switch (Host())
            {
                case Os.Windows:
                    return new[] { "segoeui", "segoeuib", "segoeuii", "segoeuiz", "arial", "arialbd", "ariali", "arialbi" };
                case Os.Apple:
                    return new[] { "SFNS", "SFNSItalic", "Helvetica", "HelveticaNeue" };
                case Os.Mobile:
                    return new[] { "SFUI", "SFUIItalic", "SFUIText", "Helvetica" };
                case Os.Android:
                    return new[] { "Roboto-Regular", "Roboto-Bold", "Roboto-Italic", "Roboto-BoldItalic", "Roboto", "NotoSans-Regular", "DroidSans" };
                default:
                    return new[] { "NotoSans-Regular", "NotoSans-Bold", "NotoSans-Italic", "NotoSans-BoldItalic", "DejaVuSans", "DejaVuSans-Bold", "LiberationSans-Regular", "LiberationSans-Bold", "Ubuntu-R", "Ubuntu-B" };
            }
        }

        static string FindFile(string stem)
        {
            AllFiles();
            return s_ByStem.TryGetValue(stem, out string file) ? file : null;
        }

        /// <summary>The OS default UI family; the first face is the regular cut. Empty when there are no OS fonts.</summary>
        internal static FontFace[] DefaultFaces()
        {
            if (s_DefaultFaces != null) return s_DefaultFaces;
            var faces = new List<FontFace>();
#if (UNITY_IOS || UNITY_TVOS || UNITY_VISIONOS) && !UNITY_EDITOR
            FontFace system = FaceFromCoreText(IosFonts.DefaultPath(), 'a');
            if (system != null) faces.Add(system);
#endif
            foreach (string stem in DefaultStems())
            {
                string file = FindFile(stem);
                FontFace face = file == null ? null : GetEntry(file, 0).Load();
                if (face != null && !face.IsColor) faces.Add(face);
            }
            if (faces.Count == 0 && IsSupported)
            {
                EnsureEntries();
                foreach (Entry entry in s_Entries)
                {
                    FontFace face = entry.Load();
                    if (face != null && !face.IsColor && face.HasGlyph('a')) { faces.Add(face); break; }
                }
            }
            if (faces.Count > 1)
            {
                string family = faces[0].Family;
                faces.RemoveAll(f => !string.Equals(f.Family, family, StringComparison.OrdinalIgnoreCase));
            }
            s_DefaultFaces = faces.ToArray();
            return s_DefaultFaces;
        }

        /// <summary>The OS color emoji font, or null.</summary>
        internal static FontFace EmojiFace()
        {
            if (!IsSupported) return BrowserEmoji.Face;
            if (s_EmojiSearched) return s_Emoji;
            s_EmojiSearched = true;
#if (UNITY_IOS || UNITY_TVOS || UNITY_VISIONOS) && !UNITY_EDITOR
            s_Emoji = FaceFromCoreText(IosFonts.NamedPath("AppleColorEmoji"), 0x1F600);
            if (s_Emoji != null) return s_Emoji;
#endif
            foreach (string stem in new[] { "seguiemj", "Apple Color Emoji", "AppleColorEmoji", "AppleColorEmoji@2x", "NotoColorEmoji", "NotoColorEmoji-Regular", "SamsungColorEmoji", "TwemojiMozilla" })
            {
                string file = FindFile(stem);
                FontFace face = file == null ? null : GetEntry(file, 0).Load();
                if (face != null && face.IsColor) { s_Emoji = face; return face; }
            }
            foreach (string file in AllFiles())
            {
                if (file.IndexOf("emoji", StringComparison.OrdinalIgnoreCase) < 0) continue;
                FontFace face = GetEntry(file, 0).Load();
                if (face != null && face.IsColor) { s_Emoji = face; return face; }
            }
            return null;
        }

        /// <summary>A system face by file stem or family name, or null.</summary>
        internal static FontFace FindNamed(string name)
        {
            if (string.IsNullOrEmpty(name) || !IsSupported) return null;
            if (s_Named.TryGetValue(name, out var cached)) return cached;
            string compact = name.Replace(" ", "");
            FontFace found = null;
            string file = FindFile(name) ?? FindFile(compact);
            if (file != null) found = GetEntry(file, 0).Load();
            if (found == null)
            {
                List<string> files = AllFiles();
                for (int i = 0; i < files.Count; i++)
                {
                    if (s_Stems[i].IndexOf(compact, StringComparison.OrdinalIgnoreCase) < 0) continue;
                    found = GetEntry(files[i], 0).Load();
                    if (found != null) break;
                }
            }
            s_Named[name] = found;
            return found;
        }

        // CJK families share codepoints, so the language picks the regional file or collection face.
        static FontFace FindCjkForLanguage(int codepoint, string language)
        {
            if (string.IsNullOrEmpty(language)) return null;
            string[] stems;
            int collectionIndex;
            string lang = language.ToLowerInvariant();
            if (lang.StartsWith("ja")) { stems = new[] { "YuGothM", "meiryo", "msgothic", "HiraginoSans-W3", "NotoSansCJK-Regular", "NotoSansCJKjp-Regular" }; collectionIndex = 0; }
            else if (lang.StartsWith("ko")) { stems = new[] { "malgun", "AppleSDGothicNeo", "NotoSansCJK-Regular", "NotoSansCJKkr-Regular" }; collectionIndex = 1; }
            else if (lang.StartsWith("zh-hant") || lang.StartsWith("zh-tw") || lang.StartsWith("zh-hk")) { stems = new[] { "msjh", "PingFang", "NotoSansCJK-Regular", "NotoSansCJKtc-Regular" }; collectionIndex = 3; }
            else if (lang.StartsWith("zh")) { stems = new[] { "msyh", "PingFang", "NotoSansCJK-Regular", "NotoSansCJKsc-Regular" }; collectionIndex = 2; }
            else return null;
            foreach (string stem in stems)
            {
                string file = FindFile(stem);
                if (file == null) continue;
                bool regional = stem == "NotoSansCJK-Regular" && file.EndsWith(".ttc", StringComparison.OrdinalIgnoreCase);
                FontFace face = GetEntry(file, regional ? collectionIndex : 0).Load();
                if (face != null && face.HasGlyph(codepoint)) return face;
            }
            return null;
        }

        /// <summary>The first OS font that covers a codepoint, or null. Results are cached.</summary>
        internal static FontFace FindFallback(int codepoint, string language)
        {
            if (!IsSupported) return null;
            long key = ((long)(language?.GetHashCode() ?? 0) << 32) | (uint)codepoint;
            if (s_Fallback.TryGetValue(key, out var cached)) return cached;

            FontFace found = null;
#if (UNITY_IOS || UNITY_TVOS || UNITY_VISIONOS) && !UNITY_EDITOR
            found = FaceFromCoreText(IosFonts.FallbackPath(codepoint, language), codepoint);
#endif
            found = found ?? FindCjkForLanguage(codepoint, language);
            if (found == null)
            {
                EnsureEntries();
                foreach (Entry entry in s_Entries)
                {
                    FontFace face = entry.Load();
                    if (face != null && !face.IsColor && face.HasGlyph(codepoint)) { found = face; break; }
                }
            }
            s_Fallback[key] = found;
            return found;
        }

#if (UNITY_IOS || UNITY_TVOS || UNITY_VISIONOS) && !UNITY_EDITOR
        // CoreText names a file; in a collection the face that covers the codepoint is the one wanted.
        static FontFace FaceFromCoreText(string path, int codepoint)
        {
            if (string.IsNullOrEmpty(path)) return null;
            for (int index = 0; index < 32; index++)
            {
                FontFace face = GetEntry(path, index).Load();
                if (face == null) return null;
                if (face.HasGlyph(codepoint)) return face;
            }
            return null;
        }
#endif

        /// <summary>Forgets loaded faces (after FontFace.DisposeAll).</summary>
        internal static void Reset()
        {
            s_Entries = null;
            lock (s_Discovery)
            {
                s_Files = null;
                s_Stems = null;
                s_ByStem = null;
                s_Order = null;
            }
            s_ByKey.Clear();
            s_Fallback.Clear();
            s_Named.Clear();
            s_DefaultFaces = null;
            s_Emoji = null;
            s_EmojiSearched = false;
            BrowserEmoji.Reset();
        }
    }
}

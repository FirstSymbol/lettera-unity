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
    public static partial class LetteraSystemFonts
    {
        internal enum Os { None, Windows, Apple, Mobile, Android, Linux }

        /// <summary>What a test makes the host look like; null reads the platform.</summary>
        internal static Os? HostForTests;

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

        static int s_WeightShift = int.MinValue;

        /// <summary>
        /// What the system adds to the weight of its own fonts: on Android 12 and later the user's bold text setting, 300 when it is on,
        /// read once at the first text. 0 elsewhere. System faces, a system font asset's faces and the faces the system falls back to
        /// draw shifted by it; a font of the project's own does not.
        /// </summary>
        public static int WeightShift
        {
            get
            {
                if (s_WeightShift == int.MinValue) s_WeightShift = ReadWeightShift();
                return s_WeightShift;
            }
            internal set => s_WeightShift = value;
        }

        static int ReadWeightShift()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using (var version = new AndroidJavaClass("android.os.Build$VERSION"))
                {
                    if (version.GetStatic<int>("SDK_INT") < 31) return 0;
                }
                using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (AndroidJavaObject activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
                using (AndroidJavaObject resources = activity.Call<AndroidJavaObject>("getResources"))
                using (AndroidJavaObject configuration = resources.Call<AndroidJavaObject>("getConfiguration"))
                {
                    int shift = configuration.Get<int>("fontWeightAdjustment");
                    // Configuration.FONT_WEIGHT_ADJUSTMENT_UNDEFINED.
                    return shift == int.MaxValue ? 0 : shift;
                }
            }
            catch (Exception)
            {
                return 0; // A device that will not say draws its fonts as they are.
            }
#else
            return 0;
#endif
        }

        // ---- Android: the system's own font configuration ----

        static AndroidFontConfig s_AndroidConfig;
        static bool s_AndroidRead;
        static string[] s_AndroidLanguages;
        static bool? s_AndroidTall;

        /// <summary>What a test puts in place of the device's configuration, languages and variant; null reads the device.</summary>
        internal static AndroidFontConfig AndroidConfigForTests;
        internal static string[] AndroidLanguagesForTests;
        internal static bool? AndroidTallForTests;

        static readonly string[] s_AndroidFontFolders = { "/system/fonts", "/product/fonts", "/system/font", "/data/fonts" };

        /// <summary>The device's font configuration with a vendor's changes on it, or null where none can be read.</summary>
        internal static AndroidFontConfig AndroidConfig()
        {
            if (AndroidConfigForTests != null) return AndroidConfigForTests;
            if (s_AndroidRead) return s_AndroidConfig;
            s_AndroidRead = true;
            try
            {
                foreach (string file in AndroidFontConfig.BaseFiles)
                {
                    if (!File.Exists(file)) continue;
                    s_AndroidConfig = AndroidFontConfig.Parse(File.ReadAllText(file));
                    if (s_AndroidConfig.Families.Count > 0) break;
                    s_AndroidConfig = null;
                }
                if (s_AndroidConfig != null && File.Exists(AndroidFontConfig.CustomizationFile)) s_AndroidConfig.Customize(File.ReadAllText(AndroidFontConfig.CustomizationFile));
            }
            catch (Exception)
            {
                s_AndroidConfig = null; // Unreadable: the file names decide, as before.
            }
            return s_AndroidConfig;
        }

        /// <summary>The device's languages, first first: Android's locale list.</summary>
        internal static string[] AndroidLanguages()
        {
            if (AndroidLanguagesForTests != null) return AndroidLanguagesForTests;
            if (s_AndroidLanguages != null) return s_AndroidLanguages;
            string tags = null;
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using (var list = new AndroidJavaClass("android.os.LocaleList"))
                using (AndroidJavaObject locales = list.CallStatic<AndroidJavaObject>("getDefault"))
                    tags = locales.Call<string>("toLanguageTags");
            }
            catch (Exception) { } // Before Android 7 there is one language, which the culture names.
#endif
            if (string.IsNullOrEmpty(tags)) tags = System.Globalization.CultureInfo.CurrentUICulture.Name;
            s_AndroidLanguages = tags.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
            return s_AndroidLanguages;
        }

        /// <summary>Whether the device draws the tall variants of scripts such as Arabic and Thai: Android 15, for an app made for it.</summary>
        internal static bool AndroidTall()
        {
            if (AndroidTallForTests.HasValue) return AndroidTallForTests.Value;
            if (s_AndroidTall.HasValue) return s_AndroidTall.Value;
            bool tall = false;
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                int sdk;
                using (var version = new AndroidJavaClass("android.os.Build$VERSION")) sdk = version.GetStatic<int>("SDK_INT");
                using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (AndroidJavaObject activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
                using (AndroidJavaObject info = activity.Call<AndroidJavaObject>("getApplicationInfo"))
                    tall = AndroidFontConfig.UsesTallVariants(sdk, info.Get<int>("targetSdkVersion"));
            }
            catch (Exception) { } // Compact, as before Android 15.
#endif
            s_AndroidTall = tall;
            return tall;
        }

        static string LocateAndroidFont(string file)
        {
            if (Path.IsPathRooted(file)) return File.Exists(file) ? file : null;
            foreach (string folder in s_AndroidFontFolders)
            {
                string path = Path.Combine(folder, file);
                if (File.Exists(path)) return path;
            }
            return null;
        }

        /// <summary>The fallback fonts in the order the device tries them, as files and faces of collections; a family whose file is missing is passed over.</summary>
        internal static List<(string path, int index)> AndroidFallbackFiles(AndroidFontConfig config, IList<string> languages, bool tall, Func<string, string> locate)
        {
            var files = new List<(string, int)>();
            foreach (AndroidFontConfig.Family family in config.Fallbacks(languages, tall))
            {
                AndroidFontConfig.Font font = AndroidFontConfig.Pick(family, 400, false);
                string path = font != null ? locate(font.File) : null;
                if (path != null && !files.Contains((path, font.Index))) files.Add((path, font.Index));
            }
            return files;
        }

        /// <summary>The faces of a named family, the regular one first, each file and face once.</summary>
        internal static List<(string path, int index)> AndroidFamilyFiles(AndroidFontConfig.Family family, Func<string, string> locate)
        {
            var files = new List<(string, int)>();
            if (family == null) return files;
            AndroidFontConfig.Font regular = AndroidFontConfig.Pick(family, 400, false);
            string first = regular != null ? locate(regular.File) : null;
            if (first != null) files.Add((first, regular.Index));
            foreach (AndroidFontConfig.Font font in family.Fonts)
            {
                if (font.FallbackFor != null) continue;
                string path = locate(font.File);
                if (path != null && !files.Contains((path, font.Index))) files.Add((path, font.Index));
            }
            return files;
        }

        static bool AndroidCutsOf(FontFace face, List<(string, int)> cuts)
        {
            AndroidFontConfig config = AndroidConfig();
            if (config == null) return false;
            string file = Path.GetFileName(face.FilePath);
            foreach (AndroidFontConfig.Family family in config.Families)
            {
                if (family.Name == null || !family.Fonts.Exists(f => string.Equals(f.File, file, StringComparison.OrdinalIgnoreCase) && f.Index == face.FileIndex)) continue;
                foreach ((string path, int index) in AndroidFamilyFiles(family, LocateAndroidFont))
                    if (!(string.Equals(path, face.FilePath, StringComparison.OrdinalIgnoreCase) && index == face.FileIndex))
                        cuts.Add((path, index));
                return true;
            }
            return false;
        }

        static FontFace AndroidNamed(string name)
        {
            AndroidFontConfig config = AndroidConfig();
            if (config == null) return null;
            AndroidFontConfig.Family family = config.Named(name, out int weight);
            if (family == null) return null;
            AndroidFontConfig.Font font = AndroidFontConfig.Pick(family, weight, false);
            string path = font != null ? LocateAndroidFont(font.File) : null;
            return path != null ? GetEntry(path, font.Index).Load() : null;
        }

        /// <summary>True when this platform exposes OS font files at all (WebGL does not).</summary>
        public static bool IsSupported => Host() != Os.None;

        internal static Os Host()
        {
            if (HostForTests.HasValue) return HostForTests.Value;
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
            // On Android the system's own order comes first, with the face of a collection each language takes; then every other file.
            AndroidFontConfig config = Host() == Os.Android ? AndroidConfig() : null;
            if (config != null)
                foreach ((string path, int index) in AndroidFallbackFiles(config, AndroidLanguages(), AndroidTall(), LocateAndroidFont))
                    entries.Add(GetEntry(path, index));
            var listed = new HashSet<Entry>(entries);
            foreach (string file in s_Order)
            {
                Entry entry = GetEntry(file, 0);
                if (listed.Add(entry)) entries.Add(entry);
            }
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
            AndroidFontConfig config = Host() == Os.Android ? AndroidConfig() : null;
            if (config != null)
            {
                foreach ((string path, int index) in AndroidFamilyFiles(config.Named("sans-serif", out _), LocateAndroidFont))
                {
                    FontFace face = GetEntry(path, index).Load();
                    if (face != null && !face.IsColor) faces.Add(face);
                }
            }
            foreach (string stem in faces.Count > 0 ? Array.Empty<string>() : DefaultStems())
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
            // On Android a family name or alias of the system's configuration comes first: sans-serif, serif, monospace, Roboto.
            FontFace found = Host() == Os.Android ? AndroidNamed(name) : null;
            string file = found != null ? null : FindFile(name) ?? FindFile(compact);
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

        /// <summary>
        /// The other cuts of a system face's family, as files: its weights and slopes. Found by the files whose name starts as this one's
        /// does and whose family is the same, each opened once to read its name. Empty for a face that is not from a file.
        /// </summary>
        internal static List<(string path, int index)> CutsOf(FontFace face)
        {
            var cuts = new List<(string, int)>();
            if (face == null || face.FilePath == null || !IsSupported) return cuts;
            if (Host() == Os.Android && AndroidCutsOf(face, cuts)) return cuts;
            List<string> files = AllFiles();
            string stem = Path.GetFileNameWithoutExtension(face.FilePath);
            int dash = stem.IndexOf('-');
            // "NotoSans-Bold" names its family before the dash; "segoeuib" and "seguisb" share only their start.
            string prefix = dash > 0 ? stem.Substring(0, dash + 1) : stem.Substring(0, Math.Min(3, stem.Length));
            int probed = 0;
            for (int i = 0; i < files.Count && probed < 64; i++)
            {
                if (!s_Stems[i].StartsWith(prefix, StringComparison.OrdinalIgnoreCase) || string.Equals(files[i], face.FilePath, StringComparison.OrdinalIgnoreCase)) continue;
                probed++;
                FontFace probe = null;
                try { probe = FontFace.FromFile(files[i], 0); }
                catch (Exception) { } // A file the system will not open is no cut.
                if (probe == null) continue;
                bool same = !probe.IsColor && string.Equals(probe.Family, face.Family, StringComparison.OrdinalIgnoreCase);
                probe.Dispose();
                if (same) cuts.Add((files[i], 0));
            }
            return cuts;
        }

        // CJK families share codepoints, so the language picks the regional file or collection face.
        static FontFace FindCjkForLanguage(int codepoint, string language)
        {
            if (string.IsNullOrEmpty(language)) return null;
            AndroidFontConfig config = Host() == Os.Android ? AndroidConfig() : null;
            if (config != null)
            {
                foreach (AndroidFontConfig.Family family in config.ForLanguage(language))
                {
                    AndroidFontConfig.Font font = AndroidFontConfig.Pick(family, 400, false);
                    string path = font != null ? LocateAndroidFont(font.File) : null;
                    FontFace face = path != null ? GetEntry(path, font.Index).Load() : null;
                    if (face != null && face.HasGlyph(codepoint)) return face;
                }
            }
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
            s_Families = null;
            s_Emoji = null;
            s_EmojiSearched = false;
            BrowserEmoji.Reset();
            LetteraFontResolver.Invalidate();
        }
    }
}

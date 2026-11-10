using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace FirstSymbol.Lettera
{
    public static partial class LetteraSystemFonts
    {
        /// <summary>One cut of a family the system has: a file and the face of it, with the weight and slope it states.</summary>
        internal struct FamilyCut
        {
            public string Path;
            public int Index;
            public int Weight;
            public bool Italic;

            /// <summary>The weights a variable font reaches; 0 for a font with one.</summary>
            public int WeightMin, WeightMax;
        }

        // Family name, folded, to its cuts. Found by opening each font file once, the first time a name is not a file stem.
        static Dictionary<string, List<FamilyCut>> s_Families;

        /// <summary>What a test puts in place of the machine's fonts: folded family name to cuts. Null reads the machine.</summary>
        internal static Dictionary<string, List<FamilyCut>> FamiliesForTests;

        /// <summary>A name as families are compared: letters and digits only, lower case. "Segoe UI", "segoe-ui" and "SegoeUI" are one.</summary>
        internal static string FoldFamily(string name)
        {
            if (string.IsNullOrEmpty(name)) return "";
            var folded = new System.Text.StringBuilder(name.Length);
            foreach (char c in name)
                if (char.IsLetterOrDigit(c))
                    folded.Append(char.ToLowerInvariant(c));
            return folded.ToString();
        }

        /// <summary>The cuts the system has of a family by its name, or null when it has none. Opens the font files once to read their names.</summary>
        internal static List<FamilyCut> FamilyCuts(string name)
        {
            string key = FoldFamily(name);
            if (key.Length == 0) return null;
            if (!IsSupported) return null;
            if (FamiliesForTests != null) return FamiliesForTests.TryGetValue(key, out List<FamilyCut> fixture) ? fixture : null;
            if (Host() == Os.Android)
            {
                List<FamilyCut> configured = AndroidFamilyCuts(name);
                if (configured != null) return configured;
            }
            if (s_Families == null) s_Families = ReadFamilies();
            return s_Families.TryGetValue(key, out List<FamilyCut> cuts) ? cuts : null;
        }

        /// <summary>Forgets the family names read, so the next lookup reads them again.</summary>
        internal static void ForgetFamilies() => s_Families = null;

        /// <summary>Opens the system's font files to read their family names now, so the first lookup of a name does not. Safe to call more than once.</summary>
        internal static void PrepareFamilies()
        {
            if (FamiliesForTests != null || !IsSupported) return;
            if (s_Families == null) s_Families = ReadFamilies();
        }

        static List<FamilyCut> AndroidFamilyCuts(string name)
        {
            AndroidFontConfig config = AndroidConfig();
            AndroidFontConfig.Family family = config?.Named(name, out _);
            if (family == null) return null;
            var cuts = new List<FamilyCut>();
            foreach (AndroidFontConfig.Font font in family.Fonts)
            {
                if (font.FallbackFor != null) continue;
                string path = LocateAndroidFont(font.File);
                if (path != null) cuts.Add(new FamilyCut { Path = path, Index = font.Index, Weight = font.Weight, Italic = font.Italic });
            }
            return cuts.Count > 0 ? cuts : null;
        }

        static unsafe Dictionary<string, List<FamilyCut>> ReadFamilies()
        {
            var families = new Dictionary<string, List<FamilyCut>>();
            foreach (string file in AllFiles())
            {
                int faces = 1;
                if (LetteraNative.Version >= 4 && (file.EndsWith(".ttc", StringComparison.OrdinalIgnoreCase) || file.EndsWith(".otc", StringComparison.OrdinalIgnoreCase)))
                {
                    byte[] utf8 = FontFace.NullTerminated(file);
                    fixed (byte* p = utf8) faces = Math.Max(1, Math.Min(LetteraNative.lt_file_face_count(p), 32));
                }
                for (int index = 0; index < faces; index++)
                {
                    FontFace probe = null;
                    try { probe = FontFace.FromFile(file, index); }
                    catch (Exception) { } // A file the system will not open is no cut.
                    if (probe == null) continue;
                    string key = FoldFamily(probe.Family);
                    if (key.Length > 0)
                    {
                        if (!families.TryGetValue(key, out List<FamilyCut> list)) families[key] = list = new List<FamilyCut>();
                        list.Add(new FamilyCut
                        {
                            Path = file, Index = index, Weight = probe.Weight, Italic = probe.IsItalic,
                            WeightMin = probe.HasWeightAxis ? (int)probe.WeightAxisMin : 0, WeightMax = probe.HasWeightAxis ? (int)probe.WeightAxisMax : 0,
                        });
                    }
                    probe.Dispose();
                }
            }
            return families;
        }

        /// <summary>The system's face in a file, opened once and shared.</summary>
        internal static FontFace OpenCut(string path, int index) => path == null ? null : GetEntry(path, index).Load();
    }
}

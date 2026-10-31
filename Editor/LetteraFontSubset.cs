using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace FirstSymbol.Lettera.Editor
{
    public enum LetteraSubsetMode
    {
        /// <summary>Only these characters stay.</summary>
        Keep,

        /// <summary>Everything except these characters stays.</summary>
        Remove,
    }

    /// <summary>What to make of a font. By default it keeps what a default subset would quietly drop.</summary>
    public class LetteraSubsetOptions
    {
        public LetteraSubsetMode Mode = LetteraSubsetMode.Keep;

        /// <summary>Text, ranges such as U+0400-04FF, or both.</summary>
        public string Characters = "";

        /// <summary>Adds the text of every Lettera text in scenes and prefabs.</summary>
        public bool FromProject;

        public bool KeepFeatures = true, KeepNames = true, KeepGlyphNames = true, KeepNotdefOutline = true;
    }

    /// <summary>What a subset did.</summary>
    public class LetteraSubsetResult
    {
        /// <summary>Why nothing came of it, or null.</summary>
        public string Refused;

        public byte[] Bytes;
        public int BytesBefore, BytesAfter, GlyphsBefore, GlyphsAfter;
        public readonly List<string> Warnings = new List<string>();

        public bool Succeeded => Refused == null && Bytes != null;
    }

    /// <summary>Makes a smaller font: a smaller download, and less of a font carried for characters a project never shows.</summary>
    public static unsafe class LetteraFontSubset
    {
        const string Lib = "lettera_fonttools";

        [DllImport(Lib)] static extern int lts_version();
        [DllImport(Lib)] static extern void* lts_subset(byte* data, int length, int faceIndex, uint* unicodes, int count, int options, int* outLength);
        [DllImport(Lib)] static extern void lts_free(void* memory);

        /// <summary>The characters of a preset.</summary>
        public static readonly IReadOnlyDictionary<string, string> Presets = new Dictionary<string, string>
        {
            { "Basic Latin", "U+0020-007E" },
            { "Latin-1", "U+0020-007E U+00A0-00FF" },
            { "Cyrillic", "U+0400-04FF" },
            { "Greek", "U+0370-03FF" },
            { "Digits", "U+0030-0039" },
        };

        /// <summary>False where this editor has no copy of the tool. Everything else works without it.</summary>
        public static bool IsAvailable
        {
            get
            {
                try
                {
                    return lts_version() >= 1;
                }
                catch (DllNotFoundException)
                {
                    return false;
                }
                catch (EntryPointNotFoundException)
                {
                    return false;
                }
            }
        }

        /// <summary>Makes the subset, and changes nothing. Use <see cref="Replace"/> or <see cref="SaveAs"/> with what comes back.</summary>
        public static LetteraSubsetResult Run(LetteraFont font, LetteraSubsetOptions options)
        {
            var result = new LetteraSubsetResult();
            if (!IsAvailable)
            {
                result.Refused = "This editor's platform has no copy of the subset tool.";
                return result;
            }
            byte[] bytes = font != null ? font.ReadBytes() : null;
            if (bytes == null)
            {
                result.Refused = "This asset holds no font.";
                return result;
            }
            result.BytesBefore = bytes.Length;
            Inspect(bytes, font.FaceIndex, result);
            if (result.Refused != null) return result;

            FontFace face = font.LoadFace();
            if (face == null)
            {
                result.Refused = "The stored font could not be read.";
                return result;
            }
            result.GlyphsBefore = LetteraNative.lt_face_glyph_count(face.Face);

            var chosen = new HashSet<uint>();
            if (!ParseCharacters(options.Characters, chosen, out string problem))
            {
                result.Refused = problem;
                return result;
            }
            if (options.FromProject) CollectFromProject(chosen);

            uint[] unicodes = Keep(face, chosen, options.Mode);
            if (unicodes.Length == 0)
            {
                result.Refused = "None of the font's characters would stay, so the font would be empty.";
                return result;
            }

            int flags = (options.KeepFeatures ? 1 : 0) | (options.KeepNames ? 2 : 0) | (options.KeepGlyphNames ? 4 : 0) | (options.KeepNotdefOutline ? 8 : 0);
            int length = 0;
            void* output;
            fixed (byte* data = bytes)
            fixed (uint* list = unicodes)
                output = lts_subset(data, bytes.Length, font.FaceIndex, list, unicodes.Length, flags, &length);
            if (output == null)
            {
                result.Refused = "The font could not be made smaller, for reasons inside the font.";
                return result;
            }
            result.Bytes = new byte[length];
            Marshal.Copy((IntPtr)output, result.Bytes, 0, length);
            lts_free(output);
            result.BytesAfter = length;

            LetteraFont made = LetteraFont.CreateFromBytes(result.Bytes);
            FontFace madeFace = made.LoadFace();
            result.GlyphsAfter = madeFace != null ? LetteraNative.lt_face_glyph_count(madeFace.Face) : 0;
            if (madeFace == null || result.GlyphsAfter <= 1)
            {
                result.Refused = "The subset came out empty.";
                result.Bytes = null;
            }
            UnityEngine.Object.DestroyImmediate(made);
            return result;
        }

        /// <summary>Replaces the font's data, as one undo step. The original file is not kept in the asset: undo brings it back, a later reimport has to come from its file.</summary>
        public static void Replace(LetteraFont font, byte[] bytes)
        {
            Undo.RegisterCompleteObjectUndo(font, "Subset Font");
            font.SetFontData(bytes);
            EditorUtility.SetDirty(font);
        }

        /// <summary>Leaves the font alone and stores the subset as a new asset.</summary>
        public static LetteraFont SaveAs(LetteraFont source, byte[] bytes, string path)
        {
            var made = ScriptableObject.CreateInstance<LetteraFont>();
            made.SetFontData(bytes);
            AssetDatabase.CreateAsset(made, AssetDatabase.GenerateUniqueAssetPath(path));
            AssetDatabase.SaveAssets();
            return made;
        }

        static uint[] Keep(FontFace face, HashSet<uint> chosen, LetteraSubsetMode mode)
        {
            int count = LetteraNative.lt_face_collect_unicodes(face.Face, null, 0);
            var all = new uint[count];
            fixed (uint* p = all) LetteraNative.lt_face_collect_unicodes(face.Face, p, count);
            var kept = new List<uint>(count);
            foreach (uint unicode in all)
                if (chosen.Contains(unicode) == (mode == LetteraSubsetMode.Keep))
                    kept.Add(unicode);
            return kept.ToArray();
        }

        // Which tables a font has, read from its directory: a verdict that does not need the subset to be tried.
        static void Inspect(byte[] font, int faceIndex, LetteraSubsetResult result)
        {
            var tables = new HashSet<string>();
            if (!ReadTables(font, faceIndex, tables))
            {
                result.Refused = "The stored font could not be read.";
                return;
            }
            bool outlines = tables.Contains("glyf") || tables.Contains("CFF ") || tables.Contains("CFF2");
            bool color = tables.Contains("COLR") || tables.Contains("sbix") || tables.Contains("CBDT") || tables.Contains("SVG ");
            if (!outlines && !color)
            {
                result.Refused = "This font holds only plain bitmaps, which the subset tool cannot keep.";
                return;
            }
            if (tables.Contains("kern") && !tables.Contains("GPOS")) result.Warnings.Add("This font kerns only through an old kern table, which a subset cannot carry: its kerning will be lost.");
        }

        static bool ReadTables(byte[] font, int faceIndex, HashSet<string> tables)
        {
            if (font.Length < 12) return false;
            int at = 0;
            if (font[0] == 't' && font[1] == 't' && font[2] == 'c' && font[3] == 'f')
            {
                int faces = BigInt(font, 8);
                if (faceIndex >= faces || 12 + faceIndex * 4 + 4 > font.Length) return false;
                at = BigInt(font, 12 + faceIndex * 4);
                if (at < 0 || at + 12 > font.Length) return false;
            }
            int count = (font[at + 4] << 8) | font[at + 5];
            if (at + 12 + count * 16 > font.Length) return false;
            for (int i = 0; i < count; i++)
                tables.Add(Encoding.ASCII.GetString(font, at + 12 + i * 16, 4));
            return true;
        }

        static int BigInt(byte[] data, int at) => (data[at] << 24) | (data[at + 1] << 16) | (data[at + 2] << 8) | data[at + 3];

        /// <summary>Reads text and ranges. U+ marks a character or a range; everything else counts as itself. A malformed U+ is said out loud.</summary>
        public static bool ParseCharacters(string text, HashSet<uint> into, out string problem)
        {
            problem = null;
            if (string.IsNullOrEmpty(text)) return true;
            int i = 0;
            while (i < text.Length)
            {
                if (i + 1 < text.Length && (text[i] == 'U' || text[i] == 'u') && text[i + 1] == '+')
                {
                    int start = i + 2, end = start;
                    while (end < text.Length && Uri.IsHexDigit(text[end])) end++;
                    if (end == start || !TryHex(text, start, end, out uint first))
                    {
                        problem = "Cannot read '" + text.Substring(i, Math.Min(10, text.Length - i)) + "' as a character.";
                        return false;
                    }
                    uint last = first;
                    if (end < text.Length && text[end] == '-')
                    {
                        int second = end + 1, stop = second;
                        while (stop < text.Length && Uri.IsHexDigit(text[stop])) stop++;
                        if (stop == second || !TryHex(text, second, stop, out last) || last < first)
                        {
                            problem = "Cannot read the range at '" + text.Substring(i, Math.Min(16, text.Length - i)) + "'.";
                            return false;
                        }
                        end = stop;
                    }
                    for (uint c = first; c <= last; c++) into.Add(c);
                    i = end;
                    continue;
                }
                int codepoint = char.ConvertToUtf32(text, i);
                i += char.IsSurrogatePair(text, i) ? 2 : 1;
                if (codepoint > ' ') into.Add((uint)codepoint);
            }
            return true;
        }

        static bool TryHex(string text, int start, int end, out uint value) =>
            uint.TryParse(text.Substring(start, end - start), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value) && value <= 0x10FFFF;

        static void CollectFromProject(HashSet<uint> into)
        {
            foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets" }))
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid));
                if (prefab == null) continue;
                foreach (LetteraText text in prefab.GetComponentsInChildren<LetteraText>(true)) AddText(text.Text, into);
                foreach (LetteraWorldText text in prefab.GetComponentsInChildren<LetteraWorldText>(true)) AddText(text.Text, into);
            }
            foreach (LetteraText text in Resources.FindObjectsOfTypeAll<LetteraText>()) AddText(text.Text, into);
            foreach (LetteraWorldText text in Resources.FindObjectsOfTypeAll<LetteraWorldText>()) AddText(text.Text, into);
        }

        static void AddText(string text, HashSet<uint> into)
        {
            if (string.IsNullOrEmpty(text)) return;
            for (int i = 0; i < text.Length; i++)
            {
                if (char.IsLowSurrogate(text[i])) continue;
                if (char.IsHighSurrogate(text[i]) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1])) into.Add((uint)char.ConvertToUtf32(text, i));
                else if (!char.IsSurrogate(text[i])) into.Add(text[i]);
            }
        }
    }
}

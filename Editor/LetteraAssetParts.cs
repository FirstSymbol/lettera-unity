using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace FirstSymbol.Lettera.Editor
{
    /// <summary>Who refers to an asset, from a look through the text of the project's files. Only on a press: it reads every scene and prefab.</summary>
    internal static class LetteraFindUses
    {
        static readonly string[] s_Patterns = { "*.asset", "*.prefab", "*.unity", "*.mat", "*.playable", "*.controller" };

        /// <summary>The files below a folder that name a guid, with their size on disk. The asset itself does not count.</summary>
        internal static List<(string path, long bytes)> Scan(string folder, string guid, string self)
        {
            var found = new List<(string, long)>();
            foreach (string pattern in s_Patterns)
            {
                foreach (string file in Directory.GetFiles(folder, pattern, SearchOption.AllDirectories))
                {
                    string path = file.Replace('\\', '/');
                    if (path == self) continue;
                    try
                    {
                        if (File.ReadAllText(path).Contains(guid)) found.Add((path, new FileInfo(path).Length));
                    }
                    catch (IOException)
                    {
                        // A file another process holds is left out of the count.
                    }
                }
            }
            found.Sort((a, b) => string.CompareOrdinal(a.Item1, b.Item1));
            return found;
        }

        public static VisualElement Card(UnityEngine.Object asset)
        {
            VisualElement results = LtUI.El("lt-list");
            string path = AssetDatabase.GetAssetPath(asset);
            return LtUI.Card("Find Uses",
                LtUI.Clickable("Find Uses", "lt-add lt-add--small", "Reads the project's scenes, prefabs and assets, and lists those that refer to this one.", () =>
                {
                    results.Clear();
                    if (string.IsNullOrEmpty(path))
                    {
                        results.Add(LtUI.Text("Save this asset first.", "lt-hint"));
                        return;
                    }
                    Show(results, Scan("Assets", AssetDatabase.AssetPathToGUID(path), path));
                }),
                results);
        }

        static void Show(VisualElement results, List<(string path, long bytes)> found)
        {
            if (found.Count == 0)
            {
                results.Add(LtUI.Text("Nothing in Assets refers to this. A script may still load it by name.", "lt-hint"));
                return;
            }
            foreach ((string path, long bytes) in found)
            {
                string shown = path;
                results.Add(LtUI.Clickable(Path.GetFileName(path) + "   " + bytes / 1024 + " KB", "lt-find__row", shown, () => EditorGUIUtility.PingObject(AssetDatabase.LoadMainAssetAtPath(shown))));
            }
        }
    }

    /// <summary>One glyph's outline, drawn by us, so that a font's letters show in its own inspector.</summary>
    internal sealed class LtGlyphCell : VisualElement
    {
        float[] m_Commands = Array.Empty<float>();
        float m_Upem = 1, m_Advance;

        public unsafe LtGlyphCell(FontInstance instance, uint glyph, string tooltipText)
        {
            AddToClassList("lt-glyph-cell");
            tooltip = tooltipText;
            float upem = Mathf.Max(instance.Face.Metrics.Upem, 1);
            m_Upem = upem;
            var commands = new float[4096];
            int count;
            fixed (float* p = commands) count = LetteraNative.lt_glyph_outline(instance.Font, glyph, p, commands.Length);
            m_Commands = new float[Math.Min(Math.Max(count, 0), commands.Length)];
            Array.Copy(commands, m_Commands, m_Commands.Length);
            m_Advance = upem * 0.6f;
            generateVisualContent += Draw;
        }

        void Draw(MeshGenerationContext context)
        {
            float[] c = m_Commands;
            if (c.Length == 0) return;
            Painter2D painter = context.painter2D;
            Rect box = contentRect;
            float scale = Mathf.Min(box.width, box.height) / m_Upem * 0.62f;
            Vector2 origin = new Vector2(box.width * 0.5f - m_Advance * scale * 0.5f, box.height * 0.76f);
            Vector2 P(float x, float y) => origin + new Vector2(x * scale, -y * scale);
            painter.strokeColor = resolvedStyle.color;
            painter.lineWidth = 1f;
            // A mitred join at a contour's closing corner throws a long spike.
            painter.lineJoin = LineJoin.Round;
            painter.lineCap = LineCap.Round;
            bool open = false;
            for (int i = 0; i < c.Length;)
            {
                int command = (int)c[i];
                if (command == 0 && i + 2 < c.Length)
                {
                    // Each contour is its own path: the painter joins a later one to the corner of the element otherwise.
                    if (open) painter.Stroke();
                    painter.BeginPath();
                    open = true;
                    painter.MoveTo(P(c[i + 1], c[i + 2]));
                    i += 3;
                }
                else if (command == 1 && i + 2 < c.Length)
                {
                    painter.LineTo(P(c[i + 1], c[i + 2]));
                    i += 3;
                }
                else if (command == 2 && i + 4 < c.Length)
                {
                    painter.QuadraticCurveTo(P(c[i + 1], c[i + 2]), P(c[i + 3], c[i + 4]));
                    i += 5;
                }
                else if (command == 3 && i + 6 < c.Length)
                {
                    painter.BezierCurveTo(P(c[i + 1], c[i + 2]), P(c[i + 3], c[i + 4]), P(c[i + 5], c[i + 6]));
                    i += 7;
                }
                else if (command == 4)
                {
                    painter.ClosePath();
                    i++;
                }
                else
                {
                    break;
                }
            }
            if (open) painter.Stroke();
        }
    }

    /// <summary>Type a string, see this font's glyphs for it. A click opens the glyph in the Glyphs window.</summary>
    internal static class LetteraGlyphGrid
    {
        public static VisualElement Card(LetteraFontAsset font)
        {
            var input = new TextField { value = "AaBbGg 0123" };
            input.AddToClassList("lt-text-input");
            VisualElement grid = LtUI.El("lt-glyph-grid");
            void Show(string text)
            {
                grid.Clear();
                FontFace face = font.LoadFace();
                if (face == null || face.Regular == null) return;
                foreach (int codepoint in Codepoints(text))
                {
                    uint glyph = LetteraNative.lt_font_glyph(face.Regular.Font, (uint)codepoint, 0);
                    string shown = char.ConvertFromUtf32(codepoint);
                    if (glyph == 0)
                    {
                        Label missing = LtUI.Text(shown, "lt-glyph-cell lt-glyph-cell--missing");
                        missing.tooltip = "U+" + codepoint.ToString("X4") + ": not in this font.";
                        grid.Add(missing);
                        continue;
                    }
                    var cell = new LtGlyphCell(face.Regular, glyph, "U+" + codepoint.ToString("X4") + ", glyph " + glyph + ". A click opens it in the Glyphs window.");
                    cell.RegisterCallback<ClickEvent>(_ => LetteraGlyphsWindow.OpenFor(font, shown));
                    grid.Add(cell);
                }
            }
            input.RegisterValueChangedCallback(e => Show(e.newValue));
            Show(input.value);
            return LtUI.Card("Glyphs", LtUI.Row("Show", input, "Letters to see in this font."), grid);
        }

        internal static IEnumerable<int> Codepoints(string text)
        {
            for (int i = 0; i < text.Length; i++)
            {
                int codepoint = char.ConvertToUtf32(text, i);
                if (char.IsHighSurrogate(text[i])) i++;
                if (!char.IsWhiteSpace((char)Mathf.Min(codepoint, 0xFFFF))) yield return codepoint;
            }
        }
    }

    /// <summary>What a stack's family holds, and which family answers what.</summary>
    internal static class LetteraStackFacts
    {
        static readonly (string name, int codepoint)[] s_Scripts =
        {
            ("Latin", 'A'), ("Cyrillic", 'Я'), ("Greek", 'Ω'), ("Arabic", 0x0639), ("Hebrew", 0x05D0), ("Devanagari", 0x0905), ("Thai", 0x0E01), ("Han", 0x6F22), ("Kana", 0x3042), ("Hangul", 0xD55C),
        };

        /// <summary>The writing systems a font has a typical letter of.</summary>
        internal static string Coverage(LetteraFontAsset font)
        {
            FontFace face = font != null ? font.LoadFace() : null;
            if (face == null) return "";
            var names = new List<string>();
            foreach ((string name, int codepoint) in s_Scripts)
                if (face.HasGlyph(codepoint))
                    names.Add(name);
            return names.Count > 0 ? string.Join(", ", names) : "None of the common scripts";
        }

        /// <summary>A text cut into runs by which family answers each letter: name, what it covers. An empty name means none does.</summary>
        internal static List<(string family, string text)> Answers(LetteraFontStack stack, string text)
        {
            var chain = new FontChain();
            chain.Build(null, stack);
            var runs = new List<(string, string)>();
            string current = null;
            var run = new System.Text.StringBuilder();
            var cluster = new int[1];
            for (int at = 0; at < (text ?? "").Length; at++)
            {
                int codepoint = char.ConvertToUtf32(text, at);
                if (char.IsHighSurrogate(text[at])) at++;
                // A space goes with the run it stands in.
                if (codepoint < 0x10000 && char.IsWhiteSpace((char)codepoint))
                {
                    if (run.Length > 0) run.Append((char)codepoint);
                    continue;
                }
                cluster[0] = codepoint;
                FontFace face = chain.Resolve(cluster, false, null, null, out FontChain.Family family);
                string name = face != null && face.HasGlyph(codepoint) ? family?.Name ?? face.Family : "";
                if (name != current)
                {
                    if (run.Length > 0) runs.Add((current, run.ToString().TrimEnd()));
                    run.Clear();
                    current = name;
                }
                run.Append(char.ConvertFromUtf32(codepoint));
            }
            if (run.Length > 0) runs.Add((current, run.ToString().TrimEnd()));
            return runs;
        }

        public static VisualElement TestLine(LetteraFontStack stack)
        {
            var input = new TextField { value = "Hello Привет مرحبا 漢字" };
            input.AddToClassList("lt-text-input");
            VisualElement answers = LtUI.El("lt-list");
            void Show(string text)
            {
                answers.Clear();
                foreach ((string family, string run) in Answers(stack, text))
                {
                    bool missing = string.IsNullOrEmpty(family);
                    answers.Add(LtUI.El("lt-row",
                        LtUI.Text(missing ? "Missing" : family, missing ? "lt-row__label lt-answer--missing" : "lt-row__label"),
                        LtUI.Text(run, "lt-row__value")));
                }
            }
            input.RegisterValueChangedCallback(e => Show(e.newValue));
            Show(input.value);
            return LtUI.Card("Test Line", LtUI.Row("Text", input, "Which family answers each run. Texts behind this stack do the same, with the system's fonts after it."), answers);
        }
    }
}

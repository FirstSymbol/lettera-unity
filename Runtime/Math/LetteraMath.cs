using System;
using System.Collections.Generic;
using UnityEngine;

namespace FirstSymbol.Lettera
{
    /// <summary>One formula of a text, as it is drawn.</summary>
    public struct LetteraFormulaInfo
    {
        /// <summary>The code point standing for it in the shown text.</summary>
        public int CodePoint;
        public string Source;
        public bool Display;
        /// <summary>Its box in the text's local space.</summary>
        public Rect Box;
        public bool Failed;
        public string Message;
        public int ErrorPosition;
    }

    /// <summary>What a formula that cannot be read shows.</summary>
    public enum LetteraMathErrorDisplay
    {
        /// <summary>What was written, in a frame.</summary>
        Frame,
        /// <summary>What was written.</summary>
        Source,
        /// <summary>Nothing, and no width.</summary>
        Nothing,
    }

    /// <summary>The project's defaults for formulas.</summary>
    [Serializable]
    public class LetteraMathSettings
    {
        [Tooltip("The font formulas use. Empty uses the text's font if it holds a MATH table, then a system math font, then whatever the text uses, with spacing worked out without a table.")]
        public LetteraFontAsset Font;

        [Tooltip("A formula against the text around it, for a math font visibly larger or smaller.")]
        public float Scale = 1;

        [Tooltip("What a formula that cannot be read shows. The text around it renders whichever this is.")]
        public LetteraMathErrorDisplay Errors = LetteraMathErrorDisplay.Frame;

        [Tooltip("How much a stretched delimiter covers of what it wraps. 1 covers it exactly.")]
        public float DelimiterFactor = 1;

        [Tooltip("The least a formula's rule draws, in device pixels, since a faint fraction bar reads as missing. Layout does not change. 0 turns this off.")]
        public float MinRulePixels = 1;

        [Tooltip("The most laid-out formulas kept, in bytes.")]
        public long CacheBytes = 4 * 1024 * 1024;
    }

    /// <summary>Formulas from code: measuring, checking and hearing of failures.</summary>
    public static class LetteraMath
    {
        static readonly List<ILetteraMathExtension> s_Extensions = new List<ILetteraMathExtension>();
        static readonly HashSet<string> s_Warned = new HashSet<string>();

        /// <summary>A formula could not be read: its source, where, and why. The text around it still renders.</summary>
        public static event Action<string, int, string> Failed;

        /// <summary>Adds commands the parser does not know. Asked only for a name it has no answer for.</summary>
        public static void Register(ILetteraMathExtension extension)
        {
            if (extension != null && !s_Extensions.Contains(extension)) s_Extensions.Add(extension);
        }

        public static void Unregister(ILetteraMathExtension extension) => s_Extensions.Remove(extension);

        internal static bool TryExtension(string command, out string replacement)
        {
            replacement = null;
            foreach (ILetteraMathExtension extension in s_Extensions)
                if (extension.TryParse(command, out replacement))
                    return true;
            return false;
        }

        /// <summary>Whether a formula can be read, with what is wrong and where. Reads only, so it is fit for a field's validator.</summary>
        public static bool Validate(string source, out string message, out int position)
        {
            MathTree tree = LetteraMathParser.Parse(source);
            message = tree.Error;
            position = tree.ErrorPosition;
            return tree.Error == null;
        }

        /// <summary>A formula's width, height above the baseline and depth below it, at a size, with no text around it.</summary>
        public static Vector3 Measure(string source, bool display, LetteraFontAsset font, float size)
        {
            FontFace face = font != null ? font.LoadFace() : null;
            face = face ?? LetteraSettings.Instance.Math.Font?.LoadFace() ?? DefaultFace();
            if (face == null) return default;
            LetteraMathLayout layout = LetteraMathCache.Get(source ?? "", display, face, face);
            return new Vector3(layout.Width, layout.Ascent, layout.Descent) * size;
        }

        static FontFace DefaultFace()
        {
            FontFace[] faces = LetteraSystemFonts.DefaultFaces();
            return faces != null && faces.Length > 0 ? faces[0] : null;
        }

        internal static void Report(LetteraMathLayout layout)
        {
            if (!layout.Failed) return;
            if (s_Warned.Count < 64 && s_Warned.Add(layout.Source))
                Debug.LogWarning($"Lettera: a formula could not be read at {layout.ErrorPosition}: {layout.Message} Source: {(layout.Source.Length > 64 ? layout.Source.Substring(0, 64) + "…" : layout.Source)}");
            Failed?.Invoke(layout.Source, layout.ErrorPosition, layout.Message);
        }

        internal static void ForgetWarnings() => s_Warned.Clear();
    }

    /// <summary>
    /// Laid-out formulas, shared by every text. The key holds the source and the fonts, and the source is compared on a hit.
    /// A neighbour's style change or a hover finds the same layout, so nothing goes blank in between.
    /// </summary>
    internal static class LetteraMathCache
    {
        struct Key : IEquatable<Key>
        {
            public string Source;
            public bool Display;
            public int Math, Text;

            public bool Equals(Key other) => Display == other.Display && Math == other.Math && Text == other.Text && Source == other.Source;
            public override bool Equals(object obj) => obj is Key other && Equals(other);
            public override int GetHashCode() => (Source.GetHashCode() * 397) ^ (Math * 31) ^ (Text * 17) ^ (Display ? 1 : 0);
        }

        const int MaxEntries = 1024;
        static readonly Dictionary<Key, LinkedListNode<(Key key, LetteraMathLayout layout)>> s_Entries = new Dictionary<Key, LinkedListNode<(Key, LetteraMathLayout)>>();
        static readonly LinkedList<(Key key, LetteraMathLayout layout)> s_Order = new LinkedList<(Key, LetteraMathLayout)>();
        static long s_Bytes;

        internal static int Hits { get; private set; }
        internal static int Count => s_Entries.Count;

        internal static void Clear()
        {
            s_Entries.Clear();
            s_Order.Clear();
            s_Bytes = 0;
            LetteraMathFont.ClearAll();
        }

        internal static LetteraMathLayout Get(string source, bool display, FontFace mathFace, FontFace textFace)
        {
            var key = new Key { Source = source, Display = display, Math = mathFace.Regular.Id, Text = textFace != null ? textFace.Regular.Id : 0 };
            if (s_Entries.TryGetValue(key, out LinkedListNode<(Key, LetteraMathLayout)> node))
            {
                Hits++;
                s_Order.Remove(node);
                s_Order.AddFirst(node);
                return node.Value.Item2;
            }
            LetteraMathLayout layout = Lay(source, display, mathFace, textFace);
            var added = s_Order.AddFirst((key, layout));
            s_Entries[key] = added;
            s_Bytes += layout.Bytes;
            long limit = LetteraSettings.Instance.Math.CacheBytes;
            while ((s_Entries.Count > MaxEntries || s_Bytes > limit) && s_Order.Count > 1)
            {
                var last = s_Order.Last;
                s_Order.RemoveLast();
                s_Entries.Remove(last.Value.key);
                s_Bytes -= last.Value.layout.Bytes;
            }
            return layout;
        }

        static LetteraMathLayout Lay(string source, bool display, FontFace mathFace, FontFace textFace)
        {
            LetteraMathFont font = LetteraMathFont.For(mathFace);
            LetteraMathFont text = textFace != null && !textFace.IsVirtual ? LetteraMathFont.For(textFace) : font;
            LetteraMathErrorDisplay errors = LetteraSettings.Instance.Math.Errors;
            MathTree tree = LetteraMathParser.Parse(source);
            LetteraMathLayout layout;
            if (tree.Error != null)
            {
                layout = LetteraMathBuilder.Error(source, tree.Error, tree.ErrorPosition, font, text, errors);
            }
            else
            {
                layout = LetteraMathBuilder.Build(tree, source, display, font, text);
                if (layout == null) layout = LetteraMathBuilder.Error(source, "This formula draws more than " + LetteraMathLimits.DrawItems + " things.", 0, font, text, errors);
            }
            LetteraMath.Report(layout);
            return layout;
        }
    }

    /// <summary>A formula's tag: &lt;math&gt;x^2&lt;/math&gt;, or &lt;math=display&gt; for one on a line of its own. What is inside is the formula's own, verbatim: no tag and no escape means anything there.</summary>
    [Serializable, UnityEngine.Scripting.Preserve]
    public class LetteraMathTagMatcher : LetteraMatcher
    {
        [SerializeField] string m_Name = "math";

        public LetteraMathTagMatcher() { }
        public LetteraMathTagMatcher(string name) => m_Name = name;

        public string Name { get => m_Name; set => m_Name = value; }

        public override int Priority => int.MaxValue - 1;

        public override void Find(LetteraMatchContext context)
        {
            if (string.IsNullOrEmpty(m_Name)) return;
            IReadOnlyList<int> positions = context.LessThanPositions;
            if (positions.Count == 0) return;
            string text = null;
            ReadOnlySpan<char> source = context.Text;
            string closer = "</" + m_Name + ">";
            bool noneLeft = false;
            int formulas = 0;
            for (int p = 0; p < positions.Count; p++)
            {
                int at = positions[p];
                if (!context.IsFree(at, 1)) continue;
                if (context.HasEscapes && OddBackslashesBefore(source, at)) continue;
                if (TagSyntax.MatchClose(source, at, m_Name, out int closeLength))
                {
                    if (context.IsFree(at, closeLength)) context.Replace(at, closeLength);
                    continue;
                }
                if (noneLeft) continue;
                if (!TagSyntax.MatchOpen(source, at, m_Name, out int length, out string[] slots, out string label, out _)) continue;
                if (!context.IsFree(at, length)) continue;
                // Once no closer is found from here, none is found from anywhere after, so an unclosed flood stays linear.
                if (text == null) text = source.ToString();
                int close = text.IndexOf(closer, at + length, StringComparison.OrdinalIgnoreCase);
                if (close < 0)
                {
                    noneLeft = true;
                    continue;
                }
                int total = close + closer.Length - at;
                if (!context.IsFree(at, total)) continue;
                if (++formulas > LetteraMathLimits.FormulasInText)
                {
                    context.Replace(at, total);
                    continue;
                }
                string mode = slots != null && slots.Length > 0 ? slots[0] : null;
                bool display = string.Equals(mode, "display", StringComparison.OrdinalIgnoreCase);
                string alt = slots != null && slots.Length > 1 ? slots[1] : null;
                string formula = text.Substring(at + length, close - at - length);
                string replacement = "￼";
                if (display)
                {
                    // On a line of its own, with no empty line when it already stands alone.
                    bool lineBefore = at > 0 && text[at - 1] != '\n';
                    int after = close + closer.Length;
                    bool lineAfter = after < text.Length && text[after] != '\n';
                    replacement = (lineBefore ? "\n" : "") + replacement + (lineAfter ? "\n" : "");
                }
                context.ReplaceAndRange(at, total, replacement, new[] { display ? "display" : "inline", alt, formula }, label);
            }
        }

        static bool OddBackslashesBefore(ReadOnlySpan<char> source, int at)
        {
            int count = 0;
            while (at - count - 1 >= 0 && source[at - count - 1] == '\\') count++;
            return (count & 1) == 1;
        }
    }

    /// <summary>
    /// Formulas in plain text: \(...\) and \[...\], and, when asked, $...$ and $$...$$. Off by default, since each means something else in ordinary text.
    /// A formula with no closing delimiter stays text.
    /// </summary>
    [Serializable, UnityEngine.Scripting.Preserve]
    public class LetteraMathSpanMatcher : LetteraMatcher
    {
        [Tooltip("$...$ and $$...$$. A $ opens before a non-space and closes after one, not before a digit, so $20 and $30 stays text.")]
        [SerializeField] bool m_Dollar;
        [Tooltip("\\(...\\) and \\[...\\].")]
        [SerializeField] bool m_Backslash = true;

        public bool Dollar { get => m_Dollar; set => m_Dollar = value; }
        public bool Backslash { get => m_Backslash; set => m_Backslash = value; }

        public override int Priority => int.MaxValue - 1;

        public override void Find(LetteraMatchContext context)
        {
            string text = context.Text.ToString();
            int at = 0;
            int formulas = 0;
            bool noDollar = false, noParen = false, noBracket = false, noDouble = false;
            while (at < text.Length)
            {
                char c = text[at];
                int open = 0;
                string closer = null;
                bool display = false;
                if (m_Backslash && c == '\\' && at + 1 < text.Length && text[at + 1] == '(' && !noParen)
                {
                    open = 2;
                    closer = "\\)";
                }
                else if (m_Backslash && c == '\\' && at + 1 < text.Length && text[at + 1] == '[' && !noBracket)
                {
                    open = 2;
                    closer = "\\]";
                    display = true;
                }
                else if (m_Dollar && c == '$' && at + 1 < text.Length && text[at + 1] == '$' && !noDouble)
                {
                    open = 2;
                    closer = "$$";
                    display = true;
                }
                else if (m_Dollar && c == '$' && !noDollar && at + 1 < text.Length && !char.IsWhiteSpace(text[at + 1]))
                {
                    open = 1;
                    closer = "$";
                }
                if (closer == null || !context.IsFree(at, open))
                {
                    at++;
                    continue;
                }
                int close = closer == "$" ? FindDollarClose(text, at + 1) : text.IndexOf(closer, at + open, StringComparison.Ordinal);
                if (close < 0)
                {
                    if (closer == "\\)") noParen = true;
                    else if (closer == "\\]") noBracket = true;
                    else if (closer == "$$") noDouble = true;
                    else noDollar = true;
                    at++;
                    continue;
                }
                int total = close + closer.Length - at;
                if (!context.IsFree(at, total) || ++formulas > LetteraMathLimits.FormulasInText)
                {
                    at += open;
                    continue;
                }
                string replacement = "￼";
                if (display)
                {
                    bool lineBefore = at > 0 && text[at - 1] != '\n';
                    int after = close + closer.Length;
                    bool lineAfter = after < text.Length && text[after] != '\n';
                    replacement = (lineBefore ? "\n" : "") + replacement + (lineAfter ? "\n" : "");
                }
                context.ReplaceAndRange(at, total, replacement, new[] { display ? "display" : "inline", null, text.Substring(at + open, close - at - open) }, null);
                at += total;
            }
        }

        // Within one paragraph, after a non-space and not before a digit.
        static int FindDollarClose(string text, int from)
        {
            for (int i = from; i < text.Length; i++)
            {
                char c = text[i];
                if (c == '\n') return -1;
                if (c != '$') continue;
                if (i > from && char.IsWhiteSpace(text[i - 1])) continue;
                if (i + 1 < text.Length && char.IsDigit(text[i + 1])) continue;
                return i;
            }
            return -1;
        }
    }

    /// <summary>Draws a formula in the text's flow, where its tag stood. Slots: mode, alternative text, source.</summary>
    [Serializable, UnityEngine.Scripting.Preserve]
    public class LetteraMathEffect : InlineEffect
    {
        [SerializeField, Tooltip("The font formulas of this style use. Empty uses the project's.")] LetteraFontAsset m_Font;
        [SerializeField, Tooltip("A formula against the text around it.")] float m_Scale = 1;

        public LetteraFontAsset Font { get => m_Font; set => m_Font = value; }
        public float Scale { get => m_Scale; set => m_Scale = value; }

        public override void Apply(LetteraAttributes attributes, int start, int end, in LetteraSlots slots)
        {
            base.Apply(attributes, start, end, slots);
            if (string.Equals(slots[0], "display", StringComparison.OrdinalIgnoreCase))
                attributes.SetAlign(start, end, new AlignSpec { Alignment = (int)LetteraHorizontalAlignment.Center, MinWordSpace = 80, MinGlyphWidth = 100, MaxGlyphWidth = 100, MaxWordSpace = float.PositiveInfinity });
        }

        internal override TextEngine.InlineItem ResolveIn(TextEngine engine, in LetteraSlots slots, int cp)
        {
            string source = slots[2] ?? "";
            bool display = string.Equals(slots[0], "display", StringComparison.OrdinalIgnoreCase);
            FontFace text = engine.PrimaryFaceForMath;
            FontFace face = ResolveFace(m_Font, text);
            var item = new TextEngine.InlineItem { Effect = this, Cp = cp, Size = 1, Alt = slots[1], MathDisplay = display, MathSource = source };
            if (face == null) return item;
            LetteraMathLayout layout = LetteraMathCache.Get(source, display, face, text);
            float scale = m_Scale * LetteraSettings.Instance.Math.Scale;
            item.Math = layout;
            item.MathScale = scale;
            item.Advance = layout.Width * scale;
            item.Above = layout.Ascent * scale;
            item.Below = layout.Descent * scale;
            item.BoxSize = new Vector2(layout.Width * scale, (layout.Ascent + layout.Descent) * scale);
            return item;
        }

        // A style's own, the project's, the text's when it holds a table, a system math font, and then the text's anyway, spaced with no table.
        internal static FontFace ResolveFace(LetteraFontAsset own, FontFace text)
        {
            FontFace face = own != null ? own.LoadFace() : null;
            if (face != null) return face;
            LetteraFontAsset project = LetteraSettings.Instance.Math.Font;
            face = project != null ? project.LoadFace() : null;
            if (face != null) return face;
            if (text != null && !text.IsVirtual && NativeMathData.Available(text)) return text;
            if (LetteraNative.Version >= 3)
            {
                face = SystemMathFace();
                if (face != null) return face;
            }
            return text;
        }

        static bool s_SearchedSystem;
        static FontFace s_System;

        static FontFace SystemMathFace()
        {
            if (s_SearchedSystem) return s_System;
            s_SearchedSystem = true;
            foreach (string name in SystemFontNames)
            {
                FontFace face = LetteraSystemFonts.FindNamed(name);
                if (face != null && NativeMathData.Available(face))
                {
                    s_System = face;
                    break;
                }
            }
            return s_System;
        }

        static readonly string[] SystemFontNames = { "Cambria Math", "STIX Two Math", "Noto Sans Math", "DejaVu Math TeX Gyre", "Latin Modern Math" };
    }
}

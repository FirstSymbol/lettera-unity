using System;
using System.Collections.Generic;
using UnityEngine;

namespace FirstSymbol.Lettera
{
    /// <summary>Parsed tables by source, each keeping its last layout. Shared by every text, read and written on the main thread.</summary>
    internal static class LetteraTableCache
    {
        const int MaxModels = 256;
        static readonly Dictionary<string, TableModel> s_Models = new Dictionary<string, TableModel>();
        static readonly HashSet<string> s_Warned = new HashSet<string>();

        internal static int Count => s_Models.Count;

        internal static void Clear()
        {
            s_Models.Clear();
            s_Warned.Clear();
        }

        internal static TableModel Model(string source, int number)
        {
            string key = number + "\u0001" + source;
            if (s_Models.TryGetValue(key, out TableModel model)) return model;
            if (s_Models.Count >= MaxModels) s_Models.Clear();
            model = LetteraTableParser.Parse(source);
            model.Number = number;
            s_Models[key] = model;
            return model;
        }

        /// <summary>A table's layout for a room in em, made again only when the room, the fonts or the label change.</summary>
        internal static LetteraMathLayout Layout(TableModel model, float room, FontFace text, FontFace math, string label)
        {
            if (!float.IsInfinity(room)) room = Mathf.Floor(room * 64) / 64;
            int textId = text.Regular.Id, mathId = math != null ? math.Regular.Id : 0;
            if (model.Laid != null && (model.LaidRoom == room || (float.IsInfinity(room) && float.IsInfinity(model.LaidRoom))) && model.LaidText == textId && model.LaidMath == mathId && model.LaidLabel == label)
                return model.Laid;
            LetteraMathLayout layout = LetteraTableTypesetter.Lay(model, room, text, math, label);
            model.Laid = layout;
            model.LaidRoom = room;
            model.LaidText = textId;
            model.LaidMath = mathId;
            model.LaidLabel = label;
            if (model.Warning != null && s_Warned.Count < 64 && s_Warned.Add(model.Source))
                Debug.LogWarning("Lettera: " + model.Warning + " Table: " + (model.Source.Length > 64 ? model.Source.Substring(0, 64) + "…" : model.Source));
            return layout;
        }
    }

    /// <summary>
    /// LaTeX tables in text: tabular, tabular*, tabularx, tabulary and longtable, and a table float around one with its caption.
    /// What stands from \begin to its \end is the table's own: no tag and no escape means anything there. A tabular stands in the line;
    /// longtable and table stand on a line of their own. An environment with no \end stays text.
    /// </summary>
    [Serializable, UnityEngine.Scripting.Preserve]
    public class LetteraTableMatcher : LetteraMatcher
    {
        internal override bool StartsAtMarkup => true;

        public override int Priority => int.MaxValue - 1;

        public override void Find(LetteraMatchContext context)
        {
            ReadOnlySpan<char> text = context.Text;
            if (text.IndexOf("\\begin{".AsSpan(), StringComparison.Ordinal) < 0) return;
            string source = context.Source;
            // Each \begin of a table finds its \end through one stack, so an unclosed one costs one pass and stays text.
            var open = new List<(int at, string name)>();
            int captions = 0;
            for (int i = source.IndexOf('\\'); i >= 0 && i < source.Length; i = source.IndexOf('\\', i + 1))
            {
                bool begin = string.CompareOrdinal(source, i, "\\begin{", 0, 7) == 0;
                bool end = !begin && string.CompareOrdinal(source, i, "\\end{", 0, 5) == 0;
                if (!begin && !end)
                {
                    // A doubled backslash is one character, and the next cannot start a command.
                    if (i + 1 < source.Length && source[i + 1] == '\\') i++;
                    continue;
                }
                int nameStart = i + (begin ? 7 : 5);
                int close = source.IndexOf('}', nameStart, Math.Min(32, source.Length - nameStart));
                if (close < 0) continue;
                string name = source.Substring(nameStart, close - nameStart);
                if (!LetteraTableParser.IsTableEnvironment(name)) continue;
                if (begin)
                {
                    if (context.HasEscapes && OddBackslashesBefore(source, i)) continue;
                    open.Add((i, name));
                    i = close;
                    continue;
                }
                if (open.Count == 0 || open[open.Count - 1].name != name)
                {
                    i = close;
                    continue;
                }
                (int start, string outer) = open[open.Count - 1];
                open.RemoveAt(open.Count - 1);
                i = close;
                if (open.Count > 0) continue;
                int total = close + 1 - start;
                if (!context.IsFree(start, total)) continue;
                string table = source.Substring(start, total);
                bool block = LetteraTableParser.IsBlock(outer);
                int number = 0;
                if (table.IndexOf("\\caption", StringComparison.Ordinal) >= 0) number = ++captions;
                string replacement = "￼";
                if (block)
                {
                    bool lineBefore = start > 0 && source[start - 1] != '\n';
                    bool lineAfter = close + 1 < source.Length && source[close + 1] != '\n';
                    replacement = (lineBefore ? "\n" : "") + replacement + (lineAfter ? "\n" : "");
                }
                context.ReplaceAndRange(start, total, replacement, new[] { block ? "block" : "inline", number.ToString(System.Globalization.CultureInfo.InvariantCulture), table }, null);
            }
        }

        static bool OddBackslashesBefore(string source, int at)
        {
            int count = 0;
            while (at - count - 1 >= 0 && source[at - count - 1] == '\\') count++;
            return (count & 1) == 1;
        }
    }

    /// <summary>Draws a LaTeX table where its environment stood. Slots: block or inline, caption number, source.</summary>
    [Serializable, UnityEngine.Scripting.Preserve]
    public class LetteraTableEffect : InlineEffect
    {
        [SerializeField, Tooltip("What a caption's number follows, as in 'Table 1: ...'. Empty shows the caption alone.")] string m_CaptionLabel = "Table";

        public string CaptionLabel { get => m_CaptionLabel; set => m_CaptionLabel = value; }

        public override void Apply(LetteraAttributes attributes, int start, int end, in LetteraSlots slots)
        {
            base.Apply(attributes, start, end, slots);
            if (!string.Equals(slots[0], "block", StringComparison.Ordinal)) return;
            TableModel model = LetteraTableCache.Model(slots[2] ?? "", Number(slots));
            if (model.Centered)
                attributes.SetAlign(start, end, new AlignSpec { Alignment = (int)LetteraHorizontalAlignment.Center, MinWordSpace = 80, MinGlyphWidth = 100, MaxGlyphWidth = 100, MaxWordSpace = float.PositiveInfinity });
        }

        static int Number(in LetteraSlots slots) => int.TryParse(slots[1], System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out int n) ? n : 0;

        internal override TextEngine.InlineItem ResolveIn(TextEngine engine, in LetteraSlots slots, int cp)
        {
            string source = slots[2] ?? "";
            bool block = string.Equals(slots[0], "block", StringComparison.Ordinal);
            TableModel model = LetteraTableCache.Model(source, Number(slots));
            var item = new TextEngine.InlineItem { Effect = this, Cp = cp, Size = 1, MathDisplay = block, MathSource = source, Table = model, MathScale = 1 };
            FontFace text = engine.PrimaryFaceForMath;
            if (text == null || text.IsVirtual) return item;
            Fit(ref item, text, engine.TableRoomEm(engine.TableSize));
            return item;
        }

        internal void Fit(ref TextEngine.InlineItem item, FontFace text, float room)
        {
            FontFace math = null;
#if !LETTERA_NO_MATH
            math = LetteraMathEffect.ResolveFace(null, text);
#endif
            LetteraMathLayout layout = LetteraTableCache.Layout(item.Table, room, text, math, m_CaptionLabel);
            item.TableRoom = room;
            item.Math = layout;
            item.Advance = layout.Width;
            item.Above = layout.Ascent;
            item.Below = layout.Descent;
            item.BoxSize = new Vector2(layout.Width, layout.Ascent + layout.Descent);
        }
    }
}

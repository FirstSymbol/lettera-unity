using System;
using System.Globalization;
using System.Text;
using UnityEngine;

namespace FirstSymbol.Lettera.Editor
{
    /// <summary>A change to a text: it put <see cref="Inserted"/> characters where <see cref="Removed"/> were, at <see cref="At"/>.</summary>
    internal readonly struct LetteraTextEdit
    {
        public readonly string Text;
        public readonly int At, Removed, Inserted;
        public readonly bool Ok;

        public LetteraTextEdit(string text, int at, int removed, int inserted)
        {
            Text = text;
            At = at;
            Removed = removed;
            Inserted = inserted;
            Ok = true;
        }

        public static LetteraTextEdit None(string text) => new LetteraTextEdit();

        /// <summary>Where an offset of the old text lands in the new one. One inside the changed part goes to its end.</summary>
        public int Map(int offset)
        {
            if (offset <= At) return offset;
            if (offset >= At + Removed) return offset + Inserted - Removed;
            return At + Inserted;
        }
    }

    /// <summary>
    /// Writes one slot of an open tag as the least markup that says it. Every token the edit does not name stays byte for byte,
    /// quotes and spacing included, and trailing empty slots are dropped, so a tag with nothing set is the bare tag.
    /// </summary>
    internal static class LetteraTagWriter
    {
        /// <summary>Sets a slot to a value, or empties it with null. Fails only for a value no quote can carry.</summary>
        public static LetteraTextEdit SetSlot(string source, LetteraTagSpan tag, int slot, string value)
        {
            if (slot < 0) return default;
            string token = "";
            if (!string.IsNullOrEmpty(value) && !TryEncode(value, out token)) return default;

            int count = tag.Slots.Count;
            var raws = new string[Math.Max(count, slot + 1)];
            var seps = new string[raws.Length];
            for (int i = 0; i < raws.Length; i++)
            {
                raws[i] = i < count ? source.Substring(tag.Slots[i].Start, tag.Slots[i].End - tag.Slots[i].Start) : "";
                // What lay between this token and the next, spaces and comma, stays as it was.
                seps[i] = i + 1 < count ? source.Substring(tag.Slots[i].End, tag.Slots[i + 1].Start - tag.Slots[i].End) : ",";
            }
            raws[slot] = token;

            int last = raws.Length - 1;
            while (last >= 0 && raws[last].Length == 0) last--;

            if (tag.EqualsAt >= 0)
            {
                int regionEnd = tag.ValueEnd;
                if (last < 0) return Replace(source, tag.EqualsAt, regionEnd, "");
                string lead = source.Substring(tag.EqualsAt + 1, (count > 0 ? tag.Slots[0].Start : regionEnd) - tag.EqualsAt - 1);
                var text = new StringBuilder("=").Append(lead);
                for (int i = 0; i <= last; i++)
                {
                    text.Append(raws[i]);
                    if (i < last) text.Append(seps[i]);
                }
                return Replace(source, tag.EqualsAt, regionEnd, text.ToString());
            }
            if (last < 0) return LetteraTextEdit.None(source);
            var fresh = new StringBuilder("=");
            for (int i = 0; i <= last; i++)
            {
                fresh.Append(raws[i]);
                if (i < last) fresh.Append(',');
            }
            return Replace(source, tag.HeadEnd, tag.HeadEnd, fresh.ToString());
        }

        /// <summary>Every slot back to inherit: the bare tag.</summary>
        public static LetteraTextEdit Clear(string source, LetteraTagSpan tag) =>
            tag.EqualsAt < 0 ? LetteraTextEdit.None(source) : Replace(source, tag.EqualsAt, tag.ValueEnd, "");

        static LetteraTextEdit Replace(string source, int start, int end, string with)
        {
            if (with.Length == end - start && string.CompareOrdinal(source, start, with, 0, with.Length) == 0)
                return new LetteraTextEdit(source, start, 0, 0);
            return new LetteraTextEdit(source.Substring(0, start) + with + source.Substring(end), start, end - start, with.Length);
        }

        /// <summary>The token for a value: as it is when the tag reader would read it back whole, else in quotes. False when neither works.</summary>
        public static bool TryEncode(string value, out string token)
        {
            token = value ?? "";
            if (token.Length == 0) return true;
            bool edges = token != token.Trim();
            if (!edges && ReadsBack(token, value)) return true;
            if (value.IndexOf('"') < 0)
            {
                token = "\"" + value + "\"";
                if (ReadsBack(token, value)) return true;
            }
            if (value.IndexOf('\'') < 0)
            {
                token = "'" + value + "'";
                if (ReadsBack(token, value)) return true;
            }
            token = null;
            return false;
        }

        static bool ReadsBack(string token, string value)
        {
            string tag = "<x=" + token + ">";
            if (!TagSyntax.MatchOpen(tag.AsSpan(), 0, "x", out int length, out string[] slots, out _, out bool selfClosing) || length != tag.Length || selfClosing) return false;
            return slots != null && slots.Length == 1 && slots[0] == value;
        }

        /// <summary>A number the way a person writes it: invariant, no exponent, no trailing zeros.</summary>
        public static string FormatNumber(double value, int decimals = 4)
        {
            if (decimals < 0) decimals = 4;
            double rounded = Math.Round(value, Math.Min(decimals, 8), MidpointRounding.AwayFromZero);
            if (rounded == 0) return "0";
            string text = rounded.ToString("0.########", CultureInfo.InvariantCulture);
            return text;
        }

        /// <summary>#RRGGBB when opaque, else #RRGGBBAA.</summary>
        public static string FormatColor(Color32 color) =>
            color.a == 255 ? "#" + color.r.ToString("X2") + color.g.ToString("X2") + color.b.ToString("X2")
                : "#" + color.r.ToString("X2") + color.g.ToString("X2") + color.b.ToString("X2") + color.a.ToString("X2");
    }
}

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

namespace FirstSymbol.Lettera
{
    /// <summary>A set of OpenType feature settings, shared by every run that uses the same ones.</summary>
    internal sealed class FeatureSet
    {
        public uint[] Tags;
        public uint[] Values;
    }

    /// <summary>A set of variable font axis requests. Interned for the whole process, so its id can key font instances.</summary>
    internal sealed class AxisSet
    {
        public enum Mode : byte { Absolute, PercentOfDefault, DeltaFromDefault }

        public struct Entry
        {
            public uint Tag;
            public Mode Kind;
            public float Value;
        }

        static readonly Dictionary<string, AxisSet> s_All = new Dictionary<string, AxisSet>();

        public readonly int Id;
        public readonly Entry[] Entries;

        AxisSet(int id, Entry[] entries)
        {
            Id = id;
            Entries = entries;
        }

        /// <summary>The set with these entries; a later entry replaces an earlier one for the same axis.</summary>
        public static AxisSet Of(List<Entry> entries)
        {
            var merged = new List<Entry>(entries.Count);
            foreach (Entry entry in entries)
            {
                int at = merged.FindIndex(e => e.Tag == entry.Tag);
                if (at >= 0) merged[at] = entry;
                else merged.Add(entry);
            }
            merged.Sort((a, b) => a.Tag.CompareTo(b.Tag));
            var key = new StringBuilder();
            foreach (Entry e in merged) key.Append(e.Tag).Append(':').Append((int)e.Kind).Append(':').Append(e.Value.ToString("R", CultureInfo.InvariantCulture)).Append(';');
            string text = key.ToString();
            if (!s_All.TryGetValue(text, out AxisSet set))
            {
                set = new AxisSet(s_All.Count + 1, merged.ToArray());
                s_All[text] = set;
            }
            return set;
        }

        public bool Has(uint tag)
        {
            foreach (Entry entry in Entries)
                if (entry.Tag == tag)
                    return true;
            return false;
        }

        public AxisSet Over(AxisSet below)
        {
            if (below == null) return this;
            var entries = new List<Entry>(below.Entries);
            entries.AddRange(Entries);
            return Of(entries);
        }
    }

    public enum LetteraSuperSubPlacement : byte { Super = 1, Sub = 2 }

    /// <summary>Shapes the range in capitals, with simple locale-independent mappings. The rendered text keeps its case.</summary>
    [Serializable]
    public class UppercaseEffect : LetteraEffect
    {
        public override void Apply(LetteraAttributes attributes, int start, int end, in LetteraSlots slots) => attributes.SetCase(start, end, 1);
    }

    /// <summary>Shapes the range in small letters, with simple locale-independent mappings. The rendered text keeps its case.</summary>
    [Serializable]
    public class LowercaseEffect : LetteraEffect
    {
        public override void Apply(LetteraAttributes attributes, int start, int end, in LetteraSlots slots) => attributes.SetCase(start, end, 2);
    }

    /// <summary>The font's small capitals where it has them, otherwise scaled capitals. Slot: 1 scale of the synthetic ones, 0.1-1.</summary>
    [Serializable]
    public class SmallCapsEffect : LetteraEffect
    {
        [SerializeField, Range(0.1f, 1)] float m_SyntheticScale = 0.8f;

        public float SyntheticScale { get => m_SyntheticScale; set => m_SyntheticScale = value; }

        public override void Apply(LetteraAttributes attributes, int start, int end, in LetteraSlots slots) =>
            attributes.SetSmallCaps(start, end, Mathf.Clamp(slots.Float(0, m_SyntheticScale), 0.1f, 1f));
    }

    /// <summary>The font's superior or inferior glyphs where it has them, otherwise scaled and moved ones. Slot: 1 super or sub.</summary>
    [Serializable]
    public class SuperSubEffect : LetteraEffect
    {
        [SerializeField] LetteraSuperSubPlacement m_Placement = LetteraSuperSubPlacement.Super;

        public LetteraSuperSubPlacement Placement { get => m_Placement; set => m_Placement = value; }

        public override void Apply(LetteraAttributes attributes, int start, int end, in LetteraSlots slots) =>
            attributes.SetSuperSub(start, end, (byte)slots.Enum(0, m_Placement));
    }

    /// <summary>
    /// OpenType features, written as HarfBuzz writes them: onum, -dlig, +salt, cv02 3, kern=0. Nested ranges merge and
    /// the innermost value wins. A tag the font lacks does nothing; a malformed token is ignored.
    /// </summary>
    [Serializable]
    public class OpenTypeEffect : LetteraEffect
    {
        [SerializeField] string m_Features = "";

        public string Features { get => m_Features; set => m_Features = value; }

        public override void Apply(LetteraAttributes attributes, int start, int end, in LetteraSlots slots)
        {
            var tags = new List<uint>(4);
            var values = new List<uint>(4);
            bool any = false;
            for (int i = 0; i < slots.Count; i++)
            {
                string token = slots[i];
                if (string.IsNullOrEmpty(token)) continue;
                any = true;
                if (TryParse(token, out uint tag, out uint value))
                {
                    tags.Add(tag);
                    values.Add(value);
                }
            }
            if (!any && !string.IsNullOrEmpty(m_Features))
            {
                foreach (string token in m_Features.Split(','))
                    if (TryParse(token, out uint tag, out uint value))
                    {
                        tags.Add(tag);
                        values.Add(value);
                    }
            }
            if (tags.Count > 0) attributes.AddFeatures(start, end, tags, values);
        }

        internal static bool TryParse(string token, out uint tag, out uint value)
        {
            tag = 0;
            value = 1;
            token = token.Trim();
            if (token.Length == 0) return false;
            int at = 0;
            if (token[0] == '-') { value = 0; at = 1; }
            else if (token[0] == '+') at = 1;
            int end = at;
            while (end < token.Length && token[end] != ' ' && token[end] != ':' && token[end] != '=') end++;
            int length = end - at;
            if (length < 1 || length > 4) return false;
            for (int i = 0; i < 4; i++)
            {
                char c = i < length ? token[at + i] : ' ';
                if (c < 32 || c > 126) return false;
                tag = (tag << 8) | c;
            }
            if (end < token.Length)
            {
                string number = token.Substring(end + 1).Trim();
                if (!uint.TryParse(number, NumberStyles.None, CultureInfo.InvariantCulture, out value)) return false;
            }
            return true;
        }
    }

    /// <summary>
    /// Variable font axes. Slots are positional: wght, wdth, ital, slnt, opsz, or named pairs such as "GRAD 50" for any axis.
    /// A value is absolute (640), a percent of the axis default (120%), a delta from it (+80), or ~ to leave the axis alone.
    /// Values clamp to the axis; an axis the face lacks is ignored.
    /// </summary>
    [Serializable]
    public class AxesEffect : LetteraEffect
    {
        static readonly uint[] s_Positional = { LetteraNative.Tag("wght"), LetteraNative.Tag("wdth"), LetteraNative.Tag("ital"), LetteraNative.Tag("slnt"), LetteraNative.Tag("opsz") };

        [SerializeField] string m_Axes = "";

        public string Axes { get => m_Axes; set => m_Axes = value; }

        public override void Apply(LetteraAttributes attributes, int start, int end, in LetteraSlots slots)
        {
            var entries = new List<AxisSet.Entry>(4);
            bool any = false;
            for (int i = 0; i < slots.Count; i++)
            {
                string token = slots[i];
                if (token == null) continue;
                any = true;
                Add(entries, token, i);
            }
            if (!any && !string.IsNullOrEmpty(m_Axes))
            {
                string[] tokens = m_Axes.Split(',');
                for (int i = 0; i < tokens.Length; i++) Add(entries, tokens[i], i);
            }
            if (entries.Count > 0) attributes.AddAxes(start, end, entries);
        }

        internal static void Add(List<AxisSet.Entry> entries, string token, int position)
        {
            token = token.Trim();
            if (token.Length == 0 || token == "~") return;
            uint tag;
            string number = token;
            int space = token.IndexOf(' ');
            if (space > 0)
            {
                string name = token.Substring(0, space);
                if (name.Length > 4) return;
                tag = LetteraNative.Tag(name.PadRight(4));
                number = token.Substring(space + 1).Trim();
            }
            else
            {
                if (position >= s_Positional.Length) return;
                tag = s_Positional[position];
            }
            if (number.Length == 0) return;
            AxisSet.Mode mode = AxisSet.Mode.Absolute;
            if (number.EndsWith("%", StringComparison.Ordinal))
            {
                mode = AxisSet.Mode.PercentOfDefault;
                number = number.Substring(0, number.Length - 1);
            }
            else if (number[0] == '+' || number[0] == '-')
            {
                // A slant axis is negative by nature, so a bare negative number is a value there, not a delta.
                if (tag != s_Positional[3]) mode = AxisSet.Mode.DeltaFromDefault;
            }
            if (LetteraSlots.TryNumber(number, out float value)) entries.Add(new AxisSet.Entry { Tag = tag, Kind = mode, Value = value });
        }
    }

    /// <summary>Raises the atlas density of the range's glyphs, for crisp large headings. Grows only, and is shared with every text showing the glyph. Slot: 1 steps, 0-2.</summary>
    [Serializable]
    public class ResolutionEffect : LetteraEffect
    {
        [SerializeField, Range(0, 2)] int m_Boost = 1;

        public int Boost { get => m_Boost; set => m_Boost = value; }

        public override void Apply(LetteraAttributes attributes, int start, int end, in LetteraSlots slots) =>
            attributes.SetBoost(start, end, (byte)Mathf.Clamp(slots.Int(0, m_Boost), 0, 2));
    }

    /// <summary>Matches fallback faces to the primary on a metric, for the whole text, like CSS font-size-adjust. Slot: 1 XHeight or CapHeight.</summary>
    [Serializable]
    public class SizeMatchEffect : LetteraEffect
    {
        [SerializeField] LetteraSizeMatchMetric m_Metric;

        public LetteraSizeMatchMetric Metric { get => m_Metric; set => m_Metric = value; }

        public override void Apply(LetteraAttributes attributes, int start, int end, in LetteraSlots slots) =>
            attributes.SetSizeMatch(slots.Enum(0, m_Metric));
    }
}

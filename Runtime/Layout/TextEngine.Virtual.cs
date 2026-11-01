using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using FirstSymbol.Lettera.Unicode;
using UnityEngine;

namespace FirstSymbol.Lettera
{
    // List markers and ruby readings: shaped text that draws with a range's attributes, but is no part of the rendered text.
    internal sealed unsafe partial class TextEngine
    {
        struct ShapedInline
        {
            public FontInstance Instance;
            public float UnitEm;    // Em per font unit.
            public int First, Count;    // Into m_InlineGlyphs.
            public float WidthEm;
            public bool Rtl;
        }

        struct ListItem
        {
            public int Start;
            public ShapedInline Marker;
            public float IndentEm;  // The text's distance from the start edge.
            public float GapEm;
        }

        struct RubyItem
        {
            public RubySpec Spec;
            public ShapedInline Reading;
        }

        List<ShapedGlyph> m_InlineGlyphs = Rare<ShapedGlyph>.List;
        List<ListItem> m_ListItems = Rare<ListItem>.List;
        List<RubyItem> m_RubyItems = Rare<RubyItem>.List;
        float[] m_CpListIndent = Array.Empty<float>();
        const float MarkerGapEm = 0.4f;

        // Shapes a short string in the base's font, or in another font of the chain when that one lacks it.
        ShapedInline ShapeInline(string text, FontInstance preferred, float preferredUnit, UnicodeScript fallbackScript)
        {
            int count = 0;
            int* codepoints = stackalloc int[Math.Min(text.Length, 256) + 1];
            for (int i = 0; i < text.Length && count < 256; i++)
            {
                int cp = text[i];
                if (char.IsHighSurrogate(text[i]) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
                {
                    cp = char.ConvertToUtf32(text[i], text[i + 1]);
                    i++;
                }
                codepoints[count++] = cp;
            }
            var result = new ShapedInline { First = m_InlineGlyphs.Count };
            if (count == 0) return result;

            FontInstance instance = preferred;
            float unitEm = preferredUnit;
            if (instance != null)
            {
                bool covered = true;
                for (int i = 0; i < count && covered; i++) covered = instance.Face.HasGlyph(codepoints[i]) || LetteraUnicode.IsWhiteSpace(codepoints[i]);
                if (!covered) instance = null;
            }
            if (instance == null)
            {
                FontFace face = m_Chain.Resolve(new ReadOnlySpan<int>(codepoints, count), false, null, null, out FontChain.Family owner);
                if (face == null) return result;
                instance = face.Regular;
                unitEm = UnitEm(face, owner, m_Chain.PrimaryFace, m_Attributes.SizeMatchOverride);
            }
            if (instance.Face.IsVirtual) return result;

            UnicodeScript script = fallbackScript;
            bool rtl = false;
            for (int i = 0; i < count; i++)
            {
                UnicodeScript own = LetteraUnicode.GetScript(codepoints[i]);
                if (own == UnicodeScript.Common || own == UnicodeScript.Inherited) continue;
                script = own;
                rtl = own == UnicodeScript.Arabic || own == UnicodeScript.Hebrew || own == UnicodeScript.Syriac || own == UnicodeScript.Nko;
                break;
            }

            NativeGlyph* output = stackalloc NativeGlyph[Math.Min(count * 4 + 8, 2048)];
            int shaped = LetteraNative.lt_shape(instance.Font, codepoints, count, 0, count, rtl ? 5 : 4, LetteraUnicode.ScriptToOpenTypeTag(script), null, null, 0, output, Math.Min(count * 4 + 8, 2048));
            result.Instance = instance;
            result.UnitEm = unitEm;
            result.Rtl = rtl;
            for (int i = 0; i < shaped; i++)
            {
                NativeGlyph g = output[i];
                Own(ref m_InlineGlyphs).Add(new ShapedGlyph { Glyph = g.Glyph, Cluster = (int)g.Cluster, XAdvance = g.XAdvance, XOffset = g.XOffset, YOffset = g.YOffset });
                result.WidthEm += g.XAdvance * unitEm;
            }
            result.Count = Math.Max(shaped, 0);
            return result;
        }

        void PrepareVirtual()
        {
            m_InlineGlyphs.Clear();
            m_ListItems.Clear();
            m_RubyItems.Clear();
            EnsureSize(ref m_CpListIndent, m_TextLength + 1);
            Array.Clear(m_CpListIndent, 0, m_TextLength + 1);
            if (m_Attributes.ListItems.Count > 0) PrepareLists();
            if (m_Attributes.Rubies.Count > 0) PrepareRubies();
        }

        // ------------------------------------------------------------- lists

        void PrepareLists()
        {
            List<ListItemSpec> items = m_Attributes.ListItems;
            items.Sort((a, b) => a.Start.CompareTo(b.Start));
            int* counters = stackalloc int[16];
            for (int i = 0; i < 16; i++) counters[i] = 0;
            float* widest = stackalloc float[16];
            for (int i = 0; i < 16; i++) widest[i] = 0;
            int previousParagraph = -2;
            int listStart = 0;

            for (int n = 0; n < items.Count; n++)
            {
                ListItemSpec item = items[n];
                int paragraph = ParagraphAt(item.Start);
                bool adjacent = paragraph == previousParagraph + 1;
                previousParagraph = paragraph;
                if (!adjacent)
                {
                    // A plain paragraph ends the list: its widest markers and counters do not carry over.
                    FinishList(listStart, n, widest);
                    listStart = n;
                    for (int i = 0; i < 16; i++) { counters[i] = 0; widest[i] = 0; }
                }
                int level = Mathf.Min(item.Level, 15);
                for (int deeper = level + 1; deeper < 16; deeper++) counters[deeper] = 0;

                string text;
                if (!string.IsNullOrEmpty(item.Marker))
                {
                    text = item.Marker;
                }
                else if (item.Ordered)
                {
                    counters[level] = item.HasNumber ? item.Number : counters[level] + 1;
                    LetteraNumberStyle style = item.NumberStyles != null && item.NumberStyles.Count > 0 ? item.NumberStyles[level % item.NumberStyles.Count] : LetteraNumberStyle.Decimal;
                    string number = FormatNumber(counters[level], style);
                    // The numeral stays left to right; the punctuation changes its side with the paragraph.
                    text = item.Start < m_TextLength && (m_Levels[item.Start] & 1) != 0 ? "." + number : number + ".";
                }
                else
                {
                    text = item.Bullets != null && item.Bullets.Count > 0 ? item.Bullets[level % item.Bullets.Count] : "•";
                }

                FontInstance instance = null;
                float unit = 0;
                UnicodeScript script = UnicodeScript.Latin;
                if (item.Start < m_TextLength && m_Runs.Count > 0)
                {
                    Run run = m_Runs[m_CpRun[item.Start]];
                    instance = run.Instance;
                    unit = run.UnitEm;
                    script = run.Script;
                }
                ShapedInline marker = ShapeInline(text, instance, unit, script);
                if (marker.WidthEm > widest[level]) widest[level] = marker.WidthEm;
                Own(ref m_ListItems).Add(new ListItem { Start = item.Start, Marker = marker, GapEm = MarkerGapEm });
            }
            FinishList(listStart, items.Count, widest);
        }

        void FinishList(int first, int end, float* widest)
        {
            List<ListItemSpec> items = m_Attributes.ListItems;
            for (int n = first; n < end; n++)
            {
                ListItemSpec item = items[n];
                int level = Mathf.Min(item.Level, 15);
                float indent = 0;
                // A nested item indents under its own parent's column, not the widest sibling's.
                for (int l = 0; l < level; l++) indent += widest[l] + MarkerGapEm;
                ListItem built = m_ListItems[n];
                float own = item.Placement == LetteraListPlacement.Outside ? widest[level] : built.Marker.WidthEm;
                built.IndentEm = indent + own + MarkerGapEm;
                m_ListItems[n] = built;
                int stop = Math.Min(item.End, m_TextLength);
                for (int c = item.Start; c < stop; c++) m_CpListIndent[c] = built.IndentEm;
            }
        }

        int ParagraphAt(int cp)
        {
            for (int p = 0; p < m_Paragraphs.Count; p++)
                if (cp >= m_Paragraphs[p].Start && (cp < m_Paragraphs[p].End || m_Paragraphs[p].Start == m_Paragraphs[p].End))
                    return p;
            return -1;
        }

        static string FormatNumber(int value, LetteraNumberStyle style)
        {
            switch (style)
            {
                case LetteraNumberStyle.LowerAlpha: return Alpha(value, 'a');
                case LetteraNumberStyle.UpperAlpha: return Alpha(value, 'A');
                case LetteraNumberStyle.LowerRoman: return Roman(value).ToLowerInvariant();
                case LetteraNumberStyle.UpperRoman: return Roman(value);
                default: return value.ToString(CultureInfo.InvariantCulture);
            }
        }

        static string Alpha(int value, char first)
        {
            if (value <= 0) return value.ToString(CultureInfo.InvariantCulture);
            var builder = new StringBuilder();
            while (value > 0)
            {
                value--;
                builder.Insert(0, (char)(first + value % 26));
                value /= 26;
            }
            return builder.ToString();
        }

        static readonly (int value, string text)[] s_Roman = { (1000, "M"), (900, "CM"), (500, "D"), (400, "CD"), (100, "C"), (90, "XC"), (50, "L"), (40, "XL"), (10, "X"), (9, "IX"), (5, "V"), (4, "IV"), (1, "I") };

        static string Roman(int value)
        {
            if (value <= 0 || value >= 4000) return value.ToString(CultureInfo.InvariantCulture);
            var builder = new StringBuilder();
            foreach ((int amount, string text) in s_Roman)
            {
                while (value >= amount)
                {
                    builder.Append(text);
                    value -= amount;
                }
            }
            return builder.ToString();
        }

        void PlaceListMarkers()
        {
            foreach (ListItem item in m_ListItems)
            {
                if (item.Marker.Count == 0 || item.Start >= m_TextLength || IsRemoved(item.Start)) continue;
                for (int l = 0; l < m_Lines.Count; l++)
                {
                    LetteraLine line = m_Lines[l];
                    if (line.Start != item.Start) continue;
                    Run run = m_Runs[m_CpRun[item.Start]];
                    float size = SizeOf(run, CurrentFontSize);
                    bool rtl = (m_Paragraphs[line.Paragraph].Level & 1) != 0;
                    float width = item.Marker.WidthEm * size, gap = item.GapEm * size;
                    float x = rtl ? line.X + line.Width + gap : line.X - gap - width;
                    PlaceInline(item.Marker, x, line.Baseline, size, l, item.Start, 0);
                    break;
                }
            }
        }

        void PlaceInline(in ShapedInline shaped, float x, float baseline, float size, int line, int cluster, float extraPerCluster)
        {
            float unit = shaped.UnitEm * size;
            for (int g = shaped.First; g < shaped.First + shaped.Count; g++)
            {
                ShapedGlyph glyph = m_InlineGlyphs[g];
                Own(ref m_VirtualGlyphs).Add(new PlacedGlyph
                {
                    Glyph = glyph.Glyph, Instance = shaped.Instance, Cluster = cluster, Line = line,
                    X = x + glyph.XOffset * unit, Y = baseline + glyph.YOffset * unit, Unit = unit, Size = size,
                });
                x += glyph.XAdvance * unit;
                if (g + 1 < shaped.First + shaped.Count && m_InlineGlyphs[g + 1].Cluster != glyph.Cluster) x += extraPerCluster;
            }
        }

        // ------------------------------------------------------------- ruby

        void PrepareRubies()
        {
            foreach (RubySpec spec in m_Attributes.Rubies)
            {
                if (spec.Start >= m_TextLength) continue;
                Run run = m_Runs[m_CpRun[spec.Start]];
                ShapedInline reading = ShapeInline(spec.Reading, run.Instance, run.UnitEm, UnicodeScript.Common);
                float scaled = reading.WidthEm * spec.Scale;
                Own(ref m_RubyItems).Add(new RubyItem { Spec = spec, Reading = reading });

                // A reading wider than its base widens the base's space, centered.
                float baseWidth = 0;
                int end = Math.Min(spec.End, m_TextLength);
                for (int c = spec.Start; c < end; c++) baseWidth += m_CpAdvanceEm[c];
                float extra = scaled - baseWidth;
                if (extra <= 0) continue;
                int last = end - 1;
                while (last > spec.Start && !m_Boundaries[last]) last--;
                m_CpExtraEm[spec.Start] += extra * 0.5f;
                m_CpLeadEm[spec.Start] += extra * 0.5f;
                m_CpExtraEm[last] += extra * 0.5f;
            }
        }

        // How much a line grows for the readings on it. All of them share one baseline.
        void RubyExtents(int start, int end, float em, out float over, out float under, out float overDescent)
        {
            over = under = overDescent = 0;
            FontFace primary = m_Chain.PrimaryFace;
            float ascent = primary != null ? (float)primary.Metrics.Ascender / primary.Metrics.Upem : 0.8f;
            float descent = primary != null ? -(float)primary.Metrics.Descender / primary.Metrics.Upem : 0.2f;
            foreach (RubyItem item in m_RubyItems)
            {
                if (item.Spec.Start >= end || item.Spec.End <= start || item.Reading.Count == 0 || IsRemoved(item.Spec.Start)) continue;
                float height = (ascent + descent) * em * item.Spec.Scale;
                if (item.Spec.Position == LetteraRubyPosition.Over)
                {
                    over = Mathf.Max(over, height);
                    overDescent = Mathf.Max(overDescent, descent * em * item.Spec.Scale);
                }
                else
                {
                    under = Mathf.Max(under, height);
                }
            }
        }

        void PlaceRubies()
        {
            FontFace primary = m_Chain.PrimaryFace;
            float descent = primary != null ? -(float)primary.Metrics.Descender / primary.Metrics.Upem : 0.2f;
            float ascent = primary != null ? (float)primary.Metrics.Ascender / primary.Metrics.Upem : 0.8f;
            foreach (RubyItem item in m_RubyItems)
            {
                if (item.Reading.Count == 0 || IsRemoved(item.Spec.Start)) continue;
                float left = float.MaxValue, right = float.MinValue, size = 0;
                int lineIndex = -1;
                for (int i = 0; i < m_RealCount; i++)
                {
                    ref PlacedGlyph g = ref m_Placed[i];
                    if (g.Cluster < item.Spec.Start || g.Cluster >= item.Spec.End) continue;
                    if (lineIndex < 0) lineIndex = g.Line;
                    if (g.Line != lineIndex) continue;
                    left = Mathf.Min(left, g.X);
                    right = Mathf.Max(right, g.X + g.Advance);
                    size = Mathf.Max(size, g.Size);
                }
                if (lineIndex < 0) continue;

                LetteraLine line = m_Lines[lineIndex];
                float scaledSize = size * item.Spec.Scale;
                float width = item.Reading.WidthEm * scaledSize;
                float baseline = item.Spec.Position == LetteraRubyPosition.Over
                    ? line.Baseline + line.Ascent - line.RubyOver + descent * m_LineEm(line) * item.Spec.Scale
                    : line.Baseline - line.Descent + line.RubyUnder - ascent * m_LineEm(line) * item.Spec.Scale;

                float free = right - left - width;
                int clusters = ClustersOf(item.Reading);
                float x = left, gap = 0;
                switch (item.Spec.Align)
                {
                    case LetteraRubyAlign.Start: break;
                    case LetteraRubyAlign.Center: x = left + free * 0.5f; break;
                    case LetteraRubyAlign.SpaceBetween:
                        if (clusters > 1 && free > 0) gap = free / (clusters - 1);
                        else x = left + free * 0.5f;
                        break;
                    default:
                        if (free > 0)
                        {
                            gap = free / clusters;
                            x = left + gap * 0.5f;
                        }
                        else x = left + free * 0.5f;
                        break;
                }
                PlaceInline(item.Reading, x, baseline, scaledSize, lineIndex, item.Spec.Start, gap);
            }
        }

        float m_LineEm(in LetteraLine line)
        {
            float em = 0;
            for (int c = line.Start; c < line.End && c < m_TextLength; c++) em = Mathf.Max(em, SizeOf(m_Runs[m_CpRun[c]], CurrentFontSize));
            return em > 0 ? em : CurrentFontSize;
        }

        int ClustersOf(in ShapedInline shaped)
        {
            int clusters = 0, last = -1;
            for (int g = shaped.First; g < shaped.First + shaped.Count; g++)
            {
                if (m_InlineGlyphs[g].Cluster == last) continue;
                last = m_InlineGlyphs[g].Cluster;
                clusters++;
            }
            return Math.Max(clusters, 1);
        }
    }
}

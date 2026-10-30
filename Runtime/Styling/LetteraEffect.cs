using System;
using System.Globalization;
using UnityEngine;

namespace FirstSymbol.Lettera
{
    /// <summary>Rebuild levels, lowest to highest; a higher level includes all lower ones.</summary>
    public enum DirtyLevel : byte { None, Alpha, Tint, Mesh, Positions, Layout, Shape, Parse }

    /// <summary>
    /// Changes the appearance or layout of the ranges a matcher found. Custom effects derive from this
    /// class; they are serialized by reference.
    /// </summary>
    [Serializable]
    public abstract class LetteraEffect
    {
        /// <summary>Writes this effect's attributes for one range. Slots are the resolved parameter strings; any may be null.</summary>
        public abstract void Apply(LetteraAttributes attributes, int start, int end, in LetteraSlots slots);

        /// <summary>A copy for one text. Override when the effect holds a list or another nested object; references to assets stay shared.</summary>
        public virtual LetteraEffect Clone() => (LetteraEffect)MemberwiseClone();
    }

    /// <summary>A length token: 12, 12px, 0.5em, 40%, +2, -0.1em.</summary>
    public struct LetteraLength
    {
        public enum Unit : byte { Bare, Pixels, Em, Percent }

        public float Value;
        public Unit Kind;
        public bool IsDelta;

        public LetteraLength(float value, Unit kind = Unit.Bare)
        {
            Value = value;
            Kind = kind;
            IsDelta = false;
        }

        /// <summary>Reads a token. One that does not parse, or is not finite, counts as absent.</summary>
        public static bool TryParse(string token, out LetteraLength length)
        {
            length = default;
            if (string.IsNullOrEmpty(token)) return false;
            Unit kind = Unit.Bare;
            int digits = token.Length;
            if (token.EndsWith("%", StringComparison.Ordinal)) { kind = Unit.Percent; digits -= 1; }
            else if (token.EndsWith("px", StringComparison.OrdinalIgnoreCase)) { kind = Unit.Pixels; digits -= 2; }
            else if (token.EndsWith("em", StringComparison.OrdinalIgnoreCase)) { kind = Unit.Em; digits -= 2; }
            if (!LetteraSlots.TryNumber(digits == token.Length ? token : token.Substring(0, digits), out float value)) return false;
            length = new LetteraLength { Value = value, Kind = kind, IsDelta = token[0] == '+' || token[0] == '-' };
            return true;
        }

        /// <summary>The length in local units. A bare number counts as em or as local units, as the caller's parameter defines.</summary>
        public float ToUnits(float emSize, float percentBase, bool bareIsEm)
        {
            switch (Kind)
            {
                case Unit.Pixels: return Value;
                case Unit.Em: return Value * emSize;
                case Unit.Percent: return Value * 0.01f * percentBase;
                default: return bareIsEm ? Value * emSize : Value;
            }
        }
    }

    /// <summary>The positional parameters of one range: tag slot first, then the style's default.</summary>
    public readonly struct LetteraSlots
    {
        readonly string[] m_Tag;
        readonly string[] m_Default;

        /// <summary>The tag's whole value before it was split into slots, or null. Composite effects split it their own way.</summary>
        public string Raw { get; }

        internal LetteraSlots(string[] tag, string[] defaults, string raw = null)
        {
            m_Tag = tag;
            m_Default = defaults;
            Raw = raw;
        }

        /// <summary>How many positions the tag or the style's default gives.</summary>
        public int Count => Math.Max(m_Tag?.Length ?? 0, m_Default?.Length ?? 0);

        /// <summary>The number at a position, or a fallback when it is absent or unreadable.</summary>
        public float Float(int index, float fallback) => TryNumber(this[index], out float value) ? value : fallback;

        /// <summary>The whole number at a position, or a fallback.</summary>
        public int Int(int index, int fallback) => TryNumber(this[index], out float value) ? (int)value : fallback;

        /// <summary>The color at a position, or a fallback.</summary>
        public Color32 Color(int index, Color32 fallback) => TryColor(this[index], out Color32 value) ? value : fallback;

        /// <summary>The length at a position, or a fallback.</summary>
        public LetteraLength Length(int index, LetteraLength fallback) => LetteraLength.TryParse(this[index], out LetteraLength value) ? value : fallback;

        /// <summary>true, false, 1, 0, on or off at a position, or a fallback.</summary>
        public bool Bool(int index, bool fallback)
        {
            string value = this[index];
            if (string.IsNullOrEmpty(value)) return fallback;
            if (value == "1" || value.Equals("true", StringComparison.OrdinalIgnoreCase) || value.Equals("on", StringComparison.OrdinalIgnoreCase)) return true;
            if (value == "0" || value.Equals("false", StringComparison.OrdinalIgnoreCase) || value.Equals("off", StringComparison.OrdinalIgnoreCase)) return false;
            return fallback;
        }

        /// <summary>An enum member by name, or by an initial only that member has; else a fallback.</summary>
        public T Enum<T>(int index, T fallback) where T : struct, Enum => EnumTokens<T>.TryParse(this[index], out T value) ? value : fallback;

        static class EnumTokens<T> where T : struct, Enum
        {
            static readonly string[] s_Names = System.Enum.GetNames(typeof(T));
            static readonly T[] s_Values = (T[])System.Enum.GetValues(typeof(T));

            public static bool TryParse(string token, out T value)
            {
                value = default;
                if (string.IsNullOrEmpty(token)) return false;
                int initial = -1, initials = 0;
                for (int i = 0; i < s_Names.Length; i++)
                {
                    if (s_Names[i].Equals(token, StringComparison.OrdinalIgnoreCase))
                    {
                        value = s_Values[i];
                        return true;
                    }
                    if (token.Length == 1 && char.ToLowerInvariant(s_Names[i][0]) == char.ToLowerInvariant(token[0]))
                    {
                        initial = i;
                        initials++;
                    }
                }
                if (initials != 1) return false;
                value = s_Values[initial];
                return true;
            }
        }

        /// <summary>The value at a position: the tag's, else the style's default, else null.</summary>
        public string this[int index]
        {
            get
            {
                if (m_Tag != null && index < m_Tag.Length && m_Tag[index] != null) return m_Tag[index];
                if (m_Default != null && index < m_Default.Length) return m_Default[index];
                return null;
            }
        }

        /// <summary>Parses a number with invariant culture. NaN, infinity and huge values count as absent.</summary>
        public static bool TryNumber(string value, out float number)
        {
            number = 0;
            if (string.IsNullOrEmpty(value)) return false;
            if (!float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out number)) return false;
            return !float.IsNaN(number) && !float.IsInfinity(number) && Mathf.Abs(number) <= 10000000f;
        }

        /// <summary>Parses #RGB, #RRGGBB, #RRGGBBAA or a named color.</summary>
        public static bool TryColor(string value, out Color32 color)
        {
            color = new Color32(255, 255, 255, 255);
            if (string.IsNullOrEmpty(value)) return false;
            if (value[0] == '#')
            {
                int digits = value.Length - 1;
                if (digits != 3 && digits != 4 && digits != 6 && digits != 8) return false;
                uint v = 0;
                for (int i = 1; i < value.Length; i++)
                {
                    int d = Hex(value[i]);
                    if (d < 0) return false;
                    v = (v << 4) | (uint)d;
                }
                if (digits == 3) v = (v << 4) | 0xF;
                if (digits <= 4)
                {
                    uint r = (v >> 12) & 15, g = (v >> 8) & 15, b = (v >> 4) & 15, a = v & 15;
                    color = new Color32((byte)(r * 17), (byte)(g * 17), (byte)(b * 17), (byte)(a * 17));
                    return true;
                }
                if (digits == 6) v = (v << 8) | 0xFF;
                color = new Color32((byte)(v >> 24), (byte)(v >> 16), (byte)(v >> 8), (byte)v);
                return true;
            }
            switch (value.ToLowerInvariant())
            {
                case "white": color = new Color32(255, 255, 255, 255); return true;
                case "black": color = new Color32(0, 0, 0, 255); return true;
                case "red": color = new Color32(255, 0, 0, 255); return true;
                case "green": color = new Color32(0, 128, 0, 255); return true;
                case "blue": color = new Color32(0, 0, 255, 255); return true;
                case "yellow": color = new Color32(255, 255, 0, 255); return true;
                case "cyan": color = new Color32(0, 255, 255, 255); return true;
                case "magenta": color = new Color32(255, 0, 255, 255); return true;
                case "orange": color = new Color32(255, 165, 0, 255); return true;
                case "purple": color = new Color32(128, 0, 128, 255); return true;
                case "gray": case "grey": color = new Color32(128, 128, 128, 255); return true;
                case "lime": color = new Color32(0, 255, 0, 255); return true;
                case "brown": color = new Color32(165, 42, 42, 255); return true;
                case "pink": color = new Color32(255, 192, 203, 255); return true;
                case "navy": color = new Color32(0, 0, 128, 255); return true;
                case "teal": color = new Color32(0, 128, 128, 255); return true;
                case "olive": color = new Color32(128, 128, 0, 255); return true;
                case "maroon": color = new Color32(128, 0, 0, 255); return true;
                case "silver": color = new Color32(192, 192, 192, 255); return true;
                case "gold": color = new Color32(255, 215, 0, 255); return true;
                default: return false;
            }
        }

        static int Hex(char c)
        {
            if (c >= '0' && c <= '9') return c - '0';
            if (c >= 'a' && c <= 'f') return c - 'a' + 10;
            if (c >= 'A' && c <= 'F') return c - 'A' + 10;
            return -1;
        }
    }

    /// <summary>Per-codepoint attributes effects write to. Indices are rendered codepoints.</summary>
    public sealed class LetteraAttributes
    {
        internal struct Cp
        {
            public float SizeAbsolute, SizeMul, SizeAdd;
            public short Weight;
            public byte BoldMode;       // 0 auto, 1 force synthetic, 2 real faces only.
            public bool Italic;
            public float Slant;
            public Color32 Color;
            public bool HasColor;
            public float Alpha;
            public short Family;        // Index into FamilyRequests, -1 none.
            public short Language;      // Index into Languages, -1 none.
            public ushort Underline, Strike; // 1-based indices into Decorations.
            public short Features, Axes;    // Indices into FeatureSets and AxisSets, -1 none.
            public byte CaseMap;            // 0 none, 1 upper, 2 lower.
            public byte SuperSub;           // 0 none, 1 super, 2 sub.
            public byte Boost;
            public float SmallCaps;         // The scale of synthetic small capitals, 0 off.
        }

        internal struct DecorationSpec
        {
            public bool HasColor;
            public Color32 Color;
        }

        internal struct ElideRequest
        {
            public int Start, End;
            public float Position;
            public bool Marker;
        }

        internal Cp[] Chars = new Cp[64];
        internal int Length;
        internal readonly System.Collections.Generic.List<string> FamilyRequests = new System.Collections.Generic.List<string>();
        internal readonly System.Collections.Generic.List<string> Languages = new System.Collections.Generic.List<string>();
        internal readonly System.Collections.Generic.List<DecorationSpec> Decorations = new System.Collections.Generic.List<DecorationSpec>();
        internal readonly System.Collections.Generic.List<LayerRecord> Layers = new System.Collections.Generic.List<LayerRecord>();
        readonly System.Collections.Generic.List<LayerGroup> m_Groups = new System.Collections.Generic.List<LayerGroup>();
        readonly System.Collections.Generic.List<LayerGroup> m_GroupPool = new System.Collections.Generic.List<LayerGroup>();
        object m_CurrentEffect;
        int m_CurrentStyle;
        bool m_GroupsSorted;
        internal readonly System.Collections.Generic.List<ElideRequest> Elides = new System.Collections.Generic.List<ElideRequest>();

        internal readonly System.Collections.Generic.List<FeatureSet> FeatureSets = new System.Collections.Generic.List<FeatureSet>();
        readonly System.Collections.Generic.Dictionary<string, short> m_FeatureIds = new System.Collections.Generic.Dictionary<string, short>();
        internal readonly System.Collections.Generic.List<AxisSet> AxisSets = new System.Collections.Generic.List<AxisSet>();

        /// <summary>The metric fallback faces match on for this text, or -1 for the project's.</summary>
        internal int SizeMatchOverride = -1;

        /// <summary>A base direction forced by an effect: -1 none, 0 ltr, 1 rtl, 2 auto.</summary>
        internal int DirectionOverride = -1;

        internal void Reset(int length)
        {
            if (Chars.Length < length) Chars = new Cp[Mathf.Max(length, Chars.Length * 2)];
            Length = length;
            for (int i = 0; i < length; i++)
                Chars[i] = new Cp { SizeMul = 1, Weight = 0, Alpha = 1, Family = -1, Language = -1, Slant = -1, Features = -1, Axes = -1 };
            FeatureSets.Clear();
            m_FeatureIds.Clear();
            AxisSets.Clear();
            SizeMatchOverride = -1;
            FamilyRequests.Clear();
            Languages.Clear();
            Decorations.Clear();
            Layers.Clear();
            m_GroupPool.AddRange(m_Groups);
            m_Groups.Clear();
            m_GroupsSorted = false;
            m_CurrentEffect = null;
            Elides.Clear();
            DirectionOverride = -1;
        }

        /// <summary>Length of the rendered text in codepoints.</summary>
        public int TextLength => Length;

        void Clamp(ref int start, ref int end)
        {
            start = Mathf.Clamp(start, 0, Length);
            end = Mathf.Clamp(end, start, Length);
        }

        public void SetWeight(int start, int end, int weight, int mode)
        {
            Clamp(ref start, ref end);
            for (int i = start; i < end; i++)
            {
                Chars[i].Weight = (short)weight;
                Chars[i].BoldMode = (byte)mode;
            }
        }

        public void SetItalic(int start, int end, float slantPercent)
        {
            Clamp(ref start, ref end);
            for (int i = start; i < end; i++)
            {
                Chars[i].Italic = true;
                Chars[i].Slant = slantPercent;
            }
        }

        /// <summary>Sets an absolute size in local units.</summary>
        public void SetSizeAbsolute(int start, int end, float size)
        {
            Clamp(ref start, ref end);
            for (int i = start; i < end; i++)
            {
                Chars[i].SizeAbsolute = size;
                Chars[i].SizeMul = 1;
                Chars[i].SizeAdd = 0;
            }
        }

        /// <summary>Scales the enclosing size.</summary>
        public void MultiplySize(int start, int end, float factor)
        {
            Clamp(ref start, ref end);
            for (int i = start; i < end; i++)
            {
                Chars[i].SizeMul *= factor;
                Chars[i].SizeAdd *= factor;
            }
        }

        /// <summary>Adds to the enclosing size.</summary>
        public void AddSize(int start, int end, float delta)
        {
            Clamp(ref start, ref end);
            for (int i = start; i < end; i++) Chars[i].SizeAdd += delta;
        }

        public void SetColor(int start, int end, Color32 color)
        {
            Clamp(ref start, ref end);
            for (int i = start; i < end; i++)
            {
                Chars[i].Color = color;
                Chars[i].HasColor = true;
            }
        }

        /// <summary>The innermost range's alpha applies; ranges do not compound.</summary>
        public void SetAlpha(int start, int end, float alpha)
        {
            Clamp(ref start, ref end);
            for (int i = start; i < end; i++) Chars[i].Alpha = alpha;
        }

        public void SetFamily(int start, int end, string family)
        {
            Clamp(ref start, ref end);
            short index = Intern(FamilyRequests, family);
            for (int i = start; i < end; i++) Chars[i].Family = index;
        }

        public void SetLanguage(int start, int end, string language)
        {
            Clamp(ref start, ref end);
            short index = Intern(Languages, language);
            for (int i = start; i < end; i++) Chars[i].Language = index;
        }

        public void SetUnderline(int start, int end, bool hasColor, Color32 color)
        {
            Clamp(ref start, ref end);
            Decorations.Add(new DecorationSpec { HasColor = hasColor, Color = color });
            ushort index = (ushort)Mathf.Min(Decorations.Count, ushort.MaxValue);
            for (int i = start; i < end; i++) Chars[i].Underline = index;
        }

        public void SetStrike(int start, int end, bool hasColor, Color32 color)
        {
            Clamp(ref start, ref end);
            Decorations.Add(new DecorationSpec { HasColor = hasColor, Color = color });
            ushort index = (ushort)Mathf.Min(Decorations.Count, ushort.MaxValue);
            for (int i = start; i < end; i++) Chars[i].Strike = index;
        }

        public void SetOutline(int start, int end, Color32 color, float widthEm, float widthUnits)
        {
            AddLayer(start, end, LetteraLayerBand.Rim, new LayerRecord
            {
                Kind = LayerKind.Stroke,
                Paint = new ResolvedPaint { Color = color, Blend = LetteraBlend.Normal },
                Width = new EmLength { Em = widthEm, Units = widthUnits },
                Align = 1,
            });
        }

        public void SetShadow(int start, int end, Color32 color, float offsetX, float offsetY, float softness, float dilate)
        {
            AddLayer(start, end, LetteraLayerBand.Back, new LayerRecord
            {
                Kind = LayerKind.Shadow,
                Paint = new ResolvedPaint { Color = color, Blend = LetteraBlend.Normal },
                OffsetX = new EmLength { Em = offsetX },
                OffsetY = new EmLength { Em = offsetY },
                Softness = softness,
                Dilate = new EmLength { Em = dilate },
                OnColorGlyphs = true,
            });
        }

        internal void SetCase(int start, int end, byte map)
        {
            Clamp(ref start, ref end);
            for (int i = start; i < end; i++) Chars[i].CaseMap = map;
        }

        internal void SetSmallCaps(int start, int end, float scale)
        {
            Clamp(ref start, ref end);
            for (int i = start; i < end; i++) Chars[i].SmallCaps = scale;
        }

        internal void SetSuperSub(int start, int end, byte placement)
        {
            Clamp(ref start, ref end);
            for (int i = start; i < end; i++) Chars[i].SuperSub = placement;
        }

        internal void SetBoost(int start, int end, byte boost)
        {
            Clamp(ref start, ref end);
            for (int i = start; i < end; i++) Chars[i].Boost = boost;
        }

        internal void SetSizeMatch(LetteraSizeMatchMetric metric) => SizeMatchOverride = (int)metric;

        /// <summary>Merges features into the range: the innermost value wins for a tag. Equal sets share an id, so they do not split runs.</summary>
        internal void AddFeatures(int start, int end, System.Collections.Generic.List<uint> tags, System.Collections.Generic.List<uint> values)
        {
            Clamp(ref start, ref end);
            short lastOld = -2, lastNew = -1;
            for (int i = start; i < end; i++)
            {
                short old = Chars[i].Features;
                if (old != lastOld)
                {
                    lastOld = old;
                    lastNew = MergeFeatures(old, tags, values);
                }
                Chars[i].Features = lastNew;
            }
        }

        short MergeFeatures(short existing, System.Collections.Generic.List<uint> tags, System.Collections.Generic.List<uint> values)
        {
            var merged = new System.Collections.Generic.SortedDictionary<uint, uint>();
            if (existing >= 0)
            {
                FeatureSet set = FeatureSets[existing];
                for (int i = 0; i < set.Tags.Length; i++) merged[set.Tags[i]] = set.Values[i];
            }
            for (int i = 0; i < tags.Count; i++) merged[tags[i]] = values[i];

            var key = new System.Text.StringBuilder();
            foreach (var pair in merged) key.Append(pair.Key).Append('=').Append(pair.Value).Append(';');
            string text = key.ToString();
            if (m_FeatureIds.TryGetValue(text, out short id)) return id;

            var result = new FeatureSet { Tags = new uint[merged.Count], Values = new uint[merged.Count] };
            merged.Keys.CopyTo(result.Tags, 0);
            merged.Values.CopyTo(result.Values, 0);
            id = (short)FeatureSets.Count;
            FeatureSets.Add(result);
            m_FeatureIds[text] = id;
            return id;
        }

        /// <summary>Merges axis requests into the range: the innermost value wins for an axis.</summary>
        internal void AddAxes(int start, int end, System.Collections.Generic.List<AxisSet.Entry> entries)
        {
            Clamp(ref start, ref end);
            AxisSet request = AxisSet.Of(entries);
            short lastOld = -2, lastNew = -1;
            for (int i = start; i < end; i++)
            {
                short old = Chars[i].Axes;
                if (old != lastOld)
                {
                    lastOld = old;
                    AxisSet merged = old >= 0 ? request.Over(AxisSets[old]) : request;
                    int index = AxisSets.IndexOf(merged);
                    if (index < 0)
                    {
                        index = AxisSets.Count;
                        AxisSets.Add(merged);
                    }
                    lastNew = (short)index;
                }
                Chars[i].Axes = lastNew;
            }
        }

        /// <summary>Called before an effect applies a range: its layers belong to this effect.</summary>
        internal void BeginEffect(object effect, int style)
        {
            m_CurrentEffect = effect;
            m_CurrentStyle = style;
        }

        /// <summary>
        /// Adds a painted range. Inside one effect the innermost range wins for a codepoint; different
        /// effects both draw. Later styles draw above earlier ones in a band.
        /// </summary>
        internal void AddLayer(int start, int end, LetteraLayerBand band, in LayerRecord record)
        {
            Clamp(ref start, ref end);
            LayerGroup group = GroupOf(band, record.Kind);
            int index = Layers.Count;
            LayerRecord copy = record;
            copy.Start = start;
            copy.End = end;
            Layers.Add(copy);
            int[] cover = group.Cover;
            for (int i = start; i < end; i++) cover[i] = index + 1;
        }

        LayerGroup GroupOf(LetteraLayerBand band, LayerKind kind)
        {
            for (int i = 0; i < m_Groups.Count; i++)
            {
                LayerGroup existing = m_Groups[i];
                if (existing.Key == m_CurrentEffect && existing.Layer == kind) return existing;
            }
            LayerGroup group;
            if (m_GroupPool.Count > 0)
            {
                group = m_GroupPool[m_GroupPool.Count - 1];
                m_GroupPool.RemoveAt(m_GroupPool.Count - 1);
            }
            else
            {
                group = new LayerGroup();
            }
            if (group.Cover == null || group.Cover.Length < Length) group.Cover = new int[Mathf.Max(Length, 64)];
            else System.Array.Clear(group.Cover, 0, Length);
            group.Key = m_CurrentEffect;
            group.Band = band;
            group.Layer = kind;
            group.Style = m_CurrentStyle;
            group.Sequence = m_Groups.Count;
            m_Groups.Add(group);
            m_GroupsSorted = false;
            return group;
        }

        /// <summary>The groups in drawing order: by band, then style.</summary>
        internal System.Collections.Generic.List<LayerGroup> SortedGroups()
        {
            if (!m_GroupsSorted)
            {
                m_GroupsSorted = true;
                m_Groups.Sort(s_GroupOrder);
            }
            return m_Groups;
        }

        static readonly Comparison<LayerGroup> s_GroupOrder = (a, b) =>
        {
            if (a.Band != b.Band) return a.Band.CompareTo(b.Band);
            return a.Style != b.Style ? a.Style.CompareTo(b.Style) : a.Sequence.CompareTo(b.Sequence);
        };

        internal bool NeedsStyledShader()
        {
            for (int i = 0; i < Layers.Count; i++)
            {
                LayerRecord record = Layers[i];
                if (!record.Paint.IsSolid || record.Kind == LayerKind.InnerShadow || (record.Kind == LayerKind.Stroke && record.Align < 1)) return true;
                if (record.Kind == LayerKind.Fill && record.Paint.Color.a < 255) return true;
            }
            return false;
        }

        /// <summary>Asks layout to remove clusters of the range at a position (0 start, 0.5 middle, 1 end) when the text does not fit.</summary>
        public void RequestElide(int start, int end, float position, bool marker)
        {
            Clamp(ref start, ref end);
            Elides.Add(new ElideRequest { Start = start, End = end, Position = Mathf.Clamp01(position), Marker = marker });
        }

        /// <summary>Forces the base direction of every paragraph: 0 ltr, 1 rtl, 2 auto.</summary>
        public void SetDirection(int direction) => DirectionOverride = direction;

        static short Intern(System.Collections.Generic.List<string> list, string value)
        {
            for (int i = 0; i < list.Count; i++)
                if (list[i] == value)
                    return (short)i;
            list.Add(value);
            return (short)(list.Count - 1);
        }
    }
}

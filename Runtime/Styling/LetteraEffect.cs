using System;
using System.Globalization;
using UnityEngine;

namespace FirstSymbol.Lettera
{
    /// <summary>Rebuild levels, lowest to highest; a higher level includes all lower ones.</summary>
    public enum DirtyLevel : byte { None, Alpha, Tint, Pose, Mesh, Positions, Layout, Shape, Parse }

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

        /// <summary>The parameters this effect publishes, so rules, cues and an inspector can reach them by name.</summary>
        public virtual System.Collections.Generic.IReadOnlyList<LetteraParamInfo> Parameters => s_NoParameters;

        static readonly LetteraParamInfo[] s_NoParameters = new LetteraParamInfo[0];

        /// <summary>Whether text typed at the end of this effect's range joins it. A link does not.</summary>
        public virtual bool ExtendsOnType => true;

        /// <summary>Which stages of a rebuild this effect takes part in. Only those are called, and a text with none pays nothing.</summary>
        public virtual LetteraStages Stages => LetteraStages.None;

        /// <summary>After Unicode analysis. Called once per range the effect covers, as are the others.</summary>
        public virtual void OnAnalyzed(ref LetteraAnalysisView view) { }

        /// <summary>After shaping.</summary>
        public virtual void OnShaped(ref LetteraShapedView view) { }

        /// <summary>After lines are broken, before they are placed.</summary>
        public virtual void OnLinesBroken(ref LetteraLinesView view) { }

        /// <summary>After glyphs are placed, before the mesh.</summary>
        public virtual void OnPlaced(ref LetteraPlacedView view) { }

        /// <summary>After layers are drawn.</summary>
        public virtual void OnMesh(ref LetteraMeshView view) { }

        /// <summary>Each tick.</summary>
        public virtual void OnPose(ref LetteraPoseView view) { }

        /// <summary>The effects this one runs inside it.</summary>
        internal virtual void CollectChildren(System.Collections.Generic.List<LetteraEffect> results) { }
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
        readonly ValueList<HoldEntry> m_Holds;
        readonly object m_Owner;

        /// <summary>The tag's whole value before it was split into slots, or null. Composite effects split it their own way.</summary>
        public string Raw { get; }

        /// <summary>The style's default parameter before it was split, or null.</summary>
        public string DefaultRaw { get; }

        internal LetteraSlots(string[] tag, string[] defaults, string raw = null, string defaultRaw = null, ValueList<HoldEntry> holds = null, object owner = null)
        {
            m_Tag = tag;
            m_Default = defaults;
            Raw = raw;
            DefaultRaw = defaultRaw;
            m_Holds = holds;
            m_Owner = owner;
        }

        /// <summary>The same slots with what holds say left out, for reading again later.</summary>
        internal LetteraSlots WithoutHolds() => new LetteraSlots(m_Tag, m_Default, Raw, DefaultRaw, null, m_Owner);

        /// <summary>The slots of a child effect: its own segment of the tag and of the default.</summary>
        internal LetteraSlots ForChild(object child, string segment, string fallback) =>
            new LetteraSlots(TagSyntax.SplitSlots(segment), TagSyntax.SplitSlots(fallback), segment, fallback, m_Holds, child);

        // The value a hold composes over a number, or the number itself.
        float HeldNumber(int index, float underlying)
        {
            if (m_Holds == null) return underlying;
            foreach (HoldEntry hold in m_Holds)
            {
                if (hold.Slot != index || !hold.HasNumber || hold.Effect != m_Owner) continue;
                underlying = hold.Composition == LetteraHoldComposition.Add ? underlying + hold.Number * hold.Weight
                    : hold.Composition == LetteraHoldComposition.Multiply ? underlying * Mathf.LerpUnclamped(1, hold.Number, hold.Weight)
                    : Mathf.LerpUnclamped(underlying, hold.Number, hold.Weight);
            }
            return underlying;
        }

        Color32 HeldColor(int index, Color32 underlying)
        {
            if (m_Holds == null) return underlying;
            foreach (HoldEntry hold in m_Holds)
            {
                if (hold.Slot != index || !hold.HasColor || hold.Effect != m_Owner) continue;
                underlying = hold.Composition == LetteraHoldComposition.Multiply ? (Color32)((UnityEngine.Color)underlying * UnityEngine.Color.LerpUnclamped(UnityEngine.Color.white, hold.Color, hold.Weight))
                    : Color32.LerpUnclamped(underlying, hold.Color, hold.Weight);
            }
            return underlying;
        }

        // What the tag or default says, under any hold.
        string Unheld(int index)
        {
#if UNITY_EDITOR
            Probe?.Invoke(index);
#endif
            if (m_Tag != null && index < m_Tag.Length && m_Tag[index] != null) return m_Tag[index];
            if (m_Default != null && index < m_Default.Length) return m_Default[index];
            return null;
        }

#if UNITY_EDITOR
        /// <summary>Set by a test while one effect applies, to learn which positions it reads. Compiled out of players.</summary>
        internal static Action<int> Probe;
#endif

        /// <summary>How many positions the tag or the style's default gives.</summary>
        public int Count => Math.Max(m_Tag?.Length ?? 0, m_Default?.Length ?? 0);

        /// <summary>The number at a position, or a fallback when it is absent or unreadable.</summary>
        public float Float(int index, float fallback) => HeldNumber(index, TryNumber(Unheld(index), out float value) ? value : fallback);

        /// <summary>The whole number at a position, or a fallback.</summary>
        public int Int(int index, int fallback) => (int)HeldNumber(index, TryNumber(Unheld(index), out float value) ? value : fallback);

        /// <summary>The color at a position, or a fallback.</summary>
        public Color32 Color(int index, Color32 fallback) => HeldColor(index, TryColor(Unheld(index), out Color32 value) ? value : fallback);

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
#if UNITY_EDITOR
                Probe?.Invoke(index);
#endif
                if (m_Holds != null)
                {
                    // Reading a token gives what a replacing hold holds; composing ones need a typed reader, which knows the field.
                    string held = null;
                    foreach (HoldEntry hold in m_Holds)
                        if (hold.Slot == index && hold.Effect == m_Owner && hold.Composition == LetteraHoldComposition.Replace && hold.Weight >= 0.5f)
                            held = hold.Token;
                    if (held != null) return held;
                }
                return Unheld(index);
            }
        }

        /// <summary>Parses a number with invariant culture. NaN, infinity and huge values count as absent.</summary>
        /// <summary>Reads true or false, and 1 or 0.</summary>
        public static bool TryBool(string value, out bool flag)
        {
            flag = false;
            if (string.IsNullOrEmpty(value)) return false;
            if (value == "true" || value == "1") return flag = true;
            return value == "false" || value == "0";
        }

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
        /// <summary>A range's value in a channel core knows nothing of, for an effect that a module and its host agree on.</summary>
        internal struct ExtraRange
        {
            public object Key;
            public int Start, End;
            public float Value;
            public object Payload;
        }

        internal readonly ValueList<ExtraRange> Extras = new ValueList<ExtraRange>();

        internal void AddExtra(object key, int start, int end, float value, object payload) => Extras.Add(new ExtraRange { Key = key, Start = start, End = end, Value = value, Payload = payload });

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
            public bool Union;
            public short Warp;              // Index into Warps, -1 none.
        }

        /// <summary>Layout attributes, kept apart so a text with no layout effects pays nothing for them.</summary>
        internal struct CpLayout
        {
            public LayoutLength Tracking, WordSpacing, Cell;
            public bool CellAuto;
            public int CellGroup;           // Ranges of one auto cell measure together; 0 none.
            public LayoutLength Indent;
            public float IndentPercent;
            public short LineHeight, ParagraphSpacing, Align; // Indices into their lists, -1 none.
        }

        internal CpLayout[] Layout = new CpLayout[0];
        internal bool HasLayout;
        internal readonly ValueList<LineHeightSpec> LineHeights = new ValueList<LineHeightSpec>();
        internal readonly ValueList<ParagraphSpacingSpec> ParagraphSpacings = new ValueList<ParagraphSpacingSpec>();
        internal readonly ValueList<LineGapSpec> LineGaps = new ValueList<LineGapSpec>();
        internal readonly ValueList<AlignSpec> Aligns = new ValueList<AlignSpec>();
        internal readonly System.Collections.Generic.List<Vector2Int> NoBreaks = new System.Collections.Generic.List<Vector2Int>();
        internal LetteraTrimTop TrimTop;
        internal LetteraTrimBottom TrimBottom;
        int m_CellGroups;

        internal struct DecorationSpec
        {
            public ResolvedPaint Paint;
            public LetteraLineStyle Style;
            public EmLength Thickness, Offset;
            public bool AutoThickness, AutoOffset, SkipInk, Overlay;
            public Rect RangeRect;
        }

        internal struct ElideRequest
        {
            public int Start, End;
            public float Position;
            public bool Marker;
            public LayoutLength FadeDistance, FadeShift;
        }

        internal Cp[] Chars = System.Array.Empty<Cp>();
        internal int Length;

        // A long text with no range holds one set of defaults for all its characters, not one per character.
        internal bool Uniform;

        internal ref Cp At(int i) => ref Chars[Uniform ? 0 : i];
        internal readonly System.Collections.Generic.List<string> FamilyRequests = new System.Collections.Generic.List<string>();
        internal readonly System.Collections.Generic.List<string> Languages = new System.Collections.Generic.List<string>();
        internal readonly ValueList<DecorationSpec> Decorations = new ValueList<DecorationSpec>();
        internal readonly ValueList<LayerRecord> Layers = new ValueList<LayerRecord>();
        readonly System.Collections.Generic.List<LayerGroup> m_Groups = new System.Collections.Generic.List<LayerGroup>();
        readonly System.Collections.Generic.List<LayerGroup> m_GroupPool = new System.Collections.Generic.List<LayerGroup>();
        object m_CurrentEffect;
        int m_CurrentStyle;
        bool m_GroupsSorted;
        internal readonly ValueList<ElideRequest> Elides = new ValueList<ElideRequest>();

        internal readonly System.Collections.Generic.List<FeatureSet> FeatureSets = new System.Collections.Generic.List<FeatureSet>();
        readonly System.Collections.Generic.Dictionary<string, short> m_FeatureIds = new System.Collections.Generic.Dictionary<string, short>();
        internal readonly System.Collections.Generic.List<AxisSet> AxisSets = new System.Collections.Generic.List<AxisSet>();

        /// <summary>The metric fallback faces match on for this text, or -1 for the project's.</summary>
        internal int SizeMatchOverride = -1;

        /// <summary>A base direction forced by an effect: -1 none, 0 ltr, 1 rtl, 2 auto.</summary>
        internal int DirectionOverride = -1;

        /// <summary>What a text with no range reads. Nothing writes to it, and its characters have their defaults.</summary>
        internal static readonly LetteraAttributes Plain = new LetteraAttributes();

        /// <summary>A longer plain text keeps its own characters, so that one long text does not hold memory for good.</summary>
        internal const int PlainLength = 4096;

        internal void PreparePlain(int length)
        {
            Length = length;
            Uniform = false;
            if (Chars.Length >= length) return;
            int capacity = Mathf.Min(Mathf.Max(Mathf.Max(length, 64), Chars.Length * 2), PlainLength);
            var chars = new Cp[capacity];
            var plain = new Cp { SizeMul = 1, Weight = 0, Alpha = 1, Family = -1, Language = -1, Slant = -1, Features = -1, Axes = -1, Warp = -1 };
            for (int i = 0; i < capacity; i++) chars[i] = plain;
            Chars = chars;
        }

        // How many leading characters still hold their defaults: no effect has written since they were filled, so a plain text that grows fills only what is new.
        int m_CleanUpTo;

        internal void Reset(int length, bool uniform = false)
        {
            Extras.Clear();
            Uniform = uniform;
            int filled = uniform ? 1 : length;
            if (Chars.Length < filled || Chars.Length == 0)
            {
                Chars = new Cp[Mathf.Max(Mathf.Max(filled, 16), Chars.Length * 2)];
                m_CleanUpTo = 0;
            }
            Length = length;
            for (int i = Mathf.Min(m_CleanUpTo, filled); i < filled; i++)
                Chars[i] = new Cp { SizeMul = 1, Weight = 0, Alpha = 1, Family = -1, Language = -1, Slant = -1, Features = -1, Axes = -1, Warp = -1 };
            if (filled > m_CleanUpTo) m_CleanUpTo = filled;
            Filters.Clear();
            Warps.Clear();
            MotionRequests.Clear();
            RevealRequests.Clear();
            ActiveRequests.Clear();
            SemanticRequests.Clear();
            InlineRequests.Clear();
            Marquee = null;
            PaintOrder = LetteraPaintOrder.LayerMajor;
            PaintReverse = false;
            HasUnion = false;
            CurveRadius = default;
            HasCurve = false;
            HasLayout = false;
            LineHeights.Clear();
            ParagraphSpacings.Clear();
            LineGaps.Clear();
            Aligns.Clear();
            NoBreaks.Clear();
            Dividers.Clear();
            ListItems.Clear();
            Rubies.Clear();
            TrimTop = LetteraTrimTop.None;
            TrimBottom = LetteraTrimBottom.None;
            m_CellGroups = 0;
            FeatureSets.Clear();
            m_FeatureIds.Clear();
            AxisSets.Clear();
            SizeMatchOverride = -1;
            FamilyRequests.Clear();
            Languages.Clear();
            Decorations.Clear();
            Layers.Clear();
            Highlights.Clear();
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
            AddDecoration(start, end, false, new DecorationSpec { Paint = PaintOf(hasColor, color), AutoThickness = true, AutoOffset = true });
        }

        public void SetStrike(int start, int end, bool hasColor, Color32 color)
        {
            AddDecoration(start, end, true, new DecorationSpec { Paint = PaintOf(hasColor, color), AutoThickness = true, AutoOffset = true });
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

        void EnsureLayout()
        {
            if (HasLayout) return;
            HasLayout = true;
            if (Layout.Length < Length) Layout = new CpLayout[Mathf.Max(Length, 64)];
            for (int i = 0; i < Length; i++) Layout[i] = new CpLayout { LineHeight = -1, ParagraphSpacing = -1, Align = -1 };
        }

        internal void SetTracking(int start, int end, LayoutLength amount)
        {
            Clamp(ref start, ref end);
            EnsureLayout();
            for (int i = start; i < end; i++) Layout[i].Tracking = amount;
        }

        internal void SetWordSpacing(int start, int end, LayoutLength amount)
        {
            Clamp(ref start, ref end);
            EnsureLayout();
            for (int i = start; i < end; i++) Layout[i].WordSpacing = amount;
        }

        internal void SetCell(int start, int end, LayoutLength width, bool auto)
        {
            Clamp(ref start, ref end);
            EnsureLayout();
            int group = auto ? ++m_CellGroups : 0;
            for (int i = start; i < end; i++)
            {
                Layout[i].Cell = width;
                Layout[i].CellAuto = auto;
                Layout[i].CellGroup = group;
            }
        }

        internal void SetLineHeight(int start, int end, in LineHeightSpec spec)
        {
            Clamp(ref start, ref end);
            EnsureLayout();
            LineHeights.Add(spec);
            short index = (short)(LineHeights.Count - 1);
            for (int i = start; i < end; i++) Layout[i].LineHeight = index;
        }

        internal void SetParagraphSpacing(int start, int end, in ParagraphSpacingSpec spec)
        {
            Clamp(ref start, ref end);
            EnsureLayout();
            ParagraphSpacings.Add(spec);
            short index = (short)(ParagraphSpacings.Count - 1);
            for (int i = start; i < end; i++) Layout[i].ParagraphSpacing = index;
        }

        internal void AddLineGap(int end, LayoutLength amount)
        {
            EnsureLayout();
            LineGaps.Add(new LineGapSpec { End = end, Amount = amount });
        }

        internal void AddNoBreak(int start, int end)
        {
            Clamp(ref start, ref end);
            EnsureLayout();
            if (end > start) NoBreaks.Add(new Vector2Int(start, end));
        }

        internal void AddIndent(int start, int end, LayoutLength amount, float percent)
        {
            Clamp(ref start, ref end);
            EnsureLayout();
            for (int i = start; i < end; i++)
            {
                Layout[i].Indent.Em += amount.Em;
                Layout[i].Indent.Units += amount.Units;
                Layout[i].IndentPercent += percent;
            }
        }

        internal void SetAlign(int start, int end, in AlignSpec spec)
        {
            Clamp(ref start, ref end);
            EnsureLayout();
            Aligns.Add(spec);
            short index = (short)(Aligns.Count - 1);
            for (int i = start; i < end; i++) Layout[i].Align = index;
        }

        internal readonly ValueList<ListItemSpec> ListItems = new ValueList<ListItemSpec>();
        internal readonly ValueList<RubySpec> Rubies = new ValueList<RubySpec>();

        internal void AddListItem(in ListItemSpec item)
        {
            EnsureLayout();
            ListItems.Add(item);
        }

        internal void AddRuby(in RubySpec ruby)
        {
            EnsureLayout();
            Rubies.Add(ruby);
        }

        internal readonly System.Collections.Generic.List<Vector2Int> Dividers = new System.Collections.Generic.List<Vector2Int>();

        internal void AddDivider(int start, int end)
        {
            Clamp(ref start, ref end);
            EnsureLayout();
            if (end > start) Dividers.Add(new Vector2Int(start, end));
        }

        internal void SetTrim(LetteraTrimTop top, LetteraTrimBottom bottom)
        {
            TrimTop = top;
            TrimBottom = bottom;
        }

        internal struct FilterRange
        {
            public int Style, Start, End;
            public float[] Matrix;
        }

        internal readonly ValueList<FilterRange> Filters = new ValueList<FilterRange>();
        internal readonly ValueList<WarpSpec> Warps = new ValueList<WarpSpec>();
        internal LetteraPaintOrder PaintOrder;
        internal bool PaintReverse, HasUnion, HasCurve;
        internal LayoutLength CurveRadius;
        internal float CurveAnchor;

        internal void AddFilter(int start, int end, float[] matrix)
        {
            Clamp(ref start, ref end);
            Filters.Add(new FilterRange { Style = m_CurrentStyle, Start = start, End = end, Matrix = matrix });
        }

        internal void SetUnion(int start, int end)
        {
            Clamp(ref start, ref end);
            HasUnion = true;
            for (int i = start; i < end; i++) Chars[i].Union = true;
        }

        internal struct MotionRequest
        {
            public LetteraMotionEffect Effect;
            public int Start, End, Range;
            public LetteraSlots Slots;
        }

        internal struct RevealRequest
        {
            public RevealEffect Effect;
            public int Start, End;
            public string Entrance, Reach;
            public LetteraRevealUnit Unit;
        }

        internal struct ActiveRequest
        {
            public LetteraRangeEffect Effect;
            public int Start, End, Range;
            public string Value;
        }

        internal struct InlineRequest
        {
            public InlineEffect Effect;
            public int Start;
            public LetteraSlots Slots;
        }

        internal readonly ValueList<InlineRequest> InlineRequests = new ValueList<InlineRequest>();

        internal void AddInline(InlineEffect effect, int start, int end, in LetteraSlots slots)
        {
            EnsureLayout();
            for (int c = start; c < end; c++) InlineRequests.Add(new InlineRequest { Effect = effect, Start = c, Slots = slots });
        }

        internal struct SemanticRequest
        {
            public SemanticEffect Effect;
            public int Start, End, Range;
        }

        internal readonly ValueList<ActiveRequest> ActiveRequests = new ValueList<ActiveRequest>();
        internal readonly System.Collections.Generic.List<SemanticRequest> SemanticRequests = new System.Collections.Generic.List<SemanticRequest>();

        internal void AddActive(LetteraRangeEffect effect, int start, int end, string value) =>
            ActiveRequests.Add(new ActiveRequest { Effect = effect, Start = start, End = end, Range = CurrentRange, Value = value });

        internal void AddSemantic(SemanticEffect effect, int start, int end) =>
            SemanticRequests.Add(new SemanticRequest { Effect = effect, Start = start, End = end, Range = CurrentRange });

        internal readonly ValueList<MotionRequest> MotionRequests = new ValueList<MotionRequest>();
        internal readonly ValueList<RevealRequest> RevealRequests = new ValueList<RevealRequest>();
        internal MarqueeEffect Marquee;
        internal int CurrentRange;

        internal void AddMotion(LetteraMotionEffect effect, int start, int end, in LetteraSlots slots) =>
            MotionRequests.Add(new MotionRequest { Effect = effect, Start = start, End = end, Range = CurrentRange, Slots = slots.WithoutHolds() });

        internal void AddReveal(RevealEffect effect, int start, int end, string entrance, string reach, LetteraRevealUnit unit) =>
            RevealRequests.Add(new RevealRequest { Effect = effect, Start = start, End = end, Entrance = entrance, Reach = reach, Unit = unit });

        internal bool RevealTagged(int request) => request < RevealRequests.Count && RevealRequests[request].Reach != null;

        internal void SetMarquee(MarqueeEffect effect)
        {
            if (Marquee == null) Marquee = effect;
        }

        internal void SetWarp(int start, int end, in WarpSpec spec)
        {
            Clamp(ref start, ref end);
            Warps.Add(spec);
            short index = (short)(Warps.Count - 1);
            for (int i = start; i < end; i++) Chars[i].Warp = index;
        }

        internal void SetPaintOrder(LetteraPaintOrder order, bool reverse)
        {
            PaintOrder = order;
            PaintReverse = reverse;
        }

        internal void SetCurve(LayoutLength radius, float anchor)
        {
            HasCurve = true;
            CurveRadius = radius;
            CurveAnchor = anchor;
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

        static ResolvedPaint PaintOf(bool hasColor, Color32 color) => new ResolvedPaint { Color = color, UsesGlyphColor = !hasColor, Blend = LetteraBlend.Normal };

        internal void AddDecoration(int start, int end, bool strike, in DecorationSpec spec)
        {
            Clamp(ref start, ref end);
            Decorations.Add(spec);
            ushort index = (ushort)Mathf.Min(Decorations.Count, ushort.MaxValue);
            for (int i = start; i < end; i++)
            {
                if (strike) Chars[i].Strike = index;
                else Chars[i].Underline = index;
            }
        }

        internal readonly ValueList<HighlightSpec> Highlights = new ValueList<HighlightSpec>();

        internal void AddHighlight(int start, int end, in LayerRecord record, in HighlightSpec spec)
        {
            LayerRecord copy = record;
            copy.Kind = LayerKind.Highlight;
            copy.Highlight = Highlights.Count;
            Highlights.Add(spec);
            AddLayer(start, end, LetteraLayerBand.Back, copy);
        }

        /// <summary>Called before an effect applies a range: its layers belong to this effect.</summary>
        internal void BeginEffect(object effect, int style)
        {
            m_CleanUpTo = 0;
            m_CurrentEffect = effect;
            m_CurrentStyle = style;
        }

        /// <summary>A child of a composite draws as its own layers, in the composite's place in the style order.</summary>
        internal void BeginChildEffect(object child, int index) => m_CurrentEffect = child;

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
            copy.Style = m_CurrentStyle;
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
                if (record.Kind == LayerKind.Highlight) return true;
            }
            if (Filters.Count > 0 || Warps.Count > 0) return true;
            for (int i = 0; i < Decorations.Count; i++)
                if (!Decorations[i].Paint.IsSolid || Decorations[i].Style != LetteraLineStyle.Solid)
                    return true;
            return false;
        }

        internal bool NeedsGrain()
        {
            for (int i = 0; i < Layers.Count; i++)
                if (Layers[i].Paint.Grain)
                    return true;
            for (int i = 0; i < Decorations.Count; i++)
                if (Decorations[i].Paint.Grain)
                    return true;
            return false;
        }

        /// <summary>Asks layout to remove clusters of the range at a position (0 start, 0.5 middle, 1 end) when the text does not fit.</summary>
        public void RequestElide(int start, int end, float position, bool marker) => RequestElide(start, end, position, marker, default, default);

        internal void RequestElide(int start, int end, float position, bool marker, LayoutLength fadeDistance, LayoutLength fadeShift)
        {
            Clamp(ref start, ref end);
            Elides.Add(new ElideRequest { Start = start, End = end, Position = Mathf.Clamp01(position), Marker = marker, FadeDistance = fadeDistance, FadeShift = fadeShift });
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

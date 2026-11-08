using System;
using System.Collections.Generic;
using UnityEngine;

namespace FirstSymbol.Lettera
{
    public enum LetteraClock { Scaled, Unscaled, Manual }
    public enum LetteraMotionUnit { Cluster, Word, Line, Range }
    public enum LetteraReducedMotion { Rest, Keep }

    /// <summary>What a parameter of an effect is, so that rules, cues and an inspector can reach it by name.</summary>
    public enum LetteraParamKind { Float, Int, Bool, Enum, Color, Vector2, Length, String, Choice, Paint }

    /// <summary>The unit tokens a length slot reads. Bare is a number with no suffix; Delta is a leading + or -.</summary>
    [Flags]
    public enum LetteraSlotUnits { None = 0, Bare = 1, Px = 2, Em = 4, Percent = 8, Delta = 16 }

    /// <summary>
    /// One published parameter of an effect. A slot is a position in a tag's value, <c>&lt;shadow=#000,0.1&gt;</c>; an inspector reads
    /// the names, kinds, ranges and choices from here to edit a tag without the manual. Descriptors are built once and never on the update path.
    /// </summary>
    public sealed class LetteraParamInfo
    {
        public readonly string Id;
        public readonly int Slot;
        public readonly LetteraParamKind Kind;
        public readonly DirtyLevel Level;

        /// <summary>Whether Level is a promise about cost. Slots described for an inspector make none, so holds keep the level they were asked for.</summary>
        internal readonly bool DeclaresLevel;
        public readonly string Display;
        public readonly bool Essential;
        public readonly float Min, Max;

        /// <summary>The step of a slider or a drag, and the digits written after the point. Zero and -1 let the editor choose.</summary>
        public readonly float Step;
        public readonly int Decimals;

        /// <summary>For a length: the suffixes it reads, and what a bare number means.</summary>
        public readonly LetteraSlotUnits Units;
        public readonly LetteraLength.Unit BareMeans;

        /// <summary>What a bare number is called when it is no unit of its own: "units", "x".</summary>
        public readonly string BareLabel;

        /// <summary>The tokens of a choice, in the order shown, and optionally a plain name for each. An enum's are its member names.</summary>
        public readonly string[] Choices, ChoiceLabels;

        /// <summary>What the effect uses when the slot is empty: a field path on the effect (<c>m_Offset.x</c>), else text to show.</summary>
        public readonly string DefaultField, Default;

        /// <summary>One sentence for a tooltip.</summary>
        public readonly string Hint;

        /// <summary>Null for the main slots; a name for slots an editor folds away.</summary>
        public readonly string Group;

        /// <summary>Tokens the effect reads that this kind alone would call unreadable, for example <c>#80</c> for an alpha.</summary>
        public readonly Func<string, bool> Accepts;

        public LetteraParamInfo(string id, int slot, LetteraParamKind kind, DirtyLevel level, bool essential = false, float min = float.NegativeInfinity, float max = float.PositiveInfinity)
        {
            Id = id;
            Slot = slot;
            Kind = kind;
            Level = level;
            Essential = essential;
            Min = min;
            Max = max;
            Display = id;
            Decimals = -1;
            DeclaresLevel = true;
        }

        LetteraParamInfo(int slot, string display, LetteraParamKind kind, string hint, float min, float max, float step, int decimals, LetteraSlotUnits units,
            LetteraLength.Unit bareMeans, string bareLabel, string[] choices, string[] choiceLabels, string defaultField, string defaultText, string group, bool essential, Func<string, bool> accepts)
        {
            Id = display;
            Display = display;
            Slot = slot;
            Kind = kind;
            Level = DirtyLevel.Parse;
            Essential = essential;
            Min = min;
            Max = max;
            Step = step;
            Decimals = decimals;
            Units = units;
            BareMeans = bareMeans;
            BareLabel = bareLabel;
            Choices = choices;
            ChoiceLabels = choiceLabels;
            DefaultField = defaultField;
            Default = defaultText;
            Hint = hint;
            Group = group;
            Accepts = accepts;
        }

        /// <summary>A slot of a tag, described for an editor. Build these once, in a static array.</summary>
        public static LetteraParamInfo ForSlot(int slot, string display, LetteraParamKind kind, string hint = null, float min = float.NegativeInfinity, float max = float.PositiveInfinity,
            float step = 0, int decimals = -1, LetteraSlotUnits units = LetteraSlotUnits.None, LetteraLength.Unit bareMeans = LetteraLength.Unit.Bare, string bareLabel = null,
            string[] choices = null, string[] choiceLabels = null, string defaultField = null, string defaultText = null, string group = null, bool essential = false, Func<string, bool> accepts = null) =>
            new LetteraParamInfo(slot, display, kind, hint, min, max, step, decimals, units, bareMeans, bareLabel, choices, choiceLabels, defaultField, defaultText, group, essential, accepts);

        /// <summary>A slot that names a member of an enum; the choices are the member names.</summary>
        public static LetteraParamInfo ForEnum<T>(int slot, string display, string hint = null, string defaultField = null, string defaultText = null, string group = null) where T : struct, Enum =>
            new LetteraParamInfo(slot, display, LetteraParamKind.Enum, hint, float.NegativeInfinity, float.PositiveInfinity, 0, -1, LetteraSlotUnits.None, LetteraLength.Unit.Bare, null,
                Enum.GetNames(typeof(T)), null, defaultField, defaultText, group, false, null);
    }

    /// <summary>The resolved numbers of a motion for one range. A position equals the parameter's slot.</summary>
    public struct LetteraMotionValues
    {
        public float V0, V1, V2, V3, V4, V5, V6, V7;

        public float this[int index]
        {
            get
            {
                switch (index)
                {
                    case 0: return V0;
                    case 1: return V1;
                    case 2: return V2;
                    case 3: return V3;
                    case 4: return V4;
                    case 5: return V5;
                    case 6: return V6;
                    case 7: return V7;
                    default: return 0;
                }
            }
            set
            {
                switch (index)
                {
                    case 0: V0 = value; break;
                    case 1: V1 = value; break;
                    case 2: V2 = value; break;
                    case 3: V3 = value; break;
                    case 4: V4 = value; break;
                    case 5: V5 = value; break;
                    case 6: V6 = value; break;
                    case 7: V7 = value; break;
                }
            }
        }
    }

    /// <summary>A transient transform, alpha and tint on top of where layout put a glyph. Layout never sees it.</summary>
    public struct LetteraGlyphPose
    {
        public float A, B, C, D, Tx, Ty;
        public float Alpha;
        public Color Tint;

        public static LetteraGlyphPose Identity => new LetteraGlyphPose { A = 1, D = 1, Alpha = 1, Tint = Color.white };

        public bool IsIdentity => A == 1 && B == 0 && C == 0 && D == 1 && Tx == 0 && Ty == 0 && Alpha == 1 && Tint == Color.white;

        public Vector2 Apply(Vector2 p) => new Vector2(A * p.x + C * p.y + Tx, B * p.x + D * p.y + Ty);

        /// <summary>This pose on top of another: other runs first.</summary>
        public LetteraGlyphPose After(in LetteraGlyphPose o) => new LetteraGlyphPose
        {
            A = A * o.A + C * o.B,
            B = B * o.A + D * o.B,
            C = A * o.C + C * o.D,
            D = B * o.C + D * o.D,
            Tx = A * o.Tx + C * o.Ty + Tx,
            Ty = B * o.Tx + D * o.Ty + Ty,
            Alpha = Alpha * o.Alpha,
            Tint = Tint * o.Tint,
        };

        public void Move(float dx, float dy)
        {
            Tx += dx;
            Ty += dy;
        }

        /// <summary>Turns about a local point, counterclockwise for a positive angle.</summary>
        public void Turn(float degrees, Vector2 pivot)
        {
            float r = degrees * Mathf.Deg2Rad, cos = Mathf.Cos(r), sin = Mathf.Sin(r);
            var turn = new LetteraGlyphPose { A = cos, B = sin, C = -sin, D = cos, Alpha = 1, Tint = Color.white, Tx = pivot.x - cos * pivot.x + sin * pivot.y, Ty = pivot.y - sin * pivot.x - cos * pivot.y };
            this = turn.After(this);
        }

        public void Scale(float sx, float sy, Vector2 pivot)
        {
            var scale = new LetteraGlyphPose { A = sx, D = sy, Alpha = 1, Tint = Color.white, Tx = pivot.x * (1 - sx), Ty = pivot.y * (1 - sy) };
            this = scale.After(this);
        }

        public void Skew(float x, float y, Vector2 pivot)
        {
            var skew = new LetteraGlyphPose { A = 1, C = x, B = y, D = 1, Alpha = 1, Tint = Color.white, Tx = -x * pivot.y, Ty = -y * pivot.x };
            this = skew.After(this);
        }
    }

    /// <summary>The box a body turns about, in local units.</summary>
    public struct LetteraBodyBox
    {
        public float XMin, YMin, XMax, YMax;

        public Vector2 Center => new Vector2((XMin + XMax) * 0.5f, (YMin + YMax) * 0.5f);
        public Vector2 TopCenter => new Vector2((XMin + XMax) * 0.5f, YMax);
        public Vector2 At(float x, float y) => new Vector2(Mathf.LerpUnclamped(XMin, XMax, x), Mathf.LerpUnclamped(YMin, YMax, y));
        public bool IsEmpty => XMax < XMin;
        public static LetteraBodyBox Empty => new LetteraBodyBox { XMin = float.MaxValue, YMin = float.MaxValue, XMax = float.MinValue, YMax = float.MinValue };

        public void Add(float xMin, float yMin, float xMax, float yMax)
        {
            if (xMin < XMin) XMin = xMin;
            if (yMin < YMin) YMin = yMin;
            if (xMax > XMax) XMax = xMax;
            if (yMax > YMax) YMax = yMax;
        }
    }

    /// <summary>What a motion can read about one body. Valid during the call only.</summary>
    public readonly ref struct LetteraMotionContext
    {
        /// <summary>The phase value, in seconds while a clock runs.</summary>
        public readonly float T;

        /// <summary>The phase of this body: time scaled by rate, minus the offset of its place.</summary>
        public readonly float Phi;

        public readonly int Index, Count, Seed;
        public readonly LetteraBodyBox Box;

        /// <summary>The body's font size: a length in em times this is in local units.</summary>
        public readonly float Em;

        public readonly LetteraMotionValues Values;

        public LetteraMotionContext(float t, float phi, int index, int count, int seed, in LetteraBodyBox box, float em, in LetteraMotionValues values)
        {
            T = t;
            Phi = phi;
            Index = index;
            Count = count;
            Seed = seed;
            Box = box;
            Em = em;
            Values = values;
        }
    }

    /// <summary>Noise that gives the same answer each time, so a motion can be scrubbed.</summary>
    public static class LetteraMotionNoise
    {
        public static uint Hash(int a, int b, int c)
        {
            unchecked
            {
                uint h = 2166136261u;
                h = (h ^ (uint)a) * 16777619u;
                h = (h ^ (uint)b) * 16777619u;
                h = (h ^ (uint)c) * 16777619u;
                h ^= h >> 13;
                h *= 0x5bd1e995u;
                h ^= h >> 15;
                return h;
            }
        }

        /// <summary>A number in -1..1.</summary>
        public static float Signed(int a, int b, int c) => (Hash(a, b, c) & 0xFFFFFF) / (float)0xFFFFFF * 2 - 1;

        /// <summary>A number in 0..1.</summary>
        public static float Unit(int a, int b, int c) => (Hash(a, b, c) & 0xFFFFFF) / (float)0xFFFFFF;

        /// <summary>Smooth noise in -1..1 along x.</summary>
        public static float Smooth(float x, int b, int c)
        {
            int floor = Mathf.FloorToInt(x);
            float t = x - floor;
            t = t * t * t * (t * (t * 6 - 15) + 10);
            return Mathf.Lerp(Signed(floor, b, c), Signed(floor + 1, b, c), t);
        }
    }

    /// <summary>
    /// An effect whose look is a pure function of a phase value. It moves what layout placed, every frame, with no rebuild;
    /// layout, caret and selection go on from where the glyphs rest. Custom motions derive from this class.
    /// </summary>
    [Serializable]
    public abstract class LetteraMotionEffect : LetteraEffect
    {
        [SerializeField] LetteraClock m_Clock;
        [SerializeField] float m_Phase;
        [SerializeField] LetteraMotionUnit m_Unit;
        [Tooltip("Scales the whole motion, which makes it a good target for rules and cues.")]
        [SerializeField] float m_Strength = 1;
        [SerializeField] int m_Seed;
        [SerializeField] LetteraReducedMotion m_ReducedMotion;

        [NonSerialized] internal int Version;

        public LetteraClock Clock { get => m_Clock; set { m_Clock = value; Version++; } }

        /// <summary>The input under a manual clock, and the start offset otherwise.</summary>
        public float Phase { get => m_Phase; set { m_Phase = value; Version++; } }

        public LetteraMotionUnit Unit { get => m_Unit; set { m_Unit = value; Version++; } }
        public float Strength { get => m_Strength; set { m_Strength = value; Version++; } }
        public int Seed { get => m_Seed; set { m_Seed = value; Version++; } }
        public LetteraReducedMotion ReducedMotionPolicy { get => m_ReducedMotion; set { m_ReducedMotion = value; Version++; } }

        /// <summary>The slots of rate and step, or -1 for a motion with no such thing.</summary>
        protected virtual int RateSlot => -1;
        protected virtual int StepSlot => -1;

        /// <summary>Reads the values a range uses from its slots.</summary>
        public abstract void Resolve(in LetteraSlots slots, ref LetteraMotionValues values);

        /// <summary>Writes one body's pose. Must be pure, allocate nothing and touch no Unity object.</summary>
        public abstract void PoseBody(in LetteraMotionContext context, ref LetteraGlyphPose pose);

        /// <summary>How far from rest this can move a glyph, in em.</summary>
        public virtual float MaxReachEm(in LetteraMotionValues values) => 1;

        public override void Apply(LetteraAttributes attributes, int start, int end, in LetteraSlots slots) => attributes.AddMotion(this, start, end, slots);

        internal float PhiOf(in LetteraMotionValues values, float t, int index)
        {
            float rate = RateSlot >= 0 ? values[RateSlot] : 1, step = StepSlot >= 0 ? values[StepSlot] : 0;
            return t * rate - index * step;
        }

        /// <summary>The pose of a body at a phase. Same input, same output.</summary>
        public LetteraGlyphPose PoseAt(in LetteraMotionValues values, float t, int index, int count, in LetteraBodyBox box, float em)
        {
            LetteraGlyphPose pose = LetteraGlyphPose.Identity;
            PoseBody(new LetteraMotionContext(t, PhiOf(values, t, index), index, count, m_Seed, box, em, values), ref pose);
            return Strengthen(pose, box);
        }

        // Identity at 0: everything scales down toward rest together.
        internal LetteraGlyphPose Strengthen(in LetteraGlyphPose pose, in LetteraBodyBox box)
        {
            if (m_Strength == 1) return pose;
            float s = m_Strength;
            return new LetteraGlyphPose
            {
                A = 1 + (pose.A - 1) * s,
                B = pose.B * s,
                C = pose.C * s,
                D = 1 + (pose.D - 1) * s,
                Tx = pose.Tx * s,
                Ty = pose.Ty * s,
                Alpha = 1 + (pose.Alpha - 1) * s,
                Tint = Color.LerpUnclamped(Color.white, pose.Tint, s),
            };
        }

        /// <summary>Writes other streams than positions and colors, for a motion that needs to.</summary>
        internal virtual void WriteStreams(TextEngine engine, in TextEngine.MotionSpan span, float t) { }

        internal virtual void ReserveQuads(TextEngine engine, in TextEngine.MotionSpan span) { }

        protected static float Slot(in LetteraSlots slots, int index, float field) => slots.Float(index, field);

        /// <summary>The motion's own period, or 0 when it has none. Long runs stay exact by wrapping on it.</summary>
        internal virtual double PeriodOf(in LetteraMotionValues values) => 0;
    }

    /// <summary>Glyphs bob and sway. Slots: amplitude y, amplitude x, rate, step.</summary>
    [Serializable]
    public class WaveMotion : LetteraMotionEffect
    {
        [SerializeField] float m_Amplitude = 0.15f, m_AmplitudeX, m_Rate = 1, m_Step = 0.1f;

        static readonly LetteraParamInfo[] s_Params =
        {
            new LetteraParamInfo("Amplitude", 0, LetteraParamKind.Float, DirtyLevel.Pose, true),
            new LetteraParamInfo("AmplitudeX", 1, LetteraParamKind.Float, DirtyLevel.Pose),
            new LetteraParamInfo("Rate", 2, LetteraParamKind.Float, DirtyLevel.Pose, true),
            new LetteraParamInfo("Step", 3, LetteraParamKind.Float, DirtyLevel.Pose, true),
            new LetteraParamInfo("Phase", -1, LetteraParamKind.Float, DirtyLevel.Pose),
        };

        public override IReadOnlyList<LetteraParamInfo> Parameters => s_Params;
        public float Amplitude { get => m_Amplitude; set { m_Amplitude = value; Version++; } }
        public float AmplitudeX { get => m_AmplitudeX; set { m_AmplitudeX = value; Version++; } }
        public float Rate { get => m_Rate; set { m_Rate = value; Version++; } }
        public float Step { get => m_Step; set { m_Step = value; Version++; } }
        protected override int RateSlot => 2;
        protected override int StepSlot => 3;

        public override void Resolve(in LetteraSlots s, ref LetteraMotionValues v)
        {
            v.V0 = Slot(s, 0, m_Amplitude);
            v.V1 = Slot(s, 1, m_AmplitudeX);
            v.V2 = Slot(s, 2, m_Rate);
            v.V3 = Slot(s, 3, m_Step);
        }

        public override void PoseBody(in LetteraMotionContext c, ref LetteraGlyphPose p)
        {
            float a = c.Phi * Mathf.PI * 2;
            p.Move(c.Values.V1 * Mathf.Cos(a) * c.Em, c.Values.V0 * Mathf.Sin(a) * c.Em);
        }

        public override float MaxReachEm(in LetteraMotionValues v) => Mathf.Abs(v.V0) + Mathf.Abs(v.V1);
        internal override double PeriodOf(in LetteraMotionValues v) => v.V2 != 0 ? 1.0 / Mathf.Abs(v.V2) : 0;
    }

    /// <summary>Glyphs rock and squash. Slots: turn, squash, rate, step.</summary>
    [Serializable]
    public class WobbleMotion : LetteraMotionEffect
    {
        [SerializeField] float m_Turn = 8, m_Squash = 0.06f, m_Rate = 1.5f, m_Step = 0.12f;

        static readonly LetteraParamInfo[] s_Params =
        {
            new LetteraParamInfo("Turn", 0, LetteraParamKind.Float, DirtyLevel.Pose, true),
            new LetteraParamInfo("Squash", 1, LetteraParamKind.Float, DirtyLevel.Pose),
            new LetteraParamInfo("Rate", 2, LetteraParamKind.Float, DirtyLevel.Pose, true),
            new LetteraParamInfo("Step", 3, LetteraParamKind.Float, DirtyLevel.Pose, true),
        };

        public override IReadOnlyList<LetteraParamInfo> Parameters => s_Params;
        public float Turn { get => m_Turn; set { m_Turn = value; Version++; } }
        public float Squash { get => m_Squash; set { m_Squash = value; Version++; } }
        public float Rate { get => m_Rate; set { m_Rate = value; Version++; } }
        public float Step { get => m_Step; set { m_Step = value; Version++; } }
        protected override int RateSlot => 2;
        protected override int StepSlot => 3;

        public override void Resolve(in LetteraSlots s, ref LetteraMotionValues v)
        {
            v.V0 = Slot(s, 0, m_Turn);
            v.V1 = Slot(s, 1, m_Squash);
            v.V2 = Slot(s, 2, m_Rate);
            v.V3 = Slot(s, 3, m_Step);
        }

        public override void PoseBody(in LetteraMotionContext c, ref LetteraGlyphPose p)
        {
            float a = c.Phi * Mathf.PI * 2;
            Vector2 center = c.Box.Center;
            float squash = c.Values.V1 * Mathf.Sin(a + Mathf.PI * 0.5f);
            p.Scale(1 + squash, 1 - squash, center);
            p.Turn(c.Values.V0 * Mathf.Sin(a), center);
        }

        public override float MaxReachEm(in LetteraMotionValues v) => 0.5f + Mathf.Abs(v.V1);
        internal override double PeriodOf(in LetteraMotionValues v) => v.V2 != 0 ? 1.0 / Mathf.Abs(v.V2) : 0;
    }

    /// <summary>Glyphs jitter, holding still between re-rolls. Slots: amplitude, rate, turn.</summary>
    [Serializable]
    public class ShakeMotion : LetteraMotionEffect
    {
        [SerializeField] float m_Amplitude = 0.05f, m_Rate = 14, m_Turn;

        static readonly LetteraParamInfo[] s_Params =
        {
            new LetteraParamInfo("Amplitude", 0, LetteraParamKind.Float, DirtyLevel.Pose, true),
            new LetteraParamInfo("Rate", 1, LetteraParamKind.Float, DirtyLevel.Pose, true),
            new LetteraParamInfo("Turn", 2, LetteraParamKind.Float, DirtyLevel.Pose),
        };

        public override IReadOnlyList<LetteraParamInfo> Parameters => s_Params;
        public float Amplitude { get => m_Amplitude; set { m_Amplitude = value; Version++; } }
        public float Rate { get => m_Rate; set { m_Rate = value; Version++; } }
        public float Turn { get => m_Turn; set { m_Turn = value; Version++; } }

        public override void Resolve(in LetteraSlots s, ref LetteraMotionValues v)
        {
            v.V0 = Slot(s, 0, m_Amplitude);
            v.V1 = Slot(s, 1, m_Rate);
            v.V2 = Slot(s, 2, m_Turn);
        }

        public override void PoseBody(in LetteraMotionContext c, ref LetteraGlyphPose p)
        {
            int k = Mathf.FloorToInt(c.T * c.Values.V1);
            float a = c.Values.V0 * c.Em;
            p.Move(a * LetteraMotionNoise.Signed(c.Seed, c.Index, k * 3), a * LetteraMotionNoise.Signed(c.Seed, c.Index, k * 3 + 1));
            if (c.Values.V2 != 0) p.Turn(c.Values.V2 * LetteraMotionNoise.Signed(c.Seed, c.Index, k * 3 + 2), c.Box.Center);
        }

        public override float MaxReachEm(in LetteraMotionValues v) => Mathf.Abs(v.V0) * 1.5f + (v.V2 != 0 ? 0.5f : 0);
    }

    /// <summary>Glyphs hop, and never go below where they rest. Slots: height, rate, step, share of the cycle in the air.</summary>
    [Serializable]
    public class BounceMotion : LetteraMotionEffect
    {
        [SerializeField] float m_Height = 0.3f, m_Rate = 1, m_Step = 0.1f;
        [SerializeField, Range(0.01f, 1)] float m_Share = 0.5f;

        static readonly LetteraParamInfo[] s_Params =
        {
            new LetteraParamInfo("Height", 0, LetteraParamKind.Float, DirtyLevel.Pose, true),
            new LetteraParamInfo("Rate", 1, LetteraParamKind.Float, DirtyLevel.Pose, true),
            new LetteraParamInfo("Step", 2, LetteraParamKind.Float, DirtyLevel.Pose, true),
            new LetteraParamInfo("Share", 3, LetteraParamKind.Float, DirtyLevel.Pose, false, 0.01f, 1),
        };

        public override IReadOnlyList<LetteraParamInfo> Parameters => s_Params;
        public float Height { get => m_Height; set { m_Height = value; Version++; } }
        public float Rate { get => m_Rate; set { m_Rate = value; Version++; } }
        public float Step { get => m_Step; set { m_Step = value; Version++; } }
        public float Share { get => m_Share; set { m_Share = value; Version++; } }
        protected override int RateSlot => 1;
        protected override int StepSlot => 2;

        public override void Resolve(in LetteraSlots s, ref LetteraMotionValues v)
        {
            v.V0 = Slot(s, 0, m_Height);
            v.V1 = Slot(s, 1, m_Rate);
            v.V2 = Slot(s, 2, m_Step);
            v.V3 = Mathf.Clamp(Slot(s, 3, m_Share), 0.01f, 1);
        }

        public override void PoseBody(in LetteraMotionContext c, ref LetteraGlyphPose p)
        {
            float frac = c.Phi - Mathf.Floor(c.Phi);
            if (frac >= c.Values.V3) return;
            float x = frac / c.Values.V3;
            p.Move(0, c.Values.V0 * 4 * x * (1 - x) * c.Em);
        }

        public override float MaxReachEm(in LetteraMotionValues v) => Mathf.Abs(v.V0);
        internal override double PeriodOf(in LetteraMotionValues v) => v.V1 != 0 ? 1.0 / Mathf.Abs(v.V1) : 0;
    }

    /// <summary>Glyphs drift on smooth noise. Slots: amplitude y, amplitude x, rate, step.</summary>
    [Serializable]
    public class FloatMotion : LetteraMotionEffect
    {
        [SerializeField] float m_Amplitude = 0.08f, m_AmplitudeX = 0.05f, m_Rate = 0.25f, m_Step;

        static readonly LetteraParamInfo[] s_Params =
        {
            new LetteraParamInfo("Amplitude", 0, LetteraParamKind.Float, DirtyLevel.Pose, true),
            new LetteraParamInfo("AmplitudeX", 1, LetteraParamKind.Float, DirtyLevel.Pose),
            new LetteraParamInfo("Rate", 2, LetteraParamKind.Float, DirtyLevel.Pose, true),
            new LetteraParamInfo("Step", 3, LetteraParamKind.Float, DirtyLevel.Pose),
        };

        public override IReadOnlyList<LetteraParamInfo> Parameters => s_Params;
        public float Amplitude { get => m_Amplitude; set { m_Amplitude = value; Version++; } }
        public float AmplitudeX { get => m_AmplitudeX; set { m_AmplitudeX = value; Version++; } }
        public float Rate { get => m_Rate; set { m_Rate = value; Version++; } }
        public float Step { get => m_Step; set { m_Step = value; Version++; } }
        protected override int RateSlot => 2;
        protected override int StepSlot => 3;

        public override void Resolve(in LetteraSlots s, ref LetteraMotionValues v)
        {
            v.V0 = Slot(s, 0, m_Amplitude);
            v.V1 = Slot(s, 1, m_AmplitudeX);
            v.V2 = Slot(s, 2, m_Rate);
            v.V3 = Slot(s, 3, m_Step);
        }

        public override void PoseBody(in LetteraMotionContext c, ref LetteraGlyphPose p)
        {
            p.Move(c.Values.V1 * LetteraMotionNoise.Smooth(c.Phi, c.Index, c.Seed * 2) * c.Em, c.Values.V0 * LetteraMotionNoise.Smooth(c.Phi, c.Index, c.Seed * 2 + 1) * c.Em);
        }

        public override float MaxReachEm(in LetteraMotionValues v) => Mathf.Abs(v.V0) + Mathf.Abs(v.V1);
    }

    /// <summary>Glyphs turn round. Slots: rate, step.</summary>
    [Serializable]
    public class SpinMotion : LetteraMotionEffect
    {
        [SerializeField] float m_Rate = 0.5f, m_Step = 0.05f;

        static readonly LetteraParamInfo[] s_Params =
        {
            new LetteraParamInfo("Rate", 0, LetteraParamKind.Float, DirtyLevel.Pose, true),
            new LetteraParamInfo("Step", 1, LetteraParamKind.Float, DirtyLevel.Pose, true),
        };

        public override IReadOnlyList<LetteraParamInfo> Parameters => s_Params;
        public float Rate { get => m_Rate; set { m_Rate = value; Version++; } }
        public float Step { get => m_Step; set { m_Step = value; Version++; } }
        protected override int RateSlot => 0;
        protected override int StepSlot => 1;

        public override void Resolve(in LetteraSlots s, ref LetteraMotionValues v)
        {
            v.V0 = Slot(s, 0, m_Rate);
            v.V1 = Slot(s, 1, m_Step);
        }

        public override void PoseBody(in LetteraMotionContext c, ref LetteraGlyphPose p) => p.Turn(360 * (c.Phi - Mathf.Floor(c.Phi)), c.Box.Center);
        public override float MaxReachEm(in LetteraMotionValues v) => 1;
        internal override double PeriodOf(in LetteraMotionValues v) => v.V0 != 0 ? 1.0 / Mathf.Abs(v.V0) : 0;
    }

    /// <summary>Glyphs grow and shrink. Slots: amount, rate, step.</summary>
    [Serializable]
    public class PulseMotion : LetteraMotionEffect
    {
        [SerializeField] float m_Amount = 0.1f, m_Rate = 1, m_Step = 0.1f;

        static readonly LetteraParamInfo[] s_Params =
        {
            new LetteraParamInfo("Amount", 0, LetteraParamKind.Float, DirtyLevel.Pose, true),
            new LetteraParamInfo("Rate", 1, LetteraParamKind.Float, DirtyLevel.Pose, true),
            new LetteraParamInfo("Step", 2, LetteraParamKind.Float, DirtyLevel.Pose, true),
        };

        public override IReadOnlyList<LetteraParamInfo> Parameters => s_Params;
        public float Amount { get => m_Amount; set { m_Amount = value; Version++; } }
        public float Rate { get => m_Rate; set { m_Rate = value; Version++; } }
        public float Step { get => m_Step; set { m_Step = value; Version++; } }
        protected override int RateSlot => 1;
        protected override int StepSlot => 2;

        public override void Resolve(in LetteraSlots s, ref LetteraMotionValues v)
        {
            v.V0 = Slot(s, 0, m_Amount);
            v.V1 = Slot(s, 1, m_Rate);
            v.V2 = Slot(s, 2, m_Step);
        }

        public override void PoseBody(in LetteraMotionContext c, ref LetteraGlyphPose p)
        {
            float scale = 1 + c.Values.V0 * Mathf.Sin(c.Phi * Mathf.PI * 2);
            p.Scale(scale, scale, c.Box.Center);
        }

        public override float MaxReachEm(in LetteraMotionValues v) => Mathf.Abs(v.V0);
        internal override double PeriodOf(in LetteraMotionValues v) => v.V1 != 0 ? 1.0 / Mathf.Abs(v.V1) : 0;
    }

    /// <summary>Glyphs swing from their top edge. Slots: turn, rate, step.</summary>
    [Serializable]
    public class PendulumMotion : LetteraMotionEffect
    {
        [SerializeField] float m_Turn = 15, m_Rate = 0.75f, m_Step = 0.08f;

        static readonly LetteraParamInfo[] s_Params =
        {
            new LetteraParamInfo("Turn", 0, LetteraParamKind.Float, DirtyLevel.Pose, true),
            new LetteraParamInfo("Rate", 1, LetteraParamKind.Float, DirtyLevel.Pose, true),
            new LetteraParamInfo("Step", 2, LetteraParamKind.Float, DirtyLevel.Pose, true),
        };

        public override IReadOnlyList<LetteraParamInfo> Parameters => s_Params;
        public float Turn { get => m_Turn; set { m_Turn = value; Version++; } }
        public float Rate { get => m_Rate; set { m_Rate = value; Version++; } }
        public float Step { get => m_Step; set { m_Step = value; Version++; } }
        protected override int RateSlot => 1;
        protected override int StepSlot => 2;

        public override void Resolve(in LetteraSlots s, ref LetteraMotionValues v)
        {
            v.V0 = Slot(s, 0, m_Turn);
            v.V1 = Slot(s, 1, m_Rate);
            v.V2 = Slot(s, 2, m_Step);
        }

        public override void PoseBody(in LetteraMotionContext c, ref LetteraGlyphPose p) => p.Turn(c.Values.V0 * Mathf.Sin(c.Phi * Mathf.PI * 2), c.Box.TopCenter);
        public override float MaxReachEm(in LetteraMotionValues v) => 1;
        internal override double PeriodOf(in LetteraMotionValues v) => v.V1 != 0 ? 1.0 / Mathf.Abs(v.V1) : 0;
    }

    /// <summary>The shipped warp with a clock: the distance field flows. Slots: amplitude, scale, travel.</summary>
    [Serializable]
    public class DeformMotion : LetteraMotionEffect
    {
        [SerializeField] float m_Amplitude = 0.02f, m_Scale = 0.25f, m_Travel = 1;

        static readonly LetteraParamInfo[] s_Params =
        {
            new LetteraParamInfo("Amplitude", 0, LetteraParamKind.Float, DirtyLevel.Mesh, true),
            new LetteraParamInfo("Scale", 1, LetteraParamKind.Float, DirtyLevel.Mesh),
            new LetteraParamInfo("Travel", 2, LetteraParamKind.Float, DirtyLevel.Pose, true),
        };

        public override IReadOnlyList<LetteraParamInfo> Parameters => s_Params;
        public float Amplitude { get => m_Amplitude; set { m_Amplitude = value; Version++; } }
        public float Scale { get => m_Scale; set { m_Scale = value; Version++; } }
        public float Travel { get => m_Travel; set { m_Travel = value; Version++; } }

        public override void Resolve(in LetteraSlots s, ref LetteraMotionValues v)
        {
            v.V0 = Slot(s, 0, m_Amplitude);
            v.V1 = Slot(s, 1, m_Scale);
            v.V2 = Slot(s, 2, m_Travel);
        }

        public override void PoseBody(in LetteraMotionContext c, ref LetteraGlyphPose p) { }
        public override float MaxReachEm(in LetteraMotionValues v) => Mathf.Abs(v.V0);

        public override void Apply(LetteraAttributes attributes, int start, int end, in LetteraSlots slots)
        {
            var values = default(LetteraMotionValues);
            Resolve(slots, ref values);
            attributes.SetWarp(start, end, new WarpSpec { Amplitude = values.V0, Scale = Mathf.Max(values.V1, 0.01f), Phase = Phase });
            base.Apply(attributes, start, end, slots);
        }

        internal override void WriteStreams(TextEngine engine, in TextEngine.MotionSpan span, float t) => engine.WriteDeform(span, t * span.Effective.V2);
    }
}

using System;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

namespace FirstSymbol.Lettera
{
    /// <summary>An axis of a solid text's own space: X across, Y up, Z into depth, away from the viewer.</summary>
    public enum LetteraSolidAxis
    {
        X,
        Y,
        Z,
    }

    /// <summary>
    /// Where a glyph of a solid text stands in depth, on top of where layout put it: a turn and a scale about a pivot, then a move.
    /// Turns go as a transform's rotation does. Lengths are in the text's local units; Z is away from the viewer.
    /// </summary>
    public struct LetteraSolidPose
    {
        public Quaternion Rotation;
        /// <summary>What it turns and scales about. X and Y in the text's plane, Z from the middle of the glyph's depth.</summary>
        public Vector3 Pivot;
        public Vector3 Move;
        public float Scale;
        /// <summary>A scale of the depth alone, about the pivot: 0 is flat, 1 as built.</summary>
        public float DepthScale;
        public float Alpha;

        public static LetteraSolidPose Identity => new LetteraSolidPose { Rotation = Quaternion.identity, Scale = 1, DepthScale = 1, Alpha = 1 };

        public bool IsIdentity => Rotation == Quaternion.identity && Move == Vector3.zero && Scale == 1 && DepthScale == 1 && Alpha == 1;

        /// <summary>Turns further about an axis, after what it turned already.</summary>
        public void Turn(Vector3 axis, float degrees) => Rotation = Quaternion.AngleAxis(degrees, axis) * Rotation;

        /// <summary>Moves towards the viewer, in local units.</summary>
        public void Lift(float units) => Move.z -= units;

        internal static Vector3 AxisOf(LetteraSolidAxis axis) => axis == LetteraSolidAxis.X ? Vector3.right : axis == LetteraSolidAxis.Z ? Vector3.forward : Vector3.up;

        /// <summary>This pose with only a share of its change: 0 is rest, 1 all of it.</summary>
        internal LetteraSolidPose Weighted(float share)
        {
            if (share == 1) return this;
            LetteraSolidPose p = this;
            p.Rotation = Quaternion.SlerpUnclamped(Quaternion.identity, Rotation, share);
            p.Move = Move * share;
            p.Scale = 1 + (Scale - 1) * share;
            p.DepthScale = 1 + (DepthScale - 1) * share;
            p.Alpha = 1 + (Alpha - 1) * share;
            return p;
        }

        /// <summary>The pose as an affine map of the text's space, its pivot's depth measured from <paramref name="middle"/>.</summary>
        internal float4x4 Matrix(float middle)
        {
            float3 pivot = new float3(Pivot.x, Pivot.y, Pivot.z + middle);
            float3x3 turn = new float3x3((quaternion)Rotation);
            float3x3 linear = math.mul(turn, float3x3.Scale(Scale, Scale, Scale * DepthScale));
            float3 shift = pivot + (float3)Move - math.mul(linear, pivot);
            return new float4x4(linear, shift);
        }
    }

    /// <summary>
    /// A motion that moves glyphs of a solid text in depth. A custom one derives from this and writes <see cref="PoseSolid"/>.
    /// On flat text it does nothing, unless it also writes a flat pose.
    /// </summary>
    [Serializable]
    public abstract class LetteraSolidMotion : LetteraMotionEffect
    {
        /// <summary>Flat text has no depth to move in.</summary>
        public override void PoseBody(in LetteraMotionContext context, ref LetteraGlyphPose pose) { }

        /// <summary>Writes one body's pose in depth. The pivot starts at the body's centre, in the middle of its depth. Must be pure and allocate nothing.</summary>
        public abstract void PoseSolid(in LetteraMotionContext context, ref LetteraSolidPose pose);

        /// <summary>The pose of a body at a phase, as a solid text would take it. Same input, same output.</summary>
        public LetteraSolidPose SolidPoseAt(in LetteraMotionValues values, float t, int index, int count, in LetteraBodyBox box, float em)
        {
            LetteraSolidPose pose = LetteraSolidPose.Identity;
            pose.Pivot = box.Center;
            PoseSolid(new LetteraMotionContext(t, PhiOf(values, t, index), index, count, Seed, box, em, values), ref pose);
            return pose.Weighted(Strength);
        }

        protected static float Turns(float phi) => phi - Mathf.Floor(phi);
    }

    /// <summary>Glyphs turn round about an axis, Y by default, like a sign on a pole. Slots: rate, step, axis.</summary>
    [Serializable, UnityEngine.Scripting.Preserve]
    public class RevolveMotion : LetteraSolidMotion
    {
        [SerializeField] float m_Rate = 0.5f, m_Step = 0.05f;
        [SerializeField] LetteraSolidAxis m_Axis = LetteraSolidAxis.Y;

        static readonly LetteraParamInfo[] s_Params =
        {
            new LetteraParamInfo("Rate", 0, LetteraParamKind.Float, DirtyLevel.Pose, true),
            new LetteraParamInfo("Step", 1, LetteraParamKind.Float, DirtyLevel.Pose, true),
            new LetteraParamInfo("Axis", 2, LetteraParamKind.Enum, DirtyLevel.Pose, true),
        };

        public override IReadOnlyList<LetteraParamInfo> Parameters => s_Params;
        public float Rate { get => m_Rate; set { m_Rate = value; Version++; } }
        public float Step { get => m_Step; set { m_Step = value; Version++; } }
        public LetteraSolidAxis Axis { get => m_Axis; set { m_Axis = value; Version++; } }
        protected override int RateSlot => 0;
        protected override int StepSlot => 1;

        public override void Resolve(in LetteraSlots s, ref LetteraMotionValues v)
        {
            v.V0 = Slot(s, 0, m_Rate);
            v.V1 = Slot(s, 1, m_Step);
            v.V2 = (float)s.Enum(2, m_Axis);
        }

        public override void PoseSolid(in LetteraMotionContext c, ref LetteraSolidPose p) => p.Turn(LetteraSolidPose.AxisOf((LetteraSolidAxis)(int)c.Values.V2), 360 * Turns(c.Phi));
        internal override double PeriodOf(in LetteraMotionValues v) => v.V0 != 0 ? 1.0 / Mathf.Abs(v.V0) : 0;
    }

    /// <summary>Glyphs swing to and fro in depth, from their top edge like a hanging sign, or about their middle on Y. Slots: degrees, rate, step, axis.</summary>
    [Serializable, UnityEngine.Scripting.Preserve]
    public class SwingMotion : LetteraSolidMotion
    {
        [SerializeField] float m_Angle = 30, m_Rate = 0.5f, m_Step = 0.1f;
        [SerializeField] LetteraSolidAxis m_Axis = LetteraSolidAxis.X;

        static readonly LetteraParamInfo[] s_Params =
        {
            new LetteraParamInfo("Angle", 0, LetteraParamKind.Float, DirtyLevel.Pose, true),
            new LetteraParamInfo("Rate", 1, LetteraParamKind.Float, DirtyLevel.Pose, true),
            new LetteraParamInfo("Step", 2, LetteraParamKind.Float, DirtyLevel.Pose, true),
            new LetteraParamInfo("Axis", 3, LetteraParamKind.Enum, DirtyLevel.Pose),
        };

        public override IReadOnlyList<LetteraParamInfo> Parameters => s_Params;
        public float Angle { get => m_Angle; set { m_Angle = value; Version++; } }
        public float Rate { get => m_Rate; set { m_Rate = value; Version++; } }
        public float Step { get => m_Step; set { m_Step = value; Version++; } }
        public LetteraSolidAxis Axis { get => m_Axis; set { m_Axis = value; Version++; } }
        protected override int RateSlot => 1;
        protected override int StepSlot => 2;

        public override void Resolve(in LetteraSlots s, ref LetteraMotionValues v)
        {
            v.V0 = Slot(s, 0, m_Angle);
            v.V1 = Slot(s, 1, m_Rate);
            v.V2 = Slot(s, 2, m_Step);
            v.V3 = (float)s.Enum(3, m_Axis);
        }

        public override void PoseSolid(in LetteraMotionContext c, ref LetteraSolidPose p)
        {
            var axis = (LetteraSolidAxis)(int)c.Values.V3;
            if (axis != LetteraSolidAxis.Y) p.Pivot.y = c.Box.YMax;
            p.Turn(LetteraSolidPose.AxisOf(axis), c.Values.V0 * Mathf.Sin(c.Phi * Mathf.PI * 2));
        }

        internal override double PeriodOf(in LetteraMotionValues v) => v.V1 != 0 ? 1.0 / Mathf.Abs(v.V1) : 0;
    }

    /// <summary>A wave that runs through glyphs toward the viewer and away. Slots: amplitude in em, rate, step.</summary>
    [Serializable, UnityEngine.Scripting.Preserve]
    public class SurgeMotion : LetteraSolidMotion
    {
        [SerializeField] float m_Amplitude = 0.3f, m_Rate = 1, m_Step = 0.1f;

        static readonly LetteraParamInfo[] s_Params =
        {
            new LetteraParamInfo("Amplitude", 0, LetteraParamKind.Float, DirtyLevel.Pose, true),
            new LetteraParamInfo("Rate", 1, LetteraParamKind.Float, DirtyLevel.Pose, true),
            new LetteraParamInfo("Step", 2, LetteraParamKind.Float, DirtyLevel.Pose, true),
        };

        public override IReadOnlyList<LetteraParamInfo> Parameters => s_Params;
        public float Amplitude { get => m_Amplitude; set { m_Amplitude = value; Version++; } }
        public float Rate { get => m_Rate; set { m_Rate = value; Version++; } }
        public float Step { get => m_Step; set { m_Step = value; Version++; } }
        protected override int RateSlot => 1;
        protected override int StepSlot => 2;

        public override void Resolve(in LetteraSlots s, ref LetteraMotionValues v)
        {
            v.V0 = Slot(s, 0, m_Amplitude);
            v.V1 = Slot(s, 1, m_Rate);
            v.V2 = Slot(s, 2, m_Step);
        }

        public override void PoseSolid(in LetteraMotionContext c, ref LetteraSolidPose p) => p.Lift(c.Values.V0 * c.Em * Mathf.Sin(c.Phi * Mathf.PI * 2));
        public override float MaxReachEm(in LetteraMotionValues v) => Mathf.Abs(v.V0);
        internal override double PeriodOf(in LetteraMotionValues v) => v.V1 != 0 ? 1.0 / Mathf.Abs(v.V1) : 0;
    }

    /// <summary>Glyphs circle round where they rest, about an axis: Y by default, passing in front and behind. Slots: radius in em, rate, step, axis.</summary>
    [Serializable, UnityEngine.Scripting.Preserve]
    public class OrbitMotion : LetteraSolidMotion
    {
        [SerializeField] float m_Radius = 0.25f, m_Rate = 0.5f, m_Step = 0.1f;
        [SerializeField] LetteraSolidAxis m_Axis = LetteraSolidAxis.Y;

        static readonly LetteraParamInfo[] s_Params =
        {
            new LetteraParamInfo("Radius", 0, LetteraParamKind.Float, DirtyLevel.Pose, true),
            new LetteraParamInfo("Rate", 1, LetteraParamKind.Float, DirtyLevel.Pose, true),
            new LetteraParamInfo("Step", 2, LetteraParamKind.Float, DirtyLevel.Pose, true),
            new LetteraParamInfo("Axis", 3, LetteraParamKind.Enum, DirtyLevel.Pose),
        };

        public override IReadOnlyList<LetteraParamInfo> Parameters => s_Params;
        public float Radius { get => m_Radius; set { m_Radius = value; Version++; } }
        public float Rate { get => m_Rate; set { m_Rate = value; Version++; } }
        public float Step { get => m_Step; set { m_Step = value; Version++; } }
        public LetteraSolidAxis Axis { get => m_Axis; set { m_Axis = value; Version++; } }
        protected override int RateSlot => 1;
        protected override int StepSlot => 2;

        public override void Resolve(in LetteraSlots s, ref LetteraMotionValues v)
        {
            v.V0 = Slot(s, 0, m_Radius);
            v.V1 = Slot(s, 1, m_Rate);
            v.V2 = Slot(s, 2, m_Step);
            v.V3 = (float)s.Enum(3, m_Axis);
        }

        public override void PoseSolid(in LetteraMotionContext c, ref LetteraSolidPose p)
        {
            float r = c.Values.V0 * c.Em, a = c.Phi * Mathf.PI * 2, x = r * Mathf.Cos(a), y = r * Mathf.Sin(a);
            switch ((LetteraSolidAxis)(int)c.Values.V3)
            {
                case LetteraSolidAxis.X: p.Move += new Vector3(0, x, y); break;
                case LetteraSolidAxis.Z: p.Move += new Vector3(x, y, 0); break;
                default: p.Move += new Vector3(x, 0, y); break;
            }
        }

        public override float MaxReachEm(in LetteraMotionValues v) => Mathf.Abs(v.V0);
        internal override double PeriodOf(in LetteraMotionValues v) => v.V1 != 0 ? 1.0 / Mathf.Abs(v.V1) : 0;
    }

    /// <summary>Glyphs tumble, each about an axis of its own that its seed chooses. Slots: rate, step.</summary>
    [Serializable, UnityEngine.Scripting.Preserve]
    public class TumbleMotion : LetteraSolidMotion
    {
        [SerializeField] float m_Rate = 0.25f, m_Step = 0.1f;

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

        public override void PoseSolid(in LetteraMotionContext c, ref LetteraSolidPose p) => p.Turn(LetteraSolidEntrances.AxisFor(c.Seed, c.Index), 360 * Turns(c.Phi));
        internal override double PeriodOf(in LetteraMotionValues v) => v.V0 != 0 ? 1.0 / Mathf.Abs(v.V0) : 0;
    }

    /// <summary>How the entrances that move in depth pose a solid glyph. Flat text takes their fade instead.</summary>
    internal static class LetteraSolidEntrances
    {
        internal static Vector3 AxisFor(int seed, int index)
        {
            var axis = new Vector3(LetteraMotionNoise.Signed(seed, index, 31), LetteraMotionNoise.Signed(seed, index, 32), LetteraMotionNoise.Signed(seed, index, 33));
            return axis.sqrMagnitude > 1e-4f ? axis.normalized : Vector3.up;
        }

        /// <param name="halfDepth">Half the glyph's depth, in local units.</param>
        internal static void Pose(LetteraEntrance entrance, float value, in LetteraBodyBox box, float em, int unit, int seed, float halfDepth, ref LetteraSolidPose pose)
        {
            float e = LetteraEntrance.Eased(entrance.Ease, value), o = 1 - e;
            Vector2 pivot = box.At(entrance.Pivot.x, entrance.Pivot.y);
            pose.Pivot = new Vector3(pivot.x, pivot.y, 0);
            switch (entrance.Kind)
            {
                case LetteraEntranceKind.TurnIn:
                    pose.Turn(Vector3.up, o * 90 * entrance.Amount);
                    break;
                case LetteraEntranceKind.FlyIn:
                    pose.Lift(o * entrance.Distance * 8 * em);
                    pose.Alpha *= e;
                    break;
                case LetteraEntranceKind.Deepen:
                    // From the front face, so the face stands still and the back grows out of it.
                    pose.Pivot.z = -halfDepth;
                    pose.DepthScale = Mathf.Max(e, 0.01f);
                    break;
                case LetteraEntranceKind.TumbleIn:
                    pose.Turn(AxisFor(seed, unit), o * 360 * entrance.Amount);
                    pose.Move += new Vector3(0, o * entrance.Distance * 2 * em, o * entrance.Distance * 6 * em);
                    pose.Alpha *= e;
                    break;
            }
        }
    }

    /// <summary>
    /// Poses glyphs of a solid text in depth each frame: their motions, their entrances, then the text's own hook.
    /// Arrays are kept, so a frame allocates nothing.
    /// </summary>
    internal sealed class LetteraSolidPoser
    {
        float4x4[] m_Glyph = Array.Empty<float4x4>();
        float3x3[] m_Normal = Array.Empty<float3x3>();
        float[] m_Alpha = Array.Empty<float>();
        bool[] m_Has = Array.Empty<bool>();
        float[] m_Middle = Array.Empty<float>(), m_Half = Array.Empty<float>();
        float3[] m_Centre = Array.Empty<float3>();

        internal bool Any { get; private set; }

        internal bool Has(int glyph) => glyph >= 0 && glyph < m_Has.Length && m_Has[glyph];
        internal float4x4 Matrix(int glyph) => m_Glyph[glyph];
        internal float3x3 Normal(int glyph) => m_Normal[glyph];
        internal float Alpha(int glyph) => m_Alpha[glyph];

        /// <summary>Whether anything would pose this text in depth now, checked without touching a glyph.</summary>
        internal static bool Wanted(TextEngine engine, ILetteraSolidPose hook)
        {
            if (hook != null || engine.RevealsForSolids) return true;
            for (int s = 0; s < engine.MotionSpanCountForSolids; s++)
                if (engine.MotionForSolids(s, out _, out _, out _, out _, out _) is LetteraSolidMotion)
                    return true;
            return false;
        }

        void Ensure(int glyphs)
        {
            if (m_Has.Length >= glyphs) return;
            int size = Mathf.Max(glyphs, m_Has.Length * 2);
            m_Glyph = new float4x4[size];
            m_Normal = new float3x3[size];
            m_Alpha = new float[size];
            m_Has = new bool[size];
            m_Middle = new float[size];
            m_Half = new float[size];
            m_Centre = new float3[size];
        }

        /// <summary>Fills each glyph's pose in depth. Returns whether any glyph has one.</summary>
        internal bool Compute(TextEngine engine, IReadOnlyList<LetteraSolidMeshBuilder.Placement> placed, int glyphPlacements, Unity.Collections.NativeArray<float3> ends, ILetteraSolidPose hook)
        {
            Any = false;
            int glyphs = engine.PlacedSpan.Length;
            if (!Wanted(engine, hook))
            {
                if (m_Has.Length > 0) Array.Clear(m_Has, 0, m_Has.Length);
                return false;
            }
            Ensure(glyphs);
            Array.Clear(m_Has, 0, m_Has.Length);
            for (int p = 0; p < glyphPlacements && p < placed.Count; p++)
            {
                LetteraSolidMeshBuilder.Placement place = placed[p];
                if (place.Cluster < 0 || place.GlyphIndex < 0 || place.GlyphIndex >= glyphs) continue;
                float half = place.Depth * place.SizeZ * 0.5f;
                m_Middle[place.GlyphIndex] = place.ZOffset + half;
                m_Half[place.GlyphIndex] = half;
                m_Centre[place.GlyphIndex] = (ends[p * 2] + ends[p * 2 + 1]) * 0.5f;
            }

            for (int s = 0; s < engine.MotionSpanCountForSolids; s++)
            {
                if (!(engine.MotionForSolids(s, out LetteraMotionValues values, out float t, out int linkStart, out int linkCount, out int bodyCount) is LetteraSolidMotion motion)) continue;
                for (int l = linkStart; l < linkStart + linkCount; l++)
                {
                    engine.MotionLinkForSolids(s, l, out int glyph, out int body, out LetteraBodyBox box, out float em);
                    if (glyph < 0 || glyph >= glyphs) continue;
                    LetteraSolidPose pose = motion.SolidPoseAt(values, t, body, bodyCount, box, em);
                    Compose(glyph, pose);
                }
            }

            if (engine.RevealsForSolids)
            {
                for (int g = 0; g < glyphs; g++)
                {
                    if (!engine.RevealInDepthForSolids(g, out LetteraEntrance entrance, out float value, out LetteraBodyBox box, out float em, out int unit, out int seed)) continue;
                    LetteraSolidPose pose = LetteraSolidPose.Identity;
                    LetteraSolidEntrances.Pose(entrance, value, box, em, unit, seed, m_Half[g], ref pose);
                    Compose(g, pose);
                }
            }

            if (hook != null)
            {
                for (int g = 0; g < glyphs; g++)
                {
                    float4x4 before = m_Has[g] ? m_Glyph[g] : float4x4.identity;
                    float3 centre = math.transform(before, m_Centre[g]);
                    Matrix4x4 own = Matrix4x4.identity;
                    hook.Pose(g, centre, ref own);
                    if (own == Matrix4x4.identity) continue;
                    float4x4 about = math.mul(float4x4.Translate(centre), math.mul((float4x4)own, float4x4.Translate(-centre)));
                    Set(g, math.mul(about, before), m_Has[g] ? m_Alpha[g] : 1);
                }
            }
            return Any;
        }

        // A later pose turns about where the glyph has gone by now, not where it rested.
        void Compose(int glyph, in LetteraSolidPose pose)
        {
            if (pose.IsIdentity) return;
            float4x4 before = m_Has[glyph] ? m_Glyph[glyph] : float4x4.identity;
            LetteraSolidPose moved = pose;
            float3 pivot = math.transform(before, new float3(pose.Pivot.x, pose.Pivot.y, pose.Pivot.z + m_Middle[glyph]));
            moved.Pivot = new Vector3(pivot.x, pivot.y, pivot.z - m_Middle[glyph]);
            Set(glyph, math.mul(moved.Matrix(m_Middle[glyph]), before), (m_Has[glyph] ? m_Alpha[glyph] : 1) * Mathf.Clamp01(pose.Alpha));
        }

        void Set(int glyph, float4x4 matrix, float alpha)
        {
            m_Glyph[glyph] = matrix;
            float3x3 linear = new float3x3(matrix.c0.xyz, matrix.c1.xyz, matrix.c2.xyz);
            m_Normal[glyph] = math.abs(math.determinant(linear)) > 1e-12f ? math.transpose(math.inverse(linear)) : linear;
            m_Alpha[glyph] = alpha;
            m_Has[glyph] = true;
            Any = true;
        }
    }
}

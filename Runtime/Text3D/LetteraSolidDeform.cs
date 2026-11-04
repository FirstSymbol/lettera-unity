using System;
using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace FirstSymbol.Lettera
{
    /// <summary>What a deformer does to a solid text.</summary>
    public enum LetteraDeformKind
    {
        /// <summary>Curves the text along its width. 360 degrees closes it in a ring.</summary>
        Bend,
        /// <summary>Turns the text about its reading line, more the further along.</summary>
        Twist,
        /// <summary>Narrows the text towards its end. A negative amount widens it.</summary>
        Taper,
        /// <summary>A sine along the width.</summary>
        Wave,
        /// <summary>Wraps the text onto a ball.</summary>
        Sphere,
        /// <summary>Lays the text along a curve through points.</summary>
        Path,
    }

    /// <summary>Which way a deformer moves the text.</summary>
    public enum LetteraDeformAxis
    {
        /// <summary>Towards and away from the viewer: a bend curves into depth, a wave rolls in depth.</summary>
        Depth,
        /// <summary>Up and down: a bend makes an arc in the text's plane, a wave moves letters up.</summary>
        Up,
        /// <summary>Taper and wave only: both at once.</summary>
        Both,
    }

    /// <summary>
    /// One step of a chain that deforms a solid text: its glyphs, its layers and its underlines alike. Amounts are relative to the text's
    /// own size, so a deformed text keeps its look when it is resized.
    /// </summary>
    [Serializable]
    public struct LetteraDeformer
    {
        public LetteraDeformKind Kind;
        [Tooltip("Kept in the chain, doing nothing.")] public bool Off;
        [Tooltip("Bend, twist, sphere: degrees over the text's width. Taper: how much narrower the end is, 0 to 1. Wave: height in em.")] public float Amount;
        [Tooltip("Which way it moves the text. Bend, taper and wave.")] public LetteraDeformAxis Axis;
        [Tooltip("Bend, twist, sphere: where along the width it holds still, 0 to 1.")] [Range(0, 1)] public float Center;
        [Tooltip("Wave: the length of one wave, in em.")] public float Wavelength;
        [Tooltip("Wave: degrees. Path: where the text starts along the path, 0 to 1.")] public float Phase;
        [Tooltip("Wave: degrees a second, in Play Mode.")] public float Speed;
        [Tooltip("Path: points in the text's own space, joined by a smooth curve.")] public Vector3[] Points;

        public static LetteraDeformer Bend(float degrees, LetteraDeformAxis axis = LetteraDeformAxis.Depth) => new LetteraDeformer { Kind = LetteraDeformKind.Bend, Amount = degrees, Axis = axis, Center = 0.5f };
        public static LetteraDeformer Twist(float degrees) => new LetteraDeformer { Kind = LetteraDeformKind.Twist, Amount = degrees, Center = 0.5f };
        public static LetteraDeformer Taper(float amount, LetteraDeformAxis axis = LetteraDeformAxis.Up) => new LetteraDeformer { Kind = LetteraDeformKind.Taper, Amount = amount, Axis = axis };
        public static LetteraDeformer Wave(float height, float wavelength, LetteraDeformAxis axis = LetteraDeformAxis.Depth) => new LetteraDeformer { Kind = LetteraDeformKind.Wave, Amount = height, Wavelength = wavelength, Axis = axis };
        public static LetteraDeformer Sphere(float degrees) => new LetteraDeformer { Kind = LetteraDeformKind.Sphere, Amount = degrees, Center = 0.5f };
        public static LetteraDeformer Path(params Vector3[] points) => new LetteraDeformer { Kind = LetteraDeformKind.Path, Points = points };

        /// <summary>Whether it needs the surfaces cut finer, so that a curve shows as a curve.</summary>
        internal bool Bends => !Off;

        internal ulong HashOf()
        {
            ulong h = 1469598103934665603UL;
            unchecked
            {
                void Mix(ulong v) => h = (h ^ v) * 1099511628211UL;
                Mix((ulong)Kind);
                Mix(Off ? 1UL : 0UL);
                Mix((ulong)(uint)Amount.GetHashCode());
                Mix((ulong)Axis);
                Mix((ulong)(uint)Center.GetHashCode());
                Mix((ulong)(uint)Wavelength.GetHashCode());
                Mix((ulong)(uint)Phase.GetHashCode());
                Mix((ulong)(uint)Speed.GetHashCode());
                if (Points != null)
                    foreach (Vector3 p in Points)
                        Mix((ulong)(uint)p.GetHashCode());
            }
            return h;
        }
    }

    /// <summary>A deformer as the job reads it, with the box it measures by.</summary>
    internal struct LetteraDeformOp
    {
        public int Kind, Axis;
        public float Amount, Center, Wavelength, Phase, Scale;
        public float3 Min, Max;
        public int PathStart, PathCount;
        public float PathLength;
    }

    /// <summary>Deforms a text's vertices in place: the chain of its ranges first, then the text's own, normals by the chain's derivative.</summary>
    [BurstCompile]
    internal struct LetteraDeformJob : IJob
    {
        public struct Span
        {
            public int Start, Count, OpStart, OpCount;
        }

        public NativeArray<LetteraSolidMeshVertex> Vertices;
        public int VertexCount;
        [ReadOnly] public NativeArray<LetteraDeformOp> Ops;
        public int TextOps;
        [ReadOnly] public NativeArray<Span> Spans;
        public int SpanCount;
        [ReadOnly] public NativeArray<float3> Path;
        public float Step;
        public NativeArray<float3> Ends;

        public void Execute()
        {
            float3 min = new float3(float.MaxValue), max = new float3(float.MinValue);
            int span = 0;
            for (int i = 0; i < VertexCount; i++)
            {
                while (span < SpanCount && i >= Spans[span].Start + Spans[span].Count) span++;
                int opStart = 0, opCount = 0;
                if (span < SpanCount && i >= Spans[span].Start)
                {
                    opStart = Spans[span].OpStart;
                    opCount = Spans[span].OpCount;
                }
                LetteraSolidMeshVertex v = Vertices[i];
                float3 p = v.Position;
                float3 at = Chain(p, opStart, opCount);
                float3 a = (Chain(p + new float3(Step, 0, 0), opStart, opCount) - at);
                float3 b = (Chain(p + new float3(0, Step, 0), opStart, opCount) - at);
                float3 c = (Chain(p + new float3(0, 0, Step), opStart, opCount) - at);
                float3 n = v.Normal;
                float3 turned = n.x * math.cross(b, c) + n.y * math.cross(c, a) + n.z * math.cross(a, b);
                v.Normal = math.normalizesafe(turned, n);
                v.Position = at;
                Vertices[i] = v;
                min = math.min(min, at);
                max = math.max(max, at);
            }
            Ends[0] = min;
            Ends[1] = max;
        }

        float3 Chain(float3 p, int start, int count)
        {
            for (int k = start; k < start + count; k++) p = Apply(Ops[k], p);
            for (int k = 0; k < TextOps; k++) p = Apply(Ops[k], p);
            return p;
        }

        float3 Apply(in LetteraDeformOp op, float3 p)
        {
            float width = math.max(op.Max.x - op.Min.x, 1e-4f);
            float c = op.Min.x + op.Center * width;
            float yc = (op.Min.y + op.Max.y) * 0.5f, zc = (op.Min.z + op.Max.z) * 0.5f;
            switch (op.Kind)
            {
                case (int)LetteraDeformKind.Bend:
                {
                    float angle = math.radians(op.Amount);
                    if (math.abs(angle) < 1e-5f) return p;
                    float radius = width / angle;
                    float phi = (p.x - c) / radius;
                    math.sincos(phi, out float s, out float co);
                    if (op.Axis == (int)LetteraDeformAxis.Up)
                    {
                        float r = radius + (p.y - yc);
                        return new float3(c + r * s, yc - radius + r * co, p.z);
                    }
                    float rz = radius - (p.z - zc);
                    return new float3(c + rz * s, p.y, zc + radius - rz * co);
                }
                case (int)LetteraDeformKind.Twist:
                {
                    float angle = math.radians(op.Amount) * ((p.x - c) / width);
                    math.sincos(angle, out float s, out float co);
                    float y = p.y - yc, z = p.z - zc;
                    return new float3(p.x, yc + y * co - z * s, zc + y * s + z * co);
                }
                case (int)LetteraDeformKind.Taper:
                {
                    float u = math.saturate((p.x - op.Min.x) / width);
                    float scale = 1 - op.Amount * u;
                    if (op.Axis != (int)LetteraDeformAxis.Depth) p.y = yc + (p.y - yc) * scale;
                    if (op.Axis != (int)LetteraDeformAxis.Up) p.z = zc + (p.z - zc) * scale;
                    return p;
                }
                case (int)LetteraDeformKind.Wave:
                {
                    float length = math.max(op.Wavelength, 1e-3f) * op.Scale;
                    float offset = op.Amount * op.Scale * math.sin(2 * math.PI * (p.x - op.Min.x) / length + math.radians(op.Phase));
                    if (op.Axis != (int)LetteraDeformAxis.Depth) p.y += offset;
                    if (op.Axis != (int)LetteraDeformAxis.Up) p.z += offset;
                    return p;
                }
                case (int)LetteraDeformKind.Sphere:
                {
                    float angle = math.radians(op.Amount);
                    if (math.abs(angle) < 1e-5f) return p;
                    float radius = width / angle;
                    math.sincos((p.x - c) / radius, out float s, out float co);
                    math.sincos((p.y - yc) / radius, out float ls, out float lc);
                    float r = radius - (p.z - zc);
                    return new float3(c + r * lc * s, yc + r * ls, zc + radius - r * lc * co);
                }
                case (int)LetteraDeformKind.Path:
                {
                    if (op.PathCount < 2) return p;
                    float along = op.Phase * op.PathLength + (p.x - op.Min.x);
                    float step = op.PathLength / (op.PathCount - 1);
                    float f = along / math.max(step, 1e-6f);
                    int i = (int)math.clamp(math.floor(f), 0, op.PathCount - 2);
                    float3 a = Path[op.PathStart + i], b = Path[op.PathStart + i + 1];
                    float3 tangent = math.normalizesafe(b - a, new float3(1, 0, 0));
                    float3 point = a + (b - a) * (f - i);
                    float3 up = math.abs(tangent.y) > 0.999f ? new float3(0, 0, -1) : new float3(0, 1, 0);
                    float3 u = math.normalizesafe(up - tangent * math.dot(up, tangent));
                    float3 back = math.cross(tangent, u);
                    return point + u * (p.y - yc) + back * (p.z - zc);
                }
            }
            return p;
        }
    }

    /// <summary>What a text's deformers need when its mesh is written: the ops with their boxes, and paths as even steps.</summary>
    internal sealed class LetteraDeformChain : IDisposable
    {
        internal NativeArray<LetteraDeformOp> Ops;
        internal NativeArray<LetteraDeformJob.Span> Spans;
        internal NativeArray<float3> Path;
        internal int TextOps, RangeOps, SpanCount;
        internal bool Animated;
        readonly List<LetteraDeformOp> m_Ops = new List<LetteraDeformOp>();
        readonly List<float3> m_Path = new List<float3>();
        readonly List<LetteraDeformJob.Span> m_Spans = new List<LetteraDeformJob.Span>();

        internal bool Any => TextOps + RangeOps > 0;

        internal void Begin()
        {
            m_Ops.Clear();
            m_Path.Clear();
            m_Spans.Clear();
            TextOps = 0;
            RangeOps = 0;
            Animated = false;
        }

        internal void AddText(in LetteraDeformer deformer, float3 min, float3 max, float scale, float time)
        {
            if (deformer.Off) return;
            m_Ops.Insert(TextOps, Op(deformer, min, max, scale, time));
            TextOps++;
        }

        /// <summary>Range ops come after the text's, and a span says which vertices take them.</summary>
        internal int AddRange(in LetteraDeformer deformer, float3 min, float3 max, float scale, float time)
        {
            if (deformer.Off) return -1;
            m_Ops.Add(Op(deformer, min, max, scale, time));
            RangeOps++;
            return m_Ops.Count - 1;
        }

        internal void AddSpan(int start, int count, int opStart, int opCount) => m_Spans.Add(new LetteraDeformJob.Span { Start = start, Count = count, OpStart = opStart, OpCount = opCount });

        LetteraDeformOp Op(in LetteraDeformer deformer, float3 min, float3 max, float scale, float time)
        {
            var op = new LetteraDeformOp
            {
                Kind = (int)deformer.Kind,
                Axis = (int)deformer.Axis,
                Amount = deformer.Amount,
                Center = deformer.Center,
                Wavelength = deformer.Wavelength,
                Phase = deformer.Phase + deformer.Speed * time,
                Scale = scale,
                Min = min,
                Max = max,
            };
            if (deformer.Kind == LetteraDeformKind.Wave && deformer.Speed != 0) Animated = true;
            if (deformer.Kind == LetteraDeformKind.Path) FillPath(deformer.Points, ref op);
            return op;
        }

        const int PathSteps = 256;

        // Through every point, by Catmull-Rom, then resampled to even lengths so that letters keep their spacing along it.
        void FillPath(Vector3[] points, ref LetteraDeformOp op)
        {
            op.Phase = math.saturate(op.Phase);
            if (points == null || points.Length < 2) return;
            var dense = new List<float3>(points.Length * 32);
            for (int i = 0; i < points.Length - 1; i++)
            {
                float3 p0 = points[math.max(i - 1, 0)], p1 = points[i], p2 = points[i + 1], p3 = points[math.min(i + 2, points.Length - 1)];
                for (int k = 0; k < 32; k++)
                {
                    float t = k / 32f, t2 = t * t, t3 = t2 * t;
                    dense.Add(0.5f * (2 * p1 + (p2 - p0) * t + (2 * p0 - 5 * p1 + 4 * p2 - p3) * t2 + (3 * p1 - p0 - 3 * p2 + p3) * t3));
                }
            }
            dense.Add(points[points.Length - 1]);
            var lengths = new float[dense.Count];
            for (int i = 1; i < dense.Count; i++) lengths[i] = lengths[i - 1] + math.distance(dense[i], dense[i - 1]);
            float total = lengths[dense.Count - 1];
            if (total <= 1e-6f) return;
            op.PathStart = m_Path.Count;
            op.PathCount = PathSteps;
            op.PathLength = total;
            int j = 0;
            for (int s = 0; s < PathSteps; s++)
            {
                float want = total * s / (PathSteps - 1);
                while (j < dense.Count - 2 && lengths[j + 1] < want) j++;
                float span = lengths[j + 1] - lengths[j];
                float t = span > 1e-9f ? (want - lengths[j]) / span : 0;
                m_Path.Add(math.lerp(dense[j], dense[j + 1], math.saturate(t)));
            }
        }

        static void Fit<T>(ref NativeArray<T> array, List<T> from) where T : struct
        {
            if (!array.IsCreated || array.Length < math.max(from.Count, 1))
            {
                if (array.IsCreated) array.Dispose();
                array = new NativeArray<T>(math.max(from.Count, 4), Allocator.Persistent);
            }
            for (int i = 0; i < from.Count; i++) array[i] = from[i];
        }

        /// <summary>Deforms the first count vertices in place, and gives back their box.</summary>
        internal Bounds Run(NativeArray<LetteraSolidMeshVertex> vertices, int count, float scale)
        {
            Fit(ref Ops, m_Ops);
            Fit(ref Spans, m_Spans);
            Fit(ref Path, m_Path);
            SpanCount = m_Spans.Count;
            var ends = new NativeArray<float3>(2, Allocator.TempJob);
            new LetteraDeformJob
            {
                Vertices = vertices, VertexCount = count, Ops = Ops, TextOps = TextOps, Spans = Spans, SpanCount = SpanCount, Path = Path,
                Step = math.max(scale, 1e-4f) * 1e-3f, Ends = ends,
            }.Run();
            float3 min = ends[0], max = ends[1];
            ends.Dispose();
            return count == 0 ? new Bounds(Vector3.zero, Vector3.zero) : new Bounds((min + max) * 0.5f, max - min);
        }

        internal ulong HashOf()
        {
            ulong h = 1469598103934665603UL;
            unchecked
            {
                foreach (LetteraDeformOp op in m_Ops)
                {
                    h = (h ^ (ulong)op.Kind) * 1099511628211UL;
                    h = (h ^ (uint)op.Amount.GetHashCode()) * 1099511628211UL;
                    h = (h ^ (uint)op.Phase.GetHashCode()) * 1099511628211UL;
                    h = (h ^ (uint)op.Wavelength.GetHashCode()) * 1099511628211UL;
                    h = (h ^ (uint)op.Center.GetHashCode()) * 1099511628211UL;
                    h = (h ^ (ulong)op.Axis) * 1099511628211UL;
                    h = (h ^ (uint)op.PathLength.GetHashCode()) * 1099511628211UL;
                }
            }
            return h;
        }

        public void Dispose()
        {
            if (Ops.IsCreated) Ops.Dispose();
            if (Spans.IsCreated) Spans.Dispose();
            if (Path.IsCreated) Path.Dispose();
        }
    }

    /// <summary>
    /// Cuts a solid's triangles until no edge is longer across the text's plane than asked, so that a deformer can curve it.
    /// An edge is split wherever it stands, by its two ends' places, so both triangles on it split alike and no crack opens.
    /// Edges along the depth are never split: every deformer keeps them straight.
    /// </summary>
    [BurstCompile]
    internal struct LetteraSolidSubdivideJob : IJob
    {
        public NativeList<LetteraSolidVertex> Vertices;
        public NativeList<int> Indices;
        public NativeArray<int> SurfaceStarts;
        public float MaxEdge;
        public int MaxTriangles;

        public void Execute()
        {
            if (MaxEdge <= 0 || Indices.Length == 0) return;
            float limit = MaxEdge * MaxEdge;
            var marked = new NativeHashSet<ulong>(Indices.Length, Allocator.Temp);
            var mids = new NativeHashMap<long, int>(Indices.Length, Allocator.Temp);
            var output = new NativeList<int>(Indices.Length * 2, Allocator.Temp);
            var starts = new NativeArray<int>(6, Allocator.Temp);
            for (int round = 0; round < 16; round++)
            {
                marked.Clear();
                bool any = false;
                for (int t = 0; t + 2 < Indices.Length; t += 3)
                {
                    for (int e = 0; e < 3; e++)
                    {
                        int a = Indices[t + e], b = Indices[t + (e + 1) % 3];
                        float2 d = Vertices[a].Position.xy - Vertices[b].Position.xy;
                        if (math.lengthsq(d) > limit)
                        {
                            marked.Add(EdgeKey(Vertices[a].Position, Vertices[b].Position));
                            any = true;
                        }
                    }
                }
                if (!any || Indices.Length / 3 * 4 > MaxTriangles) break;
                mids.Clear();
                output.Clear();
                for (int s = 0; s < 5; s++)
                {
                    starts[s] = output.Length;
                    for (int t = SurfaceStarts[s]; t + 2 < SurfaceStarts[s + 1]; t += 3)
                    {
                        int a = Indices[t], b = Indices[t + 1], c = Indices[t + 2];
                        bool ab = marked.Contains(EdgeKey(Vertices[a].Position, Vertices[b].Position));
                        bool bc = marked.Contains(EdgeKey(Vertices[b].Position, Vertices[c].Position));
                        bool ca = marked.Contains(EdgeKey(Vertices[c].Position, Vertices[a].Position));
                        int count = (ab ? 1 : 0) + (bc ? 1 : 0) + (ca ? 1 : 0);
                        if (count == 0)
                        {
                            Emit(ref output, a, b, c);
                            continue;
                        }
                        if (count == 3)
                        {
                            int mab = Mid(a, b, ref mids), mbc = Mid(b, c, ref mids), mca = Mid(c, a, ref mids);
                            Emit(ref output, a, mab, mca);
                            Emit(ref output, mab, b, mbc);
                            Emit(ref output, mca, mbc, c);
                            Emit(ref output, mab, mbc, mca);
                            continue;
                        }
                        // Turned so that the split edges come first, which keeps the winding.
                        if (count == 1)
                        {
                            if (bc) { int x = a; a = b; b = c; c = x; }
                            else if (ca) { int x = a; a = c; c = b; b = x; }
                            int m = Mid(a, b, ref mids);
                            Emit(ref output, a, m, c);
                            Emit(ref output, m, b, c);
                            continue;
                        }
                        if (!ca) { }
                        else if (!ab) { int x = a; a = b; b = c; c = x; }
                        else { int x = a; a = c; c = b; b = x; }
                        int m1 = Mid(a, b, ref mids), m2 = Mid(b, c, ref mids);
                        Emit(ref output, m1, b, m2);
                        Emit(ref output, a, m1, m2);
                        Emit(ref output, a, m2, c);
                    }
                }
                starts[5] = output.Length;
                Indices.Clear();
                Indices.AddRange(output.AsArray());
                for (int s = 0; s < 6; s++) SurfaceStarts[s] = starts[s];
            }
        }

        static void Emit(ref NativeList<int> output, int a, int b, int c)
        {
            output.Add(a);
            output.Add(b);
            output.Add(c);
        }

        int Mid(int a, int b, ref NativeHashMap<long, int> mids)
        {
            long key = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
            if (mids.TryGetValue(key, out int index)) return index;
            LetteraSolidVertex va = Vertices[a], vb = Vertices[b];
            var v = new LetteraSolidVertex
            {
                Position = (va.Position + vb.Position) * 0.5f,
                Normal = math.normalizesafe(va.Normal + vb.Normal, va.Normal),
                Uv = (va.Uv + vb.Uv) * 0.5f,
                Weight = (va.Weight + vb.Weight) * 0.5f,
            };
            index = Vertices.Length;
            Vertices.Add(v);
            mids.Add(key, index);
            return index;
        }

        static ulong PointKey(float3 p)
        {
            int3 q = (int3)math.round(p * 100000f);
            ulong h = 14695981039346656037UL;
            unchecked
            {
                h = (h ^ (uint)q.x) * 1099511628211UL;
                h = (h ^ (uint)q.y) * 1099511628211UL;
                h = (h ^ (uint)q.z) * 1099511628211UL;
            }
            return h;
        }

        static ulong EdgeKey(float3 a, float3 b)
        {
            ulong ka = PointKey(a), kb = PointKey(b);
            ulong low = math.min(ka, kb), high = math.max(ka, kb);
            unchecked { return low * 0x9E3779B97F4A7C15UL ^ (high + 0x632BE59BD9B4E019UL + (low << 6) + (low >> 2)); }
        }
    }
}

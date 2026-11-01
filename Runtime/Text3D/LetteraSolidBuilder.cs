using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace FirstSymbol.Lettera
{
    /// <summary>The surfaces of a solid, each of which may take its own material.</summary>
    public enum LetteraSolidSurface
    {
        Face = 0,
        Bevel = 1,
        Side = 2,
        BackBevel = 3,
        Back = 4,
    }

    /// <summary>Which surfaces a solid has. A solid with any left out is open.</summary>
    [Flags]
    public enum LetteraSolidFaces
    {
        None = 0,
        Front = 1,
        Back = 2,
        Sides = 4,
        All = Front | Back | Sides,
    }

    /// <summary>A vertex of a solid, in em. Its z grows by weight times what a text's depth leaves after its bevels, so one solid serves every depth.</summary>
    internal struct LetteraSolidVertex
    {
        public float3 Position;
        public float3 Normal;
        public float2 Uv;
        public float Weight;
    }

    /// <summary>What a build is asked for. All lengths are in em.</summary>
    internal struct LetteraSolidParams
    {
        public float Tolerance;
        public float FrontWidth, FrontHeight, BackWidth, BackHeight;
        public float SmoothingCos;
        public LetteraSolidFaces Faces;
        /// <summary>From the outline's units to em.</summary>
        public float Scale;

        public const int MaxSegments = 8192;
        public const int MaxCurveSegments = 64;
        public const long MaxTests = MaxSegments * 32L;
    }

    /// <summary>How a build went.</summary>
    internal struct LetteraSolidReport
    {
        public bool Failed;
        public int BevelsClamped;
        public int CapsDropped;
        public int Loops;
    }

    /// <summary>
    /// Turns an outline into a closed solid. Pure: no Unity object, no managed memory, and the same input gives the same bytes.
    /// Fill lies on the left of every loop, the viewer on -Z, and a triangle's outward normal is cross(b - a, c - a).
    /// </summary>
    [BurstCompile]
    internal struct LetteraSolidBuildJob : IJob
    {
        [ReadOnly] public NativeArray<float> Commands;
        public int CommandCount;
        [ReadOnly] public NativeArray<float2> FrontProfile, BackProfile;
        public LetteraSolidParams Params;

        public NativeList<LetteraSolidVertex> Vertices;
        public NativeList<int> Indices;
        /// <summary>Where each surface's triangles start in the indices, with one more entry at the end.</summary>
        public NativeArray<int> SurfaceStarts;
        /// <summary>Failed, bevels clamped, caps dropped, loops.</summary>
        public NativeArray<int> Report;

        struct Loop
        {
            public int Start, Count;
            public float Area;
            public int Parent;
        }

        const float Eps = 1e-5f;
        const float WindingProbe = 1e-4f;
        const float MinArea = 1e-7f;

        public void Execute()
        {
            for (int i = 0; i < Report.Length; i++) Report[i] = 0;
            for (int i = 0; i < SurfaceStarts.Length; i++) SurfaceStarts[i] = 0;
            var points = new NativeList<float2>(256, Allocator.Temp);
            var contours = new NativeList<int2>(8, Allocator.Temp);
            float tolerance = math.max(Params.Tolerance, 1e-5f);
            bool fits = false;
            for (int attempt = 0; attempt < 4; attempt++)
            {
                Flatten(tolerance, ref points, ref contours);
                if (points.Length <= LetteraSolidParams.MaxSegments)
                {
                    fits = true;
                    break;
                }
                tolerance *= 2;
            }
            if (!fits)
            {
                Report[0] = 1;
                return;
            }
            if (contours.Length == 0) return;
            var loops = new NativeList<Loop>(8, Allocator.Temp);
            var clean = new NativeList<float2>(points.Length, Allocator.Temp);
            if (!Clean(points, contours, ref clean, ref loops))
            {
                Report[0] = 1;
                return;
            }
            if (loops.Length == 0) return;
            ParentHoles(clean, ref loops);
            Report[3] = loops.Length;
            Emit(clean, loops);
        }

        // ------------------------------------------------------------------ flatten

        void Flatten(float tolerance, ref NativeList<float2> points, ref NativeList<int2> contours)
        {
            points.Clear();
            contours.Clear();
            float scale = Params.Scale;
            int start = 0;
            float2 pen = default;
            bool open = false;
            int i = 0, count = CommandCount;
            while (i < count)
            {
                int command = (int)Commands[i];
                switch (command)
                {
                    case 0:
                        if (i + 2 >= count) { i = count; break; }
                        if (open) FinishContour(ref points, ref contours, start);
                        start = points.Length;
                        pen = new float2(Commands[i + 1], Commands[i + 2]) * scale;
                        AddPoint(ref points, start, pen);
                        open = true;
                        i += 3;
                        break;
                    case 1:
                    {
                        if (i + 2 >= count) { i = count; break; }
                        float2 to = new float2(Commands[i + 1], Commands[i + 2]) * scale;
                        if (open) AddPoint(ref points, start, to);
                        pen = to;
                        i += 3;
                        break;
                    }
                    case 2:
                    {
                        if (i + 4 >= count) { i = count; break; }
                        float2 c = new float2(Commands[i + 1], Commands[i + 2]) * scale, to = new float2(Commands[i + 3], Commands[i + 4]) * scale;
                        if (open)
                        {
                            float d = math.length(pen - 2 * c + to);
                            int n = Segments(math.sqrt(d / (4 * tolerance)));
                            for (int k = 1; k <= n; k++)
                            {
                                float t = (float)k / n, u = 1 - t;
                                AddPoint(ref points, start, u * u * pen + 2 * u * t * c + t * t * to);
                            }
                        }
                        pen = to;
                        i += 5;
                        break;
                    }
                    case 3:
                    {
                        if (i + 6 >= count) { i = count; break; }
                        float2 a = new float2(Commands[i + 1], Commands[i + 2]) * scale, b = new float2(Commands[i + 3], Commands[i + 4]) * scale, to = new float2(Commands[i + 5], Commands[i + 6]) * scale;
                        if (open)
                        {
                            float m = math.max(math.length(pen - 2 * a + b), math.length(a - 2 * b + to));
                            int n = Segments(math.sqrt(0.75f * m / tolerance));
                            for (int k = 1; k <= n; k++)
                            {
                                float t = (float)k / n, u = 1 - t;
                                AddPoint(ref points, start, u * u * u * pen + 3 * u * u * t * a + 3 * u * t * t * b + t * t * t * to);
                            }
                        }
                        pen = to;
                        i += 7;
                        break;
                    }
                    case 4:
                        if (open) FinishContour(ref points, ref contours, start);
                        open = false;
                        i += 1;
                        break;
                    default:
                        // A stream this does not understand ends here. What was closed stays.
                        i = count;
                        break;
                }
                if (points.Length > LetteraSolidParams.MaxSegments * 2) return;
            }
            if (open) FinishContour(ref points, ref contours, start);
        }

        static int Segments(float wanted)
        {
            if (!(wanted < LetteraSolidParams.MaxCurveSegments)) return LetteraSolidParams.MaxCurveSegments;
            return math.max(1, (int)math.ceil(wanted));
        }

        static bool Finite(float2 p) => !(float.IsNaN(p.x) || float.IsNaN(p.y) || float.IsInfinity(p.x) || float.IsInfinity(p.y) || math.abs(p.x) > 1e6f || math.abs(p.y) > 1e6f);

        static void AddPoint(ref NativeList<float2> points, int start, float2 p)
        {
            if (!Finite(p)) return;
            if (points.Length > start && math.lengthsq(points[points.Length - 1] - p) < Eps * Eps) return;
            points.Add(p);
        }

        static void FinishContour(ref NativeList<float2> points, ref NativeList<int2> contours, int start)
        {
            while (points.Length - start > 1 && math.lengthsq(points[points.Length - 1] - points[start]) < Eps * Eps) points.RemoveAt(points.Length - 1);
            // Straight runs lose what they do not need.
            const float Collinear = 1.7453e-4f;
            int write = start, count = points.Length - start;
            if (count >= 3)
            {
                for (int i = 0; i < count; i++)
                {
                    float2 previous = write > start ? points[write - 1] : points[start + count - 1];
                    float2 here = points[start + i], next = points[start + (i + 1) % count];
                    float2 a = here - previous, b = next - here;
                    float la = math.length(a), lb = math.length(b);
                    bool straight = la > 0 && lb > 0 && math.abs(a.x * b.y - a.y * b.x) < Collinear * la * lb && math.dot(a, b) > 0;
                    if (straight && count - (i - (write - start)) > 3) continue;
                    points[write++] = here;
                }
                points.ResizeUninitialized(write);
            }
            if (points.Length - start >= 3) contours.Add(new int2(start, points.Length - start));
            else points.ResizeUninitialized(start);
        }

        // ------------------------------------------------------------------ clean

        struct Grid
        {
            public int2 Size;
            public float2 Origin, Cell;
            public NativeArray<int> Heads;
            public NativeList<int2> Nodes;
        }

        static Grid MakeGrid(NativeArray<float2> a, NativeArray<float2> b, int count, Allocator allocator)
        {
            float2 min = new float2(float.MaxValue), max = new float2(float.MinValue);
            for (int i = 0; i < count; i++)
            {
                min = math.min(min, math.min(a[i], b[i]));
                max = math.max(max, math.max(a[i], b[i]));
            }
            int side = math.clamp((int)math.ceil(math.sqrt(count)), 1, 128);
            var grid = new Grid
            {
                Size = new int2(side, side),
                Origin = min,
                Cell = math.max((max - min) / side, new float2(1e-4f)),
                Heads = new NativeArray<int>(side * side, allocator),
                Nodes = new NativeList<int2>(count * 2, allocator),
            };
            for (int i = 0; i < grid.Heads.Length; i++) grid.Heads[i] = -1;
            for (int i = 0; i < count; i++)
            {
                int2 low = CellOf(grid, math.min(a[i], b[i])), high = CellOf(grid, math.max(a[i], b[i]));
                for (int y = low.y; y <= high.y; y++)
                    for (int x = low.x; x <= high.x; x++)
                    {
                        int c = y * side + x;
                        grid.Nodes.Add(new int2(i, grid.Heads[c]));
                        grid.Heads[c] = grid.Nodes.Length - 1;
                    }
            }
            return grid;
        }

        static int2 CellOf(in Grid grid, float2 p) => math.clamp((int2)math.floor((p - grid.Origin) / grid.Cell), 0, grid.Size - 1);

        // Line through a at direction r against one through c at direction s.
        static bool Intersect(float2 a, float2 ab, float2 c, float2 cd, out float t, out float u)
        {
            t = u = 0;
            float d = ab.x * cd.y - ab.y * cd.x;
            if (math.abs(d) < 1e-12f) return false;
            float2 ac = c - a;
            t = (ac.x * cd.y - ac.y * cd.x) / d;
            u = (ac.x * ab.y - ac.y * ab.x) / d;
            return true;
        }

        bool Clean(NativeList<float2> points, NativeList<int2> contours, ref NativeList<float2> clean, ref NativeList<Loop> loops)
        {
            int segments = points.Length;
            var from = new NativeArray<float2>(segments, Allocator.Temp);
            var to = new NativeArray<float2>(segments, Allocator.Temp);
            var contourOf = new NativeArray<int>(segments, Allocator.Temp);
            for (int c = 0; c < contours.Length; c++)
            {
                int2 contour = contours[c];
                for (int i = 0; i < contour.y; i++)
                {
                    from[contour.x + i] = points[contour.x + i];
                    to[contour.x + i] = points[contour.x + (i + 1) % contour.y];
                    contourOf[contour.x + i] = c;
                }
            }

            // Crossings, by a grid: real glyphs cost about linear time, and a hostile one runs out of its tests.
            Grid grid = MakeGrid(from, to, segments, Allocator.Temp);
            var seen = new NativeHashSet<long>(16, Allocator.Temp);
            var splits = new NativeList<float2>(16, Allocator.Temp); // x: segment, y: t
            long tests = 0;
            for (int cell = 0; cell < grid.Heads.Length; cell++)
            {
                for (int n = grid.Heads[cell]; n >= 0; n = grid.Nodes[n].y)
                {
                    int s0 = grid.Nodes[n].x;
                    for (int m = grid.Nodes[n].y; m >= 0; m = grid.Nodes[m].y)
                    {
                        int s1 = grid.Nodes[m].x;
                        if (s0 == s1) continue;
                        if (++tests > LetteraSolidParams.MaxTests) return false;
                        int low = math.min(s0, s1), high = math.max(s0, s1);
                        if (Adjacent(contours, contourOf, low, high)) continue;
                        if (!seen.Add((long)low << 32 | (uint)high)) continue;
                        float2 ab = to[low] - from[low], cd = to[high] - from[high];
                        if (!Intersect(from[low], ab, from[high], cd, out float t, out float u)) continue;
                        const float E = 1e-6f;
                        if (t < -E || t > 1 + E || u < -E || u > 1 + E) continue;
                        splits.Add(new float2(low, math.saturate(t)));
                        splits.Add(new float2(high, math.saturate(u)));
                    }
                }
            }

            if (splits.Length == 0)
            {
                for (int c = 0; c < contours.Length; c++) KeepContour(points, contours[c], from, to, segments, ref clean, ref loops);
                return true;
            }
            return Rebuild(from, to, segments, splits, ref clean, ref loops);
        }

        static bool Adjacent(NativeList<int2> contours, NativeArray<int> contourOf, int low, int high)
        {
            if (contourOf[low] != contourOf[high]) return false;
            int2 contour = contours[contourOf[low]];
            return high == low + 1 || (low == contour.x && high == contour.x + contour.y - 1);
        }

        static int Winding(float2 p, NativeArray<float2> from, NativeArray<float2> to, int count)
        {
            int winding = 0;
            for (int i = 0; i < count; i++)
            {
                float2 a = from[i], b = to[i];
                if (a.y <= p.y)
                {
                    if (b.y > p.y && (b.x - a.x) * (p.y - a.y) - (p.x - a.x) * (b.y - a.y) > 0) winding++;
                }
                else if (b.y <= p.y && (b.x - a.x) * (p.y - a.y) - (p.x - a.x) * (b.y - a.y) < 0)
                {
                    winding--;
                }
            }
            return winding;
        }

        static float SignedArea(NativeArray<float2> p, int start, int count)
        {
            float area = 0;
            for (int i = 0; i < count; i++)
            {
                float2 a = p[start + i], b = p[start + (i + 1) % count];
                area += a.x * b.y - b.x * a.y;
            }
            return area * 0.5f;
        }

        // Fill by non-zero winding, which is what the flat text draws. A loop with fill on both sides or neither is no boundary.
        void KeepContour(NativeList<float2> points, int2 contour, NativeArray<float2> from, NativeArray<float2> to, int segments, ref NativeList<float2> clean, ref NativeList<Loop> loops)
        {
            int longest = 0;
            float best = -1;
            for (int i = 0; i < contour.y; i++)
            {
                float length = math.lengthsq(to[contour.x + i] - from[contour.x + i]);
                if (length > best)
                {
                    best = length;
                    longest = i;
                }
            }
            float2 a = from[contour.x + longest], b = to[contour.x + longest];
            float2 direction = math.normalizesafe(b - a), mid = (a + b) * 0.5f, left = new float2(-direction.y, direction.x);
            int inLeft = Winding(mid + left * WindingProbe, from, to, segments), inRight = Winding(mid - left * WindingProbe, from, to, segments);
            bool fillLeft = inLeft != 0, fillRight = inRight != 0;
            if (fillLeft == fillRight) return;
            int start = clean.Length;
            if (fillLeft)
            {
                for (int i = 0; i < contour.y; i++) clean.Add(points[contour.x + i]);
            }
            else
            {
                for (int i = contour.y - 1; i >= 0; i--) clean.Add(points[contour.x + i]);
            }
            FinishLoop(ref clean, ref loops, start);
        }

        static void FinishLoop(ref NativeList<float2> clean, ref NativeList<Loop> loops, int start)
        {
            int count = clean.Length - start;
            if (count < 3)
            {
                clean.ResizeUninitialized(start);
                return;
            }
            float area = SignedArea(clean.AsArray(), start, count);
            if (math.abs(area) < MinArea)
            {
                clean.ResizeUninitialized(start);
                return;
            }
            loops.Add(new Loop { Start = start, Count = count, Area = area, Parent = -1 });
        }

        static long VertexKey(float2 p)
        {
            int2 q = (int2)math.round(p * 50000f);
            return (long)q.x << 32 | (uint)q.y;
        }

        struct Edge
        {
            public int From, To;
            public bool Used;
        }

        // Cuts every segment where it crosses, keeps what has fill on exactly one side, and walks that into loops with fill on the left.
        bool Rebuild(NativeArray<float2> from, NativeArray<float2> to, int segments, NativeList<float2> splits, ref NativeList<float2> clean, ref NativeList<Loop> loops)
        {
            var cuts = new NativeList<float>(8, Allocator.Temp);
            var ids = new NativeHashMap<long, int>(segments * 2, Allocator.Temp);
            var positions = new NativeList<float2>(segments * 2, Allocator.Temp);
            var edges = new NativeList<Edge>(segments * 2, Allocator.Temp);
            var known = new NativeHashSet<long>(segments * 2, Allocator.Temp);

            var bySegment = new NativeArray<int>(segments, Allocator.Temp);
            var nextSplit = new NativeArray<int>(splits.Length, Allocator.Temp);
            for (int i = 0; i < segments; i++) bySegment[i] = -1;
            for (int i = 0; i < splits.Length; i++)
            {
                int s = (int)splits[i].x;
                nextSplit[i] = bySegment[s];
                bySegment[s] = i;
            }

            long tests = 0;
            for (int s = 0; s < segments; s++)
            {
                cuts.Clear();
                cuts.Add(0);
                cuts.Add(1);
                for (int i = bySegment[s]; i >= 0; i = nextSplit[i]) cuts.Add(splits[i].y);
                cuts.Sort();
                float2 a = from[s], ab = to[s] - a;
                for (int k = 0; k + 1 < cuts.Length; k++)
                {
                    float2 p = a + ab * cuts[k], q = a + ab * cuts[k + 1];
                    int u = IdOf(p, ref ids, ref positions), v = IdOf(q, ref ids, ref positions);
                    if (u == v) continue;
                    float2 direction = math.normalizesafe(positions[v] - positions[u]);
                    float2 mid = (positions[u] + positions[v]) * 0.5f, left = new float2(-direction.y, direction.x);
                    tests += segments * 2;
                    if (tests > LetteraSolidParams.MaxTests * 64) return false;
                    bool fillLeft = Winding(mid + left * WindingProbe, from, to, segments) != 0;
                    bool fillRight = Winding(mid - left * WindingProbe, from, to, segments) != 0;
                    if (fillLeft == fillRight) continue;
                    int head = fillLeft ? u : v, tail = fillLeft ? v : u;
                    if (known.Add((long)head << 32 | (uint)tail)) edges.Add(new Edge { From = head, To = tail });
                }
            }

            var firstOut = new NativeArray<int>(positions.Length, Allocator.Temp);
            var nextOut = new NativeArray<int>(edges.Length, Allocator.Temp);
            for (int i = 0; i < firstOut.Length; i++) firstOut[i] = -1;
            for (int e = 0; e < edges.Length; e++)
            {
                nextOut[e] = firstOut[edges[e].From];
                firstOut[edges[e].From] = e;
            }

            for (int e = 0; e < edges.Length; e++)
            {
                if (edges[e].Used) continue;
                int start = clean.Length, at = e, steps = 0;
                bool closed = false;
                int origin = edges[e].From;
                while (steps++ <= edges.Length)
                {
                    Edge edge = edges[at];
                    edge.Used = true;
                    edges[at] = edge;
                    clean.Add(positions[edge.From]);
                    if (edge.To == origin)
                    {
                        closed = true;
                        break;
                    }
                    // The sharpest left: the first edge turning clockwise from the way back keeps the same fill on the left.
                    float2 back = positions[edge.From] - positions[edge.To];
                    float bestAngle = float.MaxValue;
                    int best = -1;
                    for (int o = firstOut[edge.To]; o >= 0; o = nextOut[o])
                    {
                        if (edges[o].Used) continue;
                        float2 out_ = positions[edges[o].To] - positions[edge.To];
                        float angle = math.atan2(back.x * out_.y - back.y * out_.x, math.dot(back, out_));
                        // Clockwise from the way back: 0 to two pi.
                        float clockwise = angle <= 0 ? -angle : 2 * math.PI - angle;
                        if (clockwise < bestAngle)
                        {
                            bestAngle = clockwise;
                            best = o;
                        }
                    }
                    if (best < 0) break;
                    at = best;
                }
                if (closed) FinishLoop(ref clean, ref loops, start);
                else clean.ResizeUninitialized(start);
            }
            return true;
        }

        static int IdOf(float2 p, ref NativeHashMap<long, int> ids, ref NativeList<float2> positions)
        {
            long key = VertexKey(p);
            if (ids.TryGetValue(key, out int id)) return id;
            id = positions.Length;
            positions.Add(p);
            ids.Add(key, id);
            return id;
        }

        static bool InLoop(float2 p, NativeArray<float2> points, in Loop loop)
        {
            int winding = 0;
            for (int i = 0; i < loop.Count; i++)
            {
                float2 a = points[loop.Start + i], b = points[loop.Start + (i + 1) % loop.Count];
                if (a.y <= p.y)
                {
                    if (b.y > p.y && (b.x - a.x) * (p.y - a.y) - (p.x - a.x) * (b.y - a.y) > 0) winding++;
                }
                else if (b.y <= p.y && (b.x - a.x) * (p.y - a.y) - (p.x - a.x) * (b.y - a.y) < 0)
                {
                    winding--;
                }
            }
            return winding != 0;
        }

        // A hole belongs to the smallest outer around it. One around nothing is no hole at all: it goes.
        static void ParentHoles(NativeList<float2> clean, ref NativeList<Loop> loops)
        {
            NativeArray<float2> points = clean.AsArray();
            for (int h = 0; h < loops.Length; h++)
            {
                Loop hole = loops[h];
                if (hole.Area >= 0) continue;
                float bestArea = float.MaxValue;
                for (int o = 0; o < loops.Length; o++)
                {
                    Loop outer = loops[o];
                    if (outer.Area <= 0 || outer.Area >= bestArea) continue;
                    if (InLoop(points[hole.Start], points, outer))
                    {
                        bestArea = outer.Area;
                        hole.Parent = o;
                    }
                }
                loops[h] = hole;
            }
            for (int h = loops.Length - 1; h >= 0; h--)
            {
                if (loops[h].Area >= 0 || loops[h].Parent >= 0) continue;
                for (int k = 0; k < loops.Length; k++)
                {
                    if (loops[k].Parent <= h) continue;
                    Loop moved = loops[k];
                    moved.Parent--;
                    loops[k] = moved;
                }
                loops.RemoveAt(h);
            }
        }

        // ------------------------------------------------------------------ offset

        struct Offsets
        {
            public NativeArray<float2> Miter;
            public NativeArray<float> Front, Back;
        }

        // The inward direction at each point, scaled so that moving by d moves its two edges by d. Limited, so a spike takes a shorter miter.
        static NativeArray<float2> Miters(NativeArray<float2> points, NativeList<Loop> loops)
        {
            var miters = new NativeArray<float2>(points.Length, Allocator.Temp);
            for (int l = 0; l < loops.Length; l++)
            {
                Loop loop = loops[l];
                for (int i = 0; i < loop.Count; i++)
                {
                    float2 previous = points[loop.Start + (i + loop.Count - 1) % loop.Count], here = points[loop.Start + i], next = points[loop.Start + (i + 1) % loop.Count];
                    float2 a = math.normalizesafe(here - previous), b = math.normalizesafe(next - here);
                    float2 na = new float2(-a.y, a.x), nb = new float2(-b.y, b.x);
                    float2 sum = na + nb;
                    float denominator = 1 + math.dot(na, nb);
                    float2 miter = denominator > 1e-3f ? sum / denominator : na;
                    float length = math.length(miter);
                    if (length > 2) miter *= 2 / length;
                    miters[loop.Start + i] = miter;
                }
            }
            return miters;
        }

        // How far each point may go in: half of the way along its miter to the nearest edge not its own, so an inset never crosses a stroke's middle.
        static NativeArray<float> Limits(NativeArray<float2> points, NativeArray<float2> miters, NativeList<Loop> loops, float width)
        {
            var limits = new NativeArray<float>(points.Length, Allocator.Temp);
            var from = new NativeArray<float2>(points.Length, Allocator.Temp);
            var to = new NativeArray<float2>(points.Length, Allocator.Temp);
            var neighbourA = new NativeArray<int>(points.Length, Allocator.Temp);
            var neighbourB = new NativeArray<int>(points.Length, Allocator.Temp);
            for (int l = 0; l < loops.Length; l++)
            {
                Loop loop = loops[l];
                for (int i = 0; i < loop.Count; i++)
                {
                    int here = loop.Start + i;
                    from[here] = points[here];
                    to[here] = points[loop.Start + (i + 1) % loop.Count];
                    neighbourA[here] = loop.Start + (i + loop.Count - 1) % loop.Count;
                    neighbourB[here] = here;
                }
            }
            Grid grid = MakeGrid(from, to, points.Length, Allocator.Temp);
            float reach = width * 2 * 2 + 1e-4f;
            int stamp = 0;
            var visited = new NativeArray<int>(points.Length, Allocator.Temp);
            for (int v = 0; v < points.Length; v++)
            {
                float2 p = points[v], miter = miters[v];
                float length = math.length(miter);
                float along = math.max(length, 1e-5f);
                float2 direction = miter / along;
                float nearest = float.MaxValue;
                stamp++;
                int2 low = CellOf(grid, p - reach), high = CellOf(grid, p + reach);
                for (int y = low.y; y <= high.y; y++)
                    for (int x = low.x; x <= high.x; x++)
                        for (int n = grid.Heads[y * grid.Size.x + x]; n >= 0; n = grid.Nodes[n].y)
                        {
                            int s = grid.Nodes[n].x;
                            if (visited[s] == stamp) continue;
                            visited[s] = stamp;
                            if (s == neighbourA[v] || s == neighbourB[v]) continue;
                            if (!Intersect(p, direction, from[s], to[s] - from[s], out float t, out float u)) continue;
                            if (t > 1e-6f && u >= 0 && u <= 1 && t < nearest) nearest = t;
                        }
                // In units of the inset, which moves the point by miter length per unit.
                limits[v] = nearest == float.MaxValue ? float.MaxValue : nearest * 0.5f / along;
            }
            // No sawtooth: a point takes the least of its neighbours'.
            var smoothed = new NativeArray<float>(points.Length, Allocator.Temp);
            for (int l = 0; l < loops.Length; l++)
            {
                Loop loop = loops[l];
                for (int i = 0; i < loop.Count; i++)
                {
                    float least = float.MaxValue;
                    for (int k = -2; k <= 2; k++) least = math.min(least, limits[loop.Start + (i + k + loop.Count * 4) % loop.Count]);
                    smoothed[loop.Start + i] = least;
                }
            }
            return smoothed;
        }

        // Whether a ring moved in by these amounts crosses itself, or another loop's ring.
        static bool RingCrosses(NativeArray<float2> points, NativeArray<float2> miters, NativeArray<float> insets, NativeList<Loop> loops)
        {
            var ring = new NativeArray<float2>(points.Length, Allocator.Temp);
            for (int i = 0; i < points.Length; i++) ring[i] = points[i] + miters[i] * insets[i];
            var from = new NativeArray<float2>(points.Length, Allocator.Temp);
            var to = new NativeArray<float2>(points.Length, Allocator.Temp);
            for (int l = 0; l < loops.Length; l++)
            {
                Loop loop = loops[l];
                for (int i = 0; i < loop.Count; i++)
                {
                    from[loop.Start + i] = ring[loop.Start + i];
                    to[loop.Start + i] = ring[loop.Start + (i + 1) % loop.Count];
                }
            }
            Grid grid = MakeGrid(from, to, points.Length, Allocator.Temp);
            long tests = 0;
            for (int cell = 0; cell < grid.Heads.Length; cell++)
                for (int n = grid.Heads[cell]; n >= 0; n = grid.Nodes[n].y)
                {
                    int s0 = grid.Nodes[n].x;
                    for (int m = grid.Nodes[n].y; m >= 0; m = grid.Nodes[m].y)
                    {
                        int s1 = grid.Nodes[m].x;
                        if (s0 == s1) continue;
                        if (++tests > LetteraSolidParams.MaxTests) return true;
                        if (Touching(loops, s0, s1)) continue;
                        if (!Intersect(from[s0], to[s0] - from[s0], from[s1], to[s1] - from[s1], out float t, out float u)) continue;
                        const float E = 1e-4f;
                        if (t > E && t < 1 - E && u > E && u < 1 - E) return true;
                    }
                }
            return false;
        }

        static bool Touching(NativeList<Loop> loops, int a, int b)
        {
            for (int l = 0; l < loops.Length; l++)
            {
                Loop loop = loops[l];
                if (a < loop.Start || a >= loop.Start + loop.Count) continue;
                if (b < loop.Start || b >= loop.Start + loop.Count) return false;
                int gap = math.abs(a - b);
                return gap == 1 || gap == loop.Count - 1;
            }
            return false;
        }

        NativeArray<float> InsetsFor(NativeArray<float2> points, NativeArray<float2> miters, NativeList<Loop> loops, ref float width, ref float height)
        {
            var insets = new NativeArray<float>(points.Length, Allocator.Temp);
            if (width <= 0) return insets;
            NativeArray<float> limits = Limits(points, miters, loops, width);
            bool clamped = false;
            for (int i = 0; i < points.Length; i++)
            {
                insets[i] = math.min(width, limits[i]);
                clamped |= limits[i] < width;
            }
            for (int attempt = 0; attempt < 4; attempt++)
            {
                if (!RingCrosses(points, miters, insets, loops))
                {
                    if (clamped || attempt > 0) Report[1] = Report[1] + 1;
                    return insets;
                }
                if (attempt == 3) break;
                for (int i = 0; i < insets.Length; i++) insets[i] *= 0.5f;
            }
            // Never a broken mesh: this edge goes plain.
            Report[1] = Report[1] + 1;
            width = 0;
            height = 0;
            for (int i = 0; i < insets.Length; i++) insets[i] = 0;
            return insets;
        }

        // ------------------------------------------------------------------ emit

        struct Writer
        {
            public NativeList<LetteraSolidVertex> Vertices;
            public NativeHashMap<ulong, int> Welded;
        }

        static ulong HashOf(in LetteraSolidVertex v)
        {
            int3 p = (int3)math.round(v.Position * 100000f), n = (int3)math.round(v.Normal * 2048f);
            int2 uv = (int2)math.round(v.Uv * 10000f);
            ulong h = 14695981039346656037UL;
            unchecked
            {
                h = (h ^ (uint)p.x) * 1099511628211UL;
                h = (h ^ (uint)p.y) * 1099511628211UL;
                h = (h ^ (uint)p.z) * 1099511628211UL;
                h = (h ^ (uint)n.x) * 1099511628211UL;
                h = (h ^ (uint)n.y) * 1099511628211UL;
                h = (h ^ (uint)n.z) * 1099511628211UL;
                h = (h ^ (uint)uv.x) * 1099511628211UL;
                h = (h ^ (uint)uv.y) * 1099511628211UL;
                h = (h ^ (uint)(v.Weight * 2)) * 1099511628211UL;
            }
            return h;
        }

        static int Add(ref Writer writer, in LetteraSolidVertex vertex)
        {
            ulong key = HashOf(vertex);
            if (writer.Welded.TryGetValue(key, out int index)) return index;
            index = writer.Vertices.Length;
            writer.Vertices.Add(vertex);
            writer.Welded.Add(key, index);
            return index;
        }

        struct PathPoint
        {
            public float T;      // Share of the point's inset.
            public float Z;      // At build, with the side at no length.
            public float Weight;
            public float Along;  // Distance along the profile.
        }

        // Each stretch of the profile, as normal in (outward, z), and its surface.
        struct PathSegment
        {
            public float2 Normal;
            public LetteraSolidSurface Surface;
            public float2 AtStart, AtEnd;
        }

        void Emit(NativeList<float2> clean, NativeList<Loop> loops)
        {
            NativeArray<float2> points = clean.AsArray();
            NativeArray<float2> miters = Miters(points, loops);
            float frontWidth = math.max(Params.FrontWidth, 0), frontHeight = FrontProfile.Length >= 2 ? Params.FrontHeight : 0;
            float backWidth = math.max(Params.BackWidth, 0), backHeight = BackProfile.Length >= 2 ? Params.BackHeight : 0;
            if (FrontProfile.Length < 2) frontWidth = 0;
            if (BackProfile.Length < 2) backWidth = 0;
            NativeArray<float> front = InsetsFor(points, miters, loops, ref frontWidth, ref frontHeight);
            NativeArray<float> back = InsetsFor(points, miters, loops, ref backWidth, ref backHeight);
            bool hasFront = frontWidth > 0 || frontHeight != 0, hasBack = backWidth > 0 || backHeight != 0;

            // From the middle of the face, over the rim and down the side, to the middle of the back.
            var path = new NativeList<PathPoint>(32, Allocator.Temp);
            var frontOwner = new NativeList<bool>(32, Allocator.Temp);
            if (hasFront)
            {
                for (int k = FrontProfile.Length - 1; k >= 0; k--)
                {
                    float2 p = FrontProfile[k];
                    path.Add(new PathPoint { T = p.x, Z = frontHeight * (1 - p.y), Weight = 0 });
                    frontOwner.Add(true);
                }
            }
            else
            {
                path.Add(new PathPoint { T = 0, Z = 0, Weight = 0 });
                frontOwner.Add(true);
            }
            int sideStart = path.Length - 1;
            if (hasBack)
            {
                for (int k = 0; k < BackProfile.Length; k++)
                {
                    float2 p = BackProfile[k];
                    path.Add(new PathPoint { T = p.x, Z = frontHeight + backHeight * p.y, Weight = 1 });
                    frontOwner.Add(false);
                }
            }
            else
            {
                path.Add(new PathPoint { T = 0, Z = frontHeight, Weight = 1 });
                frontOwner.Add(false);
            }

            int segmentCount = path.Length - 1;
            var segments = new NativeArray<PathSegment>(segmentCount, Allocator.Temp);
            for (int s = 0; s < segmentCount; s++)
            {
                PathPoint a = path[s], b = path[s + 1];
                LetteraSolidSurface surface;
                float widthOf;
                if (s < sideStart)
                {
                    surface = LetteraSolidSurface.Bevel;
                    widthOf = frontWidth;
                }
                else if (s == sideStart)
                {
                    surface = LetteraSolidSurface.Side;
                    widthOf = 0;
                }
                else
                {
                    surface = LetteraSolidSurface.BackBevel;
                    widthOf = backWidth;
                }
                // Toward the side, inset falls: that is outward.
                float stepOut = (a.T - b.T) * widthOf, stepZ = b.Z - a.Z;
                if (s == sideStart)
                {
                    stepOut = 0;
                    stepZ = 1;
                }
                float2 normal = math.normalizesafe(new float2(stepZ, -stepOut), new float2(1, 0));
                segments[s] = new PathSegment { Normal = normal, Surface = surface, AtStart = normal, AtEnd = normal };
            }
            float smoothing = Params.SmoothingCos;
            for (int s = 0; s + 1 < segmentCount; s++)
            {
                PathSegment a = segments[s], b = segments[s + 1];
                if (math.dot(a.Normal, b.Normal) < smoothing) continue;
                float2 shared = math.normalizesafe(a.Normal + b.Normal, a.Normal);
                a.AtEnd = shared;
                b.AtStart = shared;
                segments[s] = a;
                segments[s + 1] = b;
            }
            if (segmentCount > 0)
            {
                // A bevel whose end is tangent to its cap shades into it.
                PathSegment first = segments[0];
                if (hasFront && math.dot(first.Normal, new float2(0, -1)) >= smoothing) first.AtStart = new float2(0, -1);
                segments[0] = first;
                PathSegment last = segments[segmentCount - 1];
                if (hasBack && math.dot(last.Normal, new float2(0, 1)) >= smoothing) last.AtEnd = new float2(0, 1);
                segments[segmentCount - 1] = last;
            }

            var writer = new Writer
            {
                Vertices = Vertices,
                Welded = new NativeHashMap<ulong, int>(points.Length * 8, Allocator.Temp),
            };
            var bySurface = new NativeArray<NativeList<int>>(5, Allocator.Temp);
            for (int i = 0; i < 5; i++) bySurface[i] = new NativeList<int>(64, Allocator.Temp);

            bool sides = (Params.Faces & LetteraSolidFaces.Sides) != 0;
            for (int l = 0; l < loops.Length; l++)
            {
                Loop loop = loops[l];
                // Along each loop's contour, for the band's U.
                float along = 0;
                for (int e = 0; e < loop.Count; e++)
                {
                    int i0 = loop.Start + e, i1 = loop.Start + (e + 1) % loop.Count;
                    float2 p0 = points[i0], p1 = points[i1];
                    float length = math.length(p1 - p0);
                    if (length < 1e-7f) continue;
                    float2 direction = (p1 - p0) / length, outward = new float2(direction.y, -direction.x);
                    float2 out0 = CornerNormal(points, loop, e, outward), out1 = CornerNormal(points, loop, (e + 1) % loop.Count, outward);
                    float v = 0;
                    for (int s = 0; s < segmentCount; s++)
                    {
                        PathSegment segment = segments[s];
                        PathPoint a = path[s], b = path[s + 1];
                        NativeArray<float> insetsA = frontOwner[s] ? front : back, insetsB = frontOwner[s + 1] ? front : back;
                        float3 a0 = PointAt(p0, miters[i0], insetsA[i0], a), a1 = PointAt(p1, miters[i1], insetsA[i1], a);
                        float3 b0 = PointAt(p0, miters[i0], insetsB[i0], b), b1 = PointAt(p1, miters[i1], insetsB[i1], b);
                        float stretch = s == sideStart ? 0 : math.distance(a0, b0);
                        if (segment.Surface != LetteraSolidSurface.Side || sides)
                        {
                            NativeList<int> target = bySurface[(int)segment.Surface];
                            int n00 = Add(ref writer, new LetteraSolidVertex { Position = a0, Normal = Normal3(out0, segment.AtStart), Uv = new float2(along, v), Weight = a.Weight });
                            int n01 = Add(ref writer, new LetteraSolidVertex { Position = a1, Normal = Normal3(out1, segment.AtStart), Uv = new float2(along + length, v), Weight = a.Weight });
                            int n10 = Add(ref writer, new LetteraSolidVertex { Position = b0, Normal = Normal3(out0, segment.AtEnd), Uv = new float2(along, v + stretch), Weight = b.Weight });
                            int n11 = Add(ref writer, new LetteraSolidVertex { Position = b1, Normal = Normal3(out1, segment.AtEnd), Uv = new float2(along + length, v + stretch), Weight = b.Weight });
                            Triangle(ref target, a0, a1, b1, a.Weight, a.Weight, b.Weight, n00, n01, n11);
                            Triangle(ref target, a0, b1, b0, a.Weight, b.Weight, b.Weight, n00, n11, n10);
                            bySurface[(int)segment.Surface] = target;
                        }
                        v += stretch;
                    }
                    along += length;
                }
            }

            // Caps, on the innermost ring of each side.
            if ((Params.Faces & LetteraSolidFaces.Front) != 0)
                Caps(points, miters, front, loops, path[0], true, ref writer, ref bySurface);
            if ((Params.Faces & LetteraSolidFaces.Back) != 0)
                Caps(points, miters, back, loops, path[path.Length - 1], false, ref writer, ref bySurface);

            int at = 0;
            for (int s = 0; s < 5; s++)
            {
                SurfaceStarts[s] = at;
                NativeList<int> list = bySurface[s];
                for (int i = 0; i < list.Length; i++) Indices.Add(list[i]);
                at += list.Length;
            }
            SurfaceStarts[5] = at;
        }

        static void Triangle(ref NativeList<int> target, float3 a, float3 b, float3 c, float wa, float wb, float wc, int ia, int ib, int ic)
        {
            // Two corners on one spot at every depth make no triangle, and its neighbours close over it.
            bool ab = wa == wb && math.lengthsq(a - b) < 1e-12f, bc = wb == wc && math.lengthsq(b - c) < 1e-12f, ca = wc == wa && math.lengthsq(c - a) < 1e-12f;
            if (ab || bc || ca) return;
            target.Add(ia);
            target.Add(ib);
            target.Add(ic);
        }

        static float3 PointAt(float2 p, float2 miter, float inset, in PathPoint at)
        {
            float2 moved = p + miter * inset * at.T;
            return new float3(moved.x, moved.y, at.Z);
        }

        static float3 Normal3(float2 outward, float2 profile) => math.normalizesafe(new float3(outward * profile.x, profile.y), new float3(0, 0, -1));

        // Edges within the smoothing angle share a normal at their corner; sharper, each keeps its own.
        float2 CornerNormal(NativeArray<float2> points, in Loop loop, int corner, float2 own)
        {
            int count = loop.Count;
            float2 previous = points[loop.Start + (corner + count - 1) % count], here = points[loop.Start + corner], next = points[loop.Start + (corner + 1) % count];
            float2 a = math.normalizesafe(here - previous), b = math.normalizesafe(next - here);
            float2 na = new float2(a.y, -a.x), nb = new float2(b.y, -b.x);
            if (math.dot(na, nb) < Params.SmoothingCos) return own;
            return math.normalizesafe(na + nb, own);
        }

        // ------------------------------------------------------------------ caps

        void Caps(NativeArray<float2> points, NativeArray<float2> miters, NativeArray<float> insets, NativeList<Loop> loops, in PathPoint at, bool front, ref Writer writer, ref NativeArray<NativeList<int>> bySurface)
        {
            var ring = new NativeArray<float2>(points.Length, Allocator.Temp);
            for (int i = 0; i < points.Length; i++) ring[i] = points[i] + miters[i] * insets[i] * at.T;
            LetteraSolidSurface surface = front ? LetteraSolidSurface.Face : LetteraSolidSurface.Back;
            NativeList<int> target = bySurface[(int)surface];
            float3 normal = new float3(0, 0, front ? -1 : 1);
            var triangles = new NativeList<int>(points.Length * 3, Allocator.Temp);
            for (int o = 0; o < loops.Length; o++)
            {
                if (loops[o].Area <= 0) continue;
                triangles.Clear();
                int why = LetteraEarClipper.Triangulate(ring, loops.AsArray().Reinterpret<int4>(), o, ref triangles);
                if (why != 0)
                {
                    Report[2] = Report[2] + 1;
                    Report[4] = why;
                    continue;
                }
                for (int t = 0; t + 2 < triangles.Length; t += 3)
                {
                    int ia = triangles[t], ib = triangles[t + 1], ic = triangles[t + 2];
                    float2 pa = ring[ia], pb = ring[ib], pc = ring[ic];
                    int a = Add(ref writer, new LetteraSolidVertex { Position = new float3(pa, at.Z), Normal = normal, Uv = pa, Weight = at.Weight });
                    int b = Add(ref writer, new LetteraSolidVertex { Position = new float3(pb, at.Z), Normal = normal, Uv = pb, Weight = at.Weight });
                    int c = Add(ref writer, new LetteraSolidVertex { Position = new float3(pc, at.Z), Normal = normal, Uv = pc, Weight = at.Weight });
                    if (a == b || b == c || c == a) continue;
                    target.Add(a);
                    target.Add(front ? c : b);
                    target.Add(front ? b : c);
                }
            }
            bySurface[(int)surface] = target;
        }
    }

    /// <summary>Ear clipping with holes merged by bridges, on loops with fill on the left.</summary>
    internal static class LetteraEarClipper
    {
        struct Node
        {
            public int Vertex, Previous, Next;
            public bool Gone;
        }

        // Loop reinterpreted: Start, Count, Area (as bits), Parent.
        internal static int Triangulate(NativeArray<float2> ring, NativeArray<int4> loops, int outer, ref NativeList<int> triangles)
        {
            int total = loops[outer].y;
            int holes = 0;
            for (int h = 0; h < loops.Length; h++)
            {
                if (loops[h].w != outer || h == outer) continue;
                holes++;
                total += loops[h].y + 2;
            }
            var nodes = new NativeList<Node>(total, Allocator.Temp);
            int first = AddLoop(ref nodes, loops[outer].x, loops[outer].y);
            float expected = LoopArea(ring, loops[outer]);

            if (holes > 0)
            {
                // Rightmost first, so that each bridge sees past those still to come.
                var order = new NativeList<float2>(holes, Allocator.Temp);
                for (int h = 0; h < loops.Length; h++)
                {
                    if (loops[h].w != outer || h == outer) continue;
                    float most = float.MinValue;
                    for (int i = 0; i < loops[h].y; i++) most = math.max(most, ring[loops[h].x + i].x);
                    order.Add(new float2(most, h));
                    expected += LoopArea(ring, loops[h]);
                }
                for (int a = 1; a < order.Length; a++)
                {
                    float2 key = order[a];
                    int b = a - 1;
                    while (b >= 0 && order[b].x < key.x)
                    {
                        order[b + 1] = order[b];
                        b--;
                    }
                    order[b + 1] = key;
                }
                for (int k = 0; k < order.Length; k++)
                    if (!Bridge(ring, ref nodes, first, loops[(int)order[k].y]))
                        return 1;
            }

            int start = triangles.Length;
            if (!Clip(ring, ref nodes, first, ref triangles))
            {
                triangles.ResizeUninitialized(start);
                return 2;
            }
            float area = 0;
            for (int t = start; t + 2 < triangles.Length; t += 3)
                area += Cross(ring[triangles[t]], ring[triangles[t + 1]], ring[triangles[t + 2]]) * 0.5f;
            if (math.abs(area - expected) > math.abs(expected) * 0.001f + 1e-5f)
            {
                triangles.ResizeUninitialized(start);
                return 3;
            }
            return 0;
        }

        static float LoopArea(NativeArray<float2> ring, int4 loop)
        {
            float area = 0;
            for (int i = 0; i < loop.y; i++)
            {
                float2 a = ring[loop.x + i], b = ring[loop.x + (i + 1) % loop.y];
                area += a.x * b.y - b.x * a.y;
            }
            return area * 0.5f;
        }

        static int AddLoop(ref NativeList<Node> nodes, int start, int count)
        {
            int first = nodes.Length;
            for (int i = 0; i < count; i++)
                nodes.Add(new Node { Vertex = start + i, Previous = first + (i + count - 1) % count, Next = first + (i + 1) % count });
            return first;
        }

        static float Cross(float2 a, float2 b, float2 c) => (b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x);

        static bool Bridge(NativeArray<float2> ring, ref NativeList<Node> nodes, int first, int4 hole)
        {
            int holeFirst = AddLoop(ref nodes, hole.x, hole.y);
            int m = holeFirst;
            for (int i = 0; i < hole.y; i++)
            {
                float2 p = ring[nodes[holeFirst + i].Vertex], best = ring[nodes[m].Vertex];
                if (p.x > best.x || (p.x == best.x && p.y < best.y)) m = holeFirst + i;
            }
            float2 from = ring[nodes[m].Vertex];
            // The first edge a ray to the right meets, then the point of it that is visible.
            int hit = -1;
            float nearest = float.MaxValue;
            int n = first;
            int guard = nodes.Length + 4;
            do
            {
                int next = nodes[n].Next;
                float2 a = ring[nodes[n].Vertex], b = ring[nodes[next].Vertex];
                if (from.y <= math.max(a.y, b.y) && from.y >= math.min(a.y, b.y) && a.y != b.y)
                {
                    float x = a.x + (from.y - a.y) * (b.x - a.x) / (b.y - a.y);
                    if (x >= from.x && x < nearest)
                    {
                        nearest = x;
                        hit = a.x > b.x ? n : next;
                    }
                }
                n = next;
            }
            while (n != first && --guard > 0);
            if (hit < 0) return false;

            float2 mid = new float2(nearest, from.y), candidate = ring[nodes[hit].Vertex];
            float bestTan = float.MaxValue;
            int chosen = hit;
            n = first;
            guard = nodes.Length + 4;
            do
            {
                float2 p = ring[nodes[n].Vertex];
                if (p.x >= from.x && p.x <= candidate.x && !Same(p, candidate) && InTriangle(from.y < candidate.y ? from : mid, candidate, from.y < candidate.y ? mid : from, p))
                {
                    float tan = math.abs(from.y - p.y) / math.max(p.x - from.x, 1e-9f);
                    int previous = nodes[n].Previous, next = nodes[n].Next;
                    bool reflex = Cross(ring[nodes[previous].Vertex], p, ring[nodes[next].Vertex]) < 0;
                    if (reflex && (tan < bestTan || (tan == bestTan && p.x > ring[nodes[chosen].Vertex].x)))
                    {
                        bestTan = tan;
                        chosen = n;
                    }
                }
                n = nodes[n].Next;
            }
            while (n != first && --guard > 0);

            // Two new nodes make the bridge: there and back again.
            int outerNode = chosen, holeNode = m;
            int outerNext = nodes[outerNode].Next, holePrevious = nodes[holeNode].Previous;
            int outerCopy = nodes.Length;
            nodes.Add(new Node { Vertex = nodes[outerNode].Vertex, Previous = -1, Next = outerNext });
            int holeCopy = nodes.Length;
            nodes.Add(new Node { Vertex = nodes[holeNode].Vertex, Previous = holePrevious, Next = outerCopy });
            Node c = nodes[outerCopy];
            c.Previous = holeCopy;
            nodes[outerCopy] = c;
            Node q = nodes[outerNext];
            q.Previous = outerCopy;
            nodes[outerNext] = q;
            Node r = nodes[holePrevious];
            r.Next = holeCopy;
            nodes[holePrevious] = r;
            Node a2 = nodes[outerNode];
            a2.Next = holeNode;
            nodes[outerNode] = a2;
            Node b2 = nodes[holeNode];
            b2.Previous = outerNode;
            nodes[holeNode] = b2;
            return true;
        }

        static bool Same(float2 a, float2 b) => a.x == b.x && a.y == b.y;

        static bool InTriangle(float2 a, float2 b, float2 c, float2 p) =>
            Cross(a, b, p) >= -1e-9f && Cross(b, c, p) >= -1e-9f && Cross(c, a, p) >= -1e-9f;

        // Strictly inside: a point on the triangle's edge touches it and blocks nothing, and every polygon edge is still used once.
        static bool StrictlyIn(float2 a, float2 b, float2 c, float2 p, float margin) =>
            Cross(a, b, p) > margin && Cross(b, c, p) > margin && Cross(c, a, p) > margin;

        static bool Clip(NativeArray<float2> ring, ref NativeList<Node> nodes, int first, ref NativeList<int> triangles)
        {
            int remaining = 0;
            int n = first;
            int guard = nodes.Length + 4;
            do
            {
                remaining++;
                n = nodes[n].Next;
            }
            while (n != first && --guard > 0);
            if (guard == 0) return false;

            long budget = 64L * remaining * math.max(remaining, 64) + 4096;
            int current = first, stalled = 0;
            while (remaining > 3)
            {
                if (--budget < 0) return false;
                Node node = nodes[current];
                int previous = node.Previous, next = node.Next;
                float2 a = ring[nodes[previous].Vertex], b = ring[node.Vertex], c = ring[next < 0 ? 0 : nodes[next].Vertex];
                float area = Cross(a, b, c);
                bool stuck = stalled > remaining;
                // A ring moved in by a clamped inset can fold by a hair. Past that point the area check at the end is what decides.
                float margin = stuck ? 1e-6f : 0;
                bool ear = area > 0 || (stuck && area >= -1e-6f);
                if (ear)
                {
                    int walk = nodes[next].Next;
                    while (walk != previous)
                    {
                        Node other = nodes[walk];
                        float2 p = ring[other.Vertex];
                        budget--;
                        if (!Same(p, a) && !Same(p, b) && !Same(p, c) && StrictlyIn(a, b, c, p, margin))
                        {
                            float2 before = ring[nodes[other.Previous].Vertex], after = ring[nodes[other.Next].Vertex];
                            if (Cross(before, p, after) < 0 || stuck == false)
                            {
                                ear = false;
                                break;
                            }
                        }
                        walk = other.Next;
                    }
                }
                if (ear)
                {
                    triangles.Add(nodes[previous].Vertex);
                    triangles.Add(node.Vertex);
                    triangles.Add(nodes[next].Vertex);
                    Node p = nodes[previous];
                    p.Next = next;
                    nodes[previous] = p;
                    Node q = nodes[next];
                    q.Previous = previous;
                    nodes[next] = q;
                    remaining--;
                    current = next;
                    stalled = 0;
                    continue;
                }
                current = next;
                if (++stalled > remaining * 3 + 3) return false;
            }
            Node last = nodes[current];
            triangles.Add(nodes[last.Previous].Vertex);
            triangles.Add(last.Vertex);
            triangles.Add(nodes[last.Next].Vertex);
            return true;
        }
    }

    /// <summary>The shapes of an edge, as (inset share, height share) from the side (0, 0) to the face (1, 1).</summary>
    public enum LetteraBevelShape
    {
        None,
        Chamfer,
        Round,
        Cove,
        Bead,
        Step,
        TwoSteps,
        Ogee,
        Engraved,
        Custom,
    }

    /// <summary>The profile functions behind each edge shape. Small closed forms, and each starts and ends in the same two places.</summary>
    internal static class LetteraBevelProfile
    {
        const int MaxSegments = 16;

        /// <summary>Fills a profile, from the side to the face. A curve is sampled once and handed over as points, so that nothing managed reaches a build.</summary>
        internal static void Fill(LetteraBevelShape shape, float roundness, int segments, AnimationCurveSampler custom, ref NativeList<float2> output)
        {
            output.Clear();
            segments = math.clamp(segments, 1, MaxSegments);
            switch (shape)
            {
                case LetteraBevelShape.None:
                    break;
                case LetteraBevelShape.Chamfer:
                    output.Add(new float2(0, 0));
                    output.Add(new float2(1, 1));
                    break;
                case LetteraBevelShape.Round:
                {
                    // A superellipse, where 0.5 is a quarter circle, lower is hollow and higher nearly square.
                    float exponent = math.max(0.25f, math.pow(4, math.clamp(roundness, 0.02f, 0.98f) * 2 - 1) * 2);
                    for (int i = 0; i <= segments; i++)
                    {
                        float angle = 0.5f * math.PI * i / segments;
                        float t = 1 - math.pow(math.cos(angle), 2 / exponent);
                        float h = math.pow(math.sin(angle), 2 / exponent);
                        output.Add(new float2(t, h));
                    }
                    break;
                }
                case LetteraBevelShape.Cove:
                    for (int i = 0; i <= segments; i++)
                    {
                        float angle = 0.5f * math.PI * i / segments;
                        output.Add(new float2(math.sin(angle), 1 - math.cos(angle)));
                    }
                    break;
                case LetteraBevelShape.Bead:
                {
                    // A half circle bump on a straight rise.
                    int half = math.max(2, segments);
                    for (int i = 0; i <= half; i++)
                    {
                        float angle = math.PI * i / half;
                        float t = (1 - math.cos(angle)) * 0.5f;
                        output.Add(new float2(t, t + math.sin(angle) * 0.25f));
                    }
                    Clamp(ref output);
                    break;
                }
                case LetteraBevelShape.Step:
                    output.Add(new float2(0, 0));
                    output.Add(new float2(0, 0.5f));
                    output.Add(new float2(0.5f, 0.5f));
                    output.Add(new float2(0.5f, 1));
                    output.Add(new float2(1, 1));
                    break;
                case LetteraBevelShape.TwoSteps:
                    output.Add(new float2(0, 0));
                    output.Add(new float2(0, 1f / 3));
                    output.Add(new float2(1f / 3, 1f / 3));
                    output.Add(new float2(1f / 3, 2f / 3));
                    output.Add(new float2(2f / 3, 2f / 3));
                    output.Add(new float2(2f / 3, 1));
                    output.Add(new float2(1, 1));
                    break;
                case LetteraBevelShape.Ogee:
                    for (int i = 0; i <= segments * 2; i++)
                    {
                        float t = (float)i / (segments * 2);
                        output.Add(new float2(t, t * t * (3 - 2 * t)));
                    }
                    break;
                case LetteraBevelShape.Engraved:
                {
                    // Up to a rim, then down to a face that stands lower than it.
                    output.Add(new float2(0, 0));
                    output.Add(new float2(0.3f, 1));
                    output.Add(new float2(0.6f, 1));
                    output.Add(new float2(1, 0.6f));
                    break;
                }
                case LetteraBevelShape.Custom:
                {
                    int samples = segments * 4;
                    for (int i = 0; i <= samples; i++)
                    {
                        float t = (float)i / samples;
                        output.Add(new float2(t, custom.Sample(t)));
                    }
                    float2 start = output[0], end = output[output.Length - 1];
                    start.y = 0;
                    end.y = 1;
                    output[0] = start;
                    output[output.Length - 1] = end;
                    break;
                }
            }
        }

        static void Clamp(ref NativeList<float2> points)
        {
            for (int i = 0; i < points.Length; i++)
            {
                float2 p = points[i];
                p.y = math.saturate(p.y);
                points[i] = p;
            }
            points[0] = new float2(0, 0);
            points[points.Length - 1] = new float2(1, 1);
        }
    }

    /// <summary>A curve as plain samples. Whatever a hostile curve holds comes out finite and in range.</summary>
    internal readonly struct AnimationCurveSampler
    {
        readonly UnityEngine.AnimationCurve m_Curve;

        public AnimationCurveSampler(UnityEngine.AnimationCurve curve) => m_Curve = curve;

        public float Sample(float t)
        {
            if (m_Curve == null || m_Curve.length == 0) return t;
            float value = m_Curve.Evaluate(t);
            if (float.IsNaN(value) || float.IsInfinity(value)) return t;
            return math.saturate(value);
        }
    }
}

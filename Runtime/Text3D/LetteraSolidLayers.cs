using System;
using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace FirstSymbol.Lettera
{
    /// <summary>What an extra solid around text is shaped like.</summary>
    public enum LetteraSolidLayerShape
    {
        /// <summary>The glyphs' own outline, grown or shrunk.</summary>
        Outline,
        /// <summary>A box with rounded corners around the ink.</summary>
        Plate,
        /// <summary>A round tube along each outline, like neon.</summary>
        Tube,
    }

    /// <summary>What an extra solid is made around: each glyph, each word, each line or the whole text.</summary>
    public enum LetteraSolidLayerScope
    {
        Glyph,
        Word,
        Line,
        Text,
    }

    /// <summary>What the glyphs do to a layer they stand in.</summary>
    public enum LetteraSolidLayerCut
    {
        None,
        /// <summary>Glyph-shaped holes right through it.</summary>
        Through,
        /// <summary>Glyph-shaped hollows in its front, as deep as asked.</summary>
        Engraved,
    }

    /// <summary>Which face of the text a layer's place in depth is measured from.</summary>
    public enum LetteraSolidLayerAnchor
    {
        Front,
        Back,
    }

    /// <summary>Common layers, ready to adjust.</summary>
    public enum LetteraSolidLayerPreset
    {
        /// <summary>A grown outline behind each word.</summary>
        Outline,
        /// <summary>A rounded plate behind the whole text.</summary>
        BackPlate,
        /// <summary>A tube along every glyph's outline, standing in front.</summary>
        Neon,
        /// <summary>A plate around each word with the letters cut through it.</summary>
        Badge,
        /// <summary>A plate with the letters sunk into its front.</summary>
        EngravedPlate,
        /// <summary>A grown slab behind each glyph.</summary>
        Slab,
    }

    /// <summary>
    /// An extra solid a solid text builds around its glyphs: its shape, what it is made around, where it stands, and its own depth,
    /// edge, colour and materials. It moves with its glyph when made per glyph, and is deformed with the text.
    /// </summary>
    [Serializable]
    public class LetteraSolidLayer
    {
        public string Name = "Layer";
        public bool Enabled = true;
        public LetteraSolidLayerShape Shape = LetteraSolidLayerShape.Outline;
        public LetteraSolidLayerScope Scope = LetteraSolidLayerScope.Word;
        [Tooltip("Outline: how far it grows past the glyphs, in em, or shrinks when negative. Plate: room around the ink. Tube: how far inside the outline its middle runs.")]
        public float Offset = 0.05f;
        [Tooltip("Plate: the corners' radius. Tube: the tube's radius. In em.")]
        public float Radius = 0.08f;
        [Tooltip("Above zero, only a ring this wide along its edge, in em.")]
        public float Band;
        public LetteraSolidLayerCut Cut = LetteraSolidLayerCut.None;
        [Tooltip("Engraved: how deep the glyphs go into it, in em.")]
        public float CutDepth = 0.04f;
        [Tooltip("How deep it is, in em. A tube is as deep as it is wide.")]
        public float Depth = 0.08f;
        [Tooltip("Which face of the text its front is measured from.")]
        public LetteraSolidLayerAnchor Anchor = LetteraSolidLayerAnchor.Back;
        [Tooltip("How far behind that face its front sits, in em. Negative stands in front of it.")]
        public float Z;
        [Tooltip("Moves it across the text's plane, in em.")]
        public Vector2 Shift;
        public LetteraBevel Bevel = new LetteraBevel { Shape = LetteraBevelShape.Chamfer, Width = 0.01f, Height = 0.01f, Roundness = 0.5f, Segments = 2, Curve = AnimationCurve.Linear(0, 0, 1, 1) };
        public Color Color = new Color(0.2f, 0.22f, 0.26f, 1);
        [Tooltip("Its front, edges and back. Empty takes the text's.")]
        public Material Material;
        [Tooltip("Its sides. Empty takes its Material, then the text's.")]
        public Material SideMaterial;
        [Tooltip("Cells per em of the field it is traced from: more is finer and slower to build.")]
        [Range(8, 96)] public int Detail = 32;

        public LetteraSolidLayer Clone() => (LetteraSolidLayer)MemberwiseClone();

        /// <summary>A common layer, ready to adjust.</summary>
        public static LetteraSolidLayer Preset(LetteraSolidLayerPreset preset)
        {
            var layer = new LetteraSolidLayer { Name = preset.ToString() };
            switch (preset)
            {
                case LetteraSolidLayerPreset.Outline:
                    layer.Shape = LetteraSolidLayerShape.Outline;
                    layer.Scope = LetteraSolidLayerScope.Word;
                    layer.Offset = 0.06f;
                    layer.Anchor = LetteraSolidLayerAnchor.Front;
                    layer.Z = 0.03f;
                    layer.Depth = 0.1f;
                    break;
                case LetteraSolidLayerPreset.BackPlate:
                    layer.Shape = LetteraSolidLayerShape.Plate;
                    layer.Scope = LetteraSolidLayerScope.Text;
                    layer.Offset = 0.2f;
                    layer.Radius = 0.15f;
                    layer.Depth = 0.08f;
                    layer.Anchor = LetteraSolidLayerAnchor.Back;
                    break;
                case LetteraSolidLayerPreset.Neon:
                    layer.Shape = LetteraSolidLayerShape.Tube;
                    layer.Scope = LetteraSolidLayerScope.Glyph;
                    layer.Offset = 0.035f;
                    layer.Radius = 0.022f;
                    layer.Anchor = LetteraSolidLayerAnchor.Front;
                    layer.Z = -0.05f;
                    layer.Color = new Color(1f, 0.3f, 0.75f, 1);
                    break;
                case LetteraSolidLayerPreset.Badge:
                    layer.Shape = LetteraSolidLayerShape.Plate;
                    layer.Scope = LetteraSolidLayerScope.Word;
                    layer.Cut = LetteraSolidLayerCut.Through;
                    layer.Offset = 0.12f;
                    layer.Radius = 0.1f;
                    layer.Depth = 0.1f;
                    layer.Anchor = LetteraSolidLayerAnchor.Front;
                    layer.Z = 0.02f;
                    break;
                case LetteraSolidLayerPreset.EngravedPlate:
                    layer.Shape = LetteraSolidLayerShape.Plate;
                    layer.Scope = LetteraSolidLayerScope.Text;
                    layer.Cut = LetteraSolidLayerCut.Engraved;
                    layer.Offset = 0.2f;
                    layer.Radius = 0.08f;
                    layer.Depth = 0.15f;
                    layer.CutDepth = 0.05f;
                    layer.Anchor = LetteraSolidLayerAnchor.Front;
                    layer.Color = new Color(0.62f, 0.6f, 0.56f, 1);
                    break;
                case LetteraSolidLayerPreset.Slab:
                    layer.Shape = LetteraSolidLayerShape.Outline;
                    layer.Scope = LetteraSolidLayerScope.Glyph;
                    layer.Offset = 0.03f;
                    layer.Anchor = LetteraSolidLayerAnchor.Back;
                    layer.Depth = 0.12f;
                    break;
            }
            return layer;
        }

        internal bool IsTube => Shape == LetteraSolidLayerShape.Tube;

        internal float SolidDepth => IsTube ? 2 * Mathf.Max(Radius, 0.001f) : Mathf.Max(Depth, 0);

        internal LetteraBevel EdgeFor()
        {
            if (!IsTube) return Bevel;
            float r = Mathf.Max(Radius, 0.001f);
            return new LetteraBevel { Shape = LetteraBevelShape.Round, Width = r, Height = r, Roundness = 0.5f, Segments = 5, Curve = AnimationCurve.Linear(0, 0, 1, 1) };
        }

        // What decides the outline it is traced to. Part 1 is an engraved plate's back, which has no glyphs in it.
        internal int ShapeHash(int part)
        {
            unchecked
            {
                int hash = 7 + (int)Shape * 31 + (int)Cut * 131 + part * 1031;
                hash = hash * 31 + Offset.GetHashCode();
                hash = hash * 31 + Radius.GetHashCode();
                hash = hash * 31 + Band.GetHashCode();
                hash = hash * 31 + Detail;
                return hash == 0 ? 1 : hash;
            }
        }

        internal ulong HashOf()
        {
            ulong h = (ulong)(uint)ShapeHash(0);
            unchecked
            {
                void Mix(int v) => h = (h ^ (uint)v) * 1099511628211UL;
                Mix(Enabled ? 1 : 0);
                Mix((int)Scope);
                Mix(CutDepth.GetHashCode());
                Mix(Depth.GetHashCode());
                Mix((int)Anchor);
                Mix(Z.GetHashCode());
                Mix(Shift.GetHashCode());
                Mix(EdgeFor().HashOf());
                Mix(Color.GetHashCode());
                Mix(Material != null ? Material.GetInstanceID() : 0);
                Mix(SideMaterial != null ? SideMaterial.GetInstanceID() : 0);
            }
            return h;
        }
    }

    /// <summary>
    /// Traces a layer's outline from the glyphs: a distance field on a grid near their edges, the inside by non-zero winding as the glyphs
    /// fill, then the shape's own field, and its zero line followed cell by cell into closed loops with the fill on their left.
    /// </summary>
    [BurstCompile]
    internal struct LetteraLayerFieldJob : IJob
    {
        public const int ModeOutline = 0, ModeTube = 1, ModePlate = 2;

        [ReadOnly] public NativeArray<float4> Segments;
        public int SegmentCount;
        public int Mode;
        public bool CutGlyphs;
        public float Offset, Radius, Band;
        public float2 BoxCenter, BoxHalf;
        public float2 Origin;
        public float Cell;
        public int Width, Height;
        public float Reach;
        public NativeList<float2> Points;
        public NativeList<int2> Loops;

        struct Crossing
        {
            public int Row;
            public float X;
            public int Dir;
        }

        struct ByRow : IComparer<Crossing>
        {
            public int Compare(Crossing a, Crossing b) => a.Row != b.Row ? a.Row.CompareTo(b.Row) : a.X.CompareTo(b.X);
        }

        public void Execute()
        {
            Points.Clear();
            Loops.Clear();
            int count = Width * Height;
            var field = new NativeArray<float>(count, Allocator.Temp);
            for (int i = 0; i < count; i++) field[i] = Reach;

            // Distance, near the edges only: further out, the shape needs no more than which side a node is on.
            for (int s = 0; s < SegmentCount; s++)
            {
                float4 seg = Segments[s];
                float2 a = seg.xy, b = seg.zw;
                float2 low = math.min(a, b) - Reach, high = math.max(a, b) + Reach;
                int i0 = math.max(0, (int)math.floor((low.x - Origin.x) / Cell)), i1 = math.min(Width - 1, (int)math.ceil((high.x - Origin.x) / Cell));
                int j0 = math.max(0, (int)math.floor((low.y - Origin.y) / Cell)), j1 = math.min(Height - 1, (int)math.ceil((high.y - Origin.y) / Cell));
                float2 ab = b - a;
                float length = math.max(math.dot(ab, ab), 1e-12f);
                for (int j = j0; j <= j1; j++)
                {
                    for (int i = i0; i <= i1; i++)
                    {
                        float2 p = Origin + new float2(i, j) * Cell;
                        float t = math.saturate(math.dot(p - a, ab) / length);
                        float d = math.distance(p, a + ab * t);
                        int at = j * Width + i;
                        if (d < field[at]) field[at] = d;
                    }
                }
            }

            // Inside, by the winding of every segment a row of nodes crosses.
            var crossings = new NativeList<Crossing>(SegmentCount * 2, Allocator.Temp);
            for (int s = 0; s < SegmentCount; s++)
            {
                float4 seg = Segments[s];
                if (seg.y == seg.w) continue;
                float ylo = math.min(seg.y, seg.w), yhi = math.max(seg.y, seg.w);
                int j0 = math.max(0, (int)math.ceil((ylo - Origin.y) / Cell));
                int j1 = math.min(Height - 1, (int)math.ceil((yhi - Origin.y) / Cell) - 1);
                for (int j = j0; j <= j1; j++)
                {
                    float y = Origin.y + j * Cell;
                    if (y < ylo || y >= yhi) continue;
                    float t = (y - seg.y) / (seg.w - seg.y);
                    crossings.Add(new Crossing { Row = j, X = seg.x + t * (seg.z - seg.x), Dir = seg.w > seg.y ? 1 : -1 });
                }
            }
            crossings.Sort(new ByRow());
            int k = 0;
            for (int j = 0; j < Height; j++)
            {
                while (k < crossings.Length && crossings[k].Row < j) k++;
                int winding = 0;
                for (int i = 0; i < Width; i++)
                {
                    float x = Origin.x + i * Cell;
                    while (k < crossings.Length && crossings[k].Row == j && crossings[k].X < x)
                    {
                        winding += crossings[k].Dir;
                        k++;
                    }
                    int at = j * Width + i;
                    float g = winding != 0 ? -field[at] : field[at];
                    float2 p = Origin + new float2(i, j) * Cell;
                    float f;
                    if (Mode == ModeOutline)
                    {
                        f = g - Offset;
                        if (Band > 0) f = math.max(f, Offset - Band - g);
                    }
                    else if (Mode == ModeTube)
                    {
                        f = math.abs(g + Offset) - Radius;
                    }
                    else
                    {
                        float r = math.min(Radius, math.cmin(BoxHalf));
                        float2 q = math.abs(p - BoxCenter) - (BoxHalf - r);
                        f = math.length(math.max(q, 0)) + math.min(math.cmax(q), 0) - r;
                        if (Band > 0) f = math.max(f, -(f + Band));
                    }
                    if (CutGlyphs) f = math.max(f, -g);
                    // The grid's rim is outside, so every loop closes.
                    if (i == 0 || j == 0 || i == Width - 1 || j == Height - 1) f = math.max(f, Cell * 0.01f);
                    field[at] = f;
                }
                while (k < crossings.Length && crossings[k].Row == j) k++;
            }

            Trace(field);
        }

        // An edge's crossing, from its own two nodes in a fixed order, so that both cells beside it find the same point.
        float2 EdgePoint(NativeArray<float> field, int edge)
        {
            int node = edge >> 1;
            int i = node % Width, j = node / Width;
            int other = (edge & 1) == 0 ? node + 1 : node + Width;
            float fa = field[node], fb = field[other];
            float t = math.saturate(fa / (fa - fb));
            float2 a = Origin + new float2(i, j) * Cell;
            float2 b = (edge & 1) == 0 ? a + new float2(Cell, 0) : a + new float2(0, Cell);
            return math.lerp(a, b, t);
        }

        void Trace(NativeArray<float> field)
        {
            var next = new NativeHashMap<int, int>(1024, Allocator.Temp);
            var edges = new NativeArray<int>(4, Allocator.Temp);
            var exits = new NativeArray<bool>(4, Allocator.Temp);
            var ids = new NativeArray<int>(4, Allocator.Temp);
            var corners = new NativeArray<float>(4, Allocator.Temp);
            for (int j = 0; j < Height - 1; j++)
            {
                for (int i = 0; i < Width - 1; i++)
                {
                    int n0 = j * Width + i, n1 = n0 + 1, n2 = n0 + Width + 1, n3 = n0 + Width;
                    corners[0] = field[n0];
                    corners[1] = field[n1];
                    corners[2] = field[n2];
                    corners[3] = field[n3];
                    int mask = (corners[0] < 0 ? 1 : 0) | (corners[1] < 0 ? 2 : 0) | (corners[2] < 0 ? 4 : 0) | (corners[3] < 0 ? 8 : 0);
                    if (mask == 0 || mask == 15) continue;
                    // Round the cell from below, against the clock: bottom, right, top, left.
                    ids[0] = n0 * 2;
                    ids[1] = n1 * 2 + 1;
                    ids[2] = n3 * 2;
                    ids[3] = n0 * 2 + 1;
                    int found = 0;
                    for (int e = 0; e < 4; e++)
                    {
                        bool inA = corners[e] < 0, inB = corners[(e + 1) & 3] < 0;
                        if (inA == inB) continue;
                        edges[found] = ids[e];
                        exits[found] = inA;
                        found++;
                    }
                    if (found == 2)
                    {
                        int from = exits[0] ? 0 : 1;
                        next.TryAdd(edges[from], edges[1 - from]);
                    }
                    else if (found == 4)
                    {
                        // A saddle: the middle decides whether the inside joins across the cell.
                        bool middleIn = corners[0] + corners[1] + corners[2] + corners[3] < 0;
                        for (int c = 0; c < 4; c++)
                        {
                            if (!exits[c]) continue;
                            int to = middleIn ? (c + 1) & 3 : (c + 3) & 3;
                            next.TryAdd(edges[c], edges[to]);
                        }
                    }
                }
            }

            var visited = new NativeHashSet<int>(next.Count, Allocator.Temp);
            NativeArray<int> starts = next.GetKeyArray(Allocator.Temp);
            starts.Sort();
            var loop = new NativeList<float2>(256, Allocator.Temp);
            float tolerance = Cell * 0.02f;
            for (int s = 0; s < starts.Length; s++)
            {
                int first = starts[s];
                if (visited.Contains(first)) continue;
                loop.Clear();
                int current = first;
                bool closed = false;
                for (int guard = 0; guard < next.Count + 1; guard++)
                {
                    visited.Add(current);
                    loop.Add(EdgePoint(field, current));
                    if (!next.TryGetValue(current, out int to)) break;
                    if (to == first)
                    {
                        closed = true;
                        break;
                    }
                    if (visited.Contains(to)) break;
                    current = to;
                }
                if (!closed || loop.Length < 3) continue;
                int start = Points.Length;
                // Points along a straight run go: the builder's work grows with them.
                for (int p = 0; p < loop.Length; p++)
                {
                    float2 previous = Points.Length > start ? Points[Points.Length - 1] : loop[loop.Length - 1];
                    float2 here = loop[p], after = loop[(p + 1) % loop.Length];
                    float2 dir = after - previous;
                    float len = math.length(dir);
                    if (len > 1e-9f && math.abs(dir.x * (here.y - previous.y) - dir.y * (here.x - previous.x)) / len < tolerance) continue;
                    Points.Add(here);
                }
                int added = Points.Length - start;
                if (added < 3)
                {
                    Points.Resize(start, NativeArrayOptions.UninitializedMemory);
                    continue;
                }
                Loops.Add(new int2(start, added));
            }
        }
    }

    /// <summary>Builds layers' solids: per glyph through the shared cache, per word, line or text for one text.</summary>
    internal static class LetteraSolidLayerBuilder
    {
        static float[] s_Outline = new float[4096];
        static readonly List<float> s_Commands = new List<float>(4096);

        /// <summary>A glyph's outline as segments, flattened, placed by a transform from em into the group's units.</summary>
        internal static unsafe void AddGlyph(FontInstance instance, uint glyph, float2 origin, float sizeX, float sizeY, float cos, float sin, ref NativeList<float4> segments, ref float2 low, ref float2 high)
        {
            if (instance == null) return;
            int count;
            fixed (float* p = s_Outline) count = LetteraNative.lt_glyph_outline(instance.Font, glyph, p, s_Outline.Length);
            if (count > s_Outline.Length)
            {
                if (count > 16 * 1024 * 1024) return;
                s_Outline = new float[count + 1024];
                fixed (float* p = s_Outline) count = LetteraNative.lt_glyph_outline(instance.Font, glyph, p, s_Outline.Length);
            }
            float scale = 1f / Mathf.Max(instance.Face.Metrics.Upem, 1);
            float2 Place(float x, float y)
            {
                float lx = x * scale * sizeX, ly = y * scale * sizeY;
                return origin + new float2(lx * cos - ly * sin, lx * sin + ly * cos);
            }
            float2 start = default, pen = default;
            bool open = false;
            int i = 0;
            void Line(float2 to, ref NativeList<float4> list, ref float2 lo, ref float2 hi)
            {
                if (math.any(pen != to)) list.Add(new float4(pen, to));
                lo = math.min(lo, to);
                hi = math.max(hi, to);
                pen = to;
            }
            while (i < count)
            {
                int command = (int)s_Outline[i];
                if (command == 0 && i + 2 < count)
                {
                    if (open) Line(start, ref segments, ref low, ref high);
                    pen = start = Place(s_Outline[i + 1], s_Outline[i + 2]);
                    low = math.min(low, pen);
                    high = math.max(high, pen);
                    open = true;
                    i += 3;
                }
                else if (command == 1 && i + 2 < count)
                {
                    Line(Place(s_Outline[i + 1], s_Outline[i + 2]), ref segments, ref low, ref high);
                    i += 3;
                }
                else if (command == 2 && i + 4 < count)
                {
                    float2 from = pen, c = Place(s_Outline[i + 1], s_Outline[i + 2]), to = Place(s_Outline[i + 3], s_Outline[i + 4]);
                    for (int k = 1; k <= 8; k++)
                    {
                        float t = k / 8f, u = 1 - t;
                        Line(u * u * from + 2 * u * t * c + t * t * to, ref segments, ref low, ref high);
                    }
                    i += 5;
                }
                else if (command == 3 && i + 6 < count)
                {
                    float2 from = pen, a = Place(s_Outline[i + 1], s_Outline[i + 2]), b = Place(s_Outline[i + 3], s_Outline[i + 4]), to = Place(s_Outline[i + 5], s_Outline[i + 6]);
                    for (int k = 1; k <= 12; k++)
                    {
                        float t = k / 12f, u = 1 - t;
                        Line(u * u * u * from + 3 * u * u * t * a + 3 * u * t * t * b + t * t * t * to, ref segments, ref low, ref high);
                    }
                    i += 7;
                }
                else if (command == 4)
                {
                    if (open) Line(start, ref segments, ref low, ref high);
                    open = false;
                    i += 1;
                }
                else
                {
                    break;
                }
            }
            if (open) Line(start, ref segments, ref low, ref high);
        }

        /// <summary>
        /// The solid of a layer around segments already in em. Part 0 is the layer, part 1 an engraved plate's back. Null when there is
        /// nothing to build. The caller owns the result.
        /// </summary>
        internal static LetteraSolidEntry Build(in LetteraSolidKey key, NativeList<float4> segments, float2 low, float2 high, LetteraSolidLayer layer, int part, in LetteraSolidRecipe recipe)
        {
            if (segments.Length == 0 && layer.Shape != LetteraSolidLayerShape.Plate) return null;
            if (math.any(low > high)) return null;
            int mode;
            if (layer.Shape == LetteraSolidLayerShape.Outline) mode = LetteraLayerFieldJob.ModeOutline;
            else if (layer.Shape == LetteraSolidLayerShape.Tube) mode = LetteraLayerFieldJob.ModeTube;
            else mode = LetteraLayerFieldJob.ModePlate;
            bool cut = layer.Cut != LetteraSolidLayerCut.None && part == 0 && mode != LetteraLayerFieldJob.ModeTube;
            float offset = layer.Offset, radius = Mathf.Max(layer.Radius, 0.001f), band = Mathf.Max(layer.Band, 0);
            float2 center = (low + high) * 0.5f, half = (high - low) * 0.5f + math.max(offset, 0);
            float cell = 1f / Mathf.Clamp(layer.Detail, 8, 96);
            var points = new NativeList<float2>(1024, Allocator.TempJob);
            var loops = new NativeList<int2>(16, Allocator.TempJob);
            var input = new NativeArray<float4>(math.max(segments.Length, 1), Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
            for (int i = 0; i < segments.Length; i++) input[i] = segments[i];
            try
            {
                for (int attempt = 0; attempt < 4; attempt++)
                {
                    float grow = mode == LetteraLayerFieldJob.ModeTube ? math.abs(offset) + radius : math.max(offset, 0) + band;
                    float2 from, to;
                    if (mode >= LetteraLayerFieldJob.ModePlate)
                    {
                        from = center - half - 3 * cell;
                        to = center + half + 3 * cell;
                    }
                    else
                    {
                        from = low - grow - 3 * cell;
                        to = high + grow + 3 * cell;
                    }
                    int width = (int)math.ceil((to.x - from.x) / cell) + 1, height = (int)math.ceil((to.y - from.y) / cell) + 1;
                    if ((long)width * height > 4_000_000)
                    {
                        cell *= 1.5f;
                        continue;
                    }
                    var job = new LetteraLayerFieldJob
                    {
                        Segments = input,
                        SegmentCount = segments.Length,
                        Mode = mode,
                        CutGlyphs = cut,
                        Offset = offset,
                        Radius = radius,
                        Band = band,
                        BoxCenter = center,
                        BoxHalf = math.max(half, 1e-4f),
                        Origin = from,
                        Cell = cell,
                        Width = width,
                        Height = height,
                        Reach = math.abs(offset) + math.max(band, radius) + 3 * cell,
                        Points = points,
                        Loops = loops,
                    };
                    job.Run();
                    // The builder takes so many points at most; a coarser field gives fewer.
                    if (points.Length <= LetteraSolidParams.MaxSegments * 3 / 4) break;
                    cell *= 1.5f;
                }
                if (loops.Length == 0) return null;
                s_Commands.Clear();
                for (int l = 0; l < loops.Length; l++)
                {
                    int2 loop = loops[l];
                    for (int p = 0; p < loop.y; p++)
                    {
                        float2 at = points[loop.x + p];
                        s_Commands.Add(p == 0 ? 0 : 1);
                        s_Commands.Add(at.x);
                        s_Commands.Add(at.y);
                    }
                    s_Commands.Add(4);
                }
                float[] commands = s_Commands.ToArray();
                return LetteraSolidCache.Build(key, commands, commands.Length, 1, recipe);
            }
            finally
            {
                points.Dispose();
                loops.Dispose();
                input.Dispose();
            }
        }

        /// <summary>The recipe a layer is built by: its own edge on both faces, the text's tolerance, smoothing and fineness.</summary>
        internal static LetteraSolidRecipe RecipeOf(LetteraSolidLayer layer, in LetteraSolidRecipe text)
        {
            LetteraBevel edge = layer.EdgeFor();
            return new LetteraSolidRecipe { Front = edge, Back = edge, Tolerance = text.Tolerance, SmoothingCos = text.SmoothingCos, Faces = LetteraSolidFaces.All, MaxEdge = text.MaxEdge };
        }
    }
}

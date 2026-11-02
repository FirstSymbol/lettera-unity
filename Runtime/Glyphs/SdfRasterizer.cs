using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;

namespace FirstSymbol.Lettera
{
    /// <summary>A rectangle of a target buffer to fill with a signed distance field.</summary>
    internal unsafe struct SdfTile
    {
        public byte* Target;
        public int Stride;
        public int X, Y, Width, Height;
        public int LineStart, LineCount;
        public float Spread;

        /// <summary>Four bytes per pixel: the three color channels of a multi-channel field, and the true distance in alpha.</summary>
        public bool Multi;
    }

    /// <summary>
    /// Computes the distance from each pixel center to the nearest outline segment; non-zero winding gives
    /// the sign, so overlapping contours render whole. 0.5 in the output is the edge, values reach 0 and 1
    /// at Spread pixels from it.
    /// </summary>
    [BurstCompile]
    internal unsafe struct SdfTileJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<SdfTile> Tiles;
        [ReadOnly] public NativeArray<float4> Lines;

        // How each line is colored, for multi-channel tiles: bits 0-2 the channels it belongs to, 3 and 4 whether its start and end are corners.
        [NativeDisableUnsafePtrRestriction] public byte* LineColors;

        public void Execute(int index)
        {
            SdfTile tile = Tiles[index];
            float4* source = (float4*)Lines.GetUnsafeReadOnlyPtr() + tile.LineStart;
            var kept = new NativeArray<float4>(tile.LineCount, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
            var keptColors = new NativeArray<byte>(tile.LineCount, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
            float4* lines = (float4*)kept.GetUnsafePtr();
            byte* colors = (byte*)keptColors.GetUnsafePtr();
            byte* sourceColors = tile.Multi ? LineColors + tile.LineStart : null;
            int lineCount = RemoveInteriorEdges(source, sourceColors, tile.LineCount, lines, colors);

            if (tile.Multi) RasterMulti(tile, source, tile.LineCount, lines, colors, lineCount);
            else RasterSingle(tile, source, tile.LineCount, lines, lineCount);
            kept.Dispose();
            keptColors.Dispose();
        }

        static bool Better(float squared, float align, float best, float bestAlign)
        {
            if (squared < best - 1e-3f) return true;
            return squared <= best + 1e-3f && align < bestAlign;
        }

        static int WindingAt(float4* lines, int count, float2 p)
        {
            int winding = 0;
            for (int i = 0; i < count; i++)
            {
                float2 a = lines[i].xy, b = lines[i].zw;
                if ((a.y <= p.y) == (b.y <= p.y)) continue;
                float crossing = a.x + (p.y - a.y) / (b.y - a.y) * (b.x - a.x);
                if (crossing > p.x) winding += b.y > a.y ? 1 : -1;
            }
            return winding;
        }

        // Edges with the fill on both sides are where contours overlap. They are no outline, and a field that kept them would show a seam there.
        static int RemoveInteriorEdges(float4* lines, byte* colors, int count, float4* output, byte* outputColors)
        {
            int kept = 0;
            for (int i = 0; i < count; i++)
            {
                float2 a = lines[i].xy, b = lines[i].zw, edge = b - a;
                float length = math.length(edge);
                if (length <= 0) continue;
                float2 normal = new float2(-edge.y, edge.x) / length * 0.1f, middle = (a + b) * 0.5f;
                bool left = WindingAt(lines, count, middle + normal) != 0, right = WindingAt(lines, count, middle - normal) != 0;
                if (left == right) continue;
                output[kept] = lines[i];
                if (colors != null) outputColors[kept] = colors[i];
                kept++;
            }
            return kept;
        }

        // The places where a row crosses the outline, sorted, and the direction of each. The winding at a pixel is the sum of the directions
        // of crossings to its right, by the same arithmetic as asking every line at every pixel.
        static int RowCrossings(float4* all, int allCount, float py, float* xs, int* directions, out int total)
        {
            int count = 0;
            total = 0;
            for (int i = 0; i < allCount; i++)
            {
                float2 a = all[i].xy, b = all[i].zw;
                if ((a.y <= py) == (b.y <= py)) continue;
                float crossing = a.x + (py - a.y) / (b.y - a.y) * (b.x - a.x);
                int direction = b.y > a.y ? 1 : -1;
                total += direction;
                int j = count++;
                while (j > 0 && xs[j - 1] > crossing)
                {
                    xs[j] = xs[j - 1];
                    directions[j] = directions[j - 1];
                    j--;
                }
                xs[j] = crossing;
                directions[j] = direction;
            }
            return count;
        }

        // Distances are to the outline alone; the fill comes from every contour, or overlaps would count wrong.
        // A pixel farther than the spread from every line comes out fully in or out whatever its exact distance, so each line measures
        // only the pixels within its reach. The bytes are the same as measuring every line at every pixel.
        static void RasterSingle(in SdfTile tile, float4* all, int allCount, float4* lines, int lineCount)
        {
            float toValue = 0.5f / tile.Spread;
            int width = tile.Width, height = tile.Height;
            long pixels = (long)width * height;
            var nearest = (float*)UnsafeUtility.Malloc(pixels * sizeof(float), 16, Allocator.Persistent);
            for (long i = 0; i < pixels; i++) nearest[i] = float.MaxValue;
            float reach = tile.Spread + 1;
            for (int i = 0; i < lineCount; i++)
            {
                float4 line = lines[i];
                float2 a = line.xy, b = line.zw;
                int x0 = math.max((int)math.floor(math.min(a.x, b.x) - reach), 0), x1 = math.min((int)math.ceil(math.max(a.x, b.x) + reach), width - 1);
                int y0 = math.max((int)math.floor(math.min(a.y, b.y) - reach), 0), y1 = math.min((int)math.ceil(math.max(a.y, b.y) + reach), height - 1);
                for (int row = y0; row <= y1; row++)
                {
                    float py = row + 0.5f;
                    float* rowNearest = nearest + (long)row * width;
                    for (int column = x0; column <= x1; column++)
                    {
                        float2 p = new float2(column + 0.5f, py);
                        float2 edge = b - a, toPoint = p - a;
                        float lengthSquared = math.dot(edge, edge);
                        float t = lengthSquared > 0 ? math.saturate(math.dot(toPoint, edge) / lengthSquared) : 0;
                        float2 offset = toPoint - edge * t;
                        rowNearest[column] = math.min(rowNearest[column], math.dot(offset, offset));
                    }
                }
            }

            var xs = (float*)UnsafeUtility.Malloc(math.max(allCount, 1) * (long)sizeof(float), 16, Allocator.Persistent);
            var directions = (int*)UnsafeUtility.Malloc(math.max(allCount, 1) * (long)sizeof(int), 16, Allocator.Persistent);
            for (int row = 0; row < height; row++)
            {
                byte* output = tile.Target + (tile.Y + row) * tile.Stride + tile.X;
                float py = row + 0.5f;
                int crossings = RowCrossings(all, allCount, py, xs, directions, out int winding);
                int next = 0;
                float* rowNearest = nearest + (long)row * width;
                for (int column = 0; column < width; column++)
                {
                    float px = column + 0.5f;
                    while (next < crossings && xs[next] <= px) winding -= directions[next++];
                    float distance = math.sqrt(rowNearest[column]);
                    if (winding == 0) distance = -distance;
                    output[column] = (byte)(math.saturate(0.5f + distance * toValue) * 255f + 0.5f);
                }
            }
            UnsafeUtility.Free(directions, Allocator.Persistent);
            UnsafeUtility.Free(xs, Allocator.Persistent);
            UnsafeUtility.Free(nearest, Allocator.Persistent);
        }

        // Each channel measures to the nearest line of its color. Beyond a corner a line's distance extends straight past its end, so at
        // the corner two channels disagree and their median keeps the corner sharp at any scale. The method is Chlumsky's multi-channel
        // distance field; pixels where the median's sign contradicts the true fill fall back to the true distance, removing artifacts.
        static void RasterMulti(in SdfTile tile, float4* all, int allCount, float4* lines, byte* colors, int lineCount)
        {
            float toValue = 0.5f / tile.Spread;
            float area = 0;
            for (int i = 0; i < lineCount; i++) area += lines[i].x * lines[i].w - lines[i].z * lines[i].y;
            float insideSign = area >= 0 ? 1 : -1;

            var xs = (float*)UnsafeUtility.Malloc(math.max(allCount, 1) * (long)sizeof(float), 16, Allocator.Persistent);
            var directions = (int*)UnsafeUtility.Malloc(math.max(allCount, 1) * (long)sizeof(int), 16, Allocator.Persistent);
            for (int row = 0; row < tile.Height; row++)
            {
                byte* output = tile.Target + (tile.Y + row) * tile.Stride + tile.X * 4;
                float py = row + 0.5f;
                int crossings = RowCrossings(all, allCount, py, xs, directions, out int winding);
                int next = 0;
                for (int column = 0; column < tile.Width; column++)
                {
                    var p = new float2(column + 0.5f, py);
                    while (next < crossings && xs[next] <= p.x) winding -= directions[next++];
                    float nearest = float.MaxValue;
                    float3 best = float.MaxValue, bestAlign = 2, bestSigned = 0;
                    for (int i = 0; i < lineCount; i++)
                    {
                        float4 line = lines[i];
                        float2 a = line.xy, b = line.zw;
                        float2 edge = b - a, toPoint = p - a;
                        float lengthSquared = math.dot(edge, edge);
                        float raw = lengthSquared > 0 ? math.dot(toPoint, edge) / lengthSquared : 0;
                        float t = math.saturate(raw);
                        float2 offset = toPoint - edge * t;
                        float squared = math.dot(offset, offset);
                        nearest = math.min(nearest, squared);

                        byte color = colors[i];
                        if ((color & 7) == 0) continue;
                        float cross = edge.x * toPoint.y - edge.y * toPoint.x;
                        float sign = cross >= 0 ? insideSign : -insideSign;
                        float distance = math.sqrt(squared);
                        if (lengthSquared > 0 && ((raw < 0 && (color & 8) != 0) || (raw > 1 && (color & 16) != 0)))
                            distance = math.abs(cross) / math.sqrt(lengthSquared);
                        float signedDistance = sign * distance;
                        // Two lines that meet at a corner are equally near past it. The one the point lies less straight ahead of wins, or
                        // a channel would follow a line's extension out into empty space.
                        float align = 2;
                        if (lengthSquared > 0 && squared > 0) align = math.abs(math.dot(edge, offset)) / math.sqrt(lengthSquared * squared);
                        if ((color & 1) != 0 && Better(squared, align, best.x, bestAlign.x)) { best.x = squared; bestAlign.x = align; bestSigned.x = signedDistance; }
                        if ((color & 2) != 0 && Better(squared, align, best.y, bestAlign.y)) { best.y = squared; bestAlign.y = align; bestSigned.y = signedDistance; }
                        if ((color & 4) != 0 && Better(squared, align, best.z, bestAlign.z)) { best.z = squared; bestAlign.z = align; bestSigned.z = signedDistance; }
                    }
                    float trueDistance = math.sqrt(nearest);
                    if (winding == 0) trueDistance = -trueDistance;
                    float median = math.max(math.min(bestSigned.x, bestSigned.y), math.min(math.max(bestSigned.x, bestSigned.y), bestSigned.z));
                    if ((median >= 0) != (trueDistance >= 0) || best.x == float.MaxValue || best.y == float.MaxValue || best.z == float.MaxValue)
                        bestSigned = trueDistance;
                    output[column * 4] = (byte)(math.saturate(0.5f + bestSigned.x * toValue) * 255f + 0.5f);
                    output[column * 4 + 1] = (byte)(math.saturate(0.5f + bestSigned.y * toValue) * 255f + 0.5f);
                    output[column * 4 + 2] = (byte)(math.saturate(0.5f + bestSigned.z * toValue) * 255f + 0.5f);
                    output[column * 4 + 3] = (byte)(math.saturate(0.5f + trueDistance * toValue) * 255f + 0.5f);
                }
            }
            UnsafeUtility.Free(directions, Allocator.Persistent);
            UnsafeUtility.Free(xs, Allocator.Persistent);
        }
    }

    /// <summary>Gives each stretch of an outline between corners a color, so that the two lines at a corner share exactly one channel.</summary>
    internal static class EdgeColoring
    {
        const byte Cyan = 6, Magenta = 5, Yellow = 3, White = 7, StartCorner = 8, EndCorner = 16;
        static readonly float s_CornerCos = math.cos(math.radians(20f));

        /// <summary>Colors the lines from an index on. Contours are the runs of lines that connect end to start.</summary>
        public static void Color(NativeList<float4> lines, NativeList<byte> colors, int first)
        {
            while (colors.Length < lines.Length) colors.Add(0);
            int start = first;
            while (start < lines.Length)
            {
                int end = start + 1;
                while (end < lines.Length && math.all(lines[end - 1].zw == lines[end].xy)) end++;
                ColorContour(lines, colors, start, end);
                start = end;
            }
        }

        static bool IsCorner(float4 before, float4 after)
        {
            float2 a = before.zw - before.xy, b = after.zw - after.xy;
            float la = math.length(a), lb = math.length(b);
            return la > 0 && lb > 0 && math.dot(a, b) / (la * lb) < s_CornerCos;
        }

        static void ColorContour(NativeList<float4> lines, NativeList<byte> colors, int start, int end)
        {
            int count = end - start;
            int corners = 0, firstCorner = -1;
            for (int i = 0; i < count; i++)
            {
                float4 before = lines[start + (i + count - 1) % count], after = lines[start + i];
                if (!IsCorner(before, after)) continue;
                corners++;
                if (firstCorner < 0) firstCorner = i;
            }

            if (corners == 0)
            {
                for (int i = 0; i < count; i++) colors[start + i] = White;
                return;
            }

            // Walk from the first corner, changing color at each corner; one corner still needs three stretches.
            int stretches = Math.Max(corners, 3);
            int stretch = 0;
            for (int n = 0; n < count; n++)
            {
                int i = (firstCorner + n) % count;
                float4 before = lines[start + (i + count - 1) % count], after = lines[start + i], next = lines[start + (i + 1) % count];
                bool cornerAtStart = IsCorner(before, after), cornerAtEnd = IsCorner(after, next);
                if (corners == 1) stretch = n * 3 / count;
                else if (cornerAtStart && n > 0) stretch++;
                byte color;
                if (stretches % 3 == 1 && stretch == stretches - 1) color = Magenta;
                else color = stretch % 3 == 0 ? Cyan : stretch % 3 == 1 ? Magenta : Yellow;
                colors[start + i] = (byte)(color | (cornerAtStart ? StartCorner : 0) | (cornerAtEnd ? EndCorner : 0));
            }
        }
    }

    /// <summary>Turns hb outline commands into line segments in tile pixels.</summary>
    internal static class OutlineFlattener
    {
        /// <summary>Appends the outline's segments, with coordinates (x * scale + offsetX, y * scale + offsetY).</summary>
        public static void Flatten(float[] commands, int count, float scale, float offsetX, float offsetY, NativeList<float4> lines)
        {
            float2 current = default, start = default;
            bool open = false;
            int i = 0;
            while (i < count)
            {
                int op = (int)commands[i++];
                switch (op)
                {
                    case 0:
                        if (open && !current.Equals(start)) lines.Add(new float4(current, start));
                        current = start = Point(commands, ref i, scale, offsetX, offsetY);
                        open = true;
                        break;
                    case 1:
                    {
                        float2 p = Point(commands, ref i, scale, offsetX, offsetY);
                        lines.Add(new float4(current, p));
                        current = p;
                        break;
                    }
                    case 2:
                    {
                        float2 c = Point(commands, ref i, scale, offsetX, offsetY);
                        float2 p = Point(commands, ref i, scale, offsetX, offsetY);
                        int steps = Steps(math.distance(current, c) + math.distance(c, p));
                        float2 previous = current;
                        for (int s = 1; s <= steps; s++)
                        {
                            float t = (float)s / steps, u = 1 - t;
                            float2 next = u * u * current + 2 * u * t * c + t * t * p;
                            lines.Add(new float4(previous, next));
                            previous = next;
                        }
                        current = p;
                        break;
                    }
                    case 3:
                    {
                        float2 c1 = Point(commands, ref i, scale, offsetX, offsetY);
                        float2 c2 = Point(commands, ref i, scale, offsetX, offsetY);
                        float2 p = Point(commands, ref i, scale, offsetX, offsetY);
                        int steps = Steps(math.distance(current, c1) + math.distance(c1, c2) + math.distance(c2, p));
                        float2 previous = current;
                        for (int s = 1; s <= steps; s++)
                        {
                            float t = (float)s / steps, u = 1 - t;
                            float2 next = u * u * u * current + 3 * u * u * t * c1 + 3 * u * t * t * c2 + t * t * t * p;
                            lines.Add(new float4(previous, next));
                            previous = next;
                        }
                        current = p;
                        break;
                    }
                    default:
                        if (open && !current.Equals(start)) lines.Add(new float4(current, start));
                        current = start;
                        open = false;
                        break;
                }
            }
            if (open && !current.Equals(start)) lines.Add(new float4(current, start));
        }

        static float2 Point(float[] commands, ref int i, float scale, float offsetX, float offsetY)
        {
            var p = new float2(commands[i] * scale + offsetX, commands[i + 1] * scale + offsetY);
            i += 2;
            return p;
        }

        static int Steps(float lengthInPixels) => math.clamp((int)math.ceil(math.sqrt(lengthInPixels) * 0.8f), 1, 24);
    }
}

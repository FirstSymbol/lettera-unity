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

        public void Execute(int index)
        {
            SdfTile tile = Tiles[index];
            float4* lines = (float4*)Lines.GetUnsafeReadOnlyPtr() + tile.LineStart;
            float toValue = 0.5f / tile.Spread;

            for (int row = 0; row < tile.Height; row++)
            {
                byte* output = tile.Target + (tile.Y + row) * tile.Stride + tile.X;
                float py = row + 0.5f;
                for (int column = 0; column < tile.Width; column++)
                {
                    float px = column + 0.5f;
                    float nearest = float.MaxValue;
                    int winding = 0;
                    for (int i = 0; i < tile.LineCount; i++)
                    {
                        float4 line = lines[i];
                        float2 a = line.xy, b = line.zw, p = new float2(px, py);
                        float2 edge = b - a, toPoint = p - a;
                        float lengthSquared = math.dot(edge, edge);
                        float t = lengthSquared > 0 ? math.saturate(math.dot(toPoint, edge) / lengthSquared) : 0;
                        float2 offset = toPoint - edge * t;
                        nearest = math.min(nearest, math.dot(offset, offset));

                        if ((a.y <= py) != (b.y <= py))
                        {
                            float crossing = a.x + (py - a.y) / (b.y - a.y) * edge.x;
                            if (crossing > px) winding += b.y > a.y ? 1 : -1;
                        }
                    }
                    float distance = math.sqrt(nearest);
                    if (winding == 0) distance = -distance;
                    output[column] = (byte)(math.saturate(0.5f + distance * toValue) * 255f + 0.5f);
                }
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

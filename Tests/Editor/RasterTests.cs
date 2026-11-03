using NUnit.Framework;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using FirstSymbol.Lettera.Outlines;
using UnityEngine;

namespace FirstSymbol.Lettera.Editor.Tests
{
    public unsafe class RasterTests : TextFixture
    {
        // A square from (10,10) to (30,30) in pixels, wound as a font contour.
        static readonly float4[] Square =
        {
            new float4(10, 10, 10, 30), new float4(10, 30, 30, 30), new float4(30, 30, 30, 10), new float4(30, 10, 10, 10),
        };

        static byte[] Rasterize(float4[] segments, int size, float spread)
        {
            var output = new NativeArray<byte>(size * size, Allocator.TempJob);
            var lines = new NativeArray<float4>(segments, Allocator.TempJob);
            var tiles = new NativeArray<SdfTile>(1, Allocator.TempJob);
            tiles[0] = new SdfTile { Target = (byte*)output.GetUnsafePtr(), Stride = size, Width = size, Height = size, LineCount = lines.Length, Spread = spread };
            new SdfTileJob { Tiles = tiles, Lines = lines }.Run(1);
            byte[] result = output.ToArray();
            output.Dispose();
            lines.Dispose();
            tiles.Dispose();
            return result;
        }

        [Test]
        public void DistanceFieldHasTheEdgeAtHalf()
        {
            byte[] sdf = Rasterize(Square, 40, 8);
            Assert.AreEqual(255, sdf[20 * 40 + 20], "deep inside saturates");
            Assert.AreEqual(0, sdf[20 * 40 + 1], "far outside saturates");
            // A pixel center half a pixel inside the left edge.
            float value = sdf[20 * 40 + 10] / 255f;
            Assert.AreEqual(0.5f + 0.5f * 0.5f / 8f, value, 0.01f);
            value = sdf[20 * 40 + 9] / 255f;
            Assert.AreEqual(0.5f - 0.5f * 0.5f / 8f, value, 0.01f);
        }

        [Test]
        public void OverlappingContoursStayFilled()
        {
            // Two overlapping squares wound the same way: the overlap must not cancel out.
            var shifted = new float4[8];
            Square.CopyTo(shifted, 0);
            for (int i = 0; i < 4; i++) shifted[4 + i] = Square[i] + new float4(5, 0, 5, 0);
            byte[] sdf = Rasterize(shifted, 40, 8);
            Assert.Greater(sdf[20 * 40 + 20], 127, "inside the overlap");
            Assert.Greater(sdf[20 * 40 + 32], 127, "inside the second square only");
        }

        [Test]
        public void FlattenClosesContoursAndHandlesCurves()
        {
            var lines = new NativeList<float4>(16, Allocator.Temp);
            float[] commands = { 0, 0, 0, 1, 10, 0, 2, 10, 10, 0, 10, 3, 0, 10, 0, 5, 0, 0, 4 };
            OutlineFlattener.Flatten(commands, commands.Length, 1, 0, 0, lines);
            Assert.Greater(lines.Length, 3);
            Assert.AreEqual(0f, lines[0].x);
            float4 last = lines[lines.Length - 1];
            Assert.AreEqual(0f, last.z);
            Assert.AreEqual(0f, last.w);
            lines.Dispose();
        }

        [Test]
        public void AtlasPlacesGlyphsAndLeavesSpacesEmpty()
        {
            FontFace face = Latin.LoadFace();
            uint a = LetteraNative.lt_font_glyph(face.Regular.Font, 'a', 0);
            uint space = LetteraNative.lt_font_glyph(face.Regular.Font, ' ', 0);
            AtlasGlyph glyph = GlyphAtlas.Get(face.Regular, a, 64);
            Assert.AreEqual(AtlasGlyph.Sdf, glyph.Kind);
            Assert.Greater(glyph.U1, glyph.U0);
            Assert.Greater(glyph.V1, glyph.V0);
            Assert.Greater(glyph.Right, glyph.Left);
            Assert.AreEqual(AtlasGlyph.Empty, GlyphAtlas.Get(face.Regular, space, 64).Kind);
            Assert.AreEqual(glyph.U0, GlyphAtlas.Get(face.Regular, a, 64).U0, "a second request reuses the entry");
            GlyphAtlas.Flush();
        }

        [Test]
        public void MeshHasLayersInBackToFrontOrder()
        {
            LetteraWorldText plain = Make("a");
            Assert.AreEqual(4, plain.Engine.VertexCount);
            LetteraWorldText layered = Make("<shadow=#000><outline=#F00>a</outline></shadow>");
            Assert.AreEqual(12, layered.Engine.VertexCount, "shadow, outline, face");
            Assert.AreEqual(0, layered.Engine.Colors[0].r, "shadow first");
            Assert.AreEqual(255, layered.Engine.Colors[4].r, "then the red outline");
            Assert.AreEqual(255, layered.Engine.Colors[8].g, "the white face on top");
        }

        [Test]
        public void DecorationsAddQuads()
        {
            int plain = Make("a b").Engine.VertexCount;
            Assert.AreEqual(plain + 4, Make("<u>a b</u>").Engine.VertexCount, "one continuous line across the space");
            Assert.AreEqual(plain + 8, Make("<u><s>a b</s></u>").Engine.VertexCount);
        }

        [Test]
        public void SpacesAddNoGeometry()
        {
            Assert.AreEqual(Make("ab").Engine.VertexCount, Make("a    b").Engine.VertexCount);
        }
        // The field as it was first computed: every line measured at every pixel, and the fill counted at each pixel from every line.
        static byte[] EveryLineAtEveryPixel(float4[] all, int size, float spread)
        {
            int Winding(float2 p, bool keptOnly)
            {
                int winding = 0;
                foreach (float4 line in all)
                {
                    float2 a = line.xy, b = line.zw;
                    if ((a.y <= p.y) == (b.y <= p.y)) continue;
                    float crossing = a.x + (p.y - a.y) / (b.y - a.y) * (b.x - a.x);
                    if (crossing > p.x) winding += b.y > a.y ? 1 : -1;
                }
                return winding;
            }
            var kept = new System.Collections.Generic.List<float4>();
            foreach (float4 line in all)
            {
                float2 a = line.xy, b = line.zw, edge = b - a;
                float length = math.length(edge);
                if (length <= 0) continue;
                float2 normal = new float2(-edge.y, edge.x) / length * 0.1f, middle = (a + b) * 0.5f;
                if ((Winding(middle + normal, false) != 0) != (Winding(middle - normal, false) != 0)) kept.Add(line);
            }
            float toValue = 0.5f / spread;
            var output = new byte[size * size];
            for (int row = 0; row < size; row++)
                for (int column = 0; column < size; column++)
                {
                    var p = new float2(column + 0.5f, row + 0.5f);
                    float nearest = float.MaxValue;
                    foreach (float4 line in kept)
                    {
                        float2 a = line.xy, b = line.zw, edge = b - a, toPoint = p - a;
                        float lengthSquared = math.dot(edge, edge);
                        float t = lengthSquared > 0 ? math.saturate(math.dot(toPoint, edge) / lengthSquared) : 0;
                        float2 offset = toPoint - edge * t;
                        nearest = math.min(nearest, math.dot(offset, offset));
                    }
                    float distance = math.sqrt(nearest);
                    if (Winding(p, false) == 0) distance = -distance;
                    output[row * size + column] = (byte)(math.saturate(0.5f + distance * toValue) * 255f + 0.5f);
                }
            return output;
        }

        static float4[] Polygon(float cx, float cy, float radius, int sides, bool clockwise, float spike = 0)
        {
            var points = new float2[sides];
            for (int i = 0; i < sides; i++)
            {
                float angle = (clockwise ? -1 : 1) * i * 2 * math.PI / sides;
                float r = radius * (spike > 0 && i % 2 == 1 ? spike : 1);
                points[i] = new float2(cx + r * math.cos(angle), cy + r * math.sin(angle));
            }
            var lines = new float4[sides];
            for (int i = 0; i < sides; i++) lines[i] = new float4(points[i], points[(i + 1) % sides]);
            return lines;
        }

        static float4[] Join(params float4[][] parts)
        {
            var all = new System.Collections.Generic.List<float4>();
            foreach (float4[] part in parts) all.AddRange(part);
            return all.ToArray();
        }

        [Test]
        public void TheFieldIsWhatMeasuringEveryLineAtEveryPixelGives()
        {
            var shapes = new[]
            {
                Square,
                Join(Square, Polygon(20, 20, 6, 24, true)),
                Join(Polygon(30, 30, 22, 64, false), Polygon(30, 30, 12, 48, true)),
                Polygon(32, 30, 26, 18, false, 0.4f),
                Join(Polygon(18, 30, 14, 40, false), Polygon(40, 30, 14, 40, false)),
                Polygon(3, 3, 9, 7, false),
                new[] { new float4(5, 5, 50, 52), new float4(50, 52, 55, 5), new float4(55, 5, 5, 5), new float4(20, 20, 20, 20) },
            };
            int size = 64;
            foreach (float spread in new[] { 2f, 6f, 12.5f })
                for (int s = 0; s < shapes.Length; s++)
                {
                    byte[] wanted = EveryLineAtEveryPixel(shapes[s], size, spread);
                    byte[] got = Rasterize(shapes[s], size, spread);
                    int differ = 0, most = 0;
                    for (int i = 0; i < wanted.Length; i++)
                    {
                        int d = System.Math.Abs(wanted[i] - got[i]);
                        if (d > 0) differ++;
                        most = System.Math.Max(most, d);
                    }
                    // Compiled code may round a last bit differently from this reference; anything more is a different field.
                    Assert.LessOrEqual(most, 1, "shape " + s + " spread " + spread);
                    Assert.LessOrEqual(differ, wanted.Length / 200, "shape " + s + " spread " + spread);
                }
        }

        [Test]
        public void AMultiChannelFieldCarriesTheSameTrueDistance()
        {
            float4[] shape = Join(Polygon(30, 30, 22, 64, false), Polygon(30, 30, 12, 48, true), Polygon(32, 30, 26, 18, false, 0.4f));
            int size = 64;
            byte[] single = Rasterize(shape, size, 6);
            var lines = new NativeList<float4>(shape.Length, Allocator.TempJob);
            foreach (float4 line in shape) lines.Add(line);
            var colors = new NativeList<byte>(shape.Length, Allocator.TempJob);
            EdgeColoring.Color(lines, colors, 0);
            var output = new NativeArray<byte>(size * size * 4, Allocator.TempJob);
            var tiles = new NativeArray<SdfTile>(1, Allocator.TempJob);
            tiles[0] = new SdfTile { Target = (byte*)output.GetUnsafePtr(), Stride = size * 4, Width = size, Height = size, LineCount = lines.Length, Spread = 6, Multi = true };
            new SdfTileJob { Tiles = tiles, Lines = lines.AsArray(), LineColors = colors.GetUnsafePtr() }.Run(1);
            for (int i = 0; i < size * size; i++) Assert.AreEqual(single[i], output[i * 4 + 3], "pixel " + i);
            output.Dispose();
            tiles.Dispose();
            lines.Dispose();
            colors.Dispose();
        }
    }
}

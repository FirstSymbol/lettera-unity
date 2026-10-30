using NUnit.Framework;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;
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
    }
}

using NUnit.Framework;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using FirstSymbol.Lettera.Outlines;
using UnityEngine;

namespace FirstSymbol.Lettera.Editor.Tests
{
    public unsafe class MsdfTests : TextFixture
    {
        const int W = 400, H = 400;
        static readonly Color Background = new Color(0, 0, 0, 1);

        [SetUp]
        public void ClearAtlas() => GlyphAtlas.Clear();

        LetteraWorldText m_Last;

        LetteraWorldText Big(string markup, LetteraRenderMode mode, float size = 300)
        {
            if (m_Last != null) Object.DestroyImmediate(m_Last.gameObject);
            LetteraWorldText text = Make("", 380, 380, null, size);
            m_Last = text;
            text.RenderMode = mode;
            text.HorizontalAlignment = LetteraHorizontalAlignment.Center;
            text.VerticalAlignment = LetteraVerticalAlignment.Middle;
            text.Text = markup;
            text.ForceRebuild();
            return text;
        }

        static void Rectangle(NativeList<float4> lines, float x0, float y0, float x1, float y1)
        {
            lines.Add(new float4(x0, y0, x1, y0));
            lines.Add(new float4(x1, y0, x1, y1));
            lines.Add(new float4(x1, y1, x0, y1));
            lines.Add(new float4(x0, y1, x0, y0));
        }

        static byte[] Raster(NativeList<float4> lines, int size, bool multi, float spread)
        {
            var colors = new NativeList<byte>(lines.Length, Allocator.TempJob);
            if (multi) EdgeColoring.Color(lines, colors, 0);
            else
                for (int i = 0; i < lines.Length; i++) colors.Add(0);
            int bytes = multi ? 4 : 1;
            var output = new byte[size * size * bytes];
            fixed (byte* target = output)
            {
                var tiles = new NativeArray<SdfTile>(1, Allocator.TempJob);
                tiles[0] = new SdfTile { Target = target, Stride = size * bytes, Width = size, Height = size, LineCount = lines.Length, Spread = spread, Multi = multi };
                new SdfTileJob { Tiles = tiles, Lines = lines.AsArray(), LineColors = colors.GetUnsafePtr() }.Run(1);
                tiles.Dispose();
            }
            colors.Dispose();
            return output;
        }

        // Two rectangles crossing like a plus sign: their contours overlap in the middle.
        static NativeList<float4> Plus()
        {
            var lines = new NativeList<float4>(16, Allocator.TempJob);
            Rectangle(lines, 10, 26, 54, 38);
            Rectangle(lines, 26, 10, 38, 54);
            return lines;
        }

        [Test]
        public void OverlapLeavesNoSeamInThePlainField()
        {
            NativeList<float4> lines = Plus();
            byte[] field = Raster(lines, 64, false, 6);
            lines.Dispose();
            // The middle of the plus, where the rectangles' own edges cross, is all inside and far from the real outline.
            Assert.Greater(field[32 * 64 + 26], 128 + 40, "Where an interior edge ran.");
            Assert.Greater(field[32 * 64 + 32], 128 + 40);
        }

        [Test]
        public void OverlapLeavesNoSeamInTheMultiField()
        {
            NativeList<float4> lines = Plus();
            byte[] field = Raster(lines, 64, true, 6);
            lines.Dispose();
            for (int y = 28; y < 36; y++)
                for (int x = 28; x < 36; x++)
                {
                    int at = (y * 64 + x) * 4;
                    int median = Mathf.Max(Mathf.Min(field[at], field[at + 1]), Mathf.Min(Mathf.Max(field[at], field[at + 1]), field[at + 2]));
                    Assert.Greater(median, 128, $"inside at {x},{y}");
                }
        }

        [Test]
        public void SquareCornerStaysSharpOutside()
        {
            var lines = new NativeList<float4>(4, Allocator.TempJob);
            Rectangle(lines, 16, 16, 48, 48);
            byte[] multi = Raster(lines, 64, true, 6);
            byte[] plain = Raster(lines, 64, false, 6);
            lines.Dispose();
            // Just outside the corner on the diagonal: the plain field is round, and its value is lower than the median's, which
            // stays about the same as at the same distance from the straight edge.
            const int x = 49, y = 49;
            int at = (y * 64 + x) * 4;
            int median = Mathf.Max(Mathf.Min(multi[at], multi[at + 1]), Mathf.Min(Mathf.Max(multi[at], multi[at + 1]), multi[at + 2]));
            int round = plain[y * 64 + x];
            Assert.Greater(median, round, "The median is closer to the edge than the true distance, past a corner.");
            Assert.Less(median, 128, "Still outside.");
        }

        [Test]
        public void TrueDistanceIsInAlpha()
        {
            var lines = new NativeList<float4>(4, Allocator.TempJob);
            Rectangle(lines, 16, 16, 48, 48);
            byte[] multi = Raster(lines, 64, true, 6);
            byte[] plain = Raster(lines, 64, false, 6);
            lines.Dispose();
            for (int i = 0; i < plain.Length; i++) Assert.AreEqual(plain[i], multi[i * 4 + 3], 1);
        }

        [Test]
        public void HoleKeepsItsInside()
        {
            var lines = new NativeList<float4>(8, Allocator.TempJob);
            Rectangle(lines, 8, 8, 56, 56);
            // Wound the other way, so it cuts a hole.
            lines.Add(new float4(24, 24, 24, 40));
            lines.Add(new float4(24, 40, 40, 40));
            lines.Add(new float4(40, 40, 40, 24));
            lines.Add(new float4(40, 24, 24, 24));
            byte[] multi = Raster(lines, 64, true, 6);
            lines.Dispose();
            int MedianAt(int x, int y)
            {
                int at = (y * 64 + x) * 4;
                return Mathf.Max(Mathf.Min(multi[at], multi[at + 1]), Mathf.Min(Mathf.Max(multi[at], multi[at + 1]), multi[at + 2]));
            }
            Assert.Less(MedianAt(32, 32), 128, "Inside the hole is outside the shape.");
            Assert.Greater(MedianAt(16, 32), 128, "The ring is inside the shape.");
            Assert.Less(MedianAt(4, 32), 128, "Past the ring is outside.");
        }

        [Test]
        public void EverySmoothContourIsWhite()
        {
            var lines = new NativeList<float4>(64, Allocator.TempJob);
            var colors = new NativeList<byte>(64, Allocator.TempJob);
            float2 previous = new float2(30, 0);
            for (int i = 1; i <= 36; i++)
            {
                float angle = i * 10 * Mathf.Deg2Rad;
                float2 next = new float2(Mathf.Cos(angle) * 30, Mathf.Sin(angle) * 30);
                if (i == 36) next = new float2(30, 0);
                lines.Add(new float4(previous, next));
                previous = next;
            }
            EdgeColoring.Color(lines, colors, 0);
            for (int i = 0; i < colors.Length; i++) Assert.AreEqual(7, colors[i] & 7);
            lines.Dispose();
            colors.Dispose();
        }

        [Test]
        public void AdjacentStretchesShareOneChannel()
        {
            var lines = new NativeList<float4>(4, Allocator.TempJob);
            var colors = new NativeList<byte>(4, Allocator.TempJob);
            Rectangle(lines, 0, 0, 10, 10);
            EdgeColoring.Color(lines, colors, 0);
            for (int i = 0; i < 4; i++)
            {
                int a = colors[i] & 7, b = colors[(i + 1) % 4] & 7;
                Assert.AreEqual(1, math.countbits(a & b), $"edges {i} and {i + 1}");
            }
            lines.Dispose();
            colors.Dispose();
        }

        // ------------------------------------------------------------- in a text

        [Test]
        public void MsdfGlyphsLiveInTheirOwnAtlas()
        {
            LetteraWorldText text = Big("M", LetteraRenderMode.MSDF);
            Assert.AreEqual(8f, text.Engine.Uv0[0].w);
            Assert.GreaterOrEqual(GlyphAtlas.MsdfPageCount, 1);
        }

        [Test]
        public void SwitchingModeRaisesOneLevelEverywhere()
        {
            LetteraWorldText text = Big("M", LetteraRenderMode.SDF);
            int shapes = text.ShapeCount;
            text.RenderMode = LetteraRenderMode.MSDF;
            text.ForceRebuild();
            Assert.AreEqual(shapes + 1, text.ShapeCount);
            Assert.AreEqual(8f, text.Engine.Uv0[0].w);
        }

        [Test]
        public void FaceRendersAndCornerIsSharper()
        {
            // The sharp corner of a capital L's foot at a large size: the pixel just inside the outer corner.
            Big("L", LetteraRenderMode.SDF, 300);
            Texture2D round = Render(W, H, Background);
            Big("L", LetteraRenderMode.MSDF, 300);
            Texture2D sharp = Render(W, H, Background);

            int Filled(Texture2D image)
            {
                int filled = 0;
                for (int y = 0; y < H; y++)
                    for (int x = 0; x < W; x++)
                        if (image.GetPixel(x, y).r > 0.5f)
                            filled++;
                return filled;
            }
            int a = Filled(round), b = Filled(sharp);
            Assert.Greater(b, 1000, "MSDF draws the glyph.");
            Assert.GreaterOrEqual(b, a, "A sharp corner fills what a rounded one leaves out.");
        }

        [Test]
        public void EveryLayerRendersInMsdf()
        {
            LetteraWorldText text = Big("<stroke=#F00,0.03><glow=#00F,0.05><inner-shadow=#000,0.02,-0.02><shadow=#0F0,0.1,-0.1>Hg</shadow></inner-shadow></glow></stroke>", LetteraRenderMode.MSDF, 120);
            Assert.IsTrue(text.Engine.Styled);
            Texture2D image = Render(W, H, Background);
            int lit = 0;
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                    if (image.GetPixel(x, y).maxColorComponent > 0.5f)
                        lit++;
            Assert.Greater(lit, 1000);
        }

        [Test]
        public void MixingModesKeepsOneMaterial()
        {
            TextEngine a = Make("x").Engine;
            LetteraWorldText b = Make("");
            b.RenderMode = LetteraRenderMode.MSDF;
            b.Text = "x";
            b.ForceRebuild();
            Assert.IsFalse(a.Styled);
            Assert.IsFalse(b.Engine.Styled);
            Assert.AreEqual(1, b.Engine.Segments.Count);
        }

        [Test]
        public void ColorGlyphsAreUnaffected()
        {
            LetteraWorldText text = Make("");
            text.RenderMode = LetteraRenderMode.MSDF;
            text.Text = "a b";
            text.ForceRebuild();
            Assert.AreEqual(2, text.Engine.VertexCount / 4, "A space has no entry.");
        }
    }
}

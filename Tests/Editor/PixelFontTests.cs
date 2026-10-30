using NUnit.Framework;
using UnityEngine;

namespace FirstSymbol.Lettera.Editor.Tests
{
    public class PixelFontTests : TextFixture
    {
        const int W = 200, H = 200;
        static readonly Color Background = new Color(0, 0, 0, 1);

        [SetUp]
        public void ClearAtlas()
        {
            GlyphAtlas.Clear();
            Pixel.FontPixelsPerEm = 8;
            Pixel.SnapToFontPixels = true;
        }

        static LetteraFont Pixel => Font("LetteraPixelTest.ttf");

        LetteraWorldText m_Last;

        LetteraWorldText Show(string markup, float size)
        {
            if (m_Last != null) Object.DestroyImmediate(m_Last.gameObject);
            LetteraWorldText text = Make("", 180, 180, Pixel, size);
            m_Last = text;
            text.Text = markup;
            text.ForceRebuild();
            return text;
        }

        static bool IsWhite(Color c) => c.r > 0.99f && c.g > 0.99f && c.b > 0.99f;
        static bool IsBlack(Color c) => c.r < 0.01f && c.g < 0.01f && c.b < 0.01f;
        static bool IsRed(Color c) => c.r > 0.99f && c.g < 0.01f && c.b < 0.01f;

        [TestCase(8)]
        [TestCase(16)]
        [TestCase(32)]
        public void MultiplesAreExactPixels(int size)
        {
            Show("H", size);
            Texture2D image = Render(W, H, Background);
            int filled = 0;
            foreach (Color c in image.GetPixels())
            {
                Assert.IsTrue(IsWhite(c) || IsBlack(c), $"An in-between color {c} at size {size}.");
                if (IsWhite(c)) filled++;
            }
            // H has 28 font pixels: two 2 by 7 stems and a 3 pixel bar.
            int scale = size / 8;
            Assert.AreEqual((2 * 7 * 2 + 3) * scale * scale, filled);
        }

        [Test]
        public void OneStrokeIsTheWholeRing()
        {
            Show("<stroke=#F00,0.125>I</stroke>", 16);
            Texture2D image = Render(W, H, Background);
            Assert.Greater(Count(image, IsRed), 0);
            foreach (Color c in image.GetPixels()) Assert.IsTrue(IsWhite(c) || IsBlack(c) || IsRed(c), "Hard edges: " + c);

            // Every cell next to a white one, also diagonally, is red.
            int scale = 2;
            for (int y = scale; y < H - scale; y += 1)
                for (int x = scale; x < W - scale; x += 1)
                {
                    if (!IsWhite(image.GetPixel(x, y))) continue;
                    foreach ((int dx, int dy) in new[] { (-scale, 0), (scale, 0), (0, scale), (0, -scale), (scale, scale), (-scale, -scale), (scale, -scale), (-scale, scale) })
                        Assert.IsFalse(IsBlack(image.GetPixel(x + dx, y + dy)), $"A gap in the ring at {x + dx},{y + dy}");
                }
        }

        [Test]
        public void TwoStrokeHasRoundedCorners()
        {
            Show("<stroke=#F00,0.25>.</stroke>", 16);
            Texture2D image = Render(W, H, Background);
            // A square dot's corner two cells out diagonally is not covered: its center is 2.12 pixels away.
            int redPixels = Count(image, IsRed);
            int scale = 2;
            // The dot is 2 by 2 cells, so 6 by 6 cells with the ring, minus the 4 corner cells, minus the dot.
            Assert.AreEqual((6 * 6 - 4 - 4) * scale * scale, redPixels);
        }

        [Test]
        public void ShadowOffsetSnapsToWholePixels()
        {
            TextEngine e = Show("<shadow=#F00,0.04,0,0>I</shadow>", 16).Engine;
            // 0.04 em is under a third of a font pixel, which rounds to none.
            Assert.AreEqual(e.Vertices[4].x, e.Vertices[0].x, 1e-3f);

            Pixel.SnapToFontPixels = false;
            e = Show("<shadow=#F00,0.04,0,0>I</shadow>", 16).Engine;
            Assert.AreNotEqual(e.Vertices[4].x, e.Vertices[0].x);
        }

        [Test]
        public void GlyphPositionsSitOnTheGrid()
        {
            LetteraWorldText text = Show("IHx", 16);
            for (int i = 0; i < text.GlyphCount; i++)
            {
                float cells = text.GetGlyph(i).X / 2;
                Assert.AreEqual(Mathf.Round(cells), cells, 1e-3f);
            }
        }

        [Test]
        public void DetectionFindsTheGridAndNotOnOrdinaryFonts()
        {
            Assert.AreEqual(8, Pixel.FindPixelGrid());
            Assert.AreEqual(0, Latin.FindPixelGrid());
            Assert.AreEqual(0, Arabic.FindPixelGrid());
            Assert.AreEqual(8, Pixel.FindPixelGrid(), "Detecting again gives the same value.");
        }

        [Test]
        public void GridZeroRendersAsAnOrdinaryFont()
        {
            Pixel.FontPixelsPerEm = 0;
            LetteraWorldText text = Show("H", 16);
            Assert.AreEqual(0f, text.Engine.Uv0[0].w);
            Texture2D image = Render(W, H, Background);
            bool inBetween = false;
            foreach (Color c in image.GetPixels()) inBetween |= !IsWhite(c) && !IsBlack(c);
            Assert.IsTrue(inBetween, "Antialiased, unlike the pixel path.");
        }

        [Test]
        public void MsdfModeStillGivesPixels()
        {
            if (m_Last != null) Object.DestroyImmediate(m_Last.gameObject);
            LetteraWorldText text = Make("", 180, 180, Pixel, 16);
            m_Last = text;
            text.RenderMode = LetteraRenderMode.MSDF;
            text.Text = "H";
            text.ForceRebuild();
            Assert.AreEqual(11f, text.Engine.Uv0[0].w);
        }

        [Test]
        public void PixelTextKeepsOneSegmentAndMaterial()
        {
            TextEngine e = Show("HI", 16).Engine;
            Assert.AreEqual(1, e.Segments.Count);
            Assert.IsFalse(e.Styled);
        }

        [Test]
        public void GlowStepsPerPixel()
        {
            LetteraWorldText text = Show("<glow=#F00,0.25,1>I</glow>", 32);
            Texture2D image = Render(W, H, Background);
            // Colors change only at font pixel edges: within one 4 pixel cell the color is the same.
            int column = Mathf.RoundToInt(text.GetGlyph(0).X + 16 + W * 0.5f);
            int start = -1;
            for (int y = 0; y < H && start < 0; y++)
                if (image.GetPixel(column, y).r > 0.05f)
                    start = y;
            Assert.GreaterOrEqual(start, 0, "column " + column);
            Color first = image.GetPixel(column, start);
            for (int y = start; y < start + 4; y++) Assert.AreEqual(first.r, image.GetPixel(column, y).r, 0.01f);
        }

        static int Count(Texture2D image, System.Func<Color, bool> test)
        {
            int count = 0;
            foreach (Color c in image.GetPixels())
                if (test(c))
                    count++;
            return count;
        }
    }
}

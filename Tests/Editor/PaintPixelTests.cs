using NUnit.Framework;
using UnityEngine;

namespace FirstSymbol.Lettera.Editor.Tests
{
    /// <summary>What the shaders actually draw, read back from a render. "O" at this size has a hollow middle and a thick ring.</summary>
    public class PaintPixelTests : TextFixture
    {
        const int Width = 300, Height = 300;
        static readonly Color Background = new Color(0, 0, 0, 1);

        [SetUp]
        public void ClearAtlas() => GlyphAtlas.Clear();

        LetteraWorldText m_Last;

        // One text at a time, or the one before it would still draw.
        LetteraWorldText Big(string markup, params LetteraStyle[] styles)
        {
            if (m_Last != null) Object.DestroyImmediate(m_Last.gameObject);
            LetteraWorldText text = Make("", 280, 280, null, 200);
            m_Last = text;
            text.HorizontalAlignment = LetteraHorizontalAlignment.Center;
            text.VerticalAlignment = LetteraVerticalAlignment.Middle;
            text.Styles.AddRange(styles);
            text.MarkStylesDirty();
            text.Text = markup;
            text.ForceRebuild();
            return text;
        }

        // The widest horizontal run of pixels on the middle row that a predicate accepts, and where it starts.
        static int RunOnMiddleRow(Texture2D image, System.Func<Color, bool> accept, out int start)
        {
            int best = 0;
            start = -1;
            int run = 0;
            for (int x = 0; x < Width; x++)
            {
                if (accept(image.GetPixel(x, Height / 2)))
                {
                    run++;
                    if (run > best)
                    {
                        best = run;
                        start = x - run + 1;
                    }
                }
                else
                {
                    run = 0;
                }
            }
            return best;
        }

        static bool IsRed(Color c) => c.r > 0.8f && c.g < 0.2f && c.b < 0.2f;
        static bool IsWhite(Color c) => c.r > 0.8f && c.g > 0.8f && c.b > 0.8f;

        [Test]
        public void PlainFaceRendersWhite()
        {
            Big("O");
            Texture2D image = Render(Width, Height, Background);
            Assert.Greater(RunOnMiddleRow(image, IsWhite, out _), 10);
        }

        [Test]
        public void OutsideStrokeAddsOnlyOutsidePixels()
        {
            Big("O");
            int face = RunOnMiddleRow(Render(Width, Height, Background), IsWhite, out _);
            Big("<stroke=#F00,0.05,outside>O</stroke>");
            Texture2D image = Render(Width, Height, Background);
            Assert.AreEqual(face, RunOnMiddleRow(image, IsWhite, out _), 1, "The face interior is unchanged.");
            Assert.Greater(RunOnMiddleRow(image, IsRed, out _), 4);
        }

        [Test]
        public void InsideStrokeShowsOverTheFace()
        {
            Big("O");
            int face = RunOnMiddleRow(Render(Width, Height, Background), IsWhite, out _);
            Big("<stroke=#F00,0.02,inside>O</stroke>");
            Texture2D image = Render(Width, Height, Background);
            int red = RunOnMiddleRow(image, IsRed, out _);
            Assert.AreEqual(4, red, 2, "0.02 em at size 200 is 4 pixels, inside the glyph.");
            Assert.AreEqual(face - 8, RunOnMiddleRow(image, IsWhite, out _), 3, "Both sides of the wall lose 4 pixels of white.");
        }

        [Test]
        public void CenterStrokeSplitsAcrossTheEdge()
        {
            // 4 pixels on each side of each edge of the wall: 8 pixels of band per edge, with white between.
            Big("O");
            int face = RunOnMiddleRow(Render(Width, Height, Background), IsWhite, out _);
            Big("<stroke=#F00,0.02,center>O</stroke>");
            Texture2D image = Render(Width, Height, Background);
            Assert.AreEqual(4, RunOnMiddleRow(image, IsRed, out _), 2, "The band is 0.02 em wide in all: 4 pixels.");
            Assert.AreEqual(face - 4, RunOnMiddleRow(image, IsWhite, out _), 3);
        }

        [Test]
        public void TransparentFillLeavesAHollowRim()
        {
            Big("<fill=#0000><stroke=#F00,0.03>O</stroke></fill>");
            Texture2D image = Render(Width, Height, Background);
            Assert.AreEqual(0, RunOnMiddleRow(image, IsWhite, out _));
            int red = RunOnMiddleRow(image, IsRed, out _);
            Assert.AreEqual(6, red, 2, "The rim alone: not the whole thickness of the glyph.");
        }

        [Test]
        public void FaceKeepsItsPixelsWhenAWideStrokeRaisesTheReach()
        {
            Big("O");
            int face = RunOnMiddleRow(Render(Width, Height, Background), IsWhite, out int start);
            Big("<stroke=#F00,0.5>O</stroke>");
            int wide = RunOnMiddleRow(Render(Width, Height, Background), IsWhite, out int wideStart);
            Assert.AreEqual(face, wide, 1);
            Assert.AreEqual(start, wideStart, 1, "Layers and face stay aligned across reach tiers.");
        }

        [Test]
        public void WideStrokeIsNotCutAtTheQuad()
        {
            LetteraWorldText text = Big("<stroke=#F00,0.3>O</stroke>");
            Texture2D image = Render(Width, Height, Background);
            // A cut quad shows as red pixels on a straight line right at its edge: the stroke's outer edge must fade first.
            Bounds bounds = text.Engine.MeshBounds;
            int edgeX = Mathf.RoundToInt(bounds.min.x + Width * 0.5f) + 1;
            for (int y = 0; y < Height; y++)
                if (edgeX >= 0 && edgeX < Width)
                    Assert.Less(image.GetPixel(edgeX, y).r, 0.5f, "row " + y);
            Assert.Greater(RunOnMiddleRow(image, IsRed, out _), 30);
        }

        [Test]
        public void GradientIsMonotonicAcrossARange()
        {
            var fill = new FillEffect();
            fill.SetPaint(LetteraPaint.Of(LetteraGradient.Of(new Color32(0, 0, 0, 255), new Color32(255, 255, 255, 255)), LetteraPaintSpan.Range));
            LetteraWorldText text = Make("", 280, 280, null, 60);
            text.VerticalAlignment = LetteraVerticalAlignment.Middle;
            text.Styles.Add(LetteraStyle.ForTag(fill, "k"));
            text.MarkStylesDirty();
            text.WordWrap = false;
            text.Text = "<k>HHHHHH</k>";
            text.ForceRebuild();
            Texture2D image = Render(Width, Height, new Color(1, 0, 0, 1));
            float last = -1;
            int samples = 0;
            for (int x = 0; x < Width; x++)
            {
                Color c = image.GetPixel(x, Height / 2);
                if (c.g <= 0.01f && c.r > 0.9f) continue; // Background.
                if (c.r - c.g > 0.05f) continue; // An antialiased edge.
                Assert.GreaterOrEqual(c.g, last - 0.02f, "x " + x);
                last = c.g;
                samples++;
            }
            Assert.Greater(samples, 20);
            Assert.Greater(last, 0.6f);
        }

        [Test]
        public void RadialContainDrawsCircles()
        {
            var fill = new FillEffect();
            LetteraPaint paint = LetteraPaint.Of(LetteraGradient.Of(new Color32(0, 0, 0, 255), new Color32(255, 255, 255, 255)), LetteraPaintSpan.Block);
            paint.Settings.Shape = LetteraPaintShape.Radial;
            paint.Settings.Fit = LetteraPaintFit.Contain;
            fill.SetPaint(paint);
            LetteraWorldText text = Make("", 280, 280, null, 150);
            text.HorizontalAlignment = LetteraHorizontalAlignment.Center;
            text.VerticalAlignment = LetteraVerticalAlignment.Middle;
            text.Styles.Add(LetteraStyle.ForTag(fill, "k"));
            text.MarkStylesDirty();
            text.Text = "<k>■</k>";
            text.ForceRebuild();

            // Noto Sans has the square; the block rectangle is the line, wider than high would stretch an ellipse.
            Assert.AreEqual(1f + 3f, text.Engine.Uv2[0].w, "Radial with the clamp edge.");
            Rect block = text.Engine.LayoutBounds;
            Vector2 center = block.center;
            Vector2 a = Sample(text, center + new Vector2(30, 0)), b = Sample(text, center + new Vector2(0, 30));
            Assert.AreEqual(a.magnitude, b.magnitude, 1e-2f, "Equal distances give equal ramp positions.");
        }

        // The paint position at a point, interpolated from the first quad.
        static Vector2 Sample(LetteraWorldText text, Vector2 point)
        {
            TextEngine e = text.Engine;
            Vector3 p0 = e.Vertices[0], p2 = e.Vertices[2];
            float tx = (point.x - p0.x) / (p2.x - p0.x), ty = (point.y - p0.y) / (p2.y - p0.y);
            Vector4 uv0 = e.Uv2[0], uv2 = e.Uv2[2];
            return new Vector2(Mathf.Lerp(uv0.x, uv2.x, tx), Mathf.Lerp(uv0.y, uv2.y, ty));
        }

        [Test]
        public void InnerShadowStaysInsideAndFallsOnTheLitSide()
        {
            Big("O");
            Texture2D plain = Render(Width, Height, Background);
            var before = plain.GetPixels();
            Big("<inner-shadow=#000F,0.05,-0.05,0>O</inner-shadow>");
            Texture2D image = Render(Width, Height, Background);
            var after = image.GetPixels();
            int darkened = 0, grown = 0;
            for (int i = 0; i < before.Length; i++)
            {
                if (after[i].r < before[i].r - 0.1f) darkened++;
                if (after[i].r > before[i].r + 0.02f) grown++;
            }
            Assert.Greater(darkened, 100);
            Assert.AreEqual(0, grown, "Pixels outside the face are unchanged.");
        }

        [Test]
        public void GlowFadesMonotonically()
        {
            Big("<glow=#F00,0.2>O</glow>");
            Texture2D image = Render(Width, Height, Background);
            int start = 0;
            while (start < Width && !IsWhite(image.GetPixel(start, Height / 2))) start++;
            Assert.Greater(start, 0);
            float last = 1.1f;
            for (int x = start - 3; x >= 0; x--)
            {
                float r = image.GetPixel(x, Height / 2).r;
                Assert.LessOrEqual(r, last + 0.02f, "x " + x);
                last = r;
            }
            Assert.Less(image.GetPixel(Mathf.Max(start - 45, 0), Height / 2).r, 0.05f, "Zero past the radius of 40 pixels.");
        }

        [Test]
        public void MultiplyMultipliesTheBackdrop()
        {
            LetteraPaint paint = LetteraPaint.Solid(new Color32(128, 128, 128, 255));
            paint.Settings.Blend = LetteraBlend.Multiply;
            var fill = new FillEffect();
            fill.SetPaint(paint);
            Big("<k>O</k>", LetteraStyle.ForTag(fill, "k"));
            Texture2D image = Render(Width, Height, new Color(1, 0.5f, 0, 1));
            RunOnMiddleRow(image, c => c.r < 0.7f && c.r > 0.3f, out int start);
            Assert.Greater(start, 0);
            Color inside = image.GetPixel(start + 6, Height / 2);
            Assert.AreEqual(0.5f, inside.r, 0.08f);
            Assert.AreEqual(0.25f, inside.g, 0.08f);
        }

        [Test]
        public void TextureDrawsBetweenItsNeighboursInOrder()
        {
            var texture = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            texture.SetPixel(0, 0, Color.green);
            texture.Apply();
            var fill = new FillEffect();
            fill.SetPaint(LetteraPaint.Of(texture));
            LetteraWorldText text = Make("", 280, 280, null, 80);
            text.VerticalAlignment = LetteraVerticalAlignment.Middle;
            text.HorizontalAlignment = LetteraHorizontalAlignment.Center;
            text.Styles.Add(LetteraStyle.ForTag(fill, "k"));
            text.MarkStylesDirty();
            text.Text = "H<k>H</k>H";
            text.ForceRebuild();
            Assert.AreEqual(3, text.Engine.Segments.Count);
            Texture2D image = Render(Width, Height, Background);
            Assert.Greater(RunOnMiddleRow(image, c => c.g > 0.8f && c.r < 0.2f, out int green), 10);
            Assert.Greater(RunOnMiddleRow(image, IsWhite, out int white), 10);
            Assert.AreNotEqual(green, white);
            Object.DestroyImmediate(texture);
        }

        [Test]
        public void DitherLeavesNoLongBands()
        {
            var fill = new FillEffect();
            fill.SetPaint(LetteraPaint.Of(LetteraGradient.Of(new Color32(60, 60, 60, 255), new Color32(70, 70, 70, 255)), LetteraPaintSpan.Range));
            LetteraWorldText text = Make("", 280, 280, null, 200);
            text.HorizontalAlignment = LetteraHorizontalAlignment.Center;
            text.VerticalAlignment = LetteraVerticalAlignment.Middle;
            text.Styles.Add(LetteraStyle.ForTag(fill, "k"));
            text.MarkStylesDirty();
            text.Text = "<k>O</k>";
            text.ForceRebuild();
            Texture2D image = Render(Width, Height, Background);
            int longest = 0, run = 0;
            float last = -1;
            for (int y = 0; y < Height; y++)
            {
                Color c = image.GetPixel(Width / 2 - 55, y);
                if (c.r > 0.18f && Mathf.Approximately(c.r, last)) longest = Mathf.Max(longest, ++run);
                else run = 0;
                last = c.r;
            }
            Assert.Less(longest, 20, "Ten levels over 200 pixels would otherwise show bands 20 pixels tall.");
        }
    }
}

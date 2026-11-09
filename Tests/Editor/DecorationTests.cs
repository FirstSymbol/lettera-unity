using NUnit.Framework;
using UnityEngine;

namespace FirstSymbol.Lettera.Editor.Tests
{
    public class DecorationTests : TextFixture
    {
        const int W = 400, H = 200;
        static readonly Color Background = new Color(0, 0, 0, 1);

        [SetUp]
        public void ClearAtlas() => GlyphAtlas.Clear();

        LetteraWorldText m_Last;

        LetteraWorldText Big(string markup, float size = 60)
        {
            if (m_Last != null) Object.DestroyImmediate(m_Last.gameObject);
            LetteraWorldText text = Make("", 380, 180, null, size);
            m_Last = text;
            text.HorizontalAlignment = LetteraHorizontalAlignment.Center;
            text.VerticalAlignment = LetteraVerticalAlignment.Middle;
            text.Text = markup;
            text.ForceRebuild();
            return text;
        }

        static bool IsRed(Color c) => c.r > 0.4f && c.g < 0.3f && c.b < 0.3f;

        // Red pixels in the column at a fraction of the image's width, as runs top to bottom.
        static int RunsInColumn(Texture2D image, int x, out int red)
        {
            int runs = 0;
            red = 0;
            bool inside = false;
            for (int y = 0; y < H; y++)
            {
                bool isRed = IsRed(image.GetPixel(x, y));
                if (isRed)
                {
                    red++;
                    if (!inside) runs++;
                }
                inside = isRed;
            }
            return runs;
        }

        // ------------------------------------------------------------- lines

        [Test]
        public void PlainLinesStayOnThePlainShader()
        {
            TextEngine e = Big("<u=#F00>a</u>").Engine;
            Assert.IsFalse(e.Styled);
            Assert.AreEqual(3f, e.Uv0[e.VertexCount - 1].w);
        }

        static float PeakRed(Texture2D image)
        {
            float peak = 0;
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    Color c = image.GetPixel(x, y);
                    if (c.g < 0.3f && c.b < 0.3f) peak = Mathf.Max(peak, c.r);
                }
            return peak;
        }

        [Test]
        public void EveryLineStyleReachesTheFullColorTheSolidLineDoes()
        {
            Big("<u=#F00,solid,0.1em>HHHH</u>");
            float solid = PeakRed(Render(W, H, Background));
            Assert.Greater(solid, 0.95f);
            foreach (string style in new[] { "double", "dotted", "dashed", "wavy" })
            {
                Big("<u=#F00," + style + ",0.1em>HHHH</u>");
                Assert.Greater(PeakRed(Render(W, H, Background)), solid * 0.95f, style + " line stays under the solid line's color");
            }
        }

        [Test]
        public void OldColorSlotStillWorks()
        {
            TextEngine e = Big("<u=#F00>a</u>").Engine;
            Assert.AreEqual(new Color32(255, 0, 0, 255), e.Colors[e.VertexCount - 1]);
        }

        [Test]
        public void DefaultLineTakesTheGlyphColor()
        {
            TextEngine e = Big("<color=#00F><u>a</u></color>").Engine;
            Assert.AreEqual(new Color32(0, 0, 255, 255), e.Colors[e.VertexCount - 1]);
        }

        [Test]
        public void SolidLineDraws()
        {
            Big("<u=#F00>HHHH</u>");
            Texture2D image = Render(W, H, Background);
            Assert.AreEqual(1, RunsInColumn(image, W / 2, out int red));
            Assert.Greater(red, 1);
        }

        [Test]
        public void DoubleShowsTwoBands()
        {
            Big("<u=#F00,double,0.05em>HHHH</u>");
            Assert.AreEqual(2, RunsInColumn(Render(W, H, Background), W / 2, out _));
        }

        [Test]
        public void DashedLeavesGaps()
        {
            Big("<u=#F00,dashed,0.05em>HHHH</u>");
            Texture2D image = Render(W, H, Background);
            int columns = 0, gaps = 0;
            for (int x = 0; x < W; x++)
            {
                if (RunsInColumn(image, x, out int red) > 0 && red > 0) columns++;
                else if (columns > 0) gaps++;
            }
            Assert.Greater(columns, 20);
            Assert.Greater(gaps, 10);
        }

        [Test]
        public void DottedDrawsRoundDots()
        {
            LetteraWorldText text = Big("<u=#F00,dotted,0.1em,-0.3em>HHHH</u>");
            Assert.IsTrue(text.Engine.Styled);
            Texture2D image = Render(W, H, Background);
            int dots = 0;
            bool inside = false;
            int row = -1;
            for (int y = 0; y < H && row < 0; y++)
                for (int x = 0; x < W; x++)
                    if (IsRed(image.GetPixel(x, y)))
                    {
                        row = y + 3;
                        break;
                    }
            for (int x = 0; x < W; x++)
            {
                bool red = IsRed(image.GetPixel(x, row));
                if (red && !inside) dots++;
                inside = red;
            }
            Assert.Greater(dots, 5);
        }

        [Test]
        public void WavyStaysACurve()
        {
            Big("<u=#F00,wavy,0.04em>HHHH</u>");
            Texture2D image = Render(W, H, Background);
            int lowest = H, highest = -1;
            for (int x = 40; x < W - 40; x++)
            {
                for (int y = 0; y < H; y++)
                {
                    if (!IsRed(image.GetPixel(x, y))) continue;
                    lowest = Mathf.Min(lowest, y);
                    highest = Mathf.Max(highest, y);
                }
            }
            Assert.Greater(highest - lowest, 6, "The line goes up and down.");
        }

        [Test]
        public void StrokeFollowsAStyledLine()
        {
            LetteraWorldText text = Big("<stroke=#0F0,0.05><u=#F00,wavy,0.04em>HH</u></stroke>");
            Assert.IsTrue(text.Engine.Styled);
            int lines = 0;
            for (int v = 0; v < text.Engine.VertexCount; v += 4)
                if (text.Engine.Uv0[v].w == 6f)
                    lines++;
            Assert.AreEqual(2, lines, "The line and its stroke.");
        }

        [Test]
        public void OverlayDrawsAboveTheGlyphs()
        {
            TextEngine under = Big("<u=#F00,solid,auto,auto,false,false>a</u>").Engine;
            int lineUnder = under.VertexCount;
            TextEngine over = Big("<stroke=#0F0,0.05><u=#F00,solid,auto,auto,false,true>a</u></stroke>").Engine;
            Assert.AreEqual(new Color32(255, 0, 0, 255), over.Colors[over.VertexCount - 1], "The line draws last.");
            Assert.AreEqual(new Color32(0, 255, 0, 255), over.Colors[over.VertexCount - 5], "Its stroke comes just before it, above the face.");
            Assert.Greater(lineUnder, 0);
        }

        [Test]
        public void SkipInkLeavesGapsAroundDescenders()
        {
            TextEngine plain = Big("<u=#F00>agg</u>").Engine;
            int whole = plain.VertexCount;
            TextEngine skipped = Big("<u=#F00,solid,auto,auto,true>agg</u>").Engine;
            Assert.AreNotEqual(whole, skipped.VertexCount);
        }

        [Test]
        public void ThicknessZeroDrawsNothing()
        {
            Assert.AreEqual(4, Big("<u=#F00,solid,0>a</u>").Engine.VertexCount);
        }

        [Test]
        public void LinesSplitPerBidiFragment()
        {
            // The Hebrew run reads backwards, so the second Hebrew letter lands between the underlined text and the first one.
            LetteraWorldText text = Make("");
            text.Font = null;
            text.FontStack = Stack(Latin, Hebrew);
            text.Text = "<u=#F00>aa א</u>ב cc";
            text.ForceRebuild();
            int lines = 0;
            for (int v = 0; v < text.Engine.VertexCount; v += 4)
                if (text.Engine.Uv0[v].w == 3f)
                    lines++;
            Assert.AreEqual(2, lines);
        }

        [Test]
        public void GradientLineUsesItsPaint()
        {
            var underline = new UnderlineEffect();
            underline.SetPaint(LetteraPaint.Of(LetteraGradient.Of(Color.red, Color.blue), LetteraPaintSpan.Range));
            LetteraWorldText text = Make("");
            text.Styles.Add(LetteraStyle.ForTag(underline, "k"));
            text.MarkStylesDirty();
            text.Text = "<k>abc</k>";
            text.ForceRebuild();
            TextEngine e = text.Engine;
            Assert.IsTrue(e.Styled);
            Assert.AreEqual(1f, e.Uv2[e.VertexCount - 1].w);
        }

        // ------------------------------------------------------------- highlights

        static int Boxes(TextEngine e)
        {
            int boxes = 0;
            for (int v = 0; v < e.VertexCount; v += 4)
                if (e.Uv0[v].w == 7f)
                    boxes++;
            return boxes;
        }

        const string Wrapped = "aaaa aaaa aaaa";

        LetteraWorldText Marked(string geometry, float radius, string body = Wrapped, float width = 60)
        {
            if (m_Last != null) Object.DestroyImmediate(m_Last.gameObject);
            m_Last = Make($"<mark=#FF0,{geometry},fromgeometry,{radius.ToString(System.Globalization.CultureInfo.InvariantCulture)}>{body}</mark>", width);
            return m_Last;
        }

        static int RoundedCorners(TextEngine e)
        {
            int corners = 0;
            for (int v = 0; v < e.VertexCount; v += 4)
            {
                if (e.Uv0[v].w != 7f) continue;
                Vector4 r = e.Uv3[v];
                corners += (r.x > 0 ? 1 : 0) + (r.y > 0 ? 1 : 0) + (r.z > 0 ? 1 : 0) + (r.w > 0 ? 1 : 0);
            }
            return corners;
        }

        [Test]
        public void HighlightDrawsBehindByDefault()
        {
            TextEngine e = Make("<mark=#FF0>a</mark>").Engine;
            Assert.AreEqual(7f, e.Uv0[0].w);
            Assert.AreEqual(0f, e.Uv0[4].w);
        }

        [Test]
        public void HighlightCanDrawAbove()
        {
            TextEngine e = Make("<mark=#FF0,range,fromgeometry,0,0,above>a</mark>").Engine;
            Assert.AreEqual(0f, e.Uv0[0].w);
            Assert.AreEqual(7f, e.Uv0[4].w);
        }

        [Test]
        public void LineGeometryHasAllCornersOnEveryLine()
        {
            TextEngine e = Marked("line", 0.2f).Engine;
            Assert.AreEqual(3, m_Last.Lines.Count);
            Assert.AreEqual(3, Boxes(e));
            Assert.AreEqual(12, RoundedCorners(e));
        }

        [Test]
        public void RangeGeometryRoundsOnlyTheOuterCorners()
        {
            TextEngine e = Marked("range", 0.2f).Engine;
            Assert.AreEqual(3, Boxes(e));
            Assert.AreEqual(4, RoundedCorners(e));
        }

        [Test]
        public void GlyphGeometryMakesOneShapePerCluster()
        {
            TextEngine e = Make("<mark=#FF0,glyph>abc</mark>").Engine;
            Assert.AreEqual(3, Boxes(e));
        }

        [Test]
        public void BlockJoinsRowsWithoutOverlap()
        {
            TextEngine e = Marked("block", 0.1f).Engine;
            Assert.AreEqual(3, Boxes(e));
            for (int v = 0; v + 8 <= e.VertexCount; v += 4)
            {
                if (e.Uv0[v].w != 7f || e.Uv0[v + 4].w != 7f) continue;
                Assert.AreEqual(e.Vertices[v].y, e.Vertices[v + 5].y, 1e-3f, "Rows meet exactly.");
            }
        }

        [Test]
        public void BlockHasNoSeamUnderTranslucentPaint()
        {
            if (m_Last != null) Object.DestroyImmediate(m_Last.gameObject);
            LetteraWorldText text = Make("<mark=#F008,block><line-height=1>HHHH\nHHHH</line-height></mark>", 380, 180, null, 60);
            m_Last = text;
            text.HorizontalAlignment = LetteraHorizontalAlignment.Center;
            text.VerticalAlignment = LetteraVerticalAlignment.Middle;
            text.ForceRebuild();
            Texture2D image = Render(W, H, Background);
            float redest = 0;
            for (int y = 0; y < H; y++)
            {
                Color c = image.GetPixel(10 + W / 2 - 80, y);
                if (c.g < 0.1f) redest = Mathf.Max(redest, c.r);
            }
            // The paint's alpha is 0x88. A Linear project shows one layer as 0.76 and two as 0.9; a Gamma project, as 0.53 and 0.78.
            bool linear = QualitySettings.activeColorSpace == ColorSpace.Linear;
            float alpha = 0x88 / 255f;
            float one = linear ? Mathf.LinearToGammaSpace(alpha) : alpha;
            float two = linear ? Mathf.LinearToGammaSpace(1f - (1f - alpha) * (1f - alpha)) : 1f - (1f - alpha) * (1f - alpha);
            Assert.Less(redest, (one + two) / 2f, $"One layer shows as {one:0.##}; two layers would show as {two:0.##} ({QualitySettings.activeColorSpace}).");
            Assert.Greater(redest, one - 0.05f);
        }

        [Test]
        public void RtlCornersFollowTheDirection()
        {
            var highlight = new HighlightEffect();
            highlight.Look.Corners = LetteraCorners.TopStart;
            highlight.Look.CornerRadius = 0.2f;
            highlight.Look.Geometry = LetteraHighlightGeometry.Line;
            LetteraWorldText text = Make("", 300, 100, Hebrew);
            text.Styles.Add(LetteraStyle.ForTag(highlight, "k"));
            text.MarkStylesDirty();
            text.Text = "<k>אב</k>";
            text.ForceRebuild();
            Vector4 radii = text.Engine.Uv3[0];
            Assert.AreEqual(0f, radii.x, "Top left stays square.");
            Assert.Greater(radii.y, 0f, "In right-to-left text, the top start is on the right.");
        }

        [Test]
        public void EmptyRangeDrawsNothing() => Assert.AreEqual(0, Make("<mark=#FF0></mark>").Engine.VertexCount);

        [Test]
        public void PaddingDoesNotChangeLayout()
        {
            float plain = WidthOf(Make("<mark=#FF0>abc</mark>"));
            LetteraWorldText padded = Make("<mark=#FF0,range,fromgeometry,0,0.2>abc</mark>");
            Assert.AreEqual(plain, WidthOf(padded), 1e-3f);
            TextEngine e = padded.Engine;
            Assert.Less(e.Vertices[0].x, padded.GetGlyph(0).X - 3f);
        }

        [Test]
        public void HighlightStaysInOneSegment()
        {
            TextEngine e = Make("<mark=#FF0>a</mark> b").Engine;
            Assert.AreEqual(1, e.Segments.Count);
        }
    }
}

using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace FirstSymbol.Lettera.Editor.Tests
{
    /// <summary>The outlines of a laid-out text as paths: where they are, what they are made of, and that reading them again costs nothing.</summary>
    public class GlyphOutlineTests : TextFixture
    {
        static unsafe Rect InkOf(LetteraFont font, char c, float size, Vector2 origin)
        {
            FontFace face = font.LoadFace();
            uint glyph = LetteraNative.lt_font_glyph(face.Regular.Font, c, 0);
            int* e = stackalloc int[4];
            Assert.AreNotEqual(0, LetteraNative.lt_glyph_extents(face.Regular.Font, glyph, e));
            float unit = size / face.Metrics.Upem;
            // Extents give the box from the left bearing and the top, with a negative height.
            float left = origin.x + e[0] * unit, right = left + e[2] * unit, top = origin.y + e[1] * unit, bottom = top + e[3] * unit;
            return Rect.MinMaxRect(left, bottom, right, top);
        }

        static void AssertRect(Rect expected, Rect actual, float tolerance, string what)
        {
            Assert.AreEqual(expected.xMin, actual.xMin, tolerance, what + " left");
            Assert.AreEqual(expected.xMax, actual.xMax, tolerance, what + " right");
            Assert.AreEqual(expected.yMin, actual.yMin, tolerance, what + " bottom");
            Assert.AreEqual(expected.yMax, actual.yMax, tolerance, what + " top");
        }

        [Test]
        public void AGlyphsBoxIsTheInkOfItsLetter()
        {
            LetteraWorldText text = Make("HOg", size: 100);
            var buffer = new LetteraOutlineBuffer();
            Assert.AreEqual(3, text.GetGlyphOutlines(buffer));
            Assert.AreEqual(0, buffer.SkippedGlyphs);
            foreach (char c in "HOg")
            {
                int i = "HOg".IndexOf(c);
                LetteraGlyphInfo glyph = text.GetGlyph(i);
                LetteraGlyphPath path = buffer.Paths[i];
                Assert.AreEqual(i, path.GlyphIndex);
                Assert.AreEqual(LetteraOutlineKind.Outline, path.Kind);
                Assert.AreEqual(glyph.Glyph, path.Glyph);
                Assert.AreEqual(100, path.FontSize, 1e-3f);
                AssertRect(InkOf(Latin, c, glyph.Size, new Vector2(glyph.X, glyph.Y)), path.Bounds, 0.05f, c.ToString());
            }
        }

        [Test]
        public void ThePathsPointsAgreeWithItsCommands()
        {
            LetteraWorldText text = Make("Ra&g", size: 64);
            var buffer = new LetteraOutlineBuffer();
            text.GetGlyphOutlines(buffer);
            int commands = 0, points = 0;
            foreach (LetteraGlyphPath path in buffer.Paths)
            {
                Assert.AreEqual(commands, path.CommandStart);
                Assert.AreEqual(points, path.PointStart);
                int expected = 0;
                bool open = false;
                foreach (LetteraPathCommand c in buffer.CommandsOf(path))
                {
                    switch (c)
                    {
                        case LetteraPathCommand.MoveTo: Assert.IsFalse(open, "a contour is closed before the next begins"); open = true; expected += 1; break;
                        case LetteraPathCommand.LineTo: Assert.IsTrue(open); expected += 1; break;
                        case LetteraPathCommand.QuadTo: Assert.IsTrue(open); expected += 2; break;
                        case LetteraPathCommand.CubicTo: Assert.IsTrue(open); expected += 3; break;
                        case LetteraPathCommand.Close: Assert.IsTrue(open); open = false; break;
                    }
                }
                Assert.IsFalse(open, "every contour ends closed");
                Assert.AreEqual(expected, path.PointCount);
                Assert.AreEqual(path.PointCount, buffer.PointsOf(path).Length);
                commands += path.CommandCount;
                points += path.PointCount;
            }
            Assert.AreEqual(commands, buffer.Commands.Length);
            Assert.AreEqual(points, buffer.Points.Length);
        }

        [Test]
        public void ALetterOMakesTwoContoursOfWindingOpposedSigns()
        {
            LetteraWorldText text = Make("O", size: 100);
            var buffer = new LetteraOutlineBuffer();
            text.GetGlyphOutlines(buffer);
            int moves = 0;
            foreach (LetteraPathCommand c in buffer.Commands)
                if (c == LetteraPathCommand.MoveTo)
                    moves++;
            Assert.AreEqual(2, moves, "the ring and its hole");
            // With non-zero winding the hole needs the opposite direction.
            var polygon = new List<Vector2>();
            var areas = new List<float>();
            ReadOnlySpan<Vector2> points = buffer.Points;
            ReadOnlySpan<LetteraPathCommand> commands = buffer.Commands;
            int p = 0;
            foreach (LetteraPathCommand c in commands)
            {
                switch (c)
                {
                    case LetteraPathCommand.MoveTo: polygon.Clear(); polygon.Add(points[p++]); break;
                    case LetteraPathCommand.LineTo: polygon.Add(points[p++]); break;
                    case LetteraPathCommand.QuadTo: polygon.Add(points[p + 1]); p += 2; break;
                    case LetteraPathCommand.CubicTo: polygon.Add(points[p + 2]); p += 3; break;
                    case LetteraPathCommand.Close:
                        float area = 0;
                        for (int i = 0; i < polygon.Count; i++)
                        {
                            Vector2 a = polygon[i], b = polygon[(i + 1) % polygon.Count];
                            area += a.x * b.y - b.x * a.y;
                        }
                        areas.Add(area * 0.5f);
                        break;
                }
            }
            Assert.AreEqual(2, areas.Count);
            Assert.Less(areas[0] * areas[1], 0, "the two contours turn opposite ways");
            Assert.Greater(Mathf.Abs(areas[0]), Mathf.Abs(areas[1]), "the first is the outer");
        }

        static float DistanceToSegment(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float t = ab.sqrMagnitude > 0 ? Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude) : 0;
            return (p - (a + ab * t)).magnitude;
        }

        [TestCase("O", 0.5f)]
        [TestCase("S&g@", 0.1f)]
        [TestCase("O", 4f)]
        public void FlatteningStaysWithinTheTolerance(string content, float tolerance)
        {
            LetteraWorldText text = Make(content, size: 100);
            var curved = new LetteraOutlineBuffer();
            var flat = new LetteraOutlineBuffer();
            text.GetGlyphOutlines(curved);
            text.GetGlyphOutlines(flat, new LetteraOutlineOptions { Tolerance = tolerance });
            Assert.AreEqual(curved.Paths.Length, flat.Paths.Length);
            foreach (LetteraPathCommand c in flat.Commands)
                Assert.IsTrue(c == LetteraPathCommand.MoveTo || c == LetteraPathCommand.LineTo || c == LetteraPathCommand.Close, "lines only, got " + c);
            Assert.GreaterOrEqual(flat.Commands.Length, curved.Commands.Length, "a curve is at least one line");

            // Every point along every curve of the first reading is near a line of the second.
            var segments = new List<(Vector2, Vector2)>();
            Vector2 start = default, pen = default;
            int fp = 0;
            foreach (LetteraPathCommand c in flat.Commands)
            {
                switch (c)
                {
                    case LetteraPathCommand.MoveTo: pen = start = flat.Points[fp++]; break;
                    case LetteraPathCommand.LineTo: segments.Add((pen, flat.Points[fp])); pen = flat.Points[fp++]; break;
                    case LetteraPathCommand.Close: segments.Add((pen, start)); pen = start; break;
                }
            }
            float worst = 0;
            int cp = 0;
            pen = start = default;
            foreach (LetteraPathCommand c in curved.Commands)
            {
                switch (c)
                {
                    case LetteraPathCommand.MoveTo: pen = start = curved.Points[cp++]; break;
                    case LetteraPathCommand.LineTo: pen = curved.Points[cp++]; break;
                    case LetteraPathCommand.Close: pen = start; break;
                    case LetteraPathCommand.QuadTo:
                    {
                        Vector2 control = curved.Points[cp], to = curved.Points[cp + 1];
                        for (int k = 0; k <= 16; k++)
                        {
                            float t = k / 16f, u = 1 - t;
                            worst = Mathf.Max(worst, Nearest(segments, u * u * pen + 2 * u * t * control + t * t * to));
                        }
                        pen = to;
                        cp += 2;
                        break;
                    }
                    case LetteraPathCommand.CubicTo:
                    {
                        Vector2 a = curved.Points[cp], b = curved.Points[cp + 1], to = curved.Points[cp + 2];
                        for (int k = 0; k <= 16; k++)
                        {
                            float t = k / 16f, u = 1 - t;
                            worst = Mathf.Max(worst, Nearest(segments, u * u * u * pen + 3 * u * u * t * a + 3 * u * t * t * b + t * t * t * to));
                        }
                        pen = to;
                        cp += 3;
                        break;
                    }
                }
            }
            Assert.LessOrEqual(worst, tolerance * 1.01f + 1e-3f, "the farthest a curve strays from the lines");
        }

        static float Nearest(List<(Vector2, Vector2)> segments, Vector2 p)
        {
            float best = float.MaxValue;
            foreach ((Vector2 a, Vector2 b) in segments) best = Mathf.Min(best, DistanceToSegment(p, a, b));
            return best;
        }

        [Test]
        public void GlyphsAreWhereLayoutPutsThemAfterBreaksAndAlignment()
        {
            LetteraWorldText text = Make("alpha beta gamma delta", width: 160, height: 400, size: 30);
            text.HorizontalAlignment = LetteraHorizontalAlignment.Right;
            text.ForceRebuild();
            Assert.Greater(text.Lines.Count, 1, "the text breaks into lines");
            var buffer = new LetteraOutlineBuffer();
            int paths = text.GetGlyphOutlines(buffer);
            Assert.Greater(paths, 10);
            foreach (LetteraGlyphPath path in buffer.Paths)
            {
                LetteraGlyphInfo glyph = text.GetGlyph(path.GlyphIndex);
                Assert.AreEqual(glyph.Line, path.Line);
                Assert.AreEqual(glyph.Cluster, path.Cluster);
                // A glyph's ink starts at its pen and stays inside the line's box.
                Assert.GreaterOrEqual(path.Bounds.xMin, glyph.X - glyph.Size * 0.3f);
                Assert.LessOrEqual(path.Bounds.xMax, glyph.X + glyph.Advance + glyph.Size * 0.3f);
                LetteraLine line = text.Lines[glyph.Line];
                Assert.LessOrEqual(path.Bounds.yMax, line.Baseline + line.Ascent + 0.5f);
                Assert.GreaterOrEqual(path.Bounds.yMin, line.Baseline - line.Descent - 0.5f);
            }
            Bounds ink = text.InkBounds;
            foreach (LetteraGlyphPath path in buffer.Paths)
            {
                Assert.GreaterOrEqual(path.Bounds.xMin, ink.min.x - 0.5f);
                Assert.LessOrEqual(path.Bounds.xMax, ink.max.x + 0.5f);
            }
        }

        [Test]
        public void EachPathLiesInTheQuadTheTextDraws()
        {
            LetteraWorldText text = Make("Quad test", size: 48);
            var buffer = new LetteraOutlineBuffer();
            text.GetGlyphOutlines(buffer);
            TextEngine engine = text.Engine;
            Assert.AreEqual(engine.VertexCount, buffer.Paths.Length * 4, "a quad for each glyph with ink");
            var used = new bool[buffer.Paths.Length];
            for (int q = 0; q < buffer.Paths.Length; q++)
            {
                Vector3 a = engine.Vertices[q * 4], c = engine.Vertices[q * 4 + 2];
                Rect quad = Rect.MinMaxRect(Mathf.Min(a.x, c.x), Mathf.Min(a.y, c.y), Mathf.Max(a.x, c.x), Mathf.Max(a.y, c.y));
                int match = -1;
                for (int i = 0; i < buffer.Paths.Length; i++)
                {
                    Rect r = buffer.Paths[i].Bounds;
                    if (used[i] || r.xMin < quad.xMin - 0.5f || r.xMax > quad.xMax + 0.5f || r.yMin < quad.yMin - 0.5f || r.yMax > quad.yMax + 0.5f) continue;
                    match = i;
                    break;
                }
                Assert.GreaterOrEqual(match, 0, "no path inside quad " + q);
                used[match] = true;
                // The quad is the ink with the margin the field needs: no more than a few pixels each way.
                Rect ink = buffer.Paths[match].Bounds;
                Assert.AreEqual(ink.center.x, quad.center.x, 4f);
                Assert.AreEqual(ink.center.y, quad.center.y, 4f);
                Assert.Less(quad.width - ink.width, 0.5f * 48);
                Assert.Less(quad.height - ink.height, 0.5f * 48);
            }
        }

        [Test]
        public void AMotionsStateAtCallTimeIsTheStateRead()
        {
            LetteraWorldText text = Make("<wave>abc</wave>", size: 40);
            LetteraMotionHub.Tick(0.3f, 0.3f);
            var posed = new LetteraOutlineBuffer();
            var rest = new LetteraOutlineBuffer();
            text.GetGlyphOutlines(posed);
            text.GetGlyphOutlines(rest, new LetteraOutlineOptions { IgnoreMotion = true });
            Assert.AreEqual(3, posed.Paths.Length);
            bool moved = false;
            for (int g = 0; g < 3; g++)
            {
                float shift = posed.Paths[g].Bounds.center.y - rest.Paths[g].Bounds.center.y;
                // The mesh moved the glyph's quad by the same amount.
                float meshShift = text.Engine.Vertices[g * 4].y - text.Engine.RestVertex(g * 4).y;
                Assert.AreEqual(meshShift, shift, 0.01f, "glyph " + g);
                if (Mathf.Abs(shift) > 0.01f) moved = true;
            }
            Assert.IsTrue(moved, "the wave moved something");
            // Moved on, the next reading is of the new state.
            LetteraMotionHub.Tick(0.4f, 0.4f);
            var later = new LetteraOutlineBuffer();
            text.GetGlyphOutlines(later);
            Assert.AreNotEqual(posed.Paths[0].Bounds.center.y, later.Paths[0].Bounds.center.y);
        }

        [Test]
        public void ColourAndStrokeAreTheRunsOwn()
        {
            LetteraWorldText text = Make("<color=#FF0000>a</color><stroke=#0000FF,0.1>b</stroke>c", size: 50);
            var buffer = new LetteraOutlineBuffer();
            text.GetGlyphOutlines(buffer);
            Assert.AreEqual(3, buffer.Paths.Length);
            Assert.AreEqual(new Color32(255, 0, 0, 255), buffer.Paths[0].Fill);
            Assert.AreEqual(0, buffer.Paths[0].StrokeWidth);
            Assert.AreEqual(new Color32(0, 0, 255, 255), buffer.Paths[1].Stroke);
            Assert.AreEqual(5f, buffer.Paths[1].StrokeWidth, 1e-3f, "a tenth of an em of 50");
            Assert.AreEqual(1f, buffer.Paths[1].StrokeAlign);
            Assert.AreEqual(0, buffer.Paths[2].StrokeWidth);
            Assert.AreEqual(1f, buffer.Paths[2].Opacity);
        }

        [Test]
        public void SpacesHaveNoPathAndAnEmptyTextHasNone()
        {
            var buffer = new LetteraOutlineBuffer();
            Assert.AreEqual(0, Make("   ", size: 30).GetGlyphOutlines(buffer));
            Assert.AreEqual(0, Make("", size: 30).GetGlyphOutlines(buffer));
            Assert.AreEqual(2, Make("a b", size: 30).GetGlyphOutlines(buffer));
            Assert.AreEqual(0, buffer.Paths[0].GlyphIndex);
            Assert.AreEqual(2, buffer.Paths[1].GlyphIndex, "the space is glyph 1 and has none");
        }

        [Test]
        public void AFormulasRuleIsAFilledBox()
        {
            LetteraWorldText text = Make("<math>\\frac{a}{b}</math>", 2000, 2000, size: 40);
            var buffer = new LetteraOutlineBuffer();
            text.GetGlyphOutlines(buffer);
            ReadOnlySpan<TextEngine.PlacedGlyph> placed = text.Engine.PlacedSpan;
            int rule = -1;
            for (int i = 0; i < placed.Length; i++)
                if (placed[i].Special == TextEngine.SpecialRuleForSolids)
                    rule = i;
            Assert.GreaterOrEqual(rule, 0, "a fraction has a bar");
            LetteraGlyphPath bar = default;
            foreach (LetteraGlyphPath path in buffer.Paths)
                if (path.GlyphIndex == rule)
                    bar = path;
            Assert.AreEqual(5, bar.CommandCount, "move, three lines, close");
            Assert.AreEqual(4, bar.PointCount);
            Assert.Greater(bar.Bounds.width, bar.Bounds.height * 3, "a bar is wider than it is thick");
            Assert.Greater(bar.Bounds.height, 0.2f);
        }

        [Test]
        public unsafe void AColourGlyphWithoutLayersIsMarkedNotOutlineAndSkipped()
        {
            LetteraFont colour = Font("test_glyphs-glyf_colr_1.ttf");
            FontFace face = colour.LoadFace();
            Assert.IsTrue(face.HasColorPaint, "the fixture paints its glyphs");
            int count = LetteraNative.lt_face_collect_unicodes(face.Face, null, 0);
            var codepoints = new uint[count];
            fixed (uint* p = codepoints) LetteraNative.lt_face_collect_unicodes(face.Face, p, count);
            int painted = 0;
            foreach (uint cp in codepoints)
            {
                uint glyph = LetteraNative.lt_font_glyph(face.Regular.Font, cp, 0);
                if (glyph != 0 && LetteraNative.lt_glyph_has_paint(face.Face, glyph) != 0)
                {
                    painted = (int)cp;
                    break;
                }
            }
            if (painted == 0) Assert.Ignore("no painted glyph with a codepoint in the fixture");
            LetteraWorldText text = Make(char.ConvertFromUtf32(painted), font: colour, size: 60);
            var buffer = new LetteraOutlineBuffer();
            text.GetGlyphOutlines(buffer);
            Assert.AreEqual(1, buffer.Paths.Length);
            Assert.AreEqual(LetteraOutlineKind.NotOutline, buffer.Paths[0].Kind);
            Assert.AreEqual(0, buffer.Paths[0].CommandCount);
            Assert.AreEqual(1, buffer.SkippedGlyphs);
            Assert.Greater(buffer.Paths[0].Bounds.width, 1f, "the cell it fills");
        }

        [Test]
        public void ASystemEmojiGivesLayersOrIsSkipped()
        {
            LetteraSettings.Instance.DisableSystemFontFallback = false;
            if (LetteraSystemFonts.EmojiFace() == null) Assert.Ignore("no colour emoji font here");
            FontFace emoji = LetteraSystemFonts.EmojiFace();
            LetteraWorldText text = Make("\U0001F600", size: 60);
            var buffer = new LetteraOutlineBuffer();
            text.GetGlyphOutlines(buffer);
            Assert.GreaterOrEqual(buffer.Paths.Length, 1);
            if (emoji.HasColorLayers)
            {
                bool layered = false;
                foreach (LetteraGlyphPath path in buffer.Paths)
                    if (path.Kind == LetteraOutlineKind.ColorLayer)
                    {
                        layered = true;
                        Assert.GreaterOrEqual(path.Layer, 0);
                    }
                if (!layered) Assert.AreEqual(LetteraOutlineKind.NotOutline, buffer.Paths[0].Kind);
                else Assert.Greater(buffer.Paths.Length, 1, "several layers");
            }
            else
            {
                Assert.AreEqual(LetteraOutlineKind.NotOutline, buffer.Paths[0].Kind);
            }
            var plain = new LetteraOutlineBuffer();
            text.GetGlyphOutlines(plain, new LetteraOutlineOptions { NoColorLayers = true });
            foreach (LetteraGlyphPath path in plain.Paths) Assert.AreNotEqual(LetteraOutlineKind.ColorLayer, path.Kind);
        }

        [Test]
        public void ASecondReadOfTheSameTextAllocatesNothing()
        {
            LetteraWorldText text = Make("The quick brown fox jumps over the lazy dog", size: 30);
            var buffer = new LetteraOutlineBuffer();
            var options = new LetteraOutlineOptions { Tolerance = 0.2f };
            text.GetGlyphOutlines(buffer, options);
            text.GetGlyphOutlines(buffer, options);
            int paths = buffer.Paths.Length;
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 50; i++) text.GetGlyphOutlines(buffer, options);
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.AreEqual(0, allocated);
            Assert.AreEqual(paths, buffer.Paths.Length);

            before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 50; i++) text.GetGlyphOutlines(buffer);
            Assert.AreEqual(0, GC.GetAllocatedBytesForCurrentThread() - before, "curves kept");
        }

        [Test]
        public void ACanvasTextAndAWorldTextGiveTheSamePaths()
        {
            var go = new GameObject("Canvas Text", typeof(RectTransform));
            Created.Add(go);
            go.AddComponent<Canvas>();
            var inner = new GameObject("Label", typeof(RectTransform));
            Created.Add(inner);
            inner.transform.SetParent(go.transform, false);
            ((RectTransform)inner.transform).sizeDelta = new Vector2(1000, 500);
            var label = inner.AddComponent<LetteraText>();
            label.Font = Latin;
            label.FontSize = 36;
            label.Text = "Canvas";
            var buffer = new LetteraOutlineBuffer();
            Assert.AreEqual(6, label.GetGlyphOutlines(buffer));
            LetteraWorldText world = Make("Canvas", size: 36);
            var worldBuffer = new LetteraOutlineBuffer();
            world.GetGlyphOutlines(worldBuffer);
            for (int i = 0; i < 6; i++) AssertRect(worldBuffer.Paths[i].Bounds, buffer.Paths[i].Bounds, 0.01f, "glyph " + i);
        }

        [Test]
        public void ABufferIsClearedByEveryRead()
        {
            var buffer = new LetteraOutlineBuffer();
            LetteraWorldText text = Make("abc", size: 30);
            text.GetGlyphOutlines(buffer);
            int commands = buffer.Commands.Length;
            text.GetGlyphOutlines(buffer);
            Assert.AreEqual(commands, buffer.Commands.Length);
            Assert.AreEqual(3, buffer.Paths.Length);
            buffer.Clear();
            Assert.AreEqual(0, buffer.Paths.Length);
            Assert.AreEqual(0, buffer.Points.Length);
        }
    }
}

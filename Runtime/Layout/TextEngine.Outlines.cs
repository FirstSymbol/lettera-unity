using System;
using UnityEngine;

namespace FirstSymbol.Lettera
{
    // Reads the placed glyphs back out as paths. Nothing here runs unless a host asks, and it draws nothing.
    internal sealed partial class TextEngine
    {
        const int MaxCurveSteps = 64;

        static float[] s_OutlineCommands = new float[1024];
        static uint[] s_LayerGlyphs = new uint[256], s_LayerColors = new uint[256];

        struct Matrix2D
        {
            public float A, B, C, D, Tx, Ty;

            public Vector2 Map(float x, float y) => new Vector2(A * x + C * y + Tx, B * x + D * y + Ty);

            // This matrix first, then the pose.
            public Matrix2D Then(in LetteraGlyphPose p) => new Matrix2D
            {
                A = p.A * A + p.C * B, B = p.B * A + p.D * B,
                C = p.A * C + p.C * D, D = p.B * C + p.D * D,
                Tx = p.A * Tx + p.C * Ty + p.Tx, Ty = p.B * Tx + p.D * Ty + p.Ty,
            };
        }

        /// <summary>The glyphs as paths in the text's local space. Returns how many paths there are.</summary>
        internal int CollectOutlines(LetteraOutlineBuffer into, in LetteraOutlineOptions options)
        {
            into.Clear();
            Exact();
            float tolerance = options.Tolerance > 0 ? options.Tolerance : 0;
            for (int i = 0; i < m_PlacedCount; i++)
            {
                ref PlacedGlyph g = ref m_Placed[i];
                LetteraGlyphPose pose = options.IgnoreMotion ? LetteraGlyphPose.Identity : PoseOfGlyph(i);
                float opacity = Mathf.Clamp01(pose.Alpha * pose.Tint.a);
                if (opacity <= 0) continue;

                float cos = 1, sin = 0;
                if (g.Angle != 0)
                {
                    cos = Mathf.Cos(g.Angle);
                    sin = Mathf.Sin(g.Angle);
                }
                bool rule = g.Special == SpecialRule;
                float sizeX = g.Unit * (1 + g.XScale), sizeY = g.Unit;
                if (rule)
                {
                    MathSpecial special = m_Aside.MathSpecials[g.SpecialIndex];
                    float length = special.Vertical ? special.Thickness : special.Length, thickness = special.Vertical ? special.Length : special.Thickness;
                    sizeX = g.Unit * length;
                    sizeY = g.Unit * thickness;
                }
                var matrix = new Matrix2D { A = cos * sizeX, B = sin * sizeX, C = -sin * sizeY, D = cos * sizeY, Tx = g.X, Ty = g.Y }.Then(pose);

                Color32 fill = SolidColorOf(i);
                Color tint = pose.Tint;
                if (tint != Color.white)
                {
                    fill.r = (byte)(fill.r * Mathf.Clamp01(tint.r));
                    fill.g = (byte)(fill.g * Mathf.Clamp01(tint.g));
                    fill.b = (byte)(fill.b * Mathf.Clamp01(tint.b));
                }

                var path = new LetteraGlyphPath
                {
                    GlyphIndex = i, Glyph = g.Glyph, Cluster = g.Cluster, Line = g.Line, Layer = -1, Fill = fill, Opacity = opacity, FontSize = g.Size,
                    Kind = LetteraOutlineKind.Outline,
                };
                FindStroke(g.Cluster, g.Size, ref path);

                if (rule)
                {
                    AddRule(into, ref path, matrix);
                    continue;
                }
                FontInstance instance = g.Instance;
                if (instance == null || (i < m_RealCount && IsInlineCp(g.Cluster)))
                {
                    AddNotOutline(into, ref path, matrix, in g, null);
                    continue;
                }
                FontFace face = instance.Face;
                if (face.IsVirtual || (face.HasColorPaint && LetteraNative.lt_glyph_has_paint(face.Face, g.Glyph) != 0))
                {
                    AddNotOutline(into, ref path, matrix, in g, face);
                    continue;
                }
                if (face.HasColorLayers && !options.NoColorLayers && AddColorLayers(into, path, instance, matrix, tolerance, fill)) continue;

                int count = ReadOutline(instance, g.Glyph);
                if (count <= 0)
                {
                    if (face.HasColorBitmaps) AddNotOutline(into, ref path, matrix, in g, face);
                    continue;
                }
                AddPathFrom(into, ref path, count, matrix, tolerance);
            }
            return into.PathCount;
        }

        void FindStroke(int cluster, float size, ref LetteraGlyphPath path)
        {
            ValueList<LayerRecord> layers = m_Attributes.Layers;
            for (int r = 0; r < layers.Count; r++)
            {
                LayerRecord record = layers[r];
                if (record.Kind != LayerKind.Stroke || cluster < record.Start || cluster >= record.End) continue;
                path.Stroke = record.Paint.Color;
                path.StrokeWidth = record.Width.Of(size);
                path.StrokeAlign = record.Align;
                return;
            }
        }

        static unsafe int ReadOutline(FontInstance instance, uint glyph)
        {
            int count;
            fixed (float* p = s_OutlineCommands) count = LetteraNative.lt_glyph_outline(instance.Font, glyph, p, s_OutlineCommands.Length);
            if (count > s_OutlineCommands.Length)
            {
                if (count > 16 * 1024 * 1024) return 0;
                s_OutlineCommands = new float[count + 1024];
                fixed (float* p = s_OutlineCommands) count = LetteraNative.lt_glyph_outline(instance.Font, glyph, p, s_OutlineCommands.Length);
            }
            return count;
        }

        static unsafe bool AddColorLayers(LetteraOutlineBuffer into, LetteraGlyphPath path, FontInstance instance, in Matrix2D matrix, float tolerance, Color32 textColor)
        {
            FontFace face = instance.Face;
            int total = LetteraNative.lt_glyph_color_layers(face.Face, path.Glyph, null, null, 0);
            if (total <= 0) return false;
            int layers = Math.Min(total, s_LayerGlyphs.Length);
            fixed (uint* glyphs = s_LayerGlyphs)
            fixed (uint* colors = s_LayerColors)
                LetteraNative.lt_glyph_color_layers(face.Face, path.Glyph, glyphs, colors, layers);
            uint[] palette = face.Palette;
            for (int layer = 0; layer < layers; layer++)
            {
                int count = ReadOutline(instance, s_LayerGlyphs[layer]);
                if (count <= 0) continue;
                uint index = s_LayerColors[layer];
                LetteraGlyphPath own = path;
                own.Kind = LetteraOutlineKind.ColorLayer;
                own.Layer = layer;
                own.Glyph = s_LayerGlyphs[layer];
                if (index == 0xFFFF) own.Fill = textColor;
                else
                {
                    uint argb = index < (uint)palette.Length ? palette[index] : 0xFF000000u;
                    own.Fill = new Color32((byte)(argb >> 16), (byte)(argb >> 8), (byte)argb, (byte)(argb >> 24));
                }
                AddPathFrom(into, ref own, count, matrix, tolerance);
            }
            return true;
        }

        static void AddRule(LetteraOutlineBuffer into, ref LetteraGlyphPath path, in Matrix2D matrix)
        {
            path.CommandStart = into.CommandCount;
            path.PointStart = into.PointCount;
            Vector2 a = matrix.Map(0, 0), b = matrix.Map(1, 0), c = matrix.Map(1, 1), d = matrix.Map(0, 1);
            into.AddCommand(LetteraPathCommand.MoveTo);
            into.AddPoint(a);
            into.AddCommand(LetteraPathCommand.LineTo);
            into.AddPoint(b);
            into.AddCommand(LetteraPathCommand.LineTo);
            into.AddPoint(c);
            into.AddCommand(LetteraPathCommand.LineTo);
            into.AddPoint(d);
            into.AddCommand(LetteraPathCommand.Close);
            path.CommandCount = into.CommandCount - path.CommandStart;
            path.PointCount = into.PointCount - path.PointStart;
            path.Bounds = BoxOf(a, b, c, d);
            into.AddPath(path);
        }

        void AddNotOutline(LetteraOutlineBuffer into, ref LetteraGlyphPath path, in Matrix2D matrix, in PlacedGlyph g, FontFace face)
        {
            // The cell the glyph fills, from the font's lines; an inline object has no font, so its em.
            float low = 0, high = g.Size;
            if (face != null && g.Unit > 0)
            {
                low = face.Metrics.Descender * g.Unit;
                high = face.Metrics.Ascender * g.Unit;
            }
            // The matrix maps font units, so the box is taken in them.
            float unit = g.Unit > 0 ? g.Unit : 1;
            float width = g.Advance / (unit * (1 + g.XScale)), bottom = low / unit, top = high / unit;
            Matrix2D cell = matrix;
            path.Kind = LetteraOutlineKind.NotOutline;
            path.CommandStart = into.CommandCount;
            path.PointStart = into.PointCount;
            path.Bounds = BoxOf(cell.Map(0, bottom), cell.Map(width, bottom), cell.Map(width, top), cell.Map(0, top));
            into.AddPath(path);
            into.SkippedGlyphs++;
        }

        static Rect BoxOf(Vector2 a, Vector2 b, Vector2 c, Vector2 d)
        {
            float x0 = Mathf.Min(Mathf.Min(a.x, b.x), Mathf.Min(c.x, d.x)), x1 = Mathf.Max(Mathf.Max(a.x, b.x), Mathf.Max(c.x, d.x));
            float y0 = Mathf.Min(Mathf.Min(a.y, b.y), Mathf.Min(c.y, d.y)), y1 = Mathf.Max(Mathf.Max(a.y, b.y), Mathf.Max(c.y, d.y));
            return new Rect(x0, y0, x1 - x0, y1 - y0);
        }

        // Turns the font's command stream in s_OutlineCommands into one path through the matrix.
        static void AddPathFrom(LetteraOutlineBuffer into, ref LetteraGlyphPath path, int count, in Matrix2D matrix, float tolerance)
        {
            path.CommandStart = into.CommandCount;
            path.PointStart = into.PointCount;
            float[] s = s_OutlineCommands;
            bool open = false;
            Vector2 start = default, pen = default;
            float lowX = float.MaxValue, lowY = float.MaxValue, highX = float.MinValue, highY = float.MinValue;
            int i = 0;
            while (i < count)
            {
                int command = (int)s[i];
                if (command == 0 && i + 2 < count)
                {
                    if (open) into.AddCommand(LetteraPathCommand.Close);
                    pen = start = matrix.Map(s[i + 1], s[i + 2]);
                    into.AddCommand(LetteraPathCommand.MoveTo);
                    Put(into, pen, ref lowX, ref lowY, ref highX, ref highY);
                    open = true;
                    i += 3;
                }
                else if (command == 1 && i + 2 < count)
                {
                    pen = matrix.Map(s[i + 1], s[i + 2]);
                    into.AddCommand(LetteraPathCommand.LineTo);
                    Put(into, pen, ref lowX, ref lowY, ref highX, ref highY);
                    i += 3;
                }
                else if (command == 2 && i + 4 < count)
                {
                    Vector2 control = matrix.Map(s[i + 1], s[i + 2]), to = matrix.Map(s[i + 3], s[i + 4]);
                    if (tolerance <= 0)
                    {
                        into.AddCommand(LetteraPathCommand.QuadTo);
                        Put(into, control, ref lowX, ref lowY, ref highX, ref highY);
                        Put(into, to, ref lowX, ref lowY, ref highX, ref highY);
                    }
                    else
                    {
                        Vector2 bend = pen - 2 * control + to;
                        int steps = Mathf.Clamp(Mathf.CeilToInt(Mathf.Sqrt(0.25f * bend.magnitude / tolerance)), 1, MaxCurveSteps);
                        for (int k = 1; k <= steps; k++)
                        {
                            float t = (float)k / steps, u = 1 - t;
                            into.AddCommand(LetteraPathCommand.LineTo);
                            Put(into, u * u * pen + 2 * u * t * control + t * t * to, ref lowX, ref lowY, ref highX, ref highY);
                        }
                    }
                    pen = to;
                    i += 5;
                }
                else if (command == 3 && i + 6 < count)
                {
                    Vector2 first = matrix.Map(s[i + 1], s[i + 2]), second = matrix.Map(s[i + 3], s[i + 4]), to = matrix.Map(s[i + 5], s[i + 6]);
                    if (tolerance <= 0)
                    {
                        into.AddCommand(LetteraPathCommand.CubicTo);
                        Put(into, first, ref lowX, ref lowY, ref highX, ref highY);
                        Put(into, second, ref lowX, ref lowY, ref highX, ref highY);
                        Put(into, to, ref lowX, ref lowY, ref highX, ref highY);
                    }
                    else
                    {
                        float bend = Mathf.Max((pen - 2 * first + second).magnitude, (first - 2 * second + to).magnitude);
                        int steps = Mathf.Clamp(Mathf.CeilToInt(Mathf.Sqrt(0.75f * bend / tolerance)), 1, MaxCurveSteps);
                        for (int k = 1; k <= steps; k++)
                        {
                            float t = (float)k / steps, u = 1 - t;
                            into.AddCommand(LetteraPathCommand.LineTo);
                            Put(into, u * u * u * pen + 3 * u * u * t * first + 3 * u * t * t * second + t * t * t * to, ref lowX, ref lowY, ref highX, ref highY);
                        }
                    }
                    pen = to;
                    i += 7;
                }
                else if (command == 4)
                {
                    if (open) into.AddCommand(LetteraPathCommand.Close);
                    open = false;
                    pen = start;
                    i += 1;
                }
                else
                {
                    break;
                }
            }
            if (open) into.AddCommand(LetteraPathCommand.Close);
            path.CommandCount = into.CommandCount - path.CommandStart;
            path.PointCount = into.PointCount - path.PointStart;
            if (path.CommandCount == 0)
            {
                return;
            }
            path.Bounds = new Rect(lowX, lowY, highX - lowX, highY - lowY);
            into.AddPath(path);
        }

        static void Put(LetteraOutlineBuffer into, Vector2 point, ref float lowX, ref float lowY, ref float highX, ref float highY)
        {
            into.AddPoint(point);
            if (point.x < lowX) lowX = point.x;
            if (point.y < lowY) lowY = point.y;
            if (point.x > highX) highX = point.x;
            if (point.y > highY) highY = point.y;
        }
    }
}

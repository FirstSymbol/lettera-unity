using System;
using UnityEngine;

namespace FirstSymbol.Lettera
{
    /// <summary>One step of a glyph path. The points a step takes follow it in <see cref="LetteraOutlineBuffer.Points"/>.</summary>
    public enum LetteraPathCommand : byte
    {
        /// <summary>Starts a contour at one point.</summary>
        MoveTo = 0,

        /// <summary>A straight line to one point.</summary>
        LineTo = 1,

        /// <summary>A quadratic Bezier curve: a control point, then the end point.</summary>
        QuadTo = 2,

        /// <summary>A cubic Bezier curve: two control points, then the end point.</summary>
        CubicTo = 3,

        /// <summary>Closes the contour with a line back to where it started. Takes no point.</summary>
        Close = 4,
    }

    /// <summary>What a <see cref="LetteraGlyphPath"/> holds.</summary>
    public enum LetteraOutlineKind : byte
    {
        /// <summary>The glyph's outline, filled with the text's colour.</summary>
        Outline,

        /// <summary>One layer of a colour glyph (a COLR v0 font such as an emoji), filled with that layer's colour.</summary>
        ColorLayer,

        /// <summary>A glyph with no outline to give: a bitmap or painted colour glyph, a browser emoji or an inline object. The path has no commands and <see cref="LetteraGlyphPath.Bounds"/> is the cell the glyph fills.</summary>
        NotOutline,
    }

    /// <summary>One filled path of one glyph, in <see cref="LetteraOutlineBuffer"/>. Fill with the non-zero winding rule.</summary>
    public struct LetteraGlyphPath
    {
        /// <summary>The glyph's index, the one <c>GetGlyph</c> takes. Glyphs a formula adds come after <c>GlyphCount</c>.</summary>
        public int GlyphIndex;

        /// <summary>The glyph's id in its font.</summary>
        public uint Glyph;

        /// <summary>The first character of the glyph's cluster in the shown text.</summary>
        public int Cluster;

        /// <summary>The line the glyph is on.</summary>
        public int Line;

        public LetteraOutlineKind Kind;

        /// <summary>The colour layer of a colour glyph, from 0; -1 for everything else.</summary>
        public int Layer;

        /// <summary>The fill colour: the text's, with the range's colour and the glyph's tint on it. A colour layer takes its own, and the text's when the font leaves it to the text. Gradients and textures are not carried; this is their base colour.</summary>
        public Color32 Fill;

        /// <summary>How opaque the glyph is now, 0 to 1: its fade, as an effect has it. A glyph that is wholly hidden is left out.</summary>
        public float Opacity;

        /// <summary>The stroke colour, when <see cref="StrokeWidth"/> is above 0.</summary>
        public Color32 Stroke;

        /// <summary>The width of the stroke the text has on this glyph, in local units; 0 for none.</summary>
        public float StrokeWidth;

        /// <summary>Where the stroke sits: 1 wholly outside the outline, 0 centred on it, -1 wholly inside it.</summary>
        public float StrokeAlign;

        /// <summary>The glyph's size in local units to the em.</summary>
        public float FontSize;

        /// <summary>The box of the path's points, control points included, in local space.</summary>
        public Rect Bounds;

        /// <summary>The first of this path's commands in <see cref="LetteraOutlineBuffer.Commands"/>, and how many there are.</summary>
        public int CommandStart, CommandCount;

        /// <summary>The first of this path's points in <see cref="LetteraOutlineBuffer.Points"/>, and how many there are.</summary>
        public int PointStart, PointCount;
    }

    /// <summary>What <see cref="LetteraGlyphOutlines"/> reads. The default is curves kept, colour layers on, motion as it is now.</summary>
    public struct LetteraOutlineOptions
    {
        /// <summary>
        /// 0 keeps curves: a path then holds quadratic and cubic Beziers, as the font has them. Above 0 the paths hold lines only, and no
        /// point of a curve is farther than this from them, in local units.
        /// </summary>
        public float Tolerance;

        /// <summary>Gives a colour glyph its filled outline, in the text's colour, in place of its layers.</summary>
        public bool NoColorLayers;

        /// <summary>Places glyphs where layout puts them, before any effect moves, turns or scales them.</summary>
        public bool IgnoreMotion;
    }

    /// <summary>
    /// The paths <see cref="LetteraGlyphOutlines"/> reads, in arrays it keeps and reuses: a second read of the same size allocates nothing.
    /// </summary>
    public sealed class LetteraOutlineBuffer
    {
        LetteraGlyphPath[] m_Paths = new LetteraGlyphPath[16];
        LetteraPathCommand[] m_Commands = new LetteraPathCommand[256];
        Vector2[] m_Points = new Vector2[512];
        int m_PathCount, m_CommandCount, m_PointCount;

        /// <summary>The paths, glyph by glyph; a colour glyph has one for each layer.</summary>
        public ReadOnlySpan<LetteraGlyphPath> Paths => new ReadOnlySpan<LetteraGlyphPath>(m_Paths, 0, m_PathCount);

        /// <summary>Every path's commands, one after the other.</summary>
        public ReadOnlySpan<LetteraPathCommand> Commands => new ReadOnlySpan<LetteraPathCommand>(m_Commands, 0, m_CommandCount);

        /// <summary>Every path's points, in the local space of the text.</summary>
        public ReadOnlySpan<Vector2> Points => new ReadOnlySpan<Vector2>(m_Points, 0, m_PointCount);

        /// <summary>The commands of one path.</summary>
        public ReadOnlySpan<LetteraPathCommand> CommandsOf(in LetteraGlyphPath path) => new ReadOnlySpan<LetteraPathCommand>(m_Commands, path.CommandStart, path.CommandCount);

        /// <summary>The points of one path.</summary>
        public ReadOnlySpan<Vector2> PointsOf(in LetteraGlyphPath path) => new ReadOnlySpan<Vector2>(m_Points, path.PointStart, path.PointCount);

        /// <summary>How many glyphs had no outline to give; their paths are <see cref="LetteraOutlineKind.NotOutline"/>.</summary>
        public int SkippedGlyphs { get; internal set; }

        /// <summary>Empties the buffer. Its arrays stay.</summary>
        public void Clear()
        {
            m_PathCount = m_CommandCount = m_PointCount = 0;
            SkippedGlyphs = 0;
        }

        internal int PathCount => m_PathCount;
        internal int CommandCount => m_CommandCount;
        internal int PointCount => m_PointCount;

        internal void AddPath(in LetteraGlyphPath path)
        {
            if (m_PathCount == m_Paths.Length) Array.Resize(ref m_Paths, m_PathCount * 2);
            m_Paths[m_PathCount++] = path;
        }

        internal ref LetteraGlyphPath PathAt(int index) => ref m_Paths[index];

        internal void AddCommand(LetteraPathCommand command)
        {
            if (m_CommandCount == m_Commands.Length) Array.Resize(ref m_Commands, m_CommandCount * 2);
            m_Commands[m_CommandCount++] = command;
        }

        internal void AddPoint(Vector2 point)
        {
            if (m_PointCount == m_Points.Length) Array.Resize(ref m_Points, m_PointCount * 2);
            m_Points[m_PointCount++] = point;
        }

        internal Vector2 LastPoint => m_Points[m_PointCount - 1];
    }

    /// <summary>
    /// A laid-out text's glyph outlines as paths, for a host that draws them itself: vector graphics, a plotter, a cut file. The glyphs are
    /// where layout, line breaking, alignment and the effects of the moment put them. The paths are in the text's local space, the one
    /// <c>GetGlyph</c>, <c>InkBounds</c> and <c>LayoutBounds</c> use, with y up.
    /// </summary>
    public static class LetteraGlyphOutlines
    {
        /// <summary>Reads the outlines of a text into a buffer, which is cleared first. Rebuilds the text if it is out of date. Returns the number of paths.</summary>
        public static int GetGlyphOutlines(this LetteraText text, LetteraOutlineBuffer into, LetteraOutlineOptions options = default)
        {
            if (text == null) throw new ArgumentNullException(nameof(text));
            if (into == null) throw new ArgumentNullException(nameof(into));
            text.ForceRebuild();
            return text.Engine.CollectOutlines(into, options);
        }

        /// <summary>Reads the outlines of a world text into a buffer. See <see cref="GetGlyphOutlines(LetteraText, LetteraOutlineBuffer, LetteraOutlineOptions)"/>.</summary>
        public static int GetGlyphOutlines(this LetteraWorldText text, LetteraOutlineBuffer into, LetteraOutlineOptions options = default)
        {
            if (text == null) throw new ArgumentNullException(nameof(text));
            if (into == null) throw new ArgumentNullException(nameof(into));
            text.ForceRebuild();
            return text.Engine.CollectOutlines(into, options);
        }
    }
}

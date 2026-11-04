using System;
using System.Collections.Generic;
using UnityEngine;

namespace FirstSymbol.Lettera
{
    internal struct TableAtom
    {
        public TableRunKind Kind;
        public float Width, Ascent, Descent;
        public byte Style;
        public uint[] Glyphs;
        public float[] X, Y;
        public LetteraMathLayout Box;
        public TableModel Table;
    }

    /// <summary>A cell's words, shaped once for one font, with the narrowest and widest it can be.</summary>
    internal sealed class TablePrepared
    {
        public TableAtom[] Atoms;
        public float Min, Max;
    }

    /// <summary>
    /// Lays a table out in em of the text's size, into the same glyphs and rules a formula draws. Columns size as CSS's automatic table layout does:
    /// the widest each column's content wants when that fits, else each between its narrowest and widest in proportion, else narrower still, so a
    /// table never runs past the room it is given.
    /// </summary>
    internal sealed class LetteraTableTypesetter
    {
        // LaTeX's at a 10 pt body: \tabcolsep 6 pt, \arrayrulewidth 0.4 pt, \doublerulesep 2 pt.
        internal const float Padding = 0.6f, RuleWidth = 0.04f, DoubleGap = 0.2f, CaptionGap = 0.5f, Axis = 0.25f;

        readonly FontFace m_Text, m_Math;
        readonly FontInstance[] m_Instances = new FontInstance[4];
        readonly List<FontInstance> m_Faces = new List<FontInstance>(4);
        readonly List<Color32> m_Colors = new List<Color32>();
        readonly ValueList<LetteraMathItem> m_Items = new ValueList<LetteraMathItem>(256);
        readonly float m_Ascent, m_Descent, m_Space;
        readonly TableModel m_Root;
        readonly string m_Label;
        int m_Glyphs;

        LetteraTableTypesetter(TableModel root, FontFace text, FontFace math, string label)
        {
            m_Root = root;
            m_Text = text;
            m_Math = math;
            m_Label = label;
            FaceMetrics metrics = text.Metrics;
            float upem = Mathf.Max(metrics.Upem, 1);
            m_Ascent = metrics.Ascender / upem;
            m_Descent = -metrics.Descender / upem;
            m_Faces.Add(Instance(0));
            Shape(" ", 0, out _, out _, out _, out m_Space);
            if (m_Space <= 0) m_Space = 0.25f;
        }

        internal static int Layouts { get; private set; }

        /// <summary>A table at a room in em, with its caption numbered as given. Infinity for room lays it at its widest.</summary>
        internal static LetteraMathLayout Lay(TableModel model, float room, FontFace text, FontFace math, string label)
        {
            Layouts++;
            var typesetter = new LetteraTableTypesetter(model, text, math, label);
            Rect box = typesetter.Table(model, room, 0, 0, true, model.Depth);
            return typesetter.Finish(box, room);
        }

        FontInstance Instance(byte style)
        {
            style &= 3;
            return m_Instances[style] ?? (m_Instances[style] = style == 0 ? m_Text.Regular : m_Text.GetInstance(-1, (style & LetteraTableParser.Bold) != 0 ? 0.03f : 0, (style & LetteraTableParser.Italic) != 0 ? 0.2f : 0));
        }

        byte FaceOf(FontInstance instance)
        {
            int index = m_Faces.IndexOf(instance);
            if (index < 0)
            {
                if (m_Faces.Count >= 255) return 0;
                m_Faces.Add(instance);
                index = m_Faces.Count - 1;
            }
            return (byte)index;
        }

        unsafe void Shape(string text, byte style, out uint[] glyphs, out float[] xs, out float[] ys, out float width)
        {
            FontInstance instance = Instance(style);
            var codepoints = new int[text.Length];
            int count = 0;
            for (int i = 0; i < text.Length; i++)
            {
                int cp = text[i];
                if (char.IsHighSurrogate(text[i]) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1])) cp = char.ConvertToUtf32(text[i], text[++i]);
                codepoints[count++] = cp;
            }
            var output = new NativeGlyph[count * 2 + 8];
            int shaped = 0;
            if (count > 0 && !instance.Face.IsVirtual)
                fixed (int* t = codepoints)
                fixed (NativeGlyph* o = output)
                    shaped = LetteraNative.lt_shape(instance.Font, t, count, 0, count, 0, 0, null, null, 0, o, output.Length);
            shaped = Mathf.Clamp(shaped, 0, output.Length);
            float unit = 1f / Mathf.Max(instance.Face.Metrics.Upem, 1), x = 0;
            glyphs = new uint[shaped];
            xs = new float[shaped];
            ys = new float[shaped];
            for (int i = 0; i < shaped; i++)
            {
                glyphs[i] = output[i].Glyph;
                xs[i] = x + output[i].XOffset * unit;
                ys[i] = output[i].YOffset * unit;
                x += output[i].XAdvance * unit;
            }
            width = x;
        }

        // ---------------------------------------------------------------- cells

        TablePrepared Prepare(List<TableRun> runs, byte style, float room, int depth)
        {
            var atoms = new TableAtom[runs.Count];
            for (int i = 0; i < runs.Count; i++)
            {
                TableRun run = runs[i];
                var atom = new TableAtom { Kind = run.Kind, Style = (byte)(run.Style | style), Ascent = m_Ascent, Descent = m_Descent };
                switch (run.Kind)
                {
                    case TableRunKind.Text:
                        Shape(run.Text, atom.Style, out atom.Glyphs, out atom.X, out atom.Y, out atom.Width);
                        break;
                    case TableRunKind.Space:
                        atom.Width = run.Width > 0 ? run.Width : m_Space;
                        break;
                    case TableRunKind.Math:
#if !LETTERA_NO_MATH
                        if (m_Math != null)
                        {
                            atom.Box = LetteraMathCache.Get(run.Text ?? "", false, m_Math, m_Text);
                            atom.Width = atom.Box.Width;
                            atom.Ascent = Mathf.Max(m_Ascent, atom.Box.Ascent);
                            atom.Descent = Mathf.Max(m_Descent, atom.Box.Descent);
                        }
                        else
#endif
                        {
                            atom.Kind = TableRunKind.Text;
                            Shape(run.Text ?? "", atom.Style, out atom.Glyphs, out atom.X, out atom.Y, out atom.Width);
                        }
                        break;
                    case TableRunKind.Table:
                        atom.Table = run.Table;
                        Measure(run.Table, room, depth + 1);
                        atom.Width = run.Table.MaxWidth;
                        break;
                }
                atoms[i] = atom;
            }
            var prepared = new TablePrepared { Atoms = atoms };
            // The narrowest is the widest run that cannot break; the widest, the longest line between forced breaks.
            float segment = 0, line = 0, lead = 0;
            foreach (TableAtom atom in atoms)
            {
                switch (atom.Kind)
                {
                    case TableRunKind.Space:
                        segment = 0;
                        lead += atom.Width;
                        break;
                    case TableRunKind.Break:
                        segment = 0;
                        line = 0;
                        lead = 0;
                        break;
                    default:
                        float min = atom.Kind == TableRunKind.Table ? atom.Table.MinWidth : atom.Width;
                        segment += min;
                        prepared.Min = Mathf.Max(prepared.Min, segment);
                        line += (line > 0 ? lead : 0) + atom.Width;
                        lead = 0;
                        prepared.Max = Mathf.Max(prepared.Max, line);
                        break;
                }
            }
            return prepared;
        }

        struct Piece
        {
            public int Atom, From, To;
            public float X, Scale;
            public LetteraMathLayout Box;
        }

        sealed class Line
        {
            public readonly List<Piece> Pieces = new List<Piece>();
            public float Width, Ascent, Descent;
        }

        // Words fill lines greedily up to the width, and a word wider than it breaks between its glyphs: nothing runs past.
        List<Line> Wrap(TablePrepared prepared, float width)
        {
            var lines = new List<Line>();
            var line = new Line { Ascent = m_Ascent, Descent = m_Descent };
            float x = 0, space = 0;
            TableAtom[] atoms = prepared.Atoms;
            void Finish()
            {
                line.Width = x;
                lines.Add(line);
                line = new Line { Ascent = m_Ascent, Descent = m_Descent };
                x = 0;
                space = 0;
            }
            void Place(int index, int from, int to, float w, float scale, LetteraMathLayout box = null)
            {
                ref TableAtom atom = ref atoms[index];
                line.Pieces.Add(new Piece { Atom = index, From = from, To = to, X = x, Scale = scale, Box = box });
                line.Ascent = Mathf.Max(line.Ascent, box != null ? box.Ascent : atom.Ascent * scale);
                line.Descent = Mathf.Max(line.Descent, box != null ? box.Descent : atom.Descent * scale);
                x += w;
            }
            int i = 0;
            while (i < atoms.Length)
            {
                TableAtom atom = atoms[i];
                if (atom.Kind == TableRunKind.Break)
                {
                    Finish();
                    i++;
                    continue;
                }
                if (atom.Kind == TableRunKind.Space)
                {
                    if (x > 0) space += atom.Width;
                    i++;
                    continue;
                }
                int end = i;
                float segment = 0;
                while (end < atoms.Length && atoms[end].Kind != TableRunKind.Space && atoms[end].Kind != TableRunKind.Break)
                {
                    segment += atoms[end].Kind == TableRunKind.Table ? Mathf.Min(atoms[end].Table.MaxWidth, width) : atoms[end].Width;
                    end++;
                }
                if (x > 0 && x + space + segment > width + 0.0001f) Finish();
                x += space;
                space = 0;
                for (int k = i; k < end; k++)
                {
                    TableAtom part = atoms[k];
                    if (part.Kind == TableRunKind.Table)
                    {
                        float w = Mathf.Min(part.Table.MaxWidth, width);
                        if (x > 0 && x + w > width + 0.0001f) Finish();
                        w = Mathf.Max(Mathf.Min(part.Table.MaxWidth, width - x), 0.01f);
                        LetteraMathLayout nested = Lay(part.Table, w, m_Text, m_Math, null);
                        if (part.Table.Warning != null) m_Root.Warn(part.Table.Warning);
                        Place(k, 0, 0, nested.Width, 1, nested);
                        continue;
                    }
                    if (part.Kind == TableRunKind.Math)
                    {
                        if (x > 0 && x + part.Width > width + 0.0001f) Finish();
                        float scale = part.Width > width && part.Width > 0 ? Mathf.Max(width, 0.01f) / part.Width : 1;
                        Place(k, 0, 0, part.Width * scale, scale);
                        continue;
                    }
                    if (part.Glyphs == null || part.Glyphs.Length == 0) continue;
                    if (x + part.Width <= width + 0.0001f)
                    {
                        Place(k, 0, part.Glyphs.Length, part.Width, 1);
                        continue;
                    }
                    // Too wide for what is left: as many glyphs as fit, then a new line, at least one glyph each.
                    int g = 0;
                    while (g < part.Glyphs.Length)
                    {
                        int stop = g + 1;
                        while (stop < part.Glyphs.Length && Advance(part, g, stop + 1) <= width - x + 0.0001f) stop++;
                        if (x > 0 && Advance(part, g, stop) > width - x + 0.0001f)
                        {
                            Finish();
                            continue;
                        }
                        Place(k, g, stop, Advance(part, g, stop), 1);
                        g = stop;
                        if (g < part.Glyphs.Length) Finish();
                    }
                }
                i = end;
            }
            Finish();
            return lines;
        }

        // From one glyph's pen to another's: the last's advance is the word's end.
        static float Advance(in TableAtom atom, int from, int to)
        {
            float end = to < atom.X.Length ? atom.X[to] : atom.Width;
            return end - atom.X[from];
        }

        float Height(List<Line> lines)
        {
            float height = 0;
            foreach (Line line in lines) height += line.Ascent + line.Descent;
            return height;
        }

        // Lines at a top-left corner, each aligned in a width. The first line's baseline lies its ascent under the top.
        void Draw(TablePrepared prepared, List<Line> lines, float left, float top, float width, char align)
        {
            float y = top;
            foreach (Line line in lines)
            {
                y -= line.Ascent;
                float x = left + (align == 'c' ? (width - line.Width) / 2 : align == 'r' ? width - line.Width : 0);
                foreach (Piece piece in line.Pieces)
                {
                    TableAtom atom = prepared.Atoms[piece.Atom];
                    switch (atom.Kind)
                    {
                        case TableRunKind.Text:
                        {
                            byte face = FaceOf(Instance(atom.Style));
                            float origin = atom.X[piece.From];
                            for (int g = piece.From; g < piece.To; g++)
                            {
                                if (++m_Glyphs > LetteraTableLimits.Glyphs)
                                {
                                    m_Root.Warn("A table that draws more than " + LetteraTableLimits.Glyphs + " glyphs is cut there.");
                                    break;
                                }
                                m_Items.Add(new LetteraMathItem { Kind = MathItemKind.Glyph, Face = face, Glyph = atom.Glyphs[g], X = x + piece.X + atom.X[g] - origin, Y = y + atom.Y[g], Scale = 1 });
                            }
                            break;
                        }
                        case TableRunKind.Math:
                            Copy(atom.Box, x + piece.X, y, piece.Scale);
                            break;
                        case TableRunKind.Table:
                            Copy(piece.Box, x + piece.X, y, 1);
                            break;
                    }
                }
                y -= line.Descent;
            }
        }

        // A formula's items, moved and scaled, its faces and colors taken into this table's.
        void Copy(LetteraMathLayout box, float x, float y, float scale)
        {
            if (box == null) return;
            foreach (LetteraMathItem source in box.Items)
            {
                LetteraMathItem item = source;
                item.X = x + item.X * scale;
                item.Y = y + item.Y * scale;
                item.Scale *= scale;
                item.Length *= scale;
                item.Thickness *= scale;
                if (item.Kind == MathItemKind.Glyph && box.Faces != null && item.Face < box.Faces.Length) item.Face = FaceOf(box.Faces[item.Face]);
                if (item.Color > 0 && box.Colors != null && item.Color <= box.Colors.Length)
                {
                    m_Colors.Add(box.Colors[item.Color - 1]);
                    item.Color = m_Colors.Count;
                }
                m_Items.Add(item);
            }
        }

        void Rule(float x, float y, float length, float thickness, bool vertical)
        {
            if (length <= 0 || thickness <= 0) return;
            m_Items.Add(new LetteraMathItem { Kind = MathItemKind.Rule, X = x, Y = y, Length = length, Thickness = thickness, Vertical = vertical, Scale = 1 });
        }

        // ---------------------------------------------------------------- measuring

        sealed class Grid
        {
            public int Columns;
            public TableColumn[] Specs;
            public float[] Min, Max;
        }

        Grid MakeGrid(TableModel model)
        {
            int columns = model.Columns.Count;
            foreach (TableRow row in model.Rows)
                foreach (TableCell cell in row.Cells)
                    columns = Mathf.Max(columns, Mathf.Min(cell.Column + cell.Span, LetteraTableLimits.Columns));
            columns = Mathf.Max(columns, 1);
            var grid = new Grid { Columns = columns, Specs = new TableColumn[columns], Min = new float[columns], Max = new float[columns] };
            for (int c = 0; c < columns; c++) grid.Specs[c] = c < model.Columns.Count ? model.Columns[c] : new TableColumn();
            return grid;
        }

        // What stands between and around the columns, outside their content: padding, rules and separators.
        float[] Gaps(TableModel model, Grid grid, out float total)
        {
            int n = grid.Columns;
            var gaps = new float[n + 1];
            for (int b = 0; b <= n; b++)
            {
                int rules = b < n ? grid.Specs[b].RulesBefore : model.RulesAfter;
                string text = b < n ? grid.Specs[b].Before : model.AfterLast;
                float gap = rules > 0 ? rules * RuleWidth + (rules - 1) * DoubleGap : 0;
                if (!string.IsNullOrEmpty(text))
                {
                    Shape(text, 0, out _, out _, out _, out float w);
                    gap += w;
                }
                if (b > 0 && grid.Specs[b - 1].PadAfter && (b < n || model.PadAfterLast)) gap += Padding;
                if (b < n && grid.Specs[b].PadBefore) gap += Padding;
                gaps[b] = gap;
            }
            total = 0;
            foreach (float gap in gaps) total += gap;
            return gaps;
        }

        float Fixed(TableColumn column, float room) => column.Width < 0 ? (float.IsInfinity(room) ? 0 : -column.Width * room) : column.Width;

        void Measure(TableModel model, float room, int depth)
        {
            Grid grid = MakeGrid(model);
            Gaps(model, grid, out float gaps);
            Prepare(model, grid, room, depth);
            ColumnLimits(model, grid, room);
            float min = gaps, max = gaps;
            for (int c = 0; c < grid.Columns; c++)
            {
                min += grid.Min[c];
                max += grid.Max[c];
            }
            model.MinWidth = min;
            model.MaxWidth = max;
        }

        void Prepare(TableModel model, Grid grid, float room, int depth)
        {
            int id = m_Text.Regular.Id;
            if (model.PreparedFor == id) return;
            foreach (TableRow row in model.Rows)
                foreach (TableCell cell in row.Cells)
                {
                    byte style = cell.Column < grid.Columns ? grid.Specs[cell.Column].Style : (byte)0;
                    cell.Prepared = Prepare(cell.Runs, style, room, depth);
                }
            model.PreparedFor = id;
        }

        // Each column's narrowest and widest, from its own cells first, then grown for cells that span it.
        void ColumnLimits(TableModel model, Grid grid, float room)
        {
            int n = grid.Columns;
            for (int c = 0; c < n; c++)
            {
                grid.Min[c] = 0;
                grid.Max[c] = 0;
            }
            float[] gaps = Gaps(model, grid, out _);
            List<TableCell> spanning = null;
            foreach (TableRow row in model.Rows)
                foreach (TableCell cell in row.Cells)
                {
                    if (cell.Column >= n) continue;
                    if (cell.Span > 1)
                    {
                        (spanning ?? (spanning = new List<TableCell>())).Add(cell);
                        continue;
                    }
                    grid.Min[cell.Column] = Mathf.Max(grid.Min[cell.Column], cell.Prepared.Min);
                    grid.Max[cell.Column] = Mathf.Max(grid.Max[cell.Column], cell.Prepared.Max);
                }
            for (int c = 0; c < n; c++)
            {
                TableColumn spec = grid.Specs[c];
                if (spec.Kind == 1)
                {
                    float w = Mathf.Min(Fixed(spec, room), LetteraTableLimits.LengthEm);
                    grid.Min[c] = grid.Max[c] = w;
                }
                grid.Max[c] = Mathf.Max(grid.Max[c], grid.Min[c]);
            }
            if (spanning == null) return;
            spanning.Sort((a, b) => a.Span.CompareTo(b.Span));
            foreach (TableCell cell in spanning)
            {
                int last = Mathf.Min(cell.Column + cell.Span, n);
                float inner = 0, mins = 0, maxs = 0;
                for (int c = cell.Column; c < last; c++)
                {
                    mins += grid.Min[c];
                    maxs += grid.Max[c];
                    if (c > cell.Column) inner += gaps[c];
                }
                Spread(grid.Min, grid.Max, cell.Column, last, cell.Prepared.Min - inner - mins, maxs);
                maxs = 0;
                for (int c = cell.Column; c < last; c++) maxs += grid.Max[c];
                Spread(grid.Max, grid.Max, cell.Column, last, cell.Prepared.Max - inner - maxs, maxs);
            }
        }

        static void Spread(float[] target, float[] weights, int from, int to, float extra, float weightSum)
        {
            if (extra <= 0) return;
            int count = to - from;
            for (int c = from; c < to; c++)
            {
                float share = weightSum > 0 ? weights[c] / weightSum : 1f / count;
                target[c] += extra * share;
            }
        }

        // ---------------------------------------------------------------- widths

        float[] Widths(TableModel model, Grid grid, float room, float gaps, out float fillPerGap)
        {
            int n = grid.Columns;
            fillPerGap = 0;
            float target = float.NaN;
            if (model.TargetFactor >= 0) target = (float.IsInfinity(room) ? 0 : model.TargetFactor * room) + model.TargetEm;
            else if (model.TargetEm > 0) target = model.TargetEm;
            if (!float.IsNaN(target) && (target <= 0 || float.IsInfinity(target))) target = float.NaN;
            float limit = float.IsNaN(target) ? room : Mathf.Min(target, room);
            float available = Mathf.Max(limit - gaps, 0);
            float[] widths = new float[n];
            float sumMin = 0, sumMax = 0;
            bool flexible = false, proportional = false;
            for (int c = 0; c < n; c++)
            {
                sumMin += grid.Min[c];
                sumMax += grid.Max[c];
                flexible |= grid.Specs[c].Kind == 2;
                proportional |= grid.Specs[c].Kind == 3;
            }
            if (!float.IsNaN(target) && flexible && !float.IsInfinity(available))
            {
                // tabularx: the other columns as they would be, and X columns share what is left, equally, none under its narrowest.
                float others = 0, flexMin = 0;
                int flexCount = 0;
                for (int c = 0; c < n; c++)
                {
                    if (grid.Specs[c].Kind == 2)
                    {
                        flexCount++;
                        flexMin += grid.Min[c];
                    }
                    else others += grid.Max[c];
                }
                if (others + flexMin <= available)
                {
                    float share = (available - others) / flexCount;
                    float spare = available - others;
                    // X columns narrower than their content's narrowest keep it, and the rest share what remains.
                    int left = flexCount;
                    for (int c = 0; c < n; c++)
                    {
                        if (grid.Specs[c].Kind != 2)
                        {
                            widths[c] = grid.Max[c];
                            continue;
                        }
                        widths[c] = Mathf.Max(grid.Min[c], share);
                    }
                    float used = 0;
                    for (int c = 0; c < n; c++)
                        if (grid.Specs[c].Kind == 2)
                            used += widths[c];
                    if (used > spare + 0.001f)
                    {
                        float over = used - spare;
                        float give = 0;
                        for (int c = 0; c < n; c++)
                            if (grid.Specs[c].Kind == 2)
                                give += widths[c] - grid.Min[c];
                        for (int c = 0; c < n; c++)
                            if (grid.Specs[c].Kind == 2 && give > 0)
                                widths[c] -= over * (widths[c] - grid.Min[c]) / give;
                    }
                    _ = left;
                    return widths;
                }
            }
            if (!float.IsNaN(target) && proportional && sumMax > available && !float.IsInfinity(available))
            {
                // tabulary: in proportion to what each would like, none under its narrowest.
                float sum = 0;
                for (int c = 0; c < n; c++) sum += widths[c] = Mathf.Max(grid.Min[c], available * (sumMax > 0 ? grid.Max[c] / sumMax : 1f / n));
                if (sum <= available + 0.001f) return widths;
            }
            if (float.IsInfinity(available) || sumMax <= available)
            {
                for (int c = 0; c < n; c++) widths[c] = grid.Max[c];
                if (model.Fill && !float.IsNaN(target) && n > 1) fillPerGap = Mathf.Max(0, available - sumMax) / (n - 1);
                return widths;
            }
            if (sumMin <= available)
            {
                float t = sumMax - sumMin > 0 ? (available - sumMin) / (sumMax - sumMin) : 0;
                for (int c = 0; c < n; c++) widths[c] = grid.Min[c] + (grid.Max[c] - grid.Min[c]) * t;
                return widths;
            }
            // Narrower than every column's narrowest: each gives in proportion, and words break.
            float factor = sumMin > 0 ? available / sumMin : 0;
            for (int c = 0; c < n; c++) widths[c] = grid.Min[c] * factor;
            return widths;
        }

        // ---------------------------------------------------------------- the table

        // Lays a table with its top-left corner at a point and returns its rectangle. The outermost decides its own baseline afterwards.
        Rect Table(TableModel model, float room, float left, float top, bool outermost, int depth)
        {
            if (float.IsNaN(room) || room < 0) room = 0;
            Grid grid = MakeGrid(model);
            Prepare(model, grid, room, depth);
            ColumnLimits(model, grid, room);
            float[] gaps = Gaps(model, grid, out float gapSum);
            float[] widths = Widths(model, grid, room, gapSum, out float fill);
            int n = grid.Columns;

            // Across: where each column's cell area and content begin, and where the rules of each boundary stand.
            var cellLeft = new float[n + 1];
            var contentLeft = new float[n];
            var boundaryX = new float[n + 1];
            float x = 0;
            for (int b = 0; b <= n; b++)
            {
                if (b > 0 && grid.Specs[b - 1].PadAfter && (b < n || model.PadAfterLast)) x += Padding;
                if (b > 0 && b < n) x += fill;
                boundaryX[b] = x;
                int rules = b < n ? grid.Specs[b].RulesBefore : model.RulesAfter;
                x += rules > 0 ? rules * RuleWidth + (rules - 1) * DoubleGap : 0;
                string text = b < n ? grid.Specs[b].Before : model.AfterLast;
                if (!string.IsNullOrEmpty(text))
                {
                    Shape(text, 0, out _, out _, out _, out float w);
                    x += w;
                }
                cellLeft[b] = x;
                if (b == n) break;
                if (grid.Specs[b].PadBefore) x += Padding;
                contentLeft[b] = x;
                x += widths[b];
            }
            float tableWidth = x;
            float SpanWidth(int first, int last) => contentLeft[Mathf.Min(last, n - 1)] + widths[Mathf.Min(last, n - 1)] - contentLeft[first];

            // Down: each row's lines and room, its cells top-aligned unless their column says middle or bottom.
            int rowsCount = model.Rows.Count;
            var cellLines = new List<Line>[rowsCount][];
            var ascent = new float[rowsCount];
            var descent = new float[rowsCount];
            for (int r = 0; r < rowsCount; r++)
            {
                TableRow row = model.Rows[r];
                cellLines[r] = new List<Line>[row.Cells.Count];
                ascent[r] = m_Ascent;
                descent[r] = m_Descent;
                for (int k = 0; k < row.Cells.Count; k++)
                {
                    TableCell cell = row.Cells[k];
                    if (cell.Column >= n) continue;
                    int last = Mathf.Min(cell.Column + cell.Span, n) - 1;
                    List<Line> lines = Wrap(cell.Prepared, SpanWidth(cell.Column, last));
                    cellLines[r][k] = lines;
                    if (cell.RowSpan > 1) continue;
                    float first = lines.Count > 0 ? lines[0].Ascent : m_Ascent;
                    float height = Height(lines);
                    char vertical = grid.Specs[cell.Column].Vertical;
                    if (vertical == 't')
                    {
                        ascent[r] = Mathf.Max(ascent[r], first);
                        descent[r] = Mathf.Max(descent[r], height - first);
                    }
                    else descent[r] = Mathf.Max(descent[r], height - ascent[r]);
                }
            }
            // A cell over several rows takes no room of its first: the last of them grows if it needs more.
            for (int r = 0; r < rowsCount; r++)
                for (int k = 0; k < model.Rows[r].Cells.Count; k++)
                {
                    TableCell cell = model.Rows[r].Cells[k];
                    if (cell.RowSpan <= 1 || cellLines[r][k] == null) continue;
                    int lastRow = Mathf.Min(r + cell.RowSpan, rowsCount) - 1;
                    float have = 0;
                    for (int q = r; q <= lastRow; q++) have += ascent[q] + descent[q] + (q < lastRow ? model.Rows[q].Space : 0);
                    float need = Height(cellLines[r][k]);
                    if (need > have) descent[lastRow] += need - have;
                }

            var rulesAt = new List<TableRule>[rowsCount + 1];
            foreach (TableRule rule in model.Rules)
            {
                int b = Mathf.Clamp(rule.Boundary, 0, rowsCount);
                (rulesAt[b] ?? (rulesAt[b] = new List<TableRule>())).Add(rule);
            }

            float captionTop = 0;
            List<Line> caption = null;
            TablePrepared captionPrepared = null;
            float captionWidth = 0;
            if (model.Caption != null && outermost)
            {
                var runs = new List<TableRun>();
                if (!string.IsNullOrEmpty(m_Label))
                {
                    runs.Add(new TableRun { Kind = TableRunKind.Text, Text = m_Label + (model.Number > 0 ? " " + model.Number : "") + ":" });
                    runs.Add(new TableRun { Kind = TableRunKind.Space });
                }
                runs.AddRange(model.Caption);
                captionPrepared = Prepare(runs, 0, room, depth);
                // A caption may run as wide as the text, as LaTeX's does, and is centred over the table.
                float limit = float.IsInfinity(room) ? captionPrepared.Max : Mathf.Min(room, Mathf.Max(tableWidth, captionPrepared.Max));
                caption = Wrap(captionPrepared, Mathf.Max(limit, 0.01f));
                captionWidth = 0;
                foreach (Line line in caption) captionWidth = Mathf.Max(captionWidth, line.Width);
            }
            float blockWidth = Mathf.Max(tableWidth, captionWidth);
            float shift = model.Centered || captionWidth > tableWidth ? (blockWidth - tableWidth) / 2 : 0;
            float tableLeft = left + shift;

            float y = top;
            if (caption != null && !model.CaptionBelow)
            {
                captionTop = y;
                y -= Height(caption) + CaptionGap;
            }
            float tableTop = y;
            var rowTop = new float[rowsCount + 1];
            var rowBottom = new float[rowsCount + 1];
            var baseline = new float[rowsCount];
            for (int r = 0; r <= rowsCount; r++)
            {
                rowTop[r] = y;
                List<TableRule> rules = rulesAt[r];
                if (rules != null)
                {
                    for (int i = 0; i < rules.Count; i++)
                    {
                        TableRule rule = rules[i];
                        if (i > 0 && rules[i - 1].From < 0 && rule.From < 0) y -= DoubleGap;
                        y -= rule.Above;
                        float from = rule.From < 0 ? 0 : cellLeft[Mathf.Min(rule.From, n - 1)];
                        float to = rule.From < 0 ? tableWidth : boundaryX[Mathf.Min(rule.To + 1, n)];
                        Rule(tableLeft + from, y - rule.Thickness, to - from, rule.Thickness, false);
                        // Rules of one boundary over different columns stand side by side.
                        bool sideBySide = i + 1 < rules.Count && rule.From >= 0 && rules[i + 1].From >= 0 && rules[i + 1].From > rule.To;
                        if (!sideBySide) y -= rule.Thickness + rule.Below;
                    }
                }
                if (r == rowsCount)
                {
                    rowBottom[r] = y;
                    break;
                }
                baseline[r] = y - ascent[r];
                y = baseline[r] - descent[r] - model.Rows[r].Space;
                rowBottom[r] = y;
            }
            float tableBottom = y;

            // Cells.
            for (int r = 0; r < rowsCount; r++)
            {
                TableRow row = model.Rows[r];
                for (int k = 0; k < row.Cells.Count; k++)
                {
                    TableCell cell = row.Cells[k];
                    List<Line> lines = cellLines[r][k];
                    if (lines == null) continue;
                    int last = Mathf.Min(cell.Column + cell.Span, n) - 1;
                    float w = SpanWidth(cell.Column, last);
                    char align = cell.Align != 0 ? cell.Align : grid.Specs[cell.Column].Align;
                    char vertical = grid.Specs[cell.Column].Vertical;
                    float height = Height(lines);
                    float firstAscent = lines.Count > 0 ? lines[0].Ascent : m_Ascent;
                    float cellTop = baseline[r] + firstAscent;
                    if (cell.RowSpan > 1)
                    {
                        int lastRow = Mathf.Min(r + cell.RowSpan, rowsCount) - 1;
                        float spanTop = baseline[r] + ascent[r], spanBottom = baseline[lastRow] - descent[lastRow];
                        cellTop = (spanTop + spanBottom + height) / 2;
                    }
                    else if (vertical == 'm') cellTop = (baseline[r] + ascent[r] + baseline[r] - descent[r] + height) / 2;
                    else if (vertical == 'b') cellTop = baseline[r] - descent[r] + height;
                    Draw(cell.Prepared, lines, tableLeft + contentLeft[cell.Column], cellTop, w, align);
                }
            }

            // Vertical rules, per row, broken where a cell spans the boundary, and as a \multicolumn's own spec says at its edges.
            for (int b = 0; b <= n; b++)
            {
                int columnRules = b < n ? grid.Specs[b].RulesBefore : model.RulesAfter;
                float segmentTop = float.NaN, segmentBottom = 0;
                int segmentRules = 0;
                void Close()
                {
                    if (!float.IsNaN(segmentTop) && segmentRules > 0)
                        for (int i = 0; i < segmentRules; i++)
                            Rule(tableLeft + boundaryX[b] + i * (RuleWidth + DoubleGap), segmentBottom, segmentTop - segmentBottom, RuleWidth, true);
                    segmentTop = float.NaN;
                }
                for (int r = 0; r < rowsCount; r++)
                {
                    int rules = columnRules;
                    foreach (TableCell cell in model.Rows[r].Cells)
                    {
                        int end = cell.Column + cell.Span;
                        if (b > cell.Column && b < end) rules = 0;
                        else if (cell.Span > 1 || cell.RulesLeft >= 0)
                        {
                            if (b == cell.Column && b == 0 && cell.RulesLeft >= 0) rules = cell.RulesLeft;
                            if (b == end && cell.RulesRight >= 0) rules = cell.RulesRight;
                        }
                    }
                    float upper = rowTop[r], lower = r == rowsCount - 1 ? rowBottom[rowsCount] : rowBottom[r];
                    if (rules != segmentRules)
                    {
                        Close();
                        segmentRules = rules;
                    }
                    if (float.IsNaN(segmentTop)) segmentTop = upper;
                    segmentBottom = lower;
                }
                Close();
            }

            if (caption != null)
            {
                float captionAt = model.CaptionBelow ? tableBottom - CaptionGap : captionTop;
                foreach (Line line in caption)
                {
                    var single = new List<Line> { line };
                    Draw(captionPrepared, single, left + (blockWidth - line.Width) / 2, captionAt, line.Width, 'l');
                    captionAt -= line.Ascent + line.Descent;
                }
                if (model.CaptionBelow) y = captionAt;
            }

            m_LastBaselines = (rowsCount > 0 ? baseline[0] : top - m_Ascent, rowsCount > 0 ? baseline[rowsCount - 1] : top - m_Ascent);
            return new Rect(left, y, blockWidth, top - y);
        }

        (float first, float last) m_LastBaselines;

        LetteraMathLayout Finish(Rect box, float room)
        {
            TableModel model = m_Root;
            float height = box.height;
            // The last guard of the text's width: a table that still comes out wider, as with more columns than room for their padding, is drawn smaller.
            float scale = !float.IsInfinity(room) && room > 0 && box.width > room ? room / box.width : 1;
            if (scale < 1)
            {
                for (int i = 0; i < m_Items.Count; i++)
                {
                    LetteraMathItem item = m_Items[i];
                    item.X = box.x + (item.X - box.x) * scale;
                    item.Y = box.yMax + (item.Y - box.yMax) * scale;
                    item.Scale *= scale;
                    item.Length *= scale;
                    item.Thickness *= scale;
                    m_Items[i] = item;
                }
                box.width *= scale;
                height *= scale;
                m_LastBaselines = (m_LastBaselines.first * scale, m_LastBaselines.last * scale);
                model.Warn("A table wider than its text even at its narrowest is drawn smaller.");
            }
            float first = -m_LastBaselines.first, last = -m_LastBaselines.last;
            // Where the baseline falls: an inline table centred on the axis, or at its first or last row as [t] and [b] ask; a block at its last row.
            float ascent;
            if (model.Block) ascent = last;
            else if (model.Position == 't') ascent = first;
            else if (model.Position == 'b') ascent = last;
            else ascent = height / 2 + Axis;
            ascent = Mathf.Clamp(ascent, 0, height);
            var items = m_Items.ToArray();
            for (int i = 0; i < items.Length; i++) items[i].Y += ascent;
            var layout = new LetteraMathLayout
            {
                Width = box.width,
                Ascent = ascent,
                Descent = height - ascent,
                InkAscent = ascent,
                InkDescent = height - ascent,
                InkLeft = 0,
                InkRight = box.width,
                Items = items,
                Faces = m_Faces.ToArray(),
                Colors = m_Colors.ToArray(),
                Display = model.Block,
                Source = model.Source,
            };
            return layout;
        }
    }
}

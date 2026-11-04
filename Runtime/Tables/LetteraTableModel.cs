using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace FirstSymbol.Lettera
{
    /// <summary>What a LaTeX table in text may cost. Past each, the table is cut back to it, with one warning.</summary>
    public static class LetteraTableLimits
    {
        public const int SourceLength = 65536, Rows = 1000, Columns = 64, Cells = 20000, Depth = 4, Glyphs = 60000;
        /// <summary>The largest length a table reads, in em. A width or a space past it is taken as this.</summary>
        public const float LengthEm = 500;
    }

    internal enum TableRunKind : byte { Text, Space, Math, Table, Break }

    internal struct TableRun
    {
        public TableRunKind Kind;
        public string Text;
        public byte Style;
        public float Width;
        public TableModel Table;
    }

    internal sealed class TableCell
    {
        public readonly List<TableRun> Runs = new List<TableRun>();
        public int Span = 1, RowSpan = 1;
        /// <summary>A \multicolumn's own alignment and rules, or 0 and -1 for the column's.</summary>
        public char Align;
        public int RulesLeft = -1, RulesRight = -1;
        public int Column;
        public TablePrepared Prepared;
    }

    internal sealed class TableRow
    {
        public readonly List<TableCell> Cells = new List<TableCell>();
        public float Space;
    }

    internal struct TableRule
    {
        /// <summary>Above this row; the row count for under the last.</summary>
        public int Boundary;
        /// <summary>Columns covered, first and last, from 0; -1 for all.</summary>
        public int From, To;
        public float Thickness, Above, Below;
    }

    internal sealed class TableColumn
    {
        public char Align = 'l', Vertical = 't';
        /// <summary>0 sized by its content, 1 a fixed width, 2 sharing a set width (X), 3 in proportion to its content (tabulary).</summary>
        public byte Kind;
        public float Width;
        public int RulesBefore;
        public bool PadBefore = true, PadAfter = true;
        public string Before;
        public byte Style;
    }

    /// <summary>One parsed table, shared by every text that shows the same source. Its last layout is kept for the room it was made for.</summary>
    internal sealed class TableModel
    {
        public string Source, Environment;
        public char Position = 'c';
        public float TargetFactor = -1, TargetEm;
        public bool Fill, Block, Centered;
        public readonly List<TableColumn> Columns = new List<TableColumn>();
        public int RulesAfter;
        public bool PadAfterLast = true;
        public string AfterLast;
        public readonly List<TableRow> Rows = new List<TableRow>();
        public readonly List<TableRule> Rules = new List<TableRule>();
        public List<TableRun> Caption;
        public bool CaptionBelow;
        public int Number;
        public string Warning;
        public int Depth;

        // The last layout, for the room and fonts it was made with.
        internal float LaidRoom = float.NaN;
        internal int LaidText, LaidMath;
        internal LetteraMathLayout Laid;
        internal string LaidLabel;
        internal int PreparedFor;
        internal float MinWidth, MaxWidth;

        internal void Warn(string message)
        {
            if (Warning == null) Warning = message;
        }
    }

    /// <summary>
    /// Reads tabular, tabular*, tabularx, tabulary, longtable and a table float around them. Every brace and environment is paired once
    /// beforehand, so reading is linear in the source, and every count and length is cut back to its limit.
    /// </summary>
    internal sealed class LetteraTableParser
    {
        internal const byte Bold = 1, Italic = 2;

        readonly string m_Text;
        readonly int[] m_Brace;
        readonly int[] m_EndOf;
        int m_Cells;
        TableModel m_Root;

        LetteraTableParser(string text)
        {
            m_Text = text;
            m_Brace = new int[text.Length];
            m_EndOf = new int[text.Length];
            Pair();
        }

        internal static bool IsTableEnvironment(string name) =>
            name == "tabular" || name == "tabular*" || name == "tabularx" || name == "tabulary" || name == "longtable" || name == "table" || name == "table*";

        internal static bool IsBlock(string name) => name == "longtable" || name == "table" || name == "table*";

        /// <summary>A table from its whole source, \begin to \end. Never null and never throws: what cannot be read shows as text.</summary>
        internal static TableModel Parse(string source)
        {
            source = source ?? "";
            string cut = null;
            if (source.Length > LetteraTableLimits.SourceLength)
            {
                cut = "A table longer than " + LetteraTableLimits.SourceLength + " characters is cut there.";
                source = source.Substring(0, LetteraTableLimits.SourceLength);
            }
            var parser = new LetteraTableParser(source);
            TableModel model = null;
            int at = SkipSpaces(source, 0, source.Length);
            if (parser.Command(at, out string name, out int after) && name == "begin") model = parser.Environment(at, 0);
            if (model == null)
            {
                model = new TableModel { Environment = "tabular" };
                var row = new TableRow();
                var cell = new TableCell();
                parser.Content(0, source.Length, 0, cell.Runs, 0);
                row.Cells.Add(cell);
                model.Rows.Add(row);
                model.Columns.Add(new TableColumn());
            }
            model.Source = source;
            if (cut != null) model.Warn(cut);
            return model;
        }

        // ---------------------------------------------------------------- pairing

        // Braces with braces, and each \begin{name} with its \end{name}, in one pass. -1 for none.
        void Pair()
        {
            string s = m_Text;
            var braces = new int[s.Length + 1];
            var begins = new int[s.Length + 1];
            var names = new string[s.Length + 1];
            int braceTop = 0, beginTop = 0;
            for (int i = 0; i < s.Length; i++)
            {
                m_Brace[i] = -1;
                m_EndOf[i] = -1;
            }
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c == '\\')
                {
                    if (Starts(s, i, "\\begin{") || Starts(s, i, "\\end{"))
                    {
                        bool begin = s[i + 1] == 'b';
                        int open = i + (begin ? 6 : 4);
                        int close = s.IndexOf('}', open, Math.Min(40, s.Length - open));
                        if (close > open)
                        {
                            string name = s.Substring(open + 1, close - open - 1);
                            if (begin)
                            {
                                begins[beginTop] = i;
                                names[beginTop++] = name;
                            }
                            else if (beginTop > 0 && names[beginTop - 1] == name)
                            {
                                m_EndOf[begins[--beginTop]] = close + 1;
                            }
                            // The name's braces pair as any others do.
                            m_Brace[open] = close;
                            m_Brace[close] = open;
                            i = close;
                            continue;
                        }
                    }
                    i++;
                    continue;
                }
                if (c == '{') braces[braceTop++] = i;
                else if (c == '}' && braceTop > 0)
                {
                    int open = braces[--braceTop];
                    m_Brace[open] = i;
                    m_Brace[i] = open;
                }
            }
        }

        static bool Starts(string s, int at, string what) => at + what.Length <= s.Length && string.CompareOrdinal(s, at, what, 0, what.Length) == 0;

        static int SkipSpaces(string s, int at, int to)
        {
            while (at < to && (char.IsWhiteSpace(s[at]) || s[at] == '%'))
            {
                if (s[at] == '%')
                    while (at < to && s[at] != '\n')
                        at++;
                else at++;
            }
            return at;
        }

        static bool IsLetter(char c) => (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z');

        // A command at a backslash: its name, letters or one other character, and where it ends.
        bool Command(int at, out string name, out int after)
        {
            string s = m_Text;
            name = null;
            after = at;
            if (at >= s.Length || s[at] != '\\') return false;
            int end = at + 1;
            if (end < s.Length && IsLetter(s[end]))
            {
                while (end < s.Length && IsLetter(s[end]) && end - at <= 40) end++;
            }
            else if (end < s.Length) end++;
            name = s.Substring(at + 1, end - at - 1);
            after = end;
            return true;
        }

        // A group at a position after spaces: its inside, and where it ends. False for none.
        bool Group(ref int at, int to, out int from, out int end)
        {
            from = end = -1;
            int i = SkipSpaces(m_Text, at, to);
            if (i >= to || m_Text[i] != '{' || m_Brace[i] < 0 || m_Brace[i] >= to) return false;
            from = i + 1;
            end = m_Brace[i];
            at = end + 1;
            return true;
        }

        string GroupText(ref int at, int to)
        {
            return Group(ref at, to, out int from, out int end) ? m_Text.Substring(from, end - from) : null;
        }

        // [ ... ] after spaces, with no nesting: an option's text.
        string Option(ref int at, int to)
        {
            int i = SkipSpaces(m_Text, at, to);
            if (i >= to || m_Text[i] != '[') return null;
            int close = m_Text.IndexOf(']', i, Math.Min(to - i, 256));
            if (close < 0) return null;
            at = close + 1;
            return m_Text.Substring(i + 1, close - i - 1);
        }

        // ---------------------------------------------------------------- environments

        TableModel Environment(int at, int depth)
        {
            string s = m_Text;
            int end = m_EndOf[at];
            if (!Command(at, out _, out int after)) return null;
            string name = GroupText(ref after, s.Length);
            if (name == null || !IsTableEnvironment(name) || end < 0) return null;
            int closeStart = s.LastIndexOf("\\end{", end - 1, StringComparison.Ordinal);
            var model = new TableModel { Environment = name, Block = IsBlock(name), Depth = depth };
            if (m_Root == null) m_Root = model;
            if (depth >= LetteraTableLimits.Depth)
            {
                Root.Warn("Tables nest deeper than " + LetteraTableLimits.Depth + " levels; the deepest show as text.");
                return null;
            }
            if (name == "table" || name == "table*")
            {
                Option(ref after, closeStart);
                Float(model, after, closeStart, depth);
                return model;
            }
            if (name == "longtable")
            {
                string position = Option(ref after, closeStart);
                model.Centered = position == null || position.Trim() == "c";
            }
            else if (name != "tabular")
            {
                string width = GroupText(ref after, closeStart);
                if (width != null) ReadLength(width, out model.TargetFactor, out model.TargetEm);
            }
            if (name != "longtable")
            {
                string position = Option(ref after, closeStart);
                if (!string.IsNullOrEmpty(position)) model.Position = char.ToLowerInvariant(position.Trim().Length > 0 ? position.Trim()[0] : 'c');
            }
            string spec = GroupText(ref after, closeStart);
            if (spec == null) Root.Warn("A table with no column spec reads every column as l.");
            else Columns(model, spec, 0);
            Body(model, after, closeStart, depth);
            return model;
        }

        TableModel Root => m_Root;

        // A float: its caption, where it stands, and the first table inside it, centred when it says so.
        void Float(TableModel model, int from, int to, int depth)
        {
            string s = m_Text;
            int at = from;
            TableModel inner = null;
            bool centered = false, captionFirst = false;
            List<TableRun> caption = null;
            while (at < to)
            {
                at = SkipSpaces(s, at, to);
                if (at >= to) break;
                if (!Command(at, out string name, out int after))
                {
                    at++;
                    continue;
                }
                if (name == "begin" && m_EndOf[at] > 0)
                {
                    int end = m_EndOf[at];
                    int probe = after;
                    string env = GroupText(ref probe, to);
                    if (inner == null && env != null && IsTableEnvironment(env) && env != "table" && env != "table*")
                    {
                        inner = Environment(at, depth + 1);
                        if (inner != null) inner.Depth = depth;
                    }
                    else if (env == "center") centered = true;
                    if (env == "center" && inner == null)
                    {
                        // The table inside a center stays this float's.
                        at = probe;
                        continue;
                    }
                    at = end;
                    continue;
                }
                if (name == "centering") centered = true;
                else if (name == "caption")
                {
                    Option(ref after, to);
                    if (Group(ref after, to, out int c0, out int c1))
                    {
                        caption = new List<TableRun>();
                        Content(c0, c1, 0, caption, depth + 1);
                        captionFirst = inner == null;
                    }
                }
                else if (name == "label") GroupText(ref after, to);
                else if (name == "end") GroupText(ref after, to);
                at = after;
            }
            if (inner == null)
            {
                inner = new TableModel { Environment = "tabular" };
                inner.Columns.Add(new TableColumn());
            }
            model.Columns.AddRange(inner.Columns);
            model.Rows.AddRange(inner.Rows);
            model.Rules.AddRange(inner.Rules);
            model.RulesAfter = inner.RulesAfter;
            model.PadAfterLast = inner.PadAfterLast;
            model.AfterLast = inner.AfterLast;
            model.TargetFactor = inner.TargetFactor;
            model.TargetEm = inner.TargetEm;
            model.Fill = inner.Fill;
            model.Environment = inner.Environment == "tabular" ? model.Environment : inner.Environment;
            model.Caption = caption;
            model.CaptionBelow = !captionFirst;
            model.Centered = centered;
            model.Block = true;
        }

        // ---------------------------------------------------------------- column spec

        // l c r, p m b with a width, X, L C R J, | rules, @{} and !{} separators, >{} for a column's style, *{n}{...} repeated.
        void Columns(TableModel model, string spec, int depth)
        {
            int at = 0;
            while (at < spec.Length)
            {
                char c = spec[at++];
                switch (c)
                {
                    case 'l': case 'c': case 'r':
                        Add(model, c, 0, 0);
                        break;
                    case 'L': case 'C': case 'R': case 'J':
                        Add(model, c == 'J' ? 'l' : char.ToLowerInvariant(c), 3, 0);
                        break;
                    case 'X':
                        Add(model, 'l', 2, 0);
                        break;
                    case 'S': case 'D': case 'd':
                        Add(model, 'c', 0, 0);
                        break;
                    case 'p': case 'm': case 'b':
                    {
                        string width = Braced(spec, ref at);
                        ReadLength(width ?? "", out float factor, out float em);
                        TableColumn column = Add(model, 'l', 1, factor >= 0 ? -factor : em);
                        if (column != null) column.Vertical = c == 'p' ? 't' : c;
                        break;
                    }
                    case '|':
                        m_PendingRules++;
                        break;
                    case '@':
                    case '!':
                    {
                        string inside = Braced(spec, ref at) ?? "";
                        if (inside.Contains("\\extracolsep")) model.Fill = true;
                        if (c == '!' && inside.Contains("vrule"))
                        {
                            m_PendingRules++;
                            break;
                        }
                        string shown = Plain(inside.Replace("\\extracolsep{\\fill}", "").Replace("\\extracolsep{\\stretch{1}}", ""));
                        if (shown.Length > 0) m_PendingBefore = (m_PendingBefore ?? "") + shown;
                        if (c == '@')
                        {
                            // @{} stands in place of the space on both sides of it.
                            m_PendingNoPad = true;
                            if (model.Columns.Count > 0) model.Columns[model.Columns.Count - 1].PadAfter = false;
                        }
                        break;
                    }
                    case '>':
                    {
                        string inside = Braced(spec, ref at) ?? "";
                        m_PendingStyle = 0;
                        if (inside.Contains("\\bfseries") || inside.Contains("\\bf")) m_PendingStyle |= Bold;
                        if (inside.Contains("\\itshape") || inside.Contains("\\em")) m_PendingStyle |= Italic;
                        if (inside.Contains("\\centering")) m_PendingAlign = 'c';
                        else if (inside.Contains("\\raggedleft")) m_PendingAlign = 'r';
                        else if (inside.Contains("\\raggedright")) m_PendingAlign = 'l';
                        break;
                    }
                    case '<':
                        Braced(spec, ref at);
                        break;
                    case '*':
                    {
                        string count = Braced(spec, ref at);
                        string repeated = Braced(spec, ref at) ?? "";
                        int times = 0;
                        if (count != null && int.TryParse(count.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int n)) times = n;
                        if (depth > 4) break;
                        for (int i = 0; i < times && model.Columns.Count < LetteraTableLimits.Columns; i++) Columns(model, repeated, depth + 1);
                        if (times > 0 && model.Columns.Count >= LetteraTableLimits.Columns) Root.Warn("A table with more than " + LetteraTableLimits.Columns + " columns is cut there.");
                        break;
                    }
                }
            }
            if (depth == 0 && model.Columns.Count > 0)
            {
                // What stands after the last column.
                if (m_PendingRules > 0) model.RulesAfter += m_PendingRules;
                if (m_PendingBefore != null) model.AfterLast = m_PendingBefore;
                if (m_PendingNoPad) model.PadAfterLast = false;
                m_PendingRules = 0;
                m_PendingBefore = null;
                m_PendingNoPad = false;
            }
        }

        int m_PendingRules;
        string m_PendingBefore;
        bool m_PendingNoPad;
        byte m_PendingStyle;
        char m_PendingAlign;

        TableColumn Add(TableModel model, char align, byte kind, float width)
        {
            if (model.Columns.Count >= LetteraTableLimits.Columns)
            {
                Root.Warn("A table with more than " + LetteraTableLimits.Columns + " columns is cut there.");
                return null;
            }
            var column = new TableColumn
            {
                Align = m_PendingAlign != 0 ? m_PendingAlign : align, Kind = kind, Width = width, RulesBefore = m_PendingRules,
                Before = m_PendingBefore, PadBefore = !m_PendingNoPad, Style = m_PendingStyle,
            };
            m_PendingRules = 0;
            m_PendingBefore = null;
            m_PendingNoPad = false;
            m_PendingStyle = 0;
            m_PendingAlign = (char)0;
            model.Columns.Add(column);
            return column;
        }

        static string Braced(string spec, ref int at)
        {
            while (at < spec.Length && char.IsWhiteSpace(spec[at])) at++;
            if (at >= spec.Length || spec[at] != '{') return null;
            int depth = 0;
            for (int i = at; i < spec.Length; i++)
            {
                if (spec[i] == '{') depth++;
                else if (spec[i] == '}' && --depth == 0)
                {
                    string inside = spec.Substring(at + 1, i - at - 1);
                    at = i + 1;
                    return inside;
                }
            }
            at = spec.Length;
            return null;
        }

        // A separator's text as it shows: commands dropped, escapes kept.
        static string Plain(string text)
        {
            var plain = new StringBuilder(text.Length);
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (c == '\\')
                {
                    if (i + 1 < text.Length && !IsLetter(text[i + 1]))
                    {
                        plain.Append(text[++i] == ',' ? ' ' : text[i]);
                        continue;
                    }
                    while (i + 1 < text.Length && IsLetter(text[i + 1])) i++;
                    continue;
                }
                if (c == '{' || c == '}') continue;
                plain.Append(c);
            }
            return plain.ToString();
        }

        /// <summary>A length as a share of the text's width and an absolute part, in em: 0.8\textwidth, 5cm, 120pt. pt counts against a 10 pt body.</summary>
        internal static bool ReadLength(string text, out float factor, out float em)
        {
            factor = -1;
            em = 0;
            text = text.Trim();
            int digits = 0;
            while (digits < text.Length && (char.IsDigit(text[digits]) || text[digits] == '.' || text[digits] == '-' || text[digits] == '+')) digits++;
            float value = 1;
            if (digits > 0 && (digits > 12 || !float.TryParse(text.Substring(0, digits), NumberStyles.Float, CultureInfo.InvariantCulture, out value))) return false;
            if (float.IsNaN(value) || float.IsInfinity(value)) return false;
            string unit = text.Substring(digits).Trim();
            switch (unit)
            {
                case "\\textwidth": case "\\linewidth": case "\\columnwidth": case "\\hsize": case "\\textheight":
                    factor = Math.Max(0, Math.Min(value, 1));
                    return true;
                case "em": em = value; break;
                case "ex": em = value * 0.45f; break;
                case "pt": em = value / 10; break;
                case "bp": em = value * 1.00375f / 10; break;
                case "pc": em = value * 1.2f; break;
                case "mm": em = value * 0.2845f; break;
                case "cm": em = value * 2.845f; break;
                case "in": em = value * 7.227f; break;
                case "px": em = value / 16; break;
                default: return false;
            }
            em = Math.Max(0, Math.Min(em, LetteraTableLimits.LengthEm));
            return true;
        }

        // ---------------------------------------------------------------- body

        enum Section : byte { Body, FirstHead, Head, Foot, LastFoot }

        // Rows split at \\ and cells at & where neither stands in a group, in math or in an inner environment. Rules and captions go first.
        void Body(TableModel model, int from, int to, int depth)
        {
            string s = m_Text;
            var sections = new List<(Section section, int from, int to)>();
            int at = from, sectionStart = from;
            // longtable's heads and feet: on a single page, the first head and the last foot show, and the others do not.
            while (at < to)
            {
                char c = s[at];
                if (c == '{' && m_Brace[at] > at)
                {
                    at = m_Brace[at] + 1;
                    continue;
                }
                if (c == '\\' && Command(at, out string name, out int after))
                {
                    if (name == "begin" && m_EndOf[at] > 0)
                    {
                        at = m_EndOf[at];
                        continue;
                    }
                    Section marker = name == "endfirsthead" ? Section.FirstHead : name == "endhead" ? Section.Head : name == "endfoot" ? Section.Foot : name == "endlastfoot" ? Section.LastFoot : Section.Body;
                    if (marker != Section.Body)
                    {
                        sections.Add((marker, sectionStart, at));
                        sectionStart = at = after;
                        continue;
                    }
                    at = after;
                    continue;
                }
                at++;
            }
            sections.Add((Section.Body, sectionStart, to));
            bool hasFirst = false, hasLast = false;
            foreach (var entry in sections)
            {
                hasFirst |= entry.section == Section.FirstHead;
                hasLast |= entry.section == Section.LastFoot;
            }
            void Take(Section wanted)
            {
                foreach (var entry in sections)
                    if (entry.section == wanted)
                        Rows(model, entry.from, entry.to, depth);
            }
            Take(hasFirst ? Section.FirstHead : Section.Head);
            Take(Section.Body);
            Take(hasLast ? Section.LastFoot : Section.Foot);
        }

        void Rows(TableModel model, int from, int to, int depth)
        {
            string s = m_Text;
            int at = from, rowStart = from;
            var cells = new List<(int from, int to)>();
            int cellStart = from;
            // Once no $ closes from some place, none closes from any later one: a lone $ costs one search, not one each.
            bool noDollar = false, noTag = false;
            while (at <= to)
            {
                bool end = at == to;
                char c = end ? '\0' : s[at];
                if (!end && c == '{' && m_Brace[at] > at && m_Brace[at] < to)
                {
                    at = m_Brace[at] + 1;
                    continue;
                }
                if (!end && c == '%')
                {
                    while (at < to && s[at] != '\n') at++;
                    continue;
                }
                if (!end && c == '<' && !noTag && Starts(s, at, "<math"))
                {
                    int close = s.IndexOf("</math>", at, to - at, StringComparison.OrdinalIgnoreCase);
                    noTag |= close < 0;
                    at = close < 0 ? at + 1 : close + 7;
                    continue;
                }
                if (!end && c == '$')
                {
                    int close = noDollar ? -1 : s.IndexOf('$', at + 1, to - at - 1);
                    noDollar |= close < 0;
                    at = close < 0 ? at + 1 : close + 1;
                    continue;
                }
                int rowEnd = -1, next = at + 1;
                if (!end && c == '\\' && Command(at, out string name, out int after))
                {
                    if (name == "begin" && m_EndOf[at] > 0 && m_EndOf[at] <= to)
                    {
                        at = m_EndOf[at];
                        continue;
                    }
                    if (name == "\\" || name == "tabularnewline" || name == "cr")
                    {
                        rowEnd = at;
                        next = after;
                        if (next < to && s[next] == '*') next++;
                    }
                    else
                    {
                        at = after;
                        continue;
                    }
                }
                if (!end && c == '&')
                {
                    cells.Add((cellStart, at));
                    cellStart = at = at + 1;
                    continue;
                }
                if (end || rowEnd >= 0)
                {
                    cells.Add((cellStart, end ? to : rowEnd));
                    float space = 0;
                    if (!end)
                    {
                        string extra = Option(ref next, to);
                        if (extra != null && ReadLength(extra, out float f, out float em) && f < 0) space = em;
                    }
                    AddRow(model, cells, space, depth, end);
                    cells.Clear();
                    if (end) break;
                    cellStart = at = next;
                    continue;
                }
                at++;
            }
            _ = rowStart;
        }

        // The rules a row starts with, then its cells. A row of rules only, as after the last \\, gives its rules and no row.
        void AddRow(TableModel model, List<(int from, int to)> cells, float space, int depth, bool last)
        {
            string s = m_Text;
            (int from, int to) first = cells[0];
            int at = first.from;
            while (true)
            {
                at = SkipSpaces(s, at, first.to);
                if (at >= first.to || !Command(at, out string name, out int after)) break;
                int boundary = model.Rows.Count;
                if (name == "hline" || name == "toprule" || name == "midrule" || name == "bottomrule" || name == "hdashline" || name == "specialrule")
                {
                    float thickness = name == "toprule" || name == "bottomrule" ? 0.08f : name == "midrule" ? 0.05f : 0.04f;
                    bool book = name != "hline" && name != "hdashline";
                    if (name == "specialrule")
                    {
                        string w = GroupText(ref after, first.to);
                        GroupText(ref after, first.to);
                        GroupText(ref after, first.to);
                        if (w != null && ReadLength(w, out float f, out float em) && f < 0) thickness = Math.Min(em, 1);
                    }
                    else if (book) Option(ref after, first.to);
                    model.Rules.Add(new TableRule { Boundary = boundary, From = -1, To = -1, Thickness = thickness, Above = book ? 0.2f : 0, Below = book ? 0.3f : 0 });
                    at = after;
                    continue;
                }
                if (name == "cline" || name == "cmidrule")
                {
                    if (name == "cmidrule")
                    {
                        Option(ref after, first.to);
                        int probe = SkipSpaces(s, after, first.to);
                        if (probe < first.to && s[probe] == '(')
                        {
                            int close = s.IndexOf(')', probe, Math.Min(16, first.to - probe));
                            if (close > 0) after = close + 1;
                        }
                    }
                    string range = GroupText(ref after, first.to);
                    if (range != null)
                    {
                        string[] ends = range.Split('-');
                        if (ends.Length == 2 && int.TryParse(ends[0].Trim(), out int a) && int.TryParse(ends[1].Trim(), out int b) && a >= 1 && b >= a)
                            model.Rules.Add(new TableRule { Boundary = boundary, From = Math.Min(a - 1, LetteraTableLimits.Columns), To = Math.Min(b - 1, LetteraTableLimits.Columns), Thickness = name == "cline" ? 0.04f : 0.03f, Above = name == "cline" ? 0 : 0.2f, Below = name == "cline" ? 0 : 0.3f });
                    }
                    at = after;
                    continue;
                }
                if (name == "noalign" || name == "addlinespace" || name == "rowcolor")
                {
                    Option(ref after, first.to);
                    GroupText(ref after, first.to);
                    at = after;
                    continue;
                }
                if (name == "caption")
                {
                    Option(ref after, first.to);
                    if (Group(ref after, first.to, out int c0, out int c1))
                    {
                        model.Caption = new List<TableRun>();
                        Content(c0, c1, 0, model.Caption, depth + 1);
                        model.CaptionBelow = model.Rows.Count > 0;
                    }
                    at = after;
                    if (cells.Count == 1) return;
                    continue;
                }
                break;
            }
            cells[0] = (at, first.to);
            bool empty = cells.Count == 1 && SkipSpaces(s, at, first.to) >= first.to;
            if (empty)
            {
                // \\[2pt] on an empty row still opens room under the last one.
                if (model.Rows.Count > 0) model.Rows[model.Rows.Count - 1].Space += space;
                return;
            }
            if (model.Rows.Count >= LetteraTableLimits.Rows)
            {
                Root.Warn("A table with more than " + LetteraTableLimits.Rows + " rows is cut there.");
                return;
            }
            var row = new TableRow { Space = space };
            int column = 0;
            foreach ((int a, int b) in cells)
            {
                if (++m_Cells > LetteraTableLimits.Cells)
                {
                    Root.Warn("A table with more than " + LetteraTableLimits.Cells + " cells is cut there.");
                    break;
                }
                if (column >= LetteraTableLimits.Columns)
                {
                    Root.Warn("A table with more than " + LetteraTableLimits.Columns + " columns is cut there.");
                    break;
                }
                TableCell cell = Cell(a, b, depth);
                cell.Column = column;
                column += cell.Span;
                row.Cells.Add(cell);
            }
            model.Rows.Add(row);
        }

        TableCell Cell(int from, int to, int depth)
        {
            string s = m_Text;
            var cell = new TableCell();
            int at = SkipSpaces(s, from, to);
            if (Command(at, out string name, out int after))
            {
                if (name == "multicolumn")
                {
                    string count = GroupText(ref after, to);
                    string spec = GroupText(ref after, to);
                    if (count != null && int.TryParse(count.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int n)) cell.Span = Math.Max(1, Math.Min(n, LetteraTableLimits.Columns));
                    if (spec != null) MulticolumnSpec(cell, spec);
                    if (Group(ref after, to, out int c0, out int c1))
                    {
                        Content(c0, c1, 0, cell.Runs, depth);
                        return cell;
                    }
                }
                else if (name == "multirow")
                {
                    string count = GroupText(ref after, to);
                    Option(ref after, to);
                    GroupText(ref after, to);
                    Option(ref after, to);
                    if (count != null && int.TryParse(count.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int n)) cell.RowSpan = Math.Max(1, Math.Min(n, LetteraTableLimits.Rows));
                    if (Group(ref after, to, out int c0, out int c1))
                    {
                        Content(c0, c1, 0, cell.Runs, depth);
                        return cell;
                    }
                }
            }
            Content(from, to, 0, cell.Runs, depth);
            return cell;
        }

        static void MulticolumnSpec(TableCell cell, string spec)
        {
            int left = 0, right = 0;
            bool seen = false;
            foreach (char c in spec)
            {
                if (c == '|')
                {
                    if (seen) right++;
                    else left++;
                }
                else if ("lcrpmbXLCRJ".IndexOf(c) >= 0 && !seen)
                {
                    seen = true;
                    cell.Align = c == 'c' || c == 'C' ? 'c' : c == 'r' || c == 'R' ? 'r' : 'l';
                }
            }
            cell.RulesLeft = left;
            cell.RulesRight = right;
        }

        // ---------------------------------------------------------------- content

        // A cell's words: spaces collapse, groups and \textbf and their kin set a style, $...$ is a formula, an inner table nests.
        void Content(int from, int to, byte style, List<TableRun> runs, int depth)
        {
            string s = m_Text;
            var word = new StringBuilder();
            int at = from;
            bool noSingle = false, noDouble = false, noParen = false, noTag = false;
            void Flush()
            {
                if (word.Length == 0) return;
                runs.Add(new TableRun { Kind = TableRunKind.Text, Text = word.ToString(), Style = style });
                word.Clear();
            }
            void Space(float width = 0)
            {
                Flush();
                if (width == 0 && (runs.Count == 0 || runs[runs.Count - 1].Kind == TableRunKind.Space || runs[runs.Count - 1].Kind == TableRunKind.Break)) return;
                runs.Add(new TableRun { Kind = TableRunKind.Space, Width = width });
            }
            if (depth > LetteraTableLimits.Depth * 4)
            {
                runs.Add(new TableRun { Kind = TableRunKind.Text, Text = s.Substring(from, Math.Min(to - from, 64)), Style = style });
                return;
            }
            while (at < to)
            {
                char c = s[at];
                if (char.IsWhiteSpace(c))
                {
                    Space();
                    at++;
                    continue;
                }
                if (c == '%')
                {
                    while (at < to && s[at] != '\n') at++;
                    continue;
                }
                if (c == '{')
                {
                    int close = m_Brace[at] > at && m_Brace[at] < to ? m_Brace[at] : to;
                    Flush();
                    Content(at + 1, close, style, runs, depth + 1);
                    at = close + 1;
                    continue;
                }
                if (c == '}')
                {
                    at++;
                    continue;
                }
                if (c == '$')
                {
                    int open = at + 1 < to && s[at + 1] == '$' ? 2 : 1;
                    int close = (open == 2 ? noDouble : noSingle) ? -1 : s.IndexOf(open == 2 ? "$$" : "$", at + open, to - at - open, StringComparison.Ordinal);
                    if (close < 0)
                    {
                        if (open == 2) noDouble = true;
                        else noSingle = true;
                        word.Append('$');
                        at++;
                        continue;
                    }
                    Flush();
                    runs.Add(new TableRun { Kind = TableRunKind.Math, Text = s.Substring(at + open, close - at - open), Style = style });
                    at = close + open;
                    continue;
                }
                if (c == '~')
                {
                    word.Append(' ');
                    at++;
                    continue;
                }
                // A formula's own tag, as anywhere else in the text.
                if (c == '<' && !noTag && Starts(s, at, "<math") && at + 5 < to && (s[at + 5] == '>' || s[at + 5] == '='))
                {
                    int open = s.IndexOf('>', at, Math.Min(64, to - at));
                    int close = open < 0 ? -1 : s.IndexOf("</math>", open, to - open, StringComparison.OrdinalIgnoreCase);
                    if (close >= 0)
                    {
                        Flush();
                        runs.Add(new TableRun { Kind = TableRunKind.Math, Text = s.Substring(open + 1, close - open - 1), Style = style });
                        at = close + 7;
                        continue;
                    }
                    noTag |= open >= 0;
                }
                if (c == '-' && at + 1 < to && s[at + 1] == '-')
                {
                    bool em = at + 2 < to && s[at + 2] == '-';
                    word.Append(em ? '—' : '–');
                    at += em ? 3 : 2;
                    continue;
                }
                if (c == '`' && at + 1 < to && s[at + 1] == '`')
                {
                    word.Append('“');
                    at += 2;
                    continue;
                }
                if (c == '\'' && at + 1 < to && s[at + 1] == '\'')
                {
                    word.Append('”');
                    at += 2;
                    continue;
                }
                if (c != '\\' || !Command(at, out string name, out int after))
                {
                    word.Append(c);
                    at++;
                    continue;
                }
                at = after;
                switch (name)
                {
                    case "textbf":
                    case "textit": case "emph": case "textsl":
                    case "textrm": case "textsf": case "texttt": case "textup": case "textnormal": case "text": case "mbox": case "textsc": case "underline":
                    {
                        byte inner = name == "textbf" ? (byte)(style | Bold) : name == "textit" || name == "emph" || name == "textsl" ? (byte)(style | Italic) : name == "textnormal" || name == "textup" ? (byte)0 : style;
                        if (Group(ref at, to, out int g0, out int g1))
                        {
                            Flush();
                            Content(g0, g1, inner, runs, depth + 1);
                        }
                        continue;
                    }
                    case "bfseries": case "bf": Flush(); style |= Bold; continue;
                    case "itshape": case "it": case "em": case "slshape": case "sl": Flush(); style |= Italic; continue;
                    case "normalfont": case "rmfamily": case "upshape": case "mdseries": Flush(); style = 0; continue;
                    case "(":
                    {
                        int close = noParen ? -1 : s.IndexOf("\\)", at, to - at, StringComparison.Ordinal);
                        noParen |= close < 0;
                        if (close < 0) continue;
                        Flush();
                        runs.Add(new TableRun { Kind = TableRunKind.Math, Text = s.Substring(at, close - at), Style = style });
                        at = close + 2;
                        continue;
                    }
                    case "\\": case "newline": case "linebreak": case "par": case "tabularnewline":
                        Flush();
                        runs.Add(new TableRun { Kind = TableRunKind.Break });
                        Option(ref at, to);
                        continue;
                    case "begin":
                    {
                        int start = at - 6;
                        int end = m_EndOf[start] > 0 && m_EndOf[start] <= to ? m_EndOf[start] : -1;
                        int probe = at;
                        string env = GroupText(ref probe, to);
                        if (end > 0 && env != null && IsTableEnvironment(env))
                        {
                            TableModel nested = Environment(start, depth + 1);
                            if (nested != null)
                            {
                                Flush();
                                runs.Add(new TableRun { Kind = TableRunKind.Table, Table = nested, Style = style });
                                at = end;
                                continue;
                            }
                        }
                        if (end > 0)
                        {
                            // Another environment shows what it holds.
                            int inside = probe, close = s.LastIndexOf("\\end{", end - 1, StringComparison.Ordinal);
                            Flush();
                            Content(inside, Math.Max(inside, close), style, runs, depth + 1);
                            at = end;
                        }
                        else at = probe;
                        continue;
                    }
                    case "end":
                        GroupText(ref at, to);
                        continue;
                    case "&": case "%": case "$": case "#": case "_": case "{": case "}":
                        word.Append(name[0]);
                        continue;
                    case "textbackslash": word.Append('\\'); continue;
                    case "ldots": case "dots": case "textellipsis": word.Append('…'); continue;
                    case "textendash": word.Append('–'); continue;
                    case "textemdash": word.Append('—'); continue;
                    case "textdegree": word.Append('°'); continue;
                    case "texttimes": word.Append('×'); continue;
                    case "textpm": word.Append('±'); continue;
                    case "S": word.Append('§'); continue;
                    case "P": word.Append('¶'); continue;
                    case "copyright": case "textcopyright": word.Append('©'); continue;
                    case "checkmark": word.Append('✓'); continue;
                    case ",": case "thinspace": Space(1f / 6); continue;
                    case " ": Space(0.333f); continue;
                    case "enspace": Space(0.5f); continue;
                    case "quad": Space(1); continue;
                    case "qquad": Space(2); continue;
                    case "hspace": case "hskip":
                    {
                        if (at < to && s[at] == '*') at++;
                        string length = GroupText(ref at, to);
                        if (length != null && ReadLength(length, out float f, out float em) && f < 0 && em > 0) Space(em);
                        continue;
                    }
                    case "makecell": case "shortstack": case "parbox": case "fbox": case "framebox": case "raisebox": case "smash":
                    {
                        Option(ref at, to);
                        if (name == "parbox" || name == "raisebox") GroupText(ref at, to);
                        if (Group(ref at, to, out int g0, out int g1))
                        {
                            Flush();
                            Content(g0, g1, style, runs, depth + 1);
                        }
                        continue;
                    }
                    case "label": case "vspace": case "cellcolor": case "rowcolor": case "color": case "footnotemark": case "index": case "hypertarget":
                        if (at < to && s[at] == '*') at++;
                        Option(ref at, to);
                        GroupText(ref at, to);
                        continue;
                    case "textcolor":
                        Option(ref at, to);
                        GroupText(ref at, to);
                        continue;
                    case "ref": case "cite": case "pageref": case "eqref":
                        GroupText(ref at, to);
                        word.Append("??");
                        continue;
                    case "footnote":
                        Option(ref at, to);
                        GroupText(ref at, to);
                        continue;
                    case "rule":
                        Option(ref at, to);
                        GroupText(ref at, to);
                        GroupText(ref at, to);
                        continue;
                    case "multicolumn":
                        GroupText(ref at, to);
                        GroupText(ref at, to);
                        continue;
                    case "multirow":
                        GroupText(ref at, to);
                        Option(ref at, to);
                        GroupText(ref at, to);
                        Option(ref at, to);
                        continue;
                }
                // Any other command goes, and what its groups hold shows: \centering, \small, \hfill, \url{...}.
            }
            Flush();
            if (depth == 0 || runs.Count > 0) Trim(runs);
        }

        // A cell's last space, as before \\ or &, shows nothing.
        static void Trim(List<TableRun> runs)
        {
            while (runs.Count > 0 && runs[runs.Count - 1].Kind == TableRunKind.Space && runs[runs.Count - 1].Width == 0) runs.RemoveAt(runs.Count - 1);
        }
    }
}

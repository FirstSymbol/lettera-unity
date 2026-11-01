using System;
using System.Collections.Generic;
using System.Globalization;

namespace FirstSymbol.Lettera
{
    /// <summary>What hostile input may cost. Each has a test at, and one past, its value.</summary>
    public static class LetteraMathLimits
    {
        public const int SourceLength = 16384, Depth = 32, Nodes = 8192, Rows = 256, Columns = 64, Cells = 2500, DrawItems = 16384, AssemblyParts = 128, CommandName = 32, FormulasInText = 4096;
        public const float LengthEm = 64, StretchEm = 64;
    }

    /// <summary>A command a project adds, called only for a name the parser does not know.</summary>
    public interface ILetteraMathExtension
    {
        /// <summary>True when this is its command. The node goes in the formula's place; null draws nothing.</summary>
        bool TryParse(string command, out string replacementSource);
    }

    internal enum MathNodeKind : byte
    {
        List, Char, Space, Scripts, Fraction, Binomial, Radical, Style, Size, Delimited, Segment, BigDelimiter, Table, Row, Cell,
        Accent, Bar, Stretched, Arrow, Stack, Operator, Text, Boxed, Phantom, Smash, Color, Not,
    }

    internal enum MathClass : byte { Ord, Op, Bin, Rel, Open, Close, Punct, Inner }

    internal enum MathAlphabet : byte { Default, Roman, Italic, Bold, SansSerif, Monospace, DoubleStruck, Script, Fraktur, BoldSymbol }

    internal enum MathStyle : byte { Display, Text, Script, ScriptScript }

    internal struct MathNode
    {
        public MathNodeKind Kind;
        public MathClass Class;
        public byte Flags;
        public int Next, A, B, C;
        public int Cp, Cp2;
        public int Info;
        public float Value;
        public int Text;
    }

    /// <summary>A parsed formula: nodes in one array, joined by index, so no token costs an object.</summary>
    internal sealed class MathTree
    {
        public MathNode[] Nodes = new MathNode[32];
        public int Count;
        public readonly List<string> Strings = new List<string>();
        public int Root = -1;
        public string Error;
        public int ErrorPosition = -1;

        public int Add(MathNodeKind kind)
        {
            if (Count == Nodes.Length) Array.Resize(ref Nodes, Count * 2);
            Nodes[Count] = new MathNode { Kind = kind, Next = -1, A = -1, B = -1, C = -1, Text = -1 };
            return Count++;
        }

        public int Keep(string text)
        {
            Strings.Add(text);
            return Strings.Count - 1;
        }
    }

    /// <summary>Reads the notation in one pass, with one token of lookahead, and never goes back. So its time is linear in what it is given, whatever that is.</summary>
    internal sealed class LetteraMathParser
    {
        internal const byte FlagLimits = 1, FlagNoLimits = 2, FlagDisplayLimits = 4, FlagWide = 8, FlagForceClass = 16;
        internal const int RowHline = 1, RowDashed = 2;

        readonly string m_Source;
        readonly MathTree m_Tree = new MathTree();
        int m_At, m_Depth;
        MathAlphabet m_Alphabet;
        bool m_InTable;

        LetteraMathParser(string source) => m_Source = source;

        /// <summary>Parses a formula. A failure is in the tree's Error, and never an exception.</summary>
        internal static MathTree Parse(string source)
        {
            var parser = new LetteraMathParser(source ?? "");
            MathTree tree = parser.m_Tree;
            if (parser.m_Source.Length > LetteraMathLimits.SourceLength)
            {
                tree.Error = "This formula is longer than " + LetteraMathLimits.SourceLength + " characters.";
                tree.ErrorPosition = 0;
                return tree;
            }
            tree.Root = parser.ParseList(EndOfSource);
            if (tree.Error == null)
            {
                parser.SkipSpace();
                if (parser.m_At < parser.m_Source.Length) parser.Fail(parser.m_Source[parser.m_At] == '}' ? "A closing brace with no opening one." : "Something here cannot be read.");
            }
            return tree;
        }

        const int EndOfSource = 0, EndOfGroup = 1, EndOfCell = 2, EndOfDelimited = 3, EndOfOptional = 4, EndOfRest = 5;

        void Fail(string message)
        {
            if (m_Tree.Error == null)
            {
                m_Tree.Error = message;
                m_Tree.ErrorPosition = m_At;
            }
            m_At = m_Source.Length;
        }

        bool Failed => m_Tree.Error != null;

        int NewNode(MathNodeKind kind)
        {
            if (m_Tree.Count >= LetteraMathLimits.Nodes)
            {
                Fail("This formula has more than " + LetteraMathLimits.Nodes + " parts.");
                return 0 < m_Tree.Count ? 0 : m_Tree.Add(kind);
            }
            return m_Tree.Add(kind);
        }

        bool Enter()
        {
            if (++m_Depth > LetteraMathLimits.Depth)
            {
                Fail("This formula nests deeper than " + LetteraMathLimits.Depth + " levels.");
                return false;
            }
            return true;
        }

        void Leave() => m_Depth--;

        void SkipSpace()
        {
            while (m_At < m_Source.Length)
            {
                char c = m_Source[m_At];
                if (c == '%')
                {
                    while (m_At < m_Source.Length && m_Source[m_At] != '\n') m_At++;
                    continue;
                }
                if (c == ' ' || c == '\t' || c == '\n' || c == '\r' || c == '\f' || IsInvisible(c))
                {
                    m_At++;
                    continue;
                }
                break;
            }
        }

        static bool IsInvisible(char c) => c == 0x200E || c == 0x200F || (c >= 0x202A && c <= 0x202E) || (c >= 0x2066 && c <= 0x2069) || c == 0x200B || c == 0xFEFF || c == 0x061C;

        // A command's name, with the source position left after it. One non-letter makes a command of its own.
        string PeekCommand(out int after)
        {
            int at = m_At + 1;
            if (at >= m_Source.Length)
            {
                after = at;
                return "";
            }
            if (!IsLetter(m_Source[at]))
            {
                after = at + 1;
                return m_Source.Substring(at, 1);
            }
            int start = at;
            while (at < m_Source.Length && IsLetter(m_Source[at]) && at - start <= LetteraMathLimits.CommandName) at++;
            after = at;
            return m_Source.Substring(start, at - start);
        }

        static bool IsLetter(char c) => (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z');

        // A list of items up to what ends it, which is left for the caller to read.
        int ParseList(int end)
        {
            int list = NewNode(MathNodeKind.List), last = -1;
            if (Failed) return list;
            if (!Enter()) return list;
            while (!Failed)
            {
                SkipSpace();
                if (m_At >= m_Source.Length) break;
                char c = m_Source[m_At];
                if (c == '}')
                {
                    if (end == EndOfGroup || end == EndOfRest) break;
                    if (end == EndOfCell || end == EndOfDelimited || end == EndOfOptional) Fail("A closing brace with no opening one.");
                    break;
                }
                if (c == ']' && end == EndOfOptional) break;
                if (c == '&')
                {
                    if (m_InTable && end == EndOfCell) break;
                    if (m_InTable && end == EndOfRest) break;
                    Fail("An & outside a table.");
                    break;
                }
                if (c == '\\')
                {
                    string name = PeekCommand(out _);
                    if (name == "\\")
                    {
                        if (m_InTable && (end == EndOfCell || end == EndOfRest)) break;
                        Fail("A line break outside a table.");
                        break;
                    }
                    if (name == "end")
                    {
                        if (end == EndOfCell || end == EndOfRest) break;
                        Fail("An \\end with no \\begin.");
                        break;
                    }
                    if (name == "right" || name == "middle")
                    {
                        if (end == EndOfDelimited || end == EndOfRest) break;
                        Fail("A \\" + name + " with no \\left.");
                        break;
                    }
                }
                int item = ParseItem(end);
                if (Failed) break;
                if (item < 0) continue;
                if (last < 0) m_Tree.Nodes[list].A = item;
                else m_Tree.Nodes[last].Next = item;
                last = item;
                while (m_Tree.Nodes[last].Next >= 0) last = m_Tree.Nodes[last].Next;
            }
            Leave();
            return list;
        }

        int ParseItem(int end)
        {
            int atom = ParseAtom(end);
            if (Failed || atom < 0) return atom;
            return ParseScripts(atom);
        }

        int ParseScripts(int atom)
        {
            int sup = -1, sub = -1;
            byte limits = 0;
            while (!Failed)
            {
                SkipSpace();
                if (m_At >= m_Source.Length) break;
                char c = m_Source[m_At];
                if (c == '\\')
                {
                    string name = PeekCommand(out int after);
                    if (name == "limits") limits = FlagLimits;
                    else if (name == "nolimits") limits = FlagNoLimits;
                    else if (name == "displaylimits") limits = FlagDisplayLimits;
                    else break;
                    m_At = after;
                    continue;
                }
                if (c == '\'')
                {
                    int primes = 0;
                    while (m_At < m_Source.Length && m_Source[m_At] == '\'')
                    {
                        primes++;
                        m_At++;
                        if (primes > LetteraMathLimits.Nodes / 2) break;
                    }
                    int mark = Primes(primes);
                    if (sup < 0) sup = mark;
                    else Fail("Two superscripts on one thing.");
                    // A superscript that follows joins the primes.
                    SkipSpace();
                    if (!Failed && m_At < m_Source.Length && m_Source[m_At] == '^')
                    {
                        m_At++;
                        int more = ParseArgument();
                        int joined = NewNode(MathNodeKind.List);
                        m_Tree.Nodes[joined].A = mark;
                        m_Tree.Nodes[LastOf(mark)].Next = more;
                        sup = joined;
                    }
                    continue;
                }
                if (c != '^' && c != '_') break;
                m_At++;
                int argument = ParseArgument();
                if (Failed) break;
                if (c == '^')
                {
                    if (sup >= 0) Fail("Two superscripts on one thing.");
                    sup = argument;
                }
                else
                {
                    if (sub >= 0) Fail("Two subscripts on one thing.");
                    sub = argument;
                }
            }
            if (sup < 0 && sub < 0 && limits == 0) return atom;
            int node = NewNode(MathNodeKind.Scripts);
            ref MathNode n = ref m_Tree.Nodes[node];
            n.A = atom;
            n.B = sup;
            n.C = sub;
            n.Flags = limits;
            n.Class = m_Tree.Nodes[atom].Class;
            return node;
        }

        int LastOf(int node)
        {
            while (m_Tree.Nodes[node].Next >= 0) node = m_Tree.Nodes[node].Next;
            return node;
        }

        int Primes(int count)
        {
            int cp = count == 1 ? 0x2032 : count == 2 ? 0x2033 : count == 3 ? 0x2034 : 0x2032;
            int repeat = count <= 3 ? 1 : count;
            int first = -1, last = -1;
            for (int i = 0; i < repeat && !Failed; i++)
            {
                int node = CharNode(cp, MathClass.Ord);
                if (first < 0) first = node;
                else m_Tree.Nodes[last].Next = node;
                last = node;
            }
            return first;
        }

        int CharNode(int cp, MathClass cls)
        {
            int node = NewNode(MathNodeKind.Char);
            ref MathNode n = ref m_Tree.Nodes[node];
            n.Cp = cp;
            n.Class = cls;
            n.Info = (int)m_Alphabet;
            return node;
        }

        // One token as an argument, or a group: x^23 raises only the 2, and \frac12 works.
        int ParseArgument()
        {
            SkipSpace();
            if (m_At >= m_Source.Length)
            {
                Fail("Something is missing at the end of this formula.");
                return NewNode(MathNodeKind.List);
            }
            char c = m_Source[m_At];
            if (c == '{')
            {
                m_At++;
                int group = ParseList(EndOfGroup);
                if (!Failed)
                {
                    if (m_At >= m_Source.Length || m_Source[m_At] != '}') Fail("An opening brace with no closing one.");
                    else m_At++;
                }
                return group;
            }
            if (c == '^' || c == '_' || c == '}' || c == '&' || c == '\'')
            {
                Fail("Something is missing before '" + c + "'.");
                return NewNode(MathNodeKind.List);
            }
            if (!Enter()) return NewNode(MathNodeKind.List);
            int atom = ParseAtom(EndOfSource);
            Leave();
            return atom < 0 ? NewNode(MathNodeKind.List) : atom;
        }

        int ReadCodepoint()
        {
            char c = m_Source[m_At++];
            if (char.IsHighSurrogate(c) && m_At < m_Source.Length && char.IsLowSurrogate(m_Source[m_At])) return char.ConvertToUtf32(c, m_Source[m_At++]);
            return char.IsSurrogate(c) ? 0xFFFD : c;
        }

        int ParseAtom(int end)
        {
            char c = m_Source[m_At];
            switch (c)
            {
                case '{':
                    return ParseArgument();
                case '^':
                case '_':
                case '\'':
                    // A script with nothing before it stands on an empty base, as a prescript does.
                    return NewNode(MathNodeKind.List);
                case '~':
                    m_At++;
                    return SpaceNode(0.3333f);
                case '\\':
                    return ParseCommand(end);
            }
            int cp = ReadCodepoint();
            return CharNode(MapInput(cp, out MathClass cls), cls);
        }

        static int MapInput(int cp, out MathClass cls)
        {
            switch (cp)
            {
                case '-': cls = MathClass.Bin; return 0x2212;
                case '*': cls = MathClass.Bin; return 0x2217;
                case '+': cls = MathClass.Bin; return cp;
                case '=': case '<': case '>': case ':': cls = MathClass.Rel; return cp;
                case '(': case '[': cls = MathClass.Open; return cp;
                case ')': case ']': case '!': case '?': cls = MathClass.Close; return cp;
                case ',': case ';': cls = MathClass.Punct; return cp;
            }
            cls = cp > 0x7F ? LetteraMathSymbols.ClassOf(cp) : MathClass.Ord;
            return cp;
        }

        int SpaceNode(float em)
        {
            int node = NewNode(MathNodeKind.Space);
            m_Tree.Nodes[node].Value = Clamp(em);
            return node;
        }

        static float Clamp(float em) => em < -LetteraMathLimits.LengthEm ? -LetteraMathLimits.LengthEm : em > LetteraMathLimits.LengthEm ? LetteraMathLimits.LengthEm : em;

        int WithAlphabet(MathAlphabet alphabet)
        {
            MathAlphabet was = m_Alphabet;
            m_Alphabet = alphabet;
            int argument = ParseArgument();
            m_Alphabet = was;
            return argument;
        }

        int ParseCommand(int end)
        {
            string name = PeekCommand(out int after);
            if (name.Length == 0)
            {
                Fail("A backslash with nothing after it.");
                return -1;
            }
            if (name.Length > LetteraMathLimits.CommandName)
            {
                Fail("A command name longer than " + LetteraMathLimits.CommandName + " letters.");
                return -1;
            }
            int position = m_At;
            m_At = after;
            switch (name)
            {
                case "frac": return Fraction(-1, 0);
                case "dfrac": return Fraction((int)MathStyle.Display, 0);
                case "tfrac": return Fraction((int)MathStyle.Text, 0);
                case "cfrac": return Fraction((int)MathStyle.Display, 1);
                case "binom": return Binomial(-1);
                case "dbinom": return Binomial((int)MathStyle.Display);
                case "tbinom": return Binomial((int)MathStyle.Text);
                case "sqrt": return Radical();
                case "displaystyle": return StyleNode(MathStyle.Display);
                case "textstyle": return StyleNode(MathStyle.Text);
                case "scriptstyle": return StyleNode(MathStyle.Script);
                case "scriptscriptstyle": return StyleNode(MathStyle.ScriptScript);
                case "tiny": return SizeNode(0.5f);
                case "scriptsize": return SizeNode(0.7f);
                case "footnotesize": return SizeNode(0.8f);
                case "small": return SizeNode(0.9f);
                case "normalsize": return SizeNode(1f);
                case "large": return SizeNode(1.2f);
                case "Large": return SizeNode(1.44f);
                case "LARGE": return SizeNode(1.728f);
                case "huge": return SizeNode(2.074f);
                case "Huge": return SizeNode(2.488f);
                case "left": return Delimited();
                case "big": return BigDelimiter(1.2f, MathClass.Ord);
                case "Big": return BigDelimiter(1.8f, MathClass.Ord);
                case "bigg": return BigDelimiter(2.4f, MathClass.Ord);
                case "Bigg": return BigDelimiter(3f, MathClass.Ord);
                case "bigl": return BigDelimiter(1.2f, MathClass.Open);
                case "Bigl": return BigDelimiter(1.8f, MathClass.Open);
                case "biggl": return BigDelimiter(2.4f, MathClass.Open);
                case "Biggl": return BigDelimiter(3f, MathClass.Open);
                case "bigr": return BigDelimiter(1.2f, MathClass.Close);
                case "Bigr": return BigDelimiter(1.8f, MathClass.Close);
                case "biggr": return BigDelimiter(2.4f, MathClass.Close);
                case "Biggr": return BigDelimiter(3f, MathClass.Close);
                case "bigm": return BigDelimiter(1.2f, MathClass.Rel);
                case "Bigm": return BigDelimiter(1.8f, MathClass.Rel);
                case "biggm": return BigDelimiter(2.4f, MathClass.Rel);
                case "Biggm": return BigDelimiter(3f, MathClass.Rel);
                case "begin": return Environment();
                case "hat": return Accent(0x0302, false);
                case "check": return Accent(0x030C, false);
                case "tilde": return Accent(0x0303, false);
                case "acute": return Accent(0x0301, false);
                case "grave": return Accent(0x0300, false);
                case "dot": return Accent(0x0307, false);
                case "ddot": return Accent(0x0308, false);
                case "dddot": return Accent(0x20DB, false);
                case "ddddot": return Accent(0x20DC, false);
                case "breve": return Accent(0x0306, false);
                case "bar": return Accent(0x0304, false);
                case "vec": return Accent(0x20D7, false);
                case "mathring": return Accent(0x030A, false);
                case "widehat": return Accent(0x0302, true);
                case "widetilde": return Accent(0x0303, true);
                case "overline": return Bar(0);
                case "underline": return Bar(1);
                case "overbrace": return Stretched(0x23DE, true, MathClass.Op);
                case "underbrace": return Stretched(0x23DF, false, MathClass.Op);
                case "overrightarrow": return Stretched(0x2192, true, MathClass.Ord);
                case "overleftarrow": return Stretched(0x2190, true, MathClass.Ord);
                case "overleftrightarrow": return Stretched(0x2194, true, MathClass.Ord);
                case "underrightarrow": return Stretched(0x2192, false, MathClass.Ord);
                case "underleftarrow": return Stretched(0x2190, false, MathClass.Ord);
                case "xrightarrow": return Arrow(0x2192);
                case "xleftarrow": return Arrow(0x2190);
                case "overset": return Stack(0);
                case "underset": return Stack(1);
                case "stackrel": return Stack(2);
                case "substack": return SubStack();
                case "mathrm": return WithAlphabet(MathAlphabet.Roman);
                case "mathit": return WithAlphabet(MathAlphabet.Italic);
                case "mathbf": return WithAlphabet(MathAlphabet.Bold);
                case "mathsf": return WithAlphabet(MathAlphabet.SansSerif);
                case "mathtt": return WithAlphabet(MathAlphabet.Monospace);
                case "mathbb": return WithAlphabet(MathAlphabet.DoubleStruck);
                case "mathcal": case "mathscr": return WithAlphabet(MathAlphabet.Script);
                case "mathfrak": return WithAlphabet(MathAlphabet.Fraktur);
                case "boldsymbol": return WithAlphabet(MathAlphabet.BoldSymbol);
                case "text": case "textrm": case "mbox": return TextNode(0);
                case "textbf": return TextNode(1);
                case "textit": return TextNode(2);
                case "operatorname": return OperatorName();
                case ",": case "thinspace": return SpaceNode(3f / 18);
                case ":": case ">": case "medspace": return SpaceNode(4f / 18);
                case ";": case "thickspace": return SpaceNode(5f / 18);
                case "!": case "negthinspace": return SpaceNode(-3f / 18);
                case "negmedspace": return SpaceNode(-4f / 18);
                case "negthickspace": return SpaceNode(-5f / 18);
                case " ": return SpaceNode(0.3333f);
                case "quad": return SpaceNode(1);
                case "qquad": return SpaceNode(2);
                case "enspace": return SpaceNode(0.5f);
                case "hspace": case "mspace": return SpaceNode(ReadBracedLength());
                case "boxed": return Wrap(MathNodeKind.Boxed, 0);
                case "phantom": return Wrap(MathNodeKind.Phantom, 0);
                case "hphantom": return Wrap(MathNodeKind.Phantom, 1);
                case "vphantom": return Wrap(MathNodeKind.Phantom, 2);
                case "smash": return Wrap(MathNodeKind.Smash, 0);
                case "not": return Wrap(MathNodeKind.Not, 0);
                case "textcolor": return TextColor();
                case "color": return ColorRest(end);
                case "tag": case "tag*":
                    // A number's place on the line is a text's, and this version draws no tags.
                    ReadRawGroup();
                    return -1;
                case "notag": case "nonumber": return -1;
                case "%": case "_": case "#": case "$": case "&":
                    return CharNode(name[0], MathClass.Ord);
                case "{": return CharNode('{', MathClass.Open);
                case "}": return CharNode('}', MathClass.Close);
                case "|": return CharNode(0x2225, MathClass.Ord);
                case "backslash": return CharNode('\\', MathClass.Ord);
                case "dots": return DotsNode();
                case "ce": case "pu":
                    m_At = position;
                    Fail("\\" + name + " is not supported in this version.");
                    return -1;
                case "sin": case "cos": case "tan": case "cot": case "sec": case "csc": case "arcsin": case "arccos": case "arctan": case "sinh": case "cosh": case "tanh": case "coth": case "log": case "ln": case "lg": case "exp": case "arg": case "deg": case "dim": case "hom": case "ker": case "mod":
                    return OperatorNode(name, false);
                case "lim": case "liminf": case "limsup": case "sup": case "inf": case "min": case "max": case "det": case "gcd": case "Pr":
                    return OperatorNode(name, true);
            }
            if (LetteraMathSymbols.TryFind(name, out int cp, out MathClass cls))
                return CharNode(cp, cls);
            m_At = position;
            if (LetteraMath.TryExtension(name, out string replacement))
            {
                m_At = after;
                int text = NewNode(MathNodeKind.Text);
                m_Tree.Nodes[text].Text = m_Tree.Keep(replacement ?? "");
                return text;
            }
            Fail("There is no command \\" + name + ".");
            return -1;
        }

        int DotsNode()
        {
            // Between operators and relations the dots ride on the axis.
            SkipSpace();
            bool operatorNext = m_At < m_Source.Length && (m_Source[m_At] == '+' || m_Source[m_At] == '-' || m_Source[m_At] == '=' || m_Source[m_At] == '<' || m_Source[m_At] == '>');
            return CharNode(operatorNext ? 0x22EF : 0x2026, MathClass.Ord);
        }

        int Fraction(int style, int continued)
        {
            int node = NewNode(MathNodeKind.Fraction);
            int num = ParseArgument();
            int den = ParseArgument();
            ref MathNode n = ref m_Tree.Nodes[node];
            n.A = num;
            n.B = den;
            n.Info = style;
            n.Cp = continued;
            n.Class = MathClass.Inner;
            return node;
        }

        int Binomial(int style)
        {
            int node = NewNode(MathNodeKind.Binomial);
            int top = ParseArgument();
            int bottom = ParseArgument();
            ref MathNode n = ref m_Tree.Nodes[node];
            n.A = top;
            n.B = bottom;
            n.Info = style;
            n.Class = MathClass.Inner;
            return node;
        }

        int Radical()
        {
            int node = NewNode(MathNodeKind.Radical);
            int degree = -1;
            SkipSpace();
            if (m_At < m_Source.Length && m_Source[m_At] == '[')
            {
                m_At++;
                degree = ParseList(EndOfOptional);
                if (!Failed)
                {
                    if (m_At >= m_Source.Length || m_Source[m_At] != ']') Fail("A radical's degree with no closing bracket.");
                    else m_At++;
                }
            }
            int body = ParseArgument();
            ref MathNode n = ref m_Tree.Nodes[node];
            n.A = body;
            n.B = degree;
            return node;
        }

        int StyleNode(MathStyle style)
        {
            int node = NewNode(MathNodeKind.Style);
            m_Tree.Nodes[node].Info = (int)style;
            return node;
        }

        int SizeNode(float scale)
        {
            int node = NewNode(MathNodeKind.Size);
            m_Tree.Nodes[node].Value = scale;
            return node;
        }

        bool ReadDelimiter(out int cp)
        {
            cp = 0;
            SkipSpace();
            if (m_At >= m_Source.Length)
            {
                Fail("A delimiter is missing at the end of this formula.");
                return false;
            }
            char c = m_Source[m_At];
            if (c == '.')
            {
                m_At++;
                return true;
            }
            if (c == '\\')
            {
                string name = PeekCommand(out int after);
                m_At = after;
                switch (name)
                {
                    case "{": case "lbrace": cp = '{'; return true;
                    case "}": case "rbrace": cp = '}'; return true;
                    case "|": case "Vert": case "lVert": case "rVert": cp = 0x2225; return true;
                    case "vert": case "lvert": case "rvert": cp = '|'; return true;
                    case "backslash": cp = '\\'; return true;
                }
                if (LetteraMathSymbols.TryFind(name, out cp, out _)) return true;
                Fail("\\" + name + " is no delimiter.");
                return false;
            }
            if (c == '<')
            {
                m_At++;
                cp = 0x27E8;
                return true;
            }
            if (c == '>')
            {
                m_At++;
                cp = 0x27E9;
                return true;
            }
            cp = ReadCodepoint();
            return true;
        }

        int Delimited()
        {
            int node = NewNode(MathNodeKind.Delimited);
            if (!ReadDelimiter(out int left)) return node;
            m_Tree.Nodes[node].Cp = left;
            m_Tree.Nodes[node].Class = MathClass.Inner;
            int segment = -1;
            while (!Failed)
            {
                int list = ParseList(EndOfDelimited);
                if (Failed) break;
                int seg = NewNode(MathNodeKind.Segment);
                m_Tree.Nodes[seg].A = list;
                if (segment < 0) m_Tree.Nodes[node].A = seg;
                else m_Tree.Nodes[segment].Next = seg;
                segment = seg;
                SkipSpace();
                if (m_At >= m_Source.Length)
                {
                    Fail("A \\left with no \\right.");
                    break;
                }
                string name = PeekCommand(out int after);
                if (m_Source[m_At] != '\\' || (name != "right" && name != "middle"))
                {
                    Fail("A \\left with no \\right.");
                    break;
                }
                m_At = after;
                if (!ReadDelimiter(out int delimiter)) break;
                if (name == "right")
                {
                    m_Tree.Nodes[node].Cp2 = delimiter;
                    break;
                }
                m_Tree.Nodes[seg].Cp = delimiter;
            }
            return node;
        }

        int BigDelimiter(float size, MathClass cls)
        {
            int node = NewNode(MathNodeKind.BigDelimiter);
            if (!ReadDelimiter(out int cp)) return node;
            ref MathNode n = ref m_Tree.Nodes[node];
            n.Cp = cp;
            n.Value = size;
            n.Class = cls;
            return node;
        }

        int Accent(int cp, bool wide)
        {
            int node = NewNode(MathNodeKind.Accent);
            int body = ParseArgument();
            ref MathNode n = ref m_Tree.Nodes[node];
            n.A = body;
            n.Cp = cp;
            n.Flags = wide ? FlagWide : (byte)0;
            n.Class = BodyClass(body);
            return node;
        }

        MathClass BodyClass(int node)
        {
            if (node < 0) return MathClass.Ord;
            ref MathNode n = ref m_Tree.Nodes[node];
            if (n.Kind == MathNodeKind.List)
            {
                int first = n.A;
                return first >= 0 && m_Tree.Nodes[first].Next < 0 ? m_Tree.Nodes[first].Class : MathClass.Ord;
            }
            return n.Class;
        }

        int Bar(int which)
        {
            int node = NewNode(MathNodeKind.Bar);
            int body = ParseArgument();
            ref MathNode n = ref m_Tree.Nodes[node];
            n.A = body;
            n.Info = which;
            n.Class = BodyClass(body);
            return node;
        }

        int Stretched(int cp, bool over, MathClass cls)
        {
            int node = NewNode(MathNodeKind.Stretched);
            int body = ParseArgument();
            ref MathNode n = ref m_Tree.Nodes[node];
            n.A = body;
            n.Cp = cp;
            n.Info = over ? 0 : 1;
            n.Class = cls;
            return node;
        }

        int Arrow(int cp)
        {
            int node = NewNode(MathNodeKind.Arrow);
            int below = -1;
            SkipSpace();
            if (m_At < m_Source.Length && m_Source[m_At] == '[')
            {
                m_At++;
                below = ParseList(EndOfOptional);
                if (!Failed)
                {
                    if (m_At >= m_Source.Length || m_Source[m_At] != ']') Fail("An arrow's text with no closing bracket.");
                    else m_At++;
                }
            }
            int above = ParseArgument();
            ref MathNode n = ref m_Tree.Nodes[node];
            n.A = above;
            n.B = below;
            n.Cp = cp;
            n.Class = MathClass.Rel;
            return node;
        }

        int Stack(int which)
        {
            int node = NewNode(MathNodeKind.Stack);
            int annotation = ParseArgument();
            int body = ParseArgument();
            ref MathNode n = ref m_Tree.Nodes[node];
            n.A = annotation;
            n.B = body;
            n.Info = which;
            n.Class = which == 2 ? MathClass.Rel : BodyClass(body);
            return node;
        }

        int SubStack()
        {
            SkipSpace();
            if (m_At >= m_Source.Length || m_Source[m_At] != '{')
            {
                Fail("\\substack needs a group.");
                return -1;
            }
            m_At++;
            int table = Rows("substack", null, EndOfGroup);
            if (!Failed)
            {
                SkipSpace();
                if (m_At >= m_Source.Length || m_Source[m_At] != '}') Fail("\\substack with no closing brace.");
                else m_At++;
            }
            return table;
        }

        int TextNode(int style)
        {
            string text = ReadRawGroup();
            int node = NewNode(MathNodeKind.Text);
            ref MathNode n = ref m_Tree.Nodes[node];
            n.Text = m_Tree.Keep(text ?? "");
            n.Info = style;
            return node;
        }

        // What a group holds, as it is: nothing in it means anything here. Linear by a brace count.
        string ReadRawGroup()
        {
            SkipSpace();
            if (m_At >= m_Source.Length || m_Source[m_At] != '{')
            {
                Fail("A group is missing here.");
                return null;
            }
            int start = ++m_At, depth = 1;
            while (m_At < m_Source.Length)
            {
                char c = m_Source[m_At];
                if (c == '\\' && m_At + 1 < m_Source.Length) m_At++;
                else if (c == '{') depth++;
                else if (c == '}' && --depth == 0)
                {
                    string inner = m_Source.Substring(start, m_At - start);
                    m_At++;
                    return inner;
                }
                m_At++;
            }
            Fail("An opening brace with no closing one.");
            return null;
        }

        int OperatorName()
        {
            bool starred = m_At < m_Source.Length && m_Source[m_At] == '*';
            if (starred) m_At++;
            string name = ReadRawGroup();
            return OperatorNode(name ?? "", starred);
        }

        int OperatorNode(string name, bool limits)
        {
            int node = NewNode(MathNodeKind.Operator);
            ref MathNode n = ref m_Tree.Nodes[node];
            n.Text = m_Tree.Keep(name);
            n.Class = MathClass.Op;
            n.Flags = limits ? FlagDisplayLimits : FlagNoLimits;
            return node;
        }

        int Wrap(MathNodeKind kind, int info)
        {
            int node = NewNode(kind);
            int body = ParseArgument();
            ref MathNode n = ref m_Tree.Nodes[node];
            n.A = body;
            n.Info = info;
            n.Class = kind == MathNodeKind.Not ? MathClass.Rel : BodyClass(body);
            return node;
        }

        int TextColor()
        {
            string color = ReadRawGroup();
            int node = NewNode(MathNodeKind.Color);
            int body = ParseArgument();
            ref MathNode n = ref m_Tree.Nodes[node];
            n.Text = m_Tree.Keep(color ?? "");
            n.A = body;
            n.Class = BodyClass(body);
            return node;
        }

        // To the end of the group it stands in.
        int ColorRest(int end)
        {
            string color = ReadRawGroup();
            int node = NewNode(MathNodeKind.Color);
            int rest = ParseList(end == EndOfSource ? EndOfSource : EndOfRest);
            ref MathNode n = ref m_Tree.Nodes[node];
            n.Text = m_Tree.Keep(color ?? "");
            n.A = rest;
            return node;
        }

        float ReadBracedLength()
        {
            string text = ReadRawGroup();
            if (text == null) return 0;
            if (!TryLength(text, out float em))
            {
                Fail("Cannot read '" + (text.Length > 16 ? text.Substring(0, 16) : text) + "' as a length.");
                return 0;
            }
            return em;
        }

        /// <summary>A length in em. pt is counted against a 10 pt body, px as 1/16 em, and mu as 1/18 em.</summary>
        internal static bool TryLength(string text, out float em)
        {
            em = 0;
            text = text.Trim();
            int digits = 0;
            while (digits < text.Length && (char.IsDigit(text[digits]) || text[digits] == '.' || text[digits] == '-' || text[digits] == '+')) digits++;
            if (digits == 0 || digits > 16) return false;
            if (!float.TryParse(text.Substring(0, digits), NumberStyles.Float, CultureInfo.InvariantCulture, out float value) || float.IsNaN(value) || float.IsInfinity(value)) return false;
            switch (text.Substring(digits).Trim())
            {
                case "em": em = value; break;
                case "ex": em = value * 0.5f; break;
                case "mu": em = value / 18; break;
                case "px": em = value / 16; break;
                case "pt": em = value / 10; break;
                default: return false;
            }
            em = Clamp(em);
            return true;
        }

        int Environment()
        {
            string name = ReadRawGroup();
            if (Failed || name == null) return -1;
            string columns = null;
            switch (name)
            {
                case "array":
                case "alignedat":
                    columns = ReadRawGroup();
                    if (columns == null) return -1;
                    break;
                case "matrix": case "pmatrix": case "bmatrix": case "Bmatrix": case "vmatrix": case "Vmatrix": case "smallmatrix":
                case "cases": case "dcases": case "aligned": case "align": case "align*": case "split":
                case "gather": case "gather*": case "gathered": case "multline": case "multline*":
                case "equation": case "equation*": case "displaymath":
                    break;
                default:
                    Fail("There is no environment '" + (name.Length > 32 ? name.Substring(0, 32) : name) + "'.");
                    return -1;
            }
            int table = Rows(name, columns, EndOfCell);
            if (Failed) return table;
            SkipSpace();
            if (m_At >= m_Source.Length || m_Source[m_At] != '\\' || PeekCommand(out int after) != "end")
            {
                Fail("A \\begin{" + name + "} with no \\end.");
                return table;
            }
            m_At = after;
            string closing = ReadRawGroup();
            if (!Failed && closing != name) Fail("\\end{" + closing + "} closes \\begin{" + name + "}.");
            return table;
        }

        int Rows(string name, string columns, int end)
        {
            int table = NewNode(MathNodeKind.Table);
            m_Tree.Nodes[table].Text = m_Tree.Keep(name);
            m_Tree.Nodes[table].Cp = columns != null ? m_Tree.Keep(columns) : -1;
            m_Tree.Nodes[table].Class = MathClass.Inner;
            bool wasTable = m_InTable;
            m_InTable = true;
            int lastRow = -1, rows = 0, cells = 0;
            while (!Failed)
            {
                int rule = 0;
                ReadHorizontalRules(ref rule);
                int row = NewNode(MathNodeKind.Row);
                m_Tree.Nodes[row].Info = rule;
                if (lastRow < 0) m_Tree.Nodes[table].A = row;
                else m_Tree.Nodes[lastRow].Next = row;
                lastRow = row;
                if (++rows > LetteraMathLimits.Rows)
                {
                    Fail("A table with more than " + LetteraMathLimits.Rows + " rows.");
                    break;
                }
                int lastCell = -1, column = 0;
                while (!Failed)
                {
                    int list = ParseList(EndOfCell);
                    if (Failed) break;
                    int cell = NewNode(MathNodeKind.Cell);
                    m_Tree.Nodes[cell].A = list;
                    if (lastCell < 0) m_Tree.Nodes[row].A = cell;
                    else m_Tree.Nodes[lastCell].Next = cell;
                    lastCell = cell;
                    if (++column > LetteraMathLimits.Columns)
                    {
                        Fail("A table with more than " + LetteraMathLimits.Columns + " columns.");
                        break;
                    }
                    if (++cells > LetteraMathLimits.Cells)
                    {
                        Fail("A table with more than " + LetteraMathLimits.Cells + " cells.");
                        break;
                    }
                    SkipSpace();
                    if (m_At < m_Source.Length && m_Source[m_At] == '&')
                    {
                        m_At++;
                        continue;
                    }
                    break;
                }
                if (Failed) break;
                SkipSpace();
                if (m_At < m_Source.Length && m_Source[m_At] == '\\' && PeekCommand(out int after) == "\\")
                {
                    m_At = after;
                    SkipSpace();
                    if (m_At < m_Source.Length && m_Source[m_At] == '[')
                    {
                        int close = m_Source.IndexOf(']', m_At, Math.Min(32, m_Source.Length - m_At));
                        if (close < 0)
                        {
                            Fail("A row's extra space with no closing bracket.");
                            break;
                        }
                        if (TryLength(m_Source.Substring(m_At + 1, close - m_At - 1), out float extra)) m_Tree.Nodes[row].Value = extra;
                        m_At = close + 1;
                    }
                    continue;
                }
                break;
            }
            m_InTable = wasTable;
            return table;
        }

        void ReadHorizontalRules(ref int rule)
        {
            while (!Failed)
            {
                SkipSpace();
                if (m_At >= m_Source.Length || m_Source[m_At] != '\\') return;
                string name = PeekCommand(out int after);
                if (name == "hline") rule |= RowHline;
                else if (name == "hdashline") rule |= RowDashed;
                else return;
                m_At = after;
            }
        }
    }

    internal static partial class LetteraMathSymbols
    {
        static Dictionary<string, (int cp, MathClass cls)> s_ByName;
        static Dictionary<int, MathClass> s_ByCp;

        /// <summary>Whether the table was read, for a probe that a text with no formula costs nothing.</summary>
        internal static bool Loaded => s_ByName != null;

        static void Load()
        {
            if (s_ByName != null) return;
            s_ByName = new Dictionary<string, (int, MathClass)>(Count);
            s_ByCp = new Dictionary<int, MathClass>(Count);
            foreach (string row in Table.Split(';'))
            {
                string[] parts = row.Split(' ');
                int cp = int.Parse(parts[1], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                MathClass cls = parts[2][0] switch
                {
                    'P' => MathClass.Op,
                    'B' => MathClass.Bin,
                    'R' => MathClass.Rel,
                    'A' => MathClass.Open,
                    'C' => MathClass.Close,
                    'U' => MathClass.Punct,
                    _ => MathClass.Ord,
                };
                s_ByName[parts[0]] = (cp, cls);
                if (!s_ByCp.ContainsKey(cp)) s_ByCp[cp] = cls;
            }
        }

        internal static bool TryFind(string name, out int cp, out MathClass cls)
        {
            Load();
            if (s_ByName.TryGetValue(name, out (int cp, MathClass cls) found))
            {
                cp = found.cp;
                cls = found.cls;
                return true;
            }
            cp = 0;
            cls = MathClass.Ord;
            return false;
        }

        internal static MathClass ClassOf(int cp)
        {
            Load();
            return s_ByCp.TryGetValue(cp, out MathClass cls) ? cls : MathClass.Ord;
        }

        internal static IEnumerable<string> Names
        {
            get
            {
                Load();
                return s_ByName.Keys;
            }
        }
    }
}

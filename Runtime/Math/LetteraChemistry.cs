#if !LETTERA_NO_MATH
using System.Text;

namespace FirstSymbol.Lettera
{
    /// <summary>
    /// Chemistry and quantities, \ce{...} and \pu{...} in the mhchem notation, rewritten into the notation the formula parser reads.
    /// One forward pass over what it is given, with every bracket matched once beforehand, so its cost and its output stay linear in it.
    /// </summary>
    internal sealed class LetteraChemistry
    {
        /// <summary>Arrows' texts inside arrows' texts.</summary>
        internal const int MaxNesting = 8;

        /// <summary>What a chemistry arrow is at least, in em at the formula's size, so a bare arrow reads as a reaction and not as a minus.</summary>
        internal const float ArrowEm = 1.6f;

        readonly string m_Text;
        readonly int[] m_Match;
        readonly StringBuilder m_Out;
        string m_Error;
        int m_ErrorAt;

        LetteraChemistry(string text)
        {
            m_Text = text;
            m_Match = new int[text.Length];
            m_Out = new StringBuilder(text.Length * 4 + 16);
        }

        /// <summary>The formula notation for what \ce (or, with quantity, \pu) holds. Null with an error and its place in the content when it cannot be read.</summary>
        internal static string Translate(string content, bool quantity, out string error, out int errorAt)
        {
            var chemistry = new LetteraChemistry(content ?? "");
            if (chemistry.MatchBrackets())
            {
                if (quantity) chemistry.Quantity(0, chemistry.m_Text.Length);
                else chemistry.Equation(0, chemistry.m_Text.Length, 0);
            }
            error = chemistry.m_Error;
            errorAt = chemistry.m_ErrorAt;
            return error == null ? chemistry.m_Out.ToString() : null;
        }

        void Fail(string message, int at)
        {
            if (m_Error != null) return;
            m_Error = message;
            m_ErrorAt = at;
        }

        // Every brace, bracket and dollar finds its partner in one pass: -1 for none. Inside $...$ nothing pairs but braces.
        bool MatchBrackets()
        {
            string s = m_Text;
            var braces = new int[s.Length + 1];
            var brackets = new int[s.Length + 1];
            var dollars = new int[s.Length + 1];
            var dollarDepths = new int[s.Length + 1];
            int braceTop = 0, bracketTop = 0, dollarTop = 0;
            for (int i = 0; i < s.Length; i++)
            {
                m_Match[i] = -1;
                char c = s[i];
                if (c == '\\')
                {
                    if (i + 1 < s.Length) m_Match[++i] = -1;
                    continue;
                }
                // A $ closes the one opened at its own depth of braces: \ce{A $\ce{B $x$}$} holds math inside math.
                if (c == '$')
                {
                    if (dollarTop > 0 && dollarDepths[dollarTop - 1] == braceTop)
                    {
                        int open = dollars[--dollarTop];
                        m_Match[open] = i;
                        m_Match[i] = open;
                    }
                    else
                    {
                        dollars[dollarTop] = i;
                        dollarDepths[dollarTop++] = braceTop;
                    }
                    continue;
                }
                if (c == '{') braces[braceTop++] = i;
                else if (c == '}')
                {
                    if (braceTop == 0)
                    {
                        Fail("A closing brace with no opening one.", i);
                        return false;
                    }
                    if (dollarTop > 0 && dollarDepths[dollarTop - 1] == braceTop)
                    {
                        Fail("A $ with no closing one.", dollars[dollarTop - 1]);
                        return false;
                    }
                    int open = braces[--braceTop];
                    m_Match[open] = i;
                    m_Match[i] = open;
                }
                else if (dollarTop == 0 && c == '[') brackets[bracketTop++] = i;
                else if (dollarTop == 0 && c == ']' && bracketTop > 0)
                {
                    int open = brackets[--bracketTop];
                    m_Match[open] = i;
                    m_Match[i] = open;
                }
            }
            if (dollarTop > 0) Fail("A $ with no closing one.", dollars[dollarTop - 1]);
            else if (braceTop > 0) Fail("An opening brace with no closing one.", braces[braceTop - 1]);
            return m_Error == null;
        }

        static bool IsSpace(char c) => c == ' ' || c == '\t' || c == '\n' || c == '\r' || c == '~';
        static bool IsDigit(char c) => c >= '0' && c <= '9';
        static bool IsUpper(char c) => c >= 'A' && c <= 'Z';
        static bool IsLower(char c) => c >= 'a' && c <= 'z';
        static bool IsAsciiLetter(char c) => IsUpper(c) || IsLower(c);
        static bool IsLetter(char c) => IsAsciiLetter(c) || (c > 0x7F && char.IsLetter(c)) || c == 0xB0 || c == 0x2030 || c == 0x2032 || c == 0x2033;

        // ---------------------------------------------------------------- \ce

        enum Token : byte { None, Operator, Word, Number }

        // A reaction: words, operators and arrows, separated by spaces.
        void Equation(int from, int to, int depth)
        {
            if (depth > MaxNesting)
            {
                Fail("Arrows' texts nest deeper than " + MaxNesting + " levels.", from);
                return;
            }
            string s = m_Text;
            int at = from;
            Token previous = Token.None;
            while (at < to && m_Error == null)
            {
                if (IsSpace(s[at]))
                {
                    at++;
                    continue;
                }
                int arrow = ArrowLength(at, to);
                if (arrow > 0)
                {
                    at = Arrow(at, arrow, to, depth);
                    previous = Token.Operator;
                    continue;
                }
                int end = WordEnd(at, to);
                if (end <= at) end = at + 1;
                string single = end - at <= 3 ? s.Substring(at, end - at) : null;
                switch (single)
                {
                    case "+":
                    case "-":
                    case "=":
                        m_Out.Append(single == "-" ? "-" : single).Append(' ');
                        previous = Token.Operator;
                        at = end;
                        continue;
                    case "*":
                    case ".":
                        m_Out.Append("\\cdot ");
                        previous = Token.Operator;
                        at = end;
                        continue;
                }
                if (previous == Token.Word || previous == Token.Number) m_Out.Append("\\,");
                switch (single)
                {
                    case "v": m_Out.Append("\\downarrow "); previous = Token.Word; break;
                    case "^": m_Out.Append("\\uparrow "); previous = Token.Word; break;
                    case "(v)": m_Out.Append("(\\downarrow)"); previous = Token.Word; break;
                    case "(^)": m_Out.Append("(\\uparrow)"); previous = Token.Word; break;
                    case "...": m_Out.Append("\\ldots "); previous = Token.Word; break;
                    default:
                        previous = Word(at, end) ? Token.Number : Token.Word;
                        break;
                }
                at = end;
            }
        }

        // ->, <-, <->, <-->, <=>, <=>>, <<=>, longest first.
        int ArrowLength(int at, int to)
        {
            string s = m_Text;
            bool Is(string arrow) => at + arrow.Length <= to && string.CompareOrdinal(s, at, arrow, 0, arrow.Length) == 0;
            if (Is("<-->")) return 4;
            if (Is("<=>>")) return 4;
            if (Is("<<=>")) return 4;
            if (Is("<->")) return 3;
            if (Is("<=>")) return 3;
            if (Is("->")) return 2;
            if (Is("<-")) return 2;
            return 0;
        }

        int Arrow(int at, int length, int to, int depth)
        {
            string s = m_Text;
            string arrow = s.Substring(at, length);
            int next = at + length;
            int aboveFrom = -1, aboveTo = -1, belowFrom = -1, belowTo = -1;
            for (int argument = 0; argument < 2 && next < to && s[next] == '['; argument++)
            {
                int close = m_Match[next];
                if (close < 0 || close >= to)
                {
                    Fail("An arrow's text with no closing bracket.", next);
                    return to;
                }
                if (argument == 0)
                {
                    aboveFrom = next + 1;
                    aboveTo = close;
                }
                else
                {
                    belowFrom = next + 1;
                    belowTo = close;
                }
                next = close + 1;
            }
            string command;
            switch (arrow)
            {
                case "->": command = "\\xrightarrow"; break;
                case "<-": command = "\\xleftarrow"; break;
                case "<->": command = "\\xleftrightarrow"; break;
                case "<-->": command = "\\xrightleftarrows"; break;
                default: command = "\\xrightleftharpoons"; break;
            }
            m_Out.Append(command);
            if (belowFrom >= 0)
            {
                m_Out.Append("[{");
                Equation(belowFrom, belowTo, depth + 1);
                m_Out.Append("}]");
            }
            m_Out.Append('{');
            if (aboveFrom >= 0) Equation(aboveFrom, aboveTo, depth + 1);
            m_Out.Append("} ");
            return next;
        }

        // A word runs to a space or an arrow, over groups and math whole.
        int WordEnd(int at, int to)
        {
            string s = m_Text;
            int i = at;
            while (i < to)
            {
                char c = s[i];
                if (IsSpace(c)) break;
                if (i > at && ArrowLength(i, to) > 0) break;
                if (c == '\\')
                {
                    i += 2;
                    continue;
                }
                if ((c == '{' || c == '$') && m_Match[i] > i)
                {
                    i = m_Match[i] + 1;
                    continue;
                }
                i++;
            }
            return i > to ? to : i;
        }

        // Scripts wait for their base's last script, so a subscript and a charge stand one above the other.
        string m_Sub, m_Sup;
        bool m_HasBase;

        void Flush()
        {
            if (m_Sub != null) m_Out.Append("_{").Append(m_Sub).Append('}');
            if (m_Sup != null) m_Out.Append("^{").Append(m_Sup).Append('}');
            m_Sub = m_Sup = null;
            m_HasBase = false;
        }

        void NewBase()
        {
            Flush();
            m_HasBase = true;
        }

        void AddSub(string sub)
        {
            if (!m_HasBase || m_Sub != null)
            {
                NewBase();
                m_Out.Append("{}");
            }
            m_Sub = sub;
        }

        void AddSup(string sup)
        {
            // A long run of marks starts a base of its own, so the marks are never copied over and over.
            if (!m_HasBase || (m_Sup != null && m_Sup.Length > 64))
            {
                NewBase();
                m_Out.Append("{}");
            }
            m_Sup = m_Sup == null ? sup : m_Sup + sup;
        }

        // One formula with its amount: 2H2O, ^{227}_{90}Th+, [AgCl2]-, C6H5-CHO. True when it was only a number.
        bool Word(int from, int to)
        {
            string s = m_Text;
            int at = from;
            // A single lowercase letter on its own is a variable, as in x Na(NH4)HPO4.
            if (to - from == 1 && IsLower(s[from]))
            {
                m_Out.Append(s[from]).Append(' ');
                return false;
            }
            bool begin = true;
            m_HasBase = false;
            m_Sub = m_Sup = null;
            while (at < to && m_Error == null)
            {
                char c = s[at];
                if (begin)
                {
                    begin = false;
                    int number = Amount(at, to);
                    if (number > at)
                    {
                        if (number == to)
                        {
                            Flush();
                            return from == at;
                        }
                        m_Out.Append("\\,");
                        at = number;
                        continue;
                    }
                    if (c == '^' || c == '_')
                    {
                        at = Prescripts(at, to);
                        continue;
                    }
                }
                if (IsAsciiLetter(c))
                {
                    int end = at;
                    while (end < to && IsAsciiLetter(s[end])) end++;
                    NewBase();
                    m_Out.Append("\\mathrm{").Append(s, at, end - at).Append('}');
                    at = end;
                    continue;
                }
                if (IsDigit(c))
                {
                    int end = at;
                    while (end < to && IsDigit(s[end])) end++;
                    if (m_HasBase && m_Sub == null) m_Sub = s.Substring(at, end - at);
                    else
                    {
                        NewBase();
                        m_Out.Append(s, at, end - at);
                    }
                    at = end;
                    continue;
                }
                if (c == '+' || c == '-')
                {
                    int end = at;
                    while (end < to && (s[end] == '+' || s[end] == '-')) end++;
                    bool last = end == to || s[end] == '(' || s[end] == '^' || s[end] == '_';
                    if (c == '+' || last)
                    {
                        AddSup(Signs(at, end));
                        at = end;
                        continue;
                    }
                    Bond("{-}");
                    at++;
                    continue;
                }
                switch (c)
                {
                    case '=':
                        Bond("{=}");
                        at++;
                        continue;
                    case '#':
                        Bond("{\\equiv}");
                        at++;
                        continue;
                    case '^':
                    case '_':
                    {
                        at++;
                        string script = ScriptArgument(ref at, to);
                        if (c == '^') AddSup(script);
                        else AddSub(script);
                        continue;
                    }
                    case '*':
                        Flush();
                        m_Out.Append("\\cdot ");
                        begin = true;
                        at++;
                        continue;
                    case '.':
                        if (at + 1 < to && (IsDigit(s[at + 1]) || IsUpper(s[at + 1]) || s[at + 1] == '(' || s[at + 1] == '['))
                        {
                            Flush();
                            m_Out.Append("\\cdot ");
                            begin = true;
                        }
                        else AddSup("\\bullet ");
                        at++;
                        continue;
                    case '(':
                    case '[':
                        NewBase();
                        m_Out.Append(c);
                        m_HasBase = false;
                        at++;
                        continue;
                    case ')':
                    case ']':
                        NewBase();
                        m_Out.Append(c);
                        at++;
                        continue;
                    case '\'':
                        AddSup("\\prime ");
                        at++;
                        continue;
                    case '{':
                        at = Group(at);
                        continue;
                    case '$':
                        NewBase();
                        m_Out.Append('{').Append(s, at + 1, m_Match[at] - at - 1).Append('}');
                        at = m_Match[at] + 1;
                        continue;
                    case '\\':
                        at = Command(at, to);
                        continue;
                }
                NewBase();
                Literal(c);
                at++;
            }
            Flush();
            return false;
        }

        void Bond(string bond)
        {
            Flush();
            m_Out.Append(bond);
        }

        string Signs(int from, int to)
        {
            var signs = new StringBuilder((to - from) * 3);
            for (int i = from; i < to; i++) signs.Append(m_Text[i] == '+' ? "{+}" : "{-}");
            return signs.ToString();
        }

        void Literal(char c)
        {
            switch (c)
            {
                case '%': case '&': case '#': m_Out.Append('\\').Append(c); return;
                default: m_Out.Append(c); return;
            }
        }

        // A stoichiometric number at a word's start: 2, 0.5, 1/2, written whole. Returns where it ends, or where it began for none.
        int Amount(int at, int to)
        {
            string s = m_Text;
            int i = at;
            while (i < to && IsDigit(s[i])) i++;
            if (i == at) return at;
            int whole = i;
            string fraction = null;
            if (i + 1 < to && (s[i] == '.' || s[i] == ',') && IsDigit(s[i + 1]))
            {
                i++;
                while (i < to && IsDigit(s[i])) i++;
            }
            else if (i + 1 < to && s[i] == '/' && IsDigit(s[i + 1]))
            {
                int start = ++i;
                while (i < to && IsDigit(s[i])) i++;
                fraction = s.Substring(start, i - start);
            }
            // Digits before a charge or a script belong to the formula: they are no amount.
            if (i < to && (s[i] == '+' || s[i] == '-' || s[i] == '^' || s[i] == '_')) return at;
            if (fraction != null) m_Out.Append("\\tfrac{").Append(s, at, whole - at).Append("}{").Append(fraction).Append('}');
            else for (int k = at; k < i; k++) m_Out.Append(s[k] == ',' ? "{,}" : s[k].ToString());
            return i;
        }

        // ^{227}_{90}Th: an isotope's numbers before its symbol, set flush right against it.
        int Prescripts(int at, int to)
        {
            string s = m_Text;
            string sup = null, sub = null;
            int supLength = 0, subLength = 0;
            while (at < to && (s[at] == '^' || s[at] == '_'))
            {
                bool up = s[at] == '^';
                at++;
                int start = at;
                string script = ScriptArgument(ref at, to);
                int length = Shown(start, at);
                if (up && sup == null)
                {
                    sup = script;
                    supLength = length;
                }
                else if (!up && sub == null)
                {
                    sub = script;
                    subLength = length;
                }
                else break;
            }
            string padSup = supLength < subLength ? new string('0', subLength - supLength) : null;
            string padSub = subLength < supLength ? new string('0', supLength - subLength) : null;
            m_Out.Append("{}");
            if (sup != null) m_Out.Append("^{").Append(padSup != null ? "\\phantom{" + padSup + "}" : "").Append(sup).Append('}');
            if (sub != null) m_Out.Append("_{").Append(padSub != null ? "\\phantom{" + padSub + "}" : "").Append(sub).Append('}');
            return at;
        }

        // How many characters a script shows, braces aside, for flush-right isotope numbers.
        int Shown(int from, int to)
        {
            int count = 0;
            for (int i = from; i < to; i++)
                if (m_Text[i] != '{' && m_Text[i] != '}')
                    count++;
            return count;
        }

        // After ^ or _: a group, math, or a run as mhchem reads it: a sign then digits (_-1), or digits then signs (^2-), or a radical's dot.
        string ScriptArgument(ref int at, int to)
        {
            string s = m_Text;
            if (at >= to)
            {
                Fail("A script with nothing after it.", at);
                return "";
            }
            char c = s[at];
            if (c == '{')
            {
                int close = m_Match[at];
                string inner = ScriptContent(at + 1, close, 0);
                at = close + 1;
                return inner;
            }
            if (c == '$')
            {
                int close = m_Match[at];
                string math = "{" + s.Substring(at + 1, close - at - 1) + "}";
                at = close + 1;
                return math;
            }
            int start = at;
            bool leadingSign = false;
            while (at < to && (s[at] == '+' || s[at] == '-'))
            {
                at++;
                leadingSign = true;
            }
            while (at < to && IsDigit(s[at])) at++;
            if (at < to && s[at] == '.') at++;
            if (!leadingSign)
            {
                // A sign that a formula follows is a bond, as in \eta^2-C2H4: only one at the end is a charge.
                int signs = at;
                while (signs < to && (s[signs] == '+' || s[signs] == '-')) signs++;
                if (signs == to || !(IsAsciiLetter(s[signs]) || s[signs] == '(' || s[signs] == '[' || s[signs] == '\\')) at = signs;
            }
            if (at == start)
            {
                if (c == '\\') at = SkipCommand(at, to);
                else at++;
            }
            return ScriptContent(start, at, 0);
        }

        // A script's characters: numbers as they are, signs as ordinary minus and plus, a dot as a radical's, a lone letter as a variable, words upright.
        string ScriptContent(int from, int to, int depth)
        {
            string s = m_Text;
            if (depth > MaxNesting)
            {
                Fail("A script's groups nest deeper than " + MaxNesting + " levels.", from);
                return "";
            }
            var text = new StringBuilder((to - from) * 4 + 4);
            bool variable = to - from == 1 && IsLower(s[from]);
            int at = from;
            while (at < to)
            {
                char c = s[at];
                if (c == '+' || c == '-') text.Append(c == '+' ? "{+}" : "{-}");
                else if (c == '.') text.Append("\\bullet ");
                else if (c == '$' && m_Match[at] > at)
                {
                    text.Append('{').Append(s, at + 1, m_Match[at] - at - 1).Append('}');
                    at = m_Match[at] + 1;
                    continue;
                }
                else if (c == '{' && m_Match[at] > at)
                {
                    text.Append('{').Append(ScriptContent(at + 1, m_Match[at], depth + 1)).Append('}');
                    at = m_Match[at] + 1;
                    continue;
                }
                else if (c == '\\')
                {
                    int end = SkipCommand(at, to);
                    text.Append(s, at, end - at).Append(' ');
                    at = end;
                    continue;
                }
                else if (IsAsciiLetter(c) && !variable)
                {
                    int end = at;
                    while (end < to && IsAsciiLetter(s[end])) end++;
                    text.Append("\\mathrm{").Append(s, at, end - at).Append('}');
                    at = end;
                    continue;
                }
                else if (c == '%' || c == '&' || c == '#') text.Append('\\').Append(c);
                else text.Append(c);
                at++;
            }
            return text.ToString();
        }

        // A command and every group straight after it, as they are: \mu, \text{aq}, \frac{1}{2}.
        int SkipCommand(int at, int to)
        {
            string s = m_Text;
            int end = at + 1;
            if (end < to && IsAsciiLetter(s[end]))
                while (end < to && IsAsciiLetter(s[end]))
                    end++;
            else if (end < to) end++;
            while (end < to && s[end] == '{' && m_Match[end] > end) end = m_Match[end] + 1;
            return end > to ? to : end;
        }

        int Command(int at, int to)
        {
            string s = m_Text;
            int nameEnd = at + 1;
            while (nameEnd < to && IsAsciiLetter(s[nameEnd])) nameEnd++;
            string name = s.Substring(at + 1, nameEnd - at - 1);
            if (name == "bond" && nameEnd < to && s[nameEnd] == '{' && m_Match[nameEnd] > nameEnd)
            {
                int close = m_Match[nameEnd];
                Bond(BondOf(s.Substring(nameEnd + 1, close - nameEnd - 1)));
                return close + 1;
            }
            int end = SkipCommand(at, to);
            NewBase();
            m_Out.Append(s, at, end - at).Append(' ');
            return end;
        }

        static string BondOf(string kind)
        {
            switch (kind.Trim())
            {
                case "-": case "1": return "{-}";
                case "=": case "2": return "{=}";
                case "#": case "3": return "{\\equiv}";
                case "~": return "{\\sim}";
                case "~-": return "{\\simeq}";
                case "~=": case "~--": return "{\\cong}";
                case "-~-": return "{≂}";
                case "...": return "{\\cdots}";
                case "....": return "{\\cdots\\cdot}";
                case "->": return "{\\rightarrow}";
                case "<-": return "{\\leftarrow}";
                default: return "{-}";
            }
        }

        // {...} in a formula: empty, a separator; words, upright text; anything with a command or math, as it is.
        int Group(int at)
        {
            string s = m_Text;
            int close = m_Match[at];
            NewBase();
            string inner = s.Substring(at + 1, close - at - 1);
            if (inner.Length == 0) m_Out.Append("{}");
            else if (inner.IndexOf('\\') >= 0 || inner.IndexOf('$') >= 0) m_Out.Append('{').Append(inner).Append('}');
            else m_Out.Append("\\text{").Append(inner).Append('}');
            return close + 1;
        }

        // ---------------------------------------------------------------- \pu

        // A number, then its units: 123 kJ/mol, 1.2e3 kJ, 12345.6789 m, 123 kJ//mol, 25 °C.
        void Quantity(int from, int to)
        {
            string s = m_Text;
            int at = from;
            while (at < to && IsSpace(s[at])) at++;
            bool number = Number(ref at, to);
            while (at < to && IsSpace(s[at])) at++;
            if (number && at < to && (s[at] == '\\' && Starts(at, to, "\\pm") || Starts(at, to, "+-") || s[at] == '±'))
            {
                at += s[at] == '±' ? 1 : s[at] == '\\' ? 3 : 2;
                m_Out.Append("\\pm ");
                while (at < to && IsSpace(s[at])) at++;
                Number(ref at, to);
                while (at < to && IsSpace(s[at])) at++;
            }
            if (at >= to) return;
            // A degree of angle and its minutes and seconds touch their number; every other unit stands a thin space off.
            if (number && !(s[at] == '°' && (at + 1 >= to || !IsAsciiLetter(s[at + 1]))) && s[at] != '\'' && s[at] != '"') m_Out.Append("\\,");
            int split = -1;
            for (int i = at; i + 1 < to; i++)
            {
                if (s[i] == '{' && m_Match[i] > i)
                {
                    i = m_Match[i];
                    continue;
                }
                if (s[i] == '/' && s[i + 1] == '/')
                {
                    split = i;
                    break;
                }
            }
            if (split < 0)
            {
                Units(at, to);
                return;
            }
            m_Out.Append("\\frac{");
            Units(at, split);
            m_Out.Append("}{");
            Units(split + 2, to);
            m_Out.Append('}');
        }

        bool Starts(int at, int to, string what) => at + what.Length <= to && string.CompareOrdinal(m_Text, at, what, 0, what.Length) == 0;

        // A number, its digits grouped in threes past four, its decimal comma kept from punctuation's space, its power of ten written out.
        bool Number(ref int at, int to)
        {
            string s = m_Text;
            int start = at;
            if (at < to && (s[at] == '-' || s[at] == '+' || s[at] == '−') && at + 1 < to && IsDigit(s[at + 1]))
            {
                m_Out.Append(s[at] == '+' ? "+" : "-");
                at++;
            }
            int whole = at;
            while (at < to && IsDigit(s[at])) at++;
            if (at == whole)
            {
                at = start;
                return false;
            }
            Digits(whole, at, false);
            if (at + 1 < to && (s[at] == '.' || s[at] == ',') && IsDigit(s[at + 1]))
            {
                m_Out.Append(s[at] == ',' ? "{,}" : ".");
                int fraction = ++at;
                while (at < to && IsDigit(s[at])) at++;
                Digits(fraction, at, true);
            }
            if (at < to && (s[at] == 'e' || s[at] == 'E'))
            {
                int power = at + 1;
                if (power < to && (s[power] == '-' || s[power] == '+')) power++;
                if (power < to && IsDigit(s[power]))
                {
                    m_Out.Append(s[at] == 'e' ? "\\cdot " : "\\times ").Append("10^{");
                    int i = at + 1;
                    if (s[i] == '-') m_Out.Append("{-}");
                    if (s[i] == '-' || s[i] == '+') i++;
                    int digits = i;
                    while (i < to && IsDigit(s[i])) i++;
                    m_Out.Append(s, digits, i - digits).Append('}');
                    at = i;
                }
            }
            return true;
        }

        void Digits(int from, int to, bool fraction)
        {
            int count = to - from;
            for (int i = from; i < to; i++)
            {
                m_Out.Append(m_Text[i]);
                int done = i - from + 1;
                if (count >= 5 && done < count && (fraction ? done % 3 == 0 : (count - done) % 3 == 0)) m_Out.Append("\\,");
            }
        }

        // Units: upright, a space between them a thin one, * and . the multiplication dot, / a slash, trailing integers their powers.
        void Units(int from, int to)
        {
            string s = m_Text;
            int at = from;
            bool previousUnit = false, space = false;
            while (at < to && m_Error == null)
            {
                char c = s[at];
                if (IsSpace(c))
                {
                    space = true;
                    at++;
                    continue;
                }
                if (c == '*' || c == '.' || c == '·' || c == '⋅')
                {
                    m_Out.Append("\\cdot ");
                    previousUnit = space = false;
                    at++;
                    continue;
                }
                if (c == '/')
                {
                    m_Out.Append('/');
                    previousUnit = space = false;
                    at++;
                    continue;
                }
                if (previousUnit && space) m_Out.Append("\\,");
                space = false;
                if (c == '\\')
                {
                    int end = SkipCommand(at, to);
                    m_Out.Append("\\mathrm{").Append(s, at, end - at).Append('}');
                    at = end;
                }
                else if (c == '{' && m_Match[at] > at)
                {
                    m_Out.Append("\\mathrm{").Append(s, at + 1, m_Match[at] - at - 1).Append('}');
                    at = m_Match[at] + 1;
                }
                else if (c == '$' && m_Match[at] > at)
                {
                    m_Out.Append('{').Append(s, at + 1, m_Match[at] - at - 1).Append('}');
                    at = m_Match[at] + 1;
                }
                else if (IsLetter(c) || c == '%' || c == 'Ω' || c == 'Å' || c == 'µ')
                {
                    int end = at;
                    while (end < to && (IsLetter(s[end]) || s[end] == '%')) end++;
                    m_Out.Append("\\mathrm{");
                    for (int i = at; i < end; i++)
                    {
                        if (s[i] == '%') m_Out.Append("\\%");
                        else if (s[i] == 'µ') m_Out.Append('μ');
                        else m_Out.Append(s[i]);
                    }
                    m_Out.Append('}');
                    at = end;
                }
                else
                {
                    Literal(c);
                    at++;
                    previousUnit = false;
                    continue;
                }
                // A power: ^2, ^{-1}, or digits straight after the unit, with their sign.
                if (at < to && s[at] == '^')
                {
                    at++;
                    m_Out.Append("^{").Append(ScriptArgument(ref at, to)).Append('}');
                }
                else
                {
                    int power = at;
                    if (power < to && (s[power] == '-' || s[power] == '+')) power++;
                    int digits = power;
                    while (power < to && IsDigit(s[power])) power++;
                    if (power > digits)
                    {
                        m_Out.Append("^{");
                        if (s[at] == '-') m_Out.Append("{-}");
                        m_Out.Append(s, digits, power - digits).Append('}');
                        at = power;
                    }
                }
                previousUnit = true;
            }
        }
    }
}
#endif

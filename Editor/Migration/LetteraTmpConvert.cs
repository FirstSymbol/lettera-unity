using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace FirstSymbol.Lettera.Editor.Migration
{
    /// <summary>What a text in TextMesh Pro's markup becomes. Pure, and linear in what it reads.</summary>
    public static class LetteraTmpMarkup
    {
        /// <summary>The text with tags Lettera's vocabulary would read differently rewritten, and what could not be carried.</summary>
        public static string Convert(string source, List<string> notes)
        {
            if (string.IsNullOrEmpty(source) || source.IndexOf('<') < 0) return source ?? "";
            var result = new StringBuilder(source.Length);
            int at = 0;
            while (at < source.Length)
            {
                int open = source.IndexOf('<', at);
                if (open < 0)
                {
                    result.Append(source, at, source.Length - at);
                    break;
                }
                result.Append(source, at, open - at);
                // No tag runs over a line or past 256 characters: a less-than sign in words is words. So neither search goes further than that.
                int reach = Math.Min(256, source.Length - open - 1);
                int close = reach > 0 ? source.IndexOf('>', open + 1, reach) : -1;
                if (close < 0 || source.IndexOf('\n', open, close - open) >= 0)
                {
                    result.Append('<');
                    at = open + 1;
                    continue;
                }
                string tag = source.Substring(open + 1, close - open - 1);
                result.Append(Tag(tag, notes));
                at = close + 1;
            }
            return result.ToString();
        }

        static string Name(string tag)
        {
            int end = 0;
            while (end < tag.Length && tag[end] != '=' && tag[end] != ' ' && tag[end] != '>') end++;
            return tag.Substring(0, end).ToLowerInvariant();
        }

        static string Value(string tag, string key)
        {
            int at = tag.IndexOf(key + "=", StringComparison.OrdinalIgnoreCase);
            if (at < 0) return null;
            at += key.Length + 1;
            if (at >= tag.Length) return "";
            if (tag[at] == '"')
            {
                int end = tag.IndexOf('"', at + 1);
                return end > at ? tag.Substring(at + 1, end - at - 1) : tag.Substring(at + 1);
            }
            int stop = tag.IndexOf(' ', at);
            return stop > at ? tag.Substring(at, stop - at) : tag.Substring(at);
        }

        static string Tag(string tag, List<string> notes)
        {
            switch (Name(tag))
            {
                case "sprite":
                {
                    string name = Value(tag, "name"), index = Value(tag, "index");
                    if (tag.IndexOf("anim=", StringComparison.OrdinalIgnoreCase) >= 0) notes?.Add("<sprite anim> has no counterpart: the sprite stays still.");
                    if (tag.IndexOf("tint", StringComparison.OrdinalIgnoreCase) >= 0 || tag.IndexOf("color=", StringComparison.OrdinalIgnoreCase) >= 0) notes?.Add("A sprite's tint is not carried: set it on the inline library's entry.");
                    if (!string.IsNullOrEmpty(name)) return "<sprite=" + name + ">";
                    if (!string.IsNullOrEmpty(index)) return "<sprite=" + index + ">";
                    string direct = Value(tag, "sprite");
                    if (!string.IsNullOrEmpty(direct) && tag.StartsWith("sprite=", StringComparison.OrdinalIgnoreCase)) return "<sprite=" + direct + ">";
                    return "<" + tag + ">";
                }
                case "link":
                {
                    string value = Value(tag, "link");
                    if (value == null) return "<" + tag + ">";
                    notes?.Add("<link> marks a range for code here, as it did. It opens nothing unless a project's code says so.");
                    return "<link=" + value + ">";
                }
                case "page":
                    notes?.Add("<page> has no counterpart here and was removed.");
                    return "";
                case "quad":
                case "material":
                case "/material":
                    notes?.Add("<" + Name(tag) + "> needs a choice only you can make, and stays as text.");
                    return "<noparse><" + tag + "></noparse>";
                case "alpha":
                    notes?.Add("<alpha> holds to its own closing tag here, and until the next color tag in TextMesh Pro: check the look.");
                    return "<" + tag + ">";
                default:
                    return "<" + tag + ">";
            }
        }
    }

    /// <summary>A finding of a C# rewrite: what was done or what is left to do, on which line.</summary>
    public struct LetteraTmpNote
    {
        public int Line;
        public string Message;
        public bool Warning;
    }

    /// <summary>
    /// Rewrites a script's uses of TextMesh Pro. Lexical: strings, comments and preprocessor lines are never touched,
    /// and a member is renamed only on a receiver this file's own text shows to hold a TextMesh Pro type.
    /// </summary>
    public static class LetteraTmpScript
    {
        static readonly Dictionary<string, string> Types = new Dictionary<string, string>
        {
            { "TextMeshProUGUI", "LetteraText" },
            { "TextMeshPro", "LetteraWorldText" },
            { "TMP_Text", "LetteraText" },
            { "TMP_FontAsset", "LetteraFontAsset" },
            { "TMP_InputField", "LetteraEditable" },
            { "TMP_Dropdown", "LetteraDropdown" },
        };

        static readonly Dictionary<string, string> Members = new Dictionary<string, string>
        {
            { "text", "Text" },
            { "fontSize", "FontSize" },
            { "fontSizeMin", "MinFontSize" },
            { "fontSizeMax", "MaxFontSize" },
            { "enableAutoSizing", "AutoSize" },
            { "font", "Font" },
            { "enableWordWrapping", "WordWrap" },
            { "overflowMode", "Overflow" },
            { "ForceMeshUpdate", "ForceRebuild" },
            { "bounds", "InkBounds" },
            { "textBounds", "InkBounds" },
        };

        static readonly Dictionary<string, string> Reported = new Dictionary<string, string>
        {
            { "preferredWidth", "Use GetMaxContentWidth()." },
            { "preferredHeight", "Use GetHeightForWidth(width)." },
            { "GetPreferredValues", "Use MeasureText(...)." },
            { "textInfo", "Use Lines and GetGlyph." },
            { "maxVisibleCharacters", "Use a RevealEffect." },
            { "fontStyle", "Use a whole-text style, or tags." },
            { "fontWeight", "Use <b=weight>." },
            { "characterSpacing", "Use <tracking>." },
            { "richText", "Set UseGlobalStylePreset, or Literal." },
            { "onEndEdit", "Use Submitted, and FocusLost for an edit that ends without Enter." },
            { "onSelect", "Use FocusGained on the selectable." },
            { "onDeselect", "Use FocusLost on the selectable." },
            { "margin", "Use Padding: its order is left, bottom, right, top." },
            { "isRightToLeftText", "Use BaseDirection." },
            { "textWrappingMode", "Use WordWrap." },
        };

        static readonly Dictionary<string, (string h, string v)> Alignments = new Dictionary<string, (string, string)>
        {
            { "TopLeft", ("Start", "Top") }, { "Top", ("Center", "Top") }, { "TopRight", ("End", "Top") }, { "TopJustified", ("Justify", "Top") },
            { "Left", ("Start", "Middle") }, { "Center", ("Center", "Middle") }, { "Right", ("End", "Middle") }, { "Justified", ("Justify", "Middle") },
            { "BottomLeft", ("Start", "Bottom") }, { "Bottom", ("Center", "Bottom") }, { "BottomRight", ("End", "Bottom") }, { "BottomJustified", ("Justify", "Bottom") },
            { "MidlineLeft", ("Start", "Middle") }, { "Midline", ("Center", "Middle") }, { "MidlineRight", ("End", "Middle") },
            { "BaselineLeft", ("Start", "Middle") }, { "Baseline", ("Center", "Middle") }, { "BaselineRight", ("End", "Middle") },
        };

        /// <summary>A mark for each character: true where it is code, and false in strings, comments and preprocessor lines.</summary>
        internal static bool[] CodeMask(string s)
        {
            var code = new bool[s.Length];
            int i = 0;
            bool lineStart = true;
            while (i < s.Length)
            {
                char c = s[i];
                char next = i + 1 < s.Length ? s[i + 1] : '\0';
                if (c == '/' && next == '/')
                {
                    while (i < s.Length && s[i] != '\n') i++;
                    continue;
                }
                if (c == '/' && next == '*')
                {
                    int end = s.IndexOf("*/", i + 2, StringComparison.Ordinal);
                    i = end < 0 ? s.Length : end + 2;
                    continue;
                }
                if (lineStart && c == '#')
                {
                    while (i < s.Length && s[i] != '\n') i++;
                    continue;
                }
                if (c == '"' && next == '"' && i + 2 < s.Length && s[i + 2] == '"')
                {
                    // A raw string, closed by as many quotes as opened it.
                    int count = 0;
                    while (i + count < s.Length && s[i + count] == '"') count++;
                    int end = s.IndexOf(new string('"', count), i + count, StringComparison.Ordinal);
                    i = end < 0 ? s.Length : end + count;
                    continue;
                }
                if (c == '@' && (next == '"' || (next == '$' && i + 2 < s.Length && s[i + 2] == '"')) || c == '$' && next == '@' && i + 2 < s.Length && s[i + 2] == '"')
                {
                    i = s.IndexOf('"', i) + 1;
                    while (i < s.Length)
                    {
                        if (s[i] == '"')
                        {
                            if (i + 1 < s.Length && s[i + 1] == '"')
                            {
                                i += 2;
                                continue;
                            }
                            i++;
                            break;
                        }
                        i++;
                    }
                    continue;
                }
                if (c == '"')
                {
                    i++;
                    while (i < s.Length && s[i] != '"' && s[i] != '\n')
                    {
                        if (s[i] == '\\') i++;
                        i++;
                    }
                    i = Math.Min(i + 1, s.Length);
                    continue;
                }
                if (c == '\'')
                {
                    i++;
                    while (i < s.Length && s[i] != '\'' && s[i] != '\n')
                    {
                        if (s[i] == '\\') i++;
                        i++;
                    }
                    i = Math.Min(i + 1, s.Length);
                    continue;
                }
                if (c == '\n') lineStart = true;
                else if (!char.IsWhiteSpace(c)) lineStart = false;
                code[i] = true;
                i++;
            }
            return code;
        }

        static bool IsWord(char c) => char.IsLetterOrDigit(c) || c == '_';

        struct Token
        {
            public int Start, End;
        }

        static List<Token> Words(string s, bool[] code)
        {
            var words = new List<Token>();
            int i = 0;
            while (i < s.Length)
            {
                if (!code[i] || !IsWord(s[i]))
                {
                    i++;
                    continue;
                }
                int start = i;
                while (i < s.Length && code[i] && IsWord(s[i])) i++;
                words.Add(new Token { Start = start, End = i });
            }
            return words;
        }

        static int SkipSpace(string s, bool[] code, int at)
        {
            while (at < s.Length && (char.IsWhiteSpace(s[at]) || !code[at])) at++;
            return at;
        }

        static int LineOf(string s, int at)
        {
            int line = 1;
            for (int i = 0; i < at && i < s.Length; i++)
                if (s[i] == '\n')
                    line++;
            return line;
        }

        /// <summary>The script as it would be, and what was done or remains. Nothing is written.</summary>
        public static string Rewrite(string source, List<LetteraTmpNote> notes)
        {
            if (string.IsNullOrEmpty(source)) return source ?? "";
            bool[] code = CodeMask(source);
            List<Token> words = Words(source, code);
            var names = new HashSet<string>();
            string Word(int index) => source.Substring(words[index].Start, words[index].End - words[index].Start);

            // Who holds a TextMesh Pro type, by what this file says.
            for (int w = 0; w + 1 < words.Count; w++)
            {
                string word = Word(w);
                if (!Types.ContainsKey(word)) continue;
                // TYPE name, TYPE[] name, List<TYPE> name, GetComponent<TYPE>() is handled below.
                int after = SkipSpace(source, code, words[w].End);
                while (after < source.Length && (source[after] == '[' || source[after] == ']' || source[after] == '>' || source[after] == ',' || char.IsWhiteSpace(source[after]))) after++;
                if (words[w + 1].Start == after && IsDeclaredName(source, code, words[w + 1])) names.Add(Word(w + 1));
                // var x = GetComponent<TYPE>(), var x = (TYPE)y, var x = y as TYPE, var x = new TYPE()
                int before = w - 1;
                if (before >= 0)
                {
                    int line = source.LastIndexOf('\n', words[w].Start) + 1;
                    int assign = source.IndexOf('=', line, words[w].Start - line);
                    if (assign >= 0)
                    {
                        int back = assign - 1;
                        while (back > line && char.IsWhiteSpace(source[back])) back--;
                        int end = back + 1;
                        while (back >= line && IsWord(source[back])) back--;
                        if (end > back + 1) names.Add(source.Substring(back + 1, end - back - 1));
                    }
                }
            }
            // foreach (var t in labels), and pattern `is TYPE name` falls under the declaration above.
            for (int w = 0; w + 3 < words.Count; w++)
            {
                if (Word(w) != "foreach") continue;
                if (Word(w + 1) == "var" && Word(w + 3) == "in" && w + 4 < words.Count && names.Contains(Word(w + 4))) names.Add(Word(w + 2));
            }

            var edits = new List<(int start, int end, string text)>();
            bool anyTmp = false, keepTmpo = false;
            for (int w = 0; w < words.Count; w++)
            {
                string word = Word(w);
                if (Types.TryGetValue(word, out string mapped))
                {
                    edits.Add((words[w].Start, words[w].End, mapped));
                    anyTmp = true;
                    continue;
                }
                if (word.StartsWith("TMP_", StringComparison.Ordinal) || word == "TextAlignmentOptions" && !(w >= 2 && Word(w - 1) == "alignment"))
                {
                    if (word != "TextAlignmentOptions" || !IsAlignmentValue(source, code, words[w]))
                    {
                        keepTmpo = true;
                        notes?.Add(new LetteraTmpNote { Line = LineOf(source, words[w].Start), Message = word + " has no direct counterpart: this stays for you to change.", Warning = true });
                    }
                }
                // A member on a receiver known above.
                int dot = words[w].Start - 1;
                while (dot >= 0 && char.IsWhiteSpace(source[dot])) dot--;
                if (dot < 0 || source[dot] != '.' || !code[dot] || w == 0) continue;
                int receiverEnd = dot - 1;
                while (receiverEnd >= 0 && char.IsWhiteSpace(source[receiverEnd])) receiverEnd--;
                bool known = false;
                if (receiverEnd >= 0 && source[receiverEnd] == ')')
                {
                    // GetComponent<TYPE>().member, or GetComponentInChildren<TYPE>().member
                    int bracket = source.LastIndexOf('<', receiverEnd);
                    if (bracket >= 0 && receiverEnd - bracket < 48)
                    {
                        int close = source.IndexOf('>', bracket);
                        if (close > bracket && close < receiverEnd && Types.ContainsKey(source.Substring(bracket + 1, close - bracket - 1).Trim())) known = true;
                    }
                }
                else if (receiverEnd >= 0 && (source[receiverEnd] == ']' || IsWord(source[receiverEnd])))
                {
                    int r = receiverEnd;
                    if (source[r] == ']')
                    {
                        r = source.LastIndexOf('[', r) - 1;
                        while (r >= 0 && char.IsWhiteSpace(source[r])) r--;
                    }
                    int end = r + 1;
                    while (r >= 0 && IsWord(source[r])) r--;
                    if (end > r + 1 && names.Contains(source.Substring(r + 1, end - r - 1))) known = true;
                }
                if (!known) continue;
                if (word == "alignment")
                {
                    int equals = SkipSpace(source, code, words[w].End);
                    if (equals < source.Length && source[equals] == '=' && w + 2 < words.Count && Word(w + 1) == "TextAlignmentOptions" && Alignments.TryGetValue(Word(w + 2), out (string h, string v) align))
                    {
                        int semi = source.IndexOf(';', words[w + 2].End);
                        int receiverStart = StatementStart(source, words[w].Start);
                        string receiver = source.Substring(receiverStart, dot - receiverStart).Trim();
                        if (semi > 0)
                        {
                            edits.Add((receiverStart, semi + 1, "{ " + receiver + ".HorizontalAlignment = LetteraHorizontalAlignment." + align.h + "; " + receiver + ".VerticalAlignment = LetteraVerticalAlignment." + align.v + "; }"));
                            w += 2;
                            continue;
                        }
                    }
                    notes?.Add(new LetteraTmpNote { Line = LineOf(source, words[w].Start), Message = "alignment set from a value: use HorizontalAlignment and VerticalAlignment.", Warning = true });
                    continue;
                }
                if (Members.TryGetValue(word, out string member))
                {
                    edits.Add((words[w].Start, words[w].End, member));
                    continue;
                }
                if (word == "onValueChanged" && w + 1 < words.Count && (Word(w + 1) == "AddListener" || Word(w + 1) == "RemoveListener"))
                {
                    int open = source.IndexOf('(', words[w + 1].End);
                    int close = MatchParen(source, code, open);
                    if (open > 0 && close > open)
                    {
                        string handler = source.Substring(open + 1, close - open - 1);
                        edits.Add((words[w].Start, close + 1, "ValueChanged " + (Word(w + 1) == "AddListener" ? "+=" : "-=") + " " + handler));
                        w++;
                        continue;
                    }
                }
                if (Reported.TryGetValue(word, out string advice))
                    notes?.Add(new LetteraTmpNote { Line = LineOf(source, words[w].Start), Message = "." + word + ": " + advice, Warning = true });
            }

            // using TMPro; becomes ours, and stays too while something of it remains.
            bool hadUsing = false;
            for (int w = 0; w + 1 < words.Count; w++)
            {
                if (Word(w) != "using" || Word(w + 1) != "TMPro") continue;
                string replacement = keepTmpo ? "using TMPro;\nusing FirstSymbol.Lettera;" : "using FirstSymbol.Lettera;";
                int semi = source.IndexOf(';', words[w + 1].End);
                if (semi > 0) edits.Add((words[w].Start, semi + 1, replacement));
                anyTmp = true;
                hadUsing = true;
            }
            if (!anyTmp && edits.Count == 0) return source;

            // A script inside TextMesh Pro's own namespace needs no using of it, and needs ours all the same.
            if (!hadUsing && edits.Count > 0)
            {
                int place = 0;
                for (int w = 0; w < words.Count; w++)
                {
                    if (Word(w) != "using") continue;
                    place = words[w].Start;
                    break;
                }
                edits.Add((place, place, "using FirstSymbol.Lettera;\n"));
            }

            edits.Sort((a, b) => a.start.CompareTo(b.start));
            var result = new StringBuilder(source.Length + 64);
            int at = 0;
            foreach ((int start, int end, string text) in edits)
            {
                if (start < at) continue;
                result.Append(source, at, start - at).Append(text);
                at = end;
            }
            result.Append(source, at, source.Length - at);
            return result.ToString();
        }

        static bool IsAlignmentValue(string s, bool[] code, Token word)
        {
            int after = SkipSpace(s, code, word.End);
            return after < s.Length && s[after] == '.';
        }

        static bool IsDeclaredName(string s, bool[] code, Token name)
        {
            int after = SkipSpace(s, code, name.End);
            if (after >= s.Length) return false;
            char c = s[after];
            return c == ';' || c == '=' || c == ',' || c == ')' || c == '{';
        }

        static int StatementStart(string s, int at)
        {
            int i = at - 1;
            while (i >= 0 && s[i] != ';' && s[i] != '{' && s[i] != '}' && s[i] != ')' && s[i] != '\n') i--;
            i++;
            while (i < at && char.IsWhiteSpace(s[i])) i++;
            return i;
        }

        static int MatchParen(string s, bool[] code, int open)
        {
            if (open < 0) return -1;
            int depth = 0;
            for (int i = open; i < s.Length; i++)
            {
                if (!code[i]) continue;
                if (s[i] == '(') depth++;
                else if (s[i] == ')' && --depth == 0) return i;
            }
            return -1;
        }
    }

    /// <summary>
    /// Rewrites TextMesh Pro's components in a text-serialized scene or prefab. The script is swapped where it stands, so that a component keeps its file id,
    /// and every field, event and override that points at it stays valid, with nothing to repair. Pure: text in, text out.
    /// </summary>
    public sealed class LetteraTmpYaml
    {
        public const string GuidCanvas = "f4688fdb7df04437aeb418b961361dc5", GuidWorld = "9541d86e2fd84c1d9990edf0852d74ab";
        public const string GuidInput = "2da0c512f12947e489f739169773d7ca", GuidDropdown = "7b743370ac3e4ec2a1668f5455a8ef8a";
        public const string GuidSubMeshUi = "058cba836c1846c3aa1c5fd2e28aea77", GuidSubMesh = "07994bfe8b0e4adb97d706de5dea48d5";

        public string LetteraCanvasGuid, LetteraWorldGuid;
        /// <summary>A TextMesh Pro font asset's guid, and the Lettera font's that replaces it.</summary>
        public readonly Dictionary<string, string> FontMap = new Dictionary<string, string>();
        public readonly List<string> Losses = new List<string>();
        public int Converted, Removed;
        public int SkippedInFields;

        const string Mono = "--- !u!114 &";

        struct Doc
        {
            public int Start, End;
            public string Id;
        }

        static List<Doc> Docs(string text)
        {
            var docs = new List<Doc>();
            int at = 0;
            while (true)
            {
                int start = at == 0 && text.StartsWith("---", StringComparison.Ordinal) ? 0 : text.IndexOf("\n---", at, StringComparison.Ordinal);
                if (start < 0) break;
                if (start > 0 || text[0] == '\n') start++;
                int end = text.IndexOf("\n---", start + 1, StringComparison.Ordinal);
                end = end < 0 ? text.Length : end + 1;
                int amp = text.IndexOf('&', start, Math.Min(end - start, 64));
                string id = "";
                if (amp >= 0)
                {
                    int stop = amp + 1;
                    while (stop < end && (char.IsDigit(text[stop]) || text[stop] == '-')) stop++;
                    id = text.Substring(amp + 1, stop - amp - 1);
                }
                docs.Add(new Doc { Start = start, End = end, Id = id });
                at = end - 1;
                if (end >= text.Length) break;
            }
            return docs;
        }

        internal static string Field(string doc, string name)
        {
            int at = doc.IndexOf("\n  " + name + ":", StringComparison.Ordinal);
            if (at < 0) return null;
            at += name.Length + 4;
            int end = doc.IndexOf('\n', at);
            return (end < 0 ? doc.Substring(at) : doc.Substring(at, end - at)).Trim();
        }

        // A string as Unity writes it: plain, or quoted, and then over several lines.
        internal static string TextField(string doc, string name)
        {
            int at = doc.IndexOf("\n  " + name + ":", StringComparison.Ordinal);
            if (at < 0) return "";
            at += name.Length + 4;
            while (at < doc.Length && doc[at] == ' ') at++;
            if (at >= doc.Length) return "";
            if (doc[at] == '"' || doc[at] == '\'')
            {
                char quote = doc[at];
                var value = new StringBuilder();
                int i = at + 1;
                while (i < doc.Length)
                {
                    char c = doc[i];
                    if (quote == '"' && c == '\\' && i + 1 < doc.Length)
                    {
                        char n = doc[i + 1];
                        if (n == 'n') value.Append('\n');
                        else if (n == 't') value.Append('\t');
                        else if (n == 'u' && i + 5 < doc.Length && int.TryParse(doc.Substring(i + 2, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int code))
                        {
                            value.Append((char)code);
                            i += 4;
                        }
                        else if (n == '\n')
                        {
                            i += 2;
                            while (i < doc.Length && doc[i] == ' ') i++;
                            continue;
                        }
                        else value.Append(n);
                        i += 2;
                        continue;
                    }
                    if (c == quote)
                    {
                        if (quote == '\'' && i + 1 < doc.Length && doc[i + 1] == '\'')
                        {
                            value.Append('\'');
                            i += 2;
                            continue;
                        }
                        break;
                    }
                    if (c == '\n')
                    {
                        // A plain line break in a quoted scalar folds to a space.
                        value.Append(' ');
                        i++;
                        while (i < doc.Length && doc[i] == ' ') i++;
                        continue;
                    }
                    value.Append(c);
                    i++;
                }
                return value.ToString();
            }
            var plain = new StringBuilder();
            int line = doc.IndexOf('\n', at);
            plain.Append(line < 0 ? doc.Substring(at) : doc.Substring(at, line - at));
            // A plain scalar goes on over lines indented further than its key.
            while (line >= 0 && line + 4 < doc.Length && doc[line + 1] == ' ' && doc[line + 2] == ' ' && doc[line + 3] == ' ')
            {
                int next = doc.IndexOf('\n', line + 1);
                plain.Append(' ').Append((next < 0 ? doc.Substring(line + 1) : doc.Substring(line + 1, next - line - 1)).Trim());
                line = next;
            }
            return plain.ToString().Trim();
        }

        static string GuidOf(string reference)
        {
            if (reference == null) return null;
            int at = reference.IndexOf("guid: ", StringComparison.Ordinal);
            if (at < 0) return null;
            at += 6;
            int end = at;
            while (end < reference.Length && char.IsLetterOrDigit(reference[end])) end++;
            return reference.Substring(at, end - at);
        }

        static float Num(string value, float fallback = 0) =>
            float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float parsed) ? parsed : fallback;

        static int Int(string value, int fallback = 0) =>
            int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed) ? parsed : fallback;

        static string Quote(string text)
        {
            var q = new StringBuilder("\"");
            foreach (char c in text)
            {
                switch (c)
                {
                    case '"': q.Append("\\\""); break;
                    case '\\': q.Append("\\\\"); break;
                    case '\n': q.Append("\\n"); break;
                    case '\r': q.Append("\\r"); break;
                    case '\t': q.Append("\\t"); break;
                    default:
                        if (c < 32 || c > 126) q.Append("\\u").Append(((int)c).ToString("X4"));
                        else q.Append(c);
                        break;
                }
            }
            return q.Append('"').ToString();
        }

        static (float x, float y, float z, float w) Vec(string value)
        {
            // {x: 1, y: 2, z: 3, w: 4}
            float Part(string key)
            {
                if (value == null) return 0;
                int at = value.IndexOf(key + ": ", StringComparison.Ordinal);
                if (at < 0) return 0;
                at += key.Length + 2;
                int end = at;
                while (end < value.Length && value[end] != ',' && value[end] != '}') end++;
                return Num(value.Substring(at, end - at));
            }
            return (Part("x"), Part("y"), Part("z"), Part("w"));
        }

        // Every component that an input field or a dropdown points at keeps working as it was: those composites stay for a person.
        static HashSet<string> IdsHeldByFields(string text, List<Doc> docs)
        {
            var held = new HashSet<string>();
            foreach (Doc doc in docs)
            {
                if (!string.Equals(text.Substring(doc.Start, Math.Min(12, doc.End - doc.Start)), Mono, StringComparison.Ordinal)) continue;
                string body = text.Substring(doc.Start, doc.End - doc.Start);
                string guid = GuidOf(Field(body, "m_Script"));
                if (guid != GuidInput && guid != GuidDropdown) continue;
                int at = 0;
                while ((at = body.IndexOf("{fileID: ", at, StringComparison.Ordinal)) >= 0)
                {
                    at += 9;
                    int end = at;
                    while (end < body.Length && (char.IsDigit(body[end]) || body[end] == '-')) end++;
                    held.Add(body.Substring(at, end - at));
                }
            }
            return held;
        }

        /// <summary>The file as it becomes. A file with no TextMesh Pro text comes back as it was, to the byte.</summary>
        public string Convert(string text, string where = "")
        {
            if (text.IndexOf(GuidCanvas, StringComparison.Ordinal) < 0 && text.IndexOf(GuidWorld, StringComparison.Ordinal) < 0 && text.IndexOf(GuidSubMeshUi, StringComparison.Ordinal) < 0 && text.IndexOf(GuidSubMesh, StringComparison.Ordinal) < 0 && !HasTmpOverride(text)) return text;
            List<Doc> docs = Docs(text);
            HashSet<string> held = IdsHeldByFields(text, docs);
            var result = new StringBuilder(text.Length);
            int at = 0;
            foreach (Doc doc in docs)
            {
                result.Append(text, at, doc.Start - at);
                at = doc.End;
                string body = text.Substring(doc.Start, doc.End - doc.Start);
                if (!body.StartsWith(Mono, StringComparison.Ordinal))
                {
                    result.Append(RewriteOverrides(body));
                    continue;
                }
                string script = GuidOf(Field(body, "m_Script"));
                if (script == GuidCanvas || script == GuidWorld)
                {
                    if (held.Contains(doc.Id))
                    {
                        SkippedInFields++;
                        Losses.Add(where + ": a text inside an input field or dropdown was left as it is, with its field.");
                        result.Append(body);
                        continue;
                    }
                    result.Append(ConvertText(body, script == GuidCanvas, where));
                    Converted++;
                }
                else if (script == GuidSubMeshUi || script == GuidSubMesh)
                {
                    // A helper TextMesh Pro makes for itself: nothing else points at it, and nothing here needs it.
                    Removed++;
                }
                else
                {
                    result.Append(RewriteEvents(body));
                }
            }
            result.Append(text, at, text.Length - at);
            return result.ToString();
        }

        static bool HasTmpOverride(string text) => text.IndexOf("propertyPath: m_text", StringComparison.Ordinal) >= 0 || text.IndexOf("propertyPath: m_fontSize", StringComparison.Ordinal) >= 0 || text.IndexOf("propertyPath: m_fontColor", StringComparison.Ordinal) >= 0 || text.IndexOf("TMPro.TMP_Text", StringComparison.Ordinal) >= 0 || text.IndexOf("TMPro.TextMeshPro", StringComparison.Ordinal) >= 0;

        string RewriteEvents(string body)
        {
            if (body.IndexOf("TMPro.", StringComparison.Ordinal) < 0) return body;
            body = body.Replace("TMPro.TextMeshProUGUI, Unity.TextMeshPro", "FirstSymbol.Lettera.LetteraText, FirstSymbol.Lettera")
                .Replace("TMPro.TMP_Text, Unity.TextMeshPro", "FirstSymbol.Lettera.LetteraText, FirstSymbol.Lettera")
                .Replace("TMPro.TextMeshPro, Unity.TextMeshPro", "FirstSymbol.Lettera.LetteraWorldText, FirstSymbol.Lettera")
                .Replace("m_MethodName: set_text", "m_MethodName: set_Text")
                .Replace("m_MethodName: set_fontSize", "m_MethodName: set_FontSize");
            return body;
        }

        // An override's path follows the swap. What has no property here is named.
        string RewriteOverrides(string body)
        {
            if (body.IndexOf("propertyPath: m_", StringComparison.Ordinal) < 0) return body;
            foreach ((string from, string to) in new[]
            {
                ("propertyPath: m_text\n", "propertyPath: m_Data.Text\n"),
                ("propertyPath: m_fontSize\n", "propertyPath: m_Data.FontSize\n"),
                ("propertyPath: m_fontSizeMin\n", "propertyPath: m_Data.MinFontSize\n"),
                ("propertyPath: m_fontSizeMax\n", "propertyPath: m_Data.MaxFontSize\n"),
                ("propertyPath: m_enableAutoSizing\n", "propertyPath: m_Data.AutoSize\n"),
                ("propertyPath: m_fontColor.r\n", "propertyPath: m_Color.r\n"),
                ("propertyPath: m_fontColor.g\n", "propertyPath: m_Color.g\n"),
                ("propertyPath: m_fontColor.b\n", "propertyPath: m_Color.b\n"),
                ("propertyPath: m_fontColor.a\n", "propertyPath: m_Color.a\n"),
                ("propertyPath: m_margin.x\n", "propertyPath: m_Data.Padding.x\n"),
                ("propertyPath: m_margin.y\n", "propertyPath: m_Data.Padding.w\n"),
                ("propertyPath: m_margin.z\n", "propertyPath: m_Data.Padding.z\n"),
                ("propertyPath: m_margin.w\n", "propertyPath: m_Data.Padding.y\n"),
            })
                body = body.Replace(from, to);
            foreach (string lost in new[] { "m_HorizontalAlignment", "m_VerticalAlignment", "m_fontStyle", "m_TextWrappingMode", "m_overflowMode", "m_fontAsset", "m_sharedMaterial", "m_characterSpacing", "m_lineSpacing" })
                if (body.IndexOf("propertyPath: " + lost + "\n", StringComparison.Ordinal) >= 0)
                    Losses.Add("A prefab instance's override of " + lost + " has no path to follow: set it again on the instance.");
            return body;
        }

        string ConvertText(string body, bool canvas, string where)
        {
            string source = TextField(body, "m_text");
            string objectName = where;
            var notes = new List<string>();
            string converted = LetteraTmpMarkup.Convert(source, notes);
            foreach (string note in notes) Losses.Add(where + ": " + note);

            int style = Int(Field(body, "m_fontStyle"));
            string open = "", close = "";
            void Wrap(int bit, string tag)
            {
                if ((style & bit) == 0) return;
                open += "<" + tag + ">";
                close = "</" + tag + ">" + close;
            }
            Wrap(1, "b");
            Wrap(2, "i");
            Wrap(4, "u");
            Wrap(8, "lowercase");
            Wrap(16, "uppercase");
            Wrap(32, "smallcaps");
            Wrap(64, "s");
            if ((style & 512) != 0) Losses.Add(where + ": a Highlight font style keeps no color, so it was not carried.");
            converted = open + converted + close;

            int horizontal = Int(Field(body, "m_HorizontalAlignment"), 1), vertical = Int(Field(body, "m_VerticalAlignment"), 256);
            int h = horizontal == 2 ? 1 : horizontal == 4 ? 2 : horizontal == 8 || horizontal == 16 ? 5 : horizontal == 32 ? 1 : 0;
            int v = vertical == 512 || vertical == 2048 || vertical == 4096 || vertical == 8192 ? 1 : vertical == 1024 ? 2 : 0;
            if (vertical >= 2048) Losses.Add(where + ": Baseline, Geometry and Capline alignment became Middle: check the look.");
            int overflowMode = Int(Field(body, "m_overflowMode"));
            int overflow = overflowMode == 1 ? 1 : overflowMode == 3 ? 2 : 0;
            if (overflowMode == 2 || overflowMode >= 4) Losses.Add(where + ": overflow mode " + overflowMode + " (masking, scroll, page or linked) became Visible.");
            string wrapping = Field(body, "m_TextWrappingMode") ?? Field(body, "m_enableWordWrapping") ?? "1";
            bool wrap = Int(wrapping, 1) == 1 || Int(wrapping, 1) == 2;
            if (Int(wrapping) >= 2) Losses.Add(where + ": preserved whitespace is not carried: runs of spaces stay in the text, and lines wrap by the usual rules.");
            (float left, float top, float right, float bottom) = Vec(Field(body, "m_margin"));
            string fontGuid = GuidOf(Field(body, "m_fontAsset"));
            string font = "{fileID: 0}";
            if (fontGuid != null && FontMap.TryGetValue(fontGuid, out string mapped)) font = "{fileID: 11400000, guid: " + mapped + ", type: 2}";
            else if (fontGuid != null) Losses.Add(where + ": its font has not been mapped, so it uses the project's default.");

            string material = Field(body, "m_sharedMaterial");
            if (material != null && GuidOf(material) != null && FontMap.Count > 0 && !LooksDefault(material, fontGuid)) Losses.Add(where + ": a TextMesh Pro material's look (outline, underlay, glow) is not carried: set it with styles.");
            if (Num(Field(body, "m_characterSpacing")) != 0) Losses.Add(where + ": character spacing " + Field(body, "m_characterSpacing") + " is not carried: use <tracking>.");
            if (Num(Field(body, "m_lineSpacing")) != 0) Losses.Add(where + ": line spacing " + Field(body, "m_lineSpacing") + " is not carried: use <line-height>.");
            if (Field(body, "m_enableVertexGradient") == "1") Losses.Add(where + ": a vertex gradient is not carried: use <fill>.");

            // The head of the document stays: the object, the enabled flag, its id.
            int script = body.IndexOf("  m_Script:", StringComparison.Ordinal);
            int afterScript = body.IndexOf('\n', script) + 1;
            int nameEnd = body.IndexOf("  m_Name:", afterScript, StringComparison.Ordinal);
            var o = new StringBuilder(512);
            o.Append(body, 0, script);
            o.Append("  m_Script: {fileID: 11500000, guid: ").Append(canvas ? LetteraCanvasGuid : LetteraWorldGuid).Append(", type: 3}\n");
            o.Append("  m_Name: \n");
            o.Append("  m_EditorClassIdentifier: \n");
            if (canvas)
            {
                o.Append("  m_Material: {fileID: 0}\n");
                o.Append("  m_Color: ").Append(Field(body, "m_fontColor") ?? Field(body, "m_Color") ?? "{r: 1, g: 1, b: 1, a: 1}").Append('\n');
                o.Append("  m_RaycastTarget: ").Append(Field(body, "m_RaycastTarget") ?? "1").Append('\n');
                string padding = Field(body, "m_RaycastPadding");
                if (padding != null) o.Append("  m_RaycastPadding: ").Append(padding).Append('\n');
                o.Append("  m_Maskable: ").Append(Field(body, "m_Maskable") ?? "1").Append('\n');
                string cull = Field(body, "m_OnCullStateChanged");
                o.Append("  m_OnCullStateChanged:\n    m_PersistentCalls:\n      m_Calls: []\n");
            }
            else
            {
                o.Append("  m_Color: ").Append(Field(body, "m_fontColor") ?? "{r: 1, g: 1, b: 1, a: 1}").Append('\n');
                o.Append("  m_Standalone: 1\n");
            }
            o.Append("  m_Data:\n");
            o.Append("    Text: ").Append(Quote(converted)).Append('\n');
            o.Append("    Font: ").Append(font).Append('\n');
            o.Append("    FontSize: ").Append(Field(body, "m_fontSize") ?? "36").Append('\n');
            o.Append("    HorizontalAlignment: ").Append(h).Append('\n');
            o.Append("    VerticalAlignment: ").Append(v).Append('\n');
            o.Append("    WordWrap: ").Append(wrap ? 1 : 0).Append('\n');
            o.Append("    Overflow: ").Append(overflow).Append('\n');
            o.Append("    AutoSize: ").Append(Field(body, "m_enableAutoSizing") ?? "0").Append('\n');
            o.Append("    MinFontSize: ").Append(Field(body, "m_fontSizeMin") ?? "10").Append('\n');
            o.Append("    MaxFontSize: ").Append(Field(body, "m_fontSizeMax") ?? "72").Append('\n');
            o.Append("    Padding: {x: ").Append(F(left)).Append(", y: ").Append(F(bottom)).Append(", z: ").Append(F(right)).Append(", w: ").Append(F(top)).Append("}\n");
            o.Append("    BaseDirection: ").Append(Field(body, "m_isRightToLeft") == "1" ? 2 : 0).Append('\n');
            o.Append("    UseGlobalStylePreset: ").Append(Field(body, "m_isRichText") == "0" ? 0 : 1).Append('\n');
            return o.ToString();
        }

        static bool LooksDefault(string material, string fontGuid) => material.IndexOf("fileID: 2100000", StringComparison.Ordinal) >= 0 && GuidOf(material) == fontGuid;

        static string F(float v) => v.ToString("0.####", CultureInfo.InvariantCulture);
    }
}

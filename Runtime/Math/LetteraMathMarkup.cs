using System.Globalization;
using System.Text;

namespace FirstSymbol.Lettera
{
    /// <summary>A formula as MathML, for a browser or an application that reads it, with Lettera's spacing written out so that what is compared is layout.</summary>
    public static class LetteraMathMarkup
    {
        /// <summary>The source as MathML Core. Null when it cannot be read.</summary>
        public static string ToMathML(string source, bool display) => ToMathML(source, display, null);

        /// <summary>With a font, the spaces its italic corrections make are written too, so that a browser's boxes can be held against ours.</summary>
        internal static string ToMathML(string source, bool display, LetteraMathFont font)
        {
            MathTree tree = LetteraMathParser.Parse(source);
            if (tree.Error != null) return null;
            var w = new Writer(tree, font);
            w.Out.Append("<math xmlns=\"http://www.w3.org/1998/Math/MathML\"").Append(display ? " display=\"block\"" : "").Append('>');
            w.Group(tree.Root, display ? MathStyle.Display : MathStyle.Text);
            w.Out.Append("</math>");
            return w.Out.ToString();
        }

        sealed class Writer
        {
            readonly MathTree m_Tree;
            internal readonly StringBuilder Out = new StringBuilder(128);

            readonly LetteraMathFont m_Font;

            internal Writer(MathTree tree, LetteraMathFont font)
            {
                m_Tree = tree;
                m_Font = font;
            }

            float ItalicOf(in MathNode n, MathStyle style)
            {
                if (m_Font == null || m_Font.Data == null || n.Kind != MathNodeKind.Char) return 0;
                int level = style == MathStyle.Script ? 1 : style == MathStyle.ScriptScript ? 2 : 0;
                uint glyph = m_Font.GlyphOf(LetteraMathAlphabets.Map(n.Cp, (MathAlphabet)n.Info), level);
                if (glyph == 0) return 0;
                float scale = level == 0 ? 1 : m_Font.C(level == 1 ? MathConstant.ScriptPercentScaleDown : MathConstant.ScriptScriptPercentScaleDown) / 100;
                return m_Font.Data.ItalicCorrection(glyph) * scale / m_Font.Upem;
            }

            // A base goes bare: a wrapper would hide its italic correction from the browser's script placement.
            void Bare(int node, MathStyle style)
            {
                if (node >= 0 && m_Tree.Nodes[node].Kind == MathNodeKind.Char) Node(node, style);
                else Group(node, style);
            }

            static string Em(float value) => value.ToString("0.####", CultureInfo.InvariantCulture) + "em";

            void Text(string value)
            {
                foreach (char c in value)
                {
                    switch (c)
                    {
                        case '&': Out.Append("&amp;"); break;
                        case '<': Out.Append("&lt;"); break;
                        case '>': Out.Append("&gt;"); break;
                        case '"': Out.Append("&quot;"); break;
                        default: Out.Append(c); break;
                    }
                }
            }

            void Cp(int cp) => Text(char.ConvertFromUtf32(cp));

            internal void Group(int node, MathStyle style)
            {
                Out.Append("<mrow>");
                Inner(node, style);
                Out.Append("</mrow>");
            }

            static MathStyle Script(MathStyle s) => s <= MathStyle.Text ? MathStyle.Script : MathStyle.ScriptScript;
            static MathStyle Part(MathStyle s) => s == MathStyle.Display ? MathStyle.Text : s == MathStyle.Text ? MathStyle.Script : MathStyle.ScriptScript;

            void Inner(int node, MathStyle style)
            {
                if (node < 0) return;
                MathNode n = m_Tree.Nodes[node];
                if (n.Kind != MathNodeKind.List)
                {
                    Node(node, style);
                    return;
                }
                bool has = false;
                float italic = 0;
                MathClass previous = MathClass.Ord;
                for (int c = n.A; c >= 0; c = m_Tree.Nodes[c].Next)
                {
                    MathNode child = m_Tree.Nodes[c];
                    if (child.Kind == MathNodeKind.Style)
                    {
                        style = (MathStyle)child.Info;
                        continue;
                    }
                    MathClass cls = child.Kind == MathNodeKind.List ? MathClass.Ord : child.Class;
                    if (cls == MathClass.Bin)
                    {
                        bool bindable = has && previous != MathClass.Bin && previous != MathClass.Op && previous != MathClass.Rel && previous != MathClass.Open && previous != MathClass.Punct;
                        int next = child.Next;
                        if (next < 0) bindable = false;
                        else
                        {
                            MathClass after = m_Tree.Nodes[next].Kind == MathNodeKind.List ? MathClass.Ord : m_Tree.Nodes[next].Class;
                            if (after == MathClass.Rel || after == MathClass.Close || after == MathClass.Punct) bindable = false;
                        }
                        if (!bindable) cls = MathClass.Ord;
                    }
                    if (has && child.Kind != MathNodeKind.Space)
                    {
                        float gap = LetteraMathBuilder.SpaceBetween(previous, cls, style >= MathStyle.Script);
                        if (gap != 0) Out.Append("<mspace width=\"").Append(Em(gap)).Append("\"/>");
                    }
                    if (italic != 0 && child.Kind != MathNodeKind.Scripts)
                    {
                        bool nextItalic = child.Kind == MathNodeKind.Char && (MathAlphabet)child.Info == MathAlphabet.Default && LetteraMathBuilder.IsMathItalic(child.Cp);
                        if (!nextItalic) Out.Append("<mspace width=\"").Append(Em(italic)).Append("\"/>");
                    }
                    italic = ItalicOf(child, style);
                    Node(c, style);
                    if (child.Kind != MathNodeKind.Space)
                    {
                        previous = cls;
                        has = true;
                    }
                }
            }

            void Node(int node, MathStyle style)
            {
                MathNode n = m_Tree.Nodes[node];
                switch (n.Kind)
                {
                    case MathNodeKind.List:
                        Group(node, style);
                        break;
                    case MathNodeKind.Char:
                    {
                        int mapped = LetteraMathAlphabets.Map(n.Cp, (MathAlphabet)n.Info);
                        bool letter = mapped != n.Cp || (n.Cp >= 'a' && n.Cp <= 'z') || (n.Cp >= 'A' && n.Cp <= 'Z');
                        if (letter)
                        {
                            Out.Append("<mi mathvariant=\"normal\">");
                            Cp(mapped);
                            Out.Append("</mi>");
                        }
                        else if (n.Cp >= '0' && n.Cp <= '9')
                        {
                            Out.Append("<mn>");
                            Cp(mapped);
                            Out.Append("</mn>");
                        }
                        else
                        {
                            Out.Append("<mo lspace=\"0\" rspace=\"0\" stretchy=\"false\"").Append(n.Class == MathClass.Op && style == MathStyle.Display ? " largeop=\"true\" movablelimits=\"false\"" : " largeop=\"false\" movablelimits=\"false\"").Append('>');
                            Cp(mapped);
                            Out.Append("</mo>");
                        }
                        break;
                    }
                    case MathNodeKind.Space:
                        Out.Append("<mspace width=\"").Append(Em(n.Value)).Append("\"/>");
                        break;
                    case MathNodeKind.Scripts:
                    {
                        bool op = n.A >= 0 && m_Tree.Nodes[n.A].Class == MathClass.Op;
                        bool limits = (n.Flags & LetteraMathParser.FlagLimits) != 0;
                        if (op && style == MathStyle.Display && (n.Flags & LetteraMathParser.FlagNoLimits) == 0)
                        {
                            MathNode a = m_Tree.Nodes[n.A];
                            limits |= (a.Flags & LetteraMathParser.FlagNoLimits) == 0 && !(a.Kind == MathNodeKind.Char && ((a.Cp >= 0x222B && a.Cp <= 0x2233) || a.Cp == 0x2A0C));
                        }
                        string tag = limits ? (n.B >= 0 && n.C >= 0 ? "munderover" : n.C >= 0 ? "munder" : "mover") : (n.B >= 0 && n.C >= 0 ? "msubsup" : n.C >= 0 ? "msub" : "msup");
                        Out.Append('<').Append(tag).Append('>');
                        Bare(n.A, style);
                        if (n.C >= 0) Group(n.C, Script(style));
                        if (n.B >= 0) Group(n.B, Script(style));
                        Out.Append("</").Append(tag).Append('>');
                        break;
                    }
                    case MathNodeKind.Fraction:
                    {
                        MathStyle forced = n.Info >= 0 ? (MathStyle)n.Info : style;
                        Out.Append("<mfrac>");
                        Group(n.A, Part(forced));
                        Group(n.B, Part(forced));
                        Out.Append("</mfrac>");
                        break;
                    }
                    case MathNodeKind.Binomial:
                        Out.Append("<mrow><mo stretchy=\"true\" lspace=\"0\" rspace=\"0\">(</mo><mfrac linethickness=\"0\">");
                        Group(n.A, Part(style));
                        Group(n.B, Part(style));
                        Out.Append("</mfrac><mo stretchy=\"true\" lspace=\"0\" rspace=\"0\">)</mo></mrow>");
                        break;
                    case MathNodeKind.Radical:
                        if (n.B >= 0)
                        {
                            Out.Append("<mroot>");
                            Group(n.A, style);
                            Group(n.B, MathStyle.ScriptScript);
                            Out.Append("</mroot>");
                        }
                        else
                        {
                            Out.Append("<msqrt>");
                            Group(n.A, style);
                            Out.Append("</msqrt>");
                        }
                        break;
                    case MathNodeKind.Delimited:
                        Out.Append("<mrow>");
                        if (n.Cp != 0) Fence(n.Cp);
                        for (int s = n.A; s >= 0; s = m_Tree.Nodes[s].Next)
                        {
                            Group(m_Tree.Nodes[s].A, style);
                            if (m_Tree.Nodes[s].Next >= 0 && m_Tree.Nodes[s].Cp != 0) Fence(m_Tree.Nodes[s].Cp);
                        }
                        if (n.Cp2 != 0) Fence(n.Cp2);
                        Out.Append("</mrow>");
                        break;
                    case MathNodeKind.BigDelimiter:
                        Out.Append("<mo lspace=\"0\" rspace=\"0\" symmetric=\"true\" stretchy=\"true\" minsize=\"").Append(Em(n.Value)).Append("\" maxsize=\"").Append(Em(n.Value)).Append("\">");
                        Cp(n.Cp);
                        Out.Append("</mo>");
                        break;
                    case MathNodeKind.Table:
                        Out.Append("<mtable>");
                        for (int r = n.A; r >= 0; r = m_Tree.Nodes[r].Next)
                        {
                            Out.Append("<mtr>");
                            for (int c = m_Tree.Nodes[r].A; c >= 0; c = m_Tree.Nodes[c].Next)
                            {
                                Out.Append("<mtd style=\"padding:0 ").Append(Em(0.5f)).Append("\">");
                                Group(m_Tree.Nodes[c].A, MathStyle.Text);
                                Out.Append("</mtd>");
                            }
                            Out.Append("</mtr>");
                        }
                        Out.Append("</mtable>");
                        break;
                    case MathNodeKind.Accent:
                        Out.Append("<mover accent=\"true\">");
                        Group(n.A, style);
                        Out.Append("<mo stretchy=\"").Append((n.Flags & LetteraMathParser.FlagWide) != 0 ? "true" : "false").Append("\">");
                        Cp(n.Cp);
                        Out.Append("</mo></mover>");
                        break;
                    case MathNodeKind.Bar:
                    case MathNodeKind.Stretched:
                    {
                        bool over = n.Info == 0;
                        Out.Append(over ? "<mover>" : "<munder>");
                        Group(n.A, style);
                        Out.Append("<mo stretchy=\"true\">");
                        Cp(n.Kind == MathNodeKind.Bar ? '_' : n.Cp);
                        Out.Append("</mo>");
                        Out.Append(over ? "</mover>" : "</munder>");
                        break;
                    }
                    case MathNodeKind.Arrow:
                        Out.Append(n.B >= 0 ? "<munderover>" : "<mover>");
                        Out.Append("<mo stretchy=\"true\">");
                        Cp(n.Cp);
                        Out.Append("</mo>");
                        if (n.B >= 0) Group(n.B, Script(style));
                        Group(n.A, Script(style));
                        Out.Append(n.B >= 0 ? "</munderover>" : "</mover>");
                        break;
                    case MathNodeKind.Stack:
                        Out.Append(n.Info == 1 ? "<munder>" : "<mover>");
                        Group(n.B, style);
                        Group(n.A, Script(style));
                        Out.Append(n.Info == 1 ? "</munder>" : "</mover>");
                        break;
                    case MathNodeKind.Operator:
                        Out.Append("<mi mathvariant=\"normal\">");
                        Text(m_Tree.Strings[n.Text]);
                        Out.Append("</mi>");
                        break;
                    case MathNodeKind.Text:
                        Out.Append("<mtext>");
                        Text(m_Tree.Strings[n.Text]);
                        Out.Append("</mtext>");
                        break;
                    case MathNodeKind.Boxed:
                    case MathNodeKind.Smash:
                    case MathNodeKind.Not:
                    case MathNodeKind.Color:
                        Group(n.A, style);
                        break;
                    case MathNodeKind.Phantom:
                        Out.Append("<mphantom>");
                        Group(n.A, style);
                        Out.Append("</mphantom>");
                        break;
                }
            }

            void Fence(int cp)
            {
                Out.Append("<mo stretchy=\"true\" symmetric=\"true\" lspace=\"0\" rspace=\"0\">");
                Cp(cp);
                Out.Append("</mo>");
            }
        }
    }
}

using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace FirstSymbol.Lettera
{
    internal enum MathItemKind : byte { Glyph, Rule }

    /// <summary>One thing a formula draws, in em of the formula's size from its origin on the baseline.</summary>
    internal struct LetteraMathItem
    {
        public MathItemKind Kind;
        public bool Vertical, Cropped;
        public byte Face;
        public uint Glyph;
        public float X, Y, Scale;
        /// <summary>A part's limits along its axis in font units, where it meets its neighbours.</summary>
        public float CropLow, CropHigh;
        /// <summary>A rule's length and thickness, in em.</summary>
        public float Length, Thickness;
        public int Color;
    }

    /// <summary>A formula laid out once, in em, and shared. A size change, a re-wrap or a color change runs no math.</summary>
    internal sealed class LetteraMathLayout
    {
        public float Width, Ascent, Descent, InkAscent, InkDescent, InkLeft, InkRight;
        public LetteraMathItem[] Items;
        public FontInstance[] Faces;
        public Color32[] Colors;
        public bool Display, Failed;
        public string Source, Message;
        public int ErrorPosition = -1;
        /// <summary>A display formula's numbers and labels, one per row that has one, or null. Which number is a text's to say.</summary>
        public LetteraMathTag[] Tags;
        /// <summary>The count a \setcounter{equation} in it leaves, or -1.</summary>
        public int CounterSet = -1;
        public long Bytes => 128 + (Items?.Length ?? 0) * 48 + (Source?.Length ?? 0) * 2 + (Tags?.Length ?? 0) * 32;
    }

    /// <summary>Where a display formula's row wants a number or a label, in em from the formula's baseline.</summary>
    internal struct LetteraMathTag
    {
        /// <summary>The middle of the row the tag is centred on.</summary>
        public float Middle;
        /// <summary>A \tag's text, or null for the next number.</summary>
        public string Label;
        /// <summary>From \tag*: drawn with no parentheses.</summary>
        public bool Bare;
    }

#if !LETTERA_NO_MATH
    /// <summary>Computes boxes bottom-up. Every width is worked out once, so time is linear in what a formula draws.</summary>
    internal sealed class LetteraMathBuilder
    {
        struct Box
        {
            public float Width, Ascent, Descent, Italic;
            /// <summary>A lone glyph's ink as it is, which may sit wholly above or below the baseline. Ascent and descent stop at it.</summary>
            public float InkLow, InkHigh;
            public int First, End;
            public bool SingleGlyph, Extended;
            public uint Glyph;
            public int Accent;
        }

        readonly MathTree m_Tree;
        readonly LetteraMathFont m_Font;
        readonly LetteraMathFont m_TextFont;
        readonly ValueList<LetteraMathItem> m_Items = new ValueList<LetteraMathItem>(64);
        readonly List<FontInstance> m_Faces = new List<FontInstance>(2);
        readonly List<Color32> m_Colors = new List<Color32>();
        readonly float m_Script, m_ScriptScript;
        float m_Size = 1;
        int m_Color;

        const float MaxFallbackScale = 3, MaxVariantEm = 8;
        static readonly uint[] s_Variants = new uint[64];
        static readonly int[] s_Advances = new int[64];
        static readonly MathPart[] s_Parts = new MathPart[32];

        LetteraMathBuilder(MathTree tree, LetteraMathFont font, LetteraMathFont textFont)
        {
            m_Tree = tree;
            m_Font = font;
            m_TextFont = textFont;
            m_Script = font.C(MathConstant.ScriptPercentScaleDown) / 100;
            m_ScriptScript = font.C(MathConstant.ScriptScriptPercentScaleDown) / 100;
            m_Faces.Add(font.Instance);
        }

        internal static int Layouts { get; private set; }

        /// <summary>A failed formula's box. It holds what was written, framed, and the text around it renders.</summary>
        internal static LetteraMathLayout Error(string source, string message, int position, LetteraMathFont font, LetteraMathFont textFont, LetteraMathErrorDisplay display)
        {
            if (display == LetteraMathErrorDisplay.Nothing) return new LetteraMathLayout { Items = Array.Empty<LetteraMathItem>(), Faces = new[] { font.Instance }, Source = source, Message = message, ErrorPosition = position, Failed = true };
            var empty = new MathTree();
            var builder = new LetteraMathBuilder(empty, font, textFont);
            string shown = source.Length > 64 ? source.Substring(0, 64) + "…" : source;
            Box text = builder.TextBox(shown, 1);
            Box box = text;
            if (display == LetteraMathErrorDisplay.Frame) box = builder.Framed(text, 0.2f);
            LetteraMathLayout layout = builder.Finish(box, false);
            layout.Failed = true;
            layout.Source = source;
            layout.Message = message;
            layout.ErrorPosition = position;
            return layout;
        }

        internal static LetteraMathLayout Build(MathTree tree, string source, bool display, LetteraMathFont font, LetteraMathFont textFont)
        {
            Layouts++;
            var builder = new LetteraMathBuilder(tree, font, textFont);
            if (display) builder.m_NumberedTable = builder.TopTable(tree.Root);
            Box box = builder.ListBox(tree.Root, display ? MathStyle.Display : MathStyle.Text, false);
            if (builder.m_Items.Count > LetteraMathLimits.DrawItems) return null;
            LetteraMathLayout layout = builder.Finish(box, display);
            layout.Source = source;
            layout.CounterSet = tree.CounterSet;
            if (display) layout.Tags = builder.TagsOf(box);
            return layout;
        }

        // The environment that numbers its rows: a table standing alone in a display formula.
        int m_NumberedTable = -1;
        readonly ValueList<(int row, float middle)> m_Rows = new ValueList<(int, float)>();

        int TopTable(int list)
        {
            if (list < 0) return -1;
            int found = -1;
            for (int child = m_Tree.Nodes[list].A; child >= 0; child = m_Tree.Nodes[child].Next)
            {
                if (m_Tree.Nodes[child].Kind == MathNodeKind.Space) continue;
                if (found >= 0 || m_Tree.Nodes[child].Kind != MathNodeKind.Table) return -1;
                found = child;
            }
            if (found < 0) return -1;
            switch (m_Tree.Strings[m_Tree.Nodes[found].Text])
            {
                case "align": case "align*": case "gather": case "gather*": case "multline": case "multline*": case "equation": case "equation*": case "displaymath":
                    return found;
                default:
                    return -1;
            }
        }

        // amsmath's rules: align and gather number each row, multline and equation the whole once; a starred one only what \tag labels.
        LetteraMathTag[] TagsOf(in Box box)
        {
            var tags = new List<LetteraMathTag>();
            float whole = (box.Ascent - box.Descent) / 2;
            if (m_NumberedTable >= 0 && m_Rows.Count > 0)
            {
                string name = m_Tree.Strings[m_Tree.Nodes[m_NumberedTable].Text];
                bool starred = name.EndsWith("*", StringComparison.Ordinal) || name == "displaymath";
                bool perRow = name.StartsWith("align", StringComparison.Ordinal) || name.StartsWith("gather", StringComparison.Ordinal);
                string label = null;
                bool bare = false, none = false;
                for (int r = 0; r < m_Rows.Count; r++)
                {
                    ref MathNode row = ref m_Tree.Nodes[m_Rows[r].row];
                    string own = row.Text >= 0 ? m_Tree.Strings[row.Text] : null;
                    bool ownBare = (row.Flags & LetteraMathParser.RowTagStar) != 0, noNumber = (row.Flags & LetteraMathParser.RowNoNumber) != 0;
                    if (perRow)
                    {
                        if (own != null || (!starred && !noNumber)) tags.Add(new LetteraMathTag { Middle = m_Rows[r].middle, Label = own, Bare = ownBare });
                        continue;
                    }
                    if (own != null)
                    {
                        label = own;
                        bare = ownBare;
                    }
                    none |= noNumber;
                }
                if (!perRow && (label != null || (!starred && !none)))
                    tags.Add(new LetteraMathTag { Middle = name.StartsWith("multline", StringComparison.Ordinal) ? m_Rows[m_Rows.Count - 1].middle : whole, Label = label, Bare = bare });
            }
            if (tags.Count == 0 && m_Tree.Tag >= 0) tags.Add(new LetteraMathTag { Middle = whole, Label = m_Tree.Strings[m_Tree.Tag], Bare = m_Tree.TagStar });
            return tags.Count > 0 ? tags.ToArray() : null;
        }

        LetteraMathLayout Finish(in Box box, bool display)
        {
            var layout = new LetteraMathLayout
            {
                Width = box.Width + box.Italic * 0,
                Ascent = box.Ascent,
                Descent = box.Descent,
                Display = display,
                Items = m_Items.ToArray(),
                Faces = m_Faces.ToArray(),
                Colors = m_Colors.ToArray(),
            };
            float inkUp = 0, inkDown = 0, inkLeft = 0, inkRight = box.Width;
            foreach (LetteraMathItem item in layout.Items)
            {
                if (item.Kind == MathItemKind.Rule)
                {
                    float w = item.Vertical ? item.Thickness : item.Length, h = item.Vertical ? item.Length : item.Thickness;
                    inkUp = Mathf.Max(inkUp, item.Y + h);
                    inkDown = Mathf.Max(inkDown, -item.Y);
                    inkLeft = Mathf.Min(inkLeft, item.X);
                    inkRight = Mathf.Max(inkRight, item.X + w);
                    continue;
                }
                (float minX, float minY, float maxX, float maxY, float _) = ExtentsOf(item);
                inkUp = Mathf.Max(inkUp, item.Y + maxY * item.Scale);
                inkDown = Mathf.Max(inkDown, -item.Y - minY * item.Scale);
                inkLeft = Mathf.Min(inkLeft, item.X + minX * item.Scale);
                inkRight = Mathf.Max(inkRight, item.X + maxX * item.Scale);
            }
            layout.InkAscent = inkUp;
            layout.InkDescent = inkDown;
            layout.InkLeft = inkLeft;
            layout.InkRight = inkRight;
            return layout;
        }

        (float, float, float, float, float) ExtentsOf(in LetteraMathItem item) => item.Face == 0 ? m_Font.Extents(item.Glyph) : FontOf(item.Face).Extents(item.Glyph);

        LetteraMathFont FontOf(int face) => face == 0 ? m_Font : LetteraMathFont.For(m_Faces[face].Face);

        byte FaceOf(FontInstance instance)
        {
            int index = m_Faces.IndexOf(instance);
            if (index < 0)
            {
                m_Faces.Add(instance);
                index = m_Faces.Count - 1;
            }
            return (byte)index;
        }

        float ScaleOf(MathStyle style) => m_Size * (style == MathStyle.Script ? m_Script : style == MathStyle.ScriptScript ? m_ScriptScript : 1);

        float C(MathConstant constant, MathStyle style) => m_Font.C(constant) * ScaleOf(style);

        static bool IsDisplay(MathStyle style) => style == MathStyle.Display;

        static MathStyle ScriptOf(MathStyle style) => style <= MathStyle.Text ? MathStyle.Script : MathStyle.ScriptScript;

        static MathStyle FractionPartOf(MathStyle style) => style == MathStyle.Display ? MathStyle.Text : style == MathStyle.Text ? MathStyle.Script : MathStyle.ScriptScript;

        void Move(int first, int end, float x, float y)
        {
            for (int i = first; i < end; i++)
            {
                LetteraMathItem item = m_Items[i];
                item.X += x;
                item.Y += y;
                m_Items[i] = item;
            }
        }

        Box Empty() => new Box { First = m_Items.Count, End = m_Items.Count };

        void Rule(float x, float y, float length, float thickness, bool vertical)
        {
            m_Items.Add(new LetteraMathItem { Kind = MathItemKind.Rule, X = x, Y = y, Length = length, Thickness = thickness, Vertical = vertical, Scale = 1, Color = m_Color });
        }

        // ---------------------------------------------------------------- lists

        // TeX's table of spaces between atoms: 0 none, 1 thin, 2 medium, 3 thick. Negative: only in display and text styles.
        static readonly sbyte[,] s_Spacing =
        {
            //        Ord Op Bin Rel Open Close Punct Inner
            /*Ord*/  { 0, 1, -2, -3, 0, 0, 0, -1 },
            /*Op*/   { 1, 1, 0, -3, 0, 0, 0, -1 },
            /*Bin*/  { -2, -2, 0, 0, -2, 0, 0, -2 },
            /*Rel*/  { -3, -3, 0, 0, -3, 0, 0, -3 },
            /*Open*/ { 0, 0, 0, 0, 0, 0, 0, 0 },
            /*Close*/{ 0, 1, -2, -3, 0, 0, 0, -1 },
            /*Punct*/{ -1, -1, 0, -1, -1, -1, -1, -1 },
            /*Inner*/{ -1, 1, -2, -3, -1, 0, -1, -1 },
        };

        internal static float SpaceBetween(MathClass left, MathClass right, bool tight)
        {
            int entry = s_Spacing[(int)left, (int)right];
            if (entry < 0)
            {
                if (tight) return 0;
                entry = -entry;
            }
            return entry == 1 ? 3f / 18 : entry == 2 ? 4f / 18 : entry == 3 ? 5f / 18 : 0;
        }

        Box ListBox(int node, MathStyle style, bool cramped)
        {
            if (node < 0) return Empty();
            ref MathNode list = ref m_Tree.Nodes[node];
            if (list.Kind != MathNodeKind.List) return NodeBox(node, ref style, cramped);

            // Classes first: a binary operator with nothing to bind reads as ordinary.
            float savedSize = m_Size;
            int savedColor = m_Color;
            int first = m_Items.Count;
            float x = 0, ascent = 0, descent = 0, italic = 0;
            MathClass previous = MathClass.Ord;
            bool hasPrevious = false;
            int count = 0;
            int lastPlaced = -1;
            Box single = default;
            for (int child = list.A; child >= 0; child = m_Tree.Nodes[child].Next)
            {
                MathNodeKind kind = m_Tree.Nodes[child].Kind;
                if (kind == MathNodeKind.Style)
                {
                    style = (MathStyle)m_Tree.Nodes[child].Info;
                    continue;
                }
                if (kind == MathNodeKind.Size)
                {
                    m_Size = m_Tree.Nodes[child].Value;
                    continue;
                }
                MathClass cls = EffectiveClass(child);
                if (cls == MathClass.Bin)
                {
                    bool bindable = hasPrevious && previous != MathClass.Bin && previous != MathClass.Op && previous != MathClass.Rel && previous != MathClass.Open && previous != MathClass.Punct;
                    int next = NextVisible(child);
                    if (next < 0) bindable = false;
                    else
                    {
                        MathClass after = EffectiveClass(next);
                        if (after == MathClass.Rel || after == MathClass.Close || after == MathClass.Punct) bindable = false;
                    }
                    if (!bindable) cls = MathClass.Ord;
                }
                if (hasPrevious && kind != MathNodeKind.Space)
                {
                    float gap = SpaceBetween(previous, cls, style >= MathStyle.Script);
                    if (gap != 0) x += gap * ScaleOf(style);
                }
                // A slanted letter leans into what stands upright after it.
                if (italic != 0 && kind != MathNodeKind.Scripts)
                {
                    bool nextItalic = kind == MathNodeKind.Char && IsItalicLetter(child);
                    if (!nextItalic) x += italic;
                }
                MathStyle childStyle = style;
                int before = m_Items.Count;
                Box box = NodeBox(child, ref childStyle, cramped);
                Move(before, m_Items.Count, x, 0);
                x += box.Width;
                italic = box.SingleGlyph ? box.Italic : 0;
                ascent = Mathf.Max(ascent, box.Ascent);
                descent = Mathf.Max(descent, box.Descent);
                if (kind != MathNodeKind.Space)
                {
                    previous = cls;
                    hasPrevious = true;
                }
                if (m_Items.Count > LetteraMathLimits.DrawItems) break;
                count++;
                single = box;
                lastPlaced = child;
            }
            m_Size = savedSize;
            m_Color = savedColor;
            var result = new Box { Width = x, Ascent = ascent, Descent = descent, First = first, End = m_Items.Count, Italic = italic };
            if (count == 1)
            {
                result.SingleGlyph = single.SingleGlyph;
                result.Glyph = single.Glyph;
                result.Extended = single.Extended;
                result.Italic = single.Italic;
                result.Accent = single.Accent;
            }
            _ = lastPlaced;
            return result;
        }

        int NextVisible(int child)
        {
            for (int n = m_Tree.Nodes[child].Next; n >= 0; n = m_Tree.Nodes[n].Next)
            {
                MathNodeKind kind = m_Tree.Nodes[n].Kind;
                if (kind != MathNodeKind.Space && kind != MathNodeKind.Style && kind != MathNodeKind.Size) return n;
            }
            return -1;
        }

        MathClass EffectiveClass(int node)
        {
            ref MathNode n = ref m_Tree.Nodes[node];
            if (n.Kind == MathNodeKind.List)
            {
                // A group is ordinary, whatever it holds.
                return MathClass.Ord;
            }
            return n.Class;
        }

        bool IsItalicLetter(int node)
        {
            ref MathNode n = ref m_Tree.Nodes[node];
            return (MathAlphabet)n.Info == MathAlphabet.Default && IsMathItalic(n.Cp);
        }

        internal static bool IsMathItalic(int cp) => (cp >= 'a' && cp <= 'z') || (cp >= 'A' && cp <= 'Z') || (cp >= 0x3B1 && cp <= 0x3C9) || cp == 0x3D1 || cp == 0x3D5 || cp == 0x3D6 || cp == 0x3F0 || cp == 0x3F1 || cp == 0x3F5;

        // ---------------------------------------------------------------- nodes

        Box NodeBox(int node, ref MathStyle style, bool cramped)
        {
            ref MathNode n = ref m_Tree.Nodes[node];
            switch (n.Kind)
            {
                case MathNodeKind.List: return ListBox(node, style, cramped);
                case MathNodeKind.Char: return CharBox(n.Cp, (MathAlphabet)n.Info, style, n.Class);
                case MathNodeKind.Space:
                    return new Box { Width = n.Value * m_Size, First = m_Items.Count, End = m_Items.Count };
                case MathNodeKind.Scripts: return ScriptsBox(node, style, cramped);
                case MathNodeKind.Fraction: return FractionBox(node, style, cramped);
                case MathNodeKind.Binomial: return BinomialBox(node, style, cramped);
                case MathNodeKind.Radical: return RadicalBox(node, style);
                case MathNodeKind.Delimited: return DelimitedBox(node, style, cramped);
                case MathNodeKind.BigDelimiter: return FixedDelimiter(n.Cp, n.Value * m_Size, style);
                case MathNodeKind.Table: return TableBox(node, style);
                case MathNodeKind.Accent: return AccentBox(node, style);
                case MathNodeKind.Bar: return BarBox(node, style, cramped);
                case MathNodeKind.Stretched: return StretchedBox(node, style, cramped);
                case MathNodeKind.Arrow: return ArrowBox(node, style);
                case MathNodeKind.Stack: return StackBox(node, style, cramped);
                case MathNodeKind.Operator: return OperatorBox(node, style);
                case MathNodeKind.Text: return TextBox(m_Tree.Strings[n.Text], ScaleOf(style));
                case MathNodeKind.Boxed:
                {
                    Box inner = ListBox(n.A, style, cramped);
                    return Framed(inner, 0.3f * m_Size);
                }
                case MathNodeKind.Phantom:
                {
                    int first = m_Items.Count;
                    Box inner = ListBox(n.A, style, cramped);
                    m_Items.RemoveRange(first, m_Items.Count - first);
                    return new Box { Width = n.Info == 2 ? 0 : inner.Width, Ascent = n.Info == 1 ? 0 : inner.Ascent, Descent = n.Info == 1 ? 0 : inner.Descent, First = first, End = first };
                }
                case MathNodeKind.Smash:
                {
                    Box inner = ListBox(n.A, style, cramped);
                    inner.Ascent = inner.Descent = 0;
                    return inner;
                }
                case MathNodeKind.Not: return NotBox(node, style, cramped);
                case MathNodeKind.Color: return ColorBox(node, style, cramped);
                default: return Empty();
            }
        }

        Box ColorBox(int node, MathStyle style, bool cramped)
        {
            ref MathNode n = ref m_Tree.Nodes[node];
            int saved = m_Color;
            if (LetteraSlots.TryColor(m_Tree.Strings[n.Text], out Color32 color))
            {
                m_Colors.Add(color);
                m_Color = m_Colors.Count;
            }
            Box box = ListBox(n.A, style, cramped);
            m_Color = saved;
            return box;
        }

        Box NotBox(int node, MathStyle style, bool cramped)
        {
            ref MathNode n = ref m_Tree.Nodes[node];
            Box body = ListBox(n.A, style, cramped);
            GlyphRef stroke = FindGlyph(0x0338, ScriptLevel(style));
            if (stroke.Glyph != 0)
            {
                float scale = ScaleOf(style);
                float advance = stroke.Font.Extents(stroke.Glyph).advance * scale;
                AddGlyph(stroke, (body.Width - advance) / 2, 0, scale);
            }
            body.End = m_Items.Count;
            body.SingleGlyph = false;
            return body;
        }

        static int ScriptLevel(MathStyle style) => style == MathStyle.Script ? 1 : style == MathStyle.ScriptScript ? 2 : 0;

        readonly struct GlyphRef
        {
            public readonly LetteraMathFont Font;
            public readonly uint Glyph;
            public readonly byte Face;

            public GlyphRef(LetteraMathFont font, uint glyph, byte face)
            {
                Font = font;
                Glyph = glyph;
                Face = face;
            }
        }

        // The math face, then the text's own: a letter it lacks is no error.
        GlyphRef FindGlyph(int cp, int ssty, bool dotless = false, bool flattened = false)
        {
            uint glyph = m_Font.GlyphOf(cp, ssty, dotless, flattened);
            if (glyph != 0) return new GlyphRef(m_Font, glyph, 0);
            if (m_TextFont != null && m_TextFont != m_Font)
            {
                glyph = m_TextFont.GlyphOf(cp, 0);
                if (glyph != 0) return new GlyphRef(m_TextFont, glyph, FaceOf(m_TextFont.Instance));
            }
            return new GlyphRef(m_Font, 0, 0);
        }

        void AddGlyph(in GlyphRef glyph, float x, float y, float scale, bool cropped = false, float low = 0, float high = 0, bool vertical = false)
        {
            m_Items.Add(new LetteraMathItem { Kind = MathItemKind.Glyph, Face = glyph.Face, Glyph = glyph.Glyph, X = x, Y = y, Scale = scale, Cropped = cropped, CropLow = low, CropHigh = high, Vertical = vertical, Color = m_Color });
        }

        Box GlyphBox(in GlyphRef glyph, float scale)
        {
            (float _, float minY, float _, float maxY, float advance) = glyph.Font.Extents(glyph.Glyph);
            int first = m_Items.Count;
            AddGlyph(glyph, 0, 0, scale);
            var box = new Box
            {
                Width = advance * scale,
                Ascent = Mathf.Max(0, maxY * scale),
                Descent = Mathf.Max(0, -minY * scale),
                InkLow = minY * scale,
                InkHigh = maxY * scale,
                First = first,
                End = m_Items.Count,
                SingleGlyph = true,
                Glyph = glyph.Glyph,
            };
            ILetteraMathData data = glyph.Font.Data;
            if (data != null)
            {
                float unit = scale / glyph.Font.Upem;
                box.Italic = data.ItalicCorrection(glyph.Glyph) * unit;
                box.Accent = data.TopAccentAttachment(glyph.Glyph);
                box.Extended = data.IsExtended(glyph.Glyph);
            }
            return box;
        }

        Box CharBox(int cp, MathAlphabet alphabet, MathStyle style, MathClass cls)
        {
            float scale = ScaleOf(style);
            int mapped = LetteraMathAlphabets.Map(cp, alphabet);
            // A prime is already a raised mark. Its script form would seat it lower than it reads.
            bool prime = cp >= 0x2032 && cp <= 0x2037;
            GlyphRef glyph = FindGlyph(mapped, prime ? 0 : ScriptLevel(style));
            if (glyph.Glyph == 0 && mapped != cp)
            {
                // The block is missing: the plain letter, leaning or heavier by synthesis, as text does it.
                glyph = SyntheticLetter(cp, alphabet, style);
            }
            if (glyph.Glyph == 0) glyph = FindGlyph(cp, ScriptLevel(style));

            // A large operator in display style takes a larger shape, centred on the axis.
            if (cls == MathClass.Op && !m_InsideUnstretched)
                return LargeOperator(glyph, cp, style);
            return GlyphBox(glyph, scale);
        }

        readonly bool m_InsideUnstretched = false;

        GlyphRef SyntheticLetter(int cp, MathAlphabet alphabet, MathStyle style)
        {
            FontFace face = m_Font.Instance.Face;
            bool slant = alphabet == MathAlphabet.Default || alphabet == MathAlphabet.Italic || alphabet == MathAlphabet.BoldSymbol;
            bool bold = alphabet == MathAlphabet.Bold || alphabet == MathAlphabet.BoldSymbol;
            FontInstance instance = face.GetInstance(-1, bold ? 0.03f : 0, slant ? 0.2f : 0);
            uint glyph = m_Font.GlyphOf(cp, ScriptLevel(style));
            if (glyph == 0) return new GlyphRef(m_Font, 0, 0);
            return new GlyphRef(m_Font, glyph, FaceOf(instance));
        }

        Box LargeOperator(in GlyphRef glyph, int cp, MathStyle style)
        {
            float scale = ScaleOf(style);
            float axis = C(MathConstant.AxisHeight, style);
            Box box;
            if (!IsDisplay(style))
            {
                box = GlyphBox(glyph, scale);
            }
            else
            {
                float wanted = C(MathConstant.DisplayOperatorMinHeight, style);
                int first = m_Items.Count;
                ILetteraMathData data = glyph.Font.Data;
                uint chosen = glyph.Glyph;
                if (data != null && wanted > 0)
                {
                    int count = Mathf.Min(data.Variants(glyph.Glyph, true, s_Variants, s_Advances), s_Variants.Length);
                    float unit = scale / glyph.Font.Upem;
                    for (int i = 0; i < count; i++)
                    {
                        chosen = s_Variants[i];
                        if (s_Advances[i] * unit >= wanted) break;
                    }
                    box = GlyphBox(new GlyphRef(glyph.Font, chosen, glyph.Face), scale);
                }
                else if (data == null)
                {
                    box = GlyphBox(glyph, scale * 1.5f);
                }
                else
                {
                    box = GlyphBox(glyph, scale);
                }
                _ = first;
            }
            // Centred on the axis.
            float shift = axis - (box.Ascent - box.Descent) / 2;
            Move(box.First, box.End, 0, shift);
            box.Ascent += shift;
            box.Descent -= shift;
            return box;
        }

        Box OperatorBox(int node, MathStyle style)
        {
            ref MathNode n = ref m_Tree.Nodes[node];
            string name = m_Tree.Strings[n.Text];
            float scale = ScaleOf(style);
            int first = m_Items.Count;
            float x = 0, ascent = 0, descent = 0;
            for (int i = 0; i < name.Length; i++)
            {
                int cp = char.ConvertToUtf32(name, i);
                if (char.IsHighSurrogate(name[i])) i++;
                if (cp == ' ')
                {
                    x += 0.25f * scale;
                    continue;
                }
                GlyphRef glyph = FindGlyph(cp, ScriptLevel(style));
                (float _, float minY, float _, float maxY, float advance) = glyph.Font.Extents(glyph.Glyph);
                AddGlyph(glyph, x, 0, scale);
                x += advance * scale;
                ascent = Mathf.Max(ascent, maxY * scale);
                descent = Mathf.Max(descent, -minY * scale);
            }
            return new Box { Width = x, Ascent = ascent, Descent = descent, First = first, End = m_Items.Count };
        }

        unsafe Box TextBox(string text, float scale)
        {
            if (string.IsNullOrEmpty(text)) return Empty();
            LetteraMathFont font = m_TextFont ?? m_Font;
            int first = m_Items.Count;
            int count = 0;
            var codepoints = new int[text.Length];
            for (int i = 0; i < text.Length; i++)
            {
                int cp = text[i];
                if (char.IsHighSurrogate(text[i]) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1])) cp = char.ConvertToUtf32(text[i], text[++i]);
                codepoints[count++] = cp;
            }
            var output = new NativeGlyph[count * 2 + 8];
            int shaped;
            fixed (int* t = codepoints)
            fixed (NativeGlyph* o = output)
                shaped = LetteraNative.lt_shape(font.Instance.Font, t, count, 0, count, 0, 0, null, null, 0, o, output.Length);
            float unit = scale / font.Upem, x = 0, ascent = 0, descent = 0;
            byte face = font == m_Font ? (byte)0 : FaceOf(font.Instance);
            var reference = new GlyphRef(font, 0, face);
            for (int i = 0; i < shaped; i++)
            {
                NativeGlyph g = output[i];
                var placed = new GlyphRef(reference.Font, g.Glyph, face);
                AddGlyph(placed, x + g.XOffset * unit, g.YOffset * unit, scale);
                x += g.XAdvance * unit;
            }
            FaceMetrics metrics = font.Instance.Face.Metrics;
            ascent = metrics.Ascender / font.Upem * scale;
            descent = -metrics.Descender / font.Upem * scale;
            return new Box { Width = x, Ascent = ascent, Descent = descent, First = first, End = m_Items.Count };
        }

        Box Framed(Box inner, float padding)
        {
            float rule = C(MathConstant.FractionRuleThickness, MathStyle.Text);
            Move(inner.First, inner.End, padding + rule, 0);
            float width = inner.Width + 2 * (padding + rule);
            float top = inner.Ascent + padding, bottom = -inner.Descent - padding;
            Rule(0, top, width, rule, false);
            Rule(0, bottom - rule, width, rule, false);
            Rule(0, bottom - rule, top - bottom + 2 * rule, rule, true);
            Rule(width - rule, bottom - rule, top - bottom + 2 * rule, rule, true);
            return new Box { Width = width, Ascent = top + rule, Descent = -bottom + rule, First = inner.First, End = m_Items.Count };
        }

        // ---------------------------------------------------------------- fractions

        Box FractionBox(int node, MathStyle style, bool cramped)
        {
            ref MathNode n = ref m_Tree.Nodes[node];
            if (n.Info >= 0) style = (MathStyle)n.Info;
            bool display = IsDisplay(style);
            bool continued = n.Cp == 1;
            MathStyle numStyle = continued ? MathStyle.Display : FractionPartOf(style);
            MathStyle denStyle = continued ? MathStyle.Display : FractionPartOf(style);
            Box num = ListBox(n.A, numStyle, cramped);
            Box den = ListBox(n.B, denStyle, true);
            float t = C(MathConstant.FractionRuleThickness, style), a = C(MathConstant.AxisHeight, style);
            float numShift = C(display ? MathConstant.FractionNumeratorDisplayStyleShiftUp : MathConstant.FractionNumeratorShiftUp, style);
            float denShift = C(display ? MathConstant.FractionDenominatorDisplayStyleShiftDown : MathConstant.FractionDenominatorShiftDown, style);
            float numGap = C(display ? MathConstant.FractionNumDisplayStyleGapMin : MathConstant.FractionNumeratorGapMin, style);
            float denGap = C(display ? MathConstant.FractionDenomDisplayStyleGapMin : MathConstant.FractionDenominatorGapMin, style);
            float up = Mathf.Max(numShift, numGap + a + t / 2 + num.Descent);
            float down = Mathf.Max(denShift, denGap - (a - t / 2) + den.Ascent);
            float width = Mathf.Max(num.Width, den.Width);
            Move(num.First, num.End, (width - num.Width) / 2, up);
            Move(den.First, den.End, (width - den.Width) / 2, -down);
            int first = num.First;
            Rule(0, a - t / 2, width, t, false);
            return new Box
            {
                Width = width,
                Ascent = Mathf.Max(up + num.Ascent, a + t / 2),
                Descent = Mathf.Max(down + den.Descent, -(a - t / 2)),
                First = first,
                End = m_Items.Count,
            };
        }

        Box BinomialBox(int node, MathStyle style, bool cramped)
        {
            ref MathNode n = ref m_Tree.Nodes[node];
            if (n.Info >= 0) style = (MathStyle)n.Info;
            bool display = IsDisplay(style);
            int start = m_Items.Count;
            Box top = ListBox(n.A, FractionPartOf(style), cramped);
            Box bottom = ListBox(n.B, FractionPartOf(style), true);
            float up = C(display ? MathConstant.StackTopDisplayStyleShiftUp : MathConstant.StackTopShiftUp, style);
            float down = C(display ? MathConstant.StackBottomDisplayStyleShiftDown : MathConstant.StackBottomShiftDown, style);
            float min = C(display ? MathConstant.StackDisplayStyleGapMin : MathConstant.StackGapMin, style);
            float gap = (up - top.Descent) - (bottom.Ascent - down);
            if (gap < min)
            {
                float more = (min - gap) / 2;
                up += more;
                down += more;
            }
            float width = Mathf.Max(top.Width, bottom.Width);
            Move(top.First, top.End, (width - top.Width) / 2, up);
            Move(bottom.First, bottom.End, (width - bottom.Width) / 2, -down);
            var stack = new Box { Width = width, Ascent = up + top.Ascent, Descent = down + bottom.Descent, First = start, End = m_Items.Count };
            return Wrapped(stack, '(', ')', style);
        }

        // ---------------------------------------------------------------- radicals

        Box RadicalBox(int node, MathStyle style)
        {
            ref MathNode n = ref m_Tree.Nodes[node];
            bool display = IsDisplay(style);
            int start = m_Items.Count;
            Box body = ListBox(n.A, style, true);
            float t = C(MathConstant.RadicalRuleThickness, style);
            float g = C(display ? MathConstant.RadicalDisplayStyleVerticalGap : MathConstant.RadicalVerticalGap, style);
            float target = t + g + body.Ascent + body.Descent;
            Box surd = StretchTo(0x221A, true, target, style);
            float surdHeight = surd.Ascent + surd.Descent;
            if (surdHeight > target) g += (surdHeight - target) / 2;
            float top = body.Ascent + g + t;
            // The bar's top meets the surd's top.
            float surdShift = top - surd.Ascent;
            Move(surd.First, surd.End, 0, surdShift);
            Move(body.First, body.End, surd.Width, 0);
            Rule(surd.Width, top - t, body.Width, t, false);
            float ascent = top + C(MathConstant.RadicalExtraAscender, style);
            float descent = Mathf.Max(body.Descent, surd.Descent - surdShift);
            float width = surd.Width + body.Width;
            int end = m_Items.Count;
            if (n.B >= 0)
            {
                int degreeFirst = m_Items.Count;
                Box degree = ListBox(n.B, MathStyle.ScriptScript, true);
                float before = C(MathConstant.RadicalKernBeforeDegree, style);
                float after = C(MathConstant.RadicalKernAfterDegree, style);
                float raise = m_Font.C(MathConstant.RadicalDegreeBottomRaisePercent) / 100 * surdHeight;
                float bottom = surdShift - surd.Descent;
                float baseline = bottom + raise;
                float offset = before + degree.Width + after;
                if (offset < 0)
                {
                    before -= offset;
                    offset = 0;
                }
                Move(degree.First, degree.End, before, baseline);
                Move(start, degreeFirst, offset, 0);
                width += offset;
                ascent = Mathf.Max(ascent, baseline + degree.Ascent);
                end = m_Items.Count;
            }
            return new Box { Width = width, Ascent = ascent, Descent = descent, First = start, End = end };
        }

        // ---------------------------------------------------------------- scripts

        Box ScriptsBox(int node, MathStyle style, bool cramped)
        {
            ref MathNode n = ref m_Tree.Nodes[node];
            int start = m_Items.Count;
            bool isOp = n.Class == MathClass.Op || (n.A >= 0 && m_Tree.Nodes[n.A].Class == MathClass.Op);
            bool wantsLimits = false;
            if (isOp)
            {
                byte own = n.Flags;
                byte op = m_Tree.Nodes[n.A].Flags;
                if ((own & LetteraMathParser.FlagLimits) != 0) wantsLimits = true;
                else if ((own & LetteraMathParser.FlagNoLimits) != 0) wantsLimits = false;
                else if (IsDisplay(style)) wantsLimits = (op & LetteraMathParser.FlagNoLimits) == 0 && !IsIntegral(m_Tree.Nodes[n.A].Cp, m_Tree.Nodes[n.A].Kind);
            }
            Box body = NodeBoxOf(n.A, style, cramped);
            if (wantsLimits) return LimitsBox(n.B, n.C, body, style, cramped, start);
            MathStyle scriptStyle = ScriptOf(style);
            Box sup = n.B >= 0 ? ListBox(n.B, scriptStyle, cramped) : default;
            Box sub = n.C >= 0 ? ListBox(n.C, scriptStyle, true) : default;
            bool hasSup = n.B >= 0, hasSub = n.C >= 0;

            bool extended = !body.SingleGlyph || body.Extended;
            float Ba = extended ? body.Ascent : 0, Bd = extended ? body.Descent : 0;
            float supShift = 0, subShift = 0;
            if (hasSub)
                subShift = Mathf.Max(C(MathConstant.SubscriptShiftDown, style), sub.Ascent - C(MathConstant.SubscriptTopMax, style), C(MathConstant.SubscriptBaselineDropMin, style) + Bd);
            if (hasSup)
                supShift = Mathf.Max(C(cramped ? MathConstant.SuperscriptShiftUpCramped : MathConstant.SuperscriptShiftUp, style), C(MathConstant.SuperscriptBottomMin, style) + sup.Descent, Ba - C(MathConstant.SuperscriptBaselineDropMax, style));
            if (hasSup && hasSub)
            {
                float gap = (supShift - sup.Descent) - (sub.Ascent - subShift);
                float min = C(MathConstant.SubSuperscriptGapMin, style);
                if (gap < min)
                {
                    subShift += min - gap;
                    float room = C(MathConstant.SuperscriptBottomMaxWithSubscript, style) - (supShift - sup.Descent);
                    if (room > 0)
                    {
                        supShift += room;
                        subShift -= room;
                    }
                    float again = (supShift - sup.Descent) - (sub.Ascent - subShift);
                    if (again < min) subShift += min - again;
                }
            }
            float space = C(MathConstant.SpaceAfterScript, style);
            float width = body.Width;
            // An operator's advance holds its lean: the superscript clears the top at the advance, and the subscript tucks in under it.
            // A letter's does not, so its superscript is what moves.
            float subX = isOp ? body.Width - body.Italic : body.Width;
            float supX = isOp ? body.Width : body.Width + body.Italic;
            if (body.SingleGlyph && m_Font.Data != null)
            {
                if (hasSup) supX += KernAt(body, sup, 0, supShift, style);
                if (hasSub) subX += KernAt(body, sub, 3, subShift, style);
            }
            float ascent = body.Ascent, descent = body.Descent;
            if (hasSup)
            {
                Move(sup.First, sup.End, supX, supShift);
                width = Mathf.Max(width, supX + sup.Width);
                ascent = Mathf.Max(ascent, supShift + sup.Ascent);
                descent = Mathf.Max(descent, sup.Descent - supShift);
            }
            if (hasSub)
            {
                Move(sub.First, sub.End, subX, -subShift);
                width = Mathf.Max(width, subX + sub.Width);
                descent = Mathf.Max(descent, subShift + sub.Descent);
                ascent = Mathf.Max(ascent, sub.Ascent - subShift);
            }
            return new Box { Width = width + space, Ascent = ascent, Descent = descent, First = start, End = m_Items.Count };
        }

        Box NodeBoxOf(int node, MathStyle style, bool cramped)
        {
            MathStyle local = style;
            return NodeBox(node, ref local, cramped);
        }

        static bool IsIntegral(int cp, MathNodeKind kind) => kind == MathNodeKind.Char && ((cp >= 0x222B && cp <= 0x2233) || cp == 0x2A0C);

        // OpenType's procedure: a script tucks in by what both glyphs allow at two heights, taking the smaller sum.
        float KernAt(in Box body, in Box script, int corner, float shift, MathStyle style)
        {
            ILetteraMathData data = m_Font.Data;
            if (data == null || !body.SingleGlyph) return 0;
            float unit = ScaleOf(style) / m_Font.Upem;
            float height = corner == 0 ? shift - script.Descent : body.Descent - shift + script.Ascent;
            return data.Kern(body.Glyph, corner, Mathf.RoundToInt(height / unit)) * unit;
        }

        Box LimitsBox(int upper, int lower, Box body, MathStyle style, bool cramped, int start)
        {
            MathStyle limitStyle = ScriptOf(style);
            Box up = upper >= 0 ? ListBox(upper, limitStyle, cramped) : default;
            Box low = lower >= 0 ? ListBox(lower, limitStyle, true) : default;
            bool hasUp = upper >= 0, hasLow = lower >= 0;
            float width = Mathf.Max(body.Width, Mathf.Max(hasUp ? up.Width : 0, hasLow ? low.Width : 0));
            Move(body.First, body.End, (width - body.Width) / 2, 0);
            float ascent = body.Ascent, descent = body.Descent;
            if (hasUp)
            {
                float rise = Mathf.Max(C(MathConstant.UpperLimitBaselineRiseMin, style), C(MathConstant.UpperLimitGapMin, style) + up.Descent);
                float y = body.Ascent + rise;
                Move(up.First, up.End, (width - up.Width) / 2 + body.Italic / 2, y);
                ascent = y + up.Ascent;
            }
            if (hasLow)
            {
                float drop = Mathf.Max(C(MathConstant.LowerLimitBaselineDropMin, style), C(MathConstant.LowerLimitGapMin, style) + low.Ascent);
                float y = body.Descent + drop;
                Move(low.First, low.End, (width - low.Width) / 2 - body.Italic / 2, -y);
                descent = y + low.Descent;
            }
            return new Box { Width = width, Ascent = ascent, Descent = descent, First = start, End = m_Items.Count };
        }

        // ---------------------------------------------------------------- stretch

        // The one routine for delimiters, radicals, wide accents and arrows: base, a variant, an assembly, the largest, then a font with no table's own means.
        Box StretchTo(int cp, bool vertical, float target, MathStyle style, bool wantsFit = true)
        {
            target = Mathf.Min(target, LetteraMathLimits.StretchEm);
            float scale = ScaleOf(style);
            GlyphRef glyph = FindGlyph(cp, 0);
            if (glyph.Glyph == 0) return Empty();
            float unit = scale / glyph.Font.Upem;
            (float minX, float minY, float maxX, float maxY, float advance) = glyph.Font.Extents(glyph.Glyph);
            float have = vertical ? (maxY - minY) * scale : advance * scale;
            if (have >= target) return GlyphBox(glyph, scale);
            ILetteraMathData data = glyph.Font.Data;
            if (data != null)
            {
                int count = Mathf.Min(data.Variants(glyph.Glyph, vertical, s_Variants, s_Advances), s_Variants.Length);
                uint chosen = 0;
                float chosenSize = 0;
                for (int i = 0; i < count; i++)
                {
                    float size = s_Advances[i] * unit;
                    chosen = s_Variants[i];
                    chosenSize = size;
                    if (size >= target) break;
                }
                bool reached = chosen != 0 && chosenSize >= target;
                int parts = 0;
                int italic = 0;
                if (!reached || chosenSize > MaxVariantEm) parts = data.Assembly(glyph.Glyph, vertical, s_Parts, out italic);
                if (parts > 0) return Assemble(glyph, vertical, target, parts, scale, italic, data);
                if (chosen != 0) return GlyphBox(new GlyphRef(glyph.Font, chosen, glyph.Face), scale);
                return GlyphBox(glyph, scale);
            }
            if (TryPieces(cp, vertical, target, style, out Box pieces)) return pieces;
            // Only ever uniform, since a distance field stays right at uniform scale.
            float factor = Mathf.Min(target / Mathf.Max(have, 0.001f), MaxFallbackScale);
            return GlyphBox(glyph, scale * factor);
        }

        // OpenType's three steps: no extenders at full overlap, then spread the connections, then one more of each extender.
        Box Assemble(in GlyphRef glyph, bool vertical, float target, int count, float scale, int italic, ILetteraMathData data)
        {
            float unit = scale / glyph.Font.Upem;
            float minOverlap = data.MinOverlap(vertical) * unit;
            int extenders = 0;
            float fixedSum = 0, extenderSum = 0;
            for (int i = 0; i < count; i++)
            {
                float advance = s_Parts[i].FullAdvance * unit;
                if (s_Parts[i].Extender)
                {
                    extenders++;
                    extenderSum += advance;
                }
                else
                {
                    fixedSum += advance;
                }
            }
            int copies = 0, total = count - extenders;
            float Longest(int repeat, int pieces) => fixedSum + extenderSum * repeat - minOverlap * Mathf.Max(0, pieces - 1);
            while (extenders > 0 && total + extenders * copies < LetteraMathLimits.AssemblyParts && Longest(copies, total + extenders * copies) < target) copies++;

            var sequence = new ValueList<MathPart>(total + extenders * copies);
            for (int i = 0; i < count; i++)
            {
                if (!s_Parts[i].Extender) sequence.Add(s_Parts[i]);
                else
                    for (int k = 0; k < copies; k++)
                        sequence.Add(s_Parts[i]);
            }
            // What is left goes equally into every connection, as far as each may give.
            float length = Longest(copies, sequence.Count);
            int joints = sequence.Count - 1;
            var overlaps = new float[Mathf.Max(joints, 0)];
            float spare = joints > 0 ? (length - target) / joints : 0;
            for (int i = 0; i < joints; i++)
            {
                float most = Mathf.Min(sequence[i].EndConnector, sequence[i + 1].StartConnector) * unit;
                overlaps[i] = Mathf.Clamp(minOverlap + spare, minOverlap, Mathf.Max(most, minOverlap));
            }
            int first = m_Items.Count;
            float pen = 0, ascent = 0, descent = 0;
            float inkLow = float.MaxValue, inkHigh = float.MinValue;
            for (int i = 0; i < sequence.Count; i++)
            {
                MathPart part = sequence[i];
                (float minX, float minY, float maxX, float maxY, float _) = glyph.Font.Extents(part.Glyph);
                float low = i > 0 ? overlaps[i - 1] / 2 : float.NegativeInfinity;
                float high = i < joints ? part.FullAdvance * unit - overlaps[i] / 2 : float.PositiveInfinity;
                float origin = vertical ? pen - minY * scale : pen;
                float start = vertical ? minY : 0;
                float cropLow = float.IsInfinity(low) ? float.NegativeInfinity : (start + low / scale) * glyph.Font.Upem;
                float cropHigh = float.IsInfinity(high) ? float.PositiveInfinity : (start + high / scale) * glyph.Font.Upem;
                AddGlyph(new GlyphRef(glyph.Font, part.Glyph, glyph.Face), vertical ? 0 : origin, vertical ? origin : 0, scale, true, cropLow, cropHigh, vertical);
                if (vertical) ascent = Mathf.Max(ascent, maxX * scale);
                else
                {
                    ascent = Mathf.Max(ascent, maxY * scale);
                    descent = Mathf.Max(descent, -minY * scale);
                    inkLow = Mathf.Min(inkLow, minY * scale);
                    inkHigh = Mathf.Max(inkHigh, maxY * scale);
                }
                pen += part.FullAdvance * unit - (i < joints ? overlaps[i] : 0);
            }
            var box = new Box { First = first, End = m_Items.Count, Italic = italic * unit, Extended = true };
            if (vertical)
            {
                box.Width = ascent;
                box.Ascent = pen;
                box.Descent = 0;
            }
            else
            {
                box.Width = pen;
                box.Ascent = ascent;
                box.Descent = descent;
                box.InkLow = inkLow;
                box.InkHigh = inkHigh;
            }
            return box;
        }

        // A font with no table, when it covers Unicode's bracket pieces.
        bool TryPieces(int cp, bool vertical, float target, MathStyle style, out Box box)
        {
            box = default;
            if (!vertical) return false;
            int top, middle, bottom, extender;
            switch (cp)
            {
                case '(': top = 0x239B; extender = 0x239C; bottom = 0x239D; middle = 0; break;
                case ')': top = 0x239E; extender = 0x239F; bottom = 0x23A0; middle = 0; break;
                case '[': top = 0x23A1; extender = 0x23A2; bottom = 0x23A3; middle = 0; break;
                case ']': top = 0x23A4; extender = 0x23A5; bottom = 0x23A6; middle = 0; break;
                case '{': top = 0x23A7; middle = 0x23A8; bottom = 0x23A9; extender = 0x23AA; break;
                case '}': top = 0x23AB; middle = 0x23AC; bottom = 0x23AD; extender = 0x23AA; break;
                case '|': top = 0x23D0; extender = 0x23D0; bottom = 0x23D0; middle = 0; break;
                default: return false;
            }
            float scale = ScaleOf(style);
            uint t = m_Font.GlyphOf(top, 0), b = m_Font.GlyphOf(bottom, 0), e = m_Font.GlyphOf(extender, 0), m = middle != 0 ? m_Font.GlyphOf(middle, 0) : 0;
            if (t == 0 || b == 0 || e == 0 || (middle != 0 && m == 0)) return false;
            var font = m_Font;
            float Height(uint g)
            {
                var ex = font.Extents(g);
                return (ex.maxY - ex.minY) * scale;
            }
            float fixedHeight = Height(t) + Height(b) + (m != 0 ? Height(m) : 0);
            float each = Mathf.Max(Height(e), 0.01f);
            int repeat = Mathf.Clamp(Mathf.CeilToInt((target - fixedHeight) / each), 0, LetteraMathLimits.AssemblyParts);
            int first = m_Items.Count;
            float pen = 0, width = 0;
            void Put(uint g)
            {
                var ex = font.Extents(g);
                AddGlyph(new GlyphRef(font, g, 0), 0, pen - ex.minY * scale, scale);
                pen += (ex.maxY - ex.minY) * scale;
                width = Mathf.Max(width, ex.advance * scale);
            }
            Put(b);
            int half = m != 0 ? repeat / 2 : 0;
            for (int i = 0; i < repeat - half; i++) Put(e);
            if (m != 0) Put(m);
            for (int i = 0; i < half; i++) Put(e);
            Put(t);
            box = new Box { Width = width, Ascent = pen, Descent = 0, First = first, End = m_Items.Count, Extended = true };
            return true;
        }

        // Centred on the axis, at that total size.
        Box Centered(Box box, float axis)
        {
            float shift = axis - (box.Ascent - box.Descent) / 2;
            Move(box.First, box.End, 0, shift);
            box.Ascent += shift;
            box.Descent -= shift;
            return box;
        }

        Box FixedDelimiter(int cp, float size, MathStyle style)
        {
            if (cp == 0) return Empty();
            Box box = StretchTo(cp, true, size, style);
            return Centered(box, C(MathConstant.AxisHeight, style));
        }

        Box DelimitedBox(int node, MathStyle style, bool cramped)
        {
            ref MathNode n = ref m_Tree.Nodes[node];
            int start = m_Items.Count;
            float a = C(MathConstant.AxisHeight, style);
            // Every part's height first, and then the delimiters: each runs once, so nesting stays linear.
            var parts = new ValueList<(Box box, int cp)>();
            float ascent = 0, descent = 0;
            for (int segment = n.A; segment >= 0; segment = m_Tree.Nodes[segment].Next)
            {
                Box inner = ListBox(m_Tree.Nodes[segment].A, style, cramped);
                parts.Add((inner, m_Tree.Nodes[segment].Next >= 0 ? m_Tree.Nodes[segment].Cp : 0));
                ascent = Mathf.Max(ascent, inner.Ascent);
                descent = Mathf.Max(descent, inner.Descent);
            }
            float factor = LetteraSettings.Instance.Math.DelimiterFactor;
            float reach = Mathf.Max(ascent - a, descent + a);
            float target = 2 * reach * factor;
            float x = 0;
            float up = ascent, down = descent;
            void Place(ref Box box)
            {
                Move(box.First, box.End, x, 0);
                x += box.Width;
                up = Mathf.Max(up, box.Ascent);
                down = Mathf.Max(down, box.Descent);
            }
            Box left = n.Cp != 0 ? Centered(StretchTo(n.Cp, true, target, style), a) : Empty();
            int leftFirst = left.First, leftEnd = m_Items.Count;
            Place(ref left);
            for (int i = 0; i < parts.Count; i++)
            {
                Box part = parts[i].box;
                Move(part.First, part.End, x, 0);
                x += part.Width;
                if (parts[i].cp != 0)
                {
                    x += 5f / 18 * ScaleOf(style);
                    Box middle = Centered(StretchTo(parts[i].cp, true, target, style), a);
                    Place(ref middle);
                    x += 5f / 18 * ScaleOf(style);
                }
            }
            Box right = n.Cp2 != 0 ? Centered(StretchTo(n.Cp2, true, target, style), a) : Empty();
            Place(ref right);
            _ = leftFirst;
            _ = leftEnd;
            return new Box { Width = x, Ascent = up, Descent = down, First = start, End = m_Items.Count, Extended = true };
        }

        Box Wrapped(Box inner, int leftCp, int rightCp, MathStyle style)
        {
            float a = C(MathConstant.AxisHeight, style);
            float reach = Mathf.Max(inner.Ascent - a, inner.Descent + a);
            Box left = leftCp != 0 ? Centered(StretchTo(leftCp, true, 2 * reach, style), a) : Empty();
            Move(inner.First, inner.End, left.Width, 0);
            Box right = rightCp != 0 ? Centered(StretchTo(rightCp, true, 2 * reach, style), a) : Empty();
            Move(right.First, right.End, left.Width + inner.Width, 0);
            return new Box
            {
                Width = left.Width + inner.Width + right.Width,
                Ascent = Mathf.Max(inner.Ascent, Mathf.Max(left.Ascent, right.Ascent)),
                Descent = Mathf.Max(inner.Descent, Mathf.Max(left.Descent, right.Descent)),
                First = Mathf.Min(inner.First, Mathf.Min(left.First, right.First)),
                End = m_Items.Count,
                Extended = true,
            };
        }

        // ---------------------------------------------------------------- accents, bars and stacks

        Box AccentBox(int node, MathStyle style)
        {
            ref MathNode n = ref m_Tree.Nodes[node];
            bool wide = (n.Flags & LetteraMathParser.FlagWide) != 0;
            int start = m_Items.Count;
            // Dots of i and j give way to an accent.
            Box body = AccentBase(n.A, style);
            float scale = ScaleOf(style);
            float height = C(MathConstant.AccentBaseHeight, style);
            bool flattened = body.Ascent > C(MathConstant.FlattenedAccentBaseHeight, style) && !wide;
            GlyphRef accent = FindGlyph(n.Cp, ScriptLevel(style), false, flattened);
            if (accent.Glyph == 0) accent = FindGlyph(NonCombining(n.Cp), ScriptLevel(style));
            if (accent.Glyph == 0) return new Box { Width = body.Width, Ascent = body.Ascent, Descent = body.Descent, First = start, End = m_Items.Count, Italic = body.Italic };
            int accentFirst = m_Items.Count;
            Box mark = wide ? StretchAccent(n.Cp, accent, body.Width * 0.9f, style) : GlyphBox(accent, scale);
            float attach = accent.Font.Data != null ? accent.Font.Data.TopAccentAttachment(accent.Glyph) / accent.Font.Upem * scale : mark.Width / 2;
            if (wide) attach = mark.Width / 2;
            float bodyAttach = body.SingleGlyph && m_Font.Data != null ? body.Accent / m_Font.Upem * scale : body.Width / 2;
            float raise = Mathf.Max(0, body.Ascent - height);
            Move(mark.First, m_Items.Count, bodyAttach - attach, raise);
            float ascent = Mathf.Max(body.Ascent, raise + mark.Ascent);
            _ = accentFirst;
            return new Box { Width = body.Width, Ascent = ascent, Descent = body.Descent, First = start, End = m_Items.Count, Italic = body.Italic, SingleGlyph = false };
        }

        Box AccentBase(int node, MathStyle style)
        {
            // The usual i and j lose their dots under an accent.
            if (node >= 0)
            {
                int only = node;
                ref MathNode n = ref m_Tree.Nodes[node];
                if (n.Kind == MathNodeKind.List && n.A >= 0 && m_Tree.Nodes[n.A].Next < 0) only = n.A;
                ref MathNode c = ref m_Tree.Nodes[only];
                if (c.Kind == MathNodeKind.Char && (c.Cp == 'i' || c.Cp == 'j'))
                {
                    int mapped = LetteraMathAlphabets.Map(c.Cp, (MathAlphabet)c.Info);
                    uint dotless = m_Font.GlyphOf(mapped, ScriptLevel(style), true);
                    uint plain = m_Font.GlyphOf(mapped, ScriptLevel(style));
                    if (dotless != 0 && dotless != plain) return GlyphBox(new GlyphRef(m_Font, dotless, 0), ScaleOf(style));
                    uint fallback = m_Font.GlyphOf(c.Cp == 'i' ? 0x1D6A4 : 0x1D6A5, ScriptLevel(style));
                    if (fallback != 0) return GlyphBox(new GlyphRef(m_Font, fallback, 0), ScaleOf(style));
                }
            }
            return ListBox(node, style, true);
        }

        Box StretchAccent(int cp, in GlyphRef accent, float target, MathStyle style)
        {
            Box box = StretchTo(cp, false, target, style);
            if (box.End == box.First) box = GlyphBox(accent, ScaleOf(style));
            return box;
        }

        static int NonCombining(int cp)
        {
            switch (cp)
            {
                case 0x0302: return 0x02C6;
                case 0x030C: return 0x02C7;
                case 0x0303: return 0x02DC;
                case 0x0301: return 0x00B4;
                case 0x0300: return 0x0060;
                case 0x0307: return 0x02D9;
                case 0x0308: return 0x00A8;
                case 0x0306: return 0x02D8;
                case 0x0304: return 0x00AF;
                case 0x20D7: return 0x2192;
                case 0x030A: return 0x02DA;
                default: return cp;
            }
        }

        Box BarBox(int node, MathStyle style, bool cramped)
        {
            ref MathNode n = ref m_Tree.Nodes[node];
            int start = m_Items.Count;
            bool over = n.Info == 0;
            Box body = ListBox(n.A, style, over || cramped);
            float t = C(over ? MathConstant.OverbarRuleThickness : MathConstant.UnderbarRuleThickness, style);
            float gap = C(over ? MathConstant.OverbarVerticalGap : MathConstant.UnderbarVerticalGap, style);
            float extra = C(over ? MathConstant.OverbarExtraAscender : MathConstant.UnderbarExtraDescender, style);
            var box = new Box { Width = body.Width, Ascent = body.Ascent, Descent = body.Descent, First = start };
            if (over)
            {
                Rule(0, body.Ascent + gap, body.Width, t, false);
                box.Ascent = body.Ascent + gap + t + extra;
            }
            else
            {
                Rule(0, -body.Descent - gap - t, body.Width, t, false);
                box.Descent = body.Descent + gap + t + extra;
            }
            box.End = m_Items.Count;
            return box;
        }

        Box StretchedBox(int node, MathStyle style, bool cramped)
        {
            ref MathNode n = ref m_Tree.Nodes[node];
            int start = m_Items.Count;
            bool over = n.Info == 0;
            Box body = ListBox(n.A, style, over || cramped);
            Box mark = StretchTo(n.Cp, false, body.Width, style);
            float gap = C(over ? MathConstant.StretchStackGapBelowMin : MathConstant.StretchStackGapAboveMin, style);
            float width = Mathf.Max(body.Width, mark.Width);
            Move(body.First, body.End, (width - body.Width) / 2, 0);
            // By the ink: a brace's glyph may sit far above its baseline, and the gap is to what shows.
            float y = over ? body.Ascent + gap - mark.InkLow : -body.Descent - gap - mark.InkHigh;
            Move(mark.First, mark.End, (width - mark.Width) / 2, y);
            return new Box
            {
                Width = width,
                Ascent = over ? y + mark.InkHigh : body.Ascent,
                Descent = over ? body.Descent : -y - mark.InkLow,
                First = start,
                End = m_Items.Count,
            };
        }

        Box ArrowBox(int node, MathStyle style)
        {
            ref MathNode n = ref m_Tree.Nodes[node];
            int start = m_Items.Count;
            MathStyle annotation = ScriptOf(style);
            Box above = ListBox(n.A, annotation, false);
            Box below = n.B >= 0 ? ListBox(n.B, annotation, true) : default;
            bool hasBelow = n.B >= 0;
            float pad = 0.3f * ScaleOf(style);
            float need = Mathf.Max(Mathf.Max(above.Width, hasBelow ? below.Width : 0) + 2 * pad, n.Value * ScaleOf(style));
            Box arrow = StretchTo(n.Cp, false, need, style);
            float width = Mathf.Max(arrow.Width, need);
            float a = C(MathConstant.AxisHeight, style);
            arrow = Centered(arrow, a);
            Move(arrow.First, arrow.End, (width - arrow.Width) / 2, 0);
            float gapUp = C(MathConstant.StretchStackGapAboveMin, style), gapDown = C(MathConstant.StretchStackGapBelowMin, style);
            float upY = arrow.Ascent + gapUp + above.Descent;
            Move(above.First, above.End, (width - above.Width) / 2, upY);
            float ascent = upY + above.Ascent, descent = arrow.Descent;
            if (hasBelow)
            {
                float downY = arrow.Descent + gapDown + below.Ascent;
                Move(below.First, below.End, (width - below.Width) / 2, -downY);
                descent = downY + below.Descent;
            }
            return new Box { Width = width, Ascent = ascent, Descent = descent, First = start, End = m_Items.Count };
        }

        Box StackBox(int node, MathStyle style, bool cramped)
        {
            ref MathNode n = ref m_Tree.Nodes[node];
            int start = m_Items.Count;
            bool under = n.Info == 1;
            Box annotation = ListBox(n.A, ScriptOf(style), under || cramped);
            Box body = ListBox(n.B, style, cramped);
            float width = Mathf.Max(annotation.Width, body.Width);
            Move(body.First, body.End, (width - body.Width) / 2, 0);
            float ascent = body.Ascent, descent = body.Descent;
            if (under)
            {
                float drop = Mathf.Max(C(MathConstant.LowerLimitBaselineDropMin, style), C(MathConstant.LowerLimitGapMin, style) + annotation.Ascent);
                float y = body.Descent + drop;
                Move(annotation.First, annotation.End, (width - annotation.Width) / 2, -y);
                descent = y + annotation.Descent;
            }
            else
            {
                float rise = Mathf.Max(C(MathConstant.UpperLimitBaselineRiseMin, style), C(MathConstant.UpperLimitGapMin, style) + annotation.Descent);
                float y = body.Ascent + rise;
                Move(annotation.First, annotation.End, (width - annotation.Width) / 2, y);
                ascent = y + annotation.Ascent;
            }
            return new Box { Width = width, Ascent = ascent, Descent = descent, First = start, End = m_Items.Count };
        }

        // ---------------------------------------------------------------- tables

        Box TableBox(int node, MathStyle style)
        {
            ref MathNode n = ref m_Tree.Nodes[node];
            string name = m_Tree.Strings[n.Text];
            string columns = n.Cp >= 0 ? m_Tree.Strings[n.Cp] : null;
            MathStyle cellStyle;
            float columnGap;
            bool alignPairs = false, leftFirst = false;
            float strut = 1;
            float scale = m_Size;
            switch (name)
            {
                case "smallmatrix":
                case "substack":
                    cellStyle = ScriptOf(style);
                    columnGap = 1f / 3;
                    break;
                case "cases":
                    cellStyle = MathStyle.Text;
                    columnGap = 1;
                    leftFirst = true;
                    strut = 1.2f;
                    break;
                case "dcases":
                    cellStyle = MathStyle.Display;
                    columnGap = 1;
                    leftFirst = true;
                    strut = 1.2f;
                    break;
                case "aligned": case "align": case "align*": case "split": case "alignedat":
                    cellStyle = MathStyle.Display;
                    columnGap = 2;
                    alignPairs = true;
                    break;
                case "gather": case "gather*": case "gathered": case "multline": case "multline*": case "equation": case "equation*": case "displaymath":
                    cellStyle = MathStyle.Display;
                    columnGap = 0;
                    break;
                default:
                    cellStyle = MathStyle.Text;
                    columnGap = 1;
                    break;
            }
            float rowSkip = 1.2f * ScaleOf(cellStyle) * strut;
            float strutUp = 0.7f * rowSkip, strutDown = 0.3f * rowSkip;
            int start = m_Items.Count;

            var cells = new List<ValueList<(Box box, bool emptyLeading)>>();
            var rowFlags = new ValueList<(int rule, float extra)>();
            int columnCount = 0;
            for (int row = n.A; row >= 0; row = m_Tree.Nodes[row].Next)
            {
                var line = new ValueList<(Box, bool)>();
                int c = 0;
                for (int cell = m_Tree.Nodes[row].A; cell >= 0; cell = m_Tree.Nodes[cell].Next, c++)
                {
                    Box box = ListBox(m_Tree.Nodes[cell].A, cellStyle, false);
                    // A pair's right side reads as following something, so its relation keeps its space.
                    if (alignPairs && c % 2 == 1 && FirstClassIn(m_Tree.Nodes[cell].A) == MathClass.Rel)
                    {
                        float gap = SpaceBetween(MathClass.Ord, MathClass.Rel, false) * ScaleOf(cellStyle);
                        Move(box.First, box.End, gap, 0);
                        box.Width += gap;
                    }
                    line.Add((box, false));
                }
                cells.Add(line);
                rowFlags.Add((m_Tree.Nodes[row].Info, m_Tree.Nodes[row].Value));
                columnCount = Mathf.Max(columnCount, line.Count);
            }
            // A last row left empty by a closing \\ is no row.
            if (cells.Count > 1 && cells[cells.Count - 1].Count == 1 && cells[cells.Count - 1][0].Item1.End == cells[cells.Count - 1][0].Item1.First && rowFlags[rowFlags.Count - 1].rule == 0)
            {
                cells.RemoveAt(cells.Count - 1);
                rowFlags.RemoveAt(rowFlags.Count - 1);
            }

            var widths = new float[columnCount];
            foreach (ValueList<(Box box, bool)> line in cells)
                for (int c = 0; c < line.Count; c++)
                    widths[c] = Mathf.Max(widths[c], line[c].box.Width);

            bool[] rules = null;
            char[] align = ParseColumns(columns, columnCount, out bool[] leftRules, out bool[] dashed);
            rules = leftRules;

            // Columns left to right.
            var xs = new float[columnCount + 1];
            float x = rules != null && rules[0] ? 0.2f : 0;
            for (int c = 0; c < columnCount; c++)
            {
                xs[c] = x;
                x += widths[c];
                if (c < columnCount - 1)
                {
                    float gap = alignPairs ? (c % 2 == 0 ? 0 : columnGap) : columnGap;
                    x += gap * ScaleOf(cellStyle);
                }
            }
            xs[columnCount] = x;
            float width = x + (rules != null && columnCount < rules.Length && rules[columnCount] ? 0.2f : 0);

            // Rows top to bottom, measured from the first baseline.
            float y = 0, totalUp = 0;
            var baselines = new float[cells.Count];
            float previousDown = 0;
            for (int r = 0; r < cells.Count; r++)
            {
                float up = strutUp, down = strutDown;
                foreach ((Box box, bool _) in cells[r])
                {
                    up = Mathf.Max(up, box.Ascent);
                    down = Mathf.Max(down, box.Descent);
                }
                if (r == 0) totalUp = up;
                else y -= previousDown + up;
                baselines[r] = y;
                previousDown = down + rowFlags[r].extra;
            }
            float bottom = y - previousDown;
            float height = totalUp - bottom;
            // The whole table centres on the axis.
            float axis = C(MathConstant.AxisHeight, style);
            float lift = axis + height / 2 - totalUp;
            if (node == m_NumberedTable)
            {
                m_Rows.Clear();
                int row = n.A;
                for (int r = 0; r < cells.Count; r++, row = m_Tree.Nodes[row].Next)
                {
                    float up = strutUp, down = strutDown;
                    foreach ((Box box, bool _) in cells[r])
                    {
                        up = Mathf.Max(up, box.Ascent);
                        down = Mathf.Max(down, box.Descent);
                    }
                    m_Rows.Add((row, baselines[r] + lift + (up - down) / 2));
                }
            }
            for (int r = 0; r < cells.Count; r++)
            {
                for (int c = 0; c < cells[r].Count; c++)
                {
                    Box box = cells[r][c].box;
                    float offset;
                    char mode = align != null ? align[Mathf.Min(c, align.Length - 1)] : alignPairs ? (c % 2 == 0 ? 'r' : 'l') : leftFirst ? 'l' : name.StartsWith("multline", StringComparison.Ordinal) ? (r == 0 ? 'l' : r == cells.Count - 1 ? 'r' : 'c') : 'c';
                    if (mode == 'l') offset = 0;
                    else if (mode == 'r') offset = widths[c] - box.Width;
                    else offset = (widths[c] - box.Width) / 2;
                    Move(box.First, box.End, xs[c] + offset, baselines[r] + lift);
                }
            }
            float rule = C(MathConstant.FractionRuleThickness, MathStyle.Text);
            float top = lift + totalUp, floor = lift + bottom;
            if (rules != null)
            {
                for (int c = 0; c <= columnCount; c++)
                {
                    if (c >= rules.Length || !rules[c]) continue;
                    float rx = c == 0 ? 0 : c == columnCount ? width - rule : xs[c] - columnGap * ScaleOf(cellStyle) / 2;
                    if (dashed[c]) Dashed(rx, floor, top - floor, rule, true);
                    else Rule(rx, floor, top - floor, rule, true);
                }
            }
            for (int r = 0; r < cells.Count; r++)
            {
                int flags = rowFlags[r].rule;
                if (flags == 0) continue;
                float ry = baselines[r] + lift + (r == 0 ? totalUp : strutUp);
                if ((flags & LetteraMathParser.RowDashed) != 0) Dashed(0, ry, width, rule, false);
                else Rule(0, ry, width, rule, false);
            }
            var table = new Box { Width = width, Ascent = top, Descent = -floor, First = start, End = m_Items.Count, Extended = true };
            switch (name)
            {
                case "pmatrix": return Wrapped(table, '(', ')', style);
                case "bmatrix": return Wrapped(table, '[', ']', style);
                case "Bmatrix": return Wrapped(table, '{', '}', style);
                case "vmatrix": return Wrapped(table, '|', '|', style);
                case "Vmatrix": return Wrapped(table, 0x2225, 0x2225, style);
                case "cases": case "dcases": return Wrapped(table, '{', 0, style);
                default: return table;
            }
        }

        MathClass FirstClassIn(int list)
        {
            if (list < 0) return MathClass.Ord;
            int first = m_Tree.Nodes[list].A;
            return first >= 0 ? EffectiveClass(first) : MathClass.Ord;
        }

        void Dashed(float x, float y, float length, float thickness, bool vertical)
        {
            float step = thickness * 6, on = thickness * 3;
            int count = Mathf.Min(Mathf.CeilToInt(length / Mathf.Max(step, 0.001f)), 512);
            for (int i = 0; i < count; i++)
            {
                float at = i * step;
                float piece = Mathf.Min(on, length - at);
                if (piece <= 0) break;
                if (vertical) Rule(x, y + at, piece, thickness, true);
                else Rule(x + at, y, piece, thickness, false);
            }
        }

        static char[] ParseColumns(string columns, int count, out bool[] rules, out bool[] dashed)
        {
            rules = null;
            dashed = null;
            if (string.IsNullOrEmpty(columns)) return null;
            var align = new List<char>();
            rules = new bool[count + 2];
            dashed = new bool[count + 2];
            foreach (char c in columns)
            {
                if (c == 'l' || c == 'c' || c == 'r') align.Add(c);
                else if (c == '|' || c == ':')
                {
                    int at = Mathf.Min(align.Count, rules.Length - 1);
                    rules[at] = true;
                    dashed[at] = c == ':';
                }
            }
            return align.Count > 0 ? align.ToArray() : null;
        }
    }

    /// <summary>Unicode's mathematical alphabets, with their holes filled where they stand elsewhere.</summary>
    internal static class LetteraMathAlphabets
    {
        /// <summary>The character an alphabet makes of a letter. Others stay themselves.</summary>
        internal static int Map(int cp, MathAlphabet alphabet)
        {
            bool upper = cp >= 'A' && cp <= 'Z', lower = cp >= 'a' && cp <= 'z', digit = cp >= '0' && cp <= '9';
            bool greekLower = cp >= 0x3B1 && cp <= 0x3C9;
            bool greekUpper = cp >= 0x391 && cp <= 0x3A9;
            switch (alphabet)
            {
                case MathAlphabet.Default:
                    if (upper) return Italic(cp);
                    if (lower) return Italic(cp);
                    if (greekLower) return 0x1D6FC + (cp - 0x3B1);
                    return cp;
                case MathAlphabet.Roman:
                    return cp;
                case MathAlphabet.Italic:
                    if (upper || lower) return Italic(cp);
                    if (greekLower) return 0x1D6FC + (cp - 0x3B1);
                    if (greekUpper) return 0x1D6E2 + (cp - 0x391);
                    return cp;
                case MathAlphabet.Bold:
                    if (upper) return 0x1D400 + (cp - 'A');
                    if (lower) return 0x1D41A + (cp - 'a');
                    if (digit) return 0x1D7CE + (cp - '0');
                    if (greekUpper) return 0x1D6A8 + (cp - 0x391);
                    if (greekLower) return 0x1D6C2 + (cp - 0x3B1);
                    return cp;
                case MathAlphabet.BoldSymbol:
                    if (upper) return 0x1D468 + (cp - 'A');
                    if (lower) return 0x1D482 + (cp - 'a');
                    if (digit) return 0x1D7CE + (cp - '0');
                    if (greekUpper) return 0x1D71C + (cp - 0x391);
                    if (greekLower) return 0x1D736 + (cp - 0x3B1);
                    return cp;
                case MathAlphabet.SansSerif:
                    if (upper) return 0x1D5A0 + (cp - 'A');
                    if (lower) return 0x1D5BA + (cp - 'a');
                    if (digit) return 0x1D7E2 + (cp - '0');
                    return cp;
                case MathAlphabet.Monospace:
                    if (upper) return 0x1D670 + (cp - 'A');
                    if (lower) return 0x1D68A + (cp - 'a');
                    if (digit) return 0x1D7F6 + (cp - '0');
                    return cp;
                case MathAlphabet.DoubleStruck:
                    switch (cp)
                    {
                        case 'C': return 0x2102;
                        case 'H': return 0x210D;
                        case 'N': return 0x2115;
                        case 'P': return 0x2119;
                        case 'Q': return 0x211A;
                        case 'R': return 0x211D;
                        case 'Z': return 0x2124;
                    }
                    if (upper) return 0x1D538 + (cp - 'A');
                    if (lower) return 0x1D552 + (cp - 'a');
                    if (digit) return 0x1D7D8 + (cp - '0');
                    return cp;
                case MathAlphabet.Script:
                    switch (cp)
                    {
                        case 'B': return 0x212C;
                        case 'E': return 0x2130;
                        case 'F': return 0x2131;
                        case 'H': return 0x210B;
                        case 'I': return 0x2110;
                        case 'L': return 0x2112;
                        case 'M': return 0x2133;
                        case 'R': return 0x211B;
                        case 'e': return 0x212F;
                        case 'g': return 0x210A;
                        case 'o': return 0x2134;
                    }
                    if (upper) return 0x1D49C + (cp - 'A');
                    if (lower) return 0x1D4B6 + (cp - 'a');
                    return cp;
                case MathAlphabet.Fraktur:
                    switch (cp)
                    {
                        case 'C': return 0x212D;
                        case 'H': return 0x210C;
                        case 'I': return 0x2111;
                        case 'R': return 0x211C;
                        case 'Z': return 0x2128;
                    }
                    if (upper) return 0x1D504 + (cp - 'A');
                    if (lower) return 0x1D51E + (cp - 'a');
                    return cp;
                default:
                    return cp;
            }
        }

        static int Italic(int cp)
        {
            if (cp == 'h') return 0x210E;
            return cp >= 'a' ? 0x1D434 + 26 + (cp - 'a') : 0x1D434 + (cp - 'A');
        }
    }
#endif
}

using System;
using System.Collections.Generic;

namespace FirstSymbol.Lettera
{
    /// <summary>A markup a person can try for a tag: the value after the equals sign, and what it gives. An empty value is the bare tag.</summary>
    public sealed class LetteraTagExample
    {
        public readonly string Value, Note;

        public LetteraTagExample(string value, string note)
        {
            Value = value ?? "";
            Note = note ?? "";
        }
    }

    /// <summary>What an effect says about itself to a person writing its tag: what it does, whether it takes values, and examples.</summary>
    internal sealed class LetteraEffectFacts
    {
        public string Description;
        public LetteraParamInfo[] Params;
        /// <summary>The effect reads no tag value. Said on purpose, so that "takes none" and "not described yet" differ.</summary>
        public bool None;
        /// <summary>A tag whose value is split by semicolons or goes to another effect; the panel draws its own form.</summary>
        public bool Delegates;
        public LetteraTagExample[] Examples = Array.Empty<LetteraTagExample>();
    }

    /// <summary>The built-in tags' words, in one place so the panel, the picker and the docs read the same text.</summary>
    internal static class LetteraBuiltInTags
    {
        static Dictionary<Type, LetteraEffectFacts> s_Facts;

        /// <summary>The facts of an effect type or of the nearest built-in type it derives from; null for an effect of your own.</summary>
        public static LetteraEffectFacts Of(Type type)
        {
            Dictionary<Type, LetteraEffectFacts> map = s_Facts ?? (s_Facts = Build());
            for (; type != null; type = type.BaseType)
                if (map.TryGetValue(type, out LetteraEffectFacts facts))
                    return facts;
            return null;
        }

        /// <summary>Every type the table describes, for the test that no built-in effect is left out.</summary>
        public static IEnumerable<Type> Described => (s_Facts ?? (s_Facts = Build())).Keys;

        static LetteraTagExample Ex(string value, string note) => new LetteraTagExample(value, note);

        static void Add<T>(Dictionary<Type, LetteraEffectFacts> map, string description, LetteraParamInfo[] parameters, params LetteraTagExample[] examples) where T : LetteraEffect =>
            map[typeof(T)] = new LetteraEffectFacts { Description = description, Params = parameters, Examples = examples };

        static void AddNone<T>(Dictionary<Type, LetteraEffectFacts> map, string description, params LetteraTagExample[] examples) where T : LetteraEffect =>
            map[typeof(T)] = new LetteraEffectFacts { Description = description, None = true, Examples = examples };

        // A motion keeps its own descriptors; the table only gives it words.
        static void AddMotion<T>(Dictionary<Type, LetteraEffectFacts> map, string description, params LetteraTagExample[] examples) where T : LetteraEffect =>
            map[typeof(T)] = new LetteraEffectFacts { Description = description, Examples = examples };

        static Dictionary<Type, LetteraEffectFacts> Build()
        {
            var map = new Dictionary<Type, LetteraEffectFacts>();

            Add<BoldEffect>(map, "Makes the text heavier. Weight 700 is bold; 100 is thin and 900 black.", LetteraBuiltInSlots.Bold,
                Ex("", "Bold at the effect's weight"), Ex("900", "Black"), Ex("600,r", "Semibold, from real faces only"));
            Add<ItalicEffect>(map, "Slants the text. Uses the font's italic face when it has one, otherwise shears the glyphs.", LetteraBuiltInSlots.Italic,
                Ex("", "Italic"), Ex("14", "Synthetic slant of 14 percent"));
            Add<SizeEffect>(map, "Changes the size of the text inside the tag.", LetteraBuiltInSlots.Size,
                Ex("150%", "One and a half times the size around it"), Ex("+10", "Ten more than the size around it"), Ex("24", "Exactly 24"));
            Add<ColorEffect>(map, "Colours the text.", LetteraBuiltInSlots.Color, Ex("#FF8800", "Orange"), Ex("red", "A named colour"), Ex("#00000080", "Half-transparent black"));
            Add<AlphaEffect>(map, "Makes the text see-through. 0 is invisible and 1 solid.", LetteraBuiltInSlots.Alpha, Ex("0.5", "Half"), Ex("25%", "A quarter"), Ex("#80", "A hex pair, 80 is half"));
            Add<FontEffect>(map, "Switches the font family for the text inside.", LetteraBuiltInSlots.Font, Ex("Roboto", "A family from the font stack"));
            Add<LanguageEffect>(map, "Says which language the text is in, so shaping and line breaks follow its rules.", LetteraBuiltInSlots.Language, Ex("ru", "Russian"), Ex("zh-Hans", "Simplified Chinese"));
            Add<UnderlineEffect>(map, "Draws a line under the text. Colour, style, thickness and position can change.", LetteraBuiltInSlots.Line,
                Ex("", "A plain underline"), Ex("#F00,wavy", "Red and wavy"), Ex(",double", "Double, in the text colour"));
            Add<StrikethroughEffect>(map, "Draws a line through the text. Takes the same values as an underline.", LetteraBuiltInSlots.Line,
                Ex("", "A plain line through"), Ex("#F00,,0.12", "Red and thicker"));
            Add<OutlineEffect>(map, "A thin edge around each glyph.", LetteraBuiltInSlots.Outline, Ex("#000,0.08", "Black, 0.08 em wide"), Ex("white,2px", "White, 2 px wide"));
            Add<ShadowEffect>(map, "A soft copy of the text behind it, moved by x and y.", LetteraBuiltInSlots.Shadow,
                Ex("", "The effect's own shadow"), Ex("#000C,0.08,-0.08,0.4", "Dark, down and right, a little soft"));
            Add<GlowEffect>(map, "A halo around the glyphs.", LetteraBuiltInSlots.Glow, Ex("#FF0", "Yellow halo"), Ex("#0FF,0.3,1", "Wide cyan halo, fully soft"));
            Add<StrokeEffect>(map, "A band that follows the outline, inside, centred on it or outside.", LetteraBuiltInSlots.Stroke,
                Ex("#000,0.06", "Black, 0.06 em wide"), Ex("#F80,0.1,center", "Orange, centred on the edge"));
            Add<FillEffect>(map, "Takes over the face of the glyphs with a paint: a colour, gradient or texture swatch.", LetteraBuiltInSlots.Fill, Ex("#09F", "A flat colour"), Ex("sunset", "A swatch named sunset"));
            Add<HighlightEffect>(map, "A background shape behind (or above) the text, like a marker.", LetteraBuiltInSlots.Mark,
                Ex("", "Yellow marker"), Ex("#FF000040,line", "Red wash per line"), Ex("#0F08,block,,0.2", "Green block with rounded corners"));
            Add<TrackingEffect>(map, "Adds space after every letter.", LetteraBuiltInSlots.Tracking, Ex("2", "Two local units"), Ex("0.05em", "A twentieth of the size"), Ex("-1", "Tighter"));
            Add<LineHeightEffect>(map, "Sets the height of the lines the range touches.", LetteraBuiltInSlots.LineHeight,
                Ex("1.4", "One and four tenths times the natural height"), Ex("30px", "Exactly 30"), Ex("120%", "120 percent"));
            Add<IndentEffect>(map, "Moves the lines of a paragraph in from its start edge. Nested indents add up.", LetteraBuiltInSlots.Indent, Ex("24", "24 local units"), Ex("2em", "Two sizes"), Ex("10%", "A tenth of the width"));
            Add<AlignEffect>(map, "Sets how the paragraphs it covers sit in the box.", LetteraBuiltInSlots.Align, Ex("Center", "Centred"), Ex("Justify,InterWord", "Justified by word gaps"), Ex("Justify,,Center", "Justified, last line centred"));
            Add<SuperSubEffect>(map, "Raises the text above the line (sup) or lowers it below (sub), as in x2 and H2O.", LetteraBuiltInSlots.SuperSub, Ex("", "As the tag name says"));

            Add<InnerShadowEffect>(map, "A shadow cast inward from the edge of each glyph, for a pressed-in look.", LetteraBuiltInSlots.InnerShadow, Ex("", "Soft dark inner shadow"), Ex("#000A,0.04,-0.04,0.3", "Stronger, a little softer"));
            Add<ExtrudeEffect>(map, "Stacks copies behind the glyphs for a flat sense of depth.", LetteraBuiltInSlots.Extrude, Ex("0.06,-0.06", "Down and right"), Ex("0.1,-0.1,#FF8800,#802000", "Orange fading to brown"));
            Add<FilterEffect>(map, "Changes the colours of everything under it: grayscale, invert or sepia.", LetteraBuiltInSlots.Filter, Ex(",grayscale", "Grayscale"), Ex("50%,sepia", "Half sepia"), Ex("1,invert", "Inverted"));
            AddNone<UnionEffect>(map, "Merges overlapping glyphs into one shape, so script fonts show no seams and see-through paint does not add up.", Ex("", "Merge overlaps"));
            Add<WarpEffect>(map, "Makes the outline wavy while keeping its edge smooth.", LetteraBuiltInSlots.Warp, Ex("0.03,0.2", "A gentle ripple"), Ex("0.06,0.5,2", "Bigger and flowing"));
            AddNone<UppercaseEffect>(map, "Shows the text in capitals. The stored text keeps its case.", Ex("", "Capitals"));
            AddNone<LowercaseEffect>(map, "Shows the text in small letters. The stored text keeps its case.", Ex("", "Small letters"));
            Add<SmallCapsEffect>(map, "Small capitals: the font's own where it has them, otherwise scaled-down capitals.", LetteraBuiltInSlots.SmallCaps, Ex("", "Small capitals"), Ex("0.7", "Smaller made-up ones"));
            Add<OpenTypeEffect>(map, "Turns OpenType font features on or off, written as HarfBuzz writes them.", LetteraBuiltInSlots.OpenType,
                Ex("onum", "Old-style numerals"), Ex("-liga,smcp", "No ligatures, small caps"), Ex("cv02 3", "Character variant 2, value 3"));
            Add<AxesEffect>(map, "Sets the axes of a variable font: weight, width, slant and more.", LetteraBuiltInSlots.Axes,
                Ex("640", "Weight 640"), Ex("~,120%", "Weight untouched, width 120 percent of the default"), Ex("GRAD 50", "A named axis, GRAD, at 50"));
            Add<SizeMatchEffect>(map, "Scales fallback fonts so their x-height or cap height matches the main font. Applies to the whole text.", LetteraBuiltInSlots.SizeMatch, Ex("XHeight", "Match x-height"), Ex("CapHeight", "Match capitals"));
            Add<RubyEffect>(map, "A small reading above (or below) a base text, used for furigana and pinyin.", LetteraBuiltInSlots.Ruby, Ex("かんじ", "Writes the reading over the base"));
            Add<WordSpacingEffect>(map, "Changes the gap at spaces and nothing between letters.", LetteraBuiltInSlots.WordSpacing, Ex("4", "Four local units more"), Ex("0.2em", "Wider"));
            Add<CellWidthEffect>(map, "Gives every letter a cell of the same width with the glyph centred, as in tables of digits.", LetteraBuiltInSlots.Cell, Ex("auto", "As wide as the widest glyph"), Ex("0.6em", "Cells of 0.6 em"));
            Add<ParagraphSpacingEffect>(map, "Adds space after and before the paragraphs the range starts.", LetteraBuiltInSlots.ParagraphSpacing, Ex("12", "12 units after"), Ex("12,6", "12 after, 6 before"));
            Add<LineGapEffect>(map, "Opens space after the range, which moves the lines that follow.", LetteraBuiltInSlots.LineGap, Ex("10", "10 local units"), Ex("1em", "One size"));
            AddNone<NoBreakEffect>(map, "Keeps the text together on one line. Its edges can still break.", Ex("", "Keep together"));
            Add<ListEffect>(map, "A list item: a paragraph with a bullet or a number in front. Markers are not part of the text.", LetteraBuiltInSlots.ListItem,
                Ex("", "A bullet"), Ex("1", "A nested item"), Ex("0,5", "Numbered, starting at 5"), Ex("0,,★", "A marker of your own"));
            Add<BoxTrimEffect>(map, "Removes the space above the first line and below the last, as CSS text-box-trim does. Applies to the whole text.", LetteraBuiltInSlots.Trim, Ex("CapHeight,Baseline", "Trim to capitals and baseline"));
            Add<CurveEffect>(map, "Bends each line onto a circle. The value is the circle's radius, not an angle.", LetteraBuiltInSlots.Curve,
                Ex("180", "A circle of radius 180: the line arcs like a rainbow"), Ex("-180", "The same, bending the other way"), Ex("90,0.5", "A tight circle, held at the middle of each line"));
            Add<DividerEffect>(map, "A separator that lines may break at; at a break the separator is not drawn.", LetteraBuiltInSlots.Divider, Ex("", "The style's separator"), Ex("\" · \"", "A dot with spaces"));
            Add<EllipsisEffect>(map, "Replaces what does not fit with an ellipsis mark.", LetteraBuiltInSlots.Ellipsis, Ex("", "Cut at the end"), Ex("0.5", "Cut in the middle"));
            Add<TruncateEffect>(map, "Cuts off what does not fit, optionally with a soft fade.", LetteraBuiltInSlots.Truncate, Ex("", "Hard cut at the end"), Ex("1,1em", "Fade out over one size"));
            Add<DirectionEffect>(map, "Sets the base direction of every paragraph, even when bound to a range.", LetteraBuiltInSlots.Direction, Ex("rtl", "Right to left"), Ex("ltr", "Left to right"));
            Add<LinkEffect>(map, "A range that opens an address when it is activated. It has no look of its own; the <a> tag adds a colour and an underline.", LetteraBuiltInSlots.Link, Ex("https://example.com", "Opens a page"));
            Add<ActionEffect>(map, "A range that raises events and does nothing else: mentions, hashtags and your own actions.", LetteraBuiltInSlots.RangeAction, Ex("player-42", "A value your code receives"));
            Add<SpoilerEffect>(map, "Text under a cover until someone taps it.", LetteraBuiltInSlots.Spoiler, Ex("", "A plain spoiler"), Ex("ending", "With a value your code receives"));
            Add<InlineEffect>(map, "Puts a sprite or an object in the line as one character.", LetteraBuiltInSlots.Inline, Ex("coin", "The item named coin"), Ex("coin,1.4", "Larger"), Ex("coin,,,,,,,,#F80", "Tinted orange"));
            Add<LetteraMathEffect>(map, "Draws a formula in the line. The formula is the text between the opening and closing tags, written in LaTeX.", LetteraBuiltInSlots.Formula, Ex("", "Inline formula"), Ex("display", "On a line of its own"), Ex("display,Euler identity", "With text for screen readers"));
            AddNone<LetteraTableEffect>(map, "Draws a LaTeX table. It is written as \\begin{tabular} ... \\end{tabular}, not as a tag, so there is nothing to set here.");
            Add<RevealEffect>(map, "Shows only the first part of the text, for typewriter effects. Move the reach from code or a timeline.", null,
                Ex("fade,50%", "Half shown, letters fade in"), Ex("pop,100%,Word", "Everything, word by word, with a pop"));
            AddNone<MarqueeEffect>(map, "Scrolls a text sideways through its box. The speed, gap and pause are settings of the style, not of a tag. Applies to the whole text.");
            Add<ResolutionEffect>(map, "Raises the density of the glyph atlas for big headings so they stay crisp.", LetteraBuiltInSlots.Resolution, Ex("1", "One step up"), Ex("2", "Two steps"));
            Add<PaintOrderEffect>(map, "Chooses whether each layer is drawn over all glyphs, or all layers of one glyph before the next. Applies to the whole text.", LetteraBuiltInSlots.PaintOrder, Ex("GlyphMajor", "Finish each glyph first"));
            Add<MaterialEffect>(map, "Draws the range with a Unity material of your own. Set the material on the style.", LetteraBuiltInSlots.Material, Ex("#FFFFFF", "Tint white"));
            AddNone<SemanticEffect>(map, "Describes a range for screen readers. Its role, label and hint are settings of the style, not of a tag.");
            AddNone<NoEffect>(map, "Does nothing. Use it for a matcher whose ranges only need to exist, for code to find.");

            map[typeof(CompositeEffect)] = new LetteraEffectFacts { Description = "Several effects under one tag. The value has one part per effect, separated by semicolons.", Delegates = true };
            map[typeof(PresetEffect)] = new LetteraEffectFacts { Description = "Applies an effect preset asset. The value goes to the preset's first effect.", Delegates = true };

            AddMotion<WaveMotion>(map, "Letters bob up and down and sway, one after the other.", Ex("", "The default wave"), Ex("0.3,0,1.5", "Taller and faster"));
            AddMotion<WobbleMotion>(map, "Letters rock from side to side and squash a little.", Ex("", "The default wobble"), Ex("12,0.1,2", "A bigger, quicker rock"));
            AddMotion<ShakeMotion>(map, "Letters jitter and hold still between jumps.", Ex("", "The default shake"), Ex("0.08,20,5", "Harder, with a little turning"));
            AddMotion<BounceMotion>(map, "Letters hop and never go below where they rest.", Ex("", "The default bounce"), Ex("0.5,1.2", "Higher hops"));
            AddMotion<FloatMotion>(map, "Letters drift on smooth noise.", Ex("", "The default drift"), Ex("0.15,0.1", "Further drift"));
            AddMotion<SpinMotion>(map, "Letters turn round.", Ex("", "The default spin"), Ex("1,0", "One turn a second, all together"));
            AddMotion<PulseMotion>(map, "Letters grow and shrink.", Ex("", "The default pulse"), Ex("0.2,2", "Stronger, twice a second"));
            AddMotion<PendulumMotion>(map, "Letters swing from their top edge like a hanging sign.", Ex("", "The default swing"), Ex("25,1", "Wider, quicker"));
            AddMotion<DeformMotion>(map, "The outline of the glyphs flows like water.", Ex("", "The default flow"), Ex("0.05,0.3,2", "Bigger and faster"));
            AddMotion<GlitchMotion>(map, "Letters jerk and split into colour fringes in short bursts.", Ex("", "The default glitch"), Ex("0.06,12,0.3", "More often, wider fringes"));
            AddMotion<ScrambleMotion>(map, "Random symbols settle into the real text, in reading order.", Ex("", "The default scramble"), Ex("2", "Takes two seconds"));
            AddMotion<RollMotion>(map, "Characters turn on a wheel and settle on their value, like a counter.", Ex("", "The default roll"), Ex("2,30", "Two seconds, a longer spin"));
            AddMotion<RangeTintEffect>(map, "A pose-level tint for feedback such as hover. It costs no mesh pass.", Ex("0.5", "Half way to the tint colour"));

            return map;
        }
    }
}

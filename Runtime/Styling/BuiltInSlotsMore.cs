using System;

namespace FirstSymbol.Lettera
{
    // The slots of the rest of the built-in tags. Every position is one the effect reads; the slot test runs each effect to prove it.
    internal static partial class LetteraBuiltInSlots
    {
        const LetteraSlotUnits SpaceUnits = LetteraSlotUnits.Bare | LetteraSlotUnits.Px | LetteraSlotUnits.Em | LetteraSlotUnits.Percent;
        const string Degrees = "°";

        static LetteraParamInfo Spacing(int slot, string name, string hint, string defaultField, string example = null) =>
            LetteraParamInfo.ForSlot(slot, name, LetteraParamKind.Length, hint, units: SpaceUnits, bareMeans: LetteraLength.Unit.Pixels, step: 0.5f, decimals: 2,
                defaultField: defaultField, essential: slot == 0, example: example);

        static LetteraParamInfo Choice(int slot, string name, string hint, string[] choices, string[] labels, string defaultField = null, string defaultText = null, bool essential = false) =>
            LetteraParamInfo.ForSlot(slot, name, LetteraParamKind.Choice, hint, choices: choices, choiceLabels: labels, defaultField: defaultField, defaultText: defaultText, essential: essential);

        public static readonly LetteraParamInfo[] InnerShadow = Join(new[]
        {
            Paint(null, "#00000080"),
            EmLength(1, "X", "Offset to the right, in em, or px.", "m_Offset.x", essential: true),
            EmLength(2, "Y", "Offset upward, in em, or px. Below the text is negative.", "m_Offset.y", essential: true),
            Unit(3, "Softness", "0 sharp to 1 fully blurred.", "m_Softness", true),
            EmLength(4, "Choke", "Pulls the shadow's edge inward, in em, or px.", "m_Choke"),
        }, PaintSettings(5));

        public static readonly LetteraParamInfo[] Extrude =
        {
            EmLength(0, "X", "How far the far end of the stack sits to the right, in em, or px.", "m_Offset.x", essential: true),
            EmLength(1, "Y", "How far the far end sits upward, in em, or px. Below the text is negative.", "m_Offset.y", essential: true),
            LetteraParamInfo.ForSlot(2, "Near color", LetteraParamKind.Color, "Colour of the copy nearest the text.", defaultField: "m_NearColor", essential: true),
            LetteraParamInfo.ForSlot(3, "Far color", LetteraParamKind.Color, "Colour of the copy farthest back.", defaultField: "m_FarColor", essential: true),
            EmLength(4, "Dilate", "Grows every copy, in em, or px.", "m_Dilate"),
            Unit(5, "Softness", "0 sharp to 1 fully blurred.", "m_Softness"),
        };

        public static readonly LetteraParamInfo[] Filter =
        {
            LetteraParamInfo.ForSlot(0, "Intensity", LetteraParamKind.Float, "How much of the filter shows: 0 none, 1 full. 50% works too.", 0, 1, 0.05f, 2,
                units: LetteraSlotUnits.Bare | LetteraSlotUnits.Percent, defaultField: "m_Intensity", essential: true),
            Choice(1, "Filter", "Which filter to apply, when the style does not fix one.", new[] { "grayscale", "invert", "sepia" }, new[] { "Grayscale", "Invert", "Sepia" },
                defaultText: "the style's filter", essential: true),
        };

        public static readonly LetteraParamInfo[] Warp =
        {
            LetteraParamInfo.ForSlot(0, "Amplitude", LetteraParamKind.Float, "How far the outline moves, in glyph heights. 0 does nothing.", min: 0, step: 0.005f, decimals: 3, defaultField: "m_Amplitude", essential: true, example: "0.03"),
            LetteraParamInfo.ForSlot(1, "Scale", LetteraParamKind.Float, "Size of one ripple, in glyph heights. Smaller is finer.", min: 0.001f, step: 0.05f, decimals: 3, defaultField: "m_Scale", example: "0.2"),
            LetteraParamInfo.ForSlot(2, "Travel", LetteraParamKind.Float, "How far the ripples flow for each step of phase. The phase itself changes from a script.", step: 0.1f, decimals: 2, defaultField: "m_Travel", example: "2"),
        };

        public static readonly LetteraParamInfo[] SmallCaps =
        {
            LetteraParamInfo.ForSlot(0, "Scale", LetteraParamKind.Float, "Size of made-up small capitals against full capitals. A font with real small capitals ignores it.", 0.1f, 1, 0.05f, 2, defaultField: "m_SyntheticScale", essential: true),
        };

        public static readonly LetteraParamInfo[] OpenType =
        {
            Feature(0, true), Feature(1, false), Feature(2, false), Feature(3, false),
        };

        static LetteraParamInfo Feature(int slot, bool essential) =>
            LetteraParamInfo.ForSlot(slot, "Feature " + (slot + 1), LetteraParamKind.String, "An OpenType feature as HarfBuzz writes it: onum turns it on, -dlig off, +salt on, cv02 3 sets a value.",
                defaultText: "the font's own", essential: essential, example: slot == 0 ? "onum" : slot == 1 ? "-liga" : slot == 2 ? "ss01" : "cv02 3");

        public static readonly LetteraParamInfo[] Axes =
        {
            Axis(0, "Weight", "wght", "Weight of a variable font: 640 sets it, 120% scales the default, +80 adds to it, ~ leaves it.", "640"),
            Axis(1, "Width", "wdth", "Width axis, written like the weight: 120%, 75, +10 or ~.", "120%"),
            Axis(2, "Italic", "ital", "Italic axis, 0 upright to 1 italic.", "1"),
            Axis(3, "Slant", "slnt", "Slant axis in degrees. A bare negative number is the value, not a change.", "-10"),
            Axis(4, "Optical size", "opsz", "Optical size axis, in points.", "14"),
        };

        static LetteraParamInfo Axis(int slot, string name, string tag, string hint, string example) =>
            LetteraParamInfo.ForSlot(slot, name, LetteraParamKind.String, hint + " Any slot also takes a named axis: GRAD 50.", defaultText: "the face's default (" + tag + ")", essential: slot == 0, example: example);

        public static readonly LetteraParamInfo[] SizeMatch =
        {
            LetteraParamInfo.ForEnum<LetteraSizeMatchMetric>(0, "Metric", "Which height fallback faces are matched on: x-height or capital height.", "m_Metric"),
        };

        public static readonly LetteraParamInfo[] Ruby =
        {
            LetteraParamInfo.ForSlot(0, "Reading", LetteraParamKind.String, "The small text drawn over the base text.", defaultText: "none, so nothing is drawn", essential: true, example: "かんじ"),
        };

        public static readonly LetteraParamInfo[] WordSpacing =
        {
            Spacing(0, "Amount", "Extra space at each space: local units, em, or a percent of the size. Negative closes it up.", "m_Amount", "4"),
        };

        public static readonly LetteraParamInfo[] Cell =
        {
            LetteraParamInfo.ForSlot(0, "Width", LetteraParamKind.Length, "Width of every cell: local units, em, or auto for the widest glyph in the range.", units: SpaceUnits, bareMeans: LetteraLength.Unit.Pixels,
                step: 0.5f, decimals: 2, defaultText: "auto", essential: true, accepts: token => token.Equals("auto", StringComparison.OrdinalIgnoreCase), example: "0.6em"),
        };

        public static readonly LetteraParamInfo[] ParagraphSpacing =
        {
            Spacing(0, "After", "Space after each paragraph the range starts: local units, em, or a percent of the size.", "m_After", "12"),
            Spacing(1, "Before", "Space before each paragraph the range starts, in the same units.", "m_Before", "6"),
        };

        public static readonly LetteraParamInfo[] LineGap =
        {
            Spacing(0, "Amount", "Space opened after the range, which pushes the lines that follow: local units, em, or a percent of the size.", "m_Amount", "10"),
        };

        public static readonly LetteraParamInfo[] ListItem =
        {
            LetteraParamInfo.ForSlot(0, "Level", LetteraParamKind.Int, "How deeply the item is nested. 0 is the top level.", 0, float.PositiveInfinity, 1, 0, defaultText: "0", essential: true),
            LetteraParamInfo.ForSlot(1, "Number", LetteraParamKind.Int, "The item's number. Set one and the list counts on from it. Empty counts on from the last.", step: 1, decimals: 0, defaultText: "counts on"),
            LetteraParamInfo.ForSlot(2, "Marker", LetteraParamKind.String, "Text used in place of the bullet or number. It does not count.", defaultText: "a bullet or a number", example: "★"),
            LetteraParamInfo.ForSlot(3, "Ordered", LetteraParamKind.String, "Write ordered to number the item. Empty follows the style.", defaultText: "the style's choice", example: "ordered"),
        };

        public static readonly LetteraParamInfo[] Trim =
        {
            LetteraParamInfo.ForEnum<LetteraTrimTop>(0, "Top edge", "Where the first line's top is cut: none, the ascent, the capital height, the x-height or the ink.", "m_TopEdge"),
            LetteraParamInfo.ForEnum<LetteraTrimBottom>(1, "Bottom edge", "Where the last line's bottom is cut: none, the descent, the baseline or the ink.", "m_BottomEdge"),
        };

        public static readonly LetteraParamInfo[] Curve =
        {
            LocalLength(0, "Radius", "Radius of the circle each line follows: local units, em, or a percent of the size. Positive bends the ends down, negative bends them up.", "m_Radius", "180"),
            LetteraParamInfo.ForSlot(1, "Anchor", LetteraParamKind.Float, "Which point of each line stays put: 0 the start, 0.5 the middle, 1 the end.", 0, 1, 0.05f, 2, defaultField: "m_Anchor", essential: true),
        };

        public static readonly LetteraParamInfo[] Divider =
        {
            LetteraParamInfo.ForSlot(0, "Separator", LetteraParamKind.String, "The text between pieces. A line breaks only at separators while the pieces fit. Empty uses the style's.", defaultText: "the style's separator", essential: true, example: " · "),
        };

        public static readonly LetteraParamInfo[] Ellipsis =
        {
            LetteraParamInfo.ForSlot(0, "Position", LetteraParamKind.Float, "Where the cut falls: 0 at the start, 0.5 in the middle, 1 at the end.", 0, 1, 0.05f, 2, defaultField: "m_Position", essential: true),
        };

        public static readonly LetteraParamInfo[] Truncate =
        {
            LetteraParamInfo.ForSlot(0, "Position", LetteraParamKind.Float, "Where the cut falls: 0 at the start, 0.5 in the middle, 1 at the end.", 0, 1, 0.05f, 2, defaultField: "m_Position", essential: true),
            Spacing(1, "Fade distance", "How far the text fades out before the cut: local units, em, or a percent of the size. 0 is a hard cut.", "m_FadeDistance", "1em"),
            Spacing(2, "Fade shift", "Moves the point where the fade is complete. Positive moves it into the text.", "m_FadeShift", "0.5em"),
        };

        public static readonly LetteraParamInfo[] Direction =
        {
            Choice(0, "Direction", "Base direction of every paragraph, even when the tag covers a range.", new[] { "auto", "ltr", "rtl" }, new[] { "Automatic", "Left to right", "Right to left" }, "m_Direction", essential: true),
        };

        public static readonly LetteraParamInfo[] Link =
        {
            LetteraParamInfo.ForSlot(0, "URL", LetteraParamKind.String, "The address that opens when the range is activated.", defaultText: "none", essential: true, example: "https://example.com"),
        };

        public static readonly LetteraParamInfo[] RangeAction =
        {
            LetteraParamInfo.ForSlot(0, "Value", LetteraParamKind.String, "A value your code receives with the range's events: a name or an id.", defaultText: "none", essential: true, example: "player-42"),
        };

        public static readonly LetteraParamInfo[] Spoiler =
        {
            LetteraParamInfo.ForSlot(0, "Value", LetteraParamKind.String, "A value your code receives when the cover is toggled. Optional.", defaultText: "none", example: "ending"),
        };

        public static readonly LetteraParamInfo[] Inline =
        {
            LetteraParamInfo.ForSlot(0, "Name", LetteraParamKind.String, "The item to place: a name in the style's items, its library or the project's Inline Library.", defaultText: "none, so nothing shows", essential: true, example: "coin", suggests: "inline"),
            InlineEm(1, "Size", "Height of the item, in em.", "the item's own", true, "1.2"),
            InlineEm(2, "Advance", "How far the pen moves after the item, in em. Below 0 takes the item's width.", "the item's width", false, "1.5"),
            InlineEm(3, "Lift", "How far the item's bottom sits above the baseline, in em.", "the item's own", false, "-0.2"),
            InlineEm(4, "Above", "Room kept above the baseline, in em. Below 0 takes it from the item.", "from the item", false, "1"),
            InlineEm(5, "Below", "Room kept below the baseline, in em. Below 0 takes it from the item.", "from the item", false, "0.2"),
            LetteraParamInfo.ForSlot(6, "Pivot", LetteraParamKind.String, "The point the item turns about, as two numbers from 0 to 1: x then y.", defaultText: "the item's own", example: "0.5 0.5"),
            LetteraParamInfo.ForSlot(7, "Turn", LetteraParamKind.Float, "Turns the item, in degrees. A sprite's turn is not drawn yet.", step: 5, decimals: 1, defaultText: "the item's own", example: "15", unitLabel: Degrees),
            LetteraParamInfo.ForSlot(8, "Tint", LetteraParamKind.Color, "A colour for the item, or i to take the text's colour. Empty keeps the item's own.", defaultText: "the item's own",
                accepts: token => token == "i", example: "#FF8800"),
            LetteraParamInfo.ForSlot(9, "Keep aspect", LetteraParamKind.Bool, "Keep the sprite's shape when the size changes. Write true or false.", defaultText: "the item's own"),
        };

        static LetteraParamInfo InlineEm(int slot, string name, string hint, string defaultText, bool essential, string example) =>
            LetteraParamInfo.ForSlot(slot, name, LetteraParamKind.Float, hint, step: 0.05f, decimals: 2, defaultText: defaultText, essential: essential, example: example, unitLabel: "em");

        public static readonly LetteraParamInfo[] Formula =
        {
            Choice(0, "Mode", "inline sits in the line; display puts the formula on a line of its own, centred.", new[] { "inline", "display" }, new[] { "Inline", "Display" }, defaultText: "inline", essential: true),
            LetteraParamInfo.ForSlot(1, "Alt text", LetteraParamKind.String, "Words for screen readers and copying in place of the formula.", defaultText: "none", example: "Euler identity"),
        };

        public static readonly LetteraParamInfo[] Resolution =
        {
            LetteraParamInfo.ForSlot(0, "Boost", LetteraParamKind.Int, "How many steps to raise the glyph density: 0 none, 2 the most.", 0, 2, 1, 0, defaultField: "m_Boost", essential: true),
        };

        public static readonly LetteraParamInfo[] PaintOrder =
        {
            LetteraParamInfo.ForEnum<LetteraPaintOrder>(0, "Order", "Layer-major finishes one layer on every glyph first. Glyph-major finishes every layer of a glyph first.", "m_Order"),
            LetteraParamInfo.ForSlot(1, "Reverse", LetteraParamKind.Bool, "With glyph-major order, let the first glyph end on top.", defaultField: "m_Reverse"),
        };

        public static readonly LetteraParamInfo[] Material =
        {
            LetteraParamInfo.ForSlot(0, "Tint", LetteraParamKind.Color, "A colour multiplied into the material's result.", defaultField: "m_Tint", essential: true),
        };
    }
}

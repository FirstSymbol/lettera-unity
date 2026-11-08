using System;

namespace FirstSymbol.Lettera
{
    /// <summary>
    /// The slots of the built-in tags, in the order each effect reads them. The slot test runs every effect with these and fails if one reads
    /// a position that is not listed or leaves a listed one alone, so a change to an effect shows up there.
    /// </summary>
    internal static partial class LetteraBuiltInSlots
    {
        const LetteraSlotUnits LayerUnits = LetteraSlotUnits.Bare | LetteraSlotUnits.Px | LetteraSlotUnits.Em | LetteraSlotUnits.Percent;
        const LetteraSlotUnits LayoutUnits = LetteraSlotUnits.Bare | LetteraSlotUnits.Px | LetteraSlotUnits.Em | LetteraSlotUnits.Percent;
        const string PaintHint = "A colour (#RGB, #RRGGBB, #RRGGBBAA or a name) or the name of a swatch. Empty uses the effect's own paint.";

        static LetteraParamInfo Paint(string defaultField, string defaultText, string name = "Paint") =>
            LetteraParamInfo.ForSlot(0, name, LetteraParamKind.Paint, PaintHint, defaultField: defaultField, defaultText: defaultText, essential: true);

        // A length in em, or local units with px. Bare means em.
        static LetteraParamInfo EmLength(int slot, string name, string hint, string defaultField, string defaultText = null, bool essential = false) =>
            LetteraParamInfo.ForSlot(slot, name, LetteraParamKind.Length, hint, units: LayerUnits, bareMeans: LetteraLength.Unit.Em, step: 0.01f, decimals: 3,
                defaultField: defaultField, defaultText: defaultText, essential: essential);

        // A length in local units. Bare and px both mean local units.
        static LetteraParamInfo LocalLength(int slot, string name, string hint, string defaultField, string example = null) =>
            LetteraParamInfo.ForSlot(slot, name, LetteraParamKind.Length, hint, units: LayoutUnits, bareMeans: LetteraLength.Unit.Pixels, step: 0.5f, decimals: 2, defaultField: defaultField, essential: true, example: example);

                static LetteraParamInfo[] Join(LetteraParamInfo[] own, LetteraParamInfo[] more)
        {
            var all = new LetteraParamInfo[own.Length + more.Length];
            own.CopyTo(all, 0);
            more.CopyTo(all, own.Length);
            return all;
        }

        /// <summary>The nine settings every painted layer reads after its own slots. An editor folds them away.</summary>
        public static LetteraParamInfo[] PaintSettings(int first)
        {
            const string group = "Paint";
            return new[]
            {
                LetteraParamInfo.ForEnum<LetteraPaintSpan>(first, "Paint span", "How far one run of the paint stretches: each glyph, each line, the range or the block.", defaultText: "the swatch's, else Block", group: group, example: "Glyph"),
                LetteraParamInfo.ForEnum<LetteraPaintShape>(first + 1, "Paint shape", "Linear, radial or conic gradient. Only a gradient paint has a shape.", defaultText: "the swatch's, else Linear", group: group, example: "Radial"),
                LetteraParamInfo.ForEnum<LetteraPaintFit>(first + 2, "Paint fit", "How a texture paint fills the span.", defaultText: "the swatch's, else Stretch", group: group, example: "Cover"),
                LetteraParamInfo.ForEnum<LetteraPaintEdge>(first + 3, "Paint edge", "What a gradient does past its ends: clamp, repeat or mirror.", defaultText: "the swatch's, else Clamp", group: group, example: "Mirror"),
                LetteraParamInfo.ForEnum<LetteraBlend>(first + 4, "Blend", "How the layer mixes with what is under it.", defaultText: "the swatch's, else Normal", group: group, example: "Multiply"),
                LetteraParamInfo.ForSlot(first + 5, "Paint angle", LetteraParamKind.Float, "Turns a gradient or texture, in degrees.", step: 5, decimals: 1, defaultText: "the swatch's, else 0", group: group, example: "45", unitLabel: "°"),
                LetteraParamInfo.ForSlot(first + 6, "Paint scale", LetteraParamKind.Float, "Size of a gradient or texture against the span. Zero or less counts as 1.", step: 0.1f, decimals: 2, defaultText: "the swatch's, else 1", group: group, example: "2"),
                LetteraParamInfo.ForSlot(first + 7, "Paint offset X", LetteraParamKind.Float, "Slides the paint across the span, as a share of it.", step: 0.05f, decimals: 2, defaultText: "the swatch's, else 0", group: group, example: "0.25"),
                LetteraParamInfo.ForSlot(first + 8, "Paint offset Y", LetteraParamKind.Float, "Slides the paint up the span, as a share of it.", step: 0.05f, decimals: 2, defaultText: "the swatch's, else 0", group: group, example: "-0.25"),
            };
        }

        static LetteraParamInfo Unit(int slot, string name, string hint, string defaultField, bool essential = false) =>
            LetteraParamInfo.ForSlot(slot, name, LetteraParamKind.Float, hint, 0, 1, 0.05f, 2, defaultField: defaultField, essential: essential);

        public static readonly LetteraParamInfo[] Bold =
        {
            LetteraParamInfo.ForSlot(0, "Weight", LetteraParamKind.Int, "100 thin to 900 black. 700 is bold.", 100, 900, 100, 0, defaultField: "m_Weight", essential: true),
            LetteraParamInfo.ForSlot(1, "Mode", LetteraParamKind.Choice, "f makes the weight synthetic even where a face exists. r uses real faces only.",
                choices: new[] { "f", "r" }, choiceLabels: new[] { "Force synthetic", "Real faces only" }, defaultText: "automatic"),
        };

        public static readonly LetteraParamInfo[] Italic =
        {
            LetteraParamInfo.ForSlot(0, "Slant", LetteraParamKind.Float, "Slant of a synthetic italic, in percent of the glyph height. Empty uses the face's own italic.", step: 1, decimals: 1, defaultText: "face italic", essential: true, example: "12"),
        };

        public static readonly LetteraParamInfo[] Size =
        {
            LetteraParamInfo.ForSlot(0, "Size", LetteraParamKind.Length, "24 sets a size, 150% or 1.2em scale the enclosing size, +10 or -5 add to it.",
                units: LetteraSlotUnits.Bare | LetteraSlotUnits.Em | LetteraSlotUnits.Percent | LetteraSlotUnits.Delta, bareMeans: LetteraLength.Unit.Bare, bareLabel: "units", step: 1, decimals: 2, defaultField: "m_Size", essential: true),
        };

        public static readonly LetteraParamInfo[] Color =
        {
            LetteraParamInfo.ForSlot(0, "Color", LetteraParamKind.Color, "#RGB, #RRGGBB, #RRGGBBAA or a name.", defaultField: "m_Color", essential: true),
        };

        public static readonly LetteraParamInfo[] Alpha =
        {
            LetteraParamInfo.ForSlot(0, "Alpha", LetteraParamKind.Float, "0 clear to 1 solid. 50% works too.", 0, 1, 0.05f, 2, units: LetteraSlotUnits.Bare | LetteraSlotUnits.Percent,
                defaultField: "m_Alpha", essential: true, accepts: token => token.Length == 3 && token[0] == '#' && LetteraSlots.TryColor("#000000" + token.Substring(1), out _)),
        };

        public static readonly LetteraParamInfo[] Font =
        {
            LetteraParamInfo.ForSlot(0, "Family", LetteraParamKind.String, "A family name of the font stack. Empty uses the configured family.", defaultField: "m_Family", essential: true, example: "Roboto"),
        };

        public static readonly LetteraParamInfo[] Language =
        {
            LetteraParamInfo.ForSlot(0, "Language", LetteraParamKind.String, "A BCP 47 tag such as en, ru or zh-Hans.", defaultField: "m_Language", essential: true, example: "ru"),
        };

        // After the line's own six slots come the paint settings.
        public static readonly LetteraParamInfo[] Line = Join(new[]
        {
            Paint(null, "text colour"),
            LetteraParamInfo.ForEnum<LetteraLineStyle>(1, "Style", "Solid, double, dotted, dashed or wavy.", "m_LineStyle"),
            EmLength(2, "Thickness", "Thickness in em, or px. Empty follows the font.", null, "auto"),
            EmLength(3, "Offset", "Distance from the baseline in em, or px. Empty follows the font.", null, "auto"),
            LetteraParamInfo.ForSlot(4, "Skip ink", LetteraParamKind.Bool, "Break the line where letters have descenders.", defaultField: "m_SkipInk"),
            LetteraParamInfo.ForSlot(5, "Overlay", LetteraParamKind.Bool, "Draw the line over the glyphs, not behind them.", defaultField: "m_Overlay"),
        }, PaintSettings(6));

        public static readonly LetteraParamInfo[] Outline = Join(new[]
        {
            Paint("m_Color", null, "Color"),
            EmLength(1, "Width", "Width in em, or px.", "m_Width", essential: true),
        }, PaintSettings(2));

        public static readonly LetteraParamInfo[] Shadow = Join(new[]
        {
            Paint("m_Color", null, "Color"),
            EmLength(1, "X", "Offset to the right, in em, or px.", "m_Offset.x", essential: true),
            EmLength(2, "Y", "Offset upward, in em, or px. Below the text is negative.", "m_Offset.y", essential: true),
            Unit(3, "Softness", "0 sharp to 1 fully blurred.", "m_Softness", true),
            EmLength(4, "Dilate", "Grows the shadow's shape, in em, or px.", "m_Dilate"),
        }, PaintSettings(5));

        public static readonly LetteraParamInfo[] Glow = Join(new[]
        {
            Paint(null, "text colour"),
            EmLength(1, "Radius", "How far the halo reaches, in em, or px.", "m_Radius", essential: true),
            Unit(2, "Softness", "0 sharp to 1 fully blurred.", "m_Softness", true),
            EmLength(3, "Dilate", "Grows the halo's shape, in em, or px.", "m_Dilate"),
        }, PaintSettings(4));

        static readonly string[] s_AlignNames = { "inside", "center", "outside" };

        public static readonly LetteraParamInfo[] Stroke = Join(new[]
        {
            Paint(null, "#000000"),
            EmLength(1, "Width", "Width in em, or px.", "m_Width", essential: true),
            LetteraParamInfo.ForSlot(2, "Align", LetteraParamKind.Choice, "Inside, center or outside the outline, or a number from -1 to 1.", -1, 1, 0.1f, 2,
                choices: s_AlignNames, choiceLabels: new[] { "Inside", "Center", "Outside" }, defaultField: "m_Align", accepts: token => LetteraSlots.TryNumber(token, out _)),
            Unit(3, "Softness", "0 sharp to 1 fully blurred.", "m_Softness"),
            LetteraParamInfo.ForEnum<LetteraStrokeCorners>(4, "Corners", "Round or sharp. Sharp needs the MSDF render mode.", "m_Corners"),
        }, PaintSettings(5));

        public static readonly LetteraParamInfo[] Fill = Join(new[]
        {
            Paint(null, "text colour"),
            EmLength(1, "Dilate", "Grows the face, in em, or px.", "m_Dilate"),
            Unit(2, "Softness", "0 sharp to 1 fully blurred.", "m_Softness"),
        }, PaintSettings(3));

        public static readonly LetteraParamInfo[] Mark = Join(new[]
        {
            Paint(null, "#FFFF0080"),
            LetteraParamInfo.ForEnum<LetteraHighlightGeometry>(1, "Shape", "What the highlight follows: each glyph, each line, the whole range or one block.", "m_Look.Geometry"),
            LetteraParamInfo.ForEnum<LetteraHighlightPaintSpan>(2, "Paint span", "How far one run of the paint stretches.", "m_PaintSpan"),
            EmLength(3, "Radius", "Corner radius in em, or px.", "m_Look.CornerRadius"),
            LetteraParamInfo.ForSlot(4, "Padding", LetteraParamKind.String, "One value, or four for left, bottom, right and top, in em. Padding does not change layout.", defaultText: "none", example: "0.1 0.05 0.1 0.05"),
            LetteraParamInfo.ForEnum<LetteraHighlightOrder>(5, "Order", "Behind the glyphs or above them.", "m_Order"),
            LetteraParamInfo.ForEnum<LetteraHighlightHeight>(6, "Height", "Which height the shape takes.", "m_Look.Height"),
        }, PaintSettings(7));

        public static readonly LetteraParamInfo[] Tracking =
        {
            LetteraParamInfo.ForSlot(0, "Amount", LetteraParamKind.Length, "Space added after each letter, in local units, or em.", units: LetteraSlotUnits.Bare | LetteraSlotUnits.Px | LetteraSlotUnits.Em | LetteraSlotUnits.Percent,
                bareMeans: LetteraLength.Unit.Pixels, step: 0.1f, decimals: 3, defaultField: "m_Amount", essential: true),
        };

        public static readonly LetteraParamInfo[] LineHeight =
        {
            LetteraParamInfo.ForSlot(0, "Height", LetteraParamKind.Length, "A multiplier such as 1.4, or px, em or % for an absolute height.", units: LayoutUnits,
                bareMeans: LetteraLength.Unit.Bare, bareLabel: "x", step: 0.05f, decimals: 3, defaultField: "m_Value", essential: true),
            LetteraParamInfo.ForEnum<LetteraLineHeightMode>(1, "Mode", "What the multiplier applies to.", defaultText: "Default"),
            LetteraParamInfo.ForEnum<LetteraLeading>(2, "Leading", "Where the extra space goes.", defaultText: "Half"),
        };

        public static readonly LetteraParamInfo[] Align =
        {
            LetteraParamInfo.ForEnum<LetteraHorizontalAlignment>(0, "Alignment", "Where the lines sit in the box.", "m_Alignment"),
            LetteraParamInfo.ForEnum<LetteraJustifyMode>(1, "Justify", "How a justified line spends its space.", "m_Justify"),
            LetteraParamInfo.ForEnum<LetteraLastLine>(2, "Last line", "How the last line of a paragraph sits.", "m_LastLine"),
        };

        public static readonly LetteraParamInfo[] Indent =
        {
            LetteraParamInfo.ForSlot(0, "Amount", LetteraParamKind.Length, "Margin from the paragraph's start edge: local units, em, or % of the width.", units: LayoutUnits,
                bareMeans: LetteraLength.Unit.Pixels, step: 1, decimals: 2, defaultField: "m_Amount", essential: true),
        };

        public static readonly LetteraParamInfo[] SuperSub =
        {
            LetteraParamInfo.ForEnum<LetteraSuperSubPlacement>(0, "Placement", "Above the line or below it.", "m_Placement"),
        };
    }
}

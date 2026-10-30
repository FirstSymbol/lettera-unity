using System;
using System.Collections.Generic;
using UnityEngine;

namespace FirstSymbol.Lettera
{
    public enum LetteraJustifyMode : byte { Auto, InterWord, InterCharacter, None }
    public enum LetteraLastLine : byte { Auto, Start, End, Center, Justify }
    public enum LetteraTrimTop : byte { None, Ascent, CapHeight, XHeight, Ink }
    public enum LetteraTrimBottom : byte { None, Descent, Baseline, Ink }

    /// <summary>A layout length: bare numbers are local units, and percent is of the font size.</summary>
    internal struct LayoutLength
    {
        public float Em, Units;

        public LayoutLength(in LetteraLength length)
        {
            Em = Units = 0;
            switch (length.Kind)
            {
                case LetteraLength.Unit.Em: Em = length.Value; break;
                case LetteraLength.Unit.Percent: Em = length.Value * 0.01f; break;
                default: Units = length.Value; break;
            }
        }

        public float Of(float size) => Em * size + Units;
        public bool IsZero => Em == 0 && Units == 0;
    }

    internal struct LineHeightSpec
    {
        public float Multiplier;
        public LayoutLength Absolute;
        public bool IsAbsolute;
        public LetteraLineHeightMode Mode;
        public int Leading; // -1 the component's.
    }

    internal struct ParagraphSpacingSpec
    {
        public LayoutLength After, Before;
    }

    internal struct LineGapSpec
    {
        public int End;
        public LayoutLength Amount;
    }

    internal struct AlignSpec
    {
        public int Alignment;   // -1 the component's.
        public LetteraJustifyMode Justify;
        public LetteraLastLine LastLine;
        public float MaxWordSpace, MinWordSpace, MinTracking, MaxTracking, MinGlyphWidth, MaxGlyphWidth;

        public static readonly AlignSpec Default = new AlignSpec { Alignment = -1, MaxWordSpace = float.PositiveInfinity, MinWordSpace = 80, MinGlyphWidth = 100, MaxGlyphWidth = 100 };
    }

    /// <summary>Adds space after each cluster, keeping cursive joins with connecting strokes. Slot: 1 amount, in local units or em.</summary>
    [Serializable]
    public class TrackingEffect : LetteraEffect
    {
        [SerializeField] float m_Amount;
        [SerializeField] bool m_AmountInEm;

        public float Amount { get => m_Amount; set => m_Amount = value; }
        public bool AmountInEm { get => m_AmountInEm; set => m_AmountInEm = value; }

        static readonly List<uint> s_NoLigatureTags = new List<uint> { LetteraNative.Tag("liga"), LetteraNative.Tag("clig") };
        static readonly List<uint> s_NoLigatureValues = new List<uint> { 0, 0 };

        public override void Apply(LetteraAttributes attributes, int start, int end, in LetteraSlots slots)
        {
            var fallback = new LetteraLength(m_Amount, m_AmountInEm ? LetteraLength.Unit.Em : LetteraLength.Unit.Bare);
            var amount = new LayoutLength(slots.Length(0, fallback));
            attributes.SetTracking(start, end, amount);
            if (!amount.IsZero) attributes.AddFeatures(start, end, s_NoLigatureTags, s_NoLigatureValues);
        }
    }

    /// <summary>Changes the gap at spaces and no-break spaces, and nothing between letters. Slot: 1 amount, in local units or em.</summary>
    [Serializable]
    public class WordSpacingEffect : LetteraEffect
    {
        [SerializeField] float m_Amount;

        public float Amount { get => m_Amount; set => m_Amount = value; }

        public override void Apply(LetteraAttributes attributes, int start, int end, in LetteraSlots slots) =>
            attributes.SetWordSpacing(start, end, new LayoutLength(slots.Length(0, new LetteraLength(m_Amount))));
    }

    /// <summary>Gives each cluster a cell of one width with the glyph centered. Slot: 1 width in local units or em, or auto for the widest glyph of the range.</summary>
    [Serializable]
    public class CellWidthEffect : LetteraEffect
    {
        [SerializeField] string m_Width = "auto";

        public string Width { get => m_Width; set => m_Width = value; }

        public override void Apply(LetteraAttributes attributes, int start, int end, in LetteraSlots slots)
        {
            string token = slots[0] ?? m_Width;
            if (string.IsNullOrEmpty(token) || token.Equals("auto", StringComparison.OrdinalIgnoreCase)) attributes.SetCell(start, end, default, true);
            else if (LetteraLength.TryParse(token, out LetteraLength width) && width.Value > 0) attributes.SetCell(start, end, new LayoutLength(width), false);
        }
    }

    /// <summary>Line height of the lines a range touches; a line takes the largest asked of it. Slots: 1 a multiplier, or px, em, %; 2 mode; 3 leading.</summary>
    [Serializable]
    public class LineHeightEffect : LetteraEffect
    {
        [SerializeField] float m_Value = 1.4f;

        public float Value { get => m_Value; set => m_Value = value; }

        public override void Apply(LetteraAttributes attributes, int start, int end, in LetteraSlots slots)
        {
            var spec = new LineHeightSpec { Multiplier = m_Value, Leading = -1, Mode = slots.Enum(1, LetteraLineHeightMode.Default) };
            if (LetteraLength.TryParse(slots[0], out LetteraLength value))
            {
                if (value.Kind == LetteraLength.Unit.Bare) spec.Multiplier = value.Value;
                else if (value.Kind == LetteraLength.Unit.Percent) spec.Multiplier = value.Value * 0.01f;
                else
                {
                    spec.IsAbsolute = true;
                    spec.Absolute = new LayoutLength(value);
                }
            }
            if (!string.IsNullOrEmpty(slots[2])) spec.Leading = (int)slots.Enum(2, LetteraLeading.Half);
            attributes.SetLineHeight(start, end, spec);
        }
    }

    /// <summary>Space around paragraphs whose first codepoint the range covers. Slots: 1 after, 2 before, in local units, em or percent of the font size.</summary>
    [Serializable]
    public class ParagraphSpacingEffect : LetteraEffect
    {
        [SerializeField] float m_After;
        [SerializeField] float m_Before;

        public float After { get => m_After; set => m_After = value; }
        public float Before { get => m_Before; set => m_Before = value; }

        public override void Apply(LetteraAttributes attributes, int start, int end, in LetteraSlots slots) =>
            attributes.SetParagraphSpacing(start, end, new ParagraphSpacingSpec
            {
                After = new LayoutLength(slots.Length(0, new LetteraLength(m_After))),
                Before = new LayoutLength(slots.Length(1, new LetteraLength(m_Before))),
            });
    }

    /// <summary>Opens space after a range, moving the following line. Slot: 1 amount, in local units, em or percent of the font size.</summary>
    [Serializable]
    public class LineGapEffect : LetteraEffect
    {
        [SerializeField] float m_Amount;

        public float Amount { get => m_Amount; set => m_Amount = value; }

        public override void Apply(LetteraAttributes attributes, int start, int end, in LetteraSlots slots) =>
            attributes.AddLineGap(end, new LayoutLength(slots.Length(0, new LetteraLength(m_Amount))));
    }

    /// <summary>The range stays together on a line, and its edges can break even without a space. Explicit breaks inside still apply.</summary>
    [Serializable]
    public class NoBreakEffect : LetteraEffect
    {
        public override void Apply(LetteraAttributes attributes, int start, int end, in LetteraSlots slots) => attributes.AddNoBreak(start, end);
    }

    /// <summary>Moves lines from the paragraph's start edge. Nested ranges add up. Slot: 1 amount in local units or em, percent of the rect width.</summary>
    [Serializable]
    public class IndentEffect : LetteraEffect
    {
        [SerializeField] float m_Amount;

        public float Amount { get => m_Amount; set => m_Amount = value; }

        public override void Apply(LetteraAttributes attributes, int start, int end, in LetteraSlots slots)
        {
            LetteraLength amount = slots.Length(0, new LetteraLength(m_Amount));
            float percent = amount.Kind == LetteraLength.Unit.Percent ? amount.Value : 0;
            attributes.AddIndent(start, end, amount.Kind == LetteraLength.Unit.Percent ? default : new LayoutLength(amount), percent);
        }
    }

    /// <summary>
    /// Alignment and justification of the paragraphs the range covers. Slots: 1 alignment, 2 justify mode, 3 last line.
    /// The budgets say how a short line spends its space: word spaces first, then letter spacing, then glyph width.
    /// </summary>
    [Serializable]
    public class AlignEffect : LetteraEffect
    {
        [SerializeField] LetteraHorizontalAlignment m_Alignment;
        [SerializeField] LetteraJustifyMode m_Justify;
        [SerializeField] LetteraLastLine m_LastLine;
        [Tooltip("The widest a word space gets, in percent of a normal space. Infinity has no limit.")]
        [SerializeField] float m_MaxWordSpace = float.PositiveInfinity;
        [SerializeField, Range(0, 100)] float m_MinWordSpace = 80;
        [Tooltip("Letter spacing a line may use, in percent of the font size.")]
        [SerializeField] float m_MinTracking;
        [SerializeField] float m_MaxTracking;
        [Tooltip("Glyph width a line may use, in percent.")]
        [SerializeField] float m_MinGlyphWidth = 100;
        [SerializeField] float m_MaxGlyphWidth = 100;

        public LetteraHorizontalAlignment Alignment { get => m_Alignment; set => m_Alignment = value; }
        public LetteraJustifyMode Justify { get => m_Justify; set => m_Justify = value; }
        public LetteraLastLine LastLine { get => m_LastLine; set => m_LastLine = value; }
        public float MaxWordSpace { get => m_MaxWordSpace; set => m_MaxWordSpace = value; }
        public float MinWordSpace { get => m_MinWordSpace; set => m_MinWordSpace = value; }
        public float MinTracking { get => m_MinTracking; set => m_MinTracking = value; }
        public float MaxTracking { get => m_MaxTracking; set => m_MaxTracking = value; }
        public float MinGlyphWidth { get => m_MinGlyphWidth; set => m_MinGlyphWidth = value; }
        public float MaxGlyphWidth { get => m_MaxGlyphWidth; set => m_MaxGlyphWidth = value; }

        public override void Apply(LetteraAttributes attributes, int start, int end, in LetteraSlots slots)
        {
            attributes.SetAlign(start, end, new AlignSpec
            {
                Alignment = (int)slots.Enum(0, m_Alignment),
                Justify = slots.Enum(1, m_Justify),
                LastLine = slots.Enum(2, m_LastLine),
                MaxWordSpace = m_MaxWordSpace, MinWordSpace = m_MinWordSpace,
                MinTracking = m_MinTracking, MaxTracking = m_MaxTracking,
                MinGlyphWidth = m_MinGlyphWidth, MaxGlyphWidth = m_MaxGlyphWidth,
            });
        }
    }

    /// <summary>Removes space above the first line and below the last, as CSS text-box-trim does. Slots: 1 top edge, 2 bottom edge. Applies to the whole text.</summary>
    [Serializable]
    public class BoxTrimEffect : LetteraEffect
    {
        [SerializeField] LetteraTrimTop m_TopEdge;
        [SerializeField] LetteraTrimBottom m_BottomEdge;

        public LetteraTrimTop TopEdge { get => m_TopEdge; set => m_TopEdge = value; }
        public LetteraTrimBottom BottomEdge { get => m_BottomEdge; set => m_BottomEdge = value; }

        public override void Apply(LetteraAttributes attributes, int start, int end, in LetteraSlots slots) =>
            attributes.SetTrim(slots.Enum(0, m_TopEdge), slots.Enum(1, m_BottomEdge));
    }

    /// <summary>One way auto size may save room before it reduces the font size. Budgets are spent in list order, only as far as needed.</summary>
    [Serializable]
    public abstract class LetteraFitBudget
    {
        /// <summary>How much of the budget the last fit spent, from 0 to its limit.</summary>
        public float Spent { get; internal set; }
    }

    /// <summary>Tightens letter spacing. Limit: em per cluster.</summary>
    [Serializable]
    public class FitByTracking : LetteraFitBudget
    {
        [SerializeField, Min(0)] float m_Limit = 0.02f;

        public float Limit { get => m_Limit; set => m_Limit = value; }
    }

    /// <summary>Compresses glyphs and their advances together. Limit: percent. Above 3 percent it starts to show.</summary>
    [Serializable]
    public class FitByGlyphWidth : LetteraFitBudget
    {
        [SerializeField, Range(0, 50)] float m_Limit = 2f;

        public float Limit { get => m_Limit; set => m_Limit = value; }
    }

    /// <summary>Tightens lines. Limit: the smallest line height, in percent of the natural one.</summary>
    [Serializable]
    public class FitByLineHeight : LetteraFitBudget
    {
        [SerializeField, Range(1, 100)] float m_Limit = 90f;

        public float Limit { get => m_Limit; set => m_Limit = value; }
    }
}

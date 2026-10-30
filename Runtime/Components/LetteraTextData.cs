using System;
using System.Collections.Generic;
using UnityEngine;

namespace FirstSymbol.Lettera
{
    public enum LetteraRenderMode { SDF, MSDF }
    public enum LetteraHorizontalAlignment { Start, Center, End, Left, Right, Justify }
    public enum LetteraVerticalAlignment { Top, Middle, Bottom }
    public enum LetteraOverflow { Visible, Ellipsis, Truncate }
    public enum LetteraLineHeightMode { Default, Scaled, Content, Primary }
    public enum LetteraLeading { Half, Above, Below }
    public enum LetteraDirection { Auto, LeftToRight, RightToLeft }

    /// <summary>Which outputs a rebuild changed.</summary>
    [Flags]
    public enum LetteraChanges { None = 0, Text = 1, Layout = 2, Positions = 4, Mesh = 8 }

    /// <summary>Why the rendered text differs from the serialized Text field.</summary>
    public enum LetteraTextOverride { None, RuntimeText, Resolver, Both }

    /// <summary>Produces the text to parse from the raw text, e.g. for localization.</summary>
    public interface ILetteraTextResolver
    {
        string Resolve(Component owner, string raw);
    }

    /// <summary>The serialized state both text components share.</summary>
    [Serializable]
    internal sealed class LetteraTextData
    {
        [TextArea(3, 10)] public string Text = "";
        public LetteraFontAsset Font;
        public LetteraFontStack FontStack;
        [Min(0.01f)] public float FontSize = 36f;
        public LetteraRenderMode RenderMode;
        public LetteraHorizontalAlignment HorizontalAlignment;
        public LetteraVerticalAlignment VerticalAlignment;
        public bool WordWrap = true;
        public LetteraOverflow Overflow;
        [Range(0, 1)] public float OverflowPosition = 1f;
        public bool AutoSize;
        [Min(0.01f)] public float MinFontSize = 10f;
        [Min(0.01f)] public float MaxFontSize = 72f;
        public Vector4 Padding;
        public LetteraLineHeightMode LineHeightMode;
        public float LineHeightValue = 1.4f;
        public LetteraLeading LeadingDistribution;
        public LetteraDirection BaseDirection;
        public string Language = "";
        public List<LetteraStyle> Styles = new List<LetteraStyle>();
        public List<LetteraStylePreset> StylePresets = new List<LetteraStylePreset>();
        public bool UseGlobalStylePreset = true;
        [SerializeReference] public List<LetteraFitBudget> FitBudgets = new List<LetteraFitBudget>();

        public LetteraTextData Clone() => (LetteraTextData)MemberwiseClone();

        /// <summary>The value of every field at the last rebuild, to find what a serialized write changed.</summary>
        public struct Snapshot
        {
            public string Text;
            public UnityEngine.Object Font, FontStack;
            public float FontSize, OverflowPosition, MinFontSize, MaxFontSize, LineHeightValue;
            public int RenderMode, HorizontalAlignment, VerticalAlignment, Overflow, LineHeightMode, LeadingDistribution, BaseDirection;
            public bool WordWrap, AutoSize, UseGlobalStylePreset;
            public Vector4 Padding;
            public string Language;
        }

        public Snapshot Take() => new Snapshot
        {
            Text = Text, Font = Font, FontStack = FontStack, FontSize = FontSize, OverflowPosition = OverflowPosition,
            MinFontSize = MinFontSize, MaxFontSize = MaxFontSize, LineHeightValue = LineHeightValue, RenderMode = (int)RenderMode,
            HorizontalAlignment = (int)HorizontalAlignment, VerticalAlignment = (int)VerticalAlignment, Overflow = (int)Overflow,
            LineHeightMode = (int)LineHeightMode, LeadingDistribution = (int)LeadingDistribution, BaseDirection = (int)BaseDirection,
            WordWrap = WordWrap, AutoSize = AutoSize, UseGlobalStylePreset = UseGlobalStylePreset, Padding = Padding, Language = Language,
        };

        /// <summary>The rebuild a change from the snapshot needs.</summary>
        public DirtyLevel Compare(in Snapshot s)
        {
            if (s.Text != Text || s.UseGlobalStylePreset != UseGlobalStylePreset) return DirtyLevel.Parse;
            if (s.Font != Font || s.FontStack != FontStack || s.Language != Language || s.BaseDirection != (int)BaseDirection || s.RenderMode != (int)RenderMode) return DirtyLevel.Shape;
            if (s.FontSize != FontSize || s.WordWrap != WordWrap || s.Overflow != (int)Overflow || s.OverflowPosition != OverflowPosition || s.AutoSize != AutoSize ||
                s.MinFontSize != MinFontSize || s.MaxFontSize != MaxFontSize || s.Padding != Padding || s.LineHeightMode != (int)LineHeightMode ||
                s.LineHeightValue != LineHeightValue || s.LeadingDistribution != (int)LeadingDistribution) return DirtyLevel.Layout;
            if (s.HorizontalAlignment != (int)HorizontalAlignment || s.VerticalAlignment != (int)VerticalAlignment) return DirtyLevel.Positions;
            return DirtyLevel.None;
        }
    }

    /// <summary>Options of LetteraText.MeasureText. Null values use the component's current value.</summary>
    public struct LetteraMeasureOptions
    {
        public string Text;
        public float? MaxWidth, MaxHeight, FontSize, MinFontSize, MaxFontSize;
        public bool? AutoSize, WordWrap;
        public Vector4? Padding;
    }

    /// <summary>What a hit test found.</summary>
    public struct LetteraHit
    {
        public int GlyphIndex, ClusterStart, ClusterEnd, LineIndex;
        public Rect GlyphRect;
    }

    /// <summary>A laid-out line. Valid until the next rebuild.</summary>
    public struct LetteraLine
    {
        public int Start, End, VisibleEnd, Paragraph;
        public float X, Baseline, Width, Height, Ascent, Descent;
        public bool EndedByMandatoryBreak;

        /// <summary>Space opened above the line by paragraph spacing and line gaps.</summary>
        public float GapBefore;

        /// <summary>The distance from the box's start edge that indents ask for.</summary>
        public float Margin;

        /// <summary>How much of Ascent and Descent ruby readings take.</summary>
        public float RubyOver, RubyUnder;
    }

    /// <summary>A positioned glyph. Valid until the next rebuild.</summary>
    public struct LetteraGlyphInfo
    {
        public uint Glyph;
        public int Cluster, Line;
        public float X, Y, Advance, Size;
    }
}

using System;
using System.Collections.Generic;
using UnityEngine;

namespace FirstSymbol.Lettera
{
    public enum LetteraLineStyle : byte { Solid, Double, Dotted, Dashed, Wavy }
    public enum LetteraHighlightGeometry : byte { Glyph, Line, Range, Block }
    public enum LetteraHighlightPaintSpan : byte { FromGeometry, Glyph, Line, Range, Block }
    public enum LetteraHighlightOrder : byte { Behind, Above }
    public enum LetteraHighlightHeight : byte { Content, LineBox, LineAdvance }

    /// <summary>Which corners of a highlight round. Start and end follow the text's direction.</summary>
    [Flags]
    public enum LetteraCorners : byte { None = 0, TopStart = 1, TopEnd = 2, BottomStart = 4, BottomEnd = 8, All = 15 }

    /// <summary>How a highlight's shape is built, apart from its paint.</summary>
    [Serializable]
    public class LetteraHighlightLook
    {
        public LetteraHighlightGeometry Geometry = LetteraHighlightGeometry.Range;
        public LetteraHighlightHeight Height;
        public LetteraCorners Corners = LetteraCorners.All;
        [Tooltip("The corner radius, in em.")]
        public float CornerRadius;
        [Tooltip("Padding in em: left, bottom, right, top.")]
        public Vector4 Padding;
    }

    internal struct HighlightSpec
    {
        public LetteraHighlightLook Look;
        public LetteraHighlightPaintSpan PaintSpan;
        public LetteraHighlightOrder Order;
        public EmLength Radius, PadLeft, PadBottom, PadRight, PadTop;
    }

    /// <summary>
    /// A background shape behind or above a range. Slots: 1 paint, 2 geometry, 3 paint span, 4 corner radius (em),
    /// 5 padding ("l b r t" or one value, em), 6 order, 7 height, then the paint settings. Padding does not change layout.
    /// </summary>
    [Serializable]
    public class HighlightEffect : LetteraLayerEffect
    {
        [SerializeField] LetteraHighlightLook m_Look = new LetteraHighlightLook();
        [SerializeField] LetteraHighlightPaintSpan m_PaintSpan;
        [SerializeField] LetteraHighlightOrder m_Order;

        public LetteraHighlightLook Look { get => m_Look; set => m_Look = value; }
        public LetteraHighlightPaintSpan PaintSpan { get => m_PaintSpan; set => m_PaintSpan = value; }
        public LetteraHighlightOrder Order { get => m_Order; set => m_Order = value; }

        public override IReadOnlyList<LetteraParamInfo> Parameters => LetteraBuiltInSlots.Mark;

        public override LetteraEffect Clone()
        {
            var copy = (HighlightEffect)base.Clone();
            copy.m_Look = new LetteraHighlightLook { Geometry = m_Look.Geometry, Height = m_Look.Height, Corners = m_Look.Corners, CornerRadius = m_Look.CornerRadius, Padding = m_Look.Padding };
            return copy;
        }

        public override void Apply(LetteraAttributes attributes, int start, int end, in LetteraSlots slots)
        {
            var look = new LetteraHighlightLook
            {
                Geometry = slots.Enum(1, m_Look.Geometry),
                Height = slots.Enum(6, m_Look.Height),
                Corners = m_Look.Corners,
            };
            var spec = new HighlightSpec
            {
                Look = look,
                PaintSpan = slots.Enum(2, m_PaintSpan),
                Order = slots.Enum(5, m_Order),
                Radius = new EmLength(slots.Length(3, new LetteraLength(m_Look.CornerRadius))),
                PadLeft = new EmLength(new LetteraLength(m_Look.Padding.x)),
                PadBottom = new EmLength(new LetteraLength(m_Look.Padding.y)),
                PadRight = new EmLength(new LetteraLength(m_Look.Padding.z)),
                PadTop = new EmLength(new LetteraLength(m_Look.Padding.w)),
            };
            string padding = slots[4];
            if (!string.IsNullOrEmpty(padding))
            {
                string[] parts = padding.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 1 && LetteraLength.TryParse(parts[0], out LetteraLength all))
                    spec.PadLeft = spec.PadBottom = spec.PadRight = spec.PadTop = new EmLength(all);
                else if (parts.Length == 4)
                {
                    if (LetteraLength.TryParse(parts[0], out LetteraLength l)) spec.PadLeft = new EmLength(l);
                    if (LetteraLength.TryParse(parts[1], out LetteraLength b)) spec.PadBottom = new EmLength(b);
                    if (LetteraLength.TryParse(parts[2], out LetteraLength r)) spec.PadRight = new EmLength(r);
                    if (LetteraLength.TryParse(parts[3], out LetteraLength t)) spec.PadTop = new EmLength(t);
                }
            }
            var record = new LayerRecord { Paint = ResolvePaint(slots, 0, 7, new Color32(255, 255, 0, 128), false) };
            attributes.AddHighlight(start, end, record, spec);
        }
    }
}

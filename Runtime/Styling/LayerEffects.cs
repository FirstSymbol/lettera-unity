using System;
using System.Collections.Generic;
using UnityEngine;

namespace FirstSymbol.Lettera
{
    /// <summary>Where a layer draws. Back: shadow, glow. Rim: stroke. Face: fills. Front: inner shadow.</summary>
    public enum LetteraLayerBand : byte { Auto, Back, Rim, Face, Front, Overlay }

    /// <summary>Whether color glyphs (emoji) get a layer, drawn from their alpha. Inherit: shadow and glow do, others skip.</summary>
    public enum LetteraOnColorGlyphs : byte { Inherit, Skip, Apply }

    /// <summary>A length in em plus a length in local units; resolved against a glyph's size at the mesh pass.</summary>
    internal struct EmLength
    {
        public float Em, Units;

        public EmLength(in LetteraLength length)
        {
            Em = Units = 0;
            switch (length.Kind)
            {
                case LetteraLength.Unit.Pixels: Units = length.Value; break;
                case LetteraLength.Unit.Percent: Em = length.Value * 0.01f; break;
                default: Em = length.Value; break;
            }
        }

        public float Of(float size) => Em * size + Units;
    }

    internal enum LayerKind : byte { Fill, Stroke, Shadow, Glow, InnerShadow, Highlight, Extrude }

    /// <summary>One painted range of a layer effect, with its parameters resolved.</summary>
    internal struct LayerRecord
    {
        public LayerKind Kind;
        public ResolvedPaint Paint;
        public bool OnColorGlyphs, FixedSize;
        public EmLength OffsetX, OffsetY, Width, Dilate;
        public float Align, Softness;
        public int Start, End, Style;
        public int Group;
        public Rect RangeRect;
        public int Highlight;
        public bool SharpCorners;
        public Color32 FarColor;
        public int Steps;
        public Material Custom;
    }

    /// <summary>All ranges of one effect that draw one kind of layer. Cover holds, per codepoint, a record index plus one.</summary>
    internal sealed class LayerGroup
    {
        public object Key;
        public LetteraLayerBand Band;
        public LayerKind Layer;
        public int Style, Sequence;
        public int[] Cover;
    }

    /// <summary>The base of effects that add a painted layer to glyphs.</summary>
    [Serializable]
    public abstract class LetteraLayerEffect : LetteraEffect
    {
        [SerializeField] protected LetteraPaintChoice m_Paint = new LetteraPaintChoice();
        [SerializeField] protected LetteraPaintCatalog m_Catalog;
        [SerializeField] protected LetteraPaintLibrary m_Library;
        [SerializeField] protected List<LetteraSwatch> m_OwnSwatches = new List<LetteraSwatch>();
        [SerializeField] protected LetteraOnColorGlyphs m_OnColorGlyphs;
        [SerializeField] protected bool m_FixedPixelSize;
        [SerializeField] protected LetteraLayerBand m_Band;

        public LetteraPaintChoice Paint { get => m_Paint; set => m_Paint = value; }
        public LetteraPaintCatalog Catalog { get => m_Catalog; set => m_Catalog = value; }
        public LetteraPaintLibrary Library { get => m_Library; set => m_Library = value; }
        public List<LetteraSwatch> OwnSwatches => m_OwnSwatches;
        public LetteraOnColorGlyphs OnColorGlyphs { get => m_OnColorGlyphs; set => m_OnColorGlyphs = value; }
        public bool FixedPixelSize { get => m_FixedPixelSize; set => m_FixedPixelSize = value; }
        public LetteraLayerBand Band { get => m_Band; set => m_Band = value; }

        /// <summary>Sets a paint on this effect, in place of its default.</summary>
        public void SetPaint(LetteraPaint paint)
        {
            m_Paint.Kind = LetteraPaintChoiceKind.Inline;
            m_Paint.Inline = paint;
        }

        public override LetteraEffect Clone()
        {
            var copy = (LetteraLayerEffect)base.Clone();
            copy.m_Paint = m_Paint?.Copy() ?? new LetteraPaintChoice();
            copy.m_OwnSwatches = new List<LetteraSwatch>(m_OwnSwatches.Count);
            foreach (LetteraSwatch swatch in m_OwnSwatches) copy.m_OwnSwatches.Add(new LetteraSwatch(swatch.Name, swatch.Paint?.Copy()));
            return copy;
        }

        LetteraSwatch FindSwatch(string name)
        {
            switch (m_Catalog)
            {
                case LetteraPaintCatalog.Own: return LetteraPaintCatalogs.Find(m_OwnSwatches, name);
                case LetteraPaintCatalog.Library: return m_Library != null ? m_Library.Find(name) : null;
                default:
                    LetteraPaintLibrary project = LetteraSettings.Instance.Paints;
                    return project != null ? project.Find(name) : null;
            }
        }

        /// <summary>The paint of a range. The tag's token wins, then this effect's choice, then the default.</summary>
        private protected ResolvedPaint ResolvePaint(in LetteraSlots slots, int paintSlot, int projectionSlot, Color32 defaultColor, bool defaultIsGlyphColor)
        {
            LetteraPaint source = null;
            string token = slots[paintSlot];
            bool tokenHandled = false;
            if (LetteraSlots.TryColor(token, out Color32 color))
            {
                source = LetteraPaint.Solid(color);
                tokenHandled = true;
            }
            else if (!string.IsNullOrEmpty(token))
            {
                LetteraSwatch swatch = FindSwatch(token);
                if (swatch != null)
                {
                    source = swatch.Paint;
                    tokenHandled = true;
                }
                else
                {
                    LetteraPaintCatalogs.WarnUnknown(token);
                    // An unknown name uses the default paint, not this effect's choice.
                    tokenHandled = true;
                }
            }
            if (!tokenHandled && m_Paint != null)
            {
                if (m_Paint.Kind == LetteraPaintChoiceKind.Inline) source = m_Paint.Inline;
                else if (m_Paint.Kind == LetteraPaintChoiceKind.Named)
                {
                    LetteraSwatch swatch = FindSwatch(m_Paint.SwatchName);
                    if (swatch != null) source = swatch.Paint;
                    else LetteraPaintCatalogs.WarnUnknown(m_Paint.SwatchName);
                }
            }
            return LetteraPaintCatalogs.Resolve(source, m_Paint?.Settings, slots, projectionSlot, defaultColor, defaultIsGlyphColor);
        }
    }

    /// <summary>Takes over the glyph face. Slots: 1 paint, 2 dilate (em), 3 softness 0-1, then the paint settings.</summary>
    [Serializable]
    public class FillEffect : LetteraLayerEffect
    {
        [SerializeField] float m_Dilate;
        [SerializeField, Range(0, 1)] float m_Softness;

        public float Dilate { get => m_Dilate; set => m_Dilate = value; }
        public float Softness { get => m_Softness; set => m_Softness = value; }

        public override void Apply(LetteraAttributes attributes, int start, int end, in LetteraSlots slots)
        {
            attributes.AddLayer(start, end, m_Band == LetteraLayerBand.Auto ? LetteraLayerBand.Face : m_Band, new LayerRecord
            {
                Kind = LayerKind.Fill,
                Paint = ResolvePaint(slots, 0, 3, default, true),
                Dilate = new EmLength(slots.Length(1, new LetteraLength(m_Dilate))),
                Softness = Mathf.Clamp01(slots.Float(2, m_Softness)),
                OnColorGlyphs = m_OnColorGlyphs == LetteraOnColorGlyphs.Apply,
                FixedSize = m_FixedPixelSize,
            });
        }
    }

    public enum LetteraStrokeCorners : byte { Round, Sharp }

    /// <summary>
    /// A band following the outline. Slots: 1 paint, 2 width (em), 3 align (inside, center, outside or -1 to 1),
    /// 4 softness 0-1, 5 corners (sharp needs the MSDF render mode), then the paint settings.
    /// </summary>
    [Serializable]
    public class StrokeEffect : LetteraLayerEffect
    {
        [SerializeField] float m_Width = 0.1f;
        [SerializeField, Range(-1, 1)] float m_Align = 1f;
        [SerializeField, Range(0, 1)] float m_Softness;
        [SerializeField] LetteraStrokeCorners m_Corners;

        public LetteraStrokeCorners Corners { get => m_Corners; set => m_Corners = value; }
        public float Width { get => m_Width; set => m_Width = value; }
        public float Align { get => m_Align; set => m_Align = value; }
        public float Softness { get => m_Softness; set => m_Softness = value; }

        public override void Apply(LetteraAttributes attributes, int start, int end, in LetteraSlots slots)
        {
            float align = ParseAlign(slots[2], m_Align);
            // What lies inside the glyph would be hidden under the face, so such a stroke draws over it.
            LetteraLayerBand band = m_Band != LetteraLayerBand.Auto ? m_Band : align < 1 ? LetteraLayerBand.Front : LetteraLayerBand.Rim;
            attributes.AddLayer(start, end, band, new LayerRecord
            {
                Kind = LayerKind.Stroke,
                Paint = ResolvePaint(slots, 0, 5, new Color32(0, 0, 0, 255), false),
                Width = new EmLength(slots.Length(1, new LetteraLength(m_Width))),
                Align = align,
                SharpCorners = slots.Enum(4, m_Corners) == LetteraStrokeCorners.Sharp,
                Softness = Mathf.Clamp01(slots.Float(3, m_Softness)),
                OnColorGlyphs = m_OnColorGlyphs == LetteraOnColorGlyphs.Apply,
                FixedSize = m_FixedPixelSize,
            });
        }

        static float ParseAlign(string token, float fallback)
        {
            if (string.IsNullOrEmpty(token)) return fallback;
            if (token.Equals("inside", StringComparison.OrdinalIgnoreCase)) return -1;
            if (token.Equals("center", StringComparison.OrdinalIgnoreCase)) return 0;
            if (token.Equals("outside", StringComparison.OrdinalIgnoreCase)) return 1;
            return LetteraSlots.TryNumber(token, out float value) ? Mathf.Clamp(value, -1, 1) : fallback;
        }
    }

    /// <summary>A soft halo around the glyph. Slots: 1 paint, 2 radius (em), 3 softness 0-1, 4 dilate (em), then the paint settings.</summary>
    [Serializable]
    public class GlowEffect : LetteraLayerEffect
    {
        [SerializeField] float m_Radius = 0.2f;
        [SerializeField, Range(0, 1)] float m_Softness = 1f;
        [SerializeField] float m_Dilate;

        public float Radius { get => m_Radius; set => m_Radius = value; }
        public float Softness { get => m_Softness; set => m_Softness = value; }
        public float Dilate { get => m_Dilate; set => m_Dilate = value; }

        public override void Apply(LetteraAttributes attributes, int start, int end, in LetteraSlots slots)
        {
            attributes.AddLayer(start, end, m_Band == LetteraLayerBand.Auto ? LetteraLayerBand.Back : m_Band, new LayerRecord
            {
                Kind = LayerKind.Glow,
                Paint = ResolvePaint(slots, 0, 4, default, true),
                Width = new EmLength(slots.Length(1, new LetteraLength(m_Radius))),
                Softness = Mathf.Clamp01(slots.Float(2, m_Softness)),
                Dilate = new EmLength(slots.Length(3, new LetteraLength(m_Dilate))),
                OnColorGlyphs = m_OnColorGlyphs != LetteraOnColorGlyphs.Skip,
                FixedSize = m_FixedPixelSize,
            });
        }
    }

    /// <summary>A shadow cast inward from the edge. Slots: 1 paint, 2 and 3 offset (em), 4 softness 0-1, 5 choke (em), then the paint settings.</summary>
    [Serializable]
    public class InnerShadowEffect : LetteraLayerEffect
    {
        [SerializeField] Vector2 m_Offset = new Vector2(0.03f, -0.03f);
        [SerializeField, Range(0, 1)] float m_Softness = 0.3f;
        [SerializeField] float m_Choke;

        public Vector2 Offset { get => m_Offset; set => m_Offset = value; }
        public float Softness { get => m_Softness; set => m_Softness = value; }
        public float Choke { get => m_Choke; set => m_Choke = value; }

        public override void Apply(LetteraAttributes attributes, int start, int end, in LetteraSlots slots)
        {
            attributes.AddLayer(start, end, m_Band == LetteraLayerBand.Auto ? LetteraLayerBand.Front : m_Band, new LayerRecord
            {
                Kind = LayerKind.InnerShadow,
                Paint = ResolvePaint(slots, 0, 5, new Color32(0, 0, 0, 128), false),
                OffsetX = new EmLength(slots.Length(1, new LetteraLength(m_Offset.x))),
                OffsetY = new EmLength(slots.Length(2, new LetteraLength(m_Offset.y))),
                Softness = Mathf.Clamp01(slots.Float(3, m_Softness)),
                Dilate = new EmLength(slots.Length(4, new LetteraLength(m_Choke))),
                OnColorGlyphs = m_OnColorGlyphs == LetteraOnColorGlyphs.Apply,
                FixedSize = m_FixedPixelSize,
            });
        }
    }
}

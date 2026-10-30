using System;
using System.Collections.Generic;
using UnityEngine;

namespace FirstSymbol.Lettera
{
    public enum LetteraPaintOrder : byte { LayerMajor, GlyphMajor }
    public enum LetteraMaterialOrder : byte { Replace, Keep }

    /// <summary>
    /// Stacked copies with near-to-far shading, for a flat depth look; closed 3D text is another feature. Slots: 1 and 2 offset (em),
    /// 3 near color, 4 far color, 5 dilate (em), 6 softness. Steps quads per glyph, twice that with Bevel, which adds a face between every two.
    /// </summary>
    [Serializable]
    public class ExtrudeEffect : LetteraLayerEffect
    {
        [SerializeField] Vector2 m_Offset = new Vector2(0.06f, -0.06f);
        [SerializeField] Color32 m_NearColor = new Color32(0, 0, 0, 255);
        [SerializeField] Color32 m_FarColor = new Color32(0, 0, 0, 255);
        [SerializeField] float m_Dilate;
        [SerializeField, Range(0, 1)] float m_Softness;
        [SerializeField, Range(1, 64)] int m_Steps = 8;
        [SerializeField] bool m_Bevel;

        public Vector2 Offset { get => m_Offset; set => m_Offset = value; }
        public Color32 NearColor { get => m_NearColor; set => m_NearColor = value; }
        public Color32 FarColor { get => m_FarColor; set => m_FarColor = value; }
        public float Dilate { get => m_Dilate; set => m_Dilate = value; }
        public float Softness { get => m_Softness; set => m_Softness = value; }
        public int Steps { get => m_Steps; set => m_Steps = value; }
        public bool Bevel { get => m_Bevel; set => m_Bevel = value; }

        public override void Apply(LetteraAttributes attributes, int start, int end, in LetteraSlots slots)
        {
            attributes.AddLayer(start, end, m_Band == LetteraLayerBand.Auto ? LetteraLayerBand.Back : m_Band, new LayerRecord
            {
                Kind = LayerKind.Extrude,
                Paint = new ResolvedPaint { Color = slots.Color(2, m_NearColor), Blend = LetteraBlend.Normal },
                FarColor = slots.Color(3, m_FarColor),
                OffsetX = new EmLength(slots.Length(0, new LetteraLength(m_Offset.x))),
                OffsetY = new EmLength(slots.Length(1, new LetteraLength(m_Offset.y))),
                Dilate = new EmLength(slots.Length(4, new LetteraLength(m_Dilate))),
                Softness = Mathf.Clamp01(slots.Float(5, m_Softness)),
                Steps = Mathf.Clamp(m_Steps, 1, 64) * (m_Bevel ? 2 : 1),
                OnColorGlyphs = m_OnColorGlyphs == LetteraOnColorGlyphs.Apply,
                FixedSize = m_FixedPixelSize,
            });
        }
    }

    /// <summary>A color filter: one 4 by 5 matrix, applied to colors in the range. Derive from this for your own.</summary>
    [Serializable]
    public abstract class LetteraColorFilter
    {
        /// <summary>The matrix, row by row: r, g, b, a, each as four factors and an offset.</summary>
        public abstract void GetMatrix(float[] matrix);

        internal virtual LetteraColorFilter Copy() => (LetteraColorFilter)MemberwiseClone();

        protected static void Identity(float[] m)
        {
            Array.Clear(m, 0, 20);
            m[0] = m[6] = m[12] = m[18] = 1;
        }
    }

    [Serializable]
    public class GrayscaleFilter : LetteraColorFilter
    {
        public override void GetMatrix(float[] m)
        {
            Identity(m);
            for (int row = 0; row < 3; row++)
            {
                m[row * 5] = 0.2126f;
                m[row * 5 + 1] = 0.7152f;
                m[row * 5 + 2] = 0.0722f;
            }
        }
    }

    /// <summary>1 leaves colors unchanged, 0 is gray.</summary>
    [Serializable]
    public class SaturationFilter : LetteraColorFilter
    {
        public float Amount = 1f;

        public override void GetMatrix(float[] m)
        {
            Identity(m);
            float s = Amount;
            m[0] = 0.2126f + 0.7874f * s; m[1] = 0.7152f - 0.7152f * s; m[2] = 0.0722f - 0.0722f * s;
            m[5] = 0.2126f - 0.2126f * s; m[6] = 0.7152f + 0.2848f * s; m[7] = 0.0722f - 0.0722f * s;
            m[10] = 0.2126f - 0.2126f * s; m[11] = 0.7152f - 0.7152f * s; m[12] = 0.0722f + 0.9278f * s;
        }
    }

    /// <summary>Turns hues by an angle in degrees.</summary>
    [Serializable]
    public class HueRotateFilter : LetteraColorFilter
    {
        public float Degrees;

        public override void GetMatrix(float[] m)
        {
            Identity(m);
            float c = Mathf.Cos(Degrees * Mathf.Deg2Rad), s = Mathf.Sin(Degrees * Mathf.Deg2Rad);
            m[0] = 0.213f + c * 0.787f - s * 0.213f; m[1] = 0.715f - c * 0.715f - s * 0.715f; m[2] = 0.072f - c * 0.072f + s * 0.928f;
            m[5] = 0.213f - c * 0.213f + s * 0.143f; m[6] = 0.715f + c * 0.285f + s * 0.140f; m[7] = 0.072f - c * 0.072f - s * 0.283f;
            m[10] = 0.213f - c * 0.213f - s * 0.787f; m[11] = 0.715f - c * 0.715f + s * 0.715f; m[12] = 0.072f + c * 0.928f + s * 0.072f;
        }
    }

    /// <summary>1 leaves colors unchanged.</summary>
    [Serializable]
    public class BrightnessFilter : LetteraColorFilter
    {
        public float Amount = 1f;

        public override void GetMatrix(float[] m)
        {
            Identity(m);
            m[0] = m[6] = m[12] = Amount;
        }
    }

    /// <summary>1 leaves colors unchanged, 0 is mid gray.</summary>
    [Serializable]
    public class ContrastFilter : LetteraColorFilter
    {
        public float Amount = 1f;

        public override void GetMatrix(float[] m)
        {
            Identity(m);
            m[0] = m[6] = m[12] = Amount;
            m[4] = m[9] = m[14] = 0.5f - 0.5f * Amount;
        }
    }

    /// <summary>Stops of light: each stop doubles.</summary>
    [Serializable]
    public class ExposureFilter : LetteraColorFilter
    {
        public float Stops;

        public override void GetMatrix(float[] m)
        {
            Identity(m);
            m[0] = m[6] = m[12] = Mathf.Pow(2, Stops);
        }
    }

    [Serializable]
    public class InvertFilter : LetteraColorFilter
    {
        public override void GetMatrix(float[] m)
        {
            Identity(m);
            m[0] = m[6] = m[12] = -1;
            m[4] = m[9] = m[14] = 1;
        }
    }

    [Serializable]
    public class SepiaFilter : LetteraColorFilter
    {
        public override void GetMatrix(float[] m)
        {
            Identity(m);
            m[0] = 0.393f; m[1] = 0.769f; m[2] = 0.189f;
            m[5] = 0.349f; m[6] = 0.686f; m[7] = 0.168f;
            m[10] = 0.272f; m[11] = 0.534f; m[12] = 0.131f;
        }
    }

    /// <summary>Multiplies colors by a color.</summary>
    [Serializable]
    public class TintFilter : LetteraColorFilter
    {
        public Color Color = UnityEngine.Color.white;

        public override void GetMatrix(float[] m)
        {
            Identity(m);
            m[0] = Color.r;
            m[6] = Color.g;
            m[12] = Color.b;
            m[18] = Color.a;
        }
    }

    /// <summary>A raw matrix, row by row.</summary>
    [Serializable]
    public class ColorMatrixFilter : LetteraColorFilter
    {
        public float[] Values = { 1, 0, 0, 0, 0, 0, 1, 0, 0, 0, 0, 0, 1, 0, 0, 0, 0, 0, 1, 0 };

        internal override LetteraColorFilter Copy()
        {
            var copy = (ColorMatrixFilter)base.Copy();
            copy.Values = (float[])Values.Clone();
            return copy;
        }

        public override void GetMatrix(float[] m)
        {
            if (Values != null && Values.Length == 20) Array.Copy(Values, m, 20);
            else Identity(m);
        }
    }

    /// <summary>
    /// Changes the colors of what is stacked up to it: the face and the layers of earlier styles. Styles of the global preset come after a
    /// component's, so a filter on the component does not reach them; a filter last in the list recolors everything.
    /// Slots: 1 intensity (0-1 or %), 2 a filter's name when the effect has none: grayscale, invert, sepia.
    /// </summary>
    [Serializable]
    public class FilterEffect : LetteraEffect
    {
        [SerializeReference] LetteraColorFilter m_Filter;
        [SerializeField, Range(0, 1)] float m_Intensity = 1f;

        public FilterEffect() { }

        public FilterEffect(LetteraColorFilter filter) => m_Filter = filter;

        public LetteraColorFilter Filter { get => m_Filter; set => m_Filter = value; }
        public float Intensity { get => m_Intensity; set => m_Intensity = value; }

        public override LetteraEffect Clone()
        {
            var copy = (FilterEffect)base.Clone();
            copy.m_Filter = m_Filter?.Copy();
            return copy;
        }

        public override void Apply(LetteraAttributes attributes, int start, int end, in LetteraSlots slots)
        {
            LetteraColorFilter filter = m_Filter;
            if (filter == null)
            {
                switch ((slots[1] ?? "").ToLowerInvariant())
                {
                    case "grayscale": filter = new GrayscaleFilter(); break;
                    case "invert": filter = new InvertFilter(); break;
                    case "sepia": filter = new SepiaFilter(); break;
                    default: return;
                }
            }
            float intensity = m_Intensity;
            if (LetteraLength.TryParse(slots[0], out LetteraLength parsed))
                intensity = parsed.Kind == LetteraLength.Unit.Percent ? parsed.Value * 0.01f : parsed.Value;
            var matrix = new float[20];
            filter.GetMatrix(matrix);
            intensity = Mathf.Clamp01(intensity);
            if (intensity < 1)
            {
                for (int i = 0; i < 20; i++)
                {
                    float identity = i == 0 || i == 6 || i == 12 || i == 18 ? 1 : 0;
                    matrix[i] = identity + (matrix[i] - identity) * intensity;
                }
            }
            attributes.AddFilter(start, end, matrix);
        }
    }

    /// <summary>Overlapping glyphs in the range, as in script fonts and Arabic, draw as one merged shape: no seam, and translucent paint does not add up.</summary>
    [Serializable]
    public class UnionEffect : LetteraEffect
    {
        public override void Apply(LetteraAttributes attributes, int start, int end, in LetteraSlots slots) => attributes.SetUnion(start, end);
    }

    /// <summary>
    /// Distorts the outline per pixel; the edge stays smooth, and fill, stroke, shadow and glow bend with it. Color glyphs are not warped.
    /// Slots: 1 amplitude, 2 scale, both in glyph heights, 3 travel per phase unit. Phase changes only from a script.
    /// </summary>
    [Serializable]
    public class WarpEffect : LetteraEffect
    {
        [SerializeField, Min(0)] float m_Amplitude = 0.02f;
        [SerializeField, Min(0.001f)] float m_Scale = 0.25f;
        [SerializeField] float m_Travel = 1f;
        [SerializeField] float m_Phase;

        public float Amplitude { get => m_Amplitude; set => m_Amplitude = value; }
        public float Scale { get => m_Scale; set => m_Scale = value; }
        public float Travel { get => m_Travel; set => m_Travel = value; }
        public float Phase { get => m_Phase; set => m_Phase = value; }

        public override void Apply(LetteraAttributes attributes, int start, int end, in LetteraSlots slots)
        {
            attributes.SetWarp(start, end, new WarpSpec
            {
                Amplitude = Mathf.Max(slots.Float(0, m_Amplitude), 0),
                Scale = Mathf.Max(slots.Float(1, m_Scale), 0.001f),
                Phase = m_Phase * slots.Float(2, m_Travel),
            });
        }
    }

    internal struct WarpSpec
    {
        public float Amplitude, Scale, Phase;
    }

    /// <summary>Layer-major finishes one layer on every glyph first. Glyph-major finishes every layer of a glyph first, for overlapping neighbours; Reverse lets the first glyph end on top. A whole-text setting.</summary>
    [Serializable]
    public class PaintOrderEffect : LetteraEffect
    {
        [SerializeField] LetteraPaintOrder m_Order;
        [SerializeField] bool m_Reverse;

        public LetteraPaintOrder Order { get => m_Order; set => m_Order = value; }
        public bool Reverse { get => m_Reverse; set => m_Reverse = value; }

        public override void Apply(LetteraAttributes attributes, int start, int end, in LetteraSlots slots) =>
            attributes.SetPaintOrder(slots.Enum(0, m_Order), slots.Bool(1, m_Reverse));
    }

    /// <summary>
    /// Draws a range with a material of your own: Replace shows it in place of the face, Keep draws it as a layer of its own. Other layers stay as
    /// they are. The range gets its own draw, so it splits the batch. Slot: 1 tint.
    /// </summary>
    [Serializable]
    public class MaterialEffect : LetteraLayerEffect
    {
        [SerializeField] Material m_Material;
        [SerializeField] LetteraMaterialOrder m_Order;
        [SerializeField] Color32 m_Tint = new Color32(255, 255, 255, 255);

        [NonSerialized] bool m_Warned;

        public Material Material { get => m_Material; set => m_Material = value; }
        public LetteraMaterialOrder Order { get => m_Order; set => m_Order = value; }
        public Color32 Tint { get => m_Tint; set => m_Tint = value; }

        public override void Apply(LetteraAttributes attributes, int start, int end, in LetteraSlots slots)
        {
            if (m_Material == null)
            {
                if (!m_Warned) Debug.LogWarning("Lettera: a material effect has no material, so it does nothing.");
                m_Warned = true;
                return;
            }
            LetteraLayerBand band = m_Order == LetteraMaterialOrder.Replace ? LetteraLayerBand.Face : LetteraLayerBand.Front;
            attributes.AddLayer(start, end, m_Band == LetteraLayerBand.Auto ? band : m_Band, new LayerRecord
            {
                Kind = LayerKind.Fill,
                Paint = new ResolvedPaint { Color = slots.Color(0, m_Tint), Blend = LetteraBlend.Normal },
                Custom = m_Material,
            });
        }
    }

    /// <summary>
    /// Places each line on a circle by arc length, each glyph turned to the tangent, with lines on concentric arcs. Slots: 1 radius (local units or em;
    /// positive bends the ends down), 2 anchor, 0 start, 0.5 center, 1 end, which point of the line stays put. Applies to whole lines. Decoration lines are not drawn on curved text.
    /// </summary>
    [Serializable]
    public class CurveEffect : LetteraEffect
    {
        [SerializeField] float m_Radius = 200f;
        [SerializeField, Range(0, 1)] float m_Anchor = 0.5f;

        public float Radius { get => m_Radius; set => m_Radius = value; }
        public float Anchor { get => m_Anchor; set => m_Anchor = value; }

        public override void Apply(LetteraAttributes attributes, int start, int end, in LetteraSlots slots)
        {
            attributes.SetCurve(new LayoutLength(slots.Length(0, new LetteraLength(m_Radius))), Mathf.Clamp01(slots.Float(1, m_Anchor)));
        }
    }
}

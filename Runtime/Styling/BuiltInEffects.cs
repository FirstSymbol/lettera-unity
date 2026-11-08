using System;
using System.Collections.Generic;
using UnityEngine;

namespace FirstSymbol.Lettera
{
    /// <summary>Requests a weight. Slots: 1 weight (100-900), 2 mode: f = force synthetic, r = real faces only.</summary>
    [Serializable]
    public class BoldEffect : LetteraEffect
    {
        [SerializeField, Range(100, 900)] int m_Weight = 700;

        public int Weight { get => m_Weight; set => m_Weight = value; }

        public override IReadOnlyList<LetteraParamInfo> Parameters => LetteraBuiltInSlots.Bold;

        public override void Apply(LetteraAttributes attributes, int start, int end, in LetteraSlots slots)
        {
            int weight = m_Weight;
            if (LetteraSlots.TryNumber(slots[0], out float parsed)) weight = Mathf.Clamp((int)parsed, 100, 900);
            string mode = slots[1];
            int boldMode = mode == "f" ? 1 : mode == "r" ? 2 : 0;
            attributes.SetWeight(start, end, weight, boldMode);
        }
    }

    /// <summary>Italic face or synthetic shear. Slot: 1 slant in percent of the glyph height.</summary>
    [Serializable]
    public class ItalicEffect : LetteraEffect
    {
        public override IReadOnlyList<LetteraParamInfo> Parameters => LetteraBuiltInSlots.Italic;

        public override void Apply(LetteraAttributes attributes, int start, int end, in LetteraSlots slots)
        {
            attributes.SetItalic(start, end, LetteraSlots.TryNumber(slots[0], out float slant) ? slant : -1);
        }
    }

    /// <summary>Slot: 24 (absolute), 150% (of the enclosing size), +10 / -5 (delta), 1.2em (relative).</summary>
    [Serializable]
    public class SizeEffect : LetteraEffect
    {
        [SerializeField] string m_Size = "100%";

        public string Size { get => m_Size; set => m_Size = value; }

        public override IReadOnlyList<LetteraParamInfo> Parameters => LetteraBuiltInSlots.Size;

        public override void Apply(LetteraAttributes attributes, int start, int end, in LetteraSlots slots)
        {
            string value = slots[0] ?? m_Size;
            if (string.IsNullOrEmpty(value)) return;
            if (value.EndsWith("%", StringComparison.Ordinal))
            {
                if (LetteraSlots.TryNumber(value.Substring(0, value.Length - 1), out float percent))
                    attributes.MultiplySize(start, end, percent / 100f);
            }
            else if (value.EndsWith("em", StringComparison.OrdinalIgnoreCase))
            {
                if (LetteraSlots.TryNumber(value.Substring(0, value.Length - 2), out float em))
                    attributes.MultiplySize(start, end, em);
            }
            else if (value[0] == '+' || value[0] == '-')
            {
                if (LetteraSlots.TryNumber(value, out float delta))
                    attributes.AddSize(start, end, delta);
            }
            else if (LetteraSlots.TryNumber(value, out float size) && size > 0)
            {
                attributes.SetSizeAbsolute(start, end, size);
            }
        }
    }

    /// <summary>Slot: a color. Replaces the glyph color; color glyphs are not tinted.</summary>
    [Serializable]
    public class ColorEffect : LetteraEffect
    {
        [SerializeField] Color32 m_Color = new Color32(255, 255, 255, 255);

        public Color32 Color { get => m_Color; set => m_Color = value; }

        public override IReadOnlyList<LetteraParamInfo> Parameters => LetteraBuiltInSlots.Color;

        public override void Apply(LetteraAttributes attributes, int start, int end, in LetteraSlots slots)
        {
            attributes.SetColor(start, end, slots.Color(0, m_Color));
        }
    }

    /// <summary>Slot: 0.5, 50% or #80. Nested alpha ranges do not compound.</summary>
    [Serializable]
    public class AlphaEffect : LetteraEffect
    {
        [SerializeField, Range(0, 1)] float m_Alpha = 1f;

        public float Alpha { get => m_Alpha; set => m_Alpha = value; }

        public override IReadOnlyList<LetteraParamInfo> Parameters => LetteraBuiltInSlots.Alpha;

        public override void Apply(LetteraAttributes attributes, int start, int end, in LetteraSlots slots)
        {
            float alpha = m_Alpha;
            string value = slots[0];
            if (!string.IsNullOrEmpty(value))
            {
                if (value[0] == '#' && value.Length == 3)
                {
                    if (LetteraSlots.TryColor("#000000" + value.Substring(1), out Color32 hex)) alpha = hex.a / 255f;
                }
                else if (value.EndsWith("%", StringComparison.Ordinal))
                {
                    if (LetteraSlots.TryNumber(value.Substring(0, value.Length - 1), out float percent)) alpha = percent / 100f;
                }
                else if (LetteraSlots.TryNumber(value, out float number))
                {
                    alpha = number;
                }
            }
            attributes.SetAlpha(start, end, Mathf.Clamp01(alpha));
        }
    }

    /// <summary>Slot: a family name of the stack. A bare tag uses the configured family.</summary>
    [Serializable]
    public class FontEffect : LetteraEffect
    {
        [SerializeField] string m_Family = "";

        public string Family { get => m_Family; set => m_Family = value; }

        public override IReadOnlyList<LetteraParamInfo> Parameters => LetteraBuiltInSlots.Font;

        public override void Apply(LetteraAttributes attributes, int start, int end, in LetteraSlots slots)
        {
            string family = slots[0] ?? m_Family;
            if (!string.IsNullOrEmpty(family)) attributes.SetFamily(start, end, family);
        }
    }

    /// <summary>Slot: a BCP 47 language tag. Malformed tags are ignored with one warning.</summary>
    [Serializable]
    public class LanguageEffect : LetteraEffect
    {
        [SerializeField] string m_Language = "";

        public string Language { get => m_Language; set => m_Language = value; }

        public override IReadOnlyList<LetteraParamInfo> Parameters => LetteraBuiltInSlots.Language;

        public override void Apply(LetteraAttributes attributes, int start, int end, in LetteraSlots slots)
        {
            string language = slots[0] ?? m_Language;
            if (LanguageTags.IsWellFormed(language)) attributes.SetLanguage(start, end, language);
        }
    }

    /// <summary>
    /// The base of underline and strikethrough. Slots: 1 paint (default: the glyph's color), 2 line style, 3 thickness (em, or auto),
    /// 4 offset from the baseline (em, or auto), 5 skip ink, 6 overlay, then the paint settings.
    /// </summary>
    [Serializable]
    public abstract class LetteraLineEffect : LetteraLayerEffect
    {
        [SerializeField] LetteraLineStyle m_LineStyle;
        [SerializeField] bool m_SkipInk;
        [SerializeField] bool m_Overlay;

        public LetteraLineStyle LineStyle { get => m_LineStyle; set => m_LineStyle = value; }
        public bool SkipInk { get => m_SkipInk; set => m_SkipInk = value; }
        public bool Overlay { get => m_Overlay; set => m_Overlay = value; }

        public override IReadOnlyList<LetteraParamInfo> Parameters => LetteraBuiltInSlots.Line;

        private protected void AddLine(LetteraAttributes attributes, int start, int end, in LetteraSlots slots, bool strike)
        {
            var spec = new LetteraAttributes.DecorationSpec
            {
                Paint = ResolvePaint(slots, 0, 6, default, true),
                Style = slots.Enum(1, m_LineStyle),
                SkipInk = slots.Bool(4, m_SkipInk),
                Overlay = slots.Bool(5, m_Overlay),
            };
            spec.AutoThickness = !LetteraLength.TryParse(slots[2], out LetteraLength thickness);
            if (!spec.AutoThickness) spec.Thickness = new EmLength(thickness);
            spec.AutoOffset = !LetteraLength.TryParse(slots[3], out LetteraLength offset);
            if (!spec.AutoOffset) spec.Offset = new EmLength(offset);
            attributes.AddDecoration(start, end, strike, spec);
        }
    }

    /// <summary>A line under the range. See the base class for its slots.</summary>
    [Serializable]
    public class UnderlineEffect : LetteraLineEffect
    {
        public override void Apply(LetteraAttributes attributes, int start, int end, in LetteraSlots slots) => AddLine(attributes, start, end, slots, false);
    }

    /// <summary>A line through the range. See the base class for its slots.</summary>
    [Serializable]
    public class StrikethroughEffect : LetteraLineEffect
    {
        public override void Apply(LetteraAttributes attributes, int start, int end, in LetteraSlots slots) => AddLine(attributes, start, end, slots, true);
    }

    /// <summary>A stroke fully outside the glyph. Slots: 1 paint, 2 width in em (or with a px suffix, in local units), then the paint settings. Not applied to color glyphs.</summary>
    [Serializable]
    public class OutlineEffect : LetteraLayerEffect
    {
        [SerializeField] Color32 m_Color = new Color32(0, 0, 0, 255);
        [SerializeField, Range(0, 1)] float m_Width = 0.1f;

        public Color32 Color { get => m_Color; set => m_Color = value; }
        public float Width { get => m_Width; set => m_Width = value; }

        public override IReadOnlyList<LetteraParamInfo> Parameters => LetteraBuiltInSlots.Outline;

        public override void Apply(LetteraAttributes attributes, int start, int end, in LetteraSlots slots)
        {
            attributes.AddLayer(start, end, m_Band == LetteraLayerBand.Auto ? LetteraLayerBand.Rim : m_Band, new LayerRecord
            {
                Kind = LayerKind.Stroke,
                Paint = ResolvePaint(slots, 0, 2, m_Color, false),
                Width = new EmLength(slots.Length(1, new LetteraLength(m_Width))),
                Align = 1,
                OnColorGlyphs = m_OnColorGlyphs == LetteraOnColorGlyphs.Apply,
                FixedSize = m_FixedPixelSize,
            });
        }
    }

    /// <summary>Slots: 1 paint, 2 offset x, 3 offset y (em), 4 softness 0-1, 5 dilate (em), then the paint settings. Applies to color glyphs via alpha.</summary>
    [Serializable]
    public class ShadowEffect : LetteraLayerEffect
    {
        [SerializeField] Color32 m_Color = new Color32(0, 0, 0, 128);
        [SerializeField] Vector2 m_Offset = new Vector2(0.05f, -0.05f);
        [SerializeField, Range(0, 1)] float m_Softness = 0.1f;
        [SerializeField] float m_Dilate;

        public Color32 Color { get => m_Color; set => m_Color = value; }
        public Vector2 Offset { get => m_Offset; set => m_Offset = value; }
        public float Softness { get => m_Softness; set => m_Softness = value; }
        public float Dilate { get => m_Dilate; set => m_Dilate = value; }

        public override IReadOnlyList<LetteraParamInfo> Parameters => LetteraBuiltInSlots.Shadow;

        public override void Apply(LetteraAttributes attributes, int start, int end, in LetteraSlots slots)
        {
            attributes.AddLayer(start, end, m_Band == LetteraLayerBand.Auto ? LetteraLayerBand.Back : m_Band, new LayerRecord
            {
                Kind = LayerKind.Shadow,
                Paint = ResolvePaint(slots, 0, 5, m_Color, false),
                OffsetX = new EmLength(slots.Length(1, new LetteraLength(m_Offset.x))),
                OffsetY = new EmLength(slots.Length(2, new LetteraLength(m_Offset.y))),
                Softness = Mathf.Clamp01(slots.Float(3, m_Softness)),
                Dilate = new EmLength(slots.Length(4, new LetteraLength(m_Dilate))),
                OnColorGlyphs = m_OnColorGlyphs != LetteraOnColorGlyphs.Skip,
                FixedSize = m_FixedPixelSize,
            });
        }
    }

    /// <summary>Slot: position 0 start, 0.5 middle, 1 end. Replaces clusters that do not fit with a marker.</summary>
    [Serializable]
    public class EllipsisEffect : LetteraEffect
    {
        [SerializeField, Range(0, 1)] float m_Position = 1f;

        public float Position { get => m_Position; set => m_Position = value; }

        public override void Apply(LetteraAttributes attributes, int start, int end, in LetteraSlots slots)
        {
            attributes.RequestElide(start, end, LetteraSlots.TryNumber(slots[0], out float p) ? p : m_Position, true);
        }
    }

    /// <summary>
    /// Removes clusters that do not fit. Slots: 1 position 0 start, 0.5 middle, 1 end; 2 fade distance in local units or em, 0 none;
    /// 3 fade shift: positive moves the fully faded point away from the cut, into the text.
    /// </summary>
    [Serializable]
    public class TruncateEffect : LetteraEffect
    {
        [SerializeField, Range(0, 1)] float m_Position = 1f;
        [SerializeField, Min(0)] float m_FadeDistance;
        [SerializeField] float m_FadeShift;

        public float Position { get => m_Position; set => m_Position = value; }
        public float FadeDistance { get => m_FadeDistance; set => m_FadeDistance = value; }
        public float FadeShift { get => m_FadeShift; set => m_FadeShift = value; }

        public override void Apply(LetteraAttributes attributes, int start, int end, in LetteraSlots slots)
        {
            attributes.RequestElide(start, end, LetteraSlots.TryNumber(slots[0], out float p) ? p : m_Position, false,
                new LayoutLength(slots.Length(1, new LetteraLength(m_FadeDistance))), new LayoutLength(slots.Length(2, new LetteraLength(m_FadeShift))));
        }
    }

    /// <summary>Slot: ltr, rtl or auto. Sets the base direction of every paragraph, even when bound to a range.</summary>
    [Serializable]
    public class DirectionEffect : LetteraEffect
    {
        [SerializeField] string m_Direction = "auto";

        public string Direction { get => m_Direction; set => m_Direction = value; }

        public override void Apply(LetteraAttributes attributes, int start, int end, in LetteraSlots slots)
        {
            string value = (slots[0] ?? m_Direction ?? "auto").ToLowerInvariant();
            attributes.SetDirection(value == "ltr" ? 0 : value == "rtl" ? 1 : 2);
        }
    }

    /// <summary>BCP 47 syntax check. Every distinct malformed tag warns once.</summary>
    internal static class LanguageTags
    {
        static readonly System.Collections.Generic.HashSet<string> s_Warned = new System.Collections.Generic.HashSet<string>();

        public static bool IsWellFormed(string tag)
        {
            if (string.IsNullOrEmpty(tag)) return false;
            int subtag = 0;
            bool valid = tag.Length <= 64;
            foreach (char c in tag)
            {
                if (c == '-')
                {
                    if (subtag == 0) valid = false;
                    subtag = 0;
                    continue;
                }
                bool asciiAlnum = (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9');
                if (!asciiAlnum || ++subtag > 8) valid = false;
            }
            if (subtag == 0) valid = false;
            if (!valid && s_Warned.Add(tag)) Debug.LogWarning($"Lettera: '{tag}' is not a well-formed language tag; it is ignored.");
            return valid;
        }
    }
}

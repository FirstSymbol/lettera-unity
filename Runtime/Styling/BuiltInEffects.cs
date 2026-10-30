using System;
using UnityEngine;

namespace FirstSymbol.Lettera
{
    /// <summary>Requests a weight. Slots: 1 weight (100-900), 2 mode: f = force synthetic, r = real faces only.</summary>
    [Serializable]
    public class BoldEffect : LetteraEffect
    {
        [SerializeField, Range(100, 900)] int m_Weight = 700;

        public int Weight { get => m_Weight; set => m_Weight = value; }

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

        public override void Apply(LetteraAttributes attributes, int start, int end, in LetteraSlots slots)
        {
            attributes.SetColor(start, end, LetteraSlots.TryColor(slots[0], out Color32 color) ? color : m_Color);
        }
    }

    /// <summary>Slot: 0.5, 50% or #80. Nested alpha ranges do not compound.</summary>
    [Serializable]
    public class AlphaEffect : LetteraEffect
    {
        [SerializeField, Range(0, 1)] float m_Alpha = 1f;

        public float Alpha { get => m_Alpha; set => m_Alpha = value; }

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

        public override void Apply(LetteraAttributes attributes, int start, int end, in LetteraSlots slots)
        {
            string language = slots[0] ?? m_Language;
            if (LanguageTags.IsWellFormed(language)) attributes.SetLanguage(start, end, language);
        }
    }

    /// <summary>Slot: an optional color; the default is each run's glyph color.</summary>
    [Serializable]
    public class UnderlineEffect : LetteraEffect
    {
        public override void Apply(LetteraAttributes attributes, int start, int end, in LetteraSlots slots)
        {
            bool has = LetteraSlots.TryColor(slots[0], out Color32 color);
            attributes.SetUnderline(start, end, has, color);
        }
    }

    /// <summary>Slot: an optional color; the default is each run's glyph color.</summary>
    [Serializable]
    public class StrikethroughEffect : LetteraEffect
    {
        public override void Apply(LetteraAttributes attributes, int start, int end, in LetteraSlots slots)
        {
            bool has = LetteraSlots.TryColor(slots[0], out Color32 color);
            attributes.SetStrike(start, end, has, color);
        }
    }

    /// <summary>Slots: 1 color, 2 width in em (or with a px suffix, in local units). Not applied to color glyphs.</summary>
    [Serializable]
    public class OutlineEffect : LetteraEffect
    {
        [SerializeField] Color32 m_Color = new Color32(0, 0, 0, 255);
        [SerializeField, Range(0, GlyphAtlas.SpreadEm)] float m_Width = 0.1f;

        public Color32 Color { get => m_Color; set => m_Color = value; }
        public float Width { get => m_Width; set => m_Width = value; }

        public override void Apply(LetteraAttributes attributes, int start, int end, in LetteraSlots slots)
        {
            Color32 color = LetteraSlots.TryColor(slots[0], out Color32 parsed) ? parsed : m_Color;
            float em = m_Width, units = 0;
            string width = slots[1];
            if (!string.IsNullOrEmpty(width))
            {
                if (width.EndsWith("px", StringComparison.OrdinalIgnoreCase))
                {
                    if (LetteraSlots.TryNumber(width.Substring(0, width.Length - 2), out float px)) { units = px; em = 0; }
                }
                else if (LetteraSlots.TryNumber(width, out float value))
                {
                    em = value;
                }
            }
            attributes.SetOutline(start, end, color, em, units);
        }
    }

    /// <summary>Slots: 1 color, 2 offset x, 3 offset y (em), 4 softness 0-1, 5 dilate (em). Applies to color glyphs via alpha.</summary>
    [Serializable]
    public class ShadowEffect : LetteraEffect
    {
        [SerializeField] Color32 m_Color = new Color32(0, 0, 0, 128);
        [SerializeField] Vector2 m_Offset = new Vector2(0.05f, -0.05f);
        [SerializeField, Range(0, 1)] float m_Softness = 0.1f;
        [SerializeField, Range(0, GlyphAtlas.SpreadEm)] float m_Dilate;

        public Color32 Color { get => m_Color; set => m_Color = value; }
        public Vector2 Offset { get => m_Offset; set => m_Offset = value; }
        public float Softness { get => m_Softness; set => m_Softness = value; }
        public float Dilate { get => m_Dilate; set => m_Dilate = value; }

        public override void Apply(LetteraAttributes attributes, int start, int end, in LetteraSlots slots)
        {
            Color32 color = LetteraSlots.TryColor(slots[0], out Color32 parsed) ? parsed : m_Color;
            float x = LetteraSlots.TryNumber(slots[1], out float px) ? px : m_Offset.x;
            float y = LetteraSlots.TryNumber(slots[2], out float py) ? py : m_Offset.y;
            float softness = LetteraSlots.TryNumber(slots[3], out float s) ? Mathf.Clamp01(s) : m_Softness;
            float dilate = LetteraSlots.TryNumber(slots[4], out float d) ? d : m_Dilate;
            attributes.SetShadow(start, end, color, x, y, softness, dilate);
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

    /// <summary>Slot: position 0 start, 0.5 middle, 1 end. Removes clusters that do not fit.</summary>
    [Serializable]
    public class TruncateEffect : LetteraEffect
    {
        [SerializeField, Range(0, 1)] float m_Position = 1f;

        public float Position { get => m_Position; set => m_Position = value; }

        public override void Apply(LetteraAttributes attributes, int start, int end, in LetteraSlots slots)
        {
            attributes.RequestElide(start, end, LetteraSlots.TryNumber(slots[0], out float p) ? p : m_Position, false);
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

using System;
using System.Collections.Generic;
using UnityEngine;

namespace FirstSymbol.Lettera
{
    /// <summary>Base of everything a text can use as a font.</summary>
    public abstract partial class LetteraFontAsset : ScriptableObject
    {
        [SerializeField, HideInInspector] internal int LetteraSerializedVersion;

        /// <summary>Raised when an asset changes, so texts using it rebuild.</summary>
        public static event Action<LetteraFontAsset> FontAssetChanged;

        [Tooltip("Multiplies the size of this face.")]
        [SerializeField, Min(0.01f)] float m_FontScale = 1f;
        [Tooltip("When used as a fallback, scale this face so its x-height or cap height matches the primary face.")]
        [SerializeField] bool m_NormalizeSize = true;
        [Tooltip("Synthetic italic shear in percent of the glyph height (20 is about 11 degrees).")]
        [SerializeField, Range(0, 60)] float m_SyntheticItalicSlant = 20f;
        [Tooltip("Synthetic bold strength; 1 is the default amount.")]
        [SerializeField, Range(0, 4)] float m_SyntheticBoldStrength = 1f;
        [Tooltip("The size of the font's pixel grid, for a font drawn in pixels: 8 means an 8 pixel font. 0 renders it as an ordinary font.")]
        [SerializeField, Min(0)] int m_FontPixelsPerEm;
        [Tooltip("Keeps glyphs and the offsets of effects on the font's grid, so they move in whole pixels.")]
        [SerializeField] bool m_SnapToFontPixels = true;
        [SerializeField] bool m_OverrideMetrics;
        [SerializeField] int m_MetricAscender, m_MetricDescender, m_MetricLineGap;
        [Tooltip("Font units added to the advance of every glyph, for a face that sets too tight or too loose. Joined scripts such as Arabic keep theirs.")]
        [SerializeField] int m_SpacingOffset;
        [Tooltip("The advance of a space and a no-break space, in font units. 0 keeps the font's own.")]
        [SerializeField, Min(0)] int m_SpaceWidth;
        [SerializeField] List<LetteraGlyphOverride> m_GlyphOverrides = new List<LetteraGlyphOverride>();

        public float FontScale { get => m_FontScale; set { m_FontScale = value; NotifyChanged(); } }
        public bool NormalizeSize { get => m_NormalizeSize; set { m_NormalizeSize = value; NotifyChanged(); } }
        public float SyntheticItalicSlant { get => m_SyntheticItalicSlant; set { m_SyntheticItalicSlant = value; NotifyChanged(); } }
        public float SyntheticBoldStrength { get => m_SyntheticBoldStrength; set { m_SyntheticBoldStrength = value; NotifyChanged(); } }

        /// <summary>Font units added to every glyph's advance. Joined scripts keep theirs.</summary>
        public int SpacingOffset { get => m_SpacingOffset; set { m_SpacingOffset = value; NotifyChanged(); } }

        /// <summary>The advance of a space and a no-break space in font units; 0 keeps the font's own.</summary>
        public int SpaceWidth { get => m_SpaceWidth; set { m_SpaceWidth = Math.Max(0, value); NotifyChanged(); } }

        /// <summary>Glyphs set apart by character. After changing the list, call <see cref="GlyphOverridesChanged"/>.</summary>
        public List<LetteraGlyphOverride> GlyphOverrides => m_GlyphOverrides;

        /// <summary>Applies a changed <see cref="GlyphOverrides"/> list: texts using this font lay out and draw again.</summary>
        public void GlyphOverridesChanged() => NotifyChanged();

        public int FontPixelsPerEm { get => m_FontPixelsPerEm; set { m_FontPixelsPerEm = value; NotifyChanged(); } }
        public bool SnapToFontPixels { get => m_SnapToFontPixels; set { m_SnapToFontPixels = value; NotifyChanged(); } }

        /// <summary>Looks for the grid a font is drawn on. It returns 0 for a font that is not drawn in pixels.</summary>
        public int FindPixelGrid() => LoadFace()?.FindPixelGrid() ?? 0;

        internal abstract FontFace LoadFace();

        /// <summary>Family name read from the font, or an empty string when it cannot be loaded.</summary>
        public string FamilyName => LoadFace()?.Family ?? "";

        protected void NotifyChanged()
        {
            PushPixelSettings();
            FontAssetChanged?.Invoke(this);
        }

        // A loaded face knows its grid; the atlas and layout read it from there.
        protected void PushPixelSettings()
        {
            FontFace face = LoadedFace;
            if (face == null) return;
            face.PixelGrid = m_FontPixelsPerEm;
            face.PixelSnap = m_SnapToFontPixels;
            if (m_OverrideMetrics) face.OverrideMetrics(m_MetricAscender, m_MetricDescender, m_MetricLineGap);
            else face.OverrideMetrics(null, null, null);
            face.Tune(FaceTuning.Build(face, m_SpacingOffset, m_SpaceWidth, m_GlyphOverrides));
        }

        internal virtual FontFace LoadedFace => null;

        protected virtual void OnValidate() => NotifyChanged();
    }
}

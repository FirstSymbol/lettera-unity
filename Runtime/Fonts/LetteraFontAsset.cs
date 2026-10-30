using System;
using UnityEngine;

namespace FirstSymbol.Lettera
{
    /// <summary>Base of everything a text can use as a font.</summary>
    public abstract class LetteraFontAsset : ScriptableObject
    {
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

        public float FontScale { get => m_FontScale; set { m_FontScale = value; NotifyChanged(); } }
        public bool NormalizeSize { get => m_NormalizeSize; set { m_NormalizeSize = value; NotifyChanged(); } }
        public float SyntheticItalicSlant { get => m_SyntheticItalicSlant; set { m_SyntheticItalicSlant = value; NotifyChanged(); } }
        public float SyntheticBoldStrength { get => m_SyntheticBoldStrength; set { m_SyntheticBoldStrength = value; NotifyChanged(); } }

        internal abstract FontFace LoadFace();

        /// <summary>Family name read from the font, or an empty string when it cannot be loaded.</summary>
        public string FamilyName => LoadFace()?.Family ?? "";

        protected void NotifyChanged() => FontAssetChanged?.Invoke(this);

        protected virtual void OnValidate() => NotifyChanged();
    }
}

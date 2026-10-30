using System;
using System.Collections.Generic;
using UnityEngine;

namespace FirstSymbol.Lettera
{
    /// <summary>
    /// Another font's file at a pinned point of its design space, with its own name, weight and raster settings.
    /// Does not store the file again. A missing source counts as no font: texts fall back.
    /// </summary>
    [CreateAssetMenu(menuName = "Lettera/Font Variant", fileName = "New Lettera Font Variant", order = 101)]
    public class LetteraFontVariant : LetteraFontAsset
    {
        [Serializable]
        public struct AxisValue
        {
            [Tooltip("The axis tag, such as wght, wdth or GRAD.")]
            public string Tag;
            public float Value;
        }

        [SerializeField] LetteraFont m_Source;
        [Tooltip("The face inside a .ttc or .otc file; -1 uses the source's face.")]
        [SerializeField] int m_Face = -1;
        [SerializeField] List<AxisValue> m_Axes = new List<AxisValue>();
        [Tooltip("Empty keeps the family the file names.")]
        [SerializeField] string m_Family = "";
        [Tooltip("0 keeps the weight the file states.")]
        [SerializeField, Range(0, 900)] int m_Weight;
        [SerializeField] bool m_Italic;

        [NonSerialized] FontFace m_Loaded;
        [NonSerialized] bool m_LoadFailed;

        public LetteraFont Source { get => m_Source; set { m_Source = value; Unload(); NotifyChanged(); } }
        public int Face { get => m_Face; set { m_Face = value; Unload(); NotifyChanged(); } }
        public List<AxisValue> Axes => m_Axes;
        public string Family { get => m_Family; set { m_Family = value; Unload(); NotifyChanged(); } }
        public int Weight { get => m_Weight; set { m_Weight = value; Unload(); NotifyChanged(); } }
        public bool Italic { get => m_Italic; set { m_Italic = value; Unload(); NotifyChanged(); } }

        /// <summary>True when there is no source or its file cannot be read.</summary>
        public bool HasError => m_Source == null || !m_Source.HasData || m_LoadFailed;

        internal override FontFace LoadFace()
        {
            if (m_Loaded != null && m_Loaded.Face != IntPtr.Zero) return m_Loaded;
            if (m_LoadFailed || m_Source == null) return null;
            byte[] bytes = m_Source.ReadBytes();
            m_Loaded = bytes == null ? null : FontFace.FromBytes(bytes, m_Face >= 0 ? m_Face : m_Source.FaceIndex);
            if (m_Loaded == null)
            {
                m_LoadFailed = true;
                Debug.LogError($"Lettera: font variant '{name}' cannot read its source.", this);
                return null;
            }
            var entries = new List<AxisSet.Entry>(m_Axes.Count);
            foreach (AxisValue axis in m_Axes)
                if (!string.IsNullOrEmpty(axis.Tag) && axis.Tag.Length <= 4)
                    entries.Add(new AxisSet.Entry { Tag = LetteraNative.Tag(axis.Tag.PadRight(4)), Value = axis.Value });
            m_Loaded.Customize(entries.Count > 0 ? AxisSet.Of(entries) : null, m_Family, m_Weight, m_Italic);
            return m_Loaded;
        }

        void Unload()
        {
            m_Loaded = null;
            m_LoadFailed = false;
        }

        protected override void OnValidate()
        {
            Unload();
            base.OnValidate();
        }
    }
}

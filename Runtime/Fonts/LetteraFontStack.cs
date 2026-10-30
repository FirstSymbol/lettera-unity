using System;
using System.Collections.Generic;
using UnityEngine;

namespace FirstSymbol.Lettera
{
    /// <summary>One family of a stack: a named set of faces.</summary>
    [Serializable]
    public class LetteraFontFamily
    {
        [Tooltip("The name <font=Name> selects. Empty uses the primary face's family name.")]
        [SerializeField] string m_Name = "";
        [SerializeField] LetteraFontAsset m_Primary;
        [Tooltip("Other weights and italics of this family.")]
        [SerializeField] List<LetteraFontAsset> m_Faces = new List<LetteraFontAsset>();
        [Tooltip("BCP 47 language this family is tried first for, e.g. ja or zh-Hans.")]
        [SerializeField] string m_PreferredLanguage = "";

        public string Name { get => string.IsNullOrEmpty(m_Name) && m_Primary != null ? m_Primary.FamilyName : m_Name; set => m_Name = value; }
        public LetteraFontAsset Primary { get => m_Primary; set => m_Primary = value; }
        public List<LetteraFontAsset> Faces => m_Faces;
        public string PreferredLanguage { get => m_PreferredLanguage; set => m_PreferredLanguage = value; }
    }

    /// <summary>Ordered font families and an optional chained stack; codepoints fall through them in order.</summary>
    [CreateAssetMenu(menuName = "Lettera/Font Stack", fileName = "New Lettera Font Stack", order = 103)]
    public class LetteraFontStack : ScriptableObject
    {
        public static event Action<LetteraFontStack> StackChanged;

        [SerializeField] List<LetteraFontFamily> m_Families = new List<LetteraFontFamily>();
        [SerializeField] LetteraFontStack m_FallbackStack;

        public List<LetteraFontFamily> Families => m_Families;
        public LetteraFontStack FallbackStack { get => m_FallbackStack; set { m_FallbackStack = value; NotifyChanged(); } }

        public void NotifyChanged() => StackChanged?.Invoke(this);

        void OnValidate() => NotifyChanged();
    }
}

using System;
using UnityEngine;

namespace FirstSymbol.Lettera
{
    public enum LetteraSizeMatchMetric { XHeight, CapHeight }

    /// <summary>Project-wide defaults. Lives at Resources/LetteraSettings in the project, so it survives a package update.</summary>
    public class LetteraSettings : ScriptableObject
    {
        public const string ResourceName = "LetteraSettings";

        /// <summary>Raised when the settings change; every text rebuilds.</summary>
        public static event Action Changed;

        [Tooltip("Primary font for texts with no font of their own. Empty uses the OS default face.")]
        [SerializeField] LetteraFontAsset m_DefaultFont;
        [Tooltip("Do not fall back to OS fonts for codepoints the assigned fonts lack.")]
        [SerializeField] bool m_DisableSystemFontFallback;
        [Tooltip("Do not route emoji through color fonts.")]
        [SerializeField] bool m_DisableEmoji;
        [Tooltip("Styles every text gets last. Empty uses the built-in tag vocabulary.")]
        [SerializeField] LetteraStylePreset m_GlobalStylePreset;
        [SerializeField] LetteraLineHeightMode m_DefaultLineHeightMode = LetteraLineHeightMode.Scaled;
        [SerializeField] float m_DefaultLineHeightValue = 1.4f;
        [Tooltip("The metric fallback faces are matched on.")]
        [SerializeField] LetteraSizeMatchMetric m_FontSizeMatchMetric;
        [Tooltip("Used on WebGL when a text has no font, because browsers expose no OS fonts.")]
        [SerializeField] LetteraFontAsset m_WebGLFallbackFont;
        [Tooltip("The paints effects find by name, by default.")]
        [SerializeField] LetteraPaintLibrary m_Paints;
        [SerializeField] GameObject m_TextPrefab;
        [SerializeField] GameObject m_WorldTextPrefab;
        [SerializeField] LetteraEditingSettings m_Editing = new LetteraEditingSettings();
        [SerializeField] LetteraMotionSettings m_Motion = new LetteraMotionSettings();
        [SerializeField] LetteraDocumentSettings m_Documents = new LetteraDocumentSettings();
        [Tooltip("Word lists for scripts written without spaces. Empty costs nothing: only what is listed reaches a player.")]
        [SerializeField] System.Collections.Generic.List<LetteraWordDictionary> m_Dictionaries = new System.Collections.Generic.List<LetteraWordDictionary>();

        static LetteraSettings s_Instance;

        public static LetteraSettings Instance
        {
            get
            {
                if (s_Instance != null) return s_Instance;
                s_Instance = Resources.Load<LetteraSettings>(ResourceName);
                if (s_Instance == null)
                {
                    s_Instance = CreateInstance<LetteraSettings>();
                    s_Instance.hideFlags = HideFlags.HideAndDontSave;
                }
                return s_Instance;
            }
        }

        public LetteraFontAsset DefaultFont { get => m_DefaultFont; set { m_DefaultFont = value; Changed?.Invoke(); } }
        public bool DisableSystemFontFallback { get => m_DisableSystemFontFallback; set { m_DisableSystemFontFallback = value; Changed?.Invoke(); } }
        public bool DisableEmoji { get => m_DisableEmoji; set { m_DisableEmoji = value; Changed?.Invoke(); } }
        public LetteraStylePreset GlobalStylePreset { get => m_GlobalStylePreset; set { m_GlobalStylePreset = value; Changed?.Invoke(); } }
        public LetteraLineHeightMode DefaultLineHeightMode { get => m_DefaultLineHeightMode; set { m_DefaultLineHeightMode = value; Changed?.Invoke(); } }
        public float DefaultLineHeightValue { get => m_DefaultLineHeightValue; set { m_DefaultLineHeightValue = value; Changed?.Invoke(); } }
        public LetteraSizeMatchMetric FontSizeMatchMetric { get => m_FontSizeMatchMetric; set { m_FontSizeMatchMetric = value; Changed?.Invoke(); } }
        public LetteraFontAsset WebGLFallbackFont { get => m_WebGLFallbackFont; set { m_WebGLFallbackFont = value; Changed?.Invoke(); } }
        public LetteraPaintLibrary Paints { get => m_Paints; set { m_Paints = value; Changed?.Invoke(); } }
        public GameObject TextPrefab { get => m_TextPrefab; set => m_TextPrefab = value; }
        public GameObject WorldTextPrefab { get => m_WorldTextPrefab; set => m_WorldTextPrefab = value; }

        /// <summary>Selection and editing defaults.</summary>
        public LetteraEditingSettings Editing => m_Editing ?? (m_Editing = new LetteraEditingSettings());

        /// <summary>Animation and interaction defaults.</summary>
        public LetteraMotionSettings Motion => m_Motion ?? (m_Motion = new LetteraMotionSettings());

        /// <summary>Limits of documents' windows.</summary>
        public LetteraDocumentSettings Documents => m_Documents ?? (m_Documents = new LetteraDocumentSettings());

        public System.Collections.Generic.List<LetteraWordDictionary> Dictionaries => m_Dictionaries ?? (m_Dictionaries = new System.Collections.Generic.List<LetteraWordDictionary>());

        internal static void RaiseChanged() => Changed?.Invoke();

        void OnValidate() => Changed?.Invoke();

        void OnEnable()
        {
            if (hideFlags == HideFlags.None) s_Instance = this;
        }
    }
}

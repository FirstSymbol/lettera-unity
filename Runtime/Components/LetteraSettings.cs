using System;
using UnityEngine;

namespace FirstSymbol.Lettera
{
    public enum LetteraSizeMatchMetric { XHeight, CapHeight }

    /// <summary>The project's defaults for solid text. Numbers and a reference only, so nothing of the module is needed to read them.</summary>
    [System.Serializable]
    public class LetteraSolidSettings
    {
        [Tooltip("The material of a Text 3D made from a script, with none of its own. Empty keeps the shader out of builds that do not reference it.")]
        public Material DefaultMaterial;

        [Tooltip("The most glyph solids kept, in bytes. What no text holds goes first.")]
        public long CacheBytes =
#if UNITY_WEBGL
            16 * 1024 * 1024;
#else
            32 * 1024 * 1024;
#endif

        [Tooltip("The most vertices one text draws. Glyphs past it draw nothing, and the inspector says so.")]
        public int MaxVertices = 1000000;
    }

    /// <summary>A font the project offers to lookups by family name, under a name of its own or the one its file states.</summary>
    [System.Serializable]
    public class LetteraProjectFont
    {
        [Tooltip("The family name a lookup finds it by, such as the one a web page's font-family names. Empty reads the family from the font, which loads it with the first lookup.")]
        public string Family = "";

        [Tooltip("A face of that family. Give each weight and slope its own entry with the same family name.")]
        public LetteraFontAsset Font;
    }

    /// <summary>Project-wide defaults. Lives at Resources/LetteraSettings in the project, so it survives a package update.</summary>
    public partial class LetteraSettings : ScriptableObject
    {
        [SerializeField, HideInInspector] internal int LetteraSerializedVersion;

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
        [Tooltip("Fonts found first when a family name is looked up (see LetteraFontResolver), before the system's.")]
        [SerializeField] System.Collections.Generic.List<LetteraProjectFont> m_ProjectFonts = new System.Collections.Generic.List<LetteraProjectFont>();
        [Tooltip("Font stacks whose families a lookup by family name finds first, before the system's.")]
        [SerializeField] System.Collections.Generic.List<LetteraFontStack> m_ProjectFontStacks = new System.Collections.Generic.List<LetteraFontStack>();
        [Tooltip("The paints effects find by name, by default.")]
        [SerializeField] LetteraPaintLibrary m_Paints;
        [SerializeField] GameObject m_TextPrefab;
        [SerializeField] GameObject m_WorldTextPrefab;
        [SerializeField] GameObject m_Text3DPrefab;
        [SerializeField] GameObject m_ButtonPrefab;
        [SerializeField] GameObject m_DocumentPrefab;
        [SerializeField] LetteraEditingSettings m_Editing = new LetteraEditingSettings();
        [SerializeField] LetteraMotionSettings m_Motion = new LetteraMotionSettings();
        [SerializeField] LetteraDocumentSettings m_Documents = new LetteraDocumentSettings();
        [SerializeField] LetteraPlatformSettings m_Platform = new LetteraPlatformSettings();
        [SerializeField] LetteraFontMemorySettings m_FontMemory = new LetteraFontMemorySettings();
        [SerializeField] LetteraMemorySettings m_Memory = new LetteraMemorySettings();
        [SerializeField] LetteraAtlasSettings m_Atlas = new LetteraAtlasSettings();
        [SerializeField] LetteraMathSettings m_Math = new LetteraMathSettings();
        [SerializeField] LetteraSolidSettings m_Solid3D = new LetteraSolidSettings();
        [Tooltip("Word lists for scripts written without spaces. Empty costs nothing: only what is listed reaches a player.")]
        [SerializeField] System.Collections.Generic.List<LetteraWordDictionary> m_Dictionaries = new System.Collections.Generic.List<LetteraWordDictionary>();

        static LetteraSettings s_Instance;
        static ResourceRequest s_Ahead;

        // A player reads the settings with its first text. Asked for as it starts, they load on Unity's loading thread meanwhile, and the
        // first text finds them there rather than reading them on the main thread.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void LoadAhead()
        {
            if (Application.isEditor || s_Instance != null) return;
            s_Ahead = Resources.LoadAsync<LetteraSettings>(ResourceName);
            LetteraMaterials.LoadAhead();
        }

        /// <summary>True when reading <see cref="Instance"/> would not wait for the load begun at start.</summary>
        internal static bool LoadedAhead => s_Instance != null || s_Ahead == null || s_Ahead.isDone;

        public static LetteraSettings Instance
        {
            get
            {
                if (s_Instance != null) return s_Instance;
                // Reading the asset of a request still loading waits for it, as a load would.
                if (s_Ahead != null) s_Instance = s_Ahead.asset as LetteraSettings;
                s_Ahead = null;
                if (s_Instance == null) s_Instance = Resources.Load<LetteraSettings>(ResourceName);
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
        /// <summary>Fonts a lookup by family name finds first. After changing the list, call <see cref="LetteraFontResolver.ClearCache"/>.</summary>
        public System.Collections.Generic.List<LetteraProjectFont> ProjectFonts => m_ProjectFonts ?? (m_ProjectFonts = new System.Collections.Generic.List<LetteraProjectFont>());

        /// <summary>Font stacks a lookup by family name finds first. After changing the list, call <see cref="LetteraFontResolver.ClearCache"/>.</summary>
        public System.Collections.Generic.List<LetteraFontStack> ProjectFontStacks => m_ProjectFontStacks ?? (m_ProjectFontStacks = new System.Collections.Generic.List<LetteraFontStack>());

        public LetteraPaintLibrary Paints { get => m_Paints; set { m_Paints = value; Changed?.Invoke(); } }
        public GameObject TextPrefab { get => m_TextPrefab; set => m_TextPrefab = value; }
        public GameObject WorldTextPrefab { get => m_WorldTextPrefab; set => m_WorldTextPrefab = value; }
        public GameObject Text3DPrefab { get => m_Text3DPrefab; set => m_Text3DPrefab = value; }
        public GameObject ButtonPrefab { get => m_ButtonPrefab; set => m_ButtonPrefab = value; }
        public GameObject DocumentPrefab { get => m_DocumentPrefab; set => m_DocumentPrefab = value; }

#if UNITY_EDITOR
        /// <summary>Set by the editor: copies a default prefab's fields onto a component just added or reset.</summary>
        internal static System.Action<Component> SeedFromDefault;
#endif

        /// <summary>Selection and editing defaults.</summary>
        public LetteraEditingSettings Editing => m_Editing ?? (m_Editing = new LetteraEditingSettings());

        /// <summary>Animation and interaction defaults.</summary>
        public LetteraMotionSettings Motion => m_Motion ?? (m_Motion = new LetteraMotionSettings());

        /// <summary>Limits of documents' windows.</summary>
        public LetteraDocumentSettings Documents => m_Documents ?? (m_Documents = new LetteraDocumentSettings());

        public System.Collections.Generic.List<LetteraWordDictionary> Dictionaries => m_Dictionaries ?? (m_Dictionaries = new System.Collections.Generic.List<LetteraWordDictionary>());

        /// <summary>Which of a platform's own facilities a project wants.</summary>
        /// <summary>Defaults for formulas.</summary>
        public LetteraMathSettings Math => m_Math ?? (m_Math = new LetteraMathSettings());
        public LetteraSolidSettings Solid3D => m_Solid3D ?? (m_Solid3D = new LetteraSolidSettings());

        /// <summary>What fonts may cost, and how a build carries them.</summary>
        public LetteraFontMemorySettings FontMemory => m_FontMemory ?? (m_FontMemory = new LetteraFontMemorySettings());

        /// <summary>When Lettera gives back what it keeps for speed. See <see cref="LetteraMemory"/>.</summary>
        public LetteraMemorySettings Memory => m_Memory ?? (m_Memory = new LetteraMemorySettings());

        /// <summary>The size of the glyph atlas's pages. See <see cref="LetteraAtlasSettings"/>.</summary>
        public LetteraAtlasSettings Atlas => m_Atlas ?? (m_Atlas = new LetteraAtlasSettings());

        public LetteraPlatformSettings Platform => m_Platform ?? (m_Platform = new LetteraPlatformSettings());

        internal static void RaiseChanged() => Changed?.Invoke();

        void OnValidate() => Changed?.Invoke();

        void OnEnable()
        {
            if (hideFlags == HideFlags.None) s_Instance = this;
        }
    }
}

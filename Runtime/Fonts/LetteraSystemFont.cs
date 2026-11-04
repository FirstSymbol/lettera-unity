using System;
using System.Collections.Generic;
using UnityEngine;

namespace FirstSymbol.Lettera
{
    /// <summary>What a system font asset changes on one platform. Each part applies only when turned on there.</summary>
    [Serializable]
    public class LetteraSystemFontPlatform
    {
        [Tooltip("The CSS weight, 100 to 900, a text with no weight of its own draws at on this platform: the nearest installed cut, or that point of a variable font. 0 takes the asset's own.")]
        [Range(0, 900)] public int Weight;

        [Tooltip("Writes this platform's vertical lines, for a system face whose own are wrong here.")]
        public bool OverrideMetrics;
        public int Ascender, Descender, LineGap;

        [Tooltip("Sets this platform's raster settings, for a face drawn in pixels on one system only.")]
        public bool OverrideRaster;
        [Min(0)] public int PixelsPerEm;
        public bool SnapToPixels = true;
    }

    /// <summary>Names an OS font per platform, with a weight and overrides per platform. Nothing is loaded until a text uses it.</summary>
    [CreateAssetMenu(menuName = "Lettera/System Font Asset", fileName = "New Lettera System Font", order = 101)]
    public partial class LetteraSystemFont : LetteraFontAsset
    {
        [Tooltip("Family or file names tried on every platform, after the platform's own list.")]
        [SerializeField] string[] m_Common = new string[0];
        [SerializeField] string[] m_Windows = new string[0];
        [SerializeField] string[] m_MacOS = new string[0];
        [SerializeField] string[] m_Linux = new string[0];
        [SerializeField] string[] m_IOS = new string[0];
        [SerializeField] string[] m_Android = new string[0];

        [Tooltip("The CSS weight, 100 to 900, a text with no weight of its own draws at: the nearest installed cut, or that point of a variable font. Faces the system falls back to for letters this one lacks draw at it too. 0 draws the face as it is.")]
        [SerializeField, Range(0, 900)] int m_Weight;
        [SerializeField] LetteraSystemFontPlatform m_WindowsSettings = new LetteraSystemFontPlatform();
        [SerializeField] LetteraSystemFontPlatform m_MacOSSettings = new LetteraSystemFontPlatform();
        [SerializeField] LetteraSystemFontPlatform m_LinuxSettings = new LetteraSystemFontPlatform();
        [SerializeField] LetteraSystemFontPlatform m_IOSSettings = new LetteraSystemFontPlatform();
        [SerializeField] LetteraSystemFontPlatform m_AndroidSettings = new LetteraSystemFontPlatform();

        [NonSerialized] FontFace m_Face;
        [NonSerialized] bool m_OwnsFace;
        [NonSerialized] List<FontFace> m_Cuts;
        [NonSerialized] bool m_Resolved;

        /// <summary>The name that resolved, or null before first use or when none is installed.</summary>
        public string ResolvedFamily { get; private set; }

        /// <summary>The file the face was opened from, or null before first use.</summary>
        public string ResolvedPath { get; private set; }
        public bool ResolveFailed { get; private set; }

        public string[] Common { get => m_Common; set { m_Common = value; Changed(); } }
        public string[] Windows { get => m_Windows; set { m_Windows = value; Changed(); } }
        public string[] MacOS { get => m_MacOS; set { m_MacOS = value; Changed(); } }
        public string[] Linux { get => m_Linux; set { m_Linux = value; Changed(); } }
        public string[] IOS { get => m_IOS; set { m_IOS = value; Changed(); } }
        public string[] Android { get => m_Android; set { m_Android = value; Changed(); } }

        /// <summary>The CSS weight a text with no weight of its own draws at, where the platform sets none; 0 draws the face as it is.</summary>
        public int Weight { get => m_Weight; set { m_Weight = Mathf.Clamp(value, 0, 900); Changed(); } }

        public LetteraSystemFontPlatform WindowsSettings => m_WindowsSettings;
        public LetteraSystemFontPlatform MacOSSettings => m_MacOSSettings;
        public LetteraSystemFontPlatform LinuxSettings => m_LinuxSettings;
        public LetteraSystemFontPlatform IOSSettings => m_IOSSettings;
        public LetteraSystemFontPlatform AndroidSettings => m_AndroidSettings;

        /// <summary>Applies a change made to one of the platform settings: texts using this font lay out again.</summary>
        public void PlatformSettingsChanged() => Changed();

        string[] PlatformList()
        {
            switch (Application.platform)
            {
                case RuntimePlatform.WindowsEditor:
                case RuntimePlatform.WindowsPlayer: return m_Windows;
                case RuntimePlatform.OSXEditor:
                case RuntimePlatform.OSXPlayer: return m_MacOS;
                case RuntimePlatform.LinuxEditor:
                case RuntimePlatform.LinuxPlayer: return m_Linux;
                case RuntimePlatform.IPhonePlayer:
                case RuntimePlatform.tvOS: return m_IOS;
                case RuntimePlatform.Android: return m_Android;
                default: return null;
            }
        }

        /// <summary>The settings of the platform this runs on, or null on one with none.</summary>
        internal LetteraSystemFontPlatform PlatformSettings()
        {
            switch (Application.platform)
            {
                case RuntimePlatform.WindowsEditor:
                case RuntimePlatform.WindowsPlayer: return m_WindowsSettings;
                case RuntimePlatform.OSXEditor:
                case RuntimePlatform.OSXPlayer: return m_MacOSSettings;
                case RuntimePlatform.LinuxEditor:
                case RuntimePlatform.LinuxPlayer: return m_LinuxSettings;
                case RuntimePlatform.IPhonePlayer:
                case RuntimePlatform.tvOS: return m_IOSSettings;
                case RuntimePlatform.Android: return m_AndroidSettings;
                default: return null;
            }
        }

        internal override int DefaultWeight
        {
            get
            {
                LetteraSystemFontPlatform here = PlatformSettings();
                return here != null && here.Weight > 0 ? here.Weight : m_Weight;
            }
        }

        internal override bool MetricsHere(out int ascender, out int descender, out int lineGap)
        {
            LetteraSystemFontPlatform here = PlatformSettings();
            if (here == null || !here.OverrideMetrics) return base.MetricsHere(out ascender, out descender, out lineGap);
            ascender = here.Ascender;
            descender = here.Descender;
            lineGap = here.LineGap;
            return true;
        }

        internal override void RasterHere(out int pixelsPerEm, out bool snap)
        {
            LetteraSystemFontPlatform here = PlatformSettings();
            if (here == null || !here.OverrideRaster)
            {
                base.RasterHere(out pixelsPerEm, out snap);
                return;
            }
            pixelsPerEm = here.PixelsPerEm;
            snap = here.SnapToPixels;
        }

        internal override FontFace LoadedFace => m_Face;

        // The asset opens the file again for itself, so what it writes on its face reaches no other text that uses the system's copy.
        internal override FontFace LoadFace()
        {
            if (m_Face != null && m_Face.Face != IntPtr.Zero) return m_Face;
            // Faces let go of on a domain reload or by a reset of the system's fonts are looked up again.
            if (m_Face != null) Release();
            if (m_Resolved) return null;
            m_Resolved = true;
            FontFace shared = null;
            foreach (string[] list in new[] { PlatformList(), m_Common })
            {
                if (list == null || shared != null) continue;
                foreach (string name in list)
                {
                    shared = LetteraSystemFonts.FindNamed(name);
                    if (shared == null) continue;
                    ResolvedFamily = name;
                    break;
                }
            }
            ResolveFailed = shared == null;
            if (shared == null)
            {
                // Never "the first file in the fonts folder": the OS default face.
                FontFace[] defaults = LetteraSystemFonts.DefaultFaces();
                shared = defaults.Length > 0 ? defaults[0] : null;
            }
            if (shared == null) return null;
            m_Face = Own(shared.FilePath, shared.FileIndex);
            m_OwnsFace = m_Face != null;
            if (m_Face == null) m_Face = shared;
            ResolvedPath = shared.FilePath;
            PushPixelSettings();
            return m_Face;
        }

        static FontFace Own(string path, int index)
        {
            if (path == null) return null;
            try { return FontFace.FromFile(path, index); }
            catch (Exception) { return null; } // The system's copy serves instead.
        }

        internal override void AddCuts(List<FontFace> faces)
        {
            if (LoadFace() == null || !m_OwnsFace) return;
            if (m_Cuts == null)
            {
                m_Cuts = new List<FontFace>();
                foreach ((string path, int index) in LetteraSystemFonts.CutsOf(m_Face))
                {
                    FontFace cut = Own(path, index);
                    if (cut == null) continue;
                    PushTo(cut);
                    m_Cuts.Add(cut);
                }
            }
            faces.AddRange(m_Cuts);
        }

        protected override void PushToCuts()
        {
            if (m_Cuts == null) return;
            foreach (FontFace cut in m_Cuts) PushTo(cut);
        }

        void Changed()
        {
            Release();
            NotifyChanged();
        }

        // Names or platform settings changed: the next text looks the face up again.
        void Release()
        {
            if (m_OwnsFace) m_Face?.Dispose();
            if (m_Cuts != null)
                foreach (FontFace cut in m_Cuts)
                    cut.Dispose();
            m_Face = null;
            m_OwnsFace = false;
            m_Cuts = null;
            m_Resolved = false;
            ResolvedFamily = null;
            ResolvedPath = null;
            ResolveFailed = false;
        }

        protected override void OnValidate()
        {
            Release();
            base.OnValidate();
        }

        void OnDestroy() => Release();
    }
}

using UnityEngine;

namespace FirstSymbol.Lettera
{
    /// <summary>Names an OS font per platform. Nothing is loaded until a text uses it.</summary>
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

        /// <summary>The name that resolved, or null before first use or when none is installed.</summary>
        public string ResolvedFamily { get; private set; }
        public bool ResolveFailed { get; private set; }

        public string[] Common { get => m_Common; set { m_Common = value; Changed(); } }
        public string[] Windows { get => m_Windows; set { m_Windows = value; Changed(); } }
        public string[] MacOS { get => m_MacOS; set { m_MacOS = value; Changed(); } }
        public string[] Linux { get => m_Linux; set { m_Linux = value; Changed(); } }
        public string[] IOS { get => m_IOS; set { m_IOS = value; Changed(); } }
        public string[] Android { get => m_Android; set { m_Android = value; Changed(); } }

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

        internal override FontFace LoadFace()
        {
            foreach (string[] list in new[] { PlatformList(), m_Common })
            {
                if (list == null) continue;
                foreach (string name in list)
                {
                    FontFace face = LetteraSystemFonts.FindNamed(name);
                    if (face == null) continue;
                    ResolvedFamily = name;
                    ResolveFailed = false;
                    return face;
                }
            }
            ResolvedFamily = null;
            ResolveFailed = true;
            // Never "the first file in the fonts folder": the OS default face.
            FontFace[] defaults = LetteraSystemFonts.DefaultFaces();
            return defaults.Length > 0 ? defaults[0] : null;
        }

        void Changed()
        {
            ResolvedFamily = null;
            ResolveFailed = false;
            NotifyChanged();
        }
    }
}

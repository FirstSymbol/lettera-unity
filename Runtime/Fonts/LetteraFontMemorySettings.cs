using System;
using UnityEngine;

namespace FirstSymbol.Lettera
{
    /// <summary>What fonts may cost, and how a build carries them.</summary>
    [Serializable]
    public class LetteraFontMemorySettings
    {
        [Tooltip("The folder of unpacked fonts is kept within this many bytes, oldest first.")]
        public long FontCacheBytes = 256L * 1024 * 1024;

        [Tooltip("Above this many variation and synthesis instances of one face, a warning shows once. Nothing is evicted.")]
        public int MaxInstancesPerFace = 256;

        public LetteraFontPackaging Desktop, IOS, Android, Web;

#if UNITY_EDITOR
        /// <summary>What a build for a target does, after Auto. Only desktops pack by default: a phone has not been seen to run this yet, and a failure there would mean no text.</summary>
        public LetteraFontPackaging Packaging(UnityEditor.BuildTarget target)
        {
            switch (target)
            {
                case UnityEditor.BuildTarget.StandaloneWindows:
                case UnityEditor.BuildTarget.StandaloneWindows64:
                case UnityEditor.BuildTarget.StandaloneLinux64:
                case UnityEditor.BuildTarget.StandaloneOSX:
                    return Desktop == LetteraFontPackaging.Auto ? LetteraFontPackaging.Packed : Desktop;
                case UnityEditor.BuildTarget.iOS:
                    return IOS == LetteraFontPackaging.Auto ? LetteraFontPackaging.Inline : IOS;
                case UnityEditor.BuildTarget.Android:
                    return Android == LetteraFontPackaging.Auto ? LetteraFontPackaging.Inline : Android;
                case UnityEditor.BuildTarget.WebGL:
                    // A browser never maps a file, and packing there would only add a download.
                    return LetteraFontPackaging.Inline;
                default:
                    return LetteraFontPackaging.Inline;
            }
        }
#endif
    }
}

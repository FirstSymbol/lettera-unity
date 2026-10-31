using System;

namespace FirstSymbol.Lettera
{
    /// <summary>Which of a platform's text controls a field gets, when any. Null means the field keeps what it had before there were any.</summary>
    internal static class LetteraNativeHosts
    {
        /// <summary>Replaces the host for tests.</summary>
        internal static Func<ILetteraNativeHost> HostOverride;

        internal static ILetteraInputSource Create()
        {
            if (!LetteraPlatform.IsActive(LetteraNativeFeature.TextInput)) return null;
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
            if (HostOverride == null) return new LetteraImmInputSource(new LetteraUnityInputSource(), new LetteraWindowsImm());
#endif
            ILetteraNativeHost host = HostOverride?.Invoke() ?? PlatformHost();
            if (host == null || !host.IsAvailable) return null;
            return new LetteraMirrorSource(host);
        }

        static ILetteraNativeHost PlatformHost()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            return new LetteraWebHost();
#elif UNITY_ANDROID && !UNITY_EDITOR
            return new LetteraAndroidHost();
#elif UNITY_IOS && !UNITY_EDITOR
            return new LetteraIosHost();
#else
            return null;
#endif
        }
    }
}

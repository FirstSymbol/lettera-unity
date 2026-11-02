using System;
using UnityEngine;

namespace FirstSymbol.Lettera
{
    /// <summary>When Lettera gives back what it keeps only for speed.</summary>
    [Serializable]
    public class LetteraMemorySettings
    {
        [Tooltip("After this many seconds in which no text was rebuilt, what Lettera keeps for speed goes back: the paragraph cache, the atlas's upload textures and work buffers, the world batcher's merge buffers, and with no text left the atlas pages. 0 keeps everything.")]
        [Min(0)] public float IdleReleaseSeconds = 10;

        [Tooltip("The same release when the system says memory is low, whatever the time since the last rebuild.")]
        public bool ReleaseOnLowMemory = true;
    }

    /// <summary>
    /// Gives back what Lettera keeps only for speed. Texts keep what they show and draw and edit as before; the next one that needs a
    /// paragraph, a glyph or a buffer makes it again. Runs by itself after <see cref="LetteraMemorySettings.IdleReleaseSeconds"/> without
    /// a rebuild and on <see cref="Application.lowMemory"/>.
    /// </summary>
    public static class LetteraMemory
    {
        static int s_SeenWork = -1;
        static float s_LastWork;
        static bool s_Idle;
        static bool s_Installed;

        /// <summary>Raised after each release, with true when it was asked by the system's low-memory warning.</summary>
        public static event Action<bool> Released;

        /// <summary>How many releases ran in this session.</summary>
        public static int ReleaseCount { get; private set; }

        /// <summary>
        /// Gives back the paragraph cache, the atlas's upload textures and work buffers and the world batcher's merge buffers.
        /// With no Lettera text left in any scene, the atlas pages go too.
        /// </summary>
        public static void Release() => Release(false);

        static void Release(bool lowMemory)
        {
            TextEngine.ClearParagraphCache();
            bool noText = LetteraTextListeners.Count == 0;
            GlyphAtlas.Release(noText);
            if (noText) LetteraWorldBatcher.ReleaseScratch();
            ReleaseCount++;
            Released?.Invoke(lowMemory);
        }

        internal static void Install()
        {
            if (s_Installed) return;
            s_Installed = true;
            Application.lowMemory += OnLowMemory;
        }

        static void OnLowMemory()
        {
            if (LetteraSettings.Instance.Memory.ReleaseOnLowMemory) Release(true);
        }

        // Once a frame in play: any rebuild or new glyph starts the quiet period over.
        internal static void Tick()
        {
            int work = Work();
            float now = Time.realtimeSinceStartup;
            if (work != s_SeenWork)
            {
                s_SeenWork = work;
                s_LastWork = now;
                s_Idle = false;
                return;
            }
            if (s_Idle) return;
            float seconds = LetteraSettings.Instance.Memory.IdleReleaseSeconds;
            if (seconds <= 0 || now - s_LastWork < seconds) return;
            s_Idle = true;
            Release(false);
            // What the release itself changed is not work.
            s_SeenWork = Work();
        }

        // A text made or destroyed counts too, so the pages go back a quiet period after the last text went.
        static int Work() => (TextEngine.WorkCounter * 31 + GlyphAtlas.GlyphCount) * 31 + LetteraTextListeners.Count;
    }
}

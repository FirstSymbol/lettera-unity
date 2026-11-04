using System;
using UnityEngine;

namespace FirstSymbol.Lettera
{
    public enum LetteraAtlasPageSize { Size2048, Size1024 }

    /// <summary>
    /// The side of the glyph atlas's pages, per kind. A page of 1024 holds a quarter of what a page of 2048 holds and costs a quarter of its
    /// memory, so a game with few glyphs, such as Latin at a few sizes, pays 1 MB a page where it paid 4; one with thousands of glyphs makes more
    /// pages. A size takes effect when that kind's pages are next made from nothing: at start, after <see cref="GlyphAtlas.Clear"/>, or after a
    /// release with no text left. A glyph tile larger than a page is not drawn, which a page of 1024 reaches only with glyphs far larger than text uses.
    /// </summary>
    [Serializable]
    public class LetteraAtlasSettings
    {
        [Tooltip("Pages of distance-field glyphs, most text: 2048 costs 4 MB a page, 1024 costs 1 MB.")]
        public LetteraAtlasPageSize SdfPageSize = LetteraAtlasPageSize.Size2048;

        [Tooltip("Pages of colour glyphs, such as emoji: 2048 costs 16 MB a page, 1024 costs 4 MB.")]
        public LetteraAtlasPageSize ColorPageSize = LetteraAtlasPageSize.Size2048;

        [Tooltip("Pages of multi-channel distance fields: 2048 costs 16 MB a page, 1024 costs 4 MB.")]
        public LetteraAtlasPageSize MsdfPageSize = LetteraAtlasPageSize.Size2048;
    }

    /// <summary>When Lettera gives back what it keeps only for speed.</summary>
    [Serializable]
    public class LetteraMemorySettings
    {
        [Tooltip("After this many seconds in which no text was rebuilt, what Lettera keeps for speed goes back: the paragraph cache, the atlas's upload textures and work buffers, the world batcher's merge buffers, and with no text left the atlas pages. 0 keeps everything.")]
        [Min(0)] public float IdleReleaseSeconds = 10;

        [Tooltip("The same release when the system says memory is low, whatever the time since the last rebuild.")]
        public bool ReleaseOnLowMemory = true;

        [Tooltip("A player readies, over its first frames, what the first text of a session would pay for once: Lettera's hooks into the frame, its Canvas material, the first glyph job, Unity's first Canvas graphic and the first draw of the text shader. About 13 ms over a player's first frames, once, a step a frame, and no memory beyond the material. Not on the web. Off leaves it to the first text.")]
        public bool ReadyAtStart = true;
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

        static void Release(bool lowMemory) => Release(lowMemory, true);

        static void Release(bool lowMemory, bool pages)
        {
            TextEngine.ClearParagraphCache();
            bool noText = LetteraTextListeners.Count == 0;
            GlyphAtlas.Release(noText && pages);
            if (noText) LetteraWorldBatcher.ReleaseScratch();
            if (noText && pages) s_TextSeen = false;
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

        // A text has been shown since the pages last went: glyphs readied with no text yet, as by a warm-up on a loading screen, stay.
        static bool s_TextSeen;

        // Once a frame in play: any rebuild or new glyph starts the quiet period over.
        internal static void Tick()
        {
#if UNITY_EDITOR
            if (!Application.isPlaying) return;
#endif
            if (LetteraTextListeners.Count > 0) s_TextSeen = true;
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
            Release(false, s_TextSeen);
            // What the release itself changed is not work.
            s_SeenWork = Work();
        }

        // A text made or destroyed counts too, so the pages go back a quiet period after the last text went.
        static int Work() => (TextEngine.WorkCounter * 31 + GlyphAtlas.GlyphCount) * 31 + LetteraTextListeners.Count;
    }
}

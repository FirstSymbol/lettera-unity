using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace FirstSymbol.Lettera
{
    /// <summary>Draws an emoji cluster to RGBA pixels. On the web this is the browser's canvas.</summary>
    internal interface IEmojiRenderer
    {
        /// <summary>The advance of a cluster in pixels.</summary>
        float Measure(string cluster, int pixels);

        /// <summary>Writes width * height RGBA pixels, top row first, with straight alpha.</summary>
        void Render(string cluster, int pixels, int width, int height, int baselineY, byte[] output);
    }

    /// <summary>
    /// Emoji on platforms where no font file can be read. Each distinct cluster becomes one glyph of a
    /// virtual face, so it flows through shaping, layout and the color atlas like any other glyph.
    /// </summary>
    internal static class BrowserEmoji
    {
        public const int TilePixels = 72;
        public const int Upem = 1000;
        public const float AscentEm = 0.95f;
        public const float DescentEm = 0.25f;

        static readonly Dictionary<string, uint> s_Ids = new Dictionary<string, uint>();
        static readonly List<string> s_Clusters = new List<string>();
        static readonly List<int> s_Advances = new List<int>();
        static FontFace s_Face;

        /// <summary>Set to draw emoji; the web player sets the canvas renderer, tests may set their own.</summary>
        public static IEmojiRenderer Renderer { get; set; } =
#if UNITY_WEBGL && !UNITY_EDITOR
            new CanvasRenderer();
#else
            null;
#endif

        /// <summary>The virtual face, or null when nothing can draw emoji here.</summary>
        public static FontFace Face
        {
            get
            {
                if (Renderer == null) return null;
                if (s_Face == null || s_Face.Regular == null) s_Face = FontFace.CreateVirtual(Upem, (int)(AscentEm * Upem), -(int)(DescentEm * Upem));
                return s_Face;
            }
        }

        /// <summary>The glyph for a cluster, measured once. Allocates only the first time a cluster appears.</summary>
        public static uint GlyphFor(ReadOnlySpan<int> cluster, out int advance)
        {
            string text = ToText(cluster);
            if (!s_Ids.TryGetValue(text, out uint id))
            {
                id = (uint)s_Clusters.Count;
                s_Ids[text] = id;
                s_Clusters.Add(text);
                s_Advances.Add((int)(Renderer.Measure(text, TilePixels) / TilePixels * Upem));
            }
            advance = s_Advances[(int)id];
            return id;
        }

        public static string ClusterText(uint glyph) => s_Clusters[(int)glyph];

        public static int Advance(uint glyph) => s_Advances[(int)glyph];

        // Clusters repeat constantly, so one buffer avoids building a key from pieces.
        static readonly System.Text.StringBuilder s_Builder = new System.Text.StringBuilder(32);

        static string ToText(ReadOnlySpan<int> cluster)
        {
            s_Builder.Clear();
            foreach (int cp in cluster)
            {
                if (cp < 0x10000) s_Builder.Append((char)cp);
                else s_Builder.Append(char.ConvertFromUtf32(cp));
            }
            return s_Builder.ToString();
        }

        public static void Reset()
        {
            s_Ids.Clear();
            s_Clusters.Clear();
            s_Advances.Clear();
            s_Face = null;
        }

#if UNITY_WEBGL && !UNITY_EDITOR
        sealed class CanvasRenderer : IEmojiRenderer
        {
            [DllImport("__Internal")] static extern float LetteraEmoji_Measure(string text, int pixels);
            [DllImport("__Internal")] static extern void LetteraEmoji_Render(string text, int pixels, int width, int height, int baselineY, byte[] output);

            public float Measure(string cluster, int pixels) => LetteraEmoji_Measure(cluster, pixels);

            public void Render(string cluster, int pixels, int width, int height, int baselineY, byte[] output) =>
                LetteraEmoji_Render(cluster, pixels, width, height, baselineY, output);
        }
#endif
    }
}

using System.Collections.Generic;
using UnityEngine;

namespace FirstSymbol.Lettera
{
    /// <summary>
    /// All gradients in one texture, one row each, bound globally so different gradients never split a batch.
    /// Identical gradients share a row.
    /// </summary>
    internal static class GradientAtlas
    {
        const int Width = 256;
#if UNITY_WEBGL && !UNITY_EDITOR
        const int Height = 256;
#else
        const int Height = 1024;
#endif

        static readonly int s_RampsId = Shader.PropertyToID("_LetteraRamps");
        static readonly Dictionary<ulong, int> s_Rows = new Dictionary<ulong, int>();
        static readonly Color32[] s_Row = new Color32[Width];
        static Texture2D s_Texture;
        static bool s_Warned;

        public static int RowCount => s_Rows.Count;

        /// <summary>The texture's v coordinate of a gradient's row.</summary>
        public static float RowOf(LetteraGradient gradient)
        {
            ulong key = Hash(gradient);
            if (!s_Rows.TryGetValue(key, out int row))
            {
                if (s_Rows.Count >= Height)
                {
                    if (!s_Warned) Debug.LogWarning($"Lettera: more than {Height} different gradients are in use; further ones share the last row.");
                    s_Warned = true;
                    return (Height - 0.5f) / Height;
                }
                row = s_Rows.Count;
                s_Rows[key] = row;
                Write(gradient, row);
            }
            return (row + 0.5f) / Height;
        }

        // Gradients with grain: a texture of their own, made only when one is used, with half floats, so a slow gradient keeps its steps between
        // the 8-bit values. It holds sRGB values; the grained shader adds the grain there, one step of the target, and only then decodes.
        const int FineHeight = 128;
        static readonly int s_FineRampsId = Shader.PropertyToID("_LetteraRampsFine");
        static readonly Dictionary<ulong, int> s_FineRows = new Dictionary<ulong, int>();
        static readonly Color[] s_FineRow = new Color[Width];
        static Texture2D s_FineTexture;
        static bool s_FineWarned;

        public static int FineRowCount => s_FineRows.Count;

        /// <summary>The fine texture's v coordinate of a gradient's row, for a gradient with grain.</summary>
        public static float FineRowOf(LetteraGradient gradient)
        {
            ulong key = Hash(gradient);
            if (!s_FineRows.TryGetValue(key, out int row))
            {
                if (s_FineRows.Count >= FineHeight)
                {
                    if (!s_FineWarned) Debug.LogWarning($"Lettera: more than {FineHeight} different gradients with grain are in use; further ones share the last row.");
                    s_FineWarned = true;
                    return (FineHeight - 0.5f) / FineHeight;
                }
                row = s_FineRows.Count;
                s_FineRows[key] = row;
                if (s_FineTexture == null)
                {
                    bool half = SystemInfo.SupportsTextureFormat(TextureFormat.RGBAHalf);
                    s_FineTexture = new Texture2D(Width, FineHeight, half ? TextureFormat.RGBAHalf : TextureFormat.RGBA32, false, true)
                    {
                        name = "Lettera Gradients With Grain",
                        hideFlags = HideFlags.HideAndDontSave,
                        filterMode = FilterMode.Bilinear,
                        wrapMode = TextureWrapMode.Clamp,
                    };
                    Shader.SetGlobalTexture(s_FineRampsId, s_FineTexture);
                }
                BakeExact(gradient, s_FineRow);
                s_FineTexture.SetPixels(0, row, Width, 1, s_FineRow);
                s_FineTexture.Apply(false, false);
            }
            return (row + 0.5f) / FineHeight;
        }

        /// <summary>The fine texture's row as stored, for a test.</summary>
        internal static Color[] FineRowPixels(float v)
        {
            if (s_FineTexture == null) return null;
            return s_FineTexture.GetPixels(0, Mathf.FloorToInt(v * FineHeight), Width, 1);
        }

        static void EnsureTexture()
        {
            if (s_Texture != null) return;
            s_Texture = new Texture2D(Width, Height, TextureFormat.RGBA32, false, false)
            {
                name = "Lettera Gradients",
                hideFlags = HideFlags.HideAndDontSave,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
            };
            Shader.SetGlobalTexture(s_RampsId, s_Texture);
        }

        static void Write(LetteraGradient gradient, int row)
        {
            EnsureTexture();
            Bake(gradient, s_Row);
            s_Texture.SetPixels32(0, row, Width, 1, s_Row);
            s_Texture.Apply(false, false);
        }

        /// <summary>Evaluates a gradient at evenly spaced positions.</summary>
        internal static void Bake(LetteraGradient gradient, Color32[] target)
        {
            List<LetteraGradientStop> stops = gradient.Stops;
            for (int x = 0; x < target.Length; x++)
            {
                float t = (float)x / (target.Length - 1);
                int upper = 0;
                while (upper < stops.Count && stops[upper].Position <= t) upper++;
                if (upper == 0) target[x] = stops[0].Color;
                else if (upper == stops.Count) target[x] = stops[stops.Count - 1].Color;
                else if (gradient.Mode == LetteraGradientMode.Stepped) target[x] = stops[upper - 1].Color;
                else
                {
                    LetteraGradientStop a = stops[upper - 1], b = stops[upper];
                    float span = b.Position - a.Position;
                    target[x] = Mix(a.Color, b.Color, span > 0 ? (t - a.Position) / span : 1, gradient.Mode);
                }
            }
        }

        /// <summary>As <see cref="Bake"/>, unrounded: sRGB values and alpha from 0 to 1.</summary>
        internal static void BakeExact(LetteraGradient gradient, Color[] target)
        {
            List<LetteraGradientStop> stops = gradient.Stops;
            for (int x = 0; x < target.Length; x++)
            {
                float t = (float)x / (target.Length - 1);
                int upper = 0;
                while (upper < stops.Count && stops[upper].Position <= t) upper++;
                if (upper == 0) target[x] = stops[0].Color;
                else if (upper == stops.Count) target[x] = stops[stops.Count - 1].Color;
                else if (gradient.Mode == LetteraGradientMode.Stepped) target[x] = stops[upper - 1].Color;
                else
                {
                    LetteraGradientStop a = stops[upper - 1], b = stops[upper];
                    float span = b.Position - a.Position;
                    Color mixed = MixExact(a.Color, b.Color, span > 0 ? (t - a.Position) / span : 1, gradient.Mode);
                    target[x] = new Color(Mathf.Clamp01(mixed.r), Mathf.Clamp01(mixed.g), Mathf.Clamp01(mixed.b), Mathf.Clamp01(mixed.a));
                }
            }
        }

        static Color32 Mix(Color32 a, Color32 b, float t, LetteraGradientMode mode)
        {
            Color mixed = MixExact(a, b, t, mode);
            return new Color32(ToByte(mixed.r), ToByte(mixed.g), ToByte(mixed.b), ToByte(mixed.a));
        }

        // Colors mix premultiplied, so a fade to transparent does not darken through the transparent color.
        static Color MixExact(Color32 a, Color32 b, float t, LetteraGradientMode mode)
        {
            float alphaA = a.a / 255f, alphaB = b.a / 255f;
            float alpha = alphaA + (alphaB - alphaA) * t;
            Vector3 ca = Channels(a, mode) * alphaA, cb = Channels(b, mode) * alphaB;
            Vector3 mixed = ca + (cb - ca) * t;
            if (alpha > 0) mixed /= alpha;
            Vector3 srgb = FromChannels(mixed, mode);
            return new Color(srgb.x, srgb.y, srgb.z, alpha);
        }

        static byte ToByte(float value) => (byte)(Mathf.Clamp01(value) * 255f + 0.5f);

        static Vector3 Channels(Color32 color, LetteraGradientMode mode)
        {
            var srgb = new Vector3(color.r / 255f, color.g / 255f, color.b / 255f);
            if (mode == LetteraGradientMode.Srgb) return srgb;
            var linear = new Vector3(ToLinear(srgb.x), ToLinear(srgb.y), ToLinear(srgb.z));
            if (mode == LetteraGradientMode.Linear) return linear;
            // Oklab, from Bjorn Ottosson's published matrices.
            float l = 0.4122214708f * linear.x + 0.5363325363f * linear.y + 0.0514459929f * linear.z;
            float m = 0.2119034982f * linear.x + 0.6806995451f * linear.y + 0.1073969566f * linear.z;
            float s = 0.0883024619f * linear.x + 0.2817188376f * linear.y + 0.6299787005f * linear.z;
            float cl = Cbrt(l), cm = Cbrt(m), cs = Cbrt(s);
            return new Vector3(0.2104542553f * cl + 0.7936177850f * cm - 0.0040720468f * cs,
                1.9779984951f * cl - 2.4285922050f * cm + 0.4505937099f * cs,
                0.0259040371f * cl + 0.7827717662f * cm - 0.8086757660f * cs);
        }

        static Vector3 FromChannels(Vector3 value, LetteraGradientMode mode)
        {
            if (mode == LetteraGradientMode.Srgb) return value;
            Vector3 linear = value;
            if (mode != LetteraGradientMode.Linear)
            {
                float cl = value.x + 0.3963377774f * value.y + 0.2158037573f * value.z;
                float cm = value.x - 0.1055613458f * value.y - 0.0638541728f * value.z;
                float cs = value.x - 0.0894841775f * value.y - 1.2914855480f * value.z;
                float l = cl * cl * cl, m = cm * cm * cm, s = cs * cs * cs;
                linear = new Vector3(4.0767416621f * l - 3.3077115913f * m + 0.2309699292f * s,
                    -1.2684380046f * l + 2.6097574011f * m - 0.3413193965f * s,
                    -0.0041960863f * l - 0.7034186147f * m + 1.7076147010f * s);
            }
            return new Vector3(ToSrgb(linear.x), ToSrgb(linear.y), ToSrgb(linear.z));
        }

        static float Cbrt(float value) => Mathf.Sign(value) * Mathf.Pow(Mathf.Abs(value), 1f / 3f);
        static float ToLinear(float c) => c <= 0.04045f ? c / 12.92f : Mathf.Pow((c + 0.055f) / 1.055f, 2.4f);
        static float ToSrgb(float c) => c <= 0.0031308f ? c * 12.92f : 1.055f * Mathf.Pow(c, 1f / 2.4f) - 0.055f;

        static ulong Hash(LetteraGradient gradient)
        {
            ulong hash = 14695981039346656037UL;
            hash = (hash ^ (ulong)gradient.Mode) * 1099511628211UL;
            foreach (LetteraGradientStop stop in gradient.Stops)
            {
                hash = (hash ^ (uint)Mathf.RoundToInt(stop.Position * 65535f)) * 1099511628211UL;
                hash = (hash ^ (uint)(stop.Color.r | stop.Color.g << 8 | stop.Color.b << 16 | stop.Color.a << 24)) * 1099511628211UL;
            }
            return hash;
        }

        /// <summary>Releases the texture; texts bake their rows again after a domain reload.</summary>
        public static void Shutdown()
        {
            if (s_Texture != null)
            {
                if (Application.isPlaying) Object.Destroy(s_Texture);
                else Object.DestroyImmediate(s_Texture);
                s_Texture = null;
            }
            s_Rows.Clear();
            if (s_FineTexture != null)
            {
                if (Application.isPlaying) Object.Destroy(s_FineTexture);
                else Object.DestroyImmediate(s_FineTexture);
                s_FineTexture = null;
            }
            s_FineRows.Clear();
        }
    }
}

using System.Collections.Generic;
using UnityEngine;

namespace FirstSymbol.Lettera
{
    /// <summary>
    /// Every color filter in use, as rows of a small float texture bound globally, so different filters never split a batch.
    /// Equal matrices share a row. Row 0 means no filter.
    /// </summary>
    internal static class FilterAtlas
    {
        const int Rows = 256;
        static readonly int s_FiltersId = Shader.PropertyToID("_LetteraFilters");
        static readonly Dictionary<ulong, int> s_Known = new Dictionary<ulong, int>();
        static readonly Color[] s_Row = new Color[5];
        static Texture2D s_Texture;
        static bool s_Warned;

        public static float RowOf(float[] matrix)
        {
            ulong hash = 14695981039346656037UL;
            foreach (float value in matrix) hash = (hash ^ (uint)Mathf.RoundToInt(value * 4096f)) * 1099511628211UL;
            if (s_Known.TryGetValue(hash, out int row)) return row;
            if (s_Known.Count >= Rows - 1)
            {
                if (!s_Warned) Debug.LogWarning($"Lettera: more than {Rows - 1} different color filters are in use; further ones do nothing.");
                s_Warned = true;
                return 0;
            }
            row = s_Known.Count + 1;
            s_Known[hash] = row;
            if (s_Texture == null)
            {
                s_Texture = new Texture2D(5, Rows, TextureFormat.RGBAFloat, false, true)
                {
                    name = "Lettera Filters",
                    hideFlags = HideFlags.HideAndDontSave,
                    filterMode = FilterMode.Point,
                    wrapMode = TextureWrapMode.Clamp,
                };
                Shader.SetGlobalTexture(s_FiltersId, s_Texture);
            }
            // Columns: the factors of r, g, b and a in each output, then the offsets.
            for (int column = 0; column < 5; column++)
                s_Row[column] = new Color(matrix[column], matrix[5 + column], matrix[10 + column], matrix[15 + column]);
            s_Texture.SetPixels(0, row, 5, 1, s_Row);
            s_Texture.Apply(false, false);
            return row;
        }

        /// <summary>a then b: the matrix that does a first.</summary>
        public static void Compose(float[] a, float[] b, float[] output)
        {
            for (int row = 0; row < 4; row++)
            {
                for (int column = 0; column < 4; column++)
                    output[row * 5 + column] = b[row * 5] * a[column] + b[row * 5 + 1] * a[5 + column] + b[row * 5 + 2] * a[10 + column] + b[row * 5 + 3] * a[15 + column];
                output[row * 5 + 4] = b[row * 5] * a[4] + b[row * 5 + 1] * a[9] + b[row * 5 + 2] * a[14] + b[row * 5 + 3] * a[19] + b[row * 5 + 4];
            }
        }

        public static void Shutdown()
        {
            if (s_Texture != null)
            {
                if (Application.isPlaying) Object.Destroy(s_Texture);
                else Object.DestroyImmediate(s_Texture);
                s_Texture = null;
            }
            s_Known.Clear();
        }
    }
}

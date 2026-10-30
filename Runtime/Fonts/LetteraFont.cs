using System;
using System.IO;
using System.IO.Compression;
using UnityEngine;

namespace FirstSymbol.Lettera
{
    /// <summary>A font file stored inside an asset. Bytes stay compressed until a text first uses the font.</summary>
    [CreateAssetMenu(menuName = "Lettera/Font Asset", fileName = "New Lettera Font", order = 100)]
    public class LetteraFont : LetteraFontAsset
    {
        [SerializeField, HideInInspector] byte[] m_Compressed;
        [SerializeField, HideInInspector] int m_OriginalLength;
        [SerializeField, Min(0)] int m_FaceIndex;

        [NonSerialized] FontFace m_Face;
        [NonSerialized] bool m_LoadFailed;
        [NonSerialized] byte[] m_RuntimeBytes;

        public int FaceIndex { get => m_FaceIndex; set { m_FaceIndex = value; Unload(); NotifyChanged(); } }
        public bool HasData => (m_Compressed != null && m_Compressed.Length > 0) || m_RuntimeBytes != null;
        public int CompressedSize => m_Compressed?.Length ?? 0;
        public int OriginalSize => m_OriginalLength;
        public bool LoadFailed => m_LoadFailed;

        /// <summary>Creates a font at runtime. The array is kept and must not be changed.</summary>
        public static T CreateFromBytes<T>(byte[] bytes, int faceIndex = 0) where T : LetteraFont
        {
            var font = CreateInstance<T>();
            font.hideFlags = HideFlags.DontSave;
            font.m_RuntimeBytes = bytes;
            font.m_FaceIndex = faceIndex;
            return font;
        }

        /// <summary>Creates a font at runtime. The array is kept and must not be changed.</summary>
        public static LetteraFont CreateFromBytes(byte[] bytes, int faceIndex = 0) => CreateFromBytes<LetteraFont>(bytes, faceIndex);

        /// <summary>Stores font file bytes in the asset, compressed.</summary>
        public void SetFontData(byte[] bytes)
        {
            using (var output = new MemoryStream())
            {
                using (var deflate = new DeflateStream(output, System.IO.Compression.CompressionLevel.Optimal, true))
                    deflate.Write(bytes, 0, bytes.Length);
                m_Compressed = output.ToArray();
            }
            m_OriginalLength = bytes.Length;
            Unload();
            NotifyChanged();
        }

        /// <summary>The font file, for a variant that shows the same file another way.</summary>
        internal byte[] ReadBytes() => Decompress();

        byte[] Decompress()
        {
            if (m_RuntimeBytes != null) return m_RuntimeBytes;
            if (m_Compressed == null || m_Compressed.Length == 0) return null;
            var bytes = new byte[m_OriginalLength];
            using (var input = new MemoryStream(m_Compressed))
            using (var deflate = new DeflateStream(input, CompressionMode.Decompress))
            {
                int read = 0;
                while (read < bytes.Length)
                {
                    int n = deflate.Read(bytes, read, bytes.Length - read);
                    if (n <= 0) break;
                    read += n;
                }
            }
            return bytes;
        }

        internal override FontFace LoadedFace => m_Face;

        internal override FontFace LoadFace()
        {
            if (m_Face != null && m_Face.Face != IntPtr.Zero) return m_Face;
            if (m_LoadFailed) return null;
            m_Face = null;
            byte[] bytes = Decompress();
            if (bytes != null) m_Face = FontFace.FromBytes(bytes, m_FaceIndex);
            if (m_Face == null)
            {
                m_LoadFailed = true;
                Debug.LogError($"Lettera: font asset '{name}' holds no readable font data.", this);
            }
            PushPixelSettings();
            return m_Face;
        }

        void Unload()
        {
            m_Face?.Dispose();
            m_Face = null;
            m_LoadFailed = false;
        }

        protected virtual void OnDestroy() => Unload();
    }
}

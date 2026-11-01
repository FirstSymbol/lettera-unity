using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace FirstSymbol.Lettera
{
    /// <summary>A font file stored inside an asset. In a player no font keeps a whole copy of its file in memory: it is unpacked the first time a text needs it, and mapped from disk after that.</summary>
    [CreateAssetMenu(menuName = "Lettera/Font Asset", fileName = "New Lettera Font", order = 100)]
    public class LetteraFont : LetteraFontAsset, ISerializationCallbackReceiver
    {
#if UNITY_EDITOR
        // The authority, in the editor only. It is left out of a build, so a build can never change what is on disk.
        [SerializeField, HideInInspector] byte[] m_EditorBytes;

        /// <summary>The fonts this build carries in files of their own, by hash. Filled while a player builds.</summary>
        internal static readonly HashSet<string> PackedInThisBuild = new HashSet<string>();
#endif
        [SerializeField, HideInInspector] byte[] m_Compressed;
        [SerializeField, HideInInspector] string m_PayloadHash = "";
        [SerializeField, HideInInspector] int m_PayloadLength;
        [SerializeField, HideInInspector] int m_OriginalLength;
        [SerializeField, Min(0)] int m_FaceIndex;
        [SerializeField, Tooltip("Keeps this font inside its asset in every build, whatever the project's packaging says.")] bool m_KeepInline;

        [NonSerialized] FontFace m_Face;
        [NonSerialized] bool m_LoadFailed;
        [NonSerialized] byte[] m_RuntimeBytes;

        public int FaceIndex { get => m_FaceIndex; set { m_FaceIndex = value; Unload(); NotifyChanged(); } }
        public bool KeepInline { get => m_KeepInline; set => m_KeepInline = value; }
        public bool HasData => StoredPayload() != null || m_RuntimeBytes != null || !string.IsNullOrEmpty(m_PayloadHash);
        public int CompressedSize => StoredPayload()?.Length ?? m_PayloadLength;
        public int OriginalSize => m_OriginalLength;
        public bool LoadFailed => m_LoadFailed;

        /// <summary>Lets a test take the way a player goes, in the editor.</summary>
        internal static bool LoadAsPlayerForTests;

        byte[] StoredPayload()
        {
#if UNITY_EDITOR
            if (m_EditorBytes != null && m_EditorBytes.Length > 0) return m_EditorBytes;
#endif
            return m_Compressed != null && m_Compressed.Length > 0 ? m_Compressed : null;
        }

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
            byte[] compressed;
            using (var output = new MemoryStream())
            {
                using (var deflate = new DeflateStream(output, System.IO.Compression.CompressionLevel.Optimal, true))
                    deflate.Write(bytes, 0, bytes.Length);
                compressed = output.ToArray();
            }
#if UNITY_EDITOR
            m_EditorBytes = compressed;
            m_Compressed = null;
#else
            m_Compressed = compressed;
#endif
            m_PayloadHash = HashOf(compressed);
            m_PayloadLength = compressed.Length;
            m_OriginalLength = bytes.Length;
            Unload();
            NotifyChanged();
        }

        internal static string HashOf(byte[] data)
        {
            using (SHA256 sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(data);
                var text = new StringBuilder(hash.Length * 2);
                foreach (byte b in hash) text.Append(b.ToString("x2"));
                return text.ToString();
            }
        }

        void ISerializationCallbackReceiver.OnBeforeSerialize()
        {
#if UNITY_EDITOR
            LetteraStamps.Save(this, ref LetteraSerializedVersion);
            if (!UnityEditor.BuildPipeline.isBuildingPlayer)
            {
                m_Compressed = null;
                return;
            }
            // Everything a build holds comes through here. A font the scan missed is simply inline, so that the scan can only ever cost memory, never lose a font.
            if (string.IsNullOrEmpty(m_PayloadHash) && m_EditorBytes != null && m_EditorBytes.Length > 0) m_PayloadHash = HashOf(m_EditorBytes);
            m_PayloadLength = m_EditorBytes?.Length ?? 0;
            m_Compressed = PackedInThisBuild.Contains(m_PayloadHash) ? null : m_EditorBytes;
#endif
        }

        void ISerializationCallbackReceiver.OnAfterDeserialize()
        {
#if UNITY_EDITOR
            LetteraStamps.Load(this, LetteraSerializedVersion);
            // A font stored before there were two fields.
            if ((m_EditorBytes == null || m_EditorBytes.Length == 0) && m_Compressed != null && m_Compressed.Length > 0) m_EditorBytes = m_Compressed;
            m_Compressed = null;
#endif
        }

        /// <summary>The font file, for a variant that shows the same file another way.</summary>
        internal byte[] ReadBytes()
        {
            if (m_RuntimeBytes != null) return m_RuntimeBytes;
            byte[] payload = StoredPayload();
            if (payload != null) return Inflate(payload);
            if (string.IsNullOrEmpty(m_PayloadHash)) return null;
            // Already mapped from a file, which this one path has to read back.
            string path = LetteraFontCache.PathOf(m_PayloadHash);
            if (LetteraFontCache.IsValid(path, m_OriginalLength)) return File.ReadAllBytes(path);
            payload = LetteraFontCache.ReadPacked(m_PayloadHash);
            return payload != null ? Inflate(payload) : null;
        }

        byte[] Inflate(byte[] payload)
        {
            var bytes = new byte[m_OriginalLength];
            using (var input = new MemoryStream(payload))
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

        /// <summary>What a build carries for this font.</summary>
        internal byte[] PayloadForBuild() => StoredPayload();

        internal override FontFace LoadedFace => m_Face;

        internal override FontFace LoadFace()
        {
            if (m_Face != null && m_Face.Face != IntPtr.Zero) return m_Face;
            if (m_LoadFailed) return null;
            m_Face = null;
            if (m_RuntimeBytes != null) m_Face = FontFace.FromBytes(m_RuntimeBytes, m_FaceIndex, LetteraFontSource.RuntimeBytes);
            else m_Face = MapsItsFile ? LoadThroughCache() : LoadInMemory();
            if (m_Face == null)
            {
                m_LoadFailed = true;
                Debug.LogError($"Lettera: font asset '{name}' holds no readable font data.", this);
            }
            PushPixelSettings();
            return m_Face;
        }

        // A browser has no files to map, and an editor keeps an embedded font in memory, so that a plain session leaves nothing on disk.
        static bool MapsItsFile
        {
            get
            {
                if (LoadAsPlayerForTests) return true;
#if UNITY_WEBGL || UNITY_EDITOR
                return false;
#else
                return true;
#endif
            }
        }

        FontFace LoadInMemory()
        {
            byte[] payload = StoredPayload();
            return payload != null ? FontFace.FromBytes(Inflate(payload), m_FaceIndex, LetteraFontSource.InMemory) : null;
        }

        FontFace LoadThroughCache()
        {
            LetteraFontCache.EvictOnce();
            byte[] payload = StoredPayload();
            string hash = m_PayloadHash;
            if (string.IsNullOrEmpty(hash))
            {
                // Inside a bundle built before hashes, or one with none: this can still be read.
                if (payload == null) return null;
                hash = HashOf(payload);
            }
            string path = LetteraFontCache.PathOf(hash);
            FontFace mapped = LetteraFontCache.IsValid(path, m_OriginalLength) ? FontFace.FromFile(path, m_FaceIndex, LetteraFontSource.CacheMapped) : null;
            if (mapped != null)
            {
                DropPayload();
                return mapped;
            }
            if (payload == null) payload = LetteraFontCache.ReadPacked(hash);
            if (payload == null)
            {
                LetteraPlatform.Report(LetteraNativeFeature.TextInput, $"The packed data of font '{name}' could not be read, so text goes to the next font.");
                return null;
            }
            byte[] bytes = Inflate(payload);
            path = LetteraFontCache.Publish(hash, bytes);
            mapped = path != null ? FontFace.FromFile(path, m_FaceIndex, LetteraFontSource.CacheMapped) : null;
            if (mapped != null)
            {
                DropPayload();
                return mapped;
            }
            LetteraPlatform.Report(LetteraNativeFeature.TextInput, $"Font '{name}' could not be mapped from the cache, so it is held in memory.");
            return FontFace.FromBytes(bytes, m_FaceIndex, LetteraFontSource.InMemory);
        }

        // From here on the file holds it, and a bundle this came from can be unloaded.
        void DropPayload() => m_Compressed = null;

        /// <summary>Lets go of the loaded face until a text next asks for it.</summary>
        internal void ReleaseFace()
        {
            Unload();
            NotifyChanged();
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

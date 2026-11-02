using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace FirstSymbol.Lettera
{
    /// <summary>How a build carries a font's bytes.</summary>
    public enum LetteraFontPackaging
    {
        /// <summary>What this target does by default: packed on desktop, inside the asset elsewhere.</summary>
        Auto,

        /// <summary>Inside the asset, so they load with it until the font is first used.</summary>
        Inline,

        /// <summary>In a file of their own beside the build, so an unused font costs nothing.</summary>
        Packed,
    }

    /// <summary>Unpacked fonts on disk, so that they can be mapped rather than held in memory. The system may clear this folder, and that costs one unpacking.</summary>
    internal static class LetteraFontCache
    {
        internal const string PackedFolder = "Lettera/Fonts";

        /// <summary>Replaces the folder for tests.</summary>
        internal static string FolderOverride;

        /// <summary>Replaces StreamingAssets for tests.</summary>
        internal static string PackedFolderOverride;

        static bool s_Evicted;

        internal static string Folder => FolderOverride ?? Path.Combine(Application.temporaryCachePath, "Lettera", "Fonts");

        internal static string PathOf(string hash) => Path.Combine(Folder, hash + ".ttf");

        /// <summary>Whether a usable file is there: a wrong length or an unreadable font means it gets written again.</summary>
        internal static unsafe bool IsValid(string path, int length)
        {
            try
            {
                var info = new FileInfo(path);
                if (!info.Exists || info.Length != length) return false;
                byte[] utf8 = FontFace.NullTerminated(path);
                fixed (byte* p = utf8) return LetteraNative.lt_file_face_count(p) > 0;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>Writes a file under a temporary name and moves it into place, so two processes racing is safe. Null when it could not.</summary>
        internal static string Publish(string hash, byte[] bytes)
        {
            string target = PathOf(hash);
            string temporary = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                Directory.CreateDirectory(Folder);
                File.WriteAllBytes(temporary, bytes);
                if (File.Exists(target)) File.Delete(target);
                File.Move(temporary, target);
                return target;
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                try
                {
                    if (File.Exists(temporary)) File.Delete(temporary);
                }
                catch (Exception)
                {
                    // Nothing more can be done, and a stray file harms nothing.
                }
                // Another process may have won, and then the file is there.
                return IsValid(target, bytes.Length) ? target : null;
            }
        }

        /// <summary>A packed font's compressed bytes, or null.</summary>
        internal static byte[] ReadPacked(string hash)
        {
            string name = hash + ".ltp";
            if (PackedFolderOverride != null) return ReadFile(Path.Combine(PackedFolderOverride, name));
            string path = Path.Combine(Application.streamingAssetsPath, PackedFolder, name);
#if UNITY_ANDROID && !UNITY_EDITOR
            return ReadThroughRequest(path);
#else
            return ReadFile(path);
#endif
        }

        static byte[] ReadFile(string path)
        {
            try
            {
                return File.Exists(path) ? File.ReadAllBytes(path) : null;
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                return null;
            }
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        // Inside an APK, where File cannot look. A font loads synchronously, so this waits. That is why Android does not pack by default.
        static byte[] ReadThroughRequest(string url)
        {
            using (var request = UnityEngine.Networking.UnityWebRequest.Get(url))
            {
                request.SendWebRequest();
                double until = Time.realtimeSinceStartupAsDouble + 10;
                while (!request.isDone && Time.realtimeSinceStartupAsDouble < until) System.Threading.Thread.Sleep(1);
                return request.isDone && request.result == UnityEngine.Networking.UnityWebRequest.Result.Success ? request.downloadHandler.data : null;
            }
        }
#endif

        /// <summary>Keeps the folder within its budget, oldest first. Runs before anything is mapped, since a mapped file cannot be deleted. Once a run.</summary>
        internal static void EvictOnce()
        {
            if (s_Evicted) return;
            s_Evicted = true;
            Evict(LetteraSettings.Instance.FontMemory.FontCacheBytes);
        }

        internal static void Evict(long limit)
        {
            try
            {
                var folder = new DirectoryInfo(Folder);
                if (!folder.Exists) return;
                FileInfo[] files = folder.GetFiles("*");
                long total = 0;
                foreach (FileInfo file in files) total += file.Length;
                if (total <= limit) return;
                Array.Sort(files, (a, b) => a.LastAccessTimeUtc.CompareTo(b.LastAccessTimeUtc));
                foreach (FileInfo file in files)
                {
                    if (total <= limit) break;
                    long size = file.Length;
                    try
                    {
                        file.Delete();
                        total -= size;
                    }
                    catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
                    {
                        // Mapped by this or another process: it stays.
                    }
                }
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                LetteraPlatform.Report(LetteraNativeFeature.TextInput, "The font cache could not be trimmed: " + e.Message);
            }
        }

        internal static long FolderBytes()
        {
            try
            {
                var folder = new DirectoryInfo(Folder);
                if (!folder.Exists) return 0;
                long total = 0;
                foreach (FileInfo file in folder.GetFiles("*")) total += file.Length;
                return total;
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                return 0;
            }
        }

        internal static void ResetForTests() => s_Evicted = false;
    }

    /// <summary>Where a face's bytes are.</summary>
    /// <summary>Where a face's bytes are. RuntimeFile is a file named by <see cref="LetteraFont.CreateFromFile(string, int)"/>, mapped where the platform allows.</summary>
    public enum LetteraFontSource { SystemMapped, CacheMapped, InMemory, RuntimeBytes, Virtual, RuntimeFile }

    /// <summary>One loaded face, for a memory view.</summary>
    public struct LetteraFontMemoryEntry
    {
        public string Family, Style;
        public LetteraFontSource Source;

        /// <summary>Bytes this face holds in memory. A mapped file counts as none.</summary>
        public long RetainedBytes;

        /// <summary>The length of the file it maps.</summary>
        public long MappedBytes;

        /// <summary>Its variation and synthesis instances, the regular one included.</summary>
        public int InstanceCount;

        public int LastUsedFrame;
    }

    /// <summary>What loaded fonts cost right now.</summary>
    public static class LetteraFontMemory
    {
        /// <summary>The sums over every loaded face, and what the cache folder holds.</summary>
        public struct Sums
        {
            public long RetainedBytes, MappedBytes, CacheFolderBytes;
            public int Faces, Instances;
        }

        /// <summary>Fills a list with every loaded face.</summary>
        public static void Snapshot(List<LetteraFontMemoryEntry> into)
        {
            into.Clear();
            FontFace.CollectMemory(into);
        }

        public static Sums Totals
        {
            get
            {
                var sums = new Sums();
                FontFace.AddMemory(ref sums);
                sums.CacheFolderBytes = LetteraFontCache.FolderBytes();
                return sums;
            }
        }
    }
}

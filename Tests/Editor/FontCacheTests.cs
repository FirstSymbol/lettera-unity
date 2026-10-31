using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace FirstSymbol.Lettera.Editor.Tests
{
    /// <summary>The way a player reaches a font, in a folder of its own. A real player's run is the proof; here are the ways it can go wrong.</summary>
    public class FontCacheTests : TextFixture
    {
        string m_Cache, m_Packed;

        [SetUp]
        public void InFolders()
        {
            string root = Path.Combine(Path.GetTempPath(), "lettera_fontcache_" + Guid.NewGuid().ToString("N"));
            m_Cache = Path.Combine(root, "cache");
            m_Packed = Path.Combine(root, "packed");
            Directory.CreateDirectory(m_Packed);
            LetteraFontCache.FolderOverride = m_Cache;
            LetteraFontCache.PackedFolderOverride = m_Packed;
            LetteraFontCache.ResetForTests();
            LetteraFont.LoadAsPlayerForTests = true;
        }

        [TearDown]
        public void Back()
        {
            LetteraFont.LoadAsPlayerForTests = false;
            LetteraFontCache.FolderOverride = null;
            LetteraFontCache.PackedFolderOverride = null;
            LetteraFontCache.ResetForTests();
            SetFolderWritable(true);
            try
            {
                Directory.Delete(Path.GetDirectoryName(m_Cache), true);
            }
            catch (IOException)
            {
                // A mapped file stays until its font goes.
            }
        }

        void SetFolderWritable(bool writable)
        {
            if (!Directory.Exists(m_Cache)) return;
            var info = new DirectoryInfo(m_Cache);
            info.Attributes = writable ? FileAttributes.Normal : FileAttributes.ReadOnly;
        }

        static byte[] NotoBytes()
        {
            string asmdef = AssetDatabase.GUIDToAssetPath(AssetDatabase.FindAssets("FirstSymbol.Lettera.Editor.Tests t:asmdef")[0]);
            return File.ReadAllBytes(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(asmdef)), "..", "Fonts~", "NotoSans-Regular.ttf"));
        }

        LetteraFont Stored()
        {
            var font = ScriptableObject.CreateInstance<LetteraFont>();
            font.hideFlags = HideFlags.DontSave;
            font.SetFontData(NotoBytes());
            return font;
        }

        static string HashOf(LetteraFont font) => LetteraFont.HashOf(font.PayloadForBuild());

        string CachedPath(LetteraFont font) => Path.Combine(m_Cache, HashOf(font) + ".ttf");

        static void Unloaded(LetteraFont font)
        {
            font.FaceIndex = font.FaceIndex;
        }

        [Test]
        public void TheFirstUseUnpacksAndMaps()
        {
            LetteraFont font = Stored();
            Assert.IsFalse(File.Exists(CachedPath(font)), "no use, no file");
            FontFace face = font.LoadFace();
            Assert.IsNotNull(face);
            Assert.AreEqual(LetteraFontSource.CacheMapped, face.Source);
            Assert.AreEqual(0, face.RetainedBytes);
            Assert.AreEqual(new FileInfo(CachedPath(font)).Length, face.MappedBytes);
            Assert.AreEqual("Noto Sans", face.Family);
            Unloaded(font);
        }

        [Test]
        public void AnUnusedFontCostsNoFile()
        {
            LetteraFont font = Stored();
            Assert.IsTrue(font.HasData);
            Assert.AreEqual(0, Directory.Exists(m_Cache) ? Directory.GetFiles(m_Cache).Length : 0);
        }

        [Test]
        public void ADeletedOrCutFileIsWrittenAgain()
        {
            LetteraFont font = Stored();
            font.LoadFace();
            Unloaded(font);
            string path = CachedPath(font);
            File.Delete(path);
            Assert.AreEqual(LetteraFontSource.CacheMapped, font.LoadFace().Source);
            Assert.IsTrue(File.Exists(path));
            Unloaded(font);
            int length = (int)new FileInfo(path).Length;
            byte[] cut = File.ReadAllBytes(path);
            Array.Resize(ref cut, length / 2);
            File.WriteAllBytes(path, cut);
            Assert.AreEqual(LetteraFontSource.CacheMapped, font.LoadFace().Source);
            Assert.AreEqual(length, new FileInfo(path).Length);
            Unloaded(font);
        }

        [Test]
        public void ACacheThatCannotBeWrittenHoldsTheFontInMemory()
        {
            LetteraFont font = Stored();
            // A file where the folder should be cannot be written through, on any system and for any user.
            Directory.CreateDirectory(Path.GetDirectoryName(m_Cache));
            File.WriteAllText(m_Cache, "in the way");
            FontFace face = font.LoadFace();
            Assert.IsNotNull(face);
            Assert.AreEqual(LetteraFontSource.InMemory, face.Source);
            Assert.Greater(face.RetainedBytes, 0);
            Unloaded(font);
            File.Delete(m_Cache);
        }

        [Test]
        public void APackedPayloadIsReadFromItsFile()
        {
            LetteraFont font = Stored();
            string hash = HashOf(font);
            File.WriteAllBytes(Path.Combine(m_Packed, hash + ".ltp"), font.PayloadForBuild());
            Assert.AreEqual(hash, LetteraFont.HashOf(LetteraFontCache.ReadPacked(hash)));
            Assert.IsNull(LetteraFontCache.ReadPacked("no such font"));
        }

        [Test]
        public void TwoFontsOfOneFileShareItsCacheFile()
        {
            LetteraFont a = Stored(), b = Stored();
            a.LoadFace();
            b.LoadFace();
            Assert.AreEqual(1, Directory.GetFiles(m_Cache).Length);
            Unloaded(a);
            Unloaded(b);
        }

        [Test]
        public void AFolderOverItsLimitLosesItsOldestFirst()
        {
            Directory.CreateDirectory(m_Cache);
            string old = Path.Combine(m_Cache, "old.ttf"), recent = Path.Combine(m_Cache, "recent.ttf");
            File.WriteAllBytes(old, new byte[1000]);
            File.WriteAllBytes(recent, new byte[1000]);
            File.SetLastAccessTimeUtc(old, DateTime.UtcNow.AddDays(-5));
            File.SetLastAccessTimeUtc(recent, DateTime.UtcNow);
            LetteraFontCache.Evict(1500);
            Assert.IsFalse(File.Exists(old));
            Assert.IsTrue(File.Exists(recent));
            Assert.AreEqual(1000, LetteraFontCache.FolderBytes());
        }

        [Test]
        public void AFontStoredBeforeTheseFieldsKeepsItsBytes()
        {
            LetteraFont font = Stored();
            byte[] before = font.PayloadForBuild();
            string json = EditorJsonUtility.ToJson(font);
            StringAssert.Contains("m_EditorBytes", json);
            var old = ScriptableObject.CreateInstance<LetteraFont>();
            old.hideFlags = HideFlags.DontSave;
            // Stored the old way: all it has is the one field.
            string asOld = json.Replace("m_EditorBytes", "m_Compressed2").Replace("\"m_Compressed\"", "\"m_Unused\"").Replace("m_Compressed2", "m_Compressed");
            EditorJsonUtility.FromJsonOverwrite(asOld, old);
            CollectionAssert.AreEqual(before, old.PayloadForBuild());
        }

        [Test]
        public void TheFiguresAddUp()
        {
            LetteraFont mapped = Stored();
            mapped.LoadFace();
            LetteraFont runtime = LetteraFont.CreateFromBytes(NotoBytes());
            runtime.LoadFace();
            var entries = new List<LetteraFontMemoryEntry>();
            LetteraFontMemory.Snapshot(entries);
            long retained = 0, mappedBytes = 0;
            bool sawMapped = false, sawRuntime = false;
            foreach (LetteraFontMemoryEntry entry in entries)
            {
                retained += entry.RetainedBytes;
                mappedBytes += entry.MappedBytes;
                sawMapped |= entry.Source == LetteraFontSource.CacheMapped && entry.RetainedBytes == 0;
                sawRuntime |= entry.Source == LetteraFontSource.RuntimeBytes && entry.RetainedBytes > 0;
            }
            Assert.IsTrue(sawMapped);
            Assert.IsTrue(sawRuntime);
            LetteraFontMemory.Sums sums = LetteraFontMemory.Totals;
            Assert.AreEqual(retained, sums.RetainedBytes);
            Assert.AreEqual(mappedBytes, sums.MappedBytes);
            Assert.AreEqual(entries.Count, sums.Faces);
            Assert.Greater(sums.CacheFolderBytes, 0);
            Unloaded(mapped);
            Unloaded(runtime);
        }

        [Test]
        public void EachInstanceCounts()
        {
            FontFace face = Latin.LoadFace();
            int before = face.InstanceCount;
            for (int i = 1; i <= 5; i++) face.GetInstance(-1, 0.01f * i, 0);
            Assert.AreEqual(before + 5, face.InstanceCount);
        }

        [Test]
        public void SnapshotAllocatesOnlyWhatTheListGrowsBy()
        {
            Latin.LoadFace();
            var entries = new List<LetteraFontMemoryEntry>(256);
            LetteraFontMemory.Snapshot(entries);
            int capacity = entries.Capacity;
            LetteraFontMemory.Snapshot(entries);
            Assert.AreEqual(capacity, entries.Capacity);
        }
    }
}

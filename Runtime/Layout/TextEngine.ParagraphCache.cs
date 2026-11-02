using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using FirstSymbol.Lettera.Unicode;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;

namespace FirstSymbol.Lettera
{
    /// <summary>What the shared text caches hold, and the switch for them.</summary>
    public static class LetteraCaches
    {
        /// <summary>Off, every paragraph is analyzed and shaped again, with the same result. For comparing, and for tests.</summary>
        public static bool Enabled = true;

        /// <summary>How much the paragraph cache may hold.</summary>
        public static long ParagraphCacheBytes = Application.platform == RuntimePlatform.WebGLPlayer ? 2L << 20 : Application.isMobilePlatform ? 4L << 20 : 8L << 20;

        public struct Statistics
        {
            public long Hits, Misses, Evictions, Bytes, ShapeCalls;
        }

        internal static Statistics s_Stats;

        public static Statistics Stats
        {
            get
            {
                Statistics stats = s_Stats;
                stats.Bytes = TextEngine.ParagraphBytes;
                return stats;
            }
        }

        /// <summary>Forgets what was shaped, and frees it.</summary>
        public static void Clear() => TextEngine.ClearParagraphCache();

        internal static void ResetStats() => s_Stats = default;

        /// <summary>Makes every paragraph hash alike, so a test can see that a collision never draws the wrong text.</summary>
        internal static bool ForceCollisions;
    }

    /// <summary>One font chain per font set, shared by every text using it, so a fallback search for a character runs once.</summary>
    internal static class LetteraFontChains
    {
        struct Key : IEquatable<Key>
        {
            public LetteraFontAsset Font;
            public LetteraFontStack Stack;
            public bool Equals(Key o) => ReferenceEquals(Font, o.Font) && ReferenceEquals(Stack, o.Stack);
            public override bool Equals(object obj) => obj is Key k && Equals(k);
            public override int GetHashCode() => (Font is null ? 0 : Font.GetInstanceID()) * 397 ^ (Stack is null ? 0 : Stack.GetInstanceID());
        }

        static readonly Dictionary<Key, FontChain> s_Chains = new Dictionary<Key, FontChain>();
        static readonly List<Key> s_Dead = new List<Key>();
        static int s_NextId = 1;
        static bool s_Listening;

        internal static int Count => s_Chains.Count;

        static void Listen()
        {
            if (s_Listening) return;
            s_Listening = true;
            LetteraSettings.Changed += Invalidate;
            LetteraFontAsset.FontAssetChanged += _ => Invalidate();
            LetteraFontStack.StackChanged += _ => Invalidate();
        }

        public static FontChain Get(LetteraFontAsset font, LetteraFontStack stack)
        {
            Listen();
            var key = new Key { Font = font, Stack = stack };
            if (s_Chains.TryGetValue(key, out FontChain chain)) return chain;
            if (s_Chains.Count >= 32) SweepDestroyed();
            chain = new FontChain { Id = s_NextId++ };
            chain.Build(font, stack);
            s_Chains[key] = chain;
            return chain;
        }

        static void SweepDestroyed()
        {
            s_Dead.Clear();
            foreach (Key key in s_Chains.Keys)
                if ((!(key.Font is null) && key.Font == null) || (!(key.Stack is null) && key.Stack == null))
                    s_Dead.Add(key);
            foreach (Key key in s_Dead) s_Chains.Remove(key);
        }

        // Fonts, stacks or settings changed. Chain ids are never reused, so what was shaped with an old chain cannot be found again.
        public static void Invalidate()
        {
            s_Chains.Clear();
            TextEngine.ClearParagraphCache();
        }
    }

    // A paragraph with the same characters and shaping attributes shapes the same anywhere, so its analysis and glyphs are kept in font units and shared.
    // Each entry is one block of native memory: storing a paragraph makes no garbage, even while the cache grows, and a forgotten one is given back at once.
    internal sealed unsafe partial class TextEngine
    {
        // The head of a block. Its arrays follow it, widest first, so each stays aligned.
        struct ParagraphEntry
        {
            public long Hash, Bytes;
            public int Count, ChainId, BaseLevel, SizeMatch, Level, RunCount, GlyphCount, Language, Signed;
            public ParagraphEntry* Newer, Older;
        }

        // A run without its font, which a managed list holds for all blocks. Language and features are the text's own, read again where the run lands.
        struct StoredRun
        {
            public int Start, End, GlyphStart, GlyphCount, Instance;
            public float UnitEm, SizeAbsolute, SizeMul, SizeAdd, BaselineShift;
            public UnicodeScript Script;
            public byte Level, FaceFeature;
        }

        const int ParagraphCacheMinimum = 8;
        // What the system's allocator keeps beside each block, counted against the budget too.
        const int BlockOverhead = 16;
        static readonly int EntryHead = (sizeof(ParagraphEntry) + 15) & ~15;

        // Open addressing over native memory, so that growing it makes no garbage either.
        static ParagraphEntry** s_Table;
        static int s_TableMask = -1, s_EntryCount;
        static ParagraphEntry* s_Newest, s_Oldest;
        static long s_ParagraphBytes;
        static readonly List<FontInstance> s_StoredInstances = new List<FontInstance>();
        static readonly List<string> s_StoredLanguages = new List<string>();

        internal static long ParagraphBytes => s_ParagraphBytes;
        internal static int ParagraphCount => s_EntryCount;

        long[] m_CpSignature = Array.Empty<long>();
        bool m_SignaturesPlain;

        internal static void ClearParagraphCache()
        {
            ParagraphEntry* entry = s_Newest;
            while (entry != null)
            {
                ParagraphEntry* older = entry->Older;
                UnsafeUtility.Free(entry, Allocator.Persistent);
                entry = older;
            }
            if (s_Table != null) UnsafeUtility.Free(s_Table, Allocator.Persistent);
            s_Table = null;
            s_TableMask = -1;
            s_EntryCount = 0;
            s_Newest = s_Oldest = null;
            s_ParagraphBytes = 0;
            s_StoredInstances.Clear();
            s_StoredLanguages.Clear();
        }

        static long* SignatureOf(ParagraphEntry* e) => (long*)((byte*)e + EntryHead);
        static int* TextOf(ParagraphEntry* e) => (int*)(SignatureOf(e) + (e->Signed != 0 ? e->Count : 0));
        static int* ShapeOf(ParagraphEntry* e) => TextOf(e) + e->Count;
        static int* CpRunOf(ParagraphEntry* e) => ShapeOf(e) + e->Count;
        static float* AdvancesOf(ParagraphEntry* e) => (float*)(CpRunOf(e) + e->Count);
        static ShapedGlyph* GlyphsOf(ParagraphEntry* e) => (ShapedGlyph*)(AdvancesOf(e) + e->Count);
        static StoredRun* RunsOf(ParagraphEntry* e) => (StoredRun*)(GlyphsOf(e) + e->GlyphCount);
        static UnicodeScript* ScriptsOf(ParagraphEntry* e) => (UnicodeScript*)(RunsOf(e) + e->RunCount);
        static byte* LevelsOf(ParagraphEntry* e) => (byte*)(ScriptsOf(e) + e->Count);
        static bool* BoundariesOf(ParagraphEntry* e) => (bool*)(LevelsOf(e) + e->Count);
        static LineBreakOpportunity* BreaksOf(ParagraphEntry* e) => (LineBreakOpportunity*)(BoundariesOf(e) + e->Count + 1);

        static long BlockBytes(int count, int glyphs, int runs, bool signed) =>
            EntryHead + (signed ? count * 8L : 0) + count * 16L + glyphs * (long)sizeof(ShapedGlyph) + runs * (long)sizeof(StoredRun) + count * 2L + count + (count + 1) + count;

        static int SlotOf(long hash) => (int)(((ulong)hash * 0x9E3779B97F4A7C15UL) >> 32) & s_TableMask;

        static ParagraphEntry* Lookup(long hash)
        {
            if (s_Table == null) return null;
            for (int i = SlotOf(hash); ; i = (i + 1) & s_TableMask)
            {
                ParagraphEntry* entry = s_Table[i];
                if (entry == null || entry->Hash == hash) return entry;
            }
        }

        static void Insert(ParagraphEntry* entry)
        {
            if ((s_EntryCount + 1) * 2 > s_TableMask + 1) GrowTable();
            int i = SlotOf(entry->Hash);
            while (s_Table[i] != null) i = (i + 1) & s_TableMask;
            s_Table[i] = entry;
            s_EntryCount++;
        }

        static void GrowTable()
        {
            int old = s_TableMask + 1;
            ParagraphEntry** before = s_Table;
            int size = Math.Max(old * 2, 256);
            long bytes = size * (long)sizeof(IntPtr);
            s_Table = (ParagraphEntry**)UnsafeUtility.Malloc(bytes, 16, Allocator.Persistent);
            UnsafeUtility.MemClear(s_Table, bytes);
            s_TableMask = size - 1;
            for (int i = 0; i < old; i++)
            {
                ParagraphEntry* entry = before[i];
                if (entry == null) continue;
                int slot = SlotOf(entry->Hash);
                while (s_Table[slot] != null) slot = (slot + 1) & s_TableMask;
                s_Table[slot] = entry;
            }
            if (before != null) UnsafeUtility.Free(before, Allocator.Persistent);
        }

        // Linear probing with no markers left behind: what follows the hole moves back into it, unless its own place lies after the hole.
        static void RemoveFromTable(ParagraphEntry* entry)
        {
            int hole = SlotOf(entry->Hash);
            while (s_Table[hole] != entry) hole = (hole + 1) & s_TableMask;
            for (int j = (hole + 1) & s_TableMask; s_Table[j] != null; j = (j + 1) & s_TableMask)
            {
                int home = SlotOf(s_Table[j]->Hash);
                bool stays = hole <= j ? hole < home && home <= j : hole < home || home <= j;
                if (stays) continue;
                s_Table[hole] = s_Table[j];
                hole = j;
            }
            s_Table[hole] = null;
            s_EntryCount--;
        }

        static void Unlink(ParagraphEntry* entry)
        {
            if (entry->Newer != null) entry->Newer->Older = entry->Older;
            else s_Newest = entry->Older;
            if (entry->Older != null) entry->Older->Newer = entry->Newer;
            else s_Oldest = entry->Newer;
            entry->Newer = entry->Older = null;
        }

        static void LinkNewest(ParagraphEntry* entry)
        {
            entry->Older = s_Newest;
            if (s_Newest != null) s_Newest->Newer = entry;
            s_Newest = entry;
            if (s_Oldest == null) s_Oldest = entry;
        }

        static void Forget(ParagraphEntry* entry)
        {
            Unlink(entry);
            RemoveFromTable(entry);
            s_ParagraphBytes -= entry->Bytes;
            UnsafeUtility.Free(entry, Allocator.Persistent);
        }

        static int StoredLanguage(string language, bool add)
        {
            if (language == null) return -1;
            for (int i = s_StoredLanguages.Count - 1; i >= 0; i--)
                if (s_StoredLanguages[i] == language)
                    return i;
            if (!add) return -2;
            s_StoredLanguages.Add(language);
            return s_StoredLanguages.Count - 1;
        }

        static int StoredInstance(FontInstance instance)
        {
            for (int i = s_StoredInstances.Count - 1; i >= 0; i--)
                if (ReferenceEquals(s_StoredInstances[i], instance))
                    return i;
            s_StoredInstances.Add(instance);
            return s_StoredInstances.Count - 1;
        }

        // What about a codepoint changes how it shapes. Color, decoration and spacing are not here, so a change of those finds the same entry.
        void PrepareShapeSignatures(int length)
        {
            m_SignaturesPlain = m_MatchContext.Ranges.Count == 0;
            if (m_SignaturesPlain) return;
            EnsureSize(ref m_CpSignature, length + 1);
            LetteraAttributes.Cp[] chars = m_Attributes.Chars;
            long last = 0;
            bool hasLast = false;
            LetteraAttributes.Cp previous = default;
            for (int i = 0; i < length; i++)
            {
                ref LetteraAttributes.Cp a = ref chars[i];
                if (hasLast && a.Family == previous.Family && a.Weight == previous.Weight && a.BoldMode == previous.BoldMode && a.Italic == previous.Italic && a.Slant == previous.Slant &&
                    a.Language == previous.Language && a.Features == previous.Features && a.Axes == previous.Axes && a.SmallCaps == previous.SmallCaps && a.SuperSub == previous.SuperSub &&
                    a.CaseMap == previous.CaseMap && a.SizeAbsolute == previous.SizeAbsolute && a.SizeMul == previous.SizeMul && a.SizeAdd == previous.SizeAdd)
                {
                    m_CpSignature[i] = last;
                    continue;
                }
                previous = a;
                hasLast = true;
                long h = -3750763034362895579L;
                void Mix(long value)
                {
                    h ^= value;
                    h *= 1099511628211L;
                }
                Mix(a.Family >= 0 ? m_Attributes.FamilyRequests[a.Family].GetHashCode() : 0);
                Mix(a.Language >= 0 ? m_Attributes.Languages[a.Language].GetHashCode() : 0);
                Mix(a.Weight);
                Mix(a.BoldMode);
                Mix(a.Italic ? 1 : 0);
                Mix(BitConverter.SingleToInt32Bits(a.Slant));
                Mix(a.Axes >= 0 ? RuntimeHelpers.GetHashCode(m_Attributes.AxisSets[a.Axes]) : 0);
                if (a.Features >= 0)
                {
                    FeatureSet set = m_Attributes.FeatureSets[a.Features];
                    for (int f = 0; f < set.Tags.Length; f++)
                    {
                        Mix(set.Tags[f]);
                        Mix(set.Values[f]);
                    }
                }
                Mix(BitConverter.SingleToInt32Bits(a.SmallCaps));
                Mix(a.SuperSub);
                Mix(a.CaseMap);
                Mix(BitConverter.SingleToInt32Bits(a.SizeAbsolute));
                Mix(BitConverter.SingleToInt32Bits(a.SizeMul));
                Mix(BitConverter.SingleToInt32Bits(a.SizeAdd));
                last = h;
                m_CpSignature[i] = last;
            }
        }

        bool ParagraphCacheApplies(int count) => LetteraCaches.Enabled && count >= ParagraphCacheMinimum && MaskGlyph == 0 && m_StageHooks.Count == 0;

        long HashParagraph(int start, int count, int baseLevel, string language)
        {
            if (LetteraCaches.ForceCollisions) return 1;
            long h = -3750763034362895579L;
            fixed (int* text = &m_Text[start], shape = &m_ShapeText[start])
            {
                for (int i = 0; i < count; i++)
                {
                    h ^= text[i];
                    h *= 1099511628211L;
                    if (shape[i] == text[i]) continue;
                    h ^= shape[i];
                    h *= 1099511628211L;
                }
            }
            if (!m_SignaturesPlain)
            {
                for (int i = start; i < start + count; i++)
                {
                    h ^= m_CpSignature[i];
                    h *= 1099511628211L;
                }
            }
            h ^= m_Chain.Id * 31L + baseLevel + 2;
            h *= 1099511628211L;
            h ^= m_Attributes.SizeMatchOverride + 7L;
            h *= 1099511628211L;
            if (language != null) h ^= language.GetHashCode();
            return h;
        }

        // The hash only finds a candidate. Only equal characters and attributes may use it, so a collision can never draw the wrong text.
        bool ParagraphIsCached(long hash, int start, int count, int baseLevel, string language) => FindParagraph(hash, start, count, baseLevel, language) != null;

        ParagraphEntry* FindParagraph(long hash, int start, int count, int baseLevel, string language)
        {
            ParagraphEntry* entry = Lookup(hash);
            if (entry == null || entry->Count != count || entry->ChainId != m_Chain.Id || entry->BaseLevel != baseLevel || entry->SizeMatch != m_Attributes.SizeMatchOverride ||
                entry->Language != StoredLanguage(language, false))
                return null;
            if (!new ReadOnlySpan<int>(m_Text, start, count).SequenceEqual(new ReadOnlySpan<int>(TextOf(entry), count))) return null;
            if ((entry->Signed != 0) == m_SignaturesPlain) return null;
            if (!m_SignaturesPlain && !new ReadOnlySpan<long>(m_CpSignature, start, count).SequenceEqual(new ReadOnlySpan<long>(SignatureOf(entry), count))) return null;
            return entry;
        }

        bool TryRestoreParagraph(long hash, ref Paragraph paragraph, int start, int count, int baseLevel, string language)
        {
            ParagraphEntry* entry = FindParagraph(hash, start, count, baseLevel, language);
            if (entry == null) return false;

            Unlink(entry);
            LinkNewest(entry);

            new ReadOnlySpan<int>(ShapeOf(entry), count).CopyTo(new Span<int>(m_ShapeText, start, count));
            new ReadOnlySpan<byte>(LevelsOf(entry), count).CopyTo(new Span<byte>(m_Levels, start, count));
            new ReadOnlySpan<bool>(BoundariesOf(entry), count + 1).CopyTo(new Span<bool>(m_Boundaries, start, count + 1));
            new ReadOnlySpan<LineBreakOpportunity>(BreaksOf(entry), count).CopyTo(new Span<LineBreakOpportunity>(m_Breaks, start, count));
            new ReadOnlySpan<UnicodeScript>(ScriptsOf(entry), count).CopyTo(new Span<UnicodeScript>(m_Scripts, start, count));
            new ReadOnlySpan<float>(AdvancesOf(entry), count).CopyTo(new Span<float>(m_CpAdvanceEm, start, count));
            paragraph.Level = entry->Level;

            int runBase = m_Runs.Count;
            int* cpRun = CpRunOf(entry);
            for (int i = 0; i < count; i++) m_CpRun[start + i] = runBase + cpRun[i];
            int glyphCount = entry->GlyphCount;
            EnsureSize(ref m_Shaped, m_ShapedCount + glyphCount);
            ShapedGlyph* glyphs = GlyphsOf(entry);
            for (int g = 0; g < glyphCount; g++)
            {
                ShapedGlyph glyph = glyphs[g];
                glyph.Cluster += start;
                m_Shaped[m_ShapedCount + g] = glyph;
            }
            // A run's language and features are indices of this text's own lists: the same values, by the check above, at maybe other places.
            LetteraAttributes.Cp[] chars = m_Attributes.Chars;
            StoredRun* runs = RunsOf(entry);
            for (int r = 0; r < entry->RunCount; r++)
            {
                StoredRun stored = runs[r];
                int runStart = stored.Start + start;
                m_Runs.Add(new Run
                {
                    Start = runStart, End = stored.End + start, GlyphStart = stored.GlyphStart + m_ShapedCount, GlyphCount = stored.GlyphCount,
                    Instance = s_StoredInstances[stored.Instance], Level = stored.Level, UnitEm = stored.UnitEm,
                    SizeAbsolute = stored.SizeAbsolute, SizeMul = stored.SizeMul, SizeAdd = stored.SizeAdd, Script = stored.Script, FaceFeature = stored.FaceFeature,
                    BaselineShift = stored.BaselineShift, Language = chars[runStart].Language, Features = chars[runStart].Features,
                });
            }
            m_ShapedCount += glyphCount;
            LetteraCaches.s_Stats.Hits++;
            return true;
        }

        void StoreParagraph(long hash, in Paragraph paragraph, int start, int count, int baseLevel, string language, int firstRun, int firstGlyph, int endRun = -1)
        {
            if (endRun < 0) endRun = m_Runs.Count;
            int runCount = endRun - firstRun;
            int glyphCount = 0;
            for (int r = firstRun; r < endRun; r++) glyphCount += m_Runs[r].GlyphCount;
            for (int r = firstRun; r < endRun; r++)
                if (m_Runs[r].Instance == null || m_Runs[r].Instance.Face.IsVirtual)
                    return;

            bool signed = !m_SignaturesPlain;
            long block = BlockBytes(count, glyphCount, runCount, signed);
            long bytes = block + BlockOverhead;
            // One paragraph that would push out everything else is not worth keeping.
            if (bytes > LetteraCaches.ParagraphCacheBytes / 2) return;
            // Fonts that come and go leave their instances listed until the next clear; this keeps that list short.
            if (s_StoredInstances.Count >= 1024 || s_StoredLanguages.Count >= 1024) ClearParagraphCache();

            ParagraphEntry* old = Lookup(hash);
            if (old != null) Forget(old);
            while (s_ParagraphBytes + bytes > LetteraCaches.ParagraphCacheBytes && s_Oldest != null)
            {
                Forget(s_Oldest);
                LetteraCaches.s_Stats.Evictions++;
            }

            var entry = (ParagraphEntry*)UnsafeUtility.Malloc(block, 16, Allocator.Persistent);
            *entry = new ParagraphEntry
            {
                Hash = hash, Bytes = bytes, Count = count, ChainId = m_Chain.Id, BaseLevel = baseLevel, SizeMatch = m_Attributes.SizeMatchOverride, Level = paragraph.Level,
                RunCount = runCount, GlyphCount = glyphCount, Language = StoredLanguage(language, true), Signed = signed ? 1 : 0,
            };
            if (signed) new ReadOnlySpan<long>(m_CpSignature, start, count).CopyTo(new Span<long>(SignatureOf(entry), count));
            new ReadOnlySpan<int>(m_Text, start, count).CopyTo(new Span<int>(TextOf(entry), count));
            new ReadOnlySpan<int>(m_ShapeText, start, count).CopyTo(new Span<int>(ShapeOf(entry), count));
            new ReadOnlySpan<byte>(m_Levels, start, count).CopyTo(new Span<byte>(LevelsOf(entry), count));
            new ReadOnlySpan<bool>(m_Boundaries, start, count + 1).CopyTo(new Span<bool>(BoundariesOf(entry), count + 1));
            new ReadOnlySpan<LineBreakOpportunity>(m_Breaks, start, count).CopyTo(new Span<LineBreakOpportunity>(BreaksOf(entry), count));
            new ReadOnlySpan<UnicodeScript>(m_Scripts, start, count).CopyTo(new Span<UnicodeScript>(ScriptsOf(entry), count));
            new ReadOnlySpan<float>(m_CpAdvanceEm, start, count).CopyTo(new Span<float>(AdvancesOf(entry), count));
            int* cpRun = CpRunOf(entry);
            for (int i = 0; i < count; i++) cpRun[i] = m_CpRun[start + i] - firstRun;
            ShapedGlyph* glyphs = GlyphsOf(entry);
            for (int g = 0; g < glyphCount; g++)
            {
                ShapedGlyph glyph = m_Shaped[firstGlyph + g];
                glyph.Cluster -= start;
                glyphs[g] = glyph;
            }
            StoredRun* runs = RunsOf(entry);
            for (int r = 0; r < runCount; r++)
            {
                Run run = m_Runs[firstRun + r];
                runs[r] = new StoredRun
                {
                    Start = run.Start - start, End = run.End - start, GlyphStart = run.GlyphStart - firstGlyph, GlyphCount = run.GlyphCount,
                    Instance = StoredInstance(run.Instance), Level = run.Level, UnitEm = run.UnitEm, SizeAbsolute = run.SizeAbsolute, SizeMul = run.SizeMul,
                    SizeAdd = run.SizeAdd, Script = run.Script, FaceFeature = run.FaceFeature, BaselineShift = run.BaselineShift,
                };
            }
            Insert(entry);
            LinkNewest(entry);
            s_ParagraphBytes += bytes;
        }
    }
}

using System;
using System.Buffers;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using FirstSymbol.Lettera.Unicode;
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
    internal sealed unsafe partial class TextEngine
    {
        sealed class ParagraphEntry
        {
            public long Hash, Bytes;
            public int Count, ChainId, BaseLevel, SizeMatch, Level, RunCount, GlyphCount;
            public string Language;
            public int[] Text, Shape, CpRun;
            public long[] Signature;
            public byte[] Levels;
            public bool[] Boundaries;
            public LineBreakOpportunity[] Breaks;
            public UnicodeScript[] Scripts;
            public float[] Advances;
            public Run[] Runs;
            public ShapedGlyph[] Glyphs;
            public ParagraphEntry Newer, Older;
        }

        const int ParagraphCacheMinimum = 8;

        static readonly Dictionary<long, ParagraphEntry> s_Paragraphs = new Dictionary<long, ParagraphEntry>();
        static ParagraphEntry s_Newest, s_Oldest;
        static long s_ParagraphBytes;

        internal static long ParagraphBytes => s_ParagraphBytes;

        long[] m_CpSignature = new long[64];
        bool m_SignaturesPlain;

        internal static void ClearParagraphCache()
        {
            foreach (ParagraphEntry entry in s_Paragraphs.Values) ReturnArrays(entry);
            s_Paragraphs.Clear();
            s_Newest = s_Oldest = null;
            s_ParagraphBytes = 0;
        }

        static void ReturnArrays(ParagraphEntry entry)
        {
            ArrayPool<int>.Shared.Return(entry.Text);
            ArrayPool<int>.Shared.Return(entry.Shape);
            ArrayPool<int>.Shared.Return(entry.CpRun);
            ArrayPool<long>.Shared.Return(entry.Signature);
            ArrayPool<byte>.Shared.Return(entry.Levels);
            ArrayPool<bool>.Shared.Return(entry.Boundaries);
            ArrayPool<LineBreakOpportunity>.Shared.Return(entry.Breaks);
            ArrayPool<UnicodeScript>.Shared.Return(entry.Scripts);
            ArrayPool<float>.Shared.Return(entry.Advances);
            ArrayPool<Run>.Shared.Return(entry.Runs, true);
            ArrayPool<ShapedGlyph>.Shared.Return(entry.Glyphs);
        }

        static void Unlink(ParagraphEntry entry)
        {
            if (entry.Newer != null) entry.Newer.Older = entry.Older;
            else s_Newest = entry.Older;
            if (entry.Older != null) entry.Older.Newer = entry.Newer;
            else s_Oldest = entry.Newer;
            entry.Newer = entry.Older = null;
        }

        static void LinkNewest(ParagraphEntry entry)
        {
            entry.Older = s_Newest;
            if (s_Newest != null) s_Newest.Newer = entry;
            s_Newest = entry;
            if (s_Oldest == null) s_Oldest = entry;
        }

        static void Forget(ParagraphEntry entry)
        {
            Unlink(entry);
            s_Paragraphs.Remove(entry.Hash);
            s_ParagraphBytes -= entry.Bytes;
            ReturnArrays(entry);
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

        bool TryRestoreParagraph(long hash, ref Paragraph paragraph, int start, int count, int baseLevel, string language)
        {
            if (!s_Paragraphs.TryGetValue(hash, out ParagraphEntry entry) || entry.Count != count || entry.ChainId != m_Chain.Id || entry.BaseLevel != baseLevel ||
                entry.SizeMatch != m_Attributes.SizeMatchOverride || entry.Language != language)
                return false;
            // The hash only finds a candidate. Only equal characters and attributes may use it, so a collision can never draw the wrong text.
            if (!new ReadOnlySpan<int>(m_Text, start, count).SequenceEqual(new ReadOnlySpan<int>(entry.Text, 0, count))) return false;
            for (int i = 0; i < count; i++)
                if ((m_SignaturesPlain ? 0 : m_CpSignature[start + i]) != entry.Signature[i])
                    return false;

            Unlink(entry);
            LinkNewest(entry);

            Array.Copy(entry.Shape, 0, m_ShapeText, start, count);
            Array.Copy(entry.Levels, 0, m_Levels, start, count);
            Array.Copy(entry.Boundaries, 0, m_Boundaries, start, count + 1);
            Array.Copy(entry.Breaks, 0, m_Breaks, start, count);
            Array.Copy(entry.Scripts, 0, m_Scripts, start, count);
            Array.Copy(entry.Advances, 0, m_CpAdvanceEm, start, count);
            paragraph.Level = entry.Level;

            int runBase = m_Runs.Count;
            for (int i = 0; i < count; i++) m_CpRun[start + i] = runBase + entry.CpRun[i];
            EnsureSize(ref m_Shaped, m_ShapedCount + entry.GlyphCount);
            for (int g = 0; g < entry.GlyphCount; g++)
            {
                ShapedGlyph glyph = entry.Glyphs[g];
                glyph.Cluster += start;
                m_Shaped[m_ShapedCount + g] = glyph;
            }
            // A run's language and features are indices of this text's own lists: the same values, by the check above, at maybe other places.
            LetteraAttributes.Cp[] chars = m_Attributes.Chars;
            for (int r = 0; r < entry.RunCount; r++)
            {
                Run run = entry.Runs[r];
                run.Start += start;
                run.End += start;
                run.GlyphStart += m_ShapedCount;
                run.Language = chars[run.Start].Language;
                run.Features = chars[run.Start].Features;
                m_Runs.Add(run);
            }
            m_ShapedCount += entry.GlyphCount;
            LetteraCaches.s_Stats.Hits++;
            return true;
        }

        void StoreParagraph(long hash, in Paragraph paragraph, int start, int count, int baseLevel, string language, int firstRun, int firstGlyph)
        {
            int runCount = m_Runs.Count - firstRun, glyphCount = m_ShapedCount - firstGlyph;
            for (int r = firstRun; r < m_Runs.Count; r++)
                if (m_Runs[r].Instance == null || m_Runs[r].Instance.Face.IsVirtual)
                    return;

            long bytes = count * (4 + 4 + 4 + 8 + 1 + 1 + 1 + 4 + 4) + runCount * 64 + glyphCount * 20 + 256;
            // One paragraph that would push out everything else is not worth keeping.
            if (bytes > LetteraCaches.ParagraphCacheBytes / 2) return;

            if (s_Paragraphs.TryGetValue(hash, out ParagraphEntry old)) Forget(old);
            while (s_ParagraphBytes + bytes > LetteraCaches.ParagraphCacheBytes && s_Oldest != null)
            {
                Forget(s_Oldest);
                LetteraCaches.s_Stats.Evictions++;
            }

            var entry = new ParagraphEntry
            {
                Hash = hash, Bytes = bytes, Count = count, ChainId = m_Chain.Id, BaseLevel = baseLevel, SizeMatch = m_Attributes.SizeMatchOverride, Level = paragraph.Level,
                RunCount = runCount, GlyphCount = glyphCount, Language = language,
                Text = ArrayPool<int>.Shared.Rent(count), Shape = ArrayPool<int>.Shared.Rent(count), CpRun = ArrayPool<int>.Shared.Rent(count),
                Signature = ArrayPool<long>.Shared.Rent(count), Levels = ArrayPool<byte>.Shared.Rent(count), Boundaries = ArrayPool<bool>.Shared.Rent(count + 1),
                Breaks = ArrayPool<LineBreakOpportunity>.Shared.Rent(count), Scripts = ArrayPool<UnicodeScript>.Shared.Rent(count), Advances = ArrayPool<float>.Shared.Rent(count),
                Runs = ArrayPool<Run>.Shared.Rent(Math.Max(runCount, 1)), Glyphs = ArrayPool<ShapedGlyph>.Shared.Rent(Math.Max(glyphCount, 1)),
            };
            Array.Copy(m_Text, start, entry.Text, 0, count);
            Array.Copy(m_ShapeText, start, entry.Shape, 0, count);
            if (m_SignaturesPlain) Array.Clear(entry.Signature, 0, count);
            else Array.Copy(m_CpSignature, start, entry.Signature, 0, count);
            Array.Copy(m_Levels, start, entry.Levels, 0, count);
            Array.Copy(m_Boundaries, start, entry.Boundaries, 0, count + 1);
            Array.Copy(m_Breaks, start, entry.Breaks, 0, count);
            Array.Copy(m_Scripts, start, entry.Scripts, 0, count);
            Array.Copy(m_CpAdvanceEm, start, entry.Advances, 0, count);
            for (int i = 0; i < count; i++) entry.CpRun[i] = m_CpRun[start + i] - firstRun;
            for (int g = 0; g < glyphCount; g++)
            {
                ShapedGlyph glyph = m_Shaped[firstGlyph + g];
                glyph.Cluster -= start;
                entry.Glyphs[g] = glyph;
            }
            for (int r = 0; r < runCount; r++)
            {
                Run run = m_Runs[firstRun + r];
                run.Start -= start;
                run.End -= start;
                run.GlyphStart -= firstGlyph;
                entry.Runs[r] = run;
            }
            s_Paragraphs[hash] = entry;
            LinkNewest(entry);
            s_ParagraphBytes += bytes;
        }
    }
}

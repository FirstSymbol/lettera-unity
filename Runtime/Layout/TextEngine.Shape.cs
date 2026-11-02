using System;
using System.Collections.Generic;
using FirstSymbol.Lettera.Unicode;
using UnityEngine;

namespace FirstSymbol.Lettera
{
    internal sealed unsafe partial class TextEngine
    {
        struct Paragraph
        {
            public int Start, ContentEnd, End;
            public int Level;
            public bool Pending;    // Not shaped yet: it stands as one line of an estimated height.
        }

        struct Run
        {
            public int Start, End, GlyphStart, GlyphCount;
            public FontInstance Instance;
            public byte Level;
            public float UnitEm;        // Em per font unit, including font scale and normalization.
            public float SizeAbsolute, SizeMul, SizeAdd;
            public short Language;
            public UnicodeScript Script;
            public short Features;      // Index into the attributes' feature sets, -1 none.
            public byte FaceFeature;    // A feature enabled for this face: 1 small caps, 2 superior, 3 inferior.
            public float BaselineShift; // Synthetic super- and subscript, in em of the enclosing size.
        }

        struct ShapedGlyph
        {
            public uint Glyph;
            public int Cluster, XAdvance, XOffset, YOffset;
        }

        static readonly Dictionary<string, byte[]> s_LanguageBytes = new Dictionary<string, byte[]>();

        FontChain m_Chain = FontChain.Empty;

        /// <summary>Fonts, stacks or settings changed: resolve faces again at the next shaping.</summary>
        public void InvalidateFonts() => LetteraFontChains.Invalidate();
        readonly List<Paragraph> m_Paragraphs = new List<Paragraph>();
        readonly List<Run> m_Runs = new List<Run>();
        ShapedGlyph[] m_Shaped = Array.Empty<ShapedGlyph>();
        static NativeGlyph[] s_NativeGlyphs = new NativeGlyph[256];
        int m_ShapedCount;
        byte[] m_Levels = Array.Empty<byte>();
        bool[] m_Boundaries = Array.Empty<bool>();
        LineBreakOpportunity[] m_Breaks = Array.Empty<LineBreakOpportunity>();
        UnicodeScript[] m_Scripts = Array.Empty<UnicodeScript>();
        int[] m_CpRun = Array.Empty<int>();
        // What shaping reads: the rendered text with case effects applied, so indices stay the same and CleanText keeps its case.
        int[] m_ShapeText = Array.Empty<int>();
        static readonly uint s_Smcp = LetteraNative.Tag("smcp"), s_Sups = LetteraNative.Tag("sups"), s_Subs = LetteraNative.Tag("subs");
        float[] m_CpAdvanceEm = Array.Empty<float>();

        readonly LetteraSegmenterScratch m_SegmenterScratch = new LetteraSegmenterScratch();

        bool IsParagraphSeparator(int i, out int width)
        {
            int cp = m_Text[i];
            width = 1;
            if (cp == '\r' && i + 1 < m_TextLength && m_Text[i + 1] == '\n') { width = 2; return true; }
            return cp == '\n' || cp == '\r' || cp == 0x85 || cp == 0x2029;
        }

        void ShapeText()
        {
            ShapeCount++;
            int length = m_TextLength;
            EnsureSize(ref m_Levels, length + 1);
            EnsureSize(ref m_Boundaries, length + 2);
            EnsureSize(ref m_Breaks, length + 1);
            EnsureSize(ref m_Scripts, length + 1);
            EnsureSize(ref m_CpRun, length + 1);
            EnsureSize(ref m_CpAdvanceEm, length + 1);
            EnsureSize(ref m_ShapeText, length + 1);
            LetteraTextData data = m_Host.Data;
            m_Chain = LetteraFontChains.Get(data.Font, data.FontStack);
            int direction = FieldDirection >= 0 ? FieldDirection : m_Attributes.DirectionOverride >= 0 ? m_Attributes.DirectionOverride
                : data.BaseDirection == LetteraDirection.LeftToRight ? 0 : data.BaseDirection == LetteraDirection.RightToLeft ? 1 : 2;
            int baseLevel = direction == 2 ? -1 : direction;
            // After an edit, the paragraphs before the one it touched stay as they are.
            int resume = ShapeResume(baseLevel, data);
            m_Deferring = DeferApplies(length, data);
            m_DeferBaseLevel = baseLevel;
            if (resume == 0) ForgetEstimates();

            // A paragraph left for later prepares its own characters when it is shaped.
            if (!m_Deferring)
            {
                LetteraAttributes chars = m_Attributes;
                for (int i = resume; i < length; i++)
                {
                    int cp = m_Text[i];
                    byte map = chars.At(i).CaseMap;
                    m_ShapeText[i] = map == 0 ? cp : map == 1 ? LetteraUnicode.ToUpper(cp) : LetteraUnicode.ToLower(cp);
                }
                Array.Clear(m_CpAdvanceEm, resume, length + 1 - resume);
            }
            else
            {
                m_CpAdvanceEm[length] = 0;
            }
            LetteraWordDictionaries.EnsureFromSettings();

            if (resume == 0)
            {
                m_Paragraphs.Clear();
                m_Runs.Clear();
                m_ShapedCount = 0;
            }
            EnsureExtras(length, resume);
            if (length == 0)
            {
                CountPending();
                return;
            }
            PrepareInlineShaping();
            PrepareShapeSignatures(length);

            bool parallel = resume == 0 && !m_Deferring && ShapesInParallel(length);
            if (parallel)
            {
                BaseLevelForSlots = baseLevel;
                ShapeParagraphsInParallel(length, baseLevel);
            }
            int start = parallel ? length + 1 : resume;
            while (start <= length)
            {
                int i = start;
                int separator = 0;
                while (i < length && !IsParagraphSeparator(i, out separator)) i++;
                if (i >= length) separator = 0;
                AddParagraph(start, i, i + separator, baseLevel);
                if (i >= length) break;
                start = i + separator;
                if (start == length)
                {
                    // Text ending in a separator ends with an empty last paragraph.
                    AddParagraph(length, length, length, baseLevel);
                    break;
                }
            }
            CountPending();
            if (m_Deferring && resume == 0) ShapeSample();
            RunAnalyzedHooks();
            RunShapedHooks();
            if (m_Attributes.HasLayout)
            {
                AddInlineExtras();
                ComputeExtras();
                PrepareVirtual();
            }
            else
            {
                m_ListItems.Clear();
                m_RubyItems.Clear();
            }
        }

        void AddParagraph(int start, int contentEnd, int end, int baseLevel)
        {
            var paragraph = new Paragraph { Start = start, ContentEnd = contentEnd, End = end, Level = baseLevel == 1 ? 1 : 0 };
            if (m_Deferring)
            {
                paragraph.Pending = true;
                m_Paragraphs.Add(paragraph);
                return;
            }
            ShapeParagraph(ref paragraph, baseLevel);
            // An empty paragraph has nothing to decide a direction with, so a caret on it stays on the side the text before it reads from.
            if (contentEnd == start && baseLevel < 0 && m_Paragraphs.Count > 0) paragraph.Level = m_Paragraphs[m_Paragraphs.Count - 1].Level;
            m_Paragraphs.Add(paragraph);
        }

        void ShapeParagraph(ref Paragraph paragraph, int baseLevel)
        {
            int start = paragraph.Start, contentEnd = paragraph.ContentEnd;
            int count = paragraph.End - start;
            bool cacheable = count > 0 && ParagraphCacheApplies(count);
            long hash = 0;
            string language = null;
            int firstRun = m_Runs.Count, firstGlyph = m_ShapedCount;
            if (cacheable)
            {
                language = LanguageOf(-1);
                hash = HashParagraph(start, count, baseLevel, language);
                if (TryRestoreParagraph(hash, ref paragraph, start, count, baseLevel, language)) return;
                LetteraCaches.s_Stats.Misses++;
            }
            if (count > 0)
            {
                var text = new ReadOnlySpan<int>(m_Text, start, count);
                GraphemeBreaker.FindBoundaries(text, new Span<bool>(m_Boundaries, start, count + 1));
                if (MaskGlyph != 0)
                {
                    MaskParagraph(start, contentEnd);
                    text = new ReadOnlySpan<int>(m_ShapeText, start, count);
                }
                paragraph.Level = Bidi.ResolveParagraph(text, baseLevel, new Span<byte>(m_Levels, start, count));
                LineBreaker.FindBreaks(text, new Span<LineBreakOpportunity>(m_Breaks, start, count));
                if (LetteraWordSegmenters.Count != 0) LetteraWordSegmenters.ApplyToBreaks(text, new ReadOnlySpan<bool>(m_Boundaries, start, count + 1), new Span<LineBreakOpportunity>(m_Breaks, start, count), m_SegmenterScratch);
                ScriptItemizer.Resolve(text, new Span<UnicodeScript>(m_Scripts, start, count));
                BuildRuns(paragraph);
                if (cacheable) StoreParagraph(hash, paragraph, start, count, baseLevel, language, firstRun, firstGlyph);
            }
        }

        string LanguageOf(short index)
        {
            if (index >= 0) return m_Attributes.Languages[index];
            string language = m_Host.Data.Language;
            return !string.IsNullOrEmpty(language) && LanguageTags.IsWellFormed(language) ? language : null;
        }

        void BuildRuns(in Paragraph paragraph)
        {
            LetteraAttributes attrs = m_Attributes;
            var current = new Run { Start = -1 };
            int i = paragraph.Start;
            FontFace primary = m_Chain.PrimaryFace;
            // With no range, every character has the same attributes, so a codepoint whose face matches the run's continues it with no question asked.
            bool plain = m_SignaturesPlain && MaskGlyph == 0;
            if (plain) PrepareRunMemo();
            FontFace lastFace = null;
            FontChain.Family lastOwner = null;
            while (i < paragraph.End)
            {
                int clusterEnd = i + 1;
                while (clusterEnd < paragraph.End && !m_Boundaries[clusterEnd]) clusterEnd++;
                int memo = -1;
                if (plain && clusterEnd == i + 1)
                {
                    int single = m_ShapeText[i];
                    memo = single & (RunMemoSize - 1);
                    if (s_MemoCp[memo] == single)
                    {
                        if (current.Start >= 0 && s_MemoFace[memo] == lastFace && s_MemoOwner[memo] == lastOwner && !s_MemoEmoji[memo] && current.Level == m_Levels[i] && current.Script == m_Scripts[i])
                        {
                            i = clusterEnd;
                            continue;
                        }
                        memo = -1;
                    }
                }
                ref LetteraAttributes.Cp attr = ref attrs.At(i);
                string family = attr.Family >= 0 ? m_Attributes.FamilyRequests[attr.Family] : null;
                string language = LanguageOf(attr.Language);
                bool emoji = MaskGlyph == 0 && EmojiClassifier.IsEmojiCluster(m_Text, i, clusterEnd - i);
                FontFace face = m_Chain.Resolve(new ReadOnlySpan<int>(m_ShapeText, i, clusterEnd - i), emoji, family, language, out FontChain.Family owner);
                if (memo >= 0)
                {
                    s_MemoCp[memo] = m_ShapeText[i];
                    s_MemoFace[memo] = face;
                    s_MemoOwner[memo] = owner;
                    s_MemoEmoji[memo] = emoji;
                }
                lastFace = face;
                lastOwner = owner;
                AxisSet axes = attr.Axes >= 0 ? m_Attributes.AxisSets[attr.Axes] : null;
                FontInstance instance = face == null ? null : FontChain.Style(face, owner, attr.Weight, attr.BoldMode, attr.Italic, attr.Slant, axes);

                float sizeMul = attr.SizeMul, shift = 0;
                byte faceFeature = 0;
                if (instance != null && (attr.SmallCaps > 0 || attr.SuperSub != 0))
                    ResolveFaceFeatures(instance.Face, ref attr, i, clusterEnd, ref sizeMul, ref shift, ref faceFeature);

                bool same = current.Start >= 0 && current.Instance == instance && current.Level == m_Levels[i] && current.Script == m_Scripts[i] &&
                            current.Language == attr.Language && current.SizeAbsolute == attr.SizeAbsolute && current.SizeMul == sizeMul && current.SizeAdd == attr.SizeAdd &&
                            current.Features == attr.Features && current.FaceFeature == faceFeature && current.BaselineShift == shift;
                if (!same)
                {
                    if (current.Start >= 0) FinishRun(ref current, i, paragraph);
                    current = new Run
                    {
                        Start = i, Instance = instance, Level = m_Levels[i], Script = m_Scripts[i], Language = attr.Language,
                        SizeAbsolute = attr.SizeAbsolute, SizeMul = sizeMul, SizeAdd = attr.SizeAdd,
                        UnitEm = instance == null ? 0 : UnitEm(instance.Face, owner, primary, m_Attributes.SizeMatchOverride),
                        Features = attr.Features, FaceFeature = faceFeature, BaselineShift = shift,
                    };
                }
                i = clusterEnd;
            }
            if (current.Start >= 0) FinishRun(ref current, paragraph.End, paragraph);
        }

        // What face each codepoint of a plain text took, by its low bits, for the chain and language it was asked with.
        const int RunMemoSize = 256;
        static readonly int[] s_MemoCp = NewMemo();
        static readonly FontFace[] s_MemoFace = new FontFace[RunMemoSize];
        static readonly FontChain.Family[] s_MemoOwner = new FontChain.Family[RunMemoSize];
        static readonly bool[] s_MemoEmoji = new bool[RunMemoSize];
        static int s_MemoChain = -1;
        static string s_MemoLanguage;

        static int[] NewMemo()
        {
            var memo = new int[RunMemoSize];
            for (int i = 0; i < RunMemoSize; i++) memo[i] = -1;
            return memo;
        }

        void PrepareRunMemo()
        {
            string language = LanguageOf(-1);
            if (s_MemoChain == m_Chain.Id && (object)s_MemoLanguage == language) return;
            s_MemoChain = m_Chain.Id;
            s_MemoLanguage = language;
            for (int i = 0; i < RunMemoSize; i++)
            {
                s_MemoCp[i] = -1;
                s_MemoFace[i] = null;
                s_MemoOwner[i] = null;
            }
        }

        // Decided per face and per character: the same range can use a font's real small caps and synthesize them for a fallback.
        void ResolveFaceFeatures(FontFace face, ref LetteraAttributes.Cp attr, int start, int end, ref float sizeMul, ref float shift, ref byte faceFeature)
        {
            int cp = m_ShapeText[start];
            if (attr.SmallCaps > 0)
            {
                int upper = LetteraUnicode.ToUpper(cp);
                if (upper != cp)
                {
                    if (face.ChangesGlyph(s_Smcp, cp))
                    {
                        faceFeature = 1;
                    }
                    else
                    {
                        for (int i = start; i < end; i++) m_ShapeText[i] = LetteraUnicode.ToUpper(m_ShapeText[i]);
                        sizeMul *= attr.SmallCaps;
                    }
                }
            }
            if (attr.SuperSub == 0) return;
            bool super = attr.SuperSub == 1;
            if (face.ChangesGlyph(super ? s_Sups : s_Subs, cp))
            {
                faceFeature = (byte)(super ? 2 : 3);
                return;
            }
            FaceMetrics metrics = face.Metrics;
            float upem = metrics.Upem;
            int size = super ? metrics.SuperSize : metrics.SubSize, offset = super ? metrics.SuperOffset : metrics.SubOffset;
            float scale = size > 0 ? size / upem : 0.65f;
            float distance = offset > 0 ? offset / upem : 0.35f;
            sizeMul *= scale;
            shift = (super ? distance : -distance) * attr.SizeMul;
        }

        static float UnitEm(FontFace face, FontChain.Family owner, FontFace primary, int metricOverride)
        {
            float scale = 1f / face.Metrics.Upem;
            LetteraFontAsset asset = owner?.Asset;
            if (asset != null) scale *= asset.FontScale;
            bool normalize = asset == null || asset.NormalizeSize;
            if (normalize && primary != null && face != primary && !face.IsColor)
            {
                bool useCap = metricOverride >= 0 ? metricOverride == (int)LetteraSizeMatchMetric.CapHeight : LetteraSettings.Instance.FontSizeMatchMetric == LetteraSizeMatchMetric.CapHeight;
                float wanted = useCap ? (float)primary.Metrics.CapHeight / primary.Metrics.Upem : (float)primary.Metrics.XHeight / primary.Metrics.Upem;
                float actual = useCap ? (float)face.Metrics.CapHeight / face.Metrics.Upem : (float)face.Metrics.XHeight / face.Metrics.Upem;
                if (wanted > 0 && actual > 0) scale *= Mathf.Clamp(wanted / actual, 0.75f, 1.35f);
            }
            return scale;
        }

        void FinishRun(ref Run run, int end, in Paragraph paragraph)
        {
            run.End = end;
            int index = m_Runs.Count;
            for (int i = run.Start; i < end; i++) m_CpRun[i] = index;
            run.GlyphStart = m_ShapedCount;
            // A face drawn by the browser only exists on the web, where nothing runs in parallel, so a deferred run always has a real one.
            if (run.Instance != null && m_DeferShaping) DeferShaping(ref run);
            else if (run.Instance != null) ShapeRun(ref run, paragraph);
            m_Runs.Add(run);
        }

        void ShapeBrowserEmoji(ref Run run)
        {
            // One glyph per cluster; right-to-left runs list glyphs in visual order like shaped ones.
            int count = 0;
            int i = run.Start;
            while (i < run.End)
            {
                int end = i + 1;
                while (end < run.End && !m_Boundaries[end]) end++;
                count++;
                i = end;
            }
            EnsureSize(ref m_Shaped, m_ShapedCount + count);
            bool rtl = (run.Level & 1) != 0;
            int slot = rtl ? m_ShapedCount + count - 1 : m_ShapedCount;
            i = run.Start;
            while (i < run.End)
            {
                int end = i + 1;
                while (end < run.End && !m_Boundaries[end]) end++;
                uint glyph = BrowserEmoji.GlyphFor(new ReadOnlySpan<int>(m_Text, i, end - i), out int advance);
                m_Shaped[slot] = new ShapedGlyph { Glyph = glyph, Cluster = i, XAdvance = advance };
                m_CpAdvanceEm[i] += advance * run.UnitEm;
                slot += rtl ? -1 : 1;
                i = end;
            }
            run.GlyphCount = count;
            m_ShapedCount += count;
        }

        void ShapeRun(ref Run run, in Paragraph paragraph)
        {
            if (run.Instance.Face.IsVirtual)
            {
                ShapeBrowserEmoji(ref run);
                return;
            }
            string language = LanguageOf(run.Language);
            byte[] languageBytes = null;
            if (language != null && !s_LanguageBytes.TryGetValue(language, out languageBytes))
            {
                languageBytes = FontFace.NullTerminated(language);
                s_LanguageBytes[language] = languageBytes;
            }

            LetteraCaches.s_Stats.ShapeCalls++;
            int direction = (run.Level & 1) != 0 ? 5 : 4;
            uint script = LetteraUnicode.ScriptToOpenTypeTag(run.Script);
            FeatureSet set = run.Features >= 0 ? m_Attributes.FeatureSets[run.Features] : null;
            int featureCount = (set?.Tags.Length ?? 0) + (run.FaceFeature != 0 ? 1 : 0);
            NativeFeature* features = stackalloc NativeFeature[Math.Min(featureCount, 64) + 1];
            int used = 0;
            if (set != null)
                for (int f = 0; f < set.Tags.Length && used < 63; f++)
                    features[used++] = new NativeFeature { Tag = set.Tags[f], Value = set.Values[f], Start = 0, End = uint.MaxValue };
            if (run.FaceFeature != 0)
                features[used++] = new NativeFeature { Tag = run.FaceFeature == 1 ? s_Smcp : run.FaceFeature == 2 ? s_Sups : s_Subs, Value = 1, Start = 0, End = uint.MaxValue };

            int count;
            fixed (int* text = &m_ShapeText[paragraph.Start])
            fixed (byte* lang = languageBytes)
            {
                while (true)
                {
                    fixed (NativeGlyph* output = s_NativeGlyphs)
                        count = LetteraNative.lt_shape(run.Instance.Font, text, paragraph.End - paragraph.Start, run.Start - paragraph.Start, run.End - run.Start,
                            direction, script, lang, used > 0 ? features : null, used, output, s_NativeGlyphs.Length);
                    if (count >= 0) break;
                    s_NativeGlyphs = new NativeGlyph[-count * 2];
                }
            }

            EnsureSize(ref m_Shaped, m_ShapedCount + count);
            for (int i = 0; i < count; i++)
            {
                ref NativeGlyph g = ref s_NativeGlyphs[i];
                int cluster = (int)g.Cluster + paragraph.Start;
                m_Shaped[m_ShapedCount + i] = new ShapedGlyph { Glyph = g.Glyph, Cluster = cluster, XAdvance = g.XAdvance, XOffset = g.XOffset, YOffset = g.YOffset };
                m_CpAdvanceEm[cluster] += g.XAdvance * run.UnitEm;
            }
            run.GlyphCount = count;
            m_ShapedCount += count;
        }
    }
}

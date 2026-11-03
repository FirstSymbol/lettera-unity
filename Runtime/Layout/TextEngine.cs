using System;
using System.Collections.Generic;
using UnityEngine;

namespace FirstSymbol.Lettera
{
    /// <summary>What selection and editing need from either text component.</summary>
    internal interface ITextComponent
    {
        TextEngine SharedEngine { get; }
        RectTransform TextRect { get; }
        bool IsVisible { get; }
        string SourceText { get; set; }

        /// <summary>Writes a field's edited source.</summary>
        void WriteSource(ReadOnlySpan<char> source);

        void Dirty(DirtyLevel level);
        event Action SourceWritten;
        event Action<LetteraChanges> Rebuilt;
    }

    /// <summary>What the engine needs from the component that owns it.</summary>
    internal interface ITextHost
    {
        LetteraTextData Data { get; }
        Rect LayoutRect { get; }
        Color32 BaseColor { get; }
        float RasterScale { get; }
        bool ClipsOverflow { get; }
        Component Owner { get; }

        /// <summary>Something other than the component changed what the text shows.</summary>
        void RequestRebuild(DirtyLevel level);

        /// <summary>A pose pass wrote vertex data.</summary>
        void PoseChanged();

        /// <summary>Whether drawing this now shows anything: a hidden or culled text does no pose work.</summary>
        bool IsDrawn { get; }

        /// <summary>A text this can clip to scroll within.</summary>
        bool SupportsMarquee { get; }

        /// <summary>The ranges that take input changed.</summary>
        void ActiveRangesChanged(bool any);

        /// <summary>Glyphs with an outline draw some other way: no tile is made for them, and no quad.</summary>
        bool SolidsOnly { get; }

        /// <summary>Styles only this kind of host has, read before all others. Null for most.</summary>
        IReadOnlyList<LetteraStyle> HostStyles { get; }
    }

    /// <summary>
    /// The text pipeline both components share. Each pass is a separate partial file, in pipeline order:
    /// parse (markup), shape (Unicode analysis, fonts, shaping), layout (lines, auto size, eliding),
    /// positions (alignment, bidi order) and mesh. A rebuild runs only the passes its dirty level asks for.
    /// </summary>
    internal sealed partial class TextEngine
    {
        readonly ITextHost m_Host;
        DirtyLevel m_Dirty = DirtyLevel.Parse;
        LetteraTextData.Snapshot m_Snapshot;

        // Runtime text overrides the serialized field until the field is set again.
        char[] m_RuntimeChars = Array.Empty<char>();
        int m_RuntimeLength;
        string m_RuntimeText;
        bool m_HasRuntimeText;
        ILetteraTextResolver m_Resolver;
        string m_ResolvedText = "";

        public TextEngine(ITextHost host)
        {
            m_SolidsOnly = host.SolidsOnly;
            m_Host = host;
        }

        public DirtyLevel Dirty => m_Dirty;
        public float CurrentFontSize { get; private set; }
        public LetteraChanges LastChanges { get; private set; }
        public int RebuildCount { get; private set; }

        /// <summary>Rebuilds of every text, for telling a quiet period.</summary>
        internal static int WorkCounter;

        // How often each expensive pass ran, to prove that a change stayed at its level.
        public int ParseCount { get; private set; }
        public int ShapeCount { get; private set; }

        public void SetDirty(DirtyLevel level)
        {
            if (level == DirtyLevel.Pose)
            {
                m_PoseDirty = true;
                return;
            }
            m_MeshReady = false;
            m_DirtyStamp++;
            if (level > m_Dirty) m_Dirty = level;
        }

        static readonly Unity.Profiling.ProfilerMarker s_ParseMarker = new Unity.Profiling.ProfilerMarker("Lettera.Parse"),
            s_ShapeMarker = new Unity.Profiling.ProfilerMarker("Lettera.Shape"),
            s_LayoutMarker = new Unity.Profiling.ProfilerMarker("Lettera.Layout"),
            s_PositionsMarker = new Unity.Profiling.ProfilerMarker("Lettera.Positions"),
            s_MeshMarker = new Unity.Profiling.ProfilerMarker("Lettera.Mesh");

        // Where a rebuild's time goes, for a test that splits it: Parse, Shape, Layout, Positions, Mesh.
        internal static readonly long[] PassTicks = new long[5];
        internal static bool TimePasses;
        static long PassStart() => TimePasses ? System.Diagnostics.Stopwatch.GetTimestamp() : 0;

        static void PassEnd(int pass, long start)
        {
            if (TimePasses) System.Threading.Interlocked.Add(ref PassTicks[pass], System.Diagnostics.Stopwatch.GetTimestamp() - start);
        }

        bool m_Reapply, m_SemanticsStale;

        /// <summary>A rebuild moved or changed what a semantic tree reads.</summary>
        public event Action SemanticsChanged;

        /// <summary>An effect's parameter changed: runs the effects again on the ranges already found, then the passes from a level up.</summary>
        public void ReapplyEffects(DirtyLevel level)
        {
            if (level == DirtyLevel.Pose)
            {
                m_PoseDirty = true;
                return;
            }
            m_Reapply = true;
            SetDirty(level);
        }

        void ReapplyIfNeeded()
        {
            if (!m_Reapply) return;
            m_Reapply = false;
            ApplyEffects(m_MatchContext);
        }

        /// <summary>Clears any runtime override; the serialized text applies again.</summary>
        public void ClearRuntimeText()
        {
            m_RuntimeText = null;
            m_RuntimeLength = 0;
            m_HasRuntimeText = false;
        }

        /// <summary>Sets runtime text. Returns false when the characters are the ones already shown.</summary>
        public bool SetRuntimeText(ReadOnlySpan<char> text)
        {
            if (m_HasRuntimeText && text.SequenceEqual(new ReadOnlySpan<char>(m_RuntimeChars, 0, m_RuntimeLength))) return false;
            if (m_RuntimeChars.Length < text.Length) m_RuntimeChars = new char[Math.Max(text.Length, m_RuntimeChars.Length * 2)];
            text.CopyTo(m_RuntimeChars);
            m_RuntimeLength = text.Length;
            m_RuntimeText = null;
            m_HasRuntimeText = true;
            return true;
        }

        public string RawText
        {
            get
            {
                if (m_HasRuntimeText) return m_RuntimeText ?? (m_RuntimeText = new string(m_RuntimeChars, 0, m_RuntimeLength));
                FlushPendingSource();
                return m_Host.Data.Text ?? "";
            }
        }

        // What gets parsed, without building a string for runtime text or a field's edit.
        ReadOnlySpan<char> RawChars => m_HasRuntimeText ? new ReadOnlySpan<char>(m_RuntimeChars, 0, m_RuntimeLength) :
            m_SourcePending ? new ReadOnlySpan<char>(m_PendingChars, 0, m_PendingLength) : (m_Host.Data.Text ?? "").AsSpan();

        /// <summary>The source as it stands, with nothing made for it.</summary>
        internal ReadOnlySpan<char> SourceChars => RawChars;

        // A field's edit in play, standing for the host's serialized text until something reads that. Two buffers, so the next edit is compared with this one.
        char[] m_PendingChars = Array.Empty<char>(), m_PendingNext = Array.Empty<char>();
        int m_PendingLength;
        bool m_SourcePending;

        /// <summary>Lets a test see in edit mode what a field does in play.</summary>
        internal static bool PendingSourceInEditMode;

        /// <summary>False when the characters are the source already.</summary>
        internal bool SetPendingSource(ReadOnlySpan<char> source)
        {
            int length = source.Length;
            if (m_PendingNext.Length < length) m_PendingNext = new char[Math.Max(length + 64, m_PendingNext.Length * 2)];
            source.CopyTo(new Span<char>(m_PendingNext, 0, length));
            if (new ReadOnlySpan<char>(m_PendingNext, 0, length).SequenceEqual(RawChars)) return false;
            char[] swap = m_PendingChars;
            m_PendingChars = m_PendingNext;
            m_PendingNext = swap;
            m_PendingLength = length;
            m_SourcePending = true;
            return true;
        }

        /// <summary>Makes the host's string from a waiting edit.</summary>
        internal void FlushPendingSource()
        {
            if (!m_SourcePending) return;
            m_SourcePending = false;
            string text = new string(m_PendingChars, 0, m_PendingLength);
            m_Host.Data.Text = text;
            // What the last rebuild saw is this text: a check of serialized fields finds no change in it.
            m_Snapshot.Text = text;
        }

        internal bool DropPendingSource()
        {
            bool was = m_SourcePending;
            m_SourcePending = false;
            return was;
        }

        public string ResolvedText => m_Resolver != null ? m_ResolvedText : RawText;
        public string RenderedSourceText => ResolvedText;

        public ILetteraTextResolver Resolver
        {
            get => m_Resolver;
            set => m_Resolver = value;
        }

        public LetteraTextOverride TextOverride =>
            m_HasRuntimeText && m_Resolver != null ? LetteraTextOverride.Both :
            m_HasRuntimeText ? LetteraTextOverride.RuntimeText :
            m_Resolver != null ? LetteraTextOverride.Resolver : LetteraTextOverride.None;

        /// <summary>Raises the level a serialized write needs (Inspector, Undo, prefab revert, Animator).</summary>
        public void SyncSerializedFields()
        {
            DirtyLevel level = m_Host.Data.Compare(m_Snapshot);
            if (level == DirtyLevel.None) return;
            if (level == DirtyLevel.Parse && m_Snapshot.Text != m_Host.Data.Text) ClearRuntimeText();
            SetDirty(level);
            m_Snapshot = m_Host.Data.Take();
        }

        /// <summary>The native fonts have been released and a domain is on its way out: nothing shapes until they are back.</summary>
        internal static bool FontsGone;

        /// <summary>Runs the passes the dirty level needs. Returns true when the mesh arrays changed.</summary>
        public bool Rebuild()
        {
            if (FontsGone) return false;
            m_Snapshot = m_Host.Data.Take();
            DirtyLevel level = m_Dirty;
            m_Dirty = DirtyLevel.None;
            LastChanges = m_EarlyChanges;
            if (level == DirtyLevel.None) return false;
            m_EarlyChanges = LetteraChanges.None;
            RebuildCount++;
            WorkCounter++;
            m_InPasses = true;
            try { RunPasses(level); }
            finally
            {
                m_InPasses = false;
                m_MeshReady = false;
            }
            return true;
        }

        // Inside a rebuild a query finishes nothing: what it reads is what the passes are making.
        bool m_InPasses;

        void RunPasses(DirtyLevel level)
        {
            if (level >= DirtyLevel.Parse)
            {
                using (s_ParseMarker.Auto()) { long t = PassStart(); Parse(); PassEnd(0, t); }
                LastChanges |= LetteraChanges.Text;
            }
            else
            {
                ReapplyIfNeeded();
            }
            if (level >= DirtyLevel.Shape)
                using (s_ShapeMarker.Auto()) { long t = PassStart(); ShapeText(); PassEnd(1, t); }
            if (level >= DirtyLevel.Layout)
            {
                using (s_LayoutMarker.Auto()) { long t = PassStart(); RunLayout(); PassEnd(2, t); }
                LastChanges |= LetteraChanges.Layout;
            }
            if (level >= DirtyLevel.Positions)
            {
                using (s_PositionsMarker.Auto()) { long t = PassStart(); PositionGlyphs(); PassEnd(3, t); }
                LastChanges |= LetteraChanges.Positions;
            }
            if (level >= DirtyLevel.Mesh)
            {
                if (level != DirtyLevel.Mesh || !m_MeshReady)
                    using (s_MeshMarker.Auto()) { long t = PassStart(); BuildMesh(); PassEnd(4, t); }
                LastChanges |= LetteraChanges.Mesh;
            }
            else if (level == DirtyLevel.Alpha || level == DirtyLevel.Tint)
            {
                ApplyBaseColor();
                if (m_WantsPose) RunPosePass();
                LastChanges |= LetteraChanges.Mesh;
            }
            ApplyHookStale();
            if (level >= DirtyLevel.Mesh) RaiseInlinePlaced();
            AfterWindowPasses();
            PlaceWindowAgainIfStale();
            if (m_SemanticsStale)
            {
                m_SemanticsStale = false;
                SemanticsChanged?.Invoke();
            }
        }

        // What passes run ahead of the frame's rebuild changed, for the rebuild to report.
        LetteraChanges m_EarlyChanges;

        /// <summary>Makes glyph positions current, for caret and selection queries before the frame's rebuild. The mesh waits for it.</summary>
        public void EnsurePositioned()
        {
            if (m_PendingCount > 0 && m_Dirty < DirtyLevel.Shape && !m_InPasses) FinishForQuery();
            if (m_Dirty < DirtyLevel.Positions) return;
            EnsureMeasured();
            if (m_Dirty >= DirtyLevel.Layout)
            {
                RunLayout();
                m_EarlyChanges |= LetteraChanges.Layout;
            }
            PositionGlyphs();
            m_EarlyChanges |= LetteraChanges.Positions;
            m_Dirty = DirtyLevel.Mesh;
        }

        /// <summary>Makes parsing and shaping current, for queries before the frame's rebuild.</summary>
        public void EnsureMeasured() => EnsureMeasured(true);

        void EnsureMeasured(bool exact)
        {
            if (m_Dirty >= DirtyLevel.Parse)
            {
                Parse();
                m_EarlyChanges |= LetteraChanges.Text;
                m_Dirty = DirtyLevel.Shape;
            }
            else
            {
                ReapplyIfNeeded();
            }
            if (m_Dirty >= DirtyLevel.Shape)
            {
                ShapeText();
                m_Dirty = DirtyLevel.Layout;
            }
            if (exact && m_PendingCount > 0 && !m_InPasses) FinishForQuery();
        }

        public bool HasElideRequests => m_Attributes.Elides.Count > 0;

        sealed class MeasureHost : ITextHost
        {
            public LetteraTextData Data { get; set; }
            public Rect LayoutRect { get; set; }
            public Color32 BaseColor => new Color32(255, 255, 255, 255);
            public float RasterScale => 1f;
            public bool ClipsOverflow => false;
            public Component Owner { get; set; }
            public void RequestRebuild(DirtyLevel level) { }
            public void PoseChanged() { }
            public bool IsDrawn => false;
            public bool SupportsMarquee => false;
            public void ActiveRangesChanged(bool any) { }
            public bool SolidsOnly => false;
            public IReadOnlyList<LetteraStyle> HostStyles => null;
        }

        /// <summary>Runs every pass of a rebuild once, for what the first text of a session would otherwise pay for. No component is involved and nothing is drawn.</summary>
        internal static void PrepareFor(LetteraFontAsset font, LetteraFontStack stack, string characters, float size)
        {
            var data = new LetteraTextData { Font = font, FontStack = stack, FontSize = size, Text = characters };
            // The characters go in as they are: what looks like a tag is still what was asked for.
            var engine = new TextEngine(new MeasureHost { Data = data, LayoutRect = new Rect(0, 0, 4096, 4096) }) { Literal = true };
            engine.Rebuild();
            engine.ReleaseCells();
        }

        /// <summary>Measures a text without touching what a component displays.</summary>
        public static Vector2 MeasureWith(Component owner, LetteraTextData data, string text, float size, float width, float height, bool wrap, bool autoSize, float minSize, float maxSize)
        {
            LetteraTextData copy = data.Clone();
            copy.Text = text ?? "";
            var engine = new TextEngine(new MeasureHost { Data = copy, Owner = owner });
            Vector2 measured = engine.Measure(size, width, height, wrap, autoSize, minSize, maxSize);
            engine.ReleaseCells();
            return measured;
        }

        // What few texts ever use starts as one shared empty collection, and the text's own comes with the first write. A shared one is only read and cleared.
        static class Rare<T>
        {
            public static readonly List<T> List = new List<T>();
        }

        static class RareValues<T> where T : struct
        {
            public static readonly ValueList<T> List = new ValueList<T>();
        }

        static class Rare<TKey, TValue>
        {
            public static readonly Dictionary<TKey, TValue> Map = new Dictionary<TKey, TValue>();
        }

        /// <summary>
        /// Sets up what the first engine of a session needs whatever its text: some fifty generic types of its own and of its attributes, 10 ms of the
        /// first text on IL2CPP, and the native library. None of it touches Unity, so a player with spare cores does it on a thread of its own as it starts.
        /// The engine's own statics are left to the main thread: a type whose set-up failed on another thread would stay failed.
        /// </summary>
        internal static void PrepareTypes()
        {
            _ = LetteraAttributes.Plain;
            _ = RareValues<(LetteraEffect effect, int start, int end)>.List;
            _ = Rare<ActiveRange>.List;
            _ = RareValues<CoverQuad>.List;
            _ = RareValues<ExtraLine>.List;
            _ = RareValues<ExtraQuad>.List;
            _ = Rare<IHoldSource>.List;
            _ = Rare<InlineItem>.List;
            _ = Rare<LetteraStyle, LetteraStyle>.Map;
            _ = RareValues<ListItem>.List;
            _ = RareValues<MathSpecial>.List;
            _ = RareValues<MotionPhase>.List;
            _ = RareValues<MotionSpan>.List;
            _ = RareValues<OldRange>.List;
            _ = RareValues<PlacedGlyph>.List;
            _ = RareValues<PoseBody>.List;
            _ = RareValues<PoseLink>.List;
            _ = Rare<Rect>.List;
            _ = RareValues<Removal>.List;
            _ = RareValues<RevealBody>.List;
            _ = Rare<RevealEffect>.List;
            _ = RareValues<RevealSpan>.List;
            _ = RareValues<RubyItem>.List;
            _ = RareValues<ShapedGlyph>.List;
            _ = RareValues<SolidBox>.List;
            _ = Rare<SymbolSet>.List;
            _ = Rare<Vector2Int>.List;
            _ = Rare<int, bool>.Map;
            _ = Rare<int, float>.Map;
            _ = Rare<int, int>.Map;
            _ = Rare<int>.List;
            _ = Rare<long, int>.Map;
            _ = Rare<long>.List;
            _ = new List<LetteraLine>();
            _ = new List<LayerGroup>();
            _ = new ValueList<Paragraph>();
            _ = new ValueList<Run>();
            _ = new List<MeshSegment>();
            _ = LetteraNative.Version;
            // The Unicode tables and the analyzers, which workers run too: the first paragraph of a session paid for them.
            ReadOnlySpan<int> sample = stackalloc int[] { 'H', 'i', ' ', 0x05D0, 10 };
            Span<bool> boundaries = stackalloc bool[6];
            Span<byte> levels = stackalloc byte[5];
            Span<FirstSymbol.Lettera.Unicode.LineBreakOpportunity> breaks = stackalloc FirstSymbol.Lettera.Unicode.LineBreakOpportunity[5];
            Span<FirstSymbol.Lettera.Unicode.UnicodeScript> scripts = stackalloc FirstSymbol.Lettera.Unicode.UnicodeScript[5];
            FirstSymbol.Lettera.Unicode.GraphemeBreaker.FindBoundaries(sample, boundaries);
            FirstSymbol.Lettera.Unicode.Bidi.ResolveParagraph(sample, -1, levels);
            FirstSymbol.Lettera.Unicode.LineBreaker.FindBreaks(sample, breaks);
            FirstSymbol.Lettera.Unicode.ScriptItemizer.Resolve(sample, scripts);
            _ = FirstSymbol.Lettera.Unicode.LetteraUnicode.ToUpper('a');
            // A key that is an enum or a struct gets its comparer made by reflection at its first lookup on IL2CPP, a millisecond or so each,
            // and the first text looks up several: settings gates, word lists, font chains, a face's sizes.
            Keyed<LetteraNativeFeature>();
            Keyed<LetteraDictionaryScript>();
            Keyed<FirstSymbol.Lettera.Unicode.UnicodeScript>();
            Keyed<(long, int)>();
            _ = new Dictionary<LetteraNativeFeature, string>();
            _ = new Dictionary<LetteraDictionaryScript, FirstSymbol.Lettera.Unicode.LetteraWordAutomaton>();
            _ = new Dictionary<FirstSymbol.Lettera.Unicode.UnicodeScript, FirstSymbol.Lettera.Unicode.ILetteraWordSegmenter>();
            _ = new Dictionary<(long, int), FontInstance>();
            _ = new HashSet<LetteraFontStack>();
            _ = new List<Vector3>();
            _ = new List<Color32>();
            _ = new List<Vector4>();
            _ = new List<LetteraText>();
            LetteraFontChains.PrepareTypes();
            GlyphAtlas.Types.Prepare();
        }

        static void Keyed<T>()
        {
            var set = new HashSet<T> { default };
            _ = set.Contains(default);
            foreach (T _ in set) { }
        }

#if !UNITY_WEBGL || UNITY_EDITOR
        // Before the first scene, so that a project can still set LetteraWorkers.Mode to Inline from its own subsystem registration and keep threads out.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void PrepareTypesAside()
        {
            if (Application.isEditor || LetteraWorkers.Mode == LetteraWorkerMode.Inline || SystemInfo.processorCount <= 2) return;
            // The system's font files are listed there too, so the first character no font of the project has opens a file and lists no folder.
            Action discover = LetteraSystemFonts.DiscoveryAside();
            var thread = new System.Threading.Thread(() =>
            {
                try { PrepareTypes(); }
                catch (Exception) { }
                try { discover?.Invoke(); }
                catch (Exception) { }
            }) { IsBackground = true, Name = "Lettera types" };
            thread.Start();
        }
#endif

        // A list shared by every text that has none of it is left alone: a clear from one thread would break another's walk over it.
        static void Empty<T>(List<T> list)
        {
            if (list.Count != 0) list.Clear();
        }

        static void Empty<T>(ValueList<T> list) where T : struct
        {
            if (list.Count != 0) list.Clear();
        }

        static List<T> Own<T>(ref List<T> list) => ReferenceEquals(list, Rare<T>.List) ? list = new List<T>() : list;
        static ValueList<T> Own<T>(ref ValueList<T> list) where T : struct => ReferenceEquals(list, RareValues<T>.List) ? list = new ValueList<T>() : list;
        static Dictionary<K, V> Own<K, V>(ref Dictionary<K, V> map) => ReferenceEquals(map, Rare<K, V>.Map) ? map = new Dictionary<K, V>() : map;

        // Thousands of short labels each hold these, so they start empty and grow to what the text asks.
        const int SmallestBuffer = 16;

        static void EnsureSize<T>(ref T[] array, int size)
        {
            if (array == null) array = new T[Math.Max(size, SmallestBuffer)];
            else if (array.Length < size) Array.Resize(ref array, Math.Max(Math.Max(size, SmallestBuffer), array.Length * 2));
        }
    }
}

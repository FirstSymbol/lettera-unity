using System;
using System.Collections.Generic;
using FirstSymbol.Lettera.Unicode;
using UnityEngine;

namespace FirstSymbol.Lettera
{
    internal sealed partial class TextEngine
    {
        // What only some texts use lives apart. A managed object past 2 KB is made as a large block of its own, several times slower than a small one,
        // and thousands of labels each make an engine: two small objects cost less than one large.
        readonly Aside m_Aside = new Aside();

        sealed class Aside
        {
            // Lines placed for a query, after everything the mesh reads. The next placement forgets them.
            internal ValueList<ExtraLine> ExtraLines = RareValues<ExtraLine>.List;
            internal int ExtraEnd;
            internal float WindowBandMargin = TextEngine.WindowBandMargin;

            internal CaretSegment[] Segments = Array.Empty<CaretSegment>(); // By line, each line in visual order.
            internal int[] SegmentStarts = Array.Empty<int>(); // Keys and, below, indices of the segments in logical order.
            internal int[] LogicalOrder = Array.Empty<int>();
            internal int[] LineSegments = Array.Empty<int>(); // The first segment of each line.
            internal int SegmentCount;
            internal bool SegmentsValid;
            List<Rect> m_RangeScratch;
            internal List<Rect> RangeScratch => m_RangeScratch ?? (m_RangeScratch = new List<Rect>());

            internal List<Rect> RowRects = Rare<Rect>.List;
            internal List<int> RowLines = Rare<int>.List;

            internal int DeferBaseLevel;
            // What estimates read, learned from the paragraphs done so far.
            internal float EstAdvanceEm, EstLineEm, EstFill;
            internal double SeenAdvanceEm, SeenChars, SeenLineEm, SeenLines, SeenUsed, SeenRoom;
            internal float LaidSize, LaidWidth;
            internal bool LaidWrap;
            internal bool PlacedOnEstimates; // A paragraph above the placed lines still waits, so they stand where estimates put them.
            internal int EstimateVersion;
            List<LetteraLine> m_SpliceLines;
            internal List<LetteraLine> SpliceLines => m_SpliceLines ?? (m_SpliceLines = new List<LetteraLine>());
            ValueList<Run> m_RunScratch;
            internal ValueList<Run> RunScratch => m_RunScratch ?? (m_RunScratch = new ValueList<Run>());
            internal ShapedGlyph[] GlyphScratch = Array.Empty<ShapedGlyph>();

            internal ValueList<MathSpecial> MathSpecials = RareValues<MathSpecial>.List;
            internal int FormulaCount;
            internal List<InlineItem> InlineItems = Rare<InlineItem>.List;
            internal Dictionary<int, int> InlineByCp = Rare<int, int>.Map;

            List<LetteraLine> m_MeasureLines;
            internal List<LetteraLine> MeasureLines => m_MeasureLines ?? (m_MeasureLines = new List<LetteraLine>());
            internal ValueList<Removal> Removals = RareValues<Removal>.List;

            internal LayerGroup MergeStroke, MergeShadow;
            internal int FadeLine;
            internal float FadeCut, FadeDistance, FadeSign;

            internal ValueList<MotionSpan> MotionSpans = RareValues<MotionSpan>.List;
            internal ValueList<MotionPhase> Phases = RareValues<MotionPhase>.List;
            internal ValueList<PoseLink> PoseLinks = RareValues<PoseLink>.List;
            internal ValueList<PoseBody> PoseBodies = RareValues<PoseBody>.List;
            internal LetteraGlyphPose[] GlyphPoses = System.Array.Empty<LetteraGlyphPose>();
            internal LetteraGlyphPose[] BodyPoses = System.Array.Empty<LetteraGlyphPose>();
            internal Vector3[] RestVertices = System.Array.Empty<Vector3>();
            internal Color32[] RestColors = System.Array.Empty<Color32>();
            internal int[] QuadOwner = System.Array.Empty<int>();
            internal int[] CpBody = System.Array.Empty<int>();

            internal ValueList<PlacedGlyph> VirtualGlyphs = RareValues<PlacedGlyph>.List;

            internal List<ActiveRange> Active = Rare<ActiveRange>.List;
            List<Rect> m_ActiveRects;
            internal List<Rect> ActiveRects => m_ActiveRects ?? (m_ActiveRects = new List<Rect>());
            internal ValueList<OldRange> OldRanges = RareValues<OldRange>.List;
            internal List<int> RangeIds = Rare<int>.List;
            internal int[] OldText = System.Array.Empty<int>();
            internal int OldLength = -1, NextRangeId = 1;
            internal bool ActiveRectsValid;
            // Each range's box, kept apart from the range so a hit test rejects most of them with four comparisons and no copy of anything.
            internal float[] ActiveBoxes = System.Array.Empty<float>();
            internal LetteraRangeEffect[] ActiveEffects = System.Array.Empty<LetteraRangeEffect>();
            internal ValueList<CoverQuad> CoverQuads = RareValues<CoverQuad>.List;
            internal Dictionary<int, float> CoverValue = Rare<int, float>.Map;
            internal Dictionary<int, bool> CoverTarget = Rare<int, bool>.Map;
            internal List<int> CoverKeys = Rare<int>.List;
            internal bool CoverFading;
            internal float CoverSeconds = 0.2f;

            internal ValueList<RevealSpan> RevealSpans = RareValues<RevealSpan>.List;
            internal List<RevealEffect> RevealListening = Rare<RevealEffect>.List;
            internal ValueList<RevealBody> RevealBodies = RareValues<RevealBody>.List;
            internal int[] CpReveal = System.Array.Empty<int>(), CpUnit = System.Array.Empty<int>();
            internal float[] CpVisible = System.Array.Empty<float>(), CpTarget = System.Array.Empty<float>();
            internal bool[] CpShownEvent = System.Array.Empty<bool>();
            internal bool RevealSettled = true, RevealInitialized, RevealRemovalsDirty;
            internal MarqueeEffect Marquee;
            internal bool MarqueeScrolling;
            internal double MarqueeTime;
            internal float MarqueeOffset, MarqueeD, MarqueeW, MarqueeB;
            internal int MarqueeCopies = 1;
            internal bool MarqueeRtl;

            ValueList<ParagraphSlot> m_Slots;
            internal ValueList<ParagraphSlot> Slots => m_Slots ?? (m_Slots = new ValueList<ParagraphSlot>());
            ValueList<DeferredRun> m_Deferred;
            internal ValueList<DeferredRun> Deferred => m_Deferred ?? (m_Deferred = new ValueList<DeferredRun>());
            internal bool DeferShaping;
            internal int DeferParagraphStart, DeferParagraphLength;
            internal ShapedGlyph[] ShapedMerged = Array.Empty<ShapedGlyph>();
            internal Action<int> AnalyzeOne, ShapeOne;

            internal ValueList<SolidBox> SolidBoxes = RareValues<SolidBox>.List;

            internal ValueList<(LetteraEffect effect, int start, int end)> PoseHooks = RareValues<(LetteraEffect effect, int start, int end)>.List;
            internal ValueList<(LetteraEffect effect, int start, int end)> StageHooks = RareValues<(LetteraEffect effect, int start, int end)>.List;
            internal List<Vector2Int> HookedHidden = Rare<Vector2Int>.List;
            internal HashSet<LetteraEffect> HookFailed;
            internal DirtyLevel HookStale;
            internal bool HookRanBreak;
            internal DirtyLevel EarlyHookLevel;

            internal List<SymbolSet> SymbolSets = Rare<SymbolSet>.List;
            internal ValueList<ExtraQuad> ExtraQuads = RareValues<ExtraQuad>.List;
            internal int[] FaceQuad = System.Array.Empty<int>();
            internal SymbolSet[] GlyphSymbols = System.Array.Empty<SymbolSet>();
            internal Vector4[] RestUv0 = System.Array.Empty<Vector4>();
            internal bool UsesRestUv0;

            internal ValueList<ShapedGlyph> InlineGlyphs = RareValues<ShapedGlyph>.List;
            internal ValueList<ListItem> ListItems = RareValues<ListItem>.List;
            internal ValueList<RubyItem> RubyItems = RareValues<RubyItem>.List;
            internal float[] CpListIndent = Array.Empty<float>();

            internal LetteraDocumentView View;
            internal int WindowStart, WindowLength, PreludeLength;
            internal float WindowOffset;
            internal bool WindowOffsetWas, WindowLoaded;
            internal float ViewWidth = -1;
            internal bool WindowLean, WindowBaked;
            internal bool WindowBandStale;
            internal TextEngine BlockMeasure;
            internal MeasureHost BlockHost;
            internal bool WindowPlaced;
            internal bool PlainScroll;
            internal Dictionary<long, int> DocumentRangeIds = Rare<long, int>.Map;
            internal HashSet<long> PresentRanges;
            internal List<long> DocumentRangeScratch = Rare<long>.List;
        }
    }
}

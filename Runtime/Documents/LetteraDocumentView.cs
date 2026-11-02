using System;
using System.Collections.Generic;
using UnityEngine;

namespace FirstSymbol.Lettera
{
    public enum LetteraRevealAlign { Top, Center, Bottom, Nearest }

    /// <summary>Limits of a document's window. Read when a document opens.</summary>
    [Serializable]
    public class LetteraDocumentSettings
    {
        [Tooltip("How far past the viewport the window reaches, in viewports.")]
        [Min(0)] public float MarginViewports = 0.5f;
        [Tooltip("The window moves when what shows comes this near its edge, in viewports.")]
        [Min(0)] public float EdgeViewports = 0.25f;
        [Tooltip("How many codepoints a window holds at most, so a mesh stays within what a Canvas can draw.")]
        [Min(256)] public int MaxWindowCodepoints = 14000;
        [Tooltip("How long, per frame, finding open tags and measuring may take, in milliseconds.")]
        [Min(0)] public float ScanBudgetMs = 1;
        public bool MeasureInBackground = true;
    }

    /// <summary>
    /// Where a document stands in its text. The anchor is the truth: a character and how far past it the viewport's top is.
    /// A height that turns out different above the viewport changes <see cref="DocumentY"/> and never what stands under the viewport.
    /// </summary>
    public sealed class LetteraDocumentView : IMotionClient
    {
        readonly TextEngine m_Engine;
        readonly LetteraRope m_Rope;
        readonly bool m_Owns;
        readonly LetteraDocumentSettings m_Settings;
        readonly DocumentTagWalk m_Walk;
        double m_Y;
        int m_FirstBlock, m_LastBlock;
        bool m_InHub, m_AtEnd;
        int m_MeasureCursor;
        float m_AverageLine, m_AverageCodepointsPerLine = 60;
        bool m_Dragging;

        internal LetteraDocumentView(TextEngine engine, LetteraRope rope, bool owns, LetteraDocumentSettings settings)
        {
            m_Engine = engine;
            m_Rope = rope;
            m_Owns = owns;
            m_Settings = settings;
            m_Walk = new DocumentTagWalk(rope);
            rope.EstimateNewBlock = EstimateFrom;
            rope.Changed += OnRopeChanged;
        }

        public LetteraRope Rope => m_Rope;

        /// <summary>How far down the document the viewport's top is, in local units.</summary>
        public double DocumentY => m_Y;
        public double MaxDocumentY => Math.Max(0, m_Rope.TotalHeight - ViewportHeight);
        public float ViewportHeight => m_Engine.WindowViewportHeight;

        /// <summary>The character the viewport is held to, and how far past its line's top the viewport stands.</summary>
        public (int DocChar, float Offset) Anchor { get; private set; }

        /// <summary>The selection in chars of the source: it stays while the window moves, and follows edits.</summary>
        public (int Anchor, int Head) DocumentSelection { get; private set; }
        public bool HasDocumentSelection { get; private set; }

        internal void SetDocumentSelection(int anchor, int head)
        {
            DocumentSelection = (anchor, head);
            HasDocumentSelection = true;
        }

        /// <summary>True while the text is too small to read, and draws bars in place of glyphs.</summary>
        public bool IsShownAsBars { get; private set; }

        /// <summary>Whether bars may stand in for glyphs too small to read.</summary>
        public bool BarsEnabled { get; set; } = true;

        /// <summary>Below this many screen pixels to an em, bars show.</summary>
        public float MinLegiblePixelsPerEm { get; set; } = 4;

        /// <summary>Stands in for what the screen shows, for tests.</summary>
        internal Func<float> ProjectedEmOverride;

        // Bars come on below the limit and go off 20% above it, so a slow zoom does not flicker at the edge.
        void UpdateBars()
        {
            float em = ProjectedEmOverride != null ? ProjectedEmOverride() : m_Engine.ProjectedEmPixels;
            bool bars = BarsEnabled && (IsShownAsBars ? em < MinLegiblePixelsPerEm * 1.2f : em < MinLegiblePixelsPerEm);
            if (bars == IsShownAsBars) return;
            IsShownAsBars = bars;
            m_Engine.BarsChanged();
            if (!bars) MoveWindow(true);
        }

        public int MeasuredBlockCount => m_Rope.MeasuredBlocks;
        public bool IsExact(int block) => block >= 0 && block < m_Rope.BlockCount && m_Rope.IsBlockMeasured(block);

        public int WindowStart => m_Engine.WindowStart;
        public int WindowLength => m_Engine.WindowLength;

        /// <summary>The position moved.</summary>
        public event Action PositionChanged;

        /// <summary>Extent or window changed.</summary>
        public event Action GeometryChanged;

        /// <summary>Holds what a scrollbar reads still while its handle is dragged, so measures arriving then do not move the content under the pointer.</summary>
        public bool FreezeExtent
        {
            get => m_Dragging;
            set
            {
                if (m_Dragging == value) return;
                m_Dragging = value;
                if (!value) GeometryChanged?.Invoke();
            }
        }

        internal void Open()
        {
            SeedEstimates();
            MoveWindow(true);
            EnterHub();
        }

        internal void Close()
        {
            m_Rope.Changed -= OnRopeChanged;
            m_Rope.EstimateNewBlock = null;
            if (m_InHub) LetteraMotionHub.Remove(this);
            m_InHub = false;
            if (m_Owns) m_Rope.Dispose();
        }

        void EnterHub()
        {
            if (m_InHub) return;
            m_InHub = true;
            LetteraMotionHub.Add(this);
        }

        // ------------------------------------------------------------- estimates

        void SeedEstimates()
        {
            m_Rope.ForgetMeasures();
            m_AverageLine = m_Engine.EstimatedLineHeight;
            float width = Math.Max(m_Engine.WindowViewportWidth, 1);
            float em = m_AverageLine / 1.4f;
            m_AverageCodepointsPerLine = Math.Max(width / Math.Max(em * 0.5f, 0.5f), 1);
            for (int b = 0; b < m_Rope.BlockCount; b++) m_Rope.SetBlockHeight(b, Estimate(b));
        }

        double Estimate(int block)
        {
            m_Rope.BlockTotals(block, out int codepoints, out int paragraphs);
            return EstimateFrom(codepoints, paragraphs);
        }

        double EstimateFrom(int codepoints, int paragraphs)
        {
            int lines = Math.Max(1, (int)Math.Ceiling(codepoints / Math.Max(m_AverageCodepointsPerLine, 1))) + paragraphs;
            return lines * m_AverageLine;
        }

        /// <summary>Width, font, size or wrap changed: what was measured is an estimate again, and the anchor keeps its character at the top.</summary>
        internal void NewGeneration()
        {
            m_Rope.ForgetMeasures();
            m_MeasureCursor = 0;
            for (int b = 0; b < m_Rope.BlockCount; b++) m_Rope.SetBlockHeight(b, Estimate(b));
            JumpToAnchor();
            EnterHub();
        }

        // ------------------------------------------------------------- moving

        public void ScrollTo(double y)
        {
            y = Math.Max(0, Math.Min(y, MaxDocumentY));
            if (y == m_Y) return;
            m_Y = y;
            m_TicksSinceScroll = 0;
            m_AtEnd = m_Y >= MaxDocumentY - 0.5;
            // A scroll says where to be, so the anchor comes from that, not the other way round.
            m_Scrolled = true;
            MoveWindow(false);
            if (m_Engine.WindowOffset == (float)(m_Y - m_Rope.HeightAtBlock(m_FirstBlock)) && m_Engine.Dirty < DirtyLevel.Parse) m_Scrolled = false;
            PositionChanged?.Invoke();
        }

        public void ScrollBy(Vector2 delta) => ScrollTo(m_Y + delta.y);

        /// <summary>Brings a character to where it is asked, measuring what it must first.</summary>
        public void ScrollToChar(int docChar, LetteraRevealAlign align = LetteraRevealAlign.Top)
        {
            docChar = Math.Max(0, Math.Min(docChar, m_Rope.Length));
            int block = m_Rope.BlockCount > 0 ? m_Rope.BlockAtChar(Math.Min(docChar, Math.Max(m_Rope.Length - 1, 0))) : 0;
            int start = m_Rope.CharOfBlock(block), chars = m_Rope.BlockCount > 0 ? m_Rope.BlockChars(block) : 1;
            double height = m_Rope.HeightAtBlock(block + 1) - m_Rope.HeightAtBlock(block);
            m_Y = m_Rope.HeightAtBlock(block) + height * (docChar - start) / Math.Max(chars, 1);
            float offset = align == LetteraRevealAlign.Center ? -ViewportHeight / 2 : align == LetteraRevealAlign.Bottom ? -ViewportHeight + m_AverageLine : 0;
            Anchor = (docChar, offset);
            m_Y = Math.Max(0, m_Y + offset);
            m_JumpPending = true;
            MoveWindow(true);
            PositionChanged?.Invoke();
        }

        public void ScrollToCodepoint(int codepoint, LetteraRevealAlign align = LetteraRevealAlign.Top) => ScrollToChar(m_Rope.CodepointToChar(codepoint), align);
        public void ScrollToParagraph(int paragraph, LetteraRevealAlign align = LetteraRevealAlign.Top) => ScrollToChar(m_Rope.ParagraphToChar(paragraph), align);

        public void ScrollToSpan(int startChar, int endChar, LetteraRevealAlign align = LetteraRevealAlign.Nearest)
        {
            if (align == LetteraRevealAlign.Nearest && TryGetCharY(startChar, out double top) && top >= m_Y && TryGetCharY(endChar, out double bottom) && bottom + m_AverageLine <= m_Y + ViewportHeight) return;
            ScrollToChar(startChar, align == LetteraRevealAlign.Nearest ? LetteraRevealAlign.Top : align);
        }

        /// <summary>Where a character stands. True when that comes from a measure, not an estimate.</summary>
        public bool TryGetCharY(int docChar, out double y)
        {
            docChar = Math.Max(0, Math.Min(docChar, m_Rope.Length));
            int block = m_Rope.BlockCount > 0 ? m_Rope.BlockAtChar(Math.Min(docChar, Math.Max(m_Rope.Length - 1, 0))) : 0;
            if (m_Engine.TryWindowLineTop(docChar, out float lineTop))
            {
                y = WindowTop() + lineTop;
                return IsExact(block);
            }
            int start = m_Rope.CharOfBlock(block), chars = m_Rope.BlockCount > 0 ? m_Rope.BlockChars(block) : 1;
            double height = m_Rope.HeightAtBlock(block + 1) - m_Rope.HeightAtBlock(block);
            y = m_Rope.HeightAtBlock(block) + height * (docChar - start) / Math.Max(chars, 1);
            return false;
        }

        bool m_JumpPending, m_Scrolled;

        void JumpToAnchor()
        {
            m_JumpPending = true;
            m_Y = m_Rope.HeightAtBlock(m_Rope.BlockCount > 0 ? m_Rope.BlockAtChar(Math.Min(Anchor.DocChar, Math.Max(m_Rope.Length - 1, 0))) : 0);
            MoveWindow(true);
        }

        // What shows comes close to the window's edge, or leaves it: the window moves. Inside it, a scroll is only a pose.
        void MoveWindow(bool force)
        {
            if (IsShownAsBars)
            {
                m_Lean = false;
                // No text is laid out: bars come from what is known of each block's height.
                m_Engine.SetWindowOffset(0);
                m_Engine.BarsChanged();
                return;
            }
            if (m_Rope.BlockCount == 0)
            {
                m_Lean = false;
                m_Engine.SetWindow(0, 0, "", 0);
                return;
            }
            float viewport = Math.Max(ViewportHeight, 1);
            double windowTop = WindowTop(), windowBottom = m_Rope.HeightAtBlock(m_LastBlock + 1);
            double edge = m_Settings.EdgeViewports * viewport;
            bool nearTop = m_FirstBlock > 0 && m_Y - edge < windowTop;
            bool nearBottom = m_LastBlock + 1 < m_Rope.BlockCount && m_Y + viewport + edge > windowBottom;
            if (!force && !nearTop && !nearBottom && m_Engine.IsWindowLoaded)
            {
                m_Engine.SetWindowOffset((float)(m_Y - windowTop));
                UpdateAnchor();
                return;
            }

            // A scroll that left the whole window behind in one frame will leave the next one behind too: margins would be built and never seen.
            bool leapt = !force && m_Engine.IsWindowLoaded && (m_Y >= windowBottom || m_Y + viewport <= windowTop);
            m_Lean = leapt && LeanMargin < m_Settings.MarginViewports;
            double margin = (m_Lean ? LeanMargin : m_Settings.MarginViewports) * viewport;
            int first = m_Rope.BlockAtHeight(m_Y - margin, out _);
            int last = m_Rope.BlockAtHeight(m_Y + viewport + margin, out _);
            int scanned = 0;
            while (first > 0 && !m_Rope.BlockEndsParagraph(first - 1) && scanned < 32768)
            {
                first--;
                scanned += m_Rope.BlockChars(first);
            }

            // Capped from the side far from the anchor.
            int cap = m_Settings.MaxWindowCodepoints;
            int count = 0;
            for (int b = first; b <= last; b++)
            {
                m_Rope.BlockTotals(b, out int codepoints, out _);
                count += codepoints;
            }
            while (count > cap && last > first)
            {
                m_Rope.BlockTotals(last, out int codepoints, out _);
                count -= codepoints;
                last--;
            }

            m_FirstBlock = first;
            m_LastBlock = last;
            int startChar = m_Rope.CharOfBlock(first);
            int endChar = m_Rope.CharOfBlock(last + 1);

            // One block can hold more than a window may: the part around what shows is taken from it.
            if (count > cap)
            {
                double height = Math.Max(m_Rope.HeightAtBlock(first + 1) - m_Rope.HeightAtBlock(first), 1);
                double fraction = Math.Max(0, Math.Min(1, (m_Y - m_Rope.HeightAtBlock(first)) / height));
                int blockChars = m_Rope.BlockChars(first);
                int around = startChar + (int)(blockChars * fraction);
                int capChars = cap;
                startChar = Math.Max(startChar, around - capChars / 2);
                endChar = Math.Min(endChar, startChar + capChars);
                if (startChar > 0 && startChar < m_Rope.Length && char.IsLowSurrogate(CharAt(startChar))) startChar--;
                if (endChar > 0 && endChar < m_Rope.Length && char.IsLowSurrogate(CharAt(endChar))) endChar--;
            }

            int startBlock = m_Rope.BlockAtChar(Math.Min(startChar, Math.Max(m_Rope.Length - 1, 0)));
            string prelude = m_Walk.PreludeAt(startBlock, m_Engine);
            m_Prelude = prelude;
            int length = endChar - startChar;
            m_Engine.SetWindow(startChar, length, prelude, 0, m_Lean ? 0 : TextEngine.WindowBandMargin);
            m_Engine.SetWindowOffset((float)(m_Y - WindowTop()));
            m_WindowMoved = true;
            GeometryChanged?.Invoke();
        }

        // A frame that builds a new window has spent its time on what shows: measuring waits for one that does not.
        bool m_WindowMoved;

        // A window built for a scroll faster than its margins holds what shows and an eighth of a viewport for estimates that run short,
        // and places only what shows. Two frames after the scroll stops, it takes its margins again.
        const float LeanMargin = 0.125f;
        bool m_Lean;
        int m_TicksSinceScroll;

        internal bool IsLean => m_Lean;

        // A clock read from a timestamp: a Stopwatch is an object, and this runs every frame.
        internal static double MillisecondsSince(long started) => (System.Diagnostics.Stopwatch.GetTimestamp() - started) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;

        char CharAt(int position)
        {
            Span<char> one = stackalloc char[1];
            m_Rope.CopyTo(position, one);
            return one[0];
        }

        // ------------------------------------------------------------- after a rebuild

        // The layout of a window is what it is: each whole block's height comes from it, and the anchor holds what is under the viewport.
        internal void AfterLayout()
        {
            if (m_FirstBlock >= m_Rope.BlockCount) return;
            int startChar = m_Rope.CharOfBlock(m_FirstBlock);
            for (int b = m_FirstBlock; b <= m_LastBlock && b < m_Rope.BlockCount; b++)
            {
                int from = m_Rope.CharOfBlock(b), to = m_Rope.CharOfBlock(b + 1);
                if (from < m_Engine.WindowStart || to > m_Engine.WindowStart + m_Engine.WindowLength) continue;
                float height = m_Engine.WindowHeightBetween(from, to);
                m_Rope.SetBlockHeight(b, height);
                m_Rope.MarkBlockMeasured(b, true);
            }
            if (m_JumpPending)
            {
                m_JumpPending = false;
                if (m_Engine.TryWindowLineTop(Anchor.DocChar, out float top))
                {
                    m_Y = WindowTop() + top + Anchor.Offset;
                    m_Y = Math.Max(0, Math.Min(m_Y, MaxDocumentY));
                }
            }
            else if (!m_Scrolled && m_Engine.TryWindowLineTop(Anchor.DocChar, out float top))
            {
                m_Y = WindowTop() + top + Anchor.Offset;
            }
            m_Scrolled = false;
            if (m_AtEnd) m_Y = MaxDocumentY;
            m_Engine.SetWindowOffset((float)(m_Y - WindowTop()));
            UpdateAnchor();
            GeometryChanged?.Invoke();
        }

        void UpdateAnchor()
        {
            float offset = (float)(m_Y - WindowTop());
            if (m_Engine.TryWindowAnchorAt(offset, out int docChar, out float past)) Anchor = (docChar, past);
        }

        // ------------------------------------------------------------- what edits do

        void OnRopeChanged(LetteraRopePatch patch)
        {
            // Each block's measure lives with it, so only what the edit made starts over; the anchor follows its character.
            Anchor = (patch.Map(Anchor.DocChar), Anchor.Offset);
            if (HasDocumentSelection) DocumentSelection = (patch.Map(DocumentSelection.Anchor), patch.Map(DocumentSelection.Head));
            m_Engine.MapDocumentRanges(patch);
            m_MeasureCursor = 0;
            m_Walk.InvalidateFrom(m_Rope.LastEditFirstBlock);
            KeepWindowThrough(patch);
            EnterHub();
        }

        // An edit must not move the window under whoever made it: what they hold are indices into it, and they are not done yet.
        void KeepWindowThrough(in LetteraRopePatch patch)
        {
            int start = m_Engine.WindowStart, length = m_Engine.WindowLength, end = start + length;
            if (m_Rope.BlockCount == 0 || !m_Engine.IsWindowLoaded)
            {
                JumpToAnchor();
                return;
            }
            // Below the window nothing that shows changed: only the extent did.
            if (patch.Start > end)
            {
                GeometryChanged?.Invoke();
                return;
            }
            bool straddles = (patch.Start < start && patch.Start + patch.Removed > start) || patch.Start + patch.Removed > end;
            if (straddles)
            {
                JumpToAnchor();
                return;
            }
            if (patch.Start + patch.Removed <= start && patch.Start < start) start += patch.Inserted - patch.Removed;
            else length += patch.Inserted - patch.Removed;
            start = Math.Max(0, Math.Min(start, m_Rope.Length));
            length = Math.Max(0, Math.Min(length, m_Rope.Length - start));
            RefreshWindowBlocks(start, length);
            m_Engine.SetWindow(start, length, m_Prelude, m_Engine.WindowOffset);
        }

        void RefreshWindowBlocks(int start, int length)
        {
            int last = Math.Max(m_Rope.Length - 1, 0);
            m_FirstBlock = m_Rope.BlockAtChar(Math.Min(start, last));
            m_LastBlock = m_Rope.BlockAtChar(Math.Min(start + Math.Max(length - 1, 0), last));
        }

        string m_Prelude = "";

        // Where the window's first line stands. A window can start inside a block: after an edit that merged it, or in one too long to fit.
        double WindowTop()
        {
            if (m_Rope.BlockCount == 0) return 0;
            double top = m_Rope.HeightAtBlock(m_FirstBlock);
            int from = m_Rope.CharOfBlock(m_FirstBlock), into = m_Engine.WindowStart - from;
            if (into <= 0) return top;
            double height = m_Rope.HeightAtBlock(m_FirstBlock + 1) - top;
            return top + height * into / Math.Max(m_Rope.BlockChars(m_FirstBlock), 1);
        }

        // ------------------------------------------------------------- in the background

        bool IMotionClient.TickMotion(float scaled, float unscaled)
        {
            UpdateBars();
            if (m_Lean && ++m_TicksSinceScroll >= 2 && !m_Dragging) MoveWindow(true);
            long started = System.Diagnostics.Stopwatch.GetTimestamp();
            double budget = m_Settings.ScanBudgetMs;
            bool walking = m_Walk.Advance(m_Engine, budget * 0.5);
            if (walking && m_Walk.PreludeChanged(m_FirstBlock, m_Engine)) MoveWindow(true);
            bool measuring = false;
            bool moved = m_WindowMoved;
            m_WindowMoved = false;
            if (m_Settings.MeasureInBackground && !m_Dragging && !moved && m_Engine.CanMeasureBlocks)
            {
                while (m_MeasureCursor < m_Rope.BlockCount && LetteraDocumentView.MillisecondsSince(started) < budget)
                {
                    int block = NextToMeasure();
                    if (block < 0) break;
                    MeasureBlock(block);
                }
                measuring = m_Rope.MeasuredBlocks < m_Rope.BlockCount;
            }
            // A document stays here for the bars' sake: seeing whether its size on screen changed costs one small sum a frame.
            _ = walking || measuring;
            return true;
        }

        // Near the viewport first, then outward.
        int NextToMeasure()
        {
            int total = m_Rope.BlockCount, center = Math.Min(m_FirstBlock, total - 1);
            for (int reach = m_MeasureCursor; reach < total; reach++)
            {
                int after = center + reach, before = center - reach;
                if (after < total && !m_Rope.IsBlockMeasured(after)) return after;
                if (before >= 0 && !m_Rope.IsBlockMeasured(before)) return before;
                m_MeasureCursor = reach + 1;
            }
            return -1;
        }

        char[] m_MeasureChars = Array.Empty<char>();

        void MeasureBlock(int block)
        {
            int start = m_Rope.CharOfBlock(block), chars = m_Rope.BlockChars(block);
            string prelude = m_Walk.PreludeAt(block, m_Engine);
            if (m_MeasureChars.Length < prelude.Length + chars) m_MeasureChars = new char[Math.Max(prelude.Length + chars, m_MeasureChars.Length * 2)];
            char[] text = m_MeasureChars;
            prelude.CopyTo(0, text, 0, prelude.Length);
            m_Rope.CopyTo(start, new Span<char>(text, prelude.Length, chars));
            // The separator that ends a block starts no line of its own in the document: the next block's first line is that line.
            int length = prelude.Length + chars;
            if (block + 1 < m_Rope.BlockCount && length > prelude.Length)
            {
                char last = text[length - 1];
                if (last == '\n' || last == '\r' || last == '\u0085' || last == (char)0x2029) length--;
                if (last == '\n' && length > prelude.Length && text[length - 1] == '\r') length--;
            }
            float height = m_Engine.MeasureBlockHeight(new ReadOnlySpan<char>(text, 0, length));
            double before = WindowTop();
            m_Rope.SetBlockHeight(block, height);
            m_Rope.MarkBlockMeasured(block, true);
            m_Rope.BlockTotals(block, out int codepoints, out _);
            float lines = Math.Max(height / Math.Max(m_AverageLine, 1), 1);
            m_AverageCodepointsPerLine = Mathf.Lerp(m_AverageCodepointsPerLine, codepoints / lines, 0.1f);
            // Above the window this only says where the top really is: nothing under the viewport moves.
            double after = WindowTop();
            if (after != before)
            {
                m_Y += after - before;
                m_Engine.SetWindowOffset((float)(m_Y - after));
            }
            if (!m_Dragging) GeometryChanged?.Invoke();
        }

        /// <summary>For tests: finishes every piece of background work now.</summary>
        internal void FinishBackgroundForTests()
        {
            while (m_Walk.Advance(m_Engine, double.MaxValue)) { }
            while (m_MeasureCursor < m_Rope.BlockCount)
            {
                int block = NextToMeasure();
                if (block < 0) break;
                MeasureBlock(block);
            }
        }

        internal int FirstBlock => m_FirstBlock;
        internal int LastBlock => m_LastBlock;
    }

    // What is open at each block's end, so a tag opened thousands of lines before the window still applies. A block that cannot hold markup copies the one before it.
    internal sealed class DocumentTagWalk
    {
        readonly LetteraRope m_Rope;
        readonly List<string> m_Ends = new List<string>();
        readonly List<(string name, string raw)> m_Open = new List<(string, string)>();
        readonly List<string> m_Names = new List<string>();
        int m_NextBlock;
        bool m_NamesKnown;
        string m_LastPrelude;

        public DocumentTagWalk(LetteraRope rope) => m_Rope = rope;

        public void Invalidate() => InvalidateFrom(0);

        // What came before an edit stands. The walk takes up from the state at the block before it.
        public void InvalidateFrom(int block)
        {
            block = Math.Max(block, 0);
            if (block < m_Ends.Count) m_Ends.RemoveRange(block, m_Ends.Count - block);
            m_NextBlock = Math.Min(m_NextBlock, m_Ends.Count);
            m_Open.Clear();
            if (m_Ends.Count > 0) Scan(m_Ends[m_Ends.Count - 1].AsSpan());
            m_LastPrelude = null;
        }

        // A false return means it reached the end.
        public bool Advance(TextEngine engine, double budgetMs)
        {
            if (!m_NamesKnown)
            {
                m_NamesKnown = true;
                engine.CollectTagNames(m_Names);
            }
            long started = System.Diagnostics.Stopwatch.GetTimestamp();
            while (m_NextBlock < m_Rope.BlockCount)
            {
                ScanBlock(m_NextBlock);
                m_NextBlock++;
                if (LetteraDocumentView.MillisecondsSince(started) >= budgetMs) return m_NextBlock < m_Rope.BlockCount;
            }
            return false;
        }

        // What is open at the start of a block. Before the walk gets there, what is known so far, and the window draws again when it arrives.
        public string PreludeAt(int block, TextEngine engine)
        {
            if (!m_NamesKnown) Advance(engine, 0);
            if (block <= 0 || m_Ends.Count == 0) return "";
            return m_Ends[Math.Min(block - 1, m_Ends.Count - 1)];
        }

        // What is open at a block's start, walking there first if it must.
        public string PreludeThrough(int block, TextEngine engine)
        {
            if (!m_NamesKnown) Advance(engine, 0);
            while (m_NextBlock < block && m_NextBlock < m_Rope.BlockCount)
            {
                ScanBlock(m_NextBlock);
                m_NextBlock++;
            }
            return PreludeAt(block, engine);
        }

        public bool PreludeChanged(int block, TextEngine engine)
        {
            string now = PreludeAt(block, engine);
            if (now == m_LastPrelude) return false;
            bool first = m_LastPrelude == null;
            m_LastPrelude = now;
            return !first;
        }

        void ScanBlock(int block)
        {
            if (m_Rope.BlockHasMarkup(block))
            {
                int start = m_Rope.CharOfBlock(block), chars = m_Rope.BlockChars(block);
                var text = new char[chars];
                m_Rope.CopyTo(start, text);
                Scan(text);
                string joined = "";
                foreach ((string _, string raw) in m_Open) joined += raw;
                m_Ends.Add(joined);
            }
            else m_Ends.Add(m_Ends.Count > 0 ? m_Ends[m_Ends.Count - 1] : "");
        }

        void Scan(ReadOnlySpan<char> text)
        {
            bool noParse = m_Open.Count > 0 && m_Open[m_Open.Count - 1].name == "noparse";
            for (int i = text.IndexOf('<'); i >= 0 && i < text.Length;)
            {
                int next = i + 1;
                if (noParse)
                {
                    if (TagSyntax.MatchClose(text, i, "noparse", out int closeLength))
                    {
                        m_Open.RemoveAt(m_Open.Count - 1);
                        noParse = false;
                        next = i + closeLength;
                    }
                }
                else
                {
                    if (TagSyntax.MatchOpen(text, i, "noparse", out int noParseLength, out _, out _, out _))
                    {
                        m_Open.Add(("noparse", new string(text.Slice(i, noParseLength))));
                        noParse = true;
                        next = i + noParseLength;
                    }
                    else
                    {
                        foreach (string name in m_Names)
                        {
                            if (TagSyntax.MatchClose(text, i, name, out int closeLength))
                            {
                                for (int o = m_Open.Count - 1; o >= 0; o--)
                                {
                                    if (m_Open[o].name != name) continue;
                                    m_Open.RemoveAt(o);
                                    break;
                                }
                                next = i + closeLength;
                                break;
                            }
                            if (!TagSyntax.MatchOpen(text, i, name, out int openLength, out _, out _, out bool selfClosing)) continue;
                            if (!selfClosing) m_Open.Add((name, new string(text.Slice(i, openLength))));
                            next = i + openLength;
                            break;
                        }
                    }
                }
                int found = next < text.Length ? text.Slice(next).IndexOf('<') : -1;
                if (found < 0) break;
                i = next + found;
            }
        }
    }
}

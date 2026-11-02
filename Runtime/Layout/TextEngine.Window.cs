using System;
using System.Collections.Generic;
using UnityEngine;

namespace FirstSymbol.Lettera
{
    // A document shows through one window of its text. All of it goes through the same passes, so selection, ranges and motion work unchanged;
    // a scroll inside the window is only an offset on the pose path.
    internal sealed partial class TextEngine
    {
        LetteraDocumentView m_View;
        int m_WindowStart, m_WindowLength, m_PreludeLength;
        float m_WindowOffset;
        bool m_WindowOffsetWas, m_WindowLoaded;
        float m_ViewWidth = -1;

        public bool IsWindowed => m_View != null;
        public LetteraDocumentView View => m_View;
        public int WindowStart => m_WindowStart;
        public int WindowLength => m_WindowLength;
        public float WindowOffset => m_WindowOffset;
        internal bool IsWindowLoaded => m_WindowLoaded;

        // A scroll needs no hub, but its quads need owners and rest positions. A lean window is built again by any scroll, so it bakes its offset in.
        bool NeedsRestState => (IsWindowed && !m_WindowLean) || m_PlainScroll;
        bool m_WindowLean, m_WindowBaked;

        public float WindowViewportHeight => Mathf.Max(ComputeBox(m_Host.Data.Padding, m_Host.LayoutRect).height, 0);
        public float WindowViewportWidth => Mathf.Max(ComputeBox(m_Host.Data.Padding, m_Host.LayoutRect).width, 0);

        internal float EstimatedLineHeight
        {
            get
            {
                LetteraTextData data = m_Host.Data;
                float value = data.LineHeightMode == LetteraLineHeightMode.Default ? LetteraSettings.Instance.DefaultLineHeightValue : data.LineHeightValue;
                return Mathf.Max(data.FontSize * Mathf.Max(value, 0.5f), 1);
            }
        }

        public void OpenDocument(LetteraRope rope, bool takeOwnership, LetteraDocumentSettings settings)
        {
            CloseDocument();
            settings = settings ?? new LetteraDocumentSettings();
            ReserveFor(EstimatedWindowCodepoints(settings));
            m_View = new LetteraDocumentView(this, rope, takeOwnership, settings);
            m_WindowLoaded = false;
            m_ViewWidth = WindowViewportWidth;
            m_View.Open();
        }

        public void CloseDocument()
        {
            if (m_View == null) return;
            m_View.Close();
            m_View = null;
            m_WindowStart = m_WindowLength = m_PreludeLength = 0;
            m_WindowOffset = 0;
            m_WindowLoaded = false;
            ClearRuntimeText();
            SetDirty(DirtyLevel.Parse);
            m_Host.RequestRebuild(DirtyLevel.Parse);
        }

        // The text is going away: the view leaves the hub and lets go of its file, and nothing asks the host for anything more.
        internal void AbandonDocument()
        {
            m_View?.Close();
            m_View = null;
        }

        // Source is what is open before the window, then the window's own text: open tags give claims with no shown text, so no matcher needs to know.
        internal void SetWindow(int start, int length, string prelude, float offset, float bandMargin = -1, bool lean = false)
        {
            if (bandMargin >= 0) m_WindowBandMargin = bandMargin;
            m_WindowLean = lean;
            LetteraRope rope = m_View.Rope;
            // The window's characters go straight into the runtime text's buffer: a scroll that moves the window makes no garbage.
            int total = prelude.Length + length;
            if (m_RuntimeChars.Length < total) m_RuntimeChars = new char[Math.Max(total, m_RuntimeChars.Length * 2)];
            prelude.CopyTo(0, m_RuntimeChars, 0, prelude.Length);
            if (length > 0) rope.CopyTo(start, new Span<char>(m_RuntimeChars, prelude.Length, length));
            m_RuntimeLength = total;
            m_RuntimeText = null;
            m_HasRuntimeText = true;
            m_WindowStart = start;
            m_WindowLength = length;
            m_PreludeLength = prelude.Length;
            m_WindowOffset = offset;
            m_WindowLoaded = true;
            SetDirty(DirtyLevel.Parse);
            m_Host.RequestRebuild(DirtyLevel.Parse);
        }

        internal void SetWindowOffset(float offset)
        {
            if (offset == m_WindowOffset) return;
            float was = m_WindowOffset;
            m_WindowOffset = offset;
            // Past the placed lines, the lines that come into view are placed: inside a rebuild at its end, otherwise by the next one.
            if (m_Banded && m_Dirty < DirtyLevel.Positions && !WindowBandCovers())
            {
                if (m_InPasses)
                {
                    m_WindowBandStale = true;
                    return;
                }
                SetDirty(DirtyLevel.Positions);
                m_Host.RequestRebuild(DirtyLevel.Positions);
                return;
            }
            if (m_WindowBaked && !m_WantsPose)
            {
                ShiftBaked(offset - was);
                return;
            }
            PoseForScroll();
        }

        void BakeWindowOffset()
        {
            m_WindowBaked = IsWindowed;
            if (!m_WindowBaked || m_WindowOffset == 0) return;
            ShiftVertices(m_WindowOffset);
        }

        void ShiftBaked(float delta)
        {
            if (m_Dirty >= DirtyLevel.Mesh || VertexCount == 0) return;
            ShiftVertices(delta);
            PoseStreams |= StreamPositions;
            m_Host.PoseChanged();
        }

        void ShiftVertices(float delta)
        {
            for (int i = 0; i < VertexCount; i++) Vertices[i].y += delta;
            m_Bounds.center += new Vector3(0, delta);
        }

        bool m_WindowBandStale;

        // What a window with its margins holds, by its viewport and font size, with room to spare. Its buffers take that room when the document opens,
        // so a scroll grows nothing, and opening takes no more than a window needs.
        int EstimatedWindowCodepoints(LetteraDocumentSettings settings)
        {
            float lines = WindowViewportHeight * (1 + 2 * settings.MarginViewports) / EstimatedLineHeight + 2;
            float perLine = WindowViewportWidth / Mathf.Max(m_Host.Data.FontSize * 0.4f, 0.5f);
            return (int)Mathf.Clamp(lines * perLine * 1.25f, 2048, settings.MaxWindowCodepoints + 512);
        }

        void ReserveFor(int codepoints)
        {
            int n = Math.Max(codepoints, SmallestBuffer);
            EnsureSize(ref m_Text, n + 4);
            if (m_RuntimeChars.Length < n) m_RuntimeChars = new char[n];
            if (m_PrevSource.Length < n) m_PrevSource = new char[n];
            EnsureSize(ref m_Levels, n + 1);
            EnsureSize(ref m_Boundaries, n + 2);
            EnsureSize(ref m_Breaks, n + 1);
            EnsureSize(ref m_Scripts, n + 1);
            EnsureSize(ref m_CpRun, n + 1);
            EnsureSize(ref m_CpAdvanceEm, n + 1);
            EnsureSize(ref m_ShapeText, n + 1);
            Cleared(ref m_CpDivider, n, 0);
            Cleared(ref m_CpExtraEm, n, 0);
            Cleared(ref m_CpExtraUnits, n, 0);
            Cleared(ref m_CpLeadEm, n, 0);
            Cleared(ref m_CpLeadUnits, n, 0);
            Cleared(ref m_CpKashida, n, 0);
            EnsureSize(ref m_Shaped, n);
            // Of a window, the lines near the viewport are placed: three quarters of it at most.
            int quads = n * 3 / 4;
            EnsureSize(ref m_Placed, quads);
            EnsureSize(ref m_PlacedAtlas, quads);
            EnsureSize(ref m_PlacedReach, quads);
            if (Vertices.Length < quads * 4) GrowVertices(quads * 4);
            if (Indices.Length < quads * 6) Array.Resize(ref Indices, quads * 6);
            // A window with margins keeps a rest state for its scroll inside it, so that state takes the same room: the window a scroll stops on
            // is often larger than the first, which starts at the top with no margin above it.
            EnsureExact(ref m_RestVertices, quads * 4);
            EnsureExact(ref m_RestColors, quads * 4);
            EnsureExact(ref m_GlyphPoses, quads);
            EnsureOwnerSize(quads);
            EnsureExact(ref m_CpBody, n + 1);
        }

        void PlaceWindowAgainIfStale()
        {
            if (!m_WindowBandStale) return;
            m_WindowBandStale = false;
            if (!m_Banded || WindowBandCovers()) return;
            PositionGlyphs();
            BuildMesh();
            m_WindowPlaced = false;
        }

        void PoseForScroll()
        {
            if (m_Dirty >= DirtyLevel.Mesh || !m_WantsPose || VertexCount == 0) return;
            RunPosePass();
            m_Host.PoseChanged();
        }

        // Where the window's source stands in the document: a position in one, plus this, is that position in the other.
        internal int WindowSourceBase => m_WindowStart - m_PreludeLength;

        /// <summary>Turns a change of the window's source into the one edit of the document it means.</summary>
        internal void WriteWindowSource(string value)
        {
            ReadOnlySpan<char> old = RawChars;
            int prefix = 0, suffix = 0;
            int limit = Math.Min(old.Length, value.Length);
            while (prefix < limit && old[prefix] == value[prefix]) prefix++;
            while (suffix < limit - prefix && old[old.Length - 1 - suffix] == value[value.Length - 1 - suffix]) suffix++;
            // An edit never touches what stands before the window.
            prefix = Math.Max(prefix, m_PreludeLength);
            int removed = Math.Max(old.Length - prefix - suffix, 0);
            int inserted = Math.Max(value.Length - prefix - suffix, 0);
            m_View.Rope.Replace(WindowSourceBase + prefix, removed, value.AsSpan(prefix, inserted));
        }

        /// <summary>Where a shown codepoint is in the document.</summary>
        public int DocCharOf(int shown) => m_WindowStart + SourceAt(Math.Min(Math.Max(shown, 0), m_TextLength)) - m_PreludeLength;

        int ShownOfDocChar(int docChar)
        {
            int source = docChar - m_WindowStart + m_PreludeLength;
            if (m_IdentityIndex) return Math.Min(Math.Max(source, 0), m_TextLength);
            int low = 0, high = m_TextLength;
            while (low < high)
            {
                int middle = (low + high) / 2;
                if (m_SourceIndex[middle] < source) low = middle + 1;
                else high = middle;
            }
            return low;
        }

        // Lines run down from the window's top, measured here from that top.
        float LineTopFromWindow(int line) => m_Box.yMax - (m_Lines[line].Baseline + m_Lines[line].Ascent);

        internal bool TryWindowLineTop(int docChar, out float top)
        {
            top = 0;
            if (m_Lines.Count == 0 || docChar < m_WindowStart || docChar > m_WindowStart + m_WindowLength) return false;
            int line = LineOf(ShownOfDocChar(docChar), LetteraCaretBias.Forward);
            top = LineTopFromWindow(line);
            return true;
        }

        // The first line that reaches below a height, and how far past its top that height is.
        internal bool TryWindowAnchorAt(float offset, out int docChar, out float past)
        {
            docChar = 0;
            past = 0;
            if (m_Lines.Count == 0) return false;
            int low = 0, high = m_Lines.Count - 1;
            while (low < high)
            {
                int middle = (low + high) / 2;
                if (LineTopFromWindow(middle) + m_Lines[middle].Height <= offset) low = middle + 1;
                else high = middle;
            }
            docChar = DocCharOf(m_Lines[low].Start);
            past = offset - LineTopFromWindow(low);
            return true;
        }

        // The height of whole lines that start in a span of the document.
        internal float WindowHeightBetween(int fromChar, int toChar)
        {
            float height = 0;
            int from = ShownOfDocChar(fromChar), to = toChar >= m_View.Rope.Length ? m_TextLength + 1 : ShownOfDocChar(toChar);
            // Only the document's own end has an empty last line: a window's end is followed by more of the document.
            for (int l = 0; l < m_Lines.Count; l++)
            {
                LetteraLine line = m_Lines[l];
                if (line.Start >= from && line.Start < to) height += line.Height + line.GapBefore;
            }
            return height;
        }

        internal bool CanMeasureBlocks => m_Dirty < DirtyLevel.Shape && !SuspendElide;

        TextEngine m_BlockMeasure;
        MeasureHost m_BlockHost;

        // One engine measures every block, so measuring in the background makes no garbage once its buffers have grown.
        internal float MeasureBlockHeight(ReadOnlySpan<char> source)
        {
            if (m_BlockMeasure == null)
            {
                m_BlockHost = new MeasureHost { Owner = m_Host.Owner };
                m_BlockMeasure = new TextEngine(m_BlockHost);
            }
            LetteraTextData data = m_Host.Data;
            m_BlockHost.Data = data;
            m_BlockMeasure.SetRuntimeText(source);
            m_BlockMeasure.SetDirty(DirtyLevel.Parse);
            return m_BlockMeasure.Measure(data.FontSize, WindowViewportWidth, 0, data.WordWrap, false, 0, 0).y;
        }

        internal void CollectTagNames(List<string> names)
        {
            names.Clear();
            CollectStyles();
            foreach (LetteraStyle style in m_EffectiveStyles)
                if (style?.Matcher is TagMatcher tag && !string.IsNullOrEmpty(tag.Name) && !names.Contains(tag.Name))
                    names.Add(tag.Name);
        }

        bool m_WindowPlaced;

        /// <summary>The window's text was laid out: what is held in document positions is read again from it.</summary>
        internal event Action WindowPlaced;

        void AfterWindowPasses()
        {
            if (m_View == null || !m_WindowPlaced) return;
            m_WindowPlaced = false;
            float width = WindowViewportWidth;
            bool wider = !Mathf.Approximately(width, m_ViewWidth);
            m_ViewWidth = width;
            if (wider) m_View.NewGeneration();
            else m_View.AfterLayout();
            WindowPlaced?.Invoke();
        }

        // ------------------------------------------------------------- a plain text under a scroller

        bool m_PlainScroll;

        /// <summary>Lets a text that is no document scroll with the same offset, with no movement of its rect.</summary>
        public void SetPlainScroll(bool on)
        {
            if (m_PlainScroll == on) return;
            m_PlainScroll = on;
            if (!on) m_WindowOffset = 0;
            SetDirty(DirtyLevel.Mesh);
            m_Host.RequestRebuild(DirtyLevel.Mesh);
        }

        public bool ClipsViewport => IsWindowed || m_PlainScroll;

        public float PlainExtent => m_ContentHeight;

        public void SetPlainOffset(float offset)
        {
            if (offset == m_WindowOffset) return;
            m_WindowOffset = offset;
            PoseForScroll();
        }

        // ------------------------------------------------------------- bars

        // One em in screen pixels, from what scales a text on its way there.
        internal float ProjectedEmPixels
        {
            get
            {
                Component owner = m_Host.Owner;
                float size = m_Host.Data.FontSize;
                if (owner == null) return size;
                Vector3 scale = owner.transform.lossyScale;
                float em = size * Mathf.Abs(scale.y);
                if (owner is LetteraWorldText)
                {
                    Camera camera = Camera.main;
                    if (camera == null) return size;
                    float distance = Mathf.Max(Vector3.Dot(owner.transform.position - camera.transform.position, camera.transform.forward), camera.nearClipPlane);
                    float unitsPerScreen = camera.orthographic ? camera.orthographicSize * 2 : 2 * distance * Mathf.Tan(camera.fieldOfView * 0.5f * Mathf.Deg2Rad);
                    return em * Screen.height / Mathf.Max(unitsPerScreen, 1e-4f);
                }
                return em;
            }
        }

        bool ShowsBars => m_View != null && m_View.IsShownAsBars;

        internal void BarsChanged()
        {
            SetDirty(DirtyLevel.Mesh);
            m_Host.RequestRebuild(DirtyLevel.Mesh);
        }

        // Each block a bar, merged until a bar covers two screen pixels: the work is bounded by the screen, however much text is in view.
        void DrawBars()
        {
            LetteraRope rope = m_View.Rope;
            if (rope.BlockCount == 0) return;
            Color32 baseColor = m_Host.BaseColor;
            var color = new Color32(baseColor.r, baseColor.g, baseColor.b, (byte)(baseColor.a * 0.35f));
            float top = m_Box.yMax, height = m_Box.height;
            double y = m_View.DocumentY, bottom = y + height;
            double minimum = 2.0 * m_Host.Data.FontSize / Mathf.Max(ProjectedEmPixels, 0.01f);
            int block = rope.BlockAtHeight(y, out _);
            UseSegment(s_NoPaint);
            m_FilterRow = 0;
            m_CurrentOwner = -1;
            while (block < rope.BlockCount)
            {
                double start = rope.HeightAtBlock(block);
                if (start >= bottom) break;
                double end = rope.HeightAtBlock(block + 1);
                int next = block + 1;
                while (end - start < minimum && next < rope.BlockCount)
                {
                    next++;
                    end = rope.HeightAtBlock(next);
                }
                float from = top - (float)(start - y), to = top - (float)(end - y);
                AddSolid(m_Box.xMin, Mathf.Max(to, m_Box.yMin), m_Box.xMax, Mathf.Min(from, top), color, s_NoPaint, default, 0, 0);
                block = next;
            }
        }

        // ------------------------------------------------------------- what stays across windows

        struct DocumentRange
        {
            public int DocChar, Style;
        }

        Dictionary<long, int> m_DocumentRangeIds = Rare<long, int>.Map;
        HashSet<long> m_PresentRanges;
        List<long> m_DocumentRangeScratch = Rare<long>.List;

        /// <summary>Tells whether a range holds state worth keeping out of a window, beyond the covers this knows itself.</summary>
        internal Func<int, bool> RangeHasOutsideState;

        static long DocumentRangeKey(int docChar, int style) => (long)docChar << 20 | (uint)style;

        // A range stays itself while it stays in the window. One with state stays itself out of it too; one without gets a new id if it comes back.
        void AssignDocumentRangeIds(LetteraMatchContext context)
        {
            List<LetteraMatchContext.RangeRecord> ranges = context.Ranges;
            m_RangeIds.Clear();
            if (ranges.Count == 0 && m_DocumentRangeIds.Count == 0) return;
            HashSet<long> present = m_PresentRanges ?? (m_PresentRanges = new HashSet<long>());
            present.Clear();
            for (int r = 0; r < ranges.Count; r++)
            {
                LetteraMatchContext.RangeRecord range = ranges[r];
                int docChar = m_WindowStart + SourceAt(Math.Min(Math.Max(range.RenderedStart, 0), m_TextLength)) - m_PreludeLength;
                long key = DocumentRangeKey(docChar, range.StyleIndex);
                if (!m_DocumentRangeIds.TryGetValue(key, out int id))
                {
                    id = m_NextRangeId++;
                    Own(ref m_DocumentRangeIds)[key] = id;
                }
                present.Add(key);
                Own(ref m_RangeIds).Add(id);
            }
            m_DocumentRangeScratch.Clear();
            foreach (KeyValuePair<long, int> pair in m_DocumentRangeIds)
                if (!present.Contains(pair.Key) && !(m_CoverTarget.TryGetValue(pair.Value, out bool open) && open) && !(RangeHasOutsideState != null && RangeHasOutsideState(pair.Value)))
                    Own(ref m_DocumentRangeScratch).Add(pair.Key);
            foreach (long key in m_DocumentRangeScratch)
            {
                int id = m_DocumentRangeIds[key];
                m_DocumentRangeIds.Remove(key);
                m_CoverValue.Remove(id);
                m_CoverTarget.Remove(id);
                RangeRetired?.Invoke(id);
            }
        }

        internal void MapDocumentRanges(LetteraRopePatch patch)
        {
            if (m_DocumentRangeIds.Count == 0) return;
            var mapped = new Dictionary<long, int>(m_DocumentRangeIds.Count);
            foreach (KeyValuePair<long, int> pair in m_DocumentRangeIds)
            {
                int docChar = (int)(pair.Key >> 20), style = (int)(pair.Key & 0xFFFFF);
                // One the edit cut into starts over.
                if (docChar > patch.Start && docChar < patch.Start + patch.Removed) continue;
                mapped[DocumentRangeKey(patch.Map(docChar), style)] = pair.Value;
            }
            m_DocumentRangeIds.Clear();
            foreach (KeyValuePair<long, int> pair in mapped) Own(ref m_DocumentRangeIds)[pair.Key] = pair.Value;
        }

        // ------------------------------------------------------------- reading what a document holds

        // The same styles over a scratch text, for what reads a block outside the window.
        internal static void ParseForSearch(TextEngine owner, string source, out int[] shown, out int[] sourceIndex, out int length)
        {
            LetteraTextData copy = owner.m_Host.Data.Clone();
            copy.Text = source;
            var engine = new TextEngine(new MeasureHost { Data = copy, Owner = owner.m_Host.Owner });
            engine.EnsureParsed();
            shown = engine.m_Text;
            sourceIndex = engine.SourceIndexArray();
            length = engine.m_TextLength;
        }

        /// <summary>The plain text between two places in the document, with markup gone, read block by block with what is open before each.</summary>
        internal string DocumentPlainBetween(int fromChar, int toChar)
        {
            LetteraRope rope = m_View.Rope;
            var walk = new DocumentTagWalk(rope);
            var builder = new System.Text.StringBuilder(toChar - fromChar);
            if (rope.BlockCount == 0 || toChar <= fromChar) return "";
            for (int block = rope.BlockAtChar(fromChar); block < rope.BlockCount; block++)
            {
                int start = rope.CharOfBlock(block), chars = rope.BlockChars(block);
                if (start >= toChar) break;
                string prelude = walk.PreludeThrough(block, this);
                var source = new char[prelude.Length + chars];
                prelude.CopyTo(0, source, 0, prelude.Length);
                rope.CopyTo(start, new Span<char>(source, prelude.Length, chars));
                ParseForSearch(this, new string(source), out int[] shown, out int[] sourceIndex, out int length);
                for (int i = 0; i < length; i++)
                {
                    int position = start + sourceIndex[i] - prelude.Length;
                    if (position < fromChar || position >= toChar) continue;
                    builder.Append(char.ConvertFromUtf32(shown[i]));
                }
            }
            return builder.ToString();
        }

        /// <summary>A place in the document, as an index of what shows now, held to the window's ends.</summary>
        public int ShownOfDocument(int docChar) => ShownOfDocChar(Math.Max(m_WindowStart, Math.Min(docChar, m_WindowStart + m_WindowLength)));

        internal Vector2 ScrollIntoContent(Vector2 point)
        {
            point.y -= m_WindowOffset;
            return point;
        }

        internal Rect ScrolledRect(in Rect rect) => new Rect(rect.x, rect.y + m_WindowOffset, rect.width, rect.height);
    }
}

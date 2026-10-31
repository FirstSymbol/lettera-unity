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

        // A scroll needs no hub, but its quads need owners and rest positions.
        bool NeedsRestState => IsWindowed || m_PlainScroll;

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
            m_View = new LetteraDocumentView(this, rope, takeOwnership, settings ?? new LetteraDocumentSettings());
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
        internal void SetWindow(int start, int length, string prelude, float offset)
        {
            LetteraRope rope = m_View.Rope;
            var chars = new char[prelude.Length + length];
            prelude.CopyTo(0, chars, 0, prelude.Length);
            if (length > 0) rope.CopyTo(start, new Span<char>(chars, prelude.Length, length));
            m_WindowStart = start;
            m_WindowLength = length;
            m_PreludeLength = prelude.Length;
            m_WindowOffset = offset;
            m_WindowLoaded = true;
            SetRuntimeText(chars);
            SetDirty(DirtyLevel.Parse);
            m_Host.RequestRebuild(DirtyLevel.Parse);
        }

        internal void SetWindowOffset(float offset)
        {
            if (offset == m_WindowOffset) return;
            m_WindowOffset = offset;
            PoseForScroll();
        }

        void PoseForScroll()
        {
            if (m_Dirty >= DirtyLevel.Mesh || !m_WantsPose || VertexCount == 0) return;
            RunPosePass();
            m_Host.PoseChanged();
        }

        /// <summary>Where a shown codepoint is in the document.</summary>
        public int DocCharOf(int shown) => m_WindowStart + m_SourceIndex[Math.Min(Math.Max(shown, 0), m_TextLength)] - m_PreludeLength;

        int ShownOfDocChar(int docChar)
        {
            int source = docChar - m_WindowStart + m_PreludeLength;
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

        internal float MeasureBlockHeight(string source) => MeasureWith(m_Host.Owner, m_Host.Data, source, m_Host.Data.FontSize, WindowViewportWidth, 0, m_Host.Data.WordWrap, false, 0, 0).y;

        internal void CollectTagNames(List<string> names)
        {
            names.Clear();
            CollectStyles();
            foreach (LetteraStyle style in m_EffectiveStyles)
                if (style?.Matcher is TagMatcher tag && !string.IsNullOrEmpty(tag.Name) && !names.Contains(tag.Name))
                    names.Add(tag.Name);
        }

        bool m_WindowPlaced;

        void AfterWindowPasses()
        {
            if (m_View == null || !m_WindowPlaced) return;
            m_WindowPlaced = false;
            float width = WindowViewportWidth;
            bool wider = !Mathf.Approximately(width, m_ViewWidth);
            m_ViewWidth = width;
            if (wider) m_View.NewGeneration();
            else m_View.AfterLayout();
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

        internal Vector2 ScrollIntoContent(Vector2 point)
        {
            point.y -= m_WindowOffset;
            return point;
        }

        internal Rect ScrolledRect(in Rect rect) => new Rect(rect.x, rect.y + m_WindowOffset, rect.width, rect.height);
    }
}

using System;
using System.Collections.Generic;
using UnityEngine;

namespace FirstSymbol.Lettera
{
    public enum LetteraSpanTracking
    {
        /// <summary>Text put in at an edge stays out. A span wholly removed goes away.</summary>
        FollowContent,

        /// <summary>Text put in at an edge joins the span.</summary>
        GrowAtEdges,

        /// <summary>The indices do not move, and an edit that touches the span drops it.</summary>
        Fixed,
    }

    /// <summary>
    /// Runtime ranges with no presence in the source, for find highlights, spelling marks and diagnostics. Bound to a field they
    /// follow its edits. Use it as a style's matcher, with the effect the ranges should get.
    /// </summary>
    [Serializable]
    public sealed class LetteraLiveSpans : LetteraMatcher
    {
        struct Span
        {
            public int Id, Start, End;
            public LetteraSpanTracking Tracking;
        }

        readonly List<Span> m_Spans = new List<Span>();
        LetteraEditable m_Field;
        int m_NextId = 1;

        /// <summary>Rises with each change of the text. An update names the one it was computed from.</summary>
        public int Revision { get; private set; }

        public int Count => m_Spans.Count;

        /// <summary>A style's list clones its matcher for a text, and these spans have to stay this object.</summary>
        public override LetteraMatcher Clone() => this;

        /// <summary>Follows a field's edits from now on.</summary>
        public void Bind(LetteraEditable field)
        {
            if (m_Field != null) m_Field.Patched -= OnPatched;
            m_Field = field;
            if (field != null) field.Patched += OnPatched;
        }

        public int Add(int start, int end, LetteraSpanTracking tracking = LetteraSpanTracking.FollowContent)
        {
            int id = m_NextId++;
            m_Spans.Add(new Span { Id = id, Start = start, End = end, Tracking = tracking });
            Changed();
            return id;
        }

        public bool Remove(int id)
        {
            int removed = m_Spans.RemoveAll(s => s.Id == id);
            if (removed > 0) Changed();
            return removed > 0;
        }

        public void Clear()
        {
            m_Spans.Clear();
            Changed();
        }

        public bool TryGet(int id, out int start, out int end)
        {
            foreach (Span span in m_Spans)
            {
                if (span.Id != id) continue;
                start = span.Start;
                end = span.End;
                return true;
            }
            start = end = 0;
            return false;
        }

        /// <summary>Replaces all spans from one result, with one notification. A result from older text throws, so a late worker cannot land on the wrong text.</summary>
        public void SetAll(int revision, IEnumerable<(int start, int end)> ranges, LetteraSpanTracking tracking = LetteraSpanTracking.FollowContent)
        {
            if (revision != Revision) throw new InvalidOperationException($"These spans were computed from revision {revision}, and the text is at {Revision}.");
            m_Spans.Clear();
            foreach ((int start, int end) in ranges) m_Spans.Add(new Span { Id = m_NextId++, Start = start, End = end, Tracking = tracking });
            Changed();
        }

        void Changed()
        {
            if (m_Field != null) m_Field.Selectable.Host.Dirty(DirtyLevel.Parse);
        }

        void OnPatched(LetteraEditPatch patch)
        {
            Revision++;
            if (m_Spans.Count == 0) return;
            int removedEnd = patch.Start + patch.Removed;
            for (int i = m_Spans.Count - 1; i >= 0; i--)
            {
                Span span = m_Spans[i];
                if (span.Tracking == LetteraSpanTracking.Fixed)
                {
                    if (patch.Start < span.End && (removedEnd > span.Start || patch.Start > span.Start)) m_Spans.RemoveAt(i);
                    continue;
                }
                if (patch.Removed > 0 && patch.Start <= span.Start && removedEnd >= span.End)
                {
                    m_Spans.RemoveAt(i);
                    continue;
                }
                bool grow = span.Tracking == LetteraSpanTracking.GrowAtEdges;
                span.Start = MapEdge(span.Start, patch, grow ? false : true);
                span.End = MapEdge(span.End, patch, grow);
                m_Spans[i] = span;
            }
            Changed();
        }

        // At the very spot of an insertion an edge goes after the new text when pushed, and stays before it otherwise.
        static int MapEdge(int index, in LetteraEditPatch patch, bool pushed)
        {
            if (index == patch.Start && patch.Removed == 0) return pushed ? index + patch.Inserted : index;
            return patch.Map(index);
        }

        public override void Find(LetteraMatchContext context)
        {
            foreach (Span span in m_Spans) context.AddSpan(span.Start, span.End - span.Start);
        }
    }
}

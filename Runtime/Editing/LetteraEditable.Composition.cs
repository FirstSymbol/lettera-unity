using System;
using UnityEngine;

namespace FirstSymbol.Lettera
{
    // A composition shows at the caret but stays out of the committed text: the engine parses a temporary copy of the source that holds it.
    public partial class LetteraEditable
    {
        string m_CompText;
        int m_CompStart = -1, m_CompLength;
        int[] m_CommittedCps = new int[64];
        int m_CommittedLength;
        LetteraClause[] m_CompClauses;
        LetteraSelectionRange m_CompBase;

        public bool IsComposing => m_CompText != null;

        internal void OnComposition(string text, int caret, LetteraClause[] clauses)
        {
            if (m_ReadOnly) return;
            text = text ?? "";
            bool starting = m_CompText == null;
            if (starting)
            {
                if (text.Length == 0) return;
                Engine.EnsureParsed();
                m_SourceDuringComposition = Engine.RawText;
                m_CompBase = Selectable.Selection;
                ReadOnlySpan<int> shown = Engine.ShownCodepoints;
                if (m_CommittedCps.Length < shown.Length) m_CommittedCps = new int[shown.Length * 2];
                shown.CopyTo(m_CommittedCps);
                m_CommittedLength = shown.Length;
            }
            else
            {
                // Mapping starts from the committed text each time.
                RestoreCommitted();
            }

            m_Spans.Clear();
            Engine.SourceSpansOf(m_CompBase.Start, m_CompBase.End, m_Spans);
            int insertAt = m_Spans.Count > 0 ? m_Spans[0].x : InsertionPoint(m_CompBase.Start);
            m_Builder.Clear();
            m_Builder.Append(m_SourceDuringComposition);
            for (int i = m_Spans.Count - 1; i >= 0; i--) m_Builder.Remove(m_Spans[i].x, m_Spans[i].y - m_Spans[i].x);
            m_Builder.Insert(insertAt, text);

            m_CompText = text;
            m_CompClauses = clauses;
            m_CompLength = CountCodepoints(text);
            Engine.MaskExceptStart = m_CompBase.Start;
            Engine.MaskExceptEnd = m_CompBase.Start + m_CompLength;
            Engine.ProtectSource(insertAt, text.Length);
            Engine.SetRuntimeText(m_Builder.ToString());
            Host.Dirty(DirtyLevel.Parse);
            Engine.EnsureParsed();
            m_CompStart = Engine.RenderedIndexFromSource(insertAt);
            m_Suppress = true;
            Selectable.SetSelectionUnchecked(LetteraSelectionRange.AtCaret(m_CompStart + Mathf.Clamp(CountBefore(text, caret), 0, m_CompLength)), "sel.ime");
            m_Suppress = false;
            m_ShownChanged = true;
            Selectable.MarkVisualsDirty();
            ScrollCaretIntoView();
            if (starting) ComposingChanged?.Invoke(true);
            PushSnapshot();
        }

        static int CountBefore(string text, int utf16) => utf16 >= text.Length ? CountCodepoints(text) : CountCodepoints(text.Substring(0, Mathf.Max(utf16, 0)));

        void RestoreCommitted()
        {
            Engine.ClearRuntimeText();
            Engine.ProtectSource(0, 0);
            Engine.MaskExceptStart = Engine.MaskExceptEnd = -1;
            Host.Dirty(DirtyLevel.Parse);
            Engine.EnsureParsed();
        }

        internal void OnCompositionEnd(bool commit)
        {
            if (commit) CommitComposition();
            else DiscardComposition();
        }

        /// <summary>Puts a running composition into the text, as one undo step.</summary>
        public void CommitComposition()
        {
            if (m_CompText == null) return;
            string text = m_CompText;
            EndComposition();
            ProposeEdit(m_CompBase.Start, m_CompBase.End, text, "user.type.compose", null);
            ComposingChanged?.Invoke(false);
            PushSnapshot();
        }

        /// <summary>Drops a running composition; the selection under it comes back.</summary>
        public void DiscardComposition()
        {
            if (m_CompText == null) return;
            EndComposition();
            ComposingChanged?.Invoke(false);
            PushSnapshot();
        }

        void EndComposition()
        {
            m_CompText = null;
            m_CompLength = 0;
            m_CompStart = -1;
            m_CompClauses = null;
            RestoreCommitted();
            m_Suppress = true;
            Selectable.SetSelectionUnchecked(m_CompBase.Clamp(Engine.ShownLength), "sel.ime");
            m_Suppress = false;
            m_ShownChanged = true;
            Selectable.MarkVisualsDirty();
        }

        // A script took over the source while something was composing: what was composed goes away with what it would have replaced.
        void AbandonComposition()
        {
            if (m_CompText == null) return;
            m_CompText = null;
            m_CompLength = 0;
            m_CompStart = -1;
            m_CompClauses = null;
            Engine.ProtectSource(0, 0);
            Engine.MaskExceptStart = Engine.MaskExceptEnd = -1;
            ComposingChanged?.Invoke(false);
        }

        /// <summary>The whole text of an OS text box: what changed comes back as one edit. A refused change stays out, and the box follows the field.</summary>
        internal void Reconcile(string osText, int osCaret = -1)
        {
            string current = PlainText;
            if (osText == current) return;
            int prefix = 0, oldEnd = current.Length, newEnd = osText.Length;
            // What follows the caret did not change, which tells where one of several equal characters went.
            int floor = osCaret >= 0 ? Mathf.Min(osCaret, osText.Length) : 0;
            while (oldEnd > 0 && newEnd > floor && current[oldEnd - 1] == osText[newEnd - 1])
            {
                oldEnd--;
                newEnd--;
            }
            while (prefix < oldEnd && prefix < newEnd && current[prefix] == osText[prefix]) prefix++;
            if (prefix > 0 && prefix < current.Length && char.IsLowSurrogate(current[prefix])) prefix--;
            if (oldEnd < prefix) oldEnd = prefix;
            if (newEnd < prefix) newEnd = prefix;
            if (oldEnd < current.Length && char.IsLowSurrogate(current[oldEnd]))
            {
                oldEnd++;
                newEnd++;
            }
            int start = CountCodepoints(current.Substring(0, prefix)), end = start + CountCodepoints(current.Substring(prefix, oldEnd - prefix));
            string inserted = osText.Substring(prefix, newEnd - prefix);
            string reason = inserted.Length > 0 ? "user.type" : "user.delete.back";
            if (!m_ReadOnly) ProposeEdit(start, end, inserted, reason, null);
        }
    }
}

using System;
using System.Collections.Generic;
using System.Text;

namespace FirstSymbol.Lettera
{
    /// <summary>The undo stack of a field: exact source splices, stored flat so that keystrokes allocate nothing after warm-up.</summary>
    internal sealed class LetteraEditHistory
    {
        internal enum Kind : byte { Other, Typing, DeleteBack, DeleteForward }
        internal enum TokenClass : byte { Word, Space, Other }

        struct Splice
        {
            public int Position, RemovedStart, RemovedLength, InsertedStart, InsertedLength;
        }

        struct Step
        {
            public int SpliceStart, SpliceCount;
            public LetteraSelectionRange Before, After;
            public Kind Kind;
            public TokenClass Token;
            public double Time;
        }

        readonly ValueList<Splice> m_Splices = new ValueList<Splice>();
        readonly ValueList<Step> m_Steps = new ValueList<Step>();
        readonly List<char> m_Chars = new List<char>();
        int m_Cursor;       // Steps below it can be undone; from it on, redone.
        bool m_Sealed = true;
        int m_GroupDepth;
        bool m_GroupHasStep;

        public bool CanUndo => m_Cursor > 0;
        public bool CanRedo => m_Cursor < m_Steps.Count;
        public int Depth => m_Cursor;

        /// <summary>The next edit starts a step of its own.</summary>
        public void Seal() => m_Sealed = m_GroupDepth == 0 || m_Sealed;

        public void Clear()
        {
            m_Steps.Clear();
            m_Splices.Clear();
            m_Chars.Clear();
            m_Cursor = 0;
            m_Sealed = true;
        }

        public void BeginGroup()
        {
            if (m_GroupDepth++ == 0)
            {
                m_Sealed = true;
                m_GroupHasStep = false;
            }
        }

        public void EndGroup()
        {
            if (m_GroupDepth > 0 && --m_GroupDepth == 0) m_Sealed = true;
        }

        /// <summary>Starts an edit: a new step, or the continuation of the last one by the merging rules.</summary>
        public void BeginEdit(Kind kind, TokenClass token, LetteraSelectionRange before, double now, float mergeWindow)
        {
            if (m_Cursor < m_Steps.Count) TruncateRedo();
            if (m_GroupDepth > 0)
            {
                if (m_GroupHasStep) return;
                m_GroupHasStep = true;
                PushStep(Kind.Other, token, before, now);
                m_Sealed = false;
                return;
            }
            if (!m_Sealed && m_Steps.Count > 0 && Merges(m_Steps[m_Steps.Count - 1], kind, token, before, now, mergeWindow))
            {
                Step last = m_Steps[m_Steps.Count - 1];
                last.Time = now;
                last.Token = token;
                m_Steps[m_Steps.Count - 1] = last;
                return;
            }
            PushStep(kind, token, before, now);
            m_Sealed = kind == Kind.Other;
        }

        static bool Merges(in Step last, Kind kind, TokenClass token, LetteraSelectionRange before, double now, float window)
        {
            if (kind == Kind.Other || kind != last.Kind) return false;
            if (now - last.Time >= window) return false;
            if (!before.IsCaret) return false;
            if (kind != Kind.Typing) return true;
            // A word then a space starts a new step, and that space goes with the word after it.
            return token == last.Token || last.Token == TokenClass.Space;
        }

        void PushStep(Kind kind, TokenClass token, LetteraSelectionRange before, double now)
        {
            m_Steps.Add(new Step { SpliceStart = m_Splices.Count, Before = before, After = before, Kind = kind, Token = token, Time = now });
            m_Cursor = m_Steps.Count;
        }

        void TruncateRedo()
        {
            Step first = m_Steps[m_Cursor];
            m_Steps.RemoveRange(m_Cursor, m_Steps.Count - m_Cursor);
            int charsFrom = m_Splices[first.SpliceStart].RemovedStart;
            m_Splices.RemoveRange(first.SpliceStart, m_Splices.Count - first.SpliceStart);
            m_Chars.RemoveRange(charsFrom, m_Chars.Count - charsFrom);
            m_Sealed = true;
        }

        /// <summary>Adds one splice to the step in progress: at a source position, these characters went away and those came.</summary>
        public void AddSplice(int position, ReadOnlySpan<char> source, int removedLength, string inserted)
        {
            var splice = new Splice { Position = position, RemovedStart = m_Chars.Count, RemovedLength = removedLength, InsertedLength = inserted.Length };
            for (int i = 0; i < removedLength; i++) m_Chars.Add(source[position + i]);
            splice.InsertedStart = m_Chars.Count;
            for (int i = 0; i < inserted.Length; i++) m_Chars.Add(inserted[i]);
            m_Splices.Add(splice);
            Step last = m_Steps[m_Steps.Count - 1];
            last.SpliceCount++;
            m_Steps[m_Steps.Count - 1] = last;
        }

        /// <summary>Adds one splice to the step in progress: at a source position, these characters went away and those came.</summary>
        public void AddSplice(int position, StringBuilder source, int removedLength, string inserted)
        {
            var splice = new Splice { Position = position, RemovedStart = m_Chars.Count, RemovedLength = removedLength, InsertedLength = inserted.Length };
            for (int i = 0; i < removedLength; i++) m_Chars.Add(source[position + i]);
            splice.InsertedStart = m_Chars.Count;
            for (int i = 0; i < inserted.Length; i++) m_Chars.Add(inserted[i]);
            m_Splices.Add(splice);
            Step last = m_Steps[m_Steps.Count - 1];
            last.SpliceCount++;
            m_Steps[m_Steps.Count - 1] = last;
        }

        public void EndEdit(LetteraSelectionRange after, int memoryLimit)
        {
            Step last = m_Steps[m_Steps.Count - 1];
            last.After = after;
            m_Steps[m_Steps.Count - 1] = last;
            if (memoryLimit > 0) Evict(memoryLimit);
        }

        // Oldest steps go first. The newest always stays.
        void Evict(int limitBytes)
        {
            int limit = limitBytes / 2;
            if (m_Chars.Count <= limit || m_GroupDepth > 0) return;
            int drop = 0;
            int steps = m_Steps.Count;
            while (drop < steps - 1)
            {
                Step next = m_Steps[drop + 1];
                if (m_Chars.Count - m_Splices[next.SpliceStart].RemovedStart <= limit) { drop++; break; }
                drop++;
            }
            if (drop == 0) return;
            Step keep = m_Steps[drop];
            int spliceShift = keep.SpliceStart, charShift = m_Splices[keep.SpliceStart].RemovedStart;
            m_Steps.RemoveRange(0, drop);
            m_Splices.RemoveRange(0, spliceShift);
            m_Chars.RemoveRange(0, charShift);
            for (int i = 0; i < m_Steps.Count; i++)
            {
                Step s = m_Steps[i];
                s.SpliceStart -= spliceShift;
                m_Steps[i] = s;
            }
            for (int i = 0; i < m_Splices.Count; i++)
            {
                Splice s = m_Splices[i];
                s.RemovedStart -= charShift;
                s.InsertedStart -= charShift;
                m_Splices[i] = s;
            }
            m_Cursor = Math.Max(m_Cursor - drop, 0);
        }

        /// <summary>Moves every recorded position, when what they count from moved.</summary>
        public void Rebase(int delta)
        {
            if (delta == 0) return;
            for (int i = 0; i < m_Splices.Count; i++)
            {
                Splice splice = m_Splices[i];
                splice.Position += delta;
                m_Splices[i] = splice;
            }
        }

        /// <summary>Where the next undo or redo would act, in source positions. False when there is none.</summary>
        public bool PeekPosition(bool undo, out int position)
        {
            position = 0;
            if (undo ? !CanUndo : !CanRedo) return false;
            Step step = m_Steps[undo ? m_Cursor - 1 : m_Cursor];
            if (step.SpliceCount == 0) return false;
            position = m_Splices[step.SpliceStart].Position;
            return true;
        }

        /// <summary>Takes back the newest step on a source. False when there is none.</summary>
        public bool Undo(StringBuilder source, out LetteraSelectionRange selection)
        {
            selection = default;
            if (!CanUndo) return false;
            Step step = m_Steps[--m_Cursor];
            for (int i = step.SpliceCount - 1; i >= 0; i--)
            {
                Splice s = m_Splices[step.SpliceStart + i];
                source.Remove(s.Position, s.InsertedLength);
                InsertChars(source, s.Position, s.RemovedStart, s.RemovedLength);
            }
            selection = step.Before;
            m_Sealed = true;
            return true;
        }

        /// <summary>Applies the next undone step again. False when there is none.</summary>
        public bool Redo(StringBuilder source, out LetteraSelectionRange selection)
        {
            selection = default;
            if (!CanRedo) return false;
            Step step = m_Steps[m_Cursor++];
            for (int i = 0; i < step.SpliceCount; i++)
            {
                Splice s = m_Splices[step.SpliceStart + i];
                source.Remove(s.Position, s.RemovedLength);
                InsertChars(source, s.Position, s.InsertedStart, s.InsertedLength);
            }
            selection = step.After;
            m_Sealed = true;
            return true;
        }

        void InsertChars(StringBuilder source, int position, int start, int length)
        {
            for (int i = 0; i < length; i++) source.Insert(position + i, m_Chars[start + i]);
        }

        /// <summary>Maps all steps through a change made outside the history: steps that touch it go away.</summary>
        public void Rebase(int changeStart, int removed, int inserted)
        {
            int delta = inserted - removed;
            for (int s = m_Steps.Count - 1; s >= 0; s--)
            {
                Step step = m_Steps[s];
                bool touches = false;
                for (int i = 0; i < step.SpliceCount; i++)
                {
                    Splice splice = m_Splices[step.SpliceStart + i];
                    if (splice.Position + Math.Max(splice.RemovedLength, splice.InsertedLength) > changeStart) touches = splice.Position < changeStart + Math.Max(removed, 1);
                    if (touches) break;
                }
                if (touches)
                {
                    // What came before this step can no longer be replayed either.
                    m_Steps.RemoveRange(0, s + 1);
                    m_Cursor = Math.Max(m_Cursor - (s + 1), 0);
                    break;
                }
            }
            for (int i = 0; i < m_Steps.Count; i++)
            {
                Step step = m_Steps[i];
                for (int k = 0; k < step.SpliceCount; k++)
                {
                    Splice splice = m_Splices[step.SpliceStart + k];
                    if (splice.Position >= changeStart + removed)
                    {
                        splice.Position += delta;
                        m_Splices[step.SpliceStart + k] = splice;
                    }
                }
            }
            m_Sealed = true;
        }
    }
}

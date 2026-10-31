using System;
using System.Collections.Generic;
using UnityEngine;

namespace FirstSymbol.Lettera
{
    public enum LetteraHostMode { Mirror, Replica }

    public enum LetteraHostEventKind { Replace, WholeText, Compose, ComposeEnd, Select, Verb, Key, KeyboardFrame, Closed }

    /// <summary>What a platform's control holds, or is told to hold. Positions are UTF-16 units of <see cref="Text"/>, which is a window of the field past a certain length.</summary>
    public struct LetteraMirrorState
    {
        public string Text;
        public int Offset, SelectionStart, SelectionEnd, Revision;
        public bool Composing;
    }

    /// <summary>Something a platform reports. Never a decision: it says what happened, and the field decides.</summary>
    public struct LetteraHostEvent
    {
        public LetteraHostEventKind Kind;

        /// <summary>The state the control held when this happened.</summary>
        public int BaseRevision;

        public int Start, End, Caret;
        public string Text;
        public LetteraClause[] Clauses;
        public bool Commit;
        public LetteraKeyCode KeyCode;
        public LetteraKeyModifiers Modifiers;
        public bool Repeat;
        public Rect Frame;
    }

    /// <summary>A platform's text control, from the protocol's side. Hosts only report events and show states. They hold no rule, filter or undo.</summary>
    public interface ILetteraNativeHost
    {
        bool IsAvailable { get; }
        void Open(in LetteraMirrorState state, in LetteraKeyboardTraits traits, LetteraHostMode mode);

        /// <summary>The field changed something.</summary>
        void Push(in LetteraMirrorState state);

        /// <summary>Where the caret and the field are, in screen pixels from the bottom left.</summary>
        void Place(Rect caret, Rect field);

        /// <summary>A live change. It does not end a composition.</summary>
        void Reconfigure(in LetteraKeyboardTraits traits);

        void Close();

        /// <summary>Hands over what happened since the last call. Called once a frame, on the main thread.</summary>
        int Drain(LetteraHostEvent[] into);
    }

    /// <summary>What a field offers a mirror beyond what any input source can ask.</summary>
    public interface ILetteraMirrorField
    {
        /// <summary>An exact change, in UTF-16 units of the field's plain text.</summary>
        void ReplaceRange(int start, int end, string text, string reason);

        /// <summary>A selection, in UTF-16 units of the field's plain text.</summary>
        void SelectRange(int start, int end);

        /// <summary>A composition that takes over a range already in the text, as an autocorrect reopening a word does. The range goes when it commits.</summary>
        void ComposeOver(int start, int end, string text, int caret, LetteraClause[] clauses);

        /// <summary>Selects everything in the field, beyond what a partial mirror holds.</summary>
        void SelectAll();
    }

    /// <summary>
    /// One protocol for every platform's text control, with no platform code in it. The field is the only authority: whatever a control produces, by keys, voice,
    /// autofill or paste, enters as a proposal that may be refused, and the control then follows the field.
    /// </summary>
    public sealed class LetteraMirrorSource : ILetteraInputSource
    {
        internal const int WholeTextUnits = 4096, WindowUnits = 2048, RecentreWithin = 256, PatchHistory = 16;

        struct Patch
        {
            public int Revision, Start, Removed, Inserted;
        }

        readonly ILetteraNativeHost m_Host;
        readonly LetteraHostEvent[] m_Events = new LetteraHostEvent[64];
        readonly List<Patch> m_Patches = new List<Patch>(PatchHistory);
        readonly LetteraHostMode m_Mode;
        ILetteraInputSink m_Sink;
        ILetteraInputHost m_Field;
        LetteraMirrorState m_Sent;
        string m_FieldText = "";
        LetteraSelectionRange m_SentSelection;
        Rect m_LastCaret;
        bool m_Composing, m_Failed;
        int m_Revision;

        public LetteraMirrorSource(ILetteraNativeHost host, LetteraHostMode mode = LetteraHostMode.Mirror)
        {
            m_Host = host;
            m_Mode = mode;
        }

        public LetteraMirrorState Sent => m_Sent;

        public void Attach(ILetteraInputSink sink, ILetteraInputHost host)
        {
            m_Sink = sink;
            m_Field = host;
            m_Revision = 1;
            m_Sent = MakeState();
            Guarded(() => m_Host.Open(m_Sent, host.Traits, m_Mode));
        }

        public void Detach()
        {
            Guarded(m_Host.Close);
            m_Sink = null;
            m_Field = null;
            m_Patches.Clear();
            m_Composing = false;
        }

        // A host's failure must not take the field with it: its feature goes off and the field keeps what it has.
        void Guarded(Action call)
        {
            if (m_Failed) return;
            try
            {
                call();
            }
            catch (Exception e)
            {
                m_Failed = true;
                LetteraPlatform.MarkFailed(LetteraNativeFeature.TextInput, e.Message);
            }
        }

        LetteraMirrorState MakeState()
        {
            string whole = m_Field.CommittedPlainText ?? "";
            m_FieldText = whole;
            LetteraSelectionRange selection = m_Field.Selection;
            m_SentSelection = selection;
            int start = LetteraEditProposal.Utf16Of(whole, selection.Start), end = LetteraEditProposal.Utf16Of(whole, selection.End);
            int offset = 0, length = whole.Length;
            if (whole.Length > WholeTextUnits)
            {
                // Only context travels: an OS control must not hold a long document.
                offset = SnapToCluster(whole, Mathf.Max(0, Mathf.Min((start + end) / 2 - WindowUnits / 2, whole.Length - WindowUnits)));
                int stop = SnapToCluster(whole, Mathf.Min(offset + WindowUnits, whole.Length));
                length = stop - offset;
            }
            return new LetteraMirrorState
            {
                Text = whole.Substring(offset, length),
                Offset = offset,
                SelectionStart = Mathf.Clamp(start - offset, 0, length),
                SelectionEnd = Mathf.Clamp(end - offset, 0, length),
                Revision = m_Revision,
                Composing = m_Composing,
            };
        }

        // A window never starts inside a surrogate pair or a cluster of joined marks.
        static int SnapToCluster(string text, int at)
        {
            at = Mathf.Clamp(at, 0, text.Length);
            while (at > 0 && at < text.Length && IsExtender(text[at])) at--;
            return at;
        }

        static bool IsExtender(char c) => char.IsLowSurrogate(c) || c == '‍' || (c >= '︀' && c <= '️') || (c >= '̀' && c <= 'ͯ') || (c > 0 && char.GetUnicodeCategory(c) == System.Globalization.UnicodeCategory.NonSpacingMark);

        public void Pump()
        {
            if (m_Sink == null || m_Failed) return;
            int count = 0;
            Guarded(() => count = m_Host.Drain(m_Events));
            for (int i = 0; i < count && m_Sink != null; i++) Handle(m_Events[i]);
            if (m_Sink == null) return;
            PushIfChanged();
            PlaceIfMoved();
        }

        void Handle(in LetteraHostEvent e)
        {
            switch (e.Kind)
            {
                case LetteraHostEventKind.Replace:
                    if (TryMap(e.BaseRevision, e.Start, e.End, out int start, out int end)) ApplyReplace(start, end, e.Text);
                    else ForcePush();
                    break;
                case LetteraHostEventKind.WholeText:
                    ApplyWhole(e);
                    break;
                case LetteraHostEventKind.Compose:
                    m_Composing = true;
                    if (e.End > e.Start && m_Sink is ILetteraMirrorField over && TryMap(e.BaseRevision, e.Start, e.End, out int a, out int b)) over.ComposeOver(a, b, e.Text, e.Caret, e.Clauses);
                    else m_Sink.Composition(e.Text ?? "", e.Caret, e.Clauses);
                    break;
                case LetteraHostEventKind.ComposeEnd:
                    m_Composing = false;
                    m_Sink.CompositionEnd(e.Commit);
                    break;
                case LetteraHostEventKind.Select:
                    if (!m_Composing && m_Sink is ILetteraMirrorField selecting && TryMap(e.BaseRevision, e.Start, e.End, out int from, out int to))
                    {
                        // The whole of what a partial mirror holds means the whole field to whoever asked for it.
                        selecting.SelectRange(from, to);
                        // The control already shows this, so saying it back would only be an echo.
                        m_SentSelection = m_Field.Selection;
                        m_Sent.SelectionStart = e.Start;
                        m_Sent.SelectionEnd = e.End;
                    }
                    break;
                case LetteraHostEventKind.Verb:
                    if (e.Text == "selectAll" && m_Sink is ILetteraMirrorField all) all.SelectAll();
                    else m_Sink.Action(e.Text);
                    break;
                case LetteraHostEventKind.Key:
                    m_Sink.Key(e.KeyCode, e.Modifiers, e.Repeat);
                    break;
                case LetteraHostEventKind.Closed:
                    m_Sink.Action("done");
                    break;
            }
        }

        // An event made against an older state moves through what was pushed since. One older than that record is dropped: a lost character, never corrupted text.
        bool TryMap(int baseRevision, int start, int end, out int mappedStart, out int mappedEnd)
        {
            mappedStart = start + m_Sent.Offset;
            mappedEnd = end + m_Sent.Offset;
            if (baseRevision >= m_Sent.Revision) return true;
            if (m_Sent.Revision - baseRevision > m_Patches.Count) return false;
            for (int p = m_Patches.Count - (m_Sent.Revision - baseRevision); p < m_Patches.Count; p++)
            {
                Patch patch = m_Patches[p];
                mappedStart = MapOne(mappedStart, patch);
                mappedEnd = MapOne(mappedEnd, patch);
            }
            return true;
        }

        static int MapOne(int at, in Patch patch)
        {
            if (at <= patch.Start) return at;
            if (at >= patch.Start + patch.Removed) return at + patch.Inserted - patch.Removed;
            return patch.Start + patch.Inserted;
        }

        void ApplyReplace(int start, int end, string text)
        {
            text = text ?? "";
            if (m_Sink is ILetteraMirrorField field) field.ReplaceRange(start, end, text, text.Length > 0 ? "user.type" : "user.delete.back");
            else if (m_Sink is ILetteraKeyboardSink keyboard)
            {
                string whole = m_Field.CommittedPlainText ?? "";
                start = Mathf.Clamp(start, 0, whole.Length);
                end = Mathf.Clamp(end, start, whole.Length);
                keyboard.Reconcile(whole.Substring(0, start) + text + whole.Substring(end), start + text.Length);
            }
            AfterEdit();
        }

        // A control that says only what it holds now: what changed is worked out from that, against what it was last told.
        void ApplyWhole(in LetteraHostEvent e)
        {
            string text = e.Text ?? "";
            string old = m_Sent.Text ?? "";
            if (text == old) return;
            int prefix = 0, oldEnd = old.Length, newEnd = text.Length;
            int floor = e.Caret >= 0 ? Mathf.Min(e.Caret, text.Length) : 0;
            while (oldEnd > 0 && newEnd > floor && old[oldEnd - 1] == text[newEnd - 1])
            {
                oldEnd--;
                newEnd--;
            }
            while (prefix < oldEnd && prefix < newEnd && old[prefix] == text[prefix]) prefix++;
            if (prefix > 0 && prefix < old.Length && char.IsLowSurrogate(old[prefix])) prefix--;
            if (oldEnd < prefix) oldEnd = prefix;
            if (newEnd < prefix) newEnd = prefix;
            if (oldEnd < old.Length && char.IsLowSurrogate(old[oldEnd]))
            {
                oldEnd++;
                newEnd++;
            }
            ApplyReplace(prefix + m_Sent.Offset, oldEnd + m_Sent.Offset, text.Substring(prefix, newEnd - prefix));
        }

        void AfterEdit()
        {
            // What the field made of it goes back: a refused or rewritten edit puts the control right. Also when nothing seems to have changed, since the control did.
            ForcePush();
        }

        void ForcePush()
        {
            if (m_Field == null) return;
            string before = m_FieldText;
            int oldOffset = m_Sent.Offset;
            m_Revision++;
            m_Sent = MakeState();
            Record(before, m_FieldText);
            if (m_Composing) return;
            LetteraMirrorState state = m_Sent;
            Guarded(() => m_Host.Push(state));
            _ = oldOffset;
        }

        void Record(string before, string after)
        {
            int prefix = 0, limit = Mathf.Min(before.Length, after.Length);
            while (prefix < limit && before[prefix] == after[prefix]) prefix++;
            int suffix = 0;
            while (suffix < limit - prefix && before[before.Length - 1 - suffix] == after[after.Length - 1 - suffix]) suffix++;
            if (m_Patches.Count == PatchHistory) m_Patches.RemoveAt(0);
            m_Patches.Add(new Patch { Revision = m_Revision, Start = prefix, Removed = before.Length - prefix - suffix, Inserted = after.Length - prefix - suffix });
        }

        // Nothing moves during a composition, and with no change nothing is sent.
        void PushIfChanged()
        {
            if (m_Composing) return;
            string whole = m_Field.CommittedPlainText ?? "";
            LetteraSelectionRange selection = m_Field.Selection;
            bool sameText = ReferenceEquals(whole, m_FieldText) || whole == m_FieldText;
            if (sameText && selection == m_SentSelection && !NeedsRecentring(whole, selection)) return;
            ForcePush();
        }

        bool NeedsRecentring(string whole, LetteraSelectionRange selection)
        {
            if (whole.Length <= WholeTextUnits) return false;
            int head = LetteraEditProposal.Utf16Of(whole, selection.Head) - m_Sent.Offset;
            int length = m_Sent.Text?.Length ?? 0;
            return (head < RecentreWithin && m_Sent.Offset > 0) || (head > length - RecentreWithin && m_Sent.Offset + length < whole.Length);
        }

        void PlaceIfMoved()
        {
            Rect caret = m_Field.CaretScreenRect;
            if (caret == m_LastCaret) return;
            m_LastCaret = caret;
            Guarded(() => m_Host.Place(caret, caret));
        }

        /// <summary>A trait changed while the session runs.</summary>
        public void Reconfigure(in LetteraKeyboardTraits traits)
        {
            LetteraKeyboardTraits copy = traits;
            Guarded(() => m_Host.Reconfigure(copy));
        }
    }
}

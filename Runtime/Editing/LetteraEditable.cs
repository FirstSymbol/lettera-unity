using System;
using System.Collections.Generic;
using System.Text;
using FirstSymbol.Lettera.Unicode;
using UnityEngine;

namespace FirstSymbol.Lettera
{
    public enum LetteraMarkupView { Hidden, RevealAtCaret, Source }
    public enum LetteraTypedMarkup { AsMarkup, AsText }
    public enum LetteraPlainPaste { Auto, AsText, AsMarkup }
    public enum LetteraHistoryMode { Clear, Record, Rebase }

    /// <summary>One committed change of the source, in shown indices.</summary>
    public readonly struct LetteraEditPatch
    {
        public readonly int Start, Removed, Inserted;
        public readonly string Reason;

        public LetteraEditPatch(int start, int removed, int inserted, string reason)
        {
            Start = start;
            Removed = removed;
            Inserted = inserted;
            Reason = reason;
        }

        public int Delta => Inserted - Removed;

        /// <summary>Where an index from before the change stands after it.</summary>
        public int Map(int index) => index <= Start ? index : index >= Start + Removed ? index + Delta : Start + Inserted;
    }

    /// <summary>The value of a field, for what only reads it.</summary>
    public interface ILetteraTextView
    {
        int Length { get; }
        string PlainText { get; }
        int CodepointAt(int index);
    }

    /// <summary>Makes a text an editable field: document, undo, input rules, caret, composition and paste. Needs a LetteraSelectable.</summary>
    [AddComponentMenu("Lettera/Editable")]
    [RequireComponent(typeof(LetteraSelectable))]
    [DisallowMultipleComponent]
    [ExecuteAlways]
    public partial class LetteraEditable : MonoBehaviour, ILetteraTextView
    {
        [SerializeField] bool m_ReadOnly;
        [SerializeField] bool m_Interactable = true;
        [SerializeReference] List<LetteraFieldRule> m_Rules = new List<LetteraFieldRule>();
        [SerializeField] List<LetteraFieldPreset> m_RulePresets = new List<LetteraFieldPreset>();
        [SerializeField] LetteraCaret m_Caret;
        [SerializeField] LetteraMarkupView m_MarkupView;
        [SerializeField] LetteraTypedMarkup m_TypedMarkup;
        [SerializeField] LetteraPlainPaste m_PlainPaste;
        [SerializeField] bool m_PasteKeepsStyles = true;

        LetteraSelectable m_Selectable;
        readonly LetteraEditHistory m_History = new LetteraEditHistory();
        readonly StringBuilder m_Builder = new StringBuilder();
        readonly List<Vector2Int> m_Spans = new List<Vector2Int>();
        readonly List<Splice> m_Splices = new List<Splice>();
        readonly List<LetteraFieldRule> m_ActiveRules = new List<LetteraFieldRule>();
        int m_Version;
        bool m_Writing, m_Attached;
        bool m_ShownChanged, m_CaretStylesDirty;
        string m_LastShownText = "";
        double m_BlinkStart;
        bool m_CaretCreated;
        LetteraCheckResult m_LastCheck;

        struct Splice
        {
            public int Position, Removed;
            public string Inserted;
        }

        /// <summary>The field that has the keyboard, or null. What toolbars act on.</summary>
        public static LetteraEditable Current => LetteraSelectable.Current != null ? LetteraSelectable.Current.Editable : null;

        /// <summary>Raised on each committed change, right after Version rises and before ValueChanged.</summary>
        public event Action<LetteraEditPatch> Patched;
        public event Action<string> ValueChanged;
        public event Action<string> EditReported;
        public event Action<string> EditRejected;
        /// <summary>At most once per frame, when what the field shows differs, composition included.</summary>
        public event Action ShownTextChanged;
        public event Action<string> Submitted;
        public event Action Cancelled;
        public event Action FocusGained;
        public event Action<LetteraBlurReason> FocusLost;
        public event Action<bool> ComposingChanged;
        public event Action<LetteraCheckResult> CheckChanged;

        public LetteraSelectable Selectable => m_Selectable != null ? m_Selectable : m_Selectable = GetComponent<LetteraSelectable>();
        ITextComponent Host => Selectable.Host;
        TextEngine Engine => Selectable.Engine;

        public bool ReadOnly { get => m_ReadOnly; set { m_ReadOnly = value; PushSnapshot(); } }
        public bool Interactable { get => m_Interactable; set { m_Interactable = value; if (!value && Selectable.HasFocus) Selectable.EndSession(LetteraBlurReason.Code); } }
        public int Version => m_Version;
        public LetteraTypedMarkup TypedMarkup { get => m_TypedMarkup; set => m_TypedMarkup = value; }
        public LetteraPlainPaste PlainPaste { get => m_PlainPaste; set => m_PlainPaste = value; }
        public bool PasteKeepsStyles { get => m_PasteKeepsStyles; set => m_PasteKeepsStyles = value; }
        public LetteraCheckResult LastCheck => m_LastCheck;
        public List<LetteraFieldRule> Rules => m_Rules;
        public List<LetteraFieldPreset> RulePresets => m_RulePresets;

        public LetteraMarkupView MarkupView
        {
            get => m_MarkupView;
            set
            {
                if (m_MarkupView == value) return;
                CommitComposition();
                ChangeViewKeepingSelection(() =>
                {
                    m_MarkupView = value;
                    ApplyView();
                });
            }
        }

        // ------------------------------------------------------------- life

        void OnEnable()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            LetteraWebFields.Add(this);
#endif
            if (Selectable.Host == null) return;
            Selectable.Editable = this;
            Selectable.MarkVisualsDirty();
            m_Version = Mathf.Max(m_Version, 0);
            AttachRules();
            ApplyView();
            m_LastShownText = "";
            if (LetteraSettings.Instance != null) PushSnapshot();
        }

        void OnDisable()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            LetteraWebFields.Remove(this);
#endif
            if (Selectable == null || Selectable.Host == null) return;
            if (Selectable.HasFocus) Selectable.EndSession(LetteraBlurReason.Disabled);
            CommitComposition();
            DetachRules();
            if (Selectable.Editable == this) Selectable.Editable = null;
            Engine.Literal = false;
            Engine.RevealSource = -1;
            Engine.SuspendElide = false;
            Selectable.Host.Dirty(DirtyLevel.Parse);
            DestroyCaret();
            Selectable.MarkVisualsDirty();
        }

        void ApplyView()
        {
            Engine.Literal = m_MarkupView == LetteraMarkupView.Source || RuleWantsLiteral;
            if (m_MarkupView != LetteraMarkupView.RevealAtCaret) Engine.RevealSource = -1;
            Host.Dirty(Engine.Dirty);
        }

        /// <summary>A password or a mask holds a plain value, whatever it contains.</summary>
        internal bool RuleWantsLiteral { get; set; }

        internal void RefreshView() => ChangeViewKeepingSelection(ApplyView);

        // The same characters stay selected across a change of what the text shows.
        void ChangeViewKeepingSelection(Action change)
        {
            LetteraSelectionRange selection = Selectable.Selection;
            Engine.EnsureParsed();
            SourceEnds(selection, out int anchor, out int head);
            change();
            Engine.EnsureParsed();
            Selectable.SetSelectionUnchecked(new LetteraSelectionRange(Engine.RenderedIndexFromSource(anchor), Engine.RenderedIndexFromSource(head), selection.Bias), "sel.restore");
            Host.Dirty(DirtyLevel.Parse);
        }

        // A range's far end stands right after its last character, so delimiters that follow it stay out.
        void SourceEnds(LetteraSelectionRange selection, out int anchor, out int head)
        {
            int start = Engine.SourceStartOf(selection.Start);
            int end = selection.IsCaret ? start : Engine.SourceEndOf(selection.End - 1);
            anchor = selection.Anchor <= selection.Head ? start : end;
            head = selection.Anchor <= selection.Head ? end : start;
        }

        // ------------------------------------------------------------- value

        /// <summary>The source with its markup. Setting it replaces the whole text and clears the history.</summary>
        public string Source
        {
            get => m_CompText != null ? m_SourceDuringComposition : Engine.RawText;
            set => SetSource(value);
        }

        string m_SourceDuringComposition;

        ReadOnlySpan<int> Committed
        {
            get
            {
                if (m_CompText != null) return new ReadOnlySpan<int>(m_CommittedCps, 0, m_CommittedLength);
                Engine.EnsureParsed();
                return Engine.ShownCodepoints;
            }
        }

        /// <summary>Committed codepoints.</summary>
        public int Length => Committed.Length;

        /// <summary>The committed text without markup. A new string; to poll each frame, use CopyTo or Equals.</summary>
        public string PlainText
        {
            get
            {
                ReadOnlySpan<int> text = Committed;
                return LetteraSelectable.PlainOf(text, 0, text.Length);
            }
        }

        /// <summary>What the field shows: composition included, and mask characters under a password.</summary>
        public string ShownText
        {
            get
            {
                Engine.EnsureParsed();
                ReadOnlySpan<int> text = Engine.ShownCodepoints;
                if (Engine.MaskGlyph == 0) return LetteraSelectable.PlainOf(text, 0, text.Length);
                var builder = new StringBuilder();
                string mask = char.ConvertFromUtf32(Engine.MaskGlyph);
                int i = 0;
                while (i < text.Length)
                {
                    int next = LetteraTextNavigator.NextStop(text, i);
                    if (i >= m_CompStart && i < m_CompStart + m_CompLength) builder.Append(LetteraSelectable.PlainOf(text, i, next));
                    else builder.Append(mask);
                    i = next;
                }
                return builder.ToString();
            }
        }

        public int CodepointAt(int index) => Committed[index];

        /// <summary>Copies committed codepoints without allocating.</summary>
        public int CopyRange(int start, int count, Span<int> target)
        {
            ReadOnlySpan<int> text = Committed;
            count = Mathf.Min(count, Mathf.Min(text.Length - start, target.Length));
            text.Slice(start, count).CopyTo(target);
            return count;
        }

        /// <summary>Writes the committed text without allocating. Returns the UTF-16 length, or -1 when the target is too short.</summary>
        public int CopyTo(Span<char> target)
        {
            ReadOnlySpan<int> text = Committed;
            int at = 0;
            foreach (int cp in text)
            {
                if (cp > 0xFFFF)
                {
                    if (at + 2 > target.Length) return -1;
                    int v = cp - 0x10000;
                    target[at++] = (char)(0xD800 + (v >> 10));
                    target[at++] = (char)(0xDC00 + (v & 0x3FF));
                }
                else
                {
                    if (at >= target.Length) return -1;
                    target[at++] = (char)cp;
                }
            }
            return at;
        }

        /// <summary>Compares with the committed text without allocating.</summary>
        public bool Equals(ReadOnlySpan<char> other)
        {
            ReadOnlySpan<int> text = Committed;
            int at = 0;
            foreach (int cp in text)
            {
                if (cp > 0xFFFF)
                {
                    int v = cp - 0x10000;
                    if (at + 2 > other.Length || other[at] != (char)(0xD800 + (v >> 10)) || other[at + 1] != (char)(0xDC00 + (v & 0x3FF))) return false;
                    at += 2;
                }
                else
                {
                    if (at >= other.Length || other[at] != (char)cp) return false;
                    at++;
                }
            }
            return at == other.Length;
        }

        /// <summary>Replaces the whole source. Filters, masks and limits do not run on it.</summary>
        public bool SetSource(string value, LetteraHistoryMode mode = LetteraHistoryMode.Clear, string reason = "code.set")
        {
            value = value ?? "";
            CommitComposition();
            string old = Engine.RawText;
            if (value == old) return false;
            int prefix = 0, oldEnd = old.Length, newEnd = value.Length;
            while (prefix < oldEnd && prefix < newEnd && old[prefix] == value[prefix]) prefix++;
            while (oldEnd > prefix && newEnd > prefix && old[oldEnd - 1] == value[newEnd - 1]) { oldEnd--; newEnd--; }

            LetteraSelectionRange before = Selectable.Selection;
            int anchorSource = 0, headSource = 0;
            if (mode != LetteraHistoryMode.Clear)
            {
                Engine.EnsureParsed();
                anchorSource = MapSourceThrough(Engine.SourceStartOf(before.Anchor), prefix, oldEnd, newEnd);
                headSource = MapSourceThrough(Engine.SourceStartOf(before.Head), prefix, oldEnd, newEnd);
            }
            if (mode == LetteraHistoryMode.Record)
            {
                m_History.BeginEdit(LetteraEditHistory.Kind.Other, LetteraEditHistory.TokenClass.Other, before, LetteraEditClock.Now, 0);
                m_Builder.Clear();
                m_Builder.Append(old);
                m_History.AddSplice(prefix, m_Builder, oldEnd - prefix, value.Substring(prefix, newEnd - prefix));
            }
            else if (mode == LetteraHistoryMode.Rebase)
            {
                m_History.Rebase(prefix, oldEnd - prefix, newEnd - prefix);
            }
            else
            {
                m_History.Clear();
            }
            WriteSource(value);
            LetteraSelectionRange after = mode == LetteraHistoryMode.Clear
                ? LetteraSelectionRange.AtCaret(Engine.ShownLength)
                : new LetteraSelectionRange(Engine.RenderedIndexFromSource(anchorSource), Engine.RenderedIndexFromSource(headSource), before.Bias);
            if (mode == LetteraHistoryMode.Record) m_History.EndEdit(after, LetteraSettings.Instance.Editing.UndoMemoryLimit);
            Selectable.SetSelectionUnchecked(after, "sel.edit");
            RaiseCommitted(new LetteraEditPatch(0, 0, 0, reason), reason);
            return true;
        }

        static int MapSourceThrough(int position, int start, int oldEnd, int newEnd) =>
            position <= start ? position : position >= oldEnd ? position + newEnd - oldEnd : newEnd;

        void WriteSource(string value)
        {
            m_Writing = true;
            if (Engine.IsWindowed) Engine.WriteWindowSource(value);
            else Host.SourceText = value;
            m_Writing = false;
            KeepHistoryOnWindow();
            Engine.EnsureParsed();
            m_ShownChanged = true;
            m_CaretStylesDirty = true;
        }

        internal void OnSourceWrittenFromOutside()
        {
            if (m_Writing) return;
            // A script set the component's text: the same as SetSource with the defaults.
            AbandonComposition();
            m_History.Clear();
            Engine.EnsureParsed();
            Selectable.SetSelectionUnchecked(LetteraSelectionRange.AtCaret(Engine.ShownLength), "sel.edit");
            m_ShownChanged = true;
            RaiseCommitted(new LetteraEditPatch(0, 0, 0, "code.set"), "code.set");
        }

        void RaiseCommitted(in LetteraEditPatch patch, string reason)
        {
            m_Version++;
            m_BlinkStart = LetteraEditClock.Now;
            Patched?.Invoke(patch);
            string source = ValueChanged != null ? Engine.RawText : null;
            ValueChanged?.Invoke(source);
            EditReported?.Invoke(reason);
            RunCheck(LetteraCheckTrigger.Change);
            PushSnapshot();
            Selectable.HideMenuOnEdit();
            NotifyKeyboard();
        }

        // ------------------------------------------------------------- sanitizing

        // Returns null to reject the whole edit.
        static string Sanitize(string text)
        {
            if (text.IndexOf('\0') >= 0) return null;
            StringBuilder builder = null;
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                // This character means an inline item in the shown text, and only a tag may put one there.
                bool drop = c == '\uFFFC';
                char? swap = null;
                if (c == '\r')
                {
                    swap = '\n';
                    if (i + 1 < text.Length && text[i + 1] == '\n') drop = true;
                }
                else if ((c < 0x20 && c != '\t' && c != '\n' && c != 0x0B && c != 0x0C) || c == 0x7F)
                {
                    drop = true;
                }
                else if (char.IsHighSurrogate(c))
                {
                    if (i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
                    {
                        builder?.Append(c).Append(text[i + 1]);
                        i++;
                        continue;
                    }
                    drop = true;
                }
                else if (char.IsLowSurrogate(c))
                {
                    drop = true;
                }
                if (drop || swap.HasValue)
                {
                    if (builder == null) builder = new StringBuilder(text, 0, i, text.Length);
                    if (!drop) builder.Append(swap.Value);
                    continue;
                }
                builder?.Append(c);
            }
            return builder != null ? builder.ToString() : text;
        }

        // ------------------------------------------------------------- edits

        /// <summary>Replaces the selection with text, through the rules.</summary>
        public bool InsertText(string text) => InsertText(text, "code.set");

        /// <summary>Replaces the selection with text, through the rules.</summary>
        public bool InsertText(ReadOnlySpan<char> text) => InsertText(text.ToString(), "code.set");

        /// <summary>Replaces the selection with text, through the rules, naming where the change comes from.</summary>
        public bool InsertText(string text, string reason)
        {
            LetteraSelectionRange selection = Selectable.Selection;
            return Replace(selection.Start, selection.End, text, reason);
        }

        /// <summary>Replaces a range of shown text, through the rules. False when nothing changed.</summary>
        public bool Replace(int start, int end, string text, string reason = "code.set")
        {
            if (m_ReadOnly) return false;
            CommitComposition();
            return ProposeEdit(start, end, text ?? "", reason, null);
        }

        internal bool ProposeEdit(int start, int end, string text, string reason, List<LetteraPastedSpan> pasted)
        {
            Engine.EnsureParsed();
            int length = Engine.ShownLength;
            start = Mathf.Clamp(start, 0, length);
            end = Mathf.Clamp(end, start, length);
            text = Sanitize(text);
            if (text == null)
            {
                EditRejected?.Invoke(reason);
                return false;
            }
            if (Engine.AtomicAround(start, out int atomicStart, out _)) start = atomicStart;
            if (Engine.AtomicAround(end, out _, out int atomicEnd)) end = atomicEnd;

            var proposal = new LetteraEditProposal(this, start, end - start, text, reason, start == 0 && end == length);
            foreach (LetteraFieldRule rule in m_ActiveRules)
                if (rule.Enabled)
                    rule.FilterEdit(ref proposal);
            if (proposal.Rejected)
            {
                EditRejected?.Invoke(reason);
                return false;
            }
            if (proposal.Text != text) pasted = null;
            start = Mathf.Clamp(proposal.Start, 0, length);
            end = Mathf.Clamp(proposal.Start + proposal.RemovedLength, start, length);
            text = proposal.Text ?? "";
            if (text.Length == 0 && start == end) return false;
            if (RemovedEquals(start, end, text)) return false;
            ApplyEdit(start, end, text, reason, proposal.CaretAfter, pasted);
            return true;
        }

        bool RemovedEquals(int start, int end, string text)
        {
            if (start == end || text.Length == 0) return false;
            ReadOnlySpan<int> shown = Engine.ShownCodepoints;
            int at = 0;
            for (int i = start; i < end; i++)
            {
                int cp = shown[i];
                if (cp > 0xFFFF)
                {
                    if (at + 1 >= text.Length || !char.IsSurrogatePair(text[at], text[at + 1]) || char.ConvertToUtf32(text[at], text[at + 1]) != cp) return false;
                    at += 2;
                }
                else
                {
                    if (at >= text.Length || text[at] != cp) return false;
                    at++;
                }
            }
            return at == text.Length;
        }

        int m_HistoryBase;

        // What a field records counts from the window's start, which moves as the window does.
        void KeepHistoryOnWindow()
        {
            if (!Engine.IsWindowed) return;
            int now = Engine.WindowSourceBase;
            m_History.Rebase(m_HistoryBase - now);
            m_HistoryBase = now;
        }

        // A selection can reach past what is materialized, and half of it must not go: the edit is refused, and says why, until the rest is implemented.
        bool EditReachesOutOfWindow()
        {
            if (!Engine.IsWindowed || !Engine.View.HasDocumentSelection) return false;
            (int anchor, int head) = Engine.View.DocumentSelection;
            int from = Mathf.Min(anchor, head), to = Mathf.Max(anchor, head);
            if (from >= Engine.WindowStart && to <= Engine.WindowStart + Engine.WindowLength) return false;
            if (!s_WarnedOutOfWindow)
            {
                s_WarnedOutOfWindow = true;
                Debug.LogWarning("Lettera: an edit of a selection reaching past what a document has materialized is not supported yet, so it was refused and nothing changed.", this);
            }
            return true;
        }

        static bool s_WarnedOutOfWindow;

        void ApplyEdit(int start, int end, string text, string reason, int caretAfter, List<LetteraPastedSpan> pasted)
        {
            if (end > start && EditReachesOutOfWindow()) return;
            string source = Engine.RawText;
            bool typing = reason.StartsWith("user.type", StringComparison.Ordinal);
            LetteraSelectionRange before = Selectable.Selection;

            m_Spans.Clear();
            Engine.SourceSpansOf(start, end, m_Spans);
            int insertAt = m_Spans.Count > 0 ? m_Spans[0].x : InsertionPoint(start);

            string written = text;
            if (typing && m_TypedMarkup == LetteraTypedMarkup.AsText && !Engine.Literal) written = EscapeLiteral(text);
            if (pasted != null && pasted.Count > 0) written = WriteSpans(text, pasted);
            else if (m_PendingWritesLiteral && !Engine.Literal) written = EscapeLiteral(text);

            m_Splices.Clear();
            foreach (Vector2Int span in m_Spans)
                m_Splices.Add(new Splice { Position = span.x, Removed = span.y - span.x, Inserted = span.x == insertAt ? written : null });
            if (m_Spans.Count == 0) m_Splices.Add(new Splice { Position = insertAt, Removed = 0, Inserted = written });
            int caretSource = insertAt + written.Length;

            ClassifyEdit(reason, text, out LetteraEditHistory.Kind kind, out LetteraEditHistory.TokenClass token);
            m_History.BeginEdit(kind, token, before, LetteraEditClock.Now, LetteraSettings.Instance.Editing.UndoMergeWindow);
            string value = ApplySplices(source);

            WriteSource(value);
            if (end > start) value = RemoveEmptiedRanges(value, ref caretSource);
            if (HasPendingStyles)
            {
                caretAfter = start + CountCodepoints(text);
                ApplyPendingStyles(start, caretAfter);
            }

            int caret = caretAfter >= 0 ? Mathf.Clamp(caretAfter, 0, Engine.ShownLength) : Engine.RenderedIndexFromSource(caretSource);
            LetteraSelectionRange after = LetteraSelectionRange.AtCaret(caret, BiasAfterInsert(caret));
            m_History.EndEdit(after, LetteraSettings.Instance.Editing.UndoMemoryLimit);
            m_Suppress = true;
            Selectable.SetSelectionUnchecked(after, "sel.edit");
            m_Suppress = false;
            int insertedShown = CountCodepoints(text);
            RaiseCommitted(new LetteraEditPatch(start, end - start, insertedShown, reason), reason);
            ScrollCaretIntoView();
        }

        bool m_Suppress, m_PendingWritesLiteral;

        LetteraCaretBias BiasAfterInsert(int caret)
        {
            if (caret <= 0 || caret >= Engine.ShownLength) return LetteraCaretBias.Forward;
            ReadOnlySpan<int> text = Engine.ShownCodepoints;
            bool beforeRtl = LetteraUnicode.GetBidiClass(text[caret - 1]) == BidiClass.R || LetteraUnicode.GetBidiClass(text[caret - 1]) == BidiClass.AL;
            bool afterRtl = LetteraUnicode.GetBidiClass(text[caret]) == BidiClass.R || LetteraUnicode.GetBidiClass(text[caret]) == BidiClass.AL;
            return beforeRtl != afterRtl ? LetteraCaretBias.Backward : LetteraCaretBias.Forward;
        }

        static int CountCodepoints(string text)
        {
            int count = 0;
            for (int i = 0; i < text.Length; i++)
            {
                count++;
                if (char.IsHighSurrogate(text[i]) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1])) i++;
            }
            return count;
        }

        // New text takes the style of what stands before it in the paragraph; at a paragraph start, of what follows it.
        int InsertionPoint(int shown)
        {
            ReadOnlySpan<int> text = Engine.ShownCodepoints;
            if (shown > 0 && !LetteraTextNavigator.IsParagraphSeparator(text[shown - 1]) && !EndsANonExtendingRange(shown)) return Engine.SourceEndOf(shown - 1);
            return Engine.SourceStartOf(shown);
        }

        bool EndsANonExtendingRange(int shown)
        {
            IReadOnlyList<LetteraMatchContext.RangeRecord> ranges = Engine.ParsedRanges;
            for (int r = 0; r < ranges.Count; r++)
            {
                if (ranges[r].RenderedEnd != shown || ranges[r].RenderedStart >= shown) continue;
                LetteraEffect effect = Engine.EffectiveStyles[ranges[r].StyleIndex]?.Effect;
                if (effect != null && !effect.ExtendsOnType) return true;
            }
            return false;
        }

        // What is inserted goes where the first removed character stood, so later splices never move the caret.
        string ApplySplices(string source)
        {
            m_Builder.Clear();
            m_Builder.Append(source);
            int shift = 0;
            foreach (Splice splice in m_Splices)
            {
                string inserted = splice.Inserted ?? "";
                m_History.AddSplice(splice.Position + shift, m_Builder, splice.Removed, inserted);
                m_Builder.Remove(splice.Position + shift, splice.Removed);
                m_Builder.Insert(splice.Position + shift, inserted);
                shift += inserted.Length - splice.Removed;
            }
            return m_Builder.ToString();
        }

        // Taking a range's last character takes its delimiters too.
        string RemoveEmptiedRanges(string value, ref int caretSource)
        {
            for (int guard = 0; guard < 16; guard++)
            {
                m_Spans.Clear();
                int at = Engine.RenderedIndexFromSource(caretSource);
                Engine.EmptyRangeDelimiters(at, m_Spans);
                if (m_Spans.Count == 0) return value;
                m_Spans.Sort((a, b) => b.x.CompareTo(a.x));
                m_Builder.Clear();
                m_Builder.Append(value);
                foreach (Vector2Int span in m_Spans)
                {
                    m_History.AddSplice(span.x, m_Builder, span.y - span.x, "");
                    m_Builder.Remove(span.x, span.y - span.x);
                    value = value.Remove(span.x, span.y - span.x);
                    if (span.x < caretSource) caretSource -= Mathf.Min(span.y - span.x, caretSource - span.x);
                }
                WriteSource(value);
            }
            return value;
        }

        void ClassifyEdit(string reason, string text, out LetteraEditHistory.Kind kind, out LetteraEditHistory.TokenClass token)
        {
            token = LetteraEditHistory.TokenClass.Other;
            kind = LetteraEditHistory.Kind.Other;
            if (reason == "user.type")
            {
                kind = LetteraEditHistory.Kind.Typing;
                int cp = text.Length == 0 ? ' ' : char.ConvertToUtf32(text, 0);
                token = LetteraUnicode.IsWhiteSpace(cp) ? LetteraEditHistory.TokenClass.Space : char.IsLetterOrDigit(text, 0) || LetteraUnicode.IsMark(cp) ? LetteraEditHistory.TokenClass.Word : LetteraEditHistory.TokenClass.Other;
            }
            else if (reason == "user.delete.back") kind = LetteraEditHistory.Kind.DeleteBack;
            else if (reason == "user.delete.forward") kind = LetteraEditHistory.Kind.DeleteForward;
        }

        string EscapeLiteral(string text)
        {
            // A backslash stays next to its character. A wrap would catch what is typed after it, so it is the last resort.
            IReadOnlyList<LetteraStyle> styles = Engine.EffectiveStyles;
            string wrapped = null;
            for (int i = 0; i < styles.Count; i++)
            {
                LetteraMatcher matcher = styles[i]?.Matcher;
                string escaped = matcher?.EscapeFor(text);
                if (escaped == null) continue;
                if (matcher is EscapeMatcher) return escaped;
                if (wrapped == null) wrapped = escaped;
            }
            return wrapped ?? text;
        }

        // ------------------------------------------------------------- deleting

        bool Delete(int start, int end, string reason)
        {
            if (m_ReadOnly) return false;
            CommitComposition();
            if (start == end) return false;
            return ProposeEdit(start, end, "", reason, null);
        }

        public bool DeleteSelection()
        {
            LetteraSelectionRange s = Selectable.Selection;
            return Delete(s.Start, s.End, "user.delete.back");
        }

        /// <summary>Deletes the selection, or the cluster before the caret.</summary>
        public bool DeleteBack()
        {
            LetteraSelectionRange s = Selectable.Selection;
            if (!s.IsCaret) return DeleteSelection();
            ReadOnlySpan<int> text = Shown;
            int start = LetteraTextNavigator.PreviousStop(text, s.Head);
            if (LetteraSettings.Instance.Editing.DeleteBackMode == LetteraDeleteBackMode.Codepoint && s.Head > 0 && s.Head - start > 1 && !EmojiClassifier.IsEmojiCluster(text, start, s.Head - start))
                start = s.Head - 1;
            if (Engine.AtomicAround(start, out int a, out _)) start = a;
            return Delete(start, s.Head, "user.delete.back");
        }

        /// <summary>Deletes the selection, or the cluster after the caret.</summary>
        public bool DeleteForward()
        {
            LetteraSelectionRange s = Selectable.Selection;
            if (!s.IsCaret) return DeleteSelection();
            int end = LetteraTextNavigator.NextStop(Shown, s.Head);
            if (Engine.AtomicAround(end, out _, out int b)) end = b;
            return Delete(s.Head, end, "user.delete.forward");
        }

        public bool DeleteWordBack()
        {
            LetteraSelectionRange s = Selectable.Selection;
            return !s.IsCaret ? DeleteSelection() : Delete(LetteraTextNavigator.PreviousWordStop(Shown, s.Head), s.Head, "user.delete.back");
        }

        public bool DeleteWordForward()
        {
            LetteraSelectionRange s = Selectable.Selection;
            return !s.IsCaret ? DeleteSelection() : Delete(s.Head, LetteraTextNavigator.NextWordStop(Shown, s.Head, LetteraKeyTable.Family == LetteraPlatformFamily.Apple), "user.delete.forward");
        }

        public bool DeleteToLineStart()
        {
            LetteraSelectionRange s = Selectable.Selection;
            if (!s.IsCaret) return DeleteSelection();
            Engine.EnsurePositioned();
            return Engine.Lines.Count > 0 && Delete(Engine.Lines[Engine.LineOf(s.Head, s.Bias)].Start, s.Head, "user.delete.back");
        }

        public bool DeleteToLineEnd()
        {
            LetteraSelectionRange s = Selectable.Selection;
            if (!s.IsCaret) return DeleteSelection();
            Engine.EnsurePositioned();
            if (Engine.Lines.Count == 0) return false;
            LetteraLine line = Engine.Lines[Engine.LineOf(s.Head, s.Bias)];
            ReadOnlySpan<int> text = Shown;
            int end = line.End;
            if (end > line.Start && end <= text.Length && (LetteraTextNavigator.IsParagraphSeparator(text[end - 1]) || text[end - 1] == 0x2028)) end = LetteraTextNavigator.PreviousStop(text, end);
            return Delete(s.Head, end, "user.delete.forward");
        }

        public bool DeleteToParagraphEnd()
        {
            LetteraSelectionRange s = Selectable.Selection;
            if (!s.IsCaret) return DeleteSelection();
            LetteraTextNavigator.ParagraphAt(Shown, s.Head, out _, out int contentEnd, out int end);
            return Delete(s.Head, s.Head == contentEnd ? end : contentEnd, "user.delete.forward");
        }

        /// <summary>Swaps the clusters around the caret, each keeping its own formatting.</summary>
        public bool TransposeClusters()
        {
            if (m_ReadOnly) return false;
            CommitComposition();
            int at = Selectable.Selection.Head;
            ReadOnlySpan<int> text = Shown;
            LetteraTextNavigator.ParagraphAt(text, at, out int start, out int contentEnd, out _);
            if (at >= contentEnd) at = LetteraTextNavigator.PreviousStop(text, contentEnd);
            if (at <= start) return false;
            int previous = LetteraTextNavigator.PreviousStop(text, at), next = LetteraTextNavigator.NextStop(text, at);
            if (next > contentEnd || previous == at || next == at) return false;
            int a0 = Engine.SourceStartOf(previous), a1 = Engine.SourceEndOf(at - 1), b0 = Engine.SourceStartOf(at), b1 = Engine.SourceEndOf(next - 1);
            string source = Engine.RawText, first = source.Substring(a0, a1 - a0), second = source.Substring(b0, b1 - b0);
            LetteraSelectionRange before = Selectable.Selection;
            m_History.BeginEdit(LetteraEditHistory.Kind.Other, LetteraEditHistory.TokenClass.Other, before, LetteraEditClock.Now, 0);
            m_Builder.Clear();
            m_Builder.Append(source);
            m_History.AddSplice(b0, m_Builder, b1 - b0, first);
            m_Builder.Remove(b0, b1 - b0).Insert(b0, first);
            m_History.AddSplice(a0, m_Builder, a1 - a0, second);
            m_Builder.Remove(a0, a1 - a0).Insert(a0, second);
            string value = m_Builder.ToString();
            WriteSource(value);
            var after = LetteraSelectionRange.AtCaret(next);
            m_History.EndEdit(after, LetteraSettings.Instance.Editing.UndoMemoryLimit);
            Selectable.SetSelectionUnchecked(after, "sel.edit");
            RaiseCommitted(new LetteraEditPatch(previous, next - previous, next - previous, "user.type"), "user.type");
            return true;
        }

        public bool InsertNewline() => !m_ReadOnly && InsertText("\n", "user.type");
        public bool InsertTab() => !m_ReadOnly && InsertText("\t", "user.type");

        ReadOnlySpan<int> Shown
        {
            get
            {
                Engine.EnsureParsed();
                return Engine.ShownCodepoints;
            }
        }

        // ------------------------------------------------------------- history

        public bool CanUndo => m_History.CanUndo;
        public bool CanRedo => m_History.CanRedo;
        public int UndoDepth => m_History.Depth;
        public void ClearHistory() => m_History.Clear();

        /// <summary>All edits until the result is disposed make one undo step. Nesting flattens.</summary>
        public HistoryGroup BeginHistoryGroup()
        {
            m_History.BeginGroup();
            return new HistoryGroup(this);
        }

        public readonly struct HistoryGroup : IDisposable
        {
            readonly LetteraEditable m_Owner;
            internal HistoryGroup(LetteraEditable owner) => m_Owner = owner;
            public void Dispose() => m_Owner?.m_History.EndGroup();
        }

        public bool Undo()
        {
            if (m_ReadOnly) return false;
            CommitComposition();
            return Replay(true);
        }

        public bool Redo()
        {
            if (m_ReadOnly) return false;
            CommitComposition();
            return Replay(false);
        }

        bool Replay(bool undo)
        {
            if (Engine.IsWindowed)
            {
                // What is undone may lie far from the view: the window goes there first.
                KeepHistoryOnWindow();
                if (m_History.PeekPosition(undo, out int position))
                {
                    int docChar = position + Engine.WindowSourceBase;
                    if (docChar < Engine.WindowStart || docChar > Engine.WindowStart + Engine.WindowLength)
                    {
                        Engine.View.ScrollToChar(docChar, LetteraRevealAlign.Center);
                        Engine.EnsureParsed();
                        KeepHistoryOnWindow();
                    }
                }
            }
            m_Builder.Clear();
            m_Builder.Append(Engine.RawText);
            LetteraSelectionRange selection;
            if (!(undo ? m_History.Undo(m_Builder, out selection) : m_History.Redo(m_Builder, out selection))) return false;
            WriteSource(m_Builder.ToString());
            m_Suppress = true;
            Selectable.SetSelectionUnchecked(selection.Clamp(Engine.ShownLength), "sel.history");
            m_Suppress = false;
            string reason = undo ? "user.history.undo" : "user.history.redo";
            RaiseCommitted(new LetteraEditPatch(0, 0, 0, reason), reason);
            ScrollCaretIntoView();
            return true;
        }

        internal void OnSelectionMoved(LetteraSelectionRange previous, LetteraSelectionRange current, string reason)
        {
            if (m_Suppress) return;
            if (reason != "sel.edit" && reason != "sel.ime") m_History.Seal();
            if (reason != "sel.edit")
            {
                ClearPendingStyles();
                m_CaretStylesDirty = true;
            }
            m_BlinkStart = LetteraEditClock.Now;
            if (m_MarkupView == LetteraMarkupView.RevealAtCaret && m_CompText == null) RevealAround(current);
        }

        bool m_Revealing;

        void RevealAround(LetteraSelectionRange selection)
        {
            if (m_Revealing) return;
            m_Revealing = true;
            Engine.EnsureParsed();
            SourceEnds(selection, out int anchor, out int head);
            if (head != Engine.RevealSource)
            {
                Engine.RevealSource = head;
                Host.Dirty(DirtyLevel.Parse);
                Engine.EnsureParsed();
                Selectable.SetSelectionUnchecked(new LetteraSelectionRange(Engine.RenderedIndexFromSource(anchor), Engine.RenderedIndexFromSource(head), selection.Bias), "sel.restore");
            }
            m_Revealing = false;
        }

        // ------------------------------------------------------------- verbs and input

        internal bool CanCopy
        {
            get
            {
                foreach (LetteraFieldRule rule in m_ActiveRules)
                    if (rule.Enabled && !rule.AllowsCopy)
                        return false;
                return true;
            }
        }

        internal void DecideKey(ref LetteraKeyDecision decision)
        {
            foreach (LetteraFieldRule rule in m_ActiveRules)
                if (rule.Enabled)
                    rule.LookupKey(ref decision);
        }

        internal bool RunEditVerb(LetteraEditVerb verb, bool repeat)
        {
            switch (verb)
            {
                case LetteraEditVerb.DeleteBack: DeleteBack(); return true;
                case LetteraEditVerb.DeleteForward: DeleteForward(); return true;
                case LetteraEditVerb.DeleteWordBack: DeleteWordBack(); return true;
                case LetteraEditVerb.DeleteWordForward: DeleteWordForward(); return true;
                case LetteraEditVerb.DeleteToLineStart: DeleteToLineStart(); return true;
                case LetteraEditVerb.DeleteToLineEnd: DeleteToLineEnd(); return true;
                case LetteraEditVerb.DeleteToParagraphEnd: DeleteToParagraphEnd(); return true;
                case LetteraEditVerb.Transpose: TransposeClusters(); return true;
                case LetteraEditVerb.InsertNewline: InsertNewline(); return true;
                case LetteraEditVerb.InsertTab: InsertTab(); return true;
                case LetteraEditVerb.Cut: Cut(); return true;
                case LetteraEditVerb.Paste: RunPaste(); return true;
                case LetteraEditVerb.PastePlain: PastePlain(); return true;
                case LetteraEditVerb.Undo: Undo(); return true;
                case LetteraEditVerb.Redo: Redo(); return true;
                case LetteraEditVerb.Submit:
                    if (!repeat) Submit();
                    return true;
                case LetteraEditVerb.Cancel: Cancel(); return true;
                case LetteraEditVerb.ScrollDocStart: ScrollTo(0); return true;
                case LetteraEditVerb.ScrollDocEnd: ScrollTo(Engine.ShownLength); return true;
                default: return false;
            }
        }

        /// <summary>Raises Submitted, after committing a composition. Rules decide what happens to the focus.</summary>
        public void Submit(bool? keepFocus = null)
        {
            CommitComposition();
            RunCheck(LetteraCheckTrigger.Submit);
            foreach (LetteraFieldRule rule in m_ActiveRules)
                if (rule.Enabled && rule.BlocksSubmit)
                    return;
            Submitted?.Invoke(Engine.RawText);
            bool release = false;
            foreach (LetteraFieldRule rule in m_ActiveRules)
                if (rule.Enabled)
                    release |= rule.ReleasesOnSubmit;
            if (keepFocus.HasValue) release = !keepFocus.Value;
            if (release) Selectable.EndSession(LetteraBlurReason.Submit);
        }

        /// <summary>Raises Cancelled. Rules decide what happens to the value and the focus.</summary>
        public void Cancel()
        {
            if (m_CompText != null)
            {
                DiscardComposition();
                return;
            }
            foreach (LetteraFieldRule rule in m_ActiveRules)
                if (rule.Enabled)
                    rule.OnCancel();
            Cancelled?.Invoke();
            foreach (LetteraFieldRule rule in m_ActiveRules)
                if (rule.Enabled && rule.ReleasesOnCancel)
                {
                    Selectable.EndSession(LetteraBlurReason.Cancel);
                    break;
                }
        }

        internal void OnTextInput(string text)
        {
            if (m_ReadOnly || string.IsNullOrEmpty(text)) return;
            if (m_CompText != null) return;
            LetteraSelectionRange s = Selectable.Selection;
            // Each cluster counts by itself, for the rules and for undo.
            int at = 0;
            while (at < text.Length)
            {
                int width = char.IsHighSurrogate(text[at]) && at + 1 < text.Length ? 2 : 1;
                s = Selectable.Selection;
                ProposeEdit(s.Start, s.End, text.Substring(at, width), "user.type", null);
                at += width;
            }
        }

        internal void OnInputAction(string verb)
        {
            if (!LetteraVerbs.IsKnown(verb))
            {
                Debug.LogError("Lettera: a platform's text control sent '" + verb + "', which is no verb of the field's. Nothing was done.");
                return;
            }
            switch (verb)
            {
                case "submit": Submit(); break;
                case "newline": InsertNewline(); break;
                case "return": Selectable.Input.Key(LetteraKeyCode.Enter, LetteraKeyModifiers.None, false); break;
                case "done": CommitComposition(); Selectable.EndSession(LetteraBlurReason.Code); break;
                case "cancel": Cancel(); Selectable.EndSession(LetteraBlurReason.Cancel); break;
                case "next": MoveFocus(true); break;
                case "previous": MoveFocus(false); break;
                case "copy": Selectable.Copy(); break;
                case "cut": Cut(); break;
                case "paste": RunPaste(); break;
                case "cutCopied": DeleteSelectionForCut(); break;
                case "pastePlain": PastePlain(); break;
                case "undo": Undo(); break;
                case "redo": Redo(); break;
            }
        }

        internal void MoveFocus(bool forward)
        {
            foreach (LetteraFieldRule rule in m_ActiveRules)
                if (rule is TabKeyRule tab && tab.Enabled)
                    tab.Move(forward);
        }

        // ------------------------------------------------------------- focus

        internal void OnFocusGained()
        {
            m_BlinkStart = LetteraEditClock.Now;
            Engine.SuspendElide = true;
            Host.Dirty(DirtyLevel.Layout);
            foreach (LetteraFieldRule rule in m_ActiveRules)
                if (rule.Enabled)
                    rule.OnFocusGained();
            RunCheck(LetteraCheckTrigger.Focus);
            PushSnapshot();
            FocusGained?.Invoke();
        }

        internal void OnBeforeFocusLost(LetteraBlurReason reason) => CommitComposition();

        internal void OnFocusLost(LetteraBlurReason reason)
        {
            Engine.SuspendElide = false;
            Host.Dirty(DirtyLevel.Layout);
            m_History.Seal();
            foreach (LetteraFieldRule rule in m_ActiveRules)
                if (rule.Enabled)
                    rule.OnFocusLost(reason);
            RunCheck(LetteraCheckTrigger.Blur);
            PushSnapshot();
            HideCaret();
            FocusLost?.Invoke(reason);
        }

        // ------------------------------------------------------------- per frame

        internal void Tick()
        {
            if (m_ShownChanged)
            {
                m_ShownChanged = false;
                ShownTextChanged?.Invoke();
            }
            if (m_CaretStylesDirty)
            {
                m_CaretStylesDirty = false;
                RaiseCaretStyles();
            }
            foreach (LetteraFieldRule rule in m_ActiveRules)
                if (rule.Enabled && rule.WantsTick)
                    rule.Tick();
            if (Selectable.HasFocus) TickCaret();
        }

        bool WantsCaret => Selectable.HasFocus && !m_ReadOnly && Selectable.Selection.IsCaret && Host.IsVisible;
        internal bool WantsVisual => Selectable.HasFocus || m_CompText != null;

        void TickCaret()
        {
            ISelectionVisual visual = Selectable.Visual;
            if (visual == null) return;
            bool on = WantsCaret && CaretPhaseOn;
            if (on != m_LastBlink)
            {
                m_LastBlink = on;
                Selectable.MarkVisualsDirty();
            }
        }

        bool m_LastBlink;

        bool CaretPhaseOn
        {
            get
            {
                if (LetteraCaret.PrefersSteady || LetteraAccessibility.ReducedMotion) return true;
                float interval = m_Caret != null ? m_Caret.BlinkSeconds : LetteraSettings.Instance.Editing.CaretBlinkInterval;
                float timeout = m_Caret != null ? m_Caret.BlinkTimeout : LetteraSettings.Instance.Editing.CaretBlinkTimeout;
                if (interval <= 0) return true;
                double elapsed = LetteraEditClock.Now - m_BlinkStart;
                if (timeout > 0 && elapsed >= timeout) return true;
                return (long)(elapsed / interval) % 2 == 0;
            }
        }

        readonly List<Rect> m_UnderlineRects = new List<Rect>();
        Rect m_LastCaretBox;

        internal void DrawInto(ISelectionVisual visual)
        {
            bool caretWanted = WantsCaret;
            if (caretWanted)
            {
                LetteraSelectionRange s = Selectable.Selection;
                Engine.EnsurePositioned();
                Engine.TryGetCaretRect(s.Head, s.Bias, out Rect box);
                Color color = Engine.GlyphColorAt(s.Head > 0 ? s.Head - 1 : 0);
                if (box != m_LastCaretBox) m_BlinkStart = LetteraEditClock.Now;
                m_LastCaretBox = box;
                visual.ShowCaret(box, color, CaretPhaseOn);
                m_LastBlink = CaretPhaseOn;
            }
            else
            {
                visual.ShowCaret(default, default, false);
                m_LastBlink = false;
            }
            m_UnderlineRects.Clear();
            if (m_CompText != null && m_CompLength > 0) AddCompositionUnderlines();
            visual.ShowUnderlines(m_UnderlineRects, LetteraSettings.Instance.Editing.ImeLook.Color);
        }

        readonly List<Rect> m_UnderlineScratch = new List<Rect>();

        void AddCompositionUnderlines()
        {
            LetteraImeLook look = LetteraSettings.Instance.Editing.ImeLook;
            Engine.EnsurePositioned();
            int at = m_CompStart;
            LetteraClause[] clauses = m_CompClauses;
            int count = clauses != null ? clauses.Length : 1;
            for (int c = 0; c < count; c++)
            {
                int length = clauses != null ? clauses[c].Length : m_CompLength;
                bool active = clauses != null && (clauses[c].Kind == LetteraClauseKind.ActiveRaw || clauses[c].Kind == LetteraClauseKind.ActiveConverted);
                float thickness = active ? look.ActiveThickness : look.Thickness;
                m_UnderlineScratch.Clear();
                Engine.GetRangeRects(at, Mathf.Min(at + length, m_CompStart + m_CompLength), m_UnderlineScratch);
                foreach (Rect rect in m_UnderlineScratch)
                {
                    LetteraLine line = Engine.Lines[Engine.LineOf(at, LetteraCaretBias.Forward)];
                    m_UnderlineRects.Add(new Rect(rect.x, line.Baseline - thickness - 1, Mathf.Max(rect.width - 1, 0), thickness));
                }
                at += length;
            }
        }

        internal LetteraCaret EnsureCaret()
        {
            if (m_Caret != null) return m_Caret;
            var go = new GameObject("Lettera Caret", typeof(RectTransform)) { hideFlags = SelectionVisuals.Flags };
            go.layer = gameObject.layer;
            go.transform.SetParent(transform, false);
            go.AddComponent<UnityEngine.UI.LayoutElement>().ignoreLayout = true;
            m_Caret = go.AddComponent<LetteraCaret>();
            m_Caret.raycastTarget = false;
            CanvasSelectionVisual.FitInside(m_Caret.rectTransform, Host.TextRect);
            m_CaretCreated = true;
            return m_Caret;
        }

        public LetteraCaret Caret { get => m_Caret; set { DestroyCaret(); m_Caret = value; } }

        void HideCaret()
        {
            if (m_Caret != null) m_Caret.SetBlink(false);
        }

        void DestroyCaret()
        {
            if (m_CaretCreated && m_Caret != null) SelectionVisuals.Destroy(m_Caret.gameObject);
            if (m_CaretCreated) m_Caret = null;
            m_CaretCreated = false;
        }

        /// <summary>Unscaled blink phase for tests and custom carets: true while the caret shows.</summary>
        internal bool CaretVisibleNow => WantsCaret && CaretPhaseOn;

        // ------------------------------------------------------------- scrolling

        Vector2 m_ScrollHome;
        bool m_ScrollHomeSet;

        /// <summary>Keeps the caret inside the viewport by moving the text, which rebuilds nothing.</summary>
        public void ScrollCaretIntoView()
        {
            RectTransform viewport = Selectable.Viewport;
            if (viewport == null) return;
            Engine.EnsurePositioned();
            LetteraSelectionRange s = Selectable.Selection;
            Engine.TryGetCaretRect(s.Head, s.Bias, out Rect caret);
            ScrollRectIntoView(viewport, caret);
        }

        void ScrollTo(int index)
        {
            RectTransform viewport = Selectable.Viewport;
            if (viewport == null) return;
            Engine.EnsurePositioned();
            Engine.TryGetCaretRect(index, LetteraCaretBias.Forward, out Rect caret);
            ScrollRectIntoView(viewport, caret);
        }

        void ScrollRectIntoView(RectTransform viewport, Rect target)
        {
            RectTransform text = Host.TextRect;
            if (!m_ScrollHomeSet)
            {
                m_ScrollHome = text.anchoredPosition;
                m_ScrollHomeSet = true;
            }
            Rect view = viewport.rect;
            Vector2 min = viewport.InverseTransformPoint(text.TransformPoint(target.min));
            Vector2 max = viewport.InverseTransformPoint(text.TransformPoint(target.max));
            float em = Engine.CurrentFontSize > 0 ? Engine.CurrentFontSize : target.height;
            float marginX = Mathf.Min(em, view.width * 0.25f), marginY = Mathf.Min(target.height * 0.5f, view.height * 0.25f);
            Vector2 shift = Vector2.zero;
            if (min.x < view.xMin + marginX) shift.x = view.xMin + marginX - min.x;
            else if (max.x > view.xMax - marginX) shift.x = view.xMax - marginX - max.x;
            if (max.y > view.yMax - marginY) shift.y = view.yMax - marginY - max.y;
            else if (min.y < view.yMin + marginY) shift.y = view.yMin + marginY - min.y;

            Vector2 position = text.anchoredPosition + shift;
            // Content that fits, and the room past its ends, never scrolls.
            Rect bounds = Engine.LayoutBounds;
            float textXMin = Mathf.Min(bounds.xMin, target.xMin), textXMax = Mathf.Max(bounds.xMax, target.xMax);
            Vector2 homeMin = viewport.InverseTransformPoint(text.TransformPoint(new Vector3(textXMin, bounds.yMin))) - (Vector3)(text.anchoredPosition - m_ScrollHome);
            Vector2 homeMax = viewport.InverseTransformPoint(text.TransformPoint(new Vector3(textXMax, bounds.yMax))) - (Vector3)(text.anchoredPosition - m_ScrollHome);
            position.x = ClampScroll(position.x, m_ScrollHome.x, homeMin.x, homeMax.x, view.xMin, view.xMax, marginX);
            position.y = ClampScroll(position.y, m_ScrollHome.y, homeMin.y, homeMax.y, view.yMin, view.yMax, marginY);
            if (position == text.anchoredPosition) return;
            text.anchoredPosition = position;
            Selectable.MarkVisualsDirty();
        }

        static float ClampScroll(float position, float home, float contentMin, float contentMax, float viewMin, float viewMax, float margin)
        {
            float offset = position - home;
            float lowest = Mathf.Min(viewMax - margin - contentMax, 0), highest = Mathf.Max(viewMin - contentMin, 0);
            if (contentMax - contentMin <= viewMax - viewMin && contentMin >= viewMin && contentMax <= viewMax) return home;
            return home + Mathf.Clamp(offset, lowest, highest);
        }

        // ------------------------------------------------------------- state

        /// <summary>What a field needs to come back as it was, as plain data.</summary>
        [Serializable]
        public struct State
        {
            public string Source;
            public int Anchor, Head;
            public LetteraCaretBias Bias;
            public Vector2 Scroll;
        }

        public State CaptureState()
        {
            CommitComposition();
            LetteraSelectionRange s = Selectable.Selection;
            return new State { Source = Engine.RawText, Anchor = s.Anchor, Head = s.Head, Bias = s.Bias, Scroll = Host.TextRect.anchoredPosition };
        }

        public void RestoreState(State state)
        {
            CommitComposition();
            string old = Engine.RawText;
            if (old != (state.Source ?? ""))
            {
                WriteSource(state.Source ?? "");
                m_Version++;
                EditReported?.Invoke("user.restore");
            }
            Host.TextRect.anchoredPosition = state.Scroll;
            Selectable.SetSelectionUnchecked(new LetteraSelectionRange(state.Anchor, state.Head, state.Bias).Clamp(Engine.ShownLength), "sel.restore");
        }

        void NotifyKeyboard() => Selectable.NotifyKeyboardOfChange();
    }
}

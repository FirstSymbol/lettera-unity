using System;
using System.Collections.Generic;
using System.Text;
using FirstSymbol.Lettera.Unicode;
using UnityEngine;

namespace FirstSymbol.Lettera
{
    /// <summary>What covers the caret or the selection.</summary>
    public sealed class LetteraCaretStyles
    {
        internal readonly List<LetteraEffect> Covering = new List<LetteraEffect>();
        internal readonly List<LetteraEffect> PartialList = new List<LetteraEffect>();
        internal readonly List<LetteraEffect> PendingList = new List<LetteraEffect>();

        /// <summary>Effects covering the caret, or all of the selection. Reused after the call: copy to keep.</summary>
        public IReadOnlyList<LetteraEffect> Effects => Covering;

        /// <summary>Effects on only part of the selection.</summary>
        public IReadOnlyList<LetteraEffect> Partial => PartialList;

        /// <summary>Effects the next typed text gets on top.</summary>
        public IReadOnlyList<LetteraEffect> Pending => PendingList;

        public Rect CaretRect;
    }

    /// <summary>Text with ranges, as the clipboard carries it. Indices count codepoints.</summary>
    public struct LetteraPastedSpan
    {
        public int Start, End;
        public string EffectKey;
        public string Raw;
        public string Label;
    }

    // Formatting commands write a style's own delimiters into the source, so a field keeps one syntax.
    public partial class LetteraEditable
    {
        struct Pending
        {
            public Type Effect;
            public string Parameter;
            public bool Add;
        }

        readonly List<Pending> m_Pending = new List<Pending>();
        readonly LetteraCaretStyles m_CaretStyles = new LetteraCaretStyles();
        readonly List<RawSplice> m_RawSplices = new List<RawSplice>();
        readonly List<(int start, int end, string raw)> m_Keep = new List<(int, int, string)>();

        /// <summary>At most once per frame, when what covers the caret may have changed.</summary>
        public event Action<LetteraCaretStyles> CaretStylesChanged;

        struct RawSplice
        {
            public int Position, Removed;
            public string Inserted;
        }

        bool HasPendingStyles => m_Pending.Count > 0;

        void ClearPendingStyles() => m_Pending.Clear();

        /// <summary>The style that writes an effect of a type, or null. A field's list decides what can be written: a command never adds a style.</summary>
        LetteraStyle WriterOf(Type effect)
        {
            Engine.EnsureParsed();
            IReadOnlyList<LetteraStyle> styles = Engine.EffectiveStyles;
            for (int i = 0; i < styles.Count; i++)
            {
                LetteraStyle style = styles[i];
                if (style != null && style.Enabled && style.Effect != null && style.Effect.GetType() == effect && style.Matcher != null && style.Matcher.CanWrap) return style;
            }
            return null;
        }

        /// <summary>Whether a formatting command for an effect type can do anything here.</summary>
        public bool CanFormat(Type effect) => !m_ReadOnly && !Engine.Literal && WriterOf(effect) != null;

        /// <summary>Applies an effect to the selection, or to the next typed text at a caret. False when no style of the field writes it.</summary>
        public bool ApplyEffect(Type effect, string parameter = null, string reason = "code.format")
        {
            if (!CanFormat(effect)) return false;
            CommitComposition();
            LetteraSelectionRange s = Selectable.Selection;
            if (s.IsCaret)
            {
                SetPending(effect, parameter, true);
                return true;
            }
            FormatRange(effect, parameter, s.Start, s.End, true, reason);
            return true;
        }

        /// <summary>Takes an effect off the selection, splitting a range it only partly covers.</summary>
        public bool RemoveEffect(Type effect, string reason = "code.format")
        {
            if (!CanFormat(effect)) return false;
            CommitComposition();
            LetteraSelectionRange s = Selectable.Selection;
            if (s.IsCaret)
            {
                SetPending(effect, null, false);
                return true;
            }
            FormatRange(effect, null, s.Start, s.End, false, reason);
            return true;
        }

        /// <summary>Removes an effect where it covers the whole selection, and applies it otherwise.</summary>
        public bool ToggleEffect(Type effect, string parameter = null, string reason = "code.format")
        {
            if (!CanFormat(effect)) return false;
            return IsEffectActive(effect) ? RemoveEffect(effect, reason) : ApplyEffect(effect, parameter, reason);
        }

        public bool ApplyEffect<T>(string parameter = null) where T : LetteraEffect => ApplyEffect(typeof(T), parameter);
        public bool RemoveEffect<T>() where T : LetteraEffect => RemoveEffect(typeof(T));
        public bool ToggleEffect<T>(string parameter = null) where T : LetteraEffect => ToggleEffect(typeof(T), parameter);
        public bool IsEffectActive<T>() where T : LetteraEffect => IsEffectActive(typeof(T));

        void SetPending(Type effect, string parameter, bool add)
        {
            m_Pending.RemoveAll(p => p.Effect == effect);
            m_Pending.Add(new Pending { Effect = effect, Parameter = parameter, Add = add });
            m_CaretStylesDirty = true;
        }

        /// <summary>The next typed text is plain, or the selection loses every style that can be taken off.</summary>
        public bool ClearStyles(string reason = "code.format", bool keepLinks = false)
        {
            if (m_ReadOnly || Engine.Literal) return false;
            CommitComposition();
            LetteraSelectionRange s = Selectable.Selection;
            Engine.EnsureParsed();
            var types = new HashSet<Type>();
            IReadOnlyList<LetteraMatchContext.RangeRecord> ranges = Engine.ParsedRanges;
            int from = s.IsCaret ? Mathf.Max(s.Start - 1, 0) : s.Start, to = s.IsCaret ? s.Start : s.End;
            for (int r = 0; r < ranges.Count; r++)
            {
                if (ranges[r].RenderedEnd < from || ranges[r].RenderedStart > to) continue;
                LetteraStyle style = Engine.EffectiveStyles[ranges[r].StyleIndex];
                if (style?.Effect == null || style.Matcher == null || !style.Matcher.CanWrap) continue;
                if (keepLinks && style.Effect is LinkEffect) continue;
                types.Add(style.Effect.GetType());
            }
            if (s.IsCaret)
            {
                foreach (Type type in types) SetPending(type, null, false);
                return true;
            }
            if (types.Count == 0) return false;
            using (BeginHistoryGroup())
                foreach (Type type in types)
                    FormatRange(type, null, s.Start, s.End, false, reason);
            return true;
        }

        void ApplyPendingStyles(int start, int end)
        {
            var pending = new List<Pending>(m_Pending);
            m_Pending.Clear();
            if (end <= start) return;
            foreach (Pending p in pending) FormatRangeInner(p.Effect, p.Parameter, start, end, p.Add);
        }

        void FormatRange(Type effect, string parameter, int start, int end, bool add, string reason)
        {
            LetteraSelectionRange before = Selectable.Selection;
            m_History.BeginEdit(LetteraEditHistory.Kind.Other, LetteraEditHistory.TokenClass.Other, before, LetteraEditClock.Now, 0);
            bool changed = FormatRangeInner(effect, parameter, start, end, add);
            m_History.EndEdit(before, LetteraSettings.Instance.Editing.UndoMemoryLimit);
            m_History.Seal();
            if (changed) RaiseCommitted(new LetteraEditPatch(start, 0, 0, reason), reason);
        }

        // Ranges of this kind that touch the target are taken apart and written again: what the target merges with goes into one
        // range, what it cuts off stays with the old parameter.
        bool FormatRangeInner(Type effect, string parameter, int start, int end, bool add)
        {
            LetteraStyle writer = WriterOf(effect);
            if (writer == null) return false;
            Engine.EnsureParsed();
            IReadOnlyList<LetteraMatchContext.RangeRecord> ranges = Engine.ParsedRanges;
            m_RawSplices.Clear();
            m_Keep.Clear();
            int newStart = start, newEnd = end;
            string raw = string.IsNullOrEmpty(parameter) ? null : parameter;
            for (int r = 0; r < ranges.Count; r++)
            {
                LetteraMatchContext.RangeRecord range = ranges[r];
                LetteraStyle style = Engine.EffectiveStyles[range.StyleIndex];
                if (style?.Effect == null || style.Effect.GetType() != effect) continue;
                bool overlaps = range.RenderedStart < end && range.RenderedEnd > start;
                bool touches = range.RenderedEnd == start || range.RenderedStart == end;
                bool sameValue = (range.Raw ?? "") == (raw ?? "");
                if (!overlaps && !(touches && add && sameValue)) continue;
                if (!Engine.TryGetDelimiters(r, out int openStart, out int openLength, out int closeStart, out int closeLength) || openLength == 0 || closeLength == 0) continue;
                m_RawSplices.Add(new RawSplice { Position = openStart, Removed = openLength });
                m_RawSplices.Add(new RawSplice { Position = closeStart, Removed = closeLength });
                if (add && sameValue)
                {
                    newStart = Mathf.Min(newStart, range.RenderedStart);
                    newEnd = Mathf.Max(newEnd, range.RenderedEnd);
                }
                else
                {
                    if (range.RenderedStart < start) m_Keep.Add((range.RenderedStart, start, range.Raw));
                    if (range.RenderedEnd > end) m_Keep.Add((end, range.RenderedEnd, range.Raw));
                }
            }
            foreach ((int a, int b, string keptRaw) in m_Keep) AddWrap(writer, keptRaw, a, b);
            if (add) AddWrap(writer, raw, newStart, newEnd);
            if (m_RawSplices.Count == 0) return false;

            // From the end, so earlier positions stay valid. At one position a closer goes in before an opener.
            m_RawSplices.Sort((x, y) => x.Position != y.Position ? y.Position.CompareTo(x.Position) : x.Removed.CompareTo(y.Removed));
            m_Builder.Clear();
            m_Builder.Append(Engine.RawText);
            foreach (RawSplice splice in m_RawSplices)
            {
                string inserted = splice.Inserted ?? "";
                m_History.AddSplice(splice.Position, m_Builder, splice.Removed, inserted);
                m_Builder.Remove(splice.Position, splice.Removed);
                m_Builder.Insert(splice.Position, inserted);
            }
            WriteSource(m_Builder.ToString());
            return true;
        }

        void AddWrap(LetteraStyle writer, string raw, int start, int end)
        {
            if (end <= start) return;
            var open = new StringBuilder();
            var close = new StringBuilder();
            writer.Matcher.WriteOpen(open, raw, null);
            writer.Matcher.WriteClose(close);
            m_RawSplices.Add(new RawSplice { Position = Engine.SourceStartOf(start), Inserted = open.ToString() });
            m_RawSplices.Add(new RawSplice { Position = Engine.SourceEndOf(end - 1), Inserted = close.ToString() });
        }

        /// <summary>Would typing now produce this effect, or does it cover all of the selection?</summary>
        public bool IsEffectActive(Type effect)
        {
            foreach (Pending p in m_Pending)
                if (p.Effect == effect)
                    return p.Add;
            Engine.EnsureParsed();
            LetteraSelectionRange s = Selectable.Selection;
            ReadOnlySpan<int> text = Engine.ShownCodepoints;
            if (s.IsCaret)
            {
                int at = s.Head > 0 && !LetteraTextNavigator.IsParagraphSeparator(text[s.Head - 1]) ? s.Head - 1 : s.Head;
                if (at >= text.Length) return false;
                return EffectCovers(effect, at, s.Head) && !(EndsANonExtendingRange(s.Head) && EffectIsNonExtending(effect));
            }
            bool any = false;
            for (int i = s.Start; i < s.End; i++)
            {
                if (LetteraUnicode.IsWhiteSpace(text[i])) continue;
                any = true;
                if (!EffectCovers(effect, i, -1)) return false;
            }
            return any;
        }

        static bool EffectIsNonExtending(Type effect) => effect == typeof(LinkEffect);

        bool EffectCovers(Type effect, int shown, int caretEnd)
        {
            IReadOnlyList<LetteraMatchContext.RangeRecord> ranges = Engine.ParsedRanges;
            for (int r = 0; r < ranges.Count; r++)
                if (ranges[r].RenderedStart <= shown && ranges[r].RenderedEnd > shown && Contains(Engine.EffectiveStyles[ranges[r].StyleIndex]?.Effect, effect))
                    return true;
            return false;
        }

        static bool Contains(LetteraEffect root, Type effect)
        {
            if (root == null) return false;
            if (root.GetType() == effect) return true;
            var children = new List<LetteraEffect>();
            root.CollectChildren(children);
            foreach (LetteraEffect child in children)
                if (Contains(child, effect))
                    return true;
            return false;
        }

        /// <summary>The parameter of an effect at the caret or on the selection. False on a mixed selection.</summary>
        public bool TryGetEffectParameter<T>(out string value) where T : LetteraEffect
        {
            value = null;
            Engine.EnsureParsed();
            LetteraSelectionRange s = Selectable.Selection;
            int from = s.IsCaret ? Mathf.Max(s.Head - 1, 0) : s.Start, to = s.IsCaret ? from + 1 : s.End;
            IReadOnlyList<LetteraMatchContext.RangeRecord> ranges = Engine.ParsedRanges;
            bool found = false;
            for (int i = from; i < to; i++)
            {
                string here = null;
                bool covered = false;
                for (int r = 0; r < ranges.Count; r++)
                {
                    if (ranges[r].RenderedStart > i || ranges[r].RenderedEnd <= i || !(Engine.EffectiveStyles[ranges[r].StyleIndex]?.Effect is T)) continue;
                    covered = true;
                    here = ranges[r].Raw ?? "";
                }
                if (!covered) return false;
                if (found && here != value) return false;
                value = here;
                found = true;
            }
            return found;
        }

        void RaiseCaretStyles()
        {
            if (CaretStylesChanged == null) return;
            LetteraCaretStyles styles = m_CaretStyles;
            styles.Covering.Clear();
            styles.PartialList.Clear();
            styles.PendingList.Clear();
            Engine.EnsureParsed();
            LetteraSelectionRange s = Selectable.Selection;
            int from = s.IsCaret ? Mathf.Max(s.Head - 1, 0) : s.Start, to = s.IsCaret ? from + 1 : s.End;
            IReadOnlyList<LetteraMatchContext.RangeRecord> ranges = Engine.ParsedRanges;
            for (int r = 0; r < ranges.Count; r++)
            {
                LetteraEffect effect = Engine.EffectiveStyles[ranges[r].StyleIndex]?.Effect;
                if (effect == null || ranges[r].RenderedEnd <= from || ranges[r].RenderedStart >= to) continue;
                if (ranges[r].RenderedStart <= from && ranges[r].RenderedEnd >= to) styles.Covering.Add(effect);
                else styles.PartialList.Add(effect);
            }
            foreach (Pending p in m_Pending)
            {
                LetteraStyle writer = WriterOf(p.Effect);
                if (writer != null && p.Add) styles.PendingList.Add(writer.Effect);
            }
            Engine.TryGetCaretRect(s.Head, s.Bias, out styles.CaretRect);
            CaretStylesChanged(styles);
        }

        // ------------------------------------------------------------- what rules set on the field

        internal void SetMask(int glyph)
        {
            Engine.MaskGlyph = glyph;
            Host.Dirty(DirtyLevel.Shape);
            Selectable.MarkVisualsDirty();
            m_ShownChanged = true;
        }

        internal void ForceLtrForRule(bool on)
        {
            Engine.FieldDirection = on ? 0 : -1;
            Host.Dirty(DirtyLevel.Shape);
        }

        // ------------------------------------------------------------- pasted ranges

        // The plain text with each range written in this field's own syntax. A range no style writes stays plain: paste never adds a style.
        string WriteSpans(string plain, List<LetteraPastedSpan> spans)
        {
            var events = new List<(int at, bool open, string text)>();
            foreach (LetteraPastedSpan span in spans)
            {
                LetteraStyle writer = WriterOfKey(span.EffectKey);
                if (writer == null) continue;
                var open = new StringBuilder();
                var close = new StringBuilder();
                writer.Matcher.WriteOpen(open, span.Raw, span.Label);
                writer.Matcher.WriteClose(close);
                events.Add((span.Start, true, open.ToString()));
                events.Add((span.End, false, close.ToString()));
            }
            events.Sort((a, b) => a.at != b.at ? a.at.CompareTo(b.at) : a.open.CompareTo(b.open));
            var builder = new StringBuilder(plain.Length + events.Count * 8);
            int cp = 0, at16 = 0, next = 0;
            var piece = new StringBuilder();
            void FlushPiece()
            {
                if (piece.Length == 0) return;
                builder.Append(m_Literal ? piece.ToString() : EscapeLiteral(piece.ToString()));
                piece.Clear();
            }
            while (at16 <= plain.Length)
            {
                while (next < events.Count && events[next].at == cp)
                {
                    FlushPiece();
                    builder.Append(events[next++].text);
                }
                if (at16 >= plain.Length) break;
                int width = char.IsHighSurrogate(plain[at16]) && at16 + 1 < plain.Length ? 2 : 1;
                piece.Append(plain, at16, width);
                at16 += width;
                cp++;
            }
            FlushPiece();
            return builder.ToString();
        }

        bool m_Literal => Engine.Literal;

        LetteraStyle WriterOfKey(string key)
        {
            IReadOnlyList<LetteraStyle> styles = Engine.EffectiveStyles;
            for (int i = 0; i < styles.Count; i++)
            {
                LetteraStyle style = styles[i];
                if (style != null && style.Enabled && style.Effect != null && style.Matcher != null && style.Matcher.CanWrap && LetteraClipSchemas.KeyOf(style.Effect) == key) return style;
            }
            return null;
        }
    }

    /// <summary>Lets a rule read and change what is at the caret, for a toolbar lamp and the like.</summary>
    [Serializable]
    public abstract class LetteraCaretWatcher
    {
        public abstract void OnCaretStyles(LetteraEditable field, LetteraCaretStyles styles);
        public virtual LetteraCaretWatcher Clone() => (LetteraCaretWatcher)MemberwiseClone();
    }

    /// <summary>Raises an event only when the answer to "would typing produce this effect" flips.</summary>
    [Serializable]
    public class StyleLampWatcher : LetteraCaretWatcher
    {
        [Tooltip("The full name of the effect type.")]
        [SerializeField] string m_EffectType = "";
        [SerializeField] UnityEngine.Events.UnityEvent<bool> m_Changed = new UnityEngine.Events.UnityEvent<bool>();
        bool m_Last, m_Known;
        Type m_Resolved;

        public string EffectType { get => m_EffectType; set { m_EffectType = value; m_Resolved = null; } }
        public UnityEngine.Events.UnityEvent<bool> Changed => m_Changed;
        public void Watch<T>() where T : LetteraEffect => m_Resolved = typeof(T);

        public override void OnCaretStyles(LetteraEditable field, LetteraCaretStyles styles)
        {
            if (m_Resolved == null && !string.IsNullOrEmpty(m_EffectType)) m_Resolved = typeof(LetteraEffect).Assembly.GetType(m_EffectType);
            if (m_Resolved == null) return;
            bool on = field.IsEffectActive(m_Resolved);
            if (m_Known && on == m_Last) return;
            m_Known = true;
            m_Last = on;
            m_Changed.Invoke(on);
        }
    }

    /// <summary>Hosts watchers of the caret's styles.</summary>
    [Serializable, LetteraRuleInfo(LetteraRuleGroup.Formatting, "Caret Watch")]
    public class CaretWatchRule : LetteraFieldRule
    {
        [SerializeReference] List<LetteraCaretWatcher> m_Handlers = new List<LetteraCaretWatcher>();

        public List<LetteraCaretWatcher> Handlers => m_Handlers;

        protected override void OnEnable() => Field.CaretStylesChanged += OnStyles;
        protected override void OnDisable() => Field.CaretStylesChanged -= OnStyles;

        void OnStyles(LetteraCaretStyles styles)
        {
            foreach (LetteraCaretWatcher watcher in m_Handlers) watcher?.OnCaretStyles(Field, styles);
        }

        public override LetteraFieldRule Clone()
        {
            var copy = (CaretWatchRule)base.Clone();
            copy.m_Handlers = new List<LetteraCaretWatcher>();
            foreach (LetteraCaretWatcher watcher in m_Handlers) copy.m_Handlers.Add(watcher?.Clone());
            return copy;
        }
    }

    /// <summary>A shortcut for a formatting command: the primary modifier, optionally Shift, plus a key.</summary>
    [Serializable]
    public struct LetteraFormatShortcut
    {
        public LetteraKeyCode Key;
        public bool Shift;
        [Tooltip("The full name of the effect type. Empty clears styles.")]
        public string EffectType;
    }

    /// <summary>Makes a field a rich text editor: shortcuts, how markup shows, what paste keeps. What it sets goes back when it is off.</summary>
    [Serializable, LetteraRuleInfo(LetteraRuleGroup.Formatting, "Formatting")]
    public class FormattingRule : LetteraFieldRule
    {
        [SerializeField] List<LetteraFormatShortcut> m_Shortcuts = new List<LetteraFormatShortcut>
        {
            new LetteraFormatShortcut { Key = LetteraKeyCode.B, EffectType = typeof(BoldEffect).FullName },
            new LetteraFormatShortcut { Key = LetteraKeyCode.I, EffectType = typeof(ItalicEffect).FullName },
            new LetteraFormatShortcut { Key = LetteraKeyCode.U, EffectType = typeof(UnderlineEffect).FullName },
            new LetteraFormatShortcut { Key = LetteraKeyCode.Backslash, EffectType = "" },
        };
        [SerializeField] LetteraMarkupView m_MarkupView;
        [SerializeField] LetteraTypedMarkup m_TypedMarkup = LetteraTypedMarkup.AsText;
        [SerializeField] bool m_PasteKeepsStyles = true;
        [SerializeField] LetteraPlainPaste m_PlainPaste;
        [SerializeField] bool m_ArrowLeavesFormat = true;

        LetteraMarkupView m_OldView;
        LetteraTypedMarkup m_OldTyped;
        bool m_OldKeeps;
        LetteraPlainPaste m_OldPlain;

        public List<LetteraFormatShortcut> Shortcuts => m_Shortcuts;
        public bool ArrowLeavesFormat { get => m_ArrowLeavesFormat; set => m_ArrowLeavesFormat = value; }

        protected override void OnEnable()
        {
            m_OldView = Field.MarkupView;
            m_OldTyped = Field.TypedMarkup;
            m_OldKeeps = Field.PasteKeepsStyles;
            m_OldPlain = Field.PlainPaste;
            Field.MarkupView = m_MarkupView;
            Field.TypedMarkup = m_TypedMarkup;
            Field.PasteKeepsStyles = m_PasteKeepsStyles;
            Field.PlainPaste = m_PlainPaste;
        }

        protected override void OnDisable()
        {
            Field.MarkupView = m_OldView;
            Field.TypedMarkup = m_OldTyped;
            Field.PasteKeepsStyles = m_OldKeeps;
            Field.PlainPaste = m_OldPlain;
        }

        public override LetteraFieldRule Clone()
        {
            var copy = (FormattingRule)base.Clone();
            copy.m_Shortcuts = new List<LetteraFormatShortcut>(m_Shortcuts);
            return copy;
        }

        public override void LookupKey(ref LetteraKeyDecision key)
        {
            LetteraKeyModifiers primary = LetteraKeyTable.Family == LetteraPlatformFamily.Apple ? LetteraKeyModifiers.Command : LetteraKeyModifiers.Control;
            LetteraKeyModifiers forbidden = (LetteraKeyModifiers.Control | LetteraKeyModifiers.Alt | LetteraKeyModifiers.Command) & ~primary;
            if ((key.Modifiers & primary) != 0 && (key.Modifiers & forbidden) == 0)
            {
                bool shift = (key.Modifiers & LetteraKeyModifiers.Shift) != 0;
                foreach (LetteraFormatShortcut shortcut in m_Shortcuts)
                {
                    if (shortcut.Key != key.Key || shortcut.Shift != shift) continue;
                    bool done;
                    if (string.IsNullOrEmpty(shortcut.EffectType))
                    {
                        done = Field.ClearStyles("user.format");
                    }
                    else
                    {
                        Type type = typeof(LetteraEffect).Assembly.GetType(shortcut.EffectType);
                        done = type != null && Field.ToggleEffect(type, null, "user.format");
                    }
                    // A command that does not apply passes the key on.
                    if (done) key.Verb = LetteraEditVerb.Swallow;
                    return;
                }
            }
            if (m_ArrowLeavesFormat && key.Verb == LetteraEditVerb.MoveCharNext && key.Key == LetteraKeyCode.Right) Field.LeaveFormatAtCaret(ref key);
        }
    }

    public partial class LetteraEditable
    {
        // At the end of a formatted range the first arrow out leaves the format, and the caret moves only on the next.
        internal void LeaveFormatAtCaret(ref LetteraKeyDecision key)
        {
            LetteraSelectionRange s = Selectable.Selection;
            if (!s.IsCaret) return;
            Engine.EnsureParsed();
            IReadOnlyList<LetteraMatchContext.RangeRecord> ranges = Engine.ParsedRanges;
            for (int r = 0; r < ranges.Count; r++)
            {
                if (ranges[r].RenderedEnd != s.Head || ranges[r].RenderedStart >= s.Head) continue;
                LetteraStyle style = Engine.EffectiveStyles[ranges[r].StyleIndex];
                if (style?.Effect == null || style.Matcher == null || !style.Matcher.CanWrap) continue;
                bool alreadyLeft = false;
                foreach (Pending p in m_Pending)
                    if (p.Effect == style.Effect.GetType() && !p.Add)
                        alreadyLeft = true;
                if (alreadyLeft) continue;
                SetPending(style.Effect.GetType(), null, false);
                key.Verb = LetteraEditVerb.Swallow;
                return;
            }
        }
    }
}

using System;
using System.Collections.Generic;
using System.Text;
using FirstSymbol.Lettera.Unicode;
using UnityEngine;

namespace FirstSymbol.Lettera
{
    /// <summary>A text edit on its way into a field. Each rule sees what the rule before it left.</summary>
    public struct LetteraEditProposal
    {
        readonly LetteraEditable m_Field;

        internal LetteraEditProposal(LetteraEditable field, int start, int removed, string text, string reason, bool whole)
        {
            m_Field = field;
            Start = start;
            RemovedLength = removed;
            Text = text;
            Reason = reason;
            IsWholeRewrite = whole;
            CaretAfter = -1;
            Rejected = false;
        }

        /// <summary>The target, in shown indices. A rule can move it.</summary>
        public int Start, RemovedLength;
        public string Text;

        /// <summary>Where the caret goes after the edit, or -1 for the end of what was inserted.</summary>
        public int CaretAfter;
        public readonly string Reason;
        public bool IsWholeRewrite;

        /// <summary>Once rejected, always rejected: later rules can read it but not undo it.</summary>
        public bool Rejected { get; private set; }
        public void Reject() => Rejected = true;

        public bool IsPureDeletion => string.IsNullOrEmpty(Text);
        public LetteraEditable Field => m_Field;

        /// <summary>What the committed text would become.</summary>
        public string ResultText()
        {
            string plain = m_Field.PlainText;
            int a = Utf16Of(plain, Start), b = Utf16Of(plain, Start + RemovedLength);
            return plain.Substring(0, a) + (Text ?? "") + plain.Substring(b);
        }

        internal static int Utf16Of(string text, int codepoints)
        {
            int at = 0;
            for (int i = 0; i < codepoints && at < text.Length; i++) at += char.IsHighSurrogate(text[at]) && at + 1 < text.Length ? 2 : 1;
            return at;
        }
    }

    /// <summary>What decorators show from.</summary>
    public readonly struct LetteraFieldSnapshot
    {
        public readonly bool IsEmpty, HasFocus, IsComposing, IsReadOnly;
        public readonly int Count, Limit;
        public readonly LetteraCheckResult LastCheck;

        internal LetteraFieldSnapshot(bool isEmpty, bool hasFocus, bool isComposing, bool isReadOnly, int count, int limit, in LetteraCheckResult check)
        {
            IsEmpty = isEmpty;
            HasFocus = hasFocus;
            IsComposing = isComposing;
            IsReadOnly = isReadOnly;
            Count = count;
            Limit = limit;
            LastCheck = check;
        }

        public bool Same(in LetteraFieldSnapshot o) => IsEmpty == o.IsEmpty && HasFocus == o.HasFocus && IsComposing == o.IsComposing && IsReadOnly == o.IsReadOnly && Count == o.Count && Limit == o.Limit && LastCheck.Status == o.LastCheck.Status && LastCheck.Message == o.LastCheck.Message;
    }

    public enum LetteraRuleGroup { Input, Filters, Validation, Decoration, Formatting, Paste, Keyboard, Custom }

    /// <summary>How the inspector lists a rule.</summary>
    [AttributeUsage(AttributeTargets.Class)]
    public sealed class LetteraRuleInfoAttribute : Attribute
    {
        public readonly LetteraRuleGroup Group;
        public readonly string Title;

        public LetteraRuleInfoAttribute(LetteraRuleGroup group, string title)
        {
            Group = group;
            Title = title;
        }
    }

    /// <summary>A policy on a field: what it lets in, what keys do, what it shows. It holds the policy; the field holds none.</summary>
    [Serializable]
    public abstract class LetteraFieldRule
    {
        [SerializeField] bool m_Enabled = true;

        /// <summary>The field this rule works on. Null until attached.</summary>
        protected LetteraEditable Field { get; private set; }

        public bool Enabled
        {
            get => m_Enabled;
            set
            {
                if (m_Enabled == value) return;
                m_Enabled = value;
                if (Field == null) return;
                if (value) OnEnable();
                else OnDisable();
            }
        }

        internal void Attach(LetteraEditable field)
        {
            Field = field;
            if (m_Enabled) OnEnable();
        }

        internal void Detach()
        {
            if (m_Enabled && Field != null) OnDisable();
            Field = null;
        }

        protected virtual void OnEnable() { }

        /// <summary>Whatever OnEnable changed on the field goes back here.</summary>
        protected virtual void OnDisable() { }

        /// <summary>A text edit on its way in.</summary>
        public virtual void FilterEdit(ref LetteraEditProposal edit) { }

        /// <summary>A key before the table: this can replace its verb.</summary>
        public virtual void LookupKey(ref LetteraKeyDecision key) { }

        /// <summary>The field is raising a soft keyboard.</summary>
        public virtual void RequestKeyboard(ref LetteraKeyboardTraits traits) { }

        /// <summary>Something that is not text arrived by paste or drop.</summary>
        public virtual void OfferAttachment(LetteraAttachment attachment) { }

        /// <summary>The field's state changed.</summary>
        public virtual void OnSnapshot(in LetteraFieldSnapshot snapshot) { }

        public virtual void OnFocusGained() { }
        public virtual void OnFocusLost(LetteraBlurReason reason) { }
        public virtual void OnCancel() { }

        /// <summary>True while this rule needs a call each frame: for an animation, not for waiting.</summary>
        public virtual bool WantsTick => false;
        public virtual void Tick() { }

        public virtual bool AllowsCopy => true;
        public virtual bool ReleasesOnSubmit => false;
        public virtual bool ReleasesOnCancel => false;
        public virtual bool BlocksSubmit => false;

        /// <summary>A copy for one field. Override when the rule holds a list or another nested object.</summary>
        public virtual LetteraFieldRule Clone()
        {
            var copy = (LetteraFieldRule)MemberwiseClone();
            copy.Field = null;
            return copy;
        }
    }

    /// <summary>One rule list for many fields, such as chat, password or form. Each field works on its own copy.</summary>
    [CreateAssetMenu(fileName = "Field Preset", menuName = "Lettera/Field Preset", order = 92)]
    public class LetteraFieldPreset : ScriptableObject
    {
        [SerializeReference] List<LetteraFieldRule> m_Rules = new List<LetteraFieldRule>();

        internal static event Action<LetteraFieldPreset> PresetChanged;

        public List<LetteraFieldRule> Rules => m_Rules;

        void OnValidate() => PresetChanged?.Invoke(this);
        public void NotifyChanged() => PresetChanged?.Invoke(this);
    }

    public partial class LetteraEditable
    {
        readonly List<LetteraFieldRule> m_PresetCopies = new List<LetteraFieldRule>();

        void AttachRules()
        {
            DetachRules();
            LetteraFieldPreset.PresetChanged += OnPresetChanged;
            m_Attached = true;
            foreach (LetteraFieldRule rule in m_Rules)
                if (rule != null)
                    m_ActiveRules.Add(rule);
            foreach (LetteraFieldPreset preset in m_RulePresets)
                if (preset != null)
                    foreach (LetteraFieldRule rule in preset.Rules)
                        if (rule != null)
                        {
                            LetteraFieldRule copy = rule.Clone();
                            m_PresetCopies.Add(copy);
                            m_ActiveRules.Add(copy);
                        }
            m_AttachingRules = true;
            foreach (LetteraFieldRule rule in m_ActiveRules) rule.Attach(this);
            m_AttachingRules = false;
            m_HasSnapshot = false;
        }

        bool m_AttachingRules;

        void DetachRules()
        {
            if (!m_Attached) return;
            m_Attached = false;
            LetteraFieldPreset.PresetChanged -= OnPresetChanged;
            for (int i = m_ActiveRules.Count - 1; i >= 0; i--) m_ActiveRules[i].Detach();
            m_ActiveRules.Clear();
            m_PresetCopies.Clear();
        }

        void OnPresetChanged(LetteraFieldPreset preset)
        {
            if (m_RulePresets.Contains(preset)) RebuildRules();
        }

        void RebuildRules()
        {
            AttachRules();
            PushSnapshot();
        }

        public void AddRule(LetteraFieldRule rule)
        {
            m_Rules.Add(rule);
            RebuildRules();
        }

        public void RemoveRule(LetteraFieldRule rule)
        {
            if (m_Rules.Remove(rule)) RebuildRules();
        }

        public void AddPreset(LetteraFieldPreset preset)
        {
            m_RulePresets.Add(preset);
            RebuildRules();
        }

        public void RemovePreset(LetteraFieldPreset preset)
        {
            if (m_RulePresets.Remove(preset)) RebuildRules();
        }

        public T GetRule<T>() where T : class
        {
            foreach (LetteraFieldRule rule in m_ActiveRules)
                if (rule is T match)
                    return match;
            return null;
        }

        public void GetRules<T>(List<T> results) where T : class
        {
            foreach (LetteraFieldRule rule in m_ActiveRules)
                if (rule is T match)
                    results.Add(match);
        }

        internal LetteraKeyboardTraits KeyboardTraits
        {
            get
            {
                var traits = new LetteraKeyboardTraits { Autocorrect = true, Multiline = true };
                foreach (LetteraFieldRule rule in m_ActiveRules)
                    if (rule.Enabled)
                        rule.RequestKeyboard(ref traits);
                return traits;
            }
        }

        // ------------------------------------------------------------- snapshot

        LetteraFieldSnapshot m_LastSnapshot;
        bool m_HasSnapshot;

        /// <summary>The cap a length rule publishes, or 0.</summary>
        internal int PublishedLimit { get; set; }
        internal LetteraCountUnit? PublishedUnit { get; set; }
        internal bool PublishedCountsSource { get; set; }

        void PushSnapshot()
        {
            if (m_AttachingRules || Selectable == null || Selectable.Host == null) return;
            var snapshot = new LetteraFieldSnapshot(Length == 0 && m_CompText == null, Selectable.HasFocus, m_CompText != null, m_ReadOnly, Length, PublishedLimit, m_LastCheck);
            if (m_HasSnapshot && snapshot.Same(m_LastSnapshot)) return;
            m_LastSnapshot = snapshot;
            m_HasSnapshot = true;
            foreach (LetteraFieldRule rule in m_ActiveRules)
                if (rule.Enabled)
                    rule.OnSnapshot(snapshot);
        }

        /// <summary>The state decorators last saw.</summary>
        public LetteraFieldSnapshot Snapshot => m_LastSnapshot;

        /// <summary>Tells decorators again, after a rule was added or a target assigned.</summary>
        public void RefreshDecorators()
        {
            m_HasSnapshot = false;
            PushSnapshot();
        }

        // ------------------------------------------------------------- validation

        void RunCheck(LetteraCheckTrigger trigger)
        {
            foreach (LetteraFieldRule rule in m_ActiveRules)
                if (rule is ValidationRule validation && validation.Enabled)
                    validation.Run(trigger);
        }

        /// <summary>A check's result, late or not. One for an older version of the text is dropped.</summary>
        public void PublishCheck(int version, in LetteraCheckResult result)
        {
            if (version != m_Version) return;
            SetCheck(result);
        }

        internal void SetCheck(in LetteraCheckResult result)
        {
            if (m_LastCheck.Status == result.Status && m_LastCheck.Message == result.Message) return;
            m_LastCheck = result;
            CheckChanged?.Invoke(result);
            PushSnapshot();
        }
    }

    // ------------------------------------------------------------- input rules

    /// <summary>One line: Enter submits, and line breaks in what comes in turn into a join string.</summary>
    [Serializable, LetteraRuleInfo(LetteraRuleGroup.Input, "Single Line")]
    public class SingleLineRule : LetteraFieldRule
    {
        [SerializeField] bool m_StayFocusedOnSubmit;
        [SerializeField] string m_JoinWith = " ";

        public bool StayFocusedOnSubmit { get => m_StayFocusedOnSubmit; set => m_StayFocusedOnSubmit = value; }
        public string JoinWith { get => m_JoinWith; set => m_JoinWith = value; }
        public override bool ReleasesOnSubmit => !m_StayFocusedOnSubmit;

        public override void FilterEdit(ref LetteraEditProposal edit)
        {
            if (string.IsNullOrEmpty(edit.Text)) return;
            edit.Text = StripBreaks(edit.Text, m_JoinWith ?? "");
        }

        internal static string StripBreaks(string text, string join)
        {
            StringBuilder builder = null;
            int i = 0;
            bool anyContent = false;
            while (i < text.Length)
            {
                if (!IsBreak(text[i]))
                {
                    builder?.Append(text[i]);
                    anyContent = true;
                    i++;
                    continue;
                }
                if (builder == null) builder = new StringBuilder(text, 0, i, text.Length);
                while (i < text.Length && IsBreak(text[i])) i++;
                if (anyContent && i < text.Length) builder.Append(join);
            }
            return builder != null ? builder.ToString() : text;
        }

        static bool IsBreak(char c) => c == '\n' || c == '\r' || c == 0x0B || c == 0x0C || c == 0x85 || c == 0x2028 || c == 0x2029;

        public override void LookupKey(ref LetteraKeyDecision key)
        {
            if (key.Verb == LetteraEditVerb.InsertNewline) key.Verb = LetteraEditVerb.Submit;
        }

        public override void RequestKeyboard(ref LetteraKeyboardTraits traits) => traits.Multiline = false;
    }

    /// <summary>Shows a mask for each cluster, holds a literal value, and keeps it off the clipboard.</summary>
    [Serializable, LetteraRuleInfo(LetteraRuleGroup.Input, "Password")]
    public class PasswordRule : LetteraFieldRule
    {
        [SerializeField] string m_MaskGlyph = "•";
        [SerializeField] bool m_ShowValue;
        [SerializeField] bool m_CopyAllowed;

        public string MaskGlyph { get => m_MaskGlyph; set { m_MaskGlyph = value; Apply(); } }
        public bool ShowValue { get => m_ShowValue; set { m_ShowValue = value; Apply(); } }
        public bool CopyAllowed { get => m_CopyAllowed; set => m_CopyAllowed = value; }
        public override bool AllowsCopy => m_CopyAllowed;
        public override bool ReleasesOnSubmit => true;

        protected override void OnEnable()
        {
            Field.RuleWantsLiteral = true;
            Field.RefreshView();
            Apply();
        }

        protected override void OnDisable()
        {
            Field.SetMask(0);
            Field.RuleWantsLiteral = false;
            Field.RefreshView();
        }

        void Apply()
        {
            if (Field == null) return;
            int glyph = string.IsNullOrEmpty(m_MaskGlyph) ? 0x2022 : char.ConvertToUtf32(m_MaskGlyph, 0);
            Field.SetMask(m_ShowValue ? 0 : glyph);
        }

        public override void FilterEdit(ref LetteraEditProposal edit)
        {
            if (!string.IsNullOrEmpty(edit.Text)) edit.Text = SingleLineRule.StripBreaks(edit.Text, " ");
        }

        public override void LookupKey(ref LetteraKeyDecision key)
        {
            if (key.Verb == LetteraEditVerb.InsertNewline) key.Verb = LetteraEditVerb.Submit;
        }

        public override void OnFocusLost(LetteraBlurReason reason) => Field.ClearHistory();

        public override void RequestKeyboard(ref LetteraKeyboardTraits traits)
        {
            traits.Secure = true;
            traits.Multiline = false;
            traits.Autocorrect = false;
        }
    }

    public enum LetteraCountUnit { Graphemes, Utf16, Utf8, Codepoints }
    public enum LetteraCountSpace { Source, Plain }

    /// <summary>Lets no more than a limit in. A cluster never gets cut.</summary>
    [Serializable, LetteraRuleInfo(LetteraRuleGroup.Input, "Max Length")]
    public class MaxLengthRule : LetteraFieldRule
    {
        [Tooltip("0 or less means no limit.")]
        [SerializeField] int m_Limit;
        [SerializeField] LetteraCountUnit m_Unit;
        [SerializeField] LetteraCountSpace m_CountIn;

        public int Limit { get => m_Limit; set { m_Limit = value; Publish(); } }
        public LetteraCountUnit Unit { get => m_Unit; set { m_Unit = value; Publish(); } }
        public LetteraCountSpace CountIn { get => m_CountIn; set { m_CountIn = value; Publish(); } }

        protected override void OnEnable() => Publish();

        protected override void OnDisable()
        {
            Field.PublishedLimit = 0;
            Field.PublishedUnit = null;
        }

        void Publish()
        {
            if (Field == null) return;
            Field.PublishedLimit = Mathf.Max(m_Limit, 0);
            Field.PublishedUnit = m_Unit;
            Field.PublishedCountsSource = m_CountIn == LetteraCountSpace.Source;
            Field.RefreshDecorators();
        }

        internal static int Count(string text, LetteraCountUnit unit)
        {
            switch (unit)
            {
                case LetteraCountUnit.Utf16: return text.Length;
                case LetteraCountUnit.Utf8: return Encoding.UTF8.GetByteCount(text);
                case LetteraCountUnit.Codepoints:
                    int count = 0;
                    for (int i = 0; i < text.Length; i++)
                    {
                        count++;
                        if (char.IsHighSurrogate(text[i]) && i + 1 < text.Length) i++;
                    }
                    return count;
                default:
                    return Graphemes(text);
            }
        }

        static int[] s_Cps = new int[256];

        static int Fill(string text)
        {
            if (s_Cps.Length < text.Length) s_Cps = new int[text.Length * 2];
            int n = 0;
            for (int i = 0; i < text.Length; i++)
            {
                if (char.IsHighSurrogate(text[i]) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1])) s_Cps[n++] = char.ConvertToUtf32(text[i], text[++i]);
                else s_Cps[n++] = text[i];
            }
            return n;
        }

        internal static int Graphemes(string text)
        {
            int n = Fill(text), count = 0, at = 0;
            var span = new ReadOnlySpan<int>(s_Cps, 0, n);
            while (at < n)
            {
                at = LetteraTextNavigator.NextStop(span, at);
                count++;
            }
            return count;
        }

        public override void FilterEdit(ref LetteraEditProposal edit)
        {
            if (m_Limit <= 0 || string.IsNullOrEmpty(edit.Text)) return;
            LetteraEditable field = edit.Field;
            string baseText = m_CountIn == LetteraCountSpace.Source ? field.Source : field.PlainText;
            string removed = field.PlainText;
            int a = LetteraEditProposal.Utf16Of(removed, edit.Start), b = LetteraEditProposal.Utf16Of(removed, edit.Start + edit.RemovedLength);
            string rest = baseText.Length >= b - a ? baseText.Remove(0, b - a) : "";
            int used = Count(rest, m_Unit);
            if (used >= m_Limit)
            {
                edit.Text = "";
                if (edit.RemovedLength == 0) edit.Reject();
                return;
            }
            if (used + Count(edit.Text, m_Unit) <= m_Limit) return;

            // The longest prefix of whole clusters that fits.
            int n = Fill(edit.Text);
            var span = new ReadOnlySpan<int>(s_Cps, 0, n);
            int stop = 0, fit = 0;
            while (stop < n)
            {
                int next = LetteraTextNavigator.NextStop(span, stop);
                string candidate = LetteraSelectable.PlainOf(span, 0, next);
                if (used + Count(candidate, m_Unit) > m_Limit) break;
                fit = next;
                stop = next;
            }
            edit.Text = LetteraSelectable.PlainOf(span, 0, fit);
        }
    }

    /// <summary>A field shaped by a pattern such as (###) ###-####. # a digit, A a letter, * either, \x a literal x; anything else stands for itself.</summary>
    [Serializable, LetteraRuleInfo(LetteraRuleGroup.Input, "Pattern Mask")]
    public class PatternMaskRule : LetteraFieldRule
    {
        [SerializeField] string m_Pattern = "";
        [SerializeField] bool m_ForceLtr = true;

        struct Token
        {
            public char Literal, Slot;
        }

        readonly List<Token> m_Tokens = new List<Token>();
        string m_Parsed;

        public string Pattern { get => m_Pattern; set => m_Pattern = value; }
        public bool ForceLtr { get => m_ForceLtr; set => m_ForceLtr = value; }

        void Parse()
        {
            if (m_Parsed == m_Pattern) return;
            m_Parsed = m_Pattern;
            m_Tokens.Clear();
            string p = m_Pattern ?? "";
            for (int i = 0; i < p.Length; i++)
            {
                char c = p[i];
                if (c == '\\' && i + 1 < p.Length) m_Tokens.Add(new Token { Literal = p[++i] });
                else if (c == '#' || c == 'A' || c == '*') m_Tokens.Add(new Token { Slot = c });
                else m_Tokens.Add(new Token { Literal = c });
            }
        }

        protected override void OnEnable()
        {
            if (string.IsNullOrEmpty(m_Pattern)) return;
            Field.RuleWantsLiteral = true;
            Field.RefreshView();
            if (m_ForceLtr) Field.ForceLtrForRule(true);
        }

        protected override void OnDisable()
        {
            if (string.IsNullOrEmpty(m_Pattern)) return;
            Field.RuleWantsLiteral = false;
            Field.RefreshView();
            if (m_ForceLtr) Field.ForceLtrForRule(false);
        }

        static bool Fits(char slot, char c)
        {
            switch (slot)
            {
                case '#': return c >= '0' && c <= '9';
                case 'A': return char.IsLetter(c);
                default: return char.IsLetter(c) || (c >= '0' && c <= '9');
            }
        }

        /// <summary>Only what the user typed, without the pattern's own characters.</summary>
        public string RawValue
        {
            get
            {
                Parse();
                var builder = new StringBuilder();
                string plain = Field.PlainText;
                for (int i = 0; i < plain.Length && i < m_Tokens.Count; i++)
                    if (m_Tokens[i].Slot != 0)
                        builder.Append(plain[i]);
                return builder.ToString();
            }
        }

        /// <summary>Fills the pattern from a raw value, past the history.</summary>
        public void SetRawValue(string raw)
        {
            Parse();
            Field.SetSource(Format(Accepted(raw ?? "", 0), out _, -1));
        }

        // What fits the slots one by one, from a slot on. What fits none is dropped.
        string Accepted(string text, int slotsUsed)
        {
            var builder = new StringBuilder();
            int slot = slotsUsed;
            foreach (char c in text)
            {
                char kind = SlotKind(slot);
                if (kind == 0) break;
                if (!Fits(kind, c)) continue;
                builder.Append(c);
                slot++;
            }
            return builder.ToString();
        }

        char SlotKind(int index)
        {
            foreach (Token token in m_Tokens)
            {
                if (token.Slot == 0) continue;
                if (index-- == 0) return token.Slot;
            }
            return (char)0;
        }

        // Literals show once a character after them is there. Literals before the first slot come with the first character.
        string Format(string user, out int caret, int caretAfterChars)
        {
            var builder = new StringBuilder();
            int used = 0;
            caret = 0;
            if (user.Length == 0) return "";
            foreach (Token token in m_Tokens)
            {
                if (token.Slot == 0)
                {
                    if (used >= user.Length) break;
                    builder.Append(token.Literal);
                    if (used == caretAfterChars) caret = builder.Length;
                    continue;
                }
                if (used >= user.Length) break;
                builder.Append(user[used++]);
                if (used == caretAfterChars) caret = builder.Length;
            }
            return builder.ToString();
        }

        public override void FilterEdit(ref LetteraEditProposal edit)
        {
            if (string.IsNullOrEmpty(m_Pattern)) return;
            Parse();
            string plain = edit.Field.PlainText;
            int start = edit.Start, end = edit.Start + edit.RemovedLength;

            // Deleting a pattern's own character takes the user's character next to it.
            if (edit.IsPureDeletion && !HasSlotBetween(start, end))
            {
                if (edit.Reason == "user.delete.forward")
                {
                    while (end < plain.Length && !IsSlot(end)) end++;
                    if (end < plain.Length) end++;
                }
                else
                {
                    while (start > 0 && !IsSlot(start - 1)) start--;
                    if (start > 0) start--;
                }
            }

            var before = new StringBuilder();
            for (int i = 0; i < start && i < plain.Length; i++)
                if (IsSlot(i))
                    before.Append(plain[i]);
            var after = new StringBuilder();
            for (int i = end; i < plain.Length; i++)
                if (IsSlot(i))
                    after.Append(plain[i]);
            string middle = Accepted(edit.Text ?? "", before.Length);
            string user = Accepted(before.ToString() + middle + after, 0);
            int typed = before.Length + middle.Length;
            string formatted = Format(user, out int caret, Mathf.Min(typed, user.Length));
            edit.Start = 0;
            edit.RemovedLength = plain.Length;
            edit.Text = formatted;
            edit.IsWholeRewrite = true;
            edit.CaretAfter = typed == 0 ? 0 : caret;
        }

        bool IsSlot(int index) => index >= 0 && index < m_Tokens.Count && m_Tokens[index].Slot != 0;

        bool HasSlotBetween(int start, int end)
        {
            for (int i = start; i < end; i++)
                if (IsSlot(i))
                    return true;
            return false;
        }
    }

    public enum LetteraLetterCase { Upper, Lower, Title }

    /// <summary>Rewrites what comes in by simple case mappings, so no length changes.</summary>
    [Serializable, LetteraRuleInfo(LetteraRuleGroup.Input, "Letter Case")]
    public class LetterCaseRule : LetteraFieldRule
    {
        [SerializeField] LetteraLetterCase m_Mode;

        public LetteraLetterCase Mode { get => m_Mode; set => m_Mode = value; }

        public override void FilterEdit(ref LetteraEditProposal edit)
        {
            if (string.IsNullOrEmpty(edit.Text)) return;
            var builder = new StringBuilder(edit.Text.Length);
            int previous = edit.Start > 0 ? edit.Field.CodepointAt(edit.Start - 1) : ' ';
            string text = edit.Text;
            for (int i = 0; i < text.Length; i++)
            {
                int cp = char.ConvertToUtf32(text, i);
                if (cp > 0xFFFF) i++;
                int mapped = cp;
                switch (m_Mode)
                {
                    case LetteraLetterCase.Upper: mapped = LetteraUnicode.ToUpper(cp); break;
                    case LetteraLetterCase.Lower: mapped = LetteraUnicode.ToLower(cp); break;
                    case LetteraLetterCase.Title:
                        if (!IsWordChar(previous)) mapped = LetteraUnicode.ToUpper(cp);
                        break;
                }
                builder.Append(char.ConvertFromUtf32(mapped));
                previous = cp;
            }
            edit.Text = builder.ToString();
        }

        static bool IsWordChar(int cp)
        {
            WordBreakClass c = LetteraUnicode.GetWordBreakClass(cp);
            return c == WordBreakClass.ALetter || c == WordBreakClass.HebrewLetter || c == WordBreakClass.Numeric || c == WordBreakClass.Katakana || c == WordBreakClass.Extend || c == WordBreakClass.SingleQuote || c == WordBreakClass.MidNumLet;
        }
    }

    /// <summary>Selects everything when the field takes the keyboard.</summary>
    [Serializable, LetteraRuleInfo(LetteraRuleGroup.Input, "Select All On Focus")]
    public class SelectAllOnFocusRule : LetteraFieldRule
    {
        [SerializeField] bool m_OnPointerToo = true;

        public bool OnPointerToo { get => m_OnPointerToo; set => m_OnPointerToo = value; }

        public override void OnFocusGained()
        {
            if (m_OnPointerToo || !Field.Selectable.IsPressed) Field.Selectable.ScheduleSelectAll();
        }
    }

    /// <summary>Escape or the system's back gives the keyboard up, and can bring back the text from when it was taken.</summary>
    [Serializable, LetteraRuleInfo(LetteraRuleGroup.Input, "Release On Cancel")]
    public class ReleaseOnCancelRule : LetteraFieldRule
    {
        [SerializeField] bool m_RestoreOnCancel;
        string m_AtFocus;

        public bool RestoreOnCancel { get => m_RestoreOnCancel; set => m_RestoreOnCancel = value; }
        public override bool ReleasesOnCancel => true;

        public override void OnFocusGained() => m_AtFocus = Field.Source;

        public override void OnCancel()
        {
            if (m_RestoreOnCancel && m_AtFocus != null) Field.SetSource(m_AtFocus, LetteraHistoryMode.Record, "user.restore");
        }
    }

    public enum LetteraSubmitKey { Enter, PrimaryEnter }

    /// <summary>Which Enter submits. The other one breaks the line.</summary>
    [Serializable, LetteraRuleInfo(LetteraRuleGroup.Input, "Enter Key")]
    public class EnterKeyRule : LetteraFieldRule
    {
        [SerializeField] LetteraSubmitKey m_Key;
        [SerializeField] bool m_KeepFocus = true;

        public LetteraSubmitKey Key { get => m_Key; set => m_Key = value; }
        public bool KeepFocus { get => m_KeepFocus; set => m_KeepFocus = value; }
        public override bool ReleasesOnSubmit => !m_KeepFocus;

        public override void LookupKey(ref LetteraKeyDecision key)
        {
            if (key.Key != LetteraKeyCode.Enter && key.Key != LetteraKeyCode.KeypadEnter) return;
            LetteraKeyModifiers primary = LetteraKeyTable.Family == LetteraPlatformFamily.Apple ? LetteraKeyModifiers.Command : LetteraKeyModifiers.Control;
            bool shift = (key.Modifiers & LetteraKeyModifiers.Shift) != 0, hasPrimary = (key.Modifiers & primary) != 0;
            if (m_Key == LetteraSubmitKey.Enter) key.Verb = shift ? LetteraEditVerb.InsertNewline : hasPrimary ? LetteraEditVerb.Pass : LetteraEditVerb.Submit;
            else key.Verb = hasPrimary ? LetteraEditVerb.Submit : LetteraEditVerb.InsertNewline;
        }
    }

    public enum LetteraTabMode { InsertTab, InsertSpaces, MoveFocus }

    /// <summary>What Tab does. Without this rule Tab goes on to UI navigation.</summary>
    [Serializable, LetteraRuleInfo(LetteraRuleGroup.Input, "Tab Key")]
    public class TabKeyRule : LetteraFieldRule
    {
        [SerializeField] LetteraTabMode m_Mode;
        [SerializeField, Min(1)] int m_Spaces = 4;
        [SerializeField] UnityEngine.UI.Selectable m_Next;
        [SerializeField] UnityEngine.UI.Selectable m_Previous;
        [SerializeField] GameObject m_NextField;
        [SerializeField] GameObject m_PreviousField;

        public LetteraTabMode Mode { get => m_Mode; set => m_Mode = value; }
        public int Spaces { get => m_Spaces; set => m_Spaces = value; }
        public GameObject NextField { get => m_NextField; set => m_NextField = value; }
        public GameObject PreviousField { get => m_PreviousField; set => m_PreviousField = value; }

        public override void LookupKey(ref LetteraKeyDecision key)
        {
            if (key.Key != LetteraKeyCode.Tab) return;
            bool shift = (key.Modifiers & LetteraKeyModifiers.Shift) != 0;
            switch (m_Mode)
            {
                case LetteraTabMode.InsertTab:
                    key.Verb = LetteraEditVerb.InsertTab;
                    break;
                case LetteraTabMode.InsertSpaces:
                    Field.InsertText(new string(' ', Mathf.Max(m_Spaces, 1)), "user.type");
                    key.Verb = LetteraEditVerb.Swallow;
                    break;
                default:
                    Move(!shift);
                    key.Verb = LetteraEditVerb.Swallow;
                    break;
            }
        }

        internal void Move(bool forward)
        {
            GameObject target = forward ? m_NextField : m_PreviousField;
            if (target == null)
            {
                UnityEngine.UI.Selectable next = forward ? m_Next : m_Previous;
                if (next == null) next = UnityEngine.UI.Selectable.allSelectablesArray.Length > 0 ? forward ? UnityEngine.UI.Selectable.allSelectablesArray[0].FindSelectableOnDown() : UnityEngine.UI.Selectable.allSelectablesArray[0].FindSelectableOnUp() : null;
                if (next != null) target = next.gameObject;
            }
            if (target == null) return;
            Field.Selectable.EndSession(LetteraBlurReason.Tab);
            var other = target.GetComponent<LetteraSelectable>();
            if (other != null) other.Activate();
            else if (UnityEngine.EventSystems.EventSystem.current != null) UnityEngine.EventSystems.EventSystem.current.SetSelectedGameObject(target);
        }
    }

    /// <summary>Asks a soft keyboard for a kind of input.</summary>
    [Serializable, LetteraRuleInfo(LetteraRuleGroup.Keyboard, "Keyboard Traits")]
    public class KeyboardTraitsRule : LetteraFieldRule
    {
        [SerializeField] LetteraKeyboardTraits m_Traits = new LetteraKeyboardTraits { Autocorrect = true, Multiline = true };

        public LetteraKeyboardTraits Traits { get => m_Traits; set => m_Traits = value; }

        public override void RequestKeyboard(ref LetteraKeyboardTraits traits)
        {
            bool secure = traits.Secure;
            traits = m_Traits;
            traits.Secure |= secure;
        }
    }

    /// <summary>Lifts a UI object so the caret clears the soft keyboard.</summary>
    [Serializable, LetteraRuleInfo(LetteraRuleGroup.Keyboard, "Keyboard Lift")]
    public class KeyboardLiftRule : LetteraFieldRule
    {
        [SerializeField] RectTransform m_Target;
        [SerializeField] float m_Margin = 16;
        [SerializeField, Min(0)] float m_Duration = 0.2f;
        float m_Lift, m_Applied;

        public RectTransform Target { get => m_Target; set => m_Target = value; }
        public float Margin { get => m_Margin; set => m_Margin = value; }
        public float Duration { get => m_Duration; set => m_Duration = value; }
        public override bool WantsTick => m_Target != null && Field.Selectable.HasFocus || !Mathf.Approximately(m_Applied, 0);

        public override void OnFocusLost(LetteraBlurReason reason) => m_Lift = 0;

        public override void Tick()
        {
            if (m_Target == null) return;
            m_Lift = 0;
            if (Field.Selectable.HasFocus && TouchScreenKeyboard.visible)
            {
                float top = TouchScreenKeyboard.area.height;
                if (Field.Selectable.TryCaretOnScreen(null, out Rect caret) && caret.yMin < top + m_Margin)
                {
                    Canvas canvas = m_Target.GetComponentInParent<Canvas>();
                    m_Lift = (top + m_Margin - caret.yMin + m_Applied) / (canvas != null && canvas.scaleFactor > 0 ? canvas.scaleFactor : 1);
                }
            }
            float next = LetteraAccessibility.ReducedMotion || m_Duration <= 0 ? m_Lift : Mathf.MoveTowards(m_Applied, m_Lift, Mathf.Abs(m_Lift - m_Applied) + Time.unscaledDeltaTime / m_Duration * 200f);
            if (Mathf.Approximately(next, m_Applied)) return;
            m_Target.anchoredPosition += new Vector2(0, next - m_Applied);
            m_Applied = next;
        }
    }

    // ------------------------------------------------------------- filters

    public enum LetteraPasteFilterMode { RejectWhole, KeepLegal }

    /// <summary>Judges what an edit leaves behind. Deleting is always free, and a composition is judged only at its commit.</summary>
    [Serializable]
    public abstract class LetteraCharFilter : LetteraFieldRule
    {
        [SerializeField] LetteraPasteFilterMode m_PasteMode;

        public LetteraPasteFilterMode PasteMode { get => m_PasteMode; set => m_PasteMode = value; }

        /// <summary>Whether the text an edit would leave is legal.</summary>
        protected abstract bool Allows(string result);

        /// <summary>The keyboard that suits this filter.</summary>
        protected virtual LetteraKeyboardKind PreferredKeyboard => LetteraKeyboardKind.Default;

        public override void RequestKeyboard(ref LetteraKeyboardTraits traits)
        {
            if (traits.Kind == LetteraKeyboardKind.Default) traits.Kind = PreferredKeyboard;
        }

        public override void FilterEdit(ref LetteraEditProposal edit)
        {
            if (edit.IsPureDeletion) return;
            if (Allows(edit.ResultText())) return;
            if (m_PasteMode == LetteraPasteFilterMode.RejectWhole)
            {
                edit.Reject();
                return;
            }
            // What passes cluster by cluster stays, and the rest goes.
            var kept = new StringBuilder();
            string text = edit.Text;
            int[] cps = ToCps(text, out int n);
            int at = 0;
            string original = text;
            while (at < n)
            {
                int next = LetteraTextNavigator.NextStop(new ReadOnlySpan<int>(cps, 0, n), at);
                string piece = LetteraSelectable.PlainOf(new ReadOnlySpan<int>(cps, 0, n), at, next);
                edit.Text = kept + piece;
                if (Allows(edit.ResultText())) kept.Append(piece);
                at = next;
            }
            edit.Text = kept.ToString();
            if (original.Length > 0 && kept.Length == 0 && edit.RemovedLength == 0) edit.Reject();
        }

        static int[] ToCps(string text, out int count)
        {
            var cps = new int[text.Length];
            count = 0;
            for (int i = 0; i < text.Length; i++)
            {
                if (char.IsHighSurrogate(text[i]) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1])) cps[count++] = char.ConvertToUtf32(text[i], text[++i]);
                else cps[count++] = text[i];
            }
            return cps;
        }
    }

    [Serializable, LetteraRuleInfo(LetteraRuleGroup.Filters, "Alphanumeric")]
    public class AlphaNumericCharFilter : LetteraCharFilter
    {
        protected override bool Allows(string result)
        {
            for (int i = 0; i < result.Length; i++)
            {
                char c = result[i];
                if (char.IsSurrogate(c))
                {
                    if (char.IsHighSurrogate(c) && i + 1 < result.Length && char.IsLetter(result, i)) { i++; continue; }
                    return false;
                }
                if (!(char.IsLetter(c) || (c >= '0' && c <= '9') || LetteraUnicode.IsMark(c))) return false;
            }
            return true;
        }
    }

    [Serializable, LetteraRuleInfo(LetteraRuleGroup.Filters, "Whole Number")]
    public class WholeNumberCharFilter : LetteraCharFilter
    {
        [SerializeField] bool m_NegativeAllowed;

        public bool NegativeAllowed { get => m_NegativeAllowed; set => m_NegativeAllowed = value; }
        protected override LetteraKeyboardKind PreferredKeyboard => m_NegativeAllowed ? LetteraKeyboardKind.Ascii : LetteraKeyboardKind.Number;

        protected override bool Allows(string result)
        {
            for (int i = 0; i < result.Length; i++)
            {
                char c = result[i];
                if (c >= '0' && c <= '9') continue;
                if (c == '-' && i == 0 && m_NegativeAllowed) continue;
                return false;
            }
            return true;
        }
    }

    [Serializable, LetteraRuleInfo(LetteraRuleGroup.Filters, "Decimal")]
    public class DecimalCharFilter : LetteraCharFilter
    {
        [SerializeField] bool m_NegativeAllowed = true;
        [Tooltip("The characters allowed as the one decimal separator.")]
        [SerializeField] string m_Separators = ".";

        public bool NegativeAllowed { get => m_NegativeAllowed; set => m_NegativeAllowed = value; }
        public string Separators { get => m_Separators; set => m_Separators = value; }
        protected override LetteraKeyboardKind PreferredKeyboard => LetteraKeyboardKind.Decimal;

        protected override bool Allows(string result)
        {
            bool separator = false;
            for (int i = 0; i < result.Length; i++)
            {
                char c = result[i];
                if (c >= '0' && c <= '9') continue;
                if (c == '-' && i == 0 && m_NegativeAllowed) continue;
                if (!separator && (m_Separators ?? "").IndexOf(c) >= 0)
                {
                    separator = true;
                    continue;
                }
                return false;
            }
            return true;
        }
    }

    [Serializable, LetteraRuleInfo(LetteraRuleGroup.Filters, "Email")]
    public class EmailCharFilter : LetteraCharFilter
    {
        const string Allowed = ".!#$%&'*+/=?^_{|}~-";
        protected override LetteraKeyboardKind PreferredKeyboard => LetteraKeyboardKind.Email;

        protected override bool Allows(string result)
        {
            bool at = false;
            foreach (char c in result)
            {
                if (c == '@')
                {
                    if (at) return false;
                    at = true;
                    continue;
                }
                if (char.IsSurrogate(c) || !(char.IsLetterOrDigit(c) || Allowed.IndexOf(c) >= 0)) return false;
            }
            return true;
        }
    }

    [Serializable, LetteraRuleInfo(LetteraRuleGroup.Filters, "Person Name")]
    public class PersonNameCharFilter : LetteraCharFilter
    {
        protected override LetteraKeyboardKind PreferredKeyboard => LetteraKeyboardKind.Name;

        protected override bool Allows(string result)
        {
            foreach (char c in result)
            {
                if (char.IsLetter(c) || LetteraUnicode.IsMark(c) || c == ' ' || c == '-' || c == '\'' || c == '’') continue;
                return false;
            }
            return true;
        }
    }

    // ------------------------------------------------------------- validation

    public enum LetteraCheckTime { OnChange, OnBlur, OnSubmit, Always }
    internal enum LetteraCheckTrigger { Change, Blur, Submit, Focus }

    /// <summary>What a check found: an empty status means valid.</summary>
    public readonly struct LetteraCheckResult
    {
        public const string Invalid = "invalid", Pending = "pending";
        public readonly string Status, Message;

        public LetteraCheckResult(string status, string message = null)
        {
            Status = status;
            Message = message;
        }

        public bool IsValid => string.IsNullOrEmpty(Status);
    }

    /// <summary>A check on a field's value. It only reports: nothing is ever refused because of it.</summary>
    [Serializable]
    public abstract class LetteraValueCheck
    {
        public abstract LetteraCheckResult Check(ILetteraTextView value);
        public virtual LetteraValueCheck Clone() => (LetteraValueCheck)MemberwiseClone();
    }

    /// <summary>Runs checks at a chosen time and publishes the result. The first status wins, except that pending lets the next ones run.</summary>
    [Serializable, LetteraRuleInfo(LetteraRuleGroup.Validation, "Validation")]
    public class ValidationRule : LetteraFieldRule
    {
        [SerializeReference] List<LetteraValueCheck> m_Checks = new List<LetteraValueCheck>();
        [SerializeField] LetteraCheckTime m_When = LetteraCheckTime.OnBlur;
        [SerializeField] bool m_BlockSubmitWhenInvalid;

        public List<LetteraValueCheck> Checks => m_Checks;
        public LetteraCheckTime When { get => m_When; set => m_When = value; }
        public bool BlockSubmitWhenInvalid { get => m_BlockSubmitWhenInvalid; set => m_BlockSubmitWhenInvalid = value; }
        public override bool BlocksSubmit => m_BlockSubmitWhenInvalid && Field.LastCheck.Status == LetteraCheckResult.Invalid;

        public override LetteraFieldRule Clone()
        {
            var copy = (ValidationRule)base.Clone();
            copy.m_Checks = new List<LetteraValueCheck>();
            foreach (LetteraValueCheck check in m_Checks) copy.m_Checks.Add(check?.Clone());
            return copy;
        }

        internal void Run(LetteraCheckTrigger trigger)
        {
            bool due;
            switch (m_When)
            {
                case LetteraCheckTime.OnChange: due = trigger == LetteraCheckTrigger.Change; break;
                case LetteraCheckTime.OnSubmit: due = trigger == LetteraCheckTrigger.Submit; break;
                case LetteraCheckTime.Always: due = trigger != LetteraCheckTrigger.Blur; break;
                default: due = trigger == LetteraCheckTrigger.Blur; break;
            }
            if (!due) return;
            var result = new LetteraCheckResult(null);
            foreach (LetteraValueCheck check in m_Checks)
            {
                if (check == null) continue;
                LetteraCheckResult one = check.Check(Field);
                if (one.IsValid) continue;
                result = one;
                if (one.Status != LetteraCheckResult.Pending) break;
            }
            Field.SetCheck(result);
        }
    }

    // ------------------------------------------------------------- decorators

    /// <summary>A rule that shows the field's state on UI you assign. A decorator costs nothing per frame unless it animates.</summary>
    [Serializable]
    public abstract class LetteraFieldDecorator : LetteraFieldRule
    {
        protected override void OnEnable() => Field.RefreshDecorators();

        protected static void SetShown(LetteraText text, bool shown)
        {
            if (text == null) return;
            // The object stays active, so a layout around it stays as it is.
            if (shown) text.Show();
            else text.Hide();
        }
    }

    /// <summary>A placeholder: shows while the field is empty, composition included.</summary>
    [Serializable, LetteraRuleInfo(LetteraRuleGroup.Decoration, "Hint")]
    public class HintDecorator : LetteraFieldDecorator
    {
        [SerializeField] LetteraText m_Target;
        [SerializeField] bool m_HideOnFocus;

        public LetteraText Target { get => m_Target; set { m_Target = value; Field?.RefreshDecorators(); } }
        public bool HideOnFocus { get => m_HideOnFocus; set { m_HideOnFocus = value; Field?.RefreshDecorators(); } }

        public override void OnSnapshot(in LetteraFieldSnapshot snapshot) => SetShown(m_Target, snapshot.IsEmpty && !(m_HideOnFocus && snapshot.HasFocus));

        protected override void OnDisable() => SetShown(m_Target, true);
    }

    /// <summary>A pose of a floating label.</summary>
    [Serializable]
    public struct LetteraLabelPose
    {
        public Vector2 Position;
        public float Scale;
        public Color Color;
    }

    /// <summary>A floating label: raised while the field has focus or holds text.</summary>
    [Serializable, LetteraRuleInfo(LetteraRuleGroup.Decoration, "Raised Label")]
    public class RaisedLabelDecorator : LetteraFieldDecorator
    {
        [SerializeField] RectTransform m_Label;
        [SerializeField] LetteraLabelPose m_RestPose = new LetteraLabelPose { Scale = 1, Color = Color.white };
        [SerializeField] LetteraLabelPose m_RaisedPose = new LetteraLabelPose { Position = new Vector2(0, 20), Scale = 0.75f, Color = Color.white };
        [SerializeField, Min(0)] float m_Seconds = 0.15f;
        [SerializeField] AnimationCurve m_Ease = AnimationCurve.EaseInOut(0, 0, 1, 1);
        float m_Progress, m_Goal;
        double m_LastTime;
        bool m_Animating;

        public RectTransform Label { get => m_Label; set { m_Label = value; Field?.RefreshDecorators(); } }
        public LetteraLabelPose RestPose { get => m_RestPose; set => m_RestPose = value; }
        public LetteraLabelPose RaisedPose { get => m_RaisedPose; set => m_RaisedPose = value; }
        public float Seconds { get => m_Seconds; set => m_Seconds = value; }
        public override bool WantsTick => m_Animating;

        /// <summary>0 at rest, 1 raised.</summary>
        public float Progress => m_Progress;

        public override void OnSnapshot(in LetteraFieldSnapshot snapshot)
        {
            float goal = snapshot.HasFocus || !snapshot.IsEmpty ? 1 : 0;
            if (Mathf.Approximately(goal, m_Goal) && !m_Animating && Mathf.Approximately(m_Progress, goal)) return;
            m_Goal = goal;
            if (LetteraAccessibility.ReducedMotion || m_Seconds <= 0)
            {
                m_Progress = goal;
                m_Animating = false;
                ApplyPose();
                return;
            }
            m_Animating = true;
            m_LastTime = LetteraEditClock.Now;
        }

        public override void Tick()
        {
            double now = LetteraEditClock.Now;
            float step = (float)(now - m_LastTime) / Mathf.Max(m_Seconds, 0.0001f);
            m_LastTime = now;
            m_Progress = Mathf.MoveTowards(m_Progress, m_Goal, step);
            if (Mathf.Approximately(m_Progress, m_Goal)) m_Animating = false;
            ApplyPose();
        }

        void ApplyPose()
        {
            if (m_Label == null) return;
            float t = m_Ease != null ? m_Ease.Evaluate(m_Progress) : m_Progress;
            m_Label.anchoredPosition = Vector2.LerpUnclamped(m_RestPose.Position, m_RaisedPose.Position, t);
            float scale = Mathf.LerpUnclamped(m_RestPose.Scale, m_RaisedPose.Scale, t);
            m_Label.localScale = new Vector3(scale, scale, 1);
            var text = m_Label.GetComponent<UnityEngine.UI.Graphic>();
            if (text != null) text.color = Color.LerpUnclamped(m_RestPose.Color, m_RaisedPose.Color, t);
        }
    }

    /// <summary>A line under a field: helper text, or the check's message.</summary>
    [Serializable, LetteraRuleInfo(LetteraRuleGroup.Decoration, "Helper Text")]
    public class HelperTextDecorator : LetteraFieldDecorator
    {
        [SerializeField] LetteraText m_Target;
        [SerializeField, TextArea] string m_HelperText = "";
        [SerializeField] Color m_HelperColor = new Color(1, 1, 1, 0.6f);
        [SerializeField] Color m_ErrorColor = new Color(1f, 0.35f, 0.3f, 1f);

        public LetteraText Target { get => m_Target; set { m_Target = value; Field?.RefreshDecorators(); } }
        public string HelperText { get => m_HelperText; set { m_HelperText = value; Field?.RefreshDecorators(); } }
        public Color HelperColor { get => m_HelperColor; set => m_HelperColor = value; }
        public Color ErrorColor { get => m_ErrorColor; set => m_ErrorColor = value; }

        public override void OnSnapshot(in LetteraFieldSnapshot snapshot)
        {
            if (m_Target == null) return;
            LetteraCheckResult check = snapshot.LastCheck;
            bool failing = !check.IsValid && check.Status != LetteraCheckResult.Pending;
            m_Target.Text = failing && !string.IsNullOrEmpty(check.Message) ? check.Message : m_HelperText;
            m_Target.color = failing ? m_ErrorColor : m_HelperColor;
        }
    }

    /// <summary>Shows how much the field holds, counted in the same space as its limit.</summary>
    [Serializable, LetteraRuleInfo(LetteraRuleGroup.Decoration, "Length Counter")]
    public class LengthCounterDecorator : LetteraFieldDecorator
    {
        [SerializeField] LetteraText m_Target;
        [Tooltip("{0} is the count, {1} the limit. Empty uses {0}, or {0}/{1} under a limit.")]
        [SerializeField] string m_Format = "";
        [SerializeField] Color m_NormalColor = new Color(1, 1, 1, 0.6f);
        [SerializeField] Color m_CapColor = new Color(1f, 0.35f, 0.3f, 1f);
        [Tooltip("Used only when no limit decides the unit.")]
        [SerializeField] LetteraCountUnit m_Unit;
        int m_CountedVersion = -1, m_Count;
        int m_ShownCount = -1, m_ShownLimit = -1;

        public LetteraText Target { get => m_Target; set { m_Target = value; m_ShownCount = -1; Field?.RefreshDecorators(); } }
        public string Format { get => m_Format; set { m_Format = value; m_ShownCount = -1; Field?.RefreshDecorators(); } }
        public Color CapColor { get => m_CapColor; set => m_CapColor = value; }

        /// <summary>The count in the limit's unit, cached by the text's version.</summary>
        public int Count
        {
            get
            {
                if (m_CountedVersion == Field.Version) return m_Count;
                m_CountedVersion = Field.Version;
                string text = Field.PublishedLimit > 0 && Field.PublishedCountsSource ? Field.Source : Field.PlainText;
                m_Count = MaxLengthRule.Count(text, Field.PublishedUnit ?? m_Unit);
                return m_Count;
            }
        }

        public override void OnSnapshot(in LetteraFieldSnapshot snapshot)
        {
            if (m_Target == null) return;
            int count = Count, limit = snapshot.Limit;
            if (count == m_ShownCount && limit == m_ShownLimit) return;
            m_ShownCount = count;
            m_ShownLimit = limit;
            string format = !string.IsNullOrEmpty(m_Format) ? m_Format : limit > 0 ? "{0}/{1}" : "{0}";
            m_Target.Text = string.Format(format, count, limit);
            m_Target.color = limit > 0 && count >= limit ? m_CapColor : m_NormalColor;
        }

        public override void OnFocusGained() => m_CountedVersion = -1;
    }
}

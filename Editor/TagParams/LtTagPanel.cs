using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEditor;
using UnityEditor.ShortcutManagement;
using UnityEngine;
using UnityEngine.UIElements;

namespace FirstSymbol.Lettera.Editor
{
    /// <summary>What the panel needs from the inspector that hosts it.</summary>
    internal sealed class LtTagPanelHost
    {
        public Func<IReadOnlyList<LetteraStyle>> Styles = () => null;
        public Func<float> EmPixels = () => 0;
        /// <summary>0 when the written source is shown, else a preview.</summary>
        public Func<int> Mode = () => 0;
        public Func<bool> Multiple = () => false;
        public Func<bool> ReadOnly = () => false;
        public Action<Type> OpenStyle = _ => { };
    }

    /// <summary>
    /// The strip under the text field that shows the tag the caret is in, with a row for each of its slots. An edit goes back into the text as
    /// the least markup, one undo step per gesture, and the caret stays where it was.
    /// </summary>
    internal sealed class LtTagPanel : VisualElement
    {
        const int WindowFrom = 20000, Window = 2000;

        readonly TextField m_Source;
        readonly LtTagPanelHost m_Host;
        readonly LtGesture m_Gesture;
        readonly VisualElement m_Head, m_Crumbs, m_Markup, m_Body, m_Wrap;
        readonly Label m_Title, m_Effect, m_Signature;
        readonly LtTagForm m_Form;

        string m_Text = "";
        int m_Cursor = -1, m_Select = -1;
        bool m_Dirty = true;
        int m_PinStart = -1, m_ActiveStart = -1, m_ActiveOrdinal;
        string m_ActiveName;
        LetteraTagSpan m_Active;
        List<LetteraTagSpan> m_Enclosing = new List<LetteraTagSpan>();
        LetteraEffect m_ActiveEffect;
        LtWrapPicker m_Picker;
        int m_WrapFrom, m_WrapTo;
        bool m_SourceFocused;
        string m_ShownKey;
        IVisualElementScheduledItem m_Job;

        /// <summary>Where the caret and the other end of the selection are. The text field's, unless a test says otherwise.</summary>
        internal Func<int> Cursor, Select;

        public static LtTagPanel Focused { get; private set; }

        /// <summary>The tag the panel edits now, for tests.</summary>
        public LetteraTagSpan ActiveTag => m_Active;
        public LtTagForm Form => m_Form;
        public LtWrapPicker Picker => m_Picker;
        public string Hint { get; private set; } = "";
        public string Kind { get; private set; } = "";

        public LtTagPanel(TextField source, LtTagPanelHost host)
        {
            m_Source = source;
            Cursor = () => source.cursorIndex;
            Select = () => source.selectIndex;
            m_Host = host;
            AddToClassList("lt-tagpanel");
            m_Gesture = new LtGesture(this);
            m_Title = LtUI.Text("TAG", "lt-tagpanel__title");
            m_Crumbs = LtUI.El("lt-tagpanel__crumbs");
            m_Effect = LtUI.Text("", "lt-tagpanel__effect");
            m_Head = LtUI.El("lt-tagpanel__head", m_Title, m_Crumbs, LtUI.El("lt-tagpanel__space"), m_Effect);
            m_Markup = LtUI.El("lt-tagpanel__markup");
            m_Body = LtUI.El("lt-tagpanel__body");
            m_Wrap = LtUI.El("lt-tagpanel__wrap");
            m_Signature = LtUI.Text("", "lt-tagpanel__signature");
            m_Form = new LtTagForm(() => m_Host.EmPixels());
            m_Form.SlotChanged += Write;
            m_Form.Holding += m_Gesture.Hold;
            Add(m_Wrap);
            Add(m_Head);
            Add(m_Markup);
            Add(m_Body);
            Add(m_Signature);

            source.RegisterCallback<FocusInEvent>(_ =>
            {
                m_SourceFocused = true;
                Focused = this;
                m_Dirty = true;
            });
            source.RegisterCallback<FocusOutEvent>(_ =>
            {
                m_SourceFocused = false;
                if (Focused == this && !Inside(this)) Focused = this;
            });
            source.RegisterValueChangedCallback(_ => m_Dirty = true);
            RegisterCallback<AttachToPanelEvent>(_ =>
            {
                m_Job?.Pause();
                m_Job = schedule.Execute(Tick).Every(100);
            });
            RegisterCallback<DetachFromPanelEvent>(_ =>
            {
                m_Job?.Pause();
                if (Focused == this) Focused = null;
            });
            Refresh();
        }

        bool Inside(VisualElement element)
        {
            VisualElement focused = element.focusController?.focusedElement as VisualElement;
            return focused != null && element.Contains(focused);
        }

        // ------------------------------------------------------------- following the caret

        void Tick()
        {
            bool editingHere = m_SourceFocused || Inside(this);
            if (!editingHere && !m_Dirty) return;
            string text = m_Source.value ?? "";
            int cursor = Cursor(), select = Select();
            bool moved = cursor != m_Cursor || select != m_Select;
            if (!m_Dirty && !moved && string.Equals(text, m_Text, StringComparison.Ordinal)) return;
            // The caret moving in the source lets go of a chosen breadcrumb; the panel being edited keeps its tag.
            if (moved && m_SourceFocused) m_PinStart = -1;
            if (!m_SourceFocused && Inside(this) && !m_Dirty && !string.Equals(text, m_Text, StringComparison.Ordinal)) m_Dirty = true;
            Refresh();
        }

        /// <summary>Reads the text and caret again and draws what they hold.</summary>
        public void Refresh()
        {
            m_Dirty = false;
            SetEnabled(!m_Host.ReadOnly());
            m_Text = m_Source.value ?? "";
            m_Cursor = Cursor();
            m_Select = Select();
            m_Cursor = Mathf.Clamp(m_Cursor, 0, m_Text.Length);
            m_Select = Mathf.Clamp(m_Select, 0, m_Text.Length);

            if (m_Host.Mode() != 0)
            {
                Clear("Switch to Source to edit tags.");
                return;
            }
            if (m_Host.Multiple())
            {
                Clear("Select one text.");
                return;
            }

            int from = 0, to = m_Text.Length;
            if (m_Text.Length > WindowFrom)
            {
                from = Mathf.Max(0, m_Cursor - Window);
                to = Mathf.Min(m_Text.Length, m_Cursor + Window);
            }
            List<LetteraTagSpan> tags = LetteraTagScanner.Scan(m_Text, from, to);
            bool followCaret = m_SourceFocused || m_ActiveStart < 0 || !Inside(this);
            LetteraTagSpan active = null;
            if (followCaret)
            {
                m_Enclosing = LetteraTagScanner.Enclosing(tags, m_Cursor);
                if (m_PinStart >= 0)
                    foreach (LetteraTagSpan tag in m_Enclosing)
                        if (tag.Start == m_PinStart)
                            active = tag;
                if (active == null && m_Enclosing.Count > 0) active = m_Enclosing[m_Enclosing.Count - 1];
            }
            else
            {
                active = FindAgain(tags);
                m_Enclosing = LetteraTagScanner.Enclosing(tags, active != null ? active.Start + 1 : m_Cursor);
                if (active == null)
                {
                    followCaret = true;
                    m_Enclosing = LetteraTagScanner.Enclosing(tags, m_Cursor);
                    if (m_Enclosing.Count > 0) active = m_Enclosing[m_Enclosing.Count - 1];
                }
            }
            m_Active = active;
            if (active == null)
            {
                m_ActiveStart = -1;
                ShowNoTag();
                return;
            }
            m_ActiveStart = active.Start;
            m_ActiveName = active.Name;
            m_ActiveOrdinal = 0;
            foreach (LetteraTagSpan tag in tags)
            {
                if (tag == active) break;
                if (string.Equals(tag.Name, active.Name, StringComparison.OrdinalIgnoreCase)) m_ActiveOrdinal++;
            }
            ShowTag(active);
        }

        // After an edit or an undo: the same tag by place, else the same one among those of its name.
        LetteraTagSpan FindAgain(List<LetteraTagSpan> tags)
        {
            LetteraTagSpan byOrdinal = null;
            int seen = 0;
            foreach (LetteraTagSpan tag in tags)
            {
                if (tag.Start == m_ActiveStart && string.Equals(tag.Name, m_ActiveName, StringComparison.OrdinalIgnoreCase)) return tag;
                if (string.Equals(tag.Name, m_ActiveName, StringComparison.OrdinalIgnoreCase) && seen++ == m_ActiveOrdinal) byOrdinal = tag;
            }
            return byOrdinal;
        }

        // ------------------------------------------------------------- what the panel shows

        void Clear(string hint)
        {
            m_Active = null;
            m_Enclosing.Clear();
            Say(hint, "hint");
            m_Head.style.display = DisplayStyle.None;
            m_Markup.style.display = DisplayStyle.None;
            m_Signature.style.display = DisplayStyle.None;
        }

        void Say(string text, string kind)
        {
            Kind = kind;
            Hint = text;
            m_Body.Clear();
            m_Body.Add(LtUI.Text(text, "lt-hint lt-tagpanel__hint"));
        }

        void ShowNoTag()
        {
            m_Head.style.display = DisplayStyle.None;
            m_Markup.style.display = DisplayStyle.None;
            m_Signature.style.display = DisplayStyle.None;
            m_Form.RemoveFromHierarchy();
            LetteraTagProblem problem = LetteraTagScanner.Diagnose(m_Text, m_Cursor, out int start);
            m_Body.Clear();
            if (problem != LetteraTagProblem.None)
            {
                Kind = problem == LetteraTagProblem.NotClosed ? "unclosed" : "quote";
                Hint = problem == LetteraTagProblem.NotClosed ? "This tag is not closed, so it shows as text." : "Quote is not closed.";
                m_Head.style.display = DisplayStyle.Flex;
                m_Title.text = "TAG";
                m_Crumbs.Clear();
                m_Effect.text = "";
                m_Body.Add(new LtFix(Hint, null, "Fix", () => Mend(problem, start)));
                int rawFrom = start, rawTo = m_Text.IndexOf('\n', start);
                if (rawTo < 0) rawTo = m_Text.Length;
                var raw = new TextField { value = m_Text.Substring(rawFrom, Mathf.Min(rawTo - rawFrom, 200)), isReadOnly = true };
                raw.AddToClassList("lt-text-input");
                raw.AddToClassList("lt-tagpanel__raw");
                m_Body.Add(raw);
                return;
            }
            Kind = m_Cursor != m_Select ? "selection" : "caret";
            Hint = m_Cursor != m_Select
                ? Math.Abs(m_Cursor - m_Select).ToString(CultureInfo.InvariantCulture) + " characters selected."
                : "Put the caret inside a tag to edit its values.";
            VisualElement row = LtUI.El("lt-tagpanel__hintrow", LtUI.Text("TAG", "lt-tagpanel__title"), LtUI.Text(Hint, "lt-hint lt-tagpanel__hint"));
            VisualElement button = LtUI.Clickable("Wrap in tag...  Ctrl+.", "lt-fix__button lt-tagpanel__wrapbutton", "Choose a tag to wrap the selection in, or to insert at the caret.", () => OpenWrap(false));
            row.Add(button);
            m_Body.Add(row);
        }

        void ShowTag(LetteraTagSpan tag)
        {
            IReadOnlyList<LetteraStyle> styles = m_Host.Styles();
            HashSet<string> known = LetteraVocabulary.Tags(styles ?? new List<LetteraStyle>());
            LetteraStyle style = null;
            if (styles != null)
                foreach (LetteraStyle candidate in styles)
                {
                    if (candidate == null || !candidate.Enabled || candidate.Effect == null || !(candidate.Matcher is TagMatcher match)) continue;
                    if (!string.Equals(match.Name, tag.Name, StringComparison.OrdinalIgnoreCase)) continue;
                    if (style == null || (!LetteraTagModel.HasSchema(style.Effect.Parameters) && LetteraTagModel.HasSchema(candidate.Effect.Parameters))) style = candidate;
                }
            m_ActiveEffect = style?.Effect;

            m_Head.style.display = DisplayStyle.Flex;
            m_Signature.style.display = DisplayStyle.Flex;
            BuildCrumbs(tag);
            m_Effect.text = style?.Effect != null ? EffectLabel(style.Effect) : "";
            string key = tag.Start + "|" + tag.OpenText(m_Text);

            if (!known.Contains(tag.Name))
            {
                Kind = "unknown";
                Hint = "No style uses <" + tag.Name + ">. It shows as plain text.";
                m_Markup.style.display = DisplayStyle.None;
                m_Signature.style.display = DisplayStyle.None;
                m_Form.RemoveFromHierarchy();
                m_Body.Clear();
                m_Body.Add(LtUI.Text(Hint, "lt-note lt-tagpanel__notice"));
                return;
            }

            BuildMarkup(tag);
            IReadOnlyList<LetteraParamInfo> described = style?.Effect?.Parameters;
            if (style == null || style.Effect is CompositeEffect || !LetteraTagModel.HasSchema(described))
            {
                Kind = "noschema";
                Hint = "No slot form for this effect yet.";
                m_Form.RemoveFromHierarchy();
                m_Signature.text = "";
                m_Signature.style.display = DisplayStyle.None;
                string value = tag.EqualsAt >= 0 ? m_Text.Substring(tag.EqualsAt + 1, tag.ValueEnd - tag.EqualsAt - 1) : "";
                m_Body.Clear();
                m_Body.Add(LtUI.Text(Hint + " Edit its value as text.", "lt-hint lt-tagpanel__hint"));
                var raw = new TextField { value = value, isDelayed = true };
                raw.AddToClassList("lt-text-input");
                raw.AddToClassList("lt-tagpanel__raw");
                raw.RegisterValueChangedCallback(e => WriteRaw(e.newValue));
                m_Body.Add(raw);
                return;
            }

            Kind = "form";
            Hint = "";
            string[] defaults = style.ParsedDefault();
            List<LetteraSlotRow> rows = LetteraTagModel.Rows(tag, described, style.Effect, defaults);
            int count = LetteraTagModel.SlotCount(described);
            List<string> extras = null;
            for (int i = count; i < tag.Slots.Count; i++)
                (extras ?? (extras = new List<string>())).Add("Extra " + (i + 1) + ": " + (tag.Raw(m_Text, i).Length == 0 ? "empty" : tag.Raw(m_Text, i)));
            if (m_Form.parent != m_Body)
            {
                m_Body.Clear();
                m_Body.Add(m_Form);
            }
            m_Form.Show(described, rows, extras);
            int slot = m_SourceFocused ? LetteraTagScanner.SlotAt(tag, m_Cursor) : -1;
            m_Form.Mark(slot);
            UpdateSignature(tag, described, slot, count);
            m_ShownKey = key;
        }

        static string EffectLabel(LetteraEffect effect)
        {
            string name = LetteraStyleList.Pretty(effect.GetType().Name, "Effect");
            foreach (LetteraStyleTemplate template in LetteraStyleTemplates.All)
            {
                if (template.Driven != LetteraStyleTemplates.Tags || template.Effect == null) continue;
                try
                {
                    if (template.Effect().GetType() == effect.GetType()) return name + " . " + template.Kind;
                }
                catch (Exception)
                {
                    // Not every template can be made out of context.
                }
            }
            return name;
        }

        void BuildCrumbs(LetteraTagSpan active)
        {
            m_Crumbs.Clear();
            var chain = m_Enclosing.Count > 0 && m_Enclosing.Contains(active) ? m_Enclosing : new List<LetteraTagSpan> { active };
            foreach (LetteraTagSpan tag in chain)
            {
                LetteraTagSpan chosen = tag;
                Label chip = LtUI.Clickable("<" + tag.Name + ">", "lt-chip lt-chip--click lt-tagpanel__crumb", "Edit this tag.", () =>
                {
                    m_PinStart = chosen.Start;
                    m_Dirty = true;
                    Refresh();
                });
                chip.EnableInClassList("lt-chip--accent", tag == active);
                m_Crumbs.Add(chip);
            }
            if (!string.IsNullOrEmpty(active.Label))
            {
                Label label = LtUI.Text("#" + active.Label, "lt-chip lt-tagpanel__label");
                label.tooltip = "The label code uses to find this range.";
                m_Crumbs.Add(label);
            }
        }

        // The open tag as it will be written. Each value is a piece; a click on one goes to its row.
        void BuildMarkup(LetteraTagSpan tag)
        {
            m_Markup.style.display = DisplayStyle.Flex;
            m_Markup.Clear();
            int slotAtCaret = LetteraTagScanner.SlotAt(tag, m_Cursor);
            if (tag.EqualsAt < 0)
            {
                m_Markup.Add(LtUI.Text(tag.OpenText(m_Text), "lt-tagpanel__tok"));
                return;
            }
            m_Markup.Add(LtUI.Text(m_Text.Substring(tag.Start, tag.EqualsAt + 1 - tag.Start), "lt-tagpanel__tok lt-tagpanel__tok--tag"));
            for (int i = 0; i < tag.Slots.Count; i++)
            {
                int index = i;
                string raw = tag.Raw(m_Text, i);
                Label token = LtUI.Clickable(raw.Length == 0 ? "·" : raw, "lt-tagpanel__tok lt-tagpanel__tok--slot", "Slot " + (i + 1), () => m_Form.FocusSlot(index));
                token.EnableInClassList("lt-tagpanel__tok--on", slotAtCaret == i);
                token.EnableInClassList("lt-tagpanel__tok--empty", raw.Length == 0);
                m_Markup.Add(token);
                if (i + 1 < tag.Slots.Count)
                    m_Markup.Add(LtUI.Text(m_Text.Substring(tag.Slots[i].End, tag.Slots[i + 1].Start - tag.Slots[i].End), "lt-tagpanel__tok lt-tagpanel__tok--tag"));
            }
            m_Markup.Add(LtUI.Text(m_Text.Substring(tag.ValueEnd, tag.End - tag.ValueEnd), "lt-tagpanel__tok lt-tagpanel__tok--tag"));
        }

        void UpdateSignature(LetteraTagSpan tag, IReadOnlyList<LetteraParamInfo> described, int slot, int count) =>
            m_Signature.text = SignatureText(tag.Name, described, slot);

        /// <summary>The line under the panel: the slot the caret is in, or else the tag's whole shape.</summary>
        internal static string SignatureText(string tag, IReadOnlyList<LetteraParamInfo> described, int slot)
        {
            LetteraParamInfo info = null;
            foreach (LetteraParamInfo candidate in described)
                if (candidate.Slot == slot)
                    info = candidate;
            if (info != null) return "slot " + (slot + 1) + " of " + LetteraTagModel.SlotCount(described) + ": " + info.Display + ", " + LetteraSlotValue.Describe(info);
            var names = new List<string>();
            foreach (LetteraParamInfo candidate in described)
                if (candidate.Slot >= 0)
                    names.Add(candidate.Display.ToLowerInvariant());
            return "<" + tag + "=" + string.Join(", ", names) + ">";
        }

        public string Signature => m_Signature.text;

        // ------------------------------------------------------------- writing

        // Typing and dragging are one step until they pause; a click or a reset is a step of its own.
        internal void Write(int slot, string token) => Write(slot, token, token == null || !(m_Form.Editing || m_Gesture.IsHeld));

        internal void Write(int slot, string token, bool discrete)
        {
            LetteraTagSpan tag = Current();
            if (tag == null) return;
            LetteraTextEdit edit = LetteraTagWriter.SetSlot(m_Text, tag, slot, token);
            if (!edit.Ok)
            {
                Hint = "That value cannot be written inside a tag: it holds both kinds of quote.";
                m_Signature.text = Hint;
                return;
            }
            if (edit.Text == m_Text) return;
            Apply(edit, discrete);
        }

        void WriteRaw(string value)
        {
            LetteraTagSpan tag = Current();
            if (tag == null) return;
            string text = m_Text;
            LetteraTextEdit edit;
            string raw = (value ?? "").Trim();
            if (tag.EqualsAt >= 0)
                edit = raw.Length == 0
                    ? new LetteraTextEdit(text.Remove(tag.EqualsAt, tag.ValueEnd - tag.EqualsAt), tag.EqualsAt, tag.ValueEnd - tag.EqualsAt, 0)
                    : new LetteraTextEdit(text.Substring(0, tag.EqualsAt + 1) + raw + text.Substring(tag.ValueEnd), tag.EqualsAt + 1, tag.ValueEnd - tag.EqualsAt - 1, raw.Length);
            else if (raw.Length == 0) return;
            else edit = new LetteraTextEdit(text.Insert(tag.HeadEnd, "=" + raw), tag.HeadEnd, 0, raw.Length + 1);
            Apply(edit, true);
        }

        /// <summary>Back to the bare tag: every slot inherits.</summary>
        public void ResetAll()
        {
            LetteraTagSpan tag = Current();
            if (tag == null) return;
            LetteraTextEdit edit = LetteraTagWriter.Clear(m_Text, tag);
            if (edit.Ok && edit.Removed > 0) Apply(edit, true);
        }

        LetteraTagSpan Current()
        {
            m_Text = m_Source.value ?? "";
            return FindAgain(LetteraTagScanner.Scan(m_Text, 0, m_Text.Length));
        }

        void Apply(LetteraTextEdit edit, bool discrete)
        {
            int cursor = Cursor(), select = Select();
            m_Gesture.Run(() =>
            {
                m_Source.value = edit.Text;
            }, discrete);
            if (m_SourceFocused) m_Source.SelectRange(edit.Map(cursor), edit.Map(select));
            m_Dirty = true;
            Refresh();
        }

        /// <summary>What the Fix button does: closes the tag or the quote the caret is in.</summary>
        internal void Fix()
        {
            m_Text = m_Source.value ?? "";
            LetteraTagProblem problem = LetteraTagScanner.Diagnose(m_Text, Cursor(), out int start);
            if (problem != LetteraTagProblem.None) Mend(problem, start);
        }

        internal void CloseGesture() => m_Gesture.Close();

        void Mend(LetteraTagProblem problem, int start)
        {
            string mended = LetteraTagScanner.Mend(m_Text, start, problem, out int at, out string inserted);
            if (mended == m_Text) return;
            Apply(new LetteraTextEdit(mended, at, 0, inserted.Length), true);
        }

        // ------------------------------------------------------------- wrap

        /// <summary>The Ctrl+. key: into the tag's form when the caret is in one, else the wrap list.</summary>
        public void Shortcut()
        {
            if (m_Host.Mode() != 0 || m_Host.Multiple()) return;
            Refresh();
            if (m_Active != null && Kind == "form") m_Form.FocusSlot(Mathf.Max(0, LetteraTagScanner.SlotAt(m_Active, m_Cursor)));
            else OpenWrap(false);
        }

        public void OpenWrap(bool edit)
        {
            if (m_Host.ReadOnly()) return;
            CloseWrap();
            m_WrapFrom = Mathf.Min(Cursor(), Select());
            m_WrapTo = Mathf.Max(Cursor(), Select());
            m_WrapFrom = Mathf.Clamp(m_WrapFrom, 0, (m_Source.value ?? "").Length);
            m_WrapTo = Mathf.Clamp(m_WrapTo, m_WrapFrom, (m_Source.value ?? "").Length);
            var all = LetteraWrapList.Build(m_Host.Styles(), m_Source.value);
            int a = m_WrapFrom, b = m_WrapTo;
            LetteraTagScanner.Balance(m_Source.value ?? "", ref a, ref b, out bool adjusted);
            m_Picker = new LtWrapPicker(all, adjusted ? "Adjusted to keep tags balanced." : null);
            m_Picker.Chosen += (entry, editNow) =>
            {
                Wrap(entry.Tag, a, b);
                CloseWrap();
                if (editNow) schedule.Execute(() => m_Form.FocusSlot(0)).ExecuteLater(150);
            };
            m_Picker.Closed += () =>
            {
                m_Picker = null;
                m_Source.Focus();
            };
            m_Wrap.Add(m_Picker);
        }

        public void CloseWrap()
        {
            if (m_Picker == null) return;
            LtWrapPicker picker = m_Picker;
            m_Picker = null;
            picker.RemoveFromHierarchy();
        }

        void Wrap(string tag, int a, int b)
        {
            string text = m_Source.value ?? "";
            a = Mathf.Clamp(a, 0, text.Length);
            b = Mathf.Clamp(b, a, text.Length);
            string open = "<" + tag + ">", close = "</" + tag + ">";
            string wrapped = text.Substring(0, a) + open + text.Substring(a, b - a) + close + text.Substring(b);
            m_Gesture.Run(() => m_Source.value = wrapped, true);
            m_Source.Focus();
            m_Source.SelectRange(a + open.Length, b + open.Length);
            m_Dirty = true;
            m_PinStart = -1;
            Refresh();
        }
    }

    /// <summary>The key that opens the wrap list. It can be rebound under Edit, Shortcuts.</summary>
    internal static class LetteraTagShortcuts
    {
        [Shortcut("Lettera/Wrap In Tag", KeyCode.Period, ShortcutModifiers.Action)]
        static void WrapInTag() => LtTagPanel.Focused?.Shortcut();
    }
}

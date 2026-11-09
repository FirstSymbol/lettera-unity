using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace FirstSymbol.Lettera.Editor
{
    /// <summary>Finds words typed in the wrong keyboard layout: ищдв is Bold, and цфму is Wave.</summary>
    internal static class LtSearchText
    {
        const string Latin = "`qwertyuiop[]asdfghjkl;'zxcvbnm,./";
        const string Cyrillic = "ёйцукенгшщзхъфывапролджэячсмитьбю.";

        /// <summary>The same keys pressed on the other layout.</summary>
        public static string OtherLayout(string text)
        {
            if (string.IsNullOrEmpty(text)) return text ?? "";
            var result = new StringBuilder(text.Length);
            foreach (char raw in text)
            {
                char c = char.ToLowerInvariant(raw);
                int at = Cyrillic.IndexOf(c);
                if (at >= 0)
                {
                    result.Append(Latin[at]);
                    continue;
                }
                at = Latin.IndexOf(c);
                result.Append(at >= 0 ? Cyrillic[at] : c);
            }
            return result.ToString();
        }

        /// <summary>How well a query fits a text: 0 is no match, and higher is better. Every word of the query must be found, as written or as the other layout.</summary>
        public static int Score(string query, string text)
        {
            if (string.IsNullOrWhiteSpace(query)) return 1;
            if (string.IsNullOrEmpty(text)) return 0;
            int total = 0;
            foreach (string word in query.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
            {
                int best = Math.Max(ScoreWord(word, text), ScoreWord(OtherLayout(word), text));
                if (best == 0) return 0;
                total += best;
            }
            return total;
        }

        static int ScoreWord(string word, string text)
        {
            int at = text.IndexOf(word, StringComparison.OrdinalIgnoreCase);
            if (at == 0) return 100 + word.Length;
            if (at > 0)
            {
                bool startsWord = !char.IsLetterOrDigit(text[at - 1]) || (char.IsUpper(text[at]) && char.IsLower(text[at - 1]));
                return (startsWord ? 60 : 30) + word.Length;
            }
            // In order, with gaps: "bgc" finds "Background Color", and is the weakest kind of match.
            int from = 0, matched = 0;
            foreach (char c in word)
            {
                int next = text.IndexOf(c.ToString(), from, StringComparison.OrdinalIgnoreCase);
                if (next < 0) return 0;
                from = next + 1;
                matched++;
            }
            return word.Length >= 2 ? 5 + matched : 0;
        }
    }

    /// <summary>Reads what a person types into a number: 12*1.5, or +10 to add to what is there.</summary>
    internal static class LtExpression
    {
        /// <summary>The value, or false when this is no sum.</summary>
        public static bool TryEvaluate(string text, double current, out double value)
        {
            value = current;
            if (string.IsNullOrWhiteSpace(text)) return false;
            text = text.Trim().Replace(',', '.');
            if (text.Length > 64) return false;
            // A leading operator acts on what is there. A leading minus is a negative number.
            if (text[0] == '+' || text[0] == '*' || text[0] == '/') text = current.ToString("R", CultureInfo.InvariantCulture) + text;
            int at = 0;
            if (!Sum(text, ref at, out value)) return false;
            Skip(text, ref at);
            return at == text.Length && !double.IsNaN(value) && !double.IsInfinity(value);
        }

        static void Skip(string s, ref int at)
        {
            while (at < s.Length && s[at] == ' ') at++;
        }

        static bool Sum(string s, ref int at, out double value)
        {
            if (!Product(s, ref at, out value)) return false;
            while (true)
            {
                Skip(s, ref at);
                if (at >= s.Length || (s[at] != '+' && s[at] != '-')) return true;
                char op = s[at++];
                if (!Product(s, ref at, out double right)) return false;
                value = op == '+' ? value + right : value - right;
            }
        }

        static bool Product(string s, ref int at, out double value)
        {
            if (!Atom(s, ref at, out value, 0)) return false;
            while (true)
            {
                Skip(s, ref at);
                if (at >= s.Length || (s[at] != '*' && s[at] != '/')) return true;
                char op = s[at++];
                if (!Atom(s, ref at, out double right, 0)) return false;
                value = op == '*' ? value * right : value / right;
            }
        }

        static bool Atom(string s, ref int at, out double value, int depth)
        {
            value = 0;
            Skip(s, ref at);
            if (at >= s.Length || depth > 16) return false;
            if (s[at] == '-')
            {
                at++;
                bool ok = Atom(s, ref at, out value, depth + 1);
                value = -value;
                return ok;
            }
            if (s[at] == '(')
            {
                at++;
                if (!Sum(s, ref at, out value)) return false;
                Skip(s, ref at);
                if (at >= s.Length || s[at] != ')') return false;
                at++;
                return true;
            }
            int start = at;
            while (at < s.Length && (char.IsDigit(s[at]) || s[at] == '.')) at++;
            return at > start && double.TryParse(s.Substring(start, at - start), NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        }
    }

    /// <summary>Which rows of a list are selected, and what each act leaves. Pure, so that it can be tested by the table.</summary>
    internal sealed class LtListSelection
    {
        readonly List<int> m_Selected = new List<int>();
        int m_Anchor = -1, m_Head = -1;

        public IReadOnlyList<int> Selected => m_Selected;
        public int Anchor => m_Anchor;
        public bool IsSelected(int row) => m_Selected.Contains(row);

        public void Clear()
        {
            m_Selected.Clear();
            m_Anchor = m_Head = -1;
        }

        /// <summary>A plain click takes the row alone.</summary>
        public void Click(int row)
        {
            m_Selected.Clear();
            m_Selected.Add(row);
            m_Anchor = m_Head = row;
        }

        /// <summary>Control adds or takes away, and moves the anchor.</summary>
        public void ControlClick(int row)
        {
            if (!m_Selected.Remove(row)) m_Selected.Add(row);
            m_Selected.Sort();
            m_Anchor = m_Head = row;
        }

        /// <summary>Shift takes everything from the anchor, which stays.</summary>
        public void ShiftClick(int row)
        {
            if (m_Anchor < 0)
            {
                Click(row);
                return;
            }
            m_Selected.Clear();
            int from = Math.Min(m_Anchor, row), to = Math.Max(m_Anchor, row);
            for (int i = from; i <= to; i++) m_Selected.Add(i);
            m_Head = row;
        }

        /// <summary>Arrow keys: the next row, or with Shift a longer range.</summary>
        public void Step(int delta, int count, bool extend)
        {
            if (count == 0) return;
            if (m_Selected.Count == 0)
            {
                Click(delta > 0 ? 0 : count - 1);
                return;
            }
            int next = Mathf.Clamp(m_Head + delta, 0, count - 1);
            if (extend) ShiftClick(next);
            else Click(next);
        }

        /// <summary>After rows were removed: the row that took the place of the first of them.</summary>
        public void AfterRemove(int countNow)
        {
            int first = m_Selected.Count > 0 ? m_Selected[0] : -1;
            m_Selected.Clear();
            m_Anchor = -1;
            if (first >= 0 && countNow > 0) Click(Math.Min(first, countNow - 1));
        }

        /// <summary>Where each row goes when the selected ones are carried to a gap. Returns the new order as old indices.</summary>
        public int[] OrderAfterDrop(int count, int gap)
        {
            var moved = new List<int>(m_Selected);
            moved.Sort();
            var rest = new List<int>();
            int insert = 0;
            for (int i = 0; i < count; i++)
            {
                if (moved.Contains(i)) continue;
                if (i < gap) insert++;
                rest.Add(i);
            }
            rest.InsertRange(insert, moved);
            m_Selected.Clear();
            for (int i = 0; i < moved.Count; i++) m_Selected.Add(insert + i);
            m_Anchor = m_Selected.Count > 0 ? m_Selected[0] : -1;
            return rest.ToArray();
        }

        /// <summary>Alt with an arrow: selected rows move one place, and stop together at the ends.</summary>
        public int[] OrderAfterNudge(int count, int delta)
        {
            if (m_Selected.Count == 0) return null;
            var sorted = new List<int>(m_Selected);
            sorted.Sort();
            if (delta < 0 && sorted[0] == 0) return null;
            if (delta > 0 && sorted[sorted.Count - 1] == count - 1) return null;
            return OrderAfterDrop(count, delta < 0 ? sorted[0] - 1 : sorted[sorted.Count - 1] + 2);
        }
    }

    /// <summary>A filter across a page of sections: rows that do not fit hide by a class, and a section with none left closes.</summary>
    internal static class LtFilter
    {
        public static void Apply(IEnumerable<LtSection> sections, string query)
        {
            bool filtering = !string.IsNullOrWhiteSpace(query);
            foreach (LtSection section in sections)
            {
                bool titleFits = filtering && LtSearchText.Score(query, section.Title) > 0;
                bool any = false;
                foreach (VisualElement row in section.Content.Query(className: "lt-row").ToList())
                {
                    bool fits = !filtering || titleFits || RowFits(row, query);
                    row.EnableInClassList("lt-filtered", !fits);
                    any |= fits;
                }
                foreach (VisualElement row in section.Content.Query(className: "lt-list__row").ToList())
                {
                    bool fits = !filtering || titleFits || WordsFit(row, query);
                    row.EnableInClassList("lt-filtered", !fits);
                    any |= fits;
                }
                foreach (VisualElement tool in section.Content.Query(className: "lt-tool").ToList())
                {
                    bool fits = !filtering || titleFits || (tool is Label label && LtSearchText.Score(query, label.text) >= 30) || (!string.IsNullOrEmpty(tool.tooltip) && LtSearchText.Score(query, tool.tooltip) >= 60);
                    tool.EnableInClassList("lt-filtered", !fits);
                    any |= fits;
                }
                foreach (VisualElement source in section.Content.Query(className: "lt-source-box").ToList())
                    source.EnableInClassList("lt-filtered", filtering && !titleFits);
                section.EnableInClassList("lt-filtered", filtering && !any && !titleFits);
                section.ForceOpen(filtering && (any || titleFits));
            }
        }

        static bool RowFits(VisualElement row, string query)
        {
            if (!string.IsNullOrEmpty(row.tooltip) && LtSearchText.Score(query, row.tooltip) >= 60) return true;
            foreach (Label label in row.Query<Label>().ToList())
                if ((label.ClassListContains("lt-row__label") || label.ClassListContains("lt-inline__label")) && LtSearchText.Score(query, label.text) > 0)
                    return true;
            return false;
        }

        static bool WordsFit(VisualElement row, string query)
        {
            foreach (Label label in row.Query<Label>().ToList())
                if ((label.ClassListContains("lt-style__tag") || label.ClassListContains("lt-style__type") || label.ClassListContains("lt-row__label") || label.ClassListContains("lt-object__name")) && LtSearchText.Score(query, label.text) > 0)
                    return true;
            return false;
        }
    }

    /// <summary>A card with a header that collapses, which says in a word what it holds while closed. Open or closed is kept per user.</summary>
    internal sealed class LtSection : VisualElement
    {
        const string Prefix = "Lettera.Section.";
        readonly string m_Key;
        readonly Func<string> m_Summary;
        readonly Label m_SummaryLabel, m_Chevron;

        public VisualElement Content { get; }
        public string Key => m_Key;
        public string Title { get; }

        public bool Open
        {
            get => Content.style.display != DisplayStyle.None;
            set
            {
                Content.style.display = value ? DisplayStyle.Flex : DisplayStyle.None;
                m_Chevron.text = value ? "▾" : "▸";
                LtPrefs.SetBool(Prefix + m_Key, value);
                RefreshSummary();
            }
        }

        public LtSection(string key, string title, Func<string> summary = null, bool openByDefault = true)
        {
            m_Key = key;
            m_Summary = summary;
            Title = title;
            AddToClassList("lt-card");
            AddToClassList("lt-section");
            name = "lt-section-" + key;
            VisualElement header = LtUI.El("lt-section__header");
            m_Chevron = LtUI.Text("▾", "lt-section__chevron");
            m_SummaryLabel = LtUI.Text("", "lt-section__summary");
            header.Add(m_Chevron);
            header.Add(LtUI.Text(title.ToUpperInvariant(), "lt-card__title lt-section__title"));
            header.Add(m_SummaryLabel);
            Add(header);
            Content = LtUI.El("lt-section__content");
            Add(Content);
            // On release, so that a drag across it toggles nothing.
            bool pressed = false;
            header.RegisterCallback<PointerDownEvent>(_ => pressed = true);
            header.RegisterCallback<PointerLeaveEvent>(_ => pressed = false);
            header.RegisterCallback<PointerUpEvent>(e =>
            {
                if (!pressed) return;
                pressed = false;
                bool next = !Open;
                Open = next;
                if (e.altKey)
                    foreach (LtSection nested in this.Query<LtSection>().ToList())
                        if (nested != this)
                            nested.Open = next;
            });
            Open = LtPrefs.GetBool(Prefix + key, openByDefault);
        }

        /// <summary>Shows it for a filter's sake, without changing what is kept for the user.</summary>
        public void ForceOpen(bool force)
        {
            if (force) Content.style.display = DisplayStyle.Flex;
            else Content.style.display = LtPrefs.GetBool(Prefix + m_Key, true) ? DisplayStyle.Flex : DisplayStyle.None;
            m_Chevron.text = Content.style.display == DisplayStyle.Flex ? "▾" : "▸";
        }

        public LtSection With(params VisualElement[] children)
        {
            foreach (VisualElement child in children) Content.Add(child);
            return this;
        }

        public void RefreshSummary()
        {
            if (m_SummaryLabel == null) return;
            bool closed = Content.style.display == DisplayStyle.None;
            m_SummaryLabel.text = closed && m_Summary != null ? m_Summary() : "";
        }
    }

    /// <summary>A number that commits on Enter or on leaving the field, and scrubs by a drag on its label. A sum works: 12*1.5, or +10.</summary>
    internal sealed class LtNumber : VisualElement
    {
        readonly TextField m_Field;
        readonly SerializedProperty m_Property;
        readonly Label m_Label;
        readonly float m_Step;
        double m_Min = double.NegativeInfinity, m_Max = double.PositiveInfinity;
        bool m_Scrubbing;
        float m_LastX;
        double m_ScrubValue;
        int m_ScrubGroup;

        /// <summary>How many times a value has been written, for tests.</summary>
        public int Writes { get; private set; }
        public TextField Field => m_Field;

        /// <summary>False when Shift and Alt mean something else here, as they do in a box's sides.</summary>
        public bool ModifiersScale { get; set; } = true;

        /// <summary>After each write, with the keys held at that moment.</summary>
        public event Action<double, EventModifiers> Wrote;

        public LtNumber(string label, SerializedProperty property, string tooltip = null, float step = 0)
        {
            m_Property = property.Copy();
            AddToClassList("lt-row");
            AddToClassList("lt-number");
            this.tooltip = tooltip;
            m_Step = step > 0 ? step : property.propertyType == SerializedPropertyType.Integer ? 1 : 0.1f;
            m_Label = LtUI.Text(label ?? "", "lt-row__label lt-number__label");
            if (!string.IsNullOrEmpty(label)) Add(m_Label);
            m_Field = new TextField { isDelayed = false };
            m_Field.AddToClassList("lt-row__field");
            m_Field.AddToClassList("lt-text-input");
            Add(m_Field);

            Refresh(m_Property);
            this.TrackPropertyValue(m_Property, Refresh);
            LtUI.Mark(this, m_Label, m_Property);

            m_Field.RegisterCallback<KeyDownEvent>(e =>
            {
                if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter)
                {
                    Commit(e.modifiers);
                    e.StopPropagation();
                }
                else if (e.keyCode == KeyCode.Escape)
                {
                    Refresh(m_Property);
                    e.StopPropagation();
                }
            }, TrickleDown.TrickleDown);
            m_Field.RegisterCallback<FocusOutEvent>(_ => Commit());
            m_Field.RegisterCallback<WheelEvent>(e =>
            {
                if (!e.ctrlKey) return;
                Write(Read() - Mathf.Sign(e.delta.y) * Scale(e.modifiers) * m_Step, e.modifiers);
                e.StopPropagation();
            });

            m_Label.RegisterCallback<PointerDownEvent>(e =>
            {
                if (e.button != 0) return;
                BeginScrub();
                m_LastX = e.position.x;
                m_Label.CapturePointer(e.pointerId);
            });
            m_Label.RegisterCallback<PointerMoveEvent>(e =>
            {
                if (!m_Scrubbing) return;
                float delta = e.position.x - m_LastX;
                if (Mathf.Abs(delta) < 1) return;
                m_LastX = e.position.x;
                ScrubBy(delta, e.modifiers);
            });
            m_Label.RegisterCallback<PointerUpEvent>(e =>
            {
                if (!m_Scrubbing) return;
                m_Label.ReleasePointer(e.pointerId);
                EndScrub();
            });
        }

        float Scale(EventModifiers keys)
        {
            if (!ModifiersScale) return 1;
            return (keys & EventModifiers.Shift) != 0 ? 10 : (keys & EventModifiers.Alt) != 0 ? 0.1f : 1;
        }

        internal void BeginScrub()
        {
            m_Scrubbing = true;
            m_ScrubValue = Read();
            Undo.IncrementCurrentGroup();
            m_ScrubGroup = Undo.GetCurrentGroup();
        }

        internal void ScrubBy(float pixels, EventModifiers keys)
        {
            m_ScrubValue += pixels * Scale(keys) * m_Step;
            m_ScrubValue = Math.Min(m_Max, Math.Max(m_Min, m_ScrubValue));
            Write(m_ScrubValue, keys);
        }

        internal void EndScrub()
        {
            m_Scrubbing = false;
            // However many steps a drag took, it is one undo.
            Undo.CollapseUndoOperations(m_ScrubGroup);
        }

        /// <summary>Values outside this go to its edge.</summary>
        public LtNumber Limit(double min, double max)
        {
            m_Min = min;
            m_Max = max;
            return this;
        }

        double Read()
        {
            m_Property.serializedObject.UpdateIfRequiredOrScript();
            return m_Property.propertyType == SerializedPropertyType.Integer ? m_Property.longValue : m_Property.floatValue;
        }

        void Refresh(SerializedProperty property)
        {
            if (m_Property.hasMultipleDifferentValues)
            {
                m_Field.SetValueWithoutNotify("—");
                return;
            }
            m_Field.SetValueWithoutNotify(m_Property.propertyType == SerializedPropertyType.Integer
                ? m_Property.longValue.ToString(CultureInfo.InvariantCulture)
                : ((double)m_Property.floatValue).ToString("0.####", CultureInfo.InvariantCulture));
        }

        /// <summary>What Enter does.</summary>
        internal void Commit(EventModifiers keys = EventModifiers.None)
        {
            string typed = m_Field.value;
            if (typed == "—") return;
            double current = Read();
            if (!LtExpression.TryEvaluate(typed, current, out double value))
            {
                Refresh(m_Property);
                return;
            }
            if (m_Property.hasMultipleDifferentValues || Math.Abs(value - current) > 1e-9) Write(value, keys);
            else Refresh(m_Property);
        }

        void Write(double value, EventModifiers keys)
        {
            value = Math.Min(m_Max, Math.Max(m_Min, value));
            m_Property.serializedObject.Update();
            if (m_Property.propertyType == SerializedPropertyType.Integer) m_Property.longValue = (long)Math.Round(value);
            else m_Property.floatValue = (float)value;
            m_Property.serializedObject.ApplyModifiedProperties();
            Writes++;
            Refresh(m_Property);
            Wrote?.Invoke(value, keys);
        }
    }

    /// <summary>A number with its unit: px, em or %. Held as text, as a tag's value is, so that what is no length stays as written.</summary>
    internal sealed class LtUnit : VisualElement
    {
        static readonly string[] s_Units = { "px", "em", "%" };
        readonly SerializedProperty m_Property;
        readonly TextField m_Field;
        readonly Func<float> m_EmPixels;
        readonly Label[] m_Items;
        int m_Unit = 1;

        public TextField Field => m_Field;
        public int Writes { get; private set; }

        public LtUnit(string label, SerializedProperty property, string tooltip = null, Func<float> emPixels = null)
        {
            m_Property = property.Copy();
            m_EmPixels = emPixels;
            AddToClassList("lt-row");
            AddToClassList("lt-unit");
            this.tooltip = tooltip;
            Label caption = LtUI.Text(label ?? "", "lt-row__label");
            if (!string.IsNullOrEmpty(label)) Add(caption);
            m_Field = new TextField();
            m_Field.AddToClassList("lt-text-input");
            m_Field.AddToClassList("lt-unit__value");
            Add(m_Field);
            VisualElement units = LtUI.Choice(s_Units, () => m_Unit, Pick, out m_Items);
            units.AddToClassList("lt-unit__units");
            Add(units);
            Refresh(m_Property);
            this.TrackPropertyValue(m_Property, Refresh);
            LtUI.Mark(this, caption, m_Property);
            m_Field.RegisterCallback<KeyDownEvent>(e =>
            {
                if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter)
                {
                    Commit();
                    e.StopPropagation();
                }
                else if (e.keyCode == KeyCode.Escape)
                {
                    Refresh(m_Property);
                    e.StopPropagation();
                }
            }, TrickleDown.TrickleDown);
            m_Field.RegisterCallback<FocusOutEvent>(_ => Commit());
        }

        /// <summary>A value in another unit: 0 px, 1 em, 2 %. Pixels need the size of an em: without it the number stays, and only the unit changes.</summary>
        public static double Convert(double value, int from, int to, float emPixels)
        {
            if (from == to) return value;
            if ((from == 0 || to == 0) && emPixels <= 0) return value;
            double ems = from == 2 ? value / 100 : from == 1 ? value : value / emPixels;
            return to == 2 ? ems * 100 : to == 1 ? ems : ems * emPixels;
        }

        /// <summary>What a click on a unit does.</summary>
        public void Pick(int unit)
        {
            if (!TryRead(m_Property.stringValue, out double value, out int from))
            {
                m_Unit = unit;
                Write(m_Field.value.Trim() + s_Units[unit]);
                return;
            }
            Write(Format(Convert(value, from, unit, m_EmPixels != null ? m_EmPixels() : 0), unit));
        }

        static string Format(double value, int unit) => value.ToString("0.####", CultureInfo.InvariantCulture) + s_Units[unit];

        static bool TryRead(string token, out double value, out int unit)
        {
            value = 0;
            unit = 1;
            if (!LetteraLength.TryParse(token, out LetteraLength length)) return false;
            value = length.Value;
            unit = length.Kind == LetteraLength.Unit.Pixels ? 0 : length.Kind == LetteraLength.Unit.Percent ? 2 : 1;
            return true;
        }

        void Refresh(SerializedProperty _)
        {
            if (m_Property.hasMultipleDifferentValues)
            {
                m_Field.SetValueWithoutNotify("—");
                return;
            }
            if (TryRead(m_Property.stringValue, out double value, out int unit))
            {
                m_Unit = unit;
                m_Field.SetValueWithoutNotify(value.ToString("0.####", CultureInfo.InvariantCulture));
            }
            else
            {
                m_Field.SetValueWithoutNotify(m_Property.stringValue);
            }
            for (int i = 0; i < m_Items.Length; i++) m_Items[i].EnableInClassList("lt-seg__item--on", i == m_Unit);
        }

        internal void Commit()
        {
            string typed = m_Field.value.Trim();
            if (typed == "—") return;
            TryRead(m_Property.stringValue, out double current, out int _);
            string next = LtExpression.TryEvaluate(typed, current, out double value) ? Format(value, m_Unit) : typed;
            if (next != m_Property.stringValue) Write(next);
            else Refresh(m_Property);
        }

        void Write(string token)
        {
            m_Property.serializedObject.Update();
            m_Property.stringValue = token;
            m_Property.serializedObject.ApplyModifiedProperties();
            Writes++;
            Refresh(m_Property);
        }
    }

    /// <summary>A searchable list to choose from: arrows, Enter and a click, in either keyboard layout.</summary>
    internal sealed class LtChoiceList : VisualElement
    {
        readonly List<(string name, string hint, object value)> m_All;
        readonly List<(string name, string hint, object value)> m_Shown = new List<(string, string, object)>();
        readonly Action<object> m_Chosen;
        readonly ScrollView m_List = new ScrollView();
        readonly Func<object, string> m_GroupOf;
        readonly List<VisualElement> m_Rows = new List<VisualElement>();
        int m_At;

        public LtSearch Search { get; }
        public IReadOnlyList<(string name, string hint, object value)> Shown => m_Shown;

        public LtChoiceList(List<(string name, string hint, object value)> items, Action<object> chosen, int current = -1, bool searchable = true, Func<object, string> groupOf = null)
        {
            m_All = items;
            m_GroupOf = groupOf;
            m_Chosen = chosen;
            AddToClassList("lt-choices");
            style.flexGrow = 1;
            if (searchable)
            {
                Search = new LtSearch("Find");
                Search.Changed += Refresh;
                Add(Search);
                Search.Field.schedule.Execute(() => Search.Field.Focus());
            }
            m_List.style.flexGrow = 1;
            Add(m_List);
            RegisterCallback<KeyDownEvent>(OnKey, TrickleDown.TrickleDown);
            Refresh("");
            if (current >= 0 && current < m_Shown.Count)
            {
                m_At = current;
                Paint();
            }
        }

        void OnKey(KeyDownEvent e)
        {
            switch (e.keyCode)
            {
                case KeyCode.UpArrow:
                    m_At = Mathf.Max(0, m_At - 1);
                    Paint();
                    break;
                case KeyCode.DownArrow:
                    m_At = Mathf.Min(m_Shown.Count - 1, m_At + 1);
                    Paint();
                    break;
                case KeyCode.Return:
                case KeyCode.KeypadEnter:
                    if (m_At >= 0 && m_At < m_Shown.Count) m_Chosen(m_Shown[m_At].value);
                    break;
                default:
                    return;
            }
            e.StopPropagation();
        }

        internal void Refresh(string query)
        {
            m_Shown.Clear();
            m_List.Clear();
            m_Rows.Clear();
            foreach ((string name, string hint, object value) item in m_All)
                if (LtSearchText.Score(query, item.name) > 0 || (!string.IsNullOrEmpty(item.hint) && LtSearchText.Score(query, item.hint) >= 30))
                    m_Shown.Add(item);
            string group = null;
            foreach ((string name, string hint, object value) item in m_Shown)
            {
                object chosen = item.value;
                if (m_GroupOf != null && chosen != null && m_GroupOf(chosen) != group)
                {
                    group = m_GroupOf(chosen);
                    m_List.Add(LtUI.Text(group, "lt-choices__group"));
                }
                VisualElement row = LtUI.El("lt-palette__row", LtUI.Text(item.name, "lt-palette__name"), LtUI.Text(item.hint ?? "", "lt-palette__group"));
                row.RegisterCallback<ClickEvent>(_ => m_Chosen(chosen));
                m_List.Add(row);
                m_Rows.Add(row);
            }
            if (m_Shown.Count == 0) m_List.Add(LtUI.Text("Nothing by that name.", "lt-hint"));
            m_At = 0;
            Paint();
        }

        void Paint()
        {
            for (int i = 0; i < m_Rows.Count; i++) m_Rows[i].EnableInClassList("lt-palette__row--on", i == m_At);
            if (m_List.panel != null && m_At >= 0 && m_At < m_Rows.Count) m_List.ScrollTo(m_Rows[m_At]);
        }
    }

    /// <summary>A row that shows a value in words and chooses it from a list with a search. For what is no enum: a type, an effect's parameter, a family.</summary>
    internal sealed class LtPick : VisualElement
    {
        readonly Func<string> m_Shown;
        readonly Func<List<(string name, string hint, object value)>> m_Items;
        readonly Action<object> m_Chosen;
        readonly Func<object, string> m_GroupOf;
        readonly Label m_Value;

        public LtPick(string label, Func<string> shown, Func<List<(string name, string hint, object value)>> items, Action<object> chosen, string tooltip = null, Func<object, string> groupOf = null)
        {
            m_Shown = shown;
            m_Items = items;
            m_Chosen = chosen;
            m_GroupOf = groupOf;
            AddToClassList("lt-row");
            AddToClassList("lt-pick");
            this.tooltip = tooltip;
            if (!string.IsNullOrEmpty(label)) Add(LtUI.Text(label, "lt-row__label"));
            m_Value = LtUI.Clickable("", "lt-row__field lt-enum__value", "Choose", Open);
            Add(m_Value);
            Refresh();
        }

        public string Shown => m_Value.text;

        public void Refresh() => m_Value.text = m_Shown() ?? "";

        /// <summary>What a choice does.</summary>
        public void Choose(object value)
        {
            m_Chosen(value);
            Refresh();
        }

        void Open()
        {
            List<(string name, string hint, object value)> items = m_Items();
            LtPopup.Show(m_Value, new Vector2(Mathf.Max(m_Value.worldBound.width, 300), Mathf.Min(70 + 24 * items.Count, 360)), () =>
                new LtChoiceList(items, chosen =>
                {
                    Choose(chosen);
                    LtPopup.CloseCurrent();
                }, -1, items.Count > 8, m_GroupOf));
        }
    }

    /// <summary>A row that opens to show more, built on first opening, so that a long list stays short and quick.</summary>
    internal sealed class LtFold : VisualElement
    {
        readonly Func<VisualElement> m_Build;
        readonly VisualElement m_Body;
        readonly Label m_Chevron;
        bool m_Built;

        public LtFold(string title, Func<VisualElement> build, bool open = false, string tooltip = null)
        {
            m_Build = build;
            AddToClassList("lt-fold");
            this.tooltip = tooltip;
            m_Chevron = LtUI.Text(open ? "▾" : "▸", "lt-section__chevron");
            VisualElement head = LtUI.El("lt-fold__head", m_Chevron, LtUI.Text(title, "lt-row__label lt-fold__title"));
            head.RegisterCallback<ClickEvent>(e => Open = !Open);
            Add(head);
            m_Body = LtUI.El("lt-fold__body");
            Add(m_Body);
            m_Body.style.display = DisplayStyle.None;
            if (open) Open = true;
        }

        public bool Open
        {
            get => m_Body.style.display != DisplayStyle.None;
            set
            {
                if (value && !m_Built)
                {
                    m_Built = true;
                    m_Body.Add(m_Build());
                }
                m_Body.style.display = value ? DisplayStyle.Flex : DisplayStyle.None;
                m_Chevron.text = value ? "▾" : "▸";
            }
        }
    }

    /// <summary>Up to four choices as segments, more as a list with a search. The value reads in plain words, with the accent when it is not the first.</summary>
    internal sealed class LtEnum : VisualElement
    {
        readonly SerializedProperty m_Property;
        readonly string[] m_Names;
        readonly Label[] m_Items;
        readonly Label m_Value;

        public LtEnum(string label, SerializedProperty property, string tooltip = null, string[] names = null)
        {
            m_Property = property.Copy();
            m_Names = names ?? property.enumDisplayNames;
            AddToClassList("lt-row");
            AddToClassList("lt-enum");
            this.tooltip = tooltip;
            Label caption = LtUI.Text(label ?? "", "lt-row__label");
            if (!string.IsNullOrEmpty(label)) Add(caption);
            if (m_Names.Length <= 4)
            {
                VisualElement seg = LtUI.El("lt-seg lt-row__field");
                m_Items = new Label[m_Names.Length];
                for (int i = 0; i < m_Names.Length; i++)
                {
                    int index = i;
                    m_Items[i] = LtUI.Clickable(m_Names[i], "lt-seg__item", m_Names[i], () => Choose(index));
                    seg.Add(m_Items[i]);
                }
                Add(seg);
            }
            else
            {
                m_Value = LtUI.Clickable("", "lt-row__field lt-enum__value", "Choose", Open);
                Add(m_Value);
            }
            Refresh(m_Property);
            this.TrackPropertyValue(m_Property, Refresh);
            LtUI.Mark(this, caption, m_Property);
        }

        public int Count => m_Names.Length;

        /// <summary>What a click on a choice does.</summary>
        public void Choose(int index)
        {
            m_Property.serializedObject.Update();
            m_Property.enumValueIndex = index;
            m_Property.serializedObject.ApplyModifiedProperties();
            Refresh(m_Property);
        }

        void Open()
        {
            var items = new List<(string, string, object)>();
            for (int i = 0; i < m_Names.Length; i++) items.Add((m_Names[i], null, i));
            LtPopup.Show(m_Value, new Vector2(Mathf.Max(m_Value.worldBound.width, 200), Mathf.Min(44 + 24 * m_Names.Length, 300)), () =>
                new LtChoiceList(items, chosen =>
                {
                    Choose((int)chosen);
                    LtPopup.CloseCurrent();
                }, m_Property.enumValueIndex, m_Names.Length > 8));
        }

        void Refresh(SerializedProperty _)
        {
            bool mixed = m_Property.hasMultipleDifferentValues;
            int index = m_Property.enumValueIndex;
            if (m_Items != null)
                for (int i = 0; i < m_Items.Length; i++)
                    m_Items[i].EnableInClassList("lt-seg__item--on", !mixed && i == index);
            if (m_Value != null)
            {
                m_Value.text = mixed ? "—" : index >= 0 && index < m_Names.Length ? m_Names[index] : "";
                m_Value.EnableInClassList("lt-enum__value--changed", !mixed && index != 0);
            }
        }
    }

    /// <summary>A slot for an object: its type's icon and its name. A click pings it, a drag sets it, the ring picks from a list with a search, and the arrow opens it.</summary>
    internal sealed class LtObject : VisualElement
    {
        readonly SerializedProperty m_Property;
        readonly Type m_Type;
        readonly VisualElement m_Slot;
        readonly Label m_Name;
        readonly VisualElement m_Go;

        public LtObject(string label, SerializedProperty property, Type type = null, string tooltip = null)
        {
            m_Property = property.Copy();
            m_Type = type != null && typeof(UnityEngine.Object).IsAssignableFrom(type) ? type : typeof(UnityEngine.Object);
            AddToClassList("lt-row");
            AddToClassList("lt-object");
            this.tooltip = tooltip;
            Label caption = LtUI.Text(label ?? "", "lt-row__label");
            if (!string.IsNullOrEmpty(label)) Add(caption);
            m_Slot = LtUI.El("lt-row__field lt-object__slot");
            var icon = new Image { image = AssetPreview.GetMiniTypeThumbnail(m_Type) };
            icon.AddToClassList("lt-object__icon");
            m_Slot.Add(icon);
            m_Name = LtUI.Clickable("", "lt-object__name", "Shows it in its window. Drag something here to set it.", Ping);
            m_Slot.Add(m_Name);
            VisualElement pick = LtUI.El("lt-object__button", new LtIcon(LtIcon.Kind.Pick));
            pick.tooltip = "Choose from a list";
            pick.RegisterCallback<ClickEvent>(e =>
            {
                e.StopPropagation();
                OpenPicker();
            });
            m_Go = LtUI.El("lt-object__button", new LtIcon(LtIcon.Kind.GoTo));
            m_Go.tooltip = "Opens it in the Inspector";
            m_Go.RegisterCallback<ClickEvent>(e =>
            {
                e.StopPropagation();
                if (m_Property.objectReferenceValue != null) Selection.activeObject = m_Property.objectReferenceValue;
            });
            m_Slot.Add(pick);
            m_Slot.Add(m_Go);
            Add(m_Slot);
            m_Slot.RegisterCallback<DragUpdatedEvent>(_ =>
            {
                if (Fits(DragAndDrop.objectReferences) != null) DragAndDrop.visualMode = DragAndDropVisualMode.Link;
            });
            m_Slot.RegisterCallback<DragPerformEvent>(_ =>
            {
                UnityEngine.Object fit = Fits(DragAndDrop.objectReferences);
                if (fit == null) return;
                DragAndDrop.AcceptDrag();
                Assign(fit);
            });
            Refresh(m_Property);
            this.TrackPropertyValue(m_Property, Refresh);
            LtUI.Mark(this, caption, m_Property);
        }

        void Ping()
        {
            if (m_Property.objectReferenceValue != null) EditorGUIUtility.PingObject(m_Property.objectReferenceValue);
        }

        internal string Shown => m_Name.text;

        /// <summary>The first that can go in this slot: the object, or a component of that type on it.</summary>
        internal UnityEngine.Object Fits(UnityEngine.Object[] objects)
        {
            foreach (UnityEngine.Object o in objects)
            {
                if (o != null && m_Type.IsInstanceOfType(o)) return o;
                if (o is GameObject go && typeof(Component).IsAssignableFrom(m_Type) && go.GetComponent(m_Type) != null) return go.GetComponent(m_Type);
            }
            return null;
        }

        /// <summary>What a choice or a drop does.</summary>
        public void Assign(UnityEngine.Object value)
        {
            m_Property.serializedObject.Update();
            m_Property.objectReferenceValue = value;
            m_Property.serializedObject.ApplyModifiedProperties();
            Refresh(m_Property);
        }

        void OpenPicker() => LtPopup.Show(m_Slot, new Vector2(Mathf.Max(m_Slot.worldBound.width, 280), 320), () => new LtChoiceList(Candidates(m_Type), chosen =>
        {
            Assign((UnityEngine.Object)chosen);
            LtPopup.CloseCurrent();
        }));

        internal static List<(string name, string hint, object value)> Candidates(Type type)
        {
            var items = new List<(string, string, object)> { ("None", null, null) };
            if (typeof(Component).IsAssignableFrom(type))
            {
                foreach (UnityEngine.Object o in UnityEngine.Object.FindObjectsByType(type, FindObjectsInactive.Include, FindObjectsSortMode.None))
                    items.Add((o.name, "Scene", o));
                return items;
            }
            foreach (string guid in AssetDatabase.FindAssets("t:" + type.Name))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                UnityEngine.Object asset = AssetDatabase.LoadAssetAtPath(path, type);
                if (asset != null) items.Add((asset.name, System.IO.Path.GetDirectoryName(path)?.Replace('\\', '/'), asset));
            }
            return items;
        }

        void Refresh(SerializedProperty _)
        {
            UnityEngine.Object value = m_Property.objectReferenceValue;
            m_Name.text = m_Property.hasMultipleDifferentValues ? "—" : value != null ? value.name : "None";
            m_Name.EnableInClassList("lt-object__name--empty", value == null && !m_Property.hasMultipleDifferentValues);
            m_Go.style.display = value != null ? DisplayStyle.Flex : DisplayStyle.None;
        }
    }

    /// <summary>A colour: the swatch opens Unity's picker with its eyedropper, and hex can be typed or pasted beside it.</summary>
    internal sealed class LtColor : VisualElement
    {
        readonly SerializedProperty m_Property;
        readonly TextField m_Hex;

        public TextField Hex => m_Hex;

        public LtColor(string label, SerializedProperty property, string tooltip = null)
        {
            m_Property = property.Copy();
            AddToClassList("lt-row");
            AddToClassList("lt-color");
            this.tooltip = tooltip;
            Label caption = LtUI.Text(label ?? "", "lt-row__label");
            if (!string.IsNullOrEmpty(label)) Add(caption);
            var swatch = new ColorField { showEyeDropper = true, showAlpha = true };
            swatch.AddToClassList("lt-color__swatch");
            swatch.BindProperty(m_Property);
            Add(swatch);
            m_Hex = new TextField { isDelayed = true };
            m_Hex.AddToClassList("lt-text-input");
            m_Hex.AddToClassList("lt-color__hex");
            m_Hex.RegisterValueChangedCallback(e => SetHex(e.newValue));
            Add(m_Hex);
            Refresh(m_Property);
            this.TrackPropertyValue(m_Property, Refresh);
            LtUI.Mark(this, caption, m_Property);
        }

        internal void SetHex(string typed)
        {
            string text = (typed ?? "").Trim();
            if (text.Length == 0 || text == "—") return;
            if (text[0] != '#') text = "#" + text;
            if (!ColorUtility.TryParseHtmlString(text, out Color color))
            {
                Refresh(m_Property);
                return;
            }
            m_Property.serializedObject.Update();
            m_Property.colorValue = color;
            m_Property.serializedObject.ApplyModifiedProperties();
            Refresh(m_Property);
        }

        void Refresh(SerializedProperty _)
        {
            Color color = m_Property.colorValue;
            m_Hex.SetValueWithoutNotify(m_Property.hasMultipleDifferentValues ? "—" : "#" + (color.a < 1 ? ColorUtility.ToHtmlStringRGBA(color) : ColorUtility.ToHtmlStringRGB(color)));
        }
    }

    /// <summary>Shows what is wrong, and mends it with a click where a click can. It goes when it is mended.</summary>
    internal sealed class LtFix : VisualElement
    {
        readonly Label m_Text;
        readonly Func<bool> m_Problem;

        public LtFix(string message, Func<bool> problem, string fixLabel = null, Action fix = null, string tooltip = null)
        {
            m_Problem = problem;
            AddToClassList("lt-fix");
            this.tooltip = tooltip;
            m_Text = LtUI.Text(message, "lt-fix__text");
            Add(m_Text);
            if (fix != null)
            {
                Add(LtUI.Clickable(fixLabel ?? "Fix", "lt-fix__button", tooltip, () =>
                {
                    fix();
                    Refresh();
                }));
            }
            schedule.Execute(Refresh).Every(600);
            Refresh();
        }

        public void Refresh() => style.display = m_Problem == null || m_Problem() ? DisplayStyle.Flex : DisplayStyle.None;
    }

    /// <summary>Lines of a label, a value and a dot saying how that is.</summary>
    internal sealed class LtStatus : VisualElement
    {
        public enum State { None, Good, Warn, Bad }

        readonly List<(Label value, VisualElement dot, Func<string> text, Func<State> state)> m_Lines = new List<(Label, VisualElement, Func<string>, Func<State>)>();

        public LtStatus()
        {
            AddToClassList("lt-status");
        }

        public LtStatus Line(string label, Func<string> value, Func<State> state = null)
        {
            VisualElement row = LtUI.El("lt-status__row");
            VisualElement dot = LtUI.El("lt-status__dot");
            row.Add(dot);
            row.Add(LtUI.Text(label, "lt-status__label"));
            Label text = LtUI.Text("", "lt-status__value");
            row.Add(text);
            Add(row);
            m_Lines.Add((text, dot, value, state));
            return this;
        }

        /// <summary>Refresh, for a chain.</summary>
        public LtStatus RefreshNow()
        {
            Refresh();
            return this;
        }

        public void Refresh()
        {
            foreach ((Label value, VisualElement dot, Func<string> text, Func<State> state) in m_Lines)
            {
                value.text = text() ?? "";
                value.tooltip = value.text;
                State now = state != null ? state() : State.None;
                dot.EnableInClassList("lt-status__dot--good", now == State.Good);
                dot.EnableInClassList("lt-status__dot--warn", now == State.Warn);
                dot.EnableInClassList("lt-status__dot--bad", now == State.Bad);
            }
        }
    }

    /// <summary>The one filter box: pickers, the palette and the inspector's filter.</summary>
    internal sealed class LtSearch : VisualElement
    {
        readonly TextField m_Field;
        public event Action<string> Changed;
        public string Value => m_Field.value;
        public TextField Field => m_Field;

        public LtSearch(string placeholder = "Filter")
        {
            AddToClassList("lt-search");
            m_Field = new TextField();
            m_Field.AddToClassList("lt-text-input");
            m_Field.textEdition.placeholder = placeholder;
            m_Field.RegisterValueChangedCallback(e => Changed?.Invoke(e.newValue));
            Add(m_Field);
        }

        public void Clear() => m_Field.value = "";
    }

    /// <summary>One way to float something: on the side with more room, of a steady size, and gone with Esc or a click elsewhere.</summary>
    internal sealed class LtPopup : PopupWindowContent
    {
        static LtPopup s_Current;

        /// <summary>Takes what would open, for a run with no person to close a window.</summary>
        internal static Action<VisualElement, Vector2, Func<VisualElement>> ShowOverride;
        readonly Func<VisualElement> m_Build;
        readonly Vector2 m_Size;

        /// <summary>Closes whichever is open.</summary>
        public static void CloseCurrent()
        {
            if (s_Current != null && s_Current.editorWindow != null) s_Current.editorWindow.Close();
            s_Current = null;
        }

        LtPopup(Func<VisualElement> build, Vector2 size)
        {
            m_Build = build;
            m_Size = size;
        }

        /// <summary>Opens beside an element, built when it opens.</summary>
        public static void Show(VisualElement anchor, Vector2 size, Func<VisualElement> build)
        {
            if (ShowOverride != null)
            {
                ShowOverride(anchor, size, build);
                return;
            }
            s_Current = new LtPopup(build, size);
            UnityEditor.PopupWindow.Show(anchor.worldBound, s_Current);
        }

        public override Vector2 GetWindowSize() => m_Size;

        public override void OnGUI(Rect rect) { }

        public override VisualElement CreateGUI()
        {
            VisualElement root = LtUI.Root();
            root.AddToClassList("lt-popup");
            root.Add(m_Build());
            root.RegisterCallback<KeyDownEvent>(e =>
            {
                if (e.keyCode != KeyCode.Escape) return;
                editorWindow.Close();
                e.StopPropagation();
            }, TrickleDown.TrickleDown);
            return root;
        }

        public void Close() => editorWindow.Close();
    }

    /// <summary>Padding as a box: drag or type each side. Shift links opposite sides and Alt links all, and the row says 4 8 4 8.</summary>
    internal sealed class LtBox : VisualElement
    {
        readonly SerializedProperty m_Property;
        readonly Label m_Summary;

        public LtBox(string label, SerializedProperty rectOffsetOrVector, string tooltip = null)
        {
            m_Property = rectOffsetOrVector.Copy();
            AddToClassList("lt-row");
            this.tooltip = tooltip;
            Label caption = LtUI.Text(label, "lt-row__label");
            Add(caption);
            m_Summary = LtUI.Clickable("", "lt-row__field lt-box__summary", "Opens the four sides.", OpenBox);
            Add(m_Summary);
            Refresh(m_Property);
            this.TrackPropertyValue(m_Property, Refresh);
            LtUI.Mark(this, caption, m_Property);
        }

        // Left, right, top, bottom: as a padding's sides are stored here, whichever type holds them.
        static SerializedProperty[] Sides(SerializedProperty property)
        {
            SerializedProperty Get(params string[] names)
            {
                foreach (string name in names)
                {
                    SerializedProperty found = property.FindPropertyRelative(name);
                    if (found != null) return found;
                }
                return null;
            }
            return new[] { Get("m_Left", "Left", "x"), Get("m_Top", "Top", "w"), Get("m_Right", "Right", "z"), Get("m_Bottom", "Bottom", "y") };
        }

        internal static string SummaryOf(float left, float top, float right, float bottom)
        {
            string F(float v) => v.ToString("0.##", CultureInfo.InvariantCulture);
            if (left == top && top == right && right == bottom) return F(left);
            return F(top) + " " + F(right) + " " + F(bottom) + " " + F(left);
        }

        static float Read(SerializedProperty side) => side == null ? 0 : side.propertyType == SerializedPropertyType.Integer ? side.intValue : side.floatValue;

        internal string Summary => m_Summary.text;

        void Refresh(SerializedProperty property)
        {
            SerializedProperty[] sides = Sides(m_Property);
            m_Summary.text = m_Property.hasMultipleDifferentValues ? "—" : SummaryOf(Read(sides[0]), Read(sides[1]), Read(sides[2]), Read(sides[3]));
        }

        /// <summary>Which sides change with one, by the keys held. Left 0, top 1, right 2, bottom 3.</summary>
        internal static int[] Linked(int side, EventModifiers keys)
        {
            if ((keys & EventModifiers.Alt) != 0) return new[] { 0, 1, 2, 3 };
            if ((keys & EventModifiers.Shift) != 0) return new[] { side, (side + 2) % 4 };
            return new[] { side };
        }

        /// <summary>The four sides as a box, top over left and right over bottom, with a line on what the keys do.</summary>
        internal VisualElement BuildBox()
        {
            SerializedProperty[] sides = Sides(m_Property);
            VisualElement page = LtUI.El("lt-box");
            page.Add(LtUI.Text("Drag a name or type. Shift links opposite sides, and Alt links all four.", "lt-hint"));
            LtNumber Side(int index, string name)
            {
                var number = new LtNumber(name, sides[index]) { ModifiersScale = false };
                number.AddToClassList("lt-box__side");
                number.Wrote += (value, keys) =>
                {
                    m_Property.serializedObject.Update();
                    foreach (int linked in Linked(index, keys))
                    {
                        if (linked == index) continue;
                        if (sides[linked].propertyType == SerializedPropertyType.Integer) sides[linked].intValue = (int)Math.Round(value);
                        else sides[linked].floatValue = (float)value;
                    }
                    m_Property.serializedObject.ApplyModifiedProperties();
                };
                return number;
            }
            page.Add(LtUI.El("lt-box__line", Side(1, "Top")));
            page.Add(LtUI.El("lt-box__line", Side(0, "Left"), Side(2, "Right")));
            page.Add(LtUI.El("lt-box__line", Side(3, "Bottom")));
            return page;
        }

        void OpenBox() => LtPopup.Show(m_Summary, new Vector2(300, 150), BuildBox);
    }

    /// <summary>
    /// The one list in the house. Rows from an array property: click, Shift and Ctrl select, Delete removes, Ctrl+D copies, Alt with an arrow moves,
    /// a drag from any free place carries the selection, and each act is one undo. Long lists draw thirty rows until asked for more, so that a big one opens quickly.
    /// </summary>
    internal sealed class LtList : VisualElement
    {
        const int FirstRows = 30;
        const string HiddenKey = "Lettera.List.ControlsHidden";
        static event Action s_HiddenChanged;
        readonly SerializedProperty m_Array;
        readonly Func<SerializedProperty, int, VisualElement> m_MakeRow;
        readonly VisualElement m_Rows;
        readonly Label m_Count;
        readonly string m_Empty, m_Title;
        readonly List<VisualElement> m_RowElements = new List<VisualElement>();
        int m_Shown = FirstRows;
        int m_DragFrom = -1, m_DragGap = -1, m_DragPointer = -1;
        float m_DragStartY;
        bool m_Dragging;
        VisualElement m_DropMark;

        public LtListSelection Selection { get; } = new LtListSelection();

        /// <summary>Where a list's own header buttons go, before its Add.</summary>
        public VisualElement HeaderTools { get; } = LtUI.El("lt-list__tools");

        /// <summary>A row as text and back, for Copy and Paste. Without them a row's text is its plain value.</summary>
        public Func<SerializedProperty, string> RowToText { get; set; }
        public Action<string> PasteText { get; set; }

        /// <summary>More lines in a row's right click.</summary>
        public Action<GenericMenu, int> RowMenuExtra { get; set; }

        /// <summary>Grip, remove and the like hidden on every list, kept per user. Alt+R.</summary>
        public static bool ControlsHidden
        {
            get => LtPrefs.GetBool(HiddenKey, false);
            set
            {
                LtPrefs.SetBool(HiddenKey, value);
                s_HiddenChanged?.Invoke();
            }
        }

        /// <summary>Runs after anything changed the list.</summary>
        public event Action Changed;

        public LtList(string title, SerializedProperty array, Func<SerializedProperty, int, VisualElement> makeRow, string emptyText = "Nothing here yet.", Action<LtList> onAdd = null, string addLabel = "+ Add")
        {
            m_Array = array.Copy();
            m_MakeRow = makeRow;
            m_Empty = emptyText;
            m_Title = title;
            AddToClassList("lt-list");
            focusable = true;
            VisualElement header = LtUI.El("lt-list__header");
            if (!string.IsNullOrEmpty(title)) header.Add(LtUI.Text(title, "lt-list__title"));
            m_Count = LtUI.Text("0", "lt-chip");
            header.Add(m_Count);
            header.Add(LtUI.El("lt-list__space"));
            header.Add(HeaderTools);
            header.Add(LtUI.Clickable(addLabel, "lt-add lt-add--small", "Adds a row.", () =>
            {
                if (onAdd != null) onAdd(this);
                else AddDefault();
                OpenEnclosing();
            }));
            header.AddManipulator(new ContextualMenuManipulator(e => e.menu.AppendAction(ControlsHidden ? "Show Row Controls" : "Hide Row Controls", _ => ControlsHidden = !ControlsHidden)));
            Add(header);
            m_Rows = LtUI.El("lt-list__rows");
            Add(m_Rows);
            m_DropMark = LtUI.El("lt-list__drop");
            RegisterCallback<KeyDownEvent>(OnKey);
            RegisterCallback<PointerMoveEvent>(OnDragMove);
            RegisterCallback<PointerUpEvent>(OnDragEnd);
            void PaintHidden() => EnableInClassList("lt-list--bare", ControlsHidden);
            void OnUndo()
            {
                if (m_Array.serializedObject.targetObject != null) Rebuild();
            }
            PaintHidden();
            RegisterCallback<AttachToPanelEvent>(_ =>
            {
                s_HiddenChanged += PaintHidden;
                Undo.undoRedoPerformed += OnUndo;
            });
            RegisterCallback<DetachFromPanelEvent>(_ =>
            {
                s_HiddenChanged -= PaintHidden;
                Undo.undoRedoPerformed -= OnUndo;
            });
            this.TrackPropertyValue(m_Array, _ => { if (m_Array.arraySize != m_RowElements.Count && !Truncated) Rebuild(); });
            Rebuild();
        }

        bool Truncated => m_Array.arraySize > m_Shown;

        // Adding to a closed list opens it, so that what was added is seen.
        internal void OpenEnclosing()
        {
            LtSection section = GetFirstAncestorOfType<LtSection>();
            if (section != null && !section.Open) section.Open = true;
        }

        public void Rebuild()
        {
            m_Array.serializedObject.UpdateIfRequiredOrScript();
            m_Rows.Clear();
            m_RowElements.Clear();
            int count = m_Array.arraySize;
            m_Count.text = count.ToString();
            if (count == 0)
            {
                m_Rows.Add(LtUI.Text(m_Empty, "lt-hint"));
                return;
            }
            for (int i = 0; i < count && i < m_Shown; i++)
            {
                int index = i;
                VisualElement row = LtUI.El("lt-list__row");
                SerializedProperty element = m_Array.GetArrayElementAtIndex(i);
                row.EnableInClassList("lt-list__row--override", element.prefabOverride);
                row.Add(m_MakeRow(element, i));
                row.RegisterCallback<PointerDownEvent>(e => OnRowDown(index, e));
                row.RegisterCallback<ContextClickEvent>(_ => RowMenu(index));
                m_Rows.Add(row);
                m_RowElements.Add(row);
            }
            if (count > m_Shown)
            {
                m_Rows.Add(LtUI.Clickable($"Show all {count}", "lt-add--small", "A long list opens with its first rows, so that it opens quickly.", () =>
                {
                    m_Shown = int.MaxValue;
                    Rebuild();
                }));
            }
            PaintSelection();
        }

        void PaintSelection()
        {
            for (int i = 0; i < m_RowElements.Count; i++) m_RowElements[i].EnableInClassList("lt-list__row--on", Selection.IsSelected(i));
        }

        void OnRowDown(int index, PointerDownEvent e)
        {
            if (e.button != 0) return;
            // A control in the row keeps its own click; only a free place selects and starts a drag.
            if (e.target is VisualElement target && IsControl(target)) return;
            Focus();
            if (e.ctrlKey || e.commandKey) Selection.ControlClick(index);
            else if (e.shiftKey) Selection.ShiftClick(index);
            else if (!Selection.IsSelected(index)) Selection.Click(index);
            PaintSelection();
            m_DragFrom = index;
            m_DragStartY = e.position.y;
            m_DragPointer = e.pointerId;
            m_Dragging = false;
        }

        static bool IsControl(VisualElement element)
        {
            for (VisualElement at = element; at != null && !at.ClassListContains("lt-list__row"); at = at.parent)
                if (at.focusable || at.ClassListContains("lt-switch") || at.ClassListContains("lt-seg") || at.ClassListContains("lt-icon-btn") || at.ClassListContains("lt-add") || at.ClassListContains("lt-object__slot") || at.ClassListContains("lt-box__summary"))
                    return true;
            return false;
        }

        void OnDragMove(PointerMoveEvent e)
        {
            if (m_DragFrom < 0) return;
            if (!m_Dragging)
            {
                if (Mathf.Abs(e.position.y - m_DragStartY) < 5) return;
                m_Dragging = true;
                this.CapturePointer(m_DragPointer);
            }
            m_DragGap = GapAt(e.position.y);
            ShowDrop();
        }

        int GapAt(float y)
        {
            for (int i = 0; i < m_RowElements.Count; i++)
            {
                Rect box = m_RowElements[i].worldBound;
                if (y < box.center.y) return i;
            }
            return m_RowElements.Count;
        }

        void ShowDrop()
        {
            m_DropMark.RemoveFromHierarchy();
            int at = Mathf.Clamp(m_DragGap, 0, m_Rows.childCount);
            m_Rows.Insert(at, m_DropMark);
        }

        void OnDragEnd(PointerUpEvent e)
        {
            int from = m_DragFrom;
            bool dragged = m_Dragging;
            m_DragFrom = -1;
            m_Dragging = false;
            if (this.HasPointerCapture(e.pointerId)) this.ReleasePointer(e.pointerId);
            m_DropMark.RemoveFromHierarchy();
            if (from < 0) return;
            if (!dragged)
            {
                // A press on a row among several selected takes it alone only on release, so that a drag can carry them all.
                if (!e.ctrlKey && !e.commandKey && !e.shiftKey)
                {
                    Selection.Click(from);
                    PaintSelection();
                }
                return;
            }
            Apply(Selection.OrderAfterDrop(m_Array.arraySize, m_DragGap), "Reorder");
        }

        void OnKey(KeyDownEvent e)
        {
            if (e.target is TextField || e.target is TextElement && e.target != this) return;
            int count = m_Array.arraySize;
            bool control = e.ctrlKey || e.commandKey;
            switch (e.keyCode)
            {
                case KeyCode.Delete:
                case KeyCode.Backspace:
                    RemoveSelected();
                    break;
                case KeyCode.D when control:
                    DuplicateSelected();
                    break;
                case KeyCode.C when control:
                    CopySelected();
                    break;
                case KeyCode.V when control:
                    PasteFromClipboard();
                    break;
                case KeyCode.R when e.altKey:
                    ControlsHidden = !ControlsHidden;
                    break;
                case KeyCode.UpArrow when e.altKey:
                    Apply(Selection.OrderAfterNudge(count, -1), "Move Up");
                    break;
                case KeyCode.DownArrow when e.altKey:
                    Apply(Selection.OrderAfterNudge(count, 1), "Move Down");
                    break;
                case KeyCode.UpArrow:
                    Selection.Step(-1, Math.Min(count, m_Shown), e.shiftKey);
                    PaintSelection();
                    break;
                case KeyCode.DownArrow:
                    Selection.Step(1, Math.Min(count, m_Shown), e.shiftKey);
                    PaintSelection();
                    break;
                case KeyCode.Escape:
                    Selection.Clear();
                    PaintSelection();
                    break;
                default:
                    return;
            }
            e.StopPropagation();
        }

        void RowMenu(int index)
        {
            if (!Selection.IsSelected(index))
            {
                Selection.Click(index);
                PaintSelection();
            }
            var menu = new GenericMenu();
            menu.AddItem(new GUIContent("Remove"), false, RemoveSelected);
            menu.AddItem(new GUIContent("Duplicate"), false, DuplicateSelected);
            menu.AddItem(new GUIContent("Copy As Text"), false, CopySelected);
            if (PasteText != null) menu.AddItem(new GUIContent("Paste"), false, PasteFromClipboard);
            RowMenuExtra?.Invoke(menu, index);
            menu.ShowAsContext();
        }

        /// <summary>What the header's Add does when a list has no way of its own.</summary>
        public void AddDefault()
        {
            m_Array.serializedObject.Update();
            m_Array.arraySize++;
            m_Array.serializedObject.ApplyModifiedProperties();
            Selection.Click(m_Array.arraySize - 1);
            AfterChange();
        }

        public void RemoveSelected()
        {
            if (Selection.Selected.Count == 0) return;
            var rows = new List<int>(Selection.Selected);
            rows.Sort();
            m_Array.serializedObject.Update();
            for (int i = rows.Count - 1; i >= 0; i--) m_Array.DeleteArrayElementAtIndex(rows[i]);
            m_Array.serializedObject.ApplyModifiedProperties();
            Selection.AfterRemove(m_Array.arraySize);
            AfterChange();
        }

        public void DuplicateSelected()
        {
            if (Selection.Selected.Count == 0) return;
            var rows = new List<int>(Selection.Selected);
            rows.Sort();
            m_Array.serializedObject.Update();
            for (int i = rows.Count - 1; i >= 0; i--) m_Array.GetArrayElementAtIndex(rows[i]).DuplicateCommand();
            m_Array.serializedObject.ApplyModifiedProperties();
            AfterChange();
        }

        internal void CopySelected()
        {
            var text = new StringBuilder();
            foreach (int row in Selection.Selected)
            {
                SerializedProperty element = m_Array.GetArrayElementAtIndex(row);
                text.Append(RowToText != null ? RowToText(element) : PlainText(element)).Append('\n');
            }
            EditorGUIUtility.systemCopyBuffer = text.ToString();
        }

        void PasteFromClipboard() => PasteText?.Invoke(EditorGUIUtility.systemCopyBuffer);

        static string PlainText(SerializedProperty element)
        {
            switch (element.propertyType)
            {
                case SerializedPropertyType.String: return element.stringValue;
                case SerializedPropertyType.ObjectReference: return element.objectReferenceValue != null ? element.objectReferenceValue.name : "";
                case SerializedPropertyType.ManagedReference: return element.managedReferenceValue != null ? JsonUtility.ToJson(element.managedReferenceValue) : "";
                default: return element.displayName;
            }
        }

        void Apply(int[] order, string undo)
        {
            if (order == null) return;
            m_Array.serializedObject.Update();
            // Each row moved into its place: MoveArrayElement keeps references and managed values whole.
            var at = new List<int>(order.Length);
            for (int i = 0; i < order.Length; i++) at.Add(i);
            for (int target = 0; target < order.Length; target++)
            {
                int now = at.IndexOf(order[target]);
                if (now == target) continue;
                m_Array.MoveArrayElement(now, target);
                int moved = at[now];
                at.RemoveAt(now);
                at.Insert(target, moved);
            }
            m_Array.serializedObject.ApplyModifiedProperties();
            Undo.SetCurrentGroupName(undo);
            AfterChange();
        }

        void AfterChange()
        {
            Rebuild();
            Changed?.Invoke();
        }
    }
}

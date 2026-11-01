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
    public static class LtSearchText
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
    public static class LtExpression
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
    public sealed class LtListSelection
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

    /// <summary>A card with a header that collapses, which says in a word what it holds while closed. Open or closed is kept per user.</summary>
    public sealed class LtSection : VisualElement
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
                EditorPrefs.SetBool(Prefix + m_Key, value);
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
            Open = EditorPrefs.GetBool(Prefix + key, openByDefault);
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
    public sealed class LtNumber : VisualElement
    {
        readonly TextField m_Field;
        readonly SerializedProperty m_Property;
        readonly Label m_Label;
        readonly float m_Step;
        double m_Editing;
        bool m_Scrubbing;
        float m_LastX;
        double m_ScrubValue;

        /// <summary>How many times a typed value has been written, for tests.</summary>
        public int Writes { get; private set; }
        public TextField Field => m_Field;

        public LtNumber(string label, SerializedProperty property, string tooltip = null, float step = 0)
        {
            m_Property = property.Copy();
            AddToClassList("lt-row");
            AddToClassList("lt-number");
            this.tooltip = tooltip;
            m_Step = step > 0 ? step : property.propertyType == SerializedPropertyType.Integer ? 1 : 0.01f;
            m_Label = LtUI.Text(label, "lt-row__label lt-number__label");
            Add(m_Label);
            m_Field = new TextField { isDelayed = false };
            m_Field.AddToClassList("lt-row__field");
            m_Field.AddToClassList("lt-text-input");
            Add(m_Field);

            Refresh(m_Property);
            this.TrackPropertyValue(m_Property, Refresh);

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
            m_Field.RegisterCallback<WheelEvent>(e =>
            {
                if (!e.ctrlKey) return;
                Write(Read() - Mathf.Sign(e.delta.y) * (e.shiftKey ? 10 : e.altKey ? 0.1f : 1) * m_Step);
                e.StopPropagation();
            });

            m_Label.RegisterCallback<PointerDownEvent>(e =>
            {
                if (e.button != 0) return;
                m_Scrubbing = true;
                m_LastX = e.position.x;
                m_ScrubValue = Read();
                m_Label.CapturePointer(e.pointerId);
                Undo.IncrementCurrentGroup();
            });
            m_Label.RegisterCallback<PointerMoveEvent>(e =>
            {
                if (!m_Scrubbing) return;
                float delta = e.position.x - m_LastX;
                if (Mathf.Abs(delta) < 1) return;
                m_LastX = e.position.x;
                m_ScrubValue += delta * (e.shiftKey ? 10 : e.altKey ? 0.1f : 1) * m_Step;
                Write(m_ScrubValue);
            });
            m_Label.RegisterCallback<PointerUpEvent>(e =>
            {
                if (!m_Scrubbing) return;
                m_Scrubbing = false;
                m_Label.ReleasePointer(e.pointerId);
            });
        }

        double Read()
        {
            m_Property.serializedObject.UpdateIfRequiredOrScript();
            return m_Property.propertyType == SerializedPropertyType.Integer ? m_Property.intValue : m_Property.floatValue;
        }

        void Refresh(SerializedProperty property)
        {
            if (m_Property.hasMultipleDifferentValues)
            {
                m_Field.SetValueWithoutNotify("—");
                return;
            }
            m_Field.SetValueWithoutNotify(m_Property.propertyType == SerializedPropertyType.Integer
                ? m_Property.intValue.ToString(CultureInfo.InvariantCulture)
                : ((double)m_Property.floatValue).ToString("0.####", CultureInfo.InvariantCulture));
        }

        void Commit()
        {
            string typed = m_Field.value;
            if (typed == "—") return;
            double current = Read();
            if (!LtExpression.TryEvaluate(typed, current, out double value))
            {
                Refresh(m_Property);
                return;
            }
            if (m_Property.hasMultipleDifferentValues || Math.Abs(value - current) > 1e-9) Write(value);
            else Refresh(m_Property);
        }

        void Write(double value)
        {
            m_Property.serializedObject.Update();
            if (m_Property.propertyType == SerializedPropertyType.Integer) m_Property.intValue = (int)Math.Round(value);
            else m_Property.floatValue = (float)value;
            m_Property.serializedObject.ApplyModifiedProperties();
            Writes++;
            Refresh(m_Property);
        }
    }

    /// <summary>Shows what is wrong, and mends it with a click where a click can. It goes when it is mended.</summary>
    public sealed class LtFix : VisualElement
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
    public sealed class LtStatus : VisualElement
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

        public void Refresh()
        {
            foreach ((Label value, VisualElement dot, Func<string> text, Func<State> state) in m_Lines)
            {
                value.text = text() ?? "";
                State now = state != null ? state() : State.None;
                dot.EnableInClassList("lt-status__dot--good", now == State.Good);
                dot.EnableInClassList("lt-status__dot--warn", now == State.Warn);
                dot.EnableInClassList("lt-status__dot--bad", now == State.Bad);
            }
        }
    }

    /// <summary>The one filter box: pickers, the palette and the inspector's filter.</summary>
    public sealed class LtSearch : VisualElement
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
    public sealed class LtPopup : PopupWindowContent
    {
        readonly Func<VisualElement> m_Build;
        readonly Vector2 m_Size;

        LtPopup(Func<VisualElement> build, Vector2 size)
        {
            m_Build = build;
            m_Size = size;
        }

        /// <summary>Opens beside an element, built when it opens.</summary>
        public static void Show(VisualElement anchor, Vector2 size, Func<VisualElement> build)
        {
            UnityEditor.PopupWindow.Show(anchor.worldBound, new LtPopup(build, size));
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
    public sealed class LtBox : VisualElement
    {
        readonly SerializedProperty m_Property;
        readonly Label m_Summary;

        public LtBox(string label, SerializedProperty rectOffsetOrVector, string tooltip = null)
        {
            m_Property = rectOffsetOrVector.Copy();
            AddToClassList("lt-row");
            this.tooltip = tooltip;
            Add(LtUI.Text(label, "lt-row__label"));
            m_Summary = LtUI.Clickable("", "lt-row__field lt-box__summary", "Opens the four sides.", OpenBox);
            Add(m_Summary);
            Refresh(m_Property);
            this.TrackPropertyValue(m_Property, Refresh);
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
            return new[] { Get("m_Left", "Left", "x"), Get("m_Top", "Top", "y"), Get("m_Right", "Right", "z"), Get("m_Bottom", "Bottom", "w") };
        }

        internal static string SummaryOf(float left, float top, float right, float bottom)
        {
            string F(float v) => v.ToString("0.##", CultureInfo.InvariantCulture);
            if (left == top && top == right && right == bottom) return F(left);
            return F(top) + " " + F(right) + " " + F(bottom) + " " + F(left);
        }

        static float Read(SerializedProperty side) => side == null ? 0 : side.propertyType == SerializedPropertyType.Integer ? side.intValue : side.floatValue;

        void Refresh(SerializedProperty property)
        {
            SerializedProperty[] sides = Sides(m_Property);
            m_Summary.text = SummaryOf(Read(sides[0]), Read(sides[1]), Read(sides[2]), Read(sides[3]));
        }

        void OpenBox()
        {
            LtPopup.Show(m_Summary, new Vector2(240, 150), () =>
            {
                SerializedProperty[] sides = Sides(m_Property);
                VisualElement page = LtUI.El("lt-box");
                page.Add(LtUI.Text("Shift links opposite sides, and Alt links all four.", "lt-hint"));
                string[] names = { "Left", "Top", "Right", "Bottom" };
                for (int i = 0; i < 4; i++)
                {
                    if (sides[i] == null) continue;
                    int index = i;
                    var field = new FloatField { value = Read(sides[i]) };
                    field.RegisterValueChangedCallback(e =>
                    {
                        void Set(int s)
                        {
                            if (sides[s] == null) return;
                            if (sides[s].propertyType == SerializedPropertyType.Integer) sides[s].intValue = Mathf.RoundToInt(e.newValue);
                            else sides[s].floatValue = e.newValue;
                        }
                        m_Property.serializedObject.Update();
                        Set(index);
                        Event current = Event.current;
                        bool shift = current != null && current.shift, alt = current != null && current.alt;
                        if (alt)
                        {
                            for (int s = 0; s < 4; s++) Set(s);
                        }
                        else if (shift)
                        {
                            Set((index + 2) % 4);
                        }
                        m_Property.serializedObject.ApplyModifiedProperties();
                    });
                    page.Add(LtUI.Row(names[i], field));
                }
                return page;
            });
        }
    }

    /// <summary>
    /// The one list in the house. Rows from an array property: click, Shift and Ctrl select, Delete removes, Ctrl+D copies, Alt with an arrow moves,
    /// a drag from any free place carries the selection, and each act is one undo. Long lists draw thirty rows until asked for more, so that a big one opens quickly.
    /// </summary>
    public sealed class LtList : VisualElement
    {
        const int FirstRows = 30;
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
            header.Add(LtUI.Clickable(addLabel, "lt-add--small", "Adds a row.", () =>
            {
                if (onAdd != null) onAdd(this);
                else AddDefault();
            }));
            Add(header);
            m_Rows = LtUI.El("lt-list__rows");
            Add(m_Rows);
            m_DropMark = LtUI.El("lt-list__drop");
            RegisterCallback<KeyDownEvent>(OnKey);
            RegisterCallback<PointerMoveEvent>(OnDragMove);
            RegisterCallback<PointerUpEvent>(OnDragEnd);
            this.TrackPropertyValue(m_Array, _ => { if (m_Array.arraySize != m_RowElements.Count && !Truncated) Rebuild(); });
            Rebuild();
        }

        bool Truncated => m_Array.arraySize > m_Shown;

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
                row.Add(m_MakeRow(m_Array.GetArrayElementAtIndex(i), i));
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
                if (at is Focusable focus && at.focusable || at.ClassListContains("lt-switch") || at.ClassListContains("lt-seg") || at.ClassListContains("lt-icon-btn"))
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
            menu.ShowAsContext();
        }

        void AddDefault()
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

        void CopySelected()
        {
            var text = new StringBuilder();
            foreach (int row in Selection.Selected)
            {
                SerializedProperty element = m_Array.GetArrayElementAtIndex(row);
                text.Append(element.managedReferenceValue != null ? JsonUtility.ToJson(element.managedReferenceValue) : element.displayName).Append('\n');
            }
            EditorGUIUtility.systemCopyBuffer = text.ToString();
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

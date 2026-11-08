using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace FirstSymbol.Lettera.Editor
{
    /// <summary>
    /// The control of one kind of slot. It shows a token without telling anyone, and calls <see cref="Commit"/> when the person changes it:
    /// a token, or null to give the slot back to inherit.
    /// </summary>
    internal abstract class LtSlotControl
    {
        public VisualElement Root;
        public Action<string> Commit;
        public Action<bool> Hold;

        public abstract void Show(LetteraSlotRow row);

        /// <summary>Whether the person is typing in it, so a refresh leaves it alone.</summary>
        public bool Busy => Root != null && Root.focusController != null && Root.focusController.focusedElement is VisualElement focused && Root.Contains(focused);

        public virtual void Focus() => Root.Query<VisualElement>().Where(e => e.focusable && e.canGrabFocus).First()?.Focus();

        protected static TextField Field(string classes)
        {
            var field = new TextField { isDelayed = false };
            field.AddToClassList("lt-text-input");
            foreach (string c in classes.Split(' '))
                if (c.Length > 0)
                    field.AddToClassList(c);
            return field;
        }

        /// <summary>Enter or leaving the field commits what was typed.</summary>
        protected void OnCommit(TextField field, Action<string> typed)
        {
            field.RegisterCallback<KeyDownEvent>(e =>
            {
                if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter)
                {
                    typed(field.value);
                    e.StopPropagation();
                }
            }, TrickleDown.TrickleDown);
            field.RegisterCallback<FocusOutEvent>(_ => typed(field.value));
        }

        public static string Pretty(string name)
        {
            if (string.IsNullOrEmpty(name)) return name;
            var text = new System.Text.StringBuilder(name.Length + 4);
            for (int i = 0; i < name.Length; i++)
            {
                if (i > 0 && char.IsUpper(name[i]) && char.IsLower(name[i - 1])) text.Append(' ').Append(char.ToLowerInvariant(name[i]));
                else text.Append(i == 0 ? char.ToUpperInvariant(name[i]) : name[i]);
            }
            return text.ToString();
        }
    }

    internal sealed class LtColorSlot : LtSlotControl
    {
        readonly LetteraParamInfo m_Info;
        readonly ColorField m_Swatch;
        readonly TextField m_Hex;
        string m_Shown;

        public LtColorSlot(LetteraParamInfo info)
        {
            m_Info = info;
            m_Swatch = new ColorField { showEyeDropper = true, showAlpha = true };
            m_Swatch.AddToClassList("lt-color__swatch");
            m_Swatch.AddToClassList("lt-slot__swatch");
            m_Swatch.RegisterValueChangedCallback(e =>
            {
                Color32 color = e.newValue;
                string token = LetteraTagWriter.FormatColor(color);
                m_Hex.SetValueWithoutNotify(token);
                m_Shown = token;
                Commit(token);
            });
            m_Hex = Field("lt-color__hex lt-slot__hex");
            OnCommit(m_Hex, Typed);
            Root = LtUI.El("lt-slot__color", m_Swatch, m_Hex);
        }

        void Typed(string typed)
        {
            string text = (typed ?? "").Trim();
            if (text == m_Shown) return;
            if (text.Length == 0)
            {
                Commit(null);
                return;
            }
            if (text[0] != '#' && IsHex(text)) text = "#" + text;
            if (m_Info.Kind == LetteraParamKind.Color && !LetteraSlots.TryColor(text, out _))
            {
                m_Hex.SetValueWithoutNotify(m_Shown ?? "");
                return;
            }
            m_Shown = text;
            Commit(text);
        }

        static bool IsHex(string text)
        {
            if (text.Length != 3 && text.Length != 4 && text.Length != 6 && text.Length != 8) return false;
            foreach (char c in text)
                if (!Uri.IsHexDigit(c))
                    return false;
            return true;
        }

        public override void Show(LetteraSlotRow row)
        {
            m_Shown = row.Token ?? "";
            m_Hex.SetValueWithoutNotify(m_Shown);
            m_Swatch.SetValueWithoutNotify(LetteraSlots.TryColor(m_Shown, out Color32 color) ? (Color)color : new Color(0, 0, 0, 0));
        }
    }

    internal sealed class LtNumberSlot : LtSlotControl
    {
        readonly LetteraParamInfo m_Info;
        readonly Slider m_Slider;
        readonly TextField m_Field;
        string m_Shown = "";

        public LtNumberSlot(LetteraParamInfo info)
        {
            m_Info = info;
            Root = LtUI.El("lt-slot__number");
            if (!float.IsInfinity(info.Min) && !float.IsInfinity(info.Max))
            {
                m_Slider = new Slider(info.Min, info.Max);
                m_Slider.AddToClassList("lt-slot__slider");
                m_Slider.RegisterValueChangedCallback(e =>
                {
                    double value = e.newValue;
                    if (info.Step > 0) value = Math.Round(value / info.Step) * info.Step;
                    string token = LetteraTagWriter.FormatNumber(value, info.Decimals >= 0 ? info.Decimals : 3);
                    if (token == m_Shown) return;
                    m_Shown = token;
                    m_Field.SetValueWithoutNotify(token);
                    Commit(token);
                });
                m_Slider.RegisterCallback<PointerDownEvent>(_ => Hold?.Invoke(true), TrickleDown.TrickleDown);
                m_Slider.RegisterCallback<PointerUpEvent>(_ => Hold?.Invoke(false), TrickleDown.TrickleDown);
                m_Slider.RegisterCallback<PointerCaptureOutEvent>(_ => Hold?.Invoke(false));
                Root.Add(m_Slider);
            }
            m_Field = Field("lt-slot__value");
            OnCommit(m_Field, Typed);
            m_Field.RegisterCallback<KeyDownEvent>(e =>
            {
                if (e.keyCode != KeyCode.UpArrow && e.keyCode != KeyCode.DownArrow) return;
                double step = info.Step > 0 ? info.Step : info.Kind == LetteraParamKind.Int ? 1 : 0.1;
                if (e.shiftKey) step *= 10;
                else if (e.altKey) step *= 0.1;
                LetteraSlotValue.TryNumber(info, m_Shown, out double now);
                Step(now + (e.keyCode == KeyCode.UpArrow ? step : -step));
                e.StopPropagation();
            }, TrickleDown.TrickleDown);
            Root.Add(m_Field);
            if (!string.IsNullOrEmpty(info.UnitLabel))
            {
                Label unit = LtUI.Text(info.UnitLabel, "lt-slot__unit");
                unit.tooltip = info.UnitLabel == "°" ? "Degrees" : info.UnitLabel == "Hz" ? "Cycles a second" : info.UnitLabel;
                Root.Add(unit);
            }
        }

        void Step(double value)
        {
            if (!float.IsInfinity(m_Info.Min)) value = Math.Max(m_Info.Min, value);
            if (!float.IsInfinity(m_Info.Max)) value = Math.Min(m_Info.Max, value);
            Typed(LetteraTagWriter.FormatNumber(value, m_Info.Decimals >= 0 ? m_Info.Decimals : 3));
        }

        void Typed(string typed)
        {
            string text = (typed ?? "").Trim();
            if (text == m_Shown) return;
            if (text.Length == 0)
            {
                Commit(null);
                return;
            }
            if (LetteraSlotValue.Reads(m_Info, text) && (m_Info.Accepts != null || text.EndsWith("%", StringComparison.Ordinal)))
            {
                m_Shown = text;
                Commit(text);
                return;
            }
            LetteraSlotValue.TryNumber(m_Info, m_Shown, out double current);
            if (!LtExpression.TryEvaluate(text, current, out double value))
            {
                m_Field.SetValueWithoutNotify(m_Shown);
                return;
            }
            string token = LetteraTagWriter.FormatNumber(value, m_Info.Decimals >= 0 ? m_Info.Decimals : 4);
            if (token == m_Shown) return;
            m_Shown = token;
            m_Field.SetValueWithoutNotify(token);
            m_Slider?.SetValueWithoutNotify((float)value);
            Commit(token);
        }

        public override void Show(LetteraSlotRow row)
        {
            m_Shown = row.Token ?? "";
            m_Field.SetValueWithoutNotify(m_Shown);
            if (m_Slider != null && LetteraSlotValue.TryNumber(m_Info, m_Shown, out double number)) m_Slider.SetValueWithoutNotify((float)number);
        }
    }

    internal sealed class LtLengthSlot : LtSlotControl
    {
        static readonly LetteraLength.Unit[] s_Order = { LetteraLength.Unit.Bare, LetteraLength.Unit.Pixels, LetteraLength.Unit.Em, LetteraLength.Unit.Percent };

        readonly LetteraParamInfo m_Info;
        readonly Func<float> m_EmPixels;
        readonly TextField m_Field;
        readonly List<LetteraLength.Unit> m_Units = new List<LetteraLength.Unit>();
        readonly Label[] m_Items;
        LetteraLength.Unit m_Unit;
        string m_Shown = "";

        public LtLengthSlot(LetteraParamInfo info, Func<float> emPixels)
        {
            m_Info = info;
            m_EmPixels = emPixels;
            // Bare folds into the unit it means. When it means no unit (a multiplier, local units) it is a chip of its own.
            foreach (LetteraLength.Unit unit in s_Order)
                if (Offers(unit))
                    m_Units.Add(unit);
            // The unit a bare number means leads, so a new value starts where the effect would read it.
            if (m_Units.Remove(info.BareMeans)) m_Units.Insert(0, info.BareMeans);
            m_Unit = m_Units.Count > 0 ? m_Units[0] : LetteraLength.Unit.Bare;
            m_Field = Field("lt-slot__value lt-unit__value");
            OnCommit(m_Field, Typed);
            m_Field.RegisterCallback<KeyDownEvent>(e =>
            {
                if (e.keyCode != KeyCode.UpArrow && e.keyCode != KeyCode.DownArrow) return;
                double step = info.Step > 0 ? info.Step : 0.1;
                if (e.shiftKey) step *= 10;
                else if (e.altKey) step *= 0.1;
                Split(m_Shown, out double now, out _);
                Write(now + (e.keyCode == KeyCode.UpArrow ? step : -step), m_Unit);
                e.StopPropagation();
            }, TrickleDown.TrickleDown);
            var names = new string[m_Units.Count];
            for (int i = 0; i < names.Length; i++) names[i] = NameOf(m_Units[i]);
            VisualElement chips = LtUI.Choice(names, () => m_Units.IndexOf(m_Unit), Pick, out m_Items);
            chips.AddToClassList("lt-slot__units");
            Root = LtUI.El("lt-slot__length", m_Field, chips);
        }

        bool Offers(LetteraLength.Unit unit)
        {
            LetteraSlotUnits units = m_Info.Units;
            switch (unit)
            {
                case LetteraLength.Unit.Pixels: return (units & LetteraSlotUnits.Px) != 0 || ((units & LetteraSlotUnits.Bare) != 0 && m_Info.BareMeans == LetteraLength.Unit.Pixels);
                case LetteraLength.Unit.Em: return (units & LetteraSlotUnits.Em) != 0 || ((units & LetteraSlotUnits.Bare) != 0 && m_Info.BareMeans == LetteraLength.Unit.Em);
                case LetteraLength.Unit.Percent: return (units & LetteraSlotUnits.Percent) != 0;
                default: return (units & LetteraSlotUnits.Bare) != 0 && m_Info.BareMeans == LetteraLength.Unit.Bare;
            }
        }

        string NameOf(LetteraLength.Unit unit) => unit == LetteraLength.Unit.Pixels ? "px" : unit == LetteraLength.Unit.Em ? "em" : unit == LetteraLength.Unit.Percent ? "%" : LetteraSlotValue.BareName(m_Info);

        // The suffix a unit is written with: none for the one a bare number already means.
        string SuffixOf(LetteraLength.Unit unit)
        {
            if (unit == LetteraLength.Unit.Bare) return "";
            bool bare = (m_Info.Units & LetteraSlotUnits.Bare) != 0;
            if (unit == m_Info.BareMeans && bare) return "";
            return unit == LetteraLength.Unit.Pixels ? "px" : unit == LetteraLength.Unit.Em ? "em" : "%";
        }

        // A token as a number, as typed, and the unit it is in.
        void Split(string token, out double value, out LetteraLength.Unit unit)
        {
            value = 0;
            unit = m_Unit;
            if (!LetteraLength.TryParse(token, out LetteraLength length)) return;
            value = length.Value;
            unit = length.Kind == LetteraLength.Unit.Bare && m_Info.BareMeans != LetteraLength.Unit.Bare ? m_Info.BareMeans : length.Kind;
            if (unit == LetteraLength.Unit.Bare && !m_Units.Contains(unit)) unit = m_Unit;
        }

        static int Index(LetteraLength.Unit unit) => unit == LetteraLength.Unit.Pixels ? 0 : unit == LetteraLength.Unit.Em ? 1 : 2;

        void Pick(int index)
        {
            LetteraLength.Unit to = m_Units[index];
            if (to == m_Unit) return;
            Split(m_Shown, out double value, out LetteraLength.Unit from);
            if (string.IsNullOrEmpty(m_Shown)) value = 0;
            bool convertible = from != LetteraLength.Unit.Bare && to != LetteraLength.Unit.Bare;
            double converted = convertible ? LtUnit.Convert(value, Index(from), Index(to), m_EmPixels != null ? m_EmPixels() : 0) : value;
            Write(converted, to);
        }

        void Typed(string typed)
        {
            string text = (typed ?? "").Trim();
            if (text.Length == 0)
            {
                if (m_Shown.Length > 0) Commit(null);
                return;
            }
            // A word the effect reads in place of a length, such as auto, is taken as written.
            if (m_Info.Accepts != null && m_Info.Accepts(text))
            {
                if (text != m_Shown)
                {
                    m_Shown = text;
                    Commit(text);
                }
                return;
            }
            // A token with its own suffix is taken as written, when this slot reads it.
            if (LetteraLength.TryParse(text, out LetteraLength length) && (text.EndsWith("px", StringComparison.OrdinalIgnoreCase) || text.EndsWith("em", StringComparison.OrdinalIgnoreCase) || text.EndsWith("%", StringComparison.Ordinal)))
            {
                if (!LetteraSlotValue.Reads(m_Info, text))
                {
                    m_Field.SetValueWithoutNotify(Number(m_Shown));
                    return;
                }
                if (text != m_Shown)
                {
                    m_Shown = text;
                    Commit(text);
                }
                return;
            }
            Split(m_Shown, out double current, out _);
            if (!LtExpression.TryEvaluate(text, current, out double value))
            {
                m_Field.SetValueWithoutNotify(Number(m_Shown));
                return;
            }
            // A sign typed in front of a size stays: +10 adds, 10 sets.
            string token = (text[0] == '+' && (m_Info.Units & LetteraSlotUnits.Delta) != 0 ? "+" : "") + LetteraTagWriter.FormatNumber(value, m_Info.Decimals >= 0 ? m_Info.Decimals : 4) + SuffixOf(m_Unit);
            if (token == m_Shown) return;
            m_Shown = token;
            Commit(token);
        }

        void Write(double value, LetteraLength.Unit unit)
        {
            m_Unit = unit;
            string token = LetteraTagWriter.FormatNumber(value, m_Info.Decimals >= 0 ? m_Info.Decimals : 4) + SuffixOf(unit);
            m_Shown = token;
            m_Field.SetValueWithoutNotify(Number(token));
            Paint();
            Commit(token);
        }

        string Number(string token)
        {
            if (string.IsNullOrEmpty(token)) return "";
            if (token.EndsWith("px", StringComparison.OrdinalIgnoreCase) || token.EndsWith("em", StringComparison.OrdinalIgnoreCase)) return token.Substring(0, token.Length - 2);
            if (token.EndsWith("%", StringComparison.Ordinal)) return token.Substring(0, token.Length - 1);
            return token;
        }

        void Paint()
        {
            for (int i = 0; i < m_Items.Length; i++) m_Items[i].EnableInClassList("lt-seg__item--on", m_Units[i] == m_Unit);
        }

        public override void Show(LetteraSlotRow row)
        {
            m_Shown = row.Token ?? "";
            if (LetteraLength.TryParse(m_Shown, out _))
            {
                Split(m_Shown, out _, out LetteraLength.Unit unit);
                m_Unit = unit;
            }
            m_Field.SetValueWithoutNotify(Number(m_Shown));
            Paint();
        }
    }

    internal sealed class LtChoiceSlot : LtSlotControl
    {
        readonly LetteraParamInfo m_Info;
        readonly Label[] m_Items;
        readonly Label m_Raw;
        LtPick m_Pick;
        string m_Shown = "";

        public LtChoiceSlot(LetteraParamInfo info)
        {
            m_Info = info;
            string[] labels = LabelsOf(info);
            if (labels.Length <= 4)
            {
                VisualElement seg = LtUI.Choice(labels, () => LetteraSlotValue.ChoiceOf(info, m_Shown), index => Commit(info.Choices[index]), out m_Items);
                seg.AddToClassList("lt-slot__seg");
                m_Raw = LtUI.Text("", "lt-slot__raw");
                Root = LtUI.El("lt-slot__choice", seg, m_Raw);
            }
            else
            {
                m_Pick = new LtPick("", () => Shown(), () =>
                {
                    var items = new List<(string, string, object)>();
                    for (int i = 0; i < labels.Length; i++) items.Add((labels[i], info.Choices[i], info.Choices[i]));
                    return items;
                }, chosen => Commit((string)chosen), info.Hint);
                Root = LtUI.El("lt-slot__choice", m_Pick);
            }
        }

        static string[] LabelsOf(LetteraParamInfo info)
        {
            if (info.ChoiceLabels != null && info.ChoiceLabels.Length == info.Choices.Length) return info.ChoiceLabels;
            var labels = new string[info.Choices.Length];
            for (int i = 0; i < labels.Length; i++) labels[i] = Pretty(info.Choices[i]);
            return labels;
        }

        string Shown()
        {
            int index = LetteraSlotValue.ChoiceOf(m_Info, m_Shown);
            return index >= 0 ? LabelsOf(m_Info)[index] : m_Shown;
        }

        public override void Show(LetteraSlotRow row)
        {
            m_Shown = row.Token ?? "";
            if (m_Items != null)
            {
                int on = LetteraSlotValue.ChoiceOf(m_Info, m_Shown);
                for (int i = 0; i < m_Items.Length; i++) m_Items[i].EnableInClassList("lt-seg__item--on", i == on);
                m_Raw.text = on < 0 ? m_Shown : "";
                m_Raw.style.display = on < 0 && m_Shown.Length > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            }
            m_Pick?.Refresh();
        }
    }

    internal sealed class LtBoolSlot : LtSlotControl
    {
        readonly VisualElement m_Track;
        readonly Label m_Word;
        string m_Shown = "";
        bool m_On;

        public LtBoolSlot()
        {
            m_Track = LtUI.El("lt-switch", LtUI.El("lt-switch__knob"));
            m_Track.focusable = true;
            m_Track.RegisterCallback<ClickEvent>(_ => Flip());
            m_Track.RegisterCallback<KeyDownEvent>(e =>
            {
                if (e.keyCode != KeyCode.Space && e.keyCode != KeyCode.Return && e.keyCode != KeyCode.KeypadEnter) return;
                Flip();
                e.StopPropagation();
            });
            m_Word = LtUI.Text("", "lt-slot__raw");
            Root = LtUI.El("lt-slot__bool", m_Track, m_Word);
        }

        // The words the tag already uses stay: on and off, or 1 and 0, else true and false.
        void Flip()
        {
            bool on = !m_On;
            string token = on ? "true" : "false";
            if (m_Shown.Equals("on", StringComparison.OrdinalIgnoreCase) || m_Shown.Equals("off", StringComparison.OrdinalIgnoreCase)) token = on ? "on" : "off";
            else if (m_Shown == "1" || m_Shown == "0") token = on ? "1" : "0";
            Commit(token);
        }

        public override void Show(LetteraSlotRow row)
        {
            m_Shown = row.Token ?? "";
            if (!LetteraSlotValue.ReadsBool(m_Shown, out m_On) && LetteraSlotValue.ReadsBool(row.Inherited, out bool inherited)) m_On = inherited;
            m_Track.EnableInClassList("lt-switch--on", m_On);
            m_Word.text = m_On ? "on" : "off";
        }
    }

    internal sealed class LtTextSlot : LtSlotControl
    {
        readonly TextField m_Field;
        string m_Shown = "";

        // With a list of names to offer, a button beside the field opens it.
        public LtTextSlot(Func<IReadOnlyList<string>> names = null)
        {
            m_Field = Field("lt-slot__text");
            m_Field.isDelayed = true;
            m_Field.RegisterValueChangedCallback(e =>
            {
                string text = e.newValue ?? "";
                if (text == m_Shown) return;
                m_Shown = text;
                Commit(text.Length == 0 ? null : text);
            });
            Root = LtUI.El("lt-slot__textbox", m_Field);
            if (names != null)
            {
                Label pick = LtUI.Clickable("\u25be", "lt-chip lt-chip--click lt-slot__pick", "Pick a name from the ones this text knows.", () =>
                {
                    IReadOnlyList<string> list = names();
                    var menu = new GenericMenu();
                    if (list == null || list.Count == 0) menu.AddDisabledItem(new GUIContent("No names are known yet"));
                    else
                        foreach (string name in list)
                        {
                            string chosen = name;
                            menu.AddItem(new GUIContent(chosen), chosen == m_Shown, () =>
                            {
                                m_Shown = chosen;
                                m_Field.SetValueWithoutNotify(chosen);
                                Commit(chosen);
                            });
                        }
                    menu.ShowAsContext();
                });
                Root.Add(pick);
            }
        }

        public override void Show(LetteraSlotRow row)
        {
            m_Shown = row.Token ?? "";
            m_Field.SetValueWithoutNotify(m_Shown);
        }
    }

    /// <summary>
    /// One slot as a row: its number, its name, the control for its kind, and whether it is set. An empty slot shows what will apply instead of a
    /// control, and a click on it sets the slot to that value so nothing jumps.
    /// </summary>
    internal sealed class LtSlotRow : VisualElement
    {
        readonly Func<float> m_EmPixels;
        readonly Label m_Inherited, m_Set, m_Reset, m_Note, m_Help;
        readonly VisualElement m_Line, m_Box;
        readonly LtSlotControl m_Control;
        LetteraSlotRow m_Model;
        bool m_Opened;

        public LetteraParamInfo Info { get; }
        public LetteraSlotRow Model => m_Model;
        public bool Editing => m_Control.Busy;

        /// <summary>The person changed the slot: a token, or null for inherit.</summary>
        public event Action<LtSlotRow, string> Changed;
        public event Action<bool> Holding;

        public LtSlotRow(LetteraParamInfo info, Func<float> emPixels, Func<LetteraParamInfo, IReadOnlyList<string>> suggest = null)
        {
            Info = info;
            m_EmPixels = emPixels;
            AddToClassList("lt-slot");
            focusable = false;
            m_Control = Make(info, emPixels, suggest);
            m_Control.Commit = token => Changed?.Invoke(this, token);
            m_Control.Hold = hold => Holding?.Invoke(hold);

            Label badge = LtUI.Text((info.Slot + 1).ToString(CultureInfo.InvariantCulture), "lt-slot__badge");
            Label name = LtUI.Text(info.Display, "lt-slot__name");
            name.tooltip = Tip(info);
            badge.tooltip = name.tooltip;
            m_Inherited = LtUI.Clickable("", "lt-slot__inherited", "Click to set this slot to the value it has now.", Open);
            m_Box = LtUI.El("lt-slot__box", m_Inherited, m_Control.Root);
            m_Set = LtUI.Clickable("Set", "lt-slot__set", "Set this slot, or give it back to inherit.", Toggle);
            m_Set.focusable = true;
            m_Set.RegisterCallback<KeyDownEvent>(e =>
            {
                if (e.keyCode != KeyCode.Space && e.keyCode != KeyCode.Return && e.keyCode != KeyCode.KeypadEnter) return;
                Toggle();
                e.StopPropagation();
            });
            m_Reset = LtUI.Clickable("↺", "lt-slot__reset", "Reset to inherit (Alt+Backspace).", () => Changed?.Invoke(this, null));
            m_Line = LtUI.El("lt-slot__line", badge, name, m_Box, m_Set, m_Reset);
            m_Note = LtUI.Text("", "lt-slot__note");
            m_Note.style.display = DisplayStyle.None;
            // What the slot means is written out, not left to a tooltip.
            m_Help = LtUI.Text(LetteraSlotValue.Help(info), "lt-slot__help");
            Add(m_Line);
            Add(m_Help);
            Add(m_Note);

            RegisterCallback<FocusInEvent>(_ => AddToClassList("lt-slot--focus"));
            RegisterCallback<FocusOutEvent>(_ => RemoveFromClassList("lt-slot--focus"));
            RegisterCallback<KeyDownEvent>(e =>
            {
                if (e.keyCode == KeyCode.Backspace && e.altKey)
                {
                    Changed?.Invoke(this, null);
                    e.StopPropagation();
                }
                else if (e.keyCode == KeyCode.Escape && m_Model != null)
                {
                    m_Control.Show(m_Model);
                    e.StopPropagation();
                }
            }, TrickleDown.TrickleDown);
        }

        static LtSlotControl Make(LetteraParamInfo info, Func<float> emPixels, Func<LetteraParamInfo, IReadOnlyList<string>> suggest)
        {
            switch (info.Kind)
            {
                case LetteraParamKind.Color:
                case LetteraParamKind.Paint: return new LtColorSlot(info);
                case LetteraParamKind.Length: return new LtLengthSlot(info, emPixels);
                case LetteraParamKind.Float:
                case LetteraParamKind.Int: return new LtNumberSlot(info);
                case LetteraParamKind.Bool: return new LtBoolSlot();
                case LetteraParamKind.Enum:
                case LetteraParamKind.Choice: return info.Choices != null && info.Choices.Length > 0 ? (LtSlotControl)new LtChoiceSlot(info) : new LtTextSlot();
                default: return !string.IsNullOrEmpty(info.Suggests) && suggest != null ? new LtTextSlot(() => suggest(info)) : new LtTextSlot();
            }
        }

        static string Tip(LetteraParamInfo info)
        {
            string text = info.Hint ?? info.Display;
            text += "\n" + LetteraSlotValue.Describe(info);
            if (info.Choices != null && info.Kind == LetteraParamKind.Choice) text += ": " + string.Join(", ", info.Choices);
            return text;
        }

        // The click on "inherited": set the slot to what applies.
        void Open()
        {
            if (m_Model == null) return;
            string value = m_Model.Inherited;
            if (string.IsNullOrEmpty(value) || !LetteraSlotValue.Reads(Info, value))
            {
                if (Info.Kind == LetteraParamKind.String || Info.Kind == LetteraParamKind.Paint)
                {
                    m_Opened = true;
                    Bind(m_Model);
                    m_Control.Focus();
                    return;
                }
                value = LetteraSlotValue.Sample(Info);
            }
            Changed?.Invoke(this, value);
        }

        void Toggle()
        {
            if (m_Model == null) return;
            if (m_Model.State == LetteraSlotState.Inherit && !m_Opened) Open();
            else
            {
                m_Opened = false;
                Changed?.Invoke(this, null);
            }
        }

        /// <summary>Shows a slot. A control the person is typing in is left alone.</summary>
        public void Bind(LetteraSlotRow model)
        {
            m_Model = model;
            bool inherit = model.State == LetteraSlotState.Inherit && !m_Opened;
            if (model.State != LetteraSlotState.Inherit) m_Opened = false;
            m_Inherited.style.display = inherit ? DisplayStyle.Flex : DisplayStyle.None;
            m_Control.Root.style.display = inherit ? DisplayStyle.None : DisplayStyle.Flex;
            m_Inherited.text = InheritedText(model);
            m_Set.EnableInClassList("lt-slot__set--on", !inherit);
            m_Reset.style.visibility = inherit ? Visibility.Hidden : Visibility.Visible;
            EnableInClassList("lt-slot--inherit", inherit);
            EnableInClassList("lt-slot--invalid", model.State == LetteraSlotState.Invalid);
            EnableInClassList("lt-slot--clamped", model.State == LetteraSlotState.OutOfRange);
            string note = null;
            if (model.State == LetteraSlotState.Invalid) note = model.Note ?? "Not read: the effect's value is used. Written as: " + model.Token;
            else if (model.State == LetteraSlotState.OutOfRange) note = "Clamped to " + LetteraTagWriter.FormatNumber(Info.Min) + ".." + LetteraTagWriter.FormatNumber(Info.Max) + " when drawn.";
            m_Note.text = note ?? "";
            m_Note.style.display = note != null ? DisplayStyle.Flex : DisplayStyle.None;
            m_Note.EnableInClassList("lt-slot__note--warn", model.State == LetteraSlotState.OutOfRange);
            if (!inherit && !m_Control.Busy) m_Control.Show(model);
        }

        string InheritedText(LetteraSlotRow model)
        {
            string value = model.Inherited;
            if (string.IsNullOrEmpty(value)) return "not set";
            if (Info.Kind == LetteraParamKind.Length && LetteraLength.TryParse(value, out LetteraLength length) && length.Kind == LetteraLength.Unit.Bare)
                value += " " + LetteraSlotValue.BareName(Info);
            else if (Info.Kind == LetteraParamKind.Enum || Info.Kind == LetteraParamKind.Choice)
            {
                int index = LetteraSlotValue.ChoiceOf(Info, value);
                if (index >= 0) value = Info.ChoiceLabels != null && index < Info.ChoiceLabels.Length ? Info.ChoiceLabels[index] : LtSlotControl.Pretty(Info.Choices[index]);
            }
            return "inherited: " + value + (string.IsNullOrEmpty(model.InheritedFrom) ? "" : "  (" + model.InheritedFrom + ")");
        }

        public void FocusControl()
        {
            if (m_Model != null && m_Model.State == LetteraSlotState.Inherit && !m_Opened) m_Set.Focus();
            else m_Control.Focus();
        }
    }
}

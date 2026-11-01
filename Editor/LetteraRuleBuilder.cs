using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace FirstSymbol.Lettera.Editor
{
    /// <summary>A range rule as sentences: when this holds, those parameters go there. Chips for the plain conditions, and pickers that know what can be changed.</summary>
    internal static class LetteraRuleBuilder
    {
        static readonly (string word, string signal)[] s_Signals =
        {
            ("Hover", LetteraSignals.Hovered), ("Press", LetteraSignals.Pressed), ("Focus", LetteraSignals.Focused), ("Disabled", LetteraSignals.Disabled),
        };

        public static VisualElement Build(SerializedProperty rule)
        {
            SerializedProperty bound = rule.Copy();
            VisualElement root = LtUI.El("lt-group lt-rule");
            root.Add(Condition(bound.FindPropertyRelative("When")));
            SerializedProperty envelope = bound.FindPropertyRelative("Envelope");
            root.Add(LtUI.Field("Comes In", envelope, "Instant, over a time, or following a signal's strength."));
            VisualElement times = LtUI.El("lt-row lt-row--pair", LtUI.Field("Enter", bound.FindPropertyRelative("Enter"), "Seconds going on."), LtUI.Field("Exit", bound.FindPropertyRelative("Exit"), "Seconds going off."));
            LetteraTextInspector.ShowWhen(times, envelope, p => p.enumValueIndex == (int)LetteraEnvelopeKind.Transition);
            root.Add(times);
            root.Add(LtUI.Field("Ease", bound.FindPropertyRelative("Ease")));
            root.Add(LtUI.Field("Clock", bound.FindPropertyRelative("Clock")));
            root.Add(LtUI.El("lt-row lt-row--pair", LtUI.Field("Priority", bound.FindPropertyRelative("Priority")), LtUI.InlineSwitch("On Activate", bound.FindPropertyRelative("TriggerOnActivate"), "Runs once when the range is activated, whatever the condition says.")));
            root.Add(new LtList("Changes", bound.FindPropertyRelative("Changes"), (change, index) => ChangeRow(bound, change), "Nothing changes yet.", list =>
            {
                SerializedProperty changes = bound.FindPropertyRelative("Changes");
                bound.serializedObject.Update();
                int at = changes.arraySize;
                changes.InsertArrayElementAtIndex(at);
                changes.GetArrayElementAtIndex(at).managedReferenceValue = new LetteraRangeChange();
                bound.serializedObject.ApplyModifiedProperties();
                list.Rebuild();
            }));
            return root;
        }

        /// <summary>Chips for one signal. Any other condition keeps its own fields.</summary>
        static VisualElement Condition(SerializedProperty when)
        {
            SerializedProperty bound = when.Copy();
            VisualElement holder = LtUI.El("lt-rule__when");
            void Show()
            {
                holder.Clear();
                var row = LtUI.El("lt-chips");
                row.Add(LtUI.Text("When", "lt-row__label"));
                string current = (bound.managedReferenceValue as SignalIs)?.Signal;
                foreach ((string word, string signal) in s_Signals)
                {
                    Label chip = LtUI.Clickable(word, "lt-chip lt-chip--click", "This rule holds while the range is in this state.", () =>
                    {
                        bound.serializedObject.Update();
                        bound.managedReferenceValue = new SignalIs(signal);
                        bound.serializedObject.ApplyModifiedProperties();
                        Show();
                    });
                    chip.EnableInClassList("lt-chip--accent", current == signal);
                    row.Add(chip);
                }
                holder.Add(row);
                if (!(bound.managedReferenceValue is SignalIs s) || System.Array.FindIndex(s_Signals, x => x.signal == s.Signal) < 0)
                    holder.Add(LetteraPaintField.ManagedReference(bound));
            }
            Show();
            return holder;
        }

        // The effects a rule may reach: those of the same style list, found from where the rule stands in it.
        internal static List<(string name, LetteraEffect effect)> EffectsBeside(SerializedProperty rule)
        {
            var found = new List<(string, LetteraEffect)>();
            string path = rule.propertyPath;
            int cut = path.IndexOf("Styles.Array.data[", System.StringComparison.Ordinal);
            if (cut < 0) cut = path.IndexOf("m_Styles.Array.data[", System.StringComparison.Ordinal);
            if (cut < 0) return found;
            int end = path.IndexOf(".Array.data[", cut, System.StringComparison.Ordinal);
            SerializedProperty styles = rule.serializedObject.FindProperty(path.Substring(0, end));
            if (styles == null || !styles.isArray) return found;
            for (int i = 0; i < styles.arraySize; i++)
            {
                if (styles.GetArrayElementAtIndex(i).FindPropertyRelative("m_Effect")?.managedReferenceValue is LetteraEffect effect)
                    found.Add(((i + 1) + ". " + LetteraStyleList.Pretty(effect.GetType().Name, "Effect"), effect));
            }
            return found;
        }

        static VisualElement ChangeRow(SerializedProperty rule, SerializedProperty change)
        {
            SerializedProperty target = change.FindPropertyRelative("Target").Copy();
            SerializedProperty parameter = change.FindPropertyRelative("Parameter").Copy();
            SerializedProperty isColor = change.FindPropertyRelative("IsColor").Copy();
            VisualElement body = LtUI.El("lt-list");
            VisualElement toValue = LtUI.El("lt-list");
            void ShowValue()
            {
                toValue.Clear();
                toValue.Add(isColor.boolValue ? LtUI.Field("To", change.FindPropertyRelative("ToColor")) : LtUI.Field("To", change.FindPropertyRelative("ToNumber")));
            }
            LtPick parameterPick = null;
            body.Add(new LtPick("Effect", () => target.managedReferenceValue is LetteraEffect e ? LetteraStyleList.Pretty(e.GetType().Name, "Effect") : "None: this change does nothing", () =>
            {
                var items = new List<(string, string, object)>();
                foreach ((string name, LetteraEffect effect) in EffectsBeside(rule)) items.Add((name, null, effect));
                return items;
            }, chosen =>
            {
                target.serializedObject.Update();
                target.managedReferenceValue = chosen;
                target.serializedObject.ApplyModifiedProperties();
                parameterPick?.Refresh();
            }, "An effect in the same list as this rule."));
            parameterPick = new LtPick("Parameter", () => string.IsNullOrEmpty(parameter.stringValue) ? "Choose" : parameter.stringValue, () =>
            {
                var items = new List<(string, string, object)>();
                if (target.managedReferenceValue is LetteraEffect effect)
                    foreach (LetteraParamInfo info in effect.Parameters)
                        if (info.Kind == LetteraParamKind.Float || info.Kind == LetteraParamKind.Color)
                            items.Add((info.Display, info.Kind == LetteraParamKind.Color ? "color" : "number", info));
                return items;
            }, chosen =>
            {
                var info = (LetteraParamInfo)chosen;
                parameter.serializedObject.Update();
                parameter.stringValue = info.Id;
                isColor.boolValue = info.Kind == LetteraParamKind.Color;
                parameter.serializedObject.ApplyModifiedProperties();
                ShowValue();
            });
            body.Add(parameterPick);
            ShowValue();
            body.Add(toValue);
            body.Add(LtUI.Field("Mix", change.FindPropertyRelative("Mix")));
            return body;
        }
    }
}

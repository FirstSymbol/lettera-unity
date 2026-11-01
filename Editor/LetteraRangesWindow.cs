using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.ShortcutManagement;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace FirstSymbol.Lettera.Editor
{
    /// <summary>The ranges of a text, what each does, and what is wrong with them.</summary>
    internal sealed class LetteraRangesWindow : LetteraToolWindow
    {
        [MenuItem("Tools/Lettera/Ranges", false, 21)]
        static void Open() => GetWindow<LetteraRangesWindow>("Lettera Ranges").minSize = new Vector2(320, 300);

        protected override string Title => "Ranges";
        protected override string Subtitle => "What the markup became";

        VisualElement m_List, m_Problems;
        Label m_Target;

        protected override void Build()
        {
            m_Target = LtUI.Text("", "lt-hint");
            Page.Add(LtUI.Card("Target", PinToggle(), m_Target, LtUI.Clickable("Refresh", "lt-add lt-add--small", "Reads the selected text again.", Refresh)));
            m_Problems = LtUI.El("lt-problems");
            Page.Add(LtUI.Card("Problems", m_Problems));
            m_List = LtUI.El("lt-ranges");
            Page.Add(LtUI.Card("Ranges", m_List));
            Refresh();
        }

        protected override void SelectionChanged() => Refresh();

        void Refresh()
        {
            if (m_List == null) return;
            m_List.Clear();
            m_Problems.Clear();
            TextEngine engine = EngineOf(Selection.activeGameObject, out Component component);
            if (engine == null)
            {
                m_Target.text = "Select a text to see its ranges.";
                return;
            }
            m_Target.text = component.name;
            engine.EnsureParsed();
            foreach (string problem in LetteraRangeProblems.Of(engine)) m_Problems.Add(new LtFix(problem, null));
            if (m_Problems.childCount == 0) m_Problems.Add(LtUI.Text("None found.", "lt-hint"));
            IReadOnlyList<LetteraMatchContext.RangeRecord> ranges = engine.ParsedRanges;
            for (int i = 0; i < ranges.Count; i++)
            {
                LetteraMatchContext.RangeRecord range = ranges[i];
                LetteraStyle style = engine.EffectiveStyles[range.StyleIndex];
                string effect = style?.Effect != null ? style.Effect.GetType().Name.Replace("Effect", "") : "no effect";
                string tag = style?.Matcher is TagMatcher t ? "<" + t.Name + ">" : style?.Matcher?.GetType().Name ?? "";
                var row = LtUI.El("lt-range");
                row.Add(LtUI.Text($"{tag}  {effect}", "lt-row__value"));
                string slots = range.Slots != null && range.Slots.Length > 0 ? string.Join(", ", range.Slots.Where(x => x != null)) : "";
                row.Add(LtUI.Text($"{range.RenderedStart} to {range.RenderedEnd}{(range.WholeText ? ", whole text" : "")}{(slots.Length > 0 ? ", " + slots : "")}", "lt-hint"));
                m_List.Add(row);
            }
            if (ranges.Count == 0) m_List.Add(LtUI.Text("This text has no ranges.", "lt-hint"));
        }
    }
}
